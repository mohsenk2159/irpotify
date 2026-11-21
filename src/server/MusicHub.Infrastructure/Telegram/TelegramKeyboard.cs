using System.Globalization;
using System.Text;
using System.Text.Json;
using MusicHub.Infrastructure.Library;

namespace MusicHub.Infrastructure.Telegram;

/// <summary>
/// A parsed Telegram <c>callback_data</c> envelope.
/// </summary>
/// <remarks>
/// The wire format is versioned and compact: <c>v1:&lt;action&gt;[:&lt;argument&gt;]</c>, at most
/// 64 bytes because that is the Bot API limit. Anything longer is a programming error and throws
/// instead of being silently truncated into a different action.
/// <para>
/// The character sets are deliberately narrow. Actions are lowercase alphanumerics and arguments
/// accept letters, digits, <c>.</c>, <c>:</c> and <c>-</c> only. That makes it impossible to encode
/// a bot token (which always contains <c>:</c> and <c>_</c>) or a filesystem path (which needs a
/// separator) into a button.
/// </para>
/// </remarks>
public readonly record struct TelegramCallbackData(string Action, string? Argument)
{
    public const string Version = "v1";
    public const int MaxBytes = 64;

    private const int MaxActionLength = 24;

    /// <summary>Encodes an action and an optional argument, refusing anything Telegram would reject.</summary>
    public static string Encode(string action, string? argument = null)
    {
        if (!IsValidAction(action))
        {
            throw new ArgumentException(
                $"A callback action must be 1-{MaxActionLength} lowercase ASCII letters or digits.", nameof(action));
        }

        var data = string.IsNullOrEmpty(argument)
            ? $"{Version}:{action}"
            : $"{Version}:{action}:{argument}";

        var bytes = Encoding.UTF8.GetByteCount(data);
        if (bytes > MaxBytes)
        {
            throw new ArgumentException(
                $"callback_data must not exceed {MaxBytes} bytes but was {bytes} bytes.", nameof(argument));
        }

        // No separate character cap: every character the argument charset allows is one UTF-8
        // byte, so the 64-byte envelope above is the single length rule. A second, smaller limit
        // would make the documented limit unreachable and turn its check into dead code.
        if (!string.IsNullOrEmpty(argument) && !IsValidArgument(argument))
        {
            throw new ArgumentException(
                "A callback argument must be non-empty and use only [A-Za-z0-9.:-].", nameof(argument));
        }

        return data;
    }

    /// <summary>Parses untrusted <c>callback_data</c>. Never throws; anything unrecognised is <c>false</c>.</summary>
    public static bool TryParse(string? data, out TelegramCallbackData result)
    {
        result = default;
        if (string.IsNullOrEmpty(data) || data.Length > MaxBytes || Encoding.UTF8.GetByteCount(data) > MaxBytes)
        {
            return false;
        }

        var firstSeparator = data.IndexOf(':');
        if (firstSeparator <= 0) return false;
        if (!data.AsSpan(0, firstSeparator).SequenceEqual(Version)) return false;

        var remainder = data.AsSpan(firstSeparator + 1);
        var secondSeparator = remainder.IndexOf(':');
        var action = secondSeparator < 0 ? remainder.ToString() : remainder[..secondSeparator].ToString();
        var argument = secondSeparator < 0 ? null : remainder[(secondSeparator + 1)..].ToString();

        if (!IsValidAction(action)) return false;
        if (argument is not null && !IsValidArgument(argument)) return false;

        result = new TelegramCallbackData(action, argument);
        return true;
    }

    /// <summary>Validates raw data that a caller built by hand, e.g. while assembling a keyboard.</summary>
    public static void Validate(string? data)
    {
        if (!TryParse(data, out _))
        {
            throw new ArgumentException(
                $"callback_data must match \"{Version}:<action>[:<argument>]\" with at most {MaxBytes} bytes.", nameof(data));
        }
    }

    private static bool IsValidAction(string? action) =>
        !string.IsNullOrEmpty(action)
        && action.Length <= MaxActionLength
        && action.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c));

    private static bool IsValidArgument(string? argument)
    {
        if (argument is null) return true;
        if (argument.Length == 0) return false;
        return argument.All(c =>
            char.IsAsciiLetterOrDigit(c) || c == '.' || c == ':' || c == '-');
    }
}

public sealed record TelegramInlineButton(string Text, string CallbackData);

