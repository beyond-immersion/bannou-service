using SdkColor = BeyondImmersion.Bannou.VoxelCore.Grid.Color;
using StrideColor = Stride.Core.Mathematics.Color;
using StrideVec3 = Stride.Core.Mathematics.Vector3;
using VoxelCoord = BeyondImmersion.Bannou.VoxelCore.Math.VoxelCoord;
using ChunkCoord = BeyondImmersion.Bannou.VoxelCore.Math.ChunkCoord;

namespace BeyondImmersion.Bannou.VoxelBuilder.Stride;

/// <summary>
/// Conversion utilities between voxel SDK types and Stride types.
/// Color conversion is zero-copy: both SDK Color and Stride Color are 4 bytes RGBA.
/// Follows the using-alias pattern established by scene-composer-stride.
/// </summary>
public static class StrideTypeConverter
{
    /// <summary>
    /// Convert SDK Color to Stride Color. Zero-copy: both are 4-byte RGBA structs.
    /// </summary>
    public static StrideColor ToStride(this SdkColor c) => new(c.R, c.G, c.B, c.A);

    /// <summary>
    /// Convert Stride Color to SDK Color. Zero-copy: both are 4-byte RGBA structs.
    /// </summary>
    public static SdkColor ToSdk(this StrideColor c) => new(c.R, c.G, c.B, c.A);

    /// <summary>
    /// Convert SDK VoxelCoord to Stride Vector3 with voxel scale applied.
    /// </summary>
    /// <param name="c">The voxel coordinate.</param>
    /// <param name="scale">World units per voxel.</param>
    public static StrideVec3 ToStride(this VoxelCoord c, float scale) =>
        new(c.X * scale, c.Y * scale, c.Z * scale);

    /// <summary>
    /// Convert SDK ChunkCoord to Stride Vector3 world position.
    /// Each chunk is 16 voxels on a side, scaled by voxelScale.
    /// </summary>
    /// <param name="c">The chunk coordinate.</param>
    /// <param name="voxelScale">World units per voxel.</param>
    public static StrideVec3 ToWorldPosition(this ChunkCoord c, float voxelScale) =>
        new(c.X * 16 * voxelScale, c.Y * 16 * voxelScale, c.Z * 16 * voxelScale);
}
