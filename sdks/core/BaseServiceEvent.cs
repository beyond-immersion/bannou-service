using System.Text.Json.Serialization;

namespace BeyondImmersion.Bannou.Core;

/// <summary>
/// Base class for all service-to-service events via RabbitMQ pub/sub.
/// All service events MUST inherit from this class and include these fields.
/// </summary>
/// <remarks>
/// <para>
/// This class is the single source of truth for base service event properties.
/// All derived events generated via NSwag will inherit from this class.
/// </para>
/// <para>
/// Implements IBannouEvent for uniform event handling in message taps.
/// </para>
/// </remarks>
[System.CodeDom.Compiler.GeneratedCode("NSwag", "14.2.0.0 (NJsonSchema v11.1.0.0 (Newtonsoft.Json v13.0.0.0))")]
public class BaseServiceEvent : IBannouEvent
{
    /// <summary>
    /// Unique event type identifier (e.g., "session.connected").
    /// Convention: {service}.{event_name} or {domain}.{event_name}
    /// </summary>
    [JsonPropertyName("eventName")]
    public virtual string EventName { get; set; } = string.Empty;

    /// <summary>
    /// Unique identifier for this event instance (UUID string).
    /// Used for deduplication, tracing, and correlation.
    /// </summary>
    [JsonPropertyName("eventId")]
    public Guid EventId { get; set; } = Guid.NewGuid();

    /// <summary>
    /// When the event was created (ISO 8601 format, UTC).
    /// </summary>
    [JsonPropertyName("timestamp")]
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
}
