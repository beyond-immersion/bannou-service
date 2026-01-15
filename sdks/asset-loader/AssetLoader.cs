using System.Diagnostics;
using BeyondImmersion.Bannou.AssetLoader.Abstractions;
using BeyondImmersion.Bannou.AssetLoader.Download;
using BeyondImmersion.Bannou.AssetLoader.Models;
using BeyondImmersion.Bannou.AssetLoader.Registry;
using BeyondImmersion.Bannou.AssetLoader.Sources;
using BeyondImmersion.Bannou.Bundle.Format;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.Bannou.AssetLoader;

/// <summary>
/// Main entry point for loading assets from Bannou bundles.
/// Coordinates sources, caching, downloading, and type-specific loading.
/// </summary>
public sealed class AssetLoader : IAsyncDisposable
{
    private readonly IAssetSource _source;
    private readonly IAssetCache? _cache;
    private readonly BundleRegistry _registry;
    private readonly AssetDownloader _downloader;
    private readonly AssetLoaderOptions _options;
    private readonly Dictionary<string, IAssetTypeLoader> _typeLoaders = new();
    private readonly SemaphoreSlim _downloadSemaphore;
    private readonly ILogger<AssetLoader>? _logger;

    /// <summary>
    /// The asset source used for resolving download URLs.
    /// </summary>
    public IAssetSource Source => _source;

    /// <summary>
    /// The bundle registry tracking loaded bundles.
    /// </summary>
    public IBundleRegistry Registry => _registry;

    /// <summary>
    /// The cache used for storing downloaded bundles.
    /// </summary>
    public IAssetCache? Cache => _cache;

