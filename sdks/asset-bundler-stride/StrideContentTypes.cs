namespace BeyondImmersion.Bannou.AssetBundler.Stride;

/// <summary>
/// MIME content types for Stride-compiled assets.
/// </summary>
public static class StrideContentTypes
{
    /// <summary>Content type for compiled Stride model assets.</summary>
    public const string Model = "application/x-stride-model";

    /// <summary>Content type for compiled Stride texture assets.</summary>
    public const string Texture = "application/x-stride-texture";

    /// <summary>Content type for compiled Stride animation assets.</summary>
    public const string Animation = "application/x-stride-animation";

    /// <summary>Content type for compiled Stride material assets.</summary>
    public const string Material = "application/x-stride-material";

    /// <summary>Content type for Stride binary data (buffers, etc.).</summary>
    public const string Binary = "application/x-stride-binary";

    /// <summary>
    /// Gets the content type from a Stride asset extension.
    /// </summary>
    /// <param name="extension">File extension (with or without leading dot).</param>
    /// <returns>The corresponding MIME content type.</returns>
    public static string FromExtension(string extension)
    {
        var ext = extension.TrimStart('.').ToLowerInvariant();
        return ext switch
        {
            "sdmodel" => Model,
            "sdtex" => Texture,
            "sdanim" => Animation,
            "sdmat" => Material,
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
            Model => "sdmodel",
            Texture => "sdtex",
            Animation => "sdanim",
            Material => "sdmat",
            _ => "bin"
        };
    }
}