/// <summary>
/// Builds the <c>reply_markup</c> payload for inline keyboards: plain rows, navigation,
/// two-step confirm/cancel and pagination.
/// </summary>
public sealed class TelegramKeyboard
{
    public const string RefreshAction = "refresh";
    public const string HomeAction = "home";
    public const string BackAction = "back";
    public const string ConfirmAction = "confirm";
    public const string CancelAction = "cancel";
    public const string PageAction = "pg";
    public const string StorageAction = "storage";
    public const string StorageStartAction = "storagestart";
    public const string StorageRefreshAction = "storagerefresh";
    public const string AlbumPickAction = "albumpick";
    public const string AlbumNewAction = "albumnew";
    public const string AlbumSkipAction = "albumskip";
    public const string AlbumPageAction = "albumpg";
    public const string AlbumCreateAction = "albumcreate";
    public const string ReportsAction = "reports";
    public const string AlbumManageAction = "amalbs";
    public const string AlbumOpenAction = "amopen";
    public const string AlbumTrackPageAction = "ampgt";
    public const string AlbumRemoveAction = "amrm";
    public const string AlbumRemoveConfirmAction = "amrmdo";
    public const string AlbumAddListAction = "amaddlist";
    public const string AlbumAddAction = "amadd";
    public const string ArtistListAction = "artists";
    public const string ArtistOpenAction = "artopen";

    /// <summary>How many albums one picker page shows. Telegram renders about 100 buttons safely.</summary>
    public const int AlbumsPerPage = 8;

    /// <summary>Rows per page for the album, track and artist reports.</summary>
    public const int ListPageSize = 8;

    private const int MaxButtonTextLength = 64;

    private readonly List<List<TelegramInlineButton>> _rows = new();

    public IReadOnlyList<IReadOnlyList<TelegramInlineButton>> Rows =>
        _rows.Select(row => (IReadOnlyList<TelegramInlineButton>)row.AsReadOnly()).ToList();

    public static TelegramKeyboard Create() => new();

    /// <summary>
    /// The status screen. Refresh re-reads the database and edits the message in place; Storage opens
    /// the library-location screen. Every button leads to a screen or an action that exists.
    /// </summary>
    public static TelegramKeyboard StatusScreen()
    {
        var keyboard = new TelegramKeyboard();
        keyboard.AddRow(
            Button(TelegramBotTextIds.MenuReports, ReportsAction),
            Button(TelegramBotTextIds.MenuAlbums, AlbumManageAction, "0"));
        keyboard.AddRow(
            Button(TelegramBotTextIds.MenuArtists, ArtistListAction, "0"),
            Button(TelegramBotTextIds.StorageTitleShort, StorageAction));
        keyboard.AddRow(Button(TelegramBotTextIds.Refresh, RefreshAction));
        return keyboard;
    }

    /// <summary>
    /// The storage screen's keyboard. The start button exists only when a path has just passed every
    /// safety check, so there is no control that cannot do its job.
    /// </summary>
    public static TelegramKeyboard StorageScreen(bool canStart)
    {
        var keyboard = new TelegramKeyboard();
        if (canStart)
        {
            keyboard.AddRow(new TelegramInlineButton(TelegramBotTexts.Get(TelegramBotTextIds.Confirm),
                TelegramCallbackData.Encode(StorageStartAction)));
        }
        keyboard.AddRow(
            new TelegramInlineButton(TelegramBotTexts.Get(TelegramBotTextIds.Refresh),
                TelegramCallbackData.Encode(StorageRefreshAction)),
            new TelegramInlineButton(TelegramBotTexts.Get(TelegramBotTextIds.Home),
                TelegramCallbackData.Encode(HomeAction)));
        return keyboard;
    }

    /// <summary>
    /// The per-file album question: existing albums to pick from, a button to create a new one, and
    /// a skip. Every button is a real choice, and the album ids are GUIDs which the callback charset
    /// already allows.
    /// </summary>
    public static TelegramKeyboard AlbumPrompt(bool canSkip = true)
    {
        var keyboard = new TelegramKeyboard();
        keyboard.AddRow(
            new TelegramInlineButton(TelegramBotTexts.Get(TelegramBotTextIds.AlbumChooseExisting),
                TelegramCallbackData.Encode(AlbumPageAction, "0")),
            new TelegramInlineButton(TelegramBotTexts.Get(TelegramBotTextIds.AlbumCreateNew),
                TelegramCallbackData.Encode(AlbumNewAction)));
        if (canSkip)
        {
            keyboard.AddRow(new TelegramInlineButton(TelegramBotTexts.Get(TelegramBotTextIds.AlbumSkip),
                TelegramCallbackData.Encode(AlbumSkipAction)));
        }
        return keyboard;
    }

