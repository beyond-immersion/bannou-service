using BeyondImmersion.Bannou.VoxelCore.Grid;
using BeyondImmersion.Bannou.VoxelCore.Serialization;

namespace BeyondImmersion.Bannou.VoxelBuilder.ImportExport;

/// <summary>
/// Exports VoxelGrid to raw .bvox byte format. Thin wrapper around
/// <see cref="VoxelSerializer.Serialize"/>. Used for Save-Load and Asset service integration.
/// </summary>
public sealed class RawVoxelExporter : IVoxelExporter
{
    /// <inheritdoc />
    public byte[] Export(VoxelGrid grid) => VoxelSerializer.Serialize(grid);
}
