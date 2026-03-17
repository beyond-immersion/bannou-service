using BeyondImmersion.Bannou.VoxelCore.Grid;

namespace BeyondImmersion.Bannou.VoxelCore.Meshing;

/// <summary>
/// Per-face culling mesher. Emits faces only where neighbors are empty.
/// Fast, correct, blocky aesthetic. Every voxel engine implements this as baseline.
/// Includes per-vertex ambient occlusion with the anisotropy fix for visual quality.
/// </summary>
public sealed class CulledMesher : IMesher
{
    /// <inheritdoc />
    public MeshData Mesh(VoxelChunk chunk, VoxelChunk?[] neighbors, Palette palette, MeshingOptions options)
    {
        if (chunk.IsEmpty)
            return MeshData.Empty;

        var scale = options.VoxelScale;
        var vertices = new List<float>();
        var normals = new List<float>();
        var uvs = options.CollisionMode ? null : new List<float>();
        var indices = new List<int>();
        var colors = options.CollisionMode ? null : new List<byte>();
        var aoValues = options.CollisionMode ? null : (options.AmbientOcclusion ? new List<float>() : null);
        var vertexCount = 0;

        for (var y = 0; y < 16; y++)
        for (var z = 0; z < 16; z++)
        for (var x = 0; x < 16; x++)
        {
            var palIdx = chunk.PaletteIndices[VoxelChunk.GetFlatIndex(x, y, z)];
            if (palIdx == 0) continue;

            for (var face = 0; face < 6; face++)
            {
                var (dx, dy, dz) = MesherHelpers.FaceDirections[face];
                if (!MesherHelpers.IsNeighborEmpty(chunk, neighbors, x + dx, y + dy, z + dz))
                    continue;

                // Emit a quad for this face
                var (nx, ny, nz) = MesherHelpers.FaceNormals[face];
                var faceVerts = MesherHelpers.FaceVertices[face];

                // Compute AO for all 4 vertices before emitting (need all 4 for anisotropy fix)
                float ao0 = 1f, ao1 = 1f, ao2 = 1f, ao3 = 1f;
                if (aoValues != null)
                {
                    ao0 = ComputeVertexAO(chunk, neighbors, x, y, z, face, 0);
                    ao1 = ComputeVertexAO(chunk, neighbors, x, y, z, face, 1);
                    ao2 = ComputeVertexAO(chunk, neighbors, x, y, z, face, 2);
                    ao3 = ComputeVertexAO(chunk, neighbors, x, y, z, face, 3);
                }

                // Emit 4 vertices
                for (var v = 0; v < 4; v++)
                {
                    var (vdx, vdy, vdz) = faceVerts[v];
                    vertices.Add((x + vdx) * scale);
                    vertices.Add((y + vdy) * scale);
                    vertices.Add((z + vdz) * scale);

                    normals.Add(nx);
                    normals.Add(ny);
                    normals.Add(nz);

                    if (uvs != null)
                    {
                        var (u, uv) = MesherHelpers.ComputeUV(palIdx);
                        uvs.Add(u);
                        uvs.Add(uv);
                    }

                    if (colors != null)
                    {
                        var entry = palette.Get(palIdx);
                        colors.Add(entry.Color.R);
                        colors.Add(entry.Color.G);
                        colors.Add(entry.Color.B);
                        colors.Add(entry.Color.A);
                    }
                }

                if (aoValues != null)
                {
                    aoValues.Add(ao0);
                    aoValues.Add(ao1);
                    aoValues.Add(ao2);
                    aoValues.Add(ao3);
                }

                // Emit 2 triangles (6 indices, CCW winding)
                // Anisotropy fix: flip the diagonal when AO values differ across corners
                if (aoValues != null && ao0 + ao2 < ao1 + ao3)
                {
                    // Flipped split: (1,2,3)(3,0,1)
                    indices.Add(vertexCount + 1);
                    indices.Add(vertexCount + 2);
                    indices.Add(vertexCount + 3);
                    indices.Add(vertexCount + 3);
                    indices.Add(vertexCount + 0);
                    indices.Add(vertexCount + 1);
                }
                else
                {
                    // Standard split: (0,1,2)(2,3,0)
                    indices.Add(vertexCount + 0);
                    indices.Add(vertexCount + 1);
                    indices.Add(vertexCount + 2);
                    indices.Add(vertexCount + 2);
                    indices.Add(vertexCount + 3);
                    indices.Add(vertexCount + 0);
                }

                vertexCount += 4;
            }
        }

        return new MeshData(
            vertices.ToArray(),
            normals.ToArray(),
            uvs?.ToArray(),
            indices.ToArray(),
            colors?.ToArray(),
            aoValues?.ToArray(),
            vertexCount,
            indices.Count / 3);
    }

