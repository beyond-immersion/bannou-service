// =============================================================================
// Assess Significance Handler Unit Tests
// Tests for significance assessment (Cognition Stage 3).
// =============================================================================

using BeyondImmersion.Bannou.BehaviorCompiler.Documents.Actions;
using BeyondImmersion.BannouService.Abml.Cognition;
using BeyondImmersion.BannouService.Abml.Cognition.Handlers;
using BeyondImmersion.BannouService.Abml.Execution;
using Xunit;

namespace BeyondImmersion.BannouService.Behavior.Tests.Handlers;

/// <summary>
/// Unit tests for AssessSignificanceHandler.
/// </summary>
public class AssessSignificanceHandlerTests : CognitionHandlerTestBase
{
    private readonly AssessSignificanceHandler _handler = new();

    #region CanHandle Tests

    [Fact]
    public void CanHandle_AssessSignificanceAction_ReturnsTrue()
    {
        var action = CreateDomainAction("assess_significance", new Dictionary<string, object?>());

        var result = _handler.CanHandle(action);

        Assert.True(result);
    }

    [Fact]
    public void CanHandle_OtherAction_ReturnsFalse()
    {
        var action = CreateDomainAction("other_action", new Dictionary<string, object?>());

        var result = _handler.CanHandle(action);

        Assert.False(result);
    }

    [Fact]
    public void CanHandle_NonDomainAction_ReturnsFalse()
    {
        var action = new SetAction("var", "value");

        var result = _handler.CanHandle(action);

        Assert.False(result);
    }

    #endregion

    #region ExecuteAsync Tests - Validation

