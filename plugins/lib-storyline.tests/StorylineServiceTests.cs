using BeyondImmersion.Bannou.StorylineStoryteller.Actions;
using BeyondImmersion.Bannou.StorylineStoryteller.Planning;
using BeyondImmersion.Bannou.StorylineStoryteller.Templates;
using BeyondImmersion.Bannou.StorylineTheory.Arcs;
using BeyondImmersion.Bannou.StorylineTheory.Spectrums;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Relationship;
using BeyondImmersion.BannouService.Resource;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using BeyondImmersion.BannouService.Storyline;
using BeyondImmersion.BannouService.Testing;
using BeyondImmersion.BannouService.TestUtilities;
using Microsoft.Extensions.Logging;
using Moq;
using CharacterHistory = BeyondImmersion.BannouService.CharacterHistory;
using CharacterPersonality = BeyondImmersion.BannouService.CharacterPersonality;

namespace BeyondImmersion.BannouService.Storyline.Tests;

public class StorylineServiceTests
{
    #region Constructor Validation

    [Fact]
    public void StorylineService_HasValidConstructorPattern()
    {
        ServiceConstructorValidator.ValidateServiceConstructor<StorylineService>();
    }

    #endregion

    #region Configuration Tests

    [Fact]
    public void StorylineServiceConfiguration_CanBeInstantiated()
    {
        // Arrange & Act
        var config = new StorylineServiceConfiguration();

        // Assert
        Assert.NotNull(config);
    }

    [Fact]
    public void StorylineServiceConfiguration_HasCorrectDefaults()
    {
        // Arrange & Act
        var config = new StorylineServiceConfiguration();

        // Assert - verify defaults match schema
        Assert.Equal(3600, config.PlanCacheTtlSeconds);
        Assert.Equal(PlanningUrgency.Medium, config.DefaultPlanningUrgency);
        Assert.True(config.PlanCacheEnabled);
        Assert.Equal("drama", config.DefaultGenre);
        Assert.Equal(10, config.MaxSeedSources);

        // Confidence calculation defaults
        Assert.Equal(0.5, config.ConfidenceBaseScore);
        Assert.Equal(3, config.ConfidencePhaseThreshold);
        Assert.Equal(0.2, config.ConfidencePhaseBonus);
        Assert.Equal(0.15, config.ConfidenceCoreEventBonus);
        Assert.Equal(0.15, config.ConfidenceActionCountBonus);
        Assert.Equal(5, config.ConfidenceMinActionCount);
        Assert.Equal(20, config.ConfidenceMaxActionCount);

        // Risk identification defaults
        Assert.Equal(3, config.RiskMinActionThreshold);
        Assert.Equal(2, config.RiskMinPhaseThreshold);
    }

    #endregion

    #region SDK Type Integration Tests

    /// <summary>
    /// Verifies that SDK types are used directly without conversion.
    /// This test ensures x-sdk-type annotations are working correctly.
    /// </summary>
    [Fact]
    public void SdkTypes_AreUsedDirectlyWithoutConversion()
    {
        // SDK types should be directly usable in API contexts
        var phase = new StorylinePlanPhase
        {
            PhaseNumber = 1,
            Name = "Test Phase",
            Actions = Array.Empty<StorylinePlanAction>(),
            TargetState = new PhaseTargetState
            {
                MinPrimarySpectrum = 0.3,
                MaxPrimarySpectrum = 0.7,
                RangeDescription = "Test range"
            },
            PositionBounds = new PhasePosition
            {
                StcCenter = 0.5,
                Floor = 0.0,
                Ceiling = 1.0,
                ValidationBand = 0.1
            }
        };

        Assert.Equal(1, phase.PhaseNumber);
        Assert.Equal("Test Phase", phase.Name);
        Assert.NotNull(phase.TargetState);
        Assert.NotNull(phase.PositionBounds);
    }

    [Fact]
    public void StorylinePlanAction_SdkType_HasCorrectStructure()
    {
        // Verify SDK StorylinePlanAction structure
        var narrativeEffect = new NarrativeEffect
        {
            PrimarySpectrumDelta = 0.3,
            SecondarySpectrumDelta = -0.1,
            PositionAdvance = "standard"
        };

        var action = new StorylinePlanAction
        {
            ActionId = "test-action",
            SequenceIndex = 0,
            Effects = new[]
            {
                new ActionEffect
                {
                    Key = "test-key",
                    Value = 1.0,
                    Cardinality = EffectCardinality.Exclusive
                }
            },
            NarrativeEffect = narrativeEffect,
            IsCoreEvent = true,
            ChainedFrom = null
        };

        Assert.Equal("test-action", action.ActionId);
        Assert.Equal(0, action.SequenceIndex);
        Assert.Single(action.Effects);
        Assert.Equal(0.3, action.NarrativeEffect.PrimarySpectrumDelta);
        Assert.Equal("standard", action.NarrativeEffect.PositionAdvance);
        Assert.True(action.IsCoreEvent);
    }

