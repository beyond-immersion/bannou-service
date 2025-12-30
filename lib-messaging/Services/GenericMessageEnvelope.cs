#nullable enable

using BeyondImmersion.BannouService.Configuration;
using BeyondImmersion.BannouService.Events;
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
    public string EventId { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// When the message was created.
    /// </summary>
    [JsonPropertyName("timestamp")]
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// The topic/routing key this message was published to.
    /// </summary>
    [JsonPropertyName("topic")]
    public string Topic { get; set; } = string.Empty;

    /// <summary>
    /// The serialized JSON payload of the message.
    /// Stored as string to avoid MassTransit's System.Object restriction.
    /// </summary>
    [JsonPropertyName("payloadJson")]
    public string PayloadJson { get; set; } = "{}";

    /// <summary>
    /// Content type of the payload (typically "application/json").
    /// </summary>
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

    /// <inheritdoc />
    string IBannouEvent.BannouEventId => EventId;

    /// <inheritdoc />
    DateTimeOffset IBannouEvent.BannouTimestamp => Timestamp;

    Guid IBannouEvent.EventId => throw new NotImplementedException();

    DateTime IBannouEvent.Timestamp => throw new NotImplementedException();
}
