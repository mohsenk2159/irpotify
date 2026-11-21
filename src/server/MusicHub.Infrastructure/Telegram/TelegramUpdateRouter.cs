using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MusicHub.Domain;

namespace MusicHub.Infrastructure.Telegram;

/// <summary>How an update is dispatched. See <see cref="TelegramUpdateRouter.Classify"/>.</summary>
public enum TelegramUpdateKind
{
    /// <summary>
    /// A <c>message</c> update. It keeps the existing ingestion path: audio/documents are imported
    /// and every other message is rejected there with the unchanged rejection text.
    /// </summary>
    Message,

    /// <summary>A <c>message.text</c> that starts with <c>/</c>.</summary>
    Command,

    /// <summary>An inline-keyboard tap.</summary>
    CallbackQuery,

    /// <summary>Anything the bot does not act on in this phase.</summary>
    Ignored
}

/// <summary>What happened to a non-message update.</summary>
public enum TelegramRouteOutcome
{
    Handled,
    Rejected,
    Ignored
}

/// <summary>
/// The single authorization decision for the bot surface.
/// </summary>
/// <remarks>
/// The keyboard is never trusted and neither is any id carried in <c>callback_data</c>: the sender
/// is compared with the administrator identity re-read from the database on every update. When
/// <c>AdminChatId</c> is configured the chat must match as well. A refusal is the same for a wrong
/// sender and a wrong chat, so the answer cannot be used to probe the configuration.
/// </remarks>
public static class TelegramBotAdminGate
{
    public static bool IsAllowed(TelegramConfiguration? config, long senderId, long chatId)
    {
        if (config?.AdminTelegramUserId is not { } adminUserId) return false;
        if (senderId == 0 || senderId != adminUserId) return false;
        if (config.AdminChatId is { } adminChatId && chatId != adminChatId) return false;
        return true;
    }
}

