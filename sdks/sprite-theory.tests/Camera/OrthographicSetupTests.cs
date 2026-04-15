using BeyondImmersion.Bannou.SpriteTheory;
using BeyondImmersion.Bannou.SpriteTheory.Camera;
using Xunit;

namespace BeyondImmersion.Bannou.SpriteTheory.Tests.Camera;

public class OrthographicSetupTests
{
    private static readonly BoundingBox UnitCube = new(
        min: (-0.5f, -0.5f, -0.5f),
        max: (0.5f, 0.5f, 0.5f));

    private static readonly (int Width, int Height) DefaultFrameSize = (128, 128);

    [Fact]
    public void Compute_NorthAngle_DirectionPointsForward()
    {
        var angle = new CaptureAngle(Name: "N", Yaw: 0f, Pitch: 0f);

        var result = OrthographicSetup.Compute(angle, UnitCube, DefaultFrameSize);

        // Yaw=0, Pitch=0: direction should be primarily along +Z (forward)
        // dirX = sin(0)*cos(0) = 0, dirY = sin(0) = 0, dirZ = cos(0)*cos(0) = 1
        Assert.True(MathF.Abs(result.Direction.X) < 0.001f,
            $"Expected Direction.X near 0, got {result.Direction.X}");
        Assert.True(MathF.Abs(result.Direction.Y) < 0.001f,
            $"Expected Direction.Y near 0, got {result.Direction.Y}");
        Assert.True(MathF.Abs(result.Direction.Z - 1f) < 0.001f,
            $"Expected Direction.Z near 1, got {result.Direction.Z}");
    }

    [Fact]
    public void Compute_EastAngle_DirectionPointsRight()
    {
        var angle = new CaptureAngle(Name: "E", Yaw: 90f, Pitch: 0f);

        var result = OrthographicSetup.Compute(angle, UnitCube, DefaultFrameSize);

        // Yaw=90, Pitch=0: direction should have positive X
        Assert.True(result.Direction.X > 0.9f,
            $"Expected Direction.X > 0.9, got {result.Direction.X}");
    }

    [Fact]
    public void Compute_TopDownAngle_DirectionPointsDown()
    {
        var angle = new CaptureAngle(Name: "top", Yaw: 0f, Pitch: -90f);

        var result = OrthographicSetup.Compute(angle, UnitCube, DefaultFrameSize);

        // Yaw=0, Pitch=-90: direction should have negative Y (looking down)
        Assert.True(result.Direction.Y < -0.9f,
            $"Expected Direction.Y < -0.9, got {result.Direction.Y}");
    }

    [Fact]
    public void Compute_UnitCube_OrthoContainsAllCorners()
    {
        var angle = new CaptureAngle(Name: "N", Yaw: 0f, Pitch: 0f);

        var result = OrthographicSetup.Compute(angle, UnitCube, DefaultFrameSize);

        // Ortho dimensions must be at least as large as the projected bounding box
        // The unit cube projected from the front has width 1.0 and height 1.0
        // With 10% safety margin, ortho should be >= 1.1
        Assert.True(result.OrthoWidth >= 1.0f,
            $"Expected OrthoWidth >= 1.0, got {result.OrthoWidth}");
        Assert.True(result.OrthoHeight >= 1.0f,
            $"Expected OrthoHeight >= 1.0, got {result.OrthoHeight}");
    }

    [Fact]
    public void Compute_NearVerticalPitch_HandlesGimbalLock()
    {
        // Pitch > 89° triggers the alternative up vector path
        var angle = new CaptureAngle(Name: "top", Yaw: 0f, Pitch: -89.5f);

        var result = OrthographicSetup.Compute(angle, UnitCube, DefaultFrameSize);

        // Should produce valid (non-zero) ortho parameters
        Assert.True(result.OrthoWidth > 0f, "OrthoWidth should be positive");
        Assert.True(result.OrthoHeight > 0f, "OrthoHeight should be positive");

        // Direction should be near-vertical (Y strongly negative)
        Assert.True(result.Direction.Y < -0.9f,
            $"Expected Direction.Y < -0.9, got {result.Direction.Y}");
    }

    [Fact]
    public void Compute_AspectRatio_MatchesFrameSize()
    {
        var angle = new CaptureAngle(Name: "N", Yaw: 0f, Pitch: 0f);
        var wideFrame = (Width: 256, Height: 128);

        var result = OrthographicSetup.Compute(angle, UnitCube, wideFrame);

        var frameAspect = (float)wideFrame.Width / wideFrame.Height;
        var orthoAspect = result.OrthoWidth / result.OrthoHeight;
        Assert.True(MathF.Abs(orthoAspect - frameAspect) < 0.01f,
            $"Expected ortho aspect ratio {frameAspect}, got {orthoAspect}");
    }

    [Fact]
    public void Compute_MarginApplied()
    {
        var angle = new CaptureAngle(Name: "N", Yaw: 0f, Pitch: 0f);

        var result = OrthographicSetup.Compute(angle, UnitCube, DefaultFrameSize);

        // The unit cube viewed from the front projects to a 1×1 square.
        // With 10% safety margin, the ortho dimensions should be >= 1.1
        // (before aspect ratio adjustment which may increase one axis)
        var minDimension = MathF.Min(result.OrthoWidth, result.OrthoHeight);
        Assert.True(minDimension >= 1.1f * 0.99f,
            $"Expected ortho dimensions to include safety margin, min dimension = {minDimension}");
    }

    [Fact]
    public void Compute_FarPlane_IsThreeTimesDistance()
    {
        var angle = new CaptureAngle(Name: "N", Yaw: 0f, Pitch: 0f);

        var result = OrthographicSetup.Compute(angle, UnitCube, DefaultFrameSize);

        // distance = halfDiag * 2.5, farPlane = distance * 3
        var extents = UnitCube.Extents;
        var halfDiag = MathF.Sqrt(
            extents.X * extents.X +
            extents.Y * extents.Y +
            extents.Z * extents.Z);
        var expectedDistance = halfDiag * 2.5f;
        var expectedFarPlane = expectedDistance * 3f;

        Assert.True(MathF.Abs(result.FarPlane - expectedFarPlane) < 0.01f,
            $"Expected FarPlane={expectedFarPlane}, got {result.FarPlane}");
    }

    [Fact]
    public void Compute_Deterministic_SameInputSameOutput()
    {
        var angle = new CaptureAngle(Name: "SE", Yaw: 135f, Pitch: -45f);
        var bounds = new BoundingBox((-1f, 0f, -1f), (1f, 2f, 1f));
        var frameSize = (Width: 96, Height: 96);

        var result1 = OrthographicSetup.Compute(angle, bounds, frameSize);
        var result2 = OrthographicSetup.Compute(angle, bounds, frameSize);

        Assert.Equal(result1, result2);
    }
}
