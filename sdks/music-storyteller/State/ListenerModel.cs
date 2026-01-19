namespace BeyondImmersion.Bannou.MusicStoryteller.State;

/// <summary>
/// Models the listener's cognitive state based on Huron's ITPRA theory.
/// Tracks expectations, attention, and engagement.
/// </summary>
public sealed class ListenerModel
{
    /// <summary>
    /// Current attention level (0-1).
    /// Attention naturally decays and is renewed by surprising events.
    /// </summary>
    public double Attention { get; set; } = 0.8;

    /// <summary>
    /// Current surprise budget - how much unexpected content is acceptable.
    /// High values allow more surprising events; low values need more predictability.
    /// </summary>
    public double SurpriseBudget { get; set; } = 0.5;

    /// <summary>
    /// Whether tension has been created that expects resolution.
    /// </summary>
    public bool ExpectedResolution { get; set; }

    /// <summary>
    /// Recent prediction accuracy (rolling average).
    /// High values mean the listener is successfully predicting events.
    /// </summary>
    public double PredictionAccuracy { get; set; } = 0.7;

    /// <summary>
    /// Recent surprise accumulation (for contrast effects).
    /// </summary>
    public double RecentSurprises { get; set; }

    /// <summary>
    /// Number of musical events processed.
    /// </summary>
    public int EventsProcessed { get; set; }

    /// <summary>
    /// Accumulated pleasure from contrastive valence (resolution after tension).
    /// </summary>
    public double AccumulatedPleasure { get; set; }

    /// <summary>
    /// Current engagement level (derived from attention and prediction accuracy).
    /// </summary>
    public double Engagement => (Attention + PredictionAccuracy) / 2.0;

    /// <summary>
    /// Whether the listener is in a state of heightened anticipation.
    /// </summary>
    public bool IsAnticipating => ExpectedResolution && Attention > 0.6;

    /// <summary>
    /// Schematic expectations - style-based probabilities (semantic memory).
    /// </summary>
    public SchematicExpectations Schematic { get; } = new();

    /// <summary>
    /// Veridical expectations - piece-specific memory (episodic memory).
    /// </summary>
    public VeridicalExpectations Veridical { get; } = new();

    /// <summary>
    /// Dynamic expectations - work-in-progress patterns (short-term memory).
    /// </summary>
    public DynamicExpectations Dynamic { get; } = new();

    /// <summary>
    /// Conscious expectations - explicit predictions.
    /// </summary>
    public ConsciousExpectations Conscious { get; } = new();

    /// <summary>
    /// Processes a musical event and updates the listener model.
    /// </summary>
    /// <param name="surprise">The surprise value of the event (0-1).</param>
    /// <param name="wasResolution">Whether this was a resolution event.</param>
    /// <param name="tensionBefore">The tension level before the event.</param>
    public void ProcessEvent(double surprise, bool wasResolution, double tensionBefore)
    {
        EventsProcessed++;

        // Update prediction accuracy (rolling average)
        var accuracy = 1.0 - surprise;
        PredictionAccuracy = PredictionAccuracy * 0.9 + accuracy * 0.1;

        // Attention: boosted by surprise, decays with predictability
        if (surprise > 0.5)
        {
            Attention = Math.Min(1.0, Attention + surprise * 0.2);
            RecentSurprises += surprise;
        }
        else
        {
            Attention = Math.Max(0.3, Attention - 0.02);
        }

        // Decay recent surprises over time
        RecentSurprises *= 0.9;

        // Handle resolution (contrastive valence)
        if (wasResolution && ExpectedResolution)
        {
            // Pleasure from resolution scales with prior tension (Huron's contrastive valence)
            var pleasure = tensionBefore * 0.8;
            AccumulatedPleasure += pleasure;
            ExpectedResolution = false;

            // Resolution also reduces surprise budget need
            SurpriseBudget = Math.Max(0.3, SurpriseBudget - 0.1);
        }

        // Update surprise budget based on recent history
        if (RecentSurprises > 1.5)
        {
            // Too many surprises - reduce budget
            SurpriseBudget = Math.Max(0.2, SurpriseBudget - 0.1);
        }
        else if (RecentSurprises < 0.3 && EventsProcessed > 10)
        {
            // Too predictable - can handle more surprise
            SurpriseBudget = Math.Min(0.8, SurpriseBudget + 0.05);
        }
    }

    /// <summary>
    /// Registers that tension has been created and resolution is expected.
    /// </summary>
    public void RegisterTensionEvent()
    {
        ExpectedResolution = true;
        Attention = Math.Min(1.0, Attention + 0.1);
    }

    /// <summary>
    /// Gets the recommended surprise level for the next event.
    /// </summary>
    /// <returns>Recommended surprise value (0-1).</returns>
    public double GetRecommendedSurprise()
    {
        // Base on surprise budget and attention state
        if (Attention < 0.5)
        {
            // Low attention - need more surprise to re-engage
            return Math.Min(0.8, SurpriseBudget + 0.2);
        }

        if (ExpectedResolution)
        {
            // Expecting resolution - low surprise preferred
            return 0.2;
        }

        // Normal operation - use surprise budget
        return SurpriseBudget;
    }

    /// <summary>
    /// Creates a deep copy of this listener model.
    /// </summary>
    public ListenerModel Clone()
    {
        var clone = new ListenerModel
        {
            Attention = Attention,
            SurpriseBudget = SurpriseBudget,
            ExpectedResolution = ExpectedResolution,
            PredictionAccuracy = PredictionAccuracy,
            RecentSurprises = RecentSurprises,
            EventsProcessed = EventsProcessed,
            AccumulatedPleasure = AccumulatedPleasure
        };
        // Note: Expectation sub-objects would need their own Clone methods
        // For simplicity, we're not deep-cloning those here
        return clone;
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return $"Listener[Attn={Attention:F2}, Surprise={SurpriseBudget:F2}, Accuracy={PredictionAccuracy:F2}]";
    }
}

