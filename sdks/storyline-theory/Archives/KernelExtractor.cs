// Copyright (c) Beyond Immersion. All rights reserved.

using BeyondImmersion.Bannou.StorylineTheory.Actants;
using BeyondImmersion.Bannou.StorylineTheory.Arcs;
using BeyondImmersion.Bannou.StorylineTheory.Kernels;

namespace BeyondImmersion.Bannou.StorylineTheory.Archives;

/// <summary>
/// Identifies essential narrative events (kernels) from archive data.
/// Kernels are story opportunities - events that could seed new storylines.
/// </summary>
/// <remarks>
/// <para>
/// The KernelExtractor operates on ArchiveBundle data using opaque string keys.
/// It extracts kernels from known archive types (character, character-history,
/// character-encounter, character-personality) and processes nested archives recursively.
/// </para>
/// <para>
/// Extraction thresholds follow narratology principles:
/// - Death is always maximum significance (1.0) for archived characters
/// - HistoricalEvent kernels require significance > 0.7
/// - Conflict kernels require sentiment &lt; -0.5 and impact > 0.7
/// - DeepBond kernels require sentiment > 0.6 and 5+ encounters
/// </para>
/// </remarks>
public sealed class KernelExtractor
{
    private readonly KernelScorer _scorer = new();

    /// <summary>
    /// Minimum significance threshold for historical event kernels.
    /// </summary>
    private const float HistoricalEventSignificanceThreshold = 0.7f;

    /// <summary>
    /// Minimum negative sentiment for conflict kernels.
    /// </summary>
    private const float ConflictSentimentThreshold = -0.5f;

    /// <summary>
    /// Minimum impact intensity for conflict kernels.
    /// </summary>
    private const float ConflictImpactThreshold = 0.7f;

    /// <summary>
    /// Minimum positive sentiment for deep bond kernels.
    /// </summary>
    private const float DeepBondSentimentThreshold = 0.6f;

    /// <summary>
    /// Minimum encounters required for deep bond kernels.
    /// </summary>
    private const int DeepBondMinEncounters = 5;

    /// <summary>
    /// Extract narrative kernels from an archive bundle.
    /// Returns story opportunities ordered by significance.
    /// </summary>
    /// <param name="bundle">The archive bundle containing data entries.</param>
    /// <returns>List of narrative kernels sorted by significance (descending).</returns>
    public List<NarrativeKernel> ExtractKernels(ArchiveBundle bundle)
    {
        var kernels = new List<NarrativeKernel>();

        // Extract from each archive type the SDK knows about
        ExtractFromCharacterArchive(bundle, kernels);
        ExtractFromHistoryArchive(bundle, kernels);
        ExtractFromEncounterArchive(bundle, kernels);
        ExtractFromPersonalityArchive(bundle, kernels);

        // Process nested archives recursively
        foreach (var nested in bundle.GetNestedArchives())
        {
            kernels.AddRange(ExtractKernels(nested));
        }

        // Score and adjust significance based on cross-kernel analysis
        foreach (var kernel in kernels)
        {
            _scorer.AdjustSignificance(kernel);
        }

        return kernels
            .OrderByDescending(k => k.Significance)
            .ToList();
    }

    /// <summary>
    /// Extract Death kernels from character archive data.
    /// Death is always maximum significance for archived characters.
    /// </summary>
    private void ExtractFromCharacterArchive(ArchiveBundle bundle, List<NarrativeKernel> kernels)
    {
        if (!bundle.TryGetEntry<CharacterBaseArchive>("character", out var character) || character == null)
        {
            return;
        }

        // Death kernel - archived characters are always dead
        if (character.Status == CharacterStatus.Dead)
        {
            kernels.Add(new NarrativeKernel
            {
                KernelId = Guid.NewGuid(),
                Type = KernelType.Death,
                Significance = 1.0, // Death is always maximum significance
                SourceResourceId = character.CharacterId,
                SourceResourceType = "character",
                InvolvedCharacterIds = [character.CharacterId],
                SuggestedActants = null, // Death doesn't suggest actants directly
                CompatibleArcs = [ArcType.Tragedy, ArcType.ManInHole, ArcType.Oedipus],
                GenreAffinities = new Dictionary<string, double>
                {
                    ["action"] = 0.8,
                    ["horror"] = 0.9,
                    ["crime"] = 0.85,
                    ["war"] = 0.9
                },
                Data = new DeathKernelData
                {
                    CauseOfDeath = null, // CharacterBaseArchive doesn't include cause
                    LocationId = null, // Not available in base archive
                    DeathDate = character.DeathDate,
                    KillerId = null // Would need additional data source
                }
            });
        }
    }

