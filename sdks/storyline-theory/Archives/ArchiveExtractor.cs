// Copyright (c) Beyond Immersion. All rights reserved.

using BeyondImmersion.Bannou.StorylineTheory.Planning;
using BeyondImmersion.Bannou.StorylineTheory.Spectrums;

namespace BeyondImmersion.Bannou.StorylineTheory.Archives;

/// <summary>
/// Extracts narrative-relevant data from archives using opaque keys.
/// The SDK doesn't reference plugins directly - it receives a generic bundle
/// and extracts what it knows how to interpret.
/// </summary>
/// <remarks>
/// <para>
/// The ArchiveExtractor builds a WorldState from archive data for use in
/// GOAP planning. It extracts facts about characters, their relationships,
/// personality traits, and historical context.
/// </para>
/// <para>
/// Facts are keyed by convention:
/// - "character.{id}.trait.{axis}" - personality traits
/// - "character.{id}.status" - alive/dead/dormant
/// - "character.{id}.realm" - realm membership
/// - "character.{id}.relationship.{otherId}" - aggregate sentiment
/// - "history.participation.{eventId}" - historical event involvement
/// </para>
/// </remarks>
public sealed class ArchiveExtractor
{
    /// <summary>
    /// Extract WorldState from an archive bundle using opaque string keys.
    /// Unknown keys are silently ignored (forward compatibility).
    /// </summary>
    /// <param name="bundle">The archive bundle containing data entries.</param>
    /// <param name="primarySpectrum">The primary spectrum for the story.</param>
    /// <returns>The extracted world state.</returns>
    public WorldState ExtractWorldState(ArchiveBundle bundle, SpectrumType primarySpectrum)
    {
        var facts = new Dictionary<string, object>();

        // Extract facts from each archive type
        ExtractCharacterFacts(bundle, facts);
        ExtractHistoryFacts(bundle, facts);
        ExtractEncounterFacts(bundle, facts);
        ExtractPersonalityFacts(bundle, facts);

        return new WorldState
        {
            NarrativeState = CreateInitialNarrativeState(primarySpectrum),
            Facts = facts,
            Position = 0.0
        };
    }

    /// <summary>
    /// Extract facts from character base archive.
    /// </summary>
    private static void ExtractCharacterFacts(ArchiveBundle bundle, Dictionary<string, object> facts)
    {
        if (!bundle.TryGetEntry<CharacterBaseArchive>("character", out var character) || character == null)
        {
            return;
        }

        var prefix = $"character.{character.CharacterId}";

        facts[$"{prefix}.status"] = character.Status.ToString().ToLowerInvariant();
        facts[$"{prefix}.realm"] = character.RealmId;
        facts[$"{prefix}.species"] = character.SpeciesId;
        facts[$"{prefix}.birthDate"] = character.BirthDate;
        facts[$"{prefix}.deathDate"] = character.DeathDate;
        facts[$"{prefix}.name"] = character.Name;

        if (!string.IsNullOrEmpty(character.FamilySummary))
        {
            facts[$"{prefix}.familySummary"] = character.FamilySummary;
        }
    }

    /// <summary>
    /// Extract facts from character history archive.
    /// </summary>
    private static void ExtractHistoryFacts(ArchiveBundle bundle, Dictionary<string, object> facts)
    {
        if (!bundle.TryGetEntry<CharacterHistoryArchive>("character-history", out var history) || history == null)
        {
            return;
        }

        var characterId = history.CharacterId;
        var prefix = $"character.{characterId}";

        // Extract historical participations
        if (history.HasParticipations && history.Participations != null)
        {
            facts[$"{prefix}.participationCount"] = history.Participations.Count;

            foreach (var p in history.Participations)
            {
                var eventPrefix = $"history.{characterId}.event.{p.EventId}";
                facts[$"{eventPrefix}.name"] = p.EventName;
                facts[$"{eventPrefix}.category"] = p.EventCategory.ToString();
                facts[$"{eventPrefix}.role"] = p.Role.ToString();
                facts[$"{eventPrefix}.significance"] = (double)p.Significance;
                facts[$"{eventPrefix}.date"] = p.EventDate;
            }
        }

        // Extract backstory elements
        if (history.HasBackstory && history.Backstory?.Elements != null)
        {
            foreach (var element in history.Backstory.Elements)
            {
                var backstoryPrefix = $"{prefix}.backstory.{element.ElementType.ToString().ToLowerInvariant()}.{element.Key}";
                facts[$"{backstoryPrefix}.value"] = element.Value;
                facts[$"{backstoryPrefix}.strength"] = (double)element.Strength;

                if (element.RelatedEntityId.HasValue)
                {
                    facts[$"{backstoryPrefix}.relatedEntity"] = element.RelatedEntityId.Value;
                    facts[$"{backstoryPrefix}.relatedEntityType"] = element.RelatedEntityType ?? "unknown";
                }
            }
        }
    }

