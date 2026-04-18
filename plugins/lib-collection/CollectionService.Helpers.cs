using BeyondImmersion.Bannou.Collection.ClientEvents;
using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.GameService;
using BeyondImmersion.BannouService.Helpers;
using BeyondImmersion.BannouService.Inventory;
using BeyondImmersion.BannouService.Item;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Providers;
using BeyondImmersion.BannouService.Resource;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Collection;

// =============================================================================
// CollectionService — Private & Internal Helper Methods
// =============================================================================
//
// This partial class file is the designated home for all private and internal
// helper methods used by CollectionService. This separation exists to
// support the IMPLEMENTATION TENETS telemetry span rules:
//
//   - PRIMARY FILE (CollectionService.cs):
//     Contains ONLY public interface method implementations (the methods
//     declared in ICollectionService). These methods MUST NOT call
//     ITelemetryProvider.StartActivity because the generated controller
//     already wraps each endpoint with a telemetry span. Adding a span
//     in the service method would double-instrument the endpoint.
//
//   - THIS FILE (CollectionService.Helpers.cs):
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
/// Private and internal helper methods for CollectionService.
/// </summary>
/// <remarks>
/// <para>
/// This partial class contains all non-public helper methods. Every async method
/// in this file MUST include a <c>using var activity = _telemetryProvider.StartActivity(...)</c>
/// span per IMPLEMENTATION TENETS (T30). The generated controller instruments the
/// public interface methods; this file instruments the sub-operations.
/// </para>
/// </remarks>
public partial class CollectionService
{
    #region Unlock Listener Dispatch

    /// <summary>
    /// Dispatches unlock notifications to all registered DI listeners.
    /// Listener failures are logged as warnings and never affect the grant operation.
    /// </summary>
    private async Task DispatchUnlockListenersAsync(
        CollectionInstanceModel collection,
        EntryTemplateModel template,
        UnlockedEntryRecord entry,
        CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.collection", "CollectionService.DispatchUnlockListeners");
        if (_unlockListeners.Count == 0) return;

        var notification = new CollectionUnlockNotification(
            CollectionId: collection.CollectionId,
            OwnerId: collection.OwnerId,
            OwnerType: collection.OwnerType,
            GameServiceId: collection.GameServiceId,
            CollectionType: collection.CollectionType,
            EntryCode: template.Code,
            DisplayName: template.DisplayName,
            Category: template.Category,
            Tags: template.Tags,
            DiscoveryLevel: entry.Metadata?.DiscoveryLevel ?? 0);

        foreach (var listener in _unlockListeners)
        {
            try
            {
                await listener.OnEntryUnlockedAsync(notification, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Unlock listener {Listener} failed for entry {EntryCode} in collection {CollectionId}",
                    listener.GetType().Name, template.Code, collection.CollectionId);
            }
        }
    }

    #endregion
    #region Cache Reconciliation

