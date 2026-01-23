using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Orchestrator;
using BeyondImmersion.BannouService.Plugins;
using BeyondImmersion.BannouService.Services;
using LibOrchestrator;
using LibOrchestrator.Backends;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Orchestrator;

/// <summary>
/// Implementation of the Orchestrator service.
/// This class contains the business logic for all Orchestrator operations.
/// Uses lib-state and lib-messaging infrastructure for state management and event publishing.
/// </summary>
[BannouService("orchestrator", typeof(IOrchestratorService), lifetime: ServiceLifetime.Scoped)]
public partial class OrchestratorService : IOrchestratorService
{
    private readonly IMessageBus _messageBus;
    private readonly ILogger<OrchestratorService> _logger;
    private readonly OrchestratorServiceConfiguration _configuration;
    private readonly IOrchestratorStateManager _stateManager;
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

    /// <summary>
    /// Timestamp when the orchestrator was cached. Used for TTL-based invalidation.
    /// </summary>
    private DateTimeOffset _orchestratorCachedAt;

    /// <summary>
    /// Last known deployment configuration. Loaded on startup for awareness mode.
    /// Updated after each successful deployment.
    /// </summary>
    private DeploymentConfiguration? _lastKnownDeployment;

    private const string CONFIG_VERSION_KEY = "orchestrator:config:version";

    /// <summary>
    /// Default preset name - all services on one default node.
    /// This matches the default Docker Compose behavior where everything runs in one container.
    /// </summary>
    private static readonly string DEFAULT_PRESET = AppConstants.DEFAULT_APP_NAME;

    /// <summary>
    /// Infrastructure services that should NEVER be included in routing mappings.
    /// These services must always be handled by the local node, never delegated to another instance.
    /// </summary>
    private static readonly HashSet<string> InfrastructureServices = new(StringComparer.OrdinalIgnoreCase)
    {
        "state",
        "messaging",
        "mesh"
    };

    /// <summary>
    /// Checks if a service name is an infrastructure service that should never be routed.
    /// </summary>
    private static bool IsInfrastructureService(string serviceName) =>
        InfrastructureServices.Contains(serviceName);

    /// <summary>
    /// Gets the configured default backend, parsing from the configuration string.
    /// Falls back to Compose if the configured value is invalid.
    /// </summary>
    private BackendType GetConfiguredDefaultBackend()
    {
        var configuredBackend = _configuration.DefaultBackend;
        if (string.IsNullOrWhiteSpace(configuredBackend))
            return BackendType.Compose;

        return configuredBackend.ToLowerInvariant() switch
        {
            "kubernetes" => BackendType.Kubernetes,
            "portainer" => BackendType.Portainer,
            "swarm" => BackendType.Swarm,
            "compose" => BackendType.Compose,
            _ => BackendType.Compose
        };
    }

    public OrchestratorService(
        IMessageBus messageBus,
        ILogger<OrchestratorService> logger,
        ILoggerFactory loggerFactory,
        OrchestratorServiceConfiguration configuration,
        IOrchestratorStateManager stateManager,
        IOrchestratorEventManager eventManager,
        IServiceHealthMonitor healthMonitor,
        ISmartRestartManager restartManager,
        IBackendDetector backendDetector,
        IEventConsumer eventConsumer)
    {
        _messageBus = messageBus;
        _logger = logger;
        _loggerFactory = loggerFactory;
        _configuration = configuration;
        _stateManager = stateManager;
        _eventManager = eventManager;
        _healthMonitor = healthMonitor;
        _restartManager = restartManager;
        _backendDetector = backendDetector;

        // Create preset loader with configured presets directory
        var presetsPath = configuration.PresetsHostPath ?? "/app/provisioning/orchestrator/presets";
        _presetLoader = new PresetLoader(_loggerFactory.CreateLogger<PresetLoader>(), presetsPath);

        // Register event handlers via partial class (OrchestratorServiceEvents.cs)
        RegisterEventConsumers(eventConsumer);
    }

