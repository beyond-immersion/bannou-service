namespace BeyondImmersion.BannouService.Collection;

/// <summary>
/// Internal data models for CollectionService.
/// </summary>
/// <remarks>
/// <para>
/// This file contains internal data models, DTOs, and helper structures used
/// exclusively by this service. These are NOT exposed via the API and are NOT
/// generated from schemas.
/// </para>
/// <para>
/// <b>IMPLEMENTATION TENETS - Type Safety:</b> Internal models MUST use proper C# types
/// (enums, Guids, DateTimeOffset) - never string representations. "JSON requires strings"
/// is FALSE - BannouJson handles serialization correctly.
/// </para>
/// </remarks>
public partial class CollectionService
{
    // This partial class declaration exists to signal that the models below
    // are owned by and used exclusively by this service. The models themselves
    // are defined at namespace level as internal classes.
}

// ============================================================================
// INTERNAL DATA MODELS
// ============================================================================

/// <summary>
/// Internal storage model for entry templates.
/// Defines a collectible entry type that can be unlocked in collections.
/// </summary>
internal class EntryTemplateModel
{
    /// <summary>Unique identifier for the entry template.</summary>
    public Guid EntryTemplateId { get; set; }

    /// <summary>Unique code within this collection type and game service.</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>Type of collection this entry belongs to (opaque string code).</summary>
    public string CollectionType { get; set; } = string.Empty;

    /// <summary>Game service this entry template is scoped to.</summary>
    public Guid GameServiceId { get; set; }

    /// <summary>Human-readable display name for this entry.</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Category within the collection type (e.g., boss, ambient, battle).</summary>
    public string? Category { get; set; }

    /// <summary>Searchable tags for filtering entries.</summary>
    public List<string>? Tags { get; set; }

    /// <summary>Primary asset identifier for this entry (audio, video, image).</summary>
    public string? AssetId { get; set; }

    /// <summary>Thumbnail or preview asset identifier.</summary>
    public string? ThumbnailAssetId { get; set; }

    /// <summary>Hint text shown to users about how to unlock this entry.</summary>
    public string? UnlockHint { get; set; }

    /// <summary>Whether this entry should be hidden from users until unlocked.</summary>
    public bool HideWhenLocked { get; set; }

    /// <summary>Item template used when granting this entry to a collection.</summary>
    public Guid ItemTemplateId { get; set; }

    /// <summary>Progressive discovery levels for bestiary-style entries.</summary>
    public List<DiscoveryLevelEntry>? DiscoveryLevels { get; set; }

    /// <summary>Theme tags for music entries (e.g., battle, peaceful, forest).</summary>
    public List<string>? Themes { get; set; }

    /// <summary>Duration of the content (ISO 8601 duration or human-readable).</summary>
    public string? Duration { get; set; }

    /// <summary>Loop point for music entries (ISO 8601 duration or timestamp).</summary>
    public string? LoopPoint { get; set; }

    /// <summary>Composer or creator name for music entries.</summary>
    public string? Composer { get; set; }

    /// <summary>When this entry template was created.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>When this entry template was last updated.</summary>
    public DateTimeOffset? UpdatedAt { get; set; }
}

/// <summary>
/// Internal storage model for collection instances.
/// Links an owner (ownerType + ownerId) to a collection type with an inventory container.
/// </summary>
internal class CollectionInstanceModel
{
    /// <summary>Unique identifier for the collection instance.</summary>
    public Guid CollectionId { get; set; }

    /// <summary>Entity that owns this collection.</summary>
    public Guid OwnerId { get; set; }

    /// <summary>Entity type discriminator (e.g., account, character).</summary>
    public string OwnerType { get; set; } = string.Empty;

    /// <summary>Type of collection content (opaque string code).</summary>
    public string CollectionType { get; set; } = string.Empty;

    /// <summary>Game service this collection is scoped to.</summary>
    public Guid GameServiceId { get; set; }

    /// <summary>Inventory container holding unlocked entry items.</summary>
    public Guid ContainerId { get; set; }

    /// <summary>When this collection instance was created.</summary>
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// Cache model for collection state stored in Redis.
/// Tracks which entries have been unlocked in a specific collection instance.
/// </summary>
internal class CollectionCacheModel
{
    /// <summary>Collection instance this cache represents.</summary>
    public Guid CollectionId { get; set; }

    /// <summary>List of unlocked entries with their item references.</summary>
    public List<UnlockedEntryRecord> UnlockedEntries { get; set; } = new();

    /// <summary>When this cache was last updated.</summary>
    public DateTimeOffset LastUpdated { get; set; }
}

/// <summary>
/// Internal storage model for area-to-theme content mappings.
/// Used by the content selection algorithm to match entries to areas.
/// </summary>
internal class AreaContentConfigModel
{
    /// <summary>Unique identifier for the area config.</summary>
    public Guid AreaConfigId { get; set; }

    /// <summary>Area code (unique per game service and collection type).</summary>
    public string AreaCode { get; set; } = string.Empty;

    /// <summary>Game service this area config belongs to.</summary>
    public Guid GameServiceId { get; set; }

    /// <summary>Collection type this area config applies to.</summary>
    public string CollectionType { get; set; } = string.Empty;

    /// <summary>Theme tags for this area (matched against collection entry themes).</summary>
    public List<string> Themes { get; set; } = new();

    /// <summary>Default entry code to use when no matches are found.</summary>
    public string DefaultEntryCode { get; set; } = string.Empty;

    /// <summary>When this area config was created.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>When this area config was last updated.</summary>
    public DateTimeOffset? UpdatedAt { get; set; }
}

/// <summary>
/// Entry in the collection cache representing a single unlocked entry.
/// </summary>
internal class UnlockedEntryRecord
{
    /// <summary>Entry template code.</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>Entry template identifier.</summary>
    public Guid EntryTemplateId { get; set; }

    /// <summary>Item instance created when this entry was unlocked.</summary>
    public Guid ItemInstanceId { get; set; }

    /// <summary>When this entry was unlocked.</summary>
    public DateTimeOffset UnlockedAt { get; set; }

    /// <summary>Entry instance metadata (play count, favorites, discovery, etc.).</summary>
    public EntryMetadataModel? Metadata { get; set; }
}

/// <summary>
/// Internal metadata model for unlocked entry tracking.
/// </summary>
internal class EntryMetadataModel
{
    /// <summary>Context where the entry was unlocked (e.g., location code).</summary>
    public string? UnlockedIn { get; set; }

    /// <summary>Event or activity during which the entry was unlocked.</summary>
    public string? UnlockedDuring { get; set; }

    /// <summary>Number of times this entry has been played or viewed.</summary>
    public int PlayCount { get; set; }

    /// <summary>When this entry was last accessed or played.</summary>
    public DateTimeOffset? LastAccessedAt { get; set; }

    /// <summary>Whether this entry has been favorited by the owner.</summary>
    public bool Favorited { get; set; }

    /// <summary>Current discovery level for progressive reveal entries.</summary>
    public int DiscoveryLevel { get; set; }

    /// <summary>Kill count for bestiary entries.</summary>
    public int KillCount { get; set; }

    /// <summary>Arbitrary custom data for game-specific metadata.</summary>
    public Dictionary<string, object>? CustomData { get; set; }
}

/// <summary>
/// Internal helper for progressive discovery level definitions.
/// </summary>
internal class DiscoveryLevelEntry
{
    /// <summary>Discovery level number (zero-indexed).</summary>
    public int Level { get; set; }

    /// <summary>List of field or information keys revealed at this level.</summary>
    public List<string> Reveals { get; set; } = new();
}
