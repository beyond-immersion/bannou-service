namespace BeyondImmersion.Bannou.MusicStoryteller.State;

/// <summary>
/// Represents a 6-dimensional emotional space for music composition.
/// Each dimension is validated by psychology and music cognition research.
/// </summary>
public sealed class EmotionalState
{
    private double _tension;
    private double _brightness;
    private double _energy;
    private double _warmth;
    private double _stability;
    private double _valence;

    /// <summary>
    /// Tension level from resolution (0) to climax (1).
    /// Related to harmonic tension, dissonance, and anticipation.
    /// </summary>
    public double Tension
    {
        get => _tension;
        set => _tension = Clamp(value);
    }

    /// <summary>
    /// Brightness level from dark (0) to bright (1).
    /// Related to major/minor mode, register, and timbre.
    /// </summary>
    public double Brightness
    {
        get => _brightness;
        set => _brightness = Clamp(value);
    }

    /// <summary>
    /// Energy level from calm (0) to energetic (1).
    /// Related to tempo, rhythmic density, and dynamics.
    /// </summary>
    public double Energy
    {
        get => _energy;
        set => _energy = Clamp(value);
    }

    /// <summary>
    /// Warmth level from distant (0) to intimate (1).
    /// Related to consonance, close voicing, and lyrical melody.
    /// </summary>
    public double Warmth
    {
        get => _warmth;
        set => _warmth = Clamp(value);
    }

    /// <summary>
    /// Stability level from unstable (0) to grounded (1).
    /// Related to tonic vs. non-tonic harmony, metric regularity.
    /// </summary>
    public double Stability
    {
        get => _stability;
        set => _stability = Clamp(value);
    }

    /// <summary>
    /// Valence level from negative (0) to positive (1).
    /// The overall emotional character from sad/dark to happy/bright.
    /// </summary>
    public double Valence
    {
        get => _valence;
        set => _valence = Clamp(value);
    }

    /// <summary>
    /// Creates a new emotional state with default neutral values.
    /// </summary>
    public EmotionalState()
    {
        _tension = 0.2;
        _brightness = 0.5;
        _energy = 0.5;
        _warmth = 0.5;
        _stability = 0.8;
        _valence = 0.5;
    }

    /// <summary>
    /// Creates a new emotional state with specified values.
    /// </summary>
    public EmotionalState(
        double tension,
        double brightness,
        double energy,
        double warmth,
        double stability,
        double valence)
    {
        _tension = Clamp(tension);
        _brightness = Clamp(brightness);
        _energy = Clamp(energy);
        _warmth = Clamp(warmth);
        _stability = Clamp(stability);
        _valence = Clamp(valence);
    }

    /// <summary>
    /// Calculates the Euclidean distance to another emotional state.
    /// Useful for measuring how far the current state is from a target.
    /// </summary>
    /// <param name="target">The target emotional state.</param>
    /// <returns>Distance in the range [0, sqrt(6)] (~2.45 max).</returns>
    public double DistanceTo(EmotionalState target)
    {
        var dt = Tension - target.Tension;
        var db = Brightness - target.Brightness;
        var de = Energy - target.Energy;
        var dw = Warmth - target.Warmth;
        var ds = Stability - target.Stability;
        var dv = Valence - target.Valence;

        return Math.Sqrt(dt * dt + db * db + de * de + dw * dw + ds * ds + dv * dv);
    }

    /// <summary>
    /// Calculates the normalized distance (0-1) to another emotional state.
    /// </summary>
    /// <param name="target">The target emotional state.</param>
    /// <returns>Normalized distance from 0 (identical) to 1 (maximum difference).</returns>
    public double NormalizedDistanceTo(EmotionalState target)
    {
        // Maximum possible distance is sqrt(6) when all dimensions differ by 1
        const double maxDistance = 2.449489742783178; // sqrt(6)
        return DistanceTo(target) / maxDistance;
    }

    /// <summary>
    /// Linearly interpolates between this state and a target state.
    /// </summary>
    /// <param name="target">The target state to interpolate towards.</param>
    /// <param name="t">Interpolation factor from 0 (this state) to 1 (target state).</param>
    /// <returns>A new interpolated emotional state.</returns>
    public EmotionalState InterpolateTo(EmotionalState target, double t)
    {
        t = Clamp(t);
        return new EmotionalState(
            Lerp(Tension, target.Tension, t),
            Lerp(Brightness, target.Brightness, t),
            Lerp(Energy, target.Energy, t),
            Lerp(Warmth, target.Warmth, t),
            Lerp(Stability, target.Stability, t),
            Lerp(Valence, target.Valence, t));
    }

    /// <summary>
    /// Applies a delta change to all dimensions.
    /// </summary>
    /// <param name="delta">The delta state containing changes to apply.</param>
    /// <returns>A new state with the delta applied.</returns>
    public EmotionalState ApplyDelta(EmotionalState delta)
    {
        return new EmotionalState(
            Tension + delta.Tension - 0.5,
            Brightness + delta.Brightness - 0.5,
            Energy + delta.Energy - 0.5,
            Warmth + delta.Warmth - 0.5,
            Stability + delta.Stability - 0.5,
            Valence + delta.Valence - 0.5);
    }

    /// <summary>
    /// Creates a deep copy of this emotional state.
    /// </summary>
    public EmotionalState Clone()
    {
        return new EmotionalState(Tension, Brightness, Energy, Warmth, Stability, Valence);
    }

    /// <summary>
    /// Gets a neutral emotional state (convenience shorthand).
    /// </summary>
    public static EmotionalState Neutral => Presets.Neutral;

    /// <summary>
    /// Common emotional state presets.
    /// </summary>
    public static class Presets
    {
        /// <summary>Neutral starting point.</summary>
        public static EmotionalState Neutral => new(0.2, 0.5, 0.5, 0.5, 0.8, 0.5);

        /// <summary>High tension, low stability.</summary>
        public static EmotionalState Tense => new(0.8, 0.4, 0.7, 0.3, 0.2, 0.4);

        /// <summary>Low tension, high stability, warm.</summary>
        public static EmotionalState Peaceful => new(0.1, 0.6, 0.3, 0.8, 0.9, 0.7);

        /// <summary>High energy, bright, positive.</summary>
        public static EmotionalState Joyful => new(0.3, 0.8, 0.8, 0.7, 0.7, 0.9);

        /// <summary>Low energy, dark, negative valence.</summary>
        public static EmotionalState Melancholic => new(0.3, 0.2, 0.2, 0.5, 0.6, 0.2);

        /// <summary>Maximum tension climax point.</summary>
        public static EmotionalState Climax => new(0.95, 0.6, 0.9, 0.4, 0.1, 0.5);

        /// <summary>Resolution after climax.</summary>
        public static EmotionalState Resolution => new(0.1, 0.6, 0.4, 0.7, 0.95, 0.7);
    }

    private static double Clamp(double value) => Math.Clamp(value, 0.0, 1.0);
    private static double Lerp(double a, double b, double t) => a + (b - a) * t;

    /// <inheritdoc />
    public override string ToString()
    {
        return $"Emotional[T={Tension:F2}, B={Brightness:F2}, E={Energy:F2}, W={Warmth:F2}, S={Stability:F2}, V={Valence:F2}]";
    }
}
