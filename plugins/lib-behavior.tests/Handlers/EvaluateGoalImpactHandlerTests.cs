// =============================================================================
// Evaluate Goal Impact Handler Unit Tests
// Tests for goal impact evaluation (Cognition Stage 5).
// =============================================================================

using BeyondImmersion.BannouService.Abml.Cognition;
using BeyondImmersion.BannouService.Abml.Cognition.Handlers;
using BeyondImmersion.BannouService.Abml.Documents.Actions;
using BeyondImmersion.BannouService.Abml.Execution;
using Xunit;

namespace BeyondImmersion.BannouService.Behavior.Tests.Handlers;

/// <summary>
/// Unit tests for EvaluateGoalImpactHandler.
/// </summary>
public class EvaluateGoalImpactHandlerTests : CognitionHandlerTestBase
{
    private readonly EvaluateGoalImpactHandler _handler = new();

    #region CanHandle Tests

    [Fact]
    public void CanHandle_EvaluateGoalImpactAction_ReturnsTrue()
    {
        var action = CreateDomainAction("evaluate_goal_impact", new Dictionary<string, object?>());

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

    #region ExecuteAsync Tests - No Input

    [Fact]
    public async Task ExecuteAsync_NoPerceptions_DoesNotRequireReplan()
    {
        var action = CreateDomainAction("evaluate_goal_impact", new Dictionary<string, object?>
        {
            { "perceptions", new List<Perception>() },
            { "current_goals", new List<string> { "survive" } }
        });
        var context = CreateTestContext();

        await _handler.ExecuteAsync(action, context, CancellationToken.None);

        var result = GetScopeValue<GoalImpactResult>(context, "goal_updates");
        Assert.NotNull(result);
        Assert.False(result.RequiresReplan);
        Assert.Empty(result.AffectedGoals);
    }

    [Fact]
    public async Task ExecuteAsync_NoGoals_DoesNotRequireReplan()
    {
        var perceptions = new List<Perception>
        {
            CreatePerception("threat", "Danger!", 0.9f)
        };
        var action = CreateDomainAction("evaluate_goal_impact", new Dictionary<string, object?>
        {
            { "perceptions", perceptions },
            { "current_goals", null }
        });
        var context = CreateTestContext();

        await _handler.ExecuteAsync(action, context, CancellationToken.None);

        var result = GetScopeValue<GoalImpactResult>(context, "goal_updates");
        Assert.NotNull(result);
        Assert.False(result.RequiresReplan);
    }

    #endregion

    #region ExecuteAsync Tests - Threat Impact

    [Fact]
    public async Task ExecuteAsync_ThreatPerception_RequiresReplan()
    {
        var perceptions = new List<Perception>
        {
            CreatePerception("threat", "Enemy spotted!", 0.8f)
        };
        var goals = new List<string> { "survive" };

        var action = CreateDomainAction("evaluate_goal_impact", new Dictionary<string, object?>
        {
            { "perceptions", perceptions },
            { "current_goals", goals }
        });
        var context = CreateTestContext();

        await _handler.ExecuteAsync(action, context, CancellationToken.None);

        var result = GetScopeValue<GoalImpactResult>(context, "goal_updates");
        Assert.NotNull(result);
        Assert.True(result.RequiresReplan);
        Assert.Contains("survive", result.AffectedGoals);
    }

    [Fact]
    public async Task ExecuteAsync_ThreatWithSafetyGoal_VeryHighImpact()
    {
        var perceptions = new List<Perception>
        {
            CreatePerception("threat", "Critical danger!", 0.9f)
        };
        var goals = new List<Dictionary<string, object?>>
        {
            new() { { "id", "goal-1" }, { "name", "safety" }, { "priority", 90 } }
        };

        var action = CreateDomainAction("evaluate_goal_impact", new Dictionary<string, object?>
        {
            { "perceptions", perceptions },
            { "current_goals", goals }
        });
        var context = CreateTestContext();

        await _handler.ExecuteAsync(action, context, CancellationToken.None);

        var result = GetScopeValue<GoalImpactResult>(context, "goal_updates");
        Assert.NotNull(result);
        Assert.True(result.RequiresReplan);
        Assert.True(result.Urgency > 0.7f);
    }

    [Fact]
    public async Task ExecuteAsync_ThreatWithNonSurvivalGoal_StillImpacts()
    {
        var perceptions = new List<Perception>
        {
            CreatePerception("threat", "Enemy nearby", 0.7f)
        };
        var goals = new List<string> { "find_treasure" };

        var action = CreateDomainAction("evaluate_goal_impact", new Dictionary<string, object?>
        {
            { "perceptions", perceptions },
            { "current_goals", goals }
        });
        var context = CreateTestContext();

        await _handler.ExecuteAsync(action, context, CancellationToken.None);

        var result = GetScopeValue<GoalImpactResult>(context, "goal_updates");
        Assert.NotNull(result);
        // Threats always trigger replan consideration
        Assert.True(result.RequiresReplan);
    }

    #endregion

    #region ExecuteAsync Tests - Social Impact

    [Fact]
    public async Task ExecuteAsync_SocialPerceptionWithRelationshipGoal_AffectsGoal()
    {
        var perceptions = new List<Perception>
        {
            CreatePerception("social", "Friend is angry", 0.7f)
        };
        var goals = new List<string> { "make_friends" };

        var action = CreateDomainAction("evaluate_goal_impact", new Dictionary<string, object?>
        {
            { "perceptions", perceptions },
            { "current_goals", goals }
        });
        var context = CreateTestContext();

        await _handler.ExecuteAsync(action, context, CancellationToken.None);

        var result = GetScopeValue<GoalImpactResult>(context, "goal_updates");
        Assert.NotNull(result);
        Assert.Contains("make_friends", result.AffectedGoals);
    }

    #endregion

    #region ExecuteAsync Tests - Content Matching

    [Fact]
    public async Task ExecuteAsync_PerceptionContentMatchesGoal_AffectsGoal()
    {
        var perceptions = new List<Perception>
        {
            CreatePerception("routine", "The treasure chest is open!", 0.5f)
        };
        var goals = new List<string> { "treasure" };

        var action = CreateDomainAction("evaluate_goal_impact", new Dictionary<string, object?>
        {
            { "perceptions", perceptions },
            { "current_goals", goals }
        });
        var context = CreateTestContext();

        await _handler.ExecuteAsync(action, context, CancellationToken.None);

        var result = GetScopeValue<GoalImpactResult>(context, "goal_updates");
        Assert.NotNull(result);
        Assert.Contains("treasure", result.AffectedGoals);
    }

    #endregion

    #region ExecuteAsync Tests - Plan Invalidation

    [Fact]
    public async Task ExecuteAsync_HighUrgencyThreat_InvalidatesPlan()
    {
        var perceptions = new List<Perception>
        {
            CreatePerception("threat", "Ambush!", 0.9f)
        };
        var goals = new List<string> { "explore" };
        var currentPlan = new { action = "walk_forward" };

        var action = CreateDomainAction("evaluate_goal_impact", new Dictionary<string, object?>
        {
            { "perceptions", perceptions },
            { "current_goals", goals },
            { "current_plan", currentPlan }
        });
        var context = CreateTestContext();

        await _handler.ExecuteAsync(action, context, CancellationToken.None);

        var result = GetScopeValue<GoalImpactResult>(context, "goal_updates");
        Assert.NotNull(result);
        Assert.True(result.RequiresReplan);
        // Threat already triggers replan, so it adds "Threat impacts goal" message
        Assert.Contains("Threat impacts goal", result.Message ?? "");
    }

    [Fact]
    public async Task ExecuteAsync_PerceptionWithInvalidatesPlanFlag_InvalidatesPlan()
    {
        var perception = CreatePerception("routine", "Path blocked", 0.5f,
            data: new Dictionary<string, object> { { "invalidates_plan", true } });
        var perceptions = new List<Perception> { perception };
        var goals = new List<string> { "walk_home" };
        var currentPlan = new { action = "walk_forward" };

        var action = CreateDomainAction("evaluate_goal_impact", new Dictionary<string, object?>
        {
            { "perceptions", perceptions },
            { "current_goals", goals },
            { "current_plan", currentPlan }
        });
        var context = CreateTestContext();

        await _handler.ExecuteAsync(action, context, CancellationToken.None);

        var result = GetScopeValue<GoalImpactResult>(context, "goal_updates");
        Assert.NotNull(result);
        Assert.True(result.RequiresReplan);
    }

    [Fact]
    public async Task ExecuteAsync_PerceptionWithBlockedFlag_InvalidatesPlan()
    {
        var perception = CreatePerception("routine", "Door locked", 0.5f,
            data: new Dictionary<string, object> { { "blocked", true } });
        var perceptions = new List<Perception> { perception };
        var goals = new List<string> { "enter_house" };
        var currentPlan = new { action = "open_door" };

        var action = CreateDomainAction("evaluate_goal_impact", new Dictionary<string, object?>
        {
            { "perceptions", perceptions },
            { "current_goals", goals },
            { "current_plan", currentPlan }
        });
        var context = CreateTestContext();

        await _handler.ExecuteAsync(action, context, CancellationToken.None);

        var result = GetScopeValue<GoalImpactResult>(context, "goal_updates");
        Assert.NotNull(result);
        Assert.True(result.RequiresReplan);
    }

    #endregion

    #region ExecuteAsync Tests - Urgency Calculation

    [Fact]
    public async Task ExecuteAsync_HighPriorityGoal_HigherUrgency()
    {
        var perceptions = new List<Perception>
        {
            CreatePerception("threat", "Danger!", 0.7f)
        };
        var highPriorityGoal = new List<Dictionary<string, object?>>
        {
            new() { { "id", "goal-1" }, { "name", "survive" }, { "priority", 100 } }
        };

        var action = CreateDomainAction("evaluate_goal_impact", new Dictionary<string, object?>
        {
            { "perceptions", perceptions },
            { "current_goals", highPriorityGoal }
        });
        var context = CreateTestContext();

        await _handler.ExecuteAsync(action, context, CancellationToken.None);

        var result = GetScopeValue<GoalImpactResult>(context, "goal_updates");
        Assert.NotNull(result);
        Assert.True(result.Urgency > 0.5f);
    }

    [Fact]
    public async Task ExecuteAsync_LowPriorityGoal_LowerUrgency()
    {
        var perceptions = new List<Perception>
        {
            CreatePerception("routine", "Something happened", 0.5f)
        };
        var lowPriorityGoal = new List<Dictionary<string, object?>>
        {
            new() { { "id", "goal-1" }, { "name", "Something happened in detail" }, { "priority", 10 } }
        };

        var action = CreateDomainAction("evaluate_goal_impact", new Dictionary<string, object?>
        {
            { "perceptions", perceptions },
            { "current_goals", lowPriorityGoal }
        });
        var context = CreateTestContext();

        await _handler.ExecuteAsync(action, context, CancellationToken.None);

        var result = GetScopeValue<GoalImpactResult>(context, "goal_updates");
        Assert.NotNull(result);
        Assert.True(result.Urgency < 0.3f);
    }

    #endregion

    #region ExecuteAsync Tests - Multiple Goals

    [Fact]
    public async Task ExecuteAsync_MultipleAffectedGoals_ListsAll()
    {
        var perceptions = new List<Perception>
        {
            CreatePerception("threat", "Everything is on fire!", 0.9f)
        };
        var goals = new List<string> { "survive", "protect_home", "stay_healthy" };

        var action = CreateDomainAction("evaluate_goal_impact", new Dictionary<string, object?>
        {
            { "perceptions", perceptions },
            { "current_goals", goals }
        });
        var context = CreateTestContext();

        await _handler.ExecuteAsync(action, context, CancellationToken.None);

        var result = GetScopeValue<GoalImpactResult>(context, "goal_updates");
        Assert.NotNull(result);
        // Threats affect all survival-related goals
        Assert.True(result.AffectedGoals.Count >= 2);
    }

    #endregion

    #region ExecuteAsync Tests - Result Variable

    [Fact]
    public async Task ExecuteAsync_DefaultResultVariable_StoresAsGoalUpdates()
    {
        var action = CreateDomainAction("evaluate_goal_impact", new Dictionary<string, object?>
        {
            { "perceptions", new List<Perception>() },
            { "current_goals", new List<string>() }
        });
        var context = CreateTestContext();

        await _handler.ExecuteAsync(action, context, CancellationToken.None);

        var result = GetScopeValue<GoalImpactResult>(context, "goal_updates");
        Assert.NotNull(result);
    }

    [Fact]
    public async Task ExecuteAsync_CustomResultVariable_UsesCustomName()
    {
        var action = CreateDomainAction("evaluate_goal_impact", new Dictionary<string, object?>
        {
            { "perceptions", new List<Perception>() },
            { "current_goals", new List<string>() },
            { "result_variable", "my_impact" }
        });
        var context = CreateTestContext();

        await _handler.ExecuteAsync(action, context, CancellationToken.None);

        var result = GetScopeValue<GoalImpactResult>(context, "my_impact");
        Assert.NotNull(result);
    }

    #endregion

    #region ExecuteAsync Tests - Goal Input Formats

    [Fact]
    public async Task ExecuteAsync_StringGoal_ParsesCorrectly()
    {
        var perceptions = new List<Perception>
        {
            CreatePerception("routine", "Found survival kit", 0.5f)
        };

        var action = CreateDomainAction("evaluate_goal_impact", new Dictionary<string, object?>
        {
            { "perceptions", perceptions },
            { "current_goals", "survival" }  // Single string
        });
        var context = CreateTestContext();

        await _handler.ExecuteAsync(action, context, CancellationToken.None);

        var result = GetScopeValue<GoalImpactResult>(context, "goal_updates");
        Assert.NotNull(result);
        Assert.Contains("survival", result.AffectedGoals);
    }

    [Fact]
    public async Task ExecuteAsync_GoalObjectsWithPriority_UsesCorrectly()
    {
        var perceptions = new List<Perception>
        {
            CreatePerception("threat", "Danger!", 0.8f)
        };
        var goals = new List<object>
        {
            new Dictionary<string, object?>
            {
                { "id", "goal-1" },
                { "name", "survive" },
                { "priority", 95 }
            }
        };

        var action = CreateDomainAction("evaluate_goal_impact", new Dictionary<string, object?>
        {
            { "perceptions", perceptions },
            { "current_goals", goals }
        });
        var context = CreateTestContext();

        await _handler.ExecuteAsync(action, context, CancellationToken.None);

        var result = GetScopeValue<GoalImpactResult>(context, "goal_updates");
        Assert.NotNull(result);
        Assert.True(result.Urgency > 0.7f);  // High priority with high impact
    }

    #endregion
}
