using System.Diagnostics;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MusicHub.Api.Realtime;
using MusicHub.Domain;
using MusicHub.Infrastructure;
using MusicHub.Infrastructure.Options;

namespace MusicHub.Api.Endpoints;

public static class DashboardEndpoints
{
    public static void MapDashboardEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/dashboard/stats", [Authorize(Policy = "Admin")] async (MusicHubDbContext db, StreamTracker streams, IOptions<StorageOptions> storage, CancellationToken ct) =>
        {
            var since = DateTimeOffset.UtcNow.AddDays(-14);
            var uploads = await db.UploadSessions.AsNoTracking().Where(x => x.CreatedAt >= since)
                .Select(x => new { x.CreatedAt, x.Status }).ToListAsync(ct);
            var recentTracks = await db.Tracks.AsNoTracking().Where(x => !x.IsDeleted).Include(x => x.AudioAsset).ThenInclude(x => x.FileHash).Include(x => x.Genre).OrderByDescending(x => x.CreatedAt).Take(8).ToListAsync(ct);
            var mostPlayed = await db.PlaybackEvents.AsNoTracking().GroupBy(x => x.TrackId).Select(g => new { TrackId = g.Key, Plays = g.Count() }).OrderByDescending(x => x.Plays).Take(8).ToListAsync(ct);
            var mostIds = mostPlayed.Select(x => x.TrackId).ToList();
            var mostTracks = await db.Tracks.AsNoTracking().Where(x => mostIds.Contains(x.Id) && !x.IsDeleted).Include(x => x.AudioAsset).ThenInclude(x => x.FileHash).Include(x => x.Genre).ToListAsync(ct);
            var mostMap = mostTracks.ToDictionary(x => x.Id);
            var telegram = await db.TelegramConfigurations.AsNoTracking().FirstOrDefaultAsync(ct);
            var latestIntegrity = await db.StorageIntegrityChecks.AsNoTracking().OrderByDescending(x => x.StartedAt).Select(x => new { x.Id, x.Status, x.AssetCount, x.MissingCount, x.OrphanCount, x.StartedAt, x.CompletedAt }).FirstOrDefaultAsync(ct);
            var latestBackup = await db.BackupHistory.AsNoTracking().OrderByDescending(x => x.StartedAt).Select(x => new { x.Id, x.FileName, x.SizeBytes, x.Status, x.StartedAt, x.CompletedAt }).FirstOrDefaultAsync(ct);
            var activeUsers = await db.UserSessions.AsNoTracking().CountAsync(x => x.RevokedAt == null && x.LastSeenAt > DateTimeOffset.UtcNow.AddMinutes(-15), ct);
            var failedJobs = await db.BackgroundJobs.AsNoTracking().CountAsync(x => x.Status == JobStatus.Failed || x.Status == JobStatus.DeadLetter, ct);

            var activity = uploads.GroupBy(x => x.CreatedAt.UtcDateTime.Date)
                .Select(g => new { date = g.Key.ToString("yyyy-MM-dd"), uploaded = g.Count(x => x.Status is UploadStatus.Stored or UploadStatus.Duplicate), rejected = g.Count(x => x.Status is UploadStatus.Failed or UploadStatus.Rejected) })
                .OrderBy(x => x.date).ToList();

            return Results.Ok(new
            {
                counts = new
                {
                    tracks = await db.Tracks.CountAsync(x => !x.IsDeleted, ct),
                    albums = await db.Albums.CountAsync(x => !x.IsDeleted, ct),
                    artists = await db.Artists.CountAsync(ct),
                    users = await db.Users.CountAsync(ct),
                    playlists = await db.Playlists.CountAsync(x => !x.IsDeleted, ct),
                    folders = await db.Folders.CountAsync(x => !x.IsDeleted, ct)
                },
                storage = new
                {
                    bytes = await db.FileHashes.SumAsync(x => (long?)x.SizeBytes, ct) ?? 0,
                    assets = await db.FileHashes.CountAsync(ct),
                    disk = DiskInfo(storage.Value.RootPath)
                },
                activeUsers,
                activeStreams = streams.Active,
                recentTracks = recentTracks.Select(x => DtoMapper.Track(x)),
                mostPlayed = mostPlayed.Where(x => mostMap.ContainsKey(x.TrackId)).Select(x => new { plays = x.Plays, track = DtoMapper.Track(mostMap[x.TrackId]) }),
                uploadActivity = activity,
                telegram = new
                {
                    configured = telegram?.BotTokenProtected is not null,
                    ingestionEnabled = telegram?.IngestionEnabled == true,
                    autoPublishEnabled = telegram?.AutoPublishEnabled == true,
                    destinationConfigured = telegram?.DestinationGroupId is not null
                },
                integrity = latestIntegrity,
                backup = latestBackup,
                failedJobs,
                database = true,
                generatedAt = DateTimeOffset.UtcNow
            });
        });

        app.MapGet("/api/admin/metrics", [Authorize(Policy = "Admin")] async (MusicHubDbContext db, StreamTracker streams, IOptions<StorageOptions> storage, CancellationToken ct) =>
        {
            // Disposed: the monitoring page polls on an interval, so an undisposed Process object
            // leaked one OS handle per request until a gen-2 collection (CR-015).
            using var process = Process.GetCurrentProcess();
            var cpuStart = process.TotalProcessorTime;
            var wallStart = Stopwatch.GetTimestamp();
            var dbStart = Stopwatch.GetTimestamp();
            var dbConnected = false;
            try { dbConnected = await db.Database.CanConnectAsync(ct); } catch { }
            var dbMilliseconds = Stopwatch.GetElapsedTime(dbStart).TotalMilliseconds;
            await Task.Delay(250, ct);
            process.Refresh();
            var cpuDelta = process.TotalProcessorTime - cpuStart;
            var wallDelta = Stopwatch.GetElapsedTime(wallStart);
            var cpuPercent = wallDelta.TotalMilliseconds <= 0 ? 0 : Math.Round(100 * cpuDelta.TotalMilliseconds / (wallDelta.TotalMilliseconds * Environment.ProcessorCount), 1);
            var driveRoot = Path.GetPathRoot(Path.GetFullPath(storage.Value.RootPath));
            var drive = string.IsNullOrEmpty(driveRoot) ? null : new DriveInfo(driveRoot);
            var heartbeat = await db.AppSettings.AsNoTracking().Where(x => x.Key == "worker.heartbeat").Select(x => x.Value).SingleOrDefaultAsync(ct);
            return Results.Ok(new
            {
                process = new
                {
                    cpuPercent = Math.Clamp(cpuPercent, 0, 100),
                    workingSetBytes = process.WorkingSet64,
                    privateMemoryBytes = process.PrivateMemorySize64,
                    gcTotalBytes = GC.GetTotalMemory(false),
                    threadCount = process.Threads.Count,
                    uptimeSeconds = Math.Round((DateTimeOffset.UtcNow - process.StartTime.ToUniversalTime()).TotalSeconds, 0)
                },
                system = new
                {
                    processorCount = Environment.ProcessorCount,
                    machineName = Environment.MachineName,
                    os = Environment.OSVersion.ToString(),
                    runtime = Environment.Version.ToString()
                },
                disk = DiskSnapshot(drive),
                database = new { connected = dbConnected, responseMs = Math.Round(dbMilliseconds, 1) },
                streams = new { active = streams.Active },
                onlineUsers = await db.UserSessions.AsNoTracking().Where(x => x.RevokedAt == null && x.LastSeenAt > DateTimeOffset.UtcNow.AddMinutes(-15)).Select(x => x.UserId).Distinct().CountAsync(ct),
                jobs = new
                {
                    pending = await db.BackgroundJobs.CountAsync(x => x.Status == JobStatus.Pending, ct),
                    processing = await db.BackgroundJobs.CountAsync(x => x.Status == JobStatus.Processing, ct),
                    failed = await db.BackgroundJobs.CountAsync(x => x.Status == JobStatus.Failed || x.Status == JobStatus.DeadLetter, ct),
                    outboxPending = await db.OutboxMessages.CountAsync(x => x.ProcessedAt == null, ct),
                    telegramPending = await db.TelegramPublications.CountAsync(x => x.Status == TelegramPublishStatus.Pending || x.Status == TelegramPublishStatus.Publishing, ct)
                },
                workerHeartbeat = heartbeat,
                measuredAt = DateTimeOffset.UtcNow
            });
        });

        app.MapGet("/api/admin/audit", [Authorize(Policy = "Admin")] async (int? page, int? pageSize, string? action, Guid? actorUserId, MusicHubDbContext db, CancellationToken ct) =>
        {
            var (p, size) = RequestHelpers.Page(page, pageSize);
            var query = db.AuditLogs.AsNoTracking().AsQueryable();
            if (!string.IsNullOrWhiteSpace(action)) query = query.Where(x => x.Action == action);
            if (actorUserId is not null) query = query.Where(x => x.ActorUserId == actorUserId);
            var total = await query.CountAsync(ct);
            // Id tie-breaker: one request writes several audit rows with the same timestamp, so the
            // default ordering alone let rows shift between pages (CR-017).
            var rows = await query.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).Skip((p - 1) * size).Take(size).ToListAsync(ct);
            return Results.Ok(new PagedResult<AuditLog>(rows, p, size, total));
        });

        app.MapGet("/api/admin/backups", [Authorize(Policy = "Admin")] async (MusicHubDbContext db, CancellationToken ct) =>
        {
            var rows = await db.BackupHistory.AsNoTracking().OrderByDescending(x => x.StartedAt).Take(50).ToListAsync(ct);
            return Results.Ok(rows);
        });

        app.MapGet("/api/admin/system-events", [Authorize(Policy = "Admin")] async (MusicHubDbContext db, int? limit, CancellationToken ct) =>
        {
            var rows = await db.SystemEvents.AsNoTracking().OrderByDescending(x => x.CreatedAt).Take(Math.Clamp(limit ?? 50, 1, 200)).ToListAsync(ct);
            return Results.Ok(rows);
        });

        app.MapGet("/api/admin/online-users", [Authorize(Policy = "Admin")] async (MusicHubDbContext db, StreamTracker streams, CancellationToken ct) =>
        {
            var threshold = DateTimeOffset.UtcNow.AddMinutes(-15);
            var sessions = await db.UserSessions.AsNoTracking()
                .Where(x => x.RevokedAt == null && x.LastSeenAt >= threshold)
                .OrderByDescending(x => x.LastSeenAt)
                .Select(x => new
                {
                    sessionId = x.Id,
                    userId = x.UserId,
                    displayName = x.User.DisplayName,
                    email = x.User.Email,
                    role = x.User.UserRoles.Select(r => r.Role.Name).FirstOrDefault() ?? "User",
                    deviceName = x.DeviceName,
                    ipAddress = x.IpAddress,
                    userAgent = x.UserAgent,
                    createdAt = x.CreatedAt,
                    lastSeenAt = x.LastSeenAt
                })
                .ToListAsync(ct);

            return Results.Ok(new
            {
                onlineCount = sessions.Select(x => x.userId).Distinct().Count(),
                activeSessionsCount = sessions.Count,
                activeStreams = streams.Active,
                sessions
            });
        });
    }

    /// <summary>
    /// Disk figures for one volume. Both admin endpoints describe the volume that holds the
    /// content-addressed store: the stats endpoint used the *working directory's* root, which on a
    /// Windows-service install is C:\Windows\System32, so the dashboard reported the system volume
    /// while /api/admin/metrics reported the library's (CR-016).
    /// </summary>
    private static object? DiskSnapshot(DriveInfo? drive)
    {
        try
        {
            if (drive is not { IsReady: true }) return null;
            return new
            {
                name = drive.Name,
                totalBytes = drive.TotalSize,
                freeBytes = drive.AvailableFreeSpace,
                usedBytes = drive.TotalSize - drive.AvailableFreeSpace
            };
        }
        catch
        {
            return null;
        }
    }

    private static object? DiskInfo(string rootPath)
    {
        try
        {
            var driveRoot = Path.GetPathRoot(Path.GetFullPath(rootPath));
            if (string.IsNullOrEmpty(driveRoot)) return null;
            var drive = new DriveInfo(driveRoot);
            if (!drive.IsReady) return null;
            return new { name = drive.Name, totalBytes = drive.TotalSize, freeBytes = drive.AvailableFreeSpace };
        }
        catch
        {
            return null;
        }
    }
}

