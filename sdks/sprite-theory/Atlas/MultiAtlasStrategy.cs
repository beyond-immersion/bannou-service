namespace BeyondImmersion.Bannou.SpriteTheory.Atlas;

/// <summary>
/// Handles overflow when packed frames exceed the bounds of a single atlas.
/// Opens successive atlases as needed and validates that individual frames can
/// fit within the configured maximum atlas dimensions.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="AtlasPacker"/> calls this when the MaxRects-BSSF search on the current
/// atlas returns no usable free rectangle. The strategy validates that the frame
/// is small enough to fit in any atlas, then resets the free rectangle list so the
/// next placement attempt operates against a fresh, empty atlas.
/// </para>
/// <para>
/// Pre-validating before the reset means a frame that is too large to fit in any
/// atlas fails fast, without mutating the free rectangle list.
/// </para>
/// </remarks>
public static class MultiAtlasStrategy
{
    /// <summary>
    /// Opens the next atlas in response to an overflow in the current atlas.
    /// Validates that the padded frame dimensions fit within the maximum atlas bounds,
    /// resets the free rectangle list to a single rectangle covering the full new atlas,
    /// and returns both the new atlas index and the placement rectangle (a fresh atlas
    /// always places its first frame at the origin of the single remaining free rect).
    /// </summary>
    /// <param name="currentAtlasIndex">Index of the atlas that overflowed.</param>
    /// <param name="freeRects">Free rectangle list to reset (mutated in place).</param>
    /// <param name="maxWidth">Maximum atlas width from <see cref="AtlasOptions"/>.</param>
    /// <param name="maxHeight">Maximum atlas height from <see cref="AtlasOptions"/>.</param>
    /// <param name="paddedFrameWidth">Frame width plus padding from the current placement attempt.</param>
    /// <param name="paddedFrameHeight">Frame height plus padding from the current placement attempt.</param>
    /// <param name="frameIndex">Original frame index from the input list (for error reporting).</param>
    /// <param name="frameWidth">Unpadded frame width (for error reporting).</param>
    /// <param name="frameHeight">Unpadded frame height (for error reporting).</param>
    /// <returns>
    /// A tuple of the new atlas index (<paramref name="currentAtlasIndex"/> + 1) and the free
    /// rectangle the caller should use for placement (covers the full new atlas from origin).
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the padded frame dimensions exceed the maximum atlas dimensions.
    /// The frame cannot fit in any atlas at the given configuration.
    /// </exception>
    public static (int NewAtlasIndex, Rectangle Placement) OpenNextAtlas(
        int currentAtlasIndex,
        List<Rectangle> freeRects,
        int maxWidth,
        int maxHeight,
        int paddedFrameWidth,
        int paddedFrameHeight,
        int frameIndex,
        int frameWidth,
        int frameHeight)
    {
        if (paddedFrameWidth > maxWidth || paddedFrameHeight > maxHeight)
        {
            throw new InvalidOperationException(
                $"Frame (index={frameIndex}, {frameWidth}x{frameHeight}) exceeds maximum atlas dimensions ({maxWidth}x{maxHeight}).");
        }

        var fullAtlas = new Rectangle(0, 0, maxWidth, maxHeight);
        freeRects.Clear();
        freeRects.Add(fullAtlas);

        return (currentAtlasIndex + 1, fullAtlas);
    }
}
