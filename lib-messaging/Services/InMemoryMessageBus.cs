#nullable enable

using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace BeyondImmersion.BannouService.Messaging.Services;

/// <summary>
/// In-memory message bus for testing and minimal infrastructure scenarios.
/// Messages are NOT actually published to any broker - just logged for debugging.
/// Provides local event fan-out within the same process.
/// </summary>
public sealed class InMemoryMessageBus : IMessageBus, IMessageSubscriber
{
    private readonly ILogger<InMemoryMessageBus> _logger;

    // Local subscriptions for in-process delivery
    private readonly ConcurrentDictionary<string, List<Func<object, CancellationToken, Task>>> _subscriptions = new();
    private readonly object _subscriptionLock = new();

    public InMemoryMessageBus(ILogger<InMemoryMessageBus> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _logger.LogWarning("InMemoryMessageBus initialized - messages will NOT be persisted or delivered across processes");
    }

    /// <inheritdoc/>
    public Task<Guid> PublishAsync<TEvent>(
        string topic,
        TEvent eventData,
        PublishOptions? options = null,
        CancellationToken cancellationToken = default)
        where TEvent : class
    {
        ArgumentNullException.ThrowIfNull(topic);
        ArgumentNullException.ThrowIfNull(eventData);

        var messageId = Guid.NewGuid();
        _logger.LogDebug(
            "[InMemory] Published to topic '{Topic}': {EventType} (id: {MessageId})",
            topic, typeof(TEvent).Name, messageId);

        // Deliver to local subscribers
        _ = DeliverToSubscribersAsync(topic, eventData, cancellationToken);

        return Task.FromResult(messageId);
    }

    /// <inheritdoc/>
    public Task<Guid> PublishRawAsync(
        string topic,
        ReadOnlyMemory<byte> payload,
        string contentType,
        PublishOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(topic);

        var messageId = Guid.NewGuid();
        _logger.LogDebug(
            "[InMemory] Published raw to topic '{Topic}': {ByteCount} bytes, {ContentType} (id: {MessageId})",
            topic, payload.Length, contentType, messageId);

        return Task.FromResult(messageId);
    }

    /// <inheritdoc/>
    public Task<bool> TryPublishErrorAsync(
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
        CancellationToken cancellationToken = default)
    {
        _logger.LogWarning(
            "[InMemory] Error event from {ServiceName}/{Operation}: {ErrorType} - {Message}",
            serviceName, operation, errorType, message);

        return Task.FromResult(true);
    }

    /// <inheritdoc/>
    public Task SubscribeAsync<TEvent>(
        string topic,
        Func<TEvent, CancellationToken, Task> handler,
        SubscriptionOptions? options = null,
        CancellationToken cancellationToken = default)
        where TEvent : class
    {
        ArgumentNullException.ThrowIfNull(topic);
        ArgumentNullException.ThrowIfNull(handler);

        lock (_subscriptionLock)
        {
            var handlers = _subscriptions.GetOrAdd(topic, _ => new List<Func<object, CancellationToken, Task>>());
            handlers.Add(async (obj, ct) =>
            {
                if (obj is TEvent typedEvent)
                {
                    await handler(typedEvent, ct);
                }
            });
        }

        _logger.LogDebug("Subscribed to topic '{Topic}' for {EventType} (in-memory mode)", topic, typeof(TEvent).Name);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<IAsyncDisposable> SubscribeDynamicAsync<TEvent>(
        string topic,
        Func<TEvent, CancellationToken, Task> handler,
        CancellationToken cancellationToken = default)
        where TEvent : class
    {
        ArgumentNullException.ThrowIfNull(topic);
        ArgumentNullException.ThrowIfNull(handler);

        Func<object, CancellationToken, Task> wrappedHandler = async (obj, ct) =>
        {
            if (obj is TEvent typedEvent)
            {
                await handler(typedEvent, ct);
            }
        };

        lock (_subscriptionLock)
        {
            var handlers = _subscriptions.GetOrAdd(topic, _ => new List<Func<object, CancellationToken, Task>>());
            handlers.Add(wrappedHandler);
        }

        _logger.LogDebug("Dynamic subscription to topic '{Topic}' for {EventType} (in-memory mode)", topic, typeof(TEvent).Name);

        return Task.FromResult<IAsyncDisposable>(new DynamicSubscription(this, topic, wrappedHandler));
    }

    /// <inheritdoc/>
    public Task UnsubscribeAsync(string topic)
    {
        ArgumentNullException.ThrowIfNull(topic);

        lock (_subscriptionLock)
        {
            _subscriptions.TryRemove(topic, out _);
        }

        _logger.LogDebug("Unsubscribed from topic '{Topic}' (in-memory mode)", topic);
        return Task.CompletedTask;
    }

    private async Task DeliverToSubscribersAsync<TEvent>(string topic, TEvent eventData, CancellationToken cancellationToken)
        where TEvent : class
    {
        List<Func<object, CancellationToken, Task>>? handlers;
        lock (_subscriptionLock)
        {
            if (!_subscriptions.TryGetValue(topic, out handlers) || handlers.Count == 0)
            {
                return;
            }
            // Copy to avoid holding lock during async delivery
            handlers = handlers.ToList();
        }

        foreach (var handler in handlers)
        {
            try
            {
                await handler(eventData, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error delivering event to subscriber for topic '{Topic}' (in-memory mode)", topic);
            }
        }
    }

    private void RemoveHandler(string topic, Func<object, CancellationToken, Task> handler)
    {
        lock (_subscriptionLock)
        {
            if (_subscriptions.TryGetValue(topic, out var handlers))
            {
                handlers.Remove(handler);
            }
        }
    }

    private sealed class DynamicSubscription : IAsyncDisposable
    {
        private readonly InMemoryMessageBus _bus;
        private readonly string _topic;
        private readonly Func<object, CancellationToken, Task> _handler;

        public DynamicSubscription(InMemoryMessageBus bus, string topic, Func<object, CancellationToken, Task> handler)
        {
            _bus = bus;
            _topic = topic;
            _handler = handler;
        }

        public ValueTask DisposeAsync()
        {
            _bus.RemoveHandler(_topic, _handler);
            return ValueTask.CompletedTask;
        }
    }
}
