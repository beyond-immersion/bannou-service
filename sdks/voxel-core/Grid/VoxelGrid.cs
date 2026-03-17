using BeyondImmersion.Bannou.VoxelCore.Math;

namespace BeyondImmersion.Bannou.VoxelCore.Grid;

/// <summary>
/// Sparse 3D voxel grid subdivided into 16x16x16 chunks. Primary data structure.
/// Only non-empty chunks are allocated in memory. Thread safety: concurrent read, single-writer.
/// </summary>
public sealed class VoxelGrid
{
    private readonly Dictionary<ChunkCoord, VoxelChunk> _chunks = new();

    /// <summary>Grid dimensions (may exceed populated area).</summary>
    public VoxelBounds Bounds { get; private set; }

    /// <summary>Shared 256-entry color/material palette.</summary>
    public Palette Palette { get; }

    /// <summary>Grid-level metadata (name, author, tags, voxel scale).</summary>
    public GridMetadata Metadata { get; }

    /// <summary>Cached count of non-empty voxels across all chunks.</summary>
    public int VoxelCount { get; internal set; }

    /// <summary>Number of allocated (non-empty) chunks.</summary>
    public int ChunkCount => _chunks.Count;

    /// <summary>
    /// Creates a new empty voxel grid with the specified bounds and palette.
    /// </summary>
    /// <param name="bounds">Grid dimensions.</param>
    /// <param name="palette">Shared palette. If null, a new empty palette is created.</param>
    /// <param name="metadata">Grid metadata. If null, default metadata is created.</param>
    public VoxelGrid(VoxelBounds bounds, Palette? palette = null, GridMetadata? metadata = null)
    {
        Bounds = bounds;
        Palette = palette ?? new Palette();
        Metadata = metadata ?? new GridMetadata();
    }

    /// <summary>
    /// Gets the voxel at the given coordinate. Returns <see cref="Voxel.Empty"/> for
    /// coordinates outside the bounds or in unpopulated chunks.
    /// </summary>
    /// <param name="coord">The voxel coordinate.</param>
    /// <returns>The voxel at that position, or <see cref="Voxel.Empty"/>.</returns>
    public Voxel GetVoxel(VoxelCoord coord)
    {
        if (!Bounds.Contains(coord))
            return Voxel.Empty;

        var chunkCoord = coord.ToChunkCoord();
        if (!_chunks.TryGetValue(chunkCoord, out var chunk))
            return Voxel.Empty;

        var (lx, ly, lz) = coord.ToLocalCoord();
        return chunk.GetVoxel(lx, ly, lz);
    }

    /// <summary>
    /// Sets the voxel at the given coordinate. Creates the chunk on first write.
    /// Removes the chunk if it becomes empty. Does NOT enforce the <see cref="VoxelFlags.Frozen"/> flag.
    /// </summary>
    /// <param name="coord">The voxel coordinate. Must be within bounds.</param>
    /// <param name="voxel">The voxel to write.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if coordinate is outside bounds.</exception>
    public void SetVoxel(VoxelCoord coord, Voxel voxel)
    {
        if (!Bounds.Contains(coord))
            throw new ArgumentOutOfRangeException(nameof(coord), $"Coordinate {coord} is outside grid bounds {Bounds}");

        var chunkCoord = coord.ToChunkCoord();

        if (!_chunks.TryGetValue(chunkCoord, out var chunk))
        {
            if (voxel.IsEmpty)
                return; // No-op: setting empty in empty chunk

            chunk = new VoxelChunk();
            _chunks[chunkCoord] = chunk;
        }

        var (lx, ly, lz) = coord.ToLocalCoord();
        var oldVoxel = chunk.GetVoxel(lx, ly, lz);
        chunk.SetVoxel(lx, ly, lz, voxel);

        // Update grid-level voxel count
        if (oldVoxel.IsEmpty && !voxel.IsEmpty)
            VoxelCount++;
        else if (!oldVoxel.IsEmpty && voxel.IsEmpty)
            VoxelCount--;

        // Remove chunk if it became empty
        if (chunk.IsEmpty)
            _chunks.Remove(chunkCoord);
    }

    /// <summary>
    /// Gets the chunk at the given chunk coordinate, or null if no chunk exists there.
    /// </summary>
    /// <param name="coord">The chunk coordinate.</param>
    /// <returns>The chunk, or null if empty/unallocated.</returns>
    public VoxelChunk? GetChunk(ChunkCoord coord) =>
        _chunks.TryGetValue(coord, out var chunk) ? chunk : null;

    /// <summary>
    /// Enumerates all non-empty chunks in the grid.
    /// </summary>
    /// <returns>Sequence of (ChunkCoord, VoxelChunk) pairs.</returns>
    public IEnumerable<(ChunkCoord Coord, VoxelChunk Chunk)> EnumerateChunks() =>
        _chunks.Select(kv => (kv.Key, kv.Value));

    /// <summary>
    /// Returns the set of chunk coordinates where the chunk has <see cref="VoxelChunk.IsDirty"/> = true.
    /// </summary>
    /// <returns>Set of dirty chunk coordinates.</returns>
    public IReadOnlySet<ChunkCoord> GetDirtyChunks()
    {
        var dirty = new HashSet<ChunkCoord>();
        foreach (var (coord, chunk) in _chunks)
        {
            if (chunk.IsDirty)
                dirty.Add(coord);
        }
        return dirty;
    }

    /// <summary>
    /// Resets <see cref="VoxelChunk.IsDirty"/> on all chunks.
    /// </summary>
    public void ClearDirtyFlags()
    {
        foreach (var chunk in _chunks.Values)
            chunk.IsDirty = false;
    }

    /// <summary>
    /// Whether the voxel at the given coordinate is empty (palette index 0).
    /// Returns true for coordinates outside bounds.
    /// </summary>
    /// <param name="coord">The voxel coordinate.</param>
    /// <returns>True if the voxel is empty or outside bounds.</returns>
    public bool IsEmpty(VoxelCoord coord) => GetVoxel(coord).IsEmpty;

    /// <summary>
    /// Whether the given coordinate is within the grid bounds.
    /// </summary>
    /// <param name="coord">The voxel coordinate.</param>
    /// <returns>True if within bounds.</returns>
    public bool Contains(VoxelCoord coord) => Bounds.Contains(coord);

    /// <summary>
    /// Provides direct access to the internal chunk dictionary for serialization.
    /// </summary>
    internal Dictionary<ChunkCoord, VoxelChunk> Chunks => _chunks;
}
