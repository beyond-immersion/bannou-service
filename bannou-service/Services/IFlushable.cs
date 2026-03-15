namespace BeyondImmersion.BannouService.Services;

/// <summary>
/// Common interface for event batcher flush capability, enabling a single
/// <see cref="EventBatcherWorker"/> to flush multiple batchers of different
/// types (accumulating and deduplicating) per cycle.
/// </summary>
public interface IFlushable
{
    /// <summary>
    /// Whether the batcher has no pending entries. Workers may skip flush
    /// calls when this returns <c>true</c>.
    /// </summary>
    bool IsEmpty { get; }

    /// <summary>
    /// Atomically drains all pending entries, sorts them chronologically,
    /// and invokes the flush callback. No-ops if empty.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    Task FlushAsync(CancellationToken ct);
}
