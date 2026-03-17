using BeyondImmersion.Bannou.VoxelCore.Grid;
using BeyondImmersion.Bannou.VoxelCore.Math;
using BeyondImmersion.Bannou.VoxelCore.Serialization;
using Xunit;

namespace BeyondImmersion.Bannou.VoxelCore.Tests.Serialization;

/// <summary>
/// Tests for VoxelDelta's LZ4 compression of modified chunk diffs. The delta format now
/// applies RLE + LZ4 (same pipeline as full serializer) instead of raw RLE, reducing
/// delta sizes for large modifications.
/// </summary>
public class VoxelDeltaCompressionTests
{
    private static VoxelGrid CreateGrid()
    {
        var bounds = new VoxelBounds(new VoxelCoord(0, 0, 0), new VoxelCoord(31, 31, 31));
        var grid = new VoxelGrid(bounds);
        grid.Palette.GetOrAddIndex(Color.White, MaterialType.Diffuse);
        grid.Palette.GetOrAddIndex(Color.Black, MaterialType.Metal);
        return grid;
    }

    [Fact]
    public void ModifiedChunk_LZ4Compressed_RoundTrips()
    {
        var oldGrid = CreateGrid();
        // Fill an entire chunk
        for (var x = 0; x < 16; x++)
        for (var y = 0; y < 16; y++)
        for (var z = 0; z < 16; z++)
            oldGrid.SetVoxel(new VoxelCoord(x, y, z), new Voxel(1, VoxelFlags.None));
        oldGrid.ClearDirtyFlags();

        var newGrid = CreateGrid();
        // Same chunk but half changed to different material
        for (var x = 0; x < 16; x++)
        for (var y = 0; y < 16; y++)
        for (var z = 0; z < 16; z++)
        {
            var mat = (byte)(x < 8 ? 1 : 2);
            newGrid.SetVoxel(new VoxelCoord(x, y, z), new Voxel(mat, VoxelFlags.None));
        }

        var delta = VoxelDelta.Compute(oldGrid, newGrid);

        // Apply to a copy of oldGrid
        var patched = CreateGrid();
        for (var x = 0; x < 16; x++)
        for (var y = 0; y < 16; y++)
        for (var z = 0; z < 16; z++)
            patched.SetVoxel(new VoxelCoord(x, y, z), new Voxel(1, VoxelFlags.None));
        patched.ClearDirtyFlags();

        VoxelDelta.Apply(patched, delta);

        // Verify the left half is unchanged and right half is material 2
        Assert.Equal(1, patched.GetVoxel(new VoxelCoord(0, 0, 0)).PaletteIndex);
        Assert.Equal(2, patched.GetVoxel(new VoxelCoord(8, 0, 0)).PaletteIndex);
        Assert.Equal(2, patched.GetVoxel(new VoxelCoord(15, 15, 15)).PaletteIndex);
    }

    [Fact]
    public void ModifiedChunk_FlagsChanges_RoundTrip()
    {
        var oldGrid = CreateGrid();
        oldGrid.SetVoxel(new VoxelCoord(5, 5, 5), new Voxel(1, VoxelFlags.None));
        oldGrid.ClearDirtyFlags();

        var newGrid = CreateGrid();
        newGrid.SetVoxel(new VoxelCoord(5, 5, 5), new Voxel(1, VoxelFlags.Frozen | VoxelFlags.Damaged));

        var delta = VoxelDelta.Compute(oldGrid, newGrid);

        var patched = CreateGrid();
        patched.SetVoxel(new VoxelCoord(5, 5, 5), new Voxel(1, VoxelFlags.None));
        patched.ClearDirtyFlags();
        VoxelDelta.Apply(patched, delta);

        var voxel = patched.GetVoxel(new VoxelCoord(5, 5, 5));
        Assert.Equal(1, voxel.PaletteIndex);
        Assert.Equal(VoxelFlags.Frozen | VoxelFlags.Damaged, voxel.Flags);
    }

    [Fact]
    public void Delta_SmallerThanFullSerialization_ForSmallChanges()
    {
        var oldGrid = CreateGrid();
        for (var x = 0; x < 16; x++)
        for (var y = 0; y < 16; y++)
        for (var z = 0; z < 16; z++)
            oldGrid.SetVoxel(new VoxelCoord(x, y, z), new Voxel(1, VoxelFlags.None));
        oldGrid.ClearDirtyFlags();

        var newGrid = CreateGrid();
        for (var x = 0; x < 16; x++)
        for (var y = 0; y < 16; y++)
        for (var z = 0; z < 16; z++)
            newGrid.SetVoxel(new VoxelCoord(x, y, z), new Voxel(1, VoxelFlags.None));
        // Change just one voxel
        newGrid.SetVoxel(new VoxelCoord(8, 8, 8), new Voxel(2, VoxelFlags.Emissive));

        var delta = VoxelDelta.Compute(oldGrid, newGrid);
        var fullSize = VoxelSerializer.Serialize(newGrid).Length;

        Assert.True(delta.Length < fullSize,
            $"Delta ({delta.Length} bytes) should be smaller than full serialize ({fullSize} bytes) for a single-voxel change");
    }

    [Fact]
    public void MixedOperations_AddRemoveModify_RoundTrip()
    {
        var oldGrid = CreateGrid();
        oldGrid.SetVoxel(new VoxelCoord(0, 0, 0), new Voxel(1, VoxelFlags.None));     // Will be modified
        oldGrid.SetVoxel(new VoxelCoord(16, 0, 0), new Voxel(1, VoxelFlags.None));    // Will be removed
        oldGrid.ClearDirtyFlags();

        var newGrid = CreateGrid();
        newGrid.SetVoxel(new VoxelCoord(0, 0, 0), new Voxel(2, VoxelFlags.Frozen));   // Modified
        // Chunk (1,0,0) removed
        newGrid.SetVoxel(new VoxelCoord(0, 16, 0), new Voxel(1, VoxelFlags.None));    // Added

        var delta = VoxelDelta.Compute(oldGrid, newGrid);

        var patched = CreateGrid();
        patched.SetVoxel(new VoxelCoord(0, 0, 0), new Voxel(1, VoxelFlags.None));
        patched.SetVoxel(new VoxelCoord(16, 0, 0), new Voxel(1, VoxelFlags.None));
        patched.ClearDirtyFlags();
        VoxelDelta.Apply(patched, delta);

        Assert.Equal(new Voxel(2, VoxelFlags.Frozen), patched.GetVoxel(new VoxelCoord(0, 0, 0)));
        Assert.True(patched.IsEmpty(new VoxelCoord(16, 0, 0)));
        Assert.Equal(new Voxel(1, VoxelFlags.None), patched.GetVoxel(new VoxelCoord(0, 16, 0)));
        Assert.Equal(2, patched.VoxelCount);
    }
}