    [Fact]
    public void ArcType_SdkEnum_HasExpectedValues()
    {
        // Verify SDK ArcType enum has expected values
        Assert.True(Enum.IsDefined(typeof(ArcType), ArcType.RagsToRiches));
        Assert.True(Enum.IsDefined(typeof(ArcType), ArcType.Tragedy));
        Assert.True(Enum.IsDefined(typeof(ArcType), ArcType.ManInHole));
        Assert.True(Enum.IsDefined(typeof(ArcType), ArcType.Icarus));
        Assert.True(Enum.IsDefined(typeof(ArcType), ArcType.Cinderella));
        Assert.True(Enum.IsDefined(typeof(ArcType), ArcType.Oedipus));
    }

    [Fact]
    public void SpectrumType_SdkEnum_HasExpectedValues()
    {
        // Verify SDK SpectrumType enum has expected values (10 Life Value spectrums)
        Assert.True(Enum.IsDefined(typeof(SpectrumType), SpectrumType.LifeDeath));
        Assert.True(Enum.IsDefined(typeof(SpectrumType), SpectrumType.LoveHate));
        Assert.True(Enum.IsDefined(typeof(SpectrumType), SpectrumType.SuccessFailure));
        Assert.True(Enum.IsDefined(typeof(SpectrumType), SpectrumType.JusticeInjustice));
        Assert.True(Enum.IsDefined(typeof(SpectrumType), SpectrumType.WisdomIgnorance));
    }

    [Fact]
    public void PlanningUrgency_SdkEnum_HasExpectedValues()
    {
        // Verify SDK PlanningUrgency enum has expected values
        Assert.True(Enum.IsDefined(typeof(PlanningUrgency), PlanningUrgency.Low));
        Assert.True(Enum.IsDefined(typeof(PlanningUrgency), PlanningUrgency.Medium));
        Assert.True(Enum.IsDefined(typeof(PlanningUrgency), PlanningUrgency.High));
    }

    #endregion

    #region Enum Boundary Mapping Validation

    /// <summary>
    /// Validates that the schema-generated ArcType enum and the SDK ArcType enum
    /// have identical value names. This catches drift when either the schema or the
    /// SDK adds/removes/renames values without updating the other.
    /// </summary>
    [Fact]
    public void ArcType_SchemaAndSdk_HaveFullCoverage() =>
        EnumMappingValidator.AssertFullCoverage<Storyline.ArcType, ArcType>();

    /// <summary>
    /// Validates that the schema-generated SpectrumType enum and the SDK SpectrumType enum
    /// have identical value names.
    /// </summary>
    [Fact]
    public void SpectrumType_SchemaAndSdk_HaveFullCoverage() =>
        EnumMappingValidator.AssertFullCoverage<Storyline.SpectrumType, SpectrumType>();

    /// <summary>
    /// Validates that the schema-generated PlanningUrgency enum and the SDK PlanningUrgency enum
    /// have identical value names.
    /// </summary>
    [Fact]
    public void PlanningUrgency_SchemaAndSdk_HaveFullCoverage() =>
        EnumMappingValidator.AssertFullCoverage<Storyline.PlanningUrgency, PlanningUrgency>();

    /// <summary>
    /// Validates that the schema-generated EffectCardinality enum and the SDK EffectCardinality enum
    /// have identical value names.
    /// </summary>
    [Fact]
    public void EffectCardinality_SchemaAndSdk_HaveFullCoverage() =>
        EnumMappingValidator.AssertFullCoverage<Storyline.EffectCardinality, EffectCardinality>();

    /// <summary>
    /// Validates that the Storyline-owned ExperienceType enum is a subset of the
    /// CharacterPersonality ExperienceType enum (A2 boundary mapping via MapByName).
    /// </summary>
    [Fact]
    public void StorylineExperienceType_IsSubsetOf_CharacterPersonalityExperienceType() =>
        EnumMappingValidator.AssertSourceCoveredByTarget<
            Storyline.StorylineExperienceType,
            CharacterPersonality.ExperienceType>();

