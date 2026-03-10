namespace BeyondImmersion.BannouService.SaveLoad.Models;

/// <summary>
/// Internal model for save slot metadata stored in state store.
/// This is the authoritative record for a slot's configuration and state.
/// IMPLEMENTATION TENETS compliant: Uses proper C# types for enums and GUIDs.
/// </summary>
public sealed class SaveSlotMetadata
{
    /// <summary>
    /// Unique slot identifier (generated UUID)
    /// </summary>
    public required Guid SlotId { get; set; }

    /// <summary>
    /// Game identifier for namespace isolation
    /// </summary>
    public required string GameId { get; set; }

    /// <summary>
    /// ID of the owning entity (account, character, session, realm)
    /// </summary>
    public required Guid OwnerId { get; set; }

    /// <summary>
    /// Type of the owning entity
    /// </summary>
    public required EntityType OwnerType { get; set; }

    /// <summary>
    /// Slot name (unique per owner within game namespace)
    /// </summary>
    public required string SlotName { get; set; }

    /// <summary>
    /// Save category determining default behavior
    /// </summary>
    public required SaveCategory Category { get; set; }

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
    public CompressionType CompressionType { get; set; } = CompressionType.Gzip;

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
    /// Generates the state store key for this slot.
    /// Note: Uses ToString().ToLowerInvariant() for EntityType key composition (accepted breaking change from OwnerType migration).
    /// </summary>
    private const string KeyPrefix = "slot";

    /// <summary>Builds the state store key from this instance's properties.</summary>
    internal string BuildStateKey() => $"{KeyPrefix}:{GameId}:{OwnerType.ToString().ToLowerInvariant()}:{OwnerId}:{SlotName}";

    /// <summary>
    /// Builds the state store key from typed components.
    /// Handles EntityType and Guid conversion internally per FOUNDATION TENETS.
    /// </summary>
    internal static string BuildStateKey(string gameId, EntityType ownerType, Guid ownerId, string slotName)
        => $"{KeyPrefix}:{gameId}:{ownerType.ToString().ToLowerInvariant()}:{ownerId}:{slotName}";

    /// <summary>
    /// Builds the state store key from string components.
    /// Use the typed overload when EntityType and Guid are available.
    /// </summary>
    internal static string BuildStateKey(string gameId, string ownerType, string ownerId, string slotName)
        => $"{KeyPrefix}:{gameId}:{ownerType}:{ownerId}:{slotName}";
}
