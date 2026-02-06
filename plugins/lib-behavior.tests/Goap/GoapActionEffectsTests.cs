// ═══════════════════════════════════════════════════════════════════════════
// GoapActionEffects Unit Tests
// Tests for GOAP action effects parsing and application.
// ═══════════════════════════════════════════════════════════════════════════

using BeyondImmersion.Bannou.BehaviorCompiler.Goap;
using Xunit;

namespace BeyondImmersion.BannouService.Behavior.Tests.Goap;

/// <summary>
/// Tests for GoapActionEffects and GoapEffect classes.
/// </summary>
public class GoapActionEffectsTests
{
    #region GoapEffect Parsing

    [Fact]
    public void Parse_PositiveDelta_CreatesAddEffect()
    {
        // Arrange & Act
        var effect = GoapEffect.Parse("+25");

        // Assert
        Assert.Equal(EffectType.Add, effect.Type);
        Assert.Equal(25f, effect.Value);
    }

    [Fact]
    public void Parse_NegativeDelta_CreatesSubtractEffect()
    {
        // Arrange & Act
        var effect = GoapEffect.Parse("-0.5");

        // Assert
        Assert.Equal(EffectType.Subtract, effect.Type);
        Assert.Equal(0.5f, effect.Value);
    }

    [Fact]
    public void Parse_AbsoluteNumeric_CreatesSetEffect()
    {
        // Arrange & Act
        var effect = GoapEffect.Parse("100");

        // Assert
        Assert.Equal(EffectType.Set, effect.Type);
        Assert.Equal(100, effect.Value);
    }

    [Fact]
    public void Parse_AbsoluteFloat_CreatesSetEffect()
    {
        // Arrange & Act
        var effect = GoapEffect.Parse("3.14");

        // Assert
        Assert.Equal(EffectType.Set, effect.Type);
        Assert.Equal(3.14f, (float)effect.Value, 0.001);
    }

    [Fact]
    public void Parse_True_CreatesSetBooleanEffect()
    {
        // Arrange & Act
        var effect = GoapEffect.Parse("true");

        // Assert
        Assert.Equal(EffectType.Set, effect.Type);
        Assert.Equal(true, effect.Value);
    }

    [Fact]
    public void Parse_False_CreatesSetBooleanEffect()
    {
        // Arrange & Act
        var effect = GoapEffect.Parse("false");

        // Assert
        Assert.Equal(EffectType.Set, effect.Type);
        Assert.Equal(false, effect.Value);
    }

    [Fact]
    public void Parse_QuotedString_CreatesSetStringEffect()
    {
        // Arrange & Act
        var effect = GoapEffect.Parse("'tavern'");

        // Assert
        Assert.Equal(EffectType.Set, effect.Type);
        Assert.Equal("tavern", effect.Value);
    }

    [Fact]
    public void Parse_DoubleQuotedString_CreatesSetStringEffect()
    {
        // Arrange & Act
        var effect = GoapEffect.Parse("\"home\"");

        // Assert
        Assert.Equal(EffectType.Set, effect.Type);
        Assert.Equal("home", effect.Value);
    }

    [Fact]
    public void Parse_UnquotedString_CreatesSetStringEffect()
    {
        // Arrange & Act
        var effect = GoapEffect.Parse("idle");

        // Assert
        Assert.Equal(EffectType.Set, effect.Type);
        Assert.Equal("idle", effect.Value);
    }

    [Fact]
    public void Parse_ThrowsOnEmptyString()
    {
        // Act & Assert
        Assert.Throws<FormatException>(() => GoapEffect.Parse(""));
    }

    #endregion

    #region GoapEffect TryParse

    [Fact]
    public void TryParse_ValidEffect_ReturnsTrue()
    {
        // Arrange & Act
        var success = GoapEffect.TryParse("+10", out var effect);

        // Assert
        Assert.True(success);
        Assert.NotNull(effect);
        Assert.Equal(EffectType.Add, effect.Type);
    }

    [Fact]
    public void TryParse_EmptyString_ReturnsFalse()
    {
        // Arrange & Act
        var success = GoapEffect.TryParse("", out _);

        // Assert
        Assert.False(success);
    }

    [Fact]
    public void TryParse_WhitespaceOnly_ReturnsFalse()
    {
        // Arrange & Act
        var success = GoapEffect.TryParse("   ", out _);

        // Assert
        Assert.False(success);
    }

    #endregion

    #region GoapEffect Apply

    [Fact]
    public void Apply_Set_ReplacesValue()
    {
        // Arrange
        var effect = GoapEffect.Parse("100");

        // Act
        var result = effect.Apply(50f);

        // Assert
        Assert.Equal(100, result);
    }

    [Fact]
    public void Apply_Add_AddsToValue()
    {
        // Arrange
        var effect = GoapEffect.Parse("+25");

        // Act
        var result = effect.Apply(50f);

        // Assert
        Assert.Equal(75f, result);
    }

