using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Plugins;
using Dapr.Client;
using Microsoft.Extensions.Logging;
using System.Reflection;

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
    private readonly HttpClient _httpClient;

    private Timer? _heartbeatTimer;
    private bool _isRunning;
    private readonly List<string> _currentIssues = new();

    /// <summary>
    /// Dapr HTTP port for metadata API queries.
    /// </summary>
    private readonly int _daprHttpPort;

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
    /// Whether to re-register permissions on each periodic heartbeat.
    /// Default is true. Configurable via PERMISSION_HEARTBEAT_ENABLED environment variable.
    /// This ensures late-joining permission services receive all API mappings.
    /// </summary>
    public bool PermissionHeartbeatEnabled { get; }

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
        _httpClient = new HttpClient();

        // Resolve app-id from environment
        AppId = Environment.GetEnvironmentVariable("DAPR_APP_ID")
            ?? Environment.GetEnvironmentVariable("APP_ID")
            ?? AppConstants.DEFAULT_APP_NAME;

        // Get Dapr HTTP port (default 3500)
        var daprPortStr = Environment.GetEnvironmentVariable("DAPR_HTTP_PORT");
        _daprHttpPort = int.TryParse(daprPortStr, out var port) && port > 0 ? port : 3500;

        // Get configurable heartbeat interval (default 30 seconds)
        var intervalStr = Environment.GetEnvironmentVariable("HEARTBEAT_INTERVAL_SECONDS");
        HeartbeatIntervalSeconds = int.TryParse(intervalStr, out var interval) && interval > 0
            ? interval
            : 30;

        // Get permission heartbeat setting (default true - re-register permissions on each heartbeat)
        var permHeartbeatStr = Environment.GetEnvironmentVariable("PERMISSION_HEARTBEAT_ENABLED");
        PermissionHeartbeatEnabled = string.IsNullOrEmpty(permHeartbeatStr) ||
            !bool.TryParse(permHeartbeatStr, out var permEnabled) ||
            permEnabled;

        _logger.LogInformation(
            "ServiceHeartbeatManager initialized: InstanceId={InstanceId}, AppId={AppId}, Interval={Interval}s, PermissionHeartbeat={PermEnabled}",
            InstanceId, AppId, HeartbeatIntervalSeconds, PermissionHeartbeatEnabled);
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
    /// This blocks startup until Dapr sidecar is ready to send AND receive events.
    /// Verifies both publishing capability and subscription registration.
    /// </summary>
    /// <param name="maxRetries">Maximum number of retry attempts</param>
    /// <param name="retryDelayMs">Delay between retries in milliseconds</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if Dapr is ready (publish + subscriptions), false if all retries exhausted</returns>
    public async Task<bool> WaitForDaprConnectivityAsync(
        int maxRetries = 30,
        int retryDelayMs = 2000,
        CancellationToken cancellationToken = default)
    {
        // Discover expected subscriptions from plugin assemblies
        var expectedSubscriptions = DiscoverExpectedSubscriptions();
        _logger.LogInformation(
            "Waiting for Dapr connectivity (max {MaxRetries} attempts, {Delay}ms between retries). Expected subscriptions: [{Subscriptions}]",
            maxRetries, retryDelayMs, string.Join(", ", expectedSubscriptions));

        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                // Step 1: Attempt to publish startup heartbeat (proves outbound works)
                var publishSuccess = await PublishStartupHeartbeatAsync(cancellationToken);
                if (!publishSuccess)
                {
                    _logger.LogWarning("Dapr publish check failed on attempt {Attempt}", attempt);
                    if (attempt < maxRetries)
                        await Task.Delay(retryDelayMs, cancellationToken);
                    continue;
                }

                // Step 2: Verify subscriptions are registered (proves inbound works)
                if (expectedSubscriptions.Count == 0)
                {
                    // No subscriptions expected - publish success is enough
                    _logger.LogInformation("✅ Dapr connectivity confirmed on attempt {Attempt} (no subscriptions expected)", attempt);
                    return true;
                }

                var subscriptionsReady = await VerifySubscriptionsRegisteredAsync(expectedSubscriptions, cancellationToken);
                if (subscriptionsReady)
                {
                    _logger.LogInformation("✅ Dapr connectivity confirmed on attempt {Attempt} (publish + subscriptions ready)", attempt);
                    return true;
                }

                _logger.LogWarning(
                    "Dapr publish succeeded but subscriptions not ready on attempt {Attempt}/{MaxRetries}",
                    attempt, maxRetries);
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
    /// Discover expected pub/sub subscriptions by scanning plugin assemblies for [Topic] attributes.
    /// </summary>
    /// <returns>Set of topic names that should be registered with Dapr</returns>
    private HashSet<string> DiscoverExpectedSubscriptions()
    {
        var subscriptions = new HashSet<string>();

        foreach (var plugin in _pluginLoader.EnabledPlugins)
        {
            try
            {
                var assembly = plugin.GetType().Assembly;

                // Scan all types in the assembly for [Topic] attributes on methods
                foreach (var type in assembly.GetTypes())
                {
                    foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance))
                    {
                        // Check for Dapr.TopicAttribute
                        var topicAttr = method.GetCustomAttributes()
                            .FirstOrDefault(a => a.GetType().FullName == "Dapr.TopicAttribute");

                        if (topicAttr != null)
                        {
                            // Get the topic name from the attribute (second constructor parameter)
                            var topicProp = topicAttr.GetType().GetProperty("Name");
                            var topic = topicProp?.GetValue(topicAttr) as string;

                            if (!string.IsNullOrEmpty(topic))
                            {
                                subscriptions.Add(topic);
                                _logger.LogDebug("Discovered subscription topic '{Topic}' from {Type}.{Method}",
                                    topic, type.Name, method.Name);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error scanning plugin {Plugin} for subscriptions", plugin.PluginName);
            }
        }

        return subscriptions;
    }

    /// <summary>
    /// Verify that expected subscriptions are registered with Dapr by querying the metadata API.
    /// </summary>
    /// <param name="expectedTopics">Set of topic names that should be registered</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if all expected subscriptions are registered, false otherwise</returns>
    private async Task<bool> VerifySubscriptionsRegisteredAsync(
        HashSet<string> expectedTopics,
        CancellationToken cancellationToken)
    {
        try
        {
            // Query Dapr metadata API
            var metadataUrl = $"http://localhost:{_daprHttpPort}/v1.0/metadata";
            var response = await _httpClient.GetAsync(metadataUrl, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to query Dapr metadata API: {StatusCode}", response.StatusCode);
                return false;
            }

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            var metadata = System.Text.Json.JsonSerializer.Deserialize<DaprMetadataResponse>(content, new System.Text.Json.JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (metadata?.Subscriptions == null || metadata.Subscriptions.Count == 0)
            {
                _logger.LogDebug("Dapr metadata shows no subscriptions registered yet");
                return false;
            }

            // Check if all expected topics are in the registered subscriptions
            var registeredTopics = metadata.Subscriptions
                .Select(s => s.Topic)
                .Where(t => !string.IsNullOrEmpty(t))
                .ToHashSet();

            var missingTopics = expectedTopics.Except(registeredTopics).ToList();

            if (missingTopics.Count > 0)
            {
                _logger.LogDebug("Missing subscriptions: [{Missing}]. Registered: [{Registered}]",
                    string.Join(", ", missingTopics),
                    string.Join(", ", registeredTopics));
                return false;
            }

            _logger.LogInformation("✅ All expected subscriptions registered: [{Topics}]",
                string.Join(", ", expectedTopics));
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error verifying Dapr subscriptions");
            return false;
        }
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

            await _daprClient.PublishEventAsync(
                PUBSUB_NAME,
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

        _httpClient.Dispose();
        _logger.LogDebug("ServiceHeartbeatManager disposed");
    }
}

/// <summary>
/// Response model for Dapr metadata API (/v1.0/metadata).
/// Only includes fields needed for subscription verification.
/// </summary>
internal class DaprMetadataResponse
{
    /// <summary>
    /// List of registered pub/sub subscriptions.
    /// </summary>
    public List<DaprSubscriptionInfo>? Subscriptions { get; set; }
}

/// <summary>
/// Subscription information from Dapr metadata.
/// </summary>
internal class DaprSubscriptionInfo
{
    /// <summary>
    /// The pub/sub component name (e.g., "bannou-pubsub").
    /// </summary>
    public string? PubsubName { get; set; }

    /// <summary>
    /// The topic name (e.g., "account.deleted").
    /// </summary>
    public string? Topic { get; set; }

    /// <summary>
    /// The subscription type (PROGRAMMATIC or DECLARATIVE).
    /// </summary>
    public string? Type { get; set; }
}
