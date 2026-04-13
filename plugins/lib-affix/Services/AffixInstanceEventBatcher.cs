using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Affix;

/// <summary>
/// Singleton service managing batch lifecycle event accumulation for affix instances.
/// Three batchers: created (accumulating), modified (deduplicating), destroyed (accumulating).
/// Flushed by EventBatcherWorker on a configurable interval.
/// </summary>
[BannouHelperService("affix-instance-event-batcher", typeof(AffixService), lifetime: ServiceLifetime.Singleton)]
[TelemetrySpanExempt("EventBatcher flush callbacks — single publish call per flush, nested under EventBatcherWorker per-cycle span")]
public class AffixInstanceEventBatcher
{
    private readonly EventBatcher<AffixInstanceBatchEntry> _created;
    private readonly DeduplicatingEventBatcher<Guid, AffixInstanceBatchModifiedEntry> _modified;
    private readonly EventBatcher<AffixInstanceBatchDestroyedEntry> _destroyed;
    private readonly IServiceProvider _serviceProvider;

    /// <summary>All flushable batchers for EventBatcherWorker registration.</summary>
    public IFlushable[] AllFlushables => new IFlushable[] { _created, _modified, _destroyed };

    /// <summary>
    /// Initializes the event batcher with flush callbacks that publish batch events.
    /// </summary>
    public AffixInstanceEventBatcher(IServiceProvider serviceProvider, ILogger<AffixInstanceEventBatcher> logger)
    {
        _serviceProvider = serviceProvider;

        _created = new EventBatcher<AffixInstanceBatchEntry>(
            FlushCreatedAsync, e => e.CreatedAt, logger);
        _modified = new DeduplicatingEventBatcher<Guid, AffixInstanceBatchModifiedEntry>(
            FlushModifiedAsync, e => e.CreatedAt, logger);
        _destroyed = new EventBatcher<AffixInstanceBatchDestroyedEntry>(
            FlushDestroyedAsync, e => e.CreatedAt, logger);
    }

    /// <summary>Adds an entry for a newly created affix instance.</summary>
    public void AddCreated(AffixInstanceBatchEntry entry) => _created.Add(entry);

    /// <summary>Adds or updates a modified affix instance entry (last-write-wins).</summary>
    public void AddModified(Guid itemInstanceId, AffixInstanceBatchModifiedEntry entry) => _modified.Add(itemInstanceId, entry);

    /// <summary>Adds an entry for a destroyed affix instance.</summary>
    public void AddDestroyed(AffixInstanceBatchDestroyedEntry entry) => _destroyed.Add(entry);

    private async Task FlushCreatedAsync(List<AffixInstanceBatchEntry> entries, DateTimeOffset windowStart, CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var messageBus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
        await messageBus.PublishAffixInstanceBatchCreatedAsync(new AffixInstanceBatchCreatedEvent
        {
            Entries = entries,
            Count = entries.Count,
            WindowStartedAt = windowStart
        }, ct);
    }

    private async Task FlushModifiedAsync(List<AffixInstanceBatchModifiedEntry> entries, DateTimeOffset windowStart, CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var messageBus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
        await messageBus.PublishAffixInstanceBatchModifiedAsync(new AffixInstanceBatchModifiedEvent
        {
            Entries = entries,
            Count = entries.Count,
            WindowStartedAt = windowStart
        }, ct);
    }

    private async Task FlushDestroyedAsync(List<AffixInstanceBatchDestroyedEntry> entries, DateTimeOffset windowStart, CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var messageBus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
        await messageBus.PublishAffixInstanceBatchDestroyedAsync(new AffixInstanceBatchDestroyedEvent
        {
            Entries = entries,
            Count = entries.Count,
            WindowStartedAt = windowStart
        }, ct);
    }
}
