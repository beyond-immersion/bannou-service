namespace BeyondImmersion.Bannou.MusicStoryteller.Theory;

/// <summary>
/// Calculates contrastive valence based on Huron's ITPRA theory.
/// Final_affect ≈ Appraisal + |Reaction|
/// Resolution pleasure scales with prior tension.
/// Source: Huron, D. (2006). Sweet Anticipation, Chapter 2.
/// </summary>
public static class ContrastiveValence
{
    /// <summary>
    /// Calculates the pleasure from resolution based on prior tension.
    /// Resolution after high tension produces more pleasure than after low tension.
    /// </summary>
    /// <param name="priorTension">Tension level before resolution (0-1).</param>
    /// <param name="resolutionCompleteness">How complete the resolution is (0-1).</param>
    /// <returns>Pleasure value (0-1).</returns>
    public static double CalculateResolutionPleasure(double priorTension, double resolutionCompleteness)
    {
        // The reaction magnitude scales with prior tension
        // Higher prior tension = stronger relief reaction
        var reactionMagnitude = priorTension * resolutionCompleteness;

        // Appraisal is positive when context is safe (music is art, not danger)
        // This reframes the initially negative tension as positive excitement
        const double appraisalBonus = 0.5;

        // Final affect = appraisal + |reaction|
        return Math.Clamp(appraisalBonus + reactionMagnitude * 0.5, 0.0, 1.0);
    }

    /// <summary>
    /// Calculates the contrast effect between expectation and outcome.
    /// When outcome is better than expected = positive contrast.
    /// When outcome is worse than expected = negative contrast.
    /// </summary>
    /// <param name="expectedPleasure">Expected pleasure level (0-1).</param>
    /// <param name="actualPleasure">Actual pleasure level (0-1).</param>
    /// <returns>Contrast value (-1 to 1).</returns>
    public static double CalculateContrast(double expectedPleasure, double actualPleasure)
    {
        return actualPleasure - expectedPleasure;
    }

    /// <summary>
    /// Calculates the ITPRA response sequence for a musical event.
    /// </summary>
    /// <param name="imaginationTension">Tension from anticipation/imagination.</param>
    /// <param name="tensionResponse">Tension response to the event itself.</param>
    /// <param name="predictionAccuracy">How well the outcome matched prediction (0-1).</param>
    /// <param name="reactionIntensity">Intensity of the visceral reaction.</param>
    /// <param name="appraisalPositivity">Cognitive appraisal positivity (0-1).</param>
    /// <returns>Final affective response.</returns>
    public static ItpraResponse CalculateItpra(
        double imaginationTension,
        double tensionResponse,
        double predictionAccuracy,
        double reactionIntensity,
        double appraisalPositivity)
    {
        // I: Imagination response (anticipatory)
        // Tension from thinking about upcoming events
        var imagination = imaginationTension * 0.5;

        // T: Tension response (immediate)
        // Physiological arousal from the event
        var tension = tensionResponse;

        // P: Prediction response
        // Pleasure from accurate prediction, displeasure from violated expectation
        // Accurate predictions feel good (familiarity)
        var prediction = (predictionAccuracy - 0.5) * 0.8;

        // R: Reaction response (visceral)
        // Automatic emotional reaction to the event
        var reaction = reactionIntensity * (appraisalPositivity > 0.5 ? 1 : -1);

        // A: Appraisal response (cognitive)
        // Conscious evaluation reframes the experience
        var appraisal = (appraisalPositivity - 0.5) * 2;

        // Calculate final affect with contrastive valence
        // Key insight: negative anticipation can enhance positive resolution
        var contrastive = CalculateContrastiveEffect(tension, reaction, appraisal);

        return new ItpraResponse
        {
            Imagination = imagination,
            Tension = tension,
            Prediction = prediction,
            Reaction = reaction,
            Appraisal = appraisal,
            Contrastive = contrastive,
            FinalAffect = imagination * 0.1 + prediction * 0.2 + contrastive * 0.7
        };
    }

