namespace BeyondImmersion.Bannou.SpriteTheory.Metadata;

/// <summary>
/// Per-frame metadata within a sprite sheet. Describes a single frame's position in an atlas,
/// its animation context, pivot point, and whether it is a mirror of another frame.
/// </summary>
/// <remarks>
/// <para>
/// Captured frames come first (indices 0 through N-1), mirror frames are appended after
/// (indices N through N+M-1). This ordering is deterministic and stable.
/// </para>
/// <para>
/// Mirror frames share their source frame's atlas <see cref="Rect"/> — no duplicate pixels
/// exist in the atlas. The game engine applies a horizontal or vertical flip at render time
/// using the <see cref="IsMirror"/> flag and <see cref="MirrorSourceIndex"/>.
/// </para>
/// </remarks>
/// <param name="Index">Global frame index (0-based, unique across all frames in the sprite sheet).</param>
/// <param name="AtlasIndex">Which atlas image this frame resides in (0 for single-atlas sheets).</param>
/// <param name="AngleName">Angle name — source angle for captured frames, mirror target name for mirrors.</param>
/// <param name="AnimationName">Animation clip name (e.g., "idle", "attack_light").</param>
/// <param name="FrameInAnimation">Frame number within the animation (0-based).</param>
/// <param name="Rect">Position and size in the atlas image.</param>
/// <param name="TrimmedRect">Content bounds within the frame if TrimTransparent was enabled. Null otherwise.</param>
/// <param name="Pivot">Pivot point normalized to frame dimensions (0,0 = top-left; default: 0.5, 0.85).</param>
/// <param name="Duration">Frame display duration in seconds.</param>
/// <param name="IsMirror">True if this frame is a horizontal/vertical flip of <see cref="MirrorSourceIndex"/>.</param>
/// <param name="MirrorSourceIndex">Source frame index when <see cref="IsMirror"/> is true. Null for captured frames.</param>
public record SpriteFrame(
    int Index,
    int AtlasIndex,
    string AngleName,
    string AnimationName,
    int FrameInAnimation,
    Rectangle Rect,
    Rectangle? TrimmedRect,
    Vector2 Pivot,
    float Duration,
    bool IsMirror,
    int? MirrorSourceIndex);
