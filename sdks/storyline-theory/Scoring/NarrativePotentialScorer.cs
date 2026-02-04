using BeyondImmersion.Bannou.StorylineTheory.State;

namespace BeyondImmersion.Bannou.StorylineTheory.Scoring;

/// <summary>
/// Scores the narrative potential of a story state for GOAP planning.
/// Higher potential indicates more promising states for action selection.
///
/// WEIGHT DERIVATION: These weights are design hypotheses based on:
/// - Distance to goal is the primary GOAP heuristic
/// - Available actions represent narrative flexibility
/// - Unrevealed information creates engagement hooks
/// - Urgency signals proximity to resolution
/// Configurable to allow empirical tuning via A/B testing.
/// </summary>
public static class NarrativePotentialScorer
{
    /// <summary>
    /// Hypothesis weights for narrative potential scoring.
    /// Subject to empirical calibration.
    /// </summary>
    public static class Weights
    {
        /// <summary>
        /// Weight for progress toward goal state.
        /// Rationale: Primary GOAP heuristic - closer to goal is better.
        /// </summary>
        public const double GoalProgress = 0.35;

        /// <summary>
        /// Weight for available narrative actions.
        /// Rationale: More options = more narrative flexibility.
        /// </summary>
        public const double ActionAvailability = 0.25;

        /// <summary>
        /// Weight for tension-stakes alignment.
        /// Rationale: Misaligned tension/stakes feels wrong to audiences.
        /// </summary>
        public const double TensionStakesAlignment = 0.15;

        /// <summary>
        /// Weight for mystery engagement.
        /// Rationale: Unrevealed information hooks engagement.
        /// </summary>
        public const double MysteryEngagement = 0.15;

        /// <summary>
        /// Weight for urgency appropriateness.
        /// Rationale: Urgency should match story phase.
        /// </summary>
        public const double UrgencyAppropriateness = 0.10;
    }

    /// <summary>
    /// Thresholds for alignment scoring.
    /// </summary>
    public static class Thresholds
    {
        /// <summary>
        /// Maximum reasonable difference between tension and stakes.
        /// Beyond this, the story feels incoherent.
        /// </summary>
        public const double TensionStakesMaxDiff = 0.4;

        /// <summary>
        /// Minimum mystery needed for engagement (except at resolution).
        /// </summary>
        public const double MinEngagingMystery = 0.1;

        /// <summary>
        /// Maximum mystery that maintains clarity.
        /// </summary>
        public const double MaxClearMystery = 0.9;
    }

    /// <summary>
    /// Calculate narrative potential score.
    /// </summary>
    /// <param name="currentState">The current narrative state.</param>
    /// <param name="goalState">The target narrative state.</param>
    /// <param name="availableActionCount">Number of valid actions from this state.</param>
    /// <param name="totalPossibleActions">Total actions in the action space.</param>
    /// <param name="storyProgress">Current progress through the story (0-1).</param>
    /// <returns>Potential score (0-1). Higher scores indicate more promising states.</returns>
    public static double Calculate(
        NarrativeState currentState,
        NarrativeState goalState,
        int availableActionCount,
        int totalPossibleActions,
        double storyProgress)
    {
        // 1. Goal progress (inverse of distance - closer is better)
        var goalScore = CalculateGoalProgress(currentState, goalState);

        // 2. Action availability ratio
        var actionScore = totalPossibleActions > 0
            ? (double)availableActionCount / totalPossibleActions
            : 0.0;

        // 3. Tension-stakes alignment
        var alignmentScore = CalculateTensionStakesAlignment(currentState);

        // 4. Mystery engagement (sweet spot, not too low or high)
        var mysteryScore = CalculateMysteryEngagement(currentState, storyProgress);

        // 5. Urgency appropriateness for story phase
        var urgencyScore = CalculateUrgencyAppropriateness(currentState, storyProgress);

        return Weights.GoalProgress * goalScore +
               Weights.ActionAvailability * actionScore +
               Weights.TensionStakesAlignment * alignmentScore +
               Weights.MysteryEngagement * mysteryScore +
               Weights.UrgencyAppropriateness * urgencyScore;
    }

    /// <summary>
    /// Calculate goal progress as inverse normalized distance.
    /// </summary>
    private static double CalculateGoalProgress(NarrativeState current, NarrativeState goal)
    {
        var normalizedDistance = current.NormalizedDistanceTo(goal);
        return 1.0 - normalizedDistance;
    }

