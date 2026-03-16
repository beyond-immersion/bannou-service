using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.GameService;
using BeyondImmersion.BannouService.Helpers;
using BeyondImmersion.BannouService.Inventory;
using BeyondImmersion.BannouService.Item;
using BeyondImmersion.BannouService.Resource;
using BeyondImmersion.BannouService.Seed;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Linq.Expressions;

namespace BeyondImmersion.BannouService.Affix;

/// <summary>
/// Affix service: item modifier definition and procedural generation for equipment customization.
/// Manages affix definitions (templates), implicit mappings, per-item affix instances,
/// and provides pool-based generation, stat computation, and item valuation.
/// </summary>
[BannouService("affix", typeof(IAffixService), lifetime: ServiceLifetime.Scoped, layer: ServiceLayer.GameFeatures)]
public partial class AffixService : IAffixService, ICleanDeprecatedEntity
{
    // Core infrastructure
    private readonly IMessageBus _messageBus;
    private readonly IDistributedLockProvider _lockProvider;
    private readonly ITelemetryProvider _telemetryProvider;
    private readonly ILogger<AffixService> _logger;
    private readonly AffixServiceConfiguration _configuration;

    // Service clients (hard dependencies)
    private readonly IItemClient _itemClient;
    private readonly IGameServiceClient _gameServiceClient;
    private readonly IInventoryClient _inventoryClient;
    private readonly IResourceClient _resourceClient;
    private readonly ISeedClient _seedClient;

    // Soft dependencies
    private readonly IServiceProvider _serviceProvider;

    // Batch lifecycle event support
    private readonly AffixInstanceEventBatcher _instanceEventBatcher;

    // MySQL stores (persistent)
    private readonly IStateStore<AffixDefinitionModel> _definitionStore;
    private readonly IStateStore<ImplicitMappingModel> _implicitMappingStore;
    private readonly IStateStore<AffixInstanceModel> _instanceStore;
    private readonly IStateStore<string> _instanceStringStore;

    // Queryable MySQL stores
    private readonly IQueryableStateStore<AffixDefinitionModel> _definitionQueryStore;
    private readonly IQueryableStateStore<ImplicitMappingModel> _implicitMappingQueryStore;
    private readonly IQueryableStateStore<AffixInstanceModel> _instanceQueryStore;

    // Redis cache stores
    private readonly IStateStore<AffixDefinitionModel> _definitionCache;
    private readonly IStateStore<AffixInstanceModel> _instanceCache;
    private readonly IStateStore<ComputedStatsModel> _statsCache;
    private readonly IStateStore<EquipmentStatsModel> _equipmentCache;
    private readonly IStateStore<CachedAffixPool> _poolCache;

    /// <summary>
    /// Initializes the AffixService with all required dependencies.
    /// </summary>
    public AffixService(
        IMessageBus messageBus,
        IStateStoreFactory stateStoreFactory,
        IDistributedLockProvider lockProvider,
        ITelemetryProvider telemetryProvider,
        ILogger<AffixService> logger,
        AffixServiceConfiguration configuration,
        IItemClient itemClient,
        IGameServiceClient gameServiceClient,
        IInventoryClient inventoryClient,
        IResourceClient resourceClient,
        ISeedClient seedClient,
        IServiceProvider serviceProvider,
        AffixInstanceEventBatcher instanceEventBatcher,
        IEventConsumer eventConsumer)
    {
        _messageBus = messageBus;
        _lockProvider = lockProvider;
        _telemetryProvider = telemetryProvider;
        _logger = logger;
        _configuration = configuration;
        _itemClient = itemClient;
        _gameServiceClient = gameServiceClient;
        _inventoryClient = inventoryClient;
        _resourceClient = resourceClient;
        _seedClient = seedClient;
        _serviceProvider = serviceProvider;
        _instanceEventBatcher = instanceEventBatcher;

        // Constructor-cache all state store references per FOUNDATION TENETS
        _definitionStore = stateStoreFactory.GetStore<AffixDefinitionModel>(StateStoreDefinitions.AffixDefinitions);
        _implicitMappingStore = stateStoreFactory.GetStore<ImplicitMappingModel>(StateStoreDefinitions.AffixImplicitMappings);
        _instanceStore = stateStoreFactory.GetStore<AffixInstanceModel>(StateStoreDefinitions.AffixInstances);
        _instanceStringStore = stateStoreFactory.GetStore<string>(StateStoreDefinitions.AffixInstances);
        _definitionQueryStore = stateStoreFactory.GetQueryableStore<AffixDefinitionModel>(StateStoreDefinitions.AffixDefinitions);
        _implicitMappingQueryStore = stateStoreFactory.GetQueryableStore<ImplicitMappingModel>(StateStoreDefinitions.AffixImplicitMappings);
        _instanceQueryStore = stateStoreFactory.GetQueryableStore<AffixInstanceModel>(StateStoreDefinitions.AffixInstances);
        _definitionCache = stateStoreFactory.GetStore<AffixDefinitionModel>(StateStoreDefinitions.AffixDefinitionCache);
        _instanceCache = stateStoreFactory.GetStore<AffixInstanceModel>(StateStoreDefinitions.AffixInstanceCache);
        _statsCache = stateStoreFactory.GetStore<ComputedStatsModel>(StateStoreDefinitions.AffixInstanceCache);
        _equipmentCache = stateStoreFactory.GetStore<EquipmentStatsModel>(StateStoreDefinitions.AffixInstanceCache);
        _poolCache = stateStoreFactory.GetStore<CachedAffixPool>(StateStoreDefinitions.AffixPoolCache);

        RegisterEventConsumers(eventConsumer);
    }

    #region Definition CRUD

    /// <summary>Creates a new affix definition scoped to a game service.</summary>
    public async Task<(StatusCodes, AffixDefinitionResponse?)> CreateDefinitionAsync(CreateDefinitionRequest body, CancellationToken cancellationToken)
    {
        // Validate game service exists
        try
        {
            var gsResponse = await _gameServiceClient.GetServiceAsync(
                new GetServiceRequest { ServiceId = body.GameServiceId }, cancellationToken);
            if (gsResponse == null)
                return (StatusCodes.BadRequest, null);
        }
        catch (ApiException)
        {
            return (StatusCodes.BadRequest, null);
        }

        // Validate statGrants non-empty
        if (body.StatGrants == null || body.StatGrants.Count == 0)
            return (StatusCodes.BadRequest, null);

        // Check code uniqueness within game service
        var existing = await _definitionStore.GetAsync(
            BuildDefinitionCodeKey(body.GameServiceId, body.Code), cancellationToken);
        if (existing != null)
            return (StatusCodes.Conflict, null);

        // Check definition count limit
        var count = await _definitionQueryStore.CountAsync(
            d => d.GameServiceId == body.GameServiceId, cancellationToken);
        if (count >= _configuration.MaxDefinitionsPerGameService)
            return (StatusCodes.BadRequest, null);

        var now = DateTimeOffset.UtcNow;
        var definitionId = Guid.NewGuid();
        var model = new AffixDefinitionModel
        {
            DefinitionId = definitionId,
            GameServiceId = body.GameServiceId,
            Code = body.Code,
            SlotType = body.SlotType,
            ModGroup = body.ModGroup,
            Tier = body.Tier,
            Category = body.Category,
            Tags = body.Tags?.ToArray(),
            StatGrants = body.StatGrants.ToArray(),
            SpawnWeight = body.SpawnWeight,
            SpawnTagModifiers = body.SpawnTagModifiers?.ToArray(),
            RequiredItemLevel = body.RequiredItemLevel,
            RequiredInfluences = body.RequiredInfluences?.ToArray(),
            ValidItemClasses = body.ValidItemClasses?.ToArray(),
            DisplayName = body.DisplayName,
            DisplayOrder = body.DisplayOrder,
            CreatedAt = now
        };

        // Write to primary and code index
        await _definitionStore.SaveAsync(BuildDefinitionKey(definitionId), model, cancellationToken: cancellationToken);
        await _definitionStore.SaveAsync(BuildDefinitionCodeKey(body.GameServiceId, body.Code), model, cancellationToken: cancellationToken);

        // Write to cache
        await _definitionCache.SaveAsync(BuildDefinitionCacheKey(definitionId), model, new StateOptions { Ttl = _configuration.DefinitionCacheTtlSeconds }, cancellationToken);

        // Invalidate pool caches for affected item classes
        if (model.ValidItemClasses != null)
        {
            foreach (var itemClass in model.ValidItemClasses)
            {
                await InvalidatePoolCacheForItemClassAsync(body.GameServiceId, itemClass, cancellationToken);
            }
        }

        // Publish lifecycle event
        await _messageBus.PublishAffixDefinitionCreatedAsync(new AffixDefinitionCreatedEvent
        {
            DefinitionId = definitionId,
            GameServiceId = body.GameServiceId,
            Code = body.Code,
            SlotType = body.SlotType,
            ModGroup = body.ModGroup,
            Tier = body.Tier,
            Category = body.Category,
            Tags = body.Tags,
            StatGrants = body.StatGrants,
            SpawnWeight = body.SpawnWeight,
            SpawnTagModifiers = body.SpawnTagModifiers,
            RequiredItemLevel = body.RequiredItemLevel,
            RequiredInfluences = body.RequiredInfluences,
            ValidItemClasses = body.ValidItemClasses,
            DisplayName = body.DisplayName,
            DisplayOrder = body.DisplayOrder,
            CreatedAt = now,
            UpdatedAt = now,
            IsDeprecated = false
        }, cancellationToken);

        return (StatusCodes.OK, MapDefinitionToResponse(model));
    }

    /// <summary>Gets an affix definition by ID or by gameServiceId + code.</summary>
    public async Task<(StatusCodes, AffixDefinitionResponse?)> GetDefinitionAsync(GetDefinitionRequest body, CancellationToken cancellationToken)
    {
        AffixDefinitionModel? definition;

        if (body.DefinitionId.HasValue)
        {
            definition = await GetDefinitionWithCacheAsync(body.DefinitionId.Value, cancellationToken);
        }
        else if (body.GameServiceId.HasValue && body.Code != null)
        {
            // Check cache by code first (reuses definition cache by ID after lookup)
            definition = await _definitionStore.GetAsync(
                BuildDefinitionCodeKey(body.GameServiceId.Value, body.Code), cancellationToken);
            if (definition != null)
            {
                // Fill cache by ID
                await _definitionCache.SaveAsync(
                    BuildDefinitionCacheKey(definition.DefinitionId), definition,
                    new StateOptions { Ttl = _configuration.DefinitionCacheTtlSeconds }, cancellationToken);
            }
        }
        else
        {
            return (StatusCodes.BadRequest, null);
        }

        if (definition == null)
            return (StatusCodes.NotFound, null);

        return (StatusCodes.OK, MapDefinitionToResponse(definition));
    }

    /// <summary>Lists affix definitions with filtering and pagination.</summary>
    public async Task<(StatusCodes, ListDefinitionsResponse?)> ListDefinitionsAsync(ListDefinitionsRequest body, CancellationToken cancellationToken)
    {
        // Build combined predicate
        Expression<Func<AffixDefinitionModel, bool>> predicate = d => d.GameServiceId == body.GameServiceId;
        var allDefs = await _definitionQueryStore.QueryAsync(predicate, cancellationToken);

        // Apply in-memory filters for fields not easily expressible in single predicate
        var filtered = allDefs.AsEnumerable();
        if (!body.IncludeDeprecated)
            filtered = filtered.Where(d => !d.IsDeprecated);
        if (body.SlotType != null)
            filtered = filtered.Where(d => d.SlotType == body.SlotType);
        if (body.ModGroup != null)
            filtered = filtered.Where(d => d.ModGroup == body.ModGroup);
        if (body.Category != null)
            filtered = filtered.Where(d => d.Category == body.Category);
        if (body.TierMin.HasValue)
            filtered = filtered.Where(d => d.Tier >= body.TierMin.Value);
        if (body.TierMax.HasValue)
            filtered = filtered.Where(d => d.Tier <= body.TierMax.Value);

        var ordered = filtered.OrderBy(d => d.ModGroup).ThenBy(d => d.Tier).ToList();
        var totalCount = ordered.Count;
        var page = body.Page > 0 ? body.Page : 1;
        var pageSize = body.PageSize > 0 ? body.PageSize : 20;
        var definitions = ordered.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        return (StatusCodes.OK, new ListDefinitionsResponse
        {
            Definitions = definitions.Select(MapDefinitionToResponse).ToList(),
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
            HasNextPage = page * pageSize < totalCount
        });
    }

