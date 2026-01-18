namespace BeyondImmersion.Bannou.MusicStoryteller.Intent;

/// <summary>
/// Melodic intent expressing desired melodic behavior.
/// Passed to the music-theory SDK for generation.
/// </summary>
public sealed record MelodicIntent
{
    /// <summary>
    /// Gets the target melodic contour.
    /// </summary>
    public MelodicContour Contour { get; init; } = MelodicContour.Balanced;

    /// <summary>
    /// Gets the preferred range relative to the motif.
    /// </summary>
    public MelodicRange Range { get; init; } = MelodicRange.Middle;

    /// <summary>
    /// Gets the target rhythmic density (notes per beat).
    /// </summary>
    public double RhythmicDensity { get; init; } = 1.0;

    /// <summary>
    /// Gets whether conjunct (stepwise) motion is preferred.
    /// </summary>
    public bool PreferConjunctMotion { get; init; } = true;

    /// <summary>
    /// Gets the maximum interval size (in semitones).
    /// </summary>
    public int MaxIntervalSize { get; init; } = 7;

    /// <summary>
    /// Gets whether to emphasize chord tones.
    /// </summary>
    public bool EmphasizeChordTones { get; init; } = true;

    /// <summary>
    /// Gets the target tension level for dissonance.
    /// </summary>
    public double DissonanceLevel { get; init; } = 0.3;

    /// <summary>
    /// Gets whether to use syncopation.
    /// </summary>
    public bool AllowSyncopation { get; init; }

    /// <summary>
    /// Gets whether to end on a stable note (tonic/third/fifth).
    /// </summary>
    public bool EndOnStable { get; init; } = true;

    /// <summary>
    /// Gets the target energy level for melodic activity.
    /// </summary>
    public double EnergyLevel { get; init; } = 0.5;

    /// <summary>
    /// Gets specific scale degrees to emphasize.
    /// </summary>
    public IReadOnlyList<int> EmphasizedDegrees { get; init; } = [];

    /// <summary>
    /// Creates a default melodic intent.
    /// </summary>
    public static MelodicIntent Default => new();

    /// <summary>
    /// Creates intent for a calm, lyrical melody.
    /// </summary>
    public static MelodicIntent Lyrical => new()
    {
        Contour = MelodicContour.Balanced,
        Range = MelodicRange.Middle,
        RhythmicDensity = 0.75,
        PreferConjunctMotion = true,
        MaxIntervalSize = 5,
        DissonanceLevel = 0.1,
        EnergyLevel = 0.3
    };

    /// <summary>
    /// Creates intent for an energetic, driving melody.
    /// </summary>
    public static MelodicIntent Driving => new()
    {
        Contour = MelodicContour.Ascending,
        Range = MelodicRange.High,
        RhythmicDensity = 1.5,
        PreferConjunctMotion = false,
        MaxIntervalSize = 9,
        AllowSyncopation = true,
        DissonanceLevel = 0.4,
        EnergyLevel = 0.8
    };

    /// <summary>
    /// Creates intent for a climactic melody.
    /// </summary>
    public static MelodicIntent Climactic => new()
    {
        Contour = MelodicContour.Ascending,
        Range = MelodicRange.High,
        RhythmicDensity = 1.0,
        MaxIntervalSize = 12,
        DissonanceLevel = 0.5,
        EnergyLevel = 0.95
    };

    /// <summary>
    /// Creates intent for a restful, concluding melody.
    /// </summary>
    public static MelodicIntent Restful => new()
    {
        Contour = MelodicContour.Descending,
        Range = MelodicRange.Middle,
        RhythmicDensity = 0.5,
        PreferConjunctMotion = true,
        MaxIntervalSize = 4,
        DissonanceLevel = 0.05,
        EndOnStable = true,
        EnergyLevel = 0.2
    };
}

/// <summary>
/// Melodic contour types.
/// </summary>
public enum MelodicContour
{
    /// <summary>Generally ascending melody.</summary>
    Ascending,

    /// <summary>Generally descending melody.</summary>
    Descending,

    /// <summary>Arch shape (up then down).</summary>
    Arch,

    /// <summary>Bowl shape (down then up).</summary>
    Bowl,

    /// <summary>Balanced with no strong direction.</summary>
    Balanced,

    /// <summary>Maintains a consistent register.</summary>
    Static
}

/// <summary>
/// Melodic range preferences.
/// </summary>
public enum MelodicRange
{
    /// <summary>Low register.</summary>
    Low,

    /// <summary>Middle register.</summary>
    Middle,

    /// <summary>High register.</summary>
    High,

    /// <summary>Full range exploration.</summary>
    Full
}
