// =============================================================================
// Resource Template Interface
// Defines the shape of resource snapshot data for compile-time validation.
// =============================================================================

namespace BeyondImmersion.Bannou.BehaviorCompiler.Templates;

/// <summary>
/// Template defining the shape of resource snapshot data.
/// Enables compile-time path validation and typed deserialization.
/// </summary>
/// <remarks>
/// <para>
/// Resource templates are registered by plugins during startup. The SemanticAnalyzer
/// uses them to validate ABML expressions that access resource snapshot data.
/// </para>
/// <para>
/// <b>Implementation Pattern</b>:
/// Plugins create concrete implementations by extending ResourceTemplateBase
/// (in bannou-service) and populating the ValidPaths dictionary with all
/// navigable paths and their types.
/// </para>
/// <para>
/// <b>Path Format</b>:
/// Paths use dot notation relative to the template namespace. For example,
/// if the namespace is "personality", the path "traits.AGGRESSION" validates
/// expressions like ${candidate.personality.traits.AGGRESSION}.
/// </para>
/// <example>
/// <code>
/// public class CharacterPersonalityTemplate : ResourceTemplateBase
/// {
///     public override string SourceType => "character-personality";
///     public override string Namespace => "personality";
///
///     public override IReadOnlyDictionary&lt;string, Type&gt; ValidPaths { get; } =
///         new Dictionary&lt;string, Type&gt;
///         {
///             [""] = typeof(CharacterPersonalityArchive),
///             ["personality.archetypeHint"] = typeof(string),
///             ["combatPreferences.preferences.style"] = typeof(CombatStyle),
///         };
/// }
/// </code>
/// </example>
/// </remarks>
public interface IResourceTemplate
{
    /// <summary>
    /// The sourceType this template handles (e.g., "character-personality").
    /// </summary>
    /// <remarks>
    /// Must match the sourceType used in x-compression-callback registration.
    /// This is the value used in ABML resource_templates metadata.
    /// </remarks>
    string SourceType { get; }

    /// <summary>
    /// Short namespace for ABML paths (e.g., "personality").
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the first path segment used in expressions like
    /// ${candidate.personality.archetypeHint}. May be the same as
    /// SourceType or an alias for brevity.
    /// </para>
    /// <para>
    /// The namespace must be unique across all registered templates.
    /// </para>
    /// </remarks>
    string Namespace { get; }

    /// <summary>
    /// Valid paths and their value types for compile-time validation.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Keys are dot-separated paths relative to this template's namespace.
    /// The empty string key ("") represents the root object type.
    /// </para>
    /// <para>
    /// All leaf and intermediate paths should be included. For collections,
    /// include the collection type but not individual element access
    /// (those are validated differently).
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// [""] = typeof(CharacterPersonalityArchive),
    /// ["characterId"] = typeof(Guid),
    /// ["personality"] = typeof(PersonalityResponse),
    /// ["personality.traits"] = typeof(ICollection&lt;TraitValue&gt;),
    /// ["personality.archetypeHint"] = typeof(string),
    /// ["combatPreferences.preferences.style"] = typeof(CombatStyle),
    /// </code>
    /// </example>
    IReadOnlyDictionary<string, Type> ValidPaths { get; }

    /// <summary>
    /// Validates a path against this template's schema.
    /// </summary>
    /// <param name="path">Dot-separated path (e.g., "personality.archetypeHint").</param>
    /// <returns>
    /// Validation result with expected type if valid,
    /// or error message with suggestions if invalid.
    /// </returns>
    PathValidationResult ValidatePath(string path);
}
