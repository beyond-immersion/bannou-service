using BeyondImmersion.Bannou.MusicTheory.Collections;
using BeyondImmersion.Bannou.MusicTheory.Pitch;

namespace BeyondImmersion.Bannou.MusicStoryteller.Theory;

/// <summary>
/// Represents Lerdahl's 5-level basic space hierarchy.
/// The basic space defines the hierarchical organization of pitches within a tonal context.
/// Source: Lerdahl, F. (2001). Tonal Pitch Space. Oxford University Press.
/// </summary>
public sealed class BasicSpace
{
    /// <summary>
    /// The root pitch class of this basic space.
    /// </summary>
    public PitchClass Root { get; }

    /// <summary>
    /// The mode of this basic space.
    /// </summary>
    public ModeType Mode { get; }

    /// <summary>
    /// Level a: Root only (the tonic or chord root).
    /// Stability weight: 4
    /// </summary>
    public IReadOnlySet<int> RootLevel { get; }

    /// <summary>
    /// Level b: Fifth (root + perfect fifth).
    /// Stability weight: 3
    /// </summary>
    public IReadOnlySet<int> FifthLevel { get; }

    /// <summary>
    /// Level c: Triadic (root + third + fifth).
    /// Stability weight: 2
    /// </summary>
    public IReadOnlySet<int> TriadicLevel { get; }

    /// <summary>
    /// Level d: Diatonic scale degrees.
    /// Stability weight: 1
    /// </summary>
    public IReadOnlySet<int> DiatonicLevel { get; }

    /// <summary>
    /// Level e: Full chromatic (all 12 pitch classes).
    /// Stability weight: 0
    /// </summary>
    public IReadOnlySet<int> ChromaticLevel { get; }

    /// <summary>
    /// Creates a basic space for the given root and mode.
    /// </summary>
    /// <param name="root">The root pitch class.</param>
    /// <param name="mode">The mode type.</param>
    public BasicSpace(PitchClass root, ModeType mode)
    {
        Root = root;
        Mode = mode;

        var rootPc = (int)root;

        // Level a: Root only
        RootLevel = new HashSet<int> { rootPc };

        // Level b: Root + Fifth
        FifthLevel = new HashSet<int> { rootPc, (rootPc + 7) % 12 };

        // Level c: Triadic (depends on mode for third)
        var thirdOffset = GetThirdOffset(mode);
        TriadicLevel = new HashSet<int>
        {
            rootPc,
            (rootPc + thirdOffset) % 12,
            (rootPc + 7) % 12
        };

        // Level d: Diatonic scale
        var intervals = GetModeIntervals(mode);
        DiatonicLevel = intervals.Select(i => (rootPc + i) % 12).ToHashSet();

        // Level e: Full chromatic
        ChromaticLevel = Enumerable.Range(0, 12).ToHashSet();
    }

    /// <summary>
    /// Creates a basic space from a scale.
    /// </summary>
    /// <param name="scale">The scale.</param>
    public BasicSpace(Scale scale)
        : this(scale.Root, scale.Mode)
    {
    }

    /// <summary>
    /// Creates a basic space from a chord (chord space rather than key space).
    /// </summary>
    /// <param name="chord">The chord.</param>
    /// <param name="keyContext">The key context for diatonic level.</param>
    public static BasicSpace FromChord(Chord chord, Scale keyContext)
    {
        var rootPc = (int)chord.Root;
        var keyRoot = (int)keyContext.Root;

        // Determine third from chord quality
        var thirdOffset = chord.Quality switch
        {
            ChordQuality.Minor or ChordQuality.Minor7 or ChordQuality.Diminished or
            ChordQuality.Diminished7 or ChordQuality.HalfDiminished7 => 3,
            _ => 4
        };

        // Determine fifth from chord quality
        var fifthOffset = chord.Quality switch
        {
            ChordQuality.Diminished or ChordQuality.Diminished7 or ChordQuality.HalfDiminished7 => 6,
            ChordQuality.Augmented => 8,
            _ => 7
        };

        return new ChordBasicSpace(chord.Root, thirdOffset, fifthOffset, keyContext);
    }

    /// <summary>
    /// Gets the stability level (1-4) for a pitch class.
    /// Higher values indicate more stability.
    /// </summary>
    /// <param name="pitchClass">The pitch class (0-11).</param>
    /// <returns>Stability level from 1 (chromatic) to 4 (root).</returns>
    public int GetStabilityLevel(int pitchClass)
    {
        pitchClass = ((pitchClass % 12) + 12) % 12;

        if (RootLevel.Contains(pitchClass)) return 4;
        if (FifthLevel.Contains(pitchClass)) return 3;
        if (TriadicLevel.Contains(pitchClass)) return 2;
        if (DiatonicLevel.Contains(pitchClass)) return 1;
        return 0; // Chromatic only
    }

    /// <summary>
    /// Gets the stability level for a pitch class enum.
    /// </summary>
    public int GetStabilityLevel(PitchClass pitchClass)
    {
        return GetStabilityLevel((int)pitchClass);
    }

    /// <summary>
    /// Calculates the distinctiveness (number of pitch classes at each level).
    /// Used in the chord distance calculation.
    /// </summary>
    /// <returns>Array of counts for each level [a, b, c, d, e].</returns>
    public int[] GetLevelCounts()
    {
        return
        [
            RootLevel.Count,
            FifthLevel.Count,
            TriadicLevel.Count,
            DiatonicLevel.Count,
            ChromaticLevel.Count
        ];
    }

    /// <summary>
    /// Gets the depth of a pitch in the basic space (Lerdahl's d(p) function).
    /// Depth is the sum of levels that contain the pitch.
    /// </summary>
    /// <param name="pitchClass">The pitch class.</param>
    /// <returns>Depth from 0 to 5.</returns>
    public int GetDepth(int pitchClass)
    {
        pitchClass = ((pitchClass % 12) + 12) % 12;

        var depth = 0;
        if (ChromaticLevel.Contains(pitchClass)) depth++;
        if (DiatonicLevel.Contains(pitchClass)) depth++;
        if (TriadicLevel.Contains(pitchClass)) depth++;
        if (FifthLevel.Contains(pitchClass)) depth++;
        if (RootLevel.Contains(pitchClass)) depth++;
        return depth;
    }

    private static int GetThirdOffset(ModeType mode)
    {
        return mode switch
        {
            ModeType.Minor or ModeType.Dorian or ModeType.Phrygian or ModeType.Locrian => 3,
            _ => 4 // Major, Lydian, Mixolydian
        };
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

    public override string ToString()
    {
        return $"BasicSpace[{Root} {Mode}]";
    }
}

/// <summary>
/// A basic space derived from a chord rather than a key.
/// Used for chord-level pitch space calculations.
/// </summary>
public sealed class ChordBasicSpace : BasicSpace
{
    /// <summary>
    /// Creates a chord-based basic space.
    /// </summary>
    public ChordBasicSpace(PitchClass chordRoot, int thirdOffset, int fifthOffset, Scale keyContext)
        : base(chordRoot, keyContext.Mode)
    {
        // The base class handles most of the work
        // This specialized class could be extended for chord-specific behavior
    }
}
