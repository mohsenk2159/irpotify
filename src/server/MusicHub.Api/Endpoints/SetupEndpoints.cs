using Microsoft.EntityFrameworkCore;
using MusicHub.Api;
using MusicHub.Domain;
using MusicHub.Infrastructure;
using MusicHub.Infrastructure.Security;

namespace MusicHub.Api.Endpoints;

public static class SetupEndpoints
{
    public static void MapSetupEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/setup/status", async (MusicHubDbContext db, CancellationToken ct) =>
        {
            var initialized = await db.Users.AnyAsync(ct);
            return Results.Ok(new
            {
                initialized,
                bootstrapTokenConfigured = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("MUSICHUB_BOOTSTRAP_TOKEN"))
            });
        });

        app.MapPost("/api/setup/initialize", async (SetupRequest request, MusicHubDbContext db, HttpContext http, CancellationToken ct) =>
        {
            if (await db.Users.AnyAsync(ct))
            {
                return Results.Conflict(new { error = "This server has already been initialized." });
            }

            var expectedToken = Environment.GetEnvironmentVariable("MUSICHUB_BOOTSTRAP_TOKEN");
            if (string.IsNullOrWhiteSpace(expectedToken))
            {
                return Results.Problem("Bootstrap is disabled because MUSICHUB_BOOTSTRAP_TOKEN is not configured.", statusCode: StatusCodes.Status503ServiceUnavailable);
            }
            if (!CryptoUtil.FixedTimeEquals(expectedToken, request.BootstrapToken ?? string.Empty))
            {
                return Results.Unauthorized();
            }

            var email = (request.Email ?? string.Empty).Trim().ToLowerInvariant();
            if (!email.Contains('@') || email.Length > 320) return Results.BadRequest(new { error = "A valid email is required." });
            if ((request.Password ?? string.Empty).Length < 12) return Results.BadRequest(new { error = "Password must be at least 12 characters." });
            if (string.IsNullOrWhiteSpace(request.DisplayName)) return Results.BadRequest(new { error = "Display name is required." });

            var role = await db.Roles.SingleAsync(x => x.Name == RoleNames.Administrator, ct);
            var user = new User
            {
                Email = email,
                NormalizedEmail = email,
                DisplayName = request.DisplayName.Trim()[..Math.Min(request.DisplayName.Trim().Length, 160)],
                PasswordHash = PasswordHasher.Hash(request.Password!),
                IsActive = true
            };
            user.UserRoles.Add(new UserRole { User = user, RoleId = role.Id });
            db.Users.Add(user);
            AuditWriter.Add(db, null, "setup.initialize", "User", user.Id.ToString(), null, RequestHelpers.Ip(http));
            await db.SaveChangesAsync(ct);
            return Results.Created("/api/auth/me", new { initialized = true, user = new { user.Id, user.Email, user.DisplayName } });
        }).RequireRateLimiting("setup");

        app.MapPost("/api/setup/reset-password", async (ResetPasswordWithTokenRequest request, MusicHubDbContext db, HttpContext http, CancellationToken ct) =>
        {
            var expectedToken = Environment.GetEnvironmentVariable("MUSICHUB_BOOTSTRAP_TOKEN");
            if (string.IsNullOrWhiteSpace(expectedToken))
            {
                return Results.Problem("Password reset is disabled because MUSICHUB_BOOTSTRAP_TOKEN is not configured.", statusCode: StatusCodes.Status503ServiceUnavailable);
            }
            if (!CryptoUtil.FixedTimeEquals(expectedToken, request.BootstrapToken ?? string.Empty))
            {
                return Results.Unauthorized();
            }
            var newPassword = request.NewPassword ?? string.Empty;
            if (newPassword.Length < 12)
            {
                return Results.BadRequest(new { error = "Password must be at least 12 characters." });
            }

            User? targetUser = null;
            if (!string.IsNullOrWhiteSpace(request.Email))
            {
                var email = request.Email.Trim().ToLowerInvariant();
                targetUser = await db.Users.Include(x => x.UserRoles).ThenInclude(x => x.Role)
                    .SingleOrDefaultAsync(x => x.NormalizedEmail == email, ct);
            }
            else
            {
                targetUser = await db.Users.Include(x => x.UserRoles).ThenInclude(x => x.Role)
                    .FirstOrDefaultAsync(x => x.UserRoles.Any(r => r.Role.Name == RoleNames.Administrator), ct);
            }

            if (targetUser is null) return Results.NotFound(new { error = "User account not found." });

            targetUser.PasswordHash = PasswordHasher.Hash(newPassword);
            targetUser.SecurityStamp = Guid.NewGuid().ToString("N");
            targetUser.FailedLoginCount = 0;
            targetUser.LockoutEnd = null;
            targetUser.IsActive = true;
            var now = DateTimeOffset.UtcNow;
            await db.RefreshTokens.Where(x => x.UserId == targetUser.Id && x.RevokedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, now), ct);
            await db.UserSessions.Where(x => x.UserId == targetUser.Id && x.RevokedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, now), ct);
            AuditWriter.Add(db, null, "setup.reset_password_with_token", "User", targetUser.Id.ToString(), null, RequestHelpers.Ip(http));
            await db.SaveChangesAsync(ct);

            return Results.Ok(new { success = true, email = targetUser.Email, message = "Password has been successfully reset." });
        }).RequireRateLimiting("setup").AllowAnonymous();
    }

    public sealed record SetupRequest(string? BootstrapToken, string? Email, string? Password, string? DisplayName);
    public sealed record ResetPasswordWithTokenRequest(string? BootstrapToken, string? Email, string? NewPassword);
}
