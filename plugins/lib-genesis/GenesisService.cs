using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Actor;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Character;
using BeyondImmersion.BannouService.Currency;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.GameService;
using BeyondImmersion.BannouService.Helpers;
using BeyondImmersion.BannouService.Inventory;
using BeyondImmersion.BannouService.Item;
using BeyondImmersion.BannouService.Realm;
using BeyondImmersion.BannouService.Relationship;
using BeyondImmersion.BannouService.Resource;
using BeyondImmersion.BannouService.Seed;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.Species;
using BeyondImmersion.BannouService.State;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Genesis;

/// <summary>
/// Template-driven entity awakening lifecycle service.
/// Manages entities that progressively grow from inert objects into autonomous agents
/// with personalities, memories, and the full cognitive stack.
/// </summary>
[BannouService("genesis", typeof(IGenesisService), lifetime: ServiceLifetime.Scoped, layer: ServiceLayer.GameFoundation)]
public partial class GenesisService : IGenesisService, ICleanDeprecatedEntity
{
    // Primary data stores (one model type per store — clean for IQueryableStateStore)
    private readonly IStateStore<GenesisTemplateModel> _templateStore;
    private readonly IQueryableStateStore<GenesisTemplateModel> _templateQueryStore;
    private readonly IStateStore<GenesisEntityModel> _entityStore;
    private readonly IQueryableStateStore<GenesisEntityModel> _entityQueryStore;

    // Index stores (separate from primary data to prevent QueryAsync contamination)
    private readonly IStateStore<string> _entityIndexStore;
    private readonly IStateStore<GenesisEntityListModel> _entityListIndexStore;
    private readonly IStateStore<GenesisTemplateListModel> _templateListIndexStore;

    // Cache stores
    private readonly IStateStore<CachedGenesisEntity> _entityCacheStore;
    private readonly IStateStore<CachedCapabilityManifest> _capsCacheStore;

    // Infrastructure
    private readonly IDistributedLockProvider _lockProvider;
    private readonly IMessageBus _messageBus;
    private readonly ILogger<GenesisService> _logger;
    private readonly GenesisServiceConfiguration _configuration;
    private readonly ITelemetryProvider _telemetryProvider;

    // Service clients (all L0/L1/L2 — hard dependencies per SERVICE-HIERARCHY)
    private readonly IResourceClient _resourceClient;
    private readonly ISeedClient _seedClient;
    private readonly ICurrencyClient _currencyClient;
    private readonly ICharacterClient _characterClient;
    private readonly IActorClient _actorClient;
    private readonly IInventoryClient _inventoryClient;
    private readonly IItemClient _itemClient;
    private readonly IRelationshipClient _relationshipClient;
    private readonly IRealmClient _realmClient;
    private readonly ISpeciesClient _speciesClient;
    private readonly IGameServiceClient _gameServiceClient;

    // Shared runtime pipeline state
    private readonly GenesisGrowthState _growthState;

    /// <summary>
    /// Initializes a new instance of <see cref="GenesisService"/>.
    /// </summary>
    public GenesisService(
        IStateStoreFactory stateStoreFactory,
        IDistributedLockProvider lockProvider,
        IMessageBus messageBus,
        ILogger<GenesisService> logger,
        GenesisServiceConfiguration configuration,
        ITelemetryProvider telemetryProvider,
        IResourceClient resourceClient,
        ISeedClient seedClient,
        ICurrencyClient currencyClient,
        ICharacterClient characterClient,
        IActorClient actorClient,
        IInventoryClient inventoryClient,
        IItemClient itemClient,
        IRelationshipClient relationshipClient,
        IRealmClient realmClient,
        ISpeciesClient speciesClient,
        IGameServiceClient gameServiceClient,
        IEventConsumer eventConsumer,
        GenesisGrowthState growthState)
    {
        // Primary data stores
        _templateStore = stateStoreFactory.GetStore<GenesisTemplateModel>(StateStoreDefinitions.GenesisTemplates);
        _templateQueryStore = stateStoreFactory.GetQueryableStore<GenesisTemplateModel>(StateStoreDefinitions.GenesisTemplates);
        _entityStore = stateStoreFactory.GetStore<GenesisEntityModel>(StateStoreDefinitions.GenesisEntities);
        _entityQueryStore = stateStoreFactory.GetQueryableStore<GenesisEntityModel>(StateStoreDefinitions.GenesisEntities);

        // Index stores (separate tables — prevents QueryAsync from scanning index rows)
        _entityIndexStore = stateStoreFactory.GetStore<string>(StateStoreDefinitions.GenesisEntityIndexes);
        _entityListIndexStore = stateStoreFactory.GetStore<GenesisEntityListModel>(StateStoreDefinitions.GenesisEntityIndexes);
        _templateListIndexStore = stateStoreFactory.GetStore<GenesisTemplateListModel>(StateStoreDefinitions.GenesisTemplateIndexes);

        // Cache stores
        _entityCacheStore = stateStoreFactory.GetStore<CachedGenesisEntity>(StateStoreDefinitions.GenesisEntityCache);
        _capsCacheStore = stateStoreFactory.GetStore<CachedCapabilityManifest>(StateStoreDefinitions.GenesisEntityCache);

        _lockProvider = lockProvider;
        _messageBus = messageBus;
        _logger = logger;
        _configuration = configuration;
        _telemetryProvider = telemetryProvider;
        _resourceClient = resourceClient;
        _seedClient = seedClient;
        _currencyClient = currencyClient;
        _characterClient = characterClient;
        _actorClient = actorClient;
        _inventoryClient = inventoryClient;
        _itemClient = itemClient;
        _relationshipClient = relationshipClient;
        _realmClient = realmClient;
        _speciesClient = speciesClient;
        _gameServiceClient = gameServiceClient;
        _growthState = growthState;

        RegisterEventConsumers(eventConsumer);
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, GenesisTemplateResponse?)> RegisterTemplateAsync(
        RegisterTemplateRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Registering template {TemplateCode} for game {GameServiceId}",
            body.TemplateCode, body.GameServiceId);

        var validationError = ValidateTemplateStructure(body);
        if (validationError != null)
        {
            _logger.LogDebug("Template structure validation failed: {Error}", validationError);
            return (StatusCodes.BadRequest, null);
        }

