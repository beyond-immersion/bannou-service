namespace BeyondImmersion.Bannou.SpriteTheory.Animation;

/// <summary>
/// Per-animation capture settings that control how frames are sampled from an animation clip.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="TrimStart"/> and <see cref="TrimEnd"/> are normalized values (0.0–1.0)
/// that define a sub-range of the animation to capture. Combined with
/// <see cref="SpeedMultiplier"/>, this allows capturing only the interesting portion
/// of an animation at a modified playback speed.
/// </para>
/// </remarks>
/// <param name="FrameCount">Number of frames to capture from this animation. Defaults to 8.</param>
/// <param name="SpeedMultiplier">Playback speed override (1.0 = normal speed). Defaults to 1.0.</param>
/// <param name="TrimStart">Normalized start time — skip the beginning of the animation (0.0–1.0). Defaults to 0.0.</param>
/// <param name="TrimEnd">Normalized end time — skip the end of the animation (0.0–1.0). Defaults to 1.0.</param>
/// <param name="LoopMode">Loop mode for the output sprite animation metadata. Defaults to None.</param>
public record AnimationConfig(
    int FrameCount = 8,
    float SpeedMultiplier = 1.0f,
    float TrimStart = 0.0f,
    float TrimEnd = 1.0f,
    LoopMode LoopMode = LoopMode.None);
