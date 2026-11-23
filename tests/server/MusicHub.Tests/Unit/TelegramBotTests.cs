using System.Text;
using System.Text.Json;
using MusicHub.Domain;
using MusicHub.Infrastructure.Library;
using MusicHub.Infrastructure.Telegram;

namespace MusicHub.Tests.Unit;

/// <summary>
/// Pure tests for the Phase 0 bot foundation: callback_data encoding, keyboard building, texts, the
/// update classification table, the administrator gate and the status rendering. No database, no
/// network - these are the invariants the rest of the feature depends on.
/// </summary>
public class TelegramCallbackDataTests
{
    [Fact]
    public void Round_trips_an_action_without_an_argument()
    {
        var encoded = TelegramCallbackData.Encode("refresh");

        Assert.Equal("v1:refresh", encoded);
        Assert.True(TelegramCallbackData.TryParse(encoded, out var parsed));
        Assert.Equal("refresh", parsed.Action);
        Assert.Null(parsed.Argument);
    }

    [Fact]
    public void Encodes_a_pagination_argument_that_contains_a_separator()
    {
        var encoded = TelegramCallbackData.Encode("pg", "library:3");

        Assert.Equal("v1:pg:library:3", encoded);
        Assert.True(TelegramCallbackData.TryParse(encoded, out var parsed));
        Assert.Equal("pg", parsed.Action);
        Assert.Equal("library:3", parsed.Argument);
    }

