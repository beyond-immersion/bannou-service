namespace BeyondImmersion.Bannou.AssetLoader.Abstractions;

/// <summary>
/// Caching abstraction for downloaded bundles.
/// Implementations provide different storage strategies:
/// - FileAssetCache: Disk-based LRU cache (persistent across restarts)
/// - MemoryAssetCache: In-memory cache (transient, for hot data)
/// </summary>
public interface IAssetCache
{
    /// <summary>
    /// Checks if a bundle is cached.
    /// </summary>
    /// <param name="bundleId">Bundle ID to check.</param>
    /// <param name="contentHash">Optional content hash to verify freshness.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if bundle is cached (and hash matches if provided).</returns>
    Task<bool> HasBundleAsync(string bundleId, string? contentHash = null, CancellationToken ct = default);

    /// <summary>
    /// Gets a stream to read the cached bundle data.
    /// Returns null if bundle is not cached.
    /// </summary>
    /// <param name="bundleId">Bundle ID to retrieve.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Stream to read bundle data, or null if not cached.</returns>
    Task<Stream?> GetBundleStreamAsync(string bundleId, CancellationToken ct = default);

    /// <summary>
    /// Stores a bundle in the cache.
    /// </summary>
    /// <param name="bundleId">Bundle ID to store.</param>
    /// <param name="data">Stream containing bundle data.</param>
    /// <param name="contentHash">Content hash for integrity verification.</param>
    /// <param name="ct">Cancellation token.</param>
    Task StoreBundleAsync(string bundleId, Stream data, string contentHash, CancellationToken ct = default);

    /// <summary>
    /// Removes a bundle from the cache.
    /// </summary>
    /// <param name="bundleId">Bundle ID to remove.</param>
    /// <param name="ct">Cancellation token.</param>
    Task RemoveBundleAsync(string bundleId, CancellationToken ct = default);

    /// <summary>
    /// Gets current cache statistics.
    /// </summary>
    CacheStats GetStats();

    /// <summary>
    /// Evicts cached items to reach the target size.
    /// Uses LRU (Least Recently Used) strategy.
    /// </summary>
    /// <param name="targetBytes">Target size in bytes.</param>
    /// <param name="ct">Cancellation token.</param>
    Task EvictToSizeAsync(long targetBytes, CancellationToken ct = default);

    /// <summary>
    /// Clears all cached data.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    Task ClearAsync(CancellationToken ct = default);

    /// <summary>
    /// Maximum cache size in bytes.
    /// </summary>
    long MaxSizeBytes { get; set; }
}

/// <summary>
/// Statistics about cache state and usage.
/// </summary>
public sealed class CacheStats
{
    /// <summary>Total size of cached data in bytes.</summary>
    public required long TotalBytes { get; init; }

    /// <summary>Number of cached bundles.</summary>
    public required int BundleCount { get; init; }

    /// <summary>Maximum cache size in bytes.</summary>
    public required long MaxBytes { get; init; }

    /// <summary>Number of cache hits since startup.</summary>
    public required int HitCount { get; init; }

    /// <summary>Number of cache misses since startup.</summary>
    public required int MissCount { get; init; }

    /// <summary>Cache hit rate (0.0 to 1.0).</summary>
    public double HitRate => HitCount + MissCount > 0
        ? (double)HitCount / (HitCount + MissCount)
        : 0.0;

    /// <summary>Cache usage as percentage of max size.</summary>
    public double UsagePercent => MaxBytes > 0
        ? (double)TotalBytes / MaxBytes * 100
        : 0.0;
}
