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
[BannouService("game-service", typeof(IGameServiceService), lifetime: ServiceLifetime.Singleton, layer: ServiceLayer.GameFoundation)]
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

    /// <summary>
    /// Update a game service entry.
    /// </summary>
    /// <param name="body">Request containing service ID and optional fields to update (display name, description, active status).</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>
    /// OK with updated service info (even if no changes made), BadRequest if service ID missing,
    /// NotFound if service doesn't exist, or InternalServerError if state store fails.
    /// </returns>
    public async Task<(StatusCodes, ServiceInfo?)> UpdateServiceAsync(UpdateServiceRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Updating service {ServiceId}", body.ServiceId);

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

    /// <summary>
    /// Delete a game service entry.
    /// </summary>
    /// <param name="body">Request containing service ID and optional deletion reason.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>
    /// OK if deleted successfully, BadRequest if service ID missing,
    /// NotFound if service doesn't exist, or InternalServerError if state store fails.
    /// </returns>
    public async Task<StatusCodes> DeleteServiceAsync(DeleteServiceRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Deleting service {ServiceId}", body.ServiceId);

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

    #region Private Helpers

    /// <summary>
    /// Add a service ID to the service list index.
    /// Uses ETag-based optimistic concurrency per IMPLEMENTATION TENETS (Multi-Instance Safety).
    /// </summary>
    /// <param name="serviceId">The service ID to add to the master list.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
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
    /// <param name="serviceId">The service ID to remove from the master list.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
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
            CreatedAt = DateTimeOffset.FromUnixTimeSeconds(model.CreatedAtUnix),
            UpdatedAt = model.UpdatedAtUnix.HasValue
                ? DateTimeOffset.FromUnixTimeSeconds(model.UpdatedAtUnix.Value)
                : null
        };
    }

    #endregion

    #region Service Registration

    #endregion

    #region Error Event Publishing

    /// <summary>
    /// Publishes an error event for unexpected/internal failures.
    /// Does NOT publish for validation errors or expected failure cases.
    /// </summary>
    /// <param name="operation">The operation that failed (e.g., "CreateService").</param>
    /// <param name="errorType">The type of error (e.g., exception type name).</param>
    /// <param name="message">The error message.</param>
    /// <param name="dependency">Optional dependency that caused the failure (e.g., "state").</param>
    /// <param name="details">Optional additional details about the error.</param>
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
    /// <param name="model">The created service model to include in the event.</param>
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
    /// <param name="model">The updated service model to include in the event.</param>
    /// <param name="changedFields">List of field names that were modified.</param>
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
    /// <param name="model">The deleted service model to include in the event.</param>
    /// <param name="reason">Optional reason for deletion provided by the caller.</param>
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
