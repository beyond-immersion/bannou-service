using BeyondImmersion.Bannou.AssetBundler.Extraction;

namespace BeyondImmersion.Bannou.AssetBundler.Abstractions;

/// <summary>
/// Infers asset types and texture types from filenames and content.
/// Vendor-specific implementations handle naming conventions
/// (e.g., Synty uses "_normal", "_nml" for normal maps).
/// </summary>
public interface IAssetTypeInferencer
{
    /// <summary>
    /// Infers the general asset type from a filename.
    /// </summary>
    /// <param name="filename">The filename to analyze.</param>
    /// <param name="mimeType">Optional MIME type hint.</param>
    /// <returns>The inferred asset type.</returns>
    AssetType InferAssetType(string filename, string? mimeType = null);

    /// <summary>
    /// For texture assets, infers the specific texture type.
    /// </summary>
    /// <param name="filename">The filename to analyze.</param>
    /// <returns>The inferred texture type.</returns>
    TextureType InferTextureType(string filename);

    /// <summary>
    /// Determines if a file should be extracted based on its path.
    /// Allows filtering out engine-specific files (Unity, Unreal, Maya, etc.).
    /// </summary>
    /// <param name="relativePath">The relative path within the source.</param>
    /// <param name="category">Optional category hint.</param>
    /// <returns>True if the file should be extracted.</returns>
    bool ShouldExtract(string relativePath, string? category = null);
}
