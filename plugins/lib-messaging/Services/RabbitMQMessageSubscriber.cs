#nullable enable

using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;

namespace BeyondImmersion.BannouService.Messaging.Services;

/// <summary>
/// Direct RabbitMQ implementation of IMessageSubscriber.
/// Creates subscriptions without MassTransit envelope overhead.
/// </summary>
/// <remarks>
/// <para>
/// This implementation receives plain JSON messages directly from RabbitMQ,
/// avoiding MassTransit's type-based routing and envelope format.
/// </para>
/// <para>
/// Messages are deserialized using BannouJson for consistency across the codebase.
/// </para>
/// </remarks>
public sealed class RabbitMQMessageSubscriber : IMessageSubscriber, IAsyncDisposable
{
    private readonly IChannelManager _channelManager;
    private readonly ILogger<RabbitMQMessageSubscriber> _logger;
    private readonly MessagingServiceConfiguration _configuration;
    private readonly ITelemetryProvider _telemetryProvider;
    private readonly IMessageBus _messageBus;

    // Track static subscriptions by topic
    private readonly ConcurrentDictionary<string, StaticSubscription> _staticSubscriptions = new();

    // Track dynamic subscriptions by ID
    private readonly ConcurrentDictionary<Guid, DynamicSubscription> _dynamicSubscriptions = new();

    /// <summary>
    /// Creates a new RabbitMQMessageSubscriber instance.
    /// </summary>
    /// <param name="channelManager">Channel manager for RabbitMQ operations.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="configuration">Messaging service configuration.</param>
    /// <param name="telemetryProvider">Telemetry provider for instrumentation (NullTelemetryProvider when telemetry disabled).</param>
    /// <param name="messageBus">Message bus for publishing error events.</param>
    public RabbitMQMessageSubscriber(
        IChannelManager channelManager,
        ILogger<RabbitMQMessageSubscriber> logger,
        MessagingServiceConfiguration configuration,
        ITelemetryProvider telemetryProvider,
        IMessageBus messageBus)
    {
        _channelManager = channelManager;
        _logger = logger;
        _configuration = configuration;
        _telemetryProvider = telemetryProvider;
        _messageBus = messageBus;

        if (_telemetryProvider.TracingEnabled || _telemetryProvider.MetricsEnabled)
        {
            _logger.LogDebug(
                "RabbitMQMessageSubscriber created with telemetry instrumentation: tracing={TracingEnabled}, metrics={MetricsEnabled}",
                _telemetryProvider.TracingEnabled, _telemetryProvider.MetricsEnabled);
        }
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

        // Check if already subscribed
        if (_staticSubscriptions.ContainsKey(topic))
        {
            _logger.LogWarning("Already subscribed to topic '{Topic}', skipping duplicate subscription", topic);
            return;
        }

        var effectiveOptions = options ?? new SubscriptionOptions();
        var queueName = BuildQueueName(topic, effectiveOptions);
        var effectiveExchange = exchange ?? _channelManager.DefaultExchange;

        try
        {
            // Create a dedicated channel for this subscription
            var channel = await _channelManager.CreateConsumerChannelAsync(cancellationToken);

            // Declare the exchange (topic for service events - routes by routing key pattern)
            await channel.ExchangeDeclareAsync(
                exchange: effectiveExchange,
                type: ExchangeType.Topic,
                durable: true,
                autoDelete: false,
                arguments: null,
                cancellationToken: cancellationToken);

            // Declare the queue
            await channel.QueueDeclareAsync(
                queue: queueName,
                durable: effectiveOptions.Durable,
                exclusive: effectiveOptions.Exclusive,
                autoDelete: !effectiveOptions.Durable,
                arguments: effectiveOptions.UseDeadLetter ? CreateDeadLetterArguments() : null,
                cancellationToken: cancellationToken);

            // Bind queue to exchange - routing key determines which messages this queue receives
            await channel.QueueBindAsync(
                queue: queueName,
                exchange: effectiveExchange,
                routingKey: topic,
                arguments: null,
                cancellationToken: cancellationToken);

            // Create consumer
            var consumer = new AsyncEventingBasicConsumer(channel);
            consumer.ReceivedAsync += async (sender, ea) =>
            {
                // Extract W3C trace context from message headers for distributed tracing
                var parentContext = ExtractTraceContext(ea.BasicProperties.Headers);

                // Start telemetry activity for this consume operation
                using var activity = _telemetryProvider?.StartActivity(
                    TelemetryComponents.Messaging,
                    "messaging.consume",
                    ActivityKind.Consumer,
                    parentContext);

                var sw = Stopwatch.StartNew();

                // Set activity tags for tracing
                activity?.SetTag("messaging.system", "rabbitmq");
                activity?.SetTag("messaging.destination", topic);
                activity?.SetTag("messaging.operation", "receive");
                if (ea.BasicProperties.MessageId != null)
                {
                    activity?.SetTag("messaging.message_id", ea.BasicProperties.MessageId);
                }

                // Delegate to extracted method for testability
                var result = await HandleTypedDeliveryAsync(
                    ea.Body,
                    topic,
                    handler,
                    activity,
                    subscriptionId: null,
                    cancellationToken);

                // Handle ack/nack based on result
                switch (result)
                {
                    case MessageDeliveryResult.Success:
                        await channel.BasicAckAsync(ea.DeliveryTag, multiple: false);
                        break;
                    case MessageDeliveryResult.DeserializationFailed:
                        await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false);
                        break;
                    case MessageDeliveryResult.HandlerError:
                        // Nack with requeue for retry (unless it's been redelivered too many times)
                        var requeue = !ea.Redelivered;
                        await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: requeue);
                        break;
                }

                sw.Stop();
                RecordConsumeMetrics(topic, result == MessageDeliveryResult.Success, sw.Elapsed.TotalSeconds);
            };

