using BeyondImmersion.Bannou.VoxelCore.Grid;
using BeyondImmersion.Bannou.VoxelCore.Math;
using BeyondImmersion.Bannou.VoxelCore.Serialization;
using Xunit;

namespace BeyondImmersion.Bannou.VoxelCore.Tests.Serialization;

/// <summary>
/// Unit tests for <see cref="VoxelSerializer"/> — .bvox format roundtrip.
/// </summary>
public class VoxelSerializerTests
{
    [Fact]
    public void RoundTrip_EmptyGrid()
    {
        var bounds = new VoxelBounds(new VoxelCoord(0, 0, 0), new VoxelCoord(15, 15, 15));
        var grid = new VoxelGrid(bounds);

        var bytes = VoxelSerializer.Serialize(grid);
        var restored = VoxelSerializer.Deserialize(bytes);

        Assert.Equal(0, restored.VoxelCount);
        Assert.Equal(0, restored.ChunkCount);
    }

    [Fact]
    public void RoundTrip_SingleVoxel()
    {
        var bounds = new VoxelBounds(new VoxelCoord(0, 0, 0), new VoxelCoord(15, 15, 15));
        var grid = new VoxelGrid(bounds);
        grid.Palette.GetOrAddIndex(Color.White, MaterialType.Diffuse, 0.5f);
        grid.SetVoxel(new VoxelCoord(5, 5, 5), new Voxel(1, VoxelFlags.Emissive));

        var bytes = VoxelSerializer.Serialize(grid);
        var restored = VoxelSerializer.Deserialize(bytes);

        Assert.Equal(1, restored.VoxelCount);
        var voxel = restored.GetVoxel(new VoxelCoord(5, 5, 5));
        Assert.Equal(1, voxel.PaletteIndex);
        Assert.Equal(VoxelFlags.Emissive, voxel.Flags);
    }

    [Fact]
    public void RoundTrip_PreservesPalette()
    {
        var bounds = new VoxelBounds(new VoxelCoord(0, 0, 0), new VoxelCoord(15, 15, 15));
        var grid = new VoxelGrid(bounds);
        grid.Palette.Set(1, new PaletteEntry(new Color(200, 100, 50, 255), MaterialType.Metal, 0.3f));
        grid.Palette.Set(2, new PaletteEntry(new Color(10, 20, 30, 128), MaterialType.Glass, 0.9f));
        grid.SetVoxel(new VoxelCoord(0, 0, 0), new Voxel(1, VoxelFlags.None));
        grid.SetVoxel(new VoxelCoord(1, 0, 0), new Voxel(2, VoxelFlags.None));

        var bytes = VoxelSerializer.Serialize(grid);
        var restored = VoxelSerializer.Deserialize(bytes);

        var entry1 = restored.Palette.Get(1);
        Assert.Equal(200, entry1.Color.R);
        Assert.Equal(100, entry1.Color.G);
        Assert.Equal(50, entry1.Color.B);
        Assert.Equal(MaterialType.Metal, entry1.Material);
        Assert.Equal(0.3f, entry1.Roughness, 0.01f);

        var entry2 = restored.Palette.Get(2);
        Assert.Equal(MaterialType.Glass, entry2.Material);
    }

    [Fact]
    public void RoundTrip_PreservesMetadata()
    {
        var bounds = new VoxelBounds(new VoxelCoord(0, 0, 0), new VoxelCoord(15, 15, 15));
        var grid = new VoxelGrid(bounds, metadata: new GridMetadata
        {
            Name = "Test Grid",
            Author = "Unit Test",
            VoxelScale = 0.5f
        });

        var bytes = VoxelSerializer.Serialize(grid);
        var restored = VoxelSerializer.Deserialize(bytes);

        Assert.Equal("Test Grid", restored.Metadata.Name);
        Assert.Equal("Unit Test", restored.Metadata.Author);
        Assert.Equal(0.5f, restored.Metadata.VoxelScale);
    }

