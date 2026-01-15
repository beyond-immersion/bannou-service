// =============================================================================
// Cognition Pipeline Types
// Core types for the 5-stage cognition pipeline.
// =============================================================================

namespace BeyondImmersion.Bannou.Behavior.Cognition;

/// <summary>
/// Constants for the cognition pipeline.
/// Values can be initialized from <see cref="BeyondImmersion.BannouService.Behavior.BehaviorServiceConfiguration"/>
/// at startup via <see cref="Initialize"/>. Tests that validate specific thresholds should
/// reference these constants to stay synchronized with implementation changes.
/// </summary>
/// <remarks>
/// <para>
/// This class uses a static initialization pattern to allow configuration from environment
/// variables while maintaining the static access pattern used throughout the cognition system.
/// Call <see cref="Initialize"/> once at service startup (from BehaviorService constructor).
/// </para>
/// <para>
/// If <see cref="Initialize"/> is not called, default values matching the original constants are used.
/// </para>
/// </remarks>
public static class CognitionConstants
{
    #region Initialization

    private static bool _isInitialized;

    /// <summary>
    /// Gets whether the constants have been initialized from configuration.
    /// </summary>
    public static bool IsInitialized => _isInitialized;

    /// <summary>
    /// Initializes cognition constants from service configuration.
    /// Should be called once at BehaviorService startup.
    /// </summary>
    /// <param name="config">The behavior service configuration.</param>
    /// <remarks>
    /// This method is idempotent - subsequent calls after the first are ignored.
    /// This prevents issues with scoped service instantiation calling it multiple times.
    /// </remarks>
    public static void Initialize(BeyondImmersion.BannouService.Behavior.BehaviorServiceConfiguration config)
    {
        if (_isInitialized)
            return;


        // Urgency thresholds
        _lowUrgencyThreshold = (float)config.LowUrgencyThreshold;
        _highUrgencyThreshold = (float)config.HighUrgencyThreshold;

        // Low urgency planning parameters
        _lowUrgencyMaxDepth = config.LowUrgencyMaxPlanDepth;
        _lowUrgencyTimeoutMs = config.LowUrgencyPlanTimeoutMs;
        _lowUrgencyMaxNodes = config.LowUrgencyMaxPlanNodes;

        // Medium urgency planning parameters
        _mediumUrgencyMaxDepth = config.MediumUrgencyMaxPlanDepth;
        _mediumUrgencyTimeoutMs = config.MediumUrgencyPlanTimeoutMs;
        _mediumUrgencyMaxNodes = config.MediumUrgencyMaxPlanNodes;

        // High urgency planning parameters
        _highUrgencyMaxDepth = config.HighUrgencyMaxPlanDepth;
        _highUrgencyTimeoutMs = config.HighUrgencyPlanTimeoutMs;
        _highUrgencyMaxNodes = config.HighUrgencyMaxPlanNodes;

        // Attention weights
        _defaultThreatWeight = (float)config.DefaultThreatWeight;
        _defaultNoveltyWeight = (float)config.DefaultNoveltyWeight;
        _defaultSocialWeight = (float)config.DefaultSocialWeight;
        _defaultRoutineWeight = (float)config.DefaultRoutineWeight;
        _defaultThreatFastTrackThreshold = (float)config.DefaultThreatFastTrackThreshold;

        // Significance weights
        _defaultEmotionalWeight = (float)config.DefaultEmotionalWeight;
        _defaultGoalRelevanceWeight = (float)config.DefaultGoalRelevanceWeight;
        _defaultRelationshipWeight = (float)config.DefaultRelationshipWeight;

        // Memory relevance
        _memoryMinimumRelevanceThreshold = (float)config.MemoryMinimumRelevanceThreshold;

        // Memory scoring weights
        _defaultStorageThreshold = (float)config.DefaultStorageThreshold;
        _memoryCategoryMatchWeight = (float)config.MemoryCategoryMatchWeight;
        _memoryContentOverlapWeight = (float)config.MemoryContentOverlapWeight;
        _memoryMetadataOverlapWeight = (float)config.MemoryMetadataOverlapWeight;
        _memoryRecencyBonusWeight = (float)config.MemoryRecencyBonusWeight;
        _memorySignificanceBonusWeight = (float)config.MemorySignificanceBonusWeight;

        _isInitialized = true;
    }

