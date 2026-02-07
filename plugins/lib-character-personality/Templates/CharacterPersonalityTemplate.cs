// =============================================================================
// Character Personality Resource Template
// Defines valid paths for personality snapshot data access.
// =============================================================================

using BeyondImmersion.BannouService.CharacterPersonality;
using BeyondImmersion.BannouService.ResourceTemplates;

namespace BeyondImmersion.LibCharacterPersonality.Templates;

/// <summary>
/// Resource template for character personality snapshot data.
/// Enables compile-time validation of paths like ${candidate.personality.personality.archetypeHint}.
/// </summary>
/// <remarks>
/// <para>
/// The CharacterPersonalityArchive contains:
/// <list type="bullet">
///   <item>characterId: The character this data belongs to</item>
///   <item>hasPersonality: Flag indicating if personality traits exist</item>
///   <item>personality: PersonalityResponse with traits and archetype hint</item>
///   <item>hasCombatPreferences: Flag indicating if combat preferences exist</item>
///   <item>combatPreferences: CombatPreferencesResponse with style/range/etc.</item>
/// </list>
/// </para>
/// <para>
/// Traits are stored as a collection keyed by TraitAxis enum. ABML should use
/// iteration or filtering to access specific trait values:
/// <code>
/// # Iterate all traits
/// for trait in ${personality.personality.traits}:
///   log: "Trait ${trait.axis} = ${trait.value}"
///
/// # Or check hasPersonality first
/// cond:
///   - when: ${personality.hasPersonality}
///     then:
///       - log: "Archetype: ${personality.personality.archetypeHint}"
/// </code>
/// </para>
/// </remarks>
public sealed class CharacterPersonalityTemplate : ResourceTemplateBase
{
    /// <inheritdoc />
    public override string SourceType => "character-personality";

    /// <inheritdoc />
    public override string Namespace => "personality";

    /// <inheritdoc />
    public override IReadOnlyDictionary<string, Type> ValidPaths { get; } = new Dictionary<string, Type>
    {
        // Root archive type
        [""] = typeof(CharacterPersonalityArchive),
        ["characterId"] = typeof(Guid),
        ["hasPersonality"] = typeof(bool),
        ["hasCombatPreferences"] = typeof(bool),

        // Personality data (nullable - check hasPersonality first)
        ["personality"] = typeof(PersonalityResponse),
        ["personality.characterId"] = typeof(Guid),
        ["personality.traits"] = typeof(ICollection<TraitValue>),
        ["personality.version"] = typeof(int),
        ["personality.archetypeHint"] = typeof(string),
        ["personality.createdAt"] = typeof(DateTimeOffset),
        ["personality.updatedAt"] = typeof(DateTimeOffset),

        // TraitValue properties (accessed via collection iteration/filter)
        // Note: traits is a collection keyed by TraitAxis enum, not direct dot-access
        // ABML would use: for trait in ${personality.personality.traits}: ${trait.value}
        // Individual trait access is validated at runtime, not compile-time

        // Combat preferences (nullable - check hasCombatPreferences first)
        ["combatPreferences"] = typeof(CombatPreferencesResponse),
        ["combatPreferences.characterId"] = typeof(Guid),
        ["combatPreferences.preferences"] = typeof(CombatPreferences),
        ["combatPreferences.preferences.style"] = typeof(CombatStyle),
        ["combatPreferences.preferences.preferredRange"] = typeof(PreferredRange),
        ["combatPreferences.preferences.groupRole"] = typeof(GroupRole),
        ["combatPreferences.preferences.riskTolerance"] = typeof(float),
        ["combatPreferences.preferences.retreatThreshold"] = typeof(float),
        ["combatPreferences.preferences.protectAllies"] = typeof(bool),
        ["combatPreferences.version"] = typeof(int),
        ["combatPreferences.createdAt"] = typeof(DateTimeOffset),
        ["combatPreferences.updatedAt"] = typeof(DateTimeOffset),
    };
}
