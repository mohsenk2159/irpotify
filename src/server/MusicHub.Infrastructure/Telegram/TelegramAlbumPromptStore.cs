using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MusicHub.Domain;

namespace MusicHub.Infrastructure.Telegram;

/// <summary>A track or collection of tracks that is waiting for the operator to choose its album.</summary>
/// <param name="TrackId">The primary track the question is about.</param>
/// <param name="TrackTitle">Shown in the picker header and the confirmation.</param>
/// <param name="FileName">The uploaded file name, so two prompts for similar tracks differ.</param>
/// <param name="AwaitingName">True once the operator chose "create a new album".</param>
/// <param name="UpdatedAt">When the prompt was last touched; drives expiry.</param>
/// <param name="TrackIds">All tracks grouped under this prompt (for media groups or batch imports).</param>
/// <param name="MediaGroupId">The Telegram media_group_id if this prompt was created for a grouped message.</param>
public sealed record TelegramAlbumPrompt(
    Guid TrackId,
    string TrackTitle,
    string FileName,
    bool AwaitingName,
    DateTimeOffset UpdatedAt,
    List<Guid>? TrackIds = null,
    string? MediaGroupId = null)
{
    public IReadOnlyList<Guid> AllTrackIds => TrackIds is { Count: > 0 } ? TrackIds : [TrackId];
}

