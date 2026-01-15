using System.Collections.Concurrent;
using BeyondImmersion.Bannou.AssetLoader.Abstractions;

namespace BeyondImmersion.Bannou.AssetLoader.Cache;

/// <summary>
/// In-memory LRU cache for downloaded bundles.
/// Transient - does not persist across restarts.
/// Use for hot data or when disk caching is not desired.
/// </summary>
public sealed class MemoryAssetCache : IAssetCache
{
    private readonly ConcurrentDictionary<string, MemoryCacheEntry> _entries = new();
    private readonly SemaphoreSlim _evictionLock = new(1, 1);
    private int _hitCount;
    private int _missCount;

    /// <inheritdoc />
    public long MaxSizeBytes { get; set; }

    /// <summary>
    /// Creates a new in-memory asset cache.
    /// </summary>
    /// <param name="maxSizeBytes">Maximum cache size in bytes (default 256MB).</param>
    public MemoryAssetCache(long maxSizeBytes = 256 * 1024 * 1024)
    {
        MaxSizeBytes = maxSizeBytes;
    }

    /// <inheritdoc />
    public Task<bool> HasBundleAsync(string bundleId, string? contentHash = null, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(bundleId);

        if (!_entries.TryGetValue(bundleId, out var entry))
            return Task.FromResult(false);

        if (contentHash != null && !string.Equals(entry.ContentHash, contentHash, StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(false);

        return Task.FromResult(true);
    }

    /// <inheritdoc />
    public Task<Stream?> GetBundleStreamAsync(string bundleId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(bundleId);

        if (!_entries.TryGetValue(bundleId, out var entry))
        {
            Interlocked.Increment(ref _missCount);
            return Task.FromResult<Stream?>(null);
        }

        // Update access time for LRU
        entry.LastAccessedAt = DateTimeOffset.UtcNow;
        Interlocked.Increment(ref _hitCount);

        // Return a copy so the caller can dispose it independently
        return Task.FromResult<Stream?>(new MemoryStream(entry.Data, writable: false));
    }

    /// <inheritdoc />
    public async Task StoreBundleAsync(string bundleId, Stream data, string contentHash, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(bundleId);
        ArgumentNullException.ThrowIfNull(data);
        ArgumentException.ThrowIfNullOrEmpty(contentHash);

        // Read all data into memory
        using var memoryStream = new MemoryStream();
        await data.CopyToAsync(memoryStream, ct).ConfigureAwait(false);
        var bytes = memoryStream.ToArray();

        // Evict if necessary
        var currentSize = GetTotalSizeBytes();
        if (currentSize + bytes.Length > MaxSizeBytes)
        {
            await EvictToSizeAsync(MaxSizeBytes - bytes.Length, ct).ConfigureAwait(false);
        }

        var entry = new MemoryCacheEntry
        {
            BundleId = bundleId,
            ContentHash = contentHash,
            Data = bytes,
            CreatedAt = DateTimeOffset.UtcNow,
            LastAccessedAt = DateTimeOffset.UtcNow
        };

        _entries[bundleId] = entry;
    }

    /// <inheritdoc />
    public Task RemoveBundleAsync(string bundleId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(bundleId);
        _entries.TryRemove(bundleId, out _);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public CacheStats GetStats()
    {
        return new CacheStats
        {
            TotalBytes = GetTotalSizeBytes(),
            BundleCount = _entries.Count,
            MaxBytes = MaxSizeBytes,
            HitCount = _hitCount,
            MissCount = _missCount
        };
    }

    /// <inheritdoc />
    public async Task EvictToSizeAsync(long targetBytes, CancellationToken ct = default)
    {
        if (targetBytes < 0)
            throw new ArgumentOutOfRangeException(nameof(targetBytes));

        await _evictionLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var currentSize = GetTotalSizeBytes();
            if (currentSize <= targetBytes)
                return;

            var entriesToEvict = _entries.Values
                .OrderBy(e => e.LastAccessedAt)
                .ToList();

            foreach (var entry in entriesToEvict)
            {
                ct.ThrowIfCancellationRequested();

                if (currentSize <= targetBytes)
                    break;

                if (_entries.TryRemove(entry.BundleId, out _))
                {
                    currentSize -= entry.Data.Length;
                }
            }
        }
        finally
        {
            _evictionLock.Release();
        }
    }

    /// <inheritdoc />
    public Task ClearAsync(CancellationToken ct = default)
    {
        _entries.Clear();
        _hitCount = 0;
        _missCount = 0;
        return Task.CompletedTask;
    }

    private long GetTotalSizeBytes()
        => _entries.Values.Sum(e => (long)e.Data.Length);

    private sealed class MemoryCacheEntry
    {
        public required string BundleId { get; init; }
        public required string ContentHash { get; init; }
        public required byte[] Data { get; init; }
        public required DateTimeOffset CreatedAt { get; init; }
        public DateTimeOffset LastAccessedAt { get; set; }
    }
}
