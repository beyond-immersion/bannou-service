#nullable enable

namespace BeyondImmersion.BannouService.Messaging.Services;

/// <summary>
/// Native RabbitMQ message publishing without Dapr CloudEvents overhead.
/// Replaces DaprClient.PublishEventAsync() with direct RabbitMQ access.
/// </summary>
/// <remarks>
/// <para>
/// This interface provides the abstraction layer over the messaging backend (MassTransit/RabbitMQ).
/// Services should depend on this interface rather than concrete implementations.
/// </para>
/// <para>
/// JSON serialization uses BannouJson for consistency with the rest of the codebase.
/// </para>
/// </remarks>
public interface IMessageBus
{
    /// <summary>
    /// Publish an event to a topic (routing key).
    /// </summary>
    /// <typeparam name="TEvent">Event type (serialized to JSON via BannouJson)</typeparam>
    /// <param name="topic">Topic/routing key (e.g., "session.connected", "account.deleted")</param>
    /// <param name="eventData">Event payload</param>
    /// <param name="options">Optional publish settings</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The message ID assigned to this publication</returns>
    Task<Guid> PublishAsync<TEvent>(
        string topic,
        TEvent eventData,
        PublishOptions? options = null,
        CancellationToken cancellationToken = default)
        where TEvent : class;

    /// <summary>
    /// Publish raw bytes (for binary protocol optimization).
    /// </summary>
    /// <param name="topic">Topic/routing key</param>
    /// <param name="payload">Raw bytes to publish</param>
    /// <param name="contentType">MIME type of the payload (e.g., "application/octet-stream")</param>
    /// <param name="options">Optional publish settings</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The message ID assigned to this publication</returns>
    Task<Guid> PublishRawAsync(
        string topic,
        ReadOnlyMemory<byte> payload,
        string contentType,
        PublishOptions? options = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Options for publishing messages to RabbitMQ.
/// </summary>
public class PublishOptions
{
    /// <summary>
    /// Exchange name for routing. Defaults to "bannou".
    /// </summary>
    public string Exchange { get; set; } = "bannou";

    /// <summary>
    /// Whether the message should be persisted to disk. Defaults to true.
    /// </summary>
    public bool Persistent { get; set; } = true;

    /// <summary>
    /// Message priority (0-9). Higher values have higher priority.
    /// </summary>
    public byte Priority { get; set; } = 0;

    /// <summary>
    /// Correlation ID for request/response patterns.
    /// </summary>
    public Guid? CorrelationId { get; set; }

    /// <summary>
    /// Message expiration time. Messages expire after this duration.
    /// </summary>
    public TimeSpan? Expiration { get; set; }

    /// <summary>
    /// Custom headers to include with the message.
    /// </summary>
    public Dictionary<string, object>? Headers { get; set; }
}
