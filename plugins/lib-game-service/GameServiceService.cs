using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Resource;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.GameService;

/// <summary>
/// Implementation of the Game Service service.
/// Provides a minimal registry of game services (games/applications) that users can subscribe to.
/// </summary>
[BannouService("game-service", typeof(IGameServiceService), lifetime: ServiceLifetime.Singleton, layer: ServiceLayer.GameFoundation)]
public partial class GameServiceService : IGameServiceService
{
    private readonly IStateStoreFactory _stateStoreFactory;
    private readonly IMessageBus _messageBus;
    private readonly IDistributedLockProvider _lockProvider;
    private readonly ILogger<GameServiceService> _logger;
    private readonly GameServiceServiceConfiguration _configuration;
    private readonly IResourceClient _resourceClient;
    private readonly ITelemetryProvider _telemetryProvider;

    // Key patterns for state store
    private const string SERVICE_KEY_PREFIX = "game-service:";
    private const string SERVICE_STUB_INDEX_PREFIX = "game-service-stub:";
    private const string SERVICE_LIST_KEY = "game-service-list";

    public GameServiceService(
        IStateStoreFactory stateStoreFactory,
        IMessageBus messageBus,
        IDistributedLockProvider lockProvider,
        ILogger<GameServiceService> logger,
        GameServiceServiceConfiguration configuration,
        IEventConsumer eventConsumer,
        IResourceClient resourceClient,
        ITelemetryProvider telemetryProvider)
    {
        _stateStoreFactory = stateStoreFactory;
        _messageBus = messageBus;
        _lockProvider = lockProvider;
        _logger = logger;
        _configuration = configuration;
        _resourceClient = resourceClient;
        _telemetryProvider = telemetryProvider;

        // No event subscriptions (x-event-subscriptions: [])
        ((IBannouService)this).RegisterEventConsumers(eventConsumer);
    }

    private static string StateStoreName => StateStoreDefinitions.GameService;

    /// <summary>
    /// List all registered game services, optionally filtered by active status with pagination.
    /// </summary>
    /// <param name="body">Request containing optional filter, skip, and take for pagination.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>
    /// OK with paginated list of services and total count, or InternalServerError if state store fails.
    /// </returns>
    public async Task<(StatusCodes, ListServicesResponse?)> ListServicesAsync(ListServicesRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Listing services (activeOnly={ActiveOnly}, skip={Skip}, take={Take})",
            body.ActiveOnly, body.Skip, body.Take);

        // Get all service IDs from the index
        var listStore = _stateStoreFactory.GetStore<List<Guid>>(StateStoreName);
        var serviceIds = await listStore.GetAsync(SERVICE_LIST_KEY, cancellationToken);

        var allMatchingServices = new List<ServiceInfo>();
        var modelStore = _stateStoreFactory.GetStore<GameServiceRegistryModel>(StateStoreName);

        if (serviceIds != null)
        {
            foreach (var serviceId in serviceIds)
            {
                var serviceModel = await modelStore.GetAsync($"{SERVICE_KEY_PREFIX}{serviceId}", cancellationToken);

                if (serviceModel != null)
                {
                    // Apply active filter
                    if (body.ActiveOnly && !serviceModel.IsActive)
                        continue;

                    allMatchingServices.Add(MapToServiceInfo(serviceModel));
                }
            }
        }

        // Apply pagination after filtering
        var paginatedServices = allMatchingServices
            .Skip(body.Skip)
            .Take(body.Take)
            .ToList();

        var response = new ListServicesResponse
        {
            Services = paginatedServices,
            TotalCount = allMatchingServices.Count
        };

