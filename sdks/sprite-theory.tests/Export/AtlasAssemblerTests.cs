using BeyondImmersion.Bannou.SpriteTheory;
using BeyondImmersion.Bannou.SpriteTheory.Atlas;
using BeyondImmersion.Bannou.SpriteTheory.Export;
using BeyondImmersion.Bannou.SpriteTheory.Metadata;
using Xunit;

namespace BeyondImmersion.Bannou.SpriteTheory.Tests.Export;

public class AtlasAssemblerTests
{
    private static FrameCapture CreateFrameCapture(int frameIndex, int width, int height, byte fillValue)
    {
        var pixelData = new byte[width * height * 4];
        for (var i = 0; i < pixelData.Length; i += 4)
        {
            pixelData[i + 0] = fillValue;     // R
            pixelData[i + 1] = fillValue;     // G
            pixelData[i + 2] = fillValue;     // B
            pixelData[i + 3] = 255;           // A
        }

        return new FrameCapture(
            PixelData: pixelData,
            DepthData: null,
            Width: width,
            Height: height,
            AngleName: "right",
            AnimationName: "idle",
            FrameIndex: frameIndex,
            NormalizedTime: 0.0f);
    }

    private static AtlasLayout CreateSingleAtlasLayout(
        int atlasWidth, int atlasHeight, IReadOnlyList<PackedFrame> placements)
    {
        return new AtlasLayout(
            Placements: placements,
            AtlasWidths: new[] { atlasWidth },
            AtlasHeights: new[] { atlasHeight },
            AtlasCount: 1,
            Efficiency: 1.0f);
    }

    [Fact]
    public void Assemble_SingleFrame_CorrectPixelPlacement()
    {
        // 2x2 frame placed at (1, 1) in a 4x4 atlas
        var frame = CreateFrameCapture(0, 2, 2, 200);
        var placements = new[] { new PackedFrame(0, 0, 1, 1, 2, 2) };
        var layout = CreateSingleAtlasLayout(4, 4, placements);

        var atlases = AtlasAssembler.Assemble(
            new[] { frame }, layout, Color.Transparent);

        Assert.Single(atlases);
        var atlas = atlases[0];

        // Verify pixel at (1, 1) — should be the frame's fill color (200)
        var idx = (1 * 4 + 1) * 4; // row=1, col=1, atlasWidth=4
        Assert.Equal(200, atlas[idx + 0]); // R
        Assert.Equal(200, atlas[idx + 1]); // G
        Assert.Equal(200, atlas[idx + 2]); // B
        Assert.Equal(255, atlas[idx + 3]); // A

        // Verify pixel at (0, 0) — should be background (transparent)
        Assert.Equal(0, atlas[0]); // R
        Assert.Equal(0, atlas[1]); // G
        Assert.Equal(0, atlas[2]); // B
        Assert.Equal(0, atlas[3]); // A
    }

    [Fact]
    public void Assemble_BackgroundColor_FillsEmptySpace()
    {
        // 1x1 frame placed at (0, 0) in a 2x2 atlas with red background
        var frame = CreateFrameCapture(0, 1, 1, 100);
        var placements = new[] { new PackedFrame(0, 0, 0, 0, 1, 1) };
        var layout = CreateSingleAtlasLayout(2, 2, placements);
        var bgColor = new Color(255, 0, 0, 255); // Red

        var atlases = AtlasAssembler.Assemble(
            new[] { frame }, layout, bgColor);

        var atlas = atlases[0];

        // Pixel at (1, 0) — empty, should be red background
        var idx = (0 * 2 + 1) * 4; // row=0, col=1, atlasWidth=2
        Assert.Equal(255, atlas[idx + 0]); // R
        Assert.Equal(0, atlas[idx + 1]);   // G
        Assert.Equal(0, atlas[idx + 2]);   // B
        Assert.Equal(255, atlas[idx + 3]); // A

        // Pixel at (0, 1) — empty, should be red background
        var idx2 = (1 * 2 + 0) * 4; // row=1, col=0
        Assert.Equal(255, atlas[idx2 + 0]); // R
        Assert.Equal(0, atlas[idx2 + 1]);   // G
    }

