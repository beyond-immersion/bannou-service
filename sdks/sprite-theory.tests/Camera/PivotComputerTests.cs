using BeyondImmersion.Bannou.SpriteTheory;
using BeyondImmersion.Bannou.SpriteTheory.Camera;
using Xunit;

namespace BeyondImmersion.Bannou.SpriteTheory.Tests.Camera;

public class PivotComputerTests
{
    private static readonly BoundingBox UnitCube = new(
        min: (-0.5f, -0.5f, -0.5f),
        max: (0.5f, 0.5f, 0.5f));

    private static readonly BoundingBox HumanoidBounds = new(
        min: (-0.5f, 0f, -0.5f),
        max: (0.5f, 2f, 0.5f));

    private static readonly (int Width, int Height) DefaultFrameSize = (128, 128);

    [Fact]
    public void DefaultHumanoidPivot_IsCenterXWith85PercentFromTop()
    {
        Assert.Equal(0.5f, PivotComputer.DefaultHumanoidPivot.X);
        Assert.Equal(0.85f, PivotComputer.DefaultHumanoidPivot.Y);
    }

    [Fact]
    public void ComputeFromBounds_UnitCube_NorthAngle_PivotIsCenterXNearBottom()
    {
        // Unit cube, camera N (yaw=0, pitch=0). Feet at world (0, -0.5, 0).
        // Camera center is at bounds.Center = (0, 0, 0), so feet project to u=0 (center X)
        // and v=-0.5 (below center in camera vertical axis).
        // orthoWidth and orthoHeight come from OrthographicSetup with 10% margin.
        var angle = new CaptureAngle(Name: "N", Yaw: 0f, Pitch: 0f);
        var camera = OrthographicSetup.Compute(angle, UnitCube, DefaultFrameSize);

        var pivot = PivotComputer.ComputeFromBounds(UnitCube, camera);

        // Horizontal pivot is exactly center.
        Assert.Equal(0.5f, pivot.X, precision: 4);
        // Vertical pivot is below center (feet at bottom of the frame minus margin).
        Assert.True(pivot.Y > 0.5f, $"Expected pivot.Y > 0.5 (feet below center), got {pivot.Y}");
        // And within the frame.
        Assert.True(pivot.Y <= 1.0f, $"Expected pivot.Y <= 1.0 (inside frame), got {pivot.Y}");
    }

    [Fact]
    public void ComputeFromBounds_Humanoid_NorthAngle_PivotNearBottom()
    {
        // 1x2x1 humanoid box centered at (0, 1, 0). Feet at world (0, 0, 0).
        var angle = new CaptureAngle(Name: "N", Yaw: 0f, Pitch: 0f);
        var camera = OrthographicSetup.Compute(angle, HumanoidBounds, DefaultFrameSize);

        var pivot = PivotComputer.ComputeFromBounds(HumanoidBounds, camera);

        // X is centered.
        Assert.Equal(0.5f, pivot.X, precision: 4);
        // Y places feet near the bottom; for a Y-symmetric bounding box with 10% margin,
        // feet should land roughly at (1 - margin/2) ≈ 0.95.
        Assert.True(pivot.Y > 0.9f, $"Expected pivot.Y > 0.9 for standing humanoid, got {pivot.Y}");
        Assert.True(pivot.Y <= 1.0f, $"Expected pivot.Y <= 1.0, got {pivot.Y}");
    }

    [Fact]
    public void ComputeFromBounds_TopDown55_FeetStillProjectBelowCenter()
    {
        // At a steep top-down angle, the camera looks down at the model. Feet still
        // project to the bottom of the frame because correctedUp tilts to include the
        // vertical world component.
        var angle = new CaptureAngle(Name: "top", Yaw: 0f, Pitch: -55f);
        var camera = OrthographicSetup.Compute(angle, HumanoidBounds, DefaultFrameSize);

        var pivot = PivotComputer.ComputeFromBounds(HumanoidBounds, camera);

        Assert.Equal(0.5f, pivot.X, precision: 4);
        Assert.True(pivot.Y > 0.5f,
            $"Expected pivot.Y > 0.5 (feet below frame center at top-down), got {pivot.Y}");
    }

