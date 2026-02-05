using BeyondImmersion.Bannou.StorylineStoryteller.Actions;
using BeyondImmersion.Bannou.StorylineStoryteller.Planning;
using BeyondImmersion.Bannou.StorylineStoryteller.Templates;
using BeyondImmersion.Bannou.StorylineTheory.Arcs;
using BeyondImmersion.Bannou.StorylineTheory.Spectrums;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Resource;
using BeyondImmersion.BannouService.State;
using BeyondImmersion.BannouService.Storyline;
using BeyondImmersion.BannouService.TestUtilities;
using Microsoft.Extensions.Logging;
using Moq;

namespace BeyondImmersion.BannouService.Storyline.Tests;

public class StorylineServiceTests
{
    #region Constructor Validation

    /// <summary>
    /// Validates the service constructor follows proper DI patterns.
    ///
    /// This single test replaces N individual null-check tests and catches:
    /// - Multiple constructors (DI might pick wrong one)
    /// - Optional parameters (accidental defaults that hide missing registrations)
    /// - Missing null checks (ArgumentNullException not thrown)
    /// - Wrong parameter names in ArgumentNullException
    ///
    /// See: docs/reference/tenets/TESTING_PATTERNS.md
    /// </summary>
    [Fact]
    public void StorylineService_ConstructorIsValid() =>
        ServiceConstructorValidator.ValidateServiceConstructor<StorylineService>();

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
    public void GetPlanResponse_IndicatesNotFound()
    {
        var response = new GetPlanResponse
        {
            Found = false,
            Plan = null
        };

        Assert.False(response.Found);
        Assert.Null(response.Plan);
    }

    [Fact]
    public void GetPlanResponse_IndicatesFound()
    {
        var response = new GetPlanResponse
        {
            Found = true,
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

        Assert.True(response.Found);
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
