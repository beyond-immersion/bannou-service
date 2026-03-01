using BeyondImmersion.Bannou.Core;
using BeyondImmersion.Bannou.Status.ClientEvents;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Contract;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.GameService;
using BeyondImmersion.BannouService.Inventory;
using BeyondImmersion.BannouService.Item;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Providers;
using BeyondImmersion.BannouService.Seed;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Status;

/// <summary>
/// Implementation of the Status service providing unified entity effects management.
/// Aggregates item-based statuses (temporary, contract-managed) and seed-derived
/// passive capabilities into a single query layer.
/// </summary>
/// <remarks>
/// <para>
/// <b>FOUNDATION TENETS - PARTIAL CLASS REQUIRED:</b> This class MUST remain a partial class.
/// Event handlers are in StatusServiceEvents.cs.
/// </para>
/// <para>
/// <b>IMPLEMENTATION TENETS CHECKLIST:</b>
/// <list type="bullet">
///   <item><b>Type Safety:</b> All models use proper C# types (enums, Guids, DateTimeOffset).</item>
///   <item><b>Configuration:</b> All 13 config properties are wired up.</item>
///   <item><b>Events:</b> All state changes publish typed events.</item>
///   <item><b>Cache Stores:</b> Redis caches with TTL, invalidated on mutation, rebuilt on miss.</item>
///   <item><b>Concurrency:</b> Distributed locks via IDistributedLockProvider for entity mutations.</item>
/// </list>
/// </para>
/// </remarks>
[BannouService("status", typeof(IStatusService), lifetime: ServiceLifetime.Scoped, layer: ServiceLayer.GameFeatures)]
public partial class StatusService : IStatusService
{
    private readonly IMessageBus _messageBus;
    private readonly IStateStoreFactory _stateStoreFactory;
    private readonly ILogger<StatusService> _logger;
    private readonly StatusServiceConfiguration _configuration;
    private readonly IInventoryClient _inventoryClient;
    private readonly IItemClient _itemClient;
    private readonly IContractClient _contractClient;
    private readonly IGameServiceClient _gameServiceClient;
    private readonly IDistributedLockProvider _lockProvider;
    private readonly IServiceProvider _serviceProvider;
    private readonly IEntitySessionRegistry _entitySessionRegistry;
    private readonly ITelemetryProvider _telemetryProvider;

    #region State Store Accessors

    private IStateStore<StatusTemplateModel>? _templateStore;
    private IStateStore<StatusTemplateModel> TemplateStore =>
        _templateStore ??= _stateStoreFactory.GetStore<StatusTemplateModel>(StateStoreDefinitions.StatusTemplates);

    private IJsonQueryableStateStore<StatusTemplateModel>? _templateQueryStore;
    private IJsonQueryableStateStore<StatusTemplateModel> TemplateQueryStore =>
        _templateQueryStore ??= _stateStoreFactory.GetJsonQueryableStore<StatusTemplateModel>(StateStoreDefinitions.StatusTemplates);

    private IStateStore<StatusInstanceModel>? _instanceStore;
    private IStateStore<StatusInstanceModel> InstanceStore =>
        _instanceStore ??= _stateStoreFactory.GetStore<StatusInstanceModel>(StateStoreDefinitions.StatusInstances);

    private IJsonQueryableStateStore<StatusInstanceModel>? _instanceQueryStore;
    private IJsonQueryableStateStore<StatusInstanceModel> InstanceQueryStore =>
        _instanceQueryStore ??= _stateStoreFactory.GetJsonQueryableStore<StatusInstanceModel>(StateStoreDefinitions.StatusInstances);

    private IStateStore<StatusContainerModel>? _containerStore;
    private IStateStore<StatusContainerModel> ContainerStore =>
        _containerStore ??= _stateStoreFactory.GetStore<StatusContainerModel>(StateStoreDefinitions.StatusContainers);

    private IJsonQueryableStateStore<StatusContainerModel>? _containerQueryStore;
    private IJsonQueryableStateStore<StatusContainerModel> ContainerQueryStore =>
        _containerQueryStore ??= _stateStoreFactory.GetJsonQueryableStore<StatusContainerModel>(StateStoreDefinitions.StatusContainers);

    private IStateStore<ActiveStatusCacheModel>? _activeCacheStore;
    private IStateStore<ActiveStatusCacheModel> ActiveCacheStore =>
        _activeCacheStore ??= _stateStoreFactory.GetStore<ActiveStatusCacheModel>(StateStoreDefinitions.StatusActiveCache);

    private IStateStore<SeedEffectsCacheModel>? _seedEffectsCacheStore;
    private IStateStore<SeedEffectsCacheModel> SeedEffectsCacheStore =>
        _seedEffectsCacheStore ??= _stateStoreFactory.GetStore<SeedEffectsCacheModel>(StateStoreDefinitions.StatusSeedEffectsCache);

    #endregion

    #region Key Building

    private static string TemplateIdKey(Guid templateId) => $"tpl:{templateId}";
    private static string TemplateCodeKey(Guid gameServiceId, string code) => $"tpl:{gameServiceId}:{code}";
    private static string InstanceIdKey(Guid instanceId) => $"inst:{instanceId}";
    private static string ContainerIdKey(Guid containerId) => $"ctr:{containerId}";
    private static string ContainerEntityKey(Guid entityId, EntityType entityType, Guid gameServiceId) =>
        $"ctr:{entityId}:{entityType}:{gameServiceId}";
    private static string ActiveCacheKey(Guid entityId, EntityType entityType) => $"active:{entityId}:{entityType}";
    private static string SeedEffectsCacheKey(Guid entityId, EntityType entityType) => $"seed:{entityId}:{entityType}";
    private static string EntityLockKey(EntityType entityType, Guid entityId) => $"entity:{entityType}:{entityId}";

    #endregion

    /// <summary>
    /// Initializes the StatusService with required dependencies.
    /// </summary>
    public StatusService(
        IMessageBus messageBus,
        IStateStoreFactory stateStoreFactory,
        ILogger<StatusService> logger,
        StatusServiceConfiguration configuration,
        IEventConsumer eventConsumer,
        IInventoryClient inventoryClient,
        IItemClient itemClient,
        IContractClient contractClient,
        IGameServiceClient gameServiceClient,
        IDistributedLockProvider lockProvider,
        IServiceProvider serviceProvider,
        IEntitySessionRegistry entitySessionRegistry,
        ITelemetryProvider telemetryProvider)
    {
        _messageBus = messageBus;
        _stateStoreFactory = stateStoreFactory;
        _logger = logger;
        _configuration = configuration;
        _inventoryClient = inventoryClient;
        _itemClient = itemClient;
        _contractClient = contractClient;
        _gameServiceClient = gameServiceClient;
        _lockProvider = lockProvider;
        _serviceProvider = serviceProvider;
        _entitySessionRegistry = entitySessionRegistry;
        _telemetryProvider = telemetryProvider;

        RegisterEventConsumers(eventConsumer);

        if (_configuration.CacheWarmingEnabled)
        {
            _logger.LogInformation(
                "Status cache warming enabled with max {MaxCachedEntities} cached entities",
                _configuration.MaxCachedEntities);
        }
    }

    // ========================================================================
    // TEMPLATE MANAGEMENT
    // ========================================================================

