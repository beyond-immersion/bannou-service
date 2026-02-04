using BeyondImmersion.Bannou.StorylineTheory.Genre;
using BeyondImmersion.Bannou.StorylineTheory.Structure;

namespace BeyondImmersion.Bannou.StorylineTheory.Scoring;

/// <summary>
/// Scores how well a story satisfies its genre contract.
/// Based on Shawn Coyne's Story Grid methodology (2015).
///
/// WEIGHT DERIVATION: These weights are design hypotheses based on:
/// - Story Grid's emphasis on obligatory scenes as genre contracts
/// - Convention presence as reader expectation fulfillment
/// - Core event as the defining moment of the genre
/// - Value spectrum range as emotional journey completeness
/// Configurable to allow empirical tuning via A/B testing.
/// </summary>
public static class GenreComplianceScorer
{
    /// <summary>
    /// Hypothesis weights for genre compliance scoring.
    /// Subject to empirical calibration.
    /// </summary>
    public static class Weights
    {
        /// <summary>
        /// Weight for obligatory scene completion.
        /// Rationale: Obligatory scenes are the genre's contract with the audience.
        /// </summary>
        public const double ObligatoryScenes = 0.40;

        /// <summary>
        /// Weight for convention presence.
        /// Rationale: Conventions set reader expectations for the genre.
        /// </summary>
        public const double Conventions = 0.25;

        /// <summary>
        /// Weight for core event presence.
        /// Rationale: The core event is the defining moment of the genre.
        /// </summary>
        public const double CoreEvent = 0.20;

        /// <summary>
        /// Weight for value spectrum coverage.
        /// Rationale: Emotional range defines the genre's impact.
        /// </summary>
        public const double ValueSpectrum = 0.15;
    }

    /// <summary>
    /// Calculate genre compliance score.
    /// </summary>
    /// <param name="genre">The target genre to evaluate against.</param>
    /// <param name="satisfiedObligatorySceneCodes">Codes of obligatory scenes that have been satisfied.</param>
    /// <param name="presentConventionCodes">Codes of conventions present in the story.</param>
    /// <param name="coreEventSatisfied">Whether the genre's core event has occurred.</param>
    /// <param name="minValuePosition">Minimum position reached on the value spectrum (-1 to +1).</param>
    /// <param name="maxValuePosition">Maximum position reached on the value spectrum (-1 to +1).</param>
    /// <returns>Compliance score (0-1). Higher scores indicate better genre fit.</returns>
    public static double Calculate(
        StoryGridGenre genre,
        IReadOnlyCollection<string> satisfiedObligatorySceneCodes,
        IReadOnlyCollection<string> presentConventionCodes,
        bool coreEventSatisfied,
        double minValuePosition,
        double maxValuePosition)
    {
        // 1. Obligatory scene completion ratio
        var obligatoryScore = genre.ObligatoryScenes.Count > 0
            ? CalculateObligatorySceneScore(genre, satisfiedObligatorySceneCodes)
            : 1.0; // No obligatory scenes = full score

        // 2. Convention presence ratio
        var conventionScore = genre.Conventions.Count > 0
            ? CalculateConventionScore(genre, presentConventionCodes)
            : 1.0; // No conventions = full score

        // 3. Core event satisfaction (binary)
        var coreEventScore = coreEventSatisfied ? 1.0 : 0.0;

        // 4. Value spectrum coverage
        var valueScore = genre.ValueSpectrum.CalculateRangeCovered(minValuePosition, maxValuePosition);

        return Weights.ObligatoryScenes * obligatoryScore +
               Weights.Conventions * conventionScore +
               Weights.CoreEvent * coreEventScore +
               Weights.ValueSpectrum * valueScore;
    }

    /// <summary>
    /// Calculate obligatory scene completion with importance weighting.
    /// More important scenes contribute more to the score.
    /// </summary>
    private static double CalculateObligatorySceneScore(
        StoryGridGenre genre,
        IReadOnlyCollection<string> satisfiedCodes)
    {
        // For now, simple ratio - all obligatory scenes weighted equally
        // Could be enhanced with per-scene importance if defined
        var satisfiedCount = genre.ObligatoryScenes
            .Count(os => satisfiedCodes.Contains(os.Code, StringComparer.OrdinalIgnoreCase));

        return (double)satisfiedCount / genre.ObligatoryScenes.Count;
    }

    /// <summary>
    /// Calculate convention presence score.
    /// </summary>
    private static double CalculateConventionScore(
        StoryGridGenre genre,
        IReadOnlyCollection<string> presentCodes)
    {
        var presentCount = genre.Conventions
            .Count(c => presentCodes.Contains(c.Code, StringComparer.OrdinalIgnoreCase));

        return (double)presentCount / genre.Conventions.Count;
    }

