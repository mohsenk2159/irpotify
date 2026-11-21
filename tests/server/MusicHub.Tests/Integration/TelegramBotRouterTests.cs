using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MusicHub.Domain;
using MusicHub.Infrastructure;
using MusicHub.Infrastructure.Telegram;

namespace MusicHub.Tests.Integration;

/// <summary>
/// Exercises the Phase 0 bot foundation through the real ingestion service against a deterministic
/// stand-in for the Telegram Bot API.
/// </summary>
/// <remarks>
/// The most important test in this file is <see cref="A_document_update_still_reaches_the_importer"/>
/// and its companion <see cref="A_non_audio_message_keeps_the_frozen_rejection"/>: they prove the
/// new router did not disturb the ingestion path that was verified end to end with a real bot on
/// 2026-11-19. A regression there is a release blocker.
/// </remarks>
[Collection("api")]
public class TelegramBotRouterTests(ApiFactory factory)
{
    private const string FakeBotToken = "123456789:AAFakeTokenValueForTestingOnly_1234567890";
    private const string TokenSecretPart = "AAFakeTokenValueForTestingOnly";
    private const long AdminUserId = 900501;
    private const long AdminChatId = 900501;
    private const long StrangerId = 111222;
    private const string FrozenRejection = "Message did not contain a supported audio document.";
    private const string FrozenAcceptReply = "Only audio documents are accepted (MP3, FLAC, WAV, M4A, AAC, OGG, OPUS).";

    // ---- frozen ingestion path ---------------------------------------------------------------

    [Fact]
    public async Task A_document_update_still_reaches_the_importer()
    {
        using var api = new FakeTelegramApi();
        try
        {
            var wav = ApiFactory.BuildWav(9311); // unique sample count: fixtures dedupe by SHA-256
            api.FileContent = wav;
            api.FilePath = "documents/w035-import.wav";
            api.PendingUpdates = $$"""
                [ { "update_id": 5101, "message": {
                    "message_id": 11, "date": 1700000000,
                    "from": { "id": {{AdminUserId}}, "is_bot": false, "first_name": "Admin" },
                    "chat": { "id": {{AdminChatId}}, "type": "private" },
                    "document": { "file_id": "w035-doc-1", "file_name": "w035-import.wav",
                                  "mime_type": "audio/wav", "file_size": {{wav.Length}} } } } ]
                """;
            await ConfigureAsync(api);

            var result = await PollOnceAsync();

            Assert.Equal(1, result.Imported);
            Assert.Equal(1, result.UpdatesProcessed);

            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<MusicHubDbContext>();

            var import = await db.TelegramImports.AsNoTracking().SingleAsync(x => x.TelegramUpdateId == 5101);
            Assert.Equal(JobStatus.Completed, import.Status);
            Assert.Equal("w035-import.wav", import.FileName);
            Assert.Null(import.Error);
            Assert.NotNull(import.TrackId);
            Assert.True(await db.Tracks.AnyAsync(x => x.Id == import.TrackId));

            // The audit row and the offset are unchanged by the router.
            var update = await db.TelegramUpdates.AsNoTracking().SingleAsync(x => x.TelegramUpdateId == 5101);
            Assert.NotNull(update.ProcessedAt);
            Assert.Null(update.Error);
            var offset = await db.AppSettings.AsNoTracking().SingleAsync(x => x.Key == "telegram.updateOffset");
            Assert.Equal("5102", offset.Value);

            // The download ran and the operator was asked which album the file belongs to. The
            // album question intentionally replaces the old bare acceptance reply: the track is
            // already imported either way, and the question is the next step of the same flow.
            Assert.Single(api.Calls("getFile"));
            Assert.Single(api.Calls("download"));
            Assert.Contains(api.Calls("sendMessage"),
                request => (request.String("text") ?? string.Empty).Contains("w035-import.wav", StringComparison.Ordinal));
        }
        finally
        {
            await ResetAsync();
        }
    }

