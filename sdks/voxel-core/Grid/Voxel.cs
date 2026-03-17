namespace BeyondImmersion.Bannou.VoxelCore.Grid;

/// <summary>
/// Individual voxel: palette index (1 byte) + flags (1 byte). 2-byte value type
/// designed for zero-allocation pass-by-value in hot paths.
/// </summary>
/// <param name="PaletteIndex">Palette index. 0 = empty (air), 1-255 = palette entry.</param>
/// <param name="Flags">Per-voxel property flags.</param>
public readonly record struct Voxel(byte PaletteIndex, VoxelFlags Flags)
{
    /// <summary>
    /// An empty voxel (palette index 0, no flags).
    /// </summary>
    public static readonly Voxel Empty = new(0, VoxelFlags.None);

    /// <summary>
    /// Whether this voxel is empty (palette index is 0).
    /// </summary>
    public bool IsEmpty => PaletteIndex == 0;
}
