#nullable enable

using BeyondImmersion.BannouService.Messaging;

namespace BeyondImmersion.BannouService.Messaging.Services;

/// <summary>
/// Interface for message retry buffering to enable testability.
/// Buffers failed message publishes and retries them when the connection is available.
/// </summary>
/// <remarks>
/// <para>
/// This interface abstracts the retry buffer for mocking in unit tests.
/// The default implementation (MessageRetryBuffer) crashes the node if the buffer
/// grows too large or messages are stuck too long - this interface allows testing
/// of consumers without triggering actual process termination.
/// </para>
/// </remarks>
public interface IRetryBuffer
{
    /// <summary>
    /// Gets the current number of messages in the buffer.
    /// </summary>
    int BufferCount { get; }

    /// <summary>
    /// Gets whether the retry buffer is enabled.
    /// </summary>
    bool IsEnabled { get; }

    /// <summary>
    /// Gets whether backpressure is currently active (buffer at or above threshold).
    /// </summary>
    /// <remarks>
    /// When backpressure is active, new messages will be rejected rather than buffered.
    /// Callers should check this before publishing to implement their own overflow strategy.
    /// </remarks>
    bool IsBackpressureActive { get; }

    /// <summary>
    /// Gets the backpressure threshold count (computed from config).
    /// </summary>
    int BackpressureThreshold { get; }

    /// <summary>
    /// Attempts to add a message to the retry buffer.
    /// </summary>
    /// <param name="topic">The topic/routing key</param>
    /// <param name="jsonPayload">The serialized JSON payload</param>
    /// <param name="options">Publish options (exchange, routing key, etc.)</param>
    /// <param name="messageId">The message ID to track</param>
    /// <returns>True if message was buffered, false if rejected due to backpressure or being disabled.</returns>
    /// <remarks>
    /// When backpressure is active (buffer at threshold), new messages are rejected.
    /// Callers should handle the false return by implementing their own overflow strategy
    /// (e.g., dropping, external queue, rate limiting).
    /// </remarks>
    bool TryEnqueueForRetry(
        string topic,
        byte[] jsonPayload,
        PublishOptions? options,
        Guid messageId);
}