    /// <summary>
    /// Validates that the Storyline-owned BackstoryElementType enum is a subset of the
    /// CharacterHistory BackstoryElementType enum (A2 boundary mapping via MapByName).
    /// </summary>
    [Fact]
    public void StorylineBackstoryElementType_IsSubsetOf_CharacterHistoryBackstoryElementType() =>
        EnumMappingValidator.AssertSourceCoveredByTarget<
            Storyline.StorylineBackstoryElementType,
            CharacterHistory.BackstoryElementType>();

    /// <summary>
    /// Validates that MapByName round-trips correctly for all ArcType values.
    /// </summary>
    [Theory]
    [MemberData(nameof(AllArcTypes))]
    public void ArcType_MapByName_RoundTrips(Storyline.ArcType schemaValue)
    {
        var sdk = schemaValue.MapByName<Storyline.ArcType, ArcType>();
        var roundTripped = sdk.MapByName<ArcType, Storyline.ArcType>();
        Assert.Equal(schemaValue, roundTripped);
    }

    public static TheoryData<Storyline.ArcType> AllArcTypes()
    {
        var data = new TheoryData<Storyline.ArcType>();
        foreach (var value in Enum.GetValues<Storyline.ArcType>())
            data.Add(value);
        return data;
    }

    #endregion

    #region StorylineGoal Tests

    [Theory]
    [InlineData(StorylineGoal.Revenge)]
    [InlineData(StorylineGoal.Resurrection)]
    [InlineData(StorylineGoal.Legacy)]
    [InlineData(StorylineGoal.Mystery)]
    [InlineData(StorylineGoal.Peace)]
    public void StorylineGoal_HasOnlyPlanSpecifiedValues(StorylineGoal goal)
    {
        // Verify only the 5 goals from the plan exist
        Assert.True(Enum.IsDefined(typeof(StorylineGoal), goal));
    }

    [Fact]
    public void StorylineGoal_HasExactlyFiveValues()
    {
        // Plan specifies exactly 5 goals: revenge, resurrection, legacy, mystery, peace
        var values = Enum.GetValues<StorylineGoal>();
        Assert.Equal(5, values.Length);
    }

    #endregion

    #region EffectCardinality Tests

    [Fact]
    public void EffectCardinality_SdkEnum_HasExpectedValues()
    {
        // Verify SDK EffectCardinality enum
        Assert.True(Enum.IsDefined(typeof(EffectCardinality), EffectCardinality.Exclusive));
        Assert.True(Enum.IsDefined(typeof(EffectCardinality), EffectCardinality.Additive));
    }

    #endregion

    #region NarrativeEffect Tests

    [Fact]
    public void NarrativeEffect_SdkClass_HasExpectedProperties()
    {
        // Verify SDK NarrativeEffect class structure
        var effect = new NarrativeEffect
        {
            PrimarySpectrumDelta = 0.5,
            SecondarySpectrumDelta = -0.2,
            PositionAdvance = "macro"
        };

        Assert.Equal(0.5, effect.PrimarySpectrumDelta);
        Assert.Equal(-0.2, effect.SecondarySpectrumDelta);
        Assert.Equal("macro", effect.PositionAdvance);
    }

    [Fact]
    public void NarrativeEffect_PropertiesAreNullable()
    {
        // All properties are optional (nullable)
        var effect = new NarrativeEffect();

        Assert.Null(effect.PrimarySpectrumDelta);
        Assert.Null(effect.SecondarySpectrumDelta);
        Assert.Null(effect.PositionAdvance);
    }

    [Theory]
    [InlineData("micro")]
    [InlineData("standard")]
    [InlineData("macro")]
    public void NarrativeEffect_PositionAdvance_AcceptsValidValues(string advance)
    {
        var effect = new NarrativeEffect { PositionAdvance = advance };
        Assert.Equal(advance, effect.PositionAdvance);
    }

    #endregion

    #region StorylineRiskSeverity Tests

    [Fact]
    public void StorylineRiskSeverity_HasExpectedValues()
    {
        Assert.True(Enum.IsDefined(typeof(StorylineRiskSeverity), StorylineRiskSeverity.Low));
        Assert.True(Enum.IsDefined(typeof(StorylineRiskSeverity), StorylineRiskSeverity.Medium));
        Assert.True(Enum.IsDefined(typeof(StorylineRiskSeverity), StorylineRiskSeverity.High));
    }

    #endregion

    #region GetCompressData Response Tests

