using BeyondImmersion.Bannou.MusicStoryteller.Theory;
using BeyondImmersion.Bannou.MusicTheory.Harmony;

namespace BeyondImmersion.Bannou.MusicStoryteller.Expectations;

/// <summary>
/// Represents a musical event for expectation processing.
/// </summary>
public sealed class ExpectationEvent
{
    /// <summary>
    /// Type of event.
    /// </summary>
    public ExpectationEventType Type { get; init; }

    /// <summary>
    /// Interval (for melodic events).
    /// </summary>
    public int? Interval { get; init; }

    /// <summary>
    /// Scale degree (1-7).
    /// </summary>
    public int? ScaleDegree { get; init; }

    /// <summary>
    /// Chord degree (1-7).
    /// </summary>
    public int? ChordDegree { get; init; }

    /// <summary>
    /// Duration in beats.
    /// </summary>
    public double? Duration { get; init; }

    /// <summary>
    /// Current bar number.
    /// </summary>
    public int Bar { get; init; }
}

/// <summary>
/// Types of expectation events.
/// </summary>
public enum ExpectationEventType
{
    /// <summary>Melodic note event.</summary>
    Melodic,

    /// <summary>Harmonic chord change.</summary>
    Harmonic,

    /// <summary>Rhythmic event.</summary>
    Rhythmic,

    /// <summary>Cadence event.</summary>
    Cadence,

    /// <summary>Theme appearance.</summary>
    Theme,

    /// <summary>Section change.</summary>
    Section
}

/// <summary>
/// Unified expectation tracker combining all four expectation types.
/// Provides a single interface for managing listener expectations.
/// </summary>
public sealed class ExpectationTracker
{
    /// <summary>
    /// Schematic expectations (style-based).
    /// </summary>
    public SchematicExpectations Schematic { get; }

    /// <summary>
    /// Veridical expectations (piece-specific).
    /// </summary>
    public VeridicalExpectations Veridical { get; } = new();

    /// <summary>
    /// Dynamic expectations (short-term).
    /// </summary>
    public DynamicExpectations Dynamic { get; } = new();

    /// <summary>
    /// Conscious expectations (explicit predictions).
    /// </summary>
    public ConsciousExpectations Conscious { get; } = new();

    /// <summary>
    /// Creates an expectation tracker with default style.
    /// </summary>
    public ExpectationTracker()
    {
        Schematic = new SchematicExpectations();
    }

    /// <summary>
    /// Creates an expectation tracker for a specific style.
    /// </summary>
    /// <param name="styleId">The style identifier.</param>
    public ExpectationTracker(string styleId)
    {
        Schematic = new SchematicExpectations(styleId);
    }

    /// <summary>
    /// Processes a musical event and updates all expectation types.
    /// </summary>
    /// <param name="event">The event to process.</param>
    /// <returns>The combined expectation response.</returns>
    public ExpectationResponse ProcessEvent(ExpectationEvent @event)
    {
        var response = new ExpectationResponse
        {
            Bar = @event.Bar
        };

        switch (@event.Type)
        {
            case ExpectationEventType.Melodic:
                ProcessMelodicEvent(@event, response);
                break;

            case ExpectationEventType.Harmonic:
                ProcessHarmonicEvent(@event, response);
                break;

            case ExpectationEventType.Cadence:
                ProcessCadenceEvent(@event, response);
                break;

            case ExpectationEventType.Theme:
                ProcessThemeEvent(@event, response);
                break;
        }

        // Update conscious expectations
        UpdateConsciousExpectations(@event);

        return response;
    }

    /// <summary>
    /// Gets the combined probability for an event.
    /// Weighs schematic, veridical, and dynamic expectations.
    /// </summary>
    /// <param name="interval">The melodic interval.</param>
    /// <param name="currentDegree">Current scale degree.</param>
    /// <returns>Combined probability (0-1).</returns>
    public double GetCombinedMelodicProbability(int interval, int currentDegree)
    {
        // Weights for combining expectation types
        const double schematicWeight = 0.4;
        const double veridicalWeight = 0.3;
        const double dynamicWeight = 0.3;

        // Get schematic probability
        var schematicProb = Schematic.GetIntervalProbability(interval);

        // Get veridical probability (from pattern matching)
        var veridicalExpectation = Veridical.GetNextExpectation([interval], PatternType.Melodic);
        var veridicalProb = veridicalExpectation.HasValue
            ? (veridicalExpectation.Value.expected == interval ? veridicalExpectation.Value.confidence : 0.1)
            : schematicProb;

        // Get dynamic probability (from recent patterns)
        var dynamicProbs = Dynamic.GetIntervalTransitionProbabilities(currentDegree);
        var dynamicProb = dynamicProbs.GetValueOrDefault(interval, schematicProb);

        // Combine with weights
        return schematicWeight * schematicProb +
               veridicalWeight * veridicalProb +
               dynamicWeight * dynamicProb;
    }

