using BeyondImmersion.BannouService.Abml.Documents;
using BeyondImmersion.BannouService.Abml.Expressions;
using BeyondImmersion.BannouService.Abml.Runtime;
using BeyondImmersion.BannouService.Actor.Runtime;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Actor.Tests;

/// <summary>
/// Unit tests for OptionsEvaluator - evaluating ABML options definitions against variable scopes.
/// </summary>
public class OptionsEvaluatorTests
{
    private readonly Mock<IExpressionEvaluator> _evaluatorMock = new();
    private readonly Mock<ILogger> _loggerMock = new();

    #region Literal Value Tests

    [Fact]
    public void EvaluateAll_LiteralPreference_ReturnsValue()
    {
        // Arrange
        var options = CreateOptionsDefinition("combat", new OptionDefinition
        {
            ActionId = "attack",
            Preference = "0.8",
            Available = "true"
        });
        var scope = new VariableScope();

        // Act
        var result = OptionsEvaluator.EvaluateAll(options, scope, _evaluatorMock.Object);

        // Assert
        Assert.Single(result);
        Assert.True(result.ContainsKey("combat"));
        Assert.Single(result["combat"].Options);
        Assert.Equal("attack", result["combat"].Options[0].ActionId);
        Assert.Equal(0.8f, result["combat"].Options[0].Preference, 0.001f);
        Assert.True(result["combat"].Options[0].Available);
    }

    [Fact]
    public void EvaluateAll_LiteralRisk_ReturnsClampedValue()
    {
        // Arrange
        var options = CreateOptionsDefinition("combat", new OptionDefinition
        {
            ActionId = "reckless_attack",
            Preference = "0.5",
            Risk = "1.5", // Should be clamped to 1.0
            Available = "true"
        });
        var scope = new VariableScope();

        // Act
        var result = OptionsEvaluator.EvaluateAll(options, scope, _evaluatorMock.Object);

        // Assert
        Assert.Equal(1.0f, result["combat"].Options[0].Risk);
    }

    [Fact]
    public void EvaluateAll_LiteralCooldown_ReturnsIntValue()
    {
        // Arrange
        var options = CreateOptionsDefinition("combat", new OptionDefinition
        {
            ActionId = "special_attack",
            Preference = "0.7",
            Available = "true",
            CooldownMs = "5000"
        });
        var scope = new VariableScope();

        // Act
        var result = OptionsEvaluator.EvaluateAll(options, scope, _evaluatorMock.Object);

        // Assert
        Assert.Equal(5000, result["combat"].Options[0].CooldownMs);
    }

    [Fact]
    public void EvaluateAll_AvailableFalse_ReturnsFalse()
    {
        // Arrange
        var options = CreateOptionsDefinition("dialogue", new OptionDefinition
        {
            ActionId = "locked_option",
            Preference = "0.9",
            Available = "false"
        });
        var scope = new VariableScope();

        // Act
        var result = OptionsEvaluator.EvaluateAll(options, scope, _evaluatorMock.Object);

        // Assert
        Assert.False(result["dialogue"].Options[0].Available);
    }

    #endregion

    #region Expression Evaluation Tests

    [Fact]
    public void EvaluateAll_ExpressionPreference_EvaluatesExpression()
    {
        // Arrange
        var options = CreateOptionsDefinition("combat", new OptionDefinition
        {
            ActionId = "aggressive_attack",
            Preference = "${combat.aggressiveness}",
            Available = "true"
        });
        var scope = new VariableScope();
        scope.SetValue("combat", new Dictionary<string, object?> { ["aggressiveness"] = 0.75 });

        _evaluatorMock.Setup(e => e.Evaluate("${combat.aggressiveness}", scope))
            .Returns(0.75);

        // Act
        var result = OptionsEvaluator.EvaluateAll(options, scope, _evaluatorMock.Object);

        // Assert
        Assert.Equal(0.75f, result["combat"].Options[0].Preference, 0.001f);
        _evaluatorMock.Verify(e => e.Evaluate("${combat.aggressiveness}", scope), Times.Once);
    }

