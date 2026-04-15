namespace BeyondImmersion.Bannou.SpriteTheory.Mirror;

/// <summary>
/// Describes a single mirror relationship between a captured angle and its generated counterpart.
/// Produced by <see cref="MirrorOptimizer.ComputeMirrors"/> from <see cref="Camera.CaptureAngle"/>
/// metadata on the camera rig.
/// </summary>
/// <remarks>
/// Mirror frames share their source frame's atlas rectangle — no duplicate pixels exist in the atlas.
/// The game engine applies a flip at render time using the <see cref="FlipAxis"/> direction.
/// </remarks>
/// <param name="SourceAngleName">Name of the captured angle that produces this mirror (e.g., "NE").</param>
/// <param name="TargetAngleName">Name for the generated mirror angle (e.g., "NW").</param>
/// <param name="FlipAxis">Horizontal or Vertical flip direction for the mirror.</param>
public record MirrorInfo(
    string SourceAngleName,
    string TargetAngleName,
    MirrorAxis FlipAxis);
