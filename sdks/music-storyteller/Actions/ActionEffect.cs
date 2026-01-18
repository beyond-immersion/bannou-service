using BeyondImmersion.Bannou.MusicStoryteller.State;

namespace BeyondImmersion.Bannou.MusicStoryteller.Actions;

/// <summary>
/// Represents an effect that an action has on the composition state.
/// Effects can be fixed values, relative changes, or formula-based.
/// </summary>
public sealed class ActionEffect
{
    /// <summary>
    /// The dimension being affected (e.g., "tension", "brightness").
    /// </summary>
    public string Dimension { get; }

    /// <summary>
    /// The type of effect.
    /// </summary>
    public EffectType Type { get; }

    /// <summary>
    /// The magnitude of the effect (interpretation depends on Type).
    /// </summary>
    public double Magnitude { get; }

    /// <summary>
    /// Optional formula for calculating the effect (for complex effects).
    /// </summary>
    public Func<CompositionState, double>? Formula { get; }

    /// <summary>
    /// Research source citation for this effect.
    /// </summary>
    public string? Source { get; }

    /// <summary>
    /// Creates a fixed-value effect.
    /// </summary>
    /// <param name="dimension">The dimension to affect.</param>
    /// <param name="type">The type of effect.</param>
    /// <param name="magnitude">The magnitude.</param>
    /// <param name="source">Optional source citation.</param>
    public ActionEffect(string dimension, EffectType type, double magnitude, string? source = null)
    {
        Dimension = dimension;
        Type = type;
        Magnitude = magnitude;
        Source = source;
    }

    /// <summary>
    /// Creates a formula-based effect.
    /// </summary>
    /// <param name="dimension">The dimension to affect.</param>
    /// <param name="formula">Function to calculate the effect.</param>
    /// <param name="source">Optional source citation.</param>
    public ActionEffect(string dimension, Func<CompositionState, double> formula, string? source = null)
    {
        Dimension = dimension;
        Type = EffectType.Formula;
        Formula = formula;
        Source = source;
    }

    /// <summary>
    /// Gets the predicted value for this effect given the current state.
    /// </summary>
    /// <param name="state">The current state.</param>
    /// <returns>The predicted change value.</returns>
    public double GetPredictedValue(CompositionState state)
    {
        if (Formula != null)
        {
            return Formula(state);
        }

        return Type switch
        {
            EffectType.Absolute => Magnitude,
            EffectType.Relative => Magnitude,
            EffectType.Multiply => GetCurrentValue(state, Dimension) * Magnitude - GetCurrentValue(state, Dimension),
            _ => Magnitude
        };
    }

    /// <summary>
    /// Applies this effect to the composition state.
    /// </summary>
    /// <param name="state">The state to modify.</param>
    public void Apply(CompositionState state)
    {
        var currentValue = GetCurrentValue(state, Dimension);
        double newValue;

        if (Formula != null)
        {
            newValue = currentValue + Formula(state);
        }
        else
        {
            newValue = Type switch
            {
                EffectType.Absolute => Magnitude,
                EffectType.Relative => currentValue + Magnitude,
                EffectType.Multiply => currentValue * Magnitude,
                _ => currentValue + Magnitude
            };
        }

        SetValue(state, Dimension, Math.Clamp(newValue, 0.0, 1.0));
    }

    /// <summary>
    /// Creates a tension increase effect.
    /// </summary>
    public static ActionEffect TensionIncrease(double amount, string? source = null)
        => new("tension", EffectType.Relative, amount, source);

    /// <summary>
    /// Creates a tension decrease effect.
    /// </summary>
    public static ActionEffect TensionDecrease(double amount, string? source = null)
        => new("tension", EffectType.Relative, -amount, source);

    /// <summary>
    /// Creates a brightness change effect.
    /// </summary>
    public static ActionEffect BrightnessChange(double amount, string? source = null)
        => new("brightness", EffectType.Relative, amount, source);

    /// <summary>
    /// Creates an energy change effect.
    /// </summary>
    public static ActionEffect EnergyChange(double amount, string? source = null)
        => new("energy", EffectType.Relative, amount, source);

    /// <summary>
    /// Creates a stability change effect.
    /// </summary>
    public static ActionEffect StabilityChange(double amount, string? source = null)
        => new("stability", EffectType.Relative, amount, source);

    /// <summary>
    /// Creates a warmth change effect.
    /// </summary>
    public static ActionEffect WarmthChange(double amount, string? source = null)
        => new("warmth", EffectType.Relative, amount, source);

    /// <summary>
    /// Creates a valence change effect.
    /// </summary>
    public static ActionEffect ValenceChange(double amount, string? source = null)
        => new("valence", EffectType.Relative, amount, source);

    /// <summary>
    /// Creates a contrastive valence effect (resolution pleasure based on prior tension).
    /// </summary>
    public static ActionEffect ContrastiveValence(string? source = null)
        => new("valence", state => state.Emotional.Tension * 0.3, source ?? "Huron (2006)");

    private static double GetCurrentValue(CompositionState state, string dimension)
    {
        return dimension.ToLowerInvariant() switch
        {
            "tension" => state.Emotional.Tension,
            "brightness" => state.Emotional.Brightness,
            "energy" => state.Emotional.Energy,
            "warmth" => state.Emotional.Warmth,
            "stability" => state.Emotional.Stability,
            "valence" => state.Emotional.Valence,
            _ => 0.5
        };
    }

    private static void SetValue(CompositionState state, string dimension, double value)
    {
        switch (dimension.ToLowerInvariant())
        {
            case "tension":
                state.Emotional.Tension = value;
                break;
            case "brightness":
                state.Emotional.Brightness = value;
                break;
            case "energy":
                state.Emotional.Energy = value;
                break;
            case "warmth":
                state.Emotional.Warmth = value;
                break;
            case "stability":
                state.Emotional.Stability = value;
                break;
            case "valence":
                state.Emotional.Valence = value;
                break;
        }
    }

    public override string ToString()
    {
        return Type switch
        {
            EffectType.Absolute => $"{Dimension}={Magnitude:F2}",
            EffectType.Relative => $"{Dimension}{(Magnitude >= 0 ? "+" : "")}{Magnitude:F2}",
            EffectType.Multiply => $"{Dimension}×{Magnitude:F2}",
            EffectType.Formula => $"{Dimension}=f(state)",
            _ => $"{Dimension}?{Magnitude:F2}"
        };
    }
}

/// <summary>
/// Types of effect application.
/// </summary>
public enum EffectType
{
    /// <summary>Set to absolute value.</summary>
    Absolute,

    /// <summary>Add to current value.</summary>
    Relative,

    /// <summary>Multiply current value.</summary>
    Multiply,

    /// <summary>Apply a formula.</summary>
    Formula
}
