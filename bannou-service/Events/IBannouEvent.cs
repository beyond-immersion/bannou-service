namespace BeyondImmersion.BannouService.Events;

/// <summary>
/// Base interface for all Bannou events (both service-to-service and client events).
/// All events in the system implement this interface, enabling uniform handling
/// in message taps and event processing pipelines.
/// </summary>
/// <remarks>
/// <para>
/// This interface defines the minimal contract for all events:
/// - EventId: Unique identifier for deduplication and tracing
/// - Timestamp: When the event was created
/// </para>
/// </remarks>
public interface IBannouEvent
{
    /// <summary>
    /// Unique identifier for this event instance (as string).
    /// Used for deduplication, tracing, and correlation.
    /// </summary>
    Guid EventId { get; }

    /// <summary>
    /// When the event was created (UTC).
    /// </summary>
    DateTimeOffset Timestamp { get; }

    /// <summary>
    /// The type/name of the event- usually also the topic it's published on.
    /// </summary>
    string EventName { get; }
}
