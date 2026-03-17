using BeyondImmersion.Bannou.VoxelCore.Grid;
using BeyondImmersion.Bannou.VoxelCore.Tables;

namespace BeyondImmersion.Bannou.VoxelCore.Meshing;

/// <summary>
/// Smooth surface extraction from voxel data using the Marching Cubes algorithm.
/// Reference: Lorensen &amp; Cline, "Marching Cubes: A High Resolution 3D Surface
/// Construction Algorithm" (SIGGRAPH 1987). Uses precomputed lookup tables (256 cube
/// configurations). Patent expired 2005. Ambient occlusion is not applicable to
/// marching cubes (smooth surfaces don't have voxel corners); AO output is always null.
/// </summary>
public sealed class MarchingCubesMesher : IMesher
{
    // Edge endpoint pairs: which two corners each of the 12 edges connects.
    // Corner numbering (standard MC convention):
    // 0=(0,0,0) 1=(1,0,0) 2=(1,0,1) 3=(0,0,1)
    // 4=(0,1,0) 5=(1,1,0) 6=(1,1,1) 7=(0,1,1)
    private static readonly (int a, int b)[] EdgeEndpoints =
    {
        (0, 1), (1, 2), (2, 3), (3, 0),  // bottom edges
        (4, 5), (5, 6), (6, 7), (7, 4),  // top edges
        (0, 4), (1, 5), (2, 6), (3, 7)   // vertical edges
    };

    // Corner positions relative to cell origin (x, y, z)
    private static readonly (int dx, int dy, int dz)[] CornerOffsets =
    {
        (0, 0, 0), (1, 0, 0), (1, 0, 1), (0, 0, 1),
        (0, 1, 0), (1, 1, 0), (1, 1, 1), (0, 1, 1)
    };

