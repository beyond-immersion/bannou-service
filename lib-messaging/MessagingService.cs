using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Configuration;
using BeyondImmersion.BannouService.Messaging.Services;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("lib-messaging.tests")]

namespace BeyondImmersion.BannouService.Messaging;

/// <summary>
/// Implementation of the Messaging service.
/// Provides HTTP API layer over native RabbitMQ messaging infrastructure.
/// </summary>
[DaprService("messaging", typeof(IMessagingService), lifetime: ServiceLifetime.Singleton)]
public partial class MessagingService : IMessagingService, IAsyncDisposable
{
    private readonly ILogger<MessagingService> _logger;
    private readonly MessagingServiceConfiguration _configuration;
    private readonly IErrorEventEmitter _errorEventEmitter;
    private readonly IMessageBus _messageBus;
    private readonly IMessageSubscriber _messageSubscriber;

    /// <summary>
    /// Tracks active dynamic subscriptions by subscriptionId for removal.
    /// Key: subscriptionId (Guid), Value: (topic, subscription handle, httpClient)
    /// WARNING: In-memory storage - subscriptions will be lost on service restart.
    /// This is a known limitation for dynamic HTTP callback subscriptions.
    /// </summary>
    private readonly ConcurrentDictionary<Guid, SubscriptionEntry> _activeSubscriptions = new();

