using System.Text.Json;
using BeyondImmersion.Bannou.VoxelCore.Grid;
using BeyondImmersion.Bannou.VoxelCore.Math;

namespace BeyondImmersion.Bannou.VoxelBuilder.ImportExport;

/// <summary>
/// Imports BlockBench .bbmodel files (JSON) into <see cref="VoxelGrid"/>.
/// Parses cuboid elements into filled voxel regions with palette entries derived
/// from per-face textures. Written from scratch — no dependency on BlockBench source code.
/// </summary>
public sealed class BlockBenchImporter : IVoxelImporter
{
    /// <summary>
    /// Import a .bbmodel file from bytes.
    /// </summary>
    /// <param name="data">File content bytes (UTF-8 JSON).</param>
    /// <returns>A VoxelGrid populated from the BlockBench model.</returns>
    public VoxelGrid Import(byte[] data)
    {
        var doc = JsonDocument.Parse(data);
        return ParseModel(doc);
    }

    /// <summary>
    /// Import a .bbmodel file from a stream.
    /// </summary>
    /// <param name="stream">File content stream.</param>
    /// <returns>A VoxelGrid populated from the BlockBench model.</returns>
    public VoxelGrid Import(Stream stream)
    {
        var doc = JsonDocument.Parse(stream);
        return ParseModel(doc);
    }

    private static VoxelGrid ParseModel(JsonDocument doc)
    {
        var root = doc.RootElement;

        // Extract texture colors for palette mapping.
        // BlockBench textures are either base64 PNG or file references.
        // We extract a dominant color per texture for palette assignment.
        var textureColors = new Dictionary<int, Color>();
        if (root.TryGetProperty("textures", out var textures))
        {
            var texIdx = 0;
            foreach (var tex in textures.EnumerateArray())
            {
                // Attempt to read a representative color from the texture source.
                // For base64 PNG: decode would give us pixels. For simplicity, we use
                // the texture's "id" or index as a palette differentiator and assign
                // a default color. Real implementation would decode the PNG.
                var color = new Color(
                    (byte)(128 + texIdx * 30 % 128),
                    (byte)(128 + texIdx * 50 % 128),
                    (byte)(128 + texIdx * 70 % 128));

                if (tex.TryGetProperty("source", out var source))
                {
                    // If it's a data URI with a simple color, try to extract it.
                    // Full PNG decoding is deferred — placeholder sampling for now.
                    var src = source.GetString();
                    if (src != null && src.Length > 0)
                    {
                        // Use string hash for deterministic color assignment from texture
                        var hash = (uint)src.GetHashCode();
                        color = new Color(
                            (byte)(hash & 0xFF),
                            (byte)((hash >> 8) & 0xFF),
                            (byte)((hash >> 16) & 0xFF));
                    }
                }

                textureColors[texIdx] = color;
                texIdx++;
            }
        }

        // First pass: determine grid bounds from all elements
        var minX = int.MaxValue;
        var minY = int.MaxValue;
        var minZ = int.MaxValue;
        var maxX = int.MinValue;
        var maxY = int.MinValue;
        var maxZ = int.MinValue;

        var elements = root.TryGetProperty("elements", out var elems)
            ? elems.EnumerateArray().ToList()
            : new List<JsonElement>();

        foreach (var elem in elements)
        {
            if (!elem.TryGetProperty("from", out var from) || !elem.TryGetProperty("to", out var to))
                continue;

            var fromArr = from.EnumerateArray().ToList();
            var toArr = to.EnumerateArray().ToList();
            if (fromArr.Count < 3 || toArr.Count < 3) continue;

            var fx = (int)MathF.Floor(fromArr[0].GetSingle());
            var fy = (int)MathF.Floor(fromArr[1].GetSingle());
            var fz = (int)MathF.Floor(fromArr[2].GetSingle());
            var tx = (int)MathF.Ceiling(toArr[0].GetSingle());
            var ty = (int)MathF.Ceiling(toArr[1].GetSingle());
            var tz = (int)MathF.Ceiling(toArr[2].GetSingle());

            minX = Math.Min(minX, fx); minY = Math.Min(minY, fy); minZ = Math.Min(minZ, fz);
            maxX = Math.Max(maxX, tx); maxY = Math.Max(maxY, ty); maxZ = Math.Max(maxZ, tz);
        }

        if (elements.Count == 0)
            return new VoxelGrid(new VoxelBounds(VoxelCoord.Zero, VoxelCoord.Zero));

        var bounds = new VoxelBounds(
            new VoxelCoord(minX, minY, minZ),
            new VoxelCoord(maxX - 1, maxY - 1, maxZ - 1));
        var palette = new Palette();
        var grid = new VoxelGrid(bounds, palette);

        // Second pass: fill voxel regions from elements
        foreach (var elem in elements)
        {
            if (!elem.TryGetProperty("from", out var from) || !elem.TryGetProperty("to", out var to))
                continue;

            var fromArr = from.EnumerateArray().ToList();
            var toArr = to.EnumerateArray().ToList();
            if (fromArr.Count < 3 || toArr.Count < 3) continue;

            var fx = (int)MathF.Floor(fromArr[0].GetSingle());
            var fy = (int)MathF.Floor(fromArr[1].GetSingle());
            var fz = (int)MathF.Floor(fromArr[2].GetSingle());
            var tx = (int)MathF.Ceiling(toArr[0].GetSingle());
            var ty = (int)MathF.Ceiling(toArr[1].GetSingle());
            var tz = (int)MathF.Ceiling(toArr[2].GetSingle());

            // Determine palette index from face textures.
            // Use the most common texture reference across faces for per-voxel color.
            var color = DetermineDominantColor(elem, textureColors);
            var paletteIndex = palette.GetOrAddIndex(color, MaterialType.Diffuse);

            // Fill the cuboid region
            for (var y = fy; y < ty; y++)
            for (var z = fz; z < tz; z++)
            for (var x = fx; x < tx; x++)
            {
                var coord = new VoxelCoord(x, y, z);
                if (bounds.Contains(coord))
                    grid.SetVoxel(coord, new Voxel(paletteIndex, VoxelFlags.None));
            }
        }

        // Extract outliner tags into metadata
        if (root.TryGetProperty("outliner", out var outliner))
        {
            grid.Metadata.Tags ??= new List<string>();
            ExtractOutlinerTags(outliner, grid.Metadata.Tags);
        }

        return grid;
    }

