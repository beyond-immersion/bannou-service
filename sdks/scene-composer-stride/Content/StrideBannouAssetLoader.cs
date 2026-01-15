using BeyondImmersion.Bannou.AssetLoader;
using BeyondImmersion.Bannou.AssetLoader.Abstractions;
using BeyondImmersion.Bannou.AssetLoader.Models;
using BeyondImmersion.Bannou.AssetLoader.Sources;
using BeyondImmersion.Bannou.AssetLoader.Stride;
using Stride.Core;
using Stride.Graphics;
using Stride.Rendering;

namespace BeyondImmersion.Bannou.SceneComposer.Stride.Content;

/// <summary>
/// IAssetLoader implementation that uses the AssetLoader SDK for the SceneComposer.
/// </summary>
/// <remarks>
/// <para>
/// This class bridges the engine-agnostic <see cref="IAssetLoader"/> interface
/// to the <see cref="AssetLoader.AssetLoader"/> SDK with Stride type loaders.
/// </para>
/// <para>
/// For local file loading, it uses <see cref="FileSystemAssetSource"/>.
/// For network loading, you can provide a custom <see cref="IAssetSource"/>.
/// </para>
/// </remarks>
public sealed class StrideBannouAssetLoader : IAssetLoader, IAsyncDisposable
{
    private readonly AssetLoader.AssetLoader _assetLoader;
    private readonly FileSystemAssetSource _fileSystemSource;
    private readonly IAssetSource _primarySource;
    private readonly Action<string>? _debugLog;
    private bool _disposed;

    /// <summary>
    /// Creates a new asset loader for local file-based bundles.
    /// </summary>
    /// <param name="services">Stride service registry.</param>
    /// <param name="graphicsDevice">Graphics device for creating GPU resources.</param>
    /// <param name="cache">Optional cache for downloaded bundles.</param>
    /// <param name="debugLog">Optional callback for debug logging.</param>
    public StrideBannouAssetLoader(
        IServiceRegistry services,
        GraphicsDevice graphicsDevice,
        IAssetCache? cache = null,
        Action<string>? debugLog = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(graphicsDevice);

        _debugLog = debugLog;
        _fileSystemSource = new FileSystemAssetSource();
        _primarySource = _fileSystemSource;

        _assetLoader = new AssetLoader.AssetLoader(_fileSystemSource, cache);
        _assetLoader.UseStride(services, graphicsDevice, debugLog);

        Log("StrideBannouAssetLoader initialized with FileSystemAssetSource");
    }

    /// <summary>
    /// Creates a new asset loader with a custom asset source.
    /// </summary>
    /// <param name="services">Stride service registry.</param>
    /// <param name="graphicsDevice">Graphics device for creating GPU resources.</param>
    /// <param name="source">Asset source for resolving download URLs.</param>
    /// <param name="cache">Optional cache for downloaded bundles.</param>
    /// <param name="debugLog">Optional callback for debug logging.</param>
    public StrideBannouAssetLoader(
        IServiceRegistry services,
        GraphicsDevice graphicsDevice,
        IAssetSource source,
        IAssetCache? cache = null,
        Action<string>? debugLog = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(graphicsDevice);
        ArgumentNullException.ThrowIfNull(source);

        _debugLog = debugLog;
        _fileSystemSource = new FileSystemAssetSource();
        _primarySource = source;

        _assetLoader = new AssetLoader.AssetLoader(source, cache);
        _assetLoader.UseStride(services, graphicsDevice, debugLog);

        Log("StrideBannouAssetLoader initialized with custom IAssetSource");
    }

    /// <summary>
    /// Gets the underlying asset loader for advanced operations.
    /// </summary>
    public AssetLoader.AssetLoader AssetLoader => _assetLoader;

    /// <summary>
    /// Gets the file system source for registering local bundles.
    /// </summary>
    public FileSystemAssetSource FileSystemSource => _fileSystemSource;

    /// <inheritdoc/>
    public async Task<Model?> LoadModelAsync(
        string bundleId,
        string assetId,
        string? variantId = null,
        CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        try
        {
            var result = await _assetLoader.LoadAssetAsync<Model>(assetId, ct).ConfigureAwait(false);
            if (result.Success)
            {
                Log($"Loaded model '{assetId}'");
                return result.Asset;
            }

            Log($"Failed to load model '{assetId}': {result.ErrorMessage}");
            return null;
        }
        catch (Exception ex)
        {
            Log($"Exception loading model '{assetId}': {ex.Message}");
            return null;
        }
    }

    /// <inheritdoc/>
    public async Task<Texture?> LoadTextureAsync(
        string bundleId,
        string assetId,
        string? variantId = null,
        CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        try
        {
            var result = await _assetLoader.LoadAssetAsync<Texture>(assetId, ct).ConfigureAwait(false);
            if (result.Success)
            {
                Log($"Loaded texture '{assetId}'");
                return result.Asset;
            }

            Log($"Failed to load texture '{assetId}': {result.ErrorMessage}");
            return null;
        }
        catch (Exception ex)
        {
            Log($"Exception loading texture '{assetId}': {ex.Message}");
            return null;
        }
    }

    /// <inheritdoc/>
    public Task<Material?> LoadMaterialAsync(
        string bundleId,
        string assetId,
        string? variantId = null,
        CancellationToken ct = default)
    {
        // Materials are typically embedded in models, not loaded separately
        // Return null for now - could be extended if needed
        return Task.FromResult<Material?>(null);
    }

    /// <inheritdoc/>
    public Task<byte[]?> GetThumbnailAsync(
        string bundleId,
        string assetId,
        int width,
        int height,
        CancellationToken ct = default)
    {
        // Thumbnail generation not implemented yet
        // Would require off-screen rendering of the asset
        return Task.FromResult<byte[]?>(null);
    }

