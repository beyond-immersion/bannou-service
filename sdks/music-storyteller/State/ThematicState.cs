using BeyondImmersion.Bannou.MusicTheory.Melody;

namespace BeyondImmersion.Bannou.MusicStoryteller.State;

/// <summary>
/// Represents a motif usage event for tracking.
/// </summary>
public sealed class MotifUsage
{
    /// <summary>
    /// The motif ID.
    /// </summary>
    public string MotifId { get; }

    /// <summary>
    /// Bar number where the motif was used.
    /// </summary>
    public int Bar { get; }

    /// <summary>
    /// Type of transformation applied (if any).
    /// </summary>
    public MotifTransformationType Transformation { get; }

    /// <summary>
    /// Creates a new motif usage record.
    /// </summary>
    public MotifUsage(string motifId, int bar, MotifTransformationType transformation = MotifTransformationType.None)
    {
        MotifId = motifId;
        Bar = bar;
        Transformation = transformation;
    }
}

/// <summary>
/// Types of motif transformations.
/// </summary>
public enum MotifTransformationType
{
    /// <summary>Original form.</summary>
    None,

    /// <summary>Repeated exactly.</summary>
    Repetition,

    /// <summary>Moved to different pitch level.</summary>
    Sequence,

    /// <summary>Intervals inverted.</summary>
    Inversion,

    /// <summary>Played backwards.</summary>
    Retrograde,

    /// <summary>Rhythmic values doubled.</summary>
    Augmentation,

    /// <summary>Rhythmic values halved.</summary>
    Diminution,

    /// <summary>Only part of the motif used.</summary>
    Fragmentation,

    /// <summary>Extended with additional notes.</summary>
    Extension
}

/// <summary>
/// Tracks thematic/motivic state of the composition.
/// Manages motif memory, usage patterns, and development.
/// </summary>
public sealed class ThematicState
{
    /// <summary>
    /// The main/primary motif of the piece (if established).
    /// </summary>
    public NamedMotif? MainMotif { get; set; }

    /// <summary>
    /// Secondary motifs available for use.
    /// </summary>
    public List<NamedMotif> SecondaryMotifs { get; } = [];

    /// <summary>
    /// History of motif usages for tracking development.
    /// </summary>
    public List<MotifUsage> UsageHistory { get; } = [];

    /// <summary>
    /// Counts of how many times each motif has been used.
    /// </summary>
    public Dictionary<string, int> UsageCounts { get; } = new();

    /// <summary>
    /// Last bar each motif was used (for tracking freshness).
    /// </summary>
    public Dictionary<string, int> LastUsedBar { get; } = new();

    /// <summary>
    /// Whether the main motif has been introduced.
    /// </summary>
    public bool MainMotifIntroduced => MainMotif != null && UsageCounts.GetValueOrDefault(MainMotif.Id, 0) > 0;

    /// <summary>
    /// Number of bars since the main motif was used.
    /// </summary>
    public int BarsSinceMainMotif { get; set; }

    /// <summary>
    /// Current motivic development stage.
    /// </summary>
    public DevelopmentStage Stage { get; set; } = DevelopmentStage.Introduction;

    /// <summary>
    /// Gets the familiarity score for a motif (0-1).
    /// Higher values mean the listener has heard it more.
    /// </summary>
    /// <param name="motifId">The motif ID.</param>
    /// <returns>Familiarity from 0 (never heard) to 1 (very familiar).</returns>
    public double GetFamiliarity(string motifId)
    {
        var count = UsageCounts.GetValueOrDefault(motifId, 0);
        // Familiarity increases logarithmically, saturating around 5-6 uses
        return 1.0 - 1.0 / (1.0 + count * 0.5);
    }

    /// <summary>
    /// Gets the freshness score for a motif (0-1).
    /// Lower values mean it was used recently.
    /// </summary>
    /// <param name="motifId">The motif ID.</param>
    /// <param name="currentBar">The current bar number.</param>
    /// <returns>Freshness from 0 (just used) to 1 (not used for a while).</returns>
    public double GetFreshness(string motifId, int currentBar)
    {
        if (!LastUsedBar.TryGetValue(motifId, out var lastBar))
        {
            return 1.0; // Never used = maximum freshness
        }

        var barsSince = currentBar - lastBar;
        // Freshness increases over ~16 bars
        return Math.Min(barsSince / 16.0, 1.0);
    }

    /// <summary>
    /// Records a motif usage.
    /// </summary>
    /// <param name="motifId">The motif ID.</param>
    /// <param name="bar">The current bar.</param>
    /// <param name="transformation">The transformation applied.</param>
    public void RecordUsage(string motifId, int bar, MotifTransformationType transformation = MotifTransformationType.None)
    {
        UsageHistory.Add(new MotifUsage(motifId, bar, transformation));
        UsageCounts[motifId] = UsageCounts.GetValueOrDefault(motifId, 0) + 1;
        LastUsedBar[motifId] = bar;

        if (MainMotif != null && motifId == MainMotif.Id)
        {
            BarsSinceMainMotif = 0;
        }
    }

    /// <summary>
    /// Advances thematic state by one bar.
    /// </summary>
    public void AdvanceBar()
    {
        if (MainMotifIntroduced)
        {
            BarsSinceMainMotif++;
        }
    }

    /// <summary>
    /// Determines if the main motif should return for unity.
    /// </summary>
    /// <param name="currentBar">The current bar.</param>
    /// <returns>True if main motif return is recommended.</returns>
    public bool ShouldReturnMainMotif(int currentBar)
    {
        if (MainMotif == null || !MainMotifIntroduced)
        {
            return false;
        }

        // Recommend return after 8+ bars away, or at section boundaries
        return BarsSinceMainMotif >= 8 || (currentBar % 16 == 0 && BarsSinceMainMotif >= 4);
    }

    /// <summary>
    /// Creates a deep copy of this thematic state.
    /// </summary>
    public ThematicState Clone()
    {
        var clone = new ThematicState
        {
            MainMotif = MainMotif,
            BarsSinceMainMotif = BarsSinceMainMotif,
            Stage = Stage
        };
        clone.SecondaryMotifs.AddRange(SecondaryMotifs);
        clone.UsageHistory.AddRange(UsageHistory);
        foreach (var (key, value) in UsageCounts)
        {
            clone.UsageCounts[key] = value;
        }
        foreach (var (key, value) in LastUsedBar)
        {
            clone.LastUsedBar[key] = value;
        }
        return clone;
    }

    public override string ToString()
    {
        var mainName = MainMotif?.Id ?? "none";
        return $"Thematic[Main={mainName}, Stage={Stage}, BarsSince={BarsSinceMainMotif}]";
    }
}

/// <summary>
/// Stages of motivic development in a piece.
/// </summary>
public enum DevelopmentStage
{
    /// <summary>Initial presentation of main themes.</summary>
    Introduction,

    /// <summary>Themes being developed and varied.</summary>
    Development,

    /// <summary>Themes transforming significantly.</summary>
    Transformation,

    /// <summary>Return to original themes.</summary>
    Recapitulation,

    /// <summary>Final statements, possibly extended.</summary>
    Coda
}
