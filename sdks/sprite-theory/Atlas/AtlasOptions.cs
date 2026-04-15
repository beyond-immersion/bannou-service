namespace BeyondImmersion.Bannou.SpriteTheory.Atlas;

/// <summary>
/// Configuration options for atlas packing via <see cref="AtlasPacker"/>.
/// Controls maximum atlas dimensions, inter-frame padding, power-of-two rounding,
/// and animation grouping hints.
/// </summary>
/// <param name="MaxWidth">Maximum atlas width in pixels.</param>
/// <param name="MaxHeight">Maximum atlas height in pixels.</param>
/// <param name="Padding">Pixels of spacing between frames in the atlas.</param>
/// <param name="PowerOfTwo">Whether to round atlas dimensions up to the next power of two for GPU compatibility.</param>
/// <param name="GroupByAnimation">Visual row grouping hint — best-effort, not enforced by the packer.</param>
public record AtlasOptions(
    int MaxWidth = 4096,
    int MaxHeight = 4096,
    int Padding = 2,
    bool PowerOfTwo = true,
    bool GroupByAnimation = true);
