using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Events;

/// <summary>
/// Base class for type-safe event publishers.
/// Provides consistent event publishing patterns across services.
/// </summary>
public abstract class EventPublisherBase
{
    /// <summary>
    /// Message bus for publishing events.
    /// </summary>
    protected readonly IMessageBus MessageBus;

    /// <summary>
    /// Logger for event publishing operations.
    /// </summary>
    protected readonly ILogger Logger;

    /// <summary>
    /// Creates an event publisher with the specified dependencies.
    /// </summary>
    /// <param name="messageBus">Message bus for publishing.</param>
    /// <param name="logger">Logger for event operations.</param>
    protected EventPublisherBase(IMessageBus messageBus, ILogger logger)
    {
        MessageBus = messageBus ?? throw new ArgumentNullException(nameof(messageBus));
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Publishes an event to the specified topic with standardized error handling.
    /// </summary>
    /// <typeparam name="TEvent">Event type to publish.</typeparam>
    /// <param name="topic">Topic name (e.g., "account.created").</param>
    /// <param name="eventData">Event data to publish.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if published successfully.</returns>
    protected async Task<bool> PublishEventAsync<TEvent>(
        string topic,
        TEvent eventData,
        CancellationToken cancellationToken = default)
        where TEvent : class
    {
        try
        {
            await MessageBus.TryPublishAsync(topic, eventData);
            Logger.LogDebug("Published event to {Topic}: {EventType}", topic, typeof(TEvent).Name);
            return true;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to publish event to {Topic}: {EventType}", topic, typeof(TEvent).Name);
            return false;
        }
    }

    /// <summary>
    /// Creates a new event ID for an event.
    /// </summary>
    /// <returns>New Guid for event identification.</returns>
    protected static Guid NewEventId() => Guid.NewGuid();

    /// <summary>
    /// Gets the current UTC timestamp for an event.
    /// </summary>
    /// <returns>Current UTC time as DateTimeOffset.</returns>
    protected static DateTimeOffset CurrentTimestamp() => DateTimeOffset.UtcNow;
}
