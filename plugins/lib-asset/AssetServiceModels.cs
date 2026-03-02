using BeyondImmersion.Bannou.Asset.ClientEvents;

namespace BeyondImmersion.BannouService.Asset;

/// <summary>
/// Internal data models for AssetService.
/// </summary>
/// <remarks>
/// <para>
/// This file contains internal data models, DTOs, and helper structures used
/// exclusively by this service. These are NOT exposed via the API and are NOT
/// generated from schemas.
/// </para>
/// <para>
/// <b>When to add models here:</b>
/// <list type="bullet">
///   <item>Storage models for state stores (different from API request/response types)</item>
///   <item>Cache entry structures</item>
///   <item>Internal DTOs for service-to-service communication not exposed in API</item>
///   <item>Helper records for intermediate processing</item>
/// </list>
/// </para>
/// <para>
/// <b>IMPLEMENTATION TENETS - Type Safety:</b> Internal models MUST use proper C# types
/// (enums, Guids, DateTimeOffset) - never string representations. "JSON requires strings"
/// is FALSE - BannouJson handles serialization correctly.
/// </para>
/// </remarks>
public partial class AssetService
{
    // This partial class declaration exists to signal that the models below
    // are owned by and used exclusively by this service. The models themselves
    // are defined at namespace level as internal classes.
}

/// <summary>
/// Internal model for bundle download tokens stored in state.
/// </summary>
internal sealed class BundleDownloadToken
{
    public required string BundleId { get; set; }
    public BundleFormat Format { get; set; }
    public required string Path { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}

/// <summary>
/// Index entry for asset-to-bundle reverse lookup.
/// </summary>
internal sealed class AssetBundleIndex
{
    /// <summary>
    /// List of bundle IDs containing this asset.
    /// </summary>
    public List<string> BundleIds { get; set; } = new();
}

/// <summary>
/// Internal model for tracking async metabundle creation jobs.
/// Stored in state store for status polling and completion handling.
/// </summary>
internal sealed class MetabundleJob
{
    /// <summary>
    /// Unique job identifier.
    /// </summary>
    public Guid JobId { get; set; }

    /// <summary>
    /// Target metabundle identifier (human-readable, e.g., "game-assets-v1").
    /// </summary>
    public required string MetabundleId { get; set; }

    /// <summary>
    /// Current job status.
    /// </summary>
    public BundleStatus Status { get; set; } = BundleStatus.Queued;

    /// <summary>
    /// Progress percentage (0-100) when processing.
    /// </summary>
    public int? Progress { get; set; }

    /// <summary>
    /// Session ID of the requester for completion notification.
    /// </summary>
    public Guid? RequesterSessionId { get; set; }

    /// <summary>
    /// Serialized request for background processing.
    /// Always set when job is created; null indicates data corruption.
    /// </summary>
    public CreateMetabundleRequest? Request { get; set; }

    /// <summary>
    /// When the job was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// When the job was last updated.
    /// </summary>
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>
    /// When the job completed (success or failure).
    /// </summary>
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>
    /// Processing time in milliseconds after completion.
    /// </summary>
    public long? ProcessingTimeMs { get; set; }

    /// <summary>
    /// Error code if job failed. Uses MetabundleErrorCode enum per IMPLEMENTATION TENETS.
    /// </summary>
    public MetabundleErrorCode? ErrorCode { get; set; }

    /// <summary>
    /// Error message if job failed.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Result data when job completes successfully.
    /// </summary>
    public MetabundleJobResult? Result { get; set; }
}

/// <summary>
/// Job status enum for internal tracking (distinct from client event enum).
/// </summary>

/// <summary>
/// Result data stored when metabundle job completes successfully.
/// </summary>
internal sealed class MetabundleJobResult
{
    public int AssetCount { get; set; }
    public int? StandaloneAssetCount { get; set; }
    public long SizeBytes { get; set; }
    public string? StorageKey { get; set; }
    public List<SourceBundleReferenceInternal>? SourceBundles { get; set; }
}

/// <summary>
/// Internal model for source bundle reference in job results.
/// </summary>
internal sealed class SourceBundleReferenceInternal
{
    public required string BundleId { get; set; }
    public required string Version { get; set; }
    public required List<string> AssetIds { get; set; }
    public required string ContentHash { get; set; }
}