    /// <summary>Updates a definition with partial merge. Identity fields cannot be changed.</summary>
    public async Task<(StatusCodes, AffixDefinitionResponse?)> UpdateDefinitionAsync(UpdateDefinitionRequest body, CancellationToken cancellationToken)
    {
        var key = BuildDefinitionKey(body.DefinitionId);
        var (existing, etag) = await _definitionStore.GetWithETagAsync(key, cancellationToken);
        if (existing == null)
            return (StatusCodes.NotFound, null);

        var lockOwner = $"update-def-{Guid.NewGuid():N}";
        await using var lockResponse = await _lockProvider.LockAsync(
            StateStoreDefinitions.AffixLock, BuildDefinitionLockKey(body.DefinitionId),
            lockOwner, _configuration.LockTimeoutSeconds, cancellationToken);
        if (!lockResponse.Success)
            return (StatusCodes.Conflict, null);

        var changedFields = new List<string>();
        var generationFieldsChanged = false;

        // Apply partial update, tracking changed fields
        if (body.Tier.HasValue && body.Tier.Value != existing.Tier) { existing.Tier = body.Tier.Value; changedFields.Add("tier"); }
        if (body.Category != null && body.Category != existing.Category) { existing.Category = body.Category; changedFields.Add("category"); }
        if (body.Tags != null) { existing.Tags = body.Tags.ToArray(); changedFields.Add("tags"); }
        if (body.StatGrants != null) { existing.StatGrants = body.StatGrants.ToArray(); changedFields.Add("statGrants"); generationFieldsChanged = true; }
        if (body.SpawnWeight.HasValue && body.SpawnWeight.Value != existing.SpawnWeight) { existing.SpawnWeight = body.SpawnWeight.Value; changedFields.Add("spawnWeight"); generationFieldsChanged = true; }
        if (body.SpawnTagModifiers != null) { existing.SpawnTagModifiers = body.SpawnTagModifiers.ToArray(); changedFields.Add("spawnTagModifiers"); generationFieldsChanged = true; }
        if (body.RequiredItemLevel.HasValue && body.RequiredItemLevel.Value != existing.RequiredItemLevel) { existing.RequiredItemLevel = body.RequiredItemLevel.Value; changedFields.Add("requiredItemLevel"); generationFieldsChanged = true; }
        if (body.RequiredInfluences != null) { existing.RequiredInfluences = body.RequiredInfluences.ToArray(); changedFields.Add("requiredInfluences"); generationFieldsChanged = true; }
        if (body.ValidItemClasses != null) { existing.ValidItemClasses = body.ValidItemClasses.ToArray(); changedFields.Add("validItemClasses"); generationFieldsChanged = true; }
        if (body.DisplayName != null && body.DisplayName != existing.DisplayName) { existing.DisplayName = body.DisplayName; changedFields.Add("displayName"); }
        if (body.DisplayOrder.HasValue && body.DisplayOrder.Value != existing.DisplayOrder) { existing.DisplayOrder = body.DisplayOrder.Value; changedFields.Add("displayOrder"); }

        existing.UpdatedAt = DateTimeOffset.UtcNow;

        // ETag write
        var savedEtag = await _definitionStore.TrySaveAsync(key, existing, etag ?? string.Empty, cancellationToken: cancellationToken);
        if (savedEtag == null)
            return (StatusCodes.Conflict, null);

        // Invalidate caches
        await _definitionCache.DeleteAsync(BuildDefinitionCacheKey(body.DefinitionId), cancellationToken);
        await _definitionCache.DeleteAsync(BuildModGroupCacheKey(existing.GameServiceId, existing.ModGroup), cancellationToken);

        // Pool cache invalidation for generation-relevant changes
        if (generationFieldsChanged && existing.ValidItemClasses != null)
        {
            foreach (var itemClass in existing.ValidItemClasses)
            {
                await InvalidatePoolCacheForItemClassAsync(existing.GameServiceId, itemClass, cancellationToken);
            }
        }

        // Publish update event
        if (changedFields.Count > 0)
        {
            await _messageBus.PublishAffixDefinitionUpdatedAsync(MapDefinitionToUpdatedEvent(existing, changedFields), cancellationToken);
        }

        return (StatusCodes.OK, MapDefinitionToResponse(existing));
    }

    /// <summary>Deprecates a definition. Category B: one-way, idempotent.</summary>
    public async Task<(StatusCodes, AffixDefinitionResponse?)> DeprecateDefinitionAsync(DeprecateDefinitionRequest body, CancellationToken cancellationToken)
    {
        var key = BuildDefinitionKey(body.DefinitionId);
        var (existing, etag) = await _definitionStore.GetWithETagAsync(key, cancellationToken);
        if (existing == null)
            return (StatusCodes.NotFound, null);

        // Idempotent per IMPLEMENTATION TENETS
        if (existing.IsDeprecated)
            return (StatusCodes.OK, MapDefinitionToResponse(existing));

        var lockOwner = $"deprecate-def-{Guid.NewGuid():N}";
        await using var lockResponse = await _lockProvider.LockAsync(
            StateStoreDefinitions.AffixLock, BuildDefinitionLockKey(body.DefinitionId),
            lockOwner, _configuration.LockTimeoutSeconds, cancellationToken);
        if (!lockResponse.Success)
            return (StatusCodes.Conflict, null);

        existing.IsDeprecated = true;
        existing.DeprecatedAt = DateTimeOffset.UtcNow;
        existing.DeprecationReason = body.Reason;
        existing.UpdatedAt = DateTimeOffset.UtcNow;

        var savedEtag = await _definitionStore.TrySaveAsync(key, existing, etag ?? string.Empty, cancellationToken: cancellationToken);
        if (savedEtag == null)
            return (StatusCodes.Conflict, null);

        // Also update the code index entry
        await _definitionStore.SaveAsync(BuildDefinitionCodeKey(existing.GameServiceId, existing.Code), existing, cancellationToken: cancellationToken);

        // Invalidate caches
        await _definitionCache.DeleteAsync(BuildDefinitionCacheKey(body.DefinitionId), cancellationToken);
        await _definitionCache.DeleteAsync(BuildModGroupCacheKey(existing.GameServiceId, existing.ModGroup), cancellationToken);

        // Pool cache invalidation
        if (existing.ValidItemClasses != null)
        {
            foreach (var itemClass in existing.ValidItemClasses)
            {
                await InvalidatePoolCacheForItemClassAsync(existing.GameServiceId, itemClass, cancellationToken);
            }
        }

        var changedFields = new List<string> { "isDeprecated", "deprecatedAt", "deprecationReason" };
        await _messageBus.PublishAffixDefinitionUpdatedAsync(MapDefinitionToUpdatedEvent(existing, changedFields), cancellationToken);

        return (StatusCodes.OK, MapDefinitionToResponse(existing));
    }

    /// <summary>Bulk seeds affix definitions, skipping existing codes.</summary>
    public async Task<(StatusCodes, SeedDefinitionsResponse?)> SeedDefinitionsAsync(SeedDefinitionsRequest body, CancellationToken cancellationToken)
    {
        // Validate game service exists
        try
        {
            var gsResponse = await _gameServiceClient.GetServiceAsync(
                new GetServiceRequest { ServiceId = body.GameServiceId }, cancellationToken);
            if (gsResponse == null)
                return (StatusCodes.BadRequest, null);
        }
        catch (ApiException)
        {
            return (StatusCodes.BadRequest, null);
        }

        var createdCount = 0;
        var skippedCount = 0;
        var affectedItemClasses = new HashSet<string>();

        foreach (var entry in body.Definitions)
        {
            var existing = await _definitionStore.GetAsync(
                BuildDefinitionCodeKey(body.GameServiceId, entry.Code), cancellationToken);
            if (existing != null)
            {
                skippedCount++;
                continue;
            }

            var now = DateTimeOffset.UtcNow;
            var definitionId = Guid.NewGuid();
            var model = new AffixDefinitionModel
            {
                DefinitionId = definitionId,
                GameServiceId = body.GameServiceId,
                Code = entry.Code,
                SlotType = entry.SlotType,
                ModGroup = entry.ModGroup,
                Tier = entry.Tier,
                Category = entry.Category,
                Tags = entry.Tags?.ToArray(),
                StatGrants = entry.StatGrants.ToArray(),
                SpawnWeight = entry.SpawnWeight,
                SpawnTagModifiers = entry.SpawnTagModifiers?.ToArray(),
                RequiredItemLevel = entry.RequiredItemLevel,
                RequiredInfluences = entry.RequiredInfluences?.ToArray(),
                ValidItemClasses = entry.ValidItemClasses?.ToArray(),
                DisplayName = entry.DisplayName,
                DisplayOrder = entry.DisplayOrder,
                CreatedAt = now
            };

            await _definitionStore.SaveAsync(BuildDefinitionKey(definitionId), model, cancellationToken: cancellationToken);
            await _definitionStore.SaveAsync(BuildDefinitionCodeKey(body.GameServiceId, entry.Code), model, cancellationToken: cancellationToken);
            createdCount++;

            if (entry.ValidItemClasses != null)
            {
                foreach (var ic in entry.ValidItemClasses)
                    affectedItemClasses.Add(ic);
            }
        }

        // Deferred pool cache invalidation
        foreach (var itemClass in affectedItemClasses)
        {
            await InvalidatePoolCacheForItemClassAsync(body.GameServiceId, itemClass, cancellationToken);
        }

        return (StatusCodes.OK, new SeedDefinitionsResponse { CreatedCount = createdCount, SkippedCount = skippedCount });
    }

    /// <summary>Lists unique mod groups with definition counts for a game service.</summary>
    public async Task<(StatusCodes, ListModGroupsResponse?)> ListModGroupsAsync(ListModGroupsRequest body, CancellationToken cancellationToken)
    {
        var allDefs = await _definitionQueryStore.QueryAsync(
            d => d.GameServiceId == body.GameServiceId, cancellationToken);

        var filtered = allDefs.AsEnumerable();
        if (!body.IncludeDeprecated)
            filtered = filtered.Where(d => !d.IsDeprecated);

        var groups = filtered
            .GroupBy(d => d.ModGroup)
            .Select(g => new ModGroupSummary { Code = g.Key, DefinitionCount = g.Count() })
            .ToList();

        return (StatusCodes.OK, new ListModGroupsResponse { ModGroups = groups });
    }

