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
    private readonly ILogger<InventoryService> _logger;
    private readonly InventoryServiceConfiguration _configuration;

    // Container store key prefixes
    private const string CONT_PREFIX = "cont:";
    private const string CONT_OWNER_INDEX = "cont-owner:";
    private const string CONT_TYPE_INDEX = "cont-type:";

    /// <summary>
    /// Initializes a new instance of the InventoryService.
    /// </summary>
    public InventoryService(
        IMessageBus messageBus,
        IServiceNavigator navigator,
        IStateStoreFactory stateStoreFactory,
        ILogger<InventoryService> logger,
        InventoryServiceConfiguration configuration)
    {
        _messageBus = messageBus ?? throw new ArgumentNullException(nameof(messageBus));
        _navigator = navigator ?? throw new ArgumentNullException(nameof(navigator));
        _stateStoreFactory = stateStoreFactory ?? throw new ArgumentNullException(nameof(stateStoreFactory));
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

            var model = new ContainerModel
            {
                ContainerId = containerId.ToString(),
                OwnerId = body.OwnerId.ToString(),
                OwnerType = body.OwnerType.ToString(),
                ContainerType = body.ContainerType,
                ConstraintModel = body.ConstraintModel.ToString(),
                IsEquipmentSlot = body.IsEquipmentSlot,
                EquipmentSlotName = body.EquipmentSlotName,
                MaxSlots = body.MaxSlots ?? _configuration.DefaultMaxSlots,
                MaxWeight = body.MaxWeight ?? _configuration.DefaultMaxWeight,
                GridWidth = body.GridWidth,
                GridHeight = body.GridHeight,
                MaxVolume = body.MaxVolume,
                ParentContainerId = body.ParentContainerId?.ToString(),
                NestingDepth = nestingDepth,
                CanContainContainers = body.CanContainContainers,
                MaxNestingDepth = body.MaxNestingDepth ?? _configuration.DefaultMaxNestingDepth,
                SelfWeight = body.SelfWeight,
                WeightContribution = body.WeightContribution.ToString(),
                SlotCost = body.SlotCost,
                ParentGridWidth = body.ParentGridWidth,
                ParentGridHeight = body.ParentGridHeight,
                ParentVolume = body.ParentVolume,
                AllowedCategories = body.AllowedCategories?.ToList(),
                ForbiddenCategories = body.ForbiddenCategories?.ToList(),
                AllowedTags = body.AllowedTags?.ToList(),
                RealmId = body.RealmId?.ToString(),
                Tags = body.Tags?.ToList() ?? new List<string>(),
                Metadata = body.Metadata is not null ? BannouJson.Serialize(body.Metadata) : null,
                ContentsWeight = 0,
                UsedSlots = 0,
                CurrentVolume = 0,
                CreatedAt = now
            };

            await containerStore.SaveAsync($"{CONT_PREFIX}{containerId}", model, cancellationToken: cancellationToken);
            await AddToListAsync(StateStoreDefinitions.InventoryContainerStore,
                $"{CONT_OWNER_INDEX}{body.OwnerType}:{body.OwnerId}", containerId.ToString(), cancellationToken);
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
            var containerStore = _stateStoreFactory.GetStore<ContainerModel>(StateStoreDefinitions.InventoryContainerStore);
            var model = await containerStore.GetAsync($"{CONT_PREFIX}{body.ContainerId}", cancellationToken);

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
            var containerStore = _stateStoreFactory.GetStore<ContainerModel>(StateStoreDefinitions.InventoryContainerStore);
            var stringStore = _stateStoreFactory.GetStore<string>(StateStoreDefinitions.InventoryContainerStore);

            // Look for existing container by owner + type
            var ownerKey = $"{CONT_OWNER_INDEX}{body.OwnerType}:{body.OwnerId}";
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

            var ownerKey = $"{CONT_OWNER_INDEX}{body.OwnerType}:{body.OwnerId}";
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
                if (body.RealmId.HasValue && model.RealmId != body.RealmId.Value.ToString()) continue;

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
            var containerStore = _stateStoreFactory.GetStore<ContainerModel>(StateStoreDefinitions.InventoryContainerStore);
            var model = await containerStore.GetAsync($"{CONT_PREFIX}{body.ContainerId}", cancellationToken);

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

            await containerStore.SaveAsync($"{CONT_PREFIX}{body.ContainerId}", model, cancellationToken: cancellationToken);

            await _messageBus.TryPublishAsync("inventory-container.updated", new InventoryContainerUpdatedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = now,
                ContainerId = body.ContainerId,
                OwnerId = Guid.Parse(model.OwnerId),
                OwnerType = model.OwnerType,
                ContainerType = model.ContainerType,
                ConstraintModel = model.ConstraintModel,
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
            var containerStore = _stateStoreFactory.GetStore<ContainerModel>(StateStoreDefinitions.InventoryContainerStore);
            var model = await containerStore.GetAsync($"{CONT_PREFIX}{body.ContainerId}", cancellationToken);

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
            await RemoveFromListAsync(StateStoreDefinitions.InventoryContainerStore,
                $"{CONT_OWNER_INDEX}{model.OwnerType}:{model.OwnerId}", body.ContainerId.ToString(), cancellationToken);
            await RemoveFromListAsync(StateStoreDefinitions.InventoryContainerStore,
                $"{CONT_TYPE_INDEX}{model.ContainerType}", body.ContainerId.ToString(), cancellationToken);

            await containerStore.DeleteAsync($"{CONT_PREFIX}{body.ContainerId}", cancellationToken);

            await _messageBus.TryPublishAsync("inventory-container.deleted", new InventoryContainerDeletedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = now,
                ContainerId = body.ContainerId,
                OwnerId = Guid.Parse(model.OwnerId),
                OwnerType = model.OwnerType,
                ContainerType = model.ContainerType,
                ConstraintModel = model.ConstraintModel,
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
            var containerStore = _stateStoreFactory.GetStore<ContainerModel>(StateStoreDefinitions.InventoryContainerStore);
            var container = await containerStore.GetAsync($"{CONT_PREFIX}{body.ContainerId}", cancellationToken);

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
            if (container.AllowedCategories is not null && container.AllowedCategories.Count > 0)
            {
                if (!container.AllowedCategories.Contains(template.Category.ToString()))
                {
                    _logger.LogWarning("Category {Category} not allowed in container {ContainerId}",
                        template.Category, body.ContainerId);
                    return (StatusCodes.BadRequest, null);
                }
            }

            if (container.ForbiddenCategories is not null && container.ForbiddenCategories.Count > 0)
            {
                if (container.ForbiddenCategories.Contains(template.Category.ToString()))
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

            await containerStore.SaveAsync($"{CONT_PREFIX}{body.ContainerId}", container, cancellationToken: cancellationToken);

            await _messageBus.TryPublishAsync("inventory-item.placed", new InventoryItemPlacedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = now,
                InstanceId = body.InstanceId,
                TemplateId = item.TemplateId,
                ContainerId = body.ContainerId,
                OwnerId = Guid.Parse(container.OwnerId),
                OwnerType = container.OwnerType,
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

            var containerStore = _stateStoreFactory.GetStore<ContainerModel>(StateStoreDefinitions.InventoryContainerStore);
            var container = await containerStore.GetAsync($"{CONT_PREFIX}{item.ContainerId}", cancellationToken);

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
            await containerStore.SaveAsync($"{CONT_PREFIX}{item.ContainerId}", container, cancellationToken: cancellationToken);

            await _messageBus.TryPublishAsync("inventory-item.removed", new InventoryItemRemovedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = now,
                InstanceId = body.InstanceId,
                TemplateId = item.TemplateId,
                ContainerId = item.ContainerId,
                OwnerId = Guid.Parse(container.OwnerId),
                OwnerType = container.OwnerType
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
                // Update item slot position via modify
                try
                {
                    await _navigator.Item.ModifyItemInstanceAsync(
                        new ModifyItemInstanceRequest
                        {
                            InstanceId = body.InstanceId,
                            InstanceMetadata = new Dictionary<string, object>
                            {
                                ["slotIndex"] = body.TargetSlotIndex ?? 0,
                                ["slotX"] = body.TargetSlotX ?? 0,
                                ["slotY"] = body.TargetSlotY ?? 0,
                                ["rotated"] = body.Rotated ?? false
                            }
                        }, cancellationToken);
                }
                catch (ApiException ex)
                {
                    _logger.LogWarning(ex, "Failed to modify item position: {StatusCode}", ex.StatusCode);
                }

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
            var containerStore = _stateStoreFactory.GetStore<ContainerModel>(StateStoreDefinitions.InventoryContainerStore);
            var targetContainer = await containerStore.GetAsync($"{CONT_PREFIX}{body.TargetContainerId}", cancellationToken);

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
                SourceOwnerId = Guid.Parse(sourceContainer.OwnerId),
                SourceOwnerType = sourceContainer.OwnerType,
                TargetContainerId = body.TargetContainerId,
                TargetOwnerId = Guid.Parse(targetContainer.OwnerId),
                TargetOwnerType = targetContainer.OwnerType,
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

            var newQuantity = source.Quantity + target.Quantity;
            double? overflow = null;

            if (newQuantity > template.MaxStackSize)
            {
                overflow = newQuantity - template.MaxStackSize;
                newQuantity = template.MaxStackSize;
            }

            var now = DateTimeOffset.UtcNow;

            // Destroy source
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

            await _messageBus.TryPublishAsync("inventory-item.stacked", new InventoryItemStackedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = now,
                SourceInstanceId = body.SourceInstanceId,
                TargetInstanceId = body.TargetInstanceId,
                TemplateId = source.TemplateId,
                ContainerId = target.ContainerId,
                QuantityAdded = source.Quantity,
                NewTotalQuantity = newQuantity
            }, cancellationToken);

            return (StatusCodes.OK, new MergeStacksResponse
            {
                Success = true,
                TargetInstanceId = body.TargetInstanceId,
                NewQuantity = newQuantity,
                SourceDestroyed = true,
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

                            if (!string.IsNullOrEmpty(body.Category) && template.Category.ToString() != body.Category) continue;
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
            var (queryStatus, queryResponse) = await QueryItemsAsync(
                new QueryItemsRequest
                {
                    OwnerId = body.OwnerId,
                    OwnerType = body.OwnerType,
                    TemplateId = body.TemplateId,
                    Limit = 1000
                }, cancellationToken);

            if (queryStatus != StatusCodes.OK || queryResponse is null)
            {
                return (queryStatus, null);
            }

            var totalQuantity = queryResponse.Items.Sum(i => i.Quantity);

            return (StatusCodes.OK, new CountItemsResponse
            {
                TemplateId = body.TemplateId,
                TotalQuantity = totalQuantity,
                StackCount = queryResponse.Items.Count
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

            foreach (var container in containersResponse.Containers)
            {
                // Check if item category is allowed
                if (container.AllowedCategories is not null && container.AllowedCategories.Count > 0)
                {
                    if (!container.AllowedCategories.Contains(template.Category.ToString())) continue;
                }
                if (container.ForbiddenCategories is not null && container.ForbiddenCategories.Count > 0)
                {
                    if (container.ForbiddenCategories.Contains(template.Category.ToString())) continue;
                }

                // Check constraints
                var containerModel = new ContainerModel
                {
                    ConstraintModel = container.ConstraintModel.ToString(),
                    MaxSlots = container.MaxSlots,
                    UsedSlots = container.UsedSlots,
                    MaxWeight = container.MaxWeight,
                    ContentsWeight = container.ContentsWeight,
                    MaxVolume = container.MaxVolume,
                    CurrentVolume = container.CurrentVolume
                };

                var violation = CheckConstraints(containerModel, template, body.Quantity);
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

    private static string? CheckConstraints(
        ContainerModel container,
        ItemTemplateResponse template,
        double quantity)
    {
        var constraintModel = Enum.Parse<ContainerConstraintModel>(container.ConstraintModel);

        switch (constraintModel)
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

    private static ContainerResponse MapContainerToResponse(ContainerModel model)
    {
        return new ContainerResponse
        {
            ContainerId = Guid.Parse(model.ContainerId),
            OwnerId = Guid.Parse(model.OwnerId),
            OwnerType = Enum.Parse<ContainerOwnerType>(model.OwnerType),
            ContainerType = model.ContainerType,
            ConstraintModel = Enum.Parse<ContainerConstraintModel>(model.ConstraintModel),
            IsEquipmentSlot = model.IsEquipmentSlot,
            EquipmentSlotName = model.EquipmentSlotName,
            MaxSlots = model.MaxSlots,
            UsedSlots = model.UsedSlots,
            MaxWeight = model.MaxWeight,
            GridWidth = model.GridWidth,
            GridHeight = model.GridHeight,
            MaxVolume = model.MaxVolume,
            CurrentVolume = model.CurrentVolume,
            ParentContainerId = model.ParentContainerId is not null ? Guid.Parse(model.ParentContainerId) : null,
            NestingDepth = model.NestingDepth,
            CanContainContainers = model.CanContainContainers,
            MaxNestingDepth = model.MaxNestingDepth,
            SelfWeight = model.SelfWeight,
            WeightContribution = Enum.Parse<WeightContribution>(model.WeightContribution),
            SlotCost = model.SlotCost,
            ParentGridWidth = model.ParentGridWidth,
            ParentGridHeight = model.ParentGridHeight,
            ParentVolume = model.ParentVolume,
            ContentsWeight = model.ContentsWeight,
            TotalWeight = model.SelfWeight + model.ContentsWeight,
            AllowedCategories = model.AllowedCategories,
            ForbiddenCategories = model.ForbiddenCategories,
            AllowedTags = model.AllowedTags,
            RealmId = model.RealmId is not null ? Guid.Parse(model.RealmId) : null,
            Tags = model.Tags,
            Metadata = model.Metadata is not null ? BannouJson.Deserialize<object>(model.Metadata) : null,
            CreatedAt = model.CreatedAt,
            ModifiedAt = model.ModifiedAt
        };
    }

    #endregion
}

#region Internal Models

/// <summary>
/// Internal storage model for containers.
/// </summary>
internal class ContainerModel
{
    public string ContainerId { get; set; } = string.Empty;
    public string OwnerId { get; set; } = string.Empty;
    public string OwnerType { get; set; } = string.Empty;
    public string ContainerType { get; set; } = string.Empty;
    public string ConstraintModel { get; set; } = string.Empty;
    public bool IsEquipmentSlot { get; set; }
    public string? EquipmentSlotName { get; set; }
    public int? MaxSlots { get; set; }
    public int? UsedSlots { get; set; }
    public double? MaxWeight { get; set; }
    public int? GridWidth { get; set; }
    public int? GridHeight { get; set; }
    public double? MaxVolume { get; set; }
    public double? CurrentVolume { get; set; }
    public string? ParentContainerId { get; set; }
    public int NestingDepth { get; set; }
    public bool CanContainContainers { get; set; }
    public int? MaxNestingDepth { get; set; }
    public double SelfWeight { get; set; }
    public string WeightContribution { get; set; } = string.Empty;
    public int SlotCost { get; set; }
    public int? ParentGridWidth { get; set; }
    public int? ParentGridHeight { get; set; }
    public double? ParentVolume { get; set; }
    public double ContentsWeight { get; set; }
    public List<string>? AllowedCategories { get; set; }
    public List<string>? ForbiddenCategories { get; set; }
    public List<string>? AllowedTags { get; set; }
    public string? RealmId { get; set; }
    public List<string> Tags { get; set; } = new();
    public string? Metadata { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ModifiedAt { get; set; }
}

#endregion
