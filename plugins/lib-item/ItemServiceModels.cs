using BeyondImmersion.BannouService.Events;

namespace BeyondImmersion.BannouService.Item;

/// <summary>
/// Internal data models for ItemService.
/// </summary>
/// <remarks>
/// <para>
/// This file contains internal data models, DTOs, and helper structures used
/// exclusively by this service. These are NOT exposed via the API and are NOT
/// generated from schemas.
/// </para>
/// <para>
/// <b>When to add models here:</b>
/// <list type="bullet">
///   <item>Storage models for state stores (different from API request/response types)</item>
///   <item>Cache entry structures</item>
///   <item>Internal DTOs for service-to-service communication not exposed in API</item>
///   <item>Helper records for intermediate processing</item>
/// </list>
/// </para>
/// <para>
/// <b>IMPLEMENTATION TENETS - Type Safety:</b> Internal models MUST use proper C# types
/// (enums, Guids, DateTimeOffset) - never string representations. "JSON requires strings"
/// is FALSE - BannouJson handles serialization correctly.
/// </para>
/// </remarks>
public partial class ItemService
{
    // This partial class declaration exists to signal that the models below
    // are owned by and used exclusively by this service. The models themselves
    // are defined at namespace level as internal classes.
}

/// <summary>
/// Internal storage model for item templates.
/// </summary>
internal class ItemTemplateModel
{
    public Guid TemplateId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string GameId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public ItemCategory Category { get; set; }
    public string? Subcategory { get; set; }
    public List<string> Tags { get; set; } = new();
    public ItemRarity Rarity { get; set; }
    public QuantityModel QuantityModel { get; set; }
    public int MaxStackSize { get; set; }
    public string? UnitOfMeasure { get; set; }
    public WeightPrecision WeightPrecision { get; set; }
    public double? Weight { get; set; }
    public double? Volume { get; set; }
    public int? GridWidth { get; set; }
    public int? GridHeight { get; set; }
    public bool? CanRotate { get; set; }
    public double? BaseValue { get; set; }
    public bool Tradeable { get; set; }
    public bool Destroyable { get; set; }
    public SoulboundType SoulboundType { get; set; }
    public bool HasDurability { get; set; }
    public int? MaxDurability { get; set; }
    public ItemScope Scope { get; set; }
    public List<Guid>? AvailableRealms { get; set; }
    public string? Stats { get; set; }
    public string? Effects { get; set; }
    public string? Requirements { get; set; }
    public string? Display { get; set; }
    public string? Metadata { get; set; }
    public bool IsActive { get; set; }
    public bool IsDeprecated { get; set; }
    public DateTimeOffset? DeprecatedAt { get; set; }
    public Guid? MigrationTargetId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>
    /// Contract template ID for executable item behavior.
    /// When set, the item can be "used" via /item/use endpoint.
    /// </summary>
    public Guid? UseBehaviorContractTemplateId { get; set; }

    /// <summary>
    /// Contract template for pre-use validation.
    /// When set, /item/use first executes this contract's "validate" milestone.
    /// </summary>
    public Guid? CanUseBehaviorContractTemplateId { get; set; }

    /// <summary>
    /// Contract template executed when the main use behavior fails.
    /// Enables cleanup, partial rollback, or consequence application.
    /// </summary>
    public Guid? OnUseFailedBehaviorContractTemplateId { get; set; }

    /// <summary>
    /// Controls item consumption on use. Defaults to destroy_on_success.
    /// </summary>
    public ItemUseBehavior ItemUseBehavior { get; set; } = ItemUseBehavior.DestroyOnSuccess;

    /// <summary>
    /// Controls CanUse validation behavior. Defaults to block.
    /// </summary>
    public CanUseBehavior CanUseBehavior { get; set; } = CanUseBehavior.Block;
}

