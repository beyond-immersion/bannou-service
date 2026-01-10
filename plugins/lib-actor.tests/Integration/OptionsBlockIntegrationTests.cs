// =============================================================================
// Options Block Integration Tests
// Tests for the full flow of options block parsing, evaluation, and storage
// through ActorRunner behavior execution.
// =============================================================================

using BeyondImmersion.BannouService.Abml.Documents;
using BeyondImmersion.BannouService.Abml.Execution;
using BeyondImmersion.BannouService.Abml.Expressions;
using BeyondImmersion.BannouService.Abml.Parser;
using BeyondImmersion.BannouService.Abml.Runtime;
using BeyondImmersion.BannouService.Actor;
using BeyondImmersion.BannouService.Actor.Caching;
using BeyondImmersion.BannouService.Actor.Runtime;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Actor.Tests.Integration;

/// <summary>
/// Integration tests for ABML options block parsing, evaluation, and ActorRunner integration.
/// </summary>
public sealed class OptionsBlockIntegrationTests
{
    private readonly DocumentParser _parser = new();

    // =========================================================================
    // OPTIONS BLOCK PARSING AND EVALUATION INTEGRATION
    // =========================================================================

    [Fact]
    public void ParseAndEvaluate_LiteralOptions_EvaluatesCorrectly()
    {
        // Arrange
        var yaml = @"
version: ""2.0""
metadata:
  id: literal_options_test
  type: behavior

options:
  combat:
    - actionId: basic_attack
    preference: 0.7
    risk: 0.2
    available: true
    requirements:
        - weapon_equipped
    tags:
        - melee
        - physical

flows:
  main:
    actions:
    - log: ""tick""
";
        var parseResult = _parser.Parse(yaml);
        Assert.True(parseResult.IsSuccess);
        var document = parseResult.Value!;

        var scope = new VariableScope();
        var evaluatorMock = new Mock<IExpressionEvaluator>();

        // Act
        var evaluated = OptionsEvaluator.EvaluateAll(
            document.Options!,
            scope,
            evaluatorMock.Object);

        // Assert
        Assert.Single(evaluated);
        Assert.True(evaluated.ContainsKey("combat"));

        var combatOptions = evaluated["combat"];
        Assert.Single(combatOptions.Options);

        var basicAttack = combatOptions.Options[0];
        Assert.Equal("basic_attack", basicAttack.ActionId);
        Assert.Equal(0.7f, basicAttack.Preference, 0.001f);
        Assert.Equal(0.2f, basicAttack.Risk);
        Assert.True(basicAttack.Available);
        Assert.Contains("weapon_equipped", basicAttack.Requirements!);
        Assert.Contains("melee", basicAttack.Tags!);
    }

    [Fact]
    public void ParseAndEvaluate_ExpressionOptions_EvaluatesWithScope()
    {
        // Arrange
        var yaml = @"
version: ""2.0""
metadata:
  id: expression_options_test
  type: behavior

options:
  combat:
    - actionId: aggressive_strike
    preference: ""${combat.aggressiveness}""
    available: ""${equipment.has_weapon}""
    tags:
        - offensive

flows:
  main:
    actions:
    - log: ""tick""
";
        var parseResult = _parser.Parse(yaml);
        Assert.True(parseResult.IsSuccess);
        var document = parseResult.Value!;

        var scope = new VariableScope();
        scope.SetValue("combat", new Dictionary<string, object?> { ["aggressiveness"] = 0.85 });
        scope.SetValue("equipment", new Dictionary<string, object?> { ["has_weapon"] = true });

        var evaluatorMock = new Mock<IExpressionEvaluator>();
        evaluatorMock.Setup(e => e.Evaluate("${combat.aggressiveness}", scope))
            .Returns(0.85);
        evaluatorMock.Setup(e => e.Evaluate("${equipment.has_weapon}", scope))
            .Returns(true);

        // Act
        var evaluated = OptionsEvaluator.EvaluateAll(
            document.Options!,
            scope,
            evaluatorMock.Object);

        // Assert
        var option = evaluated["combat"].Options[0];
        Assert.Equal(0.85f, option.Preference, 0.001f);
        Assert.True(option.Available);
    }

    [Fact]
    public void ParseAndEvaluate_MultipleOptionTypes_EvaluatesAll()
    {
        // Arrange
        var yaml = @"
version: ""2.0""
metadata:
  id: multi_type_options_test
  type: behavior

options:
  combat:
    - actionId: attack
    preference: 0.8
    available: true
    - actionId: defend
    preference: 0.5
    available: true
  dialogue:
    - actionId: greet
    preference: 0.9
    available: true
  exploration:
    - actionId: investigate
    preference: 0.6
    available: true

flows:
  main:
    actions:
    - log: ""tick""
";
        var parseResult = _parser.Parse(yaml);
        Assert.True(parseResult.IsSuccess);
        var document = parseResult.Value!;

        var scope = new VariableScope();
        var evaluatorMock = new Mock<IExpressionEvaluator>();

        // Act
        var evaluated = OptionsEvaluator.EvaluateAll(
            document.Options!,
            scope,
            evaluatorMock.Object);

        // Assert
        Assert.Equal(3, evaluated.Count);
        Assert.Equal(2, evaluated["combat"].Options.Count);
        Assert.Single(evaluated["dialogue"].Options);
        Assert.Single(evaluated["exploration"].Options);
    }

