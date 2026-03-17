using BeyondImmersion.Bannou.VoxelCore.Grid;
using BeyondImmersion.Bannou.VoxelCore.Math;
using BeyondImmersion.Bannou.VoxelCore.Serialization;
using Xunit;

namespace BeyondImmersion.Bannou.VoxelCore.Tests.Serialization;

/// <summary>
/// Tests for the .bvox chunk table format which now includes nonEmptyCount per chunk
/// (14 bytes per entry instead of 12). Verifies that NonEmptyCount is preserved
/// through serialization roundtrip.
/// </summary>
public class VoxelSerializerChunkTableTests
{
    [Fact]
    public void NonEmptyCount_PreservedThroughRoundTrip()
    {
        var bounds = new VoxelBounds(new VoxelCoord(0, 0, 0), new VoxelCoord(31, 31, 31));
        var grid = new VoxelGrid(bounds);
        grid.Palette.GetOrAddIndex(Color.White, MaterialType.Diffuse);

        // Place exactly 7 voxels in one chunk
        for (var i = 0; i < 7; i++)
            grid.SetVoxel(new VoxelCoord(i, 0, 0), new Voxel(1, VoxelFlags.None));

        var chunk = grid.GetChunk(new ChunkCoord(0, 0, 0));
        Assert.NotNull(chunk);
        Assert.Equal(7, chunk.NonEmptyCount);

        var bytes = VoxelSerializer.Serialize(grid);
        var restored = VoxelSerializer.Deserialize(bytes);

        var restoredChunk = restored.GetChunk(new ChunkCoord(0, 0, 0));
        Assert.NotNull(restoredChunk);
        Assert.Equal(7, restoredChunk.NonEmptyCount);
    }

    [Fact]
    public void MultipleChunks_EachPreservesNonEmptyCount()
    {
        var bounds = new VoxelBounds(new VoxelCoord(0, 0, 0), new VoxelCoord(47, 47, 47));
        var grid = new VoxelGrid(bounds);
        grid.Palette.GetOrAddIndex(Color.White, MaterialType.Diffuse);
        grid.Palette.GetOrAddIndex(Color.Black, MaterialType.Metal);

        // Chunk (0,0,0): 3 voxels
        grid.SetVoxel(new VoxelCoord(0, 0, 0), new Voxel(1, VoxelFlags.None));
        grid.SetVoxel(new VoxelCoord(1, 0, 0), new Voxel(1, VoxelFlags.None));
        grid.SetVoxel(new VoxelCoord(2, 0, 0), new Voxel(1, VoxelFlags.None));

        // Chunk (1,0,0): 1 voxel
        grid.SetVoxel(new VoxelCoord(16, 0, 0), new Voxel(2, VoxelFlags.Frozen));

        // Chunk (0,1,0): 16 voxels (full row)
        for (var x = 0; x < 16; x++)
            grid.SetVoxel(new VoxelCoord(x, 16, 0), new Voxel(1, VoxelFlags.None));

        var bytes = VoxelSerializer.Serialize(grid);
        var restored = VoxelSerializer.Deserialize(bytes);

        Assert.Equal(3, restored.GetChunk(new ChunkCoord(0, 0, 0))?.NonEmptyCount);
        Assert.Equal(1, restored.GetChunk(new ChunkCoord(1, 0, 0))?.NonEmptyCount);
        Assert.Equal(16, restored.GetChunk(new ChunkCoord(0, 1, 0))?.NonEmptyCount);
    }

    [Fact]
    public void TotalVoxelCount_MatchesSumOfChunkCounts()
    {
        var bounds = new VoxelBounds(new VoxelCoord(0, 0, 0), new VoxelCoord(31, 31, 31));
        var grid = new VoxelGrid(bounds);
        grid.Palette.GetOrAddIndex(Color.White, MaterialType.Diffuse);

        grid.SetVoxel(new VoxelCoord(0, 0, 0), new Voxel(1, VoxelFlags.None));
        grid.SetVoxel(new VoxelCoord(5, 5, 5), new Voxel(1, VoxelFlags.None));
        grid.SetVoxel(new VoxelCoord(16, 0, 0), new Voxel(1, VoxelFlags.None));

        var bytes = VoxelSerializer.Serialize(grid);
        var restored = VoxelSerializer.Deserialize(bytes);

        var sumFromChunks = restored.EnumerateChunks().Sum(c => c.Chunk.NonEmptyCount);
        Assert.Equal(restored.VoxelCount, sumFromChunks);
        Assert.Equal(3, restored.VoxelCount);
    }
}
