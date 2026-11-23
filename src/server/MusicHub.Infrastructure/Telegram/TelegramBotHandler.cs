using System.Globalization;
using Microsoft.Extensions.Logging;
using MusicHub.Infrastructure.Library;
using MusicHub.Infrastructure.Storage;

namespace MusicHub.Infrastructure.Telegram;

/// <summary>A private-message command sent to the bot.</summary>
public sealed record TelegramBotMessage(long ChatId, long SenderId, long? ThreadId, string Text);

/// <summary>An inline-keyboard tap. <see cref="Data"/> is untrusted input.</summary>
public sealed record TelegramBotCallback(string CallbackQueryId, long ChatId, long SenderId, long MessageId, string? Data);

/// <summary>Persisted state names. Kept small and stable: they are part of the stored JSON.</summary>
public static class TelegramBotStates
{
    public const string Status = "status";
    public const string Storage = "storage";
    /// <summary>A path that passed every check and waits for the operator to confirm the move.</summary>
    public const string StorageConfirm = "storage.confirm";
    /// <summary>
    /// A track is waiting for the operator to choose its album. The prompt carries the track id in
    /// its payload, so several files sent in a row each keep their own question.
    /// </summary>
    public const string AlbumAssign = "album.assign";
    /// <summary>The operator asked for a new album and the next text message is its name.</summary>
    public const string AlbumNewName = "album.newname";
}

/// <summary>The session payload keys the bot stores.</summary>
public static class TelegramBotPayloadKeys
{
    public const string Path = "path";

    /// <summary>The track a pending album question is about.</summary>
    public const string TrackId = "trackId";

    /// <summary>
    /// The prompt message id. A callback arrives with the id of the message it was tapped on, so
    /// matching against this is what lets many per-file questions coexist: the session holds the
    /// most recent one, and an older prompt is still resolvable from the audit trail in the chat.
    /// </summary>
    public const string PromptMessageId = "promptMessageId";

    /// <summary>The track title, shown in the picker header and the confirmation.</summary>
    public const string TrackTitle = "trackTitle";

    /// <summary>The picker page, so "next" can advance without re-reading the session shape.</summary>
    public const string Page = "page";
}

/// <summary>Parses <c>/command</c> text, including the <c>@botname</c> suffix groups add.</summary>
public static class TelegramBotCommand
{
    public static bool TryParse(string? text, out string command)
    {
        command = string.Empty;
        if (!TryParseParts(text, out var parsed, out _)) return false;
        command = parsed;
        return true;
    }

    /// <summary>
    /// Parses a command and its remaining argument text. The argument is untrusted and is only ever
    /// handed to a caller that validates it; it is never logged or echoed unescaped.
    /// </summary>
    public static bool TryParseParts(string? text, out string command, out string argument)
    {
        command = string.Empty;
        argument = string.Empty;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var trimmed = text.TrimStart();
        if (trimmed.Length < 2 || trimmed[0] != '/') return false;

        var end = trimmed.IndexOfAny([' ', '\n', '\t', '\r']);
        var token = end < 0 ? trimmed : trimmed[..end];
        var mention = token.IndexOf('@');
        if (mention >= 0) token = token[..mention];

        var name = token.TrimStart('/');
        if (name.Length == 0) return false;
        if (!name.All(c => char.IsAsciiLetterOrDigit(c) || c == '_')) return false;

        command = name.ToLowerInvariant();
        argument = end < 0 ? string.Empty : trimmed[(end + 1)..].Trim();
        return true;
    }
}

