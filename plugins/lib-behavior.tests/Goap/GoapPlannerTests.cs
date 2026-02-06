// ═══════════════════════════════════════════════════════════════════════════
// GoapPlanner Unit Tests
// Tests for GOAP A* planner implementation.
// ═══════════════════════════════════════════════════════════════════════════

using BeyondImmersion.Bannou.Behavior.Goap;
using Xunit;

using InternalGoapGoal = BeyondImmersion.Bannou.Behavior.Goap.GoapGoal;

namespace BeyondImmersion.BannouService.Behavior.Tests.Goap;

/// <summary>
/// Tests for GOAP A* planner.
/// </summary>
public class GoapPlannerTests
{
    private readonly GoapPlanner _planner;

    public GoapPlannerTests()
    {
        _planner = new GoapPlanner();
    }

    #region Simple Planning

    [Fact]
    public async Task PlanAsync_SingleAction_FindsPlan()
    {
        // Arrange
        var currentState = new WorldState()
            .SetNumeric("hunger", 0.8f);

        var goal = InternalGoapGoal.FromMetadata(
            "stay_fed",
            100,
            new Dictionary<string, string> { { "hunger", "<= 0.3" } });

        var actions = new List<GoapAction>
        {
            GoapAction.FromMetadata(
                "eat_meal",
                new Dictionary<string, string>(),
                new Dictionary<string, string> { { "hunger", "-0.6" } },
                cost: 1.0f)
        };

        // Act
        var plan = await _planner.PlanAsync(currentState, goal, actions);

        // Assert
        Assert.NotNull(plan);
        Assert.Single(plan.Actions);
        Assert.Equal("eat_meal", plan.Actions[0].Action.Id);
    }

    [Fact]
    public async Task PlanAsync_GoalAlreadySatisfied_ReturnsEmptyPlan()
    {
        // Arrange
        var currentState = new WorldState()
            .SetNumeric("hunger", 0.2f); // Already below threshold

        var goal = InternalGoapGoal.FromMetadata(
            "stay_fed",
            100,
            new Dictionary<string, string> { { "hunger", "<= 0.3" } });

        var actions = new List<GoapAction>
        {
            GoapAction.FromMetadata(
                "eat_meal",
                new Dictionary<string, string>(),
                new Dictionary<string, string> { { "hunger", "-0.5" } },
                cost: 1.0f)
        };

        // Act
        var plan = await _planner.PlanAsync(currentState, goal, actions);

        // Assert
        Assert.NotNull(plan);
        Assert.Empty(plan.Actions);
    }

    [Fact]
    public async Task PlanAsync_NoActions_ReturnsNull()
    {
        // Arrange
        var currentState = new WorldState()
            .SetNumeric("hunger", 0.8f);

        var goal = InternalGoapGoal.FromMetadata(
            "stay_fed",
            100,
            new Dictionary<string, string> { { "hunger", "<= 0.3" } });

        var actions = new List<GoapAction>();

        // Act
        var plan = await _planner.PlanAsync(currentState, goal, actions);

        // Assert
        Assert.Null(plan);
    }

    [Fact]
    public async Task PlanAsync_UnreachableGoal_ReturnsNull()
    {
        // Arrange
        var currentState = new WorldState()
            .SetNumeric("hunger", 0.8f);

        var goal = InternalGoapGoal.FromMetadata(
            "impossible",
            100,
            new Dictionary<string, string> { { "magic_power", ">= 100" } });

        var actions = new List<GoapAction>
        {
            GoapAction.FromMetadata(
                "eat_meal",
                new Dictionary<string, string>(),
                new Dictionary<string, string> { { "hunger", "-0.5" } },
                cost: 1.0f)
        };

        // Act
        var plan = await _planner.PlanAsync(currentState, goal, actions);

        // Assert
        Assert.Null(plan);
    }

    #endregion

    #region Multi-Step Planning

