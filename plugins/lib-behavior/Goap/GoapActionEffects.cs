// ═══════════════════════════════════════════════════════════════════════════
// GOAP Action Effects
// Represents effects that actions apply to world state.
// ═══════════════════════════════════════════════════════════════════════════

using System.Globalization;

namespace BeyondImmersion.Bannou.Behavior.Goap;

/// <summary>
/// Type of effect to apply to world state.
/// </summary>
public enum EffectType
{
    /// <summary>Set to an absolute value.</summary>
    Set,

    /// <summary>Add to current value (delta).</summary>
    Add,

    /// <summary>Subtract from current value (delta).</summary>
    Subtract
}

/// <summary>
/// Represents a single effect on a world state property.
/// </summary>
public sealed class GoapEffect
{
    /// <summary>
    /// Type of effect.
    /// </summary>
    public EffectType Type { get; }

    /// <summary>
    /// The value to apply.
    /// </summary>
    public object Value { get; }

    /// <summary>
    /// Creates a new effect.
    /// </summary>
    /// <param name="type">Effect type.</param>
    /// <param name="value">Value to apply.</param>
    public GoapEffect(EffectType type, object value)
    {
        Type = type;
        Value = value;
    }

    /// <summary>
    /// Parses an effect string into a GoapEffect.
    /// </summary>
    /// <param name="effectString">Effect like "-0.8", "+5", "tavern", "true".</param>
    /// <returns>Parsed effect.</returns>
    public static GoapEffect Parse(string effectString)
    {
        ArgumentNullException.ThrowIfNull(effectString);

        var trimmed = effectString.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            throw new FormatException("Effect string cannot be empty");
        }

        // Check for delta notation
        if (trimmed.StartsWith("+", StringComparison.Ordinal))
        {
            var valueStr = trimmed[1..];
            if (float.TryParse(valueStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var addValue))
            {
                return new GoapEffect(EffectType.Add, addValue);
            }
            throw new FormatException($"Invalid add delta value: {valueStr}");
        }

        if (trimmed.StartsWith("-", StringComparison.Ordinal))
        {
            var valueStr = trimmed[1..];
            if (float.TryParse(valueStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var subValue))
            {
                return new GoapEffect(EffectType.Subtract, subValue);
            }
            throw new FormatException($"Invalid subtract delta value: {valueStr}");
        }

        // Parse as absolute value
        var absoluteValue = ParseAbsoluteValue(trimmed);
        return new GoapEffect(EffectType.Set, absoluteValue);
    }

    /// <summary>
    /// Tries to parse an effect string.
    /// </summary>
    /// <param name="effectString">Effect string to parse.</param>
    /// <param name="effect">Parsed effect if successful.</param>
    /// <returns>True if parsing succeeded.</returns>
    public static bool TryParse(string effectString, out GoapEffect? effect)
    {
        effect = null;
        if (string.IsNullOrWhiteSpace(effectString))
        {
            return false;
        }

        try
        {
            effect = Parse(effectString);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Applies this effect to a current value.
    /// </summary>
    /// <param name="currentValue">The current value from world state.</param>
    /// <returns>The new value after applying the effect.</returns>
    public object Apply(object? currentValue)
    {
        switch (Type)
        {
            case EffectType.Set:
                return Value;

            case EffectType.Add:
                var addAmount = (float)Value;
                var currentAdd = GetNumeric(currentValue);
                return currentAdd + addAmount;

            case EffectType.Subtract:
                var subAmount = (float)Value;
                var currentSub = GetNumeric(currentValue);
                return currentSub - subAmount;

            default:
                return Value;
        }
    }

    private static float GetNumeric(object? value)
    {
        return value switch
        {
            float f => f,
            double d => (float)d,
            int i => i,
            long l => l,
            decimal dec => (float)dec,
            string s when float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => 0f
        };
    }

    private static object ParseAbsoluteValue(string valueStr)
    {
        // Try boolean
        if (valueStr.Equals("true", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        if (valueStr.Equals("false", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // Try integer
        if (int.TryParse(valueStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intValue))
        {
            return intValue;
        }

        // Try float (only if it has decimal point or scientific notation)
        if (valueStr.Contains('.') || valueStr.Contains('e', StringComparison.OrdinalIgnoreCase))
        {
            if (float.TryParse(valueStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var floatValue))
            {
                return floatValue;
            }
        }

        // Strip quotes if present
        if ((valueStr.StartsWith("'") && valueStr.EndsWith("'")) ||
            (valueStr.StartsWith("\"") && valueStr.EndsWith("\"")))
        {
            return valueStr[1..^1];
        }

        // Return as string
        return valueStr;
    }

    /// <inheritdoc/>
    public override string ToString()
    {
        return Type switch
        {
            EffectType.Add => $"+{Value}",
            EffectType.Subtract => $"-{Value}",
            EffectType.Set => Value.ToString() ?? "null",
            _ => Value.ToString() ?? "?"
        };
    }
}

/// <summary>
/// Container for action effects.
/// Maps world state property names to their effects.
/// </summary>
public sealed class GoapActionEffects
{
    private readonly Dictionary<string, GoapEffect> _effects;

    /// <summary>
    /// Creates empty effects container.
    /// </summary>
    public GoapActionEffects()
    {
        _effects = new Dictionary<string, GoapEffect>();
    }

    /// <summary>
    /// Creates effects from a dictionary of effect strings.
    /// </summary>
    /// <param name="effectStrings">Map of property names to effect strings.</param>
    /// <returns>Parsed effects container.</returns>
    public static GoapActionEffects FromDictionary(IReadOnlyDictionary<string, string> effectStrings)
    {
        var effects = new GoapActionEffects();
        foreach (var (key, value) in effectStrings)
        {
            effects.AddEffect(key, GoapEffect.Parse(value));
        }
        return effects;
    }

    /// <summary>
    /// Adds an effect for a property.
    /// </summary>
    /// <param name="propertyName">World state property name.</param>
    /// <param name="effect">Effect to apply.</param>
    public void AddEffect(string propertyName, GoapEffect effect)
    {
        _effects[propertyName] = effect;
    }

    /// <summary>
    /// Gets all effects as key-value pairs.
    /// </summary>
    public IEnumerable<KeyValuePair<string, GoapEffect>> Effects => _effects;

    /// <summary>
    /// Gets the number of effects.
    /// </summary>
    public int Count => _effects.Count;

    /// <summary>
    /// Checks if there's an effect for a property.
    /// </summary>
    /// <param name="propertyName">Property name to check.</param>
    /// <returns>True if an effect exists for this property.</returns>
    public bool HasEffect(string propertyName) => _effects.ContainsKey(propertyName);

    /// <summary>
    /// Gets the effect for a property.
    /// </summary>
    /// <param name="propertyName">Property name.</param>
    /// <returns>The effect, or null if not found.</returns>
    public GoapEffect? GetEffect(string propertyName)
    {
        return _effects.TryGetValue(propertyName, out var effect) ? effect : null;
    }
}
