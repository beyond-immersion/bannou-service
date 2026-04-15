using BeyondImmersion.Bannou.SpriteTheory.Animation;
using Xunit;

namespace BeyondImmersion.Bannou.SpriteTheory.Tests.Animation;

public class AnimationSamplingTests
{
    // --- GenerateUniform ---

    [Fact]
    public void GenerateUniform_EightFrames_CenterOfWindow()
    {
        var result = AnimationSampling.GenerateUniform(duration: 1.0f, frameCount: 8);

        var expected = new[] { 0.0625f, 0.1875f, 0.3125f, 0.4375f, 0.5625f, 0.6875f, 0.8125f, 0.9375f };
        Assert.Equal(expected.Length, result.Timestamps.Count);

        for (var i = 0; i < expected.Length; i++)
        {
            Assert.True(MathF.Abs(result.Timestamps[i] - expected[i]) < 0.001f,
                $"Timestamp[{i}]: expected {expected[i]}, got {result.Timestamps[i]}");
        }
    }

    [Fact]
    public void GenerateUniform_OneFrame_PointFive()
    {
        var result = AnimationSampling.GenerateUniform(duration: 2.0f, frameCount: 1);

        Assert.Single(result.Timestamps);
        Assert.True(MathF.Abs(result.Timestamps[0] - 0.5f) < 0.001f,
            $"Expected 0.5, got {result.Timestamps[0]}");
    }

    [Fact]
    public void GenerateUniform_ZeroFrames_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() =>
            AnimationSampling.GenerateUniform(duration: 1.0f, frameCount: 0));
    }

    [Fact]
    public void GenerateUniform_NegativeDuration_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() =>
            AnimationSampling.GenerateUniform(duration: -1.0f, frameCount: 8));
    }

    [Fact]
    public void GenerateUniform_FrameCountMatchesOutput()
    {
        var result = AnimationSampling.GenerateUniform(duration: 1.0f, frameCount: 12);

        Assert.Equal(12, result.FrameCount);
        Assert.Equal(12, result.Timestamps.Count);
    }

    [Fact]
    public void GenerateUniform_DurationPassedThrough()
    {
        var result = AnimationSampling.GenerateUniform(duration: 3.5f, frameCount: 4);

        Assert.True(MathF.Abs(result.Duration - 3.5f) < 0.001f,
            $"Expected Duration=3.5, got {result.Duration}");
    }

    // --- GenerateFromConfig ---

    [Fact]
    public void GenerateFromConfig_DefaultConfig_MatchesUniform()
    {
        var info = new AnimationInfo(Name: "idle", Duration: 1.0f, FrameCount: 30, IsLooping: true);
        var config = new AnimationConfig(); // defaults: 8 frames, speed 1.0, trim 0-1

        var fromConfig = AnimationSampling.GenerateFromConfig(info, config);
        var uniform = AnimationSampling.GenerateUniform(duration: 1.0f, frameCount: 8);

        Assert.Equal(uniform.FrameCount, fromConfig.FrameCount);
        Assert.Equal(uniform.Timestamps.Count, fromConfig.Timestamps.Count);

        for (var i = 0; i < uniform.Timestamps.Count; i++)
        {
            Assert.True(MathF.Abs(fromConfig.Timestamps[i] - uniform.Timestamps[i]) < 0.001f,
                $"Timestamp[{i}]: expected {uniform.Timestamps[i]}, got {fromConfig.Timestamps[i]}");
        }
    }

    [Fact]
    public void GenerateFromConfig_TrimRange_AdjustsTimestamps()
    {
        var info = new AnimationInfo(Name: "attack", Duration: 2.0f, FrameCount: 60, IsLooping: false);
        var config = new AnimationConfig(FrameCount: 4, TrimStart: 0.25f, TrimEnd: 0.75f);

        var result = AnimationSampling.GenerateFromConfig(info, config);

        // All timestamps should be within [0.25, 0.75]
        foreach (var ts in result.Timestamps)
        {
            Assert.True(ts >= 0.25f - 0.001f,
                $"Timestamp {ts} is below TrimStart 0.25");
            Assert.True(ts <= 0.75f + 0.001f,
                $"Timestamp {ts} is above TrimEnd 0.75");
        }
    }

    [Fact]
    public void GenerateFromConfig_SpeedMultiplier_AffectsDuration()
    {
        var info = new AnimationInfo(Name: "run", Duration: 1.0f, FrameCount: 24, IsLooping: true);
        var config = new AnimationConfig(FrameCount: 8, SpeedMultiplier: 2.0f);

        var result = AnimationSampling.GenerateFromConfig(info, config);

        // effectiveDuration = info.Duration * effectiveRange / speedMultiplier
        // = 1.0 * 1.0 / 2.0 = 0.5
        Assert.True(MathF.Abs(result.Duration - 0.5f) < 0.001f,
            $"Expected Duration=0.5, got {result.Duration}");
    }

    [Fact]
    public void GenerateFromConfig_InvalidTrimRange_ThrowsArgumentException()
    {
        var info = new AnimationInfo(Name: "idle", Duration: 1.0f, FrameCount: 30, IsLooping: true);
        var config = new AnimationConfig(FrameCount: 8, TrimStart: 0.75f, TrimEnd: 0.25f);

        Assert.Throws<ArgumentException>(() =>
            AnimationSampling.GenerateFromConfig(info, config));
    }

    [Fact]
    public void GenerateFromConfig_ZeroFrameCount_ThrowsArgumentException()
    {
        var info = new AnimationInfo(Name: "idle", Duration: 1.0f, FrameCount: 30, IsLooping: true);
        var config = new AnimationConfig(FrameCount: 0);

        Assert.Throws<ArgumentException>(() =>
            AnimationSampling.GenerateFromConfig(info, config));
    }
}
