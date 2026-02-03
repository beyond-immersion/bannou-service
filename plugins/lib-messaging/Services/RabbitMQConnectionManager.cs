#nullable enable

using BeyondImmersion.Bannou.Core;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using System.Collections.Concurrent;

namespace BeyondImmersion.BannouService.Messaging.Services;

/// <summary>
/// Manages RabbitMQ connections and channels for the messaging system.
/// Provides connection pooling and retry logic for reliable messaging.
/// </summary>
/// <remarks>
/// <para>
/// This class maintains a single connection to RabbitMQ (connections are expensive)
/// and a pool of channels for concurrent operations (channels are cheap).
/// </para>
/// <para>
/// Follows the pattern established in lib-connect/ClientEvents/ClientEventRabbitMQSubscriber.cs
/// for consistent RabbitMQ usage across the codebase.
/// </para>
/// </remarks>
public sealed class RabbitMQConnectionManager : IAsyncDisposable
{
    private readonly ILogger<RabbitMQConnectionManager> _logger;
    private readonly MessagingServiceConfiguration _configuration;

    private IConnection? _connection;
    private readonly ConcurrentBag<IChannel> _channelPool = new();
    private readonly SemaphoreSlim _connectionLock = new(1, 1);
    private bool _disposed;

    /// <summary>
    /// Creates a new RabbitMQConnectionManager.
    /// </summary>
    public RabbitMQConnectionManager(
        ILogger<RabbitMQConnectionManager> logger,
        MessagingServiceConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
    }

    /// <summary>
    /// Gets the default exchange name from configuration.
    /// </summary>
    public string DefaultExchange => _configuration.DefaultExchange;

    /// <summary>
    /// Gets the default prefetch count from configuration.
    /// </summary>
    public int DefaultPrefetchCount => _configuration.DefaultPrefetchCount;

    /// <summary>
    /// Initialize the RabbitMQ connection with retry logic.
    /// </summary>
    public async Task<bool> InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_connection?.IsOpen == true)
        {
            return true;
        }

        await _connectionLock.WaitAsync(cancellationToken);
        try
        {
            // Double-check after acquiring lock
            if (_connection?.IsOpen == true)
            {
                return true;
            }

            var maxRetryAttempts = _configuration.ConnectionRetryCount;
            var retryDelay = _configuration.ConnectionRetryDelayMs;

            for (int attempt = 1; attempt <= maxRetryAttempts; attempt++)
            {
                try
                {
                    _logger.LogInformation(
                        "Attempting RabbitMQ connection (attempt {Attempt}/{MaxAttempts})",
                        attempt, maxRetryAttempts);

                    var connectionString = BuildConnectionString();
                    var factory = new ConnectionFactory
                    {
                        Uri = new Uri(connectionString),
                        AutomaticRecoveryEnabled = true,
                        NetworkRecoveryInterval = TimeSpan.FromSeconds(_configuration.RabbitMQNetworkRecoveryIntervalSeconds)
                    };

                    _connection = await factory.CreateConnectionAsync(cancellationToken);

                    _logger.LogInformation(
                        "RabbitMQ connection established to {Host}:{Port} (publisher confirms: {PublisherConfirms})",
                        _configuration.RabbitMQHost,
                        _configuration.RabbitMQPort,
                        _configuration.EnablePublisherConfirms);

                    return true;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "RabbitMQ connection failed (attempt {Attempt}/{MaxAttempts}). Retrying in {DelayMs}ms...",
                        attempt, maxRetryAttempts, retryDelay);

                    if (attempt < maxRetryAttempts)
                    {
                        await Task.Delay(retryDelay, cancellationToken);
                        retryDelay = Math.Min(retryDelay * 2, _configuration.ConnectionMaxBackoffMs);
                    }
                }
            }

            _logger.LogError("Failed to connect to RabbitMQ after {MaxAttempts} attempts", maxRetryAttempts);
            return false;
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    /// <summary>
    /// Gets a channel from the pool or creates a new one.
    /// </summary>
    public async Task<IChannel> GetChannelAsync(CancellationToken cancellationToken = default)
    {
        if (_connection == null || !_connection.IsOpen)
        {
            if (!await InitializeAsync(cancellationToken))
            {
                throw new InvalidOperationException("Failed to initialize RabbitMQ connection");
            }
        }

        // Connection must be set after successful initialization
        if (_connection == null)
        {
            throw new InvalidOperationException("RabbitMQ connection is null after initialization");
        }

        // Try to get from pool
        while (_channelPool.TryTake(out var channel))
        {
            if (channel.IsOpen)
            {
                return channel; // Ownership transferred to caller
            }
            // Channel was closed, close it properly before trying next
            try
            {
                await channel.CloseAsync(cancellationToken);
            }
            catch
            {
                // Ignore errors during close of already-closed channel
            }
        }

        // No usable channel in pool, create new one with publisher confirms if configured
        var channelOptions = new CreateChannelOptions(
            publisherConfirmationsEnabled: _configuration.EnablePublisherConfirms,
            publisherConfirmationTrackingEnabled: false);

        return await _connection.CreateChannelAsync(channelOptions, cancellationToken);
    }

    /// <summary>
    /// Returns a channel to the pool for reuse.
    /// </summary>
    public void ReturnChannel(IChannel channel)
    {
        if (channel.IsOpen && _channelPool.Count < _configuration.ChannelPoolSize)
        {
            _channelPool.Add(channel);
        }
        else
        {
            // Pool is full or channel is closed, dispose it
            try
            {
                channel.CloseAsync().GetAwaiter().GetResult();
            }
            catch
            {
                // Ignore errors during close
            }
        }
    }

    /// <summary>
    /// Creates a dedicated channel for a consumer (not pooled).
    /// Consumer channels should be disposed when the consumer is done.
    /// </summary>
    public async Task<IChannel> CreateConsumerChannelAsync(CancellationToken cancellationToken = default)
    {
        if (_connection == null || !_connection.IsOpen)
        {
            if (!await InitializeAsync(cancellationToken))
            {
                throw new InvalidOperationException("Failed to initialize RabbitMQ connection");
            }
        }

        // Connection must be set after successful initialization
        if (_connection == null)
        {
            throw new InvalidOperationException("RabbitMQ connection is null after initialization");
        }

        var channel = await _connection.CreateChannelAsync(cancellationToken: cancellationToken);

        // Set QoS for consumer channels
        await channel.BasicQosAsync(
            prefetchSize: 0,
            prefetchCount: (ushort)DefaultPrefetchCount,
            global: false,
            cancellationToken: cancellationToken);

        return channel;
    }

    /// <summary>
    /// Builds the RabbitMQ connection string from configuration.
    /// </summary>
    private string BuildConnectionString()
    {
        var host = _configuration.RabbitMQHost;
        var port = _configuration.RabbitMQPort;
        var username = _configuration.RabbitMQUsername;
        var password = _configuration.RabbitMQPassword;
        var vhost = _configuration.RabbitMQVirtualHost;

        // URL encode the vhost if it's not the default
        var encodedVhost = vhost == "/" ? "" : Uri.EscapeDataString(vhost);

        return $"amqp://{username}:{password}@{host}:{port}/{encodedVhost}";
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        // Close all pooled channels
        while (_channelPool.TryTake(out var channel))
        {
            try
            {
                await channel.CloseAsync();
            }
            catch
            {
                // Ignore errors during shutdown
            }
        }

        // Close connection
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

        _connectionLock.Dispose();
        _logger.LogInformation("RabbitMQConnectionManager disposed");
    }
}
