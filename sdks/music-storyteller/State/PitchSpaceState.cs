using BeyondImmersion.Bannou.MusicTheory.Collections;
using BeyondImmersion.Bannou.MusicTheory.Pitch;

namespace BeyondImmersion.Bannou.MusicStoryteller.State;

/// <summary>
/// Tracks position in Lerdahl's Tonal Pitch Space.
/// Models the hierarchical organization of pitches in tonal music.
/// </summary>
public sealed class PitchSpaceState
{
    /// <summary>
    /// Current tonic pitch class.
    /// </summary>
    public PitchClass Tonic { get; set; } = PitchClass.C;

    /// <summary>
    /// Current mode type.
    /// </summary>
    public ModeType Mode { get; set; } = ModeType.Major;

    /// <summary>
    /// Current chord root pitch class.
    /// </summary>
    public PitchClass ChordRoot { get; set; } = PitchClass.C;

    /// <summary>
    /// Distance from tonic to current chord root (in the basic space).
    /// </summary>
    public int ChordDistanceFromTonic { get; set; }

    /// <summary>
    /// Accumulated pitch space distance traveled.
    /// </summary>
    public int TotalDistanceTraveled { get; set; }

    /// <summary>
    /// Maximum distance from tonic reached.
    /// </summary>
    public int MaxDistanceFromTonic { get; set; }

    /// <summary>
    /// Current regional key (for tonicizations).
    /// </summary>
    public PitchClass? RegionalTonic { get; set; }

    /// <summary>
    /// Whether currently in a tonicization.
    /// </summary>
    public bool InTonicization => RegionalTonic.HasValue && RegionalTonic.Value != Tonic;

    /// <summary>
    /// History of chord roots for path analysis.
    /// </summary>
    public List<PitchClass> ChordPath { get; } = new(32);

    /// <summary>
    /// Gets the current basic space (5-level pitch hierarchy).
    /// </summary>
    public BasicSpaceLevels GetBasicSpace()
    {
        var effectiveTonic = RegionalTonic ?? Tonic;
        return new BasicSpaceLevels(effectiveTonic, Mode);
    }

    /// <summary>
    /// Updates the state when moving to a new chord.
    /// </summary>
    /// <param name="newChordRoot">The new chord root.</param>
    /// <param name="distance">The chord distance moved.</param>
    public void MoveToChord(PitchClass newChordRoot, int distance)
    {
        var previousRoot = ChordRoot;
        ChordRoot = newChordRoot;

        // Track distance
        TotalDistanceTraveled += distance;

        // Calculate distance from global tonic
        ChordDistanceFromTonic = CalculateRootDistance(Tonic, newChordRoot);
        MaxDistanceFromTonic = Math.Max(MaxDistanceFromTonic, ChordDistanceFromTonic);

        // Track path
        ChordPath.Add(newChordRoot);
        if (ChordPath.Count > 32)
        {
            ChordPath.RemoveAt(0);
        }
    }

    /// <summary>
    /// Enters a tonicization (temporary focus on a secondary key area).
    /// </summary>
    /// <param name="secondaryTonic">The temporary tonic.</param>
    public void EnterTonicization(PitchClass secondaryTonic)
    {
        RegionalTonic = secondaryTonic;
    }

    /// <summary>
    /// Exits a tonicization and returns to the main key.
    /// </summary>
    public void ExitTonicization()
    {
        RegionalTonic = null;
    }

    /// <summary>
    /// Modulates to a new key (permanent key change).
    /// </summary>
    /// <param name="newTonic">The new tonic.</param>
    /// <param name="newMode">The new mode.</param>
    public void Modulate(PitchClass newTonic, ModeType newMode)
    {
        Tonic = newTonic;
        Mode = newMode;
        RegionalTonic = null;
        ChordDistanceFromTonic = 0;
    }

    /// <summary>
    /// Gets the current tension based on pitch space position.
    /// </summary>
    /// <returns>Tension value (0-1) based on distance from tonic.</returns>
    public double GetPitchSpaceTension()
    {
        // Distance from tonic creates tension
        var distanceTension = Math.Min(ChordDistanceFromTonic / 10.0, 0.5);

        // Tonicization adds some tension
        var tonicizationTension = InTonicization ? 0.15 : 0.0;

        return Math.Clamp(distanceTension + tonicizationTension, 0.0, 1.0);
    }

    /// <summary>
    /// Analyzes the path for return patterns.
    /// </summary>
    /// <returns>True if the recent path suggests a return to tonic.</returns>
    public bool IsReturningHome()
    {
        if (ChordPath.Count < 3) return false;

        // Check if recent movement is toward tonic
        var recent = ChordPath.TakeLast(3).ToList();
        var distances = recent.Select(p => CalculateRootDistance(Tonic, p)).ToList();

        // Returning if distances are decreasing
        return distances[0] >= distances[1] && distances[1] >= distances[2] && distances[2] <= 2;
    }

