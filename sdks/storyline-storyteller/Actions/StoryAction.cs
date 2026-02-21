// Copyright (c) Beyond Immersion. All rights reserved.

using BeyondImmersion.Bannou.StorylineTheory.Planning;

namespace BeyondImmersion.Bannou.StorylineStoryteller.Actions;

/// <summary>
/// Full action definition for GOAP planning.
/// </summary>
public sealed class StoryAction
{
    /// <summary>
    /// Unique action identifier (e.g., "hero_at_mercy_of_villain").
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// The action category.
    /// </summary>
    public required ActionCategory Category { get; init; }

    /// <summary>
    /// GOAP planning cost.
    /// </summary>
    public required double Cost { get; init; }

    /// <summary>
    /// Whether this is an obligatory scene for genre satisfaction.
    /// </summary>
    public required bool IsCoreEvent { get; init; }

    /// <summary>
    /// Genres this action can be used in.
    /// </summary>
    public required string[] ApplicableGenres { get; init; }

    /// <summary>
    /// Preconditions that must be met to execute this action.
    /// </summary>
    public required ActionPrecondition[] Preconditions { get; init; }

    /// <summary>
    /// Effects applied when this action is executed.
    /// </summary>
    public required ActionEffect[] Effects { get; init; }

    /// <summary>
    /// Effect on the narrative/emotional arc.
    /// </summary>
    public required NarrativeEffect NarrativeEffect { get; init; }

    /// <summary>
    /// Follow-up action ID for chained actions.
    /// </summary>
    public string? ChainedAction { get; init; }

    /// <summary>
    /// Human-readable description.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Genre-specific variations.
    /// </summary>
    public StoryActionVariant[]? Variants { get; init; }

    /// <summary>
    /// Check if action is applicable given current world state.
    /// </summary>
    public bool CanExecute(WorldState state)
    {
        foreach (var precondition in Preconditions)
        {
            if (!state.Facts.TryGetValue(precondition.Key, out var value))
            {
                return false;
            }

            if (!EvaluatePrecondition(value, precondition.Value, precondition.Operator))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Apply effects to world state (returns new state, immutable).
    /// </summary>
    public WorldState Execute(WorldState state)
    {
        var newFacts = new Dictionary<string, object>(state.Facts);

        foreach (var effect in Effects)
        {
            if (effect.Cardinality == EffectCardinality.Exclusive)
            {
                newFacts[effect.Key] = effect.Value;
            }
            else if (effect.Cardinality == EffectCardinality.Additive)
            {
                if (newFacts.TryGetValue(effect.Key, out var existing) && existing is IList<object> list)
                {
                    var newList = new List<object>(list) { effect.Value };
                    newFacts[effect.Key] = newList;
                }
                else
                {
                    newFacts[effect.Key] = new List<object> { effect.Value };
                }
            }
        }

        return new WorldState
        {
            NarrativeState = state.NarrativeState,
            Facts = newFacts,
            Position = state.Position
        };
    }

    private static bool EvaluatePrecondition(object actual, object expected, ActionPreconditionOperator op)
    {
        return op switch
        {
            ActionPreconditionOperator.Equals => Equals(actual, expected),
            ActionPreconditionOperator.NotEquals => !Equals(actual, expected),
            ActionPreconditionOperator.GreaterThan => Compare(actual, expected) > 0,
            ActionPreconditionOperator.LessThan => Compare(actual, expected) < 0,
            ActionPreconditionOperator.GreaterOrEqual => Compare(actual, expected) >= 0,
            ActionPreconditionOperator.LessOrEqual => Compare(actual, expected) <= 0,
            _ => false
        };
    }

    private static int Compare(object a, object b)
    {
        if (a is IComparable ca && b is IComparable)
        {
            return ca.CompareTo(b);
        }
        return 0;
    }
}
