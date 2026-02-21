namespace BeyondImmersion.BannouService.Relationship;

/// <summary>
/// Internal data models for RelationshipService.
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
public partial class RelationshipService
{
    // This partial class declaration exists to signal that the models below
    // are owned by and used exclusively by this service. The models themselves
    // are defined at namespace level as internal classes.
}

/// <summary>
/// Internal model for storing relationships in state store.
/// </summary>
internal class RelationshipModel
{
    /// <summary>Unique identifier for the relationship.</summary>
    public Guid RelationshipId { get; set; }
    /// <summary>ID of the first entity in the relationship.</summary>
    public Guid Entity1Id { get; set; }
    /// <summary>Type of the first entity.</summary>
    public EntityType Entity1Type { get; set; }
    /// <summary>ID of the second entity in the relationship.</summary>
    public Guid Entity2Id { get; set; }
    /// <summary>Type of the second entity.</summary>
    public EntityType Entity2Type { get; set; }
    /// <summary>ID of the relationship type definition.</summary>
    public Guid RelationshipTypeId { get; set; }
    /// <summary>In-game timestamp when the relationship started.</summary>
    public DateTimeOffset StartedAt { get; set; }
    /// <summary>In-game timestamp when the relationship ended (null if active).</summary>
    public DateTimeOffset? EndedAt { get; set; }
    /// <summary>Type-specific metadata for the relationship.</summary>
    public object? Metadata { get; set; }
    /// <summary>Timestamp when the record was created.</summary>
    public DateTimeOffset CreatedAt { get; set; }
    /// <summary>Timestamp when the record was last updated.</summary>
    public DateTimeOffset? UpdatedAt { get; set; }
}

/// <summary>
/// Internal model for storing relationship types in state store.
/// </summary>
internal class RelationshipTypeModel
{
    /// <summary>Unique identifier for the relationship type.</summary>
    public Guid RelationshipTypeId { get; set; }
    /// <summary>Machine-readable code (uppercase normalized).</summary>
    public string Code { get; set; } = string.Empty;
    /// <summary>Human-readable display name.</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>Detailed description of the relationship type.</summary>
    public string? Description { get; set; }
    /// <summary>Category grouping (e.g., FAMILY, SOCIAL).</summary>
    public string? Category { get; set; }
    /// <summary>ID of the parent type in the hierarchy.</summary>
    public Guid? ParentTypeId { get; set; }
    /// <summary>Code of the parent type.</summary>
    public string? ParentTypeCode { get; set; }
    /// <summary>ID of the inverse relationship type.</summary>
    public Guid? InverseTypeId { get; set; }
    /// <summary>Code of the inverse relationship type.</summary>
    public string? InverseTypeCode { get; set; }
    /// <summary>Whether the relationship applies equally in both directions.</summary>
    public bool IsBidirectional { get; set; }
    /// <summary>Hierarchy depth level from root.</summary>
    public int Depth { get; set; }
    /// <summary>Additional custom metadata.</summary>
    public object? Metadata { get; set; }
    /// <summary>Whether this type is deprecated.</summary>
    public bool IsDeprecated { get; set; }
    /// <summary>Timestamp when deprecated.</summary>
    public DateTimeOffset? DeprecatedAt { get; set; }
    /// <summary>Reason for deprecation.</summary>
    public string? DeprecationReason { get; set; }
    /// <summary>Timestamp when the record was created.</summary>
    public DateTimeOffset CreatedAt { get; set; }
    /// <summary>Timestamp when the record was last updated.</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}
