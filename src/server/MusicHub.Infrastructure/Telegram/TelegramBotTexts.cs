using System.Collections.ObjectModel;
using System.Globalization;
using MusicHub.Infrastructure.Library;

namespace MusicHub.Infrastructure.Telegram;

/// <summary>
/// Stable text ids. They are part of the bot contract: a test asserts every id exists in both
/// languages, so a screen cannot reference a string that was never translated.
/// </summary>
public static class TelegramBotTextIds
{
    public const string Denied = "denied";
    public const string Start = "start";
    public const string Help = "help";
    public const string UnknownCommand = "unknown_command";
    public const string Cancelled = "cancelled";
    public const string CancelledNone = "cancelled_none";
    public const string StatusTitle = "status_title";
    public const string StatusOffset = "status_offset";
    public const string StatusImports = "status_imports";
    public const string StatusLastResult = "status_last_result";
    public const string StatusPublications = "status_publications";
    public const string StatusHeartbeat = "status_heartbeat";
    public const string StatusNever = "status_never";
    public const string NoRecord = "no_record";
    public const string Refresh = "refresh";
    public const string Home = "home";
    public const string Back = "back";
    public const string Confirm = "confirm";
    public const string CancelModal = "cancel_modal";
    public const string PreviousPage = "previous_page";
    public const string NextPage = "next_page";
    public const string Refreshed = "refreshed";
    public const string AlreadyCurrent = "already_current";
    public const string SessionExpired = "session_expired";
    public const string UnknownAction = "unknown_action";
    public const string Failure = "failure";
    public const string StorageTitle = "storage_title";
    public const string StorageTitleShort = "storage_title_short";
    public const string StorageCurrent = "storage_current";
    public const string StorageSource = "storage_source";
    public const string StorageAssets = "storage_assets";
    public const string StorageUsage = "storage_usage";
    public const string StorageUsageUnknown = "storage_usage_unknown";
    public const string StorageHowTo = "storage_how_to";
    public const string StorageUsageHeading = "storage_usage_heading";
    public const string StoragePointerWarning = "storage_pointer_warning";
    public const string StorageMoveRunning = "storage_move_running";
    public const string StorageMoveStatus = "storage_move_status";
    public const string StorageMoveCompleted = "storage_move_completed";
    public const string StorageMoveFailed = "storage_move_failed";
    public const string StoragePathRequired = "storage_path_required";
    public const string StoragePathRejected = "storage_path_rejected";
    public const string StorageQueued = "storage_queued";
    public const string StorageDefaultSource = "storage_default_source";
    public const string StorageRelocatedSource = "storage_relocated_source";
    public const string StorageDataRoot = "storage_data_root";
    public const string StorageKeysNote = "storage_keys_note";
    public const string StorageInvalidPathChars = "storage_invalid_path_chars";
    public const string AlbumPrompt = "album_prompt";
    public const string AlbumPromptHint = "album_prompt_hint";
    public const string AlbumChooseExisting = "album_choose_existing";
    public const string AlbumCreateNew = "album_create_new";
    public const string AlbumSkip = "album_skip";
    public const string AlbumAskNewName = "album_ask_new_name";
    public const string AlbumNameTooLong = "album_name_too_long";
    public const string AlbumAssigned = "album_assigned";
    public const string AlbumSkipped = "album_skipped";
    public const string AlbumCreated = "album_created";
    public const string AlbumFailed = "album_failed";
    public const string AlbumNoneYet = "album_none_yet";
    public const string AlbumPickerTitle = "album_picker_title";
    public const string AlbumPickerEmpty = "album_picker_empty";
    public const string AlbumTrackGone = "album_track_gone";
    public const string Done = "done";
    public const string MenuReports = "menu_reports";
    public const string MenuAlbums = "menu_albums";
    public const string MenuArtists = "menu_artists";
    public const string ReportsTitle = "reports_title";
    public const string ReportsAlbums = "reports_albums";
    public const string ReportsTracks = "reports_tracks";
    public const string ReportsArtists = "reports_artists";
    public const string ReportsUsers = "reports_users";
    public const string ReportsUsersActive = "reports_users_active";
    public const string ReportsUsersInactive = "reports_users_inactive";
    public const string ReportsDuration = "reports_duration";
    public const string ReportsAlbumsButton = "reports_albums_button";
    public const string ReportsArtistsButton = "reports_artists_button";
    public const string ReportsHint = "reports_hint";
    public const string ReportsEmpty = "reports_empty";
    public const string PageInfo = "page_info";
    public const string AlbumManageTitle = "album_manage_title";
    public const string AlbumManageEmpty = "album_manage_empty";
    public const string AlbumManageHint = "album_manage_hint";
    public const string AlbumDetailTitle = "album_detail_title";
    public const string AlbumDetailArtist = "album_detail_artist";
    public const string AlbumDetailYear = "album_detail_year";
    public const string AlbumDetailTracks = "album_detail_tracks";
    public const string AlbumDetailDuration = "album_detail_duration";
    public const string AlbumDetailArtistsHeading = "album_detail_artists_heading";
    public const string AlbumDetailNoTracks = "album_detail_no_tracks";
    public const string AlbumDetailHint = "album_detail_hint";
    public const string AlbumAdd = "album_add";
    public const string AlbumAddTitle = "album_add_title";
    public const string AlbumAddEmpty = "album_add_empty";
    public const string AlbumAddHint = "album_add_hint";
    public const string AlbumRemoved = "album_removed";
    public const string AlbumAdded = "album_added";
    public const string AlbumConfirmRemove = "album_confirm_remove";
    public const string AlbumConfirmRemoveHint = "album_confirm_remove_hint";
    public const string AlbumConfirmRemoveYes = "album_confirm_remove_yes";
    public const string AlbumBackToAlbum = "album_back_to_album";
    public const string AlbumGone = "album_gone";
    public const string AlbumTrackNotInAlbum = "album_track_not_in_album";
    public const string ArtistReportTitle = "artist_report_title";
    public const string ArtistReportHint = "artist_report_hint";
    public const string ArtistReportEmpty = "artist_report_empty";
    public const string ArtistDetailTitle = "artist_detail_title";
    public const string ArtistDetailTracks = "artist_detail_tracks";
    public const string ArtistDetailAlbums = "artist_detail_albums";
    public const string ArtistDetailDuration = "artist_detail_duration";
    public const string ArtistAlbumsHeading = "artist_albums_heading";
    public const string ArtistNoAlbums = "artist_no_albums";
    public const string ArtistTrackUnit = "artist_track_unit";
    public const string ArtistGone = "artist_gone";
}

