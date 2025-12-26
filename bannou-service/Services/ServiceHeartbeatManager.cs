using BeyondImmersion.BannouService.Configuration;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Plugins;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace BeyondImmersion.BannouService.Services;

/// <summary>
/// Manages heartbeat publishing for all services in a bannou instance.
/// Publishes aggregated heartbeats via message bus on startup and periodically.
/// </summary>
public class ServiceHeartbeatManager : IAsyncDisposable
{
    private readonly IMessageBus _messageBus;
    private readonly ILogger<ServiceHeartbeatManager> _logger;
    private readonly PluginLoader _pluginLoader;
    private readonly IServiceAppMappingResolver _mappingResolver;

    private Timer? _heartbeatTimer;
    private bool _isRunning;
    private readonly List<string> _currentIssues = new();

    /// <summary>
    /// Services that are suppressed from heartbeats because they are routed to a different app-id.
    /// When orchestrator routes a service elsewhere, this instance should stop heartbeating for it.
    /// </summary>
    private readonly ConcurrentDictionary<string, string> _suppressedServices = new();

    /// <summary>
    /// Unique instance identifier for this bannou application instance.
    /// Used for log correlation across distributed systems.
    /// </summary>
    public Guid InstanceId { get; } = Guid.NewGuid();

    /// <summary>
    /// The app-id for this instance. Resolved from configuration.
    /// </summary>
    public string AppId { get; }

    /// <summary>
    /// Heartbeat interval in seconds. Default is 30 seconds.
    /// Configurable via HEARTBEAT_INTERVAL_SECONDS environment variable.
    /// </summary>
    public int HeartbeatIntervalSeconds { get; }

    /// <summary>
    /// Whether to re-register permissions on each periodic heartbeat.
    /// Default is true. Configurable via PERMISSION_HEARTBEAT_ENABLED environment variable.
    /// This ensures late-joining permission services receive all API mappings.
    /// </summary>
    public bool PermissionHeartbeatEnabled { get; }

    /// <summary>
    /// The topic name for heartbeat events.
    /// </summary>
    private const string HEARTBEAT_TOPIC = "bannou-service-heartbeats";

    /// <inheritdoc/>
    public ServiceHeartbeatManager(
        IMessageBus messageBus,
        ILogger<ServiceHeartbeatManager> logger,
        PluginLoader pluginLoader,
        IServiceAppMappingResolver mappingResolver,
        AppConfiguration configuration)
    {
        _messageBus = messageBus ?? throw new ArgumentNullException(nameof(messageBus));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _pluginLoader = pluginLoader ?? throw new ArgumentNullException(nameof(pluginLoader));
        _mappingResolver = mappingResolver ?? throw new ArgumentNullException(nameof(mappingResolver));
        ArgumentNullException.ThrowIfNull(configuration);

        // Subscribe to mapping changes to suppress/resume heartbeats for services routed elsewhere
        _mappingResolver.MappingChanged += OnMappingChanged;

        // Resolve app-id from configuration
        AppId = configuration.BannouAppId ?? AppConstants.DEFAULT_APP_NAME;

        // Get heartbeat settings from configuration (Tenet 21 compliant)
        HeartbeatIntervalSeconds = configuration.HeartbeatIntervalSeconds > 0
            ? configuration.HeartbeatIntervalSeconds
            : 30;
        PermissionHeartbeatEnabled = configuration.PermissionHeartbeatEnabled;

        _logger.LogInformation(
            "ServiceHeartbeatManager initialized: InstanceId={InstanceId}, AppId={AppId}, Interval={Interval}s, PermissionHeartbeat={PermEnabled}",
            InstanceId, AppId, HeartbeatIntervalSeconds, PermissionHeartbeatEnabled);
    }

