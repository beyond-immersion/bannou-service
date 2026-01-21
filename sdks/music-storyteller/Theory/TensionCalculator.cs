using BeyondImmersion.Bannou.MusicTheory.Collections;

namespace BeyondImmersion.Bannou.MusicStoryteller.Theory;

/// <summary>
/// Calculates tonal tension using the TIS (Tonal Interval Space) formula.
/// T = 0.402×Diss + 0.246×Hier + 0.202×TonalDist + 0.193×VL
/// Source: Navarro, M. et al. (2020). "A Computational Model of Tonal Tension"
/// </summary>
public static class TensionCalculator
{
    /// <summary>
    /// Weights from the TIS regression model.
    /// </summary>
    public static class Weights
    {
        /// <summary>Weight for dissonance component.</summary>
        public const double Dissonance = 0.402;

        /// <summary>Weight for hierarchical tension component.</summary>
        public const double Hierarchical = 0.246;

        /// <summary>Weight for tonal distance component.</summary>
        public const double TonalDistance = 0.202;

        /// <summary>Weight for voice leading component.</summary>
        public const double VoiceLeading = 0.193;
    }

    /// <summary>
    /// Calculates total tension using the TIS formula.
    /// </summary>
    /// <param name="dissonance">Dissonance component (0-1).</param>
    /// <param name="hierarchicalTension">Hierarchical position tension (0-1).</param>
    /// <param name="tonalDistance">Distance from tonic in pitch space (0-1).</param>
    /// <param name="voiceLeadingRoughness">Voice leading roughness (0-1).</param>
    /// <returns>Total tension value (approximately 0-1).</returns>
    public static double Calculate(
        double dissonance,
        double hierarchicalTension,
        double tonalDistance,
        double voiceLeadingRoughness)
    {
        return Weights.Dissonance * dissonance +
            Weights.Hierarchical * hierarchicalTension +
            Weights.TonalDistance * tonalDistance +
            Weights.VoiceLeading * voiceLeadingRoughness;
    }

    /// <summary>
    /// Calculates tension for a chord in a tonal context.
    /// </summary>
    /// <param name="chord">The chord to analyze.</param>
    /// <param name="key">The key context.</param>
    /// <param name="previousChord">The previous chord (for voice leading).</param>
    /// <returns>Tension value (0-1).</returns>
    public static double CalculateForChord(Chord chord, Scale key, Chord? previousChord = null)
    {
        // Calculate each component
        var dissonance = CalculateDissonance(chord);
        var hierarchical = CalculateHierarchicalTension(chord, key);
        var tonalDist = ChordDistance.GetRelativeTension(chord, key);
        var voiceLeading = previousChord != null
            ? CalculateVoiceLeadingTension(previousChord, chord)
            : 0.0;

        return Calculate(dissonance, hierarchical, tonalDist, voiceLeading);
    }

    /// <summary>
    /// Calculates dissonance based on chord quality and extensions.
    /// </summary>
    /// <param name="chord">The chord.</param>
    /// <returns>Dissonance value (0-1).</returns>
    public static double CalculateDissonance(Chord chord)
    {
        // Base dissonance by quality
        var baseDissonance = chord.Quality switch
        {
            ChordQuality.Major => 0.0,
            ChordQuality.Minor => 0.1,
            ChordQuality.Dominant7 => 0.3,
            ChordQuality.Major7 => 0.25,
            ChordQuality.Minor7 => 0.25,
            ChordQuality.Diminished => 0.5,
            ChordQuality.HalfDiminished7 => 0.55,
            ChordQuality.Diminished7 => 0.6,
            ChordQuality.Augmented => 0.45,
            _ => 0.2
        };

        // Add for extensions beyond 7th
        var extensionCount = chord.PitchClasses.Count - 3;
        var extensionDissonance = extensionCount > 1 ? (extensionCount - 1) * 0.1 : 0.0;

        return Math.Clamp(baseDissonance + extensionDissonance, 0.0, 1.0);
    }

