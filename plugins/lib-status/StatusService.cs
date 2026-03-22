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
using BeyondImmersion.BannouService.Resource;
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
///   <item><b>Configuration:</b> All config properties are wired up.</item>
///   <item><b>Events:</b> All state changes publish typed events.</item>
///   <item><b>Cache Stores:</b> Redis caches with TTL, invalidated on mutation, rebuilt on miss.</item>
///   <item><b>Concurrency:</b> Distributed locks via IDistributedLockProvider for entity mutations.</item>
/// </list>
/// </para>
/// </remarks>
[BannouService("status", typeof(IStatusService), lifetime: ServiceLifetime.Scoped, layer: ServiceLayer.GameFeatures)]
public partial class StatusService : IStatusService, IAccountDeletionCleanupRequired, IDeprecateAndMergeEntity
{
    private readonly IMessageBus _messageBus;
    private readonly ILogger<StatusService> _logger;
    private readonly StatusServiceConfiguration _configuration;
    private readonly IInventoryClient _inventoryClient;
    private readonly IItemClient _itemClient;
    private readonly IContractClient _contractClient;
    private readonly IGameServiceClient _gameServiceClient;
    private readonly IDistributedLockProvider _lockProvider;
    private readonly IResourceClient _resourceClient;
    private readonly ISeedClient _seedClient;
    private readonly IServiceProvider _serviceProvider;
    private readonly IEntitySessionRegistry _entitySessionRegistry;
    private readonly ITelemetryProvider _telemetryProvider;

    #region State Store Fields

    /// <summary>Durable store for status effect template definitions (MySQL).</summary>
    private readonly IStateStore<StatusTemplateModel> _templateStore;

    /// <summary>Queryable store for status template lookups by field (MySQL JSON path).</summary>
    private readonly IJsonQueryableStateStore<StatusTemplateModel> _templateQueryStore;

    /// <summary>Durable store for active status effect instances (MySQL).</summary>
    private readonly IStateStore<StatusInstanceModel> _instanceStore;

    /// <summary>Queryable store for status instance lookups by field (MySQL JSON path).</summary>
    private readonly IJsonQueryableStateStore<StatusInstanceModel> _instanceQueryStore;

    /// <summary>Durable store for per-entity status containers (MySQL).</summary>
    private readonly IStateStore<StatusContainerModel> _containerStore;

    /// <summary>Queryable store for status container lookups by field (MySQL JSON path).</summary>
    private readonly IJsonQueryableStateStore<StatusContainerModel> _containerQueryStore;

    /// <summary>Ephemeral cache for aggregated active status effects per entity (Redis).</summary>
    private readonly IStateStore<ActiveStatusCacheModel> _activeCacheStore;

    /// <summary>Ephemeral cache for seed-derived passive capability effects per entity (Redis).</summary>
    private readonly IStateStore<SeedEffectsCacheModel> _seedEffectsCacheStore;

    #endregion

    #region Key Building

    private const string TEMPLATE_KEY_PREFIX = "tpl";
    private const string INSTANCE_KEY_PREFIX = "inst";
    private const string CONTAINER_KEY_PREFIX = "ctr";
    private const string ACTIVE_CACHE_KEY_PREFIX = "active";
    private const string SEED_EFFECTS_CACHE_KEY_PREFIX = "seed";
    private const string ENTITY_LOCK_KEY_PREFIX = "entity";

