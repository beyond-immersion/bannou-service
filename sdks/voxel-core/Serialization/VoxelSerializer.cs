using System.IO.Hashing;
using System.Text;
using System.Text.Json;
using BeyondImmersion.Bannou.VoxelCore.Grid;
using BeyondImmersion.Bannou.VoxelCore.Math;
using K4os.Compression.LZ4;

namespace BeyondImmersion.Bannou.VoxelCore.Serialization;

/// <summary>
/// Serializes and deserializes <see cref="VoxelGrid"/> to and from the .bvox binary format.
/// The format uses dual-stream RLE + LZ4 compression for compact storage.
/// Deterministic: same VoxelGrid always produces identical .bvox bytes.
/// </summary>
public static class VoxelSerializer
{
    private static readonly byte[] Magic = "BVOX"u8.ToArray();
    private const ushort CurrentVersion = 1;

    [Flags]
    private enum BvoxFlags : ushort
    {
        None = 0,
        Compressed = 1,
        HasMetadata = 2,
        HasPalette = 4
    }

    /// <summary>
    /// Serializes a VoxelGrid to the .bvox binary format.
    /// </summary>
    /// <param name="grid">The grid to serialize.</param>
    /// <returns>The .bvox binary data.</returns>
    public static byte[] Serialize(VoxelGrid grid)
    {
        using var buffer = new MemoryStream();
        using var writer = new BinaryWriter(buffer);

        // Header (20 bytes)
        writer.Write(Magic);
        writer.Write(CurrentVersion);
        var flags = BvoxFlags.Compressed | BvoxFlags.HasPalette | BvoxFlags.HasMetadata;
        writer.Write((ushort)flags);
        writer.Write((uint)grid.ChunkCount);
        writer.Write((uint)grid.VoxelCount);
        var checksumOffset = buffer.Position;
        writer.Write((uint)0); // Placeholder for checksum
        var payloadStart = buffer.Position;

        // Bounds section (24 bytes)
        writer.Write((short)grid.Bounds.Min.X);
        writer.Write((short)grid.Bounds.Min.Y);
        writer.Write((short)grid.Bounds.Min.Z);
        writer.Write((short)grid.Bounds.Max.X);
        writer.Write((short)grid.Bounds.Max.Y);
        writer.Write((short)grid.Bounds.Max.Z);

        // Palette section
        writer.Write((ushort)grid.Palette.UsedCount);
        for (var i = 1; i <= grid.Palette.UsedCount; i++)
        {
            var entry = grid.Palette.Get((byte)i);
            writer.Write(entry.Color.R);
            writer.Write(entry.Color.G);
            writer.Write(entry.Color.B);
            writer.Write(entry.Color.A);
            writer.Write((byte)entry.Material);
            writer.Write(entry.Roughness);
        }

        // Metadata section (JSON)
        var metadataJson = JsonSerializer.SerializeToUtf8Bytes(grid.Metadata);
        writer.Write((uint)metadataJson.Length);
        writer.Write(metadataJson);

        // Sort chunks deterministically for reproducible output
        var sortedChunks = grid.EnumerateChunks()
            .OrderBy(c => c.Coord)
            .ToList();

        // Compress chunks and build chunk table
        var chunkEntries = new List<(ChunkCoord coord, uint offset, ushort length, ushort nonEmptyCount)>();
        using var chunkDataBuffer = new MemoryStream();

        foreach (var (coord, chunk) in sortedChunks)
        {
            var rleIndices = VoxelCompression.RleEncode(chunk.PaletteIndices);
            var rleFlags = VoxelCompression.RleEncode(chunk.Flags);

            // Combine with length prefix so decoder knows where to split
            var combinedLength = 2 + rleIndices.Length + rleFlags.Length;
            var combined = new byte[combinedLength];
            combined[0] = (byte)(rleIndices.Length & 0xFF);
            combined[1] = (byte)((rleIndices.Length >> 8) & 0xFF);
            Buffer.BlockCopy(rleIndices, 0, combined, 2, rleIndices.Length);
            Buffer.BlockCopy(rleFlags, 0, combined, 2 + rleIndices.Length, rleFlags.Length);

            // LZ4 compress
            var maxCompressed = LZ4Codec.MaximumOutputSize(combined.Length);
            var compressed = new byte[maxCompressed];
            var compressedLength = LZ4Codec.Encode(combined, compressed, LZ4Level.L00_FAST);
            var finalCompressed = new byte[compressedLength];
            Buffer.BlockCopy(compressed, 0, finalCompressed, 0, compressedLength);

            var offset = (uint)chunkDataBuffer.Position;
            chunkDataBuffer.Write(finalCompressed);

            chunkEntries.Add((coord, offset, (ushort)compressedLength, (ushort)chunk.NonEmptyCount));
        }

        // Write chunk table (14 bytes per entry: 3x int16 coord + uint32 offset + uint16 length + uint16 nonEmptyCount)
        foreach (var (coord, offset, length, nonEmptyCount) in chunkEntries)
        {
            writer.Write((short)coord.X);
            writer.Write((short)coord.Y);
            writer.Write((short)coord.Z);
            writer.Write(offset);
            writer.Write(length);
            writer.Write(nonEmptyCount);
        }

        // Write chunk data
        chunkDataBuffer.Position = 0;
        chunkDataBuffer.CopyTo(buffer);

        // Compute checksum over all payload bytes
        var fullData = buffer.ToArray();
        var payloadSpan = fullData.AsSpan((int)payloadStart);
        var checksum = XxHash32.HashToUInt32(payloadSpan);

        // Write checksum into the placeholder position
        buffer.Position = checksumOffset;
        writer.Write(checksum);

        return buffer.ToArray();
    }

