using BeyondImmersion.Bannou.Bundle.Format;

namespace BeyondImmersion.Bannou.AssetLoader.Abstractions;

/// <summary>
/// Type-specific asset loader for deserializing raw bundle data into engine-native types.
/// Engine implementations register these for their native types:
/// - Stride: Model, Texture, Animation, Material
/// - Godot: PackedScene, Mesh, Texture2D, AudioStream
/// - Unity: GameObject, Mesh, Texture2D, AudioClip
/// </summary>
/// <typeparam name="T">Engine-native asset type.</typeparam>
public interface IAssetTypeLoader<T>
{
    /// <summary>
    /// Content types this loader can handle.
    /// Used to match assets to appropriate loaders.
    /// </summary>
    /// <example>
    /// Stride model loader: ["application/x-stride-model", "model/gltf-binary"]
    /// </example>
    IReadOnlyList<string> SupportedContentTypes { get; }

    /// <summary>
    /// Deserializes raw asset data into an engine-native type.
    /// </summary>
    /// <param name="data">Raw asset data from bundle.</param>
    /// <param name="metadata">Asset metadata from bundle manifest.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Loaded engine-native asset.</returns>
    Task<T> LoadAsync(
        ReadOnlyMemory<byte> data,
        BundleAssetEntry metadata,
        CancellationToken ct = default);

    /// <summary>
    /// Unloads/disposes a previously loaded asset.
    /// Releases GPU resources, memory, etc.
    /// </summary>
    /// <param name="asset">Asset to unload.</param>
    void Unload(T asset);
}

/// <summary>
/// Non-generic interface for type loader registration.
/// Used internally by AssetLoader for dynamic dispatch.
/// </summary>
public interface IAssetTypeLoader
{
    /// <summary>Content types this loader can handle.</summary>
    IReadOnlyList<string> SupportedContentTypes { get; }

    /// <summary>The type this loader produces.</summary>
    Type AssetType { get; }

    /// <summary>Loads asset as object (for dynamic dispatch).</summary>
    Task<object> LoadAsObjectAsync(
        ReadOnlyMemory<byte> data,
        BundleAssetEntry metadata,
        CancellationToken ct = default);

    /// <summary>Unloads asset (for dynamic dispatch).</summary>
    void UnloadObject(object asset);
}

/// <summary>
/// Base class for type loaders that implements both interfaces.
/// </summary>
/// <typeparam name="T">Engine-native asset type.</typeparam>
public abstract class AssetTypeLoaderBase<T> : IAssetTypeLoader<T>, IAssetTypeLoader
{
    /// <inheritdoc />
    public abstract IReadOnlyList<string> SupportedContentTypes { get; }

    /// <inheritdoc />
    public Type AssetType => typeof(T);

    /// <inheritdoc />
    public abstract Task<T> LoadAsync(
        ReadOnlyMemory<byte> data,
        BundleAssetEntry metadata,
        CancellationToken ct = default);

    /// <inheritdoc />
    public abstract void Unload(T asset);

    /// <inheritdoc />
    async Task<object> IAssetTypeLoader.LoadAsObjectAsync(
        ReadOnlyMemory<byte> data,
        BundleAssetEntry metadata,
        CancellationToken ct)
    {
        var result = await LoadAsync(data, metadata, ct).ConfigureAwait(false);
        return result!;
    }

    /// <inheritdoc />
    void IAssetTypeLoader.UnloadObject(object asset)
    {
        if (asset is T typedAsset)
        {
            Unload(typedAsset);
        }
    }
}
