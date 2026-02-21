#nullable enable

using BeyondImmersion.Bannou.Core;
using System.Text.Json.Serialization;

namespace BeyondImmersion.BannouService.Messaging.Services;

/// <summary>
/// Generic message envelope for arbitrary JSON payloads sent via the Messaging HTTP API.
/// MassTransit requires concrete types (not System.Object), so we wrap dynamic payloads
/// in this envelope which includes the standard event fields.
/// </summary>
/// <remarks>
/// This class implements the common Bannou event pattern with eventId and timestamp,
/// plus a serialized payload field for the actual message content.
/// Implements <see cref="IBannouEvent"/> to enable unified event handling across the system.
/// </remarks>
public class GenericMessageEnvelope : IBannouEvent
{
    /// <summary>
    /// Unique identifier for this message.
    /// </summary>
    [JsonPropertyName("eventId")]
    public Guid EventId { get; set; } = Guid.NewGuid();

    /// <summary>
    /// When the message was created.
    /// </summary>
    [JsonPropertyName("timestamp")]
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// The event type identifier. Uses the topic as the event name.
    /// </summary>
    [JsonPropertyName("eventName")]
    public string EventName { get; set; } = "messaging.generic";

    /// <summary>
    /// The topic/routing key this message was published to.
    /// </summary>
    /// <remarks>
    /// Default empty string is for deserialization only - callers should always set this
    /// via the constructor. An empty topic is not a sentinel for "absent" - it's invalid.
    /// </remarks>
    [JsonPropertyName("topic")]
    public string Topic { get; set; } = string.Empty;

    /// <summary>
    /// The serialized JSON payload of the message.
    /// Stored as string to avoid MassTransit's System.Object restriction.
    /// </summary>
    /// <remarks>
    /// Default "{}" represents an empty JSON object, which is a valid payload.
    /// This is NOT a sentinel - empty object is a legitimate value.
    /// </remarks>
    [JsonPropertyName("payloadJson")]
    public string PayloadJson { get; set; } = "{}";

    /// <summary>
    /// Content type of the payload (typically "application/json").
    /// </summary>
    /// <remarks>
    /// Default "application/json" is the most common content type.
    /// This is a valid default, NOT a sentinel for "absent".
    /// </remarks>
    [JsonPropertyName("contentType")]
    public string ContentType { get; set; } = "application/json";

    /// <summary>
    /// Creates an empty envelope.
    /// </summary>
    public GenericMessageEnvelope()
    {
    }

    /// <summary>
    /// Creates an envelope with the specified payload.
    /// </summary>
    /// <param name="topic">The topic the message is being published to</param>
    /// <param name="payload">The payload object to serialize</param>
    public GenericMessageEnvelope(string topic, object? payload)
    {
        Topic = topic;
        EventName = $"messaging.{topic}";
        PayloadJson = payload != null ? BannouJson.Serialize(payload) : "{}";
    }

    /// <summary>
    /// Deserializes the payload to the specified type.
    /// </summary>
    /// <typeparam name="T">Target type</typeparam>
    /// <returns>Deserialized payload or default</returns>
    public T? GetPayload<T>() where T : class
    {
        if (string.IsNullOrEmpty(PayloadJson) || PayloadJson == "{}")
            return default;

        return BannouJson.Deserialize<T>(PayloadJson);
    }

    /// <summary>
    /// Gets the payload as a dynamic object for untyped access.
    /// </summary>
    /// <returns>Deserialized payload as object</returns>
    public object? GetPayloadAsObject()
    {
        if (string.IsNullOrEmpty(PayloadJson) || PayloadJson == "{}")
            return null;

        return BannouJson.Deserialize<object>(PayloadJson);
    }

}
