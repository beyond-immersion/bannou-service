using BeyondImmersion.Bannou.MusicStoryteller.Actions;
using BeyondImmersion.Bannou.MusicStoryteller.State;

namespace BeyondImmersion.Bannou.MusicStoryteller.Planning;

/// <summary>
/// A GOAP-compatible wrapper for musical actions.
/// Expresses preconditions and effects in terms of WorldState.
/// </summary>
public sealed class GOAPAction
{
    /// <summary>
    /// Gets the underlying musical action.
    /// </summary>
    public IMusicalAction MusicalAction { get; }

    /// <summary>
    /// Gets the base cost of this action.
    /// </summary>
    public double BaseCost { get; }

    private readonly WorldState _preconditions;
    private readonly WorldState _effects;

    /// <summary>
    /// Creates a GOAP action wrapper.
    /// </summary>
    /// <param name="action">The musical action to wrap.</param>
    public GOAPAction(IMusicalAction action)
    {
        MusicalAction = action;
        BaseCost = action.BaseCost;
        _preconditions = ExtractPreconditions(action);
        _effects = ExtractEffects(action);
    }

    /// <summary>
    /// Gets the preconditions as a WorldState.
    /// </summary>
    public WorldState Preconditions => _preconditions.Clone();

    /// <summary>
    /// Gets the effects as a WorldState.
    /// </summary>
    public WorldState Effects => _effects.Clone();

    /// <summary>
    /// Checks if preconditions are satisfied in the given state.
    /// </summary>
    /// <param name="state">Current world state.</param>
    /// <returns>True if action can be executed.</returns>
    public bool IsSatisfied(WorldState state)
    {
        return state.Satisfies(_preconditions, 0.05);
    }

    /// <summary>
    /// Applies this action's effects to a world state.
    /// Returns a new state with effects applied.
    /// </summary>
    /// <param name="state">Current world state.</param>
    /// <returns>New state after action.</returns>
    public WorldState Apply(WorldState state)
    {
        var newState = state.Clone();

        foreach (var key in _effects.AllKeys)
        {
            var effectValue = _effects.Get<object>(key);
            if (effectValue == null) continue;

            var currentValue = newState.Get<object>(key);

            // For numeric values, apply as delta if the effect is marked as relative
            if (effectValue is double effectDouble && currentValue is double currentDouble)
            {
                // Check if this is a relative effect
                var effect = FindEffect(key);
                if (effect != null && effect.Type == EffectType.Relative)
                {
                    var newValue = Math.Clamp(currentDouble + effectDouble, 0.0, 1.0);
                    newState.Set(key, newValue);
                }
                else
                {
                    newState.Set(key, effectDouble);
                }
            }
            else
            {
                // For non-numeric, just set the value
                newState.Set(key, effectValue);
            }
        }

        return newState;
    }

    /// <summary>
    /// Calculates the cost of this action given the current state.
    /// </summary>
    /// <param name="state">Current composition state.</param>
    /// <returns>The action cost.</returns>
    public double CalculateCost(CompositionState state)
    {
        return MusicalAction.CalculateCost(state);
    }

    /// <summary>
    /// Calculates the cost using only world state.
    /// </summary>
    /// <param name="state">Current world state.</param>
    /// <returns>The action cost.</returns>
    public double CalculateCost(WorldState state)
    {
        // Use base cost when we don't have full composition state
        return BaseCost;
    }

    private ActionEffect? FindEffect(string dimension)
    {
        return MusicalAction.Effects.FirstOrDefault(e =>
            e.Dimension.Equals(dimension, StringComparison.OrdinalIgnoreCase));
    }

    private static WorldState ExtractPreconditions(IMusicalAction action)
    {
        var preconditions = new WorldState();

        foreach (var precondition in action.Preconditions)
        {
            // Map known precondition patterns to world state keys
            var name = precondition.Description.ToLowerInvariant();

            if (name.Contains("main motif") && name.Contains("introduced"))
            {
                var introduced = !name.Contains("not");
                preconditions.Set(WorldState.Keys.MainMotifIntroduced, introduced);
            }
            else if (name.Contains("dominant") || name.Contains("on v"))
            {
                preconditions.Set(WorldState.Keys.OnDominant, true);
            }
            else if (name.Contains("expecting cadence"))
            {
                preconditions.Set(WorldState.Keys.ExpectingCadence, true);
            }
            else if (name.Contains("not on tonic"))
            {
                preconditions.Set(WorldState.Keys.OnTonic, false);
            }
            else if (name.Contains("phrase boundary"))
            {
                // Phrase boundaries are position-dependent, handled separately
            }
            else if (name.Contains("progress above"))
            {
                // Progress constraints are handled dynamically
            }
            else if (name.Contains("major mode"))
            {
                preconditions.Set("mode", "major");
            }
            else if (name.Contains("minor mode"))
            {
                preconditions.Set("mode", "minor");
            }
        }

        return preconditions;
    }

    private static WorldState ExtractEffects(IMusicalAction action)
    {
        var effects = new WorldState();

        foreach (var effect in action.Effects)
        {
            var dimension = effect.Dimension.ToLowerInvariant();
            var key = dimension switch
            {
                "tension" => WorldState.Keys.Tension,
                "brightness" => WorldState.Keys.Brightness,
                "energy" => WorldState.Keys.Energy,
                "warmth" => WorldState.Keys.Warmth,
                "stability" => WorldState.Keys.Stability,
                "valence" => WorldState.Keys.Valence,
                _ => dimension
            };

            effects.Set(key, effect.Magnitude);
        }

        return effects;
    }

    /// <summary>
    /// Creates GOAP actions from an action library.
    /// </summary>
    /// <param name="library">The action library.</param>
    /// <returns>Wrapped GOAP actions.</returns>
    public static IEnumerable<GOAPAction> FromLibrary(ActionLibrary library)
    {
        return library.AllActions.Select(a => new GOAPAction(a));
    }
}
