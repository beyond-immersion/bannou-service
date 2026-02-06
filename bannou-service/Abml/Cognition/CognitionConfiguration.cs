// =============================================================================
// Cognition Configuration
// Configuration for the cognition pipeline (decoupled from behavior service).
// =============================================================================

namespace BeyondImmersion.BannouService.Abml.Cognition;

/// <summary>
/// Configuration for the cognition pipeline.
/// </summary>
/// <remarks>
/// This class decouples cognition constants from the behavior service configuration,
/// allowing the cognition system to be used in bannou-service without plugin dependencies.
/// Services that use cognition should map their configuration to this class.
/// </remarks>
public sealed class CognitionConfiguration
{
    #region Urgency Thresholds

    /// <summary>
    /// Urgency threshold below which planning uses low-urgency parameters (full deliberation).
    /// Default: 0.3
    /// </summary>
    public double LowUrgencyThreshold { get; init; } = 0.3;

    /// <summary>
    /// Urgency threshold above which planning uses high-urgency parameters (immediate reaction).
    /// Default: 0.7
    /// </summary>
    public double HighUrgencyThreshold { get; init; } = 0.7;

    #endregion

    #region Planning Parameters - Low Urgency

    /// <summary>Maximum search depth for low-urgency planning.</summary>
    public int LowUrgencyMaxPlanDepth { get; init; } = 10;

    /// <summary>Timeout in milliseconds for low-urgency planning.</summary>
    public int LowUrgencyPlanTimeoutMs { get; init; } = 100;

    /// <summary>Maximum nodes to expand for low-urgency planning.</summary>
    public int LowUrgencyMaxPlanNodes { get; init; } = 1000;

    #endregion

    #region Planning Parameters - Medium Urgency

    /// <summary>Maximum search depth for medium-urgency planning.</summary>
    public int MediumUrgencyMaxPlanDepth { get; init; } = 6;

    /// <summary>Timeout in milliseconds for medium-urgency planning.</summary>
    public int MediumUrgencyPlanTimeoutMs { get; init; } = 50;

    /// <summary>Maximum nodes to expand for medium-urgency planning.</summary>
    public int MediumUrgencyMaxPlanNodes { get; init; } = 500;

    #endregion

    #region Planning Parameters - High Urgency

    /// <summary>Maximum search depth for high-urgency planning.</summary>
    public int HighUrgencyMaxPlanDepth { get; init; } = 3;

    /// <summary>Timeout in milliseconds for high-urgency planning.</summary>
    public int HighUrgencyPlanTimeoutMs { get; init; } = 20;

    /// <summary>Maximum nodes to expand for high-urgency planning.</summary>
    public int HighUrgencyMaxPlanNodes { get; init; } = 200;

    #endregion

    #region Attention Weights

    /// <summary>Default priority multiplier for threat perceptions.</summary>
    public double DefaultThreatWeight { get; init; } = 10.0;

    /// <summary>Default priority multiplier for novel perceptions.</summary>
    public double DefaultNoveltyWeight { get; init; } = 5.0;

    /// <summary>Default priority multiplier for social perceptions.</summary>
    public double DefaultSocialWeight { get; init; } = 3.0;

    /// <summary>Default priority multiplier for routine perceptions.</summary>
    public double DefaultRoutineWeight { get; init; } = 1.0;

    /// <summary>Default urgency threshold for threat fast-track.</summary>
    public double DefaultThreatFastTrackThreshold { get; init; } = 0.8;

    #endregion

    #region Significance Weights

    /// <summary>Default weight for emotional impact in significance scoring.</summary>
    public double DefaultEmotionalWeight { get; init; } = 0.4;

    /// <summary>Default weight for goal relevance in significance scoring.</summary>
    public double DefaultGoalRelevanceWeight { get; init; } = 0.4;

    /// <summary>Default weight for relationship factor in significance scoring.</summary>
    public double DefaultRelationshipWeight { get; init; } = 0.2;

    /// <summary>Default threshold for storing memories based on significance.</summary>
    public double DefaultStorageThreshold { get; init; } = 0.7;

    #endregion

    #region Memory Relevance

    /// <summary>Minimum relevance score for a memory to be considered relevant.</summary>
    public double MemoryMinimumRelevanceThreshold { get; init; } = 0.1;

    /// <summary>Weight for category match in memory relevance scoring.</summary>
    public double MemoryCategoryMatchWeight { get; init; } = 0.3;

    /// <summary>Weight for content keyword overlap in memory relevance scoring.</summary>
    public double MemoryContentOverlapWeight { get; init; } = 0.4;

    /// <summary>Weight for metadata key overlap in memory relevance scoring.</summary>
    public double MemoryMetadataOverlapWeight { get; init; } = 0.2;

    /// <summary>Maximum recency bonus for memories less than 1 hour old.</summary>
    public double MemoryRecencyBonusWeight { get; init; } = 0.1;

    /// <summary>Weight for memory significance bonus.</summary>
    public double MemorySignificanceBonusWeight { get; init; } = 0.1;

    #endregion

    /// <summary>
    /// Creates a default configuration with all default values.
    /// </summary>
    public static CognitionConfiguration Default { get; } = new();
}
