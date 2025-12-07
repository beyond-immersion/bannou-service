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
/// Container orchestrator implementation for Docker Compose (standalone Docker).
/// Uses Docker.DotNet SDK for container management.
/// See docs/ORCHESTRATOR-SDK-REFERENCE.md for API documentation.
/// </summary>
public class DockerComposeOrchestrator : IContainerOrchestrator
{
    private readonly ILogger<DockerComposeOrchestrator> _logger;
    private readonly DockerClient _client;

    /// <summary>
    /// Label used to identify Dapr app-id on containers.
    /// </summary>
    private const string DAPR_APP_ID_LABEL = "dapr.io/app-id";

    /// <summary>
    /// Alternative label format for Dapr app-id.
    /// </summary>
    private const string DAPR_APP_ID_LABEL_ALT = "io.dapr.app-id";

    /// <summary>
    /// Docker Compose label for service name (fallback when Dapr labels not present).
    /// </summary>
    private const string COMPOSE_SERVICE_LABEL = "com.docker.compose.service";

    /// <summary>
    /// Docker Compose label for project name.
    /// </summary>
    private const string COMPOSE_PROJECT_LABEL = "com.docker.compose.project";

    public DockerComposeOrchestrator(ILogger<DockerComposeOrchestrator> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Create Docker client using default configuration
        // Linux: unix:///var/run/docker.sock
        // Windows: npipe://./pipe/docker_engine
        _client = new DockerClientConfiguration().CreateClient();
    }

    /// <inheritdoc />
    public BackendType BackendType => BackendType.Compose;