    [Fact]
    public async Task ExecuteAsync_MissingPerception_ThrowsInvalidOperationException()
    {
        var action = CreateDomainAction("assess_significance", new Dictionary<string, object?>());
        var context = CreateTestContext();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _handler.ExecuteAsync(action, context, CancellationToken.None).AsTask());
    }

    [Fact]
    public async Task ExecuteAsync_NullPerception_ThrowsInvalidOperationException()
    {
        var action = CreateDomainAction("assess_significance", new Dictionary<string, object?>
        {
            { "perception", null }
        });
        var context = CreateTestContext();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _handler.ExecuteAsync(action, context, CancellationToken.None).AsTask());
    }

    #endregion

    #region ExecuteAsync Tests - Emotional Impact

    [Fact]
    public async Task ExecuteAsync_ThreatPerception_HighEmotionalImpact()
    {
        var perception = CreatePerception("threat", "Danger!", 0.9f);
        var action = CreateDomainAction("assess_significance", new Dictionary<string, object?>
        {
            { "perception", perception }
        });
        var context = CreateTestContext();

        await _handler.ExecuteAsync(action, context, CancellationToken.None);

        var score = GetScopeValue<SignificanceScore>(context, "significance_score");
        Assert.NotNull(score);
        Assert.True(score.EmotionalImpact > 0.8f, $"Expected high emotional impact for threat, got {score.EmotionalImpact}");
    }

    [Fact]
    public async Task ExecuteAsync_RoutinePerception_LowEmotionalImpact()
    {
        var perception = CreatePerception("routine", "Normal day", 0.1f);
        var action = CreateDomainAction("assess_significance", new Dictionary<string, object?>
        {
            { "perception", perception }
        });
        var context = CreateTestContext();

        await _handler.ExecuteAsync(action, context, CancellationToken.None);

        var score = GetScopeValue<SignificanceScore>(context, "significance_score");
        Assert.NotNull(score);
        Assert.True(score.EmotionalImpact < 0.3f, $"Expected low emotional impact for routine, got {score.EmotionalImpact}");
    }

    [Fact]
    public async Task ExecuteAsync_AnxiousPersonality_AmplifiedThreatResponse()
    {
        var perception = CreatePerception("threat", "Danger!", 0.5f);
        var personality = new Dictionary<string, object?>
        {
            { "anxious", 0.8f }
        };

        var action = CreateDomainAction("assess_significance", new Dictionary<string, object?>
        {
            { "perception", perception },
            { "personality", personality }
        });
        var context = CreateTestContext();

        await _handler.ExecuteAsync(action, context, CancellationToken.None);

        var score = GetScopeValue<SignificanceScore>(context, "significance_score");
        Assert.NotNull(score);
        // Anxious personality should amplify threat response
        Assert.True(score.EmotionalImpact > 0.6f);
    }

    [Fact]
    public async Task ExecuteAsync_CuriousPersonality_AmplifiedNoveltyResponse()
    {
        var perception = CreatePerception("novelty", "Something new!", 0.5f);
        var personality = new Dictionary<string, object?>
        {
            { "curious", 0.9f }
        };

        var action = CreateDomainAction("assess_significance", new Dictionary<string, object?>
        {
            { "perception", perception },
            { "personality", personality }
        });
        var context = CreateTestContext();

        await _handler.ExecuteAsync(action, context, CancellationToken.None);

        var score = GetScopeValue<SignificanceScore>(context, "significance_score");
        Assert.NotNull(score);
        // Curious personality should amplify novelty response
        Assert.True(score.EmotionalImpact > 0.5f);
    }

    #endregion

    #region ExecuteAsync Tests - Goal Relevance

    [Fact]
    public async Task ExecuteAsync_NoGoals_BaseGoalRelevance()
    {
        var perception = CreatePerception("routine", "Something happened", 0.5f);
        var action = CreateDomainAction("assess_significance", new Dictionary<string, object?>
        {
            { "perception", perception }
        });
        var context = CreateTestContext();

        await _handler.ExecuteAsync(action, context, CancellationToken.None);

        var score = GetScopeValue<SignificanceScore>(context, "significance_score");
        Assert.NotNull(score);
        // Base relevance when no goals specified
        Assert.True(score.GoalRelevance >= 0.2f && score.GoalRelevance <= 0.5f);
    }

    [Fact]
    public async Task ExecuteAsync_ContentMatchesGoal_HighGoalRelevance()
    {
        var perception = CreatePerception("routine", "Found the treasure chest", 0.5f);
        var goals = new List<string> { "treasure" };

        var action = CreateDomainAction("assess_significance", new Dictionary<string, object?>
        {
            { "perception", perception },
            { "current_goals", goals }
        });
        var context = CreateTestContext();

        await _handler.ExecuteAsync(action, context, CancellationToken.None);

        var score = GetScopeValue<SignificanceScore>(context, "significance_score");
        Assert.NotNull(score);
        Assert.True(score.GoalRelevance > 0.5f);
    }

    [Fact]
    public async Task ExecuteAsync_ThreatWithSurvivalGoal_VeryHighGoalRelevance()
    {
        var perception = CreatePerception("threat", "Enemy attacking!", 0.9f);
        var goals = new List<string> { "survive" };

        var action = CreateDomainAction("assess_significance", new Dictionary<string, object?>
        {
            { "perception", perception },
            { "current_goals", goals }
        });
        var context = CreateTestContext();

        await _handler.ExecuteAsync(action, context, CancellationToken.None);

        var score = GetScopeValue<SignificanceScore>(context, "significance_score");
        Assert.NotNull(score);
        Assert.True(score.GoalRelevance > 0.8f);
    }

    #endregion

    #region ExecuteAsync Tests - Relationship Factor

    [Fact]
    public async Task ExecuteAsync_NoRelationships_BaseRelationshipFactor()
    {
        var perception = CreatePerception("social", "Met someone", 0.5f, "stranger");
        var action = CreateDomainAction("assess_significance", new Dictionary<string, object?>
        {
            { "perception", perception }
        });
        var context = CreateTestContext();

        await _handler.ExecuteAsync(action, context, CancellationToken.None);

        var score = GetScopeValue<SignificanceScore>(context, "significance_score");
        Assert.NotNull(score);
        // Social perceptions have higher base relationship factor
        Assert.True(score.RelationshipFactor >= 0.2f);
    }

    [Fact]
    public async Task ExecuteAsync_KnownRelationship_HigherRelationshipFactor()
    {
        var perception = CreatePerception("social", "Friend waved", 0.5f, "alice");
        var relationships = new Dictionary<string, object?>
        {
            { "alice", 0.9f }
        };

        var action = CreateDomainAction("assess_significance", new Dictionary<string, object?>
        {
            { "perception", perception },
            { "relationships", relationships }
        });
        var context = CreateTestContext();

        await _handler.ExecuteAsync(action, context, CancellationToken.None);

        var score = GetScopeValue<SignificanceScore>(context, "significance_score");
        Assert.NotNull(score);
        Assert.True(score.RelationshipFactor > 0.8f);
    }

    [Fact]
    public async Task ExecuteAsync_RelationshipWithStrengthObject_ExtractsStrength()
    {
        var perception = CreatePerception("social", "Enemy appeared", 0.5f, "bob");
        var relationships = new Dictionary<string, object?>
        {
            { "bob", new Dictionary<string, object?> { { "strength", -0.8f } } }
        };

        var action = CreateDomainAction("assess_significance", new Dictionary<string, object?>
        {
            { "perception", perception },
            { "relationships", relationships }
        });
        var context = CreateTestContext();

        await _handler.ExecuteAsync(action, context, CancellationToken.None);

        var score = GetScopeValue<SignificanceScore>(context, "significance_score");
        Assert.NotNull(score);
        // Absolute value of relationship strength
        Assert.True(score.RelationshipFactor > 0.7f);
    }

    #endregion

    #region ExecuteAsync Tests - Weighted Score

    [Fact]
    public async Task ExecuteAsync_DefaultWeights_ComputesCorrectScore()
    {
        var perception = CreatePerception("threat", "Critical threat!", 0.9f);
        var goals = new List<string> { "survive" };

        var action = CreateDomainAction("assess_significance", new Dictionary<string, object?>
        {
            { "perception", perception },
            { "current_goals", goals }
        });
        var context = CreateTestContext();

        await _handler.ExecuteAsync(action, context, CancellationToken.None);

        var score = GetScopeValue<SignificanceScore>(context, "significance_score");
        Assert.NotNull(score);
        // Total score should be weighted average
        Assert.True(score.TotalScore > 0.5f);
        Assert.Equal(0.7f, score.StorageThreshold);
    }

    [Fact]
    public async Task ExecuteAsync_CustomWeights_AppliesCorrectly()
    {
        var perception = CreatePerception("routine", "Walking", 0.1f);
        var weights = new Dictionary<string, object?>
        {
            { "emotional", 0.1f },
            { "goal_relevance", 0.1f },
            { "relationship", 0.8f }  // Heavy weight on relationship
        };

        var action = CreateDomainAction("assess_significance", new Dictionary<string, object?>
        {
            { "perception", perception },
            { "weights", weights }
        });
        var context = CreateTestContext();

        await _handler.ExecuteAsync(action, context, CancellationToken.None);

        var score = GetScopeValue<SignificanceScore>(context, "significance_score");
        Assert.NotNull(score);
        // With heavy relationship weight and no relationships, score should be lower
        Assert.True(score.TotalScore < 0.5f);
    }

    [Fact]
    public async Task ExecuteAsync_CustomThreshold_UsesProvidedThreshold()
    {
        var perception = CreatePerception("routine", "Walking", 0.1f);

        var action = CreateDomainAction("assess_significance", new Dictionary<string, object?>
        {
            { "perception", perception },
            { "threshold", 0.5f }
        });
        var context = CreateTestContext();

        await _handler.ExecuteAsync(action, context, CancellationToken.None);

        var score = GetScopeValue<SignificanceScore>(context, "significance_score");
        Assert.NotNull(score);
        Assert.Equal(0.5f, score.StorageThreshold);
    }

    #endregion

    #region ExecuteAsync Tests - Result Variable

    [Fact]
    public async Task ExecuteAsync_DefaultResultVariable_StoresAsSignificanceScore()
    {
        var perception = CreatePerception();
        var action = CreateDomainAction("assess_significance", new Dictionary<string, object?>
        {
            { "perception", perception }
        });
        var context = CreateTestContext();

        await _handler.ExecuteAsync(action, context, CancellationToken.None);

        var score = GetScopeValue<SignificanceScore>(context, "significance_score");
        Assert.NotNull(score);
    }

    [Fact]
    public async Task ExecuteAsync_CustomResultVariable_UsesCustomName()
    {
        var perception = CreatePerception();
        var action = CreateDomainAction("assess_significance", new Dictionary<string, object?>
        {
            { "perception", perception },
            { "result_variable", "my_score" }
        });
        var context = CreateTestContext();

        await _handler.ExecuteAsync(action, context, CancellationToken.None);

        var score = GetScopeValue<SignificanceScore>(context, "my_score");
        Assert.NotNull(score);
    }

    #endregion

    #region ExecuteAsync Tests - Input Conversion

    [Fact]
    public async Task ExecuteAsync_DictionaryPerception_ConvertsCorrectly()
    {
        var dictPerception = new Dictionary<string, object?>
        {
            { "category", "threat" },
            { "content", "Enemy spotted" },
            { "urgency", 0.8f }
        };

        var action = CreateDomainAction("assess_significance", new Dictionary<string, object?>
        {
            { "perception", dictPerception }
        });
        var context = CreateTestContext();

        var result = await _handler.ExecuteAsync(action, context, CancellationToken.None);

        Assert.Equal(ActionResult.Continue, result);
        var score = GetScopeValue<SignificanceScore>(context, "significance_score");
        Assert.NotNull(score);
        // Threat should have high emotional impact
        Assert.True(score.EmotionalImpact > 0.7f);
    }

    #endregion
}
