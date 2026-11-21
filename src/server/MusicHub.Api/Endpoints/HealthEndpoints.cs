using Microsoft.EntityFrameworkCore;
using MusicHub.Infrastructure;
using MusicHub.Infrastructure.Media;

namespace MusicHub.Api.Endpoints;

public static class HealthEndpoints
{
    // A deliberately cheap readiness probe: no data-root writes and no capability enumeration.
    // The previous version wrote and deleted a probe file under the quarantine root on every
    // anonymous call - arbitrary disk churn and an exception path on a full or read-only volume -
    // and published database/ffprobe/scanner/telegram state to any caller, which is
    // reconnaissance for someone mapping the deployment (CR-012).
    private static readonly TimeSpan DatabaseCacheTtl = TimeSpan.FromSeconds(10);
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static (bool Connected, DateTimeOffset CheckedAt) _databaseCache = (false, DateTimeOffset.MinValue);

    public static void MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/health", async (MusicHubDbContext db, ContentAddressedStorage storage, CancellationToken ct) =>
        {
            var database = await DatabaseReachableAsync(db, ct);

            // An access check, not a write: a directory that can be enumerated is enough to answer
            // "is the volume mounted and readable". MoveNext is what actually touches the volume,
            // so it has to be inside the try.
            var storageReadable = false;
            try
            {
                if (Directory.Exists(storage.MusicRoot))
                {
                    using var entries = Directory.EnumerateFileSystemEntries(storage.MusicRoot).GetEnumerator();
                    _ = entries.MoveNext();
                    storageReadable = true;
                }
            }
            catch
            {
                storageReadable = false;
            }

            var status = database ? "ok" : "degraded";
            var payload = new
            {
                status,
                version = "1.0.0",
                utc = DateTimeOffset.UtcNow,
                checks = new { database, storageReadable }
            };
            return database ? Results.Ok(payload) : Results.Json(payload, statusCode: StatusCodes.Status503ServiceUnavailable);
        }).AllowAnonymous();
    }

    /// <summary>
    /// Caches the database probe briefly so an aggressive poller cannot turn the readiness check
    /// into a connection storm. Only the successful/failed bit is cached, never an exception.
    /// </summary>
    private static async Task<bool> DatabaseReachableAsync(MusicHubDbContext db, CancellationToken ct)
    {
        var snapshot = _databaseCache;
        if (DateTimeOffset.UtcNow - snapshot.CheckedAt < DatabaseCacheTtl) return snapshot.Connected;

        await Gate.WaitAsync(ct);
        try
        {
            snapshot = _databaseCache;
            if (DateTimeOffset.UtcNow - snapshot.CheckedAt < DatabaseCacheTtl) return snapshot.Connected;

            var connected = false;
            try { connected = await db.Database.CanConnectAsync(ct); } catch { }
            _databaseCache = (connected, DateTimeOffset.UtcNow);
            return connected;
        }
        finally
        {
            Gate.Release();
        }
    }
}
