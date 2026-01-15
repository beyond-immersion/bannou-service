using BeyondImmersion.Bannou.AssetLoader.Abstractions;
using BeyondImmersion.Bannou.Bundle.Format;
using Stride.Animations;
using Stride.Core;

namespace BeyondImmersion.Bannou.AssetLoader.Stride.Loaders;

/// <summary>
/// IAssetTypeLoader adapter for Stride AnimationClip assets.
/// Wraps the AnimationClipLoader for use with the AssetLoader SDK.
/// </summary>
public sealed class StrideAnimationTypeLoader : IAssetTypeLoader<AnimationClip>
{
    private readonly AnimationClipLoader _innerLoader;

    /// <inheritdoc />
    public IReadOnlyList<string> SupportedContentTypes { get; } = new[]
    {
        "application/x-stride-animation",
        "application/x-sdanim"
    };

    /// <inheritdoc />
    public Type AssetType => typeof(AnimationClip);

    /// <summary>
    /// Creates a new Stride animation type loader.
    /// </summary>
    /// <param name="services">Stride service registry.</param>
    /// <param name="debugLog">Optional debug logging callback.</param>
    public StrideAnimationTypeLoader(IServiceRegistry services, Action<string>? debugLog = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        _innerLoader = new AnimationClipLoader(services, debugLog);
    }

    /// <inheritdoc />
    public async Task<AnimationClip> LoadAsync(
        ReadOnlyMemory<byte> data,
        BundleAssetEntry metadata,
        CancellationToken ct = default)
    {
        return await _innerLoader.LoadAsync(data.ToArray(), metadata.AssetId, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void Unload(AnimationClip asset)
    {
        // AnimationClips don't hold GPU resources and don't need explicit disposal
    }
}
