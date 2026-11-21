# MusicHub Enterprise — Icon & Illustration Asset Inventory

**نسخه:** 1.0 · **تاریخ:** 2026-11-21
**هدف:** فهرست کامل همه جاهایی که در سه پلتفرم (اندروید، ویندوز، وب/داکر) یک دکمه یا نمادِ فقط-متنی هست و می‌شود جایش تصویر/آیکون گذاشت.

---

## 1. چطور از این سند استفاده کنیم

1. هر آیتم یک **کد یکتا** دارد: `MH-<GROUP>-<NN>` (مثلاً `MH-NAV-04`).
2. برای هر آیتم یک پرامپت کامل نوشته شده که مستقیم قابل دادن به ایجنت تصویر است.
3. **قانون حیاتی نام‌گذاری:** خروجی هر پرامپت را با همان کد ذخیره کن، فقط با پسوند `.png`
   - `MH-NAV-04.png`
   - `MH-ILL-01.png`
4. عکس‌ها را دسته‌دسته (۱۰ تا ۱۵ تا) بفرست. با نام فایل، جای هر تصویر در پروژه بدون ابهام مشخص است.
5. اگر یک کد را دو بار ساختی، نسخه‌ای را بفرست که آخرین بار با همان نام ذخیره شده.

**ترتیب پیشنهادی تولید (بهترین بازده اول):**

| مرحله | گروه | تعداد | چرا اول |
| --- | --- | --- | --- |
| 1 | `MH-BRAND` | 12 | هویت کل محصول؛ همه‌جا استفاده می‌شود |
| 2 | `MH-NAV` | 26 | منوها و ناوبری هر سه پلتفرم |
| 3 | `MH-CTL` | 29 | کنترل‌های پخش و اکشن‌های تکراری |
| 4 | `MH-ILL` | 16 | حالت‌های خالی و هدرها؛ بیشترین اثر «گرافیکی‌تر شدن» |
| 5 | `MH-UI` | 18 | کروم اختصاصی (تایتل‌بار ویندوز، کارت‌های آمار وب) |

---

## 2. استایل مشترک (Master Style)

همه پرامپت‌ها این بند را درون خود دارند، ولی برای مرجع:

```
STYLE: flat vector UI icon, 2px uniform rounded stroke, duotone (one accent + one
neutral), solid fills only (no gradients, no bevels, no inner shadow), perfectly
centered, 12% safe padding, transparent background, 512x512 px, no text, no
letters, no numbers, no watermark, crisp at 24px when downscaled.
```

**پالت هر پلتفرم** (رنگ اکسنت را در پرامپت جایگزین کن):

| پلتفرم | اکسنت | پس‌زمینه تیره | پس‌زمینه روشن |
| --- | --- | --- | --- |
| Android | `#6C5CE7` بنفش + `#00CEC9` فیروزه‌ای | `#101014` | `#FDFCFF` |
| Windows Player | `#5B8CFF` آبی | `#0B0F14` | — |
| Web Dashboard | `#E8B64C` طلایی | `#0A0F1C` | `#F2F5FB` |

**قواعد سخت‌گیرانه:**
- هیچ متنی داخل تصویر نباشد. اگر نوشته لازم بود، در UI با فونت گذاشته می‌شود.
- شفافیت پس‌زمینه اجباری است (به‌جز بنرها و آیکون لانچر که پس‌زمینه دارند).
- گرادیان نزن؛ در ۲۴ پیکسل گل‌آلود می‌شود.
- از ایموجی و گلیف فونت استفاده نکن؛ این آیکون‌ها جایگزین همان‌ها هستند.

---

## 3. وضعیت فعلی (چرا این کار لازم است)

| پلتفرم | وضعیت امروز |
| --- | --- |
| Android | آیکون‌های Material + ۱۳ drawable؛ تب‌های کتابخانه و بخش‌های تنظیمات **کاملاً بدون آیکون** |
| Windows | ناوبری با **کاراکترهای یونیکد** (`⌂ ⌕ ♫ ▤ ☺ ≡ ★ ⚙ ▣`)، کنترل پخش با `▶ ⏮ ⏭ ☆`، تایتل‌بار با `— □ ✕` |
| Web | آیکون SVG تک‌رنگ ۲۴×۲۴ دارد، ولی کارت‌های آمار، حالت‌های خالی و صفحه‌های ورود **بدون تصویر** هستند |

---

# گروه A — برند · `MH-BRAND` (12 آیتم)

### MH-BRAND-01 · نشان اصلی محصول (Primary mark)
**کجا:** همه‌جا — مبنای بقیه برندها
**Prompt**
> App logo mark for a self-hosted music server called MusicHub. A stylized sound-wave pulse that curls into a rounded musical note inside a rounded-square badge with 26% corner radius. Two-tone: deep indigo `#1B1B33` badge, gold `#E8B64C` mark. Flat vector, solid fills, no gradient, no text, transparent background, 1024x1024, centered with 10% padding.

### MH-BRAND-02 · لوگوتایپ افقی روی تیره (Horizontal lockup, dark)
**کجا:** `LoginWindow.xaml:33-42`، `AuthShell.tsx:29-37`، `Layout.tsx:123-132`
**Prompt**
> Horizontal brand lockup for MusicHub: the gold sound-wave note mark on the left in a rounded-square badge, an empty horizontal area to its right reserved for a wordmark added later in code. Dark-mode version, background `#0A0F1C`, mark in `#E8B64C`, subtle 1px border `#233142` around the badge. Flat vector, no text, no gradient, 1600x400, mark occupies the left 22% only.

### MH-BRAND-03 · لوگوتایپ افقی روی روشن (Horizontal lockup, light)
**کجا:** همان سه نقطه، تم روشن
**Prompt**
> Same horizontal brand lockup as MH-BRAND-02 but light-mode: background `#F2F5FB`, mark in `#A8750D` gold, badge border `#D9E0EE`. Flat vector, no text, no gradient, 1600x400, mark only in the left 22%, rest empty.

### MH-BRAND-04 · فورگراند آیکون تطبیقی اندروید
**کجا:** `res/mipmap-anydpi-v26/ic_launcher.xml` → `drawable/ic_launcher_foreground`
**Prompt**
> Android adaptive-icon foreground: the MusicHub sound-wave note mark, white `#FFFFFF`, occupying only the central 66% of the canvas (the outer 34% must stay fully transparent because Android masks it). Flat vector, solid fills, transparent background, 1024x1024, no text, no gradient, no shadow.

