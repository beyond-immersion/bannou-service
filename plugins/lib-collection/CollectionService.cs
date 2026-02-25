using BeyondImmersion.Bannou.Collection.ClientEvents;
using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.GameService;
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

/// <summary>
/// Topic constants for collection events.
/// </summary>
public static class CollectionTopics
{
    /// <summary>Entry unlocked event topic.</summary>
    public const string EntryUnlocked = "collection.entry-unlocked";
    /// <summary>Entry grant failed event topic.</summary>
    public const string EntryGrantFailed = "collection.entry-grant-failed";
    /// <summary>Milestone reached event topic.</summary>
    public const string MilestoneReached = "collection.milestone-reached";
    /// <summary>Discovery advanced event topic.</summary>
    public const string DiscoveryAdvanced = "collection.discovery-advanced";
}

/// <summary>
/// Implementation of the Collection service.
/// Manages collectible content (voice galleries, scene archives, music libraries, bestiaries, etc.)
/// using the items-in-inventories pattern: entry templates define what can be collected,
/// collection instances create inventory containers per owner, and granting an entry
/// creates an item instance in that container.
/// </summary>
/// <remarks>
/// <para>
/// <b>FOUNDATION TENETS - PARTIAL CLASS REQUIRED:</b> This class MUST remain a partial class.
/// Generated code (event handlers, permissions) is placed in companion partial classes.
/// </para>
/// <para>
/// <b>SERVICE HIERARCHY (L2 Game Foundation):</b>
/// <list type="bullet">
///   <item>Hard dependencies (constructor injection): IInventoryClient (L2), IItemClient (L2), IGameServiceClient (L2), IDistributedLockProvider (L0), IEntitySessionRegistry (L1)</item>
///   <item>DI listeners (ICollectionUnlockListener): Seed (L2) receives in-process unlock notifications for growth pipeline</item>
///   <item>Client events: Pushes unlock/milestone/discovery events to owner WebSocket sessions via IEntitySessionRegistry</item>
///   <item>No owner-specific client dependencies: owner validation is caller-responsibility</item>
/// </list>
/// </para>
/// <para>
/// <b>RELATED FILES:</b>
/// <list type="bullet">
///   <item>Internal data models: CollectionServiceModels.cs (storage models, cache entries, internal DTOs)</item>
///   <item>Event handlers: CollectionServiceEvents.cs (event consumer registration and handlers)</item>
///   <item>Request/Response models: bannou-service/Generated/Models/CollectionModels.cs</item>
///   <item>Event models: bannou-service/Generated/Events/CollectionEventsModels.cs</item>
///   <item>Lifecycle events: bannou-service/Generated/Events/CollectionLifecycleEvents.cs</item>
///   <item>Configuration: Generated/CollectionServiceConfiguration.cs</item>
///   <item>State stores: bannou-service/Generated/StateStoreDefinitions.cs</item>
/// </list>
/// </para>
/// </remarks>
[BannouService("collection", typeof(ICollectionService), lifetime: ServiceLifetime.Scoped, layer: ServiceLayer.GameFoundation)]
public partial class CollectionService : ICollectionService
{
    private readonly IMessageBus _messageBus;
    private readonly IStateStoreFactory _stateStoreFactory;
    private readonly ILogger<CollectionService> _logger;
    private readonly CollectionServiceConfiguration _configuration;
    private readonly IInventoryClient _inventoryClient;
    private readonly IItemClient _itemClient;
    private readonly IGameServiceClient _gameServiceClient;
    private readonly IDistributedLockProvider _lockProvider;
    private readonly IResourceClient _resourceClient;
    private readonly ITelemetryProvider _telemetryProvider;
    private readonly IEntitySessionRegistry _entitySessionRegistry;
    private readonly IReadOnlyList<ICollectionUnlockListener> _unlockListeners;

    #region State Store Accessors

    private IQueryableStateStore<EntryTemplateModel>? _entryTemplateStore;
    private IQueryableStateStore<EntryTemplateModel> EntryTemplateStore =>
        _entryTemplateStore ??= _stateStoreFactory.GetQueryableStore<EntryTemplateModel>(StateStoreDefinitions.CollectionEntryTemplates);

    private IQueryableStateStore<CollectionInstanceModel>? _collectionStore;
    private IQueryableStateStore<CollectionInstanceModel> CollectionStore =>
        _collectionStore ??= _stateStoreFactory.GetQueryableStore<CollectionInstanceModel>(StateStoreDefinitions.CollectionInstances);

    private IQueryableStateStore<AreaContentConfigModel>? _areaContentStore;
    private IQueryableStateStore<AreaContentConfigModel> AreaContentStore =>
        _areaContentStore ??= _stateStoreFactory.GetQueryableStore<AreaContentConfigModel>(StateStoreDefinitions.CollectionAreaContentConfigs);

    private IStateStore<CollectionCacheModel>? _collectionCache;
    private IStateStore<CollectionCacheModel> CollectionCache =>
        _collectionCache ??= _stateStoreFactory.GetStore<CollectionCacheModel>(StateStoreDefinitions.CollectionCache);

    private ICacheableStateStore<CollectionCacheModel>? _cacheableCollectionCache;
    private ICacheableStateStore<CollectionCacheModel> CacheableCollectionCache =>
        _cacheableCollectionCache ??= _stateStoreFactory.GetCacheableStore<CollectionCacheModel>(StateStoreDefinitions.CollectionCache);

    #endregion

    #region Key Building

    private static string BuildTemplateKey(Guid entryTemplateId) => $"tpl:{entryTemplateId}";
    private static string BuildTemplateByCodeKey(Guid gameServiceId, string collectionType, string code) =>
        $"tpl:{gameServiceId}:{collectionType}:{code}";
    private static string BuildCollectionKey(Guid collectionId) => $"col:{collectionId}";
    private static string BuildCollectionByOwnerKey(Guid ownerId, EntityType ownerType, Guid gameServiceId, string collectionType) =>
        $"col:{ownerId}:{ownerType.ToString().ToLowerInvariant()}:{gameServiceId}:{collectionType}";
    private static string BuildAreaContentKey(Guid areaConfigId) => $"acc:{areaConfigId}";
    private static string BuildAreaContentByCodeKey(Guid gameServiceId, string collectionType, string areaCode) =>
        $"acc:{gameServiceId}:{collectionType}:{areaCode}";
    private static string BuildCacheKey(Guid collectionId) => $"cache:{collectionId}";
    private static string BuildGlobalUnlocksSetKey(Guid gameServiceId, string collectionType) =>
        $"global-unlocks:{gameServiceId}:{collectionType}";

    #endregion

    #region Owner Type Mapping

    /// <summary>
    /// Maps an EntityType to ContainerOwnerType for inventory operations.
    /// Returns null if the entity type has no known mapping.
    /// </summary>
    private static ContainerOwnerType? MapToContainerOwnerType(EntityType ownerType) => ownerType switch
    {
        EntityType.Character => ContainerOwnerType.Character,
        EntityType.Account => ContainerOwnerType.Account,
        EntityType.Location => ContainerOwnerType.Location,
        EntityType.Guild => ContainerOwnerType.Guild,
        _ => null
    };

    #endregion

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

    /// <summary>
    /// Initializes a new instance of the <see cref="CollectionService"/> class.
    /// </summary>
    /// <param name="messageBus">Message bus for event publishing.</param>
    /// <param name="stateStoreFactory">State store factory for persistence.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="configuration">Service configuration.</param>
    /// <param name="eventConsumer">Event consumer for registering event handlers.</param>
    /// <param name="inventoryClient">Inventory client for collection containers (L2 hard dependency).</param>
    /// <param name="itemClient">Item client for entry item instances (L2 hard dependency).</param>
    /// <param name="gameServiceClient">Game service client for validation (L2 hard dependency).</param>
    /// <param name="lockProvider">Distributed lock provider (L0 hard dependency).</param>
    /// <param name="resourceClient">Resource client for reference tracking (L1 hard dependency).</param>
    /// <param name="telemetryProvider">Telemetry provider for distributed tracing (L0 hard dependency).</param>
    /// <param name="unlockListeners">DI-discovered listeners for entry unlock notifications (e.g., Seed growth pipeline).</param>
    public CollectionService(
        IMessageBus messageBus,
        IStateStoreFactory stateStoreFactory,
        ILogger<CollectionService> logger,
        CollectionServiceConfiguration configuration,
        IEventConsumer eventConsumer,
        IInventoryClient inventoryClient,
        IItemClient itemClient,
        IGameServiceClient gameServiceClient,
        IDistributedLockProvider lockProvider,
        IResourceClient resourceClient,
        ITelemetryProvider telemetryProvider,
        IEntitySessionRegistry entitySessionRegistry,
        IEnumerable<ICollectionUnlockListener> unlockListeners)
    {
        _messageBus = messageBus;
        _stateStoreFactory = stateStoreFactory;
        _logger = logger;
        _configuration = configuration;
        _inventoryClient = inventoryClient;
        _itemClient = itemClient;
        _gameServiceClient = gameServiceClient;
        _lockProvider = lockProvider;
        _resourceClient = resourceClient;
        _telemetryProvider = telemetryProvider;
        _entitySessionRegistry = entitySessionRegistry;
        _unlockListeners = unlockListeners.ToList();

        RegisterEventConsumers(eventConsumer);

        if (_unlockListeners.Count > 0)
        {
            _logger.LogInformation("Collection service initialized with {Count} unlock listeners: {Listeners}",
                _unlockListeners.Count, string.Join(", ", _unlockListeners.Select(l => l.GetType().Name)));
        }
    }