    /// <summary>
    /// Gets the combined probability for a harmonic event.
    /// </summary>
    /// <param name="fromDegree">Previous chord degree.</param>
    /// <param name="toDegree">Current chord degree.</param>
    /// <returns>Combined probability (0-1).</returns>
    public double GetCombinedHarmonicProbability(int fromDegree, int toDegree)
    {
        const double schematicWeight = 0.5;
        const double dynamicWeight = 0.5;

        var schematicProb = Schematic.GetHarmonicTransitionProbability(fromDegree, toDegree);

        // Check dynamic history
        var dynamicProbs = new Dictionary<int, double>();
        foreach (var chord in Dynamic.RecentChords)
        {
            dynamicProbs[chord] = dynamicProbs.GetValueOrDefault(chord, 0) + 1;
        }

        var total = dynamicProbs.Values.Sum();
        var dynamicProb = total > 0 ? dynamicProbs.GetValueOrDefault(toDegree, 0) / total : schematicProb;

        return schematicWeight * schematicProb + dynamicWeight * dynamicProb;
    }

    /// <summary>
    /// Gets the information content (surprise) for an event.
    /// </summary>
    /// <param name="event">The event.</param>
    /// <returns>Information content in bits.</returns>
    public double GetInformationContent(ExpectationEvent @event)
    {
        double probability;

        if (@event.Type == ExpectationEventType.Melodic && @event.Interval.HasValue)
        {
            probability = GetCombinedMelodicProbability(
                @event.Interval.Value,
                @event.ScaleDegree ?? 1);
        }
        else if (@event.Type == ExpectationEventType.Harmonic && @event.ChordDegree.HasValue)
        {
            var previousChord = Dynamic.RecentChords.LastOrDefault();
            probability = GetCombinedHarmonicProbability(
                previousChord != 0 ? previousChord : 1,
                @event.ChordDegree.Value);
        }
        else
        {
            probability = 0.2; // Default moderate probability
        }

        return InformationContent.Calculate(probability);
    }

    /// <summary>
    /// Updates expectations based on current musical context.
    /// </summary>
    /// <param name="harmonicFunction">Current harmonic function.</param>
    /// <param name="barsIntoPhrase">Position in phrase.</param>
    /// <param name="phraseLength">Phrase length.</param>
    /// <param name="currentTension">Current tension level.</param>
    public void UpdateContext(
        HarmonicFunctionType harmonicFunction,
        int barsIntoPhrase,
        int phraseLength,
        double currentTension)
    {
        // Update conscious expectations from context
        var currentChordDegree = Dynamic.RecentChords.LastOrDefault();
        if (currentChordDegree == 0) currentChordDegree = 1;

        Conscious.UpdateFromHarmonicContext(
            currentChordDegree,
            harmonicFunction,
            barsIntoPhrase,
            phraseLength);

        // Estimate tension trajectory from recent history
        var trajectory = 0.0; // Would calculate from tension history
        Conscious.UpdateFromTension(currentTension, trajectory);
    }

    private void ProcessMelodicEvent(ExpectationEvent @event, ExpectationResponse response)
    {
        if (!@event.Interval.HasValue) return;

        var interval = @event.Interval.Value;

        // Get expected probability
        var probability = GetCombinedMelodicProbability(interval, @event.ScaleDegree ?? 1);
        response.Probability = probability;
        response.InformationContent = InformationContent.Calculate(probability);

        // Calculate surprise from dynamic expectations
        response.Surprise = Dynamic.CalculateSurprise(interval);

        // Update dynamic tracking
        Dynamic.AddInterval(interval);
        if (@event.ScaleDegree.HasValue)
        {
            Dynamic.AddDegree(@event.ScaleDegree.Value);
        }

        // Update schematic learning (subtle)
        if (@event.ScaleDegree.HasValue)
        {
            var prevDegree = Dynamic.RecentDegrees.Count > 1
                ? Dynamic.RecentDegrees[^2]
                : 1;
            Schematic.UpdateFromObservation(prevDegree, @event.ScaleDegree.Value, 0.02);
        }
    }

