using BeyondImmersion.Bannou.MusicTheory.Collections;
using BeyondImmersion.Bannou.MusicTheory.Pitch;

namespace BeyondImmersion.Bannou.MusicStoryteller.Theory;

/// <summary>
/// Calculates chord distance using Lerdahl's Tonal Pitch Space formula.
/// δ(x→y) = i + j + k where:
///   i = circle of fifths distance between chord roots
///   j = number of distinct pitch classes in y's basic space not in x's
///   k = number of non-common tones
/// Source: Lerdahl, F. (2001). Tonal Pitch Space, Chapter 2.
/// </summary>
public static class ChordDistance
{
    /// <summary>
    /// Calculates the full chord distance δ(x→y) = i + j + k.
    /// </summary>
    /// <param name="from">The source basic space.</param>
    /// <param name="to">The target basic space.</param>
    /// <returns>The total chord distance.</returns>
    public static int Calculate(BasicSpace from, BasicSpace to)
    {
        var i = CircleOfFifthsDistance(from.Root, to.Root);
        var j = CalculateDistinctPitchClasses(from, to);
        var k = CalculateNonCommonTones(from, to);

        return i + j + k;
    }

    /// <summary>
    /// Calculates chord distance between two chords in a key context.
    /// </summary>
    /// <param name="fromChord">The source chord.</param>
    /// <param name="toChord">The target chord.</param>
    /// <param name="keyContext">The key context for diatonic reference.</param>
    /// <returns>The chord distance.</returns>
    public static int Calculate(Chord fromChord, Chord toChord, Scale keyContext)
    {
        var fromSpace = BasicSpace.FromChord(fromChord, keyContext);
        var toSpace = BasicSpace.FromChord(toChord, keyContext);
        return Calculate(fromSpace, toSpace);
    }

    /// <summary>
    /// Calculates the circle of fifths distance (i component).
    /// </summary>
    /// <param name="from">Source pitch class.</param>
    /// <param name="to">Target pitch class.</param>
    /// <returns>Distance in fifths (0-6).</returns>
    public static int CircleOfFifthsDistance(PitchClass from, PitchClass to)
    {
        var fromPc = (int)from;
        var toPc = (int)to;

        // Convert semitones to position on circle of fifths
        var fromFifths = SemitonesToFifths(fromPc);
        var toFifths = SemitonesToFifths(toPc);

        // Calculate distance on circle (0-6, since 7 = -5, 8 = -4, etc.)
        var distance = Math.Abs(toFifths - fromFifths);
        return Math.Min(distance, 12 - distance);
    }

    /// <summary>
    /// Calculates the number of distinct pitch classes in target not in source (j component).
    /// This measures how different the diatonic collection is.
    /// </summary>
    /// <param name="from">Source basic space.</param>
    /// <param name="to">Target basic space.</param>
    /// <returns>Count of distinct pitch classes.</returns>
    public static int CalculateDistinctPitchClasses(BasicSpace from, BasicSpace to)
    {
        // Count pitch classes in target's diatonic that aren't in source's diatonic
        return to.DiatonicLevel.Count(pc => !from.DiatonicLevel.Contains(pc));
    }

    /// <summary>
    /// Calculates the number of non-common chord tones (k component).
    /// Compares triadic pitch classes between chords.
    /// </summary>
    /// <param name="from">Source basic space.</param>
    /// <param name="to">Target basic space.</param>
    /// <returns>Count of non-common tones.</returns>
    public static int CalculateNonCommonTones(BasicSpace from, BasicSpace to)
    {
        // Count pitch classes in target's triad that aren't in source's triad
        return to.TriadicLevel.Count(pc => !from.TriadicLevel.Contains(pc));
    }

