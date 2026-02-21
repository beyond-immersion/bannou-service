namespace BeyondImmersion.BannouService.CharacterPersonality;

/// <summary>
/// Internal data models for CharacterPersonalityService.
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
public partial class CharacterPersonalityService
{
    // This partial class declaration exists to signal that the models below
    // are owned by and used exclusively by this service. The models themselves
    // are defined at namespace level as internal classes.
}

// ============================================================================
// INTERNAL DATA MODELS
// ============================================================================

/// <summary>
/// Internal storage model for personality data.
/// </summary>
internal class PersonalityData
{
    public Guid CharacterId { get; set; }
    public Dictionary<TraitAxis, float> Traits { get; set; } = new();
    public string? ArchetypeHint { get; set; }
    public int Version { get; set; }
    public long CreatedAtUnix { get; set; }
    public long UpdatedAtUnix { get; set; }
}

/// <summary>
/// Internal storage model for combat preferences data.
/// </summary>
internal class CombatPreferencesData
{
    public Guid CharacterId { get; set; }
    public CombatStyle Style { get; set; } = CombatStyle.BALANCED;
    public PreferredRange PreferredRange { get; set; } = PreferredRange.MEDIUM;
    public GroupRole GroupRole { get; set; } = GroupRole.FRONTLINE;
    public float RiskTolerance { get; set; } = 0.5f;
    public float RetreatThreshold { get; set; } = 0.3f;
    public bool ProtectAllies { get; set; } = true;
    public int Version { get; set; }
    public long CreatedAtUnix { get; set; }
    public long UpdatedAtUnix { get; set; }
}