    /// <summary>
    /// Extract HistoricalEvent, Trauma, and UnfinishedBusiness kernels from history archive.
    /// </summary>
    private void ExtractFromHistoryArchive(ArchiveBundle bundle, List<NarrativeKernel> kernels)
    {
        if (!bundle.TryGetEntry<CharacterHistoryArchive>("character-history", out var history) || history == null)
        {
            return;
        }

        var sourceId = history.CharacterId;

        // High-significance historical event participations
        if (history.HasParticipations && history.Participations != null)
        {
            foreach (var p in history.Participations.Where(p => p.Significance > HistoricalEventSignificanceThreshold))
            {
                kernels.Add(new NarrativeKernel
                {
                    KernelId = Guid.NewGuid(),
                    Type = KernelType.HistoricalEvent,
                    Significance = p.Significance,
                    SourceResourceId = sourceId,
                    SourceResourceType = "character",
                    InvolvedCharacterIds = [sourceId],
                    SuggestedActants = null,
                    CompatibleArcs = GetArcsForHistoricalEvent(p.EventCategory),
                    GenreAffinities = GetGenreAffinitiesForEvent(p.EventCategory),
                    Data = new HistoricalEventKernelData
                    {
                        EventCode = p.EventName,
                        Role = p.Role.ToString(),
                        EventId = p.EventId
                    }
                });
            }
        }

        // Trauma and UnfinishedBusiness from backstory
        if (history.HasBackstory && history.Backstory?.Elements != null)
        {
            // Trauma kernels
            foreach (var trauma in history.Backstory.Elements.Where(b => b.ElementType == BackstoryElementType.TRAUMA))
            {
                kernels.Add(new NarrativeKernel
                {
                    KernelId = Guid.NewGuid(),
                    Type = KernelType.Trauma,
                    Significance = trauma.Strength,
                    SourceResourceId = sourceId,
                    SourceResourceType = "character",
                    InvolvedCharacterIds = trauma.RelatedEntityId.HasValue && trauma.RelatedEntityType == "character"
                        ? [sourceId, trauma.RelatedEntityId.Value]
                        : [sourceId],
                    SuggestedActants = trauma.RelatedEntityId.HasValue && trauma.RelatedEntityType == "character"
                        ? new Dictionary<Guid, ActantRole>
                        {
                            [sourceId] = ActantRole.Subject,
                            [trauma.RelatedEntityId.Value] = ActantRole.Opponent
                        }
                        : null,
                    CompatibleArcs = [ArcType.ManInHole, ArcType.Cinderella, ArcType.RagsToRiches],
                    GenreAffinities = new Dictionary<string, double>
                    {
                        ["morality"] = 0.9,
                        ["worldview"] = 0.8,
                        ["love"] = 0.6
                    },
                    Data = new TraumaKernelData
                    {
                        TraumaType = trauma.Key,
                        Description = trauma.Value,
                        PerpetratorId = trauma.RelatedEntityType == "character" ? trauma.RelatedEntityId : null
                    }
                });
            }

            // UnfinishedBusiness from goal elements
            var goalElements = history.Backstory.Elements
                .Where(b => b.ElementType == BackstoryElementType.GOAL)
                .ToList();

            if (goalElements.Count > 0)
            {
                var goals = goalElements.Select(g => g.Value).ToArray();
                var primaryGoal = goalElements
                    .OrderByDescending(g => g.Strength)
                    .FirstOrDefault();

                kernels.Add(new NarrativeKernel
                {
                    KernelId = Guid.NewGuid(),
                    Type = KernelType.UnfinishedBusiness,
                    Significance = primaryGoal?.Strength ?? 0.75,
                    SourceResourceId = sourceId,
                    SourceResourceType = "character",
                    InvolvedCharacterIds = [sourceId],
                    SuggestedActants = null,
                    CompatibleArcs = [ArcType.ManInHole, ArcType.Cinderella, ArcType.Icarus],
                    GenreAffinities = new Dictionary<string, double>
                    {
                        ["action"] = 0.7,
                        ["crime"] = 0.7,
                        ["status"] = 0.8
                    },
                    Data = new UnfinishedBusinessKernelData
                    {
                        Goals = goals,
                        PrimaryGoal = primaryGoal?.Value
                    }
                });
            }
        }
    }

