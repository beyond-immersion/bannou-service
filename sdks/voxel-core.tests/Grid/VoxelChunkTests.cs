using BeyondImmersion.Bannou.VoxelCore.Grid;
using BeyondImmersion.Bannou.VoxelCore.Math;
using Xunit;

namespace BeyondImmersion.Bannou.VoxelCore.Tests.Grid;

/// <summary>
/// Unit tests for the <see cref="VoxelChunk"/> class.
/// </summary>
public class VoxelChunkTests
{
    [Fact]
    public void NewChunk_IsEmpty()
    {
        var chunk = new VoxelChunk();
        Assert.True(chunk.IsEmpty);
        Assert.Equal(0, chunk.NonEmptyCount);
    }

    [Fact]
    public void NewChunk_IsNotDirty()
    {
        var chunk = new VoxelChunk();
        Assert.False(chunk.IsDirty);
    }

    [Fact]
    public void GetFlatIndex_XZYOrder()
    {
        // XZY order: x + z * 16 + y * 256
        Assert.Equal(0, VoxelChunk.GetFlatIndex(0, 0, 0));
        Assert.Equal(1, VoxelChunk.GetFlatIndex(1, 0, 0));
        Assert.Equal(16, VoxelChunk.GetFlatIndex(0, 0, 1));
        Assert.Equal(256, VoxelChunk.GetFlatIndex(0, 1, 0));
        Assert.Equal(273, VoxelChunk.GetFlatIndex(1, 1, 1)); // 1 + 1*16 + 1*256
    }

    [Fact]
    public void GetFlatIndex_MaxCoordinates()
    {
        // (15, 15, 15) = 15 + 15*16 + 15*256 = 15 + 240 + 3840 = 4095
        Assert.Equal(4095, VoxelChunk.GetFlatIndex(15, 15, 15));
    }

    [Fact]
    public void SetVoxel_MarksDirty()
    {
        var chunk = new VoxelChunk();
        chunk.SetVoxel(0, 0, 0, new Voxel(1, VoxelFlags.None));
        Assert.True(chunk.IsDirty);
    }

    [Fact]
    public void SetVoxel_IncrementsNonEmptyCount()
    {
        var chunk = new VoxelChunk();
        chunk.SetVoxel(0, 0, 0, new Voxel(1, VoxelFlags.None));
        Assert.Equal(1, chunk.NonEmptyCount);
    }

    [Fact]
    public void SetVoxel_ClearingDecrementsNonEmptyCount()
    {
        var chunk = new VoxelChunk();
        chunk.SetVoxel(0, 0, 0, new Voxel(1, VoxelFlags.None));
        Assert.Equal(1, chunk.NonEmptyCount);

        chunk.SetVoxel(0, 0, 0, Voxel.Empty);
        Assert.Equal(0, chunk.NonEmptyCount);
    }

    [Fact]
    public void SetVoxel_OverwriteDoesNotDoubleCount()
    {
        var chunk = new VoxelChunk();
        chunk.SetVoxel(0, 0, 0, new Voxel(1, VoxelFlags.None));
        chunk.SetVoxel(0, 0, 0, new Voxel(2, VoxelFlags.None));
        Assert.Equal(1, chunk.NonEmptyCount);
    }

    [Fact]
    public void GetVoxel_ReturnsSetValue()
    {
        var chunk = new VoxelChunk();
        var voxel = new Voxel(42, VoxelFlags.Emissive);
        chunk.SetVoxel(5, 10, 3, voxel);
        Assert.Equal(voxel, chunk.GetVoxel(5, 10, 3));
    }

    [Fact]
    public void GetVoxel_DefaultIsEmpty()
    {
        var chunk = new VoxelChunk();
        Assert.Equal(Voxel.Empty, chunk.GetVoxel(7, 7, 7));
    }

    [Fact]
    public void MultipleVoxels_CountTrackedCorrectly()
    {
        var chunk = new VoxelChunk();
        for (var i = 0; i < 10; i++)
            chunk.SetVoxel(i, 0, 0, new Voxel(1, VoxelFlags.None));

        Assert.Equal(10, chunk.NonEmptyCount);
        Assert.False(chunk.IsEmpty);
    }