    internal static string BuildTemplateIdKey(Guid templateId) => $"{TEMPLATE_KEY_PREFIX}:{templateId}";
    internal static string BuildTemplateCodeKey(Guid gameServiceId, string code) => $"{TEMPLATE_KEY_PREFIX}:{gameServiceId}:{code}";
    internal static string BuildInstanceIdKey(Guid instanceId) => $"{INSTANCE_KEY_PREFIX}:{instanceId}";
    internal static string BuildContainerIdKey(Guid containerId) => $"{CONTAINER_KEY_PREFIX}:{containerId}";
    internal static string BuildContainerEntityKey(Guid entityId, EntityType entityType, Guid gameServiceId) =>
        $"{CONTAINER_KEY_PREFIX}:{entityId}:{entityType}:{gameServiceId}";
    internal static string BuildActiveCacheKey(Guid entityId, EntityType entityType) => $"{ACTIVE_CACHE_KEY_PREFIX}:{entityId}:{entityType}";
    internal static string BuildSeedEffectsCacheKey(Guid entityId, EntityType entityType) => $"{SEED_EFFECTS_CACHE_KEY_PREFIX}:{entityId}:{entityType}";
    internal static string BuildEntityLockKey(EntityType entityType, Guid entityId) => $"{ENTITY_LOCK_KEY_PREFIX}:{entityType}:{entityId}";

