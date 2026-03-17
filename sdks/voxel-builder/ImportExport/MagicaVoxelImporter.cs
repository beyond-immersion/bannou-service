using BeyondImmersion.Bannou.VoxelCore.Grid;
using BeyondImmersion.Bannou.VoxelCore.Math;

namespace BeyondImmersion.Bannou.VoxelBuilder.ImportExport;

/// <summary>
/// Imports MagicaVoxel .vox files (RIFF binary) into <see cref="VoxelGrid"/>.
/// Parses XYZI (voxel positions), RGBA (palette), and MATL (material properties) chunks.
/// Handles the Z-up → Y-up coordinate conversion (MagicaVoxel uses Z-up, Bannou uses Y-up).
/// Written from scratch — the RIFF structure is community-documented.
/// </summary>
public sealed class MagicaVoxelImporter : IVoxelImporter
{
    /// <summary>
    /// Import a .vox file from bytes.
    /// </summary>
    /// <param name="data">File content bytes.</param>
    /// <returns>A VoxelGrid populated from the MagicaVoxel model.</returns>
    public VoxelGrid Import(byte[] data)
    {
        using var ms = new MemoryStream(data);
        return Import(ms);
    }

    /// <summary>
    /// Import a .vox file from a stream.
    /// </summary>
    /// <param name="stream">File content stream.</param>
    /// <returns>A VoxelGrid populated from the MagicaVoxel model.</returns>
    public VoxelGrid Import(Stream stream)
    {
        using var reader = new BinaryReader(stream);

        // File header
        var magic = new string(reader.ReadChars(4));
        if (magic != "VOX ")
            throw new FormatException("Invalid .vox file: bad magic bytes");

        var version = reader.ReadInt32(); // Typically 150+

        // Parse MAIN chunk
        var mainId = new string(reader.ReadChars(4));
        if (mainId != "MAIN")
            throw new FormatException("Invalid .vox file: expected MAIN chunk");

        var mainContentSize = reader.ReadInt32();
        var mainChildrenSize = reader.ReadInt32();

        // Parse child chunks
        int sizeX = 0, sizeY = 0, sizeZ = 0;
        var voxels = new List<(byte x, byte y, byte z, byte index)>();
        var palette = new Palette();
        var hasPalette = false;
        var materials = new Dictionary<int, (MaterialType type, float roughness)>();

        var endPosition = stream.Position + mainChildrenSize;
        while (stream.Position < endPosition)
        {
            var chunkId = new string(reader.ReadChars(4));
            var contentSize = reader.ReadInt32();
            var childrenSize = reader.ReadInt32();
            var chunkEnd = stream.Position + contentSize;

            switch (chunkId)
            {
                case "SIZE":
                    sizeX = reader.ReadInt32();
                    sizeY = reader.ReadInt32();
                    sizeZ = reader.ReadInt32();
                    break;

                case "XYZI":
                    var numVoxels = reader.ReadInt32();
                    for (var i = 0; i < numVoxels; i++)
                    {
                        var vx = reader.ReadByte();
                        var vy = reader.ReadByte();
                        var vz = reader.ReadByte();
                        var vi = reader.ReadByte();
                        voxels.Add((vx, vy, vz, vi));
                    }
                    break;

                case "RGBA":
                    hasPalette = true;
                    // MagicaVoxel palette: 256 entries, index 0 is unused (last entry is index 255)
                    // The file stores entries 1-255 then entry 0 (which is always empty)
                    for (var i = 0; i < 256; i++)
                    {
                        var r = reader.ReadByte();
                        var g = reader.ReadByte();
                        var b = reader.ReadByte();
                        var a = reader.ReadByte();
                        // MagicaVoxel entry i in file = our palette index i+1 (shifted by 1)
                        // But actually the standard mapping is: file entry 0 = palette 1, ..., file entry 254 = palette 255
                        if (i < 255)
                            palette.Set((byte)(i + 1), new PaletteEntry(new Color(r, g, b, a), MaterialType.Diffuse, 0.5f));
                    }
                    break;

                case "MATL":
                    var matId = reader.ReadInt32();
                    var propCount = reader.ReadInt32();
                    var matType = MaterialType.Diffuse;
                    var roughness = 0.5f;
                    for (var i = 0; i < propCount; i++)
                    {
                        var key = ReadMagicaVoxelString(reader);
                        var value = ReadMagicaVoxelString(reader);
                        switch (key)
                        {
                            case "_type":
                                matType = value switch
                                {
                                    "_metal" => MaterialType.Metal,
                                    "_glass" => MaterialType.Glass,
                                    "_emit" => MaterialType.Emit,
                                    "_blend" => MaterialType.Cloud,
                                    _ => MaterialType.Diffuse
                                };
                                break;
                            case "_rough":
                                float.TryParse(value, out roughness);
                                break;
                        }
                    }
                    materials[matId] = (matType, roughness);
                    break;

                default:
                    // Skip unknown chunks (nTRN, nGRP, nSHP, etc.)
                    break;
            }

            // Ensure we're at the end of this chunk (skip any unread content + children)
            stream.Position = chunkEnd + childrenSize;
        }

        // If no explicit palette was provided, use a default grayscale
        if (!hasPalette)
        {
            for (byte i = 1; i <= 255; i++)
                palette.Set(i, new PaletteEntry(new Color(i, i, i), MaterialType.Diffuse, 0.5f));
        }

        // Apply material properties to palette entries
        foreach (var (matId, (matType, roughness)) in materials)
        {
            if (matId >= 1 && matId <= 255)
            {
                var existing = palette.Get((byte)matId);
                palette.Set((byte)matId, new PaletteEntry(existing.Color, matType, roughness));
            }
        }

        // Build grid — swap Y/Z for coordinate system conversion (MagicaVoxel Z-up → Bannou Y-up)
        var bounds = new VoxelBounds(
            VoxelCoord.Zero,
            new VoxelCoord(sizeX - 1, sizeZ - 1, sizeY - 1)); // swap Y/Z in bounds
        var grid = new VoxelGrid(bounds, palette);

        foreach (var (vx, vy, vz, vi) in voxels)
        {
            // MagicaVoxel (x, y, z) Z-up → Bannou (x, z, y) Y-up
            var coord = new VoxelCoord(vx, vz, vy);
            if (bounds.Contains(coord))
                grid.SetVoxel(coord, new Voxel(vi, VoxelFlags.None));
        }

        return grid;
    }

    /// <summary>
    /// Import a multi-model .vox file with scene graph transforms.
    /// Returns each model as a separate VoxelGrid with its relative transform.
    /// </summary>
    /// <param name="data">File content bytes.</param>
    /// <returns>List of (VoxelGrid, relative position offset) tuples.</returns>
    public IReadOnlyList<(VoxelGrid Grid, VoxelCoord Offset)> ImportMulti(byte[] data)
    {
        // For now, single-model import wrapped in a list.
        // Full scene graph parsing (nTRN/nGRP/nSHP) for multi-model support
        // would iterate the transform tree and instantiate separate grids.
        var grid = Import(data);
        return new[] { (grid, VoxelCoord.Zero) };
    }

    private static string ReadMagicaVoxelString(BinaryReader reader)
    {
        var length = reader.ReadInt32();
        var bytes = reader.ReadBytes(length);
        return System.Text.Encoding.UTF8.GetString(bytes);
    }
}