    [Fact]
    public async Task A_non_audio_message_keeps_the_frozen_rejection()
    {
        using var api = new FakeTelegramApi();
        try
        {
            api.PendingUpdates = $$"""
                [ { "update_id": 5201, "message": { "message_id": 21, "date": 1700000000,
                      "from": { "id": {{AdminUserId}} }, "chat": { "id": {{AdminChatId}}, "type": "private" },
                      "photo": [ { "file_id": "p1", "width": 90, "height": 90 } ] } },
                  { "update_id": 5202, "message": { "message_id": 22, "date": 1700000000,
                      "from": { "id": {{AdminUserId}} }, "chat": { "id": {{AdminChatId}}, "type": "private" },
                      "text": "just some text" } } ]
                """;
            await ConfigureAsync(api);

            var result = await PollOnceAsync();

            Assert.Equal(0, result.Imported);
            Assert.Equal(2, result.Rejected);

            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<MusicHubDbContext>();
            var imports = await db.TelegramImports.AsNoTracking()
                .Where(x => x.TelegramUpdateId == 5201 || x.TelegramUpdateId == 5202)
                .ToListAsync();
            Assert.Equal(2, imports.Count);
            Assert.All(imports, row =>
            {
                Assert.Equal(JobStatus.Failed, row.Status);
                Assert.Equal(FrozenRejection, row.Error);
            });

            // Nothing was downloaded and the operator got the unchanged guidance text.
            Assert.Empty(api.Calls("getFile"));
            Assert.Empty(api.Calls("download"));
            Assert.Contains(api.Calls("sendMessage"), request => request.String("text") == FrozenAcceptReply);
            // No keyboard on a rejection: the frozen replies are plain text.
            Assert.All(api.Calls("sendMessage"), request =>
                Assert.False(request.Root.TryGetProperty("reply_markup", out _)));
        }
        finally
        {
            await ResetAsync();
        }
    }

    // ---- administrator gate -----------------------------------------------------------------

