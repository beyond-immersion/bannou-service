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
}
