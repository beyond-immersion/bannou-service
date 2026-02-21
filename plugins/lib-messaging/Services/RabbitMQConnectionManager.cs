#nullable enable

using BeyondImmersion.Bannou.Core;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using System.Collections.Concurrent;
using System.Diagnostics;

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
/// <para>
/// Implements <see cref="IChannelManager"/> to allow consumers (MessageRetryBuffer,
/// RabbitMQMessageBus, RabbitMQMessageSubscriber) to be tested without real RabbitMQ.
/// </para>
/// </remarks>
public sealed class RabbitMQConnectionManager : IChannelManager
{
    private readonly ILogger<RabbitMQConnectionManager> _logger;
    private readonly MessagingServiceConfiguration _configuration;

    private IConnection? _connection;
    private readonly ConcurrentBag<IChannel> _channelPool = new();
    private readonly SemaphoreSlim _connectionLock = new(1, 1);
    private SemaphoreSlim? _channelCreationSemaphore;
    private int _totalActiveChannels;
    private int _pooledChannelCount;
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
        _channelCreationSemaphore = new SemaphoreSlim(
            configuration.MaxConcurrentChannelCreation,
            configuration.MaxConcurrentChannelCreation);
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
    /// Gets the current number of active channels (pooled + in-use + consumer channels).
    /// </summary>
    public int TotalActiveChannels => Volatile.Read(ref _totalActiveChannels);

    /// <summary>
    /// Gets the current number of channels in the pool.
    /// </summary>
    public int PooledChannelCount => Volatile.Read(ref _pooledChannelCount);

    /// <summary>
    /// Gets the maximum allowed total channels from configuration.
    /// </summary>
    public int MaxTotalChannels => _configuration.MaxTotalChannels;

