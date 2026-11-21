using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MusicHub.Domain;

namespace MusicHub.Infrastructure.Telegram;

/// <summary>The conversation state persisted for one chat.</summary>
public sealed class TelegramBotSession
{
    public string State { get; init; } = string.Empty;
    public Dictionary<string, string>? Payload { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}

/// <summary>
/// Chat-scoped conversation state stored in the existing <c>AppSettings</c> table.
/// </summary>
/// <remarks>
/// <para>
/// Key format is <c>telegram.bot.state.&lt;chatId&gt;</c> and the value is
/// <c>{"state":"...","payload":{...},"updatedAt":"..."}</c>. The bot is admin-only, so this is a
/// handful of rows; using the key/value table avoids a schema migration that would cross the
/// database ownership boundary. A dedicated table is the right move when customer-facing flows
/// arrive (Phase 2), and that is a deliberate follow-up.
/// </para>
/// <para>
/// Sessions expire after <see cref="Lifetime"/> of inactivity: a read past the deadline deletes the
/// row and reports "no session", so a stale confirmation can never be completed.
/// </para>
/// </remarks>
public sealed class TelegramBotStateStore(MusicHubDbContext db, TimeProvider clock, ILogger<TelegramBotStateStore> logger)
{
    public const string KeyPrefix = "telegram.bot.state.";

    /// <summary>Inactivity window after which a session is gone.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static string KeyFor(long chatId) =>
        KeyPrefix + chatId.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Loads the state for a chat. Returns <c>null</c> when there is none, when the stored value is
    /// unreadable, or when it has expired - and deletes an expired or corrupt row while doing so.
    /// </summary>
    public async Task<TelegramBotSession?> LoadAsync(long chatId, CancellationToken ct)
    {
        var key = KeyFor(chatId);
        var row = await db.AppSettings.SingleOrDefaultAsync(x => x.Key == key, ct);
        if (row is null) return null;

        TelegramBotSession? session;
        try
        {
            session = JsonSerializer.Deserialize<TelegramBotSession>(row.Value, SerializerOptions);
        }
        catch (JsonException)
        {
            session = null;
        }

        if (session is null || string.IsNullOrWhiteSpace(session.State))
        {
            logger.LogWarning("Discarding an unreadable Telegram bot state row for chat {ChatId}.", chatId);
            db.AppSettings.Remove(row);
            await db.SaveChangesAsync(ct);
            return null;
        }

        if (IsExpired(session, clock.GetUtcNow()))
        {
            db.AppSettings.Remove(row);
            await db.SaveChangesAsync(ct);
            return null;
        }

        return session;
    }

    /// <summary>Creates or replaces the state for a chat, stamping it with the current time.</summary>
    public async Task<TelegramBotSession> SaveAsync(
        long chatId,
        string state,
        IReadOnlyDictionary<string, string>? payload,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(state))
        {
            throw new ArgumentException("A bot state name is required.", nameof(state));
        }

        var session = new TelegramBotSession
        {
            State = state,
            Payload = payload is null ? null : new Dictionary<string, string>(payload),
            UpdatedAt = clock.GetUtcNow()
        };
        var serialized = JsonSerializer.Serialize(session, SerializerOptions);
        var key = KeyFor(chatId);

        var row = await db.AppSettings.SingleOrDefaultAsync(x => x.Key == key, ct);
        if (row is null)
        {
            db.AppSettings.Add(new AppSetting
            {
                Key = key,
                Value = serialized,
                IsSecret = false,
                UpdatedAt = session.UpdatedAt
            });
        }
        else
        {
            row.Value = serialized;
            row.UpdatedAt = session.UpdatedAt;
        }

        await db.SaveChangesAsync(ct);
        return session;
    }

    /// <summary>Removes the state for a chat. Returns whether a row existed.</summary>
    public async Task<bool> DeleteAsync(long chatId, CancellationToken ct)
    {
        var key = KeyFor(chatId);
        var row = await db.AppSettings.SingleOrDefaultAsync(x => x.Key == key, ct);
        if (row is null) return false;

        db.AppSettings.Remove(row);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public static bool IsExpired(TelegramBotSession session, DateTimeOffset now) =>
        now - session.UpdatedAt > Lifetime;
}
