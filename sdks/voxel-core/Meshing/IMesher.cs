using BeyondImmersion.Bannou.VoxelCore.Grid;

namespace BeyondImmersion.Bannou.VoxelCore.Meshing;

/// <summary>
/// Meshing algorithm contract. Converts a <see cref="VoxelChunk"/> to <see cref="MeshData"/>.
/// The 6 neighbor chunks are required for correct face culling and ambient occlusion
/// computation at chunk boundaries.
/// </summary>
public interface IMesher
{
    /// <summary>
    /// Generates mesh data from a voxel chunk and its 6 axis-aligned neighbor chunks.
    /// </summary>
    /// <param name="chunk">The chunk to mesh.</param>
    /// <param name="neighbors">
    /// The 6 neighbor chunks in order: +X, -X, +Y, -Y, +Z, -Z. Null entries indicate empty neighbors.
    /// </param>
    /// <param name="palette">The palette to look up colors and materials.</param>
    /// <param name="options">Meshing options (scale, AO, collision mode).</param>
    /// <returns>Engine-agnostic mesh data.</returns>
    MeshData Mesh(VoxelChunk chunk, VoxelChunk?[] neighbors, Palette palette, MeshingOptions options);
}