    [Fact]
    public async Task PlanAsync_TwoStepPlan_FindsSequence()
    {
        // Arrange
        var currentState = new WorldState()
            .SetNumeric("hunger", 0.8f)
            .SetNumeric("gold", 0);

        var goal = InternalGoapGoal.FromMetadata(
            "stay_fed",
            100,
            new Dictionary<string, string> { { "hunger", "<= 0.3" } });

        var actions = new List<GoapAction>
        {
            // Eating requires gold
            GoapAction.FromMetadata(
                "eat_meal",
                new Dictionary<string, string> { { "gold", ">= 5" } },
                new Dictionary<string, string> { { "hunger", "-0.6" }, { "gold", "-5" } },
                cost: 1.0f),
            // Working gives gold
            GoapAction.FromMetadata(
                "work",
                new Dictionary<string, string>(),
                new Dictionary<string, string> { { "gold", "+10" } },
                cost: 2.0f)
        };

        // Act
        var plan = await _planner.PlanAsync(currentState, goal, actions);

        // Assert
        Assert.NotNull(plan);
        Assert.Equal(2, plan.Actions.Count);
        Assert.Equal("work", plan.Actions[0].Action.Id);
        Assert.Equal("eat_meal", plan.Actions[1].Action.Id);
    }

    [Fact]
    public async Task PlanAsync_ThreeStepChain_FindsSequence()
    {
        // Arrange
        var currentState = new WorldState()
            .SetNumeric("level", 1);

        var goal = InternalGoapGoal.FromMetadata(
            "reach_level_4",
            100,
            new Dictionary<string, string> { { "level", ">= 4" } });

        var actions = new List<GoapAction>
        {
            GoapAction.FromMetadata(
                "level_up",
                new Dictionary<string, string>(),
                new Dictionary<string, string> { { "level", "+1" } },
                cost: 1.0f)
        };

        // Act
        var plan = await _planner.PlanAsync(currentState, goal, actions);

        // Assert
        Assert.NotNull(plan);
        Assert.Equal(3, plan.Actions.Count);
        Assert.All(plan.Actions, a => Assert.Equal("level_up", a.Action.Id));
    }

    #endregion

    #region Cost Optimization

    [Fact]
    public async Task PlanAsync_MultipleRoutes_ChoosesLowestCost()
    {
        // Arrange
        var currentState = new WorldState()
            .SetNumeric("gold", 0);

        var goal = InternalGoapGoal.FromMetadata(
            "earn_gold",
            100,
            new Dictionary<string, string> { { "gold", ">= 10" } });

        var actions = new List<GoapAction>
        {
            // Expensive way: 2 actions of cost 5 each
            GoapAction.FromMetadata(
                "expensive_work",
                new Dictionary<string, string>(),
                new Dictionary<string, string> { { "gold", "+5" } },
                cost: 5.0f),
            // Cheap way: 2 actions of cost 1 each
            GoapAction.FromMetadata(
                "cheap_work",
                new Dictionary<string, string>(),
                new Dictionary<string, string> { { "gold", "+5" } },
                cost: 1.0f)
        };

        // Act
        var plan = await _planner.PlanAsync(currentState, goal, actions);

        // Assert
        Assert.NotNull(plan);
        Assert.Equal(2, plan.Actions.Count);
        Assert.All(plan.Actions, a => Assert.Equal("cheap_work", a.Action.Id));
        Assert.Equal(2.0f, plan.TotalCost);
    }

    [Fact]
    public async Task PlanAsync_SingleExpensiveVsMultipleCheap_ChoosesCheaper()
    {
        // Arrange
        var currentState = new WorldState()
            .SetNumeric("gold", 0);

        var goal = InternalGoapGoal.FromMetadata(
            "earn_gold",
            100,
            new Dictionary<string, string> { { "gold", ">= 10" } });

        var actions = new List<GoapAction>
        {
            // One action but expensive
            GoapAction.FromMetadata(
                "big_job",
                new Dictionary<string, string>(),
                new Dictionary<string, string> { { "gold", "+10" } },
                cost: 10.0f),
            // Multiple cheap actions
            GoapAction.FromMetadata(
                "small_job",
                new Dictionary<string, string>(),
                new Dictionary<string, string> { { "gold", "+5" } },
                cost: 2.0f)
        };

        // Act
        var plan = await _planner.PlanAsync(currentState, goal, actions);

        // Assert
        Assert.NotNull(plan);
        // Should choose 2 small jobs (total cost 4) over 1 big job (cost 10)
        Assert.Equal(2, plan.Actions.Count);
        Assert.All(plan.Actions, a => Assert.Equal("small_job", a.Action.Id));
        Assert.Equal(4.0f, plan.TotalCost);
    }