### MH-BRAND-05 · آیکون لانچر قدیمی اندروید (Legacy launcher)
**کجا:** جایگزین نسل‌های قبل از API 26 (فعلاً وجود ندارد)
**Prompt**
> Android legacy launcher icon: the MusicHub sound-wave note mark in white on a filled rounded-square background with a diagonal indigo-to-violet gradient `#6C5CE7` to `#4B3BC4`, mark centered at 58% of the canvas. Slight 8% inner padding, no outer shadow, no text, 512x512, sharp edges suitable for downscaling to 48px.

### MH-BRAND-06 · فاوآیکون وب
**کجا:** `src/web/admin/index.html` (data-URI فعلی، ساده)
**Prompt**
> Web favicon: the MusicHub sound-wave note mark simplified to its boldest 5 strokes so it stays readable at 16px. Gold `#E8B64C` strokes on a `#0D1220` rounded-square background with 20% corner radius. Flat vector, no text, no gradient, 512x512, thick strokes (at least 10% of canvas width each).

### MH-BRAND-07 · اسپلش اندروید
**کجا:** صفحه راه‌اندازی سرد (فعلاً فقط `CircularProgressIndicator`)
**Prompt**
> Android splash screen artwork, portrait 1080x1920. Centered MusicHub mark in `#6C5CE7` violet, 34% of screen width, on a near-black `#101014` background with one very soft radial violet glow behind the mark at 12% opacity. Below the mark leave a clear empty vertical band 25% of the height for a title added in code. Flat, minimal, no text, no gradient banding.

### MH-BRAND-08 · منبع آیکون ویندوز (`.ico`)
**کجا:** `MusicHub.Player.csproj` — آیکون اپ و پنجره (فعلاً تعریف نشده)
**Prompt**
> Windows application icon source: the MusicHub sound-wave note mark in white inside a rounded-square `#5B8CFF` blue badge with a thin `#8FB4FF` top highlight line. Mark must remain legible at 16x16, so use only 4 bold strokes. Flat vector, no text, no gradient, 1024x1024, centered, 10% padding.

### MH-BRAND-09 · بنر نصب‌کننده ویندوز
**کجا:** تصویر بالای نصب‌کننده/آپدیت ویندوز
**Prompt**
> Wide installer banner 1640x314 for a Windows music player. Left third: the MusicHub mark in `#5B8CFF` inside a rounded badge. Right two-thirds: an abstract dark scene of a laptop silhouette with soft blue waveform lines rising from it, low contrast, `#0B0F14` base. Flat vector illustration, no text, no logos other than the mark.

### MH-BRAND-10 · بنر هدر README
**کجا:** بالای `README.md` و `README.fa.md`
**Prompt**
> Open-source project hero banner 1600x600, dark theme `#0A0F1C`. Center-left: the MusicHub gold mark. Flowing abstract gold sound-wave ribbons across the lower half, thin and elegant, plus faint server-rack and phone silhouettes at 8% opacity on the right. Generous empty space in the upper middle for a title added in markdown. Flat vector, no text, no gradient banding.

### MH-BRAND-11 · تصویر اجتماعی گیت‌هاب (Social preview)
**کجا:** Settings → Social preview در GitHub (1280×640)
**Prompt**
> GitHub social preview 1280x640. Deep navy `#0A0F1C` background, gold `#E8B64C` MusicHub mark at the left 28%, and on the right a minimal isometric stack of three rounded rectangles suggesting a server, a desktop screen and a phone, outlined in slate `#2A3550` with one gold accent line each. Flat vector, no text, no gradient, plenty of negative space.

### MH-BRAND-12 · آواتار ربات تلگرام
**کجا:** پروفایل ربات در BotFather
**Prompt**
> Square avatar for a Telegram music bot: the MusicHub gold mark centered on a `#101014` background, with a small gold paper-plane silhouette nested in the lower-right of the note so it reads as "music + delivery". Flat vector, solid fills, no text, no gradient, 512x512, mark at 70% of canvas.

---

# گروه B — ناوبری · `MH-NAV` (26 آیتم)

این آیکون‌ها در هر سه پلتفرم استفاده می‌شوند تا حس یکپارچگی ایجاد شود.

### MH-NAV-01 · خانه (Home)
**کجا:** Android `MainShell.kt:94` · Windows `MainWindow.xaml:157` (`⌂`) · Web `Layout.tsx` nav
**Prompt**
> Home icon for a music app: a simple house outline whose chimney is replaced by one small sound-wave tick, `2px` rounded stroke, duotone with `#6C5CE7` accent on the wave and neutral `#C9C5D2` on the house. Flat vector, transparent background, 512x512, centered, no text.

### MH-NAV-02 · جستجو (Search)
**کجا:** Android `MainShell.kt:100` · Windows `MainWindow.xaml:163` (`⌕`) و `:246` · Web `icons.tsx` `search`
**Prompt**
> Search icon: a magnifier whose lens contains two tiny concentric sound-wave arcs instead of a plain circle interior. `2px` rounded stroke, accent `#6C5CE7` on the arcs, neutral on the handle. Flat vector, transparent background, 512x512, no text.

### MH-NAV-03 · کتابخانه (Library)
**کجا:** Android `MainShell.kt:106`
**Prompt**
> Library icon: three upright book spines of different heights, the tallest one shaped like a vinyl record sleeve with a small circle in its center. `2px` rounded stroke, duotone with `#6C5CE7` accent on the record sleeve. Flat vector, transparent background, 512x512, no text.

### MH-NAV-04 · در حال پخش (Now Playing)
**کجا:** Android `MainShell.kt:112` · Web player bar
**Prompt**
> Now-playing icon: a rounded vinyl record seen at a slight angle with a small equalizer of three bars standing on its lower-right edge. `2px` rounded stroke, duotone with `#6C5CE7` accent on the equalizer bars. Flat vector, transparent background, 512x512, no text.

### MH-NAV-05 · آهنگ‌ها (Tracks)
**کجا:** Windows `MainWindow.xaml:169` (`♫`) · Web `Layout.tsx:36` · Android library tab
**Prompt**
> Tracks icon: two beamed eighth notes with a thin baseline, drawn as one continuous outline so it reads at 16px. `2px` rounded stroke, duotone with `#6C5CE7` accent on the note heads. Flat vector, transparent background, 512x512, no text.

### MH-NAV-06 · آلبوم‌ها (Albums)
**کجا:** Windows `MainWindow.xaml:175` (`▤`) · Web `Layout.tsx:37` · Android library tab
**Prompt**
> Albums icon: a square record sleeve standing upright with a vinyl disc sliding halfway out of its right edge. `2px` rounded stroke, duotone with `#6C5CE7` accent on the visible disc. Flat vector, transparent background, 512x512, no text.

