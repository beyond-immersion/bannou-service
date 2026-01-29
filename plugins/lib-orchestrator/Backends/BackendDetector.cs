using BeyondImmersion.BannouService.Configuration;
using BeyondImmersion.BannouService.Orchestrator;
using Docker.DotNet;
using Microsoft.Extensions.Logging;

namespace LibOrchestrator.Backends;

/// <summary>
/// Detects available container orchestration backends and selects the best one.
/// Priority order: Kubernetes > Portainer > Swarm > Compose
/// </summary>
public class BackendDetector : IBackendDetector
{
    private readonly ILogger<BackendDetector> _logger;
    private readonly OrchestratorServiceConfiguration _configuration;
    private readonly AppConfiguration _appConfiguration;
    private readonly IHttpClientFactory _httpClientFactory;

    /// <summary>
    /// Backend priority order (lower = higher priority).
    /// </summary>
    private static readonly Dictionary<BackendType, int> BackendPriorities = new()
    {
        { BackendType.Kubernetes, 1 },
        { BackendType.Portainer, 2 },
        { BackendType.Swarm, 3 },
        { BackendType.Compose, 4 }
    };

    public BackendDetector(
        ILogger<BackendDetector> logger,
        OrchestratorServiceConfiguration configuration,
        AppConfiguration appConfiguration,
        IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _configuration = configuration;
        _appConfiguration = appConfiguration;
        _httpClientFactory = httpClientFactory;
    }

    /// <summary>
    /// Detects all available backends and returns information about each.
    /// </summary>
    public async Task<BackendsResponse> DetectBackendsAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Detecting available container orchestration backends...");

        var backends = new List<BackendInfo>();
        var detectionTasks = new List<Task<BackendInfo>>
        {
            DetectKubernetesAsync(cancellationToken),
            DetectPortainerAsync(cancellationToken),
            DetectDockerAsync(cancellationToken) // This checks both Swarm and Compose
        };

        var results = await Task.WhenAll(detectionTasks);

        // Flatten results (Docker detection returns both Swarm and Compose)
        foreach (var result in results)
        {
            if (result != null)
            {
                backends.Add(result);
            }
        }

        // If Docker detection found Swarm, also add Compose as available (since Docker is available)
        var hasSwarm = backends.Any(b => b.Type == BackendType.Swarm && b.Available);
        var hasCompose = backends.Any(b => b.Type == BackendType.Compose);
        if (hasSwarm && !hasCompose)
        {
            backends.Add(new BackendInfo
            {
                Type = BackendType.Compose,
                Available = true,
                Priority = BackendPriorities[BackendType.Compose],
                Endpoint = "Docker socket (Swarm mode active)"
            });
        }

        // Determine recommended backend (highest priority that is available)
        var recommended = backends
            .Where(b => b.Available)
            .OrderBy(b => b.Priority)
            .FirstOrDefault()?.Type ?? BackendType.Compose;

        _logger.LogInformation(
            "Backend detection complete. Found {Count} backends, recommended: {Recommended}",
            backends.Count(b => b.Available),
            recommended);

