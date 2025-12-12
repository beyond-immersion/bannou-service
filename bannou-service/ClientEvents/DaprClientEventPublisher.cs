using Dapr.Client;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.ClientEvents;

/// <summary>
/// Dapr-based implementation of IClientEventPublisher.
/// Publishes client events to session-specific RabbitMQ topics.
/// </summary>
/// <remarks>
/// <para>
/// Uses Dapr pub/sub to publish to dynamic topics (CONNECT_SESSION_{sessionId}).
/// Dapr's RabbitMQ component creates fanout exchanges for each topic automatically.
/// </para>
/// <para>
/// The Connect service uses direct RabbitMQ to subscribe to these exchanges
/// because Dapr doesn't support dynamic runtime subscriptions.
/// </para>
/// </remarks>
public class DaprClientEventPublisher : IClientEventPublisher
{
    private readonly DaprClient _daprClient;
    private readonly ILogger<DaprClientEventPublisher> _logger;

    /// <summary>
    /// Standard pub/sub component name for Bannou services.
    /// </summary>
    private const string PUBSUB_NAME = "bannou-pubsub";

    /// <summary>
    /// Prefix for session-specific topics.
    /// </summary>
    private const string SESSION_TOPIC_PREFIX = "CONNECT_SESSION_";

    /// <summary>
    /// Creates a new DaprClientEventPublisher.
    /// </summary>
    /// <param name="daprClient">Dapr client for publishing events.</param>
    /// <param name="logger">Logger for event operations.</param>
    public DaprClientEventPublisher(DaprClient daprClient, ILogger<DaprClientEventPublisher> logger)
    {
        _daprClient = daprClient ?? throw new ArgumentNullException(nameof(daprClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<bool> PublishToSessionAsync<TEvent>(
        string sessionId,
        TEvent eventData,
        CancellationToken cancellationToken = default)
        where TEvent : BaseClientEvent
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            _logger.LogWarning("Cannot publish client event: session ID is null or empty");
            return false;
        }

        // Validate event name against whitelist
        var eventName = eventData.Event_name;
        if (!ClientEventWhitelist.IsValidEventName(eventName))
        {
            _logger.LogError(
                "Rejected client event with unknown event_name: {EventName}. " +
                "Add it to the appropriate *-client-events.yaml schema and regenerate.",
                eventName);
            throw new ArgumentException($"Unknown client event type: {eventName}", nameof(eventData));
        }

        var topic = $"{SESSION_TOPIC_PREFIX}{sessionId}";

        try
        {
            await _daprClient.PublishEventAsync(PUBSUB_NAME, topic, eventData, cancellationToken);

            _logger.LogDebug(
                "Published client event {EventName} to session {SessionId}",
                eventName,
                sessionId);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to publish client event {EventName} to session {SessionId}",
                eventName,
                sessionId);

            return false;
        }
    }

    /// <inheritdoc />
    public async Task<int> PublishToSessionsAsync<TEvent>(
        IEnumerable<string> sessionIds,
        TEvent eventData,
        CancellationToken cancellationToken = default)
        where TEvent : BaseClientEvent
    {
        var sessions = sessionIds?.ToList() ?? new List<string>();
        if (sessions.Count == 0)
        {
            return 0;
        }

        // Validate event name once (same event going to multiple sessions)
        var eventName = eventData.Event_name;
        if (!ClientEventWhitelist.IsValidEventName(eventName))
        {
            _logger.LogError(
                "Rejected client event with unknown event_name: {EventName}. " +
                "Add it to the appropriate *-client-events.yaml schema and regenerate.",
                eventName);
            throw new ArgumentException($"Unknown client event type: {eventName}", nameof(eventData));
        }

        var successCount = 0;

        // Publish to each session in parallel for better performance
        var publishTasks = sessions
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(async sessionId =>
            {
                var topic = $"{SESSION_TOPIC_PREFIX}{sessionId}";
                try
                {
                    await _daprClient.PublishEventAsync(PUBSUB_NAME, topic, eventData, cancellationToken);
                    Interlocked.Increment(ref successCount);

                    _logger.LogDebug(
                        "Published client event {EventName} to session {SessionId}",
                        eventName,
                        sessionId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "Failed to publish client event {EventName} to session {SessionId}",
                        eventName,
                        sessionId);
                }
            });

        await Task.WhenAll(publishTasks);

        _logger.LogInformation(
            "Published client event {EventName} to {SuccessCount}/{TotalCount} sessions",
            eventName,
            successCount,
            sessions.Count);

        return successCount;
    }

    /// <inheritdoc />
    public bool IsValidEventName(string? eventName)
    {
        return ClientEventWhitelist.IsValidEventName(eventName);
    }
}