    private void ProcessHarmonicEvent(ExpectationEvent @event, ExpectationResponse response)
    {
        if (!@event.ChordDegree.HasValue) return;

        var chordDegree = @event.ChordDegree.Value;
        var previousChord = Dynamic.RecentChords.LastOrDefault();
        if (previousChord == 0) previousChord = 1;

        // Get expected probability
        var probability = GetCombinedHarmonicProbability(previousChord, chordDegree);
        response.Probability = probability;
        response.InformationContent = InformationContent.Calculate(probability);

        // Surprise based on schematic expectations
        var schematicProb = Schematic.GetHarmonicTransitionProbability(previousChord, chordDegree);
        response.Surprise = 1.0 - schematicProb;

        // Update dynamic tracking
        Dynamic.AddChord(chordDegree);

        // Check if conscious expectation was met
        if (Conscious.ExpectedChordDegree.HasValue)
        {
            response.ExpectationMet = Conscious.ExpectedChordDegree.Value == chordDegree;
            Conscious.RegisterOutcome(response.ExpectationMet ?? false);
        }
    }

    private void ProcessCadenceEvent(ExpectationEvent @event, ExpectationResponse response)
    {
        response.ExpectationMet = Conscious.ExpectingCadence;

        if (Conscious.ExpectingCadence)
        {
            // Expected cadence occurred - low surprise, high satisfaction
            response.Surprise = 0.2;
            response.InformationContent = 1.0;
        }
        else
        {
            // Unexpected cadence - higher surprise
            response.Surprise = 0.6;
            response.InformationContent = 3.0;
        }

        Conscious.RegisterOutcome(true);
    }

    private void ProcessThemeEvent(ExpectationEvent @event, ExpectationResponse response)
    {
        // Record in veridical memory
        Veridical.RecordMainThemeAppearance(@event.Bar);

        response.ExpectationMet = Conscious.ExpectingThemeReturn;

        if (Conscious.ExpectingThemeReturn)
        {
            // Expected theme return - satisfying
            response.Surprise = 0.2;
            response.InformationContent = 1.5;
        }
        else if (Veridical.MainThemeAppearances.Count > 1)
        {
            // Unexpected theme return - recognition surprise
            response.Surprise = 0.4;
            response.InformationContent = 2.0;
        }

        // Update theme return expectations
        var avgInterval = Veridical.MainThemeAppearances.Count > 1
            ? Veridical.MainThemeAppearances.Zip(Veridical.MainThemeAppearances.Skip(1),
                (a, b) => b - a).Average()
            : 16;
        Conscious.UpdateThemeExpectation(0, (int)avgInterval);
    }

    private void UpdateConsciousExpectations(ExpectationEvent @event)
    {
        // Decrement bars until expected
        if (Conscious.BarsUntilExpected > 0)
        {
            Conscious.BarsUntilExpected--;
        }

        // Update melodic expectations
        if (@event.ScaleDegree.HasValue)
        {
            var isLeadingTone = @event.ScaleDegree.Value == 7;
            Conscious.UpdateFromMelodicContext(@event.ScaleDegree.Value, isLeadingTone);
        }
    }
}

/// <summary>
/// Response from processing an expectation event.
/// </summary>
public sealed class ExpectationResponse
{
    /// <summary>
    /// Bar number of the event.
    /// </summary>
    public int Bar { get; init; }

    /// <summary>
    /// Combined probability of the event.
    /// </summary>
    public double Probability { get; set; }

    /// <summary>
    /// Information content (surprise) in bits.
    /// </summary>
    public double InformationContent { get; set; }

    /// <summary>
    /// Normalized surprise level (0-1).
    /// </summary>
    public double Surprise { get; set; }

    /// <summary>
    /// Whether a conscious expectation was met.
    /// </summary>
    public bool? ExpectationMet { get; set; }

    public override string ToString()
    {
        return $"ExpResp[P={Probability:F2}, IC={InformationContent:F1} bits, Surprise={Surprise:F2}]";
    }
}
