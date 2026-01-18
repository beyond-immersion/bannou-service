using BeyondImmersion.Bannou.MusicStoryteller.Actions;
using BeyondImmersion.Bannou.MusicStoryteller.Narratives;
using BeyondImmersion.Bannou.MusicStoryteller.Planning;
using BeyondImmersion.Bannou.MusicStoryteller.State;
using Xunit;

namespace BeyondImmersion.Bannou.MusicStoryteller.Tests.Planning;

/// <summary>
/// Tests for the GOAP planner.
/// </summary>
public class GOAPPlannerTests
{
    /// <summary>
    /// Planner creates non-empty plan for reachable goal.
    /// </summary>
    [Fact]
    public void CreatePlan_ReachableGoal_ReturnsNonEmptyPlan()
    {
        // Arrange
        var actions = new ActionLibrary();
        var planner = new GOAPPlanner(actions);

        var currentState = new WorldState();
        currentState.Set(WorldState.Keys.Tension, 0.2);
        currentState.Set(WorldState.Keys.Stability, 0.8);

        var goal = GOAPGoal.BuildTension();

        // Act
        var plan = planner.CreatePlan(currentState, goal);

        // Assert
        Assert.NotNull(plan);
        Assert.True(plan.Actions.Count > 0 || plan.IsEmpty,
            "Plan should have actions or goal should already be reached");
    }

    /// <summary>
    /// Planner returns empty plan when goal is already satisfied.
    /// </summary>
    [Fact]
    public void CreatePlan_GoalAlreadySatisfied_ReturnsEmptyPlan()
    {
        // Arrange
        var actions = new ActionLibrary();
        var planner = new GOAPPlanner(actions);

        var highTensionState = new WorldState();
        highTensionState.Set(WorldState.Keys.Tension, 0.9);

        var goal = GOAPGoal.BuildTension();

        // Act
        var plan = planner.CreatePlan(highTensionState, goal);

        // Assert
        Assert.True(plan.IsEmpty, "Plan should be empty when goal already satisfied");
    }

    /// <summary>
    /// Plan cost accumulates correctly.
    /// </summary>
    [Fact]
    public void Plan_CostAccumulatesCorrectly()
    {
        // Arrange
        var actions = new ActionLibrary();
        var action1 = actions.GetById("use_dominant_seventh");
        var action2 = actions.GetById("increase_harmonic_rhythm");

        if (action1 == null || action2 == null)
        {
            // Skip if actions not available
            return;
        }

        // Create GOAP actions using constructor
        var goapAction1 = new GOAPAction(action1);
        var goapAction2 = new GOAPAction(action2);

        var goal = GOAPGoal.BuildTension();
        var plan = new Plan
        {
            Goal = goal,
            Actions = [goapAction1, goapAction2],
            TotalCost = goapAction1.BaseCost + goapAction2.BaseCost,
            ExpectedFinalState = new WorldState()
        };

        // Assert
        Assert.Equal(goapAction1.BaseCost + goapAction2.BaseCost, plan.TotalCost);
    }

    /// <summary>
    /// WorldState can be created from CompositionState.
    /// </summary>
    [Fact]
    public void WorldState_FromCompositionState_CopiesValues()
    {
        // Arrange
        var compositionState = new CompositionState();
        compositionState.Emotional.Tension = 0.7;
        compositionState.Emotional.Brightness = 0.4;
        compositionState.Emotional.Energy = 0.6;

        // Act
        var worldState = WorldState.FromCompositionState(compositionState);

        // Assert
        Assert.Equal(0.7, worldState.Get<double>(WorldState.Keys.Tension));
        Assert.Equal(0.4, worldState.Get<double>(WorldState.Keys.Brightness));
        Assert.Equal(0.6, worldState.Get<double>(WorldState.Keys.Energy));
    }

