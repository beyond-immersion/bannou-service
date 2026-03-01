using BeyondImmersion.Bannou.Core;
using BeyondImmersion.Bannou.Inventory.ClientEvents;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Item;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Providers;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Inventory;

/// <summary>
/// Implementation of the Inventory service.
/// Provides container management and item placement operations for games.
/// </summary>
[BannouService("inventory", typeof(IInventoryService), lifetime: ServiceLifetime.Scoped, layer: ServiceLayer.GameFoundation)]
public partial class InventoryService : IInventoryService
{
    private readonly IMessageBus _messageBus;
    private readonly IItemClient _itemClient;
    private readonly IStateStoreFactory _stateStoreFactory;
    private readonly IDistributedLockProvider _lockProvider;
    private readonly ILogger<InventoryService> _logger;
    private readonly InventoryServiceConfiguration _configuration;
    private readonly ITelemetryProvider _telemetryProvider;
    private readonly IEntitySessionRegistry _entitySessionRegistry;
    private readonly WeightContribution _defaultWeightContribution;

    /// <summary>
    /// Suppresses client event publishing during composite operations (e.g., cross-container
    /// move calls Remove+Add internally). The composite operation publishes a single
    /// consolidated client event instead. Safe as instance field because InventoryService
    /// is Scoped (one instance per request) per IMPLEMENTATION TENETS.
    /// </summary>
    private bool _suppressClientEvents;

    // Container store key prefixes
    private const string CONT_PREFIX = "cont:";
    private const string CONT_OWNER_INDEX = "cont-owner:";
    private const string CONT_TYPE_INDEX = "cont-type:";

