namespace MusicHub.Domain;

public sealed class Role
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsSystem { get; set; } = true;
    public ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();
}

public sealed class User
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Email { get; set; } = string.Empty;
    public string NormalizedEmail { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string SecurityStamp { get; set; } = Guid.NewGuid().ToString("N");
    public bool IsActive { get; set; } = true;
    public bool CanUsePlayer { get; set; } = true;
    public long QuotaBytes { get; set; } = 10L * 1024 * 1024 * 1024;
    /// <summary>Access period end. <c>null</c> means the account never expires.</summary>
    public DateTimeOffset? ExpiresAt { get; set; }
    /// <summary>Lifetime listening allowance in seconds. <c>0</c> means unlimited.</summary>
    public long ListenQuotaSeconds { get; set; }
    /// <summary>Accumulated playback time reported by clients, in seconds.</summary>
    public long ListenSecondsUsed { get; set; }
    public DateTimeOffset? DeactivatedAt { get; set; }
    /// <summary>Why the account was deactivated (see <c>DeactivationReasons</c>).</summary>
    public string? DeactivationReason { get; set; }
    public int FailedLoginCount { get; set; }
    public DateTimeOffset? LockoutEnd { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastLoginAt { get; set; }
    public ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();
}

public sealed class UserRole
{
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public int RoleId { get; set; }
    public Role Role { get; set; } = null!;
}

public sealed class RefreshToken
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public Guid FamilyId { get; set; } = Guid.NewGuid();
    public string TokenHash { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? RevokedAt { get; set; }
    public string? ReplacedByTokenHash { get; set; }
    public string? CreatedByIp { get; set; }
    public string? RevokedByIp { get; set; }
    public string? DeviceName { get; set; }
}

public sealed class UserSession
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public Guid? RefreshTokenId { get; set; }
    public string DeviceName { get; set; } = "Unknown device";
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastSeenAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? RevokedAt { get; set; }
}

public sealed class Device
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public string Name { get; set; } = string.Empty;
    public string Platform { get; set; } = string.Empty;
    public string? AppVersion { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastSeenAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class LoginHistory
{
    public long Id { get; set; }
    public Guid? UserId { get; set; }
    public string Email { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public string? FailureReason { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class Invitation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Email { get; set; } = string.Empty;
    public string RoleName { get; set; } = RoleNames.Listener;
    public string TokenHash { get; set; } = string.Empty;
    public Guid CreatedByUserId { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? AcceptedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class PairingSession
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string CodeHash { get; set; } = string.Empty;
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public string? DeviceName { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? UsedAt { get; set; }
    public string? UsedByIp { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class AuditLog
{
    public long Id { get; set; }
    public Guid? ActorUserId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public string? EntityId { get; set; }
    public string? DetailsJson { get; set; }
    public string? IpAddress { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class SystemNotification
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Type { get; set; } = "PasswordReset";
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? Email { get; set; }
    public Guid? UserId { get; set; }
    public User? User { get; set; }
    public bool IsRead { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ReadAt { get; set; }
}