    [Fact]
    public void Assemble_MultipleFrames_AllBlitted()
    {
        // Two 2x2 frames side by side in a 4x2 atlas
        var frame0 = CreateFrameCapture(0, 2, 2, 100);
        var frame1 = CreateFrameCapture(1, 2, 2, 200);
        var placements = new[]
        {
            new PackedFrame(0, 0, 0, 0, 2, 2),
            new PackedFrame(1, 0, 2, 0, 2, 2)
        };
        var layout = CreateSingleAtlasLayout(4, 2, placements);

        var atlases = AtlasAssembler.Assemble(
            new[] { frame0, frame1 }, layout, Color.Transparent);

        var atlas = atlases[0];

        // Frame 0 at (0,0): pixel (0,0) should be fill=100
        Assert.Equal(100, atlas[0]); // R at (0,0)

        // Frame 1 at (2,0): pixel (2,0) should be fill=200
        var idx = (0 * 4 + 2) * 4; // row=0, col=2, atlasWidth=4
        Assert.Equal(200, atlas[idx]); // R at (2,0)
    }

    [Fact]
    public void Assemble_OutputSize_MatchesLayout()
    {
        var frame = CreateFrameCapture(0, 2, 2, 128);
        var placements = new[] { new PackedFrame(0, 0, 0, 0, 2, 2) };
        var layout = CreateSingleAtlasLayout(8, 16, placements);

        var atlases = AtlasAssembler.Assemble(
            new[] { frame }, layout, Color.Transparent);

        Assert.Single(atlases);
        // Atlas size = atlasWidth * atlasHeight * 4 bytes per pixel
        Assert.Equal(8 * 16 * 4, atlases[0].Length);
    }

    // --- Multi-animation identity (regression protection) ---
    //
    // FrameCapture.FrameIndex is an INTRA-ANIMATION index (0-based within its animation).
    // PackedFrame.FrameIndex is a GLOBAL ENUMERATION POSITION into the frames list passed to
    // AtlasPacker.Pack. These two semantics collide the moment any two captures share an
    // intra-animation index (i.e., any realistic multi-animation capture, where frame 0 of
    // "idle" and frame 0 of "run" both have FrameCapture.FrameIndex == 0).
    //
    // AtlasAssembler MUST key captures by their position in the frames list (matching the
    // packer's input contract and the implementation map's spec:
    //   `COMPUTE frame ← frames[placement.FrameIndex]`
    // in docs/sdks/maps/SPRITE-THEORY.md § AtlasAssembler.Assemble).
    //
    // The following tests fail if AtlasAssembler ever regresses to keying by
    // FrameCapture.FrameIndex.

    private static FrameCapture CreateFrameCaptureIdentified(
        int frameIndexInAnimation,
        string animationName,
        string angleName,
        int width,
        int height,
        byte fillValue)
    {
        var pixelData = new byte[width * height * 4];
        for (var i = 0; i < pixelData.Length; i += 4)
        {
            pixelData[i + 0] = fillValue;
            pixelData[i + 1] = fillValue;
            pixelData[i + 2] = fillValue;
            pixelData[i + 3] = 255;
        }

        return new FrameCapture(
            PixelData: pixelData,
            DepthData: null,
            Width: width,
            Height: height,
            AngleName: angleName,
            AnimationName: animationName,
            FrameIndex: frameIndexInAnimation,
            NormalizedTime: 0.0f);
    }

