namespace BeyondImmersion.Bannou.SpriteTheory.Animation;

/// <summary>
/// An ordered list of normalized frame timestamps for one animation capture,
/// produced by <see cref="AnimationSampling"/>.
/// </summary>
/// <remarks>
/// Each timestamp is a normalized value (0.0–1.0) representing when within the
/// animation duration the frame should be captured. Timestamps are placed at the
/// center of each time window for uniform sampling.
/// </remarks>
/// <param name="Timestamps">Normalized times (0.0–1.0) for each frame to capture.</param>
/// <param name="Duration">Effective animation duration in seconds (after trim and speed adjustments).</param>
/// <param name="FrameCount">Number of frames in the sequence.</param>
public record FrameSequence(
    IReadOnlyList<float> Timestamps,
    float Duration,
    int FrameCount);
