namespace BeyondImmersion.BannouService.Events;

/// <summary>
/// Base interface for all Bannou events.
/// Provides common properties that all events should have.
/// </summary>
public interface IBannouEvent
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