### MH-NAV-07 · هنرمندان (Artists)
**کجا:** Windows `MainWindow.xaml:181` (`☺`) · Web `Layout.tsx:38` · Android library tab
**Prompt**
> Artists icon: a microphone on a slim stand, with a small four-point star sparkle at its upper-left to suggest a performer. `2px` rounded stroke, duotone with `#6C5CE7` accent on the sparkle. Flat vector, transparent background, 512x512, no text.

### MH-NAV-08 · پوشه‌ها (Folders)
**کجا:** Web `Layout.tsx:39`
**Prompt**
> Folders icon: an open folder whose front flap is a vinyl record half-inserted, so the folder doubles as a sleeve. `2px` rounded stroke, duotone with `#6C5CE7` accent on the disc edge. Flat vector, transparent background, 512x512, no text.

### MH-NAV-09 · ژانرها (Genres)
**کجا:** Web `Layout.tsx:40`
**Prompt**
> Genres icon: a tag/label shape with a small hole, and inside it three ascending equalizer bars. `2px` rounded stroke, duotone with `#6C5CE7` accent on the bars. Flat vector, transparent background, 512x512, no text.

### MH-NAV-10 · پلی‌لیست‌ها (Playlists)
**کجا:** Windows `MainWindow.xaml:187` (`≡`) · Web `Layout.tsx:41` · Android library tab
**Prompt**
> Playlists icon: three stacked horizontal lines where the first line is shorter and ends in a small play triangle, plus a fourth line with a musical note at its end. `2px` rounded stroke, duotone with `#6C5CE7` accent on the triangle and note. Flat vector, transparent background, 512x512, no text.

### MH-NAV-11 · علاقه‌مندی‌ها (Favorites)
**کجا:** Windows `MainWindow.xaml:193` (`★`) · Android `FavoriteButton` + library tab
**Prompt**
> Favorites icon: a five-point star whose lower two points extend into a subtle heartbeat waveform. Outline version only (no fill). `2px` rounded stroke, duotone with `#6C5CE7` accent on the waveform portion. Flat vector, transparent background, 512x512, no text.

### MH-NAV-12 · آپلود (Upload)
**کجا:** Web `Layout.tsx:47`
**Prompt**
> Upload icon: a cloud shape with an upward arrow passing through it, and a thin horizontal baseline under the cloud suggesting a shelf. `2px` rounded stroke, duotone with `#6C5CE7` accent on the arrow. Flat vector, transparent background, 512x512, no text.

### MH-NAV-13 · کاربران (Users)
**کجا:** Web `Layout.tsx:54`
**Prompt**
> Users icon: two overlapping person busts, the rear one at 60% opacity, each with a small circular headphone band over the head. `2px` rounded stroke, duotone with `#6C5CE7` accent on the front bust. Flat vector, transparent background, 512x512, no text.

### MH-NAV-14 · تنظیمات (Settings)
**کجا:** Windows `MainWindow.xaml:199` (`⚙`) · Android `MainShell.kt:118` · Web `Layout.tsx:55`
**Prompt**
> Settings icon: a gear with only six teeth, its center replaced by a small three-band equalizer instead of a hole. `2px` rounded stroke, duotone with `#6C5CE7` accent on the equalizer. Flat vector, transparent background, 512x512, no text.

### MH-NAV-15 · تلگرام (Telegram)
**کجا:** Web `Layout.tsx:56`
**Prompt**
> Telegram module icon: a paper plane at a 30-degree upward angle whose trail is a short three-bar sound wave instead of a dotted line. `2px` rounded stroke, duotone with `#6C5CE7` accent on the plane body outline. Flat vector, transparent background, 512x512, no text.

### MH-NAV-16 · مانیتورینگ (Monitoring)
**کجا:** Web `Layout.tsx:57`
**Prompt**
> Monitoring icon: a rounded monitor frame containing a single ECG-style heartbeat line that dips into one musical note shape in the middle. `2px` rounded stroke, duotone with `#6C5CE7` accent on the note. Flat vector, transparent background, 512x512, no text.

### MH-NAV-17 · لاگ حسابرسی (Audit log)
**کجا:** Web `Layout.tsx:58`
**Prompt**
> Audit log icon: a clipboard with a shield badge on its lower-right corner and three short text lines (abstract bars, not letters) on the board. `2px` rounded stroke, duotone with `#6C5CE7` accent on the shield. Flat vector, transparent background, 512x512, no text.

### MH-NAV-18 · نشست‌ها / دستگاه‌ها (Sessions)
**کجا:** Web `Layout.tsx:65`
**Prompt**
> Sessions icon: one phone and one laptop side by side, both outlined, with a small key symbol floating between them. `2px` rounded stroke, duotone with `#6C5CE7` accent on the key. Flat vector, transparent background, 512x512, no text.

### MH-NAV-19 · فضای ذخیره‌سازی (Storage)
**کجا:** Windows `MainWindow.xaml:205` (`▣`)
**Prompt**
> Storage icon: a stack of two horizontal hard-drive platters drawn as rounded rectangles with a small activity LED dot and a tiny waveform etched on the top platter. `2px` rounded stroke, duotone with `#5B8CFF` accent on the waveform. Flat vector, transparent background, 512x512, no text.

### MH-NAV-20 · داشبورد (Dashboard)
**کجا:** Web `Layout.tsx:29`
**Prompt**
> Dashboard icon: a 2x2 grid of rounded tiles where the top-right tile is replaced by a small bar chart and the bottom-left by a circular gauge. `2px` rounded stroke, duotone with `#E8B64C` accent on the bar chart. Flat vector, transparent background, 512x512, no text.

### MH-NAV-21 · خروج از حساب (Sign out)
**کجا:** Web `Layout.tsx:196` (`IconButton icon="signout"`) · Windows `MainWindow.xaml:378`
**Prompt**
> Sign-out icon: a door frame with an arrow exiting to the right, and a tiny musical note left behind inside the doorway to signal "leaving the app". `2px` rounded stroke, duotone with a warm neutral accent on the note. Flat vector, transparent background, 512x512, no text.

### MH-NAV-22 · منوی همبرگری (Menu)
**کجا:** Web `Layout.tsx:219` · Windows `MainWindow.xaml:250` (`☰` — صف)
**Prompt**
> Menu icon: three horizontal rounded bars where the middle bar is slightly shorter and ends with a small dot, giving it a subtle "playlist" character. `2px` rounded stroke, single neutral color. Flat vector, transparent background, 512x512, no text.

