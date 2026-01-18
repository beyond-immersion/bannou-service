using BeyondImmersion.Bannou.MusicStoryteller.Narratives;
using BeyondImmersion.Bannou.MusicStoryteller.State;

namespace BeyondImmersion.Bannou.MusicStoryteller.Planning;

/// <summary>
/// A goal for GOAP planning.
/// Defines the desired world state to achieve.
/// </summary>
public sealed class GOAPGoal
{
    /// <summary>
    /// Gets the unique identifier for this goal.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Gets the display name of this goal.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the target world state that represents goal completion.
    /// </summary>
    public required WorldState TargetState { get; init; }

    /// <summary>
    /// Gets the priority of this goal (higher = more important).
    /// </summary>
    public double Priority { get; init; } = 1.0;

    /// <summary>
    /// Gets the tolerance for considering the goal satisfied.
    /// </summary>
    public double Tolerance { get; init; } = 0.1;

    /// <summary>
    /// Checks if this goal is satisfied by a world state.
    /// </summary>
    /// <param name="current">Current world state.</param>
    /// <returns>True if satisfied.</returns>
    public bool IsSatisfied(WorldState current)
    {
        return current.Satisfies(TargetState, Tolerance);
    }

    /// <summary>
    /// Gets the distance from the current state to this goal.
    /// Used for planning heuristics.
    /// </summary>
    /// <param name="current">Current world state.</param>
    /// <returns>Distance to goal.</returns>
    public double GetDistance(WorldState current)
    {
        return current.DistanceTo(TargetState);
    }

    /// <summary>
    /// Creates a goal to reach a specific emotional state.
    /// </summary>
    /// <param name="name">Goal name.</param>
    /// <param name="target">Target emotional state.</param>
    /// <param name="priority">Goal priority.</param>
    /// <returns>A new GOAP goal.</returns>
    public static GOAPGoal ReachEmotionalState(string name, EmotionalState target, double priority = 1.0)
    {
        return new GOAPGoal
        {
            Id = $"emotional_{name.ToLowerInvariant().Replace(" ", "_")}",
            Name = name,
            TargetState = WorldState.FromEmotionalTarget(target),
            Priority = priority,
            Tolerance = 0.15 // Allow some flexibility
        };
    }

    /// <summary>
    /// Creates a goal from a narrative phase.
    /// </summary>
    /// <param name="phase">The narrative phase.</param>
    /// <param name="priority">Goal priority.</param>
    /// <returns>A new GOAP goal.</returns>
    public static GOAPGoal FromNarrativePhase(NarrativePhase phase, double priority = 1.0)
    {
        var target = WorldState.FromEmotionalTarget(phase.EmotionalTarget);

        // Add thematic requirements
        if (phase.ThematicGoals.IntroduceMainMotif)
        {
            target.Set(WorldState.Keys.MainMotifIntroduced, true);
        }

        // Add resolution requirements
        if (phase.RequireResolution)
        {
            target.Set(WorldState.Keys.OnTonic, true);
            target.Set(WorldState.Keys.ExpectingCadence, false);
        }

        return new GOAPGoal
        {
            Id = $"phase_{phase.Name.ToLowerInvariant().Replace(" ", "_")}",
            Name = $"Reach {phase.Name}",
            TargetState = target,
            Priority = priority,
            Tolerance = 0.1
        };
    }

    /// <summary>
    /// Creates a goal to increase tension.
    /// </summary>
    /// <param name="targetTension">Target tension level.</param>
    /// <returns>A tension-building goal.</returns>
    public static GOAPGoal BuildTension(double targetTension = 0.8)
    {
        var target = new WorldState();
        target.Set(WorldState.Keys.Tension, targetTension);

        return new GOAPGoal
        {
            Id = "build_tension",
            Name = "Build Tension",
            TargetState = target,
            Priority = 1.0,
            Tolerance = 0.1
        };
    }

    /// <summary>
    /// Creates a goal to resolve tension.
    /// </summary>
    /// <param name="targetTension">Target tension level (low).</param>
    /// <returns>A resolution goal.</returns>
    public static GOAPGoal ResolveTension(double targetTension = 0.2)
    {
        var target = new WorldState();
        target.Set(WorldState.Keys.Tension, targetTension);
        target.Set(WorldState.Keys.Stability, 0.8);
        target.Set(WorldState.Keys.OnTonic, true);

        return new GOAPGoal
        {
            Id = "resolve_tension",
            Name = "Resolve Tension",
            TargetState = target,
            Priority = 1.5, // Higher priority than building
            Tolerance = 0.1
        };
    }

    /// <summary>
    /// Creates a goal to introduce the main motif.
    /// </summary>
    /// <returns>A thematic introduction goal.</returns>
    public static GOAPGoal IntroduceMainMotif()
    {
        var target = new WorldState();
        target.Set(WorldState.Keys.MainMotifIntroduced, true);

        return new GOAPGoal
        {
            Id = "introduce_motif",
            Name = "Introduce Main Motif",
            TargetState = target,
            Priority = 2.0, // High priority early
            Tolerance = 0.0 // Boolean - must be exact
        };
    }

    /// <summary>
    /// Creates a goal to return to the home key.
    /// </summary>
    /// <returns>A homecoming goal.</returns>
    public static GOAPGoal ReturnHome()
    {
        var target = new WorldState();
        target.Set(WorldState.Keys.KeyDistance, 0.0);
        target.Set(WorldState.Keys.OnTonic, true);

        return new GOAPGoal
        {
            Id = "return_home",
            Name = "Return to Home Key",
            TargetState = target,
            Priority = 1.2,
            Tolerance = 0.0
        };
    }

    /// <summary>
    /// Creates a goal for a final cadence.
    /// </summary>
    /// <returns>A cadential goal.</returns>
    public static GOAPGoal FinalCadence()
    {
        var target = new WorldState();
        target.Set(WorldState.Keys.Tension, 0.1);
        target.Set(WorldState.Keys.Stability, 0.9);
        target.Set(WorldState.Keys.OnTonic, true);
        target.Set(WorldState.Keys.ExpectingCadence, false);

        return new GOAPGoal
        {
            Id = "final_cadence",
            Name = "Final Cadence",
            TargetState = target,
            Priority = 2.0, // High priority at end
            Tolerance = 0.05
        };
    }
}
