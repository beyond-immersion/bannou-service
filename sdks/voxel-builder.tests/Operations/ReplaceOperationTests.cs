using BeyondImmersion.Bannou.VoxelBuilder.Core;
using BeyondImmersion.Bannou.VoxelBuilder.Operations;
using BeyondImmersion.Bannou.VoxelCore.Grid;
using BeyondImmersion.Bannou.VoxelCore.Math;
using Xunit;

namespace BeyondImmersion.Bannou.VoxelBuilder.Tests.Operations;

/// <summary>
/// Unit tests for the <see cref="ReplaceOperation"/>.
/// </summary>
public class ReplaceOperationTests
{
    private static VoxelGrid CreateGrid() =>
        new(new VoxelBounds(new VoxelCoord(0, 0, 0), new VoxelCoord(31, 31, 31)));

    private static VoxelBuilderOptions DefaultOptions => VoxelBuilderOptions.Default;

    [Fact]
    public void Replace_ChangesMatchingVoxels()
    {
        var grid = CreateGrid();
        grid.SetVoxel(new VoxelCoord(0, 0, 0), new Voxel(3, VoxelFlags.None));
        grid.SetVoxel(new VoxelCoord(1, 0, 0), new Voxel(3, VoxelFlags.None));
        grid.SetVoxel(new VoxelCoord(2, 0, 0), new Voxel(5, VoxelFlags.None));

        var op = new ReplaceOperation(3, 9);
        op.Execute(grid, DefaultOptions);

        Assert.Equal(9, grid.GetVoxel(new VoxelCoord(0, 0, 0)).PaletteIndex);
        Assert.Equal(9, grid.GetVoxel(new VoxelCoord(1, 0, 0)).PaletteIndex);
        Assert.Equal(5, grid.GetVoxel(new VoxelCoord(2, 0, 0)).PaletteIndex);
    }

    [Fact]
    public void Replace_PreservesFlags()
    {
        var grid = CreateGrid();
        grid.SetVoxel(new VoxelCoord(0, 0, 0), new Voxel(3, VoxelFlags.Emissive));

        var op = new ReplaceOperation(3, 9);
        op.Execute(grid, DefaultOptions);

        var voxel = grid.GetVoxel(new VoxelCoord(0, 0, 0));
        Assert.Equal(9, voxel.PaletteIndex);
        Assert.Equal(VoxelFlags.Emissive, voxel.Flags);
    }

    [Fact]
    public void Replace_Undo_RestoresOriginalVoxels()
    {
        var grid = CreateGrid();
        grid.SetVoxel(new VoxelCoord(0, 0, 0), new Voxel(3, VoxelFlags.Emissive));
        grid.SetVoxel(new VoxelCoord(1, 0, 0), new Voxel(3, VoxelFlags.None));

        var op = new ReplaceOperation(3, 9);
        op.Execute(grid, DefaultOptions);
        op.Undo(grid);

        Assert.Equal(3, grid.GetVoxel(new VoxelCoord(0, 0, 0)).PaletteIndex);
        Assert.Equal(VoxelFlags.Emissive, grid.GetVoxel(new VoxelCoord(0, 0, 0)).Flags);
        Assert.Equal(3, grid.GetVoxel(new VoxelCoord(1, 0, 0)).PaletteIndex);
    }

    [Fact]
    public void Replace_SkipsFrozenVoxels()
    {
        var grid = CreateGrid();
        grid.SetVoxel(new VoxelCoord(0, 0, 0), new Voxel(3, VoxelFlags.None));
        grid.SetVoxel(new VoxelCoord(1, 0, 0), new Voxel(3, VoxelFlags.Frozen));

        var op = new ReplaceOperation(3, 9);
        op.Execute(grid, DefaultOptions);

        Assert.Equal(9, grid.GetVoxel(new VoxelCoord(0, 0, 0)).PaletteIndex);
        Assert.Equal(3, grid.GetVoxel(new VoxelCoord(1, 0, 0)).PaletteIndex);
    }

    [Fact]
    public void Replace_NoMatches_AffectedRegionIsZero()
    {
        var grid = CreateGrid();
        grid.SetVoxel(new VoxelCoord(0, 0, 0), new Voxel(5, VoxelFlags.None));

        var op = new ReplaceOperation(3, 9);
        op.Execute(grid, DefaultOptions);

        Assert.Equal(new VoxelBounds(VoxelCoord.Zero, VoxelCoord.Zero), op.AffectedRegion);
    }

    [Fact]
    public void Replace_AffectedRegion_SpansAllChangedVoxels()
    {
        var grid = CreateGrid();
        grid.SetVoxel(new VoxelCoord(2, 3, 4), new Voxel(3, VoxelFlags.None));
        grid.SetVoxel(new VoxelCoord(10, 15, 20), new Voxel(3, VoxelFlags.None));

        var op = new ReplaceOperation(3, 9);
        op.Execute(grid, DefaultOptions);

        Assert.True(op.AffectedRegion.Contains(new VoxelCoord(2, 3, 4)));
        Assert.True(op.AffectedRegion.Contains(new VoxelCoord(10, 15, 20)));
    }
}