### MH-NAV-23 · تم روشن (Sun)
**کجا:** Web `Layout.tsx:181,238` · `AuthShell.tsx:25`
**Prompt**
> Light-theme icon: a sun with eight short rounded rays, and the sun's disc contains a faint crescent cut-out so it also reads as "switch". `2px` rounded stroke, accent `#E8B64C` on the disc edge. Flat vector, transparent background, 512x512, no text.

### MH-NAV-24 · تم تیره (Moon)
**کجا:** همان نقاط، تم تیره
**Prompt**
> Dark-theme icon: a crescent moon whose inner cut-out is shaped like a tiny star, with two small four-point sparkles beside it. `2px` rounded stroke, accent `#8FA6D8` on the crescent. Flat vector, transparent background, 512x512, no text.

### MH-NAV-25 · زبان (Language / globe)
**کجا:** Web `Layout.tsx:165,242` · `AuthShell.tsx:12` (دکمه متنی `FA`/`EN`)
**Prompt**
> Language icon: a globe reduced to a circle with two meridian arcs, and a small speech-bubble tail attached at the lower-right to indicate language rather than geography. `2px` rounded stroke, duotone accent on the speech tail. Flat vector, transparent background, 512x512, no text.

### MH-NAV-26 · دانلودها / آفلاین (Downloads)
**کجا:** Android — رشته `nav_downloads` وجود دارد ولی صفحه‌ای وصل نیست؛ آیکون لازم است
**Prompt**
> Downloads icon: a downward arrow inside a rounded tray, with a small vinyl disc sitting behind the tray to tie it to music. `2px` rounded stroke, duotone with `#6C5CE7` accent on the arrow. Flat vector, transparent background, 512x512, no text.

---

# گروه C — کنترل‌ها و اکشن‌ها · `MH-CTL` (29 آیتم)

این‌ها جای گلیف‌های یونیکد ویندوز (`▶ ⏮ ⏭ ☆ ⏸ 🔀 🔁`) و آیکون‌های Material اندروید می‌نشینند.

### MH-CTL-01 · پخش (Play)
**کجا:** Windows `MainWindow.xaml:44,92,334` (`▶`) و `MainViewModel.cs:317` · Android `NowPlayingScreen.kt:178` · Web `icons.tsx` `play` · `Player.tsx:207`
**Prompt**
> Play button icon: a solid right-pointing triangle with generously rounded corners (radius 3px at 24px scale). Accent `#6C5CE7`. Flat vector, solid fill, transparent background, 512x512, centered, no stroke, no text.

### MH-CTL-02 · توقف (Pause)
**کجا:** Windows `MainViewModel.cs:317` · Android `NowPlayingScreen.kt:178` · Web `icons.tsx` `pause`
**Prompt**
> Pause icon: two vertical rounded bars, equal width, gap between them exactly one bar width. Accent `#6C5CE7`. Flat vector, solid fill, transparent background, 512x512, no text.

### MH-CTL-03 · قبلی (Previous)
**کجا:** Windows `MainWindow.xaml:552` (`⏮`) · Android `ic_skip_previous` · Web
**Prompt**
> Previous-track icon: a left-pointing rounded triangle with one vertical bar on its left side. The triangle and bar must be optically balanced. Accent `#6C5CE7`. Flat vector, solid fill, transparent background, 512x512, no text.

### MH-CTL-04 · بعدی (Next)
**کجا:** Windows `MainWindow.xaml:555` (`⏭`) · Android `ic_skip_next`
**Prompt**
> Next-track icon: a right-pointing rounded triangle with one vertical bar on its right side, exact mirror of MH-CTL-03. Accent `#6C5CE7`. Flat vector, solid fill, transparent background, 512x512, no text.

### MH-CTL-05 · پخش تصادفی (Shuffle)
**کجا:** Windows `MainWindow.xaml:550` + `MainViewModel.cs:392` · Android `ic_shuffle` (NowPlayingScreen.kt:157)
**Prompt**
> Shuffle icon: two crossing arrows forming an X, each arrowhead rounded, with a small dot at each of the two start points. Accent `#6C5CE7`. Flat vector, 2px rounded stroke, transparent background, 512x512, no text.

### MH-CTL-06 · تکرار همه (Repeat all)
**کجا:** Windows `MainViewModel.cs:407` · Android `ic_repeat`
**Prompt**
> Repeat-all icon: a rounded rectangle loop made of two arrows chasing each other clockwise, with no number inside. Accent `#6C5CE7`. Flat vector, 2px rounded stroke, transparent background, 512x512, no text.

### MH-CTL-07 · تکرار یکی (Repeat one)
**کجا:** Windows `MainViewModel.cs:407` (حالت ۲) · Android `player_repeat_one`
**Prompt**
> Repeat-one icon: the same clockwise two-arrow loop as MH-CTL-06 but with a small solid dot centered inside the loop (a dot, never a numeral 1). Accent `#6C5CE7`. Flat vector, transparent background, 512x512, no text, no numerals.

### MH-CTL-08 · علاقه‌مندی — خالی (Favorite outline)
**کجا:** Windows `MainWindow.xaml:40,542` (`☆`) · Android `FavoriteButton.kt:28` (`FavoriteBorder`) · Web `icons.tsx` `star`
**Prompt**
> Favorite icon, inactive state: a five-point heart-star hybrid — a star whose top point is rounded into a heart lobe. Outline only, 2px rounded stroke, neutral `#93A4BA`. Flat vector, transparent background, 512x512, no text.

### MH-CTL-09 · علاقه‌مندی — پر (Favorite filled)
**کجا:** همان نقاط، حالت فعال
**Prompt**
> Favorite icon, active state: identical silhouette to MH-CTL-08 but fully filled with accent `#E8B64C` gold, plus a 1px darker inner outline for definition. Flat vector, transparent background, 512x512, no text.

### MH-CTL-10 · صدا (Volume)
**کجا:** Web `icons.tsx` `volume` · Windows `MainViewModel.cs:383`
**Prompt**
> Volume icon: a speaker wedge with two concentric sound arcs, the outer arc thinner than the inner. Accent `#6C5CE7` on the arcs. Flat vector, 2px rounded stroke, transparent background, 512x512, no text.

### MH-CTL-11 · بی‌صدا (Mute)
**کجا:** Windows `MainViewModel.cs:383` · Web player
**Prompt**
> Mute icon: the same speaker wedge as MH-CTL-10 but with a single clean diagonal slash across the sound arcs, and the arcs removed. Slash color `#F87171`. Flat vector, 2px rounded stroke, transparent background, 512x512, no text.

### MH-CTL-12 · صف پخش (Queue)
**کجا:** Windows `MainWindow.xaml:250,505` (`☰`) · Android `ic_queue` (فعلاً استفاده نشده)
**Prompt**
> Queue icon: three horizontal rounded bars stacked with decreasing length, each with a tiny play triangle at its left end. Accent `#6C5CE7` on the triangles. Flat vector, 2px rounded stroke, transparent background, 512x512, no text.