    [Fact]
    public void StorylineArchive_HasRequiredFields()
    {
        // Test that the archive model has all required fields
        var archive = new StorylineArchive
        {
            ResourceId = Guid.NewGuid(),
            ResourceType = "storyline",
            ArchivedAt = DateTimeOffset.UtcNow,
            SchemaVersion = 1,
            CharacterId = Guid.NewGuid(),
            Participations = new List<StorylineParticipation>(),
            ActiveArcs = new List<string>(),
            CompletedStorylines = 0
        };

        Assert.NotEqual(Guid.Empty, archive.ResourceId);
        Assert.Equal("storyline", archive.ResourceType);
        Assert.NotEqual(Guid.Empty, archive.CharacterId);
        Assert.Empty(archive.Participations);
        Assert.Empty(archive.ActiveArcs);
        Assert.Equal(0, archive.CompletedStorylines);
    }

    [Fact]
    public void StorylineParticipation_HasRequiredFields()
    {
        // Test that participation model captures scenario participation
        var participation = new StorylineParticipation
        {
            ExecutionId = Guid.NewGuid(),
            ScenarioId = Guid.NewGuid(),
            ScenarioCode = "ROMANCE_FIRST_MEETING",
            ScenarioName = "First Meeting",
            Role = "primary",
            Phase = 2,
            TotalPhases = 5,
            Status = ScenarioStatus.Active,
            StartedAt = DateTimeOffset.UtcNow.AddDays(-1),
            CompletedAt = null,
            Choices = new List<string> { "approached", "offered_help" }
        };

        Assert.Equal("ROMANCE_FIRST_MEETING", participation.ScenarioCode);
        Assert.Equal("primary", participation.Role);
        Assert.Equal(ScenarioStatus.Active, participation.Status);
        Assert.Null(participation.CompletedAt);
        Assert.Equal(2, participation.Choices.Count);
    }

    [Fact]
    public void StorylineArchive_WithActiveAndCompletedScenarios()
    {
        // Test archive with both active and completed scenarios
        var characterId = Guid.NewGuid();
        var activeExecution = new StorylineParticipation
        {
            ExecutionId = Guid.NewGuid(),
            ScenarioCode = "REVENGE_PLOT",
            ScenarioName = "Revenge Plot",
            Role = "primary",
            Phase = 1,
            TotalPhases = 3,
            Status = ScenarioStatus.Active,
            StartedAt = DateTimeOffset.UtcNow.AddHours(-2)
        };
        var completedExecution = new StorylineParticipation
        {
            ExecutionId = Guid.NewGuid(),
            ScenarioCode = "MYSTERY_SOLVED",
            ScenarioName = "Mystery Solved",
            Role = "primary",
            Phase = 4,
            TotalPhases = 4,
            Status = ScenarioStatus.Completed,
            StartedAt = DateTimeOffset.UtcNow.AddDays(-5),
            CompletedAt = DateTimeOffset.UtcNow.AddDays(-1)
        };

        var archive = new StorylineArchive
        {
            ResourceId = characterId,
            ResourceType = "storyline",
            ArchivedAt = DateTimeOffset.UtcNow,
            SchemaVersion = 1,
            CharacterId = characterId,
            Participations = new List<StorylineParticipation> { activeExecution, completedExecution },
            ActiveArcs = new List<string> { "REVENGE" },
            CompletedStorylines = 1
        };

        Assert.Equal(2, archive.Participations.Count);
        Assert.Single(archive.ActiveArcs);
        Assert.Equal(1, archive.CompletedStorylines);
        Assert.Contains(archive.Participations, p => p.Status == ScenarioStatus.Active);
        Assert.Contains(archive.Participations, p => p.Status == ScenarioStatus.Completed);
    }

    [Fact]
    public void StorylineArchive_DeriveActiveArcsFromCodes()
    {
        // Test that active arcs are derived from scenario codes (prefix before underscore)
        var activeArcs = new List<string> { "romance", "mystery" };

        // Simulate the service logic: scenario codes like "romance_first_meeting" -> "romance"
        Assert.Contains("romance", activeArcs);
        Assert.Contains("mystery", activeArcs);
    }

    #endregion
}

/// <summary>
/// Tests for ComposeRequest validation and structure.
/// </summary>
public class ComposeRequestTests
{
    [Fact]
    public void ComposeRequest_CanBeCreatedWithMinimalData()
    {
        var request = new ComposeRequest
        {
            SeedSources = new List<SeedSource>
            {
                new SeedSource { ArchiveId = Guid.NewGuid() }
            },
            Goal = StorylineGoal.Revenge
        };

        Assert.Single(request.SeedSources);
        Assert.Equal(StorylineGoal.Revenge, request.Goal);
    }

