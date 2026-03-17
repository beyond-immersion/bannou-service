namespace BeyondImmersion.Bannou.VoxelCore.Grid;

/// <summary>
/// A single entry in the 256-entry palette: color, material type, and roughness.
/// </summary>
/// <param name="Color">RGBA color.</param>
/// <param name="Material">Material type (Diffuse, Metal, Glass, Emit, Cloud).</param>
/// <param name="Roughness">Surface roughness (0.0 = smooth/mirror, 1.0 = rough/matte).</param>
public readonly record struct PaletteEntry(Color Color, MaterialType Material, float Roughness)
{
    /// <summary>
    /// An empty palette entry (transparent, diffuse, zero roughness).
    /// </summary>
    public static readonly PaletteEntry Empty = new(Color.Transparent, MaterialType.Diffuse, 0f);
}
