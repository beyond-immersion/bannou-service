using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.Logging;
// Type aliases for Orchestrator types
using BackendType = BeyondImmersion.BannouService.Orchestrator.BackendType;
using ContainerRestartRequest = BeyondImmersion.BannouService.Orchestrator.ContainerRestartRequest;
using ContainerRestartResponse = BeyondImmersion.BannouService.Orchestrator.ContainerRestartResponse;
using ContainerRestartResponseRestartStrategy = BeyondImmersion.BannouService.Orchestrator.ContainerRestartResponseRestartStrategy;
using ContainerStatus = BeyondImmersion.BannouService.Orchestrator.ContainerStatus;
using ContainerStatusStatus = BeyondImmersion.BannouService.Orchestrator.ContainerStatusStatus;
using OrchestratorServiceConfiguration = BeyondImmersion.BannouService.Orchestrator.OrchestratorServiceConfiguration;
using RestartHistoryEntry = BeyondImmersion.BannouService.Orchestrator.RestartHistoryEntry;
using RestartPriority = BeyondImmersion.BannouService.Orchestrator.RestartPriority;

namespace LibOrchestrator.Backends;

/// <summary>
/// Container orchestrator implementation for Portainer.
/// Portainer acts as a reverse-proxy to the Docker API.
/// See docs/ORCHESTRATOR-SDK-REFERENCE.md for API documentation.
///
/// Key architectural note: Portainer does NOT expose specific container management endpoints.
/// Instead, it proxies requests to the Docker API:
///   Portainer: /api/endpoints/{endpointId}/docker/{docker_api_path}
///   Maps to:   Docker API: /{docker_api_path}
/// </summary>
public class PortainerOrchestrator : IContainerOrchestrator
{
    private readonly ILogger<PortainerOrchestrator> _logger;
    private readonly OrchestratorServiceConfiguration _configuration;
    private readonly HttpClient _httpClient;
    private readonly int _endpointId;
    private readonly ITelemetryProvider _telemetryProvider;

    /// <summary>
    /// Label used to identify app-id on containers.
    /// </summary>
    private const string BANNOU_APP_ID_LABEL = "bannou.app-id";

    // NOTE: Portainer/Docker API uses camelCase - intentionally NOT using BannouJson.Options
    // which is designed for internal Bannou service communication
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public PortainerOrchestrator(
        ILogger<PortainerOrchestrator> logger,
        OrchestratorServiceConfiguration configuration,
        IHttpClientFactory httpClientFactory,
        ITelemetryProvider telemetryProvider)
    {
        _logger = logger;
        _configuration = configuration;
        ArgumentNullException.ThrowIfNull(telemetryProvider, nameof(telemetryProvider));
        _telemetryProvider = telemetryProvider;

        var portainerUrl = configuration.PortainerUrl
            ?? throw new InvalidOperationException("PORTAINER_URL not configured");

        _httpClient = httpClientFactory.CreateClient("Portainer");
        _httpClient.BaseAddress = new Uri(portainerUrl);
        _httpClient.Timeout = TimeSpan.FromSeconds(30);

        // Add API key if configured
        // See ORCHESTRATOR-SDK-REFERENCE.md for authentication options
        var apiKey = configuration.PortainerApiKey;
        if (!string.IsNullOrEmpty(apiKey))
        {
            _httpClient.DefaultRequestHeaders.Add("X-API-Key", apiKey);
        }

        // Get endpoint ID from config (defaults to 1 for single-endpoint setups)
        _endpointId = configuration.PortainerEndpointId;
    }

    /// <inheritdoc />
    public BackendType BackendType => BackendType.Portainer;

