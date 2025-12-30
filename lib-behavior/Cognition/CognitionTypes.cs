// =============================================================================
// Cognition Pipeline Types
// Core types for the 5-stage cognition pipeline.
// =============================================================================

namespace BeyondImmersion.Bannou.Behavior.Cognition;

/// <summary>
/// Attention budget based on agent state.
/// Controls how many perceptions can be processed per tick.
/// </summary>
public sealed class AttentionBudget
{
    /// <summary>
    /// Total attention units available this tick (affected by energy, stress, etc.)
    /// </summary>
    public float TotalUnits { get; init; } = 100f;

    /// <summary>
    /// Maximum perceptions to process regardless of budget.
    /// </summary>
    public int MaxPerceptions { get; init; } = 10;

    /// <summary>
    /// Reserved attention for specific categories.
    /// </summary>
    public IReadOnlyDictionary<string, float> CategoryReservations { get; init; } =
        new Dictionary<string, float>();
}

/// <summary>
/// Configurable attention weights per category.
/// Higher weights mean the perception is more likely to pass the attention filter.
/// </summary>
public sealed class AttentionWeights
{
    /// <summary>
    /// Priority multiplier for threat perceptions.
    /// </summary>
    public float ThreatWeight { get; init; } = 10.0f;

    /// <summary>
    /// Priority multiplier for novel/new perceptions.
    /// </summary>
    public float NoveltyWeight { get; init; } = 5.0f;

    /// <summary>
    /// Priority multiplier for social perceptions.
    /// </summary>
    public float SocialWeight { get; init; } = 3.0f;

    /// <summary>
    /// Priority multiplier for routine perceptions.
    /// </summary>
    public float RoutineWeight { get; init; } = 1.0f;

    /// <summary>
    /// Whether high-urgency threats should bypass the normal pipeline
    /// and go directly to intention formation.
    /// </summary>
    public bool ThreatFastTrack { get; init; } = false;

    /// <summary>
    /// Urgency threshold for threat fast-track (0-1).
    /// Perceptions with urgency above this value skip to Stage 5.
    /// </summary>
    public float ThreatFastTrackThreshold { get; init; } = 0.8f;

    /// <summary>
    /// Dynamic adjustments based on agent state.
    /// </summary>
    public IReadOnlyDictionary<string, float> ContextualModifiers { get; init; } =
        new Dictionary<string, float>();

    /// <summary>
    /// Gets the weight for a given category.
    /// </summary>
    /// <param name="category">The perception category.</param>
    /// <returns>The weight multiplier for that category.</returns>
    public float GetWeight(string category)
    {
        return category.ToLowerInvariant() switch
        {
            "threat" => ThreatWeight,
            "novelty" => NoveltyWeight,
            "social" => SocialWeight,
            "routine" => RoutineWeight,
            _ => 1.0f
        };
    }
}

/// <summary>
/// Weights for significance scoring.
/// Determines how emotional impact, goal relevance, and relationships
/// combine to produce a significance score.
/// </summary>
public sealed class SignificanceWeights
{
    /// <summary>
    /// Weight for emotional impact factor.
    /// </summary>
    public float EmotionalWeight { get; init; } = 0.4f;

    /// <summary>
    /// Weight for goal relevance factor.
    /// </summary>
    public float GoalRelevanceWeight { get; init; } = 0.4f;

    /// <summary>
    /// Weight for relationship factor.
    /// </summary>
    public float RelationshipWeight { get; init; } = 0.2f;

    /// <summary>
    /// Threshold for storing memories (0-1).
    /// Perceptions with significance above this are stored.
    /// </summary>
    public float StorageThreshold { get; init; } = 0.7f;

    /// <summary>
    /// Computes the weighted average significance score.
    /// </summary>
    /// <param name="emotional">Emotional impact factor (0-1).</param>
    /// <param name="goalRelevance">Goal relevance factor (0-1).</param>
    /// <param name="relationship">Relationship factor (0-1).</param>
    /// <returns>The computed significance score (0-1).</returns>
    public float ComputeScore(float emotional, float goalRelevance, float relationship)
    {
        var totalWeight = EmotionalWeight + GoalRelevanceWeight + RelationshipWeight;
        if (totalWeight <= 0)
        {
            return 0f;
        }

        return (emotional * EmotionalWeight +
                goalRelevance * GoalRelevanceWeight +
                relationship * RelationshipWeight) / totalWeight;
    }
}

