#nullable enable

using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Configuration;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using System.Text;

namespace BeyondImmersion.BannouService.Messaging.Services;

/// <summary>
/// Direct RabbitMQ implementation of IMessageBus.
/// Publishes messages without MassTransit envelope overhead.
/// </summary>
/// <remarks>
/// <para>
/// This implementation publishes plain JSON messages directly to RabbitMQ,
/// avoiding MassTransit's type-based routing and envelope format.
/// </para>
/// <para>
/// Messages are serialized using BannouJson for consistency across the codebase.
/// </para>
/// <para>
/// All publish methods use the Try* pattern - they never throw exceptions.
/// Failed publishes are automatically buffered and retried via MessageRetryBuffer.
/// If the connection stays down too long or buffer overflows, the node crashes
/// to trigger a restart by the orchestrator.
/// </para>
/// </remarks>
public sealed class RabbitMQMessageBus : IMessageBus
{
    private readonly RabbitMQConnectionManager _connectionManager;
    private readonly MessageRetryBuffer _retryBuffer;
    private readonly ILogger<RabbitMQMessageBus> _logger;

    // Track declared exchanges to avoid redeclaring
    private readonly HashSet<string> _declaredExchanges = new();
    private readonly object _exchangeLock = new();

    /// <summary>
    /// Topic for service error events.
    /// </summary>
    private const string SERVICE_ERROR_TOPIC = "service.error";

