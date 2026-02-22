#nullable enable

using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Messaging.Services;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using RmqExchangeType = RabbitMQ.Client.ExchangeType;

namespace BeyondImmersion.BannouService.Messaging;

/// <summary>
/// Background service that consumes dead-lettered messages from the DLX exchange,
/// logs them with structured data for observability, and publishes service error events
/// for downstream monitoring (e.g., Analytics).
/// </summary>
/// <remarks>
/// <para>
/// Dead letters arrive via two paths:
/// (1) Explicit publish from <see cref="MessageRetryBuffer"/> after retry exhaustion
///     (headers: x-original-topic, x-retry-count, x-discard-reason, etc.)
/// (2) RabbitMQ automatic dead-lettering when messages are nack'd without requeue
///     (headers: x-death, x-first-death-reason, x-first-death-queue, etc.)
/// </para>
/// <para>
/// Uses a durable shared queue (<c>bannou-dlx-consumer</c>) so accumulated dead letters
/// are not lost across restarts. Multiple instances share the queue via RabbitMQ competing
/// consumers (IMPLEMENTATION TENETS: multi-instance safety).
/// </para>
/// <para>
/// Uses <see cref="IChannelManager"/> directly instead of <see cref="IMessageSubscriber"/>
/// because <c>SubscribeDynamicRawAsync</c> only passes raw bytes to the handler — dead letter
/// processing requires access to <c>BasicProperties.Headers</c> for metadata extraction.
/// </para>
/// </remarks>
public sealed class DeadLetterConsumerService : BackgroundService
{
    /// <summary>
    /// Maximum number of characters from the message body to include in log output.
    /// Prevents logging excessively large payloads while providing debugging context.
    /// </summary>
    private const int PayloadPreviewMaxLength = 500;

    /// <summary>
    /// Durable queue name for the dead letter consumer. Shared across restarts and instances.
    /// </summary>
    private const string ConsumerQueueName = "bannou-dlx-consumer";

    /// <summary>
    /// Routing key pattern to match all dead-lettered messages regardless of original topic.
    /// </summary>
    private const string DeadLetterRoutingKeyPattern = "dead-letter.#";

    private readonly IChannelManager _channelManager;
    private readonly IMessageBus _messageBus;
    private readonly MessagingServiceConfiguration _configuration;
    private readonly ILogger<DeadLetterConsumerService> _logger;
    private readonly ITelemetryProvider _telemetryProvider;
    private IChannel? _consumerChannel;

    /// <summary>
    /// Creates a new dead letter consumer service.
    /// </summary>
    /// <param name="channelManager">Channel manager for RabbitMQ channel creation.</param>
    /// <param name="messageBus">Message bus for publishing error events.</param>
    /// <param name="configuration">Messaging configuration with DLX settings.</param>
    /// <param name="logger">Logger for structured dead letter logging.</param>
    /// <param name="telemetryProvider">Telemetry provider for span instrumentation.</param>
    public DeadLetterConsumerService(
        IChannelManager channelManager,
        IMessageBus messageBus,
        MessagingServiceConfiguration configuration,
        ILogger<DeadLetterConsumerService> logger,
        ITelemetryProvider telemetryProvider)
    {
        _channelManager = channelManager;
        _messageBus = messageBus;
        _configuration = configuration;
        _logger = logger;
        _telemetryProvider = telemetryProvider;
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_configuration.DeadLetterConsumerEnabled)
        {
            _logger.LogInformation("Dead letter consumer is disabled via configuration");
            return;
        }

        // Wait for other services to initialize before subscribing
        await Task.Delay(
            TimeSpan.FromSeconds(_configuration.DeadLetterConsumerStartupDelaySeconds),
            stoppingToken);

        using var activity = _telemetryProvider.StartActivity(
            TelemetryComponents.Messaging,
            "DeadLetterConsumer.Execute",
            ActivityKind.Consumer);

