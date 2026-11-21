using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MusicHub.Domain;
using MusicHub.Infrastructure;
using MusicHub.Infrastructure.Library;
using MusicHub.Infrastructure.Telegram;

namespace MusicHub.Tests.Integration;

/// <summary>
/// The per-file album question and album management from the bot.
/// </summary>
/// <remarks>
/// The headline requirement is that the bot asks "which album should this track be filed under?"
/// <em>separately for each file</em>. The test that pins it is
/// <see cref="Each_imported_file_gets_its_own_album_question"/>: two files in one poll must produce
/// two independent questions, each resolvable to its own track. The chat session holds a single
/// state, so that only works because prompts are keyed by the asking message's id.
/// </remarks>
[Collection("api")]
public class TelegramAlbumTests(ApiFactory factory)
{
    private const string FakeBotToken = "123456789:AAFakeTokenValueForTestingOnly_1234567890";
    private const long AdminUserId = 900601;
    private const long AdminChatId = 900601;

    // ---- the per-file requirement -------------------------------------------------------------

    [Fact]
    public async Task Each_imported_file_gets_its_own_album_question()
    {
        using var api = new FakeTelegramApi();
        try
        {
            // Two distinct fixtures: the importer dedupes by SHA-256, so they must differ.
            var first = ApiFactory.BuildWav(9401);
            var second = ApiFactory.BuildWav(9402);

            // The fake API serves one file body at a time, so the two updates are polled in two
            // batches. That is also how a real operator sends them: one message after another.
            api.FileContent = first;
            api.FilePath = "documents/album-first.wav";
            api.PendingUpdates = Document(7001, 61, "album-first.wav", first.Length);
            await ConfigureAsync(api);
            var firstPoll = await PollOnceAsync();
            Assert.Equal(1, firstPoll.Imported);

            api.FileContent = second;
            api.FilePath = "documents/album-second.wav";
            api.PendingUpdates = Document(7002, 62, "album-second.wav", second.Length);
            var secondPoll = await PollOnceAsync();
            Assert.Equal(1, secondPoll.Imported);

            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<MusicHubDbContext>();

            // Two prompts, both alive, each pointing at a different track.
            var promptRows = await db.AppSettings.AsNoTracking()
                .Where(x => x.Key.StartsWith(TelegramAlbumPromptStore.KeyPrefix + AdminChatId + "."))
                .ToListAsync();
            Assert.Equal(2, promptRows.Count);

            var trackIds = new List<Guid>();
            foreach (var row in promptRows)
            {
                using var json = JsonDocument.Parse(row.Value);
                trackIds.Add(json.RootElement.GetProperty("trackId").GetGuid());
                Assert.False(json.RootElement.GetProperty("awaitingName").GetBoolean());
                Assert.NotEqual(Guid.Empty, json.RootElement.GetProperty("trackId").GetGuid());
            }

            // Different tracks, which is the whole point: answering one must not retarget the other.
            Assert.Equal(2, trackIds.Distinct().Count());
            Assert.All(trackIds, id => Assert.True(db.Tracks.Any(x => x.Id == id)));

            // Each question was its own message, and each carried the album keyboard.
            var promptTexts = TelegramBotTexts.Get(TelegramBotTextIds.AlbumPrompt);
            var prompts = api.Calls("sendMessage")
                .Where(x => (x.String("text") ?? string.Empty).StartsWith(
                    promptTexts[..promptTexts.IndexOf('{')], StringComparison.Ordinal))
                .ToList();
            Assert.Equal(2, prompts.Count);
            Assert.All(prompts, prompt =>
            {
                var rows = prompt.Root.GetProperty("reply_markup").GetProperty("inline_keyboard");
                Assert.True(rows.GetArrayLength() >= 2);
            });

            // The two prompts name different files, so the operator can tell them apart.
            Assert.Contains(prompts, p => (p.String("text") ?? string.Empty).Contains("album-first.wav", StringComparison.Ordinal));
            Assert.Contains(prompts, p => (p.String("text") ?? string.Empty).Contains("album-second.wav", StringComparison.Ordinal));
        }
        finally
        {
            await ResetAsync();
        }
    }