    /// <summary>
    /// Creates a deep copy of this pitch space state.
    /// </summary>
    public PitchSpaceState Clone()
    {
        var clone = new PitchSpaceState
        {
            Tonic = Tonic,
            Mode = Mode,
            ChordRoot = ChordRoot,
            ChordDistanceFromTonic = ChordDistanceFromTonic,
            TotalDistanceTraveled = TotalDistanceTraveled,
            MaxDistanceFromTonic = MaxDistanceFromTonic,
            RegionalTonic = RegionalTonic
        };
        clone.ChordPath.AddRange(ChordPath);
        return clone;
    }

    private static int CalculateRootDistance(PitchClass from, PitchClass to)
    {
        // Calculate circle of fifths distance
        var semitones = ((int)to - (int)from + 12) % 12;

        // Convert to minimal fifths distance
        return semitones switch
        {
            0 => 0,
            7 => 1,  // Perfect fifth up
            5 => 1,  // Perfect fourth up (fifth down)
            2 => 2,  // Major second up (two fifths)
            10 => 2, // Minor seventh up (two fifths down)
            9 => 3,  // Major sixth
            3 => 3,  // Minor third
            4 => 4,  // Major third
            8 => 4,  // Minor sixth
            11 => 5, // Major seventh
            1 => 5,  // Minor second
            6 => 6,  // Tritone
            _ => 6
        };
    }

    /// <inheritdoc />
    public override string ToString()
    {
        var region = InTonicization ? $" (region: {RegionalTonic})" : "";
        return $"PitchSpace[{Tonic} {Mode}, Chord={ChordRoot}, Dist={ChordDistanceFromTonic}{region}]";
    }
}

/// <summary>
/// Represents the 5-level basic space hierarchy for a given tonic and mode.
/// Based on Lerdahl's Tonal Pitch Space theory.
/// </summary>
public sealed class BasicSpaceLevels
{
    /// <summary>
    /// Level a: Root only (octave).
    /// </summary>
    public HashSet<int> RootLevel { get; }

    /// <summary>
    /// Level b: Root and fifth.
    /// </summary>
    public HashSet<int> FifthLevel { get; }

    /// <summary>
    /// Level c: Triadic pitches (root, third, fifth).
    /// </summary>
    public HashSet<int> TriadicLevel { get; }

    /// <summary>
    /// Level d: Diatonic scale pitches.
    /// </summary>
    public HashSet<int> DiatonicLevel { get; }

    /// <summary>
    /// Level e: All chromatic pitches.
    /// </summary>
    public HashSet<int> ChromaticLevel { get; }

    /// <summary>
    /// Creates a basic space hierarchy for the given tonic and mode.
    /// </summary>
    public BasicSpaceLevels(PitchClass tonic, ModeType mode)
    {
        var root = (int)tonic;

        // Level a: Root
        RootLevel = [root];

        // Level b: Root + Fifth
        FifthLevel = [root, (root + 7) % 12];

        // Level c: Triadic
        var thirdOffset = mode is ModeType.Minor or ModeType.Dorian or ModeType.Phrygian ? 3 : 4;
        TriadicLevel = [root, (root + thirdOffset) % 12, (root + 7) % 12];

        // Level d: Diatonic
        var intervals = GetModeIntervals(mode);
        DiatonicLevel = new HashSet<int>(intervals.Select(i => (root + i) % 12));

        // Level e: Chromatic
        ChromaticLevel = Enumerable.Range(0, 12).ToHashSet();
    }

    /// <summary>
    /// Gets the stability level (1-5) for a pitch class.
    /// 1 = most stable (root), 5 = least stable (chromatic).
    /// </summary>
    /// <param name="pitchClass">The pitch class to check.</param>
    /// <returns>Stability level from 1-5.</returns>
    public int GetStabilityLevel(int pitchClass)
    {
        pitchClass = ((pitchClass % 12) + 12) % 12;

        if (RootLevel.Contains(pitchClass)) return 1;
        if (FifthLevel.Contains(pitchClass)) return 2;
        if (TriadicLevel.Contains(pitchClass)) return 3;
        if (DiatonicLevel.Contains(pitchClass)) return 4;
        return 5;
    }

    private static int[] GetModeIntervals(ModeType mode)
    {
        return mode switch
        {
            ModeType.Major => [0, 2, 4, 5, 7, 9, 11],
            ModeType.Minor => [0, 2, 3, 5, 7, 8, 10],
            ModeType.Dorian => [0, 2, 3, 5, 7, 9, 10],
            ModeType.Phrygian => [0, 1, 3, 5, 7, 8, 10],
            ModeType.Lydian => [0, 2, 4, 6, 7, 9, 11],
            ModeType.Mixolydian => [0, 2, 4, 5, 7, 9, 10],
            ModeType.Locrian => [0, 1, 3, 5, 6, 8, 10],
            _ => [0, 2, 4, 5, 7, 9, 11]
        };
    }
}
