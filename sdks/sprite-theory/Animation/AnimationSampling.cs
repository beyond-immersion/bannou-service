namespace BeyondImmersion.Bannou.SpriteTheory.Animation;

/// <summary>
/// Generates frame timestamp sequences for capturing animation frames at uniform intervals.
/// </summary>
public static class AnimationSampling
{
    /// <summary>
    /// Generates a uniform frame sequence spanning the full animation duration.
    /// Each timestamp is placed at the center of its time window.
    /// </summary>
    /// <remarks>
    /// For 8 frames, produces timestamps: [0.0625, 0.1875, 0.3125, 0.4375, 0.5625, 0.6875, 0.8125, 0.9375].
    /// Center-of-window placement avoids capturing the exact start (often a T-pose) or exact end of animations.
    /// </remarks>
    /// <param name="duration">Total animation duration in seconds. Must be positive.</param>
    /// <param name="frameCount">Number of frames to capture. Must be positive.</param>
    /// <returns>A <see cref="FrameSequence"/> with uniformly distributed timestamps.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="frameCount"/> or <paramref name="duration"/> is not positive.</exception>
    public static FrameSequence GenerateUniform(float duration, int frameCount)
    {
        if (frameCount <= 0)
        {
            throw new ArgumentException("Frame count must be positive.", nameof(frameCount));
        }

        if (duration <= 0f)
        {
            throw new ArgumentException("Duration must be positive.", nameof(duration));
        }

        var timestamps = new float[frameCount];
        var interval = 1.0f / frameCount;

        for (var i = 0; i < frameCount; i++)
        {
            timestamps[i] = i * interval + interval * 0.5f;
        }

        return new FrameSequence(
            Timestamps: timestamps,
            Duration: duration,
            FrameCount: frameCount);
    }

    /// <summary>
    /// Generates a frame sequence from an animation info and capture configuration,
    /// applying trim range and speed multiplier adjustments.
    /// </summary>
    /// <remarks>
    /// Timestamps are distributed uniformly within the [TrimStart, TrimEnd] range.
    /// The effective duration accounts for both the trim range and speed multiplier.
    /// </remarks>
    /// <param name="info">Animation metadata from the engine bridge.</param>
    /// <param name="config">Per-animation capture configuration.</param>
    /// <returns>A <see cref="FrameSequence"/> with timestamps adjusted for trim and speed.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <see cref="AnimationConfig.FrameCount"/> is not positive,
    /// or <see cref="AnimationConfig.TrimEnd"/> is not greater than <see cref="AnimationConfig.TrimStart"/>.
    /// </exception>
    public static FrameSequence GenerateFromConfig(AnimationInfo info, AnimationConfig config)
    {
        if (config.FrameCount <= 0)
        {
            throw new ArgumentException("Frame count must be positive.", nameof(config));
        }

        if (config.TrimEnd <= config.TrimStart)
        {
            throw new ArgumentException("TrimEnd must be greater than TrimStart.", nameof(config));
        }

        var effectiveRange = config.TrimEnd - config.TrimStart;
        var effectiveDuration = info.Duration * effectiveRange / config.SpeedMultiplier;
        var interval = effectiveRange / config.FrameCount;

        var timestamps = new float[config.FrameCount];

        for (var i = 0; i < config.FrameCount; i++)
        {
            timestamps[i] = config.TrimStart + i * interval + interval * 0.5f;
        }

        return new FrameSequence(
            Timestamps: timestamps,
            Duration: effectiveDuration,
            FrameCount: config.FrameCount);
    }
}
