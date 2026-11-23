<p align="center">
  <h1 align="center">🎵 irpotify</h1>
  <p align="center">
    <strong>Self-Hosted Music Streaming & Library Platform with Original Spotify Experience</strong>
  </p>
  <p align="center">
    <a href="https://github.com/mohsenk2159/irpotify/releases/latest"><img src="https://img.shields.io/github/v/release/mohsenk2159/irpotify?color=1DB954&label=Release&logo=github" alt="Latest Release"></a>
    <a href="https://github.com/mohsenk2159/irpotify/releases/download/v1.0.0/irpotify-android.apk"><img src="https://img.shields.io/badge/Android%20APK-v1.0.0%20(4.7MB)-3DDC84?logo=android&logoColor=white" alt="Android APK"></a>
    <img src="https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white" alt=".NET 10">
    <img src="https://img.shields.io/badge/PostgreSQL-17-4169E1?logo=postgresql&logoColor=white" alt="PostgreSQL 17">
    <img src="https://img.shields.io/badge/Docker-Compose%20Ready-2496ED?logo=docker&logoColor=white" alt="Docker">
    <a href="LICENSE"><img src="https://img.shields.io/badge/License-MIT-blue.svg" alt="License: MIT"></a>
  </p>
  <p align="center">
    <a href="README.md"><strong>English</strong></a> · <a href="README.fa.md"><strong>فارسی</strong></a>
  </p>
</p>

---

## 📖 Overview

**irpotify** is a powerful, production-ready, self-hosted music library and streaming ecosystem. Unlike third-party music services, your entire music catalog lives on your own private infrastructure, under your total ownership and privacy.

Featuring a native **Android client redesigned with the original Spotify aesthetic**, a self-contained **Windows desktop player**, an interactive **React admin dashboard**, and an automated **Telegram ingestion bot**, irpotify provides a complete private music cloud.

---

## ✨ Features

- 🎧 **Original Spotify Experience:** Modern top category chips on the Home screen (`All`, `Tracks`, `Albums`, `Artists`, `Favorites`) and the iconic "Your Library" screen with pinned Liked Songs.
- 📱 **Instant User QR Login & Pairing:** Skip typing long URLs, IPs, usernames, or passwords. Admins can generate a user-specific QR code from the Users table with one click. Scan it in the Android app to configure network routes and log in instantly. Features high-res PNG download, direct Telegram & WhatsApp sharing with rich image previews, and automatic clipboard copy.
- 🎨 **Modern Visual Identity & Realistic Vinyl Artwork:** Completely redesigned visual style featuring realistic vector vinyl record jackets with dynamic gradient center labels for tracks and albums without covers, a live studio dashboard hero banner with soundwaves, and category-themed gradient stat badges.
- 📱 **Client-Side Custom Playlists:** Create, edit, and organize custom playlists stored 100% locally on your device (Room Database) with zero server overhead or sync issues.
- 🚀 **1-Command Linux Installation:** Fully automated setup script installing Docker, PostgreSQL 17, Caddy SSL reverse proxy, and system management tool in under 2 minutes.
- 🛠️ **CLI Management Tool:** Inspect live logs, check health, restart, or update the server at any time with the built-in `irpotify` CLI.
- 🤖 **Telegram Ingestion Bot with Multi-Admin Support:** Drop audio files or albums into your private Telegram bot for instant extraction, cover art caching, and library indexing. Supports multiple admin user IDs separated by `;` (e.g. `1234124;4324234`).
- ⚡ **High-Performance Audio Core:** ASP.NET Core (.NET 10) Minimal API, PostgreSQL 17 with SCRAM-SHA-256 auth, LibVLC audio engine, and ExoPlayer Media3.
- 🔒 **Enterprise-Grade Security:** Android Keystore encrypted credentials, JWT tokens with rotating refresh tokens, automated malware screening, and strict Admin Gate access control.
- ⏳ **Server-Authoritative Subscription & Expiry Periods:** Eliminates legacy storage/listening quotas in favor of a pure, date-based access model measured strictly by the server's authoritative system clock (`DateTimeOffset.UtcNow`). On the day/time access ends, accounts automatically deactivate and are instantly evicted from all devices (revoking all active JWTs, sessions, and refresh tokens). Administrators can renew or start a new period at any time with 1-click presets (+1 Month, +3 Months, +6 Months, +1 Year) or a custom date.
- 🔐 **Zero-Lockout Password Recovery:** Reset or recover forgotten accounts via the web login modal using your secret server Bootstrap Token, or directly from the host terminal with `sudo irpotify reset-password`.

---

## 📦 Download & Clients

