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
    /// <b>Indexing contract:</b> <see cref="PackedFrame.FrameIndex"/> is the position of
    /// the frame in the <paramref name="frames"/> list — the same index the caller used
    /// when constructing input for <see cref="AtlasPacker.Pack"/> (typically
    /// <c>frames.Select((f, i) =&gt; (f.Width, f.Height, i))</c>). AtlasAssembler uses
    /// <c>frames[placement.FrameIndex]</c> to retrieve the source capture. This
    /// index is NOT <see cref="FrameCapture.FrameIndex"/>, which is the intra-animation
    /// frame number and is not unique across captures from multiple animations.
    /// </para>
    /// </remarks>
    /// <param name="frames">Captured frame pixel data from the engine bridge.</param>
    /// <param name="layout">Atlas layout computed by <see cref="AtlasPacker.Pack"/>.</param>
    /// <param name="backgroundColor">Background color to fill each atlas with before blitting frames.</param>
    /// <returns>Array of atlas byte arrays, one per atlas image. Each is RGBA (4 bytes/pixel, row-major).</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when a placement's <see cref="PackedFrame.FrameIndex"/> is outside the bounds
    /// of <paramref name="frames"/>. This indicates the layout was built from inputs that
    /// don't match the provided frames list — a caller bug.
    /// </exception>
    public static byte[][] Assemble(
        IReadOnlyList<FrameCapture> frames,
        AtlasLayout layout,
        Color backgroundColor)
    {
        var atlases = new byte[layout.AtlasCount][];

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

        // Blit each frame into its atlas at the placement position.
        // PackedFrame.FrameIndex indexes directly into the frames list — it is the
        // enumeration position the caller supplied to AtlasPacker.Pack, not the
        // intra-animation FrameCapture.FrameIndex.
        foreach (var placement in layout.Placements)
        {
            if (placement.FrameIndex < 0 || placement.FrameIndex >= frames.Count)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(layout),
                    $"Placement FrameIndex {placement.FrameIndex} is outside the bounds " +
                    $"of the frames list (count={frames.Count}). The layout must be built " +
                    $"from the same frames list passed to Assemble.");
            }

            var frame = frames[placement.FrameIndex];
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
