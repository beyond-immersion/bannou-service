#nullable enable

using BeyondImmersion.BannouService.Messaging;

namespace BeyondImmersion.BannouService.Services;

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
/// <para>
/// Uses PublishOptions and SubscriptionOptions types from Generated/Models/MessagingModels.cs
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
