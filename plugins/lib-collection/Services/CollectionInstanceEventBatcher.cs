using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Collection;

/// <summary>
/// Manages batch event publishing for collection instance lifecycle events.
/// Created uses accumulating batcher (each collection creation is unique).
/// Destroyed uses accumulating batcher (each collection deletion is unique).
/// Collections are immutable after creation, so no modified batcher is needed.
/// </summary>
/// <remarks>
/// Registered as Singleton. Service methods call Add* synchronously (non-blocking).
/// A single <see cref="EventBatcherWorker"/> flushes both batchers per cycle.
/// </remarks>
public class CollectionInstanceEventBatcher
{
    private readonly EventBatcher<CollectionBatchEntry> _created;
    private readonly EventBatcher<CollectionBatchDestroyedEntry> _destroyed;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<CollectionInstanceEventBatcher> _logger;

    /// <summary>
    /// All flushable batchers for registration with <see cref="EventBatcherWorker"/>.
    /// </summary>
    public IFlushable[] AllFlushables => new IFlushable[] { _created, _destroyed };

    /// <summary>
    /// Initializes the collection instance event batcher with flush callbacks that
    /// publish via generated batch event publisher methods.
    /// </summary>
    public CollectionInstanceEventBatcher(
        IServiceProvider serviceProvider,
        ILogger<CollectionInstanceEventBatcher> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;

        _created = new EventBatcher<CollectionBatchEntry>(
            FlushCreatedAsync, e => e.CreatedAt, logger);
        _destroyed = new EventBatcher<CollectionBatchDestroyedEntry>(
            FlushDestroyedAsync, e => e.CreatedAt, logger);
    }

    /// <summary>Records a collection instance creation for the next batch flush.</summary>
    public void AddCreated(CollectionBatchEntry entry) => _created.Add(entry);

    /// <summary>Records a collection instance destruction for the next batch flush.</summary>
    public void AddDestroyed(CollectionBatchDestroyedEntry entry) => _destroyed.Add(entry);

    private async Task FlushCreatedAsync(List<CollectionBatchEntry> entries, DateTimeOffset windowStart, CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var messageBus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
        await messageBus.PublishCollectionBatchCreatedAsync(new CollectionBatchCreatedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            Entries = entries,
            Count = entries.Count,
            WindowStartedAt = windowStart
        }, ct);
        _logger.LogInformation("Published batch collection.batch-created with {Count} entries", entries.Count);
    }

    private async Task FlushDestroyedAsync(List<CollectionBatchDestroyedEntry> entries, DateTimeOffset windowStart, CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var messageBus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
        await messageBus.PublishCollectionBatchDestroyedAsync(new CollectionBatchDestroyedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            Entries = entries,
            Count = entries.Count,
            WindowStartedAt = windowStart
        }, ct);
        _logger.LogInformation("Published batch collection.batch-destroyed with {Count} entries", entries.Count);
    }
}
