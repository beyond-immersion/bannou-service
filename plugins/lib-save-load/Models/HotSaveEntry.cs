namespace BeyondImmersion.BannouService.SaveLoad.Models;

/// <summary>
/// Internal model for hot cache entries in Redis.
/// Stores recent save data for fast access without hitting Asset service.
/// IMPLEMENTATION TENETS compliant: Uses proper C# types for enums and GUIDs.
/// </summary>
public sealed class HotSaveEntry
{
    /// <summary>
    /// Slot ID this entry belongs to
    /// </summary>
    public required Guid SlotId { get; set; }

    /// <summary>
    /// Version number
    /// </summary>
    public required int VersionNumber { get; set; }

    /// <summary>
    /// The actual save data (base64 encoded, may be compressed)
    /// </summary>
    public required string Data { get; set; }

    /// <summary>
    /// SHA-256 hash for integrity verification
    /// </summary>
    public required string ContentHash { get; set; }

    /// <summary>
    /// Whether data is compressed
    /// </summary>
    public bool IsCompressed { get; set; }

    /// <summary>
    /// Compression type if compressed
    /// </summary>
    public CompressionType? CompressionType { get; set; }

    /// <summary>
    /// Size of the data in bytes
    /// </summary>
    public long SizeBytes { get; set; }

    /// <summary>
    /// When this entry was cached
    /// </summary>
    public DateTimeOffset CachedAt { get; set; }

    /// <summary>
    /// Whether this is a delta (patch) rather than full data
    /// </summary>
    public bool IsDelta { get; set; }

    /// <summary>
    /// Generates the state store key for this hot cache entry.
    /// Note: Uses ToString() for SlotId as state store keys are strings.
    /// </summary>
    public string GetStateKey() => $"hot:{SlotId}:{VersionNumber}";

    /// <summary>
    /// Generates the state store key from components
    /// </summary>
    public static string GetStateKey(string slotId, int versionNumber)
        => $"hot:{slotId}:{versionNumber}";

    /// <summary>
    /// Generates the key for the latest version of a slot
    /// </summary>
    public static string GetLatestKey(string slotId) => $"hot:{slotId}:latest";
}
