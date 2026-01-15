using BeyondImmersion.Bannou.AssetLoader.Abstractions;
using BeyondImmersion.Bannou.Bundle.Format;
using Godot;

namespace BeyondImmersion.Bannou.AssetLoader.Godot.Loaders;

/// <summary>
/// IAssetTypeLoader for Godot Texture2D assets.
/// Supports PNG, JPEG loading from raw byte buffers.
/// </summary>
public sealed class GodotTexture2DTypeLoader : IAssetTypeLoader<Texture2D>
{
    private readonly Action<string>? _debugLog;

    /// <inheritdoc />
    public IReadOnlyList<string> SupportedContentTypes { get; } = new[]
    {
        "image/png",
        "image/jpeg",
        "image/jpg"
    };

    /// <inheritdoc />
    public Type AssetType => typeof(Texture2D);

    /// <summary>
    /// Creates a new Godot Texture2D type loader.
    /// </summary>
    /// <param name="debugLog">Optional debug logging callback.</param>
    public GodotTexture2DTypeLoader(Action<string>? debugLog = null)
    {
        _debugLog = debugLog;
    }

    /// <inheritdoc />
    public Task<Texture2D> LoadAsync(
        ReadOnlyMemory<byte> data,
        BundleAssetEntry metadata,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var image = new Image();
        Error error;

        // Determine format from content type
        if (metadata.ContentType == "image/png")
        {
            error = image.LoadPngFromBuffer(data.ToArray());
        }
        else if (metadata.ContentType is "image/jpeg" or "image/jpg")
        {
            error = image.LoadJpgFromBuffer(data.ToArray());
        }
        else
        {
            // Try PNG first, then JPEG
            error = image.LoadPngFromBuffer(data.ToArray());
            if (error != Error.Ok)
                error = image.LoadJpgFromBuffer(data.ToArray());
        }

        if (error != Error.Ok)
            throw new InvalidOperationException($"Failed to load image '{metadata.AssetId}': {error}");

        var texture = ImageTexture.CreateFromImage(image);
        _debugLog?.Invoke($"Loaded Texture2D: {metadata.AssetId} ({image.GetWidth()}x{image.GetHeight()})");

        return Task.FromResult<Texture2D>(texture);
    }

    /// <inheritdoc />
    public void Unload(Texture2D asset)
    {
        // Godot textures are reference counted, explicit disposal not typically needed
        // but we can free the resource if it's valid
        if (GodotObject.IsInstanceValid(asset))
        {
            asset.Free();
        }
    }
}