    /// <summary>
    /// Deserializes a VoxelGrid from .bvox binary data.
    /// </summary>
    /// <param name="data">The .bvox binary data.</param>
    /// <returns>The reconstructed VoxelGrid.</returns>
    /// <exception cref="FormatException">Thrown if the data is invalid or corrupted.</exception>
    public static VoxelGrid Deserialize(byte[] data)
    {
        using var ms = new MemoryStream(data);
        using var reader = new BinaryReader(ms);

        // Header (20 bytes)
        var magic = reader.ReadBytes(4);
        if (!magic.AsSpan().SequenceEqual(Magic))
            throw new FormatException("Invalid .bvox file: bad magic bytes");

        var version = reader.ReadUInt16();
        var flags = (BvoxFlags)reader.ReadUInt16();
        var chunkCount = reader.ReadUInt32();
        var voxelCount = reader.ReadUInt32();
        var storedChecksum = reader.ReadUInt32();

        // Verify checksum
        var payloadSpan = data.AsSpan(20);
        var computedChecksum = XxHash32.HashToUInt32(payloadSpan);
        if (storedChecksum != computedChecksum)
            throw new FormatException("Checksum mismatch: file may be corrupted");

        // Bounds
        var minX = reader.ReadInt16();
        var minY = reader.ReadInt16();
        var minZ = reader.ReadInt16();
        var maxX = reader.ReadInt16();
        var maxY = reader.ReadInt16();
        var maxZ = reader.ReadInt16();
        var bounds = new VoxelBounds(
            new VoxelCoord(minX, minY, minZ),
            new VoxelCoord(maxX, maxY, maxZ));

        // Palette
        var palette = new Palette();
        var entryCount = reader.ReadUInt16();
        for (var i = 1; i <= entryCount; i++)
        {
            var r = reader.ReadByte();
            var g = reader.ReadByte();
            var b = reader.ReadByte();
            var a = reader.ReadByte();
            var material = (MaterialType)reader.ReadByte();
            var roughness = reader.ReadSingle();
            palette.Set((byte)i, new PaletteEntry(new Color(r, g, b, a), material, roughness));
        }

        // Metadata
        var metaLength = reader.ReadUInt32();
        var metadataBytes = reader.ReadBytes((int)metaLength);
        var metadata = JsonSerializer.Deserialize<GridMetadata>(metadataBytes) ?? new GridMetadata();

        // Chunk table (14 bytes per entry)
        var chunkEntries = new (ChunkCoord coord, uint offset, ushort length, ushort nonEmptyCount)[chunkCount];
        for (var i = 0; i < chunkCount; i++)
        {
            var cx = reader.ReadInt16();
            var cy = reader.ReadInt16();
            var cz = reader.ReadInt16();
            var offset = reader.ReadUInt32();
            var length = reader.ReadUInt16();
            var nonEmptyCount = reader.ReadUInt16();
            chunkEntries[i] = (new ChunkCoord(cx, cy, cz), offset, length, nonEmptyCount);
        }

        // Record the start of chunk data section
        var chunkDataStart = ms.Position;

        var grid = new VoxelGrid(bounds, palette, metadata);

        // Chunk data
        foreach (var (coord, offset, length, _) in chunkEntries)
        {
            ms.Position = chunkDataStart + offset;
            var compressedData = reader.ReadBytes(length);

            // LZ4 decompress — try with increasing buffer sizes
            var decompressedSize = VoxelChunk.TotalVoxels * 2 + 256; // Generous initial estimate
            var decompressed = new byte[decompressedSize];
            var actualSize = LZ4Codec.Decode(compressedData, decompressed);
            if (actualSize < 0)
                throw new FormatException($"LZ4 decompression failed for chunk {coord}");

            // Split dual RLE streams
            var rleIndicesLength = decompressed[0] | (decompressed[1] << 8);
            var rleIndices = new byte[rleIndicesLength];
            Buffer.BlockCopy(decompressed, 2, rleIndices, 0, rleIndicesLength);
            var rleFlagsLength = actualSize - 2 - rleIndicesLength;
            var rleFlags = new byte[rleFlagsLength];
            Buffer.BlockCopy(decompressed, 2 + rleIndicesLength, rleFlags, 0, rleFlagsLength);

            var paletteIndices = VoxelCompression.RleDecode(rleIndices, VoxelChunk.TotalVoxels);
            var flagsData = VoxelCompression.RleDecode(rleFlags, VoxelChunk.TotalVoxels);

            var chunk = new VoxelChunk();
            Buffer.BlockCopy(paletteIndices, 0, chunk.PaletteIndices, 0, VoxelChunk.TotalVoxels);
            Buffer.BlockCopy(flagsData, 0, chunk.Flags, 0, VoxelChunk.TotalVoxels);
            chunk.RecalculateNonEmptyCount();

            grid.Chunks[coord] = chunk;
        }

        grid.VoxelCount = (int)voxelCount;
        return grid;
    }