    /// <summary>Category B cleanup sweep: removes deprecated definitions with zero applied instances.</summary>
    public async Task<(StatusCodes, CleanDeprecatedResponse?)> CleanDeprecatedDefinitionsAsync(CleanDeprecatedRequest body, CancellationToken cancellationToken)
    {
        var deprecated = await _definitionQueryStore.QueryAsync(
            d => d.IsDeprecated, cancellationToken);

        var result = await DeprecationCleanupHelper.ExecuteCleanupSweepAsync(
            deprecated,
            getEntityId: d => d.DefinitionId,
            getDeprecatedAt: d => d.DeprecatedAt,
            hasInstancesAsync: async (d, ct) =>
                await _instanceStringStore.HasStringListEntriesAsync(BuildInstancesByDefinitionKey(d.DefinitionId), ct),
            deleteAndPublishAsync: async (d, ct) =>
            {
                await _definitionStore.DeleteAsync(BuildDefinitionKey(d.DefinitionId), ct);
                await _definitionStore.DeleteAsync(BuildDefinitionCodeKey(d.GameServiceId, d.Code), ct);
                await _definitionCache.DeleteAsync(BuildDefinitionCacheKey(d.DefinitionId), ct);
                await _definitionCache.DeleteAsync(BuildModGroupCacheKey(d.GameServiceId, d.ModGroup), ct);

                if (d.ValidItemClasses != null)
                {
                    foreach (var itemClass in d.ValidItemClasses)
                        await InvalidatePoolCacheForItemClassAsync(d.GameServiceId, itemClass, ct);
                }

                await _messageBus.PublishAffixDefinitionDeletedAsync(new AffixDefinitionDeletedEvent
                {
                    DefinitionId = d.DefinitionId,
                    GameServiceId = d.GameServiceId,
                    Code = d.Code,
                    SlotType = d.SlotType,
                    ModGroup = d.ModGroup,
                    Tier = d.Tier,
                    Category = d.Category,
                    Tags = d.Tags?.ToList(),
                    StatGrants = d.StatGrants.ToList(),
                    SpawnWeight = d.SpawnWeight,
                    SpawnTagModifiers = d.SpawnTagModifiers?.ToList(),
                    RequiredItemLevel = d.RequiredItemLevel,
                    RequiredInfluences = d.RequiredInfluences?.ToList(),
                    ValidItemClasses = d.ValidItemClasses?.ToList(),
                    DisplayName = d.DisplayName,
                    DisplayOrder = d.DisplayOrder,
                    CreatedAt = d.CreatedAt,
                    UpdatedAt = d.UpdatedAt ?? d.CreatedAt,
                    IsDeprecated = d.IsDeprecated,
                    DeprecatedAt = d.DeprecatedAt,
                    DeprecationReason = d.DeprecationReason
                }, ct);
            },
            body.GracePeriodDays,
            body.DryRun,
            _logger,
            _telemetryProvider,
            cancellationToken);

        return (StatusCodes.OK, new CleanDeprecatedResponse
        {
            Cleaned = result.Cleaned,
            Remaining = result.Remaining,
            Errors = result.Errors,
            CleanedIds = result.CleanedIds.ToList()
        });
    }

    #endregion

    #region Implicit Mappings

    /// <summary>Creates an implicit mapping for an item template code.</summary>
    public async Task<(StatusCodes, ImplicitMappingResponse?)> CreateImplicitMappingAsync(CreateImplicitMappingRequest body, CancellationToken cancellationToken)
    {
        // Check uniqueness
        var existing = await _implicitMappingStore.GetAsync(
            BuildImplicitTemplateKey(body.GameServiceId, body.ItemTemplateCode), cancellationToken);
        if (existing != null)
            return (StatusCodes.Conflict, null);

        // Validate all referenced definitions exist and are implicit slot type
        foreach (var defId in body.ImplicitDefinitionIds)
        {
            var def = await _definitionStore.GetAsync(BuildDefinitionKey(defId), cancellationToken);
            if (def == null)
                return (StatusCodes.BadRequest, null);
            if (def.SlotType != "implicit")
                return (StatusCodes.BadRequest, null);
        }

        var mappingId = Guid.NewGuid();
        var model = new ImplicitMappingModel
        {
            MappingId = mappingId,
            GameServiceId = body.GameServiceId,
            ItemTemplateCode = body.ItemTemplateCode,
            ImplicitDefinitionIds = body.ImplicitDefinitionIds.ToArray()
        };

        await _implicitMappingStore.SaveAsync(BuildImplicitMappingKey(mappingId), model, cancellationToken: cancellationToken);
        await _implicitMappingStore.SaveAsync(BuildImplicitTemplateKey(body.GameServiceId, body.ItemTemplateCode), model, cancellationToken: cancellationToken);

        return (StatusCodes.OK, MapImplicitToResponse(model));
    }

    /// <summary>Gets implicit mapping for an item template code.</summary>
    public async Task<(StatusCodes, ImplicitMappingResponse?)> GetImplicitMappingAsync(GetImplicitMappingRequest body, CancellationToken cancellationToken)
    {
        var mapping = await _implicitMappingStore.GetAsync(
            BuildImplicitTemplateKey(body.GameServiceId, body.ItemTemplateCode), cancellationToken);
        if (mapping == null)
            return (StatusCodes.NotFound, null);

        return (StatusCodes.OK, MapImplicitToResponse(mapping));
    }

    /// <summary>Bulk seeds implicit mappings, skipping existing ones.</summary>
    public async Task<(StatusCodes, SeedImplicitMappingsResponse?)> SeedImplicitMappingsAsync(SeedImplicitMappingsRequest body, CancellationToken cancellationToken)
    {
        var createdCount = 0;
        var skippedCount = 0;

        foreach (var entry in body.Mappings)
        {
            var existing = await _implicitMappingStore.GetAsync(
                BuildImplicitTemplateKey(body.GameServiceId, entry.ItemTemplateCode), cancellationToken);
            if (existing != null)
            {
                skippedCount++;
                continue;
            }

            // Validate definitions
            var valid = true;
            foreach (var defId in entry.ImplicitDefinitionIds)
            {
                var def = await _definitionStore.GetAsync(BuildDefinitionKey(defId), cancellationToken);
                if (def == null || def.SlotType != "implicit")
                {
                    valid = false;
                    break;
                }
            }

            if (!valid)
            {
                skippedCount++;
                continue;
            }

            var mappingId = Guid.NewGuid();
            var model = new ImplicitMappingModel
            {
                MappingId = mappingId,
                GameServiceId = body.GameServiceId,
                ItemTemplateCode = entry.ItemTemplateCode,
                ImplicitDefinitionIds = entry.ImplicitDefinitionIds.ToArray()
            };

            await _implicitMappingStore.SaveAsync(BuildImplicitMappingKey(mappingId), model, cancellationToken: cancellationToken);
            await _implicitMappingStore.SaveAsync(BuildImplicitTemplateKey(body.GameServiceId, entry.ItemTemplateCode), model, cancellationToken: cancellationToken);
            createdCount++;
        }

        return (StatusCodes.OK, new SeedImplicitMappingsResponse { CreatedCount = createdCount, SkippedCount = skippedCount });
    }

    /// <summary>Rolls implicit values for an item template. Pure computation.</summary>
    public async Task<(StatusCodes, RollImplicitsResponse?)> RollImplicitsAsync(RollImplicitsRequest body, CancellationToken cancellationToken)
    {
        var mapping = await _implicitMappingStore.GetAsync(
            BuildImplicitTemplateKey(body.GameServiceId, body.ItemTemplateCode), cancellationToken);
        if (mapping == null)
            return (StatusCodes.NotFound, null);

        var overrideMap = body.Overrides?.ToDictionary(o => o.DefinitionId) ?? new Dictionary<Guid, ImplicitDefinitionRef>();
        var rolledSlots = new List<RolledSlot>();

        foreach (var defId in mapping.ImplicitDefinitionIds)
        {
            var definition = await GetDefinitionWithCacheAsync(defId, cancellationToken);
            if (definition == null)
                return (StatusCodes.BadRequest, null);

            var values = RollValuesForDefinition(definition, overrideMap.GetValueOrDefault(defId));
            rolledSlots.Add(new RolledSlot
            {
                DefinitionId = defId,
                DefinitionCode = definition.Code,
                RolledValues = values.ToList()
            });
        }

        return (StatusCodes.OK, new RollImplicitsResponse { RolledSlots = rolledSlots });
    }

    #endregion

    #region Instance Operations

    /// <summary>Initializes affix state for an item instance.</summary>
    public async Task<(StatusCodes, AffixInstanceResponse?)> InitializeItemAffixesAsync(InitializeItemAffixesRequest body, CancellationToken cancellationToken)
    {
        // Validate item exists
        try
        {
            var itemResponse = await _itemClient.GetItemInstanceAsync(
                new GetItemInstanceRequest { InstanceId = body.ItemInstanceId }, cancellationToken);
            if (itemResponse == null)
                return (StatusCodes.BadRequest, null);
        }
        catch (ApiException)
        {
            return (StatusCodes.BadRequest, null);
        }

        // Check not already initialized
        var existing = await _instanceStore.GetAsync(BuildInstanceKey(body.ItemInstanceId), cancellationToken);
        if (existing != null)
            return (StatusCodes.Conflict, null);

        // Create item-traits seed
        Guid? seedId = null;
        try
        {
            var seedResponse = await _seedClient.CreateSeedAsync(
                new CreateSeedRequest
                {
                    SeedTypeCode = _configuration.ItemTraitsSeedTypeCode,
                    OwnerType = EntityType.Item,
                    OwnerId = body.ItemInstanceId,
                    GameServiceId = body.GameServiceId
                }, cancellationToken);
            if (seedResponse != null)
                seedId = seedResponse.SeedId;
        }
        catch (ApiException ex)
        {
            _logger.LogWarning(ex, "Failed to create item-traits seed for item {ItemInstanceId}", body.ItemInstanceId);
        }

        var now = DateTimeOffset.UtcNow;
        var instance = new AffixInstanceModel
        {
            ItemInstanceId = body.ItemInstanceId,
            GameServiceId = body.GameServiceId,
            ItemLevel = 1,
            Quality = 0,
            SeedId = seedId,
            EffectiveRarity = "normal",
            States = new AffixStatesModel { IsIdentified = true },
            CreatedAt = now
        };

        // Apply initial affix set data if provided
        if (body.AffixSetData != null)
        {
            instance.ImplicitSlots = body.AffixSetData.ImplicitSlots?.Select(MapSlotDataToModel).ToList() ?? new List<AffixSlotModel>();
            instance.PrefixSlots = body.AffixSetData.PrefixSlots?.Select(MapSlotDataToModel).ToList() ?? new List<AffixSlotModel>();
            instance.SuffixSlots = body.AffixSetData.SuffixSlots?.Select(MapSlotDataToModel).ToList() ?? new List<AffixSlotModel>();
            instance.EffectiveRarity = body.AffixSetData.EffectiveRarity;
            instance.ItemLevel = body.AffixSetData.ItemLevel;
        }

        await _instanceStore.SaveAsync(BuildInstanceKey(body.ItemInstanceId), instance, cancellationToken: cancellationToken);
        await _instanceCache.SaveAsync(BuildInstanceCacheKey(body.ItemInstanceId), instance, new StateOptions { Ttl = _configuration.InstanceCacheTtlSeconds }, cancellationToken);

        // Maintain game service index
        await _instanceStringStore.AddToStringListAsync(
            BuildInstanceGameIndexKey(body.GameServiceId), body.ItemInstanceId.ToString(),
            _configuration.ListOperationMaxRetries, _logger, cancellationToken);

        // Feed batch lifecycle event
        _instanceEventBatcher.AddCreated(new AffixInstanceBatchEntry
        {
            ItemInstanceId = body.ItemInstanceId,
            GameServiceId = body.GameServiceId,
            EffectiveRarity = instance.EffectiveRarity,
            ItemLevel = instance.ItemLevel,
            Quality = instance.Quality,
            CreatedAt = now,
            UpdatedAt = now
        });

        return (StatusCodes.OK, MapInstanceToResponse(instance));
    }