    /// <summary>
    /// Evaluates compliance for multiple genres (hybrid stories).
    /// Returns the weighted average based on genre blend ratios.
    /// </summary>
    /// <param name="genreBlends">Genre and its weight in the blend (weights should sum to 1).</param>
    /// <param name="satisfiedObligatorySceneCodes">All satisfied obligatory scene codes.</param>
    /// <param name="presentConventionCodes">All present convention codes.</param>
    /// <param name="satisfiedCoreEventCodes">Codes of core events that have been satisfied.</param>
    /// <param name="minValuePosition">Minimum value spectrum position.</param>
    /// <param name="maxValuePosition">Maximum value spectrum position.</param>
    /// <returns>Blended compliance score (0-1).</returns>
    public static double CalculateBlended(
        IReadOnlyList<(StoryGridGenre Genre, double Weight)> genreBlends,
        IReadOnlyCollection<string> satisfiedObligatorySceneCodes,
        IReadOnlyCollection<string> presentConventionCodes,
        IReadOnlyCollection<string> satisfiedCoreEventCodes,
        double minValuePosition,
        double maxValuePosition)
    {
        if (genreBlends.Count == 0) return 0.0;

        var totalWeight = genreBlends.Sum(g => g.Weight);
        if (totalWeight <= 0) return 0.0;

        var weightedSum = 0.0;
        foreach (var (genre, weight) in genreBlends)
        {
            var coreEventSatisfied = satisfiedCoreEventCodes
                .Contains(genre.CoreEvent, StringComparer.OrdinalIgnoreCase);

            var score = Calculate(
                genre,
                satisfiedObligatorySceneCodes,
                presentConventionCodes,
                coreEventSatisfied,
                minValuePosition,
                maxValuePosition);

            weightedSum += score * weight;
        }

        return weightedSum / totalWeight;
    }

    /// <summary>
    /// Gets a detailed breakdown of genre compliance for analysis.
    /// </summary>
    public static GenreComplianceBreakdown GetBreakdown(
        StoryGridGenre genre,
        IReadOnlyCollection<string> satisfiedObligatorySceneCodes,
        IReadOnlyCollection<string> presentConventionCodes,
        bool coreEventSatisfied,
        double minValuePosition,
        double maxValuePosition)
    {
        var missingObligatory = genre.ObligatoryScenes
            .Where(os => !satisfiedObligatorySceneCodes.Contains(os.Code, StringComparer.OrdinalIgnoreCase))
            .Select(os => os.Code)
            .ToList();

        var missingConventions = genre.Conventions
            .Where(c => !presentConventionCodes.Contains(c.Code, StringComparer.OrdinalIgnoreCase))
            .Select(c => c.Code)
            .ToList();

        return new GenreComplianceBreakdown(
            TotalScore: Calculate(genre, satisfiedObligatorySceneCodes, presentConventionCodes,
                coreEventSatisfied, minValuePosition, maxValuePosition),
            ObligatorySceneScore: genre.ObligatoryScenes.Count > 0
                ? CalculateObligatorySceneScore(genre, satisfiedObligatorySceneCodes)
                : 1.0,
            ConventionScore: genre.Conventions.Count > 0
                ? CalculateConventionScore(genre, presentConventionCodes)
                : 1.0,
            CoreEventSatisfied: coreEventSatisfied,
            ValueSpectrumCoverage: genre.ValueSpectrum.CalculateRangeCovered(minValuePosition, maxValuePosition),
            MissingObligatoryScenes: missingObligatory,
            MissingConventions: missingConventions);
    }
}

/// <summary>
/// Detailed breakdown of genre compliance scoring.
/// </summary>
/// <param name="TotalScore">Overall compliance score (0-1).</param>
/// <param name="ObligatorySceneScore">Score from obligatory scene satisfaction (0-1).</param>
/// <param name="ConventionScore">Score from convention presence (0-1).</param>
/// <param name="CoreEventSatisfied">Whether the core event occurred.</param>
/// <param name="ValueSpectrumCoverage">Coverage of the value spectrum (0-1).</param>
/// <param name="MissingObligatoryScenes">Codes of unsatisfied obligatory scenes.</param>
/// <param name="MissingConventions">Codes of missing conventions.</param>
public sealed record GenreComplianceBreakdown(
    double TotalScore,
    double ObligatorySceneScore,
    double ConventionScore,
    bool CoreEventSatisfied,
    double ValueSpectrumCoverage,
    IReadOnlyList<string> MissingObligatoryScenes,
    IReadOnlyList<string> MissingConventions);
