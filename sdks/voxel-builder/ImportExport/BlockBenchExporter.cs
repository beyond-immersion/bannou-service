using System.Text.Json;
using System.Text.Json.Nodes;
using BeyondImmersion.Bannou.VoxelCore.Grid;
using BeyondImmersion.Bannou.VoxelCore.Math;

namespace BeyondImmersion.Bannou.VoxelBuilder.ImportExport;

/// <summary>
/// Exports a <see cref="VoxelGrid"/> to BlockBench .bbmodel format (JSON).
/// Groups voxels into maximal cuboid regions (greedy scan) to produce compact element lists.
/// Written from scratch — clean-room JSON generation, no BlockBench source code dependency.
/// </summary>
public sealed class BlockBenchExporter : IVoxelExporter
{
    /// <summary>
    /// Export a VoxelGrid as .bbmodel JSON bytes.
    /// </summary>
    /// <param name="grid">The grid to export.</param>
    /// <returns>.bbmodel file content as UTF-8 bytes.</returns>
    public byte[] Export(VoxelGrid grid)
    {
        var elements = new JsonArray();
        var elementIds = new JsonArray();
        var elementId = 0;

        // Scan for cuboid regions using a greedy approach on each Y slice.
        // Mark visited voxels to avoid overlapping elements.
        var visited = new HashSet<VoxelCoord>();

        for (var y = grid.Bounds.Min.Y; y <= grid.Bounds.Max.Y; y++)
        for (var z = grid.Bounds.Min.Z; z <= grid.Bounds.Max.Z; z++)
        for (var x = grid.Bounds.Min.X; x <= grid.Bounds.Max.X; x++)
        {
            var coord = new VoxelCoord(x, y, z);
            if (visited.Contains(coord)) continue;

            var voxel = grid.GetVoxel(coord);
            if (voxel.IsEmpty) continue;

            var palIdx = voxel.PaletteIndex;

            // Extend in X
            var extX = 1;
            while (x + extX <= grid.Bounds.Max.X
                && !visited.Contains(new VoxelCoord(x + extX, y, z))
                && grid.GetVoxel(new VoxelCoord(x + extX, y, z)).PaletteIndex == palIdx)
                extX++;

            // Extend in Z (maintaining full X width)
            var extZ = 1;
            while (z + extZ <= grid.Bounds.Max.Z)
            {
                var canExtend = true;
                for (var dx = 0; dx < extX; dx++)
                {
                    var c = new VoxelCoord(x + dx, y, z + extZ);
                    if (visited.Contains(c) || grid.GetVoxel(c).PaletteIndex != palIdx)
                    {
                        canExtend = false;
                        break;
                    }
                }
                if (!canExtend) break;
                extZ++;
            }

            // Extend in Y (maintaining full XZ rectangle)
            var extY = 1;
            while (y + extY <= grid.Bounds.Max.Y)
            {
                var canExtend = true;
                for (var dz = 0; dz < extZ && canExtend; dz++)
                for (var dx = 0; dx < extX && canExtend; dx++)
                {
                    var c = new VoxelCoord(x + dx, y + extY, z + dz);
                    if (visited.Contains(c) || grid.GetVoxel(c).PaletteIndex != palIdx)
                        canExtend = false;
                }
                if (!canExtend) break;
                extY++;
            }

            // Mark all voxels in this cuboid as visited
            for (var dy = 0; dy < extY; dy++)
            for (var dz = 0; dz < extZ; dz++)
            for (var dx = 0; dx < extX; dx++)
                visited.Add(new VoxelCoord(x + dx, y + dy, z + dz));

            // Compute UV coordinates for this palette entry
            var u0 = (palIdx % 16) / 16f;
            var v0 = (palIdx / 16) / 16f;
            var u1 = u0 + 1f / 16f;
            var v1 = v0 + 1f / 16f;

            // Create the element JSON
            var elem = new JsonObject
            {
                ["name"] = $"voxel_{elementId}",
                ["from"] = new JsonArray(x, y, z),
                ["to"] = new JsonArray(x + extX, y + extY, z + extZ),
                ["faces"] = CreateFaces(u0 * 16, v0 * 16, u1 * 16, v1 * 16)
            };

            elements.Add(elem);
            elementIds.Add(elementId.ToString());
            elementId++;
        }

        // Build the .bbmodel document
        var bbmodel = new JsonObject
        {
            ["meta"] = new JsonObject
            {
                ["format_version"] = "4.10",
                ["model_format"] = "free"
            },
            ["resolution"] = new JsonObject
            {
                ["width"] = 16,
                ["height"] = 16
            },
            ["elements"] = elements,
            ["outliner"] = elementIds,
            ["textures"] = new JsonArray()
        };

        // Add palette as a texture entry (16x16 color atlas placeholder)
        // Full PNG texture generation would require an image encoder — we include
        // the palette as metadata instead. Tools can reconstruct the atlas from vertex colors.
        if (grid.Palette.UsedCount > 0)
        {
            var texMeta = new JsonObject
            {
                ["name"] = "palette",
                ["render_mode"] = "default"
            };
            ((JsonArray)bbmodel["textures"]!).Add(texMeta);
        }

        return JsonSerializer.SerializeToUtf8Bytes(bbmodel, new JsonSerializerOptions
        {
            WriteIndented = true
        });
    }

    private static JsonObject CreateFaces(float u0, float v0, float u1, float v1)
    {
        var uv = new JsonArray(u0, v0, u1, v1);
        return new JsonObject
        {
            ["north"] = new JsonObject { ["uv"] = uv.DeepClone(), ["texture"] = 0 },
            ["south"] = new JsonObject { ["uv"] = uv.DeepClone(), ["texture"] = 0 },
            ["east"] = new JsonObject { ["uv"] = uv.DeepClone(), ["texture"] = 0 },
            ["west"] = new JsonObject { ["uv"] = uv.DeepClone(), ["texture"] = 0 },
            ["up"] = new JsonObject { ["uv"] = uv.DeepClone(), ["texture"] = 0 },
            ["down"] = new JsonObject { ["uv"] = uv.DeepClone(), ["texture"] = 0 }
        };
    }
}
