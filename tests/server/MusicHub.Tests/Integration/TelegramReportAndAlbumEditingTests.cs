using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MusicHub.Domain;
using MusicHub.Infrastructure;
using MusicHub.Infrastructure.Library;
using MusicHub.Infrastructure.Telegram;

namespace MusicHub.Tests.Integration;

/// <summary>
/// The bot's library report, album editing (add and remove tracks) and artist report.
/// </summary>
/// <remarks>
/// Every assertion here reads back the database or the exact Bot API call the operator would see.
/// Album screens carry no track ids in their buttons - the 64 byte callback budget cannot hold an
/// album id and a track id - so the row-to-track mapping lives in <c>TelegramAlbumEditStore</c>.
/// The tests that pin that design are <see cref="Removing_a_track_needs_a_confirmation_and_keeps_the_audio"/>
/// and <see cref="A_stale_remove_button_rebuilds_the_screen_and_removes_nothing"/>.
/// </remarks>
[Collection("api")]
public class TelegramReportAndAlbumEditingTests(ApiFactory factory)
{
    private const string FakeBotToken = "123456789:AAFakeTokenValueForTestingOnly_1234567890";
    private const long AdminUserId = 900701;
    private const long AdminChatId = 900701;
    private const long PromptMessageId = 5001;

    // ---- reports ------------------------------------------------------------------------------

