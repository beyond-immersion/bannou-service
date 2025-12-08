using BeyondImmersion.BannouService.Orchestrator;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Text;
using System.Text.Json;

namespace LibOrchestrator;

/// <summary>
/// Manages direct RabbitMQ connections for orchestrator service.
/// CRITICAL: Uses direct RabbitMQ.Client (NOT Dapr) to avoid chicken-and-egg dependency.
/// Dapr sidecar depends on RabbitMQ being available, so orchestrator needs direct access.
/// </summary>
public class OrchestratorEventManager : IOrchestratorEventManager
{
    private readonly ILogger<OrchestratorEventManager> _logger;
    private readonly string _connectionString;
    private IConnection? _connection;
    private IChannel? _channel;

    private const int MAX_RETRY_ATTEMPTS = 10;
    private const int INITIAL_RETRY_DELAY_MS = 1000;
    private const int MAX_RETRY_DELAY_MS = 60000;

    private const string HEARTBEAT_EXCHANGE = "bannou-service-heartbeats";
    private const string HEARTBEAT_QUEUE = "orchestrator-heartbeat-queue";
    private const string RESTART_EXCHANGE = "bannou-service-restarts";
    private const string MAPPINGS_EXCHANGE = "bannou-service-mappings";
    private const string MAPPINGS_QUEUE = "orchestrator-mappings-queue";
    private const string DEPLOYMENT_EXCHANGE = "bannou-deployment-events";

    public event Action<ServiceHeartbeatEvent>? HeartbeatReceived;
    public event Action<ServiceMappingEvent>? ServiceMappingReceived;

    /// <summary>
    /// Creates OrchestratorEventManager with connection string read directly from environment.
    /// This avoids DI lifetime conflicts with scoped configuration classes.
    /// </summary>
    public OrchestratorEventManager(ILogger<OrchestratorEventManager> logger)
    {
        _logger = logger;
        // Read connection string directly from environment to avoid DI lifetime conflicts
        _connectionString = Environment.GetEnvironmentVariable("BANNOU_RabbitMqConnectionString")
            ?? Environment.GetEnvironmentVariable("RabbitMqConnectionString")
            ?? "amqp://guest:guest@rabbitmq:5672";
    }

    /// <summary>
    /// Initialize RabbitMQ connection with wait-on-startup retry logic.
    /// Uses exponential backoff to handle infrastructure startup delays.
    /// </summary>
    public async Task<bool> InitializeAsync(CancellationToken cancellationToken = default)
    {
        var retryDelay = INITIAL_RETRY_DELAY_MS;

        for (int attempt = 1; attempt <= MAX_RETRY_ATTEMPTS; attempt++)
        {
            try
            {
                _logger.LogInformation(
                    "Attempting RabbitMQ connection (attempt {Attempt}/{MaxAttempts}): {ConnectionString}",
                    attempt, MAX_RETRY_ATTEMPTS, MaskConnectionString(_connectionString));

                var factory = new ConnectionFactory
                {
                    Uri = new Uri(_connectionString),
                    AutomaticRecoveryEnabled = true,  // ✅ Automatic reconnection
                    NetworkRecoveryInterval = TimeSpan.FromSeconds(5),
                    RequestedHeartbeat = TimeSpan.FromSeconds(30)
                };

                _connection = await factory.CreateConnectionAsync(cancellationToken);
                _channel = await _connection.CreateChannelAsync(cancellationToken: cancellationToken);

                // Declare exchanges and queues
                // CRITICAL: Match Dapr pub/sub default settings exactly: durable=true, autoDelete=true
                // Dapr creates fanout exchanges with these settings, so we must match to avoid PRECONDITION_FAILED
                await _channel.ExchangeDeclareAsync(HEARTBEAT_EXCHANGE, ExchangeType.Fanout, durable: true, autoDelete: true, cancellationToken: cancellationToken);
                await _channel.QueueDeclareAsync(HEARTBEAT_QUEUE, durable: false, exclusive: false, autoDelete: true, cancellationToken: cancellationToken);
                // Fanout exchanges ignore routing keys - use empty string
                await _channel.QueueBindAsync(HEARTBEAT_QUEUE, HEARTBEAT_EXCHANGE, string.Empty, cancellationToken: cancellationToken);

                // RESTART and DEPLOYMENT exchanges are orchestrator-only (not Dapr pub/sub), can use autoDelete: false
                await _channel.ExchangeDeclareAsync(RESTART_EXCHANGE, ExchangeType.Topic, durable: true, cancellationToken: cancellationToken);
                await _channel.ExchangeDeclareAsync(DEPLOYMENT_EXCHANGE, ExchangeType.Fanout, durable: true, cancellationToken: cancellationToken);

                // MAPPINGS exchange is shared with Dapr pub/sub (bannou-service subscribes via Dapr)
                // MUST use autoDelete: true to match Dapr's default exchange settings
                await _channel.ExchangeDeclareAsync(MAPPINGS_EXCHANGE, ExchangeType.Fanout, durable: true, autoDelete: true, cancellationToken: cancellationToken);

                // Set up mappings queue for this orchestrator instance
                await _channel.QueueDeclareAsync(MAPPINGS_QUEUE, durable: false, exclusive: false, autoDelete: true, cancellationToken: cancellationToken);
                await _channel.QueueBindAsync(MAPPINGS_QUEUE, MAPPINGS_EXCHANGE, string.Empty, cancellationToken: cancellationToken);

                // Start consuming heartbeat events
                var heartbeatConsumer = new AsyncEventingBasicConsumer(_channel);
                heartbeatConsumer.ReceivedAsync += OnHeartbeatMessageReceived;
                await _channel.BasicConsumeAsync(HEARTBEAT_QUEUE, autoAck: true, consumer: heartbeatConsumer, cancellationToken: cancellationToken);

                // Start consuming service mapping events
                var mappingsConsumer = new AsyncEventingBasicConsumer(_channel);
                mappingsConsumer.ReceivedAsync += OnMappingMessageReceived;
                await _channel.BasicConsumeAsync(MAPPINGS_QUEUE, autoAck: true, consumer: mappingsConsumer, cancellationToken: cancellationToken);

                _logger.LogInformation("RabbitMQ connection established successfully (heartbeats + mappings)");

                return true;
            }
            catch (Exception ex) when (ex is RabbitMQ.Client.Exceptions.BrokerUnreachableException ||
                                        ex is System.Net.Sockets.SocketException)
            {
                _logger.LogWarning(
                    ex,
                    "RabbitMQ connection failed (attempt {Attempt}/{MaxAttempts}). Retrying in {DelayMs}ms...",
                    attempt, MAX_RETRY_ATTEMPTS, retryDelay);

                if (attempt < MAX_RETRY_ATTEMPTS)
                {
                    await Task.Delay(retryDelay, cancellationToken);
                    retryDelay = Math.Min(retryDelay * 2, MAX_RETRY_DELAY_MS);  // Exponential backoff
                }
            }
        }

        _logger.LogError(
            "Failed to establish RabbitMQ connection after {MaxAttempts} attempts",
            MAX_RETRY_ATTEMPTS);

        return false;
    }

