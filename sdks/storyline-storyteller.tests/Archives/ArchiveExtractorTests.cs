// Copyright (c) Beyond Immersion. All rights reserved.

using BeyondImmersion.Bannou.StorylineTheory.Archives;
using BeyondImmersion.Bannou.StorylineTheory.Spectrums;
using Xunit;

namespace BeyondImmersion.Bannou.StorylineStoryteller.Tests.Archives;

/// <summary>
/// Tests for the ArchiveExtractor class.
/// </summary>
public class ArchiveExtractorTests
{
    private readonly ArchiveExtractor _extractor = new();

    /// <summary>
    /// Empty bundle produces WorldState with empty facts.
    /// </summary>
    [Fact]
    public void ExtractWorldState_EmptyBundle_ReturnsEmptyFacts()
    {
        // Arrange
        var bundle = new ArchiveBundle();

        // Act
        var state = _extractor.ExtractWorldState(bundle, SpectrumType.JusticeInjustice);

        // Assert
        Assert.NotNull(state);
        Assert.Empty(state.Facts);
        Assert.Equal(SpectrumType.JusticeInjustice, state.NarrativeState.PrimarySpectrum);
        Assert.Equal(0.0, state.Position);
    }

    /// <summary>
    /// Character archive produces character facts.
    /// </summary>
    [Fact]
    public void ExtractWorldState_CharacterArchive_ProducesCharacterFacts()
    {
        // Arrange
        var characterId = Guid.NewGuid();
        var realmId = Guid.NewGuid();
        var speciesId = Guid.NewGuid();
        var birthDate = DateTimeOffset.UtcNow.AddYears(-30);
        var deathDate = DateTimeOffset.UtcNow;

        var bundle = new ArchiveBundle();
        bundle.AddEntry("character", new CharacterBaseArchive
        {
            ResourceId = characterId,
            ResourceType = "character",
            ArchivedAt = DateTimeOffset.UtcNow,
            SchemaVersion = 1,
            CharacterId = characterId,
            Name = "Test Hero",
            RealmId = realmId,
            SpeciesId = speciesId,
            BirthDate = birthDate,
            DeathDate = deathDate,
            Status = CharacterStatus.Dead,
            FamilySummary = "Father of three"
        });

        // Act
        var state = _extractor.ExtractWorldState(bundle, SpectrumType.LifeDeath);

        // Assert
        var prefix = $"character.{characterId}";
        Assert.Equal("dead", state.Facts[$"{prefix}.status"]);
        Assert.Equal(realmId, state.Facts[$"{prefix}.realm"]);
        Assert.Equal(speciesId, state.Facts[$"{prefix}.species"]);
        Assert.Equal(birthDate, state.Facts[$"{prefix}.birthDate"]);
        Assert.Equal(deathDate, state.Facts[$"{prefix}.deathDate"]);
        Assert.Equal("Test Hero", state.Facts[$"{prefix}.name"]);
        Assert.Equal("Father of three", state.Facts[$"{prefix}.familySummary"]);
    }

    /// <summary>
    /// History archive produces participation facts.
    /// </summary>
    [Fact]
    public void ExtractWorldState_HistoryArchive_ProducesParticipationFacts()
    {
        // Arrange
        var characterId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var eventDate = DateTimeOffset.UtcNow.AddYears(-5);

        var bundle = new ArchiveBundle();
        bundle.AddEntry("character-history", new CharacterHistoryArchive
        {
            ResourceId = characterId,
            ResourceType = "character-history",
            ArchivedAt = DateTimeOffset.UtcNow,
            SchemaVersion = 1,
            CharacterId = characterId,
            HasParticipations = true,
            Participations =
            [
                new HistoricalParticipation
                {
                    ParticipationId = Guid.NewGuid(),
                    CharacterId = characterId,
                    EventId = eventId,
                    EventName = "Battle of Stormgate",
                    EventCategory = EventCategory.WAR,
                    Role = ParticipationRole.HERO,
                    EventDate = eventDate,
                    Significance = 0.9f,
                    CreatedAt = DateTimeOffset.UtcNow
                }
            ],
            HasBackstory = false
        });

        // Act
        var state = _extractor.ExtractWorldState(bundle, SpectrumType.LifeDeath);

        // Assert
        var prefix = $"character.{characterId}";
        Assert.Equal(1, state.Facts[$"{prefix}.participationCount"]);

        var eventPrefix = $"history.{characterId}.event.{eventId}";
        Assert.Equal("Battle of Stormgate", state.Facts[$"{eventPrefix}.name"]);
        Assert.Equal("WAR", state.Facts[$"{eventPrefix}.category"]);
        Assert.Equal("HERO", state.Facts[$"{eventPrefix}.role"]);
        Assert.Equal(0.9, (double)state.Facts[$"{eventPrefix}.significance"], 5);
        Assert.Equal(eventDate, state.Facts[$"{eventPrefix}.date"]);
    }

