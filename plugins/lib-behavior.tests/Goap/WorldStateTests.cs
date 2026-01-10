// ═══════════════════════════════════════════════════════════════════════════
// WorldState Unit Tests
// Tests for GOAP world state container.
// ═══════════════════════════════════════════════════════════════════════════

using BeyondImmersion.Bannou.Behavior.Goap;
using Xunit;

using InternalGoapGoal = BeyondImmersion.Bannou.Behavior.Goap.GoapGoal;

namespace BeyondImmersion.BannouService.Behavior.Tests.Goap;

/// <summary>
/// Tests for WorldState immutable key-value container.
/// </summary>
public class WorldStateTests
{
    #region Construction and Basic Operations

    [Fact]
    public void Constructor_CreatesEmptyState()
    {
        // Arrange & Act
        var state = new WorldState();

        // Assert
        Assert.Equal(0, state.Count);
    }

    [Fact]
    public void FromDictionary_CreatesStateWithValues()
    {
        // Arrange
        var values = new Dictionary<string, object>
        {
            { "hunger", 0.5f },
            { "has_weapon", true },
            { "location", "tavern" }
        };

        // Act
        var state = WorldState.FromDictionary(values);

        // Assert
        Assert.Equal(3, state.Count);
        Assert.Equal(0.5f, state.GetNumeric("hunger"));
        Assert.True(state.GetBoolean("has_weapon"));
        Assert.Equal("tavern", state.GetString("location"));
    }

    #endregion

    #region Get Operations

    [Fact]
    public void GetNumeric_ReturnsValue_WhenExists()
    {
        // Arrange
        var state = new WorldState().SetNumeric("health", 75.5f);

        // Act
        var result = state.GetNumeric("health");

        // Assert
        Assert.Equal(75.5f, result);
    }

    [Fact]
    public void GetNumeric_ReturnsDefault_WhenNotExists()
    {
        // Arrange
        var state = new WorldState();

        // Act
        var result = state.GetNumeric("missing", 100f);

        // Assert
        Assert.Equal(100f, result);
    }

