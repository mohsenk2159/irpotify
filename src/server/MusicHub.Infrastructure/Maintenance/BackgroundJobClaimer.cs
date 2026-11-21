using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using MusicHub.Domain;

namespace MusicHub.Infrastructure.Maintenance;

/// <summary>
/// Claims the next runnable background job with a single conditional UPDATE.
/// </summary>
/// <remarks>
/// The previous worker loaded a row with <c>FirstOrDefaultAsync</c>, mutated it and saved, so two
/// worker instances could both observe the same Pending row and both execute it (CR-008). The fix
/// is that <see cref="TryClaimAsync"/> re-tests the claimability predicate inside the UPDATE: a row
/// another process took in between no longer matches, <c>ExecuteUpdateAsync</c> reports zero rows
/// and the caller loses the claim.
///
/// <para>
/// <see cref="FindCandidateAsync"/> and <see cref="TryClaimAsync"/> are separate so callers (and
/// tests) can observe the two phases; <see cref="ClaimNextAsync"/> composes them for the normal
/// single-winner path.
/// </para>
/// </remarks>
public static class BackgroundJobClaimer
{
    /// <summary>A job this process now owns.</summary>
    public sealed record Claim(Guid JobId, int Attempt, DateTimeOffset LockedUntil);

    /// <summary>
    /// Picks the oldest runnable job id without changing anything. What it returns is only a hint:
    /// the row can change hands before it is claimed, which is why the claim re-checks the row.
    /// </summary>
    public static async Task<Guid?> FindCandidateAsync(MusicHubDbContext db, DateTimeOffset now, CancellationToken ct)
    {
        // Ordering carries an Id tie-breaker: several jobs queued in one request share AvailableAt,
        // and PostgreSQL does not guarantee a stable order among ties (CR-017).
        var candidateId = await db.BackgroundJobs
            .AsNoTracking()
            .Where(Claimable(now))
            .OrderBy(x => x.AvailableAt)
            .ThenBy(x => x.Id)
            .Select(x => (Guid?)x.Id)
            .FirstOrDefaultAsync(ct);
        return candidateId;
    }

    /// <summary>
    /// Moves <paramref name="jobId"/> to Processing with a fresh lock. Returns the claim, or
    /// <c>null</c> when the row is no longer claimable - already taken by another worker, finished,
    /// or scheduled for later. The predicate inside the UPDATE is the concurrency control.
    /// </summary>
    public static async Task<Claim?> TryClaimAsync(
        MusicHubDbContext db,
        Guid jobId,
        DateTimeOffset now,
        TimeSpan lockDuration,
        CancellationToken ct)
    {
        var lockUntil = now + lockDuration;

        var claimed = await db.BackgroundJobs
            .Where(x => x.Id == jobId)
            .Where(Claimable(now))
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Status, JobStatus.Processing)
                .SetProperty(x => x.Attempts, x => x.Attempts + 1)
                .SetProperty(x => x.LockedUntil, lockUntil), ct);

        if (claimed == 0) return null;

        var attempt = await db.BackgroundJobs.AsNoTracking()
            .Where(x => x.Id == jobId)
            .Select(x => x.Attempts)
            .SingleAsync(ct);
        return new Claim(jobId, attempt, lockUntil);
    }

    /// <summary>Finds and claims the next runnable job, or returns <c>null</c> when none is runnable.</summary>
    public static async Task<Claim?> ClaimNextAsync(
        MusicHubDbContext db,
        DateTimeOffset now,
        TimeSpan lockDuration,
        CancellationToken ct)
    {
        var candidateId = await FindCandidateAsync(db, now, ct);
        if (candidateId is null) return null;
        return await TryClaimAsync(db, candidateId.Value, now, lockDuration, ct);
    }

    /// <summary>
    /// Runnable now: Pending and due, or Processing with a lock that has expired (the previous
    /// owner died mid-job). Kept in one expression so the read and the UPDATE cannot drift apart.
    /// </summary>
    private static Expression<Func<BackgroundJob, bool>> Claimable(DateTimeOffset now) =>
        x => (x.Status == JobStatus.Pending && x.AvailableAt <= now)
             || (x.Status == JobStatus.Processing && x.LockedUntil != null && x.LockedUntil < now);
}
