using BeyondImmersion.Bannou.VoxelCore.Grid;

namespace BeyondImmersion.Bannou.VoxelCore.Voxelization;

/// <summary>
/// Configuration for voxelization operations.
/// </summary>
/// <param name="VoxelScale">World units per voxel (default 0.25).</param>
/// <param name="FillMode">Surface shell only, or solid-filled interior (default Solid).</param>
/// <param name="FrozenBorderWidth">Voxels from grid edge to mark as Frozen (default 1).</param>
/// <param name="DefaultPaletteIndex">Fallback material when source has no color data.</param>
public sealed record VoxelizationOptions(
    float VoxelScale = 0.25f,
    VoxelFillMode FillMode = VoxelFillMode.Solid,
    int FrozenBorderWidth = 1,
    byte DefaultPaletteIndex = 1)
{
    /// <summary>Default voxelization options.</summary>
    public static readonly VoxelizationOptions Default = new();
}