    /// <inheritdoc />
    public MeshData Mesh(VoxelChunk chunk, VoxelChunk?[] neighbors, Palette palette, MeshingOptions options)
    {
        if (chunk.IsEmpty)
            return MeshData.Empty;

        var scale = options.VoxelScale;
        var threshold = 0.5f;

        var vertices = new List<float>();
        var normals = new List<float>();
        var uvs = options.CollisionMode ? null : new List<float>();
        var indices = new List<int>();
        var colors = options.CollisionMode ? null : new List<byte>();
        var vertexCount = 0;

        // Interpolated edge vertices cache per cell (12 possible edge vertices)
        var edgeVertices = new float[12 * 3];
        var edgeNormals = new float[12 * 3];

        // Process 15x15x15 cells (each cell spans 2x2x2 voxels)
        for (var y = 0; y < 15; y++)
        for (var z = 0; z < 15; z++)
        for (var x = 0; x < 15; x++)
        {
            // Sample 8 corner densities (non-empty = 1.0, empty = 0.0)
            var cubeIndex = 0;
            var dominantPalette = (byte)0;
            var maxDensity = 0f;

            for (var c = 0; c < 8; c++)
            {
                var (cdx, cdy, cdz) = CornerOffsets[c];
                var cx = x + cdx;
                var cy = y + cdy;
                var cz = z + cdz;

                var palIdx = GetPaletteIndex(chunk, neighbors, cx, cy, cz);
                var density = palIdx > 0 ? 1f : 0f;

                if (density > threshold)
                    cubeIndex |= 1 << c;

                if (density > maxDensity)
                {
                    maxDensity = density;
                    dominantPalette = palIdx;
                }
            }

            if (cubeIndex == 0 || cubeIndex == 255)
                continue;

            var edgeMask = MarchingCubesTables.EdgeTable[cubeIndex];

            // Interpolate vertices along intersected edges
            for (var e = 0; e < 12; e++)
            {
                if ((edgeMask & (1 << e)) == 0) continue;

                var (a, b) = EdgeEndpoints[e];
                var (ax, ay, az) = CornerOffsets[a];
                var (bx, by, bz) = CornerOffsets[b];

                // Binary density (0 or 1) means interpolation is always at midpoint
                var mx = (x + ax + x + bx) * 0.5f;
                var my = (y + ay + y + by) * 0.5f;
                var mz = (z + az + z + bz) * 0.5f;

                edgeVertices[e * 3] = mx * scale;
                edgeVertices[e * 3 + 1] = my * scale;
                edgeVertices[e * 3 + 2] = mz * scale;

                // Compute gradient-based normal at the edge midpoint
                var gx = GetDensity(chunk, neighbors, x + ax + 1, y + ay, z + az) -
                         GetDensity(chunk, neighbors, x + ax - 1, y + ay, z + az);
                var gy = GetDensity(chunk, neighbors, x + ax, y + ay + 1, z + az) -
                         GetDensity(chunk, neighbors, x + ax, y + ay - 1, z + az);
                var gz = GetDensity(chunk, neighbors, x + ax, y + ay, z + az + 1) -
                         GetDensity(chunk, neighbors, x + ax, y + ay, z + az - 1);
                var len = MathF.Sqrt(gx * gx + gy * gy + gz * gz);
                if (len > 0.0001f)
                {
                    edgeNormals[e * 3] = -gx / len;
                    edgeNormals[e * 3 + 1] = -gy / len;
                    edgeNormals[e * 3 + 2] = -gz / len;
                }
                else
                {
                    edgeNormals[e * 3] = 0;
                    edgeNormals[e * 3 + 1] = 1;
                    edgeNormals[e * 3 + 2] = 0;
                }
            }

            // Emit triangles from lookup table
            var triList = MarchingCubesTables.TriTable[cubeIndex];
            for (var t = 0; t < triList.Length; t += 3)
            {
                for (var v = 0; v < 3; v++)
                {
                    var edgeIdx = triList[t + v];
                    vertices.Add(edgeVertices[edgeIdx * 3]);
                    vertices.Add(edgeVertices[edgeIdx * 3 + 1]);
                    vertices.Add(edgeVertices[edgeIdx * 3 + 2]);

                    normals.Add(edgeNormals[edgeIdx * 3]);
                    normals.Add(edgeNormals[edgeIdx * 3 + 1]);
                    normals.Add(edgeNormals[edgeIdx * 3 + 2]);

                    if (uvs != null)
                    {
                        var (u, uv) = MesherHelpers.ComputeUV(dominantPalette);
                        uvs.Add(u);
                        uvs.Add(uv);
                    }

                    if (colors != null && dominantPalette > 0)
                    {
                        var entry = palette.Get(dominantPalette);
                        colors.Add(entry.Color.R);
                        colors.Add(entry.Color.G);
                        colors.Add(entry.Color.B);
                        colors.Add(entry.Color.A);
                    }
                    else if (colors != null)
                    {
                        colors.Add(128);
                        colors.Add(128);
                        colors.Add(128);
                        colors.Add(255);
                    }

                    indices.Add(vertexCount);
                    vertexCount++;
                }
            }
        }

        return new MeshData(
            vertices.ToArray(),
            normals.ToArray(),
            uvs?.ToArray(),
            indices.ToArray(),
            colors?.ToArray(),
            null, // AO not applicable to marching cubes
            vertexCount,
            indices.Count / 3);
    }

    private static byte GetPaletteIndex(VoxelChunk chunk, VoxelChunk?[] neighbors, int x, int y, int z)
    {
        if (x >= 0 && x < 16 && y >= 0 && y < 16 && z >= 0 && z < 16)
            return chunk.PaletteIndices[VoxelChunk.GetFlatIndex(x, y, z)];

        // Outside chunk bounds — check neighbor
        if (MesherHelpers.IsNeighborEmpty(chunk, neighbors, x, y, z))
            return 0;
        return 1; // Non-empty but we can't get the actual palette index from neighbor easily
    }

    private static float GetDensity(VoxelChunk chunk, VoxelChunk?[] neighbors, int x, int y, int z)
    {
        return GetPaletteIndex(chunk, neighbors, x, y, z) > 0 ? 1f : 0f;
    }
}
