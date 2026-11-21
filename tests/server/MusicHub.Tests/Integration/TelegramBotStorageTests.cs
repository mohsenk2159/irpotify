using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MusicHub.Domain;
using MusicHub.Infrastructure;
using MusicHub.Infrastructure.Storage;
using MusicHub.Infrastructure.Telegram;

namespace MusicHub.Tests.Integration;

/// <summary>
/// The bot's storage screen, exercised through the real router against the fake Bot API.
/// </summary>
/// <remarks>
/// The properties that matter here are authorization and honesty: a stranger never sees the screen,
/// a path that fails a safety check never offers the confirm button, and the move is queued by the
/// same service the HTTP endpoint uses rather than by a second copy of the rules.
/// </remarks>
[Collection("api")]
public class TelegramBotStorageTests(ApiFactory factory)
{
    private const string FakeBotToken = "123456789:AAFakeTokenValueForTestingOnly_1234567890";
    private const long AdminUserId = 900601;
    private const long AdminChatId = 900601;
    private const long StrangerId = 111333;

    [Fact]
    public async Task A_stranger_asking_for_storage_is_refused_and_told_nothing_about_it()
    {
        using var api = new FakeTelegramApi();
        try
        {
            api.PendingUpdates = Command(6101, "/storage", StrangerId, StrangerId);
            await ConfigureAsync(api);

            var result = await PollOnceAsync();

            Assert.Equal(0, result.Imported);
            var reply = api.SingleCall("sendMessage");
            Assert.Equal(TelegramBotTexts.Denial, reply.String("text"));
            Assert.False(reply.Root.TryGetProperty("reply_markup", out _),
                "a refused sender must not receive a keyboard");
        }
        finally
        {
            await ResetAsync();
        }
    }

