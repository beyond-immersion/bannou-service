using Dapr.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Subscriptions;

/// <summary>
/// Background service that periodically checks for expired subscriptions
/// and publishes subscription.updated events for them, enabling immediate
/// capability revocation for connected clients.
/// </summary>
public class SubscriptionExpirationService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<SubscriptionExpirationService> _logger;

    /// <summary>
    /// Interval between expiration checks (default: 5 minutes).
    /// </summary>
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Grace period after which a subscription is considered expired (to avoid race conditions).
    /// </summary>
    private static readonly TimeSpan ExpirationGracePeriod = TimeSpan.FromSeconds(30);

    private const string STATE_STORE = "subscriptions-statestore";
    private const string PUBSUB_NAME = "bannou-pubsub";
    private const string SUBSCRIPTION_UPDATED_TOPIC = "subscription.updated";
    private const string SUBSCRIPTION_INDEX_KEY = "subscription-index";

    public SubscriptionExpirationService(
        IServiceProvider serviceProvider,
        ILogger<SubscriptionExpirationService> logger)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[SUB-EXPIRY] Subscription expiration service starting, check interval: {Interval}", CheckInterval);

        // Wait a bit before first check to allow other services to start
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckAndExpireSubscriptionsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Expected during shutdown
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[SUB-EXPIRY] Error during subscription expiration check");
            }

            try
            {
                await Task.Delay(CheckInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        _logger.LogInformation("[SUB-EXPIRY] Subscription expiration service stopped");
    }

    /// <summary>
    /// Checks for expired subscriptions and publishes events for them.
    /// </summary>
    private async Task CheckAndExpireSubscriptionsAsync(CancellationToken cancellationToken)
    {
        _logger.LogDebug("[SUB-EXPIRY] Checking for expired subscriptions");

        using var scope = _serviceProvider.CreateScope();
        var daprClient = scope.ServiceProvider.GetService<DaprClient>();

        if (daprClient == null)
        {
            _logger.LogWarning("[SUB-EXPIRY] DaprClient not available, skipping expiration check");
            return;
        }

        // Get the subscription index to find all subscription IDs
        var subscriptionIndex = await daprClient.GetStateAsync<List<string>>(
            STATE_STORE,
            SUBSCRIPTION_INDEX_KEY,
            cancellationToken: cancellationToken);

        if (subscriptionIndex == null || subscriptionIndex.Count == 0)
        {
            _logger.LogDebug("[SUB-EXPIRY] No subscriptions to check");
            return;
        }

        var nowUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var expiredCount = 0;

        foreach (var subscriptionId in subscriptionIndex)
        {
            try
            {
                var subscription = await daprClient.GetStateAsync<SubscriptionDataModel>(
                    STATE_STORE,
                    $"subscription:{subscriptionId}",
                    cancellationToken: cancellationToken);

                if (subscription == null)
                {
                    continue;
                }

                // Check if subscription is active and has expired
                if (subscription.IsActive &&
                    subscription.ExpirationDateUnix.HasValue &&
                    subscription.ExpirationDateUnix.Value <= nowUnix - (long)ExpirationGracePeriod.TotalSeconds)
                {
                    _logger.LogInformation("[SUB-EXPIRY] Subscription {SubscriptionId} for account {AccountId} has expired",
                        subscription.SubscriptionId, subscription.AccountId);

                    // Mark as inactive
                    subscription.IsActive = false;
                    await daprClient.SaveStateAsync(
                        STATE_STORE,
                        $"subscription:{subscriptionId}",
                        subscription,
                        cancellationToken: cancellationToken);

                    // Publish expiration event
                    var expirationEvent = new SubscriptionUpdatedEvent
                    {
                        EventId = Guid.NewGuid(),
                        Timestamp = DateTimeOffset.UtcNow,
                        SubscriptionId = subscription.SubscriptionId,
                        AccountId = subscription.AccountId,
                        ServiceId = subscription.ServiceId,
                        StubName = subscription.StubName ?? string.Empty,
                        Action = SubscriptionUpdatedEventAction.Expired,
                        IsActive = false
                    };

                    await daprClient.PublishEventAsync(
                        PUBSUB_NAME,
                        SUBSCRIPTION_UPDATED_TOPIC,
                        expirationEvent,
                        cancellationToken);

                    _logger.LogInformation("[SUB-EXPIRY] Published expiration event for subscription {SubscriptionId}",
                        subscription.SubscriptionId);

                    expiredCount++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[SUB-EXPIRY] Failed to process subscription {SubscriptionId}", subscriptionId);
            }
        }

        if (expiredCount > 0)
        {
            _logger.LogInformation("[SUB-EXPIRY] Expired {Count} subscriptions", expiredCount);
        }
        else
        {
            _logger.LogDebug("[SUB-EXPIRY] No subscriptions expired this cycle");
        }
    }

    /// <summary>
    /// Internal model for subscription data (matches SubscriptionsService storage format).
    /// </summary>
    private class SubscriptionDataModel
    {
        public Guid SubscriptionId { get; set; }
        public Guid AccountId { get; set; }
        public Guid ServiceId { get; set; }
        public string? StubName { get; set; }
        public long StartDateUnix { get; set; }
        public long? ExpirationDateUnix { get; set; }
        public bool IsActive { get; set; }
        public long CreatedAtUnix { get; set; }
        public long UpdatedAtUnix { get; set; }
    }
}
