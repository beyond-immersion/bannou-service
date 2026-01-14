using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BeyondImmersion.BannouService.Asset.Bundles;
using BeyondImmersion.Bannou.SceneComposer.Stride.Caching;
using BeyondImmersion.Bannou.SceneComposer.Stride.Loaders;
using Stride.Core;
using Stride.Core.Serialization.Contents;
using Stride.Graphics;
using Stride.Rendering;
using Stride.Rendering.Sprites;

namespace BeyondImmersion.Bannou.SceneComposer.Stride.Content;

/// <summary>
/// Content type constants matching those used by the AssetTool.
/// </summary>
public static class BundleContentTypes
{
    /// <summary>Content type for Stride models.</summary>
    public const string StrideModel = "application/x-stride-model";

    /// <summary>Content type for Stride textures.</summary>
    public const string StrideTexture = "application/x-stride-texture";

    /// <summary>Content type for Stride animations.</summary>
    public const string StrideAnimation = "application/x-stride-animation";

    /// <summary>Content type for behavior models (ABML).</summary>
    public const string BehaviorModel = "application/x-behavior-model";
}

/// <summary>
/// Stride-integrated content manager that loads assets from .bannou bundles.
/// </summary>
/// <remarks>
/// <para>
/// This manager reads .bannou bundles directly and deserializes Stride assets
/// using reflection to access internal serialization APIs.
/// </para>
/// <para>
/// Features:
/// <list type="bullet">
/// <item>Loading bundles from disk or streams</item>
/// <item>LRU caching of loaded assets</item>
/// <item>Typed asset access (models, textures, etc.)</item>
/// <item>Automatic content type detection</item>
/// </list>
/// </para>
/// <para>
/// <strong>FRAGILITY WARNING:</strong> The model and texture loaders use reflection
/// to access internal Stride APIs. They may break with Stride engine updates.
/// </para>
/// </remarks>
public sealed class StrideContentManager : IDisposable
{
    private readonly IServiceRegistry _services;
    private readonly GraphicsDevice _graphicsDevice;
    private readonly AssetCache _cache;
    private readonly Dictionary<string, BundleAssetLoader> _bundles;
    private readonly ModelLoader _modelLoader;
    private readonly TextureLoader _textureLoader;
    private readonly object _lock = new();
    private readonly Action<string>? _debugLog;
    private bool _disposed;

    /// <summary>
    /// Creates a new Stride content manager.
    /// </summary>
    /// <param name="services">Stride service registry.</param>
    /// <param name="graphicsDevice">Graphics device for creating GPU resources.</param>
    /// <param name="maxCacheSizeBytes">Maximum cache size in bytes (default: 256 MB).</param>
    /// <param name="debugLog">Optional callback for debug logging.</param>
    public StrideContentManager(
        IServiceRegistry services,
        GraphicsDevice graphicsDevice,
        long maxCacheSizeBytes = 256 * 1024 * 1024,
        Action<string>? debugLog = null)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _graphicsDevice = graphicsDevice ?? throw new ArgumentNullException(nameof(graphicsDevice));
        _cache = new AssetCache(maxCacheSizeBytes);
        _bundles = new Dictionary<string, BundleAssetLoader>(StringComparer.Ordinal);
        _debugLog = debugLog;

