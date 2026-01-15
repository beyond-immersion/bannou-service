using BeyondImmersion.Bannou.AssetLoader.Abstractions;
using BeyondImmersion.Bannou.Bundle.Format;
using Stride.Core;
using Stride.Graphics;

namespace BeyondImmersion.Bannou.AssetLoader.Stride.Loaders;

/// <summary>
/// IAssetTypeLoader adapter for Stride Texture assets.
/// Wraps the TextureLoader for use with the AssetLoader SDK.
/// </summary>
public sealed class StrideTextureTypeLoader : IAssetTypeLoader<Texture>
{
    private readonly TextureLoader _innerLoader;

    /// <inheritdoc />
    public IReadOnlyList<string> SupportedContentTypes { get; } = new[]
    {
        "application/x-stride-texture",
        "application/x-sdtex",
        "image/dds",
        "image/png",
        "image/jpeg"
    };

    /// <inheritdoc />
    public Type AssetType => typeof(Texture);

    /// <summary>
    /// Creates a new Stride texture type loader.
    /// </summary>
    /// <param name="services">Stride service registry.</param>
    /// <param name="graphicsDevice">Stride graphics device for GPU resource creation.</param>
    /// <param name="debugLog">Optional debug logging callback.</param>
    public StrideTextureTypeLoader(
        IServiceRegistry services,
        GraphicsDevice graphicsDevice,
        Action<string>? debugLog = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(graphicsDevice);
        _innerLoader = new TextureLoader(services, graphicsDevice, debugLog);
    }

    /// <inheritdoc />
    public async Task<Texture> LoadAsync(
        ReadOnlyMemory<byte> data,
        BundleAssetEntry metadata,
        CancellationToken ct = default)
    {
        return await _innerLoader.LoadAsync(data.ToArray(), metadata.AssetId, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void Unload(Texture asset)
    {
        asset?.Dispose();
    }
}
