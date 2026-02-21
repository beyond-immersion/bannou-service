// Copyright (c) Beyond Immersion. All rights reserved.

namespace BeyondImmersion.Bannou.StorylineStoryteller.Planning;

/// <summary>
/// Tracks story progress for position estimation.
/// </summary>
public sealed class StoryProgress
{
    /// <summary>
    /// Number of actions completed so far.
    /// </summary>
    public int ActionsCompleted { get; private set; }

    /// <summary>
    /// Number of phases completed so far.
    /// </summary>
    public int CompletedPhases { get; private set; }

    /// <summary>
    /// Number of actions planned for the current phase.
    /// </summary>
    public int CurrentPhaseActionsPlanned { get; private set; }

    /// <summary>
    /// Estimated total actions for the story.
    /// </summary>
    public int TotalActionsEstimate { get; private set; }

    /// <summary>
    /// Current position in the story (0.0 to 1.0).
    /// </summary>
    public double Position => TotalActionsEstimate > 0
        ? (double)ActionsCompleted / TotalActionsEstimate
        : 0.0;

    /// <summary>
    /// Called when entering a new phase (lazy evaluation).
    /// Re-estimates total based on current trajectory.
    /// </summary>
    public void OnPhaseGenerated(int phaseActionCount, int remainingPhases)
    {
        CurrentPhaseActionsPlanned = phaseActionCount;

        // Re-estimate total based on current trajectory
        var avgPerPhase = CompletedPhases > 0
            ? ActionsCompleted / CompletedPhases
            : phaseActionCount;

        TotalActionsEstimate = ActionsCompleted + (avgPerPhase * (remainingPhases + 1));
    }

    /// <summary>
    /// Called when an action is completed.
    /// </summary>
    public void OnActionCompleted()
    {
        ActionsCompleted++;
    }

    /// <summary>
    /// Called when a phase is completed.
    /// </summary>
    public void OnPhaseCompleted()
    {
        CompletedPhases++;
    }
}
