using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Configuration;
using BeyondImmersion.BannouService.Orchestrator;
using BeyondImmersion.BannouService.Services;
using Docker.DotNet;
using Docker.DotNet.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
// Docker.DotNet.Models.ContainerStatus collides with our ContainerStatus
using ContainerStatus = BeyondImmersion.BannouService.Orchestrator.ContainerStatus;

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
    /// Label used to identify app-id on containers.
    /// </summary>
    private const string BANNOU_APP_ID_LABEL = "bannou.app-id";

    /// <summary>
    /// Docker Compose label for service name (fallback when Bannou labels not present).
    /// </summary>
    private const string COMPOSE_SERVICE_LABEL = "com.docker.compose.service";

    /// <summary>
    /// Docker Compose label for project name.
    /// </summary>
    private const string COMPOSE_PROJECT_LABEL = "com.docker.compose.project";

    /// <summary>
    /// Label to identify containers deployed by the orchestrator.
    /// Used for orphan detection instead of name-based parsing.
    /// </summary>
    public const string BANNOU_ORCHESTRATOR_MANAGED_LABEL = "bannou.orchestrator-managed";

    // Configuration from environment variables
    private readonly OrchestratorServiceConfiguration _configuration;
    private readonly string _configuredDockerNetwork;
    private readonly string _certificatesHostPath;
    private readonly string _presetsHostPath;
    private readonly string _logsVolumeName;

    private readonly AppConfiguration _appConfiguration;
    private readonly ITelemetryProvider _telemetryProvider;

    // Cached discovered network (lazy initialized)
    private string? _discoveredNetwork;
    private bool _networkDiscoveryAttempted;
    // Cached discovered infrastructure hosts (service name -> IP address)
    private Dictionary<string, string>? _discoveredInfrastructureHosts;
    private bool _infrastructureDiscoveryAttempted;

    public DockerComposeOrchestrator(OrchestratorServiceConfiguration config, AppConfiguration appConfiguration, ILogger<DockerComposeOrchestrator> logger, ITelemetryProvider telemetryProvider)
    {
        _logger = logger;
        _configuration = config;
        _appConfiguration = appConfiguration;
        ArgumentNullException.ThrowIfNull(telemetryProvider, nameof(telemetryProvider));
        _telemetryProvider = telemetryProvider;

        // Create Docker client using default configuration
        // Linux: unix:///var/run/docker.sock
        // Windows: npipe://./pipe/docker_engine
        using var dockerConfig = new DockerClientConfiguration();
        _client = dockerConfig.CreateClient();

        // Read configuration from injected configuration class (IMPLEMENTATION TENETS compliant)
        // Central validation ensures non-nullable strings are not empty
        _configuredDockerNetwork = config.DockerNetwork;
        _certificatesHostPath = config.CertificatesHostPath;
        _presetsHostPath = config.PresetsHostPath;
        _logsVolumeName = config.LogsVolumeName;

        _logger.LogDebug(
            "DockerComposeOrchestrator configured: Network={Network}, Certificates={Certs}, Presets={Presets}",
            _configuredDockerNetwork, _certificatesHostPath, _presetsHostPath);
    }


    /// <summary>
    /// Resolve a host path using a primary value plus a set of fallbacks.
    /// Returns null if no candidate exists.
    /// </summary>
    private static string? ResolveHostPath(string primary, params string[] fallbacks)
    {
        var candidates = new List<string> { primary };
        candidates.AddRange(fallbacks);

        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            // If caller provided an absolute path, return it without requiring it to exist
            // The Docker daemon (host) may have the path even if this container does not.
            if (Path.IsPathRooted(candidate))
            {
                return candidate;
            }

            var expanded = Path.GetFullPath(candidate);
            if (Directory.Exists(expanded))
            {
                return expanded;
            }
        }

        return null;
    }

    /// <summary>
    /// Discovers the Docker network to use for new containers.
    /// First tries the configured network, then falls back to discovery.
    /// Returns null if no suitable network is found.
    /// </summary>
    private async Task<string?> DiscoverDockerNetworkAsync(CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.orchestrator", "DockerComposeOrchestrator.DiscoverDockerNetworkAsync");
        // Return cached result if we've already discovered the network
        if (_networkDiscoveryAttempted)
        {
            return _discoveredNetwork;
        }

        _networkDiscoveryAttempted = true;

        try
        {
            // First, check if the configured network exists
            var networks = await _client.Networks.ListNetworksAsync(
                new Docker.DotNet.Models.NetworksListParameters(),
                cancellationToken);

            var configuredNetworkExists = networks.Any(n =>
                n.Name.Equals(_configuredDockerNetwork, StringComparison.OrdinalIgnoreCase));

            if (configuredNetworkExists)
            {
                _logger.LogInformation("Using configured network: {Network}", _configuredDockerNetwork);
                _discoveredNetwork = _configuredDockerNetwork;
                return _discoveredNetwork;
            }

            _logger.LogWarning(
                "Configured network '{ConfiguredNetwork}' not found, attempting discovery...",
                _configuredDockerNetwork);

            // Try to find a network that looks like a Docker Compose default network
            // These typically end with "_default" and contain the project name
            var candidateNetworks = networks
                .Where(n => n.Name.EndsWith("_default", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(n => n.Name.Contains("bannou", StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(n => n.Name.Contains("edge", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (candidateNetworks.Count > 0)
            {
                _discoveredNetwork = candidateNetworks[0].Name;
                _logger.LogInformation(
                    "Discovered Docker Compose network: {Network} (from {Count} candidates)",
                    _discoveredNetwork, candidateNetworks.Count);
                return _discoveredNetwork;
            }

            // Last resort: try to find any network that contains "bannou"
            var bannouNetwork = networks
                .FirstOrDefault(n => n.Name.Contains("bannou", StringComparison.OrdinalIgnoreCase));

            if (bannouNetwork != null)
            {
                _discoveredNetwork = bannouNetwork.Name;
                _logger.LogInformation("Discovered bannou network: {Network}", _discoveredNetwork);
                return _discoveredNetwork;
            }

            // No suitable network found - return null to signal failure
            _logger.LogError(
                "Could not discover Docker network. Configured: '{ConfiguredNetwork}'. Available networks: {Networks}",
                _configuredDockerNetwork, string.Join(", ", networks.Select(n => n.Name)));

            _discoveredNetwork = null;
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error discovering Docker network");
            _discoveredNetwork = null;
            return null;
        }
    }

    /// <summary>
    /// Discovers infrastructure containers (rabbitmq, redis, etc.) and returns their IPs.
    /// These IPs are used as ExtraHosts so dynamically created containers can resolve
    /// service names like "rabbitmq" that are normally only available via Docker Compose DNS.
    /// </summary>
    private async Task<Dictionary<string, string>> DiscoverInfrastructureHostsAsync(
        string targetNetwork,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.orchestrator", "DockerComposeOrchestrator.DiscoverInfrastructureHostsAsync");
        if (_infrastructureDiscoveryAttempted && _discoveredInfrastructureHosts != null)
        {
            return _discoveredInfrastructureHosts;
        }

        _infrastructureDiscoveryAttempted = true;
        _discoveredInfrastructureHosts = new Dictionary<string, string>();

        // Infrastructure service names that dynamically created containers need to resolve
        // IMPORTANT: DEFAULT_APP_NAME must be included so deployed containers can reach the orchestrator
        // for fetching initial service mappings via BANNOU_MAPPING_SOURCE_APP_ID
        var serviceNames = new[] { AppConstants.DEFAULT_APP_NAME, "rabbitmq", "bannou-redis", "bannou-mysql" };

        try
        {
            var containers = await _client.Containers.ListContainersAsync(
                new Docker.DotNet.Models.ContainersListParameters { All = false },
                cancellationToken);

            foreach (var serviceName in serviceNames)
            {
                // Find container by compose service label or name pattern
                var container = containers.FirstOrDefault(c =>
                    (c.Labels.TryGetValue("com.docker.compose.service", out var label) &&
                    label.Equals(serviceName, StringComparison.OrdinalIgnoreCase)) ||
                    c.Names.Any(n => n.Contains(serviceName, StringComparison.OrdinalIgnoreCase)));

                if (container?.NetworkSettings?.Networks != null)
                {
                    // Get IP from target network, or any network if target not found
                    var networkSettings = container.NetworkSettings.Networks.TryGetValue(targetNetwork, out var net)
                        ? net
                        : container.NetworkSettings.Networks.Values.FirstOrDefault();

                    if (networkSettings?.IPAddress != null && !string.IsNullOrEmpty(networkSettings.IPAddress))
                    {
                        _discoveredInfrastructureHosts[serviceName] = networkSettings.IPAddress;
                        _logger.LogDebug("Discovered infrastructure host: {Service} -> {IP}", serviceName, networkSettings.IPAddress);
                    }
                }
            }

            if (_discoveredInfrastructureHosts.Count > 0)
            {
                _logger.LogInformation(
                    "Discovered {Count} infrastructure hosts for ExtraHosts: {Hosts}",
                    _discoveredInfrastructureHosts.Count,
                    string.Join(", ", _discoveredInfrastructureHosts.Select(kv => $"{kv.Key}={kv.Value}")));
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error discovering infrastructure hosts; containers may not resolve service names");
        }

        return _discoveredInfrastructureHosts;
    }

    /// <summary>
    /// Finds the full container name (with Docker Compose project prefix) for a given service name.
    /// This is needed because Docker Compose creates containers with names like "bannou-test-edge-placement-1"
    /// but services reference them by just "placement".
    /// </summary>
    private async Task<string?> FindContainerFullNameByServiceAsync(
        string serviceName,
        string targetNetwork,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.orchestrator", "DockerComposeOrchestrator.FindContainerFullNameByServiceAsync");
        try
        {
            var containers = await _client.Containers.ListContainersAsync(
                new Docker.DotNet.Models.ContainersListParameters
                {
                    All = false,
                    Filters = new Dictionary<string, IDictionary<string, bool>>
                    {
                        ["label"] = new Dictionary<string, bool>
                        {
                            [$"com.docker.compose.service={serviceName}"] = true
                        }
                    }
                },
                cancellationToken);

            // Get container on target network or first available
            var container = containers
                .FirstOrDefault(c =>
                    c.NetworkSettings?.Networks != null &&
                    c.NetworkSettings.Networks.ContainsKey(targetNetwork))
                ?? containers.FirstOrDefault();

            return container?.Names.FirstOrDefault()?.TrimStart('/');
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not find full container name for service {Service}", serviceName);
            return null;
        }
    }

    /// <inheritdoc />
    public BackendType BackendType => BackendType.Compose;

    /// <inheritdoc />
    public async Task<(bool Available, string Message)> CheckAvailabilityAsync(CancellationToken cancellationToken = default)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.orchestrator", "DockerComposeOrchestrator.CheckAvailabilityAsync");
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
        using var activity = _telemetryProvider.StartActivity("bannou.orchestrator", "DockerComposeOrchestrator.GetContainerStatusAsync");
        _logger.LogDebug("Getting container status for app: {AppName}", appName);

        var container = await FindContainerByAppNameAsync(appName, cancellationToken);
        if (container == null)
        {
            return new ContainerStatus
            {
                AppName = appName,
                Status = ContainerStatusType.Stopped,
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
        using var activity = _telemetryProvider.StartActivity("bannou.orchestrator", "DockerComposeOrchestrator.RestartContainerAsync");
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
                    Accepted = false
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
                ScheduledFor = DateTimeOffset.UtcNow,
                CurrentInstances = 1,
                RestartStrategy = RestartStrategy.Simultaneous
            };
        }
        catch (DockerApiException ex)
        {
            _logger.LogError(ex, "Docker API error restarting container for {AppName}", appName);
            return new ContainerRestartResponse
            {
                Accepted = false
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error restarting container for {AppName}", appName);
            return new ContainerRestartResponse
            {
                Accepted = false
            };
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ContainerStatus>> ListContainersAsync(CancellationToken cancellationToken = default)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.orchestrator", "DockerComposeOrchestrator.ListContainersAsync");
        _logger.LogDebug("Listing all containers");

        try
        {
            // ListContainersAsync - see ORCHESTRATOR-SDK-REFERENCE.md
            // First try containers with Bannou labels
            var containers = await _client.Containers.ListContainersAsync(
                new ContainersListParameters
                {
                    All = true, // Include stopped containers
                    Filters = new Dictionary<string, IDictionary<string, bool>>
                    {
                        // Filter to containers with Bannou labels
                        ["label"] = new Dictionary<string, bool>
                        {
                            [BANNOU_APP_ID_LABEL] = true
                        }
                    }
                },
                cancellationToken);

            // Also check Docker Compose labels (fallback for containers without Bannou labels)
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
                .Concat(containersCompose)
                .DistinctBy(c => c.ID)
                .ToList();

            _logger.LogDebug(
                "Found {Total} containers: {BannouCount} with Bannou labels, {ComposeCount} with Compose labels",
                allContainers.Count,
                containers.Count,
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
        using var activity = _telemetryProvider.StartActivity("bannou.orchestrator", "DockerComposeOrchestrator.GetContainerLogsAsync");
        _logger.LogDebug("Getting logs for app: {AppName}, tail: {Tail}", appName, tail);

        try
        {
            var container = await FindContainerByAppNameAsync(appName, cancellationToken);
            if (container == null)
            {
                return $"No container found with app-id '{appName}'";
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
    /// Finds a container by its app-id label.
    /// </summary>
    private async Task<ContainerListResponse?> FindContainerByAppNameAsync(
        string appName,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.orchestrator", "DockerComposeOrchestrator.FindContainerByAppNameAsync");
        // Try Bannou app-id label
        var containers = await _client.Containers.ListContainersAsync(
            new ContainersListParameters
            {
                All = true,
                Filters = new Dictionary<string, IDictionary<string, bool>>
                {
                    ["label"] = new Dictionary<string, bool>
                    {
                        [$"{BANNOU_APP_ID_LABEL}={appName}"] = true
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
    /// Extracts app-id from container labels.
    /// </summary>
    private static string GetAppNameFromContainer(ContainerListResponse container)
    {
        // First try Bannou app-id label
        if (container.Labels.TryGetValue(BANNOU_APP_ID_LABEL, out var appId))
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
            "running" => ContainerStatusType.Running,
            "restarting" => ContainerStatusType.Starting,
            "exited" => ContainerStatusType.Stopped,
            "dead" => ContainerStatusType.Unhealthy,
            "created" => ContainerStatusType.Stopped,
            "paused" => ContainerStatusType.Stopping,
            _ => ContainerStatusType.Unhealthy
        };

        return new ContainerStatus
        {
            AppName = appName,
            Status = status,
            Timestamp = DateTimeOffset.UtcNow,
            Instances = status == ContainerStatusType.Running ? 1 : 0,
            RestartHistory = new List<RestartHistoryEntry>(),
            Labels = container.Labels ?? new Dictionary<string, string>()
        };
    }

    /// <summary>
    /// Reads and demultiplexes Docker log stream.
    /// Docker uses an 8-byte header: [STREAM_TYPE][0][0][0][SIZE1][SIZE2][SIZE3][SIZE4]
    /// </summary>
    private static async Task<string> ReadDockerLogStreamAsync(MultiplexedStream stream, CancellationToken cancellationToken)
    {
        // Read all output from the multiplexed stream
        using var stdout = new MemoryStream();
        using var stderr = new MemoryStream();
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
                content += "\nstderr:\n" + stderrContent;
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
        using var activity = _telemetryProvider.StartActivity("bannou.orchestrator", "DockerComposeOrchestrator.DeployServiceAsync");
        _logger.LogInformation(
            "Deploying service: {ServiceName} with app-id: {AppId}",
            serviceName, appId);

        try
        {
            // For Docker Compose, deployment involves:
            // 1. Creating the application container with appropriate image and labels
            // 2. Connecting to the correct Docker network
            // 3. Starting the container

            var imageName = _configuration.DockerImageName;
            var containerName = $"bannou-{appId}";

            // Validate that the bannou image is present locally
            try
            {
                await _client.Images.InspectImageAsync(imageName, cancellationToken);
            }
            catch (DockerApiException)
            {
                var msg = $"Docker image '{imageName}' not found locally. Run 'make build-compose' or build/pull the image before deploying.";
                _logger.LogError(msg);
                return new DeployServiceResult
                {
                    Success = false,
                    AppId = appId,
                    Message = msg
                };
            }

            var certificatesPath = ResolveHostPath(
                _certificatesHostPath,
                "/app/provisioning/certificates",
                "provisioning/certificates");

            var presetsPath = ResolveHostPath(
                _presetsHostPath,
                "/app/provisioning/orchestrator/presets",
                "provisioning/orchestrator/presets");

            // Prepare application container environment
            // Get the orchestrator's own app-id to set as the mapping source
            var orchestratorAppId = _appConfiguration.EffectiveAppId;

            var envList = new List<string>
            {
                $"BANNOU_APP_ID={appId}",
                // Tell deployed container to query this orchestrator for initial service mappings
                // This ensures new containers have correct routing info before participating in network
                $"BANNOU_MAPPING_SOURCE_APP_ID={orchestratorAppId}",
                // Set the HTTP endpoint to the orchestrator so the new container can reach it
                // via ExtraHosts DNS resolution (not localhost which would hit itself)
                $"BANNOU_HTTP_ENDPOINT=http://{orchestratorAppId}",
                // Required for proper service operation - not forwarded from orchestrator ENV
                "DAEMON_MODE=true",
                "BANNOU_HEARTBEAT_ENABLED=true"
            };

            if (certificatesPath != null)
            {
                envList.Add("SSL_CERT_DIR=/certificates");
            }

            if (environment != null)
            {
                envList.AddRange(environment.Select(kv => $"{kv.Key}={kv.Value}"));
            }

            // Clean up any existing container with this name
            await CleanupExistingContainerAsync(containerName, cancellationToken);

            // Discover the Docker network to use - required for container communication
            var dockerNetwork = await DiscoverDockerNetworkAsync(cancellationToken);
            if (dockerNetwork == null)
            {
                var msg = $"Could not discover Docker network. Ensure Docker Compose is running with a network " +
                        $"matching '{_configuredDockerNetwork}' or containing 'bannou'. " +
                        "Set BANNOU_DockerNetwork to specify the network explicitly.";
                _logger.LogError(msg);
                return new DeployServiceResult
                {
                    Success = false,
                    AppId = appId,
                    Message = msg
                };
            }

            _logger.LogInformation("Using Docker network: {Network}", dockerNetwork);

            // IMPORTANT: Docker DNS (127.0.0.11) doesn't work reliably for containers created via Docker API
            // (as opposed to Docker Compose). We need to inject ExtraHosts with actual IP addresses for
            // infrastructure services and Docker Compose-managed containers.

            // Discover infrastructure IPs for ExtraHosts injection
            var infrastructureHosts = await DiscoverInfrastructureHostsAsync(dockerNetwork, cancellationToken);

            // Build ExtraHosts list from discovered infrastructure
            // Format is "hostname:ip" for each service
            var extraHosts = infrastructureHosts
                .Select(kv => $"{kv.Key}:{kv.Value}")
                .ToList();

            // Also add full container names with project prefix (e.g., bannou-test-edge-rabbitmq-1)
            // Docker Compose creates container names with project prefix, and some services reference these full names
            foreach (var kv in infrastructureHosts)
            {
                // The compose container name typically follows pattern: {project}-{service}-{number}
                // We try to find the actual container name that matches this service
                var fullContainerName = await FindContainerFullNameByServiceAsync(kv.Key, dockerNetwork, cancellationToken);
                if (fullContainerName != null && fullContainerName != kv.Key)
                {
                    extraHosts.Add($"{fullContainerName}:{kv.Value}");
                }
            }

            _logger.LogDebug("ExtraHosts for dynamic containers: {ExtraHosts}", string.Join(", ", extraHosts));

            // Build bind mounts list
            var bindMounts = new List<string>();
            if (certificatesPath != null)
            {
                bindMounts.Add($"{certificatesPath}:/certificates:ro");
            }
            if (presetsPath != null)
            {
                bindMounts.Add($"{presetsPath}:/app/provisioning/orchestrator/presets:ro");
            }

            // Create the application container
            _logger.LogInformation("Creating application container: {ContainerName}", containerName);

            var appCreateParams = new Docker.DotNet.Models.CreateContainerParameters
            {
                Image = imageName,
                Name = containerName,
                Env = envList,
                Labels = new Dictionary<string, string>
                {
                    [BANNOU_APP_ID_LABEL] = appId,
                    ["bannou.service"] = serviceName,
                    [BANNOU_ORCHESTRATOR_MANAGED_LABEL] = "true"
                },
                HostConfig = new Docker.DotNet.Models.HostConfig
                {
                    RestartPolicy = new Docker.DotNet.Models.RestartPolicy
                    {
                        Name = Docker.DotNet.Models.RestartPolicyKind.UnlessStopped
                    },
                    Mounts = new List<Docker.DotNet.Models.Mount>
                    {
                        // Match compose behaviour: persist logs
                        new Docker.DotNet.Models.Mount
                        {
                            Type = "volume",
                            Source = _logsVolumeName,
                            Target = "/app/logs"
                        }
                    },
                    Binds = bindMounts,
                    // ExtraHosts needed because Docker DNS doesn't work for dynamically created containers
                    ExtraHosts = extraHosts
                },
                NetworkingConfig = new Docker.DotNet.Models.NetworkingConfig
                {
                    EndpointsConfig = new Dictionary<string, Docker.DotNet.Models.EndpointSettings>
                    {
                        [dockerNetwork] = new Docker.DotNet.Models.EndpointSettings
                        {
                            Aliases = new List<string> { containerName, appId }
                        }
                    }
                }
            };

            var appContainer = await _client.Containers.CreateContainerAsync(appCreateParams, cancellationToken);
            _logger.LogInformation("Application container created: {ContainerId}", appContainer.ID[..12]);

            // Start the application container
            await _client.Containers.StartContainerAsync(appContainer.ID, new Docker.DotNet.Models.ContainerStartParameters(), cancellationToken);
            _logger.LogInformation("Application container started: {ContainerId}", appContainer.ID[..12]);

            _logger.LogInformation(
                "Service {ServiceName} deployed successfully: app={AppId}, container={ContainerId}",
                serviceName, appId, appContainer.ID[..12]);

            return new DeployServiceResult
            {
                Success = true,
                AppId = appId,
                ContainerId = appContainer.ID,
                Message = $"Service {serviceName} deployed with container {appContainer.ID[..12]}"
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

    /// <summary>
    /// Helper to clean up an existing container by name.
    /// </summary>
    private async Task CleanupExistingContainerAsync(string containerName, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.orchestrator", "DockerComposeOrchestrator.CleanupExistingContainerAsync");
        var existingContainers = await _client.Containers.ListContainersAsync(
            new Docker.DotNet.Models.ContainersListParameters
            {
                All = true,
                Filters = new Dictionary<string, IDictionary<string, bool>>
                {
                    ["name"] = new Dictionary<string, bool> { [$"^/{containerName}$"] = true }
                }
            },
            cancellationToken);

        foreach (var existing in existingContainers)
        {
            _logger.LogInformation("Removing existing container: {ContainerName} ({ContainerId})", containerName, existing.ID[..12]);

            if (existing.State == "running")
            {
                await _client.Containers.StopContainerAsync(
                    existing.ID,
                    new Docker.DotNet.Models.ContainerStopParameters { WaitBeforeKillSeconds = 10 },
                    cancellationToken);
            }

            await _client.Containers.RemoveContainerAsync(
                existing.ID,
                new Docker.DotNet.Models.ContainerRemoveParameters { Force = true },
                cancellationToken);
        }
    }

    /// <inheritdoc />
    public async Task<TeardownServiceResult> TeardownServiceAsync(
        string appName,
        bool removeVolumes = false,
        CancellationToken cancellationToken = default)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.orchestrator", "DockerComposeOrchestrator.TeardownServiceAsync");
        _logger.LogInformation(
            "Tearing down service: {AppName}, removeVolumes: {RemoveVolumes}",
            appName, removeVolumes);

        var stoppedContainers = new List<string>();

        try
        {
            // Find and remove the application container
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

            // Stop and remove the application container
            _logger.LogInformation("Stopping application container: {ContainerId}", container.ID[..12]);

            if (container.State == "running")
            {
                await _client.Containers.StopContainerAsync(
                    container.ID,
                    new Docker.DotNet.Models.ContainerStopParameters { WaitBeforeKillSeconds = 10 },
                    cancellationToken);
            }

            await _client.Containers.RemoveContainerAsync(
                container.ID,
                new Docker.DotNet.Models.ContainerRemoveParameters { RemoveVolumes = removeVolumes },
                cancellationToken);

            stoppedContainers.Add(container.ID[..12]);

            _logger.LogInformation(
                "Service {AppName} torn down successfully, {Count} container(s) removed",
                appName, stoppedContainers.Count);

            return new TeardownServiceResult
            {
                Success = true,
                AppId = appName,
                StoppedContainers = stoppedContainers,
                RemovedVolumes = new List<string>(),
                Message = $"Removed {stoppedContainers.Count} container(s): {string.Join(", ", stoppedContainers)}"
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
    public async Task<ScaleServiceResult> ScaleServiceAsync(
        string appName,
        int replicas,
        CancellationToken cancellationToken = default)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.orchestrator", "DockerComposeOrchestrator.ScaleServiceAsync");
        await Task.CompletedTask;
        _logger.LogWarning(
            "Scale operation not supported in Docker Compose mode for {AppName}. Use Docker Swarm for scaling.",
            appName);

        // Docker Compose (standalone mode) doesn't support native scaling
        // For scaling, use Docker Swarm or Kubernetes backend
        return new ScaleServiceResult
        {
            Success = false,
            AppId = appName,
            PreviousReplicas = 1,
            CurrentReplicas = 1,
            Message = "Scaling not supported in Docker Compose mode. Use Docker Swarm or Kubernetes for scaling capabilities."
        };
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> ListInfrastructureServicesAsync(CancellationToken cancellationToken = default)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.orchestrator", "DockerComposeOrchestrator.ListInfrastructureServicesAsync");
        _logger.LogDebug("Listing infrastructure services (Docker Compose mode)");

        try
        {
            // In Docker Compose mode, we identify infrastructure services by:
            // 1. Container names that match known infrastructure patterns
            // 2. Containers in the same Docker Compose project
            var infrastructurePatterns = new[] { "redis", "rabbitmq", "mysql", "mariadb", "postgres", "mongodb" };

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

    /// <inheritdoc />
    public async Task<PruneResult> PruneNetworksAsync(CancellationToken cancellationToken = default)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.orchestrator", "DockerComposeOrchestrator.PruneNetworksAsync");
        _logger.LogInformation("Pruning unused networks");

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
                ReclaimedBytes = 0, // Networks don't report reclaimed bytes
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
        using var activity = _telemetryProvider.StartActivity("bannou.orchestrator", "DockerComposeOrchestrator.PruneVolumesAsync");
        _logger.LogInformation("Pruning unused volumes");

        try
        {
            var response = await _client.Volumes.PruneAsync(
                new Docker.DotNet.Models.VolumesPruneParameters(),
                cancellationToken);

            var deletedVolumes = response.VolumesDeleted ?? new List<string>();
            var reclaimedBytes = (long)response.SpaceReclaimed;

            _logger.LogInformation(
                "Pruned {Count} unused volumes, reclaimed {Bytes} bytes: {Volumes}",
                deletedVolumes.Count,
                reclaimedBytes,
                string.Join(", ", deletedVolumes));

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
        using var activity = _telemetryProvider.StartActivity("bannou.orchestrator", "DockerComposeOrchestrator.PruneImagesAsync");
        _logger.LogInformation("Pruning dangling images");

        try
        {
            var response = await _client.Images.PruneImagesAsync(
                new Docker.DotNet.Models.ImagesPruneParameters(),
                cancellationToken);

            var deletedImages = response.ImagesDeleted ?? new List<Docker.DotNet.Models.ImageDeleteResponse>();
            var reclaimedBytes = (long)response.SpaceReclaimed;

            // Extract image IDs from the response
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
    /// Formats bytes into a human-readable string (e.g., "1.5 GB").
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

    /// <inheritdoc />
    public void Dispose()
    {
        _client.Dispose();
    }
}
