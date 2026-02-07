#nullable enable

using RabbitMQ.Client;

namespace BeyondImmersion.BannouService.Messaging.Services;

/// <summary>
/// Abstraction for RabbitMQ channel management to enable testability.
/// </summary>
/// <remarks>
/// <para>
/// This interface allows consumers of channel management (MessageRetryBuffer,
/// RabbitMQMessageBus, RabbitMQMessageSubscriber) to be tested without needing
/// a real RabbitMQ connection.
/// </para>
/// <para>
/// Production code uses <see cref="RabbitMQConnectionManager"/> which manages
/// a single RabbitMQ connection with a pool of channels.
/// </para>
/// </remarks>
public interface IChannelManager : IAsyncDisposable
{
    /// <summary>
    /// Gets the default exchange name from configuration.
    /// </summary>
    string DefaultExchange { get; }

    /// <summary>
    /// Gets the default prefetch count for consumers from configuration.
    /// </summary>
    int DefaultPrefetchCount { get; }

    /// <summary>
    /// Gets the current number of active channels (pooled + in-use + consumer channels).
    /// </summary>
    int TotalActiveChannels { get; }

    /// <summary>
    /// Gets the current number of channels in the pool.
    /// </summary>
    int PooledChannelCount { get; }

    /// <summary>
    /// Gets the maximum allowed total channels from configuration.
    /// </summary>
    int MaxTotalChannels { get; }

    /// <summary>
    /// Initialize the connection with retry logic.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if connection established, false if all retries exhausted.</returns>
    Task<bool> InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a channel from the pool or creates a new one.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A channel for publishing operations.</returns>
    /// <exception cref="InvalidOperationException">Thrown when connection cannot be established or channel limit reached.</exception>
    /// <remarks>
    /// Callers must return the channel via <see cref="ReturnChannelAsync"/> when done.
    /// </remarks>
    Task<IChannel> GetChannelAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a channel to the pool for reuse.
    /// </summary>
    /// <param name="channel">The channel to return.</param>
    /// <remarks>
    /// If the pool is full or the channel is closed, the channel will be disposed.
    /// </remarks>
    ValueTask ReturnChannelAsync(IChannel channel);

    /// <summary>
    /// Creates a dedicated channel for a consumer (not pooled).
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A dedicated channel with QoS configured for consuming.</returns>
    /// <exception cref="InvalidOperationException">Thrown when connection cannot be established or channel limit reached.</exception>
    /// <remarks>
    /// Consumer channels are tracked in the total channel count but not pooled.
    /// Callers must call <see cref="TrackChannelClosed"/> when disposing the channel.
    /// </remarks>
    Task<IChannel> CreateConsumerChannelAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Tracks that a consumer channel has been closed.
    /// </summary>
    /// <remarks>
    /// Call this when disposing a consumer channel to maintain accurate counts.
    /// </remarks>
    void TrackChannelClosed();
}
