using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Configuration;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Orchestrator;
using BeyondImmersion.BannouService.Plugins;
using BeyondImmersion.BannouService.Services;
using LibOrchestrator;
using LibOrchestrator.Backends;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace BeyondImmersion.BannouService.Orchestrator;

// =============================================================================
// OrchestratorService — Private & Internal Helper Methods
// =============================================================================
//
// This partial class file is the designated home for all private and internal
// helper methods used by OrchestratorService. This separation exists to
// support the IMPLEMENTATION TENETS telemetry span rules:
//
//   - PRIMARY FILE (OrchestratorService.cs):
//     Contains ONLY public interface method implementations (the methods
//     declared in IOrchestratorService). These methods MUST NOT call
//     ITelemetryProvider.StartActivity because the generated controller
//     already wraps each endpoint with a telemetry span. Adding a span
//     in the service method would double-instrument the endpoint.
//
//   - THIS FILE (OrchestratorService.Helpers.cs):
//     Contains all private/internal helper methods, core logic extracted
//     from endpoints, event publishing helpers, query builders, mapping
//     functions, and any other non-public methods. Every async method in
//     this file MUST call ITelemetryProvider.StartActivity to ensure
//     sub-operations are properly instrumented.
//
// Structural tests enforce both rules:
//   - Services_PrimaryFile_DoesNotCallStartActivity
//   - Services_HelperFiles_HaveStartActivityWhenAsync
//
// WHAT GOES HERE:
//   - Private async helper methods (with StartActivity spans)
//   - Private sync helper methods (query builders, mappers, validators)
//   - Internal static key builders (already in primary file by convention,
//     but may be moved here if the primary file is large)
//   - Event publishing helper methods
//   - Any extracted "core" logic (e.g., CreateAccountCoreAsync)
//
// WHAT STAYS IN THE PRIMARY FILE:
//   - Public interface method implementations (/// <inheritdoc/> methods)
//   - Constructor and field declarations
//   - Constants and key prefix definitions
//
// See: docs/reference/tenets/IMPLEMENTATION-BEHAVIOR.md (T30)
// See: docs/reference/HELPERS-AND-COMMON-PATTERNS.md
// =============================================================================

