using System.Collections.Concurrent;
using System.Text.Json;
using BeyondImmersion.Bannou.AssetLoader.Abstractions;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.Bannou.AssetLoader.Cache;

/// <summary>
/// Disk-based LRU cache for downloaded bundles.
/// Persists across application restarts.
/// </summary>
public sealed class FileAssetCache : IAssetCache, IDisposable
{
    private readonly string _cacheDirectory;
    private readonly ILogger<FileAssetCache>? _logger;
    private readonly ConcurrentDictionary<string, CacheEntry> _entries = new();
    private readonly SemaphoreSlim _evictionLock = new(1, 1);
    private readonly string _indexPath;
    private int _hitCount;
    private int _missCount;

    private const string IndexFileName = "cache-index.json";
    private const string BundleExtension = ".bannou";

    /// <inheritdoc />
    public long MaxSizeBytes { get; set; }

    /// <summary>
    /// Creates a new file-based asset cache.
    /// </summary>
    /// <param name="cacheDirectory">Directory to store cached bundles.</param>
    /// <param name="maxSizeBytes">Maximum cache size in bytes (default 1GB).</param>
    /// <param name="logger">Optional logger.</param>
    public FileAssetCache(string cacheDirectory, long maxSizeBytes = 1024 * 1024 * 1024, ILogger<FileAssetCache>? logger = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(cacheDirectory);

        _cacheDirectory = cacheDirectory;
        _indexPath = Path.Combine(cacheDirectory, IndexFileName);
        MaxSizeBytes = maxSizeBytes;
        _logger = logger;

        Directory.CreateDirectory(cacheDirectory);
        LoadIndex();
    }

    /// <inheritdoc />
    public async Task<bool> HasBundleAsync(string bundleId, string? contentHash = null, CancellationToken ct = default)
    {
        await Task.CompletedTask; // Synchronous file check - placeholder for future async implementation

        ArgumentException.ThrowIfNullOrEmpty(bundleId);

        if (!_entries.TryGetValue(bundleId, out var entry))
            return false;

        // If hash provided, verify it matches
        if (contentHash != null && !string.Equals(entry.ContentHash, contentHash, StringComparison.OrdinalIgnoreCase))
            return false;

        // Verify file still exists
        var filePath = GetBundlePath(bundleId);
        return File.Exists(filePath);
    }

    /// <inheritdoc />
    public async Task<Stream?> GetBundleStreamAsync(string bundleId, CancellationToken ct = default)
    {
        await Task.CompletedTask; // Synchronous file access - placeholder for future async implementation

        ArgumentException.ThrowIfNullOrEmpty(bundleId);

        if (!_entries.TryGetValue(bundleId, out var entry))
        {
            Interlocked.Increment(ref _missCount);
            return null;
        }

        var filePath = GetBundlePath(bundleId);
        if (!File.Exists(filePath))
        {
            _entries.TryRemove(bundleId, out _);
            Interlocked.Increment(ref _missCount);
            return null;
        }

        // Update access time for LRU
        entry.LastAccessedAt = DateTimeOffset.UtcNow;
        Interlocked.Increment(ref _hitCount);

        _logger?.LogDebug("Cache hit for bundle {BundleId}", bundleId);

        return File.OpenRead(filePath);
    }

