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
public sealed class MessageRetryBuffer : IAsyncDisposable
{
    private readonly RabbitMQConnectionManager _connectionManager;
    private readonly MessagingServiceConfiguration _configuration;
    private readonly ILogger<MessageRetryBuffer> _logger;

    private readonly ConcurrentQueue<BufferedMessage> _buffer = new();
    private readonly Timer _retryTimer;
    private readonly SemaphoreSlim _processingLock = new(1, 1);

    private int _bufferCount;
    private bool _disposed;
    private bool _isProcessing;

    /// <summary>
    /// Creates a new MessageRetryBuffer.
    /// </summary>
    public MessageRetryBuffer(
        RabbitMQConnectionManager connectionManager,
        MessagingServiceConfiguration configuration,
        ILogger<MessageRetryBuffer> logger)
    {
        _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

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
    /// Adds a message to the retry buffer.
    /// </summary>
    /// <param name="topic">The topic/routing key</param>
    /// <param name="jsonPayload">The serialized JSON payload</param>
    /// <param name="options">Publish options (exchange, routing key, etc.)</param>
    /// <param name="messageId">The message ID to track</param>
    public void EnqueueForRetry(
        string topic,
        byte[] jsonPayload,
        PublishOptions? options,
        Guid messageId)
    {
        if (!_configuration.RetryBufferEnabled)
        {
            throw new InvalidOperationException("Retry buffer is disabled");
        }

        var message = new BufferedMessage
        {
            Topic = topic,
            JsonPayload = jsonPayload,
            Options = options,
            MessageId = messageId,
            QueuedAt = DateTimeOffset.UtcNow,
            RetryCount = 0
        };

        _buffer.Enqueue(message);
        var newCount = Interlocked.Increment(ref _bufferCount);

        _logger.LogWarning(
            "Message buffered for retry (topic: {Topic}, messageId: {MessageId}, bufferSize: {BufferSize})",
            topic, messageId, newCount);

        // Check buffer health after enqueue
        CheckBufferHealth();
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
            Environment.FailFast(message);
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
                Environment.FailFast(message);
            }
        }
    }

    /// <summary>
    /// Timer callback to process the retry buffer.
    /// </summary>
    private async void ProcessRetryBuffer(object? state)
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
            await ProcessBufferedMessagesAsync();
        }
        catch (Exception ex)
        {
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
    private async Task ProcessBufferedMessagesAsync()
    {
        if (_bufferCount == 0)
        {
            return;
        }

        _logger.LogDebug("Processing retry buffer ({Count} messages)", _bufferCount);

        IChannel? channel = null;
        try
        {
            channel = await _connectionManager.GetChannelAsync();
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
            var messagesToRequeue = new List<BufferedMessage>();

            // Process all messages currently in the queue
            var initialCount = _bufferCount;
            for (var i = 0; i < initialCount && _buffer.TryDequeue(out var message); i++)
            {
                Interlocked.Decrement(ref _bufferCount);

                try
                {
                    var exchange = message.Options?.Exchange ?? _connectionManager.DefaultExchange;
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

                    _logger.LogWarning(
                        ex,
                        "Failed to retry buffered message (topic: {Topic}, messageId: {MessageId}, retryCount: {RetryCount})",
                        message.Topic, message.MessageId, message.RetryCount);

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

            if (processedCount > 0 || failedCount > 0)
            {
                _logger.LogInformation(
                    "Retry buffer processing complete (processed: {Processed}, failed: {Failed}, remaining: {Remaining})",
                    processedCount, failedCount, _bufferCount);
            }
        }
        finally
        {
            _connectionManager.ReturnChannel(channel);
        }
    }

    /// <summary>
    /// Track declared exchanges to avoid redeclaring.
    /// </summary>
    private readonly HashSet<string> _declaredExchanges = new();
    private readonly object _exchangeLock = new();

    /// <summary>
    /// Ensures an exchange is declared.
    /// </summary>
    private async Task EnsureExchangeAsync(
        IChannel channel,
        string exchange,
        PublishOptionsExchangeType exchangeType)
    {
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
            arguments: null);

        lock (_exchangeLock)
        {
            _declaredExchanges.Add(key);
        }
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
    }
}
