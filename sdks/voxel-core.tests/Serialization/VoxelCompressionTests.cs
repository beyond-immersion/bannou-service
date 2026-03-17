using BeyondImmersion.Bannou.VoxelCore.Serialization;
using Xunit;

namespace BeyondImmersion.Bannou.VoxelCore.Tests.Serialization;

/// <summary>
/// Unit tests for <see cref="VoxelCompression"/> — RLE encode/decode.
/// </summary>
public class VoxelCompressionTests
{
    [Fact]
    public void RoundTrip_EmptyArray()
    {
        var data = Array.Empty<byte>();
        var encoded = VoxelCompression.RleEncode(data);
        var decoded = VoxelCompression.RleDecode(encoded);
        Assert.Empty(decoded);
    }

    [Fact]
    public void RoundTrip_SingleByte()
    {
        var data = new byte[] { 42 };
        var encoded = VoxelCompression.RleEncode(data);
        var decoded = VoxelCompression.RleDecode(encoded);
        Assert.Equal(data, decoded);
    }

    [Fact]
    public void RoundTrip_AllZeros()
    {
        var data = new byte[4096]; // All zeros
        var encoded = VoxelCompression.RleEncode(data);
        var decoded = VoxelCompression.RleDecode(encoded, 4096);
        Assert.Equal(data, decoded);
    }

    [Fact]
    public void AllZeros_CompressesWell()
    {
        var data = new byte[4096];
        var encoded = VoxelCompression.RleEncode(data);
        // 4096 zeros → at most ceil(4096/255) * 2 = 34 bytes
        Assert.True(encoded.Length < 40, $"Expected < 40 bytes, got {encoded.Length}");
    }

    [Fact]
    public void RoundTrip_MixedData()
    {
        var data = new byte[4096];
        data[0] = 1;
        data[1] = 1;
        data[2] = 2;
        data[100] = 255;
        data[4095] = 128;

        var encoded = VoxelCompression.RleEncode(data);
        var decoded = VoxelCompression.RleDecode(encoded, 4096);
        Assert.Equal(data, decoded);
    }

    [Fact]
    public void RoundTrip_AllDifferent()
    {
        // Worst case for RLE: no two consecutive bytes are the same
        var data = new byte[256];
        for (var i = 0; i < 256; i++)
            data[i] = (byte)i;

        var encoded = VoxelCompression.RleEncode(data);
        var decoded = VoxelCompression.RleDecode(encoded);
        Assert.Equal(data, decoded);
    }

    [Fact]
    public void RoundTrip_LongRunExceeding255()
    {
        // A run of 300 same values should be split into multiple pairs
        var data = new byte[300];
        Array.Fill(data, (byte)42);

        var encoded = VoxelCompression.RleEncode(data);
        var decoded = VoxelCompression.RleDecode(encoded);
        Assert.Equal(data, decoded);
    }

    [Fact]
    public void RleDecode_WrongLength_Throws()
    {
        var data = new byte[] { 3, 42 }; // 3 x 42
        Assert.Throws<FormatException>(() =>
            VoxelCompression.RleDecode(data, 100)); // Expected 100 but only 3
    }

    [Fact]
    public void RleDecode_TruncatedData_Throws()
    {
        var data = new byte[] { 3 }; // Missing value byte
        Assert.Throws<FormatException>(() => VoxelCompression.RleDecode(data));
    }

    [Fact]
    public void RoundTrip_ChunkSizedData()
    {
        // Simulate typical voxel chunk data
        var data = new byte[4096];
        var rng = new Random(42);

        // Sparse: mostly zeros with some material patches
        for (var i = 0; i < 100; i++)
            data[rng.Next(4096)] = (byte)rng.Next(1, 10);

        var encoded = VoxelCompression.RleEncode(data);
        var decoded = VoxelCompression.RleDecode(encoded, 4096);
        Assert.Equal(data, decoded);
    }

    [Fact]
    public void Encode_Format_IsCountValuePairs()
    {
        var data = new byte[] { 5, 5, 5, 10, 10 };
        var encoded = VoxelCompression.RleEncode(data);

        // Expected: [3, 5, 2, 10]
        Assert.Equal(4, encoded.Length);
        Assert.Equal(3, encoded[0]);  // count
        Assert.Equal(5, encoded[1]);  // value
        Assert.Equal(2, encoded[2]);  // count
        Assert.Equal(10, encoded[3]); // value
    }
}
