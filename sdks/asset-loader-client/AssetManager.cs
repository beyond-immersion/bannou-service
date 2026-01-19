using BeyondImmersion.Bannou.AssetLoader.Abstractions;
using BeyondImmersion.Bannou.AssetLoader.Cache;
using BeyondImmersion.Bannou.AssetLoader.Download;
using BeyondImmersion.Bannou.AssetLoader.Models;
using BeyondImmersion.Bannou.AssetLoader.Registry;
using BeyondImmersion.Bannou.Bundle.Format;
using BeyondImmersion.Bannou.Client;
using Microsoft.Extensions.Logging;

// Alias to avoid conflict with Bundle.Format.BundleAssetEntry
using Realm = BeyondImmersion.BannouService.Asset.Realm;

namespace BeyondImmersion.Bannou.AssetLoader.Client;

/// <summary>
/// Unified facade for game clients to load assets from Bannou.
/// Handles connection, caching, resolution, downloading, and type-specific loading.
/// </summary>
/// <remarks>
/// This is the primary entry point for game developers integrating with the Bannou asset system.
/// It composes the underlying components (source, cache, loader, registry) and provides a simple API.
/// </remarks>
/// <example>
/// <code>
/// // Connect and load assets
/// await using var manager = await AssetManager.ConnectAsync(
///     "wss://bannou.example.com/connect",
///     email,
///     password,
///     new AssetManagerOptions { CacheDirectory = "./cache" });
///
/// // Load assets by ID
/// await manager.LoadAssetsAsync(assetIds, progress);
///
/// // Get typed asset (requires registered type loader)
/// var result = await manager.GetAssetAsync&lt;MyModel&gt;(assetId);
/// if (result.Success)
///     UseModel(result.Asset);
/// </code>
/// </example>
public sealed class AssetManager : IAsyncDisposable
{
    private readonly BannouWebSocketAssetSource _source;
    private readonly AssetLoader _loader;
    private readonly IAssetCache? _cache;
    private readonly ILogger<AssetManager>? _logger;
    private readonly AssetManagerOptions _options;
    private bool _disposed;

    /// <summary>
    /// The bundle registry tracking loaded bundles and assets.
    /// Use this for querying what's currently loaded.
    /// </summary>
    public IBundleRegistry Registry => _loader.Registry;

    /// <summary>
    /// Whether the manager is connected to the server.
    /// </summary>
    public bool IsConnected => _source.IsAvailable;

    /// <summary>
    /// Number of bundles currently loaded.
    /// </summary>
    public int LoadedBundleCount => _loader.Registry.BundleCount;

    /// <summary>
    /// Number of assets currently available.
    /// </summary>
    public int LoadedAssetCount => _loader.Registry.AssetCount;

    /// <summary>
    /// Cache statistics (if caching is enabled).
    /// </summary>
    public CacheStats? CacheStats => _cache?.GetStats();

    private AssetManager(
        BannouWebSocketAssetSource source,
        AssetLoader loader,
        IAssetCache? cache,
        AssetManagerOptions options,
        ILogger<AssetManager>? logger)
    {
        _source = source;
        _loader = loader;
        _cache = cache;
        _options = options;
        _logger = logger;
    }

