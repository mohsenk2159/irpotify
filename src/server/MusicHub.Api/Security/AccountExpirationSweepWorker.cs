using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MusicHub.Infrastructure.Security;

namespace MusicHub.Api.Security;

/// <summary>
/// Periodically sweeps and deactivates accounts whose access period has expired based on the
/// server system clock (DateTimeOffset.UtcNow), revoking active sessions across all devices.
/// </summary>
public sealed class AccountExpirationSweepWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<AccountExpirationSweepWorker> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(20);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(StartupDelay, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        logger.LogInformation("Account expiration sweep worker started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var service = scope.ServiceProvider.GetRequiredService<UserEntitlementService>();
                var deactivated = await service.EnforceAllAsync(stoppingToken);
                if (deactivated > 0)
                {
                    logger.LogInformation("Sweep deactivated {Count} expired account(s).", deactivated);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to run account expiration sweep.");
            }

            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
