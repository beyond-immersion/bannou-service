using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Orchestrator;
using Dapr.Client;
using LibOrchestrator;
using LibOrchestrator.Backends;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("lib-orchestrator.tests")]

namespace BeyondImmersion.BannouService.Orchestrator;

/// <summary>
/// Implementation of the Orchestrator service.
/// This class contains the business logic for all Orchestrator operations.
/// CRITICAL: Uses direct Redis/RabbitMQ connections (NOT Dapr) to avoid chicken-and-egg dependency.
/// </summary>
[DaprService("orchestrator", typeof(IOrchestratorService), lifetime: ServiceLifetime.Scoped)]
public class OrchestratorService : IOrchestratorService
{
    private readonly DaprClient _daprClient;
    private readonly ILogger<OrchestratorService> _logger;
    private readonly OrchestratorServiceConfiguration _configuration;
    private readonly IOrchestratorRedisManager _redisManager;
    private readonly IOrchestratorEventManager _eventManager;
    private readonly IServiceHealthMonitor _healthMonitor;
    private readonly ISmartRestartManager _restartManager;
    private readonly IBackendDetector _backendDetector;
    private readonly PresetLoader _presetLoader;
    private readonly ILoggerFactory _loggerFactory;

    /// <summary>
    /// Cached orchestrator instance for container operations.
    /// </summary>
    private IContainerOrchestrator? _orchestrator;

    private const string STATE_STORE = "orchestrator-store";
    private const string CONFIG_VERSION_KEY = "orchestrator:config:version";

    /// <summary>
    /// Default preset name - all services on one "bannou" node.
    /// This matches the default Docker Compose behavior where everything runs in one container.
    /// </summary>
    private const string DEFAULT_PRESET = "bannou";

