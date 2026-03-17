using BeyondImmersion.Bannou.VoxelBuilder.Core;
using BeyondImmersion.Bannou.VoxelBuilder.Operations;
using BeyondImmersion.Bannou.VoxelCore.Grid;
using BeyondImmersion.Bannou.VoxelCore.Math;
using Xunit;

namespace BeyondImmersion.Bannou.VoxelBuilder.Tests.Operations;

/// <summary>
/// Unit tests for the <see cref="BoxOperation"/>.
/// </summary>
public class BoxOperationTests
{
    private static VoxelGrid CreateGrid() =>
        new(new VoxelBounds(new VoxelCoord(0, 0, 0), new VoxelCoord(31, 31, 31)));

    private static VoxelBuilderOptions DefaultOptions => VoxelBuilderOptions.Default;

    [Fact]
    public void BoxFill_FillsAllCoordsInRegion()
    {
        var grid = CreateGrid();
        var bounds = new VoxelBounds(new VoxelCoord(2, 2, 2), new VoxelCoord(5, 5, 5));
        var op = new BoxOperation(bounds, 7, erase: false);
        op.Execute(grid, DefaultOptions);

        // 4x4x4 = 64 voxels
        Assert.Equal(64, grid.VoxelCount);
        Assert.Equal(7, grid.GetVoxel(new VoxelCoord(2, 2, 2)).PaletteIndex);
        Assert.Equal(7, grid.GetVoxel(new VoxelCoord(5, 5, 5)).PaletteIndex);
        Assert.Equal(7, grid.GetVoxel(new VoxelCoord(3, 4, 5)).PaletteIndex);
    }

    [Fact]
    public void BoxErase_ClearsAllCoordsInRegion()
    {
        var grid = CreateGrid();
        var bounds = new VoxelBounds(new VoxelCoord(0, 0, 0), new VoxelCoord(3, 3, 3));
        // Fill first
        for (var y = 0; y <= 3; y++)
        for (var z = 0; z <= 3; z++)
        for (var x = 0; x <= 3; x++)
            grid.SetVoxel(new VoxelCoord(x, y, z), new Voxel(5, VoxelFlags.None));

        var op = new BoxOperation(bounds, 0, erase: true);
        op.Execute(grid, DefaultOptions);

        Assert.Equal(0, grid.VoxelCount);
    }

    [Fact]
    public void Box_Undo_RestoresAllPreviousVoxels()
    {
        var grid = CreateGrid();
        var bounds = new VoxelBounds(new VoxelCoord(0, 0, 0), new VoxelCoord(1, 1, 1));

        // Place some existing voxels
        grid.SetVoxel(new VoxelCoord(0, 0, 0), new Voxel(3, VoxelFlags.Emissive));

        var op = new BoxOperation(bounds, 7, erase: false);
        op.Execute(grid, DefaultOptions);
        op.Undo(grid);

        var restored = grid.GetVoxel(new VoxelCoord(0, 0, 0));
        Assert.Equal(3, restored.PaletteIndex);
        Assert.Equal(VoxelFlags.Emissive, restored.Flags);

        // Other coords should be empty again
        Assert.True(grid.GetVoxel(new VoxelCoord(1, 1, 1)).IsEmpty);
    }

    [Fact]
    public void Box_SkipsFrozenVoxels()
    {
        var grid = CreateGrid();
        grid.SetVoxel(new VoxelCoord(1, 1, 1), new Voxel(3, VoxelFlags.Frozen));

        var bounds = new VoxelBounds(new VoxelCoord(0, 0, 0), new VoxelCoord(2, 2, 2));
        var op = new BoxOperation(bounds, 7, erase: false);
        op.Execute(grid, DefaultOptions);

        // Frozen voxel untouched
        Assert.Equal(3, grid.GetVoxel(new VoxelCoord(1, 1, 1)).PaletteIndex);
        Assert.Equal(VoxelFlags.Frozen, grid.GetVoxel(new VoxelCoord(1, 1, 1)).Flags);
        // Non-frozen voxels filled
        Assert.Equal(7, grid.GetVoxel(new VoxelCoord(0, 0, 0)).PaletteIndex);
    }

    [Fact]
    public void Box_SkipsOutOfBoundsCoords()
    {
        var grid = CreateGrid(); // bounds 0-31
        var bounds = new VoxelBounds(new VoxelCoord(30, 30, 30), new VoxelCoord(35, 35, 35));
        var op = new BoxOperation(bounds, 7, erase: false);
        op.Execute(grid, DefaultOptions);

        // Only in-bounds portion should be filled: (30-31)^3 = 8 voxels
        Assert.Equal(8, grid.VoxelCount);
    }

    [Fact]
    public void Box_AffectedRegion_MatchesBounds()
    {
        var bounds = new VoxelBounds(new VoxelCoord(2, 3, 4), new VoxelCoord(10, 11, 12));
        var op = new BoxOperation(bounds, 5, erase: false);
        Assert.Equal(bounds, op.AffectedRegion);
    }
}
