// ═══════════════════════════════════════════════════════════════════════════
// ABML Expression Cache (LRU, Thread-Safe)
// ═══════════════════════════════════════════════════════════════════════════

using BeyondImmersion.BannouService.Abml.Compiler;
using System.Collections.Concurrent;

namespace BeyondImmersion.BannouService.Abml.Runtime;

/// <summary>
/// Thread-safe LRU cache for compiled expressions.
/// </summary>
public sealed class ExpressionCache
{
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new();
    private readonly int _maxSize;
    private readonly ReaderWriterLockSlim _evictionLock = new();
    private long _accessCounter;
    private long _hitCount;
    private long _missCount;

    /// <summary>Gets the current number of cached expressions.</summary>
    public int Count => _cache.Count;

    /// <summary>Gets the maximum cache size.</summary>
    public int MaxSize => _maxSize;

    /// <summary>Gets cache hit count.</summary>
    public long HitCount => Interlocked.Read(ref _hitCount);

    /// <summary>Gets cache miss count.</summary>
    public long MissCount => Interlocked.Read(ref _missCount);

    /// <summary>Gets cache hit ratio.</summary>
    public double HitRatio => HitCount + MissCount == 0 ? 0 : (double)HitCount / (HitCount + MissCount);

    /// <summary>
    /// Creates a new expression cache.
    /// </summary>
    /// <param name="maxSize">Maximum number of cached expressions.</param>
    public ExpressionCache(int maxSize = VmConfig.DefaultCacheSize)
    {
        if (maxSize <= 0) throw new ArgumentOutOfRangeException(nameof(maxSize));
        _maxSize = maxSize;
    }

    /// <summary>
    /// Gets or compiles an expression.
    /// </summary>
    /// <param name="expression">The expression text.</param>
    /// <param name="compile">Function to compile the expression if not cached.</param>
    /// <returns>The compiled expression.</returns>
    public CompiledExpression GetOrCompile(string expression, Func<string, CompiledExpression> compile)
    {
        ArgumentNullException.ThrowIfNull(expression);
        ArgumentNullException.ThrowIfNull(compile);

        // Try to get from cache
        if (_cache.TryGetValue(expression, out var entry))
        {
            entry.LastAccess = Interlocked.Increment(ref _accessCounter);
            Interlocked.Increment(ref _hitCount);
            return entry.Compiled;
        }

        // Compile the expression
        var compiled = compile(expression);
        Interlocked.Increment(ref _missCount);

        // Add to cache
        var newEntry = new CacheEntry(compiled, Interlocked.Increment(ref _accessCounter));

        if (_cache.TryAdd(expression, newEntry))
        {
            // Check if eviction needed
            if (_cache.Count > _maxSize)
            {
                EvictLeastRecentlyUsed();
            }
        }

        return compiled;
    }

    /// <summary>
    /// Tries to get a cached expression.
    /// </summary>
    public bool TryGet(string expression, out CompiledExpression? compiled)
    {
        if (_cache.TryGetValue(expression, out var entry))
        {
            entry.LastAccess = Interlocked.Increment(ref _accessCounter);
            Interlocked.Increment(ref _hitCount);
            compiled = entry.Compiled;
            return true;
        }

        Interlocked.Increment(ref _missCount);
        compiled = null;
        return false;
    }

    /// <summary>
    /// Adds a compiled expression to the cache.
    /// </summary>
    public void Add(string expression, CompiledExpression compiled)
    {
        ArgumentNullException.ThrowIfNull(expression);
        ArgumentNullException.ThrowIfNull(compiled);

        var entry = new CacheEntry(compiled, Interlocked.Increment(ref _accessCounter));
        _cache.AddOrUpdate(expression, entry, (_, _) => entry);

        if (_cache.Count > _maxSize)
        {
            EvictLeastRecentlyUsed();
        }
    }

    /// <summary>
    /// Removes an expression from the cache.
    /// </summary>
    public bool Remove(string expression)
    {
        return _cache.TryRemove(expression, out _);
    }

    /// <summary>
    /// Clears all cached expressions.
    /// </summary>
    public void Clear()
    {
        _cache.Clear();
        Interlocked.Exchange(ref _hitCount, 0);
        Interlocked.Exchange(ref _missCount, 0);
        Interlocked.Exchange(ref _accessCounter, 0);
    }

    /// <summary>
    /// Checks if an expression is cached.
    /// </summary>
    public bool Contains(string expression)
    {
        return _cache.ContainsKey(expression);
    }

    /// <summary>
    /// Gets cache statistics.
    /// </summary>
    public CacheStatistics GetStatistics()
    {
        return new CacheStatistics(
            Count,
            _maxSize,
            HitCount,
            MissCount,
            HitRatio
        );
    }

    private void EvictLeastRecentlyUsed()
    {
        if (!_evictionLock.TryEnterWriteLock(TimeSpan.FromMilliseconds(100)))
        {
            // Another thread is handling eviction
            return;
        }

        try
        {
            // Only evict if still over limit
            if (_cache.Count <= _maxSize) return;

            // Find entries to evict (oldest 10%)
            var targetEvictions = Math.Max(1, _maxSize / 10);
            var entries = _cache.ToArray()
                .OrderBy(e => e.Value.LastAccess)
                .Take(targetEvictions)
                .ToList();

            foreach (var entry in entries)
            {
                _cache.TryRemove(entry.Key, out _);
            }
        }
        finally
        {
            _evictionLock.ExitWriteLock();
        }
    }

    private sealed class CacheEntry
    {
        public CompiledExpression Compiled { get; }
        public long LastAccess { get; set; }

        public CacheEntry(CompiledExpression compiled, long lastAccess)
        {
            Compiled = compiled;
            LastAccess = lastAccess;
        }
    }
}

/// <summary>
/// Cache statistics.
/// </summary>
public readonly record struct CacheStatistics(
    int CurrentSize,
    int MaxSize,
    long HitCount,
    long MissCount,
    double HitRatio
);
