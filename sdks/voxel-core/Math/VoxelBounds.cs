namespace BeyondImmersion.Bannou.VoxelCore.Math;

/// <summary>
/// Integer axis-aligned bounding box defined by minimum and maximum <see cref="VoxelCoord"/> (both inclusive).
/// </summary>
/// <param name="Min">Minimum corner (inclusive).</param>
/// <param name="Max">Maximum corner (inclusive).</param>
public readonly record struct VoxelBounds(VoxelCoord Min, VoxelCoord Max)
{
    /// <summary>Width along the X axis (inclusive).</summary>
    public int Width => Max.X - Min.X + 1;

    /// <summary>Height along the Y axis (inclusive).</summary>
    public int Height => Max.Y - Min.Y + 1;

    /// <summary>Depth along the Z axis (inclusive).</summary>
    public int Depth => Max.Z - Min.Z + 1;

    /// <summary>
    /// Total volume of the bounding box in voxels.
    /// </summary>
    public long Volume => (long)Width * Height * Depth;

    /// <summary>
    /// Tests whether a coordinate is within these bounds (inclusive on all sides).
    /// </summary>
    /// <param name="coord">The coordinate to test.</param>
    /// <returns>True if the coordinate is within bounds.</returns>
    public bool Contains(VoxelCoord coord) =>
        coord.X >= Min.X && coord.X <= Max.X &&
        coord.Y >= Min.Y && coord.Y <= Max.Y &&
        coord.Z >= Min.Z && coord.Z <= Max.Z;

    /// <summary>
    /// Tests whether another bounding box overlaps with this one.
    /// </summary>
    /// <param name="other">The other bounds to test.</param>
    /// <returns>True if the bounding boxes overlap.</returns>
    public bool Intersects(VoxelBounds other) =>
        Min.X <= other.Max.X && Max.X >= other.Min.X &&
        Min.Y <= other.Max.Y && Max.Y >= other.Min.Y &&
        Min.Z <= other.Max.Z && Max.Z >= other.Min.Z;

    /// <summary>
    /// Returns expanded bounds that include the given coordinate.
    /// </summary>
    /// <param name="coord">The coordinate to include.</param>
    /// <returns>A new bounds expanded to contain the coordinate.</returns>
    public VoxelBounds Expand(VoxelCoord coord) =>
        new(
            new VoxelCoord(
                System.Math.Min(Min.X, coord.X),
                System.Math.Min(Min.Y, coord.Y),
                System.Math.Min(Min.Z, coord.Z)),
            new VoxelCoord(
                System.Math.Max(Max.X, coord.X),
                System.Math.Max(Max.Y, coord.Y),
                System.Math.Max(Max.Z, coord.Z)));

    /// <inheritdoc />
    public override string ToString() => $"[{Min} .. {Max}]";
}
