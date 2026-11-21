using Microsoft.EntityFrameworkCore;
using MusicHub.Domain;

namespace MusicHub.Infrastructure.Library;

/// <summary>The library-wide counters a report screen shows.</summary>
/// <remarks>
/// <see cref="ActiveUserCount"/> and <see cref="InactiveUserCount"/> read <see cref="User.IsActive"/>
/// directly. That flag is not a guess: <c>UserEntitlementService</c> clears it when an expiry,
/// storage or listening limit is reached, so the report shows exactly the state the server enforces.
/// <see cref="ArtistCount"/> counts artists credited on at least one live track, which is the same
/// set the artist report lists, so the two screens can never disagree.
/// </remarks>
public sealed record LibraryOverview(
    int AlbumCount,
    int TrackCount,
    int ArtistCount,
    int UserCount,
    int ActiveUserCount,
    int InactiveUserCount,
    double TotalDurationSeconds);

/// <summary>One artist in the artist report list.</summary>
public sealed record ArtistSummary(Guid Id, string Name, int TrackCount);

/// <summary>One album credited to an artist in that artist's report.</summary>
public sealed record ArtistAlbumRow(Guid Id, string Title, int TrackCount);

/// <summary>An artist's report: how much they contributed and where.</summary>
public sealed record ArtistReport(
    Guid Id,
    string Name,
    int TrackCount,
    int AlbumCount,
    double TotalDurationSeconds,
    IReadOnlyList<ArtistAlbumRow> Albums);

/// <summary>
/// Read-only library aggregates for the bot's report screens.
/// </summary>
/// <remarks>
/// This is a service rather than EF queries inside the handler because the numbers and their
/// definitions stay in one place, which is the same reason decision D-1 in
/// <c>docs/TELEGRAM_BOT.md</c> moved album writes into <see cref="AlbumService"/>. Every value is
/// read on render, so a refresh shows current state and nothing is cached or synthesised.
/// </remarks>
public sealed class LibraryReportService(MusicHubDbContext db)
{
    /// <summary>The overview counters plus the total playing time of every live track.</summary>
    public async Task<LibraryOverview> ReadOverviewAsync(CancellationToken ct)
    {
        // Sequential on purpose: one DbContext cannot run queries concurrently.
        var albumCount = await db.Albums.AsNoTracking().CountAsync(x => !x.IsDeleted, ct);
        var trackCount = await db.Tracks.AsNoTracking().CountAsync(x => !x.IsDeleted, ct);
        var artistCount = await CountArtistsAsync(ct);
        var userCount = await db.Users.AsNoTracking().CountAsync(ct);
        var activeUsers = await db.Users.AsNoTracking().CountAsync(x => x.IsActive, ct);
        var totalDuration = await db.Tracks.AsNoTracking()
            .Where(x => !x.IsDeleted)
            .Select(x => (double?)x.DurationSeconds)
            .SumAsync(ct) ?? 0d;

        return new LibraryOverview(
            albumCount,
            trackCount,
            artistCount,
            userCount,
            activeUsers,
            userCount - activeUsers,
            totalDuration);
    }

    /// <summary>
    /// Artists with at least one live track, most prolific first. Artists are counted through
    /// <see cref="TrackArtist"/> because that is the authoritative credit; a bare name string on a
    /// track is only a display hint.
    /// </summary>
    public async Task<IReadOnlyList<ArtistSummary>> ListArtistsAsync(
        int take, int skip, CancellationToken ct)
    {
        if (take <= 0) return [];

        // Counted through TrackArtist because that is the authoritative credit; a bare name string
        // on a track is only a display hint. Written as a correlated count rather than a GroupBy so
        // the projection, the ordering and the page all translate to one query.
        return await db.Artists.AsNoTracking()
            .Where(a => a.TrackArtists.Any(ta => !ta.Track.IsDeleted))
            .OrderByDescending(a => a.TrackArtists.Count(ta => !ta.Track.IsDeleted))
            .ThenBy(a => a.Name)
            .ThenBy(a => a.Id)
            .Skip(Math.Max(0, skip))
            .Take(take)
            .Select(a => new ArtistSummary(
                a.Id, a.Name, a.TrackArtists.Count(ta => !ta.Track.IsDeleted)))
            .ToListAsync(ct);
    }

    /// <summary>How many artists have at least one live track.</summary>
    public Task<int> CountArtistsAsync(CancellationToken ct) =>
        db.Artists.AsNoTracking()
            .CountAsync(a => a.TrackArtists.Any(ta => !ta.Track.IsDeleted), ct);

    /// <summary>One artist's report, or null when the artist is gone.</summary>
    public async Task<ArtistReport?> ReadArtistAsync(Guid artistId, CancellationToken ct)
    {
        var artist = await db.Artists.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == artistId, ct);
        if (artist is null) return null;

        var tracks = db.Tracks.AsNoTracking()
            .Where(x => !x.IsDeleted && x.TrackArtists.Any(a => a.ArtistId == artistId));

        var trackCount = await tracks.CountAsync(ct);
        var duration = await tracks
            .Select(x => (double?)x.DurationSeconds)
            .SumAsync(ct) ?? 0d;

        var albums = await db.Albums.AsNoTracking()
            .Where(x => !x.IsDeleted
                        && x.AlbumTracks.Any(m => !m.Track.IsDeleted
                                                  && m.Track.TrackArtists.Any(a => a.ArtistId == artistId)))
            .OrderBy(x => x.Title)
            .Select(x => new ArtistAlbumRow(
                x.Id, x.Title, x.AlbumTracks.Count(m => !m.Track.IsDeleted)))
            .Take(20)
            .ToListAsync(ct);

        var albumCount = await db.Albums.AsNoTracking()
            .CountAsync(x => !x.IsDeleted
                             && x.AlbumTracks.Any(m => !m.Track.IsDeleted
                                                       && m.Track.TrackArtists.Any(a => a.ArtistId == artistId)), ct);

        return new ArtistReport(artist.Id, artist.Name, trackCount, albumCount, duration, albums);
    }
}