    /// <summary>
    /// Calculates the contrastive effect between tension and resolution.
    /// </summary>
    private static double CalculateContrastiveEffect(
        double tension, double reaction, double appraisal)
    {
        // If tension was high and appraisal is positive,
        // the relief amplifies the positive feeling
        if (tension > 0.5 && appraisal > 0)
        {
            // Contrastive valence: relief after tension = enhanced pleasure
            return appraisal * (1 + tension * 0.5);
        }

        // Normal case: appraisal + reaction
        return appraisal + Math.Abs(reaction) * Math.Sign(appraisal);
    }

    /// <summary>
    /// Calculates expected pleasure for a cadence type.
    /// </summary>
    /// <param name="cadenceType">The type of cadence.</param>
    /// <param name="priorTension">Tension before the cadence.</param>
    /// <returns>Expected pleasure value (0-1).</returns>
    public static double CalculateCadencePleasure(CadenceResponse cadenceType, double priorTension)
    {
        var baseResolution = cadenceType switch
        {
            CadenceResponse.AuthenticPerfect => 1.0,
            CadenceResponse.AuthenticImperfect => 0.8,
            CadenceResponse.Plagal => 0.7,
            CadenceResponse.Half => 0.3,  // Tension maintained
            CadenceResponse.Deceptive => 0.4,  // Surprise, but no resolution
            _ => 0.5
        };

        return CalculateResolutionPleasure(priorTension, baseResolution);
    }

    /// <summary>
    /// Determines the optimal timing for resolution based on contrastive valence.
    /// </summary>
    /// <param name="currentTension">Current tension level.</param>
    /// <param name="targetPleasure">Desired pleasure from resolution.</param>
    /// <returns>Whether resolution should occur now.</returns>
    public static bool ShouldResolveNow(double currentTension, double targetPleasure)
    {
        // Calculate pleasure if we resolve now
        var pleasureIfResolve = CalculateResolutionPleasure(currentTension, 1.0);

        // If we would exceed target pleasure, resolve
        // If tension is too low to reach target, build more tension first
        return pleasureIfResolve >= targetPleasure;
    }

    /// <summary>
    /// Calculates the optimal tension level to build before resolution.
    /// </summary>
    /// <param name="targetPleasure">Desired pleasure from resolution.</param>
    /// <returns>Target tension level to build.</returns>
    public static double CalculateTargetTension(double targetPleasure)
    {
        // Invert the resolution pleasure formula
        // pleasure = 0.5 + priorTension * 0.5
        // priorTension = (pleasure - 0.5) / 0.5 = 2 * pleasure - 1
        return Math.Clamp(2 * targetPleasure - 1, 0.0, 1.0);
    }
}

/// <summary>
/// Response from ITPRA calculation.
/// </summary>
public sealed class ItpraResponse
{
    /// <summary>Imagination response (anticipatory tension).</summary>
    public double Imagination { get; init; }

    /// <summary>Tension response (arousal).</summary>
    public double Tension { get; init; }

    /// <summary>Prediction response (expectation match).</summary>
    public double Prediction { get; init; }

    /// <summary>Reaction response (visceral).</summary>
    public double Reaction { get; init; }

    /// <summary>Appraisal response (cognitive evaluation).</summary>
    public double Appraisal { get; init; }

    /// <summary>Contrastive valence effect.</summary>
    public double Contrastive { get; init; }

    /// <summary>Final affective response.</summary>
    public double FinalAffect { get; init; }

    public override string ToString()
    {
        return $"ITPRA[I={Imagination:F2}, T={Tension:F2}, P={Prediction:F2}, R={Reaction:F2}, A={Appraisal:F2}] → {FinalAffect:F2}";
    }
}

/// <summary>
/// Cadence types for pleasure calculation.
/// </summary>
public enum CadenceResponse
{
    /// <summary>V-I with root position chords.</summary>
    AuthenticPerfect,

    /// <summary>V-I with inversions or different soprano.</summary>
    AuthenticImperfect,

    /// <summary>IV-I (amen cadence).</summary>
    Plagal,

    /// <summary>Ending on V (tension maintained).</summary>
    Half,

    /// <summary>V-vi (surprise, no resolution).</summary>
    Deceptive
}
