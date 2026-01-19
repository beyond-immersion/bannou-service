using BeyondImmersion.Bannou.SceneComposer.Abstractions;

namespace BeyondImmersion.Bannou.SceneComposer.Assets;

/// <summary>
/// Engine-agnostic interface for managing asset bundles.
/// Handles bundle loading, asset retrieval, and caching.
/// </summary>
public interface IAssetBundleManager
{
    #region Bundle Management

    /// <summary>
    /// Load a bundle asynchronously.
    /// </summary>
    /// <param name="bundleId">Bundle identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Handle to the loaded bundle.</returns>
    Task<BundleHandle> LoadBundleAsync(string bundleId, CancellationToken ct = default);

    /// <summary>
    /// Unload a bundle and free its resources.
    /// </summary>
    /// <param name="bundleId">Bundle identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    Task UnloadBundleAsync(string bundleId, CancellationToken ct = default);

    /// <summary>
    /// Check if a bundle is loaded.
    /// </summary>
    /// <param name="bundleId">Bundle identifier.</param>
    bool IsBundleLoaded(string bundleId);

    /// <summary>
    /// Get a loaded bundle's handle.
    /// </summary>
    /// <param name="bundleId">Bundle identifier.</param>
    /// <returns>Bundle handle, or null if not loaded.</returns>
    BundleHandle? GetBundle(string bundleId);

    /// <summary>
    /// Get all currently loaded bundle IDs.
    /// </summary>
    IEnumerable<string> GetLoadedBundles();

    #endregion

    #region Asset Queries

    /// <summary>
    /// Get all asset entries in a bundle.
    /// </summary>
    /// <param name="bundleId">Bundle identifier.</param>
    /// <param name="filterType">Optional type filter.</param>
    IEnumerable<AssetEntry> GetAssetEntries(string bundleId, AssetType? filterType = null);

    /// <summary>
    /// Check if an asset exists in a bundle.
    /// </summary>
    /// <param name="bundleId">Bundle identifier.</param>
    /// <param name="assetId">Asset identifier.</param>
    bool HasAsset(string bundleId, string assetId);

    /// <summary>
    /// Get metadata for an asset.
    /// </summary>
    /// <param name="bundleId">Bundle identifier.</param>
    /// <param name="assetId">Asset identifier.</param>
    AssetEntry? GetAssetEntry(string bundleId, string assetId);

    /// <summary>
    /// Search for assets by tag.
    /// </summary>
    /// <param name="bundleId">Bundle identifier (or null for all bundles).</param>
    /// <param name="tag">Tag to search for.</param>
    IEnumerable<AssetEntry> FindAssetsByTag(string? bundleId, string tag);

    /// <summary>
    /// Search for assets by name pattern.
    /// </summary>
    /// <param name="bundleId">Bundle identifier (or null for all bundles).</param>
    /// <param name="pattern">Name pattern (supports * wildcards).</param>
    IEnumerable<AssetEntry> FindAssetsByName(string? bundleId, string pattern);

    #endregion

    #region Asset Data

    /// <summary>
    /// Get raw bytes for an asset.
    /// </summary>
    /// <param name="bundleId">Bundle identifier.</param>
    /// <param name="assetId">Asset identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<byte[]> GetAssetBytesAsync(string bundleId, string assetId, CancellationToken ct = default);

    /// <summary>
    /// Get raw bytes for an asset from a reference.
    /// </summary>
    /// <param name="asset">Asset reference.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<byte[]> GetAssetBytesAsync(AssetReference asset, CancellationToken ct = default);

    #endregion

    #region Cache Management

    /// <summary>
    /// Current cache size in bytes.
    /// </summary>
    long CacheSize { get; }

    /// <summary>
    /// Number of cached assets.
    /// </summary>
    int CachedAssetCount { get; }

    /// <summary>
    /// Maximum cache size in bytes.
    /// </summary>
    long MaxCacheSize { get; }

    /// <summary>
    /// Set maximum cache size.
    /// </summary>
    void SetMaxCacheSize(long bytes);

    /// <summary>
    /// Clear all cached assets.
    /// </summary>
    void ClearCache();

