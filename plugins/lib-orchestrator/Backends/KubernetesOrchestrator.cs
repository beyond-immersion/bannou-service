using BeyondImmersion.BannouService.Services;
using k8s;
using k8s.Models;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using BeyondImmersion.BannouService.Orchestrator;

namespace LibOrchestrator.Backends;

/// <summary>
/// Container orchestrator implementation for Kubernetes.
/// Uses KubernetesClient SDK for pod/deployment management.
/// See docs/ORCHESTRATOR-SDK-REFERENCE.md for API documentation.
/// </summary>
public class KubernetesOrchestrator : IContainerOrchestrator
{
    private readonly ILogger<KubernetesOrchestrator> _logger;
    private readonly OrchestratorServiceConfiguration _configuration;
    private readonly Kubernetes _client;
    private readonly string _namespace;
    private readonly ITelemetryProvider _telemetryProvider;

    /// <summary>
    /// Label used to identify app-id on pods/deployments.
    /// </summary>
    private const string BANNOU_APP_ID_LABEL = "bannou.app-id";

    /// <summary>
    /// Annotation used by kubectl to track restarts.
    /// </summary>
    private const string RESTART_ANNOTATION = "kubectl.kubernetes.io/restartedAt";

    public KubernetesOrchestrator(
        ILogger<KubernetesOrchestrator> logger,
        OrchestratorServiceConfiguration configuration,
        ITelemetryProvider telemetryProvider)
    {
        _logger = logger;
        _configuration = configuration;
        ArgumentNullException.ThrowIfNull(telemetryProvider, nameof(telemetryProvider));
        _telemetryProvider = telemetryProvider;

        // Initialize Kubernetes client
        // See ORCHESTRATOR-SDK-REFERENCE.md for configuration patterns
        KubernetesClientConfiguration config;

        try
        {
            // Try in-cluster config first (running inside Kubernetes)
            config = KubernetesClientConfiguration.InClusterConfig();
            _logger.LogDebug("Using in-cluster Kubernetes configuration");
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "In-cluster config not available, falling back to kubeconfig");
            // Fall back to kubeconfig file - use configuration if set, otherwise default path
            var kubeconfigPath = configuration.KubeconfigPath
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".kube", "config");