### MH-CTL-13 · بازخوانی (Refresh)
**کجا:** Windows `MainWindow.xaml:248,447` (`⟳`) · Web `icons.tsx` `refresh` · Android `action_refresh`
**Prompt**
> Refresh icon: a circular arrow with two arrowheads at 180 degrees from each other, drawn as one continuous stroke with a small gap. Accent `#6C5CE7`. Flat vector, 2px rounded stroke, transparent background, 512x512, no text.

### MH-CTL-14 · افزودن (Add / plus)
**کجا:** Windows `MainWindow.xaml:544` (`＋`) · Web `icons.tsx` `plus` · همه دکمه‌های "Create"
**Prompt**
> Add icon: a plus sign with rounded stroke ends, plus a faint circular halo at 12% opacity behind it so it reads as a primary action. Accent `#6C5CE7`. Flat vector, 2px rounded stroke, transparent background, 512x512, no text.

### MH-CTL-15 · بستن (Close)
**کجا:** Windows `MainWindow.xaml:129,263,505` (`✕`) · `LoginWindow.xaml:64` · Android `QrScannerScreen.kt:181` · Web `icons.tsx` `close`
**Prompt**
> Close icon: an X made of two rounded strokes at 90 degrees, with the stroke ends slightly tapered. Neutral `#93A4BA`, with the option of a `#F87171` variant for destructive contexts. Flat vector, 2px rounded stroke, transparent background, 512x512, no text.

### MH-CTL-16 · بازگشت (Back)
**کجا:** Windows `MainWindow.xaml:328` (`←`) · Android `common_see_all` / album detail
**Prompt**
> Back icon: a left-pointing chevron merged with a short horizontal tail, forming an arrow whose head is a chevron rather than a filled triangle. Neutral `#93A4BA`. Flat vector, 2px rounded stroke, transparent background, 512x512, no text.

### MH-CTL-17 · حذف (Delete / trash)
**کجا:** Web `icons.tsx` `trash` · Windows `MainWindow.xaml:335` ("Delete playlist")
**Prompt**
> Delete icon: a trash can with a rounded lid, one vertical handle, and two inner vertical lines; a single small musical note falls out of the can to soften the meaning. Danger accent `#F87171` on the note only. Flat vector, 2px rounded stroke, transparent background, 512x512, no text.

### MH-CTL-18 · تأیید (Check / success)
**کجا:** Web `icons.tsx` `check` · `LoginWindow.xaml:50-53` (`✓` در فهرست ویژگی‌ها)
**Prompt**
> Check icon: a checkmark with rounded ends, enclosed in a circle whose outline is 60% of the stroke weight, so the mark dominates. Success accent `#34D399`. Flat vector, 2px rounded stroke, transparent background, 512x512, no text.

### MH-CTL-19 · ویرایش (Edit)
**کجا:** Web `icons.tsx` `edit` · `PlaylistsPage.tsx:308`
**Prompt**
> Edit icon: a pencil at 45 degrees with a rounded tip and a thin baseline under it suggesting paper; a tiny note head replaces the pencil's eraser. Accent `#6C5CE7`. Flat vector, 2px rounded stroke, transparent background, 512x512, no text.

### MH-CTL-20 · هشدار (Warning)
**کجا:** Web `icons.tsx` `warning` · `DashboardPage.tsx:92` · `guards.tsx:85` · `ErrorState`
**Prompt**
> Warning icon: a rounded triangle with one exclamation bar and one dot inside, the triangle's corners noticeably rounded. Warning accent `#E8B64C`. Flat vector, 2px rounded stroke, transparent background, 512x512, no text.

### MH-CTL-21 · اطلاع (Info)
**کجا:** Web `notice notice-info` (چند صفحه)
**Prompt**
> Info icon: a circle with a single dot above a single vertical bar, both centered; the circle outline is slightly thinner than the bars. Info accent `#5B8CFF`. Flat vector, 2px rounded stroke, transparent background, 512x512, no text.

### MH-CTL-22 · شورون‌ها (Chevrons left / right / down)
**کجا:** Web `icons.tsx` `chevron-*` · `Pagination.tsx:78-116`
**Prompt**
> A set of three chevron icons (left, right, down) on one canvas, evenly spaced in a vertical column, identical stroke weight and 90-degree angle, rounded ends. Neutral `#93A4BA`. Flat vector, 2px rounded stroke, transparent background, 512x512, no text. The three chevrons must be separable by simple cropping at 1/3 height intervals.

### MH-CTL-23 · پیوند خارجی (External link)
**کجا:** Web `icons.tsx` `external` · Windows `MainWindow.xaml:377` ("Open web dashboard")
**Prompt**
> External-link icon: a rounded square with its top-right corner opened, and an arrow escaping through that opening. Accent `#6C5CE7` on the arrow. Flat vector, 2px rounded stroke, transparent background, 512x512, no text.

### MH-CTL-24 · دانلود (Download)
**کجا:** Android `ic_download` (فعلاً استفاده نشده) · `action_download`
**Prompt**
> Download icon: a downward arrow entering a rounded horizontal tray, with a thin second tray line below to suggest storage. Accent `#6C5CE7`. Flat vector, 2px rounded stroke, transparent background, 512x512, no text.

### MH-CTL-25 · تایمر خواب (Sleep timer)
**کجا:** Android `ic_timer` (فعلاً استفاده نشده) · `player_sleep_timer`
**Prompt**
> Sleep-timer icon: a rounded clock face whose hands are replaced by a crescent moon, with a tiny "z" formed by two connected dots at the upper-right (dots and lines only, no letter). Accent `#6C5CE7`. Flat vector, 2px rounded stroke, transparent background, 512x512, no text, no letters.

### MH-CTL-26 · اکولایزر (Equalizer)
**کجا:** Android `ic_equalizer` (فعلاً استفاده نشده) · `player_equalizer`
**Prompt**
> Equalizer icon: five vertical rounded sliders of varying heights with a small circular knob on each, the middle slider tallest. Accent `#6C5CE7` on the knobs. Flat vector, 2px rounded stroke, transparent background, 512x512, no text.

### MH-CTL-27 · اسکن QR (QR scan)
**کجا:** Android `AuthScreens.kt:253` (`action_scan_qr`) · `QrScannerScreen.kt`
**Prompt**
> QR-scan icon: four corner brackets framing a small 3x3 grid of squares, with a thin horizontal laser line crossing the middle. Accent `#6C5CE7` on the laser line. Flat vector, 2px rounded stroke, transparent background, 512x512, no text.