    /// <summary>
    /// History archive produces backstory facts.
    /// </summary>
    [Fact]
    public void ExtractWorldState_HistoryArchive_ProducesBackstoryFacts()
    {
        // Arrange
        var characterId = Guid.NewGuid();
        var relatedEntityId = Guid.NewGuid();

        var bundle = new ArchiveBundle();
        bundle.AddEntry("character-history", new CharacterHistoryArchive
        {
            ResourceId = characterId,
            ResourceType = "character-history",
            ArchivedAt = DateTimeOffset.UtcNow,
            SchemaVersion = 1,
            CharacterId = characterId,
            HasParticipations = false,
            HasBackstory = true,
            Backstory = new BackstoryResponse
            {
                CharacterId = characterId,
                Elements =
                [
                    new BackstoryElement
                    {
                        ElementType = BackstoryElementType.ORIGIN,
                        Key = "homeland",
                        Value = "Northlands",
                        Strength = 0.7f
                    },
                    new BackstoryElement
                    {
                        ElementType = BackstoryElementType.TRAINING,
                        Key = "mentor",
                        Value = "Knights Guild",
                        Strength = 0.8f,
                        RelatedEntityId = relatedEntityId,
                        RelatedEntityType = "organization"
                    }
                ]
            }
        });

        // Act
        var state = _extractor.ExtractWorldState(bundle, SpectrumType.LifeDeath);

        // Assert
        var prefix = $"character.{characterId}.backstory";

        Assert.Equal("Northlands", state.Facts[$"{prefix}.origin.homeland.value"]);
        Assert.Equal(0.7, (double)state.Facts[$"{prefix}.origin.homeland.strength"], 5);

        Assert.Equal("Knights Guild", state.Facts[$"{prefix}.training.mentor.value"]);
        Assert.Equal(0.8, (double)state.Facts[$"{prefix}.training.mentor.strength"], 5);
        Assert.Equal(relatedEntityId, state.Facts[$"{prefix}.training.mentor.relatedEntity"]);
        Assert.Equal("organization", state.Facts[$"{prefix}.training.mentor.relatedEntityType"]);
    }

    /// <summary>
    /// Encounter archive produces relationship sentiment facts.
    /// </summary>
    [Fact]
    public void ExtractWorldState_EncounterArchive_ProducesRelationshipFacts()
    {
        // Arrange
        var characterId = Guid.NewGuid();
        var otherId = Guid.NewGuid();

        var bundle = new ArchiveBundle();
        bundle.AddEntry("character-encounter", new CharacterEncounterArchive
        {
            ResourceId = characterId,
            ResourceType = "character-encounter",
            ArchivedAt = DateTimeOffset.UtcNow,
            SchemaVersion = 1,
            CharacterId = characterId,
            HasEncounters = true,
            EncounterCount = 5,
            Encounters = [],
            AggregateSentiment = new Dictionary<string, float>
            {
                [otherId.ToString()] = 0.75f
            }
        });

        // Act
        var state = _extractor.ExtractWorldState(bundle, SpectrumType.LifeDeath);

        // Assert
        var prefix = $"character.{characterId}";
        Assert.Equal(true, state.Facts[$"{prefix}.hasEncounters"]);
        Assert.Equal(5, state.Facts[$"{prefix}.encounterCount"]);
        Assert.Equal(0.75, (double)state.Facts[$"{prefix}.relationship.{otherId}.sentiment"], 5);
    }

