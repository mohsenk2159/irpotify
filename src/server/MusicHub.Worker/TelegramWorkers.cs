using Microsoft.EntityFrameworkCore;
using MusicHub.Infrastructure;
using MusicHub.Infrastructure.Telegram;

namespace MusicHub.Worker;

/// <summary>
/// Long-polls Telegram while ingestion is enabled and backs off when it is disabled,
/// unconfigured, or the network fails. The update offset is persisted in the database.
/// </summary>
public sealed class TelegramPollingWorker(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<TelegramPollingWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var enabled = configuration.GetValue("Telegram:WorkerEnabled", true);
        if (!enabled)
        {
            logger.LogInformation("Telegram polling worker is disabled by configuration.");
            return;
        }

        logger.LogInformation("Telegram polling worker started.");
        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = TimeSpan.FromSeconds(10);
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var ingestion = scope.ServiceProvider.GetRequiredService<TelegramIngestionService>();
                var result = await ingestion.PollOnceAsync(stoppingToken);
                if (result.Any)
                {
                    logger.LogInformation(
                        "Telegram ingestion processed {Processed} update(s): {Imported} imported, {Duplicates} duplicate(s), {Rejected} rejected.",
                        result.UpdatesProcessed, result.Imported, result.Duplicates, result.Rejected);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Telegram polling iteration failed; retrying after backoff.");
                delay = TimeSpan.FromSeconds(30);
            }

            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
        logger.LogInformation("Telegram polling worker stopped.");
    }
}

public sealed class TelegramPublicationWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<TelegramPublicationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Telegram publication worker started.");
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var publisher = scope.ServiceProvider.GetRequiredService<TelegramPublishService>();
                var published = await publisher.PublishPendingAsync(stoppingToken);
                if (published > 0)
                {
                    logger.LogInformation("Published {Count} track(s) to Telegram.", published);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Telegram publication iteration failed.");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
        logger.LogInformation("Telegram publication worker stopped.");
    }
}
