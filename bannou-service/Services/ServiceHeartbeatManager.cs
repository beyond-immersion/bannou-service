using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Plugins;
using Dapr.Client;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Services;

/// <summary>
/// Manages heartbeat publishing for all services in a bannou instance.
/// Publishes aggregated heartbeats via Dapr pub/sub on startup and periodically.
/// </summary>
public class ServiceHeartbeatManager : IAsyncDisposable
{
    private readonly DaprClient _daprClient;
    private readonly ILogger<ServiceHeartbeatManager> _logger;
    private readonly PluginLoader _pluginLoader;

    private Timer? _heartbeatTimer;
    private bool _isRunning;
    private readonly List<string> _currentIssues = new();

    /// <summary>
    /// Unique instance identifier for this bannou application instance.
    /// Used for log correlation across distributed systems.
    /// </summary>
    public Guid InstanceId { get; } = Guid.NewGuid();

    /// <summary>
    /// The Dapr app-id for this instance. Resolved from environment variables.
    /// Order: DAPR_APP_ID -> APP_ID -> "bannou" default
    /// </summary>
    public string AppId { get; }

    /// <summary>
    /// Heartbeat interval in seconds. Default is 30 seconds.
    /// Configurable via HEARTBEAT_INTERVAL_SECONDS environment variable.
    /// </summary>
    public int HeartbeatIntervalSeconds { get; }

    /// <summary>
    /// The pub/sub component name for heartbeat events.
    /// </summary>
    private const string PUBSUB_NAME = "bannou-pubsub";

    /// <summary>
    /// The topic name for heartbeat events.
    /// </summary>
    private const string HEARTBEAT_TOPIC = "bannou-service-heartbeats";

    /// <inheritdoc/>
    public ServiceHeartbeatManager(
        DaprClient daprClient,
        ILogger<ServiceHeartbeatManager> logger,
        PluginLoader pluginLoader)
    {
        _daprClient = daprClient ?? throw new ArgumentNullException(nameof(daprClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _pluginLoader = pluginLoader ?? throw new ArgumentNullException(nameof(pluginLoader));

        // Resolve app-id from environment
        AppId = Environment.GetEnvironmentVariable("DAPR_APP_ID")
            ?? Environment.GetEnvironmentVariable("APP_ID")
            ?? AppConstants.DEFAULT_APP_NAME;

        // Get configurable heartbeat interval (default 30 seconds)
        var intervalStr = Environment.GetEnvironmentVariable("HEARTBEAT_INTERVAL_SECONDS");
        HeartbeatIntervalSeconds = int.TryParse(intervalStr, out var interval) && interval > 0
            ? interval
            : 30;

        _logger.LogInformation(
            "ServiceHeartbeatManager initialized: InstanceId={InstanceId}, AppId={AppId}, Interval={Interval}s",
            InstanceId, AppId, HeartbeatIntervalSeconds);
    }

    /// <summary>
    /// Publish startup heartbeats for all enabled services.
    /// This serves as both a heartbeat announcement and Dapr pub/sub connectivity check.
    /// Should be called after all plugins are initialized and started.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if heartbeat published successfully (Dapr is ready), false otherwise</returns>
    public async Task<bool> PublishStartupHeartbeatAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Publishing startup heartbeat for {Count} enabled services...",
            _pluginLoader.EnabledPlugins.Count);

        try
        {
            var heartbeat = BuildHeartbeatEvent(ServiceHeartbeatEventStatus.Healthy);

            await _daprClient.PublishEventAsync(
                PUBSUB_NAME,
                HEARTBEAT_TOPIC,
                heartbeat,
                cancellationToken);

            _logger.LogInformation(
                "✅ Startup heartbeat published successfully: AppId={AppId}, Services=[{Services}]",
                AppId,
                string.Join(", ", heartbeat.Services.Select(s => s.ServiceName)));

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to publish startup heartbeat - Dapr pub/sub may not be ready");
            return false;
        }
    }