    /// <summary>
    /// Extract facts from character encounter archive.
    /// </summary>
    private static void ExtractEncounterFacts(ArchiveBundle bundle, Dictionary<string, object> facts)
    {
        if (!bundle.TryGetEntry<CharacterEncounterArchive>("character-encounter", out var encounters) || encounters == null)
        {
            return;
        }

        var characterId = encounters.CharacterId;
        var prefix = $"character.{characterId}";

        facts[$"{prefix}.hasEncounters"] = encounters.HasEncounters;
        facts[$"{prefix}.encounterCount"] = encounters.EncounterCount;

        // Extract aggregate sentiment relationships
        if (encounters.AggregateSentiment != null)
        {
            foreach (var (otherIdStr, sentiment) in encounters.AggregateSentiment)
            {
                if (Guid.TryParse(otherIdStr, out var otherId))
                {
                    facts[$"{prefix}.relationship.{otherId}.sentiment"] = (double)sentiment;
                }
            }
        }

        // Extract significant encounters
        if (encounters.HasEncounters && encounters.Encounters != null)
        {
            foreach (var enc in encounters.Encounters)
            {
                var perspective = enc.Perspectives
                    .FirstOrDefault(p => p.CharacterId == characterId);

                if (perspective == null)
                {
                    continue;
                }

                // Only include encounters with strong memory
                if (perspective.MemoryStrength > 0.5f)
                {
                    var encPrefix = $"{prefix}.encounter.{enc.Encounter.EncounterId}";
                    facts[$"{encPrefix}.type"] = enc.Encounter.EncounterTypeCode;
                    facts[$"{encPrefix}.outcome"] = enc.Encounter.Outcome.ToString();
                    facts[$"{encPrefix}.sentiment"] = (double)(perspective.SentimentShift ?? 0);
                    facts[$"{encPrefix}.memoryStrength"] = (double)perspective.MemoryStrength;
                    facts[$"{encPrefix}.emotionalImpact"] = perspective.EmotionalImpact.ToString();
                    facts[$"{encPrefix}.location"] = enc.Encounter.LocationId ?? Guid.Empty;
                    facts[$"{encPrefix}.timestamp"] = enc.Encounter.Timestamp;

                    if (!string.IsNullOrEmpty(perspective.RememberedAs))
                    {
                        facts[$"{encPrefix}.rememberedAs"] = perspective.RememberedAs;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Extract facts from character personality archive.
    /// </summary>
    private static void ExtractPersonalityFacts(ArchiveBundle bundle, Dictionary<string, object> facts)
    {
        if (!bundle.TryGetEntry<CharacterPersonalityArchive>("character-personality", out var personality) || personality == null)
        {
            return;
        }

        var characterId = personality.CharacterId;
        var prefix = $"character.{characterId}";

        // Extract personality traits
        if (personality.HasPersonality && personality.Personality?.Traits != null)
        {
            facts[$"{prefix}.hasPersonality"] = true;
            facts[$"{prefix}.personality.version"] = personality.Personality.Version;

            if (!string.IsNullOrEmpty(personality.Personality.ArchetypeHint))
            {
                facts[$"{prefix}.archetype"] = personality.Personality.ArchetypeHint;
            }

            foreach (var trait in personality.Personality.Traits)
            {
                facts[$"{prefix}.trait.{trait.Axis.ToString().ToLowerInvariant()}"] = (double)trait.Value;
            }
        }
        else
        {
            facts[$"{prefix}.hasPersonality"] = false;
        }

        // Extract combat preferences
        if (personality.HasCombatPreferences && personality.CombatPreferences?.Preferences != null)
        {
            var prefs = personality.CombatPreferences.Preferences;
            var combatPrefix = $"{prefix}.combat";

            facts[$"{combatPrefix}.style"] = prefs.Style.ToString();
            facts[$"{combatPrefix}.preferredRange"] = prefs.PreferredRange.ToString();
            facts[$"{combatPrefix}.groupRole"] = prefs.GroupRole.ToString();
            facts[$"{combatPrefix}.riskTolerance"] = (double)prefs.RiskTolerance;
            facts[$"{combatPrefix}.retreatThreshold"] = (double)prefs.RetreatThreshold;
            facts[$"{combatPrefix}.protectAllies"] = prefs.ProtectAllies;
        }
    }

    private static NarrativeState CreateInitialNarrativeState(SpectrumType primarySpectrum)
    {
        return new NarrativeState
        {
            PrimarySpectrum = primarySpectrum
        };
    }
}
