using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using MusicHub.Api.Security;
using MusicHub.Api.Services;
using MusicHub.Infrastructure;

namespace MusicHub.Api.Endpoints;

public static class UpdateAndBackupEndpoints
{
    public static void MapUpdateAndBackupEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/admin/system/update-status", [Authorize(Policy = "Admin")] async (
            GitHubUpdateService updater,
            CancellationToken ct) =>
        {
            var status = await updater.GetStatusAsync(ct);
            return Results.Ok(status);
        });

        app.MapPost("/api/admin/system/check-update", [Authorize(Policy = "Admin")] async (
            ClaimsPrincipal principal,
            GitHubUpdateService updater,
            MusicHubDbContext db,
            HttpContext http,
            CancellationToken ct) =>
        {
            var status = await updater.CheckNowAsync(ct);
            AuditWriter.Add(db, principal, "system.update_check", "System", null, status.LatestCommitSha, RequestHelpers.Ip(http));
            await db.SaveChangesAsync(ct);
            return Results.Ok(status);
        });

        app.MapPost("/api/admin/system/apply-update", [Authorize(Policy = "Admin")] async (
            ClaimsPrincipal principal,
            GitHubUpdateService updater,
            MusicHubDbContext db,
            HttpContext http,
            CancellationToken ct) =>
        {
            AuditWriter.Add(db, principal, "system.update_apply", "System", null, null, RequestHelpers.Ip(http));
            await db.SaveChangesAsync(ct);
            var result = await updater.ApplyUpdateAsync(ct);
            return Results.Ok(result);
        });

        app.MapGet("/api/admin/backups/export", [Authorize(Policy = "Admin")] async (
            ClaimsPrincipal principal,
            SystemBackupService backupService,
            MusicHubDbContext db,
            HttpContext http,
            CancellationToken ct) =>
        {
            var (zipPath, fileName, _) = await backupService.CreateBackupArchiveAsync(ct);
            AuditWriter.Add(db, principal, "backup.export", "Backup", fileName, null, RequestHelpers.Ip(http));
            await db.SaveChangesAsync(ct);

            http.Response.OnCompleted(() =>
            {
                try { if (File.Exists(zipPath)) File.Delete(zipPath); } catch { }
                return Task.CompletedTask;
            });

            return Results.File(zipPath, "application/zip", fileName, enableRangeProcessing: false);
        });

        app.MapPost("/api/admin/backups/restore", [Authorize(Policy = "Admin")] async (
            HttpRequest request,
            ClaimsPrincipal principal,
            SystemBackupService backupService,
            MusicHubDbContext db,
            HttpContext http,
            CancellationToken ct) =>
        {
            if (!request.HasFormContentType)
            {
                return Results.BadRequest(new { error = "Multipart form-data with backup .zip file is required." });
            }

            var form = await request.ReadFormAsync(ct);
            var file = form.Files.GetFile("file") ?? form.Files.FirstOrDefault();
            if (file is null || file.Length == 0)
            {
                return Results.BadRequest(new { error = "Backup .zip file is missing or empty." });
            }

            if (!file.FileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                return Results.BadRequest(new { error = "Only .zip backup archives are supported." });
            }

            try
            {
                await using var stream = file.OpenReadStream();
                var result = await backupService.RestoreBackupArchiveAsync(stream, ct);
                AuditWriter.Add(db, principal, "backup.restore", "Backup", file.FileName,
                    $"users={result.UsersRestored},tracks={result.TracksRestored},files={result.FilesRestored}",
                    RequestHelpers.Ip(http));
                await db.SaveChangesAsync(ct);
                return Results.Ok(result);
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        }).DisableAntiforgery();
    }
}