### MH-CTL-28 · نمایش رمز (Password reveal)
**کجا:** Android `AuthScreens.kt:358-362` (`ic_visibility`)
**Prompt**
> Password-reveal icon: an eye whose pupil is a keyhole shape. Outline only. Accent `#6C5CE7` on the keyhole. Flat vector, 2px rounded stroke, transparent background, 512x512, no text.

### MH-CTL-29 · پنهان‌کردن رمز (Password hide)
**کجا:** همان نقطه، حالت دوم
**Prompt**
> Password-hide icon: the same keyhole-eye as MH-CTL-28 with a single clean diagonal slash across it, slash in `#93A4BA`. Flat vector, 2px rounded stroke, transparent background, 512x512, no text.

---

# گروه D — تصویرسازی‌ها · `MH-ILL` (16 آیتم)

این‌ها جای «حالت خالی» و هدرهای متنی می‌نشینند؛ بیشترین اثر را در گرافیکی‌تر شدن دارند.

### MH-ILL-01 · کتابخانه خالی (اندروید)
**کجا:** `LibraryScreen.kt:329` `EmptyState` · `library_empty`
**Prompt**
> Empty-state illustration for a music library with no content. A large open wooden-crate silhouette drawn in outline, with three vinyl records floating out of it and drifting upward, one tilted. Muted palette: `#3A3A4A` outlines, `#6C5CE7` accent on one record. Flat vector, transparent background, 1024x1024, generous negative space, no text.

### MH-ILL-02 · چیزی در حال پخش نیست (اندروید)
**کجا:** `NowPlayingScreen.kt:70-74` · `player_no_track`
**Prompt**
> Empty-state illustration for a music player with nothing playing. A record player arm resting on an empty platter, with a faint dashed circular groove and one lonely floating note above it. `#3A3A4A` outlines, `#6C5CE7` accent on the note. Flat vector, transparent background, 1024x1024, no text.

### MH-ILL-03 · نتیجه‌ای یافت نشد (اندروید)
**کجا:** `SearchScreen.kt:87` · `search_empty`
**Prompt**
> Empty-state illustration for a search with no results. A large magnifier tilted at 30 degrees, its lens showing an empty record sleeve, and two small dots falling out of the lens. `#3A3A4A` outlines, `#6C5CE7` accent on the magnifier rim. Flat vector, transparent background, 1024x1024, no text.

### MH-ILL-04 · بدون دانلود آفلاین (اندروید)
**کجا:** `downloads_empty` / `downloads_empty_body` (رشته‌ها موجود، صفحه ساخته نشده)
**Prompt**
> Empty-state illustration for an offline-downloads screen with nothing downloaded. A phone outline at a slight angle, its screen showing a dashed downward arrow, and a small cloud with a slash above it. `#3A3A4A` outlines, `#6C5CE7` accent on the arrow. Flat vector, transparent background, 1024x1024, no text.

### MH-ILL-05 · راهنمای اتصال دستگاه (اندروید)
**کجا:** `AuthScreens.kt:257` `pairing_help` · `pairing_manual_hint`
**Prompt**
> Small explanatory illustration for device pairing: a phone on the left, a server box on the right, and a QR-code square floating between them with two dotted arrows connecting all three. `#3A3A4A` outlines, `#6C5CE7` accent on the QR square. Flat vector, transparent background, 1024x1024, no text.

### MH-ILL-06 · هشدار HTTP (اندروید)
**کجا:** `AuthScreens.kt:413-427` `http_warning_title` / `http_warning_body`
**Prompt**
> Small warning illustration for unencrypted HTTP: a padlock whose shackle is drawn as a dashed, broken arc, with a small network-node diagram behind it at 20% opacity. Warning accent `#E8B64C` on the padlock body. Flat vector, transparent background, 1024x1024, no text.

### MH-ILL-07 · بنر هدر خانه (اندروید)
**کجا:** `HomeScreen.kt:197-216` `HomeHeader` (فعلاً فقط گرادیان + متن)
**Prompt**
> Home-screen hero banner, 1600x700, for a music app. Abstract scene: layered soft violet-to-indigo rounded shapes rising from the bottom like a stylized waveform, with three faint vinyl silhouettes drifting in the upper right at 15% opacity. Base `#1A1A22`, accent `#6C5CE7` and a touch of `#00CEC9`. Flat vector, wide format, must stay readable under overlaid white text, no text in the image.

### MH-ILL-08 · کتابخانه خالی (ویندوز)
**کجا:** `MainWindow.xaml:304` `EmptyCardsMessage` (`HasNoCards`)
**Prompt**
> Empty-state illustration for a Windows desktop music player with an empty library. A flat isometric view of an empty shelf with three record sleeves leaning against the back wall, one shelf bracket loose. Palette `#233142` outlines on `#0B0F14`, accent `#5B8CFF` on one sleeve. Flat vector, transparent background, 1200x900, no text.

### MH-ILL-09 · پنل برند صفحه ورود (ویندوز)
**کجا:** `LoginWindow.xaml:17-58` (نیمه چپ، فعلاً گرادیان خالی)
**Prompt**
> Vertical brand panel artwork for a Windows sign-in window, 520x640. Deep navy `#0F1A2A` base with a large soft-focus abstract waveform in `#5B8CFF` at 18% opacity flowing diagonally, plus a faint grid of dots in the lower third suggesting a server room. Leave the top 25% and bottom 15% visually quiet. Flat vector, no text, no logo, no gradient banding.

### MH-ILL-10 · پیشرفت انتقال کتابخانه (ویندوز)
**کجا:** `MainWindow.xaml:474-481` `Move progress`
**Prompt**
> Small illustration for a library-relocation progress panel: two folder shapes with a dotted arrow between them, a stack of three record sleeves moving along the arrow, and a thin circular progress ring behind. Palette `#233142` outlines, accent `#5B8CFF` on the arrow. Flat vector, transparent background, 1024x1024, no text.

### MH-ILL-11 · ناحیه کشیدن فایل (وب)
**کجا:** `UploadPage.tsx:160-162` `dropzone-icon` (فعلاً آیکون ۳۰px)
**Prompt**
> Upload dropzone illustration, wide 1200x500. A large rounded dashed rectangle occupying most of the canvas, and inside it a stack of three audio-file cards with waveform thumbnails lifting toward a cloud at the top. Palette `#2A3550` outlines, accent `#E8B64C` on the top card and cloud. Flat vector, transparent background, no text.