    /// <summary>
    /// Publish startup heartbeats for all enabled services.
    /// This serves as both a heartbeat announcement and message bus connectivity check.
    /// Should be called after all plugins are initialized and started.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if heartbeat published successfully, false otherwise</returns>
    public async Task<bool> PublishStartupHeartbeatAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Publishing startup heartbeat for {Count} enabled services...",
            _pluginLoader.EnabledPlugins.Count);

        try
        {
            var heartbeat = BuildHeartbeatEvent(ServiceHeartbeatEventStatus.Healthy);

            await _messageBus.PublishAsync(
                HEARTBEAT_TOPIC,
                heartbeat,
                cancellationToken: cancellationToken);

            _logger.LogInformation(
                "✅ Startup heartbeat published successfully: AppId={AppId}, Services=[{Services}]",
                AppId,
                string.Join(", ", heartbeat.Services.Select(s => s.ServiceName)));

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to publish startup heartbeat - message bus may not be ready");
            return false;
        }
    }

    /// <summary>
    /// Wait for message bus connectivity by attempting to publish heartbeats with retries.
    /// This blocks startup until the message bus is ready.
    /// </summary>
    /// <param name="maxRetries">Maximum number of retry attempts</param>
    /// <param name="retryDelayMs">Delay between retries in milliseconds</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if message bus is ready, false if all retries exhausted</returns>
    public async Task<bool> WaitForConnectivityAsync(
        int maxRetries = 30,
        int retryDelayMs = 2000,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Waiting for message bus connectivity (max {MaxRetries} attempts, {Delay}ms between retries)",
            maxRetries, retryDelayMs);

        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                var publishSuccess = await PublishStartupHeartbeatAsync(cancellationToken);
                if (publishSuccess)
                {
                    _logger.LogInformation("✅ Message bus connectivity confirmed on attempt {Attempt}", attempt);
                    return true;
                }

                _logger.LogWarning("Message bus publish check failed on attempt {Attempt}", attempt);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Connectivity check cancelled");
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    "Connectivity attempt {Attempt}/{MaxRetries} failed: {Error}",
                    attempt, maxRetries, ex.Message);
            }

            if (attempt < maxRetries)
            {
                await Task.Delay(retryDelayMs, cancellationToken);
            }
        }

        _logger.LogError("❌ Message bus connectivity check failed after {MaxRetries} attempts", maxRetries);
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
    /// Also re-registers permissions if enabled (solves late-subscriber and race condition issues).
    /// </summary>
    private async Task PublishPeriodicHeartbeatAsync()
    {
        try
        {
            var heartbeat = BuildHeartbeatEvent(DetermineOverallStatus());

            await _messageBus.PublishAsync(
                HEARTBEAT_TOPIC,
                heartbeat);

            _logger.LogDebug(
                "Periodic heartbeat published: AppId={AppId}, Status={Status}, Services={Count}",
                AppId, heartbeat.Status, heartbeat.Services.Count);

            // Re-register permissions on each heartbeat to handle late-joining permission services
            // and ensure eventual consistency after startup race conditions
            if (PermissionHeartbeatEnabled)
            {
                await ReRegisterPermissionsAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish periodic heartbeat");
            ReportIssue($"Heartbeat publish failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Re-register permissions for all services as part of periodic heartbeat.
    /// This ensures late-joining permission services receive API mappings.
    /// </summary>
    private async Task ReRegisterPermissionsAsync()
    {
        try
        {
            _logger.LogDebug("Re-registering permissions as part of heartbeat...");
            var success = await _pluginLoader.RegisterServicePermissionsAsync();
            if (success)
            {
                _logger.LogDebug("Permission heartbeat completed successfully");
            }
            else
            {
                _logger.LogWarning("Permission heartbeat completed with errors");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to re-register permissions during heartbeat (non-fatal)");
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

            await _messageBus.PublishAsync(
                HEARTBEAT_TOPIC,
                heartbeat,
                cancellationToken: cancellationToken);

            _logger.LogInformation("Shutdown heartbeat published successfully");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish shutdown heartbeat (continuing shutdown anyway)");
        }
    }

    /// <summary>
    /// Handle service mapping changes from orchestrator.
    /// When a service is routed to a different app-id, suppress heartbeats for it.
    /// When routed back to this instance (or removed = reverts to default), resume heartbeats.
    /// </summary>
    private void OnMappingChanged(object? sender, ServiceMappingChangedEventArgs e)
    {
        var serviceName = e.ServiceName;
        var newAppId = e.NewAppId ?? AppConstants.DEFAULT_APP_NAME; // null means reverted to default

        // Check if the service is routed to this instance or elsewhere
        var routedToUs = string.Equals(newAppId, AppId, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(newAppId, AppConstants.DEFAULT_APP_NAME, StringComparison.OrdinalIgnoreCase);

        if (routedToUs)
        {
            // Service is routed to us - resume heartbeating
            if (_suppressedServices.TryRemove(serviceName, out var previousAppId))
            {
                _logger.LogInformation(
                    "Resumed heartbeats for service {ServiceName} (routed back to {AppId} from {PreviousAppId})",
                    serviceName, AppId, previousAppId);
            }
        }
        else
        {
            // Service is routed elsewhere - suppress heartbeats
            _suppressedServices[serviceName] = newAppId;
            _logger.LogInformation(
                "Suppressed heartbeats for service {ServiceName} (routed to {NewAppId}, not {AppId})",
                serviceName, newAppId, AppId);
        }
    }

    /// <summary>
    /// Check if a service should be included in heartbeats.
    /// Returns false if the service is routed to a different app-id.
    /// </summary>
    private bool ShouldHeartbeatService(string serviceName)
    {
        return !_suppressedServices.ContainsKey(serviceName);
    }

    /// <summary>
    /// Build the heartbeat event with all service statuses.
    /// Filters out services that are routed to a different app-id.
    /// </summary>
    private ServiceHeartbeatEvent BuildHeartbeatEvent(ServiceHeartbeatEventStatus overallStatus)
    {
        var serviceStatuses = new List<ServiceStatus>();

        // Collect heartbeat data from each resolved service (excluding suppressed services)
        foreach (var plugin in _pluginLoader.EnabledPlugins)
        {
            // Skip services that are routed to a different app-id
            if (!ShouldHeartbeatService(plugin.PluginName))
            {
                _logger.LogDebug(
                    "Skipping heartbeat for service {ServiceName} (routed to different app-id)",
                    plugin.PluginName);
                continue;
            }

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
                    "Plugin {PluginName} has no resolved service (may not implement IBannouService)",
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
    /// Returns the worst status among all services (excluding suppressed services).
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
            // Skip services that are routed to a different app-id
            if (!ShouldHeartbeatService(plugin.PluginName))
            {
                continue;
            }

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
