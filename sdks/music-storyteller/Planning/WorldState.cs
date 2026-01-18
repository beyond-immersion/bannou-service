using BeyondImmersion.Bannou.MusicStoryteller.State;

namespace BeyondImmersion.Bannou.MusicStoryteller.Planning;

/// <summary>
/// Represents a snapshot of the world state for GOAP planning.
/// Contains key-value pairs that describe the current situation.
/// Source: Plan Phase 6 - GOAP Planning
/// </summary>
public sealed class WorldState
{
    private readonly Dictionary<string, object> _values = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets a value from the world state.
    /// </summary>
    /// <typeparam name="T">The type of value.</typeparam>
    /// <param name="key">The key.</param>
    /// <returns>The value, or default if not found.</returns>
    public T? Get<T>(string key)
    {
        if (_values.TryGetValue(key, out var value) && value is T typedValue)
        {
            return typedValue;
        }
        return default;
    }

    /// <summary>
    /// Gets a value from the world state with a default.
    /// </summary>
    /// <typeparam name="T">The type of value.</typeparam>
    /// <param name="key">The key.</param>
    /// <param name="defaultValue">Default value if not found.</param>
    /// <returns>The value, or the default.</returns>
    public T Get<T>(string key, T defaultValue)
    {
        if (_values.TryGetValue(key, out var value) && value is T typedValue)
        {
            return typedValue;
        }
        return defaultValue;
    }

    /// <summary>
    /// Sets a value in the world state.
    /// </summary>
    /// <typeparam name="T">The type of value.</typeparam>
    /// <param name="key">The key.</param>
    /// <param name="value">The value.</param>
    public void Set<T>(string key, T value) where T : notnull
    {
        _values[key] = value;
    }

    /// <summary>
    /// Removes a value from the world state.
    /// </summary>
    /// <param name="key">The key to remove.</param>
    /// <returns>True if removed, false if not found.</returns>
    public bool Remove(string key)
    {
        return _values.Remove(key);
    }

    /// <summary>
    /// Checks if a key exists in the world state.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <returns>True if exists.</returns>
    public bool Contains(string key)
    {
        return _values.ContainsKey(key);
    }

    /// <summary>
    /// Gets all keys in the world state.
    /// </summary>
    public IEnumerable<string> AllKeys => _values.Keys;

    /// <summary>
    /// Creates a clone of this world state.
    /// </summary>
    /// <returns>A new WorldState with the same values.</returns>
    public WorldState Clone()
    {
        var clone = new WorldState();
        foreach (var kvp in _values)
        {
            clone._values[kvp.Key] = kvp.Value;
        }
        return clone;
    }

    /// <summary>
    /// Calculates the distance to a target world state.
    /// Used for A* heuristic and goal checking.
    /// </summary>
    /// <param name="target">The target state.</param>
    /// <returns>Distance (0 = states match, higher = more different).</returns>
    public double DistanceTo(WorldState target)
    {
        var distance = 0.0;

        foreach (var key in target.AllKeys)
        {
            if (!_values.TryGetValue(key, out var currentValue))
            {
                // Missing key in current state
                distance += 1.0;
                continue;
            }

            var targetValue = target._values[key];

            if (currentValue is double currentDouble && targetValue is double targetDouble)
            {
                // Numeric comparison
                distance += Math.Abs(currentDouble - targetDouble);
            }
            else if (currentValue is bool currentBool && targetValue is bool targetBool)
            {
                // Boolean comparison
                distance += currentBool == targetBool ? 0 : 1.0;
            }
            else if (!Equals(currentValue, targetValue))
            {
                // Object comparison
                distance += 1.0;
            }
        }

        return distance;
    }