        // Validate system realm
        try
        {
            var realm = await _realmClient.GetRealmByCodeAsync(
                new GetRealmByCodeRequest { Code = body.Awakening.SystemRealmCode }, cancellationToken);
            if (!realm.IsSystemType)
            {
                _logger.LogDebug("Realm {RealmCode} is not a system realm", body.Awakening.SystemRealmCode);
                return (StatusCodes.BadRequest, null);
            }
        }
        catch (ApiException)
        {
            _logger.LogDebug("System realm {RealmCode} not found", body.Awakening.SystemRealmCode);
            return (StatusCodes.BadRequest, null);
        }

        // Validate species
        try
        {
            await _speciesClient.GetSpeciesByCodeAsync(
                new GetSpeciesByCodeRequest { Code = body.Awakening.CharacterSpeciesCode }, cancellationToken);
        }
        catch (ApiException)
        {
            _logger.LogDebug("Species {SpeciesCode} not found", body.Awakening.CharacterSpeciesCode);
            return (StatusCodes.BadRequest, null);
        }

        // Idempotent check
        var existing = await _templateStore.GetAsync(BuildTemplateKey(body.TemplateCode), cancellationToken);
        if (existing != null)
            return (StatusCodes.OK, MapTemplateToResponse(existing));

        // Register seed type
        try
        {
            await _seedClient.RegisterSeedTypeAsync(
                new RegisterSeedTypeRequest
                {
                    SeedTypeCode = body.Seed.SeedTypeCode,
                    GameServiceId = body.GameServiceId,
                    DisplayName = body.DisplayName,
                    Description = body.Description,
                    MaxPerOwner = 1,
                    AllowedOwnerTypes = new List<EntityType> { EntityType.Other },
                    GrowthPhases = body.Seed.Phases.Select(p => new GrowthPhaseDefinition
                    {
                        PhaseCode = p.PhaseName,
                        DisplayName = p.PhaseName,
                        MinTotalGrowth = (float)p.Threshold,
                    }).ToList(),
                    BondCardinality = 0,
                    BondPermanent = false,
                    CapabilityRules = body.Seed.CapabilityRules?.Select(c => new CapabilityRule
                    {
                        CapabilityCode = c.CapabilityCode,
                        Domain = c.Domain,
                        UnlockThreshold = (float)c.Threshold,
                        FidelityFormula = "linear",
                    }).ToList(),
                }, cancellationToken);
        }
        catch (ApiException ex)
        {
            _logger.LogWarning(ex, "Failed to register seed type {SeedTypeCode}", body.Seed.SeedTypeCode);
            return (StatusCodes.BadRequest, null);
        }

