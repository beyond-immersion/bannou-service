using BeyondImmersion.Bannou.MusicStoryteller.Actions;
using BeyondImmersion.Bannou.MusicStoryteller.Narratives;
using BeyondImmersion.Bannou.MusicStoryteller.State;

namespace BeyondImmersion.Bannou.MusicStoryteller.Planning;

/// <summary>
/// Multi-criteria action selection for choosing the best action
/// when multiple valid actions are available.
/// </summary>
public sealed class ActionSelection
{
    private readonly ActionLibrary _library;

    /// <summary>
    /// Creates an action selection helper.
    /// </summary>
    /// <param name="library">The action library.</param>
    public ActionSelection(ActionLibrary library)
    {
        _library = library;
    }

    /// <summary>
    /// Selects the best action toward a goal.
    /// </summary>
    /// <param name="goal">The goal to achieve.</param>
    /// <param name="state">Current composition state.</param>
    /// <param name="criteria">Selection criteria.</param>
    /// <returns>The best action, or null if none suitable.</returns>
    public IMusicalAction? SelectBest(
        GOAPGoal goal,
        CompositionState state,
        SelectionCriteria? criteria = null)
    {
        criteria ??= SelectionCriteria.Default;
        var candidates = GetCandidates(goal, state);

        if (candidates.Count == 0)
            return null;

        var scored = candidates
            .Select(action => (action, score: ScoreAction(action, goal, state, criteria)))
            .OrderByDescending(x => x.score)
            .ToList();

        return scored[0].action;
    }

    /// <summary>
    /// Selects multiple actions ranked by suitability.
    /// </summary>
    /// <param name="goal">The goal to achieve.</param>
    /// <param name="state">Current composition state.</param>
    /// <param name="count">Maximum number of actions to return.</param>
    /// <param name="criteria">Selection criteria.</param>
    /// <returns>Ranked actions.</returns>
    public IReadOnlyList<IMusicalAction> SelectTopN(
        GOAPGoal goal,
        CompositionState state,
        int count,
        SelectionCriteria? criteria = null)
    {
        criteria ??= SelectionCriteria.Default;
        var candidates = GetCandidates(goal, state);

        return candidates
            .Select(action => (action, score: ScoreAction(action, goal, state, criteria)))
            .OrderByDescending(x => x.score)
            .Take(count)
            .Select(x => x.action)
            .ToList();
    }

    /// <summary>
    /// Selects an action appropriate for a narrative phase.
    /// </summary>
    /// <param name="phase">The current narrative phase.</param>
    /// <param name="state">Current composition state.</param>
    /// <returns>The best action for the phase.</returns>
    public IMusicalAction? SelectForPhase(NarrativePhase phase, CompositionState state)
    {
        var goal = GOAPGoal.FromNarrativePhase(phase);
        var criteria = CriteriaForPhase(phase);
        return SelectBest(goal, state, criteria);
    }

    /// <summary>
    /// Gets all candidate actions that can be executed.
    /// </summary>
    /// <param name="goal">The goal to work toward.</param>
    /// <param name="state">Current composition state.</param>
    /// <returns>Executable actions that move toward the goal.</returns>
    public IReadOnlyList<IMusicalAction> GetCandidates(GOAPGoal goal, CompositionState state)
    {
        var worldState = WorldState.FromCompositionState(state);
        var targetEmotion = ExtractEmotionalTarget(goal);

        // Get actions that can execute and move toward goal
        return _library.AllActions
            .Where(a => a.CanExecute(state))
            .Where(a => MovesTowardGoal(a, worldState, goal.TargetState))
            .ToList();
    }

    private bool MovesTowardGoal(IMusicalAction action, WorldState current, WorldState target)
    {
        // Check if any effect moves us closer to the target
        foreach (var effect in action.Effects)
        {
            var dimension = effect.Dimension.ToLowerInvariant();
            var key = MapDimensionToKey(dimension);

            var currentVal = current.Get<double>(key);
            var targetVal = target.Get<double>(key);

            if (targetVal == default && !target.Contains(key))
                continue; // Target doesn't care about this dimension

            var diff = targetVal - currentVal;
            var effectMagnitude = effect.Type == EffectType.Relative ? effect.Magnitude : 0;

            // If effect moves us toward target, action is useful
            if (Math.Sign(effectMagnitude) == Math.Sign(diff) && Math.Abs(diff) > 0.05)
            {
                return true;
            }
        }

        return false;
    }

