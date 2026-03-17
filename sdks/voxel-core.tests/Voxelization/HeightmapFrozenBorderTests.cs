using BeyondImmersion.Bannou.VoxelCore.Grid;
using BeyondImmersion.Bannou.VoxelCore.Math;
using BeyondImmersion.Bannou.VoxelCore.Voxelization;
using Xunit;

namespace BeyondImmersion.Bannou.VoxelCore.Tests.Voxelization;

/// <summary>
/// Tests for the corrected HeightmapVoxelizer frozen border behavior: border COLUMNS are
/// entirely frozen (all voxels in the column), but interior column top surfaces are NOT frozen.
/// The fix changed from "border columns + all top surfaces" to "border columns only".
/// </summary>
public class HeightmapFrozenBorderTests
{
    private static (VoxelGrid grid, int gridWidth, int gridDepth) CreateTestGrid(int width, int depth, float height, int borderWidth)
    {
        var heights = new float[width, depth];
        var materials = new byte[width, depth];
        for (var x = 0; x < width; x++)
        for (var z = 0; z < depth; z++)
        {
            heights[x, z] = height;
            materials[x, z] = 1;
        }

        var palette = new Palette();
        palette.Set(1, new PaletteEntry(Color.White, MaterialType.Diffuse, 0.5f));
        var options = new VoxelizationOptions(VoxelScale: 1.0f, FrozenBorderWidth: borderWidth);

        return (HeightmapVoxelizer.Voxelize(heights, materials, palette, options), width, depth);
    }

    [Fact]
    public void InteriorColumnTopSurface_NotFrozen()
    {
        // 8x8 grid, height 3, border width 1
        var (grid, _, _) = CreateTestGrid(8, 8, 3.0f, 1);

        // Interior column (4, z=4) top surface at y=3
        var topVoxel = grid.GetVoxel(new VoxelCoord(4, 3, 4));
        Assert.False(topVoxel.IsEmpty);
        Assert.False(topVoxel.Flags.HasFlag(VoxelFlags.Frozen),
            "Interior column top surface should NOT be frozen");
    }

    [Fact]
    public void InteriorColumnAllVoxels_NotFrozen()
    {
        var (grid, _, _) = CreateTestGrid(8, 8, 3.0f, 1);

        // All voxels in an interior column should be unfrozen
        for (var y = 0; y <= 3; y++)
        {
            var voxel = grid.GetVoxel(new VoxelCoord(4, y, 4));
            Assert.False(voxel.IsEmpty);
            Assert.False(voxel.Flags.HasFlag(VoxelFlags.Frozen),
                $"Interior voxel at y={y} should not be frozen");
        }
    }

    [Fact]
    public void BorderColumnAllVoxels_Frozen()
    {
        var (grid, _, _) = CreateTestGrid(8, 8, 3.0f, 1);

        // All voxels in a border column (x=0) should be frozen
        for (var y = 0; y <= 3; y++)
        {
            var voxel = grid.GetVoxel(new VoxelCoord(0, y, 4));
            Assert.False(voxel.IsEmpty);
            Assert.True(voxel.Flags.HasFlag(VoxelFlags.Frozen),
                $"Border voxel at x=0, y={y} should be frozen");
        }
    }

    [Fact]
    public void BorderColumnTopSurface_Frozen()
    {
        var (grid, _, _) = CreateTestGrid(8, 8, 3.0f, 1);

        // Border column top surface is frozen (because the whole column is frozen)
        var topVoxel = grid.GetVoxel(new VoxelCoord(0, 3, 0));
        Assert.True(topVoxel.Flags.HasFlag(VoxelFlags.Frozen));
    }

    [Fact]
    public void AllEdgeColumns_Frozen()
    {
        var (grid, width, depth) = CreateTestGrid(8, 8, 2.0f, 1);

        // Check all 4 edges at ground level
        for (var x = 0; x < width; x++)
        {
            Assert.True(grid.GetVoxel(new VoxelCoord(x, 0, 0)).Flags.HasFlag(VoxelFlags.Frozen),
                $"Edge voxel at ({x}, 0, 0) should be frozen");
            Assert.True(grid.GetVoxel(new VoxelCoord(x, 0, depth - 1)).Flags.HasFlag(VoxelFlags.Frozen),
                $"Edge voxel at ({x}, 0, {depth - 1}) should be frozen");
        }
        for (var z = 0; z < depth; z++)
        {
            Assert.True(grid.GetVoxel(new VoxelCoord(0, 0, z)).Flags.HasFlag(VoxelFlags.Frozen),
                $"Edge voxel at (0, 0, {z}) should be frozen");
            Assert.True(grid.GetVoxel(new VoxelCoord(width - 1, 0, z)).Flags.HasFlag(VoxelFlags.Frozen),
                $"Edge voxel at ({width - 1}, 0, {z}) should be frozen");
        }
    }

    [Fact]
    public void BorderWidth2_FreezesTwo_ColumnsDeep()
    {
        var (grid, _, _) = CreateTestGrid(10, 10, 2.0f, 2);

        // x=0 and x=1 should both be frozen (border width 2)
        Assert.True(grid.GetVoxel(new VoxelCoord(0, 0, 5)).Flags.HasFlag(VoxelFlags.Frozen));
        Assert.True(grid.GetVoxel(new VoxelCoord(1, 0, 5)).Flags.HasFlag(VoxelFlags.Frozen));

        // x=2 should NOT be frozen (interior)
        Assert.False(grid.GetVoxel(new VoxelCoord(2, 0, 5)).Flags.HasFlag(VoxelFlags.Frozen));
    }

    [Fact]
    public void BorderWidth0_NothingFrozen()
    {
        var heights = new float[8, 8];
        var materials = new byte[8, 8];
        for (var x = 0; x < 8; x++)
        for (var z = 0; z < 8; z++)
        {
            heights[x, z] = 2.0f;
            materials[x, z] = 1;
        }

        var palette = new Palette();
        palette.Set(1, new PaletteEntry(Color.White, MaterialType.Diffuse, 0.5f));
        var options = new VoxelizationOptions(VoxelScale: 1.0f, FrozenBorderWidth: 0);
        var grid = HeightmapVoxelizer.Voxelize(heights, materials, palette, options);

        // With border width 0, nothing should be frozen
        foreach (var (_, chunk) in grid.EnumerateChunks())
        {
            for (var i = 0; i < VoxelChunk.TotalVoxels; i++)
            {
                if (chunk.PaletteIndices[i] != 0)
                    Assert.Equal(0, chunk.Flags[i] & (byte)VoxelFlags.Frozen);
            }
        }
    }
}
