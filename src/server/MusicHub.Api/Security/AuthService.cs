using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using MusicHub.Domain;
using MusicHub.Infrastructure;
using MusicHub.Infrastructure.Options;
using MusicHub.Infrastructure.Security;

namespace MusicHub.Api.Security;

public sealed record TokenPair(string AccessToken, DateTimeOffset AccessTokenExpiresAt, string RefreshToken, DateTimeOffset RefreshTokenExpiresAt);

public sealed record UserDto(
    Guid Id,
    string Email,
    string DisplayName,
    string Role,
    IReadOnlyList<string> Roles,
    bool IsActive,
    bool CanUsePlayer,
    long QuotaBytes,
    DateTimeOffset? ExpiresAt,
    long ListenQuotaSeconds,
    long ListenSecondsUsed,
    DateTimeOffset? DeactivatedAt,
    string? DeactivationReason,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastLoginAt);

public sealed record LoginOutcome(UserDto? User, TokenPair? Tokens, string? Error, bool Locked = false);

public sealed class AuthService(
    MusicHubDbContext db,
    UserEntitlementService entitlements,
    IOptions<AuthOptions> options)
{
    private readonly AuthOptions _options = options.Value;

    public async Task<LoginOutcome> LoginAsync(string email, string password, string? deviceName, string? clientType, string? ip, string? userAgent, CancellationToken cancellationToken)
    {
        var normalized = CryptoUtil.Normalize(email);
        var user = await db.Users
            .Include(x => x.UserRoles).ThenInclude(x => x.Role)
            .SingleOrDefaultAsync(x => x.NormalizedEmail == normalized, cancellationToken);

        if (user is null)
        {
            db.LoginHistory.Add(new LoginHistory { Email = normalized, Success = false, IpAddress = ip, UserAgent = userAgent, FailureReason = "Unknown account" });
            await db.SaveChangesAsync(cancellationToken);
            return new LoginOutcome(null, null, "Invalid email or password.");
        }

        if (user.LockoutEnd is { } lockoutEnd && lockoutEnd > DateTimeOffset.UtcNow)
        {
            db.LoginHistory.Add(new LoginHistory { UserId = user.Id, Email = normalized, Success = false, IpAddress = ip, UserAgent = userAgent, FailureReason = "Account locked" });
            await db.SaveChangesAsync(cancellationToken);
            return new LoginOutcome(null, null, "Too many failed attempts. Try again later.", Locked: true);
        }

        if (!PasswordHasher.Verify(password, user.PasswordHash))
        {
            user.FailedLoginCount++;
            if (user.FailedLoginCount >= Math.Max(1, _options.MaximumFailedLogins))
            {
                user.LockoutEnd = DateTimeOffset.UtcNow.AddMinutes(Math.Max(1, _options.LockoutMinutes));
                user.FailedLoginCount = 0;
                user.SecurityStamp = Guid.NewGuid().ToString("N");
            }
            db.LoginHistory.Add(new LoginHistory { UserId = user.Id, Email = normalized, Success = false, IpAddress = ip, UserAgent = userAgent, FailureReason = "Invalid password" });
            await db.SaveChangesAsync(cancellationToken);
            return new LoginOutcome(null, null, "Invalid email or password.");
        }

        if (!user.IsActive)
        {
            db.LoginHistory.Add(new LoginHistory { UserId = user.Id, Email = normalized, Success = false, IpAddress = ip, UserAgent = userAgent, FailureReason = "Account disabled" });
            await db.SaveChangesAsync(cancellationToken);
            return new LoginOutcome(null, null, DeactivationReasons.Describe(user.DeactivationReason));
        }

        // Expiry, storage quota, and listening quota are enforced before any token is issued.
        var entitlement = await entitlements.EnforceAsync(user.Id, checkStorage: true, cancellationToken);
        if (!entitlement.Allowed)
        {
            db.LoginHistory.Add(new LoginHistory
            {
                UserId = user.Id,
                Email = normalized,
                Success = false,
                IpAddress = ip,
                UserAgent = userAgent,
                FailureReason = "Account deactivated: " + (entitlement.Reason ?? DeactivationReasons.Manual)
            });
            await db.SaveChangesAsync(cancellationToken);
            return new LoginOutcome(null, null, DeactivationReasons.Describe(entitlement.Reason));
        }

        // Client-type specific permission checks
        if (string.Equals(clientType, "player", StringComparison.OrdinalIgnoreCase) && !user.CanUsePlayer)
        {
            db.LoginHistory.Add(new LoginHistory { UserId = user.Id, Email = normalized, Success = false, IpAddress = ip, UserAgent = userAgent, FailureReason = "Player access not permitted" });
            await db.SaveChangesAsync(cancellationToken);
            return new LoginOutcome(null, null, "This account is not permitted to use the player application.");
        }

        user.FailedLoginCount = 0;
        user.LockoutEnd = null;
        user.LastLoginAt = DateTimeOffset.UtcNow;

        var tokens = await IssueTokensAsync(user, deviceName, ip, userAgent, familyId: null, sessionId: null, cancellationToken);
        db.LoginHistory.Add(new LoginHistory { UserId = user.Id, Email = normalized, Success = true, IpAddress = ip, UserAgent = userAgent });
        await db.SaveChangesAsync(cancellationToken);
        return new LoginOutcome(ToDto(user), tokens, null);
    }

    public async Task<LoginOutcome> RefreshAsync(string refreshToken, string? ip, string? userAgent, CancellationToken cancellationToken)
    {
        var hash = CryptoUtil.Sha256Hex(refreshToken);
        var existing = await db.RefreshTokens
            .Include(x => x.User).ThenInclude(x => x.UserRoles).ThenInclude(x => x.Role)
            .SingleOrDefaultAsync(x => x.TokenHash == hash, cancellationToken);

        if (existing is null)
        {
            return new LoginOutcome(null, null, "Invalid refresh token.");
        }

        if (existing.RevokedAt is not null)
        {
            await RevokeFamilyAsync(existing.FamilyId, ip, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            return new LoginOutcome(null, null, "Refresh token reuse detected. All sessions in this family were revoked.");
        }

        if (existing.ExpiresAt <= DateTimeOffset.UtcNow || !existing.User.IsActive)
        {
            return new LoginOutcome(null, null, "Refresh token expired or account disabled.");
        }

        var entitlement = await entitlements.EnforceAsync(existing.UserId, checkStorage: true, cancellationToken);
        if (!entitlement.Allowed)
        {
            return new LoginOutcome(null, null, DeactivationReasons.Describe(entitlement.Reason));
        }

        var session = await db.UserSessions
            .Where(x => x.RefreshTokenId == existing.Id && x.RevokedAt == null)
            .SingleOrDefaultAsync(cancellationToken);

        existing.RevokedAt = DateTimeOffset.UtcNow;
        existing.RevokedByIp = ip;
        var tokens = await IssueTokensAsync(existing.User, session?.DeviceName ?? existing.DeviceName, ip, userAgent, existing.FamilyId, session?.Id, cancellationToken);
        existing.ReplacedByTokenHash = CryptoUtil.Sha256Hex(tokens.RefreshToken);
        if (session is not null) session.LastSeenAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
        return new LoginOutcome(ToDto(existing.User), tokens, null);
    }

    /// <summary>
    /// Self-service password change. Requires the current password, rehashes with the current
    /// algorithm, rotates the security stamp (so access tokens issued before the change fail),
    /// revokes every refresh token and session, and issues a fresh token pair for the calling
    /// device so the user stays signed in only where the change was made.
    /// </summary>
    public async Task<LoginOutcome> ChangePasswordAsync(Guid userId, string currentPassword, string newPassword, string? deviceName, string? ip, string? userAgent, CancellationToken cancellationToken)
    {
        var user = await db.Users
            .Include(x => x.UserRoles).ThenInclude(x => x.Role)
            .SingleOrDefaultAsync(x => x.Id == userId, cancellationToken);
        if (user is null) return new LoginOutcome(null, null, "Account not found.");

        if (!PasswordHasher.Verify(currentPassword, user.PasswordHash))
            return new LoginOutcome(null, null, "Current password is incorrect.");

        if (PasswordHasher.Verify(newPassword, user.PasswordHash))
            return new LoginOutcome(null, null, "Choose a password you have not used before.");

        user.PasswordHash = PasswordHasher.Hash(newPassword);
        user.SecurityStamp = Guid.NewGuid().ToString("N");
        user.FailedLoginCount = 0;
        user.LockoutEnd = null;
        user.UpdatedAt = DateTimeOffset.UtcNow;

        var now = DateTimeOffset.UtcNow;
        await db.RefreshTokens.Where(x => x.UserId == user.Id && x.RevokedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.RevokedAt, now), cancellationToken);
        await db.UserSessions.Where(x => x.UserId == user.Id && x.RevokedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.RevokedAt, now), cancellationToken);

        var tokens = await IssueTokensAsync(user, deviceName, ip, userAgent, familyId: null, sessionId: null, cancellationToken);
        db.LoginHistory.Add(new LoginHistory
        {
            UserId = user.Id,
            Email = user.NormalizedEmail,
            Success = true,
            IpAddress = ip,
            UserAgent = userAgent,
            FailureReason = "Password changed"
        });
        await db.SaveChangesAsync(cancellationToken);
        return new LoginOutcome(ToDto(user), tokens, null);
    }

    public async Task<bool> RevokeAsync(string refreshToken, string? ip, CancellationToken cancellationToken)
    {
        var hash = CryptoUtil.Sha256Hex(refreshToken);
        var token = await db.RefreshTokens.SingleOrDefaultAsync(x => x.TokenHash == hash, cancellationToken);
        if (token is null || token.RevokedAt is not null) return false;
        token.RevokedAt = DateTimeOffset.UtcNow;
        token.RevokedByIp = ip;
        var session = await db.UserSessions.SingleOrDefaultAsync(x => x.RefreshTokenId == token.Id, cancellationToken);
        if (session is not null) session.RevokedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task RevokeAllAsync(Guid userId, string? ip, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        await db.RefreshTokens.Where(x => x.UserId == userId && x.RevokedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.RevokedAt, now).SetProperty(x => x.RevokedByIp, ip), cancellationToken);
        await db.UserSessions.Where(x => x.UserId == userId && x.RevokedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.RevokedAt, now), cancellationToken);
        var user = await db.Users.FindAsync([userId], cancellationToken);
        if (user is not null)
        {
            user.SecurityStamp = Guid.NewGuid().ToString("N");
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<TokenPair?> IssueForUserAsync(Guid userId, string? deviceName, string? ip, string? userAgent, CancellationToken cancellationToken)
    {
        var user = await db.Users.Include(x => x.UserRoles).ThenInclude(x => x.Role)
            .SingleOrDefaultAsync(x => x.Id == userId && x.IsActive, cancellationToken);
        if (user is null) return null;
        user.LastLoginAt = DateTimeOffset.UtcNow;
        var tokens = await IssueTokensAsync(user, deviceName, ip, userAgent, null, null, cancellationToken);
        db.LoginHistory.Add(new LoginHistory { UserId = user.Id, Email = user.NormalizedEmail, Success = true, IpAddress = ip, UserAgent = userAgent, FailureReason = "Pairing" });
        await db.SaveChangesAsync(cancellationToken);
        return tokens;
    }

    private async Task RevokeFamilyAsync(Guid familyId, string? ip, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        await db.RefreshTokens.Where(x => x.FamilyId == familyId && x.RevokedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.RevokedAt, now).SetProperty(x => x.RevokedByIp, ip), cancellationToken);
    }

    private async Task<TokenPair> IssueTokensAsync(User user, string? deviceName, string? ip, string? userAgent, Guid? familyId, Guid? sessionId, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var accessExpires = now.AddMinutes(Math.Clamp(_options.AccessTokenMinutes, 1, 120));
        var refreshExpires = now.AddDays(Math.Clamp(_options.RefreshTokenDays, 1, 365));
        var roles = user.UserRoles.Select(x => x.Role.Name).Distinct().ToArray();
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Name, user.DisplayName),
            new("security_stamp", user.SecurityStamp),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N"))
        };
        if (roles.Length == 0) roles = [RoleNames.Listener];
        foreach (var role in roles)
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
            claims.Add(new Claim("role", role));
        }

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.JwtSecret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var jwt = new JwtSecurityToken(_options.Issuer, _options.Audience, claims, now.UtcDateTime, accessExpires.UtcDateTime, credentials);
        var accessToken = new JwtSecurityTokenHandler().WriteToken(jwt);

        var refreshRaw = CryptoUtil.NewOpaqueToken(48);
        var refresh = new RefreshToken
        {
            UserId = user.Id,
            FamilyId = familyId ?? Guid.NewGuid(),
            TokenHash = CryptoUtil.Sha256Hex(refreshRaw),
            ExpiresAt = refreshExpires,
            CreatedAt = now,
            CreatedByIp = ip,
            DeviceName = deviceName
        };
        db.RefreshTokens.Add(refresh);

        if (sessionId is null)
        {
            db.UserSessions.Add(new UserSession
            {
                UserId = user.Id,
                RefreshTokenId = refresh.Id,
                DeviceName = string.IsNullOrWhiteSpace(deviceName) ? "Unknown device" : deviceName.Trim()[..Math.Min(deviceName.Trim().Length, 160)],
                IpAddress = ip,
                UserAgent = userAgent,
                CreatedAt = now,
                LastSeenAt = now
            });
        }
        else
        {
            var session = await db.UserSessions.FindAsync([sessionId.Value], cancellationToken);
            if (session is not null) session.RefreshTokenId = refresh.Id;
        }

        await Task.CompletedTask;
        return new TokenPair(accessToken, accessExpires, refreshRaw, refreshExpires);
    }

    public static UserDto ToDto(User user)
    {
        var roles = user.UserRoles.Select(x => x.Role.Name).Distinct().OrderBy(x => x).ToArray();
        if (roles.Length == 0) roles = [RoleNames.Listener];
        return new UserDto(
            user.Id,
            user.Email,
            user.DisplayName,
            roles[0],
            roles,
            user.IsActive,
            user.CanUsePlayer,
            user.QuotaBytes,
            user.ExpiresAt,
            user.ListenQuotaSeconds,
            user.ListenSecondsUsed,
            user.DeactivatedAt,
            user.DeactivationReason,
            user.CreatedAt,
            user.LastLoginAt);
    }
}
