using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MusicHub.Domain;
using MusicHub.Infrastructure;
using MusicHub.Infrastructure.Options;

namespace MusicHub.Api.Services;

public sealed record BackupManifest(
    int SchemaVersion,
    DateTimeOffset CreatedAt,
    string ServerVersion,
    Dictionary<string, string> Checksums);

public sealed record RestoreResultDto(
    bool Success,
    int UsersRestored,
    int ArtistsRestored,
    int AlbumsRestored,
    int TracksRestored,
    int PlaylistsRestored,
    int FilesRestored,
    string Message);

internal sealed class BackupDatabaseSnapshot
{
    public List<RoleSnapshot> Roles { get; set; } = [];
    public List<UserSnapshot> Users { get; set; } = [];
    public List<UserRoleSnapshot> UserRoles { get; set; } = [];
    public List<GenreSnapshot> Genres { get; set; } = [];
    public List<ArtistSnapshot> Artists { get; set; } = [];
    public List<AlbumSnapshot> Albums { get; set; } = [];
    public List<FileHashSnapshot> FileHashes { get; set; } = [];
    public List<AudioAssetSnapshot> AudioAssets { get; set; } = [];
    public List<TrackSnapshot> Tracks { get; set; } = [];
    public List<TrackArtistSnapshot> TrackArtists { get; set; } = [];
    public List<AlbumTrackSnapshot> AlbumTracks { get; set; } = [];
    public List<FolderSnapshot> Folders { get; set; } = [];
    public List<FolderTrackSnapshot> FolderTracks { get; set; } = [];
    public List<PlaylistSnapshot> Playlists { get; set; } = [];
    public List<PlaylistTrackSnapshot> PlaylistTracks { get; set; } = [];
    public List<FavoriteSnapshot> Favorites { get; set; } = [];
    public List<TelegramConfigSnapshot> TelegramConfigs { get; set; } = [];
    public List<AppSettingSnapshot> AppSettings { get; set; } = [];
}

