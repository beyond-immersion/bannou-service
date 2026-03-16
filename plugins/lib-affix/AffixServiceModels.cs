namespace BeyondImmersion.BannouService.Affix;

/// <summary>
/// Internal data models for AffixService state stores.
/// </summary>
public partial class AffixService
{
    #region Key Constants

    private const string DEF_KEY_PREFIX = "def:";
    private const string DEF_CODE_KEY_PREFIX = "def-code:";
    private const string IMPL_KEY_PREFIX = "impl:";
    private const string IMPL_TPL_KEY_PREFIX = "impl-tpl:";
    private const string INST_KEY_PREFIX = "inst:";
    private const string INST_GAME_INDEX_PREFIX = "inst-game:";
    private const string INST_DEF_INDEX_PREFIX = "inst-def:";
    private const string DEF_CACHE_PREFIX = "def:";
    private const string DEF_GROUP_CACHE_PREFIX = "def-group:";
    private const string INST_CACHE_PREFIX = "inst:";
    private const string STATS_CACHE_PREFIX = "stats:";
    private const string EQUIP_CACHE_PREFIX = "equip:";
    private const string POOL_CACHE_PREFIX = "pool:";
    private const string POOL_INF_CACHE_PREFIX = "pool-inf:";
    private const string DEF_LOCK_PREFIX = "def:";
    private const string ITEM_LOCK_PREFIX = "item:";
    private const string POOL_REBUILD_LOCK_PREFIX = "pool-rebuild:";

    #endregion

    #region Key Building Helpers

    /// <summary>Builds primary definition lookup key.</summary>
    internal static string BuildDefinitionKey(Guid definitionId)
        => $"{DEF_KEY_PREFIX}{definitionId}";

    /// <summary>Builds code-uniqueness lookup key within a game service.</summary>
    internal static string BuildDefinitionCodeKey(Guid gameServiceId, string code)
        => $"{DEF_CODE_KEY_PREFIX}{gameServiceId}:{code}";

    /// <summary>Builds primary implicit mapping lookup key.</summary>
    internal static string BuildImplicitMappingKey(Guid mappingId)
        => $"{IMPL_KEY_PREFIX}{mappingId}";

    /// <summary>Builds implicit mapping lookup by item template code.</summary>
    internal static string BuildImplicitTemplateKey(Guid gameServiceId, string itemTemplateCode)
        => $"{IMPL_TPL_KEY_PREFIX}{gameServiceId}:{itemTemplateCode}";

    /// <summary>Builds primary affix instance lookup key.</summary>
    internal static string BuildInstanceKey(Guid itemInstanceId)
        => $"{INST_KEY_PREFIX}{itemInstanceId}";

    /// <summary>Builds game service index key for instance cleanup.</summary>
    internal static string BuildInstanceGameIndexKey(Guid gameServiceId)
        => $"{INST_GAME_INDEX_PREFIX}{gameServiceId}";

    /// <summary>Builds reverse index key: definition → item instance IDs.</summary>
    internal static string BuildInstancesByDefinitionKey(Guid definitionId)
        => $"{INST_DEF_INDEX_PREFIX}{definitionId}";

    /// <summary>Builds definition cache key.</summary>
    internal static string BuildDefinitionCacheKey(Guid definitionId)
        => $"{DEF_CACHE_PREFIX}{definitionId}";

    /// <summary>Builds mod group cache key for all definitions in a group.</summary>
    internal static string BuildModGroupCacheKey(Guid gameServiceId, string modGroup)
        => $"{DEF_GROUP_CACHE_PREFIX}{gameServiceId}:{modGroup}";

    /// <summary>Builds instance cache key.</summary>
    internal static string BuildInstanceCacheKey(Guid itemInstanceId)
        => $"{INST_CACHE_PREFIX}{itemInstanceId}";

    /// <summary>Builds computed stats cache key.</summary>
    internal static string BuildStatsCacheKey(Guid itemInstanceId)
        => $"{STATS_CACHE_PREFIX}{itemInstanceId}";

    /// <summary>Builds equipment stats cache key.</summary>
    internal static string BuildEquipmentCacheKey(Guid entityId, EntityType entityType)
        => $"{EQUIP_CACHE_PREFIX}{entityId}:{entityType}";

    /// <summary>Builds pool cache key.</summary>
    internal static string BuildPoolCacheKey(Guid gameServiceId, string itemClass, string slotType, int ilvlBucket)
        => $"{POOL_CACHE_PREFIX}{gameServiceId}:{itemClass}:{slotType}:{ilvlBucket}";

    /// <summary>Builds influence-specific pool cache key.</summary>
    internal static string BuildPoolInfluenceCacheKey(Guid gameServiceId, string itemClass, string slotType, int ilvlBucket, string influenceKey)
        => $"{POOL_INF_CACHE_PREFIX}{gameServiceId}:{itemClass}:{slotType}:{ilvlBucket}:{influenceKey}";

    /// <summary>Builds definition mutation lock key.</summary>
    internal static string BuildDefinitionLockKey(Guid definitionId)
        => $"{DEF_LOCK_PREFIX}{definitionId}";

