using Dapr.Client;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace BeyondImmersion.BannouService.Connect.ClientEvents;

/// <summary>
/// Manages event queuing for disconnected sessions within the reconnection window.
/// </summary>
/// <remarks>
/// <para>
/// When a client disconnects but the session is still valid (within the 5-minute
/// reconnection window), events are queued in Redis via Dapr state store.
/// </para>
/// <para>
/// When the client reconnects, queued events are delivered before normal
/// event flow resumes.
/// </para>
/// </remarks>
public class ClientEventQueueManager
{
    private readonly DaprClient _daprClient;
    private readonly ILogger<ClientEventQueueManager> _logger;

    /// <summary>
    /// State store name for Connect service session data.
    /// </summary>
    private const string STATE_STORE = "connect-statestore";

    /// <summary>
    /// Key prefix for event queues.
    /// </summary>
    private const string EVENT_QUEUE_PREFIX = "event-queue:";

    /// <summary>
    /// Maximum number of events to queue per session.
    /// </summary>
    private const int MAX_QUEUED_EVENTS = 100;

    /// <summary>
    /// Creates a new ClientEventQueueManager.
    /// </summary>
    /// <param name="daprClient">Dapr client for state management.</param>
    /// <param name="logger">Logger for queue operations.</param>
    public ClientEventQueueManager(DaprClient daprClient, ILogger<ClientEventQueueManager> logger)
    {
        _daprClient = daprClient ?? throw new ArgumentNullException(nameof(daprClient));
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
            var existingQueue = await _daprClient.GetStateAsync<List<QueuedEvent>>(
                STATE_STORE, key, cancellationToken: cancellationToken);

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
            await _daprClient.SaveStateAsync(
                STATE_STORE,
                key,
                queue,
                metadata: new Dictionary<string, string>
                {
                    // TTL of 5 minutes (matches reconnection window)
                    ["ttlInSeconds"] = "300"
                },
                cancellationToken: cancellationToken);

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

            // Get and delete the queue atomically
            var queue = await _daprClient.GetStateAsync<List<QueuedEvent>>(
                STATE_STORE, key, cancellationToken: cancellationToken);

            if (queue == null || queue.Count == 0)
            {
                return new List<byte[]>();
            }

            // Delete the queue
            await _daprClient.DeleteStateAsync(STATE_STORE, key, cancellationToken: cancellationToken);

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
            return new List<byte[]>();
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
            var queue = await _daprClient.GetStateAsync<List<QueuedEvent>>(
                STATE_STORE, key, cancellationToken: cancellationToken);

            return queue?.Count ?? 0;
        }
        catch
        {
            return 0;
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
            await _daprClient.DeleteStateAsync(STATE_STORE, key, cancellationToken: cancellationToken);

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
    private class QueuedEvent
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