    [Fact]
    public void GetBoolean_ReturnsValue_WhenExists()
    {
        // Arrange
        var state = new WorldState().SetBoolean("is_alive", true);

        // Act
        var result = state.GetBoolean("is_alive");

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void GetBoolean_ReturnsDefault_WhenNotExists()
    {
        // Arrange
        var state = new WorldState();

        // Act
        var result = state.GetBoolean("missing", true);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void GetString_ReturnsValue_WhenExists()
    {
        // Arrange
        var state = new WorldState().SetString("name", "player");

        // Act
        var result = state.GetString("name");

        // Assert
        Assert.Equal("player", result);
    }

    [Fact]
    public void GetString_ReturnsDefault_WhenNotExists()
    {
        // Arrange
        var state = new WorldState();

        // Act
        var result = state.GetString("missing", "default");

        // Assert
        Assert.Equal("default", result);
    }

    [Fact]
    public void GetValue_ReturnsNull_WhenNotExists()
    {
        // Arrange
        var state = new WorldState();

        // Act
        var result = state.GetValue("missing");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void ContainsKey_ReturnsTrue_WhenExists()
    {
        // Arrange
        var state = new WorldState().SetNumeric("health", 100);

        // Act & Assert
        Assert.True(state.ContainsKey("health"));
    }

    [Fact]
    public void ContainsKey_ReturnsFalse_WhenNotExists()
    {
        // Arrange
        var state = new WorldState();

        // Act & Assert
        Assert.False(state.ContainsKey("missing"));
    }

    #endregion

    #region Immutability

    [Fact]
    public void SetNumeric_ReturnsNewInstance()
    {
        // Arrange
        var original = new WorldState();

        // Act
        var modified = original.SetNumeric("health", 100);

        // Assert
        Assert.NotSame(original, modified);
        Assert.Equal(0, original.Count);
        Assert.Equal(1, modified.Count);
    }

    [Fact]
    public void SetBoolean_ReturnsNewInstance()
    {
        // Arrange
        var original = new WorldState();

        // Act
        var modified = original.SetBoolean("alive", true);

        // Assert
        Assert.NotSame(original, modified);
        Assert.Equal(0, original.Count);
        Assert.True(modified.GetBoolean("alive"));
    }

    [Fact]
    public void SetString_ReturnsNewInstance()
    {
        // Arrange
        var original = new WorldState();

        // Act
        var modified = original.SetString("location", "home");

        // Assert
        Assert.NotSame(original, modified);
        Assert.Equal(0, original.Count);
        Assert.Equal("home", modified.GetString("location"));
    }

    [Fact]
    public void ChainedSets_AccumulateValues()
    {
        // Arrange
        var state = new WorldState();

        // Act
        var result = state
            .SetNumeric("health", 100)
            .SetBoolean("alive", true)
            .SetString("name", "player");

        // Assert
        Assert.Equal(3, result.Count);
        Assert.Equal(100, result.GetNumeric("health"));
        Assert.True(result.GetBoolean("alive"));
        Assert.Equal("player", result.GetString("name"));
    }

    #endregion

    #region ApplyEffects

    [Fact]
    public void ApplyEffects_SetsAbsoluteValue()
    {
        // Arrange
        var state = new WorldState().SetNumeric("gold", 50);
        var effects = new GoapActionEffects();
        effects.AddEffect("gold", GoapEffect.Parse("100"));

        // Act
        var result = state.ApplyEffects(effects);

        // Assert
        Assert.Equal(100, result.GetNumeric("gold"));
    }

    [Fact]
    public void ApplyEffects_AddsToValue()
    {
        // Arrange
        var state = new WorldState().SetNumeric("gold", 50);
        var effects = new GoapActionEffects();
        effects.AddEffect("gold", GoapEffect.Parse("+25"));

        // Act
        var result = state.ApplyEffects(effects);

        // Assert
        Assert.Equal(75, result.GetNumeric("gold"));
    }

    [Fact]
    public void ApplyEffects_SubtractsFromValue()
    {
        // Arrange
        var state = new WorldState().SetNumeric("hunger", 0.8f);
        var effects = new GoapActionEffects();
        effects.AddEffect("hunger", GoapEffect.Parse("-0.5"));

        // Act
        var result = state.ApplyEffects(effects);

        // Assert
        Assert.Equal(0.3f, result.GetNumeric("hunger"), 0.001);
    }

    [Fact]
    public void ApplyEffects_SetsMultipleValues()
    {
        // Arrange
        var state = new WorldState()
            .SetNumeric("hunger", 0.8f)
            .SetString("location", "home");

        var effects = new GoapActionEffects();
        effects.AddEffect("hunger", GoapEffect.Parse("-0.5"));
        effects.AddEffect("location", GoapEffect.Parse("tavern"));

        // Act
        var result = state.ApplyEffects(effects);

        // Assert
        Assert.Equal(0.3f, result.GetNumeric("hunger"), 0.001);
        Assert.Equal("tavern", result.GetString("location"));
    }

    #endregion

    #region SatisfiesPreconditions

    [Fact]
    public void SatisfiesPreconditions_ReturnsTrue_WhenAllSatisfied()
    {
        // Arrange
        var state = new WorldState()
            .SetNumeric("hunger", 0.8f)
            .SetNumeric("gold", 10);

        var preconditions = new GoapPreconditions();
        preconditions.AddCondition("hunger", GoapCondition.Parse("> 0.6"));
        preconditions.AddCondition("gold", GoapCondition.Parse(">= 5"));

        // Act
        var result = state.SatisfiesPreconditions(preconditions);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void SatisfiesPreconditions_ReturnsFalse_WhenOneFails()
    {
        // Arrange
        var state = new WorldState()
            .SetNumeric("hunger", 0.4f)
            .SetNumeric("gold", 10);

        var preconditions = new GoapPreconditions();
        preconditions.AddCondition("hunger", GoapCondition.Parse("> 0.6"));
        preconditions.AddCondition("gold", GoapCondition.Parse(">= 5"));

        // Act
        var result = state.SatisfiesPreconditions(preconditions);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void SatisfiesPreconditions_ReturnsTrue_WhenEmpty()
    {
        // Arrange
        var state = new WorldState();
        var preconditions = new GoapPreconditions();

        // Act
        var result = state.SatisfiesPreconditions(preconditions);

        // Assert
        Assert.True(result);
    }

    #endregion

    #region SatisfiesGoal

    [Fact]
    public void SatisfiesGoal_ReturnsTrue_WhenAllConditionsMet()
    {
        // Arrange
        var state = new WorldState()
            .SetNumeric("hunger", 0.2f);

        var goal = InternalGoapGoal.FromMetadata(
            "stay_fed",
            100,
            new Dictionary<string, string> { { "hunger", "<= 0.3" } });

        // Act
        var result = state.SatisfiesGoal(goal);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void SatisfiesGoal_ReturnsFalse_WhenConditionNotMet()
    {
        // Arrange
        var state = new WorldState()
            .SetNumeric("hunger", 0.8f);

        var goal = InternalGoapGoal.FromMetadata(
            "stay_fed",
            100,
            new Dictionary<string, string> { { "hunger", "<= 0.3" } });

        // Act
        var result = state.SatisfiesGoal(goal);

        // Assert
        Assert.False(result);
    }

    #endregion

    #region DistanceToGoal

    [Fact]
    public void DistanceToGoal_ReturnsZero_WhenGoalSatisfied()
    {
        // Arrange
        var state = new WorldState()
            .SetNumeric("hunger", 0.2f);

        var goal = InternalGoapGoal.FromMetadata(
            "stay_fed",
            100,
            new Dictionary<string, string> { { "hunger", "<= 0.3" } });

        // Act
        var distance = state.DistanceToGoal(goal);

        // Assert
        Assert.Equal(0f, distance);
    }

    [Fact]
    public void DistanceToGoal_ReturnsPositive_WhenGoalNotSatisfied()
    {
        // Arrange
        var state = new WorldState()
            .SetNumeric("hunger", 0.8f);

        var goal = InternalGoapGoal.FromMetadata(
            "stay_fed",
            100,
            new Dictionary<string, string> { { "hunger", "<= 0.3" } });

        // Act
        var distance = state.DistanceToGoal(goal);

        // Assert
        Assert.True(distance > 0);
    }

    #endregion

    #region Equality and HashCode

    [Fact]
    public void Equals_ReturnsTrue_ForSameValues()
    {
        // Arrange
        var state1 = new WorldState()
            .SetNumeric("health", 100)
            .SetString("name", "player");

        var state2 = new WorldState()
            .SetNumeric("health", 100)
            .SetString("name", "player");

        // Act & Assert
        Assert.True(state1.Equals(state2));
        Assert.True(state1 == state2);
    }

    [Fact]
    public void Equals_ReturnsFalse_ForDifferentValues()
    {
        // Arrange
        var state1 = new WorldState().SetNumeric("health", 100);
        var state2 = new WorldState().SetNumeric("health", 50);

        // Act & Assert
        Assert.False(state1.Equals(state2));
        Assert.True(state1 != state2);
    }

    [Fact]
    public void GetHashCode_SameForEqualStates()
    {
        // Arrange
        var state1 = new WorldState()
            .SetNumeric("health", 100)
            .SetString("name", "player");

        var state2 = new WorldState()
            .SetNumeric("health", 100)
            .SetString("name", "player");

        // Act & Assert
        Assert.Equal(state1.GetHashCode(), state2.GetHashCode());
    }

    [Fact]
    public void GetHashCode_DifferentForDifferentStates()
    {
        // Arrange
        var state1 = new WorldState().SetNumeric("health", 100);
        var state2 = new WorldState().SetNumeric("health", 50);

        // Act & Assert
        // Note: Hash codes CAN collide, but for simple cases they typically don't
        Assert.NotEqual(state1.GetHashCode(), state2.GetHashCode());
    }

    #endregion
}