    #region Model Mapping Helpers

    /// <summary>
    /// Maps an internal entry template model to the API response type.
    /// </summary>
    private static EntryTemplateResponse MapTemplateToResponse(EntryTemplateModel model)
    {
        return new EntryTemplateResponse
        {
            EntryTemplateId = model.EntryTemplateId,
            Code = model.Code,
            CollectionType = model.CollectionType,
            GameServiceId = model.GameServiceId,
            DisplayName = model.DisplayName,
            Category = model.Category,
            Tags = model.Tags,
            AssetId = model.AssetId,
            ThumbnailAssetId = model.ThumbnailAssetId,
            UnlockHint = model.UnlockHint,
            HideWhenLocked = model.HideWhenLocked,
            ItemTemplateId = model.ItemTemplateId,
            DiscoveryLevels = model.DiscoveryLevels?.Select(dl => new DiscoveryLevel
            {
                Level = dl.Level,
                Reveals = dl.Reveals.ToList()
            }).ToList(),
            Themes = model.Themes,
            Duration = model.Duration,
            LoopPoint = model.LoopPoint,
            Composer = model.Composer,
            CreatedAt = model.CreatedAt,
            UpdatedAt = model.UpdatedAt
        };
    }

    /// <summary>
    /// Maps an internal collection instance model to the API response type.
    /// </summary>
    private static CollectionResponse MapCollectionToResponse(CollectionInstanceModel model, int entryCount)
    {
        return new CollectionResponse
        {
            CollectionId = model.CollectionId,
            OwnerId = model.OwnerId,
            OwnerType = model.OwnerType,
            CollectionType = model.CollectionType,
            GameServiceId = model.GameServiceId,
            ContainerId = model.ContainerId,
            EntryCount = entryCount,
            CreatedAt = model.CreatedAt
        };
    }

    /// <summary>
    /// Maps an internal area content config model to the API response type.
    /// </summary>
    private static AreaContentConfigResponse MapAreaContentToResponse(AreaContentConfigModel model)
    {
        return new AreaContentConfigResponse
        {
            AreaConfigId = model.AreaConfigId,
            AreaCode = model.AreaCode,
            GameServiceId = model.GameServiceId,
            CollectionType = model.CollectionType,
            Themes = model.Themes.ToList(),
            DefaultEntryCode = model.DefaultEntryCode,
            CreatedAt = model.CreatedAt,
            UpdatedAt = model.UpdatedAt
        };
    }

