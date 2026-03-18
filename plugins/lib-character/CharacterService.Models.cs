namespace BeyondImmersion.BannouService.Character;

/// <summary>
/// Internal data models for CharacterService.
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
public partial class CharacterService
{
    // This partial class declaration exists to signal that the models below
    // are owned by and used exclusively by this service. The models themselves
    // are defined at namespace level as internal classes.
}

/// <summary>
/// Character data model for lib-state storage.
/// Uses Guid types for type-safe ID handling per IMPLEMENTATION TENETS.
/// </summary>
internal class CharacterModel
{
    public Guid CharacterId { get; set; }
    public string Name { get; set; } = string.Empty;
    public Guid RealmId { get; set; }
    public Guid SpeciesId { get; set; }
    public CharacterStatus Status { get; set; } = CharacterStatus.Alive;

    // Store as DateTimeOffset directly - lib-state handles serialization
    public DateTimeOffset BirthDate { get; set; }
    public DateTimeOffset? DeathDate { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// Archive data model for compressed characters.
/// Uses Guid types for type-safe ID handling per IMPLEMENTATION TENETS.
/// </summary>
internal class CharacterArchiveModel
{
    public Guid CharacterId { get; set; }
    public string Name { get; set; } = string.Empty;
    public Guid RealmId { get; set; }
    public Guid SpeciesId { get; set; }
    public long BirthDateUnix { get; set; }
    public long DeathDateUnix { get; set; }
    public long CompressedAtUnix { get; set; }
    public string? PersonalitySummary { get; set; }
    public List<string>? KeyBackstoryPoints { get; set; }
    public List<string>? MajorLifeEvents { get; set; }
    public string? FamilySummary { get; set; }
}

/// <summary>
/// Reference count tracking data for cleanup eligibility.
/// Uses Guid type for type-safe ID handling per IMPLEMENTATION TENETS.
/// </summary>
internal class RefCountData
{
    public Guid CharacterId { get; set; }
    public long? ZeroRefSinceUnix { get; set; }
}