/// <summary>
/// Classifies every raw update and dispatches the interactive ones.
/// </summary>
/// <remarks>
/// <para>
/// Media handling stays inside <see cref="TelegramIngestionService"/>: the router reports
/// <see cref="TelegramUpdateKind.Message"/> and the poll loop calls the very same handler it always
/// called, so audio/document acceptance, the rejection text, the <c>TelegramImports</c> audit rows
/// and the offset semantics are untouched. Proving that with a test is part of the contract.
/// </para>
/// <para>
/// For commands and callbacks the administrator gate runs before anything else: before the callback
/// payload is parsed and before any id inside it is used.
/// </para>
/// </remarks>
public sealed class TelegramUpdateRouter(
    MusicHubDbContext db,
    TelegramBotClient client,
    TelegramSecretProtector protector,
    TelegramBotHandler handler,
    ILogger<TelegramUpdateRouter> logger)
{
    /// <summary>Pure classification of one update payload. Never throws on unexpected input.</summary>
    public static TelegramUpdateKind Classify(JsonElement update)
    {
        if (update.ValueKind != JsonValueKind.Object) return TelegramUpdateKind.Ignored;

        if (update.TryGetProperty("callback_query", out var callback) && callback.ValueKind == JsonValueKind.Object)
        {
            return TelegramUpdateKind.CallbackQuery;
        }

        if (!update.TryGetProperty("message", out var message) || message.ValueKind != JsonValueKind.Object)
        {
            return TelegramUpdateKind.Ignored;
        }

        if (IsObject(message, "audio") || IsObject(message, "document")) return TelegramUpdateKind.Message;

        if (message.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String
            && TelegramBotCommand.TryParse(text.GetString(), out _))
        {
            return TelegramUpdateKind.Command;
        }

        // Not media and not a command: still the existing message path, which answers with the
        // frozen "did not contain a supported audio document" rejection.
        return TelegramUpdateKind.Message;
    }

    /// <summary>Dispatch for everything that is not the frozen message/ingestion path.</summary>
    public async Task<TelegramRouteOutcome> RouteNonMediaAsync(JsonElement update, long updateId, CancellationToken ct)
    {
        switch (Classify(update))
        {
            case TelegramUpdateKind.Ignored:
                logger.LogDebug("Ignoring Telegram update {UpdateId} of type {Type}.", updateId, DescribeKind(update));
                return TelegramRouteOutcome.Ignored;

            case TelegramUpdateKind.Command:
                return await RouteCommandAsync(update.GetProperty("message"), updateId, ct);

            case TelegramUpdateKind.CallbackQuery:
                return await RouteCallbackAsync(update.GetProperty("callback_query"), updateId, ct);

            default:
                // The poll loop only calls this for non-message kinds, so reaching here is a bug.
                throw new InvalidOperationException(
                    "Message updates are handled by TelegramIngestionService and must not be routed here.");
        }
    }

    private async Task<TelegramRouteOutcome> RouteCommandAsync(JsonElement message, long updateId, CancellationToken ct)
    {
        var senderId = ReadLong(message, "from", "id");
        var chatId = ReadLong(message, "chat", "id");
        var threadId = TryReadLong(message, "message_thread_id");
        var text = message.TryGetProperty("text", out var textElement) && textElement.ValueKind == JsonValueKind.String
            ? textElement.GetString() ?? string.Empty
            : string.Empty;

        var access = await AuthorizeAsync(senderId, chatId, ct);
        if (!access.Allowed)
        {
            logger.LogWarning("Telegram bot denied update {UpdateId} from sender {SenderId}.", updateId, senderId);
            if (access.CanReply)
            {
                await SafeDenyAsync(access, chatId, threadId, null, ct);
            }
            return TelegramRouteOutcome.Rejected;
        }

        return await handler.HandleCommandAsync(
            access.Token!, access.ApiBase!, new TelegramBotMessage(chatId, senderId, threadId, text), ct);
    }

    private async Task<TelegramRouteOutcome> RouteCallbackAsync(JsonElement callback, long updateId, CancellationToken ct)
    {
        var callbackQueryId = callback.TryGetProperty("id", out var idElement) && idElement.ValueKind == JsonValueKind.String
            ? idElement.GetString()
            : null;
        var senderId = ReadLong(callback, "from", "id");
        var data = callback.TryGetProperty("data", out var dataElement) && dataElement.ValueKind == JsonValueKind.String
            ? dataElement.GetString()
            : null;

        var hasMessage = callback.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.Object;
        var chatId = hasMessage ? ReadLong(message, "chat", "id") : 0L;
        var messageId = hasMessage ? TryReadLong(message, "message_id") ?? 0L : 0L;

        if (string.IsNullOrEmpty(callbackQueryId))
        {
            logger.LogWarning("Telegram bot ignored a callback_query without an id.");
            return TelegramRouteOutcome.Ignored;
        }

        // Authorization first: data has only been read as an opaque string at this point.
        var access = await AuthorizeAsync(senderId, chatId, ct);
        if (!access.Allowed)
        {
            logger.LogWarning("Telegram bot denied callback {CallbackQueryId} from sender {SenderId}.", callbackQueryId, senderId);
            if (access.CanReply)
            {
                await SafeDenyAsync(access, chatId, null, callbackQueryId, ct);
            }
            return TelegramRouteOutcome.Rejected;
        }

        return await handler.HandleCallbackAsync(
            access.Token!,
            access.ApiBase!,
            new TelegramBotCallback(callbackQueryId, chatId, senderId, messageId, data),
            ct);
    }

    /// <summary>
    /// Re-reads the configuration from the database and decides. The decision, the token and the
    /// API base are produced together so a caller cannot dispatch before the gate has answered.
    /// </summary>
    private async Task<TelegramBotAccess> AuthorizeAsync(long senderId, long chatId, CancellationToken ct)
    {
        var config = await db.TelegramConfigurations.AsNoTracking()
            .OrderBy(x => x.UpdatedAt)
            .FirstOrDefaultAsync(ct);

        var allowed = TelegramBotAdminGate.IsAllowed(config, senderId, chatId);

        // The token is only ever used locally to talk to the Bot API; it is never logged, stored
        // on the access record's diagnostic surface, or echoed into a reply.
        var token = protector.Unprotect(config?.BotTokenProtected);
        if (string.IsNullOrWhiteSpace(token))
        {
            if (config is not null)
            {
                logger.LogWarning("The stored Telegram bot token could not be decrypted; the bot cannot answer.");
            }
            return TelegramBotAccess.Denied;
        }

        string apiBase;
        try
        {
            apiBase = TelegramBotClient.NormalizeApiBase(
                !string.IsNullOrWhiteSpace(config!.LocalBotApiEndpoint)
                    ? config.LocalBotApiEndpoint
                    : config.ApiEndpoint);
        }
        catch (InvalidOperationException ex)
        {
            // Misconfiguration is reported without the configured value: it may be a mistyped host.
            logger.LogError("The configured Telegram API endpoint is invalid; the bot cannot answer: {Message}", ex.Message);
            return TelegramBotAccess.Denied;
        }

        return allowed
            ? new TelegramBotAccess(true, token, apiBase)
            : new TelegramBotAccess(false, token, apiBase);
    }

    private async Task SafeDenyAsync(TelegramBotAccess access, long chatId, long? threadId, string? callbackQueryId, CancellationToken ct)
    {
        try
        {
            if (callbackQueryId is not null)
            {
                await client.AnswerCallbackQueryAsync(access.Token!, access.ApiBase!, callbackQueryId,
                    TelegramBotTexts.Denial, ct);
                return;
            }

            if (chatId == 0) return;
            await client.SendMessageAsync(access.Token!, access.ApiBase!, chatId, threadId,
                TelegramBotTexts.Denial, ct);
        }
        catch (Exception ex) when (ex is TelegramBotException or HttpRequestException or TaskCanceledException)
        {
            logger.LogDebug("The Telegram bot denial reply could not be delivered: {Message}", ex.Message);
        }
    }

    private static bool IsObject(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.Object;

    private static long ReadLong(JsonElement element, string parent, string property)
    {
        if (!element.TryGetProperty(parent, out var parentElement) || parentElement.ValueKind != JsonValueKind.Object)
        {
            return 0;
        }
        return TryReadLong(parentElement, property) ?? 0;
    }

    private static long? TryReadLong(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.TryGetInt64(out var parsed) ? parsed : null;

    private static string DescribeKind(JsonElement update)
    {
        foreach (var property in update.EnumerateObject())
        {
            return property.Name;
        }
        return "unknown";
    }

    private sealed record TelegramBotAccess(bool Allowed, string? Token = null, string? ApiBase = null)
    {
        public static TelegramBotAccess Denied { get; } = new(false);

        public bool CanReply => !string.IsNullOrWhiteSpace(Token) && !string.IsNullOrWhiteSpace(ApiBase);
    }
}
