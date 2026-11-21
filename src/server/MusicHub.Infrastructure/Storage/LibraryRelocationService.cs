using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MusicHub.Domain;
using MusicHub.Infrastructure.Media;
using MusicHub.Infrastructure.Options;

namespace MusicHub.Infrastructure.Storage;

/// <summary>Progress and outcome of a library move, persisted so any host can read it.</summary>
public sealed record LibraryRelocationState(
    Guid JobId,
    string Phase,
    string Source,
    string Target,
    int FilesTotal,
    int FilesCopied,
    long BytesTotal,
    long BytesCopied,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt,
    string? Error)
{
    /// <summary>AppSettings key. One row, overwritten as the move progresses.</summary>
    public const string SettingKey = "storage.relocation.state";

    public const string PhaseCopying = "copying";
    public const string PhaseVerifying = "verifying";
    public const string PhaseSwitching = "switching";
    public const string PhaseCompleted = "completed";
    public const string PhaseFailed = "failed";
}

public sealed record LibraryRelocationResult(
    string Source,
    string Target,
    int FilesCopied,
    long BytesCopied,
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt);

/// <summary>
/// Copies the library to a new folder, verifies every byte against the database, and only then
/// points the library at the copy.
/// </summary>
/// <remarks>
/// <para>
/// The invariants, in order of importance:
/// </para>
/// <list type="number">
/// <item>Nothing is deleted or moved away from the old root. A failed or abandoned move leaves a
/// working library behind, and the operator removes the old copy when they are satisfied.</item>
/// <item>The pointer is written only after every audio asset has been found at the destination
/// with the size and SHA-256 recorded in the database. A move that cannot prove that is a failed
/// move, even if the copy "looked" fine.</item>
/// <item>The target is re-validated here, from the queued payload, because the payload is
/// untrusted input that a database row could have been edited to contain.</item>
/// </list>
/// <para>
/// The move is resumable rather than restartable: a destination file whose length and hash already
/// match the source is skipped, so re-running after a partial copy costs only what is missing.
/// </para>
/// </remarks>
public sealed class LibraryRelocationService(
    MusicHubDbContext db,
    LibraryLocationStore locations,
    IOptions<StorageOptions> options,
    ILogger<LibraryRelocationService> logger)
{
    public const string JobType = "storage.relocate";

    /// <summary>Marker that proves a non-empty target belongs to this move and not to the operator's data.</summary>
    public const string MarkerFileName = ".musichub-library.json";

    private static readonly string[] LibraryAreas = ["music", "artwork", "quarantine"];

    private readonly StorageOptions _options = options.Value;

    public async Task<LibraryRelocationResult> RunAsync(Guid jobId, string targetRoot, CancellationToken ct)
    {
        var source = locations.LibraryRoot;
        var startedAt = DateTimeOffset.UtcNow;
        try
        {
            return await MoveAsync(jobId, source, targetRoot, startedAt, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The operator has to be able to see why the move stopped, and the pointer must still
            // name the old folder. Recording the failure is the difference between "nothing
            // happened" and "you lost the library".
            await TrySaveFailureAsync(jobId, source, targetRoot, startedAt, ex, ct);
            throw;
        }
    }

    private async Task TrySaveFailureAsync(
        Guid jobId, string source, string targetRoot, DateTimeOffset startedAt, Exception error, CancellationToken ct)
    {
        try
        {
            // The request token may already be cancelled; the failure still has to be recorded.
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await SaveStateAsync(new LibraryRelocationState(jobId, LibraryRelocationState.PhaseFailed,
                source, targetRoot, 0, 0, 0, 0, startedAt, DateTimeOffset.UtcNow, error.Message), timeout.Token);
        }
        catch (Exception saveError)
        {
            logger.LogError(saveError,
                "The library move to {Target} failed and its failure state could not be recorded: {Message}",
                targetRoot, error.Message);
        }
    }

    private async Task<LibraryRelocationResult> MoveAsync(
        Guid jobId, string source, string targetRoot, DateTimeOffset startedAt, CancellationToken ct)
    {
        var target = Path.GetFullPath(targetRoot);

        // A resumed move may already hold files, so emptiness is judged by the marker below.
        var checks = StorageLocationValidator.Validate(target, source, locations.DataRoot, requireEmpty: false);
        if (checks.FirstOrDefault(x => !x.Ok) is { } failed)
        {
            throw new InvalidOperationException(
                $"The target folder is not usable ({failed.Id}): {failed.Message}");
        }

        EnsureMarker(source, target, jobId);

        var files = await EnumerateSourcesAsync(source, ct);
        var totalBytes = files.Sum(x => x.Length);
        var copied = 0;
        long copiedBytes = 0;

        await SaveStateAsync(new LibraryRelocationState(jobId, LibraryRelocationState.PhaseCopying,
            source, target, files.Count, 0, totalBytes, 0, startedAt, null, null), ct);

        var lastReport = DateTimeOffset.UtcNow;
        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();

            var destination = Path.Combine(target, file.Relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

            // Resumable: skip what is already there and identical, so a retry copies only the rest.
            var alreadyThere = File.Exists(destination)
                               && new FileInfo(destination).Length == file.Length
                               && string.Equals(
                                   await ContentAddressedStorage.ComputeFileSha256Async(destination, ct),
                                   file.Sha256,
                                   StringComparison.OrdinalIgnoreCase);

            if (!alreadyThere)
            {
                await using var input = new FileStream(file.Absolute, FileMode.Open, FileAccess.Read,
                    FileShare.Read, 1024 * 128, FileOptions.Asynchronous | FileOptions.SequentialScan);
                await using var output = new FileStream(destination, FileMode.Create, FileAccess.Write,
                    FileShare.None, 1024 * 128, FileOptions.Asynchronous | FileOptions.SequentialScan);
                await input.CopyToAsync(output, ct);
            }

            copied++;
            copiedBytes += file.Length;

            if (DateTimeOffset.UtcNow - lastReport > TimeSpan.FromSeconds(2) || copied == files.Count)
            {
                lastReport = DateTimeOffset.UtcNow;
                await SaveStateAsync(new LibraryRelocationState(jobId, LibraryRelocationState.PhaseCopying,
                    source, target, files.Count, copied, totalBytes, copiedBytes, startedAt, null, null), ct);
            }
        }

        await SaveStateAsync(new LibraryRelocationState(jobId, LibraryRelocationState.PhaseVerifying,
            source, target, files.Count, copied, totalBytes, copiedBytes, startedAt, null, null), ct);

        await VerifyAsync(target, files, ct);

        await SaveStateAsync(new LibraryRelocationState(jobId, LibraryRelocationState.PhaseSwitching,
            source, target, files.Count, copied, totalBytes, copiedBytes, startedAt, null, null), ct);

        await locations.WriteAsync(target, ct);

        var finishedAt = DateTimeOffset.UtcNow;
        await SaveStateAsync(new LibraryRelocationState(jobId, LibraryRelocationState.PhaseCompleted,
            source, target, files.Count, copied, totalBytes, copiedBytes, startedAt, finishedAt, null), ct);

        logger.LogInformation(
            "Library relocated from {Source} to {Target}: {Files} file(s), {Bytes} bytes verified. The previous copy was kept.",
            source, target, copied, copiedBytes);

        return new LibraryRelocationResult(source, target, copied, copiedBytes, startedAt, finishedAt);
    }

    /// <summary>
    /// Proves the copy is complete and correct before the pointer moves. Audio is checked against
    /// the database as well as the enumerated file list, so a track committed while the copy ran is
    /// still covered; artwork and quarantine have no database record and are checked as files.
    /// </summary>
    /// <remarks>
    /// Anything found wrong is copied again once, and then the whole verification runs again. A
    /// second failure is final: the move stops and the pointer is never written. The re-check covers
    /// every file, not only the ones that were repaired, because a repair is itself a write that
    /// could fail.
    /// </remarks>
    private async Task VerifyAsync(string target, IReadOnlyList<SourceFile> files, CancellationToken ct)
    {
        var firstPass = await FindMismatchesAsync(target, files, ct);
        if (firstPass.Count == 0) return;

        logger.LogWarning(
            "Library verification found {Count} file(s) that do not match after the copy; copying them again once.",
            firstPass.Count);

        await RepairAsync(target, firstPass, ct);

        var secondPass = await FindMismatchesAsync(target, files, ct);
        if (secondPass.Count == 0) return;

        var detail = string.Join("; ", secondPass.Take(5).Select(x => $"{x.Relative} {x.Reason}"));
        if (secondPass.Count > 5) detail += $"; and {secondPass.Count - 5} more";
        throw new InvalidOperationException(
            $"The copy could not be verified, so the library was not moved: {detail}.");
    }

    /// <summary>
    /// Returns the relative paths that are missing or do not match, comparing both the files that
    /// were copied and every audio asset the database knows about.
    /// </summary>
    private async Task<List<Mismatch>> FindMismatchesAsync(
        string target, IReadOnlyList<SourceFile> files, CancellationToken ct)
    {
        var problems = new List<Mismatch>();

        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            var path = Path.Combine(target, file.Relative.Replace('/', Path.DirectorySeparatorChar));
            var info = new FileInfo(path);
            if (!info.Exists) { problems.Add(new Mismatch(file.Relative, "is missing")); continue; }
            if (info.Length != file.Length) { problems.Add(new Mismatch(file.Relative, "has the wrong size")); continue; }
            if (!string.Equals(
                    await ContentAddressedStorage.ComputeFileSha256Async(path, ct),
                    file.Sha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                problems.Add(new Mismatch(file.Relative, "has the wrong SHA-256"));
            }
        }

        var assets = await db.AudioAssets.AsNoTracking()
            .Select(x => new { x.StoragePath, x.SizeBytes, Sha256 = x.FileHash.Sha256 })
            .ToListAsync(ct);

        foreach (var asset in assets)
        {
            ct.ThrowIfCancellationRequested();
            var path = Path.Combine(target, asset.StoragePath.Replace('/', Path.DirectorySeparatorChar));
            var info = new FileInfo(path);
            if (!info.Exists) { problems.Add(new Mismatch(asset.StoragePath, "is missing")); continue; }
            if (info.Length != asset.SizeBytes) { problems.Add(new Mismatch(asset.StoragePath, "has the wrong size")); continue; }
            if (!string.Equals(
                    await ContentAddressedStorage.ComputeFileSha256Async(path, ct),
                    asset.Sha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                problems.Add(new Mismatch(asset.StoragePath, "has the wrong SHA-256"));
            }
        }

        return problems
            .GroupBy(x => x.Relative, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.First())
            .ToList();
    }

    private Task RepairAsync(string target, IReadOnlyCollection<Mismatch> problems, CancellationToken ct)
    {
        foreach (var problem in problems)
        {
            ct.ThrowIfCancellationRequested();
            var relative = problem.Relative;
            var source = Path.Combine(locations.LibraryRoot, relative.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(source)) continue;
            var destination = Path.Combine(target, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(source, destination, overwrite: true);
        }
        logger.LogInformation("Re-copied {Count} file(s) into the new library location.", problems.Count);
        return Task.CompletedTask;
    }

    private void EnsureMarker(string source, string target, Guid jobId)
    {
        var marker = Path.Combine(target, MarkerFileName);
        if (File.Exists(marker))
        {
            var existing = JsonSerializer.Deserialize<MarkerPayload>(File.ReadAllText(marker));
            if (existing is null || !string.Equals(existing.Source, source, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "The target folder belongs to a different library. Choose an empty folder.");
            }
            return;
        }

        if (Directory.Exists(target) && Directory.EnumerateFileSystemEntries(target)
                .Any(name => !Path.GetFileName(name).StartsWith(".musichub-", StringComparison.Ordinal)))
        {
            throw new InvalidOperationException(
                "The target folder is not empty. Choose an empty folder so existing data cannot be overwritten.");
        }

        Directory.CreateDirectory(target);
        File.WriteAllText(marker, JsonSerializer.Serialize(new MarkerPayload
        {
            Source = source,
            Target = target,
            JobId = jobId,
            StartedAt = DateTimeOffset.UtcNow
        }));
    }

    /// <summary>
    /// Lists every file in the three library areas with its length and hash. Hashing here rather
    /// than during the copy lets the copy re-check an existing destination file without the source
    /// file being read twice at full speed only to find it already matches.
    /// </summary>
    private async Task<List<SourceFile>> EnumerateSourcesAsync(string root, CancellationToken ct)
    {
        var files = new List<SourceFile>();
        foreach (var area in LibraryAreas)
        {
            var directory = Path.Combine(root, area);
            if (!Directory.Exists(directory)) continue;
            foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            {
                ct.ThrowIfCancellationRequested();
                var info = new FileInfo(file);
                files.Add(new SourceFile(
                    file,
                    Path.GetRelativePath(root, file).Replace('\\', '/'),
                    info.Length,
                    await ContentAddressedStorage.ComputeFileSha256Async(file, ct)));
            }
        }
        return files;
    }

    private async Task SaveStateAsync(LibraryRelocationState state, CancellationToken ct)
    {
        var value = JsonSerializer.Serialize(state);
        var row = await db.AppSettings.SingleOrDefaultAsync(x => x.Key == LibraryRelocationState.SettingKey, ct);
        if (row is null)
        {
            db.AppSettings.Add(new AppSetting
            {
                Key = LibraryRelocationState.SettingKey,
                Value = value,
                UpdatedAt = DateTimeOffset.UtcNow
            });
        }
        else
        {
            row.Value = value;
            row.UpdatedAt = DateTimeOffset.UtcNow;
        }
        await db.SaveChangesAsync(ct);
    }

    private sealed record SourceFile(string Absolute, string Relative, long Length, string Sha256);

    /// <summary>A relative path plus why the copy of it is not acceptable.</summary>
    private sealed record Mismatch(string Relative, string Reason);

    private sealed class MarkerPayload
    {
        public string Source { get; set; } = string.Empty;
        public string Target { get; set; } = string.Empty;
        public Guid JobId { get; set; }
        public DateTimeOffset StartedAt { get; set; }
    }
}
