using BeyondImmersion.Bannou.MusicTheory.Collections;
using BeyondImmersion.Bannou.MusicTheory.Harmony;
using BeyondImmersion.Bannou.MusicTheory.Pitch;

namespace BeyondImmersion.Bannou.MusicStoryteller.Expectations;

/// <summary>
/// Schematic expectations based on style/genre conventions.
/// Represents semantic memory - general knowledge about how this type of music typically behaves.
/// Source: Huron, D. (2006). Sweet Anticipation, Chapter 12.
/// </summary>
public sealed class SchematicExpectations
{
    private readonly string _styleId;

    /// <summary>
    /// Probability distribution for melodic intervals.
    /// Key = interval in semitones, Value = probability.
    /// </summary>
    public Dictionary<int, double> IntervalProbabilities { get; } = new();

    /// <summary>
    /// Probability distribution for scale degree transitions.
    /// Key = (fromDegree, toDegree), Value = probability.
    /// </summary>
    public Dictionary<(int from, int to), double> DegreeTransitions { get; } = new();

    /// <summary>
    /// Probability distribution for harmonic progressions.
    /// Key = (fromDegree, toDegree), Value = probability.
    /// </summary>
    public Dictionary<(int from, int to), double> HarmonicTransitions { get; } = new();

    /// <summary>
    /// Expected cadence probabilities at phrase boundaries.
    /// </summary>
    public Dictionary<CadenceType, double> CadenceProbabilities { get; } = new();

    /// <summary>
    /// Expected phrase length distribution.
    /// </summary>
    public Dictionary<int, double> PhraseLengthProbabilities { get; } = new();

    /// <summary>
    /// Expected harmonic rhythm (chords per bar).
    /// </summary>
    public double ExpectedHarmonicRhythm { get; set; } = 1.0;

    /// <summary>
    /// Expected mode distribution.
    /// </summary>
    public Dictionary<ModeType, double> ModeProbabilities { get; } = new();

    /// <summary>
    /// Creates schematic expectations with default distributions.
    /// </summary>
    public SchematicExpectations()
    {
        _styleId = "default";
        LoadDefaultDistributions();
    }

    /// <summary>
    /// Creates schematic expectations for a specific style.
    /// </summary>
    /// <param name="styleId">The style identifier.</param>
    public SchematicExpectations(string styleId)
    {
        _styleId = styleId;
        LoadDefaultDistributions();
        LoadStyleOverrides(styleId);
    }

    /// <summary>
    /// Gets the expected probability for an interval.
    /// </summary>
    /// <param name="semitones">The interval in semitones.</param>
    /// <returns>Probability (0-1).</returns>
    public double GetIntervalProbability(int semitones)
    {
        return IntervalProbabilities.GetValueOrDefault(semitones, 0.01);
    }

    /// <summary>
    /// Gets the expected probability for a scale degree transition.
    /// </summary>
    /// <param name="fromDegree">Source scale degree (1-7).</param>
    /// <param name="toDegree">Target scale degree (1-7).</param>
    /// <returns>Probability (0-1).</returns>
    public double GetDegreeTransitionProbability(int fromDegree, int toDegree)
    {
        return DegreeTransitions.GetValueOrDefault((fromDegree, toDegree), 0.05);
    }

    /// <summary>
    /// Gets the expected probability for a chord progression step.
    /// </summary>
    /// <param name="fromDegree">Source chord degree (1-7).</param>
    /// <param name="toDegree">Target chord degree (1-7).</param>
    /// <returns>Probability (0-1).</returns>
    public double GetHarmonicTransitionProbability(int fromDegree, int toDegree)
    {
        return HarmonicTransitions.GetValueOrDefault((fromDegree, toDegree), 0.05);
    }

    /// <summary>
    /// Gets the expected cadence probability at a phrase boundary.
    /// </summary>
    /// <param name="cadenceType">The cadence type.</param>
    /// <returns>Probability (0-1).</returns>
    public double GetCadenceProbability(CadenceType cadenceType)
    {
        return CadenceProbabilities.GetValueOrDefault(cadenceType, 0.1);
    }

    /// <summary>
    /// Gets the most expected next scale degree.
    /// </summary>
    /// <param name="currentDegree">Current scale degree.</param>
    /// <returns>Most probable next degree and its probability.</returns>
    public (int degree, double probability) GetMostExpectedDegree(int currentDegree)
    {
        var maxProbability = 0.0;
        var mostExpected = 1;

        for (var degree = 1; degree <= 7; degree++)
        {
            var prob = GetDegreeTransitionProbability(currentDegree, degree);
            if (prob > maxProbability)
            {
                maxProbability = prob;
                mostExpected = degree;
            }
        }

        return (mostExpected, maxProbability);
    }

    /// <summary>
    /// Gets the most expected next chord.
    /// </summary>
    /// <param name="currentChordDegree">Current chord degree.</param>
    /// <returns>Most probable next chord degree and its probability.</returns>
    public (int degree, double probability) GetMostExpectedChord(int currentChordDegree)
    {
        var maxProbability = 0.0;
        var mostExpected = 1;

        for (var degree = 1; degree <= 7; degree++)
        {
            var prob = GetHarmonicTransitionProbability(currentChordDegree, degree);
            if (prob > maxProbability)
            {
                maxProbability = prob;
                mostExpected = degree;
            }
        }

        return (mostExpected, maxProbability);
    }