    /// <summary>
    /// One page of the album picker. Album buttons carry the album id; the paging row only renders
    /// directions that exist, so no button is ever dead.
    /// </summary>
    public static TelegramKeyboard AlbumPicker(
        IReadOnlyList<(Guid Id, string Title)> albums,
        int page,
        int totalPages)
    {
        var keyboard = new TelegramKeyboard();
        foreach (var album in albums)
        {
            keyboard.AddRow(new TelegramInlineButton(Clip(album.Title, 40),
                TelegramCallbackData.Encode(AlbumPickAction, album.Id.ToString("N"))));
        }

        if (totalPages > 1)
        {
            keyboard.AddPaginationForAlbums(page, totalPages);
        }

        keyboard.AddRow(
            Button(TelegramBotTextIds.AlbumCreateNew, AlbumNewAction),
            Button(TelegramBotTextIds.AlbumSkip, AlbumSkipAction));
        return keyboard;
    }

    /// <summary>Paging row for the album picker. Only reachable directions are rendered.</summary>
    private void AddPaginationForAlbums(int page, int totalPages) =>
        AddPaginationFor(AlbumPageAction, null, page, totalPages);

    /// <summary>
    /// The library report screen: the two detailed reports plus a real refresh and home.
    /// </summary>
    public static TelegramKeyboard ReportsScreen()
    {
        var keyboard = new TelegramKeyboard();
        keyboard.AddRow(
            Button(TelegramBotTextIds.ReportsAlbumsButton, AlbumManageAction, "0"),
            Button(TelegramBotTextIds.ReportsArtistsButton, ArtistListAction, "0"));
        keyboard.AddRow(
            Button(TelegramBotTextIds.Refresh, ReportsAction),
            Button(TelegramBotTextIds.Home, HomeAction));
        return keyboard;
    }

    /// <summary>The album-management list, one button per album, newest first.</summary>
    public static TelegramKeyboard AlbumManageScreen(
        IReadOnlyList<AlbumSummary> albums, int page, int totalPages)
    {
        var keyboard = new TelegramKeyboard();
        foreach (var album in albums)
        {
            var label = Clip(album.Title, 44)
                + " (" + album.TrackCount.ToString(CultureInfo.InvariantCulture) + ")";
            keyboard.AddRow(new TelegramInlineButton(label,
                TelegramCallbackData.Encode(AlbumOpenAction, album.Id.ToString("N"))));
        }

        keyboard.AddPaginationFor(AlbumManageAction, null, page, totalPages);
        keyboard.AddRow(
            Button(TelegramBotTextIds.Refresh, AlbumManageAction, "0"),
            Button(TelegramBotTextIds.Home, HomeAction));
        return keyboard;
    }

    /// <summary>
    /// One album's tracks: every row removes exactly one track (the id behind the row is resolved
    /// from the message's own stored state), plus add, paging and navigation.
    /// </summary>
    public static TelegramKeyboard AlbumDetailScreen(
        Guid albumId, IReadOnlyList<AlbumTrackRow> tracks, int page, int totalPages)
    {
        var id = albumId.ToString("N");
        var keyboard = new TelegramKeyboard();
        for (var index = 0; index < tracks.Count; index++)
        {
            var track = tracks[index];
            var label = "❌ " + Clip(track.Title, 42);
            keyboard.AddRow(new TelegramInlineButton(label,
                TelegramCallbackData.Encode(
                    AlbumRemoveAction,
                    id + ":" + index.ToString(CultureInfo.InvariantCulture))));
        }

        keyboard.AddRow(Button(TelegramBotTextIds.AlbumAdd, AlbumAddListAction, id + ":0"));
        keyboard.AddPaginationFor(AlbumTrackPageAction, id, page, totalPages);
        keyboard.AddRow(
            Button(TelegramBotTextIds.Back, AlbumManageAction, "0"),
            Button(TelegramBotTextIds.Home, HomeAction));
        return keyboard;
    }

