using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using MusicHub.Api.Realtime;
using MusicHub.Domain;
using MusicHub.Infrastructure;
using MusicHub.Infrastructure.Library;
using MusicHub.Infrastructure.Security;

namespace MusicHub.Api.Endpoints;

public static class LibraryEndpoints
{
    /// <summary>Upper bound on a caller-supplied id list, so one request cannot do unbounded work (CR-011).</summary>
    private const int MaxBatchSize = 500;

    private const string BatchTooLargeMessage = "At most 500 tracks may be submitted in one request.";

    public static void MapLibraryEndpoints(this IEndpointRouteBuilder app)
    {
        MapTracks(app);
        MapAlbums(app);
        MapArtists(app);
        MapFolders(app);
        MapGenres(app);
        MapPlaylists(app);
        MapFavorites(app);
        MapHistory(app);
        MapSearch(app);
    }

    private static void MapTracks(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/tracks", [Authorize] async (string? q, Guid? folderId, Guid? albumId, Guid? artistId, Guid? genreId, bool? favorites, bool? includeDeleted, string? sort, int? page, int? pageSize, ClaimsPrincipal principal, MusicHubDbContext db, CancellationToken ct) =>
        {
            var (p, size) = RequestHelpers.Page(page, pageSize);
            var userId = principal.GetUserId();
            // Soft-deleted rows carry title, artist, album, SHA-256, size and duration. The dashboard
            // only offers the toggle to administrators, so the server must enforce the same rule -
            // otherwise a Listener can enumerate ids that GET /api/tracks/{id} then 404s (CR-006).
            if (includeDeleted == true && !principal.IsAdministrator()) return ApiResults.Forbidden("Only administrators may list deleted tracks.");
            var query = db.Tracks.AsNoTracking()
                .Include(x => x.AudioAsset).ThenInclude(x => x.FileHash)
                .Include(x => x.Genre)
                .AsQueryable();

            if (includeDeleted != true) query = query.Where(x => !x.IsDeleted);
            if (!string.IsNullOrWhiteSpace(q))
            {
                var term = $"%{q.Trim()}%";
                query = query.Where(x => EF.Functions.ILike(x.Title, term) || EF.Functions.ILike(x.ArtistDisplay, term) || EF.Functions.ILike(x.AlbumDisplay, term));
            }
            if (folderId is not null) query = query.Where(x => x.FolderTracks.Any(f => f.FolderId == folderId));
            if (albumId is not null) query = query.Where(x => x.AlbumTracks.Any(a => a.AlbumId == albumId));
            if (artistId is not null) query = query.Where(x => x.TrackArtists.Any(a => a.ArtistId == artistId));
            if (genreId is not null) query = query.Where(x => x.GenreId == genreId);
            if (favorites == true) query = query.Where(x => db.Favorites.Any(f => f.UserId == userId && f.TrackId == x.Id));

            // Every ordering ends with Id so a page boundary can never repeat or drop a row when
            // several rows share the sort key (a bulk import writes many identical CreatedAt
            // values, and PostgreSQL does not guarantee tie order) (CR-017).
            query = sort switch
            {
                "title" => query.OrderBy(x => x.NormalizedTitle).ThenBy(x => x.Id),
                "artist" => query.OrderBy(x => x.ArtistDisplay).ThenBy(x => x.NormalizedTitle).ThenBy(x => x.Id),
                "oldest" => query.OrderBy(x => x.CreatedAt).ThenBy(x => x.Id),
                "duration" => query.OrderByDescending(x => x.DurationSeconds).ThenByDescending(x => x.Id),
                _ => query.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
            };

            var total = await query.CountAsync(ct);
            var rows = await query.Skip((p - 1) * size).Take(size).ToListAsync(ct);
            // Only the page's own ids are needed: reading the caller's whole favourites set cost a
            // full scan of Favorites per request for a user with a large collection (CR-020).
            var ids = rows.Select(x => x.Id).ToList();
            var favoriteSet = ids.Count == 0
                ? []
                : (await db.Favorites.AsNoTracking().Where(x => x.UserId == userId && ids.Contains(x.TrackId)).Select(x => x.TrackId).ToListAsync(ct)).ToHashSet();
            return Results.Ok(new PagedResult<TrackDto>(rows.Select(x => DtoMapper.Track(x, favoriteSet.Contains(x.Id))).ToList(), p, size, total));
        });

