using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;

namespace BeyondImmersion.BannouService.Connect.ClientEvents;

/// <summary>
/// Manages direct RabbitMQ subscriptions for session-specific client events.
/// </summary>
/// <remarks>
/// <para>
/// TENET EXCEPTION: Uses direct RabbitMQ because Dapr pub/sub doesn't support
/// dynamic runtime subscriptions. This is similar to how Orchestrator uses direct Redis.
/// </para>
/// <para>
/// When a client connects, we create a queue and bind it to the session-specific
/// exchange (CONNECT_SESSION_{sessionId}). Dapr creates these exchanges automatically
/// when services publish to the topic.
/// </para>
/// </remarks>
public class ClientEventRabbitMQSubscriber : IAsyncDisposable
{
    private readonly ILogger<ClientEventRabbitMQSubscriber> _logger;
    private readonly Func<string, byte[], Task> _eventHandler;
    private readonly string _connectionString;

    private IConnection? _connection;
    private IChannel? _channel;

    // Track consumer tags per session for cleanup
    private readonly ConcurrentDictionary<string, string> _sessionConsumerTags = new();
    private readonly ConcurrentDictionary<string, string> _sessionQueueNames = new();

    private const int MAX_RETRY_ATTEMPTS = 10;
    private const int INITIAL_RETRY_DELAY_MS = 1000;

    /// <summary>
    /// Prefix for session-specific topics/exchanges.
    /// Must match DaprClientEventPublisher.SESSION_TOPIC_PREFIX.
    /// </summary>
    private const string SESSION_TOPIC_PREFIX = "CONNECT_SESSION_";

    /// <summary>
    /// Queue name prefix for this Connect instance.
    /// </summary>
    private readonly string _queuePrefix;

    /// <summary>
    /// Creates a new ClientEventRabbitMQSubscriber.
    /// </summary>
    /// <param name="logger">Logger for connection operations.</param>
    /// <param name="eventHandler">Callback to handle received events (sessionId, eventPayload).</param>
    /// <param name="instanceId">Unique identifier for this Connect instance (for queue naming).</param>
    public ClientEventRabbitMQSubscriber(
        ILogger<ClientEventRabbitMQSubscriber> logger,
        Func<string, byte[], Task> eventHandler,
        string? instanceId = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _eventHandler = eventHandler ?? throw new ArgumentNullException(nameof(eventHandler));

        // Read connection string directly from environment (like OrchestratorRedisManager)
        _connectionString = Environment.GetEnvironmentVariable("BANNOU_RabbitMQConnectionString")
            ?? Environment.GetEnvironmentVariable("RabbitMQConnectionString")
            ?? "amqp://guest:guest@rabbitmq:5672";

        // Create unique queue prefix for this instance
        _queuePrefix = $"connect-session-{instanceId ?? Guid.NewGuid().ToString("N")[..8]}";
    }

    /// <summary>
    /// Initialize RabbitMQ connection with retry logic.
    /// </summary>
    public async Task<bool> InitializeAsync(CancellationToken cancellationToken = default)
    {
        var retryDelay = INITIAL_RETRY_DELAY_MS;

        for (int attempt = 1; attempt <= MAX_RETRY_ATTEMPTS; attempt++)
        {
            try
            {
                _logger.LogInformation(
                    "Attempting RabbitMQ connection for client events (attempt {Attempt}/{MaxAttempts})",
                    attempt, MAX_RETRY_ATTEMPTS);

                var factory = new ConnectionFactory
                {
                    Uri = new Uri(_connectionString),
                    AutomaticRecoveryEnabled = true,
                    NetworkRecoveryInterval = TimeSpan.FromSeconds(10)
                    // Note: RabbitMQ.Client 7.x uses async consumers by default
                };

                _connection = await factory.CreateConnectionAsync(cancellationToken);
                _channel = await _connection.CreateChannelAsync(cancellationToken: cancellationToken);

                // Set prefetch to control message flow
                await _channel.BasicQosAsync(
                    prefetchSize: 0,
                    prefetchCount: 100,
                    global: false,
                    cancellationToken: cancellationToken);

                _logger.LogInformation("RabbitMQ connection for client events established successfully");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "RabbitMQ connection failed (attempt {Attempt}/{MaxAttempts}). Retrying in {DelayMs}ms...",
                    attempt, MAX_RETRY_ATTEMPTS, retryDelay);

                if (attempt < MAX_RETRY_ATTEMPTS)
                {
                    await Task.Delay(retryDelay, cancellationToken);
                    retryDelay = Math.Min(retryDelay * 2, 60000); // Exponential backoff, max 60s
                }
            }
        }

