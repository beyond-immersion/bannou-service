using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace BeyondImmersion.Bannou.Client;

/// <summary>
/// Caches assets downloaded from remote URLs with optional CRC32 verification
/// and disk persistence.
/// </summary>
/// <typeparam name="T">The type of asset being cached.</typeparam>
/// <remarks>
/// This class provides:
/// - Memory caching with ConcurrentDictionary for thread safety
/// - Optional disk persistence for offline access
/// - CRC32 verification for data integrity
/// - Automatic URL-based asset ID extraction
/// </remarks>
public sealed class RemoteAssetCache<T> where T : class
{
    private readonly ILogger? _logger;
    private readonly HttpClient _httpClient;
    private readonly Func<byte[], T?> _deserializer;
    private readonly ConcurrentDictionary<string, CachedAsset<T>> _cache = new();
    private readonly string _cacheDirectory;
    private readonly string _fileExtension;

    /// <summary>
    /// Creates a new RemoteAssetCache.
    /// </summary>
    /// <param name="httpClient">HTTP client for downloading assets.</param>
    /// <param name="deserializer">Function to deserialize bytes to asset type T.</param>
    /// <param name="cacheDirectory">Directory for disk persistence (null to use temp).</param>
    /// <param name="fileExtension">File extension for cached files (default: ".asset").</param>
    /// <param name="logger">Optional logger instance.</param>
    public RemoteAssetCache(
        HttpClient httpClient,
        Func<byte[], T?> deserializer,
        string? cacheDirectory = null,
        string fileExtension = ".asset",
        ILogger? logger = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _deserializer = deserializer ?? throw new ArgumentNullException(nameof(deserializer));
        _logger = logger;
        _fileExtension = fileExtension;
        _cacheDirectory = cacheDirectory ?? Path.Combine(Path.GetTempPath(), "bannou-asset-cache");

        // Ensure cache directory exists
        Directory.CreateDirectory(_cacheDirectory);
    }

    /// <summary>
    /// Gets a cached asset by URL, downloading if not cached.
    /// </summary>
    /// <param name="assetUrl">URL to the asset (may be pre-signed).</param>
    /// <param name="expectedCrc">Optional CRC32 for verification.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The cached asset entry, or null if download/verification failed.</returns>
    public async Task<CachedAsset<T>?> GetOrDownloadAsync(
        string assetUrl,
        uint? expectedCrc = null,
        CancellationToken cancellationToken = default)
    {
        // Check memory cache first
        if (_cache.TryGetValue(assetUrl, out var cached))
        {
            _logger?.LogDebug("Asset cache hit: {Url}", TruncateUrl(assetUrl));
            return cached;
        }

        // Download the asset
        _logger?.LogInformation("Downloading asset from: {Url}", TruncateUrl(assetUrl));

        try
        {
            var response = await _httpClient.GetAsync(assetUrl, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger?.LogError("Failed to download asset: {StatusCode}", response.StatusCode);
                return null;
            }

            var data = await response.Content.ReadAsByteArrayAsync(cancellationToken);

            // Verify CRC if provided
            if (expectedCrc.HasValue)
            {
                var actualCrc = Crc32.Compute(data);
                if (actualCrc != expectedCrc.Value)
                {
                    _logger?.LogError(
                        "CRC mismatch for asset: expected {Expected:X8}, got {Actual:X8}",
                        expectedCrc.Value, actualCrc);
                    return null;
                }
            }

            // Deserialize the asset
            var asset = _deserializer(data);
            if (asset == null)
            {
                _logger?.LogError("Failed to deserialize asset from {Url}", TruncateUrl(assetUrl));
                return null;
            }

            // Extract asset ID from URL or generate one
            var assetId = ExtractAssetId(assetUrl);

            var cachedAsset = new CachedAsset<T>
            {
                AssetId = assetId,
                SourceUrl = assetUrl,
                Data = data,
                Asset = asset,
                Crc32 = expectedCrc ?? Crc32.Compute(data),
                CachedAt = DateTime.UtcNow
            };

            // Add to memory cache
            _cache[assetUrl] = cachedAsset;

            // Optionally persist to disk
            await PersistToDiskAsync(cachedAsset, cancellationToken);

            _logger?.LogInformation(
                "Cached asset {AssetId} ({Size} bytes)",
                assetId, data.Length);

            return cachedAsset;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Exception downloading asset from {Url}", TruncateUrl(assetUrl));
            return null;
        }
    }

