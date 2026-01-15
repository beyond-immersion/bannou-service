namespace BeyondImmersion.Bannou.AssetBundler.Extraction;

/// <summary>
/// Specific texture type hints for processing.
/// </summary>
public enum TextureType
{
    /// <summary>
    /// Color/diffuse/albedo texture.
    /// </summary>
    Color,

    /// <summary>
    /// Normal map.
    /// </summary>
    NormalMap,

    /// <summary>
    /// Emissive/glow texture.
    /// </summary>
    Emissive,

    /// <summary>
    /// Mask texture (metallic, roughness, AO, etc.).
    /// </summary>
    Mask,

    /// <summary>
    /// Height/displacement map.
    /// </summary>
    HeightMap,

    /// <summary>
    /// UI/sprite texture.
    /// </summary>
    UI
}
