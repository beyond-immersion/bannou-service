using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Item;

/// <summary>
/// Manages batch event publishing for item instance lifecycle events.
/// Created/Destroyed use accumulating batchers (each operation is unique).
/// Modified uses deduplicating batcher (same instance may be modified multiple
/// times in a window; only the latest state matters).
/// </summary>
/// <remarks>
/// Registered as Singleton. Service methods call Add* synchronously (non-blocking).
/// A single <see cref="EventBatcherWorker"/> flushes all three batchers per cycle.
/// </remarks>
public class ItemInstanceEventBatcher
{
    private readonly EventBatcher<ItemInstanceBatchEntry> _created;
    private readonly DeduplicatingEventBatcher<Guid, ItemInstanceBatchModifiedEntry> _modified;
    private readonly EventBatcher<ItemInstanceBatchDestroyedEntry> _destroyed;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ItemInstanceEventBatcher> _logger;

    /// <summary>
    /// All flushable batchers for registration with <see cref="EventBatcherWorker"/>.
    /// </summary>
    public IFlushable[] AllFlushables => new IFlushable[] { _created, _modified, _destroyed };

    /// <summary>
    /// Initializes the item instance event batcher with flush callbacks that
    /// publish via generated batch event publisher methods.
    /// </summary>
    public ItemInstanceEventBatcher(
        IServiceProvider serviceProvider,
        ILogger<ItemInstanceEventBatcher> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;

        _created = new EventBatcher<ItemInstanceBatchEntry>(
            FlushCreatedAsync, e => e.CreatedAt, logger);
        _modified = new DeduplicatingEventBatcher<Guid, ItemInstanceBatchModifiedEntry>(
            FlushModifiedAsync, e => e.CreatedAt, logger);
        _destroyed = new EventBatcher<ItemInstanceBatchDestroyedEntry>(
            FlushDestroyedAsync, e => e.CreatedAt, logger);
    }

    /// <summary>Records an item instance creation for the next batch flush.</summary>
    public void AddCreated(ItemInstanceBatchEntry entry) => _created.Add(entry);

    /// <summary>Records an item instance modification for the next batch flush. Last-write-wins by instanceId.</summary>
    public void AddModified(Guid instanceId, ItemInstanceBatchModifiedEntry entry) => _modified.Add(instanceId, entry);

    /// <summary>Records an item instance destruction for the next batch flush.</summary>
    public void AddDestroyed(ItemInstanceBatchDestroyedEntry entry) => _destroyed.Add(entry);

    private async Task FlushCreatedAsync(List<ItemInstanceBatchEntry> entries, DateTimeOffset windowStart, CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var messageBus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
        await messageBus.PublishItemInstanceBatchCreatedAsync(new ItemInstanceBatchCreatedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            Entries = entries,
            Count = entries.Count,
            WindowStartedAt = windowStart
        }, ct);
        _logger.LogInformation("Published batch item.instance.batch-created with {Count} entries", entries.Count);
    }

    private async Task FlushModifiedAsync(List<ItemInstanceBatchModifiedEntry> entries, DateTimeOffset windowStart, CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var messageBus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
        await messageBus.PublishItemInstanceBatchModifiedAsync(new ItemInstanceBatchModifiedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            Entries = entries,
            Count = entries.Count,
            WindowStartedAt = windowStart
        }, ct);
        _logger.LogInformation("Published batch item.instance.batch-modified with {Count} entries", entries.Count);
    }

    private async Task FlushDestroyedAsync(List<ItemInstanceBatchDestroyedEntry> entries, DateTimeOffset windowStart, CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var messageBus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
        await messageBus.PublishItemInstanceBatchDestroyedAsync(new ItemInstanceBatchDestroyedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            Entries = entries,
            Count = entries.Count,
            WindowStartedAt = windowStart
        }, ct);
        _logger.LogInformation("Published batch item.instance.batch-destroyed with {Count} entries", entries.Count);
    }
}
