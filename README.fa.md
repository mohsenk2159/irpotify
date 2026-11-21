<div dir="rtl">

# MusicHub Enterprise

**پلتفرم خودمیزبانِ کتابخانه و پخش موسیقی، چندکاربره.**

[English](README.md) · **فارسی**

</div>

MusicHub Enterprise یک پلتفرم موسیقی خودمیزبان و چندکاربره است. وابسته به Spotify یا هیچ
کاتالوگ شخص ثالث نیست: موسیقی شما روی دیسک خودتان، روی سرور خودتان و زیر حساب‌های کاربری خودتان
نگهداری می‌شود.

| بخش | چیست |
| --- | --- |
| **سرور / API** | ASP.NET Core (.NET 10)، EF Core، PostgreSQL، احراز هویت JWT، SignalR |
| **Worker** | دریافت و انتشار از تلگرام، اسکن یکپارچگی، کارهای صف‌بندی‌شده، پاک‌سازی دوره‌ای |
| **داشبورد مدیریت** | React + TypeScript + Vite، فارسی (راست‌به‌چپ) و انگلیسی، روشن و تیره |
| **پلیر ویندوز** | WPF بومی، پخش با LibVLC، صف پخش، علاقه‌مندی‌ها، پلی‌لیست |
| **کلاینت اندروید** | Kotlin + Compose + Media3، اتصال با QR، دانلود آفلاین |
| **استقرار** | اسکریپت‌های ویندوز و Docker Compose، با پشتیبان‌گیری و بازیابی |

## دانلود

آخرین نسخه را از صفحه‌ی [Releases](../../releases/latest) دریافت کنید.

| پلتفرم | فایل | توضیح |
| --- | --- | --- |
| اندروید ۸ به بالا | `MusicHub-Android-<version>-release.apk` | نسخه‌ی امضاشده. نصب از منابع نامشخص را برای فایل‌منیجر فعال کنید. |
| ویندوز ۱۰/۱۱ (x64) | `MusicHubPlayer-win-x64-<version>.zip` | خودکفا؛ نیازی به نصب .NET نیست. استخراج کنید و `MusicHub.Player.exe` را اجرا کنید. |
| **سرور ویندوز** | `MusicHubServer-win-x64-<version>.zip` | **بسته‌ی کامل سرور**: PostgreSQL، API، Worker (ربات تلگرام) و داشبورد. بدون نیاز به .NET SDK، بدون داکر و بدون اتصال شبکه در زمان نصب. |
| داکر (سرور) | `ghcr.io/<owner>/musichub-api:<version>` و `ghcr.io/<owner>/musichub-worker:<version>` | معماری‌های `linux/amd64` و `linux/arm64`. |

در هر انتشار فایل `SHA256SUMS.txt` و برای هر فایل یک `.sha256` منتشر می‌شود تا بتوانید پیش از
نصب، صحت دانلود را بررسی کنید.

> فایل APK امضاشده، فایل ZIP پلیر و بسته‌ی سرور ویندوز، **ضمیمه‌ی Release** هستند و در مخزن گیت
> ذخیره نمی‌شوند.

## شروع سریع

### بسته‌ی سرور ویندوز (پیشنهادی برای سرور ویندوز)

فایل `MusicHubServer-win-x64-<version>.zip` را دانلود و استخراج کنید، سپس نصب‌کننده را از پوشه‌ی
استخراج‌شده اجرا کنید. هیچ پیش‌نیازی روی سیستم لازم نیست:

```powershell
powershell -ExecutionPolicy Bypass -File .\deploy\windows\install.ps1
```

نصب‌کننده آدرس شبکه‌ی محلی این دستگاه را تشخیص می‌دهد، می‌پرسد API روی کدام پورت گوش دهد،
PostgreSQL را راه‌اندازی می‌کند، رمزها را می‌سازد، PostgreSQL و API و Worker (که ربات تلگرام را اجرا
می‌کند) را بالا می‌آورد، سرویس‌ها را روی HTTP بررسی می‌کند و یک تسک زمان ورود ثبت می‌کند تا پس از
ری‌استارت دوباره بالا بیایند. در پایان آدرس داشبورد را چاپ می‌کند.

```powershell
powershell -ExecutionPolicy Bypass -File .\deploy\windows\install.ps1 -Status
powershell -ExecutionPolicy Bypass -File .\deploy\windows\install.ps1 -Start
powershell -ExecutionPolicy Bypass -File .\deploy\windows\install.ps1 -Stop
powershell -ExecutionPolicy Bypass -File .\deploy\windows\install.ps1 -Uninstall
```

### داکر (پیشنهادی برای سرور)

```bash
git clone <repository-url> musichub
cd musichub
cp .env.example .env
```

فایل `.env` را باز کنید و همه‌ی مقادیر `CHANGE_ME` را جایگزین کنید. رمزها را با یک مولّد
رمزنگاری‌شده بسازید، نه دستی:

```bash
openssl rand -hex 32   # POSTGRES_PASSWORD
openssl rand -hex 64   # MUSICHUB_JWT_SECRET
openssl rand -hex 32   # MUSICHUB_BOOTSTRAP_TOKEN
```

