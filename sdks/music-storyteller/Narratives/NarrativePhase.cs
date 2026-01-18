using BeyondImmersion.Bannou.MusicStoryteller.State;

namespace BeyondImmersion.Bannou.MusicStoryteller.Narratives;

/// <summary>
/// Defines the harmonic character for a narrative phase.
/// </summary>
public enum HarmonicCharacter
{
    /// <summary>Stable, tonic-centered harmony.</summary>
    Stable,

    /// <summary>Moving away from tonic, creating tension.</summary>
    Departing,

    /// <summary>Wandering harmony, distant from tonic.</summary>
    Wandering,

    /// <summary>Building toward climax or resolution.</summary>
    Building,

    /// <summary>Climactic, maximum tension.</summary>
    Climactic,

    /// <summary>Returning to tonic, resolving.</summary>
    Resolving,

    /// <summary>Peaceful, post-resolution stability.</summary>
    Peaceful
}

/// <summary>
/// Defines thematic goals for a narrative phase.
/// </summary>
public sealed class ThematicGoals
{
    /// <summary>
    /// Gets whether the main motif should be introduced in this phase.
    /// </summary>
    public bool IntroduceMainMotif { get; init; }

    /// <summary>
    /// Gets whether the main motif should return in this phase.
    /// </summary>
    public bool ReturnMainMotif { get; init; }

    /// <summary>
    /// Gets whether motif development (transformation, fragmentation) is encouraged.
    /// </summary>
    public bool DevelopMotif { get; init; }

    /// <summary>
    /// Gets whether a secondary/contrasting motif can be introduced.
    /// </summary>
    public bool AllowSecondaryMotif { get; init; }

    /// <summary>
    /// Gets the preferred motif transformation types for this phase.
    /// </summary>
    public IReadOnlyList<MotifTransformationType> PreferredTransformations { get; init; } = [];

    /// <summary>
    /// Creates default thematic goals (no specific requirements).
    /// </summary>
    public static ThematicGoals Default => new();

    /// <summary>
    /// Creates goals for an introduction phase.
    /// </summary>
    public static ThematicGoals Introduction => new()
    {
        IntroduceMainMotif = true,
        DevelopMotif = false,
        AllowSecondaryMotif = false
    };

    /// <summary>
    /// Creates goals for a development phase.
    /// </summary>
    public static ThematicGoals Development => new()
    {
        DevelopMotif = true,
        AllowSecondaryMotif = true,
        PreferredTransformations = [
            MotifTransformationType.Sequence,
            MotifTransformationType.Fragmentation,
            MotifTransformationType.Extension
        ]
    };

    /// <summary>
    /// Creates goals for a recapitulation/return phase.
    /// </summary>
    public static ThematicGoals Recapitulation => new()
    {
        ReturnMainMotif = true,
        DevelopMotif = false,
        PreferredTransformations = [MotifTransformationType.Repetition]
    };
}

/// <summary>
/// Defines the musical character (texture, rhythm, register) for a phase.
/// </summary>
public sealed class MusicalCharacter
{
    /// <summary>
    /// Gets the target textural density (0 = sparse, 1 = full).
    /// </summary>
    public double TexturalDensity { get; init; } = 0.5;

    /// <summary>
    /// Gets the target rhythmic activity (0 = static, 1 = driving).
    /// </summary>
    public double RhythmicActivity { get; init; } = 0.5;

    /// <summary>
    /// Gets the target register height (0 = low, 1 = high).
    /// </summary>
    public double RegisterHeight { get; init; } = 0.5;

    /// <summary>
    /// Gets whether this phase allows dynamic/tempo changes.
    /// </summary>
    public bool AllowDynamicChanges { get; init; } = true;

    /// <summary>
    /// Creates default musical character.
    /// </summary>
    public static MusicalCharacter Default => new();

    /// <summary>
    /// Creates character for a quiet, intimate passage.
    /// </summary>
    public static MusicalCharacter Intimate => new()
    {
        TexturalDensity = 0.2,
        RhythmicActivity = 0.3,
        RegisterHeight = 0.4
    };

    /// <summary>
    /// Creates character for an active, driving passage.
    /// </summary>
    public static MusicalCharacter Driving => new()
    {
        TexturalDensity = 0.7,
        RhythmicActivity = 0.8,
        RegisterHeight = 0.6
    };

    /// <summary>
    /// Creates character for a climactic passage.
    /// </summary>
    public static MusicalCharacter Climactic => new()
    {
        TexturalDensity = 1.0,
        RhythmicActivity = 0.9,
        RegisterHeight = 0.8
    };

    /// <summary>
    /// Creates character for a peaceful conclusion.
    /// </summary>
    public static MusicalCharacter Peaceful => new()
    {
        TexturalDensity = 0.3,
        RhythmicActivity = 0.2,
        RegisterHeight = 0.5,
        AllowDynamicChanges = false
    };
}

/// <summary>
/// A phase within a narrative arc, defining emotional and musical targets.
/// Source: Plan Phase 5 - Narrative Templates
/// </summary>
public sealed class NarrativePhase
{
    /// <summary>
    /// Gets the name of this phase.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the relative duration as a fraction of total composition (0-1).
    /// All phases in a template should sum to 1.0.
    /// </summary>
    public required double RelativeDuration { get; init; }

    /// <summary>
    /// Gets the target emotional state for this phase.
    /// </summary>
    public required EmotionalState EmotionalTarget { get; init; }

    /// <summary>
    /// Gets the harmonic character for this phase.
    /// </summary>
    public HarmonicCharacter HarmonicCharacter { get; init; } = HarmonicCharacter.Stable;

    /// <summary>
    /// Gets the thematic goals for this phase.
    /// </summary>
    public ThematicGoals ThematicGoals { get; init; } = ThematicGoals.Default;

    /// <summary>
    /// Gets the musical character for this phase.
    /// </summary>
    public MusicalCharacter MusicalCharacter { get; init; } = MusicalCharacter.Default;

    /// <summary>
    /// Gets the preferred cadence type at the end of this phase, if any.
    /// </summary>
    public CadencePreference? EndingCadence { get; init; }

    /// <summary>
    /// Gets whether this phase should avoid strong resolutions (keep tension).
    /// </summary>
    public bool AvoidResolution { get; init; }

    /// <summary>
    /// Gets whether this phase requires a strong resolution.
    /// </summary>
    public bool RequireResolution { get; init; }
}

/// <summary>
/// Preferred cadence type for phase endings.
/// </summary>
public enum CadencePreference
{
    /// <summary>No preference - let planner decide.</summary>
    None,

    /// <summary>Strong authentic cadence (V-I).</summary>
    Authentic,

    /// <summary>Gentle plagal cadence (IV-I).</summary>
    Plagal,

    /// <summary>Half cadence (end on V) - maintains tension.</summary>
    Half,

    /// <summary>Deceptive cadence (V-vi) - subverts expectation.</summary>
    Deceptive
}
