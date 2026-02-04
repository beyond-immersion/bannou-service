using BeyondImmersion.Bannou.StorylineTheory.Structure;

namespace BeyondImmersion.Bannou.StorylineTheory.Scoring;

/// <summary>
/// Scores story pacing against beat sheet timing expectations.
/// Based on Blake Snyder's Save the Cat methodology (2005).
///
/// WEIGHT DERIVATION: These weights are design hypotheses based on:
/// - Critical beats (Catalyst, Midpoint, All Is Lost) are structural pillars
/// - Order violations create narrative confusion
/// - Timing drift accumulates but early beats set expectations
/// Configurable to allow empirical tuning via A/B testing.
/// </summary>
public static class PacingSatisfactionScorer
{
    /// <summary>
    /// Hypothesis weights for pacing satisfaction scoring.
    /// Subject to empirical calibration.
    /// </summary>
    public static class Weights
    {
        /// <summary>
        /// Weight for critical beat timing accuracy.
        /// Rationale: Critical beats are the structural pillars of the story.
        /// </summary>
        public const double CriticalBeatTiming = 0.40;

        /// <summary>
        /// Weight for beat order correctness.
        /// Rationale: Out-of-order beats confuse the narrative flow.
        /// </summary>
        public const double BeatOrder = 0.30;

        /// <summary>
        /// Weight for overall timing drift.
        /// Rationale: Cumulative timing errors indicate pacing problems.
        /// </summary>
        public const double OverallTiming = 0.20;

        /// <summary>
        /// Weight for beat coverage (are all beats present?).
        /// Rationale: Missing beats create structural gaps.
        /// </summary>
        public const double BeatCoverage = 0.10;
    }

    /// <summary>
    /// Timing strategy for beat percentage targets.
    /// </summary>
    public const string DefaultTimingStrategy = "fiction";

    /// <summary>
    /// Calculate pacing satisfaction score.
    /// </summary>
    /// <param name="beatOccurrences">List of (beat code, actual position 0-1) in occurrence order.</param>
    /// <param name="timingStrategy">Timing strategy: "bs2" for screenplay, "fiction" for novels.</param>
    /// <returns>Pacing satisfaction score (0-1). Higher scores indicate better pacing.</returns>
    public static double Calculate(
        IReadOnlyList<(string BeatCode, double Position)> beatOccurrences,
        string timingStrategy = DefaultTimingStrategy)
    {
        if (beatOccurrences.Count == 0) return 0.0;

        // 1. Critical beat timing accuracy
        var criticalScore = CalculateCriticalBeatTiming(beatOccurrences, timingStrategy);

        // 2. Beat order correctness
        var orderScore = CalculateBeatOrderScore(beatOccurrences);

        // 3. Overall timing drift
        var timingScore = CalculateOverallTimingScore(beatOccurrences, timingStrategy);

        // 4. Beat coverage
        var coverageScore = CalculateBeatCoverageScore(beatOccurrences);

        return Weights.CriticalBeatTiming * criticalScore +
               Weights.BeatOrder * orderScore +
               Weights.OverallTiming * timingScore +
               Weights.BeatCoverage * coverageScore;
    }

    /// <summary>
    /// Calculate timing accuracy for critical beats only.
    /// </summary>
    private static double CalculateCriticalBeatTiming(
        IReadOnlyList<(string BeatCode, double Position)> beatOccurrences,
        string timingStrategy)
    {
        var criticalBeats = SaveTheCatBeats.CriticalBeats;
        var scores = new List<double>();

        foreach (var critical in criticalBeats)
        {
            var occurrence = beatOccurrences
                .FirstOrDefault(b => b.BeatCode.Equals(critical.Code, StringComparison.OrdinalIgnoreCase));

            if (occurrence.BeatCode != null)
            {
                var target = critical.GetTargetPosition(timingStrategy);
                var deviation = Math.Abs(occurrence.Position - target);
                // Score decreases as deviation increases, with tolerance as baseline
                var score = Math.Max(0, 1.0 - deviation / Math.Max(critical.Tolerance, 0.1));
                scores.Add(score);
            }
            // Missing critical beats don't contribute (handled by coverage)
        }

        return scores.Count > 0 ? scores.Average() : 0.0;
    }

