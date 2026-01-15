using BeyondImmersion.Bannou.AssetLoader.Abstractions;
using BeyondImmersion.Bannou.Bundle.Format;

namespace BeyondImmersion.Bannou.AssetLoader.Sources;

/// <summary>
/// IAssetSource implementation for local filesystem bundles.
/// Use for offline mode, development, or pre-downloaded assets.
/// Automatically scans directories for .bannou files.
/// </summary>
public sealed class FileSystemAssetSource : IAssetSource
{
    private readonly Dictionary<string, LocalBundleInfo> _bundleInfos = new();

    /// <inheritdoc />
    public bool RequiresAuthentication => false;

    /// <inheritdoc />
    public bool IsAvailable => true;

    /// <summary>
    /// Scans a directory for bundle files and registers them.
    /// </summary>
    /// <param name="directory">Directory to scan.</param>
    /// <param name="searchPattern">Pattern for bundle files (default: *.bannou).</param>
    /// <param name="recursive">Whether to search subdirectories.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Number of bundles registered.</returns>
    public async Task<int> ScanDirectoryAsync(
        string directory,
        string searchPattern = "*.bannou",
        bool recursive = true,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(directory);

        if (!Directory.Exists(directory))
            throw new DirectoryNotFoundException($"Directory not found: {directory}");

        var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var files = Directory.GetFiles(directory, searchPattern, searchOption);
        var count = 0;

        foreach (var filePath in files)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                await RegisterBundleFileAsync(filePath, ct).ConfigureAwait(false);
                count++;
            }
            catch
            {
                // Skip invalid bundle files
            }
        }

        return count;
    }

    /// <summary>
    /// Registers a specific bundle file.
    /// </summary>
    /// <param name="filePath">Path to the bundle file.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task RegisterBundleFileAsync(string filePath, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(filePath);

        if (!File.Exists(filePath))
            throw new FileNotFoundException("Bundle file not found", filePath);

        // Read manifest to get bundle info
        await using var stream = File.OpenRead(filePath);
        using var reader = new BannouBundleReader(stream);
        await reader.ReadHeaderAsync(ct).ConfigureAwait(false);

        var manifest = reader.Manifest ?? throw new InvalidOperationException("Failed to read bundle manifest");

        var info = new LocalBundleInfo
        {
            BundleId = manifest.BundleId,
            FilePath = filePath,
            SizeBytes = new FileInfo(filePath).Length,
            AssetIds = manifest.Assets.Select(a => a.AssetId).ToList()
        };

        _bundleInfos[manifest.BundleId] = info;
    }

    /// <summary>
    /// Manually registers a bundle with custom ID.
    /// </summary>
    /// <param name="bundleId">Bundle identifier to use.</param>
    /// <param name="filePath">Path to the bundle file.</param>
    /// <param name="assetIds">Asset IDs contained in the bundle.</param>
    public void RegisterBundle(string bundleId, string filePath, IReadOnlyList<string> assetIds)
    {
        ArgumentException.ThrowIfNullOrEmpty(bundleId);
        ArgumentException.ThrowIfNullOrEmpty(filePath);
        ArgumentNullException.ThrowIfNull(assetIds);

        _bundleInfos[bundleId] = new LocalBundleInfo
        {
            BundleId = bundleId,
            FilePath = filePath,
            SizeBytes = File.Exists(filePath) ? new FileInfo(filePath).Length : 0,
            AssetIds = assetIds
        };
    }

    /// <inheritdoc />
    public Task<BundleResolutionResult> ResolveBundlesAsync(
        IReadOnlyList<string> assetIds,
        IReadOnlyList<string>? excludeBundleIds = null,
        CancellationToken ct = default)
    {
        var excludeSet = excludeBundleIds?.ToHashSet() ?? new HashSet<string>();
        var resolvedBundles = new List<ResolvedBundleInfo>();
        var unresolvedAssets = new List<string>();
        var resolvedAssetIds = new HashSet<string>();

        // Find bundles containing the requested assets
        foreach (var (bundleId, bundleInfo) in _bundleInfos)
        {
            if (excludeSet.Contains(bundleId))
                continue;

            var matchingAssets = assetIds
                .Where(id => bundleInfo.AssetIds.Contains(id))
                .Where(id => !resolvedAssetIds.Contains(id))
                .ToList();

            if (matchingAssets.Count > 0)
            {
                resolvedBundles.Add(new ResolvedBundleInfo
                {
                    BundleId = bundleId,
                    DownloadUrl = new Uri($"file://{bundleInfo.FilePath}"),
                    SizeBytes = bundleInfo.SizeBytes,
                    ExpiresAt = DateTimeOffset.MaxValue, // Local files don't expire
                    IncludedAssetIds = matchingAssets
                });

                foreach (var assetId in matchingAssets)
                {
                    resolvedAssetIds.Add(assetId);
                }
            }
        }

        // Track unresolved assets
        foreach (var assetId in assetIds)
        {
            if (!resolvedAssetIds.Contains(assetId))
            {
                unresolvedAssets.Add(assetId);
            }
        }

        return Task.FromResult(new BundleResolutionResult
        {
            Bundles = resolvedBundles,
            StandaloneAssets = Array.Empty<ResolvedAssetInfo>(), // Local source doesn't support standalone assets
            UnresolvedAssetIds = unresolvedAssets.Count > 0 ? unresolvedAssets : null
        });
    }

    /// <inheritdoc />
    public Task<BundleDownloadInfo?> GetBundleDownloadInfoAsync(string bundleId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(bundleId);

        if (!_bundleInfos.TryGetValue(bundleId, out var info))
            return Task.FromResult<BundleDownloadInfo?>(null);

        return Task.FromResult<BundleDownloadInfo?>(new BundleDownloadInfo
        {
            BundleId = bundleId,
            DownloadUrl = new Uri($"file://{info.FilePath}"),
            SizeBytes = info.SizeBytes,
            AssetIds = info.AssetIds,
            ExpiresAt = DateTimeOffset.MaxValue
        });
    }

    /// <inheritdoc />
    public Task<AssetDownloadInfo?> GetAssetDownloadInfoAsync(string assetId, CancellationToken ct = default)
    {
        // FileSystemAssetSource doesn't support standalone assets
        return Task.FromResult<AssetDownloadInfo?>(null);
    }

    /// <summary>
    /// Gets a stream for reading a local bundle file.
    /// </summary>
    /// <param name="bundleId">Bundle ID to read.</param>
    /// <returns>Stream for reading the bundle, or null if not found.</returns>
    public Stream? GetBundleStream(string bundleId)
    {
        if (!_bundleInfos.TryGetValue(bundleId, out var info))
            return null;

        if (!File.Exists(info.FilePath))
            return null;

        return File.OpenRead(info.FilePath);
    }

    /// <summary>
    /// Gets all registered bundle IDs.
    /// </summary>
    public IEnumerable<string> GetBundleIds() => _bundleInfos.Keys;

    private sealed class LocalBundleInfo
    {
        public required string BundleId { get; init; }
        public required string FilePath { get; init; }
        public required long SizeBytes { get; init; }
        public required IReadOnlyList<string> AssetIds { get; init; }
    }
}
