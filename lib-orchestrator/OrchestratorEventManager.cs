using System.Text;
using System.Text.Json;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Microsoft.Extensions.Logging;
using BeyondImmersion.BannouService.Orchestrator;

namespace LibOrchestrator;

/// <summary>
/// Manages direct RabbitMQ connections for orchestrator service.
/// CRITICAL: Uses direct RabbitMQ.Client (NOT Dapr) to avoid chicken-and-egg dependency.
/// Dapr sidecar depends on RabbitMQ being available, so orchestrator needs direct access.
/// </summary>
public class OrchestratorEventManager : IAsyncDisposable
{
    private readonly ILogger<OrchestratorEventManager> _logger;
    private readonly OrchestratorServiceConfiguration _configuration;
    private IConnection? _connection;
    private IChannel? _channel;

    private const int MAX_RETRY_ATTEMPTS = 10;
    private const int INITIAL_RETRY_DELAY_MS = 1000;
    private const int MAX_RETRY_DELAY_MS = 60000;

    private const string HEARTBEAT_EXCHANGE = "bannou-service-heartbeats";
    private const string HEARTBEAT_QUEUE = "orchestrator-heartbeat-queue";
    private const string RESTART_EXCHANGE = "bannou-service-restarts";

    public event Action<ServiceHeartbeatEvent>? HeartbeatReceived;

    public OrchestratorEventManager(
        ILogger<OrchestratorEventManager> logger,
        OrchestratorServiceConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
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
                    attempt, MAX_RETRY_ATTEMPTS, MaskConnectionString(_configuration.RabbitMqConnectionString));

                var factory = new ConnectionFactory
                {
                    Uri = new Uri(_configuration.RabbitMqConnectionString ?? "amqp://guest:guest@rabbitmq:5672"),
                    AutomaticRecoveryEnabled = true,  // ✅ Automatic reconnection
                    NetworkRecoveryInterval = TimeSpan.FromSeconds(5),
                    RequestedHeartbeat = TimeSpan.FromSeconds(30)
                };

                _connection = await factory.CreateConnectionAsync(cancellationToken);
                _channel = await _connection.CreateChannelAsync(cancellationToken: cancellationToken);

                // Declare exchanges and queues
                await _channel.ExchangeDeclareAsync(HEARTBEAT_EXCHANGE, ExchangeType.Topic, durable: true, cancellationToken: cancellationToken);
                await _channel.QueueDeclareAsync(HEARTBEAT_QUEUE, durable: false, exclusive: false, autoDelete: true, cancellationToken: cancellationToken);
                await _channel.QueueBindAsync(HEARTBEAT_QUEUE, HEARTBEAT_EXCHANGE, "service.heartbeat.#", cancellationToken: cancellationToken);

                await _channel.ExchangeDeclareAsync(RESTART_EXCHANGE, ExchangeType.Topic, durable: true, cancellationToken: cancellationToken);

                // Start consuming heartbeat events
                var consumer = new AsyncEventingBasicConsumer(_channel);
                consumer.ReceivedAsync += OnHeartbeatMessageReceived;
                await _channel.BasicConsumeAsync(HEARTBEAT_QUEUE, autoAck: true, consumer: consumer, cancellationToken: cancellationToken);

                _logger.LogInformation("RabbitMQ connection established successfully");

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
