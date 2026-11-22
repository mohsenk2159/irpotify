using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MusicHub.Api.Security;
using MusicHub.Domain;
using MusicHub.Infrastructure;
using MusicHub.Infrastructure.Options;

namespace MusicHub.Api.Endpoints;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/auth/login", async (LoginRequest request, AuthService auth, HttpContext http, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
            {
                return Results.BadRequest(new { error = "Email and password are required." });
            }

            var clientType = string.IsNullOrWhiteSpace(request.ClientType) ? "dashboard" : request.ClientType!;
            var outcome = await auth.LoginAsync(request.Email, request.Password, request.DeviceName, clientType, RequestHelpers.Ip(http), RequestHelpers.UserAgent(http), ct);
            if (outcome.Tokens is null || outcome.User is null)
            {
                return Results.Json(new { error = outcome.Error ?? "Login failed." }, statusCode: outcome.Locked ? StatusCodes.Status423Locked : StatusCodes.Status401Unauthorized);
            }
            return Results.Ok(new { accessToken = outcome.Tokens.AccessToken, accessTokenExpiresAt = outcome.Tokens.AccessTokenExpiresAt, refreshToken = outcome.Tokens.RefreshToken, refreshTokenExpiresAt = outcome.Tokens.RefreshTokenExpiresAt, user = outcome.User });
        }).RequireRateLimiting("login").AllowAnonymous();

        app.MapPost("/api/auth/refresh", async (RefreshRequest request, AuthService auth, HttpContext http, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.RefreshToken)) return Results.BadRequest(new { error = "Refresh token is required." });
            var outcome = await auth.RefreshAsync(request.RefreshToken, RequestHelpers.Ip(http), RequestHelpers.UserAgent(http), ct);
            if (outcome.Tokens is null || outcome.User is null) return Results.Json(new { error = outcome.Error ?? "Refresh failed." }, statusCode: StatusCodes.Status401Unauthorized);
            return Results.Ok(new { accessToken = outcome.Tokens.AccessToken, accessTokenExpiresAt = outcome.Tokens.AccessTokenExpiresAt, refreshToken = outcome.Tokens.RefreshToken, refreshTokenExpiresAt = outcome.Tokens.RefreshTokenExpiresAt, user = outcome.User });
        }).RequireRateLimiting("refresh").AllowAnonymous();

        app.MapPost("/api/auth/logout", async (RefreshRequest request, AuthService auth, HttpContext http, CancellationToken ct) =>
        {
            if (!string.IsNullOrWhiteSpace(request.RefreshToken)) await auth.RevokeAsync(request.RefreshToken, RequestHelpers.Ip(http), ct);
            return Results.NoContent();
        }).AllowAnonymous();

        app.MapPost("/api/auth/change-password", [Authorize] async (ChangePasswordRequest request, System.Security.Claims.ClaimsPrincipal principal, AuthService auth, MusicHubDbContext db, HttpContext http, CancellationToken ct) =>
        {
            var userId = principal.GetUserId();
            if (userId == Guid.Empty) return Results.Unauthorized();
            if (string.IsNullOrWhiteSpace(request.CurrentPassword)) return Results.BadRequest(new { error = "Current password is required." });
            var newPassword = request.NewPassword ?? string.Empty;
            if (newPassword.Length < 12) return Results.BadRequest(new { error = "Password must be at least 12 characters." });
            if (newPassword == request.CurrentPassword) return Results.BadRequest(new { error = "Choose a password you have not used before." });

            var outcome = await auth.ChangePasswordAsync(userId, request.CurrentPassword, newPassword, request.DeviceName, RequestHelpers.Ip(http), RequestHelpers.UserAgent(http), ct);
            if (outcome.Tokens is null || outcome.User is null)
            {
                // Deliberately 400, not 401. The caller is already authenticated by the bearer token
                // on this request, so a 401 would tell every client that its session expired - the
                // dashboard clears its tokens and signs the user out - when the only problem is a
                // mistyped field in the form. 401 is reserved for the token itself being invalid.
                return Results.BadRequest(new { error = outcome.Error ?? "Password change failed." });
            }

            AuditWriter.Add(db, principal, "user.password.change", "User", userId.ToString(), null, RequestHelpers.Ip(http));
            await db.SaveChangesAsync(ct);

            return Results.Ok(new
            {
                accessToken = outcome.Tokens.AccessToken,
                accessTokenExpiresAt = outcome.Tokens.AccessTokenExpiresAt,
                refreshToken = outcome.Tokens.RefreshToken,
                refreshTokenExpiresAt = outcome.Tokens.RefreshTokenExpiresAt,
                user = outcome.User
            });
        }).RequireRateLimiting("password");

        app.MapPost("/api/auth/logout-all", [Authorize] async (System.Security.Claims.ClaimsPrincipal principal, AuthService auth, HttpContext http, CancellationToken ct) =>
        {
            var userId = principal.GetUserId();
            if (userId == Guid.Empty) return Results.Unauthorized();
            await auth.RevokeAllAsync(userId, RequestHelpers.Ip(http), ct);
            return Results.NoContent();
        });

        app.MapGet("/api/auth/me", [Authorize] async (System.Security.Claims.ClaimsPrincipal principal, MusicHubDbContext db, CancellationToken ct) =>
        {
            var userId = principal.GetUserId();
            var user = await db.Users.Include(x => x.UserRoles).ThenInclude(x => x.Role).SingleOrDefaultAsync(x => x.Id == userId, ct);
            return user is null ? Results.NotFound() : Results.Ok(AuthService.ToDto(user));
        });

        app.MapGet("/api/auth/sessions", [Authorize] async (System.Security.Claims.ClaimsPrincipal principal, MusicHubDbContext db, CancellationToken ct) =>
        {
            var userId = principal.GetUserId();
            var sessions = await db.UserSessions.AsNoTracking()
                .Where(x => x.UserId == userId)
                .OrderByDescending(x => x.LastSeenAt)
                .Select(x => new { x.Id, x.DeviceName, x.IpAddress, x.CreatedAt, x.LastSeenAt, x.RevokedAt })
                .ToListAsync(ct);
            return Results.Ok(sessions);
        });

        app.MapDelete("/api/auth/sessions/{id:guid}", [Authorize] async (Guid id, System.Security.Claims.ClaimsPrincipal principal, MusicHubDbContext db, CancellationToken ct) =>
        {
            var userId = principal.GetUserId();
            var session = await db.UserSessions.SingleOrDefaultAsync(x => x.Id == id && x.UserId == userId, ct);
            if (session is null) return Results.NotFound();
            session.RevokedAt = DateTimeOffset.UtcNow;
            if (session.RefreshTokenId is { } tokenId)
            {
                var token = await db.RefreshTokens.FindAsync([tokenId], ct);
                if (token is not null) token.RevokedAt = DateTimeOffset.UtcNow;
            }
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });
    }

    public sealed record LoginRequest(string? Email, string? Password, string? DeviceName, string? ClientType);
    public sealed record RefreshRequest(string? RefreshToken);
    public sealed record ChangePasswordRequest(string? CurrentPassword, string? NewPassword, string? DeviceName);
}

