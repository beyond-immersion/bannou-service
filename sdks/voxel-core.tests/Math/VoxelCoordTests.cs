using BeyondImmersion.Bannou.VoxelCore.Math;
using Xunit;

namespace BeyondImmersion.Bannou.VoxelCore.Tests.Math;

/// <summary>
/// Unit tests for <see cref="VoxelCoord"/> — coordinate math, floor-division, and floor-modulo.
/// Negative coordinate correctness is critical: C#'s % rounds toward zero, but we need floor.
/// </summary>
public class VoxelCoordTests
{
    #region ToChunkCoord (Floor-Division)

    [Fact]
    public void ToChunkCoord_PositiveCoords()
    {
        var coord = new VoxelCoord(17, 32, 5);
        var chunk = coord.ToChunkCoord();
        Assert.Equal(new ChunkCoord(1, 2, 0), chunk);
    }

    [Fact]
    public void ToChunkCoord_Origin()
    {
        var chunk = VoxelCoord.Zero.ToChunkCoord();
        Assert.Equal(ChunkCoord.Zero, chunk);
    }

    [Fact]
    public void ToChunkCoord_NegativeCoords_FloorsCorrectly()
    {
        // -1 / 16 should floor to -1, not round toward zero (0)
        var coord = new VoxelCoord(-1, -1, -1);
        var chunk = coord.ToChunkCoord();
        Assert.Equal(new ChunkCoord(-1, -1, -1), chunk);
    }

    [Fact]
    public void ToChunkCoord_NegativeLarger_FloorsCorrectly()
    {
        // -17 / 16 should floor to -2
        var coord = new VoxelCoord(-17, 0, 0);
        var chunk = coord.ToChunkCoord();
        Assert.Equal(new ChunkCoord(-2, 0, 0), chunk);
    }

    [Fact]
    public void ToChunkCoord_ExactBoundary()
    {
        // 16 / 16 = 1 (exact)
        var coord = new VoxelCoord(16, 0, 0);
        Assert.Equal(new ChunkCoord(1, 0, 0), coord.ToChunkCoord());
    }

    #endregion

    #region ToLocalCoord (Floor-Modulo)

    [Fact]
    public void ToLocalCoord_PositiveCoords()
    {
        var (lx, ly, lz) = new VoxelCoord(17, 3, 20).ToLocalCoord();
        Assert.Equal(1, lx);   // 17 % 16 = 1
        Assert.Equal(3, ly);   // 3 % 16 = 3
        Assert.Equal(4, lz);   // 20 % 16 = 4
    }

    [Fact]
    public void ToLocalCoord_NegativeCoords_ProducesValidRange()
    {
        // -1 % 16 = -1 in C#, but we need 15
        var (lx, ly, lz) = new VoxelCoord(-1, -1, -1).ToLocalCoord();
        Assert.Equal(15, lx);
        Assert.Equal(15, ly);
        Assert.Equal(15, lz);
    }

    [Fact]
    public void ToLocalCoord_NegativeMultiple_ProducesZero()
    {
        // -16 % 16 = 0
        var (lx, ly, lz) = new VoxelCoord(-16, -32, -48).ToLocalCoord();
        Assert.Equal(0, lx);
        Assert.Equal(0, ly);
        Assert.Equal(0, lz);
    }

    [Fact]
    public void ToLocalCoord_AlwaysInRange0To15()
    {
        // Test a range of negative and positive coordinates
        for (var i = -50; i <= 50; i++)
        {
            var (lx, _, _) = new VoxelCoord(i, 0, 0).ToLocalCoord();
            Assert.InRange(lx, 0, 15);
        }
    }

    #endregion

    #region Operators

    [Fact]
    public void Addition()
    {
        var a = new VoxelCoord(1, 2, 3);
        var b = new VoxelCoord(10, 20, 30);
        Assert.Equal(new VoxelCoord(11, 22, 33), a + b);
    }

    [Fact]
    public void Subtraction()
    {
        var a = new VoxelCoord(10, 20, 30);
        var b = new VoxelCoord(1, 2, 3);
        Assert.Equal(new VoxelCoord(9, 18, 27), a - b);
    }

    #endregion

    #region Distance

    [Fact]
    public void Distance_SamePoint_IsZero()
    {
        var coord = new VoxelCoord(5, 5, 5);
        Assert.Equal(0f, coord.Distance(coord));
    }

    [Fact]
    public void Distance_UnitAxisAligned()
    {
        var a = VoxelCoord.Zero;
        var b = new VoxelCoord(1, 0, 0);
        Assert.Equal(1f, a.Distance(b), 0.001f);
    }

    [Fact]
    public void Distance_Diagonal()
    {
        var a = VoxelCoord.Zero;
        var b = new VoxelCoord(1, 1, 1);
        Assert.Equal(MathF.Sqrt(3), a.Distance(b), 0.001f);
    }

    [Fact]
    public void ManhattanDistance_SamePoint_IsZero()
    {
        var coord = new VoxelCoord(5, 5, 5);
        Assert.Equal(0, coord.ManhattanDistance(coord));
    }

    [Fact]
    public void ManhattanDistance_Diagonal()
    {
        var a = VoxelCoord.Zero;
        var b = new VoxelCoord(3, 4, 5);
        Assert.Equal(12, a.ManhattanDistance(b));
    }

    #endregion

    #region Comparison

    [Fact]
    public void CompareTo_YFirst()
    {
        var lower = new VoxelCoord(10, 0, 10);
        var higher = new VoxelCoord(0, 1, 0);
        Assert.True(lower.CompareTo(higher) < 0);
    }

    [Fact]
    public void CompareTo_ThenZ()
    {
        var lower = new VoxelCoord(10, 0, 0);
        var higher = new VoxelCoord(0, 0, 1);
        Assert.True(lower.CompareTo(higher) < 0);
    }

    [Fact]
    public void CompareTo_ThenX()
    {
        var lower = new VoxelCoord(0, 0, 0);
        var higher = new VoxelCoord(1, 0, 0);
        Assert.True(lower.CompareTo(higher) < 0);
    }

    #endregion
}
