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
    }

    public sealed record SetupRequest(string? BootstrapToken, string? Email, string? Password, string? DisplayName);
}
