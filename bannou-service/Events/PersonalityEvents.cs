// =============================================================================
// Personality Events
// Event types for personality and combat preferences evolution.
// Used by consumers to handle personality cache invalidation.
// =============================================================================

using System.Text.Json.Serialization;

namespace BeyondImmersion.BannouService.Events;

/// <summary>
/// Event published when a character's personality evolves through experience.
/// Published to "personality.evolved" topic.
/// </summary>
public class PersonalityEvolvedEvent : BaseServiceEvent
{
    /// <summary>
    /// Fixed event type identifier.
    /// </summary>
    public override string EventName => "personality.evolved";

    /// <summary>
    /// The character whose personality evolved.
    /// </summary>
    [JsonPropertyName("characterId")]
    public Guid CharacterId { get; set; }

    /// <summary>
    /// The type of experience that triggered evolution.
    /// </summary>
    [JsonPropertyName("experienceType")]
    public string ExperienceType { get; set; } = string.Empty;

    /// <summary>
    /// Intensity of the experience (0-1).
    /// </summary>
    [JsonPropertyName("intensity")]
    public float Intensity { get; set; }

    /// <summary>
    /// New version number after evolution.
    /// </summary>
    [JsonPropertyName("version")]
    public int Version { get; set; }

    /// <summary>
    /// List of trait axes that were affected.
    /// </summary>
    [JsonPropertyName("affectedTraits")]
    public List<string> AffectedTraits { get; set; } = new();
}

/// <summary>
/// Event published when a character's combat preferences evolve through combat experience.
/// Published to "combat-preferences.evolved" topic.
/// </summary>
public class CombatPreferencesEvolvedEvent : BaseServiceEvent
{
    /// <summary>
    /// Fixed event type identifier.
    /// </summary>
    public override string EventName => "combat-preferences.evolved";

    /// <summary>
    /// The character whose combat preferences evolved.
    /// </summary>
    [JsonPropertyName("characterId")]
    public Guid CharacterId { get; set; }

    /// <summary>
    /// The type of combat experience that triggered evolution.
    /// </summary>
    [JsonPropertyName("experienceType")]
    public string ExperienceType { get; set; } = string.Empty;

    /// <summary>
    /// Intensity of the experience (0-1).
    /// </summary>
    [JsonPropertyName("intensity")]
    public float Intensity { get; set; }

    /// <summary>
    /// New version number after evolution.
    /// </summary>
    [JsonPropertyName("version")]
    public int Version { get; set; }
}
