namespace BeyondImmersion.Bannou.SpriteTheory.Metadata;

/// <summary>
/// Engine-agnostic container for captured pixel data produced by the bridge after each frame render.
/// Contains RGBA pixel data, optional depth buffer data, and metadata identifying which angle,
/// animation, and frame this capture represents.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="PixelData"/> contains RGBA pixels (4 bytes per pixel, row-major top-to-bottom).
/// </para>
/// <para>
/// <see cref="DepthData"/>, when present, contains depth values normalized to 0.0 (near plane)
/// through 1.0 (far plane). The bridge is responsible for normalizing GPU depth to this range,
/// as different graphics APIs store depth differently. Null if depth was not captured.
/// </para>
/// </remarks>
/// <param name="PixelData">RGBA pixels (4 bytes per pixel, row-major top-to-bottom).</param>
/// <param name="DepthData">Depth values 0.0–1.0 per pixel. Null if depth was not captured.</param>
/// <param name="Width">Frame width in pixels.</param>
/// <param name="Height">Frame height in pixels.</param>
/// <param name="AngleName">Which CaptureAngle this frame was rendered from.</param>
/// <param name="AnimationName">Which animation was playing during capture.</param>
/// <param name="FrameIndex">Frame number within the animation (0-based).</param>
/// <param name="NormalizedTime">Animation time when captured (0.0–1.0).</param>
public record FrameCapture(
    byte[] PixelData,
    float[]? DepthData,
    int Width,
    int Height,
    string AngleName,
    string AnimationName,
    int FrameIndex,
    float NormalizedTime);