    #endregion

    #region Preconditions

    [Fact]
    public async Task PlanAsync_PreconditionsNotMet_SkipsAction()
    {
        // Arrange
        var currentState = new WorldState()
            .SetNumeric("gold", 0);

        var goal = InternalGoapGoal.FromMetadata(
            "fed",
            100,
            new Dictionary<string, string> { { "fed", "== true" } });

        var actions = new List<GoapAction>
        {
            // Requires gold we don't have
            GoapAction.FromMetadata(
                "buy_food",
                new Dictionary<string, string> { { "gold", ">= 10" } },
                new Dictionary<string, string> { { "fed", "true" } },
                cost: 1.0f)
        };

        // Act
        var plan = await _planner.PlanAsync(currentState, goal, actions);

        // Assert
        Assert.Null(plan); // Can't afford to buy food
    }

    [Fact]
    public async Task PlanAsync_PreconditionsMet_UsesAction()
    {
        // Arrange
        var currentState = new WorldState()
            .SetNumeric("gold", 20);

        var goal = InternalGoapGoal.FromMetadata(
            "fed",
            100,
            new Dictionary<string, string> { { "fed", "== true" } });

        var actions = new List<GoapAction>
        {
            GoapAction.FromMetadata(
                "buy_food",
                new Dictionary<string, string> { { "gold", ">= 10" } },
                new Dictionary<string, string> { { "fed", "true" } },
                cost: 1.0f)
        };

        // Act
        var plan = await _planner.PlanAsync(currentState, goal, actions);

        // Assert
        Assert.NotNull(plan);
        Assert.Single(plan.Actions);
    }

    #endregion

    #region Limits and Options

    [Fact]
    public async Task PlanAsync_MaxDepthReached_ReturnsNull()
    {
        // Arrange
        var currentState = new WorldState()
            .SetNumeric("level", 1);

        var goal = InternalGoapGoal.FromMetadata(
            "reach_high_level",
            100,
            new Dictionary<string, string> { { "level", ">= 20" } });

        var actions = new List<GoapAction>
        {
            GoapAction.FromMetadata(
                "level_up",
                new Dictionary<string, string>(),
                new Dictionary<string, string> { { "level", "+1" } },
                cost: 1.0f)
        };

        var options = new PlanningOptions
        {
            MaxDepth = 5 // Too shallow for 19 level-ups
        };

        // Act
        var plan = await _planner.PlanAsync(currentState, goal, actions, options);

        // Assert
        Assert.Null(plan);
    }

    [Fact]
    public async Task PlanAsync_MaxNodesReached_ReturnsNull()
    {
        // Arrange
        var currentState = new WorldState()
            .SetNumeric("level", 1);

        var goal = InternalGoapGoal.FromMetadata(
            "reach_high_level",
            100,
            new Dictionary<string, string> { { "level", ">= 100" } });

        var actions = new List<GoapAction>
        {
            GoapAction.FromMetadata(
                "level_up",
                new Dictionary<string, string>(),
                new Dictionary<string, string> { { "level", "+1" } },
                cost: 1.0f)
        };

        var options = new PlanningOptions
        {
            MaxNodesExpanded = 10
        };

        // Act
        var plan = await _planner.PlanAsync(currentState, goal, actions, options);

        // Assert
        Assert.Null(plan);
    }