    [Fact]
    public async Task The_storage_screen_reports_the_real_location_and_the_data_root()
    {
        using var api = new FakeTelegramApi();
        try
        {
            api.PendingUpdates = Command(6111, "/storage", AdminUserId, AdminChatId);
            await ConfigureAsync(api);

            var result = await PollOnceAsync();

            Assert.Equal(0, result.Rejected);
            var text = api.SingleCall("sendMessage").String("text")!;

            // The two roots are both named, and the default install reports the data root as the library.
            Assert.Contains(factory.DataRoot, text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(TelegramBotTexts.Get(TelegramBotTextIds.StorageCurrent), text, StringComparison.Ordinal);
            Assert.Contains(TelegramBotTexts.Get(TelegramBotTextIds.StorageDataRoot), text, StringComparison.Ordinal);
            Assert.Contains(TelegramBotTexts.Get(TelegramBotTextIds.StorageDefaultSource), text, StringComparison.Ordinal);

            // No confirm button: nothing has been checked, so no move can start.
            var keyboard = api.SingleCall("sendMessage").Root.GetProperty("reply_markup").GetProperty("inline_keyboard");
            var datas = keyboard.EnumerateArray()
                .SelectMany(row => row.EnumerateArray())
                .Select(button => button.GetProperty("callback_data").GetString())
                .ToList();
            Assert.DoesNotContain(TelegramKeyboard.StorageStartAction, datas.Select(x => x ?? string.Empty));
            Assert.Contains("v1:storagerefresh", datas);
        }
        finally
        {
            await ResetAsync();
        }
    }

    [Fact]
    public async Task A_dangerous_path_is_refused_and_never_offers_the_confirm_button()
    {
        using var api = new FakeTelegramApi();
        try
        {
            var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            var target = Path.Combine(windows, "MusicHubBotProbe");

            api.PendingUpdates = Command(6121, $"/storage {target}", AdminUserId, AdminChatId);
            await ConfigureAsync(api);

            await PollOnceAsync();

            var message = api.SingleCall("sendMessage");
            var text = message.String("text")!;
            Assert.Contains(TelegramBotTexts.Get(TelegramBotTextIds.StoragePathRejected), text, StringComparison.Ordinal);

            var keys = message.Root.GetProperty("reply_markup").GetProperty("inline_keyboard")
                .EnumerateArray().SelectMany(row => row.EnumerateArray())
                .Select(button => button.GetProperty("callback_data").GetString()).ToList();
            Assert.DoesNotContain("v1:storagestart", keys);

            // A refused folder is never created.
            Assert.False(Directory.Exists(target));

            // Nothing was queued.
            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<MusicHubDbContext>();
            Assert.False(await db.BackgroundJobs
                .AnyAsync(x => x.Type == LibraryRelocationService.JobType && x.Status == JobStatus.Pending));
        }
        finally
        {
            await ResetAsync();
        }
    }

    [Fact]
    public async Task A_usable_path_offers_the_move_and_the_confirm_tap_queues_it()
    {
        using var api = new FakeTelegramApi();
        try
        {
            var target = Path.Combine(Path.GetTempPath(), "musichub-bot-target-" + Guid.NewGuid().ToString("N")[..10]);
            try
            {
                api.PendingUpdates = Command(6131, $"/storage {target}", AdminUserId, AdminChatId);
                await ConfigureAsync(api);
                await PollOnceAsync();

                var offered = api.SingleCall("sendMessage");
                var keys = offered.Root.GetProperty("reply_markup").GetProperty("inline_keyboard")
                    .EnumerateArray().SelectMany(row => row.EnumerateArray())
                    .Select(button => button.GetProperty("callback_data").GetString()).ToList();
                Assert.Contains("v1:storagestart", keys);

                // The validated path is held in the session, not in the callback data.
                await using (var scope = factory.Services.CreateAsyncScope())
                {
                    var db = scope.ServiceProvider.GetRequiredService<MusicHubDbContext>();
                    var row = await db.AppSettings.AsNoTracking()
                        .SingleAsync(x => x.Key == TelegramBotStateStore.KeyFor(AdminChatId));
                    using var json = JsonDocument.Parse(row.Value);
                    Assert.Equal(TelegramBotStates.StorageConfirm, json.RootElement.GetProperty("state").GetString());
                }

                // Tapping confirm queues a real job against the same service the API uses.
                api.PendingUpdates = Callback(6132, "cb-6132", "v1:storagestart", messageId: 1000);
                await PollOnceAsync();

                await using (var scope = factory.Services.CreateAsyncScope())
                {
                    var db = scope.ServiceProvider.GetRequiredService<MusicHubDbContext>();
                    var job = await db.BackgroundJobs.AsNoTracking()
                        .SingleAsync(x => x.Type == LibraryRelocationService.JobType);
                    Assert.Equal(JobStatus.Pending, job.Status);
                    Assert.Equal(1, job.MaxAttempts);
                    using var payload = JsonDocument.Parse(job.PayloadJson);
                    Assert.Equal(Path.GetFullPath(target), payload.RootElement.GetProperty("target").GetString());
                }

                var answer = api.Calls("answerCallbackQuery").Last();
                Assert.Equal(TelegramBotTexts.Get(TelegramBotTextIds.StorageQueued), answer.String("text"));

                // The session no longer holds a confirmable path, so a second tap cannot double-queue.
                await using (var scope = factory.Services.CreateAsyncScope())
                {
                    var db = scope.ServiceProvider.GetRequiredService<MusicHubDbContext>();
                    var row = await db.AppSettings.AsNoTracking()
                        .SingleAsync(x => x.Key == TelegramBotStateStore.KeyFor(AdminChatId));
                    using var json = JsonDocument.Parse(row.Value);
                    Assert.Equal(TelegramBotStates.Storage, json.RootElement.GetProperty("state").GetString());
                }
            }
            finally
            {
                if (Directory.Exists(target)) Directory.Delete(target, true);
            }
        }
        finally
        {
            await ResetAsync();
        }
    }

    [Fact]
    public async Task A_confirmed_path_whose_session_expired_queues_nothing()
    {
        using var api = new FakeTelegramApi();
        try
        {
            api.PendingUpdates = Callback(6141, "cb-6141", "v1:storagestart", messageId: 1000);
            await ConfigureAsync(api);

            await PollOnceAsync();

            var answer = api.SingleCall("answerCallbackQuery");
            Assert.Equal(TelegramBotTexts.Get(TelegramBotTextIds.SessionExpired), answer.String("text"));

            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<MusicHubDbContext>();
            Assert.False(await db.BackgroundJobs.AnyAsync(x => x.Type == LibraryRelocationService.JobType));
        }
        finally
        {
            await ResetAsync();
        }
    }

    [Fact]
    public async Task Every_storage_string_exists_in_both_languages()
    {
        await Task.CompletedTask;
        foreach (var id in new[]
                 {
                     TelegramBotTextIds.StorageTitle,
                     TelegramBotTextIds.StorageTitleShort,
                     TelegramBotTextIds.StorageCurrent,
                     TelegramBotTextIds.StorageSource,
                     TelegramBotTextIds.StorageAssets,
                     TelegramBotTextIds.StorageUsage,
                     TelegramBotTextIds.StorageUsageUnknown,
                     TelegramBotTextIds.StorageHowTo,
                     TelegramBotTextIds.StorageUsageHeading,
                     TelegramBotTextIds.StoragePointerWarning,
                     TelegramBotTextIds.StorageMoveRunning,
                     TelegramBotTextIds.StorageMoveStatus,
                     TelegramBotTextIds.StorageMoveCompleted,
                     TelegramBotTextIds.StorageMoveFailed,
                     TelegramBotTextIds.StoragePathRequired,
                     TelegramBotTextIds.StoragePathRejected,
                     TelegramBotTextIds.StorageQueued,
                     TelegramBotTextIds.StorageDefaultSource,
                     TelegramBotTextIds.StorageRelocatedSource,
                     TelegramBotTextIds.StorageDataRoot,
                     TelegramBotTextIds.StorageKeysNote,
                     TelegramBotTextIds.StorageInvalidPathChars
                 })
        {
            foreach (var language in TelegramBotTexts.Languages)
            {
                Assert.False(string.IsNullOrWhiteSpace(TelegramBotTexts.Get(id, language)),
                    $"{id} is missing in {language}");
            }
        }
    }

    // ---- helpers ----------------------------------------------------------------------------

    private static string Command(long updateId, string text, long senderId, long chatId) => $$"""
        [ { "update_id": {{updateId}}, "message": { "message_id": {{updateId}}, "date": 1700000000,
              "from": { "id": {{senderId}}, "is_bot": false, "first_name": "Admin" },
              "chat": { "id": {{chatId}}, "type": "private" },
              "text": {{JsonSerializer.Serialize(text)}} } } ]
        """;

    private static string Callback(long updateId, string callbackQueryId, string data, long messageId) => $$"""
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

    private async Task ConfigureAsync(FakeTelegramApi api)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MusicHubDbContext>();
        var protector = scope.ServiceProvider.GetRequiredService<TelegramSecretProtector>();

        db.TelegramConfigurations.RemoveRange(await db.TelegramConfigurations.ToListAsync());
        db.TelegramConfigurations.Add(new TelegramConfiguration
        {
            BotTokenProtected = protector.Protect(FakeBotToken),
            AdminTelegramUserId = AdminUserId,
            AdminChatId = AdminChatId,
            IngestionEnabled = true,
            AutoPublishEnabled = false,
            ApiEndpoint = api.ApiBase,
            LocalBotApiEndpoint = null,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
    }

    /// <summary>Removes everything this class wrote so the shared suite stays order-independent.</summary>
    private async Task ResetAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MusicHubDbContext>();

        db.TelegramConfigurations.RemoveRange(await db.TelegramConfigurations.ToListAsync());
        db.TelegramUpdates.RemoveRange(await db.TelegramUpdates
            .Where(x => x.TelegramUpdateId >= 6100 && x.TelegramUpdateId < 6200).ToListAsync());
        db.BackgroundJobs.RemoveRange(await db.BackgroundJobs
            .Where(x => x.Type == LibraryRelocationService.JobType).ToListAsync());
        var settings = await db.AppSettings
            .Where(x => x.Key == TelegramBotStatusService.OffsetSettingKey
                        || x.Key == TelegramBotStatusService.HeartbeatSettingKey
                        || x.Key == LibraryRelocationState.SettingKey
                        || x.Key.StartsWith(TelegramBotStateStore.KeyPrefix))
            .ToListAsync();
        db.AppSettings.RemoveRange(settings);
        await db.SaveChangesAsync();
    }
}
