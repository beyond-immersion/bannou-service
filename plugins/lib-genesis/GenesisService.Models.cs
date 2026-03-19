namespace BeyondImmersion.BannouService.Genesis;

/// <summary>
/// Internal data models and key builders for GenesisService.
/// Storage models, cache entries, and internal DTOs used exclusively by this service.
/// </summary>
public partial class GenesisService
{
    #region Key Building Helpers

    private const string TEMPLATE_KEY_PREFIX = "template:";
    private const string TEMPLATE_GAME_KEY_PREFIX = "template-game:";
    private const string ENTITY_KEY_PREFIX = "entity:";
    private const string ENTITY_CODE_KEY_PREFIX = "entity-code:";
    private const string ENTITY_TEMPLATE_KEY_PREFIX = "entity-template:";
    private const string ENTITY_WALLET_KEY_PREFIX = "entity-wallet:";
    private const string CACHE_ENTITY_KEY_PREFIX = "entity:";
    private const string CACHE_CAPS_KEY_PREFIX = "caps:";

    /// <summary>Builds key for primary template lookup.</summary>
    internal static string BuildTemplateKey(string templateCode)
        => $"{TEMPLATE_KEY_PREFIX}{templateCode}";

    /// <summary>Builds key for templates-by-game-service index.</summary>
    internal static string BuildTemplateGameKey(Guid gameServiceId)
        => $"{TEMPLATE_GAME_KEY_PREFIX}{gameServiceId}";

    /// <summary>Builds key for primary entity lookup.</summary>
    internal static string BuildEntityKey(Guid entityId)
        => $"{ENTITY_KEY_PREFIX}{entityId}";

    /// <summary>Builds key for entity code uniqueness index.</summary>
    internal static string BuildEntityCodeKey(Guid gameServiceId, Guid realmId, string code)
        => $"{ENTITY_CODE_KEY_PREFIX}{gameServiceId}:{realmId}:{code}";

    /// <summary>Builds key for entities-by-template-realm index.</summary>
    internal static string BuildEntityTemplateKey(string templateCode, Guid realmId)
        => $"{ENTITY_TEMPLATE_KEY_PREFIX}{templateCode}:{realmId}";

    /// <summary>Builds key for wallet-to-entity reverse index.</summary>
    internal static string BuildEntityWalletKey(Guid walletId)
        => $"{ENTITY_WALLET_KEY_PREFIX}{walletId}";

    /// <summary>Builds cache key for entity hot cache.</summary>
    internal static string BuildEntityCacheKey(Guid entityId)
        => $"{CACHE_ENTITY_KEY_PREFIX}{entityId}";

    /// <summary>Builds cache key for capability manifest cache.</summary>
    internal static string BuildCapsCacheKey(Guid entityId)
        => $"{CACHE_CAPS_KEY_PREFIX}{entityId}";

    #endregion
}

// ============================================================================
// INTERNAL STORAGE MODELS
// ============================================================================

/// <summary>
/// Internal storage model for genesis templates.
/// </summary>
internal class GenesisTemplateModel
{
    public string TemplateCode { get; set; } = string.Empty;
    public Guid GameServiceId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public GenesisSeedConfig Seed { get; set; } = new();
    public GenesisEconomyConfig Economy { get; set; } = new();
    public GenesisStorageConfig Storage { get; set; } = new();
    public GenesisAwakeningConfig Awakening { get; set; } = new();
    public PhysicalFormType PhysicalFormType { get; set; }
    public GenesisBondConfig Bond { get; set; } = new();
    public bool ArchiveOnDestruction { get; set; } = true;
    public bool IsDeprecated { get; set; }
    public DateTimeOffset? DeprecatedAt { get; set; }
    public string? DeprecationReason { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// List model for template-game index (stores template codes for a game service).
/// </summary>
internal class GenesisTemplateListModel
{
    public List<string> TemplateCodes { get; set; } = new();
}

/// <summary>
/// Internal storage model for genesis entities.
/// </summary>
internal class GenesisEntityModel
{
    public Guid EntityId { get; set; }
    public string TemplateCode { get; set; } = string.Empty;
    public Guid GameServiceId { get; set; }
    public Guid RealmId { get; set; }
    public string? Code { get; set; }
    public string? DisplayName { get; set; }
    public Guid SeedId { get; set; }
    public Dictionary<string, Guid> WalletIds { get; set; } = new();
    public Dictionary<string, Guid> InventoryIds { get; set; } = new();
    public string CurrentPhase { get; set; } = string.Empty;
    public CognitiveStage CognitiveStage { get; set; }
    public Guid? ActorId { get; set; }
    public Guid? CharacterId { get; set; }
    public PhysicalFormType PhysicalFormType { get; set; }
    public Guid? PhysicalFormId { get; set; }
    public EntityType? BondTargetEntityType { get; set; }
    public Guid? BondTargetEntityId { get; set; }
    public Guid? BondId { get; set; }
    public GenesisEntityStatus Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// List model for entity-template index (stores entity IDs for a template/realm).
/// </summary>
internal class GenesisEntityListModel
{
    public List<Guid> EntityIds { get; set; } = new();
}

/// <summary>
/// Cached entity model for Redis hot cache (TTL-based).
/// </summary>
internal class CachedGenesisEntity
{
    public Guid EntityId { get; set; }
    public string TemplateCode { get; set; } = string.Empty;
    public Guid GameServiceId { get; set; }
    public Guid RealmId { get; set; }
    public string? Code { get; set; }
    public string? DisplayName { get; set; }
    public Guid SeedId { get; set; }
    public Dictionary<string, Guid> WalletIds { get; set; } = new();
    public Dictionary<string, Guid> InventoryIds { get; set; } = new();
    public string CurrentPhase { get; set; } = string.Empty;
    public CognitiveStage CognitiveStage { get; set; }
    public Guid? ActorId { get; set; }
    public Guid? CharacterId { get; set; }
    public PhysicalFormType PhysicalFormType { get; set; }
    public Guid? PhysicalFormId { get; set; }
    public EntityType? BondTargetEntityType { get; set; }
    public Guid? BondTargetEntityId { get; set; }
    public Guid? BondId { get; set; }
    public GenesisEntityStatus Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// Cached capability manifest for Redis hot cache (TTL-based).
/// </summary>
internal class CachedCapabilityManifest
{
    public Guid EntityId { get; set; }
    public List<GenesisCapability> Capabilities { get; set; } = new();
    public int Version { get; set; }
}
