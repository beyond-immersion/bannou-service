// =============================================================================
// Character History Resource Template
// Defines valid paths for history archive data access.
// =============================================================================

using BeyondImmersion.BannouService.CharacterHistory;
using BeyondImmersion.BannouService.ResourceTemplates;

namespace BeyondImmersion.BannouService.CharacterHistory.Templates;

/// <summary>
/// Resource template for character history archive data.
/// Enables compile-time validation of paths like ${candidate.history.hasBackstory}.
/// </summary>
/// <remarks>
/// <para>
/// The CharacterHistoryArchive contains:
/// <list type="bullet">
///   <item>characterId: The character this data belongs to</item>
///   <item>hasParticipations: Flag indicating if historical participations exist</item>
///   <item>participations: Collection of HistoricalParticipation records</item>
///   <item>hasBackstory: Flag indicating if backstory elements exist</item>
///   <item>backstory: BackstoryResponse with character background elements</item>
///   <item>summaries: HistorySummaryResponse with text summaries</item>
/// </list>
/// </para>
/// <para>
/// Participations and backstory elements are stored as collections. ABML should use
/// iteration or filtering to access specific values:
/// <code>
/// # Iterate all participations
/// for participation in ${history.participations}:
///   log: "Event ${participation.eventName} (${participation.role})"
///
/// # Or check hasBackstory first
/// cond:
///   - when: ${history.hasBackstory}
///     then:
///       - for element in ${history.backstory.elements}:
///           log: "${element.key}: ${element.value}"
/// </code>
/// </para>
/// </remarks>
public sealed class CharacterHistoryTemplate : ResourceTemplateBase
{
    /// <inheritdoc />
    public override string SourceType => "character-history";

    /// <inheritdoc />
    public override string Namespace => "history";

    /// <inheritdoc />
    public override IReadOnlyDictionary<string, Type> ValidPaths { get; } = new Dictionary<string, Type>
    {
        // Root archive type (inherits from ResourceArchiveBase)
        [""] = typeof(CharacterHistoryArchive),

        // ResourceArchiveBase inherited properties
        ["resourceId"] = typeof(Guid),
        ["resourceType"] = typeof(string),
        ["archivedAt"] = typeof(DateTimeOffset),
        ["schemaVersion"] = typeof(int),

        // CharacterHistoryArchive properties
        ["characterId"] = typeof(Guid),
        ["hasParticipations"] = typeof(bool),
        ["hasBackstory"] = typeof(bool),

        // Participations collection (check hasParticipations first)
        ["participations"] = typeof(ICollection<HistoricalParticipation>),

        // HistoricalParticipation properties (accessed via collection iteration)
        // for participation in ${history.participations}: ${participation.eventName}

        // Backstory data (nullable - check hasBackstory first)
        ["backstory"] = typeof(BackstoryResponse),
        ["backstory.characterId"] = typeof(Guid),
        ["backstory.elements"] = typeof(ICollection<BackstoryElement>),
        ["backstory.createdAt"] = typeof(DateTimeOffset?),
        ["backstory.updatedAt"] = typeof(DateTimeOffset?),

        // BackstoryElement properties (accessed via collection iteration)
        // for element in ${history.backstory.elements}: ${element.key}

        // Summaries (nullable)
        ["summaries"] = typeof(HistorySummaryResponse),
        ["summaries.characterId"] = typeof(Guid),
        ["summaries.keyBackstoryPoints"] = typeof(ICollection<string>),
        ["summaries.majorLifeEvents"] = typeof(ICollection<string>),
    };
}