    /// <summary>
    /// Loads or rebuilds the collection cache. Tries Redis cache first; on miss,
    /// queries inventory container contents as the authoritative source and rebuilds the cache.
    /// </summary>
    private async Task<CollectionCacheModel> LoadOrRebuildCollectionCacheAsync(
        CollectionInstanceModel collection,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.collection", "CollectionService.LoadOrRebuildCollectionCache");
        var cache = await _collectionCache.GetAsync(BuildCacheKey(collection.CollectionId), cancellationToken);
        if (cache != null)
        {
            return cache;
        }

        _logger.LogInformation("Collection cache miss for collection {CollectionId}, rebuilding from inventory",
            collection.CollectionId);

        ContainerWithContentsResponse containerContents;
        try
        {
            containerContents = await _inventoryClient.GetContainerAsync(
                new GetContainerRequest { ContainerId = collection.ContainerId, IncludeContents = true },
                cancellationToken);
        }
        catch (ApiException ex)
        {
            _logger.LogError(ex,
                "Failed to load container {ContainerId} for cache rebuild of collection {CollectionId}",
                collection.ContainerId, collection.CollectionId);
            await _messageBus.TryPublishErrorAsync(
                "collection",
                "LoadOrRebuildCollectionCache",
                "ContainerLoadFailure",
                ex.Message,
                dependency: "inventory",
                endpoint: "get-container",
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);

            // Graceful degradation: return empty cache on inventory failure.
            // The cache will attempt to rebuild on the next access.
            return new CollectionCacheModel
            {
                CollectionId = collection.CollectionId,
                UnlockedEntries = new List<UnlockedEntryRecord>(),
                LastUpdated = DateTimeOffset.UtcNow
            };
        }

        // Load all entry templates for this collection type + game service to match items
        var templates = await _entryTemplateStore.QueryAsync(
            t => t.CollectionType == collection.CollectionType && t.GameServiceId == collection.GameServiceId,
            cancellationToken: cancellationToken);

        var templatesByItemTemplateId = templates
            .GroupBy(t => t.ItemTemplateId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var unlockedEntries = new List<UnlockedEntryRecord>();
        foreach (var item in containerContents.Items)
        {
            if (templatesByItemTemplateId.TryGetValue(item.TemplateId, out var matchingTemplates))
            {
                var matchedTemplate = matchingTemplates.FirstOrDefault(t =>
                    !unlockedEntries.Any(u => u.Code == t.Code));

                if (matchedTemplate != null)
                {
                    unlockedEntries.Add(new UnlockedEntryRecord
                    {
                        Code = matchedTemplate.Code,
                        EntryTemplateId = matchedTemplate.EntryTemplateId,
                        ItemInstanceId = item.InstanceId,
                        UnlockedAt = collection.CreatedAt, // Best approximation when rebuilding
                        Metadata = new EntryMetadataModel()
                    });
                }
            }
        }

        cache = new CollectionCacheModel
        {
            CollectionId = collection.CollectionId,
            UnlockedEntries = unlockedEntries,
            LastUpdated = DateTimeOffset.UtcNow
        };

        await _collectionCache.SaveAsync(
            BuildCacheKey(collection.CollectionId),
            cache,
            new StateOptions { Ttl = _configuration.CollectionCacheTtlSeconds },
            cancellationToken);

        return cache;
    }

    #endregion
    #region Collection Auto-Create Helper

    /// <summary>
    /// Creates a new collection instance with an inventory container.
    /// Shared logic used by both CreateCollectionAsync and GrantEntryAsync (auto-create).
    /// </summary>
    private async Task<CollectionInstanceModel> CreateCollectionInternalAsync(
        Guid ownerId,
        EntityType ownerType,
        string collectionType,
        Guid gameServiceId,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.collection", "CollectionService.CreateCollectionInternal");
        var containerOwnerType = MapToContainerOwnerType(ownerType) ?? throw new InvalidOperationException(
                $"Owner type '{ownerType}' cannot be mapped to a ContainerOwnerType for inventory operations");
        ContainerResponse containerResponse;
        try
        {
            containerResponse = await _inventoryClient.CreateContainerAsync(
                new CreateContainerRequest
                {
                    OwnerId = ownerId,
                    OwnerType = containerOwnerType,
                    ContainerType = $"collection_{collectionType}",
                    ConstraintModel = ContainerConstraintModel.Unlimited
                },
                cancellationToken);
        }
        catch (ApiException ex)
        {
            _logger.LogError(ex,
                "Failed to create inventory container for {CollectionType} collection for {OwnerType} {OwnerId}",
                collectionType, ownerType, ownerId);
            throw;
        }

        var now = DateTimeOffset.UtcNow;
        var instance = new CollectionInstanceModel
        {
            CollectionId = Guid.NewGuid(),
            OwnerId = ownerId,
            OwnerType = ownerType,
            CollectionType = collectionType,
            GameServiceId = gameServiceId,
            ContainerId = containerResponse.ContainerId,
            CreatedAt = now
        };

        await _collectionStore.SaveAsync(
            BuildCollectionKey(instance.CollectionId),
            instance,
            cancellationToken: cancellationToken);

        await _collectionStore.SaveAsync(
            BuildCollectionByOwnerKey(ownerId, ownerType, gameServiceId, collectionType),
            instance,
            cancellationToken: cancellationToken);

        _instanceEventBatcher.AddCreated(new CollectionBatchEntry
        {
            CollectionId = instance.CollectionId,
            OwnerId = instance.OwnerId,
            OwnerType = instance.OwnerType,
            CollectionType = instance.CollectionType,
            GameServiceId = instance.GameServiceId,
            ContainerId = instance.ContainerId,
            CreatedAt = instance.CreatedAt,
            UpdatedAt = instance.CreatedAt
        });

        // Register reference with lib-resource for character-owned collections per FOUNDATION TENETS
        if (ownerType == EntityType.Character)
        {
            await RegisterCharacterReferenceAsync(
                instance.CollectionId.ToString(), ownerId, cancellationToken);
        }

        _logger.LogInformation(
            "Created collection {CollectionId} of type {CollectionType} for {OwnerType} {OwnerId}",
            instance.CollectionId, collectionType, ownerType, ownerId);

        return instance;
    }

    #endregion
    #region Milestone Checking

    /// <summary>
    /// Checks completion percentage and publishes milestone events at 25%, 50%, 75%, 100%.
    /// </summary>
    private async Task CheckAndPublishMilestonesAsync(
        CollectionInstanceModel collection,
        int unlockedCount,
        int totalCount,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.collection", "CollectionService.CheckAndPublishMilestones");
        if (totalCount == 0) return;

        var percentage = (double)unlockedCount / totalCount * 100.0;
        var milestones = new[] { 25.0, 50.0, 75.0, 100.0 };

        foreach (var milestone in milestones)
        {
            // Check if we just crossed this milestone (previous count was below, current is at or above)
            var previousPercentage = (double)(unlockedCount - 1) / totalCount * 100.0;
            if (previousPercentage < milestone && percentage >= milestone)
            {
                await _messageBus.PublishCollectionMilestoneReachedAsync(
                    new CollectionMilestoneReachedEvent
                    {
                        EventId = Guid.NewGuid(),
                        Timestamp = DateTimeOffset.UtcNow,
                        CollectionId = collection.CollectionId,
                        OwnerId = collection.OwnerId,
                        OwnerType = collection.OwnerType,
                        CollectionType = collection.CollectionType,
                        GameServiceId = collection.GameServiceId,
                        Milestone = $"{(int)milestone}%",
                        CompletionPercentage = percentage
                    },
                    cancellationToken);

                // Push milestone client event to collection owner's WebSocket sessions
                await _entitySessionRegistry.PublishToEntitySessionsAsync(
                    collection.OwnerType.ToString().ToLowerInvariant(), collection.OwnerId,
                    new CollectionMilestoneReachedClientEvent
                    {
                        EventId = Guid.NewGuid(),
                        Timestamp = DateTimeOffset.UtcNow,
                        CollectionId = collection.CollectionId,
                        CollectionType = collection.CollectionType,
                        Milestone = $"{(int)milestone}%",
                        CompletionPercentage = percentage
                    },
                    cancellationToken);

                _logger.LogInformation(
                    "Collection {CollectionId} reached {Milestone} milestone ({Percentage:F1}%)",
                    collection.CollectionId, $"{(int)milestone}%", percentage);
            }
        }
    }

    #endregion
    /// <summary>
    /// Builds a response using the area config's default entry.
    /// </summary>
    private async Task<(StatusCodes, ContentSelectionResponse?)> BuildDefaultContentResponseAsync(
        AreaContentConfigModel areaConfig,
        Guid gameServiceId,
        string collectionType,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.collection", "CollectionService.BuildDefaultContentResponse");
        var defaultTemplate = await _entryTemplateStore.GetAsync(
            BuildTemplateByCodeKey(gameServiceId, collectionType, areaConfig.DefaultEntryCode),
            cancellationToken);

        if (defaultTemplate == null)
        {
            _logger.LogWarning(
                "Default entry {DefaultEntryCode} not found for area {AreaCode}",
                areaConfig.DefaultEntryCode, areaConfig.AreaCode);
            return (StatusCodes.NotFound, null);
        }

        return (StatusCodes.OK, new ContentSelectionResponse
        {
            EntryCode = defaultTemplate.Code,
            DisplayName = defaultTemplate.DisplayName,
            Category = defaultTemplate.Category,
            AssetId = defaultTemplate.AssetId,
            ThumbnailAssetId = defaultTemplate.ThumbnailAssetId,
            Themes = defaultTemplate.Themes?.ToList(),
            MatchedThemes = new List<string>()
        });
    }
}
