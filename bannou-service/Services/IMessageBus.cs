#nullable enable

using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Messaging;

namespace BeyondImmersion.BannouService.Services;

/// <summary>
/// Native RabbitMQ message publishing via MassTransit for SERVICE-TO-SERVICE events.
/// Provides direct RabbitMQ access for event-driven communication between Bannou services.
/// </summary>
/// <remarks>
/// <para>
/// <b>WARNING - CLIENT EVENTS:</b> This interface is for service-to-service events ONLY.
/// To push events to WebSocket clients, use <see cref="ClientEvents.IClientEventPublisher"/> instead.
/// Using IMessageBus for client events will silently fail - events go to the wrong exchange.
/// See TENETS.md T17: Client Event Schema Pattern.
/// </para>
/// <para>
/// <b>WARNING - TYPED EVENTS ONLY:</b> The TEvent type parameter must be a typed event class
/// defined in a schema. Anonymous objects (<c>new { ... }</c>) are FORBIDDEN and will throw
/// <c>System.ArgumentException: Message types must not be anonymous types</c> at runtime.
/// See TENETS.md T5: Event-Driven Architecture.
/// </para>
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
/// <para>
/// All publish methods use the Try* pattern - they never throw exceptions. Failed publishes
/// are automatically buffered and retried. If the buffer overflows or messages are stuck
/// too long, the node crashes for restart by the orchestrator.
/// </para>
/// </remarks>
public interface IMessageBus
{
    /// <summary>
    /// Publish an event to a topic (routing key). Never throws.
    /// </summary>
    /// <typeparam name="TEvent">Event type (serialized to JSON via BannouJson)</typeparam>
    /// <param name="topic">Topic/routing key (e.g., "session.connected", "account.deleted")</param>
    /// <param name="eventData">Event payload</param>
    /// <param name="options">Optional publish settings</param>
    /// <param name="messageId">Optional message ID for correlation (generated if not provided)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if published or buffered for retry, false on unrecoverable failure</returns>
    Task<bool> TryPublishAsync<TEvent>(
        string topic,
        TEvent eventData,
        PublishOptions? options = null,
        Guid? messageId = null,
        CancellationToken cancellationToken = default)
        where TEvent : class;

    /// <summary>
    /// Publish an event to a topic (routing key). Never throws.
    /// Convenience overload without messageId parameter.
    /// </summary>
    Task<bool> TryPublishAsync<TEvent>(
        string topic,
        TEvent eventData,
        CancellationToken cancellationToken)
        where TEvent : class
        => TryPublishAsync(topic, eventData, null, cancellationToken: cancellationToken);

