using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Orchestrator;
using BeyondImmersion.BannouService.Services;
using Docker.DotNet;
using Docker.DotNet.Models;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace LibOrchestrator;

/// <summary>
/// Manages intelligent service restart logic with Docker container lifecycle management.
/// Uses Docker.DotNet for container operations (Docker Compose environments).
/// </summary>
public class SmartRestartManager : ISmartRestartManager
{
    private readonly ILogger<SmartRestartManager> _logger;
    private readonly OrchestratorServiceConfiguration _configuration;
    private readonly IServiceHealthMonitor _healthMonitor;
    private readonly IOrchestratorEventManager _eventManager;
    private readonly ITelemetryProvider _telemetryProvider;
    private DockerClient? _dockerClient;

    public SmartRestartManager(
        ILogger<SmartRestartManager> logger,
        OrchestratorServiceConfiguration configuration,
        IServiceHealthMonitor healthMonitor,
        IOrchestratorEventManager eventManager,
        ITelemetryProvider telemetryProvider)
    {
        _logger = logger;
        _configuration = configuration;
        _healthMonitor = healthMonitor;
        _eventManager = eventManager;
        ArgumentNullException.ThrowIfNull(telemetryProvider, nameof(telemetryProvider));
        _telemetryProvider = telemetryProvider;
    }

    /// <summary>
    /// Initialize Docker client for container management.
    /// </summary>
    public async Task InitializeAsync()
    {
        using var activity = _telemetryProvider.StartActivity("bannou.orchestrator", "SmartRestartManager.InitializeAsync");
        try
        {
            var dockerHost = _configuration.DockerHost;
            _logger.LogDebug("Initializing Docker client with host: {DockerHost}", dockerHost);

            using var config = new DockerClientConfiguration(new Uri(dockerHost));
            _dockerClient = config.CreateClient();

            _logger.LogDebug("Docker client initialized successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize Docker client");
            throw;
        }
        await Task.CompletedTask; // Satisfies async interface requirement
    }

    /// <summary>
    /// Restart a service container with optional environment updates.
    /// Implements smart restart logic based on health metrics.
    /// </summary>
    public async Task<RestartOutcome> RestartServiceAsync(ServiceRestartRequest request)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.orchestrator", "SmartRestartManager.RestartServiceAsync");
        var startTime = DateTime.UtcNow;

