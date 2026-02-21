#nullable enable

using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Configuration;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Threading.Channels;

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
/// <para>
/// When ITelemetryProvider is available, publish operations are instrumented with
/// distributed tracing spans and metrics.
/// </para>
/// <para>
/// Optional batching mode (EnablePublishBatching) queues messages and sends them in
/// batches to reduce broker overhead for high-throughput scenarios.
/// </para>
/// </remarks>
public sealed class RabbitMQMessageBus : IMessageBus, IAsyncDisposable
{
    private readonly IChannelManager _channelManager;
    private readonly IRetryBuffer _retryBuffer;
    private readonly AppConfiguration _appConfiguration;
    private readonly MessagingServiceConfiguration _messagingConfiguration;
    private readonly ILogger<RabbitMQMessageBus> _logger;
    private readonly ITelemetryProvider _telemetryProvider;

    // Track declared exchanges to avoid redeclaring (ConcurrentDictionary for lock-free access)
    private readonly ConcurrentDictionary<string, byte> _declaredExchanges = new();

    /// <summary>
    /// Topic for service error events.
    /// </summary>
    private const string SERVICE_ERROR_TOPIC = "service.error";

    // Batching support
    private readonly Channel<PendingPublish>? _batchChannel;
    private readonly Task? _batchProcessorTask;
    private readonly CancellationTokenSource? _batchProcessorCts;
    private bool _disposed;

    /// <summary>
    /// Creates a new RabbitMQMessageBus instance.
    /// </summary>
    /// <param name="channelManager">Channel manager for RabbitMQ operations.</param>
    /// <param name="retryBuffer">Buffer for retry on failed publishes.</param>
    /// <param name="appConfiguration">Application configuration.</param>
    /// <param name="messagingConfiguration">Messaging service configuration.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="telemetryProvider">Telemetry provider for instrumentation (NullTelemetryProvider when telemetry disabled).</param>
    public RabbitMQMessageBus(
        IChannelManager channelManager,
        IRetryBuffer retryBuffer,
        AppConfiguration appConfiguration,
        MessagingServiceConfiguration messagingConfiguration,
        ILogger<RabbitMQMessageBus> logger,
        ITelemetryProvider telemetryProvider)
    {
        _channelManager = channelManager;
        _retryBuffer = retryBuffer;
        _appConfiguration = appConfiguration;
        _messagingConfiguration = messagingConfiguration;
        _logger = logger;
        _telemetryProvider = telemetryProvider;

        if (_telemetryProvider.TracingEnabled || _telemetryProvider.MetricsEnabled)
        {
            _logger.LogDebug(
                "RabbitMQMessageBus created with telemetry instrumentation: tracing={TracingEnabled}, metrics={MetricsEnabled}",
                _telemetryProvider.TracingEnabled, _telemetryProvider.MetricsEnabled);
        }

        // Initialize batching if enabled
        if (_messagingConfiguration.EnablePublishBatching)
        {
            _batchChannel = Channel.CreateUnbounded<PendingPublish>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false
            });
            _batchProcessorCts = new CancellationTokenSource();
            _batchProcessorTask = ProcessBatchesAsync(_batchProcessorCts.Token);

