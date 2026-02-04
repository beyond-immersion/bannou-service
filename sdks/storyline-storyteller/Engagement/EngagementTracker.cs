using BeyondImmersion.Bannou.StorylineTheory.Arcs;
using BeyondImmersion.Bannou.StorylineTheory.Scoring;
using BeyondImmersion.Bannou.StorylineTheory.State;
using BeyondImmersion.Bannou.StorylineStoryteller.Templates;

namespace BeyondImmersion.Bannou.StorylineStoryteller.Engagement;

/// <summary>
/// Tracks and estimates audience engagement throughout a story.
/// Combines multiple scoring dimensions for real-time engagement estimation.
/// </summary>
public sealed class EngagementTracker
{
    private readonly List<EngagementSnapshot> _snapshots = new();
    private readonly NarrativeTemplate _template;

    /// <summary>
    /// Hypothesis weights for engagement scoring.
    /// Subject to empirical calibration.
    /// </summary>
    public static class Weights
    {
        /// <summary>Weight for pacing satisfaction.</summary>
        public const double Pacing = 0.25;

        /// <summary>Weight for narrative state appropriateness.</summary>
        public const double StateAlignment = 0.25;

        /// <summary>Weight for emotional arc tracking.</summary>
        public const double ArcTracking = 0.20;

        /// <summary>Weight for variety (avoiding monotony).</summary>
        public const double Variety = 0.15;

        /// <summary>Weight for momentum (avoiding stagnation).</summary>
        public const double Momentum = 0.15;
    }

    /// <summary>
    /// Creates an engagement tracker for a narrative template.
    /// </summary>
    /// <param name="template">The narrative template to track against.</param>
    public EngagementTracker(NarrativeTemplate template)
    {
        _template = template;
    }

    /// <summary>
    /// Creates a tracker using the Hero's Journey template.
    /// </summary>
    public static EngagementTracker Default() =>
        new(NarrativeTemplates.HeroJourney);

    /// <summary>
    /// Records a narrative state snapshot at the current story position.
    /// </summary>
    /// <param name="position">Story position (0-1).</param>
    /// <param name="state">Current narrative state.</param>
    /// <param name="beatCode">Optional beat code if a beat just occurred.</param>
    public void RecordSnapshot(double position, NarrativeState state, string? beatCode = null)
    {
        var snapshot = new EngagementSnapshot(
            Position: position,
            State: state.Clone(),
            BeatCode: beatCode,
            Timestamp: DateTime.UtcNow);

        _snapshots.Add(snapshot);
    }

    /// <summary>
    /// Gets the current engagement score.
    /// </summary>
    /// <returns>Engagement score (0-1) and breakdown.</returns>
    public EngagementResult GetEngagement()
    {
        if (_snapshots.Count == 0)
        {
            return new EngagementResult(
                TotalScore: 0.5, // Neutral baseline
                PacingScore: 0.5,
                StateAlignmentScore: 0.5,
                ArcTrackingScore: 0.5,
                VarietyScore: 0.5,
                MomentumScore: 0.5,
                Warnings: new[] { "No snapshots recorded yet" });
        }

        var currentPosition = _snapshots[^1].Position;
        var currentState = _snapshots[^1].State;

        // 1. Pacing score
        var beatOccurrences = _snapshots
            .Where(s => s.BeatCode != null)
            .Select(s => (s.BeatCode!, s.Position))
            .ToList();
        var pacingScore = PacingSatisfactionScorer.Calculate(beatOccurrences);

        // 2. State alignment score
        var targetState = _template.GetTargetStateAt(currentPosition);
        var stateAlignmentScore = 1.0 - currentState.NormalizedDistanceTo(targetState);

        // 3. Arc tracking score
        var arcTrackingScore = CalculateArcTracking();

        // 4. Variety score
        var varietyScore = CalculateVariety();

        // 5. Momentum score
        var momentumScore = CalculateMomentum();

        var totalScore = Weights.Pacing * pacingScore +
                        Weights.StateAlignment * stateAlignmentScore +
                        Weights.ArcTracking * arcTrackingScore +
                        Weights.Variety * varietyScore +
                        Weights.Momentum * momentumScore;

        // Generate warnings
        var warnings = new List<string>();
        if (pacingScore < 0.3)
            warnings.Add("Pacing is significantly off from beat sheet targets");
        if (stateAlignmentScore < 0.3)
            warnings.Add("Narrative state is far from expected for this story position");
        if (varietyScore < 0.3)
            warnings.Add("Story may be feeling monotonous - consider varying emotional states");
        if (momentumScore < 0.3)
            warnings.Add("Story momentum is low - consider more significant events");

        return new EngagementResult(
            TotalScore: totalScore,
            PacingScore: pacingScore,
            StateAlignmentScore: stateAlignmentScore,
            ArcTrackingScore: arcTrackingScore,
            VarietyScore: varietyScore,
            MomentumScore: momentumScore,
            Warnings: warnings);
    }

    /// <summary>
    /// Gets the best-matching emotional arc for the story so far.
    /// </summary>
    public (EmotionalArc Arc, double FitScore) GetEmotionalArc()
    {
        if (_snapshots.Count < 2)
            return (EmotionalArcs.ManInHole, 0.0);

        // Extract hope values as the fortune trajectory
        var trajectory = _snapshots
            .Select(s => s.State.Hope)
            .ToList();

        return EmotionalArcs.FindBestMatch(trajectory);
    }