        return new BackendsResponse
        {
            Timestamp = DateTimeOffset.UtcNow,
            Backends = backends,
            Recommended = recommended
        };
    }

    /// <summary>
    /// Creates an orchestrator instance for the specified backend type.
    /// </summary>
    public IContainerOrchestrator CreateOrchestrator(BackendType backendType)
    {
        return backendType switch
        {
            BackendType.Kubernetes => CreateKubernetesOrchestrator(),
            BackendType.Portainer => CreatePortainerOrchestrator(),
            BackendType.Swarm => CreateSwarmOrchestrator(),
            BackendType.Compose => CreateComposeOrchestrator(),
            _ => throw new ArgumentException($"Unknown backend type: {backendType}", nameof(backendType))
        };
    }

    /// <summary>
    /// Creates an orchestrator for the best available backend.
    /// </summary>
    public async Task<IContainerOrchestrator> CreateBestOrchestratorAsync(CancellationToken cancellationToken = default)
    {
        var detection = await DetectBackendsAsync(cancellationToken);
        return CreateOrchestrator(detection.Recommended);
    }

    private async Task<BackendInfo> DetectKubernetesAsync(CancellationToken cancellationToken)
    {
        await Task.CompletedTask;
        var info = new BackendInfo
        {
            Type = BackendType.Kubernetes,
            Priority = BackendPriorities[BackendType.Kubernetes],
            Available = false
        };

        try
        {
            // Check if running in-cluster (service account exists)
            var inClusterTokenPath = "/var/run/secrets/kubernetes.io/serviceaccount/token";
            var inClusterCaPath = "/var/run/secrets/kubernetes.io/serviceaccount/ca.crt";

            if (File.Exists(inClusterTokenPath) && File.Exists(inClusterCaPath))
            {
                _logger.LogDebug("Kubernetes in-cluster credentials found");
                info.Available = true;
                info.Endpoint = "in-cluster";
                return info;
            }

            // Check for kubeconfig file - use configuration if set, otherwise default path
            var kubeconfigPath = _configuration.KubeconfigPath
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".kube", "config");

            if (File.Exists(kubeconfigPath))
            {
                _logger.LogDebug("Kubernetes kubeconfig found at {Path}", kubeconfigPath);
                info.Available = true;
                info.Endpoint = kubeconfigPath;
                return info;
            }

            info.Error = "No Kubernetes credentials found";
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error detecting Kubernetes");
            info.Error = $"Detection error: {ex.Message}";
        }

        return info;
    }

    private async Task<BackendInfo> DetectPortainerAsync(CancellationToken cancellationToken)
    {
        var info = new BackendInfo
        {
            Type = BackendType.Portainer,
            Priority = BackendPriorities[BackendType.Portainer],
            Available = false
        };

        try
        {
            var portainerUrl = _configuration.PortainerUrl;
            if (string.IsNullOrEmpty(portainerUrl))
            {
                info.Error = "PORTAINER_URL not configured";
                return info;
            }

            // Try to reach Portainer status endpoint
            var httpClient = _httpClientFactory.CreateClient("Portainer");
            httpClient.BaseAddress = new Uri(portainerUrl);
            httpClient.Timeout = TimeSpan.FromSeconds(5);

            // Add API key if configured
            var apiKey = _configuration.PortainerApiKey;
            if (!string.IsNullOrEmpty(apiKey))
            {
                httpClient.DefaultRequestHeaders.Add("X-API-Key", apiKey);
            }

            var response = await httpClient.GetAsync("/api/status", cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                info.Available = true;
                info.Endpoint = portainerUrl;
                _logger.LogDebug("Portainer detected at {Url}", portainerUrl);
            }
            else
            {
                info.Error = $"Portainer returned {response.StatusCode}";
            }
        }
        catch (HttpRequestException ex)
        {
            info.Error = $"Connection failed: {ex.Message}";
            _logger.LogDebug(ex, "Portainer not available");
        }
        catch (TaskCanceledException)
        {
            info.Error = "Connection timeout";
        }
        catch (Exception ex)
        {
            info.Error = $"Detection error: {ex.Message}";
            _logger.LogDebug(ex, "Error detecting Portainer");
        }

        return info;
    }

    private async Task<BackendInfo> DetectDockerAsync(CancellationToken cancellationToken)
    {
        var composeInfo = new BackendInfo
        {
            Type = BackendType.Compose,
            Priority = BackendPriorities[BackendType.Compose],
            Available = false
        };

        try
        {
            // Create Docker client using default socket
            // Linux: unix:///var/run/docker.sock
            // Windows: npipe://./pipe/docker_engine
            using var config = new DockerClientConfiguration();
            using var client = config.CreateClient();

            // Check Docker availability with ping
            await client.System.PingAsync(cancellationToken);

            // Get system info to check Swarm mode
            // See ORCHESTRATOR-SDK-REFERENCE.md for API documentation
            var systemInfo = await client.System.GetSystemInfoAsync(cancellationToken);

            // Check if Swarm mode is active
            // LocalNodeState values: "inactive", "pending", "active", "error", "locked"
            var swarmState = systemInfo.Swarm?.LocalNodeState;
            var isSwarmActive = swarmState == "active";
            var isManager = systemInfo.Swarm?.ControlAvailable == true;

            if (isSwarmActive)
            {
                _logger.LogDebug(
                    "Docker Swarm detected. LocalNodeState={State}, IsManager={IsManager}",
                    swarmState,
                    isManager);

                // Return Swarm info if active
                return new BackendInfo
                {
                    Type = BackendType.Swarm,
                    Priority = BackendPriorities[BackendType.Swarm],
                    Available = true,
                    Endpoint = isManager
                        ? "Swarm manager node"
                        : "Swarm worker node (limited functionality)"
                };
            }

            // Docker available but not in Swarm mode - use Compose
            _logger.LogDebug("Docker detected in standalone mode (Compose)");
            composeInfo.Available = true;
            composeInfo.Version = systemInfo.ServerVersion;
            composeInfo.Endpoint = "docker.sock";
        }
        catch (DockerApiException ex)
        {
            composeInfo.Error = $"Docker API error: {ex.Message}";
            _logger.LogDebug(ex, "Docker API error during detection");
        }
        catch (Exception ex) when (ex.Message.Contains("socket") || ex.Message.Contains("pipe"))
        {
            composeInfo.Error = "Docker socket not accessible";
            _logger.LogDebug("Docker socket not accessible");
        }
        catch (Exception ex)
        {
            composeInfo.Error = $"Detection error: {ex.Message}";
            _logger.LogDebug(ex, "Error detecting Docker");
        }

        return composeInfo;
    }

    private IContainerOrchestrator CreateKubernetesOrchestrator()
    {
        // Kubernetes implementation uses KubernetesClient SDK
        return new KubernetesOrchestrator(
            _logger.GetType().Assembly.CreateLogger<KubernetesOrchestrator>(),
            _configuration);
    }

    private IContainerOrchestrator CreatePortainerOrchestrator()
    {
        return new PortainerOrchestrator(
            _logger.GetType().Assembly.CreateLogger<PortainerOrchestrator>(),
            _configuration,
            _httpClientFactory);
    }

    private IContainerOrchestrator CreateSwarmOrchestrator()
    {
        return new DockerSwarmOrchestrator(
            _configuration,
            _logger.GetType().Assembly.CreateLogger<DockerSwarmOrchestrator>());
    }

    private IContainerOrchestrator CreateComposeOrchestrator()
    {
        return new DockerComposeOrchestrator(
            _configuration,
            _appConfiguration,
            _logger.GetType().Assembly.CreateLogger<DockerComposeOrchestrator>());
    }
}

/// <summary>
/// Extension method to create typed loggers from assembly.
/// </summary>
internal static class LoggerExtensions
{
    public static ILogger<T> CreateLogger<T>(this System.Reflection.Assembly assembly)
    {
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        return loggerFactory.CreateLogger<T>();
    }
}
