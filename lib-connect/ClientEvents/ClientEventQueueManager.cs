using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Connect.ClientEvents;

/// <summary>
/// Manages event queuing for disconnected sessions within the reconnection window.
/// </summary>
/// <remarks>
/// <para>
/// <strong>⚠️ OBSOLETE - CLEANUP CANDIDATE ⚠️</strong>
/// </para>
/// <para>
/// This Redis-based event queue is obsolete. Event buffering during disconnection is now
/// handled natively by RabbitMQ via ClientEventRabbitMQSubscriber's queue-based consumption.
/// </para>
/// <para>
/// The new architecture:
/// <list type="bullet">
/// <item>Queue name: CONNECT_SESSION_{sessionId} - persists during disconnect</item>
/// <item>On disconnect: Consumer cancelled, queue buffers messages automatically</item>
/// <item>On reconnect: Re-subscribe to same queue, RabbitMQ delivers pending messages</item>
/// <item>Cleanup: Queue auto-expires via RabbitMQ policy (x-expires: 5 min) after inactivity</item>
/// </list>
/// </para>
/// <para>
/// This class is retained temporarily for:
/// <list type="bullet">
/// <item>Delivering any events queued in Redis before the architecture change</item>
/// <item>Potential fallback if RabbitMQ buffering has issues (not currently used)</item>
/// </list>
/// </para>
/// <para>
/// TODO: Remove this class and all references in a future cleanup once RabbitMQ-based
/// buffering is confirmed stable in production.
/// </para>
/// </remarks>
[Obsolete("Redis event queuing replaced by RabbitMQ queue-based buffering. See ClientEventRabbitMQSubscriber.")]
public class ClientEventQueueManager
{
    private readonly IStateStore<List<QueuedEvent>> _stateStore;
    private readonly ILogger<ClientEventQueueManager> _logger;

    /// <summary>
    /// Key prefix for event queues.
    /// </summary>
    private const string EVENT_QUEUE_PREFIX = "event-queue:";

    /// <summary>
    /// Maximum number of events to queue per session.
    /// </summary>
    private const int MAX_QUEUED_EVENTS = 100;

    /// <summary>
    /// TTL for queued events (matches reconnection window).
    /// </summary>
    private static readonly TimeSpan EVENT_QUEUE_TTL = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Creates a new ClientEventQueueManager.
    /// </summary>
    /// <param name="stateStoreFactory">State store factory for state management.</param>
    /// <param name="logger">Logger for queue operations.</param>
    public ClientEventQueueManager(IStateStoreFactory stateStoreFactory, ILogger<ClientEventQueueManager> logger)
    {
        ArgumentNullException.ThrowIfNull(stateStoreFactory);
        _stateStore = stateStoreFactory.GetStore<List<QueuedEvent>>("connect-statestore");
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Queue an event for a disconnected session.
    /// </summary>
    /// <param name="sessionId">The session ID to queue the event for.</param>
    /// <param name="eventPayload">The event payload (JSON bytes).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the event was queued successfully.</returns>
    public async Task<bool> QueueEventAsync(
        string sessionId,
        byte[] eventPayload,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return false;
        }

        try
        {
            var key = $"{EVENT_QUEUE_PREFIX}{sessionId}";

            // Get existing queue
            var existingQueue = await _stateStore.GetAsync(key, cancellationToken);

            var queue = existingQueue ?? new List<QueuedEvent>();

            // Check queue size limit
            if (queue.Count >= MAX_QUEUED_EVENTS)
            {
                _logger.LogWarning(
                    "Event queue full for session {SessionId} ({Count} events), dropping oldest",
                    sessionId, queue.Count);

                // Remove oldest event to make room
                queue.RemoveAt(0);
            }

            // Add new event
            queue.Add(new QueuedEvent
            {
                Payload = Convert.ToBase64String(eventPayload),
                QueuedAt = DateTimeOffset.UtcNow
            });

            // Save queue (with TTL matching reconnection window)
            await _stateStore.SaveAsync(key, queue, new StateOptions { Ttl = (int)EVENT_QUEUE_TTL.TotalSeconds }, cancellationToken);

            _logger.LogDebug(
                "Queued event for disconnected session {SessionId} (queue size: {QueueSize})",
                sessionId, queue.Count);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to queue event for session {SessionId}",
                sessionId);
            return false;
        }
    }

    /// <summary>
    /// Dequeue all events for a session (typically on reconnection).
    /// </summary>
    /// <param name="sessionId">The session ID to dequeue events for.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of queued event payloads, in order they were queued.</returns>
    public async Task<List<byte[]>> DequeueEventsAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return new List<byte[]>();
        }

        try
        {
            var key = $"{EVENT_QUEUE_PREFIX}{sessionId}";

            // Get and delete the queue
            var queue = await _stateStore.GetAsync(key, cancellationToken);

            if (queue == null || queue.Count == 0)
            {
                return new List<byte[]>();
            }

            // Delete the queue
            await _stateStore.DeleteAsync(key, cancellationToken);

            _logger.LogInformation(
                "Dequeued {Count} events for reconnecting session {SessionId}",
                queue.Count, sessionId);

            // Convert payloads back to bytes
            return queue
                .OrderBy(e => e.QueuedAt)
                .Select(e => Convert.FromBase64String(e.Payload))
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to dequeue events for session {SessionId}",
                sessionId);
            throw; // Don't mask Redis failures - events could be lost
        }
    }

    /// <summary>
    /// Get the number of queued events for a session.
    /// </summary>
    /// <param name="sessionId">The session ID to check.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Number of queued events, or 0 if none.</returns>
    public async Task<int> GetQueuedEventCountAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return 0;
        }

        try
        {
            var key = $"{EVENT_QUEUE_PREFIX}{sessionId}";
            var queue = await _stateStore.GetAsync(key, cancellationToken);

            return queue?.Count ?? 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get queued event count for session {SessionId}", sessionId);
            throw; // Don't mask Redis failures - caller needs to know
        }
    }

    /// <summary>
    /// Clear the event queue for a session (e.g., on session invalidation).
    /// </summary>
    /// <param name="sessionId">The session ID to clear.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task ClearQueueAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return;
        }

        try
        {
            var key = $"{EVENT_QUEUE_PREFIX}{sessionId}";
            await _stateStore.DeleteAsync(key, cancellationToken);

            _logger.LogDebug("Cleared event queue for session {SessionId}", sessionId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to clear event queue for session {SessionId}", sessionId);
        }
    }

    /// <summary>
    /// Internal model for queued events.
    /// </summary>
    public class QueuedEvent
    {
        /// <summary>
        /// Base64-encoded event payload.
        /// </summary>
        public string Payload { get; set; } = string.Empty;

        /// <summary>
        /// When the event was queued.
        /// </summary>
        public DateTimeOffset QueuedAt { get; set; }
    }
}
