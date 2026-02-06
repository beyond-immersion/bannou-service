// =============================================================================
// Cognition Types Unit Tests
// Tests for cognition pipeline types.
// =============================================================================

using BeyondImmersion.BannouService.Abml.Cognition;
using Xunit;

// Alias to avoid conflict with System.Memory<T>
using CognitionMemory = BeyondImmersion.BannouService.Abml.Cognition.Memory;

namespace BeyondImmersion.BannouService.Behavior.Tests.Cognition;

/// <summary>
/// Tests for cognition pipeline types.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Test-Implementation Coupling:</strong> Several tests in this class verify default values
/// and behavior that are defined by <see cref="CognitionConstants"/>. If those constant values change,
/// the corresponding tests must be updated to match. Specifically:
/// </para>
/// <list type="bullet">
/// <item><c>AttentionWeights_DefaultValues_AreCorrect</c> - coupled to Default*Weight constants</item>
/// <item><c>SignificanceWeights_DefaultValues_AreCorrect</c> - coupled to Default*Weight constants</item>
/// <item><c>UrgencyBasedPlanningOptions_FromUrgency_MapsCorrectly</c> - coupled to urgency thresholds and planning parameters</item>
/// </list>
/// <para>
/// The InlineData values for urgency tests use threshold boundaries (0.0, 0.2, 0.4, 0.6, 0.8, 1.0)
/// that correspond to the urgency bands: Low (&lt;0.3), Medium (0.3-0.7), High (&gt;=0.7).
/// </para>
/// </remarks>
public class CognitionTypesTests
{
    #region AttentionWeights Tests

    /// <summary>
    /// Verifies AttentionWeights defaults match <see cref="CognitionConstants"/>.
    /// ThreatFastTrack defaults to true (fight-or-flight is typical NPC behavior).
    /// </summary>
    [Fact]
    public void AttentionWeights_DefaultValues_AreCorrect()
    {
        // Arrange & Act
        var weights = new AttentionWeights();

        // Assert - values should match CognitionConstants.Default* values
        Assert.Equal(CognitionConstants.DefaultThreatWeight, weights.ThreatWeight);
        Assert.Equal(CognitionConstants.DefaultNoveltyWeight, weights.NoveltyWeight);
        Assert.Equal(CognitionConstants.DefaultSocialWeight, weights.SocialWeight);
        Assert.Equal(CognitionConstants.DefaultRoutineWeight, weights.RoutineWeight);
        Assert.True(weights.ThreatFastTrack); // Default true: fight-or-flight is typical
        Assert.Equal(CognitionConstants.DefaultThreatFastTrackThreshold, weights.ThreatFastTrackThreshold);
    }

