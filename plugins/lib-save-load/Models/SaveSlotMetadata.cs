namespace BeyondImmersion.BannouService.SaveLoad.Models;

/// <summary>
/// Internal model for save slot metadata stored in state store.
/// This is the authoritative record for a slot's configuration and state.
/// </summary>
public sealed class SaveSlotMetadata
{
    /// <summary>
    /// Unique slot identifier (generated UUID)
    /// </summary>
    public required string SlotId { get; set; }

    /// <summary>
    /// Game identifier for namespace isolation
    /// </summary>
    public required string GameId { get; set; }

    /// <summary>
    /// ID of the owning entity (account, character, session, realm)
    /// </summary>
    public required string OwnerId { get; set; }

    /// <summary>
    /// Type of the owning entity
    /// </summary>
    public required string OwnerType { get; set; }

    /// <summary>
    /// Slot name (unique per owner within game namespace)
    /// </summary>
    public required string SlotName { get; set; }

    /// <summary>
    /// Save category determining default behavior
    /// </summary>
    public required string Category { get; set; }

    /// <summary>
    /// Maximum versions to retain before rolling cleanup
    /// </summary>
    public int MaxVersions { get; set; }

    /// <summary>
    /// Days to retain versions (null = indefinite)
    /// </summary>
    public int? RetentionDays { get; set; }

    /// <summary>
    /// Compression type for new saves
    /// </summary>
    public string CompressionType { get; set; } = "GZIP";

    /// <summary>
    /// Current number of versions in slot
    /// </summary>
    public int VersionCount { get; set; }

    /// <summary>
    /// Latest version number (null if empty slot)
    /// </summary>
    public int? LatestVersion { get; set; }

    /// <summary>
    /// Total storage used by all versions in bytes
    /// </summary>
    public long TotalSizeBytes { get; set; }

    /// <summary>
    /// Searchable tags for slot categorization
    /// </summary>
    public List<string> Tags { get; set; } = new();

    /// <summary>
    /// Custom key-value metadata
    /// </summary>
    public Dictionary<string, string> Metadata { get; set; } = new();

    /// <summary>
    /// Slot creation timestamp
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Last modification timestamp
    /// </summary>
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>
    /// ETag for optimistic concurrency control
    /// </summary>
    public string? ETag { get; set; }

    /// <summary>
    /// Generates the state store key for this slot
    /// </summary>
    public string GetStateKey() => $"slot:{GameId}:{OwnerType}:{OwnerId}:{SlotName}";

    /// <summary>
    /// Generates the state store key from components
    /// </summary>
    public static string GetStateKey(string gameId, string ownerType, string ownerId, string slotName)
        => $"slot:{gameId}:{ownerType}:{ownerId}:{slotName}";
}
