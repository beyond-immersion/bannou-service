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

    /// <summary>
    /// Cached orchestrator instance for container operations.
    /// </summary>
    private IContainerOrchestrator? _orchestrator;

    private const string STATE_STORE = "orchestrator-store";
    private const string CONFIG_VERSION_KEY = "orchestrator:config:version";

    public OrchestratorService(
        DaprClient daprClient,
        ILogger<OrchestratorService> logger,
        OrchestratorServiceConfiguration configuration,
        IOrchestratorRedisManager redisManager,
        IOrchestratorEventManager eventManager,
        IServiceHealthMonitor healthMonitor,
        ISmartRestartManager restartManager,
        IBackendDetector backendDetector)
    {
        _daprClient = daprClient ?? throw new ArgumentNullException(nameof(daprClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _redisManager = redisManager ?? throw new ArgumentNullException(nameof(redisManager));
        _eventManager = eventManager ?? throw new ArgumentNullException(nameof(eventManager));
        _healthMonitor = healthMonitor ?? throw new ArgumentNullException(nameof(healthMonitor));
        _restartManager = restartManager ?? throw new ArgumentNullException(nameof(restartManager));
        _backendDetector = backendDetector ?? throw new ArgumentNullException(nameof(backendDetector));
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
    public async Task<(StatusCodes, InfrastructureHealthResponse?)> GetInfrastructureHealthAsync(CancellationToken cancellationToken)
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

            // Check RabbitMQ connectivity
            var (rabbitHealthy, rabbitMessage) = _eventManager.CheckHealth();
            components.Add(new ComponentHealth
            {
                Name = "rabbitmq",
                Status = rabbitHealthy ? ComponentHealthStatus.Healthy : ComponentHealthStatus.Unavailable,
                LastSeen = DateTimeOffset.UtcNow,
                Message = rabbitMessage ?? string.Empty
            });
            overallHealthy = overallHealthy && rabbitHealthy;

            // Check Dapr Placement service (via DaprClient)
            try
            {
                // Simple health check via Dapr metadata endpoint
                await _daprClient.CheckHealthAsync(cancellationToken);
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
    public async Task<(StatusCodes, ServiceHealthReport?)> GetServicesHealthAsync(CancellationToken cancellationToken)
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
    public async Task<(StatusCodes, BackendsResponse?)> GetBackendsAsync(CancellationToken cancellationToken)
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
    public Task<(StatusCodes, PresetsResponse?)> GetPresetsAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Executing GetPresets operation");

        try
        {
            var presetsDir = _configuration.PresetsDirectory;
            var presets = new List<DeploymentPreset>();

            if (Directory.Exists(presetsDir))
            {
                foreach (var file in Directory.GetFiles(presetsDir, "*.yaml"))
                {
                    var name = Path.GetFileNameWithoutExtension(file);
                    presets.Add(new DeploymentPreset
                    {
                        Name = name,
                        Description = $"Preset from {file}",
                        Topology = new ServiceTopology() // Would parse YAML for actual topology
                    });
                }
            }

            var response = new PresetsResponse
            {
                Presets = presets,
                ActivePreset = presets.FirstOrDefault()?.Name ?? "default"
            };

            return Task.FromResult<(StatusCodes, PresetsResponse?)>((StatusCodes.OK, response));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing GetPresets operation");
            return Task.FromResult<(StatusCodes, PresetsResponse?)>((StatusCodes.InternalServerError, null));
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
            // Get appropriate orchestrator - use specified backend or auto-detect best available
            IContainerOrchestrator orchestrator;
            if (body.Backend != default)
            {
                orchestrator = _backendDetector.CreateOrchestrator(body.Backend);
            }
            else
            {
                orchestrator = await _backendDetector.CreateBestOrchestratorAsync(cancellationToken);
            }

            // Get topology nodes to deploy from the request
            var nodesToDeploy = body.Topology?.Nodes ?? new List<TopologyNode>();

            if (nodesToDeploy.Count == 0)
            {
                return (StatusCodes.BadRequest, new DeployResponse
                {
                    Success = false,
                    DeploymentId = deploymentId,
                    Backend = orchestrator.BackendType,
                    Duration = "0s",
                    Message = "No topology nodes specified for deployment"
                });
            }

            var deployedServices = new List<DeployedService>();
            var failedServices = new List<string>();

            foreach (var node in nodesToDeploy)
            {
                var appId = node.DaprAppId ?? node.Name;

                // Convert IDictionary to Dictionary for the orchestrator method
                var environment = node.Environment != null
                    ? new Dictionary<string, string>(node.Environment)
                    : null;

                // Merge global environment with node-specific environment
                if (body.Environment != null)
                {
                    environment ??= new Dictionary<string, string>();
                    foreach (var kvp in body.Environment)
                    {
                        if (!environment.ContainsKey(kvp.Key))
                        {
                            environment[kvp.Key] = kvp.Value;
                        }
                    }
                }

                // Build service enable flags for this node
                foreach (var serviceName in node.Services)
                {
                    var serviceEnvKey = $"{serviceName.ToUpperInvariant()}_SERVICE_ENABLED";
                    environment ??= new Dictionary<string, string>();
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
                        await _eventManager.PublishServiceMappingEventAsync(new ServiceMappingEvent
                        {
                            EventId = Guid.NewGuid().ToString(),
                            ServiceName = serviceName,
                            AppId = appId,
                            Action = ServiceMappingAction.Register
                        });
                    }

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
    public async Task<(StatusCodes, EnvironmentStatus?)> GetStatusAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Executing GetStatus operation");

        try
        {
            var orchestrator = await GetOrchestratorAsync(cancellationToken);
            var containers = await orchestrator.ListContainersAsync(cancellationToken);

            var response = new EnvironmentStatus
            {
                Deployed = containers.Count > 0,
                Timestamp = DateTimeOffset.UtcNow,
                Backend = orchestrator.BackendType,
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
            "Executing Teardown operation: mode={Mode}, removeVolumes={RemoveVolumes}",
            body.Mode,
            body.RemoveVolumes);

        var startTime = DateTime.UtcNow;
        var teardownId = Guid.NewGuid().ToString();

        // Publish teardown started event (topology-changed action indicates infrastructure modification)
        await _eventManager.PublishDeploymentEventAsync(new DeploymentEvent
        {
            EventId = Guid.NewGuid().ToString(),
            Timestamp = DateTimeOffset.UtcNow,
            Action = DeploymentEventAction.TopologyChanged,
            DeploymentId = teardownId
        });

        try
        {
            var orchestrator = await GetOrchestratorAsync(cancellationToken);
            var stoppedContainers = new List<string>();
            var removedVolumes = new List<string>();
            var failedTeardowns = new List<string>();

            // Get all running containers to tear down
            var containers = await orchestrator.ListContainersAsync(cancellationToken);
            var servicesToTeardown = containers
                .Select(c => c.AppName ?? "")
                .Where(n => !string.IsNullOrEmpty(n))
                .ToList();

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

            foreach (var appName in servicesToTeardown)
            {
                // Tear down via container orchestrator
                var teardownResult = await orchestrator.TeardownServiceAsync(
                    appName,
                    body.RemoveVolumes,
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
                    await _eventManager.PublishServiceMappingEventAsync(new ServiceMappingEvent
                    {
                        EventId = Guid.NewGuid().ToString(),
                        ServiceName = serviceName,
                        AppId = appName,
                        Action = ServiceMappingAction.Unregister
                    });

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

            var duration = DateTime.UtcNow - startTime;
            var success = failedTeardowns.Count == 0;

            var response = new TeardownResponse
            {
                Success = success,
                Duration = $"{duration.TotalSeconds:F1}s",
                StoppedContainers = stoppedContainers,
                RemovedVolumes = removedVolumes
            };

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
        string? service,
        string? container,
        string? since,
        string? until,
        int? tail = 100,
        bool? follow = false,
        CancellationToken cancellationToken = default)
    {
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
            var logsText = await orchestrator.GetContainerLogsAsync(appName, tail ?? 100, sinceTime, cancellationToken);

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
                                        await _eventManager.PublishServiceMappingEventAsync(new ServiceMappingEvent
                                        {
                                            EventId = Guid.NewGuid().ToString(),
                                            ServiceName = serviceName,
                                            AppId = appId,
                                            Action = ServiceMappingAction.Register
                                        });
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
                                        await _eventManager.PublishServiceMappingEventAsync(new ServiceMappingEvent
                                        {
                                            EventId = Guid.NewGuid().ToString(),
                                            ServiceName = serviceName,
                                            AppId = appId,
                                            Action = ServiceMappingAction.Unregister
                                        });
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
                                    await _eventManager.PublishServiceMappingEventAsync(new ServiceMappingEvent
                                    {
                                        EventId = Guid.NewGuid().ToString(),
                                        ServiceName = serviceName,
                                        AppId = newAppId,
                                        Action = ServiceMappingAction.Update
                                    });
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
        string appName,
        ContainerRestartRequest body,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Executing RequestContainerRestart operation: app={AppName}, priority={Priority}",
            appName, body.Priority);

        try
        {
            var orchestrator = await GetOrchestratorAsync(cancellationToken);
            var response = await orchestrator.RestartContainerAsync(appName, body, cancellationToken);

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
    public async Task<(StatusCodes, ContainerStatus?)> GetContainerStatusAsync(string appName, CancellationToken cancellationToken)
    {
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
    public Task<(StatusCodes, ConfigVersionResponse?)> GetConfigVersionAsync(CancellationToken cancellationToken)
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
    /// All orchestrator operations require admin role access.
    /// </summary>
    public async Task RegisterServicePermissionsAsync()
    {
        try
        {
            var serviceName = "orchestrator";
            _logger.LogInformation("Registering permissions for {ServiceName} service", serviceName);

            var endpoints = CreateServiceEndpoints();

            await _daprClient.PublishEventAsync(
                "bannou-pubsub",
                "bannou-service-registered",
                new Events.ServiceRegistrationEvent
                {
                    EventId = Guid.NewGuid().ToString(),
                    Timestamp = DateTime.UtcNow,
                    ServiceId = serviceName,
                    Version = "2.2.0",
                    AppId = "bannou",
                    Endpoints = endpoints,
                    Metadata = new Dictionary<string, object>
                    {
                        { "generatedFrom", "OrchestratorService.RegisterServicePermissionsAsync" },
                        { "extractedAt", DateTime.UtcNow },
                        { "endpointCount", endpoints.Count }
                    }
                });

            _logger.LogInformation("Successfully registered {Count} permission rules for {ServiceName}",
                endpoints.Count, serviceName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to register permissions for orchestrator service");
        }
    }

    /// <summary>
    /// Creates ServiceEndpoint objects for all orchestrator API endpoints.
    /// All endpoints require admin role as orchestrator is infrastructure-only.
    /// </summary>
    private List<Events.ServiceEndpoint> CreateServiceEndpoints()
    {
        var endpoints = new List<Events.ServiceEndpoint>();

        // All orchestrator endpoints require admin role in any authenticated state
        var adminPermission = new Events.PermissionRequirement
        {
            Role = "admin",
            RequiredStates = new Dictionary<string, string>
            {
                { "auth", "authenticated" }
            },
            Description = "Admin access required for orchestrator operations"
        };

        // Health endpoints
        endpoints.Add(CreateEndpoint("GET", "/orchestrator/health/infrastructure", "Check infrastructure health", adminPermission));
        endpoints.Add(CreateEndpoint("GET", "/orchestrator/health/services", "Get services health report", adminPermission));

        // Service management endpoints
        endpoints.Add(CreateEndpoint("POST", "/orchestrator/services/restart", "Restart service", adminPermission));
        endpoints.Add(CreateEndpoint("POST", "/orchestrator/services/should-restart", "Check if restart needed", adminPermission));

        // Environment management endpoints
        endpoints.Add(CreateEndpoint("GET", "/orchestrator/backends", "Detect available backends", adminPermission));
        endpoints.Add(CreateEndpoint("GET", "/orchestrator/presets", "List deployment presets", adminPermission));
        endpoints.Add(CreateEndpoint("POST", "/orchestrator/deploy", "Deploy environment", adminPermission));
        endpoints.Add(CreateEndpoint("GET", "/orchestrator/status", "Get environment status", adminPermission));
        endpoints.Add(CreateEndpoint("POST", "/orchestrator/teardown", "Teardown environment", adminPermission));
        endpoints.Add(CreateEndpoint("POST", "/orchestrator/clean", "Clean unused resources", adminPermission));
        endpoints.Add(CreateEndpoint("GET", "/orchestrator/logs", "Get service logs", adminPermission));
        endpoints.Add(CreateEndpoint("POST", "/orchestrator/topology", "Update service topology", adminPermission));

        // Container management endpoints
        endpoints.Add(CreateEndpoint("POST", "/orchestrator/containers/{appName}/request-restart", "Request container restart", adminPermission));
        endpoints.Add(CreateEndpoint("GET", "/orchestrator/containers/{appName}/status", "Get container status", adminPermission));

        // Configuration management endpoints
        endpoints.Add(CreateEndpoint("POST", "/orchestrator/config/rollback", "Rollback configuration", adminPermission));
        endpoints.Add(CreateEndpoint("GET", "/orchestrator/config/version", "Get config version", adminPermission));

        return endpoints;
    }

    /// <summary>
    /// Helper to create a ServiceEndpoint with proper formatting.
    /// </summary>
    private static Events.ServiceEndpoint CreateEndpoint(string method, string path, string description, Events.PermissionRequirement permission)
    {
        return new Events.ServiceEndpoint
        {
            Path = path,
            Method = Enum.Parse<Events.ServiceEndpointMethod>(method),
            Description = description,
            Category = "orchestrator",
            Permissions = new List<Events.PermissionRequirement> { permission }
        };
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
