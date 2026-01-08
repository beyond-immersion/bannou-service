// =============================================================================
// Cognition Pipeline Integration Tests
// Tests end-to-end cognition pipeline flows with templates, overrides, and
// archetype-driven modifications.
// =============================================================================

using BeyondImmersion.Bannou.Behavior.Cognition;
using BeyondImmersion.BannouService.Behavior;
using Xunit;

namespace BeyondImmersion.BannouService.Behavior.Tests.Integration;

/// <summary>
/// Integration tests for the cognition pipeline system covering templates,
/// building, overrides, and pipeline execution.
/// </summary>
public sealed class CognitionPipelineIntegrationTests
{
    private readonly CognitionTemplateRegistry _templateRegistry;
    private readonly CognitionBuilder _builder;

    public CognitionPipelineIntegrationTests()
    {
        _templateRegistry = new CognitionTemplateRegistry(loadEmbeddedDefaults: true);
        _builder = new CognitionBuilder(_templateRegistry);
    }

    // =========================================================================
    // TEMPLATE + BUILDER INTEGRATION TESTS
    // =========================================================================

    [Fact]
    public void BuildPipeline_HumanoidTemplate_HasAllFiveStages()
    {
        // Act - Build pipeline from humanoid template
        var pipeline = _builder.Build(CognitionTemplates.HumanoidBase);

        // Assert - All 5 cognition stages present
        Assert.NotNull(pipeline);
        Assert.Equal(CognitionTemplates.HumanoidBase, pipeline.TemplateId);
        Assert.Equal(5, pipeline.Stages.Count);

        // Verify stage ordering
        Assert.Equal(CognitionStages.Filter, pipeline.Stages[0].Name);
        Assert.Equal(CognitionStages.MemoryQuery, pipeline.Stages[1].Name);
        Assert.Equal(CognitionStages.Significance, pipeline.Stages[2].Name);
        Assert.Equal(CognitionStages.Storage, pipeline.Stages[3].Name);
        Assert.Equal(CognitionStages.Intention, pipeline.Stages[4].Name);
    }

    [Fact]
    public void BuildPipeline_CreatureTemplate_HasSimplifiedThreeStages()
    {
        // Act
        var pipeline = _builder.Build(CognitionTemplates.CreatureBase);

        // Assert - Creatures skip significance and storage
        Assert.NotNull(pipeline);
        Assert.Equal(3, pipeline.Stages.Count);
        Assert.Equal(CognitionStages.Filter, pipeline.Stages[0].Name);
        Assert.Equal(CognitionStages.MemoryQuery, pipeline.Stages[1].Name);
        Assert.Equal(CognitionStages.Intention, pipeline.Stages[2].Name);
    }

    [Fact]
    public void BuildPipeline_ObjectTemplate_HasMinimalTwoStages()
    {
        // Act
        var pipeline = _builder.Build(CognitionTemplates.ObjectBase);

        // Assert - Objects only have filter and intention
        Assert.NotNull(pipeline);
        Assert.Equal(2, pipeline.Stages.Count);
        Assert.Equal(CognitionStages.Filter, pipeline.Stages[0].Name);
        Assert.Equal(CognitionStages.Intention, pipeline.Stages[1].Name);
    }

    // =========================================================================
    // OVERRIDE INTEGRATION TESTS
    // =========================================================================

    [Fact]
    public void BuildPipeline_WithParanoidOverrides_IncreasesThreatSensitivity()
    {
        // Arrange - Create paranoid character overrides
        var overrides = CognitionOverrides.FromList(
            new ParameterOverride
            {
                Stage = CognitionStages.Filter,
                HandlerId = "attention_filter",
                Parameters = new Dictionary<string, object>
                {
                    ["priority_weights"] = new Dictionary<string, object>
                    {
                        ["threat"] = 15.0f  // Higher threat sensitivity
                    },
                    ["threat_threshold"] = 0.4f  // Lower threshold (more sensitive)
                }
            },
            new ParameterOverride
            {
                Stage = CognitionStages.Significance,
                HandlerId = "assess_significance",
                Parameters = new Dictionary<string, object>
                {
                    ["weights"] = new Dictionary<string, object>
                    {
                        ["emotional"] = 0.6f  // More emotional response
                    }
                }
            });

        // Act
        var pipeline = _builder.Build(CognitionTemplates.HumanoidBase, overrides);
        Assert.NotNull(pipeline);

        // Assert - Paranoid modifications applied
        var filterStage = pipeline.Stages.First(s => s.Name == CognitionStages.Filter);
        var attentionHandler = filterStage.Handlers.First(h => h.Id == "attention_filter");
        var weights = attentionHandler.Parameters["priority_weights"] as Dictionary<string, object>;
        Assert.NotNull(weights);
        Assert.Equal(15.0f, weights["threat"]);
        Assert.Equal(0.4f, attentionHandler.Parameters["threat_threshold"]);
    }

