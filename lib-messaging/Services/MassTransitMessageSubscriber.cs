#nullable enable

using BeyondImmersion.BannouService.Services;
using MassTransit;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace BeyondImmersion.BannouService.Messaging.Services;

/// <summary>
/// MassTransit-based implementation of IMessageSubscriber.
/// Provides subscription management for RabbitMQ topics.
/// </summary>
public sealed class MassTransitMessageSubscriber : IMessageSubscriber, IAsyncDisposable
{
    private readonly IBusControl _busControl;
    private readonly ILogger<MassTransitMessageSubscriber> _logger;
    private readonly MessagingServiceConfiguration _configuration;
    private readonly ConcurrentDictionary<string, HostReceiveEndpointHandle> _staticSubscriptions = new();
    private readonly ConcurrentDictionary<Guid, DynamicSubscription> _dynamicSubscriptions = new();

    /// <summary>
    /// Creates a new MassTransitMessageSubscriber instance.
    /// </summary>
    public MassTransitMessageSubscriber(
        IBusControl busControl,
        ILogger<MassTransitMessageSubscriber> logger,
        MessagingServiceConfiguration configuration)
    {
        _busControl = busControl ?? throw new ArgumentNullException(nameof(busControl));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    }

    /// <inheritdoc/>
    public async Task SubscribeAsync<TEvent>(
        string topic,
        Func<TEvent, CancellationToken, Task> handler,
        SubscriptionOptions? options = null,
        CancellationToken cancellationToken = default)
        where TEvent : class
    {
        ArgumentNullException.ThrowIfNull(topic);
        ArgumentNullException.ThrowIfNull(handler);

        var effectiveOptions = options ?? new SubscriptionOptions();
        var queueName = BuildQueueName(topic, effectiveOptions);

        // Check if already subscribed
        if (_staticSubscriptions.ContainsKey(topic))
        {
            _logger.LogWarning("Already subscribed to topic '{Topic}', skipping duplicate subscription", topic);
            return;
        }

        try
        {
            // Create a receive endpoint for this topic
            var handle = _busControl.ConnectReceiveEndpoint(queueName, endpoint =>
            {
                // Configure endpoint options
                endpoint.PrefetchCount = effectiveOptions.PrefetchCount;

                // MassTransit uses manual acknowledgement by default
                // UseDeadLetter is handled through MassTransit's error queue mechanism
                // which is automatic for failed messages

                // Register the consumer
                endpoint.Handler<TEvent>(async context =>
                {
                    try
                    {
                        await handler(context.Message, context.CancellationToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Handler failed for message on topic '{Topic}'", topic);
                        throw; // Re-throw to trigger retry/dead-letter
                    }
                });
            });

            await handle.Ready;
            _staticSubscriptions[topic] = handle;

            _logger.LogInformation(
                "Created static subscription to topic '{Topic}' (queue: {QueueName})",
                topic,
                queueName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create subscription for topic '{Topic}'", topic);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<IAsyncDisposable> SubscribeDynamicAsync<TEvent>(
        string topic,
        Func<TEvent, CancellationToken, Task> handler,
        CancellationToken cancellationToken = default)
        where TEvent : class
    {
        ArgumentNullException.ThrowIfNull(topic);
        ArgumentNullException.ThrowIfNull(handler);

        var subscriptionId = Guid.NewGuid();
        var queueName = $"{topic}.dynamic.{subscriptionId:N}";

        try
        {
            // Create a temporary receive endpoint for dynamic subscriptions
            // MassTransit automatically handles queue cleanup when the connection closes
            var handle = _busControl.ConnectReceiveEndpoint(queueName, endpoint =>
            {
                endpoint.PrefetchCount = _configuration.DefaultPrefetchCount;

                // Dynamic subscriptions use auto-delete queues configured below
                // When using ConnectReceiveEndpoint, MassTransit creates a temporary endpoint
                // that is cleaned up when disposed

                endpoint.Handler<TEvent>(async context =>
                {
                    try
                    {
                        await handler(context.Message, context.CancellationToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(
                            ex,
                            "Handler failed for dynamic subscription {SubscriptionId} on topic '{Topic}'",
                            subscriptionId,
                            topic);
                        throw;
                    }
                });
            });

            await handle.Ready;

            var subscription = new DynamicSubscription(subscriptionId, topic, handle, this);
            _dynamicSubscriptions[subscriptionId] = subscription;

            _logger.LogDebug(
                "Created dynamic subscription {SubscriptionId} to topic '{Topic}'",
                subscriptionId,
                topic);

            return subscription;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to create dynamic subscription for topic '{Topic}'",
                topic);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task UnsubscribeAsync(string topic)
    {
        ArgumentNullException.ThrowIfNull(topic);

        if (_staticSubscriptions.TryRemove(topic, out var handle))
        {
            try
            {
                await handle.StopAsync();
                _logger.LogInformation("Unsubscribed from topic '{Topic}'", topic);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to unsubscribe from topic '{Topic}'", topic);
                throw;
            }
        }
        else
        {
            _logger.LogWarning("No static subscription found for topic '{Topic}'", topic);
        }
    }

    /// <summary>
    /// Remove a dynamic subscription by ID.
    /// </summary>
    internal async Task RemoveDynamicSubscriptionAsync(Guid subscriptionId)
    {
        if (_dynamicSubscriptions.TryRemove(subscriptionId, out var subscription))
        {
            try
            {
                await subscription.Handle.StopAsync();
                _logger.LogDebug(
                    "Removed dynamic subscription {SubscriptionId} for topic '{Topic}'",
                    subscriptionId,
                    subscription.Topic);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to remove dynamic subscription {SubscriptionId}",
                    subscriptionId);
                throw;
            }
        }
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        // Clean up all static subscriptions
        foreach (var (topic, handle) in _staticSubscriptions)
        {
            try
            {
                await handle.StopAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error stopping subscription for topic '{Topic}'", topic);
            }
        }
        _staticSubscriptions.Clear();

        // Clean up all dynamic subscriptions
        foreach (var (id, subscription) in _dynamicSubscriptions)
        {
            try
            {
                await subscription.Handle.StopAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error stopping dynamic subscription {SubscriptionId}", id);
            }
        }
        _dynamicSubscriptions.Clear();
    }

    private string BuildQueueName(string topic, SubscriptionOptions options)
    {
        // If consumer group specified, use it for competing consumers
        if (!string.IsNullOrEmpty(options.ConsumerGroup))
        {
            return $"{topic}.{options.ConsumerGroup}";
        }

        // Default: topic-based queue for pub/sub pattern
        return topic;
    }

    /// <summary>
    /// Represents a dynamic subscription that can be disposed.
    /// </summary>
    private sealed class DynamicSubscription : IAsyncDisposable
    {
        private readonly MassTransitMessageSubscriber _parent;

        public Guid Id { get; }
        public string Topic { get; }
        public HostReceiveEndpointHandle Handle { get; }

        public DynamicSubscription(
            Guid id,
            string topic,
            HostReceiveEndpointHandle handle,
            MassTransitMessageSubscriber parent)
        {
            Id = id;
            Topic = topic;
            Handle = handle;
            _parent = parent;
        }

        public async ValueTask DisposeAsync()
        {
            await _parent.RemoveDynamicSubscriptionAsync(Id);
        }
    }
}
