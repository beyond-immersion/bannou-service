using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Messaging;

/// <summary>
/// Background service that recovers external HTTP callback subscriptions on startup.
/// Also periodically refreshes TTL on persisted subscriptions to keep them alive.
/// </summary>
public sealed class MessagingSubscriptionRecoveryService : BackgroundService
{
    private readonly ILogger<MessagingSubscriptionRecoveryService> _logger;
    private readonly MessagingService _messagingService;

    /// <summary>
    /// Interval for refreshing subscription TTL (6 hours).
    /// This should be significantly less than the 24-hour TTL to ensure
    /// subscriptions don't expire while the service is running.
    /// </summary>
    private static readonly TimeSpan TtlRefreshInterval = TimeSpan.FromHours(6);

    public MessagingSubscriptionRecoveryService(
        ILogger<MessagingSubscriptionRecoveryService> logger,
        MessagingService messagingService)
    {
        _logger = logger;
        _messagingService = messagingService;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Wait briefly for other services to initialize
        await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);

        try
        {
            // Recover subscriptions on startup
            var recovered = await _messagingService.RecoverExternalSubscriptionsAsync(stoppingToken);
            _logger.LogInformation("Subscription recovery complete: {Count} subscriptions restored", recovered);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to recover external subscriptions on startup");
        }

        // Periodically refresh TTL to keep subscriptions alive
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TtlRefreshInterval, stoppingToken);
                await _messagingService.RefreshSubscriptionTtlAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Normal shutdown
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to refresh subscription TTL, will retry");
            }
        }
    }
}