        _logger.LogError("Failed to connect to RabbitMQ after {MaxAttempts} attempts", MAX_RETRY_ATTEMPTS);
        return false;
    }

    /// <summary>
    /// Subscribe to client events for a specific session.
    /// Creates a queue and binds it to the session's exchange.
    /// </summary>
    /// <param name="sessionId">The session ID to subscribe to.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if subscription was successful.</returns>
    public async Task<bool> SubscribeToSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        if (_channel == null)
        {
            _logger.LogWarning("Cannot subscribe to session {SessionId}: channel not initialized", sessionId);
            return false;
        }

        if (string.IsNullOrWhiteSpace(sessionId))
        {
            _logger.LogWarning("Cannot subscribe to session: session ID is null or empty");
            return false;
        }

        // Don't double-subscribe
        if (_sessionConsumerTags.ContainsKey(sessionId))
        {
            _logger.LogDebug("Already subscribed to session {SessionId}", sessionId);
            return true;
        }

        try
        {
            var exchangeName = $"{SESSION_TOPIC_PREFIX}{sessionId}";
            var queueName = $"{_queuePrefix}-{sessionId}";

            // Declare the queue (auto-delete when no consumers)
            await _channel.QueueDeclareAsync(
                queue: queueName,
                durable: false,
                exclusive: false,
                autoDelete: true,
                arguments: new Dictionary<string, object?>
                {
                    // Queue TTL matches reconnection window (5 minutes)
                    ["x-message-ttl"] = 300000,
                    // Max queue length to prevent memory issues
                    ["x-max-length"] = 100
                },
                cancellationToken: cancellationToken);

            // Declare the exchange as fanout (Dapr creates these, but we declare to be safe)
            // IMPORTANT: Must match Dapr's pubsub-rabbitmq.yaml settings:
            //   - durable: true (matches Dapr's durable: "true")
            //   - autoDelete: true (matches Dapr's autoDeleteExchange: "true")
            // Mismatched settings cause PRECONDITION_FAILED errors from RabbitMQ
            await _channel.ExchangeDeclareAsync(
                exchange: exchangeName,
                type: ExchangeType.Fanout,
                durable: true,
                autoDelete: true,
                arguments: null,
                cancellationToken: cancellationToken);

            // Bind queue to exchange
            await _channel.QueueBindAsync(
                queue: queueName,
                exchange: exchangeName,
                routingKey: "", // Fanout ignores routing key
                arguments: null,
                cancellationToken: cancellationToken);

            // Create async consumer
            var consumer = new AsyncEventingBasicConsumer(_channel);
            consumer.ReceivedAsync += async (sender, ea) =>
            {
                try
                {
                    // Call the event handler with session ID and payload
                    await _eventHandler(sessionId, ea.Body.ToArray());

                    // Acknowledge the message
                    await _channel.BasicAckAsync(ea.DeliveryTag, multiple: false);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "Error processing client event for session {SessionId}",
                        sessionId);

                    // Nack without requeue (dead letter or discard)
                    await _channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false);
                }
            };

            // Start consuming
            var consumerTag = await _channel.BasicConsumeAsync(
                queue: queueName,
                autoAck: false,
                consumer: consumer,
                cancellationToken: cancellationToken);

            _sessionConsumerTags[sessionId] = consumerTag;
            _sessionQueueNames[sessionId] = queueName;

            _logger.LogDebug(
                "Subscribed to client events for session {SessionId} (queue: {QueueName})",
                sessionId, queueName);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to subscribe to client events for session {SessionId}",
                sessionId);
            return false;
        }
    }

    /// <summary>
    /// Unsubscribe from client events for a specific session.
    /// </summary>
    /// <param name="sessionId">The session ID to unsubscribe from.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task UnsubscribeFromSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        if (_channel == null)
        {
            return;
        }

        if (_sessionConsumerTags.TryRemove(sessionId, out var consumerTag))
        {
            try
            {
                await _channel.BasicCancelAsync(consumerTag, cancellationToken: cancellationToken);
                _logger.LogDebug("Unsubscribed from client events for session {SessionId}", sessionId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Error cancelling consumer for session {SessionId}",
                    sessionId);
            }
        }

        // Queue will auto-delete when consumer is gone
        _sessionQueueNames.TryRemove(sessionId, out _);
    }

    /// <summary>
    /// Get the number of active session subscriptions.
    /// </summary>
    public int ActiveSubscriptionCount => _sessionConsumerTags.Count;

    /// <summary>
    /// Check if a session has an active subscription.
    /// </summary>
    public bool IsSessionSubscribed(string sessionId) => _sessionConsumerTags.ContainsKey(sessionId);

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        // Cancel all consumers
        if (_channel != null)
        {
            foreach (var kvp in _sessionConsumerTags)
            {
                try
                {
                    await _channel.BasicCancelAsync(kvp.Value);
                }
                catch
                {
                    // Ignore errors during shutdown
                }
            }

            try
            {
                await _channel.CloseAsync();
            }
            catch
            {
                // Ignore errors during shutdown
            }
        }

        if (_connection != null)
        {
            try
            {
                await _connection.CloseAsync();
            }
            catch
            {
                // Ignore errors during shutdown
            }
        }

        _sessionConsumerTags.Clear();
        _sessionQueueNames.Clear();

        _logger.LogInformation("ClientEventRabbitMQSubscriber disposed");

        GC.SuppressFinalize(this);
    }
}
