using BeyondImmersion.Bannou.SpriteTheory;
using BeyondImmersion.Bannou.SpriteTheory.Camera;
using BeyondImmersion.Bannou.SpriteTheory.Metadata;
using BeyondImmersion.Bannou.SpriteTheory.Mirror;
using Xunit;

namespace BeyondImmersion.Bannou.SpriteTheory.Tests.Mirror;

public class MirrorOptimizerTests
{
    // --- ComputeMirrors ---

    [Fact]
    public void ComputeMirrors_TopDown8Dir_ReturnsThreeMirrors()
    {
        var rig = CameraRigPresets.TopDown8Dir();

        var mirrors = MirrorOptimizer.ComputeMirrors(rig);

        Assert.Equal(3, mirrors.Count);

        // NE → NW
        Assert.Equal("NE", mirrors[0].SourceAngleName);
        Assert.Equal("NW", mirrors[0].TargetAngleName);
        Assert.Equal(MirrorAxis.Horizontal, mirrors[0].FlipAxis);

        // E → W
        Assert.Equal("E", mirrors[1].SourceAngleName);
        Assert.Equal("W", mirrors[1].TargetAngleName);
        Assert.Equal(MirrorAxis.Horizontal, mirrors[1].FlipAxis);

        // SE → SW
        Assert.Equal("SE", mirrors[2].SourceAngleName);
        Assert.Equal("SW", mirrors[2].TargetAngleName);
        Assert.Equal(MirrorAxis.Horizontal, mirrors[2].FlipAxis);
    }

    [Fact]
    public void ComputeMirrors_SideViewBrawler_ReturnsOneMirror()
    {
        var rig = CameraRigPresets.SideViewBrawler();

        var mirrors = MirrorOptimizer.ComputeMirrors(rig);

        Assert.Single(mirrors);
        Assert.Equal("right", mirrors[0].SourceAngleName);
        Assert.Equal("left", mirrors[0].TargetAngleName);
        Assert.Equal(MirrorAxis.Horizontal, mirrors[0].FlipAxis);
    }

    [Fact]
    public void ComputeMirrors_NoMirrorAngles_ReturnsEmpty()
    {
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

        var mirrors = MirrorOptimizer.ComputeMirrors(rig);

        Assert.Empty(mirrors);
    }

    // --- GenerateMirrorFrames ---

    private static List<SpriteFrame> CreateCapturedFrames(int count, string angleName)
    {
        var frames = new List<SpriteFrame>();
        for (var i = 0; i < count; i++)
        {
            frames.Add(new SpriteFrame(
                Index: i,
                AtlasIndex: 0,
                AngleName: angleName,
                AnimationName: "idle",
                FrameInAnimation: i,
                Rect: new Rectangle(i * 128, 0, 128, 128),
                TrimmedRect: null,
                Pivot: new Vector2(0.5f, 0.85f),
                Duration: 0.125f,
                IsMirror: false,
                MirrorSourceIndex: null));
        }

        return frames;
    }

    [Fact]
    public void GenerateMirrorFrames_SingleMirror_CorrectFrameCount()
    {
        var capturedFrames = CreateCapturedFrames(8, "right");
        var mirrors = new List<MirrorInfo>
        {
            new MirrorInfo("right", "left", MirrorAxis.Horizontal)
        };

        var mirrorFrames = MirrorOptimizer.GenerateMirrorFrames(capturedFrames, mirrors);

        // 8 source frames * 1 mirror = 8 mirror frames
        Assert.Equal(8, mirrorFrames.Count);
    }

    [Fact]
    public void GenerateMirrorFrames_MirrorIndicesStartAfterCaptured()
    {
        var capturedFrames = CreateCapturedFrames(4, "right");
        var mirrors = new List<MirrorInfo>
        {
            new MirrorInfo("right", "left", MirrorAxis.Horizontal)
        };

        var mirrorFrames = MirrorOptimizer.GenerateMirrorFrames(capturedFrames, mirrors);

        // First mirror index should be capturedFrames.Count = 4
        Assert.Equal(4, mirrorFrames[0].Index);
        Assert.Equal(5, mirrorFrames[1].Index);
        Assert.Equal(6, mirrorFrames[2].Index);
        Assert.Equal(7, mirrorFrames[3].Index);
    }

    [Fact]
    public void GenerateMirrorFrames_PivotFlippedHorizontally()
    {
        var capturedFrames = CreateCapturedFrames(1, "right");
        // Source pivot is (0.5, 0.85)
        var mirrors = new List<MirrorInfo>
        {
            new MirrorInfo("right", "left", MirrorAxis.Horizontal)
        };

        var mirrorFrames = MirrorOptimizer.GenerateMirrorFrames(capturedFrames, mirrors);

        // Horizontal flip: X becomes 1 - X, Y stays
        // Source: (0.5, 0.85) → Mirror: (0.5, 0.85) — because 1.0 - 0.5 = 0.5
        var mirrorPivot = mirrorFrames[0].Pivot;
        Assert.True(MathF.Abs(mirrorPivot.X - 0.5f) < 0.001f,
            $"Expected mirror pivot X=0.5, got {mirrorPivot.X}");
        Assert.True(MathF.Abs(mirrorPivot.Y - 0.85f) < 0.001f,
            $"Expected mirror pivot Y=0.85, got {mirrorPivot.Y}");
    }

    [Fact]
    public void GenerateMirrorFrames_IsMirrorTrue_SourceIndexSet()
    {
        var capturedFrames = CreateCapturedFrames(3, "right");
        var mirrors = new List<MirrorInfo>
        {
            new MirrorInfo("right", "left", MirrorAxis.Horizontal)
        };

        var mirrorFrames = MirrorOptimizer.GenerateMirrorFrames(capturedFrames, mirrors);

        foreach (var frame in mirrorFrames)
        {
            Assert.True(frame.IsMirror);
            Assert.NotNull(frame.MirrorSourceIndex);
        }

        // Each mirror frame should point to the corresponding source
        Assert.Equal(0, mirrorFrames[0].MirrorSourceIndex);
        Assert.Equal(1, mirrorFrames[1].MirrorSourceIndex);
        Assert.Equal(2, mirrorFrames[2].MirrorSourceIndex);
    }

    [Fact]
    public void GenerateMirrorFrames_SameRect_AsSource()
    {
        var capturedFrames = CreateCapturedFrames(2, "right");
        var mirrors = new List<MirrorInfo>
        {
            new MirrorInfo("right", "left", MirrorAxis.Horizontal)
        };

        var mirrorFrames = MirrorOptimizer.GenerateMirrorFrames(capturedFrames, mirrors);

        // Mirror frames share the source frame's Rect (no duplicate atlas pixels)
        Assert.Equal(capturedFrames[0].Rect, mirrorFrames[0].Rect);
        Assert.Equal(capturedFrames[1].Rect, mirrorFrames[1].Rect);
    }

    [Fact]
    public void GenerateMirrorFrames_EmptyInput_ReturnsEmpty()
    {
        var capturedFrames = new List<SpriteFrame>();
        var mirrors = new List<MirrorInfo>
        {
            new MirrorInfo("right", "left", MirrorAxis.Horizontal)
        };

        var mirrorFrames = MirrorOptimizer.GenerateMirrorFrames(capturedFrames, mirrors);

        Assert.Empty(mirrorFrames);
    }
}
