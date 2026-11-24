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
            if (PortHelper.IsDashboardPort(http))
            {
                return Results.Json(new
                {
                    error = "اتصال غیرمجاز: فعال‌سازی و اتصال اپلیکیشن‌های پلیر و اندروید منحصراً باید از طریق پورت پلیر انجام شود."
                }, statusCode: StatusCodes.Status403Forbidden);
            }

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

            var (publicUrl, lanUrl, effectiveServer) = await ResolvePlayerPairingUrlsAsync(db, config, http, ct);

            var queryParams = new List<string>
            {
                $"server={Uri.EscapeDataString(effectiveServer)}",
                $"code={Uri.EscapeDataString(code)}",
                $"expires={expiresAt.ToUnixTimeSeconds()}"
            };
            if (!string.IsNullOrWhiteSpace(lanUrl))
            {
                queryParams.Add($"lan={Uri.EscapeDataString(lanUrl)}");
            }
            if (!string.IsNullOrWhiteSpace(deviceName))
            {
                queryParams.Add($"device={Uri.EscapeDataString(deviceName)}");
            }
            queryParams.Add($"email={Uri.EscapeDataString(targetUser.Email)}");
            queryParams.Add($"name={Uri.EscapeDataString(targetUser.DisplayName)}");

            var qrPayload = $"musichub://pair?{string.Join("&", queryParams)}";
            var qrCardUrl = $"{effectiveServer}/api/pairing/{Uri.EscapeDataString(code)}/card";
            var qrImageUrl = $"{effectiveServer}/api/pairing/{Uri.EscapeDataString(code)}/image.png";

            return Results.Ok(new
            {
                userId = targetUser.Id,
                email = targetUser.Email,
                displayName = targetUser.DisplayName,
                deviceName,
                code,
                expiresAt,
                publicServerUrl = !string.IsNullOrWhiteSpace(publicUrl) ? publicUrl : null,
                lanServerUrl = !string.IsNullOrWhiteSpace(lanUrl) ? lanUrl : null,
                effectiveServerUrl = effectiveServer,
                qrPayload,
                qrCardUrl,
                qrImageUrl
            });
        });

        app.MapGet("/api/pairing/{code}/image.png", async (string code, MusicHubDbContext db, HttpContext http, IConfiguration config, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(code)) return Results.NotFound();
            var hash = CryptoUtil.Sha256Hex(code.Trim());
            var session = await db.PairingSessions.Include(x => x.User)
                .AsNoTracking()
                .SingleOrDefaultAsync(x => x.CodeHash == hash, ct);
            if (session is null || session.ExpiresAt <= DateTimeOffset.UtcNow || !session.User.IsActive)
            {
                return Results.NotFound();
            }

            var (publicUrl, lanUrl, effectiveServer) = await ResolvePlayerPairingUrlsAsync(db, config, http, ct);

            var queryParams = new List<string>
            {
                $"server={Uri.EscapeDataString(effectiveServer)}",
                $"code={Uri.EscapeDataString(code.Trim())}",
                $"expires={session.ExpiresAt.ToUnixTimeSeconds()}"
            };
            if (!string.IsNullOrWhiteSpace(lanUrl)) queryParams.Add($"lan={Uri.EscapeDataString(lanUrl)}");
            if (!string.IsNullOrWhiteSpace(session.DeviceName)) queryParams.Add($"device={Uri.EscapeDataString(session.DeviceName)}");
            queryParams.Add($"email={Uri.EscapeDataString(session.User.Email)}");
            queryParams.Add($"name={Uri.EscapeDataString(session.User.DisplayName)}");

            var qrPayload = $"musichub://pair?{string.Join("&", queryParams)}";

            using var generator = new QRCoder.QRCodeGenerator();
            var qrCodeData = generator.CreateQrCode(qrPayload, QRCoder.QRCodeGenerator.ECCLevel.Q);
            var pngQr = new QRCoder.PngByteQRCode(qrCodeData);
            byte[] bytes = pngQr.GetGraphic(20);
            return Results.File(bytes, "image/png");
        }).AllowAnonymous();

        app.MapGet("/api/pairing/{code}/card", async (string code, MusicHubDbContext db, HttpContext http, IConfiguration config, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(code)) return Results.NotFound();
            var hash = CryptoUtil.Sha256Hex(code.Trim());
            var session = await db.PairingSessions.Include(x => x.User)
                .AsNoTracking()
                .SingleOrDefaultAsync(x => x.CodeHash == hash, ct);
            if (session is null || session.ExpiresAt <= DateTimeOffset.UtcNow || !session.User.IsActive)
            {
                return Results.Content("<h1 style='color:white;font-family:sans-serif;text-align:center;margin-top:100px;'>Pairing link expired or invalid</h1>", "text/html");
            }

            var (publicUrl, lanUrl, effectiveServer) = await ResolvePlayerPairingUrlsAsync(db, config, http, ct);

            var queryParams = new List<string>
            {
                $"server={Uri.EscapeDataString(effectiveServer)}",
                $"code={Uri.EscapeDataString(code.Trim())}",
                $"expires={session.ExpiresAt.ToUnixTimeSeconds()}"
            };
            if (!string.IsNullOrWhiteSpace(lanUrl)) queryParams.Add($"lan={Uri.EscapeDataString(lanUrl)}");
            if (!string.IsNullOrWhiteSpace(session.DeviceName)) queryParams.Add($"device={Uri.EscapeDataString(session.DeviceName)}");
            queryParams.Add($"email={Uri.EscapeDataString(session.User.Email)}");
            queryParams.Add($"name={Uri.EscapeDataString(session.User.DisplayName)}");

            var qrPayload = $"musichub://pair?{string.Join("&", queryParams)}";
            var imageUrl = $"{effectiveServer}/api/pairing/{Uri.EscapeDataString(code.Trim())}/image.png";
            var userName = System.Net.WebUtility.HtmlEncode(session.User.DisplayName);

            var html = $@"<!DOCTYPE html>
<html lang=""fa"" dir=""rtl"">
<head>
    <meta charset=""utf-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1"">
    <title>ورود سریع به irpotify - {userName}</title>
    <meta property=""og:title"" content=""ورود سریع به irpotify - {userName}"" />
    <meta property=""og:description"" content=""برای ورود به برنامه irpotify، این کد QR را اسکن کنید."" />
    <meta property=""og:image"" content=""{imageUrl}"" />
    <meta property=""og:image:width"" content=""600"" />
    <meta property=""og:image:height"" content=""600"" />
    <meta property=""og:type"" content=""website"" />
    <meta name=""twitter:card"" content=""summary_large_image"" />
    <meta name=""twitter:image"" content=""{imageUrl}"" />
    <style>
        body {{ font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif; background: #121212; color: #ffffff; display: flex; flex-direction: column; align-items: center; justify-content: center; min-height: 100vh; margin: 0; padding: 24px; box-sizing: border-box; text-align: center; }}
        .card {{ background: #181818; border: 1px solid #282828; border-radius: 20px; padding: 36px 24px; max-width: 420px; width: 100%; box-shadow: 0 12px 40px rgba(0,0,0,0.6); }}
        .logo {{ font-size: 26px; font-weight: 800; color: #1db954; letter-spacing: -0.5px; margin-bottom: 6px; }}
        h2 {{ margin: 6px 0 16px; font-size: 20px; font-weight: 600; color: #fff; }}
        .qr-wrap {{ background: #ffffff; padding: 18px; border-radius: 16px; display: inline-flex; margin: 12px 0 20px; box-shadow: 0 4px 16px rgba(0,0,0,0.25); }}
        .qr-wrap img {{ display: block; width: 240px; height: 240px; }}
        .desc {{ color: #b3b3b3; font-size: 14px; line-height: 1.6; margin: 0 0 24px; }}
        .btn {{ display: inline-block; background: #1db954; color: #000000; font-weight: 700; text-decoration: none; padding: 14px 32px; border-radius: 500px; font-size: 15px; transition: transform 0.15s, background 0.15s; }}
        .btn:hover {{ transform: scale(1.04); background: #1ed760; }}
    </style>
</head>
<body>
    <div class=""card"">
        <div class=""logo"">irpotify</div>
        <h2>ورود سریع {userName}</h2>
        <div class=""qr-wrap"">
            <img src=""{imageUrl}"" alt=""QR Code"" />
        </div>
        <p class=""desc"">این تصویر را با دوربین داخل اپلیکیشن irpotify اسکن کنید تا آدرس سرور و اطلاعات حساب شما خودکار تنظیم شود.</p>
        <a class=""btn"" href=""{qrPayload}"">باز کردن در اپلیکیشن irpotify</a>
    </div>
</body>
</html>";
            return Results.Content(html, "text/html", System.Text.Encoding.UTF8);
        }).AllowAnonymous();
    }

    private static async Task<(string? publicUrl, string? lanUrl, string effectiveServer)> ResolvePlayerPairingUrlsAsync(MusicHubDbContext db, IConfiguration config, HttpContext http, CancellationToken ct)
    {
        var stored = await db.AppSettings.AsNoTracking()
            .Where(x => x.Key == "server.publicBaseUrl" || x.Key == "server.lanBaseUrl" || x.Key == "server.playerPort" || x.Key == "server.dashboardPort" || x.Key == "server.localIp" || x.Key == "server.publicIpOrDomain")
            .ToDictionaryAsync(x => x.Key, x => x.Value, ct);

        var playerPort = stored.TryGetValue("server.playerPort", out var sPPort) && !string.IsNullOrWhiteSpace(sPPort)
            ? sPPort
            : (Environment.GetEnvironmentVariable("MUSICHUB_PLAYER_PORT") ?? "5081");

        var dashboardPort = stored.TryGetValue("server.dashboardPort", out var sDPort) && !string.IsNullOrWhiteSpace(sDPort)
            ? sDPort
            : (Environment.GetEnvironmentVariable("MUSICHUB_DASHBOARD_PORT") ?? "5080");

        var localIp = stored.TryGetValue("server.localIp", out var sLIp) && !string.IsNullOrWhiteSpace(sLIp)
            ? sLIp
            : NetworkAddressDetector.DetectLocalIPv4();

        var lanUrl = stored.TryGetValue("server.lanBaseUrl", out var storedLan) && !string.IsNullOrWhiteSpace(storedLan)
            ? storedLan.Trim().TrimEnd('/')
            : (!string.IsNullOrWhiteSpace(localIp) ? $"http://{localIp}:{playerPort}" : string.Empty);

        if (!string.IsNullOrWhiteSpace(lanUrl) && lanUrl.EndsWith($":{dashboardPort}"))
        {
            lanUrl = lanUrl[..^($":{dashboardPort}".Length)] + $":{playerPort}";
        }

        var publicUrl = stored.TryGetValue("server.publicBaseUrl", out var storedPub) && !string.IsNullOrWhiteSpace(storedPub)
            ? storedPub.Trim().TrimEnd('/')
            : (Environment.GetEnvironmentVariable("MUSICHUB_PUBLIC_URL") ?? config["Server:PublicBaseUrl"] ?? string.Empty).Trim().TrimEnd('/');

        if (string.IsNullOrWhiteSpace(publicUrl))
        {
            var publicHost = stored.TryGetValue("server.publicIpOrDomain", out var sPub) && !string.IsNullOrWhiteSpace(sPub)
                ? sPub
                : (Environment.GetEnvironmentVariable("MUSICHUB_PUBLIC_HOSTNAME") ?? string.Empty);

            if (!string.IsNullOrWhiteSpace(publicHost) && publicHost != "localhost")
            {
                publicUrl = System.Net.IPAddress.TryParse(publicHost, out _)
                    ? $"http://{publicHost}:{playerPort}"
                    : $"https://{publicHost}";
            }
        }

        if (!string.IsNullOrWhiteSpace(publicUrl) && publicUrl.EndsWith($":{dashboardPort}"))
        {
            publicUrl = publicUrl[..^($":{dashboardPort}".Length)] + $":{playerPort}";
        }

        var effectiveServer = !string.IsNullOrWhiteSpace(publicUrl)
            ? publicUrl
            : (!string.IsNullOrWhiteSpace(lanUrl) ? lanUrl : $"http://{http.Request.Host.Host}:{playerPort}");

        return (!string.IsNullOrWhiteSpace(publicUrl) ? publicUrl : null, !string.IsNullOrWhiteSpace(lanUrl) ? lanUrl : null, effectiveServer);
    }

    public sealed record PairingCreateRequest(string? DeviceName);
    public sealed record PairingClaimRequest(string? Code, string? DeviceName);
    public sealed record UserPairingQrRequest(string? DeviceName, int? ExpiryDays);
}
