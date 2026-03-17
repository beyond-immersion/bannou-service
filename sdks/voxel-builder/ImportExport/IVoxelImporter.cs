using BeyondImmersion.Bannou.VoxelCore.Grid;

namespace BeyondImmersion.Bannou.VoxelBuilder.ImportExport;

/// <summary>
/// Format import contract: bytes/stream to VoxelGrid.
/// </summary>
public interface IVoxelImporter
{
    /// <summary>Import voxel data from bytes.</summary>
    /// <param name="data">File content bytes.</param>
    /// <returns>The imported VoxelGrid.</returns>
    VoxelGrid Import(byte[] data);

    /// <summary>Import voxel data from a stream.</summary>
    /// <param name="stream">File content stream.</param>
    /// <returns>The imported VoxelGrid.</returns>
    VoxelGrid Import(Stream stream);
}