| Platform | Download | Description |
| :--- | :--- | :--- |
| **Android (8.0+)** | [📥 **irpotify-android.apk** (4.9 MB)](https://github.com/mohsenk2159/irpotify/releases/download/v1.0.0/irpotify-android.apk) | Official signed release APK. Spotify UI, offline downloads, instant QR pairing. |
| **iOS (iPhone / iPad)** | [📱 **irpotify-ios.ipa**](https://github.com/mohsenk2159/irpotify/releases/latest) | Native Swift/SwiftUI client. Background audio, Lock Screen controls, QR scanner. Sideload via Sideloadly / AltStore / TrollStore. |
| **Windows Player (Setup)** | [💿 **irpotify-player-win-x64-1.0.0-setup.exe**](https://github.com/mohsenk2159/irpotify/releases/download/v1.0.0/irpotify-player-win-x64-1.0.0-setup.exe) | Official 1-click Windows Setup installer with Desktop & Start Menu shortcuts. |
| **Server for Windows (Host Setup)** | [💿 **irpotify-server-win-x64-1.0.0-setup.exe**](https://github.com/mohsenk2159/irpotify/releases/download/v1.0.0/irpotify-server-win-x64-1.0.0-setup.exe) | Complete 1-click installer to host your personal music cloud server on any Windows PC (10/11): PostgreSQL 17, API, Worker & Dashboard. |
| **Docker (Linux/ARM)** | `deploy/docker/docker-compose.yml` | Multi-arch production topology with automatic SSL by Caddy. |

> 🔗 All binaries and checksums are verified and hosted on the [GitHub Releases Page](https://github.com/mohsenk2159/irpotify/releases/tag/v1.0.0).

---

## 🚀 Quick Start

### 1. Linux Server (One-Line Automated Install)

Run this single command on Ubuntu, Debian, or any modern Linux VPS:

```bash
curl -fsSL https://raw.githubusercontent.com/mohsenk2159/irpotify/main/install.sh | sudo bash
```

The script automatically:
1. Installs Docker and required audio processing tools (`ffmpeg`, `ffprobe`).
2. Generates cryptographically secure random secrets and credentials.
3. Configures domain with free automatic SSL certificates (or local IP/LAN access).
4. Spins up PostgreSQL 17, the API engine, Background Worker, and Web Dashboard.
5. Installs the `/usr/local/bin/irpotify` management command.

#### 🎛️ Server Management CLI
Manage your server anytime by running:
```bash
sudo irpotify
```
Or use direct commands:
```bash
sudo irpotify status      # Check running container health
sudo irpotify logs        # Stream live real-time server logs
sudo irpotify restart     # Graceful restart of all services
sudo irpotify update      # Pull latest GitHub updates and rebuild
sudo irpotify credentials # Display bootstrap token and URLs
sudo irpotify reset-password # Reset admin or user password via CLI
```

> [!IMPORTANT]
> **Using irpotify on a Local Home / Office Network (LAN):**  
> By default, the automated installer detects and configures the host's public IP address. If you are hosting irpotify on a local computer, home lab, or virtual machine (without a public domain) and want other devices on your local Wi-Fi / network to connect, make sure to connect using your host's **Local Private IP** (for example: `http://192.168.1.50:5080` instead of the public IP).  
> You can find your local IP anytime by running `hostname -I` or `ip a` on Linux, or `ipconfig` on Windows.

---

### 2. Manual Docker Compose Setup

```bash
# Clone the repository
git clone https://github.com/mohsenk2159/irpotify.git /opt/irpotify
cd /opt/irpotify

# Copy and configure environment variables
cp .env.example .env
nano .env

# Start the production stack
docker compose --env-file .env -f deploy/docker/docker-compose.yml up -d --build
```

---

### 3. Setting Up the Android App & Fast QR Login

1. Download and install [**irpotify-android.apk**](https://github.com/mohsenk2159/irpotify/releases/download/v1.0.0/irpotify-android.apk) on your Android device.
2. **Instant QR Login (Recommended):**
   - Open the Web Dashboard as an administrator and go to **Users**.
   - Under the **Actions** column for any user, click the **QR Code** icon.
   - A modal displays the user's personal QR code. You can:
     - Scan it directly with the irpotify app's built-in camera scanner.
     - Click **Download PNG** to save the image.
     - Click **Telegram** or **WhatsApp**: the image is downloaded, copied to your clipboard (ready for instant `Ctrl+V` paste into chats), and messenger opens with a rich OpenGraph image preview card.
   - The Android app scans the QR code, auto-populates server network routes (LAN & Internet), and claims the user session in under a second!
3. **Manual Connection:**
   - Alternatively, type your server base URL and username/password.
   - *Local Wi-Fi Note:* If your server and phone are on the same Wi-Fi without a public domain, enter the server's local LAN IP (e.g. `http://192.168.1.xxx:5080`).

---

### 4. Setting Up the iOS App (iPhone & iPad)

The iOS client is written natively in **Swift & SwiftUI** with full background audio playback, lock screen media controls, and instant QR pairing:

1. **Download the IPA:** Download `irpotify-ios.ipa` from the [Releases](https://github.com/mohsenk2159/irpotify/releases) page.
2. **Sideload via Sideloadly (Windows / Mac - Recommended):**
   - Connect your iPhone to your PC via USB cable.
   - Launch [Sideloadly](https://sideloadly.io/), drag and drop `irpotify-ios.ipa`.
   - Enter your Apple ID and click **Start** to install.
   - On iOS: Go to **Settings > General > VPN & Device Management**, tap your developer certificate, and select **Trust**.
3. **Alternatively via AltStore or TrollStore:**
   - On AltStore: open `irpotify-ios.ipa` in AltStore and tap install.
   - On devices with TrollStore (iOS 14.0–17.0): tap `irpotify-ios.ipa` to install permanently without 7-day re-signing.
4. **Log in:** Open **irpotify** on your iPhone, tap **Scan Server QR Code**, and scan the user QR code generated in the Web Dashboard.

---

### 5. Configuring the Telegram Ingestion Bot (Multi-Admin Supported)

irpotify allows you to upload songs directly via Telegram:
1. Create a bot using [@BotFather](https://t.me/BotFather) and copy your **Bot Token**.
2. Find your personal Telegram numerical ID by messaging [@userinfobot](https://t.me/userinfobot) (e.g. `123456789`).
3. Log into the irpotify Web Dashboard as Administrator:
   - Navigate to **Settings** → **Telegram**.
   - Enter your **Bot Token**.
   - Enter your **Admin Telegram User ID**:
     > 💡 **Multi-Admin Support:** To authorize multiple administrators to send music and control the bot, separate their numeric user IDs with semicolons (`;`); for example:  
     > `1234124;4324234;99887766`
   - Enable **Ingestion** and save.
4. Any configured admin can now send or forward audio files (MP3, FLAC, M4A, OGG, WAV) to your bot to instantly import them into the library!

---

### 5. 🔐 Password Recovery & Reset

Because irpotify is a self-hosted private cloud that does not depend on external SMTP email providers, it provides two failsafe methods to recover access if you ever forget your password:

#### Method A: From the Web Dashboard (No Server Access Needed)
1. On the login screen, click **"Forgot password?"**.
2. Enter your server's **Bootstrap Token** (the master key displayed at installation time and stored in your server's `.env`, or viewed via `sudo irpotify credentials`).
3. Enter your account email (or leave blank to reset the primary administrator account).
4. Enter your new password (minimum 12 characters) and click **Reset Password**.

#### Method B: From the Linux Server Terminal (CLI)
If you have SSH/terminal access to your host, you can reset any password instantly without opening the browser:
```bash
# Interactive prompt (prompts for email and new password):
sudo irpotify reset-password

# Or pass parameters directly:
sudo irpotify reset-password admin@local MyNewSecretPassword123!
```
You can also open the management menu via `sudo irpotify` and select **Option 8 (Reset Account Password)**.

> [!NOTE]
> When a password is reset, irpotify automatically rotates the user's security stamp, revokes all previously issued JWT refresh tokens and active sessions, and clears any failed-login lockout counters.

---

## 🏛️ Architecture & Topology

```text
                     Port 80 / 443 (Internet / LAN)
                                   │
                           ┌───────▼────────┐
                           │  Caddy Proxy   │   Automatic TLS / SSL Termination
                           └───────┬────────┘
                                   │ http://api:5080
                           ┌───────▼────────┐
                           │ irpotify API   │   ASP.NET Core (.NET 10), SignalR, Auth
                           └───────┬────────┘
                 ┌─────────────────┴─────────────────┐
                 │                                   │
         ┌───────▼────────┐                  ┌───────▼────────┐
         │ PostgreSQL 17  │                  │ irpotify Worker│   Telegram Bot, Job Queue,
         │ (Private Net)  │                  │ (Private Net)  │   Integrity Scans
         └────────────────┘                  └────────────────┘
```

- **Isolated Network Security:** The database and worker are never exposed to the internet. Only Caddy faces the outside world.
- **Shared Storage Volume:** Music, artwork, and encryption keyrings reside in a secure persistent volume.

---

## 📂 Repository Layout

```text
├── src/
│   ├── android/                  # Android application (Kotlin, Jetpack Compose, Media3)
│   ├── desktop/MusicHub.Player/  # Windows desktop player (WPF, LibVLC)
│   ├── web/admin/                # Web Dashboard (React 18, TypeScript, Vite)
│   └── server/
│       ├── MusicHub.Api/         # REST API, SignalR hubs, Streaming endpoints
│       ├── MusicHub.Worker/      # Background services & Telegram ingestion
│       ├── MusicHub.Domain/      # Domain entities and contracts
│       ├── MusicHub.Application/ # Business logic & services
│       └── MusicHub.Infrastructure/ # EF Core, PostgreSQL, Audio parsing
├── deploy/
│   ├── docker/                   # Dockerfiles, Compose specs & Caddyfile
│   └── windows/                  # Native Windows installation scripts
├── docs/                         # Architecture, API specs, and runbooks
└── install.sh                    # Universal one-line Linux installer & CLI manager
```

---

## 🤝 Contributing

Contributions, issues, and feature requests are welcome! Feel free to check the [issues page](https://github.com/mohsenk2159/irpotify/issues).

---

## 📄 License

This project is licensed under the **MIT License** — see the [LICENSE](LICENSE) file for details.
