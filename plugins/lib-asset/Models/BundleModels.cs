using BeyondImmersion.BannouService.Asset;

namespace BeyondImmersion.BannouService.Asset.Models;

/// <summary>
/// Metadata for a stored bundle (source or metabundle).
/// </summary>
public sealed class BundleMetadata
{
    /// <summary>
    /// Unique bundle identifier.
    /// </summary>
    public required Guid BundleId { get; init; }

    /// <summary>
    /// Bundle content version string.
    /// </summary>
    public required string Version { get; init; }

    /// <summary>
    /// Metadata version number (increments on metadata changes).
    /// </summary>
    public int MetadataVersion { get; set; } = 1;

    /// <summary>
    /// Bundle type: source (uploaded/server-created) or metabundle (composed from source bundles).
    /// </summary>
    public required BundleType BundleType { get; init; }

    /// <summary>
    /// Game realm stub name this bundle belongs to.
    /// </summary>
    public required string Realm { get; init; }

    /// <summary>
    /// Human-readable bundle name.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Bundle description.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Key-value tags for categorization and filtering.
    /// </summary>
    public Dictionary<string, string>? Tags { get; set; }

    /// <summary>
    /// List of asset IDs included in the bundle (platform asset IDs).
    /// </summary>
    public required List<string> AssetIds { get; init; }

    /// <summary>
    /// Full asset manifest with content hashes and metadata.
    /// </summary>
    public List<StoredBundleAssetEntry>? Assets { get; init; }

    /// <summary>
    /// Storage key for the bundle file.
    /// </summary>
    public required string StorageKey { get; init; }

    /// <summary>
    /// Storage bucket for the bundle file.
    /// </summary>
    public string? Bucket { get; init; }

    /// <summary>
    /// Bundle size in bytes.
    /// </summary>
    public required long SizeBytes { get; init; }

    /// <summary>
    /// When the bundle was created.
    /// </summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// When the bundle metadata was last updated.
    /// </summary>
    public DateTimeOffset? UpdatedAt { get; set; }

    /// <summary>
    /// When the bundle was soft-deleted (null if active).
    /// </summary>
    public DateTimeOffset? DeletedAt { get; set; }

    /// <summary>
    /// Bundle lifecycle status (active, deleted, processing).
    /// </summary>
    public BundleLifecycleStatus LifecycleStatus { get; set; } = BundleLifecycleStatus.Active;

    /// <summary>
    /// Current bundle processing status.
    /// </summary>
    public required BundleStatus Status { get; init; }

    /// <summary>
    /// Owner of this bundle. NOT a session ID.
    /// For user-initiated: the accountId (UUID format).
    /// For service-initiated: the service name.
    /// </summary>
    public string? Owner { get; init; }

    /// <summary>
    /// Provenance data for metabundles - tracks which source bundles were composed.
    /// Null for source bundles.
    /// </summary>
    public List<StoredSourceBundleReference>? SourceBundles { get; init; }

    /// <summary>
    /// List of standalone asset IDs that were included directly (not from bundles).
    /// Only present for metabundles created with standalone assets.
    /// </summary>
    public List<string>? StandaloneAssetIds { get; init; }

    /// <summary>
    /// Optional custom metadata (legacy field, use Tags for new code).
    /// </summary>
    public Dictionary<string, object>? Metadata { get; init; }

    /// <summary>
    /// Converts this internal metadata to a BundleSummary for API responses.
    /// </summary>
    public BundleSummary ToBundleSummary()
    {
        return new BundleSummary
        {
            BundleId = BundleId.ToString(),
            BundleType = BundleType,
            Version = Version,
            AssetCount = AssetIds.Count,
            SizeBytes = SizeBytes,
            Realm = Realm,
            CreatedAt = CreatedAt
        };
    }

    /// <summary>
    /// Converts this internal metadata to the full API BundleInfo response.
    /// </summary>
    public Asset.BundleInfo ToApiMetadata()
    {
        return new Asset.BundleInfo
        {
            BundleId = BundleId.ToString(),
            BundleType = BundleType,
            Version = Version,
            MetadataVersion = MetadataVersion,
            Name = Name,
            Description = Description,
            Owner = Owner,
            Realm = Realm,
            Tags = Tags,
            Status = LifecycleStatus switch
            {
                BundleLifecycleStatus.Active => Asset.BundleLifecycle.Active,
                BundleLifecycleStatus.Deleted => Asset.BundleLifecycle.Deleted,
                BundleLifecycleStatus.Processing => Asset.BundleLifecycle.Processing,
                _ => Asset.BundleLifecycle.Active
            },
            AssetCount = AssetIds.Count,
            SizeBytes = SizeBytes,
            CreatedAt = CreatedAt,
            UpdatedAt = UpdatedAt,
            DeletedAt = DeletedAt
        };
    }
}

