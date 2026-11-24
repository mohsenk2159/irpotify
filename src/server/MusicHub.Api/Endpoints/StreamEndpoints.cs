using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MusicHub.Api.Realtime;
using MusicHub.Domain;
using MusicHub.Infrastructure;
using MusicHub.Infrastructure.Media;
using MusicHub.Infrastructure.Options;
namespace MusicHub.Api.Endpoints;

public sealed class StreamTokenService(IOptions<AuthOptions> options)
{
    private readonly byte[] _key = SHA256.HashData(Encoding.UTF8.GetBytes(options.Value.JwtSecret + ":stream"));

    /// <summary>
    /// Issues a stream token. The payload carries the issuing user so the stream endpoint - which
    /// is anonymous, because players fetch it without an Authorization header - can still attribute
    /// the bytes it serves and charge real listening time (B-023). Without the user id a token
    /// would be usable but unattributable, which is exactly the gap a forged client exploited.
    /// </summary>
    public (string Token, DateTimeOffset ExpiresAt) Create(Guid trackId, Guid userId, TimeSpan lifetime)
    {
        var expires = DateTimeOffset.UtcNow.Add(lifetime);
        var payload = $"{trackId:N}:{expires.ToUnixTimeSeconds()}:{userId:N}";
        var signature = Convert.ToBase64String(HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(payload))).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return ($"{payload}.{signature}", expires);
    }

    /// <summary>
    /// Validates a token and returns the user it was issued to. <paramref name="userId"/> is
    /// <see cref="Guid.Empty"/> only when the token is rejected.
    /// </summary>
    public bool Validate(string token, Guid expectedTrackId, out Guid userId)
    {
        userId = Guid.Empty;
        var dot = token.LastIndexOf('.');
        if (dot <= 0) return false;
        var payload = token[..dot];
        var signature = token[(dot + 1)..];
        var expected = Convert.ToBase64String(HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(payload))).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(signature), Encoding.UTF8.GetBytes(expected))) return false;
        var parts = payload.Split(':');
        if (parts.Length != 3 || !Guid.TryParseExact(parts[0], "N", out var trackId) || trackId != expectedTrackId) return false;
        if (!Guid.TryParseExact(parts[2], "N", out var issuer)) return false;
        if (!long.TryParse(parts[1], out var expiry) || DateTimeOffset.FromUnixTimeSeconds(expiry) <= DateTimeOffset.UtcNow) return false;
        userId = issuer;
        return true;
    }
}

