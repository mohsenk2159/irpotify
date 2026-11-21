using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MusicHub.Domain;
using MusicHub.Infrastructure;
using MusicHub.Infrastructure.Media;
using MusicHub.Infrastructure.Options;
using MusicHub.Infrastructure.Telegram;

namespace MusicHub.Api.Endpoints;

public static class TelegramEndpoints
{
    public static void MapTelegramEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/telegram").RequireAuthorization("Admin");

        group.MapGet("", async (MusicHubDbContext db, CancellationToken ct) =>
        {
            var config = await db.TelegramConfigurations.AsNoTracking().OrderBy(x => x.UpdatedAt).FirstOrDefaultAsync(ct);
            return Results.Ok(ToSummary(config));
        });

        group.MapPut("", async (TelegramConfigRequest request, ClaimsPrincipal principal, MusicHubDbContext db,
            TelegramSecretProtector protector, HttpContext http, CancellationToken ct) =>
        {
            if (request.BotToken is not null && request.BotToken.Length > 0 && !TelegramBotClient.LooksLikeBotToken(request.BotToken))
            {
                return Results.BadRequest(new { error = "The bot token format is invalid." });
            }
            if (request.MaxIncomingFileSizeBytes is < 1024 or > 2L * 1024 * 1024 * 1024)
            {
                return Results.BadRequest(new { error = "maxIncomingFileSizeBytes must be between 1 KiB and 2 GiB." });
            }
            if (request.MaxAudioDurationSeconds is < 1 or > 12 * 60 * 60)
            {
                return Results.BadRequest(new { error = "maxAudioDurationSeconds must be between 1 second and 12 hours." });
            }
            if (request.MaxRetryAttempts is < 1 or > 20)
            {
                return Results.BadRequest(new { error = "maxRetryAttempts must be between 1 and 20." });
            }
            if (!string.IsNullOrWhiteSpace(request.ApiEndpoint))
            {
                try { TelegramBotClient.NormalizeApiBase(request.ApiEndpoint); }
                catch (InvalidOperationException ex) { return Results.BadRequest(new { error = ex.Message }); }
            }
            if (!string.IsNullOrWhiteSpace(request.LocalBotApiEndpoint))
            {
                try { TelegramBotClient.NormalizeApiBase(request.LocalBotApiEndpoint); }
                catch (InvalidOperationException ex) { return Results.BadRequest(new { error = ex.Message }); }
            }

            var config = await db.TelegramConfigurations.OrderBy(x => x.UpdatedAt).FirstOrDefaultAsync(ct);
            if (config is null)
            {
                config = new TelegramConfiguration();
                db.TelegramConfigurations.Add(config);
            }

            if (request.BotToken is not null)
            {
                config.BotTokenProtected = request.BotToken.Length == 0 ? null : protector.Protect(request.BotToken.Trim());
            }
            if (request.AdminTelegramUserId is not null) config.AdminTelegramUserId = request.AdminTelegramUserId;
            if (request.AdminChatId is not null) config.AdminChatId = request.AdminChatId;
            if (request.DestinationGroupId is not null) config.DestinationGroupId = request.DestinationGroupId;
            if (request.DestinationTopicId is not null) config.DestinationTopicId = request.DestinationTopicId;
            if (request.AutoPublishEnabled is not null) config.AutoPublishEnabled = request.AutoPublishEnabled.Value;
            if (request.IngestionEnabled is not null) config.IngestionEnabled = request.IngestionEnabled.Value;
            if (request.MaxIncomingFileSizeBytes is not null) config.MaxIncomingFileSizeBytes = request.MaxIncomingFileSizeBytes.Value;
            if (request.MaxAudioDurationSeconds is not null) config.MaxAudioDurationSeconds = request.MaxAudioDurationSeconds.Value;
            if (!string.IsNullOrWhiteSpace(request.ApiEndpoint)) config.ApiEndpoint = request.ApiEndpoint.Trim().TrimEnd('/');
            if (request.LocalBotApiEndpoint is not null)
            {
                config.LocalBotApiEndpoint = string.IsNullOrWhiteSpace(request.LocalBotApiEndpoint)
                    ? null
                    : request.LocalBotApiEndpoint.Trim().TrimEnd('/');
            }
            if (request.MaxRetryAttempts is not null) config.MaxRetryAttempts = request.MaxRetryAttempts.Value;

            config.UpdatedAt = DateTimeOffset.UtcNow;
            config.UpdatedByUserId = principal.GetUserId();
            AuditWriter.Add(db, principal, "telegram.config.update", "TelegramConfiguration", config.Id.ToString(),
                JsonSerializer.Serialize(new
                {
                    tokenChanged = request.BotToken is not null,
                    config.IngestionEnabled,
                    config.AutoPublishEnabled,
                    config.AdminTelegramUserId,
                    config.DestinationGroupId
                }), RequestHelpers.Ip(http));
            await db.SaveChangesAsync(ct);
            return Results.Ok(ToSummary(config));
        });

