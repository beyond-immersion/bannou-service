namespace BeyondImmersion.Bannou.SpriteTheory.Camera;

/// <summary>
/// Built-in camera rig factory methods for common sprite capture configurations.
/// </summary>
public static class CameraRigPresets
{
    /// <summary>
    /// Creates a side-view brawler rig with one capture angle (right) that produces
    /// a horizontal mirror (left). Result: 1 rendered angle, 1 mirror = 2 total directions.
    /// </summary>
    /// <param name="frameWidth">Per-frame width in pixels. Defaults to 128.</param>
    /// <param name="frameHeight">Per-frame height in pixels. Defaults to 128.</param>
    /// <returns>A camera rig configured for side-view brawler sprite capture.</returns>
    public static CameraRig SideViewBrawler(int frameWidth = 128, int frameHeight = 128)
    {
        var angles = new[]
        {
            new CaptureAngle(
                Name: "right",
                Yaw: 90f,
                Pitch: 0f,
                ProducesMirror: true,
                MirrorTargetName: "left",
                MirrorAxis: MirrorAxis.Horizontal)
        };

        return new CameraRig(
            Name: "SideView-Brawler",
            Projection: ProjectionType.Orthographic,
            Angles: angles,
            FrameSize: (frameWidth, frameHeight),
            Padding: 2,
            BackgroundColor: Color.Transparent,
            IncludeNormalMap: false,
            TrimTransparent: false);
    }

    /// <summary>
    /// Creates a top-down 8-direction rig with 5 capture angles (N, NE, E, SE, S)
    /// where NE, E, and SE produce horizontal mirrors (NW, W, SW).
    /// Result: 5 rendered angles, 3 mirrors = 8 total directions.
    /// </summary>
    /// <param name="pitch">Pitch angle in degrees (negative = looking down). Defaults to -55.</param>
    /// <param name="frameWidth">Per-frame width in pixels. Defaults to 96.</param>
    /// <param name="frameHeight">Per-frame height in pixels. Defaults to 96.</param>
    /// <returns>A camera rig configured for top-down 8-directional sprite capture.</returns>
    public static CameraRig TopDown8Dir(float pitch = -55f, int frameWidth = 96, int frameHeight = 96)
    {
        var angles = new[]
        {
            new CaptureAngle(Name: "N", Yaw: 0f, Pitch: pitch, ProducesMirror: false),
            new CaptureAngle(Name: "NE", Yaw: 45f, Pitch: pitch,
                ProducesMirror: true, MirrorTargetName: "NW", MirrorAxis: MirrorAxis.Horizontal),
            new CaptureAngle(Name: "E", Yaw: 90f, Pitch: pitch,
                ProducesMirror: true, MirrorTargetName: "W", MirrorAxis: MirrorAxis.Horizontal),
            new CaptureAngle(Name: "SE", Yaw: 135f, Pitch: pitch,
                ProducesMirror: true, MirrorTargetName: "SW", MirrorAxis: MirrorAxis.Horizontal),
            new CaptureAngle(Name: "S", Yaw: 180f, Pitch: pitch, ProducesMirror: false)
        };

        return new CameraRig(
            Name: "TopDown-8Dir",
            Projection: ProjectionType.Orthographic,
            Angles: angles,
            FrameSize: (frameWidth, frameHeight),
            Padding: 2,
            BackgroundColor: Color.Transparent,
            IncludeNormalMap: false,
            TrimTransparent: false);
    }

    /// <summary>
    /// Creates a top-down 4-direction rig with 3 capture angles (N, E, S)
    /// where E produces a horizontal mirror (W).
    /// Result: 3 rendered angles, 1 mirror = 4 total directions.
    /// </summary>
    /// <param name="pitch">Pitch angle in degrees (negative = looking down). Defaults to -55.</param>
    /// <param name="frameWidth">Per-frame width in pixels. Defaults to 96.</param>
    /// <param name="frameHeight">Per-frame height in pixels. Defaults to 96.</param>
    /// <returns>A camera rig configured for top-down 4-directional sprite capture.</returns>
    public static CameraRig TopDown4Dir(float pitch = -55f, int frameWidth = 96, int frameHeight = 96)
    {
        var angles = new[]
        {
            new CaptureAngle(Name: "N", Yaw: 0f, Pitch: pitch, ProducesMirror: false),
            new CaptureAngle(Name: "E", Yaw: 90f, Pitch: pitch,
                ProducesMirror: true, MirrorTargetName: "W", MirrorAxis: MirrorAxis.Horizontal),
            new CaptureAngle(Name: "S", Yaw: 180f, Pitch: pitch, ProducesMirror: false)
        };

        return new CameraRig(
            Name: "TopDown-4Dir",
            Projection: ProjectionType.Orthographic,
            Angles: angles,
            FrameSize: (frameWidth, frameHeight),
            Padding: 2,
            BackgroundColor: Color.Transparent,
            IncludeNormalMap: false,
            TrimTransparent: false);
    }
}
