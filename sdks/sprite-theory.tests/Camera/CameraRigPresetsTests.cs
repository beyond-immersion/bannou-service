using BeyondImmersion.Bannou.SpriteTheory;
using BeyondImmersion.Bannou.SpriteTheory.Camera;
using Xunit;

namespace BeyondImmersion.Bannou.SpriteTheory.Tests.Camera;

public class CameraRigPresetsTests
{
    // --- SideViewBrawler ---

    [Fact]
    public void SideViewBrawler_ReturnsOneAngle_WithMirror()
    {
        var rig = CameraRigPresets.SideViewBrawler();

        Assert.Single(rig.Angles);

        var angle = rig.Angles[0];
        Assert.Equal("right", angle.Name);
        Assert.True(MathF.Abs(angle.Yaw - 90f) < 0.001f, $"Expected Yaw=90, got {angle.Yaw}");
        Assert.True(angle.ProducesMirror);
        Assert.Equal("left", angle.MirrorTargetName);
    }

    [Fact]
    public void SideViewBrawler_DefaultFrameSize_Is128x128()
    {
        var rig = CameraRigPresets.SideViewBrawler();

        Assert.Equal(128, rig.FrameSize.Width);
        Assert.Equal(128, rig.FrameSize.Height);
    }

    [Fact]
    public void SideViewBrawler_CustomFrameSize_IsApplied()
    {
        var rig = CameraRigPresets.SideViewBrawler(frameWidth: 256, frameHeight: 192);

        Assert.Equal(256, rig.FrameSize.Width);
        Assert.Equal(192, rig.FrameSize.Height);
    }

    [Fact]
    public void SideViewBrawler_RigProperties_AreCorrect()
    {
        var rig = CameraRigPresets.SideViewBrawler();

        Assert.Equal("SideView-Brawler", rig.Name);
        Assert.Equal(ProjectionType.Orthographic, rig.Projection);
        Assert.Equal(2, rig.Padding);
        Assert.Equal(Color.Transparent, rig.BackgroundColor);
    }

    // --- TopDown8Dir ---

    [Fact]
    public void TopDown8Dir_ReturnsFiveAngles()
    {
        var rig = CameraRigPresets.TopDown8Dir();

        Assert.Equal(5, rig.Angles.Count);

        var mirrorCount = rig.Angles.Count(a => a.ProducesMirror);
        Assert.Equal(3, mirrorCount);
    }

    [Fact]
    public void TopDown8Dir_DefaultPitch_IsNegative55()
    {
        var rig = CameraRigPresets.TopDown8Dir();

        foreach (var angle in rig.Angles)
        {
            Assert.True(MathF.Abs(angle.Pitch - (-55f)) < 0.001f,
                $"Expected Pitch=-55 for angle {angle.Name}, got {angle.Pitch}");
        }
    }

    [Fact]
    public void TopDown8Dir_AngleNames_AreCorrect()
    {
        var rig = CameraRigPresets.TopDown8Dir();

        var names = rig.Angles.Select(a => a.Name).ToList();
        Assert.Equal(new[] { "N", "NE", "E", "SE", "S" }, names);
    }

    [Fact]
    public void TopDown8Dir_MirrorTargets_AreCorrect()
    {
        var rig = CameraRigPresets.TopDown8Dir();

        var mirrors = rig.Angles
            .Where(a => a.ProducesMirror)
            .ToDictionary(a => a.Name, a => a.MirrorTargetName);

        Assert.Equal("NW", mirrors["NE"]);
        Assert.Equal("W", mirrors["E"]);
        Assert.Equal("SW", mirrors["SE"]);
    }

    [Fact]
    public void TopDown8Dir_RigProperties_AreCorrect()
    {
        var rig = CameraRigPresets.TopDown8Dir();

        Assert.Equal("TopDown-8Dir", rig.Name);
        Assert.Equal(ProjectionType.Orthographic, rig.Projection);
        Assert.Equal(2, rig.Padding);
        Assert.Equal(Color.Transparent, rig.BackgroundColor);
        Assert.Equal(96, rig.FrameSize.Width);
        Assert.Equal(96, rig.FrameSize.Height);
    }

    // --- TopDown4Dir ---

    [Fact]
    public void TopDown4Dir_ReturnsThreeAngles()
    {
        var rig = CameraRigPresets.TopDown4Dir();

        Assert.Equal(3, rig.Angles.Count);

        var mirrorCount = rig.Angles.Count(a => a.ProducesMirror);
        Assert.Equal(1, mirrorCount);
    }

    [Fact]
    public void TopDown4Dir_MirrorTarget_IsW()
    {
        var rig = CameraRigPresets.TopDown4Dir();

        var mirrorAngle = rig.Angles.Single(a => a.ProducesMirror);
        Assert.Equal("E", mirrorAngle.Name);
        Assert.Equal("W", mirrorAngle.MirrorTargetName);
    }

    [Fact]
    public void TopDown4Dir_RigProperties_AreCorrect()
    {
        var rig = CameraRigPresets.TopDown4Dir();

        Assert.Equal("TopDown-4Dir", rig.Name);
        Assert.Equal(ProjectionType.Orthographic, rig.Projection);
        Assert.Equal(2, rig.Padding);
        Assert.Equal(Color.Transparent, rig.BackgroundColor);
        Assert.Equal(96, rig.FrameSize.Width);
        Assert.Equal(96, rig.FrameSize.Height);
    }
}
