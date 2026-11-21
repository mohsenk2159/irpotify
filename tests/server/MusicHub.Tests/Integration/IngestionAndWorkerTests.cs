using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MusicHub.Domain;
using MusicHub.Infrastructure;
using MusicHub.Infrastructure.Maintenance;
using MusicHub.Infrastructure.Telegram;

namespace MusicHub.Tests.Integration;

/// <summary>
/// Regression tests for the two findings that turn a transient fault into a permanent one, and
/// which therefore cannot be verified by reading the code alone:
///
/// <list type="bullet">
/// <item>CR-007 - a Telegram poll whose batch contained only already-seen updates never advanced
/// the stored offset, so the same updates were fetched forever and ingestion stalled silently.</item>
/// <item>CR-008 - two worker instances could both load the same Pending background job and both
/// execute it, because the claim was a read-then-write and the advertised lock row did not exist.</item>
/// </list>
///
/// <para>
/// Both are exercised through the real types: the ingestion service talks HTTP to a loopback stub
/// of the Bot API, and the claimer runs the real SQL against PostgreSQL.
/// </para>
/// </summary>
[Collection("api")]
public class IngestionAndWorkerTests(ApiFactory factory)
{
    // ---- CR-007 ---------------------------------------------------------------------------

    [Fact]
    public async Task A_poll_of_already_seen_updates_still_advances_the_offset()
    {
        // Two updates that the database already knows about, which is exactly the state after a
        // crash between handling an update and persisting the offset.
        using var stub = new TelegramApiStub();
        try
        {
            stub.PendingUpdates = """
                [
                  { "update_id": 4101, "message": { "message_id": 1, "date": 1700000000, "from": { "id": 900501 }, "chat": { "id": 900501, "type": "private" } } },
                  { "update_id": 4102, "message": { "message_id": 2, "date": 1700000000, "from": { "id": 900501 }, "chat": { "id": 900501, "type": "private" } } }
                ]
                """;

            await CreateTelegramConfigAsync(stub.ApiBase);
            await SeedProcessedUpdateAsync(4101);
            await SeedProcessedUpdateAsync(4102);
            await SetOffsetAsync(0);

            var result = await PollOnceAsync();

            // Every update was skipped, so nothing was imported - and, before the fix, the offset
            // stayed at 0 for this batch, which is the wedge: the next poll re-fetches the same two
            // updates, skips them again, and never reaches anything after 4102.
            Assert.Equal(0, result.Imported);
            Assert.Equal(0, result.UpdatesProcessed);

            var offset = await ReadOffsetAsync();
            Assert.Equal(4103, offset);

            // The follow-up poll proves ingestion is still moving: the offset it sends is the one
            // that was just persisted.
            stub.PendingUpdates = "[]";
            await PollOnceAsync();
            Assert.NotNull(stub.LastRequestedOffset);
            Assert.Equal(4103, stub.LastRequestedOffset!.Value);
        }
        finally
        {
            await ClearTelegramStateAsync();
        }
    }

    [Fact]
    public async Task A_disabled_configuration_reports_why_and_touches_nothing()
    {
        using var stub = new TelegramApiStub();
        try
        {
            stub.PendingUpdates = "[]";
            await CreateTelegramConfigAsync(stub.ApiBase, ingestionEnabled: false);

            var result = await PollOnceAsync();

            Assert.Equal(0, result.UpdatesProcessed);
            Assert.False(string.IsNullOrWhiteSpace(result.Note));
            // No poll was attempted, so no request reached the Bot API.
            Assert.Null(stub.LastRequestedOffset);
        }
        finally
        {
            await ClearTelegramStateAsync();
        }
    }

    // ---- CR-008 ---------------------------------------------------------------------------

    [Fact]
    public async Task Two_workers_cannot_claim_the_same_job()
    {
        var jobId = await SeedPendingJobAsync("noop");
        var now = DateTimeOffset.UtcNow;
        var lockDuration = TimeSpan.FromMinutes(10);

        // Model the real race: both workers select their candidate *before* either claims. With the
        // pre-fix read-then-write shape both then took the row and the job ran twice.
        var candidateA = await FindCandidateAsync(now);
        var candidateB = await FindCandidateAsync(now);
        Assert.Equal(jobId, candidateA);
        Assert.Equal(jobId, candidateB);

        var claimA = await TryClaimAsync(candidateA!.Value, now, lockDuration);
        var claimB = await TryClaimAsync(candidateB!.Value, now, lockDuration);

        // Exactly one wins; the loser is told the row is gone rather than being handed it again.
        Assert.NotNull(claimA);
        Assert.Null(claimB);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MusicHubDbContext>();
        var stored = await db.BackgroundJobs.AsNoTracking().SingleAsync(x => x.Id == jobId);
        Assert.Equal(JobStatus.Processing, stored.Status);
        // One claim, one attempt increment - the second worker did not run the job.
        Assert.Equal(1, stored.Attempts);
        Assert.NotNull(stored.LockedUntil);
    }

