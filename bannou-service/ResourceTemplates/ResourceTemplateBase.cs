// =============================================================================
// Resource Template Base
// Base class for resource templates with common validation logic.
// =============================================================================

using BeyondImmersion.Bannou.BehaviorCompiler.Templates;

namespace BeyondImmersion.BannouService.ResourceTemplates;

/// <summary>
/// Base class for resource templates with common validation logic.
/// </summary>
/// <remarks>
/// <para>
/// Plugin implementations should extend this class and provide:
/// <list type="bullet">
///   <item><see cref="SourceType"/>: The sourceType from x-compression-callback</item>
///   <item><see cref="Namespace"/>: Short name for ABML expressions</item>
///   <item><see cref="ValidPaths"/>: Dictionary of all navigable paths and their types</item>
/// </list>
/// </para>
/// <para>
/// <b>ValidPaths Dictionary</b>:
/// </para>
/// <list type="bullet">
///   <item>Use "" (empty string) for the root object type</item>
///   <item>Use dot notation for nested paths (e.g., "personality.archetypeHint")</item>
///   <item>Include all leaf paths AND intermediate object paths</item>
///   <item>For collections, include the collection type (iteration is validated separately)</item>
/// </list>
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
///             ["characterId"] = typeof(Guid),
///             ["hasPersonality"] = typeof(bool),
///             ["personality"] = typeof(PersonalityResponse),
///             ["personality.archetypeHint"] = typeof(string),
///         };
/// }
/// </code>
/// </example>
/// </remarks>
public abstract class ResourceTemplateBase : IResourceTemplate
{
    /// <inheritdoc />
    public abstract string SourceType { get; }

    /// <inheritdoc />
    public abstract string Namespace { get; }

    /// <inheritdoc />
    public abstract IReadOnlyDictionary<string, Type> ValidPaths { get; }

    /// <inheritdoc />
    public PathValidationResult ValidatePath(string path)
    {
        // Empty path = root object
        if (string.IsNullOrEmpty(path))
        {
            return ValidPaths.TryGetValue("", out var rootType)
                ? PathValidationResult.Valid(rootType)
                : PathValidationResult.InvalidPath("", ValidPaths.Keys);
        }

        // Direct match in ValidPaths
        if (ValidPaths.TryGetValue(path, out var type))
        {
            return PathValidationResult.Valid(type);
        }

        // Check if this is a valid intermediate path (prefix of known paths)
        // This allows navigating to container objects even if they're not explicitly listed
        var isIntermediate = ValidPaths.Keys.Any(k =>
            k.StartsWith(path + ".", StringComparison.OrdinalIgnoreCase));

        if (isIntermediate)
        {
            // Return object type for intermediate nodes - caller knows it's valid but not terminal
            return PathValidationResult.Valid(typeof(object));
        }

        return PathValidationResult.InvalidPath(path, ValidPaths.Keys);
    }
}