    /// <summary>Builds item affix modification lock key.</summary>
    internal static string BuildItemLockKey(Guid itemInstanceId)
        => $"{ITEM_LOCK_PREFIX}{itemInstanceId}";

    /// <summary>Builds pool cache rebuild lock key.</summary>
    internal static string BuildPoolRebuildLockKey(Guid gameServiceId)
        => $"{POOL_REBUILD_LOCK_PREFIX}{gameServiceId}";

    #endregion
}

// ============================================================================
// INTERNAL DATA MODELS
// ============================================================================

/// <summary>
/// Internal storage model for affix definitions (MySQL: affix-definitions).
/// </summary>
internal class AffixDefinitionModel
{
    public Guid DefinitionId { get; set; }
    public Guid GameServiceId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string SlotType { get; set; } = string.Empty;
    public string ModGroup { get; set; } = string.Empty;
    public int Tier { get; set; }
    public string? Category { get; set; }
    public string[]? Tags { get; set; }
    public StatGrant[] StatGrants { get; set; } = Array.Empty<StatGrant>();
    public int SpawnWeight { get; set; }
    public SpawnTagModifier[]? SpawnTagModifiers { get; set; }
    public int RequiredItemLevel { get; set; }
    public string[]? RequiredInfluences { get; set; }
    public string[]? ValidItemClasses { get; set; }
    public string? DisplayName { get; set; }
    public int DisplayOrder { get; set; }
    public bool IsDeprecated { get; set; }
    public DateTimeOffset? DeprecatedAt { get; set; }
    public string? DeprecationReason { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}

/// <summary>
/// Internal storage model for implicit affix mappings (MySQL: affix-implicit-mappings).
/// </summary>
internal class ImplicitMappingModel
{
    public Guid MappingId { get; set; }
    public Guid GameServiceId { get; set; }
    public string ItemTemplateCode { get; set; } = string.Empty;
    public Guid[] ImplicitDefinitionIds { get; set; } = Array.Empty<Guid>();
}

/// <summary>
/// Internal storage model for per-item affix state (MySQL: affix-instances).
/// </summary>
internal class AffixInstanceModel
{
    public Guid ItemInstanceId { get; set; }
    public Guid GameServiceId { get; set; }
    public string EffectiveRarity { get; set; } = "normal";
    public int ItemLevel { get; set; }
    public int Quality { get; set; }
    public Guid? SeedId { get; set; }
    public List<AffixSlotModel> ImplicitSlots { get; set; } = new();
    public List<AffixSlotModel> PrefixSlots { get; set; } = new();
    public List<AffixSlotModel> SuffixSlots { get; set; } = new();
    public List<AffixSlotModel> EnchantSlots { get; set; } = new();
    public List<string> Influences { get; set; } = new();
    public AffixStatesModel States { get; set; } = new();
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }

    /// <summary>Returns all affix slots across all slot types.</summary>
    internal IEnumerable<AffixSlotModel> AllSlots()
        => ImplicitSlots.Concat(PrefixSlots).Concat(SuffixSlots).Concat(EnchantSlots);
}

/// <summary>
/// Individual affix slot within an instance.
/// </summary>
internal class AffixSlotModel
{
    public Guid DefinitionId { get; set; }
    public string DefinitionCode { get; set; } = string.Empty;
    public string ModGroup { get; set; } = string.Empty;
    public double[] RolledValues { get; set; } = Array.Empty<double>();
    public bool IsFractured { get; set; }
}

/// <summary>
/// Boolean state flags for an affix instance.
/// </summary>
internal class AffixStatesModel
{
    public bool IsCorrupted { get; set; }
    public bool IsMirrored { get; set; }
    public bool IsSplit { get; set; }
    public bool IsIdentified { get; set; } = true;
    public bool IsSynthesized { get; set; }
}

/// <summary>
/// Pre-computed affix pool cached in Redis (affix-pool-cache).
/// </summary>
internal class CachedAffixPool
{
    public List<CachedPoolEntry> Entries { get; set; } = new();
    public int TotalWeight { get; set; }
}

/// <summary>
/// A single entry in a cached affix pool.
/// </summary>
internal class CachedPoolEntry
{
    public Guid DefinitionId { get; set; }
    public string DefinitionCode { get; set; } = string.Empty;
    public string ModGroup { get; set; } = string.Empty;
    public int Tier { get; set; }
    public int BaseWeight { get; set; }
    public StatGrant[] StatGrants { get; set; } = Array.Empty<StatGrant>();
    public int RequiredItemLevel { get; set; }
    public string[]? RequiredInfluences { get; set; }
    public SpawnTagModifier[]? SpawnTagModifiers { get; set; }
}

/// <summary>
/// Cached computed stats for an item (Redis: affix-instance-cache, stats: prefix).
/// </summary>
internal class ComputedStatsModel
{
    public List<StatValue> Stats { get; set; } = new();
    public double QualityModifier { get; set; }
}

/// <summary>
/// Cached aggregate equipment stats for an entity (Redis: affix-instance-cache, equip: prefix).
/// </summary>
internal class EquipmentStatsModel
{
    public List<StatValue> PerStatTotals { get; set; } = new();
    public List<ItemBreakdown> PerItemBreakdown { get; set; } = new();
}
