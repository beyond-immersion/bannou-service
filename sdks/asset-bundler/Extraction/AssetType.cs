namespace BeyondImmersion.Bannou.AssetBundler.Extraction;

/// <summary>
/// General category of an asset.
/// </summary>
public enum AssetType
{
    /// <summary>
    /// 3D model (FBX, GLB, GLTF, OBJ, etc.).
    /// </summary>
    Model,

    /// <summary>
    /// Texture/image (PNG, JPG, TGA, DDS, etc.).
    /// </summary>
    Texture,

    /// <summary>
    /// Audio file (WAV, OGG, MP3, etc.).
    /// </summary>
    Audio,

    /// <summary>
    /// Animation data.
    /// </summary>
    Animation,

    /// <summary>
    /// Material definition.
    /// </summary>
    Material,

    /// <summary>
    /// Behavior definition (YAML, JSON).
    /// </summary>
    Behavior,

    /// <summary>
    /// Other/unknown asset type.
    /// </summary>
    Other
}
