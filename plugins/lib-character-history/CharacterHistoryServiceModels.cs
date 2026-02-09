namespace BeyondImmersion.BannouService.CharacterHistory;

/// <summary>
/// Internal data models for CharacterHistoryService.
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
public partial class CharacterHistoryService
{
    // This partial class declaration exists to signal that the models below
    // are owned by and used exclusively by this service. The models themselves
    // are defined at namespace level as internal classes.
}

// ============================================================================
// Internal Data Models
// ============================================================================

/// <summary>
/// Internal storage model for participation data.
/// </summary>
internal class ParticipationData
{
    public Guid ParticipationId { get; set; }
    public Guid CharacterId { get; set; }
    public Guid EventId { get; set; }
    public string EventName { get; set; } = string.Empty;
    public EventCategory EventCategory { get; set; }
    public ParticipationRole Role { get; set; }
    public long EventDateUnix { get; set; }
    public float Significance { get; set; }
    public object? Metadata { get; set; }
    public long CreatedAtUnix { get; set; }
}

/// <summary>
/// Internal storage model for backstory data.
/// </summary>
internal class BackstoryData
{
    public Guid CharacterId { get; set; }
    public List<BackstoryElementData> Elements { get; set; } = new();
    public long CreatedAtUnix { get; set; }
    public long UpdatedAtUnix { get; set; }
}

/// <summary>
/// Internal storage model for a backstory element.
/// </summary>
internal class BackstoryElementData
{
    public BackstoryElementType ElementType { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public float Strength { get; set; }
    public Guid? RelatedEntityId { get; set; }
    public string? RelatedEntityType { get; set; }
}