### MH-ILL-12 · صفحه ۴۰۴ (وب)
**کجا:** `App.tsx:53` `NotFoundPage`
**Prompt**
> 404 illustration for a music dashboard: a vinyl record lying on the floor with a needle arm bent away from it, and one small dust mote floating. Palette `#2A3550` outlines on `#0A0F1C`, accent `#E8B64C` on the record label. Flat vector, transparent background, 1200x900, no text, no numerals.

### MH-ILL-13 · دسترسی غیرمجاز (وب)
**کجا:** `guards.tsx:57-85` `access-denied`
**Prompt**
> Access-denied illustration: a closed door with a keyhole, and a large padlock hanging on the handle, with a subtle "no entry" circle behind the padlock at 15% opacity. Palette `#2A3550` outlines, accent `#F87171` on the padlock body. Flat vector, transparent background, 1200x900, no text.

### MH-ILL-14 · هدر ورود / راه‌اندازی (وب)
**کجا:** `AuthShell.tsx:28-41` · `LoginPage` · `SetupPage`
**Prompt**
> Header illustration for a web sign-in card, 900x360. A minimal scene of a browser window frame containing a vinyl record and a small play triangle, with two soft gold arcs radiating from behind the frame. Palette `#2A3550` outlines, accent `#E8B64C` on the arcs and record label. Flat vector, transparent background, no text.

### MH-ILL-15 · بدون ایمپورت تلگرام (وب)
**کجا:** `TelegramPage.tsx:474,543,607` `EmptyState icon="telegram"`
**Prompt**
> Empty-state illustration for a Telegram ingestion panel: a paper plane flying out of a dashed square outline toward the left, leaving a short trail of three small music notes. Palette `#2A3550` outlines, accent `#E8B64C` on the plane. Flat vector, transparent background, 1024x1024, no text.

### MH-ILL-16 · هدر مانیتورینگ (وب)
**کجا:** `MonitoringPage.tsx:58-60`
**Prompt**
> Header illustration for a monitoring page, 1200x400. Three stacked rounded panels in isometric perspective, the top one showing a heartbeat line, the middle one a bar chart, the bottom one a circular gauge. Palette `#2A3550` outlines, accent `#E8B64C` on the heartbeat line. Flat vector, transparent background, no text.

---

# گروه E — کروم اختصاصی · `MH-UI` (18 آیتم)

### MH-UI-01 · کوچک‌کردن پنجره (Windows minimize)
**کجا:** `MainWindow.xaml:127` (`—`)
**Prompt**
> Windows title-bar minimize icon: one horizontal rounded bar centered on a 512 canvas, stroke weight matching a 1.5px line at 24px. Neutral `#93A4BA`. Flat vector, transparent background, no text.

### MH-UI-02 · بزرگ‌کردن پنجره (Windows maximize)
**کجا:** `MainWindow.xaml:128` (`□`)
**Prompt**
> Windows title-bar maximize icon: a rounded square outline, corners radius 2px at 24px scale, centered, with equal 14% padding. Neutral `#93A4BA`. Flat vector, transparent background, 512x512, no text.

### MH-UI-03 · بازگرداندن پنجره (Windows restore)
**کجا:** همان دکمه، حالت دوم
**Prompt**
> Windows title-bar restore icon: two overlapping rounded squares, the rear one offset 12% up and right and drawn at 55% opacity. Neutral `#93A4BA`. Flat vector, transparent background, 512x512, no text.

### MH-UI-04 · بستن پنجره (Windows close)
**کجا:** `MainWindow.xaml:129` (`✕`)
**Prompt**
> Windows title-bar close icon: an X with rounded stroke ends, geometrically identical to MH-CTL-15 but slightly smaller in frame (16% padding). Variant A neutral `#93A4BA`; variant B `#F87171` for hover. Flat vector, transparent background, 512x512, no text. Deliver both variants as two files named MH-UI-04a and MH-UI-04b.

### MH-UI-05 · کاور پیش‌فرض کارت (Windows card placeholder)
**کجا:** `MainWindow.xaml:57-73` (گرادیان + حرف اول)
**Prompt**
> Album-cover placeholder artwork, square 800x800. A soft abstract composition of three overlapping vinyl discs at 15% opacity on a deep blue `#1B2A40` base, with a subtle diagonal light sweep in the upper left. No text, no letters, no initials. Flat vector with very soft shapes, must not compete with real cover art.

### MH-UI-06 · نشان لوگو در تایتل‌بار (Windows)
**کجا:** `MainWindow.xaml:115-118` (`♪` در مربع accent)
**Prompt**
> Tiny brand chip for a Windows title bar: the MusicHub sound-wave note mark in white, optimized for 22x22 px rendering (only 3 strokes, thick ends), inside a rounded-square `#5B8CFF` badge with 6px corner radius. Flat vector, transparent background outside the badge, 512x512, no text.

### MH-UI-07 · کارت آمار: آهنگ‌ها (Web stat card)
**کجا:** `DashboardPage.tsx:61` `StatCard icon="tracks"`
**Prompt**
> Small decorative icon for a dashboard statistic tile representing tracks: a stack of three horizontal waveform strips of decreasing length, the top strip in accent `#E8B64C`. Flat vector, 2px rounded stroke, transparent background, 512x512, no text.

### MH-UI-08 · کارت آمار: آلبوم‌ها
**کجا:** `DashboardPage.tsx:62`
**Prompt**
> Dashboard stat icon for albums: a record sleeve standing upright with two discs peeking out behind it, the front sleeve outlined and the discs filled at 30% opacity. Accent `#E8B64C` on the front sleeve edge. Flat vector, transparent background, 512x512, no text.

### MH-UI-09 · کارت آمار: هنرمندان
**کجا:** `DashboardPage.tsx:63`
**Prompt**
> Dashboard stat icon for artists: a microphone with a curved cable that ends in a small four-point star, the star in accent `#E8B64C`. Flat vector, 2px rounded stroke, transparent background, 512x512, no text.

### MH-UI-10 · کارت آمار: کاربران
**کجا:** `DashboardPage.tsx:64`
**Prompt**
> Dashboard stat icon for users: three overlapping person busts in a fan arrangement, each successive one smaller and lighter, the front bust in accent `#E8B64C`. Flat vector, transparent background, 512x512, no text.

### MH-UI-11 · کارت آمار: فضای ذخیره‌سازی
**کجا:** `DashboardPage.tsx:71`
**Prompt**
> Dashboard stat icon for storage volume: a stack of three hard-drive platters seen from a slight angle, the top platter outlined and the two below filled at 25% opacity, with a small activity dot in accent `#E8B64C`. Flat vector, transparent background, 512x512, no text.

