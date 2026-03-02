using System.Text.Json.Serialization;

namespace BeyondImmersion.Bannou.Core;

/// <summary>
/// Base class for all server-to-client push events delivered via WebSocket.
/// All client events MUST inherit from this class and include these fields.
/// The eventName field is used for whitelist validation in Connect service.
/// </summary>
/// <remarks>
/// <para>
/// This class is the single source of truth for base client event properties.
/// All derived events generated via NSwag will inherit from this class.
/// </para>
/// <para>
/// Implements IBannouEvent for uniform event handling in message taps.
/// </para>
/// </remarks>
[System.CodeDom.Compiler.GeneratedCode("NSwag", "14.2.0.0 (NJsonSchema v11.1.0.0 (Newtonsoft.Json v13.0.0.0))")]
public class BaseClientEvent : IBannouEvent
{
    /// <summary>
    /// Unique event type identifier (e.g., "connect.capability-manifest").
    /// Must be in the generated ClientEventWhitelist or will be rejected.
    /// Convention: {service}.{event-name}
    /// </summary>
    [JsonPropertyName("eventName")]
    public virtual string EventName { get; set; } = string.Empty;

    /// <summary>
    /// Unique identifier for this event instance.
    /// Used for deduplication, tracing, and correlation.
    /// </summary>
    [JsonPropertyName("eventId")]
    public Guid EventId { get; set; } = Guid.NewGuid();

    /// <summary>
    /// When the event was generated (ISO 8601 format, UTC).
    /// </summary>
    [JsonPropertyName("timestamp")]
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
}
