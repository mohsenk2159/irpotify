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
- 📱 **Client-Side Custom Playlists:** Create, edit, and organize custom playlists stored 100% locally on your device (Room Database) with zero server overhead or sync issues.
- 🚀 **1-Command Linux Installation:** Fully automated setup script installing Docker, PostgreSQL 17, Caddy SSL reverse proxy, and system management tool in under 2 minutes.
- 🛠️ **CLI Management Tool:** Inspect live logs, check health, restart, or update the server at any time with the built-in `irpotify` CLI.
- 🤖 **Telegram Ingestion Bot:** Drop audio files or albums into your private Telegram bot for instant extraction, cover art caching, and library indexing.
- ⚡ **High-Performance Audio Core:** ASP.NET Core (.NET 10) Minimal API, PostgreSQL 17 with SCRAM-SHA-256 auth, LibVLC audio engine, and ExoPlayer Media3.
- 🔒 **Enterprise-Grade Security:** Android Keystore encrypted credentials, JWT tokens with rotating refresh tokens, automated malware screening, and strict Admin Gate access control.

---

## 📦 Download & Clients

| Platform | Download | Description |
| :--- | :--- | :--- |
| **Android (8.0+)** | [📥 **irpotify-android.apk** (v1.0.0)](https://github.com/mohsenk2159/irpotify/releases/download/v1.0.0/irpotify-android.apk) | Official signed release APK (4.7 MB). Jetpack Compose, Media3 ExoPlayer, offline downloads, QR pairing. |
| **Windows Desktop** | [📥 **MusicHubPlayer-win-x64-1.0.0.zip**](https://github.com/mohsenk2159/irpotify/releases/tag/v1.0.0) | Standalone WPF player with LibVLC engine. No .NET runtime installation required. |
| **Windows Server** | [📥 **MusicHubServer-win-x64-1.0.0.zip**](https://github.com/mohsenk2159/irpotify/releases/tag/v1.0.0) | Complete native server stack: PostgreSQL, API, Worker, and Web Dashboard. |
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
```

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

### 3. Setting Up the Android App

1. Download [**irpotify-android.apk**](https://github.com/mohsenk2159/irpotify/releases/download/v1.0.0/irpotify-android.apk) on your Android device and install it.
2. Open the app and connect using your server's IP/Domain or scan the QR code from the Web Dashboard.
3. Enjoy your library, create custom local playlists, and listen to music seamlessly!

---

### 4. Configuring the Telegram Ingestion Bot

irpotify allows you to upload songs directly via Telegram:
1. Create a bot using [@BotFather](https://t.me/BotFather) and copy your **Bot Token**.
2. Find your personal Telegram numerical ID by messaging [@userinfobot](https://t.me/userinfobot) (e.g. `123456789`).
3. Log into the irpotify Web Dashboard as Administrator:
   - Navigate to **Settings** → **Telegram**.
   - Enter your **Bot Token** and **Admin Telegram User ID**.
   - Enable **Ingestion**.
4. Send or forward any audio file (MP3, FLAC, M4A, OGG, WAV) to your bot to instantly import it into your library!

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
