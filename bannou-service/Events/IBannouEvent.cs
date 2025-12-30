namespace BeyondImmersion.BannouService.Events;

/// <summary>
/// Base interface for all Bannou events (both service-to-service and client events).
/// All events in the system implement this interface, enabling uniform handling
/// in message taps and event processing pipelines.
/// </summary>
/// <remarks>
/// <para>
/// This interface defines the minimal contract for all events:
/// - BannouEventId: Unique identifier for deduplication and tracing
/// - BannouTimestamp: When the event was created
/// </para>
/// <para>
/// Property names are prefixed with "Bannou" to avoid conflicts with generated
/// properties that have different types (e.g., Event_id as Guid vs string).
/// Implementing classes use explicit interface implementation to bridge the gap.
/// </para>
/// </remarks>
public interface IBannouEvent
{
    /// <summary>
    /// Unique identifier for this event instance (as string).
    /// Used for deduplication, tracing, and correlation.
    /// </summary>
    string BannouEventId { get; }

    /// <summary>
    /// When the event was created (UTC).
    /// </summary>
    DateTimeOffset BannouTimestamp { get; }
}
