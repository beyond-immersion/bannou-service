using BeyondImmersion.Bannou.VoxelCore.Grid;
using BeyondImmersion.Bannou.VoxelCore.Math;
using BeyondImmersion.Bannou.VoxelCore.Meshing;

namespace BeyondImmersion.Bannou.VoxelCore.Voxelization;

/// <summary>
/// Converts a triangle mesh to a <see cref="VoxelGrid"/> via 3D scan-conversion and
/// ray-parity solid fill. Reference: Nooruddin &amp; Turk, "Simplification and Repair of
/// Polygonal Models Using Volumetric Techniques" (IEEE TVCG, 2003).
/// </summary>
public static class MeshVoxelizer
{
    /// <summary>
    /// Voxelizes a triangle mesh into a voxel grid. Phase 1: surface rasterization by
    /// 3D scan-converting each triangle. Phase 2 (if Solid fill mode): ray-parity interior fill.
    /// Phase 3: frozen border marking.
    /// </summary>
    /// <param name="source">The mesh data to voxelize.</param>
    /// <param name="palette">Palette for material assignment.</param>
    /// <param name="options">Voxelization options.</param>
    /// <returns>A new VoxelGrid containing the voxelized mesh.</returns>
    public static VoxelGrid Voxelize(MeshData source, Palette palette, VoxelizationOptions options)
    {
        // Phase 1: Determine grid bounds from mesh extents
        var minX = float.MaxValue;
        var minY = float.MaxValue;
        var minZ = float.MaxValue;
        var maxX = float.MinValue;
        var maxY = float.MinValue;
        var maxZ = float.MinValue;

        for (var i = 0; i < source.VertexCount; i++)
        {
            var vx = source.Vertices[i * 3];
            var vy = source.Vertices[i * 3 + 1];
            var vz = source.Vertices[i * 3 + 2];
            if (vx < minX) minX = vx;
            if (vy < minY) minY = vy;
            if (vz < minZ) minZ = vz;
            if (vx > maxX) maxX = vx;
            if (vy > maxY) maxY = vy;
            if (vz > maxZ) maxZ = vz;
        }

        var gridMin = new VoxelCoord(
            (int)MathF.Floor(minX / options.VoxelScale),
            (int)MathF.Floor(minY / options.VoxelScale),
            (int)MathF.Floor(minZ / options.VoxelScale));
        var gridMax = new VoxelCoord(
            (int)MathF.Ceiling(maxX / options.VoxelScale),
            (int)MathF.Ceiling(maxY / options.VoxelScale),
            (int)MathF.Ceiling(maxZ / options.VoxelScale));

        var bounds = new VoxelBounds(gridMin, gridMax);
        var grid = new VoxelGrid(bounds, palette);

        // Phase 2: Surface rasterization
        var invScale = 1f / options.VoxelScale;
        var halfDiag = MathF.Sqrt(3f) / 2f; // 0.866 — half-diagonal of unit cube

        for (var t = 0; t < source.Indices.Length; t += 3)
        {
            var i0 = source.Indices[t];
            var i1 = source.Indices[t + 1];
            var i2 = source.Indices[t + 2];

            var v0x = source.Vertices[i0 * 3] * invScale;
            var v0y = source.Vertices[i0 * 3 + 1] * invScale;
            var v0z = source.Vertices[i0 * 3 + 2] * invScale;
            var v1x = source.Vertices[i1 * 3] * invScale;
            var v1y = source.Vertices[i1 * 3 + 1] * invScale;
            var v1z = source.Vertices[i1 * 3 + 2] * invScale;
            var v2x = source.Vertices[i2 * 3] * invScale;
            var v2y = source.Vertices[i2 * 3 + 1] * invScale;
            var v2z = source.Vertices[i2 * 3 + 2] * invScale;

            // Determine palette index from source vertex colors
            var paletteIndex = options.DefaultPaletteIndex;
            if (source.Colors != null)
            {
                var avgR = (byte)((source.Colors[i0 * 4] + source.Colors[i1 * 4] + source.Colors[i2 * 4]) / 3);
                var avgG = (byte)((source.Colors[i0 * 4 + 1] + source.Colors[i1 * 4 + 1] + source.Colors[i2 * 4 + 1]) / 3);
                var avgB = (byte)((source.Colors[i0 * 4 + 2] + source.Colors[i1 * 4 + 2] + source.Colors[i2 * 4 + 2]) / 3);
                var avgA = (byte)((source.Colors[i0 * 4 + 3] + source.Colors[i1 * 4 + 3] + source.Colors[i2 * 4 + 3]) / 3);
                paletteIndex = palette.GetOrAddIndex(new Color(avgR, avgG, avgB, avgA), MaterialType.Diffuse, 0.5f);
            }

            // Triangle bounding box in voxel space
            var triMinX = (int)MathF.Floor(System.Math.Min(v0x, System.Math.Min(v1x, v2x)));
            var triMinY = (int)MathF.Floor(System.Math.Min(v0y, System.Math.Min(v1y, v2y)));
            var triMinZ = (int)MathF.Floor(System.Math.Min(v0z, System.Math.Min(v1z, v2z)));
            var triMaxX = (int)MathF.Ceiling(System.Math.Max(v0x, System.Math.Max(v1x, v2x)));
            var triMaxY = (int)MathF.Ceiling(System.Math.Max(v0y, System.Math.Max(v1y, v2y)));
            var triMaxZ = (int)MathF.Ceiling(System.Math.Max(v0z, System.Math.Max(v1z, v2z)));

            for (var y = triMinY; y <= triMaxY; y++)
            for (var z = triMinZ; z <= triMaxZ; z++)
            for (var x = triMinX; x <= triMaxX; x++)
            {
                var coord = new VoxelCoord(x, y, z);
                if (!bounds.Contains(coord)) continue;

                var cx = x + 0.5f;
                var cy = y + 0.5f;
                var cz = z + 0.5f;

                if (PointTriangleDistance(cx, cy, cz, v0x, v0y, v0z, v1x, v1y, v1z, v2x, v2y, v2z) < halfDiag)
                {
                    grid.SetVoxel(coord, new Voxel(paletteIndex, VoxelFlags.None));
                }
            }
        }

        // Phase 3: Solid fill (ray-parity)
        if (options.FillMode == VoxelFillMode.Solid)
        {
            for (var x = gridMin.X; x <= gridMax.X; x++)
            for (var z = gridMin.Z; z <= gridMax.Z; z++)
            {
                var inside = false;
                var lastSurfaceMaterial = options.DefaultPaletteIndex;

                for (var y = gridMin.Y; y <= gridMax.Y; y++)
                {
                    var coord = new VoxelCoord(x, y, z);
                    var voxel = grid.GetVoxel(coord);

                    if (!voxel.IsEmpty)
                    {
                        inside = !inside;
                        lastSurfaceMaterial = voxel.PaletteIndex;
                    }
                    else if (inside)
                    {
                        grid.SetVoxel(coord, new Voxel(lastSurfaceMaterial, VoxelFlags.None));
                    }
                }
            }
        }

        // Phase 4: Mark frozen border
        foreach (var (chunkCoord, chunk) in grid.EnumerateChunks())
        {
            var baseCoord = chunkCoord.ToVoxelCoord();
            for (var y = 0; y < 16; y++)
            for (var z = 0; z < 16; z++)
            for (var x = 0; x < 16; x++)
            {
                if (chunk.PaletteIndices[VoxelChunk.GetFlatIndex(x, y, z)] == 0) continue;

                var wx = baseCoord.X + x;
                var wy = baseCoord.Y + y;
                var wz = baseCoord.Z + z;

                var distToEdgeX = System.Math.Min(wx - gridMin.X, gridMax.X - wx);
                var distToEdgeY = System.Math.Min(wy - gridMin.Y, gridMax.Y - wy);
                var distToEdgeZ = System.Math.Min(wz - gridMin.Z, gridMax.Z - wz);

                if (System.Math.Min(distToEdgeX, System.Math.Min(distToEdgeY, distToEdgeZ)) < options.FrozenBorderWidth)
                {
                    var voxel = chunk.GetVoxel(x, y, z);
                    chunk.SetVoxel(x, y, z, new Voxel(voxel.PaletteIndex, voxel.Flags | VoxelFlags.Frozen));
                }
            }
        }

        return grid;
    }