    /// <summary>
    /// Represents an active subscription with its associated resources.
    /// </summary>
    private sealed record SubscriptionEntry(string Topic, IAsyncDisposable Handle, HttpClient HttpClient) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await Handle.DisposeAsync();
            HttpClient.Dispose();
        }
    }

    public MessagingService(
        ILogger<MessagingService> logger,
        MessagingServiceConfiguration configuration,
        IErrorEventEmitter errorEventEmitter,
        IMessageBus messageBus,
        IMessageSubscriber messageSubscriber)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _errorEventEmitter = errorEventEmitter ?? throw new ArgumentNullException(nameof(errorEventEmitter));
        _messageBus = messageBus ?? throw new ArgumentNullException(nameof(messageBus));
        _messageSubscriber = messageSubscriber ?? throw new ArgumentNullException(nameof(messageSubscriber));
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, PublishEventResponse?)> PublishEventAsync(
        PublishEventRequest body,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Publishing event to topic {Topic}", body.Topic);

        try
        {
            // Map API options to internal PublishOptions (Services.PublishOptions)
            Services.PublishOptions? options = body.Options != null
                ? new Services.PublishOptions
                {
                    Exchange = body.Options.Exchange,
                    Persistent = body.Options.Persistent,
                    Priority = (byte)body.Options.Priority,
                    CorrelationId = body.Options.CorrelationId != Guid.Empty ? body.Options.CorrelationId : null
                }
                : null;

            // Wrap payload in GenericMessageEnvelope - MassTransit requires concrete types
            var envelope = new Services.GenericMessageEnvelope(body.Topic, body.Payload);

            // Publish via IMessageBus using the envelope
            var messageId = await _messageBus.PublishAsync(
                body.Topic,
                envelope,
                options,
                cancellationToken);

            _logger.LogDebug("Published event {MessageId} to topic {Topic}", messageId, body.Topic);

            return (StatusCodes.OK, new PublishEventResponse
            {
                Success = true,
                MessageId = messageId
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish event to topic {Topic}", body.Topic);
            await _errorEventEmitter.TryPublishAsync(
                "messaging",
                "PublishEvent",
                ex.GetType().Name,
                ex.Message,
                dependency: "rabbitmq",
                endpoint: "post:/messaging/publish",
                details: new { Topic = body.Topic },
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, new PublishEventResponse { Success = false });
        }
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, CreateSubscriptionResponse?)> CreateSubscriptionAsync(
        CreateSubscriptionRequest body,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Creating subscription to topic {Topic} with callback {CallbackUrl}",
            body.Topic, body.CallbackUrl);

        HttpClient? httpClient = null;
        try
        {
            var subscriptionId = Guid.NewGuid();
            var queueName = $"bannou-dynamic-{subscriptionId:N}";

            // Create HTTP client that lives for the subscription duration
            httpClient = new HttpClient();
            var callbackUrl = body.CallbackUrl;

            // Subscribe dynamically using GenericMessageEnvelope - MassTransit requires concrete types
            var handle = await _messageSubscriber.SubscribeDynamicAsync<Services.GenericMessageEnvelope>(
                body.Topic,
                async (envelope, ct) =>
                {
                    try
                    {
                        // Forward the original payload JSON to the callback (unwrap the envelope)
                        var content = new StringContent(envelope.PayloadJson, System.Text.Encoding.UTF8, envelope.ContentType);
                        await httpClient.PostAsync(callbackUrl, content, ct);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to deliver event to callback {CallbackUrl}", callbackUrl);
                    }
                },
                cancellationToken);

            // Track for later removal - includes HttpClient for proper disposal
            var entry = new SubscriptionEntry(body.Topic, handle, httpClient);
            _activeSubscriptions[subscriptionId] = entry;

            _logger.LogInformation("Created subscription {SubscriptionId} to topic {Topic}",
                subscriptionId, body.Topic);

            return (StatusCodes.OK, new CreateSubscriptionResponse
            {
                SubscriptionId = subscriptionId,
                QueueName = queueName
            });
        }
        catch (Exception ex)
        {
            // Dispose HttpClient if we failed after creating it
            httpClient?.Dispose();

            _logger.LogError(ex, "Failed to create subscription to topic {Topic}", body.Topic);
            await _errorEventEmitter.TryPublishAsync(
                "messaging",
                "CreateSubscription",
                ex.GetType().Name,
                ex.Message,
                dependency: "rabbitmq",
                endpoint: "post:/messaging/subscribe",
                details: new { Topic = body.Topic, CallbackUrl = body.CallbackUrl },
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, RemoveSubscriptionResponse?)> RemoveSubscriptionAsync(
        RemoveSubscriptionRequest body,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Removing subscription {SubscriptionId}", body.SubscriptionId);

        try
        {
            if (!_activeSubscriptions.TryRemove(body.SubscriptionId, out var entry))
            {
                _logger.LogWarning("Subscription {SubscriptionId} not found", body.SubscriptionId);
                return (StatusCodes.NotFound, new RemoveSubscriptionResponse { Success = false });
            }

            // Dispose the subscription entry (handle + HttpClient)
            await entry.DisposeAsync();

            _logger.LogInformation("Removed subscription {SubscriptionId} from topic {Topic}",
                body.SubscriptionId, entry.Topic);

            return (StatusCodes.OK, new RemoveSubscriptionResponse { Success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove subscription {SubscriptionId}", body.SubscriptionId);
            await _errorEventEmitter.TryPublishAsync(
                "messaging",
                "RemoveSubscription",
                ex.GetType().Name,
                ex.Message,
                dependency: "rabbitmq",
                endpoint: "post:/messaging/unsubscribe",
                details: new { SubscriptionId = body.SubscriptionId },
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, new RemoveSubscriptionResponse { Success = false });
        }
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, ListTopicsResponse?)> ListTopicsAsync(
        ListTopicsRequest body,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Listing topics with filter {ExchangeFilter}", body?.ExchangeFilter);

        try
        {
            // Get topics from active subscriptions we're tracking
            var topics = _activeSubscriptions.Values
                .Select(entry => entry.Topic)
                .Distinct()
                .Where(t => string.IsNullOrEmpty(body?.ExchangeFilter) ||
                            t.StartsWith(body.ExchangeFilter, StringComparison.OrdinalIgnoreCase))
                .Select(t => new TopicInfo
                {
                    Name = t,
                    MessageCount = 0, // Would require RabbitMQ management API to get real counts
                    ConsumerCount = _activeSubscriptions.Values.Count(entry => entry.Topic == t)
                })
                .ToList();

            return (StatusCodes.OK, new ListTopicsResponse { Topics = topics });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list topics");
            await _errorEventEmitter.TryPublishAsync(
                "messaging",
                "ListTopics",
                ex.GetType().Name,
                ex.Message,
                dependency: "rabbitmq",
                endpoint: "post:/messaging/list-topics",
                details: new { ExchangeFilter = body?.ExchangeFilter },
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, new ListTopicsResponse { Topics = new List<TopicInfo>() });
        }
    }

    /// <summary>
    /// Disposes all active subscriptions when the service is disposed.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        _logger.LogInformation("Disposing MessagingService with {Count} active subscriptions",
            _activeSubscriptions.Count);

        foreach (var kvp in _activeSubscriptions)
        {
            try
            {
                await kvp.Value.DisposeAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to dispose subscription {SubscriptionId}", kvp.Key);
            }
        }

        _activeSubscriptions.Clear();
    }
}