/// <summary>
/// Bot strings in Persian and English, resolved by id.
/// </summary>
/// <remarks>
/// <para>
/// Persian is the default because the operator of this installation is Persian-speaking. There is
/// no per-user language preference yet: a stored preference that no screen reads would be a dead
/// control, so Phase 0 resolves everything to <see cref="DefaultLanguage"/> and
/// <see cref="Get"/> accepts a language argument for the Phase 2 wiring.
/// </para>
/// <para>
/// Every Persian string is written in real Persian orthography with ZWNJ where the language
/// requires it, and no string carries an explicit directional override. Labels therefore render
/// correctly under an RTL base direction without any invisible bidi markers.
/// </para>
/// </remarks>
public static class TelegramBotTexts
{
    public const string PersianLanguage = "fa";
    public const string EnglishLanguage = "en";
    public const string DefaultLanguage = PersianLanguage;

    private static readonly ReadOnlyDictionary<string, string> Persian = new(new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [TelegramBotTextIds.Denied] = "این ربات خصوصی است و برای شما فعال نیست.",
        [TelegramBotTextIds.Start] = "پنل مدیریت سامانه",
        [TelegramBotTextIds.Help] =
            "دستورهای موجود:\n" +
            "/start — نمایش پنل\n" +
            "/status — وضعیت فعلی\n" +
            "/reports — گزارش کتابخانه\n" +
            "/albums — مدیریت آلبوم‌ها\n" +
            "/artists — گزارش آرتیست‌ها\n" +
            "/help — همین راهنما\n" +
            "/cancel — پاک کردن وضعیت ذخیره‌شده",
        [TelegramBotTextIds.UnknownCommand] = "این دستور شناخته‌شده نیست.",
        [TelegramBotTextIds.Cancelled] = "وضعیت ذخیره‌شده پاک شد.",
        [TelegramBotTextIds.CancelledNone] = "هیچ وضعیت ذخیره‌شده‌ای برای لغو وجود ندارد.",
        [TelegramBotTextIds.StatusTitle] = "وضعیت ربات",
        [TelegramBotTextIds.StatusOffset] = "آفست به‌روزرسانی",
        [TelegramBotTextIds.StatusImports] = "وارد‌شده‌ها",
        [TelegramBotTextIds.StatusLastResult] = "آخرین نتیجه",
        [TelegramBotTextIds.StatusPublications] = "انتشارهای منتشرنشده",
        [TelegramBotTextIds.StatusHeartbeat] = "آخرین ضربان سرویس",
        [TelegramBotTextIds.StatusNever] = "هرگز",
        [TelegramBotTextIds.NoRecord] = "بدون سابقه",
        [TelegramBotTextIds.Refresh] = "به‌روزرسانی",
        [TelegramBotTextIds.Home] = "خانه",
        [TelegramBotTextIds.Back] = "بازگشت",
        [TelegramBotTextIds.Confirm] = "تأیید",
        [TelegramBotTextIds.CancelModal] = "انصراف",
        [TelegramBotTextIds.PreviousPage] = "قبلی",
        [TelegramBotTextIds.NextPage] = "بعدی",
        [TelegramBotTextIds.Refreshed] = "به‌روزرسانی شد.",
        [TelegramBotTextIds.AlreadyCurrent] = "پیام از قبل به‌روز است.",
        [TelegramBotTextIds.SessionExpired] = "نشست قبلی منقضی شده بود و صفحه بازنویسی شد.",
        [TelegramBotTextIds.UnknownAction] = "این دکمه دیگر معتبر نیست.",
        [TelegramBotTextIds.Failure] = "اجرای درخواست با خطا روبه‌رو شد.",
        [TelegramBotTextIds.StorageTitle] = "محل نگهداری کتابخانه",
        [TelegramBotTextIds.StorageTitleShort] = "محل کتابخانه",
        [TelegramBotTextIds.StorageCurrent] = "مسیر فعلی",
        [TelegramBotTextIds.StorageSource] = "منبع تنظیم",
        [TelegramBotTextIds.StorageAssets] = "فایل‌های ذخیره‌شده",
        [TelegramBotTextIds.StorageUsage] = "حجم کتابخانه",
        [TelegramBotTextIds.StorageUsageUnknown] = "قابل اندازه‌گیری نیست",
        [TelegramBotTextIds.StorageHowTo] =
            "برای جابه‌جایی، مسیر کامل پوشهٔ خالی را در یک پیام بفرستید:\n" +
            "/storage D:\\MusicHub\\library\n" +
            "ابتدا مسیر بررسی می‌شود و تنها در صورت تأیید، دکمهٔ شروع جابه‌جایی نمایش داده می‌شود.",
        [TelegramBotTextIds.StorageUsageHeading] = "پوشه‌ها",
        [TelegramBotTextIds.StoragePointerWarning] =
            "هشدار: پروندهٔ مسیر کتابخانه خوانا نیست و فعلاً از پوشهٔ داده استفاده می‌شود.",
        [TelegramBotTextIds.StorageMoveRunning] = "یک جابه‌جایی در حال اجراست.",
        [TelegramBotTextIds.StorageMoveStatus] = "وضعیت جابه‌جایی",
        [TelegramBotTextIds.StorageMoveCompleted] =
            "جابه‌جایی کامل شد و نسخهٔ قبلی حذف نشده است. پس از اطمینان از پخش، نسخهٔ قدیمی را خودتان پاک کنید.",
        [TelegramBotTextIds.StorageMoveFailed] = "جابه‌جایی انجام نشد",
        [TelegramBotTextIds.StoragePathRequired] = "هیچ مسیری ارسال نشد. مسیر کامل پوشه را بنویسید.",
        [TelegramBotTextIds.StoragePathRejected] = "این مسیر پذیرفته نشد",
        [TelegramBotTextIds.StorageQueued] =
            "جابه‌جایی در صف قرار گرفت. سرویس به‌زودی کپی و سپس بررسی کامل فایل‌ها را آغاز می‌کند. تا پایان آن فایلی بارگذاری نکنید.",
        [TelegramBotTextIds.StorageDefaultSource] = "پیش‌فرض (پوشهٔ داده)",
        [TelegramBotTextIds.StorageRelocatedSource] = "تنظیم‌شده توسط مدیر",
        [TelegramBotTextIds.StorageDataRoot] = "پوشهٔ داده (ثابت)",
        [TelegramBotTextIds.StorageKeysNote] =
            "پوشهٔ داده شامل کلیدها و اعتبارنامهٔ پایگاه‌داده است و از این صفحه تغییر نمی‌کند.",
        [TelegramBotTextIds.StorageInvalidPathChars] = "مسیر شامل نویسه‌های غیرمجاز است.",
        [TelegramBotTextIds.AlbumPrompt] = "این فایل در کتابخانه ثبت شد: «{0}»",
        [TelegramBotTextIds.AlbumPromptHint] = "این آهنگ در کدام آلبوم قرار بگیرد؟",
        [TelegramBotTextIds.AlbumChooseExisting] = "انتخاب از آلبوم‌های موجود",
        [TelegramBotTextIds.AlbumCreateNew] = "ساخت آلبوم تازه",
        [TelegramBotTextIds.AlbumSkip] = "بدون آلبوم",
        [TelegramBotTextIds.AlbumAskNewName] = "نام آلبوم تازه را در یک پیام بفرستید.",
        [TelegramBotTextIds.AlbumNameTooLong] = "نام آلبوم بیش از حد بلند است.",
        [TelegramBotTextIds.AlbumAssigned] = "«{0}» در آلبوم «{1}» قرار گرفت.",
        [TelegramBotTextIds.AlbumSkipped] = "«{0}» بدون آلبوم ثبت شد.",
        [TelegramBotTextIds.AlbumCreated] = "آلبوم «{0}» ساخته شد.",
        [TelegramBotTextIds.AlbumFailed] = "انجام نشد: {0}",
        [TelegramBotTextIds.AlbumNoneYet] = "هنوز آلبومی ساخته نشده است.",
        [TelegramBotTextIds.AlbumPickerTitle] = "آلبوم «{0}» را انتخاب کنید",
        [TelegramBotTextIds.AlbumPickerEmpty] = "آلبومی برای انتخاب وجود ندارد. با دکمهٔ «ساخت آلبوم تازه» یکی بسازید.",
        [TelegramBotTextIds.AlbumTrackGone] = "این آهنگ دیگر در کتابخانه نیست.",
        [TelegramBotTextIds.Done] = "انجام شد.",
        [TelegramBotTextIds.MenuReports] = "📊 گزارش‌ها",
        [TelegramBotTextIds.MenuAlbums] = "🎵 آلبوم‌ها",
        [TelegramBotTextIds.MenuArtists] = "🎤 آرتیست‌ها",
        [TelegramBotTextIds.ReportsTitle] = "📊 گزارش کلی کتابخانه",
        [TelegramBotTextIds.ReportsAlbums] = "آلبوم‌ها",
        [TelegramBotTextIds.ReportsTracks] = "آهنگ‌ها",
        [TelegramBotTextIds.ReportsArtists] = "آرتیست‌ها",
        [TelegramBotTextIds.ReportsUsers] = "کاربران",
        [TelegramBotTextIds.ReportsUsersActive] = "کاربران فعال",
        [TelegramBotTextIds.ReportsUsersInactive] = "کاربران غیرفعال",
        [TelegramBotTextIds.ReportsDuration] = "مدت کل موسیقی",
        [TelegramBotTextIds.ReportsAlbumsButton] = "گزارش آلبوم‌ها",
        [TelegramBotTextIds.ReportsArtistsButton] = "گزارش آرتیست‌ها",
        [TelegramBotTextIds.ReportsHint] = "برای دیدن گزارش تخصصی، یکی از دو دکمهٔ زیر را بزنید.",
        [TelegramBotTextIds.ReportsEmpty] = "هنوز آهنگ یا آلبومی در کتابخانه نیست.",
        [TelegramBotTextIds.PageInfo] = "صفحهٔ {0} از {1}",
        [TelegramBotTextIds.AlbumManageTitle] = "🎵 مدیریت آلبوم‌ها",
        [TelegramBotTextIds.AlbumManageEmpty] = "آلبومی برای مدیریت وجود ندارد.",
        [TelegramBotTextIds.AlbumManageHint] =
            "یک آلبوم را انتخاب کنید تا قطعه‌هایش را ببینید، حذف کنید یا قطعهٔ تازه‌ای بیفزایید.",
        [TelegramBotTextIds.AlbumDetailTitle] = "🎵 آلبوم «{0}»",
        [TelegramBotTextIds.AlbumDetailArtist] = "آرتیست آلبوم",
        [TelegramBotTextIds.AlbumDetailYear] = "سال",
        [TelegramBotTextIds.AlbumDetailTracks] = "قطعه‌ها",
        [TelegramBotTextIds.AlbumDetailDuration] = "مدت",
        [TelegramBotTextIds.AlbumDetailArtistsHeading] = "سهم آرتیست‌ها",
        [TelegramBotTextIds.AlbumDetailNoTracks] = "این آلبوم هنوز قطعه‌ای ندارد.",
        [TelegramBotTextIds.AlbumDetailHint] =
            "برای حذف یک قطعه، دکمهٔ آن را بزنید. فایل صوتی پاک نمی‌شود و فقط عضویت آلبوم برداشته می‌شود.",
        [TelegramBotTextIds.AlbumAdd] = "افزودن قطعه",
        [TelegramBotTextIds.AlbumAddTitle] = "افزودن قطعه به «{0}»",
        [TelegramBotTextIds.AlbumAddEmpty] = "قطعهٔ تازه‌ای برای افزودن نیست.",
        [TelegramBotTextIds.AlbumAddHint] = "برای افزودن، دکمهٔ قطعه را بزنید.",
        [TelegramBotTextIds.AlbumRemoved] = "«{0}» از آلبوم «{1}» حذف شد.",
        [TelegramBotTextIds.AlbumAdded] = "«{0}» به آلبوم «{1}» افزوده شد.",
        [TelegramBotTextIds.AlbumConfirmRemove] = "«{0}» از آلبوم «{1}» حذف شود؟",
        [TelegramBotTextIds.AlbumConfirmRemoveHint] =
            "فقط عضویت آلبوم برداشته می‌شود؛ فایل صوتی و خود آهنگ در کتابخانه می‌ماند.",
        [TelegramBotTextIds.AlbumConfirmRemoveYes] = "تأیید حذف",
        [TelegramBotTextIds.AlbumBackToAlbum] = "بازگشت به آلبوم",
        [TelegramBotTextIds.AlbumGone] = "این آلبوم دیگر وجود ندارد.",
        [TelegramBotTextIds.AlbumTrackNotInAlbum] = "این قطعه دیگر در این آلبوم نیست.",
        [TelegramBotTextIds.ArtistReportTitle] = "🎤 گزارش آرتیست‌ها",
        [TelegramBotTextIds.ArtistReportHint] = "یک آرتیست را انتخاب کنید تا گزارش تخصصی‌اش را ببینید.",
        [TelegramBotTextIds.ArtistReportEmpty] = "آرتیستی با آهنگ ثبت‌شده وجود ندارد.",
        [TelegramBotTextIds.ArtistDetailTitle] = "🎤 آرتیست «{0}»",
        [TelegramBotTextIds.ArtistDetailTracks] = "قطعه‌ها",
        [TelegramBotTextIds.ArtistDetailAlbums] = "آلبوم‌ها",
        [TelegramBotTextIds.ArtistDetailDuration] = "مدت",
        [TelegramBotTextIds.ArtistAlbumsHeading] = "آلبوم‌های این آرتیست",
        [TelegramBotTextIds.ArtistNoAlbums] = "این آرتیست در هیچ آلبومی نیست.",
        [TelegramBotTextIds.ArtistTrackUnit] = "{0} قطعه",
        [TelegramBotTextIds.ArtistGone] = "این آرتیست دیگر وجود ندارد."
    });

    private static readonly ReadOnlyDictionary<string, string> English = new(new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [TelegramBotTextIds.Denied] = "This bot is private and is not enabled for you.",
        [TelegramBotTextIds.Start] = "System administration panel",
        [TelegramBotTextIds.Help] =
            "Available commands:\n" +
            "/start — open the panel\n" +
            "/status — current status\n" +
            "/reports — library report\n" +
            "/albums — album management\n" +
            "/artists — artist report\n" +
            "/help — this help\n" +
            "/cancel — clear the stored state",
        [TelegramBotTextIds.UnknownCommand] = "That command is not recognised.",
        [TelegramBotTextIds.Cancelled] = "The stored state was cleared.",
        [TelegramBotTextIds.CancelledNone] = "There is no stored state to clear.",
        [TelegramBotTextIds.StatusTitle] = "Bot status",
        [TelegramBotTextIds.StatusOffset] = "Update offset",
        [TelegramBotTextIds.StatusImports] = "Imports",
        [TelegramBotTextIds.StatusLastResult] = "last result",
        [TelegramBotTextIds.StatusPublications] = "Unpublished publications",
        [TelegramBotTextIds.StatusHeartbeat] = "Service heartbeat",
        [TelegramBotTextIds.StatusNever] = "never",
        [TelegramBotTextIds.NoRecord] = "no record",
        [TelegramBotTextIds.Refresh] = "Refresh",
        [TelegramBotTextIds.Home] = "Home",
        [TelegramBotTextIds.Back] = "Back",
        [TelegramBotTextIds.Confirm] = "Confirm",
        [TelegramBotTextIds.CancelModal] = "Cancel",
        [TelegramBotTextIds.PreviousPage] = "Prev",
        [TelegramBotTextIds.NextPage] = "Next",
        [TelegramBotTextIds.Refreshed] = "Refreshed.",
        [TelegramBotTextIds.AlreadyCurrent] = "The message is already up to date.",
        [TelegramBotTextIds.SessionExpired] = "The previous session had expired; the screen was rebuilt.",
        [TelegramBotTextIds.UnknownAction] = "That button is no longer valid.",
        [TelegramBotTextIds.Failure] = "The request could not be completed.",
        [TelegramBotTextIds.StorageTitle] = "Library storage location",
        [TelegramBotTextIds.StorageTitleShort] = "Storage",
        [TelegramBotTextIds.StorageCurrent] = "Current path",
        [TelegramBotTextIds.StorageSource] = "Set by",
        [TelegramBotTextIds.StorageAssets] = "Stored files",
        [TelegramBotTextIds.StorageUsage] = "Library size",
        [TelegramBotTextIds.StorageUsageUnknown] = "could not be measured",
        [TelegramBotTextIds.StorageHowTo] =
            "To move the library, send the full path of an empty folder in one message:\n" +
            "/storage D:\\MusicHub\\library\n" +
            "The path is checked first, and the start button only appears once it passes.",
        [TelegramBotTextIds.StorageUsageHeading] = "Folders",
        [TelegramBotTextIds.StoragePointerWarning] =
            "Warning: the library location pointer is unreadable, so the data folder is in use.",
        [TelegramBotTextIds.StorageMoveRunning] = "A move is already running.",
        [TelegramBotTextIds.StorageMoveStatus] = "Move status",
        [TelegramBotTextIds.StorageMoveCompleted] =
            "The move completed and the previous copy was kept. Remove the old copy yourself once playback is verified.",
        [TelegramBotTextIds.StorageMoveFailed] = "The move did not finish",
        [TelegramBotTextIds.StoragePathRequired] = "No path was given. Send the full path of the folder.",
        [TelegramBotTextIds.StoragePathRejected] = "That folder was refused",
        [TelegramBotTextIds.StorageQueued] =
            "The move is queued. The service starts copying and then verifying every file shortly. Do not upload until it finishes.",
        [TelegramBotTextIds.StorageDefaultSource] = "Default (the data folder)",
        [TelegramBotTextIds.StorageRelocatedSource] = "Set by an administrator",
        [TelegramBotTextIds.StorageDataRoot] = "Data folder (fixed)",
        [TelegramBotTextIds.StorageKeysNote] =
            "The data folder holds the keys and the database credential and is not changed from this screen.",
        [TelegramBotTextIds.StorageInvalidPathChars] = "The path contains characters that are not allowed.",
        [TelegramBotTextIds.AlbumPrompt] = "This file was added to the library: \"{0}\"",
        [TelegramBotTextIds.AlbumPromptHint] = "Which album should this track be filed under?",
        [TelegramBotTextIds.AlbumChooseExisting] = "Choose an existing album",
        [TelegramBotTextIds.AlbumCreateNew] = "Create a new album",
        [TelegramBotTextIds.AlbumSkip] = "No album",
        [TelegramBotTextIds.AlbumAskNewName] = "Send the name of the new album in one message.",
        [TelegramBotTextIds.AlbumNameTooLong] = "That album name is too long.",
        [TelegramBotTextIds.AlbumAssigned] = "\"{0}\" was filed under \"{1}\".",
        [TelegramBotTextIds.AlbumSkipped] = "\"{0}\" was added without an album.",
        [TelegramBotTextIds.AlbumCreated] = "Album \"{0}\" was created.",
        [TelegramBotTextIds.AlbumFailed] = "Not done: {0}",
        [TelegramBotTextIds.AlbumNoneYet] = "No album has been created yet.",
        [TelegramBotTextIds.AlbumPickerTitle] = "Choose an album for \"{0}\"",
        [TelegramBotTextIds.AlbumPickerEmpty] = "There is no album to choose from. Create one with the \"Create a new album\" button.",
        [TelegramBotTextIds.AlbumTrackGone] = "That track is no longer in the library.",
        [TelegramBotTextIds.Done] = "Done.",
        [TelegramBotTextIds.MenuReports] = "📊 Reports",
        [TelegramBotTextIds.MenuAlbums] = "🎵 Albums",
        [TelegramBotTextIds.MenuArtists] = "🎤 Artists",
        [TelegramBotTextIds.ReportsTitle] = "📊 Library overview",
        [TelegramBotTextIds.ReportsAlbums] = "Albums",
        [TelegramBotTextIds.ReportsTracks] = "Tracks",
        [TelegramBotTextIds.ReportsArtists] = "Artists",
        [TelegramBotTextIds.ReportsUsers] = "Users",
        [TelegramBotTextIds.ReportsUsersActive] = "Active users",
        [TelegramBotTextIds.ReportsUsersInactive] = "Inactive users",
        [TelegramBotTextIds.ReportsDuration] = "Total music time",
        [TelegramBotTextIds.ReportsAlbumsButton] = "Album report",
        [TelegramBotTextIds.ReportsArtistsButton] = "Artist report",
        [TelegramBotTextIds.ReportsHint] = "Pick one of the two detailed reports below.",
        [TelegramBotTextIds.ReportsEmpty] = "There is no track or album in the library yet.",
        [TelegramBotTextIds.PageInfo] = "Page {0} of {1}",
        [TelegramBotTextIds.AlbumManageTitle] = "🎵 Album management",
        [TelegramBotTextIds.AlbumManageEmpty] = "There is no album to manage.",
        [TelegramBotTextIds.AlbumManageHint] =
            "Pick an album to see its tracks, remove one, or add a track that is not in it yet.",
        [TelegramBotTextIds.AlbumDetailTitle] = "🎵 Album \"{0}\"",
        [TelegramBotTextIds.AlbumDetailArtist] = "Album artist",
        [TelegramBotTextIds.AlbumDetailYear] = "Year",
        [TelegramBotTextIds.AlbumDetailTracks] = "Tracks",
        [TelegramBotTextIds.AlbumDetailDuration] = "Duration",
        [TelegramBotTextIds.AlbumDetailArtistsHeading] = "Artist share",
        [TelegramBotTextIds.AlbumDetailNoTracks] = "This album has no track yet.",
        [TelegramBotTextIds.AlbumDetailHint] =
            "Tap a track to remove it. The audio file is not deleted; only its album membership is dropped.",
        [TelegramBotTextIds.AlbumAdd] = "Add track",
        [TelegramBotTextIds.AlbumAddTitle] = "Add a track to \"{0}\"",
        [TelegramBotTextIds.AlbumAddEmpty] = "Every track is already in this album.",
        [TelegramBotTextIds.AlbumAddHint] = "Tap a track to add it.",
        [TelegramBotTextIds.AlbumRemoved] = "\"{0}\" was removed from \"{1}\".",
        [TelegramBotTextIds.AlbumAdded] = "\"{0}\" was added to \"{1}\".",
        [TelegramBotTextIds.AlbumConfirmRemove] = "Remove \"{0}\" from \"{1}\"?",
        [TelegramBotTextIds.AlbumConfirmRemoveHint] =
            "Only the album membership is dropped; the audio file and the track stay in the library.",
        [TelegramBotTextIds.AlbumConfirmRemoveYes] = "Remove it",
        [TelegramBotTextIds.AlbumBackToAlbum] = "Back to album",
        [TelegramBotTextIds.AlbumGone] = "That album no longer exists.",
        [TelegramBotTextIds.AlbumTrackNotInAlbum] = "That track is no longer in this album.",
        [TelegramBotTextIds.ArtistReportTitle] = "🎤 Artist report",
        [TelegramBotTextIds.ArtistReportHint] = "Pick an artist to see their detailed report.",
        [TelegramBotTextIds.ArtistReportEmpty] = "There is no artist with a track yet.",
        [TelegramBotTextIds.ArtistDetailTitle] = "🎤 Artist \"{0}\"",
        [TelegramBotTextIds.ArtistDetailTracks] = "Tracks",
        [TelegramBotTextIds.ArtistDetailAlbums] = "Albums",
        [TelegramBotTextIds.ArtistDetailDuration] = "Duration",
        [TelegramBotTextIds.ArtistAlbumsHeading] = "Albums of this artist",
        [TelegramBotTextIds.ArtistNoAlbums] = "This artist is not in any album.",
        [TelegramBotTextIds.ArtistTrackUnit] = "{0} tracks",
        [TelegramBotTextIds.ArtistGone] = "That artist no longer exists."
    });

    /// <summary>Every id, so a test can prove both dictionaries are complete.</summary>
    public static IReadOnlyCollection<string> Ids { get; } =
        Persian.Keys.Union(English.Keys, StringComparer.Ordinal).ToList().AsReadOnly();

    public static IReadOnlyCollection<string> Languages { get; } =
        new[] { PersianLanguage, EnglishLanguage }.ToList().AsReadOnly();

    /// <summary>
    /// Resolves a text id. Persian is the default; an unknown language falls back to Persian and an
    /// unknown id throws rather than shipping a raw identifier to a user.
    /// </summary>
    public static string Get(string id, string? language = null)
    {
        var dictionary = Normalize(language) == EnglishLanguage ? English : Persian;
        return dictionary.TryGetValue(id, out var value)
            ? value
            : throw new KeyNotFoundException($"The Telegram bot text id \"{id}\" is not defined.");
    }

    /// <summary>
    /// The one denial text. It never varies with configuration, so a refused sender cannot learn
    /// whether an administrator chat id is configured.
    /// </summary>
    public static string Denial => Get(TelegramBotTextIds.Denied);

    /// <summary>Renders the status screen from values read out of the database.</summary>
    public static string FormatStatus(TelegramBotStatus status, string? language = null)
    {
        var lines = new[]
        {
            Get(TelegramBotTextIds.StatusTitle, language),
            Label(TelegramBotTextIds.StatusOffset, language) + ": "
                + status.UpdateOffset.ToString(CultureInfo.InvariantCulture),
            Label(TelegramBotTextIds.StatusImports, language) + ": "
                + status.ImportCount.ToString(CultureInfo.InvariantCulture)
                + " — " + Label(TelegramBotTextIds.StatusLastResult, language) + ": "
                + (status.LastImportResult ?? Get(TelegramBotTextIds.NoRecord, language)),
            Label(TelegramBotTextIds.StatusPublications, language) + ": "
                + status.UnpublishedPublications.ToString(CultureInfo.InvariantCulture),
            Label(TelegramBotTextIds.StatusHeartbeat, language) + ": "
                + (status.WorkerHeartbeat ?? Get(TelegramBotTextIds.StatusNever, language))
        };
        return string.Join("\n", lines);
    }

    /// <summary>Help text plus the reason, used for unknown commands.</summary>
    public static string UnknownCommandText(string? language = null) =>
        Get(TelegramBotTextIds.UnknownCommand, language) + "\n\n" + Get(TelegramBotTextIds.Help, language);

    /// <summary>
    /// Renders the storage screen from values read out of the server, including the safety verdict
    /// when a path has just been checked.
    /// </summary>
    public static string FormatStorage(TelegramStorageView view, string? language = null)
    {
        var lines = new List<string>
        {
            Get(TelegramBotTextIds.StorageTitle, language),
            Label(TelegramBotTextIds.StorageCurrent, language) + ": " + view.LibraryPath,
            Label(TelegramBotTextIds.StorageSource, language) + ": "
                + Get(view.Relocated ? TelegramBotTextIds.StorageRelocatedSource
                    : TelegramBotTextIds.StorageDefaultSource, language),
            Label(TelegramBotTextIds.StorageAssets, language) + ": "
                + view.AssetCount.ToString(CultureInfo.InvariantCulture)
                + " — " + Label(TelegramBotTextIds.StorageUsage, language) + ": " + FormatBytes(view.AssetBytes),
            Label(TelegramBotTextIds.StorageDataRoot, language) + ": " + view.DataRoot
        };

        if (view.Areas.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add(Get(TelegramBotTextIds.StorageUsageHeading, language) + ":");
            foreach (var area in view.Areas)
            {
                lines.Add("• " + area.Name + ": " + area.Summary);
            }
        }

        if (!string.IsNullOrWhiteSpace(view.PointerError))
        {
            lines.Add(string.Empty);
            lines.Add(Get(TelegramBotTextIds.StoragePointerWarning, language));
        }

        if (view.MoveActive)
        {
            lines.Add(string.Empty);
            lines.Add(Get(TelegramBotTextIds.StorageMoveRunning, language));
        }

        if (!string.IsNullOrWhiteSpace(view.MoveSummary))
        {
            lines.Add(string.Empty);
            lines.Add(Label(TelegramBotTextIds.StorageMoveStatus, language) + ": " + view.MoveSummary);
        }

        if (view.CandidatePath is not null)
        {
            lines.Add(string.Empty);
            if (view.CandidateAccepted)
            {
                lines.Add(Get(TelegramBotTextIds.StorageQueued, language) + "\n" + view.CandidatePath);
            }
            else
            {
                lines.Add(Get(TelegramBotTextIds.StoragePathRejected, language) + ": " + view.CandidatePath);
                if (!string.IsNullOrWhiteSpace(view.CandidateError))
                {
                    lines.Add("• " + view.CandidateError);
                }
            }
        }

        lines.Add(string.Empty);
        lines.Add(Get(TelegramBotTextIds.StorageHowTo, language));
        lines.Add(Get(TelegramBotTextIds.StorageKeysNote, language));
        return string.Join("\n", lines);
    }

    /// <summary>Human-readable size, matching the dashboard's units.</summary>
    public static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return unit == 0
            ? $"{bytes.ToString(CultureInfo.InvariantCulture)} B"
            : value.ToString("0.#", CultureInfo.InvariantCulture) + " " + units[unit];
    }

    /// <summary>
    /// The per-file album question. It names the file that was just imported so an operator who
    /// sent several files can tell the prompts apart.
    /// </summary>
    public static string FormatAlbumPrompt(string fileName, string? language = null) =>
        Format(TelegramBotTextIds.AlbumPrompt, language, fileName) + "\n"
        + Get(TelegramBotTextIds.AlbumPromptHint, language);

    /// <summary>The grouped album question for media groups (multiple audio files sent together).</summary>
    public static string FormatAlbumPromptMulti(int count, string fileName, string? language = null)
    {
        var isFa = Normalize(language) == "fa";
        var header = isFa
            ? $"{count} قطعه صوتی (پیام گروهی) در کتابخانه ثبت شد."
            : $"{count} audio tracks (grouped message) were added to the library.";
        return header + "\n" + Get(TelegramBotTextIds.AlbumPromptHint, language);
    }

    /// <summary>The album picker header. Names the track so several open pickers stay distinguishable.</summary>
    public static string FormatAlbumPicker(string trackTitle, int albumCount, string? language = null)
    {
        var header = Format(TelegramBotTextIds.AlbumPickerTitle, language, trackTitle);
        if (albumCount > 0) return header;

        // Nothing to choose from: say so, and still offer "create a new album" via the keyboard.
        return header + "\n\n"
            + Get(TelegramBotTextIds.AlbumNoneYet, language) + " "
            + Get(TelegramBotTextIds.AlbumPickerEmpty, language);
    }

    public static string FormatAlbumAssigned(string trackTitle, string albumTitle, string? language = null) =>
        Format(TelegramBotTextIds.AlbumAssigned, language, trackTitle, albumTitle);

    public static string FormatAlbumAssignedMulti(int count, string albumTitle, string? language = null)
    {
        var isFa = Normalize(language) == "fa";
        return isFa
            ? $"{count} قطعه در آلبوم «{albumTitle}» قرار گرفتند."
            : $"{count} tracks were filed under \"{albumTitle}\".";
    }

    public static string FormatAlbumSkipped(string trackTitle, string? language = null) =>
        Format(TelegramBotTextIds.AlbumSkipped, language, trackTitle);

    public static string FormatAlbumSkippedMulti(int count, string? language = null)
    {
        var isFa = Normalize(language) == "fa";
        return isFa
            ? $"{count} قطعه بدون آلبوم ثبت شدند."
            : $"{count} tracks were added without an album.";
    }

    public static string FormatAlbumCreated(string albumTitle, string? language = null) =>
        Format(TelegramBotTextIds.AlbumCreated, language, albumTitle);

    public static string FormatAlbumFailed(string reason, string? language = null) =>
        Format(TelegramBotTextIds.AlbumFailed, language, reason);

    /// <summary>Playing time as <c>m:ss</c>, or <c>h:mm:ss</c> once it passes an hour.</summary>
    public static string FormatDuration(double seconds)
    {
        if (double.IsNaN(seconds) || seconds < 0) seconds = 0;

        var total = (long)Math.Round(seconds);
        var hours = total / 3600;
        var minutes = total % 3600 / 60;
        var remaining = total % 60;
        return hours > 0
            ? string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}:{2:00}", hours, minutes, remaining)
            : string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}", minutes, remaining);
    }

    /// <summary>The library overview: the counters the operator asked for, all read at render time.</summary>
    public static string FormatOverview(LibraryOverview overview, string? language = null)
    {
        var lines = new List<string>
        {
            Get(TelegramBotTextIds.ReportsTitle, language),
            Line(TelegramBotTextIds.ReportsAlbums, overview.AlbumCount, language),
            Line(TelegramBotTextIds.ReportsTracks, overview.TrackCount, language),
            Line(TelegramBotTextIds.ReportsArtists, overview.ArtistCount, language),
            Line(TelegramBotTextIds.ReportsUsers, overview.UserCount, language),
            Line(TelegramBotTextIds.ReportsUsersActive, overview.ActiveUserCount, language),
            Line(TelegramBotTextIds.ReportsUsersInactive, overview.InactiveUserCount, language),
            Label(TelegramBotTextIds.ReportsDuration, language) + ": "
                + FormatDuration(overview.TotalDurationSeconds)
        };

        lines.Add(string.Empty);
        lines.Add(Get(overview.TrackCount == 0 && overview.AlbumCount == 0
            ? TelegramBotTextIds.ReportsEmpty
            : TelegramBotTextIds.ReportsHint, language));
        return string.Join("\n", lines);
    }

    /// <summary>The album-management list header.</summary>
    public static string FormatAlbumManage(int albumCount, int page, int totalPages, string? language = null)
    {
        var lines = new List<string>
        {
            Get(TelegramBotTextIds.AlbumManageTitle, language)
                + " (" + albumCount.ToString(CultureInfo.InvariantCulture) + ")"
        };

        lines.Add(string.Empty);
        lines.Add(Get(albumCount == 0
            ? TelegramBotTextIds.AlbumManageEmpty
            : TelegramBotTextIds.AlbumManageHint, language));
        AddPageInfo(lines, page, totalPages, language);
        return string.Join("\n", lines);
    }

    /// <summary>One album's report: identity, totals, artist share, and the removal hint.</summary>
    public static string FormatAlbumDetail(AlbumReport report, int page, int totalPages, string? language = null)
    {
        var lines = new List<string>
        {
            Format(TelegramBotTextIds.AlbumDetailTitle, language, report.Title)
        };

        if (!string.IsNullOrWhiteSpace(report.AlbumArtist))
        {
            lines.Add(Label(TelegramBotTextIds.AlbumDetailArtist, language) + ": " + report.AlbumArtist);
        }
        if (report.Year is { } year)
        {
            lines.Add(Label(TelegramBotTextIds.AlbumDetailYear, language) + ": "
                + year.ToString(CultureInfo.InvariantCulture));
        }

        lines.Add(
            Label(TelegramBotTextIds.AlbumDetailTracks, language) + ": "
            + report.TrackCount.ToString(CultureInfo.InvariantCulture)
            + " — " + Label(TelegramBotTextIds.AlbumDetailDuration, language) + ": "
            + FormatDuration(report.TotalDurationSeconds));

        if (report.Artists.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add(Label(TelegramBotTextIds.AlbumDetailArtistsHeading, language) + ":");
            foreach (var share in report.Artists)
            {
                lines.Add("• " + share.Name + ": " + share.TrackCount.ToString(CultureInfo.InvariantCulture));
            }
        }

        lines.Add(string.Empty);
        lines.Add(Get(report.TrackCount == 0
            ? TelegramBotTextIds.AlbumDetailNoTracks
            : TelegramBotTextIds.AlbumDetailHint, language));
        AddPageInfo(lines, page, totalPages, language);
        return string.Join("\n", lines);
    }

    /// <summary>The "add a track" screen for one album.</summary>
    public static string FormatAlbumAdd(
        string albumTitle, int candidateCount, int page, int totalPages, string? language = null)
    {
        var lines = new List<string>
        {
            Format(TelegramBotTextIds.AlbumAddTitle, language, albumTitle),
            string.Empty,
            Get(candidateCount == 0
                ? TelegramBotTextIds.AlbumAddEmpty
                : TelegramBotTextIds.AlbumAddHint, language)
        };
        AddPageInfo(lines, page, totalPages, language);
        return string.Join("\n", lines);
    }

    /// <summary>The two-step removal confirmation. Removal is reversible but not accidental.</summary>
    public static string FormatAlbumConfirmRemove(string trackTitle, string albumTitle, string? language = null) =>
        Format(TelegramBotTextIds.AlbumConfirmRemove, language, trackTitle, albumTitle) + "\n\n"
        + Get(TelegramBotTextIds.AlbumConfirmRemoveHint, language);

    public static string FormatAlbumRemoved(string trackTitle, string albumTitle, string? language = null) =>
        Format(TelegramBotTextIds.AlbumRemoved, language, trackTitle, albumTitle);

    public static string FormatAlbumAdded(string trackTitle, string albumTitle, string? language = null) =>
        Format(TelegramBotTextIds.AlbumAdded, language, trackTitle, albumTitle);

    /// <summary>The artist-report list header.</summary>
    public static string FormatArtistList(int artistCount, int page, int totalPages, string? language = null)
    {
        var lines = new List<string>
        {
            Get(TelegramBotTextIds.ArtistReportTitle, language)
                + " (" + artistCount.ToString(CultureInfo.InvariantCulture) + ")"
        };

        lines.Add(string.Empty);
        lines.Add(Get(artistCount == 0
            ? TelegramBotTextIds.ArtistReportEmpty
            : TelegramBotTextIds.ArtistReportHint, language));
        AddPageInfo(lines, page, totalPages, language);
        return string.Join("\n", lines);
    }

    /// <summary>One artist's report: contribution totals and the albums they appear on.</summary>
    public static string FormatArtistDetail(ArtistReport report, string? language = null)
    {
        var lines = new List<string>
        {
            Format(TelegramBotTextIds.ArtistDetailTitle, language, report.Name),
            string.Empty,
            Label(TelegramBotTextIds.ArtistDetailTracks, language) + ": "
                + report.TrackCount.ToString(CultureInfo.InvariantCulture),
            Label(TelegramBotTextIds.ArtistDetailAlbums, language) + ": "
                + report.AlbumCount.ToString(CultureInfo.InvariantCulture),
            Label(TelegramBotTextIds.ArtistDetailDuration, language) + ": "
                + FormatDuration(report.TotalDurationSeconds),
            string.Empty,
            Label(TelegramBotTextIds.ArtistAlbumsHeading, language) + ":"
        };

        if (report.Albums.Count == 0)
        {
            lines.Add(Get(TelegramBotTextIds.ArtistNoAlbums, language));
        }
        else
        {
            foreach (var album in report.Albums)
            {
                lines.Add("• " + album.Title + " — "
                    + Format(TelegramBotTextIds.ArtistTrackUnit, language, album.TrackCount));
            }
        }

        return string.Join("\n", lines);
    }

    private static string Line(string id, int value, string? language) =>
        Label(id, language) + ": " + value.ToString(CultureInfo.InvariantCulture);

    private static void AddPageInfo(List<string> lines, int page, int totalPages, string? language)
    {
        if (totalPages <= 1) return;
        lines.Add(string.Empty);
        lines.Add(Format(TelegramBotTextIds.PageInfo, language, page + 1, totalPages));
    }

    /// <summary>
    /// Fills <c>{0}</c>, <c>{1}</c> placeholders. Uses the invariant culture so a number in a file
    /// name is never reformatted, and never lets a missing argument throw into a request path.
    /// </summary>
    private static string Format(string id, string? language, params object[] arguments)
    {
        var template = Get(id, language);
        try
        {
            return string.Format(CultureInfo.InvariantCulture, template, arguments);
        }
        catch (FormatException)
        {
            // A template that does not match its arguments is a programming error; shipping the
            // unformatted text is better than failing the whole update.
            return template;
        }
    }

    private static string Label(string id, string? language) => Get(id, language);

    private static string Normalize(string? language) =>
        string.IsNullOrWhiteSpace(language) ? DefaultLanguage : language.Trim().ToLowerInvariant();
}
