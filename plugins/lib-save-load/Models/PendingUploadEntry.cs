namespace BeyondImmersion.BannouService.SaveLoad.Models;

/// <summary>
/// Internal model for pending upload queue entries in Redis.
/// Holds save data awaiting async upload to Asset service.
/// IMPLEMENTATION TENETS compliant: Uses proper C# types for enums and GUIDs.
/// </summary>
public sealed class PendingUploadEntry
{
    /// <summary>
    /// Unique ID for this upload entry
    /// </summary>
    public required Guid UploadId { get; set; }

    /// <summary>
    /// Slot ID this upload belongs to
    /// </summary>
    public required Guid SlotId { get; set; }

    /// <summary>
    /// Version number being uploaded
    /// </summary>
    public required int VersionNumber { get; set; }

    /// <summary>
    /// Game ID for the save
    /// </summary>
    public required string GameId { get; set; }

    /// <summary>
    /// Owner ID for the save
    /// </summary>
    public required Guid OwnerId { get; set; }

    /// <summary>
    /// Owner type for the save
    /// </summary>
    public required EntityType OwnerType { get; set; }

    /// <summary>
    /// The actual save data (base64 encoded, compressed)
    /// </summary>
    public required string Data { get; set; }

    /// <summary>
    /// SHA-256 hash for integrity verification
    /// </summary>
    public required string ContentHash { get; set; }

    /// <summary>
    /// Compression type applied
    /// </summary>
    public CompressionType CompressionType { get; set; } = CompressionType.GZIP;

    /// <summary>
    /// Size of uncompressed data in bytes
    /// </summary>
    public long SizeBytes { get; set; }

    /// <summary>
    /// Size of compressed data in bytes
    /// </summary>
    public long CompressedSizeBytes { get; set; }

    /// <summary>
    /// Whether this is a delta save
    /// </summary>
    public bool IsDelta { get; set; }

    /// <summary>
    /// Base version for delta saves
    /// </summary>
    public int? BaseVersionNumber { get; set; }

    /// <summary>
    /// Delta algorithm used
    /// </summary>
    public DeltaAlgorithm? DeltaAlgorithm { get; set; }

    /// <summary>
    /// Thumbnail data (base64 encoded) if provided
    /// </summary>
    public string? ThumbnailData { get; set; }

    /// <summary>
    /// Number of upload attempts
    /// </summary>
    public int AttemptCount { get; set; }

    /// <summary>
    /// Last error message if upload failed
    /// </summary>
    public string? LastError { get; set; }

    /// <summary>
    /// When this entry was queued
    /// </summary>
    public DateTimeOffset QueuedAt { get; set; }

    /// <summary>
    /// When the last upload attempt was made
    /// </summary>
    public DateTimeOffset? LastAttemptAt { get; set; }

    /// <summary>
    /// Priority for upload ordering (lower = higher priority)
    /// </summary>
    public int Priority { get; set; }

    /// <summary>
    /// Generates the state store key for this pending upload.
    /// Note: Uses ToString() for UploadId as state store keys are strings.
    /// </summary>
    public string GetStateKey() => $"pending:{UploadId}";

    /// <summary>
    /// Generates the state store key from upload ID
    /// </summary>
    public static string GetStateKey(string uploadId) => $"pending:{uploadId}";
}
