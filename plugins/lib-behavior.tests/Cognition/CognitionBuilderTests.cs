// =============================================================================
// Cognition Builder Tests
// Tests for cognition pipeline building and override application.
// =============================================================================

using BeyondImmersion.Bannou.Behavior.Cognition;
using BeyondImmersion.BannouService.Behavior;
using Xunit;

namespace BeyondImmersion.BannouService.Behavior.Tests.Cognition;

/// <summary>
/// Tests for <see cref="CognitionBuilder"/>.
/// </summary>
public sealed class CognitionBuilderTests
{
    private readonly CognitionTemplateRegistry _registry;
    private readonly CognitionBuilder _builder;

    public CognitionBuilderTests()
    {
        _registry = new CognitionTemplateRegistry(loadEmbeddedDefaults: true);
        _builder = new CognitionBuilder(_registry);
    }

    // =========================================================================
    // BASIC BUILDING TESTS
    // =========================================================================

    [Fact]
    public void Build_ValidTemplateId_ReturnsPipeline()
    {
        // Act
        var pipeline = _builder.Build(CognitionTemplates.HumanoidBase);

        // Assert
        Assert.NotNull(pipeline);
        Assert.Equal(CognitionTemplates.HumanoidBase, pipeline.TemplateId);
        Assert.Equal(5, pipeline.Stages.Count);
    }

    [Fact]
    public void Build_UnknownTemplateId_ReturnsNull()
    {
        // Act
        var pipeline = _builder.Build("unknown-template");

        // Assert
        Assert.Null(pipeline);
    }

    [Fact]
    public void Build_NullTemplateId_Throws()
    {
        // Act & Assert
        Assert.ThrowsAny<ArgumentException>(() => _builder.Build((string)null!));
    }

    [Fact]
    public void Build_Template_ReturnsPipeline()
    {
        // Arrange
        var template = _registry.GetTemplate(CognitionTemplates.HumanoidBase)!;

        // Act
        var pipeline = _builder.Build(template);

        // Assert
        Assert.NotNull(pipeline);
        Assert.Equal(CognitionTemplates.HumanoidBase, pipeline.TemplateId);
    }