    [Fact]
    public void RoundTrip_MultipleChunks()
    {
        var bounds = new VoxelBounds(new VoxelCoord(0, 0, 0), new VoxelCoord(31, 31, 31));
        var grid = new VoxelGrid(bounds);
        grid.Palette.GetOrAddIndex(Color.White, MaterialType.Diffuse);

        // Place voxels in different chunks
        grid.SetVoxel(new VoxelCoord(0, 0, 0), new Voxel(1, VoxelFlags.None));    // Chunk (0,0,0)
        grid.SetVoxel(new VoxelCoord(16, 0, 0), new Voxel(1, VoxelFlags.None));   // Chunk (1,0,0)
        grid.SetVoxel(new VoxelCoord(0, 16, 0), new Voxel(1, VoxelFlags.Frozen)); // Chunk (0,1,0)

        var bytes = VoxelSerializer.Serialize(grid);
        var restored = VoxelSerializer.Deserialize(bytes);

        Assert.Equal(3, restored.VoxelCount);
        Assert.Equal(3, restored.ChunkCount);
        Assert.Equal(VoxelFlags.Frozen, restored.GetVoxel(new VoxelCoord(0, 16, 0)).Flags);
    }

    [Fact]
    public void RoundTrip_PreservesBounds()
    {
        var bounds = new VoxelBounds(new VoxelCoord(-10, -5, 0), new VoxelCoord(20, 30, 40));
        var grid = new VoxelGrid(bounds);

        var bytes = VoxelSerializer.Serialize(grid);
        var restored = VoxelSerializer.Deserialize(bytes);

        Assert.Equal(bounds, restored.Bounds);
    }

    [Fact]
    public void Serialize_Deterministic()
    {
        var bounds = new VoxelBounds(new VoxelCoord(0, 0, 0), new VoxelCoord(31, 31, 31));
        var grid = new VoxelGrid(bounds);
        grid.Palette.GetOrAddIndex(Color.White, MaterialType.Diffuse);
        grid.SetVoxel(new VoxelCoord(0, 0, 0), new Voxel(1, VoxelFlags.None));
        grid.SetVoxel(new VoxelCoord(16, 0, 0), new Voxel(1, VoxelFlags.None));

        var bytes1 = VoxelSerializer.Serialize(grid);
        var bytes2 = VoxelSerializer.Serialize(grid);

        Assert.Equal(bytes1, bytes2);
    }

    [Fact]
    public void Deserialize_InvalidMagic_Throws()
    {
        var data = new byte[] { (byte)'N', (byte)'O', (byte)'P', (byte)'E', 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
        Assert.Throws<FormatException>(() => VoxelSerializer.Deserialize(data));
    }

    [Fact]
    public void Deserialize_CorruptedChecksum_Throws()
    {
        var bounds = new VoxelBounds(new VoxelCoord(0, 0, 0), new VoxelCoord(15, 15, 15));
        var grid = new VoxelGrid(bounds);
        var bytes = VoxelSerializer.Serialize(grid);

        // Corrupt a byte in the payload area
        if (bytes.Length > 25)
            bytes[25] ^= 0xFF;

        Assert.Throws<FormatException>(() => VoxelSerializer.Deserialize(bytes));
    }

    [Fact]
    public void Serialize_CompressedSmallerThanRaw()
    {
        var bounds = new VoxelBounds(new VoxelCoord(0, 0, 0), new VoxelCoord(63, 63, 63));
        var grid = new VoxelGrid(bounds);
        grid.Palette.GetOrAddIndex(Color.White, MaterialType.Diffuse);

        // Fill a large region with same material (compresses well)
        for (var x = 0; x < 16; x++)
        for (var y = 0; y < 16; y++)
        for (var z = 0; z < 16; z++)
            grid.SetVoxel(new VoxelCoord(x, y, z), new Voxel(1, VoxelFlags.None));

        var bytes = VoxelSerializer.Serialize(grid);
        var rawSize = 4096 * 2; // 16^3 voxels, 2 bytes each

        Assert.True(bytes.Length < rawSize,
            $"Compressed size ({bytes.Length}) should be less than raw ({rawSize})");
    }

    [Fact]
    public void SerializeChunk_DeserializeChunk_RoundTrip()
    {
        var chunk = new VoxelChunk();
        chunk.SetVoxel(0, 0, 0, new Voxel(1, VoxelFlags.None));
        chunk.SetVoxel(15, 15, 15, new Voxel(255, VoxelFlags.Frozen | VoxelFlags.Emissive));

        var bytes = VoxelSerializer.SerializeChunk(chunk);
        var restored = VoxelSerializer.DeserializeChunk(bytes);

        Assert.Equal(chunk.GetVoxel(0, 0, 0), restored.GetVoxel(0, 0, 0));
        Assert.Equal(chunk.GetVoxel(15, 15, 15), restored.GetVoxel(15, 15, 15));
        Assert.Equal(chunk.NonEmptyCount, restored.NonEmptyCount);
    }
}
