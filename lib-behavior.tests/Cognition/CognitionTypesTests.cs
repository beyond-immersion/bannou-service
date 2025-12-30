// =============================================================================
// Cognition Types Unit Tests
// Tests for cognition pipeline types.
// =============================================================================

using BeyondImmersion.Bannou.Behavior.Cognition;
using Xunit;

namespace BeyondImmersion.BannouService.Behavior.Tests.Cognition;

/// <summary>
/// Tests for cognition pipeline types.
/// </summary>
public class CognitionTypesTests
{
    #region AttentionWeights Tests

    [Fact]
    public void AttentionWeights_DefaultValues_AreCorrect()
    {
        // Arrange & Act
        var weights = new AttentionWeights();

        // Assert
        Assert.Equal(10.0f, weights.ThreatWeight);
        Assert.Equal(5.0f, weights.NoveltyWeight);
        Assert.Equal(3.0f, weights.SocialWeight);
        Assert.Equal(1.0f, weights.RoutineWeight);
        Assert.False(weights.ThreatFastTrack);
        Assert.Equal(0.8f, weights.ThreatFastTrackThreshold);
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

    [Fact]
    public void SignificanceWeights_DefaultValues_AreCorrect()
    {
        // Arrange & Act
        var weights = new SignificanceWeights();

        // Assert
        Assert.Equal(0.4f, weights.EmotionalWeight);
        Assert.Equal(0.4f, weights.GoalRelevanceWeight);
        Assert.Equal(0.2f, weights.RelationshipWeight);
        Assert.Equal(0.7f, weights.StorageThreshold);
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

    [Theory]
    [InlineData(0.0f, 10, 100, 1000)]  // Low urgency
    [InlineData(0.2f, 10, 100, 1000)]  // Low urgency
    [InlineData(0.4f, 6, 50, 500)]     // Medium urgency
    [InlineData(0.6f, 6, 50, 500)]     // Medium urgency
    [InlineData(0.8f, 3, 20, 200)]     // High urgency
    [InlineData(1.0f, 3, 20, 200)]     // High urgency
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
        var memory = new Memory();

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
        var memory = new Memory
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
}
