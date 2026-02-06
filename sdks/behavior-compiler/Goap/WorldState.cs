// ═══════════════════════════════════════════════════════════════════════════
// GOAP World State
// Immutable key-value state container for A* planning.
// ═══════════════════════════════════════════════════════════════════════════

using System.Collections.Immutable;

namespace BeyondImmersion.Bannou.BehaviorCompiler.Goap;

/// <summary>
/// Represents immutable world state for GOAP planning.
/// All modifications return new instances, enabling safe A* backtracking.
/// </summary>
public sealed class WorldState : IEquatable<WorldState>
{
    private readonly ImmutableDictionary<string, object> _state;
    private readonly int _cachedHashCode;

    /// <summary>
    /// Creates an empty world state.
    /// </summary>
    public WorldState()
    {
        _state = ImmutableDictionary<string, object>.Empty;
        _cachedHashCode = ComputeHashCode();
    }

    /// <summary>
    /// Creates world state from existing values.
    /// </summary>
    /// <param name="state">Initial state values.</param>
    private WorldState(ImmutableDictionary<string, object> state)
    {
        _state = state;
        _cachedHashCode = ComputeHashCode();
    }

    /// <summary>
    /// Creates world state from a dictionary of values.
    /// </summary>
    /// <param name="values">Key-value pairs to initialize state with.</param>
    /// <returns>New world state with the given values.</returns>
    public static WorldState FromDictionary(IReadOnlyDictionary<string, object> values)
    {
        var builder = ImmutableDictionary.CreateBuilder<string, object>();
        foreach (var (key, value) in values)
        {
            builder[key] = value;
        }
        return new WorldState(builder.ToImmutable());
    }

    /// <summary>
    /// Gets a numeric value from state.
    /// </summary>
    /// <param name="key">Property key (supports dot notation).</param>
    /// <param name="defaultValue">Default if key not found.</param>
    /// <returns>The numeric value or default.</returns>
    public float GetNumeric(string key, float defaultValue = 0f)
    {
        if (!_state.TryGetValue(key, out var value))
        {
            return defaultValue;
        }

        return value switch
        {
            float f => f,
            double d => (float)d,
            int i => i,
            long l => l,
            decimal dec => (float)dec,
            string s when float.TryParse(s, out var parsed) => parsed,
            _ => defaultValue
        };
    }

    /// <summary>
    /// Gets a boolean value from state.
    /// </summary>
    /// <param name="key">Property key.</param>
    /// <param name="defaultValue">Default if key not found.</param>
    /// <returns>The boolean value or default.</returns>
    public bool GetBoolean(string key, bool defaultValue = false)
    {
        if (!_state.TryGetValue(key, out var value))
        {
            return defaultValue;
        }

        return value switch
        {
            bool b => b,
            string s => s.Equals("true", StringComparison.OrdinalIgnoreCase),
            int i => i != 0,
            float f => f != 0f,
            _ => defaultValue
        };
    }

    /// <summary>
    /// Gets a string value from state.
    /// </summary>
    /// <param name="key">Property key.</param>
    /// <param name="defaultValue">Default if key not found.</param>
    /// <returns>The string value or default.</returns>
    public string GetString(string key, string defaultValue = "")
    {
        if (!_state.TryGetValue(key, out var value))
        {
            return defaultValue;
        }

        return value?.ToString() ?? defaultValue;
    }

    /// <summary>
    /// Gets a raw value from state.
    /// </summary>
    /// <param name="key">Property key.</param>
    /// <returns>The value or null if not found.</returns>
    public object? GetValue(string key)
    {
        return _state.TryGetValue(key, out var value) ? value : null;
    }

    /// <summary>
    /// Checks if state contains a key.
    /// </summary>
    /// <param name="key">Property key to check.</param>
    /// <returns>True if key exists.</returns>
    public bool ContainsKey(string key) => _state.ContainsKey(key);

    /// <summary>
    /// Sets a numeric value, returning a new state.
    /// </summary>
    /// <param name="key">Property key.</param>
    /// <param name="value">Numeric value to set.</param>
    /// <returns>New world state with the value set.</returns>
    public WorldState SetNumeric(string key, float value)
    {
        return new WorldState(_state.SetItem(key, value));
    }