    /// <summary>
    /// Extract Conflict and DeepBond kernels from encounter archive.
    /// </summary>
    private void ExtractFromEncounterArchive(ArchiveBundle bundle, List<NarrativeKernel> kernels)
    {
        if (!bundle.TryGetEntry<CharacterEncounterArchive>("character-encounter", out var encounters) || encounters == null)
        {
            return;
        }

        var sourceId = encounters.CharacterId;

        if (!encounters.HasEncounters || encounters.Encounters == null)
        {
            return;
        }

        // Group encounters by other participant and calculate aggregate sentiment
        var encountersByParticipant = new Dictionary<Guid, List<EncounterResponse>>();

        foreach (var enc in encounters.Encounters)
        {
            foreach (var participantId in enc.Encounter.ParticipantIds.Where(p => p != sourceId))
            {
                if (!encountersByParticipant.ContainsKey(participantId))
                {
                    encountersByParticipant[participantId] = [];
                }
                encountersByParticipant[participantId].Add(enc);
            }
        }

        // Extract Conflict kernels from high-impact negative encounters
        foreach (var enc in encounters.Encounters)
        {
            // Find this character's perspective
            var perspective = enc.Perspectives
                .FirstOrDefault(p => p.CharacterId == sourceId);

            if (perspective == null)
            {
                continue;
            }

            var sentiment = perspective.SentimentShift ?? 0;
            var impactIntensity = perspective.ImpactIntensity;

            // Conflict: negative sentiment and high impact intensity
            if (sentiment < ConflictSentimentThreshold && impactIntensity > ConflictImpactThreshold)
            {
                var otherIds = enc.Encounter.ParticipantIds
                    .Where(p => p != sourceId)
                    .ToArray();

                kernels.Add(new NarrativeKernel
                {
                    KernelId = Guid.NewGuid(),
                    Type = KernelType.Conflict,
                    Significance = 0.85,
                    SourceResourceId = sourceId,
                    SourceResourceType = "character",
                    InvolvedCharacterIds = [sourceId, .. otherIds],
                    SuggestedActants = otherIds.Length > 0
                        ? new Dictionary<Guid, ActantRole>
                        {
                            [sourceId] = ActantRole.Subject,
                            [otherIds[0]] = ActantRole.Opponent
                        }
                        : null,
                    CompatibleArcs = [ArcType.ManInHole, ArcType.Tragedy, ArcType.Oedipus],
                    GenreAffinities = new Dictionary<string, double>
                    {
                        ["action"] = 0.9,
                        ["crime"] = 0.85,
                        ["thriller"] = 0.9,
                        ["western"] = 0.8
                    },
                    Data = new ConflictKernelData
                    {
                        EncounterId = enc.Encounter.EncounterId,
                        OtherCharacterIds = otherIds,
                        SentimentImpact = sentiment,
                        ConflictType = enc.Encounter.EncounterTypeCode
                    }
                });
            }
        }

        // Extract DeepBond kernels from positive relationships with many encounters
        if (encounters.AggregateSentiment != null)
        {
            foreach (var (otherIdStr, avgSentiment) in encounters.AggregateSentiment)
            {
                if (!Guid.TryParse(otherIdStr, out var otherId))
                {
                    continue;
                }

                if (avgSentiment <= DeepBondSentimentThreshold)
                {
                    continue;
                }

                if (!encountersByParticipant.TryGetValue(otherId, out var encList))
                {
                    continue;
                }

                var encounterCount = encList.Count;
                if (encounterCount < DeepBondMinEncounters)
                {
                    continue;
                }

                kernels.Add(new NarrativeKernel
                {
                    KernelId = Guid.NewGuid(),
                    Type = KernelType.DeepBond,
                    Significance = 0.7,
                    SourceResourceId = sourceId,
                    SourceResourceType = "character",
                    InvolvedCharacterIds = [sourceId, otherId],
                    SuggestedActants = new Dictionary<Guid, ActantRole>
                    {
                        [sourceId] = ActantRole.Subject,
                        [otherId] = ActantRole.Helper
                    },
                    CompatibleArcs = [ArcType.RagsToRiches, ArcType.Cinderella, ArcType.ManInHole],
                    GenreAffinities = new Dictionary<string, double>
                    {
                        ["love"] = 0.9,
                        ["morality"] = 0.8,
                        ["war"] = 0.7,
                        ["status"] = 0.6
                    },
                    Data = new DeepBondKernelData
                    {
                        BondedCharacterId = otherId,
                        AverageSentiment = avgSentiment,
                        EncounterCount = encounterCount,
                        RelationshipType = null // Would need Relationship service data
                    }
                });
            }
        }
    }

