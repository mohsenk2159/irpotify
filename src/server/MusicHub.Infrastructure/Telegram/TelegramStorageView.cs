namespace MusicHub.Infrastructure.Telegram;

/// <summary>One library folder as the storage screen shows it.</summary>
public sealed record TelegramStorageArea(string Name, string Summary);

/// <summary>
/// Everything the storage screen renders. It is built by the handler from the database and the
/// filesystem, so the text layer stays free of I/O and can be tested on its own.
/// </summary>
/// <param name="LibraryPath">The folder the library currently lives in.</param>
/// <param name="Relocated">True when an administrator moved it, false when it is the data folder.</param>
/// <param name="DataRoot">The fixed data folder, shown so the two roots are never confused.</param>
/// <param name="PointerError">Why the pointer is unusable, when it is.</param>
/// <param name="AssetCount">Rows in the database that describe stored files.</param>
/// <param name="AssetBytes">Total size recorded for those rows.</param>
/// <param name="Areas">Per-folder usage.</param>
/// <param name="MoveActive">Whether a move is queued or running.</param>
/// <param name="MoveSummary">Progress or the failure reason of the last move.</param>
/// <param name="CandidatePath">A path that was just checked, if any.</param>
/// <param name="CandidateAccepted">Whether that path passed and the move was queued.</param>
/// <param name="CandidateError">Why that path was refused.</param>
public sealed record TelegramStorageView(
    string LibraryPath,
    bool Relocated,
    string DataRoot,
    string? PointerError,
    int AssetCount,
    long AssetBytes,
    IReadOnlyList<TelegramStorageArea> Areas,
    bool MoveActive,
    string? MoveSummary,
    string? CandidatePath = null,
    bool CandidateAccepted = false,
    string? CandidateError = null);