    [Fact]
    public async Task A_second_claim_of_the_same_job_is_refused()
    {
        var jobId = await SeedPendingJobAsync("noop");
        var now = DateTimeOffset.UtcNow;
        var lockDuration = TimeSpan.FromMinutes(10);

        Assert.NotNull(await TryClaimAsync(jobId, now, lockDuration));

        // The row is already Processing under a live lock, so a repeat claim must be a no-op.
        Assert.Null(await TryClaimAsync(jobId, now, lockDuration));

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MusicHubDbContext>();
        var stored = await db.BackgroundJobs.AsNoTracking().SingleAsync(x => x.Id == jobId);
        Assert.Equal(1, stored.Attempts);
    }

    [Fact]
    public async Task A_job_whose_lock_expired_can_be_claimed_again()
    {
        var now = DateTimeOffset.UtcNow;
        var jobId = await SeedPendingJobAsync("noop", status: JobStatus.Processing, lockedUntil: now.AddMinutes(-1));

        var claim = await BackgroundJobClaimer.ClaimNextAsync(NewDbContext(), now, TimeSpan.FromMinutes(10), CancellationToken.None);

        Assert.NotNull(claim);
        Assert.Contains(jobId, new[] { claim!.JobId });
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MusicHubDbContext>();
        var stored = await db.BackgroundJobs.AsNoTracking().SingleAsync(x => x.Id == jobId);
        Assert.Equal(JobStatus.Processing, stored.Status);
        Assert.True(stored.LockedUntil > now, "a re-claimed job must get a fresh lock");
    }

    [Fact]
    public async Task A_job_scheduled_for_the_future_is_not_claimed()
    {
        var now = DateTimeOffset.UtcNow;
        var jobId = await SeedPendingJobAsync("noop", availableAt: now.AddMinutes(5));

        var claim = await BackgroundJobClaimer.ClaimNextAsync(NewDbContext(), now, TimeSpan.FromMinutes(10), CancellationToken.None);

        // Another test may have left a genuinely runnable job behind, so assert on this job only.
        if (claim is not null) Assert.NotEqual(jobId, claim.JobId);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MusicHubDbContext>();
        var stored = await db.BackgroundJobs.AsNoTracking().SingleAsync(x => x.Id == jobId);
        Assert.Equal(JobStatus.Pending, stored.Status);
        Assert.Equal(0, stored.Attempts);
        Assert.Null(stored.LockedUntil);
    }

    // ---- helpers ---------------------------------------------------------------------------

    private MusicHubDbContext NewDbContext()
    {
        var scope = factory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<MusicHubDbContext>();
    }

