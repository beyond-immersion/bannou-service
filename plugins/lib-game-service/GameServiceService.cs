using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.GameService;

/// <summary>
/// Implementation of the Game Service service.
/// Provides a minimal registry of game services (games/applications) that users can subscribe to.
/// </summary>
[BannouService("game-service", typeof(IGameServiceService), lifetime: ServiceLifetime.Singleton)]
public partial class GameServiceService : IGameServiceService
{
    private readonly IStateStoreFactory _stateStoreFactory;
    private readonly IMessageBus _messageBus;
    private readonly ILogger<GameServiceService> _logger;
    private readonly GameServiceServiceConfiguration _configuration;

    // Key patterns for state store
    private const string SERVICE_KEY_PREFIX = "game-service:";
    private const string SERVICE_STUB_INDEX_PREFIX = "game-service-stub:";
    private const string SERVICE_LIST_KEY = "game-service-list";

    public GameServiceService(
        IStateStoreFactory stateStoreFactory,
        IMessageBus messageBus,
        ILogger<GameServiceService> logger,
        GameServiceServiceConfiguration configuration,
        IEventConsumer eventConsumer)
    {
        _stateStoreFactory = stateStoreFactory;
        _messageBus = messageBus;
        _logger = logger;
        _configuration = configuration;

        // Register event handlers via partial class (GameServiceServiceEvents.cs)
        ((IBannouService)this).RegisterEventConsumers(eventConsumer);
    }

    private static string StateStoreName => StateStoreDefinitions.GameService;

