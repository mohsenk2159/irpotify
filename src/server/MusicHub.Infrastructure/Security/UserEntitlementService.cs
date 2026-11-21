using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MusicHub.Domain;

namespace MusicHub.Infrastructure.Security;

/// <summary>
/// Reason codes stored in <see cref="User.DeactivationReason"/>.
/// Values are persisted, so keep them short and stable.
/// </summary>
public static class DeactivationReasons
{
    public const string Expired = "expired";
    public const string StorageQuota = "storage_quota";
    public const string ListeningQuota = "listening_quota";
    public const string Manual = "manual";

    /// <summary>Operator-facing explanation for a deactivation reason code.</summary>
    public static string Describe(string? reason) => reason switch
    {
        Expired => "This account's access period has ended.",
        StorageQuota => "This account has used its full storage allowance.",
        ListeningQuota => "This account has used its full listening allowance.",
        Manual => "This account was disabled by an administrator.",
        _ => "This account is disabled."
    };
}

public sealed record EntitlementResult(bool Allowed, string? Reason)
{
    public static readonly EntitlementResult AllowedResult = new(true, null);
    public static EntitlementResult Denied(string reason) => new(false, reason);
}

/// <summary>
/// Central authority for account expiry, storage quota, and listening-time quota.
/// Administrators are always exempt from automatic deactivation.
/// </summary>
public sealed class UserEntitlementService(MusicHubDbContext db, ILogger<UserEntitlementService> logger)
{
    /// <summary>Bytes of non-deleted audio attributed to the user.</summary>
    public async Task<long> GetStorageUsedBytesAsync(Guid userId, CancellationToken ct) =>
        await db.Tracks.Where(x => x.CreatedByUserId == userId && !x.IsDeleted)
            .SumAsync(x => (long?)x.AudioAsset.SizeBytes, ct) ?? 0;

    private static bool IsExpired(DateTimeOffset? expiresAt, DateTimeOffset now) =>
        expiresAt is { } value && value <= now;

    private static bool IsStorageExhausted(long quotaBytes, long usedBytes) =>
        quotaBytes > 0 && usedBytes >= quotaBytes;

    private static bool IsListeningExhausted(long quotaSeconds, long usedSeconds) =>
        quotaSeconds > 0 && usedSeconds >= quotaSeconds;

    /// <summary>
    /// Evaluates the user's entitlements and deactivates the account when a limit is exhausted.
    /// Returns <see cref="EntitlementResult.AllowedResult"/> for administrators and for accounts within limits.
    /// </summary>
    public async Task<EntitlementResult> EnforceAsync(Guid userId, bool checkStorage, CancellationToken ct)
    {
        var user = await db.Users.SingleOrDefaultAsync(x => x.Id == userId, ct);
        if (user is null) return EntitlementResult.Denied(DeactivationReasons.Manual);

        var isAdministrator = await db.UserRoles
            .AnyAsync(x => x.UserId == userId && x.Role.Name == RoleNames.Administrator, ct);
        if (isAdministrator) return EntitlementResult.AllowedResult;

        if (!user.IsActive) return EntitlementResult.Denied(user.DeactivationReason ?? DeactivationReasons.Manual);

        var now = DateTimeOffset.UtcNow;
        string? violation = null;
        if (IsExpired(user.ExpiresAt, now)) violation = DeactivationReasons.Expired;
        else if (IsListeningExhausted(user.ListenQuotaSeconds, user.ListenSecondsUsed)) violation = DeactivationReasons.ListeningQuota;
        else if (checkStorage && IsStorageExhausted(user.QuotaBytes, await GetStorageUsedBytesAsync(userId, ct))) violation = DeactivationReasons.StorageQuota;

        if (violation is null) return EntitlementResult.AllowedResult;

        await DeactivateAsync(user.Id, violation, ct);
        return EntitlementResult.Denied(violation);
    }

    /// <summary>
    /// Adds reported playback time to the user's usage and enforces the listening quota immediately.
    /// </summary>
    public async Task<EntitlementResult> ConsumeListeningTimeAsync(Guid userId, long seconds, CancellationToken ct)
    {
        if (seconds <= 0) return await EnforceAsync(userId, checkStorage: false, ct);

        var user = await db.Users.SingleOrDefaultAsync(x => x.Id == userId, ct);
        if (user is null) return EntitlementResult.Denied(DeactivationReasons.Manual);
        if (!user.IsActive) return EntitlementResult.Denied(user.DeactivationReason ?? DeactivationReasons.Manual);

        var isAdministrator = await db.UserRoles
            .AnyAsync(x => x.UserId == userId && x.Role.Name == RoleNames.Administrator, ct);
        if (isAdministrator) return EntitlementResult.AllowedResult;

        // Increment in SQL. Reading the row, adding in memory and saving meant two overlapping
        // reports (the player flushes every 30 s and on pause/track change) both started from the
        // same baseline and the last commit won, so usage was systematically under-counted and
        // the allowance outlived what the operator granted (CR-018).
        var now = DateTimeOffset.UtcNow;
        await db.Users.Where(x => x.Id == userId).ExecuteUpdateAsync(s => s
            .SetProperty(x => x.ListenSecondsUsed, x => x.ListenSecondsUsed + seconds)
            .SetProperty(x => x.UpdatedAt, now), ct);

        // Only this entity is stale. Detaching it (rather than clearing the whole tracker) keeps any
        // pending work the caller has staged - the playback event the request is about to insert,
        // for example - instead of silently discarding it.
        db.Entry(user).State = EntityState.Detached;

        // Re-read after the atomic increment so the exhaustion decision uses the real total.
        var updated = await db.Users.AsNoTracking().Where(x => x.Id == userId)
            .Select(x => new { x.ListenQuotaSeconds, x.ListenSecondsUsed }).SingleOrDefaultAsync(ct);
        if (updated is null) return EntitlementResult.Denied(DeactivationReasons.Manual);

        if (IsListeningExhausted(updated.ListenQuotaSeconds, updated.ListenSecondsUsed))
        {
            await DeactivateAsync(userId, DeactivationReasons.ListeningQuota, ct);
            return EntitlementResult.Denied(DeactivationReasons.ListeningQuota);
        }

        return EntitlementResult.AllowedResult;
    }

