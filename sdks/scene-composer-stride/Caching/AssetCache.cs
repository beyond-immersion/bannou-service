using System;
using System.Collections.Generic;

namespace BeyondImmersion.Bannou.Stride.SceneComposer.Caching;

/// <summary>
/// LRU cache for loaded assets with size-based eviction.
/// </summary>
/// <remarks>
/// Thread-safe cache that tracks asset size and automatically evicts
/// least recently used assets when the cache exceeds its size limit.
/// </remarks>
public sealed class AssetCache : IDisposable
{
    private readonly long _maxSizeBytes;
    private readonly LinkedList<CacheEntry> _lruList;
    private readonly Dictionary<string, LinkedListNode<CacheEntry>> _lookup;
    private readonly object _lock = new();
    private long _currentSize;
    private bool _disposed;

    /// <summary>
    /// Creates a new asset cache with the specified size limit.
    /// </summary>
    /// <param name="maxSizeBytes">Maximum cache size in bytes. Default is 256 MB.</param>
    public AssetCache(long maxSizeBytes = 256 * 1024 * 1024)
    {
        _maxSizeBytes = maxSizeBytes;
        _lruList = new LinkedList<CacheEntry>();
        _lookup = new Dictionary<string, LinkedListNode<CacheEntry>>(StringComparer.Ordinal);
    }

    /// <summary>
    /// Gets the current cache size in bytes.
    /// </summary>
    /// <exception cref="ObjectDisposedException">Thrown if the cache has been disposed.</exception>
    public long CurrentSize
    {
        get
        {
            lock (_lock)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                return _currentSize;
            }
        }
    }

    /// <summary>
    /// Gets the number of cached assets.
    /// </summary>
    /// <exception cref="ObjectDisposedException">Thrown if the cache has been disposed.</exception>
    public int Count
    {
        get
        {
            lock (_lock)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                return _lookup.Count;
            }
        }
    }

    /// <summary>
    /// Gets the maximum cache size in bytes.
    /// </summary>
    public long MaxSize => _maxSizeBytes;

    /// <summary>
    /// Attempts to get a cached asset.
    /// </summary>
    /// <typeparam name="T">Expected asset type.</typeparam>
    /// <param name="assetId">Asset identifier.</param>
    /// <param name="asset">Retrieved asset if found.</param>
    /// <returns>True if asset was found and is of correct type.</returns>
    /// <exception cref="ObjectDisposedException">Thrown if the cache has been disposed.</exception>
    public bool TryGet<T>(string assetId, out T? asset) where T : class
    {
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_lookup.TryGetValue(assetId, out var node))
            {
                // Move to front (most recently used)
                _lruList.Remove(node);
                _lruList.AddFirst(node);

                if (node.Value.Asset is T typedAsset)
                {
                    asset = typedAsset;
                    return true;
                }
            }

            asset = default;
            return false;
        }
    }

    /// <summary>
    /// Adds or updates an asset in the cache.
    /// </summary>
    /// <param name="assetId">Asset identifier.</param>
    /// <param name="asset">Asset to cache.</param>
    /// <param name="sizeBytes">Size of the asset in bytes for eviction tracking.</param>
    /// <exception cref="ObjectDisposedException">Thrown if the cache has been disposed.</exception>
    public void Add(string assetId, object asset, long sizeBytes = 0)
    {
        ArgumentNullException.ThrowIfNull(assetId);
        ArgumentNullException.ThrowIfNull(asset);

        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            // Remove existing entry if present
            if (_lookup.TryGetValue(assetId, out var existingNode))
            {
                _currentSize -= existingNode.Value.SizeBytes;
                _lruList.Remove(existingNode);
                _lookup.Remove(assetId);
            }

            // Evict entries until we have room
            while (_currentSize + sizeBytes > _maxSizeBytes && _lruList.Count > 0)
            {
                var last = _lruList.Last
                    ?? throw new InvalidOperationException("LRU list is unexpectedly empty during eviction");
                EvictEntry(last);
            }

            // Add new entry at front
            var entry = new CacheEntry(assetId, asset, sizeBytes);
            var node = _lruList.AddFirst(entry);
            _lookup[assetId] = node;
            _currentSize += sizeBytes;
        }
    }

    /// <summary>
    /// Removes an asset from the cache.
    /// </summary>
    /// <param name="assetId">Asset identifier.</param>
    /// <returns>True if asset was removed.</returns>
    /// <exception cref="ObjectDisposedException">Thrown if the cache has been disposed.</exception>
    public bool Remove(string assetId)
    {
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_lookup.TryGetValue(assetId, out var node))
            {
                EvictEntry(node);
                return true;
            }
            return false;
        }
    }

    /// <summary>
    /// Clears all cached assets.
    /// </summary>
    /// <exception cref="ObjectDisposedException">Thrown if the cache has been disposed.</exception>
    public void Clear()
    {
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ClearInternal();
        }
    }

    /// <summary>
    /// Internal clear that doesn't check disposed state (for use during Dispose).
    /// </summary>
    private void ClearInternal()
    {
        foreach (var node in _lruList)
        {
            DisposeAsset(node.Asset);
        }
        _lruList.Clear();
        _lookup.Clear();
        _currentSize = 0;
    }

    /// <summary>
    /// Checks if an asset is cached.
    /// </summary>
    /// <param name="assetId">Asset identifier.</param>
    /// <returns>True if the asset is in the cache.</returns>
    /// <exception cref="ObjectDisposedException">Thrown if the cache has been disposed.</exception>
    public bool Contains(string assetId)
    {
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _lookup.ContainsKey(assetId);
        }
    }

    private void EvictEntry(LinkedListNode<CacheEntry> node)
    {
        _lruList.Remove(node);
        _lookup.Remove(node.Value.AssetId);
        _currentSize -= node.Value.SizeBytes;
        DisposeAsset(node.Value.Asset);
    }

    private static void DisposeAsset(object asset)
    {
        if (asset is IDisposable disposable)
        {
            try
            {
                disposable.Dispose();
            }
            catch
            {
                // Ignore disposal errors
            }
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
                return;

            _disposed = true;
            ClearInternal();
        }
    }

    private sealed record CacheEntry(string AssetId, object Asset, long SizeBytes);
}