    [Fact]
    public void Apply_Subtract_SubtractsFromValue()
    {
        // Arrange
        var effect = GoapEffect.Parse("-0.3");

        // Act
        var result = effect.Apply(0.8f);

        // Assert
        Assert.Equal(0.5f, (float)result, 0.001);
    }

    [Fact]
    public void Apply_Add_ToNull_TreatsAsZero()
    {
        // Arrange
        var effect = GoapEffect.Parse("+10");

        // Act
        var result = effect.Apply(null);

        // Assert
        Assert.Equal(10f, result);
    }

    [Fact]
    public void Apply_Subtract_FromNull_TreatsAsZero()
    {
        // Arrange
        var effect = GoapEffect.Parse("-5");

        // Act
        var result = effect.Apply(null);

        // Assert
        Assert.Equal(-5f, result);
    }

    [Fact]
    public void Apply_Set_Boolean_SetsBoolean()
    {
        // Arrange
        var effect = GoapEffect.Parse("true");

        // Act
        var result = effect.Apply(false);

        // Assert
        Assert.Equal(true, result);
    }

    [Fact]
    public void Apply_Set_String_SetsString()
    {
        // Arrange
        var effect = GoapEffect.Parse("'new_location'");

        // Act
        var result = effect.Apply("old_location");

        // Assert
        Assert.Equal("new_location", result);
    }

    #endregion

    #region GoapActionEffects Container

    [Fact]
    public void Constructor_CreatesEmptyContainer()
    {
        // Arrange & Act
        var effects = new GoapActionEffects();

        // Assert
        Assert.Equal(0, effects.Count);
    }

    [Fact]
    public void FromDictionary_ParsesAllEffects()
    {
        // Arrange
        var effectStrings = new Dictionary<string, string>
        {
            { "gold", "+10" },
            { "hunger", "-0.5" },
            { "location", "tavern" }
        };

        // Act
        var effects = GoapActionEffects.FromDictionary(effectStrings);

        // Assert
        Assert.Equal(3, effects.Count);
        Assert.True(effects.HasEffect("gold"));
        Assert.True(effects.HasEffect("hunger"));
        Assert.True(effects.HasEffect("location"));
    }

    [Fact]
    public void AddEffect_AddsToContainer()
    {
        // Arrange
        var effects = new GoapActionEffects();
        var effect = GoapEffect.Parse("+100");

        // Act
        effects.AddEffect("score", effect);

        // Assert
        Assert.Equal(1, effects.Count);
        Assert.True(effects.HasEffect("score"));
    }

    [Fact]
    public void GetEffect_ReturnsEffect_WhenExists()
    {
        // Arrange
        var effects = GoapActionEffects.FromDictionary(new Dictionary<string, string>
        {
            { "health", "+20" }
        });

        // Act
        var effect = effects.GetEffect("health");

        // Assert
        Assert.NotNull(effect);
        Assert.Equal(EffectType.Add, effect.Type);
    }

    [Fact]
    public void GetEffect_ReturnsNull_WhenNotExists()
    {
        // Arrange
        var effects = new GoapActionEffects();

        // Act
        var effect = effects.GetEffect("missing");

        // Assert
        Assert.Null(effect);
    }

    [Fact]
    public void HasEffect_ReturnsFalse_WhenNotExists()
    {
        // Arrange
        var effects = new GoapActionEffects();

        // Act & Assert
        Assert.False(effects.HasEffect("missing"));
    }

    [Fact]
    public void Effects_EnumeratesAllEffects()
    {
        // Arrange
        var effects = GoapActionEffects.FromDictionary(new Dictionary<string, string>
        {
            { "a", "+1" },
            { "b", "-2" },
            { "c", "3" }
        });

        // Act
        var keys = effects.Effects.Select(e => e.Key).ToList();

        // Assert
        Assert.Equal(3, keys.Count);
        Assert.Contains("a", keys);
        Assert.Contains("b", keys);
        Assert.Contains("c", keys);
    }

    #endregion

    #region GoapEffect ToString

    [Fact]
    public void ToString_Add_IncludesPlus()
    {
        // Arrange
        var effect = GoapEffect.Parse("+10");

        // Act
        var result = effect.ToString();

        // Assert
        Assert.Equal("+10", result);
    }

    [Fact]
    public void ToString_Subtract_IncludesMinus()
    {
        // Arrange
        var effect = GoapEffect.Parse("-5");

        // Act
        var result = effect.ToString();

        // Assert
        Assert.Equal("-5", result);
    }

    [Fact]
    public void ToString_Set_ShowsValue()
    {
        // Arrange
        var effect = GoapEffect.Parse("100");

        // Act
        var result = effect.ToString();

        // Assert
        Assert.Equal("100", result);
    }

    #endregion
}
