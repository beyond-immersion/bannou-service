using BeyondImmersion.Bannou.MusicStoryteller.State;

namespace BeyondImmersion.Bannou.MusicStoryteller.Actions;

/// <summary>
/// Registry of all available musical actions.
/// Provides lookup and filtering capabilities for GOAP planning.
/// </summary>
public sealed class ActionLibrary
{
    private readonly Dictionary<string, IMusicalAction> _actionsById = new();
    private readonly Dictionary<ActionCategory, List<IMusicalAction>> _actionsByCategory = new();

    /// <summary>
    /// Gets all registered actions.
    /// </summary>
    public IEnumerable<IMusicalAction> AllActions => _actionsById.Values;

    /// <summary>
    /// Gets the count of registered actions.
    /// </summary>
    public int Count => _actionsById.Count;

    /// <summary>
    /// Creates an action library with all built-in actions.
    /// </summary>
    public ActionLibrary()
    {
        // Initialize category dictionary
        foreach (ActionCategory category in Enum.GetValues(typeof(ActionCategory)))
        {
            _actionsByCategory[category] = [];
        }

        // Register all built-in actions
        RegisterBuiltInActions();
    }

    /// <summary>
    /// Registers an action.
    /// </summary>
    /// <param name="action">The action to register.</param>
    public void Register(IMusicalAction action)
    {
        _actionsById[action.Id] = action;
        _actionsByCategory[action.Category].Add(action);
    }

    /// <summary>
    /// Gets an action by ID.
    /// </summary>
    /// <param name="id">The action ID.</param>
    /// <returns>The action, or null if not found.</returns>
    public IMusicalAction? GetById(string id)
    {
        return _actionsById.GetValueOrDefault(id);
    }

    /// <summary>
    /// Gets all actions in a category.
    /// </summary>
    /// <param name="category">The category.</param>
    /// <returns>Actions in the category.</returns>
    public IEnumerable<IMusicalAction> GetByCategory(ActionCategory category)
    {
        return _actionsByCategory.GetValueOrDefault(category) ?? [];
    }

    /// <summary>
    /// Gets all actions that can be executed given the current state.
    /// </summary>
    /// <param name="state">The current state.</param>
    /// <returns>Executable actions.</returns>
    public IEnumerable<IMusicalAction> GetExecutableActions(CompositionState state)
    {
        return AllActions.Where(a => a.CanExecute(state));
    }

    /// <summary>
    /// Gets actions that affect a specific dimension.
    /// </summary>
    /// <param name="dimension">The dimension (e.g., "tension").</param>
    /// <returns>Actions affecting that dimension.</returns>
    public IEnumerable<IMusicalAction> GetActionsByDimension(string dimension)
    {
        return AllActions.Where(a =>
            a.Effects.Any(e => e.Dimension.Equals(dimension, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>
    /// Gets actions that increase a specific dimension.
    /// </summary>
    /// <param name="dimension">The dimension.</param>
    /// <returns>Actions that increase it.</returns>
    public IEnumerable<IMusicalAction> GetActionsToIncrease(string dimension)
    {
        return GetActionsByDimension(dimension)
            .Where(a => a.Effects.Any(e =>
                e.Dimension.Equals(dimension, StringComparison.OrdinalIgnoreCase) &&
                (e.Type == EffectType.Relative && e.Magnitude > 0)));
    }

    /// <summary>
    /// Gets actions that decrease a specific dimension.
    /// </summary>
    /// <param name="dimension">The dimension.</param>
    /// <returns>Actions that decrease it.</returns>
    public IEnumerable<IMusicalAction> GetActionsToDecrease(string dimension)
    {
        return GetActionsByDimension(dimension)
            .Where(a => a.Effects.Any(e =>
                e.Dimension.Equals(dimension, StringComparison.OrdinalIgnoreCase) &&
                (e.Type == EffectType.Relative && e.Magnitude < 0)));
    }

    /// <summary>
    /// Finds actions that move toward a target state.
    /// </summary>
    /// <param name="current">Current emotional state.</param>
    /// <param name="target">Target emotional state.</param>
    /// <param name="state">Full composition state for precondition checking.</param>
    /// <returns>Actions sorted by relevance to reaching the target.</returns>
    public IEnumerable<(IMusicalAction action, double relevance)> FindActionsTowardTarget(
        EmotionalState current,
        EmotionalState target,
        CompositionState state)
    {
        var results = new List<(IMusicalAction, double)>();

        foreach (var action in GetExecutableActions(state))
        {
            var relevance = CalculateRelevance(action, current, target, state);
            if (relevance > 0)
            {
                results.Add((action, relevance));
            }
        }

        return results.OrderByDescending(r => r.Item2);
    }

    /// <summary>
    /// Gets the best action to move toward a target dimension value.
    /// </summary>
    /// <param name="dimension">The dimension to change.</param>
    /// <param name="currentValue">Current value.</param>
    /// <param name="targetValue">Target value.</param>
    /// <param name="state">Full composition state.</param>
    /// <returns>The best action, or null if none found.</returns>
    public IMusicalAction? GetBestActionForDimension(
        string dimension,
        double currentValue,
        double targetValue,
        CompositionState state)
    {
        var needsIncrease = targetValue > currentValue;
        var actions = needsIncrease
            ? GetActionsToIncrease(dimension)
            : GetActionsToDecrease(dimension);

        return actions
            .Where(a => a.CanExecute(state))
            .OrderBy(a => a.CalculateCost(state))
            .FirstOrDefault();
    }

    private double CalculateRelevance(
        IMusicalAction action,
        EmotionalState current,
        EmotionalState target,
        CompositionState state)
    {
        var relevance = 0.0;
        var predictions = action.GetPredictedEffects(state);

        foreach (var (dimension, change) in predictions)
        {
            var currentVal = GetDimensionValue(current, dimension);
            var targetVal = GetDimensionValue(target, dimension);
            var diff = targetVal - currentVal;

            // Positive relevance if action moves us in the right direction
            if (Math.Sign(change) == Math.Sign(diff) && Math.Abs(diff) > 0.05)
            {
                // More relevance for larger changes in the right direction
                relevance += Math.Min(Math.Abs(change), Math.Abs(diff));
            }
            else if (Math.Sign(change) == -Math.Sign(diff))
            {
                // Negative relevance if action moves us away
                relevance -= Math.Abs(change) * 0.5;
            }
        }

        // Factor in cost (lower cost = more relevant)
        var cost = action.CalculateCost(state);
        relevance /= (1 + cost);

        return relevance;
    }

    private static double GetDimensionValue(EmotionalState state, string dimension)
    {
        return dimension.ToLowerInvariant() switch
        {
            "tension" => state.Tension,
            "brightness" => state.Brightness,
            "energy" => state.Energy,
            "warmth" => state.Warmth,
            "stability" => state.Stability,
            "valence" => state.Valence,
            _ => 0.5
        };
    }

    private void RegisterBuiltInActions()
    {
        // Register all tension actions
        foreach (var action in TensionActions.All)
        {
            Register(action);
        }

        // Register all resolution actions
        foreach (var action in ResolutionActions.All)
        {
            Register(action);
        }

        // Register all color actions
        foreach (var action in ColorActions.All)
        {
            Register(action);
        }

        // Register all thematic actions
        foreach (var action in ThematicActions.All)
        {
            Register(action);
        }

        // Register all texture actions
        foreach (var action in TextureActions.All)
        {
            Register(action);
        }
    }
}
