// ═══════════════════════════════════════════════════════════════════════════
// GoapCondition Unit Tests
// Tests for GOAP condition parsing and evaluation.
// ═══════════════════════════════════════════════════════════════════════════

using BeyondImmersion.Bannou.BehaviorCompiler.Goap;
using Xunit;

namespace BeyondImmersion.BannouService.Behavior.Tests.Goap;

/// <summary>
/// Tests for GoapCondition parsing and evaluation.
/// </summary>
public class GoapConditionTests
{
    #region Parsing - Numeric Operators

    [Fact]
    public void Parse_GreaterThan_Numeric()
    {
        // Arrange & Act
        var condition = GoapCondition.Parse("> 0.6");

        // Assert
        Assert.NotNull(condition);
    }

    [Fact]
    public void Parse_GreaterThanOrEqual_Numeric()
    {
        // Arrange & Act
        var condition = GoapCondition.Parse(">= 5");

        // Assert
        Assert.NotNull(condition);
    }

    [Fact]
    public void Parse_LessThan_Numeric()
    {
        // Arrange & Act
        var condition = GoapCondition.Parse("< 0.3");

        // Assert
        Assert.NotNull(condition);
    }

    [Fact]
    public void Parse_LessThanOrEqual_Numeric()
    {
        // Arrange & Act
        var condition = GoapCondition.Parse("<= 100");

        // Assert
        Assert.NotNull(condition);
    }

    [Fact]
    public void Parse_Equals_Numeric()
    {
        // Arrange & Act
        var condition = GoapCondition.Parse("== 50");

        // Assert
        Assert.NotNull(condition);
    }

    [Fact]
    public void Parse_NotEquals_Numeric()
    {
        // Arrange & Act
        var condition = GoapCondition.Parse("!= 0");

        // Assert
        Assert.NotNull(condition);
    }

    [Fact]
    public void Parse_NegativeNumber()
    {
        // Arrange & Act
        var condition = GoapCondition.Parse("> -10");

        // Assert
        Assert.NotNull(condition);
    }

    [Fact]
    public void Parse_FloatWithDecimal()
    {
        // Arrange & Act
        var condition = GoapCondition.Parse(">= 3.14159");

        // Assert
        Assert.NotNull(condition);
    }

    #endregion

    #region Parsing - Boolean Operators

    [Fact]
    public void Parse_Equals_True()
    {
        // Arrange & Act
        var condition = GoapCondition.Parse("== true");

        // Assert
        Assert.NotNull(condition);
    }

    [Fact]
    public void Parse_Equals_False()
    {
        // Arrange & Act
        var condition = GoapCondition.Parse("== false");

        // Assert
        Assert.NotNull(condition);
    }

    [Fact]
    public void Parse_NotEquals_True()
    {
        // Arrange & Act
        var condition = GoapCondition.Parse("!= true");

        // Assert
        Assert.NotNull(condition);
    }

    #endregion

    #region Parsing - String Operators

    [Fact]
    public void Parse_Equals_QuotedString()
    {
        // Arrange & Act
        var condition = GoapCondition.Parse("== 'tavern'");

        // Assert
        Assert.NotNull(condition);
    }

    [Fact]
    public void Parse_Equals_DoubleQuotedString()
    {
        // Arrange & Act
        var condition = GoapCondition.Parse("== \"tavern\"");

        // Assert
        Assert.NotNull(condition);
    }

    [Fact]
    public void Parse_NotEquals_String()
    {
        // Arrange & Act
        var condition = GoapCondition.Parse("!= 'idle'");

        // Assert
        Assert.NotNull(condition);
    }

    #endregion

    #region TryParse

    [Fact]
    public void TryParse_ReturnsTrue_ForValidCondition()
    {
        // Arrange & Act
        var success = GoapCondition.TryParse("> 0.5", out var condition);

        // Assert
        Assert.True(success);
        Assert.NotNull(condition);
    }

    [Fact]
    public void TryParse_ReturnsFalse_ForInvalidCondition()
    {
        // Arrange & Act
        var success = GoapCondition.TryParse("invalid", out var condition);

        // Assert
        Assert.False(success);
    }

    [Fact]
    public void TryParse_ReturnsFalse_ForEmptyString()
    {
        // Arrange & Act
        var success = GoapCondition.TryParse("", out _);

        // Assert
        Assert.False(success);
    }

    #endregion

    #region Evaluate - Numeric Comparisons

    [Theory]
    [InlineData(0.7f, true)]
    [InlineData(0.6f, false)]
    [InlineData(0.5f, false)]
    public void Evaluate_GreaterThan(float value, bool expected)
    {
        // Arrange
        var condition = GoapCondition.Parse("> 0.6");

        // Act
        var result = condition.Evaluate(value);

        // Assert
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(6f, true)]
    [InlineData(5f, true)]
    [InlineData(4f, false)]
    public void Evaluate_GreaterThanOrEqual(float value, bool expected)
    {
        // Arrange
        var condition = GoapCondition.Parse(">= 5");

        // Act
        var result = condition.Evaluate(value);

        // Assert
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(0.2f, true)]
    [InlineData(0.3f, false)]
    [InlineData(0.4f, false)]
    public void Evaluate_LessThan(float value, bool expected)
    {
        // Arrange
        var condition = GoapCondition.Parse("< 0.3");

        // Act
        var result = condition.Evaluate(value);

        // Assert
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(99f, true)]
    [InlineData(100f, true)]
    [InlineData(101f, false)]
    public void Evaluate_LessThanOrEqual(float value, bool expected)
    {
        // Arrange
        var condition = GoapCondition.Parse("<= 100");

        // Act
        var result = condition.Evaluate(value);

        // Assert
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(50f, true)]
    [InlineData(49f, false)]
    [InlineData(51f, false)]
    public void Evaluate_Equals_Numeric(float value, bool expected)
    {
        // Arrange
        var condition = GoapCondition.Parse("== 50");

        // Act
        var result = condition.Evaluate(value);

        // Assert
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(1f, true)]
    [InlineData(-1f, true)]
    [InlineData(0f, false)]
    public void Evaluate_NotEquals_Numeric(float value, bool expected)
    {
        // Arrange
        var condition = GoapCondition.Parse("!= 0");

        // Act
        var result = condition.Evaluate(value);

        // Assert
        Assert.Equal(expected, result);
    }

