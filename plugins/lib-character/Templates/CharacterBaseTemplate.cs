// =============================================================================
// Character Base Resource Template
// Defines valid paths for character base archive data access.
// =============================================================================

using BeyondImmersion.BannouService.Character;
using BeyondImmersion.BannouService.ResourceTemplates;

namespace BeyondImmersion.LibCharacter.Templates;

/// <summary>
/// Resource template for character base archive data.
/// Enables compile-time validation of paths like ${candidate.character.name}.
/// </summary>
/// <remarks>
/// <para>
/// The CharacterBaseArchive contains the core identity of a character:
/// <list type="bullet">
///   <item>characterId: The unique identifier (equals resourceId)</item>
///   <item>name: Display name of the character</item>
///   <item>realmId: The realm partition key</item>
///   <item>speciesId: Foreign key to the Species service</item>
///   <item>birthDate/deathDate: In-game lifecycle timestamps</item>
///   <item>status: Current lifecycle status (alive/dead/dormant)</item>
///   <item>familySummary: Optional text summary of family relationships</item>
/// </list>
/// </para>
/// <para>
/// This template represents the foundational character data from the Character
/// service (L2). Higher-layer services like CharacterPersonality and CharacterHistory
/// provide additional character data through their own templates.
/// </para>
/// <para>
/// Example ABML usage:
/// <code>
/// # Access character base data
/// log: "Character ${character.name} from realm ${character.realmId}"
///
/// # Check status for conditionals
/// cond:
///   - when: ${character.status} == "dead"
///     then:
///       - log: "This character has passed on"
/// </code>
/// </para>
/// </remarks>
public sealed class CharacterBaseTemplate : ResourceTemplateBase
{
    /// <inheritdoc />
    public override string SourceType => "character";

    /// <inheritdoc />
    public override string Namespace => "character";

    /// <inheritdoc />
    public override IReadOnlyDictionary<string, Type> ValidPaths { get; } = new Dictionary<string, Type>
    {
        // Root archive type (inherits from ResourceArchiveBase)
        [""] = typeof(CharacterBaseArchive),

        // ResourceArchiveBase inherited properties
        ["resourceId"] = typeof(Guid),
        ["resourceType"] = typeof(string),
        ["archivedAt"] = typeof(DateTimeOffset),
        ["schemaVersion"] = typeof(int),

        // CharacterBaseArchive properties
        ["characterId"] = typeof(Guid),
        ["name"] = typeof(string),
        ["realmId"] = typeof(Guid),
        ["speciesId"] = typeof(Guid),
        ["birthDate"] = typeof(DateTimeOffset),
        ["deathDate"] = typeof(DateTimeOffset),
        ["status"] = typeof(CharacterStatus),
        ["familySummary"] = typeof(string),
        ["createdAt"] = typeof(DateTimeOffset),
        ["updatedAt"] = typeof(DateTimeOffset?),
    };
}