    /// <summary>
    /// Gets a cached asset by URL without downloading.
    /// </summary>
    /// <param name="assetUrl">URL key for the cached asset.</param>
    /// <returns>The cached asset if found, null otherwise.</returns>
    public CachedAsset<T>? TryGet(string assetUrl)
    {
        _cache.TryGetValue(assetUrl, out var cached);
        return cached;
    }

    /// <summary>
    /// Adds an asset to the cache without downloading.
    /// Useful for pre-loading assets from local storage.
    /// </summary>
    /// <param name="assetUrl">URL key for the asset.</param>
    /// <param name="data">Raw asset data.</param>
    /// <param name="asset">Deserialized asset.</param>
    /// <returns>The cached asset entry.</returns>
    public CachedAsset<T> Add(string assetUrl, byte[] data, T asset)
    {
        var assetId = ExtractAssetId(assetUrl);
        var cachedAsset = new CachedAsset<T>
        {
            AssetId = assetId,
            SourceUrl = assetUrl,
            Data = data,
            Asset = asset,
            Crc32 = Crc32.Compute(data),
            CachedAt = DateTime.UtcNow
        };

        _cache[assetUrl] = cachedAsset;
        return cachedAsset;
    }

    /// <summary>
    /// Removes an asset from the cache.
    /// </summary>
    /// <param name="assetUrl">URL key of the asset to remove.</param>
    /// <returns>True if the asset was removed, false if not found.</returns>
    public bool Invalidate(string assetUrl)
    {
        if (_cache.TryRemove(assetUrl, out var removed))
        {
            _logger?.LogDebug("Invalidated cached asset: {AssetId}", removed.AssetId);
            return true;
        }
        return false;
    }

    /// <summary>
    /// Clears all cached assets from memory.
    /// </summary>
    public void Clear()
    {
        _cache.Clear();
        _logger?.LogInformation("Cleared asset cache");
    }

    /// <summary>
    /// Gets cache statistics.
    /// </summary>
    /// <returns>Current cache statistics.</returns>
    public RemoteAssetCacheStats GetStats()
    {
        var entries = _cache.Values.ToList();
        return new RemoteAssetCacheStats
        {
            CachedCount = entries.Count,
            TotalBytes = entries.Sum(e => e.Data.Length),
            OldestEntry = entries.MinBy(e => e.CachedAt)?.CachedAt,
            NewestEntry = entries.MaxBy(e => e.CachedAt)?.CachedAt
        };
    }

    /// <summary>
    /// Loads an asset from disk cache if available.
    /// </summary>
    /// <param name="assetId">Asset ID to load.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The loaded asset, or null if not found on disk.</returns>
    public async Task<CachedAsset<T>?> LoadFromDiskAsync(string assetId, CancellationToken cancellationToken = default)
    {
        var filePath = Path.Combine(_cacheDirectory, $"{assetId}{_fileExtension}");
        if (!File.Exists(filePath))
        {
            return null;
        }

        try
        {
            var data = await File.ReadAllBytesAsync(filePath, cancellationToken);
            var asset = _deserializer(data);
            if (asset == null)
            {
                return null;
            }

            return new CachedAsset<T>
            {
                AssetId = assetId,
                SourceUrl = $"file://{filePath}",
                Data = data,
                Asset = asset,
                Crc32 = Crc32.Compute(data),
                CachedAt = File.GetLastWriteTimeUtc(filePath)
            };
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to load asset from disk: {AssetId}", assetId);
            return null;
        }
    }