    /// <summary>
    /// Resets constants to default values. Primarily for testing.
    /// </summary>
    internal static void Reset()
    {
        _lowUrgencyThreshold = 0.3f;
        _highUrgencyThreshold = 0.7f;

        _lowUrgencyMaxDepth = 10;
        _lowUrgencyTimeoutMs = 100;
        _lowUrgencyMaxNodes = 1000;

        _mediumUrgencyMaxDepth = 6;
        _mediumUrgencyTimeoutMs = 50;
        _mediumUrgencyMaxNodes = 500;

        _highUrgencyMaxDepth = 3;
        _highUrgencyTimeoutMs = 20;
        _highUrgencyMaxNodes = 200;

        _defaultThreatWeight = 10.0f;
        _defaultNoveltyWeight = 5.0f;
        _defaultSocialWeight = 3.0f;
        _defaultRoutineWeight = 1.0f;
        _defaultThreatFastTrackThreshold = 0.8f;

        _defaultEmotionalWeight = 0.4f;
        _defaultGoalRelevanceWeight = 0.4f;
        _defaultRelationshipWeight = 0.2f;

        _memoryMinimumRelevanceThreshold = 0.1f;

        _defaultStorageThreshold = 0.7f;
        _memoryCategoryMatchWeight = 0.3f;
        _memoryContentOverlapWeight = 0.4f;
        _memoryMetadataOverlapWeight = 0.2f;
        _memoryRecencyBonusWeight = 0.1f;
        _memorySignificanceBonusWeight = 0.1f;

        _isInitialized = false;
    }

    #endregion

    #region Urgency Thresholds

    private static float _lowUrgencyThreshold = 0.3f;
    private static float _highUrgencyThreshold = 0.7f;

    /// <summary>
    /// Urgency threshold below which planning uses low-urgency parameters (full deliberation).
    /// Urgency in range [0, LowUrgencyThreshold) -> low urgency.
    /// </summary>
    public static float LowUrgencyThreshold => _lowUrgencyThreshold;

    /// <summary>
    /// Urgency threshold below which planning uses medium-urgency parameters (quick decision).
    /// Urgency in range [LowUrgencyThreshold, HighUrgencyThreshold) -> medium urgency.
    /// </summary>
    public static float HighUrgencyThreshold => _highUrgencyThreshold;

    #endregion

    #region Planning Parameters - Low Urgency (Full Deliberation)

    private static int _lowUrgencyMaxDepth = 10;
    private static int _lowUrgencyTimeoutMs = 100;
    private static int _lowUrgencyMaxNodes = 1000;

    /// <summary>Maximum search depth for low-urgency planning.</summary>
    public static int LowUrgencyMaxDepth => _lowUrgencyMaxDepth;

    /// <summary>Timeout in milliseconds for low-urgency planning.</summary>
    public static int LowUrgencyTimeoutMs => _lowUrgencyTimeoutMs;

    /// <summary>Maximum nodes to expand for low-urgency planning.</summary>
    public static int LowUrgencyMaxNodes => _lowUrgencyMaxNodes;

    #endregion

    #region Planning Parameters - Medium Urgency (Quick Decision)

    private static int _mediumUrgencyMaxDepth = 6;
    private static int _mediumUrgencyTimeoutMs = 50;
    private static int _mediumUrgencyMaxNodes = 500;

    /// <summary>Maximum search depth for medium-urgency planning.</summary>
    public static int MediumUrgencyMaxDepth => _mediumUrgencyMaxDepth;

    /// <summary>Timeout in milliseconds for medium-urgency planning.</summary>
    public static int MediumUrgencyTimeoutMs => _mediumUrgencyTimeoutMs;

    /// <summary>Maximum nodes to expand for medium-urgency planning.</summary>
    public static int MediumUrgencyMaxNodes => _mediumUrgencyMaxNodes;

    #endregion

    #region Planning Parameters - High Urgency (Immediate Reaction)

    private static int _highUrgencyMaxDepth = 3;
    private static int _highUrgencyTimeoutMs = 20;
    private static int _highUrgencyMaxNodes = 200;

    /// <summary>Maximum search depth for high-urgency planning.</summary>
    public static int HighUrgencyMaxDepth => _highUrgencyMaxDepth;

    /// <summary>Timeout in milliseconds for high-urgency planning.</summary>
    public static int HighUrgencyTimeoutMs => _highUrgencyTimeoutMs;

    /// <summary>Maximum nodes to expand for high-urgency planning.</summary>
    public static int HighUrgencyMaxNodes => _highUrgencyMaxNodes;

    #endregion

    #region Attention Weights Defaults

