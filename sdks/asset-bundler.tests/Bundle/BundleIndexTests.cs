using BeyondImmersion.Bannou.Bundle.Format;
using Xunit;

namespace BeyondImmersion.Bannou.AssetBundler.Tests.Bundle;

/// <summary>
/// Tests for BundleIndex binary serialization (BNIX format).
/// </summary>
public class BundleIndexTests
{
    private static byte[] CreateTestHash(byte fill = 0x00)
    {
        var hash = new byte[24];
        Array.Fill(hash, fill);
        return hash;
    }

    [Fact]
    public void RoundTrip_SingleEntry_PreservesData()
    {
        // Arrange
        var entries = new List<BundleIndexEntry>
        {
            new()
            {
                Offset = 1024,
                CompressedSize = 512,
                UncompressedSize = 1024,
                ContentHashPrefix = CreateTestHash(0xAB)
            }
        };
        var index = new BundleIndex { Entries = entries };

        // Act
        using var stream = new MemoryStream();
        index.WriteTo(stream);
        stream.Position = 0;

        var readIndex = BundleIndex.ReadFrom(stream);

        // Assert
        Assert.Single(readIndex.Entries);
        var entry = readIndex.Entries[0];
        Assert.Equal(1024, entry.Offset);
        Assert.Equal(512, entry.CompressedSize);
        Assert.Equal(1024, entry.UncompressedSize);
        Assert.Equal(CreateTestHash(0xAB), entry.ContentHashPrefix);
    }

    [Fact]
    public void RoundTrip_MultipleEntries_PreservesOrder()
    {
        // Arrange
        var entries = new List<BundleIndexEntry>();
        for (int i = 0; i < 100; i++)
        {
            entries.Add(new BundleIndexEntry
            {
                Offset = i * 1000,
                CompressedSize = 500 + i,
                UncompressedSize = 1000 + i,
                ContentHashPrefix = CreateTestHash((byte)i)
            });
        }
        var index = new BundleIndex { Entries = entries };

        // Act
        using var stream = new MemoryStream();
        index.WriteTo(stream);
        stream.Position = 0;

        var readIndex = BundleIndex.ReadFrom(stream);

        // Assert
        Assert.Equal(100, readIndex.Entries.Count);
        for (int i = 0; i < 100; i++)
        {
            Assert.Equal(i * 1000, readIndex.Entries[i].Offset);
            Assert.Equal(500 + i, readIndex.Entries[i].CompressedSize);
            Assert.Equal(1000 + i, readIndex.Entries[i].UncompressedSize);
        }
    }

    [Fact]
    public void WriteTo_ProducesBnixMagicHeader()
    {
        // Arrange
        var entries = new List<BundleIndexEntry>
        {
            new()
            {
                Offset = 0,
                CompressedSize = 0,
                UncompressedSize = 0,
                ContentHashPrefix = CreateTestHash()
            }
        };
        var index = new BundleIndex { Entries = entries };

        // Act
        using var stream = new MemoryStream();
        index.WriteTo(stream);

        // Assert - Check magic bytes "BNIX"
        stream.Position = 0;
        var magic = new byte[4];
        stream.Read(magic, 0, 4);

        Assert.Equal((byte)'B', magic[0]);
        Assert.Equal((byte)'N', magic[1]);
        Assert.Equal((byte)'I', magic[2]);
        Assert.Equal((byte)'X', magic[3]);
    }

    [Fact]
    public void ReadFrom_InvalidMagic_ThrowsException()
    {
        // Arrange
        using var stream = new MemoryStream();
        stream.Write("FAKE"u8);
        stream.Write(BitConverter.GetBytes(0)); // entry count (not big endian but close enough for test)
        stream.Position = 0;

        // Act & Assert
        Assert.Throws<InvalidDataException>(() => BundleIndex.ReadFrom(stream));
    }

    [Fact]
    public void RoundTrip_EmptyIndex_Works()
    {
        // Arrange
        var index = new BundleIndex { Entries = Array.Empty<BundleIndexEntry>() };

        // Act
        using var stream = new MemoryStream();
        index.WriteTo(stream);
        stream.Position = 0;

        var readIndex = BundleIndex.ReadFrom(stream);

        // Assert
        Assert.Empty(readIndex.Entries);
    }

    [Fact]
    public void RoundTrip_LargeOffsets_PreservesValues()
    {
        // Arrange - Use values that exceed int32 range
        var entries = new List<BundleIndexEntry>
        {
            new()
            {
                Offset = 5_000_000_000L, // 5GB offset
                CompressedSize = 1_000_000_000L, // 1GB compressed
                UncompressedSize = 3_000_000_000L, // 3GB uncompressed
                ContentHashPrefix = CreateTestHash()
            }
        };
        var index = new BundleIndex { Entries = entries };

        // Act
        using var stream = new MemoryStream();
        index.WriteTo(stream);
        stream.Position = 0;

        var readIndex = BundleIndex.ReadFrom(stream);

        // Assert
        var entry = readIndex.Entries[0];
        Assert.Equal(5_000_000_000L, entry.Offset);
        Assert.Equal(1_000_000_000L, entry.CompressedSize);
        Assert.Equal(3_000_000_000L, entry.UncompressedSize);
    }

    [Fact]
    public void GetEntry_ValidIndex_ReturnsEntry()
    {
        // Arrange
        var entries = new List<BundleIndexEntry>
        {
            new() { Offset = 0, CompressedSize = 100, UncompressedSize = 200, ContentHashPrefix = CreateTestHash(0x01) },
            new() { Offset = 100, CompressedSize = 150, UncompressedSize = 300, ContentHashPrefix = CreateTestHash(0x02) },
            new() { Offset = 250, CompressedSize = 200, UncompressedSize = 400, ContentHashPrefix = CreateTestHash(0x03) }
        };
        var index = new BundleIndex { Entries = entries };

        // Act
        var entry0 = index.GetEntry(0);
        var entry1 = index.GetEntry(1);
        var entry2 = index.GetEntry(2);

        // Assert
        Assert.Equal(0, entry0.Offset);
        Assert.Equal(100, entry1.Offset);
        Assert.Equal(250, entry2.Offset);
    }

    [Fact]
    public void GetEntry_InvalidIndex_ThrowsArgumentOutOfRange()
    {
        // Arrange
        var entries = new List<BundleIndexEntry>
        {
            new() { Offset = 0, CompressedSize = 100, UncompressedSize = 200, ContentHashPrefix = CreateTestHash() }
        };
        var index = new BundleIndex { Entries = entries };

        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => index.GetEntry(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => index.GetEntry(1));
    }

    [Fact]
    public void EntrySize_Is48Bytes()
    {
        Assert.Equal(48, BundleIndex.EntrySize);
    }
}
