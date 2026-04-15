namespace BeyondImmersion.Bannou.SpriteTheory.Camera;

/// <summary>
/// Defines a single capture angle for sprite rendering. Every CaptureAngle in a
/// <see cref="CameraRig"/> is rendered by the bridge — this is never a skip flag.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ProducesMirror"/> is additive: when true, MirrorOptimizer generates
/// a flipped counterpart in addition to the captured frame. The angle itself is
/// always captured regardless of this flag.
/// </para>
/// <para>
/// Yaw is measured in degrees around the Y axis (0 = north/forward).
/// Pitch is measured in degrees from horizontal (0 = level, negative = looking down).
/// </para>
/// </remarks>
/// <param name="Name">Angle identifier (e.g., "right", "N", "NE").</param>
/// <param name="Yaw">Degrees rotation around the Y axis (0 = north/forward).</param>
/// <param name="Pitch">Degrees from horizontal (0 = level, negative = looking down).</param>
/// <param name="ProducesMirror">If true, MirrorOptimizer generates a horizontally flipped counterpart.</param>
/// <param name="MirrorTargetName">Name for the generated mirror angle (e.g., "NW"). Null if no mirror.</param>
/// <param name="MirrorAxis">Flip axis for the generated mirror. Defaults to Horizontal.</param>
public record CaptureAngle(
    string Name,
    float Yaw,
    float Pitch,
    bool ProducesMirror = false,
    string? MirrorTargetName = null,
    MirrorAxis MirrorAxis = MirrorAxis.Horizontal);
