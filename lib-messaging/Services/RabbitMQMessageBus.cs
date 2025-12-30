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
/// </remarks>
public sealed class RabbitMQMessageBus : IMessageBus
{
    private readonly RabbitMQConnectionManager _connectionManager;
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
        ILogger<RabbitMQMessageBus> logger)
    {
        _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public async Task<Guid> PublishAsync<TEvent>(
        string topic,
        TEvent eventData,
        PublishOptions? options = null,
        CancellationToken cancellationToken = default)
        where TEvent : class
    {
        ArgumentNullException.ThrowIfNull(topic);
        ArgumentNullException.ThrowIfNull(eventData);

        var messageId = Guid.NewGuid();
        var exchange = options?.Exchange ?? _connectionManager.DefaultExchange;
        var exchangeType = options?.ExchangeType ?? PublishOptionsExchangeType.Fanout;
        var routingKey = options?.RoutingKey ?? topic;

        var channel = await _connectionManager.GetChannelAsync(cancellationToken);
        try
        {
            // Ensure exchange exists
            await EnsureExchangeAsync(channel, exchange, exchangeType, cancellationToken);

            // Serialize the event
            var json = BannouJson.Serialize(eventData);
            var body = Encoding.UTF8.GetBytes(json);

            // Build properties
            var properties = new BasicProperties
            {
                MessageId = messageId.ToString(),
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
                messageId);

            return messageId;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to publish {EventType} to exchange '{Exchange}'",
                typeof(TEvent).Name,
                exchange);
            throw;
        }
        finally
        {
            _connectionManager.ReturnChannel(channel);
        }
    }

    /// <inheritdoc/>
    public async Task<Guid> PublishRawAsync(
        string topic,
        ReadOnlyMemory<byte> payload,
        string contentType,
        PublishOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(topic);
        ArgumentNullException.ThrowIfNull(contentType);

        var messageId = Guid.NewGuid();
        var exchange = options?.Exchange ?? _connectionManager.DefaultExchange;
        var exchangeType = options?.ExchangeType ?? PublishOptionsExchangeType.Fanout;
        var routingKey = options?.RoutingKey ?? topic;

        var channel = await _connectionManager.GetChannelAsync(cancellationToken);
        try
        {
            // Ensure exchange exists
            await EnsureExchangeAsync(channel, exchange, exchangeType, cancellationToken);

            // Build properties
            var properties = new BasicProperties
            {
                MessageId = messageId.ToString(),
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
                messageId);

            return messageId;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to publish raw message to exchange '{Exchange}'",
                exchange);
            throw;
        }
        finally
        {
            _connectionManager.ReturnChannel(channel);
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
                EventName = ServiceErrorEventEventName.Service_error,
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                ServiceId = Guid.Parse(Program.ServiceGUID),
                ServiceName = serviceName,
                AppId = Environment.GetEnvironmentVariable(AppConstants.ENV_BANNOU_APP_ID) ?? AppConstants.DEFAULT_APP_NAME,
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

            await PublishAsync(SERVICE_ERROR_TOPIC, errorEvent, null, cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            // Avoid cascading failures when pub/sub infrastructure is the culprit.
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
