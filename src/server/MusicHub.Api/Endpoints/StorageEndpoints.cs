using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MusicHub.Domain;
using MusicHub.Infrastructure;
using MusicHub.Infrastructure.Security;
using MusicHub.Infrastructure.Storage;

namespace MusicHub.Api.Endpoints;

public static class StorageEndpoints
{
    public static void MapStorageEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/admin/storage/integrity-scans", [Authorize(Policy = "Admin")] async (ClaimsPrincipal principal, MusicHubDbContext db, HttpContext http, CancellationToken ct) =>
        {
            var running = await db.BackgroundJobs.AnyAsync(x => x.Type == "integrity.scan" && (x.Status == JobStatus.Pending || x.Status == JobStatus.Processing), ct);
            if (running) return Results.Conflict(new { error = "An integrity scan is already queued or running." });

            var job = new BackgroundJob
            {
                Type = "integrity.scan",
                PayloadJson = "{}",
                Status = JobStatus.Pending,
                MaxAttempts = 2
            };
            db.BackgroundJobs.Add(job);
            AuditWriter.Add(db, principal, "storage.integrity.enqueue", "BackgroundJob", job.Id.ToString(), null, RequestHelpers.Ip(http));
            await db.SaveChangesAsync(ct);
            return Results.Accepted($"/api/admin/jobs/{job.Id}", new { jobId = job.Id, status = job.Status });
        });

        app.MapGet("/api/admin/storage/integrity-scans", [Authorize(Policy = "Admin")] async (MusicHubDbContext db, int? take, CancellationToken ct) =>
        {
            var limit = Math.Clamp(take ?? 20, 1, 100);
            var rows = await db.StorageIntegrityChecks.AsNoTracking().OrderByDescending(x => x.StartedAt).Take(limit)
                .Select(x => new { x.Id, x.Status, x.AssetCount, x.MissingCount, x.OrphanCount, x.StartedAt, x.CompletedAt })
                .ToListAsync(ct);
            return Results.Ok(rows);
        });

        app.MapGet("/api/admin/storage/integrity-scans/{id:guid}", [Authorize(Policy = "Admin")] async (Guid id, MusicHubDbContext db, CancellationToken ct) =>
        {
            var row = await db.StorageIntegrityChecks.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
            return row is null ? Results.NotFound() : Results.Ok(row);
        });

        app.MapGet("/api/admin/jobs", [Authorize(Policy = "Admin")] async (MusicHubDbContext db, string? status, int? page, int? pageSize, CancellationToken ct) =>
        {
            var (p, size) = RequestHelpers.Page(page, pageSize);
            var query = db.BackgroundJobs.AsNoTracking().AsQueryable();
            if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<JobStatus>(status, true, out var parsed))
            {
                query = query.Where(x => x.Status == parsed);
            }
            var total = await query.CountAsync(ct);
            var rows = await query.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).Skip((p - 1) * size).Take(size)
                .Select(x => new { x.Id, x.Type, x.Status, x.Attempts, x.MaxAttempts, x.AvailableAt, x.LastError, x.CreatedAt, x.CompletedAt })
                .ToListAsync(ct);
            return Results.Ok(new PagedResult<object>(rows.Cast<object>().ToList(), p, size, total));
        });

        app.MapPost("/api/admin/jobs/{id:guid}/retry", [Authorize(Policy = "Admin")] async (Guid id, ClaimsPrincipal principal, MusicHubDbContext db, HttpContext http, CancellationToken ct) =>
        {
            var job = await db.BackgroundJobs.SingleOrDefaultAsync(x => x.Id == id, ct);
            if (job is null) return Results.NotFound();
            if (job.Status is JobStatus.Completed or JobStatus.Processing)
            {
                return Results.Conflict(new { error = "Only failed or pending jobs can be retried." });
            }
            job.Status = JobStatus.Pending;
            job.Attempts = 0;
            job.AvailableAt = DateTimeOffset.UtcNow;
            job.LockedUntil = null;
            job.LastError = null;
            AuditWriter.Add(db, principal, "job.retry", "BackgroundJob", id.ToString(), JsonSerializer.Serialize(new { job.Type }), RequestHelpers.Ip(http));
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        app.MapGet("/api/admin/outbox", [Authorize(Policy = "Admin")] async (MusicHubDbContext db, int? take, CancellationToken ct) =>
        {
            var limit = Math.Clamp(take ?? 50, 1, 200);
            var rows = await db.OutboxMessages.AsNoTracking().OrderByDescending(x => x.CreatedAt).Take(limit)
                .Select(x => new { x.Id, x.Type, x.Attempts, x.CreatedAt, x.ProcessedAt, x.NextAttemptAt, x.Error })
                .ToListAsync(ct);
            return Results.Ok(rows);
        });

        // Where the library lives, split into the two roots that must not be confused: the data
        // root (keys, credential, fixed for the life of the process) and the library root (music,
        // artwork, quarantine - the one an administrator may move).
        app.MapGet("/api/admin/storage/location", [Authorize(Policy = "Admin")] async (
            MusicHubDbContext db, StorageAdminService storage, IConfiguration config, CancellationToken ct) =>
        {
            var locations = storage.Locations;
            var libraryRoot = locations.LibraryRoot;
            var areas = new[] { "music", "artwork", "quarantine" }
                .Select(name => DescribeArea(name, Path.Combine(libraryRoot, name)))
                .ToArray();
            var assetCount = await db.FileHashes.CountAsync(ct);
            var assetBytes = await db.FileHashes.SumAsync(x => (long?)x.SizeBytes, ct) ?? 0;
            var relocation = await storage.ReadRelocationAsync(ct);

            return Results.Ok(new
            {
                dataRoot = locations.DataRoot,
                dataRootFrom = ResolveDataRootSource(config),
                library = new
                {
                    path = libraryRoot,
                    source = locations.Source,
                    pointerError = locations.PointerError,
                    writable = ProbeWritable(libraryRoot),
                    areas,
                    disk = DescribeDisk(libraryRoot)
                },
                assets = new { count = assetCount, bytes = assetBytes },
                relocation = new
                {
                    active = relocation.Active,
                    job = relocation.Job,
                    progress = relocation.Progress
                }
            });
        });

        // Validates a candidate library folder without changing anything. The only side effects are a
        // temporary probe file and, when the folder did not exist, the folder itself, which is
        // removed again when it is still empty.
        app.MapPost("/api/admin/storage/validate", [Authorize(Policy = "Admin")] (
            StoragePathCandidate request, StorageAdminService storage) =>
        {
            var locations = storage.Locations;
            var checks = StorageLocationValidator.Validate(
                request.Path, locations.LibraryRoot, locations.DataRoot, requireEmpty: true);
            var resolved = checks.Any(x => x.Id == "path.valid" && x.Ok)
                ? SafeFullPath(request.Path)
                : null;
            return Results.Ok(new
            {
                valid = checks.All(x => x.Ok),
                requestedPath = request.Path,
                resolvedPath = resolved,
                checks,
                disk = resolved is null ? null : DescribeDisk(resolved)
            });
        });

        // Queues the move. Copying a real library inside an HTTP request would time out, so the
        // worker performs it and the client polls /relocation for progress.
        app.MapPost("/api/admin/storage/relocate", [Authorize(Policy = "Admin")] async (
            StorageRelocateRequest request, ClaimsPrincipal principal, MusicHubDbContext db,
            StorageAdminService storage, HttpContext http, CancellationToken ct) =>
        {
            // A machine-driven request must state the intent: this moves a whole library.
            if (!string.Equals(request.Confirmation, "relocate", StringComparison.Ordinal))
            {
                return Results.BadRequest(new
                {
                    error = "Set confirmation to \"relocate\" to move the library.",
                    checks = Array.Empty<StorageCheck>()
                });
            }

            var result = await storage.TryQueueRelocationAsync(request.Path, ct);
            if (!result.Queued)
            {
                // A refusal caused by a failing safety check is a bad request; a refusal caused by
                // the library being busy is a conflict. Both carry the same body shape.
                return result.Checks.Any(x => !x.Ok)
                    ? Results.BadRequest(new { error = result.Error, checks = result.Checks })
                    : Results.Conflict(new { error = result.Error, checks = result.Checks });
            }

            AuditWriter.Add(db, principal, "storage.relocate.enqueue", "BackgroundJob", result.JobId!.Value.ToString(),
                JsonSerializer.Serialize(new { from = storage.Locations.LibraryRoot, to = result.Target }),
                RequestHelpers.Ip(http));
            await db.SaveChangesAsync(ct);

            return Results.Accepted("/api/admin/storage/relocation",
                new { jobId = result.JobId, target = result.Target, stateUrl = "/api/admin/storage/relocation" });
        });

        app.MapGet("/api/admin/storage/relocation", [Authorize(Policy = "Admin")] async (
            StorageAdminService storage, CancellationToken ct) =>
        {
            var relocation = await storage.ReadRelocationAsync(ct);
            return Results.Ok(new { active = relocation.Active, job = relocation.Job, progress = relocation.Progress });
        });
    }

    private static string ResolveDataRootSource(IConfiguration config) =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ConnectionStringResolver.DataRootEnvironmentVariable))
            ? "environment"
            : !string.IsNullOrWhiteSpace(config["Storage:RootPath"]) ? "configuration" : "default";

    private static string? SafeFullPath(string? path)
    {
        try
        {
            return string.IsNullOrWhiteSpace(path) ? null : Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    private static StorageArea DescribeArea(string name, string path)
    {
        if (!Directory.Exists(path)) return new StorageArea(name, path, false, null, null, null);
        try
        {
            long bytes = 0;
            var files = 0;
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                bytes += new FileInfo(file).Length;
                files++;
            }
            return new StorageArea(name, path, true, files, bytes, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new StorageArea(name, path, true, null, null, "Could not be measured.");
        }
    }

    private static bool ProbeWritable(string root)
    {
        try
        {
            Directory.CreateDirectory(root);
            var probe = Path.Combine(root, ".musichub-write-probe-" + Guid.NewGuid().ToString("N"));
            File.WriteAllText(probe, string.Empty);
            File.Delete(probe);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return false;
        }
    }

    private static DiskSpace? DescribeDisk(string rootPath)
    {
        try
        {
            var driveRoot = Path.GetPathRoot(Path.GetFullPath(rootPath));
            if (string.IsNullOrEmpty(driveRoot)) return null;
            var drive = new DriveInfo(driveRoot);
            if (!drive.IsReady) return null;
            return new DiskSpace(drive.Name, drive.TotalSize, drive.AvailableFreeSpace);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}

public sealed record StoragePathCandidate(string? Path);

public sealed record StorageRelocateRequest(string? Path, string? Confirmation);

public sealed record StorageArea(string Name, string Path, bool Exists, int? Files, long? Bytes, string? Error);

public sealed record DiskSpace(string Name, long TotalBytes, long FreeBytes);
