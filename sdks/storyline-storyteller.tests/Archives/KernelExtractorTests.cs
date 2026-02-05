// Copyright (c) Beyond Immersion. All rights reserved.

using BeyondImmersion.Bannou.StorylineTheory.Archives;
using BeyondImmersion.Bannou.StorylineTheory.Kernels;
using Xunit;

namespace BeyondImmersion.Bannou.StorylineStoryteller.Tests.Archives;

/// <summary>
/// Tests for the KernelExtractor class.
/// </summary>
public class KernelExtractorTests
{
    private readonly KernelExtractor _extractor = new();

    /// <summary>
    /// Empty bundle produces no kernels.
    /// </summary>
    [Fact]
    public void ExtractKernels_EmptyBundle_ReturnsEmptyList()
    {
        // Arrange
        var bundle = new ArchiveBundle();

        // Act
        var kernels = _extractor.ExtractKernels(bundle);

        // Assert
        Assert.Empty(kernels);
    }

    /// <summary>
    /// Dead character produces Death kernel with maximum significance.
    /// </summary>
    [Fact]
    public void ExtractKernels_DeadCharacter_ProducesDeathKernel()
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
            Name = "Test Character",
            RealmId = Guid.NewGuid(),
            SpeciesId = Guid.NewGuid(),
            BirthDate = DateTimeOffset.UtcNow.AddYears(-30),
            DeathDate = DateTimeOffset.UtcNow,
            Status = CharacterStatus.Dead
        });

        // Act
        var kernels = _extractor.ExtractKernels(bundle);

        // Assert
        Assert.Single(kernels);
        var kernel = kernels[0];
        Assert.Equal(KernelType.Death, kernel.Type);
        Assert.Equal(1.0, kernel.Significance);
        Assert.Equal(characterId, kernel.SourceResourceId);
        Assert.Contains(characterId, kernel.InvolvedCharacterIds);
    }

    /// <summary>
    /// Alive character produces no Death kernel.
    /// </summary>
    [Fact]
    public void ExtractKernels_AliveCharacter_NoDeathKernel()
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
            Name = "Test Character",
            RealmId = Guid.NewGuid(),
            SpeciesId = Guid.NewGuid(),
            BirthDate = DateTimeOffset.UtcNow.AddYears(-30),
            DeathDate = default,
            Status = CharacterStatus.Alive
        });

        // Act
        var kernels = _extractor.ExtractKernels(bundle);

        // Assert
        Assert.Empty(kernels);
    }

    /// <summary>
    /// High-significance historical participation produces HistoricalEvent kernel.
    /// </summary>
    [Fact]
    public void ExtractKernels_HighSignificanceEvent_ProducesHistoricalEventKernel()
    {
        // Arrange
        var characterId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
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
                    EventDate = DateTimeOffset.UtcNow.AddYears(-5),
                    Significance = 0.9f,
                    CreatedAt = DateTimeOffset.UtcNow
                }
            ],
            HasBackstory = false
        });

        // Act
        var kernels = _extractor.ExtractKernels(bundle);

        // Assert
        Assert.Single(kernels);
        var kernel = kernels[0];
        Assert.Equal(KernelType.HistoricalEvent, kernel.Type);
        Assert.Equal(0.9, kernel.Significance, 2);
        Assert.Equal(characterId, kernel.SourceResourceId);
    }

    /// <summary>
    /// Low-significance historical participation does not produce kernel.
    /// </summary>
    [Fact]
    public void ExtractKernels_LowSignificanceEvent_NoKernel()
    {
        // Arrange
        var characterId = Guid.NewGuid();
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
                    EventId = Guid.NewGuid(),
                    EventName = "Minor Skirmish",
                    EventCategory = EventCategory.WAR,
                    Role = ParticipationRole.WITNESS,
                    EventDate = DateTimeOffset.UtcNow.AddYears(-5),
                    Significance = 0.3f, // Below 0.7 threshold
                    CreatedAt = DateTimeOffset.UtcNow
                }
            ],
            HasBackstory = false
        });

        // Act
        var kernels = _extractor.ExtractKernels(bundle);

        // Assert
        Assert.Empty(kernels);
    }

    /// <summary>
    /// Trauma backstory element produces Trauma kernel.
    /// </summary>
    [Fact]
    public void ExtractKernels_TraumaBackstory_ProducesTraumaKernel()
    {
        // Arrange
        var characterId = Guid.NewGuid();
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
                        ElementType = BackstoryElementType.TRAUMA,
                        Key = "betrayal",
                        Value = "Betrayed by trusted mentor",
                        Strength = 0.8f
                    }
                ]
            }
        });

        // Act
        var kernels = _extractor.ExtractKernels(bundle);

        // Assert
        Assert.Single(kernels);
        var kernel = kernels[0];
        Assert.Equal(KernelType.Trauma, kernel.Type);
        Assert.Equal(0.8, kernel.Significance, 2);

        var data = Assert.IsType<TraumaKernelData>(kernel.Data);
        Assert.Equal("betrayal", data.TraumaType);
        Assert.Equal("Betrayed by trusted mentor", data.Description);
    }

    /// <summary>
    /// Goal backstory elements produce UnfinishedBusiness kernel.
    /// </summary>
    [Fact]
    public void ExtractKernels_GoalBackstory_ProducesUnfinishedBusinessKernel()
    {
        // Arrange
        var characterId = Guid.NewGuid();
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
                        ElementType = BackstoryElementType.GOAL,
                        Key = "revenge",
                        Value = "Avenge fallen comrades",
                        Strength = 0.9f
                    },
                    new BackstoryElement
                    {
                        ElementType = BackstoryElementType.GOAL,
                        Key = "wealth",
                        Value = "Restore family fortune",
                        Strength = 0.6f
                    }
                ]
            }
        });

        // Act
        var kernels = _extractor.ExtractKernels(bundle);

        // Assert
        Assert.Single(kernels);
        var kernel = kernels[0];
        Assert.Equal(KernelType.UnfinishedBusiness, kernel.Type);
        Assert.Equal(0.9, kernel.Significance, 2); // Highest strength goal

        var data = Assert.IsType<UnfinishedBusinessKernelData>(kernel.Data);
        Assert.Equal(2, data.Goals.Length);
        Assert.Equal("Avenge fallen comrades", data.PrimaryGoal);
    }

    /// <summary>
    /// Negative encounter with high impact intensity produces Conflict kernel.
    /// </summary>
    [Fact]
    public void ExtractKernels_NegativeEncounter_ProducesConflictKernel()
    {
        // Arrange
        var characterId = Guid.NewGuid();
        var otherId = Guid.NewGuid();
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
                        EncounterTypeCode = "COMBAT",
                        Outcome = EncounterOutcome.NEGATIVE,
                        ParticipantIds = [characterId, otherId],
                        CreatedAt = DateTimeOffset.UtcNow
                    },
                    Perspectives =
                    [
                        new EncounterPerspectiveModel
                        {
                            PerspectiveId = Guid.NewGuid(),
                            EncounterId = encounterId,
                            CharacterId = characterId,
                            EmotionalImpact = EmotionalImpact.ANGER,
                            SentimentShift = -0.8f, // Below -0.5 threshold
                            ImpactIntensity = 0.9f, // Above 0.7 threshold
                            MemoryStrength = 0.5f,
                            CreatedAt = DateTimeOffset.UtcNow
                        }
                    ]
                }
            ]
        });

        // Act
        var kernels = _extractor.ExtractKernels(bundle);

        // Assert
        Assert.Single(kernels);
        var kernel = kernels[0];
        Assert.Equal(KernelType.Conflict, kernel.Type);
        Assert.Contains(characterId, kernel.InvolvedCharacterIds);
        Assert.Contains(otherId, kernel.InvolvedCharacterIds);

        var data = Assert.IsType<ConflictKernelData>(kernel.Data);
        Assert.Equal(encounterId, data.EncounterId);
        Assert.Equal(-0.8, data.SentimentImpact, 2);
    }

    /// <summary>
    /// Positive relationship with many encounters produces DeepBond kernel.
    /// </summary>
    [Fact]
    public void ExtractKernels_PositiveRelationship_ProducesDeepBondKernel()
    {
        // Arrange
        var characterId = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        var bundle = new ArchiveBundle();

        // Create 6 encounters with the same character (above 5 threshold)
        var encounters = new List<EncounterResponse>();
        for (var i = 0; i < 6; i++)
        {
            var encId = Guid.NewGuid();
            encounters.Add(new EncounterResponse
            {
                Encounter = new EncounterModel
                {
                    EncounterId = encId,
                    Timestamp = DateTimeOffset.UtcNow.AddDays(-i * 10),
                    RealmId = Guid.NewGuid(),
                    EncounterTypeCode = "TRADE",
                    Outcome = EncounterOutcome.POSITIVE,
                    ParticipantIds = [characterId, otherId],
                    CreatedAt = DateTimeOffset.UtcNow
                },
                Perspectives =
                [
                    new EncounterPerspectiveModel
                    {
                        PerspectiveId = Guid.NewGuid(),
                        EncounterId = encId,
                        CharacterId = characterId,
                        EmotionalImpact = EmotionalImpact.GRATITUDE,
                        SentimentShift = 0.3f,
                        MemoryStrength = 0.5f,
                        CreatedAt = DateTimeOffset.UtcNow
                    }
                ]
            });
        }

        bundle.AddEntry("character-encounter", new CharacterEncounterArchive
        {
            ResourceId = characterId,
            ResourceType = "character-encounter",
            ArchivedAt = DateTimeOffset.UtcNow,
            SchemaVersion = 1,
            CharacterId = characterId,
            HasEncounters = true,
            EncounterCount = 6,
            Encounters = encounters,
            AggregateSentiment = new Dictionary<string, float>
            {
                [otherId.ToString()] = 0.75f // Above 0.6 threshold
            }
        });

        // Act
        var kernels = _extractor.ExtractKernels(bundle);

        // Assert
        Assert.Single(kernels);
        var kernel = kernels[0];
        Assert.Equal(KernelType.DeepBond, kernel.Type);
        Assert.Contains(characterId, kernel.InvolvedCharacterIds);
        Assert.Contains(otherId, kernel.InvolvedCharacterIds);

        var data = Assert.IsType<DeepBondKernelData>(kernel.Data);
        Assert.Equal(otherId, data.BondedCharacterId);
        Assert.Equal(0.75, data.AverageSentiment, 2);
        Assert.Equal(6, data.EncounterCount);
    }

    /// <summary>
    /// Relationship with few encounters does not produce DeepBond kernel.
    /// </summary>
    [Fact]
    public void ExtractKernels_FewEncounters_NoDeepBondKernel()
    {
        // Arrange
        var characterId = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        var bundle = new ArchiveBundle();

        // Create only 3 encounters (below 5 threshold)
        var encounters = new List<EncounterResponse>();
        for (var i = 0; i < 3; i++)
        {
            var encId = Guid.NewGuid();
            encounters.Add(new EncounterResponse
            {
                Encounter = new EncounterModel
                {
                    EncounterId = encId,
                    Timestamp = DateTimeOffset.UtcNow.AddDays(-i * 10),
                    RealmId = Guid.NewGuid(),
                    EncounterTypeCode = "TRADE",
                    Outcome = EncounterOutcome.POSITIVE,
                    ParticipantIds = [characterId, otherId],
                    CreatedAt = DateTimeOffset.UtcNow
                },
                Perspectives =
                [
                    new EncounterPerspectiveModel
                    {
                        PerspectiveId = Guid.NewGuid(),
                        EncounterId = encId,
                        CharacterId = characterId,
                        EmotionalImpact = EmotionalImpact.GRATITUDE,
                        SentimentShift = 0.3f,
                        MemoryStrength = 0.5f,
                        CreatedAt = DateTimeOffset.UtcNow
                    }
                ]
            });
        }

        bundle.AddEntry("character-encounter", new CharacterEncounterArchive
        {
            ResourceId = characterId,
            ResourceType = "character-encounter",
            ArchivedAt = DateTimeOffset.UtcNow,
            SchemaVersion = 1,
            CharacterId = characterId,
            HasEncounters = true,
            EncounterCount = 3,
            Encounters = encounters,
            AggregateSentiment = new Dictionary<string, float>
            {
                [otherId.ToString()] = 0.9f // High sentiment but not enough encounters
            }
        });

        // Act
        var kernels = _extractor.ExtractKernels(bundle);

        // Assert
        Assert.Empty(kernels);
    }

    /// <summary>
    /// Kernels are sorted by significance descending.
    /// </summary>
    [Fact]
    public void ExtractKernels_MultipleKernels_SortedBySignificanceDescending()
    {
        // Arrange
        var characterId = Guid.NewGuid();
        var bundle = new ArchiveBundle();

        // Add a dead character (significance 1.0)
        bundle.AddEntry("character", new CharacterBaseArchive
        {
            ResourceId = characterId,
            ResourceType = "character",
            ArchivedAt = DateTimeOffset.UtcNow,
            SchemaVersion = 1,
            CharacterId = characterId,
            Name = "Test Character",
            RealmId = Guid.NewGuid(),
            SpeciesId = Guid.NewGuid(),
            BirthDate = DateTimeOffset.UtcNow.AddYears(-30),
            DeathDate = DateTimeOffset.UtcNow,
            Status = CharacterStatus.Dead
        });

        // Add history with trauma (significance 0.6)
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
                        ElementType = BackstoryElementType.TRAUMA,
                        Key = "loss",
                        Value = "Lost family home",
                        Strength = 0.6f
                    }
                ]
            }
        });

        // Act
        var kernels = _extractor.ExtractKernels(bundle);

        // Assert
        Assert.Equal(2, kernels.Count);
        Assert.Equal(KernelType.Death, kernels[0].Type);
        Assert.Equal(1.0, kernels[0].Significance);
        Assert.Equal(KernelType.Trauma, kernels[1].Type);
        Assert.Equal(0.6, kernels[1].Significance, 2);
    }

    /// <summary>
    /// Nested archives are processed recursively.
    /// </summary>
    [Fact]
    public void ExtractKernels_NestedArchive_ExtractsFromBoth()
    {
        // Arrange
        var characterId1 = Guid.NewGuid();
        var characterId2 = Guid.NewGuid();

        var nestedBundle = new ArchiveBundle();
        nestedBundle.AddEntry("character", new CharacterBaseArchive
        {
            ResourceId = characterId2,
            ResourceType = "character",
            ArchivedAt = DateTimeOffset.UtcNow,
            SchemaVersion = 1,
            CharacterId = characterId2,
            Name = "Nested Character",
            RealmId = Guid.NewGuid(),
            SpeciesId = Guid.NewGuid(),
            BirthDate = DateTimeOffset.UtcNow.AddYears(-25),
            DeathDate = DateTimeOffset.UtcNow,
            Status = CharacterStatus.Dead
        });

        var bundle = new ArchiveBundle();
        bundle.AddEntry("character", new CharacterBaseArchive
        {
            ResourceId = characterId1,
            ResourceType = "character",
            ArchivedAt = DateTimeOffset.UtcNow,
            SchemaVersion = 1,
            CharacterId = characterId1,
            Name = "Main Character",
            RealmId = Guid.NewGuid(),
            SpeciesId = Guid.NewGuid(),
            BirthDate = DateTimeOffset.UtcNow.AddYears(-30),
            DeathDate = DateTimeOffset.UtcNow,
            Status = CharacterStatus.Dead
        });
        bundle.AddEntry("nested", nestedBundle);

        // Act
        var kernels = _extractor.ExtractKernels(bundle);

        // Assert
        Assert.Equal(2, kernels.Count);
        Assert.All(kernels, k => Assert.Equal(KernelType.Death, k.Type));
        Assert.Contains(kernels, k => k.SourceResourceId == characterId1);
        Assert.Contains(kernels, k => k.SourceResourceId == characterId2);
    }
}
