namespace BeyondImmersion.Bannou.AssetBundler.Godot;

/// <summary>
/// MIME content types for assets that Godot can load at runtime.
/// These are standard formats that Godot's buffer-based APIs support.
/// </summary>
public static class GodotContentTypes
{
    #region Textures

    /// <summary>PNG image format. Loaded via Image.LoadPngFromBuffer().</summary>
    public const string TexturePng = "image/png";

    /// <summary>JPEG image format. Loaded via Image.LoadJpgFromBuffer().</summary>
    public const string TextureJpeg = "image/jpeg";

    /// <summary>WebP image format. Loaded via Image.LoadWebpFromBuffer().</summary>
    public const string TextureWebp = "image/webp";

    #endregion

    #region Models

    /// <summary>glTF binary format (glb). Loaded via GltfDocument.AppendFromBuffer().</summary>
    public const string ModelGltfBinary = "model/gltf-binary";

    /// <summary>glTF JSON format. Loaded via GltfDocument.AppendFromBuffer().</summary>
    public const string ModelGltfJson = "model/gltf+json";

    #endregion

    #region Audio

    /// <summary>WAV audio format. Loaded into AudioStreamWav.</summary>
    public const string AudioWav = "audio/wav";

    /// <summary>OGG Vorbis audio format. Loaded via AudioStreamOggVorbis.LoadFromBuffer().</summary>
    public const string AudioOgg = "audio/ogg";

    /// <summary>MP3 audio format. Loaded into AudioStreamMP3.</summary>
    public const string AudioMp3 = "audio/mpeg";

    #endregion

    #region Data

    /// <summary>JSON data format for scenes, behaviors, etc.</summary>
    public const string DataJson = "application/json";

    /// <summary>YAML data format for behaviors, configuration.</summary>
    public const string DataYaml = "application/x-yaml";

    /// <summary>Binary data (generic).</summary>
    public const string Binary = "application/octet-stream";

    #endregion

    /// <summary>
    /// Gets the preferred content type from a file extension.
    /// </summary>
    /// <param name="extension">File extension (with or without leading dot).</param>
    /// <returns>The corresponding MIME content type.</returns>
    public static string FromExtension(string extension)
    {
        var ext = extension.TrimStart('.').ToLowerInvariant();
        return ext switch
        {
            // Textures
            "png" => TexturePng,
            "jpg" or "jpeg" => TextureJpeg,
            "webp" => TextureWebp,

            // Models
            "glb" => ModelGltfBinary,
            "gltf" => ModelGltfJson,

            // Audio
            "wav" or "wave" => AudioWav,
            "ogg" => AudioOgg,
            "mp3" => AudioMp3,

            // Data
            "json" => DataJson,
            "yaml" or "yml" => DataYaml,

            _ => Binary
        };
    }

    /// <summary>
    /// Gets the file extension for a content type.
    /// </summary>
    /// <param name="contentType">MIME content type.</param>
    /// <returns>File extension without leading dot, or "bin" for unknown types.</returns>
    public static string ToExtension(string contentType)
    {
        return contentType switch
        {
            TexturePng => "png",
            TextureJpeg => "jpg",
            TextureWebp => "webp",
            ModelGltfBinary => "glb",
            ModelGltfJson => "gltf",
            AudioWav => "wav",
            AudioOgg => "ogg",
            AudioMp3 => "mp3",
            DataJson => "json",
            DataYaml => "yaml",
            _ => "bin"
        };
    }

    /// <summary>
    /// Checks if a content type can be loaded by Godot at runtime.
    /// </summary>
    /// <param name="contentType">MIME content type to check.</param>
    /// <returns>True if Godot has native runtime loading support.</returns>
    public static bool IsRuntimeLoadable(string contentType)
    {
        return contentType switch
        {
            TexturePng or TextureJpeg or TextureWebp => true,
            ModelGltfBinary or ModelGltfJson => true,
            AudioWav or AudioOgg or AudioMp3 => true,
            DataJson or DataYaml => true,
            _ => false
        };
    }

    /// <summary>
    /// Checks if the content type requires conversion for Godot runtime loading.
    /// </summary>
    /// <param name="contentType">MIME content type to check.</param>
    /// <returns>True if conversion is needed before runtime loading.</returns>
    public static bool RequiresConversion(string contentType)
    {
        return contentType switch
        {
            "application/x-fbx" => true,        // FBX needs glTF conversion
            "image/targa" or "image/x-tga" => true,  // TGA needs PNG conversion
            "image/vnd-ms.dds" => true,         // DDS needs PNG conversion
            "audio/flac" => true,               // FLAC needs OGG/WAV conversion
            "image/bmp" => true,                // BMP should be PNG
            "image/tiff" => true,               // TIFF should be PNG
            _ => false
        };
    }

    /// <summary>
    /// Gets the target content type for conversion.
    /// </summary>
    /// <param name="sourceContentType">Original content type.</param>
    /// <returns>Target content type for conversion, or original if no conversion needed.</returns>
    public static string GetConversionTarget(string sourceContentType)
    {
        return sourceContentType switch
        {
            "application/x-fbx" => ModelGltfBinary,
            "image/targa" or "image/x-tga" => TexturePng,
            "image/vnd-ms.dds" => TexturePng,
            "image/bmp" => TexturePng,
            "image/tiff" => TexturePng,
            "audio/flac" => AudioOgg,
            _ => sourceContentType
        };
    }
}
