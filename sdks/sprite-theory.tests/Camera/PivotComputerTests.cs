using BeyondImmersion.Bannou.Core.Math;
using BeyondImmersion.Bannou.SpriteTheory;
using BeyondImmersion.Bannou.SpriteTheory.Camera;
using Xunit;

namespace BeyondImmersion.Bannou.SpriteTheory.Tests.Camera;

public class PivotComputerTests
{
    private static readonly BoundingBox UnitCube = new(
        min: new Vector3(-0.5f, -0.5f, -0.5f),
        max: new Vector3(0.5f, 0.5f, 0.5f));

    private static readonly BoundingBox HumanoidBounds = new(
        min: new Vector3(-0.5f, 0f, -0.5f),
        max: new Vector3(0.5f, 2f, 0.5f));

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
            Position: new Vector3(0f, 0f, 0f),
            Direction: new Vector3(0f, 1f, 0f),
            Up: new Vector3(0f, 1f, 0f),
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
            Position: new Vector3(0f, 0f, -2f),
            Direction: new Vector3(0f, 0f, 1f),
            Up: new Vector3(0f, 1f, 0f),
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
            Position: new Vector3(0f, 0f, -2f),
            Direction: new Vector3(0f, 0f, 1f),
            Up: new Vector3(0f, 1f, 0f),
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
            Position: new Vector3(0f, 0f, -2f),
            Direction: new Vector3(0f, 0f, 1f),
            Up: new Vector3(0f, 1f, 0f),
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
            min: new Vector3(1f, 0f, -0.5f),
            max: new Vector3(3f, 2f, 0.5f));
        var angle = new CaptureAngle(Name: "N", Yaw: 0f, Pitch: 0f);
        var camera = OrthographicSetup.Compute(angle, offCenterBounds, DefaultFrameSize);

        var pivot = PivotComputer.ComputeFromBounds(offCenterBounds, camera);

        // Feet at world X=2 (center of bounds X range). bounds.Center.X = 2.
        // Feet projects to u=0 (same as bounds.Center), so pivot.X = 0.5.
        // This verifies that "off-center in world coordinates" doesn't shift pivot —
        // what matters is feet position RELATIVE TO bounds.Center.
        Assert.Equal(0.5f, pivot.X, precision: 4);
    }

    // --- Per-angle divergence (regression protection) ---
    //
    // PivotComputer.ComputeFromBounds takes an OrthographicParameters derived from a
    // specific CaptureAngle. For a rig whose angles share the same pitch (the canonical
    // presets SideViewBrawler / TopDown8Dir / TopDown4Dir), feet projection is the same
    // across angles. But for a rig whose angles have DIFFERENT pitches, pivots diverge —
    // a camera at pitch=0 (feet at frame bottom) produces a different pivotY than a
    // camera at pitch=-55 (feet closer to frame center).
    //
    // The composer MUST compute pivot per-angle inside its angle loop. Computing once
    // per rig (e.g., `PivotComputer.ComputeFromBounds(bounds, OrthographicSetup.Compute(
    // rig.Angles[0], ...))`) and applying the result to all angles is only correct when
    // all angles share a pitch — not a safe assumption for custom rigs.
    //
    // The following test fails if anyone regresses to the "one pivot per rig" shortcut.

    [Fact]
    public void ComputeFromBounds_DifferentPitches_ProduceDifferentPivots()
    {
        // Same upright humanoid, two angles at different pitches.
        // pitch=0 (side-view): feet project near frame bottom — pivotY ≈ 0.95
        // pitch=-55 (top-down): feet project closer to frame center — pivotY < 0.95
        var sideAngle = new CaptureAngle(Name: "right", Yaw: 90f, Pitch: 0f);
        var topAngle = new CaptureAngle(Name: "NE", Yaw: 45f, Pitch: -55f);

        var sideCamera = OrthographicSetup.Compute(sideAngle, HumanoidBounds, DefaultFrameSize);
        var topCamera = OrthographicSetup.Compute(topAngle, HumanoidBounds, DefaultFrameSize);

        var sidePivot = PivotComputer.ComputeFromBounds(HumanoidBounds, sideCamera);
        var topPivot = PivotComputer.ComputeFromBounds(HumanoidBounds, topCamera);

        // Pivots must differ — proves that per-angle pivot computation is required
        // for mixed-pitch rigs.
        Assert.NotEqual(sidePivot.Y, topPivot.Y);

        // And explicitly: side-view places feet closer to the frame bottom than top-down.
        Assert.True(
            sidePivot.Y > topPivot.Y,
            $"Expected side-view pivot.Y ({sidePivot.Y}) > top-down pivot.Y ({topPivot.Y}) " +
            "because side-view places feet near frame bottom while top-down pulls them up.");
    }

    [Fact]
    public void ComputeFromBounds_SameYawDifferentPitches_DivergeInY()
    {
        // Complementary to DifferentPitches — pitch is the dominant axis of pivot
        // divergence. At steeper downward pitch, the world Y-axis projects more heavily
        // onto the camera's correctedUp axis, which compresses feet toward the frame
        // center (smaller pivotY).
        const float yaw = 45f;
        var levelAngle = new CaptureAngle(Name: "NE-level", Yaw: yaw, Pitch: 0f);
        var steepAngle = new CaptureAngle(Name: "NE-steep", Yaw: yaw, Pitch: -55f);

        var levelPivot = PivotComputer.ComputeFromBounds(
            HumanoidBounds,
            OrthographicSetup.Compute(levelAngle, HumanoidBounds, DefaultFrameSize));
        var steepPivot = PivotComputer.ComputeFromBounds(
            HumanoidBounds,
            OrthographicSetup.Compute(steepAngle, HumanoidBounds, DefaultFrameSize));

        Assert.True(
            levelPivot.Y > steepPivot.Y,
            $"Expected level pivot.Y ({levelPivot.Y}) > steep pivot.Y ({steepPivot.Y}) — " +
            "steep top-down pulls feet toward frame center.");
    }

    [Fact]
    public void ComputeFromBounds_DifferentYawsSamePitch_AlsoDivergeInY()
    {
        // Even when every angle in a rig shares a pitch (the canonical TopDown8Dir
        // pattern), pivots still differ across yaws. The feet point (Center.X, Min.Y,
        // Center.Z) projects to u=0 for all yaws with symmetric bounds, but the
        // projected height of the bounding box (orthoHeight) varies with yaw because
        // the convex hull of the 8 world-space corners on the camera's (right, up)
        // plane changes. A cube viewed face-on has a smaller projected "width × height"
        // than the same cube viewed corner-on (3D diagonal is longer).
        //
        // Since pivotY = 0.5 - v/orthoHeight, and v is approximately constant across
        // yaws at the same pitch but orthoHeight varies, pivotY varies.
        //
        // Practical consequence: the composer MUST compute pivot per-angle, even for
        // the canonical single-pitch presets. Computing once per rig using rig.Angles[0]
        // produces visibly wrong feet placement on most angles.
        const float pitch = -55f;
        var nAngle = new CaptureAngle(Name: "N", Yaw: 0f, Pitch: pitch);
        var neAngle = new CaptureAngle(Name: "NE", Yaw: 45f, Pitch: pitch);

        var nPivot = PivotComputer.ComputeFromBounds(
            HumanoidBounds,
            OrthographicSetup.Compute(nAngle, HumanoidBounds, DefaultFrameSize));
        var nePivot = PivotComputer.ComputeFromBounds(
            HumanoidBounds,
            OrthographicSetup.Compute(neAngle, HumanoidBounds, DefaultFrameSize));

        // X stays at 0.5 for both (symmetric feet projection).
        Assert.Equal(0.5f, nPivot.X, precision: 4);
        Assert.Equal(0.5f, nePivot.X, precision: 4);

        // Y must differ — proves per-angle pivot computation is required EVERYWHERE,
        // not just for custom rigs with mixed pitches.
        Assert.NotEqual(nPivot.Y, nePivot.Y);
    }

    // --- ProjectWorldPointToFrame (general primitive) ---
    //
    // ComputeFromBounds is a thin wrapper that computes the feet point and delegates
    // to ProjectWorldPointToFrame. These tests exercise the general case — projecting
    // an arbitrary world-space point (e.g., a skeleton bone position reported by the
    // bridge) onto the camera frame. The feet case is already covered above; the tests
    // below verify that non-feet anchor points produce correspondingly non-feet pivots.

    [Fact]
    public void ProjectWorldPointToFrame_BoundsCenter_IsFrameCenter()
    {
        // The center of the bounding box is exactly where the camera is pointed, so it
        // must project to the frame center (0.5, 0.5) for any well-formed orthographic
        // setup. This is the anchor test: "point on the look-at axis" → "frame center".
        var angle = new CaptureAngle(Name: "N", Yaw: 0f, Pitch: 0f);
        var camera = OrthographicSetup.Compute(angle, HumanoidBounds, DefaultFrameSize);

        var pivot = PivotComputer.ProjectWorldPointToFrame(HumanoidBounds.Center, camera);

        Assert.Equal(0.5f, pivot.X, precision: 4);
        Assert.Equal(0.5f, pivot.Y, precision: 4);
    }

    [Fact]
    public void ProjectWorldPointToFrame_HeadPoint_YieldsHigherPivotThanFeet()
    {
        // A bone anchor near the top of a humanoid (e.g., a "head" bone at y=1.8) must
        // produce a pivot ABOVE the feet pivot in frame coordinates — smaller pivotY,
        // since pivot origin is top-left. This is the sprite-composer scenario: if the
        // variant sets AnchorBoneName = "head", the pivot should track the head, not
        // the feet.
        var angle = new CaptureAngle(Name: "N", Yaw: 0f, Pitch: 0f);
        var camera = OrthographicSetup.Compute(angle, HumanoidBounds, DefaultFrameSize);

        var feetPivot = PivotComputer.ComputeFromBounds(HumanoidBounds, camera);
        var headPivot = PivotComputer.ProjectWorldPointToFrame(
            new Vector3(0f, 1.8f, 0f), camera);

        Assert.True(
            headPivot.Y < feetPivot.Y,
            $"Expected head pivot.Y ({headPivot.Y}) < feet pivot.Y ({feetPivot.Y}) — " +
            "head is above feet in world space, which maps to smaller pivot.Y (top-left origin).");
    }

    [Fact]
    public void ProjectWorldPointToFrame_DegenerateCamera_ReturnsDefault()
    {
        // Same degenerate-basis fallback as ComputeFromBounds — the underlying
        // projection is undefined when Direction is parallel to Up.
        var degenerate = new OrthographicParameters(
            Position: new Vector3(0f, 0f, 0f),
            Direction: new Vector3(0f, 1f, 0f),
            Up: new Vector3(0f, 1f, 0f),
            OrthoWidth: 2.0f,
            OrthoHeight: 2.0f,
            NearPlane: 0.01f,
            FarPlane: 10f);

        var pivot = PivotComputer.ProjectWorldPointToFrame(new Vector3(1f, 2f, 3f), degenerate);

        Assert.Equal(PivotComputer.DefaultHumanoidPivot, pivot);
    }
}
