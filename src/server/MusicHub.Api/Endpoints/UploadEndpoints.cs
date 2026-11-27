using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MusicHub.Domain;
using MusicHub.Infrastructure;
using MusicHub.Infrastructure.Media;
using MusicHub.Infrastructure.Options;

namespace MusicHub.Api.Endpoints;

public static class UploadEndpoints
{
    public static void MapUploadEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/uploads/history", [Authorize] async (ClaimsPrincipal principal, MusicHubDbContext db, int? page, int? pageSize, CancellationToken ct) =>
        {
            var (p, size) = RequestHelpers.Page(page, pageSize);
            var query = db.UploadSessions.AsNoTracking().AsQueryable();
            if (!principal.IsAdministrator()) query = query.Where(x => x.UserId == principal.GetUserId());
            var total = await query.CountAsync(ct);
            // Id tie-breaker: a bulk upload writes several sessions with the same CreatedAt, and
            // PostgreSQL does not guarantee tie order between queries (CR-017).
            var rows = await query.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).Skip((p - 1) * size).Take(size)
                .Select(x => new { x.Id, x.OriginalFileName, x.ExpectedSizeBytes, x.ReceivedBytes, x.Sha256, x.Status, x.TrackId, x.Error, x.CreatedAt, x.CompletedAt })
                .ToListAsync(ct);
            return Results.Ok(new PagedResult<object>(rows.Cast<object>().ToList(), p, size, total));
        });

        // Only administrators may add content. Every other role is read-only for the shared library
        // (favourites, own playlists and playback history remain per-user writes) (CR-004).
        app.MapPost("/api/uploads", [Authorize(Policy = "Admin")] async (HttpRequest request, ClaimsPrincipal principal, MusicHubDbContext db, TrackImportService importer, IOptions<StorageOptions> options, HttpContext http, CancellationToken ct) =>
        {
            if (!request.HasFormContentType) return Results.StatusCode(StatusCodes.Status415UnsupportedMediaType);
            var form = await request.ReadFormAsync(ct);
            if (form.Files.Count == 0) return Results.BadRequest(new { error = "At least one audio file is required." });
            if (form.Files.Count > 25) return Results.BadRequest(new { error = "A maximum of 25 files may be uploaded at once." });

            Guid? folderId = null;
            if (Guid.TryParse(form["folderId"].FirstOrDefault(), out var parsedFolder)) folderId = parsedFolder;
            Guid? albumId = null;
            if (Guid.TryParse(form["albumId"].FirstOrDefault(), out var parsedAlbum)) albumId = parsedAlbum;

            if (folderId is not null)
            {
                var targetFolder = await db.Folders.AsNoTracking().SingleOrDefaultAsync(x => x.Id == folderId && !x.IsDeleted, ct);
                if (targetFolder is null) return Results.BadRequest(new { error = "Folder not found." });
            }
            if (albumId is not null && !await db.Albums.AnyAsync(x => x.Id == albumId && !x.IsDeleted, ct))
                return Results.BadRequest(new { error = "Album not found." });

            var userId = principal.GetUserId();
            var results = new List<UploadResult>();
            var uploadedTrackIds = new List<Guid>();
            foreach (var file in form.Files)
            {
                await using var stream = file.OpenReadStream();
                var result = await importer.ImportAsync(new TrackImportRequest(
                    stream,
                    file.FileName,
                    file.Length,
                    userId,
                    folderId,
                    albumId,
                    "upload",
                    null,
                    options.Value.MaxUploadBytes), ct);

                if (result.Success && result.TrackId is Guid tid) uploadedTrackIds.Add(tid);
                results.Add(new UploadResult(result.FileName, result.Success, result.Duplicate, result.Error, await LoadTrackAsync(db, result.TrackId, userId, ct)));
            }

            if (folderId is not null && albumId is null && uploadedTrackIds.Count > 0)
            {
                var targetFolder = await db.Folders.AsNoTracking().SingleOrDefaultAsync(x => x.Id == folderId && !x.IsDeleted, ct);
                if (targetFolder is not null)
                {
                    await Services.FolderSyncHelper.SyncTracksAsync(db, targetFolder, uploadedTrackIds, ct);
                }
            }

            if (results.Any(x => x.Success))
            {
                AuditWriter.Add(db, principal, "track.upload", "Track", null,
                    System.Text.Json.JsonSerializer.Serialize(new { files = results.Select(x => new { x.FileName, x.Success, x.Duplicate, trackId = x.Track?.Id }) }),
                    RequestHelpers.Ip(http));
                await db.SaveChangesAsync(ct);
            }

            var failures = results.Count(x => !x.Success);
            var status = failures == 0 ? StatusCodes.Status201Created : failures == results.Count ? StatusCodes.Status422UnprocessableEntity : StatusCodes.Status207MultiStatus;
            return Results.Json(new { results }, statusCode: status);
        }).RequireRateLimiting("upload").DisableAntiforgery();
    }

    internal static async Task<TrackDto?> LoadTrackAsync(MusicHubDbContext db, Guid? trackId, Guid userId, CancellationToken ct)
    {
        if (trackId is null) return null;
        var track = await db.Tracks.AsNoTracking()
            .Include(x => x.AudioAsset).ThenInclude(x => x.FileHash)
            .Include(x => x.Genre)
            .SingleOrDefaultAsync(x => x.Id == trackId && !x.IsDeleted, ct);
        if (track is null) return null;
        var favorite = await db.Favorites.AnyAsync(x => x.UserId == userId && x.TrackId == track.Id, ct);
        return DtoMapper.Track(track, favorite);
    }

    public sealed record UploadResult(string FileName, bool Success, bool Duplicate, string? Error, TrackDto? Track);
}

public sealed class RealtimeImportListener(MusicHubDbContext db, Realtime.RealtimeNotifier notifier) : ITrackImportListener
{
    public async Task OnTrackImportedAsync(TrackImportResult result, CancellationToken ct)
    {
        if (result.TrackId is null) return;
        var track = await db.Tracks.AsNoTracking()
            .Include(x => x.AudioAsset).ThenInclude(x => x.FileHash)
            .Include(x => x.Genre)
            .SingleOrDefaultAsync(x => x.Id == result.TrackId, ct);
        if (track is not null)
        {
            await notifier.TrackAdded(DtoMapper.Track(track));
        }
        await notifier.UploadCompleted(new { fileName = result.FileName, duplicate = result.Duplicate, trackId = result.TrackId, source = "import" });
    }

    public Task OnImportFailedAsync(string fileName, string error, CancellationToken ct)
        => Task.CompletedTask;
}
