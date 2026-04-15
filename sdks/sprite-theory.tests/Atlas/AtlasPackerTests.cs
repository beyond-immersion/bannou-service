using BeyondImmersion.Bannou.SpriteTheory;
using BeyondImmersion.Bannou.SpriteTheory.Atlas;
using Xunit;

namespace BeyondImmersion.Bannou.SpriteTheory.Tests.Atlas;

public class AtlasPackerTests
{
    private static List<(int Width, int Height, int Index)> CreateUniformFrames(int count, int width, int height)
    {
        var frames = new List<(int Width, int Height, int Index)>();
        for (var i = 0; i < count; i++)
            frames.Add((width, height, i));
        return frames;
    }

    [Fact]
    public void Pack_SingleFrame_SingleAtlas()
    {
        var frames = new List<(int Width, int Height, int Index)> { (64, 64, 0) };
        var options = new AtlasOptions(MaxWidth: 4096, MaxHeight: 4096);

        var layout = AtlasPacker.Pack(frames, options);

        Assert.Equal(1, layout.AtlasCount);
        Assert.Single(layout.Placements);
        Assert.Equal(0, layout.Placements[0].FrameIndex);
        Assert.Equal(0, layout.Placements[0].AtlasIndex);
        Assert.Equal(64, layout.Placements[0].Width);
        Assert.Equal(64, layout.Placements[0].Height);
    }

    [Fact]
    public void Pack_MultipleFrames_DeterministicOrder()
    {
        var frames = new List<(int Width, int Height, int Index)>
        {
            (32, 64, 0),
            (64, 32, 1),
            (48, 48, 2),
            (64, 64, 3),
            (16, 16, 4)
        };
        var options = new AtlasOptions(MaxWidth: 4096, MaxHeight: 4096);

        var layout1 = AtlasPacker.Pack(frames, options);
        var layout2 = AtlasPacker.Pack(frames, options);

        Assert.Equal(layout1.Placements.Count, layout2.Placements.Count);
        for (var i = 0; i < layout1.Placements.Count; i++)
        {
            Assert.Equal(layout1.Placements[i].FrameIndex, layout2.Placements[i].FrameIndex);
            Assert.Equal(layout1.Placements[i].AtlasIndex, layout2.Placements[i].AtlasIndex);
            Assert.Equal(layout1.Placements[i].X, layout2.Placements[i].X);
            Assert.Equal(layout1.Placements[i].Y, layout2.Placements[i].Y);
        }
    }

    [Fact]
    public void Pack_UniformFrames_EfficientPacking()
    {
        // 64 frames of 64x64 fit perfectly in 512x512 (8x8 grid)
        var frames = CreateUniformFrames(64, 64, 64);
        var options = new AtlasOptions(MaxWidth: 512, MaxHeight: 512, PowerOfTwo: false, Padding: 0);

        var layout = AtlasPacker.Pack(frames, options);

        // 64 frames × 64×64 = 262144 px²; atlas should be near 512×512 = 262144
        Assert.True(layout.Efficiency > 0.5f,
            $"Efficiency {layout.Efficiency} should be > 0.5 for 64 uniform 64x64 frames in 512x512 atlas");
    }

    [Fact]
    public void Pack_FramesExceedingAtlas_MultiAtlasOverflow()
    {
        // Each frame is 100x100 with 0 padding. A 256x256 atlas fits at most a few frames.
        var frames = CreateUniformFrames(20, 100, 100);
        var options = new AtlasOptions(MaxWidth: 256, MaxHeight: 256, Padding: 0, PowerOfTwo: false);

        var layout = AtlasPacker.Pack(frames, options);

        Assert.True(layout.AtlasCount > 1,
            $"Expected multi-atlas overflow but got AtlasCount={layout.AtlasCount}");
        Assert.Equal(20, layout.Placements.Count);
    }

    [Fact]
    public void Pack_PowerOfTwo_RoundsDimensions()
    {
        // A single 100x100 frame should round atlas to 128x128 with PowerOfTwo
        var frames = new List<(int Width, int Height, int Index)> { (100, 100, 0) };
        var options = new AtlasOptions(MaxWidth: 4096, MaxHeight: 4096, PowerOfTwo: true, Padding: 0);

        var layout = AtlasPacker.Pack(frames, options);

        Assert.Equal(128, layout.AtlasWidths[0]);
        Assert.Equal(128, layout.AtlasHeights[0]);
    }