    /// <summary>
    /// Suggests what should happen next to maintain engagement.
    /// </summary>
    public EngagementSuggestion GetSuggestion()
    {
        if (_snapshots.Count == 0)
        {
            return new EngagementSuggestion(
                SuggestedBeat: _template.Beats.FirstOrDefault(),
                TargetState: NarrativeState.Equilibrium,
                Rationale: "Begin the story with the opening beat");
        }

        var current = _snapshots[^1];
        var nextBeat = _template.GetNextBeat(current.Position);
        var targetState = nextBeat != null
            ? nextBeat.TargetState
            : _template.Beats[^1].TargetState;

        var suggestions = NarrativePotentialScorer.SuggestAdjustments(
            current.State,
            targetState,
            current.Position);

        var rationale = suggestions.Suggestions.Count > 0
            ? string.Join("; ", suggestions.Suggestions)
            : nextBeat != null
                ? $"Move toward {nextBeat.Name}"
                : "Maintain current trajectory";

        return new EngagementSuggestion(
            SuggestedBeat: nextBeat,
            TargetState: targetState,
            Rationale: rationale);
    }

    /// <summary>
    /// Calculate how well the story tracks expected emotional arcs.
    /// </summary>
    private double CalculateArcTracking()
    {
        if (_snapshots.Count < 3)
            return 0.5;

        var (_, fitScore) = GetEmotionalArc();
        return fitScore;
    }

    /// <summary>
    /// Calculate variety score - penalize monotony.
    /// </summary>
    private double CalculateVariety()
    {
        if (_snapshots.Count < 3)
            return 0.5;

        // Measure standard deviation of state changes
        var changes = new List<double>();
        for (var i = 1; i < _snapshots.Count; i++)
        {
            var distance = _snapshots[i].State.DistanceTo(_snapshots[i - 1].State);
            changes.Add(distance);
        }

        if (changes.Count == 0)
            return 0.5;

        var avgChange = changes.Average();
        var stdDev = Math.Sqrt(changes.Average(c => Math.Pow(c - avgChange, 2)));

        // Variety is good when there's meaningful standard deviation
        // but not so much that it's chaotic
        if (avgChange < 0.05)
            return 0.2; // Too static

        if (stdDev > 0.3)
            return 0.3; // Too chaotic

        return Math.Clamp(avgChange * 2 + stdDev, 0, 1);
    }

    /// <summary>
    /// Calculate momentum - penalize stagnation.
    /// </summary>
    private double CalculateMomentum()
    {
        if (_snapshots.Count < 2)
            return 0.5;

        // Recent snapshots (last 20% or at least 3)
        var recentCount = Math.Max(3, _snapshots.Count / 5);
        var recent = _snapshots.TakeLast(recentCount).ToList();

        if (recent.Count < 2)
            return 0.5;

        // Average state change in recent history
        var totalChange = 0.0;
        for (var i = 1; i < recent.Count; i++)
        {
            totalChange += recent[i].State.DistanceTo(recent[i - 1].State);
        }

        var avgChange = totalChange / (recent.Count - 1);

        // Good momentum is around 0.1-0.3 state change per snapshot
        if (avgChange < 0.03)
            return 0.2; // Stagnant

        if (avgChange > 0.4)
            return 0.7; // Very active (not necessarily bad)

        return Math.Clamp(avgChange * 3, 0, 1);
    }

    /// <summary>
    /// Gets all recorded snapshots.
    /// </summary>
    public IReadOnlyList<EngagementSnapshot> Snapshots => _snapshots.AsReadOnly();

    /// <summary>
    /// Clears all recorded snapshots.
    /// </summary>
    public void Reset() => _snapshots.Clear();
}

/// <summary>
/// A snapshot of narrative state at a point in the story.
/// </summary>
/// <param name="Position">Story position (0-1).</param>
/// <param name="State">Narrative state at this point.</param>
/// <param name="BeatCode">Beat code if a beat occurred here.</param>
/// <param name="Timestamp">When this snapshot was recorded.</param>
public sealed record EngagementSnapshot(
    double Position,
    NarrativeState State,
    string? BeatCode,
    DateTime Timestamp);

/// <summary>
/// Result of engagement calculation.
/// </summary>
/// <param name="TotalScore">Overall engagement score (0-1).</param>
/// <param name="PacingScore">Pacing satisfaction score.</param>
/// <param name="StateAlignmentScore">Alignment with expected state.</param>
/// <param name="ArcTrackingScore">Emotional arc tracking score.</param>
/// <param name="VarietyScore">Variety/non-monotony score.</param>
/// <param name="MomentumScore">Story momentum score.</param>
/// <param name="Warnings">Any engagement warnings.</param>
public sealed record EngagementResult(
    double TotalScore,
    double PacingScore,
    double StateAlignmentScore,
    double ArcTrackingScore,
    double VarietyScore,
    double MomentumScore,
    IReadOnlyList<string> Warnings);

/// <summary>
/// Suggestion for maintaining engagement.
/// </summary>
/// <param name="SuggestedBeat">Next beat to aim for.</param>
/// <param name="TargetState">Ideal narrative state to target.</param>
/// <param name="Rationale">Explanation of the suggestion.</param>
public sealed record EngagementSuggestion(
    TemplateBeat? SuggestedBeat,
    NarrativeState TargetState,
    string Rationale);