    /// <summary>
    /// Calculates hierarchical tension based on position in harmonic hierarchy.
    /// </summary>
    /// <param name="chord">The chord.</param>
    /// <param name="key">The key context.</param>
    /// <returns>Hierarchical tension (0-1).</returns>
    public static double CalculateHierarchicalTension(Chord chord, Scale key)
    {
        var space = new BasicSpace(key);
        var rootStability = space.GetStabilityLevel(chord.Root);

        // Convert stability (4=most stable) to tension (0=least tense)
        return 1.0 - (rootStability / 4.0);
    }

    /// <summary>
    /// Calculates voice leading tension between two chords.
    /// </summary>
    /// <param name="from">Source chord.</param>
    /// <param name="to">Target chord.</param>
    /// <returns>Voice leading tension (0-1).</returns>
    public static double CalculateVoiceLeadingTension(Chord from, Chord to)
    {
        // Simple estimation based on root motion
        var rootMotion = Math.Abs((int)to.Root - (int)from.Root);
        if (rootMotion > 6) rootMotion = 12 - rootMotion; // Shortest path

        // Smooth voice leading has small root motion
        var motionTension = rootMotion switch
        {
            0 => 0.0,
            1 or 2 => 0.2,
            5 => 0.3, // Perfect fourth
            7 => 0.3, // Perfect fifth
            3 or 4 => 0.4,
            _ => 0.5
        };

        // Add tension for quality changes
        var qualityChange = from.Quality != to.Quality ? 0.1 : 0.0;

        return Math.Clamp(motionTension + qualityChange, 0.0, 1.0);
    }

    /// <summary>
    /// Calculates the tension trajectory over a chord sequence.
    /// </summary>
    /// <param name="chords">The chord sequence.</param>
    /// <param name="key">The key context.</param>
    /// <returns>Array of tension values for each chord.</returns>
    public static double[] CalculateTensionCurve(IReadOnlyList<Chord> chords, Scale key)
    {
        var tensions = new double[chords.Count];

        for (var i = 0; i < chords.Count; i++)
        {
            var previous = i > 0 ? chords[i - 1] : null;
            tensions[i] = CalculateForChord(chords[i], key, previous);
        }

        return tensions;
    }

    /// <summary>
    /// Identifies tension peaks in a sequence.
    /// </summary>
    /// <param name="tensions">The tension values.</param>
    /// <param name="threshold">Minimum tension for a peak.</param>
    /// <returns>Indices of tension peaks.</returns>
    public static List<int> FindTensionPeaks(double[] tensions, double threshold = 0.5)
    {
        var peaks = new List<int>();

        for (var i = 1; i < tensions.Length - 1; i++)
        {
            if (tensions[i] > threshold &&
                tensions[i] > tensions[i - 1] &&
                tensions[i] >= tensions[i + 1])
            {
                peaks.Add(i);
            }
        }

        return peaks;
    }

    /// <summary>
    /// Calculates the resolution strength from one tension level to another.
    /// Larger drops indicate stronger resolution.
    /// </summary>
    /// <param name="tensionBefore">Tension before resolution.</param>
    /// <param name="tensionAfter">Tension after resolution.</param>
    /// <returns>Resolution strength (0-1).</returns>
    public static double CalculateResolutionStrength(double tensionBefore, double tensionAfter)
    {
        var drop = tensionBefore - tensionAfter;
        return Math.Clamp(drop, 0.0, 1.0);
    }

    /// <summary>
    /// Reference tension values for common harmonic functions.
    /// </summary>
    public static class Reference
    {
        /// <summary>Tension of tonic chord (I or i)</summary>
        public const double Tonic = 0.1;

        /// <summary>Tension of subdominant chord (IV or iv)</summary>
        public const double Subdominant = 0.35;

        /// <summary>Tension of dominant chord (V)</summary>
        public const double Dominant = 0.5;

        /// <summary>Tension of dominant seventh (V7)</summary>
        public const double DominantSeventh = 0.65;

        /// <summary>Tension of diminished seventh</summary>
        public const double DiminishedSeventh = 0.8;

        /// <summary>Tension of secondary dominant</summary>
        public const double SecondaryDominant = 0.6;
    }
}