public static class StreamEndpoints
{
    public static void MapStreamEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/tracks/{id:guid}/stream-token", [Authorize] (Guid id, StreamTokenService tokens, HttpContext http) =>
        {
            if (PortHelper.IsDashboardPort(http) && !http.User.IsInRole(RoleNames.Administrator))
            {
                return Results.Json(new { error = "اتصال غیرمجاز: پخش موسیقی برای کاربران و پلیرها منحصراً از طریق پورت پلیر امکان‌پذیر است." }, statusCode: StatusCodes.Status403Forbidden);
            }
            var (token, expires) = tokens.Create(id, http.User.GetUserId(), TimeSpan.FromMinutes(10));
            var url = $"/api/tracks/{id}/stream?token={Uri.EscapeDataString(token)}";
            return Results.Ok(new { token, expiresAt = expires, streamUrl = url, absoluteUrl = $"{http.Request.Scheme}://{http.Request.Host}{url}" });
        });

        app.MapMethods("/api/tracks/{id:guid}/stream", ["GET", "HEAD"], async (Guid id, StreamTokenService tokens, MusicHubDbContext db, ContentAddressedStorage storage, StreamTracker tracker, ListeningTimeTracker listening, IOptions<StorageOptions> storageOptions, HttpContext http, CancellationToken ct) =>
        {
            var authenticated = http.User.Identity?.IsAuthenticated == true;
            var queryToken = http.Request.Query["token"].FirstOrDefault();
            Guid tokenUserId = Guid.Empty;
            var tokenValid = !string.IsNullOrWhiteSpace(queryToken) && tokens.Validate(queryToken!, id, out tokenUserId);

            if (PortHelper.IsDashboardPort(http) && !authenticated)
            {
                // Unauthenticated player stream attempt on the dashboard port
                return Results.Json(new { error = "اتصال غیرمجاز: پخش موسیقی برای اپلیکیشن‌های پلیر منحصراً از طریق پورت پلیر امکان‌پذیر است." }, statusCode: StatusCodes.Status403Forbidden);
            }

            // The user who is charged for these bytes. A bearer-authenticated request uses the
            // token subject; an anonymous request (the player's media fetch) uses the user the
            // stream token was issued to. Never Guid.Empty when the request is authorized.
            var listenerId = authenticated ? http.User.GetUserId() : (tokenValid ? tokenUserId : Guid.Empty);

            if (!authenticated && !tokenValid)
            {
                return Results.Json(new { error = "Authentication or a valid stream token is required." }, statusCode: StatusCodes.Status401Unauthorized);
            }

            var data = await db.Tracks.AsNoTracking().Where(x => x.Id == id && !x.IsDeleted)
                .Select(x => new { x.AudioAsset.StoragePath, x.AudioAsset.MimeType, x.AudioAsset.SizeBytes, x.AudioAsset.FileHash.Sha256, x.Title })
                .SingleOrDefaultAsync(ct);
            if (data is null) return Results.NotFound();
            var path = storage.ResolveMusicPath(data.StoragePath);
            if (!File.Exists(path)) return Results.NotFound();

            // streaming.maxConcurrentPerUser was editable in the dashboard but nothing read it; the
            // only accounting was the global counter (CR-019). The stored setting wins over the
            // configured default so an operator change takes effect without a restart.
            var userId = http.User.GetUserId();
            var limit = storageOptions.Value.MaxConcurrentStreamsPerUser;
            var storedLimit = await db.AppSettings.AsNoTracking()
                .Where(x => x.Key == SettingsEndpoints.MaxConcurrentPerUserKey)
                .Select(x => x.Value)
                .SingleOrDefaultAsync(ct);
            if (int.TryParse(storedLimit, out var parsedLimit)) limit = parsedLimit;
            if (limit > 0 && userId != Guid.Empty && tracker.ActiveFor(userId) >= limit)
            {
                return Results.Json(
                    new { error = $"At most {limit} simultaneous streams are allowed per account." },
                    statusCode: StatusCodes.Status429TooManyRequests);
            }

            using var lease = tracker.Track(userId);
            var fileInfo = new FileInfo(path);
            var length = fileInfo.Length;
            http.Response.Headers.AcceptRanges = "bytes";
            http.Response.Headers.ETag = $"\"{data.Sha256}\"";
            http.Response.Headers.LastModified = fileInfo.LastWriteTimeUtc.ToString("R");
            http.Response.Headers.CacheControl = "private,max-age=0";
            http.Response.Headers.ContentDisposition = $"inline; filename*=UTF-8''{Uri.EscapeDataString(SafeDownloadName(data.Title, Path.GetExtension(path)))}";
            http.Response.ContentType = data.MimeType;

            var rangeHeader = http.Request.Headers.Range.FirstOrDefault();
            long start = 0;
            long end = length - 1;
            var partial = false;
            if (!string.IsNullOrWhiteSpace(rangeHeader))
            {
                if (!TryParseRange(rangeHeader!, length, out start, out end))
                {
                    http.Response.StatusCode = StatusCodes.Status416RangeNotSatisfiable;
                    http.Response.Headers.ContentRange = $"bytes */{length}";
                    return Results.Empty;
                }
                partial = true;
            }

            var count = end - start + 1;
            http.Response.StatusCode = partial ? StatusCodes.Status206PartialContent : StatusCodes.Status200OK;
            http.Response.ContentLength = count;
            if (partial) http.Response.Headers.ContentRange = $"bytes {start}-{end}/{length}";
            if (HttpMethods.IsHead(http.Request.Method)) return Results.Empty;

            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 64, FileOptions.Asynchronous | FileOptions.SequentialScan);
            stream.Seek(start, SeekOrigin.Begin);
            var buffer = new byte[1024 * 64];
            var remaining = count;
            long served = 0;
            try
            {
                while (remaining > 0)
                {
                    var wanted = (int)Math.Min(buffer.Length, remaining);
                    var read = await stream.ReadAsync(buffer.AsMemory(0, wanted), ct);
                    if (read == 0) break;
                    await http.Response.Body.WriteAsync(buffer.AsMemory(0, read), ct);
                    remaining -= read;
                    served += read;
                }
            }
            catch (OperationCanceledException) when (http.RequestAborted.IsCancellationRequested)
            {
                // Client disconnected.
            }
            catch (IOException) when (http.RequestAborted.IsCancellationRequested)
            {
                // Client disconnected.
            }
            finally
            {
                // Charge only the bytes that actually reached the client, so an aborted or
                // seeking player is billed for what it really played rather than the whole file.
                listening.AddStreamedBytes(listenerId, served);
            }
            return Results.Empty;
        }).AllowAnonymous();
    }

    private static bool TryParseRange(string header, long length, out long start, out long end)
    {
        start = 0;
        end = length - 1;
        if (!header.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase)) return false;
        var spec = header[6..].Trim();
        if (spec.Contains(',')) return false;
        var dash = spec.IndexOf('-');
        if (dash < 0) return false;
        var left = spec[..dash].Trim();
        var right = spec[(dash + 1)..].Trim();
        if (left.Length == 0)
        {
            if (!long.TryParse(right, out var suffix) || suffix <= 0) return false;
            suffix = Math.Min(suffix, length);
            start = length - suffix;
            end = length - 1;
        }
        else
        {
            if (!long.TryParse(left, out start) || start < 0 || start >= length) return false;
            if (right.Length > 0)
            {
                if (!long.TryParse(right, out end) || end < start) return false;
                end = Math.Min(end, length - 1);
            }
            else
            {
                end = length - 1;
            }
        }
        return true;
    }

    private static string SafeDownloadName(string title, string extension)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var name = new string(title.Where(c => !invalid.Contains(c)).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(name) ? "track" + extension : name + extension;
    }
}
