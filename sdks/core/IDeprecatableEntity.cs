namespace BeyondImmersion.Bannou.Core;

/// <summary>
/// Interface for entities that support the deprecation lifecycle per IMPLEMENTATION TENETS.
/// Implemented by generated lifecycle event classes when the entity's
/// <c>x-lifecycle</c> definition includes <c>deprecation: true</c>.
/// </summary>
/// <remarks>
/// <para>
/// Two deprecation categories exist (Category A: world-building definitions,
/// Category B: content templates), but both share the same three-field model.
/// The category distinction affects endpoint design (deprecate/undeprecate/delete
/// vs deprecate-only), not the data contract.
/// </para>
/// <para>
/// Deprecation state changes are published via <c>*.updated</c> events with
/// <c>ChangedFields</c> containing "isDeprecated", "deprecatedAt", and
/// "deprecationReason" — not via dedicated deprecation events.
/// </para>
/// </remarks>
public interface IDeprecatableEntity
{
    /// <summary>
    /// Whether this entity is deprecated.
    /// Deprecated entities should not be used for new instance creation.
    /// </summary>
    bool IsDeprecated { get; }

    /// <summary>
    /// When the entity was deprecated. Null if not deprecated.
    /// </summary>
    DateTimeOffset? DeprecatedAt { get; }

    /// <summary>
    /// Reason for deprecation. Null if not deprecated or no reason provided.
    /// Maximum 500 characters.
    /// </summary>
    string? DeprecationReason { get; }
}