    [Fact]
    public async Task The_overview_reports_the_real_counts_from_the_database()
    {
        using var api = new FakeTelegramApi();
        try
        {
            await ConfigureAsync(api);

            // Read the expected numbers immediately before the render: the report must describe the
            // database as it is, not a fixture.
            int albumCount, trackCount, artistCount, userCount, activeUsers;
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<MusicHubDbContext>();
                albumCount = await db.Albums.CountAsync(x => !x.IsDeleted);
                trackCount = await db.Tracks.CountAsync(x => !x.IsDeleted);
                artistCount = await db.TrackArtists.Where(x => !x.Track.IsDeleted)
                    .Select(x => x.ArtistId).Distinct().CountAsync();
                userCount = await db.Users.CountAsync();
                activeUsers = await db.Users.CountAsync(x => x.IsActive);
            }

            api.PendingUpdates = Text(9101, 401, "/reports");
            var result = await PollOnceAsync();
            Assert.Equal(0, result.Rejected);

            var message = LastCall(api, "sendMessage");
            var text = message.String("text")!;
            Assert.Contains(Label(TelegramBotTextIds.ReportsAlbums, albumCount), text, StringComparison.Ordinal);
            Assert.Contains(Label(TelegramBotTextIds.ReportsTracks, trackCount), text, StringComparison.Ordinal);
            Assert.Contains(Label(TelegramBotTextIds.ReportsArtists, artistCount), text, StringComparison.Ordinal);
            Assert.Contains(Label(TelegramBotTextIds.ReportsUsers, userCount), text, StringComparison.Ordinal);
            Assert.Contains(Label(TelegramBotTextIds.ReportsUsersActive, activeUsers), text, StringComparison.Ordinal);
            Assert.Contains(Label(TelegramBotTextIds.ReportsUsersInactive, userCount - activeUsers), text, StringComparison.Ordinal);

            // Both detailed reports are one tap away, and the refresh and home buttons are real.
            var callbacks = ButtonCallbacks(message);
            Assert.Contains("v1:amalbs:0", callbacks);
            Assert.Contains("v1:artists:0", callbacks);
            Assert.Contains("v1:reports", callbacks);
            Assert.Contains("v1:home", callbacks);
        }
        finally
        {
            await ResetAsync();
        }
    }

    [Fact]
    public async Task The_artist_report_lists_artists_and_opens_a_real_report()
    {
        using var api = new FakeTelegramApi();
        try
        {
            await ConfigureAsync(api);

            var artistName = "ReportTest Artist " + Guid.NewGuid().ToString("N")[..6];
            var first = await SeedTrackAsync("ReportTest Song A", artistName, 100);
            var second = await SeedTrackAsync("ReportTest Song B", artistName, 50);

            Guid albumId;
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var albums = scope.ServiceProvider.GetRequiredService<AlbumService>();
                var created = await albums.CreateAsync(
                    "ReportTest Artist Album", artistName, null, 2024, null, null, CancellationToken.None);
                Assert.True(created.Succeeded);
                albumId = created.AlbumId!.Value;
                Assert.True((await albums.AssignTrackAsync(albumId, first, null, CancellationToken.None)).Succeeded);
                Assert.True((await albums.AssignTrackAsync(albumId, second, null, CancellationToken.None)).Succeeded);
            }

            api.PendingUpdates = Text(9111, 411, "/artists");
            await PollOnceAsync();

            var list = LastCall(api, "sendMessage");
            var entry = SingleButton(list, data => data.StartsWith("v1:artopen:", StringComparison.Ordinal)
                                                     && ButtonText(list, data).Contains(artistName, StringComparison.Ordinal));
            Assert.Contains(artistName, ButtonText(list, entry), StringComparison.Ordinal);

            var artistId = Guid.ParseExact(entry["v1:artopen:".Length..], "N");
            Assert.NotEqual(Guid.Empty, artistId);
            api.PendingUpdates = Callback(9112, "cb-9112", entry, PromptMessageId);
            await PollOnceAsync();

            var detail = LastCall(api, "editMessageText");
            var text = detail.String("text")!;
            Assert.Contains(artistName, text, StringComparison.Ordinal);

            // Two live tracks and one album, both read from the database.
            Assert.Contains(
                $"{TelegramBotTexts.Get(TelegramBotTextIds.ArtistDetailTracks)}: 2",
                text, StringComparison.Ordinal);
            Assert.Contains(
                $"{TelegramBotTexts.Get(TelegramBotTextIds.ArtistDetailAlbums)}: 1",
                text, StringComparison.Ordinal);
            Assert.Contains("ReportTest Artist Album", text, StringComparison.Ordinal);

            // The duration is the sum of the two seeded tracks (2:30), not a hardcoded value.
            Assert.Contains(TelegramBotTexts.FormatDuration(150), text, StringComparison.Ordinal);

            // The detail screen offers the album and a way back to the artist list.
            var callbacks = ButtonCallbacks(detail);
            Assert.Contains("v1:amopen:" + albumId.ToString("N"), callbacks);
            Assert.Contains("v1:artists:0", callbacks);
        }
        finally
        {
            await ResetAsync();
        }
    }

    // ---- album management ---------------------------------------------------------------------

    [Fact]
    public async Task The_album_management_list_opens_a_real_album_screen()
    {
        using var api = new FakeTelegramApi();
        try
        {
            await ConfigureAsync(api);

            var (albumId, trackId) = await SeedAlbumAsync("ReportTest Manage Album", "ReportTest Manage Artist");

            api.PendingUpdates = Text(9121, 421, "/albums");
            await PollOnceAsync();

            var list = LastCall(api, "sendMessage");
            var open = SingleButton(list, data => data == "v1:amopen:" + albumId.ToString("N"));

            api.PendingUpdates = Callback(9122, "cb-9122", open, PromptMessageId);
            await PollOnceAsync();

            var detail = LastCall(api, "editMessageText");
            var text = detail.String("text")!;
            Assert.Contains("ReportTest Manage Album", text, StringComparison.Ordinal);
            Assert.Contains(
                $"{TelegramBotTexts.Get(TelegramBotTextIds.AlbumDetailTracks)}: 1",
                text, StringComparison.Ordinal);

            // The track is a real remove button and the "add track" control exists.
            var callbacks = ButtonCallbacks(detail);
            Assert.Contains($"v1:amrm:{albumId:N}:0", callbacks);
            Assert.Contains($"v1:amaddlist:{albumId:N}:0", callbacks);
            Assert.Contains("v1:amalbs:0", callbacks);
            Assert.NotEqual(Guid.Empty, trackId);
        }
        finally
        {
            await ResetAsync();
        }
    }

    [Fact]
    public async Task The_track_page_two_shows_different_tracks_than_page_one()
    {
        using var api = new FakeTelegramApi();
        try
        {
            await ConfigureAsync(api);

            Guid albumId;
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var albums = scope.ServiceProvider.GetRequiredService<AlbumService>();
                var created = await albums.CreateAsync(
                    "ReportTest Paged Album", "ReportTest Paged Artist", null, null, null, null, CancellationToken.None);
                Assert.True(created.Succeeded);
                albumId = created.AlbumId!.Value;

                for (var i = 1; i <= 9; i++)
                {
                    var trackId = await SeedTrackAsync($"ReportTest Paged Song {i}", "ReportTest Paged Artist", 60);
                    Assert.True((await albums.AssignTrackAsync(albumId, trackId, null, CancellationToken.None)).Succeeded);
                }
            }

            api.PendingUpdates = Callback(9131, "cb-9131", $"v1:amopen:{albumId:N}", PromptMessageId);
            await PollOnceAsync();
            var firstPage = LastCall(api, "editMessageText");
            var firstLabels = ButtonTextsStartingWith("❌ ", firstPage);
            Assert.Equal(TelegramKeyboard.ListPageSize, firstLabels.Count);

            // "Next" carries the album id and the page, so page 2 is a different list.
            var next = SingleButton(firstPage, data => data == $"v1:ampgt:{albumId:N}:1");
            api.PendingUpdates = Callback(9132, "cb-9132", next, PromptMessageId);
            await PollOnceAsync();

            var secondPage = LastCall(api, "editMessageText");
            var secondLabels = ButtonTextsStartingWith("❌ ", secondPage);
            Assert.Single(secondLabels);
            Assert.Empty(firstLabels.Intersect(secondLabels));
        }
        finally
        {
            await ResetAsync();
        }
    }

    [Fact]
    public async Task Removing_a_track_needs_a_confirmation_and_keeps_the_audio()
    {
        using var api = new FakeTelegramApi();
        try
        {
            await ConfigureAsync(api);

            var (albumId, trackId) = await SeedAlbumAsync("ReportTest Remove Album", "ReportTest Remove Artist");

            // Open the album.
            api.PendingUpdates = Callback(9141, "cb-9141", $"v1:amopen:{albumId:N}", PromptMessageId);
            await PollOnceAsync();
            Assert.Contains($"v1:amrm:{albumId:N}:0", ButtonCallbacks(LastCall(api, "editMessageText")));

            // First tap is only a question: nothing may be written yet.
            api.PendingUpdates = Callback(9142, "cb-9142", $"v1:amrm:{albumId:N}:0", PromptMessageId);
            await PollOnceAsync();

            var confirmation = LastCall(api, "editMessageText");
            var confirmText = confirmation.String("text")!;
            Assert.Contains("ReportTest Remove Song", confirmText, StringComparison.Ordinal);
            Assert.Contains("ReportTest Remove Album", confirmText, StringComparison.Ordinal);
            Assert.Contains($"v1:amrmdo:{albumId:N}", ButtonCallbacks(confirmation));

            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<MusicHubDbContext>();
                Assert.True(await db.AlbumTracks.AnyAsync(x => x.AlbumId == albumId && x.TrackId == trackId));
                var track = await db.Tracks.AsNoTracking().SingleAsync(x => x.Id == trackId);
                Assert.Equal("ReportTest Remove Album", track.AlbumDisplay);
            }

            // The confirmation is what actually removes the membership.
            api.PendingUpdates = Callback(9143, "cb-9143", $"v1:amrmdo:{albumId:N}", PromptMessageId);
            await PollOnceAsync();

            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<MusicHubDbContext>();
                Assert.False(await db.AlbumTracks.AnyAsync(x => x.AlbumId == albumId && x.TrackId == trackId));

                // Only the membership went: the track and its audio are still in the library, and
                // the display no longer claims an album it is not in.
                var track = await db.Tracks.AsNoTracking().SingleAsync(x => x.Id == trackId);
                Assert.False(track.IsDeleted);
                Assert.Equal(string.Empty, track.AlbumDisplay);
                Assert.True(await db.AudioAssets.AnyAsync(x => x.Id == track.AudioAssetId));
            }

            var after = LastCall(api, "editMessageText");
            Assert.Contains(
                $"{TelegramBotTexts.Get(TelegramBotTextIds.AlbumDetailTracks)}: 0",
                after.String("text")!, StringComparison.Ordinal);
            Assert.Contains(
                TelegramBotTexts.Get(TelegramBotTextIds.AlbumDetailNoTracks),
                after.String("text")!, StringComparison.Ordinal);

            // The removal itself is auditable, and only after the confirmation tap landed.
            await using var auditScope = factory.Services.CreateAsyncScope();
            var auditDb = auditScope.ServiceProvider.GetRequiredService<MusicHubDbContext>();
            var removal = await auditDb.AuditLogs.AsNoTracking()
                .SingleAsync(x => x.Action == "album.track.remove"
                                 && x.EntityType == "AlbumTrack"
                                 && x.EntityId == albumId.ToString("N"));
            Assert.Null(removal.ActorUserId);
            Assert.Contains("\"actor\":\"telegram\"", removal.DetailsJson!, StringComparison.Ordinal);
        }
        finally
        {
            await ResetAsync();
        }
    }

    [Fact]
    public async Task A_track_can_be_added_to_an_album_from_the_bot()
    {
        using var api = new FakeTelegramApi();
        try
        {
            await ConfigureAsync(api);

            var (albumId, _) = await SeedAlbumAsync("ReportTest Add Album", "ReportTest Add Artist");
            var outsiderTitle = "ReportTest Outside Song";
            var outsiderId = await SeedTrackAsync(outsiderTitle, "ReportTest Add Artist", 42);
            var remainingTitle = "ReportTest Remaining Song";
            await SeedTrackAsync(remainingTitle, "ReportTest Add Artist", 30);

            // Open the album, then ask for tracks that are not in it.
            api.PendingUpdates = Callback(9151, "cb-9151", $"v1:amopen:{albumId:N}", PromptMessageId);
            await PollOnceAsync();
            api.PendingUpdates = Callback(9152, "cb-9152", $"v1:amaddlist:{albumId:N}:0", PromptMessageId);
            await PollOnceAsync();

            var addScreen = LastCall(api, "editMessageText");
            var add = SingleButton(addScreen, data => data.StartsWith($"v1:amadd:{albumId:N}:", StringComparison.Ordinal)
                                                      && ButtonText(addScreen, data).Contains(outsiderTitle, StringComparison.Ordinal));

            api.PendingUpdates = Callback(9153, "cb-9153", add, PromptMessageId);
            await PollOnceAsync();

            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<MusicHubDbContext>();
                Assert.True(await db.AlbumTracks.AnyAsync(x => x.AlbumId == albumId && x.TrackId == outsiderId));
                var track = await db.Tracks.AsNoTracking().SingleAsync(x => x.Id == outsiderId);
                Assert.Equal("ReportTest Add Album", track.AlbumDisplay);
            }

            // The operator stays on the shortlist so several tracks can be added in a row: the
            // added track is gone from it and the next candidate is still offered.
            var after = LastCall(api, "editMessageText");
            var afterText = after.String("text")!;
            Assert.Contains(
                TelegramBotTexts.Get(TelegramBotTextIds.AlbumAddHint),
                afterText, StringComparison.Ordinal);

            var afterLabels = ButtonTextsStartingWith("➕ ", after);
            Assert.DoesNotContain(afterLabels, label => label.Contains(outsiderTitle, StringComparison.Ordinal));
            Assert.Contains(afterLabels, label => label.Contains(remainingTitle, StringComparison.Ordinal));

            var answer = LastCall(api, "answerCallbackQuery");
            Assert.Contains(outsiderTitle, answer.String("text")!, StringComparison.Ordinal);

            // Adding a track from the bot is a real, auditable library change.
            await using var addScope = factory.Services.CreateAsyncScope();
            var addDb = addScope.ServiceProvider.GetRequiredService<MusicHubDbContext>();
            var addAudit = await addDb.AuditLogs.AsNoTracking()
                .SingleAsync(x => x.Action == "album.track.add"
                                 && x.EntityType == "AlbumTrack"
                                 && x.EntityId == albumId.ToString("N"));
            Assert.Contains("\"trackId\":\"" + outsiderId + "\"", addAudit.DetailsJson!, StringComparison.Ordinal);
        }
        finally
        {
            await ResetAsync();
        }
    }

    [Fact]
    public async Task A_stale_remove_button_rebuilds_the_screen_and_removes_nothing()
    {
        using var api = new FakeTelegramApi();
        try
        {
            await ConfigureAsync(api);

            var (albumId, trackId) = await SeedAlbumAsync("ReportTest Stale Album", "ReportTest Stale Artist");

            // Index 7 is outside the one-row mapping this screen stored. Nothing may be removed.
            api.PendingUpdates = Callback(9161, "cb-9161", $"v1:amrm:{albumId:N}:7", PromptMessageId);
            await PollOnceAsync();

            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<MusicHubDbContext>();
                Assert.True(await db.AlbumTracks.AnyAsync(x => x.AlbumId == albumId && x.TrackId == trackId));
            }

            // The operator is not left on a dead screen: a real album screen was rendered instead.
            var rebuilt = LastCall(api, "editMessageText");
            Assert.Contains("ReportTest Stale Album", rebuilt.String("text")!, StringComparison.Ordinal);
            Assert.Contains($"v1:amrm:{albumId:N}:0", ButtonCallbacks(rebuilt));
            Assert.Equal(
                TelegramBotTexts.Get(TelegramBotTextIds.SessionExpired),
                LastCall(api, "answerCallbackQuery").String("text"));
        }
        finally
        {
            await ResetAsync();
        }
    }

    [Fact]
    public async Task The_album_track_delete_endpoint_clears_the_display_album()
    {
        var (albumId, trackId) = await SeedAlbumAsync("ReportTest Http Album", "ReportTest Http Artist");
        try
        {
            using var admin = await factory.CreateAdminClientAsync();
            var response = await admin.DeleteAsync($"/api/albums/{albumId}/tracks/{trackId}");
            Assert.Equal(System.Net.HttpStatusCode.NoContent, response.StatusCode);

            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<MusicHubDbContext>();
            Assert.False(await db.AlbumTracks.AnyAsync(x => x.AlbumId == albumId && x.TrackId == trackId));

            // Both transports share AlbumService, so both clear the denormalized hint.
            var track = await db.Tracks.AsNoTracking().SingleAsync(x => x.Id == trackId);
            Assert.Equal(string.Empty, track.AlbumDisplay);
        }
        finally
        {
            await ResetAsync();
        }
    }

    // ---- seeding ------------------------------------------------------------------------------

    private async Task<(Guid AlbumId, Guid TrackId)> SeedAlbumAsync(string albumTitle, string artistName)
    {
        var trackId = await SeedTrackAsync(albumTitle.Replace(" Album", " Song", StringComparison.Ordinal), artistName, 180);

        await using var scope = factory.Services.CreateAsyncScope();
        var albums = scope.ServiceProvider.GetRequiredService<AlbumService>();
        var created = await albums.CreateAsync(albumTitle, artistName, null, null, null, null, CancellationToken.None);
        Assert.True(created.Succeeded);
        var albumId = created.AlbumId!.Value;
        Assert.True((await albums.AssignTrackAsync(albumId, trackId, null, CancellationToken.None)).Succeeded);
        return (albumId, trackId);
    }

    /// <summary>
    /// A live track with real metadata, its own content hash and a primary artist credit, so the
    /// reports and album screens read exactly what the importer would have written.
    /// </summary>
    private async Task<Guid> SeedTrackAsync(string title, string artistName, double durationSeconds)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MusicHubDbContext>();

        var normalizedArtist = artistName.ToLowerInvariant();
        var artist = await db.Artists.FirstOrDefaultAsync(x => x.NormalizedName == normalizedArtist);
        if (artist is null)
        {
            artist = new Artist { Name = artistName, NormalizedName = normalizedArtist };
            db.Artists.Add(artist);
        }

        var track = new Track
        {
            Title = title,
            NormalizedTitle = title.ToLowerInvariant(),
            ArtistDisplay = artistName,
            AlbumDisplay = string.Empty,
            DurationSeconds = durationSeconds,
            AudioAsset = new AudioAsset
            {
                OriginalFileName = title + ".mp3",
                Extension = ".mp3",
                MimeType = "audio/mpeg",
                StoragePath = "tests/" + Guid.NewGuid().ToString("N"),
                SizeBytes = 1024,
                FileHash = new FileHash
                {
                    Sha256 = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"),
                    SizeBytes = 1024
                }
            }
        };
        track.TrackArtists.Add(new TrackArtist { Artist = artist, Role = "Primary", Position = 0 });

        db.Tracks.Add(track);
        await db.SaveChangesAsync();
        return track.Id;
    }

    // ---- update helpers -----------------------------------------------------------------------

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

    private static string Label(string textId, int value) =>
        TelegramBotTexts.Get(textId) + ": " + value.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Every <c>callback_data</c> in a rendered message, in row order.</summary>
    private static List<string> ButtonCallbacks(RecordedRequest message)
    {
        var result = new List<string>();
        if (!message.Root.TryGetProperty("reply_markup", out var markup)) return result;
        if (!markup.TryGetProperty("inline_keyboard", out var rows)) return result;

        foreach (var row in rows.EnumerateArray())
        {
            foreach (var button in row.EnumerateArray())
            {
                result.Add(button.GetProperty("callback_data").GetString()!);
            }
        }
        return result;
    }

    private static string ButtonText(RecordedRequest message, string callbackData)
    {
        foreach (var row in message.Root.GetProperty("reply_markup").GetProperty("inline_keyboard").EnumerateArray())
        {
            foreach (var button in row.EnumerateArray())
            {
                if (button.GetProperty("callback_data").GetString() == callbackData)
                {
                    return button.GetProperty("text").GetString() ?? string.Empty;
                }
            }
        }
        Assert.Fail($"No button carries callback_data {callbackData}.");
        return string.Empty;
    }

    private static string SingleButton(RecordedRequest message, Func<string, bool> predicate)
    {
        var matches = ButtonCallbacks(message).Where(predicate).ToList();
        Assert.True(matches.Count == 1, $"Expected exactly one matching button but found {matches.Count}.");
        return matches[0];
    }

    private static List<string> ButtonTextsStartingWith(string prefix, RecordedRequest message)
    {
        var result = new List<string>();
        foreach (var row in message.Root.GetProperty("reply_markup").GetProperty("inline_keyboard").EnumerateArray())
        {
            foreach (var button in row.EnumerateArray())
            {
                var text = button.GetProperty("text").GetString() ?? string.Empty;
                if (text.StartsWith(prefix, StringComparison.Ordinal)) result.Add(text);
            }
        }
        return result;
    }

    private static RecordedRequest LastCall(FakeTelegramApi api, string method)
    {
        var calls = api.Calls(method);
        Assert.NotEmpty(calls);
        return calls[^1];
    }

    private async Task<TelegramIngestionResult> PollOnceAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var ingestion = scope.ServiceProvider.GetRequiredService<TelegramIngestionService>();
        return await ingestion.PollOnceAsync(CancellationToken.None);
    }

    private async Task ConfigureAsync(FakeTelegramApi api)
    {
        using (var bootstrap = await factory.CreateAdminClientAsync())
        {
            Assert.NotNull(bootstrap);
        }

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MusicHubDbContext>();
        var protector = scope.ServiceProvider.GetRequiredService<TelegramSecretProtector>();

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

    /// <summary>Removes every row these tests created, so the shared suite stays order-independent.</summary>
    private async Task ResetAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MusicHubDbContext>();

        db.TelegramConfigurations.RemoveRange(await db.TelegramConfigurations.ToListAsync());
        db.TelegramImports.RemoveRange(await db.TelegramImports
            .Where(x => x.TelegramUpdateId >= 9000 && x.TelegramUpdateId < 10000).ToListAsync());
        db.TelegramUpdates.RemoveRange(await db.TelegramUpdates
            .Where(x => x.TelegramUpdateId >= 9000 && x.TelegramUpdateId < 10000).ToListAsync());

        var trackIds = await db.Tracks
            .Where(x => x.Title.StartsWith("ReportTest "))
            .Select(x => new { x.Id, x.AudioAssetId, x.AudioAsset.FileHashId })
            .ToListAsync();

        if (trackIds.Count > 0)
        {
            var ids = trackIds.Select(x => x.Id).ToList();
            db.AlbumTracks.RemoveRange(await db.AlbumTracks.Where(x => ids.Contains(x.TrackId)).ToListAsync());
            db.Tracks.RemoveRange(await db.Tracks.Where(x => ids.Contains(x.Id)).ToListAsync());
            await db.SaveChangesAsync();

            var assetIds = trackIds.Select(x => x.AudioAssetId).Distinct().ToList();
            var hashIds = trackIds.Select(x => x.FileHashId).Distinct().ToList();
            db.AudioAssets.RemoveRange(await db.AudioAssets.Where(x => assetIds.Contains(x.Id)).ToListAsync());
            await db.SaveChangesAsync();
            db.FileHashes.RemoveRange(await db.FileHashes.Where(x => hashIds.Contains(x.Id)).ToListAsync());
        }

        var albumIds = await db.Albums.Where(x => x.Title.StartsWith("ReportTest "))
            .Select(x => x.Id).ToListAsync();
        if (albumIds.Count > 0)
        {
            db.AlbumTracks.RemoveRange(await db.AlbumTracks.Where(x => albumIds.Contains(x.AlbumId)).ToListAsync());
            db.Albums.RemoveRange(await db.Albums.Where(x => albumIds.Contains(x.Id)).ToListAsync());

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
                        || x.Key.StartsWith(TelegramAlbumPromptStore.KeyPrefix)
                        || x.Key.StartsWith(TelegramAlbumEditStore.KeyPrefix))
            .ToListAsync();
        db.AppSettings.RemoveRange(settings);
        await db.SaveChangesAsync();
    }
}
