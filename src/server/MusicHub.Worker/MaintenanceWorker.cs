using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MusicHub.Domain;
using MusicHub.Infrastructure;
using MusicHub.Infrastructure.Maintenance;
using MusicHub.Infrastructure.Storage;

namespace MusicHub.Worker;

/// <summary>
/// Background maintenance loop: worker heartbeat, queued job execution, outbox drain,
/// and bounded retention pruning.
/// </summary>
/// <remarks>
/// Job claiming goes through <see cref="BackgroundJobClaimer"/>, a single conditional UPDATE, so
/// two worker instances cannot both execute the same job (CR-008). The advertised "database lock
/// row" never existed, so the claim itself has to be the concurrency control.
/// </remarks>
public sealed class MaintenanceWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<MaintenanceWorker> logger) : BackgroundService
{
    private const string HeartbeatKey = "worker.heartbeat";
    private DateTimeOffset _lastPrune = DateTimeOffset.MinValue;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Maintenance worker started.");
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                await HeartbeatAsync(scope.ServiceProvider.GetRequiredService<MusicHubDbContext>(), stoppingToken);
                await ProcessJobAsync(scope.ServiceProvider, stoppingToken);
                await DrainOutboxAsync(scope.ServiceProvider.GetRequiredService<MusicHubDbContext>(), stoppingToken);

                if (DateTimeOffset.UtcNow - _lastPrune > TimeSpan.FromHours(1))
                {
                    await PruneAsync(scope.ServiceProvider.GetRequiredService<MusicHubDbContext>(), stoppingToken);
                    _lastPrune = DateTimeOffset.UtcNow;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Maintenance iteration failed.");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
        logger.LogInformation("Maintenance worker stopped.");
    }

    private static async Task HeartbeatAsync(MusicHubDbContext db, CancellationToken ct)
    {
        var value = DateTimeOffset.UtcNow.ToString("O");
        var row = await db.AppSettings.SingleOrDefaultAsync(x => x.Key == HeartbeatKey, ct);
        if (row is null)
        {
            db.AppSettings.Add(new AppSetting { Key = HeartbeatKey, Value = value, UpdatedAt = DateTimeOffset.UtcNow });
        }
        else
        {
            row.Value = value;
            row.UpdatedAt = DateTimeOffset.UtcNow;
        }
        await db.SaveChangesAsync(ct);
    }

    private sealed record RelocationPayload(string? Target);

    private async Task ProcessJobAsync(IServiceProvider services, CancellationToken ct)
    {
        var db = services.GetRequiredService<MusicHubDbContext>();

        // The claim is a single conditional UPDATE, so two worker instances cannot both execute
        // the same job (CR-008).
        var claim = await BackgroundJobClaimer.ClaimNextAsync(db, DateTimeOffset.UtcNow, TimeSpan.FromMinutes(10), ct);
        if (claim is null) return;

        var job = await db.BackgroundJobs.SingleOrDefaultAsync(x => x.Id == claim.JobId, ct);
        if (job is null) return;

        try
        {
            switch (job.Type)
            {
                case "integrity.scan":
                {
                    var integrity = services.GetRequiredService<StorageIntegrityService>();
                    var checkId = await integrity.RunAsync(ct);
                    job.PayloadJson = JsonSerializer.Serialize(new { checkId });
                    break;
                }
                case LibraryRelocationService.JobType:
                {
                    // The payload is a database row, so it is untrusted: the service validates the
                    // target again before it copies anything.
                    var target = JsonSerializer.Deserialize<RelocationPayload>(job.PayloadJson)?.Target;
                    if (string.IsNullOrWhiteSpace(target))
                    {
                        throw new InvalidOperationException("The relocation job has no target folder.");
                    }
                    var relocation = services.GetRequiredService<LibraryRelocationService>();
                    var result = await relocation.RunAsync(job.Id, target, ct);
                    job.PayloadJson = JsonSerializer.Serialize(result);
                    break;
                }
                case "noop":
                    break;
                default:
                    throw new InvalidOperationException($"Unknown job type '{job.Type}'.");
            }

            job.Status = JobStatus.Completed;
            job.LastError = null;
            job.CompletedAt = DateTimeOffset.UtcNow;
            job.LockedUntil = null;
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Background job {JobId} ({Type}) completed.", job.Id, job.Type);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            job.LastError = ex.Message.Length > 2000 ? ex.Message[..2000] : ex.Message;
            job.LockedUntil = null;
            if (job.Attempts >= job.MaxAttempts)
            {
                job.Status = JobStatus.Failed;
                job.CompletedAt = DateTimeOffset.UtcNow;
                db.FailedJobs.Add(new FailedJob
                {
                    BackgroundJobId = job.Id,
                    Type = job.Type,
                    PayloadJson = job.PayloadJson,
                    Error = job.LastError,
                    FailedAt = DateTimeOffset.UtcNow
                });
            }
            else
            {
                job.Status = JobStatus.Pending;
                job.AvailableAt = DateTimeOffset.UtcNow.AddMinutes(Math.Pow(2, job.Attempts));
            }
            await db.SaveChangesAsync(CancellationToken.None);
            logger.LogWarning(ex, "Background job {JobId} ({Type}) failed on attempt {Attempts}.", job.Id, job.Type, job.Attempts);
        }
    }

