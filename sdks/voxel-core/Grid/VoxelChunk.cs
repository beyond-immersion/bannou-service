namespace BeyondImmersion.Bannou.VoxelCore.Grid;

/// <summary>
/// Fixed 16x16x16 voxel volume. Stored as flat byte arrays in XZY memory order:
/// horizontal XZ slices are contiguous, optimizing the greedy mesher's most common
/// scan direction and column-wise terrain voxelization.
/// </summary>
public sealed class VoxelChunk
{
    /// <summary>Side length of a chunk in voxels.</summary>
    public const int Size = 16;

    /// <summary>Total number of voxels in a chunk (16^3 = 4096).</summary>
    public const int TotalVoxels = Size * Size * Size;

    internal readonly byte[] PaletteIndices = new byte[TotalVoxels];
    internal readonly byte[] Flags = new byte[TotalVoxels];

    /// <summary>
    /// Cached count of non-empty voxels in this chunk. Used for quick emptiness checks.
    /// </summary>
    public int NonEmptyCount { get; internal set; }

    /// <summary>
    /// Whether this chunk has been modified since the last serialization or dirty flag clear.
    /// </summary>
    public bool IsDirty { get; internal set; }

    /// <summary>
    /// Whether this chunk contains no non-empty voxels.
    /// </summary>
    public bool IsEmpty => NonEmptyCount == 0;

    /// <summary>
    /// Computes the flat array index from local chunk coordinates (0-15 each).
    /// XZY order: x + z * 16 + y * 256.
    /// </summary>
    /// <param name="x">Local X coordinate (0-15).</param>
    /// <param name="y">Local Y coordinate (0-15).</param>
    /// <param name="z">Local Z coordinate (0-15).</param>
    /// <returns>Flat array index (0-4095).</returns>
    public static int GetFlatIndex(int x, int y, int z) => x + z * Size + y * (Size * Size);

    /// <summary>
    /// Gets the voxel at local coordinates within this chunk.
    /// </summary>
    /// <param name="x">Local X coordinate (0-15).</param>
    /// <param name="y">Local Y coordinate (0-15).</param>
    /// <param name="z">Local Z coordinate (0-15).</param>
    /// <returns>The voxel at the specified position.</returns>
    public Voxel GetVoxel(int x, int y, int z)
    {
        var index = GetFlatIndex(x, y, z);
        return new Voxel(PaletteIndices[index], (VoxelFlags)Flags[index]);
    }

    /// <summary>
    /// Sets the voxel at local coordinates within this chunk. Updates
    /// <see cref="NonEmptyCount"/> and marks the chunk as dirty.
    /// </summary>
    /// <param name="x">Local X coordinate (0-15).</param>
    /// <param name="y">Local Y coordinate (0-15).</param>
    /// <param name="z">Local Z coordinate (0-15).</param>
    /// <param name="voxel">The voxel to write.</param>
    public void SetVoxel(int x, int y, int z, Voxel voxel)
    {
        var index = GetFlatIndex(x, y, z);
        var wasEmpty = PaletteIndices[index] == 0;
        PaletteIndices[index] = voxel.PaletteIndex;
        Flags[index] = (byte)voxel.Flags;
        var isNowEmpty = voxel.PaletteIndex == 0;

        if (wasEmpty && !isNowEmpty)
            NonEmptyCount++;
        else if (!wasEmpty && isNowEmpty)
            NonEmptyCount--;

        IsDirty = true;
    }

    /// <summary>
    /// Recalculates <see cref="NonEmptyCount"/> from the palette index array.
    /// Used after bulk writes (deserialization, delta application).
    /// </summary>
    internal void RecalculateNonEmptyCount()
    {
        var count = 0;
        for (var i = 0; i < TotalVoxels; i++)
        {
            if (PaletteIndices[i] != 0)
                count++;
        }
        NonEmptyCount = count;
    }
}