    /// <summary>
    /// Calculate tension-stakes alignment.
    /// High tension with low stakes (or vice versa) feels wrong.
    /// </summary>
    private static double CalculateTensionStakesAlignment(NarrativeState state)
    {
        var diff = Math.Abs(state.Tension - state.Stakes);
        return Math.Max(0, 1.0 - diff / Thresholds.TensionStakesMaxDiff);
    }

    /// <summary>
    /// Calculate mystery engagement score.
    /// Mystery should decrease toward resolution but maintain engagement.
    /// </summary>
    private static double CalculateMysteryEngagement(NarrativeState state, double storyProgress)
    {
        // Expected mystery decreases as story progresses
        var expectedMystery = Math.Max(Thresholds.MinEngagingMystery,
            Thresholds.MaxClearMystery * (1.0 - storyProgress));

        // Score based on being in the engaging range
        if (state.Mystery < Thresholds.MinEngagingMystery && storyProgress < 0.9)
        {
            // Too little mystery too early
            return state.Mystery / Thresholds.MinEngagingMystery;
        }

        if (state.Mystery > Thresholds.MaxClearMystery)
        {
            // Too much mystery - audience is lost
            return Thresholds.MaxClearMystery / state.Mystery;
        }

        // Within acceptable range - score based on matching expected trajectory
        var deviation = Math.Abs(state.Mystery - expectedMystery);
        return Math.Max(0, 1.0 - deviation);
    }

    /// <summary>
    /// Calculate urgency appropriateness for the story phase.
    /// Urgency should generally increase toward the climax.
    /// </summary>
    private static double CalculateUrgencyAppropriateness(NarrativeState state, double storyProgress)
    {
        // Expected urgency increases toward climax (around 75-80%)
        // then can decrease in resolution
        double expectedUrgency;
        if (storyProgress < 0.75)
        {
            // Building toward climax
            expectedUrgency = 0.2 + 0.7 * (storyProgress / 0.75);
        }
        else
        {
            // Post-climax, urgency can decrease
            var postClimaxProgress = (storyProgress - 0.75) / 0.25;
            expectedUrgency = 0.9 - 0.7 * postClimaxProgress;
        }

        var deviation = Math.Abs(state.Urgency - expectedUrgency);
        return Math.Max(0, 1.0 - deviation);
    }

    /// <summary>
    /// GOAP heuristic function for A* planning.
    /// Returns estimated cost to reach goal from current state.
    /// </summary>
    /// <param name="currentState">Current narrative state.</param>
    /// <param name="goalState">Target narrative state.</param>
    /// <returns>Estimated cost (lower is closer to goal).</returns>
    public static double GoapHeuristic(NarrativeState currentState, NarrativeState goalState)
    {
        // Simple Euclidean distance in 6D space
        return currentState.DistanceTo(goalState);
    }

    /// <summary>
    /// Evaluates whether a state transition moves toward the goal.
    /// Useful for GOAP action evaluation.
    /// </summary>
    /// <param name="beforeState">State before action.</param>
    /// <param name="afterState">State after action.</param>
    /// <param name="goalState">Target state.</param>
    /// <returns>Positive if moving toward goal, negative if moving away.</returns>
    public static double EvaluateTransition(
        NarrativeState beforeState,
        NarrativeState afterState,
        NarrativeState goalState)
    {
        var distanceBefore = beforeState.DistanceTo(goalState);
        var distanceAfter = afterState.DistanceTo(goalState);
        return distanceBefore - distanceAfter; // Positive = improvement
    }

    /// <summary>
    /// Gets a detailed breakdown of narrative potential for analysis.
    /// </summary>
    public static NarrativePotentialBreakdown GetBreakdown(
        NarrativeState currentState,
        NarrativeState goalState,
        int availableActionCount,
        int totalPossibleActions,
        double storyProgress)
    {
        return new NarrativePotentialBreakdown(
            TotalScore: Calculate(currentState, goalState, availableActionCount,
                totalPossibleActions, storyProgress),
            GoalProgressScore: CalculateGoalProgress(currentState, goalState),
            ActionAvailabilityScore: totalPossibleActions > 0
                ? (double)availableActionCount / totalPossibleActions
                : 0.0,
            TensionStakesAlignmentScore: CalculateTensionStakesAlignment(currentState),
            MysteryEngagementScore: CalculateMysteryEngagement(currentState, storyProgress),
            UrgencyAppropriatenessScore: CalculateUrgencyAppropriateness(currentState, storyProgress),
            DistanceToGoal: currentState.DistanceTo(goalState),
            NormalizedDistanceToGoal: currentState.NormalizedDistanceTo(goalState),
            AvailableActions: availableActionCount,
            TotalActions: totalPossibleActions);
    }

