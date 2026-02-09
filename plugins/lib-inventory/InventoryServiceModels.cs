namespace BeyondImmersion.BannouService.Inventory;

/// <summary>
/// Internal data models for InventoryService.
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
public partial class InventoryService
{
    // This partial class declaration exists to signal that the models below
    // are owned by and used exclusively by this service. The models themselves
    // are defined at namespace level as internal classes.
}

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
