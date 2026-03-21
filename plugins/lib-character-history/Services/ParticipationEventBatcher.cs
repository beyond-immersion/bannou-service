using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.CharacterHistory;

/// <summary>
/// Manages batch event publishing for participation lifecycle events.
/// Created/Destroyed use accumulating batchers (each operation is unique).
/// Participations are immutable after creation, so no Modified batcher is needed
/// (the generated BatchModified infrastructure exists but is never called).
/// </summary>
/// <remarks>
/// Registered as Singleton. Service methods call Add* synchronously (non-blocking).
/// A single <see cref="EventBatcherWorker"/> flushes both batchers per cycle.
/// </remarks>
[BannouHelperService("participation-batcher", typeof(ICharacterHistoryService), lifetime: ServiceLifetime.Singleton, DependencyMode = DependencyRegistrationMode.Concrete)]
public class ParticipationEventBatcher
{
    private readonly EventBatcher<ParticipationBatchEntry> _created;
    private readonly EventBatcher<ParticipationBatchDestroyedEntry> _destroyed;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ParticipationEventBatcher> _logger;

    /// <summary>
    /// All flushable batchers for registration with <see cref="EventBatcherWorker"/>.
    /// </summary>
    public IFlushable[] AllFlushables => new IFlushable[] { _created, _destroyed };

    /// <summary>
    /// Initializes the participation event batcher with flush callbacks that
    /// publish via generated batch event publisher methods.
    /// </summary>
    public ParticipationEventBatcher(
        IServiceProvider serviceProvider,
        ILogger<ParticipationEventBatcher> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;

        _created = new EventBatcher<ParticipationBatchEntry>(
            FlushCreatedAsync, e => e.CreatedAt, logger);
        _destroyed = new EventBatcher<ParticipationBatchDestroyedEntry>(
            FlushDestroyedAsync, e => e.CreatedAt, logger);
    }

    /// <summary>Records a participation creation for the next batch flush.</summary>
    public void AddCreated(ParticipationBatchEntry entry) => _created.Add(entry);

    /// <summary>Records a participation destruction for the next batch flush.</summary>
    public void AddDestroyed(ParticipationBatchDestroyedEntry entry) => _destroyed.Add(entry);

    private async Task FlushCreatedAsync(List<ParticipationBatchEntry> entries, DateTimeOffset windowStart, CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var messageBus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
        await messageBus.PublishParticipationBatchCreatedAsync(new ParticipationBatchCreatedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            Entries = entries,
            Count = entries.Count,
            WindowStartedAt = windowStart
        }, ct);
        _logger.LogInformation("Published batch character-history.participation.batch-created with {Count} entries", entries.Count);
    }

    private async Task FlushDestroyedAsync(List<ParticipationBatchDestroyedEntry> entries, DateTimeOffset windowStart, CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var messageBus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
        await messageBus.PublishParticipationBatchDestroyedAsync(new ParticipationBatchDestroyedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            Entries = entries,
            Count = entries.Count,
            WindowStartedAt = windowStart
        }, ct);
        _logger.LogInformation("Published batch character-history.participation.batch-destroyed with {Count} entries", entries.Count);
    }
}
