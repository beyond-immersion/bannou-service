using BeyondImmersion.Bannou.VoxelBuilder.Core;
using BeyondImmersion.Bannou.VoxelCore.Grid;
using BeyondImmersion.Bannou.VoxelCore.Math;
using BeyondImmersion.Bannou.VoxelCore.Serialization;

namespace BeyondImmersion.Bannou.VoxelBuilder.Operations;

/// <summary>
/// Applies a <see cref="VoxelDelta"/> as a single undoable operation. Before-state is
/// captured by serializing affected chunks before the delta is applied. Undo restores
/// the chunks from the serialized snapshots and recalculates the voxel count.
/// </summary>
public sealed class GridPatchOperation : IVoxelOperation
{
    /// <summary>The binary delta data to apply.</summary>
    public byte[] Delta { get; }

    /// <summary>
    /// Serialized before-state of affected chunks. Null values represent chunks that
    /// did not exist before the delta was applied.
    /// </summary>
    public Dictionary<ChunkCoord, byte[]?> BeforeChunks { get; }

    /// <inheritdoc />
    public string SourceId { get; set; }

    /// <inheritdoc />
    public string Description => $"Apply grid patch ({Delta.Length} bytes)";

    /// <inheritdoc />
    public VoxelOperationType OperationType => VoxelOperationType.GridPatch;

    /// <inheritdoc />
    public VoxelBounds AffectedRegion { get; private set; }

    /// <summary>
    /// Creates a new grid patch operation.
    /// </summary>
    /// <param name="delta">The binary delta data to apply.</param>
    /// <param name="sourceId">Who created this operation.</param>
    public GridPatchOperation(byte[] delta, string sourceId = "local")
    {
        Delta = delta;
        BeforeChunks = new Dictionary<ChunkCoord, byte[]?>();
        SourceId = sourceId;
    }

    /// <summary>
    /// Creates a grid patch operation with pre-computed before-state snapshots.
    /// Used by <see cref="Core.VoxelBuilder.DiffToOperation"/> on the sender side.
    /// </summary>
    /// <param name="delta">The binary delta data.</param>
    /// <param name="sourceId">Who created this operation.</param>
    /// <param name="beforeChunks">Pre-computed chunk snapshots for undo.</param>
    internal GridPatchOperation(byte[] delta, string sourceId, Dictionary<ChunkCoord, byte[]?> beforeChunks)
    {
        Delta = delta;
        BeforeChunks = beforeChunks;
        SourceId = sourceId;
    }

    /// <inheritdoc />
    public void Execute(VoxelGrid grid, VoxelBuilderOptions options)
    {
        // Snapshot affected chunks before applying the delta (only on first execution)
        if (BeforeChunks.Count == 0)
        {
            var affectedCoords = GetAffectedChunkCoords();
            foreach (var coord in affectedCoords)
            {
                var existingChunk = grid.GetChunk(coord);
                BeforeChunks[coord] = existingChunk != null
                    ? VoxelSerializer.SerializeChunk(existingChunk)
                    : null;
            }
        }

        VoxelDelta.Apply(grid, Delta);

        // Compute affected region from chunk coordinates
        ComputeAffectedRegion();
    }

    /// <inheritdoc />
    public void Undo(VoxelGrid grid)
    {
        // Restore each affected chunk directly via the internal Chunks dictionary — O(1) per chunk
        // instead of O(4096) per-voxel SetVoxel calls. Requires InternalsVisibleTo from voxel-core.
        foreach (var (chunkCoord, serializedChunk) in BeforeChunks)
        {
            if (serializedChunk == null)
            {
                // Chunk didn't exist before — remove it
                grid.Chunks.Remove(chunkCoord);
            }
            else
            {
                // Restore the pre-patch chunk
                var chunk = VoxelSerializer.DeserializeChunk(serializedChunk);
                grid.Chunks[chunkCoord] = chunk;
            }
        }

        // Remove any chunks that are now empty
        var emptyChunks = grid.Chunks
            .Where(kv => kv.Value.IsEmpty)
            .Select(kv => kv.Key)
            .ToList();
        foreach (var coord in emptyChunks)
            grid.Chunks.Remove(coord);

        // Recalculate VoxelCount from all chunks
        var totalVoxels = 0;
        foreach (var (_, chunk) in grid.EnumerateChunks())
            totalVoxels += chunk.NonEmptyCount;
        grid.VoxelCount = totalVoxels;
    }

    /// <summary>
    /// Parses the delta header to extract all affected chunk coordinates (added, removed, and modified).
    /// </summary>
    /// <returns>Set of affected chunk coordinates.</returns>
    private HashSet<ChunkCoord> GetAffectedChunkCoords()
    {
        var coords = new HashSet<ChunkCoord>();
        using var ms = new MemoryStream(Delta);
        using var reader = new BinaryReader(ms);

        // Added chunks
        var addedCount = reader.ReadUInt32();
        for (var i = 0; i < addedCount; i++)
        {
            var coord = new ChunkCoord(reader.ReadInt16(), reader.ReadInt16(), reader.ReadInt16());
            coords.Add(coord);
            var dataLength = reader.ReadUInt32();
            ms.Position += dataLength; // Skip chunk data
        }

        // Removed chunks
        var removedCount = reader.ReadUInt32();
        for (var i = 0; i < removedCount; i++)
        {
            var coord = new ChunkCoord(reader.ReadInt16(), reader.ReadInt16(), reader.ReadInt16());
            coords.Add(coord);
        }

        // Modified chunks
        var modifiedCount = reader.ReadUInt32();
        for (var i = 0; i < modifiedCount; i++)
        {
            var coord = new ChunkCoord(reader.ReadInt16(), reader.ReadInt16(), reader.ReadInt16());
            coords.Add(coord);
            var compressedLength = reader.ReadUInt32();
            ms.Position += compressedLength; // Skip compressed diff
        }

        return coords;
    }

    /// <summary>
    /// Computes the affected region bounding box from the before-chunk coordinates.
    /// </summary>
    private void ComputeAffectedRegion()
    {
        if (BeforeChunks.Count == 0)
        {
            AffectedRegion = new VoxelBounds(VoxelCoord.Zero, VoxelCoord.Zero);
            return;
        }

        var minX = int.MaxValue;
        var minY = int.MaxValue;
        var minZ = int.MaxValue;
        var maxX = int.MinValue;
        var maxY = int.MinValue;
        var maxZ = int.MinValue;

        foreach (var coord in BeforeChunks.Keys)
        {
            var voxelOrigin = coord.ToVoxelCoord();
            minX = Math.Min(minX, voxelOrigin.X);
            minY = Math.Min(minY, voxelOrigin.Y);
            minZ = Math.Min(minZ, voxelOrigin.Z);
            maxX = Math.Max(maxX, voxelOrigin.X + VoxelChunk.Size - 1);
            maxY = Math.Max(maxY, voxelOrigin.Y + VoxelChunk.Size - 1);
            maxZ = Math.Max(maxZ, voxelOrigin.Z + VoxelChunk.Size - 1);
        }

        AffectedRegion = new VoxelBounds(
            new VoxelCoord(minX, minY, minZ),
            new VoxelCoord(maxX, maxY, maxZ));
    }
}
