namespace BeyondImmersion.Bannou.StorylineTheory.State;

/// <summary>
/// Six-dimensional narrative state space for story beat tracking.
/// Parallel to EmotionalState in music-storyteller SDK.
/// All dimensions normalized 0.0 to 1.0.
/// </summary>
public sealed class NarrativeState
{
    private double _tension;
    private double _stakes;
    private double _mystery;
    private double _urgency;
    private double _intimacy;
    private double _hope;

    /// <summary>
    /// Conflict intensity. 0 = resolved/peaceful, 1 = climactic confrontation.
    /// Maps to: unresolved conflicts, active threats, confrontation proximity.
    /// </summary>
    public double Tension
    {
        get => _tension;
        set => _tension = Clamp(value);
    }

    /// <summary>
    /// What's at risk. 0 = trivial consequences, 1 = existential/irreversible.
    /// Maps to: life/death, love/loss, honor/shame magnitude.
    /// </summary>
    public double Stakes
    {
        get => _stakes;
        set => _stakes = Clamp(value);
    }

    /// <summary>
    /// Unanswered questions. 0 = everything clear, 1 = deep enigma.
    /// Maps to: hidden information, unrevealed secrets, unclear motivations.
    /// </summary>
    public double Mystery
    {
        get => _mystery;
        set => _mystery = Clamp(value);
    }

    /// <summary>
    /// Time pressure. 0 = leisurely/no deadline, 1 = desperate/immediate.
    /// Maps to: countdown timers, approaching threats, windows closing.
    /// </summary>
    public double Urgency
    {
        get => _urgency;
        set => _urgency = Clamp(value);
    }

    /// <summary>
    /// Character emotional closeness. 0 = strangers/enemies, 1 = deeply bonded.
    /// Maps to: relationship depth, trust levels, shared history.
    /// </summary>
    public double Intimacy
    {
        get => _intimacy;
        set => _intimacy = Clamp(value);
    }

    /// <summary>
    /// Expected outcome valence. 0 = despair/doom, 1 = hope/triumph.
    /// Maps to: protagonist advantage, resource availability, ally support.
    /// </summary>
    public double Hope
    {
        get => _hope;
        set => _hope = Clamp(value);
    }

    /// <summary>
    /// Creates a new narrative state with default equilibrium values.
    /// </summary>
    public NarrativeState()
    {
        _tension = 0.2;
        _stakes = 0.3;
        _mystery = 0.2;
        _urgency = 0.2;
        _intimacy = 0.5;
        _hope = 0.7;
    }

    /// <summary>
    /// Creates a new narrative state with specified values.
    /// </summary>
    public NarrativeState(
        double tension,
        double stakes,
        double mystery,
        double urgency,
        double intimacy,
        double hope)
    {
        _tension = Clamp(tension);
        _stakes = Clamp(stakes);
        _mystery = Clamp(mystery);
        _urgency = Clamp(urgency);
        _intimacy = Clamp(intimacy);
        _hope = Clamp(hope);
    }

    /// <summary>
    /// Calculates the Euclidean distance to another narrative state.
    /// Useful for GOAP heuristics and measuring how far current state is from target.
    /// </summary>
    /// <param name="target">The target narrative state.</param>
    /// <returns>Distance in the range [0, sqrt(6)] (~2.45 max).</returns>
    public double DistanceTo(NarrativeState target)
    {
        var dt = Tension - target.Tension;
        var ds = Stakes - target.Stakes;
        var dm = Mystery - target.Mystery;
        var du = Urgency - target.Urgency;
        var di = Intimacy - target.Intimacy;
        var dh = Hope - target.Hope;

        return Math.Sqrt(dt * dt + ds * ds + dm * dm + du * du + di * di + dh * dh);
    }

    /// <summary>
    /// Calculates the normalized distance (0-1) to another narrative state.
    /// </summary>
    /// <param name="target">The target narrative state.</param>
    /// <returns>Normalized distance from 0 (identical) to 1 (maximum difference).</returns>
    public double NormalizedDistanceTo(NarrativeState target)
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
    /// <returns>A new interpolated narrative state.</returns>
    public NarrativeState InterpolateTo(NarrativeState target, double t)
    {
        t = Clamp(t);
        return new NarrativeState(
            Lerp(Tension, target.Tension, t),
            Lerp(Stakes, target.Stakes, t),
            Lerp(Mystery, target.Mystery, t),
            Lerp(Urgency, target.Urgency, t),
            Lerp(Intimacy, target.Intimacy, t),
            Lerp(Hope, target.Hope, t));
    }

    /// <summary>
    /// Applies a delta change to all dimensions.
    /// Delta values are centered at 0.5 (no change), so 0.7 adds 0.2, 0.3 subtracts 0.2.
    /// </summary>
    /// <param name="delta">The delta state containing changes to apply.</param>
    /// <returns>A new state with the delta applied.</returns>
    public NarrativeState ApplyDelta(NarrativeState delta)
    {
        return new NarrativeState(
            Tension + delta.Tension - 0.5,
            Stakes + delta.Stakes - 0.5,
            Mystery + delta.Mystery - 0.5,
            Urgency + delta.Urgency - 0.5,
            Intimacy + delta.Intimacy - 0.5,
            Hope + delta.Hope - 0.5);
    }

    /// <summary>
    /// Creates a deep copy of this narrative state.
    /// </summary>
    public NarrativeState Clone() =>
        new(Tension, Stakes, Mystery, Urgency, Intimacy, Hope);

    /// <summary>
    /// Gets the equilibrium preset (convenience shorthand).
    /// </summary>
    public static NarrativeState Equilibrium => Presets.Equilibrium;

    /// <summary>
    /// Common narrative state presets.
    /// </summary>
    public static class Presets
    {
        /// <summary>Stable starting point.</summary>
        public static NarrativeState Equilibrium => new(0.2, 0.3, 0.2, 0.2, 0.5, 0.7);

        /// <summary>Building toward climax.</summary>
        public static NarrativeState RisingAction => new(0.5, 0.5, 0.5, 0.5, 0.5, 0.5);

        /// <summary>Maximum tension climax point.</summary>
        public static NarrativeState Climax => new(0.95, 0.9, 0.3, 0.9, 0.8, 0.4);

        /// <summary>Resolution after climax.</summary>
        public static NarrativeState Resolution => new(0.1, 0.2, 0.1, 0.1, 0.9, 0.85);

        /// <summary>Tragic ending.</summary>
        public static NarrativeState Tragedy => new(0.1, 0.3, 0.1, 0.1, 0.8, 0.1);

        /// <summary>Mystery opening with high intrigue.</summary>
        public static NarrativeState MysteryHook => new(0.4, 0.4, 0.9, 0.3, 0.3, 0.5);

        /// <summary>All Is Lost moment (Save the Cat).</summary>
        public static NarrativeState DarkestHour => new(0.7, 0.9, 0.2, 0.8, 0.7, 0.1);

        /// <summary>Hero at Mercy of Villain (Story Grid).</summary>
        public static NarrativeState HeroAtMercy => new(0.9, 0.95, 0.1, 0.95, 0.6, 0.15);
    }

    private static double Clamp(double value) => Math.Clamp(value, 0.0, 1.0);
    private static double Lerp(double a, double b, double t) => a + (b - a) * t;

    /// <inheritdoc />
    public override string ToString() =>
        $"Narrative[T={Tension:F2}, S={Stakes:F2}, M={Mystery:F2}, U={Urgency:F2}, I={Intimacy:F2}, H={Hope:F2}]";
}