    /// <inheritdoc />
    public async Task<(bool Available, string Message)> CheckAvailabilityAsync(CancellationToken cancellationToken = default)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.orchestrator", "PortainerOrchestrator.CheckAvailabilityAsync");
        try
        {
            // GET /api/status - see ORCHESTRATOR-SDK-REFERENCE.md
            var response = await _httpClient.GetAsync("/api/status", cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return (false, $"Portainer returned {response.StatusCode}");
            }

            var status = await response.Content.ReadFromJsonAsync<PortainerStatus>(JsonOptions, cancellationToken);
            return (true, $"Portainer {status?.Version ?? "unknown"}");
        }
        catch (Exception ex)
        {
            return (false, $"Portainer not available: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<ContainerStatus> GetContainerStatusAsync(string appName, CancellationToken cancellationToken = default)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.orchestrator", "PortainerOrchestrator.GetContainerStatusAsync");
        _logger.LogDebug("Getting Portainer container status for app: {AppName}", appName);

        try
        {
            var container = await FindContainerByAppNameAsync(appName, cancellationToken);
            if (container == null)
            {
                return new ContainerStatus
                {
                    AppName = appName,
                    Status = ContainerStatusStatus.Stopped,
                    Timestamp = DateTimeOffset.UtcNow,
                    Instances = 0
                };
            }

            return MapContainerToStatus(container, appName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting Portainer container status for {AppName}", appName);
            return new ContainerStatus
            {
                AppName = appName,
                Status = ContainerStatusStatus.Unhealthy,
                Timestamp = DateTimeOffset.UtcNow,
                Instances = 0
            };
        }
    }

    /// <inheritdoc />
    public async Task<ContainerRestartResponse> RestartContainerAsync(
        string appName,
        ContainerRestartRequest request,
        CancellationToken cancellationToken = default)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.orchestrator", "PortainerOrchestrator.RestartContainerAsync");
        _logger.LogInformation(
            "Restarting Portainer container for app: {AppName}, Priority: {Priority}, Reason: {Reason}",
            appName,
            request.Priority,
            request.Reason);

        try
        {
            var container = await FindContainerByAppNameAsync(appName, cancellationToken);
            if (container == null)
            {
                return new ContainerRestartResponse
                {
                    Accepted = false,
                    AppName = appName,
                    Message = $"No container found with app-id '{appName}'"
                };
            }

            // POST /api/endpoints/{id}/docker/containers/{containerId}/restart
            // See ORCHESTRATOR-SDK-REFERENCE.md for Portainer Docker proxy pattern
            var url = $"/api/endpoints/{_endpointId}/docker/containers/{container.Id}/restart";

            // Add timeout parameter for graceful shutdown
            var timeout = request.Priority == RestartPriority.Immediate ? 0 : request.ShutdownGracePeriod;
            if (timeout > 0)
            {
                url += $"?t={timeout}";
            }

            var response = await _httpClient.PostAsync(url, null, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(cancellationToken);
                return new ContainerRestartResponse
                {
                    Accepted = false,
                    AppName = appName,
                    Message = $"Portainer API error: {response.StatusCode} - {error}"
                };
            }

            _logger.LogInformation(
                "Portainer container {ContainerId} restarted successfully",
                container.Id[..12]);

            return new ContainerRestartResponse
            {
                Accepted = true,
                AppName = appName,
                ScheduledFor = DateTimeOffset.UtcNow,
                CurrentInstances = 1,
                RestartStrategy = ContainerRestartResponseRestartStrategy.Simultaneous,
                Message = $"Container {container.Id[..12]} restarted successfully"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error restarting Portainer container for {AppName}", appName);
            return new ContainerRestartResponse
            {
                Accepted = false,
                AppName = appName,
                Message = $"Error: {ex.Message}"
            };
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ContainerStatus>> ListContainersAsync(CancellationToken cancellationToken = default)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.orchestrator", "PortainerOrchestrator.ListContainersAsync");
        _logger.LogDebug("Listing all Portainer containers");

        try
        {
            // GET /api/endpoints/{id}/docker/containers/json?all=true
            // See ORCHESTRATOR-SDK-REFERENCE.md for Docker proxy pattern
            var url = $"/api/endpoints/{_endpointId}/docker/containers/json?all=true";
            var response = await _httpClient.GetAsync(url, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Portainer API returned {StatusCode}", response.StatusCode);
                return Array.Empty<ContainerStatus>();
            }

            var containers = await response.Content.ReadFromJsonAsync<List<PortainerContainer>>(
                JsonOptions,
                cancellationToken) ?? new List<PortainerContainer>();

            // Filter to containers with app-id label
            return containers
                .Where(c => c.Labels?.ContainsKey(BANNOU_APP_ID_LABEL) == true)
                .Select(c => MapContainerToStatus(c, GetAppNameFromContainer(c)))
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing Portainer containers");
            return Array.Empty<ContainerStatus>();
        }
    }

    /// <inheritdoc />
    public async Task<string> GetContainerLogsAsync(
        string appName,
        int tail = 100,
        DateTimeOffset? since = null,
        CancellationToken cancellationToken = default)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.orchestrator", "PortainerOrchestrator.GetContainerLogsAsync");
        _logger.LogDebug("Getting Portainer container logs for app: {AppName}, tail: {Tail}", appName, tail);

        try
        {
            var container = await FindContainerByAppNameAsync(appName, cancellationToken);
            if (container == null)
            {
                return $"No container found with app-id '{appName}'";
            }

            // GET /api/endpoints/{id}/docker/containers/{containerId}/logs
            // See ORCHESTRATOR-SDK-REFERENCE.md for Docker proxy pattern
            var url = $"/api/endpoints/{_endpointId}/docker/containers/{container.Id}/logs" +
                    $"?stdout=true&stderr=true&tail={tail}&timestamps=true";

            if (since.HasValue)
            {
                url += $"&since={since.Value.ToUnixTimeSeconds()}";
            }

            var response = await _httpClient.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return $"Portainer API error: {response.StatusCode}";
            }

            return await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting Portainer container logs for {AppName}", appName);
            return $"Error retrieving logs: {ex.Message}";
        }
    }

