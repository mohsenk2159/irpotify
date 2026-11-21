using Microsoft.EntityFrameworkCore;
using MusicHub.Domain;

namespace MusicHub.Infrastructure.Telegram;

/// <summary>A read-only snapshot of the values the bot's status screen shows.</summary>
public sealed record TelegramBotStatus(
    long UpdateOffset,
    int ImportCount,
    string? LastImportResult,
    DateTimeOffset? LastImportAt,
    int UnpublishedPublications,
    string? WorkerHeartbeat);

/// <summary>
/// Computes the status screen from the database. Nothing here is cached or hardcoded: the offset,
/// the import counters, the publication backlog and the worker heartbeat are read on every render,
/// so the refresh button re-reads real state instead of replaying a snapshot.
/// </summary>
public sealed class TelegramBotStatusService(MusicHubDbContext db)
{
    public const string OffsetSettingKey = "telegram.updateOffset";
    public const string HeartbeatSettingKey = "worker.heartbeat";

    public async Task<TelegramBotStatus> ReadAsync(CancellationToken ct)
    {
        var offsetValue = await db.AppSettings.AsNoTracking()
            .Where(x => x.Key == OffsetSettingKey)
            .Select(x => x.Value)
            .SingleOrDefaultAsync(ct);
        var offset = long.TryParse(offsetValue, out var parsedOffset) ? parsedOffset : 0L;

        var importCount = await db.TelegramImports.AsNoTracking().CountAsync(ct);
        var lastImport = await db.TelegramImports.AsNoTracking()
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new { x.Status, x.CreatedAt })
            .FirstOrDefaultAsync(ct);

        // TelegramPublishStatus has no "Completed": a publication is finished only when it is
        // Published. Pending, Publishing, Failed and DeadLetter are all unfinished work.
        var unpublished = await db.TelegramPublications.AsNoTracking()
            .CountAsync(x => x.Status != TelegramPublishStatus.Published, ct);

        var heartbeat = await db.AppSettings.AsNoTracking()
            .Where(x => x.Key == HeartbeatSettingKey)
            .Select(x => x.Value)
            .SingleOrDefaultAsync(ct);

        return new TelegramBotStatus(
            offset,
            importCount,
            lastImport?.Status.ToString(),
            lastImport?.CreatedAt,
            unpublished,
            string.IsNullOrWhiteSpace(heartbeat) ? null : heartbeat);
    }
}
