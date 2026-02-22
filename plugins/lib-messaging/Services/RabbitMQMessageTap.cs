#nullable enable

using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RmqExchangeType = RabbitMQ.Client.ExchangeType;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace BeyondImmersion.BannouService.Messaging.Services;

/// <summary>
/// Direct RabbitMQ implementation of IMessageTap.
/// Creates taps that subscribe to source topics and forward messages to destinations.
/// </summary>
/// <remarks>
/// <para>
/// Each tap creates a dynamic subscription to the source topic that forwards
/// all received messages to the destination exchange with the configured routing.
/// </para>
/// <para>
/// Unlike MassTransit, this implementation receives raw bytes and forwards them
/// without type-based routing. This allows tapping arbitrary message types.
/// </para>
/// </remarks>
public sealed class RabbitMQMessageTap : IMessageTap, IAsyncDisposable
{
    private readonly IChannelManager _channelManager;
    private readonly ILogger<RabbitMQMessageTap> _logger;
    private readonly MessagingServiceConfiguration _configuration;
    private readonly ITelemetryProvider _telemetryProvider;
    private readonly ConcurrentDictionary<Guid, TapHandleImpl> _activeTaps = new();

    // Track declared exchanges to avoid redeclaring (ConcurrentDictionary for lock-free access)
    private readonly ConcurrentDictionary<string, byte> _declaredExchanges = new();

    /// <summary>
    /// Creates a new RabbitMQMessageTap instance.
    /// </summary>
    /// <param name="channelManager">Channel manager for RabbitMQ operations.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="configuration">Messaging service configuration.</param>
    /// <param name="telemetryProvider">Telemetry provider for instrumentation.</param>
    public RabbitMQMessageTap(
        IChannelManager channelManager,
        ILogger<RabbitMQMessageTap> logger,
        MessagingServiceConfiguration configuration,
        ITelemetryProvider telemetryProvider)
    {
        _channelManager = channelManager;
        _logger = logger;
        _configuration = configuration;
        _telemetryProvider = telemetryProvider;
    }

    /// <inheritdoc/>
    public async Task<ITapHandle> CreateTapAsync(
        string sourceTopic,
        TapDestination destination,
        string? sourceExchange = null,
        CancellationToken cancellationToken = default)
    {
        using var activity = _telemetryProvider?.StartActivity(
            TelemetryComponents.Messaging,
            "messaging.tap.create",
            ActivityKind.Client);
        activity?.SetTag("messaging.tap.source_topic", sourceTopic);
        activity?.SetTag("messaging.tap.destination_exchange", destination.Exchange);

        var tapId = Guid.NewGuid();
        var effectiveSourceExchange = sourceExchange ?? _configuration.DefaultExchange;
        var createdAt = DateTimeOffset.UtcNow;

        // Create a unique queue name for this tap's source subscription
        var tapQueueName = $"tap.{tapId:N}.{sourceTopic}";

        _logger.LogDebug(
            "Creating tap {TapId} from {SourceExchange}/{SourceTopic} to {DestExchange}/{DestRoutingKey} ({DestType})",
            tapId,
            effectiveSourceExchange,
            sourceTopic,
            destination.Exchange,
            destination.RoutingKey,
            destination.ExchangeType);

        try
        {
            // Create a dedicated channel for this tap
            var channel = await _channelManager.CreateConsumerChannelAsync(cancellationToken);

            // Ensure source exchange exists (topic for service events - routes by routing key)
            await EnsureExchangeAsync(channel, effectiveSourceExchange, RmqExchangeType.Topic, cancellationToken);

            // Ensure destination exchange exists
            var destExchangeType = destination.ExchangeType switch
            {
                TapExchangeType.Direct => RmqExchangeType.Direct,
                TapExchangeType.Fanout => RmqExchangeType.Fanout,
                _ => RmqExchangeType.Topic
            };

            if (destination.CreateExchangeIfNotExists)
            {
                await EnsureExchangeAsync(channel, destination.Exchange, destExchangeType, cancellationToken);
            }

            // Declare the tap queue
            await channel.QueueDeclareAsync(
                queue: tapQueueName,
                durable: false,          // Tap queues are transient
                exclusive: false,
                autoDelete: true,        // Clean up when tap is removed
                arguments: null,
                cancellationToken: cancellationToken);

            // Bind tap queue to source exchange
            // For topic exchanges, routing key determines which messages are received
            await channel.QueueBindAsync(
                queue: tapQueueName,
                exchange: effectiveSourceExchange,
                routingKey: sourceTopic,
                arguments: null,
                cancellationToken: cancellationToken);

            _logger.LogInformation(
                "Tap {TapId}: Bound queue '{QueueName}' to source exchange '{Exchange}' (routing key: '{RoutingKey}')",
                tapId, tapQueueName, effectiveSourceExchange, sourceTopic);

            // Create consumer that receives raw bytes
            var consumer = new AsyncEventingBasicConsumer(channel);
            consumer.ReceivedAsync += async (sender, ea) =>
            {
                _logger.LogDebug(
                    "Tap {TapId}: Received message on queue '{QueueName}'",
                    tapId, tapQueueName);

                try
                {
                    await ForwardRawMessageAsync(
                        channel,
                        ea.Body,
                        ea.BasicProperties,
                        tapId,
                        sourceTopic,
                        effectiveSourceExchange,
                        destination,
                        createdAt,
                        cancellationToken);

                    await channel.BasicAckAsync(ea.DeliveryTag, multiple: false);
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "Tap {TapId}: Failed to forward message from {SourceTopic}",
                        tapId,
                        sourceTopic);
                    await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false);
                }
            };

