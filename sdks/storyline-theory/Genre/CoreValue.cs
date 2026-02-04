namespace BeyondImmersion.Bannou.StorylineTheory.Genre;

/// <summary>
/// Core values at stake in Story Grid genres.
/// Each genre centers on a specific value spectrum that drives the narrative.
/// </summary>
public enum CoreValue
{
    /// <summary>Life vs Death - Action, Thriller, Horror genres.</summary>
    Life,

    /// <summary>Justice vs Injustice - Crime, Legal genres.</summary>
    Justice,

    /// <summary>Love vs Hate - Romance, Buddy Love genres.</summary>
    Love,

    /// <summary>Success vs Failure - Status, Performance genres.</summary>
    Success,

    /// <summary>Sophistication vs Naivete - Worldview (Coming of Age) genre.</summary>
    Worldview,

    /// <summary>Altruism vs Selfishness - Morality genre.</summary>
    Morality,

    /// <summary>Honor vs Dishonor - War genre.</summary>
    Honor,

    /// <summary>Freedom vs Subjugation - Society genre.</summary>
    Freedom,

    /// <summary>Truth vs Deception - Mystery genre.</summary>
    Truth
}

/// <summary>
/// Represents a value spectrum with positive, negative, and negation-of-negation states.
/// </summary>
/// <param name="Positive">The positive end of the spectrum (e.g., "Life").</param>
/// <param name="Negative">The negative end (e.g., "Death").</param>
/// <param name="NegationOfNegation">The fate worse than negative (e.g., "Damnation").</param>
/// <remarks>
/// Story Grid's value spectrum includes a "negation of the negation" - a state worse
/// than the simple negative. For example, in Thriller:
/// - Positive: Life
/// - Negative: Death
/// - Negation of Negation: Damnation (a fate worse than death)
///
/// The negation of negation represents the ultimate stakes and often appears
/// at the "All Is Lost" moment.
/// </remarks>
public sealed record ValueSpectrum(
    string Positive,
    string Negative,
    string? NegationOfNegation = null)
{
    /// <summary>
    /// Converts a value position (-1 to +1) to a spectrum position.
    /// -1.0 = Negation of Negation, 0.0 = Negative, +1.0 = Positive
    /// </summary>
    public double NormalizePosition(double value) => Math.Clamp(value, -1.0, 1.0);

    /// <summary>
    /// Gets the label for a value position on the spectrum.
    /// </summary>
    public string GetLabel(double position) => position switch
    {
        >= 0.5 => Positive,
        <= -0.5 when NegationOfNegation != null => NegationOfNegation,
        _ => Negative
    };

    /// <summary>
    /// Calculates the range covered between two positions.
    /// </summary>
    /// <param name="minPosition">Lowest point reached.</param>
    /// <param name="maxPosition">Highest point reached.</param>
    /// <returns>Range as a fraction of the full spectrum (0-1).</returns>
    public double CalculateRangeCovered(double minPosition, double maxPosition)
    {
        var fullRange = NegationOfNegation != null ? 2.0 : 1.0; // -1 to +1 or 0 to +1
        var coveredRange = maxPosition - minPosition;
        return Math.Clamp(coveredRange / fullRange, 0.0, 1.0);
    }
}