    /// <summary>
    /// Calculate beat order correctness.
    /// Returns 1.0 if beats are in correct order, decreases with inversions.
    /// </summary>
    private static double CalculateBeatOrderScore(
        IReadOnlyList<(string BeatCode, double Position)> beatOccurrences)
    {
        if (beatOccurrences.Count < 2) return 1.0;

        var inversions = 0;
        var comparisons = 0;

        for (var i = 0; i < beatOccurrences.Count - 1; i++)
        {
            var currentBeat = SaveTheCatBeats.GetByCode(beatOccurrences[i].BeatCode);
            var nextBeat = SaveTheCatBeats.GetByCode(beatOccurrences[i + 1].BeatCode);

            if (currentBeat == null || nextBeat == null) continue;

            comparisons++;
            if (currentBeat.PositionIndex > nextBeat.PositionIndex)
            {
                inversions++;
            }
        }

        return comparisons > 0
            ? 1.0 - (double)inversions / comparisons
            : 1.0;
    }

    /// <summary>
    /// Calculate overall timing drift across all beats.
    /// </summary>
    private static double CalculateOverallTimingScore(
        IReadOnlyList<(string BeatCode, double Position)> beatOccurrences,
        string timingStrategy)
    {
        var deviations = new List<double>();

        foreach (var (beatCode, position) in beatOccurrences)
        {
            var beat = SaveTheCatBeats.GetByCode(beatCode);
            if (beat == null) continue;

            var target = beat.GetTargetPosition(timingStrategy);
            var deviation = Math.Abs(position - target);

            // Weight by beat importance
            deviations.Add(deviation * beat.Importance);
        }

        if (deviations.Count == 0) return 0.0;

        // Average weighted deviation, normalized
        var avgDeviation = deviations.Average();
        // Max reasonable deviation is ~0.5 (half the story off)
        return Math.Max(0, 1.0 - avgDeviation * 2);
    }

    /// <summary>
    /// Calculate beat coverage score.
    /// </summary>
    private static double CalculateBeatCoverageScore(
        IReadOnlyList<(string BeatCode, double Position)> beatOccurrences)
    {
        var allBeats = SaveTheCatBeats.All;
        var presentCodes = beatOccurrences
            .Select(b => b.BeatCode)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Weight coverage by beat importance
        var totalImportance = allBeats.Sum(b => b.Importance);
        var coveredImportance = allBeats
            .Where(b => presentCodes.Contains(b.Code))
            .Sum(b => b.Importance);

        return totalImportance > 0 ? coveredImportance / totalImportance : 0.0;
    }

    /// <summary>
    /// Gets a detailed breakdown of pacing for analysis.
    /// </summary>
    public static PacingBreakdown GetBreakdown(
        IReadOnlyList<(string BeatCode, double Position)> beatOccurrences,
        string timingStrategy = DefaultTimingStrategy)
    {
        var beatTimings = new List<BeatTimingAnalysis>();

        foreach (var (beatCode, position) in beatOccurrences)
        {
            var beat = SaveTheCatBeats.GetByCode(beatCode);
            if (beat == null) continue;

            var target = beat.GetTargetPosition(timingStrategy);
            var deviation = position - target;

            beatTimings.Add(new BeatTimingAnalysis(
                BeatCode: beatCode,
                BeatName: beat.Name,
                ActualPosition: position,
                TargetPosition: target,
                Deviation: deviation,
                WithinTolerance: Math.Abs(deviation) <= beat.Tolerance,
                IsCritical: beat.IsCritical));
        }

        var missingBeats = SaveTheCatBeats.All
            .Where(b => !beatOccurrences.Any(bo =>
                bo.BeatCode.Equals(b.Code, StringComparison.OrdinalIgnoreCase)))
            .Select(b => b.Code)
            .ToList();

        // Find order inversions
        var inversions = new List<(string First, string Second)>();
        for (var i = 0; i < beatOccurrences.Count - 1; i++)
        {
            var currentBeat = SaveTheCatBeats.GetByCode(beatOccurrences[i].BeatCode);
            var nextBeat = SaveTheCatBeats.GetByCode(beatOccurrences[i + 1].BeatCode);

            if (currentBeat != null && nextBeat != null &&
                currentBeat.PositionIndex > nextBeat.PositionIndex)
            {
                inversions.Add((beatOccurrences[i].BeatCode, beatOccurrences[i + 1].BeatCode));
            }
        }

        return new PacingBreakdown(
            TotalScore: Calculate(beatOccurrences, timingStrategy),
            CriticalBeatScore: CalculateCriticalBeatTiming(beatOccurrences, timingStrategy),
            OrderScore: CalculateBeatOrderScore(beatOccurrences),
            TimingScore: CalculateOverallTimingScore(beatOccurrences, timingStrategy),
            CoverageScore: CalculateBeatCoverageScore(beatOccurrences),
            BeatTimings: beatTimings,
            MissingBeats: missingBeats,
            OrderInversions: inversions);
    }