public static class SettingsEndpoints
{
    /// <summary>Settings key for the per-account simultaneous stream limit (CR-019).</summary>
    public const string MaxConcurrentPerUserKey = "streaming.maxConcurrentPerUser";

    private static readonly HashSet<string> AllowedKeys =
    [
        "server.name", "server.publicBaseUrl", "server.publicUrlEnabled", "server.lanBaseUrl", "server.lanUrlEnabled",
        "server.localIp", "server.publicIpOrDomain", "server.dashboardPort", "server.playerPort",
        "server.maintenanceMode", "ui.defaultTheme", "ui.defaultLanguage",
        "storage.maxUploadBytes", MaxConcurrentPerUserKey, "telegram.enabled"
    ];

    public static void MapSettingsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/admin/settings", [Authorize(Policy = "Admin")] async (MusicHubDbContext db, IConfiguration config, IOptions<StorageOptions> storage, HttpContext http, CancellationToken ct) =>
        {
            var stored = await db.AppSettings.AsNoTracking().Where(x => AllowedKeys.Contains(x.Key)).ToDictionaryAsync(x => x.Key, x => x.Value, ct);
            var localIp = NetworkAddressDetector.DetectLocalIPv4();
            var isDockerBridge = localIp.StartsWith("172.17.") || localIp.StartsWith("172.18.") || localIp.StartsWith("172.19.") || localIp.StartsWith("172.20.");
            var detectedPublicIp = await NetworkAddressDetector.DetectPublicIPv4Async(ct);

            var dashboardPort = Get(stored, "server.dashboardPort", Environment.GetEnvironmentVariable("MUSICHUB_DASHBOARD_PORT") ?? "5080");
            var playerPort = Get(stored, "server.playerPort", Environment.GetEnvironmentVariable("MUSICHUB_PLAYER_PORT") ?? "5081");

            var resolvedLocalIp = Get(stored, "server.localIp", isDockerBridge ? string.Empty : localIp);
            var resolvedPublicIpOrDomain = Get(stored, "server.publicIpOrDomain", !string.IsNullOrWhiteSpace(detectedPublicIp) ? detectedPublicIp : string.Empty);

            var defaultLanUrl = !string.IsNullOrWhiteSpace(resolvedLocalIp) ? $"http://{resolvedLocalIp}:{playerPort}" : string.Empty;
            var defaultPublicUrl = !string.IsNullOrWhiteSpace(resolvedPublicIpOrDomain)
                ? (resolvedPublicIpOrDomain.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || resolvedPublicIpOrDomain.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                    ? resolvedPublicIpOrDomain
                    : (System.Net.IPAddress.TryParse(resolvedPublicIpOrDomain, out _) ? $"http://{resolvedPublicIpOrDomain}:{playerPort}" : $"https://{resolvedPublicIpOrDomain}"))
                : (Environment.GetEnvironmentVariable("MUSICHUB_PUBLIC_URL") ?? config["Server:PublicBaseUrl"] ?? string.Empty);

            return Results.Ok(new
            {
                values = new
                {
                    serverName = Get(stored, "server.name", config["Server:Name"] ?? "MusicHub Enterprise"),
                    localIp = resolvedLocalIp,
                    publicIpOrDomain = resolvedPublicIpOrDomain,
                    dashboardPort,
                    playerPort,
                    publicBaseUrl = Get(stored, "server.publicBaseUrl", defaultPublicUrl),
                    publicUrlEnabled = !stored.TryGetValue("server.publicUrlEnabled", out var pEn) || pEn != "false",
                    lanBaseUrl = Get(stored, "server.lanBaseUrl", defaultLanUrl),
                    lanUrlEnabled = !stored.TryGetValue("server.lanUrlEnabled", out var lEn) || lEn != "false",
                    detectedLocalIp = isDockerBridge ? string.Empty : localIp,
                    detectedPublicIp,
                    maintenanceMode = bool.TryParse(Get(stored, "server.maintenanceMode", "false"), out var maintenance) && maintenance,
                    defaultTheme = Get(stored, "ui.defaultTheme", "dark"),
                    defaultLanguage = Get(stored, "ui.defaultLanguage", "en"),
                    maxUploadBytes = long.TryParse(Get(stored, "storage.maxUploadBytes", storage.Value.MaxUploadBytes.ToString()), out var max) ? max : storage.Value.MaxUploadBytes,
                    maxConcurrentStreamsPerUser = int.TryParse(Get(stored, SettingsEndpoints.MaxConcurrentPerUserKey, storage.Value.MaxConcurrentStreamsPerUser.ToString()), out var streams) ? streams : storage.Value.MaxConcurrentStreamsPerUser
                },
                capabilities = new
                {
                    ffprobeConfigured = !string.IsNullOrWhiteSpace(storage.Value.FfprobePath),
                    malwareScanRequired = storage.Value.RequireMalwareScan,
                    clamAvConfigured = !string.IsNullOrWhiteSpace(storage.Value.ClamAvHost)
                },
                updatedAt = stored.Count == 0 ? (DateTimeOffset?)null : DateTimeOffset.UtcNow
            });
        });

        app.MapPut("/api/admin/settings", [Authorize(Policy = "Admin")] async (Dictionary<string, string> values, ClaimsPrincipal principal, MusicHubDbContext db, IOptions<StorageOptions> storage, HttpContext http, CancellationToken ct) =>
        {
            var rejected = values.Keys.Where(x => !AllowedKeys.Contains(x)).ToList();
            if (rejected.Count > 0) return Results.BadRequest(new { error = "Unknown settings keys.", rejected });

            // Port validation
            var dPortStr = values.TryGetValue("server.dashboardPort", out var dVal) ? dVal : null;
            var pPortStr = values.TryGetValue("server.playerPort", out var pVal) ? pVal : null;

            int? newDPort = null;
            int? newPPort = null;

            if (!string.IsNullOrWhiteSpace(dPortStr))
            {
                if (!int.TryParse(dPortStr, out var dp) || dp < 1024 || dp > 65535)
                {
                    return Results.BadRequest(new { error = "پورت داشبورد باید عددی معتبر بین 1024 تا 65535 باشد." });
                }
                newDPort = dp;
            }

            if (!string.IsNullOrWhiteSpace(pPortStr))
            {
                if (!int.TryParse(pPortStr, out var pp) || pp < 1024 || pp > 65535)
                {
                    return Results.BadRequest(new { error = "پورت وب‌پلیر باید عددی معتبر بین 1024 تا 65535 باشد." });
                }
                newPPort = pp;
            }

            if (newDPort.HasValue && newPPort.HasValue && newDPort.Value == newPPort.Value)
            {
                return Results.BadRequest(new { error = "پورت داشبورد مدیریت و پورت وب‌پلیر نمی‌توانند یکسان باشند. لطفاً دو پورت متفاوت انتخاب کنید." });
            }

            var currentDPort = newDPort ?? PortHelper.GetConfiguredDashboardPort();
            var currentPPort = newPPort ?? PortHelper.GetConfiguredPlayerPort();

            // Auto-format LAN & Public Base URLs
            if (values.TryGetValue("server.localIp", out var lIp) && !string.IsNullOrWhiteSpace(lIp))
            {
                var cleanIp = lIp.Trim().Replace("http://", "").Replace("https://", "").TrimEnd('/');
                if (cleanIp.Contains(':')) cleanIp = cleanIp.Split(':')[0];
                values["server.localIp"] = cleanIp;
                values["server.lanBaseUrl"] = $"http://{cleanIp}:{currentPPort}";
            }

            if (values.TryGetValue("server.publicIpOrDomain", out var pubIpDomain) && !string.IsNullOrWhiteSpace(pubIpDomain))
            {
                var cleanPub = pubIpDomain.Trim().Replace("http://", "").Replace("https://", "").TrimEnd('/');
                if (cleanPub.Contains(':')) cleanPub = cleanPub.Split(':')[0];
                values["server.publicIpOrDomain"] = cleanPub;
                values["server.publicBaseUrl"] = System.Net.IPAddress.TryParse(cleanPub, out _)
                    ? $"http://{cleanPub}:{currentPPort}"
                    : $"https://{cleanPub}";
            }

            if (values.TryGetValue("server.publicBaseUrl", out var pUrl) && !string.IsNullOrWhiteSpace(pUrl))
            {
                var pTrim = pUrl.Trim();
                if (!pTrim.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !pTrim.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    return Results.BadRequest(new { error = "Public Server URL must start with http:// or https://" });
                }
            }
            if (values.TryGetValue("server.lanBaseUrl", out var lUrl) && !string.IsNullOrWhiteSpace(lUrl))
            {
                var lTrim = lUrl.Trim();
                if (!lTrim.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !lTrim.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    return Results.BadRequest(new { error = "Local LAN Server URL must start with http:// or https://" });
                }
            }

            foreach (var (key, value) in values)
            {
                var existing = await db.AppSettings.SingleOrDefaultAsync(x => x.Key == key, ct);
                if (existing is null) db.AppSettings.Add(new AppSetting { Key = key, Value = value, UpdatedAt = DateTimeOffset.UtcNow });
                else { existing.Value = value; existing.UpdatedAt = DateTimeOffset.UtcNow; }
            }
            AuditWriter.Add(db, principal, "settings.update", "AppSetting", null, System.Text.Json.JsonSerializer.Serialize(values.Keys), RequestHelpers.Ip(http));
            await db.SaveChangesAsync(ct);

            // Synchronize .env on disk and write port_config.json
            var localIpVal = values.TryGetValue("server.localIp", out var valLIp) ? valLIp : null;
            var publicIpVal = values.TryGetValue("server.publicIpOrDomain", out var valPub) ? valPub : null;
            var publicUrlVal = values.TryGetValue("server.publicBaseUrl", out var valPUrl) ? valPUrl : null;

            PortHelper.TryUpdateEnvFile(currentDPort, currentPPort, localIpVal, publicIpVal, publicUrlVal);

            try
            {
                var dataRoot = storage.Value.RootPath ?? Environment.GetEnvironmentVariable("MUSICHUB_DATA_ROOT") ?? "/var/lib/musichub";
                var portConfigFile = Path.Combine(dataRoot, "port_config.json");
                var configPayload = System.Text.Json.JsonSerializer.Serialize(new
                {
                    dashboardPort = currentDPort,
                    playerPort = currentPPort,
                    localIp = localIpVal,
                    publicIpOrDomain = publicIpVal,
                    lanBaseUrl = values.GetValueOrDefault("server.lanBaseUrl"),
                    publicBaseUrl = values.GetValueOrDefault("server.publicBaseUrl"),
                    updatedAt = DateTimeOffset.UtcNow
                }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(portConfigFile, configPayload);
            }
            catch { }

            return Results.NoContent();
        });
    }

    private static string Get(IReadOnlyDictionary<string, string> values, string key, string fallback) => values.TryGetValue(key, out var value) ? value : fallback;
}