    /// <summary>
    /// Personality archive doesn't directly produce kernels but can adjust scoring.
    /// </summary>
    private void ExtractFromPersonalityArchive(ArchiveBundle bundle, List<NarrativeKernel> kernels)
    {
        // Personality traits are used by KernelScorer to adjust significance
        // based on personality-kernel affinity. For example:
        // - High aggression trait boosts Conflict kernel significance
        // - High agreeableness trait boosts DeepBond kernel significance
        //
        // The scorer retrieves personality from the bundle during AdjustSignificance().
    }

    /// <summary>
    /// Get compatible arcs based on historical event category.
    /// </summary>
    private static ArcType[] GetArcsForHistoricalEvent(EventCategory category)
    {
        return category switch
        {
            EventCategory.WAR => [ArcType.ManInHole, ArcType.Tragedy, ArcType.Oedipus],
            EventCategory.NATURAL_DISASTER => [ArcType.ManInHole, ArcType.Tragedy],
            EventCategory.POLITICAL => [ArcType.Icarus, ArcType.Oedipus, ArcType.ManInHole],
            EventCategory.PERSONAL => [ArcType.Cinderella, ArcType.ManInHole, ArcType.RagsToRiches],
            _ => [ArcType.ManInHole, ArcType.Cinderella]
        };
    }

    /// <summary>
    /// Get genre affinities based on historical event category.
    /// </summary>
    private static Dictionary<string, double> GetGenreAffinitiesForEvent(EventCategory category)
    {
        return category switch
        {
            EventCategory.WAR => new Dictionary<string, double>
            {
                ["war"] = 1.0,
                ["action"] = 0.9,
                ["thriller"] = 0.7
            },
            EventCategory.NATURAL_DISASTER => new Dictionary<string, double>
            {
                ["thriller"] = 0.9,
                ["horror"] = 0.7,
                ["worldview"] = 0.8
            },
            EventCategory.POLITICAL => new Dictionary<string, double>
            {
                ["thriller"] = 0.9,
                ["crime"] = 0.8,
                ["status"] = 0.9
            },
            EventCategory.ECONOMIC => new Dictionary<string, double>
            {
                ["status"] = 0.9,
                ["crime"] = 0.7,
                ["thriller"] = 0.6
            },
            EventCategory.RELIGIOUS => new Dictionary<string, double>
            {
                ["worldview"] = 1.0,
                ["horror"] = 0.6,
                ["morality"] = 0.8
            },
            EventCategory.CULTURAL => new Dictionary<string, double>
            {
                ["worldview"] = 0.9,
                ["status"] = 0.7,
                ["love"] = 0.6
            },
            EventCategory.PERSONAL => new Dictionary<string, double>
            {
                ["love"] = 0.8,
                ["morality"] = 0.9,
                ["status"] = 0.7
            },
            _ => new Dictionary<string, double>
            {
                ["action"] = 0.5,
                ["thriller"] = 0.5
            }
        };
    }
}
