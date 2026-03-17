using BeyondImmersion.Bannou.VoxelCore.Grid;
using BeyondImmersion.Bannou.VoxelCore.Math;

namespace BeyondImmersion.Bannou.VoxelBuilder.Core;

/// <summary>
/// Stores copied voxel data for paste operations. Voxels are stored in relative
/// coordinates (from the selection min corner) with a palette snapshot for cross-grid paste.
/// </summary>
public sealed class VoxelClipboard
{
    /// <summary>Voxels stored by relative coordinate from the selection origin.</summary>
    public Dictionary<VoxelCoord, Voxel> Voxels { get; } = new();

    /// <summary>Bounding box of the copied region.</summary>
    public VoxelBounds Bounds { get; set; }

    /// <summary>
    /// Palette entries used by copied voxels, indexed by original palette index.
    /// Used for cross-grid palette merging during paste.
    /// </summary>
    public Dictionary<byte, PaletteEntry> PaletteSnapshot { get; } = new();
}