    [Fact]
    public void SetVoxel_ClearAll_CountGoesToZero()
    {
        var chunk = new VoxelChunk();
        chunk.SetVoxel(0, 0, 0, new Voxel(1, VoxelFlags.None));
        chunk.SetVoxel(5, 5, 5, new Voxel(2, VoxelFlags.None));
        chunk.SetVoxel(15, 15, 15, new Voxel(3, VoxelFlags.None));
        Assert.Equal(3, chunk.NonEmptyCount);

        chunk.SetVoxel(0, 0, 0, Voxel.Empty);
        chunk.SetVoxel(5, 5, 5, Voxel.Empty);
        chunk.SetVoxel(15, 15, 15, Voxel.Empty);
        Assert.Equal(0, chunk.NonEmptyCount);
        Assert.True(chunk.IsEmpty);
    }

    #region Internal API Tests

    [Fact]
    public void PaletteIndices_DirectAccess_BypassesCountTracking()
    {
        var chunk = new VoxelChunk();
        chunk.PaletteIndices[0] = 1;
        chunk.PaletteIndices[100] = 5;
        chunk.PaletteIndices[4000] = 255;

        // Count is stale because we bypassed SetVoxel
        Assert.Equal(0, chunk.NonEmptyCount);
        Assert.True(chunk.IsEmpty); // Wrong — but expected since we bypassed tracking
    }

    [Fact]
    public void RecalculateNonEmptyCount_CorrectsStaleCounts()
    {
        var chunk = new VoxelChunk();
        chunk.PaletteIndices[0] = 1;
        chunk.PaletteIndices[100] = 5;
        chunk.PaletteIndices[4000] = 255;

        chunk.RecalculateNonEmptyCount();
        Assert.Equal(3, chunk.NonEmptyCount);
    }

    [Fact]
    public void Flags_DirectAccess_WritesAndReads()
    {
        var chunk = new VoxelChunk();
        chunk.PaletteIndices[0] = 1;
        chunk.Flags[0] = (byte)(VoxelFlags.Frozen | VoxelFlags.Emissive);

        var voxel = chunk.GetVoxel(0, 0, 0);
        Assert.Equal(VoxelFlags.Frozen | VoxelFlags.Emissive, voxel.Flags);
    }

    [Fact]
    public void PaletteIndices_Length_Is4096()
    {
        var chunk = new VoxelChunk();
        Assert.Equal(VoxelChunk.TotalVoxels, chunk.PaletteIndices.Length);
        Assert.Equal(4096, chunk.PaletteIndices.Length);
    }

    [Fact]
    public void Flags_Length_Is4096()
    {
        var chunk = new VoxelChunk();
        Assert.Equal(VoxelChunk.TotalVoxels, chunk.Flags.Length);
    }

    [Fact]
    public void IsDirty_CanBeSetExternally()
    {
        var chunk = new VoxelChunk();
        Assert.False(chunk.IsDirty);

        chunk.IsDirty = true;
        Assert.True(chunk.IsDirty);

        chunk.IsDirty = false;
        Assert.False(chunk.IsDirty);
    }

    [Fact]
    public void VoxelCount_CanBeSetOnGrid()
    {
        var grid = new VoxelGrid(new VoxelBounds(
            new VoxelCoord(0, 0, 0), new VoxelCoord(15, 15, 15)));
        Assert.Equal(0, grid.VoxelCount);

        grid.VoxelCount = 42;
        Assert.Equal(42, grid.VoxelCount);
    }

    [Fact]
    public void Chunks_DirectDictionaryAccess()
    {
        var grid = new VoxelGrid(new VoxelBounds(
            new VoxelCoord(0, 0, 0), new VoxelCoord(15, 15, 15)));
        Assert.Empty(grid.Chunks);

        var chunk = new VoxelChunk();
        chunk.SetVoxel(0, 0, 0, new Voxel(1, VoxelFlags.None));
        grid.Chunks[new ChunkCoord(0, 0, 0)] = chunk;

        Assert.Single(grid.Chunks);
        Assert.NotNull(grid.GetChunk(new ChunkCoord(0, 0, 0)));
    }

    #endregion
}
