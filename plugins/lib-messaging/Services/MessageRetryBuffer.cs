#nullable enable

using BeyondImmersion.BannouService.Messaging;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using System.Collections.Concurrent;
using System.Text;

namespace BeyondImmersion.BannouService.Messaging.Services;

/// <summary>
/// Buffers failed message publishes and retries them when the connection is available.
/// Crashes the node if the buffer grows too large or messages are stuck too long.
/// </summary>
/// <remarks>
/// <para>
/// This class provides reliability for event publishing by buffering messages that
/// fail to publish (typically due to transient connection issues) and retrying them
/// when the connection is restored. This allows callers to treat PublishAsync as
/// fire-and-forget without worrying about connection state.
/// </para>
/// <para>
/// Safety limits ensure the node crashes rather than accumulating unbounded messages
/// or silently dropping stale events. This is preferable to silent data loss because:
/// - Events are critical (account.created, session.updated, etc.)
/// - A crashed node will be restarted by the orchestrator
/// - The crash is visible in monitoring/alerting
/// </para>
/// </remarks>
public sealed class MessageRetryBuffer : IRetryBuffer, IAsyncDisposable
{
    private readonly IChannelManager _channelManager;
    private readonly MessagingServiceConfiguration _configuration;
    private readonly ILogger<MessageRetryBuffer> _logger;
    private readonly IProcessTerminator _processTerminator;

    private readonly ConcurrentQueue<BufferedMessage> _buffer = new();
    private readonly Timer _retryTimer;
    private readonly SemaphoreSlim _processingLock = new(1, 1);

    private int _bufferCount;
    private bool _disposed;
    private bool _isProcessing;
    private bool _backpressureLogged;

    /// <summary>
    /// Creates a new MessageRetryBuffer.
    /// </summary>
    /// <param name="channelManager">Channel manager for RabbitMQ operations.</param>
    /// <param name="configuration">Messaging service configuration.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="processTerminator">Process terminator for crash-fast behavior (optional, defaults to Environment.FailFast).</param>
    public MessageRetryBuffer(
        IChannelManager channelManager,
        MessagingServiceConfiguration configuration,
        ILogger<MessageRetryBuffer> logger,
        IProcessTerminator? processTerminator = null)
    {
        _channelManager = channelManager;
        _configuration = configuration;
        _logger = logger;
        _processTerminator = processTerminator ?? new DefaultProcessTerminator();

        if (_configuration.RetryBufferEnabled)
        {
            var intervalMs = _configuration.RetryBufferIntervalSeconds * 1000;
            _retryTimer = new Timer(ProcessRetryBuffer, null, intervalMs, intervalMs);
            _logger.LogInformation(
                "MessageRetryBuffer initialized (maxSize: {MaxSize}, maxAge: {MaxAge}s, interval: {Interval}s)",
                _configuration.RetryBufferMaxSize,
                _configuration.RetryBufferMaxAgeSeconds,
                _configuration.RetryBufferIntervalSeconds);
        }
        else
        {
            // Disabled timer - create but don't start
            _retryTimer = new Timer(_ => { }, null, Timeout.Infinite, Timeout.Infinite);
            _logger.LogWarning("MessageRetryBuffer is DISABLED - publish failures will throw immediately");
        }
    }

    /// <summary>
    /// Gets the current number of messages in the buffer.
    /// </summary>
    public int BufferCount => _bufferCount;

    /// <summary>
    /// Gets whether the retry buffer is enabled.
    /// </summary>
    public bool IsEnabled => _configuration.RetryBufferEnabled;

    /// <summary>
    /// Gets whether backpressure is currently active (buffer at or above threshold).
    /// </summary>
    /// <remarks>
    /// When backpressure is active, new messages will be rejected rather than buffered.
    /// Callers should check this before publishing to implement their own overflow strategy.
    /// </remarks>
    public bool IsBackpressureActive
    {
        get
        {
            if (!_configuration.RetryBufferEnabled)
            {
                return false;
            }

            var threshold = (int)(_configuration.RetryBufferMaxSize * _configuration.RetryBufferBackpressureThreshold);
            return _bufferCount >= threshold;
        }
    }

    /// <summary>
    /// Gets the backpressure threshold count (computed from config).
    /// </summary>
    public int BackpressureThreshold => (int)(_configuration.RetryBufferMaxSize * _configuration.RetryBufferBackpressureThreshold);

