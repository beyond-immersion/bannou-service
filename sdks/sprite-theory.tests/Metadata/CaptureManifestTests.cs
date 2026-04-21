using BeyondImmersion.Bannou.SpriteTheory;
using BeyondImmersion.Bannou.SpriteTheory.Animation;
using BeyondImmersion.Bannou.SpriteTheory.Camera;
using BeyondImmersion.Bannou.SpriteTheory.Metadata;
using Xunit;

namespace BeyondImmersion.Bannou.SpriteTheory.Tests.Metadata;

public class CaptureManifestTests
{
    private static CharacterVariant CreateTestVariant()
    {
        return new CharacterVariant(
            Name: "test",
            Model: new AssetReference("test-bundle", "model"),
            Equipment: Array.Empty<EquipmentSlot>());
    }

    private static IReadOnlyList<(AnimationInfo Info, AnimationConfig Config)> CreateAnimations(
        int animationCount, int framesPerAnimation)
    {
        var animations = new List<(AnimationInfo, AnimationConfig)>();
        for (var i = 0; i < animationCount; i++)
        {
            animations.Add((
                new AnimationInfo($"anim_{i}", 1.0f, 24, false),
                new AnimationConfig(FrameCount: framesPerAnimation)));
        }

        return animations;
    }

    [Fact]
    public void Compute_DefendersScenario_CorrectFrameCounts()
    {
        // TopDown8Dir: 5 angles (3 mirrors) + SideViewBrawler: 1 angle (1 mirror)
        // 20 animations x 8 frames each
        var variant = CreateTestVariant();
        var rigs = new List<CameraRig>
        {
            CameraRigPresets.TopDown8Dir(),
            CameraRigPresets.SideViewBrawler()
        };
        var animations = CreateAnimations(20, 8);

        var manifest = CaptureManifest.Compute(variant, rigs, animations);

        // TopDown8Dir:    5 angles * 20 anims * 8 frames = 800 captured, 3 mirrors * 20 * 8 = 480 mirror
        // SideViewBrawler: 1 angle * 20 anims * 8 frames = 160 captured, 1 mirror * 20 * 8 = 160 mirror
        // Total: 960 captured + 640 mirror = 1600 total
        Assert.Equal(960, manifest.TotalCapturedFrames);
        Assert.Equal(640, manifest.TotalMirrorFrames);
        Assert.Equal(1600, manifest.TotalFrames);
        Assert.Equal(48000, manifest.EstimatedCaptureTimeMs); // 960 * 50ms
    }

    [Fact]
    public void Compute_SingleRig_CorrectCounts()
    {
        var variant = CreateTestVariant();
        var rigs = new List<CameraRig> { CameraRigPresets.SideViewBrawler() };
        var animations = CreateAnimations(1, 4);

        var manifest = CaptureManifest.Compute(variant, rigs, animations);

        // 1 angle * 1 anim * 4 frames = 4 captured
        // 1 mirror * 1 anim * 4 frames = 4 mirror
        Assert.Equal(4, manifest.TotalCapturedFrames);
        Assert.Equal(4, manifest.TotalMirrorFrames);
        Assert.Equal(8, manifest.TotalFrames);
        Assert.Equal(200, manifest.EstimatedCaptureTimeMs); // 4 * 50ms
    }

    [Fact]
    public void Compute_NoMirrors_ZeroMirrorCount()
    {
        var variant = CreateTestVariant();
        // Create a rig with no mirrors
        var angles = new[]
        {
            new CaptureAngle(Name: "N", Yaw: 0f, Pitch: -55f, ProducesMirror: false),
            new CaptureAngle(Name: "S", Yaw: 180f, Pitch: -55f, ProducesMirror: false)
        };
        var rig = new CameraRig(
            Name: "NoMirror",
            Projection: ProjectionType.Orthographic,
            Angles: angles,
            FrameSize: (96, 96),
            Padding: 2,
            BackgroundColor: Color.Transparent,
            IncludeNormalMap: false,
            TrimTransparent: false);

        var rigs = new List<CameraRig> { rig };
        var animations = CreateAnimations(3, 10);

        var manifest = CaptureManifest.Compute(variant, rigs, animations);

        // 2 angles * 3 anims * 10 frames = 60 captured, 0 mirrors
        Assert.Equal(60, manifest.TotalCapturedFrames);
        Assert.Equal(0, manifest.TotalMirrorFrames);
        Assert.Equal(60, manifest.TotalFrames);
    }

    [Fact]
    public void Compute_AnimationCount_MatchesInput()
    {
        var variant = CreateTestVariant();
        var rigs = new List<CameraRig> { CameraRigPresets.TopDown4Dir() };
        var animations = CreateAnimations(7, 8);

        var manifest = CaptureManifest.Compute(variant, rigs, animations);

        Assert.Equal(7, manifest.AnimationCount);
    }

    [Fact]
    public void Compute_RigManifests_PerRigBreakdown()
    {
        var variant = CreateTestVariant();
        var rigs = new List<CameraRig>
        {
            CameraRigPresets.TopDown8Dir(),
            CameraRigPresets.SideViewBrawler()
        };
        var animations = CreateAnimations(5, 10);

        var manifest = CaptureManifest.Compute(variant, rigs, animations);

        Assert.Equal(2, manifest.Rigs.Count);

        // TopDown8Dir: 5 angles, 3 mirrors
        var topDown = manifest.Rigs[0];
        Assert.Equal("TopDown-8Dir", topDown.RigName);
        Assert.Equal(5, topDown.AngleCount);
        Assert.Equal(3, topDown.MirrorCount);
        Assert.Equal(250, topDown.CapturedFrames);  // 5 * 5 * 10
        Assert.Equal(150, topDown.MirrorFrames);     // 3 * 5 * 10

        // SideViewBrawler: 1 angle, 1 mirror
        var sideView = manifest.Rigs[1];
        Assert.Equal("SideView-Brawler", sideView.RigName);
        Assert.Equal(1, sideView.AngleCount);
        Assert.Equal(1, sideView.MirrorCount);
        Assert.Equal(50, sideView.CapturedFrames);   // 1 * 5 * 10
        Assert.Equal(50, sideView.MirrorFrames);      // 1 * 5 * 10
    }
}
