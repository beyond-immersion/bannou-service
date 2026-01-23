using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Item;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.ServiceClients;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("lib-inventory.tests")]
[assembly: InternalsVisibleTo("DynamicProxyGenAssembly2")]

namespace BeyondImmersion.BannouService.Inventory;

/// <summary>
/// Implementation of the Inventory service.
/// Provides container management and item placement operations for games.
/// </summary>
[BannouService("inventory", typeof(IInventoryService), lifetime: ServiceLifetime.Scoped)]
public partial class InventoryService : IInventoryService
{
    private readonly IMessageBus _messageBus;
    private readonly IServiceNavigator _navigator;
    private readonly IStateStoreFactory _stateStoreFactory;
    private readonly IDistributedLockProvider _lockProvider;
    private readonly ILogger<InventoryService> _logger;
    private readonly InventoryServiceConfiguration _configuration;

    // Container store key prefixes
    private const string CONT_PREFIX = "cont:";
    private const string CONT_OWNER_INDEX = "cont-owner:";
    private const string CONT_TYPE_INDEX = "cont-type:";

    // Query page size and lock configuration now use _configuration properties
    // (QueryPageSize, LockTimeoutSeconds, ContainerCacheTtlSeconds)

    /// <summary>
    /// Initializes a new instance of the InventoryService.
    /// </summary>
    public InventoryService(
        IMessageBus messageBus,
        IServiceNavigator navigator,
        IStateStoreFactory stateStoreFactory,
        IDistributedLockProvider lockProvider,
        ILogger<InventoryService> logger,
        InventoryServiceConfiguration configuration)
    {
        _messageBus = messageBus ?? throw new ArgumentNullException(nameof(messageBus));
        _navigator = navigator ?? throw new ArgumentNullException(nameof(navigator));
        _stateStoreFactory = stateStoreFactory ?? throw new ArgumentNullException(nameof(stateStoreFactory));
        _lockProvider = lockProvider ?? throw new ArgumentNullException(nameof(lockProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    }

    #region Container Operations

    /// <inheritdoc/>
    public async Task<(StatusCodes, ContainerResponse?)> CreateContainerAsync(
        CreateContainerRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Creating container type {ContainerType} for owner {OwnerId}",
                body.ContainerType, body.OwnerId);

            // Input validation
            if (body.MaxSlots.HasValue && body.MaxSlots.Value <= 0)
            {
                _logger.LogWarning("MaxSlots must be positive, got {MaxSlots}", body.MaxSlots.Value);
                return (StatusCodes.BadRequest, null);
            }
            if (body.MaxWeight.HasValue && body.MaxWeight.Value <= 0)
            {
                _logger.LogWarning("MaxWeight must be positive, got {MaxWeight}", body.MaxWeight.Value);
                return (StatusCodes.BadRequest, null);
            }
            if (body.MaxVolume.HasValue && body.MaxVolume.Value <= 0)
            {
                _logger.LogWarning("MaxVolume must be positive, got {MaxVolume}", body.MaxVolume.Value);
                return (StatusCodes.BadRequest, null);
            }
            if (body.GridWidth.HasValue && body.GridWidth.Value <= 0)
            {
                _logger.LogWarning("GridWidth must be positive, got {GridWidth}", body.GridWidth.Value);
                return (StatusCodes.BadRequest, null);
            }
            if (body.GridHeight.HasValue && body.GridHeight.Value <= 0)
            {
                _logger.LogWarning("GridHeight must be positive, got {GridHeight}", body.GridHeight.Value);
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
                    _logger.LogWarning("Parent container not found: {ParentId}", body.ParentContainerId);
                    return (StatusCodes.BadRequest, null);
                }
                nestingDepth = parent.NestingDepth + 1;

                var maxNesting = parent.MaxNestingDepth ?? _configuration.DefaultMaxNestingDepth;
                if (nestingDepth > maxNesting)
                {
                    _logger.LogWarning("Max nesting depth exceeded for parent {ParentId}", body.ParentContainerId);
                    return (StatusCodes.BadRequest, null);
                }
            }

            // Resolve weight contribution: use config default if not specified (enum default is None)
            var weightContribution = body.WeightContribution;
            if (weightContribution == WeightContribution.None
                && Enum.TryParse<WeightContribution>(_configuration.DefaultWeightContribution, true, out var configDefault))
            {
                weightContribution = configDefault;
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

            await _messageBus.TryPublishAsync("inventory-container.created", new InventoryContainerCreatedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = now,
                ContainerId = containerId,
                OwnerId = body.OwnerId,
                OwnerType = body.OwnerType.ToString(),
                ContainerType = body.ContainerType,
                ConstraintModel = body.ConstraintModel.ToString(),
                IsEquipmentSlot = body.IsEquipmentSlot
            }, cancellationToken);

            _logger.LogInformation("Created container {ContainerId} type={Type}", containerId, body.ContainerType);
            return (StatusCodes.OK, MapContainerToResponse(model));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating container");
            await _messageBus.TryPublishErrorAsync(
                "inventory", "CreateContainer", "unexpected_exception", ex.Message,
                dependency: null, endpoint: "post:/inventory/container/create",
                details: null, stack: ex.StackTrace);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, ContainerWithContentsResponse?)> GetContainerAsync(
        GetContainerRequest body,
        CancellationToken cancellationToken = default)
    {
        try
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
                    var itemsResponse = await _navigator.Item.ListItemsByContainerAsync(
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting container {ContainerId}", body.ContainerId);
            await _messageBus.TryPublishErrorAsync(
                "inventory", "GetContainer", "unexpected_exception", ex.Message,
                dependency: null, endpoint: "post:/inventory/container/get",
                details: null, stack: ex.StackTrace);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, ContainerResponse?)> GetOrCreateContainerAsync(
        GetOrCreateContainerRequest body,
        CancellationToken cancellationToken = default)
    {
        try
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in get-or-create container");
            await _messageBus.TryPublishErrorAsync(
                "inventory", "GetOrCreateContainer", "unexpected_exception", ex.Message,
                dependency: null, endpoint: "post:/inventory/container/get-or-create",
                details: null, stack: ex.StackTrace);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, ListContainersResponse?)> ListContainersAsync(
        ListContainersRequest body,
        CancellationToken cancellationToken = default)
    {
        try
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing containers for owner {OwnerId}", body.OwnerId);
            await _messageBus.TryPublishErrorAsync(
                "inventory", "ListContainers", "unexpected_exception", ex.Message,
                dependency: null, endpoint: "post:/inventory/container/list",
                details: null, stack: ex.StackTrace);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, ContainerResponse?)> UpdateContainerAsync(
        UpdateContainerRequest body,
        CancellationToken cancellationToken = default)
    {
        try
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

            await _messageBus.TryPublishAsync("inventory-container.updated", new InventoryContainerUpdatedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = now,
                ContainerId = body.ContainerId,
                OwnerId = model.OwnerId,
                OwnerType = model.OwnerType.ToString(),
                ContainerType = model.ContainerType,
                ConstraintModel = model.ConstraintModel.ToString(),
                IsEquipmentSlot = model.IsEquipmentSlot
            }, cancellationToken);

            _logger.LogInformation("Updated container {ContainerId}", body.ContainerId);
            return (StatusCodes.OK, MapContainerToResponse(model));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating container {ContainerId}", body.ContainerId);
            await _messageBus.TryPublishErrorAsync(
                "inventory", "UpdateContainer", "unexpected_exception", ex.Message,
                dependency: null, endpoint: "post:/inventory/container/update",
                details: null, stack: ex.StackTrace);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, DeleteContainerResponse?)> DeleteContainerAsync(
        DeleteContainerRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Use cache read-through (per IMPLEMENTATION TENETS - use defined cache stores)
            var model = await GetContainerWithCacheAsync(body.ContainerId, cancellationToken);

            if (model is null)
            {
                return (StatusCodes.NotFound, null);
            }

            // Get items in container
            var items = new List<ItemInstanceResponse>();
            try
            {
                var itemsResponse = await _navigator.Item.ListItemsByContainerAsync(
                    new ListItemsByContainerRequest { ContainerId = body.ContainerId },
                    cancellationToken);
                items = itemsResponse.Items.ToList();
            }
            catch (ApiException ex)
            {
                _logger.LogWarning(ex, "Failed to get items for container deletion: {StatusCode}", ex.StatusCode);
                // Proceed with deletion even if items can't be fetched
            }

            var itemCount = items.Count;

            if (itemCount > 0)
            {
                switch (body.ItemHandling)
                {
                    case ItemHandling.Error:
                        _logger.LogWarning("Container {ContainerId} is not empty", body.ContainerId);
                        return (StatusCodes.BadRequest, null);

                    case ItemHandling.Destroy:
                        foreach (var item in items)
                        {
                            try
                            {
                                await _navigator.Item.DestroyItemInstanceAsync(
                                    new DestroyItemInstanceRequest
                                    {
                                        InstanceId = item.InstanceId,
                                        Reason = "container_deleted"
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

            await _messageBus.TryPublishAsync("inventory-container.deleted", new InventoryContainerDeletedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = now,
                ContainerId = body.ContainerId,
                OwnerId = model.OwnerId,
                OwnerType = model.OwnerType.ToString(),
                ContainerType = model.ContainerType,
                ConstraintModel = model.ConstraintModel.ToString(),
                IsEquipmentSlot = model.IsEquipmentSlot
            }, cancellationToken);

            _logger.LogInformation("Deleted container {ContainerId}", body.ContainerId);
            return (StatusCodes.OK, new DeleteContainerResponse
            {
                Deleted = true,
                ContainerId = body.ContainerId,
                ItemsHandled = itemCount
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting container {ContainerId}", body.ContainerId);
            await _messageBus.TryPublishErrorAsync(
                "inventory", "DeleteContainer", "unexpected_exception", ex.Message,
                dependency: null, endpoint: "post:/inventory/container/delete",
                details: null, stack: ex.StackTrace);
            return (StatusCodes.InternalServerError, null);
        }
    }

    #endregion

    #region Inventory Operations

    /// <inheritdoc/>
    public async Task<(StatusCodes, AddItemResponse?)> AddItemToContainerAsync(
        AddItemRequest body,
        CancellationToken cancellationToken = default)
    {
        try
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
                item = await _navigator.Item.GetItemInstanceAsync(
                    new GetItemInstanceRequest { InstanceId = body.InstanceId },
                    cancellationToken);
            }
            catch (ApiException ex)
            {
                _logger.LogWarning(ex, "Item not found: {InstanceId}", body.InstanceId);
                return (MapHttpStatusCode(ex.StatusCode), null);
            }

            // Get template for constraint checking
            ItemTemplateResponse template;
            try
            {
                template = await _navigator.Item.GetItemTemplateAsync(
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
                    _logger.LogWarning("Category {Category} forbidden in container {ContainerId}",
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

            // Check if container is now full and emit event
            await EmitContainerFullEventIfNeededAsync(container, now, cancellationToken);

            await _messageBus.TryPublishAsync("inventory-item.placed", new InventoryItemPlacedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = now,
                InstanceId = body.InstanceId,
                TemplateId = item.TemplateId,
                ContainerId = body.ContainerId,
                OwnerId = container.OwnerId,
                OwnerType = container.OwnerType.ToString(),
                Quantity = item.Quantity,
                SlotIndex = body.SlotIndex,
                SlotX = body.SlotX,
                SlotY = body.SlotY
            }, cancellationToken);

            return (StatusCodes.OK, new AddItemResponse
            {
                Success = true,
                InstanceId = body.InstanceId,
                ContainerId = body.ContainerId,
                SlotIndex = body.SlotIndex,
                SlotX = body.SlotX,
                SlotY = body.SlotY
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding item {InstanceId} to container {ContainerId}",
                body.InstanceId, body.ContainerId);
            await _messageBus.TryPublishErrorAsync(
                "inventory", "AddItemToContainer", "unexpected_exception", ex.Message,
                dependency: null, endpoint: "post:/inventory/add",
                details: null, stack: ex.StackTrace);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, RemoveItemResponse?)> RemoveItemFromContainerAsync(
        RemoveItemRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Get item to find its container
            ItemInstanceResponse item;
            try
            {
                item = await _navigator.Item.GetItemInstanceAsync(
                    new GetItemInstanceRequest { InstanceId = body.InstanceId },
                    cancellationToken);
            }
            catch (ApiException ex)
            {
                _logger.LogWarning(ex, "Item not found: {InstanceId}", body.InstanceId);
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
                var template = await _navigator.Item.GetItemTemplateAsync(
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

            await _messageBus.TryPublishAsync("inventory-item.removed", new InventoryItemRemovedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = now,
                InstanceId = body.InstanceId,
                TemplateId = item.TemplateId,
                ContainerId = item.ContainerId,
                OwnerId = container.OwnerId,
                OwnerType = container.OwnerType.ToString()
            }, cancellationToken);

            return (StatusCodes.OK, new RemoveItemResponse
            {
                Success = true,
                InstanceId = body.InstanceId,
                PreviousContainerId = item.ContainerId
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing item {InstanceId}", body.InstanceId);
            await _messageBus.TryPublishErrorAsync(
                "inventory", "RemoveItemFromContainer", "unexpected_exception", ex.Message,
                dependency: null, endpoint: "post:/inventory/remove",
                details: null, stack: ex.StackTrace);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, MoveItemResponse?)> MoveItemAsync(
        MoveItemRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Get item
            ItemInstanceResponse item;
            try
            {
                item = await _navigator.Item.GetItemInstanceAsync(
                    new GetItemInstanceRequest { InstanceId = body.InstanceId },
                    cancellationToken);
            }
            catch (ApiException ex)
            {
                _logger.LogWarning(ex, "Item not found: {InstanceId}", body.InstanceId);
                return (MapHttpStatusCode(ex.StatusCode), null);
            }

            var sourceContainerId = item.ContainerId;

            // If moving to same container, just update slot
            if (sourceContainerId == body.TargetContainerId)
            {
                return (StatusCodes.OK, new MoveItemResponse
                {
                    Success = true,
                    InstanceId = body.InstanceId,
                    SourceContainerId = sourceContainerId,
                    TargetContainerId = body.TargetContainerId,
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
                template = await _navigator.Item.GetItemTemplateAsync(
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

            // Remove from source
            await RemoveItemFromContainerAsync(new RemoveItemRequest { InstanceId = body.InstanceId }, cancellationToken);

            // Add to target
            await AddItemToContainerAsync(new AddItemRequest
            {
                InstanceId = body.InstanceId,
                ContainerId = body.TargetContainerId,
                SlotIndex = body.TargetSlotIndex,
                SlotX = body.TargetSlotX,
                SlotY = body.TargetSlotY,
                Rotated = body.Rotated
            }, cancellationToken);

            var now = DateTimeOffset.UtcNow;

            await _messageBus.TryPublishAsync("inventory-item.moved", new InventoryItemMovedEvent
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

            return (StatusCodes.OK, new MoveItemResponse
            {
                Success = true,
                InstanceId = body.InstanceId,
                SourceContainerId = sourceContainerId,
                TargetContainerId = body.TargetContainerId,
                SlotIndex = body.TargetSlotIndex,
                SlotX = body.TargetSlotX,
                SlotY = body.TargetSlotY
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error moving item {InstanceId}", body.InstanceId);
            await _messageBus.TryPublishErrorAsync(
                "inventory", "MoveItem", "unexpected_exception", ex.Message,
                dependency: null, endpoint: "post:/inventory/move",
                details: null, stack: ex.StackTrace);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, TransferItemResponse?)> TransferItemAsync(
        TransferItemRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Get item
            ItemInstanceResponse item;
            try
            {
                item = await _navigator.Item.GetItemInstanceAsync(
                    new GetItemInstanceRequest { InstanceId = body.InstanceId },
                    cancellationToken);
            }
            catch (ApiException ex)
            {
                _logger.LogWarning(ex, "Item not found: {InstanceId}", body.InstanceId);
                return (MapHttpStatusCode(ex.StatusCode), null);
            }

            // Check if item is tradeable
            ItemTemplateResponse template;
            try
            {
                template = await _navigator.Item.GetItemTemplateAsync(
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
                _logger.LogWarning("Item {InstanceId} is not tradeable", body.InstanceId);
                return (StatusCodes.BadRequest, null);
            }

            if (item.BoundToId.HasValue)
            {
                _logger.LogWarning("Item {InstanceId} is bound", body.InstanceId);
                return (StatusCodes.BadRequest, null);
            }

            var sourceContainerId = item.ContainerId;
            var quantityToTransfer = body.Quantity ?? item.Quantity;

            // Get source and target containers
            var containerStore = _stateStoreFactory.GetStore<ContainerModel>(StateStoreDefinitions.InventoryContainerStore);
            var sourceContainer = await containerStore.GetAsync($"{CONT_PREFIX}{sourceContainerId}", cancellationToken);
            var targetContainer = await containerStore.GetAsync($"{CONT_PREFIX}{body.TargetContainerId}", cancellationToken);

            if (sourceContainer is null || targetContainer is null)
            {
                return (StatusCodes.NotFound, null);
            }

            // Move the item
            var (moveStatus, _) = await MoveItemAsync(new MoveItemRequest
            {
                InstanceId = body.InstanceId,
                TargetContainerId = body.TargetContainerId
            }, cancellationToken);

            if (moveStatus != StatusCodes.OK)
            {
                return (moveStatus, null);
            }

            var now = DateTimeOffset.UtcNow;

            await _messageBus.TryPublishAsync("inventory-item.transferred", new InventoryItemTransferredEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = now,
                InstanceId = body.InstanceId,
                TemplateId = item.TemplateId,
                SourceContainerId = sourceContainerId,
                SourceOwnerId = sourceContainer.OwnerId,
                SourceOwnerType = sourceContainer.OwnerType.ToString(),
                TargetContainerId = body.TargetContainerId,
                TargetOwnerId = targetContainer.OwnerId,
                TargetOwnerType = targetContainer.OwnerType.ToString(),
                QuantityTransferred = quantityToTransfer
            }, cancellationToken);

            return (StatusCodes.OK, new TransferItemResponse
            {
                Success = true,
                InstanceId = body.InstanceId,
                SourceContainerId = sourceContainerId,
                TargetContainerId = body.TargetContainerId,
                QuantityTransferred = quantityToTransfer
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error transferring item {InstanceId}", body.InstanceId);
            await _messageBus.TryPublishErrorAsync(
                "inventory", "TransferItem", "unexpected_exception", ex.Message,
                dependency: null, endpoint: "post:/inventory/transfer",
                details: null, stack: ex.StackTrace);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, SplitStackResponse?)> SplitStackAsync(
        SplitStackRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Get item
            ItemInstanceResponse item;
            try
            {
                item = await _navigator.Item.GetItemInstanceAsync(
                    new GetItemInstanceRequest { InstanceId = body.InstanceId },
                    cancellationToken);
            }
            catch (ApiException ex)
            {
                _logger.LogWarning(ex, "Item not found: {InstanceId}", body.InstanceId);
                return (MapHttpStatusCode(ex.StatusCode), null);
            }

            if (item.Quantity <= body.Quantity)
            {
                _logger.LogWarning("Cannot split {Quantity} from stack of {Total}",
                    body.Quantity, item.Quantity);
                return (StatusCodes.BadRequest, null);
            }

            // Get template to check if stackable
            ItemTemplateResponse template;
            try
            {
                template = await _navigator.Item.GetItemTemplateAsync(
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
                _logger.LogWarning("Cannot split unique items");
                return (StatusCodes.BadRequest, null);
            }

            var now = DateTimeOffset.UtcNow;
            var originalRemaining = item.Quantity - body.Quantity;

            // Update original instance quantity
            try
            {
                await _navigator.Item.ModifyItemInstanceAsync(
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
                newItem = await _navigator.Item.CreateItemInstanceAsync(
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
                    await _navigator.Item.ModifyItemInstanceAsync(
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

            await _messageBus.TryPublishAsync("inventory-item.split", new InventoryItemSplitEvent
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

            return (StatusCodes.OK, new SplitStackResponse
            {
                Success = true,
                OriginalInstanceId = body.InstanceId,
                NewInstanceId = newItem.InstanceId,
                OriginalQuantity = originalRemaining,
                NewQuantity = body.Quantity
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error splitting stack {InstanceId}", body.InstanceId);
            await _messageBus.TryPublishErrorAsync(
                "inventory", "SplitStack", "unexpected_exception", ex.Message,
                dependency: null, endpoint: "post:/inventory/split",
                details: null, stack: ex.StackTrace);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, MergeStacksResponse?)> MergeStacksAsync(
        MergeStacksRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Get both items
            ItemInstanceResponse source;
            ItemInstanceResponse target;
            try
            {
                source = await _navigator.Item.GetItemInstanceAsync(
                    new GetItemInstanceRequest { InstanceId = body.SourceInstanceId },
                    cancellationToken);
            }
            catch (ApiException ex)
            {
                _logger.LogWarning(ex, "Source item not found: {InstanceId}", body.SourceInstanceId);
                return (MapHttpStatusCode(ex.StatusCode), null);
            }

            try
            {
                target = await _navigator.Item.GetItemInstanceAsync(
                    new GetItemInstanceRequest { InstanceId = body.TargetInstanceId },
                    cancellationToken);
            }
            catch (ApiException ex)
            {
                _logger.LogWarning(ex, "Target item not found: {InstanceId}", body.TargetInstanceId);
                return (MapHttpStatusCode(ex.StatusCode), null);
            }

            if (source.TemplateId != target.TemplateId)
            {
                _logger.LogWarning("Cannot merge different templates");
                return (StatusCodes.BadRequest, null);
            }

            // Get template for max stack
            ItemTemplateResponse template;
            try
            {
                template = await _navigator.Item.GetItemTemplateAsync(
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

            var now = DateTimeOffset.UtcNow;

            // Update target quantity first (safer: if this fails, source is unaffected)
            try
            {
                await _navigator.Item.ModifyItemInstanceAsync(
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
                    await _navigator.Item.ModifyItemInstanceAsync(
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
                    await _navigator.Item.DestroyItemInstanceAsync(
                        new DestroyItemInstanceRequest
                        {
                            InstanceId = body.SourceInstanceId,
                            Reason = "merged"
                        }, cancellationToken);
                }
                catch (ApiException ex)
                {
                    _logger.LogWarning(ex, "Failed to destroy source item during merge: {StatusCode}", ex.StatusCode);
                }

                // Update container slot count since source is gone
                var containerStore = _stateStoreFactory.GetStore<ContainerModel>(StateStoreDefinitions.InventoryContainerStore);
                var container = await containerStore.GetAsync($"{CONT_PREFIX}{source.ContainerId}", cancellationToken);
                if (container is not null)
                {
                    container.UsedSlots = Math.Max(0, (container.UsedSlots ?? 0) - 1);
                    container.ModifiedAt = now;
                    await containerStore.SaveAsync($"{CONT_PREFIX}{source.ContainerId}", container, cancellationToken: cancellationToken);
                }
            }

            await _messageBus.TryPublishAsync("inventory-item.stacked", new InventoryItemStackedEvent
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

            return (StatusCodes.OK, new MergeStacksResponse
            {
                Success = true,
                TargetInstanceId = body.TargetInstanceId,
                NewQuantity = combinedQuantity,
                SourceDestroyed = !overflow.HasValue || overflow.Value <= 0,
                OverflowQuantity = overflow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error merging stacks");
            await _messageBus.TryPublishErrorAsync(
                "inventory", "MergeStacks", "unexpected_exception", ex.Message,
                dependency: null, endpoint: "post:/inventory/merge",
                details: null, stack: ex.StackTrace);
            return (StatusCodes.InternalServerError, null);
        }
    }

    #endregion

    #region Query Operations

    /// <inheritdoc/>
    public async Task<(StatusCodes, QueryItemsResponse?)> QueryItemsAsync(
        QueryItemsRequest body,
        CancellationToken cancellationToken = default)
    {
        try
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
                    var itemsResponse = await _navigator.Item.ListItemsByContainerAsync(
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
                            var template = await _navigator.Item.GetItemTemplateAsync(
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error querying items for owner {OwnerId}", body.OwnerId);
            await _messageBus.TryPublishErrorAsync(
                "inventory", "QueryItems", "unexpected_exception", ex.Message,
                dependency: null, endpoint: "post:/inventory/query",
                details: null, stack: ex.StackTrace);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, CountItemsResponse?)> CountItemsAsync(
        CountItemsRequest body,
        CancellationToken cancellationToken = default)
    {
        try
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error counting items");
            await _messageBus.TryPublishErrorAsync(
                "inventory", "CountItems", "unexpected_exception", ex.Message,
                dependency: null, endpoint: "post:/inventory/count",
                details: null, stack: ex.StackTrace);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, HasItemsResponse?)> HasItemsAsync(
        HasItemsRequest body,
        CancellationToken cancellationToken = default)
    {
        try
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking items");
            await _messageBus.TryPublishErrorAsync(
                "inventory", "HasItems", "unexpected_exception", ex.Message,
                dependency: null, endpoint: "post:/inventory/has",
                details: null, stack: ex.StackTrace);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, FindSpaceResponse?)> FindSpaceAsync(
        FindSpaceRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Get template for constraints
            ItemTemplateResponse template;
            try
            {
                template = await _navigator.Item.GetItemTemplateAsync(
                    new GetItemTemplateRequest { TemplateId = body.TemplateId },
                    cancellationToken);
            }
            catch (ApiException ex)
            {
                _logger.LogWarning(ex, "Template not found: {TemplateId}", body.TemplateId);
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
                        var itemsResponse = await _navigator.Item.ListItemsByContainerAsync(
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error finding space for item");
            await _messageBus.TryPublishErrorAsync(
                "inventory", "FindSpace", "unexpected_exception", ex.Message,
                dependency: null, endpoint: "post:/inventory/find-space",
                details: null, stack: ex.StackTrace);
            return (StatusCodes.InternalServerError, null);
        }
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
            case ContainerConstraintModel.Slot_only:
                if (container.MaxSlots.HasValue && (container.UsedSlots ?? 0) >= container.MaxSlots.Value)
                    return "Container is full (slots)";
                break;

            case ContainerConstraintModel.Weight_only:
                if (container.MaxWeight.HasValue && template.Weight.HasValue)
                {
                    var newWeight = container.ContentsWeight + template.Weight.Value * quantity;
                    if (newWeight > container.MaxWeight.Value)
                        return "Container is full (weight)";
                }
                break;

            case ContainerConstraintModel.Slot_and_weight:
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
            case ContainerConstraintModel.Slot_only:
                if (container.MaxSlots.HasValue && (container.UsedSlots ?? 0) >= container.MaxSlots.Value)
                    return "Container is full (slots)";
                break;

            case ContainerConstraintModel.Weight_only:
                if (container.MaxWeight.HasValue && template.Weight.HasValue)
                {
                    var newWeight = container.ContentsWeight + template.Weight.Value * quantity;
                    if (newWeight > container.MaxWeight.Value)
                        return "Container is full (weight)";
                }
                break;

            case ContainerConstraintModel.Slot_and_weight:
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
    /// Emits a container full event if the container has reached its capacity.
    /// </summary>
    private async Task EmitContainerFullEventIfNeededAsync(
        ContainerModel container,
        DateTimeOffset timestamp,
        CancellationToken cancellationToken)
    {
        string? constraintType = null;

        switch (container.ConstraintModel)
        {
            case ContainerConstraintModel.Slot_only:
            case ContainerConstraintModel.Slot_and_weight:
                if (container.MaxSlots.HasValue && (container.UsedSlots ?? 0) >= container.MaxSlots.Value)
                    constraintType = "slots";
                break;

            case ContainerConstraintModel.Weight_only:
                if (container.MaxWeight.HasValue && container.ContentsWeight >= container.MaxWeight.Value)
                    constraintType = "weight";
                break;

            case ContainerConstraintModel.Grid:
                if (container.MaxSlots.HasValue && (container.UsedSlots ?? 0) >= container.MaxSlots.Value)
                    constraintType = "grid";
                break;

            case ContainerConstraintModel.Volumetric:
                if (container.MaxVolume.HasValue && (container.CurrentVolume ?? 0) >= container.MaxVolume.Value)
                    constraintType = "volume";
                break;
        }

        // Also check weight for slot_and_weight
        if (constraintType is null && container.ConstraintModel == ContainerConstraintModel.Slot_and_weight)
        {
            if (container.MaxWeight.HasValue && container.ContentsWeight >= container.MaxWeight.Value)
                constraintType = "weight";
        }

        if (constraintType is not null)
        {
            await _messageBus.TryPublishAsync("inventory-container.full", new InventoryContainerFullEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = timestamp,
                ContainerId = container.ContainerId,
                OwnerId = container.OwnerId,
                OwnerType = container.OwnerType.ToString(),
                ContainerType = container.ContainerType,
                ConstraintType = constraintType
            }, cancellationToken);
        }
    }

    /// <summary>
    /// Adds a value to a JSON-serialized list in the state store.
    /// </summary>
    private async Task AddToListAsync(string storeName, string key, string value, CancellationToken ct)
    {
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
        var key = $"{CONT_PREFIX}{container.ContainerId}";
        var store = _stateStoreFactory.GetStore<ContainerModel>(StateStoreDefinitions.InventoryContainerStore);
        await store.SaveAsync(key, container, cancellationToken: ct);

        // Update Redis cache after MySQL write
        await UpdateContainerCacheAsync(key, container, ct);
    }

    #endregion
}

#region Internal Models

/// <summary>
/// Internal storage model for containers.
/// Uses proper typed fields for enums and GUIDs to avoid string roundtripping.
/// </summary>
internal class ContainerModel
{
    /// <summary>Container unique identifier</summary>
    public Guid ContainerId { get; set; }

    /// <summary>Owner entity ID</summary>
    public Guid OwnerId { get; set; }

    /// <summary>Owner type</summary>
    public ContainerOwnerType OwnerType { get; set; }

    /// <summary>Game-defined container type</summary>
    public string ContainerType { get; set; } = string.Empty;

    /// <summary>Capacity constraint model</summary>
    public ContainerConstraintModel ConstraintModel { get; set; }

    /// <summary>Whether this is an equipment slot</summary>
    public bool IsEquipmentSlot { get; set; }

    /// <summary>Equipment slot name if applicable</summary>
    public string? EquipmentSlotName { get; set; }

    /// <summary>Maximum slots for slot-based containers</summary>
    public int? MaxSlots { get; set; }

    /// <summary>Current used slots</summary>
    public int? UsedSlots { get; set; }

    /// <summary>Maximum weight capacity</summary>
    public double? MaxWeight { get; set; }

    /// <summary>Internal grid width</summary>
    public int? GridWidth { get; set; }

    /// <summary>Internal grid height</summary>
    public int? GridHeight { get; set; }

    /// <summary>Maximum volume</summary>
    public double? MaxVolume { get; set; }

    /// <summary>Current volume used</summary>
    public double? CurrentVolume { get; set; }

    /// <summary>Parent container ID for nested containers</summary>
    public Guid? ParentContainerId { get; set; }

    /// <summary>Depth in container hierarchy</summary>
    public int NestingDepth { get; set; }

    /// <summary>Whether can hold other containers</summary>
    public bool CanContainContainers { get; set; }

    /// <summary>Max nesting depth</summary>
    public int? MaxNestingDepth { get; set; }

    /// <summary>Empty container weight</summary>
    public double SelfWeight { get; set; }

    /// <summary>Weight propagation mode</summary>
    public WeightContribution WeightContribution { get; set; }

    /// <summary>Slots used in parent</summary>
    public int SlotCost { get; set; }

    /// <summary>Width in parent grid</summary>
    public int? ParentGridWidth { get; set; }

    /// <summary>Height in parent grid</summary>
    public int? ParentGridHeight { get; set; }

    /// <summary>Volume in parent</summary>
    public double? ParentVolume { get; set; }

    /// <summary>Weight of direct contents</summary>
    public double ContentsWeight { get; set; }

    /// <summary>Allowed item categories</summary>
    public List<string>? AllowedCategories { get; set; }

    /// <summary>Forbidden item categories</summary>
    public List<string>? ForbiddenCategories { get; set; }

    /// <summary>Required item tags</summary>
    public List<string>? AllowedTags { get; set; }

    /// <summary>Realm this container belongs to</summary>
    public Guid? RealmId { get; set; }

    /// <summary>Container tags</summary>
    public List<string> Tags { get; set; } = new();

    /// <summary>Serialized game-specific metadata</summary>
    public string? Metadata { get; set; }

    /// <summary>Creation timestamp</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Last modification timestamp</summary>
    public DateTimeOffset? ModifiedAt { get; set; }
}

#endregion