    /// <summary>
    /// AO corner offsets per face direction per vertex. Each vertex needs 3 neighbor samples:
    /// two edge-adjacent and one diagonal. The arrays define the relative offsets from the
    /// voxel's position to sample for each corner of each face.
    /// </summary>
    private static readonly (int dx, int dy, int dz)[][][] AoCornerOffsets = BuildAoCornerOffsets();

    private static (int dx, int dy, int dz)[][][] BuildAoCornerOffsets()
    {
        // For each face direction (6), for each vertex (4), we need 3 neighbor positions
        // to sample: side1, side2, and corner (diagonal).
        // The face tangent and bitangent define the plane. AO samples are in that plane
        // offset by the face normal.
        var offsets = new (int, int, int)[6][][];

        // Face tangent/bitangent pairs for AO sampling
        var faceTangents = new (int, int, int)[]
        {
            (0, 1, 0),  // +X: tangent = Y
            (0, 1, 0),  // -X: tangent = Y
            (1, 0, 0),  // +Y: tangent = X
            (1, 0, 0),  // -Y: tangent = X
            (1, 0, 0),  // +Z: tangent = X
            (1, 0, 0),  // -Z: tangent = X
        };

        var faceBitangents = new (int, int, int)[]
        {
            (0, 0, 1),  // +X: bitangent = Z
            (0, 0, 1),  // -X: bitangent = Z
            (0, 0, 1),  // +Y: bitangent = Z
            (0, 0, 1),  // -Y: bitangent = Z
            (0, 1, 0),  // +Z: bitangent = Y
            (0, 1, 0),  // -Z: bitangent = Y
        };

        // Vertex corner signs (relative to tangent/bitangent axes): BL, BR, TR, TL
        var cornerSigns = new (int s1, int s2)[]
        {
            (-1, -1), (1, -1), (1, 1), (-1, 1)
        };

        for (var face = 0; face < 6; face++)
        {
            var (ndx, ndy, ndz) = MesherHelpers.FaceDirections[face];
            var (tdx, tdy, tdz) = faceTangents[face];
            var (bdx, bdy, bdz) = faceBitangents[face];

            offsets[face] = new (int, int, int)[4][];
            for (var v = 0; v < 4; v++)
            {
                var (s1, s2) = cornerSigns[v];
                offsets[face][v] = new (int, int, int)[]
                {
                    // side1: along tangent
                    (ndx + s1 * tdx, ndy + s1 * tdy, ndz + s1 * tdz),
                    // side2: along bitangent
                    (ndx + s2 * bdx, ndy + s2 * bdy, ndz + s2 * bdz),
                    // corner: diagonal
                    (ndx + s1 * tdx + s2 * bdx, ndy + s1 * tdy + s2 * bdy, ndz + s1 * tdz + s2 * bdz)
                };
            }
        }

        return offsets;
    }

    private static float ComputeVertexAO(
        VoxelChunk chunk, VoxelChunk?[] neighbors,
        int x, int y, int z, int face, int vertex)
    {
        var offsets = AoCornerOffsets[face][vertex];
        var side1 = MesherHelpers.IsSolid(chunk, neighbors,
            x + offsets[0].dx, y + offsets[0].dy, z + offsets[0].dz) ? 1 : 0;
        var side2 = MesherHelpers.IsSolid(chunk, neighbors,
            x + offsets[1].dx, y + offsets[1].dy, z + offsets[1].dz) ? 1 : 0;

        // If both sides are solid, corner is fully occluded (no diagonal check needed)
        if (side1 == 1 && side2 == 1)
            return 0f;

        var corner = MesherHelpers.IsSolid(chunk, neighbors,
            x + offsets[2].dx, y + offsets[2].dy, z + offsets[2].dz) ? 1 : 0;

        return 1f - (side1 + side2 + corner) / 3f;
    }
}
