namespace BeyondImmersion.Bannou.MusicStoryteller.Theory;

/// <summary>
/// Calculates Shannon entropy for musical probability distributions.
/// H = -Σ pᵢ × log₂(pᵢ)
/// Higher entropy indicates greater uncertainty/variety.
/// </summary>
public static class EntropyCalculator
{
    /// <summary>
    /// Calculates Shannon entropy from a probability distribution.
    /// H = -Σ pᵢ × log₂(pᵢ)
    /// </summary>
    /// <param name="probabilities">The probability distribution (should sum to 1).</param>
    /// <returns>Entropy in bits.</returns>
    public static double Calculate(IEnumerable<double> probabilities)
    {
        var entropy = 0.0;

        foreach (var p in probabilities)
        {
            if (p > 0 && p <= 1)
            {
                entropy -= p * Math.Log2(p);
            }
        }

        return entropy;
    }

    /// <summary>
    /// Calculates entropy from a dictionary of outcomes and their counts.
    /// </summary>
    /// <typeparam name="T">The type of outcomes.</typeparam>
    /// <param name="counts">Dictionary of outcome to count.</param>
    /// <returns>Entropy in bits.</returns>
    public static double CalculateFromCounts<T>(IDictionary<T, int> counts) where T : notnull
    {
        var total = counts.Values.Sum();
        if (total == 0) return 0;

        var probabilities = counts.Values.Select(c => (double)c / total);
        return Calculate(probabilities);
    }

    /// <summary>
    /// Calculates the maximum possible entropy for a given number of outcomes.
    /// H_max = log₂(n)
    /// </summary>
    /// <param name="outcomeCount">Number of possible outcomes.</param>
    /// <returns>Maximum entropy in bits.</returns>
    public static double CalculateMaximum(int outcomeCount)
    {
        if (outcomeCount <= 1) return 0;
        return Math.Log2(outcomeCount);
    }

    /// <summary>
    /// Calculates normalized entropy (0-1).
    /// H_normalized = H / H_max
    /// </summary>
    /// <param name="probabilities">The probability distribution.</param>
    /// <param name="outcomeCount">Total number of possible outcomes.</param>
    /// <returns>Normalized entropy (0-1).</returns>
    public static double CalculateNormalized(IEnumerable<double> probabilities, int outcomeCount)
    {
        var maxEntropy = CalculateMaximum(outcomeCount);
        if (maxEntropy == 0) return 0;

        var entropy = Calculate(probabilities);
        return entropy / maxEntropy;
    }

    /// <summary>
    /// Calculates entropy of a melodic interval distribution.
    /// </summary>
    /// <param name="intervals">The intervals observed.</param>
    /// <returns>Entropy in bits.</returns>
    public static double CalculateMelodicEntropy(IEnumerable<int> intervals)
    {
        var counts = new Dictionary<int, int>();

        foreach (var interval in intervals)
        {
            counts[interval] = counts.GetValueOrDefault(interval, 0) + 1;
        }

        return CalculateFromCounts(counts);
    }

    /// <summary>
    /// Calculates entropy of a rhythmic pattern.
    /// </summary>
    /// <param name="durations">The note durations.</param>
    /// <returns>Entropy in bits.</returns>
    public static double CalculateRhythmicEntropy(IEnumerable<double> durations)
    {
        // Quantize durations to nearest rhythmic value
        var counts = new Dictionary<double, int>();

        foreach (var duration in durations)
        {
            var quantized = QuantizeDuration(duration);
            counts[quantized] = counts.GetValueOrDefault(quantized, 0) + 1;
        }

        return CalculateFromCounts(counts);
    }

    /// <summary>
    /// Calculates entropy of harmonic rhythm (chord changes).
    /// </summary>
    /// <param name="chordDurations">The duration of each chord.</param>
    /// <returns>Entropy in bits.</returns>
    public static double CalculateHarmonicRhythmEntropy(IEnumerable<double> chordDurations)
    {
        return CalculateRhythmicEntropy(chordDurations);
    }

    /// <summary>
    /// Estimates the "predictability" of a sequence based on entropy.
    /// Lower entropy = more predictable.
    /// </summary>
    /// <param name="entropy">The entropy value.</param>
    /// <param name="maxEntropy">The maximum possible entropy.</param>
    /// <returns>Predictability (0-1).</returns>
    public static double CalculatePredictability(double entropy, double maxEntropy)
    {
        if (maxEntropy <= 0) return 1.0;
        return 1.0 - (entropy / maxEntropy);
    }

    /// <summary>
    /// Calculates cross-entropy between predicted and actual distributions.
    /// H(p, q) = -Σ p(x) × log₂(q(x))
    /// </summary>
    /// <param name="actual">Actual distribution.</param>
    /// <param name="predicted">Predicted distribution.</param>
    /// <returns>Cross-entropy in bits.</returns>
    public static double CalculateCrossEntropy(
        IDictionary<int, double> actual,
        IDictionary<int, double> predicted)
    {
        var crossEntropy = 0.0;

        foreach (var (key, p) in actual)
        {
            if (p > 0 && predicted.TryGetValue(key, out var q) && q > 0)
            {
                crossEntropy -= p * Math.Log2(q);
            }
            else if (p > 0)
            {
                // Predicted probability is 0 for an actual outcome - infinite cross-entropy
                crossEntropy += p * 10; // Use large value instead of infinity
            }
        }

        return crossEntropy;
    }

    /// <summary>
    /// Calculates KL-divergence between actual and predicted distributions.
    /// D_KL(P || Q) = Σ P(x) × log₂(P(x) / Q(x))
    /// </summary>
    /// <param name="actual">Actual distribution P.</param>
    /// <param name="predicted">Predicted distribution Q.</param>
    /// <returns>KL-divergence in bits.</returns>
    public static double CalculateKLDivergence(
        IDictionary<int, double> actual,
        IDictionary<int, double> predicted)
    {
        var actualEntropy = Calculate(actual.Values);
        var crossEntropy = CalculateCrossEntropy(actual, predicted);
        return crossEntropy - actualEntropy;
    }

    private static double QuantizeDuration(double duration)
    {
        // Quantize to common rhythmic values
        // 1.0 = quarter note
        if (duration < 0.1875) return 0.125;  // 32nd
        if (duration < 0.375) return 0.25;    // 16th
        if (duration < 0.75) return 0.5;      // 8th
        if (duration < 1.5) return 1.0;       // Quarter
        if (duration < 3.0) return 2.0;       // Half
        return 4.0;                            // Whole
    }

    /// <summary>
    /// Reference entropy values for musical contexts.
    /// </summary>
    public static class Reference
    {
        /// <summary>Entropy of uniform distribution over 12 pitch classes</summary>
        public const double MaxPitchClassEntropy = 3.585; // log₂(12)

        /// <summary>Entropy of uniform distribution over 7 scale degrees</summary>
        public const double MaxScaleDegreeEntropy = 2.807; // log₂(7)

        /// <summary>Entropy of typical melodic interval distribution</summary>
        public const double TypicalMelodicEntropy = 2.5;

        /// <summary>Entropy of typical harmonic progression</summary>
        public const double TypicalHarmonicEntropy = 2.0;

        /// <summary>Low entropy threshold (predictable)</summary>
        public const double LowEntropyThreshold = 1.5;

        /// <summary>High entropy threshold (unpredictable)</summary>
        public const double HighEntropyThreshold = 3.0;
    }
}
