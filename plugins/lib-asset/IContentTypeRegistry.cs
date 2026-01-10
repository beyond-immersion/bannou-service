namespace BeyondImmersion.BannouService.Asset;

/// <summary>
/// Central registry for content type (MIME type) management.
/// Consolidates all MIME type logic for the Asset service.
/// </summary>
public interface IContentTypeRegistry
{
    /// <summary>
    /// Checks if a content type is processable by any processor.
    /// </summary>
    /// <param name="contentType">The content type to check.</param>
    /// <returns>True if the content type can be processed.</returns>
    bool IsProcessable(string contentType);

    /// <summary>
    /// Checks if a content type is forbidden (not allowed to upload/process).
    /// </summary>
    /// <param name="contentType">The content type to check.</param>
    /// <returns>True if the content type is forbidden.</returns>
    bool IsForbidden(string contentType);

    /// <summary>
    /// Checks if a content type is an audio type.
    /// </summary>
    /// <param name="contentType">The content type to check.</param>
    /// <returns>True if the content type is audio.</returns>
    bool IsAudio(string contentType);

    /// <summary>
    /// Checks if a content type is a texture/image type.
    /// </summary>
    /// <param name="contentType">The content type to check.</param>
    /// <returns>True if the content type is a texture/image.</returns>
    bool IsTexture(string contentType);

    /// <summary>
    /// Checks if a content type is a 3D model type.
    /// </summary>
    /// <param name="contentType">The content type to check.</param>
    /// <returns>True if the content type is a 3D model.</returns>
    bool IsModel(string contentType);

    /// <summary>
    /// Checks if a content type is a lossless audio format.
    /// </summary>
    /// <param name="contentType">The content type to check.</param>
    /// <returns>True if the content type is lossless audio.</returns>
    bool IsLosslessAudio(string contentType);

    /// <summary>
    /// Gets the content type for a file extension.
    /// </summary>
    /// <param name="extension">The file extension (with or without leading dot).</param>
    /// <returns>The content type, or "application/octet-stream" if unknown.</returns>
    string GetContentTypeFromExtension(string extension);

    /// <summary>
    /// Gets all processable content types.
    /// </summary>
    IReadOnlySet<string> ProcessableContentTypes { get; }

    /// <summary>
    /// Gets all audio content types.
    /// </summary>
    IReadOnlyList<string> AudioContentTypes { get; }

    /// <summary>
    /// Gets all texture/image content types.
    /// </summary>
    IReadOnlyList<string> TextureContentTypes { get; }

    /// <summary>
    /// Gets all 3D model content types.
    /// </summary>
    IReadOnlyList<string> ModelContentTypes { get; }

    /// <summary>
    /// Gets all forbidden content types.
    /// </summary>
    IReadOnlySet<string> ForbiddenContentTypes { get; }

    /// <summary>
    /// Gets the extension-to-content-type mappings.
    /// </summary>
    IReadOnlyDictionary<string, string> ExtensionMappings { get; }
}