        // Register actor templates for each phase that requires an actor (EventBrain/CharacterBrain with
        // a behaviorRef). Failure here is logged and skipped: phase transitions will publish
        // transition-failed events when they can't find the actor template in the state map. This keeps
        // RegisterTemplate resilient against transient Actor service unavailability.
        await EnsureActorTemplatesForRegistrationAsync(body, cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var template = new GenesisTemplateModel
        {
            TemplateCode = body.TemplateCode,
            GameServiceId = body.GameServiceId,
            DisplayName = body.DisplayName,
            Description = body.Description,
            Seed = body.Seed,
            Economy = body.Economy,
            Storage = body.Storage,
            Awakening = body.Awakening,
            PhysicalFormType = body.PhysicalFormType,
            Bond = body.Bond,
            ArchiveOnDestruction = body.ArchiveOnDestruction,
            CreatedAt = now,
            UpdatedAt = now
        };

        var lockOwner = $"register-template-{Guid.NewGuid():N}";
        await using var lockHandle = await _lockProvider.LockAsync(
            StateStoreDefinitions.GenesisLock, $"entity:{body.TemplateCode}",
            lockOwner, _configuration.EntityLockTimeoutSeconds, cancellationToken: cancellationToken);
        if (!lockHandle.Success) return (StatusCodes.Conflict, null);

        await _templateStore.SaveAsync(BuildTemplateKey(body.TemplateCode), template, cancellationToken: cancellationToken);
        await AddToTemplateGameIndexAsync(body.GameServiceId, body.TemplateCode, cancellationToken);

        await _messageBus.PublishTemplateCreatedAsync(new TemplateCreatedEvent
        {
            TemplateCode = template.TemplateCode,
            GameServiceId = template.GameServiceId,
            DisplayName = template.DisplayName,
            Description = template.Description,
            PhysicalFormType = template.PhysicalFormType,
            CreatedAt = template.CreatedAt,
            UpdatedAt = template.UpdatedAt,
            IsDeprecated = false,
        }, cancellationToken);

        _logger.LogInformation("Registered template {TemplateCode} for game {GameServiceId}",
            body.TemplateCode, body.GameServiceId);

        return (StatusCodes.OK, MapTemplateToResponse(template));
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, GenesisTemplateResponse?)> GetTemplateAsync(
        GetTemplateRequest body, CancellationToken cancellationToken)
    {
        var template = await _templateStore.GetAsync(BuildTemplateKey(body.TemplateCode), cancellationToken);
        if (template == null) return (StatusCodes.NotFound, null);
        return (StatusCodes.OK, MapTemplateToResponse(template));
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, ListTemplatesResponse?)> ListTemplatesAsync(
        ListTemplatesRequest body, CancellationToken cancellationToken)
    {
        var pageSize = body.PageSize ?? _configuration.DefaultPageSize;
        var index = await _templateListIndexStore.GetAsync(BuildTemplateGameKey(body.GameServiceId), cancellationToken);
        if (index == null || index.TemplateCodes.Count == 0)
        {
            return (StatusCodes.OK, new ListTemplatesResponse
            {
                Templates = new List<GenesisTemplateResponse>(),
                TotalCount = 0,
                Page = body.Page,
                PageSize = pageSize
            });
        }

        var templates = new List<GenesisTemplateModel>();
        foreach (var code in index.TemplateCodes)
        {
            var template = await _templateStore.GetAsync(BuildTemplateKey(code), cancellationToken);
            if (template != null)
            {
                if (!body.IncludeDeprecated && template.IsDeprecated) continue;
                templates.Add(template);
            }
        }

        var totalCount = templates.Count;
        var paged = templates
            .Skip((body.Page - 1) * pageSize)
            .Take(pageSize)
            .Select(MapTemplateToResponse)
            .ToList();

        return (StatusCodes.OK, new ListTemplatesResponse
        {
            Templates = paged,
            TotalCount = totalCount,
            Page = body.Page,
            PageSize = pageSize
        });
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, GenesisTemplateResponse?)> UpdateTemplateAsync(
        UpdateTemplateRequest body, CancellationToken cancellationToken)
    {
        var template = await _templateStore.GetAsync(BuildTemplateKey(body.TemplateCode), cancellationToken);
        if (template == null) return (StatusCodes.NotFound, null);

        var changedFields = new List<string>();

        if (body.Awakening != null)
        {
            try
            {
                var realm = await _realmClient.GetRealmByCodeAsync(
                    new GetRealmByCodeRequest { Code = body.Awakening.SystemRealmCode }, cancellationToken);
                if (!realm.IsSystemType) return (StatusCodes.BadRequest, null);
            }
            catch (ApiException) { return (StatusCodes.BadRequest, null); }

            try
            {
                await _speciesClient.GetSpeciesByCodeAsync(
                    new GetSpeciesByCodeRequest { Code = body.Awakening.CharacterSpeciesCode }, cancellationToken);
            }
            catch (ApiException) { return (StatusCodes.BadRequest, null); }

            template.Awakening = body.Awakening;
            changedFields.Add("Awakening");
        }

        if (body.DisplayName != null) { template.DisplayName = body.DisplayName; changedFields.Add("DisplayName"); }
        if (body.Description != null) { template.Description = body.Description; changedFields.Add("Description"); }
        if (body.Seed != null) { template.Seed = body.Seed; changedFields.Add("Seed"); }
        if (body.Economy != null) { template.Economy = body.Economy; changedFields.Add("Economy"); }
        if (body.Storage != null) { template.Storage = body.Storage; changedFields.Add("Storage"); }
        if (body.Bond != null) { template.Bond = body.Bond; changedFields.Add("Bond"); }

        var lockOwner = $"update-template-{Guid.NewGuid():N}";
        await using var lockHandle = await _lockProvider.LockAsync(
            StateStoreDefinitions.GenesisLock, $"entity:{body.TemplateCode}",
            lockOwner, _configuration.EntityLockTimeoutSeconds, cancellationToken: cancellationToken);
        if (!lockHandle.Success) return (StatusCodes.Conflict, null);

        template.UpdatedAt = DateTimeOffset.UtcNow;
        await _templateStore.SaveAsync(BuildTemplateKey(body.TemplateCode), template, cancellationToken: cancellationToken);

        await _messageBus.PublishTemplateUpdatedAsync(new TemplateUpdatedEvent
        {
            TemplateCode = template.TemplateCode,
            GameServiceId = template.GameServiceId,
            DisplayName = template.DisplayName,
            Description = template.Description,
            PhysicalFormType = template.PhysicalFormType,
            CreatedAt = template.CreatedAt,
            UpdatedAt = template.UpdatedAt,
            IsDeprecated = template.IsDeprecated,
            DeprecatedAt = template.DeprecatedAt,
            DeprecationReason = template.DeprecationReason,
            ChangedFields = changedFields,
        }, cancellationToken);

        return (StatusCodes.OK, MapTemplateToResponse(template));
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, GenesisTemplateResponse?)> DeprecateTemplateAsync(
        DeprecateTemplateRequest body, CancellationToken cancellationToken)
    {
        var template = await _templateStore.GetAsync(BuildTemplateKey(body.TemplateCode), cancellationToken);
        if (template == null) return (StatusCodes.NotFound, null);

        if (template.IsDeprecated)
            return (StatusCodes.OK, MapTemplateToResponse(template));

        template.IsDeprecated = true;
        template.DeprecatedAt = DateTimeOffset.UtcNow;
        template.DeprecationReason = body.Reason;
        template.UpdatedAt = DateTimeOffset.UtcNow;

        await _templateStore.SaveAsync(BuildTemplateKey(body.TemplateCode), template, cancellationToken: cancellationToken);

        await _messageBus.PublishTemplateUpdatedAsync(new TemplateUpdatedEvent
        {
            TemplateCode = template.TemplateCode,
            GameServiceId = template.GameServiceId,
            DisplayName = template.DisplayName,
            Description = template.Description,
            PhysicalFormType = template.PhysicalFormType,
            CreatedAt = template.CreatedAt,
            UpdatedAt = template.UpdatedAt,
            IsDeprecated = template.IsDeprecated,
            DeprecatedAt = template.DeprecatedAt,
            DeprecationReason = template.DeprecationReason,
            ChangedFields = new List<string> { "IsDeprecated", "DeprecatedAt", "DeprecationReason" },
        }, cancellationToken);

        _logger.LogInformation("Deprecated template {TemplateCode}", body.TemplateCode);
        return (StatusCodes.OK, MapTemplateToResponse(template));
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, CleanDeprecatedStringKeyResponse?)> CleanDeprecatedAsync(
        CleanDeprecatedRequest body, CancellationToken cancellationToken)
    {
        // QueryAsync on template store is now clean — no index rows contaminate the scan
        var deprecatedTemplates = await _templateQueryStore.QueryAsync(
            t => t.IsDeprecated, cancellationToken);

        var result = await DeprecationCleanupHelper.ExecuteCleanupSweepAsync(
            deprecatedTemplates,
            getEntityId: t => t.TemplateCode,
            getDeprecatedAt: t => t.DeprecatedAt,
            hasInstancesAsync: (t, ct) =>
                _entityIndexStore.HasStringListEntriesAsync(
                    BuildEntityTemplateInstancesKey(t.TemplateCode), ct),
            deleteAndPublishAsync: async (t, ct) =>
            {
                await _templateStore.DeleteAsync(BuildTemplateKey(t.TemplateCode), ct);
                await RemoveFromTemplateGameIndexAsync(t.GameServiceId, t.TemplateCode, ct);
                await _messageBus.PublishTemplateDeletedAsync(new TemplateDeletedEvent
                {
                    EventId = Guid.NewGuid(),
                    Timestamp = DateTimeOffset.UtcNow,
                    TemplateCode = t.TemplateCode,
                    GameServiceId = t.GameServiceId,
                    DisplayName = t.DisplayName,
                    Description = t.Description,
                    PhysicalFormType = t.PhysicalFormType,
                    CreatedAt = t.CreatedAt,
                    UpdatedAt = t.UpdatedAt,
                    IsDeprecated = t.IsDeprecated,
                    DeprecatedAt = t.DeprecatedAt,
                    DeprecationReason = t.DeprecationReason,
                    DeletedReason = "Removed by clean-deprecated sweep",
                }, ct);
            },
            gracePeriodDays: body.GracePeriodDays,
            dryRun: body.DryRun,
            _logger,
            _telemetryProvider,
            cancellationToken);

        return (StatusCodes.OK, new CleanDeprecatedStringKeyResponse
        {
            Cleaned = result.Cleaned,
            Remaining = result.Remaining,
            Errors = result.Errors,
            CleanedIds = result.CleanedIds.ToList(),
        });
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, GenesisEntityResponse?)> CreateEntityAsync(
        CreateEntityRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Creating entity from template {TemplateCode} in realm {RealmId}",
            body.TemplateCode, body.RealmId);