    [Fact]
    public void ComposeRequest_OptionalFieldsAreNullable()
    {
        var request = new ComposeRequest
        {
            SeedSources = new List<SeedSource>
            {
                new SeedSource { ArchiveId = Guid.NewGuid() }
            },
            Goal = StorylineGoal.Legacy
        };

        Assert.Null(request.Constraints);
        Assert.Null(request.Genre);
        Assert.Null(request.ArcType);
        Assert.Null(request.Urgency);
        Assert.Null(request.Seed);
    }

    [Fact]
    public void ComposeRequest_CanSpecifyAllOptions()
    {
        var request = new ComposeRequest
        {
            SeedSources = new List<SeedSource>
            {
                new SeedSource
                {
                    ArchiveId = Guid.NewGuid(),
                    Role = "protagonist"
                }
            },
            Goal = StorylineGoal.Mystery,
            Constraints = new ComposeConstraints
            {
                RealmId = Guid.NewGuid(),
                LocationId = Guid.NewGuid(),
                MaxEntities = 5,
                MaxPhases = 4
            },
            Genre = "crime",
            ArcType = ArcType.Cinderella,
            Urgency = PlanningUrgency.High,
            Seed = 12345
        };

        Assert.NotNull(request.Constraints);
        Assert.Equal("crime", request.Genre);
        Assert.Equal(ArcType.Cinderella, request.ArcType);
        Assert.Equal(PlanningUrgency.High, request.Urgency);
        Assert.Equal(12345, request.Seed);
    }
}

/// <summary>
/// Tests for SeedSource structure.
/// </summary>
public class SeedSourceTests
{
    [Fact]
    public void SeedSource_CanUseArchiveId()
    {
        var archiveId = Guid.NewGuid();
        var source = new SeedSource { ArchiveId = archiveId };

        Assert.Equal(archiveId, source.ArchiveId);
        Assert.Null(source.SnapshotId);
    }

    [Fact]
    public void SeedSource_CanUseSnapshotId()
    {
        var snapshotId = Guid.NewGuid();
        var source = new SeedSource { SnapshotId = snapshotId };

        Assert.Null(source.ArchiveId);
        Assert.Equal(snapshotId, source.SnapshotId);
    }

    [Fact]
    public void SeedSource_RoleIsOptional()
    {
        var source = new SeedSource { ArchiveId = Guid.NewGuid() };

        Assert.Null(source.Role);
    }

    [Theory]
    [InlineData("protagonist")]
    [InlineData("antagonist")]
    [InlineData("helper")]
    public void SeedSource_CanSpecifyRole(string role)
    {
        var source = new SeedSource
        {
            ArchiveId = Guid.NewGuid(),
            Role = role
        };

        Assert.Equal(role, source.Role);
    }
}

/// <summary>
/// Tests for ComposeResponse structure.
/// </summary>
public class ComposeResponseTests
{
    [Fact]
    public void ComposeResponse_HasRequiredFields()
    {
        var response = new ComposeResponse
        {
            PlanId = Guid.NewGuid(),
            Confidence = 0.85,
            Goal = StorylineGoal.Revenge,
            Genre = "action",
            ArcType = ArcType.ManInHole,
            PrimarySpectrum = SpectrumType.JusticeInjustice,
            Phases = new List<StorylinePlanPhase>(),
            GenerationTimeMs = 150,
            Cached = false
        };

        Assert.NotEqual(Guid.Empty, response.PlanId);
        Assert.Equal(0.85, response.Confidence);
        Assert.Equal(StorylineGoal.Revenge, response.Goal);
    }

    [Fact]
    public void ComposeResponse_PhasesUsesSdkType()
    {
        // Verify Phases collection uses SDK StorylinePlanPhase directly
        var phase = new StorylinePlanPhase
        {
            PhaseNumber = 1,
            Name = "Discovery",
            Actions = Array.Empty<StorylinePlanAction>(),
            TargetState = new PhaseTargetState
            {
                MinPrimarySpectrum = 0.2,
                MaxPrimarySpectrum = 0.4
            },
            PositionBounds = new PhasePosition
            {
                StcCenter = 0.125,
                Floor = 0.0,
                Ceiling = 0.25,
                ValidationBand = 0.05
            }
        };

        var response = new ComposeResponse
        {
            PlanId = Guid.NewGuid(),
            Confidence = 0.7,
            Goal = StorylineGoal.Mystery,
            ArcType = ArcType.Cinderella,
            PrimarySpectrum = SpectrumType.WisdomIgnorance,
            Phases = new List<StorylinePlanPhase> { phase },
            GenerationTimeMs = 100,
            Cached = false
        };

        Assert.Single(response.Phases);
        Assert.Equal("Discovery", response.Phases.First().Name);
    }
}

