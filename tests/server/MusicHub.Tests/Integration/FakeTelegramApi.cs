using System.Net;
using System.Text;
using System.Text.Json;

namespace MusicHub.Tests.Integration;

/// <summary>
/// A deterministic stand-in for the Telegram Bot API.
/// </summary>
/// <remarks>
/// <para>
/// Every response is a fixed shape, message ids are handed out from a fixed counter and nothing is
/// random, so a test can assert on exact bytes and on the exact call sequence. Requests are recorded
/// with their parsed JSON body, which is how the router tests prove that
/// <c>answerCallbackQuery</c> was called on the error path and that a document really reached the
/// importer.
/// </para>
/// <para>
/// <c>getUpdates</c> honours the requested offset the way the real API does: it returns only updates
/// whose <c>update_id</c> is at least the offset. That is what lets one test poll twice.
/// </para>
/// </remarks>
internal sealed class FakeTelegramApi : IDisposable
{
    private readonly HttpListener _listener = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _loop;
    private readonly List<RecordedRequest> _requests = new();
    private readonly object _gate = new();
    private long _nextMessageId = 1000;

    public FakeTelegramApi()
    {
        var port = FreePort();
        ApiBase = $"http://127.0.0.1:{port}";
        _listener.Prefixes.Add(ApiBase + "/");
        _listener.Start();
        _loop = Task.Run(ServeAsync);
    }

    public string ApiBase { get; }

    /// <summary>Raw Telegram <c>result</c> array for <c>getUpdates</c>.</summary>
    public string PendingUpdates { get; set; } = "[]";

    /// <summary>Bytes served for any file download.</summary>
    public byte[] FileContent { get; set; } = Array.Empty<byte>();

    /// <summary>The <c>file_path</c> returned by <c>getFile</c>.</summary>
    public string FilePath { get; set; } = "documents/w035-fixture.wav";

    /// <summary>When set, <c>sendMessage</c> and <c>editMessageText</c> answer with HTTP 400 and this description.</summary>
    public string? TextErrorDescription { get; set; }

    public long? LastRequestedOffset { get; private set; }

    public IReadOnlyList<RecordedRequest> Requests
    {
        get { lock (_gate) return _requests.ToList(); }
    }

    public IReadOnlyList<RecordedRequest> Calls(string method)
    {
        lock (_gate) return _requests.Where(x => x.Method == method).ToList();
    }

    public RecordedRequest SingleCall(string method)
    {
        var calls = Calls(method);
        Assert.True(calls.Count == 1, $"Expected exactly one {method} call but observed {calls.Count}.");
        return calls[0];
    }

