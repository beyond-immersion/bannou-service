#nullable enable

using System.Collections.Concurrent;

namespace BeyondImmersion.BannouService.Events;

/// <summary>
/// Static registry mapping topic names to event types.
/// Required because IEventConsumer handlers are stored as object-typed delegates.
/// </summary>
/// <remarks>
/// <para>
/// The IEventConsumer stores handlers as <c>Func&lt;IServiceProvider, object, Task&gt;</c> -
/// type information is lost after registration. This registry provides the topic→type
/// mapping needed by NativeEventConsumerBackend to deserialize incoming messages.
/// </para>
/// <para>
/// <strong>IMPORTANT</strong>: This registry MUST be populated before NativeEventConsumerBackend
/// starts. Use the auto-generated <c>EventSubscriptionRegistration.RegisterAll()</c> method
/// during application startup, or manually register event types in plugin initialization.
/// </para>
/// <para>
/// This class is thread-safe and supports idempotent registration.
/// </para>
/// </remarks>
public static class EventSubscriptionRegistry
{
    private static readonly ConcurrentDictionary<string, Type> _topicToEventType = new();

    /// <summary>
    /// Register a topic→eventType mapping.
    /// Called during service initialization (before subscriptions start).
    /// </summary>
    /// <typeparam name="TEvent">The event type for this topic</typeparam>
    /// <param name="topicName">The topic/routing key name (e.g., "session.connected")</param>
    /// <remarks>
    /// Registration is idempotent - registering the same topic multiple times with the same
    /// type is safe. However, registering a topic with a different type will overwrite
    /// the previous mapping and log a warning.
    /// </remarks>
    public static void Register<TEvent>(string topicName) where TEvent : class
    {
        ArgumentNullException.ThrowIfNull(topicName);

        var newType = typeof(TEvent);
        _topicToEventType.AddOrUpdate(
            topicName,
            newType,
            (_, existingType) =>
            {
                if (existingType != newType)
                {
                    // Log warning about type change - this could indicate a configuration error
                    Console.WriteLine(
                        $"[WARN] EventSubscriptionRegistry: Topic '{topicName}' type changed " +
                        $"from {existingType.Name} to {newType.Name}");
                }
                return newType;
            });
    }

    /// <summary>
    /// Get the event type for a topic, or null if not registered.
    /// </summary>
    /// <param name="topicName">The topic/routing key name</param>
    /// <returns>The registered event type, or null if not registered</returns>
    public static Type? GetEventType(string topicName)
    {
        ArgumentNullException.ThrowIfNull(topicName);
        return _topicToEventType.TryGetValue(topicName, out var type) ? type : null;
    }

    /// <summary>
    /// Get all registered topic names.
    /// </summary>
    /// <returns>All registered topic names</returns>
    public static IEnumerable<string> GetRegisteredTopics() => _topicToEventType.Keys;

    /// <summary>
    /// Get the count of registered topic→type mappings.
    /// </summary>
    public static int Count => _topicToEventType.Count;

    /// <summary>
    /// Check if a topic is registered.
    /// </summary>
    /// <param name="topicName">The topic/routing key name</param>
    /// <returns>True if the topic has a registered event type</returns>
    public static bool IsRegistered(string topicName)
    {
        ArgumentNullException.ThrowIfNull(topicName);
        return _topicToEventType.ContainsKey(topicName);
    }

    /// <summary>
    /// Clear all registrations. For testing purposes only.
    /// </summary>
    internal static void Clear() => _topicToEventType.Clear();
}
