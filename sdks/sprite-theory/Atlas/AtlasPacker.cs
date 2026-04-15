namespace BeyondImmersion.Bannou.SpriteTheory.Atlas;

/// <summary>
/// Packs variable-sized frame rectangles into one or more atlas images using the
/// MaxRects-BSSF (Best Short Side Fit) bin-packing algorithm.
/// </summary>
/// <remarks>
/// <para>
/// The algorithm maintains a list of free rectangles representing unoccupied atlas regions.
/// For each frame, the free rectangle with the smallest short-side remainder is selected (BSSF heuristic).
/// After placement, the free space is subdivided into up to four new rectangles, and
/// fully-contained duplicates are pruned.
/// </para>
/// <para>
/// When no free rectangle can fit the current frame, a new atlas is started transparently.
/// The result includes per-atlas dimensions (optionally rounded to power-of-two) and overall
/// packing efficiency.
/// </para>
/// <para>
/// Reference: Jukka Jylänki, "A Thousand Ways to Pack the Bin" (2010).
/// </para>
/// </remarks>
public static class AtlasPacker
{
    /// <summary>
    /// Packs a list of frames into one or more atlas images using MaxRects-BSSF.
    /// </summary>
    /// <param name="frames">
    /// Input frames to pack. Each tuple contains the frame's width, height, and original index.
    /// </param>
    /// <param name="options">Atlas packing configuration (max size, padding, power-of-two, etc.).</param>
    /// <returns>An <see cref="AtlasLayout"/> containing per-frame placements and per-atlas dimensions.</returns>
    public static AtlasLayout Pack(IReadOnlyList<(int Width, int Height, int Index)> frames, AtlasOptions options)
    {
        // Sort by height desc, width desc, index asc (stable deterministic order)
        var sorted = frames
            .OrderByDescending(f => f.Height)
            .ThenByDescending(f => f.Width)
            .ThenBy(f => f.Index)
            .ToList();

        var allPlacements = new List<PackedFrame>();
        var currentAtlas = 0;
        var freeRects = new List<Rectangle>
        {
            new(0, 0, options.MaxWidth, options.MaxHeight)
        };

        foreach (var frame in sorted)
        {
            var pw = frame.Width + options.Padding;
            var ph = frame.Height + options.Padding;

            // Best Short Side Fit: find free rect with smallest short-side remainder
            var bestRect = FindBestRect(freeRects, pw, ph);

            if (bestRect is null)
            {
                // Multi-atlas overflow: start new atlas
                currentAtlas++;
                freeRects.Clear();
                freeRects.Add(new Rectangle(0, 0, options.MaxWidth, options.MaxHeight));

                // Retry BSSF on fresh atlas (guaranteed success for any frame <= MaxSize)
                bestRect = FindBestRect(freeRects, pw, ph);

                if (bestRect is null)
                {
                    throw new InvalidOperationException(
                        $"Frame (index={frame.Index}, {frame.Width}x{frame.Height}) exceeds maximum atlas dimensions ({options.MaxWidth}x{options.MaxHeight}).");
                }
            }

            var best = bestRect.Value;

            // Place frame at bestRect position
            allPlacements.Add(new PackedFrame(
                frame.Index, currentAtlas, best.X, best.Y, frame.Width, frame.Height));

            // MaxRects subdivision: split remaining free space around placed rectangle
            var placed = new Rectangle(best.X, best.Y, pw, ph);
            var newFreeRects = new List<Rectangle>();

            foreach (var rect in freeRects)
            {
                if (!Intersects(rect, placed))
                {
                    newFreeRects.Add(rect);
                    continue;
                }

                // Generate up to 4 remainder rectangles
                if (placed.X > rect.X)
                {
                    newFreeRects.Add(new Rectangle(
                        rect.X, rect.Y, placed.X - rect.X, rect.Height));
                }

                if (placed.X + pw < rect.X + rect.Width)
                {
                    newFreeRects.Add(new Rectangle(
                        placed.X + pw, rect.Y, rect.X + rect.Width - placed.X - pw, rect.Height));
                }

                if (placed.Y > rect.Y)
                {
                    newFreeRects.Add(new Rectangle(
                        rect.X, rect.Y, rect.Width, placed.Y - rect.Y));
                }

                if (placed.Y + ph < rect.Y + rect.Height)
                {
                    newFreeRects.Add(new Rectangle(
                        rect.X, placed.Y + ph, rect.Width, rect.Y + rect.Height - placed.Y - ph));
                }
            }

            // Prune: remove free rects fully contained within another free rect
            freeRects = PruneFreeRects(newFreeRects);
        }

        // Compute actual atlas dimensions per atlas
        var atlasCount = currentAtlas + 1;
        var atlasWidths = new int[atlasCount];
        var atlasHeights = new int[atlasCount];

        for (var i = 0; i < atlasCount; i++)
        {
            var maxX = 0;
            var maxY = 0;

            foreach (var p in allPlacements)
            {
                if (p.AtlasIndex != i)
                    continue;

                var right = p.X + p.Width;
                var bottom = p.Y + p.Height;

                if (right > maxX) maxX = right;
                if (bottom > maxY) maxY = bottom;
            }

            atlasWidths[i] = options.PowerOfTwo ? NextPowerOfTwo(maxX) : maxX;
            atlasHeights[i] = options.PowerOfTwo ? NextPowerOfTwo(maxY) : maxY;
        }

        var totalFrameArea = 0L;
        foreach (var f in frames)
            totalFrameArea += (long)f.Width * f.Height;

        var totalAtlasArea = 0L;
        for (var i = 0; i < atlasCount; i++)
            totalAtlasArea += (long)atlasWidths[i] * atlasHeights[i];

        var efficiency = totalAtlasArea > 0
            ? (float)totalFrameArea / totalAtlasArea
            : 0f;

        return new AtlasLayout(
            allPlacements,
            atlasWidths,
            atlasHeights,
            atlasCount,
            efficiency);
    }

