namespace BeyondImmersion.Bannou.SpriteTheory.Export;

/// <summary>
/// Engine-agnostic abstraction for depth buffer data. Implemented by engine bridges
/// to provide captured depth values without coupling sprite-theory to any rendering API.
/// </summary>
/// <remarks>
/// Depth values are normalized to 0.0 (near plane) through 1.0 (far plane).
/// The bridge is responsible for normalizing GPU depth to this range, as different
/// graphics APIs store depth differently. Total float count must equal
/// <see cref="Width"/> × <see cref="Height"/>.
/// </remarks>
public interface IDepthSource
{
    /// <summary>
    /// Gets the depth buffer data as normalized float values (0.0–1.0 per pixel).
    /// </summary>
    /// <returns>Depth array with length Width × Height.</returns>
    float[] GetDepth();

    /// <summary>
    /// Gets the width of the depth data in pixels.
    /// </summary>
    int Width { get; }

    /// <summary>
    /// Gets the height of the depth data in pixels.
    /// </summary>
    int Height { get; }
}