    /// <summary>
    /// Attempts to add a message to the retry buffer.
    /// </summary>
    /// <param name="topic">The topic/routing key</param>
    /// <param name="jsonPayload">The serialized JSON payload</param>
    /// <param name="options">Publish options (exchange, routing key, etc.)</param>
    /// <param name="messageId">The message ID to track</param>
    /// <returns>True if message was buffered, false if rejected due to backpressure.</returns>
    /// <remarks>
    /// When backpressure is active (buffer at threshold), new messages are rejected.
    /// Callers should handle the false return by implementing their own overflow strategy
    /// (e.g., dropping, external queue, rate limiting).
    /// </remarks>
    public bool TryEnqueueForRetry(
        string topic,
        byte[] jsonPayload,
        PublishOptions? options,
        Guid messageId)
    {
        if (!_configuration.RetryBufferEnabled)
        {
            return false;
        }

        // Check backpressure before enqueueing
        var threshold = BackpressureThreshold;
        var currentCount = _bufferCount;
        if (currentCount >= threshold)
        {
            if (!_backpressureLogged)
            {
                _backpressureLogged = true;
                _logger.LogWarning(
                    "Retry buffer backpressure active - rejecting new messages " +
                    "(current: {Current}, threshold: {Threshold}, max: {Max})",
                    currentCount, threshold, _configuration.RetryBufferMaxSize);
            }
            return false;
        }

        // Reset backpressure logged flag if we're below threshold
        if (_backpressureLogged && currentCount < threshold * 0.9)
        {
            _backpressureLogged = false;
            _logger.LogInformation(
                "Retry buffer backpressure released (current: {Current}, threshold: {Threshold})",
                currentCount, threshold);
        }

        var message = new BufferedMessage
        {
            Topic = topic,
            JsonPayload = jsonPayload,
            Options = options,
            MessageId = messageId,
            QueuedAt = DateTimeOffset.UtcNow,
            RetryCount = 0,
            NextRetryAt = DateTimeOffset.UtcNow // Retry immediately on first attempt
        };

        _buffer.Enqueue(message);
        var newCount = Interlocked.Increment(ref _bufferCount);

        _logger.LogWarning(
            "Message buffered for retry (topic: {Topic}, messageId: {MessageId}, bufferSize: {BufferSize})",
            topic, messageId, newCount);

        // Check buffer health after enqueue
        CheckBufferHealth();
        return true;
    }

    /// <summary>
    /// Adds a message to the retry buffer. Throws if buffer is disabled.
    /// </summary>
    /// <remarks>
    /// Prefer <see cref="TryEnqueueForRetry"/> for new code - this method exists for backwards compatibility.
    /// </remarks>
    [Obsolete("Use TryEnqueueForRetry instead for backpressure support")]
    public void EnqueueForRetry(
        string topic,
        byte[] jsonPayload,
        PublishOptions? options,
        Guid messageId)
    {
        if (!TryEnqueueForRetry(topic, jsonPayload, options, messageId))
        {
            throw new InvalidOperationException(
                "Retry buffer rejected message due to backpressure or being disabled");
        }
    }

    /// <summary>
    /// Checks buffer health and crashes if thresholds are exceeded.
    /// </summary>
    private void CheckBufferHealth()
    {
        // Check size threshold
        if (_bufferCount >= _configuration.RetryBufferMaxSize)
        {
            var message = $"FATAL: Message retry buffer exceeded max size ({_bufferCount} >= {_configuration.RetryBufferMaxSize}). " +
                        "RabbitMQ connection has been down too long. Crashing node for restart.";
            _logger.LogCritical(message);
            _processTerminator.TerminateProcess(message);
        }

        // Check age threshold (oldest message in queue)
        if (_buffer.TryPeek(out var oldest))
        {
            var age = DateTimeOffset.UtcNow - oldest.QueuedAt;
            if (age.TotalSeconds >= _configuration.RetryBufferMaxAgeSeconds)
            {
                var message = $"FATAL: Oldest buffered message is {age.TotalSeconds:F0}s old (max: {_configuration.RetryBufferMaxAgeSeconds}s). " +
                            "RabbitMQ connection has been down too long. Crashing node for restart.";
                _logger.LogCritical(message);
                _processTerminator.TerminateProcess(message);
            }
        }
    }

    /// <summary>
    /// Timer callback to process the retry buffer.
    /// Uses fire-and-forget pattern with proper error handling via ProcessRetryBufferAsync.
    /// </summary>
    /// <remarks>
    /// IMPLEMENTATION TENETS (T23): Timer callbacks cannot be async directly.
    /// We use fire-and-forget with discard operator, but all exceptions are
    /// handled inside ProcessRetryBufferAsync to prevent process crashes.
    /// </remarks>
    private void ProcessRetryBuffer(object? state)
    {
        // Fire-and-forget with proper error handling inside the async method
        _ = ProcessRetryBufferAsync();
    }

