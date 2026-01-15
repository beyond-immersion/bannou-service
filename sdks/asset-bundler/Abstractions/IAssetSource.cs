using BeyondImmersion.Bannou.AssetBundler.Extraction;

namespace BeyondImmersion.Bannou.AssetBundler.Abstractions;

/// <summary>
/// Represents a source of raw assets that can be extracted for bundling.
/// Examples: a Synty pack ZIP, a directory of FBX files, a Unity asset export.
/// </summary>
public interface IAssetSource
{
    /// <summary>
    /// Unique identifier for this source (e.g., "synty/polygon-adventure").
    /// </summary>
    string SourceId { get; }

    /// <summary>
    /// Human-readable name (e.g., "POLYGON - Adventure Pack").
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Version string (e.g., "v4", "1.0.0").
    /// </summary>
    string Version { get; }

    /// <summary>
    /// Content hash for change detection (SHA256 of source file/directory).
    /// </summary>
    string ContentHash { get; }

    /// <summary>
    /// Tags for categorization and smart bundling.
    /// </summary>
    IReadOnlyDictionary<string, string> Tags { get; }

    /// <summary>
    /// Extracts raw assets from this source to a working directory.
    /// </summary>
    /// <param name="workingDir">Directory to extract assets into.</param>
    /// <param name="typeInferencer">Optional type inferencer for asset classification.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Extraction result with list of extracted assets.</returns>
    Task<ExtractionResult> ExtractAsync(
        DirectoryInfo workingDir,
        IAssetTypeInferencer? typeInferencer = null,
        CancellationToken ct = default);
}