سپس سرویس‌ها را بالا بیاورید:

```bash
docker compose --env-file .env -f deploy/docker/docker-compose.yml up -d
```

یا بگذارید اسکریپت نصب همه‌ی این کارها را انجام دهد — رمزها را می‌سازد، آدرس شبکه‌ی محلی همین
دستگاه را تشخیص می‌دهد، می‌پرسد داشبورد روی کدام پورت منتشر شود، و آدرسی را که باید باز کنید
چاپ می‌کند:

```powershell
.\deploy\docker\install.ps1
```

### ویندوز از مخزن کد (توسعه)

این مسیر برای توسعه است و به .NET 10 SDK و پوشه‌های `tools\pgsql` و `tools\ffmpeg` نیاز دارد که هر دو
در گیت نادیده گرفته می‌شوند، پس یک clone تازه باید آن‌ها را فراهم کند. برای نصب روی سرور ویندوز
بدون هیچ ابزار نصبی، از بسته‌ی سرور ویندوز در بالا استفاده کنید.

```powershell
# ۱. اعتبارنامه‌ی پایگاه داده. آدرس شبکه را تشخیص می‌دهد و پورت API را می‌پرسد.
.\deploy\scripts\install-server.ps1

# ۲. اجرای PostgreSQL و API و Worker (ربات تلگرام هم با Worker بالا می‌آید).
.\deploy\scripts\start-dev.ps1 -BindLan

# ۳. بررسی
.\deploy\scripts\verify.ps1
```

در پایان، اسکریپت آدرس‌های دقیق را چاپ می‌کند:

```text
  Access this server
    This machine:      http://127.0.0.1:5080/index.html
    Other devices:     http://192.168.1.20:5080/index.html
    Server address for the Android and Windows apps:
      http://192.168.1.20:5080
```

برای گوشی و پلیر ویندوز از آدرس **Other devices** استفاده کنید. هم آدرس و هم پورت در زمان نصب
تشخیص داده می‌شوند، بنابراین همان اسکریپت‌ها روی هر دستگاهی کار می‌کنند و هیچ تداخل پورتی از پیش
نادیده گرفته نمی‌شود.

### اولین مدیر

مقدار `MUSICHUB_BOOTSTRAP_TOKEN` باید تنظیم شده باشد. سپس:

```bash
curl -X POST http://127.0.0.1:5080/api/setup/initialize \
  -H "Content-Type: application/json" \
  -d '{"bootstrapToken":"<your token>","email":"admin@example.com","password":"<12+ characters>","displayName":"Administrator"}'
```

پس از ساخته‌شدن حساب، توکن را از `.env` پاک کنید و API را دوباره ایجاد کنید تا bootstrap برای همیشه
غیرفعال شود.

## چطور کار می‌کند

```text
                    :80 / :443
                        │
                    ┌───▼────┐   پایان TLS اینجاست (Caddy، گواهی واقعی)
                    │ caddy  │
                    └───┬────┘
                        │ http://api:5080
                    ┌───▼────┐
                    │  api   │   مهاجرت‌ها را اعمال می‌کند، API و داشبورد را سرو می‌کند
                    └───┬────┘
          ┌─────────────┴──────────────┐
    ┌─────▼─────┐                ┌─────▼─────┐
    │ postgres  │                │  worker   │   فقط شبکه‌ی خصوصی
    └───────────┘                └───────────┘
```

* فقط لبه‌ی TLS پورت میزبان را منتشر می‌کند. PostgreSQL و API و Worker فقط داخل شبکه‌ی compose
  قابل دسترسی‌اند.
* والیوم `musichub_data` به **هر دو** سرویس API و Worker متصل است. موسیقی، کاورها، ناحیه‌ی
  قرنطینه و کلیدهای DataProtection اینجا هستند. اشتراک این کلیدها الزامی است: API توکن ربات
  تلگرام را رمز می‌کند و Worker باید آن را باز کند.
* لایه‌ی LAN (فایل `deploy/docker/docker-compose.lan.yml`) به‌صورت اختیاری داشبورد را روی یک آدرس
  خصوصی و پورتی که کاربر انتخاب می‌کند منتشر می‌کند، تا گوشی در همان شبکه بدون نیاز به دامنه‌ی
  عمومی و بدون تضعیف TLS به آن دسترسی داشته باشد.

## ساختار مخزن

```text
src/server/MusicHub.Api            API، احراز هویت، endpointها، SignalR
src/server/MusicHub.Worker         Worker پس‌زمینه (تلگرام، کارها، نگهداشت)
src/server/MusicHub.Domain         موجودیت‌ها و enumها
src/server/MusicHub.Application    لایه‌ی اپلیکیشن
src/server/MusicHub.Infrastructure EF Core، ذخیره‌سازی، FFprobe، امنیت، تلگرام
src/web/admin                      داشبورد مدیریت (React + Vite)
src/desktop/MusicHub.Player        پلیر ویندوز (WPF + LibVLC)
src/android                        کلاینت اندروید
tests/server/MusicHub.Tests        تست‌های واحد و یکپارچگی بک‌اند
deploy/scripts                     نصب، اجرا، بررسی، پشتیبان‌گیری، بازیابی در ویندوز
deploy/docker                      توپولوژی کانتینر، Dockerfileها، اسکریپت نصب
docs/                              معماری، قراردادها، وضعیت، runbookها
tools/                             ابزارهای همراه (gitignored)
storage/                           ریشه‌ی داده‌ی زمان اجرا (gitignored)
```