    [Fact]
    public async Task PlanAsync_CancellationRequested_StopsPlanning()
    {
        // Arrange
        var currentState = new WorldState()
            .SetNumeric("level", 1);

        var goal = InternalGoapGoal.FromMetadata(
            "reach_high_level",
            100,
            new Dictionary<string, string> { { "level", ">= 1000" } });

        var actions = new List<GoapAction>
        {
            GoapAction.FromMetadata(
                "level_up",
                new Dictionary<string, string>(),
                new Dictionary<string, string> { { "level", "+1" } },
                cost: 1.0f)
        };

        using var cts = new CancellationTokenSource();
        cts.Cancel(); // Cancel immediately

        // Act
        var plan = await _planner.PlanAsync(currentState, goal, actions, ct: cts.Token);

        // Assert
        Assert.Null(plan);
    }

    /// <summary>
    /// Verifies that planner respects TimeoutMs option.
    /// The planner should abort and return null when timeout is exceeded.
    /// </summary>
    /// <remarks>
    /// This test creates a scenario requiring many iterations and sets a very short timeout.
    /// The timeout value (1ms) is intentionally minimal to guarantee the planner cannot complete.
    /// </remarks>
    [Fact]
    public async Task PlanAsync_TimeoutReached_ReturnsNull()
    {
        // Arrange: Goal requiring many iterations that cannot complete in 1ms
        var currentState = new WorldState()
            .SetNumeric("level", 1);

        var goal = InternalGoapGoal.FromMetadata(
            "reach_high_level",
            100,
            new Dictionary<string, string> { { "level", ">= 10000" } });

        var actions = new List<GoapAction>
        {
            GoapAction.FromMetadata(
                "level_up",
                new Dictionary<string, string>(),
                new Dictionary<string, string> { { "level", "+1" } },
                cost: 1.0f)
        };

        var options = new PlanningOptions
        {
            TimeoutMs = 1, // 1ms timeout - impossible to complete
            MaxDepth = 20000, // Allow enough depth that timeout is the limiting factor
            MaxNodesExpanded = 100000 // Allow enough nodes that timeout is the limiting factor
        };

        // Act
        var plan = await _planner.PlanAsync(currentState, goal, actions, options);

        // Assert: Should return null due to timeout, not find a plan
        Assert.Null(plan);
    }

    /// <summary>
    /// Verifies that a reachable goal with adequate timeout succeeds.
    /// This contrasts with the timeout test to ensure timeout is the cause of failure.
    /// </summary>
    [Fact]
    public async Task PlanAsync_AdequateTimeout_FindsPlan()
    {
        // Arrange: Goal requiring few iterations that will complete quickly
        var currentState = new WorldState()
            .SetNumeric("level", 1);

        var goal = InternalGoapGoal.FromMetadata(
            "reach_level_5",
            100,
            new Dictionary<string, string> { { "level", ">= 5" } });

        var actions = new List<GoapAction>
        {
            GoapAction.FromMetadata(
                "level_up",
                new Dictionary<string, string>(),
                new Dictionary<string, string> { { "level", "+1" } },
                cost: 1.0f)
        };

        var options = new PlanningOptions
        {
            TimeoutMs = 10000, // 10 seconds - plenty of time
            MaxDepth = 100,
            MaxNodesExpanded = 1000
        };

        // Act
        var plan = await _planner.PlanAsync(currentState, goal, actions, options);

        // Assert: Should find a 4-action plan (1 → 5 requires 4 level-ups)
        Assert.NotNull(plan);
        Assert.Equal(4, plan.Actions.Count);
    }