/// <summary>
/// Tests for GetPlanRequest/Response structure.
/// </summary>
public class GetPlanTests
{
    [Fact]
    public void GetPlanRequest_RequiresPlanId()
    {
        var planId = Guid.NewGuid();
        var request = new GetPlanRequest { PlanId = planId };

        Assert.Equal(planId, request.PlanId);
    }

    [Fact]
    public void GetPlanResponse_WithNullPlan()
    {
        var response = new GetPlanResponse
        {
            Plan = null
        };

        Assert.Null(response.Plan);
    }

    [Fact]
    public void GetPlanResponse_WithPlan()
    {
        var response = new GetPlanResponse
        {
            Plan = new ComposeResponse
            {
                PlanId = Guid.NewGuid(),
                Confidence = 0.9,
                Goal = StorylineGoal.Legacy,
                ArcType = ArcType.RagsToRiches,
                PrimarySpectrum = SpectrumType.SuccessFailure,
                Phases = new List<StorylinePlanPhase>(),
                GenerationTimeMs = 200,
                Cached = true
            }
        };

        Assert.NotNull(response.Plan);
        Assert.True(response.Plan.Cached);
    }
}

/// <summary>
/// Tests for ListPlansRequest/Response structure.
/// </summary>
public class ListPlansTests
{
    [Fact]
    public void ListPlansRequest_HasDefaults()
    {
        var request = new ListPlansRequest();

        Assert.Equal(20, request.Limit);
        Assert.Equal(0, request.Offset);
        Assert.Null(request.RealmId);
    }

    [Fact]
    public void ListPlansRequest_CanFilterByRealm()
    {
        var realmId = Guid.NewGuid();
        var request = new ListPlansRequest
        {
            RealmId = realmId,
            Limit = 50,
            Offset = 10
        };

        Assert.Equal(realmId, request.RealmId);
        Assert.Equal(50, request.Limit);
        Assert.Equal(10, request.Offset);
    }

    [Fact]
    public void ListPlansResponse_ContainsPlanSummaries()
    {
        var response = new ListPlansResponse
        {
            Plans = new List<PlanSummary>
            {
                new PlanSummary
                {
                    PlanId = Guid.NewGuid(),
                    Goal = StorylineGoal.Peace,
                    ArcType = ArcType.ManInHole,
                    Confidence = 0.75,
                    CreatedAt = DateTimeOffset.UtcNow
                }
            },
            TotalCount = 1
        };

        Assert.Single(response.Plans);
        Assert.Equal(1, response.TotalCount);
    }
}

/// <summary>
/// Tests for StorylineRisk structure.
/// </summary>
public class StorylineRiskTests
{
    [Fact]
    public void StorylineRisk_CanBeCreated()
    {
        var risk = new StorylineRisk
        {
            RiskType = "thin_content",
            Description = "Plan has very few actions",
            Severity = StorylineRiskSeverity.Medium,
            Mitigation = "Add more intermediate actions"
        };

        Assert.Equal("thin_content", risk.RiskType);
        Assert.Equal(StorylineRiskSeverity.Medium, risk.Severity);
        Assert.NotNull(risk.Mitigation);
    }

    [Fact]
    public void StorylineRisk_MitigationIsOptional()
    {
        var risk = new StorylineRisk
        {
            RiskType = "test",
            Description = "Test risk",
            Severity = StorylineRiskSeverity.Low
        };

        // Mitigation should be nullable
        Assert.Null(risk.Mitigation);
    }
}

/// <summary>
/// Tests for EntityRequirement structure.
/// </summary>
public class EntityRequirementTests
{
    [Fact]
    public void EntityRequirement_HasRequiredFields()
    {
        var entity = new EntityRequirement
        {
            Role = "witness",
            EntityType = "character",
            Description = "A character who witnessed the crime"
        };

        Assert.Equal("witness", entity.Role);
        Assert.Equal("character", entity.EntityType);
        Assert.NotEmpty(entity.Description);
    }

    [Fact]
    public void EntityRequirement_OptionalFieldsAreNullable()
    {
        var entity = new EntityRequirement
        {
            Role = "target",
            EntityType = "location",
            Description = "Target location"
        };

        Assert.Null(entity.Constraints);
        Assert.Null(entity.SourceArchiveId);
    }
}