    /// <summary>
    /// Creates a new status template definition for a game service.
    /// Validates the game service exists and the template code is unique.
    /// </summary>
    public async Task<(StatusCodes, StatusTemplateResponse?)> CreateStatusTemplateAsync(
        CreateStatusTemplateRequest body, CancellationToken cancellationToken)
    {
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

        // Check template count limit per game service
        var countConditions = new List<QueryCondition>
        {
            new QueryCondition { Path = "$.StatusTemplateId", Operator = QueryOperator.Exists, Value = true },
            new QueryCondition { Path = "$.GameServiceId", Operator = QueryOperator.Equals, Value = body.GameServiceId }
        };
        var existing = await TemplateQueryStore.JsonQueryPagedAsync(
            countConditions, 0, 1, null, cancellationToken);
        if (existing.TotalCount >= _configuration.MaxStatusTemplatesPerGameService)
        {
            _logger.LogWarning(
                "Game service {GameServiceId} has reached template limit {Limit}",
                body.GameServiceId, _configuration.MaxStatusTemplatesPerGameService);
            return (StatusCodes.Conflict, null);
        }

        // Check code uniqueness
        var codeKey = TemplateCodeKey(body.GameServiceId, body.Code);
        var existingByCode = await TemplateStore.GetAsync(codeKey, cancellationToken);
        if (existingByCode != null)
        {
            _logger.LogWarning(
                "Template code {Code} already exists for game service {GameServiceId}",
                body.Code, body.GameServiceId);
            return (StatusCodes.Conflict, null);
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

        var effectiveMaxStacks = Math.Min(
            body.MaxStacks,
            _configuration.MaxStacksPerStatus);

        var now = DateTimeOffset.UtcNow;
        var templateId = Guid.NewGuid();
        var model = new StatusTemplateModel
        {
            StatusTemplateId = templateId,
            GameServiceId = body.GameServiceId,
            Code = body.Code,
            DisplayName = body.DisplayName,
            Description = body.Description,
            Category = body.Category,
            Stackable = body.Stackable,
            MaxStacks = effectiveMaxStacks,
            StackBehavior = body.StackBehavior,
            ContractTemplateId = body.ContractTemplateId,
            ItemTemplateId = body.ItemTemplateId,
            DefaultDurationSeconds = body.DefaultDurationSeconds,
            IconAssetId = body.IconAssetId,
            CreatedAt = now
        };

        // Save with dual keys
        await TemplateStore.SaveAsync(TemplateIdKey(templateId), model, cancellationToken: cancellationToken);
        await TemplateStore.SaveAsync(codeKey, model, cancellationToken: cancellationToken);

        // Publish lifecycle event
        await _messageBus.TryPublishAsync(
            "status.template.created",
            new StatusTemplateCreatedEvent
            {
                StatusTemplateId = templateId,
                GameServiceId = body.GameServiceId,
                Code = body.Code,
                DisplayName = body.DisplayName,
                Category = body.Category,
                Stackable = body.Stackable,
                MaxStacks = effectiveMaxStacks,
                StackBehavior = body.StackBehavior,
                CreatedAt = now
            },
            cancellationToken: cancellationToken);

        _logger.LogInformation(
            "Created status template {Code} ({TemplateId}) for game service {GameServiceId}",
            body.Code, templateId, body.GameServiceId);

        return (StatusCodes.OK, ToTemplateResponse(model));
    }

    /// <summary>
    /// Retrieves a status template by its unique identifier.
    /// </summary>
    public async Task<(StatusCodes, StatusTemplateResponse?)> GetStatusTemplateAsync(
        GetStatusTemplateRequest body, CancellationToken cancellationToken)
    {
        var template = await TemplateStore.GetAsync(
            TemplateIdKey(body.StatusTemplateId), cancellationToken);

        if (template == null)
        {
            return (StatusCodes.NotFound, null);
        }

        return (StatusCodes.OK, ToTemplateResponse(template));
    }

    /// <summary>
    /// Retrieves a status template by game service ID and code.
    /// </summary>
    public async Task<(StatusCodes, StatusTemplateResponse?)> GetStatusTemplateByCodeAsync(
        GetStatusTemplateByCodeRequest body, CancellationToken cancellationToken)
    {
        var template = await TemplateStore.GetAsync(
            TemplateCodeKey(body.GameServiceId, body.Code), cancellationToken);

        if (template == null)
        {
            return (StatusCodes.NotFound, null);
        }

        return (StatusCodes.OK, ToTemplateResponse(template));
    }

    /// <summary>
    /// Lists status templates for a game service with optional category filter and pagination.
    /// </summary>
    public async Task<(StatusCodes, ListStatusTemplatesResponse?)> ListStatusTemplatesAsync(
        ListStatusTemplatesRequest body, CancellationToken cancellationToken)
    {
        var conditions = new List<QueryCondition>
        {
            new QueryCondition { Path = "$.StatusTemplateId", Operator = QueryOperator.Exists, Value = true },
            new QueryCondition { Path = "$.GameServiceId", Operator = QueryOperator.Equals, Value = body.GameServiceId }
        };

        if (body.Category.HasValue)
        {
            conditions.Add(new QueryCondition
            {
                Path = "$.Category",
                Operator = QueryOperator.Equals,
                Value = body.Category.Value.ToString()
            });
        }

        var pageSize = body.PageSize > 0 ? body.PageSize : _configuration.DefaultPageSize;
        var offset = (body.Page - 1) * pageSize;

        var result = await TemplateQueryStore.JsonQueryPagedAsync(
            conditions, offset, pageSize,
            new JsonSortSpec { Path = "$.Code", Descending = false },
            cancellationToken);

        var templates = result.Items
            .Select(r => ToTemplateResponse(r.Value))
            .ToList();

        return (StatusCodes.OK, new ListStatusTemplatesResponse
        {
            Templates = templates,
            TotalCount = (int)result.TotalCount,
            Page = body.Page,
            PageSize = pageSize
        });
    }

    /// <summary>
    /// Updates mutable fields on an existing status template.
    /// </summary>
    public async Task<(StatusCodes, StatusTemplateResponse?)> UpdateStatusTemplateAsync(
        UpdateStatusTemplateRequest body, CancellationToken cancellationToken)
    {
        await using var lockHandle = await _lockProvider.LockAsync(
            StateStoreDefinitions.StatusLock,
            $"tpl:{body.StatusTemplateId}",
            Guid.NewGuid().ToString(),
            _configuration.LockTimeoutSeconds,
            cancellationToken);

        if (!lockHandle.Success)
        {
            _logger.LogWarning("Failed to acquire lock for template {TemplateId}", body.StatusTemplateId);
            return (StatusCodes.Conflict, null);
        }

        var template = await TemplateStore.GetAsync(
            TemplateIdKey(body.StatusTemplateId), cancellationToken);

        if (template == null)
        {
            return (StatusCodes.NotFound, null);
        }

        var changedFields = new List<string>();

        if (body.DisplayName != null)
        {
            template.DisplayName = body.DisplayName;
            changedFields.Add("displayName");
        }
        if (body.Description != null)
        {
            template.Description = body.Description;
            changedFields.Add("description");
        }
        if (body.Category.HasValue)
        {
            template.Category = body.Category.Value;
            changedFields.Add("category");
        }
        if (body.Stackable.HasValue)
        {
            template.Stackable = body.Stackable.Value;
            changedFields.Add("stackable");
        }
        if (body.MaxStacks.HasValue)
        {
            template.MaxStacks = Math.Min(body.MaxStacks.Value, _configuration.MaxStacksPerStatus);
            changedFields.Add("maxStacks");
        }
        if (body.StackBehavior.HasValue)
        {
            template.StackBehavior = body.StackBehavior.Value;
            changedFields.Add("stackBehavior");
        }
        if (body.ContractTemplateId.HasValue)
        {
            template.ContractTemplateId = body.ContractTemplateId;
            changedFields.Add("contractTemplateId");
        }
        if (body.DefaultDurationSeconds.HasValue)
        {
            template.DefaultDurationSeconds = body.DefaultDurationSeconds;
            changedFields.Add("defaultDurationSeconds");
        }
        if (body.IconAssetId.HasValue)
        {
            template.IconAssetId = body.IconAssetId;
            changedFields.Add("iconAssetId");
        }

        template.UpdatedAt = DateTimeOffset.UtcNow;

        // Save with dual keys
        await TemplateStore.SaveAsync(TemplateIdKey(template.StatusTemplateId), template, cancellationToken: cancellationToken);
        await TemplateStore.SaveAsync(
            TemplateCodeKey(template.GameServiceId, template.Code), template, cancellationToken: cancellationToken);

        // Publish lifecycle event
        await _messageBus.TryPublishAsync(
            "status.template.updated",
            new StatusTemplateUpdatedEvent
            {
                StatusTemplateId = template.StatusTemplateId,
                GameServiceId = template.GameServiceId,
                Code = template.Code,
                DisplayName = template.DisplayName,
                Category = template.Category,
                Stackable = template.Stackable,
                MaxStacks = template.MaxStacks,
                StackBehavior = template.StackBehavior,
                CreatedAt = template.CreatedAt,
                UpdatedAt = template.UpdatedAt,
                ChangedFields = changedFields
            },
            cancellationToken: cancellationToken);

        _logger.LogInformation(
            "Updated status template {TemplateId} fields: {Fields}",
            template.StatusTemplateId, string.Join(", ", changedFields));

        return (StatusCodes.OK, ToTemplateResponse(template));
    }

    /// <summary>
    /// Bulk-creates status templates, skipping any whose code already exists for the game service.
    /// </summary>
    public async Task<(StatusCodes, SeedStatusTemplatesResponse?)> SeedStatusTemplatesAsync(
        SeedStatusTemplatesRequest body, CancellationToken cancellationToken)
    {
        // Validate game service exists (matches CreateStatusTemplateAsync validation)
        try
        {
            await _gameServiceClient.GetServiceAsync(
                new GetServiceRequest { ServiceId = body.GameServiceId },
                cancellationToken);
        }
        catch (ApiException ex) when (ex.StatusCode == 404)
        {
            _logger.LogWarning("Game service {GameServiceId} not found for seed operation", body.GameServiceId);
            return (StatusCodes.NotFound, null);
        }

        var created = 0;
        var skipped = 0;

        foreach (var templateReq in body.Templates)
        {
            var codeKey = TemplateCodeKey(body.GameServiceId, templateReq.Code);
            var existingByCode = await TemplateStore.GetAsync(codeKey, cancellationToken);

            if (existingByCode != null)
            {
                skipped++;
                continue;
            }

            // Validate item template exists (matches CreateStatusTemplateAsync validation)
            try
            {
                await _itemClient.GetItemTemplateAsync(
                    new GetItemTemplateRequest { TemplateId = templateReq.ItemTemplateId },
                    cancellationToken);
            }
            catch (ApiException ex) when (ex.StatusCode == 404)
            {
                _logger.LogWarning(
                    "Skipping status template {Code}: item template {ItemTemplateId} not found",
                    templateReq.Code, templateReq.ItemTemplateId);
                skipped++;
                continue;
            }

            var effectiveMaxStacks = Math.Min(
                templateReq.MaxStacks,
                _configuration.MaxStacksPerStatus);

            var now = DateTimeOffset.UtcNow;
            var templateId = Guid.NewGuid();
            var model = new StatusTemplateModel
            {
                StatusTemplateId = templateId,
                GameServiceId = body.GameServiceId,
                Code = templateReq.Code,
                DisplayName = templateReq.DisplayName,
                Description = templateReq.Description,
                Category = templateReq.Category,
                Stackable = templateReq.Stackable,
                MaxStacks = effectiveMaxStacks,
                StackBehavior = templateReq.StackBehavior,
                ContractTemplateId = templateReq.ContractTemplateId,
                ItemTemplateId = templateReq.ItemTemplateId,
                DefaultDurationSeconds = templateReq.DefaultDurationSeconds,
                IconAssetId = templateReq.IconAssetId,
                CreatedAt = now
            };

            await TemplateStore.SaveAsync(TemplateIdKey(templateId), model, cancellationToken: cancellationToken);
            await TemplateStore.SaveAsync(codeKey, model, cancellationToken: cancellationToken);

            await _messageBus.TryPublishAsync(
                "status.template.created",
                new StatusTemplateCreatedEvent
                {
                    StatusTemplateId = templateId,
                    GameServiceId = body.GameServiceId,
                    Code = templateReq.Code,
                    DisplayName = templateReq.DisplayName,
                    Category = templateReq.Category,
                    Stackable = templateReq.Stackable,
                    MaxStacks = effectiveMaxStacks,
                    StackBehavior = templateReq.StackBehavior,
                    CreatedAt = now
                },
                cancellationToken: cancellationToken);

            created++;
        }

        _logger.LogInformation(
            "Seeded status templates for game service {GameServiceId}: {Created} created, {Skipped} skipped",
            body.GameServiceId, created, skipped);

        return (StatusCodes.OK, new SeedStatusTemplatesResponse
        {
            Created = created,
            Skipped = skipped
        });
    }

    // ========================================================================
    // STATUS OPERATIONS
    // ========================================================================

    /// <summary>
    /// Grants a status effect to an entity. Handles stacking behavior based on
    /// the template configuration. Creates an item instance in the entity's
    /// status container and optionally a contract instance for lifecycle management.
    /// </summary>
    public async Task<(StatusCodes, GrantStatusResponse?)> GrantStatusAsync(
        GrantStatusRequest body, CancellationToken cancellationToken)
    {
        // Look up template by game service + code
        var template = await TemplateStore.GetAsync(
            TemplateCodeKey(body.GameServiceId, body.StatusTemplateCode), cancellationToken);

        if (template == null)
        {
            await PublishGrantFailedEventAsync(body, GrantFailureReason.TemplateNotFound, null, cancellationToken);
            return (StatusCodes.NotFound, null);
        }

        // Acquire entity lock for mutation safety
        using var lockCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        lockCts.CancelAfter(TimeSpan.FromSeconds(_configuration.LockAcquisitionTimeoutSeconds));

        await using var lockHandle = await _lockProvider.LockAsync(
            StateStoreDefinitions.StatusLock,
            EntityLockKey(body.EntityType, body.EntityId),
            Guid.NewGuid().ToString(),
            _configuration.LockTimeoutSeconds,
            lockCts.Token);

        if (!lockHandle.Success)
        {
            _logger.LogWarning(
                "Failed to acquire lock for {EntityType} {EntityId}",
                body.EntityType, body.EntityId);
            return (StatusCodes.Conflict, null);
        }

        // Query existing instances for this entity + template code
        var existingConditions = new List<QueryCondition>
        {
            new QueryCondition { Path = "$.StatusInstanceId", Operator = QueryOperator.Exists, Value = true },
            new QueryCondition { Path = "$.EntityId", Operator = QueryOperator.Equals, Value = body.EntityId },
            new QueryCondition { Path = "$.EntityType", Operator = QueryOperator.Equals, Value = body.EntityType },
            new QueryCondition { Path = "$.StatusTemplateCode", Operator = QueryOperator.Equals, Value = body.StatusTemplateCode }
        };
        var existingResult = await InstanceQueryStore.JsonQueryPagedAsync(
            existingConditions, 0, _configuration.MaxStacksPerStatus, null, cancellationToken);

        var existingInstances = existingResult.Items.Select(r => r.Value).ToList();

        // Handle stacking if status already exists on entity
        if (existingInstances.Count > 0)
        {
            return await HandleStackingAsync(body, template, existingInstances, cancellationToken);
        }

        // Check entity-wide status limit
        var entityCountConditions = new List<QueryCondition>
        {
            new QueryCondition { Path = "$.StatusInstanceId", Operator = QueryOperator.Exists, Value = true },
            new QueryCondition { Path = "$.EntityId", Operator = QueryOperator.Equals, Value = body.EntityId },
            new QueryCondition { Path = "$.EntityType", Operator = QueryOperator.Equals, Value = body.EntityType }
        };
        var entityCount = await InstanceQueryStore.JsonQueryPagedAsync(
            entityCountConditions, 0, 1, null, cancellationToken);

        if (entityCount.TotalCount >= _configuration.MaxStatusesPerEntity)
        {
            await PublishGrantFailedEventAsync(body, GrantFailureReason.EntityAtMaxStatuses, null, cancellationToken);
            _logger.LogWarning(
                "Entity {EntityType} {EntityId} at max statuses {Max}",
                body.EntityType, body.EntityId, _configuration.MaxStatusesPerEntity);
            return (StatusCodes.Conflict, null);
        }

        // Create new status instance
        return await CreateNewStatusInstanceAsync(body, template, cancellationToken);
    }

    /// <summary>
    /// Removes a specific status instance by ID.
    /// </summary>
    public async Task<(StatusCodes, StatusInstanceResponse?)> RemoveStatusAsync(
        RemoveStatusRequest body, CancellationToken cancellationToken)
    {
        var instance = await InstanceStore.GetAsync(
            InstanceIdKey(body.StatusInstanceId), cancellationToken);

        if (instance == null)
        {
            return (StatusCodes.NotFound, null);
        }

        // Acquire distributed lock per IMPLEMENTATION TENETS (multi-instance safety)
        using var lockCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        lockCts.CancelAfter(TimeSpan.FromSeconds(_configuration.LockAcquisitionTimeoutSeconds));

        await using var lockHandle = await _lockProvider.LockAsync(
            StateStoreDefinitions.StatusLock,
            EntityLockKey(instance.EntityType, instance.EntityId),
            Guid.NewGuid().ToString(),
            _configuration.LockTimeoutSeconds,
            lockCts.Token);

        if (!lockHandle.Success)
        {
            return (StatusCodes.Conflict, null);
        }

        await RemoveInstanceInternalAsync(instance, body.Reason, cancellationToken);

        return (StatusCodes.OK, ToInstanceResponse(instance));
    }

    /// <summary>
    /// Removes all statuses granted by a specific source for an entity.
    /// </summary>
    public async Task<(StatusCodes, RemoveStatusesResponse?)> RemoveBySourceAsync(
        RemoveBySourceRequest body, CancellationToken cancellationToken)
    {
        using var lockCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        lockCts.CancelAfter(TimeSpan.FromSeconds(_configuration.LockAcquisitionTimeoutSeconds));

        await using var lockHandle = await _lockProvider.LockAsync(
            StateStoreDefinitions.StatusLock,
            EntityLockKey(body.EntityType, body.EntityId),
            Guid.NewGuid().ToString(),
            _configuration.LockTimeoutSeconds,
            lockCts.Token);

        if (!lockHandle.Success)
        {
            return (StatusCodes.Conflict, null);
        }

        var conditions = new List<QueryCondition>
        {
            new QueryCondition { Path = "$.StatusInstanceId", Operator = QueryOperator.Exists, Value = true },
            new QueryCondition { Path = "$.EntityId", Operator = QueryOperator.Equals, Value = body.EntityId },
            new QueryCondition { Path = "$.EntityType", Operator = QueryOperator.Equals, Value = body.EntityType },
            new QueryCondition { Path = "$.SourceId", Operator = QueryOperator.Equals, Value = body.SourceId }
        };

        var result = await InstanceQueryStore.JsonQueryPagedAsync(
            conditions, 0, _configuration.MaxStatusesPerEntity, null, cancellationToken);

        var removed = 0;
        foreach (var entry in result.Items)
        {
            await RemoveInstanceInternalAsync(entry.Value, StatusRemoveReason.SourceRemoved, cancellationToken);
            removed++;
        }

        return (StatusCodes.OK, new RemoveStatusesResponse { StatusesRemoved = removed });
    }

    /// <summary>
    /// Removes all statuses of a specific category for an entity (cleanse effect).
    /// </summary>
    public async Task<(StatusCodes, RemoveStatusesResponse?)> RemoveByCategoryAsync(
        RemoveByCategoryRequest body, CancellationToken cancellationToken)
    {
        using var lockCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        lockCts.CancelAfter(TimeSpan.FromSeconds(_configuration.LockAcquisitionTimeoutSeconds));

        await using var lockHandle = await _lockProvider.LockAsync(
            StateStoreDefinitions.StatusLock,
            EntityLockKey(body.EntityType, body.EntityId),
            Guid.NewGuid().ToString(),
            _configuration.LockTimeoutSeconds,
            lockCts.Token);

        if (!lockHandle.Success)
        {
            return (StatusCodes.Conflict, null);
        }

        var conditions = new List<QueryCondition>
        {
            new QueryCondition { Path = "$.StatusInstanceId", Operator = QueryOperator.Exists, Value = true },
            new QueryCondition { Path = "$.EntityId", Operator = QueryOperator.Equals, Value = body.EntityId },
            new QueryCondition { Path = "$.EntityType", Operator = QueryOperator.Equals, Value = body.EntityType },
            new QueryCondition { Path = "$.Category", Operator = QueryOperator.Equals, Value = body.Category.ToString() }
        };

        var result = await InstanceQueryStore.JsonQueryPagedAsync(
            conditions, 0, _configuration.MaxStatusesPerEntity, null, cancellationToken);

        var removed = 0;
        foreach (var entry in result.Items)
        {
            await RemoveInstanceInternalAsync(entry.Value, body.Reason, cancellationToken);
            removed++;
        }

        // Publish cleansed event for the batch operation
        if (removed > 0)
        {
            await _messageBus.TryPublishAsync(
                "status.cleansed",
                new StatusCleansedEvent
                {
                    EventId = Guid.NewGuid(),
                    Timestamp = DateTimeOffset.UtcNow,
                    EntityId = body.EntityId,
                    EntityType = body.EntityType,
                    Category = body.Category,
                    StatusesRemoved = removed,
                    Reason = body.Reason
                },
                cancellationToken: cancellationToken);
        }

        return (StatusCodes.OK, new RemoveStatusesResponse { StatusesRemoved = removed });
    }

    /// <summary>
    /// Checks whether an entity has an active status with the given code.
    /// Uses the active cache for fast lookups.
    /// </summary>
    public async Task<(StatusCodes, HasStatusResponse?)> HasStatusAsync(
        HasStatusRequest body, CancellationToken cancellationToken)
    {
        var cache = await GetOrBuildActiveCacheAsync(
            body.EntityId, body.EntityType, cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var match = cache.Statuses.FirstOrDefault(s =>
            s.StatusTemplateCode == body.StatusCode &&
            (!s.ExpiresAt.HasValue || s.ExpiresAt.Value > now));

        if (match != null)
        {
            return (StatusCodes.OK, new HasStatusResponse
            {
                HasStatus = true,
                StatusInstanceId = match.StatusInstanceId,
                StackCount = match.StackCount
            });
        }

        return (StatusCodes.OK, new HasStatusResponse { HasStatus = false });
    }

    /// <summary>
    /// Lists active statuses for an entity with optional category filter and passive effects.
    /// </summary>
    public async Task<(StatusCodes, ListStatusesResponse?)> ListStatusesAsync(
        ListStatusesRequest body, CancellationToken cancellationToken)
    {
        var allEffects = new List<StatusEffectSummary>();
        var now = DateTimeOffset.UtcNow;

        // Get item-based statuses from cache
        var cache = await GetOrBuildActiveCacheAsync(
            body.EntityId, body.EntityType, cancellationToken);

        var itemStatuses = cache.Statuses
            .Where(s => !s.ExpiresAt.HasValue || s.ExpiresAt.Value > now);

        if (body.Category.HasValue)
        {
            itemStatuses = itemStatuses.Where(s => s.Category == body.Category.Value);
        }

        allEffects.AddRange(itemStatuses.Select(s => new StatusEffectSummary
        {
            StatusCode = s.StatusTemplateCode,
            Category = s.Category,
            EffectSource = EffectSource.ItemBased,
            StackCount = s.StackCount,
            ExpiresAt = s.ExpiresAt,
            SourceId = s.SourceId
        }));

        // Add seed-derived effects if requested
        if (body.IncludePassive && _configuration.SeedEffectsEnabled)
        {
            var seedCache = await GetOrBuildSeedEffectsCacheAsync(
                body.EntityId, body.EntityType, cancellationToken);

            allEffects.AddRange(seedCache.Effects.Select(e => new StatusEffectSummary
            {
                StatusCode = e.CapabilityCode,
                Category = StatusCategory.Passive,
                EffectSource = EffectSource.SeedDerived,
                Fidelity = e.Fidelity,
                SeedId = e.SeedId
            }));
        }

        var totalCount = allEffects.Count;
        var pageSize = body.PageSize > 0 ? body.PageSize : _configuration.DefaultPageSize;
        var paged = allEffects
            .Skip((body.Page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        return (StatusCodes.OK, new ListStatusesResponse
        {
            Statuses = paged,
            TotalCount = totalCount,
            Page = body.Page,
            PageSize = pageSize
        });
    }

    /// <summary>
    /// Retrieves a single status instance by ID.
    /// </summary>
    public async Task<(StatusCodes, StatusInstanceResponse?)> GetStatusAsync(
        GetStatusRequest body, CancellationToken cancellationToken)
    {
        var instance = await InstanceStore.GetAsync(
            InstanceIdKey(body.StatusInstanceId), cancellationToken);

        if (instance == null)
        {
            return (StatusCodes.NotFound, null);
        }

        return (StatusCodes.OK, ToInstanceResponse(instance));
    }

    // ========================================================================
    // EFFECTS QUERIES
    // ========================================================================

    /// <summary>
    /// Returns a unified view of all active effects on an entity,
    /// combining item-based statuses and seed-derived passive capabilities.
    /// </summary>
    public async Task<(StatusCodes, GetEffectsResponse?)> GetEffectsAsync(
        GetEffectsRequest body, CancellationToken cancellationToken)
    {
        var effects = new List<StatusEffectSummary>();
        var now = DateTimeOffset.UtcNow;

        // Item-based statuses
        var cache = await GetOrBuildActiveCacheAsync(
            body.EntityId, body.EntityType, cancellationToken);

        var activeStatuses = cache.Statuses
            .Where(s => !s.ExpiresAt.HasValue || s.ExpiresAt.Value > now)
            .ToList();

        effects.AddRange(activeStatuses.Select(s => new StatusEffectSummary
        {
            StatusCode = s.StatusTemplateCode,
            Category = s.Category,
            EffectSource = EffectSource.ItemBased,
            StackCount = s.StackCount,
            ExpiresAt = s.ExpiresAt,
            SourceId = s.SourceId
        }));

        var seedDerivedCount = 0;

        // Seed-derived effects
        if (body.IncludePassive && _configuration.SeedEffectsEnabled)
        {
            var seedCache = await GetOrBuildSeedEffectsCacheAsync(
                body.EntityId, body.EntityType, cancellationToken);

            seedDerivedCount = seedCache.Effects.Count;

            effects.AddRange(seedCache.Effects.Select(e => new StatusEffectSummary
            {
                StatusCode = e.CapabilityCode,
                Category = StatusCategory.Passive,
                EffectSource = EffectSource.SeedDerived,
                Fidelity = e.Fidelity,
                SeedId = e.SeedId
            }));
        }

        return (StatusCodes.OK, new GetEffectsResponse
        {
            EntityId = body.EntityId,
            EntityType = body.EntityType,
            ItemBasedCount = activeStatuses.Count,
            SeedDerivedCount = seedDerivedCount,
            Effects = effects
        });
    }

    /// <summary>
    /// Returns seed-derived passive effects only. Gated by SeedEffectsEnabled config.
    /// </summary>
    public async Task<(StatusCodes, SeedEffectsResponse?)> GetSeedEffectsAsync(
        GetSeedEffectsRequest body, CancellationToken cancellationToken)
    {
        if (!_configuration.SeedEffectsEnabled)
        {
            return (StatusCodes.OK, new SeedEffectsResponse
            {
                EntityId = body.EntityId,
                EntityType = body.EntityType,
                Effects = new List<SeedEffectEntry>()
            });
        }

        var cache = await GetOrBuildSeedEffectsCacheAsync(
            body.EntityId, body.EntityType, cancellationToken);

        var effects = cache.Effects.Select(e => new SeedEffectEntry
        {
            CapabilityCode = e.CapabilityCode,
            Domain = e.Domain,
            Fidelity = e.Fidelity,
            SeedId = e.SeedId,
            SeedTypeCode = e.SeedTypeCode
        }).ToList();

        return (StatusCodes.OK, new SeedEffectsResponse
        {
            EntityId = body.EntityId,
            EntityType = body.EntityType,
            Effects = effects
        });
    }

    // ========================================================================
    // CLEANUP
    // ========================================================================

    /// <summary>
    /// Removes all status data for an owner entity. Called by lib-resource cleanup callbacks.
    /// Deletes all instances, their backing items, containers, and caches.
    /// </summary>
    public async Task<(StatusCodes, CleanupResponse?)> CleanupByOwnerAsync(
        CleanupByOwnerRequest body, CancellationToken cancellationToken)
    {
        var statusesRemoved = 0;
        var containersDeleted = 0;

        // Find all containers for this owner across all game services
        var containerConditions = new List<QueryCondition>
        {
            new QueryCondition { Path = "$.ContainerId", Operator = QueryOperator.Exists, Value = true },
            new QueryCondition { Path = "$.EntityId", Operator = QueryOperator.Equals, Value = body.OwnerId },
            new QueryCondition { Path = "$.EntityType", Operator = QueryOperator.Equals, Value = body.OwnerType }
        };
        var containers = await ContainerQueryStore.JsonQueryPagedAsync(
            containerConditions, 0, 100, null, cancellationToken);

        foreach (var containerEntry in containers.Items)
        {
            var container = containerEntry.Value;

            // Find all instances for this entity + game service
            var instanceConditions = new List<QueryCondition>
            {
                new QueryCondition { Path = "$.StatusInstanceId", Operator = QueryOperator.Exists, Value = true },
                new QueryCondition { Path = "$.EntityId", Operator = QueryOperator.Equals, Value = body.OwnerId },
                new QueryCondition { Path = "$.EntityType", Operator = QueryOperator.Equals, Value = body.OwnerType },
                new QueryCondition { Path = "$.GameServiceId", Operator = QueryOperator.Equals, Value = container.GameServiceId }
            };
            var instances = await InstanceQueryStore.JsonQueryPagedAsync(
                instanceConditions, 0, _configuration.MaxStatusesPerEntity, null, cancellationToken);

            foreach (var instanceEntry in instances.Items)
            {
                var instance = instanceEntry.Value;

                // Delete backing item
                try
                {
                    await _itemClient.DestroyItemInstanceAsync(
                        new DestroyItemInstanceRequest { InstanceId = instance.ItemInstanceId, Reason = DestroyReason.Destroyed },
                        cancellationToken);
                }
                catch (ApiException ex)
                {
                    _logger.LogWarning(ex,
                        "Failed to delete item instance {ItemInstanceId} during cleanup",
                        instance.ItemInstanceId);
                }

                // Delete instance record
                await InstanceStore.DeleteAsync(
                    InstanceIdKey(instance.StatusInstanceId), cancellationToken);
                statusesRemoved++;
            }

            // Delete container via inventory
            try
            {
                await _inventoryClient.DeleteContainerAsync(
                    new DeleteContainerRequest { ContainerId = container.ContainerId, ItemHandling = ItemHandling.Destroy },
                    cancellationToken);
            }
            catch (ApiException ex)
            {
                _logger.LogWarning(ex,
                    "Failed to delete container {ContainerId} during cleanup",
                    container.ContainerId);
            }

            // Delete container records (dual keys)
            await ContainerStore.DeleteAsync(
                ContainerIdKey(container.ContainerId), cancellationToken);
            await ContainerStore.DeleteAsync(
                ContainerEntityKey(container.EntityId, container.EntityType, container.GameServiceId),
                cancellationToken);
            containersDeleted++;
        }

        // Invalidate caches
        await InvalidateActiveCacheAsync(body.OwnerId, body.OwnerType, cancellationToken);
        if (_configuration.SeedEffectsEnabled)
        {
            await InvalidateSeedEffectsCacheAsync(body.OwnerId, body.OwnerType, cancellationToken);
        }

        _logger.LogInformation(
            "Cleaned up {StatusesRemoved} statuses and {ContainersDeleted} containers for {OwnerType} {OwnerId}",
            statusesRemoved, containersDeleted, body.OwnerType, body.OwnerId);

        return (StatusCodes.OK, new CleanupResponse
        {
            StatusesRemoved = statusesRemoved,
            ContainersDeleted = containersDeleted
        });
    }

    // ========================================================================
    // PRIVATE HELPERS
    // ========================================================================

    /// <summary>
    /// Handles stacking behavior when granting a status that already exists on the entity.
    /// </summary>
    private async Task<(StatusCodes, GrantStatusResponse?)> HandleStackingAsync(
        GrantStatusRequest body,
        StatusTemplateModel template,
        List<StatusInstanceModel> existingInstances,
        CancellationToken cancellationToken)
    {
        var effectiveMaxStacks = Math.Min(template.MaxStacks, _configuration.MaxStacksPerStatus);
        var existing = existingInstances[0];

        switch (template.StackBehavior)
        {
            case StackBehavior.Ignore:
                await PublishGrantFailedEventAsync(
                    body, GrantFailureReason.StackBehaviorIgnore, existing.StatusInstanceId, cancellationToken);
                return (StatusCodes.Conflict, null);

            case StackBehavior.Replace:
                // Remove old instance, create new
                await RemoveInstanceInternalAsync(existing, StatusRemoveReason.Cancelled, cancellationToken);
                return await CreateNewStatusInstanceAsync(body, template, cancellationToken);

            case StackBehavior.RefreshDuration:
                // Update expiration on existing
                existing.ExpiresAt = CalculateExpiry(body, template);
                await InstanceStore.SaveAsync(
                    InstanceIdKey(existing.StatusInstanceId), existing, cancellationToken: cancellationToken);
                await InvalidateActiveCacheAsync(body.EntityId, body.EntityType, cancellationToken);

                await _messageBus.TryPublishAsync(
                    "status.stacked",
                    new StatusStackedEvent
                    {
                        EventId = Guid.NewGuid(),
                        Timestamp = DateTimeOffset.UtcNow,
                        EntityId = body.EntityId,
                        EntityType = body.EntityType,
                        StatusTemplateCode = body.StatusTemplateCode,
                        StatusInstanceId = existing.StatusInstanceId,
                        OldStackCount = existing.StackCount,
                        NewStackCount = existing.StackCount
                    },
                    cancellationToken: cancellationToken);

                // Push client event for duration refresh
                await PublishStatusClientEventAsync(body.EntityType, body.EntityId,
                    new StatusEffectChangedClientEvent
                    {
                        EventName = "status.effect-changed",
                        EventId = Guid.NewGuid(),
                        Timestamp = DateTimeOffset.UtcNow,
                        ChangeType = StatusChangeType.Stacked,
                        EntityId = body.EntityId,
                        EntityType = body.EntityType,
                        StatusTemplateCode = body.StatusTemplateCode,
                        StatusInstanceId = existing.StatusInstanceId,
                        Category = template.Category,
                        StackCount = existing.StackCount,
                        ExpiresAt = existing.ExpiresAt
                    }, cancellationToken);

                return (StatusCodes.OK, new GrantStatusResponse
                {
                    StatusInstanceId = existing.StatusInstanceId,
                    StatusTemplateCode = body.StatusTemplateCode,
                    StackCount = existing.StackCount,
                    ContractInstanceId = existing.ContractInstanceId,
                    ItemInstanceId = existing.ItemInstanceId,
                    GrantedAt = existing.GrantedAt,
                    ExpiresAt = existing.ExpiresAt,
                    GrantResult = GrantResult.Refreshed
                });

            case StackBehavior.IncreaseIntensity:
                if (existing.StackCount >= effectiveMaxStacks)
                {
                    await PublishGrantFailedEventAsync(
                        body, GrantFailureReason.StackLimitReached, existing.StatusInstanceId, cancellationToken);
                    return (StatusCodes.Conflict, null);
                }

                var oldCount = existing.StackCount;
                existing.StackCount++;
                // Optionally refresh duration on stack
                if (body.DurationOverrideSeconds.HasValue)
                {
                    existing.ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(body.DurationOverrideSeconds.Value);
                }
                await InstanceStore.SaveAsync(
                    InstanceIdKey(existing.StatusInstanceId), existing, cancellationToken: cancellationToken);
                await InvalidateActiveCacheAsync(body.EntityId, body.EntityType, cancellationToken);

                await _messageBus.TryPublishAsync(
                    "status.stacked",
                    new StatusStackedEvent
                    {
                        EventId = Guid.NewGuid(),
                        Timestamp = DateTimeOffset.UtcNow,
                        EntityId = body.EntityId,
                        EntityType = body.EntityType,
                        StatusTemplateCode = body.StatusTemplateCode,
                        StatusInstanceId = existing.StatusInstanceId,
                        OldStackCount = oldCount,
                        NewStackCount = existing.StackCount
                    },
                    cancellationToken: cancellationToken);

                // Push client event for stack increase
                await PublishStatusClientEventAsync(body.EntityType, body.EntityId,
                    new StatusEffectChangedClientEvent
                    {
                        EventName = "status.effect-changed",
                        EventId = Guid.NewGuid(),
                        Timestamp = DateTimeOffset.UtcNow,
                        ChangeType = StatusChangeType.Stacked,
                        EntityId = body.EntityId,
                        EntityType = body.EntityType,
                        StatusTemplateCode = body.StatusTemplateCode,
                        StatusInstanceId = existing.StatusInstanceId,
                        Category = template.Category,
                        StackCount = existing.StackCount,
                        ExpiresAt = existing.ExpiresAt
                    }, cancellationToken);

                return (StatusCodes.OK, new GrantStatusResponse
                {
                    StatusInstanceId = existing.StatusInstanceId,
                    StatusTemplateCode = body.StatusTemplateCode,
                    StackCount = existing.StackCount,
                    ContractInstanceId = existing.ContractInstanceId,
                    ItemInstanceId = existing.ItemInstanceId,
                    GrantedAt = existing.GrantedAt,
                    ExpiresAt = existing.ExpiresAt,
                    GrantResult = GrantResult.Stacked
                });

            case StackBehavior.Independent:
                if (existingInstances.Count >= effectiveMaxStacks)
                {
                    await PublishGrantFailedEventAsync(
                        body, GrantFailureReason.StackLimitReached, existing.StatusInstanceId, cancellationToken);
                    return (StatusCodes.Conflict, null);
                }
                // Create a new independent instance
                return await CreateNewStatusInstanceAsync(body, template, cancellationToken);

            default:
                _logger.LogError(
                    "Unknown stack behavior {StackBehavior} for template {Code}",
                    template.StackBehavior, template.Code);
                return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Creates a new status instance with backing item and optional contract.
    /// </summary>
    private async Task<(StatusCodes, GrantStatusResponse?)> CreateNewStatusInstanceAsync(
        GrantStatusRequest body,
        StatusTemplateModel template,
        CancellationToken cancellationToken)
    {
        // Get or create container for this entity
        var container = await GetOrCreateContainerAsync(
            body.EntityId, body.EntityType, body.GameServiceId, cancellationToken);

        if (container == null)
        {
            await PublishGrantFailedEventAsync(body, GrantFailureReason.ItemCreationFailed, null, cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }

        // Create item instance in the container
        Guid itemInstanceId;
        try
        {
            var itemResponse = await _itemClient.CreateItemInstanceAsync(
                new CreateItemInstanceRequest
                {
                    TemplateId = template.ItemTemplateId,
                    ContainerId = container.ContainerId,
                    // RealmId is required by Item for partitioning;
                    // using GameServiceId as the partition key per Collection pattern
                    RealmId = body.GameServiceId,
                    Quantity = 1
                },
                cancellationToken);
            itemInstanceId = itemResponse.InstanceId;
        }
        catch (ApiException ex)
        {
            _logger.LogError(ex, "Failed to create item instance for status grant");
            await PublishGrantFailedEventAsync(
                body, GrantFailureReason.ItemCreationFailed, null, cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }

        // Create contract instance if template specifies one
        Guid? contractInstanceId = null;
        if (template.ContractTemplateId.HasValue)
        {
            try
            {
                var contractResponse = await _contractClient.CreateContractInstanceAsync(
                    new CreateContractInstanceRequest
                    {
                        TemplateId = template.ContractTemplateId.Value,
                        Parties = new List<ContractPartyInput>
                        {
                            new ContractPartyInput
                            {
                                EntityId = body.EntityId,
                                EntityType = EntityType.Character,
                                Role = "subject"
                            }
                        }
                    },
                    cancellationToken);
                contractInstanceId = contractResponse.ContractId;
            }
            catch (ApiException ex)
            {
                _logger.LogError(ex, "Failed to create contract for status grant, compensating");
                // Saga compensation: delete the item we just created
                try
                {
                    await _itemClient.DestroyItemInstanceAsync(
                        new DestroyItemInstanceRequest { InstanceId = itemInstanceId, Reason = DestroyReason.Destroyed },
                        cancellationToken);
                }
                catch (ApiException deleteEx)
                {
                    _logger.LogError(deleteEx,
                        "Failed to delete item {ItemInstanceId} during contract failure compensation",
                        itemInstanceId);
                }
                await PublishGrantFailedEventAsync(
                    body, GrantFailureReason.ContractFailed, null, cancellationToken);
                return (StatusCodes.InternalServerError, null);
            }
        }

        var now = DateTimeOffset.UtcNow;
        var instanceId = Guid.NewGuid();
        var expiresAt = CalculateExpiry(body, template);

        var instance = new StatusInstanceModel
        {
            StatusInstanceId = instanceId,
            EntityId = body.EntityId,
            EntityType = body.EntityType,
            GameServiceId = body.GameServiceId,
            StatusTemplateCode = body.StatusTemplateCode,
            Category = template.Category,
            StackCount = 1,
            SourceId = body.SourceId,
            ContractInstanceId = contractInstanceId,
            ItemInstanceId = itemInstanceId,
            GrantedAt = now,
            ExpiresAt = expiresAt,
            Metadata = body.Metadata as Dictionary<string, object>
        };

        // Save instance
        await InstanceStore.SaveAsync(InstanceIdKey(instanceId), instance, cancellationToken: cancellationToken);

        // Invalidate active cache
        await InvalidateActiveCacheAsync(body.EntityId, body.EntityType, cancellationToken);

        // Publish granted event
        await _messageBus.TryPublishAsync(
            "status.granted",
            new StatusGrantedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = now,
                EntityId = body.EntityId,
                EntityType = body.EntityType,
                StatusTemplateCode = body.StatusTemplateCode,
                StatusInstanceId = instanceId,
                Category = template.Category,
                StackCount = 1,
                SourceId = body.SourceId,
                ExpiresAt = expiresAt,
                GrantResult = GrantResult.Granted
            },
            cancellationToken: cancellationToken);

        // Push client event to sessions observing this entity
        await PublishStatusClientEventAsync(body.EntityType, body.EntityId,
            new StatusEffectChangedClientEvent
            {
                EventName = "status.effect-changed",
                EventId = Guid.NewGuid(),
                Timestamp = now,
                ChangeType = StatusChangeType.Granted,
                EntityId = body.EntityId,
                EntityType = body.EntityType,
                StatusTemplateCode = body.StatusTemplateCode,
                StatusInstanceId = instanceId,
                Category = template.Category,
                StackCount = 1,
                ExpiresAt = expiresAt,
                SourceId = body.SourceId
            }, cancellationToken);

        _logger.LogInformation(
            "Granted status {Code} to {EntityType} {EntityId} (instance {InstanceId})",
            body.StatusTemplateCode, body.EntityType, body.EntityId, instanceId);

        return (StatusCodes.OK, new GrantStatusResponse
        {
            StatusInstanceId = instanceId,
            StatusTemplateCode = body.StatusTemplateCode,
            StackCount = 1,
            ContractInstanceId = contractInstanceId,
            ItemInstanceId = itemInstanceId,
            GrantedAt = now,
            ExpiresAt = expiresAt,
            GrantResult = GrantResult.Granted
        });
    }

    /// <summary>
    /// Gets or creates an inventory container for an entity's status effects.
    /// </summary>
    private async Task<StatusContainerModel?> GetOrCreateContainerAsync(
        Guid entityId, EntityType entityType, Guid gameServiceId, CancellationToken cancellationToken)
    {
        var entityKey = ContainerEntityKey(entityId, entityType, gameServiceId);
        var existing = await ContainerStore.GetAsync(entityKey, cancellationToken);
        if (existing != null)
        {
            return existing;
        }

        // Create container via inventory service
        // ContainerOwnerType is an enum; status containers use Other for polymorphic entity support
        ContainerResponse containerResponse;
        try
        {
            containerResponse = await _inventoryClient.CreateContainerAsync(
                new CreateContainerRequest
                {
                    OwnerId = entityId,
                    OwnerType = ContainerOwnerType.Other,
                    ContainerType = "status_effects",
                    ConstraintModel = ContainerConstraintModel.Unlimited
                },
                cancellationToken);
        }
        catch (ApiException ex)
        {
            _logger.LogError(ex,
                "Failed to create status container for {EntityType} {EntityId} via inventory service",
                entityType, entityId);
            await _messageBus.TryPublishErrorAsync(
                "status", "create-container", "InventoryError",
                $"Failed to create status container for {entityType} {entityId}",
                dependency: "inventory",
                cancellationToken: cancellationToken);
            return null;
        }

        var container = new StatusContainerModel
        {
            ContainerId = containerResponse.ContainerId,
            EntityId = entityId,
            EntityType = entityType,
            GameServiceId = gameServiceId
        };

        // Save with dual keys
        await ContainerStore.SaveAsync(ContainerIdKey(container.ContainerId), container, cancellationToken: cancellationToken);
        await ContainerStore.SaveAsync(entityKey, container, cancellationToken: cancellationToken);

        return container;
    }

    /// <summary>
    /// Removes a status instance: deletes backing item, cancels contract, deletes record, invalidates cache.
    /// </summary>
    private async Task RemoveInstanceInternalAsync(
        StatusInstanceModel instance, StatusRemoveReason reason, CancellationToken cancellationToken)
    {
        // Delete backing item
        try
        {
            await _itemClient.DestroyItemInstanceAsync(
                new DestroyItemInstanceRequest { InstanceId = instance.ItemInstanceId, Reason = DestroyReason.Destroyed },
                cancellationToken);
        }
        catch (ApiException ex)
        {
            _logger.LogWarning(ex,
                "Failed to delete item instance {ItemInstanceId} for status removal",
                instance.ItemInstanceId);
        }

        // Cancel contract if exists
        if (instance.ContractInstanceId.HasValue)
        {
            try
            {
                await _contractClient.TerminateContractInstanceAsync(
                    new TerminateContractInstanceRequest
                    {
                        ContractId = instance.ContractInstanceId.Value,
                        RequestingEntityId = instance.EntityId,
                        RequestingEntityType = EntityType.Character,
                        Reason = "status-removed"
                    },
                    cancellationToken);
            }
            catch (ApiException ex)
            {
                _logger.LogWarning(ex,
                    "Failed to terminate contract {ContractInstanceId} for status removal",
                    instance.ContractInstanceId.Value);
            }
        }

        // Delete instance record
        await InstanceStore.DeleteAsync(InstanceIdKey(instance.StatusInstanceId), cancellationToken);

        // Invalidate active cache
        await InvalidateActiveCacheAsync(instance.EntityId, instance.EntityType, cancellationToken);

        // Publish removed event
        await _messageBus.TryPublishAsync(
            "status.removed",
            new StatusRemovedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                EntityId = instance.EntityId,
                EntityType = instance.EntityType,
                StatusTemplateCode = instance.StatusTemplateCode,
                StatusInstanceId = instance.StatusInstanceId,
                Reason = reason
            },
            cancellationToken: cancellationToken);

        // Push client event to sessions observing this entity
        await PublishStatusClientEventAsync(instance.EntityType, instance.EntityId,
            new StatusEffectChangedClientEvent
            {
                EventName = "status.effect-changed",
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                ChangeType = reason == StatusRemoveReason.Cleansed
                    ? StatusChangeType.Cleansed
                    : StatusChangeType.Removed,
                EntityId = instance.EntityId,
                EntityType = instance.EntityType,
                StatusTemplateCode = instance.StatusTemplateCode,
                StatusInstanceId = instance.StatusInstanceId,
                Category = instance.Category
            }, cancellationToken);
    }

    /// <summary>
    /// Calculates the expiration time for a new or refreshed status.
    /// </summary>
    private DateTimeOffset? CalculateExpiry(GrantStatusRequest body, StatusTemplateModel template)
    {
        if (body.DurationOverrideSeconds.HasValue)
        {
            return DateTimeOffset.UtcNow.AddSeconds(body.DurationOverrideSeconds.Value);
        }

        if (template.DefaultDurationSeconds.HasValue)
        {
            return DateTimeOffset.UtcNow.AddSeconds(template.DefaultDurationSeconds.Value);
        }

        // If contract-managed (has ContractTemplateId), expiry is null (contract controls lifecycle)
        if (template.ContractTemplateId.HasValue)
        {
            return null;
        }

        // No explicit duration and no contract: use config default
        return DateTimeOffset.UtcNow.AddSeconds(_configuration.DefaultStatusDurationSeconds);
    }

    /// <summary>
    /// Publishes a status client event to all sessions observing the affected entity.
    /// Uses the entity's own type for routing so sessions watching a character
    /// receive status updates alongside inventory/collection changes.
    /// </summary>
    private async Task PublishStatusClientEventAsync(
        EntityType entityType,
        Guid entityId,
        StatusEffectChangedClientEvent clientEvent,
        CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.status", "StatusService.PublishStatusClientEventAsync");
        await _entitySessionRegistry.PublishToEntitySessionsAsync(
            entityType.ToString().ToLowerInvariant(), entityId, clientEvent, ct);
    }

    /// <summary>
    /// Gets the active status cache for an entity, building it from MySQL on cache miss.
    /// Filters out expired statuses during rebuild and publishes expiration events.
    /// </summary>
    private async Task<ActiveStatusCacheModel> GetOrBuildActiveCacheAsync(
        Guid entityId, EntityType entityType, CancellationToken cancellationToken)
    {
        var cacheKey = ActiveCacheKey(entityId, entityType);
        var cached = await ActiveCacheStore.GetAsync(cacheKey, cancellationToken);
        if (cached != null)
        {
            return cached;
        }

        // Build from MySQL
        var conditions = new List<QueryCondition>
        {
            new QueryCondition { Path = "$.StatusInstanceId", Operator = QueryOperator.Exists, Value = true },
            new QueryCondition { Path = "$.EntityId", Operator = QueryOperator.Equals, Value = entityId },
            new QueryCondition { Path = "$.EntityType", Operator = QueryOperator.Equals, Value = entityType }
        };
        var result = await InstanceQueryStore.JsonQueryPagedAsync(
            conditions, 0, _configuration.MaxStatusesPerEntity, null, cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var activeStatuses = new List<CachedStatusEntry>();

        foreach (var entry in result.Items)
        {
            var instance = entry.Value;

            // Lazy expiration: clean up expired statuses found during rebuild
            if (instance.ExpiresAt.HasValue && instance.ExpiresAt.Value <= now)
            {
                await _messageBus.TryPublishAsync(
                    "status.expired",
                    new StatusExpiredEvent
                    {
                        EventId = Guid.NewGuid(),
                        Timestamp = now,
                        EntityId = instance.EntityId,
                        EntityType = instance.EntityType,
                        StatusTemplateCode = instance.StatusTemplateCode,
                        StatusInstanceId = instance.StatusInstanceId
                    },
                    cancellationToken: cancellationToken);

                // Push client event for expiration
                await PublishStatusClientEventAsync(instance.EntityType, instance.EntityId,
                    new StatusEffectChangedClientEvent
                    {
                        EventName = "status.effect-changed",
                        EventId = Guid.NewGuid(),
                        Timestamp = now,
                        ChangeType = StatusChangeType.Expired,
                        EntityId = instance.EntityId,
                        EntityType = instance.EntityType,
                        StatusTemplateCode = instance.StatusTemplateCode,
                        StatusInstanceId = instance.StatusInstanceId,
                        Category = instance.Category
                    }, cancellationToken);

                // Remove expired instance (fire-and-forget the item deletion)
                await InstanceStore.DeleteAsync(
                    InstanceIdKey(instance.StatusInstanceId), cancellationToken);
                continue;
            }

            activeStatuses.Add(new CachedStatusEntry
            {
                StatusInstanceId = instance.StatusInstanceId,
                StatusTemplateCode = instance.StatusTemplateCode,
                Category = instance.Category,
                StackCount = instance.StackCount,
                SourceId = instance.SourceId,
                ExpiresAt = instance.ExpiresAt
            });
        }

        var cache = new ActiveStatusCacheModel
        {
            EntityId = entityId,
            EntityType = entityType,
            Statuses = activeStatuses,
            CachedAt = now
        };

        // Save to cache with TTL; MaxCachedEntities is enforced by Redis eviction policy
        await ActiveCacheStore.SaveAsync(cacheKey, cache,
            new StateOptions { Ttl = _configuration.StatusCacheTtlSeconds },
            cancellationToken);

        return cache;
    }

    /// <summary>
    /// Gets the seed effects cache for an entity, building it from the Seed service on cache miss.
    /// </summary>
    private async Task<SeedEffectsCacheModel> GetOrBuildSeedEffectsCacheAsync(
        Guid entityId, EntityType entityType, CancellationToken cancellationToken)
    {
        var cacheKey = SeedEffectsCacheKey(entityId, entityType);
        var cached = await SeedEffectsCacheStore.GetAsync(cacheKey, cancellationToken);
        if (cached != null)
        {
            return cached;
        }

        // Build from Seed service
        var effects = new List<CachedSeedEffect>();
        var seedClient = _serviceProvider.GetService<ISeedClient>();

        if (seedClient != null)
        {
            try
            {
                var seedsResponse = await seedClient.GetSeedsByOwnerAsync(
                    new GetSeedsByOwnerRequest
                    {
                        OwnerId = entityId,
                        OwnerType = entityType
                    },
                    cancellationToken);

                foreach (var seed in seedsResponse.Seeds)
                {
                    var capsResponse = await seedClient.GetCapabilityManifestAsync(
                        new GetCapabilityManifestRequest { SeedId = seed.SeedId },
                        cancellationToken);

                    foreach (var cap in capsResponse.Capabilities)
                    {
                        effects.Add(new CachedSeedEffect
                        {
                            CapabilityCode = cap.CapabilityCode,
                            Domain = cap.Domain,
                            Fidelity = cap.Fidelity,
                            SeedId = seed.SeedId,
                            SeedTypeCode = seed.SeedTypeCode
                        });
                    }
                }
            }
            catch (ApiException ex)
            {
                _logger.LogWarning(ex,
                    "Failed to fetch seed capabilities for {EntityType} {EntityId}",
                    entityType, entityId);
            }
        }

        var cache = new SeedEffectsCacheModel
        {
            EntityId = entityId,
            EntityType = entityType,
            Effects = effects,
            CachedAt = DateTimeOffset.UtcNow
        };

        // Save to cache with TTL
        await SeedEffectsCacheStore.SaveAsync(cacheKey, cache,
            new StateOptions { Ttl = _configuration.SeedEffectsCacheTtlSeconds },
            cancellationToken);

        return cache;
    }

    /// <summary>
    /// Invalidates the active status cache for an entity.
    /// </summary>
    private async Task InvalidateActiveCacheAsync(
        Guid entityId, EntityType entityType, CancellationToken cancellationToken)
    {
        try
        {
            await ActiveCacheStore.DeleteAsync(
                ActiveCacheKey(entityId, entityType), cancellationToken);
        }
        catch (Exception ex)
        {
            // Cache invalidation failure is non-fatal; cache will expire via TTL
            _logger.LogWarning(ex,
                "Failed to invalidate active cache for {EntityType} {EntityId}",
                entityType, entityId);
            await _messageBus.TryPublishErrorAsync(
                "status", "invalidate-active-cache", "CacheError",
                $"Failed to invalidate active cache for {entityType} {entityId}",
                dependency: "state",
                severity: ServiceErrorEventSeverity.Warning,
                cancellationToken: cancellationToken);
        }
    }

    /// <summary>
    /// Invalidates the seed effects cache for an entity.
    /// </summary>
    private async Task InvalidateSeedEffectsCacheAsync(
        Guid entityId, EntityType entityType, CancellationToken cancellationToken)
    {
        try
        {
            await SeedEffectsCacheStore.DeleteAsync(
                SeedEffectsCacheKey(entityId, entityType), cancellationToken);
        }
        catch (Exception ex)
        {
            // Cache invalidation failure is non-fatal; cache will expire via TTL
            _logger.LogWarning(ex,
                "Failed to invalidate seed effects cache for {EntityType} {EntityId}",
                entityType, entityId);
            await _messageBus.TryPublishErrorAsync(
                "status", "invalidate-seed-effects-cache", "CacheError",
                $"Failed to invalidate seed effects cache for {entityType} {entityId}",
                dependency: "state",
                severity: ServiceErrorEventSeverity.Warning,
                cancellationToken: cancellationToken);
        }
    }

    /// <summary>
    /// Publishes a StatusGrantFailedEvent for tracking and diagnostics.
    /// </summary>
    private async Task PublishGrantFailedEventAsync(
        GrantStatusRequest body, GrantFailureReason reason,
        Guid? existingStatusInstanceId, CancellationToken cancellationToken)
    {
        await _messageBus.TryPublishAsync(
            "status.grant-failed",
            new StatusGrantFailedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                EntityId = body.EntityId,
                EntityType = body.EntityType,
                StatusTemplateCode = body.StatusTemplateCode,
                Reason = reason,
                ExistingStatusInstanceId = existingStatusInstanceId
            },
            cancellationToken: cancellationToken);
    }

    #region Response Builders

    private static StatusTemplateResponse ToTemplateResponse(StatusTemplateModel model) => new()
    {
        StatusTemplateId = model.StatusTemplateId,
        GameServiceId = model.GameServiceId,
        Code = model.Code,
        DisplayName = model.DisplayName,
        Description = model.Description,
        Category = model.Category,
        Stackable = model.Stackable,
        MaxStacks = model.MaxStacks,
        StackBehavior = model.StackBehavior,
        ContractTemplateId = model.ContractTemplateId,
        ItemTemplateId = model.ItemTemplateId,
        DefaultDurationSeconds = model.DefaultDurationSeconds,
        IconAssetId = model.IconAssetId,
        CreatedAt = model.CreatedAt,
        UpdatedAt = model.UpdatedAt
    };

    private static StatusInstanceResponse ToInstanceResponse(StatusInstanceModel model) => new()
    {
        StatusInstanceId = model.StatusInstanceId,
        EntityId = model.EntityId,
        EntityType = model.EntityType,
        StatusTemplateCode = model.StatusTemplateCode,
        Category = model.Category,
        StackCount = model.StackCount,
        SourceId = model.SourceId,
        ContractInstanceId = model.ContractInstanceId,
        ItemInstanceId = model.ItemInstanceId,
        GrantedAt = model.GrantedAt,
        ExpiresAt = model.ExpiresAt,
        Metadata = model.Metadata
    };

    #endregion
}