    /// <summary>
    /// Verifies timeout test against CognitionConstants high-urgency timeout.
    /// High urgency uses 20ms timeout - verify a complex problem aborts within that window.
    /// </summary>
    /// <remarks>
    /// This test uses the actual high-urgency timeout value from CognitionConstants
    /// to verify realistic timeout behavior. A 10000-step problem cannot complete in 20ms.
    /// </remarks>
    [Fact]
    public async Task PlanAsync_HighUrgencyTimeout_AbortsLargeProblem()
    {
        // Arrange: Problem that cannot be solved in high-urgency timeframe
        var currentState = new WorldState()
            .SetNumeric("level", 1);

        var goal = InternalGoapGoal.FromMetadata(
            "reach_extremely_high_level",
            100,
            new Dictionary<string, string> { { "level", ">= 10000" } });

        var actions = new List<GoapAction>
        {
            GoapAction.FromMetadata(
                "level_up",
                new Dictionary<string, string>(),
                new Dictionary<string, string> { { "level", "+1" } },
                cost: 1.0f)
        };

        // Use actual high-urgency planning options from CognitionConstants
        var options = BeyondImmersion.BannouService.Abml.Cognition.UrgencyBasedPlanningOptions
            .FromUrgency(0.9f) // High urgency
            .ToPlanningOptions();

        // Act
        var plan = await _planner.PlanAsync(currentState, goal, actions, options);

        // Assert: Should return null - cannot solve 10000-step problem in 20ms with depth 3
        Assert.Null(plan);
    }

    [Fact]
    public async Task PlanAsync_TracksStatistics()
    {
        // Arrange
        var currentState = new WorldState()
            .SetNumeric("level", 1);

        var goal = InternalGoapGoal.FromMetadata(
            "level_up",
            100,
            new Dictionary<string, string> { { "level", ">= 3" } });

        var actions = new List<GoapAction>
        {
            GoapAction.FromMetadata(
                "level_up",
                new Dictionary<string, string>(),
                new Dictionary<string, string> { { "level", "+1" } },
                cost: 1.0f)
        };

        // Act
        var plan = await _planner.PlanAsync(currentState, goal, actions);

        // Assert
        Assert.NotNull(plan);
        Assert.True(plan.NodesExpanded > 0);
        Assert.True(plan.PlanningTimeMs >= 0);
    }

    #endregion

    #region Plan Validation

    [Fact]
    public async Task ValidatePlanAsync_ValidPlan_ReturnsContinue()
    {
        // Arrange
        var goal = InternalGoapGoal.FromMetadata(
            "test",
            100,
            new Dictionary<string, string> { { "done", "== true" } });

        var action = GoapAction.FromMetadata(
            "do_task",
            new Dictionary<string, string>(),
            new Dictionary<string, string> { { "done", "true" } },
            cost: 1.0f);

        var plan = new GoapPlan(
            goal: goal,
            actions: new List<PlannedAction> { new PlannedAction(action, 0) },
            totalCost: 1.0f,
            nodesExpanded: 1,
            planningTimeMs: 1,
            initialState: new WorldState(),
            expectedFinalState: new WorldState().SetBoolean("done", true));

        var currentState = new WorldState().SetBoolean("done", false);

        // Act
        var result = await _planner.ValidatePlanAsync(plan, 0, currentState);

        // Assert
        Assert.True(result.IsValid);
        Assert.Equal(ReplanReason.None, result.Reason);
        Assert.Equal(ValidationSuggestion.Continue, result.Suggestion);
    }

    [Fact]
    public async Task ValidatePlanAsync_PlanCompleted_ReturnsCompleted()
    {
        // Arrange
        var goal = InternalGoapGoal.FromMetadata(
            "test",
            100,
            new Dictionary<string, string> { { "done", "== true" } });

        var action = GoapAction.FromMetadata(
            "do_task",
            new Dictionary<string, string>(),
            new Dictionary<string, string> { { "done", "true" } },
            cost: 1.0f);

        var plan = new GoapPlan(
            goal: goal,
            actions: new List<PlannedAction> { new PlannedAction(action, 0) },
            totalCost: 1.0f,
            nodesExpanded: 1,
            planningTimeMs: 1,
            initialState: new WorldState(),
            expectedFinalState: new WorldState().SetBoolean("done", true));

        var currentState = new WorldState().SetBoolean("done", false);

        // Act - current action index beyond plan length
        var result = await _planner.ValidatePlanAsync(plan, 1, currentState);

        // Assert
        Assert.True(result.IsValid);
        Assert.Equal(ReplanReason.PlanCompleted, result.Reason);
        Assert.Equal(ValidationSuggestion.Abort, result.Suggestion);
    }