    [Fact]
    public void Pack_PowerOfTwoDisabled_ExactDimensions()
    {
        // A single 100x100 frame should produce exact 100x100 atlas
        var frames = new List<(int Width, int Height, int Index)> { (100, 100, 0) };
        var options = new AtlasOptions(MaxWidth: 4096, MaxHeight: 4096, PowerOfTwo: false, Padding: 0);

        var layout = AtlasPacker.Pack(frames, options);

        Assert.Equal(100, layout.AtlasWidths[0]);
        Assert.Equal(100, layout.AtlasHeights[0]);
    }

    [Fact]
    public void Pack_PaddingApplied_FramesDontOverlap()
    {
        var frames = CreateUniformFrames(25, 32, 32);
        var options = new AtlasOptions(MaxWidth: 4096, MaxHeight: 4096, Padding: 4, PowerOfTwo: false);

        var layout = AtlasPacker.Pack(frames, options);

        // Verify no two placements on the same atlas overlap (including padding)
        var placementsByAtlas = layout.Placements
            .GroupBy(p => p.AtlasIndex);

        foreach (var group in placementsByAtlas)
        {
            var placements = group.ToList();
            for (var i = 0; i < placements.Count; i++)
            {
                for (var j = i + 1; j < placements.Count; j++)
                {
                    var a = placements[i];
                    var b = placements[j];

                    // Check content rects (Width x Height) don't overlap
                    var aRight = a.X + a.Width;
                    var aBottom = a.Y + a.Height;
                    var bRight = b.X + b.Width;
                    var bBottom = b.Y + b.Height;

                    var overlaps = a.X < bRight && aRight > b.X && a.Y < bBottom && aBottom > b.Y;

                    Assert.False(overlaps,
                        $"Frames {a.FrameIndex} ({a.X},{a.Y},{a.Width},{a.Height}) and " +
                        $"{b.FrameIndex} ({b.X},{b.Y},{b.Width},{b.Height}) overlap in atlas {group.Key}");
                }
            }
        }
    }

    [Fact]
    public void Pack_EmptyInput_EmptyResult()
    {
        var frames = new List<(int Width, int Height, int Index)>();
        var options = new AtlasOptions();

        var layout = AtlasPacker.Pack(frames, options);

        Assert.Empty(layout.Placements);
        Assert.Equal(1, layout.AtlasCount);
    }

    [Fact]
    public void Pack_SingleLargeFrame_FillsAtlas()
    {
        // Frame that nearly fills the max atlas size
        var frames = new List<(int Width, int Height, int Index)> { (4000, 4000, 0) };
        var options = new AtlasOptions(MaxWidth: 4096, MaxHeight: 4096, Padding: 0, PowerOfTwo: true);

        var layout = AtlasPacker.Pack(frames, options);

        Assert.Equal(1, layout.AtlasCount);
        Assert.Single(layout.Placements);
        Assert.Equal(4096, layout.AtlasWidths[0]);
        Assert.Equal(4096, layout.AtlasHeights[0]);
    }

    [Fact]
    public void Pack_FrameExceedsMaxSize_ThrowsInvalidOperationException()
    {
        // Frame larger than the atlas can hold
        var frames = new List<(int Width, int Height, int Index)> { (5000, 5000, 0) };
        var options = new AtlasOptions(MaxWidth: 4096, MaxHeight: 4096, Padding: 0);

        Assert.Throws<InvalidOperationException>(() => AtlasPacker.Pack(frames, options));
    }

    [Fact]
    public void Pack_AllFrameIndicesPreserved()
    {
        var frames = new List<(int Width, int Height, int Index)>
        {
            (64, 128, 5),
            (32, 32, 10),
            (128, 64, 0),
            (48, 48, 7)
        };
        var options = new AtlasOptions(MaxWidth: 4096, MaxHeight: 4096);

        var layout = AtlasPacker.Pack(frames, options);

        var outputIndices = layout.Placements.Select(p => p.FrameIndex).OrderBy(i => i).ToList();
        var inputIndices = frames.Select(f => f.Index).OrderBy(i => i).ToList();

        Assert.Equal(inputIndices, outputIndices);
    }

    [Fact]
    public void Pack_Efficiency_IsPositive()
    {
        var frames = CreateUniformFrames(10, 64, 64);
        var options = new AtlasOptions(MaxWidth: 4096, MaxHeight: 4096);

        var layout = AtlasPacker.Pack(frames, options);

        Assert.True(layout.Efficiency > 0f,
            $"Efficiency should be > 0 for non-empty input, got {layout.Efficiency}");
    }
}
