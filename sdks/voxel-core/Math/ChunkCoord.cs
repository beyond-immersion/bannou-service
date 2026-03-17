namespace BeyondImmersion.Bannou.VoxelCore.Math;

/// <summary>
/// Chunk-level coordinate. Each chunk is 16x16x16 voxels. Derived from
/// <see cref="VoxelCoord"/> by floor-division by 16.
/// </summary>
/// <param name="X">Chunk X position (voxel X &gt;&gt; 4).</param>
/// <param name="Y">Chunk Y position (voxel Y &gt;&gt; 4).</param>
/// <param name="Z">Chunk Z position (voxel Z &gt;&gt; 4).</param>
public readonly record struct ChunkCoord(int X, int Y, int Z) : IComparable<ChunkCoord>
{
    /// <summary>The origin chunk (0, 0, 0).</summary>
    public static readonly ChunkCoord Zero = new(0, 0, 0);

    /// <summary>
    /// Returns the voxel coordinate of this chunk's minimum corner (lowest X, Y, Z).
    /// </summary>
    public VoxelCoord ToVoxelCoord() => new(X * 16, Y * 16, Z * 16);

    /// <summary>
    /// Comparison for deterministic ordering: Y first, then Z, then X.
    /// Matches <see cref="VoxelCoord"/> ordering for consistent chunk iteration.
    /// </summary>
    public int CompareTo(ChunkCoord other)
    {
        var cmp = Y.CompareTo(other.Y);
        if (cmp != 0) return cmp;
        cmp = Z.CompareTo(other.Z);
        if (cmp != 0) return cmp;
        return X.CompareTo(other.X);
    }

    /// <inheritdoc />
    public override string ToString() => $"Chunk({X}, {Y}, {Z})";
}