        try
        {
            _consumerChannel = await _channelManager.CreateConsumerChannelAsync(stoppingToken);

            var dlxExchange = _configuration.DeadLetterExchange;

            // Ensure DLX exchange exists (topic type for routing-key based matching)
            await _consumerChannel.ExchangeDeclareAsync(
                exchange: dlxExchange,
                type: RmqExchangeType.Topic,
                durable: true,
                autoDelete: false,
                arguments: null,
                cancellationToken: stoppingToken);

            // Declare a durable, non-auto-delete queue that persists across restarts
            // so accumulated dead letters are consumed even after service restarts
            await _consumerChannel.QueueDeclareAsync(
                queue: ConsumerQueueName,
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: null,
                cancellationToken: stoppingToken);

            // Bind to all dead letter routing keys
            await _consumerChannel.QueueBindAsync(
                queue: ConsumerQueueName,
                exchange: dlxExchange,
                routingKey: DeadLetterRoutingKeyPattern,
                arguments: null,
                cancellationToken: stoppingToken);

            // Create consumer with manual ack
            var consumer = new AsyncEventingBasicConsumer(_consumerChannel);
            consumer.ReceivedAsync += async (sender, ea) =>
            {
                await ProcessDeadLetterAsync(ea, stoppingToken);
            };

            await _consumerChannel.BasicConsumeAsync(
                queue: ConsumerQueueName,
                autoAck: false,
                consumer: consumer,
                cancellationToken: stoppingToken);

            _logger.LogInformation(
                "Dead letter consumer started on exchange {Exchange} queue {Queue}",
                dlxExchange,
                ConsumerQueueName);

            // Keep the service alive until shutdown
            await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown
            _logger.LogDebug("Dead letter consumer shutting down");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Dead letter consumer failed to start or lost connection");
        }
    }

    /// <summary>
    /// Processes a single dead-lettered message: extracts metadata headers, logs structured
    /// information, publishes a service error event, and acks the message.
    /// </summary>
    private async Task ProcessDeadLetterAsync(BasicDeliverEventArgs ea, CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity(
            TelemetryComponents.Messaging,
            "DeadLetterConsumer.ProcessMessage",
            ActivityKind.Consumer);

        try
        {
            var headers = ea.BasicProperties.Headers;

            // Extract metadata from both dead letter paths
            var originalTopic = ExtractStringHeader(headers, "x-original-topic");
            var originalExchange = ExtractStringHeader(headers, "x-original-exchange");
            var retryCount = ExtractIntHeader(headers, "x-retry-count");
            var discardReason = ExtractStringHeader(headers, "x-discard-reason");
            var queuedAtMs = ExtractLongHeader(headers, "x-queued-at");
            var discardedAtMs = ExtractLongHeader(headers, "x-discarded-at");

            // Extract RabbitMQ automatic dead-letter headers (nack'd messages)
            var firstDeathReason = ExtractStringHeader(headers, "x-first-death-reason");
            var firstDeathQueue = ExtractStringHeader(headers, "x-first-death-queue");
            var firstDeathExchange = ExtractStringHeader(headers, "x-first-death-exchange");

            // Determine effective values (explicit headers take priority over RabbitMQ automatic ones)
            var effectiveTopic = originalTopic ?? firstDeathQueue ?? ea.RoutingKey;
            var effectiveReason = discardReason ?? firstDeathReason ?? "unknown";
            var effectiveExchange = originalExchange ?? firstDeathExchange;

            // Set telemetry tags
            activity?.SetTag("messaging.dead_letter.original_topic", effectiveTopic);
            activity?.SetTag("messaging.dead_letter.reason", effectiveReason);
            if (retryCount.HasValue)
            {
                activity?.SetTag("messaging.dead_letter.retry_count", retryCount.Value);
            }

            // Extract truncated payload preview for debugging
            var payloadPreview = ExtractPayloadPreview(ea.Body);

            // Calculate age if timestamps are available
            var ageDescription = CalculateAgeDescription(queuedAtMs, discardedAtMs);

            _logger.LogError(
                "Dead letter consumed: topic={OriginalTopic} reason={Reason} retryCount={RetryCount} " +
                "exchange={Exchange} routingKey={RoutingKey} messageId={MessageId} " +
                "age={Age} bodySize={BodySize} payloadPreview={PayloadPreview}",
                effectiveTopic,
                effectiveReason,
                retryCount,
                effectiveExchange,
                ea.RoutingKey,
                ea.BasicProperties.MessageId,
                ageDescription,
                ea.Body.Length,
                payloadPreview);

            // Publish service error event for downstream monitoring
            // Severity is Warning (not Error) because the original failure was already logged
            // as Error by the retry buffer or subscriber — this is the investigation trail
            await _messageBus.TryPublishErrorAsync(
                serviceName: "messaging",
                operation: "dead-letter-consumer",
                errorType: "DeadLetter",
                message: $"Dead letter from topic '{effectiveTopic}': {effectiveReason}",
                dependency: "RabbitMQ",
                endpoint: effectiveTopic,
                severity: ServiceErrorEventSeverity.Warning,
                details: new
                {
                    OriginalTopic = effectiveTopic,
                    OriginalExchange = effectiveExchange,
                    DiscardReason = effectiveReason,
                    RetryCount = retryCount,
                    RoutingKey = ea.RoutingKey,
                    MessageId = ea.BasicProperties.MessageId,
                    BodySize = ea.Body.Length,
                    Age = ageDescription
                },
                cancellationToken: ct);

            await _consumerChannel!.BasicAckAsync(ea.DeliveryTag, multiple: false, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to process dead letter (routingKey={RoutingKey}, deliveryTag={DeliveryTag})",
                ea.RoutingKey,
                ea.DeliveryTag);

            // Nack without requeue — don't re-dead-letter a dead letter
            try
            {
                await _consumerChannel!.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false, ct);
            }
            catch (Exception nackEx)
            {
                _logger.LogWarning(nackEx, "Failed to nack dead letter (deliveryTag={DeliveryTag})", ea.DeliveryTag);
            }
        }
    }

    /// <summary>
    /// Extracts a string value from AMQP message headers.
    /// Headers may contain byte[] (AMQP binary) or direct string values.
    /// </summary>
    internal static string? ExtractStringHeader(IDictionary<string, object?>? headers, string key)
    {
        if (headers == null || !headers.TryGetValue(key, out var value) || value == null)
        {
            return null;
        }

        return value switch
        {
            byte[] bytes => Encoding.UTF8.GetString(bytes),
            string str => str,
            _ => value.ToString()
        };
    }

    /// <summary>
    /// Extracts an integer value from AMQP message headers.
    /// </summary>
    internal static int? ExtractIntHeader(IDictionary<string, object?>? headers, string key)
    {
        if (headers == null || !headers.TryGetValue(key, out var value) || value == null)
        {
            return null;
        }

        return value switch
        {
            int i => i,
            long l => (int)l,
            _ => int.TryParse(value.ToString(), out var parsed) ? parsed : null
        };
    }

    /// <summary>
    /// Extracts a long value from AMQP message headers (used for Unix timestamps in milliseconds).
    /// </summary>
    internal static long? ExtractLongHeader(IDictionary<string, object?>? headers, string key)
    {
        if (headers == null || !headers.TryGetValue(key, out var value) || value == null)
        {
            return null;
        }

        return value switch
        {
            long l => l,
            int i => i,
            _ => long.TryParse(value.ToString(), out var parsed) ? parsed : null
        };
    }

    /// <summary>
    /// Extracts a truncated preview of the message body for debugging context.
    /// Never logs the full payload (could be very large or contain sensitive data).
    /// </summary>
    internal static string ExtractPayloadPreview(ReadOnlyMemory<byte> body)
    {
        if (body.IsEmpty)
        {
            return "(empty)";
        }

        try
        {
            var length = Math.Min(body.Length, PayloadPreviewMaxLength);
            var preview = Encoding.UTF8.GetString(body.Span[..length]);
            if (body.Length > PayloadPreviewMaxLength)
            {
                preview += $"... ({body.Length} bytes total)";
            }
            return preview;
        }
        catch
        {
            return $"(binary, {body.Length} bytes)";
        }
    }

    /// <summary>
    /// Calculates a human-readable age description from queued and discarded timestamps.
    /// </summary>
    internal static string? CalculateAgeDescription(long? queuedAtMs, long? discardedAtMs)
    {
        if (!queuedAtMs.HasValue)
        {
            return null;
        }

        var queuedAt = DateTimeOffset.FromUnixTimeMilliseconds(queuedAtMs.Value);
        var discardedAt = discardedAtMs.HasValue
            ? DateTimeOffset.FromUnixTimeMilliseconds(discardedAtMs.Value)
            : DateTimeOffset.UtcNow;

        var age = discardedAt - queuedAt;
        return age.TotalSeconds < 60
            ? $"{age.TotalSeconds:F1}s"
            : $"{age.TotalMinutes:F1}m";
    }

    /// <inheritdoc/>
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_consumerChannel != null)
        {
            try
            {
                await _consumerChannel.CloseAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error closing dead letter consumer channel");
            }
        }

        await base.StopAsync(cancellationToken);
    }
}
