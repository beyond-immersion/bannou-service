namespace BeyondImmersion.BannouService.Events;

/// <summary>
/// Base interface for all Dapr events.
/// Provides common properties that all events should have.
/// </summary>
public interface IDaprEvent
{
    /// <summary>
    /// Unique identifier for this event instance.
    /// </summary>
    string EventId { get; }

    /// <summary>
    /// Timestamp when the event occurred.
    /// </summary>
    DateTime Timestamp { get; }
}
