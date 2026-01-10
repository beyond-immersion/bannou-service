using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Configuration;
using BeyondImmersion.BannouService.Messaging.Services;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace BeyondImmersion.BannouService.Messaging;

/// <summary>
/// Data model for persisting external HTTP callback subscriptions.
/// Stored in lib-state by app-id so subscriptions can be recovered on restart.
/// </summary>
/// <param name="SubscriptionId">Unique subscription identifier.</param>
/// <param name="Topic">The topic being subscribed to.</param>
/// <param name="CallbackUrl">HTTP callback URL for message delivery.</param>
/// <param name="CreatedAt">When the subscription was created.</param>
public sealed record ExternalSubscriptionData(
    Guid SubscriptionId,
    string Topic,
    string CallbackUrl,
    DateTimeOffset CreatedAt);

/// <summary>
/// Implementation of the Messaging service.
/// Provides HTTP API layer over native RabbitMQ messaging infrastructure.
/// </summary>
/// <remarks>
/// <para><strong>Subscription Architecture:</strong></para>
/// <para>
/// There are two ways to subscribe to messages in Bannou:
/// </para>
/// <list type="number">
/// <item>
/// <term>Internal (IMessageBus via DI)</term>
/// <description>
/// Used by plugins/services within the same process. Subscriptions are ephemeral
/// and tied to the node's lifecycle. When the node dies, the RabbitMQ consumers
/// die with it, and TTL cleans up the queues. This is intentional - internal
/// subscribers reconnect and re-subscribe on startup.
/// </description>
/// </item>
/// <item>
/// <term>External (MessagingClient via HTTP API)</term>
/// <description>
/// Used by external services (e.g., SDK clients) that call POST /messaging/subscribe.
/// These subscriptions are persisted to lib-state keyed by app-id so they can be
/// recovered on restart. External callers don't know when we restart, so we must
/// re-establish their subscriptions automatically.
/// </description>
/// </item>
/// </list>
/// <para>
/// The local <see cref="_activeSubscriptions"/> dictionary tracks live RabbitMQ consumer
/// handles for the current node. It's intentionally in-memory because handles can't be
/// serialized. The <see cref="_subscriptionStore"/> persists metadata for recovery.
/// </para>
/// </remarks>
[BannouService("messaging", typeof(IMessagingService), lifetime: ServiceLifetime.Singleton)]
public partial class MessagingService : IMessagingService, IAsyncDisposable
{
    /// <summary>
    /// Named HttpClient for subscription callbacks. Configured via IHttpClientFactory.
    /// </summary>
    internal const string HttpClientName = "MessagingCallbacks";

    /// <summary>
    /// Store name for external subscription persistence.
    /// </summary>
    internal const string ExternalSubscriptionStoreName = "messaging-external-subs";

    /// <summary>
    /// Default TTL for external subscription sets (24 hours).
    /// If a node doesn't come back online within this period, its subscriptions expire.
    /// </summary>
    private const int ExternalSubscriptionTtlSeconds = 86400;

    private readonly ILogger<MessagingService> _logger;
    private readonly MessagingServiceConfiguration _configuration;
    private readonly AppConfiguration _appConfiguration;
    private readonly IMessageBus _messageBus;
    private readonly IMessageSubscriber _messageSubscriber;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IStateStore<ExternalSubscriptionData> _subscriptionStore;
    private readonly string _appId;

