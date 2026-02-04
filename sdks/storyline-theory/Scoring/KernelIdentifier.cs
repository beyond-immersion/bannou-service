using BeyondImmersion.Bannou.StorylineTheory.Elements;

namespace BeyondImmersion.Bannou.StorylineTheory.Scoring;

/// <summary>
/// Classifies events as Kernels (essential) or Satellites (elaboration).
/// Based on Roland Barthes' "Introduction to the Structural Analysis of Narratives" (1966).
///
/// WEIGHT DERIVATION: These weights are design hypotheses based on:
/// - Barthes' conceptual hierarchy (kernels = branching points)
/// - Propp's phase ordering (later phases = higher stakes)
/// - Configurable to allow empirical tuning via A/B testing.
/// </summary>
public static class KernelIdentifier
{
    /// <summary>
    /// Hypothesis weights for kernel identification.
    /// Subject to empirical calibration.
    /// </summary>
    public static class Weights
    {
        /// <summary>
        /// Weight for Propp function satisfaction.
        /// Rationale: Functions define story grammar.
        /// </summary>
        public const double ProppFunction = 0.35;

        /// <summary>
        /// Weight for obligatory scene satisfaction.
        /// Rationale: Genre contracts with audience.
        /// </summary>
        public const double ObligatoryScene = 0.25;

        /// <summary>
        /// Weight for value change magnitude.
        /// Rationale: McKee's definition of dramatic unit.
        /// </summary>
        public const double ValueChange = 0.20;

        /// <summary>
        /// Weight for consequence ratio.
        /// Rationale: Causal importance in plot structure.
        /// </summary>
        public const double ConsequenceRatio = 0.20;
    }

    /// <summary>
    /// Default threshold for kernel classification.
    /// Events scoring above this are kernels; below are satellites.
    /// </summary>
    public const double DefaultThreshold = 0.5;

    /// <summary>
    /// Calculate kernel score for an event.
    /// </summary>
    /// <param name="proppFunctionPhase">The Propp phase if this event satisfies a function, null otherwise.</param>
    /// <param name="isObligatoryScene">Whether this event fulfills a genre obligatory scene.</param>
    /// <param name="valueChangeMagnitude">Magnitude of value spectrum change (0-1).</param>
    /// <param name="consequenceRatio">Fraction of story events that depend on this one (0-1).</param>
    /// <returns>Kernel score (0-1). Higher scores indicate more essential events.</returns>
    public static double Calculate(
        string? proppFunctionPhase,
        bool isObligatoryScene,
        double valueChangeMagnitude,
        double consequenceRatio)
    {
        // 1. Propp function score - uses phase-based significance
        var proppScore = proppFunctionPhase != null
            ? ProppFunctions.GetPhaseSignificance(proppFunctionPhase)
            : 0.0;

        // 2. Obligatory scene score - binary
        var obligatoryScore = isObligatoryScene ? 1.0 : 0.0;

        // 3. Value change - direct input, already normalized
        var valueScore = Math.Clamp(valueChangeMagnitude, 0.0, 1.0);

        // 4. Consequence ratio - direct input, already normalized
        var consequenceScore = Math.Clamp(consequenceRatio, 0.0, 1.0);

        return Weights.ProppFunction * proppScore +
               Weights.ObligatoryScene * obligatoryScore +
               Weights.ValueChange * valueScore +
               Weights.ConsequenceRatio * consequenceScore;
    }

    /// <summary>
    /// Calculate kernel score using a Propp function code directly.
    /// </summary>
    /// <param name="proppFunctionCode">The Propp function code (e.g., "villainy", "victory"), or null.</param>
    /// <param name="isObligatoryScene">Whether this event fulfills a genre obligatory scene.</param>
    /// <param name="valueChangeMagnitude">Magnitude of value spectrum change (0-1).</param>
    /// <param name="consequenceRatio">Fraction of story events that depend on this one (0-1).</param>
    /// <returns>Kernel score (0-1).</returns>
    public static double CalculateByFunctionCode(
        string? proppFunctionCode,
        bool isObligatoryScene,
        double valueChangeMagnitude,
        double consequenceRatio)
    {
        var phase = proppFunctionCode != null
            ? ProppFunctions.GetPhaseForFunction(proppFunctionCode)
            : null;

        // "unknown" phase means the code wasn't recognized
        if (phase == "unknown") phase = null;

        return Calculate(phase, isObligatoryScene, valueChangeMagnitude, consequenceRatio);
    }

    /// <summary>
    /// Determines if an event is a kernel based on its score.
    /// </summary>
    /// <param name="score">The kernel score.</param>
    /// <param name="threshold">Classification threshold (default 0.5).</param>
    /// <returns>True if the event is a kernel, false if satellite.</returns>
    public static bool IsKernel(double score, double threshold = DefaultThreshold) =>
        score > threshold;

    /// <summary>
    /// Classifies an event as kernel or satellite.
    /// </summary>
    public static KernelClassification Classify(
        string? proppFunctionPhase,
        bool isObligatoryScene,
        double valueChangeMagnitude,
        double consequenceRatio,
        double threshold = DefaultThreshold)
    {
        var score = Calculate(proppFunctionPhase, isObligatoryScene, valueChangeMagnitude, consequenceRatio);
        var isKernel = score > threshold;

        return new KernelClassification(
            Score: score,
            IsKernel: isKernel,
            Type: isKernel ? NarrativeUnitType.Kernel : NarrativeUnitType.Satellite);
    }
}

/// <summary>
/// Result of kernel classification.
/// </summary>
/// <param name="Score">The calculated kernel score (0-1).</param>
/// <param name="IsKernel">Whether the event is classified as a kernel.</param>
/// <param name="Type">The narrative unit type.</param>
public sealed record KernelClassification(
    double Score,
    bool IsKernel,
    NarrativeUnitType Type);

/// <summary>
/// Types of narrative units based on Barthes' classification.
/// </summary>
public enum NarrativeUnitType
{
    /// <summary>
    /// Essential event that cannot be removed without altering story logic.
    /// Represents branching points where choices occur.
    /// </summary>
    Kernel,

    /// <summary>
    /// Elaboration and texture that can be freely modified or removed.
    /// Embellishes kernels without changing the fundamental story.
    /// </summary>
    Satellite
}