        _logger.LogDebug("Listed {PageCount} of {TotalCount} services (skip={Skip}, take={Take})",
            paginatedServices.Count, allMatchingServices.Count, body.Skip, body.Take);
        return (StatusCodes.OK, response);
    }

    /// <summary>
    /// Get a service by ID or stub name.
    /// </summary>
    /// <param name="body">Request containing either a service ID (GUID) or stub name for lookup.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>
    /// OK with service info if found, NotFound if service doesn't exist, or InternalServerError if state store fails.
    /// </returns>
    public async Task<(StatusCodes, ServiceInfo?)> GetServiceAsync(GetServiceRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Getting service (serviceId={ServiceId}, stubName={StubName})",
            body.ServiceId, body.StubName);

        GameServiceRegistryModel? serviceModel = null;
        var modelStore = _stateStoreFactory.GetStore<GameServiceRegistryModel>(StateStoreName);
        var stringStore = _stateStoreFactory.GetStore<string>(StateStoreName);

        // Try by service ID first
        if (body.ServiceId.HasValue)
        {
            serviceModel = await modelStore.GetAsync($"{SERVICE_KEY_PREFIX}{body.ServiceId.Value}", cancellationToken);
        }
        // Try by stub name if not found
        else if (!string.IsNullOrWhiteSpace(body.StubName))
        {
            // Get service ID from stub name index
            var serviceId = await stringStore.GetAsync($"{SERVICE_STUB_INDEX_PREFIX}{body.StubName.ToLowerInvariant()}", cancellationToken);

            if (!string.IsNullOrEmpty(serviceId))
            {
                serviceModel = await modelStore.GetAsync($"{SERVICE_KEY_PREFIX}{serviceId}", cancellationToken);
            }
        }

        if (serviceModel == null)
        {
            _logger.LogDebug("Service not found (serviceId={ServiceId}, stubName={StubName})",
                body.ServiceId, body.StubName);
            return (StatusCodes.NotFound, null);
        }

        return (StatusCodes.OK, MapToServiceInfo(serviceModel));
    }

    /// <summary>
    /// Create a new game service entry.
    /// </summary>
    /// <param name="body">Request containing stub name, display name, optional description, and active status.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>
    /// OK with created service info, BadRequest if required fields missing, Conflict if stub name exists,
    /// or InternalServerError if state store fails.
    /// </returns>
    public async Task<(StatusCodes, ServiceInfo?)> CreateServiceAsync(CreateServiceRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Creating service (stubName={StubName}, displayName={DisplayName})",
            body.StubName, body.DisplayName);

        var normalizedStubName = body.StubName.ToLowerInvariant();
        var stringStore = _stateStoreFactory.GetStore<string>(StateStoreName);
        var modelStore = _stateStoreFactory.GetStore<GameServiceRegistryModel>(StateStoreName);

        // Distributed lock on stub name to prevent concurrent creates with same name
        // per IMPLEMENTATION TENETS (Multi-Instance Safety)
        var lockOwner = $"create-game-service-{Guid.NewGuid():N}";
        await using var lockResponse = await _lockProvider.LockAsync(
            StateStoreDefinitions.GameServiceLock,
            $"game-service-stub:{normalizedStubName}",
            lockOwner,
            30,
            cancellationToken);

        if (!lockResponse.Success)
        {
            _logger.LogDebug("Could not acquire lock for stub name {StubName}", normalizedStubName);
            return (StatusCodes.Conflict, null);
        }

        // Check if stub name already exists (under lock)
        var existingServiceId = await stringStore.GetAsync($"{SERVICE_STUB_INDEX_PREFIX}{normalizedStubName}", cancellationToken);

        if (!string.IsNullOrEmpty(existingServiceId))
        {
            _logger.LogDebug("Service with stub name {StubName} already exists", body.StubName);
            return (StatusCodes.Conflict, null);
        }

        // Create new service
        var serviceId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        var serviceModel = new GameServiceRegistryModel
        {
            ServiceId = serviceId,
            StubName = normalizedStubName,
            DisplayName = body.DisplayName,
            Description = body.Description,
            IsActive = body.IsActive,
            AutoLobbyEnabled = body.AutoLobbyEnabled,
            CreatedAtUnix = now.ToUnixTimeSeconds(),
            UpdatedAtUnix = null
        };

        // Save service data
        await modelStore.SaveAsync($"{SERVICE_KEY_PREFIX}{serviceId}", serviceModel, cancellationToken: cancellationToken);

        // Create stub name index (stored as string for lookup)
        await stringStore.SaveAsync($"{SERVICE_STUB_INDEX_PREFIX}{normalizedStubName}", serviceId.ToString(), cancellationToken: cancellationToken);

        // Add to service list
        await AddToServiceListAsync(serviceId, cancellationToken);

        _logger.LogInformation("Created service {ServiceId} with stub name {StubName}",
            serviceId, normalizedStubName);

        // Publish lifecycle event for other services (e.g., analytics, achievements)
        await PublishServiceCreatedEventAsync(serviceModel);

        return (StatusCodes.OK, MapToServiceInfo(serviceModel));
    }

    /// <summary>
    /// Update a game service entry.
    /// </summary>
    /// <param name="body">Request containing service ID and optional fields to update (display name, description, active status).</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>
    /// OK with updated service info (even if no changes made),
    /// NotFound if service doesn't exist, or InternalServerError if state store fails.
    /// </returns>
    public async Task<(StatusCodes, ServiceInfo?)> UpdateServiceAsync(UpdateServiceRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Updating service {ServiceId}", body.ServiceId);

        var modelStore = _stateStoreFactory.GetStore<GameServiceRegistryModel>(StateStoreName);

        // Get existing service
        var serviceModel = await modelStore.GetAsync($"{SERVICE_KEY_PREFIX}{body.ServiceId}", cancellationToken);

        if (serviceModel == null)
        {
            _logger.LogDebug("Service {ServiceId} not found", body.ServiceId);
            return (StatusCodes.NotFound, null);
        }

        // Track which fields changed for the update event
        var changedFields = new List<string>();

        // Update fields if provided
        if (!string.IsNullOrEmpty(body.DisplayName) && body.DisplayName != serviceModel.DisplayName)
        {
            serviceModel.DisplayName = body.DisplayName;
            changedFields.Add("displayName");
        }

        if (body.Description != null && body.Description != serviceModel.Description)
        {
            serviceModel.Description = body.Description;
            changedFields.Add("description");
        }

        if (body.IsActive.HasValue && body.IsActive.Value != serviceModel.IsActive)
        {
            serviceModel.IsActive = body.IsActive.Value;
            changedFields.Add("isActive");
        }

        if (body.AutoLobbyEnabled.HasValue && body.AutoLobbyEnabled.Value != serviceModel.AutoLobbyEnabled)
        {
            serviceModel.AutoLobbyEnabled = body.AutoLobbyEnabled.Value;
            changedFields.Add("autoLobbyEnabled");
        }

        // Only save if something actually changed
        if (changedFields.Count == 0)
        {
            _logger.LogDebug("No changes to service {ServiceId}", body.ServiceId);
            return (StatusCodes.OK, MapToServiceInfo(serviceModel));
        }

        serviceModel.UpdatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        // Save updated service
        await modelStore.SaveAsync($"{SERVICE_KEY_PREFIX}{body.ServiceId}", serviceModel, cancellationToken: cancellationToken);

        _logger.LogInformation("Updated service {ServiceId} (changed: {ChangedFields})", body.ServiceId, string.Join(", ", changedFields));

        // Publish lifecycle event for other services
        await PublishServiceUpdatedEventAsync(serviceModel, changedFields);

        return (StatusCodes.OK, MapToServiceInfo(serviceModel));
    }

    /// <summary>
    /// Delete a game service entry.
    /// Checks for external references via lib-resource and returns 409 if references exist.
    /// </summary>
    /// <param name="body">Request containing service ID and optional deletion reason.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>
    /// OK if deleted successfully,
    /// NotFound if service doesn't exist, Conflict if references exist,
    /// or InternalServerError if state store fails.
    /// </returns>
    public async Task<StatusCodes> DeleteServiceAsync(DeleteServiceRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Deleting service {ServiceId}", body.ServiceId);

        var modelStore = _stateStoreFactory.GetStore<GameServiceRegistryModel>(StateStoreName);
        var stringStore = _stateStoreFactory.GetStore<string>(StateStoreName);

        // Get existing service to get stub name for index cleanup
        var serviceModel = await modelStore.GetAsync($"{SERVICE_KEY_PREFIX}{body.ServiceId}", cancellationToken);

        if (serviceModel == null)
        {
            _logger.LogDebug("Service {ServiceId} not found", body.ServiceId);
            return StatusCodes.NotFound;
        }

        // Check for external references and execute cleanup callbacks (per x-resource-lifecycle contract)
        try
        {
            var resourceCheck = await _resourceClient.CheckReferencesAsync(
                new CheckReferencesRequest
                {
                    ResourceType = "game-service",
                    ResourceId = body.ServiceId
                }, cancellationToken);

            if (resourceCheck != null && resourceCheck.RefCount > 0)
            {
                var sourceTypes = string.Join(", ",
                    resourceCheck.Sources?.Select(s => s.SourceType) ?? Enumerable.Empty<string>());
                _logger.LogDebug(
                    "Game service {ServiceId} has {RefCount} external references from: {SourceTypes}, executing cleanup",
                    body.ServiceId, resourceCheck.RefCount, sourceTypes);

                var cleanupResult = await _resourceClient.ExecuteCleanupAsync(
                    new ExecuteCleanupRequest
                    {
                        ResourceType = "game-service",
                        ResourceId = body.ServiceId,
                        CleanupPolicy = CleanupPolicy.ALL_REQUIRED
                    }, cancellationToken);

                if (cleanupResult == null || !cleanupResult.Success)
                {
                    _logger.LogWarning(
                        "Cleanup failed for game service {ServiceId}: {Reason}",
                        body.ServiceId, cleanupResult?.AbortReason ?? "no response");
                    return StatusCodes.Conflict;
                }
            }
        }
        catch (ApiException ex)
        {
            _logger.LogWarning(ex, "Resource service call failed during game service {ServiceId} deletion", body.ServiceId);
            return StatusCodes.Conflict;
        }

        // Delete service data
        await modelStore.DeleteAsync($"{SERVICE_KEY_PREFIX}{body.ServiceId}", cancellationToken);

        // Delete stub name index
        if (!string.IsNullOrEmpty(serviceModel.StubName))
        {
            await stringStore.DeleteAsync($"{SERVICE_STUB_INDEX_PREFIX}{serviceModel.StubName}", cancellationToken);
        }

        // Remove from service list
        await RemoveFromServiceListAsync(body.ServiceId, cancellationToken);

        _logger.LogInformation("Deleted service {ServiceId}", body.ServiceId);

        // Publish lifecycle event for other services (e.g., cleanup subscriptions)
        await PublishServiceDeletedEventAsync(serviceModel, body.Reason);

        return StatusCodes.OK;
    }

    #region Private Helpers

    /// <summary>
    /// Add a service ID to the service list index.
    /// Uses ETag-based optimistic concurrency per IMPLEMENTATION TENETS (Multi-Instance Safety).
    /// </summary>
    /// <param name="serviceId">The service ID to add to the master list.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    private async Task AddToServiceListAsync(Guid serviceId, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.game-service", "GameServiceService.AddToServiceList");

        var listStore = _stateStoreFactory.GetStore<List<Guid>>(StateStoreName);

        for (var attempt = 0; attempt < _configuration.ServiceListRetryAttempts; attempt++)
        {
            var (serviceIds, etag) = await listStore.GetWithETagAsync(SERVICE_LIST_KEY, cancellationToken);
            serviceIds ??= new List<Guid>();

            if (serviceIds.Contains(serviceId))
            {
                return; // Already in list
            }

            serviceIds.Add(serviceId);

            // etag is null when the list key doesn't exist yet; empty string signals
            // "new entry" to TrySaveAsync (will never conflict on new entries)
            var result = await listStore.TrySaveAsync(SERVICE_LIST_KEY, serviceIds, etag ?? string.Empty, cancellationToken);
            if (result != null)
            {
                return;
            }

            _logger.LogDebug("Concurrent modification on service list, retrying add (attempt {Attempt})", attempt + 1);
        }

        _logger.LogWarning("Failed to add service {ServiceId} to list after {MaxAttempts} attempts",
            serviceId, _configuration.ServiceListRetryAttempts);
    }

    /// <summary>
    /// Remove a service ID from the service list index.
    /// Uses ETag-based optimistic concurrency per IMPLEMENTATION TENETS (Multi-Instance Safety).
    /// </summary>
    /// <param name="serviceId">The service ID to remove from the master list.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    private async Task RemoveFromServiceListAsync(Guid serviceId, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.game-service", "GameServiceService.RemoveFromServiceList");

        var listStore = _stateStoreFactory.GetStore<List<Guid>>(StateStoreName);

        for (var attempt = 0; attempt < _configuration.ServiceListRetryAttempts; attempt++)
        {
            var (serviceIds, etag) = await listStore.GetWithETagAsync(SERVICE_LIST_KEY, cancellationToken);
            if (serviceIds == null || !serviceIds.Remove(serviceId))
            {
                return; // Not in list or already removed
            }

            // etag is null when the list key doesn't exist yet; empty string signals
            // "new entry" to TrySaveAsync (will never conflict on new entries)
            var result = await listStore.TrySaveAsync(SERVICE_LIST_KEY, serviceIds, etag ?? string.Empty, cancellationToken);
            if (result != null)
            {
                return;
            }

            _logger.LogDebug("Concurrent modification on service list, retrying remove (attempt {Attempt})", attempt + 1);
        }

        _logger.LogWarning("Failed to remove service {ServiceId} from list after {MaxAttempts} attempts",
            serviceId, _configuration.ServiceListRetryAttempts);
    }

    /// <summary>
    /// Map internal storage model to API response model.
    /// </summary>
    /// <param name="model">The internal storage model to convert.</param>
    /// <returns>A ServiceInfo response model with all fields mapped and timestamps converted from Unix.</returns>
    private static ServiceInfo MapToServiceInfo(GameServiceRegistryModel model)
    {
        return new ServiceInfo
        {
            ServiceId = model.ServiceId,
            StubName = model.StubName,
            DisplayName = model.DisplayName,
            Description = model.Description,
            IsActive = model.IsActive,
            AutoLobbyEnabled = model.AutoLobbyEnabled,
            CreatedAt = DateTimeOffset.FromUnixTimeSeconds(model.CreatedAtUnix),
            UpdatedAt = model.UpdatedAtUnix.HasValue
                ? DateTimeOffset.FromUnixTimeSeconds(model.UpdatedAtUnix.Value)
                : null
        };
    }

    #endregion

    #region Lifecycle Event Publishing

    /// <summary>
    /// Publishes a GameServiceCreatedEvent when a new game service is registered.
    /// </summary>
    /// <param name="model">The created service model to include in the event.</param>
    private async Task PublishServiceCreatedEventAsync(GameServiceRegistryModel model)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.game-service", "GameServiceService.PublishServiceCreatedEvent");

        var eventModel = new GameServiceCreatedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            GameServiceId = model.ServiceId,
            StubName = model.StubName,
            DisplayName = model.DisplayName,
            Description = model.Description,
            IsActive = model.IsActive,
            AutoLobbyEnabled = model.AutoLobbyEnabled,
            CreatedAt = DateTimeOffset.FromUnixTimeSeconds(model.CreatedAtUnix),
            UpdatedAt = model.UpdatedAtUnix.HasValue
                ? DateTimeOffset.FromUnixTimeSeconds(model.UpdatedAtUnix.Value)
                : null
        };
        await _messageBus.TryPublishAsync("game-service.created", eventModel);
    }

    /// <summary>
    /// Publishes a GameServiceUpdatedEvent when a game service is modified.
    /// </summary>
    /// <param name="model">The updated service model to include in the event.</param>
    /// <param name="changedFields">List of field names that were modified.</param>
    private async Task PublishServiceUpdatedEventAsync(GameServiceRegistryModel model, List<string> changedFields)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.game-service", "GameServiceService.PublishServiceUpdatedEvent");

        var eventModel = new GameServiceUpdatedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            GameServiceId = model.ServiceId,
            StubName = model.StubName,
            DisplayName = model.DisplayName,
            Description = model.Description,
            IsActive = model.IsActive,
            AutoLobbyEnabled = model.AutoLobbyEnabled,
            CreatedAt = DateTimeOffset.FromUnixTimeSeconds(model.CreatedAtUnix),
            UpdatedAt = model.UpdatedAtUnix.HasValue
                ? DateTimeOffset.FromUnixTimeSeconds(model.UpdatedAtUnix.Value)
                : null,
            ChangedFields = changedFields
        };
        await _messageBus.TryPublishAsync("game-service.updated", eventModel);
    }

    /// <summary>
    /// Publishes a GameServiceDeletedEvent when a game service is removed.
    /// </summary>
    /// <param name="model">The deleted service model to include in the event.</param>
    /// <param name="reason">Optional reason for deletion provided by the caller.</param>
    private async Task PublishServiceDeletedEventAsync(GameServiceRegistryModel model, string? reason)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.game-service", "GameServiceService.PublishServiceDeletedEvent");

        var eventModel = new GameServiceDeletedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            GameServiceId = model.ServiceId,
            StubName = model.StubName,
            DisplayName = model.DisplayName,
            Description = model.Description,
            IsActive = model.IsActive,
            AutoLobbyEnabled = model.AutoLobbyEnabled,
            CreatedAt = DateTimeOffset.FromUnixTimeSeconds(model.CreatedAtUnix),
            UpdatedAt = model.UpdatedAtUnix.HasValue
                ? DateTimeOffset.FromUnixTimeSeconds(model.UpdatedAtUnix.Value)
                : null,
            DeletedReason = reason
        };
        await _messageBus.TryPublishAsync("game-service.deleted", eventModel);
    }

    #endregion
}
