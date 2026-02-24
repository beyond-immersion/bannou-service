#nullable enable

using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;

namespace BeyondImmersion.BannouService.Messaging.Services;

/// <summary>
/// Native RabbitMQ backend for IEventConsumer fan-out.
/// Replaces mesh-generated EventsController files.
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
    private readonly ITelemetryProvider _telemetryProvider;
    private readonly ConcurrentBag<IAsyncDisposable> _subscriptions = new();
    private volatile bool _disposing;

    /// <summary>
    /// Creates a new NativeEventConsumerBackend instance.
    /// </summary>
    public NativeEventConsumerBackend(
        IMessageSubscriber subscriber,
        IEventConsumer eventConsumer,
        IServiceProvider serviceProvider,
        ILogger<NativeEventConsumerBackend> logger,
        ITelemetryProvider telemetryProvider)
    {
        _subscriber = subscriber;
        _eventConsumer = eventConsumer;
        _serviceProvider = serviceProvider;
        _logger = logger;
        _telemetryProvider = telemetryProvider;
    }

    /// <inheritdoc/>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity(
            TelemetryComponents.Messaging,
            "messaging.event_consumer_backend.start",
            ActivityKind.Client);

        var registeredTopics = _eventConsumer.GetRegisteredTopics().ToList();

        _logger.LogInformation(
            "Starting NativeEventConsumerBackend - found {TopicCount} topics with registered handlers",
            registeredTopics.Count);

        var successCount = 0;
        var failCount = 0;

        foreach (var topic in registeredTopics)
        {
            // Check disposal flag to prevent race during shutdown
            if (_disposing)
            {
                _logger.LogWarning("Shutdown in progress, skipping remaining subscriptions");
                break;
            }

            var eventType = EventSubscriptionRegistry.GetEventType(topic);
            if (eventType == null)
            {
                _logger.LogWarning(
                    "Topic '{Topic}' has handlers but no event type registered in EventSubscriptionRegistry. " +
                    "Skipping subscription. Ensure EventSubscriptionRegistration.RegisterAll() is called before StartAsync.",
                    topic);
                failCount++;
                continue;
            }

            try
            {
                _logger.LogDebug("Subscribing to topic '{Topic}' ({EventType})...", topic, eventType.Name);

                // Create typed subscription using reflection
                var method = typeof(NativeEventConsumerBackend)
                    .GetMethod(nameof(SubscribeTypedAsync), BindingFlags.NonPublic | BindingFlags.Instance);

                if (method == null)
                {
                    _logger.LogError("Failed to find SubscribeTypedAsync method via reflection");
                    failCount++;
                    continue;
                }

                var genericMethod = method.MakeGenericMethod(eventType);
                var subscriptionTask = (Task<IAsyncDisposable>?)genericMethod.Invoke(
                    this,
                    new object[] { topic, cancellationToken });

                if (subscriptionTask == null)
                {
                    _logger.LogError("SubscribeTypedAsync returned null for topic '{Topic}'", topic);
                    failCount++;
                    continue;
                }

                var subscription = await subscriptionTask;
                _subscriptions.Add(subscription);
                successCount++;

                _logger.LogDebug(
                    "Subscribed to topic '{Topic}' ({EventType}) - {Current}/{Total}",
                    topic,
                    eventType.Name,
                    successCount,
                    registeredTopics.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to subscribe to topic '{Topic}' with event type {EventType}",
                    topic,
                    eventType.Name);
                failCount++;
                // Continue with other topics - don't fail entirely
            }
        }

        if (failCount > 0)
        {
            _logger.LogWarning(
                "NativeEventConsumerBackend started with {SuccessCount} active subscriptions ({FailCount} failed)",
                successCount, failCount);
        }
        else
        {
            _logger.LogInformation(
                "NativeEventConsumerBackend started with {SubscriptionCount} active subscriptions",
                successCount);
        }
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
                        "Event dispatch failed for topic '{Topic}' - message will be nacked/requeued. " +
                        "Handler isolation ensures other handlers on this topic executed.",
                        topic);

                    // Record telemetry for observability
                    _telemetryProvider.RecordCounter(
                        TelemetryComponents.Messaging,
                        "messaging.handler.failures",
                        1,
                        new KeyValuePair<string, object?>("topic", topic));

                    throw; // Intentional: triggers NACK with requeue logic
                }
            },
            exchange: null, // Use default exchange for service events
            cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        // Set flag to prevent new subscriptions during shutdown
        _disposing = true;

        // Take snapshot to avoid race conditions with concurrent modifications
        var subscriptions = _subscriptions.ToArray();
        _logger.LogInformation("Stopping NativeEventConsumerBackend - cleaning up {Count} subscriptions", subscriptions.Length);

        foreach (var subscription in subscriptions)
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

        // Drain the bag (ConcurrentBag has no Clear method)
        while (_subscriptions.TryTake(out _)) { }
        _logger.LogInformation("NativeEventConsumerBackend stopped");
    }
}