public static class UserEndpoints
{
    /// <summary>Ten years, the upper bound accepted for a listening allowance.</summary>
    private const long MaxListenQuotaSeconds = 10L * 365 * 24 * 60 * 60;

    public static void MapUserEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/users", [Authorize(Policy = "Admin")] async (MusicHubDbContext db, CancellationToken ct) =>
        {
            var users = await db.Users.AsNoTracking().OrderBy(x => x.Email)
                .Select(x => new
                {
                    x.Id,
                    x.Email,
                    x.DisplayName,
                    x.IsActive,
                    x.CanUsePlayer,
                    x.QuotaBytes,
                    x.ExpiresAt,
                    x.ListenQuotaSeconds,
                    x.ListenSecondsUsed,
                    x.DeactivatedAt,
                    x.DeactivationReason,
                    x.CreatedAt,
                    x.LastLoginAt,
                    roles = x.UserRoles.Select(r => r.Role.Name)
                })
                .ToListAsync(ct);

            var usage = await StorageUsageAsync(db, ct);
            return Results.Ok(users.Select(x => new
            {
                x.Id,
                x.Email,
                x.DisplayName,
                x.IsActive,
                x.CanUsePlayer,
                x.QuotaBytes,
                x.ExpiresAt,
                x.ListenQuotaSeconds,
                x.ListenSecondsUsed,
                x.DeactivatedAt,
                x.DeactivationReason,
                x.CreatedAt,
                x.LastLoginAt,
                x.roles,
                storageUsedBytes = usage.TryGetValue(x.Id, out var used) ? used : 0
            }));
        });

        app.MapPost("/api/users", [Authorize(Policy = "Admin")] async (CreateUserRequest request, System.Security.Claims.ClaimsPrincipal principal, MusicHubDbContext db, IOptions<StorageOptions> storageOptions, HttpContext http, CancellationToken ct) =>
        {
            var email = (request.Email ?? string.Empty).Trim().ToLowerInvariant();
            // A malformed address is a validation failure (400) and a duplicate is a conflict (409).
            // Combining them in one Conflict branch reported a bad email as "already registered"
            // and hid which rule the caller broke (CR-021).
            if (!IsPlausibleEmail(email)) return Results.BadRequest(new { error = "Email is not a valid address." });
            if (await db.Users.AnyAsync(x => x.NormalizedEmail == email, ct)) return Results.Conflict(new { error = "Email is already registered." });
            if ((request.Password ?? string.Empty).Length < 12) return Results.BadRequest(new { error = "Password must be at least 12 characters." });
            if (request.ExpiresAt is { } expiresAt && expiresAt <= DateTimeOffset.UtcNow)
                return Results.BadRequest(new { error = "Expiry date must be in the future." });
            if (request.ListenQuotaSeconds is { } createListenQuota && (createListenQuota < 0 || createListenQuota > MaxListenQuotaSeconds))
                return Results.BadRequest(new { error = "Listening allowance must be between 0 (unlimited) and 87600 hours." });
            if (request.QuotaBytes is { } createQuota && (createQuota < 0 || createQuota > 100L * 1024 * 1024 * 1024 * 1024))
                return Results.BadRequest(new { error = "Storage quota must be between 0 and 100 TB." });

            var roleName = RoleNames.All.Contains(request.Role) ? request.Role! : RoleNames.Listener;
            var role = await db.Roles.SingleAsync(x => x.Name == roleName, ct);
            var user = new User
            {
                Email = email,
                NormalizedEmail = email,
                DisplayName = string.IsNullOrWhiteSpace(request.DisplayName) ? email : request.DisplayName.Trim(),
                PasswordHash = MusicHub.Infrastructure.Security.PasswordHasher.Hash(request.Password!),
                QuotaBytes = request.QuotaBytes is { } qb ? Math.Max(0, qb) : 0,
                CanUsePlayer = request.CanUsePlayer ?? true,
                ExpiresAt = request.ExpiresAt,
                ListenQuotaSeconds = request.ListenQuotaSeconds is { } lq ? Math.Max(0, lq) : 0
            };
            user.UserRoles.Add(new UserRole { User = user, RoleId = role.Id });
            db.Users.Add(user);
            AuditWriter.Add(db, principal, "user.create", "User", user.Id.ToString(), null, RequestHelpers.Ip(http));
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/users/{user.Id}", new
            {
                user.Id,
                user.Email,
                user.DisplayName,
                role = role.Name,
                user.QuotaBytes,
                user.CanUsePlayer,
                user.ExpiresAt,
                user.ListenQuotaSeconds,
                user.ListenSecondsUsed
            });
        });

        app.MapPatch("/api/users/{id:guid}", [Authorize(Policy = "Admin")] async (Guid id, UpdateUserRequest request, System.Security.Claims.ClaimsPrincipal principal, MusicHubDbContext db, HttpContext http, CancellationToken ct) =>
        {
            // Role must be included: the previous version loaded UserRoles only, so `x.Role.Name`
            // was null both here and in the last-administrator guard below.
            var user = await db.Users.Include(x => x.UserRoles).ThenInclude(x => x.Role).SingleOrDefaultAsync(x => x.Id == id, ct);
            if (user is null) return Results.NotFound();
            var wasActive = user.IsActive;

            if (request.DisplayName is not null) user.DisplayName = request.DisplayName.Trim()[..Math.Min(request.DisplayName.Trim().Length, 160)];
            if (request.QuotaBytes is not null) user.QuotaBytes = Math.Max(0, request.QuotaBytes.Value);
            if (request.CanUsePlayer is not null) user.CanUsePlayer = request.CanUsePlayer.Value;
            if (request.ClearExpiresAt == true)
            {
                user.ExpiresAt = null;
            }
            else if (request.ExpiresAt is { } expiresAt)
            {
                if (expiresAt <= DateTimeOffset.UtcNow)
                    return Results.BadRequest(new { error = "Expiry date must be in the future." });
                user.ExpiresAt = expiresAt;
            }
            if (request.ListenQuotaSeconds is { } listenQuotaSeconds)
            {
                if (listenQuotaSeconds < 0 || listenQuotaSeconds > MaxListenQuotaSeconds)
                    return Results.BadRequest(new { error = "Listening allowance must be between 0 (unlimited) and 87600 hours." });
                user.ListenQuotaSeconds = listenQuotaSeconds;
            }
            if (request.ResetListenUsage == true) user.ListenSecondsUsed = 0;
            var roleChanged = false;
            if (!string.IsNullOrWhiteSpace(request.Role) && RoleNames.All.Contains(request.Role))
            {
                var currentRole = user.UserRoles.Select(x => x.Role.Name).FirstOrDefault();
                if (!string.Equals(currentRole, request.Role, StringComparison.Ordinal))
                {
                    // An administrator may not remove the last administrator: doing so leaves the
                    // instance with no recovery path outside direct database access. The dashboard
                    // only hides the control for the current user, and UI hiding is not
                    // authorization (CR-005).
                    var isTargetAdministrator = string.Equals(currentRole, RoleNames.Administrator, StringComparison.Ordinal);
                    var wouldRemoveAdministrator = isTargetAdministrator && !string.Equals(request.Role, RoleNames.Administrator, StringComparison.Ordinal);
                    if (wouldRemoveAdministrator)
                    {
                        var remaining = await db.UserRoles.CountAsync(x => x.Role.Name == RoleNames.Administrator && x.UserId != user.Id, ct);
                        if (remaining == 0) return Results.Conflict(new { error = "At least one administrator is required." });
                    }
                    var role = await db.Roles.SingleAsync(x => x.Name == request.Role, ct);
                    db.UserRoles.RemoveRange(user.UserRoles);
                    user.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = role.Id });
                    roleChanged = true;
                }
            }

            var now = DateTimeOffset.UtcNow;
            var storageUsed = await db.Tracks.Where(x => x.CreatedByUserId == user.Id && !x.IsDeleted)
                .SumAsync(x => (long?)x.AudioAsset.SizeBytes, ct) ?? 0;

            if (request.IsActive is true)
            {
                // An inactive account may only be reactivated when the access period has not ended.
                // If the account has expired, a future expiry date must be set first or concurrently to start a new period.
                if (!wasActive)
                {
                    if (user.ExpiresAt is { } userExpiresAt && userExpiresAt <= now)
                        return Results.BadRequest(new { error = "Cannot reactivate: the access period has ended. Set a future expiry date first to start a new period." });
                }
                user.IsActive = true;
                user.DeactivatedAt = null;
                user.DeactivationReason = null;
            }
            else if (request.IsActive is false)
            {
                // Same dead end as demotion, reached a different way: deactivating the last active
                // administrator leaves nobody who can sign in to reactivate anyone, and the only
                // remaining route is the offline install CLI. The demotion guard above does not
                // cover this, so the check is repeated here rather than assumed.
                var isAdministrator = user.UserRoles.Any(x =>
                    string.Equals(x.Role.Name, RoleNames.Administrator, StringComparison.Ordinal));
                if (isAdministrator && wasActive)
                {
                    var otherActiveAdmins = await db.UserRoles.CountAsync(x =>
                        x.Role.Name == RoleNames.Administrator && x.UserId != user.Id && x.User.IsActive, ct);
                    if (otherActiveAdmins == 0)
                        return Results.Conflict(new { error = "At least one active administrator is required." });
                }

                user.IsActive = false;
                user.DeactivatedAt = now;
                user.DeactivationReason = MusicHub.Infrastructure.Security.DeactivationReasons.Manual;
                await db.RefreshTokens.Where(x => x.UserId == user.Id && x.RevokedAt == null)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, now), ct);
                await db.UserSessions.Where(x => x.UserId == user.Id && x.RevokedAt == null)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, now), ct);
            }

            user.UpdatedAt = now;
            // Rotating the stamp invalidates every live access token for the account, so it must
            // only happen on paths that genuinely revoke access: deactivation and a role change
            // (the JWT carries role claims). Rotating it for a display-name, quota or expiry edit
            // signed the user out of every client mid-playback (CR-005).
            var deactivated = wasActive && request.IsActive is false;
            if (deactivated || roleChanged) user.SecurityStamp = Guid.NewGuid().ToString("N");
            var details = System.Text.Json.JsonSerializer.Serialize(new
            {
                isActive = user.IsActive,
                expiresAt = user.ExpiresAt,
                listenQuotaSeconds = user.ListenQuotaSeconds,
                listenSecondsUsed = user.ListenSecondsUsed,
                quotaBytes = user.QuotaBytes,
                storageUsedBytes = storageUsed,
                deactivationReason = user.DeactivationReason
            });
            AuditWriter.Add(db, principal, "user.update", "User", user.Id.ToString(), details, RequestHelpers.Ip(http));
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        app.MapPost("/api/users/{id:guid}/reset-password", [Authorize(Policy = "Admin")] async (Guid id, ResetPasswordRequest request, System.Security.Claims.ClaimsPrincipal principal, MusicHubDbContext db, HttpContext http, CancellationToken ct) =>
        {
            if ((request.NewPassword ?? string.Empty).Length < 12) return Results.BadRequest(new { error = "Password must be at least 12 characters." });
            var user = await db.Users.SingleOrDefaultAsync(x => x.Id == id, ct);
            if (user is null) return Results.NotFound();
            user.PasswordHash = MusicHub.Infrastructure.Security.PasswordHasher.Hash(request.NewPassword!);
            user.SecurityStamp = Guid.NewGuid().ToString("N");
            user.FailedLoginCount = 0;
            user.LockoutEnd = null;
            var now = DateTimeOffset.UtcNow;
            await db.RefreshTokens.Where(x => x.UserId == user.Id && x.RevokedAt == null).ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, now), ct);
            await db.UserSessions.Where(x => x.UserId == user.Id && x.RevokedAt == null).ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, now), ct);
            AuditWriter.Add(db, principal, "user.password.reset", "User", user.Id.ToString(), null, RequestHelpers.Ip(http));
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        app.MapGet("/api/users/{id:guid}/login-history", [Authorize(Policy = "Admin")] async (Guid id, MusicHubDbContext db, CancellationToken ct) =>
        {
            var history = await db.LoginHistory.AsNoTracking().Where(x => x.UserId == id).OrderByDescending(x => x.CreatedAt).Take(100).ToListAsync(ct);
            return Results.Ok(history);
        });
    }

    private static async Task<Dictionary<Guid, long>> StorageUsageAsync(MusicHubDbContext db, CancellationToken ct) =>
        await db.Tracks.AsNoTracking()
            .Where(x => !x.IsDeleted && x.CreatedByUserId != null)
            .GroupBy(x => x.CreatedByUserId!.Value)
            .Select(g => new { UserId = g.Key, Bytes = g.Sum(x => (long?)x.AudioAsset.SizeBytes) ?? 0 })
            .ToDictionaryAsync(x => x.UserId, x => x.Bytes, ct);

    /// <summary>
    /// Shape check only. Real deliverability is not something this server can know, and the
    /// previous <c>Contains('@')</c> test accepted values such as <c>@</c> or <c>a@</c>.
    /// </summary>
    private static bool IsPlausibleEmail(string email)
    {
        if (email.Length < 3 || email.Length > 320) return false;
        var at = email.IndexOf('@');
        if (at <= 0 || at != email.LastIndexOf('@') || at == email.Length - 1) return false;
        var domain = email[(at + 1)..];
        return domain.Contains('.') && !domain.StartsWith('.') && !domain.EndsWith('.') && !email.Contains(' ');
    }

    public sealed record CreateUserRequest(string? Email, string? Password, string? DisplayName, string? Role, long? QuotaBytes, bool? CanUsePlayer, DateTimeOffset? ExpiresAt, long? ListenQuotaSeconds);
    public sealed record UpdateUserRequest(string? DisplayName, string? Role, bool? IsActive, long? QuotaBytes, bool? CanUsePlayer, DateTimeOffset? ExpiresAt, bool? ClearExpiresAt, long? ListenQuotaSeconds, bool? ResetListenUsage);
    public sealed record ResetPasswordRequest(string? NewPassword);
}
