using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Subscription;

/// <summary>
/// Background service that periodically checks for expired subscriptions
/// and delegates expiration to SubscriptionService.ExpireSubscriptionAsync,
/// enabling immediate capability revocation for connected clients.
/// </summary>
public class SubscriptionExpirationService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<SubscriptionExpirationService> _logger;
    private readonly SubscriptionServiceConfiguration _configuration;

    private const string SUBSCRIPTION_INDEX_KEY = "subscription-index";

    /// <summary>
    /// Interval between expiration checks, from configuration.
    /// </summary>
    private TimeSpan CheckInterval => TimeSpan.FromMinutes(_configuration.ExpirationCheckIntervalMinutes);

    /// <summary>
    /// Grace period after which a subscription is considered expired (to avoid race conditions).
    /// </summary>
    private TimeSpan ExpirationGracePeriod => TimeSpan.FromSeconds(_configuration.ExpirationGracePeriodSeconds);

    /// <summary>
    /// Startup delay before first check, from configuration.
    /// </summary>
    private TimeSpan StartupDelay => TimeSpan.FromSeconds(_configuration.StartupDelaySeconds);

    /// <summary>
    /// Creates a new instance of the SubscriptionExpirationService.
    /// </summary>
    public SubscriptionExpirationService(
        IServiceProvider serviceProvider,
        ILogger<SubscriptionExpirationService> logger,
        SubscriptionServiceConfiguration configuration)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _configuration = configuration;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Subscription expiration service starting, check interval: {Interval}", CheckInterval);

        // Wait a bit before first check to allow other services to start
        try
        {
            await Task.Delay(StartupDelay, stoppingToken);
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
                    var messageBus = errorScope.ServiceProvider.GetRequiredService<IMessageBus>();
                    await messageBus.TryPublishErrorAsync(
                        "subscription",
                        "ExpirationCheck",
                        ex.GetType().Name,
                        ex.Message,
                        severity: ServiceErrorEventSeverity.Error);
                }
                catch (Exception pubEx)
                {
                    // Don't let error publishing failures affect the loop
                    _logger.LogDebug(pubEx, "Failed to publish error event - continuing expiration loop");
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
    /// Checks for expired subscriptions and delegates expiration to the main SubscriptionService.
    /// Removes fully-processed entries from the global index to prevent unbounded growth.
    /// </summary>
    private async Task CheckAndExpireSubscriptionsAsync(CancellationToken cancellationToken)
    {
        _logger.LogDebug("Checking for expired subscriptions");

        using var scope = _serviceProvider.CreateScope();
        var stateStoreFactory = scope.ServiceProvider.GetRequiredService<IStateStoreFactory>();
        var subscriptionService = scope.ServiceProvider.GetRequiredService<ISubscriptionService>();

        // Get the subscription index to find all subscription IDs
        var indexStore = stateStoreFactory.GetStore<List<Guid>>(StateStoreDefinitions.Subscription);
        var subscriptionIndex = await indexStore.GetAsync(SUBSCRIPTION_INDEX_KEY, cancellationToken);

        if (subscriptionIndex == null || subscriptionIndex.Count == 0)
        {
            _logger.LogDebug("No subscriptions to check");
            return;
        }

        var nowUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var expiredCount = 0;
        var idsToRemoveFromIndex = new List<Guid>();

        var subscriptionStore = stateStoreFactory.GetStore<SubscriptionDataModel>(StateStoreDefinitions.Subscription);
        foreach (var subscriptionId in subscriptionIndex)
        {
            try
            {
                var subscription = await subscriptionStore.GetAsync(
                    $"subscription:{subscriptionId}",
                    cancellationToken);

                if (subscription == null)
                {
                    // Subscription was deleted - remove from index
                    idsToRemoveFromIndex.Add(subscriptionId);
                    continue;
                }

                // Already inactive - no longer needs to be in the expiration index
                if (!subscription.IsActive)
                {
                    idsToRemoveFromIndex.Add(subscriptionId);
                    continue;
                }

                // No expiration date means unlimited subscription - remove from expiration index
                if (!subscription.ExpirationDateUnix.HasValue)
                {
                    idsToRemoveFromIndex.Add(subscriptionId);
                    continue;
                }

                // Check if subscription has expired (with grace period)
                if (subscription.ExpirationDateUnix.Value <= nowUnix - (long)ExpirationGracePeriod.TotalSeconds)
                {
                    // Delegate actual expiration to the service (which handles locking and event publishing)
                    var expired = await subscriptionService.ExpireSubscriptionAsync(subscriptionId, cancellationToken);

                    if (expired)
                    {
                        _logger.LogInformation("Expired subscription {SubscriptionId} for account {AccountId}",
                            subscription.SubscriptionId, subscription.AccountId);
                        expiredCount++;
                    }

                    idsToRemoveFromIndex.Add(subscriptionId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to process subscription {SubscriptionId}", subscriptionId);
            }
        }

        // Clean up the subscription index by removing processed entries
        if (idsToRemoveFromIndex.Count > 0)
        {
            await CleanupSubscriptionIndexAsync(indexStore, idsToRemoveFromIndex, cancellationToken);
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
    /// Removes processed subscription IDs from the global subscription index.
    /// Uses optimistic concurrency to handle concurrent modifications safely.
    /// </summary>
    private async Task CleanupSubscriptionIndexAsync(
        IStateStore<List<Guid>> indexStore,
        List<Guid> idsToRemove,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var (currentIndex, etag) = await indexStore.GetWithETagAsync(SUBSCRIPTION_INDEX_KEY, cancellationToken);
            if (currentIndex == null || currentIndex.Count == 0)
            {
                return;
            }

            var removeSet = new HashSet<Guid>(idsToRemove);
            var updatedIndex = currentIndex.Where(id => !removeSet.Contains(id)).ToList();

            if (updatedIndex.Count == currentIndex.Count)
            {
                return; // Nothing to remove
            }

            // GetWithETagAsync returns non-null etag for existing records;
            // coalesce satisfies compiler's nullable analysis (will never execute)
            var result = await indexStore.TrySaveAsync(SUBSCRIPTION_INDEX_KEY, updatedIndex, etag ?? string.Empty, cancellationToken);
            if (result != null)
            {
                _logger.LogDebug("Cleaned {Count} entries from subscription index", currentIndex.Count - updatedIndex.Count);
                return;
            }

            _logger.LogDebug("Concurrent modification on subscription index during cleanup, retrying (attempt {Attempt})", attempt + 1);
        }

        _logger.LogWarning("Failed to clean subscription index after retries - will retry next cycle");
    }
}
