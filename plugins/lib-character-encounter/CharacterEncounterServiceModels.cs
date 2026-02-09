namespace BeyondImmersion.BannouService.CharacterEncounter;

/// <summary>
/// Internal data models for CharacterEncounterService.
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
public partial class CharacterEncounterService
{
    // This partial class declaration exists to signal that the models below
    // are owned by and used exclusively by this service. The models themselves
    // are defined at namespace level as internal classes.
}

// ============================================================================
// Internal Data Models
// ============================================================================

internal record BuiltInEncounterType(string Code, string Name, string Description, EmotionalImpact DefaultEmotionalImpact, int SortOrder);

internal class EncounterTypeData
{
    public Guid TypeId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsBuiltIn { get; set; }
    public EmotionalImpact? DefaultEmotionalImpact { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public long CreatedAtUnix { get; set; }
}

internal class EncounterData
{
    public Guid EncounterId { get; set; }
    public long Timestamp { get; set; }
    public Guid RealmId { get; set; }
    public Guid? LocationId { get; set; }
    public string EncounterTypeCode { get; set; } = string.Empty;
    public string? Context { get; set; }
    public EncounterOutcome Outcome { get; set; }
    public List<Guid> ParticipantIds { get; set; } = new();
    public object? Metadata { get; set; }
    public long CreatedAtUnix { get; set; }
}

internal class PerspectiveData
{
    public Guid PerspectiveId { get; set; }
    public Guid EncounterId { get; set; }
    public Guid CharacterId { get; set; }
    public EmotionalImpact EmotionalImpact { get; set; }
    public float? SentimentShift { get; set; }
    public float MemoryStrength { get; set; } = 1.0f;
    public string? RememberedAs { get; set; }
    public long? LastDecayedAtUnix { get; set; }
    public long CreatedAtUnix { get; set; }
    public long? UpdatedAtUnix { get; set; }
}

internal class CharacterIndexData
{
    public Guid CharacterId { get; set; }
    public List<Guid> PerspectiveIds { get; set; } = new();
}

internal class PairIndexData
{
    public Guid CharacterIdA { get; set; }
    public Guid CharacterIdB { get; set; }
    public List<Guid> EncounterIds { get; set; } = new();
}

internal class LocationIndexData
{
    public Guid LocationId { get; set; }
    public List<Guid> EncounterIds { get; set; } = new();
}

/// <summary>
/// Global index tracking all character IDs that have encounter perspectives.
/// Used for global memory decay operations.
/// </summary>
internal class GlobalCharacterIndexData
{
    public List<Guid> CharacterIds { get; set; } = new();
}

/// <summary>
/// Index tracking all custom encounter type codes.
/// Used for listing custom types since state stores don't support prefix queries.
/// </summary>
internal class CustomTypeIndexData
{
    public List<string> TypeCodes { get; set; } = new();
}

/// <summary>
/// Index tracking encounter IDs using a specific encounter type code.
/// Used for validating type deletion (409 if encounters exist using the type).
/// </summary>
internal class TypeEncounterIndexData
{
    public string TypeCode { get; set; } = string.Empty;
    public List<Guid> EncounterIds { get; set; } = new();
}

/// <summary>
/// Index mapping encounter IDs to their perspective IDs.
/// Eliminates O(N*M) scan when loading perspectives for an encounter.
/// </summary>
internal class EncounterPerspectiveIndexData
{
    public Guid EncounterId { get; set; }
    public List<Guid> PerspectiveIds { get; set; } = new();
}
