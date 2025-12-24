#nullable enable

namespace BeyondImmersion.BannouService.Messaging.Services;

/// <summary>
/// Subscribe to RabbitMQ topics without Dapr topic handlers.
/// Integrates with existing IEventConsumer for fan-out.
/// </summary>
/// <remarks>
/// <para>
/// This interface provides both static subscriptions (survive restarts) and dynamic
/// subscriptions (per-session, disposable). Static subscriptions are typically used
/// by service event handlers, while dynamic subscriptions are used by the Connect
/// service for per-client event delivery.
/// </para>
/// <para>
/// JSON deserialization uses BannouJson for consistency with the rest of the codebase.
/// </para>
/// </remarks>
public interface IMessageSubscriber
{
    /// <summary>
    /// Create a static subscription (survives restarts).
    /// </summary>
    /// <typeparam name="TEvent">Event type to receive</typeparam>
    /// <param name="topic">Topic/routing key to subscribe to</param>
    /// <param name="handler">Handler function called for each message</param>
    /// <param name="options">Optional subscription settings</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task that completes when subscription is established</returns>
    Task SubscribeAsync<TEvent>(
        string topic,
        Func<TEvent, CancellationToken, Task> handler,
        SubscriptionOptions? options = null,
        CancellationToken cancellationToken = default)
        where TEvent : class;

    /// <summary>
    /// Create a dynamic subscription (Connect service per-session).
    /// Returns IAsyncDisposable for cleanup.
    /// </summary>
    /// <typeparam name="TEvent">Event type to receive</typeparam>
    /// <param name="topic">Topic/routing key to subscribe to</param>
    /// <param name="handler">Handler function called for each message</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Disposable subscription handle - dispose to unsubscribe</returns>
    Task<IAsyncDisposable> SubscribeDynamicAsync<TEvent>(
        string topic,
        Func<TEvent, CancellationToken, Task> handler,
        CancellationToken cancellationToken = default)
        where TEvent : class;

    /// <summary>
    /// Unsubscribe from a topic (for static subscriptions).
    /// </summary>
    /// <param name="topic">Topic/routing key to unsubscribe from</param>
    /// <returns>Task that completes when unsubscription is complete</returns>
    Task UnsubscribeAsync(string topic);
}

/// <summary>
/// Options for subscribing to RabbitMQ topics.
/// </summary>
public class SubscriptionOptions
{
    /// <summary>
    /// Whether the queue should survive broker restarts. Defaults to true.
    /// </summary>
    public bool Durable { get; set; } = true;

    /// <summary>
    /// Whether only this connection can consume from the queue. Defaults to false.
    /// </summary>
    public bool Exclusive { get; set; } = false;

    /// <summary>
    /// Whether messages should be auto-acknowledged. Defaults to false for reliability.
    /// When false, messages are acknowledged after successful handler completion.
    /// </summary>
    public bool AutoAck { get; set; } = false;

    /// <summary>
    /// Number of messages to prefetch. Controls throughput vs latency trade-off.
    /// </summary>
    public ushort PrefetchCount { get; set; } = 10;

    /// <summary>
    /// Whether to use dead letter exchange for failed messages. Defaults to true.
    /// </summary>
    public bool UseDeadLetter { get; set; } = true;

    /// <summary>
    /// Name of the consumer group for load balancing. Optional.
    /// </summary>
    public string? ConsumerGroup { get; set; }
}