    /// <summary>Gets affix instance with cache read-through.</summary>
    public async Task<(StatusCodes, AffixInstanceResponse?)> GetAffixInstanceAsync(GetAffixInstanceRequest body, CancellationToken cancellationToken)
    {
        var instance = await GetInstanceWithCacheAsync(body.ItemInstanceId, cancellationToken);
        if (instance == null)
            return (StatusCodes.NotFound, null);

        return (StatusCodes.OK, MapInstanceToResponse(instance));
    }

    /// <summary>Applies an affix to an item with full validation.</summary>
    public async Task<(StatusCodes, ApplyAffixResponse?)> ApplyAffixAsync(ApplyAffixRequest body, CancellationToken cancellationToken)
    {
        var lockOwner = $"apply-affix-{Guid.NewGuid():N}";
        await using var lockResponse = await _lockProvider.LockAsync(
            StateStoreDefinitions.AffixLock, BuildItemLockKey(body.ItemInstanceId),
            lockOwner, _configuration.LockTimeoutSeconds, cancellationToken);
        if (!lockResponse.Success)
            return (StatusCodes.Conflict, null);

        var key = BuildInstanceKey(body.ItemInstanceId);
        var (instance, etag) = await _instanceStore.GetWithETagAsync(key, cancellationToken);
        if (instance == null)
        {
            // Auto-create empty instance for unmanaged items
            instance = new AffixInstanceModel
            {
                ItemInstanceId = body.ItemInstanceId,
                CreatedAt = DateTimeOffset.UtcNow,
                States = new AffixStatesModel { IsIdentified = true }
            };
            etag = null;
        }

        // Get definition
        var definition = await GetDefinitionWithCacheAsync(body.DefinitionId, cancellationToken);
        if (definition == null)
            return (StatusCodes.BadRequest, null);

        // Category B guard: cannot apply deprecated definition
        if (definition.IsDeprecated)
            return (StatusCodes.BadRequest, null);

        // Validate item exists and get item class
        try
        {
            var itemResponse = await _itemClient.GetItemInstanceAsync(
                new GetItemInstanceRequest { InstanceId = body.ItemInstanceId }, cancellationToken);
            if (itemResponse == null)
                return (StatusCodes.BadRequest, null);
        }
        catch (ApiException)
        {
            return (StatusCodes.BadRequest, null);
        }

        // State validations
        if (instance.States.IsCorrupted)
            return (StatusCodes.BadRequest, null);
        if (instance.States.IsMirrored)
            return (StatusCodes.BadRequest, null);

        // Item level validation
        if (instance.ItemLevel < definition.RequiredItemLevel)
            return (StatusCodes.BadRequest, null);

        // Influence requirements
        if (definition.RequiredInfluences != null && definition.RequiredInfluences.Length > 0)
        {
            if (!definition.RequiredInfluences.All(ri => instance.Influences.Contains(ri)))
                return (StatusCodes.BadRequest, null);
        }

        // Get target slot list based on slot type
        var targetSlots = GetSlotListForType(instance, definition.SlotType);

        // Slot capacity check
        var maxSlots = GetMaxSlotsForType(definition.SlotType);
        if (targetSlots.Count >= maxSlots)
            return (StatusCodes.BadRequest, null);

        // Mod group exclusivity
        if (instance.AllSlots().Any(s => s.ModGroup == definition.ModGroup))
            return (StatusCodes.Conflict, null);

        // Roll values
        var rolledValues = RollValuesForDefinition(definition, null, body.ValuePercentileTarget);

        // Append new affix slot
        var newSlot = new AffixSlotModel
        {
            DefinitionId = definition.DefinitionId,
            DefinitionCode = definition.Code,
            ModGroup = definition.ModGroup,
            RolledValues = rolledValues
        };
        targetSlots.Add(newSlot);

        // Recompute effective rarity
        var previousRarity = instance.EffectiveRarity;
        instance.EffectiveRarity = ComputeEffectiveRarity(instance);
        instance.UpdatedAt = DateTimeOffset.UtcNow;

        // Save with ETag
        if (etag != null)
        {
            var saveResult = await _instanceStore.TrySaveAsync(key, instance, etag ?? string.Empty, cancellationToken: cancellationToken);
            if (saveResult == null)
                return (StatusCodes.Conflict, null);
        }
        else
        {
            await _instanceStore.SaveAsync(key, instance, cancellationToken: cancellationToken);
        }

        // Invalidate caches
        await _instanceCache.DeleteAsync(BuildInstanceCacheKey(body.ItemInstanceId), cancellationToken);
        await _statsCache.DeleteAsync(BuildStatsCacheKey(body.ItemInstanceId), cancellationToken);

        // Record seed growth
        if (instance.SeedId.HasValue)
        {
            try
            {
                await _seedClient.RecordGrowthAsync(new RecordGrowthRequest
                {
                    SeedId = instance.SeedId.Value,
                    Domain = "enchantment",
                    Amount = definition.Tier
                }, cancellationToken);
            }
            catch (ApiException ex)
            {
                _logger.LogWarning(ex, "Failed to record seed growth for item {ItemInstanceId}", body.ItemInstanceId);
            }
        }

        // Publish modifier applied event
        await _messageBus.PublishAffixModifierAppliedAsync(new AffixModifierAppliedEvent
        {
            ItemInstanceId = body.ItemInstanceId,
            DefinitionId = body.DefinitionId,
            DefinitionCode = definition.Code,
            SlotType = definition.SlotType,
            ModGroup = definition.ModGroup,
            RolledValues = rolledValues.ToList()
        }, cancellationToken);

        // Reverse index maintenance
        await _instanceStringStore.AddToStringListAsync(
            BuildInstancesByDefinitionKey(body.DefinitionId), body.ItemInstanceId.ToString(),
            _configuration.ListOperationMaxRetries, _logger, cancellationToken);

        // Rarity transition event
        if (previousRarity != instance.EffectiveRarity)
        {
            await _messageBus.PublishAffixRarityChangedAsync(new AffixRarityChangedEvent
            {
                ItemInstanceId = body.ItemInstanceId,
                PreviousRarity = previousRarity,
                NewRarity = instance.EffectiveRarity
            }, cancellationToken);
        }

        // Feed batch lifecycle modified event
        _instanceEventBatcher.AddModified(body.ItemInstanceId, MapInstanceToModifiedEntry(instance, new[] { "prefixSlots", "suffixSlots", "enchantSlots", "effectiveRarity" }));

        return (StatusCodes.OK, new ApplyAffixResponse { Instance = MapInstanceToResponse(instance) });
    }

    /// <summary>Removes an affix from an item.</summary>
    public async Task<(StatusCodes, RemoveAffixResponse?)> RemoveAffixAsync(RemoveAffixRequest body, CancellationToken cancellationToken)
    {
        var lockOwner = $"remove-affix-{Guid.NewGuid():N}";
        await using var lockResponse = await _lockProvider.LockAsync(
            StateStoreDefinitions.AffixLock, BuildItemLockKey(body.ItemInstanceId),
            lockOwner, _configuration.LockTimeoutSeconds, cancellationToken);
        if (!lockResponse.Success)
            return (StatusCodes.Conflict, null);

        var key = BuildInstanceKey(body.ItemInstanceId);
        var (instance, etag) = await _instanceStore.GetWithETagAsync(key, cancellationToken);
        if (instance == null)
            return (StatusCodes.NotFound, null);

        // Find target slot
        var (slot, slotList) = FindSlotByDefinitionId(instance, body.DefinitionId);
        if (slot == null || slotList == null)
            return (StatusCodes.NotFound, null);

        // State validations
        if (instance.States.IsCorrupted)
            return (StatusCodes.BadRequest, null);
        if (instance.States.IsMirrored)
            return (StatusCodes.BadRequest, null);
        if (slot.IsFractured)
            return (StatusCodes.BadRequest, null);

        // Remove slot
        slotList.Remove(slot);

        var previousRarity = instance.EffectiveRarity;
        instance.EffectiveRarity = ComputeEffectiveRarity(instance);
        instance.UpdatedAt = DateTimeOffset.UtcNow;

        var savedEtag = await _instanceStore.TrySaveAsync(key, instance, etag ?? string.Empty, cancellationToken: cancellationToken);
        if (savedEtag == null)
            return (StatusCodes.Conflict, null);

        await _instanceCache.DeleteAsync(BuildInstanceCacheKey(body.ItemInstanceId), cancellationToken);
        await _statsCache.DeleteAsync(BuildStatsCacheKey(body.ItemInstanceId), cancellationToken);

        await _messageBus.PublishAffixModifierRemovedAsync(new AffixModifierRemovedEvent
        {
            ItemInstanceId = body.ItemInstanceId,
            DefinitionId = body.DefinitionId,
            DefinitionCode = slot.DefinitionCode,
            SlotType = GetSlotTypeForList(instance, slotList),
            ModGroup = slot.ModGroup
        }, cancellationToken);

        // Reverse index maintenance
        await _instanceStringStore.RemoveFromStringListAsync(
            BuildInstancesByDefinitionKey(body.DefinitionId), body.ItemInstanceId.ToString(),
            _configuration.ListOperationMaxRetries, _logger, cancellationToken);

        if (previousRarity != instance.EffectiveRarity)
        {
            await _messageBus.PublishAffixRarityChangedAsync(new AffixRarityChangedEvent
            {
                ItemInstanceId = body.ItemInstanceId,
                PreviousRarity = previousRarity,
                NewRarity = instance.EffectiveRarity
            }, cancellationToken);
        }

        _instanceEventBatcher.AddModified(body.ItemInstanceId, MapInstanceToModifiedEntry(instance, new[] { "prefixSlots", "suffixSlots", "enchantSlots", "effectiveRarity" }));

        return (StatusCodes.OK, new RemoveAffixResponse { Instance = MapInstanceToResponse(instance) });
    }

    /// <summary>Rerolls values for an affix on an item.</summary>
    public async Task<(StatusCodes, RerollValuesResponse?)> RerollValuesAsync(RerollValuesRequest body, CancellationToken cancellationToken)
    {
        var lockOwner = $"reroll-affix-{Guid.NewGuid():N}";
        await using var lockResponse = await _lockProvider.LockAsync(
            StateStoreDefinitions.AffixLock, BuildItemLockKey(body.ItemInstanceId),
            lockOwner, _configuration.LockTimeoutSeconds, cancellationToken);
        if (!lockResponse.Success)
            return (StatusCodes.Conflict, null);

        var key = BuildInstanceKey(body.ItemInstanceId);
        var (instance, etag) = await _instanceStore.GetWithETagAsync(key, cancellationToken);
        if (instance == null)
            return (StatusCodes.NotFound, null);

        var (slot, _) = FindSlotByDefinitionId(instance, body.DefinitionId);
        if (slot == null)
            return (StatusCodes.NotFound, null);

        // State validations (fractured does NOT block reroll)
        if (instance.States.IsCorrupted)
            return (StatusCodes.BadRequest, null);
        if (instance.States.IsMirrored)
            return (StatusCodes.BadRequest, null);

        var definition = await GetDefinitionWithCacheAsync(body.DefinitionId, cancellationToken);
        if (definition == null)
            return (StatusCodes.BadRequest, null);

        var previousValues = slot.RolledValues.ToArray();
        slot.RolledValues = RollValuesForDefinition(definition);
        instance.UpdatedAt = DateTimeOffset.UtcNow;

        var savedEtag = await _instanceStore.TrySaveAsync(key, instance, etag ?? string.Empty, cancellationToken: cancellationToken);
        if (savedEtag == null)
            return (StatusCodes.Conflict, null);

        await _instanceCache.DeleteAsync(BuildInstanceCacheKey(body.ItemInstanceId), cancellationToken);
        await _statsCache.DeleteAsync(BuildStatsCacheKey(body.ItemInstanceId), cancellationToken);

        await _messageBus.PublishAffixModifierRerolledAsync(new AffixModifierRerolledEvent
        {
            ItemInstanceId = body.ItemInstanceId,
            DefinitionId = body.DefinitionId,
            DefinitionCode = definition.Code,
            PreviousValues = previousValues.ToList(),
            NewValues = slot.RolledValues.ToList()
        }, cancellationToken);

        _instanceEventBatcher.AddModified(body.ItemInstanceId, MapInstanceToModifiedEntry(instance, new[] { "rolledValues" }));

        return (StatusCodes.OK, new RerollValuesResponse
        {
            Instance = MapInstanceToResponse(instance),
            PreviousValues = previousValues.ToList(),
            NewValues = slot.RolledValues.ToList()
        });
    }

