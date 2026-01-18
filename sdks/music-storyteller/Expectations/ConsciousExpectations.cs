using BeyondImmersion.Bannou.MusicTheory.Harmony;

namespace BeyondImmersion.Bannou.MusicStoryteller.Expectations;

/// <summary>
/// Conscious expectations - explicit predictions the listener is aware of.
/// Represents what the listener is actively anticipating.
/// Source: Huron, D. (2006). Sweet Anticipation, Chapter 12.
/// </summary>
public sealed class ConsciousExpectations
{
    /// <summary>
    /// Whether a cadence is consciously expected.
    /// </summary>
    public bool ExpectingCadence { get; set; }

    /// <summary>
    /// The type of cadence expected (if any).
    /// </summary>
    public CadenceType? ExpectedCadenceType { get; set; }

    /// <summary>
    /// Whether a theme/motif return is expected.
    /// </summary>
    public bool ExpectingThemeReturn { get; set; }

    /// <summary>
    /// Expected harmonic destination (chord degree).
    /// </summary>
    public int? ExpectedChordDegree { get; set; }

    /// <summary>
    /// Expected melodic resolution (scale degree).
    /// </summary>
    public int? ExpectedMelodicResolution { get; set; }

    /// <summary>
    /// Bars remaining until expected event.
    /// </summary>
    public int BarsUntilExpected { get; set; }

    /// <summary>
    /// Confidence in the expectation (0-1).
    /// </summary>
    public double ExpectationConfidence { get; set; }

    /// <summary>
    /// Whether expecting a section change.
    /// </summary>
    public bool ExpectingSectionChange { get; set; }

    /// <summary>
    /// Expected section (A, B, etc.).
    /// </summary>
    public char? ExpectedSection { get; set; }

    /// <summary>
    /// Whether expecting a climax.
    /// </summary>
    public bool ExpectingClimax { get; set; }

    /// <summary>
    /// Whether expecting resolution/relaxation.
    /// </summary>
    public bool ExpectingResolution { get; set; }

    /// <summary>
    /// Active prediction about what will happen next.
    /// </summary>
    public string? ActivePrediction { get; set; }

    /// <summary>
    /// Updates conscious expectations based on harmonic context.
    /// </summary>
    /// <param name="currentChordDegree">Current chord degree.</param>
    /// <param name="harmonicFunction">Current harmonic function.</param>
    /// <param name="barsIntoPhrase">Position in current phrase.</param>
    /// <param name="phraseLength">Expected phrase length.</param>
    public void UpdateFromHarmonicContext(
        int currentChordDegree,
        HarmonicFunctionType harmonicFunction,
        int barsIntoPhrase,
        int phraseLength)
    {
        // Dominant creates expectation of tonic
        if (harmonicFunction == HarmonicFunctionType.Dominant)
        {
            ExpectingCadence = true;
            ExpectedCadenceType = CadenceType.AuthenticPerfect;
            ExpectedChordDegree = 1; // Tonic
            BarsUntilExpected = 1;
            ExpectationConfidence = 0.7;
            ActivePrediction = "V → I cadence expected";
        }

        // Approaching phrase end creates cadence expectation
        if (barsIntoPhrase >= phraseLength - 2)
        {
            ExpectingCadence = true;
            BarsUntilExpected = phraseLength - barsIntoPhrase;

            if (harmonicFunction != HarmonicFunctionType.Dominant)
            {
                // Expect dominant then tonic
                ExpectedChordDegree = 5; // Dominant first
                ActivePrediction = "Approaching phrase end, cadence expected";
            }

            ExpectationConfidence = Math.Min(0.9, 0.5 + (barsIntoPhrase / (double)phraseLength) * 0.4);
        }

        // Subdominant near phrase end suggests plagal possibility
        if (harmonicFunction == HarmonicFunctionType.Subdominant && barsIntoPhrase >= phraseLength - 1)
        {
            ExpectedCadenceType = CadenceType.Plagal;
            ExpectationConfidence = 0.4; // Lower confidence for plagal
            ActivePrediction = "IV → I plagal cadence possible";
        }
    }