/// <summary>
/// Result of significance assessment for a perception.
/// </summary>
public sealed class SignificanceScore
{
    /// <summary>
    /// Emotional impact factor (0-1).
    /// </summary>
    public float EmotionalImpact { get; init; }

    /// <summary>
    /// Goal relevance factor (0-1).
    /// </summary>
    public float GoalRelevance { get; init; }

    /// <summary>
    /// Relationship factor (0-1).
    /// </summary>
    public float RelationshipFactor { get; init; }

    /// <summary>
    /// Computed total score (0-1).
    /// </summary>
    public float TotalScore { get; init; }

    /// <summary>
    /// Storage threshold used for this assessment.
    /// </summary>
    public float StorageThreshold { get; init; }

    /// <summary>
    /// Whether this perception should be stored as a memory.
    /// </summary>
    public bool ShouldStore => TotalScore >= StorageThreshold;
}

/// <summary>
/// Result of goal impact evaluation.
/// </summary>
public sealed class GoalImpactResult
{
    /// <summary>
    /// Whether any goals were affected and replanning is needed.
    /// </summary>
    public bool RequiresReplan { get; init; }

    /// <summary>
    /// IDs of goals affected by the perceptions.
    /// </summary>
    public IReadOnlyList<string> AffectedGoals { get; init; } = [];

    /// <summary>
    /// Urgency level for replanning (0-1).
    /// Higher urgency means faster, shallower GOAP search.
    /// </summary>
    public float Urgency { get; init; }

    /// <summary>
    /// Optional message describing the impact.
    /// </summary>
    public string? Message { get; init; }
}

/// <summary>
/// GOAP planning options mapped from urgency level.
/// </summary>
public sealed class UrgencyBasedPlanningOptions
{
    /// <summary>
    /// Maximum search depth for A* planner.
    /// </summary>
    public int MaxDepth { get; init; }

    /// <summary>
    /// Timeout in milliseconds for planning.
    /// </summary>
    public int TimeoutMs { get; init; }

    /// <summary>
    /// Maximum nodes to expand during search.
    /// </summary>
    public int MaxNodes { get; init; }

    /// <summary>
    /// Maps urgency (0-1) to planning options.
    /// </summary>
    /// <param name="urgency">Urgency level (0-1).</param>
    /// <returns>Planning options appropriate for the urgency level.</returns>
    public static UrgencyBasedPlanningOptions FromUrgency(float urgency)
    {
        // Low urgency (0-0.3): Full deliberation
        if (urgency < 0.3f)
        {
            return new UrgencyBasedPlanningOptions
            {
                MaxDepth = 10,
                TimeoutMs = 100,
                MaxNodes = 1000
            };
        }

        // Medium urgency (0.3-0.7): Quick decision
        if (urgency < 0.7f)
        {
            return new UrgencyBasedPlanningOptions
            {
                MaxDepth = 6,
                TimeoutMs = 50,
                MaxNodes = 500
            };
        }

        // High urgency (0.7-1.0): Immediate reaction
        return new UrgencyBasedPlanningOptions
        {
            MaxDepth = 3,
            TimeoutMs = 20,
            MaxNodes = 200
        };
    }
}

/// <summary>
/// Result of attention filtering.
/// </summary>
public sealed class FilteredPerceptionsResult
{
    /// <summary>
    /// Perceptions that passed the attention filter.
    /// </summary>
    public IReadOnlyList<Perception> FilteredPerceptions { get; init; } = [];

    /// <summary>
    /// High-urgency threat perceptions that should skip to Stage 5.
    /// Only populated when ThreatFastTrack is enabled.
    /// </summary>
    public IReadOnlyList<Perception> FastTrackPerceptions { get; init; } = [];

    /// <summary>
    /// Perceptions that were dropped due to budget constraints.
    /// </summary>
    public IReadOnlyList<Perception> DroppedPerceptions { get; init; } = [];

    /// <summary>
    /// Remaining attention budget after filtering.
    /// </summary>
    public float RemainingBudget { get; init; }
}