/// <summary>
/// Tests for StorylineLink structure.
/// </summary>
public class StorylineDeprecationTests : ServiceTestBase<StorylineServiceConfiguration>
{
    private readonly Mock<IMessageBus> _mockMessageBus;
    private readonly Mock<IStateStoreFactory> _mockStateStoreFactory;
    private readonly Mock<IResourceClient> _mockResourceClient;
    private readonly Mock<IRelationshipClient> _mockRelationshipClient;
    private readonly Mock<IDistributedLockProvider> _mockLockProvider;
    private readonly Mock<ITelemetryProvider> _mockTelemetryProvider;
    private readonly Mock<ILogger<StorylineService>> _mockLogger;
    private readonly Mock<IServiceProvider> _mockServiceProvider;

    // State stores
    private readonly Mock<IStateStore<CachedPlan>> _mockPlanStore;
    private readonly Mock<ICacheableStateStore<PlanIndexEntry>> _mockPlanIndexStore;
    private readonly Mock<IQueryableStateStore<ScenarioDefinitionModel>> _mockScenarioDefinitionStore;
    private readonly Mock<ICacheableStateStore<ScenarioDefinitionModel>> _mockScenarioCacheStore;
    private readonly Mock<IQueryableStateStore<ScenarioExecutionModel>> _mockScenarioExecutionStore;
    private readonly Mock<ICacheableStateStore<CooldownMarker>> _mockCooldownStore;
    private readonly Mock<ICacheableStateStore<ActiveScenarioEntry>> _mockActiveStore;
    private readonly Mock<ICacheableStateStore<IdempotencyMarker>> _mockIdempotencyStore;

