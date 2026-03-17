using BeyondImmersion.Bannou.VoxelCore.Grid;
using BeyondImmersion.Bannou.VoxelCore.Math;
using Xunit;

namespace BeyondImmersion.Bannou.VoxelCore.Tests.Grid;

/// <summary>
/// Unit tests for the <see cref="VoxelGrid"/> class.
/// </summary>
public class VoxelGridTests
{
    private static VoxelGrid CreateSmallGrid() =>
        new(new VoxelBounds(new VoxelCoord(0, 0, 0), new VoxelCoord(31, 31, 31)));

    [Fact]
    public void NewGrid_HasZeroVoxels()
    {
        var grid = CreateSmallGrid();
        Assert.Equal(0, grid.VoxelCount);
        Assert.Equal(0, grid.ChunkCount);
    }

    [Fact]
    public void SetVoxel_CreatesChunk()
    {
        var grid = CreateSmallGrid();
        grid.SetVoxel(new VoxelCoord(0, 0, 0), new Voxel(1, VoxelFlags.None));
        Assert.Equal(1, grid.ChunkCount);
        Assert.Equal(1, grid.VoxelCount);
    }

    [Fact]
    public void SetVoxel_ClearingRemovesChunkWhenEmpty()
    {
        var grid = CreateSmallGrid();
        grid.SetVoxel(new VoxelCoord(0, 0, 0), new Voxel(1, VoxelFlags.None));
        Assert.Equal(1, grid.ChunkCount);

        grid.SetVoxel(new VoxelCoord(0, 0, 0), Voxel.Empty);
        Assert.Equal(0, grid.ChunkCount);
        Assert.Equal(0, grid.VoxelCount);
    }

    [Fact]
    public void SetVoxel_EmptyInEmptyChunk_IsNoOp()
    {
        var grid = CreateSmallGrid();
        grid.SetVoxel(new VoxelCoord(5, 5, 5), Voxel.Empty);
        Assert.Equal(0, grid.ChunkCount);
    }

    [Fact]
    public void GetVoxel_ReturnsSetValue()
    {
        var grid = CreateSmallGrid();
        var voxel = new Voxel(42, VoxelFlags.Emissive);
        grid.SetVoxel(new VoxelCoord(10, 5, 3), voxel);
        Assert.Equal(voxel, grid.GetVoxel(new VoxelCoord(10, 5, 3)));
    }

    [Fact]
    public void GetVoxel_OutOfBounds_ReturnsEmpty()
    {
        var grid = CreateSmallGrid();
        Assert.Equal(Voxel.Empty, grid.GetVoxel(new VoxelCoord(100, 100, 100)));
    }

    [Fact]
    public void GetVoxel_EmptyChunk_ReturnsEmpty()
    {
        var grid = CreateSmallGrid();
        Assert.Equal(Voxel.Empty, grid.GetVoxel(new VoxelCoord(5, 5, 5)));
    }

