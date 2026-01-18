using BeyondImmersion.Bannou.MusicStoryteller.State;
using Xunit;

namespace BeyondImmersion.Bannou.MusicStoryteller.Tests.State;

/// <summary>
/// Tests for the 6-dimensional EmotionalState class.
/// </summary>
public class EmotionalStateTests
{
    /// <summary>
    /// Default constructor creates neutral state.
    /// </summary>
    [Fact]
    public void Constructor_Default_CreatesNeutralState()
    {
        var state = new EmotionalState();

        Assert.Equal(0.2, state.Tension);
        Assert.Equal(0.5, state.Brightness);
        Assert.Equal(0.5, state.Energy);
        Assert.Equal(0.5, state.Warmth);
        Assert.Equal(0.8, state.Stability);
        Assert.Equal(0.5, state.Valence);
    }

    /// <summary>
    /// Values are clamped to 0-1 range.
    /// </summary>
    [Theory]
    [InlineData(-0.5, 0.0)]
    [InlineData(1.5, 1.0)]
    [InlineData(0.5, 0.5)]
    public void Tension_IsClamped(double input, double expected)
    {
        var state = new EmotionalState { Tension = input };
        Assert.Equal(expected, state.Tension);
    }

    /// <summary>
    /// Distance to self is zero.
    /// </summary>
    [Fact]
    public void DistanceTo_Self_ReturnsZero()
    {
        var state = EmotionalState.Presets.Neutral;
        var distance = state.DistanceTo(state);
        Assert.Equal(0.0, distance);
    }

    /// <summary>
    /// Distance between extremes is maximum (~2.45 for 6 dimensions).
    /// </summary>
    [Fact]
    public void DistanceTo_Extremes_ReturnsMaxDistance()
    {
        var low = new EmotionalState(0, 0, 0, 0, 0, 0);
        var high = new EmotionalState(1, 1, 1, 1, 1, 1);

        var distance = low.DistanceTo(high);

        // sqrt(6) ≈ 2.449
        Assert.True(distance > 2.4 && distance < 2.5,
            $"Max distance ({distance}) should be sqrt(6) ≈ 2.449");
    }

    /// <summary>
    /// Normalized distance is in 0-1 range.
    /// </summary>
    [Fact]
    public void NormalizedDistanceTo_ReturnsValueIn0To1()
    {
        var state1 = EmotionalState.Presets.Peaceful;
        var state2 = EmotionalState.Presets.Tense;

        var normalized = state1.NormalizedDistanceTo(state2);

        Assert.True(normalized >= 0 && normalized <= 1,
            $"Normalized distance ({normalized}) should be in [0,1]");
    }

    /// <summary>
    /// Interpolate at t=0 returns source state.
    /// </summary>
    [Fact]
    public void InterpolateTo_AtZero_ReturnsSource()
    {
        var source = EmotionalState.Presets.Peaceful;
        var target = EmotionalState.Presets.Tense;

        var result = source.InterpolateTo(target, 0);

        Assert.Equal(source.Tension, result.Tension, precision: 5);
        Assert.Equal(source.Brightness, result.Brightness, precision: 5);
        Assert.Equal(source.Energy, result.Energy, precision: 5);
    }

    /// <summary>
    /// Interpolate at t=1 returns target state.
    /// </summary>
    [Fact]
    public void InterpolateTo_AtOne_ReturnsTarget()
    {
        var source = EmotionalState.Presets.Peaceful;
        var target = EmotionalState.Presets.Tense;

        var result = source.InterpolateTo(target, 1);

        Assert.Equal(target.Tension, result.Tension, precision: 5);
        Assert.Equal(target.Brightness, result.Brightness, precision: 5);
        Assert.Equal(target.Energy, result.Energy, precision: 5);
    }

    /// <summary>
    /// Interpolate at t=0.5 returns midpoint.
    /// </summary>
    [Fact]
    public void InterpolateTo_AtHalf_ReturnsMidpoint()
    {
        var source = new EmotionalState(0, 0, 0, 0, 0, 0);
        var target = new EmotionalState(1, 1, 1, 1, 1, 1);

        var result = source.InterpolateTo(target, 0.5);

        Assert.Equal(0.5, result.Tension, precision: 5);
        Assert.Equal(0.5, result.Brightness, precision: 5);
        Assert.Equal(0.5, result.Energy, precision: 5);
        Assert.Equal(0.5, result.Warmth, precision: 5);
        Assert.Equal(0.5, result.Stability, precision: 5);
        Assert.Equal(0.5, result.Valence, precision: 5);
    }

    /// <summary>
    /// Clone creates independent copy.
    /// </summary>
    [Fact]
    public void Clone_CreatesIndependentCopy()
    {
        var original = EmotionalState.Presets.Climax;
        var clone = original.Clone();

        clone.Tension = 0.0;

        Assert.NotEqual(original.Tension, clone.Tension);
        Assert.Equal(0.95, original.Tension); // Climax tension
    }

    /// <summary>
    /// Presets have expected characteristics.
    /// </summary>
    [Fact]
    public void Presets_HaveExpectedCharacteristics()
    {
        Assert.True(EmotionalState.Presets.Climax.Tension > 0.9, "Climax should have high tension");
        Assert.True(EmotionalState.Presets.Peaceful.Tension < 0.2, "Peaceful should have low tension");
        Assert.True(EmotionalState.Presets.Joyful.Valence > 0.8, "Joyful should have high valence");
        Assert.True(EmotionalState.Presets.Melancholic.Valence < 0.3, "Melancholic should have low valence");
        Assert.True(EmotionalState.Presets.Resolution.Stability > 0.9, "Resolution should have high stability");
    }

    /// <summary>
    /// Neutral static property returns same as Presets.Neutral.
    /// </summary>
    [Fact]
    public void Neutral_MatchesPresetsNeutral()
    {
        var neutral = EmotionalState.Neutral;
        var presetsNeutral = EmotionalState.Presets.Neutral;

        Assert.Equal(presetsNeutral.Tension, neutral.Tension);
        Assert.Equal(presetsNeutral.Brightness, neutral.Brightness);
        Assert.Equal(presetsNeutral.Stability, neutral.Stability);
    }

    /// <summary>
    /// ToString produces readable format.
    /// </summary>
    [Fact]
    public void ToString_ProducesReadableFormat()
    {
        var state = EmotionalState.Presets.Neutral;
        var str = state.ToString();

        Assert.Contains("Emotional", str);
        Assert.Contains("T=", str);
        Assert.Contains("B=", str);
    }
}
