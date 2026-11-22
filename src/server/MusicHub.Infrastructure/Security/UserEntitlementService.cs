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
/// Central authority for account expiry enforcement.
/// Evaluates account status strictly based on authentic server system time (DateTimeOffset.UtcNow).
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

    /// <summary>
    /// Evaluates the user's expiration date using server system time and deactivates the account when expired.
    /// Returns <see cref="EntitlementResult.AllowedResult"/> for administrators and for accounts whose period is active.
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
        if (IsExpired(user.ExpiresAt, now))
        {
            await DeactivateAsync(user.Id, DeactivationReasons.Expired, ct);
            return EntitlementResult.Denied(DeactivationReasons.Expired);
        }

        return EntitlementResult.AllowedResult;
    }

    /// <summary>
    /// Adds reported playback time to the user's statistics and verifies the account has not expired.
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

        var now = DateTimeOffset.UtcNow;
        if (IsExpired(user.ExpiresAt, now))
        {
            await DeactivateAsync(userId, DeactivationReasons.Expired, ct);
            return EntitlementResult.Denied(DeactivationReasons.Expired);
        }

        await db.Users.Where(x => x.Id == userId).ExecuteUpdateAsync(s => s
            .SetProperty(x => x.ListenSecondsUsed, x => x.ListenSecondsUsed + seconds)
            .SetProperty(x => x.UpdatedAt, now), ct);

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
    /// Sweeps every active non-administrator whose expiry date has passed and deactivates them,
    /// revoking all sessions and tokens across all devices.
    /// </summary>
    public async Task<int> EnforceAllAsync(CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;

        var expiredUserIds = await db.Users.AsNoTracking()
            .Where(x => x.IsActive)
            .Where(x => !x.UserRoles.Any(r => r.Role.Name == RoleNames.Administrator))
            .Where(x => x.ExpiresAt != null && x.ExpiresAt <= now)
            .Select(x => x.Id)
            .ToListAsync(ct);

        if (expiredUserIds.Count == 0) return 0;

        var deactivated = 0;
        foreach (var userId in expiredUserIds)
        {
            ct.ThrowIfCancellationRequested();
            await DeactivateAsync(userId, DeactivationReasons.Expired, ct);
            deactivated++;
        }

        if (deactivated > 0)
        {
            logger.LogInformation("Entitlement sweep deactivated {Count} expired account(s).", deactivated);
        }
        return deactivated;
    }
}