    /// <summary>
    /// Async implementation of retry buffer processing.
    /// All exceptions are caught and logged to prevent process crashes.
    /// </summary>
    private async Task ProcessRetryBufferAsync()
    {
        if (_disposed || _isProcessing || _bufferCount == 0)
        {
            return;
        }

        // Check buffer health on each tick
        CheckBufferHealth();

        if (!await _processingLock.WaitAsync(0))
        {
            // Already processing, skip this tick
            return;
        }

        try
        {
            _isProcessing = true;
            await ProcessBufferedMessagesInternalAsync();
        }
        catch (Exception ex)
        {
            // Catch all exceptions to prevent process crash from fire-and-forget
            _logger.LogError(ex, "Error processing retry buffer");
        }
        finally
        {
            _isProcessing = false;
            _processingLock.Release();
        }
    }

    /// <summary>
    /// Processes buffered messages, retrying failed publishes.
    /// </summary>
    private async Task ProcessBufferedMessagesInternalAsync()
    {
        if (_bufferCount == 0)
        {
            return;
        }

        _logger.LogDebug("Processing retry buffer ({Count} messages)", _bufferCount);

        IChannel? channel = null;
        try
        {
            channel = await _channelManager.GetChannelAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cannot get channel for retry processing - connection still unavailable");
            return;
        }

        try
        {
            var processedCount = 0;
            var failedCount = 0;
            var discardedCount = 0;
            var deferredCount = 0;
            var messagesToRequeue = new List<BufferedMessage>();
            var now = DateTimeOffset.UtcNow;

            // Process all messages currently in the queue
            var initialCount = _bufferCount;
            for (var i = 0; i < initialCount && _buffer.TryDequeue(out var message); i++)
            {
                Interlocked.Decrement(ref _bufferCount);

                // Check if message has exceeded max retry attempts (poison message)
                if (message.RetryCount >= _configuration.RetryMaxAttempts)
                {
                    discardedCount++;
                    _logger.LogError(
                        "Message exceeded max retries ({RetryCount}/{MaxAttempts}) for topic '{Topic}', " +
                        "discarding to dead-letter. MessageId: {MessageId}, Age: {Age:F1}s",
                        message.RetryCount,
                        _configuration.RetryMaxAttempts,
                        message.Topic,
                        message.MessageId,
                        (now - message.QueuedAt).TotalSeconds);

                    // Attempt to publish to dead-letter topic for investigation
                    await TryPublishToDeadLetterAsync(channel, message);
                    continue;
                }

                // Check if message is still in backoff period
                if (message.NextRetryAt > now)
                {
                    deferredCount++;
                    // Put back for later processing
                    _buffer.Enqueue(message);
                    Interlocked.Increment(ref _bufferCount);
                    continue;
                }

                try
                {
                    var exchange = message.Options?.Exchange ?? _channelManager.DefaultExchange;
                    var exchangeType = message.Options?.ExchangeType ?? PublishOptionsExchangeType.Topic;
                    var routingKey = message.Options?.RoutingKey ?? message.Topic;

                    // Ensure exchange exists (cached in connection manager)
                    await EnsureExchangeAsync(channel, exchange, exchangeType);

                    // Build properties
                    var properties = new BasicProperties
                    {
                        MessageId = message.MessageId.ToString(),
                        ContentType = "application/json",
                        DeliveryMode = (message.Options?.Persistent ?? true) ? DeliveryModes.Persistent : DeliveryModes.Transient,
                        Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds())
                    };

                    var effectiveRoutingKey = exchangeType == PublishOptionsExchangeType.Fanout ? "" : routingKey;

                    await channel.BasicPublishAsync(
                        exchange: exchange,
                        routingKey: effectiveRoutingKey,
                        mandatory: false,
                        basicProperties: properties,
                        body: message.JsonPayload);

                    processedCount++;
                    _logger.LogDebug(
                        "Successfully retried buffered message (topic: {Topic}, messageId: {MessageId}, retryCount: {RetryCount})",
                        message.Topic, message.MessageId, message.RetryCount);
                }
                catch (Exception ex)
                {
                    failedCount++;
                    message.RetryCount++;

                    // Calculate exponential backoff with cap
                    var backoffMs = Math.Min(
                        _configuration.RetryDelayMs * (int)Math.Pow(2, message.RetryCount - 1),
                        _configuration.RetryMaxBackoffMs);
                    message.NextRetryAt = now.AddMilliseconds(backoffMs);

                    _logger.LogWarning(
                        ex,
                        "Failed to retry buffered message (topic: {Topic}, messageId: {MessageId}, " +
                        "retryCount: {RetryCount}/{MaxRetries}, nextRetryIn: {BackoffMs}ms)",
                        message.Topic, message.MessageId, message.RetryCount,
                        _configuration.RetryMaxAttempts, backoffMs);

                    // Re-queue for next attempt
                    messagesToRequeue.Add(message);
                }
            }

            // Re-queue failed messages
            foreach (var message in messagesToRequeue)
            {
                _buffer.Enqueue(message);
                Interlocked.Increment(ref _bufferCount);
            }

            if (processedCount > 0 || failedCount > 0 || discardedCount > 0)
            {
                _logger.LogInformation(
                    "Retry buffer processing complete (processed: {Processed}, failed: {Failed}, " +
                    "discarded: {Discarded}, deferred: {Deferred}, remaining: {Remaining})",
                    processedCount, failedCount, discardedCount, deferredCount, _bufferCount);
            }
        }
        finally
        {
            await _channelManager.ReturnChannelAsync(channel);
        }
    }

    /// <summary>
    /// Track declared exchanges to avoid redeclaring (ConcurrentDictionary for lock-free access).
    /// </summary>
    private readonly ConcurrentDictionary<string, byte> _declaredExchanges = new();

    /// <summary>
    /// Attempts to publish a discarded message to the dead-letter topic for investigation.
    /// </summary>
    /// <remarks>
    /// This is best-effort - if publishing to DLX fails, we log and continue.
    /// The message is already being discarded, so we don't want to re-queue it.
    /// </remarks>
    private async Task TryPublishToDeadLetterAsync(IChannel channel, BufferedMessage message)
    {
        try
        {
            var dlxExchange = _configuration.DeadLetterExchange;

            // Ensure DLX exists
            await EnsureExchangeAsync(channel, dlxExchange, PublishOptionsExchangeType.Topic);

            // Build properties with additional context headers
            var properties = new BasicProperties
            {
                MessageId = message.MessageId.ToString(),
                ContentType = "application/json",
                DeliveryMode = DeliveryModes.Persistent,
                Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds()),
                Headers = new Dictionary<string, object?>
                {
                    ["x-original-topic"] = message.Topic,
                    ["x-original-exchange"] = message.Options?.Exchange ?? _channelManager.DefaultExchange,
                    ["x-retry-count"] = message.RetryCount,
                    ["x-queued-at"] = message.QueuedAt.ToUnixTimeMilliseconds(),
                    ["x-discarded-at"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    ["x-discard-reason"] = "max-retries-exceeded"
                }
            };

            // Route to dead-letter topic based on original topic
            var dlxRoutingKey = $"dead-letter.{message.Topic}";

            await channel.BasicPublishAsync(
                exchange: dlxExchange,
                routingKey: dlxRoutingKey,
                mandatory: false,
                basicProperties: properties,
                body: message.JsonPayload);

            _logger.LogDebug(
                "Published discarded message to dead-letter (topic: {DlxTopic}, originalTopic: {OriginalTopic}, messageId: {MessageId})",
                dlxRoutingKey, message.Topic, message.MessageId);
        }
        catch (Exception ex)
        {
            // Best-effort - log and continue, don't re-throw
            _logger.LogWarning(
                ex,
                "Failed to publish discarded message to dead-letter (topic: {Topic}, messageId: {MessageId}). " +
                "Message will be lost.",
                message.Topic, message.MessageId);
        }
    }

    /// <summary>
    /// Ensures an exchange is declared.
    /// </summary>
    private async Task EnsureExchangeAsync(
        IChannel channel,
        string exchange,
        PublishOptionsExchangeType exchangeType)
    {
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
            arguments: null);

        _declaredExchanges.TryAdd(key, 0);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        await _retryTimer.DisposeAsync();
        _processingLock.Dispose();

        if (_bufferCount > 0)
        {
            _logger.LogWarning(
                "MessageRetryBuffer disposed with {Count} messages still buffered - these will be lost",
                _bufferCount);
        }

        _logger.LogInformation("MessageRetryBuffer disposed");
    }

    /// <summary>
    /// Represents a message waiting to be retried.
    /// </summary>
    private struct BufferedMessage
    {
        public required string Topic { get; init; }
        public required byte[] JsonPayload { get; init; }
        public PublishOptions? Options { get; init; }
        public required Guid MessageId { get; init; }
        public required DateTimeOffset QueuedAt { get; init; }
        public int RetryCount { get; set; }
        public DateTimeOffset NextRetryAt { get; set; }
    }
}