    /// <summary>
    /// Wait for Dapr connectivity by attempting to publish heartbeats with retries.
    /// This blocks startup until Dapr sidecar is ready to send/receive events.
    /// </summary>
    /// <param name="maxRetries">Maximum number of retry attempts</param>
    /// <param name="retryDelayMs">Delay between retries in milliseconds</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if Dapr is ready, false if all retries exhausted</returns>
    public async Task<bool> WaitForDaprConnectivityAsync(
        int maxRetries = 30,
        int retryDelayMs = 2000,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Waiting for Dapr connectivity (max {MaxRetries} attempts, {Delay}ms between retries)...",
            maxRetries, retryDelayMs);

        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                // Attempt to publish startup heartbeat
                var success = await PublishStartupHeartbeatAsync(cancellationToken);
                if (success)
                {
                    _logger.LogInformation("✅ Dapr connectivity confirmed on attempt {Attempt}", attempt);
                    return true;
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Dapr connectivity check cancelled");
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    "Dapr connectivity attempt {Attempt}/{MaxRetries} failed: {Error}",
                    attempt, maxRetries, ex.Message);
            }

            if (attempt < maxRetries)
            {
                await Task.Delay(retryDelayMs, cancellationToken);
            }
        }

        _logger.LogError("❌ Dapr connectivity check failed after {MaxRetries} attempts", maxRetries);
        return false;
    }

    /// <summary>
    /// Start the periodic heartbeat timer.
    /// Heartbeats will be published at the configured interval.
    /// </summary>
    public void StartPeriodicHeartbeats()
    {
        if (_isRunning)
        {
            _logger.LogWarning("Periodic heartbeats already running");
            return;
        }

        _isRunning = true;
        var intervalMs = HeartbeatIntervalSeconds * 1000;

        _heartbeatTimer = new Timer(
            async _ => await PublishPeriodicHeartbeatAsync(),
            null,
            intervalMs, // Initial delay (wait one interval before first periodic heartbeat)
            intervalMs);

        _logger.LogInformation("Periodic heartbeats started (every {Interval}s)", HeartbeatIntervalSeconds);
    }

    /// <summary>
    /// Publish a periodic heartbeat with current service statuses.
    /// </summary>
    private async Task PublishPeriodicHeartbeatAsync()
    {
        try
        {
            var heartbeat = BuildHeartbeatEvent(DetermineOverallStatus());

            await _daprClient.PublishEventAsync(
                PUBSUB_NAME,
                HEARTBEAT_TOPIC,
                heartbeat);

            _logger.LogDebug(
                "Periodic heartbeat published: AppId={AppId}, Status={Status}, Services={Count}",
                AppId, heartbeat.Status, heartbeat.Services.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish periodic heartbeat");
            ReportIssue($"Heartbeat publish failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Publish a shutdown heartbeat to notify orchestrator this instance is going down.
    /// </summary>
    public async Task PublishShutdownHeartbeatAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Publishing shutdown heartbeat...");

        try
        {
            var heartbeat = BuildHeartbeatEvent(ServiceHeartbeatEventStatus.Shutting_down);

            await _daprClient.PublishEventAsync(
                PUBSUB_NAME,
                HEARTBEAT_TOPIC,
                heartbeat,
                cancellationToken);

            _logger.LogInformation("Shutdown heartbeat published successfully");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish shutdown heartbeat (continuing shutdown anyway)");
        }
    }

    /// <summary>
    /// Build the heartbeat event with all service statuses.
    /// </summary>
    private ServiceHeartbeatEvent BuildHeartbeatEvent(ServiceHeartbeatEventStatus overallStatus)
    {
        var serviceStatuses = new List<ServiceStatus>();

        // Collect heartbeat data from each resolved service
        foreach (var plugin in _pluginLoader.EnabledPlugins)
        {
            var service = _pluginLoader.GetResolvedService(plugin.PluginName);
            if (service != null)
            {
                try
                {
                    var status = service.OnHeartbeat();
                    serviceStatuses.Add(status);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Error getting heartbeat from service {ServiceName}",
                        plugin.PluginName);

                    // Add a degraded status for the service
                    serviceStatuses.Add(new ServiceStatus
                    {
                        ServiceId = service.InstanceId,
                        ServiceName = plugin.PluginName,
                        Status = ServiceStatusStatus.Degraded,
                        Version = service.ServiceVersion
                    });
                }
            }
            else
            {
                _logger.LogDebug(
                    "Plugin {PluginName} has no resolved service (may not implement IDaprService)",
                    plugin.PluginName);
            }
        }

        return new ServiceHeartbeatEvent
        {
            EventId = Guid.NewGuid().ToString(),
            Timestamp = DateTimeOffset.UtcNow,
            ServiceId = InstanceId,
            AppId = AppId,
            Status = overallStatus,
            Services = serviceStatuses,
            Issues = _currentIssues.Count > 0 ? _currentIssues.ToList() : null,
            Capacity = GetInstanceCapacity()
        };
    }

    /// <summary>
    /// Determine the overall status based on all service statuses.
    /// Returns the worst status among all services.
    /// </summary>
    private ServiceHeartbeatEventStatus DetermineOverallStatus()
    {
        if (_currentIssues.Count > 0)
        {
            return ServiceHeartbeatEventStatus.Degraded;
        }

        var worstServiceStatus = ServiceStatusStatus.Healthy;

        foreach (var plugin in _pluginLoader.EnabledPlugins)
        {
            var service = _pluginLoader.GetResolvedService(plugin.PluginName);
            if (service != null)
            {
                try
                {
                    var status = service.OnHeartbeat();
                    if (status.Status > worstServiceStatus)
                    {
                        worstServiceStatus = status.Status;
                    }
                }
                catch
                {
                    worstServiceStatus = ServiceStatusStatus.Degraded;
                }
            }
        }

        // Map service status to heartbeat status
        return worstServiceStatus switch
        {
            ServiceStatusStatus.Healthy => ServiceHeartbeatEventStatus.Healthy,
            ServiceStatusStatus.Degraded => ServiceHeartbeatEventStatus.Degraded,
            ServiceStatusStatus.Unavailable => ServiceHeartbeatEventStatus.Unavailable,
            _ => ServiceHeartbeatEventStatus.Healthy
        };
    }

    /// <summary>
    /// Get instance-level capacity information.
    /// </summary>
    private InstanceCapacity? GetInstanceCapacity()
    {
        try
        {
            var process = System.Diagnostics.Process.GetCurrentProcess();
            var workingSetMb = process.WorkingSet64 / (1024.0 * 1024.0);

            // Note: Getting accurate CPU usage requires sampling over time
            // For now, we'll omit CPU usage or implement it in a future enhancement
            return new InstanceCapacity
            {
                MemoryUsage = (float)(workingSetMb / 1024.0) // Convert to GB as ratio
            };
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to get instance capacity");
            return null;
        }
    }

    /// <summary>
    /// Report an issue affecting this instance.
    /// Issues are included in heartbeat messages.
    /// </summary>
    public void ReportIssue(string issue)
    {
        if (!_currentIssues.Contains(issue))
        {
            _currentIssues.Add(issue);
            _logger.LogWarning("Issue reported: {Issue}", issue);
        }
    }

    /// <summary>
    /// Clear a previously reported issue.
    /// </summary>
    public void ClearIssue(string issue)
    {
        if (_currentIssues.Remove(issue))
        {
            _logger.LogInformation("Issue cleared: {Issue}", issue);
        }
    }

    /// <summary>
    /// Clear all reported issues.
    /// </summary>
    public void ClearAllIssues()
    {
        _currentIssues.Clear();
        _logger.LogInformation("All issues cleared");
    }

    /// <summary>
    /// Stop periodic heartbeats and clean up resources.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        _isRunning = false;

        if (_heartbeatTimer != null)
        {
            await _heartbeatTimer.DisposeAsync();
            _heartbeatTimer = null;
        }

        _logger.LogDebug("ServiceHeartbeatManager disposed");
    }
}