    /// <summary>Sets item state flags with transition validation.</summary>
    public async Task<(StatusCodes, SetItemStateResponse?)> SetItemStateAsync(SetItemStateRequest body, CancellationToken cancellationToken)
    {
        var lockOwner = $"set-state-{Guid.NewGuid():N}";
        await using var lockResponse = await _lockProvider.LockAsync(
            StateStoreDefinitions.AffixLock, BuildItemLockKey(body.ItemInstanceId),
            lockOwner, _configuration.LockTimeoutSeconds, cancellationToken);
        if (!lockResponse.Success)
            return (StatusCodes.Conflict, null);

        var key = BuildInstanceKey(body.ItemInstanceId);
        var (instance, etag) = await _instanceStore.GetWithETagAsync(key, cancellationToken);
        if (instance == null)
            return (StatusCodes.NotFound, null);

        var changedFlags = new List<ChangedFlag>();

        // Validate irreversible transitions
        if (body.IsCorrupted.HasValue)
        {
            if (!body.IsCorrupted.Value && instance.States.IsCorrupted)
                return (StatusCodes.BadRequest, null); // Cannot uncorrupt
            if (body.IsCorrupted.Value != instance.States.IsCorrupted)
            {
                changedFlags.Add(new ChangedFlag { FlagName = "isCorrupted", OldValue = instance.States.IsCorrupted, NewValue = body.IsCorrupted.Value });
                instance.States.IsCorrupted = body.IsCorrupted.Value;
            }
        }
        if (body.IsMirrored.HasValue)
        {
            if (!body.IsMirrored.Value && instance.States.IsMirrored)
                return (StatusCodes.BadRequest, null); // Cannot unmirror
            if (body.IsMirrored.Value != instance.States.IsMirrored)
            {
                changedFlags.Add(new ChangedFlag { FlagName = "isMirrored", OldValue = instance.States.IsMirrored, NewValue = body.IsMirrored.Value });
                instance.States.IsMirrored = body.IsMirrored.Value;
            }
        }
        if (body.IsSplit.HasValue)
        {
            if (!body.IsSplit.Value && instance.States.IsSplit)
                return (StatusCodes.BadRequest, null); // Cannot unsplit
            if (body.IsSplit.Value != instance.States.IsSplit)
            {
                changedFlags.Add(new ChangedFlag { FlagName = "isSplit", OldValue = instance.States.IsSplit, NewValue = body.IsSplit.Value });
                instance.States.IsSplit = body.IsSplit.Value;
            }
        }
        if (body.IsIdentified.HasValue && body.IsIdentified.Value != instance.States.IsIdentified)
        {
            changedFlags.Add(new ChangedFlag { FlagName = "isIdentified", OldValue = instance.States.IsIdentified, NewValue = body.IsIdentified.Value });
            instance.States.IsIdentified = body.IsIdentified.Value;
        }
        if (body.IsSynthesized.HasValue && body.IsSynthesized.Value != instance.States.IsSynthesized)
        {
            changedFlags.Add(new ChangedFlag { FlagName = "isSynthesized", OldValue = instance.States.IsSynthesized, NewValue = body.IsSynthesized.Value });
            instance.States.IsSynthesized = body.IsSynthesized.Value;
        }

        // Handle fracture targeting specific slot
        if (body.FractureDefinitionId.HasValue)
        {
            var (slot, _) = FindSlotByDefinitionId(instance, body.FractureDefinitionId.Value);
            if (slot == null)
                return (StatusCodes.NotFound, null);
            slot.IsFractured = true;
        }

        instance.UpdatedAt = DateTimeOffset.UtcNow;

        var savedEtag = await _instanceStore.TrySaveAsync(key, instance, etag ?? string.Empty, cancellationToken: cancellationToken);
        if (savedEtag == null)
            return (StatusCodes.Conflict, null);

        await _instanceCache.DeleteAsync(BuildInstanceCacheKey(body.ItemInstanceId), cancellationToken);
        await _statsCache.DeleteAsync(BuildStatsCacheKey(body.ItemInstanceId), cancellationToken);

        if (changedFlags.Count > 0)
        {
            await _messageBus.PublishAffixInstanceStateChangedAsync(new AffixInstanceStateChangedEvent
            {
                ItemInstanceId = body.ItemInstanceId,
                ChangedFlags = changedFlags
            }, cancellationToken);
        }

        _instanceEventBatcher.AddModified(body.ItemInstanceId, MapInstanceToModifiedEntry(instance, new[] { "states" }));

        return (StatusCodes.OK, new SetItemStateResponse { Instance = MapInstanceToResponse(instance) });
    }

    /// <summary>Sets influence types on an item.</summary>
    public async Task<(StatusCodes, SetInfluenceResponse?)> SetInfluenceAsync(SetInfluenceRequest body, CancellationToken cancellationToken)
    {
        var lockOwner = $"set-influence-{Guid.NewGuid():N}";
        await using var lockResponse = await _lockProvider.LockAsync(
            StateStoreDefinitions.AffixLock, BuildItemLockKey(body.ItemInstanceId),
            lockOwner, _configuration.LockTimeoutSeconds, cancellationToken);
        if (!lockResponse.Success)
            return (StatusCodes.Conflict, null);

        var key = BuildInstanceKey(body.ItemInstanceId);
        var (instance, etag) = await _instanceStore.GetWithETagAsync(key, cancellationToken);
        if (instance == null)
            return (StatusCodes.NotFound, null);

        if (instance.States.IsMirrored)
            return (StatusCodes.BadRequest, null);

        var previousInfluences = instance.Influences.ToList();
        instance.Influences = body.Influences.ToList();
        instance.UpdatedAt = DateTimeOffset.UtcNow;

        var savedEtag = await _instanceStore.TrySaveAsync(key, instance, etag ?? string.Empty, cancellationToken: cancellationToken);
        if (savedEtag == null)
            return (StatusCodes.Conflict, null);

        await _instanceCache.DeleteAsync(BuildInstanceCacheKey(body.ItemInstanceId), cancellationToken);

        // Invalidate pool caches (influences affect eligible pools)
        // We don't have itemClass readily here, so invalidate broadly
        // The pool cache is keyed by gameServiceId:itemClass — a broad invalidation is acceptable here

        await _messageBus.PublishAffixInfluenceChangedAsync(new AffixInfluenceChangedEvent
        {
            ItemInstanceId = body.ItemInstanceId,
            PreviousInfluences = previousInfluences,
            NewInfluences = body.Influences.ToList()
        }, cancellationToken);

        _instanceEventBatcher.AddModified(body.ItemInstanceId, MapInstanceToModifiedEntry(instance, new[] { "influences" }));

        return (StatusCodes.OK, new SetInfluenceResponse { Instance = MapInstanceToResponse(instance) });
    }

    #endregion

    #region Generation

    /// <summary>Generates a filtered affix pool for item generation.</summary>
    public async Task<(StatusCodes, AffixPoolResponse?)> GenerateAffixPoolAsync(GenerateAffixPoolRequest body, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.affix", "AffixService.GenerateAffixPool");

        var ilvlBucket = (body.ItemLevel / _configuration.ItemLevelBucketSize) * _configuration.ItemLevelBucketSize;
        var poolKey = BuildPoolCacheKey(body.GameServiceId, body.ItemClass, body.SlotType, ilvlBucket);

        var pool = await _poolCache.GetAsync(poolKey, cancellationToken);
        if (pool == null)
        {
            // Acquire pool-rebuild lock to prevent thundering herd on cache miss
            var poolLockOwner = $"pool-rebuild-{Guid.NewGuid():N}";
            await using var poolLock = await _lockProvider.LockAsync(
                StateStoreDefinitions.AffixLock, BuildPoolRebuildLockKey(body.GameServiceId),
                poolLockOwner, _configuration.LockTimeoutSeconds, cancellationToken);

            // Check cache again after acquiring lock (another thread may have built it)
            pool = await _poolCache.GetAsync(poolKey, cancellationToken);
            if (pool == null)
            {
                pool = await BuildPoolAsync(body.GameServiceId, body.ItemClass, body.SlotType, ilvlBucket, cancellationToken);
                await _poolCache.SaveAsync(poolKey, pool, new StateOptions { Ttl = _configuration.PoolCacheTtlSeconds }, cancellationToken);
            }
        }

        // In-memory filtering
        var entries = pool.Entries.AsEnumerable();

        // Filter by item level
        entries = entries.Where(e => e.RequiredItemLevel <= body.ItemLevel);

        // Exclude existing mod groups
        if (body.ExistingModGroups != null && body.ExistingModGroups.Count > 0)
        {
            var exclusions = new HashSet<string>(body.ExistingModGroups);
            entries = entries.Where(e => !exclusions.Contains(e.ModGroup));
        }

        // Apply external weight modifiers and convert to response entries
        var weightModifierMap = body.WeightModifiers?.ToDictionary(w => w.DefinitionId) ?? new Dictionary<Guid, WeightModifier>();

        var responseEntries = new List<AffixPoolEntry>();
        var totalWeight = 0;

        foreach (var entry in entries)
        {
            var effectiveWeight = entry.BaseWeight;
            if (weightModifierMap.TryGetValue(entry.DefinitionId, out var modifier))
            {
                effectiveWeight = (int)(effectiveWeight * modifier.Multiplier);
            }
            if (effectiveWeight <= 0) continue;

            totalWeight += effectiveWeight;
            responseEntries.Add(new AffixPoolEntry
            {
                DefinitionId = entry.DefinitionId,
                DefinitionCode = entry.DefinitionCode,
                ModGroup = entry.ModGroup,
                Tier = entry.Tier,
                EffectiveWeight = effectiveWeight,
                StatGrants = entry.StatGrants.ToList()
            });
        }

        return (StatusCodes.OK, new AffixPoolResponse { Entries = responseEntries, TotalWeight = totalWeight });
    }