    private static float _defaultThreatWeight = 10.0f;
    private static float _defaultNoveltyWeight = 5.0f;
    private static float _defaultSocialWeight = 3.0f;
    private static float _defaultRoutineWeight = 1.0f;
    private static float _defaultThreatFastTrackThreshold = 0.8f;

    /// <summary>Default priority multiplier for threat perceptions.</summary>
    public static float DefaultThreatWeight => _defaultThreatWeight;

    /// <summary>Default priority multiplier for novel perceptions.</summary>
    public static float DefaultNoveltyWeight => _defaultNoveltyWeight;

    /// <summary>Default priority multiplier for social perceptions.</summary>
    public static float DefaultSocialWeight => _defaultSocialWeight;

    /// <summary>Default priority multiplier for routine perceptions.</summary>
    public static float DefaultRoutineWeight => _defaultRoutineWeight;

    /// <summary>Default urgency threshold for threat fast-track.</summary>
    public static float DefaultThreatFastTrackThreshold => _defaultThreatFastTrackThreshold;

    #endregion

    #region Significance Weights Defaults

    private static float _defaultEmotionalWeight = 0.4f;
    private static float _defaultGoalRelevanceWeight = 0.4f;
    private static float _defaultRelationshipWeight = 0.2f;

    /// <summary>Default weight for emotional impact in significance scoring.</summary>
    public static float DefaultEmotionalWeight => _defaultEmotionalWeight;

    /// <summary>Default weight for goal relevance in significance scoring.</summary>
    public static float DefaultGoalRelevanceWeight => _defaultGoalRelevanceWeight;

    /// <summary>Default weight for relationship factor in significance scoring.</summary>
    public static float DefaultRelationshipWeight => _defaultRelationshipWeight;

    private static float _defaultStorageThreshold = 0.7f;

    /// <summary>Default threshold for storing memories based on significance.</summary>
    public static float DefaultStorageThreshold => _defaultStorageThreshold;

    #endregion

    #region Memory Relevance Scoring

    private static float _memoryCategoryMatchWeight = 0.3f;
    private static float _memoryContentOverlapWeight = 0.4f;
    private static float _memoryMetadataOverlapWeight = 0.2f;
    private static float _memoryRecencyBonusWeight = 0.1f;
    private static float _memorySignificanceBonusWeight = 0.1f;

    /// <summary>
    /// Weight for category match in memory relevance scoring.
    /// A memory with matching category gets this added to its score.
    /// </summary>
    public static float MemoryCategoryMatchWeight => _memoryCategoryMatchWeight;

    /// <summary>
    /// Weight for content keyword overlap in memory relevance scoring.
    /// Score contribution = this weight * (overlap count / max word count).
    /// </summary>
    public static float MemoryContentOverlapWeight => _memoryContentOverlapWeight;

    /// <summary>
    /// Weight for metadata key overlap in memory relevance scoring.
    /// Score contribution = this weight * (overlap count / max key count).
    /// </summary>
    public static float MemoryMetadataOverlapWeight => _memoryMetadataOverlapWeight;

    /// <summary>
    /// Maximum recency bonus for memories less than 1 hour old.
    /// Score contribution = this weight * (1 - hours_old) for memories &lt; 1 hour.
    /// </summary>
    public static float MemoryRecencyBonusWeight => _memoryRecencyBonusWeight;

    /// <summary>
    /// Weight for memory significance bonus.
    /// Score contribution = this weight * memory.Significance.
    /// </summary>
    public static float MemorySignificanceBonusWeight => _memorySignificanceBonusWeight;

    private static float _memoryMinimumRelevanceThreshold = 0.1f;

    /// <summary>
    /// Minimum relevance score for a memory to be considered relevant.
    /// Memories with scores below this threshold are filtered out.
    /// </summary>
    /// <remarks>
    /// Setting this above 0 prevents weakly-related memories from being returned.
    /// A value of 0.1 requires at least some meaningful connection (e.g., a category
    /// match alone would score 0.3, a single word overlap might score ~0.04).
    /// </remarks>
    public static float MemoryMinimumRelevanceThreshold => _memoryMinimumRelevanceThreshold;

