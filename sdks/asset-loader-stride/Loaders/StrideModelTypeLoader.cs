using BeyondImmersion.Bannou.AssetLoader.Abstractions;
using BeyondImmersion.Bannou.Bundle.Format;
using Stride.Core;
using Stride.Rendering;

namespace BeyondImmersion.Bannou.AssetLoader.Stride.Loaders;

/// <summary>
/// IAssetTypeLoader adapter for Stride Model assets.
/// Wraps the ModelLoader for use with the AssetLoader SDK.
/// </summary>
public sealed class StrideModelTypeLoader : IAssetTypeLoader<Model>
{
    private readonly ModelLoader _innerLoader;

    /// <inheritdoc />
    public IReadOnlyList<string> SupportedContentTypes { get; } = new[]
    {
        "application/x-stride-model",
        "application/x-sdmodel"
    };

    /// <inheritdoc />
    public Type AssetType => typeof(Model);

    /// <summary>
    /// Creates a new Stride model type loader.
    /// </summary>
    /// <param name="services">Stride service registry.</param>
    /// <param name="debugLog">Optional debug logging callback.</param>
    public StrideModelTypeLoader(IServiceRegistry services, Action<string>? debugLog = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        _innerLoader = new ModelLoader(services, debugLog);
    }

    /// <inheritdoc />
    public async Task<Model> LoadAsync(
        ReadOnlyMemory<byte> data,
        BundleAssetEntry metadata,
        CancellationToken ct = default)
    {
        return await _innerLoader.LoadAsync(data.ToArray(), metadata.AssetId, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void Unload(Model asset)
    {
        // Models don't need explicit disposal - GPU resources are managed by Stride
    }
}
