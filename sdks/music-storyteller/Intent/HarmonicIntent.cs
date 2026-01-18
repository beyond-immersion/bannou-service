using BeyondImmersion.Bannou.MusicStoryteller.Narratives;
using BeyondImmersion.Bannou.MusicTheory.Collections;

namespace BeyondImmersion.Bannou.MusicStoryteller.Intent;

/// <summary>
/// Harmonic intent expressing desired chord progressions and harmonic behavior.
/// Passed to the music-theory SDK for generation.
/// Source: Plan Phase 7 - Intent Generation
/// </summary>
public sealed record HarmonicIntent
{
    /// <summary>
    /// Gets whether to avoid the tonic chord (maintain tension).
    /// </summary>
    public bool AvoidTonic { get; init; }

    /// <summary>
    /// Gets whether to emphasize the dominant function.
    /// </summary>
    public bool EmphasizeDominant { get; init; }

    /// <summary>
    /// Gets the preferred cadence at phrase endings.
    /// </summary>
    public CadencePreference? EndingCadence { get; init; }

    /// <summary>
    /// Gets preferred harmonic functions for this section.
    /// </summary>
    public IReadOnlyList<HarmonicFunction> PreferredFunctions { get; init; } = [];

    /// <summary>
    /// Gets harmonic functions to avoid.
    /// </summary>
    public IReadOnlyList<HarmonicFunction> AvoidFunctions { get; init; } = [];

    /// <summary>
    /// Gets the target harmonic rhythm density (chords per bar).
    /// 0.5 = one chord every 2 bars, 1.0 = one per bar, 2.0 = two per bar.
    /// </summary>
    public double HarmonicRhythmDensity { get; init; } = 1.0;

    /// <summary>
    /// Gets whether modal interchange (borrowed chords) is allowed.
    /// </summary>
    public bool AllowModalInterchange { get; init; }

    /// <summary>
    /// Gets whether secondary dominants are encouraged.
    /// </summary>
    public bool EncourageSecondaryDominants { get; init; }

    /// <summary>
    /// Gets the target key for modulation, if any.
    /// </summary>
    public Scale? TargetKey { get; init; }

    /// <summary>
    /// Gets whether chromatic voice leading is preferred.
    /// </summary>
    public bool PreferChromaticVoiceLeading { get; init; }

    /// <summary>
    /// Gets the target tension level (0-1) for chord selection.
    /// </summary>
    public double TargetTensionLevel { get; init; } = 0.5;

    /// <summary>
    /// Creates a default harmonic intent.
    /// </summary>
    public static HarmonicIntent Default => new();

    /// <summary>
    /// Creates intent for building tension.
    /// </summary>
    public static HarmonicIntent BuildTension => new()
    {
        AvoidTonic = true,
        EmphasizeDominant = true,
        EncourageSecondaryDominants = true,
        HarmonicRhythmDensity = 1.5,
        TargetTensionLevel = 0.8,
        EndingCadence = CadencePreference.Half
    };

    /// <summary>
    /// Creates intent for resolution.
    /// </summary>
    public static HarmonicIntent Resolve => new()
    {
        AvoidTonic = false,
        EmphasizeDominant = false,
        HarmonicRhythmDensity = 0.5,
        TargetTensionLevel = 0.2,
        EndingCadence = CadencePreference.Authentic,
        PreferredFunctions = [HarmonicFunction.Tonic, HarmonicFunction.Subdominant]
    };

    /// <summary>
    /// Creates intent for wandering/development.
    /// </summary>
    public static HarmonicIntent Wandering => new()
    {
        AvoidTonic = true,
        AllowModalInterchange = true,
        EncourageSecondaryDominants = true,
        HarmonicRhythmDensity = 1.0,
        TargetTensionLevel = 0.6,
        PreferChromaticVoiceLeading = true
    };

    /// <summary>
    /// Creates intent for stable passages.
    /// </summary>
    public static HarmonicIntent Stable => new()
    {
        AvoidTonic = false,
        HarmonicRhythmDensity = 0.5,
        TargetTensionLevel = 0.3,
        PreferredFunctions = [HarmonicFunction.Tonic, HarmonicFunction.Subdominant],
        AvoidFunctions = [HarmonicFunction.Secondary]
    };
}

/// <summary>
/// Harmonic function categories.
/// </summary>
public enum HarmonicFunction
{
    /// <summary>Tonic function (I, vi).</summary>
    Tonic,

    /// <summary>Subdominant function (IV, ii).</summary>
    Subdominant,

    /// <summary>Dominant function (V, vii°).</summary>
    Dominant,

    /// <summary>Secondary dominant (V/x).</summary>
    Secondary,

    /// <summary>Borrowed from parallel mode.</summary>
    Borrowed,

    /// <summary>Chromatic passing chord.</summary>
    Chromatic
}