    /// <summary>
    /// Updates expectations based on observed event (learning).
    /// </summary>
    /// <param name="fromDegree">Previous degree.</param>
    /// <param name="toDegree">Observed degree.</param>
    /// <param name="learningRate">How much to adjust (0-1).</param>
    public void UpdateFromObservation(int fromDegree, int toDegree, double learningRate = 0.1)
    {
        var key = (fromDegree, toDegree);
        var current = DegreeTransitions.GetValueOrDefault(key, 0.05);

        // Increase probability of observed transition
        DegreeTransitions[key] = current + learningRate * (1 - current);

        // Decrease probability of other transitions (normalize)
        var total = 0.0;
        for (var degree = 1; degree <= 7; degree++)
        {
            total += DegreeTransitions.GetValueOrDefault((fromDegree, degree), 0.05);
        }

        if (total > 0)
        {
            for (var degree = 1; degree <= 7; degree++)
            {
                var k = (fromDegree, degree);
                DegreeTransitions[k] = DegreeTransitions.GetValueOrDefault(k, 0.05) / total;
            }
        }
    }

    private void LoadDefaultDistributions()
    {
        // Default interval probabilities (based on corpus studies)
        IntervalProbabilities[0] = 0.15;  // Unison
        IntervalProbabilities[1] = 0.12;  // Minor second
        IntervalProbabilities[-1] = 0.12;
        IntervalProbabilities[2] = 0.20;  // Major second
        IntervalProbabilities[-2] = 0.18;
        IntervalProbabilities[3] = 0.08;  // Minor third
        IntervalProbabilities[-3] = 0.06;
        IntervalProbabilities[4] = 0.04;  // Major third
        IntervalProbabilities[-4] = 0.03;
        IntervalProbabilities[5] = 0.02;  // Perfect fourth
        IntervalProbabilities[-5] = 0.02;

        // Default harmonic transitions (tonal music)
        HarmonicTransitions[(1, 4)] = 0.20;
        HarmonicTransitions[(1, 5)] = 0.25;
        HarmonicTransitions[(1, 6)] = 0.15;
        HarmonicTransitions[(4, 5)] = 0.30;
        HarmonicTransitions[(4, 1)] = 0.20;
        HarmonicTransitions[(5, 1)] = 0.50;
        HarmonicTransitions[(5, 6)] = 0.15;
        HarmonicTransitions[(2, 5)] = 0.35;
        HarmonicTransitions[(6, 4)] = 0.20;
        HarmonicTransitions[(6, 2)] = 0.20;

        // Default cadence probabilities
        CadenceProbabilities[CadenceType.AuthenticPerfect] = 0.45;
        CadenceProbabilities[CadenceType.Half] = 0.25;
        CadenceProbabilities[CadenceType.Plagal] = 0.10;
        CadenceProbabilities[CadenceType.Deceptive] = 0.10;
        CadenceProbabilities[CadenceType.AuthenticImperfect] = 0.10;

        // Default phrase lengths
        PhraseLengthProbabilities[4] = 0.50;
        PhraseLengthProbabilities[8] = 0.30;
        PhraseLengthProbabilities[2] = 0.10;
        PhraseLengthProbabilities[6] = 0.10;
    }

    private void LoadStyleOverrides(string styleId)
    {
        switch (styleId.ToLowerInvariant())
        {
            case "celtic":
                // Celtic tends toward modal, less dominant-tonic
                HarmonicTransitions[(5, 1)] = 0.35;
                HarmonicTransitions[(7, 1)] = 0.20; // bVII to I common
                CadenceProbabilities[CadenceType.AuthenticPerfect] = 0.35;
                CadenceProbabilities[CadenceType.Plagal] = 0.25;
                ModeProbabilities[ModeType.Major] = 0.60;
                ModeProbabilities[ModeType.Dorian] = 0.20;
                ModeProbabilities[ModeType.Mixolydian] = 0.15;
                ExpectedHarmonicRhythm = 0.5; // Slower harmonic rhythm
                break;

            case "jazz":
                // Jazz has more ii-V-I and extended harmony
                HarmonicTransitions[(2, 5)] = 0.50;
                HarmonicTransitions[(5, 1)] = 0.45;
                HarmonicTransitions[(1, 6)] = 0.20;
                HarmonicTransitions[(6, 2)] = 0.30;
                ExpectedHarmonicRhythm = 2.0; // Faster harmonic rhythm
                CadenceProbabilities[CadenceType.Deceptive] = 0.15;
                break;

            case "baroque":
                // Baroque has strong functional harmony
                HarmonicTransitions[(5, 1)] = 0.55;
                HarmonicTransitions[(4, 5)] = 0.35;
                CadenceProbabilities[CadenceType.AuthenticPerfect] = 0.55;
                ExpectedHarmonicRhythm = 1.0;
                break;
        }
    }
}