    /// <summary>
    /// Finds a container by its app-id label.
    /// </summary>
    private async Task<PortainerContainer?> FindContainerByAppNameAsync(
        string appName,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.orchestrator", "PortainerOrchestrator.FindContainerByAppNameAsync");
        // Get all containers and filter by label
        // Portainer's Docker proxy supports filters but the format is complex
        var url = $"/api/endpoints/{_endpointId}/docker/containers/json?all=true";
        var response = await _httpClient.GetAsync(url, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var containers = await response.Content.ReadFromJsonAsync<List<PortainerContainer>>(
            JsonOptions,
            cancellationToken) ?? new List<PortainerContainer>();

        return containers.FirstOrDefault(c =>
            c.Labels?.TryGetValue(BANNOU_APP_ID_LABEL, out var label) == true &&
            label == appName);
    }

    /// <summary>
    /// Extracts app-id from container labels.
    /// </summary>
    private static string GetAppNameFromContainer(PortainerContainer container)
    {
        if (container.Labels?.TryGetValue(BANNOU_APP_ID_LABEL, out var appId) == true)
        {
            return appId;
        }

        // Fallback to container name
        return container.Names?.FirstOrDefault()?.TrimStart('/') ?? container.Id[..12];
    }

    /// <summary>
    /// Maps a Portainer container to our ContainerStatus model.
    /// </summary>
    private static ContainerStatus MapContainerToStatus(PortainerContainer container, string appName)
    {
        var dockerState = container.State?.ToLowerInvariant() ?? "";
        var status = dockerState switch
        {
            "running" => ContainerStatusStatus.Running,
            "restarting" => ContainerStatusStatus.Starting,
            "exited" => ContainerStatusStatus.Stopped,
            "dead" => ContainerStatusStatus.Unhealthy,
            "created" => ContainerStatusStatus.Stopped,
            "paused" => ContainerStatusStatus.Stopping,
            _ => ContainerStatusStatus.Unhealthy
        };

        return new ContainerStatus
        {
            AppName = appName,
            Status = status,
            Timestamp = DateTimeOffset.UtcNow,
            Instances = status == ContainerStatusStatus.Running ? 1 : 0,
            RestartHistory = new List<RestartHistoryEntry>()
        };
    }

    /// <inheritdoc />
    public async Task<DeployServiceResult> DeployServiceAsync(
        string serviceName,
        string appId,
        Dictionary<string, string>? environment = null,
        CancellationToken cancellationToken = default)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.orchestrator", "PortainerOrchestrator.DeployServiceAsync");
        _logger.LogInformation(
            "Deploying Portainer container: {ServiceName} with app-id: {AppId}",
            serviceName, appId);

        try
        {
            var imageName = _configuration.DockerImageName;

            // Prepare environment variables
            var envList = new List<string>
            {
                $"{serviceName.ToUpperInvariant()}_SERVICE_ENABLED=true",
                $"BANNOU_APP_ID={appId}",
                // Required for proper service operation - not forwarded from orchestrator ENV
                "DAEMON_MODE=true",
                "BANNOU_HEARTBEAT_ENABLED=true"
            };

            if (environment != null)
            {
                envList.AddRange(environment.Select(kv => $"{kv.Key}={kv.Value}"));
            }

            // POST /api/endpoints/{id}/docker/containers/create
            // See ORCHESTRATOR-SDK-REFERENCE.md for Portainer Docker proxy pattern
            var createRequest = new
            {
                Image = imageName,
                name = $"bannou-{serviceName}-{appId}",
                Env = envList,
                Labels = new Dictionary<string, string>
                {
                    [BANNOU_APP_ID_LABEL] = appId,
                    ["bannou.service"] = serviceName
                },
                HostConfig = new
                {
                    RestartPolicy = new { Name = "unless-stopped" }
                }
            };

            var createUrl = $"/api/endpoints/{_endpointId}/docker/containers/create?name=bannou-{serviceName}-{appId}";
            var createResponse = await _httpClient.PostAsJsonAsync(createUrl, createRequest, cancellationToken);

            if (!createResponse.IsSuccessStatusCode)
            {
                var error = await createResponse.Content.ReadAsStringAsync(cancellationToken);
                return new DeployServiceResult
                {
                    Success = false,
                    AppId = appId,
                    Message = $"Portainer API error creating container: {createResponse.StatusCode} - {error}"
                };
            }

            var createResult = await createResponse.Content.ReadFromJsonAsync<PortainerCreateContainerResponse>(
                JsonOptions, cancellationToken);

            // Start the container
            var startUrl = $"/api/endpoints/{_endpointId}/docker/containers/{createResult?.Id}/start";
            var startResponse = await _httpClient.PostAsync(startUrl, null, cancellationToken);

            if (!startResponse.IsSuccessStatusCode)
            {
                var error = await startResponse.Content.ReadAsStringAsync(cancellationToken);
                return new DeployServiceResult
                {
                    Success = false,
                    AppId = appId,
                    ContainerId = createResult?.Id,
                    Message = $"Portainer API error starting container: {startResponse.StatusCode} - {error}"
                };
            }

            _logger.LogInformation(
                "Portainer container {ServiceName} deployed successfully: {ContainerId}",
                serviceName, createResult?.Id?[..12]);

            return new DeployServiceResult
            {
                Success = true,
                AppId = appId,
                ContainerId = createResult?.Id,
                Message = $"Container {createResult?.Id?[..12]} created and started"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deploying Portainer container {ServiceName}", serviceName);
            return new DeployServiceResult
            {
                Success = false,
                AppId = appId,
                Message = $"Error: {ex.Message}"
            };
        }
    }

    /// <inheritdoc />
    public async Task<TeardownServiceResult> TeardownServiceAsync(
        string appName,
        bool removeVolumes = false,
        CancellationToken cancellationToken = default)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.orchestrator", "PortainerOrchestrator.TeardownServiceAsync");
        _logger.LogInformation(
            "Tearing down Portainer container: {AppName}, removeVolumes: {RemoveVolumes}",
            appName, removeVolumes);

        try
        {
            var container = await FindContainerByAppNameAsync(appName, cancellationToken);
            if (container == null)
            {
                return new TeardownServiceResult
                {
                    Success = false,
                    AppId = appName,
                    Message = $"No container found with app-id '{appName}'"
                };
            }

            // Stop the container first
            var stopUrl = $"/api/endpoints/{_endpointId}/docker/containers/{container.Id}/stop";
            var stopResponse = await _httpClient.PostAsync(stopUrl, null, cancellationToken);

            // Remove the container
            var removeUrl = $"/api/endpoints/{_endpointId}/docker/containers/{container.Id}?v={removeVolumes.ToString().ToLowerInvariant()}";
            var removeResponse = await _httpClient.DeleteAsync(removeUrl, cancellationToken);

            if (!removeResponse.IsSuccessStatusCode)
            {
                var error = await removeResponse.Content.ReadAsStringAsync(cancellationToken);
                return new TeardownServiceResult
                {
                    Success = false,
                    AppId = appName,
                    Message = $"Portainer API error removing container: {removeResponse.StatusCode} - {error}"
                };
            }

            _logger.LogInformation(
                "Portainer container {AppName} torn down successfully: {ContainerId}",
                appName, container.Id[..12]);

            return new TeardownServiceResult
            {
                Success = true,
                AppId = appName,
                StoppedContainers = new List<string> { container.Id[..12] },
                Message = $"Container {container.Id[..12]} stopped and removed"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error tearing down Portainer container {AppName}", appName);
            return new TeardownServiceResult
            {
                Success = false,
                AppId = appName,
                Message = $"Error: {ex.Message}"
            };
        }
    }

    /// <inheritdoc />
    public async Task<ScaleServiceResult> ScaleServiceAsync(
        string appName,
        int replicas,
        CancellationToken cancellationToken = default)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.orchestrator", "PortainerOrchestrator.ScaleServiceAsync");
        await Task.CompletedTask;
        _logger.LogWarning(
            "Scale operation not supported in Portainer standalone mode for {AppName}. Use Swarm or Kubernetes for scaling.",
            appName);

        // Portainer with standalone Docker doesn't support native scaling
        // For scaling, use Swarm mode or Kubernetes backend
        return new ScaleServiceResult
        {
            Success = false,
            AppId = appName,
            PreviousReplicas = 1,
            CurrentReplicas = 1,
            Message = "Scaling not supported in Portainer standalone mode. Use Docker Swarm or Kubernetes for scaling capabilities."
        };
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> ListInfrastructureServicesAsync(CancellationToken cancellationToken = default)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.orchestrator", "PortainerOrchestrator.ListInfrastructureServicesAsync");
        _logger.LogDebug("Listing infrastructure services (Portainer mode)");

        try
        {
            // In Portainer mode, identify infrastructure by container name patterns
            var infrastructurePatterns = new[] { "redis", "rabbitmq", "mysql", "mariadb", "postgres", "mongodb" };

            var containers = await ListContainersAsync(cancellationToken);

            var infrastructureServices = containers
                .Where(c =>
                {
                    var name = c.AppName ?? "";
                    return infrastructurePatterns.Any(pattern =>
                        name.Contains(pattern, StringComparison.OrdinalIgnoreCase));
                })
                .Select(c => c.AppName ?? "")
                .Where(n => !string.IsNullOrEmpty(n))
                .ToList();

            _logger.LogInformation(
                "Found {Count} infrastructure services: {Services}",
                infrastructureServices.Count,
                string.Join(", ", infrastructureServices));

            return infrastructureServices;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing infrastructure services");
            return Array.Empty<string>();
        }
    }