    [Fact]
    public void Accepts_exactly_the_64_byte_limit_and_refuses_one_byte_more()
    {
        // "v1:refresh:" is 11 bytes, so 53 argument characters reach exactly 64.
        var atLimit = new string('a', 53);
        var encoded = TelegramCallbackData.Encode("refresh", atLimit);
        Assert.Equal(TelegramCallbackData.MaxBytes, Encoding.UTF8.GetByteCount(encoded));

        var overLimit = Assert.Throws<ArgumentException>(() => TelegramCallbackData.Encode("refresh", new string('a', 54)));
        Assert.Contains("64 bytes", overLimit.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Refuses_to_encode_a_token_shaped_argument()
    {
        // A real bot token contains ':' and '_'; neither may appear in an argument, so a token can
        // never be smuggled into a button.
        var token = "123456789:AAFakeTokenValueForTestingOnly_1234567890";

        Assert.Throws<ArgumentException>(() => TelegramCallbackData.Encode("refresh", token));
    }

    [Fact]
    public void Refuses_to_encode_a_filesystem_path()
    {
        Assert.Throws<ArgumentException>(() => TelegramCallbackData.Encode("open", "music/2026/track.mp3"));
        Assert.Throws<ArgumentException>(() => TelegramCallbackData.Encode("open", @"C:\music\track.mp3"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Refresh")]
    [InlineData("re fresh")]
    [InlineData("re/fresh")]
    [InlineData("action_with_underscore")]
    public void Refuses_an_invalid_action(string action)
    {
        Assert.Throws<ArgumentException>(() => TelegramCallbackData.Encode(action));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("refresh")]
    [InlineData("v2:refresh")]
    [InlineData("v1:")]
    [InlineData("v1:REFRESH")]
    [InlineData("v1:refresh:")]
    [InlineData("v1:refresh:has space")]
    [InlineData("v1:refresh:path/segment")]
    public void TryParse_rejects_anything_that_is_not_the_versioned_envelope(string? data)
    {
        Assert.False(TelegramCallbackData.TryParse(data, out _));
    }

    [Fact]
    public void TryParse_rejects_data_longer_than_the_limit()
    {
        var oversized = "v1:refresh:" + new string('a', 60);
        Assert.False(TelegramCallbackData.TryParse(oversized, out _));
    }

    [Fact]
    public void A_hand_built_callback_data_is_validated_before_it_reaches_a_keyboard()
    {
        Assert.Throws<ArgumentException>(() => TelegramCallbackData.Validate("v1:refresh:too_much_underscore"));
        TelegramCallbackData.Validate("v1:refresh");
    }
}

public class TelegramKeyboardTests
{
    [Fact]
    public void The_panel_offers_reports_albums_artists_storage_and_refresh()
    {
        var keyboard = TelegramKeyboard.StatusScreen();

        Assert.Equal(3, keyboard.Rows.Count);
        Assert.Equal(
            new[] { "v1:reports", "v1:amalbs:0" },
            keyboard.Rows[0].Select(button => button.CallbackData));
        Assert.Equal(
            new[] { "v1:artists:0", "v1:storage" },
            keyboard.Rows[1].Select(button => button.CallbackData));
        Assert.Equal(new[] { "v1:refresh" }, keyboard.Rows[2].Select(button => button.CallbackData));
        Assert.All(keyboard.Rows.SelectMany(row => row),
            button => Assert.False(string.IsNullOrWhiteSpace(button.Text)));
    }

    [Fact]
    public void An_album_screen_button_carries_the_album_id_and_the_row_index()
    {
        var albumId = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var tracks = new[]
        {
            new AlbumTrackRow(Guid.NewGuid(), "First Track", "Artist", 60, 1),
            new AlbumTrackRow(Guid.NewGuid(), "Second Track", "Artist", 90, 2)
        };

        var keyboard = TelegramKeyboard.AlbumDetailScreen(albumId, tracks, 0, 1);
        var id = albumId.ToString("N");

        Assert.Equal("v1:amrm:" + id + ":0", keyboard.Rows[0][0].CallbackData);
        Assert.Equal("v1:amrm:" + id + ":1", keyboard.Rows[1][0].CallbackData);
        Assert.Equal("v1:amaddlist:" + id + ":0", keyboard.Rows[2][0].CallbackData);
        Assert.Equal(new[] { "v1:amalbs:0", "v1:home" },
            keyboard.Rows[3].Select(button => button.CallbackData));

        // The whole point of the short encoding: an album id and a row index fit in 64 bytes.
        Assert.All(keyboard.Rows.SelectMany(row => row),
            button => Assert.True(System.Text.Encoding.UTF8.GetByteCount(button.CallbackData) <= 64));
    }

    [Fact]
    public void An_album_screen_never_ships_a_dead_paging_direction()
    {
        var albumId = Guid.NewGuid();
        var tracks = new[] { new AlbumTrackRow(Guid.NewGuid(), "Only", "Artist", 60, 1) };

        var single = TelegramKeyboard.AlbumDetailScreen(albumId, tracks, 0, 1);
        Assert.DoesNotContain(single.Rows.SelectMany(row => row),
            button => button.CallbackData.StartsWith("v1:ampgt:", StringComparison.Ordinal));

        var first = TelegramKeyboard.AlbumDetailScreen(albumId, tracks, 0, 3);
        Assert.Contains(first.Rows.SelectMany(row => row),
            button => button.CallbackData == "v1:ampgt:" + albumId.ToString("N") + ":1");

        var last = TelegramKeyboard.AlbumDetailScreen(albumId, tracks, 2, 3);
        Assert.Contains(last.Rows.SelectMany(row => row),
            button => button.CallbackData == "v1:ampgt:" + albumId.ToString("N") + ":1");
        Assert.DoesNotContain(last.Rows.SelectMany(row => row),
            button => button.CallbackData == "v1:ampgt:" + albumId.ToString("N") + ":3");
    }

    [Fact]
    public void A_long_album_or_artist_name_is_clipped_to_fit_a_button()
    {
        var longTitle = new string('x', 200);
        var keyboard = TelegramKeyboard.AlbumManageScreen(
            new[] { new AlbumSummary(Guid.NewGuid(), longTitle, "Artist", 4) }, 0, 1);

        Assert.All(keyboard.Rows.SelectMany(row => row),
            button => Assert.True(button.Text.Length <= 64));
    }

    [Fact]
    public void Serialises_to_the_reply_markup_shape_the_bot_api_expects()
    {
        var keyboard = TelegramKeyboard.Create()
            .AddRow("Refresh", "v1:refresh")
            .AddRow("Back", "v1:back");

        using var document = JsonDocument.Parse(keyboard.ToJson());
        var inline = document.RootElement.GetProperty("inline_keyboard");
        Assert.Equal(2, inline.GetArrayLength());
        Assert.Equal("Refresh", inline[0][0].GetProperty("text").GetString());
        Assert.Equal("v1:refresh", inline[0][0].GetProperty("callback_data").GetString());
        Assert.Equal("v1:back", inline[1][0].GetProperty("callback_data").GetString());
    }

    [Fact]
    public void A_navigation_row_always_carries_back_and_home()
    {
        var keyboard = TelegramKeyboard.Create().AddNavigation();

        var row = Assert.Single(keyboard.Rows);
        Assert.Equal(2, row.Count);
        Assert.Equal("v1:back", row[0].CallbackData);
        Assert.Equal("v1:home", row[1].CallbackData);
    }

    [Fact]
    public void A_two_step_confirmation_encodes_the_action_id_and_a_real_cancel()
    {
        var keyboard = TelegramKeyboard.Create().AddConfirmCancel("track-delete");

        var row = Assert.Single(keyboard.Rows);
        Assert.Equal("v1:confirm:track-delete", row[0].CallbackData);
        Assert.Equal("v1:cancel", row[1].CallbackData);
        Assert.True(TelegramCallbackData.TryParse(row[0].CallbackData, out var parsed));
        Assert.Equal("track-delete", parsed.Argument);
    }

    [Fact]
    public void Pagination_only_renders_directions_that_exist()
    {
        // Only page first: next only, so there is no dead "previous" button.
        var first = TelegramKeyboard.Create().AddPagination("library", 0, 3);
        Assert.Equal("v1:pg:library:1", Assert.Single(Assert.Single(first.Rows)).CallbackData);

        // Middle: both directions.
        var middle = TelegramKeyboard.Create().AddPagination("library", 1, 3);
        var row = Assert.Single(middle.Rows);
        Assert.Equal("v1:pg:library:0", row[0].CallbackData);
        Assert.Equal("v1:pg:library:2", row[1].CallbackData);

        // Last: previous only.
        var last = TelegramKeyboard.Create().AddPagination("library", 2, 3);
        Assert.Equal("v1:pg:library:1", Assert.Single(Assert.Single(last.Rows)).CallbackData);

        // A single-page screen needs no pagination row at all.
        var single = TelegramKeyboard.Create().AddPagination("library", 0, 1);
        Assert.True(single.IsEmpty);
    }

    [Theory]
    [InlineData(4, 3)]
    [InlineData(3, 3)]
    [InlineData(-1, 3)]
    public void Pagination_rejects_pages_outside_the_range(int page, int totalPages)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            TelegramKeyboard.Create().AddPagination("library", page, totalPages));
    }

    [Fact]
    public void Pagination_with_no_pages_is_a_programming_error()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TelegramKeyboard.Create().AddPagination("library", 0, 0));
    }

    [Fact]
    public void A_row_cannot_carry_callback_data_that_would_break_the_envelope()
    {
        Assert.Throws<ArgumentException>(() => TelegramKeyboard.Create().AddRow("Open", "v1:open:/etc/passwd"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_button_without_a_label_is_refused(string text)
    {
        Assert.Throws<ArgumentException>(() => TelegramKeyboard.Create().AddRow(text, "v1:refresh"));
    }

    [Fact]
    public void A_button_label_longer_than_the_api_limit_is_refused()
    {
        Assert.Throws<ArgumentException>(() =>
            TelegramKeyboard.Create().AddRow(new string('x', 65), "v1:refresh"));
    }
}

public class TelegramBotTextsTests
{
    [Fact]
    public void Every_id_exists_in_both_languages()
    {
        Assert.NotEmpty(TelegramBotTexts.Ids);
        foreach (var id in TelegramBotTexts.Ids)
        {
            Assert.False(string.IsNullOrWhiteSpace(TelegramBotTexts.Get(id, TelegramBotTexts.PersianLanguage)));
            Assert.False(string.IsNullOrWhiteSpace(TelegramBotTexts.Get(id, TelegramBotTexts.EnglishLanguage)));
        }
    }

    [Fact]
    public void The_resolver_defaults_to_persian()
    {
        foreach (var id in TelegramBotTexts.Ids)
        {
            Assert.Equal(TelegramBotTexts.Get(id, TelegramBotTexts.PersianLanguage), TelegramBotTexts.Get(id));
            Assert.Equal(TelegramBotTexts.Get(id, TelegramBotTexts.PersianLanguage), TelegramBotTexts.Get(id, "de"));
        }
    }

    [Fact]
    public void Persian_and_english_use_the_same_placeholders()
    {
        // A template whose placeholders differ between languages cannot be filled correctly: the
        // formatter falls back to shipping the raw "{0}" text, so the operator would read a brace
        // instead of the album name. This keeps the two dictionaries in step as ids are added.
        foreach (var id in TelegramBotTexts.Ids)
        {
            var persian = Placeholders(TelegramBotTexts.Get(id, TelegramBotTexts.PersianLanguage));
            var english = Placeholders(TelegramBotTexts.Get(id, TelegramBotTexts.EnglishLanguage));
            Assert.True(persian.SetEquals(english),
                $"Text id \"{id}\" uses {string.Join(",", persian)} in Persian but {string.Join(",", english)} in English.");
        }
    }

    private static HashSet<string> Placeholders(string template) =>
        System.Text.RegularExpressions.Regex
            .Matches(template, @"\{\d+\}")
            .Select(match => match.Value)
            .ToHashSet(StringComparer.Ordinal);

    [Fact]
    public void Persian_and_english_differ_for_every_id()
    {
        foreach (var id in TelegramBotTexts.Ids)
        {
            Assert.NotEqual(
                TelegramBotTexts.Get(id, TelegramBotTexts.PersianLanguage),
                TelegramBotTexts.Get(id, TelegramBotTexts.EnglishLanguage));
        }
    }

    [Fact]
    public void Every_persian_string_contains_persian_letters()
    {
        foreach (var id in TelegramBotTexts.Ids)
        {
            var text = TelegramBotTexts.Get(id, TelegramBotTexts.PersianLanguage);
            Assert.True(text.Any(c => c is >= '\u0600' and <= '\u06FF'),
                $"The Persian text for \"{id}\" contains no Persian letters.");
        }
    }

    [Fact]
    public void No_string_carries_an_explicit_directional_override()
    {
        // U+200E/U+200F (LRM/RLM) and U+202A..U+202E / U+2066..U+2069 (embeddings and isolates)
        // would let a string fight the chat's base direction; none is needed here.
        char[] forbidden = ['\u200E', '\u200F', '\u202A', '\u202B', '\u202C', '\u202D', '\u202E',
                            '\u2066', '\u2067', '\u2068', '\u2069'];

        foreach (var id in TelegramBotTexts.Ids)
        {
            foreach (var language in TelegramBotTexts.Languages)
            {
                var text = TelegramBotTexts.Get(id, language);
                Assert.DoesNotContain(text, c => forbidden.Contains(c));
                Assert.DoesNotContain("\r", text);
            }
        }
    }

    [Fact]
    public void An_unknown_id_fails_loudly_instead_of_shipping_the_identifier()
    {
        Assert.Throws<KeyNotFoundException>(() => TelegramBotTexts.Get("no_such_text_id"));
    }

    [Fact]
    public void The_denial_text_cannot_reveal_a_configured_id()
    {
        var denial = TelegramBotTexts.Denial;

        Assert.False(string.IsNullOrWhiteSpace(denial));
        // A configured chat id or user id is digits; the fixed refusal contains none.
        Assert.DoesNotContain(denial, char.IsAsciiDigit);
        Assert.DoesNotContain("900501", denial, StringComparison.Ordinal);
    }

    [Fact]
    public void The_status_screen_renders_real_values()
    {
        var status = new TelegramBotStatus(424242, 7, "Failed", DateTimeOffset.UtcNow, 3, "2026-11-19T10:11:12.0000000+00:00");

        var text = TelegramBotTexts.FormatStatus(status);

        foreach (var expected in new[] { "424242", "7", "Failed", "3", "2026-11-19T10:11:12.0000000+00:00" })
        {
            Assert.Contains(expected, text, StringComparison.Ordinal);
        }
        Assert.Contains(TelegramBotTexts.Get(TelegramBotTextIds.StatusTitle), text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_status_screen_reports_missing_values_instead_of_inventing_them()
    {
        var status = new TelegramBotStatus(0, 0, null, null, 0, null);

        var text = TelegramBotTexts.FormatStatus(status);

        Assert.Contains(TelegramBotTexts.Get(TelegramBotTextIds.NoRecord), text, StringComparison.Ordinal);
        Assert.Contains(TelegramBotTexts.Get(TelegramBotTextIds.StatusNever), text, StringComparison.Ordinal);
    }

    [Fact]
    public void Duration_is_rendered_in_minutes_and_hours()
    {
        Assert.Equal("0:00", TelegramBotTexts.FormatDuration(0));
        Assert.Equal("3:05", TelegramBotTexts.FormatDuration(185));
        Assert.Equal("1:02:03", TelegramBotTexts.FormatDuration(3723));
    }

    [Fact]
    public void The_overview_names_every_counter_the_operator_asked_for()
    {
        var overview = new LibraryOverview(3, 13, 6, 15, 8, 7, 3723);

        var text = TelegramBotTexts.FormatOverview(overview);

        foreach (var line in new[]
                 {
                     TelegramBotTexts.Get(TelegramBotTextIds.ReportsAlbums) + ": 3",
                     TelegramBotTexts.Get(TelegramBotTextIds.ReportsTracks) + ": 13",
                     TelegramBotTexts.Get(TelegramBotTextIds.ReportsArtists) + ": 6",
                     TelegramBotTexts.Get(TelegramBotTextIds.ReportsUsers) + ": 15",
                     TelegramBotTexts.Get(TelegramBotTextIds.ReportsUsersActive) + ": 8",
                     TelegramBotTexts.Get(TelegramBotTextIds.ReportsUsersInactive) + ": 7",
                     TelegramBotTexts.Get(TelegramBotTextIds.ReportsDuration) + ": 1:02:03"
                 })
        {
            Assert.Contains(line, text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void The_album_report_shows_its_totals_and_artist_share()
    {
        var report = new AlbumReport(Guid.NewGuid(), "Album", "Artist", 1999, 2, 150,
            new[] { new AlbumArtistShare("Artist", 2) });

        var text = TelegramBotTexts.FormatAlbumDetail(report, 0, 1);

        Assert.Contains("Album", text, StringComparison.Ordinal);
        Assert.Contains("1999", text, StringComparison.Ordinal);
        Assert.Contains("2:30", text, StringComparison.Ordinal);
        Assert.Contains("Artist: 2", text, StringComparison.Ordinal);
        Assert.DoesNotContain(TelegramBotTexts.Get(TelegramBotTextIds.AlbumDetailNoTracks), text,
            StringComparison.Ordinal);
    }

    [Fact]
    public void An_empty_album_says_so_instead_of_showing_an_empty_list()
    {
        var report = new AlbumReport(Guid.NewGuid(), "Empty", "Artist", null, 0, 0,
            Array.Empty<AlbumArtistShare>());

        var text = TelegramBotTexts.FormatAlbumDetail(report, 0, 1);

        Assert.Contains(TelegramBotTexts.Get(TelegramBotTextIds.AlbumDetailNoTracks), text,
            StringComparison.Ordinal);
    }
}

public class TelegramUpdateClassificationTests
{
    [Fact]
    public void An_audio_message_is_the_frozen_message_path()
    {
        Assert.Equal(TelegramUpdateKind.Message, Classify("""
            { "update_id": 1, "message": { "message_id": 1, "from": { "id": 1 }, "chat": { "id": 1 },
              "audio": { "file_id": "x", "file_name": "a.mp3", "mime_type": "audio/mpeg", "file_size": 10, "duration": 5 } } }
            """));
    }

    [Fact]
    public void A_document_message_is_the_frozen_message_path()
    {
        Assert.Equal(TelegramUpdateKind.Message, Classify("""
            { "update_id": 2, "message": { "message_id": 2, "from": { "id": 1 }, "chat": { "id": 1 },
              "document": { "file_id": "x", "file_name": "a.flac", "mime_type": "audio/flac", "file_size": 10 } } }
            """));
    }

    [Theory]
    [InlineData("""{ "update_id": 3, "message": { "message_id": 3, "from": { "id": 1 }, "chat": { "id": 1 }, "text": "hello" } }""")]
    [InlineData("""{ "update_id": 4, "message": { "message_id": 4, "from": { "id": 1 }, "chat": { "id": 1 }, "photo": [ { "file_id": "p" } ] } }""")]
    [InlineData("""{ "update_id": 5, "message": { "message_id": 5, "from": { "id": 1 }, "chat": { "id": 1 }, "sticker": { "file_id": "s" } } }""")]
    public void Any_other_message_keeps_the_frozen_rejection_path(string update)
    {
        // Not media and not a command: the existing ingestion handler must still answer it with the
        // unchanged rejection, so it is classified as a message, not as ignored.
        Assert.Equal(TelegramUpdateKind.Message, Classify(update));
    }

    [Theory]
    [InlineData("/start")]
    [InlineData("/status")]
    [InlineData("/start@musichub_test_bot")]
    [InlineData(" /help")]
    [InlineData("/cancel")]
    public void A_slash_command_is_a_command(string text)
    {
        var update = $$"""
            { "update_id": 6, "message": { "message_id": 6, "from": { "id": 1 }, "chat": { "id": 1 }, "text": {{JsonSerializer.Serialize(text)}} } }
            """;
        Assert.Equal(TelegramUpdateKind.Command, Classify(update));
    }

    [Fact]
    public void Media_wins_over_command_looking_text()
    {
        Assert.Equal(TelegramUpdateKind.Message, Classify("""
            { "update_id": 7, "message": { "message_id": 7, "from": { "id": 1 }, "chat": { "id": 1 }, "text": "/start",
              "document": { "file_id": "x", "file_name": "a.mp3", "mime_type": "audio/mpeg", "file_size": 10 } } }
            """));
    }

    [Fact]
    public void A_callback_query_is_a_callback()
    {
        Assert.Equal(TelegramUpdateKind.CallbackQuery, Classify("""
            { "update_id": 8, "callback_query": { "id": "cb-1", "from": { "id": 1 }, "data": "v1:refresh",
              "message": { "message_id": 10, "chat": { "id": 1, "type": "private" } } } }
            """));
    }

    [Theory]
    [InlineData("""{ "update_id": 9, "inline_query": { "id": "iq", "from": { "id": 1 }, "query": "" } }""")]
    [InlineData("""{ "update_id": 10, "edited_message": { "message_id": 1, "from": { "id": 1 }, "chat": { "id": 1 }, "text": "x" } }""")]
    [InlineData("""{ "update_id": 11, "channel_post": { "message_id": 1, "chat": { "id": -100 }, "text": "x" } }""")]
    [InlineData("""{ "update_id": 12 }""")]
    public void Updates_the_bot_does_not_act_on_are_ignored(string update)
    {
        Assert.Equal(TelegramUpdateKind.Ignored, Classify(update));
    }

    [Fact]
    public void A_non_object_payload_is_ignored_rather_than_throwing()
    {
        using var document = JsonDocument.Parse("[]");
        Assert.Equal(TelegramUpdateKind.Ignored, TelegramUpdateRouter.Classify(document.RootElement));
    }

    private static TelegramUpdateKind Classify(string json)
    {
        using var document = JsonDocument.Parse(json);
        return TelegramUpdateRouter.Classify(document.RootElement);
    }
}

public class TelegramBotAdminGateTests
{
    [Fact]
    public void The_configured_administrator_is_allowed()
    {
        var config = new TelegramConfiguration { AdminTelegramUserId = 900501, AdminChatId = 900501 };

        Assert.True(TelegramBotAdminGate.IsAllowed(config, 900501, 900501));
    }

    [Fact]
    public void The_administrator_is_allowed_in_any_chat_when_no_chat_is_configured()
    {
        var config = new TelegramConfiguration { AdminTelegramUserId = 900501, AdminChatId = null };

        Assert.True(TelegramBotAdminGate.IsAllowed(config, 900501, 123456));
    }

    [Fact]
    public void Another_sender_is_refused()
    {
        var config = new TelegramConfiguration { AdminTelegramUserId = 900501, AdminChatId = null };

        Assert.False(TelegramBotAdminGate.IsAllowed(config, 999999, 999999));
        Assert.False(TelegramBotAdminGate.IsAllowed(config, 0, 900501));
    }

    [Fact]
    public void The_administrator_is_refused_in_an_unexpected_chat()
    {
        var config = new TelegramConfiguration { AdminTelegramUserId = 900501, AdminChatId = 900501 };

        Assert.False(TelegramBotAdminGate.IsAllowed(config, 900501, -1001234567890));
    }

    [Theory]
    [InlineData(null, 900501L)]
    [InlineData(0L, 900501L)]
    public void Nothing_is_allowed_before_an_administrator_is_configured(long? adminUserId, long senderId)
    {
        var config = new TelegramConfiguration { AdminTelegramUserId = adminUserId };

        Assert.False(TelegramBotAdminGate.IsAllowed(config, senderId, senderId));
        Assert.False(TelegramBotAdminGate.IsAllowed(null, senderId, senderId));
    }

    [Fact]
    public void Multiple_administrators_configured_via_semicolon_are_all_allowed()
    {
        var config = new TelegramConfiguration { AdminTelegramUserId = 1234124, AdminChatId = null };
        var adminIds = TelegramBotAdminGate.ParseAdminIds("1234124;4324234;999888");

        Assert.True(TelegramBotAdminGate.IsAllowed(config, 1234124, 1234124, adminIds));
        Assert.True(TelegramBotAdminGate.IsAllowed(config, 4324234, 4324234, adminIds));
        Assert.True(TelegramBotAdminGate.IsAllowed(config, 999888, 999888, adminIds));
        Assert.False(TelegramBotAdminGate.IsAllowed(config, 555555, 555555, adminIds));
    }

    [Fact]
    public void ParseAdminIds_handles_various_formats_and_whitespace()
    {
        var ids = TelegramBotAdminGate.ParseAdminIds(" 1234124 ; 4324234 ; invalid ; 0 ; -5 ; 888 ");
        Assert.Equal(3, ids.Count);
        Assert.Contains(1234124L, ids);
        Assert.Contains(4324234L, ids);
        Assert.Contains(888L, ids);
    }
}

public class TelegramAlbumMediaGroupTests
{
    [Fact]
    public void TelegramAlbumPrompt_AllTrackIds_returns_single_track_when_list_is_null_or_empty()
    {
        var id = Guid.NewGuid();
        var prompt1 = new TelegramAlbumPrompt(id, "Track 1", "track1.mp3", false, DateTimeOffset.UtcNow);
        Assert.Single(prompt1.AllTrackIds);
        Assert.Equal(id, prompt1.AllTrackIds[0]);

        var prompt2 = new TelegramAlbumPrompt(id, "Track 1", "track1.mp3", false, DateTimeOffset.UtcNow, new List<Guid>());
        Assert.Single(prompt2.AllTrackIds);
        Assert.Equal(id, prompt2.AllTrackIds[0]);
    }

    [Fact]
    public void TelegramAlbumPrompt_AllTrackIds_returns_multiple_tracks_when_provided()
    {
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        var id3 = Guid.NewGuid();
        var prompt = new TelegramAlbumPrompt(id1, "Track 1", "track1.mp3", false, DateTimeOffset.UtcNow, [id1, id2, id3], "mg-12345");
        Assert.Equal(3, prompt.AllTrackIds.Count);
        Assert.Contains(id1, prompt.AllTrackIds);
        Assert.Contains(id2, prompt.AllTrackIds);
        Assert.Contains(id3, prompt.AllTrackIds);
        Assert.Equal("mg-12345", prompt.MediaGroupId);
    }

    [Fact]
    public void Formats_multi_track_album_prompt_and_assignment_in_both_languages()
    {
        var enPrompt = TelegramBotTexts.FormatAlbumPromptMulti(5, "album.mp3", "en");
        Assert.Contains("5 audio tracks", enPrompt);

        var faPrompt = TelegramBotTexts.FormatAlbumPromptMulti(5, "album.mp3", "fa");
        Assert.Contains("5 قطعه صوتی", faPrompt);

        var enAssigned = TelegramBotTexts.FormatAlbumAssignedMulti(5, "Rock 2026", "en");
        Assert.Contains("5 tracks were filed under \"Rock 2026\"", enAssigned);

        var faAssigned = TelegramBotTexts.FormatAlbumAssignedMulti(5, "Rock 2026", "fa");
        Assert.Contains("5 قطعه در آلبوم «Rock 2026» قرار گرفتند", faAssigned);
    }
}