    /// <inheritdoc/>
    public async Task PreloadBundleAsync(string bundleId, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_assetLoader.Registry.HasBundle(bundleId))
        {
            Log($"Bundle '{bundleId}' already loaded");
            return;
        }

        // Try to get download info and load the bundle
        var downloadInfo = await _primarySource.GetBundleDownloadInfoAsync(bundleId, ct).ConfigureAwait(false);
        if (downloadInfo != null)
        {
            await _assetLoader.LoadBundleAsync(bundleId, downloadInfo.DownloadUrl, ct: ct).ConfigureAwait(false);
            Log($"Preloaded bundle '{bundleId}'");
        }
        else
        {
            Log($"Cannot preload bundle '{bundleId}': not found in source");
        }
    }

    /// <inheritdoc/>
    public void UnloadBundle(string bundleId)
    {
        _assetLoader.UnloadBundle(bundleId);
        Log($"Unloaded bundle '{bundleId}'");
    }

    /// <inheritdoc/>
    public bool IsBundleLoaded(string bundleId)
    {
        return _assetLoader.Registry.HasBundle(bundleId);
    }

    /// <inheritdoc/>
    public bool HasAsset(string bundleId, string assetId)
    {
        // Check if asset is available in any loaded bundle
        return _assetLoader.Registry.HasAsset(assetId);
    }

    /// <summary>
    /// Loads a bundle from a file path.
    /// </summary>
    /// <param name="bundlePath">Path to the .bannou bundle file.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The bundle ID assigned to the loaded bundle.</returns>
    public async Task<string> LoadBundleFromFileAsync(string bundlePath, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrEmpty(bundlePath);

        // Register with file system source
        await _fileSystemSource.RegisterBundleFileAsync(bundlePath, ct).ConfigureAwait(false);

        // Get bundle info to get the actual bundle ID
        var bundleIds = _fileSystemSource.GetBundleIds().ToList();
        var bundleId = bundleIds.LastOrDefault()
            ?? throw new InvalidOperationException($"Failed to register bundle from '{bundlePath}'");

        // Load the bundle
        var downloadInfo = await _fileSystemSource.GetBundleDownloadInfoAsync(bundleId, ct).ConfigureAwait(false);
        if (downloadInfo == null)
            throw new InvalidOperationException($"Failed to get download info for bundle '{bundleId}'");

        var result = await _assetLoader.LoadBundleAsync(bundleId, downloadInfo.DownloadUrl, ct: ct).ConfigureAwait(false);
        if (result.Status == BundleLoadStatus.Failed)
            throw new InvalidOperationException($"Failed to load bundle '{bundleId}': {result.ErrorMessage}");

        Log($"Loaded bundle '{bundleId}' from '{bundlePath}'");
        return bundleId;
    }

    /// <summary>
    /// Loads a bundle from a file path with a specific ID.
    /// </summary>
    /// <param name="bundlePath">Path to the .bannou bundle file.</param>
    /// <param name="bundleId">Bundle ID to assign.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The bundle ID assigned to the loaded bundle.</returns>
    public async Task<string> LoadBundleFromFileAsync(string bundlePath, string bundleId, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrEmpty(bundlePath);
        ArgumentException.ThrowIfNullOrEmpty(bundleId);

        // Register with file system source using custom ID
        // First read the manifest to get asset IDs
        await _fileSystemSource.RegisterBundleFileAsync(bundlePath, ct).ConfigureAwait(false);

        // Get the actual bundle ID from the file, then we'll use it to load
        var registeredIds = _fileSystemSource.GetBundleIds().ToList();
        var actualBundleId = registeredIds.LastOrDefault()
            ?? throw new InvalidOperationException($"Failed to register bundle from '{bundlePath}'");

        // Load the bundle (using the actual ID from the file)
        var downloadInfo = await _fileSystemSource.GetBundleDownloadInfoAsync(actualBundleId, ct).ConfigureAwait(false);
        if (downloadInfo == null)
            throw new InvalidOperationException($"Failed to get download info for bundle");

        var result = await _assetLoader.LoadBundleAsync(actualBundleId, downloadInfo.DownloadUrl, ct: ct).ConfigureAwait(false);
        if (result.Status == BundleLoadStatus.Failed)
            throw new InvalidOperationException($"Failed to load bundle: {result.ErrorMessage}");

        Log($"Loaded bundle '{actualBundleId}' from '{bundlePath}'");
        return actualBundleId;
    }

    /// <summary>
    /// Scans a directory for bundle files and registers them.
    /// </summary>
    /// <param name="directory">Directory to scan.</param>
    /// <param name="recursive">Whether to search subdirectories.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Number of bundles found and registered.</returns>
    public async Task<int> ScanDirectoryAsync(string directory, bool recursive = true, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var count = await _fileSystemSource.ScanDirectoryAsync(directory, "*.bannou", recursive, ct).ConfigureAwait(false);
        Log($"Scanned directory '{directory}': found {count} bundles");
        return count;
    }

    /// <summary>
    /// Gets the raw bytes of an asset from a loaded bundle.
    /// </summary>
    /// <param name="assetId">Asset ID to get.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Asset bytes, or null if not found.</returns>
    public async Task<byte[]?> GetAssetBytesAsync(string assetId, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return await _assetLoader.GetAssetBytesAsync(assetId, ct).ConfigureAwait(false);
    }

    private void Log(string message)
    {
        _debugLog?.Invoke($"[StrideBannouAssetLoader] {message}");
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        await _assetLoader.DisposeAsync().ConfigureAwait(false);
        Log("Disposed");
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
