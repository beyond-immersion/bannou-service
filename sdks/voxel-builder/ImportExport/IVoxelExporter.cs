using BeyondImmersion.Bannou.VoxelCore.Grid;

namespace BeyondImmersion.Bannou.VoxelBuilder.ImportExport;

/// <summary>
/// Format export contract: VoxelGrid to bytes/stream.
/// </summary>
public interface IVoxelExporter
{
    /// <summary>Export a VoxelGrid to bytes in the target format.</summary>
    /// <param name="grid">The grid to export.</param>
    /// <returns>File content bytes.</returns>
    byte[] Export(VoxelGrid grid);
}
