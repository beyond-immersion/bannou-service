using BeyondImmersion.Bannou.VoxelBuilder.Core;
using BeyondImmersion.Bannou.VoxelBuilder.Operations;
using BeyondImmersion.Bannou.VoxelCore.Grid;
using BeyondImmersion.Bannou.VoxelCore.Math;
using Xunit;

namespace BeyondImmersion.Bannou.VoxelBuilder.Tests.Operations;

/// <summary>
/// Unit tests for the <see cref="FillOperation"/> BFS flood fill.
/// </summary>
public class FillOperationTests
{
    private static VoxelGrid CreateGrid() =>
        new(new VoxelBounds(new VoxelCoord(0, 0, 0), new VoxelCoord(31, 31, 31)));

    private static VoxelBuilderOptions DefaultOptions => VoxelBuilderOptions.Default;

    [Fact]
    public void Fill_SamePaletteIndex_IsNoOp()
    {
        var grid = CreateGrid();
        var coord = new VoxelCoord(5, 5, 5);
        grid.SetVoxel(coord, new Voxel(7, VoxelFlags.None));

        var op = new FillOperation(coord, 7, grid.Bounds);
        op.Execute(grid, DefaultOptions);

        // No change — filling with same palette
        Assert.Equal(7, grid.GetVoxel(coord).PaletteIndex);
    }

    [Fact]
    public void Fill_ReplacesConnectedSameColorVoxels()
    {
        var grid = CreateGrid();
        // Create a connected line of palette index 3
        grid.SetVoxel(new VoxelCoord(5, 5, 5), new Voxel(3, VoxelFlags.None));
        grid.SetVoxel(new VoxelCoord(6, 5, 5), new Voxel(3, VoxelFlags.None));
        grid.SetVoxel(new VoxelCoord(7, 5, 5), new Voxel(3, VoxelFlags.None));
        // Different color neighbor — should not be filled
        grid.SetVoxel(new VoxelCoord(8, 5, 5), new Voxel(5, VoxelFlags.None));

        var op = new FillOperation(new VoxelCoord(5, 5, 5), 9, grid.Bounds);
        op.Execute(grid, DefaultOptions);

        Assert.Equal(9, grid.GetVoxel(new VoxelCoord(5, 5, 5)).PaletteIndex);
        Assert.Equal(9, grid.GetVoxel(new VoxelCoord(6, 5, 5)).PaletteIndex);
        Assert.Equal(9, grid.GetVoxel(new VoxelCoord(7, 5, 5)).PaletteIndex);
        // Unrelated neighbor unchanged
        Assert.Equal(5, grid.GetVoxel(new VoxelCoord(8, 5, 5)).PaletteIndex);
    }

    [Fact]
    public void Fill_PreservesFlags()
    {
        var grid = CreateGrid();
        grid.SetVoxel(new VoxelCoord(5, 5, 5), new Voxel(3, VoxelFlags.Emissive));

        var op = new FillOperation(new VoxelCoord(5, 5, 5), 9, grid.Bounds);
        op.Execute(grid, DefaultOptions);

        var voxel = grid.GetVoxel(new VoxelCoord(5, 5, 5));
        Assert.Equal(9, voxel.PaletteIndex);
        Assert.Equal(VoxelFlags.Emissive, voxel.Flags);
    }

    [Fact]
    public void Fill_Undo_RestoresAllPreviousVoxels()
    {
        var grid = CreateGrid();
        grid.SetVoxel(new VoxelCoord(5, 5, 5), new Voxel(3, VoxelFlags.None));
        grid.SetVoxel(new VoxelCoord(6, 5, 5), new Voxel(3, VoxelFlags.None));

        var op = new FillOperation(new VoxelCoord(5, 5, 5), 9, grid.Bounds);
        op.Execute(grid, DefaultOptions);
        op.Undo(grid);

        Assert.Equal(3, grid.GetVoxel(new VoxelCoord(5, 5, 5)).PaletteIndex);
        Assert.Equal(3, grid.GetVoxel(new VoxelCoord(6, 5, 5)).PaletteIndex);
    }