    private async Task PersistToDiskAsync(CachedAsset<T> asset, CancellationToken cancellationToken)
    {
        try
        {
            var filePath = Path.Combine(_cacheDirectory, $"{asset.AssetId}{_fileExtension}");
            await File.WriteAllBytesAsync(filePath, asset.Data, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to persist asset to disk: {AssetId}", asset.AssetId);
        }
    }

    private static string ExtractAssetId(string url)
    {
        // Extract the last path segment before query string as the asset ID
        try
        {
            var uri = new Uri(url);
            var path = uri.AbsolutePath;
            var fileName = Path.GetFileNameWithoutExtension(path);
            return string.IsNullOrEmpty(fileName) ? Guid.NewGuid().ToString("N") : fileName;
        }
        catch
        {
            return Guid.NewGuid().ToString("N");
        }
    }

    private static string TruncateUrl(string url)
    {
        // Truncate URL for logging (remove query params which may contain secrets)
        var idx = url.IndexOf('?');
        return idx > 0 ? url[..idx] + "?..." : url;
    }
}

/// <summary>
/// A cached asset with metadata.
/// </summary>
/// <typeparam name="T">The asset type.</typeparam>
public sealed class CachedAsset<T> where T : class
{
    /// <summary>
    /// Unique asset identifier.
    /// </summary>
    public required string AssetId { get; init; }

    /// <summary>
    /// Source URL this was downloaded from.
    /// </summary>
    public required string SourceUrl { get; init; }

    /// <summary>
    /// Raw asset binary data.
    /// </summary>
    public required byte[] Data { get; init; }

    /// <summary>
    /// Deserialized asset instance.
    /// </summary>
    public required T Asset { get; init; }

    /// <summary>
    /// CRC32 checksum of the data.
    /// </summary>
    public uint Crc32 { get; init; }

    /// <summary>
    /// When this asset was cached.
    /// </summary>
    public DateTime CachedAt { get; init; }
}

/// <summary>
/// Asset cache statistics.
/// </summary>
public sealed class RemoteAssetCacheStats
{
    /// <summary>Number of assets in cache.</summary>
    public int CachedCount { get; init; }

    /// <summary>Total size of cached assets in bytes.</summary>
    public long TotalBytes { get; init; }

    /// <summary>Timestamp of oldest cached entry.</summary>
    public DateTime? OldestEntry { get; init; }

    /// <summary>Timestamp of newest cached entry.</summary>
    public DateTime? NewestEntry { get; init; }
}

/// <summary>
/// CRC32 checksum utility.
/// </summary>
public static class Crc32
{
    private static readonly uint[] Table = GenerateTable();

    private static uint[] GenerateTable()
    {
        const uint polynomial = 0xEDB88320;
        var table = new uint[256];

        for (uint i = 0; i < 256; i++)
        {
            var crc = i;
            for (var j = 0; j < 8; j++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ polynomial : crc >> 1;
            }
            table[i] = crc;
        }

        return table;
    }

    /// <summary>
    /// Computes the CRC32 checksum of the given data.
    /// </summary>
    /// <param name="data">Data to compute checksum for.</param>
    /// <returns>CRC32 checksum.</returns>
    public static uint Compute(byte[] data)
    {
        uint result = 0xFFFFFFFF;
        foreach (var b in data)
        {
            result = Table[(result ^ b) & 0xFF] ^ (result >> 8);
        }
        return result ^ 0xFFFFFFFF;
    }

    /// <summary>
    /// Computes the CRC32 checksum of the given data span.
    /// </summary>
    /// <param name="data">Data span to compute checksum for.</param>
    /// <returns>CRC32 checksum.</returns>
    public static uint Compute(ReadOnlySpan<byte> data)
    {
        uint result = 0xFFFFFFFF;
        foreach (var b in data)
        {
            result = Table[(result ^ b) & 0xFF] ^ (result >> 8);
        }
        return result ^ 0xFFFFFFFF;
    }
}