    [Fact]
    public void Assemble_MultipleAnimations_EachFramePlacedFromCorrectCapture()
    {
        // Four captures from two animations, each animation carrying intra-animation
        // FrameIndex values 0 and 1. If AtlasAssembler keyed by FrameCapture.FrameIndex,
        // "idle"/0 and "run"/0 would collide (only one survives the dictionary), and
        // "idle"/1 and "run"/1 would collide. Two of the four placements would use the
        // wrong pixels and two would be skipped entirely — leaving their slots at the
        // background color.
        var idle0 = CreateFrameCaptureIdentified(0, "idle", "right", 1, 1, 10);
        var idle1 = CreateFrameCaptureIdentified(1, "idle", "right", 1, 1, 20);
        var run0 = CreateFrameCaptureIdentified(0, "run", "right", 1, 1, 30);
        var run1 = CreateFrameCaptureIdentified(1, "run", "right", 1, 1, 40);

        // Placements reference list positions 0..3. The composer guarantees this
        // by calling AtlasPacker.Pack(frames.Select((f, i) => (f.Width, f.Height, i))).
        var placements = new[]
        {
            new PackedFrame(FrameIndex: 0, AtlasIndex: 0, X: 0, Y: 0, Width: 1, Height: 1),
            new PackedFrame(FrameIndex: 1, AtlasIndex: 0, X: 1, Y: 0, Width: 1, Height: 1),
            new PackedFrame(FrameIndex: 2, AtlasIndex: 0, X: 2, Y: 0, Width: 1, Height: 1),
            new PackedFrame(FrameIndex: 3, AtlasIndex: 0, X: 3, Y: 0, Width: 1, Height: 1)
        };
        var layout = CreateSingleAtlasLayout(4, 1, placements);

        var atlases = AtlasAssembler.Assemble(
            new[] { idle0, idle1, run0, run1 }, layout, Color.Transparent);

        var atlas = atlases[0];

        // Each slot must carry the correct animation's pixel fill:
        Assert.Equal(10, atlas[0]);   // (0,0) = idle0
        Assert.Equal(20, atlas[4]);   // (1,0) = idle1
        Assert.Equal(30, atlas[8]);   // (2,0) = run0
        Assert.Equal(40, atlas[12]);  // (3,0) = run1
    }

    [Fact]
    public void Assemble_MultipleAnimations_NoSlotFallsBackToBackgroundIncorrectly()
    {
        // Variation that makes the "slots left as background" symptom of the bug explicit.
        // If any placement is skipped because its FrameIndex wasn't found in a lookup
        // keyed by FrameCapture.FrameIndex, that slot stays at the background color.
        // Using a non-zero background color makes the failure mode obvious.
        var idle0 = CreateFrameCaptureIdentified(0, "idle", "right", 1, 1, 100);
        var idle1 = CreateFrameCaptureIdentified(1, "idle", "right", 1, 1, 101);
        var run0 = CreateFrameCaptureIdentified(0, "run", "right", 1, 1, 200);
        var run1 = CreateFrameCaptureIdentified(1, "run", "right", 1, 1, 201);

        var placements = new[]
        {
            new PackedFrame(FrameIndex: 0, AtlasIndex: 0, X: 0, Y: 0, Width: 1, Height: 1),
            new PackedFrame(FrameIndex: 1, AtlasIndex: 0, X: 1, Y: 0, Width: 1, Height: 1),
            new PackedFrame(FrameIndex: 2, AtlasIndex: 0, X: 2, Y: 0, Width: 1, Height: 1),
            new PackedFrame(FrameIndex: 3, AtlasIndex: 0, X: 3, Y: 0, Width: 1, Height: 1)
        };
        var layout = CreateSingleAtlasLayout(4, 1, placements);
        var red = new Color(255, 0, 0, 255);

        var atlases = AtlasAssembler.Assemble(
            new[] { idle0, idle1, run0, run1 }, layout, red);

        var atlas = atlases[0];

        // No slot may carry the background red channel (255) — every slot must be
        // overwritten by a frame.
        Assert.NotEqual(255, atlas[0]);
        Assert.NotEqual(255, atlas[4]);
        Assert.NotEqual(255, atlas[8]);
        Assert.NotEqual(255, atlas[12]);

        // And the fills must be distinct per slot — proving no two slots collapsed.
        Assert.Equal(100, atlas[0]);
        Assert.Equal(101, atlas[4]);
        Assert.Equal(200, atlas[8]);
        Assert.Equal(201, atlas[12]);
    }