    /// <inheritdoc />
    public async Task<(bool Available, string Message)> CheckAvailabilityAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _client.System.PingAsync(cancellationToken);
            var version = await _client.System.GetVersionAsync(cancellationToken);
            return (true, $"Docker {version.Version} available");
        }
        catch (Exception ex)
        {
            return (false, $"Docker not available: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<ContainerStatus> GetContainerStatusAsync(string appName, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Getting container status for app: {AppName}", appName);

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

    /// <inheritdoc />
    public async Task<ContainerRestartResponse> RestartContainerAsync(
        string appName,
        ContainerRestartRequest request,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Restarting container for app: {AppName}, Priority: {Priority}, Reason: {Reason}",
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
                    Message = $"No container found with Dapr app-id '{appName}'"
                };
            }

            // Determine restart parameters based on priority
            var waitSeconds = request.Priority switch
            {
                RestartPriority.Immediate => 0,
                RestartPriority.Force => 0,
                RestartPriority.Graceful => request.ShutdownGracePeriod,
                _ => request.ShutdownGracePeriod
            };

            // RestartContainerAsync - see ORCHESTRATOR-SDK-REFERENCE.md
            await _client.Containers.RestartContainerAsync(
                container.ID,
                new ContainerRestartParameters
                {
                    WaitBeforeKillSeconds = (uint)waitSeconds
                },
                cancellationToken);

            _logger.LogInformation(
                "Container {ContainerId} restarted successfully",
                container.ID[..12]);

            return new ContainerRestartResponse
            {
                Accepted = true,
                AppName = appName,
                ScheduledFor = DateTimeOffset.UtcNow,
                CurrentInstances = 1,
                RestartStrategy = ContainerRestartResponseRestartStrategy.Simultaneous,
                Message = $"Container {container.ID[..12]} restarted successfully"
            };
        }
        catch (DockerApiException ex)
        {
            _logger.LogError(ex, "Docker API error restarting container for {AppName}", appName);
            return new ContainerRestartResponse
            {
                Accepted = false,
                AppName = appName,
                Message = $"Docker API error: {ex.Message}"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error restarting container for {AppName}", appName);
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
        _logger.LogDebug("Listing all containers");

        try
        {
            // ListContainersAsync - see ORCHESTRATOR-SDK-REFERENCE.md
            // First try containers with Dapr labels
            var containers = await _client.Containers.ListContainersAsync(
                new ContainersListParameters
                {
                    All = true, // Include stopped containers
                    Filters = new Dictionary<string, IDictionary<string, bool>>
                    {
                        // Filter to containers with Dapr labels
                        ["label"] = new Dictionary<string, bool>
                        {
                            [DAPR_APP_ID_LABEL] = true
                        }
                    }
                },
                cancellationToken);

            // Also check alternative Dapr label format
            var containersAlt = await _client.Containers.ListContainersAsync(
                new ContainersListParameters
                {
                    All = true,
                    Filters = new Dictionary<string, IDictionary<string, bool>>
                    {
                        ["label"] = new Dictionary<string, bool>
                        {
                            [DAPR_APP_ID_LABEL_ALT] = true
                        }
                    }
                },
                cancellationToken);

            // Also check Docker Compose labels (fallback for containers without Dapr labels)
            var containersCompose = await _client.Containers.ListContainersAsync(
                new ContainersListParameters
                {
                    All = true,
                    Filters = new Dictionary<string, IDictionary<string, bool>>
                    {
                        ["label"] = new Dictionary<string, bool>
                        {
                            [COMPOSE_SERVICE_LABEL] = true
                        }
                    }
                },
                cancellationToken);

            // Merge results, avoiding duplicates
            var allContainers = containers
                .Concat(containersAlt)
                .Concat(containersCompose)
                .DistinctBy(c => c.ID)
                .ToList();

            _logger.LogDebug(
                "Found {Total} containers: {DaprCount} with Dapr labels, {ComposeCount} with Compose labels",
                allContainers.Count,
                containers.Count + containersAlt.Count,
                containersCompose.Count);

            return allContainers
                .Select(c => MapContainerToStatus(c, GetAppNameFromContainer(c)))
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing containers");
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
        _logger.LogDebug("Getting logs for app: {AppName}, tail: {Tail}", appName, tail);

        try
        {
            var container = await FindContainerByAppNameAsync(appName, cancellationToken);
            if (container == null)
            {
                return $"No container found with Dapr app-id '{appName}'";
            }

            // GetContainerLogsAsync - see ORCHESTRATOR-SDK-REFERENCE.md
            using var stream = await _client.Containers.GetContainerLogsAsync(
                container.ID,
                false, // tty = false to get proper multiplexed stream
                new ContainerLogsParameters
                {
                    ShowStdout = true,
                    ShowStderr = true,
                    Tail = tail.ToString(),
                    Since = since?.ToUnixTimeSeconds().ToString(),
                    Timestamps = true
                },
                cancellationToken);

            // Docker multiplexes stdout/stderr with a header format
            return await ReadDockerLogStreamAsync(stream, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting logs for {AppName}", appName);
            return $"Error retrieving logs: {ex.Message}";
        }
    }

    /// <summary>
    /// Finds a container by its Dapr app-id label.
    /// </summary>
    private async Task<ContainerListResponse?> FindContainerByAppNameAsync(
        string appName,
        CancellationToken cancellationToken)
    {
        // Try primary Dapr label format
        var containers = await _client.Containers.ListContainersAsync(
            new ContainersListParameters
            {
                All = true,
                Filters = new Dictionary<string, IDictionary<string, bool>>
                {
                    ["label"] = new Dictionary<string, bool>
                    {
                        [$"{DAPR_APP_ID_LABEL}={appName}"] = true
                    }
                }
            },
            cancellationToken);

        if (containers.Count > 0)
        {
            return containers[0];
        }

        // Try alternative Dapr label format
        containers = await _client.Containers.ListContainersAsync(
            new ContainersListParameters
            {
                All = true,
                Filters = new Dictionary<string, IDictionary<string, bool>>
                {
                    ["label"] = new Dictionary<string, bool>
                    {
                        [$"{DAPR_APP_ID_LABEL_ALT}={appName}"] = true
                    }
                }
            },
            cancellationToken);

        if (containers.Count > 0)
        {
            return containers[0];
        }

        // Try Docker Compose service label (fallback for Compose-deployed containers)
        containers = await _client.Containers.ListContainersAsync(
            new ContainersListParameters
            {
                All = true,
                Filters = new Dictionary<string, IDictionary<string, bool>>
                {
                    ["label"] = new Dictionary<string, bool>
                    {
                        [$"{COMPOSE_SERVICE_LABEL}={appName}"] = true
                    }
                }
            },
            cancellationToken);

        return containers.FirstOrDefault();
    }

    /// <summary>
    /// Extracts Dapr app-id from container labels.
    /// </summary>
    private static string GetAppNameFromContainer(ContainerListResponse container)
    {
        // First try Dapr labels
        if (container.Labels.TryGetValue(DAPR_APP_ID_LABEL, out var appId))
        {
            return appId;
        }

        if (container.Labels.TryGetValue(DAPR_APP_ID_LABEL_ALT, out appId))
        {
            return appId;
        }

        // Try Docker Compose service name (fallback for Compose-deployed containers)
        if (container.Labels.TryGetValue(COMPOSE_SERVICE_LABEL, out var serviceName))
        {
            return serviceName;
        }

        // Final fallback to container name
        return container.Names.FirstOrDefault()?.TrimStart('/') ?? container.ID[..12];
    }

    /// <summary>
    /// Maps a Docker container to our ContainerStatus model.
    /// </summary>
    private static ContainerStatus MapContainerToStatus(ContainerListResponse container, string appName)
    {
        var dockerState = container.State.ToLowerInvariant();
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

    /// <summary>
    /// Reads and demultiplexes Docker log stream.
    /// Docker uses an 8-byte header: [STREAM_TYPE][0][0][0][SIZE1][SIZE2][SIZE3][SIZE4]
    /// </summary>
    private static async Task<string> ReadDockerLogStreamAsync(MultiplexedStream stream, CancellationToken cancellationToken)
    {
        // Read all output from the multiplexed stream
        var stdout = new MemoryStream();
        var stderr = new MemoryStream();
        await stream.CopyOutputToAsync(Stream.Null, stdout, stderr, cancellationToken);

        stdout.Position = 0;
        using var reader = new StreamReader(stdout);
        var content = await reader.ReadToEndAsync(cancellationToken);

        // Append stderr if present
        if (stderr.Length > 0)
        {
            stderr.Position = 0;
            using var stderrReader = new StreamReader(stderr);
            var stderrContent = await stderrReader.ReadToEndAsync(cancellationToken);
            if (!string.IsNullOrEmpty(stderrContent))
            {
                content += "\n[STDERR]\n" + stderrContent;
            }
        }

        return content;
    }

    /// <inheritdoc />
    public async Task<DeployServiceResult> DeployServiceAsync(
        string serviceName,
        string appId,
        Dictionary<string, string>? environment = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Deploying service: {ServiceName} with app-id: {AppId}",
            serviceName, appId);

        try
        {
            // For Docker Compose, deployment involves:
            // 1. Creating a container with the appropriate image and labels
            // 2. Setting environment variables
            // 3. Starting the container

            // Get the bannou image (assumes it's already built)
            var imageName = "bannou:latest";

            // Prepare container configuration
            var envList = new List<string>
            {
                $"{serviceName.ToUpperInvariant()}_SERVICE_ENABLED=true",
                $"DAPR_APP_ID={appId}"
            };

            if (environment != null)
            {
                envList.AddRange(environment.Select(kv => $"{kv.Key}={kv.Value}"));
            }

            // CreateContainerAsync - see ORCHESTRATOR-SDK-REFERENCE.md
            var createParams = new Docker.DotNet.Models.CreateContainerParameters
            {
                Image = imageName,
                Name = $"bannou-{serviceName}-{appId}",
                Env = envList,
                Labels = new Dictionary<string, string>
                {
                    [DAPR_APP_ID_LABEL] = appId,
                    ["bannou.service"] = serviceName
                },
                HostConfig = new Docker.DotNet.Models.HostConfig
                {
                    RestartPolicy = new Docker.DotNet.Models.RestartPolicy
                    {
                        Name = Docker.DotNet.Models.RestartPolicyKind.UnlessStopped
                    }
                }
            };

            var container = await _client.Containers.CreateContainerAsync(createParams, cancellationToken);

            // Start the container
            await _client.Containers.StartContainerAsync(container.ID, new Docker.DotNet.Models.ContainerStartParameters(), cancellationToken);

            _logger.LogInformation(
                "Service {ServiceName} deployed successfully with container: {ContainerId}",
                serviceName, container.ID[..12]);

            return new DeployServiceResult
            {
                Success = true,
                AppId = appId,
                ContainerId = container.ID,
                Message = $"Service {serviceName} deployed as container {container.ID[..12]}"
            };
        }
        catch (DockerApiException ex)
        {
            _logger.LogError(ex, "Docker API error deploying service {ServiceName}", serviceName);
            return new DeployServiceResult
            {
                Success = false,
                AppId = appId,
                Message = $"Docker API error: {ex.Message}"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deploying service {ServiceName}", serviceName);
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
            "Tearing down service: {AppName}, removeVolumes: {RemoveVolumes}",
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
                    Message = $"No container found with Dapr app-id '{appName}'"
                };
            }

            // Stop the container
            await _client.Containers.StopContainerAsync(
                container.ID,
                new Docker.DotNet.Models.ContainerStopParameters { WaitBeforeKillSeconds = 10 },
                cancellationToken);

            // Remove the container
            await _client.Containers.RemoveContainerAsync(
                container.ID,
                new Docker.DotNet.Models.ContainerRemoveParameters { RemoveVolumes = removeVolumes },
                cancellationToken);

            _logger.LogInformation(
                "Service {AppName} torn down successfully, container removed: {ContainerId}",
                appName, container.ID[..12]);

            return new TeardownServiceResult
            {
                Success = true,
                AppId = appName,
                StoppedContainers = new List<string> { container.ID[..12] },
                RemovedVolumes = new List<string>(), // Would track volumes if removeVolumes=true
                Message = $"Container {container.ID[..12]} stopped and removed"
            };
        }
        catch (DockerApiException ex)
        {
            _logger.LogError(ex, "Docker API error tearing down service {AppName}", appName);
            return new TeardownServiceResult
            {
                Success = false,
                AppId = appName,
                Message = $"Docker API error: {ex.Message}"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error tearing down service {AppName}", appName);
            return new TeardownServiceResult
            {
                Success = false,
                AppId = appName,
                Message = $"Error: {ex.Message}"
            };
        }
    }

    /// <inheritdoc />
    public Task<ScaleServiceResult> ScaleServiceAsync(
        string appName,
        int replicas,
        CancellationToken cancellationToken = default)
    {
        _logger.LogWarning(
            "Scale operation not supported in Docker Compose mode for {AppName}. Use Docker Swarm for scaling.",
            appName);

        // Docker Compose (standalone mode) doesn't support native scaling
        // For scaling, use Docker Swarm or Kubernetes backend
        return Task.FromResult(new ScaleServiceResult
        {
            Success = false,
            AppId = appName,
            PreviousReplicas = 1,
            CurrentReplicas = 1,
            Message = "Scaling not supported in Docker Compose mode. Use Docker Swarm or Kubernetes for scaling capabilities."
        });
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> ListInfrastructureServicesAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Listing infrastructure services (Docker Compose mode)");

        try
        {
            // In Docker Compose mode, we identify infrastructure services by:
            // 1. Container names that match known infrastructure patterns
            // 2. Containers in the same Docker Compose project
            var infrastructurePatterns = new[] { "redis", "rabbitmq", "mysql", "mariadb", "postgres", "mongodb", "placement", "dapr" };

            var allContainers = await _client.Containers.ListContainersAsync(
                new ContainersListParameters { All = true },
                cancellationToken);

            var infrastructureServices = allContainers
                .Where(c =>
                {
                    var name = c.Names.FirstOrDefault()?.TrimStart('/') ?? "";
                    var image = c.Image?.ToLowerInvariant() ?? "";

                    // Check if container name or image matches infrastructure patterns
                    return infrastructurePatterns.Any(pattern =>
                        name.Contains(pattern, StringComparison.OrdinalIgnoreCase) ||
                        image.Contains(pattern, StringComparison.OrdinalIgnoreCase));
                })
                // Use GetAppNameFromContainer for consistent naming with ListContainersAsync
                .Select(c => GetAppNameFromContainer(c))
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

    public void Dispose()
    {
        _client.Dispose();
    }
}