/// <summary>
/// Private and internal helper methods for OrchestratorService.
/// </summary>
/// <remarks>
/// <para>
/// This partial class contains all non-public helper methods. Every async method
/// in this file MUST include a <c>using var activity = _telemetryProvider.StartActivity(...)</c>
/// span per IMPLEMENTATION TENETS (T30). The generated controller instruments the
/// public interface methods; this file instruments the sub-operations.
/// </para>
/// </remarks>
public partial class OrchestratorService
{
    // Private/internal helper methods moved from OrchestratorService.cs
    /// <summary>
    /// Ensures the last known deployment state is loaded from persistent storage.
    /// Called lazily on first access to provide startup awareness.
    /// </summary>
    private async Task EnsureLastDeploymentLoadedAsync()
    {
        using var activity = _telemetryProvider.StartActivity("bannou.orchestrator", "OrchestratorService.EnsureLastDeploymentLoadedAsync");
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
    /// Provides within-request reuse only (service is Scoped, instance resets per request).
    /// </summary>
    private async Task<IContainerOrchestrator> GetOrchestratorAsync(CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.orchestrator", "OrchestratorService.GetOrchestratorAsync");
        if (_orchestrator != null)
        {
            return _orchestrator;
        }

        _orchestrator = await _backendDetector.CreateBestOrchestratorAsync(cancellationToken);
        return _orchestrator;
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
        using var activity = _telemetryProvider.StartActivity("bannou.orchestrator", "OrchestratorService.ExecuteActualTeardownAsync");
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
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            Action = success ? DeploymentAction.Completed : DeploymentAction.Failed,
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
        using var activity = _telemetryProvider.StartActivity("bannou.orchestrator", "OrchestratorService.PublishErrorEventAsync");
        await _messageBus.TryPublishErrorAsync(
            serviceName: "orchestrator",
            operation: operation,
            errorType: errorType,
            message: message,
            dependency: dependency,
            details: details);
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
        using var activity = _telemetryProvider.StartActivity("bannou.orchestrator", "OrchestratorService.ResetToDefaultTopologyAsync");
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
                await InvalidateOpenRestyRoutingCacheAsync();
                await _stateManager.ClearCurrentConfigurationAsync();

                return (true, "Already at default topology - all services on 'bannou', configuration cleared", tornDownServices);
            }

            // CRITICAL: Reset service mappings FIRST, before tearing down containers.
            // This ensures routing proxies (OpenResty, lib-mesh) get updated routes
            // before the old containers become unavailable.
            await _healthMonitor.ResetAllMappingsToDefaultAsync();
            await InvalidateOpenRestyRoutingCacheAsync();

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
        using var activity = _telemetryProvider.StartActivity("bannou.orchestrator", "OrchestratorService.InitializePoolConfigurationsAsync");
        foreach (var pool in processingPools)
        {
            if (string.IsNullOrEmpty(pool.PoolType))
            {
                _logger.LogWarning("Skipping pool with empty pool_type");
                continue;
            }

            // Build the pool worker environment variables
            var poolEnvironment = new Dictionary<string, string>();

            // Enable only the specified plugin
            if (!string.IsNullOrEmpty(pool.Plugin))
            {
                poolEnvironment["BANNOU_SERVICES_ENABLED"] = "false";
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

            await _stateManager.SetPoolConfigurationAsync(pool.PoolType, config);

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
        using var activity = _telemetryProvider.StartActivity("bannou.orchestrator", "OrchestratorService.RegisterPoolTypeAsync");
        var knownPools = await _stateManager.GetKnownPoolTypesAsync() ?? new List<string>();

        if (!knownPools.Contains(poolType))
        {
            knownPools.Add(poolType);
            await _stateManager.SetKnownPoolTypesAsync(knownPools);
        }
    }
    /// <summary>
    /// Gets a list of known pool types from state store.
    /// </summary>
    private async Task<List<string>> GetKnownPoolTypesAsync()
    {
        using var activity = _telemetryProvider.StartActivity("bannou.orchestrator", "OrchestratorService.GetKnownPoolTypesAsync");
        var knownPools = await _stateManager.GetKnownPoolTypesAsync();
        return knownPools ?? new List<string>();
    }
    /// <summary>
    /// Reclaims expired leases by returning their processors to the available list.
    /// Called before checking availability to ensure expired leases don't permanently consume processors.
    /// </summary>
    /// <param name="poolType">The pool type to reclaim leases for.</param>
    private async Task ReclaimExpiredLeasesAsync(string poolType)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.orchestrator", "OrchestratorService.ReclaimExpiredLeasesAsync");
        var leases = await _stateManager.GetLeasesAsync(poolType);
        if (leases == null || leases.Count == 0)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var expiredLeaseIds = new List<string>();

        foreach (var kvp in leases)
        {
            if (kvp.Value.ExpiresAt < now)
            {
                expiredLeaseIds.Add(kvp.Key);
            }
        }

        if (expiredLeaseIds.Count == 0)
        {
            return;
        }

        // Return expired processors to the available list
        var availableProcessors = await _stateManager.GetAvailableProcessorsAsync(poolType) ?? new List<ProcessorInstance>();

        foreach (var leaseId in expiredLeaseIds)
        {
            var expiredLease = leases[leaseId];

            availableProcessors.Add(new ProcessorInstance
            {
                ProcessorId = expiredLease.ProcessorId,
                AppId = expiredLease.AppId,
                PoolType = poolType,
                Status = ProcessorStatus.Available,
                LastUpdated = now
            });

            leases.Remove(leaseId);

            _logger.LogInformation(
                "Reclaimed expired lease {LeaseId} for processor {ProcessorId} in pool {PoolType} (expired at {ExpiresAt})",
                leaseId, expiredLease.ProcessorId, poolType, expiredLease.ExpiresAt);
        }

        await _stateManager.SetAvailableProcessorsAsync(poolType, availableProcessors);
        await _stateManager.SetLeasesAsync(poolType, leases);
    }
    /// <summary>
    /// Updates pool metrics after a job completes.
    /// Resets hourly counters when the 1-hour window has elapsed.
    /// </summary>
    private async Task UpdatePoolMetricsAsync(string poolType, bool success)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.orchestrator", "OrchestratorService.UpdatePoolMetricsAsync");
        var metrics = await _stateManager.GetPoolMetricsAsync(poolType) ?? new PoolMetricsData();

        // Reset counters if the 1-hour window has elapsed
        if (metrics.WindowStart == default || DateTimeOffset.UtcNow - metrics.WindowStart >= TimeSpan.FromHours(1))
        {
            metrics.JobsCompleted1h = 0;
            metrics.JobsFailed1h = 0;
            metrics.WindowStart = DateTimeOffset.UtcNow;
        }

        if (success)
        {
            metrics.JobsCompleted1h++;
        }
        else
        {
            metrics.JobsFailed1h++;
        }

        await _stateManager.SetPoolMetricsAsync(poolType, metrics);
    }
    /// <summary>
    /// Invalidates the OpenResty routing cache by calling an internal endpoint.
    /// This forces OpenResty to re-read routing data from Redis on the next request.
    /// Non-blocking: failures are logged but don't stop the deployment flow.
    /// </summary>
    private async Task InvalidateOpenRestyRoutingCacheAsync()
    {
        using var activity = _telemetryProvider.StartActivity("bannou.orchestrator", "OrchestratorService.InvalidateOpenRestyRoutingCacheAsync");
        try
        {
            // OpenResty exposes an internal cache invalidation endpoint
            // This is only accessible from within the Docker network
            var openRestyHost = _configuration.OpenRestyHost;
            var openRestyPort = _configuration.OpenRestyPort;
            var invalidateUrl = $"http://{openRestyHost}:{openRestyPort}/internal/cache/invalidate";

            using var httpClient = _httpClientFactory.CreateClient("OpenResty");
            httpClient.Timeout = TimeSpan.FromSeconds(_configuration.OpenRestyRequestTimeoutSeconds);
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
}