    /// <summary>
    /// Maps an EntityType to ContainerOwnerType for inventory operations.
    /// Name-matched values (Character, Account, Location, Guild) map directly;
    /// all others fall back to Other. Uses shared EnumMapping helper per IMPLEMENTATION TENETS.
    /// </summary>
    internal static ContainerOwnerType MapToContainerOwnerType(EntityType entityType) =>
        entityType.MapByNameOrDefault(ContainerOwnerType.Other);

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
        IResourceClient resourceClient,
        ISeedClient seedClient,
        IServiceProvider serviceProvider,
        IEntitySessionRegistry entitySessionRegistry,
        ITelemetryProvider telemetryProvider)
    {
        _messageBus = messageBus;
        _logger = logger;
        _configuration = configuration;
        _inventoryClient = inventoryClient;
        _itemClient = itemClient;
        _contractClient = contractClient;
        _gameServiceClient = gameServiceClient;
        _lockProvider = lockProvider;
        _resourceClient = resourceClient;
        _seedClient = seedClient;
        _serviceProvider = serviceProvider;
        _entitySessionRegistry = entitySessionRegistry;
        _telemetryProvider = telemetryProvider;

        // Constructor-cache all state stores per FOUNDATION TENETS
        _templateStore = stateStoreFactory.GetStore<StatusTemplateModel>(StateStoreDefinitions.StatusTemplates);
        _templateQueryStore = stateStoreFactory.GetJsonQueryableStore<StatusTemplateModel>(StateStoreDefinitions.StatusTemplates);
        _instanceStore = stateStoreFactory.GetStore<StatusInstanceModel>(StateStoreDefinitions.StatusInstances);
        _instanceQueryStore = stateStoreFactory.GetJsonQueryableStore<StatusInstanceModel>(StateStoreDefinitions.StatusInstances);
        _containerStore = stateStoreFactory.GetStore<StatusContainerModel>(StateStoreDefinitions.StatusContainers);
        _containerQueryStore = stateStoreFactory.GetJsonQueryableStore<StatusContainerModel>(StateStoreDefinitions.StatusContainers);
        _activeCacheStore = stateStoreFactory.GetStore<ActiveStatusCacheModel>(StateStoreDefinitions.StatusActiveCache);
        _seedEffectsCacheStore = stateStoreFactory.GetStore<SeedEffectsCacheModel>(StateStoreDefinitions.StatusSeedEffectsCache);

        RegisterEventConsumers(eventConsumer);
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
        var existing = await _templateQueryStore.JsonQueryPagedAsync(
            countConditions, 0, 1, null, cancellationToken);
        if (existing.TotalCount >= _configuration.MaxStatusTemplatesPerGameService)
        {
            _logger.LogWarning(
                "Game service {GameServiceId} has reached template limit {Limit}",
                body.GameServiceId, _configuration.MaxStatusTemplatesPerGameService);
            return (StatusCodes.Conflict, null);
        }

        // Check code uniqueness
        var codeKey = BuildTemplateCodeKey(body.GameServiceId, body.Code);
        var existingByCode = await _templateStore.GetAsync(codeKey, cancellationToken);
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
        await _templateStore.SaveAsync(BuildTemplateIdKey(templateId), model, cancellationToken: cancellationToken);
        await _templateStore.SaveAsync(codeKey, model, cancellationToken: cancellationToken);

        // Publish lifecycle event
        await _messageBus.PublishStatusTemplateCreatedAsync(
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
                IsDeprecated = false,
                CreatedAt = now
            },
            cancellationToken);

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
        var template = await _templateStore.GetAsync(
            BuildTemplateIdKey(body.StatusTemplateId), cancellationToken);

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
        var template = await _templateStore.GetAsync(
            BuildTemplateCodeKey(body.GameServiceId, body.Code), cancellationToken);

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
                Value = body.Category.Value
            });
        }

        // Filter out deprecated templates by default per IMPLEMENTATION TENETS
        if (!body.IncludeDeprecated)
        {
            conditions.Add(new QueryCondition
            {
                Path = "$.IsDeprecated",
                Operator = QueryOperator.Equals,
                Value = false
            });
        }

        var pageSize = body.PageSize > 0 ? body.PageSize : _configuration.DefaultPageSize;
        var offset = (body.Page - 1) * pageSize;

        var result = await _templateQueryStore.JsonQueryPagedAsync(
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
        using var lockCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        lockCts.CancelAfter(TimeSpan.FromSeconds(_configuration.LockAcquisitionTimeoutSeconds));

        await using var lockHandle = await _lockProvider.LockAsync(
            StateStoreDefinitions.StatusLock,
            BuildTemplateIdKey(body.StatusTemplateId),
            Guid.NewGuid().ToString(),
            _configuration.LockTimeoutSeconds,
            lockCts.Token);

        if (!lockHandle.Success)
        {
            _logger.LogWarning("Failed to acquire lock for template {TemplateId}", body.StatusTemplateId);
            return (StatusCodes.Conflict, null);
        }

        var template = await _templateStore.GetAsync(
            BuildTemplateIdKey(body.StatusTemplateId), cancellationToken);

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
        await _templateStore.SaveAsync(BuildTemplateIdKey(template.StatusTemplateId), template, cancellationToken: cancellationToken);
        await _templateStore.SaveAsync(
            BuildTemplateCodeKey(template.GameServiceId, template.Code), template, cancellationToken: cancellationToken);

        // Publish lifecycle event
        await _messageBus.PublishStatusTemplateUpdatedAsync(
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
                IsDeprecated = template.IsDeprecated,
                DeprecatedAt = template.DeprecatedAt,
                DeprecationReason = template.DeprecationReason,
                CreatedAt = template.CreatedAt,
                UpdatedAt = template.UpdatedAt ?? template.CreatedAt,
                ChangedFields = changedFields
            },
            cancellationToken);

        _logger.LogInformation(
            "Updated status template {TemplateId} fields: {Fields}",
            template.StatusTemplateId, string.Join(", ", changedFields));

        return (StatusCodes.OK, ToTemplateResponse(template));
    }

    /// <summary>
    /// Marks a status template as deprecated. Idempotent per IMPLEMENTATION TENETS.
    /// Deprecated templates cannot grant new instances but existing instances remain active.
    /// </summary>
    public async Task<(StatusCodes, StatusTemplateResponse?)> DeprecateStatusTemplateAsync(
        DeprecateStatusTemplateRequest body, CancellationToken cancellationToken)
    {
        using var lockCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        lockCts.CancelAfter(TimeSpan.FromSeconds(_configuration.LockAcquisitionTimeoutSeconds));

        await using var lockHandle = await _lockProvider.LockAsync(
            StateStoreDefinitions.StatusLock,
            BuildTemplateIdKey(body.StatusTemplateId),
            Guid.NewGuid().ToString(),
            _configuration.LockTimeoutSeconds,
            lockCts.Token);

        if (!lockHandle.Success)
        {
            _logger.LogWarning("Failed to acquire lock for template {TemplateId}", body.StatusTemplateId);
            return (StatusCodes.Conflict, null);
        }

        var template = await _templateStore.GetAsync(
            BuildTemplateIdKey(body.StatusTemplateId), cancellationToken);

        if (template == null)
        {
            return (StatusCodes.NotFound, null);
        }

        // Idempotent: already deprecated returns OK
        if (template.IsDeprecated)
        {
            return (StatusCodes.OK, ToTemplateResponse(template));
        }

        template.IsDeprecated = true;
        template.DeprecatedAt = DateTimeOffset.UtcNow;
        template.DeprecationReason = body.Reason;
        template.UpdatedAt = DateTimeOffset.UtcNow;

        await _templateStore.SaveAsync(BuildTemplateIdKey(template.StatusTemplateId), template, cancellationToken: cancellationToken);
        await _templateStore.SaveAsync(
            BuildTemplateCodeKey(template.GameServiceId, template.Code), template, cancellationToken: cancellationToken);

        await _messageBus.PublishStatusTemplateUpdatedAsync(
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
                IsDeprecated = template.IsDeprecated,
                DeprecatedAt = template.DeprecatedAt,
                DeprecationReason = template.DeprecationReason,
                CreatedAt = template.CreatedAt,
                UpdatedAt = template.UpdatedAt ?? template.CreatedAt,
                ChangedFields = new List<string> { "isDeprecated", "deprecatedAt", "deprecationReason" }
            },
            cancellationToken);

        _logger.LogInformation(
            "Deprecated status template {TemplateId} ({Code}): {Reason}",
            template.StatusTemplateId, template.Code, body.Reason);

        return (StatusCodes.OK, ToTemplateResponse(template));
    }

    /// <summary>
    /// Reverses deprecation on a status template. Idempotent per IMPLEMENTATION TENETS.
    /// Category A entities support undeprecation.
    /// </summary>
    public async Task<(StatusCodes, StatusTemplateResponse?)> UndeprecateStatusTemplateAsync(
        UndeprecateStatusTemplateRequest body, CancellationToken cancellationToken)
    {
        using var lockCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        lockCts.CancelAfter(TimeSpan.FromSeconds(_configuration.LockAcquisitionTimeoutSeconds));

        await using var lockHandle = await _lockProvider.LockAsync(
            StateStoreDefinitions.StatusLock,
            BuildTemplateIdKey(body.StatusTemplateId),
            Guid.NewGuid().ToString(),
            _configuration.LockTimeoutSeconds,
            lockCts.Token);

        if (!lockHandle.Success)
        {
            _logger.LogWarning("Failed to acquire lock for template {TemplateId}", body.StatusTemplateId);
            return (StatusCodes.Conflict, null);
        }

        var template = await _templateStore.GetAsync(
            BuildTemplateIdKey(body.StatusTemplateId), cancellationToken);

        if (template == null)
        {
            return (StatusCodes.NotFound, null);
        }

        // Idempotent: not deprecated returns OK
        if (!template.IsDeprecated)
        {
            return (StatusCodes.OK, ToTemplateResponse(template));
        }

        template.IsDeprecated = false;
        template.DeprecatedAt = null;
        template.DeprecationReason = null;
        template.UpdatedAt = DateTimeOffset.UtcNow;

        await _templateStore.SaveAsync(BuildTemplateIdKey(template.StatusTemplateId), template, cancellationToken: cancellationToken);
        await _templateStore.SaveAsync(
            BuildTemplateCodeKey(template.GameServiceId, template.Code), template, cancellationToken: cancellationToken);

        await _messageBus.PublishStatusTemplateUpdatedAsync(
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
                IsDeprecated = template.IsDeprecated,
                CreatedAt = template.CreatedAt,
                UpdatedAt = template.UpdatedAt ?? template.CreatedAt,
                ChangedFields = new List<string> { "isDeprecated", "deprecatedAt", "deprecationReason" }
            },
            cancellationToken);

        _logger.LogInformation(
            "Undeprecated status template {TemplateId} ({Code})",
            template.StatusTemplateId, template.Code);

        return (StatusCodes.OK, ToTemplateResponse(template));
    }

    /// <summary>
    /// Deletes a status template. Requires prior deprecation per IMPLEMENTATION TENETS.
    /// Coordinates cleanup of dependent data via lib-resource.
    /// </summary>
    public async Task<StatusCodes> DeleteStatusTemplateAsync(
        DeleteStatusTemplateRequest body, CancellationToken cancellationToken)
    {
        using var lockCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        lockCts.CancelAfter(TimeSpan.FromSeconds(_configuration.LockAcquisitionTimeoutSeconds));

        await using var lockHandle = await _lockProvider.LockAsync(
            StateStoreDefinitions.StatusLock,
            BuildTemplateIdKey(body.StatusTemplateId),
            Guid.NewGuid().ToString(),
            _configuration.LockTimeoutSeconds,
            lockCts.Token);

        if (!lockHandle.Success)
        {
            _logger.LogWarning("Failed to acquire lock for template {TemplateId}", body.StatusTemplateId);
            return StatusCodes.Conflict;
        }

        var template = await _templateStore.GetAsync(
            BuildTemplateIdKey(body.StatusTemplateId), cancellationToken);

        if (template == null)
        {
            return StatusCodes.NotFound;
        }

        // Must be deprecated before deletion per IMPLEMENTATION TENETS
        if (!template.IsDeprecated)
        {
            _logger.LogWarning(
                "Attempted to delete non-deprecated template {TemplateId}",
                body.StatusTemplateId);
            return StatusCodes.BadRequest;
        }

        // Delete both keys
        await _templateStore.DeleteAsync(BuildTemplateIdKey(template.StatusTemplateId), cancellationToken);
        await _templateStore.DeleteAsync(
            BuildTemplateCodeKey(template.GameServiceId, template.Code), cancellationToken);

        // Publish lifecycle event
        await _messageBus.PublishStatusTemplateDeletedAsync(
            new StatusTemplateDeletedEvent
            {
                StatusTemplateId = template.StatusTemplateId,
                GameServiceId = template.GameServiceId,
                Code = template.Code,
                DisplayName = template.DisplayName,
                Category = template.Category,
                Stackable = template.Stackable,
                MaxStacks = template.MaxStacks,
                StackBehavior = template.StackBehavior,
                IsDeprecated = template.IsDeprecated,
                DeprecatedAt = template.DeprecatedAt,
                DeprecationReason = template.DeprecationReason,
                CreatedAt = template.CreatedAt,
                UpdatedAt = template.UpdatedAt ?? template.CreatedAt
            },
            cancellationToken);

        _logger.LogInformation(
            "Deleted status template {TemplateId} ({Code})",
            template.StatusTemplateId, template.Code);

        return StatusCodes.OK;
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
            try
            {
                var codeKey = BuildTemplateCodeKey(body.GameServiceId, templateReq.Code);
                var existingByCode = await _templateStore.GetAsync(codeKey, cancellationToken);

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

                await _templateStore.SaveAsync(BuildTemplateIdKey(templateId), model, cancellationToken: cancellationToken);
                await _templateStore.SaveAsync(codeKey, model, cancellationToken: cancellationToken);

                await _messageBus.PublishStatusTemplateCreatedAsync(
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
                        IsDeprecated = false,
                        CreatedAt = now
                    },
                    cancellationToken);

                created++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to seed status template {Code} for game service {GameServiceId}",
                    templateReq.Code, body.GameServiceId);
                skipped++;
            }
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
        var template = await _templateStore.GetAsync(
            BuildTemplateCodeKey(body.GameServiceId, body.StatusTemplateCode), cancellationToken);

        if (template == null)
        {
            await PublishGrantFailedEventAsync(body, GrantFailureReason.TemplateNotFound, null, cancellationToken);
            return (StatusCodes.NotFound, null);
        }

        // Reject grants for deprecated templates per IMPLEMENTATION TENETS
        if (template.IsDeprecated)
        {
            _logger.LogWarning(
                "Rejected grant of deprecated template {Code} for {EntityType} {EntityId}",
                body.StatusTemplateCode, body.EntityType, body.EntityId);
            await PublishGrantFailedEventAsync(body, GrantFailureReason.TemplateDeprecated, null, cancellationToken);
            return (StatusCodes.BadRequest, null);
        }

        // Acquire entity lock for mutation safety
        using var lockCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        lockCts.CancelAfter(TimeSpan.FromSeconds(_configuration.LockAcquisitionTimeoutSeconds));

        await using var lockHandle = await _lockProvider.LockAsync(
            StateStoreDefinitions.StatusLock,
            BuildEntityLockKey(body.EntityType, body.EntityId),
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
        var existingResult = await _instanceQueryStore.JsonQueryPagedAsync(
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
        var entityCount = await _instanceQueryStore.JsonQueryPagedAsync(
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
        var instance = await _instanceStore.GetAsync(
            BuildInstanceIdKey(body.StatusInstanceId), cancellationToken);

        if (instance == null)
        {
            return (StatusCodes.NotFound, null);
        }

        // Acquire distributed lock per IMPLEMENTATION TENETS (multi-instance safety)
        using var lockCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        lockCts.CancelAfter(TimeSpan.FromSeconds(_configuration.LockAcquisitionTimeoutSeconds));

        await using var lockHandle = await _lockProvider.LockAsync(
            StateStoreDefinitions.StatusLock,
            BuildEntityLockKey(instance.EntityType, instance.EntityId),
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
            BuildEntityLockKey(body.EntityType, body.EntityId),
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

        var result = await _instanceQueryStore.JsonQueryPagedAsync(
            conditions, 0, _configuration.MaxStatusesPerEntity, null, cancellationToken);

        var removed = 0;
        foreach (var entry in result.Items)
        {
            try
            {
                await RemoveInstanceInternalAsync(entry.Value, StatusRemoveReason.SourceRemoved, cancellationToken);
                removed++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to remove status instance {StatusInstanceId} during source removal",
                    entry.Value.StatusInstanceId);
            }
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
            BuildEntityLockKey(body.EntityType, body.EntityId),
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
            new QueryCondition { Path = "$.Category", Operator = QueryOperator.Equals, Value = body.Category }
        };

        var result = await _instanceQueryStore.JsonQueryPagedAsync(
            conditions, 0, _configuration.MaxStatusesPerEntity, null, cancellationToken);

        var removed = 0;
        foreach (var entry in result.Items)
        {
            try
            {
                await RemoveInstanceInternalAsync(entry.Value, body.Reason, cancellationToken);
                removed++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to remove status instance {StatusInstanceId} during category cleanse",
                    entry.Value.StatusInstanceId);
            }
        }

        // Publish cleansed event for the batch operation
        if (removed > 0)
        {
            await _messageBus.PublishStatusCleansedAsync(
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
                cancellationToken);
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
        var instance = await _instanceStore.GetAsync(
            BuildInstanceIdKey(body.StatusInstanceId), cancellationToken);

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
        var containers = await _containerQueryStore.JsonQueryPagedAsync(
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
            var instances = await _instanceQueryStore.JsonQueryPagedAsync(
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
                await _instanceStore.DeleteAsync(
                    BuildInstanceIdKey(instance.StatusInstanceId), cancellationToken);
                statusesRemoved++;

                // Publish status.removed lifecycle event per FOUNDATION TENETS
                try
                {
                    await _messageBus.PublishStatusRemovedAsync(
                        new StatusRemovedEvent
                        {
                            EventId = Guid.NewGuid(),
                            Timestamp = DateTimeOffset.UtcNow,
                            EntityId = instance.EntityId,
                            EntityType = instance.EntityType,
                            StatusTemplateCode = instance.StatusTemplateCode,
                            StatusInstanceId = instance.StatusInstanceId,
                            Reason = StatusRemoveReason.SourceRemoved
                        },
                        cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Failed to publish status.removed event for instance {StatusInstanceId} during cleanup",
                        instance.StatusInstanceId);
                }
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
            await _containerStore.DeleteAsync(
                BuildContainerIdKey(container.ContainerId), cancellationToken);
            await _containerStore.DeleteAsync(
                BuildContainerEntityKey(container.EntityId, container.EntityType, container.GameServiceId),
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

}