        try
        {
            // Check if restart is needed (unless forced)
            if (!request.Force)
            {
                var recommendation = await _healthMonitor.ShouldRestartServiceAsync(request.ServiceName);
                if (!recommendation.ShouldRestart)
                {
                    return new RestartOutcome(
                        Succeeded: false,
                        DeclineReason: $"Restart not needed: {recommendation.Reason}",
                        Duration: (DateTime.UtcNow - startTime).ToString(@"hh\:mm\:ss"),
                        PreviousStatus: recommendation.CurrentStatus,
                        CurrentStatus: recommendation.CurrentStatus);
                }
            }

            // Get current service status before restart
            var previousStatus = await GetCurrentServiceStatusAsync(request.ServiceName);

            // Find container by service name
            var container = await FindContainerByServiceNameAsync(request.ServiceName) ?? throw new InvalidOperationException($"Container for service '{request.ServiceName}' not found");
            _logger.LogDebug(
                "Restarting service: {ServiceName} (container: {ContainerId})",
                request.ServiceName, container.ID[..12]);

            // Update environment variables if provided
            if (request.Environment != null && request.Environment.Any())
            {
                _logger.LogDebug(
                    "Updating environment variables for {ServiceName}: {Count} variables",
                    request.ServiceName, request.Environment.Count);

                // Note: Docker requires recreating container to update environment
                // For now, we'll just restart with existing environment
                // Full implementation would require container recreation
                _logger.LogWarning(
                    "Environment variable updates require container recreation - not implemented yet");
            }

            // Restart the container
            if (_dockerClient == null)
            {
                throw new InvalidOperationException("Docker client not initialized");
            }
            await _dockerClient.Containers.RestartContainerAsync(
                container.ID,
                new ContainerRestartParameters
                {
                    WaitBeforeKillSeconds = (uint)_configuration.DefaultWaitBeforeKillSeconds
                });

            // Wait for service to become healthy
            var timeoutSeconds = request.Timeout > 0 ? request.Timeout : _configuration.RestartTimeoutSeconds;
            var timeout = TimeSpan.FromSeconds(timeoutSeconds);
            var isHealthy = await WaitForServiceHealthAsync(request.ServiceName, timeout);

            var currentStatus = await GetCurrentServiceStatusAsync(request.ServiceName);
            var duration = DateTime.UtcNow - startTime;

            // Publish restart event
            await _eventManager.PublishServiceRestartEventAsync(new ServiceRestartEvent
            {
                EventId = Guid.NewGuid(),
                ServiceName = request.ServiceName,
                Reason = $"Smart restart - previous status: {previousStatus}",
                Forced = request.Force,
                NewEnvironment = request.Environment != null ? new Dictionary<string, string>(request.Environment) : new Dictionary<string, string>()
            });

            return new RestartOutcome(
                Succeeded: isHealthy,
                DeclineReason: isHealthy ? null : $"Service restarted but failed to become healthy within {timeout.TotalSeconds} seconds",
                Duration: duration.ToString(@"hh\:mm\:ss"),
                PreviousStatus: previousStatus,
                CurrentStatus: currentStatus);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to restart service: {ServiceName}", request.ServiceName);

            return new RestartOutcome(
                Succeeded: false,
                DeclineReason: $"Restart failed: {ex.Message}",
                Duration: (DateTime.UtcNow - startTime).ToString(@"hh\:mm\:ss"),
                PreviousStatus: null,
                CurrentStatus: null);
        }
    }

    /// <summary>
    /// Find Docker container by service name.
    /// Looks for containers with matching labels or names.
    /// </summary>
    private async Task<ContainerListResponse?> FindContainerByServiceNameAsync(string serviceName)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.orchestrator", "SmartRestartManager.FindContainerByServiceNameAsync");
        if (_dockerClient == null)
        {
            throw new InvalidOperationException("Docker client not initialized");
        }

        var containers = await _dockerClient.Containers.ListContainersAsync(
            new ContainersListParameters
            {
                All = true
            });

        // Try matching by Docker Compose service label
        var container = containers.FirstOrDefault(c =>
            c.Labels.TryGetValue("com.docker.compose.service", out var service) &&
            service == serviceName) ?? containers.FirstOrDefault(c =>
                c.Names.Any(n => n.Contains(serviceName, StringComparison.OrdinalIgnoreCase)));
        return container;
    }

    /// <summary>
    /// Wait for service to become healthy after restart.
    /// Polls health status until timeout or service becomes healthy.
    /// </summary>
    private async Task<bool> WaitForServiceHealthAsync(string serviceName, TimeSpan timeout)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.orchestrator", "SmartRestartManager.WaitForServiceHealthAsync");
        var startTime = DateTime.UtcNow;
        var endTime = startTime + timeout;

        _logger.LogDebug(
            "Waiting for {ServiceName} to become healthy (timeout: {TimeoutSeconds}s)",
            serviceName, timeout.TotalSeconds);

        while (DateTime.UtcNow < endTime)
        {
            var recommendation = await _healthMonitor.ShouldRestartServiceAsync(serviceName);

            if (recommendation.CurrentStatus == ServiceHealthStatus.Healthy)
            {
                var elapsed = DateTime.UtcNow - startTime;
                _logger.LogInformation(
                    "Service {ServiceName} is healthy (took {ElapsedSeconds:F1}s)",
                    serviceName, elapsed.TotalSeconds);
                return true;
            }

            _logger.LogDebug(
                "Service {ServiceName} status: {Status} - waiting...",
                serviceName, recommendation.CurrentStatus);

            await Task.Delay(_configuration.HealthCheckIntervalMs);
        }

        _logger.LogWarning(
            "Service {ServiceName} did not become healthy within {TimeoutSeconds}s timeout",
            serviceName, timeout.TotalSeconds);

        return false;
    }

    /// <summary>
    /// Get current service status from health monitoring.
    /// </summary>
    private async Task<ServiceHealthStatus?> GetCurrentServiceStatusAsync(string serviceName)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.orchestrator", "SmartRestartManager.GetCurrentServiceStatusAsync");
        var recommendation = await _healthMonitor.ShouldRestartServiceAsync(serviceName);
        return recommendation.CurrentStatus;
    }

    /// <summary>
    /// Synchronous dispose for DI container compatibility.
    /// </summary>
    public void Dispose()
    {
        if (_dockerClient != null)
        {
            _dockerClient.Dispose();
            _logger.LogDebug("Docker client disposed synchronously");
        }
    }

    /// <summary>
    /// Async dispose for async-aware disposal contexts.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        Dispose();
        await Task.CompletedTask;
    }
}