/// <summary>
/// Internal storage model for item instances.
/// </summary>
internal class ItemInstanceModel
{
    public Guid InstanceId { get; set; }
    public Guid TemplateId { get; set; }
    public Guid ContainerId { get; set; }
    public Guid RealmId { get; set; }
    public double Quantity { get; set; }
    public int? SlotIndex { get; set; }
    public int? SlotX { get; set; }
    public int? SlotY { get; set; }
    public bool? Rotated { get; set; }
    public int? CurrentDurability { get; set; }
    public Guid? BoundToId { get; set; }
    public DateTimeOffset? BoundAt { get; set; }
    public string? CustomStats { get; set; }
    public string? CustomName { get; set; }
    public string? InstanceMetadata { get; set; }
    public ItemOriginType OriginType { get; set; }
    public Guid? OriginId { get; set; }

    /// <summary>
    /// Bound contract instance ID for persistent item-contract bindings
    /// or active multi-step use sessions.
    /// </summary>
    public Guid? ContractInstanceId { get; set; }

    /// <summary>
    /// Type of contract binding. 'Session' bindings are managed by Item service
    /// for multi-step use. 'Lifecycle' bindings are managed by external orchestrators
    /// (lib-status, lib-license) and should NOT be modified by Item service.
    /// </summary>
    public ContractBindingType ContractBindingType { get; set; } = ContractBindingType.None;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ModifiedAt { get; set; }
}

/// <summary>
/// Tracks batched item use events within a deduplication window.
/// Thread-safe via lock on the instance.
/// </summary>
internal sealed class ItemUseBatchState
{
    private readonly object _lock = new();

    /// <summary>Unique identifier for this batch window.</summary>
    public Guid BatchId { get; } = Guid.NewGuid();

    /// <summary>When this batch window started.</summary>
    public DateTimeOffset WindowStart { get; } = DateTimeOffset.UtcNow;

    /// <summary>Individual use records in this batch.</summary>
    public List<ItemUseRecord> Records { get; } = new();

    /// <summary>Total count including any that were deduplicated (same instance used multiple times).</summary>
    public int TotalCount { get; private set; }

    /// <summary>
    /// Thread-safe addition of a use record to this batch.
    /// </summary>
    /// <param name="record">The use record to add.</param>
    /// <returns>Current total count after addition.</returns>
    public int AddRecord(ItemUseRecord record)
    {
        lock (_lock)
        {
            Records.Add(record);
            TotalCount++;
            return TotalCount;
        }
    }

    /// <summary>
    /// Thread-safe snapshot of current state for publishing.
    /// </summary>
    /// <returns>Tuple of (records copy, total count).</returns>
    public (List<ItemUseRecord> Records, int TotalCount) GetSnapshot()
    {
        lock (_lock)
        {
            return (new List<ItemUseRecord>(Records), TotalCount);
        }
    }
}

/// <summary>
/// Tracks batched item use failure events within a deduplication window.
/// Thread-safe via lock on the instance.
/// </summary>
internal sealed class ItemUseFailureBatchState
{
    private readonly object _lock = new();

    /// <summary>Unique identifier for this batch window.</summary>
    public Guid BatchId { get; } = Guid.NewGuid();

    /// <summary>When this batch window started.</summary>
    public DateTimeOffset WindowStart { get; } = DateTimeOffset.UtcNow;

    /// <summary>Individual failure records in this batch.</summary>
    public List<ItemUseFailureRecord> Records { get; } = new();

    /// <summary>Total count including any that were deduplicated.</summary>
    public int TotalCount { get; private set; }

    /// <summary>
    /// Thread-safe addition of a failure record to this batch.
    /// </summary>
    /// <param name="record">The failure record to add.</param>
    /// <returns>Current total count after addition.</returns>
    public int AddRecord(ItemUseFailureRecord record)
    {
        lock (_lock)
        {
            Records.Add(record);
            TotalCount++;
            return TotalCount;
        }
    }

    /// <summary>
    /// Thread-safe snapshot of current state for publishing.
    /// </summary>
    /// <returns>Tuple of (records copy, total count).</returns>
    public (List<ItemUseFailureRecord> Records, int TotalCount) GetSnapshot()
    {
        lock (_lock)
        {
            return (new List<ItemUseFailureRecord>(Records), TotalCount);
        }
    }
}
