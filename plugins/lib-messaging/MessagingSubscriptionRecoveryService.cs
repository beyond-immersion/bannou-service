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
    private readonly MessagingServiceConfiguration _configuration;

    public MessagingSubscriptionRecoveryService(
        ILogger<MessagingSubscriptionRecoveryService> logger,
        MessagingService messagingService,
        MessagingServiceConfiguration configuration)
    {
        _logger = logger;
        _messagingService = messagingService;
        _configuration = configuration;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Wait briefly for other services to initialize
        await Task.Delay(TimeSpan.FromSeconds(_configuration.SubscriptionRecoveryStartupDelaySeconds), stoppingToken);

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
                await Task.Delay(TimeSpan.FromHours(_configuration.SubscriptionTtlRefreshIntervalHours), stoppingToken);
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
