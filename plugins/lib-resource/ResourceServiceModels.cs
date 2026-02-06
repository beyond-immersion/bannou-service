namespace BeyondImmersion.BannouService.Resource;

/// <summary>
/// Internal data models for ResourceService.
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
public partial class ResourceService
{
    // This partial class declaration exists to signal that the models below
    // are owned by and used exclusively by this service. The models themselves
    // are defined at namespace level as internal classes.
}

// =========================================================================
// Internal POCOs for State Storage
// =========================================================================

/// <summary>
/// Entry in the reference set for a resource.
/// </summary>
internal class ResourceReferenceEntry
{
    /// <summary>
    /// Type of entity holding the reference (opaque identifier).
    /// </summary>
    public string SourceType { get; set; } = string.Empty;

    /// <summary>
    /// ID of the entity holding the reference (opaque string, supports non-Guid IDs).
    /// </summary>
    public string SourceId { get; set; } = string.Empty;

    /// <summary>
    /// When this reference was registered.
    /// </summary>
    public DateTimeOffset RegisteredAt { get; set; }

    /// <summary>
    /// Override equality for set operations.
    /// Two references are equal if they have the same source type and ID.
    /// </summary>
    public override bool Equals(object? obj)
    {
        if (obj is not ResourceReferenceEntry other) return false;
        return SourceType == other.SourceType && SourceId == other.SourceId;
    }

    /// <summary>
    /// Override hash code for set operations.
    /// </summary>
    public override int GetHashCode()
        => HashCode.Combine(SourceType, SourceId);
}

/// <summary>
/// Record of when a resource's refcount became zero.
/// </summary>
internal class GracePeriodRecord
{
    /// <summary>
    /// Type of resource (opaque identifier).
    /// </summary>
    public string ResourceType { get; set; } = string.Empty;

    /// <summary>
    /// ID of the resource.
    /// </summary>
    public Guid ResourceId { get; set; }

    /// <summary>
    /// When the refcount became zero.
    /// </summary>
    public DateTimeOffset ZeroTimestamp { get; set; }
}

/// <summary>
/// Definition of a cleanup callback for a resource type.
/// </summary>
internal class CleanupCallbackDefinition
{
    /// <summary>
    /// Type of resource this cleanup handles (opaque identifier).
    /// </summary>
    public string ResourceType { get; set; } = string.Empty;

    /// <summary>
    /// Type of entity that will be cleaned up (opaque identifier).
    /// </summary>
    public string SourceType { get; set; } = string.Empty;

    /// <summary>
    /// Action to take when the resource is deleted.
    /// CASCADE (default): Delete dependent entities via callback.
    /// RESTRICT: Block deletion if references of this type exist.
    /// DETACH: Nullify references via callback (consumer implements).
    /// </summary>
    public OnDeleteAction OnDeleteAction { get; set; } = OnDeleteAction.CASCADE;

    /// <summary>
    /// Target service name for callback invocation.
    /// </summary>
    public string ServiceName { get; set; } = string.Empty;

    /// <summary>
    /// Endpoint path for callback invocation.
    /// </summary>
    public string CallbackEndpoint { get; set; } = string.Empty;

    /// <summary>
    /// JSON template with {{resourceId}} placeholder.
    /// </summary>
    public string PayloadTemplate { get; set; } = string.Empty;

    /// <summary>
    /// Human-readable description.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// When this callback was registered.
    /// </summary>
    public DateTimeOffset RegisteredAt { get; set; }
}

/// <summary>
/// Definition of a compression callback for a resource type.
/// </summary>
internal class CompressCallbackDefinition
{
    /// <summary>
    /// Type of resource this compression handles (opaque identifier).
    /// </summary>
    public string ResourceType { get; set; } = string.Empty;

    /// <summary>
    /// Type of data being compressed (opaque identifier, e.g., "character-personality").
    /// </summary>
    public string SourceType { get; set; } = string.Empty;