    /// <summary>The tracks that could be added to the album, one button each.</summary>
    public static TelegramKeyboard AlbumAddScreen(
        Guid albumId, IReadOnlyList<AlbumTrackRow> candidates, int page, int totalPages)
    {
        var id = albumId.ToString("N");
        var keyboard = new TelegramKeyboard();
        for (var index = 0; index < candidates.Count; index++)
        {
            var track = candidates[index];
            var label = "➕ " + Clip(track.Title, 30)
                + (string.IsNullOrWhiteSpace(track.Artist) ? string.Empty : " — " + Clip(track.Artist, 16));
            keyboard.AddRow(new TelegramInlineButton(label,
                TelegramCallbackData.Encode(
                    AlbumAddAction,
                    id + ":" + index.ToString(CultureInfo.InvariantCulture))));
        }

        keyboard.AddPaginationFor(AlbumAddListAction, id, page, totalPages);
        keyboard.AddRow(
            Button(TelegramBotTextIds.AlbumBackToAlbum, AlbumOpenAction, id),
            Button(TelegramBotTextIds.Home, HomeAction));
        return keyboard;
    }

    /// <summary>The two-step removal confirmation: a real confirm and a real way back.</summary>
    public static TelegramKeyboard AlbumRemoveConfirmScreen(Guid albumId)
    {
        var id = albumId.ToString("N");
        var keyboard = new TelegramKeyboard();
        keyboard.AddRow(
            Button(TelegramBotTextIds.AlbumConfirmRemoveYes, AlbumRemoveConfirmAction, id),
            Button(TelegramBotTextIds.AlbumBackToAlbum, AlbumOpenAction, id));
        return keyboard;
    }

    /// <summary>The artist report list, busiest artist first.</summary>
    public static TelegramKeyboard ArtistListScreen(
        IReadOnlyList<ArtistSummary> artists, int page, int totalPages)
    {
        var keyboard = new TelegramKeyboard();
        foreach (var artist in artists)
        {
            var label = Clip(artist.Name, 40)
                + " (" + artist.TrackCount.ToString(CultureInfo.InvariantCulture) + ")";
            keyboard.AddRow(new TelegramInlineButton(label,
                TelegramCallbackData.Encode(ArtistOpenAction, artist.Id.ToString("N"))));
        }

        keyboard.AddPaginationFor(ArtistListAction, null, page, totalPages);
        keyboard.AddRow(
            Button(TelegramBotTextIds.Refresh, ArtistListAction, "0"),
            Button(TelegramBotTextIds.Home, HomeAction));
        return keyboard;
    }

    /// <summary>One artist's report: the albums they appear on, then navigation.</summary>
    public static TelegramKeyboard ArtistDetailScreen(Guid artistId, IReadOnlyList<ArtistAlbumRow> albums)
    {
        var keyboard = new TelegramKeyboard();
        foreach (var album in albums.Take(8))
        {
            keyboard.AddRow(new TelegramInlineButton(Clip(album.Title, 44),
                TelegramCallbackData.Encode(AlbumOpenAction, album.Id.ToString("N"))));
        }

        keyboard.AddRow(
            Button(TelegramBotTextIds.Back, ArtistListAction, "0"),
            Button(TelegramBotTextIds.Home, HomeAction));
        return keyboard;
    }

    /// <summary>The one-screen menu. Every entry leads to a screen that exists.</summary>
    public static TelegramKeyboard MainMenu() => StatusScreen();

    /// <summary>A localised button with an optional encoded argument.</summary>
    private static TelegramInlineButton Button(string textId, string action, string? argument = null) =>
        new(TelegramBotTexts.Get(textId), TelegramCallbackData.Encode(action, argument));

    /// <summary>
    /// Paging row for <paramref name="action"/>. The page is either the whole argument or the last
    /// part after <paramref name="prefix"/>, which is how album screens keep their album id in every
    /// button. Only reachable directions are rendered.
    /// </summary>
    private void AddPaginationFor(string action, string? prefix, int page, int totalPages)
    {
        if (totalPages <= 1) return;

        var buttons = new List<TelegramInlineButton>(2);
        if (page > 0)
        {
            buttons.Add(new TelegramInlineButton(TelegramBotTexts.Get(TelegramBotTextIds.PreviousPage),
                TelegramCallbackData.Encode(action, PaginationArgument(prefix, page - 1))));
        }
        if (page < totalPages - 1)
        {
            buttons.Add(new TelegramInlineButton(TelegramBotTexts.Get(TelegramBotTextIds.NextPage),
                TelegramCallbackData.Encode(action, PaginationArgument(prefix, page + 1))));
        }
        if (buttons.Count > 0) AddRow(buttons.ToArray());
    }

    private static string PaginationArgument(string? prefix, int page)
    {
        var pageText = page.ToString(CultureInfo.InvariantCulture);
        return string.IsNullOrEmpty(prefix) ? pageText : prefix + ":" + pageText;
    }

