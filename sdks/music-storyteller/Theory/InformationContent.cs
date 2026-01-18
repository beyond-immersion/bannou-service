namespace BeyondImmersion.Bannou.MusicStoryteller.Theory;

/// <summary>
/// Calculates Information Content (IC) for musical events.
/// IC = -log₂P(event|context)
/// Higher IC indicates more surprising/unexpected events.
/// Source: Pearce, M.T. (2005). "The Construction and Evaluation of Statistical Models of Melody"
/// </summary>
public static class InformationContent
{
    /// <summary>
    /// Calculates information content from a probability value.
    /// IC = -log₂(p)
    /// </summary>
    /// <param name="probability">The probability of the event (0-1).</param>
    /// <returns>Information content in bits.</returns>
    public static double Calculate(double probability)
    {
        if (probability <= 0)
        {
            return double.MaxValue; // Impossible event = infinite surprise
        }

        if (probability >= 1)
        {
            return 0; // Certain event = no surprise
        }

        return -Math.Log2(probability);
    }

    /// <summary>
    /// Calculates IC for an interval based on transition probabilities.
    /// </summary>
    /// <param name="interval">The interval in semitones.</param>
    /// <param name="context">The style context (affects probability distribution).</param>
    /// <returns>Information content in bits.</returns>
    public static double CalculateForInterval(int interval, string? context = null)
    {
        var probability = GetIntervalProbability(interval, context);
        return Calculate(probability);
    }

    /// <summary>
    /// Calculates IC for a scale degree in context.
    /// </summary>
    /// <param name="degree">Scale degree (1-7).</param>
    /// <param name="previousDegree">Previous scale degree.</param>
    /// <returns>Information content in bits.</returns>
    public static double CalculateForScaleDegree(int degree, int? previousDegree = null)
    {
        double probability;

        if (previousDegree.HasValue)
        {
            // Use transition probability
            probability = GetDegreeTransitionProbability(previousDegree.Value, degree);
        }
        else
        {
            // Use stationary probability
            probability = GetDegreeStationaryProbability(degree);
        }

        return Calculate(probability);
    }

    /// <summary>
    /// Calculates IC for a chord progression step.
    /// </summary>
    /// <param name="fromDegree">Previous chord degree.</param>
    /// <param name="toDegree">Current chord degree.</param>
    /// <returns>Information content in bits.</returns>
    public static double CalculateForHarmony(int fromDegree, int toDegree)
    {
        var probability = GetHarmonicTransitionProbability(fromDegree, toDegree);
        return Calculate(probability);
    }

    /// <summary>
    /// Estimates the surprise level (0-1) from IC.
    /// </summary>
    /// <param name="informationContent">The IC in bits.</param>
    /// <returns>Normalized surprise level (0-1).</returns>
    public static double NormalizeSurprise(double informationContent)
    {
        // Typical IC range is 0-6 bits for musical events
        // Map to 0-1 using sigmoid-like function
        return 1.0 - 1.0 / (1.0 + informationContent / 3.0);
    }

    /// <summary>
    /// Gets the probability for a melodic interval.
    /// Based on corpus studies of interval distributions.
    /// </summary>
    private static double GetIntervalProbability(int interval, string? context)
    {
        // Default distribution (roughly based on Krumhansl & Shepard)
        var absInterval = Math.Abs(interval);

        // Context-specific adjustments could be made here
        return absInterval switch
        {
            0 => 0.20,  // Unison/repeat
            1 => 0.18,  // Minor second
            2 => 0.22,  // Major second
            3 => 0.12,  // Minor third
            4 => 0.10,  // Major third
            5 => 0.07,  // Perfect fourth
            6 => 0.02,  // Tritone
            7 => 0.05,  // Perfect fifth
            8 => 0.02,  // Minor sixth
            9 => 0.01,  // Major sixth
            10 => 0.005, // Minor seventh
            11 => 0.003, // Major seventh
            12 => 0.01,  // Octave
            _ => 0.001   // Larger intervals
        };
    }

    /// <summary>
    /// Gets stationary probability for a scale degree.
    /// </summary>
    private static double GetDegreeStationaryProbability(int degree)
    {
        // Based on corpus statistics
        return degree switch
        {
            1 => 0.25,  // Do - most common
            2 => 0.12,  // Re
            3 => 0.14,  // Mi
            4 => 0.08,  // Fa
            5 => 0.20,  // Sol - second most common
            6 => 0.10,  // La
            7 => 0.06,  // Ti
            _ => 0.05
        };
    }

    /// <summary>
    /// Gets transition probability between scale degrees.
    /// </summary>
    private static double GetDegreeTransitionProbability(int from, int to)
    {
        // Simplified transition matrix (would be style-specific in full implementation)
        // High probability for stepwise motion
        var distance = Math.Abs(to - from);

        return distance switch
        {
            0 => 0.10,  // Repeat
            1 => 0.35,  // Step
            2 => 0.25,  // Skip (third)
            3 => 0.12,  // Small leap
            4 => 0.08,  // Leap
            5 => 0.05,  // Large leap
            6 => 0.03,  // Very large leap
            _ => 0.02
        };
    }

    /// <summary>
    /// Gets transition probability for harmonic progressions.
    /// </summary>
    private static double GetHarmonicTransitionProbability(int from, int to)
    {
        // Common progressions have high probability
        return (from, to) switch
        {
            (5, 1) => 0.40,  // V → I
            (4, 5) => 0.25,  // IV → V
            (1, 4) => 0.20,  // I → IV
            (1, 5) => 0.20,  // I → V
            (2, 5) => 0.30,  // ii → V
            (5, 6) => 0.10,  // V → vi (deceptive)
            (6, 4) => 0.15,  // vi → IV
            (6, 2) => 0.15,  // vi → ii
            (4, 1) => 0.15,  // IV → I (plagal)
            (1, 6) => 0.12,  // I → vi
            (1, 1) => 0.08,  // I → I (prolongation)
            _ => 0.05        // Other progressions
        };
    }

    /// <summary>
    /// Reference IC values for common musical events.
    /// </summary>
    public static class Reference
    {
        /// <summary>IC for very common event (p ≈ 0.5)</summary>
        public const double VeryCommon = 1.0;

        /// <summary>IC for common event (p ≈ 0.25)</summary>
        public const double Common = 2.0;

        /// <summary>IC for moderate event (p ≈ 0.125)</summary>
        public const double Moderate = 3.0;

        /// <summary>IC for uncommon event (p ≈ 0.0625)</summary>
        public const double Uncommon = 4.0;

        /// <summary>IC for rare event (p ≈ 0.03)</summary>
        public const double Rare = 5.0;

        /// <summary>IC for very rare event (p ≈ 0.015)</summary>
        public const double VeryRare = 6.0;

        /// <summary>Threshold for "surprising" event</summary>
        public const double SurpriseThreshold = 3.0;
    }
}