    private async Task ServeAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync();
            }
            catch (Exception) when (_stop.IsCancellationRequested)
            {
                return;
            }
            catch (HttpListenerException)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }

            using (context.Response)
            {
                try
                {
                    await HandleAsync(context);
                }
                catch (Exception ex)
                {
                    await TryWriteAsync(context, 500, "application/json",
                        $"{{\"ok\":false,\"error_code\":500,\"description\":{JsonSerializer.Serialize(ex.Message)}}}");
                }
            }
        }
    }

    private async Task HandleAsync(HttpListenerContext context)
    {
        var path = context.Request.Url!.AbsolutePath;

        if (context.Request.HttpMethod == "GET" && path.StartsWith("/file/", StringComparison.Ordinal))
        {
            lock (_gate) _requests.Add(new RecordedRequest("download", path, null));
            await WriteBytesAsync(context, FileContent);
            return;
        }

        var raw = string.Empty;
        if (context.Request.HasEntityBody)
        {
            using var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8);
            raw = await reader.ReadToEndAsync();
        }

        var method = path.TrimEnd('/').Split('/').Last();
        lock (_gate) _requests.Add(new RecordedRequest(method, path, Parse(raw)));

        switch (method)
        {
            case "getUpdates":
                await WriteJsonAsync(context, BuildUpdatesResponse(raw));
                return;

            case "getFile":
                await WriteJsonAsync(context,
                    "{\"ok\":true,\"result\":{\"file_id\":\"w035-file-id\",\"file_unique_id\":\"w035-unique\","
                    + $"\"file_path\":{JsonSerializer.Serialize(FilePath)},\"file_size\":{FileContent.Length}}}}}");
                return;

            case "sendMessage":
            case "editMessageText":
                if (TextErrorDescription is not null)
                {
                    await WriteJsonAsync(context, 400,
                        $"{{\"ok\":false,\"error_code\":400,\"description\":{JsonSerializer.Serialize(TextErrorDescription)}}}");
                    return;
                }
                await WriteJsonAsync(context, BuildMessageResponse(raw));
                return;

            case "sendAudio":
                await WriteJsonAsync(context, MessageResponse(NextMessageId(), 0, "audio"));
                return;

            case "answerCallbackQuery":
            case "sendChatAction":
                await WriteJsonAsync(context, "{\"ok\":true,\"result\":true}");
                return;

            case "getMe":
                await WriteJsonAsync(context,
                    "{\"ok\":true,\"result\":{\"id\":100000000,\"is_bot\":true,\"first_name\":\"MusicHub\",\"username\":\"musichub_test_bot\"}}");
                return;

            default:
                await WriteJsonAsync(context, "{\"ok\":true,\"result\":true}");
                return;
        }
    }

    private string BuildUpdatesResponse(string rawBody)
    {
        long offset = 0;
        if (!string.IsNullOrWhiteSpace(rawBody))
        {
            using var request = JsonDocument.Parse(rawBody);
            if (request.RootElement.TryGetProperty("offset", out var offsetElement) && offsetElement.TryGetInt64(out var parsed))
            {
                offset = parsed;
            }
        }
        LastRequestedOffset = offset;

        var selected = new List<string>();
        using (var updates = JsonDocument.Parse(PendingUpdates))
        {
            foreach (var update in updates.RootElement.EnumerateArray())
            {
                var updateId = update.TryGetProperty("update_id", out var id) && id.TryGetInt64(out var parsedId) ? parsedId : 0;
                if (updateId >= offset) selected.Add(update.GetRawText());
            }
        }

        return "{\"ok\":true,\"result\":[" + string.Join(",", selected) + "]}";
    }

    private string BuildMessageResponse(string rawBody)
    {
        var chatId = 0L;
        var messageId = 0L;
        var text = string.Empty;
        if (!string.IsNullOrWhiteSpace(rawBody))
        {
            using var body = JsonDocument.Parse(rawBody);
            if (body.RootElement.TryGetProperty("chat_id", out var chat) && chat.TryGetInt64(out var parsedChat)) chatId = parsedChat;
            if (body.RootElement.TryGetProperty("message_id", out var message) && message.TryGetInt64(out var parsedMessage)) messageId = parsedMessage;
            if (body.RootElement.TryGetProperty("text", out var textElement)) text = textElement.GetString() ?? string.Empty;
        }

        return MessageResponse(messageId == 0 ? NextMessageId() : messageId, chatId, text);
    }

    private string MessageResponse(long messageId, long chatId, string text) =>
        "{\"ok\":true,\"result\":{\"message_id\":" + messageId
        + ",\"date\":1700000000,\"chat\":{\"id\":" + chatId + ",\"type\":\"private\"}"
        + ",\"text\":" + JsonSerializer.Serialize(text) + "}}";

    private long NextMessageId()
    {
        lock (_gate) return _nextMessageId++;
    }

    private static JsonDocument? Parse(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        try
        {
            var document = JsonDocument.Parse(raw);
            // The stream is returned as an owned copy so the caller keeps a live element after the
            // socket is closed.
            return JsonDocument.Parse(document.RootElement.GetRawText());
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static Task WriteJsonAsync(HttpListenerContext context, string json) =>
        WriteJsonAsync(context, 200, json);

    private static async Task WriteJsonAsync(HttpListenerContext context, int status, string json)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/json";
        context.Response.ContentLength64 = bytes.Length;
        await context.Response.OutputStream.WriteAsync(bytes);
    }

    private static async Task WriteBytesAsync(HttpListenerContext context, byte[] content)
    {
        context.Response.StatusCode = 200;
        context.Response.ContentType = "application/octet-stream";
        context.Response.ContentLength64 = content.Length;
        await context.Response.OutputStream.WriteAsync(content);
    }

    private static async Task TryWriteAsync(HttpListenerContext context, int status, string contentType, string body)
    {
        try
        {
            var bytes = Encoding.UTF8.GetBytes(body);
            context.Response.StatusCode = status;
            context.Response.ContentType = contentType;
            context.Response.ContentLength64 = bytes.Length;
            await context.Response.OutputStream.WriteAsync(bytes);
        }
        catch
        {
            // The client hung up; nothing useful to report from a test double.
        }
    }

    private static int FreePort()
    {
        using var socket = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        socket.Start();
        var port = ((IPEndPoint)socket.LocalEndpoint).Port;
        socket.Stop();
        return port;
    }

    public void Dispose()
    {
        _stop.Cancel();
        try { _listener.Stop(); } catch { }
        try { _listener.Close(); } catch { }
        try { _loop.Wait(TimeSpan.FromSeconds(5)); } catch { }
        _stop.Dispose();
        foreach (var request in Requests) request.Body?.Dispose();
    }
}

/// <summary>One recorded Bot API call: the method name, the request path and the parsed body.</summary>
internal sealed class RecordedRequest(string method, string path, JsonDocument? body)
{
    public string Method { get; } = method;
    public string Path { get; } = path;
    public JsonDocument? Body { get; } = body;

    public JsonElement Root => Body!.RootElement;

    public string? String(string property)
    {
        var root = Body?.RootElement;
        return root is { ValueKind: JsonValueKind.Object } element
            && element.TryGetProperty(property, out var value)
            && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
    }

    public long? Number(string property)
    {
        var root = Body?.RootElement;
        return root is { ValueKind: JsonValueKind.Object } element
            && element.TryGetProperty(property, out var value)
            && value.TryGetInt64(out var parsed)
                ? parsed
                : null;
    }
}