    /// <summary>
    /// Drains pending outbox messages. A message that fails is rescheduled with exponential
    /// backoff instead of being marked processed, and after the attempt budget is spent it is
    /// marked processed with <see cref="OutboxMessage.Error"/> set so it stops being retried and
    /// pruning can collect it. The columns existed but were never written, so the outbox silently
    /// dropped anything that failed on the first attempt (CR-025).
    /// </summary>
    private static async Task DrainOutboxAsync(MusicHubDbContext db, CancellationToken ct)
    {
        const int maxAttempts = 5;
        var now = DateTimeOffset.UtcNow;
        var batch = await db.OutboxMessages
            .Where(x => x.ProcessedAt == null && (x.NextAttemptAt == null || x.NextAttemptAt <= now))
            .OrderBy(x => x.CreatedAt)
            .Take(25)
            .ToListAsync(ct);
        if (batch.Count == 0) return;

        foreach (var message in batch)
        {
            message.Attempts++;
            try
            {
                if (message.Type == "TrackAdded")
                {
                    db.SystemEvents.Add(new SystemEvent
                    {
                        Level = "Information",
                        Category = "Library",
                        Message = "A track was added to the library.",
                        DataJson = message.PayloadJson
                    });
                }
                message.ProcessedAt = DateTimeOffset.UtcNow;
                message.Error = null;
                message.NextAttemptAt = null;
            }
            catch (Exception ex)
            {
                message.Error = ex.Message.Length > 2000 ? ex.Message[..2000] : ex.Message;
                if (message.Attempts >= maxAttempts)
                {
                    // Dead-letter: keep the row (and its error) but stop retrying it.
                    message.ProcessedAt = DateTimeOffset.UtcNow;
                    message.NextAttemptAt = null;
                }
                else
                {
                    message.ProcessedAt = null;
                    message.NextAttemptAt = DateTimeOffset.UtcNow.AddSeconds(Math.Pow(2, message.Attempts) * 30);
                }
            }
        }
        await db.SaveChangesAsync(ct);
    }

    private static async Task PruneAsync(MusicHubDbContext db, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        await db.RefreshTokens.Where(x => x.ExpiresAt < now.AddDays(-30) || (x.RevokedAt != null && x.RevokedAt < now.AddDays(-30)))
            .ExecuteDeleteAsync(ct);
        await db.PairingSessions.Where(x => x.ExpiresAt < now.AddDays(-7)).ExecuteDeleteAsync(ct);
        await db.LoginHistory.Where(x => x.CreatedAt < now.AddDays(-180)).ExecuteDeleteAsync(ct);
        await db.TelegramUpdates.Where(x => x.ProcessedAt != null && x.ReceivedAt < now.AddDays(-30)).ExecuteDeleteAsync(ct);
        await db.SystemEvents.Where(x => x.CreatedAt < now.AddDays(-90)).ExecuteDeleteAsync(ct);
        await db.OutboxMessages.Where(x => x.ProcessedAt != null && x.ProcessedAt < now.AddDays(-7)).ExecuteDeleteAsync(ct);
        await db.SaveChangesAsync(ct);
    }
}