    /// <summary>
    /// Ensures the last known deployment state is loaded from persistent storage.
    /// Called lazily on first access to provide startup awareness.
    /// </summary>
    private async Task EnsureLastDeploymentLoadedAsync()
    {
        // Skip if already loaded
        if (_lastKnownDeployment != null)
        {
            return;
        }

        try
        {
            _lastKnownDeployment = await _stateManager.GetCurrentConfigurationAsync();

            if (_lastKnownDeployment != null)
            {
                _logger.LogInformation(
                    "Loaded previous deployment state: DeploymentId={DeploymentId}, Preset={Preset}, Version={Version}, DeployedAt={Timestamp}",
                    _lastKnownDeployment.DeploymentId,
                    _lastKnownDeployment.PresetName,
                    _lastKnownDeployment.Version,
                    _lastKnownDeployment.Timestamp);
            }
            else
            {
                _logger.LogInformation("No previous deployment state found - clean start");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load previous deployment state - continuing with clean start");
        }
    }

    /// <summary>
    /// Gets or creates the container orchestrator for the best available backend.
    /// Implements TTL-based cache invalidation to handle connection staleness.
    /// </summary>
    private async Task<IContainerOrchestrator> GetOrchestratorAsync(CancellationToken cancellationToken)
    {
        if (_orchestrator != null)
        {
            // Check if TTL has expired (0 = caching disabled)
            var ttlMinutes = _configuration.CacheTtlMinutes;
            if (ttlMinutes > 0)
            {
                var cacheAge = DateTimeOffset.UtcNow - _orchestratorCachedAt;
                if (cacheAge.TotalMinutes >= ttlMinutes)
                {
                    _logger.LogDebug(
                        "Orchestrator cache TTL expired ({Age:F1}min >= {Ttl}min), re-validating connection",
                        cacheAge.TotalMinutes, ttlMinutes);

                    // Re-validate the connection using CheckAvailabilityAsync
                    var (isAvailable, message) = await _orchestrator.CheckAvailabilityAsync(cancellationToken);

                    if (isAvailable)
                    {
                        // Connection still valid - refresh the cache timestamp
                        _orchestratorCachedAt = DateTimeOffset.UtcNow;
                        _logger.LogDebug(
                            "Orchestrator connection re-validated successfully: {Message}",
                            message);
                    }
                    else
                    {
                        // Connection invalid - dispose and re-detect
                        _logger.LogWarning(
                            "Orchestrator connection invalid after TTL expiry: {Message}. Re-detecting backend.",
                            message);
                        _orchestrator.Dispose();
                        _orchestrator = null;
                    }
                }
            }

            if (_orchestrator != null)
            {
                return _orchestrator;
            }
        }

        _orchestrator = await _backendDetector.CreateBestOrchestratorAsync(cancellationToken);
        _orchestratorCachedAt = DateTimeOffset.UtcNow;
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

            // Check state store connectivity
            var (stateHealthy, stateMessage, stateOpTime) = await _stateManager.CheckHealthAsync();
            components.Add(new ComponentHealth
            {
                Name = "statestore",
                Status = stateHealthy ? ComponentHealthStatus.Healthy : ComponentHealthStatus.Unavailable,
                LastSeen = DateTimeOffset.UtcNow,
                Message = stateMessage, // Nullable per schema
                Metrics = stateOpTime.HasValue
                    ? (object)new Dictionary<string, object> { { "operationTimeMs", stateOpTime.Value.TotalMilliseconds } }
                    : new Dictionary<string, object>()
            });
            overallHealthy = overallHealthy && stateHealthy;

            // Check pub/sub path by publishing a tiny health probe
            bool pubsubHealthy;
            string pubsubMessage;
            try
            {
                await _messageBus.TryPublishAsync("orchestrator-health", new OrchestratorHealthPingEvent
                {
                    EventName = "orchestrator.health_ping",
                    EventId = Guid.NewGuid(),
                    Timestamp = DateTimeOffset.UtcNow,
                    Status = OrchestratorHealthPingEventStatus.Ok
                });
                pubsubHealthy = true;
                pubsubMessage = "Message bus pub/sub path active";
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Message bus pub/sub health check failed");
                pubsubHealthy = false;
                pubsubMessage = $"Pub/sub check failed: {ex.Message}";
            }
            components.Add(new ComponentHealth
            {
                Name = "pubsub",
                Status = pubsubHealthy ? ComponentHealthStatus.Healthy : ComponentHealthStatus.Unavailable,
                LastSeen = DateTimeOffset.UtcNow,
                Message = pubsubMessage // Always set in try/catch above
            });
            overallHealthy = overallHealthy && pubsubHealthy;

            // Check messaging infrastructure health
            bool messagingHealthy;
            string messagingMessage;
            try
            {
                // If we got here and the messageBus is not null, the infrastructure is healthy
                // The actual pub/sub test above validates connectivity
                messagingHealthy = _messageBus != null;
                messagingMessage = messagingHealthy
                    ? "Messaging infrastructure available"
                    : "Messaging infrastructure unavailable";
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Messaging infrastructure health check failed");
                messagingHealthy = false;
                messagingMessage = $"Messaging check failed: {ex.Message}";
            }
            components.Add(new ComponentHealth
            {
                Name = "messaging",
                Status = messagingHealthy ? ComponentHealthStatus.Healthy : ComponentHealthStatus.Unavailable,
                LastSeen = DateTimeOffset.UtcNow,
                Message = messagingMessage
            });
            overallHealthy = overallHealthy && messagingHealthy;

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
            await PublishErrorEventAsync("GetInfrastructureHealth", ex.GetType().Name, ex.Message);
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
            await PublishErrorEventAsync("GetServicesHealth", ex.GetType().Name, ex.Message);
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
            await PublishErrorEventAsync("RestartService", ex.GetType().Name, ex.Message, details: new { ServiceName = body.ServiceName });
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
            await PublishErrorEventAsync("ShouldRestartService", ex.GetType().Name, ex.Message, details: new { ServiceName = body.ServiceName });
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
            await PublishErrorEventAsync("GetBackends", ex.GetType().Name, ex.Message);
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
            await PublishErrorEventAsync("GetPresets", ex.GetType().Name, ex.Message);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Implementation of Deploy operation.
    /// Deploys services using the selected backend and topology or preset.
    /// </summary>
    public async Task<(StatusCodes, DeployResponse?)> DeployAsync(DeployRequest body, CancellationToken cancellationToken)
    {
        // Resolve effective backend: use request value if provided, otherwise use configured default
        var effectiveBackend = body.Backend ?? GetConfiguredDefaultBackend();

        _logger.LogInformation(
            "Executing Deploy operation: preset={Preset}, backend={Backend} (requested={RequestedBackend}), dryRun={DryRun}",
            body.Preset, effectiveBackend, body.Backend, body.DryRun);

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
            Backend = effectiveBackend
        });

        try
        {
            // Detect available backends first
            var backends = await _backendDetector.DetectBackendsAsync(cancellationToken);

            // Check if the effective backend is available
            var requestedBackend = backends.Backends?.FirstOrDefault(b => b.Type == effectiveBackend);

            IContainerOrchestrator orchestrator;
            if (requestedBackend != null && !requestedBackend.Available)
            {
                // Backend exists in detection but is not available - fail the deployment
                var errorMsg = requestedBackend.Error ?? "Backend not available";
                var availableBackends = string.Join(", ", backends.Backends?.Where(b => b.Available).Select(b => b.Type) ?? Enumerable.Empty<BackendType>());
                _logger.LogWarning(
                    "Requested backend {Backend} is not available: {Error}. Available backends: {AvailableBackends}",
                    effectiveBackend, errorMsg, availableBackends);
                return (StatusCodes.BadRequest, null);
            }

            // Use the effective backend if available, otherwise use recommended
            if (requestedBackend != null && requestedBackend.Available)
            {
                orchestrator = _backendDetector.CreateOrchestrator(effectiveBackend);
            }
            else
            {
                // Fall back to recommended backend
                orchestrator = _backendDetector.CreateOrchestrator(backends.Recommended);
                _logger.LogInformation(
                    "Using recommended backend {Recommended} instead of {Requested}",
                    backends.Recommended, effectiveBackend);
            }

            // Get topology nodes to deploy - from preset or direct topology
            ICollection<TopologyNode> nodesToDeploy;
            IDictionary<string, string>? presetEnvironment = null;

            // Check for "reset to default" request FIRST - "default", "bannou", or empty preset with no topology
            // This must be checked before auto-cleanup since ResetToDefaultTopologyAsync handles its own cleanup.
            var isResetToDefault = IsResetToDefaultRequest(body.Preset, body.Topology);

            // AUTO-CLEANUP: If there's an existing deployment, clean it up before deploying new containers.
            // This prevents orphaned containers when DeployAsync is called multiple times without cleanup.
            // Each deployment is treated as a replacement, not an accumulation.
            // SKIP for reset-to-default requests since ResetToDefaultTopologyAsync handles cleanup.
            if (!body.DryRun && !isResetToDefault)
            {
                var previousDeployment = await _stateManager.GetCurrentConfigurationAsync();
                if (previousDeployment != null && previousDeployment.Services.Count > 0)
                {
                    var previousAppIds = previousDeployment.Services.Values
                        .Where(s => s.Enabled && !string.IsNullOrEmpty(s.AppId))
                        .Select(s => s.AppId!)
                        .Where(appId => !appId.Equals("bannou", StringComparison.OrdinalIgnoreCase))
                        .Distinct()
                        .ToList();

                    if (previousAppIds.Count > 0)
                    {
                        _logger.LogInformation(
                            "Auto-cleanup: Found existing deployment with {Count} containers [{AppIds}], tearing down before new deployment",
                            previousAppIds.Count, string.Join(", ", previousAppIds));

                        // CRITICAL: Reset service mappings FIRST, before tearing down containers.
                        // This ensures routing proxies (OpenResty, lib-mesh) get updated routes
                        // before the old containers become unavailable. Prevents 404/502 errors
                        // during the transition window.
                        await _healthMonitor.ResetAllMappingsToDefaultAsync();

                        // Invalidate OpenResty routing cache so it reads fresh routes from Redis
                        await InvalidateOpenRestryRoutingCacheAsync();

                        // Now tear down the old containers (routes already point elsewhere)
                        foreach (var appId in previousAppIds)
                        {
                            try
                            {
                                var teardownResult = await orchestrator.TeardownServiceAsync(
                                    appId,
                                    removeVolumes: false,
                                    cancellationToken);

                                if (teardownResult.Success)
                                {
                                    _logger.LogInformation("Auto-cleanup: Torn down previous container {AppId}", appId);
                                }
                                else
                                {
                                    _logger.LogWarning(
                                        "Auto-cleanup: Failed to tear down {AppId}: {Message}",
                                        appId, teardownResult.Message);
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "Auto-cleanup: Error tearing down container {AppId}", appId);
                            }
                        }
                    }
                }
            }

            if (isResetToDefault)
            {
                // Reset to default topology: tear down all dynamically deployed nodes, reset mappings to "bannou"
                _logger.LogInformation("Reset to default topology requested (preset: '{Preset}')", body.Preset ?? "(empty)");

                var resetResult = await ResetToDefaultTopologyAsync(orchestrator, deploymentId, cancellationToken);

                var resetDuration = DateTime.UtcNow - startTime;
                return (StatusCodes.OK, new DeployResponse
                {
                    Success = resetResult.Success,
                    DeploymentId = deploymentId,
                    Backend = orchestrator.BackendType,
                    Preset = "default",
                    Duration = $"{resetDuration.TotalSeconds:F1}s",
                    Message = resetResult.Message,
                    Topology = new ServiceTopology { Nodes = new List<TopologyNode>() }, // Empty topology = default
                    Services = resetResult.TornDownServices.Select(s => new DeployedService
                    {
                        Name = s,
                        Status = DeployedServiceStatus.Stopped,
                        Node = AppConstants.DEFAULT_APP_NAME
                    }).ToList()
                });
            }
            else if (!string.IsNullOrEmpty(body.Preset))
            {
                // Load preset by name
                var preset = await _presetLoader.LoadPresetAsync(body.Preset, cancellationToken);
                if (preset == null)
                {
                    _logger.LogWarning("Preset '{Preset}' not found", body.Preset);
                    return (StatusCodes.NotFound, null);
                }

                var topology = _presetLoader.ConvertToTopology(preset);
                nodesToDeploy = topology.Nodes;
                presetEnvironment = preset.Environment;

                _logger.LogInformation(
                    "Loaded preset '{Preset}' with {NodeCount} nodes",
                    body.Preset, nodesToDeploy.Count);

                // Initialize processing pools if present in preset
                if (preset.ProcessingPools != null && preset.ProcessingPools.Count > 0)
                {
                    await InitializePoolConfigurationsAsync(preset.ProcessingPools, cancellationToken);
                    _logger.LogInformation(
                        "Initialized {Count} processing pool configurations from preset",
                        preset.ProcessingPools.Count);
                }
            }
            else if (body.Topology?.Nodes != null && body.Topology.Nodes.Count > 0)
            {
                nodesToDeploy = body.Topology.Nodes;
            }
            else
            {
                _logger.LogWarning("No topology nodes specified for deployment. Provide a preset name or topology");
                return (StatusCodes.BadRequest, null);
            }

            var deployedServices = new List<DeployedService>();
            var failedServices = new List<string>();
            var expectedAppIds = new HashSet<string>(); // Track app-ids that should send heartbeats

            // Track successfully deployed nodes for rollback on partial failure
            var successfullyDeployedNodes = new List<(string AppId, ICollection<string> Services)>();

            foreach (var node in nodesToDeploy)
            {
                var appId = node.AppId ?? node.Name;

                // VALIDATION: Reject any node attempting to deploy orchestrator service
                // Orchestrator cannot deploy itself or another orchestrator - route cannot be overridden
                if (node.Services.Any(s => s.Equals("orchestrator", StringComparison.OrdinalIgnoreCase)))
                {
                    _logger.LogError(
                        "REJECTED: Node '{NodeName}' contains 'orchestrator' service - orchestrator cannot deploy itself or another orchestrator",
                        node.Name);
                    failedServices.Add($"{node.Name}: Cannot deploy orchestrator service - route cannot be overridden");
                    continue; // Skip this node
                }

                // Build environment with proper layering (lowest to highest priority):
                // 1. Orchestrator's own environment (foundation)
                // 2. Preset environment
                // 3. Node-specific environment
                // 4. Request body environment (highest priority)
                var environment = new Dictionary<string, string>();

                // Layer 1: Forward orchestrator's own environment as foundation
                // This ensures deployed containers inherit all service configuration
                // (AUTH_*, STATE_*, CONNECT_*, etc.) without needing to duplicate in presets
                //
                // IMPLEMENTATION TENETS exception: Direct Environment.GetEnvironmentVariables() access
                // is required here because we're forwarding UNKNOWN configuration to deployed containers.
                // This is the orchestrator's core responsibility - not reading config for itself.
                // Uses strict whitelist (IsAllowedEnvironmentVariable) and excludes per-container values.
                foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
                {
                    var key = entry.Key?.ToString();
                    var value = entry.Value?.ToString();
                    if (string.IsNullOrEmpty(key) || value == null)
                        continue;

                    // Only forward vars with valid service prefixes (whitelist approach)
                    if (!IsAllowedEnvironmentVariable(key))
                        continue;

                    environment[key] = value;
                }

                // Layer 2: Preset environment (overrides orchestrator's environment)
                if (presetEnvironment != null)
                {
                    foreach (var kvp in presetEnvironment)
                    {
                        environment[kvp.Key] = kvp.Value;
                    }
                }

                // Layer 3: Node-specific environment (overrides preset)
                if (node.Environment != null)
                {
                    foreach (var kvp in node.Environment)
                    {
                        environment[kvp.Key] = kvp.Value;
                    }
                }

                // Layer 4: Request body environment (highest priority, overrides all)
                if (body.Environment != null)
                {
                    foreach (var kvp in body.Environment)
                    {
                        environment[kvp.Key] = kvp.Value;
                    }
                }

                // CRITICAL: Disable all services by default, then enable only the specified ones
                // This MUST override any forwarded SERVICES_ENABLED=true from the orchestrator's environment
                // Without this, deployed containers would run ALL services (including orchestrator itself!)
                environment["SERVICES_ENABLED"] = "false";

                // Build service enable flags for this node's specified services only
                foreach (var serviceName in node.Services)
                {
                    var serviceEnvKey = $"{serviceName.ToUpperInvariant().Replace("-", "_")}_SERVICE_ENABLED";
                    environment[serviceEnvKey] = "true";
                }

                // CRITICAL: Set the app-id so the container knows its identity for heartbeats and mesh routing
                // Without this, the container falls back to "bannou" and the orchestrator never receives
                // the expected heartbeat from the deployed app-id
                environment["BANNOU_APP_ID"] = appId;

                _logger.LogDebug(
                    "Node {NodeName} service configuration: SERVICES_ENABLED=false, APP_ID={AppId}, enabled services: {Services}",
                    node.Name, appId, string.Join(", ", node.Services));

                // DryRun mode: collect what would be deployed without actually deploying
                if (body.DryRun)
                {
                    deployedServices.Add(new DeployedService
                    {
                        Name = node.Name,
                        Status = DeployedServiceStatus.Starting, // Use Starting as placeholder for dryRun
                        Node = Environment.MachineName
                    });
                    _logger.LogInformation(
                        "[DryRun] Would deploy node {NodeName} with app-id {AppId}, services: {Services}",
                        node.Name, appId, string.Join(", ", node.Services));
                    continue;
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

                    // Register service routing for each service on this node
                    // ServiceHealthMonitor will automatically publish FullServiceMappingsEvent
                    foreach (var serviceName in node.Services)
                    {
                        await _healthMonitor.SetServiceRoutingAsync(serviceName, appId);
                    }

                    // Track this app-id for heartbeat waiting
                    expectedAppIds.Add(appId);

                    // Track for potential rollback
                    successfullyDeployedNodes.Add((appId, node.Services));

                    _logger.LogInformation(
                        "Node {NodeName} deployed with app-id {AppId}, {ServiceCount} service routings set",
                        node.Name, appId, node.Services.Count);
                }
                else
                {
                    failedServices.Add($"{node.Name}: {deployResult.Message}");
                    _logger.LogWarning(
                        "Failed to deploy node {NodeName}: {Message}",
                        node.Name, deployResult.Message);

                    // ROLLBACK: On partial failure, tear down all previously successful deployments
                    if (successfullyDeployedNodes.Count > 0)
                    {
                        _logger.LogWarning(
                            "Initiating rollback of {Count} previously deployed nodes due to failure of {NodeName}",
                            successfullyDeployedNodes.Count, node.Name);

                        foreach (var (rolledBackAppId, rolledBackServices) in successfullyDeployedNodes)
                        {
                            // Tear down the container
                            var teardownResult = await orchestrator.TeardownServiceAsync(
                                rolledBackAppId,
                                false,
                                cancellationToken);

                            if (teardownResult.Success)
                            {
                                _logger.LogInformation(
                                    "Rolled back deployment of {AppId}",
                                    rolledBackAppId);
                            }
                            else
                            {
                                _logger.LogError(
                                    "Failed to rollback deployment of {AppId}: {Message}",
                                    rolledBackAppId, teardownResult.Message);
                            }

                            // Remove service routings that were set
                            foreach (var serviceName in rolledBackServices)
                            {
                                await _healthMonitor.RestoreServiceRoutingToDefaultAsync(serviceName);
                            }
                        }

                        // Clear tracking - rollback complete
                        successfullyDeployedNodes.Clear();
                        deployedServices.Clear();
                        expectedAppIds.Clear();

                        failedServices.Add("Deployment rolled back due to partial failure");
                    }

                    // Stop processing further nodes after rollback
                    break;
                }
            }

            // Wait for heartbeats from deployed containers before declaring success
            // This ensures new containers are fully operational (mesh connected, plugins loaded)
            // Skip in dryRun mode since no containers are actually deployed
            if (expectedAppIds.Count > 0 && failedServices.Count == 0 && !body.DryRun)
            {
                _logger.LogInformation(
                    "Waiting for heartbeats from {Count} deployed app-ids: [{AppIds}]",
                    expectedAppIds.Count, string.Join(", ", expectedAppIds));

                var heartbeatTimeout = TimeSpan.FromSeconds(_configuration.HeartbeatTimeoutSeconds);
                var pollInterval = TimeSpan.FromSeconds(_configuration.ContainerStatusPollIntervalSeconds);
                var heartbeatWaitStart = DateTime.UtcNow;
                var receivedHeartbeats = new HashSet<string>();

                while (DateTime.UtcNow - heartbeatWaitStart < heartbeatTimeout)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // Poll Redis for heartbeats from expected app-ids
                    var heartbeats = await _stateManager.GetServiceHeartbeatsAsync();

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

                // Check for timeout - treat as deployment failure and rollback
                var missingHeartbeats = expectedAppIds.Except(receivedHeartbeats).ToList();
                if (missingHeartbeats.Count > 0)
                {
                    var elapsed = DateTime.UtcNow - heartbeatWaitStart;
                    failedServices.Add($"Heartbeat timeout ({elapsed.TotalSeconds:F0}s) - missing heartbeats from: [{string.Join(", ", missingHeartbeats)}]");
                    _logger.LogWarning(
                        "Deployment heartbeat timeout after {Elapsed}s - missing heartbeats from: [{AppIds}]",
                        elapsed.TotalSeconds, string.Join(", ", missingHeartbeats));

                    // ROLLBACK: Tear down all deployed nodes since deployment is incomplete
                    if (successfullyDeployedNodes.Count > 0)
                    {
                        _logger.LogWarning(
                            "Initiating rollback of {Count} deployed nodes due to heartbeat timeout",
                            successfullyDeployedNodes.Count);

                        foreach (var (rolledBackAppId, rolledBackServices) in successfullyDeployedNodes)
                        {
                            var teardownResult = await orchestrator.TeardownServiceAsync(
                                rolledBackAppId,
                                false,
                                cancellationToken);

                            if (teardownResult.Success)
                            {
                                _logger.LogInformation("Rolled back deployment of {AppId}", rolledBackAppId);
                            }
                            else
                            {
                                _logger.LogError(
                                    "Failed to rollback deployment of {AppId}: {Message}",
                                    rolledBackAppId, teardownResult.Message);
                            }

                            foreach (var serviceName in rolledBackServices)
                            {
                                await _healthMonitor.RestoreServiceRoutingToDefaultAsync(serviceName);
                            }
                        }

                        successfullyDeployedNodes.Clear();
                        deployedServices.Clear();
                        failedServices.Add("Deployment rolled back due to heartbeat timeout");
                    }
                }
            }

            var duration = DateTime.UtcNow - startTime;
            var success = failedServices.Count == 0;

            // Save configuration version on successful deployment (skip in dryRun mode)
            if (success && !body.DryRun)
            {
                // Get current configuration to store as previous state (for rollback)
                var previousConfig = await _stateManager.GetCurrentConfigurationAsync();

                // Strip nested PreviousDeploymentState to keep only one level deep
                if (previousConfig?.PreviousDeploymentState != null)
                {
                    previousConfig.PreviousDeploymentState = null;
                }

                var deploymentConfig = new DeploymentConfiguration
                {
                    DeploymentId = deploymentId,
                    PresetName = body.Preset,
                    Description = $"Deployment {deploymentId} via {orchestrator.BackendType}",
                    PreviousDeploymentState = previousConfig
                };

                // Build service configuration from deployed nodes
                foreach (var node in nodesToDeploy)
                {
                    foreach (var serviceName in node.Services)
                    {
                        deploymentConfig.Services[serviceName] = new ServiceDeploymentConfig
                        {
                            Enabled = true,
                            AppId = node.AppId ?? node.Name,
                            Replicas = 1
                        };
                    }
                }

                // Store environment variables (without sensitive values)
                if (body.Environment != null)
                {
                    foreach (var kvp in body.Environment)
                    {
                        // Skip sensitive keys
                        if (!kvp.Key.Contains("PASSWORD", StringComparison.OrdinalIgnoreCase) &&
                            !kvp.Key.Contains("SECRET", StringComparison.OrdinalIgnoreCase) &&
                            !kvp.Key.Contains("KEY", StringComparison.OrdinalIgnoreCase))
                        {
                            deploymentConfig.EnvironmentVariables[kvp.Key] = kvp.Value;
                        }
                    }
                }

                var configVersion = await _stateManager.SaveConfigurationVersionAsync(deploymentConfig);

                // Update in-memory cache for awareness mode
                deploymentConfig.Version = configVersion;
                _lastKnownDeployment = deploymentConfig;

                _logger.LogInformation(
                    "Saved deployment configuration version {Version} for deployment {DeploymentId}",
                    configVersion, deploymentId);
            }

            var response = new DeployResponse
            {
                Success = success,
                DeploymentId = deploymentId,
                Backend = orchestrator.BackendType,
                Duration = $"{duration.TotalSeconds:F1}s",
                Message = body.DryRun
                    ? $"[DryRun] Would deploy {deployedServices.Count} node(s)"
                    : success
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
            await PublishErrorEventAsync("Deploy", ex.GetType().Name, ex.Message, details: new { Preset = body.Preset, Backend = body.Backend });

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
    /// Gets current service-to-app-id routing mappings.
    /// This is the authoritative source of truth for how services are routed in the current deployment.
    /// </summary>
    public async Task<(StatusCodes, ServiceRoutingResponse?)> GetServiceRoutingAsync(
        GetServiceRoutingRequest body,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Executing GetServiceRouting operation");

        try
        {
            // Ensure we have the last deployment state loaded for DeploymentId
            await EnsureLastDeploymentLoadedAsync();

            // Get service-to-app-id mappings from Redis
            // These are populated by DeployAsync when deploying presets with split topologies
            var serviceRoutings = await _stateManager.GetServiceRoutingsAsync();

            // Convert to simple string -> string mappings (service -> appId)
            // CRITICAL: Exclude infrastructure services (state, messaging, mesh) - these must always be local
            var mappings = new Dictionary<string, string>();
            foreach (var kvp in serviceRoutings)
            {
                // Never include infrastructure services - they must be handled locally
                if (IsInfrastructureService(kvp.Key))
                {
                    continue;
                }

                // Apply filter if specified
                if (string.IsNullOrEmpty(body.ServiceFilter) ||
                    kvp.Key.StartsWith(body.ServiceFilter, StringComparison.OrdinalIgnoreCase))
                {
                    mappings[kvp.Key] = kvp.Value.AppId;
                }
            }

            // In development/monolith mode, all services route to "bannou"
            if (mappings.Count == 0)
            {
                _logger.LogDebug("No custom service mappings in Redis - using default 'bannou' for all services");
            }

            var response = new ServiceRoutingResponse
            {
                Mappings = mappings,
                DefaultAppId = AppConstants.DEFAULT_APP_NAME,
                GeneratedAt = DateTimeOffset.UtcNow,
                TotalServices = mappings.Count,
                DeploymentId = _lastKnownDeployment?.DeploymentId
            };

            _logger.LogInformation("Returning {Count} service routing mappings from Redis", response.TotalServices);
            return (StatusCodes.OK, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving service routing mappings");
            await PublishErrorEventAsync("GetServiceRouting", ex.GetType().Name, ex.Message, dependency: "redis");
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
                        MeshEnabled = true,
                        AppId = g.Key
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
            await PublishErrorEventAsync("GetStatus", ex.GetType().Name, ex.Message);
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
                "DESTRUCTIVE OPERATION: includeInfrastructure=true will remove Redis, RabbitMQ, MySQL, etc. " +
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

            return (StatusCodes.OK, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing Teardown operation");
            await PublishErrorEventAsync("Teardown", ex.GetType().Name, ex.Message);

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

                    // Remove service routing - ServiceHealthMonitor will publish FullServiceMappingsEvent
                    await _healthMonitor.RestoreServiceRoutingToDefaultAsync(serviceName);

                    _logger.LogInformation(
                        "Service {AppName} torn down, routing removed",
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
    /// Implementation of Clean operation.
    /// Cleans up unused resources (images, volumes, networks) using the container orchestrator.
    /// </summary>
    public async Task<(StatusCodes, CleanResponse?)> CleanAsync(CleanRequest body, CancellationToken cancellationToken)
    {
        var targets = body.Targets?.ToList() ?? new List<CleanTarget>();
        var force = body.Force;

        _logger.LogInformation(
            "Executing Clean operation: targets={Targets}, force={Force}",
            string.Join(",", targets), force);

        try
        {
            var orchestrator = await GetOrchestratorAsync(cancellationToken);

            // Track cleanup results
            int removedContainers = 0;
            int removedNetworks = 0;
            int removedVolumes = 0;
            int removedImages = 0;
            long reclaimedBytes = 0;
            var cleanedItems = new List<string>();

            // Determine which targets to clean
            var cleanAll = targets.Contains(CleanTarget.All);
            var cleanContainers = cleanAll || targets.Contains(CleanTarget.Containers);
            var cleanNetworks = cleanAll || targets.Contains(CleanTarget.Networks);
            var cleanVolumes = cleanAll || targets.Contains(CleanTarget.Volumes);
            var cleanImages = cleanAll || targets.Contains(CleanTarget.Images);

            // Clean stopped containers
            if (cleanContainers)
            {
                _logger.LogInformation("Cleaning stopped containers...");
                var containers = await orchestrator.ListContainersAsync(cancellationToken);
                var stoppedContainers = containers.Where(c =>
                    c.Status == ContainerStatusStatus.Stopped).ToList();

                foreach (var container in stoppedContainers)
                {
                    if (!force && !IsOrphanedContainer(container))
                    {
                        _logger.LogDebug("Skipping non-orphaned container {AppName}", container.AppName);
                        continue;
                    }

                    try
                    {
                        var result = await orchestrator.TeardownServiceAsync(container.AppName, removeVolumes: false, cancellationToken);
                        if (result.Success)
                        {
                            removedContainers++;
                            cleanedItems.Add($"container:{container.AppName}");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to remove container {AppName}", container.AppName);
                    }
                }
            }

            // Note: Network, volume, and image pruning would require extending IContainerOrchestrator
            // or direct Docker SDK access. Track unsupported operations to report accurately.
            var unsupportedOperations = new List<string>();

            if (cleanNetworks)
            {
                _logger.LogWarning("Network pruning requested but not yet implemented");
                unsupportedOperations.Add("networks");
            }

            if (cleanVolumes)
            {
                _logger.LogWarning("Volume pruning requested but not yet implemented");
                unsupportedOperations.Add("volumes");
            }

            if (cleanImages)
            {
                _logger.LogWarning("Image pruning requested but not yet implemented");
                unsupportedOperations.Add("images");
            }

            // Build response message
            var messageBuilder = new System.Text.StringBuilder();
            if (cleanedItems.Count > 0)
            {
                messageBuilder.Append($"Cleaned: {string.Join(", ", cleanedItems)}");
            }
            else if (cleanContainers)
            {
                messageBuilder.Append("No orphaned containers found");
            }

            if (unsupportedOperations.Count > 0)
            {
                if (messageBuilder.Length > 0)
                    messageBuilder.Append(". ");
                messageBuilder.Append($"Unsupported operations skipped: {string.Join(", ", unsupportedOperations)}");
            }

            if (messageBuilder.Length == 0)
            {
                messageBuilder.Append("No cleanup targets specified");
            }

            // Success is true only if all requested operations were attempted
            // (even if they found nothing to clean)
            var allOperationsSupported = !unsupportedOperations.Any(op =>
                (op == "networks" && cleanNetworks) ||
                (op == "volumes" && cleanVolumes) ||
                (op == "images" && cleanImages));

            var cleanSuccess = unsupportedOperations.Count == 0 || cleanContainers;
            var response = new CleanResponse
            {
                Success = cleanSuccess,
                ReclaimedSpaceMb = (int)(reclaimedBytes / (1024 * 1024)),
                RemovedContainers = removedContainers,
                RemovedNetworks = removedNetworks,
                RemovedVolumes = removedVolumes,
                RemovedImages = removedImages,
                Message = messageBuilder.ToString()
            };

            _logger.LogInformation(
                "Clean operation completed: containers={Containers}, networks={Networks}, volumes={Volumes}",
                removedContainers, removedNetworks, removedVolumes);

            return (StatusCodes.OK, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing Clean operation");
            await PublishErrorEventAsync("Clean", ex.GetType().Name, ex.Message);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Determines if an environment variable should be forwarded to deployed containers.
    /// Uses a whitelist approach based on discovered plugin prefixes.
    /// </summary>
    /// <remarks>
    /// Only forwards environment variables with prefixes matching:
    /// - Core infrastructure: BANNOU_, ACCOUNT_, HEARTBEAT_, SERVICES_, OPENRESTY_, ASPNETCORE_ENVIRONMENT
    /// - Plugin prefixes: AUTH_, CONNECT_, STATE_, MESH_, etc. (derived from discovered plugins)
    ///
    /// EXCLUDES: *_SERVICE_ENABLED and *_SERVICE_DISABLED flags - these are controlled
    /// entirely by the deployment logic based on the preset's services list.
    /// </remarks>
    private static bool IsAllowedEnvironmentVariable(string key)
    {
        // NEVER forward service enable/disable flags - these are set by deployment logic
        // based on the preset's services list, not inherited from orchestrator
        if (key.EndsWith("_SERVICE_ENABLED", StringComparison.OrdinalIgnoreCase) ||
            key.EndsWith("_SERVICE_DISABLED", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // NEVER forward BANNOU_APP_ID - each deployed container gets its own app-id
        // set explicitly by the deployment logic
        if (key.Equals("BANNOU_APP_ID", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var validPrefixes = PluginLoader.ValidEnvironmentPrefixes;

        // Check if the key starts with any valid prefix
        foreach (var prefix in validPrefixes)
        {
            if (key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Determines if a container is orphaned (no longer part of active deployment).
    /// </summary>
    /// <remarks>
    /// Uses the 'bannou.orchestrator-managed' label to identify containers deployed
    /// by the orchestrator. Only containers with this label AND stopped status AND
    /// stopped for more than 24 hours are considered orphaned.
    ///
    /// This approach is more reliable than name-based parsing and ensures we only
    /// clean up containers the orchestrator actually created.
    /// </remarks>
    private static bool IsOrphanedContainer(ContainerStatus container)
    {
        // Container must be stopped to be considered orphaned
        if (container.Status != ContainerStatusStatus.Stopped)
        {
            return false;
        }

        // Only consider containers that were deployed by the orchestrator
        // Check for the orchestrator-managed label
        if (container.Labels == null ||
            !container.Labels.TryGetValue(DockerComposeOrchestrator.BANNOU_ORCHESTRATOR_MANAGED_LABEL, out var labelValue) ||
            labelValue != "true")
        {
            return false;
        }

        // Use Timestamp as best-effort proxy for stop time
        // Conservative 24-hour threshold to avoid removing recently-stopped containers
        // that might be part of an active deployment restart cycle
        var timeSinceLastUpdate = DateTimeOffset.UtcNow - container.Timestamp;
        return timeSinceLastUpdate.TotalHours > 24;
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

            var appName = service ?? container ?? AppConstants.DEFAULT_APP_NAME;
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
            await PublishErrorEventAsync("GetLogs", ex.GetType().Name, ex.Message, details: new { Service = body.Service });
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
                _logger.LogWarning("No topology changes specified");
                return (StatusCodes.BadRequest, null);
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
                                    : new Dictionary<string, string>();

                                // CRITICAL: Disable all services by default, then enable only the specified ones
                                if (!nodeEnvironment.ContainsKey("SERVICES_ENABLED"))
                                {
                                    nodeEnvironment["SERVICES_ENABLED"] = "false";
                                }

                                // Enable each service listed in the change
                                foreach (var svc in change.Services)
                                {
                                    var envKey = $"{svc.ToUpperInvariant().Replace("-", "_")}_SERVICE_ENABLED";
                                    if (!nodeEnvironment.ContainsKey(envKey))
                                    {
                                        nodeEnvironment[envKey] = "true";
                                    }
                                }

                                var addNodeSucceeded = 0;
                                var addNodeFailed = 0;
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
                                        // Set service routing - ServiceHealthMonitor will publish FullServiceMappingsEvent
                                        await _healthMonitor.SetServiceRoutingAsync(serviceName, appId);
                                        addNodeSucceeded++;
                                    }
                                    else
                                    {
                                        warnings.Add($"Failed to deploy {serviceName} on {change.NodeName}: {deployResult.Message}");
                                        addNodeFailed++;
                                    }
                                }
                                // Only report success if at least one deployment succeeded AND none failed
                                appliedChange.Success = addNodeSucceeded > 0 && addNodeFailed == 0;
                                if (addNodeFailed > 0 && addNodeSucceeded > 0)
                                {
                                    appliedChange.Error = $"Partial failure: {addNodeSucceeded} deployed, {addNodeFailed} failed";
                                }
                                else if (addNodeFailed > 0)
                                {
                                    appliedChange.Error = $"All {addNodeFailed} deployments failed";
                                }
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
                                var removeNodeSucceeded = 0;
                                var removeNodeFailed = 0;
                                foreach (var serviceName in change.Services)
                                {
                                    var appId = $"bannou-{serviceName}-{change.NodeName}";
                                    var teardownResult = await orchestrator.TeardownServiceAsync(
                                        appId,
                                        false,
                                        cancellationToken);

                                    if (teardownResult.Success)
                                    {
                                        // Remove routing - ServiceHealthMonitor will publish FullServiceMappingsEvent
                                        await _healthMonitor.RestoreServiceRoutingToDefaultAsync(serviceName);
                                        removeNodeSucceeded++;
                                    }
                                    else
                                    {
                                        warnings.Add($"Failed to teardown {serviceName} on {change.NodeName}: {teardownResult.Message}");
                                        removeNodeFailed++;
                                    }
                                }
                                // Only report success if at least one teardown succeeded AND none failed
                                appliedChange.Success = removeNodeSucceeded > 0 && removeNodeFailed == 0;
                                if (removeNodeFailed > 0 && removeNodeSucceeded > 0)
                                {
                                    appliedChange.Error = $"Partial failure: {removeNodeSucceeded} removed, {removeNodeFailed} failed";
                                }
                                else if (removeNodeFailed > 0)
                                {
                                    appliedChange.Error = $"All {removeNodeFailed} teardowns failed";
                                }
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
                                var moveSucceeded = 0;
                                var moveFailed = 0;
                                foreach (var serviceName in change.Services)
                                {
                                    var newAppId = $"bannou-{serviceName}-{change.NodeName}";

                                    try
                                    {
                                        // Update routing - ServiceHealthMonitor will publish FullServiceMappingsEvent
                                        await _healthMonitor.SetServiceRoutingAsync(serviceName, newAppId);
                                        moveSucceeded++;
                                    }
                                    catch (Exception ex)
                                    {
                                        warnings.Add($"Failed to move {serviceName} to {change.NodeName}: {ex.Message}");
                                        moveFailed++;
                                    }
                                }
                                appliedChange.Success = moveSucceeded > 0 && moveFailed == 0;
                                if (moveFailed > 0 && moveSucceeded > 0)
                                {
                                    appliedChange.Error = $"Partial failure: {moveSucceeded} moved, {moveFailed} failed";
                                }
                                else if (moveFailed > 0)
                                {
                                    appliedChange.Error = $"All {moveFailed} service moves failed";
                                }
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
                                var scaleSucceeded = 0;
                                var scaleFailed = 0;
                                foreach (var serviceName in change.Services)
                                {
                                    var appId = !string.IsNullOrEmpty(change.NodeName)
                                        ? $"bannou-{serviceName}-{change.NodeName}"
                                        : $"bannou-{serviceName}";

                                    var scaleResult = await orchestrator.ScaleServiceAsync(
                                        appId,
                                        change.Replicas,
                                        cancellationToken);

                                    if (scaleResult.Success)
                                    {
                                        scaleSucceeded++;
                                    }
                                    else
                                    {
                                        warnings.Add($"Failed to scale {serviceName}: {scaleResult.Message}");
                                        scaleFailed++;
                                    }
                                }
                                appliedChange.Success = scaleSucceeded > 0 && scaleFailed == 0;
                                if (scaleFailed > 0 && scaleSucceeded > 0)
                                {
                                    appliedChange.Error = $"Partial failure: {scaleSucceeded} scaled, {scaleFailed} failed";
                                }
                                else if (scaleFailed > 0)
                                {
                                    appliedChange.Error = $"All {scaleFailed} scale operations failed";
                                }
                            }
                            else
                            {
                                appliedChange.Error = "Services list is required for scale action";
                            }
                            break;

                        case TopologyChangeAction.UpdateEnv:
                            // Update environment variables for services by redeploying with new env vars.
                            // DeployServiceAsync handles container cleanup and recreation automatically.
                            if (change.Services != null && change.Environment != null)
                            {
                                var envDict = new Dictionary<string, string>(change.Environment);
                                var allEnvDeploysSucceeded = true;

                                foreach (var serviceName in change.Services)
                                {
                                    // Construct appId the same way as other actions
                                    var appId = !string.IsNullOrEmpty(change.NodeName)
                                        ? $"bannou-{serviceName}-{change.NodeName}"
                                        : $"bannou-{serviceName}";

                                    _logger.LogInformation(
                                        "Updating environment for {ServiceName} (appId: {AppId}) with {EnvCount} variables",
                                        serviceName, appId, envDict.Count);

                                    // DeployServiceAsync cleans up existing container and redeploys with new env vars
                                    var deployResult = await orchestrator.DeployServiceAsync(
                                        serviceName,
                                        appId,
                                        envDict,
                                        cancellationToken);

                                    if (!deployResult.Success)
                                    {
                                        warnings.Add($"Failed to update environment for {serviceName}: {deployResult.Message}");
                                        allEnvDeploysSucceeded = false;
                                    }
                                    else
                                    {
                                        _logger.LogInformation(
                                            "Successfully updated environment for {ServiceName} (container: {ContainerId})",
                                            serviceName, deployResult.ContainerId ?? "unknown");
                                    }
                                }

                                appliedChange.Success = allEnvDeploysSucceeded;
                                if (!allEnvDeploysSucceeded)
                                {
                                    appliedChange.Error = "Some services failed to update environment - check warnings for details";
                                }
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
                    _ = PublishErrorEventAsync("UpdateTopology", ex.GetType().Name, ex.Message, details: new { Action = change.Action, NodeName = change.NodeName });
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

            return (StatusCodes.OK, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing UpdateTopology operation");
            await PublishErrorEventAsync("UpdateTopology", ex.GetType().Name, ex.Message);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Implementation of RequestContainerRestart operation.
    /// Restarts a specific container by Bannou app name.
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
            await PublishErrorEventAsync("RequestContainerRestart", ex.GetType().Name, ex.Message, details: new { AppName = body.AppName });
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Implementation of GetContainerStatus operation.
    /// Gets the status of a specific container by Bannou app name.
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
            await PublishErrorEventAsync("GetContainerStatus", ex.GetType().Name, ex.Message, details: new { AppName = body.AppName });
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Implementation of RollbackConfiguration operation.
    /// Rolls back to a previous configuration version.
    /// </summary>
    public async Task<(StatusCodes, ConfigRollbackResponse?)> RollbackConfigurationAsync(ConfigRollbackRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Executing RollbackConfiguration operation: reason={Reason}", body.Reason);

        try
        {
            // Get current configuration
            var currentConfig = await _stateManager.GetCurrentConfigurationAsync();
            var currentVersion = await _stateManager.GetConfigVersionAsync();

            if (currentVersion <= 1)
            {
                _logger.LogWarning("Cannot rollback - no previous configuration version available (current version: {Version})",
                    currentVersion);
                return (StatusCodes.BadRequest, null);
            }

            // Get the previous version (currentVersion - 1)
            var targetVersion = currentVersion - 1;
            var previousConfig = await _stateManager.GetConfigurationVersionAsync(targetVersion);

            if (previousConfig == null)
            {
                _logger.LogWarning("Previous configuration version {Version} not found in history (may have expired)", targetVersion);
                return (StatusCodes.NotFound, null);
            }

            // Perform the rollback
            var success = await _stateManager.RestoreConfigurationVersionAsync(targetVersion);

            if (!success)
            {
                _logger.LogError("Failed to restore configuration version {Version}", targetVersion);
                await PublishErrorEventAsync("RollbackConfiguration", "restore_failed", $"Failed to restore configuration version {targetVersion}", dependency: "redis");
                return (StatusCodes.InternalServerError, null);
            }

            // Get the new version (which is currentVersion + 1 after rollback)
            var newVersion = await _stateManager.GetConfigVersionAsync();

            // Determine what changed
            var changedKeys = new List<string>();
            if (currentConfig != null && previousConfig.Services != null)
            {
                // Find services that were different
                foreach (var service in previousConfig.Services)
                {
                    if (!currentConfig.Services.TryGetValue(service.Key, out var currentService) ||
                        currentService.Enabled != service.Value.Enabled)
                    {
                        changedKeys.Add($"services.{service.Key}.enabled");
                    }
                }

                // Find env vars that changed
                foreach (var env in previousConfig.EnvironmentVariables)
                {
                    if (!currentConfig.EnvironmentVariables.TryGetValue(env.Key, out var currentValue) ||
                        currentValue != env.Value)
                    {
                        changedKeys.Add($"env.{env.Key}");
                    }
                }
            }

            _logger.LogInformation(
                "Configuration rolled back from version {OldVersion} to version {NewVersion} (restored from v{RestoredVersion}). Reason: {Reason}. Changed keys: {ChangedCount}",
                currentVersion, newVersion, targetVersion, body.Reason, changedKeys.Count);

            return (StatusCodes.OK, new ConfigRollbackResponse
            {
                PreviousVersion = currentVersion,
                CurrentVersion = newVersion,
                ChangedKeys = changedKeys,
                Message = $"Successfully rolled back to configuration version {targetVersion}. Reason: {body.Reason}"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing RollbackConfiguration operation");
            await PublishErrorEventAsync("RollbackConfiguration", ex.GetType().Name, ex.Message);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Implementation of GetConfigVersion operation.
    /// Gets the current configuration version.
    /// </summary>
    public async Task<(StatusCodes, ConfigVersionResponse?)> GetConfigVersionAsync(GetConfigVersionRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Executing GetConfigVersion operation");

        try
        {
            // Get current version and configuration from Redis
            var currentVersion = await _stateManager.GetConfigVersionAsync();
            var currentConfig = await _stateManager.GetCurrentConfigurationAsync();

            // Check if previous version exists
            var hasPreviousConfig = currentVersion > 1 &&
                await _stateManager.GetConfigurationVersionAsync(currentVersion - 1) != null;

            // Collect key prefixes from current config
            var keyPrefixes = new List<string>();
            if (currentConfig != null)
            {
                if (currentConfig.Services.Any())
                    keyPrefixes.Add("services");
                if (currentConfig.EnvironmentVariables.Any())
                {
                    // Extract unique prefixes from env vars (e.g., "AUTH", "ACCOUNT")
                    var envPrefixes = currentConfig.EnvironmentVariables.Keys
                        .Select(k => k.Split('_').FirstOrDefault() ?? k)
                        .Distinct()
                        .Take(10); // Limit to avoid excessive data
                    keyPrefixes.AddRange(envPrefixes.Select(p => $"env.{p}"));
                }
            }

            var response = new ConfigVersionResponse
            {
                Version = currentVersion,
                Timestamp = currentConfig?.Timestamp ?? DateTimeOffset.UtcNow,
                HasPreviousConfig = hasPreviousConfig,
                KeyCount = (currentConfig?.Services.Count ?? 0) + (currentConfig?.EnvironmentVariables.Count ?? 0),
                KeyPrefixes = keyPrefixes
            };

            return (StatusCodes.OK, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing GetConfigVersion operation");
            await PublishErrorEventAsync("GetConfigVersion", ex.GetType().Name, ex.Message, dependency: "redis");
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Registers service permissions for the Orchestrator API endpoints.
    /// Uses the generated OrchestratorPermissionRegistration to publish to the correct pub/sub topic.
    /// All orchestrator operations require admin role access.
    /// When SecureWebsocket is enabled (default), publishes a blank registration to make
    /// orchestrator inaccessible via WebSocket - only service-to-service calls work.
    /// </summary>
    public async Task RegisterServicePermissionsAsync()
    {
        _logger.LogInformation("Registering Orchestrator service permissions... (starting)");
        try
        {
            if (_configuration.SecureWebsocket)
            {
                // Secure mode: publish blank registration to make orchestrator inaccessible via WebSocket
                // This overwrites any previous permissions, ensuring the service cannot be called by clients
                var blankRegistration = new ServiceRegistrationEvent
                {
                    EventId = Guid.NewGuid(),
                    Timestamp = DateTimeOffset.UtcNow,
                    ServiceId = Guid.Parse(Program.ServiceGUID),
                    ServiceName = OrchestratorPermissionRegistration.ServiceId,
                    Version = OrchestratorPermissionRegistration.ServiceVersion,
                    AppId = Program.Configuration.EffectiveAppId,
                    Endpoints = new List<ServiceEndpoint>() // Empty = no WebSocket access
                };

                var success = await _messageBus.TryPublishAsync("permission.service-registered", blankRegistration);
                if (success)
                {
                    _logger.LogInformation(
                        "Orchestrator running in secure mode - published blank registration (no WebSocket access)");
                }
                else
                {
                    _logger.LogWarning("Failed to publish blank registration for secure mode (will be retried)");
                }
            }
            else
            {
                // Non-secure mode: register all endpoints for admin access (testing environments)
                await OrchestratorPermissionRegistration.RegisterViaEventAsync(_messageBus, _logger);
                _logger.LogInformation("Orchestrator service permissions registered via event (complete)");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to register Orchestrator service permissions");
            await PublishErrorEventAsync("RegisterServicePermissions", ex.GetType().Name, ex.Message, dependency: "permission");
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

    /// <summary>
    /// Publishes an error event for unexpected/internal failures.
    /// Does NOT publish for validation errors or expected failure cases.
    /// </summary>
    private async Task PublishErrorEventAsync(
        string operation,
        string errorType,
        string message,
        string? dependency = null,
        object? details = null)
    {
        await _messageBus.TryPublishErrorAsync(
            serviceName: "orchestrator",
            operation: operation,
            errorType: errorType,
            message: message,
            dependency: dependency,
            details: details);
    }

    /// <summary>
    /// Checks if the deploy request is a "reset to default topology" request.
    /// Returns true if preset is "default", "bannou", empty/null AND no direct topology is specified.
    /// </summary>
    private static bool IsResetToDefaultRequest(string? preset, ServiceTopology? topology)
    {
        // If a direct topology is specified, it's not a reset request
        if (topology?.Nodes != null && topology.Nodes.Count > 0)
        {
            return false;
        }

        // Check if preset indicates reset to default
        if (string.IsNullOrWhiteSpace(preset))
        {
            return true; // Empty/null preset with no topology = reset to default
        }

        var normalizedPreset = preset.Trim().ToLowerInvariant();
        return normalizedPreset == "default" || normalizedPreset == AppConstants.DEFAULT_APP_NAME;
    }

    /// <summary>
    /// Resets to default topology by tearing down containers tracked in the current deployment configuration
    /// and resetting service mappings to all point to "bannou".
    /// Uses explicit configuration tracking instead of inferring from container names.
    /// </summary>
    private async Task<(bool Success, string Message, List<string> TornDownServices)> ResetToDefaultTopologyAsync(
        IContainerOrchestrator orchestrator,
        string deploymentId,
        CancellationToken cancellationToken)
    {
        var tornDownServices = new List<string>();

        try
        {
            // Get the current deployment configuration - this tells us exactly what we deployed
            var currentConfig = await _stateManager.GetCurrentConfigurationAsync();

            if (currentConfig == null || currentConfig.Services.Count == 0)
            {
                // No configuration tracked or empty - already at default topology
                _logger.LogInformation("Reset to default: No deployment configuration found, already at default topology");

                // Still reset mappings to ensure clean state
                await _healthMonitor.ResetAllMappingsToDefaultAsync();

                return (true, "Already at default topology - no tracked deployments, mappings reset", tornDownServices);
            }

            // Extract unique AppIds (container names) from the tracked deployment
            // Exclude "bannou" as that's the default monolith
            var deployedAppIds = currentConfig.Services.Values
                .Where(s => s.Enabled && !string.IsNullOrEmpty(s.AppId))
                .Select(s => s.AppId!)
                .Where(appId => !appId.Equals("bannou", StringComparison.OrdinalIgnoreCase))
                .Distinct()
                .ToList();

            _logger.LogInformation(
                "Reset to default: Found {Count} tracked deployments to tear down: [{AppIds}]",
                deployedAppIds.Count,
                string.Join(", ", deployedAppIds));

            if (deployedAppIds.Count == 0)
            {
                // All services were deployed to "bannou" - nothing to tear down
                await _healthMonitor.ResetAllMappingsToDefaultAsync();
                await InvalidateOpenRestryRoutingCacheAsync();
                await _stateManager.ClearCurrentConfigurationAsync();

                return (true, "Already at default topology - all services on 'bannou', configuration cleared", tornDownServices);
            }

            // CRITICAL: Reset service mappings FIRST, before tearing down containers.
            // This ensures routing proxies (OpenResty, lib-mesh) get updated routes
            // before the old containers become unavailable.
            await _healthMonitor.ResetAllMappingsToDefaultAsync();
            await InvalidateOpenRestryRoutingCacheAsync();

            // Now tear down each tracked deployment (routes already point to default)
            foreach (var appId in deployedAppIds)
            {
                try
                {
                    var teardownResult = await orchestrator.TeardownServiceAsync(
                        appId,
                        removeVolumes: false,
                        cancellationToken);

                    if (teardownResult.Success)
                    {
                        tornDownServices.Add(appId);
                        _logger.LogInformation("Torn down tracked container {AppId}", appId);
                    }
                    else
                    {
                        _logger.LogWarning("Failed to tear down tracked container {AppId}: {Message}",
                            appId, teardownResult.Message);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error tearing down tracked container {AppId}", appId);
                }
            }

            // Clear the deployment configuration (saves empty config as new version for audit trail)
            await _stateManager.ClearCurrentConfigurationAsync();

            var message = tornDownServices.Count > 0
                ? $"Reset to default topology: torn down {tornDownServices.Count} tracked container(s), all services now route to 'bannou'"
                : "Reset to default topology: no containers torn down (may have already been removed), configuration cleared";

            return (true, message, tornDownServices);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error resetting to default topology");
            return (false, $"Failed to reset to default topology: {ex.Message}", tornDownServices);
        }
    }

    #region Processing Pool Management

    // Redis key patterns for processing pool
    private const string POOL_INSTANCES_KEY = "processing-pool:{0}:instances";
    private const string POOL_AVAILABLE_KEY = "processing-pool:{0}:available";
    private const string POOL_LEASES_KEY = "processing-pool:{0}:leases";
    private const string POOL_METRICS_KEY = "processing-pool:{0}:metrics";
    private const string POOL_CONFIG_KEY = "processing-pool:{0}:config";

    /// <summary>
    /// Acquires a processor from the pool for processing work.
    /// </summary>
    /// <param name="body">Request containing pool type and priority.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Processor information including app-id and lease.</returns>
    public async Task<(StatusCodes, AcquireProcessorResponse?)> AcquireProcessorAsync(
        AcquireProcessorRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {

            if (string.IsNullOrEmpty(body.PoolType))
            {
                _logger.LogWarning("AcquireProcessor: Missing pool_type");
                return (StatusCodes.BadRequest, null);
            }

            var availableKey = string.Format(POOL_AVAILABLE_KEY, body.PoolType);
            var leasesKey = string.Format(POOL_LEASES_KEY, body.PoolType);

            // Try to get an available processor from the pool
            var availableProcessors = await _stateManager.GetListAsync<ProcessorInstance>(availableKey);

            if (availableProcessors == null || availableProcessors.Count == 0)
            {
                _logger.LogInformation("AcquireProcessor: No available processors in pool {PoolType}", body.PoolType);

                // Return 503 Service Unavailable to indicate pool is busy
                return (StatusCodes.ServiceUnavailable, null);
            }

            // Pop the first available processor
            var processor = availableProcessors[0];
            availableProcessors.RemoveAt(0);
            await _stateManager.SetListAsync(availableKey, availableProcessors);

            // Create a lease for this processor
            var leaseId = Guid.NewGuid();
            var timeoutSeconds = body.TimeoutSeconds > 0 ? body.TimeoutSeconds : 300;
            var expiresAt = DateTimeOffset.UtcNow.AddSeconds(timeoutSeconds);

            var lease = new ProcessorLease
            {
                LeaseId = leaseId,
                ProcessorId = processor.ProcessorId,
                AppId = processor.AppId,
                PoolType = body.PoolType,
                AcquiredAt = DateTimeOffset.UtcNow,
                ExpiresAt = expiresAt,
                Priority = body.Priority,
                Metadata = body.Metadata
            };

            // Store the lease
            var leases = await _stateManager.GetHashAsync<ProcessorLease>(leasesKey) ?? new Dictionary<string, ProcessorLease>();
            leases[leaseId.ToString()] = lease;
            await _stateManager.SetHashAsync(leasesKey, leases);

            _logger.LogInformation(
                "AcquireProcessor: Acquired processor {ProcessorId} from pool {PoolType} with lease {LeaseId}, expires at {ExpiresAt}",
                processor.ProcessorId, body.PoolType, leaseId, expiresAt);

            return (StatusCodes.OK, new AcquireProcessorResponse
            {
                ProcessorId = processor.ProcessorId,
                AppId = processor.AppId,
                LeaseId = leaseId,
                ExpiresAt = expiresAt
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AcquireProcessor: Error acquiring processor from pool {PoolType}", body?.PoolType);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Releases a processor back to the pool.
    /// </summary>
    /// <param name="body">Request containing lease ID and completion status.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Release confirmation.</returns>
    public async Task<(StatusCodes, ReleaseProcessorResponse?)> ReleaseProcessorAsync(
        ReleaseProcessorRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {

            if (body.LeaseId == Guid.Empty)
            {
                _logger.LogWarning("ReleaseProcessor: Missing or invalid lease_id");
                return (StatusCodes.BadRequest, null);
            }

            // Find the lease across all pool types
            ProcessorLease? lease = null;
            string? poolType = null;

            // We need to search all pools for the lease
            var poolTypes = await GetKnownPoolTypesAsync();

            foreach (var pt in poolTypes)
            {
                var leasesKey = string.Format(POOL_LEASES_KEY, pt);
                var leases = await _stateManager.GetHashAsync<ProcessorLease>(leasesKey);

                if (leases != null && leases.TryGetValue(body.LeaseId.ToString(), out var foundLease))
                {
                    lease = foundLease;
                    poolType = pt;
                    break;
                }
            }

            if (lease == null || poolType == null)
            {
                _logger.LogWarning("ReleaseProcessor: Lease {LeaseId} not found", body.LeaseId);
                return (StatusCodes.NotFound, null);
            }

            // Remove the lease
            var leasesKeyToUpdate = string.Format(POOL_LEASES_KEY, poolType);
            var currentLeases = await _stateManager.GetHashAsync<ProcessorLease>(leasesKeyToUpdate) ?? new Dictionary<string, ProcessorLease>();
            currentLeases.Remove(body.LeaseId.ToString());
            await _stateManager.SetHashAsync(leasesKeyToUpdate, currentLeases);

            // Return processor to available pool
            var availableKey = string.Format(POOL_AVAILABLE_KEY, poolType);
            var availableProcessors = await _stateManager.GetListAsync<ProcessorInstance>(availableKey) ?? new List<ProcessorInstance>();

            availableProcessors.Add(new ProcessorInstance
            {
                ProcessorId = lease.ProcessorId,
                AppId = lease.AppId,
                PoolType = poolType,
                Status = ProcessorStatus.Available,
                LastUpdated = DateTimeOffset.UtcNow
            });

            await _stateManager.SetListAsync(availableKey, availableProcessors);

            // Update metrics
            await UpdatePoolMetricsAsync(poolType, body.Success);

            _logger.LogInformation(
                "ReleaseProcessor: Released processor {ProcessorId} back to pool {PoolType}, success={Success}",
                lease.ProcessorId, poolType, body.Success);

            return (StatusCodes.OK, new ReleaseProcessorResponse
            {
                Released = true,
                ProcessorId = lease.ProcessorId
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ReleaseProcessor: Error releasing processor with lease {LeaseId}", body?.LeaseId);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Gets the current status of a processing pool.
    /// </summary>
    /// <param name="body">Request containing pool type.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Pool status including instance counts and metrics.</returns>
    public async Task<(StatusCodes, PoolStatusResponse?)> GetPoolStatusAsync(
        GetPoolStatusRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {

            if (string.IsNullOrEmpty(body.PoolType))
            {
                _logger.LogWarning("GetPoolStatus: Missing pool_type");
                return (StatusCodes.BadRequest, null);
            }

            var instancesKey = string.Format(POOL_INSTANCES_KEY, body.PoolType);
            var availableKey = string.Format(POOL_AVAILABLE_KEY, body.PoolType);
            var leasesKey = string.Format(POOL_LEASES_KEY, body.PoolType);
            var configKey = string.Format(POOL_CONFIG_KEY, body.PoolType);
            var metricsKey = string.Format(POOL_METRICS_KEY, body.PoolType);

            var allInstances = await _stateManager.GetListAsync<ProcessorInstance>(instancesKey) ?? new List<ProcessorInstance>();
            var availableInstances = await _stateManager.GetListAsync<ProcessorInstance>(availableKey) ?? new List<ProcessorInstance>();
            var leases = await _stateManager.GetHashAsync<ProcessorLease>(leasesKey) ?? new Dictionary<string, ProcessorLease>();
            var config = await _stateManager.GetValueAsync<PoolConfiguration>(configKey);

            var response = new PoolStatusResponse
            {
                PoolType = body.PoolType,
                TotalInstances = allInstances.Count,
                AvailableInstances = availableInstances.Count,
                BusyInstances = leases.Count,
                QueueDepth = 0, // We don't have a queue yet
                MinInstances = config?.MinInstances ?? 1,
                MaxInstances = config?.MaxInstances ?? 5
            };

            // Include metrics if requested
            if (body.IncludeMetrics)
            {
                var metrics = await _stateManager.GetValueAsync<PoolMetricsData>(metricsKey);
                if (metrics != null)
                {
                    response.RecentMetrics = new PoolMetrics
                    {
                        JobsCompleted1h = metrics.JobsCompleted1h,
                        JobsFailed1h = metrics.JobsFailed1h,
                        AvgProcessingTimeMs = metrics.AvgProcessingTimeMs,
                        LastScaleEvent = metrics.LastScaleEvent
                    };
                }
            }

            return (StatusCodes.OK, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetPoolStatus: Error getting status for pool {PoolType}", body?.PoolType);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Scales a processing pool to a target number of instances.
    /// </summary>
    /// <param name="body">Request containing pool type and target instances.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Scaling result including previous and current instance counts.</returns>
    public async Task<(StatusCodes, ScalePoolResponse?)> ScalePoolAsync(
        ScalePoolRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {

            if (string.IsNullOrEmpty(body.PoolType))
            {
                _logger.LogWarning("ScalePool: Missing pool_type");
                return (StatusCodes.BadRequest, null);
            }

            if (body.TargetInstances < 0)
            {
                _logger.LogWarning("ScalePool: Invalid target_instances {Target}", body.TargetInstances);
                return (StatusCodes.BadRequest, null);
            }

            // Load pool configuration - required to know how to deploy workers
            var configKey = string.Format(POOL_CONFIG_KEY, body.PoolType);
            var poolConfig = await _stateManager.GetValueAsync<PoolConfiguration>(configKey);
            if (poolConfig == null)
            {
                _logger.LogWarning("ScalePool: No configuration found for pool {PoolType}", body.PoolType);
                return (StatusCodes.NotFound, null);
            }

            // Get container orchestrator backend
            var orchestrator = await GetOrchestratorAsync(cancellationToken);

            var instancesKey = string.Format(POOL_INSTANCES_KEY, body.PoolType);
            var availableKey = string.Format(POOL_AVAILABLE_KEY, body.PoolType);
            var leasesKey = string.Format(POOL_LEASES_KEY, body.PoolType);
            var metricsKey = string.Format(POOL_METRICS_KEY, body.PoolType);

            var currentInstances = await _stateManager.GetListAsync<ProcessorInstance>(instancesKey) ?? new List<ProcessorInstance>();
            var availableInstances = await _stateManager.GetListAsync<ProcessorInstance>(availableKey) ?? new List<ProcessorInstance>();
            var leases = await _stateManager.GetHashAsync<ProcessorLease>(leasesKey) ?? new Dictionary<string, ProcessorLease>();

            var previousCount = currentInstances.Count;
            var scaledUp = 0;
            var scaledDown = 0;
            var deployErrors = new List<string>();

            if (body.TargetInstances > previousCount)
            {
                // Scale up - deploy new worker containers
                var toSpawn = body.TargetInstances - previousCount;

                for (int i = 0; i < toSpawn; i++)
                {
                    var processorId = $"{body.PoolType}-{Guid.NewGuid():N}";
                    var appId = $"bannou-pool-{body.PoolType}-{processorId[^8..]}";

                    // Build environment for this pool worker
                    var workerEnv = new Dictionary<string, string>();

                    // Include base pool configuration environment
                    if (poolConfig.Environment != null)
                    {
                        foreach (var kvp in poolConfig.Environment)
                        {
                            workerEnv[kvp.Key] = kvp.Value;
                        }
                    }

                    // Set the unique identifiers for this worker
                    // BANNOU_APP_ID is the standard mesh routing identifier
                    workerEnv["BANNOU_APP_ID"] = appId;

                    // ActorPoolNodeWorker uses this for self-registration
                    workerEnv["ACTOR_POOL_NODE_ID"] = processorId;

                    // Service name defaults to "bannou" if not specified in pool config
                    var serviceName = poolConfig.ServiceName;
                    if (string.IsNullOrEmpty(serviceName))
                    {
                        serviceName = AppConstants.DEFAULT_APP_NAME;
                    }

                    // Deploy the worker container
                    _logger.LogInformation(
                        "ScalePool: Deploying worker {ProcessorId} with app-id {AppId} for pool {PoolType}",
                        processorId, appId, body.PoolType);

                    var deployResult = await orchestrator.DeployServiceAsync(
                        serviceName,
                        appId,
                        workerEnv,
                        cancellationToken);

                    if (deployResult.Success)
                    {
                        // Create instance record - initially pending until self-registration
                        var newInstance = new ProcessorInstance
                        {
                            ProcessorId = processorId,
                            AppId = appId,
                            PoolType = body.PoolType,
                            Status = ProcessorStatus.Pending, // Will become Available after self-registration
                            CreatedAt = DateTimeOffset.UtcNow,
                            LastUpdated = DateTimeOffset.UtcNow
                        };

                        currentInstances.Add(newInstance);
                        scaledUp++;

                        _logger.LogInformation(
                            "ScalePool: Deployed worker {ProcessorId} successfully",
                            processorId);
                    }
                    else
                    {
                        _logger.LogError(
                            "ScalePool: Failed to deploy worker {ProcessorId}: {Message}",
                            processorId, deployResult.Message);
                        deployErrors.Add($"{processorId}: {deployResult.Message}");
                    }
                }

                _logger.LogInformation(
                    "ScalePool: Scaled up pool {PoolType} by {Count} instances (target was {Target})",
                    body.PoolType, scaledUp, toSpawn);
            }
            else if (body.TargetInstances < previousCount)
            {
                // Scale down - teardown worker containers
                var toRemove = previousCount - body.TargetInstances;

                // Can only remove available instances (unless force)
                var maxRemovable = body.Force ? previousCount : availableInstances.Count;
                var actualToRemove = Math.Min(toRemove, maxRemovable);

                if (actualToRemove > 0)
                {
                    // Collect instances to remove (prefer available/idle ones first)
                    var instancesToRemove = new List<ProcessorInstance>();

                    // First take from available
                    for (int i = 0; i < Math.Min(actualToRemove, availableInstances.Count); i++)
                    {
                        instancesToRemove.Add(availableInstances[i]);
                    }

                    // If force and still need more, take from busy instances
                    if (body.Force && instancesToRemove.Count < actualToRemove)
                    {
                        var busyToRemove = actualToRemove - instancesToRemove.Count;
                        var busyInstances = currentInstances
                            .Where(i => !instancesToRemove.Any(r => r.ProcessorId == i.ProcessorId))
                            .Take(busyToRemove);
                        instancesToRemove.AddRange(busyInstances);
                    }

                    // Teardown each worker container
                    foreach (var instance in instancesToRemove)
                    {
                        _logger.LogInformation(
                            "ScalePool: Tearing down worker {ProcessorId} with app-id {AppId}",
                            instance.ProcessorId, instance.AppId);

                        var teardownResult = await orchestrator.TeardownServiceAsync(
                            instance.AppId,
                            removeVolumes: false,
                            cancellationToken);

                        if (teardownResult.Success)
                        {
                            // Release any lease for this processor
                            var leaseToRemove = leases.FirstOrDefault(l => l.Value.ProcessorId == instance.ProcessorId);
                            if (leaseToRemove.Key != null)
                            {
                                leases.Remove(leaseToRemove.Key);
                            }

                            availableInstances.RemoveAll(p => p.ProcessorId == instance.ProcessorId);
                            currentInstances.RemoveAll(p => p.ProcessorId == instance.ProcessorId);
                            scaledDown++;

                            _logger.LogInformation(
                                "ScalePool: Torn down worker {ProcessorId} successfully",
                                instance.ProcessorId);
                        }
                        else
                        {
                            _logger.LogWarning(
                                "ScalePool: Failed to teardown worker {ProcessorId}: {Message}",
                                instance.ProcessorId, teardownResult.Message);
                        }
                    }
                }

                _logger.LogInformation(
                    "ScalePool: Scaled down pool {PoolType} by {Count} instances",
                    body.PoolType, scaledDown);
            }

            // Save updated state
            await _stateManager.SetListAsync(instancesKey, currentInstances);
            await _stateManager.SetListAsync(availableKey, availableInstances);
            await _stateManager.SetHashAsync(leasesKey, leases);

            // Update metrics with scale event
            var metrics = await _stateManager.GetValueAsync<PoolMetricsData>(metricsKey) ?? new PoolMetricsData();
            metrics.LastScaleEvent = DateTimeOffset.UtcNow;
            await _stateManager.SetValueAsync(metricsKey, metrics);

            return (StatusCodes.OK, new ScalePoolResponse
            {
                PoolType = body.PoolType,
                PreviousInstances = previousCount,
                CurrentInstances = currentInstances.Count,
                ScaledUp = scaledUp,
                ScaledDown = scaledDown
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ScalePool: Error scaling pool {PoolType}", body?.PoolType);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Cleans up idle processors from a pool, optionally preserving minimum instances.
    /// </summary>
    /// <param name="body">Request containing pool type and preservation preference.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Cleanup result including removed instance count.</returns>
    public async Task<(StatusCodes, CleanupPoolResponse?)> CleanupPoolAsync(
        CleanupPoolRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {

            if (string.IsNullOrEmpty(body.PoolType))
            {
                _logger.LogWarning("CleanupPool: Missing pool_type");
                return (StatusCodes.BadRequest, null);
            }

            var instancesKey = string.Format(POOL_INSTANCES_KEY, body.PoolType);
            var availableKey = string.Format(POOL_AVAILABLE_KEY, body.PoolType);
            var configKey = string.Format(POOL_CONFIG_KEY, body.PoolType);

            var currentInstances = await _stateManager.GetListAsync<ProcessorInstance>(instancesKey) ?? new List<ProcessorInstance>();
            var availableInstances = await _stateManager.GetListAsync<ProcessorInstance>(availableKey) ?? new List<ProcessorInstance>();
            var config = await _stateManager.GetValueAsync<PoolConfiguration>(configKey);

            var minInstances = config?.MinInstances ?? 1;
            var targetCount = body.PreserveMinimum ? minInstances : 0;

            var toRemove = availableInstances.Count - targetCount;
            if (toRemove <= 0)
            {
                return (StatusCodes.OK, new CleanupPoolResponse
                {
                    PoolType = body.PoolType,
                    InstancesRemoved = 0,
                    CurrentInstances = currentInstances.Count,
                    Message = "No idle instances to remove"
                });
            }

            // Remove idle instances
            var removedIds = availableInstances
                .Take(toRemove)
                .Select(p => p.ProcessorId)
                .ToHashSet();

            availableInstances.RemoveAll(p => removedIds.Contains(p.ProcessorId));
            currentInstances.RemoveAll(p => removedIds.Contains(p.ProcessorId));

            await _stateManager.SetListAsync(instancesKey, currentInstances);
            await _stateManager.SetListAsync(availableKey, availableInstances);

            _logger.LogInformation(
                "CleanupPool: Removed {Count} idle instances from pool {PoolType}, {Remaining} instances remaining",
                removedIds.Count, body.PoolType, currentInstances.Count);

            return (StatusCodes.OK, new CleanupPoolResponse
            {
                PoolType = body.PoolType,
                InstancesRemoved = removedIds.Count,
                CurrentInstances = currentInstances.Count,
                Message = $"Cleaned up {removedIds.Count} idle processor(s)"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CleanupPool: Error cleaning up pool {PoolType}", body?.PoolType);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Initializes pool configurations in Redis from preset processing_pools definitions.
    /// Called when a preset with processing pools is deployed.
    /// </summary>
    /// <param name="processingPools">Processing pool definitions from the preset.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    private async Task InitializePoolConfigurationsAsync(
        List<PresetProcessingPool> processingPools,
        CancellationToken cancellationToken)
    {
        foreach (var pool in processingPools)
        {
            if (string.IsNullOrEmpty(pool.PoolType))
            {
                _logger.LogWarning("Skipping pool with empty pool_type");
                continue;
            }

            var configKey = string.Format(POOL_CONFIG_KEY, pool.PoolType);

            // Build the pool worker environment variables
            var poolEnvironment = new Dictionary<string, string>();

            // Enable only the specified plugin
            if (!string.IsNullOrEmpty(pool.Plugin))
            {
                poolEnvironment["SERVICES_ENABLED"] = "false";
                var envVarName = pool.Plugin.ToUpperInvariant().Replace("-", "_") + "_SERVICE_ENABLED";
                poolEnvironment[envVarName] = "true";
            }

            // Merge custom environment from preset
            if (pool.Environment != null)
            {
                foreach (var kvp in pool.Environment)
                {
                    poolEnvironment[kvp.Key] = kvp.Value;
                }
            }

            var config = new PoolConfiguration
            {
                PoolType = pool.PoolType,
                ServiceName = pool.Plugin,
                Image = pool.Image,
                Environment = poolEnvironment,
                MinInstances = pool.MinInstances,
                MaxInstances = pool.MaxInstances,
                ScaleUpThreshold = pool.ScaleUpThreshold,
                ScaleDownThreshold = pool.ScaleDownThreshold,
                IdleTimeoutMinutes = pool.IdleTimeoutMinutes
            };

            await _stateManager.SetValueAsync(configKey, config);

            _logger.LogInformation(
                "Initialized pool configuration: PoolType={PoolType}, Plugin={Plugin}, Min={Min}, Max={Max}",
                pool.PoolType, pool.Plugin, pool.MinInstances, pool.MaxInstances);

            // Register this pool type in the known pools list
            await RegisterPoolTypeAsync(pool.PoolType, cancellationToken);
        }
    }

    /// <summary>
    /// Registers a pool type in the known pools list stored in Redis.
    /// </summary>
    /// <param name="poolType">The pool type to register.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    private async Task RegisterPoolTypeAsync(string poolType, CancellationToken cancellationToken)
    {
        var knownPoolsKey = "orchestrator:pools:known";
        var knownPools = await _stateManager.GetListAsync<string>(knownPoolsKey) ?? new List<string>();

        if (!knownPools.Contains(poolType))
        {
            knownPools.Add(poolType);
            await _stateManager.SetListAsync(knownPoolsKey, knownPools);
        }
    }

    /// <summary>
    /// Gets a list of known pool types from Redis.
    /// </summary>
    private async Task<List<string>> GetKnownPoolTypesAsync()
    {
        var knownPoolsKey = "orchestrator:pools:known";
        var knownPools = await _stateManager.GetListAsync<string>(knownPoolsKey);
        return knownPools ?? new List<string>();
    }

    /// <summary>
    /// Updates pool metrics after a job completes.
    /// </summary>
    private async Task UpdatePoolMetricsAsync(string poolType, bool success)
    {
        var metricsKey = string.Format(POOL_METRICS_KEY, poolType);
        var metrics = await _stateManager.GetValueAsync<PoolMetricsData>(metricsKey) ?? new PoolMetricsData();

        if (success)
        {
            metrics.JobsCompleted1h++;
        }
        else
        {
            metrics.JobsFailed1h++;
        }

        await _stateManager.SetValueAsync(metricsKey, metrics);
    }

    /// <summary>
    /// Invalidates the OpenResty routing cache by calling an internal endpoint.
    /// This forces OpenResty to re-read routing data from Redis on the next request.
    /// Non-blocking: failures are logged but don't stop the deployment flow.
    /// </summary>
    private async Task InvalidateOpenRestryRoutingCacheAsync()
    {
        try
        {
            // OpenResty exposes an internal cache invalidation endpoint
            // This is only accessible from within the Docker network
            var openRestyHost = _configuration.OpenRestyHost;
            var openRestyPort = _configuration.OpenRestyPort;
            var invalidateUrl = $"http://{openRestyHost}:{openRestyPort}/internal/cache/invalidate";

            using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(_configuration.OpenRestyRequestTimeoutSeconds) };
            var response = await httpClient.PostAsync(invalidateUrl, null);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("OpenResty routing cache invalidated successfully");
            }
            else
            {
                _logger.LogWarning(
                    "OpenResty cache invalidation returned {StatusCode}: {Reason}",
                    (int)response.StatusCode, response.ReasonPhrase);
            }
        }
        catch (HttpRequestException ex)
        {
            // OpenResty might not be available (local dev without Docker) - this is fine
            _logger.LogDebug(ex, "Could not invalidate OpenResty cache (endpoint may not be available)");
        }
        catch (TaskCanceledException)
        {
            _logger.LogDebug("OpenResty cache invalidation timed out");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unexpected error invalidating OpenResty cache");
        }
    }

    #endregion

    #region Processing Pool Internal Models

    /// <summary>
    /// Status of a processor instance in the pool.
    /// </summary>
    internal enum ProcessorStatus
    {
        /// <summary>Processor is ready to handle requests.</summary>
        Available,
        /// <summary>Processor is starting up and not yet ready.</summary>
        Pending
    }

    /// <summary>
    /// Represents a processor instance in the pool.
    /// </summary>
    internal sealed class ProcessorInstance
    {
        public string ProcessorId { get; set; } = string.Empty;
        public string AppId { get; set; } = string.Empty;
        public string PoolType { get; set; } = string.Empty;
        public ProcessorStatus Status { get; set; } = ProcessorStatus.Available;
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset LastUpdated { get; set; }
    }

    /// <summary>
    /// Represents an active lease for a processor.
    /// </summary>
    internal sealed class ProcessorLease
    {
        public Guid LeaseId { get; set; }
        public string ProcessorId { get; set; } = string.Empty;
        public string AppId { get; set; } = string.Empty;
        public string PoolType { get; set; } = string.Empty;
        public DateTimeOffset AcquiredAt { get; set; }
        public DateTimeOffset ExpiresAt { get; set; }
        public int Priority { get; set; }
        public object? Metadata { get; set; }
    }

    /// <summary>
    /// Pool configuration stored in Redis.
    /// Contains all settings needed to spawn pool worker containers.
    /// </summary>
    internal sealed class PoolConfiguration
    {
        /// <summary>Pool type identifier (e.g., "actor-shared", "asset-image").</summary>
        public string PoolType { get; set; } = string.Empty;

        /// <summary>Service/plugin name to enable on pool workers.</summary>
        public string ServiceName { get; set; } = string.Empty;

        /// <summary>Docker image for pool workers. If empty, uses default bannou image.</summary>
        public string? Image { get; set; }

        /// <summary>Environment variables for pool worker containers.</summary>
        public Dictionary<string, string>? Environment { get; set; }

        /// <summary>Minimum number of instances to maintain.</summary>
        public int MinInstances { get; set; } = 1;

        /// <summary>Maximum number of instances allowed.</summary>
        public int MaxInstances { get; set; } = 5;

        /// <summary>Scale up when utilization exceeds this threshold (0.0-1.0).</summary>
        public double ScaleUpThreshold { get; set; } = 0.8;

        /// <summary>Scale down when utilization drops below this threshold (0.0-1.0).</summary>
        public double ScaleDownThreshold { get; set; } = 0.2;

        /// <summary>Time in minutes before idle workers are cleaned up.</summary>
        public int IdleTimeoutMinutes { get; set; } = 5;
    }

    /// <summary>
    /// Pool metrics data stored in Redis.
    /// </summary>
    internal sealed class PoolMetricsData
    {
        public int JobsCompleted1h { get; set; }
        public int JobsFailed1h { get; set; }
        public int AvgProcessingTimeMs { get; set; }
        public DateTimeOffset LastScaleEvent { get; set; }
    }

    #endregion
}