    /// <summary>
    /// Initializes a new instance of the InventoryService.
    /// </summary>
    public InventoryService(
        IMessageBus messageBus,
        IItemClient itemClient,
        IStateStoreFactory stateStoreFactory,
        IDistributedLockProvider lockProvider,
        ILogger<InventoryService> logger,
        InventoryServiceConfiguration configuration,
        ITelemetryProvider telemetryProvider,
        IEntitySessionRegistry entitySessionRegistry)
    {
        _messageBus = messageBus ?? throw new ArgumentNullException(nameof(messageBus));
        _itemClient = itemClient ?? throw new ArgumentNullException(nameof(itemClient));
        _stateStoreFactory = stateStoreFactory ?? throw new ArgumentNullException(nameof(stateStoreFactory));
        _lockProvider = lockProvider ?? throw new ArgumentNullException(nameof(lockProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _telemetryProvider = telemetryProvider ?? throw new ArgumentNullException(nameof(telemetryProvider));
        _entitySessionRegistry = entitySessionRegistry ?? throw new ArgumentNullException(nameof(entitySessionRegistry));

        // Configuration already provides typed enum (T25 compliant)
        _defaultWeightContribution = _configuration.DefaultWeightContribution;
    }

    #region Container Operations

    /// <inheritdoc/>
    public async Task<(StatusCodes, ContainerResponse?)> CreateContainerAsync(
        CreateContainerRequest body,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Creating container type {ContainerType} for owner {OwnerId}",
            body.ContainerType, body.OwnerId);

        // Input validation
        if (body.MaxSlots.HasValue && body.MaxSlots.Value <= 0)
        {
            _logger.LogDebug("MaxSlots must be positive, got {MaxSlots}", body.MaxSlots.Value);
            return (StatusCodes.BadRequest, null);
        }
        if (body.MaxWeight.HasValue && body.MaxWeight.Value <= 0)
        {
            _logger.LogDebug("MaxWeight must be positive, got {MaxWeight}", body.MaxWeight.Value);
            return (StatusCodes.BadRequest, null);
        }
        if (body.MaxVolume.HasValue && body.MaxVolume.Value <= 0)
        {
            _logger.LogDebug("MaxVolume must be positive, got {MaxVolume}", body.MaxVolume.Value);
            return (StatusCodes.BadRequest, null);
        }
        if (body.GridWidth.HasValue && body.GridWidth.Value <= 0)
        {
            _logger.LogDebug("GridWidth must be positive, got {GridWidth}", body.GridWidth.Value);
            return (StatusCodes.BadRequest, null);
        }
        if (body.GridHeight.HasValue && body.GridHeight.Value <= 0)
        {
            _logger.LogDebug("GridHeight must be positive, got {GridHeight}", body.GridHeight.Value);
            return (StatusCodes.BadRequest, null);
        }

        var containerStore = _stateStoreFactory.GetStore<ContainerModel>(StateStoreDefinitions.InventoryContainerStore);

        var containerId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        // Calculate nesting depth if parent specified
        var nestingDepth = 0;
        if (body.ParentContainerId.HasValue)
        {
            var parent = await containerStore.GetAsync($"{CONT_PREFIX}{body.ParentContainerId}", cancellationToken);
            if (parent is null)
            {
                _logger.LogDebug("Parent container not found: {ParentId}", body.ParentContainerId);
                return (StatusCodes.BadRequest, null);
            }
            nestingDepth = parent.NestingDepth + 1;

            var maxNesting = parent.MaxNestingDepth ?? _configuration.DefaultMaxNestingDepth;
            if (nestingDepth > maxNesting)
            {
                _logger.LogDebug("Max nesting depth exceeded for parent {ParentId}", body.ParentContainerId);
                return (StatusCodes.BadRequest, null);
            }
        }

        // Resolve weight contribution: use config default if not specified (enum default is None)
        var weightContribution = body.WeightContribution;
        if (weightContribution == WeightContribution.None)
        {
            weightContribution = _defaultWeightContribution;
        }

        var model = new ContainerModel
        {
            ContainerId = containerId,
            OwnerId = body.OwnerId,
            OwnerType = body.OwnerType,
            ContainerType = body.ContainerType,
            ConstraintModel = body.ConstraintModel,
            IsEquipmentSlot = body.IsEquipmentSlot,
            EquipmentSlotName = body.EquipmentSlotName,
            MaxSlots = body.MaxSlots ?? _configuration.DefaultMaxSlots,
            MaxWeight = body.MaxWeight ?? _configuration.DefaultMaxWeight,
            GridWidth = body.GridWidth,
            GridHeight = body.GridHeight,
            MaxVolume = body.MaxVolume,
            ParentContainerId = body.ParentContainerId,
            NestingDepth = nestingDepth,
            CanContainContainers = body.CanContainContainers,
            MaxNestingDepth = body.MaxNestingDepth ?? _configuration.DefaultMaxNestingDepth,
            SelfWeight = body.SelfWeight,
            WeightContribution = weightContribution,
            SlotCost = body.SlotCost,
            ParentGridWidth = body.ParentGridWidth,
            ParentGridHeight = body.ParentGridHeight,
            ParentVolume = body.ParentVolume,
            AllowedCategories = body.AllowedCategories?.ToList(),
            ForbiddenCategories = body.ForbiddenCategories?.ToList(),
            AllowedTags = body.AllowedTags?.ToList(),
            RealmId = body.RealmId,
            Tags = body.Tags?.ToList() ?? new List<string>(),
            Metadata = body.Metadata is not null ? BannouJson.Serialize(body.Metadata) : null,
            ContentsWeight = 0,
            UsedSlots = 0,
            CurrentVolume = 0,
            CreatedAt = now
        };

        await containerStore.SaveAsync($"{CONT_PREFIX}{containerId}", model, cancellationToken: cancellationToken);

        // Update Redis cache after MySQL write
        await UpdateContainerCacheAsync($"{CONT_PREFIX}{containerId}", model, cancellationToken);

        var ownerIndexKey = BuildOwnerIndexKey(body.OwnerType, body.OwnerId);
        await AddToListAsync(StateStoreDefinitions.InventoryContainerStore,
            ownerIndexKey, containerId.ToString(), cancellationToken);
        await AddToListAsync(StateStoreDefinitions.InventoryContainerStore,
            $"{CONT_TYPE_INDEX}{body.ContainerType}", containerId.ToString(), cancellationToken);

        await _messageBus.TryPublishAsync("inventory.container.created", new InventoryContainerCreatedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = now,
            ContainerId = containerId,
            OwnerId = body.OwnerId,
            OwnerType = body.OwnerType,
            ContainerType = body.ContainerType,
            ConstraintModel = body.ConstraintModel,
            IsEquipmentSlot = body.IsEquipmentSlot,
            EquipmentSlotName = model.EquipmentSlotName,
            MaxSlots = model.MaxSlots,
            UsedSlots = model.UsedSlots,
            MaxWeight = model.MaxWeight,
            GridWidth = model.GridWidth,
            GridHeight = model.GridHeight,
            MaxVolume = model.MaxVolume,
            CurrentVolume = model.CurrentVolume,
            ParentContainerId = model.ParentContainerId,
            NestingDepth = model.NestingDepth,
            CanContainContainers = model.CanContainContainers,
            MaxNestingDepth = model.MaxNestingDepth,
            SelfWeight = model.SelfWeight,
            WeightContribution = model.WeightContribution,
            SlotCost = model.SlotCost,
            ContentsWeight = model.ContentsWeight,
            TotalWeight = model.SelfWeight + model.ContentsWeight,
            RealmId = model.RealmId,
            CreatedAt = model.CreatedAt,
            ModifiedAt = model.ModifiedAt
        }, cancellationToken);

        _logger.LogDebug("Created container {ContainerId} type={Type}", containerId, body.ContainerType);
        return (StatusCodes.OK, MapContainerToResponse(model));
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, ContainerWithContentsResponse?)> GetContainerAsync(
        GetContainerRequest body,
        CancellationToken cancellationToken = default)
    {
        // Use cache read-through (per IMPLEMENTATION TENETS - use defined cache stores)
        var model = await GetContainerWithCacheAsync(body.ContainerId, cancellationToken);

        if (model is null)
        {
            return (StatusCodes.NotFound, null);
        }

        var response = new ContainerWithContentsResponse
        {
            Container = MapContainerToResponse(model),
            Items = new List<ContainerItem>()
        };

        if (body.IncludeContents)
        {
            try
            {
                var itemsResponse = await _itemClient.ListItemsByContainerAsync(
                    new ListItemsByContainerRequest { ContainerId = body.ContainerId },
                    cancellationToken);

                response.Items = itemsResponse.Items.Select(i => new ContainerItem
                {
                    InstanceId = i.InstanceId,
                    TemplateId = i.TemplateId,
                    Quantity = i.Quantity,
                    SlotIndex = i.SlotIndex,
                    SlotX = i.SlotX,
                    SlotY = i.SlotY,
                    Rotated = i.Rotated
                }).ToList();
            }
            catch (ApiException ex)
            {
                _logger.LogWarning(ex, "Failed to get items for container {ContainerId}: {StatusCode}",
                    body.ContainerId, ex.StatusCode);
                // Container exists but items couldn't be fetched - return empty list
            }
        }

        return (StatusCodes.OK, response);
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, ContainerResponse?)> GetOrCreateContainerAsync(
        GetOrCreateContainerRequest body,
        CancellationToken cancellationToken = default)
    {
        // Check if lazy container creation is enabled
        if (!_configuration.EnableLazyContainerCreation)
        {
            _logger.LogWarning("Lazy container creation is disabled");
            return (StatusCodes.BadRequest, null);
        }

        var containerStore = _stateStoreFactory.GetStore<ContainerModel>(StateStoreDefinitions.InventoryContainerStore);
        var stringStore = _stateStoreFactory.GetStore<string>(StateStoreDefinitions.InventoryContainerStore);

        // Look for existing container by owner + type
        var ownerKey = BuildOwnerIndexKey(body.OwnerType, body.OwnerId);
        var idsJson = await stringStore.GetAsync(ownerKey, cancellationToken);
        var ids = string.IsNullOrEmpty(idsJson)
            ? new List<string>()
            : BannouJson.Deserialize<List<string>>(idsJson) ?? new List<string>();

        foreach (var id in ids)
        {
            var existing = await containerStore.GetAsync($"{CONT_PREFIX}{id}", cancellationToken);
            if (existing is not null && existing.ContainerType == body.ContainerType)
            {
                return (StatusCodes.OK, MapContainerToResponse(existing));
            }
        }

        // Container doesn't exist, create it
        return await CreateContainerAsync(new CreateContainerRequest
        {
            OwnerId = body.OwnerId,
            OwnerType = body.OwnerType,
            ContainerType = body.ContainerType,
            ConstraintModel = body.ConstraintModel,
            MaxSlots = body.MaxSlots,
            MaxWeight = body.MaxWeight,
            GridWidth = body.GridWidth,
            GridHeight = body.GridHeight,
            RealmId = body.RealmId
        }, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, ListContainersResponse?)> ListContainersAsync(
        ListContainersRequest body,
        CancellationToken cancellationToken = default)
    {
        var containerStore = _stateStoreFactory.GetStore<ContainerModel>(StateStoreDefinitions.InventoryContainerStore);
        var stringStore = _stateStoreFactory.GetStore<string>(StateStoreDefinitions.InventoryContainerStore);

        var ownerKey = BuildOwnerIndexKey(body.OwnerType, body.OwnerId);
        var idsJson = await stringStore.GetAsync(ownerKey, cancellationToken);
        var ids = string.IsNullOrEmpty(idsJson)
            ? new List<string>()
            : BannouJson.Deserialize<List<string>>(idsJson) ?? new List<string>();

        var containers = new List<ContainerResponse>();
        foreach (var id in ids)
        {
            var model = await containerStore.GetAsync($"{CONT_PREFIX}{id}", cancellationToken);
            if (model is null) continue;

            // Apply filters
            if (!string.IsNullOrEmpty(body.ContainerType) && model.ContainerType != body.ContainerType) continue;
            if (!body.IncludeEquipmentSlots && model.IsEquipmentSlot) continue;
            if (body.RealmId.HasValue && model.RealmId != body.RealmId.Value) continue;

            containers.Add(MapContainerToResponse(model));
        }

        return (StatusCodes.OK, new ListContainersResponse
        {
            Containers = containers,
            TotalCount = containers.Count
        });
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, ContainerResponse?)> UpdateContainerAsync(
        UpdateContainerRequest body,
        CancellationToken cancellationToken = default)
    {
        // Input validation
        if (body.MaxSlots.HasValue && body.MaxSlots.Value <= 0)
        {
            return (StatusCodes.BadRequest, null);
        }
        if (body.MaxWeight.HasValue && body.MaxWeight.Value <= 0)
        {
            return (StatusCodes.BadRequest, null);
        }
        if (body.MaxVolume.HasValue && body.MaxVolume.Value <= 0)
        {
            return (StatusCodes.BadRequest, null);
        }
        if (body.GridWidth.HasValue && body.GridWidth.Value <= 0)
        {
            return (StatusCodes.BadRequest, null);
        }
        if (body.GridHeight.HasValue && body.GridHeight.Value <= 0)
        {
            return (StatusCodes.BadRequest, null);
        }

        // Acquire distributed lock for container modification (per IMPLEMENTATION TENETS)
        var lockOwner = $"update-container-{Guid.NewGuid():N}";
        await using var lockResponse = await _lockProvider.LockAsync(
            StateStoreDefinitions.InventoryLock,
            body.ContainerId.ToString(),
            lockOwner,
            _configuration.LockTimeoutSeconds,
            cancellationToken);

        if (!lockResponse.Success)
        {
            _logger.LogWarning("Failed to acquire lock for container {ContainerId}", body.ContainerId);
            return (StatusCodes.Conflict, null);
        }

        // Use cache read-through (per IMPLEMENTATION TENETS - use defined cache stores)
        var model = await GetContainerWithCacheAsync(body.ContainerId, cancellationToken);

        if (model is null)
        {
            return (StatusCodes.NotFound, null);
        }

        var now = DateTimeOffset.UtcNow;

        if (body.MaxSlots.HasValue) model.MaxSlots = body.MaxSlots.Value;
        if (body.MaxWeight.HasValue) model.MaxWeight = body.MaxWeight.Value;
        if (body.GridWidth.HasValue) model.GridWidth = body.GridWidth.Value;
        if (body.GridHeight.HasValue) model.GridHeight = body.GridHeight.Value;
        if (body.MaxVolume.HasValue) model.MaxVolume = body.MaxVolume.Value;
        if (body.AllowedCategories is not null) model.AllowedCategories = body.AllowedCategories.ToList();
        if (body.ForbiddenCategories is not null) model.ForbiddenCategories = body.ForbiddenCategories.ToList();
        if (body.AllowedTags is not null) model.AllowedTags = body.AllowedTags.ToList();
        if (body.Tags is not null) model.Tags = body.Tags.ToList();
        if (body.Metadata is not null) model.Metadata = BannouJson.Serialize(body.Metadata);
        model.ModifiedAt = now;

        // Save with cache write-through
        await SaveContainerWithCacheAsync(model, cancellationToken);

        await _messageBus.TryPublishAsync("inventory.container.updated", new InventoryContainerUpdatedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = now,
            ContainerId = body.ContainerId,
            OwnerId = model.OwnerId,
            OwnerType = model.OwnerType,
            ContainerType = model.ContainerType,
            ConstraintModel = model.ConstraintModel,
            IsEquipmentSlot = model.IsEquipmentSlot,
            EquipmentSlotName = model.EquipmentSlotName,
            MaxSlots = model.MaxSlots,
            UsedSlots = model.UsedSlots,
            MaxWeight = model.MaxWeight,
            GridWidth = model.GridWidth,
            GridHeight = model.GridHeight,
            MaxVolume = model.MaxVolume,
            CurrentVolume = model.CurrentVolume,
            ParentContainerId = model.ParentContainerId,
            NestingDepth = model.NestingDepth,
            CanContainContainers = model.CanContainContainers,
            MaxNestingDepth = model.MaxNestingDepth,
            SelfWeight = model.SelfWeight,
            WeightContribution = model.WeightContribution,
            SlotCost = model.SlotCost,
            ContentsWeight = model.ContentsWeight,
            TotalWeight = model.SelfWeight + model.ContentsWeight,
            RealmId = model.RealmId,
            CreatedAt = model.CreatedAt,
            ModifiedAt = model.ModifiedAt
        }, cancellationToken);

        _logger.LogDebug("Updated container {ContainerId}", body.ContainerId);
        return (StatusCodes.OK, MapContainerToResponse(model));
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, DeleteContainerResponse?)> DeleteContainerAsync(
        DeleteContainerRequest body,
        CancellationToken cancellationToken = default)
    {
        // Use extended delete lock timeout to account for serial item destruction/transfer
        // (per IMPLEMENTATION TENETS - tunables must be config properties)
        var lockOwner = $"delete-container-{Guid.NewGuid():N}";
        await using var lockResponse = await _lockProvider.LockAsync(
            StateStoreDefinitions.InventoryLock,
            body.ContainerId.ToString(),
            lockOwner,
            _configuration.DeleteLockTimeoutSeconds,
            cancellationToken);

        if (!lockResponse.Success)
        {
            _logger.LogWarning("Failed to acquire lock for container deletion {ContainerId}", body.ContainerId);
            return (StatusCodes.Conflict, null);
        }

        // Read container within lock to prevent TOCTOU race
        var model = await GetContainerWithCacheAsync(body.ContainerId, cancellationToken);

        if (model is null)
        {
            return (StatusCodes.NotFound, null);
        }

        // Get items in container - abort if item service is unreachable to prevent orphaning items
        List<ItemInstanceResponse> items;
        try
        {
            var itemsResponse = await _itemClient.ListItemsByContainerAsync(
                new ListItemsByContainerRequest { ContainerId = body.ContainerId },
                cancellationToken);
            items = itemsResponse.Items.ToList();
        }
        catch (ApiException ex)
        {
            _logger.LogError(ex, "Cannot delete container {ContainerId}: unable to determine contained items (status {StatusCode})",
                body.ContainerId, ex.StatusCode);
            return (StatusCodes.ServiceUnavailable, null);
        }

        var itemCount = items.Count;

        if (itemCount > 0)
        {
            switch (body.ItemHandling)
            {
                case ItemHandling.Error:
                    _logger.LogDebug("Container {ContainerId} is not empty", body.ContainerId);
                    return (StatusCodes.BadRequest, null);

                case ItemHandling.Destroy:
                    foreach (var item in items)
                    {
                        try
                        {
                            await _itemClient.DestroyItemInstanceAsync(
                                new DestroyItemInstanceRequest
                                {
                                    InstanceId = item.InstanceId,
                                    Reason = DestroyReason.Destroyed
                                }, cancellationToken);
                        }
                        catch (ApiException ex)
                        {
                            _logger.LogWarning(ex, "Failed to destroy item {InstanceId}", item.InstanceId);
                        }
                    }
                    break;

                case ItemHandling.Transfer:
                    if (!body.TransferToContainerId.HasValue)
                    {
                        _logger.LogWarning("Transfer target required when itemHandling is transfer");
                        return (StatusCodes.BadRequest, null);
                    }
                    foreach (var item in items)
                    {
                        await MoveItemAsync(new MoveItemRequest
                        {
                            InstanceId = item.InstanceId,
                            TargetContainerId = body.TransferToContainerId.Value
                        }, cancellationToken);
                    }
                    break;
            }
        }

        var now = DateTimeOffset.UtcNow;

        // Remove from indexes
        var ownerIndexKey = BuildOwnerIndexKey(model.OwnerType, model.OwnerId);
        await RemoveFromListAsync(StateStoreDefinitions.InventoryContainerStore,
            ownerIndexKey, body.ContainerId.ToString(), cancellationToken);
        await RemoveFromListAsync(StateStoreDefinitions.InventoryContainerStore,
            $"{CONT_TYPE_INDEX}{model.ContainerType}", body.ContainerId.ToString(), cancellationToken);

        var containerStore = _stateStoreFactory.GetStore<ContainerModel>(StateStoreDefinitions.InventoryContainerStore);
        await containerStore.DeleteAsync($"{CONT_PREFIX}{body.ContainerId}", cancellationToken);

        // Invalidate cache after MySQL delete
        await InvalidateContainerCacheAsync($"{CONT_PREFIX}{body.ContainerId}", cancellationToken);

        await _messageBus.TryPublishAsync("inventory.container.deleted", new InventoryContainerDeletedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = now,
            ContainerId = body.ContainerId,
            OwnerId = model.OwnerId,
            OwnerType = model.OwnerType,
            ContainerType = model.ContainerType,
            ConstraintModel = model.ConstraintModel,
            IsEquipmentSlot = model.IsEquipmentSlot,
            EquipmentSlotName = model.EquipmentSlotName,
            MaxSlots = model.MaxSlots,
            UsedSlots = model.UsedSlots,
            MaxWeight = model.MaxWeight,
            GridWidth = model.GridWidth,
            GridHeight = model.GridHeight,
            MaxVolume = model.MaxVolume,
            CurrentVolume = model.CurrentVolume,
            ParentContainerId = model.ParentContainerId,
            NestingDepth = model.NestingDepth,
            CanContainContainers = model.CanContainContainers,
            MaxNestingDepth = model.MaxNestingDepth,
            SelfWeight = model.SelfWeight,
            WeightContribution = model.WeightContribution,
            SlotCost = model.SlotCost,
            ContentsWeight = model.ContentsWeight,
            TotalWeight = model.SelfWeight + model.ContentsWeight,
            RealmId = model.RealmId,
            CreatedAt = model.CreatedAt,
            ModifiedAt = model.ModifiedAt
        }, cancellationToken);