/// <summary>
/// Bundle lifecycle status (different from processing status).
/// </summary>
public enum BundleLifecycleStatus
{
    /// <summary>
    /// Bundle is active and available.
    /// </summary>
    Active,

    /// <summary>
    /// Bundle has been soft-deleted.
    /// </summary>
    Deleted,

    /// <summary>
    /// Bundle is being processed (metabundle creation).
    /// </summary>
    Processing
}

/// <summary>
/// Asset entry stored within bundle metadata (internal representation).
/// </summary>
public sealed class StoredBundleAssetEntry
{
    /// <summary>
    /// Platform-specific asset identifier.
    /// </summary>
    public required string AssetId { get; init; }

    /// <summary>
    /// SHA256 content hash for deduplication.
    /// </summary>
    public required string ContentHash { get; init; }

    /// <summary>
    /// Original filename.
    /// </summary>
    public string? Filename { get; init; }

    /// <summary>
    /// MIME content type.
    /// </summary>
    public string? ContentType { get; init; }

    /// <summary>
    /// Asset size in bytes (uncompressed).
    /// </summary>
    public required long Size { get; init; }

    /// <summary>
    /// External asset identifier for cross-system references.
    /// </summary>
    public string? ExternalAssetId { get; init; }

    /// <summary>
    /// Tags associated with this asset within the bundle.
    /// </summary>
    public List<string>? Tags { get; init; }
}

/// <summary>
/// Source bundle reference stored in metabundle metadata (internal representation).
/// </summary>
public sealed class StoredSourceBundleReference
{
    /// <summary>
    /// Source bundle identifier.
    /// </summary>
    public required Guid BundleId { get; init; }

    /// <summary>
    /// Version of source bundle at composition time.
    /// </summary>
    public required string Version { get; init; }

    /// <summary>
    /// Asset IDs contributed from this source bundle.
    /// </summary>
    public required List<string> AssetIds { get; init; }

    /// <summary>
    /// Content hash of source bundle for integrity verification.
    /// </summary>
    public required string ContentHash { get; init; }

    /// <summary>
    /// Converts to API response model.
    /// </summary>
    public SourceBundleReference ToApiModel()
    {
        return new SourceBundleReference
        {
            BundleId = BundleId.ToString(),
            Version = Version,
            AssetIds = AssetIds,
            ContentHash = ContentHash
        };
    }
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
    public required Guid JobId { get; init; }

    /// <summary>
    /// Target bundle identifier.
    /// </summary>
    public required Guid BundleId { get; init; }

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
    public required Guid UploadId { get; init; }

    /// <summary>
    /// Expected bundle ID.
    /// </summary>
    public required Guid BundleId { get; init; }

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

/// <summary>
/// Internal model for bundle version history records.
/// </summary>
public sealed class StoredBundleVersionRecord
{
    /// <summary>
    /// Bundle identifier.
    /// </summary>
    public required Guid BundleId { get; init; }

    /// <summary>
    /// Metadata version number.
    /// </summary>
    public required int Version { get; init; }

    /// <summary>
    /// When this version was created.
    /// </summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// Account ID or service name that made the change.
    /// </summary>
    public required string CreatedBy { get; init; }

    /// <summary>
    /// List of changes made in this version.
    /// </summary>
    public required List<string> Changes { get; init; }

    /// <summary>
    /// Optional reason for the change.
    /// </summary>
    public string? Reason { get; init; }

    /// <summary>
    /// Full metadata snapshot at this version (for restore operations).
    /// </summary>
    public BundleMetadata? Snapshot { get; init; }

    /// <summary>
    /// Converts to API response model.
    /// </summary>
    public Asset.BundleVersionRecord ToApiModel(bool includeSnapshot = false)
    {
        return new Asset.BundleVersionRecord
        {
            Version = Version,
            CreatedAt = CreatedAt,
            CreatedBy = CreatedBy,
            Changes = Changes,
            Reason = Reason,
            Snapshot = includeSnapshot ? Snapshot?.ToApiMetadata() : null
        };
    }
}
