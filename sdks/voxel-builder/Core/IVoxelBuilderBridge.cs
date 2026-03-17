using BeyondImmersion.Bannou.VoxelCore.Grid;
using BeyondImmersion.Bannou.VoxelCore.Math;
using BeyondImmersion.Bannou.VoxelCore.Meshing;

namespace BeyondImmersion.Bannou.VoxelBuilder.Core;

/// <summary>
/// Engine rendering contract. Implemented per-engine (Stride, Godot, Unity).
/// Translates between SDK types (MeshData, VoxelCoord) and engine types.
/// </summary>
public interface IVoxelBuilderBridge : IDisposable
{
    /// <summary>Full grid replacement — re-mesh everything.</summary>
    /// <param name="grid">The new grid.</param>
    void OnGridLoaded(VoxelGrid grid);

    /// <summary>Incremental mesh update for modified chunks.</summary>
    /// <param name="coords">Set of chunk coordinates that need re-meshing.</param>
    void OnChunksModified(IReadOnlySet<ChunkCoord> coords);

    /// <summary>Palette has changed — rebuild palette texture atlas.</summary>
    /// <param name="palette">The updated palette.</param>
    void OnPaletteChanged(Palette palette);

    /// <summary>Convert screen-space position to voxel coordinate via raycasting.</summary>
    /// <param name="screenX">Screen X position.</param>
    /// <param name="screenY">Screen Y position.</param>
    /// <returns>The voxel coordinate under the cursor, or null if no hit.</returns>
    VoxelCoord? ScreenToVoxel(float screenX, float screenY);

    /// <summary>Swap the meshing strategy used for rendering.</summary>
    /// <param name="mesher">The new mesher to use.</param>
    void SetMesher(IMesher mesher);
}
