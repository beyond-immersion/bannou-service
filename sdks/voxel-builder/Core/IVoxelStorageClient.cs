using BeyondImmersion.Bannou.VoxelCore.Grid;

namespace BeyondImmersion.Bannou.VoxelBuilder.Core;

/// <summary>
/// Optional persistence integration for saving/loading voxel grids.
/// Implemented by plugins that connect to Save-Load or Asset services.
/// </summary>
public interface IVoxelStorageClient
{
    /// <summary>Save the current grid state.</summary>
    /// <param name="grid">The grid to save.</param>
    /// <param name="ct">Cancellation token.</param>
    Task SaveAsync(VoxelGrid grid, CancellationToken ct = default);

    /// <summary>Load a grid from storage.</summary>
    /// <param name="identifier">Storage identifier (asset ID, save slot, etc.).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The loaded grid, or null if not found.</returns>
    Task<VoxelGrid?> LoadAsync(string identifier, CancellationToken ct = default);
}