    /// <summary>Generates a complete affix set for an item. Pure computation.</summary>
    public async Task<(StatusCodes, AffixSetDataResponse?)> GenerateAffixSetAsync(GenerateAffixSetRequest body, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.affix", "AffixService.GenerateAffixSet");

        // Roll implicits if mapping exists
        var implicitSlots = new List<AffixSlotData>();
        var mapping = await _implicitMappingStore.GetAsync(
            BuildImplicitTemplateKey(body.GameServiceId, body.ItemTemplateCode), cancellationToken);
        if (mapping != null)
        {
            foreach (var defId in mapping.ImplicitDefinitionIds)
            {
                var def = await GetDefinitionWithCacheAsync(defId, cancellationToken);
                if (def != null)
                {
                    var values = RollValuesForDefinition(def, null, body.ValuePercentileTarget);
                    implicitSlots.Add(new AffixSlotData
                    {
                        DefinitionId = defId,
                        DefinitionCode = def.Code,
                        ModGroup = def.ModGroup,
                        RolledValues = values.ToList()
                    });
                }
            }
        }

        // Determine slot counts from target rarity
        var maxPrefixes = _configuration.DefaultMaxPrefixes;
        var maxSuffixes = _configuration.DefaultMaxSuffixes;

        // Simple rarity-based slot count mapping
        switch (body.TargetRarity?.ToLowerInvariant())
        {
            case "normal": maxPrefixes = 0; maxSuffixes = 0; break;
            case "magic": maxPrefixes = 1; maxSuffixes = 1; break;
            case "rare": maxPrefixes = _configuration.DefaultMaxPrefixes; maxSuffixes = _configuration.DefaultMaxSuffixes; break;
        }

        var prefixSlots = new List<AffixSlotData>();
        var suffixSlots = new List<AffixSlotData>();
        var usedModGroups = new HashSet<string>();

        // Generate prefix affixes
        for (var i = 0; i < maxPrefixes; i++)
        {
            var selected = await SelectWeightedAffixAsync(body.GameServiceId, body.ItemClass, "prefix", body.ItemLevel, usedModGroups, body.WeightModifiers, body.Influences, cancellationToken);
            if (selected == null) break;
            usedModGroups.Add(selected.ModGroup);
            var values = RollValuesForDefinition(selected, null, body.ValuePercentileTarget);
            prefixSlots.Add(new AffixSlotData
            {
                DefinitionId = selected.DefinitionId,
                DefinitionCode = selected.Code,
                ModGroup = selected.ModGroup,
                RolledValues = values.ToList()
            });
        }

        // Generate suffix affixes
        for (var i = 0; i < maxSuffixes; i++)
        {
            var selected = await SelectWeightedAffixAsync(body.GameServiceId, body.ItemClass, "suffix", body.ItemLevel, usedModGroups, body.WeightModifiers, body.Influences, cancellationToken);
            if (selected == null) break;
            usedModGroups.Add(selected.ModGroup);
            var values = RollValuesForDefinition(selected, null, body.ValuePercentileTarget);
            suffixSlots.Add(new AffixSlotData
            {
                DefinitionId = selected.DefinitionId,
                DefinitionCode = selected.Code,
                ModGroup = selected.ModGroup,
                RolledValues = values.ToList()
            });
        }

        var effectiveRarity = DetermineRarity(prefixSlots.Count, suffixSlots.Count);

        return (StatusCodes.OK, new AffixSetDataResponse
        {
            ImplicitSlots = implicitSlots,
            PrefixSlots = prefixSlots,
            SuffixSlots = suffixSlots,
            EffectiveRarity = effectiveRarity,
            ItemLevel = body.ItemLevel
        });
    }

    /// <summary>Generates affix sets for multiple items with batch event deduplication.</summary>
    public async Task<(StatusCodes, BatchGenerateAffixSetsResponse?)> BatchGenerateAffixSetsAsync(BatchGenerateAffixSetsRequest body, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.affix", "AffixService.BatchGenerateAffixSets");

        var results = new List<AffixSetDataResponse>();

        foreach (var item in body.Items)
        {
            var (status, result) = await GenerateAffixSetAsync(new GenerateAffixSetRequest
            {
                GameServiceId = item.GameServiceId,
                ItemTemplateCode = item.ItemTemplateCode,
                ItemClass = item.ItemClass,
                ItemLevel = item.ItemLevel,
                TargetRarity = item.TargetRarity,
                Influences = item.Influences,
                WeightModifiers = item.WeightModifiers,
                ValuePercentileTarget = item.ValuePercentileTarget
            }, cancellationToken);

            if (status == StatusCodes.OK && result != null)
                results.Add(result);
        }

        // Batch event deduplication by source within window
        var windowBucket = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / _configuration.GenerationEventDeduplicationWindowSeconds;
        var dedupKey = $"batch-gen:{body.SourceId}:{windowBucket}";
        var existingDedup = await _poolCache.GetAsync(dedupKey, cancellationToken);
        if (existingDedup == null)
        {
            // Use pool cache store for dedup keys (it's Redis, suitable for short-lived keys)
            await _poolCache.SaveAsync(dedupKey, new CachedAffixPool(), new StateOptions { Ttl = _configuration.GenerationEventDeduplicationWindowSeconds }, cancellationToken);
            await _messageBus.PublishAffixBatchGeneratedAsync(new AffixBatchGeneratedEvent
            {
                SourceId = body.SourceId,
                BatchSize = body.Items.Count,
                GameServiceId = body.Items.FirstOrDefault()?.GameServiceId ?? Guid.Empty
            }, cancellationToken);
        }

        return (StatusCodes.OK, new BatchGenerateAffixSetsResponse { Results = results });
    }

    #endregion

    #region Query & Computation

    /// <summary>Gets enriched affix data for an item, respecting identification state.</summary>
    public async Task<(StatusCodes, EnrichedAffixInstanceResponse?)> GetItemAffixesAsync(GetItemAffixesRequest body, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.affix", "AffixService.GetItemAffixes");

        var instance = await GetInstanceWithCacheAsync(body.ItemInstanceId, cancellationToken);
        if (instance == null)
            return (StatusCodes.NotFound, null);

        var enrichImplicit = await EnrichSlotsAsync(instance.ImplicitSlots, instance.States.IsIdentified, cancellationToken);
        var enrichPrefix = await EnrichSlotsAsync(instance.PrefixSlots, instance.States.IsIdentified, cancellationToken);
        var enrichSuffix = await EnrichSlotsAsync(instance.SuffixSlots, instance.States.IsIdentified, cancellationToken);
        var enrichEnchant = await EnrichSlotsAsync(instance.EnchantSlots, instance.States.IsIdentified, cancellationToken);

        return (StatusCodes.OK, new EnrichedAffixInstanceResponse
        {
            ItemInstanceId = instance.ItemInstanceId,
            GameServiceId = instance.GameServiceId,
            EffectiveRarity = instance.EffectiveRarity,
            ItemLevel = instance.ItemLevel,
            ImplicitSlots = enrichImplicit,
            PrefixSlots = enrichPrefix,
            SuffixSlots = enrichSuffix,
            EnchantSlots = enrichEnchant,
            Influences = instance.Influences.ToList(),
            States = MapStatesToResponse(instance.States),
            Quality = instance.Quality
        });
    }

    /// <summary>Computes aggregated stats for a single item.</summary>
    public async Task<(StatusCodes, ComputedItemStatsResponse?)> ComputeItemStatsAsync(ComputeItemStatsRequest body, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.affix", "AffixService.ComputeItemStats");

        // Check stats cache first
        var cached = await _statsCache.GetAsync(BuildStatsCacheKey(body.ItemInstanceId), cancellationToken);
        if (cached != null)
            return (StatusCodes.OK, new ComputedItemStatsResponse { Stats = cached.Stats, QualityModifier = cached.QualityModifier });

        var instance = await GetInstanceWithCacheAsync(body.ItemInstanceId, cancellationToken);
        if (instance == null)
            return (StatusCodes.NotFound, null);

        var statTotals = new Dictionary<string, double>();
        var qualityModifier = 1.0 + (instance.Quality / 100.0);

        // Aggregate all slot values
        foreach (var slot in instance.AllSlots())
        {
            var def = await GetDefinitionWithCacheAsync(slot.DefinitionId, cancellationToken);
            if (def == null) continue;

            for (var i = 0; i < def.StatGrants.Length && i < slot.RolledValues.Length; i++)
            {
                var statCode = def.StatGrants[i].StatCode;
                var value = slot.RolledValues[i] * qualityModifier;
                statTotals[statCode] = statTotals.GetValueOrDefault(statCode) + value;
            }
        }

        var stats = statTotals.Select(kv => new StatValue { StatCode = kv.Key, Value = Math.Round(kv.Value, 2) }).ToList();
        var computedStats = new ComputedStatsModel { Stats = stats, QualityModifier = qualityModifier };

        await _statsCache.SaveAsync(BuildStatsCacheKey(body.ItemInstanceId), computedStats, new StateOptions { Ttl = _configuration.ComputedStatsCacheTtlSeconds }, cancellationToken);

        return (StatusCodes.OK, new ComputedItemStatsResponse { Stats = stats, QualityModifier = qualityModifier });
    }

    /// <summary>Computes aggregate equipment stats for an entity.</summary>
    public async Task<(StatusCodes, EquipmentStatsResponse?)> ComputeEquipmentStatsAsync(ComputeEquipmentStatsRequest body, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.affix", "AffixService.ComputeEquipmentStats");

        var cacheKey = BuildEquipmentCacheKey(body.EntityId, body.EntityType);
        var cached = await _equipmentCache.GetAsync(cacheKey, cancellationToken);
        if (cached != null)
            return (StatusCodes.OK, new EquipmentStatsResponse { PerStatTotals = cached.PerStatTotals, PerItemBreakdown = cached.PerItemBreakdown });

        // Query equipment containers and extract equipped item IDs
        var equippedItemIds = new List<Guid>();
        try
        {
            var invResponse = await _inventoryClient.ListContainersAsync(
                new ListContainersRequest { OwnerId = body.EntityId }, cancellationToken);
            if (invResponse?.Containers == null || invResponse.Containers.Count == 0)
                return (StatusCodes.OK, new EquipmentStatsResponse { PerStatTotals = new List<StatValue>(), PerItemBreakdown = new List<ItemBreakdown>() });

            // Fetch contents of each equipment container to get item IDs
            foreach (var container in invResponse.Containers.Where(c => c.IsEquipmentSlot))
            {
                try
                {
                    var containerWithContents = await _inventoryClient.GetContainerAsync(
                        new GetContainerRequest { ContainerId = container.ContainerId, IncludeContents = true }, cancellationToken);
                    if (containerWithContents?.Items != null)
                    {
                        equippedItemIds.AddRange(containerWithContents.Items.Select(i => i.InstanceId));
                    }
                }
                catch (ApiException ex)
                {
                    _logger.LogWarning(ex, "Failed to get contents of equipment container {ContainerId}", container.ContainerId);
                }
            }
        }
        catch (ApiException)
        {
            return (StatusCodes.OK, new EquipmentStatsResponse { PerStatTotals = new List<StatValue>(), PerItemBreakdown = new List<ItemBreakdown>() });
        }

        var perStatTotals = new Dictionary<string, double>();
        var perItemBreakdown = new List<ItemBreakdown>();

        // Aggregate stats from each equipped item that has affix instances
        // Fetch instances for each equipped item individually (avoids full table scan)
        foreach (var equippedId in equippedItemIds)
        {
            var inst = await GetInstanceWithCacheAsync(equippedId, cancellationToken);
            if (inst == null) continue;

            var itemStats = new Dictionary<string, double>();
            var qualityMod = 1.0 + (inst.Quality / 100.0);

            foreach (var slot in inst.AllSlots())
            {
                var def = await GetDefinitionWithCacheAsync(slot.DefinitionId, cancellationToken);
                if (def == null) continue;
                for (var i = 0; i < def.StatGrants.Length && i < slot.RolledValues.Length; i++)
                {
                    var sc = def.StatGrants[i].StatCode;
                    var v = slot.RolledValues[i] * qualityMod;
                    itemStats[sc] = itemStats.GetValueOrDefault(sc) + v;
                    perStatTotals[sc] = perStatTotals.GetValueOrDefault(sc) + v;
                }
            }

            perItemBreakdown.Add(new ItemBreakdown
            {
                ItemInstanceId = inst.ItemInstanceId,
                Stats = itemStats.Select(kv => new StatValue { StatCode = kv.Key, Value = Math.Round(kv.Value, 2) }).ToList()
            });
        }

        var result = new EquipmentStatsModel
        {
            PerStatTotals = perStatTotals.Select(kv => new StatValue { StatCode = kv.Key, Value = Math.Round(kv.Value, 2) }).ToList(),
            PerItemBreakdown = perItemBreakdown
        };
        await _equipmentCache.SaveAsync(cacheKey, result, new StateOptions { Ttl = _configuration.EquipmentStatsCacheTtlSeconds }, cancellationToken);

        return (StatusCodes.OK, new EquipmentStatsResponse { PerStatTotals = result.PerStatTotals, PerItemBreakdown = result.PerItemBreakdown });
    }

