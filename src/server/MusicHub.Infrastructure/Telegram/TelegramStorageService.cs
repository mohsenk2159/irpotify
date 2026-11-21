using Microsoft.EntityFrameworkCore;
using MusicHub.Domain;
using MusicHub.Infrastructure.Storage;

namespace MusicHub.Infrastructure.Telegram;

/// <summary>
/// Builds the storage screen's data for the bot: where the library is, how big it is, and whether a
/// candidate path may be used.
/// </summary>
/// <remarks>
/// The filesystem reading and the database query live here rather than in the text formatter, so the
/// formatting can be tested without a database and the I/O has exactly one implementation that the
/// bot and any future screen share.
/// </remarks>
public sealed class TelegramStorageService(
    MusicHubDbContext db,
    StorageAdminService storage)
{
    private static readonly string[] Areas = ["music", "artwork", "quarantine"];

    public async Task<TelegramStorageView> ReadAsync(CancellationToken ct)
    {
        var locations = storage.Locations;
        var libraryRoot = locations.LibraryRoot;

        var assetCount = await db.FileHashes.CountAsync(ct);
        var assetBytes = await db.FileHashes.SumAsync(x => (long?)x.SizeBytes, ct) ?? 0;
        var relocation = await storage.ReadRelocationAsync(ct);

        return new TelegramStorageView(
            libraryRoot,
            locations.Source == "relocated",
            locations.DataRoot,
            locations.PointerError,
            assetCount,
            assetBytes,
            Areas.Select(name => Describe(name, Path.Combine(libraryRoot, name))).ToList(),
            relocation.Active,
            Summarize(relocation));
    }

    /// <summary>
    /// Runs the same validator the HTTP endpoint runs. It is deliberately not a second copy of the
    /// rules: a path that the bot accepts and the dashboard refuses would be a real defect.
    /// </summary>
    public Task<IReadOnlyList<StorageCheck>> ValidateAsync(string path, CancellationToken ct)
    {
        var locations = storage.Locations;
        return Task.FromResult(StorageLocationValidator.Validate(
            path, locations.LibraryRoot, locations.DataRoot, requireEmpty: true));
    }

    private static TelegramStorageArea Describe(string name, string path)
    {
        if (!Directory.Exists(path))
        {
            return new TelegramStorageArea(name, "not created yet");
        }

        try
        {
            long bytes = 0;
            var files = 0;
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                bytes += new FileInfo(file).Length;
                files++;
            }
            return new TelegramStorageArea(name,
                $"{files} file(s), {TelegramBotTexts.FormatBytes(bytes)}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new TelegramStorageArea(name, "could not be measured");
        }
    }

    /// <summary>
    /// One line describing the last move. It reports the failure reason when there is one, because a
    /// silent failure on this screen would read as "nothing happened".
    /// </summary>
    private string? Summarize(StorageRelocationSnapshot snapshot)
    {
        var progress = snapshot.Progress;
        if (progress is not null)
        {
            return progress.Phase switch
            {
                LibraryRelocationState.PhaseCompleted =>
                    $"{progress.FilesCopied} file(s), {TelegramBotTexts.FormatBytes(progress.BytesCopied)} — "
                    + TelegramBotTexts.Get(TelegramBotTextIds.StorageMoveCompleted),
                LibraryRelocationState.PhaseFailed =>
                    TelegramBotTexts.Get(TelegramBotTextIds.StorageMoveFailed) + ": "
                    + (progress.Error ?? "see the server log"),
                LibraryRelocationState.PhaseCopying =>
                    $"copying {progress.FilesCopied} of {progress.FilesTotal} file(s)",
                LibraryRelocationState.PhaseVerifying =>
                    $"verifying {progress.FilesTotal} file(s)",
                LibraryRelocationState.PhaseSwitching => "switching the location",
                _ => progress.Phase
            };
        }

        if (snapshot.Job?.Error is { Length: > 0 } jobError)
        {
            return TelegramBotTexts.Get(TelegramBotTextIds.StorageMoveFailed) + ": " + jobError;
        }

        if (snapshot.Job is { Status: "Completed" })
        {
            return TelegramBotTexts.Get(TelegramBotTextIds.StorageMoveCompleted);
        }

        return null;
    }
}