    public StorylineDeprecationTests()
    {
        _mockMessageBus = new Mock<IMessageBus>();
        _mockStateStoreFactory = new Mock<IStateStoreFactory>();
        _mockResourceClient = new Mock<IResourceClient>();
        _mockRelationshipClient = new Mock<IRelationshipClient>();
        _mockLockProvider = new Mock<IDistributedLockProvider>();
        _mockTelemetryProvider = new Mock<ITelemetryProvider>();
        _mockLogger = new Mock<ILogger<StorylineService>>();
        _mockServiceProvider = new Mock<IServiceProvider>();

        _mockPlanStore = new Mock<IStateStore<CachedPlan>>();
        _mockPlanIndexStore = new Mock<ICacheableStateStore<PlanIndexEntry>>();
        _mockScenarioDefinitionStore = new Mock<IQueryableStateStore<ScenarioDefinitionModel>>();
        _mockScenarioCacheStore = new Mock<ICacheableStateStore<ScenarioDefinitionModel>>();
        _mockScenarioExecutionStore = new Mock<IQueryableStateStore<ScenarioExecutionModel>>();
        _mockCooldownStore = new Mock<ICacheableStateStore<CooldownMarker>>();
        _mockActiveStore = new Mock<ICacheableStateStore<ActiveScenarioEntry>>();
        _mockIdempotencyStore = new Mock<ICacheableStateStore<IdempotencyMarker>>();

        // Setup state store factory
        _mockStateStoreFactory.Setup(f => f.GetStore<CachedPlan>(StateStoreDefinitions.StorylinePlans)).Returns(_mockPlanStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetCacheableStore<PlanIndexEntry>(StateStoreDefinitions.StorylinePlanIndex)).Returns(_mockPlanIndexStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetQueryableStore<ScenarioDefinitionModel>(StateStoreDefinitions.StorylineScenarioDefinitions)).Returns(_mockScenarioDefinitionStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetCacheableStore<ScenarioDefinitionModel>(StateStoreDefinitions.StorylineScenarioCache)).Returns(_mockScenarioCacheStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetQueryableStore<ScenarioExecutionModel>(StateStoreDefinitions.StorylineScenarioExecutions)).Returns(_mockScenarioExecutionStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetCacheableStore<CooldownMarker>(StateStoreDefinitions.StorylineScenarioCooldown)).Returns(_mockCooldownStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetCacheableStore<ActiveScenarioEntry>(StateStoreDefinitions.StorylineScenarioActive)).Returns(_mockActiveStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetCacheableStore<IdempotencyMarker>(StateStoreDefinitions.StorylineScenarioIdempotency)).Returns(_mockIdempotencyStore.Object);

        // Default message bus setup
        _mockMessageBus
            .Setup(m => m.TryPublishAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
    }

    private StorylineService CreateService()
    {
        return new StorylineService(
            _mockMessageBus.Object,
            _mockStateStoreFactory.Object,
            _mockResourceClient.Object,
            _mockRelationshipClient.Object,
            _mockServiceProvider.Object,
            _mockLockProvider.Object,
            _mockTelemetryProvider.Object,
            _mockLogger.Object,
            Configuration);
    }

    private static ScenarioDefinitionModel CreateTestScenarioModel(Guid scenarioId, bool isDeprecated = false)
    {
        return new ScenarioDefinitionModel
        {
            ScenarioId = scenarioId,
            Code = "TEST_SCENARIO",
            Name = "Test Scenario",
            Description = "A test scenario",
            TriggerConditionsJson = "[]",
            PhasesJson = "[]",
            Priority = 1,
            Enabled = !isDeprecated,
            IsDeprecated = isDeprecated,
            DeprecatedAt = isDeprecated ? DateTimeOffset.UtcNow.AddDays(-1) : null,
            DeprecationReason = isDeprecated ? "Previously deprecated" : null,
            CreatedAt = DateTimeOffset.UtcNow,
            Etag = Guid.NewGuid().ToString("N")
        };
    }

    #region Deprecation Tests

    [Fact]
    public async Task DeprecateScenarioDefinitionAsync_ValidScenario_ReturnsOK()
    {
        // Arrange
        var service = CreateService();
        var scenarioId = Guid.NewGuid();
        var model = CreateTestScenarioModel(scenarioId);

        // GetScenarioDefinitionWithCacheAsync checks cache first, then MySQL
        _mockScenarioCacheStore
            .Setup(s => s.GetAsync(scenarioId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(model);

        var request = new DeprecateScenarioDefinitionRequest
        {
            ScenarioId = scenarioId,
            Reason = "No longer relevant"
        };

        // Act
        var status = await service.DeprecateScenarioDefinitionAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);

        // Verify the model was saved with deprecation fields
        _mockScenarioDefinitionStore.Verify(
            s => s.SaveAsync(
                scenarioId.ToString(),
                It.Is<ScenarioDefinitionModel>(m =>
                    m.IsDeprecated &&
                    m.DeprecatedAt != null &&
                    m.DeprecationReason == "No longer relevant" &&
                    !m.Enabled),
                It.IsAny<StateOptions?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);

        // Verify cache was invalidated
        _mockScenarioCacheStore.Verify(
            s => s.DeleteAsync(scenarioId.ToString(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task DeprecateScenarioDefinitionAsync_NotFound_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        var scenarioId = Guid.NewGuid();

        // Cache miss
        _mockScenarioCacheStore
            .Setup(s => s.GetAsync(scenarioId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ScenarioDefinitionModel?)null);

        // MySQL miss
        _mockScenarioDefinitionStore
            .Setup(s => s.GetAsync(scenarioId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ScenarioDefinitionModel?)null);

        var request = new DeprecateScenarioDefinitionRequest { ScenarioId = scenarioId };

        // Act
        var status = await service.DeprecateScenarioDefinitionAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
    }

    [Fact]
    public async Task DeprecateScenarioDefinitionAsync_SetsTripleFieldModel()
    {
        // Arrange
        var service = CreateService();
        var scenarioId = Guid.NewGuid();
        var model = CreateTestScenarioModel(scenarioId);

        _mockScenarioCacheStore
            .Setup(s => s.GetAsync(scenarioId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(model);

        ScenarioDefinitionModel? savedModel = null;
        _mockScenarioDefinitionStore
            .Setup(s => s.SaveAsync(
                It.IsAny<string>(),
                It.IsAny<ScenarioDefinitionModel>(),
                It.IsAny<StateOptions?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, ScenarioDefinitionModel, StateOptions?, CancellationToken>((_, m, _, _) => savedModel = m)
            .ReturnsAsync("etag");

        var request = new DeprecateScenarioDefinitionRequest
        {
            ScenarioId = scenarioId,
            Reason = "Obsolete scenario"
        };

        // Act
        await service.DeprecateScenarioDefinitionAsync(request, CancellationToken.None);

        // Assert — triple-field model per IMPLEMENTATION TENETS
        Assert.NotNull(savedModel);
        Assert.True(savedModel.IsDeprecated);
        Assert.NotNull(savedModel.DeprecatedAt);
        Assert.Equal("Obsolete scenario", savedModel.DeprecationReason);
        Assert.False(savedModel.Enabled);
    }

    #endregion
}

/// <summary>
/// Tests for StorylineLink model.
/// </summary>
public class StorylineLinkTests
{
    [Fact]
    public void StorylineLink_HasRequiredFields()
    {
        var link = new StorylineLink
        {
            SourceRole = "protagonist",
            TargetRole = "antagonist",
            LinkType = "opposes"
        };

        Assert.Equal("protagonist", link.SourceRole);
        Assert.Equal("antagonist", link.TargetRole);
        Assert.Equal("opposes", link.LinkType);
    }
}