    /// <summary>
    /// Serializes a single chunk for delta encoding.
    /// </summary>
    /// <param name="chunk">The chunk to serialize.</param>
    /// <returns>Compressed chunk data.</returns>
    public static byte[] SerializeChunk(VoxelChunk chunk)
    {
        var rleIndices = VoxelCompression.RleEncode(chunk.PaletteIndices);
        var rleFlags = VoxelCompression.RleEncode(chunk.Flags);

        var combinedLength = 2 + rleIndices.Length + rleFlags.Length;
        var combined = new byte[combinedLength];
        combined[0] = (byte)(rleIndices.Length & 0xFF);
        combined[1] = (byte)((rleIndices.Length >> 8) & 0xFF);
        Buffer.BlockCopy(rleIndices, 0, combined, 2, rleIndices.Length);
        Buffer.BlockCopy(rleFlags, 0, combined, 2 + rleIndices.Length, rleFlags.Length);

        var maxCompressed = LZ4Codec.MaximumOutputSize(combined.Length);
        var compressed = new byte[maxCompressed];
        var compressedLength = LZ4Codec.Encode(combined, compressed, LZ4Level.L00_FAST);
        var result = new byte[compressedLength];
        Buffer.BlockCopy(compressed, 0, result, 0, compressedLength);
        return result;
    }

    /// <summary>
    /// Deserializes a single chunk from delta encoding data.
    /// </summary>
    /// <param name="data">Compressed chunk data.</param>
    /// <returns>The reconstructed chunk.</returns>
    public static VoxelChunk DeserializeChunk(byte[] data)
    {
        var decompressedSize = VoxelChunk.TotalVoxels * 2 + 256;
        var decompressed = new byte[decompressedSize];
        var actualSize = LZ4Codec.Decode(data, decompressed);
        if (actualSize < 0)
            throw new FormatException("LZ4 decompression failed for chunk");

        var rleIndicesLength = decompressed[0] | (decompressed[1] << 8);
        var rleIndices = new byte[rleIndicesLength];
        Buffer.BlockCopy(decompressed, 2, rleIndices, 0, rleIndicesLength);
        var rleFlagsLength = actualSize - 2 - rleIndicesLength;
        var rleFlags = new byte[rleFlagsLength];
        Buffer.BlockCopy(decompressed, 2 + rleIndicesLength, rleFlags, 0, rleFlagsLength);

        var paletteIndices = VoxelCompression.RleDecode(rleIndices, VoxelChunk.TotalVoxels);
        var flagsData = VoxelCompression.RleDecode(rleFlags, VoxelChunk.TotalVoxels);

        var chunk = new VoxelChunk();
        Buffer.BlockCopy(paletteIndices, 0, chunk.PaletteIndices, 0, VoxelChunk.TotalVoxels);
        Buffer.BlockCopy(flagsData, 0, chunk.Flags, 0, VoxelChunk.TotalVoxels);
        chunk.RecalculateNonEmptyCount();
        return chunk;
    }
}