    [Fact]
    public void Build_NullTemplate_Throws()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => _builder.Build((CognitionTemplate)null!));
    }

    [Fact]
    public void Build_WithNoOverrides_PreservesTemplate()
    {
        // Act
        var pipeline = _builder.Build(CognitionTemplates.HumanoidBase, null);

        // Assert
        Assert.NotNull(pipeline);
        Assert.Equal(5, pipeline.Stages.Count);

        var filterStage = pipeline.Stages.First(s => s.Name == CognitionStages.Filter);
        var attentionHandler = filterStage.Handlers.First(h => h.Id == "attention_filter");
        Assert.True(attentionHandler.IsEnabled);
    }

    // =========================================================================
    // PARAMETER OVERRIDE TESTS
    // =========================================================================

    [Fact]
    public void Build_WithParameterOverride_ModifiesParameters()
    {
        // Arrange
        var overrides = CognitionOverrides.FromList(
            new ParameterOverride
            {
                Stage = CognitionStages.Filter,
                HandlerId = "attention_filter",
                Parameters = new Dictionary<string, object>
                {
                    ["attention_budget"] = 200f,
                    ["max_perceptions"] = 20
                }
            });

        // Act
        var pipeline = _builder.Build(CognitionTemplates.HumanoidBase, overrides);
        Assert.NotNull(pipeline);

        // Assert
        Assert.NotNull(pipeline);
        var handler = GetHandler(pipeline, CognitionStages.Filter, "attention_filter");
        Assert.Equal(200f, handler.Parameters["attention_budget"]);
        Assert.Equal(20, handler.Parameters["max_perceptions"]);
    }

    [Fact]
    public void Build_WithNestedParameterOverride_DeepMerges()
    {
        // Arrange - Override only threat weight, keep others
        var overrides = CognitionOverrides.FromList(
            new ParameterOverride
            {
                Stage = CognitionStages.Filter,
                HandlerId = "attention_filter",
                Parameters = new Dictionary<string, object>
                {
                    ["priority_weights"] = new Dictionary<string, object>
                    {
                        ["threat"] = 15.0f  // Paranoid: higher threat sensitivity
                    }
                }
            });

        // Act
        var pipeline = _builder.Build(CognitionTemplates.HumanoidBase, overrides);
        Assert.NotNull(pipeline);

        // Assert
        Assert.NotNull(pipeline);
        var handler = GetHandler(pipeline, CognitionStages.Filter, "attention_filter");
        var weights = (Dictionary<string, object>)handler.Parameters["priority_weights"];

        Assert.Equal(15.0f, weights["threat"]);  // Overridden
        Assert.Equal(5.0f, weights["novelty"]);  // Preserved from base
        Assert.Equal(3.0f, weights["social"]);   // Preserved from base
    }

    [Fact]
    public void Build_WithMultipleParameterOverrides_AppliesInOrder()
    {
        // Arrange
        var overrides = CognitionOverrides.FromList(
            new ParameterOverride
            {
                Stage = CognitionStages.Filter,
                HandlerId = "attention_filter",
                Parameters = new Dictionary<string, object> { ["attention_budget"] = 100f }
            },
            new ParameterOverride
            {
                Stage = CognitionStages.Filter,
                HandlerId = "attention_filter",
                Parameters = new Dictionary<string, object> { ["attention_budget"] = 200f }
            });

        // Act
        var pipeline = _builder.Build(CognitionTemplates.HumanoidBase, overrides);
        Assert.NotNull(pipeline);

        // Assert - Second override wins
        var handler = GetHandler(pipeline, CognitionStages.Filter, "attention_filter");
        Assert.Equal(200f, handler.Parameters["attention_budget"]);
    }

    // =========================================================================
    // DISABLE OVERRIDE TESTS
    // =========================================================================

    [Fact]
    public void Build_WithDisableOverride_DisablesHandler()
    {
        // Arrange
        var overrides = CognitionOverrides.FromList(
            new DisableHandlerOverride
            {
                Stage = CognitionStages.Storage,
                HandlerId = "store_memory"
            });

        // Act
        var pipeline = _builder.Build(CognitionTemplates.HumanoidBase, overrides);
        Assert.NotNull(pipeline);

        // Assert
        var handler = GetHandler(pipeline, CognitionStages.Storage, "store_memory");
        Assert.False(handler.IsEnabled);
    }

    [Fact]
    public void Build_WithConditionalDisableOverride_PreservesCondition()
    {
        // Arrange
        var overrides = CognitionOverrides.FromList(
            new DisableHandlerOverride
            {
                Stage = CognitionStages.Storage,
                HandlerId = "store_memory",
                Condition = "${situation.combat_mode}"
            });

        // Act
        var pipeline = _builder.Build(CognitionTemplates.HumanoidBase, overrides);
        Assert.NotNull(pipeline);

        // Assert - Handler is still enabled (condition evaluated at runtime)
        var handler = GetHandler(pipeline, CognitionStages.Storage, "store_memory");
        Assert.True(handler.IsEnabled); // Not unconditionally disabled
    }

    // =========================================================================
    // ADD HANDLER OVERRIDE TESTS
    // =========================================================================

    [Fact]
    public void Build_WithAddHandlerOverride_AddsHandler()
    {
        // Arrange
        var overrides = CognitionOverrides.FromList(
            new AddHandlerOverride
            {
                Stage = CognitionStages.Significance,
                Handler = new CognitionHandlerDefinition
                {
                    Id = "paranoia_boost",
                    HandlerName = "paranoia_significance_boost",
                    Parameters = new Dictionary<string, object> { ["multiplier"] = 1.5f }
                }
            });

        // Act
        var pipeline = _builder.Build(CognitionTemplates.HumanoidBase, overrides);
        Assert.NotNull(pipeline);

        // Assert
        var stage = pipeline.Stages.First(s => s.Name == CognitionStages.Significance);
        Assert.Contains(stage.Handlers, h => h.Id == "paranoia_boost");
    }

    [Fact]
    public void Build_WithAddHandlerOverride_AfterExisting_InsertsAfter()
    {
        // Arrange
        var overrides = CognitionOverrides.FromList(
            new AddHandlerOverride
            {
                Stage = CognitionStages.Intention,
                AfterId = "goal_impact",
                Handler = new CognitionHandlerDefinition
                {
                    Id = "custom_handler",
                    HandlerName = "custom_intention_handler",
                    Parameters = new Dictionary<string, object>()
                }
            });

        // Act
        var pipeline = _builder.Build(CognitionTemplates.HumanoidBase, overrides);
        Assert.NotNull(pipeline);

        // Assert
        var stage = pipeline.Stages.First(s => s.Name == CognitionStages.Intention);
        var goalImpactIndex = stage.Handlers.ToList().FindIndex(h => h.Id == "goal_impact");
        var customIndex = stage.Handlers.ToList().FindIndex(h => h.Id == "custom_handler");

        Assert.Equal(goalImpactIndex + 1, customIndex);
    }

    [Fact]
    public void Build_WithAddHandlerOverride_BeforeExisting_InsertsBefore()
    {
        // Arrange
        var overrides = CognitionOverrides.FromList(
            new AddHandlerOverride
            {
                Stage = CognitionStages.Intention,
                BeforeId = "goap_replan",
                Handler = new CognitionHandlerDefinition
                {
                    Id = "pre_replan_check",
                    HandlerName = "pre_replan_handler",
                    Parameters = new Dictionary<string, object>()
                }
            });

        // Act
        var pipeline = _builder.Build(CognitionTemplates.HumanoidBase, overrides);
        Assert.NotNull(pipeline);

        // Assert
        var stage = pipeline.Stages.First(s => s.Name == CognitionStages.Intention);
        var preReplanIndex = stage.Handlers.ToList().FindIndex(h => h.Id == "pre_replan_check");
        var goapReplanIndex = stage.Handlers.ToList().FindIndex(h => h.Id == "goap_replan");

        Assert.Equal(goapReplanIndex - 1, preReplanIndex);
    }

    [Fact]
    public void Build_WithAddHandlerOverride_NoPosition_AddsAtBeginning()
    {
        // Arrange
        var overrides = CognitionOverrides.FromList(
            new AddHandlerOverride
            {
                Stage = CognitionStages.Intention,
                Handler = new CognitionHandlerDefinition
                {
                    Id = "first_handler",
                    HandlerName = "first_intention_handler",
                    Parameters = new Dictionary<string, object>()
                }
            });

        // Act
        var pipeline = _builder.Build(CognitionTemplates.HumanoidBase, overrides);
        Assert.NotNull(pipeline);

        // Assert
        var stage = pipeline.Stages.First(s => s.Name == CognitionStages.Intention);
        Assert.Equal("first_handler", stage.Handlers[0].Id);
    }

    // =========================================================================
    // REPLACE HANDLER OVERRIDE TESTS
    // =========================================================================

    [Fact]
    public void Build_WithReplaceOverride_ReplacesHandler()
    {
        // Arrange
        var overrides = CognitionOverrides.FromList(
            new ReplaceHandlerOverride
            {
                Stage = CognitionStages.Filter,
                HandlerId = "attention_filter",
                NewHandler = new CognitionHandlerDefinition
                {
                    Id = "combat_attention_filter",
                    HandlerName = "combat_filter_attention",
                    Parameters = new Dictionary<string, object> { ["attention_budget"] = 300f }
                }
            });

        // Act
        var pipeline = _builder.Build(CognitionTemplates.HumanoidBase, overrides);
        Assert.NotNull(pipeline);

        // Assert
        var stage = pipeline.Stages.First(s => s.Name == CognitionStages.Filter);
        Assert.DoesNotContain(stage.Handlers, h => h.Id == "attention_filter");
        Assert.Contains(stage.Handlers, h => h.Id == "combat_attention_filter");

        var newHandler = stage.Handlers.First(h => h.Id == "combat_attention_filter");
        Assert.Equal("combat_filter_attention", newHandler.HandlerName);
        Assert.Equal(300f, newHandler.Parameters["attention_budget"]);
    }

    // =========================================================================
    // REORDER HANDLER OVERRIDE TESTS
    // =========================================================================

    [Fact]
    public void Build_WithReorderOverride_MovesHandler()
    {
        // Arrange - Move goap_replan before goal_impact
        var overrides = CognitionOverrides.FromList(
            new ReorderHandlerOverride
            {
                Stage = CognitionStages.Intention,
                HandlerId = "goap_replan",
                BeforeId = "goal_impact"
            });

        // Act
        var pipeline = _builder.Build(CognitionTemplates.HumanoidBase, overrides);
        Assert.NotNull(pipeline);

        // Assert
        var stage = pipeline.Stages.First(s => s.Name == CognitionStages.Intention);
        var goapIndex = stage.Handlers.ToList().FindIndex(h => h.Id == "goap_replan");
        var goalIndex = stage.Handlers.ToList().FindIndex(h => h.Id == "goal_impact");

        Assert.True(goapIndex < goalIndex);
    }

    // =========================================================================
    // VALIDATION TESTS
    // =========================================================================

    [Fact]
    public void ValidateOverrides_ValidOverrides_ReturnsValid()
    {
        // Arrange
        var overrides = CognitionOverrides.FromList(
            new ParameterOverride
            {
                Stage = CognitionStages.Filter,
                HandlerId = "attention_filter",
                Parameters = new Dictionary<string, object> { ["attention_budget"] = 200f }
            });

        // Act
        var result = _builder.ValidateOverrides(CognitionTemplates.HumanoidBase, overrides);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void ValidateOverrides_UnknownStage_ReturnsError()
    {
        // Arrange
        var overrides = CognitionOverrides.FromList(
            new ParameterOverride
            {
                Stage = "unknown_stage",
                HandlerId = "some_handler",
                Parameters = new Dictionary<string, object>()
            });

        // Act
        var result = _builder.ValidateOverrides(CognitionTemplates.HumanoidBase, overrides);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Stage not found"));
    }

    [Fact]
    public void ValidateOverrides_UnknownHandler_ReturnsError()
    {
        // Arrange
        var overrides = CognitionOverrides.FromList(
            new ParameterOverride
            {
                Stage = CognitionStages.Filter,
                HandlerId = "unknown_handler",
                Parameters = new Dictionary<string, object>()
            });

        // Act
        var result = _builder.ValidateOverrides(CognitionTemplates.HumanoidBase, overrides);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Handler not found"));
    }

    [Fact]
    public void ValidateOverrides_UnknownTemplate_ReturnsError()
    {
        // Arrange
        var overrides = CognitionOverrides.FromList();

        // Act
        var result = _builder.ValidateOverrides("unknown-template", overrides);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Template not found"));
    }

    [Fact]
    public void ValidateOverrides_AddWithInvalidAfterId_ReturnsWarning()
    {
        // Arrange
        var overrides = CognitionOverrides.FromList(
            new AddHandlerOverride
            {
                Stage = CognitionStages.Filter,
                AfterId = "nonexistent_handler",
                Handler = new CognitionHandlerDefinition
                {
                    Id = "new_handler",
                    HandlerName = "new_handler",
                    Parameters = new Dictionary<string, object>()
                }
            });

        // Act
        var result = _builder.ValidateOverrides(CognitionTemplates.HumanoidBase, overrides);

        // Assert
        Assert.True(result.IsValid); // Warning, not error
        Assert.Contains(result.Warnings, w => w.Contains("AfterId handler not found"));
    }

    // =========================================================================
    // COMPLEX SCENARIO TESTS
    // =========================================================================

    [Fact]
    public void Build_ParanoidArchetype_AppliesMultipleOverrides()
    {
        // Arrange - Paranoid archetype: higher threat sensitivity, lower threshold
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
                    ["threat_threshold"] = 0.6f  // Lower threshold = more fast-tracking
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
                        ["emotional"] = 0.6f  // Higher emotional weight
                    }
                }
            });

        // Act
        var pipeline = _builder.Build(CognitionTemplates.HumanoidBase, overrides);
        Assert.NotNull(pipeline);

        // Assert
        Assert.NotNull(pipeline);

        var filterHandler = GetHandler(pipeline, CognitionStages.Filter, "attention_filter");
        var weights = (Dictionary<string, object>)filterHandler.Parameters["priority_weights"];
        Assert.Equal(15.0f, weights["threat"]);
        Assert.Equal(0.6f, filterHandler.Parameters["threat_threshold"]);

        var sigHandler = GetHandler(pipeline, CognitionStages.Significance, "assess_significance");
        var sigWeights = (Dictionary<string, object>)sigHandler.Parameters["weights"];
        Assert.Equal(0.6f, sigWeights["emotional"]);
    }

    [Fact]
    public void Build_ObliviousArchetype_DisablesHandlers()
    {
        // Arrange - Oblivious archetype: limited perception, no memory storage
        var overrides = CognitionOverrides.FromList(
            new ParameterOverride
            {
                Stage = CognitionStages.Filter,
                HandlerId = "attention_filter",
                Parameters = new Dictionary<string, object>
                {
                    ["attention_budget"] = 30f,   // Very limited attention
                    ["max_perceptions"] = 3,       // Few perceptions
                    ["threat_fast_track"] = false  // No fast-track
                }
            },
            new DisableHandlerOverride
            {
                Stage = CognitionStages.Storage,
                HandlerId = "store_memory"  // Doesn't form new memories
            });

        // Act
        var pipeline = _builder.Build(CognitionTemplates.HumanoidBase, overrides);
        Assert.NotNull(pipeline);

        // Assert
        var filterHandler = GetHandler(pipeline, CognitionStages.Filter, "attention_filter");
        Assert.Equal(30f, filterHandler.Parameters["attention_budget"]);
        Assert.Equal(false, filterHandler.Parameters["threat_fast_track"]);

        var storeHandler = GetHandler(pipeline, CognitionStages.Storage, "store_memory");
        Assert.False(storeHandler.IsEnabled);
    }

    // =========================================================================
    // HELPER METHODS
    // =========================================================================

    private static ICognitionHandler GetHandler(ICognitionPipeline pipeline, string stageName, string handlerId)
    {
        var stage = pipeline.Stages.First(s => s.Name == stageName);
        return stage.Handlers.First(h => h.Id == handlerId);
    }
}