    /// <inheritdoc />
    public async Task<PruneResult> PruneNetworksAsync(CancellationToken cancellationToken = default)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.orchestrator", "PortainerOrchestrator.PruneNetworksAsync");
        _logger.LogInformation("Pruning unused networks via Portainer");

        try
        {
            // POST /api/endpoints/{id}/docker/networks/prune
            var url = $"/api/endpoints/{_endpointId}/docker/networks/prune";
            var response = await _httpClient.PostAsync(url, null, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(cancellationToken);
                return new PruneResult
                {
                    Success = false,
                    Message = $"Portainer API error: {response.StatusCode} - {error}"
                };
            }

            var result = await response.Content.ReadFromJsonAsync<PortainerNetworksPruneResponse>(
                JsonOptions, cancellationToken);

            var deletedNetworks = result?.NetworksDeleted ?? new List<string>();

            _logger.LogInformation(
                "Pruned {Count} unused networks via Portainer",
                deletedNetworks.Count);

            return new PruneResult
            {
                Success = true,
                DeletedItems = deletedNetworks.ToList(),
                DeletedCount = deletedNetworks.Count,
                ReclaimedBytes = 0,
                Message = deletedNetworks.Count > 0
                    ? $"Pruned {deletedNetworks.Count} unused network(s)"
                    : "No unused networks to prune"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error pruning networks via Portainer");
            return new PruneResult
            {
                Success = false,
                Message = $"Error pruning networks: {ex.Message}"
            };
        }
    }