            config = KubernetesClientConfiguration.BuildConfigFromConfigFile(kubeconfigPath);
            _logger.LogDebug("Using kubeconfig from {Path}", kubeconfigPath);
        }

        _client = new Kubernetes(config);

        // Get namespace from config or default
        _namespace = configuration.KubernetesNamespace;
    }

    /// <inheritdoc />
    public BackendType BackendType => BackendType.Kubernetes;

    /// <inheritdoc />
    public async Task<(bool Available, string Message)> CheckAvailabilityAsync(CancellationToken cancellationToken = default)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.orchestrator", "KubernetesOrchestrator.CheckAvailabilityAsync");
        try
        {
            // GetCodeAsync returns version info
            // See ORCHESTRATOR-SDK-REFERENCE.md for version detection
            var version = await _client.Version.GetCodeAsync(cancellationToken);
            return (true, $"Kubernetes {version.GitVersion}");
        }
        catch (Exception ex)
        {
            return (false, $"Kubernetes not available: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<ContainerStatus> GetContainerStatusAsync(string appName, CancellationToken cancellationToken = default)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.orchestrator", "KubernetesOrchestrator.GetContainerStatusAsync");
        _logger.LogDebug("Getting Kubernetes deployment status for app: {AppName}", appName);

        try
        {
            var deployment = await FindDeploymentByAppNameAsync(appName, cancellationToken);
            if (deployment == null)
            {
                // Try finding a pod directly (might not be part of a deployment)
                var pod = await FindPodByAppNameAsync(appName, cancellationToken);
                if (pod != null)
                {
                    return MapPodToStatus(pod, appName);
                }

                return new ContainerStatus
                {
                    AppName = appName,
                    Status = ContainerStatusType.Stopped,
                    Timestamp = DateTimeOffset.UtcNow,
                    Instances = 0
                };
            }

            return MapDeploymentToStatus(deployment, appName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting Kubernetes deployment status for {AppName}", appName);
            return new ContainerStatus
            {
                AppName = appName,
                Status = ContainerStatusType.Unhealthy,
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
        using var activity = _telemetryProvider.StartActivity("bannou.orchestrator", "KubernetesOrchestrator.RestartContainerAsync");
        _logger.LogInformation(
            "Restarting Kubernetes deployment for app: {AppName}, Priority: {Priority}, Reason: {Reason}",
            appName,
            request.Priority,
            request.Reason);

        try
        {
            var deployment = await FindDeploymentByAppNameAsync(appName, cancellationToken);
            if (deployment == null)
            {
                return new ContainerRestartResponse
                {
                    Accepted = false
                };
            }

            // Force rollout by patching annotations
            // See ORCHESTRATOR-SDK-REFERENCE.md for rolling restart pattern
            // This is equivalent to: kubectl rollout restart deployment/<name>
            var patch = new V1Patch(
                new
                {
                    spec = new
                    {
                        template = new
                        {
                            metadata = new
                            {
                                annotations = new Dictionary<string, string>
                                {
                                    { RESTART_ANNOTATION, DateTime.UtcNow.ToString("o") }
                                }
                            }
                        }
                    }
                },
                V1Patch.PatchType.StrategicMergePatch);

            // PatchNamespacedDeploymentAsync - see ORCHESTRATOR-SDK-REFERENCE.md
            await _client.AppsV1.PatchNamespacedDeploymentAsync(
                patch,
                deployment.Metadata.Name,
                _namespace,
                cancellationToken: cancellationToken);

            var desiredReplicas = deployment.Spec.Replicas ?? 1;

            _logger.LogInformation(
                "Kubernetes deployment {DeploymentName} rolling restart initiated",
                deployment.Metadata.Name);

            return new ContainerRestartResponse
            {
                Accepted = true,
                ScheduledFor = DateTimeOffset.UtcNow,
                CurrentInstances = desiredReplicas,
                RestartStrategy = RestartStrategy.Rolling
            };
        }
        catch (k8s.Autorest.HttpOperationException ex)
        {
            _logger.LogError(ex, "Kubernetes API error restarting deployment for {AppName}", appName);
            return new ContainerRestartResponse
            {
                Accepted = false
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error restarting Kubernetes deployment for {AppName}", appName);
            return new ContainerRestartResponse
            {
                Accepted = false
            };
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ContainerStatus>> ListContainersAsync(CancellationToken cancellationToken = default)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.orchestrator", "KubernetesOrchestrator.ListContainersAsync");
        _logger.LogDebug("Listing all Kubernetes deployments in namespace {Namespace}", _namespace);

        try
        {
            // ListNamespacedDeploymentAsync - see ORCHESTRATOR-SDK-REFERENCE.md
            var deployments = await _client.AppsV1.ListNamespacedDeploymentAsync(
                _namespace,
                labelSelector: BANNOU_APP_ID_LABEL,
                cancellationToken: cancellationToken);

            return deployments.Items
                .Select(d => MapDeploymentToStatus(d, GetAppNameFromDeployment(d)))
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing Kubernetes deployments");
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
        using var activity = _telemetryProvider.StartActivity("bannou.orchestrator", "KubernetesOrchestrator.GetContainerLogsAsync");
        _logger.LogDebug("Getting Kubernetes pod logs for app: {AppName}, tail: {Tail}", appName, tail);

        try
        {
            var pod = await FindPodByAppNameAsync(appName, cancellationToken);
            if (pod == null)
            {
                return $"No pod found with app-id '{appName}' in namespace '{_namespace}'";
            }

            // ReadNamespacedPodLogAsync returns a stream
            var sinceSeconds = since.HasValue
                ? (int?)(DateTimeOffset.UtcNow - since.Value).TotalSeconds
                : null;

            var logStream = await _client.CoreV1.ReadNamespacedPodLogAsync(
                pod.Metadata.Name,
                _namespace,
                tailLines: tail,
                sinceSeconds: sinceSeconds,
                timestamps: true,
                cancellationToken: cancellationToken);

            using var reader = new StreamReader(logStream);
            return await reader.ReadToEndAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting Kubernetes pod logs for {AppName}", appName);
            return $"Error retrieving logs: {ex.Message}";
        }
    }

    /// <summary>
    /// Finds a deployment by its app-id label.
    /// </summary>
    private async Task<V1Deployment?> FindDeploymentByAppNameAsync(
        string appName,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.orchestrator", "KubernetesOrchestrator.FindDeploymentByAppNameAsync");
        var deployments = await _client.AppsV1.ListNamespacedDeploymentAsync(
            _namespace,
            labelSelector: $"{BANNOU_APP_ID_LABEL}={appName}",
            cancellationToken: cancellationToken);

        return deployments.Items.FirstOrDefault();
    }

    /// <summary>
    /// Finds a pod by its app-id label.
    /// </summary>
    private async Task<V1Pod?> FindPodByAppNameAsync(
        string appName,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.orchestrator", "KubernetesOrchestrator.FindPodByAppNameAsync");
        // ListNamespacedPodAsync - see ORCHESTRATOR-SDK-REFERENCE.md
        var pods = await _client.CoreV1.ListNamespacedPodAsync(
            _namespace,
            labelSelector: $"{BANNOU_APP_ID_LABEL}={appName}",
            cancellationToken: cancellationToken);

        // Return the first running pod, or any pod if none are running
        return pods.Items
            .OrderByDescending(p => p.Status.Phase == "Running")
            .FirstOrDefault();
    }

    /// <summary>
    /// Extracts app-id from deployment labels.
    /// </summary>
    private static string GetAppNameFromDeployment(V1Deployment deployment)
    {
        if (deployment.Metadata.Labels?.TryGetValue(BANNOU_APP_ID_LABEL, out var appId) == true)
        {
            return appId;
        }

        // Try pod template labels
        if (deployment.Spec.Template.Metadata.Labels?.TryGetValue(BANNOU_APP_ID_LABEL, out appId) == true)
        {
            return appId;
        }

        return deployment.Metadata.Name;
    }

    /// <summary>
    /// Maps a Kubernetes deployment to our ContainerStatus model.
    /// </summary>
    private static ContainerStatus MapDeploymentToStatus(V1Deployment deployment, string appName)
    {
        var readyReplicas = deployment.Status.ReadyReplicas ?? 0;
        var desiredReplicas = deployment.Spec.Replicas ?? 1;

        var status = (readyReplicas, desiredReplicas) switch
        {
            (0, _) => ContainerStatusType.Stopped,
            var (r, d) when r == d => ContainerStatusType.Running,
            var (r, d) when r < d => ContainerStatusType.Starting,
            _ => ContainerStatusType.Unhealthy
        };

        return new ContainerStatus
        {
            AppName = appName,
            Status = status,
            Timestamp = DateTimeOffset.UtcNow,
            Instances = readyReplicas,
            RestartHistory = new List<RestartHistoryEntry>()
        };
    }

    /// <summary>
    /// Maps a Kubernetes pod to our ContainerStatus model.
    /// </summary>
    private static ContainerStatus MapPodToStatus(V1Pod pod, string appName)
    {
        var phase = pod.Status.Phase?.ToLowerInvariant();
        var status = phase switch
        {
            "running" => ContainerStatusType.Running,
            "pending" => ContainerStatusType.Starting,
            "succeeded" => ContainerStatusType.Stopped,
            "failed" => ContainerStatusType.Unhealthy,
            _ => ContainerStatusType.Unhealthy
        };

        return new ContainerStatus
        {
            AppName = appName,
            Status = status,
            Timestamp = DateTimeOffset.UtcNow,
            Instances = status == ContainerStatusType.Running ? 1 : 0,
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
        using var activity = _telemetryProvider.StartActivity("bannou.orchestrator", "KubernetesOrchestrator.DeployServiceAsync");
        _logger.LogInformation(
            "Deploying Kubernetes deployment: {ServiceName} with app-id: {AppId}",
            serviceName, appId);

        try
        {
            var imageName = _configuration.DockerImageName;

            // Prepare environment variables
            var envVars = new List<V1EnvVar>
            {
                new() { Name = $"{serviceName.ToUpperInvariant()}_SERVICE_ENABLED", Value = "true" },
                new() { Name = "BANNOU_APP_ID", Value = appId },
                // Required for proper service operation - not forwarded from orchestrator ENV
                new() { Name = "DAEMON_MODE", Value = "true" },
                new() { Name = "BANNOU_HEARTBEAT_ENABLED", Value = "true" }
            };

            if (environment != null)
            {
                envVars.AddRange(environment.Select(kv => new V1EnvVar { Name = kv.Key, Value = kv.Value }));
            }

            // Create deployment
            var deployment = new V1Deployment
            {
                Metadata = new V1ObjectMeta
                {
                    Name = $"bannou-{serviceName}-{appId}",
                    Labels = new Dictionary<string, string>
                    {
                        [BANNOU_APP_ID_LABEL] = appId,
                        ["bannou.service"] = serviceName
                    }
                },
                Spec = new V1DeploymentSpec
                {
                    Replicas = 1,
                    Selector = new V1LabelSelector
                    {
                        MatchLabels = new Dictionary<string, string>
                        {
                            [BANNOU_APP_ID_LABEL] = appId
                        }
                    },
                    Template = new V1PodTemplateSpec
                    {
                        Metadata = new V1ObjectMeta
                        {
                            Labels = new Dictionary<string, string>
                            {
                                [BANNOU_APP_ID_LABEL] = appId,
                                ["bannou.service"] = serviceName
                            },
                            Annotations = new Dictionary<string, string>
                            {
                                ["bannou.mesh-enabled"] = "true",
                                ["bannou.app-id"] = appId,
                                ["bannou.app-port"] = _configuration.DefaultServicePort.ToString()
                            }
                        },
                        Spec = new V1PodSpec
                        {
                            Containers = new List<V1Container>
                            {
                                new()
                                {
                                    Name = "bannou",
                                    Image = imageName,
                                    Env = envVars,
                                    Ports = new List<V1ContainerPort>
                                    {
                                        new() { ContainerPort = _configuration.DefaultServicePort, Name = "http" }
                                    }
                                }
                            }
                        }
                    }
                }
            };

            // CreateNamespacedDeploymentAsync - see ORCHESTRATOR-SDK-REFERENCE.md
            var created = await _client.AppsV1.CreateNamespacedDeploymentAsync(
                deployment,
                _namespace,
                cancellationToken: cancellationToken);

            _logger.LogInformation(
                "Kubernetes deployment {ServiceName} created successfully: {DeploymentName}",
                serviceName, created.Metadata.Name);

            return new DeployServiceResult
            {
                Success = true,
                AppId = appId,
                ContainerId = created.Metadata.Uid,
                Message = $"Deployment {created.Metadata.Name} created in namespace {_namespace}"
            };
        }
        catch (k8s.Autorest.HttpOperationException ex)
        {
            _logger.LogError(ex, "Kubernetes API error deploying deployment {ServiceName}", serviceName);
            return new DeployServiceResult
            {
                Success = false,
                AppId = appId,
                Message = $"Kubernetes API error: {ex.Message}"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deploying Kubernetes deployment {ServiceName}", serviceName);
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
        using var activity = _telemetryProvider.StartActivity("bannou.orchestrator", "KubernetesOrchestrator.TeardownServiceAsync");
        _logger.LogInformation(
            "Tearing down Kubernetes deployment: {AppName}, removeVolumes: {RemoveVolumes}",
            appName, removeVolumes);

        try
        {
            var deployment = await FindDeploymentByAppNameAsync(appName, cancellationToken);
            if (deployment == null)
            {
                return new TeardownServiceResult
                {
                    Success = false,
                    AppId = appName,
                    Message = $"No deployment found with app-id '{appName}' in namespace '{_namespace}'"
                };
            }

            // DeleteNamespacedDeploymentAsync - see ORCHESTRATOR-SDK-REFERENCE.md
            await _client.AppsV1.DeleteNamespacedDeploymentAsync(
                deployment.Metadata.Name,
                _namespace,
                cancellationToken: cancellationToken);

            _logger.LogInformation(
                "Kubernetes deployment {AppName} deleted successfully: {DeploymentName}",
                appName, deployment.Metadata.Name);

            return new TeardownServiceResult
            {
                Success = true,
                AppId = appName,
                StoppedContainers = new List<string> { deployment.Metadata.Name },
                Message = $"Deployment {deployment.Metadata.Name} deleted from namespace {_namespace}"
            };
        }
        catch (k8s.Autorest.HttpOperationException ex)
        {
            _logger.LogError(ex, "Kubernetes API error tearing down deployment {AppName}", appName);
            return new TeardownServiceResult
            {
                Success = false,
                AppId = appName,
                Message = $"Kubernetes API error: {ex.Message}"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error tearing down Kubernetes deployment {AppName}", appName);
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
        using var activity = _telemetryProvider.StartActivity("bannou.orchestrator", "KubernetesOrchestrator.ScaleServiceAsync");
        _logger.LogInformation(
            "Scaling Kubernetes deployment: {AppName} to {Replicas} replicas",
            appName, replicas);

        try
        {
            var deployment = await FindDeploymentByAppNameAsync(appName, cancellationToken);
            if (deployment == null)
            {
                return new ScaleServiceResult
                {
                    Success = false,
                    AppId = appName,
                    Message = $"No deployment found with app-id '{appName}' in namespace '{_namespace}'"
                };
            }

            var previousReplicas = deployment.Spec.Replicas ?? 1;

            // Patch the deployment with new replica count
            var patch = new V1Patch(
                new { spec = new { replicas } },
                V1Patch.PatchType.StrategicMergePatch);

            await _client.AppsV1.PatchNamespacedDeploymentScaleAsync(
                patch,
                deployment.Metadata.Name,
                _namespace,
                cancellationToken: cancellationToken);

            _logger.LogInformation(
                "Kubernetes deployment {AppName} scaled from {Previous} to {Current} replicas",
                appName, previousReplicas, replicas);

            return new ScaleServiceResult
            {
                Success = true,
                AppId = appName,
                PreviousReplicas = previousReplicas,
                CurrentReplicas = replicas,
                Message = $"Scaled deployment {deployment.Metadata.Name} from {previousReplicas} to {replicas} replicas"
            };
        }
        catch (k8s.Autorest.HttpOperationException ex)
        {
            _logger.LogError(ex, "Kubernetes API error scaling deployment {AppName}", appName);
            return new ScaleServiceResult
            {
                Success = false,
                AppId = appName,
                Message = $"Kubernetes API error: {ex.Message}"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error scaling Kubernetes deployment {AppName}", appName);
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
        using var activity = _telemetryProvider.StartActivity("bannou.orchestrator", "KubernetesOrchestrator.ListInfrastructureServicesAsync");
        _logger.LogInformation("Listing infrastructure services (Kubernetes mode)");

        try
        {
            // In Kubernetes mode, identify infrastructure by:
            // 1. Services/Deployments with specific labels
            // 2. Well-known infrastructure patterns in the same namespace
            var infrastructurePatterns = new[] { "redis", "rabbitmq", "mysql", "mariadb", "postgres", "mongodb" };

            var deployments = await _client.ListNamespacedDeploymentAsync(_namespace, cancellationToken: cancellationToken);

            var infrastructureServices = deployments.Items
                .Where(d =>
                {
                    var name = d.Metadata.Name ?? "";
                    return infrastructurePatterns.Any(pattern =>
                        name.Contains(pattern, StringComparison.OrdinalIgnoreCase));
                })
                .Select(d => d.Metadata.Name ?? "")
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
        using var activity = _telemetryProvider.StartActivity("bannou.orchestrator", "KubernetesOrchestrator.PruneNetworksAsync");
        // Kubernetes network policies and services are managed differently than Docker
        _logger.LogDebug("Network pruning not applicable to Kubernetes - networks are managed by CNI");
        await Task.CompletedTask;
        return new PruneResult
        {
            Success = true,
            Message = "Network pruning not applicable to Kubernetes (CNI-managed)"
        };
    }

    /// <inheritdoc />
    public async Task<PruneResult> PruneVolumesAsync(CancellationToken cancellationToken = default)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.orchestrator", "KubernetesOrchestrator.PruneVolumesAsync");
        // Kubernetes PersistentVolumeClaims require explicit deletion
        _logger.LogWarning("Volume pruning in Kubernetes requires explicit PVC management - not supported via orchestrator");
        await Task.CompletedTask;
        return new PruneResult
        {
            Success = false,
            Message = "Volume pruning not supported in Kubernetes mode - manage PVCs directly"
        };
    }

    /// <inheritdoc />
    public async Task<PruneResult> PruneImagesAsync(CancellationToken cancellationToken = default)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.orchestrator", "KubernetesOrchestrator.PruneImagesAsync");
        // Image pruning in Kubernetes happens at the node level via kubelet garbage collection
        _logger.LogDebug("Image pruning in Kubernetes is handled by kubelet garbage collection");
        await Task.CompletedTask;
        return new PruneResult
        {
            Success = true,
            Message = "Image pruning handled automatically by kubelet garbage collection"
        };
    }

    public void Dispose()
    {
        _client.Dispose();
    }
}
