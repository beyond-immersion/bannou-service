using BeyondImmersion.Bannou.VoxelCore.Grid;
using BeyondImmersion.Bannou.VoxelCore.Math;
using BeyondImmersion.Bannou.VoxelCore.Voxelization;
using Xunit;

namespace BeyondImmersion.Bannou.VoxelCore.Tests.Voxelization;

/// <summary>
/// Unit tests for <see cref="HeightmapVoxelizer"/> — heightmap to voxel grid conversion.
/// </summary>
public class HeightmapVoxelizerTests
{
    [Fact]
    public void FlatHeightmap_FillsColumnsToHeight()
    {
        var heights = new float[4, 4];
        var materials = new byte[4, 4];

        // Flat heightmap at height 2.0 with voxel scale 1.0 → 3 voxels per column (y=0,1,2)
        for (var x = 0; x < 4; x++)
        for (var z = 0; z < 4; z++)
        {
            heights[x, z] = 2.0f;
            materials[x, z] = 1;
        }

        var palette = new Palette();
        palette.Set(1, new PaletteEntry(Color.White, MaterialType.Diffuse, 0.5f));
        var options = new VoxelizationOptions(VoxelScale: 1.0f, FrozenBorderWidth: 0);

        var grid = HeightmapVoxelizer.Voxelize(heights, materials, palette, options);

        // Each of 16 columns fills y=0,1,2 → 3 voxels each → 48 total
        Assert.Equal(48, grid.VoxelCount);

        // Check a specific voxel
        Assert.False(grid.IsEmpty(new VoxelCoord(2, 0, 2)));
        Assert.False(grid.IsEmpty(new VoxelCoord(2, 2, 2)));
        Assert.True(grid.IsEmpty(new VoxelCoord(2, 3, 2)));
    }

    [Fact]
    public void DefaultPaletteIndex_UsedWhenMaterialIsZero()
    {
        var heights = new float[2, 2];
        var materials = new byte[2, 2];

        heights[0, 0] = 1.0f;
        materials[0, 0] = 0; // Zero — should use default

        var palette = new Palette();
        palette.Set(5, new PaletteEntry(Color.White, MaterialType.Diffuse, 0.5f));
        var options = new VoxelizationOptions(VoxelScale: 1.0f, DefaultPaletteIndex: 5, FrozenBorderWidth: 0);

        var grid = HeightmapVoxelizer.Voxelize(heights, materials, palette, options);

        var voxel = grid.GetVoxel(new VoxelCoord(0, 0, 0));
        Assert.Equal(5, voxel.PaletteIndex);
    }

    [Fact]
    public void FrozenBorder_MarkedOnEdgeVoxels()
    {
        var heights = new float[8, 8];
        var materials = new byte[8, 8];
        for (var x = 0; x < 8; x++)
        for (var z = 0; z < 8; z++)
        {
            heights[x, z] = 3.0f;
            materials[x, z] = 1;
        }

        var palette = new Palette();
        palette.Set(1, new PaletteEntry(Color.White, MaterialType.Diffuse, 0.5f));
        var options = new VoxelizationOptions(VoxelScale: 1.0f, FrozenBorderWidth: 1);

        var grid = HeightmapVoxelizer.Voxelize(heights, materials, palette, options);

        // Edge voxel (0,0,0) should be frozen (border column)
        var edgeVoxel = grid.GetVoxel(new VoxelCoord(0, 0, 0));
        Assert.True(edgeVoxel.Flags.HasFlag(VoxelFlags.Frozen));

        // Interior voxel (4,0,4) should NOT be frozen
        var interiorVoxel = grid.GetVoxel(new VoxelCoord(4, 0, 4));
        Assert.False(interiorVoxel.Flags.HasFlag(VoxelFlags.Frozen));
    }

    [Fact]
    public void VaryingHeights_ProduceDifferentColumnHeights()
    {
        var heights = new float[4, 4];
        var materials = new byte[4, 4];

        heights[0, 0] = 1.0f;
        heights[1, 0] = 3.0f;
        heights[2, 0] = 5.0f;
        materials[0, 0] = 1;
        materials[1, 0] = 1;
        materials[2, 0] = 1;

        var palette = new Palette();
        palette.Set(1, new PaletteEntry(Color.White, MaterialType.Diffuse, 0.5f));
        var options = new VoxelizationOptions(VoxelScale: 1.0f, FrozenBorderWidth: 0);

        var grid = HeightmapVoxelizer.Voxelize(heights, materials, palette, options);

        Assert.False(grid.IsEmpty(new VoxelCoord(0, 1, 0)));
        Assert.True(grid.IsEmpty(new VoxelCoord(0, 2, 0)));

        Assert.False(grid.IsEmpty(new VoxelCoord(1, 3, 0)));
        Assert.True(grid.IsEmpty(new VoxelCoord(1, 4, 0)));

        Assert.False(grid.IsEmpty(new VoxelCoord(2, 5, 0)));
        Assert.True(grid.IsEmpty(new VoxelCoord(2, 6, 0)));
    }

    [Fact]
    public void VoxelScale_AffectsColumnHeight()
    {
        var heights = new float[2, 2];
        var materials = new byte[2, 2];
        heights[0, 0] = 1.0f;
        materials[0, 0] = 1;

        var palette = new Palette();
        palette.Set(1, new PaletteEntry(Color.White, MaterialType.Diffuse, 0.5f));

        // Scale 0.25 → height 1.0 / 0.25 = 4 voxels (y=0,1,2,3,4)
        var options = new VoxelizationOptions(VoxelScale: 0.25f, FrozenBorderWidth: 0);
        var grid = HeightmapVoxelizer.Voxelize(heights, materials, palette, options);

        Assert.False(grid.IsEmpty(new VoxelCoord(0, 4, 0)));
        Assert.True(grid.IsEmpty(new VoxelCoord(0, 5, 0)));
    }
}