    /// <inheritdoc />
    public async Task<PruneResult> PruneVolumesAsync(CancellationToken cancellationToken = default)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.orchestrator", "PortainerOrchestrator.PruneVolumesAsync");
        _logger.LogInformation("Pruning unused volumes via Portainer");

        try
        {
            // POST /api/endpoints/{id}/docker/volumes/prune
            var url = $"/api/endpoints/{_endpointId}/docker/volumes/prune";
            var response = await _httpClient.PostAsync(url, null, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(cancellationToken);
                return new PruneResult
                {
                    Success = false,
                    Message = $"Portainer API error: {response.StatusCode} - {error}"
                };
            }

            var result = await response.Content.ReadFromJsonAsync<PortainerVolumesPruneResponse>(
                JsonOptions, cancellationToken);

            var deletedVolumes = result?.VolumesDeleted ?? new List<string>();
            var reclaimedBytes = (long)(result?.SpaceReclaimed ?? 0);

            _logger.LogInformation(
                "Pruned {Count} unused volumes via Portainer, reclaimed {Bytes} bytes",
                deletedVolumes.Count,
                reclaimedBytes);

            return new PruneResult
            {
                Success = true,
                DeletedItems = deletedVolumes.ToList(),
                DeletedCount = deletedVolumes.Count,
                ReclaimedBytes = reclaimedBytes,
                Message = deletedVolumes.Count > 0
                    ? $"Pruned {deletedVolumes.Count} unused volume(s), reclaimed {FormatBytes(reclaimedBytes)}"
                    : "No unused volumes to prune"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error pruning volumes via Portainer");
            return new PruneResult
            {
                Success = false,
                Message = $"Error pruning volumes: {ex.Message}"
            };
        }
    }