    #endregion
}

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
    public float ThreatWeight { get; init; } = CognitionConstants.DefaultThreatWeight;

    /// <summary>
    /// Priority multiplier for novel/new perceptions.
    /// </summary>
    public float NoveltyWeight { get; init; } = CognitionConstants.DefaultNoveltyWeight;

    /// <summary>
    /// Priority multiplier for social perceptions.
    /// </summary>
    public float SocialWeight { get; init; } = CognitionConstants.DefaultSocialWeight;

    /// <summary>
    /// Priority multiplier for routine perceptions.
    /// </summary>
    public float RoutineWeight { get; init; } = CognitionConstants.DefaultRoutineWeight;

    /// <summary>
    /// Whether high-urgency threats should bypass the normal pipeline
    /// and go directly to intention formation (fight-or-flight response).
    /// </summary>
    /// <remarks>
    /// <para>
    /// When enabled, threat perceptions with urgency above <see cref="ThreatFastTrackThreshold"/>
    /// skip the normal cognition pipeline (significance assessment, memory formation) and
    /// go directly to Stage 5 (intention formation). This models the biological fight-or-flight
    /// response where immediate threats trigger instinctive reactions.
    /// </para>
    /// <para>
    /// IMPORTANT: Fast-tracked perceptions do NOT form memories through the normal process.
    /// If memory formation is important for a character type (e.g., strategists, leaders),
    /// set this to false to ensure full cognition processing.
    /// </para>
    /// <para>
    /// Defaults to true because immediate threat response is the typical NPC behavior.
    /// Set to false for characters that should remain calm under pressure.
    /// </para>
    /// </remarks>
    public bool ThreatFastTrack { get; init; } = true;

    /// <summary>
    /// Urgency threshold for threat fast-track (0-1).
    /// Perceptions with urgency above this value skip to Stage 5.
    /// </summary>
    public float ThreatFastTrackThreshold { get; init; } = CognitionConstants.DefaultThreatFastTrackThreshold;

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
    public float EmotionalWeight { get; init; } = CognitionConstants.DefaultEmotionalWeight;

    /// <summary>
    /// Weight for goal relevance factor.
    /// </summary>
    public float GoalRelevanceWeight { get; init; } = CognitionConstants.DefaultGoalRelevanceWeight;

    /// <summary>
    /// Weight for relationship factor.
    /// </summary>
    public float RelationshipWeight { get; init; } = CognitionConstants.DefaultRelationshipWeight;

    /// <summary>
    /// Threshold for storing memories (0-1).
    /// Perceptions with significance above this are stored.
    /// </summary>
    public float StorageThreshold { get; init; } = CognitionConstants.DefaultStorageThreshold;

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
    /// <remarks>
    /// Urgency bands:
    /// <list type="bullet">
    /// <item>Low (0 to &lt;0.3): Full deliberation - deeper search, longer timeout</item>
    /// <item>Medium (0.3 to &lt;0.7): Quick decision - moderate constraints</item>
    /// <item>High (0.7 to 1.0): Immediate reaction - shallow search, short timeout</item>
    /// </list>
    /// See <see cref="CognitionConstants"/> for threshold and parameter values.
    /// </remarks>
    public static UrgencyBasedPlanningOptions FromUrgency(float urgency)
    {
        // Low urgency: Full deliberation
        if (urgency < CognitionConstants.LowUrgencyThreshold)
        {
            return new UrgencyBasedPlanningOptions
            {
                MaxDepth = CognitionConstants.LowUrgencyMaxDepth,
                TimeoutMs = CognitionConstants.LowUrgencyTimeoutMs,
                MaxNodes = CognitionConstants.LowUrgencyMaxNodes
            };
        }

        // Medium urgency: Quick decision
        if (urgency < CognitionConstants.HighUrgencyThreshold)
        {
            return new UrgencyBasedPlanningOptions
            {
                MaxDepth = CognitionConstants.MediumUrgencyMaxDepth,
                TimeoutMs = CognitionConstants.MediumUrgencyTimeoutMs,
                MaxNodes = CognitionConstants.MediumUrgencyMaxNodes
            };
        }

        // High urgency: Immediate reaction
        return new UrgencyBasedPlanningOptions
        {
            MaxDepth = CognitionConstants.HighUrgencyMaxDepth,
            TimeoutMs = CognitionConstants.HighUrgencyTimeoutMs,
            MaxNodes = CognitionConstants.HighUrgencyMaxNodes
        };
    }

    /// <summary>
    /// Converts to GOAP PlanningOptions for the planner.
    /// </summary>
    /// <returns>GOAP planning options.</returns>
    public Goap.PlanningOptions ToPlanningOptions()
    {
        return new Goap.PlanningOptions
        {
            MaxDepth = MaxDepth,
            MaxNodesExpanded = MaxNodes,
            TimeoutMs = TimeoutMs
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