    /// <summary>Deactivates an account by id. No-op when the account is missing or already inactive.</summary>
    public async Task DeactivateAsync(Guid userId, string reason, CancellationToken ct)
    {
        var user = await db.Users.SingleOrDefaultAsync(x => x.Id == userId, ct);
        if (user is null) return;
        await DeactivateAsync(user, reason, ct);
    }

    /// <summary>Deactivates a tracked account, revoking every refresh token and session.</summary>
    public async Task DeactivateAsync(User user, string reason, CancellationToken ct)
    {
        if (!user.IsActive) return;
        var now = DateTimeOffset.UtcNow;
        user.IsActive = false;
        user.DeactivatedAt = now;
        user.DeactivationReason = reason;
        user.UpdatedAt = now;
        user.SecurityStamp = Guid.NewGuid().ToString("N");

        await db.RefreshTokens.Where(x => x.UserId == user.Id && x.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, now), ct);
        await db.UserSessions.Where(x => x.UserId == user.Id && x.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, now), ct);

        db.AuditLogs.Add(new AuditLog
        {
            Action = "user.deactivate.auto",
            EntityType = "User",
            EntityId = user.Id.ToString(),
            DetailsJson = JsonSerializer.Serialize(new { reason }),
            CreatedAt = now
        });

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Account {UserId} deactivated automatically: {Reason}.", user.Id, reason);
    }

    /// <summary>
    /// Sweeps every active non-administrator whose expiry, storage quota, or listening quota
    /// is exhausted and deactivates them. Returns the number of accounts deactivated.
    /// </summary>
    /// <remarks>
    /// Everything needed to decide is read in two queries - the candidate rows and their storage
    /// usage - and the previous per-user <see cref="EnforceAsync"/> call (two more round trips per
    /// candidate, on every 2-minute pass) is gone. Deactivation still goes through
    /// <see cref="DeactivateAsync(Guid, string, CancellationToken)"/>, which revokes tokens,
    /// sessions and writes an audit row (CR-009).
    /// </remarks>
    public async Task<int> EnforceAllAsync(CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;

        var candidates = await db.Users.AsNoTracking()
            .Where(x => x.IsActive)
            .Where(x => !x.UserRoles.Any(r => r.Role.Name == RoleNames.Administrator))
            .Where(x => (x.ExpiresAt != null && x.ExpiresAt <= now)
                || (x.ListenQuotaSeconds > 0 && x.ListenSecondsUsed >= x.ListenQuotaSeconds)
                || x.QuotaBytes > 0)
            .Select(x => new { x.Id, x.ExpiresAt, x.ListenQuotaSeconds, x.ListenSecondsUsed, x.QuotaBytes })
            .ToListAsync(ct);

        if (candidates.Count == 0) return 0;

        var usage = await db.Tracks.AsNoTracking()
            .Where(x => !x.IsDeleted && x.CreatedByUserId != null)
            .GroupBy(x => x.CreatedByUserId!.Value)
            .Select(g => new { UserId = g.Key, Bytes = g.Sum(x => (long?)x.AudioAsset.SizeBytes) ?? 0 })
            .ToDictionaryAsync(x => x.UserId, x => x.Bytes, ct);

        var deactivated = 0;
        foreach (var candidate in candidates)
        {
            ct.ThrowIfCancellationRequested();

            // Same precedence as EnforceAsync: expiry, then listening, then storage.
            var violation = IsExpired(candidate.ExpiresAt, now) ? DeactivationReasons.Expired
                : IsListeningExhausted(candidate.ListenQuotaSeconds, candidate.ListenSecondsUsed) ? DeactivationReasons.ListeningQuota
                : IsStorageExhausted(candidate.QuotaBytes, usage.GetValueOrDefault(candidate.Id)) ? DeactivationReasons.StorageQuota
                : null;

            if (violation is null) continue;
            await DeactivateAsync(candidate.Id, violation, ct);
            deactivated++;
        }

        if (deactivated > 0)
        {
            logger.LogInformation("Entitlement sweep deactivated {Count} account(s).", deactivated);
        }
        return deactivated;
    }
}
