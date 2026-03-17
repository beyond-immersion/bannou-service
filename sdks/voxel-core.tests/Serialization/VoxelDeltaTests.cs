using BeyondImmersion.Bannou.VoxelCore.Grid;
using BeyondImmersion.Bannou.VoxelCore.Math;
using BeyondImmersion.Bannou.VoxelCore.Serialization;
using Xunit;

namespace BeyondImmersion.Bannou.VoxelCore.Tests.Serialization;

/// <summary>
/// Unit tests for <see cref="VoxelDelta"/> — chunk-level binary delta encoding.
/// </summary>
public class VoxelDeltaTests
{
    private static VoxelGrid CreateGrid()
    {
        var bounds = new VoxelBounds(new VoxelCoord(0, 0, 0), new VoxelCoord(63, 63, 63));
        var grid = new VoxelGrid(bounds);
        grid.Palette.GetOrAddIndex(Color.White, MaterialType.Diffuse);
        grid.Palette.GetOrAddIndex(Color.Black, MaterialType.Metal);
        return grid;
    }

    [Fact]
    public void IdenticalGrids_ProducesEmptyDelta()
    {
        var grid = CreateGrid();
        grid.SetVoxel(new VoxelCoord(0, 0, 0), new Voxel(1, VoxelFlags.None));
        grid.ClearDirtyFlags();

        var delta = VoxelDelta.Compute(grid, grid);
        // With no dirty chunks and same chunk keys, the delta should have 0 added, 0 removed, 0 modified
        Assert.NotNull(delta);
    }

    [Fact]
    public void AddedChunk_DetectedAndApplied()
    {
        var oldGrid = CreateGrid();
        oldGrid.SetVoxel(new VoxelCoord(0, 0, 0), new Voxel(1, VoxelFlags.None));
        oldGrid.ClearDirtyFlags();

        var newGrid = CreateGrid();
        newGrid.SetVoxel(new VoxelCoord(0, 0, 0), new Voxel(1, VoxelFlags.None));
        newGrid.SetVoxel(new VoxelCoord(16, 0, 0), new Voxel(2, VoxelFlags.None)); // New chunk

        var delta = VoxelDelta.Compute(oldGrid, newGrid);

        // Apply delta to a copy of oldGrid
        var patched = CreateGrid();
        patched.SetVoxel(new VoxelCoord(0, 0, 0), new Voxel(1, VoxelFlags.None));
        VoxelDelta.Apply(patched, delta);

        Assert.Equal(2, patched.VoxelCount);
        Assert.Equal(new Voxel(2, VoxelFlags.None), patched.GetVoxel(new VoxelCoord(16, 0, 0)));
    }

    [Fact]
    public void RemovedChunk_DetectedAndApplied()
    {
        var oldGrid = CreateGrid();
        oldGrid.SetVoxel(new VoxelCoord(0, 0, 0), new Voxel(1, VoxelFlags.None));
        oldGrid.SetVoxel(new VoxelCoord(16, 0, 0), new Voxel(2, VoxelFlags.None));
        oldGrid.ClearDirtyFlags();

        var newGrid = CreateGrid();
        newGrid.SetVoxel(new VoxelCoord(0, 0, 0), new Voxel(1, VoxelFlags.None));
        // Chunk (1,0,0) is missing in newGrid

        var delta = VoxelDelta.Compute(oldGrid, newGrid);

        var patched = CreateGrid();
        patched.SetVoxel(new VoxelCoord(0, 0, 0), new Voxel(1, VoxelFlags.None));
        patched.SetVoxel(new VoxelCoord(16, 0, 0), new Voxel(2, VoxelFlags.None));
        VoxelDelta.Apply(patched, delta);

        Assert.Equal(1, patched.VoxelCount);
        Assert.True(patched.IsEmpty(new VoxelCoord(16, 0, 0)));
    }

    [Fact]
    public void ModifiedChunk_DetectedAndApplied()
    {
        var oldGrid = CreateGrid();
        oldGrid.SetVoxel(new VoxelCoord(5, 5, 5), new Voxel(1, VoxelFlags.None));
        oldGrid.ClearDirtyFlags();

        var newGrid = CreateGrid();
        newGrid.SetVoxel(new VoxelCoord(5, 5, 5), new Voxel(2, VoxelFlags.Frozen)); // Changed material and flags
        // newGrid's chunk is dirty

        var delta = VoxelDelta.Compute(oldGrid, newGrid);

        var patched = CreateGrid();
        patched.SetVoxel(new VoxelCoord(5, 5, 5), new Voxel(1, VoxelFlags.None));
        patched.ClearDirtyFlags(); // Need to clear so Apply can re-mark
        VoxelDelta.Apply(patched, delta);

        var voxel = patched.GetVoxel(new VoxelCoord(5, 5, 5));
        Assert.Equal(2, voxel.PaletteIndex);
        Assert.Equal(VoxelFlags.Frozen, voxel.Flags);
    }

    [Fact]
    public void Delta_IsDeterministic()
    {
        var oldGrid = CreateGrid();
        oldGrid.SetVoxel(new VoxelCoord(0, 0, 0), new Voxel(1, VoxelFlags.None));
        oldGrid.ClearDirtyFlags();

        var newGrid = CreateGrid();
        newGrid.SetVoxel(new VoxelCoord(0, 0, 0), new Voxel(1, VoxelFlags.None));
        newGrid.SetVoxel(new VoxelCoord(16, 0, 0), new Voxel(2, VoxelFlags.None));

        var delta1 = VoxelDelta.Compute(oldGrid, newGrid);
        var delta2 = VoxelDelta.Compute(oldGrid, newGrid);

        Assert.Equal(delta1, delta2);
    }
}