## پیکربندی

| متغیر | الزامی | کاربرد |
| --- | --- | --- |
| `MUSICHUB_JWT_SECRET` | بله | کلید امضای HMAC، حداقل ۳۲ کاراکتر |
| `MUSICHUB_BOOTSTRAP_TOKEN` | فقط بار اول | توکن یک‌بارمصرف برای ساخت اولین مدیر |
| `POSTGRES_PASSWORD` | داکر | رمز نقش پایگاه داده |
| `MUSICHUB_CONNECTION_STRING` | بومی | رشته‌ی اتصال PostgreSQL |
| `MUSICHUB_PUBLIC_URL` | پیشنهادی | مبدأ مطلق برای بار داده‌ی اتصال اندروید و پلیر |
| `MUSICHUB_PUBLIC_HOSTNAME` / `MUSICHUB_ACME_EMAIL` | داکر | نام میزبان گواهی Caddy و ایمیل ACME |
| `MUSICHUB_DATA_ROOT` | پیشنهادی | مسیر مطلق موسیقی، کاور، قرنطینه، کلیدها |
| `MUSICHUB_LAN_ADDRESS` / `MUSICHUB_LAN_PORT` | اختیاری | توسط `deploy/docker/install.ps1` نوشته می‌شود |
| `MUSICHUB_FFPROBE_PATH` / `MUSICHUB_FFMPEG_PATH` | پیشنهادی | مسیر ابزارهای رسانه |
| `MUSICHUB_REQUIRE_MALWARE_SCAN` / `MUSICHUB_CLAMAV_HOST` | اختیاری | الزام ClamAV |

هرگز رمزها را commit نکنید. فایل `.env`، پوشه‌ی `storage/` (شامل `dev.secrets.ps1` و فایل رمز
پایگاه داده) و کلید امضای اندروید در `.gitignore` هستند.

## ساخت از سورس

```powershell
$env:PATH="$env:PATH;$env:LOCALAPPDATA\Microsoft\dotnet"
dotnet build MusicHub.slnx
dotnet test MusicHub.slnx

npm --prefix src/web/admin install
npm --prefix src/web/admin run build
```

ایمیج API خودش داشبورد را کامپایل می‌کند، پس ایمیج ساخته‌شده از قبل رابط کاربری را سرو می‌کند.
برای اجرای بومی، بسته را در ریشه‌ی استاتیک کپی کنید:

```powershell
Copy-Item src\web\admin\dist\* src\server\MusicHub.Api\wwwroot\ -Recurse -Force
```

## امنیت

احراز مجوز سمت سرور برای هر endpoint و برای پخش رسانه؛ توکن دسترسی JWT با refresh token چرخشی و
تشخیص استفاده‌ی مجدد؛ bootstrap نیازمند توکن صریح است و هیچ رمز پیش‌فرضی وجود ندارد؛ آپلودها با
پسوند، magic bytes و تجزیه‌ی FFprobe اعتبارسنجی می‌شوند، با اسکن بدافزار اختیاری؛ موسیقی با
شناسه‌ی محتوایی ذخیره می‌شود و مسیرهایش هرگز به کلاینت برنمی‌گردد؛ توکن ربات تلگرام در حالت
سکون رمز شده و از API برنمی‌گردد.

جزئیات را در [`SECURITY.md`](SECURITY.md) ببینید.

## مستندات

| سند | محتوا |
| --- | --- |
| [`docs/INSTALL_AND_RUN.md`](docs/INSTALL_AND_RUN.md) | نصب نسخه ۱ روی ویندوز، اندروید و داکر |
| [`deploy/docker/README.md`](deploy/docker/README.md) | runbook کانتینر: گزینه‌های انتشار، پشتیبان‌گیری، بازیابی، ارتقا |
| [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) | طراحی اجزا و جریان داده |
| [`docs/API_CONTRACT.md`](docs/API_CONTRACT.md) | قرارداد endpointها، همراه با `docs/openapi.json` |
| [`docs/DATABASE_SCHEMA.md`](docs/DATABASE_SCHEMA.md) | جدول‌ها، روابط، ایندکس‌ها |
| [`docs/TELEGRAM_BOT.md`](docs/TELEGRAM_BOT.md) | راه‌اندازی ربات، دستورها، دریافت و انتشار |
| [`docs/PROJECT_STATUS.md`](docs/PROJECT_STATUS.md) | وضعیت مستندشده‌ی هر قابلیت |

## مشارکت

Issue و pull request پذیرفته می‌شود. پیش از گزارش هر چیزی که شبیه آسیب‌پذیری است،
[`SECURITY.md`](SECURITY.md) را بخوانید و برای نقص امنیتی issue عمومی باز نکنید.

## مجوز

[MIT](LICENSE)
