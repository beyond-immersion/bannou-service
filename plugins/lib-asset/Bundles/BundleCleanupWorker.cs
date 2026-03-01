using BeyondImmersion.BannouService.Asset.Models;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Asset.Bundles;

/// <summary>
/// Background service that periodically scans for expired soft-deleted bundles
/// and permanently removes them (state, storage, and indexes).
/// </summary>
public sealed class BundleCleanupWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ITelemetryProvider _telemetryProvider;
    private readonly ILogger<BundleCleanupWorker> _logger;
    private readonly AssetServiceConfiguration _configuration;

    /// <summary>
    /// Creates a new BundleCleanupWorker.
    /// </summary>
    public BundleCleanupWorker(
        IServiceProvider serviceProvider,
        ITelemetryProvider telemetryProvider,
        ILogger<BundleCleanupWorker> logger,
        AssetServiceConfiguration configuration)
    {
        _serviceProvider = serviceProvider;
        _telemetryProvider = telemetryProvider;
        _logger = logger;
        _configuration = configuration;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromMinutes(_configuration.BundleCleanupIntervalMinutes);
        _logger.LogInformation("Bundle cleanup worker starting, interval: {Interval}, retention: {RetentionDays} days",
            interval, _configuration.DeletedBundleRetentionDays);

        // Startup delay to allow other services to initialize
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CleanupExpiredBundlesAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during bundle cleanup scan");
                try
                {
                    using var errorScope = _serviceProvider.CreateScope();
                    var messageBus = errorScope.ServiceProvider.GetRequiredService<IMessageBus>();
                    await messageBus.TryPublishErrorAsync(
                        "asset",
                        "BundleCleanup",
                        ex.GetType().Name,
                        ex.Message,
                        severity: ServiceErrorEventSeverity.Error);
                }
                catch (Exception pubEx)
                {
                    _logger.LogDebug(pubEx, "Failed to publish error event - continuing cleanup loop");
                }
            }

            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        _logger.LogInformation("Bundle cleanup worker stopped");
    }

    /// <summary>
    /// Scans the deleted bundles index and permanently removes bundles past their retention period.
    /// </summary>
    internal async Task CleanupExpiredBundlesAsync(CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.asset", "BundleCleanupWorker.CleanupExpiredBundlesAsync");

        var stateStoreFactory = _serviceProvider.GetRequiredService<IStateStoreFactory>();
        var bundleStore = stateStoreFactory.GetCacheableStore<BundleMetadata>(StateStoreDefinitions.Asset);
        var messageBus = _serviceProvider.GetRequiredService<IMessageBus>();
        var storageProvider = _serviceProvider.GetService<IAssetStorageProvider>();

        // Read all deleted bundle IDs from the index (set operations require cacheable store)
        var deletedBundleIds = await bundleStore.GetSetAsync<string>("deleted-bundles-index", cancellationToken);
        if (deletedBundleIds.Count == 0)
        {
            _logger.LogDebug("BundleCleanup: No deleted bundles in index");
            return;
        }

        _logger.LogInformation("BundleCleanup: Scanning {Count} deleted bundles for expiry", deletedBundleIds.Count);

        var now = DateTimeOffset.UtcNow;
        var retentionDays = _configuration.DeletedBundleRetentionDays > 0
            ? _configuration.DeletedBundleRetentionDays
            : 30;
        var purgedCount = 0;

        foreach (var bundleId in deletedBundleIds)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var bundleKey = $"{_configuration.BundleKeyPrefix}{bundleId}";
                var bundle = await bundleStore.GetAsync(bundleKey, cancellationToken);

                if (bundle == null)
                {
                    // Bundle already permanently deleted - clean up orphaned index entry
                    await bundleStore.RemoveFromSetAsync("deleted-bundles-index", bundleId, cancellationToken: cancellationToken);
                    continue;
                }

                // Check if bundle has been restored (no longer deleted)
                if (bundle.LifecycleStatus != BundleLifecycleStatus.Deleted)
                {
                    await bundleStore.RemoveFromSetAsync("deleted-bundles-index", bundleId, cancellationToken: cancellationToken);
                    continue;
                }

                // Check if retention period has passed
                if (bundle.DeletedAt == null || now < bundle.DeletedAt.Value.AddDays(retentionDays))
                {
                    continue; // Not yet expired
                }

                // Permanently delete: storage, state, and indexes
                await PermanentlyDeleteBundleAsync(
                    bundleId, bundle, bundleStore, storageProvider, messageBus, cancellationToken);

                purgedCount++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "BundleCleanup: Failed to process bundle {BundleId}, will retry next scan", bundleId);
            }
        }

        if (purgedCount > 0)
        {
            _logger.LogInformation("BundleCleanup: Permanently deleted {PurgedCount} expired bundles", purgedCount);
        }
    }

    /// <summary>
    /// Permanently removes a bundle from storage, state stores, and all indexes.
    /// </summary>
    private async Task PermanentlyDeleteBundleAsync(
        string bundleId,
        BundleMetadata bundle,
        ICacheableStateStore<BundleMetadata> bundleStore,
        IAssetStorageProvider? storageProvider,
        IMessageBus messageBus,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.asset", "BundleCleanupWorker.PermanentlyDeleteBundleAsync");

        // Delete from object storage
        if (storageProvider != null && !string.IsNullOrEmpty(bundle.StorageKey))
        {
            try
            {
                await storageProvider.DeleteObjectAsync(
                    bundle.Bucket ?? _configuration.StorageBucket,
                    bundle.StorageKey);

                _logger.LogDebug("BundleCleanup: Deleted storage object for bundle {BundleId}", bundleId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "BundleCleanup: Failed to delete storage for bundle {BundleId}, proceeding with state cleanup", bundleId);
            }
        }

        // Delete bundle metadata from state store
        var bundleKey = $"{_configuration.BundleKeyPrefix}{bundleId}";
        await bundleStore.DeleteAsync(bundleKey, cancellationToken);

        // Remove per-asset reverse indexes
        var assetBundleIndexStore = _serviceProvider.GetRequiredService<IStateStoreFactory>()
            .GetStore<AssetBundleIndex>(StateStoreDefinitions.Asset);

        foreach (var assetId in bundle.AssetIds)
        {
            try
            {
                var realmPrefix = (bundle.Realm ?? "cross-realm").ToLowerInvariant();
                var indexKey = $"{realmPrefix}:asset-bundles:{assetId}";
                var index = await assetBundleIndexStore.GetAsync(indexKey, cancellationToken);
                if (index != null)
                {
                    index.BundleIds.Remove(bundleId);
                    if (index.BundleIds.Count == 0)
                    {
                        await assetBundleIndexStore.DeleteAsync(indexKey, cancellationToken);
                    }
                    else
                    {
                        await assetBundleIndexStore.SaveAsync(indexKey, index, cancellationToken: cancellationToken);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "BundleCleanup: Failed to clean asset-bundle index for asset {AssetId} in bundle {BundleId}", assetId, bundleId);
            }
        }

        // Delete version history entries (set operations require cacheable store)
        var versionStore = _serviceProvider.GetRequiredService<IStateStoreFactory>()
            .GetCacheableStore<StoredBundleVersionRecord>(StateStoreDefinitions.Asset);
        var versionIndexKey = $"bundle-version-index:{bundleId}";
        var versionNumbers = await versionStore.GetSetAsync<int>(versionIndexKey, cancellationToken);
        foreach (var version in versionNumbers)
        {
            await versionStore.DeleteAsync($"bundle-version:{bundleId}:{version}", cancellationToken);
        }
        await versionStore.DeleteAsync(versionIndexKey, cancellationToken);

        // Remove from deleted bundles index
        await bundleStore.RemoveFromSetAsync("deleted-bundles-index", bundleId, cancellationToken: cancellationToken);

        // Publish permanent deletion event
        await messageBus.TryPublishAsync("asset.bundle.deleted", new BundleDeletedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            BundleId = bundleId,
            Permanent = true,
            Reason = "Retention period expired",
            DeletedBy = "system",
            Realm = bundle.Realm
        });

        _logger.LogInformation("BundleCleanup: Permanently deleted bundle {BundleId} (deleted at {DeletedAt})",
            bundleId, bundle.DeletedAt);
    }
}