    [Fact]
    public void Assemble_OutOfOrderPlacements_StillAddressFramesByListPosition()
    {
        // AtlasPacker sorts inputs by height desc / width desc before placing, so
        // placements often arrive to AtlasAssembler in a different order than the
        // frames list. The FrameIndex on each placement still references the ORIGINAL
        // list position, not the sorted order. AtlasAssembler must honour that.
        var frameA = CreateFrameCapture(frameIndex: 0, width: 1, height: 1, fillValue: 11);
        var frameB = CreateFrameCapture(frameIndex: 1, width: 1, height: 1, fillValue: 22);
        var frameC = CreateFrameCapture(frameIndex: 2, width: 1, height: 1, fillValue: 33);

        // Placements in reverse order — FrameIndex values 2, 1, 0 placed left-to-right.
        var placements = new[]
        {
            new PackedFrame(FrameIndex: 2, AtlasIndex: 0, X: 0, Y: 0, Width: 1, Height: 1),
            new PackedFrame(FrameIndex: 1, AtlasIndex: 0, X: 1, Y: 0, Width: 1, Height: 1),
            new PackedFrame(FrameIndex: 0, AtlasIndex: 0, X: 2, Y: 0, Width: 1, Height: 1)
        };
        var layout = CreateSingleAtlasLayout(3, 1, placements);

        var atlases = AtlasAssembler.Assemble(
            new[] { frameA, frameB, frameC }, layout, Color.Transparent);

        var atlas = atlases[0];

        // Slot 0 references frameC (index 2, fill 33).
        Assert.Equal(33, atlas[0]);
        // Slot 1 references frameB (index 1, fill 22).
        Assert.Equal(22, atlas[4]);
        // Slot 2 references frameA (index 0, fill 11).
        Assert.Equal(11, atlas[8]);
    }

    [Fact]
    public void Assemble_RealisticDefendersShape_AllFramesBlitted()
    {
        // Model the Defenders dual-rig realistic case at small scale: 3 animations
        // ("idle", "run", "attack") x 3 angles ("N", "NE", "E") x 2 frames each = 18 captures.
        // Each FrameCapture.FrameIndex is 0 or 1 (intra-animation), so the bug's
        // collision rate is dramatic: most captures share an intra-animation index.
        // This test proves the assembler handles the realistic multi-animation x
        // multi-angle shape.
        var frames = new List<FrameCapture>();
        var fillValues = new List<byte>();
        byte fill = 1;
        string[] animations = { "idle", "run", "attack" };
        string[] angles = { "N", "NE", "E" };

        foreach (var anim in animations)
        {
            foreach (var angle in angles)
            {
                for (var frameIdx = 0; frameIdx < 2; frameIdx++)
                {
                    frames.Add(CreateFrameCaptureIdentified(
                        frameIndexInAnimation: frameIdx,
                        animationName: anim,
                        angleName: angle,
                        width: 1,
                        height: 1,
                        fillValue: fill));
                    fillValues.Add(fill);
                    fill++;
                }
            }
        }

        // 18 frames side-by-side in an 18x1 atlas.
        var placements = new PackedFrame[frames.Count];
        for (var i = 0; i < frames.Count; i++)
        {
            placements[i] = new PackedFrame(
                FrameIndex: i,
                AtlasIndex: 0,
                X: i,
                Y: 0,
                Width: 1,
                Height: 1);
        }
        var layout = CreateSingleAtlasLayout(frames.Count, 1, placements);

        var atlases = AtlasAssembler.Assemble(frames, layout, Color.Transparent);
        var atlas = atlases[0];

        // Every slot i must carry the fill value at frames[i].
        for (var i = 0; i < frames.Count; i++)
        {
            var expected = fillValues[i];
            var actualR = atlas[i * 4 + 0];
            Assert.True(
                expected == actualR,
                $"Slot {i} expected fill {expected} (animation={frames[i].AnimationName}, " +
                $"angle={frames[i].AngleName}, frameIndex={frames[i].FrameIndex}), " +
                $"got {actualR}");
        }
    }
}