    private async Task<Guid?> FindCandidateAsync(DateTimeOffset now)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MusicHubDbContext>();
        return await BackgroundJobClaimer.FindCandidateAsync(db, now, CancellationToken.None);
    }

    private async Task<BackgroundJobClaimer.Claim?> TryClaimAsync(Guid jobId, DateTimeOffset now, TimeSpan lockDuration)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MusicHubDbContext>();
        return await BackgroundJobClaimer.TryClaimAsync(db, jobId, now, lockDuration, CancellationToken.None);
    }

    private async Task<BackgroundJobClaimer.Claim?> ClaimAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MusicHubDbContext>();
        return await BackgroundJobClaimer.ClaimNextAsync(db, DateTimeOffset.UtcNow, TimeSpan.FromMinutes(10), CancellationToken.None);
    }

    private async Task<TelegramIngestionResult> PollOnceAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<TelegramIngestionService>();
        return await service.PollOnceAsync(CancellationToken.None);
    }

    private async Task<Guid> CreateTelegramConfigAsync(string apiBase, bool ingestionEnabled = true)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MusicHubDbContext>();
        var protector = scope.ServiceProvider.GetRequiredService<TelegramSecretProtector>();

        var existing = await db.TelegramConfigurations.ToListAsync();
        db.TelegramConfigurations.RemoveRange(existing);

        var config = new TelegramConfiguration
        {
            BotTokenProtected = protector.Protect("123456789:AAFakeTokenValueForTestingOnly_1234567890"),
            AdminTelegramUserId = 900501,
            AdminChatId = 900501,
            IngestionEnabled = ingestionEnabled,
            AutoPublishEnabled = false,
            ApiEndpoint = apiBase,
            LocalBotApiEndpoint = null,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        db.TelegramConfigurations.Add(config);
        await db.SaveChangesAsync();
        return config.Id;
    }

    private async Task SeedProcessedUpdateAsync(long updateId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MusicHubDbContext>();
        if (await db.TelegramUpdates.AnyAsync(x => x.TelegramUpdateId == updateId)) return;
        db.TelegramUpdates.Add(new TelegramUpdate
        {
            TelegramUpdateId = updateId,
            PayloadJson = "{}",
            ProcessedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
    }

    private async Task SetOffsetAsync(long offset)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MusicHubDbContext>();
        var row = await db.AppSettings.SingleOrDefaultAsync(x => x.Key == "telegram.updateOffset");
        if (row is null)
        {
            db.AppSettings.Add(new AppSetting { Key = "telegram.updateOffset", Value = offset.ToString() });
        }
        else
        {
            row.Value = offset.ToString();
            row.UpdatedAt = DateTimeOffset.UtcNow;
        }
        await db.SaveChangesAsync();
    }

    private async Task<long> ReadOffsetAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MusicHubDbContext>();
        var row = await db.AppSettings.AsNoTracking().SingleAsync(x => x.Key == "telegram.updateOffset");
        return long.Parse(row.Value);
    }

    /// <summary>
    /// Removes every trace of this test's Telegram state. The suite shares one database, and
    /// <c>AuthorizationAndTelegramTests</c> asserts an unconfigured instance, so leaving a
    /// configuration behind makes that unrelated test fail depending on execution order.
    /// </summary>
    private async Task ClearTelegramStateAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MusicHubDbContext>();
        db.TelegramConfigurations.RemoveRange(db.TelegramConfigurations);
        var offset = await db.AppSettings.SingleOrDefaultAsync(x => x.Key == "telegram.updateOffset");
        if (offset is not null) db.AppSettings.Remove(offset);
        await db.SaveChangesAsync();
    }

    private async Task<Guid> SeedPendingJobAsync(
        string type,
        JobStatus status = JobStatus.Pending,
        DateTimeOffset? availableAt = null,
        DateTimeOffset? lockedUntil = null)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MusicHubDbContext>();
        var job = new BackgroundJob
        {
            Type = type,
            PayloadJson = "{}",
            Status = status,
            AvailableAt = availableAt ?? DateTimeOffset.UtcNow.AddSeconds(-1),
            LockedUntil = lockedUntil
        };
        db.BackgroundJobs.Add(job);
        await db.SaveChangesAsync();
        return job.Id;
    }

    /// <summary>
    /// Minimal loopback stand-in for the Telegram Bot API. Only <c>getUpdates</c> is needed: the
    /// offset behaviour is decided before any download is attempted, and every update in these
    /// tests is already known, so no media path is reached.
    /// </summary>
    private sealed class TelegramApiStub : IDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _loop;

        public string ApiBase { get; }
        public string PendingUpdates { get; set; } = "[]";
        public long? LastRequestedOffset { get; private set; }

        public TelegramApiStub()
        {
            var port = FreePort();
            ApiBase = $"http://127.0.0.1:{port}";
            _listener.Prefixes.Add(ApiBase + "/");
            _listener.Start();
            _loop = Task.Run(ServeAsync);
        }

        private async Task ServeAsync()
        {
            while (!_stop.IsCancellationRequested)
            {
                HttpListenerContext context;
                try
                {
                    context = await _listener.GetContextAsync();
                }
                catch (Exception) when (_stop.IsCancellationRequested)
                {
                    return;
                }
                catch (HttpListenerException)
                {
                    return;
                }

                using (context.Response)
                {
                    if (context.Request.Url!.AbsolutePath.EndsWith("/getUpdates", StringComparison.Ordinal))
                    {
                        using var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8);
                        var body = await reader.ReadToEndAsync();
                        using var request = JsonDocument.Parse(body);
                        LastRequestedOffset = request.RootElement.TryGetProperty("offset", out var offset) ? offset.GetInt64() : null;

                        var payload = $"{{\"ok\":true,\"result\":{PendingUpdates}}}";
                        var bytes = Encoding.UTF8.GetBytes(payload);
                        context.Response.StatusCode = 200;
                        context.Response.ContentType = "application/json";
                        context.Response.ContentLength64 = bytes.Length;
                        await context.Response.OutputStream.WriteAsync(bytes);
                    }
                    else
                    {
                        var bytes = Encoding.UTF8.GetBytes("{\"ok\":true,\"result\":true}");
                        context.Response.StatusCode = 200;
                        context.Response.ContentType = "application/json";
                        context.Response.ContentLength64 = bytes.Length;
                        await context.Response.OutputStream.WriteAsync(bytes);
                    }
                }
            }
        }

        private static int FreePort()
        {
            using var socket = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
            socket.Start();
            var port = ((IPEndPoint)socket.LocalEndpoint).Port;
            socket.Stop();
            return port;
        }

        public void Dispose()
        {
            _stop.Cancel();
            try { _listener.Stop(); } catch { }
            try { _listener.Close(); } catch { }
            try { _loop.Wait(TimeSpan.FromSeconds(5)); } catch { }
            _stop.Dispose();
        }
    }
}
