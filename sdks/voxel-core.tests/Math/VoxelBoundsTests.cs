using BeyondImmersion.Bannou.VoxelCore.Math;
using Xunit;

namespace BeyondImmersion.Bannou.VoxelCore.Tests.Math;

/// <summary>
/// Unit tests for <see cref="VoxelBounds"/>.
/// </summary>
public class VoxelBoundsTests
{
    private static readonly VoxelBounds TestBounds =
        new(new VoxelCoord(0, 0, 0), new VoxelCoord(15, 15, 15));

    [Fact]
    public void Dimensions_AreInclusive()
    {
        Assert.Equal(16, TestBounds.Width);
        Assert.Equal(16, TestBounds.Height);
        Assert.Equal(16, TestBounds.Depth);
    }

    [Fact]
    public void Volume_IsProduct()
    {
        Assert.Equal(4096L, TestBounds.Volume);
    }

    [Fact]
    public void Contains_InsideBounds_True()
    {
        Assert.True(TestBounds.Contains(new VoxelCoord(7, 7, 7)));
    }

    [Fact]
    public void Contains_OnMinCorner_True()
    {
        Assert.True(TestBounds.Contains(new VoxelCoord(0, 0, 0)));
    }

    [Fact]
    public void Contains_OnMaxCorner_True()
    {
        Assert.True(TestBounds.Contains(new VoxelCoord(15, 15, 15)));
    }

    [Fact]
    public void Contains_OutsideBounds_False()
    {
        Assert.False(TestBounds.Contains(new VoxelCoord(16, 0, 0)));
        Assert.False(TestBounds.Contains(new VoxelCoord(-1, 0, 0)));
    }

    [Fact]
    public void Intersects_OverlappingBounds_True()
    {
        var other = new VoxelBounds(new VoxelCoord(10, 10, 10), new VoxelCoord(20, 20, 20));
        Assert.True(TestBounds.Intersects(other));
    }

    [Fact]
    public void Intersects_NonOverlapping_False()
    {
        var other = new VoxelBounds(new VoxelCoord(100, 100, 100), new VoxelCoord(200, 200, 200));
        Assert.False(TestBounds.Intersects(other));
    }

    [Fact]
    public void Intersects_AdjacentTouching_True()
    {
        var other = new VoxelBounds(new VoxelCoord(15, 0, 0), new VoxelCoord(20, 15, 15));
        Assert.True(TestBounds.Intersects(other));
    }

    [Fact]
    public void Expand_IncludesPoint()
    {
        var expanded = TestBounds.Expand(new VoxelCoord(100, -5, 50));
        Assert.True(expanded.Contains(new VoxelCoord(100, -5, 50)));
        Assert.True(expanded.Contains(new VoxelCoord(0, 0, 0)));
        Assert.True(expanded.Contains(new VoxelCoord(15, 15, 15)));
    }

    [Fact]
    public void Expand_PointInsideBounds_NoChange()
    {
        var expanded = TestBounds.Expand(new VoxelCoord(7, 7, 7));
        Assert.Equal(TestBounds, expanded);
    }

    [Fact]
    public void NegativeBounds_WorkCorrectly()
    {
        var bounds = new VoxelBounds(new VoxelCoord(-10, -10, -10), new VoxelCoord(-1, -1, -1));
        Assert.Equal(10, bounds.Width);
        Assert.True(bounds.Contains(new VoxelCoord(-5, -5, -5)));
        Assert.False(bounds.Contains(new VoxelCoord(0, 0, 0)));
    }
}
