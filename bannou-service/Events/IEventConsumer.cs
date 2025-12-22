using Microsoft.Extensions.DependencyInjection;

namespace BeyondImmersion.BannouService.Events;

/// <summary>
/// Service for registering and dispatching Dapr pub/sub events across all plugins.
/// This provides an application-level fan-out layer on top of Dapr's single-subscription-per-topic limitation.
/// </summary>
/// <remarks>
/// <para>
/// Dapr only allows ONE endpoint to subscribe to a topic per app-id. When multiple plugins
/// want to handle the same event, only one receives it. This service solves that by:
/// </para>
/// <list type="number">
/// <item>Having generated controllers push events into this singleton service</item>
/// <item>Dispatching to ALL registered handlers across all plugins</item>
/// <item>Isolating handler failures so one throwing doesn't prevent others from running</item>
/// </list>
/// <para>
/// Registration uses a factory pattern to avoid capturing stale service instances.
/// When dispatching, fresh service instances are resolved from the request scope.
/// </para>
/// </remarks>
public interface IEventConsumer
{
    /// <summary>
    /// Registers an event handler for a specific topic.
    /// </summary>
    /// <typeparam name="TEvent">The event type to handle.</typeparam>
    /// <param name="topicName">The Dapr topic name (e.g., "session.connected").</param>
    /// <param name="handlerKey">Unique key to prevent duplicate registrations (e.g., "permissions:session.connected").</param>
    /// <param name="handlerFactory">
    /// Factory that takes IServiceProvider and event, resolves the service, and calls the handler.
    /// The factory should resolve services from the provided scope, not capture constructor instances.
    /// </param>
    void Register<TEvent>(
        string topicName,
        string handlerKey,
        Func<IServiceProvider, TEvent, Task> handlerFactory) where TEvent : class;

    /// <summary>
    /// Dispatches an event to all registered handlers for the topic.
    /// </summary>
    /// <typeparam name="TEvent">The event type.</typeparam>
    /// <param name="topicName">The Dapr topic name.</param>
    /// <param name="evt">The event data.</param>
    /// <param name="requestScope">The DI scope for the current request (used to resolve services).</param>
    /// <returns>Task that completes when all handlers have been invoked.</returns>
    /// <remarks>
    /// Handler failures are logged but do not prevent other handlers from running.
    /// All handlers are invoked even if some throw exceptions.
    /// </remarks>
    Task DispatchAsync<TEvent>(
        string topicName,
        TEvent evt,
        IServiceProvider requestScope) where TEvent : class;

    /// <summary>
    /// Gets all registered topic names for diagnostic purposes.
    /// </summary>
    IEnumerable<string> GetRegisteredTopics();

    /// <summary>
    /// Gets the number of handlers registered for a topic.
    /// </summary>
    int GetHandlerCount(string topicName);
}
