using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using Dapr.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("lib-servicedata.tests")]

namespace BeyondImmersion.BannouService.Servicedata;

/// <summary>
/// Implementation of the ServiceData service.
/// Provides a minimal registry of game services (games/applications) that users can subscribe to.
/// </summary>
[DaprService("servicedata", typeof(IServicedataService), lifetime: ServiceLifetime.Singleton)]
public class ServicedataService : IServicedataService
{
    private readonly DaprClient _daprClient;
    private readonly ILogger<ServicedataService> _logger;
    private readonly ServicedataServiceConfiguration _configuration;

    // Key patterns for Dapr state store
    private const string SERVICE_KEY_PREFIX = "service:";
    private const string SERVICE_STUB_INDEX_PREFIX = "service-stub:";
    private const string SERVICE_LIST_KEY = "service-list";

    public ServicedataService(
        DaprClient daprClient,
        ILogger<ServicedataService> logger,
        ServicedataServiceConfiguration configuration)
    {
        _daprClient = daprClient ?? throw new ArgumentNullException(nameof(daprClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    }

    private string StateStoreName => _configuration.StateStoreName ?? "servicedata-statestore";

    /// <summary>
    /// List all registered game services, optionally filtered by active status.
    /// </summary>
    public async Task<(StatusCodes, ListServicesResponse?)> ListServicesAsync(ListServicesRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Listing services (activeOnly={ActiveOnly})", body.ActiveOnly);

        try
        {
            // Get all service IDs from the index
            var serviceIds = await _daprClient.GetStateAsync<List<string>>(
                StateStoreName,
                SERVICE_LIST_KEY,
                cancellationToken: cancellationToken);

            var services = new List<ServiceInfo>();

            if (serviceIds != null)
            {
                foreach (var serviceId in serviceIds)
                {
                    var serviceModel = await _daprClient.GetStateAsync<ServiceDataModel>(
                        StateStoreName,
                        $"{SERVICE_KEY_PREFIX}{serviceId}",
                        cancellationToken: cancellationToken);

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
            return (StatusCodes.InternalServerError, new ListServicesResponse { Services = new List<ServiceInfo>(), TotalCount = 0 });
        }
    }

    /// <summary>
    /// Get a service by ID or stub name.
    /// </summary>
    public async Task<(StatusCodes, ServiceInfo?)> GetServiceAsync(GetServiceRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Getting service (serviceId={ServiceId}, stubName={StubName})",
            body.ServiceId, body.StubName);

        try
        {
            ServiceDataModel? serviceModel = null;

            // Try by service ID first
            if (body.ServiceId != Guid.Empty)
            {
                serviceModel = await _daprClient.GetStateAsync<ServiceDataModel>(
                    StateStoreName,
                    $"{SERVICE_KEY_PREFIX}{body.ServiceId}",
                    cancellationToken: cancellationToken);
            }
            // Try by stub name if not found
            else if (!string.IsNullOrWhiteSpace(body.StubName))
            {
                // Get service ID from stub name index
                var serviceId = await _daprClient.GetStateAsync<string>(
                    StateStoreName,
                    $"{SERVICE_STUB_INDEX_PREFIX}{body.StubName.ToLowerInvariant()}",
                    cancellationToken: cancellationToken);

                if (!string.IsNullOrEmpty(serviceId))
                {
                    serviceModel = await _daprClient.GetStateAsync<ServiceDataModel>(
                        StateStoreName,
                        $"{SERVICE_KEY_PREFIX}{serviceId}",
                        cancellationToken: cancellationToken);
                }
            }

            if (serviceModel == null)
            {
                _logger.LogWarning("Service not found (serviceId={ServiceId}, stubName={StubName})",
                    body.ServiceId, body.StubName);
                return (StatusCodes.NotFound, null!);
            }

            return (StatusCodes.OK, MapToServiceInfo(serviceModel));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting service");
            return (StatusCodes.InternalServerError, null!);
        }
    }

    /// <summary>
    /// Create a new game service entry.
    /// </summary>
    public async Task<(StatusCodes, ServiceInfo?)> CreateServiceAsync(CreateServiceRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Creating service (stubName={StubName}, displayName={DisplayName})",
            body.StubName, body.DisplayName);

        try
        {
            // Validate required fields
            if (string.IsNullOrWhiteSpace(body.StubName))
            {
                _logger.LogWarning("Stub name is required");
                return (StatusCodes.BadRequest, null!);
            }

            if (string.IsNullOrWhiteSpace(body.DisplayName))
            {
                _logger.LogWarning("Display name is required");
                return (StatusCodes.BadRequest, null!);
            }

            var normalizedStubName = body.StubName.ToLowerInvariant();

            // Check if stub name already exists
            var existingServiceId = await _daprClient.GetStateAsync<string>(
                StateStoreName,
                $"{SERVICE_STUB_INDEX_PREFIX}{normalizedStubName}",
                cancellationToken: cancellationToken);

            if (!string.IsNullOrEmpty(existingServiceId))
            {
                _logger.LogWarning("Service with stub name {StubName} already exists", body.StubName);
                return (StatusCodes.Conflict, null!);
            }

            // Create new service
            var serviceId = Guid.NewGuid();
            var now = DateTimeOffset.UtcNow;

            var serviceModel = new ServiceDataModel
            {
                ServiceId = serviceId.ToString(),
                StubName = normalizedStubName,
                DisplayName = body.DisplayName,
                Description = body.Description,
                IsActive = body.IsActive,
                CreatedAtUnix = now.ToUnixTimeSeconds(),
                UpdatedAtUnix = null
            };

            // Save service data
            await _daprClient.SaveStateAsync(
                StateStoreName,
                $"{SERVICE_KEY_PREFIX}{serviceId}",
                serviceModel,
                cancellationToken: cancellationToken);

            // Create stub name index
            await _daprClient.SaveStateAsync(
                StateStoreName,
                $"{SERVICE_STUB_INDEX_PREFIX}{normalizedStubName}",
                serviceId.ToString(),
                cancellationToken: cancellationToken);

            // Add to service list
            await AddToServiceListAsync(serviceId.ToString(), cancellationToken);

            _logger.LogInformation("Created service {ServiceId} with stub name {StubName}",
                serviceId, normalizedStubName);

            return (StatusCodes.Created, MapToServiceInfo(serviceModel));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating service");
            return (StatusCodes.InternalServerError, null!);
        }
    }

    /// <summary>
    /// Update a game service entry.
    /// </summary>
    public async Task<(StatusCodes, ServiceInfo?)> UpdateServiceAsync(UpdateServiceRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Updating service {ServiceId}", body.ServiceId);

        try
        {
            if (body.ServiceId == Guid.Empty)
            {
                _logger.LogWarning("Service ID is required");
                return (StatusCodes.BadRequest, null!);
            }

            // Get existing service
            var serviceModel = await _daprClient.GetStateAsync<ServiceDataModel>(
                StateStoreName,
                $"{SERVICE_KEY_PREFIX}{body.ServiceId}",
                cancellationToken: cancellationToken);

            if (serviceModel == null)
            {
                _logger.LogWarning("Service {ServiceId} not found", body.ServiceId);
                return (StatusCodes.NotFound, null!);
            }

            // Update fields if provided
            if (!string.IsNullOrEmpty(body.DisplayName))
            {
                serviceModel.DisplayName = body.DisplayName;
            }

            if (body.Description != null)
            {
                serviceModel.Description = body.Description;
            }

            if (body.IsActive.HasValue)
            {
                serviceModel.IsActive = body.IsActive.Value;
            }

            serviceModel.UpdatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            // Save updated service
            await _daprClient.SaveStateAsync(
                StateStoreName,
                $"{SERVICE_KEY_PREFIX}{body.ServiceId}",
                serviceModel,
                cancellationToken: cancellationToken);

            _logger.LogInformation("Updated service {ServiceId}", body.ServiceId);
            return (StatusCodes.OK, MapToServiceInfo(serviceModel));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating service {ServiceId}", body.ServiceId);
            return (StatusCodes.InternalServerError, null!);
        }
    }

    /// <summary>
    /// Delete a game service entry.
    /// </summary>
    public async Task<(StatusCodes, object?)> DeleteServiceAsync(DeleteServiceRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Deleting service {ServiceId}", body.ServiceId);

        try
        {
            if (body.ServiceId == Guid.Empty)
            {
                _logger.LogWarning("Service ID is required");
                return (StatusCodes.BadRequest, null!);
            }

            // Get existing service to get stub name for index cleanup
            var serviceModel = await _daprClient.GetStateAsync<ServiceDataModel>(
                StateStoreName,
                $"{SERVICE_KEY_PREFIX}{body.ServiceId}",
                cancellationToken: cancellationToken);

            if (serviceModel == null)
            {
                _logger.LogWarning("Service {ServiceId} not found", body.ServiceId);
                return (StatusCodes.NotFound, null!);
            }

            // Delete service data
            await _daprClient.DeleteStateAsync(
                StateStoreName,
                $"{SERVICE_KEY_PREFIX}{body.ServiceId}",
                cancellationToken: cancellationToken);

            // Delete stub name index
            if (!string.IsNullOrEmpty(serviceModel.StubName))
            {
                await _daprClient.DeleteStateAsync(
                    StateStoreName,
                    $"{SERVICE_STUB_INDEX_PREFIX}{serviceModel.StubName}",
                    cancellationToken: cancellationToken);
            }

            // Remove from service list
            await RemoveFromServiceListAsync(body.ServiceId.ToString(), cancellationToken);

            _logger.LogInformation("Deleted service {ServiceId}", body.ServiceId);
            return (StatusCodes.NoContent, null!);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting service {ServiceId}", body.ServiceId);
            return (StatusCodes.InternalServerError, null!);
        }
    }

    #region Private Helpers

    /// <summary>
    /// Add a service ID to the service list index.
    /// </summary>
    private async Task AddToServiceListAsync(string serviceId, CancellationToken cancellationToken)
    {
        var serviceIds = await _daprClient.GetStateAsync<List<string>>(
            StateStoreName,
            SERVICE_LIST_KEY,
            cancellationToken: cancellationToken) ?? new List<string>();

        if (!serviceIds.Contains(serviceId))
        {
            serviceIds.Add(serviceId);
            await _daprClient.SaveStateAsync(
                StateStoreName,
                SERVICE_LIST_KEY,
                serviceIds,
                cancellationToken: cancellationToken);
        }
    }

    /// <summary>
    /// Remove a service ID from the service list index.
    /// </summary>
    private async Task RemoveFromServiceListAsync(string serviceId, CancellationToken cancellationToken)
    {
        var serviceIds = await _daprClient.GetStateAsync<List<string>>(
            StateStoreName,
            SERVICE_LIST_KEY,
            cancellationToken: cancellationToken);

        if (serviceIds != null && serviceIds.Remove(serviceId))
        {
            await _daprClient.SaveStateAsync(
                StateStoreName,
                SERVICE_LIST_KEY,
                serviceIds,
                cancellationToken: cancellationToken);
        }
    }

    /// <summary>
    /// Map internal storage model to API response model.
    /// </summary>
    private static ServiceInfo MapToServiceInfo(ServiceDataModel model)
    {
        return new ServiceInfo
        {
            ServiceId = Guid.Parse(model.ServiceId),
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
    /// Registers this service's API permissions with the Permissions service on startup.
    /// Overrides the default IDaprService implementation to use generated permission data.
    /// </summary>
    public async Task RegisterServicePermissionsAsync()
    {
        _logger.LogInformation("Registering ServiceData service permissions...");
        await ServicedataPermissionRegistration.RegisterViaEventAsync(_daprClient, _logger);
    }

    #endregion
}

/// <summary>
/// Internal storage model using Unix timestamps to avoid Dapr serialization issues.
/// Accessible to test project via InternalsVisibleTo attribute.
/// </summary>
internal class ServiceDataModel
{
    public string ServiceId { get; set; } = string.Empty;
    public string StubName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; }
    public long CreatedAtUnix { get; set; }
    public long? UpdatedAtUnix { get; set; }
}
