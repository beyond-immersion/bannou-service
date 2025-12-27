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
/// TENET EXCEPTION: Uses direct RabbitMQ because lib-messaging pub/sub doesn't support
/// dynamic runtime subscriptions. This is similar to how Orchestrator uses direct Redis.
/// </para>
/// <para>
/// Architecture uses dedicated "bannou-client-events" DIRECT exchange for client events:
/// - Queue name: CONNECT_SESSION_{sessionId} (matches routing key used by publisher)
/// - Bound to "bannou-client-events" direct exchange with binding key = queue name
/// - Routing key filtering happens AT THE BROKER (efficient, no flood of unrelated messages)
/// - Messages buffer in queue during client disconnect (RabbitMQ handles buffering)
/// - On reconnect, client gets all pending messages automatically
/// </para>
/// <para>
/// This is SEPARATE from service events (heartbeats, mappings, IEventConsumer subscriptions)
/// which use the "bannou" fanout exchange via lib-messaging's MassTransitMessageSubscriber.
/// </para>
/// <para>
/// Queue lifecycle:
/// - Created on first subscription (subscriber creates queue before publisher sends)
/// - Persists during disconnect to buffer messages
/// - Auto-expires after 5 minutes of inactivity via RabbitMQ policy
///   (policy defined in provisioning/rabbitmq/definitions.json, pattern: ^CONNECT_SESSION_.*)
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
    /// Prefix for session-specific queues.
    /// Must match the routing key used by MessageBusClientEventPublisher.
    /// </summary>
    private const string SESSION_QUEUE_PREFIX = "CONNECT_SESSION_";

    /// <summary>
    /// Dedicated direct exchange for client events.
    /// Defined in provisioning/rabbitmq/definitions.json.
    /// MUST NOT use "bannou" - that's the fanout exchange for service events.
    /// </summary>
    private const string CLIENT_EVENTS_EXCHANGE = "bannou-client-events";

    // NOTE: Queue arguments (x-expires, x-message-ttl) are NOT set in code.
    // RabbitMQ policy (defined in definitions.json) applies x-expires: 300000ms (5 min)
    // to all queues matching pattern "^CONNECT_SESSION_.*".
    // This avoids PRECONDITION_FAILED errors when queue arguments don't match.

    /// <summary>
    /// Unique identifier for this Connect instance (for logging/debugging).
    /// </summary>
    private readonly string _instanceId;

    /// <summary>
    /// Creates a new ClientEventRabbitMQSubscriber.
    /// </summary>
    /// <param name="connectionString">RabbitMQ connection string from configuration.</param>
    /// <param name="logger">Logger for connection operations.</param>
    /// <param name="eventHandler">Callback to handle received events (sessionId, eventPayload).</param>
    /// <param name="instanceId">Unique identifier for this Connect instance (for logging).</param>
    public ClientEventRabbitMQSubscriber(
        string connectionString,
        ILogger<ClientEventRabbitMQSubscriber> logger,
        Func<string, byte[], Task> eventHandler,
        string? instanceId = null)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _eventHandler = eventHandler ?? throw new ArgumentNullException(nameof(eventHandler));

        // Instance ID for logging/debugging (not used in queue naming anymore)
        _instanceId = instanceId ?? Guid.NewGuid().ToString("N")[..8];
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
    /// Creates/declares a queue bound to the client events direct exchange.
    /// </summary>
    /// <remarks>
    /// Queue naming matches the routing key used by MessageBusClientEventPublisher.
    /// Direct exchange routing ensures only messages for this session are delivered.
    /// Queue persists during disconnect to buffer messages - RabbitMQ handles buffering natively.
    /// </remarks>
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
            // Queue name = routing key used by MessageBusClientEventPublisher
            var queueName = $"{SESSION_QUEUE_PREFIX}{sessionId}";

            // Declare the queue with standard properties
            // NOTE: x-expires is applied via RabbitMQ policy (definitions.json), not here
            // This avoids PRECONDITION_FAILED errors if policy settings change
            await _channel.QueueDeclareAsync(
                queue: queueName,
                durable: true,           // Survives broker restart
                exclusive: false,        // Allow any Connect instance to consume (cross-instance reconnection)
                autoDelete: false,       // Keep queue during disconnect for message buffering
                arguments: null,         // No custom arguments - x-expires applied via policy
                cancellationToken: cancellationToken);

            // Bind queue to direct exchange with binding key = queue name
            // Direct exchange delivers only to queues with matching binding key
            await _channel.QueueBindAsync(
                queue: queueName,
                exchange: CLIENT_EVENTS_EXCHANGE,
                routingKey: queueName,   // Binding key matches routing key used by publisher
                arguments: null,
                cancellationToken: cancellationToken);

            // Create async consumer with manual acknowledgment
            var consumer = new AsyncEventingBasicConsumer(_channel);
            consumer.ReceivedAsync += async (sender, ea) =>
            {
                try
                {
                    // Call the event handler with session ID and payload
                    await _eventHandler(sessionId, ea.Body.ToArray());

                    // Acknowledge the message - removes from queue
                    await _channel.BasicAckAsync(ea.DeliveryTag, multiple: false);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "Error processing client event for session {SessionId}",
                        sessionId);

                    // Nack WITH requeue - message goes back to queue for retry
                    // This handles the case where delivery fails mid-processing
                    // On disconnect, consumer cancellation handles unacked messages
                    await _channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: true);
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

            _logger.LogInformation(
                "Subscribed to client events for session {SessionId} (queue: {QueueName}, instance: {InstanceId})",
                sessionId, queueName, _instanceId);

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
    /// <remarks>
    /// <para>
    /// Cancels the consumer but does NOT delete the queue. The queue persists to buffer
    /// messages during disconnect. RabbitMQ handles this natively - no Redis fallback needed.
    /// </para>
    /// <para>
    /// On consumer cancellation, RabbitMQ automatically requeues any unacked messages.
    /// When client reconnects, we re-subscribe and get all pending messages.
    /// </para>
    /// <para>
    /// Queue cleanup is handled by RabbitMQ policy (definitions.json) which applies
    /// x-expires: 300000ms (5 min) to queues matching pattern "^CONNECT_SESSION_.*".
    /// </para>
    /// </remarks>
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
                // Cancel consumer - RabbitMQ requeues any unacked messages automatically
                await _channel.BasicCancelAsync(consumerTag, cancellationToken: cancellationToken);
                _logger.LogInformation(
                    "Unsubscribed from client events for session {SessionId} (queue persists for buffering)",
                    sessionId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Error cancelling consumer for session {SessionId}",
                    sessionId);
            }
        }

        // Note: Queue persists (no delete) - will buffer messages during disconnect
        // Cleanup happens via RabbitMQ policy (x-expires: 5 min) after inactivity
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
