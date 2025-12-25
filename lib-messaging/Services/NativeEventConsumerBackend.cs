#nullable enable

using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Reflection;

namespace BeyondImmersion.BannouService.Messaging.Services;

/// <summary>
/// Native RabbitMQ backend for IEventConsumer fan-out.
/// Replaces Dapr-generated EventsController files.
/// </summary>
/// <remarks>
/// <para>
/// This IHostedService bridges the native RabbitMQ subscriptions (via IMessageSubscriber)
/// to the existing IEventConsumer fan-out system. When a message arrives on a topic,
/// it is deserialized using the type from EventSubscriptionRegistry and dispatched
/// to all registered handlers via IEventConsumer.DispatchAsync().
/// </para>
/// <para>
/// <strong>Startup Order</strong>:
/// <list type="number">
/// <item>EventSubscriptionRegistration.RegisterAll() populates EventSubscriptionRegistry</item>
/// <item>Service plugins register handlers via IEventConsumer.Register()</item>
/// <item>NativeEventConsumerBackend.StartAsync() creates RabbitMQ subscriptions</item>
/// </list>
/// </para>
/// </remarks>
public sealed class NativeEventConsumerBackend : IHostedService
{
    private readonly IMessageSubscriber _subscriber;
    private readonly IEventConsumer _eventConsumer;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<NativeEventConsumerBackend> _logger;
    private readonly List<IAsyncDisposable> _subscriptions = new();

    /// <summary>
    /// Creates a new NativeEventConsumerBackend instance.
    /// </summary>
    public NativeEventConsumerBackend(
        IMessageSubscriber subscriber,
        IEventConsumer eventConsumer,
        IServiceProvider serviceProvider,
        ILogger<NativeEventConsumerBackend> logger)
    {
        _subscriber = subscriber ?? throw new ArgumentNullException(nameof(subscriber));
        _eventConsumer = eventConsumer ?? throw new ArgumentNullException(nameof(eventConsumer));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var registeredTopics = _eventConsumer.GetRegisteredTopics().ToList();

        _logger.LogInformation(
            "Starting NativeEventConsumerBackend - found {TopicCount} topics with registered handlers",
            registeredTopics.Count);

        foreach (var topic in registeredTopics)
        {
            var eventType = EventSubscriptionRegistry.GetEventType(topic);
            if (eventType == null)
            {
                _logger.LogWarning(
                    "Topic '{Topic}' has handlers but no event type registered in EventSubscriptionRegistry. " +
                    "Skipping subscription. Ensure EventSubscriptionRegistration.RegisterAll() is called before StartAsync.",
                    topic);
                continue;
            }

            try
            {
                // Create typed subscription using reflection
                var method = typeof(NativeEventConsumerBackend)
                    .GetMethod(nameof(SubscribeTypedAsync), BindingFlags.NonPublic | BindingFlags.Instance);

                if (method == null)
                {
                    _logger.LogError("Failed to find SubscribeTypedAsync method via reflection");
                    continue;
                }

                var genericMethod = method.MakeGenericMethod(eventType);
                var subscriptionTask = (Task<IAsyncDisposable>?)genericMethod.Invoke(
                    this,
                    new object[] { topic, cancellationToken });

                if (subscriptionTask == null)
                {
                    _logger.LogError("SubscribeTypedAsync returned null for topic '{Topic}'", topic);
                    continue;
                }

                var subscription = await subscriptionTask;
                _subscriptions.Add(subscription);

                _logger.LogInformation(
                    "Subscribed to topic '{Topic}' with event type {EventType}",
                    topic,
                    eventType.Name);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to subscribe to topic '{Topic}' with event type {EventType}",
                    topic,
                    eventType.Name);
                // Continue with other topics - don't fail entirely
            }
        }

        _logger.LogInformation(
            "NativeEventConsumerBackend started with {SubscriptionCount} active subscriptions",
            _subscriptions.Count);
    }

    /// <summary>
    /// Create a typed subscription for a specific event type.
    /// Called via reflection to maintain type safety.
    /// </summary>
    private async Task<IAsyncDisposable> SubscribeTypedAsync<TEvent>(
        string topic,
        CancellationToken cancellationToken) where TEvent : class
    {
        return await _subscriber.SubscribeDynamicAsync<TEvent>(
            topic,
            async (evt, ct) =>
            {
                // Create a scope for DI resolution (per-request services)
                using var scope = _serviceProvider.CreateScope();

                try
                {
                    // Dispatch to all registered handlers (fan-out)
                    await _eventConsumer.DispatchAsync(topic, evt, scope.ServiceProvider);
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "Error dispatching event on topic '{Topic}' to handlers",
                        topic);
                    throw; // Re-throw to trigger retry/dead-letter handling
                }
            },
            cancellationToken);
    }

    /// <inheritdoc/>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping NativeEventConsumerBackend - cleaning up {Count} subscriptions", _subscriptions.Count);

        foreach (var subscription in _subscriptions)
        {
            try
            {
                await subscription.DisposeAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error disposing subscription during shutdown");
            }
        }

        _subscriptions.Clear();
        _logger.LogInformation("NativeEventConsumerBackend stopped");
    }
}