    /// <summary>
    /// Creates a new RabbitMQMessageBus instance.
    /// </summary>
    public RabbitMQMessageBus(
        RabbitMQConnectionManager connectionManager,
        MessageRetryBuffer retryBuffer,
        ILogger<RabbitMQMessageBus> logger)
    {
        _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
        _retryBuffer = retryBuffer ?? throw new ArgumentNullException(nameof(retryBuffer));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public async Task<bool> TryPublishAsync<TEvent>(
        string topic,
        TEvent eventData,
        PublishOptions? options = null,
        Guid? messageId = null,
        CancellationToken cancellationToken = default)
        where TEvent : class
    {
        // Generate messageId upfront if not provided
        var effectiveMessageId = messageId ?? Guid.NewGuid();

        try
        {
            ArgumentNullException.ThrowIfNull(topic);
            ArgumentNullException.ThrowIfNull(eventData);

            var exchange = options?.Exchange ?? _connectionManager.DefaultExchange;
            var exchangeType = options?.ExchangeType ?? PublishOptionsExchangeType.Topic;
            var routingKey = options?.RoutingKey ?? topic;

            // Serialize - if this fails, it's a programming error (not retryable)
            string json;
            byte[] body;
            try
            {
                json = BannouJson.Serialize(eventData);
                body = Encoding.UTF8.GetBytes(json);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to serialize {EventType} for topic '{Topic}' - this is a programming error",
                    typeof(TEvent).Name, topic);
                return false;
            }

            var channel = await _connectionManager.GetChannelAsync(cancellationToken);
            try
            {
                // Ensure exchange exists
                await EnsureExchangeAsync(channel, exchange, exchangeType, cancellationToken);

                // Build properties
                var properties = new BasicProperties
                {
                    MessageId = effectiveMessageId.ToString(),
                    ContentType = "application/json",
                    DeliveryMode = (options?.Persistent ?? true) ? DeliveryModes.Persistent : DeliveryModes.Transient,
                    Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds())
                };

                if (options?.CorrelationId.HasValue == true)
                {
                    properties.CorrelationId = options.CorrelationId.Value.ToString();
                }

                if (options?.Expiration.HasValue == true)
                {
                    properties.Expiration = ((int)options.Expiration.Value.TotalMilliseconds).ToString();
                }

                if (options?.Priority > 0)
                {
                    properties.Priority = (byte)Math.Min(options.Priority, 9);
                }

                if (options?.Headers is IDictionary<string, object> headers && headers.Count > 0)
                {
                    properties.Headers = new Dictionary<string, object?>(
                        headers.Select(kvp => new KeyValuePair<string, object?>(kvp.Key, kvp.Value)));
                }

                // Publish - for fanout, routing key is ignored but we still pass it for logging
                var effectiveRoutingKey = exchangeType == PublishOptionsExchangeType.Fanout ? "" : routingKey;

                await channel.BasicPublishAsync(
                    exchange: exchange,
                    routingKey: effectiveRoutingKey,
                    mandatory: false,
                    basicProperties: properties,
                    body: body,
                    cancellationToken: cancellationToken);

                _logger.LogDebug(
                    "Published {EventType} to exchange '{Exchange}' (type: {ExchangeType}, routingKey: '{RoutingKey}') with MessageId {MessageId}",
                    typeof(TEvent).Name,
                    exchange,
                    exchangeType,
                    routingKey,
                    effectiveMessageId);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to publish {EventType} to exchange '{Exchange}', buffering for retry",
                    typeof(TEvent).Name,
                    exchange);

                // Buffer for retry - this may crash the node if buffer is full/stale
                _retryBuffer.EnqueueForRetry(topic, body, options, effectiveMessageId);
                return true; // Buffered successfully, will be retried
            }
            finally
            {
                _connectionManager.ReturnChannel(channel);
            }
        }
        catch (Exception ex)
        {
            // Catch-all for any unexpected errors (channel acquisition, etc.)
            _logger.LogError(ex, "Unexpected error in TryPublishAsync for topic '{Topic}'", topic);
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task<bool> TryPublishRawAsync(
        string topic,
        ReadOnlyMemory<byte> payload,
        string contentType,
        PublishOptions? options = null,
        Guid? messageId = null,
        CancellationToken cancellationToken = default)
    {
        // Generate messageId upfront if not provided
        var effectiveMessageId = messageId ?? Guid.NewGuid();

        try
        {
            ArgumentNullException.ThrowIfNull(topic);
            ArgumentNullException.ThrowIfNull(contentType);

            var exchange = options?.Exchange ?? _connectionManager.DefaultExchange;
            var exchangeType = options?.ExchangeType ?? PublishOptionsExchangeType.Topic;
            var routingKey = options?.RoutingKey ?? topic;

            var channel = await _connectionManager.GetChannelAsync(cancellationToken);
            try
            {
                // Ensure exchange exists
                await EnsureExchangeAsync(channel, exchange, exchangeType, cancellationToken);

                // Build properties
                var properties = new BasicProperties
                {
                    MessageId = effectiveMessageId.ToString(),
                    ContentType = contentType,
                    DeliveryMode = (options?.Persistent ?? true) ? DeliveryModes.Persistent : DeliveryModes.Transient,
                    Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds())
                };

                if (options?.CorrelationId.HasValue == true)
                {
                    properties.CorrelationId = options.CorrelationId.Value.ToString();
                }

                if (options?.Expiration.HasValue == true)
                {
                    properties.Expiration = ((int)options.Expiration.Value.TotalMilliseconds).ToString();
                }

                // Publish
                var effectiveRoutingKey = exchangeType == PublishOptionsExchangeType.Fanout ? "" : routingKey;

                await channel.BasicPublishAsync(
                    exchange: exchange,
                    routingKey: effectiveRoutingKey,
                    mandatory: false,
                    basicProperties: properties,
                    body: payload,
                    cancellationToken: cancellationToken);

                _logger.LogDebug(
                    "Published raw message ({Size} bytes, {ContentType}) to exchange '{Exchange}' with MessageId {MessageId}",
                    payload.Length,
                    contentType,
                    exchange,
                    effectiveMessageId);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to publish raw message to exchange '{Exchange}', buffering for retry",
                    exchange);

                // Buffer for retry - this may crash the node if buffer is full/stale
                _retryBuffer.EnqueueForRetry(topic, payload.ToArray(), options, effectiveMessageId);
                return true; // Buffered successfully, will be retried
            }
            finally
            {
                _connectionManager.ReturnChannel(channel);
            }
        }
        catch (Exception ex)
        {
            // Catch-all for any unexpected errors
            _logger.LogError(ex, "Unexpected error in TryPublishRawAsync for topic '{Topic}'", topic);
            return false;
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
        string? correlationId = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var errorEvent = new ServiceErrorEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                ServiceId = Guid.Parse(Program.ServiceGUID),
                ServiceName = serviceName,
                AppId = Program.Configuration.EffectiveAppId,
                Operation = operation,
                ErrorType = errorType,
                Message = message,
                Dependency = dependency,
                Endpoint = endpoint,
                Severity = severity,
                Details = details,
                Stack = stack,
                CorrelationId = correlationId
            };

            return await TryPublishAsync(SERVICE_ERROR_TOPIC, errorEvent, null, cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            // Extra safety net - TryPublishAsync shouldn't throw, but just in case
            _logger.LogWarning(ex, "Failed to publish ServiceErrorEvent for {ServiceName}/{Operation}", serviceName, operation);
            return false;
        }
    }

    /// <summary>
    /// Ensures an exchange is declared.
    /// </summary>
    private async Task EnsureExchangeAsync(
        IChannel channel,
        string exchange,
        PublishOptionsExchangeType exchangeType,
        CancellationToken cancellationToken)
    {
        // Check if already declared (optimization to avoid redeclaring)
        var key = $"{exchange}:{exchangeType}";
        lock (_exchangeLock)
        {
            if (_declaredExchanges.Contains(key))
            {
                return;
            }
        }

        var type = exchangeType switch
        {
            PublishOptionsExchangeType.Direct => ExchangeType.Direct,
            PublishOptionsExchangeType.Topic => ExchangeType.Topic,
            _ => ExchangeType.Fanout
        };

        await channel.ExchangeDeclareAsync(
            exchange: exchange,
            type: type,
            durable: true,
            autoDelete: false,
            arguments: null,
            cancellationToken: cancellationToken);

        lock (_exchangeLock)
        {
            _declaredExchanges.Add(key);
        }

        _logger.LogDebug("Declared exchange '{Exchange}' of type {Type}", exchange, type);
    }
}