    [Fact]
    public void EvaluateAll_ExpressionAvailable_EvaluatesExpression()
    {
        // Arrange
        var options = CreateOptionsDefinition("combat", new OptionDefinition
        {
            ActionId = "sword_attack",
            Preference = "0.8",
            Available = "${equipment.has_sword}"
        });
        var scope = new VariableScope();
        scope.SetValue("equipment", new Dictionary<string, object?> { ["has_sword"] = true });

        _evaluatorMock.Setup(e => e.Evaluate("${equipment.has_sword}", scope))
            .Returns(true);

        // Act
        var result = OptionsEvaluator.EvaluateAll(options, scope, _evaluatorMock.Object);

        // Assert
        Assert.True(result["combat"].Options[0].Available);
    }

    [Fact]
    public void EvaluateAll_ExpressionRisk_EvaluatesExpression()
    {
        // Arrange
        var options = CreateOptionsDefinition("combat", new OptionDefinition
        {
            ActionId = "risky_move",
            Preference = "0.5",
            Risk = "${1.0 - personality.caution}",
            Available = "true"
        });
        var scope = new VariableScope();
        scope.SetValue("personality", new Dictionary<string, object?> { ["caution"] = 0.3 });

        _evaluatorMock.Setup(e => e.Evaluate("${1.0 - personality.caution}", scope))
            .Returns(0.7);

        // Act
        var result = OptionsEvaluator.EvaluateAll(options, scope, _evaluatorMock.Object);

        // Assert
        Assert.Equal(0.7f, result["combat"].Options[0].Risk);
    }

    [Fact]
    public void EvaluateAll_TernaryExpression_EvaluatesCorrectly()
    {
        // Arrange
        var options = CreateOptionsDefinition("combat", new OptionDefinition
        {
            ActionId = "conditional_attack",
            Preference = "${is_aggressive ? 0.9 : 0.4}",
            Available = "true"
        });
        var scope = new VariableScope();
        scope.SetValue("is_aggressive", true);

        _evaluatorMock.Setup(e => e.Evaluate("${is_aggressive ? 0.9 : 0.4}", scope))
            .Returns(0.9);

        // Act
        var result = OptionsEvaluator.EvaluateAll(options, scope, _evaluatorMock.Object);

        // Assert
        Assert.Equal(0.9f, result["combat"].Options[0].Preference, 0.001f);
    }

    #endregion

    #region Multiple Option Types Tests

    [Fact]
    public void EvaluateAll_MultipleOptionTypes_EvaluatesAll()
    {
        // Arrange
        var optionsByType = new Dictionary<string, IReadOnlyList<OptionDefinition>>
        {
            ["combat"] = new List<OptionDefinition>
            {
                new() { ActionId = "attack", Preference = "0.8", Available = "true" },
                new() { ActionId = "defend", Preference = "0.6", Available = "true" }
            },
            ["dialogue"] = new List<OptionDefinition>
            {
                new() { ActionId = "greet", Preference = "0.9", Available = "true" }
            }
        };
        var options = new OptionsDefinition { OptionsByType = optionsByType };
        var scope = new VariableScope();

        // Act
        var result = OptionsEvaluator.EvaluateAll(options, scope, _evaluatorMock.Object);

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Equal(2, result["combat"].Options.Count);
        Assert.Single(result["dialogue"].Options);
    }

    #endregion

    #region Requirements and Tags Tests

    [Fact]
    public void EvaluateAll_WithRequirements_PreservesRequirements()
    {
        // Arrange
        var options = CreateOptionsDefinition("combat", new OptionDefinition
        {
            ActionId = "special_move",
            Preference = "0.7",
            Available = "true",
            Requirements = new List<string> { "has_mana", "level_10" }
        });
        var scope = new VariableScope();

        // Act
        var result = OptionsEvaluator.EvaluateAll(options, scope, _evaluatorMock.Object);

        // Assert
        var option = result["combat"].Options[0];
        Assert.Equal(2, option.Requirements.Count);
        Assert.Contains("has_mana", option.Requirements);
        Assert.Contains("level_10", option.Requirements);
    }

    [Fact]
    public void EvaluateAll_WithTags_PreservesTags()
    {
        // Arrange
        var options = CreateOptionsDefinition("combat", new OptionDefinition
        {
            ActionId = "sword_slash",
            Preference = "0.8",
            Available = "true",
            Tags = new List<string> { "melee", "offensive", "physical" }
        });
        var scope = new VariableScope();

        // Act
        var result = OptionsEvaluator.EvaluateAll(options, scope, _evaluatorMock.Object);

        // Assert
        var option = result["combat"].Options[0];
        Assert.Equal(3, option.Tags.Count);
        Assert.Contains("melee", option.Tags);
        Assert.Contains("offensive", option.Tags);
        Assert.Contains("physical", option.Tags);
    }

