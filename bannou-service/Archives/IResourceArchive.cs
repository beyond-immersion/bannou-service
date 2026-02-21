namespace BeyondImmersion.BannouService.Archives;

/// <summary>
/// Marker interface for resource archive types that can be stored
/// in the resource service's archive bundles and consumed by the
/// storyline SDK's ArchiveExtractor.
///
/// Archives implementing this interface follow the schema-first pattern
/// with x-archive-type: true in their OpenAPI schema definition.
/// </summary>
public interface IResourceArchive
{
    /// <summary>
    /// Unique identifier of the archived resource.
    /// This is the primary key for the resource being archived.
    /// </summary>
    Guid ResourceId { get; }

    /// <summary>
    /// Type identifier for the archived resource.
    /// Examples: "character", "character-personality", "character-history",
    /// "character-encounter", "realm-history".
    /// </summary>
    string ResourceType { get; }

    /// <summary>
    /// When this archive was created.
    /// Used for versioning and ordering of archives.
    /// </summary>
    DateTimeOffset ArchivedAt { get; }

    /// <summary>
    /// Schema version for forward compatibility migration.
    /// Increment when breaking changes are made to the archive format.
    /// </summary>
    int SchemaVersion { get; }

    /// <summary>
    /// Child archives from dependent resources.
    /// Populated by lib-resource compression when archiving hierarchical data.
    /// For example, a character archive may contain nested personality,
    /// history, and encounter archives.
    /// </summary>
    IReadOnlyList<IResourceArchive> NestedArchives { get; }
}
