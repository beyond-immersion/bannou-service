// =============================================================================
// Cognition Extensions for GOAP
// Bridges bannou-service cognition types to lib-behavior GOAP types.
// =============================================================================

using BeyondImmersion.BannouService.Abml.Cognition;
using BeyondImmersion.BannouService.Behavior;

namespace BeyondImmersion.Bannou.Behavior.Goap;

/// <summary>
/// Extension methods bridging cognition types to GOAP planning options.
/// </summary>
public static class CognitionExtensions
{
    /// <summary>
    /// Converts urgency-based planning options to GOAP planner options.
    /// </summary>
    /// <param name="options">The urgency-based planning options.</param>
    /// <returns>GOAP planning options.</returns>
    public static PlanningOptions ToPlanningOptions(this UrgencyBasedPlanningOptions options)
    {
        return new PlanningOptions
        {
            MaxDepth = options.MaxDepth,
            MaxNodesExpanded = options.MaxNodes,
            TimeoutMs = options.TimeoutMs
        };
    }

    /// <summary>
    /// Converts behavior service configuration to cognition configuration.
    /// </summary>
    /// <param name="config">The behavior service configuration.</param>
    /// <returns>Cognition configuration.</returns>
    public static CognitionConfiguration ToCognitionConfiguration(this BehaviorServiceConfiguration config)
    {
        return new CognitionConfiguration
        {
            LowUrgencyThreshold = config.LowUrgencyThreshold,
            HighUrgencyThreshold = config.HighUrgencyThreshold,
            LowUrgencyMaxPlanDepth = config.LowUrgencyMaxPlanDepth,
            LowUrgencyPlanTimeoutMs = config.LowUrgencyPlanTimeoutMs,
            LowUrgencyMaxPlanNodes = config.LowUrgencyMaxPlanNodes,
            MediumUrgencyMaxPlanDepth = config.MediumUrgencyMaxPlanDepth,
            MediumUrgencyPlanTimeoutMs = config.MediumUrgencyPlanTimeoutMs,
            MediumUrgencyMaxPlanNodes = config.MediumUrgencyMaxPlanNodes,
            HighUrgencyMaxPlanDepth = config.HighUrgencyMaxPlanDepth,
            HighUrgencyPlanTimeoutMs = config.HighUrgencyPlanTimeoutMs,
            HighUrgencyMaxPlanNodes = config.HighUrgencyMaxPlanNodes,
            DefaultThreatWeight = config.DefaultThreatWeight,
            DefaultNoveltyWeight = config.DefaultNoveltyWeight,
            DefaultSocialWeight = config.DefaultSocialWeight,
            DefaultRoutineWeight = config.DefaultRoutineWeight,
            DefaultThreatFastTrackThreshold = config.DefaultThreatFastTrackThreshold,
            DefaultEmotionalWeight = config.DefaultEmotionalWeight,
            DefaultGoalRelevanceWeight = config.DefaultGoalRelevanceWeight,
            DefaultRelationshipWeight = config.DefaultRelationshipWeight,
            DefaultStorageThreshold = config.DefaultStorageThreshold,
            MemoryMinimumRelevanceThreshold = config.MemoryMinimumRelevanceThreshold,
            MemoryCategoryMatchWeight = config.MemoryCategoryMatchWeight,
            MemoryContentOverlapWeight = config.MemoryContentOverlapWeight,
            MemoryMetadataOverlapWeight = config.MemoryMetadataOverlapWeight,
            MemoryRecencyBonusWeight = config.MemoryRecencyBonusWeight,
            MemorySignificanceBonusWeight = config.MemorySignificanceBonusWeight
        };
    }
}