        _modelLoader = new ModelLoader(services, debugLog);
        _textureLoader = new TextureLoader(services, graphicsDevice, debugLog);
    }

    /// <summary>
    /// Gets the number of loaded bundles.
    /// </summary>
    public int BundleCount
    {
        get { lock (_lock) return _bundles.Count; }
    }

    /// <summary>
    /// Gets the current cache size in bytes.
    /// </summary>
    public long CacheSize => _cache.CurrentSize;

    /// <summary>
    /// Gets the number of cached assets.
    /// </summary>
    public int CachedAssetCount => _cache.Count;

    /// <summary>
    /// Loads a bundle from a file path.
    /// </summary>
    /// <param name="bundlePath">Path to the .bannou bundle file.</param>
    /// <param name="bundleId">Optional bundle ID override. If null, uses the bundle's internal ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The bundle ID used to reference this bundle.</returns>
    public async Task<string> LoadBundleAsync(
        string bundlePath,
        string? bundleId = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var stream = File.OpenRead(bundlePath);
        try
        {
            return await LoadBundleFromStreamAsync(stream, bundleId, cancellationToken);
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Loads a bundle from a stream.
    /// </summary>
    /// <param name="stream">Stream containing the bundle data.</param>
    /// <param name="bundleId">Optional bundle ID override.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The bundle ID used to reference this bundle.</returns>
    public async Task<string> LoadBundleFromStreamAsync(
        Stream stream,
        string? bundleId = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var loader = new BundleAssetLoader(stream);
        await loader.InitializeAsync(cancellationToken);

        var id = bundleId ?? loader.BundleId;

        lock (_lock)
        {
            if (_bundles.TryGetValue(id, out var existing))
            {
                existing.Dispose();
            }
            _bundles[id] = loader;
        }

        Log($"Loaded bundle '{id}' with {loader.AssetCount} assets");
        return id;
    }

    /// <summary>
    /// Unloads a bundle.
    /// </summary>
    /// <param name="bundleId">The bundle ID to unload.</param>
    /// <returns>True if the bundle was found and unloaded.</returns>
    public bool UnloadBundle(string bundleId)
    {
        lock (_lock)
        {
            if (_bundles.TryGetValue(bundleId, out var loader))
            {
                _bundles.Remove(bundleId);
                loader.Dispose();
                Log($"Unloaded bundle '{bundleId}'");
                return true;
            }
            return false;
        }
    }

    /// <summary>
    /// Checks if a bundle is loaded.
    /// </summary>
    /// <param name="bundleId">The bundle ID to check.</param>
    /// <returns>True if the bundle is loaded.</returns>
    public bool IsBundleLoaded(string bundleId)
    {
        lock (_lock)
        {
            return _bundles.ContainsKey(bundleId);
        }
    }

    /// <summary>
    /// Gets a model from loaded bundles.
    /// </summary>
    /// <param name="assetId">The asset ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The loaded model.</returns>
    /// <exception cref="AssetNotFoundException">Thrown if asset not found in any bundle.</exception>
    public async Task<Model> GetModelAsync(string assetId, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Check cache first
        if (_cache.TryGet<Model>(assetId, out var cached))
        {
            // TryGet returned true so cached is guaranteed non-null;
            // coalesce satisfies compiler's nullable analysis (will never execute)
            return cached ?? throw new InvalidOperationException("Cache returned null for existing entry");
        }

        return await GetModelFromBundleAsync(assetId, cancellationToken);
    }

    private async Task<Model> GetModelFromBundleAsync(string assetId, CancellationToken cancellationToken)
    {
        // Find in bundles
        BundleAssetLoader? bundleLoader;
        BundleAssetEntry? entry;

        lock (_lock)
        {
            (bundleLoader, entry) = FindAsset(assetId);
        }

        if (bundleLoader == null || entry == null)
        {
            throw new AssetNotFoundException(assetId);
        }

        // Verify content type
        if (!entry.ContentType.Equals(BundleContentTypes.StrideModel, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Asset '{assetId}' has content type '{entry.ContentType}', " +
                $"expected '{BundleContentTypes.StrideModel}'");
        }

        // Read raw data
        var data = await bundleLoader.ReadAssetAsync(assetId, cancellationToken)
            ?? throw new InvalidOperationException($"Failed to read asset '{assetId}'");

        // Create dependency resolver that reads from the same bundle
        DependencyResolver dependencyResolver = (depUrl) =>
        {
            var depEntry = bundleLoader.GetAssetEntry(depUrl);
            if (depEntry == null)
                return null;

            // Read dependency data synchronously (we're already in an async context)
            return bundleLoader.ReadAssetAsync(depUrl, cancellationToken)
                .GetAwaiter().GetResult();
        };

        // Load model with dependency resolution
        var model = await _modelLoader.LoadAsync(data, assetId, dependencyResolver, cancellationToken);
        var size = _modelLoader.EstimateSize(model);

        // Cache and return
        _cache.Add(assetId, model, size);
        return model;
    }

    /// <summary>
    /// Gets a texture from loaded bundles.
    /// </summary>
    /// <param name="assetId">The asset ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The loaded texture.</returns>
    /// <exception cref="AssetNotFoundException">Thrown if asset not found in any bundle.</exception>
    public async Task<Texture> GetTextureAsync(string assetId, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Check cache first
        if (_cache.TryGet<Texture>(assetId, out var cached))
        {
            // TryGet returned true so cached is guaranteed non-null;
            // coalesce satisfies compiler's nullable analysis (will never execute)
            return cached ?? throw new InvalidOperationException("Cache returned null for existing entry");
        }

        return await GetAssetAsync<Texture>(
            assetId,
            BundleContentTypes.StrideTexture,
            _textureLoader,
            cancellationToken);
    }

    /// <summary>
    /// Gets a UI sprite from loaded bundles.
    /// Creates a SpriteFromTexture wrapping the loaded texture.
    /// </summary>
    /// <param name="assetId">The asset ID (e.g., "ui/bars/health_fill").</param>
    /// <param name="pixelsPerUnit">Pixels per unit for the sprite (default: 100).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The loaded sprite provider.</returns>
    /// <exception cref="AssetNotFoundException">Thrown if asset not found in any bundle.</exception>
    public async Task<SpriteFromTexture> GetUISpriteAsync(
        string assetId,
        float pixelsPerUnit = 100f,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Check cache first (using sprite-specific cache key)
        var spriteCacheKey = $"sprite:{assetId}";
        if (_cache.TryGet<SpriteFromTexture>(spriteCacheKey, out var cached))
        {
            // TryGet returned true so cached is guaranteed non-null;
            // coalesce satisfies compiler's nullable analysis (will never execute)
            return cached ?? throw new InvalidOperationException("Cache returned null for existing entry");
        }

        // Load the underlying texture
        var texture = await GetTextureAsync(assetId, cancellationToken);

        // Create a sprite from the texture
        var sprite = new SpriteFromTexture
        {
            Texture = texture,
            PixelsPerUnit = pixelsPerUnit,
            IsTransparent = true
        };

        // Cache the sprite (size estimate: texture size + sprite overhead)
        var spriteSize = _textureLoader.EstimateSize(texture) + 256;
        _cache.Add(spriteCacheKey, sprite, spriteSize);

        return sprite;
    }

    /// <summary>
    /// Gets raw asset data from loaded bundles.
    /// </summary>
    /// <param name="assetId">The asset ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The raw asset data and entry, or null if not found.</returns>
    public async Task<(byte[] Data, BundleAssetEntry Entry)?> GetRawAssetAsync(
        string assetId,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        BundleAssetLoader? loader;
        BundleAssetEntry? entry;

        lock (_lock)
        {
            (loader, entry) = FindAsset(assetId);
        }

        if (loader == null || entry == null)
        {
            return null;
        }

        var data = await loader.ReadAssetAsync(assetId, cancellationToken);
        if (data == null)
        {
            return null;
        }

        return (data, entry);
    }

    /// <summary>
    /// Checks if an asset exists in any loaded bundle.
    /// </summary>
    /// <param name="assetId">The asset ID to check.</param>
    /// <returns>True if the asset exists.</returns>
    public bool HasAsset(string assetId)
    {
        lock (_lock)
        {
            foreach (var bundle in _bundles.Values)
            {
                if (bundle.HasAsset(assetId))
                    return true;
            }
            return false;
        }
    }

    /// <summary>
    /// Gets the content type of an asset.
    /// </summary>
    /// <param name="assetId">The asset ID.</param>
    /// <returns>The content type, or null if asset not found.</returns>
    public string? GetContentType(string assetId)
    {
        lock (_lock)
        {
            foreach (var bundle in _bundles.Values)
            {
                var entry = bundle.GetAssetEntry(assetId);
                if (entry != null)
                    return entry.ContentType;
            }
            return null;
        }
    }

    /// <summary>
    /// Gets all loaded bundle IDs.
    /// </summary>
    /// <returns>List of bundle IDs.</returns>
    public IReadOnlyList<string> GetLoadedBundleIds()
    {
        lock (_lock)
        {
            return new List<string>(_bundles.Keys);
        }
    }

    /// <summary>
    /// Gets a bundle loader by ID.
    /// </summary>
    /// <param name="bundleId">The bundle ID.</param>
    /// <returns>The bundle loader, or null if not found.</returns>
    public BundleAssetLoader? GetBundle(string bundleId)
    {
        lock (_lock)
        {
            return _bundles.GetValueOrDefault(bundleId);
        }
    }

    /// <summary>
    /// Clears the asset cache.
    /// </summary>
    public void ClearCache()
    {
        _cache.Clear();
    }

    /// <summary>
    /// Removes a specific asset from the cache.
    /// </summary>
    /// <param name="assetId">The asset ID to remove.</param>
    /// <returns>True if the asset was removed.</returns>
    public bool RemoveFromCache(string assetId)
    {
        return _cache.Remove(assetId);
    }

    private async Task<T> GetAssetAsync<T>(
        string assetId,
        string expectedContentType,
        IStrideAssetLoader<T> loader,
        CancellationToken cancellationToken) where T : class
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Check cache first
        if (_cache.TryGet<T>(assetId, out var cached))
        {
            // TryGet returned true so cached is guaranteed non-null;
            // coalesce satisfies compiler's nullable analysis (will never execute)
            return cached ?? throw new InvalidOperationException("Cache returned null for existing entry");
        }

        // Find in bundles
        BundleAssetLoader? bundleLoader;
        BundleAssetEntry? entry;

        lock (_lock)
        {
            (bundleLoader, entry) = FindAsset(assetId);
        }

        if (bundleLoader == null || entry == null)
        {
            throw new AssetNotFoundException(assetId);
        }

        // Verify content type
        if (!entry.ContentType.Equals(expectedContentType, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Asset '{assetId}' has content type '{entry.ContentType}', " +
                $"expected '{expectedContentType}'");
        }

        // Read raw data
        var data = await bundleLoader.ReadAssetAsync(assetId, cancellationToken)
            ?? throw new InvalidOperationException($"Failed to read asset '{assetId}'");

        // Load typed asset
        var asset = await loader.LoadAsync(data, assetId, cancellationToken);
        var size = loader.EstimateSize(asset);

        // Cache and return
        _cache.Add(assetId, asset, size);
        return asset;
    }

    private (BundleAssetLoader? Loader, BundleAssetEntry? Entry) FindAsset(string assetId)
    {
        foreach (var bundle in _bundles.Values)
        {
            var entry = bundle.GetAssetEntry(assetId);
            if (entry != null)
            {
                return (bundle, entry);
            }
        }
        return (null, null);
    }

    private void Log(string message)
    {
        _debugLog?.Invoke(message);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        _cache.Dispose();

        lock (_lock)
        {
            foreach (var bundle in _bundles.Values)
            {
                bundle.Dispose();
            }
            _bundles.Clear();
        }
    }
}

/// <summary>
/// Exception thrown when an asset is not found in any loaded bundle.
/// </summary>
public sealed class AssetNotFoundException : Exception
{
    /// <summary>
    /// The asset ID that was not found.
    /// </summary>
    public string AssetId { get; }

    /// <summary>
    /// Creates a new AssetNotFoundException.
    /// </summary>
    /// <param name="assetId">The asset ID that was not found.</param>
    public AssetNotFoundException(string assetId)
        : base($"Asset '{assetId}' not found in any loaded bundle")
    {
        AssetId = assetId;
    }

    /// <summary>
    /// Creates a new AssetNotFoundException with an inner exception.
    /// </summary>
    /// <param name="assetId">The asset ID that was not found.</param>
    /// <param name="innerException">The inner exception.</param>
    public AssetNotFoundException(string assetId, Exception innerException)
        : base($"Asset '{assetId}' not found in any loaded bundle", innerException)
    {
        AssetId = assetId;
    }
}