    private double ScoreAction(
        IMusicalAction action,
        GOAPGoal goal,
        CompositionState state,
        SelectionCriteria criteria)
    {
        var score = 0.0;
        var worldState = WorldState.FromCompositionState(state);

        // 1. Relevance to goal (highest weight)
        var relevance = CalculateRelevance(action, worldState, goal.TargetState);
        score += relevance * criteria.RelevanceWeight;

        // 2. Cost efficiency
        var cost = action.CalculateCost(state);
        var costScore = Math.Max(0, 2.0 - cost); // Lower cost = higher score
        score += costScore * criteria.CostWeight;

        // 3. Variety (avoid repeating same action type)
        if (criteria.AvoidRecent.Contains(action.Id))
        {
            score -= 0.5;
        }

        // 4. Category preference
        if (criteria.PreferredCategories.Contains(action.Category))
        {
            score += 0.3;
        }
        if (criteria.AvoidCategories.Contains(action.Category))
        {
            score -= 0.3;
        }

        // 5. Thematic considerations
        if (criteria.PreferThematicActions && action.Category == ActionCategory.Thematic)
        {
            score += 0.2;
        }

        return score;
    }

    private double CalculateRelevance(
        IMusicalAction action,
        WorldState current,
        WorldState target)
    {
        var relevance = 0.0;

        foreach (var effect in action.Effects)
        {
            var dimension = effect.Dimension.ToLowerInvariant();
            var key = MapDimensionToKey(dimension);

            var currentVal = current.Get<double>(key);
            var targetVal = target.Get<double>(key);

            if (!target.Contains(key))
                continue;

            var diff = targetVal - currentVal;
            var effectMagnitude = effect.Type == EffectType.Relative ? effect.Magnitude : 0;

            // Positive relevance if moving in right direction
            if (Math.Sign(effectMagnitude) == Math.Sign(diff) && Math.Abs(diff) > 0.05)
            {
                relevance += Math.Min(Math.Abs(effectMagnitude), Math.Abs(diff));
            }
            else if (Math.Sign(effectMagnitude) == -Math.Sign(diff))
            {
                // Negative relevance if moving away
                relevance -= Math.Abs(effectMagnitude) * 0.5;
            }
        }

        return relevance;
    }

    private EmotionalState ExtractEmotionalTarget(GOAPGoal goal)
    {
        return new EmotionalState
        {
            Tension = goal.TargetState.Get<double>(WorldState.Keys.Tension),
            Brightness = goal.TargetState.Get<double>(WorldState.Keys.Brightness),
            Energy = goal.TargetState.Get<double>(WorldState.Keys.Energy),
            Warmth = goal.TargetState.Get<double>(WorldState.Keys.Warmth),
            Stability = goal.TargetState.Get<double>(WorldState.Keys.Stability),
            Valence = goal.TargetState.Get<double>(WorldState.Keys.Valence)
        };
    }

    private SelectionCriteria CriteriaForPhase(NarrativePhase phase)
    {
        var criteria = SelectionCriteria.Default;

        // Adjust based on harmonic character
        if (phase.HarmonicCharacter == HarmonicCharacter.Resolving)
        {
            criteria = criteria with
            {
                PreferredCategories = [ActionCategory.Resolution]
            };
        }
        else if (phase.HarmonicCharacter == HarmonicCharacter.Building ||
                 phase.HarmonicCharacter == HarmonicCharacter.Climactic)
        {
            criteria = criteria with
            {
                PreferredCategories = [ActionCategory.Tension, ActionCategory.Texture]
            };
        }

        // Adjust for thematic goals
        if (phase.ThematicGoals.IntroduceMainMotif || phase.ThematicGoals.ReturnMainMotif)
        {
            criteria = criteria with { PreferThematicActions = true };
        }

        return criteria;
    }

    private static string MapDimensionToKey(string dimension)
    {
        return dimension switch
        {
            "tension" => WorldState.Keys.Tension,
            "brightness" => WorldState.Keys.Brightness,
            "energy" => WorldState.Keys.Energy,
            "warmth" => WorldState.Keys.Warmth,
            "stability" => WorldState.Keys.Stability,
            "valence" => WorldState.Keys.Valence,
            _ => dimension
        };
    }
}

/// <summary>
/// Criteria for action selection.
/// </summary>
public sealed record SelectionCriteria
{
    /// <summary>
    /// Weight for goal relevance in scoring.
    /// </summary>
    public double RelevanceWeight { get; init; } = 2.0;

    /// <summary>
    /// Weight for cost in scoring.
    /// </summary>
    public double CostWeight { get; init; } = 1.0;

    /// <summary>
    /// Action IDs to avoid (for variety).
    /// </summary>
    public IReadOnlyList<string> AvoidRecent { get; init; } = [];

    /// <summary>
    /// Preferred action categories.
    /// </summary>
    public IReadOnlyList<ActionCategory> PreferredCategories { get; init; } = [];

    /// <summary>
    /// Categories to avoid.
    /// </summary>
    public IReadOnlyList<ActionCategory> AvoidCategories { get; init; } = [];

    /// <summary>
    /// Whether to prefer thematic actions.
    /// </summary>
    public bool PreferThematicActions { get; init; }

    /// <summary>
    /// Gets default selection criteria.
    /// </summary>
    public static SelectionCriteria Default => new();
}