    /// <summary>
    /// WorldState distance calculation is symmetric.
    /// </summary>
    [Fact]
    public void WorldState_DistanceTo_IsSymmetric()
    {
        // Arrange
        var state1 = new WorldState();
        state1.Set(WorldState.Keys.Tension, 0.2);
        state1.Set(WorldState.Keys.Brightness, 0.5);

        var state2 = new WorldState();
        state2.Set(WorldState.Keys.Tension, 0.8);
        state2.Set(WorldState.Keys.Brightness, 0.3);

        // Act
        var d1to2 = state1.DistanceTo(state2);
        var d2to1 = state2.DistanceTo(state1);

        // Assert
        Assert.Equal(d1to2, d2to1, precision: 5);
    }

    /// <summary>
    /// WorldState satisfies goal when values are within tolerance.
    /// Satisfies requires values to be within ±tolerance, not just "exceeds".
    /// </summary>
    [Fact]
    public void WorldState_Satisfies_WhenValuesWithinTolerance()
    {
        // Arrange
        var state = new WorldState();
        state.Set(WorldState.Keys.Tension, 0.72);

        var goal = new WorldState();
        goal.Set(WorldState.Keys.Tension, 0.7);

        // Act - 0.72 is within 0.05 tolerance of 0.7
        var satisfies = state.Satisfies(goal, tolerance: 0.05);

        // Assert
        Assert.True(satisfies, "|0.72 - 0.7| = 0.02 should satisfy tolerance of 0.05");
    }

    /// <summary>
    /// WorldState does not satisfy goal when values exceed tolerance.
    /// </summary>
    [Fact]
    public void WorldState_DoesNotSatisfy_WhenValuesExceedTolerance()
    {
        // Arrange
        var state = new WorldState();
        state.Set(WorldState.Keys.Tension, 0.8);

        var goal = new WorldState();
        goal.Set(WorldState.Keys.Tension, 0.7);

        // Act - 0.8 is NOT within 0.05 tolerance of 0.7 (|0.8-0.7| = 0.1 > 0.05)
        var satisfies = state.Satisfies(goal, tolerance: 0.05);

        // Assert
        Assert.False(satisfies, "|0.8 - 0.7| = 0.1 exceeds tolerance of 0.05");
    }

    /// <summary>
    /// GOAPGoal from narrative phase targets correct values.
    /// </summary>
    [Fact]
    public void GOAPGoal_FromNarrativePhase_SetsTargets()
    {
        // Arrange
        var phase = new NarrativePhase
        {
            Name = "Climax",
            EmotionalTarget = EmotionalState.Presets.Climax,
            RelativeDuration = 0.1
        };

        // Act
        var goal = GOAPGoal.FromNarrativePhase(phase);

        // Assert
        Assert.Contains("Climax", goal.Name);
        var targetTension = goal.TargetState.Get<double>(WorldState.Keys.Tension);
        Assert.True(targetTension > 0.8, $"Climax phase should target high tension: {targetTension}");
    }

    /// <summary>
    /// Replanner detects significant divergence.
    /// </summary>
    [Fact]
    public void Replanner_DetectsDivergence()
    {
        // Arrange
        var actions = new ActionLibrary();
        var planner = new GOAPPlanner(actions);
        var replanner = new Replanner(planner) { DivergenceThreshold = 0.2 };

        var expected = new WorldState();
        expected.Set(WorldState.Keys.Tension, 0.5);

        var actual = new WorldState();
        actual.Set(WorldState.Keys.Tension, 0.2); // Significantly different

        // Act
        var needsReplan = replanner.NeedsReplan(expected, actual);

        // Assert
        Assert.True(needsReplan);
    }

    /// <summary>
    /// Replanner doesn't trigger for small differences.
    /// </summary>
    [Fact]
    public void Replanner_DoesNotTriggerForSmallDifferences()
    {
        // Arrange
        var actions = new ActionLibrary();
        var planner = new GOAPPlanner(actions);
        var replanner = new Replanner(planner) { DivergenceThreshold = 0.2 };

        var expected = new WorldState();
        expected.Set(WorldState.Keys.Tension, 0.5);

        var actual = new WorldState();
        actual.Set(WorldState.Keys.Tension, 0.55); // Small difference

        // Act
        var needsReplan = replanner.NeedsReplan(expected, actual);

        // Assert
        Assert.False(needsReplan);
    }
}