    [Fact]
    public async Task Picking_an_album_files_that_specific_track()
    {
        using var api = new FakeTelegramApi();
        try
        {
            var wav = ApiFactory.BuildWav(9411);
            api.FileContent = wav;
            api.FilePath = "documents/pick.wav";
            api.PendingUpdates = Document(7101, 71, "pick.wav", wav.Length);
            await ConfigureAsync(api);
            await PollOnceAsync();

            var promptMessageId = await SinglePromptMessageIdAsync();
            var trackId = await SinglePromptTrackIdAsync();

            // An album created through the same service the HTTP endpoint uses.
            Guid albumId;
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var albums = scope.ServiceProvider.GetRequiredService<AlbumService>();
                var created = await albums.CreateAsync("Pick Test Album", "Tester", null, null, null, null, CancellationToken.None);
                Assert.True(created.Succeeded);
                albumId = created.AlbumId!.Value;
            }

            api.PendingUpdates = Callback(7102, "cb-7102", $"v1:albumpick:{albumId:N}", promptMessageId);
            var result = await PollOnceAsync();

            Assert.Equal(0, result.Rejected);
            var answer = api.SingleCall("answerCallbackQuery");
            Assert.NotNull(answer.String("text"));

            // The track is now in that album and its denormalized AlbumDisplay follows.
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<MusicHubDbContext>();
                Assert.True(await db.AlbumTracks.AnyAsync(x => x.AlbumId == albumId && x.TrackId == trackId));
                var track = await db.Tracks.AsNoTracking().SingleAsync(x => x.Id == trackId);
                Assert.Equal("Pick Test Album", track.AlbumDisplay);

                // A bot-initiated change is auditable: the Telegram sender is recorded even though
                // there is no user-account principal behind the callback.
                var audit = await db.AuditLogs.AsNoTracking()
                    .SingleAsync(x => x.Action == "album.track.assign" && x.EntityId == albumId.ToString("N"));
                Assert.Null(audit.ActorUserId);
                Assert.Contains(AdminUserId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    audit.DetailsJson!, StringComparison.Ordinal);
                Assert.Contains("\"actor\":\"telegram\"", audit.DetailsJson!, StringComparison.Ordinal);
            }

            // The prompt is answered and removed: the question cannot be asked twice.
            Assert.False(await PromptExistsAsync(promptMessageId));