    /// <summary>Compares stats between two items.</summary>
    public async Task<(StatusCodes, ItemComparisonResponse?)> CompareItemsAsync(CompareItemsRequest body, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.affix", "AffixService.CompareItems");

        var instanceA = await GetInstanceWithCacheAsync(body.ItemInstanceIdA, cancellationToken);
        if (instanceA == null) return (StatusCodes.NotFound, null);

        var instanceB = await GetInstanceWithCacheAsync(body.ItemInstanceIdB, cancellationToken);
        if (instanceB == null) return (StatusCodes.NotFound, null);

        var statsA = await ComputeStatsForInstance(instanceA, cancellationToken);
        var statsB = await ComputeStatsForInstance(instanceB, cancellationToken);

        var allStatCodes = statsA.Keys.Union(statsB.Keys).Distinct();
        var diffs = allStatCodes.Select(sc =>
        {
            var vA = statsA.GetValueOrDefault(sc);
            var vB = statsB.GetValueOrDefault(sc);
            var delta = vA - vB;
            return new StatDiff
            {
                StatCode = sc,
                ValueA = Math.Round(vA, 2),
                ValueB = Math.Round(vB, 2),
                Delta = Math.Round(delta, 2),
                Winner = delta > 0 ? "A" : delta < 0 ? "B" : null
            };
        }).ToList();

        return (StatusCodes.OK, new ItemComparisonResponse { StatDiffs = diffs });
    }

    /// <summary>Estimates the value of an item based on affix quality.</summary>
    public async Task<(StatusCodes, ItemValueEstimateResponse?)> EstimateItemValueAsync(EstimateItemValueRequest body, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.affix", "AffixService.EstimateItemValue");

        var instance = await GetInstanceWithCacheAsync(body.ItemInstanceId, cancellationToken);
        if (instance == null) return (StatusCodes.NotFound, null);

        var scoringFactors = new List<ScoringFactor>();
        var totalScore = 0.0;
        var totalWeight = 0.0;

        foreach (var slot in instance.AllSlots())
        {
            var def = await GetDefinitionWithCacheAsync(slot.DefinitionId, cancellationToken);
            if (def == null) continue;

            // Compute roll percentile for each stat
            var rollScore = 0.0;
            for (var i = 0; i < def.StatGrants.Length && i < slot.RolledValues.Length; i++)
            {
                var grant = def.StatGrants[i];
                var range = grant.MaxValue - grant.MinValue;
                if (range > 0)
                    rollScore += (slot.RolledValues[i] - grant.MinValue) / range;
                else
                    rollScore += 1.0; // Max if no range
            }
            if (def.StatGrants.Length > 0)
                rollScore /= def.StatGrants.Length;

            // Tier weight (higher tier = higher weight)
            var tierWeight = def.Tier * 1.0;
            var affixScore = tierWeight * rollScore;
            totalScore += affixScore;
            totalWeight += tierWeight;

            scoringFactors.Add(new ScoringFactor
            {
                Name = $"{def.Code}:t{def.Tier}",
                Score = Math.Round(rollScore, 3),
                Weight = tierWeight
            });
        }

        // Apply multipliers
        if (instance.AllSlots().Any(s => s.IsFractured))
        {
            totalScore *= 1.15; // Fractured bonus
            scoringFactors.Add(new ScoringFactor { Name = "fractured_bonus", Score = 0.15, Weight = 1.0 });
        }
        if (instance.Influences.Count > 0)
        {
            totalScore *= 1.0 + (instance.Influences.Count * 0.1);
            scoringFactors.Add(new ScoringFactor { Name = "influence_bonus", Score = instance.Influences.Count * 0.1, Weight = 1.0 });
        }
        if (instance.Quality > 0)
        {
            totalScore *= 1.0 + (instance.Quality / 100.0);
            scoringFactors.Add(new ScoringFactor { Name = "quality_bonus", Score = instance.Quality / 100.0, Weight = 1.0 });
        }

        // Normalize to 0-1
        var maxPossible = totalWeight > 0 ? totalWeight : 1.0;
        var normalizedScore = Math.Clamp(totalScore / (maxPossible * 2.0), 0.0, 1.0);
        var suggestedValue = normalizedScore * 1000.0; // Base currency scaling

        return (StatusCodes.OK, new ItemValueEstimateResponse
        {
            NormalizedScore = Math.Round(normalizedScore, 4),
            SuggestedCurrencyValue = Math.Round(suggestedValue, 2),
            ScoringFactors = scoringFactors
        });
    }

    #endregion

    #region Cleanup

    /// <summary>Cleans up all affix data for a game service (lib-resource callback).</summary>
    public async Task<(StatusCodes, CleanupByGameServiceResponse?)> CleanupByGameServiceAsync(CleanupByGameServiceRequest body, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.affix", "AffixService.CleanupByGameService");

        _logger.LogInformation("Cleaning up affix data for game service {GameServiceId}", body.GameServiceId);

        // Instance cleanup
        var instanceIndex = await _instanceStringStore.GetAsync(BuildInstanceGameIndexKey(body.GameServiceId), cancellationToken);
        if (instanceIndex != null)
        {
            var itemIds = BannouJson.Deserialize<List<string>>(instanceIndex);
            if (itemIds != null)
            {
                foreach (var itemIdStr in itemIds)
                {
                    if (Guid.TryParse(itemIdStr, out var itemId))
                    {
                        await _instanceStore.DeleteAsync(BuildInstanceKey(itemId), cancellationToken);
                        await _instanceCache.DeleteAsync(BuildInstanceCacheKey(itemId), cancellationToken);
                        await _statsCache.DeleteAsync(BuildStatsCacheKey(itemId), cancellationToken);
                    }
                }
            }
            await _instanceStringStore.DeleteAsync(BuildInstanceGameIndexKey(body.GameServiceId), cancellationToken);
        }

        // Definition cleanup
        var definitions = await _definitionQueryStore.QueryAsync(
            d => d.GameServiceId == body.GameServiceId, cancellationToken);
        var cleanedModGroups = new HashSet<string>();
        foreach (var def in definitions)
        {
            await _definitionStore.DeleteAsync(BuildDefinitionKey(def.DefinitionId), cancellationToken);
            await _definitionStore.DeleteAsync(BuildDefinitionCodeKey(body.GameServiceId, def.Code), cancellationToken);
            await _definitionCache.DeleteAsync(BuildDefinitionCacheKey(def.DefinitionId), cancellationToken);
            if (cleanedModGroups.Add(def.ModGroup))
                await _definitionCache.DeleteAsync(BuildModGroupCacheKey(body.GameServiceId, def.ModGroup), cancellationToken);
        }

        // Implicit mapping cleanup
        var mappings = await _implicitMappingQueryStore.QueryAsync(
            m => m.GameServiceId == body.GameServiceId, cancellationToken);
        foreach (var mapping in mappings)
        {
            await _implicitMappingStore.DeleteAsync(BuildImplicitMappingKey(mapping.MappingId), cancellationToken);
            await _implicitMappingStore.DeleteAsync(BuildImplicitTemplateKey(body.GameServiceId, mapping.ItemTemplateCode), cancellationToken);
        }

        // Pool cache cleanup: invalidate all pool caches for this game service
        var affectedItemClasses = definitions
            .Where(d => d.ValidItemClasses != null)
            .SelectMany(d => d.ValidItemClasses!)
            .Distinct();
        foreach (var itemClass in affectedItemClasses)
        {
            await InvalidatePoolCacheForItemClassAsync(body.GameServiceId, itemClass, cancellationToken);
        }

        _logger.LogInformation("Affix cleanup complete for game service {GameServiceId}: {DefCount} definitions, {MapCount} mappings removed",
            body.GameServiceId, definitions.Count, mappings.Count);

        return (StatusCodes.OK, new CleanupByGameServiceResponse());
    }

    #endregion

    #region Private Helpers

    /// <summary>Gets definition from cache with read-through to store.</summary>
    private async Task<AffixDefinitionModel?> GetDefinitionWithCacheAsync(Guid definitionId, CancellationToken ct)
    {
        var cached = await _definitionCache.GetAsync(BuildDefinitionCacheKey(definitionId), ct);
        if (cached != null) return cached;

        var stored = await _definitionStore.GetAsync(BuildDefinitionKey(definitionId), ct);
        if (stored != null)
            await _definitionCache.SaveAsync(BuildDefinitionCacheKey(definitionId), stored, new StateOptions { Ttl = _configuration.DefinitionCacheTtlSeconds }, ct);

        return stored;
    }

    /// <summary>Gets instance from cache with read-through to store.</summary>
    private async Task<AffixInstanceModel?> GetInstanceWithCacheAsync(Guid itemInstanceId, CancellationToken ct)
    {
        var cached = await _instanceCache.GetAsync(BuildInstanceCacheKey(itemInstanceId), ct);
        if (cached != null) return cached;

        var stored = await _instanceStore.GetAsync(BuildInstanceKey(itemInstanceId), ct);
        if (stored != null)
            await _instanceCache.SaveAsync(BuildInstanceCacheKey(itemInstanceId), stored, new StateOptions { Ttl = _configuration.InstanceCacheTtlSeconds }, ct);

        return stored;
    }

    /// <summary>Rolls values for a definition's stat grants.</summary>
    private static double[] RollValuesForDefinition(AffixDefinitionModel definition, ImplicitDefinitionRef? overrides = null, double? percentileTarget = null)
    {
        var random = Random.Shared;
        var values = new double[definition.StatGrants.Length];
        for (var i = 0; i < definition.StatGrants.Length; i++)
        {
            var grant = definition.StatGrants[i];
            var min = overrides?.MinValueOverride ?? grant.MinValue;
            var max = overrides?.MaxValueOverride ?? grant.MaxValue;

            if (percentileTarget.HasValue)
            {
                // Bias toward percentile target
                var target = min + (max - min) * percentileTarget.Value;
                var spread = (max - min) * 0.1;
                values[i] = Math.Clamp(target + (random.NextDouble() - 0.5) * spread, min, max);
            }
            else
            {
                values[i] = min + random.NextDouble() * (max - min);
            }
            values[i] = Math.Round(values[i], 2);
        }
        return values;
    }

    /// <summary>Builds a pool of eligible definitions for generation.</summary>
    private async Task<CachedAffixPool> BuildPoolAsync(Guid gameServiceId, string itemClass, string slotType, int ilvlBucket, CancellationToken ct)
    {
        var upperBound = ilvlBucket + _configuration.ItemLevelBucketSize;
        var definitions = await _definitionQueryStore.QueryAsync(
            d => d.GameServiceId == gameServiceId && d.SlotType == slotType && !d.IsDeprecated && d.RequiredItemLevel <= upperBound, ct);

        var pool = new CachedAffixPool();
        foreach (var def in definitions)
        {
            if (def.ValidItemClasses != null && !def.ValidItemClasses.Contains(itemClass))
                continue;

            pool.Entries.Add(new CachedPoolEntry
            {
                DefinitionId = def.DefinitionId,
                DefinitionCode = def.Code,
                ModGroup = def.ModGroup,
                Tier = def.Tier,
                BaseWeight = def.SpawnWeight,
                StatGrants = def.StatGrants,
                RequiredItemLevel = def.RequiredItemLevel,
                RequiredInfluences = def.RequiredInfluences,
                SpawnTagModifiers = def.SpawnTagModifiers
            });
            pool.TotalWeight += def.SpawnWeight;
        }

        return pool;
    }