    /// <summary>
    /// Maps an unlocked entry record and its template to the API response type.
    /// </summary>
    private static UnlockedEntryResponse MapUnlockedEntryToResponse(UnlockedEntryRecord record, EntryTemplateModel template)
    {
        return new UnlockedEntryResponse
        {
            EntryTemplateId = record.EntryTemplateId,
            Code = record.Code,
            DisplayName = template.DisplayName,
            Category = template.Category,
            Tags = template.Tags,
            ItemInstanceId = record.ItemInstanceId,
            UnlockedAt = record.UnlockedAt,
            Metadata = record.Metadata != null ? new EntryMetadata
            {
                UnlockedIn = record.Metadata.UnlockedIn,
                UnlockedDuring = record.Metadata.UnlockedDuring,
                PlayCount = record.Metadata.PlayCount,
                LastAccessedAt = record.Metadata.LastAccessedAt,
                Favorited = record.Metadata.Favorited,
                DiscoveryLevel = record.Metadata.DiscoveryLevel,
                KillCount = record.Metadata.KillCount,
                CustomData = record.Metadata.CustomData != null
                    ? new Dictionary<string, object>(record.Metadata.CustomData)
                    : null
            } : new EntryMetadata()
        };
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
        var cache = await CollectionCache.GetAsync(BuildCacheKey(collection.CollectionId), cancellationToken);
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
        var templates = await EntryTemplateStore.QueryAsync(
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

        await CollectionCache.SaveAsync(
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

        await CollectionStore.SaveAsync(
            BuildCollectionKey(instance.CollectionId),
            instance,
            cancellationToken: cancellationToken);

        await CollectionStore.SaveAsync(
            BuildCollectionByOwnerKey(ownerId, ownerType, gameServiceId, collectionType),
            instance,
            cancellationToken: cancellationToken);

        await _messageBus.TryPublishAsync(
            "collection.created",
            new CollectionCreatedEvent
            {
                CollectionId = instance.CollectionId,
                OwnerId = instance.OwnerId,
                OwnerType = instance.OwnerType,
                CollectionType = instance.CollectionType,
                GameServiceId = instance.GameServiceId,
                ContainerId = instance.ContainerId,
                CreatedAt = instance.CreatedAt
            },
            cancellationToken: cancellationToken);

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
                await _messageBus.TryPublishAsync(
                    CollectionTopics.MilestoneReached,
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
                    cancellationToken: cancellationToken);

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

    #region Entry Template Management

    /// <inheritdoc/>
    public async Task<(StatusCodes, EntryTemplateResponse?)> CreateEntryTemplateAsync(
        CreateEntryTemplateRequest body,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Creating entry template {Code} for collection type {CollectionType} in game service {GameServiceId}",
            body.Code, body.CollectionType, body.GameServiceId);

        // Validate game service exists
        try
        {
            await _gameServiceClient.GetServiceAsync(
                new GetServiceRequest { ServiceId = body.GameServiceId },
                cancellationToken);
        }
        catch (ApiException ex) when (ex.StatusCode == 404)
        {
            _logger.LogWarning("Game service {GameServiceId} not found", body.GameServiceId);
            return (StatusCodes.NotFound, null);
        }

        // Validate item template exists
        try
        {
            await _itemClient.GetItemTemplateAsync(
                new GetItemTemplateRequest { TemplateId = body.ItemTemplateId },
                cancellationToken);
        }
        catch (ApiException ex) when (ex.StatusCode == 404)
        {
            _logger.LogWarning("Item template {ItemTemplateId} not found", body.ItemTemplateId);
            return (StatusCodes.NotFound, null);
        }

        // Check code uniqueness within collection type + game service
        var existing = await EntryTemplateStore.GetAsync(
            BuildTemplateByCodeKey(body.GameServiceId, body.CollectionType, body.Code),
            cancellationToken);

        if (existing != null)
        {
            _logger.LogWarning(
                "Entry template with code {Code} already exists for type {CollectionType} in game {GameServiceId}",
                body.Code, body.CollectionType, body.GameServiceId);
            return (StatusCodes.Conflict, null);
        }

        var now = DateTimeOffset.UtcNow;
        var template = new EntryTemplateModel
        {
            EntryTemplateId = Guid.NewGuid(),
            Code = body.Code,
            CollectionType = body.CollectionType,
            GameServiceId = body.GameServiceId,
            DisplayName = body.DisplayName,
            Category = body.Category,
            Tags = body.Tags?.ToList(),
            AssetId = body.AssetId,
            ThumbnailAssetId = body.ThumbnailAssetId,
            UnlockHint = body.UnlockHint,
            HideWhenLocked = body.HideWhenLocked,
            ItemTemplateId = body.ItemTemplateId,
            DiscoveryLevels = body.DiscoveryLevels?.Select(dl => new DiscoveryLevelEntry
            {
                Level = dl.Level,
                Reveals = dl.Reveals.ToList()
            }).ToList(),
            Themes = body.Themes?.ToList(),
            Duration = body.Duration,
            LoopPoint = body.LoopPoint,
            Composer = body.Composer,
            CreatedAt = now
        };

        // Save by ID
        await EntryTemplateStore.SaveAsync(
            BuildTemplateKey(template.EntryTemplateId),
            template,
            cancellationToken: cancellationToken);

        // Save by code lookup key
        await EntryTemplateStore.SaveAsync(
            BuildTemplateByCodeKey(template.GameServiceId, template.CollectionType, template.Code),
            template,
            cancellationToken: cancellationToken);

        await _messageBus.TryPublishAsync(
            "collection-entry-template.created",
            new CollectionEntryTemplateCreatedEvent
            {
                EntryTemplateId = template.EntryTemplateId,
                Code = template.Code,
                CollectionType = template.CollectionType,
                GameServiceId = template.GameServiceId,
                DisplayName = template.DisplayName,
                Category = template.Category,
                HideWhenLocked = template.HideWhenLocked,
                ItemTemplateId = template.ItemTemplateId,
                CreatedAt = template.CreatedAt,
                UpdatedAt = template.UpdatedAt
            },
            cancellationToken: cancellationToken);

        _logger.LogInformation("Created entry template {EntryTemplateId}", template.EntryTemplateId);

        return (StatusCodes.OK, MapTemplateToResponse(template));
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, EntryTemplateResponse?)> GetEntryTemplateAsync(
        GetEntryTemplateRequest body,
        CancellationToken cancellationToken)
    {
        var template = await EntryTemplateStore.GetAsync(
            BuildTemplateKey(body.EntryTemplateId),
            cancellationToken);

        if (template == null)
        {
            return (StatusCodes.NotFound, null);
        }

        return (StatusCodes.OK, MapTemplateToResponse(template));
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, ListEntryTemplatesResponse?)> ListEntryTemplatesAsync(
        ListEntryTemplatesRequest body,
        CancellationToken cancellationToken)
    {
        var pageSize = body.PageSize ?? _configuration.DefaultPageSize;

        var results = await EntryTemplateStore.QueryAsync(
            t => t.CollectionType == body.CollectionType && t.GameServiceId == body.GameServiceId,
            cancellationToken: cancellationToken);

        // Apply optional category filter
        IEnumerable<EntryTemplateModel> filtered = results;
        if (!string.IsNullOrEmpty(body.Category))
        {
            filtered = results.Where(t => t.Category == body.Category);
        }

        var allItems = filtered.ToList();

        // Cursor-based pagination using index offset
        var startIndex = 0;
        if (!string.IsNullOrEmpty(body.Cursor) && int.TryParse(body.Cursor, out var cursorIndex))
        {
            startIndex = cursorIndex;
        }

        var paged = allItems.Skip(startIndex).Take(pageSize + 1).ToList();
        var hasMore = paged.Count > pageSize;
        var items = paged.Take(pageSize).ToList();

        return (StatusCodes.OK, new ListEntryTemplatesResponse
        {
            Templates = items.Select(MapTemplateToResponse).ToList(),
            NextCursor = hasMore ? (startIndex + pageSize).ToString() : null,
            HasMore = hasMore
        });
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, EntryTemplateResponse?)> UpdateEntryTemplateAsync(
        UpdateEntryTemplateRequest body,
        CancellationToken cancellationToken)
    {
        await using var lockHandle = await _lockProvider.LockAsync(
            StateStoreDefinitions.CollectionLock,
            $"tpl:{body.EntryTemplateId}",
            Guid.NewGuid().ToString(),
            _configuration.LockTimeoutSeconds,
            cancellationToken);

        if (!lockHandle.Success)
        {
            _logger.LogWarning("Failed to acquire lock for entry template {EntryTemplateId}", body.EntryTemplateId);
            return (StatusCodes.Conflict, null);
        }

        var template = await EntryTemplateStore.GetAsync(
            BuildTemplateKey(body.EntryTemplateId),
            cancellationToken);

        if (template == null)
        {
            return (StatusCodes.NotFound, null);
        }

        var changedFields = new List<string>();
        var now = DateTimeOffset.UtcNow;

        if (body.DisplayName != null)
        {
            template.DisplayName = body.DisplayName;
            changedFields.Add("displayName");
        }
        if (body.Category != null)
        {
            template.Category = body.Category;
            changedFields.Add("category");
        }
        if (body.Tags != null)
        {
            template.Tags = body.Tags.ToList();
            changedFields.Add("tags");
        }
        if (body.AssetId != null)
        {
            template.AssetId = body.AssetId;
            changedFields.Add("assetId");
        }
        if (body.ThumbnailAssetId != null)
        {
            template.ThumbnailAssetId = body.ThumbnailAssetId;
            changedFields.Add("thumbnailAssetId");
        }
        if (body.UnlockHint != null)
        {
            template.UnlockHint = body.UnlockHint;
            changedFields.Add("unlockHint");
        }
        if (body.Themes != null)
        {
            template.Themes = body.Themes.ToList();
            changedFields.Add("themes");
        }
        if (body.Duration != null)
        {
            template.Duration = body.Duration;
            changedFields.Add("duration");
        }
        if (body.LoopPoint != null)
        {
            template.LoopPoint = body.LoopPoint;
            changedFields.Add("loopPoint");
        }
        if (body.Composer != null)
        {
            template.Composer = body.Composer;
            changedFields.Add("composer");
        }
        if (body.HideWhenLocked != null)
        {
            template.HideWhenLocked = body.HideWhenLocked.Value;
            changedFields.Add("hideWhenLocked");
        }
        if (body.DiscoveryLevels != null)
        {
            template.DiscoveryLevels = body.DiscoveryLevels
                .Select(dl => new DiscoveryLevelEntry { Level = dl.Level, Reveals = dl.Reveals.ToList() })
                .ToList();
            changedFields.Add("discoveryLevels");
        }

        if (changedFields.Count == 0)
        {
            return (StatusCodes.OK, MapTemplateToResponse(template));
        }

        template.UpdatedAt = now;

        await EntryTemplateStore.SaveAsync(
            BuildTemplateKey(template.EntryTemplateId),
            template,
            cancellationToken: cancellationToken);

        await EntryTemplateStore.SaveAsync(
            BuildTemplateByCodeKey(template.GameServiceId, template.CollectionType, template.Code),
            template,
            cancellationToken: cancellationToken);

        await _messageBus.TryPublishAsync(
            "collection-entry-template.updated",
            new CollectionEntryTemplateUpdatedEvent
            {
                EntryTemplateId = template.EntryTemplateId,
                Code = template.Code,
                CollectionType = template.CollectionType,
                GameServiceId = template.GameServiceId,
                DisplayName = template.DisplayName,
                Category = template.Category,
                HideWhenLocked = template.HideWhenLocked,
                ItemTemplateId = template.ItemTemplateId,
                CreatedAt = template.CreatedAt,
                UpdatedAt = template.UpdatedAt,
                ChangedFields = changedFields
            },
            cancellationToken: cancellationToken);

        _logger.LogInformation(
            "Updated entry template {EntryTemplateId}, fields: {ChangedFields}",
            template.EntryTemplateId, string.Join(", ", changedFields));

        return (StatusCodes.OK, MapTemplateToResponse(template));
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, EntryTemplateResponse?)> DeleteEntryTemplateAsync(
        DeleteEntryTemplateRequest body,
        CancellationToken cancellationToken)
    {
        await using var lockHandle = await _lockProvider.LockAsync(
            StateStoreDefinitions.CollectionLock,
            $"tpl:{body.EntryTemplateId}",
            Guid.NewGuid().ToString(),
            _configuration.LockTimeoutSeconds,
            cancellationToken);

        if (!lockHandle.Success)
        {
            _logger.LogWarning("Failed to acquire lock for entry template {EntryTemplateId}", body.EntryTemplateId);
            return (StatusCodes.Conflict, null);
        }

        var template = await EntryTemplateStore.GetAsync(
            BuildTemplateKey(body.EntryTemplateId),
            cancellationToken);

        if (template == null)
        {
            return (StatusCodes.NotFound, null);
        }

        // Check if any collection instances reference this template (warn if so)
        var collectionsOfType = await CollectionStore.QueryAsync(
            c => c.CollectionType == template.CollectionType && c.GameServiceId == template.GameServiceId,
            cancellationToken: cancellationToken);

        foreach (var collection in collectionsOfType)
        {
            var cache = await CollectionCache.GetAsync(BuildCacheKey(collection.CollectionId), cancellationToken);
            if (cache?.UnlockedEntries.Any(e => e.Code == template.Code) == true)
            {
                _logger.LogWarning(
                    "Deleting entry template {EntryTemplateId} ({Code}) which is referenced by collection {CollectionId} for {OwnerType} {OwnerId}",
                    template.EntryTemplateId, template.Code, collection.CollectionId, collection.OwnerType, collection.OwnerId);
            }
        }

        await EntryTemplateStore.DeleteAsync(
            BuildTemplateKey(template.EntryTemplateId),
            cancellationToken);

        await EntryTemplateStore.DeleteAsync(
            BuildTemplateByCodeKey(template.GameServiceId, template.CollectionType, template.Code),
            cancellationToken);

        await _messageBus.TryPublishAsync(
            "collection-entry-template.deleted",
            new CollectionEntryTemplateDeletedEvent
            {
                EntryTemplateId = template.EntryTemplateId,
                Code = template.Code,
                CollectionType = template.CollectionType,
                GameServiceId = template.GameServiceId,
                DisplayName = template.DisplayName,
                Category = template.Category,
                HideWhenLocked = template.HideWhenLocked,
                ItemTemplateId = template.ItemTemplateId,
                CreatedAt = template.CreatedAt,
                UpdatedAt = template.UpdatedAt
            },
            cancellationToken: cancellationToken);

        _logger.LogInformation("Deleted entry template {EntryTemplateId}", template.EntryTemplateId);

        return (StatusCodes.OK, MapTemplateToResponse(template));
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, SeedEntryTemplatesResponse?)> SeedEntryTemplatesAsync(
        SeedEntryTemplatesRequest body,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Seeding {Count} entry templates", body.Templates.Count);

        // Validate itemTemplateIds upfront by collecting unique IDs and checking each
        var uniqueItemTemplateIds = body.Templates.Select(t => t.ItemTemplateId).Distinct().ToList();
        var validItemTemplateIds = new HashSet<Guid>();

        foreach (var itemTemplateId in uniqueItemTemplateIds)
        {
            try
            {
                await _itemClient.GetItemTemplateAsync(
                    new GetItemTemplateRequest { TemplateId = itemTemplateId },
                    cancellationToken);
                validItemTemplateIds.Add(itemTemplateId);
            }
            catch (ApiException ex) when (ex.StatusCode == 404)
            {
                _logger.LogWarning("Item template {ItemTemplateId} not found, skipping templates that reference it",
                    itemTemplateId);
            }
        }

        var created = 0;
        var skipped = 0;

        foreach (var templateRequest in body.Templates)
        {
            var existingByCode = await EntryTemplateStore.GetAsync(
                BuildTemplateByCodeKey(templateRequest.GameServiceId, templateRequest.CollectionType, templateRequest.Code),
                cancellationToken);

            if (existingByCode != null)
            {
                skipped++;
                continue;
            }

            if (!validItemTemplateIds.Contains(templateRequest.ItemTemplateId))
            {
                _logger.LogWarning("Skipping entry template {Code}: invalid item template {ItemTemplateId}",
                    templateRequest.Code, templateRequest.ItemTemplateId);
                skipped++;
                continue;
            }

            var now = DateTimeOffset.UtcNow;
            var template = new EntryTemplateModel
            {
                EntryTemplateId = Guid.NewGuid(),
                Code = templateRequest.Code,
                CollectionType = templateRequest.CollectionType,
                GameServiceId = templateRequest.GameServiceId,
                DisplayName = templateRequest.DisplayName,
                Category = templateRequest.Category,
                Tags = templateRequest.Tags?.ToList(),
                AssetId = templateRequest.AssetId,
                ThumbnailAssetId = templateRequest.ThumbnailAssetId,
                UnlockHint = templateRequest.UnlockHint,
                HideWhenLocked = templateRequest.HideWhenLocked,
                ItemTemplateId = templateRequest.ItemTemplateId,
                DiscoveryLevels = templateRequest.DiscoveryLevels?.Select(dl => new DiscoveryLevelEntry
                {
                    Level = dl.Level,
                    Reveals = dl.Reveals.ToList()
                }).ToList(),
                Themes = templateRequest.Themes?.ToList(),
                Duration = templateRequest.Duration,
                LoopPoint = templateRequest.LoopPoint,
                Composer = templateRequest.Composer,
                CreatedAt = now
            };

            await EntryTemplateStore.SaveAsync(
                BuildTemplateKey(template.EntryTemplateId),
                template,
                cancellationToken: cancellationToken);

            await EntryTemplateStore.SaveAsync(
                BuildTemplateByCodeKey(template.GameServiceId, template.CollectionType, template.Code),
                template,
                cancellationToken: cancellationToken);

            await _messageBus.TryPublishAsync(
                "collection-entry-template.created",
                new CollectionEntryTemplateCreatedEvent
                {
                    EntryTemplateId = template.EntryTemplateId,
                    Code = template.Code,
                    CollectionType = template.CollectionType,
                    GameServiceId = template.GameServiceId,
                    DisplayName = template.DisplayName,
                    Category = template.Category,
                    HideWhenLocked = template.HideWhenLocked,
                    ItemTemplateId = template.ItemTemplateId,
                    CreatedAt = template.CreatedAt,
                    UpdatedAt = template.UpdatedAt
                },
                cancellationToken: cancellationToken);

            created++;
        }

        _logger.LogInformation("Seed complete: {Created} created, {Skipped} skipped", created, skipped);

        return (StatusCodes.OK, new SeedEntryTemplatesResponse
        {
            Created = created,
            Skipped = skipped
        });
    }

    #endregion

    #region Collection Instance Management

    /// <inheritdoc/>
    public async Task<(StatusCodes, CollectionResponse?)> CreateCollectionAsync(
        CreateCollectionRequest body,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Creating collection of type {CollectionType} for {OwnerType} {OwnerId} in game {GameServiceId}",
            body.CollectionType, body.OwnerType, body.OwnerId, body.GameServiceId);

        if (MapToContainerOwnerType(body.OwnerType) == null)
        {
            _logger.LogWarning("Owner type {OwnerType} cannot be mapped to inventory ContainerOwnerType", body.OwnerType);
            return (StatusCodes.BadRequest, null);
        }

        // Validate game service exists
        try
        {
            await _gameServiceClient.GetServiceAsync(
                new GetServiceRequest { ServiceId = body.GameServiceId },
                cancellationToken);
        }
        catch (ApiException ex) when (ex.StatusCode == 404)
        {
            _logger.LogWarning("Game service {GameServiceId} not found", body.GameServiceId);
            return (StatusCodes.NotFound, null);
        }

        // Check uniqueness: one collection per type per game per owner
        var existingCollection = await CollectionStore.GetAsync(
            BuildCollectionByOwnerKey(body.OwnerId, body.OwnerType, body.GameServiceId, body.CollectionType),
            cancellationToken);

        if (existingCollection != null)
        {
            _logger.LogWarning(
                "Collection of type {CollectionType} already exists for {OwnerType} {OwnerId} in game {GameServiceId}",
                body.CollectionType, body.OwnerType, body.OwnerId, body.GameServiceId);
            return (StatusCodes.Conflict, null);
        }

        // Check max collections per owner
        var ownerCollections = await CollectionStore.QueryAsync(
            c => c.OwnerId == body.OwnerId && c.OwnerType == body.OwnerType,
            cancellationToken: cancellationToken);

        if (ownerCollections.Count >= _configuration.MaxCollectionsPerOwner)
        {
            _logger.LogWarning(
                "Owner {OwnerType} {OwnerId} has reached max collections limit of {Max}",
                body.OwnerType, body.OwnerId, _configuration.MaxCollectionsPerOwner);
            return (StatusCodes.Conflict, null);
        }

        var instance = await CreateCollectionInternalAsync(
            body.OwnerId, body.OwnerType, body.CollectionType, body.GameServiceId, cancellationToken);

        return (StatusCodes.OK, MapCollectionToResponse(instance, 0));
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, CollectionResponse?)> GetCollectionAsync(
        GetCollectionRequest body,
        CancellationToken cancellationToken)
    {
        var collection = await CollectionStore.GetAsync(
            BuildCollectionKey(body.CollectionId),
            cancellationToken);

        if (collection == null)
        {
            return (StatusCodes.NotFound, null);
        }

        var cache = await LoadOrRebuildCollectionCacheAsync(collection, cancellationToken);
        return (StatusCodes.OK, MapCollectionToResponse(collection, cache.UnlockedEntries.Count));
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, ListCollectionsResponse?)> ListCollectionsAsync(
        ListCollectionsRequest body,
        CancellationToken cancellationToken)
    {
        var collections = await CollectionStore.QueryAsync(
            c => c.OwnerId == body.OwnerId && c.OwnerType == body.OwnerType,
            cancellationToken: cancellationToken);

        // Apply optional game service filter
        IEnumerable<CollectionInstanceModel> filtered = collections;
        if (body.GameServiceId.HasValue)
        {
            filtered = collections.Where(c => c.GameServiceId == body.GameServiceId.Value);
        }

        var results = new List<CollectionResponse>();
        foreach (var collection in filtered)
        {
            // Try to load cache for entry count; use 0 if cache doesn't exist
            // (GetCollection will rebuild on demand)
            var cache = await CollectionCache.GetAsync(BuildCacheKey(collection.CollectionId), cancellationToken);
            var entryCount = cache?.UnlockedEntries.Count ?? 0;
            results.Add(MapCollectionToResponse(collection, entryCount));
        }

        return (StatusCodes.OK, new ListCollectionsResponse
        {
            Collections = results
        });
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, CollectionResponse?)> DeleteCollectionAsync(
        DeleteCollectionRequest body,
        CancellationToken cancellationToken)
    {
        await using var lockHandle = await _lockProvider.LockAsync(
            StateStoreDefinitions.CollectionLock,
            $"col:{body.CollectionId}",
            Guid.NewGuid().ToString(),
            _configuration.LockTimeoutSeconds,
            cancellationToken);

        if (!lockHandle.Success)
        {
            _logger.LogWarning("Failed to acquire lock for collection {CollectionId}", body.CollectionId);
            return (StatusCodes.Conflict, null);
        }

        var collection = await CollectionStore.GetAsync(
            BuildCollectionKey(body.CollectionId),
            cancellationToken);

        if (collection == null)
        {
            return (StatusCodes.NotFound, null);
        }

        // Delete inventory container (which also deletes contained items)
        try
        {
            await _inventoryClient.DeleteContainerAsync(
                new DeleteContainerRequest { ContainerId = collection.ContainerId },
                cancellationToken);
        }
        catch (ApiException ex) when (ex.StatusCode == 404)
        {
            _logger.LogWarning("Container {ContainerId} already deleted", collection.ContainerId);
        }

        // Delete cache
        await CollectionCache.DeleteAsync(BuildCacheKey(collection.CollectionId), cancellationToken);

        // Delete collection instance records
        await CollectionStore.DeleteAsync(BuildCollectionKey(collection.CollectionId), cancellationToken);
        await CollectionStore.DeleteAsync(
            BuildCollectionByOwnerKey(collection.OwnerId, collection.OwnerType, collection.GameServiceId, collection.CollectionType),
            cancellationToken);

        await _messageBus.TryPublishAsync(
            "collection.deleted",
            new CollectionDeletedEvent
            {
                CollectionId = collection.CollectionId,
                OwnerId = collection.OwnerId,
                OwnerType = collection.OwnerType,
                CollectionType = collection.CollectionType,
                GameServiceId = collection.GameServiceId,
                ContainerId = collection.ContainerId,
                CreatedAt = collection.CreatedAt
            },
            cancellationToken: cancellationToken);

        // Unregister reference with lib-resource for character-owned collections per FOUNDATION TENETS
        if (collection.OwnerType == EntityType.Character)
        {
            await UnregisterCharacterReferenceAsync(
                collection.CollectionId.ToString(), collection.OwnerId, cancellationToken);
        }

        _logger.LogInformation("Deleted collection {CollectionId}", collection.CollectionId);

        return (StatusCodes.OK, MapCollectionToResponse(collection, 0));
    }

    #endregion

    #region Entry Operations

    /// <inheritdoc/>
    public async Task<(StatusCodes, GrantEntryResponse?)> GrantEntryAsync(
        GrantEntryRequest body,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Granting entry {EntryCode} of type {CollectionType} to {OwnerType} {OwnerId}",
            body.EntryCode, body.CollectionType, body.OwnerType, body.OwnerId);

        if (MapToContainerOwnerType(body.OwnerType) == null)
        {
            _logger.LogWarning("Owner type {OwnerType} cannot be mapped to inventory ContainerOwnerType", body.OwnerType);
            return (StatusCodes.BadRequest, null);
        }

        // Look up entry template by code
        var template = await EntryTemplateStore.GetAsync(
            BuildTemplateByCodeKey(body.GameServiceId, body.CollectionType, body.EntryCode),
            cancellationToken);

        if (template == null)
        {
            _logger.LogWarning(
                "Entry template with code {EntryCode} not found for type {CollectionType} in game {GameServiceId}",
                body.EntryCode, body.CollectionType, body.GameServiceId);

            await _messageBus.TryPublishAsync(
                CollectionTopics.EntryGrantFailed,
                new CollectionEntryGrantFailedEvent
                {
                    EventId = Guid.NewGuid(),
                    Timestamp = DateTimeOffset.UtcNow,
                    OwnerId = body.OwnerId,
                    OwnerType = body.OwnerType,
                    EntryCode = body.EntryCode,
                    Reason = GrantFailureReason.EntryNotFound
                },
                cancellationToken: cancellationToken);

            return (StatusCodes.NotFound, null);
        }

        // Find or auto-create collection
        var collection = await CollectionStore.GetAsync(
            BuildCollectionByOwnerKey(body.OwnerId, body.OwnerType, body.GameServiceId, body.CollectionType),
            cancellationToken);

        if (collection == null)
        {
            // Check max collections per owner before auto-creating
            var ownerCollections = await CollectionStore.QueryAsync(
                c => c.OwnerId == body.OwnerId && c.OwnerType == body.OwnerType,
                cancellationToken: cancellationToken);

            if (ownerCollections.Count >= _configuration.MaxCollectionsPerOwner)
            {
                _logger.LogWarning(
                    "Owner {OwnerType} {OwnerId} has reached max collections limit of {Max} during grant auto-create",
                    body.OwnerType, body.OwnerId, _configuration.MaxCollectionsPerOwner);
                return (StatusCodes.Conflict, null);
            }

            // Auto-create collection (and inventory container)
            _logger.LogInformation(
                "Auto-creating collection of type {CollectionType} for {OwnerType} {OwnerId}",
                body.CollectionType, body.OwnerType, body.OwnerId);

            collection = await CreateCollectionInternalAsync(
                body.OwnerId, body.OwnerType, body.CollectionType, body.GameServiceId, cancellationToken);
        }

        // Lock the collection for the grant operation
        await using var lockHandle = await _lockProvider.LockAsync(
            StateStoreDefinitions.CollectionLock,
            $"col:{collection.CollectionId}",
            Guid.NewGuid().ToString(),
            _configuration.LockTimeoutSeconds,
            cancellationToken);

        if (!lockHandle.Success)
        {
            _logger.LogWarning("Failed to acquire lock for collection {CollectionId}", collection.CollectionId);
            return (StatusCodes.Conflict, null);
        }

        // Load cache inside lock
        var cache = await LoadOrRebuildCollectionCacheAsync(collection, cancellationToken);

        // Idempotency check: already unlocked → return existing
        var existingEntry = cache.UnlockedEntries.FirstOrDefault(e => e.Code == body.EntryCode);
        if (existingEntry != null)
        {
            _logger.LogInformation(
                "Entry {EntryCode} already unlocked in collection {CollectionId}",
                body.EntryCode, collection.CollectionId);

            return (StatusCodes.OK, new GrantEntryResponse
            {
                EntryTemplateId = existingEntry.EntryTemplateId,
                Code = existingEntry.Code,
                CollectionId = collection.CollectionId,
                ItemInstanceId = existingEntry.ItemInstanceId,
                AlreadyUnlocked = true,
                UnlockedAt = existingEntry.UnlockedAt
            });
        }

        // Check max entries limit
        if (cache.UnlockedEntries.Count >= _configuration.MaxEntriesPerCollection)
        {
            _logger.LogWarning(
                "Collection {CollectionId} has reached max entries limit of {Max}",
                collection.CollectionId, _configuration.MaxEntriesPerCollection);

            await _messageBus.TryPublishAsync(
                CollectionTopics.EntryGrantFailed,
                new CollectionEntryGrantFailedEvent
                {
                    EventId = Guid.NewGuid(),
                    Timestamp = DateTimeOffset.UtcNow,
                    CollectionId = collection.CollectionId,
                    OwnerId = body.OwnerId,
                    OwnerType = body.OwnerType,
                    EntryCode = body.EntryCode,
                    Reason = GrantFailureReason.MaxEntriesReached
                },
                cancellationToken: cancellationToken);

            return (StatusCodes.Conflict, null);
        }

        // Create item instance in the collection's inventory container
        // Collection items are metadata tokens tracking unlocks, not physical game items.
        // RealmId is required by the Item service for partitioning; using GameServiceId
        // as the partition key since collections are scoped to game services.
        ItemInstanceResponse itemResponse;
        try
        {
            itemResponse = await _itemClient.CreateItemInstanceAsync(
                new CreateItemInstanceRequest
                {
                    TemplateId = template.ItemTemplateId,
                    ContainerId = collection.ContainerId,
                    RealmId = body.GameServiceId,
                    Quantity = 1
                },
                cancellationToken);
        }
        catch (ApiException ex)
        {
            _logger.LogError(ex,
                "Failed to create item instance for entry {EntryCode} in collection {CollectionId}",
                body.EntryCode, collection.CollectionId);

            await _messageBus.TryPublishAsync(
                CollectionTopics.EntryGrantFailed,
                new CollectionEntryGrantFailedEvent
                {
                    EventId = Guid.NewGuid(),
                    Timestamp = DateTimeOffset.UtcNow,
                    CollectionId = collection.CollectionId,
                    OwnerId = body.OwnerId,
                    OwnerType = body.OwnerType,
                    EntryCode = body.EntryCode,
                    Reason = GrantFailureReason.ItemCreationFailed
                },
                cancellationToken: cancellationToken);

            return (StatusCodes.InternalServerError, null);
        }

        var now = DateTimeOffset.UtcNow;
        var newEntry = new UnlockedEntryRecord
        {
            Code = template.Code,
            EntryTemplateId = template.EntryTemplateId,
            ItemInstanceId = itemResponse.InstanceId,
            UnlockedAt = now,
            Metadata = body.Metadata != null ? new EntryMetadataModel
            {
                UnlockedIn = body.Metadata.UnlockedIn,
                UnlockedDuring = body.Metadata.UnlockedDuring,
                PlayCount = body.Metadata.PlayCount,
                LastAccessedAt = body.Metadata.LastAccessedAt,
                Favorited = body.Metadata.Favorited,
                DiscoveryLevel = body.Metadata.DiscoveryLevel,
                KillCount = body.Metadata.KillCount,
                CustomData = body.Metadata.CustomData != null
                    ? new Dictionary<string, object> { { "data", body.Metadata.CustomData } }
                    : null
            } : new EntryMetadataModel()
        };

        // Update cache with ETag-based optimistic concurrency
        for (var retry = 0; retry < _configuration.MaxConcurrencyRetries; retry++)
        {
            var (cachedValue, etag) = await CollectionCache.GetWithETagAsync(
                BuildCacheKey(collection.CollectionId), cancellationToken);

            var cacheToUpdate = cachedValue ?? cache;
            cacheToUpdate.UnlockedEntries.Add(newEntry);
            cacheToUpdate.LastUpdated = now;

            if (etag != null)
            {
                var newEtag = await CollectionCache.TrySaveAsync(
                    BuildCacheKey(collection.CollectionId),
                    cacheToUpdate,
                    etag,
                    cancellationToken);

                if (newEtag != null) break;

                _logger.LogDebug("Cache ETag conflict on retry {Retry} for collection {CollectionId}",
                    retry, collection.CollectionId);
            }
            else
            {
                await CollectionCache.SaveAsync(
                    BuildCacheKey(collection.CollectionId),
                    cacheToUpdate,
                    new StateOptions { Ttl = _configuration.CollectionCacheTtlSeconds },
                    cancellationToken);
                break;
            }
        }

        // Track global first-unlock via atomic Redis SADD (returns true if newly added)
        var isFirstGlobal = await CacheableCollectionCache.AddToSetAsync(
            BuildGlobalUnlocksSetKey(collection.GameServiceId, collection.CollectionType),
            template.Code,
            cancellationToken: cancellationToken);

        // Publish entry-unlocked event (for external/distributed consumers)
        await _messageBus.TryPublishAsync(
            CollectionTopics.EntryUnlocked,
            new CollectionEntryUnlockedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = now,
                CollectionId = collection.CollectionId,
                OwnerId = collection.OwnerId,
                OwnerType = collection.OwnerType,
                GameServiceId = collection.GameServiceId,
                CollectionType = collection.CollectionType,
                EntryCode = template.Code,
                DisplayName = template.DisplayName,
                Category = template.Category,
                Tags = template.Tags,
                IsFirstGlobal = isFirstGlobal
            },
            cancellationToken: cancellationToken);