            // The message is edited into a confirmation, not re-sent.
            var edit = api.SingleCall("editMessageText");
            Assert.Contains("Pick Test Album", edit.String("text")!, StringComparison.Ordinal);
        }
        finally
        {
            await ResetAsync();
        }
    }

    [Fact]
    public async Task Creating_an_album_from_the_bot_creates_and_files_it()
    {
        using var api = new FakeTelegramApi();
        try
        {
            var wav = ApiFactory.BuildWav(9421);
            api.FileContent = wav;
            api.FilePath = "documents/newname.wav";
            api.PendingUpdates = Document(7201, 81, "newname.wav", wav.Length);
            await ConfigureAsync(api);
            await PollOnceAsync();

            var promptMessageId = await SinglePromptMessageIdAsync();
            var trackId = await SinglePromptTrackIdAsync();

            // Tap "Create a new album": the same message flips into waiting-for-a-name.
            api.PendingUpdates = Callback(7202, "cb-7202", "v1:albumnew", promptMessageId);
            await PollOnceAsync();
            Assert.True(await PromptAwaitingNameAsync(promptMessageId));

            // Then the operator types the name as an ordinary text message.
            api.PendingUpdates = Text(7203, 83, "Bot Made Album");
            await PollOnceAsync();

            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<MusicHubDbContext>();

            var album = await db.Albums.AsNoTracking().SingleOrDefaultAsync(x => x.Title == "Bot Made Album" && !x.IsDeleted);
            Assert.NotNull(album);
            Assert.Equal("bot made album", album!.NormalizedTitle);
            Assert.True(await db.AlbumTracks.AnyAsync(x => x.AlbumId == album.Id && x.TrackId == trackId));

            var track = await db.Tracks.AsNoTracking().SingleAsync(x => x.Id == trackId);
            Assert.Equal("Bot Made Album", track.AlbumDisplay);

            // Both writes are audited: the album creation and the filing, attributed to the admin.
            var created = await db.AuditLogs.AsNoTracking()
                .SingleAsync(x => x.Action == "album.create" && x.EntityId == album.Id.ToString("N"));
            Assert.Contains("\"actor\":\"telegram\"", created.DetailsJson!, StringComparison.Ordinal);
            Assert.True(await db.AuditLogs.AnyAsync(x => x.Action == "album.track.assign"
                                                         && x.EntityId == album.Id.ToString("N")));

            // The prompt is gone and the operator was told the album was created.
            Assert.False(await PromptExistsAsync(promptMessageId));
            Assert.Contains(api.Calls("sendMessage"),
                r => (r.String("text") ?? string.Empty).Contains("Bot Made Album", StringComparison.Ordinal));
        }
        finally
        {
            await ResetAsync();
        }
    }

    [Fact]
    public async Task Typing_a_name_that_already_exists_reuses_that_album()
    {
        using var api = new FakeTelegramApi();
        try
        {
            var wav = ApiFactory.BuildWav(9431);
            api.FileContent = wav;
            api.FilePath = "documents/reuse.wav";
            api.PendingUpdates = Document(7301, 91, "reuse.wav", wav.Length);
            await ConfigureAsync(api);
            await PollOnceAsync();

            var promptMessageId = await SinglePromptMessageIdAsync();
            var trackId = await SinglePromptTrackIdAsync();

            Guid existingId;
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var albums = scope.ServiceProvider.GetRequiredService<AlbumService>();
                existingId = (await albums.CreateAsync("Reused Album", null, null, null, null, null, CancellationToken.None)).AlbumId!.Value;
            }

            api.PendingUpdates = Callback(7302, "cb-7302", "v1:albumnew", promptMessageId);
            await PollOnceAsync();
            // Different casing: the lookup is case-insensitive so a near-duplicate is not created.
            api.PendingUpdates = Text(7303, 93, "reused album");
            await PollOnceAsync();

            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<MusicHubDbContext>();
                Assert.Equal(1, await db.Albums.CountAsync(x => x.NormalizedTitle == "reused album" && !x.IsDeleted));
                Assert.True(await db.AlbumTracks.AnyAsync(x => x.AlbumId == existingId && x.TrackId == trackId));
            }
        }
        finally
        {
            await ResetAsync();
        }
    }

    [Fact]
    public async Task Skipping_leaves_the_track_without_an_album()
    {
        using var api = new FakeTelegramApi();
        try
        {
            var wav = ApiFactory.BuildWav(9441);
            api.FileContent = wav;
            api.FilePath = "documents/skip.wav";
            api.PendingUpdates = Document(7401, 101, "skip.wav", wav.Length);
            await ConfigureAsync(api);
            await PollOnceAsync();

            var promptMessageId = await SinglePromptMessageIdAsync();
            var trackId = await SinglePromptTrackIdAsync();

            api.PendingUpdates = Callback(7402, "cb-7402", "v1:albumskip", promptMessageId);
            await PollOnceAsync();

            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<MusicHubDbContext>();
            Assert.False(await db.AlbumTracks.AnyAsync(x => x.TrackId == trackId));
            Assert.False(await PromptExistsAsync(promptMessageId));
        }
        finally
        {
            await ResetAsync();
        }
    }

    [Fact]
    public async Task An_answered_prompt_cannot_be_used_again()
    {
        using var api = new FakeTelegramApi();
        try
        {
            var wav = ApiFactory.BuildWav(9451);
            api.FileContent = wav;
            api.FilePath = "documents/replay.wav";
            api.PendingUpdates = Document(7501, 111, "replay.wav", wav.Length);
            await ConfigureAsync(api);
            await PollOnceAsync();

            var promptMessageId = await SinglePromptMessageIdAsync();
            Guid albumId;
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var albums = scope.ServiceProvider.GetRequiredService<AlbumService>();
                albumId = (await albums.CreateAsync("Replay Album", null, null, null, null, null, CancellationToken.None)).AlbumId!.Value;
            }

            api.PendingUpdates = Callback(7502, "cb-7502", $"v1:albumpick:{albumId:N}", promptMessageId);
            await PollOnceAsync();

            // Replaying the same callback: the prompt row is gone, so the answer is a refusal and
            // nothing is assigned twice.
            api.PendingUpdates = Callback(7503, "cb-7503", $"v1:albumpick:{albumId:N}", promptMessageId);
            var replay = await PollOnceAsync();

            Assert.Equal(TelegramBotTexts.Get(TelegramBotTextIds.SessionExpired),
                api.Calls("answerCallbackQuery").Last().String("text"));
        }
        finally
        {
            await ResetAsync();
        }
    }

    [Fact]
    public async Task Choosing_an_existing_album_opens_a_real_picker_with_the_albums()
    {
        using var api = new FakeTelegramApi();
        try
        {
            var wav = ApiFactory.BuildWav(9471);
            api.FileContent = wav;
            api.FilePath = "documents/picker.wav";
            api.PendingUpdates = Document(7801, 141, "picker.wav", wav.Length);
            await ConfigureAsync(api);
            await PollOnceAsync();

            var promptMessageId = await SinglePromptMessageIdAsync();

            Guid albumId;
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var albums = scope.ServiceProvider.GetRequiredService<AlbumService>();
                albumId = (await albums.CreateAsync("Picker Listed Album", null, null, null, null, null, CancellationToken.None)).AlbumId!.Value;
            }

            api.PendingUpdates = Callback(7802, "cb-7802", "v1:albumpg:0", promptMessageId);
            var result = await PollOnceAsync();

            Assert.Equal(0, result.Rejected);

            // The picker edits the same message rather than sending a new one, names the track it is
            // asking about, and lists the real album with a button that carries that album's id.
            var edit = api.SingleCall("editMessageText");
            Assert.Equal(promptMessageId, edit.Number("message_id"));

            var keyboard = edit.Root.GetProperty("reply_markup").GetProperty("inline_keyboard");
            var buttons = new List<string>();
            var labels = new List<string>();
            foreach (var row in keyboard.EnumerateArray())
            {
                foreach (var button in row.EnumerateArray())
                {
                    buttons.Add(button.GetProperty("callback_data").GetString()!);
                    labels.Add(button.GetProperty("text").GetString()!);
                }
            }
            Assert.Contains($"v1:albumpick:{albumId:N}", buttons);
            Assert.Contains("Picker Listed Album", labels);

            // Every button on the picker leads somewhere: no dead control.
            Assert.All(buttons, data => Assert.True(
                data.StartsWith("v1:albumpick:", StringComparison.Ordinal)
                || data.StartsWith("v1:albumpg:", StringComparison.Ordinal)
                || data is "v1:albumnew" or "v1:albumskip",
                $"unexpected picker callback {data}"));

            // The toast is a sentence, not an unfilled template.
            var answer = api.SingleCall("answerCallbackQuery").String("text")!;
            Assert.DoesNotContain("{0}", answer, StringComparison.Ordinal);

            // Opening the picker does not answer the question: the prompt is still pending.
            Assert.True(await PromptExistsAsync(promptMessageId));
        }
        finally
        {
            await ResetAsync();
        }
    }

    [Fact]
    public async Task The_picker_second_page_shows_different_albums_than_the_first()
    {
        using var api = new FakeTelegramApi();
        try
        {
            var wav = ApiFactory.BuildWav(9481);
            api.FileContent = wav;
            api.FilePath = "documents/pages.wav";
            api.PendingUpdates = Document(7901, 151, "pages.wav", wav.Length);
            await ConfigureAsync(api);
            await PollOnceAsync();

            var promptMessageId = await SinglePromptMessageIdAsync();

            // Nine albums is one more than a page, so a second page must exist and must differ.
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var albums = scope.ServiceProvider.GetRequiredService<AlbumService>();
                for (var i = 0; i < TelegramKeyboard.AlbumsPerPage + 1; i++)
                {
                    var created = await albums.CreateAsync(
                        $"Paged Album {i:00}", null, null, null, null, null, CancellationToken.None);
                    Assert.True(created.Succeeded);
                }
            }

            api.PendingUpdates = Callback(7902, "cb-7902", "v1:albumpg:0", promptMessageId);
            await PollOnceAsync();
            var firstPage = PickerAlbumIds(api.Calls("editMessageText").Last());

            api.PendingUpdates = Callback(7903, "cb-7903", "v1:albumpg:1", promptMessageId);
            await PollOnceAsync();
            var secondPage = PickerAlbumIds(api.Calls("editMessageText").Last());

            Assert.Equal(TelegramKeyboard.AlbumsPerPage, firstPage.Count);
            Assert.NotEmpty(secondPage);

            // The bug this pins: without a Skip the second page repeated the first, so the "next"
            // button was a dead control that looked like it worked.
            Assert.Empty(firstPage.Intersect(secondPage));

            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<MusicHubDbContext>();
                var all = await db.Albums.AsNoTracking()
                    .Where(x => x.Title.StartsWith("Paged Album") && !x.IsDeleted)
                    .CountAsync();
                Assert.Equal(TelegramKeyboard.AlbumsPerPage + 1, all);
            }
        }
        finally
        {
            await ResetAsync();
        }
    }

    /// <summary>The album ids a picker message offers, in button order.</summary>
    private static List<Guid> PickerAlbumIds(RecordedRequest edit)
    {
        var ids = new List<Guid>();
        foreach (var row in edit.Root.GetProperty("reply_markup").GetProperty("inline_keyboard").EnumerateArray())
        {
            foreach (var button in row.EnumerateArray())
            {
                var data = button.GetProperty("callback_data").GetString()!;
                if (data.StartsWith("v1:albumpick:", StringComparison.Ordinal))
                {
                    ids.Add(Guid.ParseExact(data["v1:albumpick:".Length..], "N"));
                }
            }
        }
        return ids;
    }

    [Fact]
    public async Task The_picker_with_no_albums_says_so_and_still_offers_creation()
    {
        using var api = new FakeTelegramApi();
        try
        {
            var wav = ApiFactory.BuildWav(9491);
            api.FileContent = wav;
            api.FilePath = "documents/empty.wav";
            api.PendingUpdates = Document(7951, 161, "empty.wav", wav.Length);
            await ConfigureAsync(api);
            await PollOnceAsync();

            var promptMessageId = await SinglePromptMessageIdAsync();

            // No albums exist, so the picker must explain that rather than showing a bare header.
            api.PendingUpdates = Callback(7952, "cb-7952", "v1:albumpg:0", promptMessageId);
            var result = await PollOnceAsync();
            Assert.Equal(0, result.Rejected);

            var edit = api.SingleCall("editMessageText");
            var text = edit.String("text")!;
            Assert.Contains(TelegramBotTexts.Get(TelegramBotTextIds.AlbumNoneYet), text, StringComparison.Ordinal);

            // The way out is still present: create an album, or skip.
            var buttons = new List<string>();
            foreach (var row in edit.Root.GetProperty("reply_markup").GetProperty("inline_keyboard").EnumerateArray())
            {
                foreach (var button in row.EnumerateArray())
                {
                    buttons.Add(button.GetProperty("callback_data").GetString()!);
                }
            }
            Assert.Contains("v1:albumnew", buttons);
            Assert.Contains("v1:albumskip", buttons);
            Assert.Empty(PickerAlbumIds(edit));
        }
        finally
        {
            await ResetAsync();
        }
    }

    // ---- the frozen ingestion path is untouched ------------------------------------------------

    [Fact]
    public async Task Ordinary_text_still_gets_the_frozen_rejection_when_no_album_is_pending()
    {
        using var api = new FakeTelegramApi();
        try
        {
            api.PendingUpdates = Text(7601, 121, "just some text");
            await ConfigureAsync(api);

            var result = await PollOnceAsync();

            Assert.Equal(1, result.Rejected);
            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<MusicHubDbContext>();
            var import = await db.TelegramImports.AsNoTracking().SingleAsync(x => x.TelegramUpdateId == 7601);
            Assert.Equal("Message did not contain a supported audio document.", import.Error);

            // No album was created by an unrelated text message.
            Assert.False(await db.Albums.AnyAsync(x => x.Title == "just some text"));
        }
        finally
        {
            await ResetAsync();
        }
    }

    [Fact]
    public async Task A_command_is_never_treated_as_an_album_name()
    {
        using var api = new FakeTelegramApi();
        try
        {
            var wav = ApiFactory.BuildWav(9461);
            api.FileContent = wav;
            api.FilePath = "documents/cmd.wav";
            api.PendingUpdates = Document(7701, 131, "cmd.wav", wav.Length);
            await ConfigureAsync(api);
            await PollOnceAsync();

            var promptMessageId = await SinglePromptMessageIdAsync();
            api.PendingUpdates = Callback(7702, "cb-7702", "v1:albumnew", promptMessageId);
            await PollOnceAsync();

            // A command arrives while a name is awaited; it must be routed as a command.
            api.PendingUpdates = Text(7703, 133, "/status");
            await PollOnceAsync();

            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<MusicHubDbContext>();
            Assert.False(await db.Albums.AnyAsync(x => x.Title == "/status" || x.Title == "status"));
        }
        finally
        {
            await ResetAsync();
        }
    }

    // ---- the shared service ------------------------------------------------------------------

    [Fact]
    public async Task The_shared_album_service_rejects_a_blank_title_and_a_missing_track()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var albums = scope.ServiceProvider.GetRequiredService<AlbumService>();

        var blank = await albums.CreateAsync("   ", null, null, null, null, null, CancellationToken.None);
        Assert.False(blank.Succeeded);
        Assert.Equal("Album title is required.", blank.Error);

        var created = await albums.CreateAsync("Service Test", null, null, null, null, null, CancellationToken.None);
        Assert.True(created.Succeeded);

        var missing = await albums.AssignTrackAsync(created.AlbumId!.Value, Guid.NewGuid(), null, CancellationToken.None);
        Assert.False(missing.Succeeded);

        var unknownAlbum = await albums.AssignTrackAsync(Guid.NewGuid(), Guid.NewGuid(), null, CancellationToken.None);
        Assert.False(unknownAlbum.Succeeded);
    }

    // ---- helpers -----------------------------------------------------------------------------

    private static string Document(long updateId, long messageId, string fileName, int size) => $$"""
        [ { "update_id": {{updateId}}, "message": { "message_id": {{messageId}}, "date": 1700000000,
              "from": { "id": {{AdminUserId}}, "is_bot": false, "first_name": "Admin" },
              "chat": { "id": {{AdminChatId}}, "type": "private" },
              "document": { "file_id": "album-doc-{{messageId}}", "file_name": {{JsonSerializer.Serialize(fileName)}},
                            "mime_type": "audio/wav", "file_size": {{size}} } } } ]
        """;

    private static string Text(long updateId, long messageId, string text) => $$"""
        [ { "update_id": {{updateId}}, "message": { "message_id": {{messageId}}, "date": 1700000000,
              "from": { "id": {{AdminUserId}}, "is_bot": false, "first_name": "Admin" },
              "chat": { "id": {{AdminChatId}}, "type": "private" },
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

    private async Task<long> SinglePromptMessageIdAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MusicHubDbContext>();
        var prefix = TelegramAlbumPromptStore.KeyPrefix + AdminChatId + ".";
        var keys = await db.AppSettings.AsNoTracking()
            .Where(x => x.Key.StartsWith(prefix))
            .Select(x => x.Key)
            .ToListAsync();
        Assert.Single(keys);
        return long.Parse(keys[0][prefix.Length..]);
    }

    private async Task<Guid> SinglePromptTrackIdAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MusicHubDbContext>();
        var prefix = TelegramAlbumPromptStore.KeyPrefix + AdminChatId + ".";
        var value = await db.AppSettings.AsNoTracking()
            .Where(x => x.Key.StartsWith(prefix))
            .Select(x => x.Value)
            .SingleAsync();
        using var json = JsonDocument.Parse(value);
        return json.RootElement.GetProperty("trackId").GetGuid();
    }

    private async Task<bool> PromptExistsAsync(long messageId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MusicHubDbContext>();
        return await db.AppSettings.AsNoTracking()
            .AnyAsync(x => x.Key == TelegramAlbumPromptStore.KeyFor(AdminChatId, messageId));
    }

    private async Task<bool> PromptAwaitingNameAsync(long messageId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MusicHubDbContext>();
        var value = await db.AppSettings.AsNoTracking()
            .Where(x => x.Key == TelegramAlbumPromptStore.KeyFor(AdminChatId, messageId))
            .Select(x => x.Value)
            .SingleAsync();
        using var json = JsonDocument.Parse(value);
        return json.RootElement.GetProperty("awaitingName").GetBoolean();
    }

    private async Task ConfigureAsync(FakeTelegramApi api)
    {
        // Ingestion attributes every import to an active administrator. Bootstrapping one here keeps
        // these tests independent of whether another test in the shared collection already did it:
        // without this, ResolveImportingUserIdAsync returns null and every document is rejected with
        // "No active administrator account for attribution." even though nothing is wrong.
        using (var bootstrap = await factory.CreateAdminClientAsync())
        {
            Assert.NotNull(bootstrap);
        }

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MusicHubDbContext>();
        var protector = scope.ServiceProvider.GetRequiredService<TelegramSecretProtector>();

        // The poll offset lives in AppSettings and outlives a class's own reset if another class
        // ran first. A stale high value would make the fake API withhold this test's updates, so it
        // is zeroed here: every update id in this class is then >= the offset by construction.
        var offset = await db.AppSettings.SingleOrDefaultAsync(x => x.Key == TelegramBotStatusService.OffsetSettingKey);
        if (offset is not null) db.AppSettings.Remove(offset);

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

    /// <summary>Removes this class's Telegram state and albums so the shared suite stays order-independent.</summary>
    private async Task ResetAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MusicHubDbContext>();

        db.TelegramConfigurations.RemoveRange(await db.TelegramConfigurations.ToListAsync());
        db.TelegramImports.RemoveRange(await db.TelegramImports
            .Where(x => x.TelegramUpdateId >= 7000 && x.TelegramUpdateId < 8000).ToListAsync());
        db.TelegramUpdates.RemoveRange(await db.TelegramUpdates
            .Where(x => x.TelegramUpdateId >= 7000 && x.TelegramUpdateId < 8000).ToListAsync());

        // Albums created by these tests are removed with their memberships first.
        var albumIds = await db.Albums
            .Where(x => x.Title == "Pick Test Album"
                        || x.Title == "Bot Made Album"
                        || x.NormalizedTitle == "reused album"
                        || x.Title == "Replay Album"
                        || x.Title == "Picker Listed Album"
                        || x.Title.StartsWith("Paged Album")
                        || x.Title == "Service Test")
            .Select(x => x.Id)
            .ToListAsync();
        if (albumIds.Count > 0)
        {
            db.AlbumTracks.RemoveRange(await db.AlbumTracks.Where(x => albumIds.Contains(x.AlbumId)).ToListAsync());
            db.Albums.RemoveRange(await db.Albums.Where(x => albumIds.Contains(x.Id)).ToListAsync());

            // Audit rows for the bot-created albums go away with them.
            var albumIdTexts = albumIds.Select(x => x.ToString("N")).ToList();
            db.AuditLogs.RemoveRange(await db.AuditLogs
                .Where(x => (x.EntityType == "Album" || x.EntityType == "AlbumTrack")
                            && x.EntityId != null
                            && albumIdTexts.Contains(x.EntityId))
                .ToListAsync());
        }

        var settings = await db.AppSettings
            .Where(x => x.Key == TelegramBotStatusService.OffsetSettingKey
                        || x.Key == TelegramBotStatusService.HeartbeatSettingKey
                        || x.Key.StartsWith(TelegramBotStateStore.KeyPrefix)
                        || x.Key.StartsWith(TelegramAlbumPromptStore.KeyPrefix))
            .ToListAsync();
        db.AppSettings.RemoveRange(settings);
        await db.SaveChangesAsync();
    }
}
