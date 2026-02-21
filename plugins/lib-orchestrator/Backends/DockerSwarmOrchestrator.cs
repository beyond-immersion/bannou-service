using BeyondImmersion.BannouService.Orchestrator;
using Docker.DotNet;
using Docker.DotNet.Models;
using Microsoft.Extensions.Logging;

// Type aliases to shadow Docker.DotNet types with our Orchestrator types
using BackendType = BeyondImmersion.BannouService.Orchestrator.BackendType;
using ContainerRestartRequest = BeyondImmersion.BannouService.Orchestrator.ContainerRestartRequest;
using ContainerRestartResponse = BeyondImmersion.BannouService.Orchestrator.ContainerRestartResponse;
using ContainerRestartResponseRestartStrategy = BeyondImmersion.BannouService.Orchestrator.ContainerRestartResponseRestartStrategy;
using ContainerStatus = BeyondImmersion.BannouService.Orchestrator.ContainerStatus;
using ContainerStatusStatus = BeyondImmersion.BannouService.Orchestrator.ContainerStatusStatus;
using RestartHistoryEntry = BeyondImmersion.BannouService.Orchestrator.RestartHistoryEntry;
using RestartPriority = BeyondImmersion.BannouService.Orchestrator.RestartPriority;

namespace LibOrchestrator.Backends;

/// <summary>
/// Container orchestrator implementation for Docker Swarm mode.
/// Uses Docker.DotNet SDK for service management.
/// See docs/ORCHESTRATOR-SDK-REFERENCE.md for API documentation.
/// </summary>
public class DockerSwarmOrchestrator : IContainerOrchestrator
{
    private readonly ILogger<DockerSwarmOrchestrator> _logger;
    private readonly OrchestratorServiceConfiguration _configuration;
    private readonly DockerClient _client;

    /// <summary>
    /// Label used to identify app-id on services.
    /// </summary>
    private const string BANNOU_APP_ID_LABEL = "bannou.app-id";

    public DockerSwarmOrchestrator(OrchestratorServiceConfiguration configuration, ILogger<DockerSwarmOrchestrator> logger)
    {
        _logger = logger;
        _configuration = configuration;
        using var config = new DockerClientConfiguration();
        _client = config.CreateClient();
    }

    /// <inheritdoc />
    public BackendType BackendType => BackendType.Swarm;

    /// <inheritdoc />
    public async Task<(bool Available, string Message)> CheckAvailabilityAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // GetSystemInfoAsync - see ORCHESTRATOR-SDK-REFERENCE.md
            var systemInfo = await _client.System.GetSystemInfoAsync(cancellationToken);

            // Check LocalNodeState: "inactive", "pending", "active", "error", "locked"
            var swarmState = systemInfo.Swarm?.LocalNodeState;
            if (swarmState != "active")
            {
                return (false, $"Swarm not active (state: {swarmState ?? "null"})");
            }

            var isManager = systemInfo.Swarm?.ControlAvailable == true;
            if (!isManager)
            {
                return (true, "Swarm worker node (limited functionality)");
            }