    /// <summary>
    /// Initialize the RabbitMQ connection with retry logic.
    /// </summary>
    /// <remarks>
    /// Always takes the lock to avoid TOCTOU race conditions.
    /// The performance cost is negligible since initialization is rare.
    /// </remarks>
    public async Task<bool> InitializeAsync(CancellationToken cancellationToken = default)
    {
        await _connectionLock.WaitAsync(cancellationToken);
        try
        {
            // Check connection state under lock
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
            _logger.LogDebug("DIAG: GetChannelAsync - connection not open, initializing");
            var initSw = Stopwatch.StartNew();
            if (!await InitializeAsync(cancellationToken))
            {
                throw new InvalidOperationException("Failed to initialize RabbitMQ connection");
            }
            initSw.Stop();
            if (initSw.ElapsedMilliseconds > 100)
            {
                _logger.LogWarning(
                    "DIAG: InitializeAsync took {ElapsedMs}ms",
                    initSw.ElapsedMilliseconds);
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
            Interlocked.Decrement(ref _pooledChannelCount);
            if (channel.IsOpen)
            {
                _logger.LogDebug(
                    "DIAG: GetChannelAsync - got channel from pool (remaining pool: {PoolSize}, total active: {TotalActive})",
                    Volatile.Read(ref _pooledChannelCount),
                    Volatile.Read(ref _totalActiveChannels));
                return channel; // Ownership transferred to caller
            }
            // Channel was closed, close it properly before trying next
            Interlocked.Decrement(ref _totalActiveChannels);
            try
            {
                await channel.CloseAsync(cancellationToken);
            }
            catch
            {
                // Ignore errors during close of already-closed channel
            }
        }

        // No usable channel in pool - check if we can create more
        var currentTotal = Volatile.Read(ref _totalActiveChannels);
        _logger.LogDebug(
            "DIAG: GetChannelAsync - pool empty, must create channel (total active: {TotalActive}, max: {MaxTotal})",
            currentTotal, _configuration.MaxTotalChannels);

        if (currentTotal >= _configuration.MaxTotalChannels)
        {
            throw new InvalidOperationException(
                $"Maximum channel limit reached ({_configuration.MaxTotalChannels}). " +
                "This indicates extremely high concurrent publish load. Consider increasing " +
                "MaxTotalChannels or reducing publish concurrency.");
        }

        // Use semaphore to limit concurrent channel creation (backpressure)
        var semaphore = _channelCreationSemaphore
            ?? throw new InvalidOperationException("Channel creation semaphore not initialized");

        var semaphoreSw = Stopwatch.StartNew();
        await semaphore.WaitAsync(cancellationToken);
        semaphoreSw.Stop();
        if (semaphoreSw.ElapsedMilliseconds > 100)
        {
            _logger.LogWarning(
                "DIAG: Channel creation semaphore wait took {ElapsedMs}ms (total active: {TotalActive})",
                semaphoreSw.ElapsedMilliseconds, Volatile.Read(ref _totalActiveChannels));
        }

        try
        {
            // Double-check limit after acquiring semaphore (another thread may have created channels)
            currentTotal = Volatile.Read(ref _totalActiveChannels);
            if (currentTotal >= _configuration.MaxTotalChannels)
            {
                throw new InvalidOperationException(
                    $"Maximum channel limit reached ({_configuration.MaxTotalChannels}).");
            }

            // Create new channel with publisher confirms if configured
            // With tracking enabled, BasicPublishAsync awaits broker confirmation (RabbitMQ.Client 7.x pattern)
            var channelOptions = new CreateChannelOptions(
                publisherConfirmationsEnabled: _configuration.EnablePublisherConfirms,
                publisherConfirmationTrackingEnabled: _configuration.EnablePublisherConfirms);

            var createSw = Stopwatch.StartNew();
            var newChannel = await _connection.CreateChannelAsync(channelOptions, cancellationToken);
            createSw.Stop();
            Interlocked.Increment(ref _totalActiveChannels);

            if (createSw.ElapsedMilliseconds > 100)
            {
                _logger.LogWarning(
                    "DIAG: CreateChannelAsync took {ElapsedMs}ms (total active: {TotalActive}, pool size: {PoolSize})",
                    createSw.ElapsedMilliseconds,
                    Volatile.Read(ref _totalActiveChannels),
                    Volatile.Read(ref _pooledChannelCount));
            }
            else
            {
                _logger.LogDebug(
                    "Created new channel in {ElapsedMs}ms (total active: {TotalActive}, pool size: {PoolSize})",
                    createSw.ElapsedMilliseconds,
                    Volatile.Read(ref _totalActiveChannels),
                    Volatile.Read(ref _pooledChannelCount));
            }

            return newChannel;
        }
        finally
        {
            semaphore.Release();
        }
    }

    /// <summary>
    /// Returns a channel to the pool for reuse.
    /// </summary>
    /// <remarks>
    /// Uses compare-exchange pattern to safely limit pool size without races.
    /// Channels that can't be pooled are closed asynchronously.
    /// </remarks>
    public async ValueTask ReturnChannelAsync(IChannel channel)
    {
        if (!channel.IsOpen)
        {
            // Channel is closed, just decrement the counter
            Interlocked.Decrement(ref _totalActiveChannels);
            return;
        }

        // Use compare-exchange to atomically check and increment pool count
        while (true)
        {
            var currentPooled = Volatile.Read(ref _pooledChannelCount);
            if (currentPooled >= _configuration.ChannelPoolSize)
            {
                // Pool is full, close this channel
                Interlocked.Decrement(ref _totalActiveChannels);
                try
                {
                    await channel.CloseAsync();
                }
                catch
                {
                    // Ignore errors during close
                }
                return;
            }

            // Try to atomically increment pool count
            if (Interlocked.CompareExchange(ref _pooledChannelCount, currentPooled + 1, currentPooled) == currentPooled)
            {
                // Successfully reserved a spot in the pool
                _channelPool.Add(channel);
                return;
            }
            // Another thread beat us, retry
        }
    }

    /// <summary>
    /// Creates a dedicated channel for a consumer (not pooled).
    /// Consumer channels should be disposed when the consumer is done.
    /// </summary>
    /// <remarks>
    /// Consumer channels are tracked in the total channel count but not pooled.
    /// Callers are responsible for calling <see cref="TrackChannelClosed"/> when done.
    /// </remarks>
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

        // Check total channel limit
        var currentTotal = Volatile.Read(ref _totalActiveChannels);
        if (currentTotal >= _configuration.MaxTotalChannels)
        {
            throw new InvalidOperationException(
                $"Maximum channel limit reached ({_configuration.MaxTotalChannels}).");
        }

        var channel = await _connection.CreateChannelAsync(cancellationToken: cancellationToken);
        Interlocked.Increment(ref _totalActiveChannels);

        // Set QoS for consumer channels
        await channel.BasicQosAsync(
            prefetchSize: 0,
            prefetchCount: (ushort)DefaultPrefetchCount,
            global: false,
            cancellationToken: cancellationToken);

        return channel;
    }

    /// <summary>
    /// Tracks that a consumer channel has been closed.
    /// Call this when disposing a consumer channel to maintain accurate counts.
    /// </summary>
    public void TrackChannelClosed()
    {
        Interlocked.Decrement(ref _totalActiveChannels);
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
            Interlocked.Decrement(ref _pooledChannelCount);
            Interlocked.Decrement(ref _totalActiveChannels);
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
        _channelCreationSemaphore?.Dispose();

        _logger.LogInformation(
            "RabbitMQConnectionManager disposed (final channel count: {TotalActive})",
            Volatile.Read(ref _totalActiveChannels));
    }
}
