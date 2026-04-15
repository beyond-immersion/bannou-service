namespace BeyondImmersion.Bannou.SpriteTheory.Metadata;

/// <summary>
/// Describes a single atlas image within a sprite sheet. When frames overflow a single atlas
/// (exceeding <see cref="Atlas.AtlasOptions.MaxWidth"/> x <see cref="Atlas.AtlasOptions.MaxHeight"/>),
/// multiple atlas images are produced, each tracked by an AtlasInfo entry.
/// </summary>
/// <param name="Index">0-based atlas index. Corresponds to <see cref="SpriteFrame.AtlasIndex"/>.</param>
/// <param name="Filename">Output filename for this atlas image (e.g., "warrior_atlas_0.png").</param>
/// <param name="Width">Atlas width in pixels.</param>
/// <param name="Height">Atlas height in pixels.</param>
public record AtlasInfo(
    int Index,
    string Filename,
    int Width,
    int Height);