    #endregion

    #region Evaluate - Boolean Comparisons

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void Evaluate_Equals_True(bool value, bool expected)
    {
        // Arrange
        var condition = GoapCondition.Parse("== true");

        // Act
        var result = condition.Evaluate(value);

        // Assert
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Evaluate_Equals_False(bool value, bool expected)
    {
        // Arrange
        var condition = GoapCondition.Parse("== false");

        // Act
        var result = condition.Evaluate(value);

        // Assert
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Evaluate_NotEquals_True(bool value, bool expected)
    {
        // Arrange
        var condition = GoapCondition.Parse("!= true");

        // Act
        var result = condition.Evaluate(value);

        // Assert
        Assert.Equal(expected, result);
    }

    #endregion

    #region Evaluate - String Comparisons

    [Theory]
    [InlineData("tavern", true)]
    [InlineData("home", false)]
    [InlineData("", false)]
    public void Evaluate_Equals_String(string value, bool expected)
    {
        // Arrange
        var condition = GoapCondition.Parse("== 'tavern'");

        // Act
        var result = condition.Evaluate(value);

        // Assert
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("idle", false)]
    [InlineData("walking", true)]
    [InlineData("running", true)]
    public void Evaluate_NotEquals_String(string value, bool expected)
    {
        // Arrange
        var condition = GoapCondition.Parse("!= 'idle'");

        // Act
        var result = condition.Evaluate(value);

        // Assert
        Assert.Equal(expected, result);
    }

    #endregion

    #region Evaluate - Null Values

    [Fact]
    public void Evaluate_NullValue_ReturnsAppropriateResult()
    {
        // Arrange
        var condition = GoapCondition.Parse("> 0");

        // Act
        var result = condition.Evaluate(null);

        // Assert
        // Null should be treated as 0 for numeric comparisons
        Assert.False(result);
    }

    [Fact]
    public void Evaluate_MissingValue_UsesDefault()
    {
        // Arrange
        var condition = GoapCondition.Parse(">= 0");

        // Act - null should be treated as default (0)
        var result = condition.Evaluate(null);

        // Assert
        Assert.True(result); // 0 >= 0
    }

    #endregion

    #region Distance Heuristic

    [Fact]
    public void Distance_ReturnsZero_WhenConditionSatisfied()
    {
        // Arrange
        var condition = GoapCondition.Parse(">= 50");

        // Act
        var distance = condition.Distance(100f);

        // Assert
        Assert.Equal(0f, distance);
    }

    [Fact]
    public void Distance_ReturnsPositive_WhenConditionNotSatisfied()
    {
        // Arrange
        var condition = GoapCondition.Parse(">= 50");

        // Act
        var distance = condition.Distance(30f);

        // Assert
        Assert.True(distance > 0);
    }

    [Fact]
    public void Distance_LessThan_ReturnsZero_WhenSatisfied()
    {
        // Arrange
        var condition = GoapCondition.Parse("< 0.3");

        // Act
        var distance = condition.Distance(0.1f);

        // Assert
        Assert.Equal(0f, distance);
    }

    [Fact]
    public void Distance_LessThan_ReturnsPositive_WhenNotSatisfied()
    {
        // Arrange
        var condition = GoapCondition.Parse("< 0.3");

        // Act
        var distance = condition.Distance(0.5f);

        // Assert
        Assert.True(distance > 0);
    }

    [Fact]
    public void Distance_Boolean_ReturnsZero_WhenMatches()
    {
        // Arrange
        var condition = GoapCondition.Parse("== true");

        // Act
        var distance = condition.Distance(true);

        // Assert
        Assert.Equal(0f, distance);
    }

    [Fact]
    public void Distance_Boolean_ReturnsOne_WhenNotMatches()
    {
        // Arrange
        var condition = GoapCondition.Parse("== true");

        // Act
        var distance = condition.Distance(false);

        // Assert
        Assert.Equal(1f, distance);
    }

    [Fact]
    public void Distance_String_ReturnsZero_WhenMatches()
    {
        // Arrange
        var condition = GoapCondition.Parse("== 'tavern'");

        // Act
        var distance = condition.Distance("tavern");

        // Assert
        Assert.Equal(0f, distance);
    }

    [Fact]
    public void Distance_String_ReturnsOne_WhenNotMatches()
    {
        // Arrange
        var condition = GoapCondition.Parse("== 'tavern'");

        // Act
        var distance = condition.Distance("home");

        // Assert
        Assert.Equal(1f, distance);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void Parse_HandlesExtraWhitespace()
    {
        // Arrange & Act
        var condition = GoapCondition.Parse("  >=   5  ");

        // Assert
        Assert.True(condition.Evaluate(5f));
    }

    [Fact]
    public void Parse_HandlesIntegerFromString()
    {
        // Arrange
        var condition = GoapCondition.Parse(">= 10");

        // Act - Value passed as int should work
        var result = condition.Evaluate(15);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void Parse_HandlesDoubleValue()
    {
        // Arrange
        var condition = GoapCondition.Parse(">= 5.5");

        // Act - Value passed as double should work
        var result = condition.Evaluate(6.0);

        // Assert
        Assert.True(result);
    }

    #endregion
}