    [Fact]
    public void Fill_RespectsBoundaryLimit()
    {
        var grid = CreateGrid();
        // Fill a connected region of empty voxels (palette 0)
        var limit = new VoxelBounds(new VoxelCoord(5, 5, 5), new VoxelCoord(7, 5, 5));
        var op = new FillOperation(new VoxelCoord(5, 5, 5), 9, limit);
        op.Execute(grid, DefaultOptions);

        // Only the limited region should be filled
        Assert.Equal(9, grid.GetVoxel(new VoxelCoord(5, 5, 5)).PaletteIndex);
        Assert.Equal(9, grid.GetVoxel(new VoxelCoord(6, 5, 5)).PaletteIndex);
        Assert.Equal(9, grid.GetVoxel(new VoxelCoord(7, 5, 5)).PaletteIndex);
        // Outside the limit
        Assert.True(grid.GetVoxel(new VoxelCoord(8, 5, 5)).IsEmpty);
        Assert.True(grid.GetVoxel(new VoxelCoord(4, 5, 5)).IsEmpty);
    }

    [Fact]
    public void Fill_SkipsFrozenVoxels()
    {
        var grid = CreateGrid();
        grid.SetVoxel(new VoxelCoord(5, 5, 5), new Voxel(3, VoxelFlags.None));
        grid.SetVoxel(new VoxelCoord(6, 5, 5), new Voxel(3, VoxelFlags.Frozen));
        grid.SetVoxel(new VoxelCoord(7, 5, 5), new Voxel(3, VoxelFlags.None));

        var op = new FillOperation(new VoxelCoord(5, 5, 5), 9, grid.Bounds);
        op.Execute(grid, DefaultOptions);

        Assert.Equal(9, grid.GetVoxel(new VoxelCoord(5, 5, 5)).PaletteIndex);
        // Frozen voxel unchanged — blocks further propagation in that direction
        Assert.Equal(3, grid.GetVoxel(new VoxelCoord(6, 5, 5)).PaletteIndex);
    }

    [Fact]
    public void Fill_6Connected_NotDiagonal()
    {
        var grid = CreateGrid();
        // Place a single voxel with palette 3 at origin
        grid.SetVoxel(new VoxelCoord(5, 5, 5), new Voxel(3, VoxelFlags.None));
        // Place a diagonal neighbor with same palette
        grid.SetVoxel(new VoxelCoord(6, 6, 5), new Voxel(3, VoxelFlags.None));

        var op = new FillOperation(new VoxelCoord(5, 5, 5), 9, grid.Bounds);
        op.Execute(grid, DefaultOptions);

        // Origin should be filled
        Assert.Equal(9, grid.GetVoxel(new VoxelCoord(5, 5, 5)).PaletteIndex);
        // Diagonal neighbor should NOT be filled (6-connected, not 26-connected)
        Assert.Equal(3, grid.GetVoxel(new VoxelCoord(6, 6, 5)).PaletteIndex);
    }

    [Fact]
    public void Fill_AffectedRegion_ExpandsDynamically()
    {
        var grid = CreateGrid();
        grid.SetVoxel(new VoxelCoord(0, 0, 0), new Voxel(3, VoxelFlags.None));
        grid.SetVoxel(new VoxelCoord(1, 0, 0), new Voxel(3, VoxelFlags.None));
        grid.SetVoxel(new VoxelCoord(0, 1, 0), new Voxel(3, VoxelFlags.None));

        var op = new FillOperation(new VoxelCoord(0, 0, 0), 9, grid.Bounds);
        op.Execute(grid, DefaultOptions);

        var region = op.AffectedRegion;
        Assert.True(region.Contains(new VoxelCoord(0, 0, 0)));
        Assert.True(region.Contains(new VoxelCoord(1, 0, 0)));
        Assert.True(region.Contains(new VoxelCoord(0, 1, 0)));
    }
}
