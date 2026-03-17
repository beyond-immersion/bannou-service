using BeyondImmersion.Bannou.VoxelCore.Grid;

namespace BeyondImmersion.Bannou.VoxelCore.Meshing;

/// <summary>
/// Shared helper methods for mesher implementations.
/// </summary>
internal static class MesherHelpers
{
    /// <summary>Face direction offsets: +X, -X, +Y, -Y, +Z, -Z.</summary>
    internal static readonly (int dx, int dy, int dz)[] FaceDirections =
    {
        (1, 0, 0), (-1, 0, 0),
        (0, 1, 0), (0, -1, 0),
        (0, 0, 1), (0, 0, -1)
    };

    /// <summary>Face normal vectors as float triplets, matching FaceDirections order.</summary>
    internal static readonly (float nx, float ny, float nz)[] FaceNormals =
    {
        (1, 0, 0), (-1, 0, 0),
        (0, 1, 0), (0, -1, 0),
        (0, 0, 1), (0, 0, -1)
    };

    /// <summary>
    /// Quad vertex offsets for each face direction (4 vertices per face).
    /// Each quad is wound CCW (right-handed convention, front-facing).
    /// Vertices are in the order: bottom-left, bottom-right, top-right, top-left
    /// relative to the face plane.
    /// </summary>
    internal static readonly (float dx, float dy, float dz)[][] FaceVertices =
    {
        // +X face (right)
        new[] { (1f,0f,0f), (1f,0f,1f), (1f,1f,1f), (1f,1f,0f) },
        // -X face (left)
        new[] { (0f,0f,1f), (0f,0f,0f), (0f,1f,0f), (0f,1f,1f) },
        // +Y face (top)
        new[] { (0f,1f,0f), (1f,1f,0f), (1f,1f,1f), (0f,1f,1f) },
        // -Y face (bottom)
        new[] { (0f,0f,1f), (1f,0f,1f), (1f,0f,0f), (0f,0f,0f) },
        // +Z face (front)
        new[] { (0f,0f,1f), (0f,1f,1f), (1f,1f,1f), (1f,0f,1f) },
        // -Z face (back)
        new[] { (1f,0f,0f), (1f,1f,0f), (0f,1f,0f), (0f,0f,0f) }
    };

    /// <summary>
    /// Checks whether a neighbor voxel at the given local coords (which may be outside the chunk)
    /// is empty. Uses the appropriate neighbor chunk for boundary lookups.
    /// </summary>
    internal static bool IsNeighborEmpty(
        VoxelChunk chunk, VoxelChunk?[] neighbors,
        int x, int y, int z)
    {
        // Determine which chunk to look in
        if (x >= 0 && x < 16 && y >= 0 && y < 16 && z >= 0 && z < 16)
            return chunk.PaletteIndices[VoxelChunk.GetFlatIndex(x, y, z)] == 0;

        // Outside this chunk — look in the appropriate neighbor
        VoxelChunk? neighborChunk = null;
        var nx = x;
        var ny = y;
        var nz = z;

        if (x >= 16) { neighborChunk = neighbors[0]; nx = x - 16; }      // +X
        else if (x < 0) { neighborChunk = neighbors[1]; nx = x + 16; }   // -X
        else if (y >= 16) { neighborChunk = neighbors[2]; ny = y - 16; }  // +Y
        else if (y < 0) { neighborChunk = neighbors[3]; ny = y + 16; }    // -Y
        else if (z >= 16) { neighborChunk = neighbors[4]; nz = z - 16; }  // +Z
        else if (z < 0) { neighborChunk = neighbors[5]; nz = z + 16; }    // -Z

        if (neighborChunk == null) return true; // No neighbor = treat as empty
        return neighborChunk.PaletteIndices[VoxelChunk.GetFlatIndex(nx, ny, nz)] == 0;
    }

    /// <summary>
    /// Checks whether the voxel at the given coordinates is solid (non-empty).
    /// Handles cross-chunk boundary lookups via the neighbor array.
    /// </summary>
    internal static bool IsSolid(VoxelChunk chunk, VoxelChunk?[] neighbors, int x, int y, int z)
        => !IsNeighborEmpty(chunk, neighbors, x, y, z);

    /// <summary>
    /// Computes texel-centered UV coordinates for a palette index in a 16x16 atlas.
    /// The +0.5 centers the sample point within the texel, preventing color bleeding.
    /// </summary>
    internal static (float u, float v) ComputeUV(byte paletteIndex)
    {
        var u = (paletteIndex % 16 + 0.5f) / 16f;
        var v = (paletteIndex / 16 + 0.5f) / 16f;
        return (u, v);
    }
}
