#nullable enable

using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Collections.Concurrent;
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
    private readonly RabbitMQConnectionManager _connectionManager;
    private readonly ILogger<RabbitMQMessageSubscriber> _logger;
    private readonly MessagingServiceConfiguration _configuration;

    // Track static subscriptions by topic
    private readonly ConcurrentDictionary<string, StaticSubscription> _staticSubscriptions = new();

    // Track dynamic subscriptions by ID
    private readonly ConcurrentDictionary<Guid, DynamicSubscription> _dynamicSubscriptions = new();

    /// <summary>
    /// Creates a new RabbitMQMessageSubscriber instance.
    /// </summary>
    public RabbitMQMessageSubscriber(
        RabbitMQConnectionManager connectionManager,
        ILogger<RabbitMQMessageSubscriber> logger,
        MessagingServiceConfiguration configuration)
    {
        _connectionManager = connectionManager;
        _logger = logger;
        _configuration = configuration;
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
        var effectiveExchange = exchange ?? _connectionManager.DefaultExchange;

        try
        {
            // Create a dedicated channel for this subscription
            var channel = await _connectionManager.CreateConsumerChannelAsync(cancellationToken);

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
                try
                {
                    // Deserialize the message
                    var json = Encoding.UTF8.GetString(ea.Body.Span);
                    var eventData = BannouJson.Deserialize<TEvent>(json);

                    if (eventData == null)
                    {
                        _logger.LogWarning(
                            "Failed to deserialize message on topic '{Topic}' to {EventType}",
                            topic,
                            typeof(TEvent).Name);
                        await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false);
                        return;
                    }

                    // Call handler
                    await handler(eventData, cancellationToken);

                    // Acknowledge
                    await channel.BasicAckAsync(ea.DeliveryTag, multiple: false);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Handler failed for message on topic '{Topic}'", topic);
                    // Nack with requeue for retry (unless it's been redelivered too many times)
                    var requeue = !ea.Redelivered;
                    await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: requeue);
                }
            };

            // Start consuming
            var consumerTag = await channel.BasicConsumeAsync(
                queue: queueName,
                autoAck: effectiveOptions.AutoAck,
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
        var effectiveExchange = exchange ?? _connectionManager.DefaultExchange;

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
            var channel = await _connectionManager.CreateConsumerChannelAsync(cancellationToken);

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
            consumer.ReceivedAsync += async (sender, ea) =>
            {
                try
                {
                    // Deserialize the message
                    var json = Encoding.UTF8.GetString(ea.Body.Span);
                    var eventData = BannouJson.Deserialize<TEvent>(json);

                    if (eventData == null)
                    {
                        _logger.LogWarning(
                            "Failed to deserialize message on dynamic subscription {SubscriptionId} topic '{Topic}'",
                            subscriptionId,
                            topic);
                        await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false);
                        return;
                    }

                    // Call handler
                    await handler(eventData, cancellationToken);

                    // Acknowledge
                    await channel.BasicAckAsync(ea.DeliveryTag, multiple: false);
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "Handler failed for dynamic subscription {SubscriptionId} on topic '{Topic}'",
                        subscriptionId,
                        topic);
                    await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false);
                }
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
        var effectiveExchange = exchange ?? _connectionManager.DefaultExchange;

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
            var channel = await _connectionManager.CreateConsumerChannelAsync(cancellationToken);

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
                try
                {
                    // Pass raw bytes directly to handler - no deserialization
                    await handler(ea.Body.ToArray(), cancellationToken);

                    // Acknowledge
                    await channel.BasicAckAsync(ea.DeliveryTag, multiple: false);
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "Handler failed for raw dynamic subscription {SubscriptionId} on topic '{Topic}'",
                        subscriptionId,
                        topic);
                    await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false);
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

    private static Dictionary<string, object?> CreateDeadLetterArguments()
    {
        return new Dictionary<string, object?>
        {
            ["x-dead-letter-exchange"] = "bannou-dlx",
            ["x-dead-letter-routing-key"] = "dead-letter"
        };
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
