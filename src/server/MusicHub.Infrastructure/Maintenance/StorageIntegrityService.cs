using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MusicHub.Domain;
using MusicHub.Infrastructure.Media;

namespace MusicHub.Infrastructure.Maintenance;

public sealed record IntegrityIssue(Guid AssetId, string StoragePath, string Issue, long? ExpectedSize, long? ActualSize);

public sealed record IntegrityScanResult(Guid CheckId, int AssetCount, int MissingCount, int OrphanCount, int HashMismatchCount, int SizeMismatchCount);

/// <summary>
/// Verifies every stored asset against the database, recomputing SHA-256 and detecting orphan
/// files. It never deletes anything automatically; issues are reported for operator action.
/// </summary>
public sealed class StorageIntegrityService(
    MusicHubDbContext db,
    ContentAddressedStorage storage,
    ILogger<StorageIntegrityService> logger)
{
    public async Task<Guid> RunAsync(CancellationToken ct)
    {
        var check = new StorageIntegrityCheck { Status = IntegrityCheckStatus.Running };
        db.StorageIntegrityChecks.Add(check);
        await db.SaveChangesAsync(ct);

        var issues = new List<IntegrityIssue>();
        var missing = 0;
        var hashMismatch = 0;
        var sizeMismatch = 0;
        var orphanCount = 0;
        var assetCount = 0;

        try
        {
            var assets = await db.AudioAssets.AsNoTracking()
                .Select(x => new { x.Id, x.StoragePath, x.SizeBytes, Sha256 = x.FileHash.Sha256 })
                .ToListAsync(ct);
            assetCount = assets.Count;
            var knownNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var asset in assets)
            {
                ct.ThrowIfCancellationRequested();
                string path;
                try
                {
                    path = storage.ResolveMusicPath(asset.StoragePath);
                    knownNames.Add(Path.GetFileName(path));
                }
                catch (UnauthorizedAccessException)
                {
                    missing++;
                    issues.Add(new IntegrityIssue(asset.Id, asset.StoragePath, "path-outside-root", asset.SizeBytes, null));
                    continue;
                }

                if (!File.Exists(path))
                {
                    missing++;
                    issues.Add(new IntegrityIssue(asset.Id, asset.StoragePath, "missing", asset.SizeBytes, null));
                    continue;
                }

                var info = new FileInfo(path);
                if (info.Length != asset.SizeBytes)
                {
                    sizeMismatch++;
                    issues.Add(new IntegrityIssue(asset.Id, asset.StoragePath, "size-mismatch", asset.SizeBytes, info.Length));
                }

                try
                {
                    var actual = await ContentAddressedStorage.ComputeFileSha256Async(path, ct);
                    if (!string.Equals(actual, asset.Sha256, StringComparison.OrdinalIgnoreCase))
                    {
                        hashMismatch++;
                        issues.Add(new IntegrityIssue(asset.Id, asset.StoragePath, "hash-mismatch", asset.SizeBytes, info.Length));
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    issues.Add(new IntegrityIssue(asset.Id, asset.StoragePath, "unreadable", asset.SizeBytes, null));
                }
            }

            if (Directory.Exists(storage.MusicRoot))
            {
                foreach (var file in Directory.EnumerateFiles(storage.MusicRoot, "*", SearchOption.AllDirectories))
                {
                    ct.ThrowIfCancellationRequested();
                    if (!knownNames.Contains(Path.GetFileName(file)))
                    {
                        orphanCount++;
                        if (issues.Count < 500)
                        {
                            issues.Add(new IntegrityIssue(Guid.Empty, Path.GetRelativePath(storage.RootPath, file).Replace('\\', '/'), "orphan-file", null, new FileInfo(file).Length));
                        }
                    }
                }
            }

            check.Status = IntegrityCheckStatus.Completed;
            check.AssetCount = assetCount;
            check.MissingCount = missing;
            check.OrphanCount = orphanCount;
            check.DetailsJson = JsonSerializer.Serialize(issues.Take(500));
            check.CompletedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);

            if (missing + hashMismatch + sizeMismatch + orphanCount > 0)
            {
                db.SystemEvents.Add(new SystemEvent
                {
                    Level = "Warning",
                    Category = "Storage",
                    Message = $"Integrity scan found {missing} missing, {hashMismatch} hash mismatch, {sizeMismatch} size mismatch and {orphanCount} orphan file(s).",
                    DataJson = JsonSerializer.Serialize(new { checkId = check.Id })
                });
                await db.SaveChangesAsync(ct);
            }

            logger.LogInformation("Integrity scan {CheckId} completed for {Assets} assets", check.Id, assetCount);
            return check.Id;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            check.Status = IntegrityCheckStatus.Failed;
            check.CompletedAt = DateTimeOffset.UtcNow;
            check.DetailsJson = JsonSerializer.Serialize(new { error = ex.Message });
            await db.SaveChangesAsync(CancellationToken.None);
            logger.LogError(ex, "Integrity scan {CheckId} failed", check.Id);
            throw;
        }
    }
}
