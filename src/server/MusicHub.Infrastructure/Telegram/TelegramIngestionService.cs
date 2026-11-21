using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MusicHub.Domain;
using MusicHub.Infrastructure.Library;
using MusicHub.Infrastructure.Media;

namespace MusicHub.Infrastructure.Telegram;

public sealed class TelegramIngestionResult(int updatesProcessed, int imported, int duplicates, int rejected, string? note)
{
    public int UpdatesProcessed { get; } = updatesProcessed;
    public int Imported { get; } = imported;
    public int Duplicates { get; } = duplicates;
    public int Rejected { get; } = rejected;
    public string? Note { get; } = note;
    public bool Any => UpdatesProcessed > 0;
}

/// <summary>
/// Admin-only Telegram ingestion. Every message is authorized against the configured admin
/// identity before any file metadata is trusted, and the file is only downloaded after the
/// declared type and size pass strict validation.
/// </summary>
public sealed class TelegramIngestionService(
    MusicHubDbContext db,
    TelegramBotClient client,
    TelegramSecretProtector protector,
    TrackImportService importer,
    TelegramUpdateRouter router,
    TelegramAlbumPromptStore albumPrompts,
    AlbumService albums,
    ILogger<TelegramIngestionService> logger)
{
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".flac", ".wav", ".m4a", ".aac", ".ogg", ".opus"
    };

    private const string OffsetSettingKey = "telegram.updateOffset";

    public async Task<TelegramIngestionResult> PollOnceAsync(CancellationToken ct)
    {
        var config = await db.TelegramConfigurations.AsNoTracking().OrderBy(x => x.UpdatedAt).FirstOrDefaultAsync(ct);
        if (config is null || !config.IngestionEnabled)
        {
            return new TelegramIngestionResult(0, 0, 0, 0, "Telegram ingestion is disabled or not configured.");
        }
        if (config.AdminTelegramUserId is null)
        {
            return new TelegramIngestionResult(0, 0, 0, 0, "No admin Telegram user is configured; ingestion is blocked.");
        }

        var token = protector.Unprotect(config.BotTokenProtected);
        if (string.IsNullOrWhiteSpace(token))
        {
            return new TelegramIngestionResult(0, 0, 0, 0, "The stored bot token could not be decrypted. Re-save the token.");
        }

        var offsetRow = await db.AppSettings.SingleOrDefaultAsync(x => x.Key == OffsetSettingKey, ct);
        var offset = long.TryParse(offsetRow?.Value, out var parsedOffset) ? parsedOffset : 0L;

        IReadOnlyList<JsonDocument> updates;
        try
        {
            updates = await client.GetUpdatesAsync(token, ApiBase(config), offset, 20, ct);
        }
        catch (TelegramBotException ex) when (ex.ErrorCode == 409)
        {
            logger.LogWarning("Telegram polling conflict: another process is already polling this bot (webhook or second worker).");
            return new TelegramIngestionResult(0, 0, 0, 0, "Polling conflict: another client is consuming updates.");
        }

        var imported = 0;
        var duplicates = 0;
        var rejected = 0;
        var processed = 0;
        var newOffset = offset;

        foreach (var update in updates)
        {
            using (update)
            {
                var updateId = update.RootElement.TryGetProperty("update_id", out var idElement) ? idElement.GetInt64() : 0;
                newOffset = Math.Max(newOffset, updateId + 1);
                if (updateId <= 0) continue;
                if (await db.TelegramUpdates.AnyAsync(x => x.TelegramUpdateId == updateId, ct)) continue;

                var payload = update.RootElement.GetRawText();
                var record = new TelegramUpdate
                {
                    TelegramUpdateId = updateId,
                    PayloadJson = payload.Length > 64 * 1024 ? payload[..(64 * 1024)] : payload
                };
                db.TelegramUpdates.Add(record);
                processed++;

                try
                {
                    // Classification decides the dispatcher, not a second copy of the rules. A
                    // message update - audio, document, or anything else - goes to the unchanged
                    // ingestion handler below; commands and callback queries go to the bot router.
                    var kind = TelegramUpdateRouter.Classify(update.RootElement);
                    if (kind == TelegramUpdateKind.Message)
                    {
                        var outcome = await HandleUpdateAsync(token, config, update.RootElement, updateId, ct);
                        record.ProcessedAt = DateTimeOffset.UtcNow;
                        if (outcome == IngestOutcome.Imported) imported++;
                        else if (outcome == IngestOutcome.Duplicate) duplicates++;
                        else rejected++;
                    }
                    else
                    {
                        var routed = await router.RouteNonMediaAsync(update.RootElement, updateId, ct);
                        record.ProcessedAt = DateTimeOffset.UtcNow;
                        if (routed == TelegramRouteOutcome.Rejected) rejected++;
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    rejected++;
                    record.Error = Trim(ex.Message, 1000);
                    logger.LogWarning(ex, "Telegram update {UpdateId} failed", updateId);
                }

                await db.SaveChangesAsync(ct);
            }
        }

        // Persist the offset once per poll, covering every branch, instead of only inside the loop
        // body. The previous shape wrote it after handling a *new* update, so a batch made up
        // entirely of already-seen updates (the `continue` above) never advanced it: the same
        // updates were fetched forever and ingestion stalled permanently with no error, while
        // genuinely new messages sat behind them undelivered (CR-007).
        if (newOffset != offset || offsetRow is null)
        {
            await UpsertOffsetAsync(newOffset, ct);
            await db.SaveChangesAsync(ct);
        }

        return new TelegramIngestionResult(processed, imported, duplicates, rejected, null);
    }

    private async Task<IngestOutcome> HandleUpdateAsync(string token, TelegramConfiguration config, JsonElement update, long updateId, CancellationToken ct)
    {
        if (!update.TryGetProperty("message", out var message) || message.ValueKind != JsonValueKind.Object)
        {
            return IngestOutcome.Rejected;
        }

        var chatId = message.TryGetProperty("chat", out var chat) && chat.TryGetProperty("id", out var chatIdElement)
            ? chatIdElement.GetInt64() : 0;
        var senderId = message.TryGetProperty("from", out var from) && from.TryGetProperty("id", out var fromIdElement)
            ? fromIdElement.GetInt64() : 0;
        var threadId = message.TryGetProperty("message_thread_id", out var thread) && thread.TryGetInt64(out var parsedThread)
            ? (long?)parsedThread : null;

        if (senderId == 0 || senderId != config.AdminTelegramUserId)
        {
            logger.LogWarning("Rejected Telegram message from unauthorized user {SenderId} in chat {ChatId}.", senderId, chatId);
            db.TelegramImports.Add(new TelegramImport
            {
                TelegramUpdateId = updateId,
                TelegramUserId = senderId,
                ChatId = chatId,
                Status = JobStatus.Failed,
                Error = "Sender is not the configured Telegram administrator.",
                CompletedAt = DateTimeOffset.UtcNow
            });
            return IngestOutcome.Rejected;
        }
        if (config.AdminChatId is not null && chatId != config.AdminChatId)
        {
            logger.LogWarning("Rejected Telegram message from admin in unexpected chat {ChatId}.", chatId);
            db.TelegramImports.Add(new TelegramImport
            {
                TelegramUpdateId = updateId,
                TelegramUserId = senderId,
                ChatId = chatId,
                Status = JobStatus.Failed,
                Error = "Chat is not the configured administrator chat.",
                CompletedAt = DateTimeOffset.UtcNow
            });
            return IngestOutcome.Rejected;
        }

        var media = ExtractAudio(message);
        if (media is null)
        {
            // A plain text message is normally a rejection, but one case is a legitimate answer:
            // the operator typed the name of a new album after tapping "Create a new album". That
            // is checked first, and only for the administrator, because the gate above has already
            // established that this sender is the configured admin.
            if (await TryHandleAlbumNameAsync(token, config, chatId, threadId, message, ct))
            {
                return IngestOutcome.Imported;
            }

            await SafeReplyAsync(token, config, chatId, threadId, "Only audio documents are accepted (MP3, FLAC, WAV, M4A, AAC, OGG, OPUS).", ct);
            db.TelegramImports.Add(new TelegramImport
            {
                TelegramUpdateId = updateId,
                TelegramUserId = senderId,
                ChatId = chatId,
                Status = JobStatus.Failed,
                Error = "Message did not contain a supported audio document.",
                CompletedAt = DateTimeOffset.UtcNow
            });
            return IngestOutcome.Rejected;
        }

        if (media.FileSize > config.MaxIncomingFileSizeBytes)
        {
            await SafeReplyAsync(token, config, chatId, threadId, $"Rejected: file exceeds the configured limit of {config.MaxIncomingFileSizeBytes} bytes.", ct);
            return Reject(updateId, senderId, chatId, media, "File exceeds the configured maximum size.");
        }
        if (media.DurationSeconds is > 0 && media.DurationSeconds > config.MaxAudioDurationSeconds)
        {
            await SafeReplyAsync(token, config, chatId, threadId, $"Rejected: audio duration exceeds the configured limit of {config.MaxAudioDurationSeconds} seconds.", ct);
            return Reject(updateId, senderId, chatId, media, "Audio duration exceeds the configured maximum.");
        }
        if (!string.IsNullOrWhiteSpace(media.MimeType) && !media.MimeType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase))
        {
            await SafeReplyAsync(token, config, chatId, threadId, "Rejected: the document MIME type is not audio.", ct);
            return Reject(updateId, senderId, chatId, media, "Document MIME type is not audio.");
        }
        if (string.IsNullOrWhiteSpace(media.FileName) || !AllowedExtensions.Contains(Path.GetExtension(media.FileName)))
        {
            await SafeReplyAsync(token, config, chatId, threadId, "Rejected: unsupported audio file extension.", ct);
            return Reject(updateId, senderId, chatId, media, "Unsupported audio file extension.");
        }

        var importingUserId = await ResolveImportingUserIdAsync(config, ct);
        if (importingUserId is null)
        {
            await SafeReplyAsync(token, config, chatId, threadId, "Rejected: no active MusicHub administrator account is available for attribution.", ct);
            return Reject(updateId, senderId, chatId, media, "No active administrator account for attribution.");
        }

        var import = new TelegramImport
        {
            TelegramUpdateId = updateId,
            TelegramUserId = senderId,
            ChatId = chatId,
            FileId = media.FileId,
            FileName = media.FileName,
            Status = JobStatus.Processing
        };
        db.TelegramImports.Add(import);
        await db.SaveChangesAsync(ct);

        var tempRoot = Path.Combine(Path.GetTempPath(), "musichub-telegram");
        Directory.CreateDirectory(tempRoot);
        var tempPath = Path.Combine(tempRoot, Guid.NewGuid().ToString("N") + Path.GetExtension(media.FileName).ToLowerInvariant());

        try
        {
            TelegramFileInfo file;
            try
            {
                file = await client.GetFileAsync(token, ApiBase(config), media.FileId, ct);
            }
            catch (TelegramBotException ex)
            {
                import.Status = JobStatus.Failed;
                import.Error = Trim(ex.Message, 1000);
                import.CompletedAt = DateTimeOffset.UtcNow;
                await SafeReplyAsync(token, config, chatId, threadId, "Telegram did not return a downloadable file. Check the bot API limit and configuration.", ct);
                return IngestOutcome.Rejected;
            }

            if (file.FileSize > config.MaxIncomingFileSizeBytes)
            {
                import.Status = JobStatus.Failed;
                import.Error = "Telegram reported a file size above the configured limit.";
                import.CompletedAt = DateTimeOffset.UtcNow;
                await SafeReplyAsync(token, config, chatId, threadId, "Rejected: the Telegram file exceeds the configured size limit.", ct);
                return IngestOutcome.Rejected;
            }

            long downloaded;
            await using (var destination = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous))
            {
                downloaded = await client.DownloadFileAsync(token, ApiBase(config), file.FilePath, destination, config.MaxIncomingFileSizeBytes, ct);
            }

            await using var source = new FileStream(tempPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
            var result = await importer.ImportAsync(new TrackImportRequest(
                source,
                media.FileName,
                downloaded,
                importingUserId.Value,
                null,
                null,
                "telegram",
                media.FileId,
                config.MaxIncomingFileSizeBytes), ct);

            import.Status = result.Success ? JobStatus.Completed : JobStatus.Failed;
            import.TrackId = result.TrackId;
            import.Error = result.Error is null ? null : Trim(result.Error, 1000);
            import.CompletedAt = DateTimeOffset.UtcNow;

            if (result.Success && result.Duplicate)
            {
                await SafeReplyAsync(token, config, chatId, threadId, "This audio already exists in the library (duplicate SHA-256). No new copy was stored.", ct);
                return IngestOutcome.Duplicate;
            }
            if (result.Success)
            {
                // Every file gets its own album question, keyed by the id of the message that asks
                // it. That is what makes "ask separately for each file" true even when several
                // tracks are forwarded in a row: the chat session holds a single state, so keying
                // by message is the only way each open question keeps its own track.
                var asked = await TryAskAlbumAsync(
                    token, config, chatId, threadId, result.TrackId, media.FileName, ct);
                if (!asked)
                {
                    // The track is already imported; a failed question must not turn that into a
                    // failure. The operator still learns the file was accepted.
                    await SafeReplyAsync(token, config, chatId, threadId, "Audio validated and added to the library.", ct);
                }
                return IngestOutcome.Imported;
            }

            await SafeReplyAsync(token, config, chatId, threadId, "Rejected: " + (result.Error ?? "the audio failed validation."), ct);
            return IngestOutcome.Rejected;
        }
        finally
        {
            try
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
            catch (IOException)
            {
                logger.LogDebug("Temporary Telegram download could not be removed immediately.");
            }
        }
    }

    private IngestOutcome Reject(long updateId, long senderId, long chatId, AudioCandidate media, string error)
    {
        db.TelegramImports.Add(new TelegramImport
        {
            TelegramUpdateId = updateId,
            TelegramUserId = senderId,
            ChatId = chatId,
            FileId = media.FileId,
            FileName = media.FileName,
            Status = JobStatus.Failed,
            Error = error,
            CompletedAt = DateTimeOffset.UtcNow
        });
        return IngestOutcome.Rejected;
    }

    private async Task<Guid?> ResolveImportingUserIdAsync(TelegramConfiguration config, CancellationToken ct)
    {
        if (config.UpdatedByUserId is not null)
        {
            var active = await db.Users.AsNoTracking().Where(x => x.Id == config.UpdatedByUserId && x.IsActive)
                .Select(x => (Guid?)x.Id).SingleOrDefaultAsync(ct);
            if (active is not null) return active;
        }
        return await db.UserRoles.AsNoTracking()
            .Where(x => x.Role.Name == RoleNames.Administrator && x.User.IsActive)
            .OrderBy(x => x.User.CreatedAt)
            .Select(x => (Guid?)x.UserId)
            .FirstOrDefaultAsync(ct);
    }

    private async Task SafeReplyAsync(string token, TelegramConfiguration config, long chatId, long? threadId, string text, CancellationToken ct)
    {
        try
        {
            await client.SendMessageAsync(token, ApiBase(config), chatId, threadId, text, ct);
        }
        catch (Exception ex) when (ex is TelegramBotException or HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning("Telegram reply could not be delivered: {Message}", ex.Message);
        }
    }

    /// <summary>
    /// Handles a typed album name for the "create a new album" step.
    /// </summary>
    /// <remarks>
    /// Returns true when the message was an album name and has been fully handled, so the caller does
    /// not also send the audio-only rejection. Returns false when there is no prompt waiting for a
    /// name, in which case the message is ordinary text and keeps the frozen rejection.
    /// <para>
    /// Commands are excluded here: <c>/start</c> and friends were already routed to the bot handler,
    /// and treating one as an album name would create junk albums.
    /// </para>
    /// </remarks>
    private async Task<bool> TryHandleAlbumNameAsync(
        string token,
        TelegramConfiguration config,
        long chatId,
        long? threadId,
        JsonElement message,
        CancellationToken ct)
    {
        if (!message.TryGetProperty("text", out var textElement) || textElement.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var text = textElement.GetString();
        if (string.IsNullOrWhiteSpace(text)) return false;
        if (TelegramBotCommand.TryParse(text, out _)) return false;

        var pending = await albumPrompts.LoadAwaitingNameAsync(chatId, ct);
        if (pending is null) return false;

        var (promptMessageId, prompt) = pending.Value;
        var name = text.Trim();

        if (name.Length > AlbumService.MaxTitleLength)
        {
            await SafeReplyAsync(token, config, chatId, threadId,
                TelegramBotTexts.Get(TelegramBotTextIds.AlbumNameTooLong), ct);
            return true;
        }

        // Reuse an existing album with the same name instead of creating a near-duplicate; the
        // operator typing a name that already exists almost always means that album.
        var existing = await albums.FindByTitleAsync(name, ct);
        var created = existing is null
            ? await albums.CreateAsync(name, null, null, null, null, null, ct)
            : AlbumOperationResult.Ok(existing.Id, existing.Title);

        if (!created.Succeeded)
        {
            await SafeReplyAsync(token, config, chatId, threadId,
                TelegramBotTexts.FormatAlbumFailed(created.Error ?? string.Empty), ct);
            await albumPrompts.DeleteAsync(chatId, promptMessageId, ct);
            return true;
        }

        var assigned = await albums.AssignTrackAsync(created.AlbumId!.Value, prompt.TrackId, null, ct);
        await albumPrompts.DeleteAsync(chatId, promptMessageId, ct);

        if (!assigned.Succeeded)
        {
            // The album exists but the track is gone: it was deleted between the prompt and this
            // reply. Say so plainly instead of reporting a generic failure.
            var reason = assigned.Error is not null
                         && assigned.Error.Contains("track", StringComparison.OrdinalIgnoreCase)
                ? TelegramBotTexts.Get(TelegramBotTextIds.AlbumTrackGone)
                : TelegramBotTexts.FormatAlbumFailed(assigned.Error ?? string.Empty);
            await SafeReplyAsync(token, config, chatId, threadId, reason, ct);
            return true;
        }

        // Audit the two writes this typed name performed. The sender is the configured admin: a
        // non-admin message was rejected by the gate long before this method could run.
        if (config.AdminTelegramUserId is { } auditUserId)
        {
            if (existing is null)
            {
                await TelegramAudit.RecordAsync(db, auditUserId, chatId,
                    "album.create", "Album", created.AlbumId!.Value.ToString("N"),
                    new { albumId = created.AlbumId, title = created.AlbumTitle }, ct);
            }
            await TelegramAudit.RecordAsync(db, auditUserId, chatId,
                "album.track.assign", "AlbumTrack", created.AlbumId!.Value.ToString("N"),
                new { albumId = created.AlbumId, trackId = prompt.TrackId, albumTitle = created.AlbumTitle }, ct);
        }

        var reply = existing is null
            ? TelegramBotTexts.FormatAlbumCreated(created.AlbumTitle ?? name)
            : string.Empty;
        var filed = TelegramBotTexts.FormatAlbumAssigned(prompt.TrackTitle, assigned.AlbumTitle ?? name);
        await SafeReplyAsync(token, config, chatId, threadId,
            reply.Length == 0 ? filed : reply + "\n" + filed, ct);
        return true;
    }

    /// <summary>
    /// Asks which album a just-imported track belongs to, as its own message.
    /// </summary>
    /// <remarks>
    /// Returns false when the question could not be delivered, so the caller can fall back to the
    /// plain acceptance reply. The prompt row is only written after Telegram reported a message id,
    /// because a prompt keyed to message 0 could never be matched by a button tap and would be an
    /// unanswerable question.
    /// </remarks>
    private async Task<bool> TryAskAlbumAsync(
        string token,
        TelegramConfiguration config,
        long chatId,
        long? threadId,
        Guid? trackId,
        string fileName,
        CancellationToken ct)
    {
        if (trackId is not { } id) return false;

        try
        {
            var title = await db.Tracks.AsNoTracking()
                .Where(x => x.Id == id)
                .Select(x => x.Title)
                .SingleOrDefaultAsync(ct);
            if (string.IsNullOrWhiteSpace(title)) return false;

            var messageId = await client.SendMessageReturningIdAsync(
                token, ApiBase(config), chatId, threadId,
                TelegramBotTexts.FormatAlbumPrompt(fileName),
                TelegramKeyboard.AlbumPrompt(),
                ct);
            if (messageId == 0)
            {
                logger.LogWarning("Telegram accepted the album question but returned no message id; the question cannot be answered.");
                return false;
            }

            await albumPrompts.SaveAsync(chatId, messageId, id, title, fileName, awaitingName: false, ct);
            return true;
        }
        catch (Exception ex) when (ex is TelegramBotException or HttpRequestException or TaskCanceledException or DbUpdateException)
        {
            logger.LogWarning("The album question could not be asked: {Message}", ex.Message);
            return false;
        }
    }

    private async Task UpsertOffsetAsync(long offset, CancellationToken ct)
    {
        var row = await db.AppSettings.SingleOrDefaultAsync(x => x.Key == OffsetSettingKey, ct);
        if (row is null)
        {
            db.AppSettings.Add(new AppSetting { Key = OffsetSettingKey, Value = offset.ToString(), UpdatedAt = DateTimeOffset.UtcNow });
        }
        else
        {
            row.Value = offset.ToString();
            row.UpdatedAt = DateTimeOffset.UtcNow;
        }
    }

    private static string ApiBase(TelegramConfiguration config) => TelegramBotClient.NormalizeApiBase(
        !string.IsNullOrWhiteSpace(config.LocalBotApiEndpoint) ? config.LocalBotApiEndpoint : config.ApiEndpoint);

    private static AudioCandidate? ExtractAudio(JsonElement message)
    {
        if (message.TryGetProperty("audio", out var audio) && audio.ValueKind == JsonValueKind.Object)
        {
            var name = audio.TryGetProperty("file_name", out var audioName) ? audioName.GetString() : null;
            return new AudioCandidate(
                audio.TryGetProperty("file_id", out var audioId) ? audioId.GetString() ?? string.Empty : string.Empty,
                name,
                audio.TryGetProperty("mime_type", out var mime) ? mime.GetString() : null,
                audio.TryGetProperty("file_size", out var size) && size.TryGetInt64(out var parsedSize) ? parsedSize : 0,
                audio.TryGetProperty("duration", out var duration) && duration.TryGetInt32(out var parsedDuration) ? parsedDuration : null);
        }
        if (message.TryGetProperty("document", out var document) && document.ValueKind == JsonValueKind.Object)
        {
            var name = document.TryGetProperty("file_name", out var documentName) ? documentName.GetString() : null;
            return new AudioCandidate(
                document.TryGetProperty("file_id", out var documentId) ? documentId.GetString() ?? string.Empty : string.Empty,
                name,
                document.TryGetProperty("mime_type", out var mime) ? mime.GetString() : null,
                document.TryGetProperty("file_size", out var size) && size.TryGetInt64(out var parsedSize) ? parsedSize : 0,
                null);
        }
        return null;
    }

    private static string Trim(string value, int max) => value.Length <= max ? value : value[..max];

    private sealed record AudioCandidate(string FileId, string? FileName, string? MimeType, long FileSize, int? DurationSeconds);

    private enum IngestOutcome { Imported, Duplicate, Rejected }
}

public static class TelegramImports
{
    public static bool IsSupportedExtension(string? fileName) => fileName is not null &&
        AllowedExtensionsInternal.Contains(Path.GetExtension(fileName));

    private static readonly HashSet<string> AllowedExtensionsInternal = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".flac", ".wav", ".m4a", ".aac", ".ogg", ".opus"
    };
}
