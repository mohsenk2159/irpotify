using Microsoft.EntityFrameworkCore;
using MusicHub.Domain;

namespace MusicHub.Infrastructure.Library;

/// <summary>The album operations the admin HTTP endpoints and the Telegram bot both perform.</summary>
/// <param name="Succeeded">Whether the operation was applied.</param>
/// <param name="AlbumId">The album that was created or targeted, when known.</param>
/// <param name="AlbumTitle">The album title, for an operator-facing confirmation.</param>
/// <param name="Error">Why the request was refused, in operator language.</param>
public sealed record AlbumOperationResult(bool Succeeded, Guid? AlbumId, string? AlbumTitle, string? Error)
{
    public static AlbumOperationResult Ok(Guid albumId, string title) => new(true, albumId, title, null);

    public static AlbumOperationResult Fail(string error) => new(false, null, null, error);
}

/// <summary>One album as a picker or list shows it.</summary>
public sealed record AlbumSummary(Guid Id, string Title, string AlbumArtist, int TrackCount);

/// <summary>One track inside an album, as the album screens list it.</summary>
public sealed record AlbumTrackRow(
    Guid Id,
    string Title,
    string Artist,
    double DurationSeconds,
    int Position);

/// <summary>How many tracks of one album come from one artist.</summary>
public sealed record AlbumArtistShare(string Name, int TrackCount);

/// <summary>The read-only facts an album report shows, plus its paged track rows.</summary>
public sealed record AlbumReport(
    Guid Id,
    string Title,
    string AlbumArtist,
    int? Year,
    int TrackCount,
    double TotalDurationSeconds,
    IReadOnlyList<AlbumArtistShare> Artists);

/// <summary>The outcome of taking one track out of one album.</summary>
public sealed record AlbumTrackOperationResult(
    bool Succeeded,
    string? TrackTitle,
    string? AlbumTitle,
    string? Error)
{
    public static AlbumTrackOperationResult Ok(string trackTitle, string albumTitle) =>
        new(true, trackTitle, albumTitle, null);

    public static AlbumTrackOperationResult Fail(string error) => new(false, null, null, error);
}

/// <summary>
/// The single implementation of "create an album" and "put a track in an album".
/// </summary>
/// <remarks>
/// <para>
/// This exists because two front ends ask for the same thing: the admin HTTP endpoints in
/// <c>LibraryEndpoints</c> and the Telegram bot. Keeping one copy of the rules is decision D-1
/// option B in <c>docs/TELEGRAM_BOT.md</c>; letting the bot run its own EF queries (option C) was
/// rejected because the authorization and validation rules would drift apart.
/// </para>
/// <para>
/// The service is authorization-agnostic on purpose. Callers authorize first - the endpoints with
/// the Admin policy, the bot with <c>TelegramBotAdminGate</c> - and only then call in. Audit rows
/// and realtime notifications also stay with the caller, because they are transport concerns
/// (<c>AuditWriter</c> needs the caller's IP and <c>RealtimeNotifier</c> is an API-layer singleton).
/// </para>
/// </remarks>
public sealed class AlbumService(MusicHubDbContext db)
{
    /// <summary>Album titles are stored truncated to this length, matching the HTTP contract.</summary>
    public const int MaxTitleLength = 400;

    /// <summary>
    /// Creates an album. A blank title is refused rather than stored, and the artist falls back to
    /// "Various Artists" exactly as the HTTP endpoint has always done.
    /// </summary>
    public async Task<AlbumOperationResult> CreateAsync(
        string? title,
        string? albumArtist,
        Guid? artistId,
        int? year,
        string? genre,
        string? description,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return AlbumOperationResult.Fail("Album title is required.");
        }

        var trimmed = title.Trim();
        var album = new Album
        {
            Title = trimmed[..Math.Min(trimmed.Length, MaxTitleLength)],
            NormalizedTitle = trimmed.ToLowerInvariant(),
            AlbumArtist = (albumArtist ?? "Various Artists").Trim(),
            Year = year,
            Genre = genre,
            Description = description
        };

        if (artistId is not null)
        {
            album.Artist = await db.Artists.FindAsync([artistId.Value], ct);
        }