    [Fact]
    public void ParseAndEvaluate_ConditionalAvailability_DisablesOption()
    {
        // Arrange
        var yaml = @"
version: ""2.0""
metadata:
  id: conditional_available_test
  type: behavior

options:
  combat:
    - actionId: sword_slash
    preference: 0.8
    available: ""${equipment.has_sword}""
    requirements:
        - has_sword

flows:
  main:
    actions:
    - log: ""tick""
";
        var parseResult = _parser.Parse(yaml);
        Assert.True(parseResult.IsSuccess);
        var document = parseResult.Value!;

        var scope = new VariableScope();
        scope.SetValue("equipment", new Dictionary<string, object?> { ["has_sword"] = false });

        var evaluatorMock = new Mock<IExpressionEvaluator>();
        evaluatorMock.Setup(e => e.Evaluate("${equipment.has_sword}", scope))
            .Returns(false);

        // Act
        var evaluated = OptionsEvaluator.EvaluateAll(
            document.Options!,
            scope,
            evaluatorMock.Object);

        // Assert
        var option = evaluated["combat"].Options[0];
        Assert.False(option.Available);
    }

    [Fact]
    public void ParseAndEvaluate_TernaryPreference_EvaluatesCorrectly()
    {
        // Arrange
        var yaml = @"
version: ""2.0""
metadata:
  id: ternary_preference_test
  type: behavior

options:
  combat:
    - actionId: adaptive_attack
    preference: ""${combat.style == 'aggressive' ? 0.9 : 0.5}""
    available: true

flows:
  main:
    actions:
    - log: ""tick""
";
        var parseResult = _parser.Parse(yaml);
        Assert.True(parseResult.IsSuccess);
        var document = parseResult.Value!;

        var scope = new VariableScope();
        scope.SetValue("combat", new Dictionary<string, object?> { ["style"] = "aggressive" });

        var evaluatorMock = new Mock<IExpressionEvaluator>();
        evaluatorMock.Setup(e => e.Evaluate("${combat.style == 'aggressive' ? 0.9 : 0.5}", scope))
            .Returns(0.9);

        // Act
        var evaluated = OptionsEvaluator.EvaluateAll(
            document.Options!,
            scope,
            evaluatorMock.Object);

        // Assert
        var option = evaluated["combat"].Options[0];
        Assert.Equal(0.9f, option.Preference, 0.001f);
    }

    [Fact]
    public void ParseAndEvaluate_WithCooldown_ParsesAndEvaluates()
    {
        // Arrange
        var yaml = @"
version: ""2.0""
metadata:
  id: cooldown_options_test
  type: behavior

options:
  combat:
    - actionId: special_attack
    preference: 0.7
    available: true
    cooldownMs: 5000
    - actionId: ultimate
    preference: 0.9
    available: true
    cooldownMs: ""${skills.ultimate_cooldown}""

flows:
  main:
    actions:
    - log: ""tick""
";
        var parseResult = _parser.Parse(yaml);
        Assert.True(parseResult.IsSuccess);
        var document = parseResult.Value!;

        var scope = new VariableScope();
        scope.SetValue("skills", new Dictionary<string, object?> { ["ultimate_cooldown"] = 30000 });

        var evaluatorMock = new Mock<IExpressionEvaluator>();
        evaluatorMock.Setup(e => e.Evaluate("${skills.ultimate_cooldown}", scope))
            .Returns(30000);

        // Act
        var evaluated = OptionsEvaluator.EvaluateAll(
            document.Options!,
            scope,
            evaluatorMock.Object);

        // Assert
        Assert.Equal(5000, evaluated["combat"].Options[0].CooldownMs);
        Assert.Equal(30000, evaluated["combat"].Options[1].CooldownMs);
    }

    [Fact]
    public void ParseAndEvaluate_ErrorInExpression_ContinuesWithOthers()
    {
        // Arrange
        var yaml = @"
version: ""2.0""
metadata:
  id: error_handling_test
  type: behavior

options:
  combat:
    - actionId: broken_option
    preference: ""${throw_error}""
    available: true
    - actionId: valid_option
    preference: 0.5
    available: true

flows:
  main:
    actions:
    - log: ""tick""
";
        var parseResult = _parser.Parse(yaml);
        Assert.True(parseResult.IsSuccess);
        var document = parseResult.Value!;

        var scope = new VariableScope();

        var evaluatorMock = new Mock<IExpressionEvaluator>();
        evaluatorMock.Setup(e => e.Evaluate("${throw_error}", scope))
            .Throws(new InvalidOperationException("Test error"));

        var loggerMock = new Mock<ILogger>();

        // Act
        var evaluated = OptionsEvaluator.EvaluateAll(
            document.Options!,
            scope,
            evaluatorMock.Object,
            loggerMock.Object);

        // Assert - should have the valid option, skip the broken one
        Assert.Single(evaluated["combat"].Options);
        Assert.Equal("valid_option", evaluated["combat"].Options[0].ActionId);
    }

    // =========================================================================
    // OPTIONS TIMESTAMP TESTS
    // =========================================================================

    [Fact]
    public void ParseAndEvaluate_SetsTimestamp()
    {
        // Arrange
        var yaml = @"
version: ""2.0""
metadata:
  id: timestamp_test
  type: behavior

options:
  combat:
    - actionId: attack
    preference: 0.5
    available: true

flows:
  main:
    actions:
    - log: ""tick""
";
        var parseResult = _parser.Parse(yaml);
        Assert.True(parseResult.IsSuccess);
        var document = parseResult.Value!;

        var before = DateTimeOffset.UtcNow;

        // Act
        var evaluated = OptionsEvaluator.EvaluateAll(
            document.Options!,
            new VariableScope(),
            new Mock<IExpressionEvaluator>().Object);

        var after = DateTimeOffset.UtcNow;

        // Assert
        Assert.True(evaluated["combat"].ComputedAt >= before);
        Assert.True(evaluated["combat"].ComputedAt <= after);
    }
}