            // Start consuming
            var consumerTag = await channel.BasicConsumeAsync(
                queue: tapQueueName,
                autoAck: false,
                consumer: consumer,
                cancellationToken: cancellationToken);

            var tapHandle = new TapHandleImpl(
                tapId,
                sourceTopic,
                destination,
                createdAt,
                channel,
                consumerTag,
                this);

            _activeTaps[tapId] = tapHandle;

            _logger.LogInformation(
                "Created tap {TapId} from {SourceExchange}/{SourceTopic} to {DestExchange}/{DestRoutingKey}",
                tapId,
                effectiveSourceExchange,
                sourceTopic,
                destination.Exchange,
                destination.RoutingKey);

            return tapHandle;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to create tap {TapId} from {SourceTopic} to {DestExchange}/{DestRoutingKey}",
                tapId,
                sourceTopic,
                destination.Exchange,
                destination.RoutingKey);
            throw;
        }
    }

    private async Task ForwardRawMessageAsync(
        IChannel channel,
        ReadOnlyMemory<byte> body,
        IReadOnlyBasicProperties basicProperties,
        Guid tapId,
        string sourceTopic,
        string sourceExchange,
        TapDestination destination,
        DateTimeOffset tapCreatedAt,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider?.StartActivity(
            TelemetryComponents.Messaging,
            "messaging.tap.forward",
            ActivityKind.Producer);
        activity?.SetTag("messaging.tap.id", tapId.ToString());
        activity?.SetTag("messaging.tap.source_topic", sourceTopic);

        Guid? eventId = null;
        string? eventName = null;
        DateTimeOffset timestamp = DateTimeOffset.UtcNow;

        // Parse the raw JSON to extract eventId, eventName, and timestamp
        string rawJsonPayload = Encoding.UTF8.GetString(body.Span);

        try
        {
            using var doc = JsonDocument.Parse(rawJsonPayload);
            var root = doc.RootElement;

            // Extract eventId from the message
            if (root.TryGetProperty("eventId", out var eventIdProp) &&
                eventIdProp.TryGetGuid(out var parsedId))
            {
                eventId = parsedId;
            }

            // Extract eventName from the message
            if (root.TryGetProperty("eventName", out var eventNameProp))
            {
                eventName = eventNameProp.GetString();
            }

            // Extract timestamp from the message
            if (root.TryGetProperty("timestamp", out var timestampProp) &&
                timestampProp.TryGetDateTimeOffset(out var ts))
            {
                timestamp = ts;
            }
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(
                ex,
                "Tap {TapId}: Failed to parse message JSON for eventId extraction",
                tapId);
        }

        if (!eventId.HasValue)
        {
            // Try to parse MessageId as Guid, otherwise generate new
            if (basicProperties.MessageId != null && Guid.TryParse(basicProperties.MessageId, out var msgId))
            {
                eventId = msgId;
            }
            else
            {
                eventId = Guid.NewGuid();
            }
        }

        // Create tapped envelope with the raw JSON as payload
        // Note: eventId is guaranteed to have a value by this point (assigned in fallback above)
        var tappedEnvelope = new TappedMessageEnvelope
        {
            EventId = eventId.Value,
            EventName = eventName ?? $"tap.{sourceTopic}",
            Timestamp = timestamp,
            Topic = sourceTopic,
            PayloadJson = rawJsonPayload,
            ContentType = "application/json",
            TapId = tapId,
            SourceTopic = sourceTopic,
            SourceExchange = sourceExchange,
            DestinationExchange = destination.Exchange,
            DestinationRoutingKey = destination.RoutingKey,
            DestinationExchangeType = destination.ExchangeType,
            TapCreatedAt = tapCreatedAt,
            ForwardedAt = DateTimeOffset.UtcNow
        };

        // Serialize the envelope
        var envelopeJson = BannouJson.Serialize(tappedEnvelope);
        var envelopeBytes = Encoding.UTF8.GetBytes(envelopeJson);

        // Build properties for the forwarded message
        var properties = new BasicProperties
        {
            MessageId = Guid.NewGuid().ToString(),
            ContentType = "application/json",
            DeliveryMode = DeliveryModes.Persistent,
            Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds())
        };

        // Determine routing key for destination
        var effectiveRoutingKey = destination.ExchangeType == TapExchangeType.Fanout
            ? ""
            : destination.RoutingKey;

        // Publish to destination
        await channel.BasicPublishAsync(
            exchange: destination.Exchange,
            routingKey: effectiveRoutingKey,
            mandatory: false,
            basicProperties: properties,
            body: envelopeBytes,
            cancellationToken: cancellationToken);

        _logger.LogDebug(
            "Tap {TapId}: Forwarded message {EventId} from {SourceTopic} to {DestExchange}/{DestRoutingKey}",
            tapId,
            eventId,
            sourceTopic,
            destination.Exchange,
            destination.RoutingKey);
    }

    /// <summary>
    /// Ensures an exchange is declared.
    /// </summary>
    private async Task EnsureExchangeAsync(
        IChannel channel,
        string exchange,
        string exchangeType,
        CancellationToken cancellationToken)
    {
        // Check if already declared (optimization to avoid redeclaring)
        var key = $"{exchange}:{exchangeType}";
        if (_declaredExchanges.ContainsKey(key))
        {
            return;
        }

        await channel.ExchangeDeclareAsync(
            exchange: exchange,
            type: exchangeType,
            durable: true,
            autoDelete: false,
            arguments: null,
            cancellationToken: cancellationToken);

        _declaredExchanges.TryAdd(key, 0);

        _logger.LogDebug("Declared exchange '{Exchange}' of type {Type}", exchange, exchangeType);
    }

    /// <summary>
    /// Removes a tap by its ID.
    /// </summary>
    internal async Task RemoveTapAsync(Guid tapId)
    {
        using var activity = _telemetryProvider?.StartActivity(
            TelemetryComponents.Messaging,
            "messaging.tap.remove",
            ActivityKind.Client);

        TapHandleImpl? tap = null;
        try
        {
            if (!_activeTaps.TryRemove(tapId, out tap))
            {
                return;
            }

            await tap.Channel.BasicCancelAsync(tap.ConsumerTag);
            await tap.Channel.CloseAsync();
            tap.MarkInactive();

            _logger.LogInformation(
                "Removed tap {TapId} from {SourceTopic}",
                tapId,
                tap.SourceTopic);

            tap = null; // Cleanup complete - prevents double dispose in finally
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error removing tap {TapId}",
                tapId);
            throw;
        }
        finally
        {
            // Ensure tap is marked inactive even if an exception occurred during cleanup
            tap?.MarkInactive();
        }
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        foreach (var (tapId, tap) in _activeTaps)
        {
            try
            {
                await tap.Channel.BasicCancelAsync(tap.ConsumerTag);
                await tap.Channel.CloseAsync();
                tap.MarkInactive();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error disposing tap {TapId}", tapId);
            }
        }
        _activeTaps.Clear();

        _logger.LogInformation("RabbitMQMessageTap disposed");
    }

    /// <summary>
    /// Internal implementation of ITapHandle.
    /// </summary>
    private sealed class TapHandleImpl : ITapHandle
    {
        private readonly RabbitMQMessageTap _parent;
        private bool _isActive = true;

        public Guid TapId { get; }
        public string SourceTopic { get; }
        public TapDestination Destination { get; }
        public DateTimeOffset CreatedAt { get; }
        public bool IsActive => _isActive;
        public IChannel Channel { get; }
        public string ConsumerTag { get; }

        public TapHandleImpl(
            Guid tapId,
            string sourceTopic,
            TapDestination destination,
            DateTimeOffset createdAt,
            IChannel channel,
            string consumerTag,
            RabbitMQMessageTap parent)
        {
            TapId = tapId;
            SourceTopic = sourceTopic;
            Destination = destination;
            CreatedAt = createdAt;
            Channel = channel;
            ConsumerTag = consumerTag;
            _parent = parent;
        }

        public void MarkInactive() => _isActive = false;

        public async ValueTask DisposeAsync()
        {
            if (_isActive)
            {
                await _parent.RemoveTapAsync(TapId);
            }
        }
    }
}
