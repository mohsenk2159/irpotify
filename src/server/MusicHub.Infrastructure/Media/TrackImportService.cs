using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MusicHub.Domain;
using MusicHub.Infrastructure.Options;

namespace MusicHub.Infrastructure.Media;

public sealed record TrackImportRequest(
    Stream Content,
    string FileName,
    long DeclaredSizeBytes,
    Guid UserId,
    Guid? FolderId = null,
    Guid? AlbumId = null,
    string Source = "upload",
    string? SourceReference = null,
    long? MaximumBytes = null);

public sealed record TrackImportResult(
    string FileName,
    bool Success,
    bool Duplicate,
    string? Error,
    Guid? TrackId,
    string? Sha256,
    long StoredBytes,
    bool BadRequest = false);

/// <summary>
/// Single authorization-agnostic ingestion pipeline shared by HTTP uploads, the Telegram
/// importer and background jobs. Callers are responsible for authorizing the acting user.
/// </summary>
public sealed class TrackImportService(
    MusicHubDbContext db,
    ContentAddressedStorage storage,
    AudioProbeService probe,
    IMalwareScanner scanner,
    IOptions<StorageOptions> options,
    ITrackImportListener listener,
    ILogger<TrackImportService> logger)
{
    private readonly StorageOptions _options = options.Value;

    public async Task<TrackImportResult> ImportAsync(TrackImportRequest request, CancellationToken ct)
    {
        var originalName = ContentAddressedStorage.SafeOriginalFileName(request.FileName);
        var extension = ContentAddressedStorage.SanitizeExtension(request.FileName);
        if (string.IsNullOrEmpty(extension))
        {
            return new TrackImportResult(originalName, false, false, "Unsupported audio extension.", null, null, 0, true);
        }

        var userId = request.UserId;
        var maximumBytes = Math.Min(request.MaximumBytes ?? _options.MaxUploadBytes, _options.MaxUploadBytes);

        var user = await db.Users.AsNoTracking().Where(x => x.Id == userId)
            .Select(x => new { x.QuotaBytes, x.IsActive }).SingleOrDefaultAsync(ct);
        if (user is null || !user.IsActive)
        {
            return new TrackImportResult(originalName, false, false, "The importing user is not active.", null, null, 0, true);
        }

        var usedBytes = await db.Tracks.Where(x => x.CreatedByUserId == userId && !x.IsDeleted)
            .SumAsync(x => (long?)x.AudioAsset.SizeBytes, ct) ?? 0;
        // QuotaBytes == 0 means unlimited (the dashboard labels it "Unlimited").
        if (user.QuotaBytes > 0 && request.DeclaredSizeBytes > 0 && usedBytes + request.DeclaredSizeBytes > user.QuotaBytes)
        {
            return new TrackImportResult(originalName, false, false, "User storage quota exceeded.", null, null, 0);
        }
        if (request.DeclaredSizeBytes > maximumBytes)
        {
            return new TrackImportResult(originalName, false, false, "File exceeds the maximum allowed size.", null, null, 0);
        }

        var session = new UploadSession
        {
            UserId = userId,
            OriginalFileName = originalName,
            ExpectedSizeBytes = request.DeclaredSizeBytes,
            Status = UploadStatus.Validating
        };
        db.UploadSessions.Add(session);

        ImportJob? job = null;
        if (!string.Equals(request.Source, "upload", StringComparison.OrdinalIgnoreCase))
        {
            job = new ImportJob
            {
                Source = request.Source,
                SourceReference = request.SourceReference ?? string.Empty,
                Status = JobStatus.Processing,
                Attempts = 1
            };
            db.ImportJobs.Add(job);
        }

        await db.SaveChangesAsync(ct);

        string? quarantine = null;
        // Declared outside the try so every rollback path can delete a file this call committed
        // (CR-024).
        string? committedPath = null;
        Track? createdTrack = null;
        try
        {
            var receiveLimit = maximumBytes;
            if (user.QuotaBytes > 0)
            {
                receiveLimit = Math.Min(Math.Max(0, user.QuotaBytes - usedBytes), maximumBytes);
            }
            var received = await storage.ReceiveToQuarantineAsync(request.Content, extension, receiveLimit, ct);
            quarantine = received.QuarantinePath;
            session.ReceivedBytes = received.SizeBytes;
            session.Sha256 = received.Sha256;
            session.QuarantinePath = received.QuarantinePath;
            await db.SaveChangesAsync(ct);

            await storage.VerifySignatureAsync(quarantine, extension, ct);
            var metadata = await probe.ProbeAsync(quarantine, ct);
            if (metadata.DurationSeconds > 12 * 60 * 60)
            {
                throw new InvalidAudioException("Audio duration exceeds the 12 hour safety limit.");
            }

            var scan = await scanner.ScanAsync(quarantine, ct);
            if (!scan.Available && _options.RequireMalwareScan)
            {
                throw new ApiFailureException(503, "Malware scanning is required but the scanner is unavailable. Import rejected.");
            }
            if (scan.Available && !scan.Clean)
            {
                throw new InvalidAudioException("Malware scan did not report the file as clean: " + scan.Message);
            }

            var existing = await db.AudioAssets
                .Include(x => x.FileHash)
                .Include(x => x.Tracks).ThenInclude(x => x.Genre)
                .FirstOrDefaultAsync(x => x.FileHash.Sha256 == session.Sha256, ct);
            var existingTrack = existing?.Tracks.FirstOrDefault(x => !x.IsDeleted);
            if (existingTrack is not null)
            {
                db.DuplicateReports.Add(new DuplicateReport
                {
                    UserId = userId,
                    ExistingTrackId = existingTrack.Id,
                    IncomingFileName = originalName,
                    Sha256 = session.Sha256!
                });
                session.Status = UploadStatus.Duplicate;
                session.TrackId = existingTrack.Id;
                session.CompletedAt = DateTimeOffset.UtcNow;
                if (job is not null)
                {
                    job.Status = JobStatus.Completed;
                    job.TrackId = existingTrack.Id;
                    job.CompletedAt = DateTimeOffset.UtcNow;
                }
                await db.SaveChangesAsync(ct);
                ContentAddressedStorage.TryDelete(quarantine);
                await listener.OnTrackImportedAsync(new TrackImportResult(originalName, true, true, null, existingTrack.Id, session.Sha256, received.SizeBytes), ct);
                return new TrackImportResult(originalName, true, true, null, existingTrack.Id, session.Sha256, received.SizeBytes);
            }

            var commit = await storage.CommitAsync(quarantine, session.Sha256!, extension, originalName, ct);
            var relativePath = commit.RelativePath;
            quarantine = null;
            // CommitAsync moves the quarantine file into the store before the transaction opens, so
            // the committed path must be tracked separately: on any rollback that is not the
            // SHA-256 race the old code tried to delete `quarantine`, which is already null, and
            // left the audio behind with no row referencing it (CR-024). Only a file this call
            // created is ours to remove; a pre-existing one belongs to another asset.
            committedPath = commit.CreatedFile ? storage.ResolveMusicPath(relativePath) : null;

            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            try
            {
                // Re-assert the quota inside the transaction. The pre-upload check reads usage
                // before the bytes land, so parallel uploads each passed the check and together
                // overshot the allowance (CR-010).
                //
                // Re-reading alone is not enough: under ReadCommitted two transactions still see
                // the same baseline and both commit. Taking the account's row lock first makes
                // concurrent imports for the same user take turns, so the one that arrives second
                // sees the first one's committed bytes and fails here instead of silently
                // exceeding the allowance. The lock is released when this transaction ends.
                //
                // The quota is read *after* the lock, not from the pre-upload snapshot, so a limit
                // tightened while the bytes were in flight is still enforced.
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT 1 FROM \"Users\" WHERE \"Id\" = {userId} FOR UPDATE", ct);
                var quotaBytes = await db.Users.AsNoTracking()
                    .Where(x => x.Id == userId).Select(x => x.QuotaBytes).SingleAsync(ct);
                if (quotaBytes > 0)
                {
                    var usedNow = await db.Tracks.Where(x => x.CreatedByUserId == userId && !x.IsDeleted)
                        .SumAsync(x => (long?)x.AudioAsset.SizeBytes, ct) ?? 0;
                    if (usedNow + session.ReceivedBytes > quotaBytes)
                    {
                        throw new InvalidAudioException("User storage quota exceeded.");
                    }
                }
                var fileHash = existing?.FileHash ?? new FileHash { Sha256 = session.Sha256!, SizeBytes = session.ReceivedBytes };
                var asset = existing;
                if (asset is null)
                {
                    db.FileHashes.Add(fileHash);
                    asset = new AudioAsset
                    {
                        FileHash = fileHash,
                        OriginalFileName = originalName,
                        Extension = extension,
                        MimeType = MimeForExtension(extension),
                        StoragePath = relativePath,
                        SizeBytes = session.ReceivedBytes
                    };
                    db.AudioAssets.Add(asset);
                }

                createdTrack = await BuildTrackAsync(db, asset, metadata, request.FolderId, request.AlbumId, userId, relativePath, ct);
                db.Tracks.Add(createdTrack);

                db.OutboxMessages.Add(new OutboxMessage
                {
                    Type = "TrackAdded",
                    PayloadJson = JsonSerializer.Serialize(new { trackId = createdTrack.Id, sha256 = session.Sha256, source = request.Source })
                });

                var telegram = await db.TelegramConfigurations.AsNoTracking().FirstOrDefaultAsync(ct);
                if (telegram is { AutoPublishEnabled: true, DestinationGroupId: not null, BotTokenProtected: not null })
                {
                    db.TelegramPublications.Add(new TelegramPublication
                    {
                        TrackId = createdTrack.Id,
                        ChatId = telegram.DestinationGroupId,
                        Status = TelegramPublishStatus.Pending,
                        NextAttemptAt = DateTimeOffset.UtcNow
                    });
                }

                session.Status = UploadStatus.Stored;
                session.TrackId = createdTrack.Id;
                session.CompletedAt = DateTimeOffset.UtcNow;
                if (job is not null)
                {
                    job.Status = JobStatus.Completed;
                    job.TrackId = createdTrack.Id;
                    job.CompletedAt = DateTimeOffset.UtcNow;
                }
                await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
            }
            catch (DbUpdateException ex) when (IsUniqueHashViolation(ex))
            {
                await transaction.RollbackAsync(ct);
                db.ChangeTracker.Clear();
                // The row was not written, so the file this call committed is now unreferenced.
                if (committedPath is not null) ContentAddressedStorage.TryDelete(committedPath);
                var duplicate = await db.AudioAssets.Include(x => x.FileHash).Include(x => x.Tracks)
                    .FirstOrDefaultAsync(x => x.FileHash.Sha256 == session.Sha256, ct);
                var duplicateTrack = duplicate?.Tracks.FirstOrDefault(x => !x.IsDeleted);
                var persistedSession = await db.UploadSessions.SingleAsync(x => x.Id == session.Id, ct);
                persistedSession.Status = UploadStatus.Duplicate;
                persistedSession.TrackId = duplicateTrack?.Id;
                persistedSession.CompletedAt = DateTimeOffset.UtcNow;
                db.DuplicateReports.Add(new DuplicateReport
                {
                    UserId = userId,
                    ExistingTrackId = duplicateTrack?.Id,
                    IncomingFileName = originalName,
                    Sha256 = session.Sha256!
                });
                if (job is not null)
                {
                    var persistedJob = await db.ImportJobs.SingleOrDefaultAsync(x => x.Id == job.Id, ct);
                    if (persistedJob is not null)
                    {
                        persistedJob.Status = JobStatus.Completed;
                        persistedJob.TrackId = duplicateTrack?.Id;
                        persistedJob.CompletedAt = DateTimeOffset.UtcNow;
                    }
                }
                await db.SaveChangesAsync(ct);
                var sha = session.Sha256;
                if (duplicateTrack is null)
                {
                    const string message = "An identical file already exists, but no active track references it.";
                    await listener.OnImportFailedAsync(originalName, message, ct);
                    return new TrackImportResult(originalName, false, true, message, null, sha, 0);
                }
                await listener.OnTrackImportedAsync(new TrackImportResult(originalName, true, true, null, duplicateTrack.Id, sha, 0), ct);
                return new TrackImportResult(originalName, true, true, null, duplicateTrack.Id, sha, 0);
            }

            var result = new TrackImportResult(originalName, true, false, null, createdTrack.Id, session.Sha256, session.ReceivedBytes);
            await listener.OnTrackImportedAsync(result, ct);
            return result;
        }
        catch (Exception ex) when (ex is MediaValidationException or ApiFailureException or DbUpdateException)
        {
            ContentAddressedStorage.TryDelete(quarantine);
            // A database failure must not echo Npgsql/PostgreSQL text - constraint names, table and
            // column names, and sometimes the failing statement - into the HTTP response (CR-022).
            // The detail is logged and kept on the upload session for operators.
            var clientMessage = ex is DbUpdateException ? "The file could not be stored." : ex.Message;
            // `committedPath` is non-null only when this call created the file, so any failure after
            // the commit - including the in-transaction quota re-assert - removes it instead of
            // leaving audio behind that no row references (CR-024).
            ContentAddressedStorage.TryDelete(committedPath);
            db.ChangeTracker.Clear();
            await PersistFailureAsync(session.Id, job?.Id, ex.Message, ct);
            await listener.OnImportFailedAsync(originalName, clientMessage, ct);
            logger.LogInformation(ex, "Import rejected for {FileName} from {Source}", originalName, request.Source);
            return new TrackImportResult(originalName, false, false, clientMessage, null, null, 0);
        }
        catch (OperationCanceledException)
        {
            ContentAddressedStorage.TryDelete(quarantine);
            ContentAddressedStorage.TryDelete(committedPath);
            throw;
        }
        catch (Exception ex)
        {
            ContentAddressedStorage.TryDelete(quarantine);
            // Same rule as above: the operator sees the detail in the log, the caller gets a fixed
            // message (CR-022).
            ContentAddressedStorage.TryDelete(committedPath);
            db.ChangeTracker.Clear();
            await PersistFailureAsync(session.Id, job?.Id, "Import failed: " + ex.Message, ct);
            logger.LogError(ex, "Import failed for {FileName} from {Source}", originalName, request.Source);
            return new TrackImportResult(originalName, false, false, "The file could not be imported.", null, null, 0);
        }
    }

    private async Task PersistFailureAsync(Guid sessionId, Guid? jobId, string message, CancellationToken ct)
    {
        var trimmed = message.Length > 1000 ? message[..1000] : message;
        var persistedSession = await db.UploadSessions.SingleOrDefaultAsync(x => x.Id == sessionId, ct);
        if (persistedSession is not null)
        {
            persistedSession.Status = UploadStatus.Failed;
            persistedSession.Error = trimmed;
            persistedSession.CompletedAt = DateTimeOffset.UtcNow;
        }
        if (jobId is not null)
        {
            var job = await db.ImportJobs.SingleOrDefaultAsync(x => x.Id == jobId, ct);
            if (job is not null)
            {
                job.Status = JobStatus.Failed;
                job.Error = trimmed;
                job.CompletedAt = DateTimeOffset.UtcNow;
            }
        }
        await db.SaveChangesAsync(ct);
    }

    private async Task<Track> BuildTrackAsync(MusicHubDbContext context, AudioAsset asset, AudioMetadata metadata, Guid? folderId, Guid? albumId, Guid userId, string relativePath, CancellationToken ct)
    {
        var title = string.IsNullOrWhiteSpace(metadata.Title) ? Path.GetFileNameWithoutExtension(asset.OriginalFileName) : metadata.Title;
        var artist = await GetOrCreateArtistAsync(context, string.IsNullOrWhiteSpace(metadata.Artist) ? "Unknown Artist" : metadata.Artist, ct);
        var genre = string.IsNullOrWhiteSpace(metadata.Genre) ? null : await GetOrCreateGenreAsync(context, metadata.Genre, ct);

        var track = new Track
        {
            Title = title.Length > 400 ? title[..400] : title,
            NormalizedTitle = title.ToLowerInvariant(),
            ArtistDisplay = metadata.Artist,
            AlbumDisplay = metadata.Album,
            Genre = genre,
            AudioAsset = asset,
            Year = metadata.Year,
            TrackNumber = metadata.TrackNumber,
            DiscNumber = metadata.DiscNumber,
            DurationSeconds = metadata.DurationSeconds,
            Bitrate = metadata.Bitrate is > int.MaxValue ? int.MaxValue : (int?)metadata.Bitrate,
            SampleRate = metadata.SampleRate,
            Channels = metadata.Channels,
            Codec = metadata.Codec,
            CreatedByUserId = userId
        };
        track.TrackArtists.Add(new TrackArtist { Track = track, Artist = artist, Role = "Primary", Position = 0 });

        if (folderId is not null) track.FolderTracks.Add(new FolderTrack { Track = track, FolderId = folderId.Value });

        if (albumId is not null)
        {
            var next = (await context.AlbumTracks.Where(x => x.AlbumId == albumId).Select(x => (int?)x.Position).MaxAsync(ct) ?? 0) + 1;
            track.AlbumTracks.Add(new AlbumTrack { Track = track, AlbumId = albumId.Value, Position = metadata.TrackNumber ?? next });
        }
        else if (!string.IsNullOrWhiteSpace(metadata.Album))
        {
            var normalized = metadata.Album.ToLowerInvariant();
            var album = await context.Albums.FirstOrDefaultAsync(x => !x.IsDeleted && x.NormalizedTitle == normalized && x.AlbumArtist == metadata.AlbumArtist, ct);
            if (album is null)
            {
                album = new Album
                {
                    Title = metadata.Album,
                    NormalizedTitle = normalized,
                    AlbumArtist = string.IsNullOrWhiteSpace(metadata.AlbumArtist) ? metadata.Artist : metadata.AlbumArtist,
                    Artist = artist,
                    Year = metadata.Year,
                    Genre = metadata.Genre
                };
                context.Albums.Add(album);
            }
            track.AlbumTracks.Add(new AlbumTrack { Track = track, Album = album, Position = metadata.TrackNumber ?? 1 });
        }

        var absolute = storage.ResolveMusicPath(relativePath);
        var artwork = await probe.TryExtractArtworkAsync(absolute, track.Id, storage.ArtworkRoot, ct);
        if (artwork is not null)
        {
            track.ArtworkPath = artwork;
            // Album artwork is only ever extracted per track, so it was never populated anywhere and
            // the album/artist grids rendered the placeholder forever (CR-014). Seed it from the
            // first track that has a cover; later tracks keep the existing album cover.
            var album = track.AlbumTracks.Select(x => x.Album).FirstOrDefault();
            if (album is not null && album.ArtworkPath is null) album.ArtworkPath = artwork;
            var primaryArtist = track.TrackArtists.Select(x => x.Artist).FirstOrDefault();
            if (primaryArtist is not null && primaryArtist.ArtworkPath is null) primaryArtist.ArtworkPath = artwork;
        }
        return track;
    }

    private static async Task<Artist> GetOrCreateArtistAsync(MusicHubDbContext context, string name, CancellationToken ct)
    {
        var trimmed = name.Trim();
        var normalized = trimmed.ToLowerInvariant();
        var existing = await context.Artists.SingleOrDefaultAsync(x => x.NormalizedName == normalized, ct);
        if (existing is not null) return existing;
        var display = trimmed[..Math.Min(trimmed.Length, 300)];
        var key = normalized[..Math.Min(normalized.Length, 300)];
        // Concurrent imports of untagged files all resolve the same fallback artist, so two of them
        // read "missing" and both insert; the loser then failed the unique index and lost the whole
        // upload as "The file could not be stored." Insert-or-ignore is atomic, and the re-read picks
        // up whichever transaction won.
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"""
             INSERT INTO "Artists" ("Id", "Name", "NormalizedName", "CreatedAt", "UpdatedAt")
             VALUES ({Guid.NewGuid()}, {display}, {key}, now(), now())
             ON CONFLICT ("NormalizedName") DO NOTHING
             """, ct);
        return await context.Artists.SingleAsync(x => x.NormalizedName == key, ct);
    }

    private static async Task<Genre> GetOrCreateGenreAsync(MusicHubDbContext context, string name, CancellationToken ct)
    {
        var trimmed = name.Trim();
        var normalized = trimmed.ToLowerInvariant();
        var existing = await context.Genres.SingleOrDefaultAsync(x => x.NormalizedName == normalized, ct);
        if (existing is not null) return existing;
        var display = trimmed[..Math.Min(trimmed.Length, 160)];
        var key = normalized[..Math.Min(normalized.Length, 160)];
        // Same race as the artist path above.
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"""
             INSERT INTO "Genres" ("Id", "Name", "NormalizedName", "CreatedAt")
             VALUES ({Guid.NewGuid()}, {display}, {key}, now())
             ON CONFLICT ("NormalizedName") DO NOTHING
             """, ct);
        return await context.Genres.SingleAsync(x => x.NormalizedName == key, ct);
    }

    private static bool IsUniqueHashViolation(DbUpdateException ex)
    {
        return ex.InnerException is Npgsql.PostgresException pg && pg.SqlState == Npgsql.PostgresErrorCodes.UniqueViolation &&
               (pg.ConstraintName?.Contains("FileHashes", StringComparison.OrdinalIgnoreCase) == true || pg.ConstraintName?.Contains("Sha256", StringComparison.OrdinalIgnoreCase) == true);
    }

    private static string MimeForExtension(string extension) => extension switch
    {
        ".mp3" => "audio/mpeg",
        ".flac" => "audio/flac",
        ".wav" => "audio/wav",
        ".m4a" => "audio/mp4",
        ".aac" => "audio/aac",
        ".ogg" => "audio/ogg",
        ".opus" => "audio/ogg",
        _ => "application/octet-stream"
    };
}

public interface ITrackImportListener
{
    Task OnTrackImportedAsync(TrackImportResult result, CancellationToken ct);
    Task OnImportFailedAsync(string fileName, string error, CancellationToken ct);
}

public sealed class NullTrackImportListener : ITrackImportListener
{
    public Task OnTrackImportedAsync(TrackImportResult result, CancellationToken ct) => Task.CompletedTask;
    public Task OnImportFailedAsync(string fileName, string error, CancellationToken ct) => Task.CompletedTask;
}

/// <summary>Service-layer failure that maps to a specific HTTP status at the edge.</summary>
public sealed class ApiFailureException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}