    /// <summary>
    /// Handle incoming heartbeat messages from RabbitMQ.
    /// </summary>
    private Task OnHeartbeatMessageReceived(object sender, BasicDeliverEventArgs args)
    {
        try
        {
            var body = args.Body.ToArray();
            var message = Encoding.UTF8.GetString(body);

            var heartbeat = JsonSerializer.Deserialize<ServiceHeartbeatEvent>(message);
            if (heartbeat != null)
            {
                _logger.LogDebug(
                    "Received heartbeat: {ServiceId}:{AppId} - {Status}",
                    heartbeat.ServiceId, heartbeat.AppId, heartbeat.Status);

                HeartbeatReceived?.Invoke(heartbeat);
            }
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to deserialize heartbeat message");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing heartbeat message");
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Handle incoming service mapping messages from RabbitMQ.
    /// Supports both CloudEvents format (published by this orchestrator) and raw format.
    /// </summary>
    private Task OnMappingMessageReceived(object sender, BasicDeliverEventArgs args)
    {
        try
        {
            var body = args.Body.ToArray();
            var message = Encoding.UTF8.GetString(body);

            ServiceMappingEvent? mappingEvent = null;

            // Try to parse as CloudEvents format first (has "data" wrapper)
            try
            {
                using var doc = JsonDocument.Parse(message);
                if (doc.RootElement.TryGetProperty("data", out var dataElement))
                {
                    // CloudEvents format - extract the data payload
                    mappingEvent = dataElement.Deserialize<ServiceMappingEvent>();
                    _logger.LogDebug("Parsed CloudEvents format mapping message");
                }
                else
                {
                    // Raw format - deserialize directly
                    mappingEvent = JsonSerializer.Deserialize<ServiceMappingEvent>(message);
                    _logger.LogDebug("Parsed raw format mapping message");
                }
            }
            catch
            {
                // Fallback to raw format
                mappingEvent = JsonSerializer.Deserialize<ServiceMappingEvent>(message);
            }

            if (mappingEvent != null)
            {
                _logger.LogInformation(
                    "Received mapping event: {ServiceName} -> {AppId} ({Action})",
                    mappingEvent.ServiceName, mappingEvent.AppId, mappingEvent.Action);

                ServiceMappingReceived?.Invoke(mappingEvent);
            }
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to deserialize mapping message");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing mapping message");
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Publish service restart event to RabbitMQ.
    /// </summary>
    public async Task PublishServiceRestartEventAsync(ServiceRestartEvent restartEvent)
    {
        if (_channel == null)
        {
            _logger.LogWarning("RabbitMQ channel not initialized. Cannot publish restart event.");
            return;
        }

        try
        {
            var message = JsonSerializer.Serialize(restartEvent);
            var body = Encoding.UTF8.GetBytes(message);

            var routingKey = $"service.restart.{restartEvent.ServiceName}";

            await _channel.BasicPublishAsync(
                exchange: RESTART_EXCHANGE,
                routingKey: routingKey,
                body: body);

            _logger.LogInformation(
                "Published restart event for service: {ServiceName}",
                restartEvent.ServiceName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish restart event for {ServiceName}", restartEvent.ServiceName);
            throw;
        }
    }

    /// <summary>
    /// Publish service mapping event to RabbitMQ.
    /// Used when topology changes to notify all bannou instances of new service-to-app-id mappings.
    /// Uses fanout exchange so ALL bannou instances receive the mapping update.
    /// IMPORTANT: Wraps payload in CloudEvents format so Dapr pubsub can receive it.
    /// </summary>
    public async Task PublishServiceMappingEventAsync(ServiceMappingEvent mappingEvent)
    {
        if (_channel == null)
        {
            _logger.LogWarning("RabbitMQ channel not initialized. Cannot publish service mapping event.");
            return;
        }

        try
        {
            // Wrap in CloudEvents format for Dapr compatibility
            // Dapr expects CloudEvents when receiving pub/sub messages
            var cloudEvent = new
            {
                specversion = "1.0",
                type = "com.bannou.servicemapping",
                source = "orchestrator",
                id = mappingEvent.EventId,
                time = DateTime.UtcNow.ToString("o"),
                datacontenttype = "application/json",
                data = mappingEvent
            };

            var message = JsonSerializer.Serialize(cloudEvent);
            var body = Encoding.UTF8.GetBytes(message);

            // Fanout exchange - all consumers receive the message
            await _channel.BasicPublishAsync(
                exchange: MAPPINGS_EXCHANGE,
                routingKey: string.Empty,  // Fanout ignores routing key
                body: body);

            _logger.LogInformation(
                "Published service mapping event (CloudEvents): {ServiceName} -> {AppId} ({Action})",
                mappingEvent.ServiceName, mappingEvent.AppId, mappingEvent.Action);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to publish service mapping event for {ServiceName}",
                mappingEvent.ServiceName);
            throw;
        }
    }

    /// <summary>
    /// Publish deployment event to RabbitMQ.
    /// Used to broadcast deployment lifecycle events (started, completed, failed, topology-changed).
    /// Uses fanout exchange so ALL consumers (including test handlers) receive the event.
    /// </summary>
    public async Task PublishDeploymentEventAsync(DeploymentEvent deploymentEvent)
    {
        if (_channel == null)
        {
            _logger.LogWarning("RabbitMQ channel not initialized. Cannot publish deployment event.");
            return;
        }

        try
        {
            var message = JsonSerializer.Serialize(deploymentEvent);
            var body = Encoding.UTF8.GetBytes(message);

            // Fanout exchange - all consumers receive the message
            await _channel.BasicPublishAsync(
                exchange: DEPLOYMENT_EXCHANGE,
                routingKey: string.Empty,  // Fanout ignores routing key
                body: body);

            _logger.LogInformation(
                "Published deployment event: {DeploymentId} - {Action}",
                deploymentEvent.DeploymentId, deploymentEvent.Action);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to publish deployment event for {DeploymentId}",
                deploymentEvent.DeploymentId);
            throw;
        }
    }

    /// <summary>
    /// Check if RabbitMQ connection is healthy.
    /// </summary>
    public (bool IsHealthy, string? Message) CheckHealth()
    {
        if (_connection == null || !_connection.IsOpen)
        {
            return (false, "RabbitMQ connection not established");
        }

        if (_channel == null || !_channel.IsOpen)
        {
            return (false, "RabbitMQ channel not established");
        }

        return (true, "RabbitMQ connected");
    }

    /// <summary>
    /// Mask sensitive parts of connection string for logging.
    /// </summary>
    private string MaskConnectionString(string? connectionString)
    {
        if (string.IsNullOrEmpty(connectionString))
        {
            return "amqp://***:***@rabbitmq:5672";
        }

        // Basic masking - replace password in AMQP URI
        var uri = new Uri(connectionString);
        return $"amqp://***:***@{uri.Host}:{uri.Port}";
    }

    /// <summary>
    /// Synchronous dispose for DI container compatibility.
    /// </summary>
    public void Dispose()
    {
        if (_channel != null)
        {
            _channel.CloseAsync().GetAwaiter().GetResult();
            _channel.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        if (_connection != null)
        {
            _connection.CloseAsync().GetAwaiter().GetResult();
            _connection.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        _logger.LogDebug("RabbitMQ connection closed synchronously");
    }

    /// <summary>
    /// Dispose RabbitMQ resources asynchronously.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_channel != null)
        {
            await _channel.CloseAsync();
            await _channel.DisposeAsync();
        }

        if (_connection != null)
        {
            await _connection.CloseAsync();
            await _connection.DisposeAsync();
        }

        _logger.LogInformation("RabbitMQ connection closed");
    }
}

/// <summary>
/// Event published when a service is restarted.
/// </summary>
public class ServiceRestartEvent
{
    public required string EventId { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public required string ServiceName { get; set; }
    public string? Reason { get; set; }
    public bool Forced { get; set; }
    public Dictionary<string, string>? NewEnvironment { get; set; }
}
