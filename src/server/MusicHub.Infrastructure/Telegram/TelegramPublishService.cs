using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MusicHub.Domain;
using MusicHub.Infrastructure.Media;

namespace MusicHub.Infrastructure.Telegram;

/// <summary>
/// Durable Telegram publication worker. Reads pending rows from the database, sends the
/// stored audio file, records the resulting message id and applies bounded exponential backoff.
/// </summary>
public sealed class TelegramPublishService(
    MusicHubDbContext db,
    TelegramBotClient client,
    TelegramSecretProtector protector,
    ContentAddressedStorage storage,
    ILogger<TelegramPublishService> logger)
{
    public async Task<int> PublishPendingAsync(CancellationToken ct)
    {
        var config = await db.TelegramConfigurations.AsNoTracking().OrderBy(x => x.UpdatedAt).FirstOrDefaultAsync(ct);
        if (config is null || config.DestinationGroupId is null) return 0;
        var token = protector.Unprotect(config.BotTokenProtected);
        if (string.IsNullOrWhiteSpace(token)) return 0;

        var now = DateTimeOffset.UtcNow;
        var batch = await db.TelegramPublications
            .Where(x => (x.Status == TelegramPublishStatus.Pending || x.Status == TelegramPublishStatus.Publishing)
                        && x.NextAttemptAt <= now)
            .OrderBy(x => x.NextAttemptAt)
            .Take(5)
            .ToListAsync(ct);

        var completed = 0;
        foreach (var publication in batch)
        {
            var track = await db.Tracks.AsNoTracking()
                .Include(x => x.AudioAsset).ThenInclude(x => x.FileHash)
                .SingleOrDefaultAsync(x => x.Id == publication.TrackId && !x.IsDeleted, ct);
            if (track is null)
            {
                publication.Status = TelegramPublishStatus.Failed;
                publication.LastError = "The track no longer exists.";
                publication.UpdatedAt = DateTimeOffset.UtcNow;
                continue;
            }

            var chatId = publication.ChatId ?? config.DestinationGroupId!.Value;
            var apiBase = TelegramBotClient.NormalizeApiBase(
                !string.IsNullOrWhiteSpace(config.LocalBotApiEndpoint) ? config.LocalBotApiEndpoint : config.ApiEndpoint);

            try
            {
                var absolute = storage.ResolveMusicPath(track.AudioAsset.StoragePath);
                if (!File.Exists(absolute))
                {
                    publication.Status = TelegramPublishStatus.Failed;
                    publication.LastError = "The stored audio file is missing.";
                    publication.UpdatedAt = DateTimeOffset.UtcNow;
                    continue;
                }
                var duration = (int)Math.Round(track.DurationSeconds);
                var caption = string.IsNullOrWhiteSpace(track.AlbumDisplay)
                    ? track.ArtistDisplay
                    : $"{track.ArtistDisplay} — {track.AlbumDisplay}";
                var messageId = await client.SendAudioAsync(
                    token,
                    apiBase,
                    chatId,
                    config.DestinationTopicId,
                    absolute,
                    Path.GetFileName(track.AudioAsset.StoragePath),
                    track.Title,
                    track.ArtistDisplay,
                    duration,
                    caption,
                    ct);

                publication.Status = TelegramPublishStatus.Published;
                publication.TelegramMessageId = messageId;
                publication.ChatId = chatId;
                publication.LastError = null;
                publication.UpdatedAt = DateTimeOffset.UtcNow;
                db.TelegramMessageMappings.Add(new TelegramMessageMapping
                {
                    TrackId = track.Id,
                    ChatId = chatId,
                    MessageId = messageId
                });
                completed++;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                publication.Attempts++;
                publication.LastError = ex.Message.Length > 1000 ? ex.Message[..1000] : ex.Message;
                publication.UpdatedAt = DateTimeOffset.UtcNow;
                var maxAttempts = Math.Clamp(config.MaxRetryAttempts, 1, 20);
                if (publication.Attempts >= maxAttempts)
                {
                    publication.Status = TelegramPublishStatus.Failed;
                }
                else
                {
                    publication.Status = TelegramPublishStatus.Publishing;
                    var delayMinutes = Math.Min(60, Math.Pow(2, publication.Attempts));
                    publication.NextAttemptAt = DateTimeOffset.UtcNow.AddMinutes(delayMinutes);
                }
                logger.LogWarning("Telegram publication {PublicationId} failed on attempt {Attempts}: {Message}",
                    publication.Id, publication.Attempts, ex.Message);
            }
        }

        if (batch.Count > 0) await db.SaveChangesAsync(ct);
        return completed;
    }
}