    [Theory]
    [InlineData("threat", 10.0f)]
    [InlineData("Threat", 10.0f)]
    [InlineData("THREAT", 10.0f)]
    [InlineData("novelty", 5.0f)]
    [InlineData("social", 3.0f)]
    [InlineData("routine", 1.0f)]
    [InlineData("unknown", 1.0f)]
    public void AttentionWeights_GetWeight_ReturnsCategoryWeight(string category, float expected)
    {
        // Arrange
        var weights = new AttentionWeights();

        // Act
        var result = weights.GetWeight(category);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void AttentionWeights_CustomValues_AreUsed()
    {
        // Arrange & Act
        var weights = new AttentionWeights
        {
            ThreatWeight = 20.0f,
            NoveltyWeight = 8.0f,
            ThreatFastTrack = true,
            ThreatFastTrackThreshold = 0.6f
        };

        // Assert
        Assert.Equal(20.0f, weights.GetWeight("threat"));
        Assert.Equal(8.0f, weights.GetWeight("novelty"));
        Assert.True(weights.ThreatFastTrack);
        Assert.Equal(0.6f, weights.ThreatFastTrackThreshold);
    }

    #endregion

    #region AttentionBudget Tests

    [Fact]
    public void AttentionBudget_DefaultValues_AreCorrect()
    {
        // Arrange & Act
        var budget = new AttentionBudget();

        // Assert
        Assert.Equal(100f, budget.TotalUnits);
        Assert.Equal(10, budget.MaxPerceptions);
        Assert.Empty(budget.CategoryReservations);
    }

    [Fact]
    public void AttentionBudget_CustomValues_AreUsed()
    {
        // Arrange & Act
        var budget = new AttentionBudget
        {
            TotalUnits = 50f,
            MaxPerceptions = 5,
            CategoryReservations = new Dictionary<string, float>
            {
                { "threat", 20f }
            }
        };

        // Assert
        Assert.Equal(50f, budget.TotalUnits);
        Assert.Equal(5, budget.MaxPerceptions);
        Assert.Single(budget.CategoryReservations);
    }

    #endregion

    #region SignificanceWeights Tests

    /// <summary>
    /// Verifies SignificanceWeights defaults match <see cref="CognitionConstants"/>.
    /// </summary>
    [Fact]
    public void SignificanceWeights_DefaultValues_AreCorrect()
    {
        // Arrange & Act
        var weights = new SignificanceWeights();

        // Assert - values should match CognitionConstants.Default* values
        Assert.Equal(CognitionConstants.DefaultEmotionalWeight, weights.EmotionalWeight);
        Assert.Equal(CognitionConstants.DefaultGoalRelevanceWeight, weights.GoalRelevanceWeight);
        Assert.Equal(CognitionConstants.DefaultRelationshipWeight, weights.RelationshipWeight);
        Assert.Equal(CognitionConstants.DefaultStorageThreshold, weights.StorageThreshold);
    }

    [Theory]
    [InlineData(1.0f, 0.5f, 0.3f, 0.66f)] // (1.0*0.4 + 0.5*0.4 + 0.3*0.2) = 0.4+0.2+0.06
    [InlineData(0.0f, 0.0f, 0.0f, 0.0f)]
    [InlineData(1.0f, 1.0f, 1.0f, 1.0f)]
    [InlineData(0.5f, 0.5f, 0.5f, 0.5f)]
    public void SignificanceWeights_ComputeScore_CalculatesWeightedAverage(
        float emotional, float goalRelevance, float relationship, float expected)
    {
        // Arrange
        var weights = new SignificanceWeights();

        // Act
        var result = weights.ComputeScore(emotional, goalRelevance, relationship);

        // Assert
        Assert.Equal(expected, result, 2);
    }

    [Fact]
    public void SignificanceWeights_ComputeScore_ZeroWeights_ReturnsZero()
    {
        // Arrange
        var weights = new SignificanceWeights
        {
            EmotionalWeight = 0f,
            GoalRelevanceWeight = 0f,
            RelationshipWeight = 0f
        };

        // Act
        var result = weights.ComputeScore(1.0f, 1.0f, 1.0f);

        // Assert
        Assert.Equal(0f, result);
    }

    #endregion

    #region SignificanceScore Tests

    [Fact]
    public void SignificanceScore_ShouldStore_TrueWhenAboveThreshold()
    {
        // Arrange & Act
        var score = new SignificanceScore
        {
            TotalScore = 0.8f,
            StorageThreshold = 0.7f
        };

        // Assert
        Assert.True(score.ShouldStore);
    }

    [Fact]
    public void SignificanceScore_ShouldStore_FalseWhenBelowThreshold()
    {
        // Arrange & Act
        var score = new SignificanceScore
        {
            TotalScore = 0.6f,
            StorageThreshold = 0.7f
        };

        // Assert
        Assert.False(score.ShouldStore);
    }

    [Fact]
    public void SignificanceScore_ShouldStore_TrueWhenEqualToThreshold()
    {
        // Arrange & Act
        var score = new SignificanceScore
        {
            TotalScore = 0.7f,
            StorageThreshold = 0.7f
        };

        // Assert
        Assert.True(score.ShouldStore);
    }

    #endregion

    #region UrgencyBasedPlanningOptions Tests

    /// <summary>
    /// Verifies urgency-to-planning mapping uses correct thresholds from <see cref="CognitionConstants"/>.
    /// </summary>
    /// <remarks>
    /// <para>Urgency bands (see <see cref="CognitionConstants"/>):</para>
    /// <list type="bullet">
    /// <item>Low (&lt;0.3): Full deliberation - MaxDepth=10, TimeoutMs=100, MaxNodes=1000</item>
    /// <item>Medium (0.3-0.7): Quick decision - MaxDepth=6, TimeoutMs=50, MaxNodes=500</item>
    /// <item>High (&gt;=0.7): Immediate reaction - MaxDepth=3, TimeoutMs=20, MaxNodes=200</item>
    /// </list>
    /// <para>
    /// <strong>IMPORTANT:</strong> The InlineData values are coupled to <see cref="CognitionConstants"/>.
    /// If threshold or parameter constants change, these test cases must be updated.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(0.0f, 10, 100, 1000)]  // Low urgency: below LowUrgencyThreshold (0.3)
    [InlineData(0.2f, 10, 100, 1000)]  // Low urgency: below LowUrgencyThreshold (0.3)
    [InlineData(0.4f, 6, 50, 500)]     // Medium urgency: >= 0.3, < 0.7
    [InlineData(0.6f, 6, 50, 500)]     // Medium urgency: >= 0.3, < 0.7
    [InlineData(0.8f, 3, 20, 200)]     // High urgency: >= HighUrgencyThreshold (0.7)
    [InlineData(1.0f, 3, 20, 200)]     // High urgency: >= HighUrgencyThreshold (0.7)
    public void UrgencyBasedPlanningOptions_FromUrgency_MapsCorrectly(
        float urgency, int expectedMaxDepth, int expectedTimeoutMs, int expectedMaxNodes)
    {
        // Act
        var options = UrgencyBasedPlanningOptions.FromUrgency(urgency);

        // Assert
        Assert.Equal(expectedMaxDepth, options.MaxDepth);
        Assert.Equal(expectedTimeoutMs, options.TimeoutMs);
        Assert.Equal(expectedMaxNodes, options.MaxNodes);
    }

    /// <summary>
    /// Verifies urgency threshold boundaries are applied correctly.
    /// This test uses the actual constants to verify edge cases.
    /// </summary>
    [Fact]
    public void UrgencyBasedPlanningOptions_ThresholdBoundaries_ApplyCorrectly()
    {
        // Just below low threshold - should be low urgency
        var justBelowLow = UrgencyBasedPlanningOptions.FromUrgency(
            CognitionConstants.LowUrgencyThreshold - 0.01f);
        Assert.Equal(CognitionConstants.LowUrgencyMaxDepth, justBelowLow.MaxDepth);

        // At low threshold - should be medium urgency
        var atLowThreshold = UrgencyBasedPlanningOptions.FromUrgency(
            CognitionConstants.LowUrgencyThreshold);
        Assert.Equal(CognitionConstants.MediumUrgencyMaxDepth, atLowThreshold.MaxDepth);

        // Just below high threshold - should still be medium urgency
        var justBelowHigh = UrgencyBasedPlanningOptions.FromUrgency(
            CognitionConstants.HighUrgencyThreshold - 0.01f);
        Assert.Equal(CognitionConstants.MediumUrgencyMaxDepth, justBelowHigh.MaxDepth);

        // At high threshold - should be high urgency
        var atHighThreshold = UrgencyBasedPlanningOptions.FromUrgency(
            CognitionConstants.HighUrgencyThreshold);
        Assert.Equal(CognitionConstants.HighUrgencyMaxDepth, atHighThreshold.MaxDepth);
    }

    #endregion

    #region Perception Tests

    [Fact]
    public void Perception_FromDictionary_CreatesPerception()
    {
        // Arrange
        var data = new Dictionary<string, object?>
        {
            { "id", "perception-123" },
            { "category", "threat" },
            { "content", "Enemy spotted" },
            { "urgency", 0.9f },
            { "source", "enemy-1" }
        };

        // Act
        var perception = Perception.FromDictionary(data);

        // Assert
        Assert.Equal("perception-123", perception.Id);
        Assert.Equal("threat", perception.Category);
        Assert.Equal("Enemy spotted", perception.Content);
        Assert.Equal(0.9f, perception.Urgency);
        Assert.Equal("enemy-1", perception.Source);
    }

    [Fact]
    public void Perception_FromDictionary_UsesDefaults_WhenMissing()
    {
        // Arrange
        var data = new Dictionary<string, object?>
        {
            { "content", "Something happened" }
        };

        // Act
        var perception = Perception.FromDictionary(data);

        // Assert
        Assert.NotEmpty(perception.Id); // Generated GUID
        Assert.Equal("routine", perception.Category);
        Assert.Equal("Something happened", perception.Content);
        Assert.Equal(0f, perception.Urgency);
        Assert.Empty(perception.Source);
    }

    #endregion

    #region GoalImpactResult Tests

    [Fact]
    public void GoalImpactResult_DefaultValues()
    {
        // Arrange & Act
        var result = new GoalImpactResult();

        // Assert
        Assert.False(result.RequiresReplan);
        Assert.Empty(result.AffectedGoals);
        Assert.Equal(0f, result.Urgency);
        Assert.Null(result.Message);
    }

    [Fact]
    public void GoalImpactResult_CustomValues()
    {
        // Arrange & Act
        var result = new GoalImpactResult
        {
            RequiresReplan = true,
            AffectedGoals = ["goal-1", "goal-2"],
            Urgency = 0.8f,
            Message = "Threat detected"
        };

        // Assert
        Assert.True(result.RequiresReplan);
        Assert.Equal(2, result.AffectedGoals.Count);
        Assert.Equal(0.8f, result.Urgency);
        Assert.Equal("Threat detected", result.Message);
    }

    #endregion

    #region FilteredPerceptionsResult Tests

    [Fact]
    public void FilteredPerceptionsResult_DefaultValues()
    {
        // Arrange & Act
        var result = new FilteredPerceptionsResult();

        // Assert
        Assert.Empty(result.FilteredPerceptions);
        Assert.Empty(result.FastTrackPerceptions);
        Assert.Empty(result.DroppedPerceptions);
        Assert.Equal(0f, result.RemainingBudget);
    }

    #endregion

    #region Memory Tests

    [Fact]
    public void Memory_DefaultValues()
    {
        // Arrange & Act
        var memory = new CognitionMemory();

        // Assert
        Assert.Empty(memory.Id);
        Assert.Empty(memory.EntityId);
        Assert.Empty(memory.Content);
        Assert.Empty(memory.Category);
        Assert.Equal(0f, memory.Significance);
        Assert.Empty(memory.Metadata);
        Assert.Empty(memory.RelatedMemoryIds);
    }

    [Fact]
    public void Memory_CustomValues()
    {
        // Arrange & Act
        var memory = new CognitionMemory
        {
            Id = "mem-123",
            EntityId = "entity-456",
            Content = "Saw an enemy",
            Category = "threat",
            Significance = 0.9f,
            Timestamp = DateTimeOffset.UtcNow,
            Metadata = new Dictionary<string, object> { { "enemy_type", "goblin" } },
            RelatedMemoryIds = ["mem-100", "mem-101"]
        };

        // Assert
        Assert.Equal("mem-123", memory.Id);
        Assert.Equal("entity-456", memory.EntityId);
        Assert.Equal("Saw an enemy", memory.Content);
        Assert.Equal("threat", memory.Category);
        Assert.Equal(0.9f, memory.Significance);
        Assert.Single(memory.Metadata);
        Assert.Equal(2, memory.RelatedMemoryIds.Count);
    }

    #endregion

    #region CognitionConstants Initialization Tests

    /// <summary>
    /// Verifies that CognitionConstants.Initialize() applies configuration values.
    /// </summary>
    [Fact]
    public void CognitionConstants_Initialize_AppliesConfigurationValues()
    {
        // Arrange
        CognitionConstants.Reset(); // Ensure clean state

        var config = new CognitionConfiguration
        {
            LowUrgencyThreshold = 0.25f,
            HighUrgencyThreshold = 0.75f,
            LowUrgencyMaxPlanDepth = 12,
            LowUrgencyPlanTimeoutMs = 150,
            LowUrgencyMaxPlanNodes = 1200,
            MediumUrgencyMaxPlanDepth = 8,
            MediumUrgencyPlanTimeoutMs = 75,
            MediumUrgencyMaxPlanNodes = 600,
            HighUrgencyMaxPlanDepth = 4,
            HighUrgencyPlanTimeoutMs = 30,
            HighUrgencyMaxPlanNodes = 250,
            DefaultThreatWeight = 12.0f,
            DefaultNoveltyWeight = 6.0f,
            DefaultSocialWeight = 4.0f,
            DefaultRoutineWeight = 1.5f,
            DefaultThreatFastTrackThreshold = 0.85f,
            DefaultEmotionalWeight = 0.5f,
            DefaultGoalRelevanceWeight = 0.35f,
            DefaultRelationshipWeight = 0.15f,
            MemoryMinimumRelevanceThreshold = 0.15f,
            DefaultStorageThreshold = 0.65f,
            MemoryCategoryMatchWeight = 0.35f,
            MemoryContentOverlapWeight = 0.45f,
            MemoryMetadataOverlapWeight = 0.25f,
            MemoryRecencyBonusWeight = 0.15f,
            MemorySignificanceBonusWeight = 0.12f
        };

        // Act
        CognitionConstants.Initialize(config);

        // Assert - Urgency thresholds
        Assert.Equal(0.25f, CognitionConstants.LowUrgencyThreshold);
        Assert.Equal(0.75f, CognitionConstants.HighUrgencyThreshold);

        // Assert - Low urgency planning
        Assert.Equal(12, CognitionConstants.LowUrgencyMaxDepth);
        Assert.Equal(150, CognitionConstants.LowUrgencyTimeoutMs);
        Assert.Equal(1200, CognitionConstants.LowUrgencyMaxNodes);

        // Assert - Medium urgency planning
        Assert.Equal(8, CognitionConstants.MediumUrgencyMaxDepth);
        Assert.Equal(75, CognitionConstants.MediumUrgencyTimeoutMs);
        Assert.Equal(600, CognitionConstants.MediumUrgencyMaxNodes);

        // Assert - High urgency planning
        Assert.Equal(4, CognitionConstants.HighUrgencyMaxDepth);
        Assert.Equal(30, CognitionConstants.HighUrgencyTimeoutMs);
        Assert.Equal(250, CognitionConstants.HighUrgencyMaxNodes);

        // Assert - Attention weights
        Assert.Equal(12.0f, CognitionConstants.DefaultThreatWeight);
        Assert.Equal(6.0f, CognitionConstants.DefaultNoveltyWeight);
        Assert.Equal(4.0f, CognitionConstants.DefaultSocialWeight);
        Assert.Equal(1.5f, CognitionConstants.DefaultRoutineWeight);
        Assert.Equal(0.85f, CognitionConstants.DefaultThreatFastTrackThreshold);

        // Assert - Significance weights
        Assert.Equal(0.5f, CognitionConstants.DefaultEmotionalWeight);
        Assert.Equal(0.35f, CognitionConstants.DefaultGoalRelevanceWeight);
        Assert.Equal(0.15f, CognitionConstants.DefaultRelationshipWeight);

        // Assert - Memory relevance weights
        Assert.Equal(0.15f, CognitionConstants.MemoryMinimumRelevanceThreshold);
        Assert.Equal(0.65f, CognitionConstants.DefaultStorageThreshold);
        Assert.Equal(0.35f, CognitionConstants.MemoryCategoryMatchWeight);
        Assert.Equal(0.45f, CognitionConstants.MemoryContentOverlapWeight);
        Assert.Equal(0.25f, CognitionConstants.MemoryMetadataOverlapWeight);
        Assert.Equal(0.15f, CognitionConstants.MemoryRecencyBonusWeight);
        Assert.Equal(0.12f, CognitionConstants.MemorySignificanceBonusWeight);

        Assert.True(CognitionConstants.IsInitialized);

        // Cleanup
        CognitionConstants.Reset();
    }

    /// <summary>
    /// Verifies that CognitionConstants.Reset() restores default values.
    /// </summary>
    [Fact]
    public void CognitionConstants_Reset_RestoresDefaultValues()
    {
        // Arrange - Initialize with non-default values
        CognitionConstants.Reset();
        var config = new CognitionConfiguration
        {
            LowUrgencyThreshold = 0.5f,
            HighUrgencyThreshold = 0.9f,
            DefaultThreatWeight = 99.0f,
            DefaultStorageThreshold = 0.99f,
            MemoryCategoryMatchWeight = 0.99f
        };
        CognitionConstants.Initialize(config);

        // Act
        CognitionConstants.Reset();

        // Assert - values should be back to defaults
        Assert.Equal(0.3f, CognitionConstants.LowUrgencyThreshold);
        Assert.Equal(0.7f, CognitionConstants.HighUrgencyThreshold);
        Assert.Equal(10.0f, CognitionConstants.DefaultThreatWeight);
        Assert.Equal(0.7f, CognitionConstants.DefaultStorageThreshold);
        Assert.Equal(0.3f, CognitionConstants.MemoryCategoryMatchWeight);
        Assert.False(CognitionConstants.IsInitialized);
    }

    /// <summary>
    /// Verifies that CognitionConstants.Initialize() is idempotent - subsequent calls are ignored.
    /// </summary>
    [Fact]
    public void CognitionConstants_Initialize_IsIdempotent()
    {
        // Arrange
        CognitionConstants.Reset();
        var config1 = new CognitionConfiguration
        {
            DefaultThreatWeight = 15.0f,
            DefaultStorageThreshold = 0.8f
        };
        var config2 = new CognitionConfiguration
        {
            DefaultThreatWeight = 25.0f,
            DefaultStorageThreshold = 0.5f
        };

        // Act - First initialization
        CognitionConstants.Initialize(config1);
        var threatWeightAfterFirst = CognitionConstants.DefaultThreatWeight;
        var storageThresholdAfterFirst = CognitionConstants.DefaultStorageThreshold;

        // Second initialization should be ignored
        CognitionConstants.Initialize(config2);

        // Assert - values should match first config, not second
        Assert.Equal(threatWeightAfterFirst, CognitionConstants.DefaultThreatWeight);
        Assert.Equal(storageThresholdAfterFirst, CognitionConstants.DefaultStorageThreshold);
        Assert.Equal(15.0f, CognitionConstants.DefaultThreatWeight);
        Assert.Equal(0.8f, CognitionConstants.DefaultStorageThreshold);

        // Cleanup
        CognitionConstants.Reset();
    }

    /// <summary>
    /// Verifies that CognitionConstants works with default config values (no explicit settings).
    /// </summary>
    [Fact]
    public void CognitionConstants_Initialize_WithDefaultConfig_UsesSchemaDefaults()
    {
        // Arrange
        CognitionConstants.Reset();
        var config = new CognitionConfiguration(); // All defaults

        // Act
        CognitionConstants.Initialize(config);

        // Assert - values should match schema defaults
        Assert.Equal(0.3f, CognitionConstants.LowUrgencyThreshold);
        Assert.Equal(0.7f, CognitionConstants.HighUrgencyThreshold);
        Assert.Equal(10, CognitionConstants.LowUrgencyMaxDepth);
        Assert.Equal(100, CognitionConstants.LowUrgencyTimeoutMs);
        Assert.Equal(1000, CognitionConstants.LowUrgencyMaxNodes);
        Assert.Equal(10.0f, CognitionConstants.DefaultThreatWeight);
        Assert.Equal(0.4f, CognitionConstants.DefaultEmotionalWeight);
        Assert.Equal(0.7f, CognitionConstants.DefaultStorageThreshold);
        Assert.Equal(0.3f, CognitionConstants.MemoryCategoryMatchWeight);
        Assert.True(CognitionConstants.IsInitialized);

        // Cleanup
        CognitionConstants.Reset();
    }

    /// <summary>
    /// Verifies that default values are usable without calling Initialize().
    /// </summary>
    [Fact]
    public void CognitionConstants_WithoutInitialize_HasValidDefaults()
    {
        // Arrange
        CognitionConstants.Reset();

        // Act & Assert - should have valid defaults even without Initialize()
        Assert.Equal(0.3f, CognitionConstants.LowUrgencyThreshold);
        Assert.Equal(0.7f, CognitionConstants.HighUrgencyThreshold);
        Assert.Equal(10.0f, CognitionConstants.DefaultThreatWeight);
        Assert.Equal(5.0f, CognitionConstants.DefaultNoveltyWeight);
        Assert.Equal(3.0f, CognitionConstants.DefaultSocialWeight);
        Assert.Equal(1.0f, CognitionConstants.DefaultRoutineWeight);
        Assert.Equal(0.7f, CognitionConstants.DefaultStorageThreshold);
        Assert.Equal(0.3f, CognitionConstants.MemoryCategoryMatchWeight);
        Assert.Equal(0.4f, CognitionConstants.MemoryContentOverlapWeight);
        Assert.Equal(0.2f, CognitionConstants.MemoryMetadataOverlapWeight);
        Assert.Equal(0.1f, CognitionConstants.MemoryRecencyBonusWeight);
        Assert.Equal(0.1f, CognitionConstants.MemorySignificanceBonusWeight);
        Assert.False(CognitionConstants.IsInitialized);
    }

    #endregion
}