    /// <summary>
    /// Updates expectations based on tension level.
    /// </summary>
    /// <param name="currentTension">Current tension level (0-1).</param>
    /// <param name="tensionTrajectory">Whether tension is rising or falling.</param>
    public void UpdateFromTension(double currentTension, double tensionTrajectory)
    {
        // High tension creates expectation of resolution
        if (currentTension > 0.7)
        {
            ExpectingResolution = true;
            ExpectationConfidence = currentTension;
            ActivePrediction = "High tension, resolution expected";
        }
        else
        {
            ExpectingResolution = false;
        }

        // Rising tension trajectory suggests climax coming
        if (tensionTrajectory > 0.1 && currentTension > 0.5)
        {
            ExpectingClimax = true;
            // Estimate bars until climax based on rate of increase
            BarsUntilExpected = Math.Max(1, (int)((1.0 - currentTension) / tensionTrajectory * 4));
            ActivePrediction = $"Tension rising, climax in ~{BarsUntilExpected} bars";
        }
        else if (currentTension < 0.3)
        {
            ExpectingClimax = false;
        }
    }

    /// <summary>
    /// Updates expectations based on melodic context.
    /// </summary>
    /// <param name="currentDegree">Current scale degree.</param>
    /// <param name="isLeadingTone">Whether on leading tone.</param>
    public void UpdateFromMelodicContext(int currentDegree, bool isLeadingTone)
    {
        // Leading tone strongly expects tonic
        if (isLeadingTone || currentDegree == 7)
        {
            ExpectedMelodicResolution = 1; // Do
            ExpectationConfidence = 0.8;
            ActivePrediction = "Leading tone → tonic expected";
        }
        // 4 often moves to 3
        else if (currentDegree == 4)
        {
            ExpectedMelodicResolution = 3; // Mi
            ExpectationConfidence = 0.5;
            ActivePrediction = "Fa → Mi possible";
        }
        // 2 often moves to 1
        else if (currentDegree == 2)
        {
            ExpectedMelodicResolution = 1; // Do
            ExpectationConfidence = 0.4;
            ActivePrediction = "Re → Do possible";
        }
        else
        {
            ExpectedMelodicResolution = null;
        }
    }

    /// <summary>
    /// Updates expectations for theme return.
    /// </summary>
    /// <param name="barsSinceTheme">Bars since last theme appearance.</param>
    /// <param name="typicalInterval">Typical interval between theme appearances.</param>
    public void UpdateThemeExpectation(int barsSinceTheme, int typicalInterval)
    {
        if (typicalInterval > 0)
        {
            var progress = (double)barsSinceTheme / typicalInterval;

            if (progress > 0.8)
            {
                ExpectingThemeReturn = true;
                BarsUntilExpected = Math.Max(0, typicalInterval - barsSinceTheme);
                ExpectationConfidence = Math.Min(0.9, progress);
                ActivePrediction = $"Theme return in ~{BarsUntilExpected} bars";
            }
            else
            {
                ExpectingThemeReturn = false;
            }
        }
    }

    /// <summary>
    /// Registers that an expected event occurred.
    /// </summary>
    /// <param name="wasAccurate">Whether the prediction was accurate.</param>
    public void RegisterOutcome(bool wasAccurate)
    {
        if (wasAccurate)
        {
            // Prediction confirmed - expectations were correct
            ExpectationConfidence = Math.Min(1.0, ExpectationConfidence + 0.1);
        }
        else
        {
            // Prediction violated - reduce confidence
            ExpectationConfidence *= 0.7;
        }

        // Clear specific expectations
        ExpectingCadence = false;
        ExpectedCadenceType = null;
        ExpectedChordDegree = null;
        ExpectedMelodicResolution = null;
        ActivePrediction = null;
    }

    /// <summary>
    /// Clears all conscious expectations.
    /// </summary>
    public void Clear()
    {
        ExpectingCadence = false;
        ExpectedCadenceType = null;
        ExpectingThemeReturn = false;
        ExpectedChordDegree = null;
        ExpectedMelodicResolution = null;
        BarsUntilExpected = 0;
        ExpectationConfidence = 0;
        ExpectingSectionChange = false;
        ExpectedSection = null;
        ExpectingClimax = false;
        ExpectingResolution = false;
        ActivePrediction = null;
    }

    public override string ToString()
    {
        if (ActivePrediction != null)
        {
            return $"Conscious[{ActivePrediction}, conf={ExpectationConfidence:F2}]";
        }

        return "Conscious[no active prediction]";
    }
}