    [Fact]
    public void BuildPipeline_ChainedOverrides_AppliesInOrder()
    {
        // Arrange - First override sets attention budget to 200
        var firstOverride = CognitionOverrides.FromList(
            new ParameterOverride
            {
                Stage = CognitionStages.Filter,
                HandlerId = "attention_filter",
                Parameters = new Dictionary<string, object>
                {
                    ["attention_budget"] = 200f
                }
            });

        // Second override sets it to 300
        var secondOverride = CognitionOverrides.FromList(
            new ParameterOverride
            {
                Stage = CognitionStages.Filter,
                HandlerId = "attention_filter",
                Parameters = new Dictionary<string, object>
                {
                    ["attention_budget"] = 300f
                }
            });

        // Act - Build with first, then second override
        var pipeline1 = _builder.Build(CognitionTemplates.HumanoidBase, firstOverride);
        Assert.NotNull(pipeline1);

        // Combine overrides
        var combined = CognitionOverrides.FromList(
            firstOverride.Overrides.Concat(secondOverride.Overrides).ToArray());
        var pipeline2 = _builder.Build(CognitionTemplates.HumanoidBase, combined);
        Assert.NotNull(pipeline2);

        // Assert - Second override wins
        var handler = pipeline2.Stages
            .First(s => s.Name == CognitionStages.Filter).Handlers
            .First(h => h.Id == "attention_filter");
        Assert.Equal(300f, handler.Parameters["attention_budget"]);
    }

    [Fact]
    public void BuildPipeline_WithDisabledHandler_SkipsHandler()
    {
        // Arrange - Disable memory storage for a "goldfish" character
        var overrides = CognitionOverrides.FromList(
            new DisableHandlerOverride
            {
                Stage = CognitionStages.Storage,
                HandlerId = "store_memory"
            });

        // Act
        var pipeline = _builder.Build(CognitionTemplates.HumanoidBase, overrides);
        Assert.NotNull(pipeline);

        // Assert - Handler exists but is disabled
        var storageStage = pipeline.Stages.First(s => s.Name == CognitionStages.Storage);
        var storeHandler = storageStage.Handlers.First(h => h.Id == "store_memory");
        Assert.False(storeHandler.IsEnabled);
    }

    [Fact]
    public void BuildPipeline_WithConditionalDisable_PreservesCondition()
    {
        // Arrange - Disable memory storage only in combat
        var overrides = CognitionOverrides.FromList(
            new DisableHandlerOverride
            {
                Stage = CognitionStages.Storage,
                HandlerId = "store_memory",
                Condition = "entity.in_combat == true"
            });

        // Act
        var pipeline = _builder.Build(CognitionTemplates.HumanoidBase, overrides);
        Assert.NotNull(pipeline);

        // Assert - Handler is enabled (condition evaluated at runtime)
        var storageStage = pipeline.Stages.First(s => s.Name == CognitionStages.Storage);
        var storeHandler = storageStage.Handlers.First(h => h.Id == "store_memory");
        Assert.True(storeHandler.IsEnabled); // Not unconditionally disabled
    }

    [Fact]
    public void BuildPipeline_WithAddedHandler_InsertsCorrectly()
    {
        // Arrange - Add a custom handler after goal_impact in intention stage
        var overrides = CognitionOverrides.FromList(
            new AddHandlerOverride
            {
                Stage = CognitionStages.Intention,
                AfterId = "goal_impact",
                Handler = new CognitionHandlerDefinition
                {
                    Id = "faction_reputation_check",
                    HandlerName = "check_faction_reputation",
                    Parameters = new Dictionary<string, object>
                    {
                        ["faction_weight"] = 0.3f
                    }
                }
            });

        // Act
        var pipeline = _builder.Build(CognitionTemplates.HumanoidBase, overrides);
        Assert.NotNull(pipeline);

        // Assert - New handler inserted after goal_impact
        var intentionStage = pipeline.Stages.First(s => s.Name == CognitionStages.Intention);
        var handlers = intentionStage.Handlers.ToList();

        var goalImpactIndex = handlers.FindIndex(h => h.Id == "goal_impact");
        var customIndex = handlers.FindIndex(h => h.Id == "faction_reputation_check");

        Assert.True(customIndex > goalImpactIndex);
        Assert.Equal(goalImpactIndex + 1, customIndex);
    }

    // =========================================================================
    // VALIDATION INTEGRATION TESTS
    // =========================================================================