        var template = await _templateStore.GetAsync(BuildTemplateKey(body.TemplateCode), cancellationToken);
        if (template == null) return (StatusCodes.NotFound, null);
        if (template.IsDeprecated) return (StatusCodes.BadRequest, null);

        // Validate game service
        try { await _gameServiceClient.GetServiceAsync(new GetServiceRequest { ServiceId = body.GameServiceId }, cancellationToken); }
        catch (ApiException) { return (StatusCodes.BadRequest, null); }

        // Code uniqueness check (index store — lightweight string lookup)
        if (body.Code != null)
        {
            var existingId = await _entityIndexStore.GetAsync(
                BuildEntityCodeKey(body.GameServiceId, body.RealmId, body.Code), cancellationToken);
            if (existingId != null) return (StatusCodes.Conflict, null);
        }

        // Resolve currency definition IDs from template wallet codes — validate they exist
        var currencyDefIds = new Dictionary<string, Guid>();
        foreach (var wallet in template.Economy.Wallets)
        {
            try
            {
                var currencyDef = await _currencyClient.GetCurrencyDefinitionAsync(
                    new GetCurrencyDefinitionRequest { Code = wallet.CurrencyCode }, cancellationToken);
                currencyDefIds[wallet.WalletCode] = currencyDef.DefinitionId;
            }
            catch (ApiException)
            {
                _logger.LogDebug("Currency definition {Code} not found for wallet {WalletCode}",
                    wallet.CurrencyCode, wallet.WalletCode);
                return (StatusCodes.BadRequest, null);
            }
        }

        var entityId = Guid.NewGuid();

        var lockOwner = $"create-entity-{Guid.NewGuid():N}";
        await using var lockHandle = await _lockProvider.LockAsync(
            StateStoreDefinitions.GenesisLock, $"entity:{entityId}",
            lockOwner, _configuration.EntityLockTimeoutSeconds, cancellationToken: cancellationToken);
        if (!lockHandle.Success) return (StatusCodes.Conflict, null);

        // Multi-service provisioning with direct catch-block compensation per IMPLEMENTATION TENETS
        var provisionedWalletIds = new Dictionary<string, Guid>();
        var provisionedInventoryIds = new Dictionary<string, Guid>();
        Guid provisionedSeedId;

        try
        {
            // Provision seed
            var seedResponse = await _seedClient.CreateSeedAsync(
                new CreateSeedRequest
                {
                    OwnerType = EntityType.Other,
                    OwnerId = entityId,
                    SeedTypeCode = template.Seed.SeedTypeCode,
                    GameServiceId = body.GameServiceId,
                }, cancellationToken);
            provisionedSeedId = seedResponse.SeedId;

            // Provision wallets
            foreach (var wallet in template.Economy.Wallets)
            {
                var wResponse = await _currencyClient.CreateWalletAsync(
                    new CreateWalletRequest
                    {
                        OwnerId = entityId,
                        OwnerType = EntityType.Other,
                        RealmId = body.RealmId,
                    }, cancellationToken);
                provisionedWalletIds[wallet.WalletCode] = wResponse.WalletId;
            }

            // Provision inventories
            foreach (var inv in template.Storage.Inventories)
            {
                var iResponse = await _inventoryClient.CreateContainerAsync(
                    new CreateContainerRequest
                    {
                        OwnerId = entityId,
                        OwnerType = ContainerOwnerType.Other,
                        ContainerType = inv.InventoryCode,
                        ConstraintModel = inv.ConstraintModel.MapByName<ContainerConstraintModel>(),
                        MaxSlots = inv.Capacity,
                    }, cancellationToken);
                provisionedInventoryIds[inv.InventoryCode] = iResponse.ContainerId;
            }
        }
        catch (Exception)
        {
            // Direct compensation: undo whatever was successfully provisioned
            await CompensateProvisioningAsync(provisionedWalletIds, provisionedInventoryIds, cancellationToken);
            throw;
        }

        var now = DateTimeOffset.UtcNow;
        var entity = new GenesisEntityModel
        {
            EntityId = entityId,
            TemplateCode = body.TemplateCode,
            GameServiceId = body.GameServiceId,
            RealmId = body.RealmId,
            Code = body.Code,
            DisplayName = body.DisplayName,
            SeedId = provisionedSeedId,
            WalletIds = provisionedWalletIds,
            InventoryIds = provisionedInventoryIds,
            CurrentPhase = template.Seed.Phases.First().PhaseName,
            CognitiveStage = CognitiveStage.Dormant,
            PhysicalFormType = template.PhysicalFormType,
            Status = GenesisEntityStatus.Active,
            CreatedAt = now,
            UpdatedAt = now,
        };

        // Save entity record (primary store only)
        await _entityStore.SaveAsync(BuildEntityKey(entityId), entity, cancellationToken: cancellationToken);

        // Save lightweight indexes (separate store)
        if (body.Code != null)
            await _entityIndexStore.SaveAsync(
                BuildEntityCodeKey(body.GameServiceId, body.RealmId, body.Code),
                entityId.ToString(), cancellationToken: cancellationToken);
        await AddToEntityTemplateIndexAsync(body.TemplateCode, body.RealmId, entityId, cancellationToken);
        foreach (var walletId in entity.WalletIds.Values)
            await _entityIndexStore.SaveAsync(
                BuildEntityWalletKey(walletId), entityId.ToString(), cancellationToken: cancellationToken);

        // Maintain template→entity reverse index for clean-deprecated instance checks
        await _entityIndexStore.AddToStringListAsync(
            BuildEntityTemplateInstancesKey(body.TemplateCode),
            entityId.ToString(),
            _configuration.ListOperationMaxRetries,
            _logger,
            cancellationToken);

        // Populate the in-memory wallet map on this node immediately so credits/debits for
        // the new entity's wallets are matched by GenesisCurrencyTransactionListener without
        // waiting for the genesis.entity.created event round-trip. Other nodes learn about
        // the new entity via the broadcast event handler below.
        PopulateWalletMap(entity, template);

        await _messageBus.PublishEntityCreatedAsync(new EntityCreatedEvent
        {
            EntityId = entityId,
            TemplateCode = body.TemplateCode,
            GameServiceId = body.GameServiceId,
            RealmId = body.RealmId,
            Code = body.Code,
            DisplayName = body.DisplayName,
            WalletIds = entity.WalletIds,
            InventoryIds = entity.InventoryIds,
            CurrentPhase = entity.CurrentPhase,
            CognitiveStage = entity.CognitiveStage,
            PhysicalFormType = entity.PhysicalFormType,
            Status = entity.Status,
            CreatedAt = entity.CreatedAt,
            UpdatedAt = entity.UpdatedAt,
        }, cancellationToken);