    /// <summary>
    /// Finds the free rectangle with the Best Short Side Fit for the given padded dimensions.
    /// </summary>
    private static Rectangle? FindBestRect(List<Rectangle> freeRects, int pw, int ph)
    {
        Rectangle? bestRect = null;
        var bestShortSide = int.MaxValue;
        var bestLongSide = int.MaxValue;

        foreach (var rect in freeRects)
        {
            if (pw > rect.Width || ph > rect.Height)
                continue;

            var shortSide = Math.Min(rect.Width - pw, rect.Height - ph);
            var longSide = Math.Max(rect.Width - pw, rect.Height - ph);

            if (shortSide < bestShortSide || (shortSide == bestShortSide && longSide < bestLongSide))
            {
                bestRect = rect;
                bestShortSide = shortSide;
                bestLongSide = longSide;
            }
        }

        return bestRect;
    }

    /// <summary>
    /// Removes any free rectangle that is fully contained within another free rectangle.
    /// </summary>
    private static List<Rectangle> PruneFreeRects(List<Rectangle> rects)
    {
        var result = new List<Rectangle>(rects.Count);

        for (var i = 0; i < rects.Count; i++)
        {
            var contained = false;

            for (var j = 0; j < rects.Count; j++)
            {
                if (i == j) continue;

                if (IsContainedIn(rects[i], rects[j]))
                {
                    contained = true;
                    break;
                }
            }

            if (!contained)
                result.Add(rects[i]);
        }

        return result;
    }

    /// <summary>
    /// Tests whether rectangle <paramref name="inner"/> is fully contained within rectangle <paramref name="outer"/>.
    /// </summary>
    private static bool IsContainedIn(Rectangle inner, Rectangle outer)
    {
        return inner.X >= outer.X
            && inner.Y >= outer.Y
            && inner.X + inner.Width <= outer.X + outer.Width
            && inner.Y + inner.Height <= outer.Y + outer.Height;
    }

    /// <summary>
    /// Tests whether two rectangles overlap (share any area).
    /// </summary>
    private static bool Intersects(Rectangle a, Rectangle b)
    {
        return a.X < b.X + b.Width
            && a.X + a.Width > b.X
            && a.Y < b.Y + b.Height
            && a.Y + a.Height > b.Y;
    }

    /// <summary>
    /// Returns the smallest power of two that is greater than or equal to the given value.
    /// </summary>
    private static int NextPowerOfTwo(int value)
    {
        if (value <= 0) return 1;

        value--;
        value |= value >> 1;
        value |= value >> 2;
        value |= value >> 4;
        value |= value >> 8;
        value |= value >> 16;
        value++;

        return value;
    }
}
