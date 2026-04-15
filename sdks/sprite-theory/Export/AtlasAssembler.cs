using BeyondImmersion.Bannou.SpriteTheory.Atlas;
using BeyondImmersion.Bannou.SpriteTheory.Metadata;

namespace BeyondImmersion.Bannou.SpriteTheory.Export;

/// <summary>
/// Composites captured frame pixel data into atlas RGBA byte arrays at positions
/// determined by <see cref="AtlasPacker"/>. Each atlas is a flat RGBA byte array
/// (4 bytes per pixel, row-major) ready for PNG encoding by the consumer.
/// </summary>
public static class AtlasAssembler
{
    /// <summary>
    /// Assembles captured frame pixel data into one or more atlas byte arrays using
    /// the placement positions from an <see cref="AtlasLayout"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each atlas is initialized with the specified background color, then frame pixels
    /// are blitted in row-by-row using <see cref="Buffer.BlockCopy"/> for efficiency.
    /// </para>
    /// <para>
    /// Frame captures are matched to placements by <see cref="PackedFrame.FrameIndex"/>
    /// via dictionary lookup, supporting any ordering of input frames.
    /// </para>
    /// </remarks>
    /// <param name="frames">Captured frame pixel data from the engine bridge.</param>
    /// <param name="layout">Atlas layout computed by <see cref="AtlasPacker.Pack"/>.</param>
    /// <param name="backgroundColor">Background color to fill each atlas with before blitting frames.</param>
    /// <returns>Array of atlas byte arrays, one per atlas image. Each is RGBA (4 bytes/pixel, row-major).</returns>
    public static byte[][] Assemble(
        IReadOnlyList<FrameCapture> frames,
        AtlasLayout layout,
        Color backgroundColor)
    {
        var atlases = new byte[layout.AtlasCount][];

        // Build lookup from FrameIndex to FrameCapture for O(1) access
        var frameLookup = new Dictionary<int, FrameCapture>(frames.Count);
        for (var i = 0; i < frames.Count; i++)
        {
            frameLookup[frames[i].FrameIndex] = frames[i];
        }

        // Allocate and fill each atlas with background color
        for (var i = 0; i < layout.AtlasCount; i++)
        {
            var w = layout.AtlasWidths[i];
            var h = layout.AtlasHeights[i];
            var atlas = new byte[w * h * 4];

            for (var p = 0; p < w * h; p++)
            {
                atlas[p * 4 + 0] = backgroundColor.R;
                atlas[p * 4 + 1] = backgroundColor.G;
                atlas[p * 4 + 2] = backgroundColor.B;
                atlas[p * 4 + 3] = backgroundColor.A;
            }

            atlases[i] = atlas;
        }

        // Blit each frame into its atlas at the placement position
        foreach (var placement in layout.Placements)
        {
            if (!frameLookup.TryGetValue(placement.FrameIndex, out var frame))
            {
                continue;
            }

            var atlas = atlases[placement.AtlasIndex];
            var atlasW = layout.AtlasWidths[placement.AtlasIndex];

            for (var row = 0; row < frame.Height; row++)
            {
                var src = row * frame.Width * 4;
                var dst = ((placement.Y + row) * atlasW + placement.X) * 4;
                Buffer.BlockCopy(frame.PixelData, src, atlas, dst, frame.Width * 4);
            }
        }

        return atlases;
    }
}
