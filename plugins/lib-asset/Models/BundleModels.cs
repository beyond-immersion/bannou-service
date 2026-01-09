using BeyondImmersion.BannouService.Asset;

namespace BeyondImmersion.BannouService.Asset.Models;

/// <summary>
/// Metadata for a stored bundle.
/// </summary>
public sealed class BundleMetadata
{
    /// <summary>
    /// Unique bundle identifier.
    /// </summary>
    public required string BundleId { get; init; }

    /// <summary>
    /// Bundle version string.
    /// </summary>
    public required string Version { get; init; }

    /// <summary>
    /// List of asset IDs included in the bundle.
    /// </summary>
    public required List<string> AssetIds { get; init; }

    /// <summary>
    /// Storage key for the bundle file.
    /// </summary>
    public required string StorageKey { get; init; }

    /// <summary>
    /// Bundle size in bytes.
    /// </summary>
    public required long SizeBytes { get; init; }

    /// <summary>
    /// When the bundle was created.
    /// </summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// Current bundle status.
    /// </summary>
    public required BundleStatus Status { get; init; }

    /// <summary>
    /// Optional custom metadata.
    /// </summary>
    public Dictionary<string, object>? Metadata { get; init; }
}

/// <summary>
/// Bundle status enumeration.
/// </summary>
public enum BundleStatus
{
    /// <summary>
    /// Bundle creation is pending/queued.
    /// </summary>
    Pending,

    /// <summary>
    /// Bundle is being processed.
    /// </summary>
    Processing,

    /// <summary>
    /// Bundle is ready for download.
    /// </summary>
    Ready,

    /// <summary>
    /// Bundle creation failed.
    /// </summary>
    Failed
}

/// <summary>
/// Bundle creation job for processing pool.
/// </summary>
public sealed class BundleCreationJob
{
    /// <summary>
    /// Unique job identifier.
    /// </summary>
    public required string JobId { get; init; }

    /// <summary>
    /// Target bundle identifier.
    /// </summary>
    public required string BundleId { get; init; }

    /// <summary>
    /// Bundle version.
    /// </summary>
    public required string Version { get; init; }

    /// <summary>
    /// Asset IDs to include.
    /// </summary>
    public required List<string> AssetIds { get; init; }

    /// <summary>
    /// Compression type to use.
    /// </summary>
    public required CompressionType Compression { get; init; }

    /// <summary>
    /// Custom metadata.
    /// </summary>
    public Dictionary<string, object>? Metadata { get; init; }

    /// <summary>
    /// Job status.
    /// </summary>
    public required BundleCreationStatus Status { get; set; }

    /// <summary>
    /// When the job was created.
    /// </summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// When processing started.
    /// </summary>
    public DateTimeOffset? StartedAt { get; set; }

    /// <summary>
    /// When processing completed.
    /// </summary>
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>
    /// Error message if failed.
    /// </summary>
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Bundle creation job status.
/// </summary>
public enum BundleCreationStatus
{
    /// <summary>
    /// Job is queued.
    /// </summary>
    Queued,

    /// <summary>
    /// Job is being processed.
    /// </summary>
    Processing,

    /// <summary>
    /// Job completed successfully.
    /// </summary>
    Completed,

    /// <summary>
    /// Job failed.
    /// </summary>
    Failed
}

/// <summary>
/// Bundle upload session for validation.
/// </summary>
public sealed class BundleUploadSession
{
    /// <summary>
    /// Upload session identifier.
    /// </summary>
    public required string UploadId { get; init; }

    /// <summary>
    /// Expected bundle ID.
    /// </summary>
    public required string BundleId { get; init; }

    /// <summary>
    /// Filename being uploaded.
    /// </summary>
    public required string Filename { get; init; }

    /// <summary>
    /// Expected content type.
    /// </summary>
    public required string ContentType { get; init; }

    /// <summary>
    /// Expected file size.
    /// </summary>
    public required long SizeBytes { get; init; }

    /// <summary>
    /// Storage key for the upload.
    /// </summary>
    public required string StorageKey { get; init; }

    /// <summary>
    /// Manifest preview for validation.
    /// </summary>
    public BundleManifestPreview? ManifestPreview { get; init; }

    /// <summary>
    /// Owner of this bundle upload session. NOT a session ID.
    /// For user-initiated uploads: the accountId (UUID format).
    /// For service-initiated uploads: the service name (e.g., "orchestrator").
    /// </summary>
    public required string Owner { get; init; }

    /// <summary>
    /// When the session was created.
    /// </summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// When the session expires.
    /// </summary>
    public required DateTimeOffset ExpiresAt { get; init; }
}
