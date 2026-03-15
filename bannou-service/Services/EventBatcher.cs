using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace BeyondImmersion.BannouService.Services;

/// <summary>
/// Accumulates events without deduplication, flushing periodically as batch events.
/// Thread-safe. Per-node in-memory accumulation (each node publishes its own
/// batches independently).
/// </summary>
/// <remarks>
/// <para>
/// <b>IMPLEMENTATION TENETS - Multi-Instance Safety:</b>
/// The ConcurrentBag accumulator is in-memory per-node. Each node publishes
/// its own batch independently. Analytics aggregates per-node data. This is
/// by-design for observability — batch events are fire-and-forget with no
/// functional dependency.
/// </para>
/// <para>
/// <b>Mode 1 (Accumulating):</b> Every entry is preserved. Use for events where
/// each operation is unique: item created/destroyed, currency credit/debit,
/// encounter recorded, journey waypoint, blessing granted, etc.
/// </para>
/// </remarks>
/// <typeparam name="TEntry">Entry payload type (e.g., the individual event data).</typeparam>
public class EventBatcher<TEntry> : IFlushable
{
    private ConcurrentBag<TEntry> _entries = new();
    private DateTimeOffset _windowStartedAt = DateTimeOffset.UtcNow;
    private readonly Func<List<TEntry>, DateTimeOffset, CancellationToken, Task> _flushCallback;
    private readonly Func<TEntry, DateTimeOffset> _timestampSelector;
    private readonly ILogger _logger;

    /// <summary>
    /// Initializes an accumulating event batcher.
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
    public EventBatcher(
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
    /// Appends an entry for inclusion in the next batch flush.
    /// Thread-safe, non-blocking, synchronous.
    /// </summary>
    /// <param name="entry">The entry to accumulate.</param>
    public void Add(TEntry entry)
    {
        _entries.Add(entry);
    }

    /// <summary>
    /// Appends multiple entries for inclusion in the next batch flush.
    /// Thread-safe, non-blocking, synchronous. Use when a batch endpoint
    /// creates multiple entities in a single call.
    /// </summary>
    /// <param name="entries">The entries to accumulate.</param>
    public void AddRange(IEnumerable<TEntry> entries)
    {
        foreach (var entry in entries)
        {
            _entries.Add(entry);
        }
    }

    /// <inheritdoc/>
    public async Task FlushAsync(CancellationToken ct)
    {
        if (_entries.IsEmpty) return;

        var snapshot = Interlocked.Exchange(ref _entries, new ConcurrentBag<TEntry>());

        if (snapshot.IsEmpty) return;

        var entries = snapshot.ToList();
        entries.Sort((a, b) => _timestampSelector(a).CompareTo(_timestampSelector(b)));

        var windowStart = _windowStartedAt;
        _windowStartedAt = DateTimeOffset.UtcNow;

        await _flushCallback(entries, windowStart, ct);

        _logger.LogDebug(
            "EventBatcher<{EntryType}> flushed {Count} entries",
            typeof(TEntry).Name, entries.Count);
    }
}