    [Fact]
    public async Task ValidatePlanAsync_GoalAlreadySatisfied_ReturnsGoalSatisfied()
    {
        // Arrange
        var goal = InternalGoapGoal.FromMetadata(
            "test",
            100,
            new Dictionary<string, string> { { "done", "== true" } });

        var action = GoapAction.FromMetadata(
            "do_task",
            new Dictionary<string, string>(),
            new Dictionary<string, string> { { "done", "true" } },
            cost: 1.0f);

        var plan = new GoapPlan(
            goal: goal,
            actions: new List<PlannedAction> { new PlannedAction(action, 0) },
            totalCost: 1.0f,
            nodesExpanded: 1,
            planningTimeMs: 1,
            initialState: new WorldState(),
            expectedFinalState: new WorldState().SetBoolean("done", true));

        var currentState = new WorldState().SetBoolean("done", true); // Already done!

        // Act
        var result = await _planner.ValidatePlanAsync(plan, 0, currentState);

        // Assert
        Assert.True(result.IsValid);
        Assert.Equal(ReplanReason.GoalAlreadySatisfied, result.Reason);
        Assert.Equal(ValidationSuggestion.Abort, result.Suggestion);
    }

    [Fact]
    public async Task ValidatePlanAsync_PreconditionInvalidated_ReturnsReplan()
    {
        // Arrange
        var goal = InternalGoapGoal.FromMetadata(
            "fed",
            100,
            new Dictionary<string, string> { { "fed", "== true" } });

        var action = GoapAction.FromMetadata(
            "buy_food",
            new Dictionary<string, string> { { "gold", ">= 10" } }, // Requires gold
            new Dictionary<string, string> { { "fed", "true" } },
            cost: 1.0f);

        var plan = new GoapPlan(
            goal: goal,
            actions: new List<PlannedAction> { new PlannedAction(action, 0) },
            totalCost: 1.0f,
            nodesExpanded: 1,
            planningTimeMs: 1,
            initialState: new WorldState().SetNumeric("gold", 10),
            expectedFinalState: new WorldState().SetBoolean("fed", true));

        var currentState = new WorldState().SetNumeric("gold", 0); // Lost gold!

        // Act
        var result = await _planner.ValidatePlanAsync(plan, 0, currentState);

        // Assert
        Assert.False(result.IsValid);
        Assert.Equal(ReplanReason.PreconditionInvalidated, result.Reason);
        Assert.Equal(ValidationSuggestion.Replan, result.Suggestion);
    }

    [Fact]
    public async Task ValidatePlanAsync_HigherPriorityGoal_ReturnsReplan()
    {
        // Arrange
        var lowPriorityGoal = InternalGoapGoal.FromMetadata(
            "earn_money",
            50,
            new Dictionary<string, string> { { "gold", ">= 100" } });

        var highPriorityGoal = InternalGoapGoal.FromMetadata(
            "survive",
            100,
            new Dictionary<string, string> { { "health", ">= 50" } });

        var action = GoapAction.FromMetadata(
            "work",
            new Dictionary<string, string>(),
            new Dictionary<string, string> { { "gold", "+10" } },
            cost: 1.0f);

        var plan = new GoapPlan(
            goal: lowPriorityGoal,
            actions: new List<PlannedAction> { new PlannedAction(action, 0) },
            totalCost: 1.0f,
            nodesExpanded: 1,
            planningTimeMs: 1,
            initialState: new WorldState(),
            expectedFinalState: new WorldState().SetNumeric("gold", 100));

        var currentState = new WorldState()
            .SetNumeric("gold", 50)
            .SetNumeric("health", 30); // Health is low!

        var activeGoals = new List<InternalGoapGoal> { lowPriorityGoal, highPriorityGoal };

        // Act
        var result = await _planner.ValidatePlanAsync(plan, 0, currentState, activeGoals);

        // Assert
        Assert.False(result.IsValid);
        Assert.Equal(ReplanReason.BetterGoalAvailable, result.Reason);
        Assert.Equal(ValidationSuggestion.Replan, result.Suggestion);
        Assert.Equal(highPriorityGoal, result.BetterGoal);
    }

    #endregion
}
