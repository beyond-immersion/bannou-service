namespace BeyondImmersion.Bannou.SpriteTheory.Metadata;

/// <summary>
/// Groups frames by animation name with per-angle frame index lookup for game runtime use.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="AngleFrameMap"/> is the primary runtime lookup. To play an animation facing a
/// specific direction, read <c>AngleFrameMap[angleName]</c> to get an ordered array of frame
/// indices, then look up each <see cref="SpriteFrame"/> by index for atlas rect, duration,
/// and mirror info. Mirror angles (e.g., "NW") are included in the map — they point to
/// mirror SpriteFrame entries.
/// </para>
/// </remarks>
/// <param name="Name">Animation clip name (matches source animation name).</param>
/// <param name="LoopMode">Playback loop behavior (None, Loop, or PingPong).</param>
/// <param name="TotalDuration">Total animation duration in seconds.</param>
/// <param name="AngleFrameMap">Angle name to ordered frame index array. Includes both captured and mirror angles.</param>
/// <param name="Events">Optional per-frame event markers (hit frames, sound cues). Null if no events.</param>
public record SpriteAnimation(
    string Name,
    LoopMode LoopMode,
    float TotalDuration,
    Dictionary<string, int[]> AngleFrameMap,
    IReadOnlyList<AnimationEvent>? Events);
