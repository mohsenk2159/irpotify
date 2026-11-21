using System.Security.Claims;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.EntityFrameworkCore;
using MusicHub.Domain;
using MusicHub.Infrastructure;

namespace MusicHub.Api;

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int Total);

public static class ClaimsExtensions
{
    public static Guid GetUserId(this ClaimsPrincipal principal)
    {
        var raw = principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? principal.FindFirstValue("sub");
        return Guid.TryParse(raw, out var id) ? id : Guid.Empty;
    }

    public static string? GetSecurityStamp(this ClaimsPrincipal principal) => principal.FindFirstValue("security_stamp");
    public static bool IsAdministrator(this ClaimsPrincipal principal) => principal.IsInRole(RoleNames.Administrator);
}

/// <summary>
/// Result helpers for the 4xx cases that must still carry the documented <c>{ error }</c> body.
/// </summary>
public static class ApiResults
{
    /// <summary>
    /// 403 with the same <c>{ error }</c> shape every other 4xx uses. A bare
    /// <c>Results.Forbid()</c> produces an empty body (or a cookie-redirect for the identity
    /// middleware), which a client cannot tell apart from a transient failure (CR-021).
    /// </summary>
    public static IResult Forbidden(string message) =>
        Results.Json(new { error = message }, statusCode: StatusCodes.Status403Forbidden);

    /// <summary>401 with a body, for the same reason as <see cref="Forbidden"/>.</summary>
    public static IResult Unauthorized(string message) =>
        Results.Json(new { error = message }, statusCode: StatusCodes.Status401Unauthorized);
}

/// <summary>
/// Serves extracted artwork from both artwork endpoints so the cache header cannot drift between
/// them again (CR-013).
/// </summary>
public static class ArtworkResults
{
    /// <summary>
    /// These endpoints require authentication, so a shared cache must never store the response: a
    /// <c>public</c> directive explicitly authorises a proxy to replay the image to other users.
    /// </summary>
    public const string CacheControlValue = "private,max-age=86400";

    public static IResult File(HttpContext http, string fullPath)
    {
        http.Response.Headers.CacheControl = CacheControlValue;
        return Results.File(fullPath, "image/jpeg", enableRangeProcessing: false);
    }
}

public static class AuditWriter
{
    public static void Add(MusicHubDbContext db, ClaimsPrincipal? principal, string action, string entityType, string? entityId, string? detailsJson, string? ip)
    {
        db.AuditLogs.Add(new AuditLog
        {
            ActorUserId = principal?.GetUserId(),
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            DetailsJson = detailsJson,
            IpAddress = ip
        });
    }
}

public static class RequestHelpers
{
    public static string? Ip(HttpContext context) => context.Connection.RemoteIpAddress?.ToString();
    public static string? UserAgent(HttpContext context) => context.Request.Headers.UserAgent.ToString();

    public static (int Page, int PageSize) Page(int? page, int? pageSize)
    {
        var p = Math.Max(1, page ?? 1);
        var s = Math.Clamp(pageSize ?? 50, 1, 200);
        return (p, s);
    }

    public static string NormalizeSearch(string? value) => (value ?? string.Empty).Trim().ToLowerInvariant();
}

/// <summary>
/// Resolves the externally reachable origin used in pairing payloads and QR codes.
/// </summary>
public static class PublicUrl
{
    public const string EnvironmentVariable = "MUSICHUB_PUBLIC_URL";
    public const string ConfigurationKey = "Server:PublicBaseUrl";

    /// <summary>
    /// Precedence: <c>MUSICHUB_PUBLIC_URL</c>, then <c>Server:PublicBaseUrl</c>, then the origin of
    /// the current request.
    /// </summary>
    /// <remarks>
    /// A blank or whitespace value counts as <em>unset</em>. Reading the variable with <c>??</c>
    /// alone treated an empty string as configured and produced a pairing payload with an empty
    /// server (<c>musichub://pair?server=&amp;code=...</c>), which no client can parse (B-020).
    /// A value that is present but not an absolute http(s) URL is a configuration error rather
    /// than something to paper over, because the resulting QR code would be silently wrong.
    /// </remarks>
    public static string Resolve(HttpContext http, IConfiguration config)
    {
        foreach (var candidate in new[] { Environment.GetEnvironmentVariable(EnvironmentVariable), config[ConfigurationKey] })
        {
            if (string.IsNullOrWhiteSpace(candidate)) continue;

            var trimmed = candidate.Trim().TrimEnd('/');
            if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
                string.IsNullOrEmpty(uri.Host))
            {
                throw new ApiException(
                    StatusCodes.Status500InternalServerError,
                    $"{EnvironmentVariable} (or {ConfigurationKey}) is set to '{trimmed}', which is not an absolute http or https URL. " +
                    "Use a full origin such as https://music.example.com, or leave it unset to derive the origin from the request.");
            }

            return trimmed;
        }

        return $"{http.Request.Scheme}://{http.Request.Host}";
    }
}

public sealed class ApiException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}