    [Fact]
    public void SetVoxel_OutOfBounds_ThrowsArgumentOutOfRange()
    {
        var grid = CreateSmallGrid();
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            grid.SetVoxel(new VoxelCoord(100, 0, 0), new Voxel(1, VoxelFlags.None)));
    }

    [Fact]
    public void VoxelsInDifferentChunks_CreateSeparateChunks()
    {
        var grid = CreateSmallGrid();
        grid.SetVoxel(new VoxelCoord(0, 0, 0), new Voxel(1, VoxelFlags.None));   // Chunk (0,0,0)
        grid.SetVoxel(new VoxelCoord(16, 0, 0), new Voxel(2, VoxelFlags.None));  // Chunk (1,0,0)
        Assert.Equal(2, grid.ChunkCount);
        Assert.Equal(2, grid.VoxelCount);
    }

    [Fact]
    public void GetChunk_ExistingChunk_ReturnsChunk()
    {
        var grid = CreateSmallGrid();
        grid.SetVoxel(new VoxelCoord(0, 0, 0), new Voxel(1, VoxelFlags.None));
        var chunk = grid.GetChunk(new ChunkCoord(0, 0, 0));
        Assert.NotNull(chunk);
    }

    [Fact]
    public void GetChunk_EmptyCoord_ReturnsNull()
    {
        var grid = CreateSmallGrid();
        Assert.Null(grid.GetChunk(new ChunkCoord(5, 5, 5)));
    }

    [Fact]
    public void IsEmpty_EmptyCoord_ReturnsTrue()
    {
        var grid = CreateSmallGrid();
        Assert.True(grid.IsEmpty(new VoxelCoord(0, 0, 0)));
    }

    [Fact]
    public void IsEmpty_NonEmptyCoord_ReturnsFalse()
    {
        var grid = CreateSmallGrid();
        grid.SetVoxel(new VoxelCoord(5, 5, 5), new Voxel(1, VoxelFlags.None));
        Assert.False(grid.IsEmpty(new VoxelCoord(5, 5, 5)));
    }

    [Fact]
    public void Contains_InsideBounds_ReturnsTrue()
    {
        var grid = CreateSmallGrid();
        Assert.True(grid.Contains(new VoxelCoord(15, 15, 15)));
    }

    [Fact]
    public void Contains_OutsideBounds_ReturnsFalse()
    {
        var grid = CreateSmallGrid();
        Assert.False(grid.Contains(new VoxelCoord(32, 0, 0)));
    }

    [Fact]
    public void EnumerateChunks_ReturnsAllNonEmpty()
    {
        var grid = CreateSmallGrid();
        grid.SetVoxel(new VoxelCoord(0, 0, 0), new Voxel(1, VoxelFlags.None));
        grid.SetVoxel(new VoxelCoord(16, 0, 0), new Voxel(2, VoxelFlags.None));

        var chunks = grid.EnumerateChunks().ToList();
        Assert.Equal(2, chunks.Count);
    }

    [Fact]
    public void GetDirtyChunks_ReturnsModified()
    {
        var grid = CreateSmallGrid();
        grid.SetVoxel(new VoxelCoord(0, 0, 0), new Voxel(1, VoxelFlags.None));
        grid.SetVoxel(new VoxelCoord(16, 0, 0), new Voxel(2, VoxelFlags.None));

        var dirty = grid.GetDirtyChunks();
        Assert.Equal(2, dirty.Count);
    }

    [Fact]
    public void ClearDirtyFlags_ResetsAll()
    {
        var grid = CreateSmallGrid();
        grid.SetVoxel(new VoxelCoord(0, 0, 0), new Voxel(1, VoxelFlags.None));
        grid.ClearDirtyFlags();

        var dirty = grid.GetDirtyChunks();
        Assert.Empty(dirty);
    }

    [Fact]
    public void VoxelCount_TracksAcrossMultipleChunks()
    {
        var grid = CreateSmallGrid();
        grid.SetVoxel(new VoxelCoord(0, 0, 0), new Voxel(1, VoxelFlags.None));
        grid.SetVoxel(new VoxelCoord(1, 0, 0), new Voxel(2, VoxelFlags.None));
        grid.SetVoxel(new VoxelCoord(16, 0, 0), new Voxel(3, VoxelFlags.None));
        Assert.Equal(3, grid.VoxelCount);

        grid.SetVoxel(new VoxelCoord(1, 0, 0), Voxel.Empty);
        Assert.Equal(2, grid.VoxelCount);
    }

    [Fact]
    public void DefaultPalette_IsCreatedAutomatically()
    {
        var grid = CreateSmallGrid();
        Assert.NotNull(grid.Palette);
        Assert.Equal(0, grid.Palette.UsedCount);
    }

    [Fact]
    public void DefaultMetadata_IsCreatedAutomatically()
    {
        var grid = CreateSmallGrid();
        Assert.NotNull(grid.Metadata);
        Assert.Equal(0.25f, grid.Metadata.VoxelScale);
    }
}
