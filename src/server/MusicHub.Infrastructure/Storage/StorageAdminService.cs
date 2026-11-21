using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MusicHub.Domain;

namespace MusicHub.Infrastructure.Storage;

/// <summary>The outcome of asking to move the library.</summary>
/// <param name="Queued">Whether a job was created.</param>
/// <param name="JobId">The queued job, when one was created.</param>
/// <param name="Error">Why the request was refused, in operator language.</param>
/// <param name="Checks">The safety checks, so a client can show which one failed.</param>
/// <param name="Target">The absolute target folder, when the request got that far.</param>
public sealed record StorageRelocationRequestResult(
    bool Queued,
    Guid? JobId,
    string? Error,
    IReadOnlyList<StorageCheck> Checks,
    string? Target);

/// <summary>What the storage screen needs to know.</summary>
public sealed record StorageRelocationSnapshot(
    bool Active,
    StorageRelocationJob? Job,
    LibraryRelocationState? Progress);

public sealed record StorageRelocationJob(
    Guid Id,
    string Status,
    int Attempts,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt,
    string? Error);

/// <summary>
/// The rules for moving the library, in one place because two front ends ask for it: the admin
/// HTTP endpoints and the Telegram bot.
/// </summary>
/// <remarks>
/// <para>
/// This is the single implementation of "may this move start". The checks it runs, the one-move-at-a-
/// time rule and the "no upload in flight" rule are authorization-adjacent decisions, so a second
/// copy of them in the bot would be a second place to get them wrong.
/// </para>
/// <para>
/// The target is validated here and validated again by <see cref="LibraryRelocationService"/> before
/// it copies, because the queued payload is a database row and therefore untrusted input.
/// </para>
/// </remarks>
public sealed class StorageAdminService(MusicHubDbContext db, LibraryLocationStore locations)
{
    public LibraryLocationStore Locations => locations;

    /// <summary>
    /// Validates <paramref name="path"/> and queues the move when every check passes. Returns rather
    /// than throws for a refusal, because every refusal is something the operator must be told.
    /// </summary>
    public async Task<StorageRelocationRequestResult> TryQueueRelocationAsync(
        string? path,
        CancellationToken ct)
    {
        var checks = StorageLocationValidator.Validate(
            path, locations.LibraryRoot, locations.DataRoot, requireEmpty: true);
        if (checks.FirstOrDefault(x => !x.Ok) is { } failed)
        {
            return new StorageRelocationRequestResult(false, null, failed.Message, checks, null);
        }

        var target = Path.GetFullPath(path!);
        var job = new BackgroundJob
        {
            Type = LibraryRelocationService.JobType,
            PayloadJson = JsonSerializer.Serialize(new { target }),
            Status = JobStatus.Pending,
            // One attempt: retrying against an unknown target is a decision for the operator, not
            // something the worker should repeat on its own.
            MaxAttempts = 1
        };

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        // "One move at a time" has to survive two taps arriving together, so the read and the insert
        // happen under one transaction-scoped lock. A plain check-then-insert let two concurrent
        // requests both observe an empty queue and both enqueue a copy of the whole library.
        //
        // The lock is advisory because no single row represents "the queue": there is nothing to
        // SELECT ... FOR UPDATE on. It is transaction-scoped, so it is released on commit or
        // rollback and cannot be left behind by a failed request. The key is arbitrary but must be
        // stable - it is the contract that every writer of this job type takes the same lock.
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({RelocationQueueLockKey})", ct);

        var pending = await db.BackgroundJobs.AnyAsync(
            x => x.Type == LibraryRelocationService.JobType
                 && (x.Status == JobStatus.Pending || x.Status == JobStatus.Processing), ct);
        if (pending)
        {
            return new StorageRelocationRequestResult(
                false, null, "A library move is already queued or running.", checks, null);
        }

        // A file committed while the copy runs is covered by the verification pass, but an upload in
        // flight is a moving target for both. Asking the operator to wait is the honest answer.
        var uploading = await db.UploadSessions.AnyAsync(
            x => x.Status == UploadStatus.Pending || x.Status == UploadStatus.Validating, ct);
        if (uploading)
        {
            return new StorageRelocationRequestResult(false, null,
                "An upload is in progress. Wait for it to finish before moving the library.", checks, null);
        }

        db.BackgroundJobs.Add(job);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        return new StorageRelocationRequestResult(true, job.Id, null, checks, target);
    }

    /// <summary>
    /// The advisory-lock key that serialises "may this move start". Any value would do; it must never
    /// change, because two different keys would stop excluding each other.
    /// </summary>
    private const long RelocationQueueLockKey = 6_131_204_118;

    /// <summary>Reads the latest move and its progress. Never throws on a damaged progress row.</summary>
    public async Task<StorageRelocationSnapshot> ReadRelocationAsync(CancellationToken ct)
    {
        var job = await db.BackgroundJobs.AsNoTracking()
            .Where(x => x.Type == LibraryRelocationService.JobType)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new { x.Id, x.Status, x.Attempts, x.CreatedAt, x.CompletedAt, x.LastError })
            .FirstOrDefaultAsync(ct);

        var stateRow = await db.AppSettings.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Key == LibraryRelocationState.SettingKey, ct);

        LibraryRelocationState? progress = null;
        if (stateRow is not null)
        {
            try
            {
                progress = JsonSerializer.Deserialize<LibraryRelocationState>(stateRow.Value);
            }
            catch (JsonException)
            {
                // A damaged progress row must not break the whole storage screen.
            }
        }

        return new StorageRelocationSnapshot(
            job is not null && job.Status is JobStatus.Pending or JobStatus.Processing,
            job is null
                ? null
                : new StorageRelocationJob(job.Id, job.Status.ToString(), job.Attempts,
                    job.CreatedAt, job.CompletedAt, job.LastError),
            progress);
    }

    /// <summary>The single in-flight move, if any. Used by the "one move at a time" check.</summary>
    public Task<bool> IsRelocationActiveAsync(CancellationToken ct) =>
        db.BackgroundJobs.AnyAsync(
            x => x.Type == LibraryRelocationService.JobType
                 && (x.Status == JobStatus.Pending || x.Status == JobStatus.Processing), ct);
}