        _logger.LogDebug("Deleted container {ContainerId}", body.ContainerId);
        return (StatusCodes.OK, new DeleteContainerResponse
        {
            ItemsHandled = itemCount
        });
    }

    #endregion

    #region Inventory Operations

    /// <inheritdoc/>
    public async Task<(StatusCodes, AddItemResponse?)> AddItemToContainerAsync(
        AddItemRequest body,
        CancellationToken cancellationToken = default)
    {
        // Acquire distributed lock for container modification (per IMPLEMENTATION TENETS)
        var lockOwner = $"add-item-{Guid.NewGuid():N}";
        await using var lockResponse = await _lockProvider.LockAsync(
            StateStoreDefinitions.InventoryLock,
            body.ContainerId.ToString(),
            lockOwner,
            _configuration.LockTimeoutSeconds,
            cancellationToken);

        if (!lockResponse.Success)
        {
            _logger.LogWarning("Failed to acquire lock for container {ContainerId}", body.ContainerId);
            return (StatusCodes.Conflict, null);
        }

        // Use cache read-through (per IMPLEMENTATION TENETS - use defined cache stores)
        var container = await GetContainerWithCacheAsync(body.ContainerId, cancellationToken);

        if (container is null)
        {
            return (StatusCodes.NotFound, null);
        }

        // Get item instance
        ItemInstanceResponse item;
        try
        {
            item = await _itemClient.GetItemInstanceAsync(
                new GetItemInstanceRequest { InstanceId = body.InstanceId },
                cancellationToken);
        }
        catch (ApiException ex)
        {
            _logger.LogDebug(ex, "Item not found: {InstanceId}", body.InstanceId);
            return (MapHttpStatusCode(ex.StatusCode), null);
        }

        // Get template for constraint checking
        ItemTemplateResponse template;
        try
        {
            template = await _itemClient.GetItemTemplateAsync(
                new GetItemTemplateRequest { TemplateId = item.TemplateId },
                cancellationToken);
        }
        catch (ApiException ex)
        {
            _logger.LogError(ex, "Failed to get template for item {InstanceId}", body.InstanceId);
            return (StatusCodes.InternalServerError, null);
        }

        // Check category constraints
        var categoryString = template.Category.ToString();
        if (container.AllowedCategories is not null && container.AllowedCategories.Count > 0)
        {
            if (!container.AllowedCategories.Any(c => string.Equals(c, categoryString, StringComparison.OrdinalIgnoreCase)))
            {
                _logger.LogWarning("Category {Category} not allowed in container {ContainerId}",
                    template.Category, body.ContainerId);
                return (StatusCodes.BadRequest, null);
            }
        }

        if (container.ForbiddenCategories is not null && container.ForbiddenCategories.Count > 0)
        {
            if (container.ForbiddenCategories.Any(c => string.Equals(c, categoryString, StringComparison.OrdinalIgnoreCase)))
            {
                _logger.LogDebug("Category {Category} forbidden in container {ContainerId}",
                    template.Category, body.ContainerId);
                return (StatusCodes.BadRequest, null);
            }
        }

        // Check capacity constraints
        var constraintViolation = CheckConstraints(container, template, item.Quantity);
        if (constraintViolation is not null)
        {
            _logger.LogWarning("Constraint violation adding to {ContainerId}: {Violation}",
                body.ContainerId, constraintViolation);
            return (StatusCodes.BadRequest, null);
        }

        var now = DateTimeOffset.UtcNow;

        // Update container usage stats
        container.UsedSlots = (container.UsedSlots ?? 0) + 1;
        if (template.Weight.HasValue)
        {
            container.ContentsWeight += template.Weight.Value * item.Quantity;
        }
        if (template.Volume.HasValue)
        {
            container.CurrentVolume = (container.CurrentVolume ?? 0) + template.Volume.Value * item.Quantity;
        }
        container.ModifiedAt = now;

        // Save with cache write-through
        await SaveContainerWithCacheAsync(container, cancellationToken);

        // Update item's container reference in item service
        try
        {
            await _itemClient.ModifyItemInstanceAsync(
                new ModifyItemInstanceRequest
                {
                    InstanceId = body.InstanceId,
                    NewContainerId = body.ContainerId,
                    NewSlotIndex = body.SlotIndex,
                    NewSlotX = body.SlotX,
                    NewSlotY = body.SlotY
                }, cancellationToken);
        }
        catch (ApiException ex)
        {
            _logger.LogError(ex, "Failed to update item container reference for {InstanceId}", body.InstanceId);
            return (StatusCodes.InternalServerError, null);
        }

        // Check if container is now full and emit event
        await EmitContainerFullEventIfNeededAsync(container, now, cancellationToken);

        await _messageBus.TryPublishAsync("inventory.item.placed", new InventoryItemPlacedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = now,
            InstanceId = body.InstanceId,
            TemplateId = item.TemplateId,
            ContainerId = body.ContainerId,
            OwnerId = container.OwnerId,
            OwnerType = container.OwnerType,
            Quantity = item.Quantity,
            SlotIndex = body.SlotIndex,
            SlotX = body.SlotX,
            SlotY = body.SlotY
        }, cancellationToken);

        await PublishContainerClientEventAsync(container.OwnerId, new InventoryItemChangedClientEvent
        {
            ChangeType = InventoryItemChangeType.Placed,
            ContainerId = body.ContainerId,
            ContainerType = container.ContainerType,
            InstanceId = body.InstanceId,
            TemplateId = item.TemplateId,
            Quantity = item.Quantity,
            SlotIndex = body.SlotIndex,
            SlotX = body.SlotX,
            SlotY = body.SlotY
        }, cancellationToken);

        return (StatusCodes.OK, new AddItemResponse
        {
            SlotIndex = body.SlotIndex,
            SlotX = body.SlotX,
            SlotY = body.SlotY
        });
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, RemoveItemResponse?)> RemoveItemFromContainerAsync(
        RemoveItemRequest body,
        CancellationToken cancellationToken = default)
    {
        // Get item to find its container
        ItemInstanceResponse item;
        try
        {
            item = await _itemClient.GetItemInstanceAsync(
                new GetItemInstanceRequest { InstanceId = body.InstanceId },
                cancellationToken);
        }
        catch (ApiException ex)
        {
            _logger.LogDebug(ex, "Item not found: {InstanceId}", body.InstanceId);
            return (MapHttpStatusCode(ex.StatusCode), null);
        }

        // Acquire distributed lock for container modification (per IMPLEMENTATION TENETS)
        var lockOwner = $"remove-item-{Guid.NewGuid():N}";
        await using var lockResponse = await _lockProvider.LockAsync(
            StateStoreDefinitions.InventoryLock,
            item.ContainerId.ToString(),
            lockOwner,
            _configuration.LockTimeoutSeconds,
            cancellationToken);

        if (!lockResponse.Success)
        {
            _logger.LogWarning("Failed to acquire lock for container {ContainerId}", item.ContainerId);
            return (StatusCodes.Conflict, null);
        }

        // Use cache read-through (per IMPLEMENTATION TENETS - use defined cache stores)
        var container = await GetContainerWithCacheAsync(item.ContainerId, cancellationToken);

        if (container is null)
        {
            return (StatusCodes.NotFound, null);
        }

        var now = DateTimeOffset.UtcNow;

        // Update container usage stats
        container.UsedSlots = Math.Max(0, (container.UsedSlots ?? 0) - 1);

        // Get template for weight/volume
        try
        {
            var template = await _itemClient.GetItemTemplateAsync(
                new GetItemTemplateRequest { TemplateId = item.TemplateId },
                cancellationToken);

            if (template.Weight.HasValue)
            {
                container.ContentsWeight = Math.Max(0, container.ContentsWeight - template.Weight.Value * item.Quantity);
            }
            if (template.Volume.HasValue)
            {
                container.CurrentVolume = Math.Max(0, (container.CurrentVolume ?? 0) - template.Volume.Value * item.Quantity);
            }
        }
        catch (ApiException ex)
        {
            _logger.LogWarning(ex, "Failed to get template for weight/volume update: {StatusCode}", ex.StatusCode);
            // Continue without weight/volume update
        }

        container.ModifiedAt = now;
        // Save with cache write-through
        await SaveContainerWithCacheAsync(container, cancellationToken);

        // Clear the item's container reference in item service (removes from container index)
        try
        {
            await _itemClient.ModifyItemInstanceAsync(
                new ModifyItemInstanceRequest
                {
                    InstanceId = body.InstanceId,
                    NewContainerId = null
                }, cancellationToken);
        }
        catch (ApiException ex)
        {
            _logger.LogWarning(ex, "Failed to clear item container reference for {InstanceId}", body.InstanceId);
        }

        await _messageBus.TryPublishAsync("inventory.item.removed", new InventoryItemRemovedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = now,
            InstanceId = body.InstanceId,
            TemplateId = item.TemplateId,
            ContainerId = item.ContainerId,
            OwnerId = container.OwnerId,
            OwnerType = container.OwnerType
        }, cancellationToken);

        await PublishContainerClientEventAsync(container.OwnerId, new InventoryItemChangedClientEvent
        {
            ChangeType = InventoryItemChangeType.Removed,
            ContainerId = item.ContainerId,
            ContainerType = container.ContainerType,
            InstanceId = body.InstanceId,
            TemplateId = item.TemplateId
        }, cancellationToken);

        return (StatusCodes.OK, new RemoveItemResponse
        {
            PreviousContainerId = item.ContainerId
        });
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, MoveItemResponse?)> MoveItemAsync(
        MoveItemRequest body,
        CancellationToken cancellationToken = default)
    {
        // Get item
        ItemInstanceResponse item;
        try
        {
            item = await _itemClient.GetItemInstanceAsync(
                new GetItemInstanceRequest { InstanceId = body.InstanceId },
                cancellationToken);
        }
        catch (ApiException ex)
        {
            _logger.LogDebug(ex, "Item not found: {InstanceId}", body.InstanceId);
            return (MapHttpStatusCode(ex.StatusCode), null);
        }

        var sourceContainerId = item.ContainerId;

        // If moving to same container, update slot position only (no counter changes needed)
        if (sourceContainerId == body.TargetContainerId)
        {
            // Acquire distributed lock for container modification (per IMPLEMENTATION TENETS)
            var sameContainerLockOwner = $"move-item-{Guid.NewGuid():N}";
            await using var sameContainerLock = await _lockProvider.LockAsync(
                StateStoreDefinitions.InventoryLock,
                body.TargetContainerId.ToString(),
                sameContainerLockOwner,
                _configuration.LockTimeoutSeconds,
                cancellationToken);

            if (!sameContainerLock.Success)
            {
                _logger.LogWarning("Failed to acquire lock for container {ContainerId}", body.TargetContainerId);
                return (StatusCodes.Conflict, null);
            }

            // Persist the slot position change via lib-item
            try
            {
                await _itemClient.ModifyItemInstanceAsync(
                    new ModifyItemInstanceRequest
                    {
                        InstanceId = body.InstanceId,
                        NewSlotIndex = body.TargetSlotIndex,
                        NewSlotX = body.TargetSlotX,
                        NewSlotY = body.TargetSlotY
                    }, cancellationToken);
            }
            catch (ApiException ex)
            {
                _logger.LogError(ex, "Failed to update slot position for item {InstanceId}", body.InstanceId);
                return (MapHttpStatusCode(ex.StatusCode), null);
            }

            var sameContainerNow = DateTimeOffset.UtcNow;

            await _messageBus.TryPublishAsync("inventory.item.moved", new InventoryItemMovedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = sameContainerNow,
                InstanceId = body.InstanceId,
                TemplateId = item.TemplateId,
                SourceContainerId = sourceContainerId,
                TargetContainerId = body.TargetContainerId,
                Quantity = item.Quantity,
                PreviousSlotIndex = item.SlotIndex,
                PreviousSlotX = item.SlotX,
                PreviousSlotY = item.SlotY,
                NewSlotIndex = body.TargetSlotIndex,
                NewSlotX = body.TargetSlotX,
                NewSlotY = body.TargetSlotY
            }, cancellationToken);

            // Load container for owner routing (cache read-through avoids redundant Redis calls)
            var sameContainer = await GetContainerWithCacheAsync(sourceContainerId, cancellationToken);
            if (sameContainer is not null)
            {
                await PublishContainerClientEventAsync(sameContainer.OwnerId, new InventoryItemChangedClientEvent
                {
                    ChangeType = InventoryItemChangeType.Moved,
                    ContainerId = sourceContainerId,
                    ContainerType = sameContainer.ContainerType,
                    InstanceId = body.InstanceId,
                    TemplateId = item.TemplateId,
                    Quantity = item.Quantity,
                    SlotIndex = body.TargetSlotIndex,
                    SlotX = body.TargetSlotX,
                    SlotY = body.TargetSlotY
                }, cancellationToken);
            }

            return (StatusCodes.OK, new MoveItemResponse
            {
                SourceContainerId = sourceContainerId,
                SlotIndex = body.TargetSlotIndex,
                SlotX = body.TargetSlotX,
                SlotY = body.TargetSlotY
            });
        }

        // Moving to different container - check constraints
        // Use cache read-through (per IMPLEMENTATION TENETS - use defined cache stores)
        var targetContainer = await GetContainerWithCacheAsync(body.TargetContainerId, cancellationToken);

        if (targetContainer is null)
        {
            return (StatusCodes.NotFound, null);
        }

        // Get template for constraint checking
        ItemTemplateResponse template;
        try
        {
            template = await _itemClient.GetItemTemplateAsync(
                new GetItemTemplateRequest { TemplateId = item.TemplateId },
                cancellationToken);
        }
        catch (ApiException ex)
        {
            _logger.LogError(ex, "Failed to get template for item {InstanceId}", body.InstanceId);
            return (StatusCodes.InternalServerError, null);
        }

        var constraintViolation = CheckConstraints(targetContainer, template, item.Quantity);
        if (constraintViolation is not null)
        {
            _logger.LogWarning("Constraint violation moving to {ContainerId}: {Violation}",
                body.TargetContainerId, constraintViolation);
            return (StatusCodes.BadRequest, null);
        }

        // Suppress client events from sub-operations (Remove+Add each publish their own);
        // this composite operation publishes a single consolidated "moved" client event.
        _suppressClientEvents = true;

        // Remove from source — check return status to avoid partial moves
        var (removeStatus, _) = await RemoveItemFromContainerAsync(
            new RemoveItemRequest { InstanceId = body.InstanceId }, cancellationToken);

        if (removeStatus != StatusCodes.OK)
        {
            _suppressClientEvents = false;
            _logger.LogWarning("Failed to remove item {InstanceId} from source container during move: {Status}",
                body.InstanceId, removeStatus);
            return (removeStatus, null);
        }

        // Add to target — if this fails, the item is orphaned (removed from source but not placed in target)
        var (addStatus, _) = await AddItemToContainerAsync(new AddItemRequest
        {
            InstanceId = body.InstanceId,
            ContainerId = body.TargetContainerId,
            SlotIndex = body.TargetSlotIndex,
            SlotX = body.TargetSlotX,
            SlotY = body.TargetSlotY,
            Rotated = body.Rotated
        }, cancellationToken);

        _suppressClientEvents = false;

        if (addStatus != StatusCodes.OK)
        {
            _logger.LogError("Item {InstanceId} removed from source but failed to add to target {TargetContainerId}: {Status}. Item may be orphaned.",
                body.InstanceId, body.TargetContainerId, addStatus);
            return (addStatus, null);
        }

        var now = DateTimeOffset.UtcNow;

        await _messageBus.TryPublishAsync("inventory.item.moved", new InventoryItemMovedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = now,
            InstanceId = body.InstanceId,
            TemplateId = item.TemplateId,
            SourceContainerId = sourceContainerId,
            TargetContainerId = body.TargetContainerId,
            Quantity = item.Quantity,
            NewSlotIndex = body.TargetSlotIndex,
            NewSlotX = body.TargetSlotX,
            NewSlotY = body.TargetSlotY
        }, cancellationToken);

        // Publish consolidated client event for cross-container move (source owner sees the change)
        var sourceContainerModel = await GetContainerWithCacheAsync(sourceContainerId, cancellationToken);
        if (sourceContainerModel is not null)
        {
            await PublishContainerClientEventAsync(sourceContainerModel.OwnerId, new InventoryItemChangedClientEvent
            {
                ChangeType = InventoryItemChangeType.Moved,
                ContainerId = body.TargetContainerId,
                ContainerType = targetContainer.ContainerType,
                InstanceId = body.InstanceId,
                TemplateId = item.TemplateId,
                Quantity = item.Quantity,
                SlotIndex = body.TargetSlotIndex,
                SlotX = body.TargetSlotX,
                SlotY = body.TargetSlotY
            }, cancellationToken);
        }

        return (StatusCodes.OK, new MoveItemResponse
        {
            SourceContainerId = sourceContainerId,
            SlotIndex = body.TargetSlotIndex,
            SlotX = body.TargetSlotX,
            SlotY = body.TargetSlotY
        });
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, TransferItemResponse?)> TransferItemAsync(
        TransferItemRequest body,
        CancellationToken cancellationToken = default)
    {
        // Get item
        ItemInstanceResponse item;
        try
        {
            item = await _itemClient.GetItemInstanceAsync(
                new GetItemInstanceRequest { InstanceId = body.InstanceId },
                cancellationToken);
        }
        catch (ApiException ex)
        {
            _logger.LogDebug(ex, "Item not found: {InstanceId}", body.InstanceId);
            return (MapHttpStatusCode(ex.StatusCode), null);
        }

        // Check if item is tradeable
        ItemTemplateResponse template;
        try
        {
            template = await _itemClient.GetItemTemplateAsync(
                new GetItemTemplateRequest { TemplateId = item.TemplateId },
                cancellationToken);
        }
        catch (ApiException ex)
        {
            _logger.LogError(ex, "Failed to get template for item {InstanceId}", body.InstanceId);
            return (StatusCodes.InternalServerError, null);
        }

        if (!template.Tradeable)
        {
            _logger.LogDebug("Item {InstanceId} is not tradeable", body.InstanceId);
            return (StatusCodes.BadRequest, null);
        }

        if (item.BoundToId.HasValue)
        {
            _logger.LogDebug("Item {InstanceId} is bound", body.InstanceId);
            return (StatusCodes.BadRequest, null);
        }

        var sourceContainerId = item.ContainerId;

        // Acquire distributed lock on source container for transfer safety
        var lockOwner = $"transfer-item-{Guid.NewGuid():N}";
        await using var lockResponse = await _lockProvider.LockAsync(
            StateStoreDefinitions.InventoryLock,
            sourceContainerId.ToString(),
            lockOwner,
            _configuration.LockTimeoutSeconds,
            cancellationToken);

        if (!lockResponse.Success)
        {
            _logger.LogWarning("Failed to acquire lock for container {ContainerId} during transfer", sourceContainerId);
            return (StatusCodes.Conflict, null);
        }

        var quantityToTransfer = body.Quantity ?? item.Quantity;

        // Validate requested quantity doesn't exceed available
        if (body.Quantity.HasValue && body.Quantity.Value > item.Quantity)
        {
            _logger.LogDebug("Cannot transfer {Requested} from stack of {Available}",
                body.Quantity.Value, item.Quantity);
            return (StatusCodes.BadRequest, null);
        }

        // Get source and target containers
        var containerStore = _stateStoreFactory.GetStore<ContainerModel>(StateStoreDefinitions.InventoryContainerStore);
        var sourceContainer = await containerStore.GetAsync($"{CONT_PREFIX}{sourceContainerId}", cancellationToken);
        var targetContainer = await containerStore.GetAsync($"{CONT_PREFIX}{body.TargetContainerId}", cancellationToken);

        if (sourceContainer is null || targetContainer is null)
        {
            return (StatusCodes.NotFound, null);
        }

        // Suppress client events from sub-operations (Split+Move each publish their own);
        // this composite operation publishes a single consolidated "transferred" client event.
        _suppressClientEvents = true;

        // Determine which item to move - if partial transfer, split first
        var instanceIdToMove = body.InstanceId;
        if (body.Quantity.HasValue && body.Quantity.Value < item.Quantity)
        {
            // Partial transfer: split the stack first, then move the split portion
            var (splitStatus, splitResponse) = await SplitStackAsync(new SplitStackRequest
            {
                InstanceId = body.InstanceId,
                Quantity = body.Quantity.Value
            }, cancellationToken);

            if (splitStatus != StatusCodes.OK || splitResponse is null)
            {
                _suppressClientEvents = false;
                _logger.LogWarning("Failed to split stack for partial transfer: {Status}", splitStatus);
                return (splitStatus, null);
            }

            // Move the newly created split item, not the original
            instanceIdToMove = splitResponse.NewInstanceId;
        }

        // Move the item (either original for full transfer, or split item for partial)
        var (moveStatus, _) = await MoveItemAsync(new MoveItemRequest
        {
            InstanceId = instanceIdToMove,
            TargetContainerId = body.TargetContainerId
        }, cancellationToken);

        _suppressClientEvents = false;

        if (moveStatus != StatusCodes.OK)
        {
            return (moveStatus, null);
        }

        var now = DateTimeOffset.UtcNow;

        await _messageBus.TryPublishAsync("inventory.item.transferred", new InventoryItemTransferredEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = now,
            InstanceId = instanceIdToMove,
            TemplateId = item.TemplateId,
            SourceContainerId = sourceContainerId,
            SourceOwnerId = sourceContainer.OwnerId,
            SourceOwnerType = sourceContainer.OwnerType,
            TargetContainerId = body.TargetContainerId,
            TargetOwnerId = targetContainer.OwnerId,
            TargetOwnerType = targetContainer.OwnerType,
            QuantityTransferred = quantityToTransfer
        }, cancellationToken);

        // Publish consolidated client event to both source and target owner sessions
        var transferClientEvent = new InventoryItemTransferredClientEvent
        {
            InstanceId = instanceIdToMove,
            TemplateId = item.TemplateId,
            SourceContainerId = sourceContainerId,
            TargetContainerId = body.TargetContainerId,
            SourceContainerType = sourceContainer.ContainerType,
            TargetContainerType = targetContainer.ContainerType,
            QuantityTransferred = quantityToTransfer
        };

        await PublishContainerClientEventAsync(sourceContainer.OwnerId, transferClientEvent, cancellationToken);

        // If target owner is different from source, notify target owner too
        if (targetContainer.OwnerId != sourceContainer.OwnerId)
        {
            await PublishContainerClientEventAsync(targetContainer.OwnerId, transferClientEvent, cancellationToken);
        }

        return (StatusCodes.OK, new TransferItemResponse
        {
            InstanceId = instanceIdToMove,
            SourceContainerId = sourceContainerId,
            QuantityTransferred = quantityToTransfer
        });
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, SplitStackResponse?)> SplitStackAsync(
        SplitStackRequest body,
        CancellationToken cancellationToken = default)
    {
        // Get item first to determine container
        ItemInstanceResponse item;
        try
        {
            item = await _itemClient.GetItemInstanceAsync(
                new GetItemInstanceRequest { InstanceId = body.InstanceId },
                cancellationToken);
        }
        catch (ApiException ex)
        {
            _logger.LogDebug(ex, "Item not found: {InstanceId}", body.InstanceId);
            return (MapHttpStatusCode(ex.StatusCode), null);
        }

        if (item.Quantity <= body.Quantity)
        {
            _logger.LogDebug("Cannot split {Quantity} from stack of {Total}",
                body.Quantity, item.Quantity);
            return (StatusCodes.BadRequest, null);
        }

        // Get template to check if stackable
        ItemTemplateResponse template;
        try
        {
            template = await _itemClient.GetItemTemplateAsync(
                new GetItemTemplateRequest { TemplateId = item.TemplateId },
                cancellationToken);
        }
        catch (ApiException ex)
        {
            _logger.LogError(ex, "Failed to get template for item {InstanceId}", body.InstanceId);
            return (StatusCodes.InternalServerError, null);
        }

        if (template.QuantityModel == QuantityModel.Unique)
        {
            _logger.LogDebug("Cannot split unique items");
            return (StatusCodes.BadRequest, null);
        }

        // Acquire distributed lock on the container for slot count consistency
        var lockOwner = $"split-stack-{Guid.NewGuid():N}";
        await using var lockResponse = await _lockProvider.LockAsync(
            StateStoreDefinitions.InventoryLock,
            item.ContainerId.ToString(),
            lockOwner,
            _configuration.LockTimeoutSeconds,
            cancellationToken);

        if (!lockResponse.Success)
        {
            _logger.LogWarning("Failed to acquire lock for container {ContainerId} during split", item.ContainerId);
            return (StatusCodes.Conflict, null);
        }

        var now = DateTimeOffset.UtcNow;
        var originalRemaining = item.Quantity - body.Quantity;

        // Update original instance quantity
        try
        {
            await _itemClient.ModifyItemInstanceAsync(
                new ModifyItemInstanceRequest
                {
                    InstanceId = body.InstanceId,
                    QuantityDelta = -body.Quantity
                }, cancellationToken);
        }
        catch (ApiException ex)
        {
            _logger.LogError(ex, "Failed to update original quantity for split");
            return (StatusCodes.InternalServerError, null);
        }

        // Create new instance with split quantity
        ItemInstanceResponse newItem;
        try
        {
            newItem = await _itemClient.CreateItemInstanceAsync(
                new CreateItemInstanceRequest
                {
                    TemplateId = item.TemplateId,
                    ContainerId = item.ContainerId,
                    RealmId = item.RealmId,
                    Quantity = body.Quantity,
                    SlotIndex = body.TargetSlotIndex,
                    SlotX = body.TargetSlotX,
                    SlotY = body.TargetSlotY,
                    OriginType = ItemOriginType.Other
                }, cancellationToken);
        }
        catch (ApiException ex)
        {
            _logger.LogError(ex, "Failed to create new item instance for split");
            // Attempt to restore original quantity
            try
            {
                await _itemClient.ModifyItemInstanceAsync(
                    new ModifyItemInstanceRequest
                    {
                        InstanceId = body.InstanceId,
                        QuantityDelta = body.Quantity
                    }, cancellationToken);
            }
            catch (ApiException restoreEx)
            {
                _logger.LogError(restoreEx, "Failed to restore original quantity after split failure");
            }
            return (StatusCodes.InternalServerError, null);
        }

        // Update container UsedSlots for the new item instance
        var container = await GetContainerWithCacheAsync(item.ContainerId, cancellationToken);
        if (container != null)
        {
            container.UsedSlots = (container.UsedSlots ?? 0) + 1;
            container.ModifiedAt = now;
            await SaveContainerWithCacheAsync(container, cancellationToken);
        }

        await _messageBus.TryPublishAsync("inventory.item.split", new InventoryItemSplitEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = now,
            OriginalInstanceId = body.InstanceId,
            NewInstanceId = newItem.InstanceId,
            TemplateId = item.TemplateId,
            ContainerId = item.ContainerId,
            QuantitySplit = body.Quantity,
            OriginalRemaining = originalRemaining
        }, cancellationToken);

        if (container is not null)
        {
            await PublishContainerClientEventAsync(container.OwnerId, new InventoryItemChangedClientEvent
            {
                ChangeType = InventoryItemChangeType.Split,
                ContainerId = item.ContainerId,
                ContainerType = container.ContainerType,
                InstanceId = newItem.InstanceId,
                TemplateId = item.TemplateId,
                Quantity = body.Quantity,
                SlotIndex = body.TargetSlotIndex,
                SlotX = body.TargetSlotX,
                SlotY = body.TargetSlotY
            }, cancellationToken);
        }

        return (StatusCodes.OK, new SplitStackResponse
        {
            NewInstanceId = newItem.InstanceId,
            OriginalQuantity = originalRemaining,
            NewQuantity = body.Quantity
        });
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, MergeStacksResponse?)> MergeStacksAsync(
        MergeStacksRequest body,
        CancellationToken cancellationToken = default)
    {
        // Get both items
        ItemInstanceResponse source;
        ItemInstanceResponse target;
        try
        {
            source = await _itemClient.GetItemInstanceAsync(
                new GetItemInstanceRequest { InstanceId = body.SourceInstanceId },
                cancellationToken);
        }
        catch (ApiException ex)
        {
            _logger.LogDebug(ex, "Source item not found: {InstanceId}", body.SourceInstanceId);
            return (MapHttpStatusCode(ex.StatusCode), null);
        }

        try
        {
            target = await _itemClient.GetItemInstanceAsync(
                new GetItemInstanceRequest { InstanceId = body.TargetInstanceId },
                cancellationToken);
        }
        catch (ApiException ex)
        {
            _logger.LogDebug(ex, "Target item not found: {InstanceId}", body.TargetInstanceId);
            return (MapHttpStatusCode(ex.StatusCode), null);
        }

        if (source.TemplateId != target.TemplateId)
        {
            _logger.LogDebug("Cannot merge different templates");
            return (StatusCodes.BadRequest, null);
        }

        // Get template for max stack
        ItemTemplateResponse template;
        try
        {
            template = await _itemClient.GetItemTemplateAsync(
                new GetItemTemplateRequest { TemplateId = source.TemplateId },
                cancellationToken);
        }
        catch (ApiException ex)
        {
            _logger.LogError(ex, "Failed to get template for merge");
            return (StatusCodes.InternalServerError, null);
        }

        var combinedQuantity = source.Quantity + target.Quantity;
        var quantityToAdd = source.Quantity;
        double? overflow = null;

        if (combinedQuantity > template.MaxStackSize)
        {
            overflow = combinedQuantity - template.MaxStackSize;
            quantityToAdd = source.Quantity - overflow.Value;
            combinedQuantity = template.MaxStackSize;
        }

        // Acquire distributed locks for merge operation safety
        // When items are in different containers, lock both to prevent races with
        // concurrent operations (e.g., DeleteContainer on target while we modify target item)
        // Use deterministic ordering (smaller GUID first) to prevent deadlocks
        var lockOwner = $"merge-stack-{Guid.NewGuid():N}";
        var sameContainer = source.ContainerId == target.ContainerId;

        Guid firstLockId;
        Guid? secondLockId;
        if (sameContainer)
        {
            firstLockId = source.ContainerId;
            secondLockId = null; // Single container - only one lock needed
        }
        else
        {
            // Deterministic ordering: lock smaller GUID first to prevent deadlocks
            if (source.ContainerId.CompareTo(target.ContainerId) < 0)
            {
                firstLockId = source.ContainerId;
                secondLockId = target.ContainerId;
            }
            else
            {
                firstLockId = target.ContainerId;
                secondLockId = source.ContainerId;
            }
        }

        // Acquire first lock
        await using var firstLock = await _lockProvider.LockAsync(
            StateStoreDefinitions.InventoryLock,
            firstLockId.ToString(),
            lockOwner,
            _configuration.LockTimeoutSeconds,
            cancellationToken);

        if (!firstLock.Success)
        {
            _logger.LogWarning("Failed to acquire lock for container {ContainerId} during merge", firstLockId);
            return (StatusCodes.Conflict, null);
        }

        // Acquire second lock if items are in different containers
        IAsyncDisposable? secondLock = null;
        if (!sameContainer && secondLockId.HasValue)
        {
            var secondLockResponse = await _lockProvider.LockAsync(
                StateStoreDefinitions.InventoryLock,
                secondLockId.Value.ToString(),
                lockOwner,
                _configuration.LockTimeoutSeconds,
                cancellationToken);

            if (!secondLockResponse.Success)
            {
                _logger.LogWarning("Failed to acquire lock for container {ContainerId} during merge", secondLockId.Value);
                return (StatusCodes.Conflict, null);
            }
            secondLock = secondLockResponse;
        }

        try
        {
            var now = DateTimeOffset.UtcNow;

            // Update target quantity first (safer: if this fails, source is unaffected)
            try
            {
                await _itemClient.ModifyItemInstanceAsync(
                    new ModifyItemInstanceRequest
                    {
                        InstanceId = body.TargetInstanceId,
                        QuantityDelta = quantityToAdd
                    }, cancellationToken);
            }
            catch (ApiException ex)
            {
                _logger.LogError(ex, "Failed to update target quantity during merge");
                return (StatusCodes.InternalServerError, null);
            }

            // Then destroy or reduce source
            if (overflow.HasValue && overflow.Value > 0)
            {
                // Partial merge - reduce source quantity
                try
                {
                    await _itemClient.ModifyItemInstanceAsync(
                        new ModifyItemInstanceRequest
                        {
                            InstanceId = body.SourceInstanceId,
                            QuantityDelta = -quantityToAdd
                        }, cancellationToken);
                }
                catch (ApiException ex)
                {
                    _logger.LogWarning(ex, "Failed to reduce source quantity during partial merge: {StatusCode}", ex.StatusCode);
                }
            }
            else
            {
                // Full merge - destroy source
                try
                {
                    await _itemClient.DestroyItemInstanceAsync(
                        new DestroyItemInstanceRequest
                        {
                            InstanceId = body.SourceInstanceId,
                            Reason = DestroyReason.Consumed
                        }, cancellationToken);
                }
                catch (ApiException ex)
                {
                    _logger.LogWarning(ex, "Failed to destroy source item during merge: {StatusCode}", ex.StatusCode);
                }

                // Update container slot count since source is gone (lock already held)
                var container = await GetContainerWithCacheAsync(source.ContainerId, cancellationToken);
                if (container is not null)
                {
                    container.UsedSlots = Math.Max(0, (container.UsedSlots ?? 0) - 1);
                    container.ModifiedAt = now;
                    await SaveContainerWithCacheAsync(container, cancellationToken);
                }
            }

            await _messageBus.TryPublishAsync("inventory.item.stacked", new InventoryItemStackedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = now,
                SourceInstanceId = body.SourceInstanceId,
                TargetInstanceId = body.TargetInstanceId,
                TemplateId = source.TemplateId,
                ContainerId = target.ContainerId,
                QuantityAdded = quantityToAdd,
                NewTotalQuantity = combinedQuantity
            }, cancellationToken);

            // Load container for owner routing (cache read-through)
            var mergeContainer = await GetContainerWithCacheAsync(target.ContainerId, cancellationToken);
            if (mergeContainer is not null)
            {
                await PublishContainerClientEventAsync(mergeContainer.OwnerId, new InventoryItemChangedClientEvent
                {
                    ChangeType = InventoryItemChangeType.Stacked,
                    ContainerId = target.ContainerId,
                    ContainerType = mergeContainer.ContainerType,
                    InstanceId = body.TargetInstanceId,
                    TemplateId = source.TemplateId,
                    Quantity = combinedQuantity
                }, cancellationToken);
            }

            return (StatusCodes.OK, new MergeStacksResponse
            {
                NewQuantity = combinedQuantity,
                SourceDestroyed = !overflow.HasValue || overflow.Value <= 0,
                OverflowQuantity = overflow
            });
        }
        finally
        {
            // Dispose second lock if acquired (first lock disposed via await using)
            if (secondLock is not null)
            {
                await secondLock.DisposeAsync();
            }
        }
    }

    #endregion

    #region Query Operations

    /// <inheritdoc/>
    public async Task<(StatusCodes, QueryItemsResponse?)> QueryItemsAsync(
        QueryItemsRequest body,
        CancellationToken cancellationToken = default)
    {
        // Get all containers for owner
        var (containersStatus, containersResponse) = await ListContainersAsync(
            new ListContainersRequest
            {
                OwnerId = body.OwnerId,
                OwnerType = body.OwnerType,
                ContainerType = body.ContainerType,
                IncludeEquipmentSlots = !body.ExcludeEquipmentSlots
            }, cancellationToken);

        if (containersStatus != StatusCodes.OK || containersResponse is null)
        {
            return (containersStatus, null);
        }

        var results = new List<QueryResultItem>();

        foreach (var container in containersResponse.Containers)
        {
            List<ItemInstanceResponse> items;
            try
            {
                var itemsResponse = await _itemClient.ListItemsByContainerAsync(
                    new ListItemsByContainerRequest { ContainerId = container.ContainerId },
                    cancellationToken);
                items = itemsResponse.Items.ToList();
            }
            catch (ApiException)
            {
                continue;
            }

            foreach (var item in items)
            {
                // Apply filters
                if (body.TemplateId.HasValue && item.TemplateId != body.TemplateId.Value) continue;

                // Get template for category/tag filtering
                if (!string.IsNullOrEmpty(body.Category) || (body.Tags is not null && body.Tags.Count > 0))
                {
                    try
                    {
                        var template = await _itemClient.GetItemTemplateAsync(
                            new GetItemTemplateRequest { TemplateId = item.TemplateId },
                            cancellationToken);

                        if (!string.IsNullOrEmpty(body.Category)
                            && !string.Equals(template.Category.ToString(), body.Category, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        if (body.Tags is not null && body.Tags.Count > 0)
                        {
                            if (!body.Tags.All(t => template.Tags.Contains(t))) continue;
                        }
                    }
                    catch (ApiException)
                    {
                        continue;
                    }
                }

                results.Add(new QueryResultItem
                {
                    InstanceId = item.InstanceId,
                    TemplateId = item.TemplateId,
                    ContainerId = container.ContainerId,
                    ContainerType = container.ContainerType,
                    Quantity = item.Quantity,
                    SlotIndex = item.SlotIndex
                });
            }
        }

        var totalCount = results.Count;
        var paged = results.Skip(body.Offset).Take(body.Limit).ToList();

        return (StatusCodes.OK, new QueryItemsResponse
        {
            Items = paged,
            TotalCount = totalCount
        });
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, CountItemsResponse?)> CountItemsAsync(
        CountItemsRequest body,
        CancellationToken cancellationToken = default)
    {
        // Page through all results to get accurate count
        var allItems = new List<QueryResultItem>();
        var offset = 0;
        var maxItems = _configuration.MaxCountQueryLimit;

        while (offset < maxItems)
        {
            var (queryStatus, queryResponse) = await QueryItemsAsync(
                new QueryItemsRequest
                {
                    OwnerId = body.OwnerId,
                    OwnerType = body.OwnerType,
                    TemplateId = body.TemplateId,
                    Offset = offset,
                    Limit = _configuration.QueryPageSize
                }, cancellationToken);

            if (queryStatus != StatusCodes.OK || queryResponse is null)
            {
                return (queryStatus, null);
            }

            allItems.AddRange(queryResponse.Items);

            // If we got fewer items than the page size, we've reached the end
            if (queryResponse.Items.Count < _configuration.QueryPageSize)
            {
                break;
            }

            offset += _configuration.QueryPageSize;
        }

        var totalQuantity = allItems.Sum(i => i.Quantity);

        return (StatusCodes.OK, new CountItemsResponse
        {
            TemplateId = body.TemplateId,
            TotalQuantity = totalQuantity,
            StackCount = allItems.Count
        });
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, HasItemsResponse?)> HasItemsAsync(
        HasItemsRequest body,
        CancellationToken cancellationToken = default)
    {
        var results = new List<HasItemResult>();
        var hasAll = true;

        foreach (var req in body.Requirements)
        {
            var (countStatus, countResponse) = await CountItemsAsync(
                new CountItemsRequest
                {
                    OwnerId = body.OwnerId,
                    OwnerType = body.OwnerType,
                    TemplateId = req.TemplateId
                }, cancellationToken);

            var available = countResponse?.TotalQuantity ?? 0;
            var satisfied = available >= req.Quantity;

            if (!satisfied) hasAll = false;

            results.Add(new HasItemResult
            {
                TemplateId = req.TemplateId,
                Required = req.Quantity,
                Available = available,
                Satisfied = satisfied
            });
        }

        return (StatusCodes.OK, new HasItemsResponse
        {
            HasAll = hasAll,
            Results = results
        });
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, FindSpaceResponse?)> FindSpaceAsync(
        FindSpaceRequest body,
        CancellationToken cancellationToken = default)
    {
        // Get template for constraints
        ItemTemplateResponse template;
        try
        {
            template = await _itemClient.GetItemTemplateAsync(
                new GetItemTemplateRequest { TemplateId = body.TemplateId },
                cancellationToken);
        }
        catch (ApiException ex)
        {
            _logger.LogDebug(ex, "Template not found: {TemplateId}", body.TemplateId);
            return (MapHttpStatusCode(ex.StatusCode), null);
        }

        // Get all containers for owner
        var (containersStatus, containersResponse) = await ListContainersAsync(
            new ListContainersRequest
            {
                OwnerId = body.OwnerId,
                OwnerType = body.OwnerType
            }, cancellationToken);

        if (containersStatus != StatusCodes.OK || containersResponse is null)
        {
            return (containersStatus, null);
        }

        var candidates = new List<SpaceCandidate>();
        var categoryString = template.Category.ToString();

        foreach (var container in containersResponse.Containers)
        {
            // Check if item category is allowed (case-insensitive)
            if (container.AllowedCategories is not null && container.AllowedCategories.Count > 0)
            {
                if (!container.AllowedCategories.Any(c => string.Equals(c, categoryString, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }
            }
            if (container.ForbiddenCategories is not null && container.ForbiddenCategories.Count > 0)
            {
                if (container.ForbiddenCategories.Any(c => string.Equals(c, categoryString, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }
            }

            // Check constraints using response fields directly
            var violation = CheckConstraintsFromResponse(container, template, body.Quantity);
            if (violation is not null) continue;

            var candidate = new SpaceCandidate
            {
                ContainerId = container.ContainerId,
                ContainerType = container.ContainerType,
                CanFitQuantity = body.Quantity
            };

            // If prefer stackable, check for existing stacks
            if (body.PreferStackable && template.QuantityModel != QuantityModel.Unique)
            {
                try
                {
                    var itemsResponse = await _itemClient.ListItemsByContainerAsync(
                        new ListItemsByContainerRequest { ContainerId = container.ContainerId },
                        cancellationToken);

                    var existingStack = itemsResponse.Items.FirstOrDefault(i =>
                        i.TemplateId == body.TemplateId && i.Quantity < template.MaxStackSize);

                    if (existingStack is not null)
                    {
                        candidate.ExistingStackInstanceId = existingStack.InstanceId;
                        candidate.CanFitQuantity = Math.Min(body.Quantity,
                            template.MaxStackSize - existingStack.Quantity);
                    }
                }
                catch (ApiException)
                {
                    // Container exists but items couldn't be fetched - skip stack check
                }
            }

            candidates.Add(candidate);
        }

        return (StatusCodes.OK, new FindSpaceResponse
        {
            HasSpace = candidates.Count > 0,
            Candidates = candidates
        });
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Maps HTTP status code to internal StatusCodes enum.
    /// </summary>
    private static StatusCodes MapHttpStatusCode(int httpStatusCode)
    {
        return httpStatusCode switch
        {
            >= 200 and < 300 => StatusCodes.OK,
            400 => StatusCodes.BadRequest,
            401 => StatusCodes.Unauthorized,
            403 => StatusCodes.Forbidden,
            404 => StatusCodes.NotFound,
            409 => StatusCodes.Conflict,
            501 => StatusCodes.NotImplemented,
            503 => StatusCodes.ServiceUnavailable,
            _ => StatusCodes.InternalServerError
        };
    }

    /// <summary>
    /// Builds the owner index key for state store lookups.
    /// </summary>
    private static string BuildOwnerIndexKey(ContainerOwnerType ownerType, Guid ownerId)
    {
        return $"{CONT_OWNER_INDEX}{ownerType}:{ownerId}";
    }

    /// <summary>
    /// Checks container capacity constraints against the typed ContainerModel.
    /// </summary>
    private static string? CheckConstraints(
        ContainerModel container,
        ItemTemplateResponse template,
        double quantity)
    {
        switch (container.ConstraintModel)
        {
            case ContainerConstraintModel.SlotOnly:
                if (container.MaxSlots.HasValue && (container.UsedSlots ?? 0) >= container.MaxSlots.Value)
                    return "Container is full (slots)";
                break;

            case ContainerConstraintModel.WeightOnly:
                if (container.MaxWeight.HasValue && template.Weight.HasValue)
                {
                    var newWeight = container.ContentsWeight + template.Weight.Value * quantity;
                    if (newWeight > container.MaxWeight.Value)
                        return "Container is full (weight)";
                }
                break;

            case ContainerConstraintModel.SlotAndWeight:
                if (container.MaxSlots.HasValue && (container.UsedSlots ?? 0) >= container.MaxSlots.Value)
                    return "Container is full (slots)";
                if (container.MaxWeight.HasValue && template.Weight.HasValue)
                {
                    var newWeight = container.ContentsWeight + template.Weight.Value * quantity;
                    if (newWeight > container.MaxWeight.Value)
                        return "Container is full (weight)";
                }
                break;

            case ContainerConstraintModel.Grid:
                // Grid constraint checking would require tracking occupied cells
                // For now, use slot count as approximation
                if (container.MaxSlots.HasValue && (container.UsedSlots ?? 0) >= container.MaxSlots.Value)
                    return "Container is full (grid)";
                break;

            case ContainerConstraintModel.Volumetric:
                if (container.MaxVolume.HasValue && template.Volume.HasValue)
                {
                    var newVolume = (container.CurrentVolume ?? 0) + template.Volume.Value * quantity;
                    if (newVolume > container.MaxVolume.Value)
                        return "Container is full (volume)";
                }
                break;

            case ContainerConstraintModel.Unlimited:
                // No constraints
                break;
        }

        return null;
    }

    /// <summary>
    /// Checks container capacity constraints using ContainerResponse fields directly.
    /// Avoids creating temporary ContainerModel instances.
    /// </summary>
    private static string? CheckConstraintsFromResponse(
        ContainerResponse container,
        ItemTemplateResponse template,
        double quantity)
    {
        switch (container.ConstraintModel)
        {
            case ContainerConstraintModel.SlotOnly:
                if (container.MaxSlots.HasValue && (container.UsedSlots ?? 0) >= container.MaxSlots.Value)
                    return "Container is full (slots)";
                break;

            case ContainerConstraintModel.WeightOnly:
                if (container.MaxWeight.HasValue && template.Weight.HasValue)
                {
                    var newWeight = container.ContentsWeight + template.Weight.Value * quantity;
                    if (newWeight > container.MaxWeight.Value)
                        return "Container is full (weight)";
                }
                break;

            case ContainerConstraintModel.SlotAndWeight:
                if (container.MaxSlots.HasValue && (container.UsedSlots ?? 0) >= container.MaxSlots.Value)
                    return "Container is full (slots)";
                if (container.MaxWeight.HasValue && template.Weight.HasValue)
                {
                    var newWeight = container.ContentsWeight + template.Weight.Value * quantity;
                    if (newWeight > container.MaxWeight.Value)
                        return "Container is full (weight)";
                }
                break;

            case ContainerConstraintModel.Grid:
                if (container.MaxSlots.HasValue && (container.UsedSlots ?? 0) >= container.MaxSlots.Value)
                    return "Container is full (grid)";
                break;

            case ContainerConstraintModel.Volumetric:
                if (container.MaxVolume.HasValue && template.Volume.HasValue)
                {
                    var newVolume = (container.CurrentVolume ?? 0) + template.Volume.Value * quantity;
                    if (newVolume > container.MaxVolume.Value)
                        return "Container is full (volume)";
                }
                break;

            case ContainerConstraintModel.Unlimited:
                break;
        }

        return null;
    }

    /// <summary>
    /// Publishes a client event to all sessions observing the container owner's inventory.
    /// Skipped when <see cref="_suppressClientEvents"/> is true (during composite operations).
    /// </summary>
    private async Task PublishContainerClientEventAsync<TEvent>(
        Guid ownerId,
        TEvent clientEvent,
        CancellationToken ct)
        where TEvent : BaseClientEvent
    {
        using var activity = _telemetryProvider.StartActivity("bannou.inventory", "InventoryService.PublishContainerClientEventAsync");
        if (_suppressClientEvents) return;
        await _entitySessionRegistry.PublishToEntitySessionsAsync("inventory", ownerId, clientEvent, ct);
    }

    /// <summary>
    /// Emits a container full event if the container has reached its capacity.
    /// </summary>
    private async Task EmitContainerFullEventIfNeededAsync(
        ContainerModel container,
        DateTimeOffset timestamp,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.inventory", "InventoryService.EmitContainerFullEventIfNeededAsync");
        ConstraintLimitType? constraintType = null;

        switch (container.ConstraintModel)
        {
            case ContainerConstraintModel.SlotOnly:
            case ContainerConstraintModel.SlotAndWeight:
                if (container.MaxSlots.HasValue && (container.UsedSlots ?? 0) >= container.MaxSlots.Value)
                    constraintType = ConstraintLimitType.Slots;
                break;

            case ContainerConstraintModel.WeightOnly:
                if (container.MaxWeight.HasValue && container.ContentsWeight >= container.MaxWeight.Value)
                    constraintType = ConstraintLimitType.Weight;
                break;

            case ContainerConstraintModel.Grid:
                if (container.MaxSlots.HasValue && (container.UsedSlots ?? 0) >= container.MaxSlots.Value)
                    constraintType = ConstraintLimitType.Grid;
                break;

            case ContainerConstraintModel.Volumetric:
                if (container.MaxVolume.HasValue && (container.CurrentVolume ?? 0) >= container.MaxVolume.Value)
                    constraintType = ConstraintLimitType.Volume;
                break;
        }

        // Also check weight for slot_and_weight
        if (constraintType is null && container.ConstraintModel == ContainerConstraintModel.SlotAndWeight)
        {
            if (container.MaxWeight.HasValue && container.ContentsWeight >= container.MaxWeight.Value)
                constraintType = ConstraintLimitType.Weight;
        }

        if (constraintType is not null)
        {
            await _messageBus.TryPublishAsync("inventory.container.full", new InventoryContainerFullEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = timestamp,
                ContainerId = container.ContainerId,
                OwnerId = container.OwnerId,
                OwnerType = container.OwnerType,
                ContainerType = container.ContainerType,
                ConstraintType = constraintType.Value
            }, cancellationToken);

            await PublishContainerClientEventAsync(container.OwnerId, new InventoryContainerFullClientEvent
            {
                ContainerId = container.ContainerId,
                ContainerType = container.ContainerType,
                ConstraintType = constraintType.Value
            }, cancellationToken);
        }
    }

    /// <summary>
    /// Adds a value to a JSON-serialized list in the state store.
    /// </summary>
    private async Task AddToListAsync(string storeName, string key, string value, CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.inventory", "InventoryService.AddToListAsync");
        await using var lockResponse = await _lockProvider.LockAsync(
            storeName, key, Guid.NewGuid().ToString(), _configuration.ListLockTimeoutSeconds, ct);
        if (!lockResponse.Success)
        {
            _logger.LogWarning("Could not acquire list lock for {StoreName}:{Key}", storeName, key);
            return;
        }

        var stringStore = _stateStoreFactory.GetStore<string>(storeName);
        var json = await stringStore.GetAsync(key, ct);
        var list = string.IsNullOrEmpty(json)
            ? new List<string>()
            : BannouJson.Deserialize<List<string>>(json) ?? new List<string>();

        if (!list.Contains(value))
        {
            list.Add(value);
            await stringStore.SaveAsync(key, BannouJson.Serialize(list), cancellationToken: ct);
        }
    }

    /// <summary>
    /// Removes a value from a JSON-serialized list in the state store.
    /// </summary>
    private async Task RemoveFromListAsync(string storeName, string key, string value, CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.inventory", "InventoryService.RemoveFromListAsync");
        await using var lockResponse = await _lockProvider.LockAsync(
            storeName, key, Guid.NewGuid().ToString(), _configuration.ListLockTimeoutSeconds, ct);
        if (!lockResponse.Success)
        {
            _logger.LogWarning("Could not acquire list lock for {StoreName}:{Key}", storeName, key);
            return;
        }

        var stringStore = _stateStoreFactory.GetStore<string>(storeName);
        var json = await stringStore.GetAsync(key, ct);
        if (string.IsNullOrEmpty(json)) return;

        var list = BannouJson.Deserialize<List<string>>(json) ?? new List<string>();
        if (list.Remove(value))
        {
            await stringStore.SaveAsync(key, BannouJson.Serialize(list), cancellationToken: ct);
        }
    }

    /// <summary>
    /// Maps the internal ContainerModel to the API response type.
    /// </summary>
    private static ContainerResponse MapContainerToResponse(ContainerModel model)
    {
        return new ContainerResponse
        {
            ContainerId = model.ContainerId,
            OwnerId = model.OwnerId,
            OwnerType = model.OwnerType,
            ContainerType = model.ContainerType,
            ConstraintModel = model.ConstraintModel,
            IsEquipmentSlot = model.IsEquipmentSlot,
            EquipmentSlotName = model.EquipmentSlotName,
            MaxSlots = model.MaxSlots,
            UsedSlots = model.UsedSlots,
            MaxWeight = model.MaxWeight,
            GridWidth = model.GridWidth,
            GridHeight = model.GridHeight,
            MaxVolume = model.MaxVolume,
            CurrentVolume = model.CurrentVolume,
            ParentContainerId = model.ParentContainerId,
            NestingDepth = model.NestingDepth,
            CanContainContainers = model.CanContainContainers,
            MaxNestingDepth = model.MaxNestingDepth,
            SelfWeight = model.SelfWeight,
            WeightContribution = model.WeightContribution,
            SlotCost = model.SlotCost,
            ParentGridWidth = model.ParentGridWidth,
            ParentGridHeight = model.ParentGridHeight,
            ParentVolume = model.ParentVolume,
            ContentsWeight = model.ContentsWeight,
            TotalWeight = model.SelfWeight + model.ContentsWeight,
            AllowedCategories = model.AllowedCategories,
            ForbiddenCategories = model.ForbiddenCategories,
            AllowedTags = model.AllowedTags,
            RealmId = model.RealmId,
            Tags = model.Tags,
            Metadata = model.Metadata is not null ? BannouJson.Deserialize<object>(model.Metadata) : null,
            CreatedAt = model.CreatedAt,
            ModifiedAt = model.ModifiedAt
        };
    }

    #endregion

    #region Container Cache Helpers

    /// <summary>
    /// Attempts to retrieve a container from the Redis cache.
    /// Uses StateStoreDefinitions.InventoryContainerCache directly per IMPLEMENTATION TENETS.
    /// </summary>
    private async Task<ContainerModel?> TryGetContainerFromCacheAsync(string key, CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.inventory", "InventoryService.TryGetContainerFromCacheAsync");
        try
        {
            var cache = _stateStoreFactory.GetStore<ContainerModel>(StateStoreDefinitions.InventoryContainerCache);
            return await cache.GetAsync(key, ct);
        }
        catch (Exception ex)
        {
            // Cache error is non-fatal - proceed to MySQL
            _logger.LogDebug(ex, "Container cache lookup failed for {Key}", key);
            return null;
        }
    }

    /// <summary>
    /// Updates the container cache after a read or write.
    /// Uses StateStoreDefinitions.InventoryContainerCache directly per IMPLEMENTATION TENETS.
    /// </summary>
    private async Task UpdateContainerCacheAsync(string key, ContainerModel container, CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.inventory", "InventoryService.UpdateContainerCacheAsync");
        try
        {
            var cache = _stateStoreFactory.GetStore<ContainerModel>(StateStoreDefinitions.InventoryContainerCache);
            await cache.SaveAsync(key, container, new StateOptions { Ttl = _configuration.ContainerCacheTtlSeconds }, ct);
        }
        catch (Exception ex)
        {
            // Cache write failure is non-fatal
            _logger.LogWarning(ex, "Failed to update container cache for {Key}", key);
        }
    }

    /// <summary>
    /// Invalidates a container from the cache (for deletes).
    /// </summary>
    private async Task InvalidateContainerCacheAsync(string key, CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.inventory", "InventoryService.InvalidateContainerCacheAsync");
        try
        {
            var cache = _stateStoreFactory.GetStore<ContainerModel>(StateStoreDefinitions.InventoryContainerCache);
            await cache.DeleteAsync(key, ct);
        }
        catch (Exception ex)
        {
            // Cache invalidation failure is non-fatal
            _logger.LogDebug(ex, "Failed to invalidate container cache for {Key}", key);
        }
    }

    /// <summary>
    /// Gets a container, checking cache first, then MySQL.
    /// Populates cache on miss.
    /// </summary>
    private async Task<ContainerModel?> GetContainerWithCacheAsync(Guid containerId, CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.inventory", "InventoryService.GetContainerWithCacheAsync");
        var key = $"{CONT_PREFIX}{containerId}";

        // Check Redis cache first
        var cached = await TryGetContainerFromCacheAsync(key, ct);
        if (cached != null)
            return cached;

        // Cache miss - read from MySQL
        var store = _stateStoreFactory.GetStore<ContainerModel>(StateStoreDefinitions.InventoryContainerStore);
        var container = await store.GetAsync(key, ct);

        if (container != null)
        {
            // Populate cache for future reads
            await UpdateContainerCacheAsync(key, container, ct);
        }

        return container;
    }

    /// <summary>
    /// Saves a container to MySQL and updates the cache.
    /// </summary>
    private async Task SaveContainerWithCacheAsync(ContainerModel container, CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.inventory", "InventoryService.SaveContainerWithCacheAsync");
        var key = $"{CONT_PREFIX}{container.ContainerId}";
        var store = _stateStoreFactory.GetStore<ContainerModel>(StateStoreDefinitions.InventoryContainerStore);
        await store.SaveAsync(key, container, cancellationToken: ct);

        // Update Redis cache after MySQL write
        await UpdateContainerCacheAsync(key, container, ct);
    }

    #endregion
}
