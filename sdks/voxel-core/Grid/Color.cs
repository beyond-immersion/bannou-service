namespace BeyondImmersion.Bannou.VoxelCore.Grid;

/// <summary>
/// RGBA color stored as 4 bytes. Used by <see cref="Palette"/>.
/// Byte layout matches Stride's Color struct for zero-copy bridge conversion.
/// </summary>
/// <param name="R">Red channel (0-255).</param>
/// <param name="G">Green channel (0-255).</param>
/// <param name="B">Blue channel (0-255).</param>
/// <param name="A">Alpha channel (0-255). Defaults to 255 (fully opaque).</param>
public readonly record struct Color(byte R, byte G, byte B, byte A = 255)
{
    /// <summary>Black (0, 0, 0, 255).</summary>
    public static readonly Color Black = new(0, 0, 0);

    /// <summary>White (255, 255, 255, 255).</summary>
    public static readonly Color White = new(255, 255, 255);

    /// <summary>Transparent (0, 0, 0, 0).</summary>
    public static readonly Color Transparent = new(0, 0, 0, 0);
}
