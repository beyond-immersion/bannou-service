namespace BeyondImmersion.Bannou.SpriteTheory.Export;

/// <summary>
/// Engine-agnostic abstraction for raw RGBA pixel data. Implemented by engine bridges
/// to provide captured frame pixels without coupling sprite-theory to any rendering API.
/// </summary>
/// <remarks>
/// Pixel data is RGBA (4 bytes per pixel), row-major from top-left to bottom-right.
/// Total byte count must equal <see cref="Width"/> × <see cref="Height"/> × 4.
/// </remarks>
public interface IPixelSource
{
    /// <summary>
    /// Gets the raw RGBA pixel data (4 bytes per pixel, row-major top-to-bottom).
    /// </summary>
    /// <returns>RGBA pixel array with length Width × Height × 4.</returns>
    byte[] GetPixels();

    /// <summary>
    /// Gets the width of the pixel data in pixels.
    /// </summary>
    int Width { get; }

    /// <summary>
    /// Gets the height of the pixel data in pixels.
    /// </summary>
    int Height { get; }
}