### MH-UI-12 · کارت آمار: دیسک
**کجا:** `DashboardPage.tsx:77`
**Prompt**
> Dashboard stat icon for disk capacity: a circular pie-gauge with one wedge separated slightly from the rest, the separated wedge in accent `#E8B64C`. Flat vector, 2px rounded stroke, transparent background, 512x512, no text, no numerals.

### MH-UI-13 · کارت آمار: استریم فعال
**کجا:** `DashboardPage.tsx:90`
**Prompt**
> Dashboard stat icon for active streams: a play triangle whose right half dissolves into three expanding arcs, the arcs in accent `#E8B64C`. Flat vector, transparent background, 512x512, no text.

### MH-UI-14 · کارت آمار: کارهای ناموفق
**کجا:** `DashboardPage.tsx:92` (`icon="warning"`, tone danger)
**Prompt**
> Dashboard stat icon for failed jobs: a rounded triangle with a small broken-link chain inside it instead of an exclamation mark. Danger accent `#F87171`. Flat vector, 2px rounded stroke, transparent background, 512x512, no text.

### MH-UI-15 · کارت آمار: پایگاه داده
**کجا:** `DashboardPage.tsx:98` (`icon="shield"`)
**Prompt**
> Dashboard stat icon for database health: a classic database cylinder with three stacked discs, wrapped by a thin shield outline on its right side. Success accent `#34D399` on the shield. Flat vector, 2px rounded stroke, transparent background, 512x512, no text.

### MH-UI-16 · اعلان موفقیت (Web toast)
**کجا:** `notify(..., 'success')` — سراسر داشبورد
**Prompt**
> Toast icon for success: a circle with a checkmark, and three short radiating lines at the upper right to imply a positive outcome. Success accent `#34D399`. Flat vector, 2px rounded stroke, transparent background, 512x512, no text.

### MH-UI-17 · اعلان خطا (Web toast)
**کجا:** `notify(..., 'error')`
**Prompt**
> Toast icon for error: a circle with a short vertical bar and a dot inside, plus a small crack line at the circle's lower-right edge. Danger accent `#F87171`. Flat vector, 2px rounded stroke, transparent background, 512x512, no text.

### MH-UI-18 · صفحه‌بندی (Web pagination)
**کجا:** `Pagination.tsx:70-130`
**Prompt**
> Pagination icon set on one canvas: four glyphs in a horizontal row — double-chevron-left, chevron-left, chevron-right, double-chevron-right — evenly spaced, identical stroke weight and angle, rounded ends. Neutral `#93A4BA`. Flat vector, 2px rounded stroke, transparent background, 1024x256, no text. The four glyphs must be separable by cropping at 25% width intervals.

---

## 4. قرارداد تحویل فایل‌ها

| گروه | فرمت | اندازه | پس‌زمینه |
| --- | --- | --- | --- |
| `MH-NAV`, `MH-CTL`, `MH-UI-01..06`, `MH-UI-07..18` | PNG | 512×512 | شفاف |
| `MH-ILL-01..06`, `MH-ILL-10`, `MH-ILL-15` | PNG | 1024×1024 | شفاف |
| `MH-ILL-07`, `MH-ILL-11`, `MH-ILL-12`, `MH-ILL-13`, `MH-ILL-14`, `MH-ILL-16` | PNG | 1200×900 یا 1200×500 | شفاف |
| `MH-ILL-08`, `MH-ILL-09` | PNG | 1200×900 / 520×640 | شفاف |
| `MH-BRAND-01`, `-04`, `-05`, `-06`, `-08`, `-12` | PNG | 1024×1024 | شفاف (به‌جز `-05`، `-06`) |
| `MH-BRAND-02`, `-03` | PNG | 1600×400 | تیره / روشن |
| `MH-BRAND-07` | PNG | 1080×1920 | تیره |
| `MH-BRAND-09` | PNG | 1640×314 | تیره |
| `MH-BRAND-10` | PNG | 1600×600 | تیره |
| `MH-BRAND-11` | PNG | 1280×640 | تیره |

**مسیر نهایی در پروژه پس از تحویل:**

| پلتفرم | مسیر |
| --- | --- |
| Android | `src/android/app/src/main/res/drawable-nodpi/mh_<code>.png` |
| Android launcher | `src/android/app/src/main/res/drawable/ic_launcher_foreground.png` + `mipmap-*dpi/ic_launcher.png` |
| Windows | `src/desktop/MusicHub.Player/Assets/Icons/<code>.png` + `Assets/app.ico` |
| Web | `src/web/admin/public/assets/<code>.png` (و در صورت نیاز نسخه trace‌شده SVG) |

---

## 5. نکات مهم برای ایجنت تصویر

1. **همیشه** فایل را با کد ذخیره کن؛ نام فایل تنها راه تشخیص قطعی است.
2. اگر یک پرامپت خروجی چندگانه داد (مثل `MH-CTL-22` یا `MH-UI-18`)، همان شیت را با کد ذخیره کن؛ من خودم برش می‌زنم.
3. برای آیکون‌ها هرگز پس‌زمینه نگذار — شفافیت ضروری است.
4. اگر خروجی به ۱۰۲۴ رسید اشکالی ندارد؛ من به ۵۱۲ و ۲۴ مقیاس می‌دهم.
5. اگر تصویری «تقریباً درست» شد ولی متن داشت، دوباره بساز — متن در UI با فونت می‌آید.
6. کدها را تغییر نده و نساز؛ اگر کدی در این فهرست نیست، آن بخش هنوز به آیکون نیاز ندارد.

---

## 6. جدول رهگیری (برای علامت‌زدن هنگام تحویل)

| کد | تحویل شد | یکپارچه شد |
| --- | --- | --- |
| `MH-BRAND-01` | ☐ | ☐ |
| `MH-BRAND-02` | ☐ | ☐ |
| `MH-BRAND-03` | ☐ | ☐ |
| `MH-BRAND-04` | ☐ | ☐ |
| `MH-BRAND-05` | ☐ | ☐ |
| `MH-BRAND-06` | ☐ | ☐ |
| `MH-BRAND-07` | ☐ | ☐ |
| `MH-BRAND-08` | ☐ | ☐ |
| `MH-BRAND-09` | ☐ | ☐ |
| `MH-BRAND-10` | ☐ | ☐ |
| `MH-BRAND-11` | ☐ | ☐ |
| `MH-BRAND-12` | ☐ | ☐ |
| `MH-NAV-01` … `MH-NAV-26` | ☐ | ☐ |
| `MH-CTL-01` … `MH-CTL-29` | ☐ | ☐ |
| `MH-ILL-01` … `MH-ILL-16` | ☐ | ☐ |
| `MH-UI-01` … `MH-UI-18` | ☐ | ☐ |
