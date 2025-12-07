using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Servicedata;
using Dapr.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("lib-subscriptions.tests")]

namespace BeyondImmersion.BannouService.Subscriptions;

/// <summary>
/// Implementation of the Subscriptions service.
/// Manages user subscriptions to game services with time-limited access control.
/// Publishes subscription.updated events for real-time session authorization updates.
/// </summary>
[DaprService("subscriptions", typeof(ISubscriptionsService), lifetime: ServiceLifetime.Scoped)]
public class SubscriptionsService : ISubscriptionsService
{
    private readonly DaprClient _daprClient;
    private readonly ILogger<SubscriptionsService> _logger;
    private readonly SubscriptionsServiceConfiguration _configuration;
    private readonly IServicedataClient _servicedataClient;

    // Key patterns for Dapr state store
    private const string SUBSCRIPTION_KEY_PREFIX = "subscription:";
    private const string ACCOUNT_SUBSCRIPTIONS_PREFIX = "account-subscriptions:";
    private const string SERVICE_SUBSCRIPTIONS_PREFIX = "service-subscriptions:";

    // Pub/sub configuration
    private const string PUBSUB_NAME = "bannou-pubsub";
    private const string SUBSCRIPTION_UPDATED_TOPIC = "subscription.updated";

    public SubscriptionsService(
        DaprClient daprClient,
        ILogger<SubscriptionsService> logger,
        SubscriptionsServiceConfiguration configuration,
        IServicedataClient servicedataClient)
    {
        _daprClient = daprClient ?? throw new ArgumentNullException(nameof(daprClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _servicedataClient = servicedataClient ?? throw new ArgumentNullException(nameof(servicedataClient));
    }

    private string StateStoreName => _configuration.StateStoreName ?? "subscriptions-statestore";
    private string AuthorizationSuffix => _configuration.AuthorizationSuffix ?? "authorized";

    /// <summary>
    /// Get all subscriptions for an account with optional filtering.
    /// </summary>
    public async Task<(StatusCodes, SubscriptionListResponse?)> GetAccountSubscriptionsAsync(
        GetAccountSubscriptionsRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Getting subscriptions for account {AccountId} (includeInactive={IncludeInactive}, includeExpired={IncludeExpired})",
            body.AccountId, body.IncludeInactive, body.IncludeExpired);

        try
        {
            var subscriptionIds = await _daprClient.GetStateAsync<List<string>>(
                StateStoreName,
                $"{ACCOUNT_SUBSCRIPTIONS_PREFIX}{body.AccountId}",
                cancellationToken: cancellationToken);

            var subscriptions = new List<SubscriptionInfo>();
            var now = DateTimeOffset.UtcNow;

            if (subscriptionIds != null)
            {
                foreach (var subscriptionId in subscriptionIds)
                {
                    var model = await _daprClient.GetStateAsync<SubscriptionDataModel>(
                        StateStoreName,
                        $"{SUBSCRIPTION_KEY_PREFIX}{subscriptionId}",
                        cancellationToken: cancellationToken);

                    if (model != null)
                    {
                        // Apply filters
                        if (!body.IncludeInactive && !model.IsActive)
                            continue;

                        if (!body.IncludeExpired && model.ExpirationDateUnix.HasValue)
                        {
                            var expirationDate = DateTimeOffset.FromUnixTimeSeconds(model.ExpirationDateUnix.Value);
                            if (expirationDate <= now)
                                continue;
                        }

                        subscriptions.Add(MapToSubscriptionInfo(model));
                    }
                }
            }

            var response = new SubscriptionListResponse
            {
                Subscriptions = subscriptions,
                TotalCount = subscriptions.Count
            };

            _logger.LogDebug("Found {Count} subscriptions for account {AccountId}", subscriptions.Count, body.AccountId);
            return (StatusCodes.OK, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting subscriptions for account {AccountId}", body.AccountId);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Get current active, non-expired subscriptions as authorization strings.
    /// Used by Auth service during session creation to populate session authorizations.
    /// </summary>
    public async Task<(StatusCodes, CurrentSubscriptionsResponse?)> GetCurrentSubscriptionsAsync(
        GetCurrentSubscriptionsRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Getting current subscriptions for account {AccountId}", body.AccountId);

        try
        {
            var subscriptionIds = await _daprClient.GetStateAsync<List<string>>(
                StateStoreName,
                $"{ACCOUNT_SUBSCRIPTIONS_PREFIX}{body.AccountId}",
                cancellationToken: cancellationToken);

            var authorizations = new List<string>();
            var subscriptions = new List<SubscriptionInfo>();
            var now = DateTimeOffset.UtcNow;

            if (subscriptionIds != null)
            {
                foreach (var subscriptionId in subscriptionIds)
                {
                    var model = await _daprClient.GetStateAsync<SubscriptionDataModel>(
                        StateStoreName,
                        $"{SUBSCRIPTION_KEY_PREFIX}{subscriptionId}",
                        cancellationToken: cancellationToken);

                    if (model != null && model.IsActive)
                    {
                        // Check expiration
                        if (model.ExpirationDateUnix.HasValue)
                        {
                            var expirationDate = DateTimeOffset.FromUnixTimeSeconds(model.ExpirationDateUnix.Value);
                            if (expirationDate <= now)
                                continue;
                        }

                        // Add authorization string (e.g., "arcadia:authorized")
                        authorizations.Add($"{model.StubName}:{AuthorizationSuffix}");
                        subscriptions.Add(MapToSubscriptionInfo(model));
                    }
                }
            }

            var response = new CurrentSubscriptionsResponse
            {
                AccountId = body.AccountId,
                Authorizations = authorizations,
                Subscriptions = subscriptions
            };

            _logger.LogDebug("Found {Count} active authorizations for account {AccountId}: {Authorizations}",
                authorizations.Count, body.AccountId, string.Join(", ", authorizations));
            return (StatusCodes.OK, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting current subscriptions for account {AccountId}", body.AccountId);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Get a specific subscription by ID.
    /// </summary>
    public async Task<(StatusCodes, SubscriptionInfo?)> GetSubscriptionAsync(
        GetSubscriptionRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Getting subscription {SubscriptionId}", body.SubscriptionId);

        try
        {
            var model = await _daprClient.GetStateAsync<SubscriptionDataModel>(
                StateStoreName,
                $"{SUBSCRIPTION_KEY_PREFIX}{body.SubscriptionId}",
                cancellationToken: cancellationToken);

            if (model == null)
            {
                _logger.LogWarning("Subscription {SubscriptionId} not found", body.SubscriptionId);
                return (StatusCodes.NotFound, null);
            }

            return (StatusCodes.OK, MapToSubscriptionInfo(model));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting subscription {SubscriptionId}", body.SubscriptionId);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Create a new subscription for an account.
    /// </summary>
    public async Task<(StatusCodes, SubscriptionInfo?)> CreateSubscriptionAsync(
        CreateSubscriptionRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Creating subscription for account {AccountId} to service {ServiceId}",
            body.AccountId, body.ServiceId);

        try
        {
            // Fetch service info from ServiceData service
            ServiceInfo? serviceInfo;
            try
            {
                serviceInfo = await _servicedataClient.GetServiceAsync(
                    new GetServiceRequest { ServiceId = body.ServiceId },
                    cancellationToken);
            }
            catch (ApiException ex) when (ex.StatusCode == 404)
            {
                _logger.LogWarning("Service {ServiceId} not found", body.ServiceId);
                return (StatusCodes.NotFound, null);
            }

            if (serviceInfo == null)
            {
                _logger.LogWarning("Service {ServiceId} not found", body.ServiceId);
                return (StatusCodes.NotFound, null);
            }

            // Check for existing active subscription
            var existingSubscriptionIds = await _daprClient.GetStateAsync<List<string>>(
                StateStoreName,
                $"{ACCOUNT_SUBSCRIPTIONS_PREFIX}{body.AccountId}",
                cancellationToken: cancellationToken) ?? new List<string>();

            foreach (var existingId in existingSubscriptionIds)
            {
                var existing = await _daprClient.GetStateAsync<SubscriptionDataModel>(
                    StateStoreName,
                    $"{SUBSCRIPTION_KEY_PREFIX}{existingId}",
                    cancellationToken: cancellationToken);

                if (existing != null &&
                    existing.ServiceId == body.ServiceId.ToString() &&
                    existing.IsActive)
                {
                    _logger.LogWarning("Active subscription already exists for account {AccountId} and service {ServiceId}",
                        body.AccountId, body.ServiceId);
                    return (StatusCodes.Conflict, null);
                }
            }

            // Create new subscription
            var subscriptionId = Guid.NewGuid();
            var now = DateTimeOffset.UtcNow;
            var startDate = body.StartDate ?? now;

            // Calculate expiration date
            long? expirationDateUnix = null;
            if (body.ExpirationDate.HasValue)
            {
                expirationDateUnix = body.ExpirationDate.Value.ToUnixTimeSeconds();
            }
            else if (body.DurationDays.HasValue && body.DurationDays.Value > 0)
            {
                expirationDateUnix = startDate.AddDays(body.DurationDays.Value).ToUnixTimeSeconds();
            }

            var model = new SubscriptionDataModel
            {
                SubscriptionId = subscriptionId.ToString(),
                AccountId = body.AccountId.ToString(),
                ServiceId = body.ServiceId.ToString(),
                StubName = serviceInfo.StubName,
                DisplayName = serviceInfo.DisplayName,
                StartDateUnix = startDate.ToUnixTimeSeconds(),
                ExpirationDateUnix = expirationDateUnix,
                IsActive = true,
                CancelledAtUnix = null,
                CancellationReason = null,
                CreatedAtUnix = now.ToUnixTimeSeconds(),
                UpdatedAtUnix = null
            };

            // Save subscription
            await _daprClient.SaveStateAsync(
                StateStoreName,
                $"{SUBSCRIPTION_KEY_PREFIX}{subscriptionId}",
                model,
                cancellationToken: cancellationToken);

            // Add to account index
            await AddToAccountIndexAsync(body.AccountId.ToString(), subscriptionId.ToString(), cancellationToken);

            // Add to service index
            await AddToServiceIndexAsync(body.ServiceId.ToString(), subscriptionId.ToString(), cancellationToken);

            // Publish subscription.updated event
            await PublishSubscriptionUpdatedEventAsync(model, "created", cancellationToken);

            _logger.LogInformation("Created subscription {SubscriptionId} for account {AccountId} to service {StubName}",
                subscriptionId, body.AccountId, serviceInfo.StubName);

            return (StatusCodes.Created, MapToSubscriptionInfo(model));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating subscription for account {AccountId}", body.AccountId);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Update an existing subscription.
    /// </summary>
    public async Task<(StatusCodes, SubscriptionInfo?)> UpdateSubscriptionAsync(
        UpdateSubscriptionRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Updating subscription {SubscriptionId}", body.SubscriptionId);

        try
        {
            var model = await _daprClient.GetStateAsync<SubscriptionDataModel>(
                StateStoreName,
                $"{SUBSCRIPTION_KEY_PREFIX}{body.SubscriptionId}",
                cancellationToken: cancellationToken);

            if (model == null)
            {
                _logger.LogWarning("Subscription {SubscriptionId} not found", body.SubscriptionId);
                return (StatusCodes.NotFound, null);
            }

            var wasActive = model.IsActive;

            // Update fields if provided
            if (body.ExpirationDate.HasValue)
            {
                model.ExpirationDateUnix = body.ExpirationDate.Value.ToUnixTimeSeconds();
            }

            if (body.IsActive.HasValue)
            {
                model.IsActive = body.IsActive.Value;
            }

            model.UpdatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            // Save updated subscription
            await _daprClient.SaveStateAsync(
                StateStoreName,
                $"{SUBSCRIPTION_KEY_PREFIX}{body.SubscriptionId}",
                model,
                cancellationToken: cancellationToken);

            // Publish subscription.updated event
            await PublishSubscriptionUpdatedEventAsync(model, "updated", cancellationToken);

            _logger.LogInformation("Updated subscription {SubscriptionId}", body.SubscriptionId);
            return (StatusCodes.OK, MapToSubscriptionInfo(model));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating subscription {SubscriptionId}", body.SubscriptionId);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Cancel a subscription.
    /// </summary>
    public async Task<(StatusCodes, SubscriptionInfo?)> CancelSubscriptionAsync(
        CancelSubscriptionRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Cancelling subscription {SubscriptionId}", body.SubscriptionId);

        try
        {
            var model = await _daprClient.GetStateAsync<SubscriptionDataModel>(
                StateStoreName,
                $"{SUBSCRIPTION_KEY_PREFIX}{body.SubscriptionId}",
                cancellationToken: cancellationToken);

            if (model == null)
            {
                _logger.LogWarning("Subscription {SubscriptionId} not found", body.SubscriptionId);
                return (StatusCodes.NotFound, null);
            }

            var now = DateTimeOffset.UtcNow;

            model.IsActive = false;
            model.CancelledAtUnix = now.ToUnixTimeSeconds();
            model.CancellationReason = body.Reason;
            model.UpdatedAtUnix = now.ToUnixTimeSeconds();

            // Save updated subscription
            await _daprClient.SaveStateAsync(
                StateStoreName,
                $"{SUBSCRIPTION_KEY_PREFIX}{body.SubscriptionId}",
                model,
                cancellationToken: cancellationToken);

            // Publish subscription.updated event
            await PublishSubscriptionUpdatedEventAsync(model, "cancelled", cancellationToken);

            _logger.LogInformation("Cancelled subscription {SubscriptionId} for account {AccountId}",
                body.SubscriptionId, model.AccountId);

            return (StatusCodes.OK, MapToSubscriptionInfo(model));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cancelling subscription {SubscriptionId}", body.SubscriptionId);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Renew or extend a subscription.
    /// </summary>
    public async Task<(StatusCodes, SubscriptionInfo?)> RenewSubscriptionAsync(
        RenewSubscriptionRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Renewing subscription {SubscriptionId}", body.SubscriptionId);

        try
        {
            var model = await _daprClient.GetStateAsync<SubscriptionDataModel>(
                StateStoreName,
                $"{SUBSCRIPTION_KEY_PREFIX}{body.SubscriptionId}",
                cancellationToken: cancellationToken);

            if (model == null)
            {
                _logger.LogWarning("Subscription {SubscriptionId} not found", body.SubscriptionId);
                return (StatusCodes.NotFound, null);
            }

            var now = DateTimeOffset.UtcNow;

            // Calculate new expiration date
            if (body.NewExpirationDate.HasValue)
            {
                model.ExpirationDateUnix = body.NewExpirationDate.Value.ToUnixTimeSeconds();
            }
            else if (body.ExtensionDays > 0)
            {
                // Extend from current expiration or from now if already expired
                DateTimeOffset baseDate;
                if (model.ExpirationDateUnix.HasValue)
                {
                    var currentExpiration = DateTimeOffset.FromUnixTimeSeconds(model.ExpirationDateUnix.Value);
                    baseDate = currentExpiration > now ? currentExpiration : now;
                }
                else
                {
                    baseDate = now;
                }
                model.ExpirationDateUnix = baseDate.AddDays(body.ExtensionDays).ToUnixTimeSeconds();
            }

            // Reactivate if cancelled
            model.IsActive = true;
            model.CancelledAtUnix = null;
            model.CancellationReason = null;
            model.UpdatedAtUnix = now.ToUnixTimeSeconds();

            // Save updated subscription
            await _daprClient.SaveStateAsync(
                StateStoreName,
                $"{SUBSCRIPTION_KEY_PREFIX}{body.SubscriptionId}",
                model,
                cancellationToken: cancellationToken);

            // Publish subscription.updated event
            await PublishSubscriptionUpdatedEventAsync(model, "renewed", cancellationToken);

            _logger.LogInformation("Renewed subscription {SubscriptionId} for account {AccountId}",
                body.SubscriptionId, model.AccountId);

            return (StatusCodes.OK, MapToSubscriptionInfo(model));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error renewing subscription {SubscriptionId}", body.SubscriptionId);
            return (StatusCodes.InternalServerError, null);
        }
    }

    #region Public Methods for Background Job

    /// <summary>
    /// Mark a subscription as expired. Called by the expiration background job.
    /// </summary>
    public async Task<bool> ExpireSubscriptionAsync(string subscriptionId, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Expiring subscription {SubscriptionId}", subscriptionId);

        try
        {
            var model = await _daprClient.GetStateAsync<SubscriptionDataModel>(
                StateStoreName,
                $"{SUBSCRIPTION_KEY_PREFIX}{subscriptionId}",
                cancellationToken: cancellationToken);

            if (model == null || !model.IsActive)
            {
                return false;
            }

            model.IsActive = false;
            model.UpdatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            await _daprClient.SaveStateAsync(
                StateStoreName,
                $"{SUBSCRIPTION_KEY_PREFIX}{subscriptionId}",
                model,
                cancellationToken: cancellationToken);

            await PublishSubscriptionUpdatedEventAsync(model, "expired", cancellationToken);

            _logger.LogInformation("Expired subscription {SubscriptionId} for account {AccountId}",
                subscriptionId, model.AccountId);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error expiring subscription {SubscriptionId}", subscriptionId);
            return false;
        }
    }

    /// <summary>
    /// Get subscriptions that need to be expired. Called by the expiration background job.
    /// </summary>
    public Task<List<string>> GetExpiredSubscriptionIdsAsync(CancellationToken cancellationToken)
    {
        // Note: In a real implementation, you'd want a more efficient way to query
        // expired subscriptions, possibly using a secondary index or a time-based key pattern.
        // For now, this is a placeholder that would need to iterate through all subscriptions.
        _logger.LogWarning("GetExpiredSubscriptionIdsAsync requires full scan - consider implementing secondary index");
        return Task.FromResult(new List<string>());
    }

    #endregion

    #region Private Helpers

    private async Task AddToAccountIndexAsync(string accountId, string subscriptionId, CancellationToken cancellationToken)
    {
        var subscriptionIds = await _daprClient.GetStateAsync<List<string>>(
            StateStoreName,
            $"{ACCOUNT_SUBSCRIPTIONS_PREFIX}{accountId}",
            cancellationToken: cancellationToken) ?? new List<string>();

        if (!subscriptionIds.Contains(subscriptionId))
        {
            subscriptionIds.Add(subscriptionId);
            await _daprClient.SaveStateAsync(
                StateStoreName,
                $"{ACCOUNT_SUBSCRIPTIONS_PREFIX}{accountId}",
                subscriptionIds,
                cancellationToken: cancellationToken);
        }
    }

    private async Task AddToServiceIndexAsync(string serviceId, string subscriptionId, CancellationToken cancellationToken)
    {
        var subscriptionIds = await _daprClient.GetStateAsync<List<string>>(
            StateStoreName,
            $"{SERVICE_SUBSCRIPTIONS_PREFIX}{serviceId}",
            cancellationToken: cancellationToken) ?? new List<string>();

        if (!subscriptionIds.Contains(subscriptionId))
        {
            subscriptionIds.Add(subscriptionId);
            await _daprClient.SaveStateAsync(
                StateStoreName,
                $"{SERVICE_SUBSCRIPTIONS_PREFIX}{serviceId}",
                subscriptionIds,
                cancellationToken: cancellationToken);
        }
    }

    private async Task PublishSubscriptionUpdatedEventAsync(
        SubscriptionDataModel model, string action, CancellationToken cancellationToken)
    {
        var eventData = new SubscriptionUpdatedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            SubscriptionId = Guid.Parse(model.SubscriptionId),
            AccountId = Guid.Parse(model.AccountId),
            ServiceId = Guid.Parse(model.ServiceId),
            StubName = model.StubName,
            DisplayName = model.DisplayName,
            Action = ParseAction(action),
            IsActive = model.IsActive,
            ExpirationDate = model.ExpirationDateUnix.HasValue
                ? DateTimeOffset.FromUnixTimeSeconds(model.ExpirationDateUnix.Value)
                : null
        };

        await _daprClient.PublishEventAsync(
            PUBSUB_NAME,
            SUBSCRIPTION_UPDATED_TOPIC,
            eventData,
            cancellationToken);

        _logger.LogDebug("Published subscription.updated event for {SubscriptionId} with action {Action}",
            model.SubscriptionId, action);
    }

    private static SubscriptionUpdatedEventAction ParseAction(string action)
    {
        return action.ToLowerInvariant() switch
        {
            "created" => SubscriptionUpdatedEventAction.Created,
            "updated" => SubscriptionUpdatedEventAction.Updated,
            "cancelled" => SubscriptionUpdatedEventAction.Cancelled,
            "expired" => SubscriptionUpdatedEventAction.Expired,
            "renewed" => SubscriptionUpdatedEventAction.Renewed,
            _ => SubscriptionUpdatedEventAction.Updated
        };
    }

    private static SubscriptionInfo MapToSubscriptionInfo(SubscriptionDataModel model)
    {
        return new SubscriptionInfo
        {
            SubscriptionId = Guid.Parse(model.SubscriptionId),
            AccountId = Guid.Parse(model.AccountId),
            ServiceId = Guid.Parse(model.ServiceId),
            StubName = model.StubName,
            DisplayName = model.DisplayName,
            StartDate = DateTimeOffset.FromUnixTimeSeconds(model.StartDateUnix),
            ExpirationDate = model.ExpirationDateUnix.HasValue
                ? DateTimeOffset.FromUnixTimeSeconds(model.ExpirationDateUnix.Value)
                : null,
            IsActive = model.IsActive,
            CancelledAt = model.CancelledAtUnix.HasValue
                ? DateTimeOffset.FromUnixTimeSeconds(model.CancelledAtUnix.Value)
                : null,
            CancellationReason = model.CancellationReason,
            CreatedAt = DateTimeOffset.FromUnixTimeSeconds(model.CreatedAtUnix),
            UpdatedAt = model.UpdatedAtUnix.HasValue
                ? DateTimeOffset.FromUnixTimeSeconds(model.UpdatedAtUnix.Value)
                : null
        };
    }

    #endregion

    #region Internal Storage Model

    /// <summary>
    /// Internal storage model using Unix timestamps to avoid Dapr serialization issues.
    /// </summary>
    private class SubscriptionDataModel
    {
        public string SubscriptionId { get; set; } = string.Empty;
        public string AccountId { get; set; } = string.Empty;
        public string ServiceId { get; set; } = string.Empty;
        public string StubName { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public long StartDateUnix { get; set; }
        public long? ExpirationDateUnix { get; set; }
        public bool IsActive { get; set; }
        public long? CancelledAtUnix { get; set; }
        public string? CancellationReason { get; set; }
        public long CreatedAtUnix { get; set; }
        public long? UpdatedAtUnix { get; set; }
    }

    #endregion
}

/// <summary>
/// Event published when a subscription changes state.
/// Auth service subscribes to this event to update session authorizations.
/// </summary>
public class SubscriptionUpdatedEvent
{
    public Guid EventId { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public Guid SubscriptionId { get; set; }
    public Guid AccountId { get; set; }
    public Guid ServiceId { get; set; }
    public string StubName { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public SubscriptionUpdatedEventAction Action { get; set; }
    public bool IsActive { get; set; }
    public DateTimeOffset? ExpirationDate { get; set; }
}

/// <summary>
/// Actions that can trigger a subscription.updated event.
/// </summary>
public enum SubscriptionUpdatedEventAction
{
    Created,
    Updated,
    Cancelled,
    Expired,
    Renewed
}
