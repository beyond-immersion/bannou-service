using BeyondImmersion.Bannou.VoxelCore.Grid;
using BeyondImmersion.Bannou.VoxelCore.Math;
using K4os.Compression.LZ4;

namespace BeyondImmersion.Bannou.VoxelCore.Serialization;

/// <summary>
/// Binary delta encoding between two <see cref="VoxelGrid"/> states at the chunk level.
/// Identifies added, removed, and modified chunks. Modified chunks are diffed using XOR
/// on their flat arrays. Deterministic: same (old, new) pair always produces identical delta bytes.
/// </summary>
public static class VoxelDelta
{
    /// <summary>
    /// Computes a binary delta between two grid states. Uses dirty flags on the new grid
    /// to identify modified chunks efficiently.
    /// </summary>
    /// <param name="oldGrid">The original grid state.</param>
    /// <param name="newGrid">The modified grid state.</param>
    /// <returns>Binary delta data that can be applied to the old grid to produce the new grid.</returns>
    public static byte[] Compute(VoxelGrid oldGrid, VoxelGrid newGrid)
    {
        var oldChunks = new HashSet<ChunkCoord>(oldGrid.EnumerateChunks().Select(c => c.Coord));
        var newChunks = new HashSet<ChunkCoord>(newGrid.EnumerateChunks().Select(c => c.Coord));
        var dirtyChunks = newGrid.GetDirtyChunks();

        // Added: in new but not in old
        var addedCoords = newChunks.Except(oldChunks).OrderBy(c => c).ToList();
        // Removed: in old but not in new
        var removedCoords = oldChunks.Except(newChunks).OrderBy(c => c).ToList();
        // Modified: in both AND dirty in new
        var modifiedCoords = dirtyChunks.Intersect(oldChunks).OrderBy(c => c).ToList();

        using var buffer = new MemoryStream();
        using var writer = new BinaryWriter(buffer);

        // Added chunks (full data)
        writer.Write((uint)addedCoords.Count);
        foreach (var coord in addedCoords)
        {
            writer.Write((short)coord.X);
            writer.Write((short)coord.Y);
            writer.Write((short)coord.Z);
            var chunkData = VoxelSerializer.SerializeChunk(newGrid.GetChunk(coord)
                ?? throw new InvalidOperationException($"Missing chunk at {coord}"));
            writer.Write((uint)chunkData.Length);
            writer.Write(chunkData);
        }

        // Removed chunks (coords only)
        writer.Write((uint)removedCoords.Count);
        foreach (var coord in removedCoords)
        {
            writer.Write((short)coord.X);
            writer.Write((short)coord.Y);
            writer.Write((short)coord.Z);
        }

        // Modified chunks (XOR diff)
        writer.Write((uint)modifiedCoords.Count);
        foreach (var coord in modifiedCoords)
        {
            writer.Write((short)coord.X);
            writer.Write((short)coord.Y);
            writer.Write((short)coord.Z);

            var oldChunk = oldGrid.GetChunk(coord)
                ?? throw new InvalidOperationException($"Missing old chunk at {coord}");
            var newChunk = newGrid.GetChunk(coord)
                ?? throw new InvalidOperationException($"Missing new chunk at {coord}");

            // XOR diff of both flat arrays
            var diff = new byte[VoxelChunk.TotalVoxels * 2];
            for (var i = 0; i < VoxelChunk.TotalVoxels; i++)
            {
                diff[i] = (byte)(oldChunk.PaletteIndices[i] ^ newChunk.PaletteIndices[i]);
                diff[VoxelChunk.TotalVoxels + i] = (byte)(oldChunk.Flags[i] ^ newChunk.Flags[i]);
            }

            // RLE + LZ4 compress the diff (same pipeline as full serializer)
            var rleData = VoxelCompression.RleEncode(diff);
            var maxCompressed = LZ4Codec.MaximumOutputSize(rleData.Length);
            var compressed = new byte[maxCompressed];
            var compressedLength = LZ4Codec.Encode(rleData, compressed, LZ4Level.L00_FAST);
            writer.Write((uint)compressedLength);
            writer.Write(compressed, 0, compressedLength);
        }

        return buffer.ToArray();
    }

    /// <summary>
    /// Applies a binary delta to a grid in-place, modifying it to reflect the new state.
    /// </summary>
    /// <param name="grid">The grid to patch. Modified in-place.</param>
    /// <param name="delta">The binary delta data from <see cref="Compute"/>.</param>
    public static void Apply(VoxelGrid grid, byte[] delta)
    {
        using var ms = new MemoryStream(delta);
        using var reader = new BinaryReader(ms);

        // Apply added chunks
        var addedCount = reader.ReadUInt32();
        for (var i = 0; i < addedCount; i++)
        {
            var coord = new ChunkCoord(reader.ReadInt16(), reader.ReadInt16(), reader.ReadInt16());
            var dataLength = reader.ReadUInt32();
            var chunkData = reader.ReadBytes((int)dataLength);
            var chunk = VoxelSerializer.DeserializeChunk(chunkData);
            grid.Chunks[coord] = chunk;
        }

        // Apply removed chunks
        var removedCount = reader.ReadUInt32();
        for (var i = 0; i < removedCount; i++)
        {
            var coord = new ChunkCoord(reader.ReadInt16(), reader.ReadInt16(), reader.ReadInt16());
            grid.Chunks.Remove(coord);
        }

        // Apply modified chunks
        var modifiedCount = reader.ReadUInt32();
        for (var i = 0; i < modifiedCount; i++)
        {
            var coord = new ChunkCoord(reader.ReadInt16(), reader.ReadInt16(), reader.ReadInt16());
            var compressedLength = reader.ReadUInt32();
            var compressedData = reader.ReadBytes((int)compressedLength);
            // LZ4 decompress, then RLE decode (same pipeline as full serializer)
            var decompressedSize = VoxelChunk.TotalVoxels * 4 + 256; // Generous estimate for RLE output
            var decompressed = new byte[decompressedSize];
            var actualSize = LZ4Codec.Decode(compressedData, decompressed);
            if (actualSize < 0)
                throw new FormatException($"LZ4 decompression failed for modified chunk {coord}");
            var rleData = decompressed.AsSpan(0, actualSize).ToArray();
            var diff = VoxelCompression.RleDecode(rleData, VoxelChunk.TotalVoxels * 2);

            var chunk = grid.GetChunk(coord);
            if (chunk == null)
            {
                chunk = new VoxelChunk();
                grid.Chunks[coord] = chunk;
            }

            // Apply XOR diff
            for (var j = 0; j < VoxelChunk.TotalVoxels; j++)
            {
                chunk.PaletteIndices[j] ^= diff[j];
                chunk.Flags[j] ^= diff[VoxelChunk.TotalVoxels + j];
            }
            chunk.RecalculateNonEmptyCount();
            chunk.IsDirty = true;
        }

        // Recalculate VoxelCount
        var totalVoxels = 0;
        foreach (var (_, chunk) in grid.EnumerateChunks())
            totalVoxels += chunk.NonEmptyCount;
        grid.VoxelCount = totalVoxels;

        // Remove empty chunks
        var emptyChunks = grid.EnumerateChunks()
            .Where(c => c.Chunk.IsEmpty)
            .Select(c => c.Coord)
            .ToList();
        foreach (var coord in emptyChunks)
            grid.Chunks.Remove(coord);
    }
}