            // Start consuming - use config fallback when AutoAck not explicitly specified
            var consumerTag = await channel.BasicConsumeAsync(
                queue: queueName,
                autoAck: effectiveOptions.AutoAck ?? _configuration.DefaultAutoAck,
                consumer: consumer,
                cancellationToken: cancellationToken);

            var subscription = new StaticSubscription(topic, queueName, channel, consumerTag);
            _staticSubscriptions[topic] = subscription;

            _logger.LogInformation(
                "Created static subscription to topic '{Topic}' (queue: {QueueName})",
                topic,
                queueName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create subscription for topic '{Topic}'", topic);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<IAsyncDisposable> SubscribeDynamicAsync<TEvent>(
        string topic,
        Func<TEvent, CancellationToken, Task> handler,
        string? exchange = null,
        SubscriptionExchangeType exchangeType = SubscriptionExchangeType.Topic,
        CancellationToken cancellationToken = default)
        where TEvent : class
    {

        var subscriptionId = Guid.NewGuid();
        var queueName = $"{topic}.dynamic.{subscriptionId:N}";
        var effectiveExchange = exchange ?? _channelManager.DefaultExchange;

        // Map enum to RabbitMQ exchange type
        var rabbitExchangeType = exchangeType switch
        {
            SubscriptionExchangeType.Direct => ExchangeType.Direct,
            SubscriptionExchangeType.Fanout => ExchangeType.Fanout,
            _ => ExchangeType.Topic
        };

        try
        {
            // Create a dedicated channel for this subscription
            var channel = await _channelManager.CreateConsumerChannelAsync(cancellationToken);

            // Declare the exchange with the specified type
            await channel.ExchangeDeclareAsync(
                exchange: effectiveExchange,
                type: rabbitExchangeType,
                durable: true,
                autoDelete: false,
                arguments: null,
                cancellationToken: cancellationToken);

            // Declare a temporary queue for this dynamic subscription
            await channel.QueueDeclareAsync(
                queue: queueName,
                durable: false,          // Dynamic subscriptions are transient
                exclusive: false,        // Allow other connections to see it for debugging
                autoDelete: true,        // Clean up when consumer disconnects
                arguments: null,
                cancellationToken: cancellationToken);

            // Bind queue to exchange
            await channel.QueueBindAsync(
                queue: queueName,
                exchange: effectiveExchange,
                routingKey: topic,
                arguments: null,
                cancellationToken: cancellationToken);

            // Create consumer
            var consumer = new AsyncEventingBasicConsumer(channel);
            var subId = subscriptionId; // Capture for closure
            consumer.ReceivedAsync += async (sender, ea) =>
            {
                // Extract W3C trace context from message headers for distributed tracing
                var parentContext = ExtractTraceContext(ea.BasicProperties.Headers);

                // Start telemetry activity for this consume operation
                using var activity = _telemetryProvider?.StartActivity(
                    TelemetryComponents.Messaging,
                    "messaging.consume",
                    ActivityKind.Consumer,
                    parentContext);

                var sw = Stopwatch.StartNew();

                // Set activity tags for tracing
                activity?.SetTag("messaging.system", "rabbitmq");
                activity?.SetTag("messaging.destination", topic);
                activity?.SetTag("messaging.operation", "receive");
                activity?.SetTag("messaging.subscription.id", subId.ToString());
                if (ea.BasicProperties.MessageId != null)
                {
                    activity?.SetTag("messaging.message_id", ea.BasicProperties.MessageId);
                }

                // Delegate to extracted method for testability
                var result = await HandleTypedDeliveryAsync(
                    ea.Body,
                    topic,
                    handler,
                    activity,
                    subId,
                    cancellationToken);

                // Handle ack/nack based on result - dynamic subscriptions don't requeue
                switch (result)
                {
                    case MessageDeliveryResult.Success:
                        await channel.BasicAckAsync(ea.DeliveryTag, multiple: false);
                        break;
                    case MessageDeliveryResult.DeserializationFailed:
                    case MessageDeliveryResult.HandlerError:
                        await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false);
                        break;
                }

                sw.Stop();
                RecordConsumeMetrics(topic, result == MessageDeliveryResult.Success, sw.Elapsed.TotalSeconds);
            };

            // Start consuming
            var consumerTag = await channel.BasicConsumeAsync(
                queue: queueName,
                autoAck: false,
                consumer: consumer,
                cancellationToken: cancellationToken);

            var subscription = new DynamicSubscription(subscriptionId, topic, queueName, channel, consumerTag, this);
            _dynamicSubscriptions[subscriptionId] = subscription;

            _logger.LogDebug(
                "Created dynamic subscription {SubscriptionId} to topic '{Topic}'",
                subscriptionId,
                topic);

            return subscription;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to create dynamic subscription for topic '{Topic}'",
                topic);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<IAsyncDisposable> SubscribeDynamicRawAsync(
        string topic,
        Func<byte[], CancellationToken, Task> handler,
        string? exchange = null,
        SubscriptionExchangeType exchangeType = SubscriptionExchangeType.Topic,
        string? queueName = null,
        TimeSpan? queueTtl = null,
        CancellationToken cancellationToken = default)
    {

        var subscriptionId = Guid.NewGuid();
        // Use provided queue name for deterministic naming (reconnection support), or generate one
        var effectiveQueueName = queueName ?? $"{topic}.dynamic.{subscriptionId:N}";
        var effectiveExchange = exchange ?? _channelManager.DefaultExchange;

        // Map enum to RabbitMQ exchange type
        var rabbitExchangeType = exchangeType switch
        {
            SubscriptionExchangeType.Direct => ExchangeType.Direct,
            SubscriptionExchangeType.Fanout => ExchangeType.Fanout,
            _ => ExchangeType.Topic
        };

        // When TTL is specified, create a durable queue that expires after being unused
        // This supports reconnection scenarios where messages should be buffered during disconnect
        var isDurable = queueTtl.HasValue;
        var autoDelete = !queueTtl.HasValue; // Auto-delete only if no TTL (ephemeral)
        Dictionary<string, object?>? queueArguments = null;

        if (queueTtl.HasValue)
        {
            queueArguments = new Dictionary<string, object?>
            {
                // x-expires: queue is deleted after being unused (no consumers) for this many ms
                ["x-expires"] = (int)queueTtl.Value.TotalMilliseconds
            };
            _logger.LogDebug(
                "Creating durable queue {QueueName} with TTL {TtlMs}ms for reconnection support",
                effectiveQueueName,
                queueTtl.Value.TotalMilliseconds);
        }

        try
        {
            // Create a dedicated channel for this subscription
            var channel = await _channelManager.CreateConsumerChannelAsync(cancellationToken);

            // Declare the exchange with the specified type
            await channel.ExchangeDeclareAsync(
                exchange: effectiveExchange,
                type: rabbitExchangeType,
                durable: true,
                autoDelete: false,
                arguments: null,
                cancellationToken: cancellationToken);

            // Declare the queue - durable with TTL if specified, otherwise ephemeral
            await channel.QueueDeclareAsync(
                queue: effectiveQueueName,
                durable: isDurable,
                exclusive: false,
                autoDelete: autoDelete,
                arguments: queueArguments,
                cancellationToken: cancellationToken);

            // Bind queue to exchange
            await channel.QueueBindAsync(
                queue: effectiveQueueName,
                exchange: effectiveExchange,
                routingKey: topic,
                arguments: null,
                cancellationToken: cancellationToken);

            // Create consumer that passes raw bytes directly
            var consumer = new AsyncEventingBasicConsumer(channel);
            consumer.ReceivedAsync += async (sender, ea) =>
            {
                // Extract W3C trace context from message headers for distributed tracing
                var parentContext = ExtractTraceContext(ea.BasicProperties.Headers);

                // Start telemetry activity for this consume operation
                using var activity = _telemetryProvider?.StartActivity(
                    TelemetryComponents.Messaging,
                    "messaging.consume.raw",
                    ActivityKind.Consumer,
                    parentContext);

                var sw = Stopwatch.StartNew();
                var success = false;

                // Set activity tags for tracing
                activity?.SetTag("messaging.system", "rabbitmq");
                activity?.SetTag("messaging.destination", topic);
                activity?.SetTag("messaging.operation", "receive");
                activity?.SetTag("messaging.subscription.id", subscriptionId.ToString());
                activity?.SetTag("messaging.message.body_size", ea.Body.Length);
                if (ea.BasicProperties.MessageId != null)
                {
                    activity?.SetTag("messaging.message_id", ea.BasicProperties.MessageId);
                }

                try
                {
                    // Pass raw bytes directly to handler - no deserialization
                    await handler(ea.Body.ToArray(), cancellationToken);

                    // Acknowledge
                    await channel.BasicAckAsync(ea.DeliveryTag, multiple: false);
                    success = true;
                    activity?.SetStatus(ActivityStatusCode.Ok);
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "Handler failed for raw dynamic subscription {SubscriptionId} on topic '{Topic}'",
                        subscriptionId,
                        topic);
                    activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                    await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false);
                }
                finally
                {
                    sw.Stop();
                    RecordConsumeMetrics(topic, success, sw.Elapsed.TotalSeconds);
                }
            };