        db.Albums.Add(album);
        await db.SaveChangesAsync(ct);
        return AlbumOperationResult.Ok(album.Id, album.Title);
    }

    /// <summary>
    /// Adds a track to an album, appending it at the end unless a position is given, and mirrors the
    /// album title onto the track's denormalized <c>AlbumDisplay</c>. Idempotent for an existing
    /// membership, which is what makes a repeated button tap harmless.
    /// </summary>
    public async Task<AlbumOperationResult> AssignTrackAsync(
        Guid albumId,
        Guid trackId,
        int? position,
        CancellationToken ct)
    {
        var album = await db.Albums.SingleOrDefaultAsync(x => x.Id == albumId && !x.IsDeleted, ct);
        if (album is null) return AlbumOperationResult.Fail("The album does not exist.");

        var track = await db.Tracks.SingleOrDefaultAsync(x => x.Id == trackId && !x.IsDeleted, ct);
        if (track is null) return AlbumOperationResult.Fail("The track does not exist.");

        var existing = await db.AlbumTracks
            .SingleOrDefaultAsync(x => x.AlbumId == albumId && x.TrackId == trackId, ct);

        if (existing is null)
        {
            var next = position
                ?? (await db.AlbumTracks.Where(x => x.AlbumId == albumId)
                        .Select(x => (int?)x.Position).MaxAsync(ct) ?? 0) + 1;
            db.AlbumTracks.Add(new AlbumTrack { AlbumId = albumId, TrackId = trackId, Position = next });
        }
        else if (position is not null)
        {
            existing.Position = position.Value;
        }

        track.AlbumDisplay = album.Title;
        await db.SaveChangesAsync(ct);
        return AlbumOperationResult.Ok(album.Id, album.Title);
    }

    /// <summary>
    /// The albums a picker can offer, newest first so a just-created album is easy to find. Deleted
    /// albums are never listed.
    /// </summary>
    /// <param name="take">Page size.</param>
    /// <param name="skip">Albums to skip, so page 2 shows the next page and not page 1 again.</param>
    public async Task<IReadOnlyList<AlbumSummary>> ListAsync(int take, int skip, CancellationToken ct)
    {
        if (take <= 0) return [];

        var rows = await db.Albums.AsNoTracking()
            .Where(x => !x.IsDeleted)
            .OrderByDescending(x => x.CreatedAt)
            .ThenBy(x => x.Id)
            .Skip(Math.Max(0, skip))
            .Take(take)
            .Select(x => new AlbumSummary(
                x.Id,
                x.Title,
                x.AlbumArtist,
                x.AlbumTracks.Count(m => !m.Track.IsDeleted)))
            .ToListAsync(ct);
        return rows;
    }

    /// <summary>Total live albums, so a picker can say "showing 8 of 23".</summary>
    public Task<int> CountAsync(CancellationToken ct) =>
        db.Albums.AsNoTracking().CountAsync(x => !x.IsDeleted, ct);

    /// <summary>
    /// Finds an album by title, case-insensitively, so the bot can reuse an existing album instead
    /// of creating a near-duplicate when the operator types a name that already exists.
    /// </summary>
    public Task<Album?> FindByTitleAsync(string title, CancellationToken ct)
    {
        var normalized = title.Trim().ToLowerInvariant();
        return db.Albums.FirstOrDefaultAsync(x => !x.IsDeleted && x.NormalizedTitle == normalized, ct);
    }

    // ---- album editing and album report -------------------------------------------------------

    /// <summary>The live album's title, or null when it is gone.</summary>
    public Task<string?> GetTitleAsync(Guid albumId, CancellationToken ct) =>
        db.Albums.AsNoTracking()
            .Where(x => x.Id == albumId && !x.IsDeleted)
            .Select(x => x.Title)
            .SingleOrDefaultAsync(ct);

    /// <summary>The live track's title, or null when it is gone.</summary>
    public Task<string?> GetTrackTitleAsync(Guid trackId, CancellationToken ct) =>
        db.Tracks.AsNoTracking()
            .Where(x => x.Id == trackId && !x.IsDeleted)
            .Select(x => x.Title)
            .SingleOrDefaultAsync(ct);

    /// <summary>
    /// Everything an album screen or report shows, or null when the album is gone. Track counts and
    /// durations only include live tracks, so a soft-deleted track never inflates the numbers.
    /// </summary>
    public async Task<AlbumReport?> ReadReportAsync(Guid albumId, CancellationToken ct)
    {
        var album = await db.Albums.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == albumId && !x.IsDeleted, ct);
        if (album is null) return null;

        var tracks = db.AlbumTracks.AsNoTracking()
            .Where(x => x.AlbumId == albumId && !x.Track.IsDeleted);

        var trackCount = await tracks.CountAsync(ct);
        var duration = await tracks
            .Select(x => (double?)x.Track.DurationSeconds)
            .SumAsync(ct) ?? 0d;

        // Artist shares come from the same join the library uses. Reading the names and counting
        // them here keeps the query simple and an album is small by nature; a track with two
        // credited artists therefore counts once for each.
        var names = await tracks
            .SelectMany(x => x.Track.TrackArtists.Select(ta => ta.Artist.Name))
            .ToListAsync(ct);

        var shares = names
            .GroupBy(name => name, StringComparer.OrdinalIgnoreCase)
            .Select(group => new AlbumArtistShare(group.Key, group.Count()))
            .OrderByDescending(x => x.TrackCount)
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .Take(6)
            .ToList();

        return new AlbumReport(album.Id, album.Title, album.AlbumArtist, album.Year, trackCount, duration, shares);
    }

    /// <summary>One page of an album's live tracks, in album order.</summary>
    public async Task<IReadOnlyList<AlbumTrackRow>> ListTracksAsync(
        Guid albumId, int take, int skip, CancellationToken ct)
    {
        if (take <= 0) return [];

        return await db.AlbumTracks.AsNoTracking()
            .Where(x => x.AlbumId == albumId && !x.Track.IsDeleted)
            .OrderBy(x => x.Position)
            .ThenBy(x => x.Track.Title)
            .ThenBy(x => x.TrackId)
            .Skip(Math.Max(0, skip))
            .Take(take)
            .Select(x => new AlbumTrackRow(
                x.TrackId, x.Track.Title, x.Track.ArtistDisplay, x.Track.DurationSeconds, x.Position))
            .ToListAsync(ct);
    }

    /// <summary>Total live tracks in the album, so a screen can say "page 1 of 2".</summary>
    public Task<int> CountTracksAsync(Guid albumId, CancellationToken ct) =>
        db.AlbumTracks.AsNoTracking().CountAsync(x => x.AlbumId == albumId && !x.Track.IsDeleted, ct);

    /// <summary>Live tracks that are not in this album yet, newest first, for the "add track" screen.</summary>
    public async Task<IReadOnlyList<AlbumTrackRow>> ListCandidatesAsync(
        Guid albumId, int take, int skip, CancellationToken ct)
    {
        if (take <= 0) return [];

        return await db.Tracks.AsNoTracking()
            .Where(x => !x.IsDeleted && !x.AlbumTracks.Any(a => a.AlbumId == albumId))
            .OrderByDescending(x => x.CreatedAt)
            .ThenBy(x => x.Title)
            .ThenBy(x => x.Id)
            .Skip(Math.Max(0, skip))
            .Take(take)
            .Select(x => new AlbumTrackRow(
                x.Id, x.Title, x.ArtistDisplay, x.DurationSeconds, x.TrackNumber ?? 0))
            .ToListAsync(ct);
    }

    /// <summary>How many live tracks the album could still accept.</summary>
    public Task<int> CountCandidatesAsync(Guid albumId, CancellationToken ct) =>
        db.Tracks.AsNoTracking()
            .CountAsync(x => !x.IsDeleted && !x.AlbumTracks.Any(a => a.AlbumId == albumId), ct);

    /// <summary>
    /// Takes a track out of an album. Only the membership goes: the audio, the track row and any
    /// other album membership stay. The denormalized <c>AlbumDisplay</c> is cleared when this was
    /// the track's last album and it still pointed at this one, so the display never claims an
    /// album the track is no longer in.
    /// </summary>
    public async Task<AlbumTrackOperationResult> RemoveTrackAsync(
        Guid albumId, Guid trackId, CancellationToken ct)
    {
        var album = await db.Albums.SingleOrDefaultAsync(x => x.Id == albumId && !x.IsDeleted, ct);
        if (album is null) return AlbumTrackOperationResult.Fail("The album does not exist.");

        var membership = await db.AlbumTracks
            .SingleOrDefaultAsync(x => x.AlbumId == albumId && x.TrackId == trackId, ct);
        if (membership is null) return AlbumTrackOperationResult.Fail("That track is not in this album.");

        db.AlbumTracks.Remove(membership);

        var track = await db.Tracks.SingleOrDefaultAsync(x => x.Id == trackId, ct);
        if (track is not null)
        {
            var stillInAnotherAlbum = await db.AlbumTracks
                .AnyAsync(x => x.TrackId == trackId && x.AlbumId != albumId, ct);
            if (!stillInAnotherAlbum && string.Equals(track.AlbumDisplay, album.Title, StringComparison.Ordinal))
            {
                track.AlbumDisplay = string.Empty;
            }
        }

        await db.SaveChangesAsync(ct);
        return AlbumTrackOperationResult.Ok(track?.Title ?? string.Empty, album.Title);
    }
}
