#nullable enable

using BeyondImmersion.Bannou.Core;
using System.Text.Json.Serialization;

namespace BeyondImmersion.BannouService.Messaging.Services;

/// <summary>
/// Extended message envelope that includes tap routing metadata.
/// Used when forwarding events through a tap to allow consumers to distinguish
/// between different tap sources and route accordingly.
/// </summary>
/// <remarks>
/// <para>
/// This envelope extends <see cref="GenericMessageEnvelope"/> with additional fields
/// that describe where the message was tapped from and how it was routed.
/// </para>
/// <para>
/// When a client session taps multiple character event streams, the consumer
/// can use these fields to identify which character the event came from.
/// </para>
/// <para>
/// EXTENSION POINT: Future filtering could be added here by including
/// filter criteria that was applied when creating the tap.
/// </para>
/// </remarks>
public class TappedMessageEnvelope : GenericMessageEnvelope
{
    /// <summary>
    /// Unique identifier for this tap instance.
    /// Multiple taps from different sources will have different TapIds.
    /// </summary>
    [JsonPropertyName("tapId")]
    public Guid TapId { get; set; } = Guid.Empty;

    /// <summary>
    /// The source topic/routing key that was tapped.
    /// This is the original topic the message was published to.
    /// </summary>
    /// <example>character.events.char-abc123</example>
    [JsonPropertyName("sourceTopic")]
    public string SourceTopic { get; set; } = string.Empty;

    /// <summary>
    /// The source exchange the message was tapped from.
    /// </summary>
    /// <example>bannou</example>
    [JsonPropertyName("sourceExchange")]
    public string SourceExchange { get; set; } = string.Empty;

    /// <summary>
    /// The destination exchange the message was forwarded to.
    /// </summary>
    /// <example>bannou-client-events</example>
    [JsonPropertyName("destinationExchange")]
    public string DestinationExchange { get; set; } = string.Empty;

    /// <summary>
    /// The destination routing key used when forwarding the message.
    /// </summary>
    /// <example>CONNECT_SESSION_abc123</example>
    [JsonPropertyName("destinationRoutingKey")]
    public string DestinationRoutingKey { get; set; } = string.Empty;

    /// <summary>
    /// The exchange type used for the destination.
    /// </summary>
    [JsonPropertyName("destinationExchangeType")]
    public string DestinationExchangeType { get; set; } = "fanout";

    /// <summary>
    /// When the tap was created.
    /// </summary>
    [JsonPropertyName("tapCreatedAt")]
    public DateTimeOffset TapCreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// When the message was forwarded through the tap.
    /// </summary>
    [JsonPropertyName("forwardedAt")]
    public DateTimeOffset ForwardedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Creates an empty tapped envelope.
    /// </summary>
    public TappedMessageEnvelope()
    {
    }

    /// <summary>
    /// Creates a tapped envelope from an original event implementing IBannouEvent.
    /// </summary>
    /// <param name="original">The original event being tapped</param>
    /// <param name="tapId">The unique tap identifier</param>
    /// <param name="sourceTopic">The source topic</param>
    /// <param name="sourceExchange">The source exchange</param>
    /// <param name="destinationExchange">The destination exchange</param>
    /// <param name="destinationRoutingKey">The destination routing key</param>
    /// <param name="destinationExchangeType">The destination exchange type</param>
    /// <param name="tapCreatedAt">When the tap was created</param>
    public TappedMessageEnvelope(
        IBannouEvent original,
        Guid tapId,
        string sourceTopic,
        string sourceExchange,
        string destinationExchange,
        string destinationRoutingKey,
        string destinationExchangeType,
        DateTimeOffset tapCreatedAt)
    {
        // Copy base envelope fields from IBannouEvent
        EventId = original.EventId;
        Timestamp = original.Timestamp;
        EventName = original.EventName;
        Topic = sourceTopic;
        PayloadJson = BannouJson.Serialize(original);
        ContentType = "application/json";

        // Set tap metadata
        TapId = tapId;
        SourceTopic = sourceTopic;
        SourceExchange = sourceExchange;
        DestinationExchange = destinationExchange;
        DestinationRoutingKey = destinationRoutingKey;
        DestinationExchangeType = destinationExchangeType;
        TapCreatedAt = tapCreatedAt;
        ForwardedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Creates a tapped envelope from a typed event.
    /// </summary>
    /// <typeparam name="TEvent">The event type</typeparam>
    /// <param name="sourceTopic">The source topic</param>
    /// <param name="eventData">The event data</param>
    /// <param name="tapId">The unique tap identifier</param>
    /// <param name="sourceExchange">The source exchange</param>
    /// <param name="destinationExchange">The destination exchange</param>
    /// <param name="destinationRoutingKey">The destination routing key</param>
    /// <param name="destinationExchangeType">The destination exchange type</param>
    /// <param name="tapCreatedAt">When the tap was created</param>
    /// <returns>A new tapped envelope</returns>
    public static TappedMessageEnvelope FromEvent<TEvent>(
        string sourceTopic,
        TEvent eventData,
        Guid tapId,
        string sourceExchange,
        string destinationExchange,
        string destinationRoutingKey,
        string destinationExchangeType,
        DateTimeOffset tapCreatedAt)
        where TEvent : class
    {
        return new TappedMessageEnvelope
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            EventName = $"tap.{sourceTopic}",
            Topic = sourceTopic,
            PayloadJson = BannouJson.Serialize(eventData),
            ContentType = "application/json",
            TapId = tapId,
            SourceTopic = sourceTopic,
            SourceExchange = sourceExchange,
            DestinationExchange = destinationExchange,
            DestinationRoutingKey = destinationRoutingKey,
            DestinationExchangeType = destinationExchangeType,
            TapCreatedAt = tapCreatedAt,
            ForwardedAt = DateTimeOffset.UtcNow
        };
    }
}
