using BeyondImmersion.Bannou.VoxelCore.Grid;
using BeyondImmersion.Bannou.VoxelCore.Serialization;

namespace BeyondImmersion.Bannou.VoxelBuilder.ImportExport;

/// <summary>
/// Imports VoxelGrid from raw .bvox byte format. Thin wrapper around
/// <see cref="VoxelSerializer.Deserialize"/>. Used for procedural output consumption.
/// </summary>
public sealed class RawVoxelImporter : IVoxelImporter
{
    /// <inheritdoc />
    public VoxelGrid Import(byte[] data) => VoxelSerializer.Deserialize(data);

    /// <inheritdoc />
    public VoxelGrid Import(Stream stream)
    {
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return VoxelSerializer.Deserialize(ms.ToArray());
    }
}