            _logger.LogInformation(
                "RabbitMQMessageBus batching enabled (batchSize: {BatchSize}, timeoutMs: {TimeoutMs})",
                _messagingConfiguration.PublishBatchSize,
                _messagingConfiguration.PublishBatchTimeoutMs);
        }
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

        // Start telemetry activity for this publish operation
        using var activity = _telemetryProvider?.StartActivity(
            TelemetryComponents.Messaging,
            "messaging.publish",
            ActivityKind.Producer);

        var sw = Stopwatch.StartNew();
        var success = false;

        try
        {
            var exchange = options?.Exchange ?? _channelManager.DefaultExchange;
            var exchangeType = options?.ExchangeType ?? PublishOptionsExchangeType.Topic;
            var routingKey = options?.RoutingKey ?? topic;

            // Set activity tags for tracing
            activity?.SetTag("messaging.system", "rabbitmq");
            activity?.SetTag("messaging.destination", topic);
            activity?.SetTag("messaging.operation", "publish");
            activity?.SetTag("messaging.message_id", effectiveMessageId.ToString());
            activity?.SetTag("messaging.rabbitmq.exchange", exchange);
            activity?.SetTag("messaging.rabbitmq.routing_key", routingKey);

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
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                return false;
            }

            // If batching is enabled, queue the message and wait for batch flush
            if (_batchChannel != null)
            {
                var pending = new PendingPublish(
                    topic,
                    body,
                    options,
                    effectiveMessageId,
                    new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously));

                await _batchChannel.Writer.WriteAsync(pending, cancellationToken);
                success = await pending.Completion.Task;
                activity?.SetStatus(success ? ActivityStatusCode.Ok : ActivityStatusCode.Error, success ? null : "Batch publish failed");
                return success;
            }

            _logger.LogDebug(
                "DIAG: TryPublishAsync entering GetChannelAsync for {EventType} topic '{Topic}' (exchange: '{Exchange}')",
                typeof(TEvent).Name, topic, exchange);

            var getChannelSw = Stopwatch.StartNew();
            var channel = await _channelManager.GetChannelAsync(cancellationToken);
            getChannelSw.Stop();
            if (getChannelSw.ElapsedMilliseconds > 100)
            {
                _logger.LogWarning(
                    "DIAG: GetChannelAsync took {ElapsedMs}ms for {EventType} on topic '{Topic}'",
                    getChannelSw.ElapsedMilliseconds, typeof(TEvent).Name, topic);
            }
            else
            {
                _logger.LogDebug(
                    "DIAG: GetChannelAsync completed in {ElapsedMs}ms for {EventType} on topic '{Topic}'",
                    getChannelSw.ElapsedMilliseconds, typeof(TEvent).Name, topic);
            }

            try
            {
                // Ensure exchange exists
                var ensureExchangeSw = Stopwatch.StartNew();
                await EnsureExchangeAsync(channel, exchange, exchangeType, cancellationToken);
                ensureExchangeSw.Stop();
                if (ensureExchangeSw.ElapsedMilliseconds > 100)
                {
                    _logger.LogWarning(
                        "DIAG: EnsureExchangeAsync took {ElapsedMs}ms for {EventType} on exchange '{Exchange}'",
                        ensureExchangeSw.ElapsedMilliseconds, typeof(TEvent).Name, exchange);
                }

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

                // Initialize headers dictionary
                var messageHeaders = new Dictionary<string, object?>();
                if (options?.Headers is IDictionary<string, object> headers && headers.Count > 0)
                {
                    foreach (var kvp in headers)
                    {
                        messageHeaders[kvp.Key] = kvp.Value;
                    }
                }

                // Inject W3C trace context headers if activity is active
                if (activity != null && Activity.Current != null)
                {
                    // W3C Trace Context propagation
                    messageHeaders["traceparent"] = $"00-{activity.TraceId}-{activity.SpanId}-{(activity.Recorded ? "01" : "00")}";
                    if (!string.IsNullOrEmpty(activity.TraceStateString))
                    {
                        messageHeaders["tracestate"] = activity.TraceStateString;
                    }
                }

                if (messageHeaders.Count > 0)
                {
                    properties.Headers = messageHeaders;
                }

                // Publish - for fanout, routing key is ignored but we still pass it for logging
                var effectiveRoutingKey = exchangeType == PublishOptionsExchangeType.Fanout ? "" : routingKey;

                _logger.LogDebug(
                    "DIAG: TryPublishAsync entering BasicPublishAsync for {EventType} topic '{Topic}' exchange '{Exchange}'",
                    typeof(TEvent).Name, topic, exchange);

                var publishSw = Stopwatch.StartNew();
                await channel.BasicPublishAsync(
                    exchange: exchange,
                    routingKey: effectiveRoutingKey,
                    mandatory: false,
                    basicProperties: properties,
                    body: body,
                    cancellationToken: cancellationToken);
                publishSw.Stop();

                if (publishSw.ElapsedMilliseconds > 100)
                {
                    _logger.LogWarning(
                        "DIAG: BasicPublishAsync took {ElapsedMs}ms for {EventType} on topic '{Topic}' exchange '{Exchange}'",
                        publishSw.ElapsedMilliseconds, typeof(TEvent).Name, topic, exchange);
                }
                else
                {
                    _logger.LogDebug(
                        "DIAG: BasicPublishAsync completed in {ElapsedMs}ms for {EventType} on topic '{Topic}'",
                        publishSw.ElapsedMilliseconds, typeof(TEvent).Name, topic);
                }

                _logger.LogDebug(
                    "Published {EventType} to exchange '{Exchange}' (type: {ExchangeType}, routingKey: '{RoutingKey}') with MessageId {MessageId}",
                    typeof(TEvent).Name,
                    exchange,
                    exchangeType,
                    routingKey,
                    effectiveMessageId);

                success = true;
                activity?.SetStatus(ActivityStatusCode.Ok);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to publish {EventType} to exchange '{Exchange}', attempting to buffer for retry",
                    typeof(TEvent).Name,
                    exchange);

                // Buffer for retry - may return false if backpressure is active
                if (_retryBuffer.TryEnqueueForRetry(topic, body, options, effectiveMessageId))
                {
                    success = true; // Buffered successfully, will be retried
                    return true;
                }
                else
                {
                    // Backpressure active - cannot buffer, message will be lost
                    _logger.LogError(
                        "BACKPRESSURE: Message dropped for topic '{Topic}' (messageId: {MessageId}). " +
                        "Buffer at capacity, RabbitMQ connection failure persisting.",
                        topic, effectiveMessageId);
                    activity?.SetStatus(ActivityStatusCode.Error, "Backpressure - message dropped");
                    return false;
                }
            }
            finally
            {
                await _channelManager.ReturnChannelAsync(channel);
            }
        }
        catch (Exception ex)
        {
            // Catch-all for any unexpected errors (channel acquisition, etc.)
            _logger.LogError(ex, "Unexpected error in TryPublishAsync for topic '{Topic}'", topic);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            return false;
        }
        finally
        {
            // Record telemetry metrics
            sw.Stop();
            RecordPublishMetrics(topic, success, sw.Elapsed.TotalSeconds);
        }
    }

    /// <summary>
    /// Records metrics for a publish operation.
    /// </summary>
    private void RecordPublishMetrics(string topic, bool success, double durationSeconds)
    {
        if (_telemetryProvider == null)
        {
            return;
        }

        var tags = new[]
        {
            new KeyValuePair<string, object?>("topic", topic),
            new KeyValuePair<string, object?>("exchange", _channelManager.DefaultExchange),
            new KeyValuePair<string, object?>("success", success)
        };

        _telemetryProvider.RecordCounter(TelemetryComponents.Messaging, TelemetryMetrics.MessagingPublished, 1, tags);
        _telemetryProvider.RecordHistogram(TelemetryComponents.Messaging, TelemetryMetrics.MessagingPublishDuration, durationSeconds, tags);
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

            var exchange = options?.Exchange ?? _channelManager.DefaultExchange;
            var exchangeType = options?.ExchangeType ?? PublishOptionsExchangeType.Topic;
            var routingKey = options?.RoutingKey ?? topic;

            var channel = await _channelManager.GetChannelAsync(cancellationToken);
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
                    "Failed to publish raw message to exchange '{Exchange}', attempting to buffer for retry",
                    exchange);

                // Buffer for retry - may return false if backpressure is active
                if (_retryBuffer.TryEnqueueForRetry(topic, payload.ToArray(), options, effectiveMessageId))
                {
                    return true; // Buffered successfully, will be retried
                }
                else
                {
                    // Backpressure active - cannot buffer, message will be lost
                    _logger.LogError(
                        "BACKPRESSURE: Raw message dropped for topic '{Topic}' (messageId: {MessageId}). " +
                        "Buffer at capacity, RabbitMQ connection failure persisting.",
                        topic, effectiveMessageId);
                    return false;
                }
            }
            finally
            {
                await _channelManager.ReturnChannelAsync(channel);
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
        Guid? correlationId = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var errorEvent = new ServiceErrorEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                ServiceId = Program.ServiceGUID,
                ServiceName = serviceName,
                AppId = _appConfiguration.EffectiveAppId,
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
        if (_declaredExchanges.ContainsKey(key))
        {
            return;
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

        _declaredExchanges.TryAdd(key, 0);

        _logger.LogDebug("Declared exchange '{Exchange}' of type {Type}", exchange, type);
    }

    /// <summary>
    /// Background task that processes batches of pending publishes.
    /// </summary>
    private async Task ProcessBatchesAsync(CancellationToken cancellationToken)
    {
        var batch = new List<PendingPublish>(_messagingConfiguration.PublishBatchSize);
        var timeoutMs = _messagingConfiguration.PublishBatchTimeoutMs;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                batch.Clear();

                // Wait for at least one message
                try
                {
                    // Use a timeout to ensure we flush partial batches
                    using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    timeoutCts.CancelAfter(timeoutMs);

                    // Read first message (blocking with timeout)
                    if (await _batchChannel!.Reader.WaitToReadAsync(timeoutCts.Token))
                    {
                        if (_batchChannel.Reader.TryRead(out var first))
                        {
                            batch.Add(first);
                        }
                    }
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    // Timeout without messages - continue to check for shutdown
                    continue;
                }

                // Fill batch up to max size (non-blocking)
                while (batch.Count < _messagingConfiguration.PublishBatchSize &&
                        _batchChannel!.Reader.TryRead(out var pending))
                {
                    batch.Add(pending);
                }

                if (batch.Count == 0)
                {
                    continue;
                }

                // Publish the batch
                await PublishBatchAsync(batch, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Normal shutdown - flush remaining messages
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Batch processor failed unexpectedly");
        }

        // Flush remaining messages on shutdown
        while (_batchChannel!.Reader.TryRead(out var pending))
        {
            batch.Add(pending);
        }

        if (batch.Count > 0)
        {
            _logger.LogInformation("Flushing {Count} remaining messages on shutdown", batch.Count);
            await PublishBatchAsync(batch, CancellationToken.None);
        }
    }

    /// <summary>
    /// Publishes a batch of pending messages.
    /// </summary>
    private async Task PublishBatchAsync(List<PendingPublish> batch, CancellationToken cancellationToken)
    {
        IChannel? channel = null;
        try
        {
            channel = await _channelManager.GetChannelAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get channel for batch publish - marking all {Count} messages as failed", batch.Count);
            foreach (var pending in batch)
            {
                pending.Completion.TrySetResult(false);
            }
            return;
        }

        try
        {
            foreach (var pending in batch)
            {
                try
                {
                    var exchange = pending.Options?.Exchange ?? _channelManager.DefaultExchange;
                    var exchangeType = pending.Options?.ExchangeType ?? PublishOptionsExchangeType.Topic;
                    var routingKey = pending.Options?.RoutingKey ?? pending.Topic;

                    // Ensure exchange exists
                    await EnsureExchangeAsync(channel, exchange, exchangeType, cancellationToken);

                    // Build properties
                    var properties = new BasicProperties
                    {
                        MessageId = pending.MessageId.ToString(),
                        ContentType = "application/json",
                        DeliveryMode = (pending.Options?.Persistent ?? true) ? DeliveryModes.Persistent : DeliveryModes.Transient,
                        Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds())
                    };

                    if (pending.Options?.CorrelationId.HasValue == true)
                    {
                        properties.CorrelationId = pending.Options.CorrelationId.Value.ToString();
                    }

                    if (pending.Options?.Expiration.HasValue == true)
                    {
                        properties.Expiration = ((int)pending.Options.Expiration.Value.TotalMilliseconds).ToString();
                    }

                    if (pending.Options?.Priority > 0)
                    {
                        properties.Priority = (byte)Math.Min(pending.Options.Priority, 9);
                    }

                    var effectiveRoutingKey = exchangeType == PublishOptionsExchangeType.Fanout ? "" : routingKey;

                    await channel.BasicPublishAsync(
                        exchange: exchange,
                        routingKey: effectiveRoutingKey,
                        mandatory: false,
                        basicProperties: properties,
                        body: pending.Body,
                        cancellationToken: cancellationToken);

                    pending.Completion.TrySetResult(true);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to publish message in batch (topic: {Topic}, messageId: {MessageId})",
                        pending.Topic, pending.MessageId);

                    // Try to buffer for retry
                    if (_retryBuffer.TryEnqueueForRetry(pending.Topic, pending.Body, pending.Options, pending.MessageId))
                    {
                        pending.Completion.TrySetResult(true); // Buffered successfully
                    }
                    else
                    {
                        pending.Completion.TrySetResult(false); // Failed and couldn't buffer
                    }
                }
            }

            _logger.LogDebug("Batch published {Count} messages", batch.Count);
        }
        finally
        {
            await _channelManager.ReturnChannelAsync(channel);
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        if (_batchProcessorCts != null && _batchProcessorTask != null)
        {
            // Signal shutdown and wait for processor to complete
            await _batchProcessorCts.CancelAsync();
            _batchChannel?.Writer.Complete();

            try
            {
                await _batchProcessorTask.WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch (TimeoutException)
            {
                _logger.LogWarning("Batch processor did not complete within timeout");
            }

            _batchProcessorCts.Dispose();
        }

        _logger.LogInformation("RabbitMQMessageBus disposed");
    }

    /// <summary>
    /// Represents a message pending batch publication.
    /// </summary>
    private sealed record PendingPublish(
        string Topic,
        byte[] Body,
        PublishOptions? Options,
        Guid MessageId,
        TaskCompletionSource<bool> Completion);
}
