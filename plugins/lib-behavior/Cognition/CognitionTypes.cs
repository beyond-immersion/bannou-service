// =============================================================================
// Cognition Types Type Forwarding
// Types moved to bannou-service/Abml/Cognition/ per Issue #288.
// This file maintains backwards compatibility by re-exporting the types.
// =============================================================================

// Re-export types from bannou-service for backwards compatibility
global using AttentionBudget = BeyondImmersion.BannouService.Abml.Cognition.AttentionBudget;
global using AttentionWeights = BeyondImmersion.BannouService.Abml.Cognition.AttentionWeights;
global using SignificanceWeights = BeyondImmersion.BannouService.Abml.Cognition.SignificanceWeights;
global using SignificanceScore = BeyondImmersion.BannouService.Abml.Cognition.SignificanceScore;
global using GoalImpactResult = BeyondImmersion.BannouService.Abml.Cognition.GoalImpactResult;
global using UrgencyBasedPlanningOptions = BeyondImmersion.BannouService.Abml.Cognition.UrgencyBasedPlanningOptions;
global using FilteredPerceptionsResult = BeyondImmersion.BannouService.Abml.Cognition.FilteredPerceptionsResult;
global using CognitionConfiguration = BeyondImmersion.BannouService.Abml.Cognition.CognitionConfiguration;

using BeyondImmersion.BannouService.Abml.Cognition;

namespace BeyondImmersion.Bannou.Behavior.Cognition;

/// <summary>
/// Legacy static class that forwards to the new CognitionConstants location.
/// </summary>
/// <remarks>
/// CognitionConstants has been moved to BeyondImmersion.BannouService.Abml.Cognition.
/// This class provides backwards compatibility by forwarding to the new location.
/// New code should use BeyondImmersion.BannouService.Abml.Cognition.CognitionConstants directly.
/// </remarks>
public static class CognitionConstants
{
    // Forward all properties to the new location
    public static bool IsInitialized => BannouService.Abml.Cognition.CognitionConstants.IsInitialized;

    /// <summary>
    /// Initializes cognition constants from BehaviorServiceConfiguration.
    /// Maps the configuration to CognitionConfiguration and calls the new Initialize method.
    /// </summary>
    public static void Initialize(BeyondImmersion.BannouService.Behavior.BehaviorServiceConfiguration config)
    {
        var cognitionConfig = new CognitionConfiguration
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

        BannouService.Abml.Cognition.CognitionConstants.Initialize(cognitionConfig);
    }

    internal static void Reset() => BannouService.Abml.Cognition.CognitionConstants.Reset();

    // Urgency thresholds
    public static float LowUrgencyThreshold => BannouService.Abml.Cognition.CognitionConstants.LowUrgencyThreshold;
    public static float HighUrgencyThreshold => BannouService.Abml.Cognition.CognitionConstants.HighUrgencyThreshold;

    // Low urgency planning
    public static int LowUrgencyMaxDepth => BannouService.Abml.Cognition.CognitionConstants.LowUrgencyMaxDepth;
    public static int LowUrgencyTimeoutMs => BannouService.Abml.Cognition.CognitionConstants.LowUrgencyTimeoutMs;
    public static int LowUrgencyMaxNodes => BannouService.Abml.Cognition.CognitionConstants.LowUrgencyMaxNodes;

    // Medium urgency planning
    public static int MediumUrgencyMaxDepth => BannouService.Abml.Cognition.CognitionConstants.MediumUrgencyMaxDepth;
    public static int MediumUrgencyTimeoutMs => BannouService.Abml.Cognition.CognitionConstants.MediumUrgencyTimeoutMs;
    public static int MediumUrgencyMaxNodes => BannouService.Abml.Cognition.CognitionConstants.MediumUrgencyMaxNodes;

    // High urgency planning
    public static int HighUrgencyMaxDepth => BannouService.Abml.Cognition.CognitionConstants.HighUrgencyMaxDepth;
    public static int HighUrgencyTimeoutMs => BannouService.Abml.Cognition.CognitionConstants.HighUrgencyTimeoutMs;
    public static int HighUrgencyMaxNodes => BannouService.Abml.Cognition.CognitionConstants.HighUrgencyMaxNodes;

    // Attention weights
    public static float DefaultThreatWeight => BannouService.Abml.Cognition.CognitionConstants.DefaultThreatWeight;
    public static float DefaultNoveltyWeight => BannouService.Abml.Cognition.CognitionConstants.DefaultNoveltyWeight;
    public static float DefaultSocialWeight => BannouService.Abml.Cognition.CognitionConstants.DefaultSocialWeight;
    public static float DefaultRoutineWeight => BannouService.Abml.Cognition.CognitionConstants.DefaultRoutineWeight;
    public static float DefaultThreatFastTrackThreshold => BannouService.Abml.Cognition.CognitionConstants.DefaultThreatFastTrackThreshold;

    // Significance weights
    public static float DefaultEmotionalWeight => BannouService.Abml.Cognition.CognitionConstants.DefaultEmotionalWeight;
    public static float DefaultGoalRelevanceWeight => BannouService.Abml.Cognition.CognitionConstants.DefaultGoalRelevanceWeight;
    public static float DefaultRelationshipWeight => BannouService.Abml.Cognition.CognitionConstants.DefaultRelationshipWeight;
    public static float DefaultStorageThreshold => BannouService.Abml.Cognition.CognitionConstants.DefaultStorageThreshold;

    // Memory relevance
    public static float MemoryCategoryMatchWeight => BannouService.Abml.Cognition.CognitionConstants.MemoryCategoryMatchWeight;
    public static float MemoryContentOverlapWeight => BannouService.Abml.Cognition.CognitionConstants.MemoryContentOverlapWeight;
    public static float MemoryMetadataOverlapWeight => BannouService.Abml.Cognition.CognitionConstants.MemoryMetadataOverlapWeight;
    public static float MemoryRecencyBonusWeight => BannouService.Abml.Cognition.CognitionConstants.MemoryRecencyBonusWeight;
    public static float MemorySignificanceBonusWeight => BannouService.Abml.Cognition.CognitionConstants.MemorySignificanceBonusWeight;
    public static float MemoryMinimumRelevanceThreshold => BannouService.Abml.Cognition.CognitionConstants.MemoryMinimumRelevanceThreshold;
}

// Type aliases for backwards compatibility - types are now in bannou-service
// Note: These are type aliases, not using directives, to allow existing code to work
// New code should use BeyondImmersion.BannouService.Abml.Cognition types directly.

/// <summary>
/// Extension methods for GOAP integration with cognition types.
/// </summary>
public static class UrgencyBasedPlanningOptionsExtensions
{
    /// <summary>
    /// Converts UrgencyBasedPlanningOptions to GOAP PlanningOptions for the planner.
    /// </summary>
    /// <param name="options">The urgency-based planning options.</param>
    /// <returns>GOAP planning options.</returns>
    public static Goap.PlanningOptions ToPlanningOptions(
        this BeyondImmersion.BannouService.Abml.Cognition.UrgencyBasedPlanningOptions options)
    {
        return new Goap.PlanningOptions
        {
            MaxDepth = options.MaxDepth,
            MaxNodesExpanded = options.MaxNodes,
            TimeoutMs = options.TimeoutMs
        };
    }
}
