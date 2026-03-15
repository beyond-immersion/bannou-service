#nullable enable

using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Collections.Immutable;

namespace BeyondImmersion.BannouService.Messaging.Services;

/// <summary>
/// Zero-overhead event delivery for embedded and sidecar deployments.
/// Dispatches directly to IEventConsumer handlers on TryPublishAsync,
/// bypassing the pub/sub subscription layer entirely.
/// Objects are passed by reference — no serialization.
/// This is the event-side analog to embedded mesh direct DI dispatch.
/// Environment variable: MESSAGING_USE_DIRECT_DISPATCH=true
/// </summary>
[BannouHelperService("direct-dispatch-message-bus", typeof(IMessagingService), lifetime: ServiceLifetime.Singleton)]
public sealed class DirectDispatchMessageBus : IMessageBus, IMessageSubscriber
{
    private readonly IEventConsumer _eventConsumer;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DirectDispatchMessageBus> _logger;
    private readonly MessagingServiceConfiguration _configuration;

    // For non-IEventConsumer subscribers (Connect per-session, Messaging HTTP callbacks)
    // IMPLEMENTATION TENETS: ImmutableList for lock-free concurrent read/write via AddOrUpdate
    private readonly ConcurrentDictionary<string, ImmutableList<Func<object, CancellationToken, Task>>>
        _directSubscriptions = new();

    /// <summary>
    /// Creates a new DirectDispatchMessageBus instance.
    /// </summary>
    /// <param name="eventConsumer">Singleton event consumer for handler dispatch.</param>
    /// <param name="serviceProvider">Root service provider for scope creation.</param>
    /// <param name="logger">Logger for this service.</param>
    /// <param name="configuration">Messaging configuration for optional features.</param>
    public DirectDispatchMessageBus(
        IEventConsumer eventConsumer,
        IServiceProvider serviceProvider,
        ILogger<DirectDispatchMessageBus> logger,
        MessagingServiceConfiguration configuration)
    {
        _eventConsumer = eventConsumer;
        _serviceProvider = serviceProvider;
        _logger = logger;
        _configuration = configuration;
        _logger.LogWarning(
            "DirectDispatchMessageBus initialized - events dispatched directly to IEventConsumer (zero transport overhead, single-node only)");
    }

    /// <inheritdoc/>
    public Task<bool> TryPublishAsync<TEvent>(
        string topic,
        TEvent eventData,
        PublishOptions? options = null,
        Guid? messageId = null,
        CancellationToken cancellationToken = default)
        where TEvent : class
    {
        try
        {
            _logger.LogDebug(
                "DirectDispatch publishing to topic '{Topic}': {EventType}",
                topic, typeof(TEvent).Name);

            // Fire-and-forget: scope lifetime managed inside the dispatched task
            // IMPLEMENTATION TENETS: matches InMemoryMessageBus fire-and-forget semantics
            _ = DispatchAsync(topic, eventData, cancellationToken);

            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initiate direct dispatch for topic '{Topic}'", topic);
            return Task.FromResult(false);
        }
    }