            return (true, "Swarm manager node");
        }
        catch (Exception ex)
        {
            return (false, $"Swarm check failed: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<ContainerStatus> GetContainerStatusAsync(string appName, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Getting Swarm service status for app: {AppName}", appName);

        try
        {
            var service = await FindServiceByAppNameAsync(appName, cancellationToken);
            if (service == null)
            {
                return new ContainerStatus
                {
                    AppName = appName,
                    Status = ContainerStatusStatus.Stopped,
                    Timestamp = DateTimeOffset.UtcNow,
                    Instances = 0
                };
            }

            return await MapServiceToStatusAsync(service, appName, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting Swarm service status for {AppName}", appName);
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
        _logger.LogInformation(
            "Restarting Swarm service for app: {AppName}, Priority: {Priority}, Reason: {Reason}",
            appName,
            request.Priority,
            request.Reason);

        try
        {
            var service = await FindServiceByAppNameAsync(appName, cancellationToken);
            if (service == null)
            {
                return new ContainerRestartResponse
                {
                    Accepted = false,
                    AppName = appName,
                    Message = $"No Swarm service found with app-id '{appName}'"
                };
            }

            // Force update to trigger rolling restart (--force flag equivalent)
            // Increment ForceUpdate to trigger a rolling restart
            var updateParams = new ServiceUpdateParameters
            {
                Service = service.Spec,
                Version = (long)service.Version.Index
            };

            // Increment ForceUpdate counter to force a new deployment
            updateParams.Service.TaskTemplate.ForceUpdate =
                updateParams.Service.TaskTemplate.ForceUpdate + 1;

            // Configure stop grace period based on priority
            if (request.Priority == RestartPriority.Immediate || request.Priority == RestartPriority.Force)
            {
                updateParams.Service.TaskTemplate.ContainerSpec.StopGracePeriod = 0;
            }
            else
            {
                updateParams.Service.TaskTemplate.ContainerSpec.StopGracePeriod =
                    (long)TimeSpan.FromSeconds(request.ShutdownGracePeriod).TotalNanoseconds;
            }

            // UpdateServiceAsync - see ORCHESTRATOR-SDK-REFERENCE.md
            await _client.Swarm.UpdateServiceAsync(service.ID, updateParams, cancellationToken);

            var desiredReplicas = (int)(service.Spec.Mode?.Replicated?.Replicas ?? 1);

            _logger.LogInformation(
                "Swarm service {ServiceId} rolling restart initiated",
                service.ID[..12]);

            return new ContainerRestartResponse
            {
                Accepted = true,
                AppName = appName,
                ScheduledFor = DateTimeOffset.UtcNow,
                CurrentInstances = desiredReplicas,
                RestartStrategy = ContainerRestartResponseRestartStrategy.Rolling,
                Message = $"Rolling restart initiated for service {service.Spec.Name}"
            };
        }
        catch (DockerApiException ex)
        {
            _logger.LogError(ex, "Docker API error restarting Swarm service for {AppName}", appName);
            return new ContainerRestartResponse
            {
                Accepted = false,
                AppName = appName,
                Message = $"Docker API error: {ex.Message}"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error restarting Swarm service for {AppName}", appName);
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
        _logger.LogDebug("Listing all Swarm services");

        try
        {
            // ListServicesAsync - see ORCHESTRATOR-SDK-REFERENCE.md
            var services = await _client.Swarm.ListServicesAsync(
                new ServicesListParameters
                {
                    Filters = new ServiceFilter
                    {
                        Label = new[] { BANNOU_APP_ID_LABEL }
                    }
                },
                cancellationToken);

            var statuses = new List<ContainerStatus>();
            foreach (var service in services)
            {
                var appName = GetAppNameFromService(service);
                var status = await MapServiceToStatusAsync(service, appName, cancellationToken);
                statuses.Add(status);
            }

            return statuses;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing Swarm services");
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
        _logger.LogDebug("Getting Swarm service logs for app: {AppName}, tail: {Tail}", appName, tail);

        try
        {
            var service = await FindServiceByAppNameAsync(appName, cancellationToken);
            if (service == null)
            {
                return $"No Swarm service found with app-id '{appName}'";
            }

            // GetServiceLogsAsync - see ORCHESTRATOR-SDK-REFERENCE.md
            using var stream = await _client.Swarm.GetServiceLogsAsync(
                service.ID,
                new ServiceLogsParameters
                {
                    ShowStdout = true,
                    ShowStderr = true,
                    Tail = tail.ToString(),
                    Since = since?.ToUnixTimeSeconds().ToString(),
                    Timestamps = true
                },
                cancellationToken);

            using var reader = new StreamReader(stream);
            return await reader.ReadToEndAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting Swarm service logs for {AppName}", appName);
            return $"Error retrieving logs: {ex.Message}";
        }
    }

    /// <summary>
    /// Finds a Swarm service by its app-id label.
    /// </summary>
    private async Task<SwarmService?> FindServiceByAppNameAsync(
        string appName,
        CancellationToken cancellationToken)
    {
        var services = await _client.Swarm.ListServicesAsync(
            new ServicesListParameters
            {
                Filters = new ServiceFilter
                {
                    Label = new[] { $"{BANNOU_APP_ID_LABEL}={appName}" }
                }
            },
            cancellationToken);

        return services.FirstOrDefault();
    }

    /// <summary>
    /// Extracts app-id from service labels.
    /// </summary>
    private static string GetAppNameFromService(SwarmService service)
    {
        if (service.Spec.Labels?.TryGetValue(BANNOU_APP_ID_LABEL, out var appId) == true)
        {
            return appId;
        }

        return service.Spec.Name;
    }

    /// <summary>
    /// Maps a Swarm service to our ContainerStatus model.
    /// </summary>
    private async Task<ContainerStatus> MapServiceToStatusAsync(
        SwarmService service,
        string appName,
        CancellationToken cancellationToken)
    {
        // Get service tasks to determine actual running state
        var tasks = await GetServiceTasksAsync(service.ID, cancellationToken);

        var runningTasks = tasks.Count(t => t.Status.State == "running");
        var desiredTasks = (int)(service.Spec.Mode?.Replicated?.Replicas ?? 1);

        var status = (runningTasks, desiredTasks) switch
        {
            (0, _) => ContainerStatusStatus.Stopped,
            var (r, d) when r == d => ContainerStatusStatus.Running,
            var (r, d) when r < d => ContainerStatusStatus.Starting,
            _ => ContainerStatusStatus.Unhealthy
        };

        return new ContainerStatus
        {
            AppName = appName,
            Status = status,
            Timestamp = DateTimeOffset.UtcNow,
            Instances = runningTasks,
            RestartHistory = new List<RestartHistoryEntry>()
        };
    }

    /// <summary>
    /// Gets tasks for a specific service.
    /// </summary>
    private async Task<IList<TaskResponse>> GetServiceTasksAsync(
        string serviceId,
        CancellationToken cancellationToken)
    {
        await Task.CompletedTask;
        try
        {
            // Note: Docker.DotNet doesn't have a direct ListTasksAsync on Swarm
            // We'd need to use the Tasks endpoint if available
            // For now, return empty list - this would need investigation
            // of available methods in Docker.DotNet for task listing
            return Array.Empty<TaskResponse>();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to list service tasks for swarm service");
            return Array.Empty<TaskResponse>();
        }
    }

    /// <inheritdoc />
    public async Task<DeployServiceResult> DeployServiceAsync(
        string serviceName,
        string appId,
        Dictionary<string, string>? environment = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Deploying Swarm service: {ServiceName} with app-id: {AppId}",
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

            // CreateServiceAsync - see ORCHESTRATOR-SDK-REFERENCE.md
            var serviceSpec = new ServiceSpec
            {
                Name = $"bannou-{serviceName}-{appId}",
                Labels = new Dictionary<string, string>
                {
                    [BANNOU_APP_ID_LABEL] = appId,
                    ["bannou.service"] = serviceName
                },
                TaskTemplate = new TaskSpec
                {
                    ContainerSpec = new ContainerSpec
                    {
                        Image = imageName,
                        Env = envList
                    },
                    RestartPolicy = new SwarmRestartPolicy
                    {
                        Condition = "any"
                    }
                },
                Mode = new ServiceMode
                {
                    Replicated = new ReplicatedService { Replicas = 1 }
                }
            };

            var createParams = new ServiceCreateParameters { Service = serviceSpec };
            var response = await _client.Swarm.CreateServiceAsync(createParams, cancellationToken);

            _logger.LogInformation(
                "Swarm service {ServiceName} deployed successfully with ID: {ServiceId}",
                serviceName, response.ID[..12]);

            return new DeployServiceResult
            {
                Success = true,
                AppId = appId,
                ContainerId = response.ID,
                Message = $"Swarm service {serviceName} deployed with ID {response.ID[..12]}"
            };
        }
        catch (DockerApiException ex)
        {
            _logger.LogError(ex, "Docker API error deploying Swarm service {ServiceName}", serviceName);
            return new DeployServiceResult
            {
                Success = false,
                AppId = appId,
                Message = $"Docker API error: {ex.Message}"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deploying Swarm service {ServiceName}", serviceName);
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
        _logger.LogInformation(
            "Tearing down Swarm service: {AppName}, removeVolumes: {RemoveVolumes}",
            appName, removeVolumes);

        try
        {
            var service = await FindServiceByAppNameAsync(appName, cancellationToken);
            if (service == null)
            {
                return new TeardownServiceResult
                {
                    Success = false,
                    AppId = appName,
                    Message = $"No Swarm service found with app-id '{appName}'"
                };
            }

            // RemoveServiceAsync - see ORCHESTRATOR-SDK-REFERENCE.md
            await _client.Swarm.RemoveServiceAsync(service.ID, cancellationToken);

            _logger.LogInformation(
                "Swarm service {AppName} removed successfully: {ServiceId}",
                appName, service.ID[..12]);

            return new TeardownServiceResult
            {
                Success = true,
                AppId = appName,
                StoppedContainers = new List<string> { service.ID[..12] },
                Message = $"Swarm service {service.Spec.Name} removed"
            };
        }
        catch (DockerApiException ex)
        {
            _logger.LogError(ex, "Docker API error tearing down Swarm service {AppName}", appName);
            return new TeardownServiceResult
            {
                Success = false,
                AppId = appName,
                Message = $"Docker API error: {ex.Message}"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error tearing down Swarm service {AppName}", appName);
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
        _logger.LogInformation(
            "Scaling Swarm service: {AppName} to {Replicas} replicas",
            appName, replicas);

        try
        {
            var service = await FindServiceByAppNameAsync(appName, cancellationToken);
            if (service == null)
            {
                return new ScaleServiceResult
                {
                    Success = false,
                    AppId = appName,
                    Message = $"No Swarm service found with app-id '{appName}'"
                };
            }

            var previousReplicas = (int)(service.Spec.Mode?.Replicated?.Replicas ?? 1);

            // Update service with new replica count
            var updateParams = new ServiceUpdateParameters
            {
                Service = service.Spec,
                Version = (long)service.Version.Index
            };

            if (updateParams.Service.Mode == null)
            {
                updateParams.Service.Mode = new ServiceMode();
            }
            if (updateParams.Service.Mode.Replicated == null)
            {
                updateParams.Service.Mode.Replicated = new ReplicatedService();
            }
            updateParams.Service.Mode.Replicated.Replicas = (ulong)replicas;

            await _client.Swarm.UpdateServiceAsync(service.ID, updateParams, cancellationToken);

            _logger.LogInformation(
                "Swarm service {AppName} scaled from {Previous} to {Current} replicas",
                appName, previousReplicas, replicas);

            return new ScaleServiceResult
            {
                Success = true,
                AppId = appName,
                PreviousReplicas = previousReplicas,
                CurrentReplicas = replicas,
                Message = $"Scaled from {previousReplicas} to {replicas} replicas"
            };
        }
        catch (DockerApiException ex)
        {
            _logger.LogError(ex, "Docker API error scaling Swarm service {AppName}", appName);
            return new ScaleServiceResult
            {
                Success = false,
                AppId = appName,
                Message = $"Docker API error: {ex.Message}"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error scaling Swarm service {AppName}", appName);
            return new ScaleServiceResult
            {
                Success = false,
                AppId = appName,
                Message = $"Error: {ex.Message}"
            };
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> ListInfrastructureServicesAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Listing infrastructure services (Docker Swarm mode)");

        try
        {
            // In Docker Swarm mode, identify infrastructure services by label or name patterns
            var infrastructurePatterns = new[] { "redis", "rabbitmq", "mysql", "mariadb", "postgres", "mongodb" };

            var services = await _client.Swarm.ListServicesAsync(cancellationToken: cancellationToken);

            var infrastructureServices = services
                .Where(s =>
                {
                    var name = s.Spec.Name ?? "";
                    // Check if service name matches infrastructure patterns
                    return infrastructurePatterns.Any(pattern =>
                        name.Contains(pattern, StringComparison.OrdinalIgnoreCase));
                })
                .Select(s => s.Spec.Name ?? s.ID)
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
        _logger.LogInformation("Pruning unused networks (Docker Swarm)");

        try
        {
            var response = await _client.Networks.PruneNetworksAsync(
                new Docker.DotNet.Models.NetworksDeleteUnusedParameters(),
                cancellationToken);

            var deletedNetworks = response.NetworksDeleted ?? new List<string>();

            _logger.LogInformation(
                "Pruned {Count} unused networks: {Networks}",
                deletedNetworks.Count,
                string.Join(", ", deletedNetworks));

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
            _logger.LogError(ex, "Error pruning networks");
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
        _logger.LogInformation("Pruning unused volumes (Docker Swarm)");

        try
        {
            var response = await _client.Volumes.PruneAsync(
                new Docker.DotNet.Models.VolumesPruneParameters(),
                cancellationToken);

            var deletedVolumes = response.VolumesDeleted ?? new List<string>();
            var reclaimedBytes = (long)response.SpaceReclaimed;

            _logger.LogInformation(
                "Pruned {Count} unused volumes, reclaimed {Bytes} bytes",
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
            _logger.LogError(ex, "Error pruning volumes");
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
        _logger.LogInformation("Pruning dangling images (Docker Swarm)");

        try
        {
            var response = await _client.Images.PruneImagesAsync(
                new Docker.DotNet.Models.ImagesPruneParameters(),
                cancellationToken);

            var deletedImages = response.ImagesDeleted ?? new List<Docker.DotNet.Models.ImageDeleteResponse>();
            var reclaimedBytes = (long)response.SpaceReclaimed;

            var deletedIds = deletedImages
                .Where(img => !string.IsNullOrEmpty(img.Deleted))
                .Select(img => img.Deleted)
                .ToList();

            _logger.LogInformation(
                "Pruned {Count} dangling images, reclaimed {Bytes} bytes",
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
            _logger.LogError(ex, "Error pruning images");
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
        _client.Dispose();
    }
}

/// <summary>
/// Represents a Swarm task response (stub for type safety).
/// </summary>
internal class TaskResponse
{
    public TaskStatus Status { get; set; } = new();
}

/// <summary>
/// Represents Swarm task status.
/// </summary>
internal class TaskStatus
{
    public string State { get; set; } = "";
}
