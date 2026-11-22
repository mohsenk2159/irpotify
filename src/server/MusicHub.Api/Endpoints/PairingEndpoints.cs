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

        app.MapPost("/api/users/{id:guid}/pairing-qr", [Microsoft.AspNetCore.Authorization.Authorize(Policy = "Admin")] async (Guid id, UserPairingQrRequest? request, ClaimsPrincipal principal, MusicHubDbContext db, HttpContext http, IConfiguration config, CancellationToken ct) =>
        {
            var targetUser = await db.Users.SingleOrDefaultAsync(x => x.Id == id, ct);
            if (targetUser is null) return Results.NotFound(new { error = "User not found." });

            var deviceName = !string.IsNullOrWhiteSpace(request?.DeviceName)
                ? request.DeviceName.Trim()
                : $"{targetUser.DisplayName}'s Device";

            var days = Math.Clamp(request?.ExpiryDays ?? 30, 1, 365);
            var expiresAt = DateTimeOffset.UtcNow.AddDays(days);
            var code = CryptoUtil.Base64UrlEncode(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16));

            var pairing = new MusicHub.Domain.PairingSession
            {
                CodeHash = CryptoUtil.Sha256Hex(code),
                UserId = targetUser.Id,
                DeviceName = deviceName,
                ExpiresAt = expiresAt
            };
            db.PairingSessions.Add(pairing);
            AuditWriter.Add(db, principal, "user.pairing_qr", "User", targetUser.Id.ToString(), null, RequestHelpers.Ip(http));
            await db.SaveChangesAsync(ct);

            var stored = await db.AppSettings.AsNoTracking()
                .Where(x => x.Key == "server.publicBaseUrl" || x.Key == "server.publicUrlEnabled" || x.Key == "server.lanBaseUrl" || x.Key == "server.lanUrlEnabled")
                .ToDictionaryAsync(x => x.Key, x => x.Value, ct);

            var localIp = NetworkAddressDetector.DetectLocalIPv4();
            var defaultLanPort = Environment.GetEnvironmentVariable("MUSICHUB_LAN_PORT") ?? "5080";
            var lanUrl = stored.TryGetValue("server.lanBaseUrl", out var storedLan) && !string.IsNullOrWhiteSpace(storedLan)
                ? storedLan.Trim().TrimEnd('/')
                : $"http://{localIp}:{defaultLanPort}";
            var lanEnabled = !stored.TryGetValue("server.lanUrlEnabled", out var lEn) || lEn != "false";

            var publicUrl = stored.TryGetValue("server.publicBaseUrl", out var storedPub) && !string.IsNullOrWhiteSpace(storedPub)
                ? storedPub.Trim().TrimEnd('/')
                : (Environment.GetEnvironmentVariable("MUSICHUB_PUBLIC_URL") ?? config["Server:PublicBaseUrl"] ?? string.Empty).Trim().TrimEnd('/');

            if (string.IsNullOrWhiteSpace(publicUrl))
            {
                publicUrl = $"{http.Request.Scheme}://{http.Request.Host}".TrimEnd('/');
            }
            var publicEnabled = !stored.TryGetValue("server.publicUrlEnabled", out var pEn) || pEn != "false";

            var effectiveServer = publicEnabled && !string.IsNullOrWhiteSpace(publicUrl)
                ? publicUrl
                : (lanEnabled ? lanUrl : $"{http.Request.Scheme}://{http.Request.Host}".TrimEnd('/'));

            var effectiveLan = lanEnabled ? lanUrl : string.Empty;

            var queryParams = new List<string>
            {
                $"server={Uri.EscapeDataString(effectiveServer)}",
                $"code={Uri.EscapeDataString(code)}",
                $"expires={expiresAt.ToUnixTimeSeconds()}"
            };
            if (!string.IsNullOrWhiteSpace(effectiveLan))
            {
                queryParams.Add($"lan={Uri.EscapeDataString(effectiveLan)}");
            }
            if (!string.IsNullOrWhiteSpace(deviceName))
            {
                queryParams.Add($"device={Uri.EscapeDataString(deviceName)}");
            }
            queryParams.Add($"email={Uri.EscapeDataString(targetUser.Email)}");
            queryParams.Add($"name={Uri.EscapeDataString(targetUser.DisplayName)}");

            var qrPayload = $"musichub://pair?{string.Join("&", queryParams)}";

            return Results.Ok(new
            {
                userId = targetUser.Id,
                email = targetUser.Email,
                displayName = targetUser.DisplayName,
                deviceName,
                code,
                expiresAt,
                publicServerUrl = publicEnabled ? publicUrl : null,
                lanServerUrl = lanEnabled ? lanUrl : null,
                effectiveServerUrl = effectiveServer,
                qrPayload
            });
        });
    }

    public sealed record PairingCreateRequest(string? DeviceName);
    public sealed record PairingClaimRequest(string? Code, string? DeviceName);
    public sealed record UserPairingQrRequest(string? DeviceName, int? ExpiryDays);
}