    /// <summary>Selects a weighted random definition from the pool.</summary>
    private async Task<AffixDefinitionModel?> SelectWeightedAffixAsync(
        Guid gameServiceId, string itemClass, string slotType, int itemLevel,
        HashSet<string> usedModGroups, ICollection<WeightModifier>? weightModifiers,
        ICollection<string>? influences, CancellationToken ct)
    {
        var (_, poolResponse) = await GenerateAffixPoolAsync(new GenerateAffixPoolRequest
        {
            GameServiceId = gameServiceId,
            ItemClass = itemClass,
            ItemLevel = itemLevel,
            SlotType = slotType,
            ExistingModGroups = usedModGroups.ToList(),
            WeightModifiers = weightModifiers?.ToList(),
            Influences = influences?.ToList()
        }, ct);

        if (poolResponse == null || poolResponse.Entries.Count == 0 || poolResponse.TotalWeight <= 0)
            return null;

        var roll = Random.Shared.Next(poolResponse.TotalWeight);
        var cumulative = 0;
        foreach (var entry in poolResponse.Entries)
        {
            cumulative += entry.EffectiveWeight;
            if (roll < cumulative)
            {
                return await GetDefinitionWithCacheAsync(entry.DefinitionId, ct);
            }
        }

        return null;
    }

    /// <summary>Computes effective rarity from slot counts.</summary>
    private static string ComputeEffectiveRarity(AffixInstanceModel instance)
        => DetermineRarity(instance.PrefixSlots.Count, instance.SuffixSlots.Count);

    /// <summary>Determines rarity from prefix/suffix counts.</summary>
    private static string DetermineRarity(int prefixCount, int suffixCount)
    {
        var total = prefixCount + suffixCount;
        return total switch
        {
            0 => "normal",
            <= 2 => "magic",
            _ => "rare"
        };
    }

    /// <summary>Gets the slot list for a given slot type string.</summary>
    private static List<AffixSlotModel> GetSlotListForType(AffixInstanceModel instance, string slotType)
    {
        return slotType.ToLowerInvariant() switch
        {
            "prefix" => instance.PrefixSlots,
            "suffix" => instance.SuffixSlots,
            "enchant" => instance.EnchantSlots,
            "implicit" => instance.ImplicitSlots,
            _ => instance.PrefixSlots
        };
    }

    /// <summary>Gets max slot count for a slot type.</summary>
    private int GetMaxSlotsForType(string slotType)
    {
        return slotType.ToLowerInvariant() switch
        {
            "prefix" => _configuration.DefaultMaxPrefixes,
            "suffix" => _configuration.DefaultMaxSuffixes,
            "enchant" => _configuration.MaxAffixesPerItem,
            "implicit" => _configuration.MaxAffixesPerItem,
            _ => _configuration.DefaultMaxPrefixes
        };
    }

    /// <summary>Finds an affix slot by definition ID across all slot types.</summary>
    private static (AffixSlotModel? slot, List<AffixSlotModel>? list) FindSlotByDefinitionId(AffixInstanceModel instance, Guid definitionId)
    {
        foreach (var slot in instance.ImplicitSlots)
            if (slot.DefinitionId == definitionId) return (slot, instance.ImplicitSlots);
        foreach (var slot in instance.PrefixSlots)
            if (slot.DefinitionId == definitionId) return (slot, instance.PrefixSlots);
        foreach (var slot in instance.SuffixSlots)
            if (slot.DefinitionId == definitionId) return (slot, instance.SuffixSlots);
        foreach (var slot in instance.EnchantSlots)
            if (slot.DefinitionId == definitionId) return (slot, instance.EnchantSlots);
        return (null, null);
    }

    /// <summary>Gets the slot type string for a list of slots within an instance.</summary>
    private static string GetSlotTypeForList(AffixInstanceModel instance, List<AffixSlotModel> list)
    {
        if (ReferenceEquals(list, instance.ImplicitSlots)) return "implicit";
        if (ReferenceEquals(list, instance.PrefixSlots)) return "prefix";
        if (ReferenceEquals(list, instance.SuffixSlots)) return "suffix";
        if (ReferenceEquals(list, instance.EnchantSlots)) return "enchant";
        return "unknown";
    }

    /// <summary>Invalidates pool cache entries for all level buckets of an item class.</summary>
    private async Task InvalidatePoolCacheForItemClassAsync(Guid gameServiceId, string itemClass, CancellationToken ct)
    {
        // Invalidate known slot types across level buckets
        var slotTypes = new[] { "prefix", "suffix", "enchant" };
        foreach (var slotType in slotTypes)
        {
            for (var bucket = 0; bucket <= _configuration.MaxItemLevel; bucket += _configuration.ItemLevelBucketSize)
            {
                await _poolCache.DeleteAsync(BuildPoolCacheKey(gameServiceId, itemClass, slotType, bucket), ct);
            }
        }
    }

    /// <summary>Enriches affix slots with definition details.</summary>
    private async Task<List<EnrichedAffixSlot>> EnrichSlotsAsync(List<AffixSlotModel> slots, bool isIdentified, CancellationToken ct)
    {
        var enriched = new List<EnrichedAffixSlot>();
        foreach (var slot in slots)
        {
            var def = await GetDefinitionWithCacheAsync(slot.DefinitionId, ct);
            enriched.Add(new EnrichedAffixSlot
            {
                DefinitionId = slot.DefinitionId,
                DefinitionCode = slot.DefinitionCode,
                ModGroup = slot.ModGroup,
                DisplayName = def?.DisplayName,
                Tier = def?.Tier,
                Category = def?.Category,
                RolledValues = isIdentified ? slot.RolledValues.ToList() : null,
                StatGrants = isIdentified ? def?.StatGrants.ToList() : null,
                IsFractured = slot.IsFractured
            });
        }
        return enriched;
    }

    /// <summary>Computes stats for an instance without caching (used by CompareItems).</summary>
    private async Task<Dictionary<string, double>> ComputeStatsForInstance(AffixInstanceModel instance, CancellationToken ct)
    {
        var stats = new Dictionary<string, double>();
        var qualityMod = 1.0 + (instance.Quality / 100.0);
        foreach (var slot in instance.AllSlots())
        {
            var def = await GetDefinitionWithCacheAsync(slot.DefinitionId, ct);
            if (def == null) continue;
            for (var i = 0; i < def.StatGrants.Length && i < slot.RolledValues.Length; i++)
            {
                var sc = def.StatGrants[i].StatCode;
                stats[sc] = stats.GetValueOrDefault(sc) + slot.RolledValues[i] * qualityMod;
            }
        }
        return stats;
    }

    #endregion

    #region Mapping Helpers

    private static AffixDefinitionResponse MapDefinitionToResponse(AffixDefinitionModel model)
        => new()
        {
            DefinitionId = model.DefinitionId,
            GameServiceId = model.GameServiceId,
            Code = model.Code,
            SlotType = model.SlotType,
            ModGroup = model.ModGroup,
            Tier = model.Tier,
            Category = model.Category,
            Tags = model.Tags?.ToList(),
            StatGrants = model.StatGrants.ToList(),
            SpawnWeight = model.SpawnWeight,
            SpawnTagModifiers = model.SpawnTagModifiers?.ToList(),
            RequiredItemLevel = model.RequiredItemLevel,
            RequiredInfluences = model.RequiredInfluences?.ToList(),
            ValidItemClasses = model.ValidItemClasses?.ToList(),
            DisplayName = model.DisplayName,
            DisplayOrder = model.DisplayOrder,
            IsDeprecated = model.IsDeprecated,
            DeprecatedAt = model.DeprecatedAt,
            DeprecationReason = model.DeprecationReason,
            CreatedAt = model.CreatedAt,
            UpdatedAt = model.UpdatedAt
        };

    private static AffixDefinitionUpdatedEvent MapDefinitionToUpdatedEvent(AffixDefinitionModel model, List<string> changedFields)
        => new()
        {
            DefinitionId = model.DefinitionId,
            GameServiceId = model.GameServiceId,
            Code = model.Code,
            SlotType = model.SlotType,
            ModGroup = model.ModGroup,
            Tier = model.Tier,
            Category = model.Category,
            Tags = model.Tags?.ToList(),
            StatGrants = model.StatGrants.ToList(),
            SpawnWeight = model.SpawnWeight,
            SpawnTagModifiers = model.SpawnTagModifiers?.ToList(),
            RequiredItemLevel = model.RequiredItemLevel,
            RequiredInfluences = model.RequiredInfluences?.ToList(),
            ValidItemClasses = model.ValidItemClasses?.ToList(),
            DisplayName = model.DisplayName,
            DisplayOrder = model.DisplayOrder,
            CreatedAt = model.CreatedAt,
            UpdatedAt = model.UpdatedAt ?? model.CreatedAt,
            IsDeprecated = model.IsDeprecated,
            DeprecatedAt = model.DeprecatedAt,
            DeprecationReason = model.DeprecationReason,
            ChangedFields = changedFields
        };

    private static ImplicitMappingResponse MapImplicitToResponse(ImplicitMappingModel model)
        => new()
        {
            MappingId = model.MappingId,
            GameServiceId = model.GameServiceId,
            ItemTemplateCode = model.ItemTemplateCode,
            ImplicitDefinitionIds = model.ImplicitDefinitionIds.ToList()
        };

    private static AffixInstanceResponse MapInstanceToResponse(AffixInstanceModel model)
        => new()
        {
            ItemInstanceId = model.ItemInstanceId,
            GameServiceId = model.GameServiceId,
            EffectiveRarity = model.EffectiveRarity,
            ItemLevel = model.ItemLevel,
            ImplicitSlots = model.ImplicitSlots.Select(MapSlotModelToData).ToList(),
            PrefixSlots = model.PrefixSlots.Select(MapSlotModelToData).ToList(),
            SuffixSlots = model.SuffixSlots.Select(MapSlotModelToData).ToList(),
            EnchantSlots = model.EnchantSlots.Select(MapSlotModelToData).ToList(),
            Influences = model.Influences.ToList(),
            States = MapStatesToResponse(model.States),
            Quality = model.Quality
        };

    private static AffixSlotData MapSlotModelToData(AffixSlotModel model)
        => new()
        {
            DefinitionId = model.DefinitionId,
            DefinitionCode = model.DefinitionCode,
            ModGroup = model.ModGroup,
            RolledValues = model.RolledValues.ToList(),
            IsFractured = model.IsFractured
        };

    private static AffixSlotModel MapSlotDataToModel(AffixSlotData data)
        => new()
        {
            DefinitionId = data.DefinitionId,
            DefinitionCode = data.DefinitionCode,
            ModGroup = data.ModGroup,
            RolledValues = data.RolledValues.ToArray(),
            IsFractured = data.IsFractured
        };

    private static AffixStates MapStatesToResponse(AffixStatesModel model)
        => new()
        {
            IsCorrupted = model.IsCorrupted,
            IsMirrored = model.IsMirrored,
            IsSplit = model.IsSplit,
            IsIdentified = model.IsIdentified,
            IsSynthesized = model.IsSynthesized
        };

    private static AffixInstanceBatchModifiedEntry MapInstanceToModifiedEntry(AffixInstanceModel model, string[] changedFields)
        => new()
        {
            ItemInstanceId = model.ItemInstanceId,
            GameServiceId = model.GameServiceId,
            EffectiveRarity = model.EffectiveRarity,
            ItemLevel = model.ItemLevel,
            Quality = model.Quality,
            CreatedAt = model.CreatedAt,
            UpdatedAt = model.UpdatedAt ?? model.CreatedAt,
            ChangedFields = changedFields.ToList()
        };

    #endregion
}