    /// <summary>
    /// Publish raw bytes (for binary protocol optimization). Never throws.
    /// </summary>
    /// <param name="topic">Topic/routing key</param>
    /// <param name="payload">Raw bytes to publish</param>
    /// <param name="contentType">MIME type of the payload (e.g., "application/octet-stream")</param>
    /// <param name="options">Optional publish settings</param>
    /// <param name="messageId">Optional message ID for correlation (generated if not provided)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if published or buffered for retry, false on unrecoverable failure</returns>
    Task<bool> TryPublishRawAsync(
        string topic,
        ReadOnlyMemory<byte> payload,
        string contentType,
        PublishOptions? options = null,
        Guid? messageId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Publish raw bytes (for binary protocol optimization). Never throws.
    /// Convenience overload without messageId parameter.
    /// </summary>
    Task<bool> TryPublishRawAsync(
        string topic,
        ReadOnlyMemory<byte> payload,
        string contentType,
        CancellationToken cancellationToken)
        => TryPublishRawAsync(topic, payload, contentType, null, null, cancellationToken);

    /// <summary>
    /// Publish a service error event. Convenience method replacing IErrorEventEmitter.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This method wraps error publishing with try/catch to prevent cascading failures
    /// when the messaging infrastructure itself is the source of errors.
    /// </para>
    /// <para>
    /// Use for unexpected/internal failures only (not user/input validation errors).
    /// Similar to Sentry/error tracking services.
    /// </para>
    /// </remarks>
    /// <param name="serviceName">Service name (e.g., "accounts", "auth")</param>
    /// <param name="operation">Operation that failed (e.g., "GetAccount", "ValidateToken")</param>
    /// <param name="errorType">Exception type name</param>
    /// <param name="message">Error message</param>
    /// <param name="dependency">External dependency that failed (optional)</param>
    /// <param name="endpoint">Endpoint being called (optional)</param>
    /// <param name="severity">Error severity level</param>
    /// <param name="details">Additional context (will be serialized)</param>
    /// <param name="stack">Stack trace (optional)</param>
    /// <param name="correlationId">Correlation ID for tracing (optional)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if the error event was published successfully, false otherwise</returns>
    Task<bool> TryPublishErrorAsync(
        string serviceName,
        string operation,
        string errorType,
        string message,
        string? dependency = null,
        string? endpoint = null,
        ServiceErrorEventSeverity severity = ServiceErrorEventSeverity.Error,
        object? details = null,
        string? stack = null,
        string? correlationId = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Subscribe to RabbitMQ topics via MassTransit.
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
    /// <param name="exchange">Exchange to bind to (defaults to service default exchange)</param>
    /// <param name="options">Optional subscription settings (queue durability, ack mode, etc.)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task that completes when subscription is established</returns>
    Task SubscribeAsync<TEvent>(
        string topic,
        Func<TEvent, CancellationToken, Task> handler,
        string? exchange = null,
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
    /// <param name="exchange">Exchange to bind to (defaults to service default exchange)</param>
    /// <param name="exchangeType">Exchange type (defaults to Topic for routing-key-based service events)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Disposable subscription handle - dispose to unsubscribe</returns>
    Task<IAsyncDisposable> SubscribeDynamicAsync<TEvent>(
        string topic,
        Func<TEvent, CancellationToken, Task> handler,
        string? exchange = null,
        SubscriptionExchangeType exchangeType = SubscriptionExchangeType.Topic,
        CancellationToken cancellationToken = default)
        where TEvent : class;

    /// <summary>
    /// Create a dynamic subscription for raw bytes (no deserialization).
    /// Optimized for forwarding scenarios where payload is passed through unchanged.
    /// </summary>
    /// <param name="topic">Topic/routing key to subscribe to</param>
    /// <param name="handler">Handler function called with raw message bytes</param>
    /// <param name="exchange">Exchange to bind to (defaults to service default exchange)</param>
    /// <param name="exchangeType">Exchange type (defaults to Topic for routing-key-based service events)</param>
    /// <param name="queueName">Optional deterministic queue name (for reconnection scenarios)</param>
    /// <param name="queueTtl">Optional queue TTL - when set, creates a durable queue that expires after this duration of being unused</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Disposable subscription handle - dispose to unsubscribe</returns>
    Task<IAsyncDisposable> SubscribeDynamicRawAsync(
        string topic,
        Func<byte[], CancellationToken, Task> handler,
        string? exchange = null,
        SubscriptionExchangeType exchangeType = SubscriptionExchangeType.Topic,
        string? queueName = null,
        TimeSpan? queueTtl = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Unsubscribe from a topic (for static subscriptions).
    /// </summary>
    /// <param name="topic">Topic/routing key to unsubscribe from</param>
    /// <returns>Task that completes when unsubscription is complete</returns>
    Task UnsubscribeAsync(string topic);
}

/// <summary>
/// Exchange types for dynamic subscriptions.
/// </summary>
public enum SubscriptionExchangeType
{
    /// <summary>
    /// Fanout exchange - broadcasts to all bound queues (use for broadcast scenarios).
    /// </summary>
    Fanout,

    /// <summary>
    /// Direct exchange - routes by exact routing key match (used for client events).
    /// </summary>
    Direct,

    /// <summary>
    /// Topic exchange - routes by pattern matching on routing key (default for service events).
    /// </summary>
    Topic
}