    /// <summary>
    /// Creates a new AssetLoader.
    /// </summary>
    /// <param name="source">Asset source for resolving download URLs.</param>
    /// <param name="cache">Optional cache for downloaded bundles.</param>
    /// <param name="options">Loader configuration options.</param>
    /// <param name="logger">Optional logger.</param>
    public AssetLoader(
        IAssetSource source,
        IAssetCache? cache = null,
        AssetLoaderOptions? options = null,
        ILogger<AssetLoader>? logger = null)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _cache = cache;
        _options = options ?? new AssetLoaderOptions();
        _logger = logger;
        _registry = new BundleRegistry();
        _downloader = new AssetDownloader(_options.DownloadOptions, logger != null ? null : null);
        _downloadSemaphore = new SemaphoreSlim(_options.MaxConcurrentDownloads);
    }

    /// <summary>
    /// Registers a type-specific loader for deserializing assets.
    /// </summary>
    /// <typeparam name="T">Asset type.</typeparam>
    /// <param name="loader">Type loader to register.</param>
    public void RegisterTypeLoader<T>(IAssetTypeLoader<T> loader)
    {
        ArgumentNullException.ThrowIfNull(loader);

        var genericLoader = loader as IAssetTypeLoader
            ?? throw new ArgumentException("Loader must implement IAssetTypeLoader", nameof(loader));

        foreach (var contentType in loader.SupportedContentTypes)
        {
            _typeLoaders[contentType] = genericLoader;
            _logger?.LogDebug("Registered type loader for {ContentType} -> {Type}", contentType, typeof(T).Name);
        }
    }

    /// <summary>
    /// Ensures the specified assets are available, downloading bundles if needed.
    /// </summary>
    /// <param name="assetIds">Asset IDs to make available.</param>
    /// <param name="progress">Optional progress reporter.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Result indicating which assets are available.</returns>
    public async Task<AssetAvailabilityResult> EnsureAssetsAvailableAsync(
        IReadOnlyList<string> assetIds,
        IProgress<BundleDownloadProgress>? progress = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(assetIds);

        // Check which assets are already loaded
        var missing = assetIds.Where(id => !_registry.HasAsset(id)).ToList();
        if (missing.Count == 0)
        {
            _logger?.LogDebug("All {Count} assets already available", assetIds.Count);
            return AssetAvailabilityResult.AllAlreadyAvailable(assetIds);
        }

        _logger?.LogInformation("Resolving {Count} missing assets", missing.Count);

        // Resolve bundles for missing assets
        var resolution = await _source.ResolveBundlesAsync(
            missing,
            _registry.GetLoadedBundleIds().ToList(),
            ct).ConfigureAwait(false);

        // Download required bundles
        var downloadedBundles = new List<string>();
        var downloadTasks = new List<Task<BundleLoadResult>>();

        foreach (var bundleInfo in resolution.Bundles)
        {
            downloadTasks.Add(LoadBundleInternalAsync(
                bundleInfo.BundleId,
                bundleInfo.DownloadUrl,
                progress,
                ct));
        }

        var results = await Task.WhenAll(downloadTasks).ConfigureAwait(false);
        downloadedBundles.AddRange(results
            .Where(r => r.Status == BundleLoadStatus.Success || r.Status == BundleLoadStatus.AlreadyLoaded)
            .Select(r => r.BundleId));

        return new AssetAvailabilityResult
        {
            RequestedAssetIds = assetIds,
            DownloadedBundleIds = downloadedBundles,
            UnresolvedAssetIds = resolution.UnresolvedAssetIds ?? Array.Empty<string>()
        };
    }

    /// <summary>
    /// Loads a bundle from URL or cache.
    /// </summary>
    /// <param name="bundleId">Bundle ID to load.</param>
    /// <param name="downloadUrl">Download URL for the bundle.</param>
    /// <param name="progress">Optional progress reporter.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Load result.</returns>
    public async Task<BundleLoadResult> LoadBundleAsync(
        string bundleId,
        Uri downloadUrl,
        IProgress<BundleDownloadProgress>? progress = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(bundleId);
        ArgumentNullException.ThrowIfNull(downloadUrl);

        return await LoadBundleInternalAsync(bundleId, downloadUrl, progress, ct).ConfigureAwait(false);
    }

    private async Task<BundleLoadResult> LoadBundleInternalAsync(
        string bundleId,
        Uri downloadUrl,
        IProgress<BundleDownloadProgress>? progress,
        CancellationToken ct)
    {
        // Check if already loaded
        if (_registry.HasBundle(bundleId))
        {
            _logger?.LogDebug("Bundle {BundleId} already loaded", bundleId);
            return BundleLoadResult.AlreadyLoaded(bundleId);
        }

        await _downloadSemaphore.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // Double-check after acquiring semaphore
            if (_registry.HasBundle(bundleId))
                return BundleLoadResult.AlreadyLoaded(bundleId);

            var stopwatch = Stopwatch.StartNew();
            Stream bundleStream;
            var fromCache = false;

            // Check cache first
            if (_options.PreferCache && _cache != null)
            {
                var cachedStream = await _cache.GetBundleStreamAsync(bundleId, ct).ConfigureAwait(false);
                if (cachedStream != null)
                {
                    _logger?.LogDebug("Loading bundle {BundleId} from cache", bundleId);
                    bundleStream = cachedStream;
                    fromCache = true;
                }
                else
                {
                    bundleStream = await DownloadAndCacheAsync(bundleId, downloadUrl, progress, ct).ConfigureAwait(false);
                }
            }
            else if (downloadUrl.Scheme == "file")
            {
                // Local file
                var filePath = downloadUrl.LocalPath;
                if (!File.Exists(filePath))
                {
                    return BundleLoadResult.Failed(bundleId, $"Local file not found: {filePath}");
                }
                bundleStream = File.OpenRead(filePath);
                fromCache = true;
            }
            else
            {
                bundleStream = await DownloadAndCacheAsync(bundleId, downloadUrl, progress, ct).ConfigureAwait(false);
            }

            // Parse bundle
            var reader = new BannouBundleReader(bundleStream);
            await reader.ReadHeaderAsync(ct).ConfigureAwait(false);

            if (reader.Manifest == null)
            {
                reader.Dispose();
                bundleStream.Dispose();
                return BundleLoadResult.Failed(bundleId, "Failed to parse bundle manifest");
            }

            var loadedBundle = new LoadedBundle
            {
                BundleId = bundleId,
                Manifest = reader.Manifest,
                AssetIds = reader.Manifest.Assets.Select(a => a.AssetId).ToList(),
                Reader = reader
            };

            _registry.Register(loadedBundle);

            stopwatch.Stop();
            _logger?.LogInformation("Loaded bundle {BundleId} with {AssetCount} assets in {Time}ms (cache: {FromCache})",
                bundleId, loadedBundle.AssetIds.Count, stopwatch.ElapsedMilliseconds, fromCache);

            return BundleLoadResult.Success(
                bundleId,
                loadedBundle.AssetIds.Count,
                fromCache,
                stopwatch.ElapsedMilliseconds,
                reader.Manifest.TotalCompressedSize);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to load bundle {BundleId}", bundleId);
            return BundleLoadResult.Failed(bundleId, ex.Message);
        }
        finally
        {
            _downloadSemaphore.Release();
        }
    }

    private async Task<Stream> DownloadAndCacheAsync(
        string bundleId,
        Uri downloadUrl,
        IProgress<BundleDownloadProgress>? progress,
        CancellationToken ct)
    {
        _logger?.LogInformation("Downloading bundle {BundleId} from {Url}", bundleId, downloadUrl);

        var result = await _downloader.DownloadAsync(downloadUrl, bundleId, progress: progress, ct: ct)
            .ConfigureAwait(false);

        if (_cache != null)
        {
            // Store in cache (create a copy of the stream)
            var cacheStream = new MemoryStream(result.Stream.ToArray());
            await _cache.StoreBundleAsync(bundleId, cacheStream, result.ContentHash, ct).ConfigureAwait(false);
        }

        result.Stream.Position = 0;
        return result.Stream;
    }

    /// <summary>
    /// Gets raw asset bytes from a loaded bundle.
    /// </summary>
    /// <param name="assetId">Asset ID to get.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Asset bytes, or null if not found.</returns>
    public async Task<byte[]?> GetAssetBytesAsync(string assetId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(assetId);

        var bundleId = _registry.FindBundleForAsset(assetId);
        if (bundleId == null)
        {
            _logger?.LogWarning("Asset {AssetId} not found in any loaded bundle", assetId);
            return null;
        }

        var bundle = _registry.GetBundle(bundleId);
        if (bundle == null)
            return null;

        return await bundle.ReadAssetAsync(assetId, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Loads an asset as a specific type using a registered type loader.
    /// </summary>
    /// <typeparam name="T">Asset type to load.</typeparam>
    /// <param name="assetId">Asset ID to load.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Loaded asset, or default if not found or load failed.</returns>
    public async Task<AssetLoadResult<T>> LoadAssetAsync<T>(string assetId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(assetId);

        var bundleId = _registry.FindBundleForAsset(assetId);
        if (bundleId == null)
            return AssetLoadResult<T>.Failed(assetId, "Asset not found in any loaded bundle");

        var bundle = _registry.GetBundle(bundleId);
        if (bundle == null)
            return AssetLoadResult<T>.Failed(assetId, "Bundle not found");

        var entry = bundle.GetAssetEntry(assetId);
        if (entry == null)
            return AssetLoadResult<T>.Failed(assetId, "Asset entry not found in bundle");

        if (!_typeLoaders.TryGetValue(entry.ContentType, out var loader))
            return AssetLoadResult<T>.Failed(assetId, $"No type loader registered for content type '{entry.ContentType}'");

        if (loader.AssetType != typeof(T))
            return AssetLoadResult<T>.Failed(assetId, $"Type mismatch: loader produces {loader.AssetType.Name}, requested {typeof(T).Name}");

        try
        {
            var data = await bundle.ReadAssetAsync(assetId, ct).ConfigureAwait(false);
            if (data == null)
                return AssetLoadResult<T>.Failed(assetId, "Failed to read asset data");

            var asset = await loader.LoadAsObjectAsync(data, entry, ct).ConfigureAwait(false);
            return AssetLoadResult<T>.Succeeded(assetId, (T)asset, bundleId);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to load asset {AssetId} as {Type}", assetId, typeof(T).Name);
            return AssetLoadResult<T>.Failed(assetId, ex.Message);
        }
    }

    /// <summary>
    /// Gets asset metadata from a loaded bundle.
    /// </summary>
    /// <param name="assetId">Asset ID to get metadata for.</param>
    /// <returns>Asset entry, or null if not found.</returns>
    public BundleAssetEntry? GetAssetEntry(string assetId)
    {
        var bundleId = _registry.FindBundleForAsset(assetId);
        if (bundleId == null)
            return null;

        return _registry.GetBundle(bundleId)?.GetAssetEntry(assetId);
    }

    /// <summary>
    /// Unloads a bundle and releases its resources.
    /// </summary>
    /// <param name="bundleId">Bundle ID to unload.</param>
    public void UnloadBundle(string bundleId)
    {
        ArgumentException.ThrowIfNullOrEmpty(bundleId);
        _registry.Unregister(bundleId);
        _logger?.LogDebug("Unloaded bundle {BundleId}", bundleId);
    }

    /// <summary>
    /// Unloads all bundles.
    /// </summary>
    public void UnloadAllBundles()
    {
        _registry.Clear();
        _logger?.LogInformation("Unloaded all bundles");
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        _registry.Clear();
        _downloader.Dispose();
        _downloadSemaphore.Dispose();

        if (_cache is IDisposable disposableCache)
            disposableCache.Dispose();

        return ValueTask.CompletedTask;
    }
}