        group.MapPost("/test", async (MusicHubDbContext db, TelegramSecretProtector protector, HttpContext http,
            ClaimsPrincipal principal, CancellationToken ct) =>
        {
            var config = await db.TelegramConfigurations.AsNoTracking().OrderBy(x => x.UpdatedAt).FirstOrDefaultAsync(ct);
            var token = protector.Unprotect(config?.BotTokenProtected);
            if (string.IsNullOrWhiteSpace(token))
            {
                return Results.BadRequest(new { ok = false, error = "No bot token is stored." });
            }
            var apiBase = TelegramBotClient.NormalizeApiBase(config!.LocalBotApiEndpoint ?? config.ApiEndpoint);
            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
                var me = await new TelegramBotClient(client).GetMeAsync(token, apiBase, ct);
                AuditWriter.Add(db, principal, "telegram.test", "TelegramConfiguration", config.Id.ToString(),
                    JsonSerializer.Serialize(new { me.Id, me.Username }), RequestHelpers.Ip(http));
                await db.SaveChangesAsync(ct);
                return Results.Ok(new { ok = true, botId = me.Id, username = me.Username, firstName = me.FirstName });
            }
            catch (Exception ex) when (ex is TelegramBotException or HttpRequestException or TaskCanceledException)
            {
                return Results.Ok(new { ok = false, error = ex.Message });
            }
        });

        group.MapPost("/test-ingest", async (HttpRequest request, ClaimsPrincipal principal, MusicHubDbContext db,
            TrackImportService importer, TelegramSecretProtector protector, HttpContext http, CancellationToken ct) =>
        {
            if (!request.HasFormContentType) return Results.StatusCode(StatusCodes.Status415UnsupportedMediaType);
            var config = await db.TelegramConfigurations.AsNoTracking().OrderBy(x => x.UpdatedAt).FirstOrDefaultAsync(ct);
            if (config is null) return Results.BadRequest(new { error = "Telegram is not configured." });
            if (!config.IngestionEnabled) return Results.BadRequest(new { error = "Telegram ingestion is disabled." });
            if (string.IsNullOrWhiteSpace(protector.Unprotect(config.BotTokenProtected)))
                return Results.BadRequest(new { error = "A valid bot token must be stored before the adapter can run." });

            var form = await request.ReadFormAsync(ct);
            var file = form.Files.FirstOrDefault();
            if (file is null) return Results.BadRequest(new { error = "A single audio file is required." });

            await using var stream = file.OpenReadStream();
            var result = await importer.ImportAsync(new TrackImportRequest(
                stream,
                file.FileName,
                file.Length,
                principal.GetUserId(),
                null,
                null,
                "telegram-test",
                "admin-adapter",
                config.MaxIncomingFileSizeBytes), ct);

            db.TelegramImports.Add(new TelegramImport
            {
                TelegramUserId = config.AdminTelegramUserId ?? 0,
                ChatId = config.AdminChatId ?? 0,
                FileName = result.FileName,
                Status = result.Success ? JobStatus.Completed : JobStatus.Failed,
                TrackId = result.TrackId,
                Error = result.Error,
                CompletedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync(ct);

            return result.Success
                ? Results.Ok(new { success = true, duplicate = result.Duplicate, trackId = result.TrackId })
                : Results.UnprocessableEntity(new { success = false, error = result.Error });
        }).DisableAntiforgery().RequireRateLimiting("upload");

        group.MapGet("/imports", async (MusicHubDbContext db, int? page, int? pageSize, CancellationToken ct) =>
        {
            var (p, size) = RequestHelpers.Page(page, pageSize);
            var query = db.TelegramImports.AsNoTracking().OrderByDescending(x => x.CreatedAt);
            var total = await query.CountAsync(ct);
            var rows = await query.Skip((p - 1) * size).Take(size)
                .Select(x => new
                {
                    x.Id,
                    x.TelegramUpdateId,
                    x.TelegramUserId,
                    x.ChatId,
                    x.FileId,
                    x.FileName,
                    x.Status,
                    x.TrackId,
                    x.Error,
                    x.CreatedAt,
                    x.CompletedAt
                }).ToListAsync(ct);
            return Results.Ok(new PagedResult<object>(rows.Cast<object>().ToList(), p, size, total));
        });

        group.MapGet("/publications", async (MusicHubDbContext db, string? status, int? page, int? pageSize, CancellationToken ct) =>
        {
            var (p, size) = RequestHelpers.Page(page, pageSize);
            var query = db.TelegramPublications.AsNoTracking().AsQueryable();
            if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<TelegramPublishStatus>(status, true, out var parsed))
            {
                query = query.Where(x => x.Status == parsed);
            }
            var total = await query.CountAsync(ct);
            var rows = await query.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).Skip((p - 1) * size).Take(size)
                .Select(x => new
                {
                    x.Id,
                    x.TrackId,
                    TrackTitle = x.Track.Title,
                    x.Status,
                    x.Attempts,
                    x.NextAttemptAt,
                    x.LastError,
                    x.TelegramMessageId,
                    x.ChatId,
                    x.CreatedAt,
                    x.UpdatedAt
                }).ToListAsync(ct);
            return Results.Ok(new PagedResult<object>(rows.Cast<object>().ToList(), p, size, total));
        });

        group.MapPost("/publications/{id:guid}/retry", async (Guid id, ClaimsPrincipal principal, MusicHubDbContext db,
            HttpContext http, CancellationToken ct) =>
        {
            var publication = await db.TelegramPublications.SingleOrDefaultAsync(x => x.Id == id, ct);
            if (publication is null) return Results.NotFound();
            if (publication.Status == TelegramPublishStatus.Published)
                return Results.Conflict(new { error = "The publication already succeeded." });
            publication.Status = TelegramPublishStatus.Pending;
            publication.NextAttemptAt = DateTimeOffset.UtcNow;
            publication.LastError = null;
            publication.UpdatedAt = DateTimeOffset.UtcNow;
            AuditWriter.Add(db, principal, "telegram.publication.retry", "TelegramPublication", id.ToString(), null, RequestHelpers.Ip(http));
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        group.MapGet("/updates", async (MusicHubDbContext db, int? take, CancellationToken ct) =>
        {
            var limit = Math.Clamp(take ?? 50, 1, 200);
            var rows = await db.TelegramUpdates.AsNoTracking().OrderByDescending(x => x.ReceivedAt).Take(limit)
                .Select(x => new { x.Id, x.TelegramUpdateId, x.ReceivedAt, x.ProcessedAt, x.Error })
                .ToListAsync(ct);
            return Results.Ok(rows);
        });

        group.MapPost("/enable", async (bool enabled, ClaimsPrincipal principal, MusicHubDbContext db, HttpContext http, CancellationToken ct) =>
        {
            var config = await db.TelegramConfigurations.OrderBy(x => x.UpdatedAt).FirstOrDefaultAsync(ct);
            if (config is null) return Results.BadRequest(new { error = "Telegram is not configured." });
            if (enabled && string.IsNullOrWhiteSpace(config.BotTokenProtected))
                return Results.BadRequest(new { error = "A bot token must be stored before enabling ingestion." });
            if (enabled && config.AdminTelegramUserId is null)
                return Results.BadRequest(new { error = "An administrator Telegram user id is required before enabling ingestion." });
            config.IngestionEnabled = enabled;
            config.UpdatedAt = DateTimeOffset.UtcNow;
            config.UpdatedByUserId = principal.GetUserId();
            AuditWriter.Add(db, principal, enabled ? "telegram.enable" : "telegram.disable", "TelegramConfiguration",
                config.Id.ToString(), null, RequestHelpers.Ip(http));
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { enabled });
        });
    }

    private static object ToSummary(TelegramConfiguration? config) => new
    {
        configured = config is not null,
        tokenConfigured = !string.IsNullOrWhiteSpace(config?.BotTokenProtected),
        adminTelegramUserId = config?.AdminTelegramUserId,
        adminChatId = config?.AdminChatId,
        destinationGroupId = config?.DestinationGroupId,
        destinationTopicId = config?.DestinationTopicId,
        autoPublishEnabled = config?.AutoPublishEnabled ?? false,
        ingestionEnabled = config?.IngestionEnabled ?? false,
        maxIncomingFileSizeBytes = config?.MaxIncomingFileSizeBytes ?? 20L * 1024 * 1024,
        maxAudioDurationSeconds = config?.MaxAudioDurationSeconds ?? 3600,
        apiEndpoint = config?.ApiEndpoint ?? "https://api.telegram.org",
        localBotApiEndpoint = config?.LocalBotApiEndpoint,
        maxRetryAttempts = config?.MaxRetryAttempts ?? 5,
        updatedAt = config?.UpdatedAt
    };

    public sealed record TelegramConfigRequest(
        string? BotToken,
        long? AdminTelegramUserId,
        long? AdminChatId,
        long? DestinationGroupId,
        long? DestinationTopicId,
        bool? AutoPublishEnabled,
        bool? IngestionEnabled,
        long? MaxIncomingFileSizeBytes,
        int? MaxAudioDurationSeconds,
        string? ApiEndpoint,
        string? LocalBotApiEndpoint,
        int? MaxRetryAttempts);
}
