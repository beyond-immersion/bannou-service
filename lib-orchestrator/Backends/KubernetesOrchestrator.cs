using k8s;
using k8s.Models;
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

    /// <summary>
    /// Label used to identify Dapr app-id on pods/deployments.
    /// </summary>
    private const string DAPR_APP_ID_LABEL = "dapr.io/app-id";

    /// <summary>
    /// Annotation used by kubectl to track restarts.
    /// </summary>
    private const string RESTART_ANNOTATION = "kubectl.kubernetes.io/restartedAt";

    public KubernetesOrchestrator(
        ILogger<KubernetesOrchestrator> logger,
        OrchestratorServiceConfiguration configuration)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));

        // Initialize Kubernetes client
        // See ORCHESTRATOR-SDK-REFERENCE.md for configuration patterns
        KubernetesClientConfiguration config;

        try
        {
            // Try in-cluster config first (running inside Kubernetes)
            config = KubernetesClientConfiguration.InClusterConfig();
            _logger.LogDebug("Using in-cluster Kubernetes configuration");
        }
        catch
        {
            // Fall back to kubeconfig file
            var kubeconfigPath = Environment.GetEnvironmentVariable("KUBECONFIG")
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".kube", "config");

            config = KubernetesClientConfiguration.BuildConfigFromConfigFile(kubeconfigPath);
            _logger.LogDebug("Using kubeconfig from {Path}", kubeconfigPath);
        }

        _client = new Kubernetes(config);

        // Get namespace from config or default
        _namespace = configuration.KubernetesNamespace ?? "default";
    }

    /// <inheritdoc />
    public BackendType BackendType => BackendType.Kubernetes;

    /// <inheritdoc />
    public async Task<(bool Available, string Message)> CheckAvailabilityAsync(CancellationToken cancellationToken = default)
    {
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
                    Status = ContainerStatusStatus.Stopped,
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
                    Accepted = false,
                    AppName = appName,
                    Message = $"No deployment found with Dapr app-id '{appName}' in namespace '{_namespace}'"
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
                AppName = appName,
                ScheduledFor = DateTimeOffset.UtcNow,
                CurrentInstances = desiredReplicas,
                RestartStrategy = ContainerRestartResponseRestartStrategy.Rolling,
                Message = $"Rolling restart initiated for deployment {deployment.Metadata.Name}"
            };
        }
        catch (k8s.Autorest.HttpOperationException ex)
        {
            _logger.LogError(ex, "Kubernetes API error restarting deployment for {AppName}", appName);
            return new ContainerRestartResponse
            {
                Accepted = false,
                AppName = appName,
                Message = $"Kubernetes API error: {ex.Message}"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error restarting Kubernetes deployment for {AppName}", appName);
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
        _logger.LogDebug("Listing all Kubernetes deployments in namespace {Namespace}", _namespace);

        try
        {
            // ListNamespacedDeploymentAsync - see ORCHESTRATOR-SDK-REFERENCE.md
            var deployments = await _client.AppsV1.ListNamespacedDeploymentAsync(
                _namespace,
                labelSelector: DAPR_APP_ID_LABEL,
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
        _logger.LogDebug("Getting Kubernetes pod logs for app: {AppName}, tail: {Tail}", appName, tail);

        try
        {
            var pod = await FindPodByAppNameAsync(appName, cancellationToken);
            if (pod == null)
            {
                return $"No pod found with Dapr app-id '{appName}' in namespace '{_namespace}'";
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
    /// Finds a deployment by its Dapr app-id label.
    /// </summary>
    private async Task<V1Deployment?> FindDeploymentByAppNameAsync(
        string appName,
        CancellationToken cancellationToken)
    {
        var deployments = await _client.AppsV1.ListNamespacedDeploymentAsync(
            _namespace,
            labelSelector: $"{DAPR_APP_ID_LABEL}={appName}",
            cancellationToken: cancellationToken);

        return deployments.Items.FirstOrDefault();
    }

    /// <summary>
    /// Finds a pod by its Dapr app-id label.
    /// </summary>
    private async Task<V1Pod?> FindPodByAppNameAsync(
        string appName,
        CancellationToken cancellationToken)
    {
        // ListNamespacedPodAsync - see ORCHESTRATOR-SDK-REFERENCE.md
        var pods = await _client.CoreV1.ListNamespacedPodAsync(
            _namespace,
            labelSelector: $"{DAPR_APP_ID_LABEL}={appName}",
            cancellationToken: cancellationToken);

        // Return the first running pod, or any pod if none are running
        return pods.Items
            .OrderByDescending(p => p.Status.Phase == "Running")
            .FirstOrDefault();
    }

    /// <summary>
    /// Extracts Dapr app-id from deployment labels.
    /// </summary>
    private static string GetAppNameFromDeployment(V1Deployment deployment)
    {
        if (deployment.Metadata.Labels?.TryGetValue(DAPR_APP_ID_LABEL, out var appId) == true)
        {
            return appId;
        }

        // Try pod template labels
        if (deployment.Spec.Template.Metadata.Labels?.TryGetValue(DAPR_APP_ID_LABEL, out appId) == true)
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
            "running" => ContainerStatusStatus.Running,
            "pending" => ContainerStatusStatus.Starting,
            "succeeded" => ContainerStatusStatus.Stopped,
            "failed" => ContainerStatusStatus.Unhealthy,
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
        _logger.LogInformation(
            "Deploying Kubernetes deployment: {ServiceName} with app-id: {AppId}",
            serviceName, appId);

        try
        {
            var imageName = "bannou:latest";

            // Prepare environment variables
            var envVars = new List<V1EnvVar>
            {
                new() { Name = $"{serviceName.ToUpperInvariant()}_SERVICE_ENABLED", Value = "true" },
                new() { Name = "DAPR_APP_ID", Value = appId }
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
                        [DAPR_APP_ID_LABEL] = appId,
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
                            [DAPR_APP_ID_LABEL] = appId
                        }
                    },
                    Template = new V1PodTemplateSpec
                    {
                        Metadata = new V1ObjectMeta
                        {
                            Labels = new Dictionary<string, string>
                            {
                                [DAPR_APP_ID_LABEL] = appId,
                                ["bannou.service"] = serviceName
                            },
                            Annotations = new Dictionary<string, string>
                            {
                                ["dapr.io/enabled"] = "true",
                                ["dapr.io/app-id"] = appId,
                                ["dapr.io/app-port"] = "80"
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
                                        new() { ContainerPort = 80, Name = "http" }
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
                    Message = $"No deployment found with Dapr app-id '{appName}' in namespace '{_namespace}'"
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
                    Message = $"No deployment found with Dapr app-id '{appName}' in namespace '{_namespace}'"
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

    public void Dispose()
    {
        _client.Dispose();
    }
}