    [Fact]
    public void ValidateOverrides_ValidOverrides_NoErrors()
    {
        // Arrange
        var overrides = CognitionOverrides.FromList(
            new ParameterOverride
            {
                Stage = CognitionStages.Filter,
                HandlerId = "attention_filter",
                Parameters = new Dictionary<string, object>
                {
                    ["attention_budget"] = 150f
                }
            });

        // Act
        var result = _builder.ValidateOverrides(CognitionTemplates.HumanoidBase, overrides);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void ValidateOverrides_InvalidStage_ReturnsError()
    {
        // Arrange
        var overrides = CognitionOverrides.FromList(
            new ParameterOverride
            {
                Stage = "nonexistent_stage",
                HandlerId = "attention_filter",
                Parameters = new Dictionary<string, object>()
            });

        // Act
        var result = _builder.ValidateOverrides(CognitionTemplates.HumanoidBase, overrides);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Stage not found"));
    }

    [Fact]
    public void ValidateOverrides_InvalidHandler_ReturnsError()
    {
        // Arrange
        var overrides = CognitionOverrides.FromList(
            new ParameterOverride
            {
                Stage = CognitionStages.Filter,
                HandlerId = "nonexistent_handler",
                Parameters = new Dictionary<string, object>()
            });

        // Act
        var result = _builder.ValidateOverrides(CognitionTemplates.HumanoidBase, overrides);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Handler not found"));
    }

    // =========================================================================
    // MULTI-TEMPLATE INTEGRATION TESTS
    // =========================================================================

    [Fact]
    public void AllEmbeddedTemplates_HaveValidStructure()
    {
        // Arrange - Get all template IDs
        var templateIds = _templateRegistry.GetTemplateIds();

        // Act & Assert - Each template builds successfully
        foreach (var templateId in templateIds)
        {
            var pipeline = _builder.Build(templateId);
            Assert.NotNull(pipeline);
            Assert.True(pipeline.Stages.Count >= 2); // At least filter and intention
            Assert.Contains(pipeline.Stages, s => s.Name == CognitionStages.Filter);
            Assert.Contains(pipeline.Stages, s => s.Name == CognitionStages.Intention);
        }
    }

    [Fact]
    public void TemplateOverrides_ApplyAcrossTemplateTypes()
    {
        // Arrange - Same override for all templates
        var overrides = CognitionOverrides.FromList(
            new ParameterOverride
            {
                Stage = CognitionStages.Filter,
                HandlerId = "attention_filter",
                Parameters = new Dictionary<string, object>
                {
                    ["attention_budget"] = 50f // Low budget for stealth entity
                }
            });

        // Act - Apply to all templates
        var humanoidPipeline = _builder.Build(CognitionTemplates.HumanoidBase, overrides);
        var creaturePipeline = _builder.Build(CognitionTemplates.CreatureBase, overrides);
        var objectPipeline = _builder.Build(CognitionTemplates.ObjectBase, overrides);

        // Assert - Override applied to all
        Assert.NotNull(humanoidPipeline);
        Assert.NotNull(creaturePipeline);
        Assert.NotNull(objectPipeline);

        AssertAttentionBudget(humanoidPipeline, 50f);
        AssertAttentionBudget(creaturePipeline, 50f);
        AssertAttentionBudget(objectPipeline, 50f);
    }

    // =========================================================================
    // DEEP MERGE INTEGRATION TESTS
    // =========================================================================

    [Fact]
    public void DeepMerge_NestedParameters_PreservesUnmodified()
    {
        // Arrange - Override only one nested value
        var overrides = CognitionOverrides.FromList(
            new ParameterOverride
            {
                Stage = CognitionStages.Filter,
                HandlerId = "attention_filter",
                Parameters = new Dictionary<string, object>
                {
                    ["priority_weights"] = new Dictionary<string, object>
                    {
                        ["threat"] = 20.0f // Double threat sensitivity
                    }
                }
            });

        // Act
        var pipeline = _builder.Build(CognitionTemplates.HumanoidBase, overrides);
        Assert.NotNull(pipeline);

        // Assert - Threat modified, others preserved
        var handler = pipeline.Stages
            .First(s => s.Name == CognitionStages.Filter).Handlers
            .First(h => h.Id == "attention_filter");

        var weights = handler.Parameters["priority_weights"] as Dictionary<string, object>;
        Assert.NotNull(weights);
        Assert.Equal(20.0f, weights["threat"]);
        Assert.Equal(5.0f, weights["novelty"]); // Preserved from template
        Assert.Equal(3.0f, weights["social"]);  // Preserved from template
    }

    // =========================================================================
    // HELPERS
    // =========================================================================

    private static void AssertAttentionBudget(ICognitionPipeline pipeline, float expected)
    {
        var filterStage = pipeline.Stages.First(s => s.Name == CognitionStages.Filter);
        var handler = filterStage.Handlers.First(h => h.Id == "attention_filter");
        Assert.Equal(expected, handler.Parameters["attention_budget"]);
    }
}
