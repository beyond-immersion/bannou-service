namespace BeyondImmersion.Bannou.Core;

/// <summary>
/// Base interface for all lifecycle events (created, updated, deleted).
/// Lifecycle events carry the entity's temporal tracking fields that are
/// guaranteed present in every entity's storage model.
/// </summary>
/// <remarks>
/// <para>
/// Generated lifecycle events (from <c>x-lifecycle</c> in event schemas) implement
/// the appropriate sub-interface automatically. Non-lifecycle events that follow
/// the same patterns (e.g., a manually-defined <c>*.updated</c> event with
/// <c>ChangedFields</c>) can also implement these interfaces for uniform handling.
/// </para>
/// <para>
/// These interfaces complement <see cref="IBannouEvent"/> (which provides EventId,
/// Timestamp, EventName) by adding entity-level temporal fields that every lifecycle
/// event carries regardless of entity type.
/// </para>
/// </remarks>
public interface ILifecycleEvent
{
    /// <summary>
    /// Timestamp when the entity was originally created.
    /// Always present — every entity tracks its creation time.
    /// </summary>
    DateTimeOffset CreatedAt { get; }

    /// <summary>
    /// Timestamp when the entity was last updated.
    /// Set to CreatedAt on creation (entity was "last touched" at birth).
    /// </summary>
    DateTimeOffset UpdatedAt { get; }
}

/// <summary>
/// Interface for lifecycle created events.
/// Implemented by all generated <c>*CreatedEvent</c> classes.
/// </summary>
/// <remarks>
/// Created events carry the full entity snapshot at creation time.
/// The entity-specific fields (e.g., <c>AccountId</c>, <c>CharacterId</c>)
/// vary per entity and are not part of this interface.
/// </remarks>
public interface ILifecycleCreatedEvent : ILifecycleEvent
{
}

/// <summary>
/// Interface for lifecycle updated events.
/// Implemented by all generated <c>*UpdatedEvent</c> classes.
/// </summary>
/// <remarks>
/// <para>
/// Updated events carry the full entity snapshot after mutation plus a manifest
/// of which fields changed, enabling consumers to filter on relevant changes
/// without diffing the entire payload.
/// </para>
/// <para>
/// <c>ChangedFields</c> contains camelCase JSON property names matching the
/// event schema (e.g., "displayName", "email"), not C# PascalCase names.
/// </para>
/// </remarks>
public interface ILifecycleUpdatedEvent : ILifecycleEvent
{
    /// <summary>
    /// List of field names that were modified in this update.
    /// Uses camelCase JSON property names from the event schema.
    /// </summary>
    ICollection<string> ChangedFields { get; }
}

/// <summary>
/// Interface for lifecycle deleted events.
/// Implemented by all generated <c>*DeletedEvent</c> classes.
/// </summary>
/// <remarks>
/// Deleted events carry the full entity snapshot at deletion time plus an
/// optional reason. The reason is particularly useful for merge operations
/// (e.g., "Merged into {targetId}") and audit trails.
/// </remarks>
public interface ILifecycleDeletedEvent : ILifecycleEvent
{
    /// <summary>
    /// Optional reason for deletion (e.g., "Merged into {targetId}", "User requested").
    /// Null when no specific reason was provided.
    /// </summary>
    string? DeletedReason { get; }
}
