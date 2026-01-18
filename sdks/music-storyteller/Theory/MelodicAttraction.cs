using BeyondImmersion.Bannou.MusicTheory.Pitch;

namespace BeyondImmersion.Bannou.MusicStoryteller.Theory;

/// <summary>
/// Calculates melodic attraction using Lerdahl's formula.
/// α = (s₂/s₁) × (1/n²)
/// Where:
///   s₁ = stability of source pitch
///   s₂ = stability of target pitch
///   n = distance in semitones between pitches
/// Source: Lerdahl, F. (2001). Tonal Pitch Space, Chapter 5.
/// </summary>
public static class MelodicAttraction
{
    /// <summary>
    /// Calculates the melodic attraction from source to target pitch.
    /// Higher values indicate stronger attraction (more expected resolution).
    /// </summary>
    /// <param name="sourcePitch">The source pitch (MIDI number or semitone).</param>
    /// <param name="targetPitch">The target pitch (MIDI number or semitone).</param>
    /// <param name="context">The basic space context for stability calculation.</param>
    /// <returns>Attraction value (typically 0.0 to 4.0, higher = stronger attraction).</returns>
    public static double Calculate(int sourcePitch, int targetPitch, BasicSpace context)
    {
        // Handle same pitch (no movement)
        if (sourcePitch == targetPitch)
        {
            return 0.0;
        }

        // Get stability levels (1-4 scale)
        var s1 = context.GetStabilityLevel(sourcePitch % 12);
        var s2 = context.GetStabilityLevel(targetPitch % 12);

        // Distance in semitones
        var n = Math.Abs(targetPitch - sourcePitch);

        // Lerdahl's formula: α = (s₂/s₁) × (1/n²)
        // Higher s₂ (more stable target) increases attraction
        // Higher s₁ (more stable source) decreases attraction
        // Larger distance decreases attraction quadratically
        if (s1 == 0) s1 = 1; // Prevent division by zero

        return (s2 / (double)s1) * (1.0 / (n * n));
    }

    /// <summary>
    /// Calculates melodic attraction using Pitch struct.
    /// </summary>
    public static double Calculate(Pitch source, Pitch target, BasicSpace context)
    {
        return Calculate(source.MidiNumber, target.MidiNumber, context);
    }

    /// <summary>
    /// Calculates the total melodic attraction from a pitch to all diatonic scale degrees.
    /// Identifies which pitch is most strongly attracted.
    /// </summary>
    /// <param name="sourcePitch">The source pitch (MIDI number).</param>
    /// <param name="context">The basic space context.</param>
    /// <param name="octave">The octave for target pitches.</param>
    /// <returns>Dictionary of target pitch class to attraction value.</returns>
    public static Dictionary<int, double> CalculateAllAttractions(int sourcePitch, BasicSpace context, int octave = 4)
    {
        var attractions = new Dictionary<int, double>();

        foreach (var targetPc in context.DiatonicLevel)
        {
            // Calculate target MIDI number in the same octave region
            var targetMidi = (octave + 1) * 12 + targetPc;

            // Check both the same octave and adjacent octaves for closest pitch
            var distances = new[]
            {
                targetMidi - 12,
                targetMidi,
                targetMidi + 12
            };

            // Find closest target to source
            var closest = distances.OrderBy(d => Math.Abs(d - sourcePitch)).First();

            var attraction = Calculate(sourcePitch, closest, context);
            attractions[targetPc] = attraction;
        }

        return attractions;
    }

    /// <summary>
    /// Gets the most attracted target for a given source pitch.
    /// </summary>
    /// <param name="sourcePitch">The source pitch (MIDI number).</param>
    /// <param name="context">The basic space context.</param>
    /// <param name="octave">The octave for target pitches.</param>
    /// <returns>The pitch class with highest attraction, and its attraction value.</returns>
    public static (int targetPitchClass, double attraction) GetStrongestAttraction(
        int sourcePitch, BasicSpace context, int octave = 4)
    {
        var attractions = CalculateAllAttractions(sourcePitch, context, octave);
        var strongest = attractions.MaxBy(kvp => kvp.Value);
        return (strongest.Key, strongest.Value);
    }

    /// <summary>
    /// Calculates the expectedness of a melodic resolution.
    /// High attraction means the resolution is expected; low means surprising.
    /// </summary>
    /// <param name="source">Source pitch.</param>
    /// <param name="actual">Actual target pitch (what happened).</param>
    /// <param name="context">The basic space context.</param>
    /// <returns>Expectedness from 0 (unexpected) to 1 (highly expected).</returns>
    public static double CalculateExpectedness(Pitch source, Pitch actual, BasicSpace context)
    {
        // Get attraction to actual target
        var actualAttraction = Calculate(source, actual, context);

        // Get maximum possible attraction (strongest expected target)
        var (_, maxAttraction) = GetStrongestAttraction(source.MidiNumber, context, source.Octave);

        // Normalize: how close is actual to maximum expected?
        if (maxAttraction <= 0) return 0.5; // No clear expectation

        var ratio = actualAttraction / maxAttraction;
        return Math.Clamp(ratio, 0.0, 1.0);
    }

    /// <summary>
    /// Calculates voice leading smoothness based on attraction principles.
    /// Lower values indicate smoother voice leading.
    /// </summary>
    /// <param name="intervals">The melodic intervals (in semitones).</param>
    /// <param name="context">The basic space context.</param>
    /// <returns>Roughness value (0 = smoothest, higher = rougher).</returns>
    public static double CalculateVoiceLeadingRoughness(IEnumerable<int> intervals, BasicSpace context)
    {
        var roughness = 0.0;
        foreach (var interval in intervals)
        {
            var absInterval = Math.Abs(interval);

            // Roughness increases with larger intervals
            // Step motion (1-2 semitones) is smooth
            // Skip (3-4 semitones) is moderate
            // Leap (5+ semitones) is rough
            roughness += absInterval switch
            {
                0 => 0.0,
                1 => 0.1,
                2 => 0.15,
                3 => 0.25,
                4 => 0.35,
                5 => 0.5,
                6 => 0.6,
                7 => 0.5, // Perfect fifth is somewhat stable
                >= 8 => 0.8,
                _ => 0.0 // Should not occur with Math.Abs
            };
        }

        return roughness;
    }

    /// <summary>
    /// Common melodic attraction reference values.
    /// </summary>
    public static class Reference
    {
        /// <summary>Leading tone to tonic (B→C in C major) - maximum attraction</summary>
        public const double LeadingToneResolution = 4.0;

        /// <summary>Seventh to tonic (Ti→Do) typical attraction</summary>
        public const double SeventhToTonic = 2.0;

        /// <summary>Second to tonic (Re→Do) typical attraction</summary>
        public const double SecondToTonic = 1.0;

        /// <summary>Fourth to third (Fa→Mi) typical attraction</summary>
        public const double FourthToThird = 0.5;

        /// <summary>Threshold above which attraction is considered "strong"</summary>
        public const double StrongAttractionThreshold = 0.5;
    }
}
