#nullable enable

using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Services;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Messaging.Services;

/// <summary>
/// MassTransit-based implementation of IMessageBus.
/// Provides mature .NET abstractions over RabbitMQ.
/// </summary>
/// <remarks>
/// <para>
/// This implementation uses MassTransit's IBus for publishing messages to RabbitMQ.
/// Messages are serialized using BannouJson for consistency with the rest of the codebase.
/// </para>
/// <para>
/// The implementation supports both typed event publishing and raw binary publishing
/// for performance-critical paths.
/// </para>
/// </remarks>
public sealed class MassTransitMessageBus : IMessageBus
{
    private readonly IBus _bus;
    private readonly ILogger<MassTransitMessageBus> _logger;
    private readonly MessagingServiceConfiguration _configuration;

    /// <summary>
    /// Creates a new MassTransitMessageBus instance.
    /// </summary>
    /// <param name="bus">MassTransit bus instance</param>
    /// <param name="logger">Logger for this service</param>
    /// <param name="configuration">Messaging configuration</param>
    public MassTransitMessageBus(
        IBus bus,
        ILogger<MassTransitMessageBus> logger,
        MessagingServiceConfiguration configuration)
    {
        _bus = bus ?? throw new ArgumentNullException(nameof(bus));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
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
        var exchange = options?.Exchange ?? _configuration.DefaultExchange;

        try
        {
            // Build the exchange URI for MassTransit
            // Format: exchange:{exchangeName}?bind=true&queue={topicName}
            var endpointUri = new Uri($"exchange:{exchange}?bind=true&queue={topic}");
            var endpoint = await _bus.GetSendEndpoint(endpointUri);

            await endpoint.Send(eventData, context =>
            {
                context.MessageId = messageId;

                if (options?.CorrelationId.HasValue == true)
                {
                    context.CorrelationId = options.CorrelationId;
                }

                if (options?.Expiration.HasValue == true)
                {
                    context.TimeToLive = options.Expiration.Value;
                }

                if (options?.Priority > 0)
                {
                    // MassTransit priority support varies by transport
                    context.Headers.Set("x-priority", options.Priority);
                }

                if (options?.Headers is IDictionary<string, object> headers)
                {
                    foreach (var header in headers)
                    {
                        context.Headers.Set(header.Key, header.Value);
                    }
                }

                // Set persistence
                context.Durable = options?.Persistent ?? true;
            }, cancellationToken);

            _logger.LogDebug(
                "Published {EventType} to topic '{Topic}' (exchange: {Exchange}) with MessageId {MessageId}",
                typeof(TEvent).Name,
                topic,
                exchange,
                messageId);

            return messageId;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to publish {EventType} to topic '{Topic}' (exchange: {Exchange})",
                typeof(TEvent).Name,
                topic,
                exchange);
            throw;
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
        var exchange = options?.Exchange ?? _configuration.DefaultExchange;

        try
        {
            // For raw bytes, we use a wrapper message
            var rawMessage = new RawBinaryMessage
            {
                Payload = payload.ToArray(),
                ContentType = contentType
            };

            var endpointUri = new Uri($"exchange:{exchange}?bind=true&queue={topic}");
            var endpoint = await _bus.GetSendEndpoint(endpointUri);

            await endpoint.Send(rawMessage, context =>
            {
                context.MessageId = messageId;
                context.ContentType = new System.Net.Mime.ContentType(contentType);

                if (options?.CorrelationId.HasValue == true)
                {
                    context.CorrelationId = options.CorrelationId;
                }

                if (options?.Expiration.HasValue == true)
                {
                    context.TimeToLive = options.Expiration.Value;
                }

                context.Durable = options?.Persistent ?? true;
            }, cancellationToken);

            _logger.LogDebug(
                "Published raw message ({Size} bytes, {ContentType}) to topic '{Topic}' with MessageId {MessageId}",
                payload.Length,
                contentType,
                topic,
                messageId);

            return messageId;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to publish raw message to topic '{Topic}'",
                topic);
            throw;
        }
    }

    /// <summary>
    /// Topic for service error events.
    /// </summary>
    private const string SERVICE_ERROR_TOPIC = "service.error";

    /// <inheritdoc/>
    public async Task<bool> TryPublishErrorAsync(
        string serviceId,
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
                ServiceId = serviceId,
                AppId = Environment.GetEnvironmentVariable("BANNOU_APP_ID") ?? "bannou",
                Operation = operation,
                ErrorType = errorType,
                Message = message,
                Dependency = dependency,
                Endpoint = endpoint,
                Severity = severity,
                Stack = stack,
                CorrelationId = correlationId
            };

            await PublishAsync(SERVICE_ERROR_TOPIC, errorEvent, null, cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            // Avoid cascading failures when pub/sub infrastructure is the culprit.
            _logger.LogWarning(ex, "Failed to publish ServiceErrorEvent for {ServiceId}/{Operation}", serviceId, operation);
            return false;
        }
    }
}

/// <summary>
/// Wrapper for raw binary messages sent through MassTransit.
/// </summary>
internal class RawBinaryMessage
{
    /// <summary>
    /// The raw payload bytes.
    /// </summary>
    public byte[] Payload { get; set; } = Array.Empty<byte>();

    /// <summary>
    /// The content type of the payload.
    /// </summary>
    public string ContentType { get; set; } = "application/octet-stream";
}