/// <summary>
/// Command and callback handling for the admin bot.
/// </summary>
/// <remarks>
/// Phase 0 ships exactly one screen, and every control on it does work: the refresh button re-reads
/// the database and edits the message in place. There is no menu entry whose handler does not exist,
/// because a button that does nothing is worse than no button.
/// </remarks>
public sealed class TelegramBotHandler(
    TelegramBotClient client,
    TelegramBotStateStore sessions,
    TelegramBotStatusService statusService,
    StorageAdminService storage,
    TelegramStorageService storageView,
    TelegramAlbumPromptStore albumPrompts,
    AlbumService albums,
    LibraryReportService reports,
    TelegramAlbumEditStore edits,
    MusicHubDbContext db,
    ILogger<TelegramBotHandler> logger)
{
    public async Task<TelegramRouteOutcome> HandleCommandAsync(
        string token,
        string apiBase,
        TelegramBotMessage message,
        CancellationToken ct)
    {
        if (!TelegramBotCommand.TryParseParts(message.Text, out var command, out var argument))
        {
            await client.SendMessageAsync(token, apiBase, message.ChatId, message.ThreadId,
                TelegramBotTexts.UnknownCommandText(), null, ct);
            return TelegramRouteOutcome.Handled;
        }

        switch (command)
        {
            case "start":
            case "menu":
                await SendMenuAsync(token, apiBase, message, ct);
                return TelegramRouteOutcome.Handled;

            case "status":
                await SendStatusScreenAsync(token, apiBase, message, ct);
                return TelegramRouteOutcome.Handled;

            case "storage":
                await SendStorageScreenAsync(token, apiBase, message, argument, ct);
                return TelegramRouteOutcome.Handled;

            case "reports":
                await SendReportsScreenAsync(token, apiBase, message, ct);
                return TelegramRouteOutcome.Handled;

            case "albums":
                await SendAlbumManageScreenAsync(token, apiBase, message, 0, ct);
                return TelegramRouteOutcome.Handled;

            case "artists":
                await SendArtistListScreenAsync(token, apiBase, message, 0, ct);
                return TelegramRouteOutcome.Handled;

            case "help":
                await client.SendMessageAsync(token, apiBase, message.ChatId, message.ThreadId,
                    TelegramBotTexts.Get(TelegramBotTextIds.Help), null, ct);
                return TelegramRouteOutcome.Handled;

            case "cancel":
            {
                var cleared = await sessions.DeleteAsync(message.ChatId, ct);
                await client.SendMessageAsync(token, apiBase, message.ChatId, message.ThreadId,
                    TelegramBotTexts.Get(cleared ? TelegramBotTextIds.Cancelled : TelegramBotTextIds.CancelledNone),
                    null, ct);
                return TelegramRouteOutcome.Handled;
            }

            default:
                // /library, /lastupload and every other unbuilt command: a truthful short help reply.
                await client.SendMessageAsync(token, apiBase, message.ChatId, message.ThreadId,
                    TelegramBotTexts.UnknownCommandText(), null, ct);
                return TelegramRouteOutcome.Handled;
        }
    }

    public async Task<TelegramRouteOutcome> HandleCallbackAsync(
        string token,
        string apiBase,
        TelegramBotCallback callback,
        CancellationToken ct)
    {
        var outcome = TelegramRouteOutcome.Rejected;
        // The answer is sent from the finally block on every path, including failures: an
        // unanswered callback leaves a spinner on the operator's screen until the client gives up.
        var answer = TelegramBotTexts.Get(TelegramBotTextIds.Failure);

        try
        {
            if (callback.ChatId == 0 || callback.MessageId == 0)
            {
                logger.LogWarning("Telegram bot callback has no editable message to work on.");
                answer = TelegramBotTexts.Get(TelegramBotTextIds.UnknownAction);
                return TelegramRouteOutcome.Rejected;
            }

            if (!TelegramCallbackData.TryParse(callback.Data, out var data))
            {
                // Never echo the payload: it is untrusted and may contain anything.
                logger.LogWarning("Telegram bot callback carried malformed data of length {Length}.", callback.Data?.Length ?? 0);
                answer = TelegramBotTexts.Get(TelegramBotTextIds.UnknownAction);
                return TelegramRouteOutcome.Rejected;
            }

            switch (data.Action)
            {
                case TelegramKeyboard.RefreshAction:
                case TelegramKeyboard.HomeAction:
                {
                    // A null session covers both "nothing stored" and "expired and deleted".
                    var session = await sessions.LoadAsync(callback.ChatId, ct);

                    // Refresh is contextual: it re-reads whatever screen the chat is on, so a refresh
                    // on the storage screen does not silently jump back to the status screen.
                    if (session?.State is TelegramBotStates.Storage or TelegramBotStates.StorageConfirm)
                    {
                        var view = await storageView.ReadAsync(ct);
                        var held = CandidateFrom(session);
                        var unchanged = await TryEditAsync(token, apiBase, callback.ChatId, callback.MessageId,
                            TelegramBotTexts.FormatStorage(view with
                            {
                                CandidatePath = held,
                                CandidateAccepted = held is not null
                            }),
                            TelegramKeyboard.StorageScreen(held is not null), ct);

                        answer = unchanged
                            ? TelegramBotTexts.Get(TelegramBotTextIds.AlreadyCurrent)
                            : TelegramBotTexts.Get(TelegramBotTextIds.Refreshed);
                        outcome = TelegramRouteOutcome.Handled;
                        return outcome;
                    }

                    var snapshot = await statusService.ReadAsync(ct);
                    await sessions.SaveAsync(callback.ChatId, TelegramBotStates.Status, null, ct);
                    var statusUnchanged = await TryEditAsync(token, apiBase, callback.ChatId, callback.MessageId,
                        TelegramBotTexts.FormatStatus(snapshot), TelegramKeyboard.StatusScreen(), ct);

                    answer = statusUnchanged
                        ? TelegramBotTexts.Get(TelegramBotTextIds.AlreadyCurrent)
                        : session is null
                            ? TelegramBotTexts.Get(TelegramBotTextIds.SessionExpired)
                            : TelegramBotTexts.Get(TelegramBotTextIds.Refreshed);
                    outcome = TelegramRouteOutcome.Handled;
                    return outcome;
                }

                case TelegramKeyboard.StorageAction:
                case TelegramKeyboard.StorageRefreshAction:
                {
                    var session = await sessions.LoadAsync(callback.ChatId, ct);
                    var view = await storageView.ReadAsync(ct);
                    var candidate = CandidateFrom(session);

                    await sessions.SaveAsync(callback.ChatId,
                        candidate is null ? TelegramBotStates.Storage : TelegramBotStates.StorageConfirm,
                        candidate is null
                            ? null
                            : new Dictionary<string, string> { [TelegramBotPayloadKeys.Path] = candidate },
                        ct);

                    var unchanged = await TryEditAsync(token, apiBase, callback.ChatId, callback.MessageId,
                        TelegramBotTexts.FormatStorage(view with
                        {
                            CandidatePath = candidate,
                            CandidateAccepted = candidate is not null
                        }),
                        TelegramKeyboard.StorageScreen(candidate is not null), ct);

                    answer = unchanged
                        ? TelegramBotTexts.Get(TelegramBotTextIds.AlreadyCurrent)
                        : TelegramBotTexts.Get(TelegramBotTextIds.Refreshed);
                    outcome = TelegramRouteOutcome.Handled;
                    return outcome;
                }

                case TelegramKeyboard.StorageStartAction:
                {
                    var session = await sessions.LoadAsync(callback.ChatId, ct);
                    var candidate = CandidateFrom(session);
                    if (candidate is null)
                    {
                        // The session expired or was replaced. The move is not attempted: a path
                        // from an earlier screen must be checked again, not trusted.
                        await sessions.DeleteAsync(callback.ChatId, ct);
                        var view = await storageView.ReadAsync(ct);
                        await client.EditMessageTextAsync(token, apiBase, callback.ChatId, callback.MessageId,
                            TelegramBotTexts.FormatStorage(view), TelegramKeyboard.StorageScreen(false), ct);
                        answer = TelegramBotTexts.Get(TelegramBotTextIds.SessionExpired);
                        outcome = TelegramRouteOutcome.Handled;
                        return outcome;
                    }

                    // The shared service re-validates the path. The button is not what authorizes the
                    // move; the same rules run here as on the HTTP endpoint.
                    var queued = await storage.TryQueueRelocationAsync(candidate, ct);
                    await sessions.SaveAsync(callback.ChatId, TelegramBotStates.Storage, null, ct);
                    var after = await storageView.ReadAsync(ct);
                    await client.EditMessageTextAsync(token, apiBase, callback.ChatId, callback.MessageId,
                        TelegramBotTexts.FormatStorage(after with
                        {
                            CandidatePath = candidate,
                            CandidateAccepted = queued.Queued,
                            CandidateError = queued.Error
                        }),
                        TelegramKeyboard.StorageScreen(false), ct);

                    answer = queued.Queued
                        ? TelegramBotTexts.Get(TelegramBotTextIds.StorageQueued)
                        : queued.Error ?? TelegramBotTexts.Get(TelegramBotTextIds.UnknownAction);
                    outcome = TelegramRouteOutcome.Handled;
                    return outcome;
                }

                case TelegramKeyboard.AlbumPageAction:
                {
                    var page = int.TryParse(data.Argument, out var parsedPage) && parsedPage >= 0 ? parsedPage : 0;
                    var prompt = await albumPrompts.LoadAsync(callback.ChatId, callback.MessageId, ct);
                    if (prompt is null)
                    {
                        answer = TelegramBotTexts.Get(TelegramBotTextIds.SessionExpired);
                        outcome = TelegramRouteOutcome.Handled;
                        return outcome;
                    }

                    var total = await albums.CountAsync(ct);
                    var totalPages = Math.Max(1, (total + TelegramKeyboard.AlbumsPerPage - 1) / TelegramKeyboard.AlbumsPerPage);
                    var safePage = Math.Min(page, totalPages - 1);
                    var rows = await albums.ListAsync(
                        TelegramKeyboard.AlbumsPerPage, safePage * TelegramKeyboard.AlbumsPerPage, ct);

                    await TryEditAsync(token, apiBase, callback.ChatId, callback.MessageId,
                        TelegramBotTexts.FormatAlbumPicker(prompt.TrackTitle, total),
                        TelegramKeyboard.AlbumPicker(
                            rows.Select(x => (x.Id, x.Title)).ToList(), safePage, totalPages), ct);

                    answer = TelegramBotTexts.Get(TelegramBotTextIds.AlbumChooseExisting);
                    outcome = TelegramRouteOutcome.Handled;
                    return outcome;
                }

                case TelegramKeyboard.AlbumPickAction:
                {
                    var prompt = await albumPrompts.LoadAsync(callback.ChatId, callback.MessageId, ct);
                    if (prompt is null)
                    {
                        answer = TelegramBotTexts.Get(TelegramBotTextIds.SessionExpired);
                        outcome = TelegramRouteOutcome.Handled;
                        return outcome;
                    }

                    if (!Guid.TryParseExact(data.Argument, "N", out var albumId))
                    {
                        answer = TelegramBotTexts.Get(TelegramBotTextIds.UnknownAction);
                        outcome = TelegramRouteOutcome.Rejected;
                        return outcome;
                    }

                    // The shared service re-checks that both the album and the track still exist, so
                    // a stale button cannot resurrect a deleted album.
                    var trackIds = prompt.AllTrackIds;
                    var assignedSuccess = false;
                    var albumTitle = string.Empty;
                    foreach (var trackId in trackIds)
                    {
                        var assigned = await albums.AssignTrackAsync(albumId, trackId, null, ct);
                        if (assigned.Succeeded)
                        {
                            assignedSuccess = true;
                            albumTitle = assigned.AlbumTitle ?? albumTitle;
                            await TelegramAudit.RecordAsync(db, callback.SenderId, callback.ChatId,
                                "album.track.assign", "AlbumTrack", albumId.ToString("N"),
                                new { albumId, trackId, albumTitle = assigned.AlbumTitle }, ct);
                        }
                    }

                    await albumPrompts.DeleteAsync(callback.ChatId, callback.MessageId, ct);

                    if (!assignedSuccess)
                    {
                        await TryEditAsync(token, apiBase, callback.ChatId, callback.MessageId,
                            TelegramBotTexts.Get(TelegramBotTextIds.AlbumFailed), null, ct);
                        answer = TelegramBotTexts.Get(TelegramBotTextIds.AlbumFailed);
                        outcome = TelegramRouteOutcome.Handled;
                        return outcome;
                    }

                    var replyText = trackIds.Count > 1
                        ? TelegramBotTexts.FormatAlbumAssignedMulti(trackIds.Count, albumTitle)
                        : TelegramBotTexts.FormatAlbumAssigned(prompt.TrackTitle, albumTitle);

                    await TryEditAsync(token, apiBase, callback.ChatId, callback.MessageId, replyText, null, ct);
                    answer = replyText;
                    outcome = TelegramRouteOutcome.Handled;
                    return outcome;
                }

                case TelegramKeyboard.AlbumSkipAction:
                {
                    var prompt = await albumPrompts.LoadAsync(callback.ChatId, callback.MessageId, ct);
                    if (prompt is null)
                    {
                        answer = TelegramBotTexts.Get(TelegramBotTextIds.SessionExpired);
                        outcome = TelegramRouteOutcome.Handled;
                        return outcome;
                    }

                    await albumPrompts.DeleteAsync(callback.ChatId, callback.MessageId, ct);
                    var skipText = prompt.AllTrackIds.Count > 1
                        ? TelegramBotTexts.FormatAlbumSkippedMulti(prompt.AllTrackIds.Count)
                        : TelegramBotTexts.FormatAlbumSkipped(prompt.TrackTitle);
                    await TryEditAsync(token, apiBase, callback.ChatId, callback.MessageId,
                        skipText, null, ct);
                    answer = skipText;
                    outcome = TelegramRouteOutcome.Handled;
                    return outcome;
                }

                case TelegramKeyboard.AlbumNewAction:
                {
                    var prompt = await albumPrompts.LoadAsync(callback.ChatId, callback.MessageId, ct);
                    if (prompt is null)
                    {
                        answer = TelegramBotTexts.Get(TelegramBotTextIds.SessionExpired);
                        outcome = TelegramRouteOutcome.Handled;
                        return outcome;
                    }

                    // Flip the same prompt into "waiting for a name" instead of creating a second
                    // row, so the track being filed is still the one this message asked about.
                    await albumPrompts.SaveAsync(callback.ChatId, callback.MessageId, prompt.TrackId,
                        prompt.TrackTitle, prompt.FileName, awaitingName: true, ct, prompt.TrackIds, prompt.MediaGroupId);
                    await TryEditAsync(token, apiBase, callback.ChatId, callback.MessageId,
                        TelegramBotTexts.Get(TelegramBotTextIds.AlbumAskNewName), null, ct);
                    answer = TelegramBotTexts.Get(TelegramBotTextIds.AlbumAskNewName);
                    outcome = TelegramRouteOutcome.Handled;
                    return outcome;
                }

                case TelegramKeyboard.ReportsAction:
                {
                    var built = await BuildReportsScreenAsync(ct);
                    var unchanged = await TryEditAsync(token, apiBase, callback.ChatId, callback.MessageId,
                        built.Text, built.Keyboard, ct);
                    answer = unchanged
                        ? TelegramBotTexts.Get(TelegramBotTextIds.AlreadyCurrent)
                        : TelegramBotTexts.Get(TelegramBotTextIds.Refreshed);
                    outcome = TelegramRouteOutcome.Handled;
                    return outcome;
                }

                case TelegramKeyboard.AlbumManageAction:
                {
                    var built = await BuildAlbumManageScreenAsync(PageFrom(data.Argument), ct);
                    var unchanged = await TryEditAsync(token, apiBase, callback.ChatId, callback.MessageId,
                        built.Text, built.Keyboard, ct);
                    answer = unchanged
                        ? TelegramBotTexts.Get(TelegramBotTextIds.AlreadyCurrent)
                        : TelegramBotTexts.Get(TelegramBotTextIds.Done);
                    outcome = TelegramRouteOutcome.Handled;
                    return outcome;
                }

                case TelegramKeyboard.AlbumOpenAction:
                {
                    if (!TryParseId(data.Argument, out var albumId))
                    {
                        answer = TelegramBotTexts.Get(TelegramBotTextIds.UnknownAction);
                        outcome = TelegramRouteOutcome.Rejected;
                        return outcome;
                    }

                    var built = await BuildAlbumDetailScreenAsync(
                        callback.ChatId, callback.MessageId, albumId, 0, ct);
                    if (built is null)
                    {
                        await EditAlbumGoneAsync(token, apiBase, callback.ChatId, callback.MessageId, ct);
                        answer = TelegramBotTexts.Get(TelegramBotTextIds.AlbumGone);
                        outcome = TelegramRouteOutcome.Handled;
                        return outcome;
                    }

                    var unchanged = await TryEditAsync(token, apiBase, callback.ChatId, callback.MessageId,
                        built.Value.Text, built.Value.Keyboard, ct);
                    answer = unchanged
                        ? TelegramBotTexts.Get(TelegramBotTextIds.AlreadyCurrent)
                        : TelegramBotTexts.Get(TelegramBotTextIds.Done);
                    outcome = TelegramRouteOutcome.Handled;
                    return outcome;
                }

                case TelegramKeyboard.AlbumTrackPageAction:
                {
                    if (!TryParseIdAndPage(data.Argument, out var albumId, out var page))
                    {
                        answer = TelegramBotTexts.Get(TelegramBotTextIds.UnknownAction);
                        outcome = TelegramRouteOutcome.Rejected;
                        return outcome;
                    }

                    var built = await BuildAlbumDetailScreenAsync(
                        callback.ChatId, callback.MessageId, albumId, page, ct);
                    if (built is null)
                    {
                        await EditAlbumGoneAsync(token, apiBase, callback.ChatId, callback.MessageId, ct);
                        answer = TelegramBotTexts.Get(TelegramBotTextIds.AlbumGone);
                        outcome = TelegramRouteOutcome.Handled;
                        return outcome;
                    }

                    await TryEditAsync(token, apiBase, callback.ChatId, callback.MessageId,
                        built.Value.Text, built.Value.Keyboard, ct);
                    answer = TelegramBotTexts.Get(TelegramBotTextIds.Done);
                    outcome = TelegramRouteOutcome.Handled;
                    return outcome;
                }

                case TelegramKeyboard.AlbumRemoveAction:
                {
                    // Removing a track from an album is not destructive to the audio, but it is a
                    // change the operator can trigger by a single mis-tap, so it goes through the
                    // confirmation step the protocol requires before anything is written.
                    if (!TryParseIdAndIndex(data.Argument, out var albumId, out var index))
                    {
                        answer = TelegramBotTexts.Get(TelegramBotTextIds.UnknownAction);
                        outcome = TelegramRouteOutcome.Rejected;
                        return outcome;
                    }

                    var state = await edits.LoadAsync(callback.ChatId, callback.MessageId, ct);
                    if (state is null
                        || state.AlbumId != albumId
                        || state.Mode != TelegramAlbumEditModes.Tracks
                        || index < 0
                        || index >= state.Ids.Count)
                    {
                        answer = TelegramBotTexts.Get(TelegramBotTextIds.SessionExpired);
                        await RebuildAlbumAsync(token, apiBase, callback.ChatId, callback.MessageId,
                            albumId, state?.Page ?? 0, ct);
                        outcome = TelegramRouteOutcome.Handled;
                        return outcome;
                    }

                    var trackId = state.Ids[index];
                    var trackTitle = await albums.GetTrackTitleAsync(trackId, ct);
                    var albumTitle = await albums.GetTitleAsync(albumId, ct);
                    if (trackTitle is null || albumTitle is null)
                    {
                        answer = TelegramBotTexts.Get(TelegramBotTextIds.AlbumTrackNotInAlbum);
                        await RebuildAlbumAsync(token, apiBase, callback.ChatId, callback.MessageId,
                            albumId, state.Page, ct);
                        outcome = TelegramRouteOutcome.Handled;
                        return outcome;
                    }

                    await edits.SaveAsync(callback.ChatId, callback.MessageId, albumId,
                        TelegramAlbumEditModes.ConfirmRemove, state.Page, [trackId], ct);
                    await TryEditAsync(token, apiBase, callback.ChatId, callback.MessageId,
                        TelegramBotTexts.FormatAlbumConfirmRemove(trackTitle, albumTitle),
                        TelegramKeyboard.AlbumRemoveConfirmScreen(albumId), ct);

                    answer = TelegramBotTexts.Get(TelegramBotTextIds.Done);
                    outcome = TelegramRouteOutcome.Handled;
                    return outcome;
                }

                case TelegramKeyboard.AlbumRemoveConfirmAction:
                {
                    if (!TryParseId(data.Argument, out var albumId))
                    {
                        answer = TelegramBotTexts.Get(TelegramBotTextIds.UnknownAction);
                        outcome = TelegramRouteOutcome.Rejected;
                        return outcome;
                    }

                    var state = await edits.LoadAsync(callback.ChatId, callback.MessageId, ct);
                    if (state is null
                        || state.AlbumId != albumId
                        || state.Mode != TelegramAlbumEditModes.ConfirmRemove
                        || state.Ids.Count == 0)
                    {
                        answer = TelegramBotTexts.Get(TelegramBotTextIds.SessionExpired);
                        await RebuildAlbumAsync(token, apiBase, callback.ChatId, callback.MessageId,
                            albumId, state?.Page ?? 0, ct);
                        outcome = TelegramRouteOutcome.Handled;
                        return outcome;
                    }

                    var removed = await albums.RemoveTrackAsync(albumId, state.Ids[0], ct);
                    answer = removed.Succeeded
                        ? Short(TelegramBotTexts.FormatAlbumRemoved(
                            removed.TrackTitle ?? string.Empty, removed.AlbumTitle ?? string.Empty))
                        : TelegramBotTexts.Get(TelegramBotTextIds.AlbumTrackNotInAlbum);

                    if (removed.Succeeded)
                    {
                        await TelegramAudit.RecordAsync(db, callback.SenderId, callback.ChatId,
                            "album.track.remove", "AlbumTrack", albumId.ToString("N"),
                            new { albumId, trackId = state.Ids[0], albumTitle = removed.AlbumTitle }, ct);
                    }

                    await RebuildAlbumAsync(token, apiBase, callback.ChatId, callback.MessageId,
                        albumId, state.Page, ct);
                    outcome = TelegramRouteOutcome.Handled;
                    return outcome;
                }

                case TelegramKeyboard.AlbumAddListAction:
                {
                    if (!TryParseIdAndPage(data.Argument, out var albumId, out var page))
                    {
                        answer = TelegramBotTexts.Get(TelegramBotTextIds.UnknownAction);
                        outcome = TelegramRouteOutcome.Rejected;
                        return outcome;
                    }

                    var built = await BuildAlbumAddScreenAsync(
                        callback.ChatId, callback.MessageId, albumId, page, ct);
                    if (built is null)
                    {
                        await EditAlbumGoneAsync(token, apiBase, callback.ChatId, callback.MessageId, ct);
                        answer = TelegramBotTexts.Get(TelegramBotTextIds.AlbumGone);
                        outcome = TelegramRouteOutcome.Handled;
                        return outcome;
                    }

                    await TryEditAsync(token, apiBase, callback.ChatId, callback.MessageId,
                        built.Value.Text, built.Value.Keyboard, ct);
                    answer = TelegramBotTexts.Get(TelegramBotTextIds.Done);
                    outcome = TelegramRouteOutcome.Handled;
                    return outcome;
                }

                case TelegramKeyboard.AlbumAddAction:
                {
                    if (!TryParseIdAndIndex(data.Argument, out var albumId, out var index))
                    {
                        answer = TelegramBotTexts.Get(TelegramBotTextIds.UnknownAction);
                        outcome = TelegramRouteOutcome.Rejected;
                        return outcome;
                    }

                    var state = await edits.LoadAsync(callback.ChatId, callback.MessageId, ct);
                    if (state is null
                        || state.AlbumId != albumId
                        || state.Mode != TelegramAlbumEditModes.Add
                        || index < 0
                        || index >= state.Ids.Count)
                    {
                        answer = TelegramBotTexts.Get(TelegramBotTextIds.SessionExpired);
                        await RebuildAlbumAsync(token, apiBase, callback.ChatId, callback.MessageId,
                            albumId, state?.Page ?? 0, ct);
                        outcome = TelegramRouteOutcome.Handled;
                        return outcome;
                    }

                    var trackId = state.Ids[index];
                    var trackTitle = await albums.GetTrackTitleAsync(trackId, ct);
                    var added = await albums.AssignTrackAsync(albumId, trackId, null, ct);
                    answer = added.Succeeded
                        ? Short(TelegramBotTexts.FormatAlbumAdded(
                            trackTitle ?? string.Empty, added.AlbumTitle ?? string.Empty))
                        : TelegramBotTexts.Get(TelegramBotTextIds.AlbumTrackGone);

                    if (added.Succeeded)
                    {
                        await TelegramAudit.RecordAsync(db, callback.SenderId, callback.ChatId,
                            "album.track.add", "AlbumTrack", albumId.ToString("N"),
                            new { albumId, trackId, albumTitle = added.AlbumTitle }, ct);
                    }

                    // Re-render the shortlist so the operator can add several tracks without
                    // walking back into the album each time.
                    var rebuilt = await BuildAlbumAddScreenAsync(
                        callback.ChatId, callback.MessageId, albumId, state.Page, ct);
                    if (rebuilt is not null)
                    {
                        await TryEditAsync(token, apiBase, callback.ChatId, callback.MessageId,
                            rebuilt.Value.Text, rebuilt.Value.Keyboard, ct);
                    }
                    outcome = TelegramRouteOutcome.Handled;
                    return outcome;
                }

                case TelegramKeyboard.ArtistListAction:
                {
                    var built = await BuildArtistListScreenAsync(PageFrom(data.Argument), ct);
                    var unchanged = await TryEditAsync(token, apiBase, callback.ChatId, callback.MessageId,
                        built.Text, built.Keyboard, ct);
                    answer = unchanged
                        ? TelegramBotTexts.Get(TelegramBotTextIds.AlreadyCurrent)
                        : TelegramBotTexts.Get(TelegramBotTextIds.Done);
                    outcome = TelegramRouteOutcome.Handled;
                    return outcome;
                }

                case TelegramKeyboard.ArtistOpenAction:
                {
                    if (!TryParseId(data.Argument, out var artistId))
                    {
                        answer = TelegramBotTexts.Get(TelegramBotTextIds.UnknownAction);
                        outcome = TelegramRouteOutcome.Rejected;
                        return outcome;
                    }

                    var built = await BuildArtistDetailScreenAsync(artistId, ct);
                    if (built is null)
                    {
                        var fallback = await BuildArtistListScreenAsync(0, ct);
                        await TryEditAsync(token, apiBase, callback.ChatId, callback.MessageId,
                            TelegramBotTexts.Get(TelegramBotTextIds.ArtistGone) + "\n\n" + fallback.Text,
                            fallback.Keyboard, ct);
                        answer = TelegramBotTexts.Get(TelegramBotTextIds.ArtistGone);
                        outcome = TelegramRouteOutcome.Handled;
                        return outcome;
                    }

                    var unchanged = await TryEditAsync(token, apiBase, callback.ChatId, callback.MessageId,
                        built.Value.Text, built.Value.Keyboard, ct);
                    answer = unchanged
                        ? TelegramBotTexts.Get(TelegramBotTextIds.AlreadyCurrent)
                        : TelegramBotTexts.Get(TelegramBotTextIds.Done);
                    outcome = TelegramRouteOutcome.Handled;
                    return outcome;
                }

                default:
                    logger.LogWarning("Telegram bot callback action {Action} has no handler in this phase.", data.Action);
                    answer = TelegramBotTexts.Get(TelegramBotTextIds.UnknownAction);
                    return TelegramRouteOutcome.Rejected;
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Telegram bot callback handling failed.");
            return TelegramRouteOutcome.Rejected;
        }
        finally
        {
            await TryAnswerAsync(token, apiBase, callback.CallbackQueryId, answer, ct);
        }
    }

    /// <summary>
    /// The path awaiting confirmation, or null. The stored value is treated as untrusted input even
    /// though this process wrote it: anyone with database access can edit that row.
    /// </summary>
    private static string? CandidateFrom(TelegramBotSession? session)
    {
        if (session is null || session.State != TelegramBotStates.StorageConfirm) return null;
        return session.Payload is not null
               && session.Payload.TryGetValue(TelegramBotPayloadKeys.Path, out var path)
               && !string.IsNullOrWhiteSpace(path)
            ? path
            : null;
    }

    private async Task SendMenuAsync(string token, string apiBase, TelegramBotMessage message, CancellationToken ct)
    {
        // The same visible "working" signal the status screen sends; the menu re-reads the database
        // too, because it includes the live status line.
        await TrySendTypingAsync(token, apiBase, message, ct);

        var snapshot = await statusService.ReadAsync(ct);
        await sessions.SaveAsync(message.ChatId, TelegramBotStates.Status, null, ct);
        await client.SendMessageAsync(token, apiBase, message.ChatId, message.ThreadId,
            TelegramBotTexts.Get(TelegramBotTextIds.Start) + "\n\n" + TelegramBotTexts.FormatStatus(snapshot),
            TelegramKeyboard.MainMenu(), ct);
    }

    /// <summary>
    /// A chat action is a hint, never a requirement: a failure here must not cost the operator the
    /// screen they asked for.
    /// </summary>
    private async Task TrySendTypingAsync(
        string token, string apiBase, TelegramBotMessage message, CancellationToken ct)
    {
        try
        {
            await client.SendChatActionAsync(token, apiBase, message.ChatId, message.ThreadId, "typing", ct);
        }
        catch (Exception ex) when (ex is TelegramBotException or HttpRequestException or TaskCanceledException)
        {
            logger.LogDebug("Telegram chat action could not be sent: {Message}", ex.Message);
        }
    }

    /// <summary>
    /// The storage screen. With no argument it reports the current location; with a path it runs the
    /// same validation the HTTP endpoint runs and offers the move only when every check passed.
    /// </summary>
    private async Task SendStorageScreenAsync(
        string token, string apiBase, TelegramBotMessage message, string argument, CancellationToken ct)
    {
        await TrySendTypingAsync(token, apiBase, message, ct);

        var view = await storageView.ReadAsync(ct);
        if (string.IsNullOrWhiteSpace(argument))
        {
            await sessions.SaveAsync(message.ChatId, TelegramBotStates.Storage, null, ct);
            await client.SendMessageAsync(token, apiBase, message.ChatId, message.ThreadId,
                TelegramBotTexts.FormatStorage(view), TelegramKeyboard.StorageScreen(false), ct);
            return;
        }

        var checks = await storageView.ValidateAsync(argument, ct);
        var failed = checks.FirstOrDefault(x => !x.Ok);
        if (failed is not null)
        {
            await sessions.SaveAsync(message.ChatId, TelegramBotStates.Storage, null, ct);
            await client.SendMessageAsync(token, apiBase, message.ChatId, message.ThreadId,
                TelegramBotTexts.FormatStorage(view with
                {
                    CandidatePath = argument,
                    CandidateAccepted = false,
                    CandidateError = failed.Message
                }),
                TelegramKeyboard.StorageScreen(false), ct);
            return;
        }

        // Held for a short while so the operator confirms deliberately; the session expiry, not the
        // keyboard, is what invalidates it.
        await sessions.SaveAsync(message.ChatId, TelegramBotStates.StorageConfirm,
            new Dictionary<string, string> { [TelegramBotPayloadKeys.Path] = argument }, ct);
        await client.SendMessageAsync(token, apiBase, message.ChatId, message.ThreadId,
            TelegramBotTexts.FormatStorage(view with
            {
                CandidatePath = argument,
                CandidateAccepted = true
            }),
            TelegramKeyboard.StorageScreen(true), ct);
    }

    private async Task SendStatusScreenAsync(string token, string apiBase, TelegramBotMessage message, CancellationToken ct)
    {
        // A visible "working" signal while the database is read; best effort, never fatal.
        await TrySendTypingAsync(token, apiBase, message, ct);

        var snapshot = await statusService.ReadAsync(ct);
        await sessions.SaveAsync(message.ChatId, TelegramBotStates.Status, null, ct);
        await client.SendMessageAsync(token, apiBase, message.ChatId, message.ThreadId,
            TelegramBotTexts.FormatStatus(snapshot), TelegramKeyboard.StatusScreen(), ct);
    }

    // ---- report and album-management screens --------------------------------------------------

    /// <summary>The library overview. Every counter is read from the database on each render.</summary>
    private async Task<(string Text, TelegramKeyboard Keyboard)> BuildReportsScreenAsync(CancellationToken ct)
    {
        var overview = await reports.ReadOverviewAsync(ct);
        return (TelegramBotTexts.FormatOverview(overview), TelegramKeyboard.ReportsScreen());
    }

    private async Task SendReportsScreenAsync(
        string token, string apiBase, TelegramBotMessage message, CancellationToken ct)
    {
        await TrySendTypingAsync(token, apiBase, message, ct);
        var built = await BuildReportsScreenAsync(ct);
        await client.SendMessageAsync(token, apiBase, message.ChatId, message.ThreadId,
            built.Text, built.Keyboard, ct);
    }

    private async Task<(string Text, TelegramKeyboard Keyboard)> BuildAlbumManageScreenAsync(
        int page, CancellationToken ct)
    {
        var total = await albums.CountAsync(ct);
        var totalPages = PageCount(total, TelegramKeyboard.ListPageSize);
        var safePage = ClampPage(page, totalPages);
        var rows = await albums.ListAsync(
            TelegramKeyboard.ListPageSize, safePage * TelegramKeyboard.ListPageSize, ct);

        return (TelegramBotTexts.FormatAlbumManage(total, safePage, totalPages),
            TelegramKeyboard.AlbumManageScreen(rows, safePage, totalPages));
    }

    private async Task SendAlbumManageScreenAsync(
        string token, string apiBase, TelegramBotMessage message, int page, CancellationToken ct)
    {
        await TrySendTypingAsync(token, apiBase, message, ct);
        var built = await BuildAlbumManageScreenAsync(page, ct);
        await client.SendMessageAsync(token, apiBase, message.ChatId, message.ThreadId,
            built.Text, built.Keyboard, ct);
    }

    /// <summary>
    /// One album's screen. Saving the row-to-track mapping here is what lets a 44-byte button mean
    /// one exact track: the mapping always matches the buttons in the message being rendered.
    /// </summary>
    private async Task<(string Text, TelegramKeyboard Keyboard)?> BuildAlbumDetailScreenAsync(
        long chatId, long messageId, Guid albumId, int page, CancellationToken ct)
    {
        var report = await albums.ReadReportAsync(albumId, ct);
        if (report is null) return null;

        var totalPages = PageCount(report.TrackCount, TelegramKeyboard.ListPageSize);
        var safePage = ClampPage(page, totalPages);
        var tracks = await albums.ListTracksAsync(
            albumId, TelegramKeyboard.ListPageSize, safePage * TelegramKeyboard.ListPageSize, ct);

        await edits.SaveAsync(chatId, messageId, albumId, TelegramAlbumEditModes.Tracks, safePage,
            tracks.Select(x => x.Id).ToList(), ct);

        return (TelegramBotTexts.FormatAlbumDetail(report, safePage, totalPages),
            TelegramKeyboard.AlbumDetailScreen(albumId, tracks, safePage, totalPages));
    }

    /// <summary>The tracks that are not in the album yet, paged and stored for the add buttons.</summary>
    private async Task<(string Text, TelegramKeyboard Keyboard)?> BuildAlbumAddScreenAsync(
        long chatId, long messageId, Guid albumId, int page, CancellationToken ct)
    {
        var title = await albums.GetTitleAsync(albumId, ct);
        if (title is null) return null;

        var total = await albums.CountCandidatesAsync(albumId, ct);
        var totalPages = PageCount(total, TelegramKeyboard.ListPageSize);
        var safePage = ClampPage(page, totalPages);
        var candidates = await albums.ListCandidatesAsync(
            albumId, TelegramKeyboard.ListPageSize, safePage * TelegramKeyboard.ListPageSize, ct);

        await edits.SaveAsync(chatId, messageId, albumId, TelegramAlbumEditModes.Add, safePage,
            candidates.Select(x => x.Id).ToList(), ct);

        return (TelegramBotTexts.FormatAlbumAdd(title, total, safePage, totalPages),
            TelegramKeyboard.AlbumAddScreen(albumId, candidates, safePage, totalPages));
    }

    private async Task<(string Text, TelegramKeyboard Keyboard)> BuildArtistListScreenAsync(
        int page, CancellationToken ct)
    {
        var total = await reports.CountArtistsAsync(ct);
        var totalPages = PageCount(total, TelegramKeyboard.ListPageSize);
        var safePage = ClampPage(page, totalPages);
        var rows = await reports.ListArtistsAsync(
            TelegramKeyboard.ListPageSize, safePage * TelegramKeyboard.ListPageSize, ct);

        return (TelegramBotTexts.FormatArtistList(total, safePage, totalPages),
            TelegramKeyboard.ArtistListScreen(rows, safePage, totalPages));
    }

    private async Task SendArtistListScreenAsync(
        string token, string apiBase, TelegramBotMessage message, int page, CancellationToken ct)
    {
        await TrySendTypingAsync(token, apiBase, message, ct);
        var built = await BuildArtistListScreenAsync(page, ct);
        await client.SendMessageAsync(token, apiBase, message.ChatId, message.ThreadId,
            built.Text, built.Keyboard, ct);
    }

    private async Task<(string Text, TelegramKeyboard Keyboard)?> BuildArtistDetailScreenAsync(
        Guid artistId, CancellationToken ct)
    {
        var report = await reports.ReadArtistAsync(artistId, ct);
        if (report is null) return null;

        return (TelegramBotTexts.FormatArtistDetail(report),
            TelegramKeyboard.ArtistDetailScreen(report.Id, report.Albums));
    }

    /// <summary>
    /// Re-renders an album after a write, or replaces the message with an honest "album is gone"
    /// screen. A failed render must never leave the operator on a screen whose buttons no longer work.
    /// </summary>
    private async Task RebuildAlbumAsync(
        string token, string apiBase, long chatId, long messageId, Guid albumId, int page, CancellationToken ct)
    {
        var built = await BuildAlbumDetailScreenAsync(chatId, messageId, albumId, page, ct);
        if (built is null)
        {
            await EditAlbumGoneAsync(token, apiBase, chatId, messageId, ct);
            return;
        }

        await TryEditAsync(token, apiBase, chatId, messageId, built.Value.Text, built.Value.Keyboard, ct);
    }

    private async Task EditAlbumGoneAsync(
        string token, string apiBase, long chatId, long messageId, CancellationToken ct)
    {
        await edits.DeleteAsync(chatId, messageId, ct);

        var keyboard = TelegramKeyboard.Create()
            .AddRow(TelegramBotTexts.Get(TelegramBotTextIds.AlbumManageTitle),
                TelegramCallbackData.Encode(TelegramKeyboard.AlbumManageAction, "0"))
            .AddRow(TelegramBotTexts.Get(TelegramBotTextIds.Home),
                TelegramCallbackData.Encode(TelegramKeyboard.HomeAction));

        await TryEditAsync(token, apiBase, chatId, messageId,
            TelegramBotTexts.Get(TelegramBotTextIds.AlbumGone), keyboard, ct);
    }

    private static int PageFrom(string? argument) =>
        int.TryParse(argument, NumberStyles.Integer, CultureInfo.InvariantCulture, out var page) && page >= 0
            ? page
            : 0;

    private static bool TryParseId(string? argument, out Guid id) =>
        Guid.TryParseExact(argument, "N", out id);

    private static bool TryParseIdAndPage(string? argument, out Guid id, out int page)
    {
        id = Guid.Empty;
        page = 0;
        if (string.IsNullOrEmpty(argument)) return false;

        var separator = argument.IndexOf(':');
        if (separator <= 0) return false;

        return Guid.TryParseExact(argument[..separator], "N", out id)
               && int.TryParse(argument[(separator + 1)..], NumberStyles.Integer,
                   CultureInfo.InvariantCulture, out page)
               && page >= 0;
    }

    private static bool TryParseIdAndIndex(string? argument, out Guid id, out int index)
    {
        id = Guid.Empty;
        index = -1;
        if (string.IsNullOrEmpty(argument)) return false;

        var separator = argument.IndexOf(':');
        if (separator <= 0) return false;

        return Guid.TryParseExact(argument[..separator], "N", out id)
               && int.TryParse(argument[(separator + 1)..], NumberStyles.Integer,
                   CultureInfo.InvariantCulture, out index)
               && index >= 0;
    }

    private static int PageCount(int total, int pageSize) =>
        Math.Max(1, (total + pageSize - 1) / pageSize);

    private static int ClampPage(int page, int totalPages) =>
        Math.Min(Math.Max(0, page), totalPages - 1);

    /// <summary>
    /// A callback toast is capped by Telegram; a long title is clipped rather than making the whole
    /// answer fail.
    /// </summary>
    private static string Short(string text) => text.Length <= 190 ? text : text[..189] + "…";

    /// <summary>
    /// Edits a screen in place. Returns true when Telegram refused the edit because nothing changed,
    /// which is a success from the operator's point of view and must not surface as an error.
    /// </summary>
    /// <summary>
    /// Edits a screen in place. A null keyboard is legitimate: a confirmation replaces the buttons it
    /// answers with, so the operator cannot tap an option that has already been applied.
    /// </summary>
    private async Task<bool> TryEditAsync(
        string token, string apiBase, long chatId, long messageId,
        string text, TelegramKeyboard? keyboard, CancellationToken ct)
    {
        try
        {
            await client.EditMessageTextAsync(token, apiBase, chatId, messageId, text, keyboard, ct);
            return false;
        }
        catch (TelegramBotException ex) when (IsNotModified(ex))
        {
            // The screen already shows exactly this; the caller reports "already current".
            logger.LogDebug("Telegram bot screen at chat {ChatId} was already current.", chatId);
            return true;
        }
    }

    private async Task TryAnswerAsync(string token, string apiBase, string callbackQueryId, string text, CancellationToken ct)    {
        try
        {
            await client.AnswerCallbackQueryAsync(token, apiBase, callbackQueryId, text, ct);
        }
        catch (Exception ex) when (ex is TelegramBotException or HttpRequestException or TaskCanceledException)
        {
            logger.LogDebug("Telegram answerCallbackQuery could not be delivered: {Message}", ex.Message);
        }
    }

    private static bool IsNotModified(TelegramBotException exception) =>
        exception.ErrorCode == 400 && exception.Message.Contains("not modified", StringComparison.OrdinalIgnoreCase);
}
