using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using MusicHub.Domain;
using MusicHub.Infrastructure;

namespace MusicHub.Api.Endpoints;

public static class NotificationEndpoints
{
    public static void MapNotificationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/notifications").RequireAuthorization("Admin");

        group.MapGet("/", async (
            string? status,
            string? sort,
            int? page,
            int? pageSize,
            MusicHubDbContext db,
            CancellationToken ct) =>
        {
            var p = page.GetValueOrDefault(1);
            if (p < 1) p = 1;
            var ps = pageSize.GetValueOrDefault(50);
            if (ps < 1 || ps > 200) ps = 50;

            IQueryable<SystemNotification> query = db.Notifications.AsNoTracking();

            if (string.Equals(status, "unread", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(x => !x.IsRead);
            }
            else if (string.Equals(status, "read", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(x => x.IsRead);
            }

            if (string.Equals(sort, "oldest", StringComparison.OrdinalIgnoreCase))
            {
                query = query.OrderBy(x => x.CreatedAt);
            }
            else if (string.Equals(sort, "unreadFirst", StringComparison.OrdinalIgnoreCase))
            {
                query = query.OrderBy(x => x.IsRead).ThenByDescending(x => x.CreatedAt);
            }
            else
            {
                query = query.OrderByDescending(x => x.CreatedAt);
            }

            var totalCount = await query.CountAsync(ct);
            var unreadCount = await db.Notifications.AsNoTracking().CountAsync(x => !x.IsRead, ct);

            var items = await query
                .Skip((p - 1) * ps)
                .Take(ps)
                .Select(x => new
                {
                    x.Id,
                    x.Type,
                    x.Title,
                    x.Message,
                    x.Email,
                    x.UserId,
                    x.IsRead,
                    x.CreatedAt,
                    x.ReadAt
                })
                .ToListAsync(ct);

            return Results.Ok(new
            {
                items,
                totalCount,
                unreadCount,
                page = p,
                pageSize = ps
            });
        });

        group.MapGet("/unread-count", async (MusicHubDbContext db, CancellationToken ct) =>
        {
            var unreadCount = await db.Notifications.AsNoTracking().CountAsync(x => !x.IsRead, ct);
            return Results.Ok(new { unreadCount });
        });

        group.MapPatch("/{id:guid}/read", async (Guid id, MusicHubDbContext db, CancellationToken ct) =>
        {
            var notification = await db.Notifications.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (notification is null) return Results.NotFound();

            if (!notification.IsRead)
            {
                notification.IsRead = true;
                notification.ReadAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync(ct);
            }

            return Results.Ok(new { id = notification.Id, isRead = notification.IsRead, readAt = notification.ReadAt });
        });

        group.MapPost("/mark-all-read", async (MusicHubDbContext db, CancellationToken ct) =>
        {
            var now = DateTimeOffset.UtcNow;
            await db.Notifications
                .Where(x => !x.IsRead)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.IsRead, true).SetProperty(x => x.ReadAt, now), ct);

            return Results.NoContent();
        });

        group.MapDelete("/{id:guid}", async (Guid id, MusicHubDbContext db, CancellationToken ct) =>
        {
            var notification = await db.Notifications.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (notification is null) return Results.NotFound();

            db.Notifications.Remove(notification);
            await db.SaveChangesAsync(ct);

            return Results.NoContent();
        });

        group.MapPost("/batch-delete", async (BatchDeleteNotificationsRequest request, MusicHubDbContext db, CancellationToken ct) =>
        {
            if (request.Ids == null || request.Ids.Count == 0)
            {
                return Results.BadRequest(new { error = "No IDs provided." });
            }

            await db.Notifications
                .Where(x => request.Ids.Contains(x.Id))
                .ExecuteDeleteAsync(ct);

            return Results.NoContent();
        });
    }

    public sealed record BatchDeleteNotificationsRequest(List<Guid> Ids);
}
