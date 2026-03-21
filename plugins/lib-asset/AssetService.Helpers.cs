using BeyondImmersion.Bannou.Asset.ClientEvents;
using BeyondImmersion.Bannou.Bundle.Format;
using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Asset.Bundles;
using BeyondImmersion.BannouService.Asset.Events;
using BeyondImmersion.BannouService.Asset.Models;
using BeyondImmersion.BannouService.Asset.Pool;
using BeyondImmersion.BannouService.Asset.Storage;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Orchestrator;
using BeyondImmersion.BannouService.ServiceClients;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using StorageModels = BeyondImmersion.BannouService.Storage;

namespace BeyondImmersion.BannouService.Asset;

// =============================================================================
// AssetService — Private & Internal Helper Methods
// =============================================================================
//
// This partial class file is the designated home for all private and internal
// helper methods used by AssetService. This separation exists to
// support the IMPLEMENTATION TENETS telemetry span rules:
//
//   - PRIMARY FILE (AssetService.cs):
//     Contains ONLY public interface method implementations (the methods
//     declared in IAssetService). These methods MUST NOT call
//     ITelemetryProvider.StartActivity because the generated controller
//     already wraps each endpoint with a telemetry span. Adding a span
//     in the service method would double-instrument the endpoint.
//
//   - THIS FILE (AssetService.Helpers.cs):
//     Contains all private/internal helper methods, core logic extracted
//     from endpoints, event publishing helpers, query builders, mapping
//     functions, and any other non-public methods. Every async method in
//     this file MUST call ITelemetryProvider.StartActivity to ensure
//     sub-operations are properly instrumented.
//
// Structural tests enforce both rules:
//   - Services_PrimaryFile_DoesNotCallStartActivity
//   - Services_HelperFiles_HaveStartActivityWhenAsync
//
// WHAT GOES HERE:
//   - Private async helper methods (with StartActivity spans)
//   - Private sync helper methods (query builders, mappers, validators)
//   - Internal static key builders (already in primary file by convention,
//     but may be moved here if the primary file is large)
//   - Event publishing helper methods
//   - Any extracted "core" logic (e.g., CreateAccountCoreAsync)
//
// WHAT STAYS IN THE PRIMARY FILE:
//   - Public interface method implementations (/// <inheritdoc/> methods)
//   - Constructor and field declarations
//   - Constants and key prefix definitions
//
// See: docs/reference/tenets/IMPLEMENTATION-BEHAVIOR.md (T30)
// See: docs/reference/HELPERS-AND-COMMON-PATTERNS.md
// =============================================================================

