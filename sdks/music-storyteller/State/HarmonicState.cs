using BeyondImmersion.Bannou.MusicTheory.Collections;
using BeyondImmersion.Bannou.MusicTheory.Harmony;
using BeyondImmersion.Bannou.MusicTheory.Pitch;

namespace BeyondImmersion.Bannou.MusicStoryteller.State;

/// <summary>
/// Tracks the current harmonic context of the composition.
/// </summary>
public sealed class HarmonicState
{
    /// <summary>
    /// The current key/scale.
    /// </summary>
    public Scale CurrentKey { get; set; }

    /// <summary>
    /// The original/home key (for modulation tracking).
    /// </summary>
    public Scale HomeKey { get; set; }

    /// <summary>
    /// The current chord.
    /// </summary>
    public Chord? CurrentChord { get; set; }

    /// <summary>
    /// The previous chord (for voice leading).
    /// </summary>
    public Chord? PreviousChord { get; set; }

    /// <summary>
    /// Current harmonic function (tonic, subdominant, dominant).
    /// </summary>
    public HarmonicFunctionType CurrentFunction { get; set; } = HarmonicFunctionType.Tonic;

    /// <summary>
    /// Distance from home key in the circle of fifths.
    /// 0 = home key, positive = sharp direction, negative = flat direction.
    /// </summary>
    public int KeyDistance { get; set; }

    /// <summary>
    /// Whether we're currently in a modulation.
    /// </summary>
    public bool IsModulating { get; set; }

    /// <summary>
    /// Number of bars since last tonic chord.
    /// </summary>
    public int BarsSinceTonic { get; set; }

    /// <summary>
    /// Whether a cadence is expected (tension has been built).
    /// </summary>
    public bool ExpectingCadence { get; set; }

    /// <summary>
    /// Whether currently on a tonic chord.
    /// </summary>
    public bool OnTonic => CurrentFunction == HarmonicFunctionType.Tonic;

    /// <summary>
    /// Whether currently on a dominant chord.
    /// </summary>
    public bool OnDominant => CurrentFunction == HarmonicFunctionType.Dominant;

    /// <summary>
    /// The dominant preparation count (how many pre-dominant chords).
    /// </summary>
    public int DominantPreparation { get; set; }

    /// <summary>
    /// Recent chord progression (last N chords for pattern detection).
    /// </summary>
    public List<Chord> RecentProgression { get; } = new(8);

    /// <summary>
    /// Creates a new harmonic state with default C major key.
    /// </summary>
    public HarmonicState()
    {
        CurrentKey = new Scale(PitchClass.C, ModeType.Major);
        HomeKey = CurrentKey;
    }

    /// <summary>
    /// Creates a new harmonic state with the specified key.
    /// </summary>
    /// <param name="key">The initial key.</param>
    public HarmonicState(Scale key)
    {
        CurrentKey = key;
        HomeKey = key;
    }

    /// <summary>
    /// Updates the current chord and tracks progression.
    /// </summary>
    /// <param name="chord">The new current chord.</param>
    public void SetChord(Chord chord)
    {
        PreviousChord = CurrentChord;
        CurrentChord = chord;

        // Track recent progression
        RecentProgression.Add(chord);
        if (RecentProgression.Count > 8)
        {
            RecentProgression.RemoveAt(0);
        }

        // Update function
        var function = HarmonicFunctionAnalyzer.Analyze(chord, CurrentKey);
        if (function != null)
        {
            CurrentFunction = function.Type;

            if (CurrentFunction == HarmonicFunctionType.Tonic)
            {
                BarsSinceTonic = 0;
                ExpectingCadence = false;
                DominantPreparation = 0;
            }
            else if (CurrentFunction == HarmonicFunctionType.Dominant)
            {
                ExpectingCadence = true;
            }
            else if (CurrentFunction == HarmonicFunctionType.Subdominant)
            {
                DominantPreparation++;
            }
        }
    }

    /// <summary>
    /// Advances harmonic state by one bar (updates counters).
    /// </summary>
    public void AdvanceBar()
    {
        if (CurrentFunction != HarmonicFunctionType.Tonic)
        {
            BarsSinceTonic++;
        }
    }

    /// <summary>
    /// Modulates to a new key.
    /// </summary>
    /// <param name="newKey">The new key.</param>
    public void Modulate(Scale newKey)
    {
        IsModulating = true;
        CurrentKey = newKey;
        KeyDistance = CalculateKeyDistance(HomeKey, newKey);
    }

    /// <summary>
    /// Returns to the home key.
    /// </summary>
    public void ReturnHome()
    {
        CurrentKey = HomeKey;
        KeyDistance = 0;
        IsModulating = false;
    }

    /// <summary>
    /// Calculates the current harmonic tension based on state.
    /// </summary>
    /// <returns>Tension value from 0 (resolved) to 1 (maximum tension).</returns>
    public double CalculateTension()
    {
        var tension = 0.0;

        // Distance from tonic increases tension
        tension += CurrentFunction switch
        {
            HarmonicFunctionType.Tonic => 0.0,
            HarmonicFunctionType.Subdominant => 0.3,
            HarmonicFunctionType.Dominant => 0.6,
            _ => 0.3
        };

        // Key distance adds tension
        tension += Math.Abs(KeyDistance) * 0.05;

        // Bars since tonic adds tension
        tension += Math.Min(BarsSinceTonic * 0.05, 0.3);

        // Dominant preparation adds tension
        tension += DominantPreparation * 0.1;

        return Math.Clamp(tension, 0.0, 1.0);
    }

    /// <summary>
    /// Creates a deep copy of this harmonic state.
    /// </summary>
    public HarmonicState Clone()
    {
        var clone = new HarmonicState(CurrentKey)
        {
            HomeKey = HomeKey,
            CurrentChord = CurrentChord,
            PreviousChord = PreviousChord,
            CurrentFunction = CurrentFunction,
            KeyDistance = KeyDistance,
            IsModulating = IsModulating,
            BarsSinceTonic = BarsSinceTonic,
            ExpectingCadence = ExpectingCadence,
            DominantPreparation = DominantPreparation
        };
        clone.RecentProgression.AddRange(RecentProgression);
        return clone;
    }

    private static int CalculateKeyDistance(Scale from, Scale to)
    {
        // Calculate circle of fifths distance
        var fromRoot = (int)from.Root;
        var toRoot = (int)to.Root;

        // Distance in semitones
        var semitones = (toRoot - fromRoot + 12) % 12;

        // Convert to fifths distance (7 semitones = 1 fifth)
        // This is simplified - a proper implementation would track direction
        return semitones switch
        {
            0 => 0,
            7 => 1,  // Up a fifth
            5 => -1, // Down a fifth (up a fourth)
            2 => 2,  // Up two fifths
            10 => -2, // Down two fifths
            _ => semitones > 6 ? semitones - 12 : semitones
        };
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return $"Harmonic[Key={CurrentKey}, Chord={CurrentChord}, Function={CurrentFunction}]";
    }
}
