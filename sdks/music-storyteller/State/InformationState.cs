namespace BeyondImmersion.Bannou.MusicStoryteller.State;

/// <summary>
/// Tracks information-theoretic properties of the composition.
/// Based on information content (IC) and entropy measurements.
/// </summary>
public sealed class InformationState
{
    /// <summary>
    /// Rolling average of information content per event.
    /// Higher values indicate more surprising/unpredictable music.
    /// </summary>
    public double AverageIC { get; set; } = 2.0;

    /// <summary>
    /// Current local entropy (uncertainty about next event).
    /// Higher entropy means more possible outcomes.
    /// </summary>
    public double LocalEntropy { get; set; } = 2.5;

    /// <summary>
    /// Peak information content seen so far.
    /// </summary>
    public double PeakIC { get; set; }

    /// <summary>
    /// Bar number of the peak IC event.
    /// </summary>
    public int PeakBar { get; set; }

    /// <summary>
    /// Number of high-IC events (surprises) so far.
    /// </summary>
    public int HighICEventCount { get; set; }

    /// <summary>
    /// Number of low-IC events (predictable) so far.
    /// </summary>
    public int LowICEventCount { get; set; }

    /// <summary>
    /// Total events processed.
    /// </summary>
    public int TotalEvents { get; set; }

    /// <summary>
    /// Accumulated IC over the piece.
    /// </summary>
    public double TotalIC { get; set; }

    /// <summary>
    /// Recent IC values for local analysis.
    /// </summary>
    public List<double> RecentIC { get; } = new(16);

    /// <summary>
    /// Threshold for considering an event "high IC" (surprising).
    /// Default is 3.0 bits - about 1/8 probability.
    /// </summary>
    public double HighICThreshold { get; set; } = 3.0;

    /// <summary>
    /// Threshold for considering an event "low IC" (predictable).
    /// Default is 1.0 bits - about 1/2 probability.
    /// </summary>
    public double LowICThreshold { get; set; } = 1.0;

    /// <summary>
    /// Gets the balance between surprise and predictability.
    /// Values near 0.5 indicate good balance.
    /// </summary>
    public double SurpriseBalance
    {
        get
        {
            var total = HighICEventCount + LowICEventCount;
            if (total == 0) return 0.5;
            return (double)HighICEventCount / total;
        }
    }

    /// <summary>
    /// Gets whether the piece is currently in a high-information region.
    /// </summary>
    public bool InHighInformationRegion => AverageIC > HighICThreshold * 0.8;

    /// <summary>
    /// Gets whether the piece is currently in a low-information region.
    /// </summary>
    public bool InLowInformationRegion => AverageIC < LowICThreshold * 1.5;

    /// <summary>
    /// Records an IC measurement for an event.
    /// </summary>
    /// <param name="ic">Information content in bits.</param>
    /// <param name="bar">Current bar number.</param>
    public void RecordIC(double ic, int bar)
    {
        TotalEvents++;
        TotalIC += ic;

        // Update rolling average
        AverageIC = AverageIC * 0.9 + ic * 0.1;

        // Track recent values
        RecentIC.Add(ic);
        if (RecentIC.Count > 16)
        {
            RecentIC.RemoveAt(0);
        }

        // Update local entropy estimate
        UpdateLocalEntropy();

        // Track peaks
        if (ic > PeakIC)
        {
            PeakIC = ic;
            PeakBar = bar;
        }

        // Categorize event
        if (ic > HighICThreshold)
        {
            HighICEventCount++;
        }
        else if (ic < LowICThreshold)
        {
            LowICEventCount++;
        }
    }

    /// <summary>
    /// Gets the recommended IC for the next event based on current state.
    /// </summary>
    /// <returns>Target IC value in bits.</returns>
    public double GetTargetIC()
    {
        // Aim for balance - if too predictable, increase target; if too surprising, decrease
        if (SurpriseBalance < 0.3)
        {
            // Too predictable - aim higher
            return 2.5;
        }
        if (SurpriseBalance > 0.5)
        {
            // Too surprising - aim lower
            return 1.5;
        }

        // Balanced - aim for moderate IC
        return 2.0;
    }

    /// <summary>
    /// Gets the IC trajectory (whether IC is increasing or decreasing).
    /// </summary>
    /// <returns>Positive for increasing, negative for decreasing, near zero for stable.</returns>
    public double GetTrajectory()
    {
        if (RecentIC.Count < 4) return 0;

        var firstHalf = RecentIC.Take(RecentIC.Count / 2).Average();
        var secondHalf = RecentIC.Skip(RecentIC.Count / 2).Average();

        return secondHalf - firstHalf;
    }

    /// <summary>
    /// Creates a deep copy of this information state.
    /// </summary>
    public InformationState Clone()
    {
        var clone = new InformationState
        {
            AverageIC = AverageIC,
            LocalEntropy = LocalEntropy,
            PeakIC = PeakIC,
            PeakBar = PeakBar,
            HighICEventCount = HighICEventCount,
            LowICEventCount = LowICEventCount,
            TotalEvents = TotalEvents,
            TotalIC = TotalIC,
            HighICThreshold = HighICThreshold,
            LowICThreshold = LowICThreshold
        };
        clone.RecentIC.AddRange(RecentIC);
        return clone;
    }

    private void UpdateLocalEntropy()
    {
        if (RecentIC.Count < 4)
        {
            LocalEntropy = 2.5; // Default mid-range entropy
            return;
        }

        // Estimate entropy from variance in recent IC values
        var mean = RecentIC.Average();
        var variance = RecentIC.Sum(x => (x - mean) * (x - mean)) / RecentIC.Count;

        // Higher variance suggests higher entropy (less predictable)
        LocalEntropy = Math.Sqrt(variance) + mean * 0.5;
    }

    public override string ToString()
    {
        return $"Info[AvgIC={AverageIC:F2}, H={LocalEntropy:F2}, Balance={SurpriseBalance:P0}]";
    }
}
