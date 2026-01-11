namespace BeyondImmersion.BannouService.SaveLoad.Models;

/// <summary>
/// Internal model for save version manifest stored in state store.
/// Tracks metadata and asset references for each version.
/// </summary>
public sealed class SaveVersionManifest
{
    /// <summary>
    /// Parent slot ID
    /// </summary>
    public required string SlotId { get; set; }

    /// <summary>
    /// Version number (sequential within slot)
    /// </summary>
    public required int VersionNumber { get; set; }

    /// <summary>
    /// Asset ID for the save data blob (from Asset service)
    /// </summary>
    public string? AssetId { get; set; }

    /// <summary>
    /// SHA-256 hash of the save data (uncompressed)
    /// </summary>
    public required string ContentHash { get; set; }

    /// <summary>
    /// Size of uncompressed data in bytes
    /// </summary>
    public long SizeBytes { get; set; }

    /// <summary>
    /// Size of compressed data in bytes (if compression applied)
    /// </summary>
    public long? CompressedSizeBytes { get; set; }

    /// <summary>
    /// Compression type applied to this version
    /// </summary>
    public string CompressionType { get; set; } = "NONE";

    /// <summary>
    /// Whether this version is pinned (excluded from rolling cleanup)
    /// </summary>
    public bool IsPinned { get; set; }

    /// <summary>
    /// Checkpoint name if pinned
    /// </summary>
    public string? CheckpointName { get; set; }

    /// <summary>
    /// Whether this is a delta version (stores patch, not full data)
    /// </summary>
    public bool IsDelta { get; set; }

    /// <summary>
    /// Base version number for delta reconstruction
    /// </summary>
    public int? BaseVersionNumber { get; set; }

    /// <summary>
    /// Delta algorithm used (JSON_PATCH, BSDIFF, XDELTA)
    /// </summary>
    public string? DeltaAlgorithm { get; set; }

    /// <summary>
    /// Thumbnail asset ID (if thumbnail was provided)
    /// </summary>
    public string? ThumbnailAssetId { get; set; }

    /// <summary>
    /// Device ID that created this version (for conflict detection)
    /// </summary>
    public string? DeviceId { get; set; }

    /// <summary>
    /// Schema version of the save data
    /// </summary>
    public string? SchemaVersion { get; set; }

    /// <summary>
    /// Custom metadata for this version
    /// </summary>
    public Dictionary<string, object> Metadata { get; set; } = new();

    /// <summary>
    /// Upload status (PENDING, COMPLETE, FAILED)
    /// </summary>
    public string UploadStatus { get; set; } = "PENDING";

    /// <summary>
    /// Version creation timestamp
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// ETag for optimistic concurrency control
    /// </summary>
    public string? ETag { get; set; }

    /// <summary>
    /// Generates the state store key for this version
    /// </summary>
    public string GetStateKey() => $"version:{SlotId}:{VersionNumber}";

    /// <summary>
    /// Generates the state store key from components
    /// </summary>
    public static string GetStateKey(string slotId, int versionNumber)
        => $"version:{slotId}:{versionNumber}";
}