    /// <summary>
    /// Connects to a Bannou server using email/password authentication.
    /// </summary>
    /// <param name="serverUrl">WebSocket server URL (e.g., "wss://bannou.example.com/connect").</param>
    /// <param name="email">Account email.</param>
    /// <param name="password">Account password.</param>
    /// <param name="options">Optional configuration. If null, uses defaults.</param>
    /// <param name="loggerFactory">Optional logger factory for diagnostics.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Connected AssetManager ready for use.</returns>
    /// <exception cref="InvalidOperationException">Connection failed.</exception>
    public static async Task<AssetManager> ConnectAsync(
        string serverUrl,
        string email,
        string password,
        AssetManagerOptions? options = null,
        ILoggerFactory? loggerFactory = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(serverUrl);
        ArgumentException.ThrowIfNullOrEmpty(email);
        ArgumentException.ThrowIfNullOrEmpty(password);

        options ??= new AssetManagerOptions();
        var logger = loggerFactory?.CreateLogger<AssetManager>();
        var sourceLogger = loggerFactory?.CreateLogger<BannouWebSocketAssetSource>();

        logger?.LogDebug("Connecting to {ServerUrl} with email {Email}", serverUrl, email);

        BannouWebSocketAssetSource? source = await BannouWebSocketAssetSource.ConnectAsync(
            serverUrl,
            email,
            password,
            options.Realm,
            sourceLogger,
            ct).ConfigureAwait(false);

        try
        {
            var result = await CreateFromSourceAsync(source, options, loggerFactory, logger).ConfigureAwait(false);
            source = null; // Ownership transferred to AssetManager
            return result;
        }
        finally
        {
            if (source != null)
                await source.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Connects to a Bannou server using a service token.
    /// </summary>
    /// <param name="serverUrl">WebSocket server URL.</param>
    /// <param name="serviceToken">Service authentication token (e.g., from prior login).</param>
    /// <param name="options">Optional configuration.</param>
    /// <param name="loggerFactory">Optional logger factory.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Connected AssetManager.</returns>
    public static async Task<AssetManager> ConnectWithTokenAsync(
        string serverUrl,
        string serviceToken,
        AssetManagerOptions? options = null,
        ILoggerFactory? loggerFactory = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(serverUrl);
        ArgumentException.ThrowIfNullOrEmpty(serviceToken);

        options ??= new AssetManagerOptions();
        var logger = loggerFactory?.CreateLogger<AssetManager>();
        var sourceLogger = loggerFactory?.CreateLogger<BannouWebSocketAssetSource>();

        logger?.LogDebug("Connecting to {ServerUrl} with token", serverUrl);

        BannouWebSocketAssetSource? source = await BannouWebSocketAssetSource.ConnectWithTokenAsync(
            serverUrl,
            serviceToken,
            options.Realm,
            sourceLogger,
            ct).ConfigureAwait(false);

        try
        {
            var result = await CreateFromSourceAsync(source, options, loggerFactory, logger).ConfigureAwait(false);
            source = null; // Ownership transferred to AssetManager
            return result;
        }
        finally
        {
            if (source != null)
                await source.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Creates an AssetManager using an existing BannouClient connection.
    /// The client connection is not owned by the manager (won't be disposed).
    /// </summary>
    /// <param name="client">Connected BannouClient instance.</param>
    /// <param name="options">Optional configuration.</param>
    /// <param name="loggerFactory">Optional logger factory.</param>
    /// <returns>AssetManager wrapping the client.</returns>
    public static async Task<AssetManager> FromClientAsync(
        IBannouClient client,
        AssetManagerOptions? options = null,
        ILoggerFactory? loggerFactory = null)
    {
        ArgumentNullException.ThrowIfNull(client);

        if (!client.IsConnected)
            throw new InvalidOperationException("Client must be connected");

        options ??= new AssetManagerOptions();
        var logger = loggerFactory?.CreateLogger<AssetManager>();
        var sourceLogger = loggerFactory?.CreateLogger<BannouWebSocketAssetSource>();

        BannouWebSocketAssetSource? source = new BannouWebSocketAssetSource(client, options.Realm, sourceLogger);
        try
        {
            var result = await CreateFromSourceAsync(source, options, loggerFactory, logger).ConfigureAwait(false);
            source = null; // Ownership transferred to AssetManager
            return result;
        }
        finally
        {
            if (source != null)
                await source.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static async Task<AssetManager> CreateFromSourceAsync(
        BannouWebSocketAssetSource source,
        AssetManagerOptions options,
        ILoggerFactory? loggerFactory,
        ILogger<AssetManager>? logger)
    {
        IAssetCache? cache = null;
        AssetLoader? loader = null;
        try
        {
            if (options.EnableCache)
            {
                var cacheLogger = loggerFactory?.CreateLogger<FileAssetCache>();
                cache = new FileAssetCache(options.CacheDirectory, options.MaxCacheSizeBytes, cacheLogger);
                logger?.LogDebug("File cache enabled at {CacheDir} (max {MaxSize} bytes)",
                    options.CacheDirectory, options.MaxCacheSizeBytes);
            }

            var loaderOptions = new AssetLoaderOptions
            {
                ValidateBundles = options.ValidateBundles,
                MaxConcurrentDownloads = options.MaxConcurrentDownloads,
                PreferCache = options.PreferCache
            };

            var loaderLogger = loggerFactory?.CreateLogger<AssetLoader>();
            loader = new AssetLoader(source, cache, loaderOptions, loaderLogger);

            logger?.LogInformation("AssetManager initialized (cache: {CacheEnabled}, validation: {Validation})",
                options.EnableCache, options.ValidateBundles);

            var result = new AssetManager(source, loader, cache, options, logger);
            // Ownership of cache and loader transferred to AssetManager
            cache = null;
            loader = null;
            return result;
        }
        finally
        {
            if (loader != null)
                await loader.DisposeAsync().ConfigureAwait(false);
            (cache as IDisposable)?.Dispose();
        }
    }

    /// <summary>
    /// Ensures the specified assets are available, downloading bundles if needed.
    /// </summary>
    /// <param name="assetIds">Asset IDs to make available.</param>
    /// <param name="progress">Optional progress reporter.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Result indicating which assets are available.</returns>
    public async Task<AssetAvailabilityResult> LoadAssetsAsync(
        IReadOnlyList<string> assetIds,
        IProgress<AssetLoadProgress>? progress = null,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(assetIds);

        if (assetIds.Count == 0)
            return AssetAvailabilityResult.AllAlreadyAvailable(assetIds);

        _logger?.LogInformation("Loading {Count} assets", assetIds.Count);

        // Report resolving phase
        progress?.Report(AssetLoadProgress.Resolving());

        // Create a progress adapter to translate BundleDownloadProgress to AssetLoadProgress
        IProgress<BundleDownloadProgress>? bundleProgress = null;
        if (progress != null)
        {
            var totalBundles = 0;
            var completedBundles = 0;
            long totalBytes = 0;
            long downloadedBytes = 0;

            bundleProgress = new Progress<BundleDownloadProgress>(p =>
            {
                // Update tracking (simplified - real implementation would track per-bundle)
                if (p.Phase == DownloadPhase.Starting)
                {
                    totalBundles++;
                    totalBytes += p.TotalBytes > 0 ? p.TotalBytes : 0;
                }
                else if (p.Phase == DownloadPhase.Complete)
                {
                    completedBundles++;
                    downloadedBytes = totalBytes; // Simplified
                }
                else if (p.Phase == DownloadPhase.Downloading)
                {
                    downloadedBytes += p.BytesDownloaded;
                }

                progress.Report(AssetLoadProgress.Downloading(
                    totalBundles,
                    completedBundles,
                    totalBytes,
                    p.BytesDownloaded,
                    p.BundleId,
                    p.BytesPerSecond));
            });
        }

        var result = await _loader.EnsureAssetsAvailableAsync(assetIds, bundleProgress, ct)
            .ConfigureAwait(false);

        // Report completion
        if (result.AllAvailable)
        {
            progress?.Report(AssetLoadProgress.Complete(
                result.DownloadedBundleIds.Count,
                0)); // Total bytes not tracked in result
        }

        _logger?.LogInformation("Loaded assets: {Available}/{Requested}, bundles downloaded: {Bundles}",
            result.AvailableCount, assetIds.Count, result.DownloadedBundleIds.Count);

        return result;
    }

    /// <summary>
    /// Loads a single asset and makes it available.
    /// Convenience method for loading one asset at a time.
    /// </summary>
    /// <param name="assetId">Asset ID to load.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if asset is now available.</returns>
    public async Task<bool> LoadAssetAsync(string assetId, CancellationToken ct = default)
    {
        var result = await LoadAssetsAsync(new[] { assetId }, progress: null, ct).ConfigureAwait(false);
        return result.AllAvailable;
    }

    /// <summary>
    /// Gets raw asset bytes from a loaded bundle.
    /// </summary>
    /// <param name="assetId">Asset ID to get.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Asset bytes, or null if not loaded.</returns>
    public Task<byte[]?> GetAssetBytesAsync(string assetId, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrEmpty(assetId);
        return _loader.GetAssetBytesAsync(assetId, ct);
    }

    /// <summary>
    /// Loads an asset as a specific type using a registered type loader.
    /// </summary>
    /// <typeparam name="T">Asset type to load.</typeparam>
    /// <param name="assetId">Asset ID to load.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Load result with typed asset or error.</returns>
    public Task<AssetLoadResult<T>> GetAssetAsync<T>(string assetId, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrEmpty(assetId);
        return _loader.LoadAssetAsync<T>(assetId, ct);
    }

    /// <summary>
    /// Gets asset metadata from a loaded bundle.
    /// </summary>
    /// <param name="assetId">Asset ID to query.</param>
    /// <returns>Asset entry with metadata, or null if not loaded.</returns>
    public BundleAssetEntry? GetAssetEntry(string assetId)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrEmpty(assetId);
        return _loader.GetAssetEntry(assetId);
    }

    /// <summary>
    /// Checks if an asset is currently loaded and available.
    /// </summary>
    /// <param name="assetId">Asset ID to check.</param>
    /// <returns>True if asset is available for immediate use.</returns>
    public bool HasAsset(string assetId)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrEmpty(assetId);
        return Registry.HasAsset(assetId);
    }

    /// <summary>
    /// Checks if a bundle is currently loaded.
    /// </summary>
    /// <param name="bundleId">Bundle ID to check.</param>
    /// <returns>True if bundle is loaded.</returns>
    public bool HasBundle(string bundleId)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrEmpty(bundleId);
        return Registry.HasBundle(bundleId);
    }

    /// <summary>
    /// Registers a type-specific loader for deserializing assets.
    /// Call this before loading assets of the specified type.
    /// </summary>
    /// <typeparam name="T">Asset type the loader produces.</typeparam>
    /// <param name="loader">Type loader to register.</param>
    public void RegisterTypeLoader<T>(IAssetTypeLoader<T> loader)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(loader);
        _loader.RegisterTypeLoader(loader);
        _logger?.LogDebug("Registered type loader for {Type}", typeof(T).Name);
    }

    /// <summary>
    /// Unloads a specific bundle and releases its resources.
    /// </summary>
    /// <param name="bundleId">Bundle ID to unload.</param>
    public void UnloadBundle(string bundleId)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrEmpty(bundleId);
        _loader.UnloadBundle(bundleId);
        _logger?.LogDebug("Unloaded bundle {BundleId}", bundleId);
    }

    /// <summary>
    /// Unloads all loaded bundles.
    /// </summary>
    public void UnloadAllBundles()
    {
        ThrowIfDisposed();
        _loader.UnloadAllBundles();
        _logger?.LogInformation("Unloaded all bundles");
    }

    /// <summary>
    /// Clears the asset cache.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    public async Task ClearCacheAsync(CancellationToken ct = default)
    {
        ThrowIfDisposed();
        if (_cache != null)
        {
            await _cache.ClearAsync(ct).ConfigureAwait(false);
            _logger?.LogInformation("Cleared asset cache");
        }
    }

    /// <summary>
    /// Gets IDs of all loaded bundles.
    /// </summary>
    public IEnumerable<string> GetLoadedBundleIds()
    {
        ThrowIfDisposed();
        return Registry.GetLoadedBundleIds();
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(AssetManager));
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;

        await _loader.DisposeAsync().ConfigureAwait(false);
        await _source.DisposeAsync().ConfigureAwait(false);

        if (_cache is IDisposable disposableCache)
            disposableCache.Dispose();

        _logger?.LogDebug("AssetManager disposed");
    }
}
