using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace BeyondImmersion.BannouService.Events;

/// <summary>
/// Implementation of <see cref="IEventConsumer"/> that provides application-level event fan-out.
/// </summary>
/// <remarks>
/// This service is registered as a singleton. Handler registration is thread-safe and idempotent.
/// When dispatching, handlers are resolved from the request scope to ensure proper DI lifetime management.
/// </remarks>
public class EventConsumer : IEventConsumer
{
    private readonly ILogger<EventConsumer> _logger;

    // Topic name -> list of (handlerKey, handlerFactory)
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, Func<IServiceProvider, object, Task>>> _handlers = new();

    // Track registered keys to prevent duplicates
    private readonly ConcurrentDictionary<string, byte> _registeredKeys = new();

    /// <summary>
    /// Creates a new EventConsumer instance.
    /// </summary>
    /// <param name="logger">Logger for this service.</param>
    public EventConsumer(ILogger<EventConsumer> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public void Register<TEvent>(
        string topicName,
        string handlerKey,
        Func<IServiceProvider, TEvent, Task> handlerFactory) where TEvent : class
    {
        ArgumentNullException.ThrowIfNull(topicName);
        ArgumentNullException.ThrowIfNull(handlerKey);
        ArgumentNullException.ThrowIfNull(handlerFactory);

        // Idempotent registration - skip if already registered
        if (!_registeredKeys.TryAdd(handlerKey, 0))
        {
            return;
        }

        // Add handler to topic
        var topicHandlers = _handlers.GetOrAdd(topicName, _ => new ConcurrentDictionary<string, Func<IServiceProvider, object, Task>>());

        // Wrap the typed handler in an object-based delegate
        topicHandlers[handlerKey] = async (sp, evt) => await handlerFactory(sp, (TEvent)evt);

        _logger.LogDebug("Registered handler '{HandlerKey}' for topic '{TopicName}'", handlerKey, topicName);
    }

    /// <inheritdoc/>
    public async Task DispatchAsync<TEvent>(
        string topicName,
        TEvent evt,
        IServiceProvider requestScope) where TEvent : class
    {
        ArgumentNullException.ThrowIfNull(topicName);
        ArgumentNullException.ThrowIfNull(evt);
        ArgumentNullException.ThrowIfNull(requestScope);

        if (!_handlers.TryGetValue(topicName, out var topicHandlers) || topicHandlers.IsEmpty)
        {
            _logger.LogDebug("No handlers registered for topic '{TopicName}'", topicName);
            return;
        }

        var handlerCount = topicHandlers.Count;
        var successCount = 0;
        var failureCount = 0;

        _logger.LogDebug("Dispatching '{TopicName}' to {HandlerCount} handler(s)", topicName, handlerCount);

        foreach (var (handlerKey, handler) in topicHandlers)
        {
            try
            {
                await handler(requestScope, evt);
                successCount++;
                _logger.LogDebug("Handler '{HandlerKey}' completed successfully", handlerKey);
            }
            catch (Exception ex)
            {
                failureCount++;
                _logger.LogError(ex, "Handler '{HandlerKey}' failed for topic '{TopicName}'", handlerKey, topicName);
                // Continue to next handler - don't let one failure prevent others
            }
        }

        if (failureCount > 0)
        {
            _logger.LogWarning("Topic '{TopicName}' dispatch completed: {SuccessCount}/{HandlerCount} succeeded, {FailureCount} failed",
                topicName, successCount, handlerCount, failureCount);
        }
        else
        {
            _logger.LogDebug("Topic '{TopicName}' dispatch completed: all {HandlerCount} handlers succeeded", topicName, handlerCount);
        }
    }

    /// <inheritdoc/>
    public IEnumerable<string> GetRegisteredTopics() => _handlers.Keys;

    /// <inheritdoc/>
    public int GetHandlerCount(string topicName)
    {
        return _handlers.TryGetValue(topicName, out var handlers) ? handlers.Count : 0;
    }
}