/// <summary>
/// Pending per-file album questions, keyed by the bot message that asks them.
/// </summary>
/// <remarks>
/// <para>
/// This is a separate store from <see cref="TelegramBotStateStore"/> on purpose. The requirement is
/// that the bot asks about the album <em>separately for each file</em>, so an operator who forwards
/// ten tracks has ten open questions. The chat session holds exactly one state, which would mean
/// answering the second file silently retargeted the first file's buttons. Keying by the prompt's
/// message id removes that: every callback carries the id of the message it was tapped on, so the
/// question it belongs to is unambiguous no matter how many are outstanding.
/// </para>
/// <para>
/// Rows live in the existing <c>AppSettings</c> table under
/// <c>telegram.bot.album.&lt;chatId&gt;.&lt;messageId&gt;</c>, so no migration is needed. They are
/// deleted when answered, when the operator skips, and when they expire, so the table does not
/// accumulate. The 15 minute window matches the session lifetime.
/// </para>
/// </remarks>
public sealed class TelegramAlbumPromptStore(
    MusicHubDbContext db,
    TimeProvider clock,
    ILogger<TelegramAlbumPromptStore> logger)
{
    public const string KeyPrefix = "telegram.bot.album.";

    /// <summary>Inactivity window after which an unanswered prompt is discarded.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static string KeyFor(long chatId, long messageId) =>
        KeyPrefix
        + chatId.ToString(CultureInfo.InvariantCulture) + "."
        + messageId.ToString(CultureInfo.InvariantCulture);

    public static string MediaGroupKeyFor(long chatId, string mediaGroupId) =>
        KeyPrefix + "mg."
        + chatId.ToString(CultureInfo.InvariantCulture) + "."
        + mediaGroupId;

    /// <summary>Stores a prompt against the message that asks it.</summary>
    public async Task SaveAsync(
        long chatId,
        long messageId,
        Guid trackId,
        string trackTitle,
        string fileName,
        bool awaitingName,
        CancellationToken ct,
        List<Guid>? trackIds = null,
        string? mediaGroupId = null)
    {
        var now = clock.GetUtcNow();
        var prompt = new TelegramAlbumPrompt(trackId, trackTitle, fileName, awaitingName, now, trackIds, mediaGroupId);
        var serialized = JsonSerializer.Serialize(prompt, SerializerOptions);
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

    /// <summary>Gets the prompt message id associated with an active media group, if any.</summary>
    public async Task<long?> GetMessageIdForMediaGroupAsync(long chatId, string mediaGroupId, CancellationToken ct)
    {
        var key = MediaGroupKeyFor(chatId, mediaGroupId);
        var row = await db.AppSettings.SingleOrDefaultAsync(x => x.Key == key, ct);
        if (row is null) return null;

        if (clock.GetUtcNow() - row.UpdatedAt > Lifetime)
        {
            db.AppSettings.Remove(row);
            await db.SaveChangesAsync(ct);
            return null;
        }

        return long.TryParse(row.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var msgId) ? msgId : null;
    }

    /// <summary>Records the prompt message id for a media group so subsequent tracks map to the same prompt.</summary>
    public async Task SetMessageIdForMediaGroupAsync(long chatId, string mediaGroupId, long messageId, CancellationToken ct)
    {
        var key = MediaGroupKeyFor(chatId, mediaGroupId);
        var now = clock.GetUtcNow();
        var row = await db.AppSettings.SingleOrDefaultAsync(x => x.Key == key, ct);
        if (row is null)
        {
            db.AppSettings.Add(new AppSetting
            {
                Key = key,
                Value = messageId.ToString(CultureInfo.InvariantCulture),
                IsSecret = false,
                UpdatedAt = now
            });
        }
        else
        {
            row.Value = messageId.ToString(CultureInfo.InvariantCulture);
            row.UpdatedAt = now;
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Loads the prompt for one message. Returns null when there is none, when the row is
    /// unreadable, or when it has expired - deleting a stale row while doing so.
    /// </summary>
    public async Task<TelegramAlbumPrompt?> LoadAsync(long chatId, long messageId, CancellationToken ct)
    {
        var key = KeyFor(chatId, messageId);
        var row = await db.AppSettings.SingleOrDefaultAsync(x => x.Key == key, ct);
        if (row is null) return null;

        TelegramAlbumPrompt? prompt;
        try
        {
            prompt = JsonSerializer.Deserialize<TelegramAlbumPrompt>(row.Value, SerializerOptions);
        }
        catch (JsonException)
        {
            prompt = null;
        }

        if (prompt is null || prompt.TrackId == Guid.Empty)
        {
            logger.LogWarning("Discarding an unreadable album prompt for chat {ChatId} message {MessageId}.", chatId, messageId);
            db.AppSettings.Remove(row);
            await db.SaveChangesAsync(ct);
            return null;
        }

        if (clock.GetUtcNow() - prompt.UpdatedAt > Lifetime)
        {
            db.AppSettings.Remove(row);
            await db.SaveChangesAsync(ct);
            return null;
        }

        return prompt;
    }

    /// <summary>Removes a prompt once it is answered or abandoned. Returns whether a row existed.</summary>
    public async Task<bool> DeleteAsync(long chatId, long messageId, CancellationToken ct)
    {
        var key = KeyFor(chatId, messageId);
        var row = await db.AppSettings.SingleOrDefaultAsync(x => x.Key == key, ct);
        if (row is null) return false;

        try
        {
            var prompt = JsonSerializer.Deserialize<TelegramAlbumPrompt>(row.Value, SerializerOptions);
            if (!string.IsNullOrWhiteSpace(prompt?.MediaGroupId))
            {
                var mgKey = MediaGroupKeyFor(chatId, prompt.MediaGroupId);
                var mgRow = await db.AppSettings.SingleOrDefaultAsync(x => x.Key == mgKey, ct);
                if (mgRow is not null) db.AppSettings.Remove(mgRow);
            }
        }
        catch (JsonException)
        {
        }

        db.AppSettings.Remove(row);
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>
    /// The prompt the operator is currently answering with a typed album name. There is at most one
    /// per chat because only one text reply can arrive at a time, so "the most recent awaiting-name
    /// prompt" is unambiguous.
    /// </summary>
    public async Task<(long MessageId, TelegramAlbumPrompt Prompt)?> LoadAwaitingNameAsync(long chatId, CancellationToken ct)
    {
        var prefix = KeyPrefix + chatId.ToString(CultureInfo.InvariantCulture) + ".";
        var rows = await db.AppSettings.AsNoTracking()
            .Where(x => x.Key.StartsWith(prefix))
            .ToListAsync(ct);

        (long MessageId, TelegramAlbumPrompt Prompt)? best = null;
        var expired = new List<AppSetting>();

        foreach (var row in rows)
        {
            TelegramAlbumPrompt? prompt;
            try
            {
                prompt = JsonSerializer.Deserialize<TelegramAlbumPrompt>(row.Value, SerializerOptions);
            }
            catch (JsonException)
            {
                expired.Add(row);
                continue;
            }

            if (prompt is null || !prompt.AwaitingName) continue;

            if (clock.GetUtcNow() - prompt.UpdatedAt > Lifetime)
            {
                expired.Add(row);
                continue;
            }

            var suffix = row.Key[prefix.Length..];
            if (!long.TryParse(suffix, NumberStyles.Integer, CultureInfo.InvariantCulture, out var messageId)) continue;

            if (best is null || prompt.UpdatedAt > best.Value.Prompt.UpdatedAt)
            {
                best = (messageId, prompt);
            }
        }

        if (expired.Count > 0)
        {
            var keys = expired.Select(x => x.Key).ToList();
            db.AppSettings.RemoveRange(await db.AppSettings.Where(x => keys.Contains(x.Key)).ToListAsync(ct));
            await db.SaveChangesAsync(ct);
        }

        return best;
    }
}