    /// <summary>
    /// Suggests state adjustments to improve narrative potential.
    /// </summary>
    public static NarrativeStateAdjustments SuggestAdjustments(
        NarrativeState currentState,
        NarrativeState goalState,
        double storyProgress)
    {
        var suggestions = new List<string>();
        var targetState = currentState.Clone();

        // Check tension-stakes alignment
        var tensionStakesDiff = Math.Abs(currentState.Tension - currentState.Stakes);
        if (tensionStakesDiff > Thresholds.TensionStakesMaxDiff)
        {
            if (currentState.Tension > currentState.Stakes)
            {
                suggestions.Add("Raise stakes to match tension");
                targetState.Stakes = currentState.Tension;
            }
            else
            {
                suggestions.Add("Raise tension to match stakes");
                targetState.Tension = currentState.Stakes;
            }
        }

        // Check mystery engagement
        if (currentState.Mystery < Thresholds.MinEngagingMystery && storyProgress < 0.9)
        {
            suggestions.Add("Introduce new mystery or unanswered question");
            targetState.Mystery = Thresholds.MinEngagingMystery + 0.1;
        }
        else if (currentState.Mystery > Thresholds.MaxClearMystery)
        {
            suggestions.Add("Reveal some information to reduce confusion");
            targetState.Mystery = Thresholds.MaxClearMystery;
        }

        // Check urgency appropriateness
        var expectedUrgency = storyProgress < 0.75
            ? 0.2 + 0.7 * (storyProgress / 0.75)
            : 0.9 - 0.7 * ((storyProgress - 0.75) / 0.25);

        var urgencyDiff = currentState.Urgency - expectedUrgency;
        if (Math.Abs(urgencyDiff) > 0.2)
        {
            if (urgencyDiff > 0)
            {
                suggestions.Add("Reduce urgency - story isn't at climax yet");
            }
            else
            {
                suggestions.Add("Increase urgency - story is approaching climax");
            }
            targetState.Urgency = expectedUrgency;
        }

        // Suggest moving toward goal
        var goalDistance = currentState.NormalizedDistanceTo(goalState);
        if (goalDistance > 0.3)
        {
            suggestions.Add($"Story is {goalDistance:P0} away from goal state - consider actions that move toward resolution");
        }

        return new NarrativeStateAdjustments(
            Suggestions: suggestions,
            SuggestedTargetState: targetState,
            CurrentPotential: Calculate(currentState, goalState, 0, 1, storyProgress),
            ProjectedPotential: Calculate(targetState, goalState, 0, 1, storyProgress));
    }
}

/// <summary>
/// Detailed breakdown of narrative potential scoring.
/// </summary>
/// <param name="TotalScore">Overall potential score (0-1).</param>
/// <param name="GoalProgressScore">Score for progress toward goal (0-1).</param>
/// <param name="ActionAvailabilityScore">Score for available actions (0-1).</param>
/// <param name="TensionStakesAlignmentScore">Score for tension-stakes alignment (0-1).</param>
/// <param name="MysteryEngagementScore">Score for mystery engagement (0-1).</param>
/// <param name="UrgencyAppropriatenessScore">Score for urgency appropriateness (0-1).</param>
/// <param name="DistanceToGoal">Raw Euclidean distance to goal state.</param>
/// <param name="NormalizedDistanceToGoal">Normalized distance to goal (0-1).</param>
/// <param name="AvailableActions">Count of available actions.</param>
/// <param name="TotalActions">Total possible actions.</param>
public sealed record NarrativePotentialBreakdown(
    double TotalScore,
    double GoalProgressScore,
    double ActionAvailabilityScore,
    double TensionStakesAlignmentScore,
    double MysteryEngagementScore,
    double UrgencyAppropriatenessScore,
    double DistanceToGoal,
    double NormalizedDistanceToGoal,
    int AvailableActions,
    int TotalActions);

/// <summary>
/// Suggested adjustments to improve narrative potential.
/// </summary>
/// <param name="Suggestions">Human-readable improvement suggestions.</param>
/// <param name="SuggestedTargetState">Recommended state to target.</param>
/// <param name="CurrentPotential">Current potential score.</param>
/// <param name="ProjectedPotential">Projected score if suggestions applied.</param>
public sealed record NarrativeStateAdjustments(
    IReadOnlyList<string> Suggestions,
    NarrativeState SuggestedTargetState,
    double CurrentPotential,
    double ProjectedPotential);
