using BeyondImmersion.Bannou.VoxelBuilder.Core;
using BeyondImmersion.Bannou.VoxelBuilder.Operations;
using BeyondImmersion.Bannou.VoxelCore.Grid;
using BeyondImmersion.Bannou.VoxelCore.Math;
using Xunit;

namespace BeyondImmersion.Bannou.VoxelBuilder.Tests.Operations;

/// <summary>
/// Unit tests for the <see cref="CompoundOperation"/> atomic grouping.
/// </summary>
public class CompoundOperationTests
{
    private static VoxelGrid CreateGrid() =>
        new(new VoxelBounds(new VoxelCoord(0, 0, 0), new VoxelCoord(31, 31, 31)));

    private static VoxelBuilderOptions DefaultOptions => VoxelBuilderOptions.Default;

    [Fact]
    public void Execute_RunsAllSubOperations()
    {
        var grid = CreateGrid();
        var subOps = new List<IVoxelOperation>
        {
            new PlaceOperation(new VoxelCoord(1, 0, 0), 1),
            new PlaceOperation(new VoxelCoord(2, 0, 0), 2),
            new PlaceOperation(new VoxelCoord(3, 0, 0), 3)
        };

        var compound = new CompoundOperation(subOps, "batch");
        compound.Execute(grid, DefaultOptions);

        Assert.Equal(1, grid.GetVoxel(new VoxelCoord(1, 0, 0)).PaletteIndex);
        Assert.Equal(2, grid.GetVoxel(new VoxelCoord(2, 0, 0)).PaletteIndex);
        Assert.Equal(3, grid.GetVoxel(new VoxelCoord(3, 0, 0)).PaletteIndex);
    }

    [Fact]
    public void Undo_ReversesSubOperationsInOrder()
    {
        var grid = CreateGrid();
        // Place then erase at the same coord: erase should undo first, then place undone
        var subOps = new List<IVoxelOperation>
        {
            new PlaceOperation(new VoxelCoord(1, 0, 0), 5),
            new PlaceOperation(new VoxelCoord(2, 0, 0), 7)
        };

        var compound = new CompoundOperation(subOps, "batch");
        compound.Execute(grid, DefaultOptions);
        compound.Undo(grid);

        Assert.True(grid.GetVoxel(new VoxelCoord(1, 0, 0)).IsEmpty);
        Assert.True(grid.GetVoxel(new VoxelCoord(2, 0, 0)).IsEmpty);
    }

    [Fact]
    public void AffectedRegion_IsUnionOfSubOperations()
    {
        var subOps = new List<IVoxelOperation>
        {
            new PlaceOperation(new VoxelCoord(0, 0, 0), 1),
            new PlaceOperation(new VoxelCoord(10, 10, 10), 2)
        };

        var compound = new CompoundOperation(subOps, "batch");
        var region = compound.AffectedRegion;

        Assert.Equal(new VoxelCoord(0, 0, 0), region.Min);
        Assert.Equal(new VoxelCoord(10, 10, 10), region.Max);
    }

    [Fact]
    public void EmptyCompound_AffectedRegionIsZero()
    {
        var compound = new CompoundOperation(new List<IVoxelOperation>(), "empty");
        var region = compound.AffectedRegion;

        Assert.Equal(VoxelCoord.Zero, region.Min);
        Assert.Equal(VoxelCoord.Zero, region.Max);
    }

    [Fact]
    public void Properties_AreCorrect()
    {
        var subOps = new List<IVoxelOperation>
        {
            new PlaceOperation(VoxelCoord.Zero, 1)
        };

        var compound = new CompoundOperation(subOps, "my description");
        Assert.Equal(VoxelOperationType.Compound, compound.OperationType);
        Assert.Equal("my description", compound.Description);
        Assert.Single(compound.Operations);
    }

    [Fact]
    public void NestedCompound_WorksCorrectly()
    {
        var grid = CreateGrid();
        var inner = new CompoundOperation(new List<IVoxelOperation>
        {
            new PlaceOperation(new VoxelCoord(1, 0, 0), 1),
            new PlaceOperation(new VoxelCoord(2, 0, 0), 2)
        }, "inner");

        var outer = new CompoundOperation(new List<IVoxelOperation>
        {
            inner,
            new PlaceOperation(new VoxelCoord(3, 0, 0), 3)
        }, "outer");

        outer.Execute(grid, DefaultOptions);

        Assert.Equal(1, grid.GetVoxel(new VoxelCoord(1, 0, 0)).PaletteIndex);
        Assert.Equal(2, grid.GetVoxel(new VoxelCoord(2, 0, 0)).PaletteIndex);
        Assert.Equal(3, grid.GetVoxel(new VoxelCoord(3, 0, 0)).PaletteIndex);

        outer.Undo(grid);
        Assert.True(grid.GetVoxel(new VoxelCoord(1, 0, 0)).IsEmpty);
        Assert.True(grid.GetVoxel(new VoxelCoord(2, 0, 0)).IsEmpty);
        Assert.True(grid.GetVoxel(new VoxelCoord(3, 0, 0)).IsEmpty);
    }
}