    /// <summary>
    /// Calculates a simplified chord distance for common progressions.
    /// Uses pre-computed values for standard functional progressions.
    /// </summary>
    /// <param name="fromDegree">Source scale degree (1-7).</param>
    /// <param name="toDegree">Target scale degree (1-7).</param>
    /// <param name="isMajorKey">Whether in a major key.</param>
    /// <returns>Chord distance.</returns>
    public static int CalculateFromDegrees(int fromDegree, int toDegree, bool isMajorKey = true)
    {
        if (fromDegree < 1 || fromDegree > 7 || toDegree < 1 || toDegree > 7)
        {
            throw new ArgumentOutOfRangeException(nameof(fromDegree), "Degrees must be 1-7");
        }

        // Pre-computed distance matrix for major keys
        // Based on Lerdahl TPS values
        if (isMajorKey)
        {
            return MajorKeyDistances[fromDegree - 1, toDegree - 1];
        }

        return MinorKeyDistances[fromDegree - 1, toDegree - 1];
    }

    /// <summary>
    /// Gets the relative tension of a chord based on distance from tonic.
    /// </summary>
    /// <param name="chord">The chord.</param>
    /// <param name="key">The key context.</param>
    /// <returns>Relative tension value (0 = tonic, higher = more distant).</returns>
    public static double GetRelativeTension(Chord chord, Scale key)
    {
        var tonicChord = Chord.FromScaleDegree(key, 1, false);
        var distance = Calculate(tonicChord, chord, key);

        // Normalize to 0-1 range (max practical distance ~12)
        return Math.Min(distance / 12.0, 1.0);
    }

    // Pre-computed distance matrix for major keys
    // Row = from degree (0-indexed), Column = to degree (0-indexed)
    private static readonly int[,] MajorKeyDistances =
    {
        //  I  ii iii IV  V  vi vii°
        { 0, 5, 7, 5, 5, 8, 8 },  // from I
        { 5, 0, 5, 7, 5, 5, 7 },  // from ii
        { 7, 5, 0, 5, 7, 5, 5 },  // from iii
        { 5, 7, 5, 0, 5, 7, 5 },  // from IV
        { 5, 5, 7, 5, 0, 5, 7 },  // from V
        { 8, 5, 5, 7, 5, 0, 5 },  // from vi
        { 8, 7, 5, 5, 7, 5, 0 }   // from vii°
    };

    // Pre-computed distance matrix for minor keys
    private static readonly int[,] MinorKeyDistances =
    {
        //  i ii° III iv  V  VI VII
        { 0, 5, 7, 5, 5, 8, 8 },  // from i
        { 5, 0, 5, 7, 5, 5, 7 },  // from ii°
        { 7, 5, 0, 5, 7, 5, 5 },  // from III
        { 5, 7, 5, 0, 5, 7, 5 },  // from iv
        { 5, 5, 7, 5, 0, 5, 7 },  // from V
        { 8, 5, 5, 7, 5, 0, 5 },  // from VI
        { 8, 7, 5, 5, 7, 5, 0 }   // from VII
    };

    /// <summary>
    /// Converts semitone position to circle of fifths position.
    /// </summary>
    private static int SemitonesToFifths(int semitones)
    {
        // Circle of fifths order: C=0, G=1, D=2, A=3, E=4, B=5, F#=6, C#=7, Ab=8, Eb=9, Bb=10, F=11
        // Semitone to fifths mapping
        return semitones switch
        {
            0 => 0,   // C
            1 => 7,   // C#/Db
            2 => 2,   // D
            3 => 9,   // D#/Eb
            4 => 4,   // E
            5 => 11,  // F
            6 => 6,   // F#/Gb
            7 => 1,   // G
            8 => 8,   // G#/Ab
            9 => 3,   // A
            10 => 10, // A#/Bb
            11 => 5,  // B
            _ => 0
        };
    }

    /// <summary>
    /// Common chord distance reference values for quick lookup.
    /// </summary>
    public static class Reference
    {
        /// <summary>I to V (or V to I) - strong functional relationship</summary>
        public const int TonicDominant = 5;

        /// <summary>I to IV (or IV to I) - subdominant relationship</summary>
        public const int TonicSubdominant = 5;

        /// <summary>I to vi (or vi to I) - relative minor/major</summary>
        public const int TonicRelative = 8;

        /// <summary>ii to V - pre-dominant to dominant</summary>
        public const int TwoFive = 5;

        /// <summary>V to vi - deceptive cadence</summary>
        public const int DeceptiveCadence = 5;
    }
}
