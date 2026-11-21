using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MusicHub.Api.Security;
using MusicHub.Infrastructure;
using MusicHub.Infrastructure.Options;
using MusicHub.Infrastructure.Security;

namespace MusicHub.Api.Endpoints;

public static class PairingEndpoints
{
    public static void MapPairingEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/pairing/create", [Microsoft.AspNetCore.Authorization.Authorize] async (PairingCreateRequest request, ClaimsPrincipal principal, MusicHubDbContext db, HttpContext http, IOptions<AuthOptions> authOptions, IConfiguration config, CancellationToken ct) =>
        {
            var userId = principal.GetUserId();
            var code = CryptoUtil.Base64UrlEncode(System.Security.Cryptography.RandomNumberGenerator.GetBytes(12));
            var expiresAt = DateTimeOffset.UtcNow.AddMinutes(Math.Clamp(authOptions.Value.PairingCodeMinutes, 1, 15));
            var pairing = new MusicHub.Domain.PairingSession
            {
                CodeHash = CryptoUtil.Sha256Hex(code),
                UserId = userId,
                DeviceName = request.DeviceName,
                ExpiresAt = expiresAt
            };
            db.PairingSessions.Add(pairing);
            AuditWriter.Add(db, principal, "pairing.create", "PairingSession", pairing.Id.ToString(), null, RequestHelpers.Ip(http));
            await db.SaveChangesAsync(ct);

            var serverUrl = PublicUrl.Resolve(http, config);
            var payload = $"musichub://pair?server={Uri.EscapeDataString(serverUrl)}&code={Uri.EscapeDataString(code)}&expires={expiresAt.ToUnixTimeSeconds()}";
            return Results.Ok(new { code, expiresAt, serverUrl, qrPayload = payload });
        });

        app.MapPost("/api/pairing/claim", async (PairingClaimRequest request, MusicHubDbContext db, AuthService auth, UserEntitlementService entitlements, HttpContext http, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Code)) return Results.BadRequest(new { error = "Pairing code is required." });
            var hash = CryptoUtil.Sha256Hex(request.Code.Trim());
            var session = await db.PairingSessions.Include(x => x.User)
                .SingleOrDefaultAsync(x => x.CodeHash == hash, ct);
            if (session is null || session.ExpiresAt <= DateTimeOffset.UtcNow || !session.User.IsActive)
            {
                return Results.Json(new { error = "Pairing code is invalid or expired." }, statusCode: StatusCodes.Status401Unauthorized);
            }

            // Login and refresh both enforce expiry/storage/listening before issuing tokens, but the
            // pairing claim did not, so an account waiting for the 2-minute sweep could pair and
            // receive tokens that OnTokenValidated killed on first use (CR-023).
            var entitlement = await entitlements.EnforceAsync(session.UserId, checkStorage: true, ct);
            if (!entitlement.Allowed)
            {
                return Results.Json(
                    new { error = DeactivationReasons.Describe(entitlement.Reason), reason = entitlement.Reason },
                    statusCode: StatusCodes.Status401Unauthorized);
            }

            var now = DateTimeOffset.UtcNow;
            var claimed = await db.PairingSessions
                .Where(x => x.Id == session.Id && x.UsedAt == null && x.ExpiresAt > now)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.UsedAt, now).SetProperty(x => x.UsedByIp, RequestHelpers.Ip(http)), ct);
            if (claimed != 1)
            {
                return Results.Json(new { error = "Pairing code has already been used." }, statusCode: StatusCodes.Status409Conflict);
            }

            var tokens = await auth.IssueForUserAsync(session.UserId, request.DeviceName ?? session.DeviceName, RequestHelpers.Ip(http), RequestHelpers.UserAgent(http), ct);
            if (tokens is null) return Results.Json(new { error = "Account disabled." }, statusCode: StatusCodes.Status401Unauthorized);
            var user = await db.Users.Include(x => x.UserRoles).ThenInclude(x => x.Role).SingleAsync(x => x.Id == session.UserId, ct);
            return Results.Ok(new
            {
                accessToken = tokens.AccessToken,
                accessTokenExpiresAt = tokens.AccessTokenExpiresAt,
                refreshToken = tokens.RefreshToken,
                refreshTokenExpiresAt = tokens.RefreshTokenExpiresAt,
                user = AuthService.ToDto(user)
            });
        }).RequireRateLimiting("pairing").AllowAnonymous();

        app.MapGet("/api/pairing/sessions", [Microsoft.AspNetCore.Authorization.Authorize] async (ClaimsPrincipal principal, MusicHubDbContext db, CancellationToken ct) =>
        {
            var userId = principal.GetUserId();
            var rows = await db.PairingSessions.AsNoTracking().Where(x => x.UserId == userId)
                .OrderByDescending(x => x.CreatedAt).Take(50)
                .Select(x => new { x.Id, x.DeviceName, x.ExpiresAt, x.UsedAt, x.CreatedAt })
                .ToListAsync(ct);
            return Results.Ok(rows);
        });
    }

    public sealed record PairingCreateRequest(string? DeviceName);
    public sealed record PairingClaimRequest(string? Code, string? DeviceName);
}
