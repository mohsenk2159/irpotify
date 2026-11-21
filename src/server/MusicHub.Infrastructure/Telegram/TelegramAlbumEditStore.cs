using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MusicHub.Domain;

namespace MusicHub.Infrastructure.Telegram;

/// <summary>Which list an album screen is showing.</summary>
public static class TelegramAlbumEditModes
{
    /// <summary>The album's own tracks, each with a remove button.</summary>
    public const string Tracks = "tracks";

    /// <summary>Live tracks that are not in the album yet, each with an add button.</summary>
    public const string Add = "add";

    /// <summary>A single track waiting for the operator to confirm its removal.</summary>
    public const string ConfirmRemove = "confirm";
}

/// <summary>
/// One album screen: which album it is about, which list it shows, and the exact track ids behind
/// the buttons the operator can currently tap.
/// </summary>
/// <remarks>
/// The ids are part of the stored state because the whole album <c>callback_data</c> budget is
/// 64 bytes: an album id and a track id together do not fit. A button therefore carries the album
/// id (so the screen can always be rebuilt) and the position of the row, and this row-to-id mapping
/// is what turns that position into one specific track. The mapping is rewritten on every render,
/// so a tap can never hit a track from an older page.
/// </remarks>
public sealed record TelegramAlbumEditState(
    Guid AlbumId,
    string Mode,
    int Page,
    List<Guid> Ids,
    DateTimeOffset UpdatedAt);

/// <summary>
/// Album-management screens, keyed by the bot message that shows them.
/// </summary>
/// <remarks>
/// This is a separate store from <see cref="TelegramBotStateStore"/> for the same reason
/// <see cref="TelegramAlbumPromptStore"/> is: the chat session holds one state, while the operator
/// can leave several album screens open. A callback arrives with the id of the message it was
/// tapped on, so keying by message keeps each screen's row-to-track mapping its own.
/// <para>
/// Rows live in the existing <c>AppSettings</c> table under
/// <c>telegram.bot.albumedit.&lt;chatId&gt;.&lt;messageId&gt;</c>, so no migration is needed. The
/// 15 minute lifetime matches the session window.
/// </para>
/// </remarks>
public sealed class TelegramAlbumEditStore(
    MusicHubDbContext db,
    TimeProvider clock,
    ILogger<TelegramAlbumEditStore> logger)
{
    public const string KeyPrefix = "telegram.bot.albumedit.";

    /// <summary>Inactivity window after which an album screen is discarded.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static string KeyFor(long chatId, long messageId) =>
        KeyPrefix
        + chatId.ToString(CultureInfo.InvariantCulture) + "."
        + messageId.ToString(CultureInfo.InvariantCulture);

    /// <summary>Stores (or replaces) the state behind one album message.</summary>
    public async Task SaveAsync(
        long chatId,
        long messageId,
        Guid albumId,
        string mode,
        int page,
        IReadOnlyList<Guid> ids,
        CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var state = new TelegramAlbumEditState(albumId, mode, page, ids.ToList(), now);
        var serialized = JsonSerializer.Serialize(state, SerializerOptions);
        var key = KeyFor(chatId, messageId);

        var row = await db.AppSettings.SingleOrDefaultAsync(x => x.Key == key, ct);
        if (row is null)
        {
            db.AppSettings.Add(new AppSetting
            {
                Key = key,
                Value = serialized,
                IsSecret = false,
                UpdatedAt = now
            });
        }
        else
        {
            row.Value = serialized;
            row.UpdatedAt = now;
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Loads the state for one message. Returns null when there is none, when the row is unreadable,
    /// or when it has expired - deleting a stale row while doing so.
    /// </summary>
    public async Task<TelegramAlbumEditState?> LoadAsync(long chatId, long messageId, CancellationToken ct)
    {
        var key = KeyFor(chatId, messageId);
        var row = await db.AppSettings.SingleOrDefaultAsync(x => x.Key == key, ct);
        if (row is null) return null;

        TelegramAlbumEditState? state;
        try
        {
            state = JsonSerializer.Deserialize<TelegramAlbumEditState>(row.Value, SerializerOptions);
        }
        catch (JsonException)
        {
            state = null;
        }

        if (state is null || state.AlbumId == Guid.Empty || string.IsNullOrWhiteSpace(state.Mode))
        {
            logger.LogWarning(
                "Discarding an unreadable album screen state for chat {ChatId} message {MessageId}.",
                chatId, messageId);
            db.AppSettings.Remove(row);
            await db.SaveChangesAsync(ct);
            return null;
        }

        if (clock.GetUtcNow() - state.UpdatedAt > Lifetime)
        {
            db.AppSettings.Remove(row);
            await db.SaveChangesAsync(ct);
            return null;
        }

        return state;
    }

    /// <summary>Removes a screen's state. Returns whether a row existed.</summary>
    public async Task<bool> DeleteAsync(long chatId, long messageId, CancellationToken ct)
    {
        var key = KeyFor(chatId, messageId);
        var row = await db.AppSettings.SingleOrDefaultAsync(x => x.Key == key, ct);
        if (row is null) return false;

        db.AppSettings.Remove(row);
        await db.SaveChangesAsync(ct);
        return true;
    }
}