    /// <summary>
    /// Tracks active dynamic subscriptions by subscriptionId for removal.
    /// Key: subscriptionId (Guid), Value: (topic, subscription handle, httpClient)
    /// This is intentionally in-memory - handles are process-local and can't be serialized.
    /// External subscription metadata is separately persisted to lib-state for recovery.
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
        AppConfiguration appConfiguration,
        IMessageBus messageBus,
        IMessageSubscriber messageSubscriber,
        IHttpClientFactory httpClientFactory,
        IStateStoreFactory stateStoreFactory)
    {
        _logger = logger;
        _configuration = configuration;
        _appConfiguration = appConfiguration;
        _messageBus = messageBus;
        _messageSubscriber = messageSubscriber;
        _httpClientFactory = httpClientFactory;
        _subscriptionStore = stateStoreFactory.GetStore<ExternalSubscriptionData>(ExternalSubscriptionStoreName);
        _appId = appConfiguration.EffectiveAppId;
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, PublishEventResponse?)> PublishEventAsync(
        PublishEventRequest body,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Publishing event to topic {Topic}", body.Topic);

        try
        {
            // Wrap payload in GenericMessageEnvelope - MassTransit requires concrete types
            var envelope = new Services.GenericMessageEnvelope(body.Topic, body.Payload);

            // Normalize options - treat Guid.Empty as null for CorrelationId
            var options = body.Options;
            if (options?.CorrelationId == Guid.Empty)
            {
                options.CorrelationId = null;
            }

            // Generate messageId upfront so we can return it to the caller
            var messageId = Guid.NewGuid();

            // Publish via IMessageBus using the envelope
            // API options and interface options use the same generated PublishOptions type
            var success = await _messageBus.TryPublishAsync(
                body.Topic,
                envelope,
                options,
                messageId,
                cancellationToken);

            _logger.LogDebug("Published event {MessageId} to topic {Topic} (success: {Success})", messageId, body.Topic, success);

            return (StatusCodes.OK, new PublishEventResponse
            {
                MessageId = messageId
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish event to topic {Topic}", body.Topic);
            await _messageBus.TryPublishErrorAsync(
                "messaging",
                "PublishEvent",
                ex.GetType().Name,
                ex.Message,
                dependency: "rabbitmq",
                endpoint: "post:/messaging/publish",
                details: new { Topic = body.Topic },
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
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

            // Create HTTP client that lives for the subscription duration (FOUNDATION TENETS: use IHttpClientFactory)
            httpClient = _httpClientFactory.CreateClient(HttpClientName);
            var callbackUrl = body.CallbackUrl;

            // Subscribe dynamically using GenericMessageEnvelope - MassTransit requires concrete types
            // Capture config values for the lambda closure
            var maxRetries = _configuration.CallbackRetryMaxAttempts;
            var retryDelayMs = _configuration.CallbackRetryDelayMs;

            var handle = await _messageSubscriber.SubscribeDynamicAsync<Services.GenericMessageEnvelope>(
                body.Topic,
                async (envelope, ct) =>
                {
                    var attempt = 0;
                    Exception? lastException = null;

                    while (attempt <= maxRetries)
                    {
                        try
                        {
                            // Forward the original payload JSON to the callback (unwrap the envelope)
                            var content = new StringContent(envelope.PayloadJson, System.Text.Encoding.UTF8, envelope.ContentType);
                            var response = await httpClient.PostAsync(callbackUrl, content, ct);

                            // Success - don't retry on any HTTP response (including 4xx/5xx)
                            // Only network-level failures should retry
                            return;
                        }
                        catch (HttpRequestException ex)
                        {
                            // Network-level failure (endpoint not reachable) - retry
                            lastException = ex;
                            attempt++;
                            if (attempt <= maxRetries)
                            {
                                _logger.LogDebug(
                                    "HTTP callback delivery attempt {Attempt} failed for {CallbackUrl}, retrying in {DelayMs}ms",
                                    attempt, callbackUrl, retryDelayMs);
                                await Task.Delay(retryDelayMs, ct);
                            }
                        }
                        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
                        {
                            // Timeout (not user cancellation) - retry
                            lastException = ex;
                            attempt++;
                            if (attempt <= maxRetries)
                            {
                                _logger.LogDebug(
                                    "HTTP callback delivery attempt {Attempt} timed out for {CallbackUrl}, retrying in {DelayMs}ms",
                                    attempt, callbackUrl, retryDelayMs);
                                await Task.Delay(retryDelayMs, ct);
                            }
                        }
                        catch (Exception ex)
                        {
                            // Other exceptions (including user cancellation) - don't retry
                            _logger.LogError(ex, "Failed to deliver event to callback {CallbackUrl}: unexpected error", callbackUrl);
                            return;
                        }
                    }

                    // All retries exhausted - log error and publish error event
                    _logger.LogError(
                        lastException,
                        "Failed to deliver event to callback {CallbackUrl} after {Attempts} attempts",
                        callbackUrl, maxRetries + 1);

                    await _messageBus.TryPublishErrorAsync(
                        "messaging",
                        "DeliverCallback",
                        lastException?.GetType().Name ?? "Unknown",
                        lastException?.Message ?? "Unknown error",
                        dependency: "http-callback",
                        endpoint: callbackUrl.ToString(),
                        details: new { Topic = body.Topic, Attempts = maxRetries + 1 },
                        stack: lastException?.StackTrace,
                        cancellationToken: ct);
                },
                exchange: null, // Use default exchange
                cancellationToken: cancellationToken);

            // Track for later removal - includes HttpClient for proper disposal
            var entry = new SubscriptionEntry(body.Topic, handle, httpClient);
            _activeSubscriptions[subscriptionId] = entry;

            // Persist to lib-state for recovery on restart (keyed by app-id)
            // External callers (via HTTP API) need their subscriptions restored automatically
            var subscriptionData = new ExternalSubscriptionData(
                subscriptionId,
                body.Topic,
                callbackUrl.ToString(),
                DateTimeOffset.UtcNow);

            await _subscriptionStore.AddToSetAsync(
                _appId,
                subscriptionData,
                new StateOptions { Ttl = ExternalSubscriptionTtlSeconds },
                cancellationToken);

            _logger.LogInformation("Created subscription {SubscriptionId} to topic {Topic} (persisted for app-id: {AppId})",
                subscriptionId, body.Topic, _appId);

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
            await _messageBus.TryPublishErrorAsync(
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
                return (StatusCodes.NotFound, null);
            }

            // Dispose the subscription entry (handle + HttpClient)
            await entry.DisposeAsync();

            // Remove from persisted set - find the matching subscription by ID
            var persistedSubs = await _subscriptionStore.GetSetAsync<ExternalSubscriptionData>(_appId, cancellationToken);
            var subToRemove = persistedSubs.FirstOrDefault(s => s.SubscriptionId == body.SubscriptionId);
            if (subToRemove != null)
            {
                await _subscriptionStore.RemoveFromSetAsync(_appId, subToRemove, cancellationToken);
            }

            _logger.LogInformation("Removed subscription {SubscriptionId} from topic {Topic}",
                body.SubscriptionId, entry.Topic);

            return (StatusCodes.OK, new RemoveSubscriptionResponse());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove subscription {SubscriptionId}", body.SubscriptionId);
            await _messageBus.TryPublishErrorAsync(
                "messaging",
                "RemoveSubscription",
                ex.GetType().Name,
                ex.Message,
                dependency: "rabbitmq",
                endpoint: "post:/messaging/unsubscribe",
                details: new { SubscriptionId = body.SubscriptionId },
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
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
            await _messageBus.TryPublishErrorAsync(
                "messaging",
                "ListTopics",
                ex.GetType().Name,
                ex.Message,
                dependency: "rabbitmq",
                endpoint: "post:/messaging/list-topics",
                details: new { ExchangeFilter = body?.ExchangeFilter },
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Recovers external subscriptions from lib-state on startup.
    /// Called by MessagingSubscriptionRecoveryService to restore subscriptions
    /// that were persisted before a restart.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Number of subscriptions recovered.</returns>
    public async Task<int> RecoverExternalSubscriptionsAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Recovering external subscriptions for app-id {AppId}", _appId);

        var persistedSubs = await _subscriptionStore.GetSetAsync<ExternalSubscriptionData>(_appId, cancellationToken);
        if (persistedSubs.Count == 0)
        {
            _logger.LogDebug("No persisted subscriptions found for app-id {AppId}", _appId);
            return 0;
        }

        var recovered = 0;
        var failed = 0;

        foreach (var sub in persistedSubs)
        {
            // Skip if already active (shouldn't happen, but be safe)
            if (_activeSubscriptions.ContainsKey(sub.SubscriptionId))
            {
                _logger.LogDebug("Subscription {SubscriptionId} already active, skipping", sub.SubscriptionId);
                continue;
            }

            try
            {
                // Re-create the subscription
                var request = new CreateSubscriptionRequest
                {
                    Topic = sub.Topic,
                    CallbackUrl = new Uri(sub.CallbackUrl)
                };

                // Create subscription without re-persisting (it's already persisted)
                var httpClient = _httpClientFactory.CreateClient(HttpClientName);
                var queueName = $"bannou-dynamic-{sub.SubscriptionId:N}";
                var maxRetries = _configuration.CallbackRetryMaxAttempts;
                var retryDelayMs = _configuration.CallbackRetryDelayMs;
                var callbackUrl = sub.CallbackUrl;

                var handle = await _messageSubscriber.SubscribeDynamicAsync<Services.GenericMessageEnvelope>(
                    sub.Topic,
                    async (envelope, ct) =>
                    {
                        var attempt = 0;
                        Exception? lastException = null;

                        while (attempt <= maxRetries)
                        {
                            try
                            {
                                var content = new StringContent(envelope.PayloadJson, System.Text.Encoding.UTF8, envelope.ContentType);
                                var response = await httpClient.PostAsync(callbackUrl, content, ct);
                                return;
                            }
                            catch (HttpRequestException ex)
                            {
                                lastException = ex;
                                attempt++;
                                if (attempt <= maxRetries)
                                {
                                    await Task.Delay(retryDelayMs, ct);
                                }
                            }
                            catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
                            {
                                lastException = ex;
                                attempt++;
                                if (attempt <= maxRetries)
                                {
                                    await Task.Delay(retryDelayMs, ct);
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "Failed to deliver event to callback {CallbackUrl}: unexpected error", callbackUrl);
                                return;
                            }
                        }

                        _logger.LogError(
                            lastException,
                            "Failed to deliver event to callback {CallbackUrl} after {Attempts} attempts (recovered subscription)",
                            callbackUrl, maxRetries + 1);
                    },
                    exchange: null,
                    cancellationToken: cancellationToken);

                var entry = new SubscriptionEntry(sub.Topic, handle, httpClient);
                _activeSubscriptions[sub.SubscriptionId] = entry;
                recovered++;

                _logger.LogDebug("Recovered subscription {SubscriptionId} to topic {Topic}",
                    sub.SubscriptionId, sub.Topic);
            }
            catch (Exception ex)
            {
                failed++;
                _logger.LogError(ex, "Failed to recover subscription {SubscriptionId} to topic {Topic}",
                    sub.SubscriptionId, sub.Topic);

                // Remove failed subscription from persisted set
                await _subscriptionStore.RemoveFromSetAsync(_appId, sub, cancellationToken);
            }
        }

        // Refresh TTL on the set to keep it alive
        await _subscriptionStore.RefreshSetTtlAsync(_appId, ExternalSubscriptionTtlSeconds, cancellationToken);

        _logger.LogInformation("Recovered {Recovered} subscriptions for app-id {AppId} ({Failed} failed)",
            recovered, _appId, failed);

        return recovered;
    }

    /// <summary>
    /// Refreshes the TTL on persisted subscriptions to keep them alive.
    /// Should be called periodically to prevent subscription data from expiring.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task RefreshSubscriptionTtlAsync(CancellationToken cancellationToken)
    {
        if (_activeSubscriptions.Count > 0)
        {
            await _subscriptionStore.RefreshSetTtlAsync(_appId, ExternalSubscriptionTtlSeconds, cancellationToken);
            _logger.LogDebug("Refreshed TTL on subscription set for app-id {AppId}", _appId);
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
