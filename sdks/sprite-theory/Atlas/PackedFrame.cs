namespace BeyondImmersion.Bannou.SpriteTheory.Atlas;

/// <summary>
/// Represents a single frame's placement within an atlas image, as determined by <see cref="AtlasPacker"/>.
/// </summary>
/// <param name="FrameIndex">Original frame index from the input list.</param>
/// <param name="AtlasIndex">Which atlas image this frame was placed in (0-based).</param>
/// <param name="X">Horizontal position of the frame's top-left corner in the atlas, in pixels.</param>
/// <param name="Y">Vertical position of the frame's top-left corner in the atlas, in pixels.</param>
/// <param name="Width">Frame width in pixels (without padding).</param>
/// <param name="Height">Frame height in pixels (without padding).</param>
public record PackedFrame(
    int FrameIndex,
    int AtlasIndex,
    int X,
    int Y,
    int Width,
    int Height);
