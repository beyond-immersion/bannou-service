using BeyondImmersion.Bannou.Core;
using BeyondImmersion.Bannou.Inventory.ClientEvents;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Inventory.Caching;
using BeyondImmersion.BannouService.Item;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Providers;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Inventory;

// =============================================================================
// InventoryService — Private & Internal Helper Methods
// =============================================================================
//
// This partial class file is the designated home for all private and internal
// helper methods used by InventoryService. This separation exists to
// support the IMPLEMENTATION TENETS telemetry span rules:
//
//   - PRIMARY FILE (InventoryService.cs):
//     Contains ONLY public interface method implementations (the methods
//     declared in IInventoryService). These methods MUST NOT call
//     ITelemetryProvider.StartActivity because the generated controller
//     already wraps each endpoint with a telemetry span. Adding a span
//     in the service method would double-instrument the endpoint.
//
//   - THIS FILE (InventoryService.Helpers.cs):
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
/// Private and internal helper methods for InventoryService.
/// </summary>
/// <remarks>
/// <para>
/// This partial class contains all non-public helper methods. Every async method
/// in this file MUST include a <c>using var activity = _telemetryProvider.StartActivity(...)</c>
/// span per IMPLEMENTATION TENETS (T30). The generated controller instruments the
/// public interface methods; this file instruments the sub-operations.
/// </para>
/// </remarks>
public partial class InventoryService
{
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
    internal static string BuildOwnerIndexKey(ContainerOwnerType ownerType, Guid ownerId)
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
            await _messageBus.PublishInventoryContainerFullAsync(new InventoryContainerFullEvent
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
    private async Task AddToListAsync(IStateStore<string> store, string key, string value, CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.inventory", "InventoryService.AddToListAsync");
        await using var lockResponse = await _lockProvider.LockAsync(
            StateStoreDefinitions.InventoryContainerStore, key, Guid.NewGuid().ToString(), _configuration.ListLockTimeoutSeconds, ct);
        if (!lockResponse.Success)
        {
            _logger.LogWarning("Could not acquire list lock for {Key}", key);
            return;
        }

        var json = await store.GetAsync(key, ct);
        var list = string.IsNullOrEmpty(json)
            ? new List<string>()
            : BannouJson.Deserialize<List<string>>(json) ?? new List<string>();

        if (!list.Contains(value))
        {
            list.Add(value);
            await store.SaveAsync(key, BannouJson.Serialize(list), cancellationToken: ct);
        }
    }

    /// <summary>
    /// Removes a value from a JSON-serialized list in the state store.
    /// </summary>
    private async Task RemoveFromListAsync(IStateStore<string> store, string key, string value, CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.inventory", "InventoryService.RemoveFromListAsync");
        await using var lockResponse = await _lockProvider.LockAsync(
            StateStoreDefinitions.InventoryContainerStore, key, Guid.NewGuid().ToString(), _configuration.ListLockTimeoutSeconds, ct);
        if (!lockResponse.Success)
        {
            _logger.LogWarning("Could not acquire list lock for {Key}", key);
            return;
        }

        var json = await store.GetAsync(key, ct);
        if (string.IsNullOrEmpty(json)) return;

        var list = BannouJson.Deserialize<List<string>>(json) ?? new List<string>();
        if (list.Remove(value))
        {
            await store.SaveAsync(key, BannouJson.Serialize(list), cancellationToken: ct);
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
    /// </summary>
    private async Task<ContainerModel?> TryGetContainerFromCacheAsync(string key, CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.inventory", "InventoryService.TryGetContainerFromCacheAsync");
        try
        {
            return await _containerCache.GetAsync(key, ct);
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
    /// </summary>
    private async Task UpdateContainerCacheAsync(string key, ContainerModel container, CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.inventory", "InventoryService.UpdateContainerCacheAsync");
        try
        {
            await _containerCache.SaveAsync(key, container, new StateOptions { Ttl = _configuration.ContainerCacheTtlSeconds }, ct);
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
            await _containerCache.DeleteAsync(key, ct);
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
        var container = await _containerStore.GetAsync(key, ct);

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
        await _containerStore.SaveAsync(key, container, cancellationToken: ct);

        // Update Redis cache after MySQL write
        await UpdateContainerCacheAsync(key, container, ct);
    }

    #endregion
}