            // Start consuming
            var consumerTag = await channel.BasicConsumeAsync(
                queue: effectiveQueueName,
                autoAck: false,
                consumer: consumer,
                cancellationToken: cancellationToken);

            var subscription = new DynamicSubscription(subscriptionId, topic, effectiveQueueName, channel, consumerTag, this);
            _dynamicSubscriptions[subscriptionId] = subscription;

            _logger.LogDebug(
                "Created raw dynamic subscription {SubscriptionId} to topic '{Topic}' on exchange '{Exchange}' ({ExchangeType})",
                subscriptionId,
                topic,
                effectiveExchange,
                exchangeType);

            return subscription;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to create raw dynamic subscription for topic '{Topic}'",
                topic);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task UnsubscribeAsync(string topic)
    {

        if (_staticSubscriptions.TryRemove(topic, out var subscription))
        {
            try
            {
                await subscription.Channel.BasicCancelAsync(subscription.ConsumerTag);
                await subscription.Channel.CloseAsync();

                _logger.LogInformation("Unsubscribed from topic '{Topic}'", topic);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to unsubscribe from topic '{Topic}'", topic);
                throw;
            }
        }
        else
        {
            _logger.LogWarning("No static subscription found for topic '{Topic}'", topic);
        }
    }

    /// <summary>
    /// Remove a dynamic subscription by ID.
    /// </summary>
    internal async Task RemoveDynamicSubscriptionAsync(Guid subscriptionId)
    {
        if (_dynamicSubscriptions.TryRemove(subscriptionId, out var subscription))
        {
            try
            {
                await subscription.Channel.BasicCancelAsync(subscription.ConsumerTag);
                await subscription.Channel.CloseAsync();

                _logger.LogDebug(
                    "Removed dynamic subscription {SubscriptionId} for topic '{Topic}'",
                    subscriptionId,
                    subscription.Topic);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to remove dynamic subscription {SubscriptionId}",
                    subscriptionId);
                throw;
            }
        }
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        // Clean up all static subscriptions
        foreach (var (topic, subscription) in _staticSubscriptions)
        {
            try
            {
                await subscription.Channel.BasicCancelAsync(subscription.ConsumerTag);
                await subscription.Channel.CloseAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error stopping subscription for topic '{Topic}'", topic);
            }
        }
        _staticSubscriptions.Clear();

        // Clean up all dynamic subscriptions
        foreach (var (id, subscription) in _dynamicSubscriptions)
        {
            try
            {
                await subscription.Channel.BasicCancelAsync(subscription.ConsumerTag);
                await subscription.Channel.CloseAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error stopping dynamic subscription {SubscriptionId}", id);
            }
        }
        _dynamicSubscriptions.Clear();

        _logger.LogInformation("RabbitMQMessageSubscriber disposed");
    }

    private string BuildQueueName(string topic, SubscriptionOptions options)
    {
        // If consumer group specified, use it for competing consumers
        if (!string.IsNullOrEmpty(options.ConsumerGroup))
        {
            return $"{topic}.{options.ConsumerGroup}";
        }

        // Default: topic-based queue for pub/sub pattern
        return topic;
    }

    private Dictionary<string, object?> CreateDeadLetterArguments()
    {
        return new Dictionary<string, object?>
        {
            ["x-dead-letter-exchange"] = _configuration.DeadLetterExchange,
            ["x-dead-letter-routing-key"] = "dead-letter"
        };
    }

    /// <summary>
    /// Extracts W3C trace context from RabbitMQ message headers.
    /// </summary>
    /// <param name="headers">Message headers.</param>
    /// <returns>ActivityContext if traceparent header present, null otherwise.</returns>
    private static ActivityContext? ExtractTraceContext(IDictionary<string, object?>? headers)
    {
        if (headers == null)
        {
            return null;
        }

        // Look for W3C traceparent header
        if (!headers.TryGetValue("traceparent", out var traceparentObj))
        {
            return null;
        }

        var traceparent = traceparentObj switch
        {
            byte[] bytes => Encoding.UTF8.GetString(bytes),
            string str => str,
            _ => null
        };

        if (string.IsNullOrEmpty(traceparent))
        {
            return null;
        }

        // Extract tracestate if present
        string? tracestate = null;
        if (headers.TryGetValue("tracestate", out var tracestateObj))
        {
            tracestate = tracestateObj switch
            {
                byte[] bytes => Encoding.UTF8.GetString(bytes),
                string str => str,
                _ => null
            };
        }

        // Use ActivityContext.TryParse to parse W3C trace context
        if (ActivityContext.TryParse(traceparent, tracestate, out var context))
        {
            return context;
        }

        return null;
    }

    /// <summary>
    /// Records metrics for a consume operation.
    /// </summary>
    private void RecordConsumeMetrics(string topic, bool success, double durationSeconds)
    {
        if (_telemetryProvider == null)
        {
            return;
        }

        var tags = new[]
        {
            new KeyValuePair<string, object?>("topic", topic),
            new KeyValuePair<string, object?>("success", success)
        };

        _telemetryProvider.RecordCounter(TelemetryComponents.Messaging, TelemetryMetrics.MessagingConsumed, 1, tags);
        _telemetryProvider.RecordHistogram(TelemetryComponents.Messaging, TelemetryMetrics.MessagingConsumeDuration, durationSeconds, tags);
    }

    /// <summary>
    /// Result of a message delivery attempt.
    /// Used for testing to verify correct handling of different scenarios.
    /// </summary>
    internal enum MessageDeliveryResult
    {
        /// <summary>Message was successfully processed.</summary>
        Success,
        /// <summary>Message deserialized to null.</summary>
        DeserializationFailed,
        /// <summary>Handler threw an exception.</summary>
        HandlerError
    }

    /// <summary>
    /// Handles typed message delivery - extracted for testability.
    /// </summary>
    /// <typeparam name="TEvent">The event type to deserialize to.</typeparam>
    /// <param name="body">Raw message body bytes.</param>
    /// <param name="topic">The topic/routing key the message was received on.</param>
    /// <param name="handler">The handler to invoke with the deserialized event.</param>
    /// <param name="activity">Optional telemetry activity for tracing.</param>
    /// <param name="subscriptionId">Optional subscription ID for dynamic subscriptions.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The delivery result indicating success or failure type.</returns>
    /// <remarks>
    /// This method is internal to allow unit testing of message deserialization and handler
    /// invocation without requiring actual RabbitMQ infrastructure.
    /// </remarks>
    internal async Task<MessageDeliveryResult> HandleTypedDeliveryAsync<TEvent>(
        ReadOnlyMemory<byte> body,
        string topic,
        Func<TEvent, CancellationToken, Task> handler,
        Activity? activity = null,
        Guid? subscriptionId = null,
        CancellationToken cancellationToken = default)
        where TEvent : class
    {
        try
        {
            // Deserialize the message
            var json = Encoding.UTF8.GetString(body.Span);
            var eventData = BannouJson.Deserialize<TEvent>(json);

            if (eventData == null)
            {
                // Truncate payload for logging (avoid memory issues with large payloads)
                var truncatedPayload = json.Length > 500 ? json[..500] + "..." : json;

                if (subscriptionId.HasValue)
                {
                    _logger.LogError(
                        "Failed to deserialize message on dynamic subscription {SubscriptionId} topic '{Topic}' to {EventType}. Payload (truncated): {Payload}",
                        subscriptionId.Value,
                        topic,
                        typeof(TEvent).Name,
                        truncatedPayload);
                }
                else
                {
                    _logger.LogError(
                        "Failed to deserialize message on topic '{Topic}' to {EventType}. Payload (truncated): {Payload}",
                        topic,
                        typeof(TEvent).Name,
                        truncatedPayload);
                }

                activity?.SetStatus(ActivityStatusCode.Error, "Deserialization failed");

                // Publish error event for observability
                var details = subscriptionId.HasValue
                    ? (object)new { SubscriptionId = subscriptionId.Value, Topic = topic, EventType = typeof(TEvent).Name, PayloadLength = json.Length }
                    : new { Topic = topic, EventType = typeof(TEvent).Name, PayloadLength = json.Length };

                var message = subscriptionId.HasValue
                    ? $"Failed to deserialize message on dynamic subscription {subscriptionId.Value} topic '{topic}' to {typeof(TEvent).Name}"
                    : $"Failed to deserialize message on topic '{topic}' to {typeof(TEvent).Name}";

                await _messageBus.TryPublishErrorAsync(
                    serviceName: "messaging",
                    operation: "deserialize",
                    errorType: "DeserializationFailed",
                    message: message,
                    details: details,
                    cancellationToken: cancellationToken);

                return MessageDeliveryResult.DeserializationFailed;
            }

            // Call handler
            await handler(eventData, cancellationToken);
            activity?.SetStatus(ActivityStatusCode.Ok);
            return MessageDeliveryResult.Success;
        }
        catch (Exception ex)
        {
            if (subscriptionId.HasValue)
            {
                _logger.LogError(
                    ex,
                    "Handler failed for dynamic subscription {SubscriptionId} on topic '{Topic}'",
                    subscriptionId.Value,
                    topic);
            }
            else
            {
                _logger.LogError(ex, "Handler failed for message on topic '{Topic}'", topic);
            }
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            return MessageDeliveryResult.HandlerError;
        }
    }

    /// <summary>
    /// Handles raw message delivery - extracted for testability.
    /// </summary>
    /// <param name="body">Raw message body bytes.</param>
    /// <param name="topic">The topic/routing key the message was received on.</param>
    /// <param name="handler">The handler to invoke with the raw bytes.</param>
    /// <param name="activity">Optional telemetry activity for tracing.</param>
    /// <param name="subscriptionId">Subscription ID for logging.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The delivery result indicating success or failure type.</returns>
    internal async Task<MessageDeliveryResult> HandleRawDeliveryAsync(
        ReadOnlyMemory<byte> body,
        string topic,
        Func<byte[], CancellationToken, Task> handler,
        Activity? activity = null,
        Guid? subscriptionId = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Pass raw bytes directly to handler - no deserialization
            await handler(body.ToArray(), cancellationToken);
            activity?.SetStatus(ActivityStatusCode.Ok);
            return MessageDeliveryResult.Success;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Handler failed for raw dynamic subscription {SubscriptionId} on topic '{Topic}'",
                subscriptionId,
                topic);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            return MessageDeliveryResult.HandlerError;
        }
    }

    /// <summary>
    /// Represents a static subscription.
    /// </summary>
    private sealed record StaticSubscription(
        string Topic,
        string QueueName,
        IChannel Channel,
        string ConsumerTag);

    /// <summary>
    /// Represents a dynamic subscription that can be disposed.
    /// </summary>
    private sealed class DynamicSubscription : IAsyncDisposable
    {
        private readonly RabbitMQMessageSubscriber _parent;

        public Guid Id { get; }
        public string Topic { get; }
        public string QueueName { get; }
        public IChannel Channel { get; }
        public string ConsumerTag { get; }

        public DynamicSubscription(
            Guid id,
            string topic,
            string queueName,
            IChannel channel,
            string consumerTag,
            RabbitMQMessageSubscriber parent)
        {
            Id = id;
            Topic = topic;
            QueueName = queueName;
            Channel = channel;
            ConsumerTag = consumerTag;
            _parent = parent;
        }

        public async ValueTask DisposeAsync()
        {
            await _parent.RemoveDynamicSubscriptionAsync(Id);
        }
    }
}