    /// <summary>
    /// Determines the dominant color for an element from its face texture references.
    /// When faces reference different textures, uses the most frequently referenced one.
    /// </summary>
    private static Color DetermineDominantColor(JsonElement element, Dictionary<int, Color> textureColors)
    {
        if (!element.TryGetProperty("faces", out var faces))
            return new Color(200, 200, 200); // Default gray

        var textureCounts = new Dictionary<int, int>();
        foreach (var face in faces.EnumerateObject())
        {
            if (face.Value.TryGetProperty("texture", out var texProp))
            {
                var texId = texProp.ValueKind == JsonValueKind.Number ? texProp.GetInt32() : 0;
                textureCounts[texId] = textureCounts.GetValueOrDefault(texId) + 1;
            }
        }

        if (textureCounts.Count == 0)
            return new Color(200, 200, 200);

        // Most common texture index across all faces
        var dominantTexture = textureCounts.MaxBy(kv => kv.Value).Key;
        return textureColors.GetValueOrDefault(dominantTexture, new Color(200, 200, 200));
    }

    /// <summary>
    /// Recursively extracts group names from the outliner hierarchy as metadata tags.
    /// </summary>
    private static void ExtractOutlinerTags(JsonElement outliner, List<string> tags)
    {
        foreach (var item in outliner.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Object && item.TryGetProperty("name", out var name))
            {
                var n = name.GetString();
                if (!string.IsNullOrEmpty(n))
                    tags.Add(n);

                if (item.TryGetProperty("children", out var children))
                    ExtractOutlinerTags(children, tags);
            }
        }
    }
}