        _logger.LogInformation("Created entity {EntityId} from template {TemplateCode}", entityId, body.TemplateCode);
        return (StatusCodes.OK, MapEntityToResponse(entity, null));
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, GenesisEntityResponse?)> GetEntityAsync(
        GetEntityRequest body, CancellationToken cancellationToken)
    {
        var cached = await _entityCacheStore.GetAsync(BuildEntityCacheKey(body.EntityId), cancellationToken);
        GenesisEntityModel? entity;

        if (cached != null)
        {
            entity = MapCachedToEntity(cached);
        }
        else
        {
            entity = await _entityStore.GetAsync(BuildEntityKey(body.EntityId), cancellationToken);
            if (entity == null) return (StatusCodes.NotFound, null);

            await _entityCacheStore.SaveAsync(
                BuildEntityCacheKey(body.EntityId), MapEntityToCache(entity),
                new StateOptions { Ttl = _configuration.EntityCacheTtlMinutes * 60 },
                cancellationToken);
        }

        Dictionary<string, double>? walletBalances = null;
        var includeBalances = body.IncludeBalances ?? _configuration.IncludeBalancesDefault;
        if (includeBalances)
            walletBalances = await FetchWalletBalancesAsync(entity, cancellationToken);

        return (StatusCodes.OK, MapEntityToResponse(entity, walletBalances));
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, ListEntitiesResponse?)> ListEntitiesAsync(
        ListEntitiesRequest body, CancellationToken cancellationToken)
    {
        var pageSize = body.PageSize ?? _configuration.DefaultPageSize;
        var index = await _entityListIndexStore.GetAsync(
            BuildEntityTemplateKey(body.TemplateCode, body.RealmId), cancellationToken);

        if (index == null || index.EntityIds.Count == 0)
        {
            return (StatusCodes.OK, new ListEntitiesResponse
            {
                Entities = new List<GenesisEntityResponse>(),
                TotalCount = 0,
                Page = body.Page,
                PageSize = pageSize
            });
        }

        var entities = new List<GenesisEntityModel>();
        foreach (var entityId in index.EntityIds)
        {
            var entity = await _entityStore.GetAsync(BuildEntityKey(entityId), cancellationToken);
            if (entity == null) continue;
            if (body.CognitiveStage.HasValue && entity.CognitiveStage != body.CognitiveStage.Value) continue;
            if (body.Status.HasValue && entity.Status != body.Status.Value) continue;
            if (body.CurrentPhase != null && entity.CurrentPhase != body.CurrentPhase) continue;
            entities.Add(entity);
        }

        var totalCount = entities.Count;
        var paged = entities
            .Skip((body.Page - 1) * pageSize)
            .Take(pageSize)
            .Select(e => MapEntityToResponse(e, null))
            .ToList();

        return (StatusCodes.OK, new ListEntitiesResponse
        {
            Entities = paged,
            TotalCount = totalCount,
            Page = body.Page,
            PageSize = pageSize
        });
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, GetCapabilitiesResponse?)> GetCapabilitiesAsync(
        GetCapabilitiesRequest body, CancellationToken cancellationToken)
    {
        var entity = await _entityStore.GetAsync(BuildEntityKey(body.EntityId), cancellationToken);
        if (entity == null) return (StatusCodes.NotFound, null);

        var cached = await _capsCacheStore.GetAsync(BuildCapsCacheKey(body.EntityId), cancellationToken);
        if (cached != null)
        {
            return (StatusCodes.OK, new GetCapabilitiesResponse
            {
                EntityId = body.EntityId,
                Capabilities = cached.Capabilities,
                Version = cached.Version,
            });
        }

        List<GenesisCapability> capabilities;
        int version;
        try
        {
            var capResponse = await _seedClient.GetCapabilityManifestAsync(
                new GetCapabilityManifestRequest { SeedId = entity.SeedId }, cancellationToken);
            capabilities = capResponse.Capabilities.Select(c => new GenesisCapability
            {
                CapabilityCode = c.CapabilityCode,
                IsUnlocked = c.Unlocked,
            }).ToList();
            version = capResponse.Version;
        }
        catch (ApiException)
        {
            capabilities = new List<GenesisCapability>();
            version = 0;
        }

        await _capsCacheStore.SaveAsync(BuildCapsCacheKey(body.EntityId), new CachedCapabilityManifest
        {
            EntityId = body.EntityId,
            Capabilities = capabilities,
            Version = version,
        }, new StateOptions { Ttl = _configuration.CapabilityCacheTtlMinutes * 60 }, cancellationToken);

        return (StatusCodes.OK, new GetCapabilitiesResponse
        {
            EntityId = body.EntityId,
            Capabilities = capabilities,
            Version = version,
        });
    }

    /// <inheritdoc/>
    public async Task<StatusCodes> DestroyEntityAsync(
        DestroyEntityRequest body, CancellationToken cancellationToken)
    {
        var entity = await _entityStore.GetAsync(BuildEntityKey(body.EntityId), cancellationToken);
        if (entity == null) return StatusCodes.NotFound;

        var template = await _templateStore.GetAsync(BuildTemplateKey(entity.TemplateCode), cancellationToken);

        var lockOwner = $"destroy-entity-{Guid.NewGuid():N}";
        await using var lockHandle = await _lockProvider.LockAsync(
            StateStoreDefinitions.GenesisLock, $"entity:{body.EntityId}",
            lockOwner, _configuration.EntityLockTimeoutSeconds, cancellationToken: cancellationToken);
        if (!lockHandle.Success) return StatusCodes.Conflict;

        await DestroyEntityCoreAsync(entity, template, cancellationToken);
        _logger.LogInformation("Destroyed entity {EntityId}", body.EntityId);
        return StatusCodes.OK;
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, GenesisEntityResponse?)> BindPhysicalFormAsync(
        BindPhysicalFormRequest body, CancellationToken cancellationToken)
    {
        var entity = await _entityStore.GetAsync(BuildEntityKey(body.EntityId), cancellationToken);
        if (entity == null) return (StatusCodes.NotFound, null);

        var template = await _templateStore.GetAsync(BuildTemplateKey(entity.TemplateCode), cancellationToken);
        if (template != null && body.PhysicalFormType != template.PhysicalFormType)
            return (StatusCodes.BadRequest, null);

        if (body.PhysicalFormType == PhysicalFormType.Item)
        {
            try { await _itemClient.GetItemInstanceAsync(new GetItemInstanceRequest { InstanceId = body.PhysicalFormId }, cancellationToken); }
            catch (ApiException) { return (StatusCodes.BadRequest, null); }
        }

        var lockOwner = $"bind-form-{Guid.NewGuid():N}";
        await using var lockHandle = await _lockProvider.LockAsync(
            StateStoreDefinitions.GenesisLock, $"entity:{body.EntityId}",
            lockOwner, _configuration.EntityLockTimeoutSeconds, cancellationToken: cancellationToken);
        if (!lockHandle.Success) return (StatusCodes.Conflict, null);

        entity.PhysicalFormType = body.PhysicalFormType;
        entity.PhysicalFormId = body.PhysicalFormId;
        entity.UpdatedAt = DateTimeOffset.UtcNow;

        await _entityStore.SaveAsync(BuildEntityKey(body.EntityId), entity, cancellationToken: cancellationToken);
        await _entityCacheStore.DeleteAsync(BuildEntityCacheKey(body.EntityId), cancellationToken);

        await _messageBus.PublishEntityUpdatedAsync(new EntityUpdatedEvent
        {
            EntityId = entity.EntityId,
            TemplateCode = entity.TemplateCode,
            GameServiceId = entity.GameServiceId,
            RealmId = entity.RealmId,
            Code = entity.Code,
            DisplayName = entity.DisplayName,
            WalletIds = entity.WalletIds,
            InventoryIds = entity.InventoryIds,
            CurrentPhase = entity.CurrentPhase,
            CognitiveStage = entity.CognitiveStage,
            ActorId = entity.ActorId,
            CharacterId = entity.CharacterId,
            PhysicalFormType = entity.PhysicalFormType,
            PhysicalFormId = entity.PhysicalFormId,
            Status = entity.Status,
            CreatedAt = entity.CreatedAt,
            UpdatedAt = entity.UpdatedAt,
            ChangedFields = new List<string> { "PhysicalFormType", "PhysicalFormId" },
        }, cancellationToken);

        return (StatusCodes.OK, MapEntityToResponse(entity, null));
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, GenesisEntityResponse?)> CreateBondAsync(
        CreateBondRequest body, CancellationToken cancellationToken)
    {
        var entity = await _entityStore.GetAsync(BuildEntityKey(body.EntityId), cancellationToken);
        if (entity == null) return (StatusCodes.NotFound, null);

        var template = await _templateStore.GetAsync(BuildTemplateKey(entity.TemplateCode), cancellationToken);
        if (template == null) return (StatusCodes.NotFound, null);

        if (!template.Bond.Enabled) return (StatusCodes.BadRequest, null);
        if (template.Bond.Cardinality == BondCardinality.None) return (StatusCodes.BadRequest, null);
        if ((template.Bond.Cardinality == BondCardinality.OptionalOne ||
            template.Bond.Cardinality == BondCardinality.RequiredOne) &&
            entity.BondTargetEntityId != null)
            return (StatusCodes.Conflict, null);

        var lockOwner = $"create-bond-{Guid.NewGuid():N}";
        await using var lockHandle = await _lockProvider.LockAsync(
            StateStoreDefinitions.GenesisLock, $"bond:{body.EntityId}",
            lockOwner, _configuration.EntityLockTimeoutSeconds, cancellationToken: cancellationToken);
        if (!lockHandle.Success) return (StatusCodes.Conflict, null);

        entity.BondTargetEntityType = body.TargetEntityType;
        entity.BondTargetEntityId = body.TargetEntityId;

        if (entity.CharacterId != null)
        {
            try
            {
                var relType = await _relationshipClient.GetRelationshipTypeByCodeAsync(
                    new GetRelationshipTypeByCodeRequest { Code = template.Bond.RelationshipTypeCode ?? "bond" }, cancellationToken);
                var relResponse = await _relationshipClient.CreateRelationshipAsync(
                    new CreateRelationshipRequest
                    {
                        Entity1Id = entity.CharacterId.Value,
                        Entity1Type = EntityType.Character,
                        Entity2Id = body.TargetEntityId,
                        Entity2Type = body.TargetEntityType,
                        RelationshipTypeId = relType.RelationshipTypeId,
                        StartedAt = DateTimeOffset.UtcNow,
                    }, cancellationToken);
                entity.BondId = relResponse.RelationshipId;
            }
            catch (ApiException ex)
            {
                _logger.LogWarning(ex, "Failed to create bond relationship for entity {EntityId}", body.EntityId);
            }
        }

        entity.UpdatedAt = DateTimeOffset.UtcNow;
        await _entityStore.SaveAsync(BuildEntityKey(body.EntityId), entity, cancellationToken: cancellationToken);
        await _entityCacheStore.DeleteAsync(BuildEntityCacheKey(body.EntityId), cancellationToken);

        await _messageBus.PublishGenesisEntityBondCreatedAsync(new GenesisEntityBondCreatedEvent
        {
            EntityId = entity.EntityId,
            TargetEntityType = body.TargetEntityType,
            TargetEntityId = body.TargetEntityId,
            BondId = entity.BondId,
        }, cancellationToken);

        return (StatusCodes.OK, MapEntityToResponse(entity, null));
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, GenesisBondResponse?)> GetBondAsync(
        GetBondRequest body, CancellationToken cancellationToken)
    {
        var entity = await _entityStore.GetAsync(BuildEntityKey(body.EntityId), cancellationToken);
        if (entity == null) return (StatusCodes.NotFound, null);
        if (entity.BondTargetEntityId == null) return (StatusCodes.NotFound, null);

        return (StatusCodes.OK, new GenesisBondResponse
        {
            BondId = entity.BondId,
            BondTargetEntityType = entity.BondTargetEntityType!.Value,
            BondTargetEntityId = entity.BondTargetEntityId.Value,
        });
    }

    /// <inheritdoc/>
    public async Task<StatusCodes> DissolveBondAsync(
        DissolveBondRequest body, CancellationToken cancellationToken)
    {
        var entity = await _entityStore.GetAsync(BuildEntityKey(body.EntityId), cancellationToken);
        if (entity == null) return StatusCodes.NotFound;
        if (entity.BondTargetEntityId == null) return StatusCodes.NotFound;

        var lockOwner = $"dissolve-bond-{Guid.NewGuid():N}";
        await using var lockHandle = await _lockProvider.LockAsync(
            StateStoreDefinitions.GenesisLock, $"bond:{body.EntityId}",
            lockOwner, _configuration.EntityLockTimeoutSeconds, cancellationToken: cancellationToken);
        if (!lockHandle.Success) return StatusCodes.Conflict;

        if (entity.BondId != null)
        {
            try { await _relationshipClient.EndRelationshipAsync(new EndRelationshipRequest { RelationshipId = entity.BondId.Value }, cancellationToken); }
            catch (ApiException ex) { _logger.LogWarning(ex, "Failed to end bond relationship {BondId}", entity.BondId); }
        }

        entity.BondTargetEntityType = null;
        entity.BondTargetEntityId = null;
        entity.BondId = null;
        entity.UpdatedAt = DateTimeOffset.UtcNow;

        await _entityStore.SaveAsync(BuildEntityKey(body.EntityId), entity, cancellationToken: cancellationToken);
        await _entityCacheStore.DeleteAsync(BuildEntityCacheKey(body.EntityId), cancellationToken);
        await _messageBus.PublishGenesisEntityBondDissolvedAsync(new GenesisEntityBondDissolvedEvent { EntityId = entity.EntityId }, cancellationToken);

        return StatusCodes.OK;
    }

    /// <inheritdoc/>
    public async Task<StatusCodes> CleanupByCharacterAsync(
        CleanupByCharacterRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Cleaning up genesis entities for character {CharacterId}", body.CharacterId);
        var entitiesToDestroy = await FindEntitiesByCharacterIdAsync(body.CharacterId, cancellationToken);

        foreach (var entity in entitiesToDestroy)
        {
            try
            {
                var lockOwner = $"cleanup-char-{Guid.NewGuid():N}";
                await using var lh = await _lockProvider.LockAsync(
                    StateStoreDefinitions.GenesisLock, $"entity:{entity.EntityId}",
                    lockOwner, _configuration.EntityLockTimeoutSeconds, cancellationToken: cancellationToken);
                if (!lh.Success) continue;

                if (entity.ActorId != null)
                {
                    try { await _actorClient.StopActorAsync(new StopActorRequest { ActorId = entity.ActorId }, cancellationToken); }
                    catch (ApiException ex) { _logger.LogWarning(ex, "Failed to stop actor for entity {EntityId}", entity.EntityId); }
                }
                if (entity.BondId != null)
                {
                    try { await _relationshipClient.EndRelationshipAsync(new EndRelationshipRequest { RelationshipId = entity.BondId.Value }, cancellationToken); }
                    catch (ApiException ex) { _logger.LogWarning(ex, "Failed to end bond for entity {EntityId}", entity.EntityId); }
                }
                try
                {
                    await _resourceClient.ExecuteCleanupAsync(
                        new ExecuteCleanupRequest { ResourceType = "genesis-entity", ResourceId = entity.EntityId }, cancellationToken);
                }
                catch (ApiException ex) { _logger.LogWarning(ex, "Failed resource cleanup for entity {EntityId}", entity.EntityId); }

                await DeleteEntityRecordsAsync(entity, cancellationToken);
                await _messageBus.PublishEntityDeletedAsync(BuildEntityDeletedEvent(entity), cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to cleanup genesis entity {EntityId} for character {CharacterId}", entity.EntityId, body.CharacterId);
            }
        }
        return StatusCodes.OK;
    }

    /// <inheritdoc/>
    public async Task<StatusCodes> CleanupByRealmAsync(
        CleanupByRealmRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Cleaning up genesis entities for realm {RealmId}", body.RealmId);

        // Process in batches to avoid overwhelming infrastructure per implementation map
        while (true)
        {
            var batch = await _entityQueryStore.QueryPagedAsync(
                e => e.RealmId == body.RealmId,
                page: 1, // Always page 1 — we delete as we go, so earlier results shift
                pageSize: _configuration.CleanupBatchSize,
                orderBy: null,
                descending: false,
                cancellationToken);

            if (batch.Items.Count == 0) break;

            foreach (var entity in batch.Items)
            {
                try
                {
                    var template = await _templateStore.GetAsync(BuildTemplateKey(entity.TemplateCode), cancellationToken);

                    var lockOwner = $"cleanup-realm-{Guid.NewGuid():N}";
                    await using var lh = await _lockProvider.LockAsync(
                        StateStoreDefinitions.GenesisLock, $"entity:{entity.EntityId}",
                        lockOwner, _configuration.EntityLockTimeoutSeconds, cancellationToken: cancellationToken);
                    if (!lh.Success) continue;

                    if (entity.ActorId != null)
                    {
                        try { await _actorClient.StopActorAsync(new StopActorRequest { ActorId = entity.ActorId }, cancellationToken); }
                        catch (ApiException ex) { _logger.LogWarning(ex, "Failed to stop actor for entity {EntityId}", entity.EntityId); }
                    }
                    if (entity.CharacterId != null && template?.ArchiveOnDestruction == true)
                    {
                        try { await _resourceClient.ExecuteCompressAsync(new ExecuteCompressRequest { ResourceType = "character", ResourceId = entity.CharacterId.Value }, cancellationToken); }
                        catch (ApiException ex) { _logger.LogWarning(ex, "Failed to archive character for entity {EntityId}", entity.EntityId); }
                    }
                    if (entity.BondId != null)
                    {
                        try { await _relationshipClient.EndRelationshipAsync(new EndRelationshipRequest { RelationshipId = entity.BondId.Value }, cancellationToken); }
                        catch (ApiException ex) { _logger.LogWarning(ex, "Failed to end bond for entity {EntityId}", entity.EntityId); }
                    }
                    try
                    {
                        await _resourceClient.ExecuteCleanupAsync(
                            new ExecuteCleanupRequest { ResourceType = "genesis-entity", ResourceId = entity.EntityId }, cancellationToken);
                    }
                    catch (ApiException ex) { _logger.LogWarning(ex, "Failed resource cleanup for entity {EntityId}", entity.EntityId); }

                    await DeleteEntityRecordsAsync(entity, cancellationToken);
                    await _messageBus.PublishEntityDeletedAsync(BuildEntityDeletedEvent(entity), cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to cleanup genesis entity {EntityId} for realm {RealmId}", entity.EntityId, body.RealmId);
                }
            }
        }
        return StatusCodes.OK;
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, GenesisArchive?)> GetCompressDataAsync(
        GetCompressDataRequest body, CancellationToken cancellationToken)
    {
        var entities = await FindEntitiesByCharacterIdAsync(body.CharacterId, cancellationToken);
        var archivedEntities = new List<GenesisArchivedEntity>();

        foreach (var entity in entities)
        {
            var walletBalances = await FetchWalletBalancesAsync(entity, cancellationToken);
            archivedEntities.Add(new GenesisArchivedEntity
            {
                EntityId = entity.EntityId,
                TemplateCode = entity.TemplateCode,
                GameServiceId = entity.GameServiceId,
                RealmId = entity.RealmId,
                Code = entity.Code,
                DisplayName = entity.DisplayName,
                WalletBalances = walletBalances ?? new Dictionary<string, double>(),
                CurrentPhase = entity.CurrentPhase,
                CognitiveStage = entity.CognitiveStage,
                CreatedAt = entity.CreatedAt,
            });
        }

        return (StatusCodes.OK, new GenesisArchive { Entities = archivedEntities });
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, RestoreFromArchiveResponse?)> RestoreFromArchiveAsync(
        RestoreFromArchiveRequest body, CancellationToken cancellationToken)
    {
        var restoredCount = 0;
        foreach (var archived in body.Archive.Entities)
        {
            var template = await _templateStore.GetAsync(BuildTemplateKey(archived.TemplateCode), cancellationToken);
            if (template == null) return (StatusCodes.BadRequest, null);

            // Multi-service provisioning with direct compensation
            var provisionedWalletIds = new Dictionary<string, Guid>();
            var provisionedInventoryIds = new Dictionary<string, Guid>();
            Guid provisionedSeedId;

            try
            {
                var seedResponse = await _seedClient.CreateSeedAsync(
                    new CreateSeedRequest
                    {
                        OwnerType = EntityType.Other,
                        OwnerId = archived.EntityId,
                        SeedTypeCode = template.Seed.SeedTypeCode,
                        GameServiceId = archived.GameServiceId,
                    }, cancellationToken);
                provisionedSeedId = seedResponse.SeedId;

                foreach (var wallet in template.Economy.Wallets)
                {
                    var wResponse = await _currencyClient.CreateWalletAsync(
                        new CreateWalletRequest { OwnerId = archived.EntityId, OwnerType = EntityType.Other, RealmId = archived.RealmId },
                        cancellationToken);
                    provisionedWalletIds[wallet.WalletCode] = wResponse.WalletId;

                    if (archived.WalletBalances.TryGetValue(wallet.WalletCode, out var balance) && balance > 0)
                    {
                        try
                        {
                            var currencyDef = await _currencyClient.GetCurrencyDefinitionAsync(
                                new GetCurrencyDefinitionRequest { Code = wallet.CurrencyCode }, cancellationToken);
                            await _currencyClient.CreditCurrencyAsync(
                                new CreditCurrencyRequest
                                {
                                    WalletId = wResponse.WalletId,
                                    CurrencyDefinitionId = currencyDef.DefinitionId,
                                    Amount = balance,
                                    TransactionType = TransactionType.Refund,
                                }, cancellationToken);
                        }
                        catch (ApiException ex)
                        {
                            _logger.LogWarning(ex, "Failed to restore balance for wallet {WalletCode}", wallet.WalletCode);
                        }
                    }
                }

                foreach (var inv in template.Storage.Inventories)
                {
                    var iResponse = await _inventoryClient.CreateContainerAsync(
                        new CreateContainerRequest
                        {
                            OwnerId = archived.EntityId,
                            OwnerType = ContainerOwnerType.Other,
                            ContainerType = inv.InventoryCode,
                            ConstraintModel = inv.ConstraintModel.MapByName<ContainerConstraintModel>(),
                            MaxSlots = inv.Capacity,
                        }, cancellationToken);
                    provisionedInventoryIds[inv.InventoryCode] = iResponse.ContainerId;
                }
            }
            catch (Exception)
            {
                await CompensateProvisioningAsync(provisionedWalletIds, provisionedInventoryIds, cancellationToken);
                throw;
            }

            var now = DateTimeOffset.UtcNow;
            var entity = new GenesisEntityModel
            {
                EntityId = archived.EntityId,
                TemplateCode = archived.TemplateCode,
                GameServiceId = archived.GameServiceId,
                RealmId = archived.RealmId,
                Code = archived.Code,
                DisplayName = archived.DisplayName,
                SeedId = provisionedSeedId,
                WalletIds = provisionedWalletIds,
                InventoryIds = provisionedInventoryIds,
                CurrentPhase = archived.CurrentPhase,
                CognitiveStage = CognitiveStage.Dormant,
                PhysicalFormType = template.PhysicalFormType,
                Status = GenesisEntityStatus.Active,
                CreatedAt = archived.CreatedAt,
                UpdatedAt = now,
            };

            await _entityStore.SaveAsync(BuildEntityKey(archived.EntityId), entity, cancellationToken: cancellationToken);
            if (archived.Code != null)
                await _entityIndexStore.SaveAsync(
                    BuildEntityCodeKey(archived.GameServiceId, archived.RealmId, archived.Code),
                    archived.EntityId.ToString(), cancellationToken: cancellationToken);
            await AddToEntityTemplateIndexAsync(archived.TemplateCode, archived.RealmId, archived.EntityId, cancellationToken);
            foreach (var walletId in provisionedWalletIds.Values)
                await _entityIndexStore.SaveAsync(BuildEntityWalletKey(walletId), archived.EntityId.ToString(), cancellationToken: cancellationToken);

            // Maintain template→entity reverse index for clean-deprecated instance checks
            await _entityIndexStore.AddToStringListAsync(
                BuildEntityTemplateInstancesKey(archived.TemplateCode),
                archived.EntityId.ToString(),
                _configuration.ListOperationMaxRetries,
                _logger,
                cancellationToken);

            // Populate the in-memory wallet map for the restored entity's wallets
            PopulateWalletMap(entity, template);

            await _messageBus.PublishEntityCreatedAsync(new EntityCreatedEvent
            {
                EntityId = archived.EntityId,
                TemplateCode = archived.TemplateCode,
                GameServiceId = archived.GameServiceId,
                RealmId = archived.RealmId,
                Code = archived.Code,
                DisplayName = archived.DisplayName,
                WalletIds = provisionedWalletIds,
                InventoryIds = provisionedInventoryIds,
                CurrentPhase = entity.CurrentPhase,
                CognitiveStage = entity.CognitiveStage,
                PhysicalFormType = entity.PhysicalFormType,
                Status = entity.Status,
                CreatedAt = entity.CreatedAt,
                UpdatedAt = entity.UpdatedAt,
            }, cancellationToken);

            restoredCount++;
        }
        return (StatusCodes.OK, new RestoreFromArchiveResponse { RestoredCount = restoredCount });
    }
}