    /// <summary>
    /// Shortens a label to fit a button, so a long title or artist name never pushes the keyboard
    /// past what Telegram renders. An empty label becomes a dash so no blank button exists.
    /// </summary>
    private static string Clip(string? text, int max)
    {
        var value = string.IsNullOrWhiteSpace(text) ? "—" : text.Trim();
        return value.Length <= max ? value : value[..(max - 1)] + "…";
    }

    public TelegramKeyboard AddRow(params TelegramInlineButton[] buttons)
    {
        if (buttons.Length == 0) return this;
        foreach (var button in buttons)
        {
            if (string.IsNullOrWhiteSpace(button.Text) || button.Text.Length > MaxButtonTextLength)
            {
                throw new ArgumentException(
                    $"Inline button text must be 1-{MaxButtonTextLength} characters.", nameof(buttons));
            }
            TelegramCallbackData.Validate(button.CallbackData);
        }
        _rows.Add(buttons.ToList());
        return this;
    }

    public TelegramKeyboard AddRow(string text, string callbackData) =>
        AddRow(new TelegramInlineButton(text, callbackData));

    /// <summary>Back and Home, the two navigation affordances every sub-screen carries.</summary>
    public TelegramKeyboard AddNavigation(string? backData = null, string? homeData = null) =>
        AddRow(
            new TelegramInlineButton(TelegramBotTexts.Get(TelegramBotTextIds.Back),
                backData ?? TelegramCallbackData.Encode(BackAction)),
            new TelegramInlineButton(TelegramBotTexts.Get(TelegramBotTextIds.Home),
                homeData ?? TelegramCallbackData.Encode(HomeAction)));

    /// <summary>
    /// Two-step confirmation. The confirm button carries the action id, the cancel button is the
    /// plain cancel action; the session generation, not just the id, decides whether it is still valid.
    /// </summary>
    public TelegramKeyboard AddConfirmCancel(string actionId, string? cancelData = null) =>
        AddRow(
            new TelegramInlineButton(TelegramBotTexts.Get(TelegramBotTextIds.Confirm),
                TelegramCallbackData.Encode(ConfirmAction, RequireActionId(actionId))),
            new TelegramInlineButton(TelegramBotTexts.Get(TelegramBotTextIds.CancelModal),
                cancelData ?? TelegramCallbackData.Encode(CancelAction)));

    /// <summary>
    /// Pagination row for <paramref name="screen"/>. Only reachable directions are rendered, so no
    /// button ever exists that does nothing. The page number belongs in the message text, not in a
    /// dead button, which is why there is no "current page" control.
    /// </summary>
    public TelegramKeyboard AddPagination(string screen, int page, int totalPages)
    {
        if (!screen.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c is '-' or '_'))
        {
            throw new ArgumentException("A pagination screen id must be lowercase ASCII.", nameof(screen));
        }
        if (totalPages < 1) throw new ArgumentOutOfRangeException(nameof(totalPages), "At least one page is required.");
        if (page < 0 || page >= totalPages)
        {
            throw new ArgumentOutOfRangeException(nameof(page), $"Page must be between 0 and {totalPages - 1}.");
        }

        var buttons = new List<TelegramInlineButton>(2);
        if (page > 0)
        {
            buttons.Add(new TelegramInlineButton(TelegramBotTexts.Get(TelegramBotTextIds.PreviousPage),
                TelegramCallbackData.Encode(PageAction, PageArgument(screen, page - 1))));
        }
        if (page < totalPages - 1)
        {
            buttons.Add(new TelegramInlineButton(TelegramBotTexts.Get(TelegramBotTextIds.NextPage),
                TelegramCallbackData.Encode(PageAction, PageArgument(screen, page + 1))));
        }

        if (buttons.Count > 0) AddRow(buttons.ToArray());
        return this;
    }

    public bool IsEmpty => _rows.Count == 0;

    /// <summary>The exact <c>reply_markup</c> object Telegram expects.</summary>
    public string ToJson()
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WritePropertyName("inline_keyboard");
            writer.WriteStartArray();
            foreach (var row in _rows)
            {
                writer.WriteStartArray();
                foreach (var button in row)
                {
                    writer.WriteStartObject();
                    writer.WriteString("text", button.Text);
                    writer.WriteString("callback_data", button.CallbackData);
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static string PageArgument(string screen, int page) =>
        $"{screen}:{page.ToString(CultureInfo.InvariantCulture)}";

    private static string RequireActionId(string actionId)
    {
        if (!actionId.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c is '-' or '_'))
        {
            throw new ArgumentException("A confirm action id must be lowercase ASCII.", nameof(actionId));
        }
        return actionId;
    }
}
