namespace BeyondImmersion.BannouService.Status;

/// <summary>
/// Internal data models for StatusService.
/// </summary>
/// <remarks>
/// <para>
/// Storage models for MySQL state stores, cache models for Redis, and helper types.
/// All models use proper C# types per IMPLEMENTATION TENETS (enums, Guids, DateTimeOffset).
/// </para>
/// </remarks>
public partial class StatusService
{
    // Partial class linkage for models defined at namespace level below.
}

// ============================================================================
// STORAGE MODELS (MySQL)
// ============================================================================

/// <summary>
/// Internal storage model for status template definitions.
/// Stored in status-templates MySQL store with dual keys (by ID and by gameServiceId:code).
/// </summary>
internal class StatusTemplateModel
{
    /// <summary>Unique template identifier.</summary>
    public Guid StatusTemplateId { get; set; }

    /// <summary>Game service this template is scoped to.</summary>
    public Guid GameServiceId { get; set; }

    /// <summary>Unique status code within this game service.</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>Human-readable display name.</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Description of the status effect.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Classification of this status effect.</summary>
    public StatusCategory Category { get; set; }

    /// <summary>Whether this status can stack.</summary>
    public bool Stackable { get; set; }

    /// <summary>Maximum number of stacks.</summary>
    public int MaxStacks { get; set; }

    /// <summary>How multiple applications interact.</summary>
    public StackBehavior StackBehavior { get; set; }

    /// <summary>Optional contract template for lifecycle management.</summary>
    public Guid? ContractTemplateId { get; set; }

    /// <summary>Required item template reference for the "items in inventories" pattern.</summary>
    public Guid ItemTemplateId { get; set; }

    /// <summary>Default duration in seconds (null for permanent statuses).</summary>
    public int? DefaultDurationSeconds { get; set; }

    /// <summary>Optional icon asset reference.</summary>
    public Guid? IconAssetId { get; set; }

    /// <summary>When this template was created.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>When this template was last updated.</summary>
    public DateTimeOffset? UpdatedAt { get; set; }
}

/// <summary>
/// Internal storage model for active status instances.
/// Stored in status-instances MySQL store keyed by instance ID.
/// </summary>
internal class StatusInstanceModel
{
    /// <summary>Unique instance identifier.</summary>
    public Guid StatusInstanceId { get; set; }

    /// <summary>Entity that has this status effect.</summary>
    public Guid EntityId { get; set; }

    /// <summary>Entity type discriminator per IMPLEMENTATION TENETS.</summary>
    public EntityType EntityType { get; set; }

    /// <summary>Game service scope for this instance.</summary>
    public Guid GameServiceId { get; set; }

    /// <summary>Status template code that was granted.</summary>
    public string StatusTemplateCode { get; set; } = string.Empty;

    /// <summary>Classification of this status effect.</summary>
    public StatusCategory Category { get; set; }

    /// <summary>Current stack count.</summary>
    public int StackCount { get; set; }

    /// <summary>What granted this status (e.g., ability ID, item ID).</summary>
    public Guid? SourceId { get; set; }

    /// <summary>Associated contract instance for lifecycle management.</summary>
    public Guid? ContractInstanceId { get; set; }

    /// <summary>Associated item instance in the status container.</summary>
    public Guid ItemInstanceId { get; set; }

    /// <summary>When this status was granted.</summary>
    public DateTimeOffset GrantedAt { get; set; }

    /// <summary>When this status expires (null for permanent).</summary>
    public DateTimeOffset? ExpiresAt { get; set; }

    /// <summary>Optional metadata from the grant request.</summary>
    public Dictionary<string, object>? Metadata { get; set; }
}

/// <summary>
/// Maps an entity to its inventory container for a game service.
/// Stored in status-containers MySQL store with dual keys (by ID and by entity composite).
/// </summary>
internal class StatusContainerModel
{
    /// <summary>Inventory container ID from lib-inventory.</summary>
    public Guid ContainerId { get; set; }

    /// <summary>Entity that owns this status container.</summary>
    public Guid EntityId { get; set; }

    /// <summary>Entity type discriminator.</summary>
    public EntityType EntityType { get; set; }

    /// <summary>Game service this container is scoped to.</summary>
    public Guid GameServiceId { get; set; }
}

// ============================================================================
// CACHE MODELS (Redis)
// ============================================================================

/// <summary>
/// Cached list of active item-based statuses per entity.
/// Stored in status-active-cache Redis store, rebuilt from MySQL on cache miss.
/// </summary>
internal class ActiveStatusCacheModel
{
    /// <summary>Entity this cache entry is for.</summary>
    public Guid EntityId { get; set; }

    /// <summary>Entity type discriminator.</summary>
    public EntityType EntityType { get; set; }

    /// <summary>Cached active status entries.</summary>
    public List<CachedStatusEntry> Statuses { get; set; } = new();

    /// <summary>When this cache entry was built.</summary>
    public DateTimeOffset CachedAt { get; set; }
}

/// <summary>
/// Cache entry for a single active item-based status.
/// </summary>
internal class CachedStatusEntry
{
    /// <summary>Status instance identifier.</summary>
    public Guid StatusInstanceId { get; set; }

    /// <summary>Status template code.</summary>
    public string StatusTemplateCode { get; set; } = string.Empty;

    /// <summary>Classification category.</summary>
    public StatusCategory Category { get; set; }

    /// <summary>Current stack count.</summary>
    public int StackCount { get; set; }

    /// <summary>What granted this status.</summary>
    public Guid? SourceId { get; set; }

    /// <summary>When this status expires.</summary>
    public DateTimeOffset? ExpiresAt { get; set; }
}

/// <summary>
/// Cached seed-derived capability effects per entity.
/// Stored in status-seed-effects-cache Redis store, invalidated on seed capability changes.
/// </summary>
internal class SeedEffectsCacheModel
{
    /// <summary>Entity this cache entry is for.</summary>
    public Guid EntityId { get; set; }

    /// <summary>Entity type discriminator.</summary>
    public EntityType EntityType { get; set; }

    /// <summary>Cached seed-derived effects.</summary>
    public List<CachedSeedEffect> Effects { get; set; } = new();

    /// <summary>When this cache entry was built.</summary>
    public DateTimeOffset CachedAt { get; set; }
}

/// <summary>
/// Cache entry for a single seed-derived passive effect.
/// </summary>
internal class CachedSeedEffect
{
    /// <summary>Capability identifier code.</summary>
    public string CapabilityCode { get; set; } = string.Empty;

    /// <summary>Growth domain governing this capability.</summary>
    public string Domain { get; set; } = string.Empty;

    /// <summary>Current fidelity value (0.0-1.0).</summary>
    public float Fidelity { get; set; }

    /// <summary>Seed that provides this capability.</summary>
    public Guid SeedId { get; set; }

    /// <summary>Seed type code.</summary>
    public string SeedTypeCode { get; set; } = string.Empty;
}