    /// <summary>
    /// Encounter archive produces significant encounter facts.
    /// </summary>
    [Fact]
    public void ExtractWorldState_EncounterArchive_ProducesEncounterFacts()
    {
        // Arrange
        var characterId = Guid.NewGuid();
        var encounterId = Guid.NewGuid();
        var locationId = Guid.NewGuid();
        var timestamp = DateTimeOffset.UtcNow.AddDays(-10);

        var bundle = new ArchiveBundle();
        bundle.AddEntry("character-encounter", new CharacterEncounterArchive
        {
            ResourceId = characterId,
            ResourceType = "character-encounter",
            ArchivedAt = DateTimeOffset.UtcNow,
            SchemaVersion = 1,
            CharacterId = characterId,
            HasEncounters = true,
            EncounterCount = 1,
            Encounters =
            [
                new EncounterResponse
                {
                    Encounter = new EncounterModel
                    {
                        EncounterId = encounterId,
                        Timestamp = timestamp,
                        RealmId = Guid.NewGuid(),
                        LocationId = locationId,
                        EncounterTypeCode = "COMBAT",
                        Outcome = EncounterOutcome.MEMORABLE,
                        ParticipantIds = [characterId, Guid.NewGuid()],
                        CreatedAt = DateTimeOffset.UtcNow
                    },
                    Perspectives =
                    [
                        new EncounterPerspectiveModel
                        {
                            PerspectiveId = Guid.NewGuid(),
                            EncounterId = encounterId,
                            CharacterId = characterId,
                            EmotionalImpact = EmotionalImpact.PRIDE,
                            SentimentShift = 0.2f,
                            MemoryStrength = 0.8f, // Above 0.5 threshold
                            RememberedAs = "The day I proved myself",
                            CreatedAt = DateTimeOffset.UtcNow
                        }
                    ]
                }
            ]
        });

        // Act
        var state = _extractor.ExtractWorldState(bundle, SpectrumType.LifeDeath);

        // Assert
        var encPrefix = $"character.{characterId}.encounter.{encounterId}";
        Assert.Equal("COMBAT", state.Facts[$"{encPrefix}.type"]);
        Assert.Equal("MEMORABLE", state.Facts[$"{encPrefix}.outcome"]);
        Assert.Equal(0.2, (double)state.Facts[$"{encPrefix}.sentiment"], 5);
        Assert.Equal(0.8, (double)state.Facts[$"{encPrefix}.memoryStrength"], 5);
        Assert.Equal("PRIDE", state.Facts[$"{encPrefix}.emotionalImpact"]);
        Assert.Equal(locationId, state.Facts[$"{encPrefix}.location"]);
        Assert.Equal(timestamp, state.Facts[$"{encPrefix}.timestamp"]);
        Assert.Equal("The day I proved myself", state.Facts[$"{encPrefix}.rememberedAs"]);
    }

    /// <summary>
    /// Weak memory encounters are not included in facts.
    /// </summary>
    [Fact]
    public void ExtractWorldState_WeakMemoryEncounter_NotIncluded()
    {
        // Arrange
        var characterId = Guid.NewGuid();
        var encounterId = Guid.NewGuid();

        var bundle = new ArchiveBundle();
        bundle.AddEntry("character-encounter", new CharacterEncounterArchive
        {
            ResourceId = characterId,
            ResourceType = "character-encounter",
            ArchivedAt = DateTimeOffset.UtcNow,
            SchemaVersion = 1,
            CharacterId = characterId,
            HasEncounters = true,
            EncounterCount = 1,
            Encounters =
            [
                new EncounterResponse
                {
                    Encounter = new EncounterModel
                    {
                        EncounterId = encounterId,
                        Timestamp = DateTimeOffset.UtcNow.AddDays(-30),
                        RealmId = Guid.NewGuid(),
                        EncounterTypeCode = "TRADE",
                        Outcome = EncounterOutcome.NEUTRAL,
                        ParticipantIds = [characterId, Guid.NewGuid()],
                        CreatedAt = DateTimeOffset.UtcNow
                    },
                    Perspectives =
                    [
                        new EncounterPerspectiveModel
                        {
                            PerspectiveId = Guid.NewGuid(),
                            EncounterId = encounterId,
                            CharacterId = characterId,
                            EmotionalImpact = EmotionalImpact.INDIFFERENCE,
                            SentimentShift = 0.0f,
                            MemoryStrength = 0.3f, // Below 0.5 threshold
                            CreatedAt = DateTimeOffset.UtcNow
                        }
                    ]
                }
            ]
        });

        // Act
        var state = _extractor.ExtractWorldState(bundle, SpectrumType.LifeDeath);

        // Assert
        var encPrefix = $"character.{characterId}.encounter.{encounterId}";
        Assert.False(state.Facts.ContainsKey($"{encPrefix}.type"));
    }

    /// <summary>
    /// Personality archive produces trait facts.
    /// </summary>
    [Fact]
    public void ExtractWorldState_PersonalityArchive_ProducesTraitFacts()
    {
        // Arrange
        var characterId = Guid.NewGuid();

        var bundle = new ArchiveBundle();
        bundle.AddEntry("character-personality", new CharacterPersonalityArchive
        {
            ResourceId = characterId,
            ResourceType = "character-personality",
            ArchivedAt = DateTimeOffset.UtcNow,
            SchemaVersion = 1,
            CharacterId = characterId,
            HasPersonality = true,
            Personality = new PersonalityResponse
            {
                CharacterId = characterId,
                Traits =
                [
                    new TraitValue { Axis = TraitAxis.AGGRESSION, Value = 0.6f },
                    new TraitValue { Axis = TraitAxis.LOYALTY, Value = 0.9f },
                    new TraitValue { Axis = TraitAxis.HONESTY, Value = -0.3f }
                ],
                Version = 2,
                ArchetypeHint = "warrior",
                CreatedAt = DateTimeOffset.UtcNow
            },
            HasCombatPreferences = false
        });

        // Act
        var state = _extractor.ExtractWorldState(bundle, SpectrumType.LifeDeath);

        // Assert
        var prefix = $"character.{characterId}";
        Assert.Equal(true, state.Facts[$"{prefix}.hasPersonality"]);
        Assert.Equal(2, state.Facts[$"{prefix}.personality.version"]);
        Assert.Equal("warrior", state.Facts[$"{prefix}.archetype"]);
        Assert.Equal(0.6, (double)state.Facts[$"{prefix}.trait.aggression"], 5);
        Assert.Equal(0.9, (double)state.Facts[$"{prefix}.trait.loyalty"], 5);
        Assert.Equal(-0.3, (double)state.Facts[$"{prefix}.trait.honesty"], 5);
    }