/// <summary>
/// Schematic expectations based on style/genre conventions.
/// Represents semantic memory - what the listener knows about this type of music.
/// </summary>
public sealed class SchematicExpectations
{
    /// <summary>
    /// Expected probability of authentic cadence at phrase end.
    /// </summary>
    public double AuthenticCadenceProbability { get; set; } = 0.7;

    /// <summary>
    /// Expected probability of melodic resolution to tonic.
    /// </summary>
    public double TonicResolutionProbability { get; set; } = 0.8;

    /// <summary>
    /// Expected harmonic rhythm (chords per bar).
    /// </summary>
    public double ExpectedHarmonicRhythm { get; set; } = 1.0;

    /// <summary>
    /// Expected phrase length in bars.
    /// </summary>
    public int ExpectedPhraseLength { get; set; } = 4;

    /// <summary>
    /// Updates expectations based on observed style patterns.
    /// </summary>
    /// <param name="styleId">The style identifier.</param>
    public void LoadStyleDefaults(string styleId)
    {
        // Could be extended to load from style definitions
        AuthenticCadenceProbability = styleId.ToLowerInvariant() switch
        {
            "celtic" => 0.8,
            "jazz" => 0.6,
            "baroque" => 0.75,
            _ => 0.7
        };
    }
}

/// <summary>
/// Veridical expectations based on specific piece memory.
/// Represents episodic memory - what has happened in this piece.
/// </summary>
public sealed class VeridicalExpectations
{
    /// <summary>
    /// Recognized patterns from this piece (motif returns, harmonic patterns).
    /// </summary>
    public List<string> RecognizedPatterns { get; } = [];

    /// <summary>
    /// Bar numbers where the main theme appeared.
    /// </summary>
    public List<int> MainThemeAppearances { get; } = [];

    /// <summary>
    /// Whether the form structure has been recognized.
    /// </summary>
    public bool FormRecognized { get; set; }

    /// <summary>
    /// Records a pattern recognition event.
    /// </summary>
    /// <param name="patternId">The pattern identifier.</param>
    public void RecordPattern(string patternId)
    {
        if (!RecognizedPatterns.Contains(patternId))
        {
            RecognizedPatterns.Add(patternId);
        }
    }
}

/// <summary>
/// Dynamic expectations based on very recent patterns.
/// Represents short-term memory - the last 10-25 events.
/// </summary>
public sealed class DynamicExpectations
{
    /// <summary>
    /// Recent intervals for pattern detection.
    /// </summary>
    public List<int> RecentIntervals { get; } = new(25);

    /// <summary>
    /// Recent rhythmic values.
    /// </summary>
    public List<double> RecentRhythms { get; } = new(25);

    /// <summary>
    /// Whether a repeating pattern has been detected.
    /// </summary>
    public bool PatternDetected { get; set; }

    /// <summary>
    /// The detected pattern length (if any).
    /// </summary>
    public int PatternLength { get; set; }

    /// <summary>
    /// Adds an interval to the recent history.
    /// </summary>
    /// <param name="semitones">The interval in semitones.</param>
    public void AddInterval(int semitones)
    {
        RecentIntervals.Add(semitones);
        if (RecentIntervals.Count > 25)
        {
            RecentIntervals.RemoveAt(0);
        }
        DetectPattern();
    }

    private void DetectPattern()
    {
        // Simple pattern detection - look for repeating sequences
        if (RecentIntervals.Count < 6)
        {
            PatternDetected = false;
            return;
        }

        // Check for patterns of length 2-4
        for (var len = 2; len <= 4; len++)
        {
            if (RecentIntervals.Count >= len * 2)
            {
                var isPattern = true;
                for (var i = 0; i < len; i++)
                {
                    if (RecentIntervals[^(len + 1 + i)] != RecentIntervals[^(1 + i)])
                    {
                        isPattern = false;
                        break;
                    }
                }
                if (isPattern)
                {
                    PatternDetected = true;
                    PatternLength = len;
                    return;
                }
            }
        }

        PatternDetected = false;
    }
}

/// <summary>
/// Conscious expectations - explicit predictions.
/// Represents what the listener is actively anticipating.
/// </summary>
public sealed class ConsciousExpectations
{
    /// <summary>
    /// Whether a cadence is consciously expected.
    /// </summary>
    public bool ExpectingCadence { get; set; }

    /// <summary>
    /// Whether theme return is expected.
    /// </summary>
    public bool ExpectingThemeReturn { get; set; }

    /// <summary>
    /// Expected harmonic destination (if any).
    /// </summary>
    public string? ExpectedHarmony { get; set; }

    /// <summary>
    /// Bars until expected event.
    /// </summary>
    public int BarsUntilExpected { get; set; }

    /// <summary>
    /// Updates expectations based on musical context.
    /// </summary>
    /// <param name="isOnDominant">Whether currently on a dominant chord.</param>
    /// <param name="barsIntoPhrase">Current position in phrase.</param>
    /// <param name="phraseLength">Expected phrase length.</param>
    public void Update(bool isOnDominant, int barsIntoPhrase, int phraseLength)
    {
        if (isOnDominant)
        {
            ExpectingCadence = true;
            ExpectedHarmony = "I";
            BarsUntilExpected = 1;
        }

        if (barsIntoPhrase >= phraseLength - 2)
        {
            ExpectingCadence = true;
            BarsUntilExpected = phraseLength - barsIntoPhrase;
        }
    }
}
