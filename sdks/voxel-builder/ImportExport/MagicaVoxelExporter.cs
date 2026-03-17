using BeyondImmersion.Bannou.VoxelCore.Grid;
using BeyondImmersion.Bannou.VoxelCore.Math;

namespace BeyondImmersion.Bannou.VoxelBuilder.ImportExport;

/// <summary>
/// Exports a <see cref="VoxelGrid"/> to MagicaVoxel .vox format (RIFF binary).
/// Handles the Y-up → Z-up coordinate conversion (Bannou uses Y-up, MagicaVoxel uses Z-up).
/// Written from scratch — the RIFF structure is community-documented.
/// </summary>
public sealed class MagicaVoxelExporter : IVoxelExporter
{
    /// <summary>
    /// Export a VoxelGrid as .vox binary bytes.
    /// </summary>
    /// <param name="grid">The grid to export.</param>
    /// <returns>.vox file content bytes.</returns>
    public byte[] Export(VoxelGrid grid)
    {
        // Collect all non-empty voxels with coordinate swap (Bannou Y-up → MagicaVoxel Z-up)
        var voxels = new List<(byte x, byte y, byte z, byte index)>();
        foreach (var (chunkCoord, chunk) in grid.EnumerateChunks())
        {
            for (var ly = 0; ly < VoxelChunk.Size; ly++)
            for (var lz = 0; lz < VoxelChunk.Size; lz++)
            for (var lx = 0; lx < VoxelChunk.Size; lx++)
            {
                var palIdx = chunk.PaletteIndices[VoxelChunk.GetFlatIndex(lx, ly, lz)];
                if (palIdx == 0) continue;

                var wx = chunkCoord.X * 16 + lx - grid.Bounds.Min.X;
                var wy = chunkCoord.Y * 16 + ly - grid.Bounds.Min.Y;
                var wz = chunkCoord.Z * 16 + lz - grid.Bounds.Min.Z;

                // MagicaVoxel coordinates are 0-255. Validate bounds.
                if (wx < 0 || wx > 255 || wy < 0 || wy > 255 || wz < 0 || wz > 255)
                    continue;

                // Bannou (x, y, z) Y-up → MagicaVoxel (x, z, y) Z-up
                voxels.Add(((byte)wx, (byte)wz, (byte)wy, palIdx));
            }
        }

        // MagicaVoxel dimensions: swap Y/Z
        var sizeX = Math.Min(grid.Bounds.Width, 256);
        var sizeY = Math.Min(grid.Bounds.Depth, 256);  // Bannou Z → MV Y
        var sizeZ = Math.Min(grid.Bounds.Height, 256);  // Bannou Y → MV Z

        // Build RIFF chunks
        using var childBuffer = new MemoryStream();
        using var childWriter = new BinaryWriter(childBuffer);

        // SIZE chunk
        WriteChunkHeader(childWriter, "SIZE", 12, 0);
        childWriter.Write(sizeX);
        childWriter.Write(sizeY);
        childWriter.Write(sizeZ);

        // XYZI chunk
        var xyziContentSize = 4 + voxels.Count * 4;
        WriteChunkHeader(childWriter, "XYZI", xyziContentSize, 0);
        childWriter.Write(voxels.Count);
        foreach (var (vx, vy, vz, vi) in voxels)
        {
            childWriter.Write(vx);
            childWriter.Write(vy);
            childWriter.Write(vz);
            childWriter.Write(vi);
        }

        // RGBA chunk (256 entries × 4 bytes = 1024 bytes)
        WriteChunkHeader(childWriter, "RGBA", 1024, 0);
        for (var i = 0; i < 256; i++)
        {
            // MagicaVoxel stores entries 1-255 then unused entry 0
            var palIdx = (byte)(i < 255 ? i + 1 : 0);
            var entry = palIdx > 0 && palIdx <= grid.Palette.UsedCount
                ? grid.Palette.Get(palIdx)
                : PaletteEntry.Empty;
            childWriter.Write(entry.Color.R);
            childWriter.Write(entry.Color.G);
            childWriter.Write(entry.Color.B);
            childWriter.Write(entry.Color.A);
        }

        // MATL chunks for non-default materials
        for (var i = 1; i <= grid.Palette.UsedCount; i++)
        {
            var entry = grid.Palette.Get((byte)i);
            if (entry.Material == MaterialType.Diffuse && Math.Abs(entry.Roughness - 0.5f) < 0.01f)
                continue; // Default material — skip

            var props = new Dictionary<string, string>();
            props["_type"] = entry.Material switch
            {
                MaterialType.Metal => "_metal",
                MaterialType.Glass => "_glass",
                MaterialType.Emit => "_emit",
                MaterialType.Cloud => "_blend",
                _ => "_diffuse"
            };
            if (Math.Abs(entry.Roughness - 0.5f) > 0.01f)
                props["_rough"] = entry.Roughness.ToString("F3");

            // Compute MATL chunk content size
            var matlContentSize = 4 + 4; // matId + propCount
            foreach (var (key, value) in props)
                matlContentSize += 4 + key.Length + 4 + value.Length;

            WriteChunkHeader(childWriter, "MATL", matlContentSize, 0);
            childWriter.Write(i); // material ID
            childWriter.Write(props.Count);
            foreach (var (key, value) in props)
            {
                WriteMagicaVoxelString(childWriter, key);
                WriteMagicaVoxelString(childWriter, value);
            }
        }

        // Assemble file
        using var output = new MemoryStream();
        using var writer = new BinaryWriter(output);

        // File header
        writer.Write("VOX ".ToCharArray());
        writer.Write(150); // version

        // MAIN chunk
        var childBytes = childBuffer.ToArray();
        WriteChunkHeader(writer, "MAIN", 0, childBytes.Length);
        writer.Write(childBytes);

        return output.ToArray();
    }

    private static void WriteChunkHeader(BinaryWriter writer, string id, int contentSize, int childrenSize)
    {
        writer.Write(id.ToCharArray());
        writer.Write(contentSize);
        writer.Write(childrenSize);
    }

    private static void WriteMagicaVoxelString(BinaryWriter writer, string value)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(value);
        writer.Write(bytes.Length);
        writer.Write(bytes);
    }
}