    /// <summary>
    /// Personality archive produces combat preference facts.
    /// </summary>
    [Fact]
    public void ExtractWorldState_PersonalityArchive_ProducesCombatFacts()
    {
        // Arrange
        var characterId = Guid.NewGuid();

        var bundle = new ArchiveBundle();
        bundle.AddEntry("character-personality", new CharacterPersonalityArchive
        {
            ResourceId = characterId,
            ResourceType = "character-personality",
            ArchivedAt = DateTimeOffset.UtcNow,
            SchemaVersion = 1,
            CharacterId = characterId,
            HasPersonality = false,
            HasCombatPreferences = true,
            CombatPreferences = new CombatPreferencesResponse
            {
                CharacterId = characterId,
                Preferences = new CombatPreferences
                {
                    Style = CombatStyle.AGGRESSIVE,
                    PreferredRange = PreferredRange.MELEE,
                    GroupRole = GroupRole.FRONTLINE,
                    RiskTolerance = 0.8f,
                    RetreatThreshold = 0.2f,
                    ProtectAllies = true
                },
                Version = 1,
                CreatedAt = DateTimeOffset.UtcNow
            }
        });

        // Act
        var state = _extractor.ExtractWorldState(bundle, SpectrumType.LifeDeath);

        // Assert
        var prefix = $"character.{characterId}";
        Assert.Equal(false, state.Facts[$"{prefix}.hasPersonality"]);

        var combatPrefix = $"{prefix}.combat";
        Assert.Equal("AGGRESSIVE", state.Facts[$"{combatPrefix}.style"]);
        Assert.Equal("MELEE", state.Facts[$"{combatPrefix}.preferredRange"]);
        Assert.Equal("FRONTLINE", state.Facts[$"{combatPrefix}.groupRole"]);
        Assert.Equal(0.8, (double)state.Facts[$"{combatPrefix}.riskTolerance"], 5);
        Assert.Equal(0.2, (double)state.Facts[$"{combatPrefix}.retreatThreshold"], 5);
        Assert.Equal(true, state.Facts[$"{combatPrefix}.protectAllies"]);
    }

    /// <summary>
    /// Multiple archives combine into single WorldState.
    /// </summary>
    [Fact]
    public void ExtractWorldState_MultipleArchives_CombinesFacts()
    {
        // Arrange
        var characterId = Guid.NewGuid();
        var bundle = new ArchiveBundle();

        bundle.AddEntry("character", new CharacterBaseArchive
        {
            ResourceId = characterId,
            ResourceType = "character",
            ArchivedAt = DateTimeOffset.UtcNow,
            SchemaVersion = 1,
            CharacterId = characterId,
            Name = "Test Hero",
            RealmId = Guid.NewGuid(),
            SpeciesId = Guid.NewGuid(),
            BirthDate = DateTimeOffset.UtcNow.AddYears(-30),
            DeathDate = DateTimeOffset.UtcNow,
            Status = CharacterStatus.Dead
        });

        bundle.AddEntry("character-personality", new CharacterPersonalityArchive
        {
            ResourceId = characterId,
            ResourceType = "character-personality",
            ArchivedAt = DateTimeOffset.UtcNow,
            SchemaVersion = 1,
            CharacterId = characterId,
            HasPersonality = true,
            Personality = new PersonalityResponse
            {
                CharacterId = characterId,
                Traits = [new TraitValue { Axis = TraitAxis.AGGRESSION, Value = 0.5f }],
                Version = 1,
                CreatedAt = DateTimeOffset.UtcNow
            },
            HasCombatPreferences = false
        });

        // Act
        var state = _extractor.ExtractWorldState(bundle, SpectrumType.LifeDeath);

        // Assert
        var prefix = $"character.{characterId}";
        Assert.Equal("dead", state.Facts[$"{prefix}.status"]);
        Assert.Equal("Test Hero", state.Facts[$"{prefix}.name"]);
        Assert.Equal(true, state.Facts[$"{prefix}.hasPersonality"]);
        Assert.Equal(0.5, (double)state.Facts[$"{prefix}.trait.aggression"], 5);
    }
}