    /// <summary>
    /// Evict least recently used assets until cache is under target size.
    /// </summary>
    /// <param name="targetSize">Target size in bytes.</param>
    void EvictToSize(long targetSize);

    #endregion

    #region Events

    /// <summary>
    /// Raised when a bundle starts loading.
    /// </summary>
    event EventHandler<BundleLoadingEventArgs>? BundleLoading;

    /// <summary>
    /// Raised when a bundle finishes loading.
    /// </summary>
    event EventHandler<BundleLoadedEventArgs>? BundleLoaded;

    /// <summary>
    /// Raised when a bundle fails to load.
    /// </summary>
    event EventHandler<BundleLoadFailedEventArgs>? BundleLoadFailed;

    /// <summary>
    /// Raised when a bundle is unloaded.
    /// </summary>
    event EventHandler<BundleUnloadedEventArgs>? BundleUnloaded;

    #endregion
}

/// <summary>
/// Type-specific asset loader interface.
/// Each engine implements this for its native asset types.
/// </summary>
/// <typeparam name="T">Engine-specific asset type.</typeparam>
public interface IAssetLoader<T>
{
    /// <summary>
    /// Load an asset from raw bytes.
    /// </summary>
    /// <param name="data">Raw asset data.</param>
    /// <param name="assetId">Asset identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<T> LoadAsync(byte[] data, string assetId, CancellationToken ct = default);

    /// <summary>
    /// Unload an asset and free resources.
    /// </summary>
    /// <param name="asset">Asset to unload.</param>
    void Unload(T asset);

    /// <summary>
    /// Estimate the memory size of a loaded asset.
    /// </summary>
    /// <param name="asset">Asset to measure.</param>
    long EstimateSize(T asset);
}

/// <summary>
/// Handle to a loaded bundle.
/// </summary>
public class BundleHandle
{
    /// <summary>
    /// Bundle identifier.
    /// </summary>
    public string BundleId { get; }

    /// <summary>
    /// Bundle manifest data.
    /// </summary>
    public BundleManifest Manifest { get; }

    /// <summary>
    /// Number of assets in this bundle.
    /// </summary>
    public int AssetCount => Manifest.Assets.Count;

    /// <summary>
    /// Total size of bundle in bytes.
    /// </summary>
    public long TotalSize { get; }

    /// <summary>
    /// When the bundle was loaded.
    /// </summary>
    public DateTime LoadedAt { get; }

    /// <summary>
    /// Reference count for this bundle.
    /// </summary>
    public int ReferenceCount { get; internal set; }

    /// <summary>Creates a new bundle handle.</summary>
    public BundleHandle(string bundleId, BundleManifest manifest, long totalSize)
    {
        BundleId = bundleId ?? throw new ArgumentNullException(nameof(bundleId));
        Manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
        TotalSize = totalSize;
        LoadedAt = DateTime.UtcNow;
        ReferenceCount = 1;
    }

    /// <summary>
    /// Get an asset entry by ID.
    /// </summary>
    public AssetEntry? GetAsset(string assetId)
    {
        return Manifest.Assets.FirstOrDefault(a => a.AssetId == assetId);
    }
}

/// <summary>
/// Bundle manifest describing contained assets.
/// </summary>
public class BundleManifest
{
    /// <summary>
    /// Bundle identifier.
    /// </summary>
    public string BundleId { get; set; } = string.Empty;

    /// <summary>
    /// Bundle version.
    /// </summary>
    public string Version { get; set; } = "1.0.0";

    /// <summary>
    /// Assets in this bundle.
    /// </summary>
    public List<AssetEntry> Assets { get; set; } = new();

    /// <summary>
    /// Dependencies on other bundles.
    /// </summary>
    public List<string> Dependencies { get; set; } = new();

    /// <summary>
    /// Bundle metadata.
    /// </summary>
    public Dictionary<string, string> Metadata { get; set; } = new();
}

/// <summary>
/// Entry describing a single asset in a bundle.
/// </summary>
public class AssetEntry
{
    /// <summary>
    /// Unique identifier within the bundle.
    /// </summary>
    public string AssetId { get; set; } = string.Empty;