    /// <summary>
    /// List all registered game services, optionally filtered by active status.
    /// </summary>
    public async Task<(StatusCodes, ListServicesResponse?)> ListServicesAsync(ListServicesRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Listing services (activeOnly={ActiveOnly})", body.ActiveOnly);

        try
        {
            // Get all service IDs from the index
            var listStore = _stateStoreFactory.GetStore<List<Guid>>(StateStoreName);
            var serviceIds = await listStore.GetAsync(SERVICE_LIST_KEY, cancellationToken);

            var services = new List<ServiceInfo>();
            var modelStore = _stateStoreFactory.GetStore<GameServiceRegistryModel>(StateStoreName);

            if (serviceIds != null)
            {
                foreach (var serviceId in serviceIds)
                {
                    var serviceModel = await modelStore.GetAsync($"{SERVICE_KEY_PREFIX}{serviceId}", cancellationToken);

                    if (serviceModel != null)
                    {
                        // Apply filter
                        if (body.ActiveOnly && !serviceModel.IsActive)
                            continue;

                        services.Add(MapToServiceInfo(serviceModel));
                    }
                }
            }

            var response = new ListServicesResponse
            {
                Services = services,
                TotalCount = services.Count
            };

            _logger.LogDebug("Listed {Count} services", services.Count);
            return (StatusCodes.OK, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing services");
            await PublishErrorEventAsync("ListServices", ex.GetType().Name, ex.Message, dependency: "state");
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Get a service by ID or stub name.
    /// </summary>
    public async Task<(StatusCodes, ServiceInfo?)> GetServiceAsync(GetServiceRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Getting service (serviceId={ServiceId}, stubName={StubName})",
            body.ServiceId, body.StubName);

        try
        {
            GameServiceRegistryModel? serviceModel = null;
            var modelStore = _stateStoreFactory.GetStore<GameServiceRegistryModel>(StateStoreName);
            var stringStore = _stateStoreFactory.GetStore<string>(StateStoreName);

            // Try by service ID first
            if (body.ServiceId.HasValue && body.ServiceId.Value != Guid.Empty)
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting service");
            await PublishErrorEventAsync("GetService", ex.GetType().Name, ex.Message, dependency: "state");
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Create a new game service entry.
    /// </summary>
    public async Task<(StatusCodes, ServiceInfo?)> CreateServiceAsync(CreateServiceRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Creating service (stubName={StubName}, displayName={DisplayName})",
            body.StubName, body.DisplayName);

        try
        {
            // Validate required fields
            if (string.IsNullOrWhiteSpace(body.StubName))
            {
                _logger.LogDebug("Stub name is required");
                return (StatusCodes.BadRequest, null);
            }

            if (string.IsNullOrWhiteSpace(body.DisplayName))
            {
                _logger.LogDebug("Display name is required");
                return (StatusCodes.BadRequest, null);
            }

            var normalizedStubName = body.StubName.ToLowerInvariant();
            var stringStore = _stateStoreFactory.GetStore<string>(StateStoreName);
            var modelStore = _stateStoreFactory.GetStore<GameServiceRegistryModel>(StateStoreName);

            // Check if stub name already exists
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating service");
            await PublishErrorEventAsync("CreateService", ex.GetType().Name, ex.Message, dependency: "state");
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Update a game service entry.
    /// </summary>
    public async Task<(StatusCodes, ServiceInfo?)> UpdateServiceAsync(UpdateServiceRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Updating service {ServiceId}", body.ServiceId);

        try
        {
            if (body.ServiceId == Guid.Empty)
            {
                _logger.LogDebug("Service ID is required");
                return (StatusCodes.BadRequest, null);
            }

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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating service {ServiceId}", body.ServiceId);
            await PublishErrorEventAsync("UpdateService", ex.GetType().Name, ex.Message, dependency: "state");
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Delete a game service entry.
    /// </summary>
    public async Task<StatusCodes> DeleteServiceAsync(DeleteServiceRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Deleting service {ServiceId}", body.ServiceId);

        try
        {
            if (body.ServiceId == Guid.Empty)
            {
                _logger.LogDebug("Service ID is required");
                return StatusCodes.BadRequest;
            }

            var modelStore = _stateStoreFactory.GetStore<GameServiceRegistryModel>(StateStoreName);
            var stringStore = _stateStoreFactory.GetStore<string>(StateStoreName);

            // Get existing service to get stub name for index cleanup
            var serviceModel = await modelStore.GetAsync($"{SERVICE_KEY_PREFIX}{body.ServiceId}", cancellationToken);

            if (serviceModel == null)
            {
                _logger.LogDebug("Service {ServiceId} not found", body.ServiceId);
                return StatusCodes.NotFound;
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting service {ServiceId}", body.ServiceId);
            await PublishErrorEventAsync("DeleteService", ex.GetType().Name, ex.Message, dependency: "state");
            return StatusCodes.InternalServerError;
        }
    }

    #region Private Helpers

    /// <summary>
    /// Add a service ID to the service list index.
    /// Uses ETag-based optimistic concurrency per IMPLEMENTATION TENETS (Multi-Instance Safety).
    /// </summary>
    private async Task AddToServiceListAsync(Guid serviceId, CancellationToken cancellationToken)
    {
        var listStore = _stateStoreFactory.GetStore<List<Guid>>(StateStoreName);

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var (serviceIds, etag) = await listStore.GetWithETagAsync(SERVICE_LIST_KEY, cancellationToken);
            serviceIds ??= new List<Guid>();

            if (serviceIds.Contains(serviceId))
            {
                return; // Already in list
            }

            serviceIds.Add(serviceId);
            var result = await listStore.TrySaveAsync(SERVICE_LIST_KEY, serviceIds, etag ?? string.Empty, cancellationToken);
            if (result != null)
            {
                return;
            }

            _logger.LogDebug("Concurrent modification on service list, retrying add (attempt {Attempt})", attempt + 1);
        }

        _logger.LogWarning("Failed to add service {ServiceId} to list after 3 attempts", serviceId);
    }

    /// <summary>
    /// Remove a service ID from the service list index.
    /// Uses ETag-based optimistic concurrency per IMPLEMENTATION TENETS (Multi-Instance Safety).
    /// </summary>
    private async Task RemoveFromServiceListAsync(Guid serviceId, CancellationToken cancellationToken)
    {
        var listStore = _stateStoreFactory.GetStore<List<Guid>>(StateStoreName);

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var (serviceIds, etag) = await listStore.GetWithETagAsync(SERVICE_LIST_KEY, cancellationToken);
            if (serviceIds == null || !serviceIds.Remove(serviceId))
            {
                return; // Not in list or already removed
            }

            var result = await listStore.TrySaveAsync(SERVICE_LIST_KEY, serviceIds, etag ?? string.Empty, cancellationToken);
            if (result != null)
            {
                return;
            }

            _logger.LogDebug("Concurrent modification on service list, retrying remove (attempt {Attempt})", attempt + 1);
        }

        _logger.LogWarning("Failed to remove service {ServiceId} from list after 3 attempts", serviceId);
    }

    /// <summary>
    /// Map internal storage model to API response model.
    /// </summary>
    private static ServiceInfo MapToServiceInfo(GameServiceRegistryModel model)
    {
        return new ServiceInfo
        {
            ServiceId = model.ServiceId,
            StubName = model.StubName,
            DisplayName = model.DisplayName,
            Description = model.Description,
            IsActive = model.IsActive,
            CreatedAt = DateTimeOffset.FromUnixTimeSeconds(model.CreatedAtUnix),
            UpdatedAt = model.UpdatedAtUnix.HasValue
                ? DateTimeOffset.FromUnixTimeSeconds(model.UpdatedAtUnix.Value)
                : null
        };
    }

    #endregion

    #region Service Registration

    /// <summary>
    /// Registers this service's API permissions with the Permission service on startup.
    /// Overrides the default IBannouService implementation to use generated permission data.
    /// </summary>
    public async Task RegisterServicePermissionsAsync(string appId)
    {
        _logger.LogInformation("Registering Game Service service permissions...");
        await GameServicePermissionRegistration.RegisterViaEventAsync(_messageBus, appId, _logger);
    }

    #endregion

    #region Error Event Publishing

    /// <summary>
    /// Publishes an error event for unexpected/internal failures.
    /// Does NOT publish for validation errors or expected failure cases.
    /// </summary>
    private async Task PublishErrorEventAsync(
        string operation,
        string errorType,
        string message,
        string? dependency = null,
        object? details = null)
    {
        await _messageBus.TryPublishErrorAsync(
            serviceName: "game-service",
            operation: operation,
            errorType: errorType,
            message: message,
            dependency: dependency,
            details: details);
    }

    #endregion

    #region Lifecycle Event Publishing

    /// <summary>
    /// Publishes a GameServiceCreatedEvent when a new game service is registered.
    /// </summary>
    private async Task PublishServiceCreatedEventAsync(GameServiceRegistryModel model)
    {
        try
        {
            var eventModel = new GameServiceCreatedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                GameServiceId = model.ServiceId,
                StubName = model.StubName,
                DisplayName = model.DisplayName,
                Description = model.Description,
                IsActive = model.IsActive,
                CreatedAt = DateTimeOffset.FromUnixTimeSeconds(model.CreatedAtUnix),
                UpdatedAt = model.UpdatedAtUnix.HasValue
                    ? DateTimeOffset.FromUnixTimeSeconds(model.UpdatedAtUnix.Value)
                    : null
            };
            await _messageBus.TryPublishAsync("game-service.created", eventModel);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish GameServiceCreatedEvent for {ServiceId}", model.ServiceId);
        }
    }

    /// <summary>
    /// Publishes a GameServiceUpdatedEvent when a game service is modified.
    /// </summary>
    private async Task PublishServiceUpdatedEventAsync(GameServiceRegistryModel model, List<string> changedFields)
    {
        try
        {
            var eventModel = new GameServiceUpdatedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                GameServiceId = model.ServiceId,
                StubName = model.StubName,
                DisplayName = model.DisplayName,
                Description = model.Description,
                IsActive = model.IsActive,
                CreatedAt = DateTimeOffset.FromUnixTimeSeconds(model.CreatedAtUnix),
                UpdatedAt = model.UpdatedAtUnix.HasValue
                    ? DateTimeOffset.FromUnixTimeSeconds(model.UpdatedAtUnix.Value)
                    : null,
                ChangedFields = changedFields
            };
            await _messageBus.TryPublishAsync("game-service.updated", eventModel);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish GameServiceUpdatedEvent for {ServiceId}", model.ServiceId);
        }
    }

    /// <summary>
    /// Publishes a GameServiceDeletedEvent when a game service is removed.
    /// </summary>
    private async Task PublishServiceDeletedEventAsync(GameServiceRegistryModel model, string? reason)
    {
        try
        {
            var eventModel = new GameServiceDeletedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                GameServiceId = model.ServiceId,
                StubName = model.StubName,
                DisplayName = model.DisplayName,
                Description = model.Description,
                IsActive = model.IsActive,
                CreatedAt = DateTimeOffset.FromUnixTimeSeconds(model.CreatedAtUnix),
                UpdatedAt = model.UpdatedAtUnix.HasValue
                    ? DateTimeOffset.FromUnixTimeSeconds(model.UpdatedAtUnix.Value)
                    : null,
                DeletedReason = reason
            };
            await _messageBus.TryPublishAsync("game-service.deleted", eventModel);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish GameServiceDeletedEvent for {ServiceId}", model.ServiceId);
        }
    }

    #endregion
}

/// <summary>
/// Internal storage model using Unix timestamps to avoid serialization issues.
/// Accessible to test project via InternalsVisibleTo attribute.
/// Uses Guid for ServiceId per IMPLEMENTATION TENETS (Type Safety).
/// </summary>
internal class GameServiceRegistryModel
{
    public Guid ServiceId { get; set; }
    public string StubName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; }
    public long CreatedAtUnix { get; set; }
    public long? UpdatedAtUnix { get; set; }
}
