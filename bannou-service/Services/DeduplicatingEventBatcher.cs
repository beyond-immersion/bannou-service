using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace BeyondImmersion.BannouService.Services;

/// <summary>
/// Accumulates events by key with last-write-wins deduplication, flushing
/// periodically as batch events. Thread-safe. Per-node in-memory accumulation.
/// </summary>
/// <remarks>
/// <para>
/// <b>IMPLEMENTATION TENETS - Multi-Instance Safety:</b>
/// The ConcurrentDictionary accumulator is in-memory per-node. Each node publishes
/// its own batch independently. Analytics aggregates per-node data.
/// </para>
/// <para>
/// <b>Mode 2 (Deduplicating):</b> Same key overwrites previous entry (last-write-wins).
/// Use for events where the same entity may be updated multiple times in a window
/// and only the latest state matters: item instance modified (durability updates),
/// permission registration, relationship updated, personality evolved, etc.
/// </para>
/// </remarks>
/// <typeparam name="TKey">Deduplication key type (e.g., Guid for entityId, string for serviceId).</typeparam>
/// <typeparam name="TEntry">Entry payload type.</typeparam>
public class DeduplicatingEventBatcher<TKey, TEntry> : IFlushable where TKey : notnull
{
    private ConcurrentDictionary<TKey, TEntry> _entries = new();
    private DateTimeOffset _windowStartedAt = DateTimeOffset.UtcNow;
    private readonly Func<List<TEntry>, DateTimeOffset, CancellationToken, Task> _flushCallback;
    private readonly Func<TEntry, DateTimeOffset> _timestampSelector;
    private readonly ILogger _logger;

    /// <summary>
    /// Initializes a deduplicating event batcher.
    /// </summary>
    /// <param name="flushCallback">
    /// Receives the chronologically-sorted entries and the window start time.
    /// Responsible for constructing the batch event and publishing via IMessageBus.
    /// </param>
    /// <param name="timestampSelector">
    /// Extracts the timestamp from an entry for chronological sorting on flush.
    /// For lifecycle entries implementing <c>ILifecycleEvent</c>, use <c>e => e.CreatedAt</c>.
    /// </param>
    /// <param name="logger">Logger for flush diagnostics.</param>
    public DeduplicatingEventBatcher(
        Func<List<TEntry>, DateTimeOffset, CancellationToken, Task> flushCallback,
        Func<TEntry, DateTimeOffset> timestampSelector,
        ILogger logger)
    {
        _flushCallback = flushCallback;
        _timestampSelector = timestampSelector;
        _logger = logger;
    }

    /// <inheritdoc/>
    public bool IsEmpty => _entries.IsEmpty;

    /// <summary>
    /// Records an entry for inclusion in the next batch flush.
    /// If an entry with the same key already exists, it is overwritten (last-write-wins).
    /// Thread-safe, non-blocking, synchronous.
    /// </summary>
    /// <param name="key">Deduplication key (e.g., entityId). Same key overwrites within a window.</param>
    /// <param name="entry">The entry to accumulate.</param>
    public void Add(TKey key, TEntry entry)
    {
        _entries[key] = entry;
    }

    /// <inheritdoc/>
    public async Task FlushAsync(CancellationToken ct)
    {
        if (_entries.IsEmpty) return;

        var snapshot = Interlocked.Exchange(ref _entries, new ConcurrentDictionary<TKey, TEntry>());

        if (snapshot.IsEmpty) return;

        var entries = snapshot.Values.ToList();
        entries.Sort((a, b) => _timestampSelector(a).CompareTo(_timestampSelector(b)));

        var windowStart = _windowStartedAt;
        _windowStartedAt = DateTimeOffset.UtcNow;

        await _flushCallback(entries, windowStart, ct);

        _logger.LogDebug(
            "DeduplicatingEventBatcher<{KeyType},{EntryType}> flushed {Count} entries",
            typeof(TKey).Name, typeof(TEntry).Name, entries.Count);
    }
}