    /// <inheritdoc/>
    public Task<bool> TryPublishRawAsync(
        string topic,
        ReadOnlyMemory<byte> payload,
        string contentType,
        PublishOptions? options = null,
        Guid? messageId = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug(
                "DirectDispatch raw publish to topic '{Topic}': {ByteCount} bytes, {ContentType}",
                topic, payload.Length, contentType);

            // Raw publish has no IEventConsumer path — only direct subscribers
            if (_directSubscriptions.TryGetValue(topic, out var handlers) && !handlers.IsEmpty)
            {
                _ = DispatchToDirectSubscribersRawAsync(topic, payload, handlers, cancellationToken);
            }

            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish raw to topic '{Topic}'", topic);
            return Task.FromResult(false);
        }
    }

    /// <inheritdoc/>
    public async Task<bool> TryPublishErrorAsync(
        string serviceName,
        string operation,
        string errorType,
        string message,
        string? dependency = null,
        string? endpoint = null,
        ServiceErrorEventSeverity severity = ServiceErrorEventSeverity.Error,
        object? details = null,
        string? stack = null,
        Guid? correlationId = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogWarning(
            "Error event from {ServiceName}/{Operation}: {ErrorType} - {Message}",
            serviceName, operation, errorType, message);

        await Task.CompletedTask;
        return true;
    }

    /// <inheritdoc/>
    public async Task SubscribeAsync<TEvent>(
        string topic,
        Func<TEvent, CancellationToken, Task> handler,
        string? exchange = null,
        SubscriptionOptions? options = null,
        CancellationToken cancellationToken = default)
        where TEvent : class
    {
        Func<object, CancellationToken, Task> wrappedHandler = async (obj, ct) =>
        {
            if (obj is TEvent typedEvent)
            {
                await handler(typedEvent, ct);
            }
        };

        _directSubscriptions.AddOrUpdate(
            topic,
            _ => ImmutableList.Create(wrappedHandler),
            (_, existing) => existing.Add(wrappedHandler));

        _logger.LogDebug(
            "DirectDispatch static subscription to topic '{Topic}' for {EventType}",
            topic, typeof(TEvent).Name);
        await Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async Task<IAsyncDisposable> SubscribeDynamicAsync<TEvent>(
        string topic,
        Func<TEvent, CancellationToken, Task> handler,
        string? exchange = null,
        ExchangeType exchangeType = ExchangeType.Topic,
        CancellationToken cancellationToken = default)
        where TEvent : class
    {
        Func<object, CancellationToken, Task> wrappedHandler = async (obj, ct) =>
        {
            if (obj is TEvent typedEvent)
            {
                await handler(typedEvent, ct);
            }
        };

        _directSubscriptions.AddOrUpdate(
            topic,
            _ => ImmutableList.Create(wrappedHandler),
            (_, existing) => existing.Add(wrappedHandler));

        _logger.LogDebug(
            "DirectDispatch dynamic subscription to topic '{Topic}' for {EventType}",
            topic, typeof(TEvent).Name);

        await Task.CompletedTask;
        return new DirectSubscription(this, topic, wrappedHandler);
    }

    /// <inheritdoc/>
    public async Task<IAsyncDisposable> SubscribeDynamicRawAsync(
        string topic,
        Func<byte[], CancellationToken, Task> handler,
        string? exchange = null,
        ExchangeType exchangeType = ExchangeType.Topic,
        string? queueName = null,
        TimeSpan? queueTtl = null,
        CancellationToken cancellationToken = default)
    {
        // For raw handlers, serialize the object to JSON bytes (same as InMemoryMessageBus)
        // This path is used by Connect for WebSocket binary protocol forwarding
        Func<object, CancellationToken, Task> wrappedHandler = async (obj, ct) =>
        {
            var json = BannouJson.Serialize(obj);
            var bytes = System.Text.Encoding.UTF8.GetBytes(json);
            await handler(bytes, ct);
        };

        _directSubscriptions.AddOrUpdate(
            topic,
            _ => ImmutableList.Create(wrappedHandler),
            (_, existing) => existing.Add(wrappedHandler));

        _logger.LogDebug(
            "DirectDispatch raw dynamic subscription to topic '{Topic}'", topic);

        await Task.CompletedTask;
        return new DirectSubscription(this, topic, wrappedHandler);
    }

    /// <inheritdoc/>
    public async Task UnsubscribeAsync(string topic)
    {
        _directSubscriptions.TryRemove(topic, out _);

        _logger.LogDebug("DirectDispatch unsubscribed from topic '{Topic}'", topic);
        await Task.CompletedTask;
    }

    private async Task DispatchAsync<TEvent>(string topic, TEvent eventData, CancellationToken ct)
        where TEvent : class
    {
        try
        {
            // Path 1: IEventConsumer handlers (main path — zero overhead)
            // Skip scope creation if SkipUnhandledTopics is enabled and no handlers registered
            if (!_configuration.SkipUnhandledTopics || _eventConsumer.GetHandlerCount(topic) > 0)
            {
                using var scope = _serviceProvider.CreateScope();
                await _eventConsumer.DispatchAsync(topic, eventData, scope.ServiceProvider);
            }

            // Path 2: Direct subscribers (Connect per-session, static subscriptions, etc.)
            if (_directSubscriptions.TryGetValue(topic, out var handlers) && !handlers.IsEmpty)
            {
                await DispatchToDirectSubscribersAsync(topic, eventData, handlers, ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DirectDispatch failed for topic {Topic}", topic);
        }
    }

    private async Task DispatchToDirectSubscribersAsync<TEvent>(
        string topic,
        TEvent eventData,
        ImmutableList<Func<object, CancellationToken, Task>> handlers,
        CancellationToken ct)
        where TEvent : class
    {
        // ImmutableList snapshot is inherently thread-safe
        foreach (var handler in handlers)
        {
            try
            {
                await handler(eventData, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Direct subscriber failed for topic {Topic}", topic);
            }
        }
    }

    private async Task DispatchToDirectSubscribersRawAsync(
        string topic,
        ReadOnlyMemory<byte> payload,
        ImmutableList<Func<object, CancellationToken, Task>> handlers,
        CancellationToken ct)
    {
        // For raw publish, pass the byte array directly to subscribers
        var bytes = payload.ToArray();
        foreach (var handler in handlers)
        {
            try
            {
                await handler(bytes, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Direct raw subscriber failed for topic {Topic}", topic);
            }
        }
    }

    private void RemoveHandler(string topic, Func<object, CancellationToken, Task> handler)
    {
        _directSubscriptions.AddOrUpdate(
            topic,
            _ => ImmutableList<Func<object, CancellationToken, Task>>.Empty,
            (_, existing) => existing.Remove(handler));
    }

    private sealed class DirectSubscription : IAsyncDisposable
    {
        private readonly DirectDispatchMessageBus _bus;
        private readonly string _topic;
        private readonly Func<object, CancellationToken, Task> _handler;

        public DirectSubscription(
            DirectDispatchMessageBus bus,
            string topic,
            Func<object, CancellationToken, Task> handler)
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
