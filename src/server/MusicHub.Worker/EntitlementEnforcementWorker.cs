using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MusicHub.Infrastructure.Security;

namespace MusicHub.Worker;

/// <summary>
/// Periodically sweeps and deactivates accounts whose access period has expired based on server time,
/// revoking tokens and sessions across all devices.
/// </summary>
public sealed class EntitlementEnforcementWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<EntitlementEnforcementWorker> logger) : BackgroundService
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

        logger.LogInformation("Entitlement enforcement worker started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var service = scope.ServiceProvider.GetRequiredService<UserEntitlementService>();
                var deactivated = await service.EnforceAllAsync(stoppingToken);
                if (deactivated > 0)
                {
                    logger.LogInformation("Entitlement sweep deactivated {Count} account(s).", deactivated);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Entitlement enforcement sweep failed.");
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