    /// <summary>
    /// Suggests the next beat based on current story position.
    /// </summary>
    /// <param name="currentPosition">Current position in the story (0-1).</param>
    /// <param name="satisfiedBeatCodes">Beats that have already occurred.</param>
    /// <param name="timingStrategy">Timing strategy for targets.</param>
    /// <returns>The recommended next beat, or null if story is complete.</returns>
    public static SaveTheCatBeat? SuggestNextBeat(
        double currentPosition,
        IReadOnlyCollection<string> satisfiedBeatCodes,
        string timingStrategy = DefaultTimingStrategy)
    {
        var unsatisfied = SaveTheCatBeats.All
            .Where(b => !satisfiedBeatCodes.Contains(b.Code, StringComparer.OrdinalIgnoreCase))
            .OrderBy(b => b.PositionIndex)
            .ToList();

        if (unsatisfied.Count == 0) return null;

        // Find the first unsatisfied beat whose target is at or after current position
        // Or if we've passed all targets, return the next one in sequence
        var upcoming = unsatisfied
            .FirstOrDefault(b => b.GetTargetPosition(timingStrategy) >= currentPosition - b.Tolerance);

        return upcoming ?? unsatisfied.First();
    }
}

/// <summary>
/// Detailed breakdown of pacing analysis.
/// </summary>
/// <param name="TotalScore">Overall pacing score (0-1).</param>
/// <param name="CriticalBeatScore">Score for critical beat timing (0-1).</param>
/// <param name="OrderScore">Score for beat order correctness (0-1).</param>
/// <param name="TimingScore">Score for overall timing accuracy (0-1).</param>
/// <param name="CoverageScore">Score for beat coverage (0-1).</param>
/// <param name="BeatTimings">Detailed timing analysis per beat.</param>
/// <param name="MissingBeats">Codes of beats not yet satisfied.</param>
/// <param name="OrderInversions">Pairs of beats that are out of order.</param>
public sealed record PacingBreakdown(
    double TotalScore,
    double CriticalBeatScore,
    double OrderScore,
    double TimingScore,
    double CoverageScore,
    IReadOnlyList<BeatTimingAnalysis> BeatTimings,
    IReadOnlyList<string> MissingBeats,
    IReadOnlyList<(string First, string Second)> OrderInversions);

/// <summary>
/// Timing analysis for a single beat occurrence.
/// </summary>
/// <param name="BeatCode">The beat's code.</param>
/// <param name="BeatName">The beat's display name.</param>
/// <param name="ActualPosition">Where the beat actually occurred (0-1).</param>
/// <param name="TargetPosition">Where the beat should ideally occur (0-1).</param>
/// <param name="Deviation">Difference from target (can be negative for early, positive for late).</param>
/// <param name="WithinTolerance">Whether the beat is within acceptable timing range.</param>
/// <param name="IsCritical">Whether this is a critical structural beat.</param>
public sealed record BeatTimingAnalysis(
    string BeatCode,
    string BeatName,
    double ActualPosition,
    double TargetPosition,
    double Deviation,
    bool WithinTolerance,
    bool IsCritical);
