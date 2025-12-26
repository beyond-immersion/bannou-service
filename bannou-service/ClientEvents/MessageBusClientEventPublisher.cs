using BeyondImmersion.BannouService.Messaging;
using Microsoft.Extensions.Logging;
using System.Reflection;
using System.Runtime.Serialization;

namespace BeyondImmersion.BannouService.ClientEvents;

/// <summary>
/// Message bus implementation of IClientEventPublisher.
/// Publishes client events to session-specific RabbitMQ topics via MassTransit.
/// </summary>
/// <remarks>
/// <para>
/// Uses IMessageBus to publish to dynamic topics (CONNECT_SESSION_{sessionId}).
/// RabbitMQ creates fanout exchanges for each topic automatically.
/// </para>
/// <para>
/// The Connect service uses direct RabbitMQ to subscribe to these exchanges
/// because MassTransit doesn't support dynamic runtime subscriptions.
/// </para>
/// </remarks>
public class MessageBusClientEventPublisher : IClientEventPublisher
{
    private readonly IMessageBus _messageBus;
    private readonly ILogger<MessageBusClientEventPublisher> _logger;

    /// <summary>
    /// Prefix for session-specific topics.
    /// </summary>
    private const string SESSION_TOPIC_PREFIX = "CONNECT_SESSION_";

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

        var topic = $"{SESSION_TOPIC_PREFIX}{sessionId}";

        try
        {
            // CRITICAL: Use topic as exchange name so it matches what ClientEventRabbitMQSubscriber expects
            // Subscriber binds to fanout exchange named "CONNECT_SESSION_{sessionId}"
            // If we used the default "bannou" exchange, messages would never reach the subscriber
            var options = new PublishOptions { Exchange = topic };
            await _messageBus.PublishAsync(topic, eventData, options, cancellationToken);

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
                var topic = $"{SESSION_TOPIC_PREFIX}{sessionId}";
                try
                {
                    // CRITICAL: Use topic as exchange name (see comment in PublishToSessionAsync)
                    var options = new PublishOptions { Exchange = topic };
                    await _messageBus.PublishAsync(topic, eventData, options, cancellationToken);
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
            return eventData.Event_name;
        }

        // Property is shadowed on derived type - get its value
        var value = eventNameProp.GetValue(eventData);
        if (value == null)
        {
            // Fall back to base property
            return eventData.Event_name;
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
