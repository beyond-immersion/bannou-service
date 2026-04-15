namespace BeyondImmersion.Bannou.SpriteTheory.Animation;

/// <summary>
/// Animation metadata reported by the engine bridge after loading a 3D model.
/// Describes a single animation clip's properties as read from the source data (FBX/glTF).
/// </summary>
/// <param name="Name">Animation clip name (e.g., "idle", "attack_light", "run").</param>
/// <param name="Duration">Total animation duration in seconds.</param>
/// <param name="FrameCount">Source frame count from the animation data.</param>
/// <param name="IsLooping">Whether the source animation clip is configured to loop.</param>
public record AnimationInfo(
    string Name,
    float Duration,
    int FrameCount,
    bool IsLooping);