    /// <inheritdoc />
    public async Task<PruneResult> PruneImagesAsync(CancellationToken cancellationToken = default)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.orchestrator", "PortainerOrchestrator.PruneImagesAsync");
        _logger.LogInformation("Pruning dangling images via Portainer");

        try
        {
            // POST /api/endpoints/{id}/docker/images/prune
            var url = $"/api/endpoints/{_endpointId}/docker/images/prune";
            var response = await _httpClient.PostAsync(url, null, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(cancellationToken);
                return new PruneResult
                {
                    Success = false,
                    Message = $"Portainer API error: {response.StatusCode} - {error}"
                };
            }

            var result = await response.Content.ReadFromJsonAsync<PortainerImagesPruneResponse>(
                JsonOptions, cancellationToken);

            var deletedImages = result?.ImagesDeleted ?? new List<PortainerImageDeleteResponse>();
            var reclaimedBytes = (long)(result?.SpaceReclaimed ?? 0);

            var deletedIds = deletedImages
                .Where(img => !string.IsNullOrEmpty(img.Deleted))
                .Select(img => img.Deleted)
                .ToList();

            _logger.LogInformation(
                "Pruned {Count} dangling images via Portainer, reclaimed {Bytes} bytes",
                deletedIds.Count,
                reclaimedBytes);

            return new PruneResult
            {
                Success = true,
                DeletedItems = deletedIds!,
                DeletedCount = deletedIds.Count,
                ReclaimedBytes = reclaimedBytes,
                Message = deletedIds.Count > 0
                    ? $"Pruned {deletedIds.Count} dangling image(s), reclaimed {FormatBytes(reclaimedBytes)}"
                    : "No dangling images to prune"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error pruning images via Portainer");
            return new PruneResult
            {
                Success = false,
                Message = $"Error pruning images: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Formats bytes into a human-readable string.
    /// </summary>
    private static string FormatBytes(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        int order = 0;
        double len = bytes;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len /= 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }

    public void Dispose()
    {
        // HttpClient is managed by factory, don't dispose
    }
}

#region Portainer API Models

/// <summary>
/// Response from container create API.
/// </summary>
internal class PortainerCreateContainerResponse
{
    [JsonPropertyName("Id")]
    public string? Id { get; set; }

    [JsonPropertyName("Warnings")]
    public List<string>? Warnings { get; set; }
}

/// <summary>
/// Portainer status response.
/// </summary>
internal class PortainerStatus
{
    [JsonPropertyName("Version")]
    public string? Version { get; set; }

    [JsonPropertyName("InstanceID")]
    public string? InstanceId { get; set; }
}

/// <summary>
/// Networks prune response from Portainer/Docker API.
/// </summary>
internal class PortainerNetworksPruneResponse
{
    [JsonPropertyName("NetworksDeleted")]
    public List<string>? NetworksDeleted { get; set; }
}

/// <summary>
/// Volumes prune response from Portainer/Docker API.
/// </summary>
internal class PortainerVolumesPruneResponse
{
    [JsonPropertyName("VolumesDeleted")]
    public List<string>? VolumesDeleted { get; set; }

    [JsonPropertyName("SpaceReclaimed")]
    public ulong SpaceReclaimed { get; set; }
}

/// <summary>
/// Images prune response from Portainer/Docker API.
/// </summary>
internal class PortainerImagesPruneResponse
{
    [JsonPropertyName("ImagesDeleted")]
    public List<PortainerImageDeleteResponse>? ImagesDeleted { get; set; }

    [JsonPropertyName("SpaceReclaimed")]
    public ulong SpaceReclaimed { get; set; }
}

/// <summary>
/// Individual image delete response from Portainer/Docker API.
/// </summary>
internal class PortainerImageDeleteResponse
{
    [JsonPropertyName("Deleted")]
    public string? Deleted { get; set; }

    [JsonPropertyName("Untagged")]
    public string? Untagged { get; set; }
}

/// <summary>
/// Container info from Portainer/Docker API.
/// </summary>
internal class PortainerContainer
{
    [JsonPropertyName("Id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("Names")]
    public List<string>? Names { get; set; }

    [JsonPropertyName("Image")]
    public string? Image { get; set; }

    [JsonPropertyName("ImageID")]
    public string? ImageID { get; set; }

    [JsonPropertyName("State")]
    public string? State { get; set; }

    [JsonPropertyName("Status")]
    public string? Status { get; set; }

    [JsonPropertyName("Labels")]
    public Dictionary<string, string>? Labels { get; set; }
}

#endregion