    /// <inheritdoc />
    public async Task StoreBundleAsync(string bundleId, Stream data, string contentHash, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(bundleId);
        ArgumentNullException.ThrowIfNull(data);
        ArgumentException.ThrowIfNullOrEmpty(contentHash);

        var filePath = GetBundlePath(bundleId);
        var tempPath = filePath + ".tmp";

        try
        {
            // Write to temp file first
            await using (var fileStream = File.Create(tempPath))
            {
                await data.CopyToAsync(fileStream, ct).ConfigureAwait(false);
            }

            var fileInfo = new FileInfo(tempPath);
            var sizeBytes = fileInfo.Length;

            // Evict if necessary to make room
            var currentSize = GetTotalSizeBytes();
            if (currentSize + sizeBytes > MaxSizeBytes)
            {
                await EvictToSizeAsync(MaxSizeBytes - sizeBytes, ct).ConfigureAwait(false);
            }

            // Move temp to final location
            File.Move(tempPath, filePath, overwrite: true);

            var entry = new CacheEntry
            {
                BundleId = bundleId,
                ContentHash = contentHash,
                SizeBytes = sizeBytes,
                CreatedAt = DateTimeOffset.UtcNow,
                LastAccessedAt = DateTimeOffset.UtcNow
            };

            _entries[bundleId] = entry;
            SaveIndex();

            _logger?.LogDebug("Cached bundle {BundleId} ({Size} bytes)", bundleId, sizeBytes);
        }
        catch
        {
            // Clean up temp file on failure
            if (File.Exists(tempPath))
                File.Delete(tempPath);
            throw;
        }
    }

    /// <inheritdoc />
    public Task RemoveBundleAsync(string bundleId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(bundleId);

        if (_entries.TryRemove(bundleId, out _))
        {
            var filePath = GetBundlePath(bundleId);
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }

            SaveIndex();
            _logger?.LogDebug("Removed bundle {BundleId} from cache", bundleId);
        }

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
            throw new ArgumentOutOfRangeException(nameof(targetBytes), "Target size must be non-negative");

        await _evictionLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var currentSize = GetTotalSizeBytes();
            if (currentSize <= targetBytes)
                return;

            // Sort by last access time (oldest first)
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
                    var filePath = GetBundlePath(entry.BundleId);
                    if (File.Exists(filePath))
                    {
                        File.Delete(filePath);
                    }

                    currentSize -= entry.SizeBytes;
                    _logger?.LogDebug("Evicted bundle {BundleId} from cache (LRU)", entry.BundleId);
                }
            }

            SaveIndex();
        }
        finally
        {
            _evictionLock.Release();
        }
    }

    /// <inheritdoc />
    public Task ClearAsync(CancellationToken ct = default)
    {
        foreach (var bundleId in _entries.Keys.ToList())
        {
            ct.ThrowIfCancellationRequested();

            if (_entries.TryRemove(bundleId, out _))
            {
                var filePath = GetBundlePath(bundleId);
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }
            }
        }

        SaveIndex();
        _hitCount = 0;
        _missCount = 0;

        _logger?.LogInformation("Cleared asset cache");
        return Task.CompletedTask;
    }

    private string GetBundlePath(string bundleId)
    {
        // Sanitize bundle ID for filesystem
        var safeName = bundleId
            .Replace('/', '_')
            .Replace('\\', '_')
            .Replace(':', '_');
        return Path.Combine(_cacheDirectory, safeName + BundleExtension);
    }

    private long GetTotalSizeBytes()
        => _entries.Values.Sum(e => e.SizeBytes);

    private void LoadIndex()
    {
        if (!File.Exists(_indexPath))
            return;

        try
        {
            var json = File.ReadAllText(_indexPath);
            var entries = JsonSerializer.Deserialize<List<CacheEntry>>(json);

            if (entries != null)
            {
                foreach (var entry in entries)
                {
                    // Verify file exists before adding to index
                    var filePath = GetBundlePath(entry.BundleId);
                    if (File.Exists(filePath))
                    {
                        _entries[entry.BundleId] = entry;
                    }
                }
            }

            _logger?.LogDebug("Loaded cache index with {Count} entries", _entries.Count);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to load cache index, starting fresh");
            _entries.Clear();
        }
    }

    private void SaveIndex()
    {
        try
        {
            var entries = _entries.Values.ToList();
            var json = JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_indexPath, json);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to save cache index");
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        SaveIndex();
        _evictionLock.Dispose();
    }

    private sealed class CacheEntry
    {
        public required string BundleId { get; init; }
        public required string ContentHash { get; init; }
        public required long SizeBytes { get; init; }
        public required DateTimeOffset CreatedAt { get; init; }
        public DateTimeOffset LastAccessedAt { get; set; }
    }
}
