using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace MusicHub.Infrastructure.Telegram;

public sealed class TelegramSecretProtector
{
    private readonly IDataProtector _protector;

    public TelegramSecretProtector(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector("MusicHub.Telegram.BotToken.v1");
    }

    public string Protect(string token) => _protector.Protect(token);

    public string? Unprotect(string? protectedToken)
    {
        if (string.IsNullOrWhiteSpace(protectedToken)) return null;
        try
        {
            return _protector.Unprotect(protectedToken);
        }
        catch
        {
            return null;
        }
    }
}

public sealed record TelegramMe(long Id, string? Username, string? FirstName);

public sealed record TelegramFileInfo(string FileId, string FilePath, long FileSize);

public sealed class TelegramBotException(string message, int? errorCode = null) : Exception(message)
{
    public int? ErrorCode { get; } = errorCode;
}

/// <summary>
/// Minimal Telegram Bot API client. The token is always supplied per call and is never
/// written to logs or exception messages.
/// </summary>
public sealed class TelegramBotClient(HttpClient http)
{
    private const int MaxErrorBody = 512;

    public static string NormalizeApiBase(string? configured)
    {
        var value = string.IsNullOrWhiteSpace(configured) ? "https://api.telegram.org" : configured.Trim().TrimEnd('/');
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
            || (uri.Scheme == Uri.UriSchemeHttp && !IsLoopback(uri.Host)))
        {
            throw new InvalidOperationException("The Telegram API endpoint must use HTTPS, or plain HTTP only for a loopback (local Bot API server) host.");
        }
        return value;
    }

    private static bool IsLoopback(string host) =>
        host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
        (IPAddress.TryParse(host, out var address) && IPAddress.IsLoopback(address));

    public static bool LooksLikeBotToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token)) return false;
        var parts = token.Split(':');
        return parts.Length == 2 && parts[0].Length is >= 5 and <= 20 && parts[0].All(char.IsAsciiDigit)
            && parts[1].Length is >= 30 and <= 80 && parts[1].All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-');
    }

    public async Task<TelegramMe> GetMeAsync(string token, string apiBase, CancellationToken ct)
    {
        using var response = await http.PostAsync($"{NormalizeApiBase(apiBase)}/bot{token}/getMe", null, ct);
        using var document = await ParseAsync(response, ct);
        var result = document.RootElement.GetProperty("result");
        return new TelegramMe(
            result.TryGetProperty("id", out var id) ? id.GetInt64() : 0,
            result.TryGetProperty("username", out var username) ? username.GetString() : null,
            result.TryGetProperty("first_name", out var first) ? first.GetString() : null);
    }

    public async Task<IReadOnlyList<JsonDocument>> GetUpdatesAsync(string token, string apiBase, long offset, int timeoutSeconds, CancellationToken ct)
    {
        var payload = JsonSerializer.Serialize(new
        {
            offset,
            timeout = Math.Clamp(timeoutSeconds, 0, 50),
            limit = 50,
            allowed_updates = new[] { "message", "callback_query" }
        });
        using var response = await http.PostAsync($"{NormalizeApiBase(apiBase)}/bot{token}/getUpdates",
            new StringContent(payload, Encoding.UTF8, "application/json"), ct);
        using var document = await ParseAsync(response, ct);
        var updates = new List<JsonDocument>();
        if (document.RootElement.TryGetProperty("result", out var result) && result.ValueKind == JsonValueKind.Array)
        {
            foreach (var update in result.EnumerateArray())
            {
                updates.Add(JsonDocument.Parse(update.GetRawText()));
            }
        }
        return updates;
    }

    public async Task<TelegramFileInfo> GetFileAsync(string token, string apiBase, string fileId, CancellationToken ct)
    {
        var payload = JsonSerializer.Serialize(new { file_id = fileId });
        using var response = await http.PostAsync($"{NormalizeApiBase(apiBase)}/bot{token}/getFile",
            new StringContent(payload, Encoding.UTF8, "application/json"), ct);
        using var document = await ParseAsync(response, ct);
        var result = document.RootElement.GetProperty("result");
        return new TelegramFileInfo(
            result.TryGetProperty("file_id", out var id) ? id.GetString() ?? fileId : fileId,
            result.GetProperty("file_path").GetString() ?? string.Empty,
            result.TryGetProperty("file_size", out var size) ? size.GetInt64() : 0);
    }

    /// <summary>Streams a downloaded file into the supplied destination while enforcing a hard byte limit.</summary>
    public async Task<long> DownloadFileAsync(string token, string apiBase, string filePath, Stream destination, long maximumBytes, CancellationToken ct)
    {
        if (filePath.StartsWith('/') || filePath.Contains("..", StringComparison.Ordinal))
        {
            throw new TelegramBotException("The Telegram file path was rejected.");
        }
        using var response = await http.GetAsync($"{NormalizeApiBase(apiBase)}/file/bot{token}/{filePath}", HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new TelegramBotException($"Telegram file download failed with HTTP {(int)response.StatusCode}.", (int)response.StatusCode);
        }
        if (response.Content.Headers.ContentLength is { } length && length > maximumBytes)
        {
            throw new TelegramBotException("The Telegram file exceeds the configured size limit.");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = await stream.ReadAsync(buffer, ct)) > 0)
        {
            total += read;
            if (total > maximumBytes)
            {
                throw new TelegramBotException("The Telegram file exceeds the configured size limit.");
            }
            await destination.WriteAsync(buffer.AsMemory(0, read), ct);
        }
        return total;
    }

    public async Task<long> SendAudioAsync(string token, string apiBase, long chatId, long? threadId, string filePath,
        string fileName, string title, string performer, int? durationSeconds, string? caption, CancellationToken ct)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(chatId.ToString()), "chat_id");
        if (threadId is not null) form.Add(new StringContent(threadId.Value.ToString()), "message_thread_id");
        form.Add(new StringContent(Truncate(title, 200)), "title");
        form.Add(new StringContent(Truncate(performer, 200)), "performer");
        if (durationSeconds is > 0) form.Add(new StringContent(durationSeconds.Value.ToString()), "duration");
        if (!string.IsNullOrWhiteSpace(caption)) form.Add(new StringContent(Truncate(caption, 900)), "caption");

        await using var file = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var audio = new StreamContent(file);
        audio.Headers.ContentType = new MediaTypeHeaderValue("audio/mpeg");
        form.Add(audio, "audio", fileName);

        using var response = await http.PostAsync($"{NormalizeApiBase(apiBase)}/bot{token}/sendAudio", form, ct);
        using var document = await ParseAsync(response, ct);
        return document.RootElement.GetProperty("result").TryGetProperty("message_id", out var messageId) ? messageId.GetInt64() : 0;
    }

    public async Task SendMessageAsync(string token, string apiBase, long chatId, long? threadId, string text, CancellationToken ct)
    {
        var payload = JsonSerializer.Serialize(new
        {
            chat_id = chatId,
            message_thread_id = threadId,
            text = Truncate(text, 3800),
            disable_web_page_preview = true
        });
        using var response = await http.PostAsync($"{NormalizeApiBase(apiBase)}/bot{token}/sendMessage",
            new StringContent(payload, Encoding.UTF8, "application/json"), ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await SafeBodyAsync(response, ct);
            throw new TelegramBotException($"Telegram sendMessage failed with HTTP {(int)response.StatusCode}: {body}", (int)response.StatusCode);
        }
    }

    /// <summary>Sends a message with an inline keyboard, if one is supplied.</summary>
    public async Task SendMessageAsync(string token, string apiBase, long chatId, long? threadId, string text,
        TelegramKeyboard? keyboard, CancellationToken ct)
    {
        if (keyboard is null)
        {
            await SendMessageAsync(token, apiBase, chatId, threadId, text, ct);
            return;
        }
        if (keyboard.IsEmpty)
        {
            throw new ArgumentException("An inline keyboard must contain at least one row.", nameof(keyboard));
        }

        // reply_markup must be a JSON object, not a string containing JSON, so the keyboard is
        // embedded as an element rather than serialized twice.
        using var markup = JsonDocument.Parse(keyboard.ToJson());
        var payload = JsonSerializer.Serialize(new
        {
            chat_id = chatId,
            message_thread_id = threadId,
            text = Truncate(text, 3800),
            disable_web_page_preview = true,
            reply_markup = markup.RootElement
        });
        using var response = await http.PostAsync($"{NormalizeApiBase(apiBase)}/bot{token}/sendMessage",
            new StringContent(payload, Encoding.UTF8, "application/json"), ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await SafeBodyAsync(response, ct);
            throw new TelegramBotException($"Telegram sendMessage failed with HTTP {(int)response.StatusCode}: {body}", (int)response.StatusCode);
        }
    }

    /// <summary>
    /// Sends a message with an inline keyboard and returns the id Telegram assigned to it.
    /// </summary>
    /// <remarks>
    /// The id matters for the per-file album question: the prompt is stored against the message that
    /// asks it, and a later button tap is matched back through the id of the message it was tapped
    /// on. A zero return means Telegram accepted the message but did not report an id, which the
    /// caller must treat as "no question was asked" rather than storing an unusable prompt.
    /// </remarks>
    public async Task<long> SendMessageReturningIdAsync(string token, string apiBase, long chatId, long? threadId,
        string text, TelegramKeyboard? keyboard, CancellationToken ct)
    {
        if (keyboard is null || keyboard.IsEmpty)
        {
            throw new ArgumentException("An inline keyboard must contain at least one row.", nameof(keyboard));
        }

        using var markup = JsonDocument.Parse(keyboard.ToJson());
        var payload = JsonSerializer.Serialize(new
        {
            chat_id = chatId,
            message_thread_id = threadId,
            text = Truncate(text, 3800),
            disable_web_page_preview = true,
            reply_markup = markup.RootElement
        });
        using var response = await http.PostAsync($"{NormalizeApiBase(apiBase)}/bot{token}/sendMessage",
            new StringContent(payload, Encoding.UTF8, "application/json"), ct);
        using var document = await ParseAsync(response, ct);
        return document.RootElement.TryGetProperty("result", out var result)
               && result.TryGetProperty("message_id", out var messageId)
               && messageId.TryGetInt64(out var parsed)
            ? parsed
            : 0;
    }

    /// <summary>
    /// Edits an existing message in place, optionally replacing its inline keyboard. The bot edits
    /// rather than sending again so a chat does not fill with dead menus.
    /// </summary>
    public async Task EditMessageTextAsync(string token, string apiBase, long chatId, long messageId, string text,
        TelegramKeyboard? keyboard, CancellationToken ct)
    {
        string payload;
        if (keyboard is null)
        {
            payload = JsonSerializer.Serialize(new
            {
                chat_id = chatId,
                message_id = messageId,
                text = Truncate(text, 3800),
                disable_web_page_preview = true
            });
        }
        else
        {
            using var markup = JsonDocument.Parse(keyboard.ToJson());
            payload = JsonSerializer.Serialize(new
            {
                chat_id = chatId,
                message_id = messageId,
                text = Truncate(text, 3800),
                disable_web_page_preview = true,
                reply_markup = markup.RootElement
            });
        }

        using var response = await http.PostAsync($"{NormalizeApiBase(apiBase)}/bot{token}/editMessageText",
            new StringContent(payload, Encoding.UTF8, "application/json"), ct);
        using var document = await ParseAsync(response, ct);
    }

    /// <summary>
    /// Acknowledges a callback query. Callers must invoke this on every path, including failures,
    /// because an unanswered query leaves a spinner on the client until it times out.
    /// </summary>
    public async Task AnswerCallbackQueryAsync(string token, string apiBase, string callbackQueryId, string? text,
        CancellationToken ct)
    {
        var payload = JsonSerializer.Serialize(new
        {
            callback_query_id = callbackQueryId,
            text = text is null ? null : Truncate(text, 200)
        });
        using var response = await http.PostAsync($"{NormalizeApiBase(apiBase)}/bot{token}/answerCallbackQuery",
            new StringContent(payload, Encoding.UTF8, "application/json"), ct);
        using var document = await ParseAsync(response, ct);
    }

    /// <summary>Shows a transient chat action such as <c>typing</c>.</summary>
    public async Task SendChatActionAsync(string token, string apiBase, long chatId, long? threadId, string action,
        CancellationToken ct)
    {
        var payload = JsonSerializer.Serialize(new
        {
            chat_id = chatId,
            message_thread_id = threadId,
            action
        });
        using var response = await http.PostAsync($"{NormalizeApiBase(apiBase)}/bot{token}/sendChatAction",
            new StringContent(payload, Encoding.UTF8, "application/json"), ct);
        using var document = await ParseAsync(response, ct);
    }

    private static async Task<JsonDocument> ParseAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var body = await response.Content.ReadAsStringAsync(ct);
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            throw new TelegramBotException($"Telegram returned a non-JSON response with HTTP {(int)response.StatusCode}.", (int)response.StatusCode);
        }

        var ok = document.RootElement.TryGetProperty("ok", out var okElement) && okElement.ValueKind == JsonValueKind.True;
        if (!response.IsSuccessStatusCode || !ok)
        {
            var description = document.RootElement.TryGetProperty("description", out var descriptionElement)
                ? descriptionElement.GetString()
                : null;
            var code = document.RootElement.TryGetProperty("error_code", out var codeElement) && codeElement.TryGetInt32(out var parsed)
                ? parsed
                : (int)response.StatusCode;
            document.Dispose();
            throw new TelegramBotException(Truncate(description ?? "Telegram request failed.", MaxErrorBody), code);
        }
        return document;
    }

    private static async Task<string> SafeBodyAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            return Truncate(body, MaxErrorBody);
        }
        catch
        {
            return "unreadable response body";
        }
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}