        app.MapGet("/api/tracks/{id:guid}", [Authorize] async (Guid id, ClaimsPrincipal principal, MusicHubDbContext db, CancellationToken ct) =>
        {
            var userId = principal.GetUserId();
            var track = await db.Tracks.AsNoTracking()
                .Include(x => x.AudioAsset).ThenInclude(x => x.FileHash)
                .Include(x => x.Genre)
                .Include(x => x.TrackArtists).ThenInclude(x => x.Artist)
                .Include(x => x.AlbumTracks).ThenInclude(x => x.Album)
                .Include(x => x.FolderTracks).ThenInclude(x => x.Folder)
                .SingleOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct);
            if (track is null) return Results.NotFound();
            var favorite = await db.Favorites.AnyAsync(x => x.UserId == userId && x.TrackId == id, ct);
            return Results.Ok(new
            {
                track = DtoMapper.Track(track, favorite),
                artists = track.TrackArtists.OrderBy(x => x.Position).Select(x => new { x.ArtistId, x.Artist.Name, x.Role }),
                albums = track.AlbumTracks.Select(x => new { x.AlbumId, x.Album.Title, x.Position }),
                folders = track.FolderTracks.Select(x => new { x.FolderId, x.Folder.Name }),
                file = new { track.AudioAsset.SizeBytes, track.AudioAsset.MimeType, track.AudioAsset.Extension, sha256 = track.AudioAsset.FileHash.Sha256, track.Bitrate, track.SampleRate, track.Channels },
                streamUrl = $"/api/tracks/{track.Id}/stream"
            });
        });

        app.MapPatch("/api/tracks/{id:guid}", [Authorize(Policy = "Admin")] async (Guid id, UpdateTrackRequest request, ClaimsPrincipal principal, MusicHubDbContext db, RealtimeNotifier realtime, HttpContext http, CancellationToken ct) =>
        {
            // The asset graph must be included: DtoMapper.Track reads AudioAsset.SizeBytes and
            // AudioAsset.FileHash.Sha256, and lazy loading is not enabled, so omitting it threw a
            // NullReferenceException after SaveChangesAsync had already committed the edit - the
            // client was told the request failed while the row was updated (CR-001). Every other
            // track read path already includes these two navigations.
            var track = await db.Tracks
                .Include(x => x.AudioAsset).ThenInclude(x => x.FileHash)
                .Include(x => x.Genre)
                .Include(x => x.TrackArtists).ThenInclude(x => x.Artist)
                .SingleOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct);
            if (track is null) return Results.NotFound();
            if (!string.IsNullOrWhiteSpace(request.Title))
            {
                track.Title = request.Title.Trim()[..Math.Min(request.Title.Trim().Length, 400)];
                track.NormalizedTitle = track.Title.ToLowerInvariant();
            }
            if (request.Artist is not null)
            {
                track.ArtistDisplay = request.Artist.Trim()[..Math.Min(request.Artist.Trim().Length, 400)];
                if (track.TrackArtists.Count == 0 && !string.IsNullOrWhiteSpace(track.ArtistDisplay))
                {
                    var artist = await GetOrCreateArtist(db, track.ArtistDisplay, ct);
                    track.TrackArtists.Add(new TrackArtist { Track = track, Artist = artist, Role = "Primary", Position = 0 });
                }
            }
            if (request.Album is not null) track.AlbumDisplay = request.Album.Trim()[..Math.Min(request.Album.Trim().Length, 400)];
            if (request.Year is not null) track.Year = request.Year;
            if (request.TrackNumber is not null) track.TrackNumber = request.TrackNumber;
            if (request.DiscNumber is not null) track.DiscNumber = request.DiscNumber;
            if (request.GenreName is not null)
            {
                track.Genre = string.IsNullOrWhiteSpace(request.GenreName) ? null : await GetOrCreateGenre(db, request.GenreName, ct);
            }
            track.UpdatedAt = DateTimeOffset.UtcNow;
            AuditWriter.Add(db, principal, "track.update", "Track", track.Id.ToString(), null, RequestHelpers.Ip(http));
            await db.SaveChangesAsync(ct);
            await realtime.TrackUpdated(DtoMapper.Track(track));
            return Results.Ok(DtoMapper.Track(track));
        });

        app.MapDelete("/api/tracks/{id:guid}", [Authorize(Policy = "Admin")] async (Guid id, ClaimsPrincipal principal, MusicHubDbContext db, RealtimeNotifier realtime, HttpContext http, CancellationToken ct) =>
        {
            var track = await db.Tracks.SingleOrDefaultAsync(x => x.Id == id, ct);
            if (track is null) return Results.NotFound();
            track.IsDeleted = true;
            track.DeletedAt = DateTimeOffset.UtcNow;
            AuditWriter.Add(db, principal, "track.delete", "Track", id.ToString(), null, RequestHelpers.Ip(http));
            await db.SaveChangesAsync(ct);
            await realtime.TrackDeleted(id);
            return Results.NoContent();
        });

        app.MapPost("/api/tracks/{id:guid}/restore", [Authorize(Policy = "Admin")] async (Guid id, ClaimsPrincipal principal, MusicHubDbContext db, RealtimeNotifier realtime, HttpContext http, CancellationToken ct) =>
        {
            var track = await db.Tracks.Include(x => x.AudioAsset).ThenInclude(x => x.FileHash).Include(x => x.Genre).SingleOrDefaultAsync(x => x.Id == id, ct);
            if (track is null) return Results.NotFound();
            track.IsDeleted = false;
            track.DeletedAt = null;
            AuditWriter.Add(db, principal, "track.restore", "Track", id.ToString(), null, RequestHelpers.Ip(http));
            await db.SaveChangesAsync(ct);
            await realtime.TrackUpdated(DtoMapper.Track(track));
            return Results.Ok(DtoMapper.Track(track));
        });

        app.MapGet("/api/tracks/{id:guid}/artwork", [Authorize] async (Guid id, MusicHubDbContext db, HttpContext http, CancellationToken ct) =>
        {
            var track = await db.Tracks.AsNoTracking().Where(x => x.Id == id).Select(x => new { x.ArtworkPath }).SingleOrDefaultAsync(ct);
            if (track?.ArtworkPath is null) return Results.NotFound();
            var storage = http.RequestServices.GetRequiredService<MusicHub.Infrastructure.Media.ContentAddressedStorage>();
            var full = storage.ResolveArtworkPath(track.ArtworkPath);
            if (!File.Exists(full)) return Results.NotFound();
            return ArtworkResults.File(http, full);
        });
    }

    private static void MapAlbums(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/albums", [Authorize] async (string? q, int? page, int? pageSize, MusicHubDbContext db, CancellationToken ct) =>
        {
            var (p, size) = RequestHelpers.Page(page, pageSize);
            var query = db.Albums.AsNoTracking().Where(x => !x.IsDeleted);
            if (!string.IsNullOrWhiteSpace(q)) query = query.Where(x => EF.Functions.ILike(x.Title, $"%{q.Trim()}%") || EF.Functions.ILike(x.AlbumArtist, $"%{q.Trim()}%"));
            var total = await query.CountAsync(ct);
            // Id tie-breaker: album titles repeat across artists and a bulk import writes many
            // identical NormalizedTitle values (CR-017).
            var rows = await query.OrderBy(x => x.NormalizedTitle).ThenBy(x => x.Id).Skip((p - 1) * size).Take(size)
                .Select(x => new
                {
                    x.Id,
                    x.Title,
                    x.AlbumArtist,
                    x.ArtistId,
                    x.Year,
                    x.Genre,
                    x.CreatedAt,
                    x.ArtworkPath,
                    TrackCount = x.AlbumTracks.Count
                })
                .ToListAsync(ct);
            // Album artwork is derived from the album's first track that has one, so existing data
            // shows covers without a backfill (CR-014). The stored Album.ArtworkPath wins when set.
            var ids = rows.Select(x => x.Id).ToList();
            var fallback = ids.Count == 0
                ? new Dictionary<Guid, bool>()
                : await db.AlbumTracks.AsNoTracking()
                    .Where(x => ids.Contains(x.AlbumId) && x.Track.ArtworkPath != null)
                    .GroupBy(x => x.AlbumId)
                    .Select(g => g.Key)
                    .ToDictionaryAsync(k => k, _ => true, ct);
            var items = rows.Select(x =>
            {
                var hasArtwork = x.ArtworkPath is not null || fallback.ContainsKey(x.Id);
                return (object)new
                {
                    x.Id,
                    x.Title,
                    x.AlbumArtist,
                    x.ArtistId,
                    x.Year,
                    x.Genre,
                    x.CreatedAt,
                    x.TrackCount,
                    artworkUrl = hasArtwork ? $"/api/albums/{x.Id}/artwork" : null
                };
            }).ToList();
            return Results.Ok(new PagedResult<object>(items, p, size, total));
        });

        app.MapGet("/api/albums/{id:guid}", [Authorize] async (Guid id, ClaimsPrincipal principal, MusicHubDbContext db, CancellationToken ct) =>
        {
            var album = await db.Albums.AsNoTracking().Include(x => x.Artist).Include(x => x.AlbumTracks).ThenInclude(x => x.Track).ThenInclude(x => x.Genre).Include(x => x.AlbumTracks).ThenInclude(x => x.Track).ThenInclude(x => x.AudioAsset).ThenInclude(x => x.FileHash)
                .SingleOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct);
            if (album is null) return Results.NotFound();
            var trackIds = album.AlbumTracks.Select(x => x.TrackId).ToList();
            var favorites = trackIds.Count == 0
                ? []
                : await db.Favorites.AsNoTracking().Where(x => x.UserId == principal.GetUserId() && trackIds.Contains(x.TrackId)).Select(x => x.TrackId).ToListAsync(ct);
            var set = favorites.ToHashSet();
            var artworkPath = album.ArtworkPath ?? album.AlbumTracks.Select(x => x.Track.ArtworkPath).FirstOrDefault(x => x is not null);
            return Results.Ok(new
            {
                album = new { album.Id, album.Title, album.AlbumArtist, album.ArtistId, ArtistName = album.Artist?.Name, album.Year, album.Genre, album.Description, album.CreatedAt, artworkUrl = artworkPath is null ? null : $"/api/albums/{album.Id}/artwork" },
                tracks = album.AlbumTracks.OrderBy(x => x.Position).Select(x => DtoMapper.Track(x.Track, set.Contains(x.TrackId)))
            });
        });

        app.MapPost("/api/albums", [Authorize(Policy = "Admin")] async (AlbumWriteRequest request, ClaimsPrincipal principal, MusicHubDbContext db, AlbumService albums, HttpContext http, CancellationToken ct) =>
        {
            // The same service the Telegram bot uses, so the two front ends cannot drift apart (D-1).
            var created = await albums.CreateAsync(
                request.Title, request.AlbumArtist ?? request.ArtistName, request.ArtistId,
                request.Year, request.Genre, request.Description, ct);
            if (!created.Succeeded)
            {
                return Results.BadRequest(new { error = created.Error });
            }

            AuditWriter.Add(db, principal, "album.create", "Album", created.AlbumId!.Value.ToString(), null, RequestHelpers.Ip(http));
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/albums/{created.AlbumId}", new { Id = created.AlbumId, Title = created.AlbumTitle });
        });

        app.MapPatch("/api/albums/{id:guid}", [Authorize(Policy = "Admin")] async (Guid id, AlbumWriteRequest request, ClaimsPrincipal principal, MusicHubDbContext db, RealtimeNotifier realtime, HttpContext http, CancellationToken ct) =>
        {
            var album = await db.Albums.SingleOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct);
            if (album is null) return Results.NotFound();
            if (!string.IsNullOrWhiteSpace(request.Title))
            {
                album.Title = request.Title.Trim()[..Math.Min(request.Title.Trim().Length, 400)];
                album.NormalizedTitle = album.Title.ToLowerInvariant();
            }
            if (request.AlbumArtist is not null) album.AlbumArtist = request.AlbumArtist.Trim();
            if (request.ArtistId is not null) album.ArtistId = request.ArtistId;
            if (request.Year is not null) album.Year = request.Year;
            if (request.Genre is not null) album.Genre = request.Genre;
            if (request.Description is not null) album.Description = request.Description;
            album.UpdatedAt = DateTimeOffset.UtcNow;
            AuditWriter.Add(db, principal, "album.update", "Album", album.Id.ToString(), null, RequestHelpers.Ip(http));
            await db.SaveChangesAsync(ct);
            await realtime.AlbumUpdated(new { album.Id, album.Title });
            return Results.NoContent();
        });

        app.MapDelete("/api/albums/{id:guid}", [Authorize(Policy = "Admin")] async (Guid id, ClaimsPrincipal principal, MusicHubDbContext db, HttpContext http, CancellationToken ct) =>
        {
            var album = await db.Albums.SingleOrDefaultAsync(x => x.Id == id);
            if (album is null) return Results.NotFound();
            album.IsDeleted = true;
            album.UpdatedAt = DateTimeOffset.UtcNow;
            AuditWriter.Add(db, principal, "album.delete", "Album", id.ToString(), null, RequestHelpers.Ip(http));
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        app.MapPost("/api/albums/{id:guid}/tracks", [Authorize(Policy = "Admin")] async (Guid id, AlbumTrackRequest request, MusicHubDbContext db, AlbumService albums, RealtimeNotifier realtime, CancellationToken ct) =>
        {
            // Shared with the bot's album picker: identical membership and AlbumDisplay rules.
            var assigned = await albums.AssignTrackAsync(id, request.TrackId, request.Position, ct);
            if (!assigned.Succeeded)
            {
                return Results.NotFound();
            }

            await realtime.AlbumUpdated(new { AlbumId = assigned.AlbumId, Title = assigned.AlbumTitle });
            return Results.NoContent();
        });

        app.MapDelete("/api/albums/{id:guid}/tracks/{trackId:guid}", [Authorize(Policy = "Admin")] async (Guid id, Guid trackId, AlbumService albums, RealtimeNotifier realtime, CancellationToken ct) =>
        {
            // The same removal rule the bot uses: only the membership goes, and the denormalized
            // AlbumDisplay is cleared when this was the track's last album.
            var removed = await albums.RemoveTrackAsync(id, trackId, ct);
            if (!removed.Succeeded) return Results.NotFound();
            await realtime.AlbumUpdated(new { id, title = (string?)null });
            return Results.NoContent();
        });

        app.MapGet("/api/albums/{id:guid}/artwork", [Authorize] async (Guid id, MusicHubDbContext db, HttpContext http, CancellationToken ct) =>
        {
            var album = await db.Albums.AsNoTracking().Where(x => x.Id == id)
                .Select(x => new
                {
                    x.ArtworkPath,
                    // Album artwork falls back to the first track cover so existing data works
                    // without a backfill (CR-014).
                    Fallback = x.AlbumTracks.OrderBy(t => t.Position).Select(t => t.Track.ArtworkPath).FirstOrDefault(p => p != null)
                })
                .SingleOrDefaultAsync(ct);
            var path = album?.ArtworkPath ?? album?.Fallback;
            if (path is null) return Results.NotFound();
            var storage = http.RequestServices.GetRequiredService<MusicHub.Infrastructure.Media.ContentAddressedStorage>();
            var full = storage.ResolveArtworkPath(path);
            return File.Exists(full) ? ArtworkResults.File(http, full) : Results.NotFound();
        });
    }

    private static void MapArtists(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/artists", [Authorize] async (string? q, int? page, int? pageSize, MusicHubDbContext db, CancellationToken ct) =>
        {
            var (p, size) = RequestHelpers.Page(page, pageSize);
            var query = db.Artists.AsNoTracking().AsQueryable();
            if (!string.IsNullOrWhiteSpace(q)) query = query.Where(x => EF.Functions.ILike(x.Name, $"%{q.Trim()}%"));
            var total = await query.CountAsync(ct);
            var items = await query.OrderBy(x => x.NormalizedName).ThenBy(x => x.Id).Skip((p - 1) * size).Take(size)
                .Select(x => new { x.Id, x.Name, x.Biography, TrackCount = x.TrackArtists.Count, AlbumCount = x.Albums.Count, artworkUrl = (string?)null })
                .ToListAsync(ct);
            return Results.Ok(new PagedResult<object>(items.Cast<object>().ToList(), p, size, total));
        });

        app.MapGet("/api/artists/{id:guid}", [Authorize] async (Guid id, MusicHubDbContext db, CancellationToken ct) =>
        {
            var artist = await db.Artists.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
            if (artist is null) return Results.NotFound();
            var tracks = await db.Tracks.AsNoTracking().Where(x => !x.IsDeleted && x.TrackArtists.Any(a => a.ArtistId == id))
                .Include(x => x.AudioAsset).ThenInclude(x => x.FileHash).Include(x => x.Genre).OrderByDescending(x => x.CreatedAt).Take(100).ToListAsync(ct);
            var albums = await db.Albums.AsNoTracking().Where(x => !x.IsDeleted && x.ArtistId == id).Select(x => new { x.Id, x.Title, x.Year, TrackCount = x.AlbumTracks.Count }).ToListAsync(ct);
            return Results.Ok(new { artist = new { artist.Id, artist.Name, artist.Biography, artworkUrl = (string?)null }, tracks = tracks.Select(x => DtoMapper.Track(x)), albums });
        });

        app.MapPost("/api/artists", [Authorize(Policy = "Admin")] async (ArtistWriteRequest request, ClaimsPrincipal principal, MusicHubDbContext db, HttpContext http, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Name)) return Results.BadRequest(new { error = "Artist name is required." });
            var name = request.Name.Trim();
            var normalized = name.ToLowerInvariant();
            if (await db.Artists.AnyAsync(x => x.NormalizedName == normalized, ct)) return Results.Conflict(new { error = "Artist already exists." });
            var artist = new Artist { Name = name, NormalizedName = normalized, Biography = request.Biography };
            db.Artists.Add(artist);
            AuditWriter.Add(db, principal, "artist.create", "Artist", artist.Id.ToString(), null, RequestHelpers.Ip(http));
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/artists/{artist.Id}", new { artist.Id, artist.Name });
        });

        app.MapPatch("/api/artists/{id:guid}", [Authorize(Policy = "Admin")] async (Guid id, ArtistWriteRequest request, ClaimsPrincipal principal, MusicHubDbContext db, HttpContext http, CancellationToken ct) =>
        {
            var artist = await db.Artists.SingleOrDefaultAsync(x => x.Id == id, ct);
            if (artist is null) return Results.NotFound();
            if (!string.IsNullOrWhiteSpace(request.Name))
            {
                artist.Name = request.Name.Trim();
                artist.NormalizedName = artist.Name.ToLowerInvariant();
            }
            if (request.Biography is not null) artist.Biography = request.Biography;
            artist.UpdatedAt = DateTimeOffset.UtcNow;
            AuditWriter.Add(db, principal, "artist.update", "Artist", artist.Id.ToString(), null, RequestHelpers.Ip(http));
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateException) { return Results.Conflict(new { error = "Artist name already exists." }); }
            return Results.NoContent();
        });

        app.MapDelete("/api/artists/{id:guid}", [Authorize(Policy = "Admin")] async (Guid id, ClaimsPrincipal principal, MusicHubDbContext db, HttpContext http, CancellationToken ct) =>
        {
            var artist = await db.Artists.SingleOrDefaultAsync(x => x.Id == id, ct);
            if (artist is null) return Results.NotFound();

            var albums = await db.Albums.Where(a => a.ArtistId == id).ToListAsync(ct);
            foreach (var album in albums)
            {
                album.ArtistId = null;
            }

            var trackArtists = await db.TrackArtists.Where(ta => ta.ArtistId == id).ToListAsync(ct);
            db.TrackArtists.RemoveRange(trackArtists);

            db.Artists.Remove(artist);
            AuditWriter.Add(db, principal, "artist.delete", "Artist", id.ToString(), null, RequestHelpers.Ip(http));
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });
    }

    private static void MapFolders(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/folders", [Authorize] async (MusicHubDbContext db, CancellationToken ct) =>
        {
            var rows = await db.Folders.AsNoTracking().Where(x => !x.IsDeleted)
                .Select(x => new { x.Id, x.Name, x.ParentId, x.Description, TrackCount = x.FolderTracks.Count, x.CreatedAt })
                .ToListAsync(ct);
            var nodes = rows.ToDictionary(x => x.Id, x => new FolderNode(x.Id, x.Name, x.ParentId, x.Description, x.TrackCount, new List<FolderNode>()));
            var roots = new List<FolderNode>();
            foreach (var row in rows)
            {
                var node = nodes[row.Id];
                if (row.ParentId is { } parentId && nodes.TryGetValue(parentId, out var parent)) parent.Children.Add(node);
                else roots.Add(node);
            }
            return Results.Ok(roots);
        });

        app.MapPost("/api/folders", [Authorize(Policy = "Admin")] async (FolderWriteRequest request, ClaimsPrincipal principal, MusicHubDbContext db, RealtimeNotifier realtime, HttpContext http, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Name)) return Results.BadRequest(new { error = "Folder name is required." });
            if (request.ParentId is not null && !await db.Folders.AnyAsync(x => x.Id == request.ParentId && !x.IsDeleted, ct)) return Results.BadRequest(new { error = "Parent folder not found." });
            var folder = new Folder { Name = request.Name.Trim()[..Math.Min(request.Name.Trim().Length, 200)], ParentId = request.ParentId, Description = request.Description, CreatedByUserId = principal.GetUserId() };
            db.Folders.Add(folder);
            AuditWriter.Add(db, principal, "folder.create", "Folder", folder.Id.ToString(), null, RequestHelpers.Ip(http));
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateException) { return Results.Conflict(new { error = "A folder with that name already exists here." }); }
            await realtime.FolderUpdated(new { folder.Id, folder.Name });
            return Results.Created($"/api/folders/{folder.Id}", new { folder.Id, folder.Name, folder.ParentId });
        });

        app.MapPatch("/api/folders/{id:guid}", [Authorize(Policy = "Admin")] async (Guid id, FolderWriteRequest request, ClaimsPrincipal principal, MusicHubDbContext db, RealtimeNotifier realtime, HttpContext http, CancellationToken ct) =>
        {
            var folder = await db.Folders.SingleOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct);
            if (folder is null) return Results.NotFound();
            if (!string.IsNullOrWhiteSpace(request.Name)) folder.Name = request.Name.Trim()[..Math.Min(request.Name.Trim().Length, 200)];
            if (request.Description is not null) folder.Description = request.Description;
            if (request.ParentId is not null)
            {
                if (request.ParentId == id) return Results.BadRequest(new { error = "A folder cannot be its own parent." });
                if (!await db.Folders.AnyAsync(x => x.Id == request.ParentId && !x.IsDeleted, ct)) return Results.BadRequest(new { error = "Parent folder not found." });
                folder.ParentId = request.ParentId;
            }
            folder.UpdatedAt = DateTimeOffset.UtcNow;
            AuditWriter.Add(db, principal, "folder.update", "Folder", folder.Id.ToString(), null, RequestHelpers.Ip(http));
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateException) { return Results.Conflict(new { error = "A folder with that name already exists here." }); }
            await realtime.FolderUpdated(new { folder.Id, folder.Name });
            return Results.NoContent();
        });

        app.MapDelete("/api/folders/{id:guid}", [Authorize(Policy = "Admin")] async (Guid id, ClaimsPrincipal principal, MusicHubDbContext db, HttpContext http, CancellationToken ct) =>
        {
            var folder = await db.Folders.SingleOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct);
            if (folder is null) return Results.NotFound();
            if (await db.Folders.AnyAsync(x => x.ParentId == id && !x.IsDeleted, ct) || await db.FolderTracks.AnyAsync(x => x.FolderId == id, ct))
                return Results.Conflict(new { error = "Move child folders and tracks before deleting this folder." });
            folder.IsDeleted = true;
            AuditWriter.Add(db, principal, "folder.delete", "Folder", id.ToString(), null, RequestHelpers.Ip(http));
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        app.MapPost("/api/folders/{id:guid}/tracks", [Authorize(Policy = "Admin")] async (Guid id, FolderTrackRequest request, MusicHubDbContext db, RealtimeNotifier realtime, CancellationToken ct) =>
        {
            if (!await db.Folders.AnyAsync(x => x.Id == id && !x.IsDeleted, ct)) return Results.NotFound();
            // An unbounded list with a per-item query let one request issue tens of thousands of
            // round trips and pin a thread; normalise to a set first and batch the two lookups
            // (CR-011).
            var requested = (request.TrackIds ?? []).Where(x => x != Guid.Empty).Distinct().ToList();
            if (requested.Count > MaxBatchSize) return Results.BadRequest(new { error = BatchTooLargeMessage });
            if (requested.Count > 0)
            {
                var existingTracks = await db.Tracks.Where(x => requested.Contains(x.Id) && !x.IsDeleted).Select(x => x.Id).ToListAsync(ct);
                var alreadyLinked = await db.FolderTracks.Where(x => x.FolderId == id && requested.Contains(x.TrackId)).Select(x => x.TrackId).ToListAsync(ct);
                var linked = alreadyLinked.ToHashSet();
                foreach (var trackId in existingTracks.Where(x => !linked.Contains(x)))
                {
                    db.FolderTracks.Add(new FolderTrack { FolderId = id, TrackId = trackId });
                }
                await db.SaveChangesAsync(ct);
            }
            await realtime.FolderUpdated(new { id });
            return Results.NoContent();
        });

        app.MapDelete("/api/folders/{id:guid}/tracks/{trackId:guid}", [Authorize(Policy = "Admin")] async (Guid id, Guid trackId, MusicHubDbContext db, CancellationToken ct) =>
        {
            var row = await db.FolderTracks.SingleOrDefaultAsync(x => x.FolderId == id && x.TrackId == trackId, ct);
            if (row is null) return Results.NotFound();
            db.FolderTracks.Remove(row);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });
    }

    private static void MapGenres(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/genres", [Authorize] async (MusicHubDbContext db, CancellationToken ct) =>
        {
            var genres = await db.Genres.AsNoTracking().OrderBy(x => x.Name).Select(x => new { x.Id, x.Name, TrackCount = x.Tracks.Count(t => !t.IsDeleted) }).ToListAsync(ct);
            return Results.Ok(genres);
        });

        app.MapPost("/api/genres", [Authorize(Policy = "Admin")] async (GenreWriteRequest request, ClaimsPrincipal principal, MusicHubDbContext db, HttpContext http, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Name)) return Results.BadRequest(new { error = "Genre name is required." });
            var name = request.Name.Trim();
            if (await db.Genres.AnyAsync(x => x.NormalizedName == name.ToLowerInvariant(), ct)) return Results.Conflict(new { error = "Genre already exists." });
            var genre = new Genre { Name = name, NormalizedName = name.ToLowerInvariant() };
            db.Genres.Add(genre);
            AuditWriter.Add(db, principal, "genre.create", "Genre", genre.Id.ToString(), null, RequestHelpers.Ip(http));
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/genres/{genre.Id}", new { genre.Id, genre.Name });
        });
    }

    private static void MapPlaylists(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/playlists", [Authorize] async (ClaimsPrincipal principal, MusicHubDbContext db, CancellationToken ct) =>
        {
            var userId = principal.GetUserId();
            var rows = await db.Playlists.AsNoTracking().Where(x => !x.IsDeleted && (x.OwnerUserId == userId || x.IsPublic))
                .OrderByDescending(x => x.UpdatedAt)
                .Select(x => new { x.Id, x.Name, x.Description, x.OwnerUserId, x.IsPublic, x.IsCollaborative, TrackCount = x.Tracks.Count, x.UpdatedAt, artworkUrl = (string?)null })
                .ToListAsync(ct);
            return Results.Ok(rows);
        });

        app.MapPost("/api/playlists", [Authorize] async (PlaylistWriteRequest request, ClaimsPrincipal principal, MusicHubDbContext db, HttpContext http, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Name)) return Results.BadRequest(new { error = "Playlist name is required." });
            var playlist = new Playlist
            {
                Name = request.Name.Trim()[..Math.Min(request.Name.Trim().Length, 300)],
                Description = request.Description,
                OwnerUserId = principal.GetUserId(),
                IsPublic = request.IsPublic ?? false,
                IsCollaborative = request.IsCollaborative ?? false
            };
            db.Playlists.Add(playlist);
            AuditWriter.Add(db, principal, "playlist.create", "Playlist", playlist.Id.ToString(), null, RequestHelpers.Ip(http));
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/playlists/{playlist.Id}", new { playlist.Id, playlist.Name });
        });

        app.MapGet("/api/playlists/{id:guid}", [Authorize] async (Guid id, ClaimsPrincipal principal, MusicHubDbContext db, CancellationToken ct) =>
        {
            var userId = principal.GetUserId();
            var playlist = await db.Playlists.AsNoTracking().Where(x => x.Id == id && !x.IsDeleted && (x.OwnerUserId == userId || x.IsPublic))
                .Select(x => new { x.Id, x.Name, x.Description, x.OwnerUserId, x.IsPublic, x.IsCollaborative, x.CreatedAt, x.UpdatedAt })
                .SingleOrDefaultAsync(ct);
            if (playlist is null) return Results.NotFound();
            var tracks = await db.PlaylistTracks.AsNoTracking().Where(x => x.PlaylistId == id).OrderBy(x => x.Position)
                .Include(x => x.Track).ThenInclude(x => x.AudioAsset).ThenInclude(x => x.FileHash)
                .Include(x => x.Track).ThenInclude(x => x.Genre)
                .ToListAsync(ct);
            var playlistTrackIds = tracks.Select(x => x.TrackId).ToList();
            var favorites = playlistTrackIds.Count == 0
                ? []
                : await db.Favorites.Where(x => x.UserId == userId && playlistTrackIds.Contains(x.TrackId)).Select(x => x.TrackId).ToListAsync(ct);
            var set = favorites.ToHashSet();
            return Results.Ok(new { playlist, tracks = tracks.Select(x => DtoMapper.Track(x.Track, set.Contains(x.TrackId))) });
        });

        app.MapPatch("/api/playlists/{id:guid}", [Authorize] async (Guid id, PlaylistWriteRequest request, ClaimsPrincipal principal, MusicHubDbContext db, RealtimeNotifier realtime, CancellationToken ct) =>
        {
            var playlist = await db.Playlists.SingleOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct);
            if (playlist is null) return Results.NotFound();
            if (playlist.OwnerUserId != principal.GetUserId() && !principal.IsAdministrator()) return ApiResults.Forbidden("You can only edit your own playlists.");
            if (!string.IsNullOrWhiteSpace(request.Name)) playlist.Name = request.Name.Trim()[..Math.Min(request.Name.Trim().Length, 300)];
            if (request.Description is not null) playlist.Description = request.Description;
            if (request.IsPublic is not null) playlist.IsPublic = request.IsPublic.Value;
            if (request.IsCollaborative is not null) playlist.IsCollaborative = request.IsCollaborative.Value;
            playlist.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            await realtime.PlaylistUpdated(new { playlist.Id, playlist.Name });
            return Results.NoContent();
        });

        app.MapDelete("/api/playlists/{id:guid}", [Authorize] async (Guid id, ClaimsPrincipal principal, MusicHubDbContext db, CancellationToken ct) =>
        {
            var playlist = await db.Playlists.SingleOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct);
            if (playlist is null) return Results.NotFound();
            if (playlist.OwnerUserId != principal.GetUserId() && !principal.IsAdministrator()) return ApiResults.Forbidden("You can only delete your own playlists.");
            playlist.IsDeleted = true;
            playlist.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        app.MapPost("/api/playlists/{id:guid}/tracks", [Authorize] async (Guid id, PlaylistTrackRequest request, ClaimsPrincipal principal, MusicHubDbContext db, RealtimeNotifier realtime, CancellationToken ct) =>
        {
            var playlist = await db.Playlists.SingleOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct);
            if (playlist is null) return Results.NotFound();
            var userId = principal.GetUserId();
            if (playlist.OwnerUserId != userId && !playlist.IsCollaborative && !principal.IsAdministrator()) return ApiResults.Forbidden("You can only change your own playlist, or a collaborative one.");
            if (!await db.Tracks.AnyAsync(x => x.Id == request.TrackId && !x.IsDeleted, ct)) return Results.NotFound();
            if (await db.PlaylistTracks.AnyAsync(x => x.PlaylistId == id && x.TrackId == request.TrackId, ct)) return Results.Conflict(new { error = "Track is already in the playlist." });
            var next = request.Position ?? (await db.PlaylistTracks.Where(x => x.PlaylistId == id).Select(x => (int?)x.Position).MaxAsync(ct) ?? 0) + 1;
            db.PlaylistTracks.Add(new PlaylistTrack { PlaylistId = id, TrackId = request.TrackId, Position = next, AddedByUserId = userId });
            playlist.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            await realtime.PlaylistUpdated(new { playlist.Id, playlist.Name });
            return Results.NoContent();
        });

        app.MapDelete("/api/playlists/{id:guid}/tracks/{trackId:guid}", [Authorize] async (Guid id, Guid trackId, ClaimsPrincipal principal, MusicHubDbContext db, RealtimeNotifier realtime, CancellationToken ct) =>
        {
            var playlist = await db.Playlists.SingleOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct);
            if (playlist is null) return Results.NotFound();
            if (playlist.OwnerUserId != principal.GetUserId() && !playlist.IsCollaborative && !principal.IsAdministrator()) return ApiResults.Forbidden("You can only change your own playlist, or a collaborative one.");
            var row = await db.PlaylistTracks.SingleOrDefaultAsync(x => x.PlaylistId == id && x.TrackId == trackId, ct);
            if (row is null) return Results.NotFound();
            db.PlaylistTracks.Remove(row);
            playlist.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            await realtime.PlaylistUpdated(new { playlist.Id, playlist.Name });
            return Results.NoContent();
        });

        app.MapPut("/api/playlists/{id:guid}/reorder", [Authorize] async (Guid id, PlaylistReorderRequest request, ClaimsPrincipal principal, MusicHubDbContext db, RealtimeNotifier realtime, CancellationToken ct) =>
        {
            var playlist = await db.Playlists.SingleOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct);
            if (playlist is null) return Results.NotFound();
            if (playlist.OwnerUserId != principal.GetUserId() && !playlist.IsCollaborative && !principal.IsAdministrator()) return ApiResults.Forbidden("You can only reorder your own playlist, or a collaborative one.");
            // A HashSet makes the "is this row mentioned" check O(1) instead of a linear scan per
            // row, and the length bound stops one request from doing unbounded work (CR-011).
            var requested = request.TrackIds ?? [];
            if (requested.Count > MaxBatchSize) return Results.BadRequest(new { error = BatchTooLargeMessage });
            var wanted = requested.ToHashSet();
            var rows = await db.PlaylistTracks.Where(x => x.PlaylistId == id).ToListAsync(ct);
            var lookup = rows.ToDictionary(x => x.TrackId);
            var position = 1;
            foreach (var trackId in requested)
            {
                if (lookup.TryGetValue(trackId, out var row)) row.Position = position++;
            }
            foreach (var row in rows.Where(x => !wanted.Contains(x.TrackId))) row.Position = position++;
            playlist.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            await realtime.PlaylistUpdated(new { playlist.Id, playlist.Name });
            return Results.NoContent();
        });
    }

    private static void MapFavorites(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/favorites/{trackId:guid}", [Authorize] async (Guid trackId, ClaimsPrincipal principal, MusicHubDbContext db, CancellationToken ct) =>
        {
            var userId = principal.GetUserId();
            if (!await db.Tracks.AnyAsync(x => x.Id == trackId && !x.IsDeleted, ct)) return Results.NotFound();
            if (!await db.Favorites.AnyAsync(x => x.UserId == userId && x.TrackId == trackId, ct))
            {
                db.Favorites.Add(new Favorite { UserId = userId, TrackId = trackId });
                await db.SaveChangesAsync(ct);
            }
            return Results.NoContent();
        });

        app.MapDelete("/api/favorites/{trackId:guid}", [Authorize] async (Guid trackId, ClaimsPrincipal principal, MusicHubDbContext db, CancellationToken ct) =>
        {
            var userId = principal.GetUserId();
            var favorite = await db.Favorites.SingleOrDefaultAsync(x => x.UserId == userId && x.TrackId == trackId, ct);
            if (favorite is not null)
            {
                db.Favorites.Remove(favorite);
                await db.SaveChangesAsync(ct);
            }
            return Results.NoContent();
        });
    }

    private static void MapHistory(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/history/playback", [Authorize] async (PlaybackRequest request, ClaimsPrincipal principal, MusicHubDbContext db, UserEntitlementService entitlements, ListeningTimeTracker listening, CancellationToken ct) =>
        {
            var userId = principal.GetUserId();
            var track = await db.Tracks.AsNoTracking().Where(x => x.Id == request.TrackId && !x.IsDeleted)
                .Select(x => new { x.Bitrate, Size = x.AudioAsset.SizeBytes, x.DurationSeconds })
                .SingleOrDefaultAsync(ct);
            if (track is null) return Results.NotFound();
            db.PlaybackEvents.Add(new PlaybackEvent { UserId = userId, TrackId = request.TrackId, PositionSeconds = Math.Max(0, request.PositionSeconds), Completed = request.Completed, DeviceId = request.DeviceId });

            // Listening time is charged from two sources and the larger wins (B-023):
            //  - the server's own count of bytes it served on /api/tracks/{id}/stream, converted
            //    with the real bitrate, which a modified client cannot influence; and
            //  - the client's wall-clock report, which is the only signal for offline playback
            //    (the app plays a downloaded local file and never touches the stream endpoint).
            // The counter is drained rather than read, so both sources describe the same interval
            // and taking the maximum cannot double-charge. Each source is clamped independently so
            // a forged report cannot exhaust an hour at a time or push the total negative.
            var reported = (long)Math.Clamp(request.ListenedSeconds ?? 0, 0, ListeningTimeTracker.MaxSecondsPerDrain);
            var streamedBytes = listening.DrainStreamedBytes(userId);
            var measured = ListeningTimeTracker.BytesToSeconds(streamedBytes, track.Bitrate, track.Size, track.DurationSeconds);
            var listenedSeconds = Math.Max(reported, measured);

            var entitlement = await entitlements.ConsumeListeningTimeAsync(userId, listenedSeconds, ct);
            if (!entitlement.Allowed)
            {
                return Results.Json(
                    new { error = DeactivationReasons.Describe(entitlement.Reason), reason = entitlement.Reason },
                    statusCode: StatusCodes.Status403Forbidden);
            }

            // One row per (user, track), maintained as an atomic upsert. Two concurrent reports used
            // to both observe null and both insert; from then on SingleOrDefaultAsync matched two
            // rows and threw, turning every later report for that track into a permanent HTTP 500
            // (CR-003). The unique index plus ON CONFLICT makes the write idempotent under
            // concurrency, and the 30-day window that caused the duplicate is gone with it.
            await UpsertListeningHistoryAsync(db, userId, request.TrackId, ct);

            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        app.MapGet("/api/history", [Authorize] async (ClaimsPrincipal principal, MusicHubDbContext db, int? limit, CancellationToken ct) =>
        {
            var userId = principal.GetUserId();
            var take = Math.Clamp(limit ?? 50, 1, 200);
            var grouped = await db.PlaybackEvents.AsNoTracking().Where(x => x.UserId == userId)
                .GroupBy(x => x.TrackId)
                .Select(g => new { TrackId = g.Key, LastPlayed = g.Max(x => x.StartedAt), Plays = g.Count() })
                .OrderByDescending(x => x.LastPlayed).Take(take).ToListAsync(ct);
            var ids = grouped.Select(x => x.TrackId).ToList();
            var tracks = await db.Tracks.AsNoTracking().Where(x => ids.Contains(x.Id) && !x.IsDeleted)
                .Include(x => x.AudioAsset).ThenInclude(x => x.FileHash).Include(x => x.Genre).ToListAsync(ct);
            var map = tracks.ToDictionary(x => x.Id);
            var favorites = await db.Favorites.Where(x => x.UserId == userId && ids.Contains(x.TrackId)).Select(x => x.TrackId).ToListAsync(ct);
            var set = favorites.ToHashSet();
            return Results.Ok(grouped.Where(x => map.ContainsKey(x.TrackId)).Select(x => new { x.LastPlayed, x.Plays, track = DtoMapper.Track(map[x.TrackId], set.Contains(x.TrackId)) }));
        });
    }

    private static void MapSearch(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/search", [Authorize] async (string? q, ClaimsPrincipal principal, MusicHubDbContext db, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(q)) return Results.Ok(new { tracks = Array.Empty<object>(), albums = Array.Empty<object>(), artists = Array.Empty<object>(), folders = Array.Empty<object>() });
            var term = $"%{q.Trim()}%";
            var userId = principal.GetUserId();
            var tracks = await db.Tracks.AsNoTracking().Where(x => !x.IsDeleted && (EF.Functions.ILike(x.Title, term) || EF.Functions.ILike(x.ArtistDisplay, term) || EF.Functions.ILike(x.AlbumDisplay, term)))
                .Include(x => x.AudioAsset).ThenInclude(x => x.FileHash).Include(x => x.Genre)
                .OrderByDescending(x => x.CreatedAt).Take(20).ToListAsync(ct);
            var trackIds = tracks.Select(x => x.Id).ToList();
            var favorites = await db.Favorites.Where(x => x.UserId == userId && trackIds.Contains(x.TrackId)).Select(x => x.TrackId).ToListAsync(ct);
            var set = favorites.ToHashSet();
            var albums = await db.Albums.AsNoTracking().Where(x => !x.IsDeleted && EF.Functions.ILike(x.Title, term)).OrderBy(x => x.Title).Take(10).Select(x => new { x.Id, x.Title, x.AlbumArtist, x.Year }).ToListAsync(ct);
            var artists = await db.Artists.AsNoTracking().Where(x => EF.Functions.ILike(x.Name, term)).OrderBy(x => x.Name).Take(10).Select(x => new { x.Id, x.Name }).ToListAsync(ct);
            var folders = await db.Folders.AsNoTracking().Where(x => !x.IsDeleted && EF.Functions.ILike(x.Name, term)).OrderBy(x => x.Name).Take(10).Select(x => new { x.Id, x.Name }).ToListAsync(ct);
            return Results.Ok(new { tracks = tracks.Select(x => DtoMapper.Track(x, set.Contains(x.Id))), albums, artists, folders });
        });

        app.MapGet("/api/home", [Authorize] async (ClaimsPrincipal principal, MusicHubDbContext db, CancellationToken ct) =>
        {
            var userId = principal.GetUserId();
            var recentlyAdded = await db.Tracks.AsNoTracking().Where(x => !x.IsDeleted).Include(x => x.AudioAsset).ThenInclude(x => x.FileHash).Include(x => x.Genre).OrderByDescending(x => x.CreatedAt).Take(20).ToListAsync(ct);
            var recentIds = await db.PlaybackEvents.AsNoTracking().Where(x => x.UserId == userId).OrderByDescending(x => x.StartedAt).Select(x => x.TrackId).Distinct().Take(20).ToListAsync(ct);
            var recentTracks = await db.Tracks.AsNoTracking().Where(x => recentIds.Contains(x.Id) && !x.IsDeleted).Include(x => x.AudioAsset).ThenInclude(x => x.FileHash).Include(x => x.Genre).ToListAsync(ct);
            var mostPlayed = await db.PlaybackEvents.AsNoTracking().Where(x => x.UserId == userId).GroupBy(x => x.TrackId).Select(g => new { TrackId = g.Key, Count = g.Count() }).OrderByDescending(x => x.Count).Take(20).ToListAsync(ct);
            var mostIds = mostPlayed.Select(x => x.TrackId).ToList();
            var mostTracks = await db.Tracks.AsNoTracking().Where(x => mostIds.Contains(x.Id) && !x.IsDeleted).Include(x => x.AudioAsset).ThenInclude(x => x.FileHash).Include(x => x.Genre).ToListAsync(ct);
            // Only the 20 newest favourites are shown, so only those ids are read; the previous
            // version materialised the caller's entire favourites set for every home page load
            // (CR-020).
            var favoriteIds = await db.Favorites.AsNoTracking().Where(x => x.UserId == userId).OrderByDescending(x => x.CreatedAt).Select(x => x.TrackId).Take(20).ToListAsync(ct);
            var favoriteTrackIds = favoriteIds.Count == 0 ? [] : await db.Tracks.AsNoTracking().Where(x => favoriteIds.Contains(x.Id) && !x.IsDeleted).OrderByDescending(x => x.CreatedAt)
                .Include(x => x.AudioAsset).ThenInclude(x => x.FileHash).Include(x => x.Genre).ToListAsync(ct);
            var visibleIds = recentlyAdded.Select(x => x.Id)
                .Concat(recentTracks.Select(x => x.Id))
                .Concat(mostTracks.Select(x => x.Id))
                .Concat(favoriteTrackIds.Select(x => x.Id))
                .ToList();
            var set = (await db.Favorites.AsNoTracking().Where(x => x.UserId == userId && visibleIds.Contains(x.TrackId)).Select(x => x.TrackId).ToListAsync(ct)).ToHashSet();
            var albums = await db.Albums.AsNoTracking().Where(x => !x.IsDeleted).OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).Take(12).Select(x => new { x.Id, x.Title, x.AlbumArtist, x.Year, TrackCount = x.AlbumTracks.Count }).ToListAsync(ct);
            return Results.Ok(new
            {
                recentlyAdded = recentlyAdded.Select(x => DtoMapper.Track(x, set.Contains(x.Id))),
                recentlyPlayed = recentTracks.Select(x => DtoMapper.Track(x, set.Contains(x.Id))),
                mostPlayed = mostTracks.Select(x => DtoMapper.Track(x, set.Contains(x.Id))),
                favorites = favoriteTrackIds.Select(x => DtoMapper.Track(x, true)),
                albums
            });
        });
    }

    private static Task<int> UpsertListeningHistoryAsync(MusicHubDbContext db, Guid userId, Guid trackId, CancellationToken ct) =>
        // Single statement, so no read-then-insert window exists. The unique index on
        // ("UserId","TrackId") makes the conflict target valid and guarantees at most one row
        // even if two app instances race (CR-003).
        db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "ListeningHistory" ("UserId", "TrackId", "PlayedAt", "PlayCount")
            VALUES ({userId}, {trackId}, {DateTimeOffset.UtcNow}, 1)
            ON CONFLICT ("UserId", "TrackId")
            DO UPDATE SET "PlayCount" = "ListeningHistory"."PlayCount" + 1, "PlayedAt" = EXCLUDED."PlayedAt"
            """, ct);

    private static async Task<Artist> GetOrCreateArtist(MusicHubDbContext db, string name, CancellationToken ct)
    {
        var normalized = name.Trim().ToLowerInvariant();
        var artist = await db.Artists.SingleOrDefaultAsync(x => x.NormalizedName == normalized, ct);
        if (artist is not null) return artist;
        artist = new Artist { Name = name.Trim()[..Math.Min(name.Trim().Length, 300)], NormalizedName = normalized[..Math.Min(normalized.Length, 300)] };
        db.Artists.Add(artist);
        return artist;
    }

    private static async Task<Genre> GetOrCreateGenre(MusicHubDbContext db, string name, CancellationToken ct)
    {
        var normalized = name.Trim().ToLowerInvariant();
        var genre = await db.Genres.SingleOrDefaultAsync(x => x.NormalizedName == normalized, ct);
        if (genre is not null) return genre;
        genre = new Genre { Name = name.Trim()[..Math.Min(name.Trim().Length, 160)], NormalizedName = normalized[..Math.Min(normalized.Length, 160)] };
        db.Genres.Add(genre);
        return genre;
    }

    public sealed record UpdateTrackRequest(string? Title, string? Artist, string? Album, string? GenreName, int? Year, int? TrackNumber, int? DiscNumber);
    public sealed record AlbumWriteRequest(string? Title, string? AlbumArtist, Guid? ArtistId, string? ArtistName, int? Year, string? Genre, string? Description);
    public sealed record AlbumTrackRequest(Guid TrackId, int? Position);
    public sealed record ArtistWriteRequest(string? Name, string? Biography);
    public sealed record FolderWriteRequest(string? Name, Guid? ParentId, string? Description);
    public sealed record FolderTrackRequest(List<Guid> TrackIds);
    public sealed record GenreWriteRequest(string? Name);
    public sealed record PlaylistWriteRequest(string? Name, string? Description, bool? IsPublic, bool? IsCollaborative);
    public sealed record PlaylistTrackRequest(Guid TrackId, int? Position);
    public sealed record PlaylistReorderRequest(List<Guid> TrackIds);
    public sealed record PlaybackRequest(Guid TrackId, double PositionSeconds, bool Completed, Guid? DeviceId, double? ListenedSeconds);
    public sealed record FolderNode(Guid Id, string Name, Guid? ParentId, string? Description, int TrackCount, List<FolderNode> Children);
}
