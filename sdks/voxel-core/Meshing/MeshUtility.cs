using BeyondImmersion.Bannou.VoxelCore.Math;

namespace BeyondImmersion.Bannou.VoxelCore.Meshing;

/// <summary>
/// Post-processing utilities for mesh data: winding order flipping for left-handed engines
/// and multi-chunk mesh merging.
/// </summary>
public static class MeshUtility
{
    /// <summary>
    /// Flips the winding order of a mesh from CCW (right-handed) to CW (left-handed).
    /// Swaps each triangle's second and third indices: (A, B, C) → (A, C, B) and negates all normals.
    /// Use for Unity and Unreal engine bridges.
    /// </summary>
    /// <param name="source">The right-handed mesh data to flip.</param>
    /// <returns>A new MeshData with reversed winding order and negated normals.</returns>
    public static MeshData FlipWindingOrder(MeshData source)
    {
        var flippedIndices = new int[source.Indices.Length];
        for (var i = 0; i < source.Indices.Length; i += 3)
        {
            flippedIndices[i] = source.Indices[i];         // A stays
            flippedIndices[i + 1] = source.Indices[i + 2]; // C moves to position 2
            flippedIndices[i + 2] = source.Indices[i + 1]; // B moves to position 3
        }

        var flippedNormals = new float[source.Normals.Length];
        for (var i = 0; i < source.Normals.Length; i++)
        {
            flippedNormals[i] = -source.Normals[i];
        }

        return new MeshData(
            source.Vertices,
            flippedNormals,
            source.UVs,
            flippedIndices,
            source.Colors,
            source.AmbientOcclusion,
            source.VertexCount,
            source.TriangleCount);
    }

    /// <summary>
    /// Merges multiple per-chunk meshes into a single MeshData, offsetting vertex positions
    /// by each chunk's world-space offset.
    /// </summary>
    /// <param name="chunks">List of (MeshData, VoxelCoord offset) tuples to merge.</param>
    /// <param name="voxelScale">World units per voxel for offset conversion.</param>
    /// <returns>A single merged MeshData.</returns>
    public static MeshData MergeMeshData(IReadOnlyList<(MeshData Mesh, VoxelCoord Offset)> chunks, float voxelScale = 0.25f)
    {
        var totalVertices = 0;
        var totalIndices = 0;
        var hasUvs = false;
        var hasColors = false;
        var hasAo = false;

        foreach (var (mesh, _) in chunks)
        {
            totalVertices += mesh.VertexCount;
            totalIndices += mesh.Indices.Length;
            if (mesh.UVs != null) hasUvs = true;
            if (mesh.Colors != null) hasColors = true;
            if (mesh.AmbientOcclusion != null) hasAo = true;
        }

        var mergedVertices = new float[totalVertices * 3];
        var mergedNormals = new float[totalVertices * 3];
        var mergedUvs = hasUvs ? new float[totalVertices * 2] : null;
        var mergedIndices = new int[totalIndices];
        var mergedColors = hasColors ? new byte[totalVertices * 4] : null;
        var mergedAo = hasAo ? new float[totalVertices] : null;

        var vertexBase = 0;
        var indexBase = 0;

        foreach (var (mesh, offset) in chunks)
        {
            var offsetX = offset.X * voxelScale;
            var offsetY = offset.Y * voxelScale;
            var offsetZ = offset.Z * voxelScale;

            // Copy vertices with world-space offset
            for (var i = 0; i < mesh.VertexCount; i++)
            {
                mergedVertices[(vertexBase + i) * 3] = mesh.Vertices[i * 3] + offsetX;
                mergedVertices[(vertexBase + i) * 3 + 1] = mesh.Vertices[i * 3 + 1] + offsetY;
                mergedVertices[(vertexBase + i) * 3 + 2] = mesh.Vertices[i * 3 + 2] + offsetZ;
            }

            // Copy normals directly
            Array.Copy(mesh.Normals, 0, mergedNormals, vertexBase * 3, mesh.VertexCount * 3);

            // Copy UVs
            if (mergedUvs != null && mesh.UVs != null)
                Array.Copy(mesh.UVs, 0, mergedUvs, vertexBase * 2, mesh.VertexCount * 2);

            // Copy colors
            if (mergedColors != null && mesh.Colors != null)
                Array.Copy(mesh.Colors, 0, mergedColors, vertexBase * 4, mesh.VertexCount * 4);

            // Copy AO
            if (mergedAo != null && mesh.AmbientOcclusion != null)
                Array.Copy(mesh.AmbientOcclusion, 0, mergedAo, vertexBase, mesh.VertexCount);

            // Copy indices with vertex base offset
            for (var i = 0; i < mesh.Indices.Length; i++)
                mergedIndices[indexBase + i] = mesh.Indices[i] + vertexBase;

            vertexBase += mesh.VertexCount;
            indexBase += mesh.Indices.Length;
        }

        return new MeshData(
            mergedVertices,
            mergedNormals,
            mergedUvs,
            mergedIndices,
            mergedColors,
            mergedAo,
            totalVertices,
            totalIndices / 3);
    }
}
