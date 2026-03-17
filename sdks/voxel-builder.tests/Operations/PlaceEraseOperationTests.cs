using BeyondImmersion.Bannou.VoxelBuilder.Core;
using BeyondImmersion.Bannou.VoxelBuilder.Operations;
using BeyondImmersion.Bannou.VoxelCore.Grid;
using BeyondImmersion.Bannou.VoxelCore.Math;
using Xunit;

namespace BeyondImmersion.Bannou.VoxelBuilder.Tests.Operations;

/// <summary>
/// Unit tests for <see cref="PlaceOperation"/> and <see cref="EraseOperation"/>.
/// </summary>
public class PlaceEraseOperationTests
{
    private static VoxelGrid CreateGrid() =>
        new(new VoxelBounds(new VoxelCoord(0, 0, 0), new VoxelCoord(31, 31, 31)));

    private static VoxelBuilderOptions DefaultOptions => VoxelBuilderOptions.Default;

    #region PlaceOperation

    [Fact]
    public void Place_SetsVoxel()
    {
        var grid = CreateGrid();
        var op = new PlaceOperation(new VoxelCoord(5, 5, 5), 7);
        op.Execute(grid, DefaultOptions);

        Assert.Equal(7, grid.GetVoxel(new VoxelCoord(5, 5, 5)).PaletteIndex);
    }

    [Fact]
    public void Place_OverwritesExistingVoxel()
    {
        var grid = CreateGrid();
        grid.SetVoxel(new VoxelCoord(5, 5, 5), new Voxel(3, VoxelFlags.None));

        var op = new PlaceOperation(new VoxelCoord(5, 5, 5), 7);
        op.Execute(grid, DefaultOptions);

        Assert.Equal(7, grid.GetVoxel(new VoxelCoord(5, 5, 5)).PaletteIndex);
    }

    [Fact]
    public void Place_Undo_RestoresPreviousVoxel()
    {
        var grid = CreateGrid();
        var coord = new VoxelCoord(5, 5, 5);
        grid.SetVoxel(coord, new Voxel(3, VoxelFlags.Emissive));

        var op = new PlaceOperation(coord, 7);
        op.Execute(grid, DefaultOptions);
        Assert.Equal(7, grid.GetVoxel(coord).PaletteIndex);

        op.Undo(grid);
        var restored = grid.GetVoxel(coord);
        Assert.Equal(3, restored.PaletteIndex);
        Assert.Equal(VoxelFlags.Emissive, restored.Flags);
    }

    [Fact]
    public void Place_Undo_RestoresEmpty()
    {
        var grid = CreateGrid();
        var coord = new VoxelCoord(5, 5, 5);

        var op = new PlaceOperation(coord, 7);
        op.Execute(grid, DefaultOptions);
        op.Undo(grid);

        Assert.True(grid.GetVoxel(coord).IsEmpty);
    }

    [Fact]
    public void Place_SkipsFrozenVoxel()
    {
        var grid = CreateGrid();
        var coord = new VoxelCoord(5, 5, 5);
        grid.SetVoxel(coord, new Voxel(3, VoxelFlags.Frozen));

        var op = new PlaceOperation(coord, 7);
        op.Execute(grid, DefaultOptions);

        // Should not have changed — frozen enforcement active
        Assert.Equal(3, grid.GetVoxel(coord).PaletteIndex);
    }

    [Fact]
    public void Place_FrozenNotEnforced_Overwrites()
    {
        var grid = CreateGrid();
        var coord = new VoxelCoord(5, 5, 5);
        grid.SetVoxel(coord, new Voxel(3, VoxelFlags.Frozen));

        var options = new VoxelBuilderOptions(EnforceFrozen: false);
        var op = new PlaceOperation(coord, 7);
        op.Execute(grid, options);

        Assert.Equal(7, grid.GetVoxel(coord).PaletteIndex);
    }

    [Fact]
    public void Place_Undo_WhenSkippedFrozen_NoChange()
    {
        var grid = CreateGrid();
        var coord = new VoxelCoord(5, 5, 5);
        grid.SetVoxel(coord, new Voxel(3, VoxelFlags.Frozen));

        var op = new PlaceOperation(coord, 7);
        op.Execute(grid, DefaultOptions);
        op.Undo(grid);

        // Still the frozen voxel
        Assert.Equal(3, grid.GetVoxel(coord).PaletteIndex);
        Assert.Equal(VoxelFlags.Frozen, grid.GetVoxel(coord).Flags);
    }

    [Fact]
    public void Place_Properties_AreCorrect()
    {
        var coord = new VoxelCoord(10, 20, 30);
        var op = new PlaceOperation(coord, 42);

        Assert.Equal(VoxelOperationType.Place, op.OperationType);
        Assert.Equal(coord, op.Coord);
        Assert.Equal(42, op.PaletteIndex);
        Assert.Contains("42", op.Description);
        Assert.Equal(new VoxelBounds(coord, coord), op.AffectedRegion);
    }

    #endregion

    #region EraseOperation

    [Fact]
    public void Erase_RemovesVoxel()
    {
        var grid = CreateGrid();
        var coord = new VoxelCoord(5, 5, 5);
        grid.SetVoxel(coord, new Voxel(7, VoxelFlags.None));

        var op = new EraseOperation(coord);
        op.Execute(grid, DefaultOptions);

        Assert.True(grid.GetVoxel(coord).IsEmpty);
    }

    [Fact]
    public void Erase_Undo_RestoresPreviousVoxel()
    {
        var grid = CreateGrid();
        var coord = new VoxelCoord(5, 5, 5);
        grid.SetVoxel(coord, new Voxel(7, VoxelFlags.Emissive));

        var op = new EraseOperation(coord);
        op.Execute(grid, DefaultOptions);
        op.Undo(grid);

        Assert.Equal(7, grid.GetVoxel(coord).PaletteIndex);
        Assert.Equal(VoxelFlags.Emissive, grid.GetVoxel(coord).Flags);
    }

    [Fact]
    public void Erase_SkipsFrozenVoxel()
    {
        var grid = CreateGrid();
        var coord = new VoxelCoord(5, 5, 5);
        grid.SetVoxel(coord, new Voxel(7, VoxelFlags.Frozen));

        var op = new EraseOperation(coord);
        op.Execute(grid, DefaultOptions);

        // Should not have been erased
        Assert.Equal(7, grid.GetVoxel(coord).PaletteIndex);
    }

    [Fact]
    public void Erase_Properties_AreCorrect()
    {
        var coord = new VoxelCoord(10, 20, 30);
        var op = new EraseOperation(coord);

        Assert.Equal(VoxelOperationType.Erase, op.OperationType);
        Assert.Equal(coord, op.Coord);
        Assert.Contains("Erase", op.Description);
    }

    #endregion
}
