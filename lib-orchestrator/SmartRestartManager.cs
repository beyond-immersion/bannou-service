using BeyondImmersion.BannouService.Orchestrator;
using Docker.DotNet;
using Docker.DotNet.Models;
using Microsoft.Extensions.Logging;

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
    private DockerClient? _dockerClient;

    private const int DEFAULT_RESTART_TIMEOUT_SECONDS = 120;
    private const int HEALTH_CHECK_INTERVAL_MS = 2000;

    public SmartRestartManager(
        ILogger<SmartRestartManager> logger,
        OrchestratorServiceConfiguration configuration,
        IServiceHealthMonitor healthMonitor,
        IOrchestratorEventManager eventManager)
    {
        _logger = logger;
        _configuration = configuration;
        _healthMonitor = healthMonitor;
        _eventManager = eventManager;
    }

    /// <summary>
    /// Initialize Docker client for container management.
    /// </summary>
    public Task InitializeAsync()
    {
        try
        {
            var dockerHost = _configuration.DockerHost ?? "unix:///var/run/docker.sock";
            _logger.LogInformation("Initializing Docker client with host: {DockerHost}", dockerHost);

            _dockerClient = new DockerClientConfiguration(new Uri(dockerHost))
                .CreateClient();

            _logger.LogInformation("Docker client initialized successfully");
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize Docker client");
            throw;
        }
    }

    /// <summary>
    /// Restart a service container with optional environment updates.
    /// Implements smart restart logic based on health metrics.
    /// </summary>
    public async Task<ServiceRestartResult> RestartServiceAsync(ServiceRestartRequest request)
    {
        var startTime = DateTime.UtcNow;

        try
        {
            // Check if restart is needed (unless forced)
            if (!request.Force)
            {
                var recommendation = await _healthMonitor.ShouldRestartServiceAsync(request.ServiceName);
                if (!recommendation.ShouldRestart)
                {
                    return new ServiceRestartResult
                    {
                        Success = false,
                        ServiceName = request.ServiceName,
                        Duration = (DateTime.UtcNow - startTime).ToString(@"hh\:mm\:ss"),
                        PreviousStatus = recommendation.CurrentStatus,
                        CurrentStatus = recommendation.CurrentStatus,
                        Message = $"Restart not needed: {recommendation.Reason}"
                    };
                }
            }

            // Get current service status before restart
            var previousStatus = await GetCurrentServiceStatusAsync(request.ServiceName);

            // Find container by service name
            var container = await FindContainerByServiceNameAsync(request.ServiceName) ?? throw new InvalidOperationException($"Container for service '{request.ServiceName}' not found");
            _logger.LogInformation(
                "Restarting service: {ServiceName} (container: {ContainerId})",
                request.ServiceName, container.ID[..12]);

            // Update environment variables if provided
            if (request.Environment != null && request.Environment.Any())
            {
                _logger.LogInformation(
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
                    WaitBeforeKillSeconds = 30
                });

            // Wait for service to become healthy
            var timeoutSeconds = request.Timeout > 0 ? request.Timeout : DEFAULT_RESTART_TIMEOUT_SECONDS;
            var timeout = TimeSpan.FromSeconds(timeoutSeconds);
            var isHealthy = await WaitForServiceHealthAsync(request.ServiceName, timeout);

            var currentStatus = await GetCurrentServiceStatusAsync(request.ServiceName);
            var duration = DateTime.UtcNow - startTime;

            // Publish restart event
            await _eventManager.PublishServiceRestartEventAsync(new ServiceRestartEvent
            {
                EventId = Guid.NewGuid().ToString(),
                ServiceName = request.ServiceName,
                Reason = $"Smart restart - previous status: {previousStatus}",
                Forced = request.Force,
                NewEnvironment = request.Environment != null ? new Dictionary<string, string>(request.Environment) : new Dictionary<string, string>()
            });

            return new ServiceRestartResult
            {
                Success = isHealthy,
                ServiceName = request.ServiceName,
                Duration = duration.ToString(@"hh\:mm\:ss"),
                PreviousStatus = previousStatus,
                CurrentStatus = currentStatus,
                Message = isHealthy
                    ? $"Service restarted successfully in {duration.TotalSeconds:F1} seconds"
                    : $"Service restarted but failed to become healthy within {timeout.TotalSeconds} seconds"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to restart service: {ServiceName}", request.ServiceName);

            return new ServiceRestartResult
            {
                Success = false,
                ServiceName = request.ServiceName,
                Duration = (DateTime.UtcNow - startTime).ToString(@"hh\:mm\:ss"),
                PreviousStatus = "unknown",
                CurrentStatus = "error",
                Message = $"Restart failed: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Find Docker container by service name.
    /// Looks for containers with matching labels or names.
    /// </summary>
    private async Task<ContainerListResponse?> FindContainerByServiceNameAsync(string serviceName)
    {
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
        var startTime = DateTime.UtcNow;
        var endTime = startTime + timeout;

        _logger.LogInformation(
            "Waiting for {ServiceName} to become healthy (timeout: {TimeoutSeconds}s)",
            serviceName, timeout.TotalSeconds);

        while (DateTime.UtcNow < endTime)
        {
            var recommendation = await _healthMonitor.ShouldRestartServiceAsync(serviceName);

            if (recommendation.CurrentStatus == "healthy")
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

            await Task.Delay(HEALTH_CHECK_INTERVAL_MS);
        }

        _logger.LogWarning(
            "Service {ServiceName} did not become healthy within {TimeoutSeconds}s timeout",
            serviceName, timeout.TotalSeconds);

        return false;
    }

    /// <summary>
    /// Get current service status from health monitoring.
    /// </summary>
    private async Task<string> GetCurrentServiceStatusAsync(string serviceName)
    {
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