    /// <summary>
    /// Target service name for callback invocation.
    /// </summary>
    public string ServiceName { get; set; } = string.Empty;

    /// <summary>
    /// Endpoint path for compression callback invocation.
    /// </summary>
    public string CompressEndpoint { get; set; } = string.Empty;

    /// <summary>
    /// JSON template with {{resourceId}} placeholder for compression.
    /// </summary>
    public string CompressPayloadTemplate { get; set; } = string.Empty;

    /// <summary>
    /// Endpoint path for decompression callback invocation (nullable).
    /// </summary>
    public string? DecompressEndpoint { get; set; }

    /// <summary>
    /// JSON template with {{resourceId}} and {{data}} placeholders for decompression.
    /// </summary>
    public string? DecompressPayloadTemplate { get; set; }

    /// <summary>
    /// Execution order (lower = earlier).
    /// </summary>
    public int Priority { get; set; } = 100;

    /// <summary>
    /// Human-readable description.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// When this callback was registered.
    /// </summary>
    public DateTimeOffset RegisteredAt { get; set; }
}

/// <summary>
/// Internal model for archive storage in MySQL.
/// </summary>
internal class ResourceArchiveModel
{
    /// <summary>
    /// Unique identifier for this archive.
    /// </summary>
    public Guid ArchiveId { get; set; }

    /// <summary>
    /// Type of resource archived.
    /// </summary>
    public string ResourceType { get; set; } = string.Empty;

    /// <summary>
    /// ID of the resource archived.
    /// </summary>
    public Guid ResourceId { get; set; }

    /// <summary>
    /// Archive version (increments on re-compression).
    /// </summary>
    public int Version { get; set; }

    /// <summary>
    /// Data entries from each compression callback.
    /// </summary>
    public List<ArchiveEntryModel> Entries { get; set; } = new();

    /// <summary>
    /// When this archive was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Whether original source data was deleted after archival.
    /// </summary>
    public bool SourceDataDeleted { get; set; }
}

/// <summary>
/// Single entry in the archive bundle.
/// </summary>
internal class ArchiveEntryModel
{
    /// <summary>
    /// Type of data (e.g., "character-personality").
    /// </summary>
    public string SourceType { get; set; } = string.Empty;

    /// <summary>
    /// Service that provided the data.
    /// </summary>
    public string ServiceName { get; set; } = string.Empty;

    /// <summary>
    /// Base64-encoded gzipped JSON from the service callback.
    /// </summary>
    public string Data { get; set; } = string.Empty;

    /// <summary>
    /// When this entry was compressed.
    /// </summary>
    public DateTimeOffset CompressedAt { get; set; }

    /// <summary>
    /// SHA256 hash for integrity verification.
    /// </summary>
    public string? DataChecksum { get; set; }

    /// <summary>
    /// Size before compression.
    /// </summary>
    public int? OriginalSizeBytes { get; set; }
}

/// <summary>
/// Ephemeral snapshot of a living resource (stored in Redis with TTL).
/// </summary>
internal class ResourceSnapshotModel
{
    /// <summary>
    /// Unique identifier for this snapshot.
    /// </summary>
    public Guid SnapshotId { get; set; }

    /// <summary>
    /// Type of resource snapshotted (opaque identifier).
    /// </summary>
    public string ResourceType { get; set; } = string.Empty;

    /// <summary>
    /// ID of the resource snapshotted.
    /// </summary>
    public Guid ResourceId { get; set; }

    /// <summary>
    /// Label for snapshot purpose (e.g., "storyline_seed").
    /// </summary>
    public string SnapshotType { get; set; } = string.Empty;

    /// <summary>
    /// Data entries from each compression callback.
    /// </summary>
    public List<ArchiveEntryModel> Entries { get; set; } = new();

    /// <summary>
    /// When this snapshot was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// When this snapshot will expire (Redis TTL handles actual deletion).
    /// </summary>
    public DateTimeOffset ExpiresAt { get; set; }
}