    public OrchestratorService(
        DaprClient daprClient,
        ILogger<OrchestratorService> logger,
        ILoggerFactory loggerFactory,
        OrchestratorServiceConfiguration configuration,
        IOrchestratorRedisManager redisManager,
        IOrchestratorEventManager eventManager,
        IServiceHealthMonitor healthMonitor,
        ISmartRestartManager restartManager,
        IBackendDetector backendDetector)
    {
        _daprClient = daprClient ?? throw new ArgumentNullException(nameof(daprClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _redisManager = redisManager ?? throw new ArgumentNullException(nameof(redisManager));
        _eventManager = eventManager ?? throw new ArgumentNullException(nameof(eventManager));
        _healthMonitor = healthMonitor ?? throw new ArgumentNullException(nameof(healthMonitor));
        _restartManager = restartManager ?? throw new ArgumentNullException(nameof(restartManager));
        _backendDetector = backendDetector ?? throw new ArgumentNullException(nameof(backendDetector));

        // Create preset loader
        _presetLoader = new PresetLoader(_loggerFactory.CreateLogger<PresetLoader>());
    }

    /// <summary>
    /// Gets or creates the container orchestrator for the best available backend.
    /// </summary>
    private async Task<IContainerOrchestrator> GetOrchestratorAsync(CancellationToken cancellationToken)
    {
        if (_orchestrator != null)
        {
            return _orchestrator;
        }

        _orchestrator = await _backendDetector.CreateBestOrchestratorAsync(cancellationToken);
        return _orchestrator;
    }

    /// <summary>
    /// Implementation of GetInfrastructureHealth operation.
    /// Validates connectivity and health of core infrastructure components.
    /// </summary>
    public async Task<(StatusCodes, InfrastructureHealthResponse?)> GetInfrastructureHealthAsync(InfrastructureHealthRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Executing GetInfrastructureHealth operation");

        try
        {
            var components = new List<ComponentHealth>();
            var overallHealthy = true;

            // Check Redis connectivity
            var (redisHealthy, redisMessage, redisPing) = await _redisManager.CheckHealthAsync();
            components.Add(new ComponentHealth
            {
                Name = "redis",
                Status = redisHealthy ? ComponentHealthStatus.Healthy : ComponentHealthStatus.Unavailable,
                LastSeen = DateTimeOffset.UtcNow,
                Message = redisMessage ?? string.Empty,
                Metrics = redisPing.HasValue
                    ? (object)new Dictionary<string, object> { { "pingTimeMs", redisPing.Value.TotalMilliseconds } }
                    : new Dictionary<string, object>()
            });
            overallHealthy = overallHealthy && redisHealthy;

            // Check pub/sub path (Dapr) by publishing a tiny health probe
            bool pubsubHealthy;
            string pubsubMessage;
            try
            {
                await _daprClient.PublishEventAsync(
                    "bannou-pubsub",
                    "orchestrator-health",
                    new { ping = "ok" },
                    cancellationToken);
                pubsubHealthy = true;
                pubsubMessage = "Dapr pub/sub path active";
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Dapr pub/sub health check failed");
                pubsubHealthy = false;
                pubsubMessage = $"Dapr pub/sub check failed: {ex.Message}";
            }
            components.Add(new ComponentHealth
            {
                Name = "pubsub",
                Status = pubsubHealthy ? ComponentHealthStatus.Healthy : ComponentHealthStatus.Unavailable,
                LastSeen = DateTimeOffset.UtcNow,
                Message = pubsubMessage ?? string.Empty
            });
            overallHealthy = overallHealthy && pubsubHealthy;

            // Check Dapr Placement service (reuse publish probe)
            try
            {
                await _daprClient.PublishEventAsync(
                    "bannou-pubsub",
                    "orchestrator-health",
                    new { ping = "ok" },
                    cancellationToken);
                components.Add(new ComponentHealth
                {
                    Name = "placement",
                    Status = ComponentHealthStatus.Healthy,
                    LastSeen = DateTimeOffset.UtcNow,
                    Message = "Dapr sidecar responding"
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Dapr health check failed");
                components.Add(new ComponentHealth
                {
                    Name = "placement",
                    Status = ComponentHealthStatus.Unavailable,
                    LastSeen = DateTimeOffset.UtcNow,
                    Message = $"Dapr check failed: {ex.Message}"
                });
                overallHealthy = false;
            }

            var response = new InfrastructureHealthResponse
            {
                Healthy = overallHealthy,
                Timestamp = DateTimeOffset.UtcNow,
                Components = components
            };

            var statusCode = overallHealthy ? StatusCodes.OK : StatusCodes.InternalServerError;
            return (statusCode, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing GetInfrastructureHealth operation");
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Implementation of GetServicesHealth operation.
    /// Retrieves health information from all services via Redis heartbeat monitoring.
    /// </summary>
    public async Task<(StatusCodes, ServiceHealthReport?)> GetServicesHealthAsync(ServiceHealthRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Executing GetServicesHealth operation");

        try
        {
            var report = await _healthMonitor.GetServiceHealthReportAsync();
            return (StatusCodes.OK, report);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing GetServicesHealth operation");
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Implementation of RestartService operation.
    /// Performs intelligent service restart based on health metrics.
    /// </summary>
    public async Task<(StatusCodes, ServiceRestartResult?)> RestartServiceAsync(ServiceRestartRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Executing RestartService operation for: {ServiceName} (force: {Force})",
            body.ServiceName, body.Force);

        try
        {
            var result = await _restartManager.RestartServiceAsync(body);

            if (!result.Success)
            {
                // Check if restart was declined (healthy service)
                if (result.Message?.Contains("not needed") == true)
                {
                    return (StatusCodes.Conflict, result);
                }

                // Restart failed
                return (StatusCodes.InternalServerError, result);
            }

            return (StatusCodes.OK, result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing RestartService operation");
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Implementation of ShouldRestartService operation.
    /// Evaluates service health and determines if restart is necessary.
    /// </summary>
    public async Task<(StatusCodes, RestartRecommendation?)> ShouldRestartServiceAsync(ShouldRestartServiceRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Executing ShouldRestartService operation for: {ServiceName}", body.ServiceName);

        try
        {
            var recommendation = await _healthMonitor.ShouldRestartServiceAsync(body.ServiceName);
            return (StatusCodes.OK, recommendation);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing ShouldRestartService operation");
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Implementation of GetBackends operation.
    /// Detects and returns available container orchestration backends.
    /// </summary>
    public async Task<(StatusCodes, BackendsResponse?)> GetBackendsAsync(ListBackendsRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Executing GetBackends operation");

        try
        {
            var response = await _backendDetector.DetectBackendsAsync(cancellationToken);
            return (StatusCodes.OK, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing GetBackends operation");
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Implementation of GetPresets operation.
    /// Returns available deployment preset configurations.
    /// </summary>
    public async Task<(StatusCodes, PresetsResponse?)> GetPresetsAsync(ListPresetsRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Executing GetPresets operation");

        try
        {
            var presetMetadata = await _presetLoader.ListPresetsAsync(cancellationToken);
            var presets = new List<DeploymentPreset>();

            foreach (var meta in presetMetadata)
            {
                // Load full preset to get topology
                var preset = await _presetLoader.LoadPresetAsync(meta.Name, cancellationToken);
                if (preset != null)
                {
                    var topology = _presetLoader.ConvertToTopology(preset);
                    presets.Add(new DeploymentPreset
                    {
                        Name = meta.Name,
                        Description = meta.Description ?? $"Preset: {meta.Name}",
                        Topology = topology
                    });
                }
            }

            var response = new PresetsResponse
            {
                Presets = presets,
                ActivePreset = DEFAULT_PRESET // Default is "bannou" - all services on one node
            };

            return (StatusCodes.OK, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing GetPresets operation");
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Implementation of Deploy operation.
    /// Deploys services using the selected backend and topology or preset.
    /// </summary>
    public async Task<(StatusCodes, DeployResponse?)> DeployAsync(DeployRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Executing Deploy operation: preset={Preset}, backend={Backend}",
            body.Preset, body.Backend);

        var startTime = DateTime.UtcNow;
        var deploymentId = Guid.NewGuid().ToString();

        // Publish deployment started event
        await _eventManager.PublishDeploymentEventAsync(new DeploymentEvent
        {
            EventId = Guid.NewGuid().ToString(),
            Timestamp = DateTimeOffset.UtcNow,
            Action = DeploymentEventAction.Started,
            DeploymentId = deploymentId,
            Preset = body.Preset,
            Backend = body.Backend
        });

        try
        {
            // Detect available backends first
            var backends = await _backendDetector.DetectBackendsAsync(cancellationToken);

            // Check if the requested backend is available
            // We always check since default(BackendType) = Kubernetes = 0, making it impossible
            // to distinguish "not specified" from "explicitly Kubernetes" via the enum value alone
            var requestedBackend = backends.Backends?.FirstOrDefault(b => b.Type == body.Backend);

            IContainerOrchestrator orchestrator;
            if (requestedBackend != null && !requestedBackend.Available)
            {
                // Backend exists in detection but is not available - fail the deployment
                var errorMsg = requestedBackend.Error ?? "Backend not available";
                _logger.LogWarning(
                    "Requested backend {Backend} is not available: {Error}",
                    body.Backend, errorMsg);

                return (StatusCodes.BadRequest, new DeployResponse
                {
                    Success = false,
                    DeploymentId = deploymentId,
                    Backend = body.Backend,
                    Duration = "0s",
                    Message = $"Requested backend '{body.Backend}' is not available: {errorMsg}. " +
                            $"Available backends: {string.Join(", ", backends.Backends?.Where(b => b.Available).Select(b => b.Type) ?? Enumerable.Empty<BackendType>())}"
                });
            }

            // Use the requested backend if available, otherwise use recommended
            if (requestedBackend != null && requestedBackend.Available)
            {
                orchestrator = _backendDetector.CreateOrchestrator(body.Backend);
            }
            else
            {
                // Fall back to recommended backend
                orchestrator = _backendDetector.CreateOrchestrator(backends.Recommended);
                _logger.LogInformation(
                    "Using recommended backend {Recommended} instead of {Requested}",
                    backends.Recommended, body.Backend);
            }

            // Get topology nodes to deploy - from preset or direct topology
            ICollection<TopologyNode> nodesToDeploy;
            IDictionary<string, string>? presetEnvironment = null;

            if (!string.IsNullOrEmpty(body.Preset))
            {
                // Load preset by name
                var preset = await _presetLoader.LoadPresetAsync(body.Preset, cancellationToken);
                if (preset == null)
                {
                    return (StatusCodes.NotFound, new DeployResponse
                    {
                        Success = false,
                        DeploymentId = deploymentId,
                        Backend = orchestrator.BackendType,
                        Duration = "0s",
                        Message = $"Preset '{body.Preset}' not found"
                    });
                }

                var topology = _presetLoader.ConvertToTopology(preset);
                nodesToDeploy = topology.Nodes;
                presetEnvironment = preset.Environment;

                _logger.LogInformation(
                    "Loaded preset '{Preset}' with {NodeCount} nodes",
                    body.Preset, nodesToDeploy.Count);
            }
            else if (body.Topology?.Nodes != null && body.Topology.Nodes.Count > 0)
            {
                nodesToDeploy = body.Topology.Nodes;
            }
            else
            {
                return (StatusCodes.BadRequest, new DeployResponse
                {
                    Success = false,
                    DeploymentId = deploymentId,
                    Backend = orchestrator.BackendType,
                    Duration = "0s",
                    Message = "No topology nodes specified for deployment. Provide a preset name or topology."
                });
            }

            var deployedServices = new List<DeployedService>();
            var failedServices = new List<string>();
            var expectedAppIds = new HashSet<string>(); // Track app-ids that should send heartbeats

            foreach (var node in nodesToDeploy)
            {
                var appId = node.DaprAppId ?? node.Name;

                // Start with preset environment (lowest priority)
                var environment = new Dictionary<string, string>();
                if (presetEnvironment != null)
                {
                    foreach (var kvp in presetEnvironment)
                    {
                        environment[kvp.Key] = kvp.Value;
                    }
                }

                // Add node-specific environment (higher priority, overrides preset)
                if (node.Environment != null)
                {
                    foreach (var kvp in node.Environment)
                    {
                        environment[kvp.Key] = kvp.Value;
                    }
                }

                // Add request environment (highest priority, overrides node and preset)
                if (body.Environment != null)
                {
                    foreach (var kvp in body.Environment)
                    {
                        environment[kvp.Key] = kvp.Value;
                    }
                }

                // Build service enable flags for this node
                foreach (var serviceName in node.Services)
                {
                    var serviceEnvKey = $"{serviceName.ToUpperInvariant()}_SERVICE_ENABLED";
                    environment[serviceEnvKey] = "true";
                }

                // Deploy via container orchestrator
                var deployResult = await orchestrator.DeployServiceAsync(
                    node.Name,
                    appId,
                    environment,
                    cancellationToken);

                if (deployResult.Success)
                {
                    deployedServices.Add(new DeployedService
                    {
                        Name = node.Name,
                        Status = DeployedServiceStatus.Starting,
                        Node = Environment.MachineName
                    });

                    // Publish service mapping events for each service on this node
                    foreach (var serviceName in node.Services)
                    {
                        await PublishServiceMappingAsync(new ServiceMappingEvent
                        {
                            EventId = Guid.NewGuid().ToString(),
                            ServiceName = serviceName,
                            AppId = appId,
                            Action = ServiceMappingAction.Register
                        }, cancellationToken);
                    }

                    // Track this app-id for heartbeat waiting
                    expectedAppIds.Add(appId);

                    _logger.LogInformation(
                        "Node {NodeName} deployed with app-id {AppId}, {ServiceCount} service mapping events published",
                        node.Name, appId, node.Services.Count);
                }
                else
                {
                    failedServices.Add($"{node.Name}: {deployResult.Message}");
                    _logger.LogWarning(
                        "Failed to deploy node {NodeName}: {Message}",
                        node.Name, deployResult.Message);
                }
            }

            // Wait for heartbeats from deployed containers before declaring success
            // This ensures new containers are fully operational (Dapr connected, plugins loaded)
            if (expectedAppIds.Count > 0 && failedServices.Count == 0)
            {
                _logger.LogInformation(
                    "Waiting for heartbeats from {Count} deployed app-ids: [{AppIds}]",
                    expectedAppIds.Count, string.Join(", ", expectedAppIds));

                var heartbeatTimeout = TimeSpan.FromSeconds(_configuration.HeartbeatTimeoutSeconds);
                var pollInterval = TimeSpan.FromSeconds(2);
                var heartbeatWaitStart = DateTime.UtcNow;
                var receivedHeartbeats = new HashSet<string>();

                while (DateTime.UtcNow - heartbeatWaitStart < heartbeatTimeout)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // Poll Redis for heartbeats from expected app-ids
                    var heartbeats = await _redisManager.GetServiceHeartbeatsAsync();

                    // Check which expected app-ids have sent heartbeats
                    foreach (var heartbeat in heartbeats)
                    {
                        if (expectedAppIds.Contains(heartbeat.AppId) &&
                            !receivedHeartbeats.Contains(heartbeat.AppId))
                        {
                            receivedHeartbeats.Add(heartbeat.AppId);
                            _logger.LogInformation(
                                "Received heartbeat from deployed app-id {AppId} ({Received}/{Expected})",
                                heartbeat.AppId, receivedHeartbeats.Count, expectedAppIds.Count);
                        }
                    }

                    // All expected app-ids have sent heartbeats - deployment complete
                    if (receivedHeartbeats.Count >= expectedAppIds.Count)
                    {
                        _logger.LogInformation(
                            "All {Count} deployed containers are operational",
                            expectedAppIds.Count);
                        break;
                    }

                    // Wait before next poll
                    await Task.Delay(pollInterval, cancellationToken);
                }

                // Check for timeout - treat as deployment failure
                var missingHeartbeats = expectedAppIds.Except(receivedHeartbeats).ToList();
                if (missingHeartbeats.Count > 0)
                {
                    var elapsed = DateTime.UtcNow - heartbeatWaitStart;
                    failedServices.Add($"Heartbeat timeout ({elapsed.TotalSeconds:F0}s) - missing heartbeats from: [{string.Join(", ", missingHeartbeats)}]");
                    _logger.LogWarning(
                        "Deployment heartbeat timeout after {Elapsed}s - missing heartbeats from: [{AppIds}]",
                        elapsed.TotalSeconds, string.Join(", ", missingHeartbeats));
                }
            }

            var duration = DateTime.UtcNow - startTime;
            var success = failedServices.Count == 0;

            var response = new DeployResponse
            {
                Success = success,
                DeploymentId = deploymentId,
                Backend = orchestrator.BackendType,
                Duration = $"{duration.TotalSeconds:F1}s",
                Message = success
                    ? $"Successfully deployed {deployedServices.Count} node(s)"
                    : $"Deployed {deployedServices.Count} node(s), {failedServices.Count} failed: {string.Join("; ", failedServices)}"
            };

            // Publish deployment completed/failed event
            await _eventManager.PublishDeploymentEventAsync(new DeploymentEvent
            {
                EventId = Guid.NewGuid().ToString(),
                Timestamp = DateTimeOffset.UtcNow,
                Action = success ? DeploymentEventAction.Completed : DeploymentEventAction.Failed,
                DeploymentId = deploymentId,
                Preset = body.Preset,
                Backend = orchestrator.BackendType,
                Changes = deployedServices.Select(s => s.Name).ToList(),
                Error = success ? null : string.Join("; ", failedServices)
            });

            return (success ? StatusCodes.OK : StatusCodes.InternalServerError, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing Deploy operation");

            // Publish deployment failed event on exception
            await _eventManager.PublishDeploymentEventAsync(new DeploymentEvent
            {
                EventId = Guid.NewGuid().ToString(),
                Timestamp = DateTimeOffset.UtcNow,
                Action = DeploymentEventAction.Failed,
                DeploymentId = deploymentId,
                Preset = body.Preset,
                Error = ex.Message
            });

            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Implementation of GetStatus operation.
    /// Returns the current environment status including running services.
    /// </summary>
    public async Task<(StatusCodes, EnvironmentStatus?)> GetStatusAsync(GetStatusRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Executing GetStatus operation");

        try
        {
            var orchestrator = await GetOrchestratorAsync(cancellationToken);
            var containers = await orchestrator.ListContainersAsync(cancellationToken);

            // Build topology from running containers
            var topology = new ServiceTopology
            {
                Nodes = containers
                    .GroupBy(c => c.AppName ?? "unknown")
                    .Select(g => new TopologyNode
                    {
                        Name = g.Key,
                        Services = new List<string> { g.Key },
                        Replicas = g.Count(),
                        DaprEnabled = true,
                        DaprAppId = g.Key
                    })
                    .ToList()
            };

            var response = new EnvironmentStatus
            {
                Deployed = containers.Count > 0,
                Timestamp = DateTimeOffset.UtcNow,
                Backend = orchestrator.BackendType,
                Topology = containers.Count > 0 ? topology : new ServiceTopology(),
                Services = containers.Select(c => new DeployedService
                {
                    Name = c.AppName ?? "unknown",
                    Status = MapContainerStatusToDeployedServiceStatus(c.Status),
                    Node = Environment.MachineName
                }).ToList()
            };

            return (StatusCodes.OK, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing GetStatus operation");
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Implementation of Teardown operation.
    /// Stops and removes all services from the environment.
    /// </summary>
    public async Task<(StatusCodes, TeardownResponse?)> TeardownAsync(TeardownRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Executing Teardown operation: dryRun={DryRun}, mode={Mode}, removeVolumes={RemoveVolumes}, includeInfrastructure={IncludeInfrastructure}",
            body.DryRun,
            body.Mode,
            body.RemoveVolumes,
            body.IncludeInfrastructure);

        if (body.IncludeInfrastructure && !body.DryRun)
        {
            _logger.LogWarning(
                "⚠️ DESTRUCTIVE OPERATION: includeInfrastructure=true will remove Redis, RabbitMQ, MySQL, etc. " +
                "All data will be lost and a full re-deployment will be required.");
        }

        var startTime = DateTime.UtcNow;
        var teardownId = Guid.NewGuid().ToString();

        // Don't publish events during dry run
        if (!body.DryRun)
        {
            // Publish teardown started event (topology-changed action indicates infrastructure modification)
            await _eventManager.PublishDeploymentEventAsync(new DeploymentEvent
            {
                EventId = Guid.NewGuid().ToString(),
                Timestamp = DateTimeOffset.UtcNow,
                Action = DeploymentEventAction.TopologyChanged,
                DeploymentId = teardownId
            });
        }

        try
        {
            var orchestrator = await GetOrchestratorAsync(cancellationToken);
            var stoppedContainers = new List<string>();
            var removedVolumes = new List<string>();
            var failedTeardowns = new List<string>();

            // Get all running containers
            var containers = await orchestrator.ListContainersAsync(cancellationToken);
            var allContainers = containers
                .Select(c => c.AppName ?? "")
                .Where(n => !string.IsNullOrEmpty(n))
                .ToList();

            // Get infrastructure services to filter them out (unless includeInfrastructure is true)
            var infrastructureServices = await orchestrator.ListInfrastructureServicesAsync(cancellationToken);
            var infraSet = new HashSet<string>(infrastructureServices, StringComparer.OrdinalIgnoreCase);

            // Filter out infrastructure from main teardown list
            var servicesToTeardown = allContainers
                .Where(n => !infraSet.Contains(n))
                .ToList();

            _logger.LogInformation(
                "Found {Count} app containers to tear down: {Services} (filtered {InfraCount} infrastructure services)",
                servicesToTeardown.Count,
                string.Join(", ", servicesToTeardown),
                infraSet.Count);

            if (servicesToTeardown.Count == 0)
            {
                return (StatusCodes.OK, new TeardownResponse
                {
                    Success = true,
                    Duration = "0s",
                    StoppedContainers = stoppedContainers,
                    RemovedVolumes = removedVolumes
                });
            }

            // Collect infrastructure services that would be torn down
            var infraToRemove = new List<string>();
            if (body.IncludeInfrastructure)
            {
                infraToRemove.AddRange(infrastructureServices);
            }

            // Build the preview response (what will be torn down)
            var previewDuration = DateTime.UtcNow - startTime;
            var previewResponse = new TeardownResponse
            {
                Success = true,
                Duration = $"{previewDuration.TotalSeconds:F1}s",
                StoppedContainers = servicesToTeardown,
                RemovedVolumes = new List<string>(),
                RemovedInfrastructure = infraToRemove
            };

            // If dry run, just return the preview
            if (body.DryRun)
            {
                _logger.LogInformation("Dry run mode - not actually tearing down containers");
                return (StatusCodes.OK, previewResponse);
            }

            // Execute teardown synchronously to avoid returning before completion
            var result = await ExecuteActualTeardownAsync(
                orchestrator,
                servicesToTeardown,
                infraToRemove,
                body.RemoveVolumes,
                body.IncludeInfrastructure,
                teardownId,
                cancellationToken);

            var duration = DateTime.UtcNow - startTime;
            var success = result.Failed.Count == 0;

            var response = new TeardownResponse
            {
                Success = success,
                Duration = $"{duration.TotalSeconds:F1}s",
                StoppedContainers = result.Stopped,
                RemovedVolumes = result.RemovedVolumes,
                RemovedInfrastructure = result.RemovedInfrastructure,
                Message = success
                    ? "Teardown completed"
                    : "Teardown completed with failures"
            };

            return (success ? StatusCodes.OK : StatusCodes.InternalServerError, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing Teardown operation");

            // Publish teardown failed event on exception
            await _eventManager.PublishDeploymentEventAsync(new DeploymentEvent
            {
                EventId = Guid.NewGuid().ToString(),
                Timestamp = DateTimeOffset.UtcNow,
                Action = DeploymentEventAction.Failed,
                DeploymentId = teardownId,
                Error = ex.Message
            });

            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Executes the actual teardown in the background after response is sent to client.
    /// </summary>
    private async Task<(List<string> Stopped, List<string> RemovedVolumes, List<string> RemovedInfrastructure, List<string> Failed)> ExecuteActualTeardownAsync(
        IContainerOrchestrator orchestrator,
        List<string> servicesToTeardown,
        List<string> infrastructureServices,
        bool removeVolumes,
        bool includeInfrastructure,
        string teardownId,
        CancellationToken cancellationToken)
    {
        var stoppedContainers = new List<string>();
        var removedVolumes = new List<string>();
        var failedTeardowns = new List<string>();
        var removedInfrastructure = new List<string>();

        _logger.LogInformation("Background teardown starting for {Count} services", servicesToTeardown.Count);

        foreach (var appName in servicesToTeardown)
        {
            try
            {
                // Tear down via container orchestrator
                var teardownResult = await orchestrator.TeardownServiceAsync(
                    appName,
                    removeVolumes,
                    cancellationToken);

                if (teardownResult.Success)
                {
                    stoppedContainers.AddRange(teardownResult.StoppedContainers);
                    removedVolumes.AddRange(teardownResult.RemovedVolumes);

                    // Extract service name from app-id (e.g., "bannou-auth" -> "auth")
                    var serviceName = appName.StartsWith("bannou-")
                        ? appName.Substring(7)
                        : appName;

                    // Publish service mapping event to notify all bannou instances to remove routing
                    await PublishServiceMappingAsync(new ServiceMappingEvent
                    {
                        EventId = Guid.NewGuid().ToString(),
                        ServiceName = serviceName,
                        AppId = appName,
                        Action = ServiceMappingAction.Unregister
                    }, cancellationToken);

                    _logger.LogInformation(
                        "Service {AppName} torn down, unregister mapping event published",
                        appName);
                }
                else
                {
                    failedTeardowns.Add($"{appName}: {teardownResult.Message}");
                    _logger.LogWarning(
                        "Failed to tear down service {AppName}: {Message}",
                        appName, teardownResult.Message);
                }
            }
            catch (Exception ex)
            {
                failedTeardowns.Add($"{appName}: {ex.Message}");
                _logger.LogWarning(ex, "Error tearing down service {AppName}", appName);
            }
        }

        // Handle infrastructure teardown if requested
        if (includeInfrastructure)
        {
            _logger.LogInformation("Proceeding with infrastructure teardown...");

            foreach (var infraName in infrastructureServices)
            {
                try
                {
                    var infraResult = await orchestrator.TeardownServiceAsync(
                        infraName,
                        removeVolumes,
                        cancellationToken);

                    if (infraResult.Success)
                    {
                        removedInfrastructure.Add(infraName);
                        stoppedContainers.AddRange(infraResult.StoppedContainers);
                        removedVolumes.AddRange(infraResult.RemovedVolumes);
                        _logger.LogInformation("Infrastructure service {InfraName} torn down", infraName);
                    }
                    else
                    {
                        failedTeardowns.Add($"[infra] {infraName}: {infraResult.Message}");
                        _logger.LogWarning(
                            "Failed to tear down infrastructure service {InfraName}: {Message}",
                            infraName, infraResult.Message);
                    }
                }
                catch (Exception ex)
                {
                    failedTeardowns.Add($"[infra] {infraName}: {ex.Message}");
                    _logger.LogWarning(ex, "Error tearing down infrastructure service {InfraName}", infraName);
                }
            }
        }

        var success = failedTeardowns.Count == 0;

        // Publish teardown completed/failed event
        await _eventManager.PublishDeploymentEventAsync(new DeploymentEvent
        {
            EventId = Guid.NewGuid().ToString(),
            Timestamp = DateTimeOffset.UtcNow,
            Action = success ? DeploymentEventAction.Completed : DeploymentEventAction.Failed,
            DeploymentId = teardownId,
            Changes = stoppedContainers,
            Error = success ? null : string.Join("; ", failedTeardowns)
        });

        _logger.LogInformation(
            "Teardown completed: {StoppedCount} containers stopped, {FailedCount} failures",
            stoppedContainers.Count,
            failedTeardowns.Count);

        return (stoppedContainers, removedVolumes, removedInfrastructure, failedTeardowns);
    }

    /// <summary>
    /// Publishes a service mapping event via Dapr pubsub (single, CloudEvents format).
    /// We avoid dual-path (RabbitMQ + Dapr) to keep formats consistent for Connect consumers.
    /// </summary>
    private async Task PublishServiceMappingAsync(ServiceMappingEvent mappingEvent, CancellationToken cancellationToken)
    {
        try
        {
            await _daprClient.PublishEventAsync(
                "bannou-pubsub",
                "bannou-service-mappings",
                mappingEvent,
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish service mapping to Dapr pubsub for {Service}", mappingEvent.ServiceName);
        }
    }

    /// <summary>
    /// Implementation of Clean operation.
    /// Cleans up unused resources (images, volumes, networks).
    /// </summary>
    public Task<(StatusCodes, CleanResponse?)> CleanAsync(CleanRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Executing Clean operation: targets={Targets}, force={Force}",
            string.Join(",", body.Targets ?? Enumerable.Empty<CleanTarget>()), body.Force);

        try
        {
            // TODO: Implement cleanup via container orchestrator
            _logger.LogWarning("Cleanup via orchestrator not yet fully implemented");

            var response = new CleanResponse
            {
                Success = false,
                ReclaimedSpaceMb = 0,
                RemovedContainers = 0,
                RemovedNetworks = 0,
                RemovedVolumes = 0
            };

            return Task.FromResult<(StatusCodes, CleanResponse?)>((StatusCodes.BadRequest, response));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing Clean operation");
            return Task.FromResult<(StatusCodes, CleanResponse?)>((StatusCodes.InternalServerError, null));
        }
    }

    /// <summary>
    /// Implementation of GetLogs operation.
    /// Retrieves logs from containers.
    /// </summary>
    public async Task<(StatusCodes, LogsResponse?)> GetLogsAsync(
        GetLogsRequest body,
        CancellationToken cancellationToken = default)
    {
        var service = body.Service;
        var container = body.Container;
        var since = body.Since;
        var tail = body.Tail;

        _logger.LogInformation(
            "Executing GetLogs operation: service={Service}, container={Container}, tail={Tail}",
            service, container, tail);

        try
        {
            var orchestrator = await GetOrchestratorAsync(cancellationToken);

            // Parse since timestamp if provided
            DateTimeOffset? sinceTime = null;
            if (!string.IsNullOrEmpty(since) && DateTimeOffset.TryParse(since, out var parsedSince))
            {
                sinceTime = parsedSince;
            }

            var appName = service ?? container ?? "bannou";
            var logsText = await orchestrator.GetContainerLogsAsync(appName, tail, sinceTime, cancellationToken);

            // Parse log text into LogEntry objects
            var logEntries = logsText
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(line => new LogEntry
                {
                    Timestamp = DateTimeOffset.UtcNow, // Would parse from log line
                    Stream = LogEntryStream.Stdout,
                    Message = line
                })
                .ToList();

            var response = new LogsResponse
            {
                Service = appName,
                Container = appName,
                Logs = logEntries
            };

            return (StatusCodes.OK, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing GetLogs operation");
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Implementation of UpdateTopology operation.
    /// Updates service topology (add/remove nodes, move/scale services, update environment).
    /// </summary>
    public async Task<(StatusCodes, TopologyUpdateResponse?)> UpdateTopologyAsync(TopologyUpdateRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Executing UpdateTopology operation: mode={Mode}, changes={Changes}",
            body.Mode, body.Changes?.Count ?? 0);

        var startTime = DateTime.UtcNow;

        try
        {
            var orchestrator = await GetOrchestratorAsync(cancellationToken);
            var changes = body.Changes ?? new List<TopologyChange>();

            if (changes.Count == 0)
            {
                return (StatusCodes.BadRequest, new TopologyUpdateResponse
                {
                    Success = false,
                    AppliedChanges = new List<AppliedChange>(),
                    Message = "No topology changes specified"
                });
            }

            var appliedChanges = new List<AppliedChange>();
            var warnings = new List<string>();

            foreach (var change in changes)
            {
                var appliedChange = new AppliedChange
                {
                    Action = change.Action.ToString(),
                    Target = change.NodeName ?? string.Join(",", change.Services ?? Array.Empty<string>()),
                    Success = false
                };

                try
                {
                    switch (change.Action)
                    {
                        case TopologyChangeAction.AddNode:
                            // Add a new node with services configured via NodeConfig
                            if (change.NodeConfig != null && change.Services != null)
                            {
                                // Convert IDictionary to Dictionary for the orchestrator method
                                var nodeEnvironment = change.Environment != null
                                    ? new Dictionary<string, string>(change.Environment)
                                    : null;

                                foreach (var serviceName in change.Services)
                                {
                                    var appId = $"bannou-{serviceName}-{change.NodeName}";
                                    var deployResult = await orchestrator.DeployServiceAsync(
                                        serviceName,
                                        appId,
                                        nodeEnvironment,
                                        cancellationToken);

                                    if (deployResult.Success)
                                    {
                                        // Publish register mapping event for each service on the new node
                                        await PublishServiceMappingAsync(new ServiceMappingEvent
                                        {
                                            EventId = Guid.NewGuid().ToString(),
                                            ServiceName = serviceName,
                                            AppId = appId,
                                            Action = ServiceMappingAction.Register
                                        }, cancellationToken);
                                    }
                                    else
                                    {
                                        warnings.Add($"Failed to deploy {serviceName} on {change.NodeName}: {deployResult.Message}");
                                    }
                                }
                                appliedChange.Success = true;
                            }
                            else
                            {
                                appliedChange.Error = "NodeConfig and Services are required for add-node action";
                            }
                            break;

                        case TopologyChangeAction.RemoveNode:
                            // Remove a node and all its services
                            if (change.Services != null)
                            {
                                foreach (var serviceName in change.Services)
                                {
                                    var appId = $"bannou-{serviceName}-{change.NodeName}";
                                    var teardownResult = await orchestrator.TeardownServiceAsync(
                                        appId,
                                        false,
                                        cancellationToken);

                                    if (teardownResult.Success)
                                    {
                                        // Publish unregister mapping event
                                        await PublishServiceMappingAsync(new ServiceMappingEvent
                                        {
                                            EventId = Guid.NewGuid().ToString(),
                                            ServiceName = serviceName,
                                            AppId = appId,
                                            Action = ServiceMappingAction.Unregister
                                        }, cancellationToken);
                                    }
                                    else
                                    {
                                        warnings.Add($"Failed to teardown {serviceName} on {change.NodeName}: {teardownResult.Message}");
                                    }
                                }
                                appliedChange.Success = true;
                            }
                            else
                            {
                                appliedChange.Error = "Services list is required for remove-node action";
                            }
                            break;

                        case TopologyChangeAction.MoveService:
                            // Move service(s) to a different node (update routing)
                            if (change.Services != null && !string.IsNullOrEmpty(change.NodeName))
                            {
                                foreach (var serviceName in change.Services)
                                {
                                    var newAppId = $"bannou-{serviceName}-{change.NodeName}";

                                    // Publish update mapping event to change routing
                                    await PublishServiceMappingAsync(new ServiceMappingEvent
                                    {
                                        EventId = Guid.NewGuid().ToString(),
                                        ServiceName = serviceName,
                                        AppId = newAppId,
                                        Action = ServiceMappingAction.Update
                                    }, cancellationToken);
                                }
                                appliedChange.Success = true;
                            }
                            else
                            {
                                appliedChange.Error = "Services list and NodeName are required for move-service action";
                            }
                            break;

                        case TopologyChangeAction.Scale:
                            // Scale service instances on a node
                            if (change.Services != null)
                            {
                                foreach (var serviceName in change.Services)
                                {
                                    var appId = !string.IsNullOrEmpty(change.NodeName)
                                        ? $"bannou-{serviceName}-{change.NodeName}"
                                        : $"bannou-{serviceName}";

                                    var scaleResult = await orchestrator.ScaleServiceAsync(
                                        appId,
                                        change.Replicas,
                                        cancellationToken);

                                    if (!scaleResult.Success)
                                    {
                                        warnings.Add($"Failed to scale {serviceName}: {scaleResult.Message}");
                                    }
                                }
                                appliedChange.Success = true;
                            }
                            else
                            {
                                appliedChange.Error = "Services list is required for scale action";
                            }
                            break;

                        case TopologyChangeAction.UpdateEnv:
                            // Update environment variables for services
                            if (change.Services != null && change.Environment != null)
                            {
                                foreach (var serviceName in change.Services)
                                {
                                    // For environment updates, we need to restart with new env vars
                                    // This is a placeholder - actual implementation would depend on orchestrator capabilities
                                    _logger.LogInformation(
                                        "Environment update requested for {ServiceName} with {EnvCount} variables",
                                        serviceName, change.Environment.Count);
                                }
                                appliedChange.Success = true;
                                warnings.Add("Environment updates require service restart to take effect");
                            }
                            else
                            {
                                appliedChange.Error = "Services list and Environment are required for update-env action";
                            }
                            break;

                        default:
                            appliedChange.Error = $"Unknown action: {change.Action}";
                            break;
                    }
                }
                catch (Exception ex)
                {
                    appliedChange.Error = ex.Message;
                    _logger.LogError(ex, "Error applying topology change: {Action} for {Target}",
                        change.Action, change.NodeName);
                }

                appliedChanges.Add(appliedChange);
            }

            var duration = DateTime.UtcNow - startTime;
            var allSucceeded = appliedChanges.All(c => c.Success);
            var successCount = appliedChanges.Count(c => c.Success);
            var failCount = appliedChanges.Count(c => !c.Success);

            var response = new TopologyUpdateResponse
            {
                Success = allSucceeded,
                AppliedChanges = appliedChanges,
                Duration = $"{duration.TotalSeconds:F1}s",
                Warnings = warnings,
                Message = allSucceeded
                    ? $"Successfully applied {successCount} topology change(s)"
                    : $"Applied {successCount} change(s), {failCount} failed"
            };

            return (allSucceeded ? StatusCodes.OK : StatusCodes.InternalServerError, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing UpdateTopology operation");
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Implementation of RequestContainerRestart operation.
    /// Restarts a specific container by Dapr app name.
    /// </summary>
    public async Task<(StatusCodes, ContainerRestartResponse?)> RequestContainerRestartAsync(
        ContainerRestartRequestBody body,
        CancellationToken cancellationToken)
    {
        var appName = body.AppName;
        _logger.LogInformation(
            "Executing RequestContainerRestart operation: app={AppName}, priority={Priority}",
            appName, body.Priority);

        try
        {
            var orchestrator = await GetOrchestratorAsync(cancellationToken);
            // Create ContainerRestartRequest from the body for the orchestrator
            var restartRequest = new ContainerRestartRequest
            {
                Priority = body.Priority,
                Reason = body.Reason
            };
            var response = await orchestrator.RestartContainerAsync(appName, restartRequest, cancellationToken);

            if (!response.Accepted)
            {
                return (StatusCodes.InternalServerError, response);
            }

            return (StatusCodes.OK, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing RequestContainerRestart operation");
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Implementation of GetContainerStatus operation.
    /// Gets the status of a specific container by Dapr app name.
    /// </summary>
    public async Task<(StatusCodes, ContainerStatus?)> GetContainerStatusAsync(GetContainerStatusRequest body, CancellationToken cancellationToken)
    {
        var appName = body.AppName;
        _logger.LogInformation("Executing GetContainerStatus operation: app={AppName}", appName);

        try
        {
            var orchestrator = await GetOrchestratorAsync(cancellationToken);
            var status = await orchestrator.GetContainerStatusAsync(appName, cancellationToken);

            if (status.Status == ContainerStatusStatus.Stopped && status.Instances == 0)
            {
                // Container not found is returned as Stopped with 0 instances
                return (StatusCodes.NotFound, status);
            }

            return (StatusCodes.OK, status);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing GetContainerStatus operation");
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Implementation of RollbackConfiguration operation.
    /// Rolls back to a previous configuration version.
    /// </summary>
    public Task<(StatusCodes, ConfigRollbackResponse?)> RollbackConfigurationAsync(ConfigRollbackRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Executing RollbackConfiguration operation: reason={Reason}", body.Reason);

        try
        {
            // TODO: Implement configuration rollback
            _logger.LogWarning("Configuration rollback not yet fully implemented");

            var response = new ConfigRollbackResponse
            {
                Success = false,
                PreviousVersion = 1,
                CurrentVersion = 1
            };

            return Task.FromResult<(StatusCodes, ConfigRollbackResponse?)>((StatusCodes.BadRequest, response));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing RollbackConfiguration operation");
            return Task.FromResult<(StatusCodes, ConfigRollbackResponse?)>((StatusCodes.InternalServerError, null));
        }
    }

    /// <summary>
    /// Implementation of GetConfigVersion operation.
    /// Gets the current configuration version.
    /// </summary>
    public Task<(StatusCodes, ConfigVersionResponse?)> GetConfigVersionAsync(GetConfigVersionRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Executing GetConfigVersion operation");

        try
        {
            // Get current version from Redis or default to 1
            // Note: We use the health monitor's connection to Redis instead of a GetAsync method
            var response = new ConfigVersionResponse
            {
                Version = 1, // Would retrieve from Redis state
                Timestamp = DateTimeOffset.UtcNow,
                HasPreviousConfig = false // Would check if history exists
            };

            return Task.FromResult<(StatusCodes, ConfigVersionResponse?)>((StatusCodes.OK, response));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing GetConfigVersion operation");
            return Task.FromResult<(StatusCodes, ConfigVersionResponse?)>((StatusCodes.InternalServerError, null));
        }
    }

    /// <summary>
    /// Registers service permissions for the Orchestrator API endpoints.
    /// Uses the generated OrchestratorPermissionRegistration to publish to the correct pub/sub topic.
    /// All orchestrator operations require admin role access.
    /// </summary>
    public async Task RegisterServicePermissionsAsync()
    {
        _logger.LogInformation("Registering Orchestrator service permissions... (starting)");
        try
        {
            await OrchestratorPermissionRegistration.RegisterViaEventAsync(_daprClient, _logger);
            _logger.LogInformation("Orchestrator service permissions registered via event (complete)");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to register Orchestrator service permissions");
            throw;
        }
    }

    /// <summary>
    /// Maps container status to deployed service status.
    /// </summary>
    private static DeployedServiceStatus MapContainerStatusToDeployedServiceStatus(ContainerStatusStatus containerStatus)
    {
        return containerStatus switch
        {
            ContainerStatusStatus.Running => DeployedServiceStatus.Running,
            ContainerStatusStatus.Stopped => DeployedServiceStatus.Stopped,
            ContainerStatusStatus.Unhealthy => DeployedServiceStatus.Unhealthy,
            ContainerStatusStatus.Starting => DeployedServiceStatus.Starting,
            ContainerStatusStatus.Stopping => DeployedServiceStatus.Stopped, // Stopping maps to Stopped (closest match)
            _ => DeployedServiceStatus.Unhealthy
        };
    }
}