/// <summary>
/// Private and internal helper methods for AssetService.
/// </summary>
/// <remarks>
/// <para>
/// This partial class contains all non-public helper methods. Every async method
/// in this file MUST include a <c>using var activity = _telemetryProvider.StartActivity(...)</c>
/// span per IMPLEMENTATION TENETS (T30). The generated controller instruments the
/// public interface methods; this file instruments the sub-operations.
/// </para>
/// </remarks>
public partial class AssetService
{
    /// <summary>
    /// Fallback search using index keys when RedisSearch is not available.
    /// Used when Redis Stack is not configured or search module is unavailable.
    /// </summary>
    private async Task<(StatusCodes, AssetSearchResult?)> SearchAssetsIndexFallbackAsync(
        AssetSearchRequest body,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.asset", "AssetService.SearchAssetsIndexFallbackAsync");
        var matchingAssets = new List<AssetMetadata>();

        // Search by asset type index
        var indexKey = $"{_configuration.AssetIndexKeyPrefix}type:{body.AssetType.ToString().ToLowerInvariant()}";
        var assetIds = await _stringListIndexStore.GetAsync(indexKey, cancellationToken).ConfigureAwait(false);

        if (assetIds != null)
        {
            foreach (var assetId in assetIds)
            {
                var internalRecord = await _internalAssetRecordStore.GetAsync($"{_configuration.AssetKeyPrefix}{assetId}", cancellationToken).ConfigureAwait(false);

                if (internalRecord != null)
                {
                    // Apply filters
                    var matchesRealm = internalRecord.Realm == body.Realm;
                    var matchesTags = body.Tags == null || body.Tags.Count == 0 ||
                        (internalRecord.Tags != null && body.Tags.All(t => internalRecord.Tags.Contains(t)));
                    var matchesContentType = string.IsNullOrEmpty(body.ContentType) ||
                        internalRecord.ContentType == body.ContentType;

                    if (matchesRealm && matchesTags && matchesContentType)
                    {
                        matchingAssets.Add(internalRecord.ToPublicMetadata());
                    }
                }
            }
        }

        // Apply pagination
        var total = matchingAssets.Count;
        var paginatedAssets = matchingAssets
            .OrderByDescending(a => a.CreatedAt)
            .Skip(body.Offset)
            .Take(body.Limit)
            .ToList();

        var response = new AssetSearchResult
        {
            Assets = paginatedAssets,
            Total = total,
            Limit = body.Limit,
            Offset = body.Offset
        };

        return (StatusCodes.OK, response);
    }
    private async Task IndexAssetAsync(AssetMetadata asset, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.asset", "AssetService.IndexAssetAsync");
        // Index by asset type
        var typeIndexKey = $"{_configuration.AssetIndexKeyPrefix}type:{asset.AssetType.ToString().ToLowerInvariant()}";
        await AddToIndexWithOptimisticConcurrencyAsync(typeIndexKey, asset.AssetId, cancellationToken).ConfigureAwait(false);

        // Index by realm (skip if cross-realm asset with no realm)
        if (asset.Realm != null)
        {
            var realmIndexKey = $"{_configuration.AssetIndexKeyPrefix}realm:{asset.Realm.ToLowerInvariant()}";
            await AddToIndexWithOptimisticConcurrencyAsync(realmIndexKey, asset.AssetId, cancellationToken).ConfigureAwait(false);
        }

        // Index by tags
        if (asset.Tags != null)
        {
            foreach (var tag in asset.Tags)
            {
                var tagIndexKey = $"{_configuration.AssetIndexKeyPrefix}tag:{tag.ToLowerInvariant()}";
                await AddToIndexWithOptimisticConcurrencyAsync(tagIndexKey, asset.AssetId, cancellationToken).ConfigureAwait(false);
            }
        }
    }
    /// <summary>
    /// Adds an asset ID to an index using ETag-based optimistic concurrency.
    /// Retries on concurrent modification conflicts.
    /// </summary>
    private async Task AddToIndexWithOptimisticConcurrencyAsync(
        string indexKey,
        string assetId,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.asset", "AssetService.AddToIndexWithOptimisticConcurrencyAsync");
        var maxRetries = _configuration.IndexOptimisticRetryMaxAttempts;

        for (var attempt = 0; attempt < maxRetries; attempt++)
        {
            // Get current state with ETag for optimistic concurrency
            var (index, etag) = await _stringListIndexStore.GetWithETagAsync(indexKey, cancellationToken).ConfigureAwait(false);

            index ??= new List<string>();

            // Already indexed, no update needed
            if (index.Contains(assetId))
            {
                return;
            }

            // Add asset ID
            index.Add(assetId);

            // Try to save with ETag (fails if state changed since read)
            if (etag == null)
            {
                // First time creating this index
                await _stringListIndexStore.SaveAsync(indexKey, index, cancellationToken: cancellationToken).ConfigureAwait(false);
                return; // Success
            }

            var savedEtag = await _stringListIndexStore.TrySaveAsync(indexKey, index, etag, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (savedEtag != null)
            {
                return; // Success
            }

            // ETag mismatch - retry after brief delay
            _logger.LogDebug(
                "Index update conflict for {IndexKey}, retrying (attempt {Attempt}/{MaxRetries})",
                indexKey, attempt + 1, maxRetries);

            await Task.Delay(TimeSpan.FromMilliseconds(_configuration.IndexOptimisticRetryBaseDelayMs * (attempt + 1)), cancellationToken).ConfigureAwait(false);
        }

        _logger.LogError(
            "Failed to update index {IndexKey} for asset {AssetId} after {MaxRetries} attempts due to concurrent modifications — asset will not appear in search results for this index",
            indexKey, assetId, maxRetries);

        await _messageBus.TryPublishErrorAsync(
            "asset",
            "AddToIndexWithOptimisticConcurrency",
            "IndexRetryExhausted",
            $"Failed to update index {indexKey} for asset {assetId} after {maxRetries} attempts",
            dependency: "state",
            endpoint: $"redis:{indexKey}",
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }
    /// <summary>
    /// Indexes all assets in a bundle for reverse lookup (asset → bundles).
    /// </summary>
    private async Task IndexBundleAssetsAsync(BundleMetadata bundle, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.asset", "AssetService.IndexBundleAssetsAsync");
        var realmPrefix = (bundle.Realm ?? "cross-realm").ToLowerInvariant();

        foreach (var assetId in bundle.AssetIds)
        {
            var indexKey = $"{realmPrefix}:asset-bundles:{assetId}";
            await AddBundleToAssetIndexAsync(_assetBundleIndexStore, indexKey, bundle.BundleId, cancellationToken).ConfigureAwait(false);
        }

        _logger.LogDebug("IndexBundleAssets: Indexed {AssetCount} assets for bundle {BundleId}",
            bundle.AssetIds.Count, bundle.BundleId);
    }
    /// <summary>
    /// Adds a bundle ID to an asset's reverse index using optimistic concurrency.
    /// </summary>
    private async Task AddBundleToAssetIndexAsync(
        IStateStore<AssetBundleIndex> indexStore,
        string indexKey,
        string bundleId,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.asset", "AssetService.AddBundleToAssetIndexAsync");
        var maxRetries = _configuration.IndexOptimisticRetryMaxAttempts;

        for (var attempt = 0; attempt < maxRetries; attempt++)
        {
            var (index, etag) = await indexStore.GetWithETagAsync(indexKey, cancellationToken).ConfigureAwait(false);

            index ??= new AssetBundleIndex();

            // Already indexed
            if (index.BundleIds.Contains(bundleId))
            {
                return;
            }

            index.BundleIds.Add(bundleId);

            if (etag == null)
            {
                await indexStore.SaveAsync(indexKey, index, cancellationToken: cancellationToken).ConfigureAwait(false);
                return;
            }

            var savedEtag = await indexStore.TrySaveAsync(indexKey, index, etag, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (savedEtag != null)
            {
                return;
            }

            // ETag mismatch - retry
            await Task.Delay(TimeSpan.FromMilliseconds(_configuration.IndexOptimisticRetryBaseDelayMs * (attempt + 1)), cancellationToken).ConfigureAwait(false);
        }

        _logger.LogError(
            "Failed to update asset-bundle index {IndexKey} for bundle {BundleId} after {MaxRetries} attempts — bundle will not appear in reverse lookup for this asset",
            indexKey, bundleId, maxRetries);

        await _messageBus.TryPublishErrorAsync(
            "asset",
            "AddBundleToAssetIndex",
            "IndexRetryExhausted",
            $"Failed to update asset-bundle index {indexKey} for bundle {bundleId} after {maxRetries} attempts",
            dependency: "state",
            endpoint: $"redis:{indexKey}",
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }
    /// <summary>
    /// Ensures at least one processor is available for the given pool type.
    /// If no processors are available, spawns a new one via the orchestrator.
    /// </summary>
    /// <param name="poolType">The pool type to check/spawn.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if a processor is available (or was spawned), false if spawning failed.</returns>
    private async Task<bool> EnsureProcessorAvailableAsync(
        string poolType,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.asset", "AssetService.EnsureProcessorAvailableAsync");
        try
        {
            // Check if any processors are available in the pool
            var availableCount = await _processorPoolManager.GetAvailableCountAsync(poolType, cancellationToken);

            if (availableCount > 0)
            {
                _logger.LogDebug(
                    "EnsureProcessorAvailable: {Count} processors available in pool {PoolType}",
                    availableCount, poolType);
                return true;
            }

            // No processors available - spawn one via orchestrator (L3 soft dependency)
            var orchestratorClient = _serviceProvider.GetService<IOrchestratorClient>();
            if (orchestratorClient == null)
            {
                _logger.LogDebug("EnsureProcessorAvailable: Orchestrator not enabled, cannot scale pool {PoolType}", poolType);
                return false;
            }

            _logger.LogInformation(
                "EnsureProcessorAvailable: No processors available in pool {PoolType}, spawning new instance",
                poolType);

            // Get current total count to calculate target
            var totalCount = await _processorPoolManager.GetTotalNodeCountAsync(poolType, cancellationToken);

            // Request orchestrator to scale up by 1
            var scaleResponse = await orchestratorClient.ScalePoolAsync(
                new ScalePoolRequest
                {
                    PoolType = poolType,
                    TargetInstances = totalCount + 1
                },
                cancellationToken);

            _logger.LogInformation(
                "EnsureProcessorAvailable: Orchestrator scaled pool {PoolType} from {Previous} to {Current} instances",
                poolType, scaleResponse.PreviousInstances, scaleResponse.CurrentInstances);

            // Wait for the new processor to register (poll state)
            var maxWait = TimeSpan.FromSeconds(_configuration.ProcessorAvailabilityMaxWaitSeconds);
            var pollInterval = TimeSpan.FromSeconds(_configuration.ProcessorAvailabilityPollIntervalSeconds);
            var elapsed = TimeSpan.Zero;

            while (elapsed < maxWait)
            {
                await Task.Delay(pollInterval, cancellationToken);
                elapsed += pollInterval;

                availableCount = await _processorPoolManager.GetAvailableCountAsync(poolType, cancellationToken);
                if (availableCount > 0)
                {
                    _logger.LogInformation(
                        "EnsureProcessorAvailable: Processor registered in pool {PoolType} after {Elapsed:F1}s",
                        poolType, elapsed.TotalSeconds);
                    return true;
                }
            }

            _logger.LogWarning(
                "EnsureProcessorAvailable: Spawned processor did not register within {Timeout}s timeout for pool {PoolType}",
                maxWait.TotalSeconds, poolType);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "EnsureProcessorAvailable: Failed to ensure processor availability for pool {PoolType}",
                poolType);
            return false;
        }
    }
    /// <summary>
    /// Delegates processing to the processing pool.
    /// Spawns a processor if none available, then acquires and publishes job.
    /// </summary>
    private async Task DelegateToProcessingPoolAsync(
        string assetId,
        AssetMetadata metadata,
        string storageKey,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.asset", "AssetService.DelegateToProcessingPoolAsync");
        var poolType = GetProcessorPoolType(metadata.ContentType);

        try
        {
            // Ensure at least one processor is available, spawning if needed
            var ensured = await EnsureProcessorAvailableAsync(poolType, cancellationToken);
            if (!ensured)
            {
                _logger.LogWarning(
                    "DelegateToProcessingPool: Could not ensure processor availability for pool {PoolType}, queueing asset {AssetId} for retry",
                    poolType, assetId);

                // Publish delayed retry event
                await _messageBus.PublishAssetProcessingRetryAsync(new AssetProcessingRetryEvent
                {
                    EventId = Guid.NewGuid(),
                    Timestamp = DateTimeOffset.UtcNow,
                    AssetId = assetId,
                    StorageKey = storageKey,
                    ContentType = metadata.ContentType,
                    PoolType = poolType,
                    RetryCount = 0,
                    MaxRetries = _configuration.ProcessingMaxRetries,
                    RetryDelaySeconds = _configuration.ProcessingRetryDelaySeconds
                }).ConfigureAwait(false);
                return;
            }

            _logger.LogInformation(
                "DelegateToProcessingPool: Acquiring processor from pool {PoolType} for asset {AssetId}",
                poolType, assetId);

            // Try to acquire a processor from the pool (L3 soft dependency)
            var orchestratorClient = _serviceProvider.GetService<IOrchestratorClient>();
            if (orchestratorClient == null)
            {
                _logger.LogWarning("DelegateToProcessingPool: Orchestrator not enabled, cannot acquire processor for asset {AssetId}", assetId);
                return;
            }

            var processorResponse = await orchestratorClient.AcquireProcessorAsync(
                new AcquireProcessorRequest
                {
                    PoolType = poolType,
                    Priority = 0,
                    TimeoutSeconds = _configuration.ProcessorAcquisitionTimeoutSeconds,
                    Metadata = new { AssetId = assetId, StorageKey = storageKey }
                },
                cancellationToken).ConfigureAwait(false);

            // Update asset metadata to Processing status
            metadata.ProcessingStatus = ProcessingStatus.Processing;
            await _assetMetadataStore.SaveAsync($"{_configuration.AssetKeyPrefix}{assetId}", metadata, cancellationToken: cancellationToken).ConfigureAwait(false);

            _logger.LogInformation(
                "DelegateToProcessingPool: Acquired processor {ProcessorId} from pool {PoolType} for asset {AssetId}, lease expires at {ExpiresAt}",
                processorResponse.ProcessorId, poolType, assetId, processorResponse.ExpiresAt);

            // Publish processing job event for the processor to pick up
            await _messageBus.PublishAssetProcessingJobDispatchedAsync(new AssetProcessingJobDispatchedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                AssetId = assetId,
                StorageKey = storageKey,
                ContentType = metadata.ContentType,
                SizeBytes = metadata.Size,
                Filename = metadata.Filename,
                PoolType = poolType,
                ProcessorId = processorResponse.ProcessorId,
                AppId = processorResponse.AppId,
                LeaseId = processorResponse.LeaseId,
                ExpiresAt = processorResponse.ExpiresAt
            }, poolType).ConfigureAwait(false);

            // Publish asset.processing.queued event for service-level tracking
            await _messageBus.PublishAssetProcessingQueuedAsync(
                new BeyondImmersion.BannouService.Events.AssetProcessingQueuedEvent
                {
                    EventId = Guid.NewGuid(),
                    Timestamp = DateTimeOffset.UtcNow,
                    AssetId = assetId,
                    ProcessingType = MapProcessingType(metadata.ContentType),
                    ProcessorAppId = processorResponse.AppId
                }).ConfigureAwait(false);
        }
        catch (ApiException ex) when (ex.StatusCode == 429)
        {
            // Pool is busy - queue for retry
            _logger.LogWarning(
                "DelegateToProcessingPool: No processors available in pool {PoolType}, queueing asset {AssetId} for retry",
                poolType, assetId);

            // Publish delayed retry event
            await _messageBus.PublishAssetProcessingRetryAsync(new AssetProcessingRetryEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                AssetId = assetId,
                StorageKey = storageKey,
                ContentType = metadata.ContentType,
                PoolType = poolType,
                RetryCount = 0,
                MaxRetries = _configuration.ProcessingMaxRetries,
                RetryDelaySeconds = _configuration.ProcessingRetryDelaySeconds
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "DelegateToProcessingPool: Failed to delegate asset {AssetId} to pool {PoolType}",
                assetId, poolType);

            // Mark as failed
            metadata.ProcessingStatus = ProcessingStatus.Failed;
            await _assetMetadataStore.SaveAsync($"{_configuration.AssetKeyPrefix}{assetId}", metadata, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
    }
    /// <summary>
    /// Helper to remove bundle from per-asset reverse indexes.
    /// Key format matches IndexBundleAssetsAsync: {realmPrefix}:asset-bundles:{assetId}
    /// </summary>
    private async Task RemoveFromBundleIndexAsync(string bundleId, List<string> assetIds, string? realm, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.asset", "AssetService.RemoveFromBundleIndexAsync");
        var realmPrefix = (realm ?? "cross-realm").ToLowerInvariant();

        foreach (var assetId in assetIds)
        {
            var indexKey = $"{realmPrefix}:asset-bundles:{assetId}";
            var index = await _assetBundleIndexStore.GetAsync(indexKey, cancellationToken);

            if (index != null && index.BundleIds.Contains(bundleId))
            {
                index.BundleIds.Remove(bundleId);

                if (index.BundleIds.Count == 0)
                {
                    await _assetBundleIndexStore.DeleteAsync(indexKey, cancellationToken);
                }
                else
                {
                    await _assetBundleIndexStore.SaveAsync(indexKey, index, cancellationToken: cancellationToken);
                }
            }
        }
    }
    /// <summary>
    /// Creates an async metabundle job and returns a queued response.
    /// </summary>
    private async Task<(StatusCodes, CreateMetabundleResponse?)> CreateMetabundleJobAsync(
        CreateMetabundleRequest request,
        List<BundleMetadata> sourceBundles,
        List<InternalAssetRecord> standaloneAssets,
        List<(StoredBundleAssetEntry Entry, string SourceBundleId)> assetsToInclude,
        List<InternalAssetRecord> standalonesToInclude,
        int totalAssetCount,
        long totalSizeBytes,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.asset", "AssetService.CreateMetabundleJobAsync");
        var jobId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        // Capture requester session ID for completion notification
        var requesterSessionId = ServiceRequestContext.SessionId;

        // Create job record
        var job = new MetabundleJob
        {
            JobId = jobId,
            MetabundleId = request.MetabundleId,
            Status = BundleStatus.Queued,
            Request = request,
            RequesterSessionId = !string.IsNullOrEmpty(requesterSessionId) ? Guid.Parse(requesterSessionId) : null,
            CreatedAt = now,
            UpdatedAt = now
        };

        // Save job to state store with TTL for automatic cleanup
        var jobKey = $"{_configuration.MetabundleJobKeyPrefix}{jobId}";
        await _metabundleJobStore.SaveAsync(jobKey, job,
            new StateOptions { Ttl = _configuration.MetabundleJobTtlSeconds },
            cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "CreateMetabundle: Created async job {JobId} for metabundle {MetabundleId} with {AssetCount} assets",
            jobId, request.MetabundleId, totalAssetCount);

        // Publish job to processing queue
        await _messageBus.PublishMetabundleJobQueuedAsync(
            new MetabundleJobQueuedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                JobId = jobId,
                MetabundleId = request.MetabundleId,
                SourceBundleCount = sourceBundles.Count,
                AssetCount = totalAssetCount,
                EstimatedSizeBytes = totalSizeBytes,
                RequesterSessionId = !string.IsNullOrEmpty(requesterSessionId) ? Guid.Parse(requesterSessionId) : null
            }).ConfigureAwait(false);

        // Build provenance data for response
        var sourceBundleRefs = sourceBundles.Select(sb => new SourceBundleReference
        {
            BundleId = sb.BundleId,
            Version = sb.Version,
            AssetIds = assetsToInclude
                .Where(a => a.SourceBundleId == sb.BundleId)
                .Select(a => a.Entry.AssetId)
                .ToList(),
            ContentHash = sb.StorageKey // Use storage key as proxy for content hash
        }).ToList();

        return (StatusCodes.OK, new CreateMetabundleResponse
        {
            MetabundleId = request.MetabundleId,
            JobId = jobId,
            Status = BundleStatus.Queued,
            AssetCount = totalAssetCount,
            StandaloneAssetCount = standalonesToInclude.Count,
            SizeBytes = totalSizeBytes,
            SourceBundles = sourceBundleRefs
        });
    }
}
