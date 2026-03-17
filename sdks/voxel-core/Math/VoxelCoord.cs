namespace BeyondImmersion.Bannou.VoxelCore.Math;

/// <summary>
/// Integer 3D voxel coordinate. Uses floor-division for correct negative coordinate handling.
/// </summary>
/// <param name="X">Voxel X position.</param>
/// <param name="Y">Voxel Y position.</param>
/// <param name="Z">Voxel Z position.</param>
public readonly record struct VoxelCoord(int X, int Y, int Z) : IComparable<VoxelCoord>
{
    /// <summary>The origin coordinate (0, 0, 0).</summary>
    public static readonly VoxelCoord Zero = new(0, 0, 0);

    /// <summary>
    /// Converts this voxel coordinate to a chunk coordinate using floor-division by 16.
    /// Arithmetic right shift (<c>&gt;&gt; 4</c>) correctly floors for negative values in C#.
    /// </summary>
    /// <returns>The chunk coordinate containing this voxel.</returns>
    public ChunkCoord ToChunkCoord() => new(X >> 4, Y >> 4, Z >> 4);

    /// <summary>
    /// Converts this voxel coordinate to a local coordinate (0-15) within its chunk.
    /// Uses floor-modulo to always produce 0-15 even for negative coordinates.
    /// </summary>
    /// <returns>Local (x, y, z) each in range [0, 15].</returns>
    public (int X, int Y, int Z) ToLocalCoord() =>
        (((X % 16) + 16) % 16, ((Y % 16) + 16) % 16, ((Z % 16) + 16) % 16);

    /// <summary>Component-wise addition.</summary>
    public static VoxelCoord operator +(VoxelCoord a, VoxelCoord b) =>
        new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

    /// <summary>Component-wise subtraction.</summary>
    public static VoxelCoord operator -(VoxelCoord a, VoxelCoord b) =>
        new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

    /// <summary>
    /// Euclidean distance to another coordinate.
    /// </summary>
    /// <param name="other">The other coordinate.</param>
    /// <returns>The Euclidean distance as a float.</returns>
    public float Distance(VoxelCoord other)
    {
        var dx = (double)(X - other.X);
        var dy = (double)(Y - other.Y);
        var dz = (double)(Z - other.Z);
        return (float)System.Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    /// <summary>
    /// Manhattan (taxi-cab) distance to another coordinate.
    /// </summary>
    /// <param name="other">The other coordinate.</param>
    /// <returns>The Manhattan distance.</returns>
    public int ManhattanDistance(VoxelCoord other) =>
        System.Math.Abs(X - other.X) + System.Math.Abs(Y - other.Y) + System.Math.Abs(Z - other.Z);

    /// <summary>
    /// Comparison for deterministic ordering: Y first, then Z, then X.
    /// </summary>
    public int CompareTo(VoxelCoord other)
    {
        var cmp = Y.CompareTo(other.Y);
        if (cmp != 0) return cmp;
        cmp = Z.CompareTo(other.Z);
        if (cmp != 0) return cmp;
        return X.CompareTo(other.X);
    }

    /// <inheritdoc />
    public override string ToString() => $"({X}, {Y}, {Z})";
}