    /// <summary>
    /// Display name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Type of asset.
    /// </summary>
    public AssetType Type { get; set; }

    /// <summary>
    /// Size in bytes (compressed).
    /// </summary>
    public long CompressedSize { get; set; }

    /// <summary>
    /// Size in bytes (uncompressed).
    /// </summary>
    public long UncompressedSize { get; set; }

    /// <summary>
    /// Offset in bundle file.
    /// </summary>
    public long Offset { get; set; }

    /// <summary>
    /// Tags for categorization.
    /// </summary>
    public List<string> Tags { get; set; } = new();

    /// <summary>
    /// Thumbnail URL (if available).
    /// </summary>
    public string? ThumbnailUrl { get; set; }

    /// <summary>
    /// Dependencies on other assets.
    /// </summary>
    public List<string> Dependencies { get; set; } = new();

    /// <summary>
    /// Variants of this asset.
    /// </summary>
    public List<AssetVariant> Variants { get; set; } = new();

    /// <summary>
    /// Convert to AssetReference.
    /// </summary>
    public AssetReference ToReference(string bundleId, string? variantId = null)
    {
        return new AssetReference(bundleId, AssetId, variantId);
    }
}

/// <summary>
/// Variant of an asset.
/// </summary>
public class AssetVariant
{
    /// <summary>
    /// Variant identifier.
    /// </summary>
    public string VariantId { get; set; } = string.Empty;

    /// <summary>
    /// Display name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Thumbnail URL (if available).
    /// </summary>
    public string? ThumbnailUrl { get; set; }
}

/// <summary>
/// Type of asset.
/// </summary>
public enum AssetType
{
    /// <summary>3D model.</summary>
    Model,

    /// <summary>2D texture.</summary>
    Texture,

    /// <summary>Audio clip.</summary>
    Audio,

    /// <summary>Animation data.</summary>
    Animation,

    /// <summary>Material definition.</summary>
    Material,

    /// <summary>Behavior script.</summary>
    Behavior,

    /// <summary>Shader program.</summary>
    Shader,

    /// <summary>Font.</summary>
    Font,

    /// <summary>Prefab definition.</summary>
    Prefab,

    /// <summary>Other/unknown type.</summary>
    Other
}

#region Event Args

/// <summary>
/// Event args for bundle loading started.
/// </summary>
public class BundleLoadingEventArgs : EventArgs
{
    /// <summary>The bundle identifier.</summary>
    public string BundleId { get; }

    /// <summary>Creates bundle loading event args.</summary>
    public BundleLoadingEventArgs(string bundleId)
    {
        BundleId = bundleId;
    }
}

/// <summary>
/// Event args for bundle loaded.
/// </summary>
public class BundleLoadedEventArgs : EventArgs
{
    /// <summary>The bundle identifier.</summary>
    public string BundleId { get; }
    /// <summary>The loaded bundle handle.</summary>
    public BundleHandle Handle { get; }
    /// <summary>Time taken to load the bundle.</summary>
    public TimeSpan LoadTime { get; }

    /// <summary>Creates bundle loaded event args.</summary>
    public BundleLoadedEventArgs(string bundleId, BundleHandle handle, TimeSpan loadTime)
    {
        BundleId = bundleId;
        Handle = handle;
        LoadTime = loadTime;
    }
}

/// <summary>
/// Event args for bundle load failure.
/// </summary>
public class BundleLoadFailedEventArgs : EventArgs
{
    /// <summary>The bundle identifier.</summary>
    public string BundleId { get; }
    /// <summary>The exception that caused the failure.</summary>
    public Exception Exception { get; }

    /// <summary>Creates bundle load failed event args.</summary>
    public BundleLoadFailedEventArgs(string bundleId, Exception exception)
    {
        BundleId = bundleId;
        Exception = exception;
    }
}

/// <summary>
/// Event args for bundle unloaded.
/// </summary>
public class BundleUnloadedEventArgs : EventArgs
{
    /// <summary>The bundle identifier.</summary>
    public string BundleId { get; }

    /// <summary>Creates bundle unloaded event args.</summary>
    public BundleUnloadedEventArgs(string bundleId)
    {
        BundleId = bundleId;
    }
}

#endregion
