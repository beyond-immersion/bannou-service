using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Messaging.Services;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State.Services;
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
        _logger.LogInformation("Subscription expiration service starting, check interval: {Interval}", CheckInterval);

        // Wait a bit before first check to allow other services to start
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("Subscription expiration service cancelled during startup");
            return;
        }

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
                _logger.LogError(ex, "Error during subscription expiration check");
                try
                {
                    using var errorScope = _serviceProvider.CreateScope();
                    var messageBus = errorScope.ServiceProvider.GetService<IMessageBus>();
                    if (messageBus != null)
                    {
                        await messageBus.TryPublishErrorAsync(
                            "subscriptions",
                            "ExpirationCheck",
                            ex.GetType().Name,
                            ex.Message,
                            severity: BeyondImmersion.BannouService.Events.ServiceErrorEventSeverity.Error);
                    }
                }
                catch
                {
                    // Don't let error publishing failures affect the loop
                }
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

        _logger.LogInformation("Subscription expiration service stopped");
    }

    /// <summary>
    /// Checks for expired subscriptions and publishes events for them.
    /// </summary>
    private async Task CheckAndExpireSubscriptionsAsync(CancellationToken cancellationToken)
    {
        _logger.LogDebug("Checking for expired subscriptions");

        using var scope = _serviceProvider.CreateScope();
        var stateStoreFactory = scope.ServiceProvider.GetService<IStateStoreFactory>();
        var messageBus = scope.ServiceProvider.GetService<IMessageBus>();

        if (stateStoreFactory == null || messageBus == null)
        {
            _logger.LogError("IStateStoreFactory or IMessageBus not available - subscription expiration checks cannot run. This is a critical infrastructure issue.");
            return;
        }

        // Get the subscription index to find all subscription IDs
        var indexStore = stateStoreFactory.GetStore<List<string>>(STATE_STORE);
        var subscriptionIndex = await indexStore.GetAsync(SUBSCRIPTION_INDEX_KEY, cancellationToken);

        if (subscriptionIndex == null || subscriptionIndex.Count == 0)
        {
            _logger.LogDebug("No subscriptions to check");
            return;
        }

        var nowUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var expiredCount = 0;

        var subscriptionStore = stateStoreFactory.GetStore<SubscriptionDataModel>(STATE_STORE);
        foreach (var subscriptionId in subscriptionIndex)
        {
            try
            {
                var subscription = await subscriptionStore.GetAsync(
                    $"subscription:{subscriptionId}",
                    cancellationToken);

                if (subscription == null)
                {
                    continue;
                }

                // Check if subscription is active and has expired
                if (subscription.IsActive &&
                    subscription.ExpirationDateUnix.HasValue &&
                    subscription.ExpirationDateUnix.Value <= nowUnix - (long)ExpirationGracePeriod.TotalSeconds)
                {
                    _logger.LogInformation("Subscription {SubscriptionId} for account {AccountId} has expired",
                        subscription.SubscriptionId, subscription.AccountId);

                    // Mark as inactive
                    subscription.IsActive = false;
                    await subscriptionStore.SaveAsync(
                        $"subscription:{subscriptionId}",
                        subscription,
                        cancellationToken: cancellationToken);

                    // Publish expiration event
                    var expirationEvent = new SubscriptionUpdatedEvent
                    {
                        EventName = SubscriptionUpdatedEventEventName.Subscription_updated,
                        EventId = Guid.NewGuid(),
                        Timestamp = DateTimeOffset.UtcNow,
                        SubscriptionId = subscription.SubscriptionId,
                        AccountId = subscription.AccountId,
                        ServiceId = subscription.ServiceId,
                        StubName = subscription.StubName ?? string.Empty,
                        Action = SubscriptionUpdatedEventAction.Expired,
                        IsActive = false
                    };

                    await messageBus.PublishAsync(
                        SUBSCRIPTION_UPDATED_TOPIC,
                        expirationEvent);

                    _logger.LogInformation("Published expiration event for subscription {SubscriptionId}",
                        subscription.SubscriptionId);

                    expiredCount++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to process subscription {SubscriptionId}", subscriptionId);
            }
        }

        if (expiredCount > 0)
        {
            _logger.LogInformation("Expired {Count} subscriptions", expiredCount);
        }
        else
        {
            _logger.LogDebug("No subscriptions expired this cycle");
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