    /// <summary>
    /// Checks if this state satisfies a goal state.
    /// All keys in the goal must match (or be within tolerance for doubles).
    /// </summary>
    /// <param name="goal">The goal state.</param>
    /// <param name="tolerance">Tolerance for numeric comparisons.</param>
    /// <returns>True if the goal is satisfied.</returns>
    public bool Satisfies(WorldState goal, double tolerance = 0.05)
    {
        foreach (var key in goal.AllKeys)
        {
            if (!_values.TryGetValue(key, out var currentValue))
            {
                return false;
            }

            var goalValue = goal._values[key];

            if (currentValue is double currentDouble && goalValue is double goalDouble)
            {
                if (Math.Abs(currentDouble - goalDouble) > tolerance)
                {
                    return false;
                }
            }
            else if (!Equals(currentValue, goalValue))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Merges another world state into this one.
    /// Values from the other state override existing values.
    /// </summary>
    /// <param name="other">The state to merge in.</param>
    public void Merge(WorldState other)
    {
        foreach (var kvp in other._values)
        {
            _values[kvp.Key] = kvp.Value;
        }
    }

    /// <summary>
    /// Creates a WorldState from a CompositionState.
    /// Extracts key values for GOAP planning.
    /// </summary>
    /// <param name="state">The composition state.</param>
    /// <returns>A WorldState representation.</returns>
    public static WorldState FromCompositionState(CompositionState state)
    {
        var world = new WorldState();

        // Emotional dimensions
        world.Set("tension", state.Emotional.Tension);
        world.Set("brightness", state.Emotional.Brightness);
        world.Set("energy", state.Emotional.Energy);
        world.Set("warmth", state.Emotional.Warmth);
        world.Set("stability", state.Emotional.Stability);
        world.Set("valence", state.Emotional.Valence);

        // Position
        world.Set("bar", (double)state.Position.Bar);
        world.Set("progress", state.Position.Progress);

        // Harmonic state
        world.Set("on_tonic", state.Harmonic.OnTonic);
        world.Set("on_dominant", state.Harmonic.OnDominant);
        world.Set("expecting_cadence", state.Harmonic.ExpectingCadence);
        world.Set("key_distance", (double)state.Harmonic.KeyDistance);

        // Thematic state
        world.Set("main_motif_introduced", state.Thematic.MainMotif != null);
        world.Set("development_stage", state.Thematic.Stage.ToString());

        // Listener state
        world.Set("attention", state.Listener.Attention);
        world.Set("surprise_budget", state.Listener.SurpriseBudget);

        return world;
    }

    /// <summary>
    /// Creates a WorldState representing emotional targets from a phase.
    /// </summary>
    /// <param name="emotional">The target emotional state.</param>
    /// <returns>A WorldState with emotional dimension targets.</returns>
    public static WorldState FromEmotionalTarget(EmotionalState emotional)
    {
        var world = new WorldState();
        world.Set("tension", emotional.Tension);
        world.Set("brightness", emotional.Brightness);
        world.Set("energy", emotional.Energy);
        world.Set("warmth", emotional.Warmth);
        world.Set("stability", emotional.Stability);
        world.Set("valence", emotional.Valence);
        return world;
    }

    /// <summary>
    /// Standard world state keys.
    /// </summary>
    public static class Keys
    {
        /// <summary>Tension level (0-1).</summary>
        public const string Tension = "tension";

        /// <summary>Brightness level (0-1).</summary>
        public const string Brightness = "brightness";

        /// <summary>Energy level (0-1).</summary>
        public const string Energy = "energy";

        /// <summary>Warmth level (0-1).</summary>
        public const string Warmth = "warmth";

        /// <summary>Stability level (0-1).</summary>
        public const string Stability = "stability";

        /// <summary>Valence level (0-1).</summary>
        public const string Valence = "valence";

        /// <summary>Current bar number.</summary>
        public const string Bar = "bar";

        /// <summary>Progress through composition (0-1).</summary>
        public const string Progress = "progress";

        /// <summary>Whether currently on tonic.</summary>
        public const string OnTonic = "on_tonic";

        /// <summary>Whether currently on dominant.</summary>
        public const string OnDominant = "on_dominant";

        /// <summary>Whether a cadence is expected.</summary>
        public const string ExpectingCadence = "expecting_cadence";

        /// <summary>Distance from home key.</summary>
        public const string KeyDistance = "key_distance";

        /// <summary>Whether main motif has been introduced.</summary>
        public const string MainMotifIntroduced = "main_motif_introduced";

        /// <summary>Current development stage.</summary>
        public const string DevelopmentStage = "development_stage";

        /// <summary>Listener attention level.</summary>
        public const string Attention = "attention";

        /// <summary>Remaining surprise budget.</summary>
        public const string SurpriseBudget = "surprise_budget";
    }
}
