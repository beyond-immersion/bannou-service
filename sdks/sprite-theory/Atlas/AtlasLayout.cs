namespace BeyondImmersion.Bannou.SpriteTheory.Atlas;

/// <summary>
/// The result of atlas packing via <see cref="AtlasPacker.Pack"/>. Contains per-frame
/// placements, per-atlas dimensions, atlas count, and overall packing efficiency.
/// </summary>
/// <param name="Placements">Per-frame atlas positions, one entry per input frame.</param>
/// <param name="AtlasWidths">Width of each atlas image in pixels (one entry per atlas).</param>
/// <param name="AtlasHeights">Height of each atlas image in pixels (one entry per atlas).</param>
/// <param name="AtlasCount">Number of atlas images produced (1 unless frames overflowed a single atlas).</param>
/// <param name="Efficiency">Packing efficiency as a ratio of total frame area to total atlas area (0.0–1.0).</param>
public record AtlasLayout(
    IReadOnlyList<PackedFrame> Placements,
    IReadOnlyList<int> AtlasWidths,
    IReadOnlyList<int> AtlasHeights,
    int AtlasCount,
    float Efficiency);