    [Fact]
    public void ComputeFromBounds_DegenerateCamera_ReturnsDefault()
    {
        // Construct parameters with a degenerate basis: direction parallel to up.
        var degenerate = new OrthographicParameters(
            Position: (0f, 0f, 0f),
            Direction: (0f, 1f, 0f),
            Up: (0f, 1f, 0f),
            OrthoWidth: 2.0f,
            OrthoHeight: 2.0f,
            NearPlane: 0.01f,
            FarPlane: 10f);

        var pivot = PivotComputer.ComputeFromBounds(UnitCube, degenerate);

        Assert.Equal(PivotComputer.DefaultHumanoidPivot, pivot);
    }

    [Fact]
    public void ComputeFromBounds_ZeroOrthoWidth_ReturnsDefault()
    {
        var invalidCamera = new OrthographicParameters(
            Position: (0f, 0f, -2f),
            Direction: (0f, 0f, 1f),
            Up: (0f, 1f, 0f),
            OrthoWidth: 0f,
            OrthoHeight: 2.0f,
            NearPlane: 0.01f,
            FarPlane: 10f);

        var pivot = PivotComputer.ComputeFromBounds(UnitCube, invalidCamera);

        Assert.Equal(PivotComputer.DefaultHumanoidPivot, pivot);
    }

    [Fact]
    public void ComputeFromBounds_ZeroOrthoHeight_ReturnsDefault()
    {
        var invalidCamera = new OrthographicParameters(
            Position: (0f, 0f, -2f),
            Direction: (0f, 0f, 1f),
            Up: (0f, 1f, 0f),
            OrthoWidth: 2.0f,
            OrthoHeight: 0f,
            NearPlane: 0.01f,
            FarPlane: 10f);

        var pivot = PivotComputer.ComputeFromBounds(UnitCube, invalidCamera);

        Assert.Equal(PivotComputer.DefaultHumanoidPivot, pivot);
    }

    [Fact]
    public void ComputeFromBounds_ClampsToUnitInterval()
    {
        // Use a tiny orthoWidth/orthoHeight so the feet projection lands far outside [0, 1].
        // This verifies the clamp protects downstream consumers from degenerate pivot values.
        var camera = new OrthographicParameters(
            Position: (0f, 0f, -2f),
            Direction: (0f, 0f, 1f),
            Up: (0f, 1f, 0f),
            OrthoWidth: 0.01f,
            OrthoHeight: 0.01f,
            NearPlane: 0.01f,
            FarPlane: 10f);

        var pivot = PivotComputer.ComputeFromBounds(HumanoidBounds, camera);

        Assert.InRange(pivot.X, 0f, 1f);
        Assert.InRange(pivot.Y, 0f, 1f);
    }

    [Fact]
    public void ComputeFromBounds_Deterministic_SameInputSameOutput()
    {
        var angle = new CaptureAngle(Name: "NE", Yaw: 45f, Pitch: -30f);
        var camera = OrthographicSetup.Compute(angle, HumanoidBounds, DefaultFrameSize);

        var pivot1 = PivotComputer.ComputeFromBounds(HumanoidBounds, camera);
        var pivot2 = PivotComputer.ComputeFromBounds(HumanoidBounds, camera);

        Assert.Equal(pivot1, pivot2);
    }

    [Fact]
    public void ComputeFromBounds_OffCenterHumanoid_PivotReflectsOffset()
    {
        // Humanoid standing off-center in X. Feet should project off-center in pivot X.
        var offCenterBounds = new BoundingBox(
            min: (1f, 0f, -0.5f),
            max: (3f, 2f, 0.5f));
        var angle = new CaptureAngle(Name: "N", Yaw: 0f, Pitch: 0f);
        var camera = OrthographicSetup.Compute(angle, offCenterBounds, DefaultFrameSize);

        var pivot = PivotComputer.ComputeFromBounds(offCenterBounds, camera);

        // Feet at world X=2 (center of bounds X range). bounds.Center.X = 2.
        // Feet projects to u=0 (same as bounds.Center), so pivot.X = 0.5.
        // This verifies that "off-center in world coordinates" doesn't shift pivot —
        // what matters is feet position RELATIVE TO bounds.Center.
        Assert.Equal(0.5f, pivot.X, precision: 4);
    }
}