        // Push client event to collection owner's WebSocket sessions
        await _entitySessionRegistry.PublishToEntitySessionsAsync(
            collection.OwnerType.ToString().ToLowerInvariant(), collection.OwnerId,
            new CollectionEntryUnlockedClientEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = now,
                CollectionId = collection.CollectionId,
                EntryCode = template.Code,
                DisplayName = template.DisplayName,
                Category = template.Category,
                CollectionType = collection.CollectionType,
                IsFirstGlobal = isFirstGlobal
            },
            cancellationToken);

        // Dispatch to in-process unlock listeners (e.g., Seed growth pipeline)
        await DispatchUnlockListenersAsync(collection, template, newEntry, cancellationToken);

        // Check and publish milestones
        var totalTemplates = await EntryTemplateStore.QueryAsync(
            t => t.CollectionType == collection.CollectionType && t.GameServiceId == collection.GameServiceId,
            cancellationToken: cancellationToken);

        await CheckAndPublishMilestonesAsync(
            collection,
            cache.UnlockedEntries.Count,
            totalTemplates.Count,
            cancellationToken);

        _logger.LogInformation(
            "Granted entry {EntryCode} to collection {CollectionId}, item instance {ItemInstanceId}",
            body.EntryCode, collection.CollectionId, itemResponse.InstanceId);

        return (StatusCodes.OK, new GrantEntryResponse
        {
            EntryTemplateId = template.EntryTemplateId,
            Code = template.Code,
            CollectionId = collection.CollectionId,
            ItemInstanceId = itemResponse.InstanceId,
            AlreadyUnlocked = false,
            UnlockedAt = now
        });
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, HasEntryResponse?)> HasEntryAsync(
        HasEntryRequest body,
        CancellationToken cancellationToken)
    {
        var collection = await CollectionStore.GetAsync(
            BuildCollectionByOwnerKey(body.OwnerId, body.OwnerType, body.GameServiceId, body.CollectionType),
            cancellationToken);

        if (collection == null)
        {
            return (StatusCodes.OK, new HasEntryResponse { HasEntry = false });
        }

        var cache = await LoadOrRebuildCollectionCacheAsync(collection, cancellationToken);
        var entry = cache.UnlockedEntries.FirstOrDefault(e => e.Code == body.EntryCode);

        return (StatusCodes.OK, new HasEntryResponse
        {
            HasEntry = entry != null,
            UnlockedAt = entry?.UnlockedAt
        });
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, QueryEntriesResponse?)> QueryEntriesAsync(
        QueryEntriesRequest body,
        CancellationToken cancellationToken)
    {
        var collection = await CollectionStore.GetAsync(
            BuildCollectionKey(body.CollectionId),
            cancellationToken);

        if (collection == null)
        {
            return (StatusCodes.NotFound, null);
        }

        var cache = await LoadOrRebuildCollectionCacheAsync(collection, cancellationToken);

        // Load all templates for this collection to enrich responses
        var templates = await EntryTemplateStore.QueryAsync(
            t => t.CollectionType == collection.CollectionType && t.GameServiceId == collection.GameServiceId,
            cancellationToken: cancellationToken);

        var templatesByCode = templates.ToDictionary(t => t.Code);

        // Filter entries
        IEnumerable<UnlockedEntryRecord> filtered = cache.UnlockedEntries;

        if (!string.IsNullOrEmpty(body.Category))
        {
            filtered = filtered.Where(e =>
                templatesByCode.TryGetValue(e.Code, out var tpl) && tpl.Category == body.Category);
        }

        if (body.Tags != null && body.Tags.Count > 0)
        {
            var requestedTags = body.Tags.ToHashSet();
            filtered = filtered.Where(e =>
                templatesByCode.TryGetValue(e.Code, out var tpl) &&
                tpl.Tags != null &&
                tpl.Tags.Any(tag => requestedTags.Contains(tag)));
        }

        var allEntries = filtered.ToList();

        // Cursor-based pagination
        var pageSize = body.PageSize ?? _configuration.DefaultPageSize;
        var startIndex = 0;
        if (!string.IsNullOrEmpty(body.Cursor) && int.TryParse(body.Cursor, out var cursorIndex))
        {
            startIndex = cursorIndex;
        }

        var paged = allEntries.Skip(startIndex).Take(pageSize + 1).ToList();
        var hasMore = paged.Count > pageSize;
        var items = paged.Take(pageSize).ToList();

        var responseEntries = new List<UnlockedEntryResponse>();
        foreach (var entry in items)
        {
            if (templatesByCode.TryGetValue(entry.Code, out var tpl))
            {
                responseEntries.Add(MapUnlockedEntryToResponse(entry, tpl));
            }
        }

        return (StatusCodes.OK, new QueryEntriesResponse
        {
            Entries = responseEntries,
            NextCursor = hasMore ? (startIndex + pageSize).ToString() : null,
            HasMore = hasMore
        });
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, UnlockedEntryResponse?)> UpdateEntryMetadataAsync(
        UpdateEntryMetadataRequest body,
        CancellationToken cancellationToken)
    {
        await using var lockHandle = await _lockProvider.LockAsync(
            StateStoreDefinitions.CollectionLock,
            $"col:{body.CollectionId}",
            Guid.NewGuid().ToString(),
            _configuration.LockTimeoutSeconds,
            cancellationToken);

        if (!lockHandle.Success)
        {
            _logger.LogWarning("Failed to acquire lock for collection {CollectionId}", body.CollectionId);
            return (StatusCodes.Conflict, null);
        }

        var collection = await CollectionStore.GetAsync(
            BuildCollectionKey(body.CollectionId),
            cancellationToken);

        if (collection == null)
        {
            return (StatusCodes.NotFound, null);
        }

        var cache = await LoadOrRebuildCollectionCacheAsync(collection, cancellationToken);
        var entry = cache.UnlockedEntries.FirstOrDefault(e => e.Code == body.EntryCode);

        if (entry == null)
        {
            _logger.LogWarning(
                "Entry {EntryCode} not found in collection {CollectionId}",
                body.EntryCode, body.CollectionId);
            return (StatusCodes.NotFound, null);
        }

        // Ensure metadata exists
        entry.Metadata ??= new EntryMetadataModel();

        if (body.PlayCount.HasValue)
        {
            entry.Metadata.PlayCount = body.PlayCount.Value;
            entry.Metadata.LastAccessedAt = DateTimeOffset.UtcNow;
        }
        if (body.KillCount.HasValue)
        {
            entry.Metadata.KillCount = body.KillCount.Value;
        }
        if (body.Favorited.HasValue)
        {
            entry.Metadata.Favorited = body.Favorited.Value;
        }
        if (body.DiscoveryLevel.HasValue)
        {
            entry.Metadata.DiscoveryLevel = body.DiscoveryLevel.Value;
        }

        cache.LastUpdated = DateTimeOffset.UtcNow;

        // Save cache with ETag-based optimistic concurrency
        for (var retry = 0; retry < _configuration.MaxConcurrencyRetries; retry++)
        {
            var (_, etag) = await CollectionCache.GetWithETagAsync(
                BuildCacheKey(collection.CollectionId), cancellationToken);

            if (etag != null)
            {
                var newEtag = await CollectionCache.TrySaveAsync(
                    BuildCacheKey(collection.CollectionId),
                    cache,
                    etag,
                    cancellationToken);

                if (newEtag != null) break;

                _logger.LogDebug("Cache ETag conflict on retry {Retry} for collection {CollectionId}",
                    retry, collection.CollectionId);

                // Reload cache on conflict
                cache = await LoadOrRebuildCollectionCacheAsync(collection, cancellationToken);
                entry = cache.UnlockedEntries.FirstOrDefault(e => e.Code == body.EntryCode);
                if (entry == null) return (StatusCodes.NotFound, null);
            }
            else
            {
                await CollectionCache.SaveAsync(
                    BuildCacheKey(collection.CollectionId),
                    cache,
                    new StateOptions { Ttl = _configuration.CollectionCacheTtlSeconds },
                    cancellationToken);
                break;
            }
        }

        // Load template for response enrichment
        var template = await EntryTemplateStore.GetAsync(
            BuildTemplateByCodeKey(collection.GameServiceId, collection.CollectionType, body.EntryCode),
            cancellationToken);

        if (template == null)
        {
            // Template was deleted but entry still exists in cache; return minimal response
            return (StatusCodes.OK, new UnlockedEntryResponse
            {
                EntryTemplateId = entry.EntryTemplateId,
                Code = entry.Code,
                DisplayName = entry.Code,
                ItemInstanceId = entry.ItemInstanceId,
                UnlockedAt = entry.UnlockedAt
            });
        }

        return (StatusCodes.OK, MapUnlockedEntryToResponse(entry, template));
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, CompletionStatsResponse?)> GetCompletionStatsAsync(
        GetCompletionStatsRequest body,
        CancellationToken cancellationToken)
    {
        // Count total templates for this collection type + game
        var allTemplates = await EntryTemplateStore.QueryAsync(
            t => t.CollectionType == body.CollectionType && t.GameServiceId == body.GameServiceId,
            cancellationToken: cancellationToken);

        var totalEntries = allTemplates.Count;

        // Find the collection
        var collection = await CollectionStore.GetAsync(
            BuildCollectionByOwnerKey(body.OwnerId, body.OwnerType, body.GameServiceId, body.CollectionType),
            cancellationToken);

        var unlockedCount = 0;
        var unlockedCodes = new HashSet<string>();

        if (collection != null)
        {
            var cache = await LoadOrRebuildCollectionCacheAsync(collection, cancellationToken);
            unlockedCount = cache.UnlockedEntries.Count;
            foreach (var entry in cache.UnlockedEntries)
            {
                unlockedCodes.Add(entry.Code);
            }
        }

        var completionPercentage = totalEntries > 0
            ? (double)unlockedCount / totalEntries * 100.0
            : 0.0;

        // Break down by category
        var byCategory = new Dictionary<string, CategoryStats>();
        var categoryGroups = allTemplates
            .Where(t => t.Category != null)
            .GroupBy(t => t.Category);

        foreach (var group in categoryGroups)
        {
            var categoryName = group.Key;
            if (categoryName == null) continue;

            var categoryTotal = group.Count();
            var categoryUnlocked = group.Count(t => unlockedCodes.Contains(t.Code));
            var categoryPercentage = categoryTotal > 0
                ? (double)categoryUnlocked / categoryTotal * 100.0
                : 0.0;

            byCategory[categoryName] = new CategoryStats
            {
                Total = categoryTotal,
                Unlocked = categoryUnlocked,
                Percentage = categoryPercentage
            };
        }

        return (StatusCodes.OK, new CompletionStatsResponse
        {
            CollectionType = body.CollectionType,
            TotalEntries = totalEntries,
            UnlockedEntries = unlockedCount,
            CompletionPercentage = completionPercentage,
            ByCategory = byCategory.Count > 0 ? byCategory : null
        });
    }

    #endregion

    #region Content Selection Operations

    /// <inheritdoc/>
    public async Task<(StatusCodes, ContentSelectionResponse?)> SelectContentForAreaAsync(
        SelectContentForAreaRequest body,
        CancellationToken cancellationToken)
    {
        // Load area config
        var areaConfig = await AreaContentStore.GetAsync(
            BuildAreaContentByCodeKey(body.GameServiceId, body.CollectionType, body.AreaCode),
            cancellationToken);

        if (areaConfig == null)
        {
            _logger.LogWarning(
                "No area content config for area {AreaCode} type {CollectionType} in game {GameServiceId}",
                body.AreaCode, body.CollectionType, body.GameServiceId);
            return (StatusCodes.NotFound, null);
        }

        // Find the owner's collection of this type
        var collection = await CollectionStore.GetAsync(
            BuildCollectionByOwnerKey(body.OwnerId, body.OwnerType, body.GameServiceId, body.CollectionType),
            cancellationToken);

        if (collection == null)
        {
            // No collection; return default entry
            return await BuildDefaultContentResponseAsync(
                areaConfig, body.GameServiceId, body.CollectionType, cancellationToken);
        }

        var cache = await LoadOrRebuildCollectionCacheAsync(collection, cancellationToken);

        if (cache.UnlockedEntries.Count == 0)
        {
            return await BuildDefaultContentResponseAsync(
                areaConfig, body.GameServiceId, body.CollectionType, cancellationToken);
        }

        // Load templates for matching
        var templates = await EntryTemplateStore.QueryAsync(
            t => t.CollectionType == body.CollectionType && t.GameServiceId == body.GameServiceId,
            cancellationToken: cancellationToken);

        var templatesByCode = templates.ToDictionary(t => t.Code);
        var areaThemes = areaConfig.Themes.ToHashSet();

        // Build weighted candidate list based on theme overlap
        var candidates = new List<(EntryTemplateModel Template, List<string> MatchedThemes, int Weight)>();

        foreach (var entry in cache.UnlockedEntries)
        {
            if (!templatesByCode.TryGetValue(entry.Code, out var tpl)) continue;
            if (tpl.Themes == null || tpl.Themes.Count == 0) continue;

            var matchedThemes = tpl.Themes.Where(t => areaThemes.Contains(t)).ToList();
            if (matchedThemes.Count > 0)
            {
                candidates.Add((tpl, matchedThemes, matchedThemes.Count));
            }
        }

        if (candidates.Count == 0)
        {
            return await BuildDefaultContentResponseAsync(
                areaConfig, body.GameServiceId, body.CollectionType, cancellationToken);
        }

        // Weighted random selection
        var totalWeight = candidates.Sum(c => c.Weight);
        var randomValue = Random.Shared.Next(totalWeight);
        var cumulative = 0;
        (EntryTemplateModel Template, List<string> MatchedThemes, int Weight) selected = candidates[0];

        foreach (var candidate in candidates)
        {
            cumulative += candidate.Weight;
            if (randomValue < cumulative)
            {
                selected = candidate;
                break;
            }
        }

        return (StatusCodes.OK, new ContentSelectionResponse
        {
            EntryCode = selected.Template.Code,
            DisplayName = selected.Template.DisplayName,
            Category = selected.Template.Category,
            AssetId = selected.Template.AssetId,
            ThumbnailAssetId = selected.Template.ThumbnailAssetId,
            Themes = selected.Template.Themes?.ToList(),
            MatchedThemes = selected.MatchedThemes
        });
    }

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
        var defaultTemplate = await EntryTemplateStore.GetAsync(
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

    /// <inheritdoc/>
    public async Task<(StatusCodes, AreaContentConfigResponse?)> SetAreaContentConfigAsync(
        SetAreaContentConfigRequest body,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Setting area content config for area {AreaCode} type {CollectionType} in game {GameServiceId}",
            body.AreaCode, body.CollectionType, body.GameServiceId);

        // Validate game service exists
        try
        {
            await _gameServiceClient.GetServiceAsync(
                new GetServiceRequest { ServiceId = body.GameServiceId },
                cancellationToken);
        }
        catch (ApiException ex) when (ex.StatusCode == 404)
        {
            _logger.LogWarning("Game service {GameServiceId} not found", body.GameServiceId);
            return (StatusCodes.NotFound, null);
        }

        // Validate default entry template exists
        var defaultEntry = await EntryTemplateStore.GetAsync(
            BuildTemplateByCodeKey(body.GameServiceId, body.CollectionType, body.DefaultEntryCode),
            cancellationToken);

        if (defaultEntry == null)
        {
            _logger.LogWarning(
                "Default entry template {DefaultEntryCode} not found for collection type {CollectionType} in game {GameServiceId}",
                body.DefaultEntryCode, body.CollectionType, body.GameServiceId);
            return (StatusCodes.NotFound, null);
        }

        var now = DateTimeOffset.UtcNow;

        // Check for existing config (upsert)
        var existing = await AreaContentStore.GetAsync(
            BuildAreaContentByCodeKey(body.GameServiceId, body.CollectionType, body.AreaCode),
            cancellationToken);

        AreaContentConfigModel config;
        var isUpdate = existing != null;
        var changedFields = new List<string>();

        if (existing != null)
        {
            if (!existing.Themes.SequenceEqual(body.Themes))
            {
                existing.Themes = body.Themes.ToList();
                changedFields.Add("themes");
            }
            if (existing.DefaultEntryCode != body.DefaultEntryCode)
            {
                existing.DefaultEntryCode = body.DefaultEntryCode;
                changedFields.Add("defaultEntryCode");
            }
            existing.UpdatedAt = now;
            config = existing;
        }
        else
        {
            config = new AreaContentConfigModel
            {
                AreaConfigId = Guid.NewGuid(),
                AreaCode = body.AreaCode,
                GameServiceId = body.GameServiceId,
                CollectionType = body.CollectionType,
                Themes = body.Themes.ToList(),
                DefaultEntryCode = body.DefaultEntryCode,
                CreatedAt = now
            };
        }

        await AreaContentStore.SaveAsync(
            BuildAreaContentKey(config.AreaConfigId),
            config,
            cancellationToken: cancellationToken);

        await AreaContentStore.SaveAsync(
            BuildAreaContentByCodeKey(config.GameServiceId, config.CollectionType, config.AreaCode),
            config,
            cancellationToken: cancellationToken);

        // Publish lifecycle event per FOUNDATION TENETS
        if (isUpdate)
        {
            await _messageBus.TryPublishAsync(
                "collection-area-content-config.updated",
                new CollectionAreaContentConfigUpdatedEvent
                {
                    AreaConfigId = config.AreaConfigId,
                    AreaCode = config.AreaCode,
                    CollectionType = config.CollectionType,
                    GameServiceId = config.GameServiceId,
                    DefaultEntryCode = config.DefaultEntryCode,
                    CreatedAt = config.CreatedAt,
                    UpdatedAt = config.UpdatedAt,
                    ChangedFields = changedFields
                },
                cancellationToken: cancellationToken);
        }
        else
        {
            await _messageBus.TryPublishAsync(
                "collection-area-content-config.created",
                new CollectionAreaContentConfigCreatedEvent
                {
                    AreaConfigId = config.AreaConfigId,
                    AreaCode = config.AreaCode,
                    CollectionType = config.CollectionType,
                    GameServiceId = config.GameServiceId,
                    DefaultEntryCode = config.DefaultEntryCode,
                    CreatedAt = config.CreatedAt,
                    UpdatedAt = config.UpdatedAt
                },
                cancellationToken: cancellationToken);
        }

        _logger.LogInformation(
            "Set area content config {AreaConfigId} for area {AreaCode} type {CollectionType}",
            config.AreaConfigId, config.AreaCode, config.CollectionType);

        return (StatusCodes.OK, MapAreaContentToResponse(config));
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, AreaContentConfigResponse?)> GetAreaContentConfigAsync(
        GetAreaContentConfigRequest body,
        CancellationToken cancellationToken)
    {
        var config = await AreaContentStore.GetAsync(
            BuildAreaContentByCodeKey(body.GameServiceId, body.CollectionType, body.AreaCode),
            cancellationToken);

        if (config == null)
        {
            return (StatusCodes.NotFound, null);
        }

        return (StatusCodes.OK, MapAreaContentToResponse(config));
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, ListAreaContentConfigsResponse?)> ListAreaContentConfigsAsync(
        ListAreaContentConfigsRequest body,
        CancellationToken cancellationToken)
    {
        var configs = await AreaContentStore.QueryAsync(
            c => c.GameServiceId == body.GameServiceId && c.CollectionType == body.CollectionType,
            cancellationToken: cancellationToken);

        return (StatusCodes.OK, new ListAreaContentConfigsResponse
        {
            Configs = configs.Select(MapAreaContentToResponse).ToList()
        });
    }

    #endregion

    #region Discovery

    /// <inheritdoc/>
    public async Task<(StatusCodes, AdvanceDiscoveryResponse?)> AdvanceDiscoveryAsync(
        AdvanceDiscoveryRequest body,
        CancellationToken cancellationToken)
    {
        await using var lockHandle = await _lockProvider.LockAsync(
            StateStoreDefinitions.CollectionLock,
            $"col:{body.CollectionId}",
            Guid.NewGuid().ToString(),
            _configuration.LockTimeoutSeconds,
            cancellationToken);

        if (!lockHandle.Success)
        {
            _logger.LogWarning("Failed to acquire lock for collection {CollectionId}", body.CollectionId);
            return (StatusCodes.Conflict, null);
        }

        var collection = await CollectionStore.GetAsync(
            BuildCollectionKey(body.CollectionId),
            cancellationToken);

        if (collection == null)
        {
            return (StatusCodes.NotFound, null);
        }

        var cache = await LoadOrRebuildCollectionCacheAsync(collection, cancellationToken);

        // Find the unlocked entry
        var entry = cache.UnlockedEntries.FirstOrDefault(e => e.Code == body.EntryCode);
        if (entry == null)
        {
            _logger.LogWarning(
                "Entry {EntryCode} not unlocked in collection {CollectionId}",
                body.EntryCode, body.CollectionId);
            return (StatusCodes.NotFound, null);
        }

        // Load template to check discovery levels
        var template = await EntryTemplateStore.GetAsync(
            BuildTemplateByCodeKey(collection.GameServiceId, collection.CollectionType, body.EntryCode),
            cancellationToken);

        if (template == null)
        {
            _logger.LogWarning("Entry template for code {EntryCode} not found", body.EntryCode);
            return (StatusCodes.NotFound, null);
        }

        if (template.DiscoveryLevels == null || template.DiscoveryLevels.Count == 0)
        {
            _logger.LogWarning("Entry template {EntryCode} has no discovery levels defined", body.EntryCode);
            return (StatusCodes.BadRequest, null);
        }

        entry.Metadata ??= new EntryMetadataModel();
        var currentLevel = entry.Metadata.DiscoveryLevel;
        var nextLevel = currentLevel + 1;

        var nextLevelDef = template.DiscoveryLevels.FirstOrDefault(dl => dl.Level == nextLevel);
        if (nextLevelDef == null)
        {
            _logger.LogWarning(
                "Entry {EntryCode} is already at max discovery level {CurrentLevel}",
                body.EntryCode, currentLevel);
            return (StatusCodes.Conflict, null);
        }

        entry.Metadata.DiscoveryLevel = nextLevel;
        cache.LastUpdated = DateTimeOffset.UtcNow;

        // Save cache with ETag-based optimistic concurrency
        for (var retry = 0; retry < _configuration.MaxConcurrencyRetries; retry++)
        {
            var (_, etag) = await CollectionCache.GetWithETagAsync(
                BuildCacheKey(collection.CollectionId), cancellationToken);

            if (etag != null)
            {
                var newEtag = await CollectionCache.TrySaveAsync(
                    BuildCacheKey(collection.CollectionId),
                    cache,
                    etag,
                    cancellationToken);

                if (newEtag != null) break;

                _logger.LogDebug("Cache ETag conflict on retry {Retry} for collection {CollectionId}",
                    retry, collection.CollectionId);
            }
            else
            {
                await CollectionCache.SaveAsync(
                    BuildCacheKey(collection.CollectionId),
                    cache,
                    new StateOptions { Ttl = _configuration.CollectionCacheTtlSeconds },
                    cancellationToken);
                break;
            }
        }

        // Publish discovery advanced event
        await _messageBus.TryPublishAsync(
            CollectionTopics.DiscoveryAdvanced,
            new CollectionDiscoveryAdvancedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                CollectionId = collection.CollectionId,
                OwnerId = collection.OwnerId,
                OwnerType = collection.OwnerType,
                EntryCode = body.EntryCode,
                NewLevel = nextLevel,
                Reveals = nextLevelDef.Reveals.ToList()
            },
            cancellationToken: cancellationToken);

        // Push discovery client event to collection owner's WebSocket sessions
        await _entitySessionRegistry.PublishToEntitySessionsAsync(
            collection.OwnerType.ToString().ToLowerInvariant(), collection.OwnerId,
            new CollectionDiscoveryAdvancedClientEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                CollectionId = collection.CollectionId,
                EntryCode = body.EntryCode,
                NewDiscoveryLevel = nextLevel,
                RevealedKeys = nextLevelDef.Reveals.ToList()
            },
            cancellationToken);

        _logger.LogInformation(
            "Advanced discovery for entry {EntryCode} in collection {CollectionId} to level {NewLevel}",
            body.EntryCode, body.CollectionId, nextLevel);

        return (StatusCodes.OK, new AdvanceDiscoveryResponse
        {
            EntryCode = body.EntryCode,
            NewLevel = nextLevel,
            Reveals = nextLevelDef.Reveals.ToList()
        });
    }

    #endregion

    #region Resource Cleanup

    /// <summary>
    /// Cleans up all collections owned by a deleted character.
    /// Called by lib-resource cleanup coordination during cascading resource cleanup.
    /// </summary>
    public async Task<(StatusCodes, CleanupByCharacterResponse?)> CleanupByCharacterAsync(
        CleanupByCharacterRequest body,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Cleaning up collections for deleted character {CharacterId}", body.CharacterId);

        var deletedCount = await CleanupCollectionsForOwnerAsync(body.CharacterId, EntityType.Character, cancellationToken);

        return (StatusCodes.OK, new CleanupByCharacterResponse
        {
            DeletedCount = deletedCount
        });
    }

    #endregion
}