    #endregion

    #region Timestamp Tests

    [Fact]
    public void EvaluateAll_SetsComputedAtTimestamp()
    {
        // Arrange
        var options = CreateOptionsDefinition("combat", new OptionDefinition
        {
            ActionId = "attack",
            Preference = "0.5",
            Available = "true"
        });
        var scope = new VariableScope();
        var before = DateTimeOffset.UtcNow;

        // Act
        var result = OptionsEvaluator.EvaluateAll(options, scope, _evaluatorMock.Object);

        // Assert
        var after = DateTimeOffset.UtcNow;
        Assert.True(result["combat"].ComputedAt >= before);
        Assert.True(result["combat"].ComputedAt <= after);
    }

    #endregion

    #region Error Handling Tests

    [Fact]
    public void EvaluateAll_ExpressionThrows_ContinuesWithOtherOptions()
    {
        // Arrange
        var optionsByType = new Dictionary<string, IReadOnlyList<OptionDefinition>>
        {
            ["combat"] = new List<OptionDefinition>
            {
                new() { ActionId = "broken", Preference = "${throw_error}", Available = "true" },
                new() { ActionId = "working", Preference = "0.5", Available = "true" }
            }
        };
        var options = new OptionsDefinition { OptionsByType = optionsByType };
        var scope = new VariableScope();

        _evaluatorMock.Setup(e => e.Evaluate("${throw_error}", scope))
            .Throws(new InvalidOperationException("Test error"));

        // Act
        var result = OptionsEvaluator.EvaluateAll(options, scope, _evaluatorMock.Object, _loggerMock.Object);

        // Assert - should have the working option
        Assert.Single(result["combat"].Options);
        Assert.Equal("working", result["combat"].Options[0].ActionId);
    }

    [Fact]
    public void EvaluateAll_EmptyOptions_ReturnsEmptyResult()
    {
        // Arrange
        var options = new OptionsDefinition
        {
            OptionsByType = new Dictionary<string, IReadOnlyList<OptionDefinition>>()
        };
        var scope = new VariableScope();

        // Act
        var result = OptionsEvaluator.EvaluateAll(options, scope, _evaluatorMock.Object);

        // Assert
        Assert.Empty(result);
    }

    #endregion

    #region Clamping Tests

    [Fact]
    public void EvaluateAll_PreferenceAboveOne_ClampedToOne()
    {
        // Arrange
        var options = CreateOptionsDefinition("combat", new OptionDefinition
        {
            ActionId = "over_eager",
            Preference = "1.5",
            Available = "true"
        });
        var scope = new VariableScope();

        // Act
        var result = OptionsEvaluator.EvaluateAll(options, scope, _evaluatorMock.Object);

        // Assert
        Assert.Equal(1.0f, result["combat"].Options[0].Preference);
    }

    [Fact]
    public void EvaluateAll_PreferenceBelowZero_ClampedToZero()
    {
        // Arrange
        var options = CreateOptionsDefinition("combat", new OptionDefinition
        {
            ActionId = "reluctant",
            Preference = "-0.5",
            Available = "true"
        });
        var scope = new VariableScope();

        // Act
        var result = OptionsEvaluator.EvaluateAll(options, scope, _evaluatorMock.Object);

        // Assert
        Assert.Equal(0.0f, result["combat"].Options[0].Preference);
    }

    [Fact]
    public void EvaluateAll_RiskBelowZero_ClampedToZero()
    {
        // Arrange
        var options = CreateOptionsDefinition("combat", new OptionDefinition
        {
            ActionId = "safe_move",
            Preference = "0.5",
            Risk = "-0.3",
            Available = "true"
        });
        var scope = new VariableScope();

        // Act
        var result = OptionsEvaluator.EvaluateAll(options, scope, _evaluatorMock.Object);

        // Assert
        Assert.Equal(0.0f, result["combat"].Options[0].Risk);
    }

    #endregion

    #region Helper Methods

    private static OptionsDefinition CreateOptionsDefinition(string optionType, OptionDefinition option)
    {
        return new OptionsDefinition
        {
            OptionsByType = new Dictionary<string, IReadOnlyList<OptionDefinition>>
            {
                [optionType] = new List<OptionDefinition> { option }
            }
        };
    }

    #endregion
}