    [Fact]
    public async Task A_stranger_gets_one_fixed_denial_and_no_state_is_written()
    {
        using var api = new FakeTelegramApi();
        try
        {
            api.PendingUpdates = StrangerCommand(5301, "/start");
            await ConfigureAsync(api);

            var result = await PollOnceAsync();

            Assert.Equal(1, result.Rejected);
            var reply = api.SingleCall("sendMessage");
            Assert.Equal(TelegramBotTexts.Denial, reply.String("text"));
            Assert.DoesNotContain(AdminChatId.ToString(), reply.String("text")!, StringComparison.Ordinal);
            Assert.DoesNotContain(AdminUserId.ToString(), reply.String("text")!, StringComparison.Ordinal);
            Assert.False(reply.Root.TryGetProperty("reply_markup", out _));

            // No state, no screen, no import audit row: the denial happens before any handler runs.
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<MusicHubDbContext>();
                Assert.False(await db.AppSettings.AnyAsync(x => x.Key.StartsWith(TelegramBotStateStore.KeyPrefix)));
                Assert.False(await db.TelegramImports.AnyAsync(x => x.TelegramUpdateId == 5301));
            }

            // The administrator writing from the wrong chat gets exactly the same text. The denial
            // therefore says nothing about whether a chat id is configured.
            api.PendingUpdates = AdminCommand(5302, "/start", chatId: -1009876543210);
            var second = await PollOnceAsync();

            Assert.Equal(1, second.Rejected);
            var replies = api.Calls("sendMessage");
            Assert.Equal(2, replies.Count);
            Assert.Equal(replies[0].String("text"), replies[1].String("text"));
        }
        finally
        {
            await ResetAsync();
        }
    }

    [Fact]
    public async Task A_callback_from_a_stranger_is_denied_before_the_payload_is_parsed()
    {
        using var api = new FakeTelegramApi();
        try
        {
            // Malformed data proves ordering: a parsed payload would produce the "unknown action"
            // answer, while the gate produces the fixed denial.
            api.PendingUpdates = """
                [ { "update_id": 5701, "callback_query": { "id": "cb-5701", "from": { "id": 111222 },
                      "data": "<<<not-a-callback>>>",
                      "message": { "message_id": 33, "chat": { "id": 111222, "type": "private" } } } } ]
                """;
            await ConfigureAsync(api);

            var result = await PollOnceAsync();

            Assert.Equal(1, result.Rejected);
            var answer = api.SingleCall("answerCallbackQuery");
            Assert.Equal(TelegramBotTexts.Denial, answer.String("text"));
            Assert.Empty(api.Calls("editMessageText"));
            Assert.Empty(api.Calls("sendMessage"));

            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<MusicHubDbContext>();
            Assert.False(await db.AppSettings.AnyAsync(x => x.Key.StartsWith(TelegramBotStateStore.KeyPrefix)));
        }
        finally
        {
            await ResetAsync();
        }
    }

    // ---- the one real screen ----------------------------------------------------------------

    [Fact]
    public async Task Start_renders_the_real_status_and_the_panel_buttons()
    {
        using var api = new FakeTelegramApi();
        try
        {
            const string heartbeat = "2026-11-19T10:11:12.0000000+00:00";
            api.PendingUpdates = AdminCommand(5401, "/start");
            await ConfigureAsync(api);
            // The seeded offset is also the offset the poll requests, so it must be below the
            // update id or the fake API would (correctly) withhold this very update.
            await SeedSettingAsync(TelegramBotStatusService.OffsetSettingKey, "4242");
            await SeedSettingAsync(TelegramBotStatusService.HeartbeatSettingKey, heartbeat);

            int importCount;
            int unpublished;
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<MusicHubDbContext>();
                importCount = await db.TelegramImports.CountAsync();
                unpublished = await db.TelegramPublications.CountAsync(x => x.Status != TelegramPublishStatus.Published);
            }

            var result = await PollOnceAsync();

            Assert.Equal(1, result.UpdatesProcessed);
            Assert.Equal(0, result.Rejected);

            var message = api.SingleCall("sendMessage");
            var text = message.String("text")!;
            Assert.Contains("4242", text, StringComparison.Ordinal);
            Assert.Contains(heartbeat, text, StringComparison.Ordinal);
            Assert.Contains(
                $"{TelegramBotTexts.Get(TelegramBotTextIds.StatusImports)}: {importCount}",
                text, StringComparison.Ordinal);
            Assert.Contains(
                $"{TelegramBotTexts.Get(TelegramBotTextIds.StatusPublications)}: {unpublished}",
                text, StringComparison.Ordinal);

            // Every button leads to a screen that exists: reports, album management, the artist
            // report, the storage screen, and a refresh that re-reads the status.
            var rows = message.Root.GetProperty("reply_markup").GetProperty("inline_keyboard");
            Assert.Equal(3, rows.GetArrayLength());
            Assert.Equal("v1:reports", rows[0][0].GetProperty("callback_data").GetString());
            Assert.Equal("v1:amalbs:0", rows[0][1].GetProperty("callback_data").GetString());
            Assert.Equal("v1:artists:0", rows[1][0].GetProperty("callback_data").GetString());
            Assert.Equal("v1:storage", rows[1][1].GetProperty("callback_data").GetString());
            Assert.Equal("v1:refresh", rows[2][0].GetProperty("callback_data").GetString());

            // A visible working signal, and a session row that matches the documented shape.
            Assert.Single(api.Calls("sendChatAction"));
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<MusicHubDbContext>();
                var state = await db.AppSettings.AsNoTracking()
                    .SingleAsync(x => x.Key == TelegramBotStateStore.KeyFor(AdminChatId));
                using var json = JsonDocument.Parse(state.Value);
                Assert.Equal("status", json.RootElement.GetProperty("state").GetString());
                Assert.True(json.RootElement.TryGetProperty("payload", out _));
                var updatedAt = json.RootElement.GetProperty("updatedAt").GetDateTimeOffset();
                Assert.True(Math.Abs((DateTimeOffset.UtcNow - updatedAt).TotalMinutes) < 2,
                    "the session timestamp must be written at render time");

                // A command is not an import attempt.
                Assert.False(await db.TelegramImports.AnyAsync(x => x.TelegramUpdateId == 5401));
            }
        }
        finally
        {
            await ResetAsync();
        }
    }

    [Fact]
    public async Task The_refresh_button_re_reads_the_database_and_edits_in_place()
    {
        using var api = new FakeTelegramApi();
        try
        {
            api.PendingUpdates = AdminCommand(5501, "/status");
            await ConfigureAsync(api);
            await SeedSettingAsync(TelegramBotStatusService.OffsetSettingKey, "4242");
            await PollOnceAsync();
            var before = await ReadSessionUpdatedAtAsync();

            // Real state moves on; the refresh must show the new value, not the rendered one.
            await SeedSettingAsync(TelegramBotStatusService.OffsetSettingKey, "4243");
            api.PendingUpdates = AdminCallback(5502, "cb-5502", "v1:refresh", messageId: 1000);

            var result = await PollOnceAsync();

            Assert.Equal(0, result.Rejected);
            var answer = api.SingleCall("answerCallbackQuery");
            Assert.Equal("cb-5502", answer.String("callback_query_id"));
            Assert.Equal(TelegramBotTexts.Get(TelegramBotTextIds.Refreshed), answer.String("text"));

            var edit = api.SingleCall("editMessageText");
            Assert.Equal(AdminChatId, edit.Number("chat_id"));
            Assert.Equal(1000, edit.Number("message_id"));
            Assert.Contains("4243", edit.String("text")!, StringComparison.Ordinal);
            Assert.DoesNotContain("4242", edit.String("text")!, StringComparison.Ordinal);
            Assert.Equal("v1:refresh",
                edit.Root.GetProperty("reply_markup").GetProperty("inline_keyboard")[2][0].GetProperty("callback_data").GetString());

            // Edited, never re-sent: the chat does not accumulate menus.
            Assert.Single(api.Calls("sendMessage"));
            Assert.True(await ReadSessionUpdatedAtAsync() > before, "the session must slide forward on use");
        }
        finally
        {
            await ResetAsync();
        }
    }

    [Fact]
    public async Task An_expired_session_is_rebuilt_and_reported()
    {
        using var api = new FakeTelegramApi();
        try
        {
            api.PendingUpdates = AdminCommand(5601, "/start");
            await ConfigureAsync(api);
            await PollOnceAsync();
            await ExpireSessionAsync();

            api.PendingUpdates = AdminCallback(5602, "cb-5602", "v1:refresh", messageId: 1000);
            var result = await PollOnceAsync();

            Assert.Equal(0, result.Rejected);
            var answer = api.SingleCall("answerCallbackQuery");
            Assert.Equal(TelegramBotTexts.Get(TelegramBotTextIds.SessionExpired), answer.String("text"));
            Assert.Single(api.Calls("editMessageText"));

            var rebuilt = await ReadSessionUpdatedAtAsync();
            Assert.True(Math.Abs((DateTimeOffset.UtcNow - rebuilt).TotalMinutes) < 2,
                "an expired session must be replaced, not resurrected");
        }
        finally
        {
            await ResetAsync();
        }
    }

    [Fact]
    public async Task A_refresh_that_changes_nothing_is_acknowledged_as_already_current()
    {
        using var api = new FakeTelegramApi();
        try
        {
            api.PendingUpdates = AdminCommand(5951, "/start");
            await ConfigureAsync(api);
            await PollOnceAsync();

            // Telegram answers 400 when an edit does not change the message.
            api.TextErrorDescription = "Bad Request: message is not modified";
            api.PendingUpdates = AdminCallback(5952, "cb-5952", "v1:refresh", messageId: 1000);

            var result = await PollOnceAsync();

            Assert.Equal(0, result.Rejected);
            Assert.Equal(TelegramBotTexts.Get(TelegramBotTextIds.AlreadyCurrent),
                api.SingleCall("answerCallbackQuery").String("text"));
        }
        finally
        {
            await ResetAsync();
        }
    }

    [Fact]
    public async Task Cancel_clears_the_stored_session()
    {
        using var api = new FakeTelegramApi();
        try
        {
            api.PendingUpdates = AdminCommand(5801, "/start");
            await ConfigureAsync(api);
            await PollOnceAsync();

            api.PendingUpdates = AdminCommand(5802, "/cancel");
            await PollOnceAsync();

            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<MusicHubDbContext>();
            Assert.False(await db.AppSettings
                .AnyAsync(x => x.Key == TelegramBotStateStore.KeyFor(AdminChatId)));

            var replies = api.Calls("sendMessage");
            Assert.Equal(2, replies.Count);
            Assert.Equal(TelegramBotTexts.Get(TelegramBotTextIds.Cancelled), replies[1].String("text"));
        }
        finally
        {
            await ResetAsync();
        }
    }

    [Fact]
    public async Task Help_and_unbuilt_commands_answer_truthfully_and_never_echo_the_token()
    {
        using var api = new FakeTelegramApi();
        try
        {
            api.PendingUpdates = $$"""
                [ { "update_id": 5901, "message": { "message_id": 91, "date": 1700000000,
                      "from": { "id": {{AdminUserId}} }, "chat": { "id": {{AdminChatId}}, "type": "private" },
                      "text": "/help" } },
                  { "update_id": 5902, "message": { "message_id": 92, "date": 1700000000,
                      "from": { "id": {{AdminUserId}} }, "chat": { "id": {{AdminChatId}}, "type": "private" },
                      "text": "/library" } } ]
                """;
            await ConfigureAsync(api);

            await PollOnceAsync();

            var replies = api.Calls("sendMessage");
            Assert.Equal(2, replies.Count);
            Assert.Equal(TelegramBotTexts.Get(TelegramBotTextIds.Help), replies[0].String("text"));
            Assert.Equal(TelegramBotTexts.UnknownCommandText(), replies[1].String("text"));
            // Phase 0 ships no button or menu entry for a screen that does not exist.
            Assert.All(replies, reply => Assert.False(reply.Root.TryGetProperty("reply_markup", out _)));

            // Nothing the bot sends ever contains the token, not even its secret part.
            Assert.All(api.Requests, request =>
            {
                var body = request.Body?.RootElement.GetRawText() ?? string.Empty;
                Assert.DoesNotContain(TokenSecretPart, body, StringComparison.Ordinal);
                Assert.DoesNotContain(FakeBotToken, body, StringComparison.Ordinal);
            });
        }
        finally
        {
            await ResetAsync();
        }
    }

    // ---- helpers ----------------------------------------------------------------------------

    private static string AdminCommand(long updateId, string text, long chatId = AdminChatId) => $$"""
        [ { "update_id": {{updateId}}, "message": { "message_id": {{updateId}}, "date": 1700000000,
              "from": { "id": {{AdminUserId}}, "is_bot": false, "first_name": "Admin" },
              "chat": { "id": {{chatId}}, "type": "private" },
              "text": {{JsonSerializer.Serialize(text)}} } } ]
        """;

    private static string StrangerCommand(long updateId, string text) => $$"""
        [ { "update_id": {{updateId}}, "message": { "message_id": {{updateId}}, "date": 1700000000,
              "from": { "id": {{StrangerId}}, "is_bot": false, "first_name": "Stranger" },
              "chat": { "id": {{StrangerId}}, "type": "private" },
              "text": {{JsonSerializer.Serialize(text)}} } } ]
        """;

    private static string AdminCallback(long updateId, string callbackQueryId, string data, long messageId) => $$"""
        [ { "update_id": {{updateId}}, "callback_query": { "id": {{JsonSerializer.Serialize(callbackQueryId)}},
              "from": { "id": {{AdminUserId}}, "is_bot": false, "first_name": "Admin" },
              "data": {{JsonSerializer.Serialize(data)}},
              "message": { "message_id": {{messageId}}, "chat": { "id": {{AdminChatId}}, "type": "private" } } } } ]
        """;

    private async Task<TelegramIngestionResult> PollOnceAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var ingestion = scope.ServiceProvider.GetRequiredService<TelegramIngestionService>();
        return await ingestion.PollOnceAsync(CancellationToken.None);
    }

    /// <summary>
    /// Points the stored configuration at the fake Bot API. The token is encrypted with the real
    /// protector, exactly as the dashboard writes it, and every id the tests use is a placeholder.
    /// </summary>
    private async Task ConfigureAsync(FakeTelegramApi api, long? adminChatId = AdminChatId)
    {
        // Ingestion attributes every import to an active administrator. Bootstrapping one here keeps
        // this class independent of whether another test in the shared collection already did it:
        // without it, ResolveImportingUserIdAsync returns null and every document is rejected with
        // "No active administrator account for attribution." when the class runs on its own.
        using (var bootstrap = await factory.CreateAdminClientAsync())
        {
            Assert.NotNull(bootstrap);
        }

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MusicHubDbContext>();
        var protector = scope.ServiceProvider.GetRequiredService<TelegramSecretProtector>();

        db.TelegramConfigurations.RemoveRange(await db.TelegramConfigurations.ToListAsync());
        db.TelegramConfigurations.Add(new TelegramConfiguration
        {
            BotTokenProtected = protector.Protect(FakeBotToken),
            AdminTelegramUserId = AdminUserId,
            AdminChatId = adminChatId,
            IngestionEnabled = true,
            AutoPublishEnabled = false,
            ApiEndpoint = api.ApiBase,
            LocalBotApiEndpoint = null,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
    }

    private async Task SeedSettingAsync(string key, string value)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MusicHubDbContext>();
        var row = await db.AppSettings.SingleOrDefaultAsync(x => x.Key == key);
        if (row is null)
        {
            db.AppSettings.Add(new AppSetting { Key = key, Value = value, UpdatedAt = DateTimeOffset.UtcNow });
        }
        else
        {
            row.Value = value;
            row.UpdatedAt = DateTimeOffset.UtcNow;
        }
        await db.SaveChangesAsync();
    }

    private async Task<DateTimeOffset> ReadSessionUpdatedAtAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MusicHubDbContext>();
        var row = await db.AppSettings.AsNoTracking()
            .SingleAsync(x => x.Key == TelegramBotStateStore.KeyFor(AdminChatId));
        using var json = JsonDocument.Parse(row.Value);
        return json.RootElement.GetProperty("updatedAt").GetDateTimeOffset();
    }

    /// <summary>Rewrites the stored session so it is already past the 15 minute inactivity window.</summary>
    private async Task ExpireSessionAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MusicHubDbContext>();
        var row = await db.AppSettings.SingleAsync(x => x.Key == TelegramBotStateStore.KeyFor(AdminChatId));
        row.Value = JsonSerializer.Serialize(new
        {
            state = "status",
            payload = (object?)null,
            updatedAt = DateTimeOffset.UtcNow - TelegramBotStateStore.Lifetime - TimeSpan.FromMinutes(1)
        });
        row.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
    }

    /// <summary>Removes every trace of this class's Telegram state so the shared suite stays order-independent.</summary>
    private async Task ResetAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MusicHubDbContext>();

        db.TelegramConfigurations.RemoveRange(await db.TelegramConfigurations.ToListAsync());
        db.TelegramImports.RemoveRange(await db.TelegramImports
            .Where(x => x.TelegramUpdateId >= 5000 && x.TelegramUpdateId < 6000).ToListAsync());
        db.TelegramUpdates.RemoveRange(await db.TelegramUpdates
            .Where(x => x.TelegramUpdateId >= 5000 && x.TelegramUpdateId < 6000).ToListAsync());
        var settings = await db.AppSettings
            .Where(x => x.Key == TelegramBotStatusService.OffsetSettingKey
                        || x.Key == TelegramBotStatusService.HeartbeatSettingKey
                        || x.Key.StartsWith(TelegramBotStateStore.KeyPrefix))
            .ToListAsync();
        db.AppSettings.RemoveRange(settings);
        await db.SaveChangesAsync();
    }
}
