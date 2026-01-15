using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService.Messaging;
using Microsoft.Extensions.Logging;
using System.Reflection;
using System.Runtime.Serialization;

namespace BeyondImmersion.BannouService.ClientEvents;

/// <summary>
/// Message bus implementation of IClientEventPublisher.
/// Publishes client events to session-specific RabbitMQ queues via MassTransit.
/// </summary>
/// <remarks>
/// <para>
/// Uses a dedicated direct exchange ("bannou-client-events") with routing keys
/// to deliver events only to the target session's queue. The routing key is
/// the queue name (CONNECT_SESSION_{sessionId}).
/// </para>
/// <para>
/// This architecture ensures:
/// - No per-session exchange proliferation (single shared direct exchange)
/// - Efficient broker-level message filtering (only matching queues receive events)
/// - Clean separation from service events which use the "bannou" fanout exchange
/// </para>
/// <para>
/// The Connect service uses direct RabbitMQ to subscribe because MassTransit
/// doesn't support dynamic runtime queue creation. Session queues have 5-minute
/// TTL applied via RabbitMQ policy for automatic cleanup.
/// </para>
/// </remarks>
public class MessageBusClientEventPublisher : IClientEventPublisher
{
    private readonly IMessageBus _messageBus;
    private readonly ILogger<MessageBusClientEventPublisher> _logger;

    /// <summary>
    /// Prefix for session-specific queue names and routing keys.
    /// </summary>
    private const string SESSION_TOPIC_PREFIX = "CONNECT_SESSION_";

    /// <summary>
    /// The dedicated direct exchange for client events.
    /// Defined in provisioning/rabbitmq/definitions.json.
    /// </summary>
    private const string CLIENT_EVENTS_EXCHANGE = "bannou-client-events";

    /// <summary>
    /// Creates a new MessageBusClientEventPublisher.
    /// </summary>
    /// <param name="messageBus">Message bus for publishing events.</param>
    /// <param name="logger">Logger for event operations.</param>
    public MessageBusClientEventPublisher(IMessageBus messageBus, ILogger<MessageBusClientEventPublisher> logger)
    {
        _messageBus = messageBus ?? throw new ArgumentNullException(nameof(messageBus));
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

        // Validate event name against whitelist (handles NSwag enum shadowing)
        var eventName = GetEventName(eventData);
        if (!ClientEventWhitelist.IsValidEventName(eventName))
        {
            _logger.LogError(
                "Rejected client event with unknown event_name: {EventName}. " +
                "Add it to the appropriate *-client-events.yaml schema and regenerate.",
                eventName);
            throw new ArgumentException($"Unknown client event type: {eventName}", nameof(eventData));
        }

        // Queue/routing key format: CONNECT_SESSION_{sessionId}
        var routingKey = $"{SESSION_TOPIC_PREFIX}{sessionId}";

        try
        {
            // Publish to direct exchange with routing key matching the subscriber's binding key
            // The broker routes messages only to queues bound with matching routing key
            var options = new PublishOptions
            {
                Exchange = CLIENT_EVENTS_EXCHANGE,
                ExchangeType = PublishOptionsExchangeType.Direct,
                RoutingKey = routingKey
            };
            await _messageBus.TryPublishAsync(routingKey, eventData, options, cancellationToken: cancellationToken);

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

        // Validate event name once (handles NSwag enum shadowing)
        var eventName = GetEventName(eventData);
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
                var routingKey = $"{SESSION_TOPIC_PREFIX}{sessionId}";
                try
                {
                    // Publish to direct exchange with session-specific routing key
                    var options = new PublishOptions
                    {
                        Exchange = CLIENT_EVENTS_EXCHANGE,
                        ExchangeType = PublishOptionsExchangeType.Direct,
                        RoutingKey = routingKey
                    };
                    await _messageBus.TryPublishAsync(routingKey, eventData, options, cancellationToken: cancellationToken);
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

    /// <summary>
    /// Extracts the event name from a client event, handling NSwag-generated enum shadowing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// NSwag generates derived event classes that shadow the base Event_name (string) property
    /// with an enum property (e.g., CapabilityManifestEventEvent_name). When accessing Event_name
    /// via the BaseClientEvent constraint, we get the base string property which may be unset.
    /// </para>
    /// <para>
    /// This method uses reflection to find the actual Event_name property on the runtime type
    /// and extracts the correct string value, handling EnumMember attributes for enum values.
    /// </para>
    /// </remarks>
    /// <typeparam name="TEvent">The event type.</typeparam>
    /// <param name="eventData">The event instance.</param>
    /// <returns>The event name string, or null if not found.</returns>
    private static string? GetEventName<TEvent>(TEvent eventData) where TEvent : BaseClientEvent
    {
        // Get the actual runtime type (may be derived)
        var actualType = eventData.GetType();

        // Use DeclaredOnly to get only properties declared on the derived type (not inherited)
        // This avoids AmbiguousMatchException when derived class shadows Event_name with an enum type
        var eventNameProp = actualType.GetProperty(
            "Event_name",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);

        // If no property declared on derived type, fall back to base class property
        if (eventNameProp == null)
        {
            return eventData.EventName;
        }

        // Property is shadowed on derived type - get its value
        var value = eventNameProp.GetValue(eventData);
        if (value == null)
        {
            // Fall back to base property
            return eventData.EventName;
        }

        // If it's an enum, extract the string value from EnumMember attribute
        if (value.GetType().IsEnum)
        {
            var enumType = value.GetType();
            var memberName = value.ToString();
            if (memberName != null)
            {
                var memberInfo = enumType.GetMember(memberName).FirstOrDefault();
                var enumMemberAttr = memberInfo?.GetCustomAttribute<EnumMemberAttribute>();
                if (enumMemberAttr?.Value != null)
                {
                    return enumMemberAttr.Value;
                }
            }
            // Fall back to enum ToString() if no EnumMember attribute
            return memberName;
        }

        // Not an enum, just convert to string
        return value.ToString();
    }
}