    /// <summary>
    /// Sets a boolean value, returning a new state.
    /// </summary>
    /// <param name="key">Property key.</param>
    /// <param name="value">Boolean value to set.</param>
    /// <returns>New world state with the value set.</returns>
    public WorldState SetBoolean(string key, bool value)
    {
        return new WorldState(_state.SetItem(key, value));
    }

    /// <summary>
    /// Sets a string value, returning a new state.
    /// </summary>
    /// <param name="key">Property key.</param>
    /// <param name="value">String value to set.</param>
    /// <returns>New world state with the value set.</returns>
    public WorldState SetString(string key, string value)
    {
        return new WorldState(_state.SetItem(key, value));
    }

    /// <summary>
    /// Sets a value of any type, returning a new state.
    /// </summary>
    /// <param name="key">Property key.</param>
    /// <param name="value">Value to set.</param>
    /// <returns>New world state with the value set.</returns>
    public WorldState SetValue(string key, object value)
    {
        return new WorldState(_state.SetItem(key, value));
    }

    /// <summary>
    /// Applies action effects to this state, returning a new state.
    /// </summary>
    /// <param name="effects">Effects to apply.</param>
    /// <returns>New world state with effects applied.</returns>
    public WorldState ApplyEffects(GoapActionEffects effects)
    {
        var newState = _state.ToBuilder();

        foreach (var (key, effect) in effects.Effects)
        {
            var newValue = effect.Apply(GetValue(key));
            newState[key] = newValue;
        }

        return new WorldState(newState.ToImmutable());
    }

    /// <summary>
    /// Checks if this state satisfies all preconditions.
    /// </summary>
    /// <param name="preconditions">Preconditions to check.</param>
    /// <returns>True if all preconditions are satisfied.</returns>
    public bool SatisfiesPreconditions(GoapPreconditions preconditions)
    {
        foreach (var (key, condition) in preconditions.Conditions)
        {
            var value = GetValue(key);
            if (!condition.Evaluate(value))
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// Checks if this state satisfies a goal.
    /// </summary>
    /// <param name="goal">Goal to check.</param>
    /// <returns>True if all goal conditions are satisfied.</returns>
    public bool SatisfiesGoal(GoapGoal goal)
    {
        foreach (var (key, condition) in goal.Conditions)
        {
            var value = GetValue(key);
            if (!condition.Evaluate(value))
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// Calculates heuristic distance to goal.
    /// Used as H-cost in A* search.
    /// </summary>
    /// <param name="goal">Goal to measure distance to.</param>
    /// <returns>Estimated distance (lower = closer).</returns>
    public float DistanceToGoal(GoapGoal goal)
    {
        var distance = 0f;

        foreach (var (key, condition) in goal.Conditions)
        {
            var value = GetValue(key);
            distance += condition.Distance(value);
        }

        return distance;
    }

    /// <summary>
    /// Gets all keys in this state.
    /// </summary>
    public IEnumerable<string> Keys => _state.Keys;

    /// <summary>
    /// Gets the number of properties in this state.
    /// </summary>
    public int Count => _state.Count;

    /// <inheritdoc/>
    public bool Equals(WorldState? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        if (_cachedHashCode != other._cachedHashCode) return false;
        if (_state.Count != other._state.Count) return false;

        foreach (var (key, value) in _state)
        {
            if (!other._state.TryGetValue(key, out var otherValue))
            {
                return false;
            }
            if (!Equals(value, otherValue))
            {
                return false;
            }
        }

        return true;
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) => Equals(obj as WorldState);

    /// <inheritdoc/>
    public override int GetHashCode() => _cachedHashCode;

    private int ComputeHashCode()
    {
        var hash = new HashCode();
        foreach (var (key, value) in _state.OrderBy(kvp => kvp.Key))
        {
            hash.Add(key);
            hash.Add(value);
        }
        return hash.ToHashCode();
    }

    /// <summary>
    /// Equality operator.
    /// </summary>
    public static bool operator ==(WorldState? left, WorldState? right) =>
        left is null ? right is null : left.Equals(right);

    /// <summary>
    /// Inequality operator.
    /// </summary>
    public static bool operator !=(WorldState? left, WorldState? right) =>
        !(left == right);
}
