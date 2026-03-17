using BeyondImmersion.Bannou.VoxelCore.Grid;

namespace BeyondImmersion.Bannou.VoxelCore.Meshing;

/// <summary>
/// Coplanar face merging mesher. Scans each 2D slice per face direction and finds
/// maximal rectangles of the same material. Typically 5-20x face count reduction.
/// Reference: Mikola Lysenko, "Meshing in a Minecraft Game" (0fps.net, 2012).
/// When AO is enabled, the merge key includes per-vertex AO values to prevent merging
/// faces with different lighting (which would produce incorrect shading).
/// </summary>
public sealed class GreedyMesher : IMesher
{
    /// <inheritdoc />
    public MeshData Mesh(VoxelChunk chunk, VoxelChunk?[] neighbors, Palette palette, MeshingOptions options)
    {
        if (chunk.IsEmpty)
            return MeshData.Empty;

        var scale = options.VoxelScale;
        var computeAo = options.AmbientOcclusion && !options.CollisionMode;

        var vertices = new List<float>();
        var normals = new List<float>();
        var uvs = options.CollisionMode ? null : new List<float>();
        var indices = new List<int>();
        var colors = options.CollisionMode ? null : new List<byte>();
        var aoValues = computeAo ? new List<float>() : null;
        var vertexCount = 0;

        // For each of 6 face directions, process all 16 slices
        for (var face = 0; face < 6; face++)
        {
            var (ndx, ndy, ndz) = MesherHelpers.FaceDirections[face];
            var (fnx, fny, fnz) = MesherHelpers.FaceNormals[face];

            // Map face direction to axis indices for slice iteration
            // axis: the axis normal to the face (0=X, 1=Y, 2=Z)
            // u, v: the two axes within the slice plane
            int axis, uAxis, vAxis;
            if (ndx != 0) { axis = 0; uAxis = 2; vAxis = 1; }      // X faces: scan ZY
            else if (ndy != 0) { axis = 1; uAxis = 0; vAxis = 2; }  // Y faces: scan XZ
            else { axis = 2; uAxis = 0; vAxis = 1; }                 // Z faces: scan XY

            for (var slice = 0; slice < 16; slice++)
            {
                // Build mask for this slice
                var maskPalette = new byte[16 * 16];
                var maskAo = computeAo ? new long[16 * 16] : null;

                for (var v = 0; v < 16; v++)
                for (var u = 0; u < 16; u++)
                {
                    int x, y, z;
                    SetCoords(axis, slice, uAxis, u, vAxis, v, out x, out y, out z);

                    var palIdx = chunk.PaletteIndices[VoxelChunk.GetFlatIndex(x, y, z)];
                    if (palIdx == 0) continue;

                    if (!MesherHelpers.IsNeighborEmpty(chunk, neighbors, x + ndx, y + ndy, z + ndz))
                        continue;

                    maskPalette[u + v * 16] = palIdx;

                    if (maskAo != null)
                    {
                        // Encode 4 AO values into a single long for fast comparison
                        var ao = ComputeFaceAo(chunk, neighbors, x, y, z, face);
                        maskAo[u + v * 16] = ao;
                    }
                }

                // Greedy merge: find maximal rectangles
                for (var v = 0; v < 16; v++)
                for (var u = 0; u < 16; u++)
                {
                    var palIdx = maskPalette[u + v * 16];
                    if (palIdx == 0) continue;

                    var aoKey = maskAo != null ? maskAo[u + v * 16] : 0L;

                    // Extend width
                    var width = 1;
                    while (u + width < 16
                           && maskPalette[u + width + v * 16] == palIdx
                           && (maskAo == null || maskAo[u + width + v * 16] == aoKey))
                    {
                        width++;
                    }

                    // Extend height
                    var height = 1;
                    var canExtend = true;
                    while (canExtend && v + height < 16)
                    {
                        for (var du = 0; du < width; du++)
                        {
                            if (maskPalette[u + du + (v + height) * 16] != palIdx
                                || (maskAo != null && maskAo[u + du + (v + height) * 16] != aoKey))
                            {
                                canExtend = false;
                                break;
                            }
                        }
                        if (canExtend) height++;
                    }

                    // Emit merged quad
                    EmitQuad(
                        vertices, normals, uvs, indices, colors, aoValues,
                        ref vertexCount, palette, options,
                        axis, slice, uAxis, vAxis,
                        u, v, width, height,
                        fnx, fny, fnz, ndx, ndy, ndz,
                        palIdx, scale, maskAo, aoKey);

                    // Clear mask for merged region
                    for (var dv = 0; dv < height; dv++)
                    for (var du = 0; du < width; du++)
                    {
                        maskPalette[u + du + (v + dv) * 16] = 0;
                        if (maskAo != null)
                            maskAo[u + du + (v + dv) * 16] = 0;
                    }
                }
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

    private static void SetCoords(int axis, int slice, int uAxis, int u, int vAxis, int v,
        out int x, out int y, out int z)
    {
        x = y = z = 0;
        switch (axis) { case 0: x = slice; break; case 1: y = slice; break; case 2: z = slice; break; }
        switch (uAxis) { case 0: x = u; break; case 1: y = u; break; case 2: z = u; break; }
        switch (vAxis) { case 0: x = v; break; case 1: y = v; break; case 2: z = v; break; }
    }

    /// <summary>
    /// Encodes 4 AO values (each quantized to 0-3) into a single long for fast equality comparison.
    /// </summary>
    private static long ComputeFaceAo(VoxelChunk chunk, VoxelChunk?[] neighbors, int x, int y, int z, int face)
    {
        // Use the same AO corner offset tables as CulledMesher
        long encoded = 0;
        for (var v = 0; v < 4; v++)
        {
            var ao = ComputeVertexAO(chunk, neighbors, x, y, z, face, v);
            // Quantize to 0-3 levels for merge comparison
            var quantized = (int)(ao * 3f + 0.5f);
            encoded |= (long)quantized << (v * 8);
        }
        return encoded;
    }

    private static float ComputeVertexAO(
        VoxelChunk chunk, VoxelChunk?[] neighbors,
        int x, int y, int z, int face, int vertex)
    {
        // Reuse the same AO computation as CulledMesher
        var offsets = CulledMesherAoOffsets.Get(face, vertex);
        var side1 = MesherHelpers.IsSolid(chunk, neighbors,
            x + offsets[0].dx, y + offsets[0].dy, z + offsets[0].dz) ? 1 : 0;
        var side2 = MesherHelpers.IsSolid(chunk, neighbors,
            x + offsets[1].dx, y + offsets[1].dy, z + offsets[1].dz) ? 1 : 0;

        if (side1 == 1 && side2 == 1) return 0f;

        var corner = MesherHelpers.IsSolid(chunk, neighbors,
            x + offsets[2].dx, y + offsets[2].dy, z + offsets[2].dz) ? 1 : 0;

        return 1f - (side1 + side2 + corner) / 3f;
    }

    private static void EmitQuad(
        List<float> vertices, List<float> normals, List<float>? uvs, List<int> indices,
        List<byte>? colors, List<float>? aoValues, ref int vertexCount,
        Palette palette, MeshingOptions options,
        int axis, int slice, int uAxis, int vAxis,
        int u, int v, int width, int height,
        float fnx, float fny, float fnz,
        int ndx, int ndy, int ndz,
        byte palIdx, float scale,
        long[]? maskAo, long aoKey)
    {
        // Compute quad corners in voxel space
        // The quad extends from (u, v) to (u+width, v+height) on the slice plane at 'slice'
        var sliceOffset = ndx > 0 || ndy > 0 || ndz > 0 ? slice + 1 : slice;

        float[] quadCorners = new float[4 * 3]; // 4 vertices, 3 components each
        for (var i = 0; i < 4; i++)
        {
            var cu = i == 1 || i == 2 ? u + width : u;
            var cv = i == 2 || i == 3 ? v + height : v;
            float cx = 0, cy = 0, cz = 0;
            switch (axis) { case 0: cx = sliceOffset; break; case 1: cy = sliceOffset; break; case 2: cz = sliceOffset; break; }
            switch (uAxis) { case 0: cx = cu; break; case 1: cy = cu; break; case 2: cz = cu; break; }
            switch (vAxis) { case 0: cx = cv; break; case 1: cy = cv; break; case 2: cz = cv; break; }
            quadCorners[i * 3] = cx * scale;
            quadCorners[i * 3 + 1] = cy * scale;
            quadCorners[i * 3 + 2] = cz * scale;
        }

        // Emit 4 vertices
        for (var i = 0; i < 4; i++)
        {
            vertices.Add(quadCorners[i * 3]);
            vertices.Add(quadCorners[i * 3 + 1]);
            vertices.Add(quadCorners[i * 3 + 2]);
            normals.Add(fnx);
            normals.Add(fny);
            normals.Add(fnz);

            if (uvs != null)
            {
                var (texU, texV) = MesherHelpers.ComputeUV(palIdx);
                uvs.Add(texU);
                uvs.Add(texV);
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

        if (aoValues != null && maskAo != null)
        {
            for (var i = 0; i < 4; i++)
            {
                var quantized = (int)((aoKey >> (i * 8)) & 0xFF);
                aoValues.Add(quantized / 3f);
            }
        }

        // Emit triangles with anisotropy fix
        var shouldFlip = false;
        if (aoValues != null && maskAo != null)
        {
            var a0 = (int)(aoKey & 0xFF);
            var a1 = (int)((aoKey >> 8) & 0xFF);
            var a2 = (int)((aoKey >> 16) & 0xFF);
            var a3 = (int)((aoKey >> 24) & 0xFF);
            shouldFlip = a0 + a2 < a1 + a3;
        }

        if (shouldFlip)
        {
            indices.Add(vertexCount + 1);
            indices.Add(vertexCount + 2);
            indices.Add(vertexCount + 3);
            indices.Add(vertexCount + 3);
            indices.Add(vertexCount + 0);
            indices.Add(vertexCount + 1);
        }
        else
        {
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

/// <summary>
/// Shared AO corner offset lookup table, reused by CulledMesher and GreedyMesher.
/// </summary>
internal static class CulledMesherAoOffsets
{
    private static readonly (int dx, int dy, int dz)[][][] Offsets = BuildOffsets();

    internal static (int dx, int dy, int dz)[] Get(int face, int vertex) => Offsets[face][vertex];

    private static (int dx, int dy, int dz)[][][] BuildOffsets()
    {
        var faceTangents = new (int, int, int)[]
        {
            (0, 1, 0), (0, 1, 0),
            (1, 0, 0), (1, 0, 0),
            (1, 0, 0), (1, 0, 0),
        };
        var faceBitangents = new (int, int, int)[]
        {
            (0, 0, 1), (0, 0, 1),
            (0, 0, 1), (0, 0, 1),
            (0, 1, 0), (0, 1, 0),
        };
        var cornerSigns = new (int s1, int s2)[]
        {
            (-1, -1), (1, -1), (1, 1), (-1, 1)
        };

        var offsets = new (int, int, int)[6][][];
        for (var face = 0; face < 6; face++)
        {
            var (ndx, ndy, ndz) = MesherHelpers.FaceDirections[face];
            var (tdx, tdy, tdz) = faceTangents[face];
            var (bdx, bdy, bdz) = faceBitangents[face];

            offsets[face] = new (int, int, int)[4][];
            for (var v = 0; v < 4; v++)
            {
                var (s1, s2) = cornerSigns[v];
                offsets[face][v] = new[]
                {
                    (ndx + s1 * tdx, ndy + s1 * tdy, ndz + s1 * tdz),
                    (ndx + s2 * bdx, ndy + s2 * bdy, ndz + s2 * bdz),
                    (ndx + s1 * tdx + s2 * bdx, ndy + s1 * tdy + s2 * bdy, ndz + s1 * tdz + s2 * bdz)
                };
            }
        }
        return offsets;
    }
}