    /// <summary>
    /// Computes the minimum distance from a point to a triangle in 3D space.
    /// </summary>
    private static float PointTriangleDistance(
        float px, float py, float pz,
        float v0x, float v0y, float v0z,
        float v1x, float v1y, float v1z,
        float v2x, float v2y, float v2z)
    {
        // Edge vectors
        var e0x = v1x - v0x; var e0y = v1y - v0y; var e0z = v1z - v0z;
        var e1x = v2x - v0x; var e1y = v2y - v0y; var e1z = v2z - v0z;
        var dx = v0x - px; var dy = v0y - py; var dz = v0z - pz;

        var a = e0x * e0x + e0y * e0y + e0z * e0z;
        var b = e0x * e1x + e0y * e1y + e0z * e1z;
        var c = e1x * e1x + e1y * e1y + e1z * e1z;
        var d = e0x * dx + e0y * dy + e0z * dz;
        var e = e1x * dx + e1y * dy + e1z * dz;
        var f = dx * dx + dy * dy + dz * dz;

        var det = a * c - b * b;
        var s = b * e - c * d;
        var t = b * d - a * e;

        if (s + t <= det)
        {
            if (s < 0)
            {
                if (t < 0)
                {
                    // Region 4
                    if (d < 0) { s = Clamp(-d / a, 0, 1); t = 0; }
                    else { s = 0; t = Clamp(-e / c, 0, 1); }
                }
                else
                {
                    // Region 3
                    s = 0;
                    t = Clamp(-e / c, 0, 1);
                }
            }
            else if (t < 0)
            {
                // Region 5
                s = Clamp(-d / a, 0, 1);
                t = 0;
            }
            else
            {
                // Region 0
                var invDet = 1f / det;
                s *= invDet;
                t *= invDet;
            }
        }
        else
        {
            if (s < 0)
            {
                // Region 2
                var tmp0 = b + d;
                var tmp1 = c + e;
                if (tmp1 > tmp0) { var numer = tmp1 - tmp0; var denom = a - 2 * b + c; s = Clamp(numer / denom, 0, 1); t = 1 - s; }
                else { s = 0; t = Clamp(-e / c, 0, 1); }
            }
            else if (t < 0)
            {
                // Region 6
                var tmp0 = b + e;
                var tmp1 = a + d;
                if (tmp1 > tmp0) { var numer = tmp1 - tmp0; var denom = a - 2 * b + c; t = Clamp(numer / denom, 0, 1); s = 1 - t; }
                else { t = 0; s = Clamp(-d / a, 0, 1); }
            }
            else
            {
                // Region 1
                var numer = (c + e) - (b + d);
                if (numer <= 0) { s = 0; t = 1; }
                else { var denom = a - 2 * b + c; s = Clamp(numer / denom, 0, 1); t = 1 - s; }
            }
        }

        var closestX = v0x + s * e0x + t * e1x - px;
        var closestY = v0y + s * e0y + t * e1y - py;
        var closestZ = v0z + s * e0z + t * e1z - pz;

        return MathF.Sqrt(closestX * closestX + closestY * closestY + closestZ * closestZ);
    }

    private static float Clamp(float value, float min, float max) =>
        value < min ? min : (value > max ? max : value);
}
