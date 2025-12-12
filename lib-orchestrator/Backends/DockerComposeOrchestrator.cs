using BeyondImmersion.BannouService;
using Docker.DotNet;
using Docker.DotNet.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
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

    /// <summary>
    /// Label to link sidecar containers to their app containers.
    /// </summary>
    private const string BANNOU_SIDECAR_FOR_LABEL = "bannou.sidecar-for";

    // Configuration from environment variables
    private readonly string _configuredDockerNetwork;
    private readonly string _daprComponentsHostPath;
    private readonly string _daprComponentsContainerPath;
    private readonly string _daprImage;
    private readonly string _placementHost;
    private readonly string _certificatesHostPath;
    private readonly string _presetsHostPath;
    private readonly string _logsVolumeName;
    // Note: No custom Dapr config needed - mDNS (default) works for standalone containers on bridge network

    // Cached discovered network (lazy initialized)
    private string? _discoveredNetwork;
    private bool _networkDiscoveryAttempted;
    // Cached discovered placement host (lazy initialized)
    private string? _discoveredPlacementHost;
    private bool _placementDiscoveryAttempted;
    // Cached discovered infrastructure hosts (service name -> IP address)
    private Dictionary<string, string>? _discoveredInfrastructureHosts;
    private bool _infrastructureDiscoveryAttempted;

    public DockerComposeOrchestrator(ILogger<DockerComposeOrchestrator> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Create Docker client using default configuration
        // Linux: unix:///var/run/docker.sock
        // Windows: npipe://./pipe/docker_engine
        _client = new DockerClientConfiguration().CreateClient();

        // Read configuration from environment variables
        // These are required for deploying containers with Dapr sidecars
        _configuredDockerNetwork = Environment.GetEnvironmentVariable("BANNOU_DockerNetwork")
            ?? "bannou_default";
        _daprComponentsHostPath = Environment.GetEnvironmentVariable("BANNOU_DaprComponentsHostPath")
            ?? "/app/provisioning/dapr/components";
        // Container-visible path for the mounted components directory (RW mount expected)
        _daprComponentsContainerPath = Environment.GetEnvironmentVariable("BANNOU_DaprComponentsContainerPath")
            ?? "/tmp/dapr-components";
        _daprImage = Environment.GetEnvironmentVariable("BANNOU_DaprImage")
            ?? "daprio/daprd:1.16.3";
        _placementHost = Environment.GetEnvironmentVariable("BANNOU_PlacementHost")
            ?? "placement:50006";
        _certificatesHostPath = Environment.GetEnvironmentVariable("BANNOU_CertificatesHostPath")
            ?? "/app/provisioning/certificates";
        _presetsHostPath = Environment.GetEnvironmentVariable("BANNOU_PresetsHostPath")
            ?? "/app/provisioning/orchestrator/presets";
        _logsVolumeName = Environment.GetEnvironmentVariable("BANNOU_LogsVolume")
            ?? "logs-data";
        // Note: No custom Dapr config needed - mDNS (default) works for standalone containers on bridge network

        _logger.LogInformation(
        "DockerComposeOrchestrator configured: Network={Network}, DaprComponents={Components}, DaprImage={Image}, Placement={Placement}",
        _configuredDockerNetwork, _daprComponentsHostPath, _daprImage, _placementHost);
    }

    // NOTE: GenerateComponentsWithIpAddresses and EnsureDnsNameResolutionConfig methods removed
    // With standalone Dapr containers (not network_mode: container:ID), Docker DNS works correctly
    // and we no longer need IP address substitution or custom DNS configuration generation.
    // mDNS (default Dapr resolver) handles Dapr-to-Dapr service discovery on bridge network.

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
    /// Discovers the reachable placement host for dynamically created Dapr sidecars.
    /// Looks for a running container with compose service name "placement" on the target network.
    /// Falls back to the configured placement host if discovery fails.
    /// </summary>
    private async Task<string?> DiscoverPlacementHostAsync(string? targetNetwork, CancellationToken cancellationToken)
    {
        if (_placementDiscoveryAttempted)
        {
            return _discoveredPlacementHost;
        }

        _placementDiscoveryAttempted = true;

        try
        {
            var containers = await _client.Containers.ListContainersAsync(
                new Docker.DotNet.Models.ContainersListParameters
                {
                    All = true,
                    Filters = new Dictionary<string, IDictionary<string, bool>>
                    {
                        ["label"] = new Dictionary<string, bool>
                        {
                            // docker compose applies this label to the placement service
                            ["com.docker.compose.service=placement"] = true
                        }
                    }
                },
                cancellationToken);

            // Fallback: allow name contains "placement" if label filter returns none
            if (containers.Count == 0)
            {
                containers = await _client.Containers.ListContainersAsync(
                    new Docker.DotNet.Models.ContainersListParameters
                    {
                        All = true
                    },
                    cancellationToken);
                containers = containers
                    .Where(c => c.Names.Any(n => n.Contains("placement", StringComparison.OrdinalIgnoreCase)))
                    .ToList();
            }

            // Choose the container on the target network if provided
            var candidate = containers
                .FirstOrDefault(c =>
                    targetNetwork != null &&
                    c.NetworkSettings?.Networks != null &&
                    c.NetworkSettings.Networks.ContainsKey(targetNetwork))
                ?? containers.FirstOrDefault();

            if (candidate != null)
            {
                var rawName = candidate.Names.FirstOrDefault()?.TrimStart('/');
                if (!string.IsNullOrWhiteSpace(rawName))
                {
                    var port = _placementHost.Contains(':')
                        ? _placementHost.Split(':').Last()
                        : "50006";

                    _discoveredPlacementHost = $"{rawName}:{port}";
                    _logger.LogInformation("Discovered placement host: {PlacementHost}", _discoveredPlacementHost);
                    return _discoveredPlacementHost;
                }
            }

            _logger.LogWarning("Could not discover placement host container; falling back to configured value {Placement}", _placementHost);
            _discoveredPlacementHost = null;
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error discovering placement host; will fall back to configured value");
            _discoveredPlacementHost = null;
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
        if (_infrastructureDiscoveryAttempted && _discoveredInfrastructureHosts != null)
        {
            return _discoveredInfrastructureHosts;
        }

        _infrastructureDiscoveryAttempted = true;
        _discoveredInfrastructureHosts = new Dictionary<string, string>();

        // Infrastructure service names that Dapr components reference, plus Docker Compose services that
        // dynamically created containers need to resolve (placement is especially critical for Dapr sidecars)
        var serviceNames = new[] { "rabbitmq", "redis", "bannou-redis", "auth-redis", "routing-redis", "account-db", "mysql", "placement" };

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
            // 1. Creating the application container with appropriate image and labels
            // 2. Creating a Dapr sidecar container that shares the app's network namespace
            // 3. Connecting both to the correct Docker network
            // 4. Starting both containers

            var imageName = "bannou:latest";
            var containerName = $"bannou-{appId}";
            var sidecarName = $"{appId}-dapr";

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

            // Ensure required host paths exist
            var componentsPath = ResolveHostPath(
                _daprComponentsHostPath,
                "/app/provisioning/dapr/components",
                "provisioning/dapr/components",
                "dapr/components");

            if (componentsPath == null)
            {
                var msg = $"Dapr components path not found (checked {_daprComponentsHostPath}). " +
                        "Set BANNOU_DaprComponentsHostPath or ensure provisioning/dapr/components exists.";
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
            var orchestratorAppId = Environment.GetEnvironmentVariable("DAPR_APP_ID")
                ?? AppConstants.DEFAULT_APP_NAME;

            // With standalone Dapr containers, the app reaches Dapr via Docker DNS
            var envList = new List<string>
            {
                // Dapr endpoints - use Docker DNS to reach standalone Dapr container
                $"DAPR_HTTP_ENDPOINT=http://{sidecarName}:3500",
                $"DAPR_GRPC_ENDPOINT=http://{sidecarName}:50001",
                $"DAPR_APP_ID={appId}",
                // Tell deployed container to query this orchestrator for initial service mappings
                // This ensures new containers have correct routing info before participating in network
                $"BANNOU_MappingSourceAppId={orchestratorAppId}"
            };

            if (certificatesPath != null)
            {
                envList.Add("SSL_CERT_DIR=/certificates");
            }

            if (environment != null)
            {
                envList.AddRange(environment.Select(kv => $"{kv.Key}={kv.Value}"));
            }

            // Clean up any existing containers with these names
            await CleanupExistingContainerAsync(containerName, cancellationToken);
            await CleanupExistingContainerAsync(sidecarName, cancellationToken);

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

            // Discover placement host reachable from dynamically created containers
            var placementHost = await DiscoverPlacementHostAsync(dockerNetwork, cancellationToken)
                ?? _placementHost;

            // IMPORTANT: Docker DNS (127.0.0.11) doesn't work reliably for containers created via Docker API
            // (as opposed to Docker Compose). We need to inject ExtraHosts with actual IP addresses for
            // infrastructure services and Docker Compose-managed containers.
            var componentsHostPath = componentsPath ?? _daprComponentsHostPath;

            // Discover infrastructure IPs for ExtraHosts injection
            var infrastructureHosts = await DiscoverInfrastructureHostsAsync(dockerNetwork, cancellationToken);

            // Build ExtraHosts list from discovered infrastructure
            // Format is "hostname:ip" for each service
            var extraHosts = infrastructureHosts
                .Select(kv => $"{kv.Key}:{kv.Value}")
                .ToList();

            // Also add full container names with project prefix (e.g., bannou-test-edge-placement-1)
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

            // Step 1: Create the Dapr sidecar container FIRST (standalone, with its own network)
            // NOTE: Order changed - create sidecar before app so app can depend on sidecar being available
            _logger.LogInformation("Creating Dapr sidecar container: {SidecarName}", sidecarName);

            var sidecarBindMounts = new List<string> { $"{componentsHostPath}:/components:ro" };
            // Note: No custom Dapr config needed - mDNS (default) works for standalone containers on bridge network
            var sidecarEnv = new List<string>();
            if (certificatesPath != null)
            {
                sidecarBindMounts.Add($"{certificatesPath}:/certificates:ro");
                sidecarEnv.Add("SSL_CERT_DIR=/certificates");
            }

            var sidecarCmd = new List<string>
            {
                "./daprd",
                "--app-id", appId,
                "--app-port", "80",
                "--app-protocol", "http",
                "--app-channel-address", containerName,  // Docker DNS resolves this to the app container
                "--dapr-http-port", "3500",
                "--dapr-grpc-port", "50001",
                "--placement-host-address", placementHost,
                "--resources-path", "/components",
                // Note: No --config flag - using mDNS (default) for Dapr-to-Dapr service discovery
                "--log-level", "info",
                "--enable-api-logging"
            };

            var sidecarCreateParams = new Docker.DotNet.Models.CreateContainerParameters
            {
                Image = _daprImage,
                Name = sidecarName,
                Env = sidecarEnv.Count > 0 ? sidecarEnv : null,
                Cmd = sidecarCmd,
                Labels = new Dictionary<string, string>
                {
                    [DAPR_APP_ID_LABEL] = appId,
                    [BANNOU_SIDECAR_FOR_LABEL] = containerName,
                    ["bannou.managed"] = "true"
                },
                HostConfig = new Docker.DotNet.Models.HostConfig
                {
                    // NO NetworkMode = container:ID - standalone container with own IP
                    RestartPolicy = new Docker.DotNet.Models.RestartPolicy
                    {
                        Name = Docker.DotNet.Models.RestartPolicyKind.UnlessStopped
                    },
                    Binds = sidecarBindMounts,
                    // ExtraHosts needed because Docker DNS doesn't work for dynamically created containers
                    ExtraHosts = extraHosts
                },
                // Sidecar gets its own network endpoint (standalone container)
                // Network alias allows app container to reach sidecar by service name
                NetworkingConfig = new Docker.DotNet.Models.NetworkingConfig
                {
                    EndpointsConfig = new Dictionary<string, Docker.DotNet.Models.EndpointSettings>
                    {
                        [dockerNetwork] = new Docker.DotNet.Models.EndpointSettings
                        {
                            Aliases = new List<string> { sidecarName, $"{appId}-dapr" }
                        }
                    }
                }
            };

            var sidecarContainer = await _client.Containers.CreateContainerAsync(sidecarCreateParams, cancellationToken);
            _logger.LogInformation("Dapr sidecar container created: {ContainerId}", sidecarContainer.ID[..12]);

            // Step 2: Start the sidecar container
            await _client.Containers.StartContainerAsync(sidecarContainer.ID, new Docker.DotNet.Models.ContainerStartParameters(), cancellationToken);
            _logger.LogInformation("Dapr sidecar container started: {ContainerId}", sidecarContainer.ID[..12]);

            // Step 3: Create the application container
            _logger.LogInformation("Creating application container: {ContainerName}", containerName);

            var appCreateParams = new Docker.DotNet.Models.CreateContainerParameters
            {
                Image = imageName,
                Name = containerName,
                Env = envList,
                Labels = new Dictionary<string, string>
                {
                    [DAPR_APP_ID_LABEL] = appId,
                    ["bannou.service"] = serviceName,
                    ["bannou.managed"] = "true"
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
                // Network alias allows sidecar to reach app by container name (for --app-channel-address)
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

            // Step 4: Start the application container
            await _client.Containers.StartContainerAsync(appContainer.ID, new Docker.DotNet.Models.ContainerStartParameters(), cancellationToken);
            _logger.LogInformation("Application container started: {ContainerId}", appContainer.ID[..12]);

            _logger.LogInformation(
                "Service {ServiceName} deployed successfully: app={AppId}, container={ContainerId}, sidecar={SidecarId}",
                serviceName, appId, appContainer.ID[..12], sidecarContainer.ID[..12]);

            return new DeployServiceResult
            {
                Success = true,
                AppId = appId,
                ContainerId = appContainer.ID,
                Message = $"Service {serviceName} deployed with app container {appContainer.ID[..12]} and sidecar {sidecarContainer.ID[..12]}"
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
                    Message = $"No container found with Dapr app-id '{appName}'"
                };
            }

            // Find the associated sidecar container (named {appName}-dapr)
            var sidecarName = $"{appName}-dapr";
            var sidecarContainers = await _client.Containers.ListContainersAsync(
                new Docker.DotNet.Models.ContainersListParameters
                {
                    All = true,
                    Filters = new Dictionary<string, IDictionary<string, bool>>
                    {
                        ["name"] = new Dictionary<string, bool> { [$"^/{sidecarName}$"] = true }
                    }
                },
                cancellationToken);

            // Stop and remove sidecar first (depends on app container's network)
            foreach (var sidecar in sidecarContainers)
            {
                _logger.LogInformation("Stopping sidecar container: {SidecarId}", sidecar.ID[..12]);

                if (sidecar.State == "running")
                {
                    await _client.Containers.StopContainerAsync(
                        sidecar.ID,
                        new Docker.DotNet.Models.ContainerStopParameters { WaitBeforeKillSeconds = 5 },
                        cancellationToken);
                }

                await _client.Containers.RemoveContainerAsync(
                    sidecar.ID,
                    new Docker.DotNet.Models.ContainerRemoveParameters { Force = true },
                    cancellationToken);

                stoppedContainers.Add(sidecar.ID[..12]);
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
