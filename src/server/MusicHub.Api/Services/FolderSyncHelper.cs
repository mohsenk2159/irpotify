using Microsoft.EntityFrameworkCore;
using MusicHub.Domain;
using MusicHub.Infrastructure;

namespace MusicHub.Api.Services;

/// <summary>
/// Synchronizes the hierarchical folder structure (Root Folder = Artist, Child Folder = Album)
/// with the Artists and Albums catalog tables automatically.
/// </summary>
public static class FolderSyncHelper
{
    public static async Task<(Artist Artist, Album? Album)> EnsureArtistAndAlbumAsync(
        MusicHubDbContext db,
        Folder folder,
        CancellationToken ct)
    {
        var root = await ResolveRootFolderAsync(db, folder, ct);
        var artistName = root.Name.Trim();
        var normArtist = artistName.ToLowerInvariant();

        var artist = await db.Artists.FirstOrDefaultAsync(
            x => x.NormalizedName == normArtist, ct);

        if (artist is null)
        {
            artist = new Artist
            {
                Name = artistName,
                NormalizedName = normArtist,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            db.Artists.Add(artist);
            await db.SaveChangesAsync(ct);
        }

        Album? album = null;
        if (folder.Id != root.Id)
        {
            var albumTitle = folder.Name.Trim();
            var normAlbum = albumTitle.ToLowerInvariant();

            album = await db.Albums.FirstOrDefaultAsync(
                x => !x.IsDeleted && x.NormalizedTitle == normAlbum && (x.ArtistId == artist.Id || x.ArtistId == null),
                ct);

            if (album is null)
            {
                album = new Album
                {
                    Title = albumTitle,
                    NormalizedTitle = normAlbum,
                    ArtistId = artist.Id,
                    AlbumArtist = artist.Name,
                    CreatedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow
                };
                db.Albums.Add(album);
                await db.SaveChangesAsync(ct);
            }
            else if (album.ArtistId is null)
            {
                album.ArtistId = artist.Id;
                album.AlbumArtist = artist.Name;
                album.UpdatedAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync(ct);
            }
        }

        return (artist, album);
    }

    public static async Task SyncRenameAsync(
        MusicHubDbContext db,
        Folder folder,
        string oldName,
        CancellationToken ct)
    {
        var oldNorm = oldName.Trim().ToLowerInvariant();
        var newName = folder.Name.Trim();
        var newNorm = newName.ToLowerInvariant();
        if (string.Equals(oldNorm, newNorm, StringComparison.Ordinal))
        {
            return;
        }

        if (folder.ParentId is null)
        {
            var artist = await db.Artists.FirstOrDefaultAsync(x => x.NormalizedName == oldNorm, ct);
            if (artist is not null && !await db.Artists.AnyAsync(x => x.NormalizedName == newNorm && x.Id != artist.Id, ct))
            {
                artist.Name = newName;
                artist.NormalizedName = newNorm;
                artist.UpdatedAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync(ct);
                return;
            }
        }
        else
        {
            var root = await ResolveRootFolderAsync(db, folder, ct);
            var rootNorm = root.Name.Trim().ToLowerInvariant();
            var artist = await db.Artists.AsNoTracking().FirstOrDefaultAsync(x => x.NormalizedName == rootNorm, ct);
            var album = await db.Albums.FirstOrDefaultAsync(
                x => !x.IsDeleted && x.NormalizedTitle == oldNorm && (artist == null || x.ArtistId == artist.Id || x.ArtistId == null),
                ct);
            if (album is not null && !await db.Albums.AnyAsync(x => !x.IsDeleted && x.NormalizedTitle == newNorm && x.ArtistId == album.ArtistId && x.Id != album.Id, ct))
            {
                album.Title = newName;
                album.NormalizedTitle = newNorm;
                album.UpdatedAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync(ct);
                return;
            }
        }

        await EnsureArtistAndAlbumAsync(db, folder, ct);
    }

    public static async Task SyncTracksAsync(
        MusicHubDbContext db,
        Folder folder,
        IReadOnlyCollection<Guid> trackIds,
        CancellationToken ct)
    {
        if (trackIds.Count == 0) return;

        var (artist, album) = await EnsureArtistAndAlbumAsync(db, folder, ct);
        var tracks = await db.Tracks
            .Where(x => trackIds.Contains(x.Id) && !x.IsDeleted)
            .ToListAsync(ct);

        if (tracks.Count == 0) return;

        var existingArtistLinks = (await db.TrackArtists
            .Where(x => trackIds.Contains(x.TrackId) && x.ArtistId == artist.Id)
            .Select(x => x.TrackId)
            .ToListAsync(ct))
            .ToHashSet();

        HashSet<Guid> existingAlbumLinks = [];
        int nextPos = 1;
        if (album is not null)
        {
            existingAlbumLinks = (await db.AlbumTracks
                .Where(x => x.AlbumId == album.Id && trackIds.Contains(x.TrackId))
                .Select(x => x.TrackId)
                .ToListAsync(ct))
                .ToHashSet();

            var maxPos = await db.AlbumTracks
                .Where(x => x.AlbumId == album.Id)
                .Select(x => (int?)x.Position)
                .MaxAsync(ct);
            nextPos = (maxPos ?? 0) + 1;
        }

        foreach (var track in tracks)
        {
            if (!existingArtistLinks.Contains(track.Id))
            {
                db.TrackArtists.Add(new TrackArtist
                {
                    TrackId = track.Id,
                    ArtistId = artist.Id,
                    Role = "Primary",
                    Position = 0
                });
            }

            if (IsPlaceholderName(track.ArtistDisplay))
            {
                track.ArtistDisplay = artist.Name;
                track.UpdatedAt = DateTimeOffset.UtcNow;
            }

            if (album is not null)
            {
                if (!existingAlbumLinks.Contains(track.Id))
                {
                    db.AlbumTracks.Add(new AlbumTrack
                    {
                        AlbumId = album.Id,
                        TrackId = track.Id,
                        Position = nextPos++
                    });
                }

                if (IsPlaceholderName(track.AlbumDisplay))
                {
                    track.AlbumDisplay = album.Title;
                    track.UpdatedAt = DateTimeOffset.UtcNow;
                }
            }
        }

        await db.SaveChangesAsync(ct);
    }

    private static async Task<Folder> ResolveRootFolderAsync(MusicHubDbContext db, Folder folder, CancellationToken ct)
    {
        var current = folder;
        var visited = new HashSet<Guid> { current.Id };
        while (current.ParentId is Guid parentId && visited.Add(parentId))
        {
            var parent = await db.Folders.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == parentId && !x.IsDeleted, ct);
            if (parent is null) break;
            current = parent;
        }
        return current;
    }

    private static bool IsPlaceholderName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return true;
        var v = value.Trim();
        return v.Equals("Unknown", StringComparison.OrdinalIgnoreCase)
            || v.Equals("Unknown Artist", StringComparison.OrdinalIgnoreCase)
            || v.Equals("Unknown Album", StringComparison.OrdinalIgnoreCase)
            || v.Equals("ناشناس", StringComparison.OrdinalIgnoreCase);
    }
}