internal sealed record RoleSnapshot(int Id, string Name, string Description, bool IsSystem);
internal sealed record UserSnapshot(
    Guid Id, string Email, string NormalizedEmail, string DisplayName, string PasswordHash,
    string SecurityStamp, bool IsActive, bool CanUsePlayer, long QuotaBytes,
    DateTimeOffset? ExpiresAt, long ListenQuotaSeconds, long ListenSecondsUsed,
    DateTimeOffset? DeactivatedAt, string? DeactivationReason, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
internal sealed record UserRoleSnapshot(Guid UserId, int RoleId);
internal sealed record GenreSnapshot(Guid Id, string Name, string NormalizedName, DateTimeOffset CreatedAt);
internal sealed record ArtistSnapshot(Guid Id, string Name, string NormalizedName, string? Biography, string? ArtworkPath, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
internal sealed record AlbumSnapshot(Guid Id, string Title, string NormalizedTitle, string AlbumArtist, Guid? ArtistId, int? Year, string? Genre, string? Description, string? ArtworkPath, bool IsDeleted, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
internal sealed record FileHashSnapshot(Guid Id, string Sha256, long SizeBytes, DateTimeOffset CreatedAt);
internal sealed record AudioAssetSnapshot(Guid Id, Guid FileHashId, string OriginalFileName, string Extension, string MimeType, string StoragePath, long SizeBytes, DateTimeOffset CreatedAt);
internal sealed record TrackSnapshot(Guid Id, string Title, string NormalizedTitle, string ArtistDisplay, string AlbumDisplay, Guid? GenreId, Guid AudioAssetId, int? Year, int? TrackNumber, int? DiscNumber, double DurationSeconds, int? Bitrate, int? SampleRate, int? Channels, string? Codec, string? ArtworkPath, bool IsDeleted, DateTimeOffset? DeletedAt, Guid? CreatedByUserId, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
internal sealed record TrackArtistSnapshot(Guid TrackId, Guid ArtistId, string Role, int Position);
internal sealed record AlbumTrackSnapshot(Guid AlbumId, Guid TrackId, int Position);
internal sealed record FolderSnapshot(Guid Id, string Name, Guid? ParentId, string? Description, string? ArtworkPath, Guid? CreatedByUserId, bool IsDeleted, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
internal sealed record FolderTrackSnapshot(Guid FolderId, Guid TrackId, DateTimeOffset AddedAt);
internal sealed record PlaylistSnapshot(Guid Id, string Name, string? Description, Guid OwnerUserId, bool IsPublic, bool IsCollaborative, string? ArtworkPath, bool IsDeleted, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
internal sealed record PlaylistTrackSnapshot(Guid PlaylistId, Guid TrackId, int Position, Guid? AddedByUserId, DateTimeOffset AddedAt);
internal sealed record FavoriteSnapshot(Guid UserId, Guid TrackId, DateTimeOffset CreatedAt);
internal sealed record TelegramConfigSnapshot(Guid Id, string? BotTokenPlaintext, long? AdminTelegramUserId, long? AdminChatId, long? DestinationGroupId, long? DestinationTopicId, bool AutoPublishEnabled, bool IngestionEnabled, long MaxIncomingFileSizeBytes, int MaxAudioDurationSeconds, string ApiEndpoint, string? LocalBotApiEndpoint, int MaxRetryAttempts, DateTimeOffset UpdatedAt, Guid? UpdatedByUserId);
internal sealed record AppSettingSnapshot(string Key, string Value, DateTimeOffset UpdatedAt);

/// <summary>
/// Full disaster-recovery backup and restore service with SHA-256 manifest verification,
/// Zip-Slip / Zip-Bomb protection, atomic database transactions, and DataProtection re-encryption.
/// </summary>
public sealed class SystemBackupService
{
    private const int MaxEntries = 50_000;
    private const long MaxTotalUncompressedBytes = 10L * 1024 * 1024 * 1024; // 10 GiB
    private const string TelegramPurpose = "MusicHub.Telegram.BotToken.v1";

    private readonly MusicHubDbContext _db;
    private readonly StorageOptions _storage;
    private readonly IDataProtector _telegramProtector;

    public SystemBackupService(
        MusicHubDbContext db,
        IOptions<StorageOptions> storage,
        IDataProtectionProvider dataProtection)
    {
        _db = db;
        _storage = storage.Value;
        _telegramProtector = dataProtection.CreateProtector(TelegramPurpose);
    }

    public async Task<(string FilePath, string FileName, long SizeBytes)> CreateBackupArchiveAsync(CancellationToken ct)
    {
        var tempDir = Path.Combine(_storage.RootPath, "tmp");
        Directory.CreateDirectory(tempDir);
        var fileName = $"irpotify-backup-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.zip";
        var zipPath = Path.Combine(tempDir, $"{Guid.NewGuid():N}-{fileName}");

        var telegramRows = await _db.TelegramConfigurations.AsNoTracking().ToListAsync(ct);
        var telegramSnapshots = telegramRows.Select(t =>
        {
            string? plaintext = null;
            if (!string.IsNullOrWhiteSpace(t.BotTokenProtected))
            {
                try { plaintext = _telegramProtector.Unprotect(t.BotTokenProtected); }
                catch { plaintext = null; }
            }
            return new TelegramConfigSnapshot(
                t.Id, plaintext, t.AdminTelegramUserId, t.AdminChatId, t.DestinationGroupId,
                t.DestinationTopicId, t.AutoPublishEnabled, t.IngestionEnabled, t.MaxIncomingFileSizeBytes,
                t.MaxAudioDurationSeconds, t.ApiEndpoint, t.LocalBotApiEndpoint, t.MaxRetryAttempts,
                t.UpdatedAt, t.UpdatedByUserId);
        }).ToList();

        var snapshot = new BackupDatabaseSnapshot
        {
            Roles = await _db.Roles.AsNoTracking().Select(r => new RoleSnapshot(r.Id, r.Name, r.Description, r.IsSystem)).ToListAsync(ct),
            Users = await _db.Users.AsNoTracking().Select(u => new UserSnapshot(
                u.Id, u.Email, u.NormalizedEmail, u.DisplayName, u.PasswordHash, u.SecurityStamp,
                u.IsActive, u.CanUsePlayer, u.QuotaBytes, u.ExpiresAt, u.ListenQuotaSeconds,
                u.ListenSecondsUsed, u.DeactivatedAt, u.DeactivationReason, u.CreatedAt, u.UpdatedAt)).ToListAsync(ct),
            UserRoles = await _db.UserRoles.AsNoTracking().Select(ur => new UserRoleSnapshot(ur.UserId, ur.RoleId)).ToListAsync(ct),
            Genres = await _db.Genres.AsNoTracking().Select(g => new GenreSnapshot(g.Id, g.Name, g.NormalizedName, g.CreatedAt)).ToListAsync(ct),
            Artists = await _db.Artists.AsNoTracking().Select(a => new ArtistSnapshot(a.Id, a.Name, a.NormalizedName, a.Biography, a.ArtworkPath, a.CreatedAt, a.UpdatedAt)).ToListAsync(ct),
            Albums = await _db.Albums.AsNoTracking().Select(a => new AlbumSnapshot(a.Id, a.Title, a.NormalizedTitle, a.AlbumArtist, a.ArtistId, a.Year, a.Genre, a.Description, a.ArtworkPath, a.IsDeleted, a.CreatedAt, a.UpdatedAt)).ToListAsync(ct),
            FileHashes = await _db.FileHashes.AsNoTracking().Select(f => new FileHashSnapshot(f.Id, f.Sha256, f.SizeBytes, f.CreatedAt)).ToListAsync(ct),
            AudioAssets = await _db.AudioAssets.AsNoTracking().Select(a => new AudioAssetSnapshot(a.Id, a.FileHashId, a.OriginalFileName, a.Extension, a.MimeType, a.StoragePath, a.SizeBytes, a.CreatedAt)).ToListAsync(ct),
            Tracks = await _db.Tracks.AsNoTracking().Select(t => new TrackSnapshot(t.Id, t.Title, t.NormalizedTitle, t.ArtistDisplay, t.AlbumDisplay, t.GenreId, t.AudioAssetId, t.Year, t.TrackNumber, t.DiscNumber, t.DurationSeconds, t.Bitrate, t.SampleRate, t.Channels, t.Codec, t.ArtworkPath, t.IsDeleted, t.DeletedAt, t.CreatedByUserId, t.CreatedAt, t.UpdatedAt)).ToListAsync(ct),
            TrackArtists = await _db.TrackArtists.AsNoTracking().Select(ta => new TrackArtistSnapshot(ta.TrackId, ta.ArtistId, ta.Role, ta.Position)).ToListAsync(ct),
            AlbumTracks = await _db.AlbumTracks.AsNoTracking().Select(at => new AlbumTrackSnapshot(at.AlbumId, at.TrackId, at.Position)).ToListAsync(ct),
            Folders = await _db.Folders.AsNoTracking().Select(f => new FolderSnapshot(f.Id, f.Name, f.ParentId, f.Description, f.ArtworkPath, f.CreatedByUserId, f.IsDeleted, f.CreatedAt, f.UpdatedAt)).ToListAsync(ct),
            FolderTracks = await _db.FolderTracks.AsNoTracking().Select(ft => new FolderTrackSnapshot(ft.FolderId, ft.TrackId, ft.AddedAt)).ToListAsync(ct),
            Playlists = await _db.Playlists.AsNoTracking().Select(p => new PlaylistSnapshot(p.Id, p.Name, p.Description, p.OwnerUserId, p.IsPublic, p.IsCollaborative, p.ArtworkPath, p.IsDeleted, p.CreatedAt, p.UpdatedAt)).ToListAsync(ct),
            PlaylistTracks = await _db.PlaylistTracks.AsNoTracking().Select(pt => new PlaylistTrackSnapshot(pt.PlaylistId, pt.TrackId, pt.Position, pt.AddedByUserId, pt.AddedAt)).ToListAsync(ct),
            Favorites = await _db.Favorites.AsNoTracking().Select(f => new FavoriteSnapshot(f.UserId, f.TrackId, f.CreatedAt)).ToListAsync(ct),
            TelegramConfigs = telegramSnapshots,
            AppSettings = await _db.AppSettings.AsNoTracking()
                .Where(s => s.Key != "update.lastCheck" && s.Key != "update.notifiedSha")
                .Select(s => new AppSettingSnapshot(s.Key, s.Value, s.UpdatedAt)).ToListAsync(ct),
        };

        var checksums = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        await using (var fs = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None))
        using (var archive = new ZipArchive(fs, ZipArchiveMode.Create, leaveOpen: false))
        {
            var dbBytes = JsonSerializer.SerializeToUtf8Bytes(snapshot, new JsonSerializerOptions { WriteIndented = false });
            checksums["database.json"] = Convert.ToHexStringLower(SHA256.HashData(dbBytes));
            var dbEntry = archive.CreateEntry("database.json", CompressionLevel.Optimal);
            await using (var entryStream = dbEntry.Open())
            {
                await entryStream.WriteAsync(dbBytes, ct);
            }

            // Collect all unique relative media/artwork paths
            var relPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var a in snapshot.AudioAssets) if (!string.IsNullOrWhiteSpace(a.StoragePath)) relPaths.Add(a.StoragePath);
            foreach (var t in snapshot.Tracks) if (!string.IsNullOrWhiteSpace(t.ArtworkPath)) relPaths.Add(t.ArtworkPath);
            foreach (var al in snapshot.Albums) if (!string.IsNullOrWhiteSpace(al.ArtworkPath)) relPaths.Add(al.ArtworkPath);
            foreach (var ar in snapshot.Artists) if (!string.IsNullOrWhiteSpace(ar.ArtworkPath)) relPaths.Add(ar.ArtworkPath);
            foreach (var pl in snapshot.Playlists) if (!string.IsNullOrWhiteSpace(pl.ArtworkPath)) relPaths.Add(pl.ArtworkPath);
            foreach (var fo in snapshot.Folders) if (!string.IsNullOrWhiteSpace(fo.ArtworkPath)) relPaths.Add(fo.ArtworkPath);

            var rootFull = Path.GetFullPath(_storage.RootPath);
            foreach (var rel in relPaths)
            {
                var safeRel = rel.Replace('\\', '/').TrimStart('/');
                if (safeRel.Contains("..", StringComparison.Ordinal)) continue;

                var physical = Path.GetFullPath(Path.Combine(rootFull, safeRel));
                if (!physical.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase) || !File.Exists(physical))
                {
                    continue;
                }

                var entryName = $"files/{safeRel}";
                var hash = await ComputeFileSha256Async(physical, ct);
                checksums[entryName] = hash;
                var fileEntry = archive.CreateEntry(entryName, CompressionLevel.NoCompression);
                await using var inStream = File.OpenRead(physical);
                await using var outStream = fileEntry.Open();
                await inStream.CopyToAsync(outStream, ct);
            }

            var manifest = new BackupManifest(1, DateTimeOffset.UtcNow, "1.0.0", checksums);
            var manifestBytes = JsonSerializer.SerializeToUtf8Bytes(manifest, new JsonSerializerOptions { WriteIndented = true });
            var manifestEntry = archive.CreateEntry("manifest.json", CompressionLevel.Optimal);
            await using var mStream = manifestEntry.Open();
            await mStream.WriteAsync(manifestBytes, ct);
        }

        var size = new FileInfo(zipPath).Length;
        _db.BackupHistory.Add(new BackupHistory
        {
            FileName = fileName,
            SizeBytes = size,
            Status = "Completed",
            StartedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow
        });
        await _db.SaveChangesAsync(ct);

        return (zipPath, fileName, size);
    }

    public async Task<RestoreResultDto> RestoreBackupArchiveAsync(Stream zipStream, CancellationToken ct)
    {
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read, leaveOpen: true);
        if (archive.Entries.Count > MaxEntries)
        {
            throw new InvalidOperationException("Backup archive exceeds maximum allowed entry count.");
        }

        var manifestEntry = archive.GetEntry("manifest.json")
            ?? throw new InvalidOperationException("Invalid backup: manifest.json is missing.");
        var dbEntry = archive.GetEntry("database.json")
            ?? throw new InvalidOperationException("Invalid backup: database.json is missing.");

        BackupManifest manifest;
        await using (var ms = manifestEntry.Open())
        {
            manifest = await JsonSerializer.DeserializeAsync<BackupManifest>(ms, cancellationToken: ct)
                ?? throw new InvalidOperationException("Invalid manifest.json.");
        }

        long totalRead = 0;
        byte[] dbBytes;
        await using (var ds = dbEntry.Open())
        using (var mem = new MemoryStream())
        {
            await ds.CopyToAsync(mem, ct);
            dbBytes = mem.ToArray();
            totalRead += dbBytes.Length;
        }

        if (!manifest.Checksums.TryGetValue("database.json", out var expectedDbHash) ||
            !string.Equals(expectedDbHash, Convert.ToHexStringLower(SHA256.HashData(dbBytes)), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Security check failed: database.json SHA-256 checksum mismatch.");
        }

        var snapshot = JsonSerializer.Deserialize<BackupDatabaseSnapshot>(dbBytes)
            ?? throw new InvalidOperationException("Failed to deserialize database.json.");

        // Validate and stage all binary files safely before mutating DB
        var rootFull = Path.GetFullPath(_storage.RootPath);
        int filesRestored = 0;
        foreach (var entry in archive.Entries)
        {
            if (!entry.FullName.StartsWith("files/", StringComparison.OrdinalIgnoreCase) || entry.FullName.EndsWith('/'))
            {
                continue;
            }

            totalRead += entry.Length;
            if (totalRead > MaxTotalUncompressedBytes)
            {
                throw new InvalidOperationException("Backup archive exceeds safety decompression limit.");
            }

            var rel = entry.FullName["files/".Length..].Replace('\\', '/').TrimStart('/');
            if (string.IsNullOrWhiteSpace(rel) || rel.Contains("..", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Unsafe path detected in backup: {entry.FullName}");
            }

            var destPath = Path.GetFullPath(Path.Combine(rootFull, rel));
            if (!destPath.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Path traversal blocked: {entry.FullName}");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
            var tempDest = $"{destPath}.{Guid.NewGuid():N}.tmp";
            string actualHash;
            await using (var es = entry.Open())
            await using (var fs = new FileStream(tempDest, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                var buffer = new byte[81920];
                int read;
                while ((read = await es.ReadAsync(buffer, ct)) > 0)
                {
                    sha.AppendData(buffer, 0, read);
                    await fs.WriteAsync(buffer.AsMemory(0, read), ct);
                }
                actualHash = Convert.ToHexStringLower(sha.GetHashAndReset());
            }

            if (manifest.Checksums.TryGetValue(entry.FullName, out var expectedHash) &&
                !string.Equals(expectedHash, actualHash, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(tempDest);
                throw new InvalidOperationException($"Security check failed: checksum mismatch for {entry.FullName}");
            }

            File.Move(tempDest, destPath, overwrite: true);
            filesRestored++;
        }

        // Atomic database restore inside a transaction
        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        foreach (var r in snapshot.Roles)
        {
            if (!await _db.Roles.AnyAsync(x => x.Id == r.Id || x.Name == r.Name, ct))
                _db.Roles.Add(new Role { Id = r.Id, Name = r.Name, Description = r.Description, IsSystem = r.IsSystem });
        }
        await _db.SaveChangesAsync(ct);

        var roleIdMap = await _db.Roles.ToDictionaryAsync(r => r.Name, r => r.Id, ct);
        var backupRoleIdToName = snapshot.Roles.ToDictionary(r => r.Id, r => r.Name);

        int usersRestored = 0;
        foreach (var u in snapshot.Users)
        {
            var existing = await _db.Users.FirstOrDefaultAsync(x => x.Id == u.Id || x.NormalizedEmail == u.NormalizedEmail, ct);
            if (existing is null)
            {
                _db.Users.Add(new User
                {
                    Id = u.Id, Email = u.Email, NormalizedEmail = u.NormalizedEmail, DisplayName = u.DisplayName,
                    PasswordHash = u.PasswordHash, SecurityStamp = u.SecurityStamp, IsActive = u.IsActive,
                    CanUsePlayer = u.CanUsePlayer, QuotaBytes = u.QuotaBytes, ExpiresAt = u.ExpiresAt,
                    ListenQuotaSeconds = u.ListenQuotaSeconds, ListenSecondsUsed = u.ListenSecondsUsed,
                    DeactivatedAt = u.DeactivatedAt, DeactivationReason = u.DeactivationReason,
                    CreatedAt = u.CreatedAt, UpdatedAt = u.UpdatedAt
                });
                usersRestored++;
            }
        }
        await _db.SaveChangesAsync(ct);

        foreach (var ur in snapshot.UserRoles)
        {
            if (!backupRoleIdToName.TryGetValue(ur.RoleId, out var rName) || !roleIdMap.TryGetValue(rName, out var targetRoleId)) continue;
            if (!await _db.UserRoles.AnyAsync(x => x.UserId == ur.UserId && x.RoleId == targetRoleId, ct))
            {
                if (await _db.Users.AnyAsync(x => x.Id == ur.UserId, ct))
                    _db.UserRoles.Add(new UserRole { UserId = ur.UserId, RoleId = targetRoleId });
            }
        }

        foreach (var g in snapshot.Genres)
        {
            if (!await _db.Genres.AnyAsync(x => x.Id == g.Id, ct))
                _db.Genres.Add(new Genre { Id = g.Id, Name = g.Name, NormalizedName = g.NormalizedName, CreatedAt = g.CreatedAt });
        }

        int artistsRestored = 0;
        foreach (var a in snapshot.Artists)
        {
            if (!await _db.Artists.AnyAsync(x => x.Id == a.Id, ct))
            {
                _db.Artists.Add(new Artist { Id = a.Id, Name = a.Name, NormalizedName = a.NormalizedName, Biography = a.Biography, ArtworkPath = a.ArtworkPath, CreatedAt = a.CreatedAt, UpdatedAt = a.UpdatedAt });
                artistsRestored++;
            }
        }
        await _db.SaveChangesAsync(ct);

        int albumsRestored = 0;
        foreach (var al in snapshot.Albums)
        {
            if (!await _db.Albums.AnyAsync(x => x.Id == al.Id, ct))
            {
                _db.Albums.Add(new Album { Id = al.Id, Title = al.Title, NormalizedTitle = al.NormalizedTitle, AlbumArtist = al.AlbumArtist, ArtistId = al.ArtistId, Year = al.Year, Genre = al.Genre, Description = al.Description, ArtworkPath = al.ArtworkPath, IsDeleted = al.IsDeleted, CreatedAt = al.CreatedAt, UpdatedAt = al.UpdatedAt });
                albumsRestored++;
            }
        }

        foreach (var fh in snapshot.FileHashes)
        {
            if (!await _db.FileHashes.AnyAsync(x => x.Id == fh.Id, ct))
                _db.FileHashes.Add(new FileHash { Id = fh.Id, Sha256 = fh.Sha256, SizeBytes = fh.SizeBytes, CreatedAt = fh.CreatedAt });
        }
        await _db.SaveChangesAsync(ct);

        foreach (var aa in snapshot.AudioAssets)
        {
            if (!await _db.AudioAssets.AnyAsync(x => x.Id == aa.Id, ct))
                _db.AudioAssets.Add(new AudioAsset { Id = aa.Id, FileHashId = aa.FileHashId, OriginalFileName = aa.OriginalFileName, Extension = aa.Extension, MimeType = aa.MimeType, StoragePath = aa.StoragePath, SizeBytes = aa.SizeBytes, CreatedAt = aa.CreatedAt });
        }
        await _db.SaveChangesAsync(ct);

        int tracksRestored = 0;
        foreach (var t in snapshot.Tracks)
        {
            if (!await _db.Tracks.AnyAsync(x => x.Id == t.Id, ct))
            {
                _db.Tracks.Add(new Track
                {
                    Id = t.Id, Title = t.Title, NormalizedTitle = t.NormalizedTitle, ArtistDisplay = t.ArtistDisplay,
                    AlbumDisplay = t.AlbumDisplay, GenreId = t.GenreId, AudioAssetId = t.AudioAssetId, Year = t.Year,
                    TrackNumber = t.TrackNumber, DiscNumber = t.DiscNumber, DurationSeconds = t.DurationSeconds,
                    Bitrate = t.Bitrate, SampleRate = t.SampleRate, Channels = t.Channels, Codec = t.Codec,
                    ArtworkPath = t.ArtworkPath, IsDeleted = t.IsDeleted, DeletedAt = t.DeletedAt,
                    CreatedByUserId = t.CreatedByUserId, CreatedAt = t.CreatedAt, UpdatedAt = t.UpdatedAt
                });
                tracksRestored++;
            }
        }
        await _db.SaveChangesAsync(ct);

        foreach (var ta in snapshot.TrackArtists)
        {
            if (!await _db.TrackArtists.AnyAsync(x => x.TrackId == ta.TrackId && x.ArtistId == ta.ArtistId && x.Role == ta.Role, ct))
                _db.TrackArtists.Add(new TrackArtist { TrackId = ta.TrackId, ArtistId = ta.ArtistId, Role = ta.Role, Position = ta.Position });
        }
        foreach (var at in snapshot.AlbumTracks)
        {
            if (!await _db.AlbumTracks.AnyAsync(x => x.AlbumId == at.AlbumId && x.TrackId == at.TrackId, ct))
                _db.AlbumTracks.Add(new AlbumTrack { AlbumId = at.AlbumId, TrackId = at.TrackId, Position = at.Position });
        }
        foreach (var fo in snapshot.Folders)
        {
            if (!await _db.Folders.AnyAsync(x => x.Id == fo.Id, ct))
                _db.Folders.Add(new Folder { Id = fo.Id, Name = fo.Name, ParentId = fo.ParentId, Description = fo.Description, ArtworkPath = fo.ArtworkPath, CreatedByUserId = fo.CreatedByUserId, IsDeleted = fo.IsDeleted, CreatedAt = fo.CreatedAt, UpdatedAt = fo.UpdatedAt });
        }
        await _db.SaveChangesAsync(ct);

        foreach (var ft in snapshot.FolderTracks)
        {
            if (!await _db.FolderTracks.AnyAsync(x => x.FolderId == ft.FolderId && x.TrackId == ft.TrackId, ct))
                _db.FolderTracks.Add(new FolderTrack { FolderId = ft.FolderId, TrackId = ft.TrackId, AddedAt = ft.AddedAt });
        }

        int playlistsRestored = 0;
        foreach (var pl in snapshot.Playlists)
        {
            if (!await _db.Playlists.AnyAsync(x => x.Id == pl.Id, ct) && await _db.Users.AnyAsync(u => u.Id == pl.OwnerUserId, ct))
            {
                _db.Playlists.Add(new Playlist { Id = pl.Id, Name = pl.Name, Description = pl.Description, OwnerUserId = pl.OwnerUserId, IsPublic = pl.IsPublic, IsCollaborative = pl.IsCollaborative, ArtworkPath = pl.ArtworkPath, IsDeleted = pl.IsDeleted, CreatedAt = pl.CreatedAt, UpdatedAt = pl.UpdatedAt });
                playlistsRestored++;
            }
        }
        await _db.SaveChangesAsync(ct);

        foreach (var pt in snapshot.PlaylistTracks)
        {
            if (!await _db.PlaylistTracks.AnyAsync(x => x.PlaylistId == pt.PlaylistId && x.TrackId == pt.TrackId, ct) && await _db.Playlists.AnyAsync(p => p.Id == pt.PlaylistId, ct))
                _db.PlaylistTracks.Add(new PlaylistTrack { PlaylistId = pt.PlaylistId, TrackId = pt.TrackId, Position = pt.Position, AddedByUserId = pt.AddedByUserId, AddedAt = pt.AddedAt });
        }
        foreach (var fav in snapshot.Favorites)
        {
            if (!await _db.Favorites.AnyAsync(x => x.UserId == fav.UserId && x.TrackId == fav.TrackId, ct) && await _db.Users.AnyAsync(u => u.Id == fav.UserId, ct))
                _db.Favorites.Add(new Favorite { UserId = fav.UserId, TrackId = fav.TrackId, CreatedAt = fav.CreatedAt });
        }

        foreach (var tg in snapshot.TelegramConfigs)
        {
            var existingTg = await _db.TelegramConfigurations.FirstOrDefaultAsync(ct);
            var protectedToken = !string.IsNullOrWhiteSpace(tg.BotTokenPlaintext)
                ? _telegramProtector.Protect(tg.BotTokenPlaintext)
                : null;
            if (existingTg is null)
            {
                _db.TelegramConfigurations.Add(new TelegramConfiguration
                {
                    Id = tg.Id, BotTokenProtected = protectedToken, AdminTelegramUserId = tg.AdminTelegramUserId,
                    AdminChatId = tg.AdminChatId, DestinationGroupId = tg.DestinationGroupId, DestinationTopicId = tg.DestinationTopicId,
                    AutoPublishEnabled = tg.AutoPublishEnabled, IngestionEnabled = tg.IngestionEnabled,
                    MaxIncomingFileSizeBytes = tg.MaxIncomingFileSizeBytes, MaxAudioDurationSeconds = tg.MaxAudioDurationSeconds,
                    ApiEndpoint = tg.ApiEndpoint, LocalBotApiEndpoint = tg.LocalBotApiEndpoint, MaxRetryAttempts = tg.MaxRetryAttempts,
                    UpdatedAt = tg.UpdatedAt, UpdatedByUserId = tg.UpdatedByUserId
                });
            }
            else if (!string.IsNullOrWhiteSpace(protectedToken))
            {
                existingTg.BotTokenProtected = protectedToken;
                existingTg.AdminTelegramUserId = tg.AdminTelegramUserId;
                existingTg.AdminChatId = tg.AdminChatId;
                existingTg.DestinationGroupId = tg.DestinationGroupId;
                existingTg.DestinationTopicId = tg.DestinationTopicId;
                existingTg.AutoPublishEnabled = tg.AutoPublishEnabled;
                existingTg.IngestionEnabled = tg.IngestionEnabled;
                existingTg.UpdatedAt = DateTimeOffset.UtcNow;
            }
        }

        foreach (var st in snapshot.AppSettings)
        {
            var existingSt = await _db.AppSettings.FirstOrDefaultAsync(x => x.Key == st.Key, ct);
            if (existingSt is null) _db.AppSettings.Add(new AppSetting { Key = st.Key, Value = st.Value, UpdatedAt = st.UpdatedAt });
            else { existingSt.Value = st.Value; existingSt.UpdatedAt = st.UpdatedAt; }
        }

        _db.BackupHistory.Add(new BackupHistory
        {
            FileName = $"restore-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.zip",
            SizeBytes = totalRead,
            Status = "Restored",
            StartedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow
        });

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return new RestoreResultDto(
            Success: true,
            UsersRestored: usersRestored,
            ArtistsRestored: artistsRestored,
            AlbumsRestored: albumsRestored,
            TracksRestored: tracksRestored,
            PlaylistsRestored: playlistsRestored,
            FilesRestored: filesRestored,
            Message: "بازیابی اطلاعات و فایل‌ها با موفقیت و با تایید سلامت SHA-256 انجام شد.");
    }

    private static async Task<string> ComputeFileSha256Async(string path, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, ct);
        return Convert.ToHexStringLower(hash);
    }
}
