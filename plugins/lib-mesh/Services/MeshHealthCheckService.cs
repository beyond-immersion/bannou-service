#nullable enable

using BeyondImmersion.BannouService.Configuration;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Mesh;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Net;

namespace BeyondImmersion.BannouService.Mesh.Services;

/// <summary>
/// Background service that periodically probes registered mesh endpoints
/// with lightweight health requests and updates their status.
/// Proactive failure detection ensures the first real request after an endpoint
/// failure doesn't have to eat the latency penalty.
/// After consecutive failures (configurable via HealthCheckFailureThreshold),
/// the endpoint is deregistered and a deregistration event is published.
/// </summary>
public class MeshHealthCheckService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<MeshHealthCheckService> _logger;
    private readonly MeshServiceConfiguration _configuration;
    private readonly HttpMessageInvoker _httpClient;

    /// <summary>
    /// Tracks consecutive health check failures per endpoint.
    /// Reset on successful probe, removed on deregistration.
    /// </summary>
    private readonly ConcurrentDictionary<Guid, int> _failureCounters = new();

    /// <summary>
    /// Cache for health check failure event deduplication.
    /// Key = instanceId.ToString(), Value = last publish time.
    /// Follows lib-state deduplication pattern per IMPLEMENTATION TENETS.
    /// </summary>
    private readonly ConcurrentDictionary<string, DateTimeOffset> _healthCheckEventDeduplicationCache = new();

    /// <summary>
    /// Creates a new MeshHealthCheckService.
    /// </summary>
    public MeshHealthCheckService(
        IServiceProvider serviceProvider,
        ILogger<MeshHealthCheckService> logger,
        MeshServiceConfiguration configuration)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _configuration = configuration;

        SocketsHttpHandler? handler = null;
        try
        {
            handler = new SocketsHttpHandler
            {
                UseProxy = false,
                AllowAutoRedirect = false,
                AutomaticDecompression = DecompressionMethods.None,
                UseCookies = false,
                ConnectTimeout = TimeSpan.FromSeconds(configuration.HealthCheckTimeoutSeconds)
            };
            _httpClient = new HttpMessageInvoker(handler);
            handler = null; // Ownership transferred to HttpMessageInvoker
        }
        finally
        {
            handler?.Dispose(); // Only executes if ownership transfer failed
        }
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_configuration.HealthCheckEnabled)
        {
            _logger.LogInformation("Mesh active health checking is disabled");
            return;
        }

        var deregistrationStatus = _configuration.HealthCheckFailureThreshold > 0
            ? $"deregister after {_configuration.HealthCheckFailureThreshold} failures"
            : "deregistration disabled";
        _logger.LogInformation(
            "Mesh health check service starting, interval: {IntervalSeconds}s, timeout: {TimeoutSeconds}s, {DeregistrationStatus}",
            _configuration.HealthCheckIntervalSeconds, _configuration.HealthCheckTimeoutSeconds, deregistrationStatus);

        // Wait before first check to allow services to register
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(_configuration.HealthCheckStartupDelaySeconds), stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProbeAllEndpointsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during mesh health check cycle");
                await TryPublishErrorAsync(ex, stoppingToken);
            }

            try
            {
                await Task.Delay(
                    TimeSpan.FromSeconds(_configuration.HealthCheckIntervalSeconds),
                    stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        _logger.LogInformation("Mesh health check service stopped");
    }

    /// <summary>
    /// Probes all registered endpoints and updates their health status.
    /// </summary>
    private async Task ProbeAllEndpointsAsync(CancellationToken cancellationToken)
    {
        var stateManager = _serviceProvider.GetRequiredService<IMeshStateManager>();
        var endpoints = await stateManager.GetAllEndpointsAsync();

        if (endpoints.Count == 0)
        {
            _logger.LogDebug("No registered endpoints to health check");
            return;
        }

        _logger.LogDebug("Probing {Count} registered endpoints", endpoints.Count);

        var tasks = endpoints.Select(endpoint => ProbeEndpointAsync(endpoint, stateManager, cancellationToken));
        await Task.WhenAll(tasks);
    }

    /// <summary>
    /// Probes a single endpoint and updates its status in state.
    /// Tracks consecutive failures and deregisters after threshold is exceeded.
    /// </summary>
    private async Task ProbeEndpointAsync(
        MeshEndpoint endpoint,
        IMeshStateManager stateManager,
        CancellationToken cancellationToken)
    {
        // Skip endpoints that are shutting down (intentional, not a health issue)
        if (endpoint.Status == EndpointStatus.ShuttingDown)
        {
            return;
        }

        var scheme = endpoint.Port == 443 ? "https" : "http";
        var healthUrl = $"{scheme}://{endpoint.Host}:{endpoint.Port}/health";

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(_configuration.HealthCheckTimeoutSeconds));

            using var request = new HttpRequestMessage(HttpMethod.Get, healthUrl);
            using var response = await _httpClient.SendAsync(request, timeoutCts.Token);

            if (response.IsSuccessStatusCode)
            {
                // Reset failure counter on successful probe
                _failureCounters.TryRemove(endpoint.InstanceId, out _);

                // Only update if status was not already Healthy (avoid unnecessary writes)
                if (endpoint.Status != EndpointStatus.Healthy)
                {
                    _logger.LogInformation(
                        "Endpoint {AppId}@{Host}:{Port} recovered (was {PreviousStatus})",
                        endpoint.AppId, endpoint.Host, endpoint.Port, endpoint.Status);

                    // Preserve existing issues - health checks don't modify issues
                    await stateManager.UpdateHeartbeatAsync(
                        endpoint.InstanceId,
                        endpoint.AppId,
                        EndpointStatus.Healthy,
                        endpoint.LoadPercent,
                        endpoint.CurrentConnections,
                        endpoint.Issues,
                        _configuration.HealthCheckIntervalSeconds * 3);
                }
            }
            else
            {
                var errorMessage = $"HTTP {(int)response.StatusCode}";
                _logger.LogWarning(
                    "Endpoint {AppId}@{Host}:{Port} returned {StatusCode}",
                    endpoint.AppId, endpoint.Host, endpoint.Port, (int)response.StatusCode);

                await HandleFailedProbeAsync(endpoint, stateManager, errorMessage, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            // Connection failure or timeout
            _logger.LogWarning(
                "Endpoint {AppId}@{Host}:{Port} health check failed: {Error}",
                endpoint.AppId, endpoint.Host, endpoint.Port, ex.Message);

            await HandleFailedProbeAsync(endpoint, stateManager, ex.Message, cancellationToken);
        }
    }

    /// <summary>
    /// Handles a failed health check probe by incrementing failure counter,
    /// publishing failure event, marking as unavailable, and potentially deregistering.
    /// </summary>
    private async Task HandleFailedProbeAsync(
        MeshEndpoint endpoint,
        IMeshStateManager stateManager,
        string? lastError,
        CancellationToken cancellationToken)
    {
        // Increment failure counter
        var failureCount = _failureCounters.AddOrUpdate(
            endpoint.InstanceId,
            1,
            (_, current) => current + 1);

        var threshold = _configuration.HealthCheckFailureThreshold;

        // Publish health check failure event (with deduplication)
        await TryPublishHealthCheckFailedEventAsync(endpoint, failureCount, lastError, cancellationToken);

        // Check if we should deregister (threshold > 0 enables deregistration)
        if (threshold > 0 && failureCount >= threshold)
        {
            _logger.LogWarning(
                "Endpoint {AppId}@{Host}:{Port} reached failure threshold ({FailureCount}/{Threshold}), deregistering",
                endpoint.AppId, endpoint.Host, endpoint.Port, failureCount, threshold);

            // Deregister the endpoint
            var deregistered = await stateManager.DeregisterEndpointAsync(endpoint.InstanceId, endpoint.AppId);

            if (deregistered)
            {
                // Clean up failure counter
                _failureCounters.TryRemove(endpoint.InstanceId, out _);

                // Publish deregistration event
                await PublishDeregistrationEventAsync(
                    endpoint.InstanceId,
                    endpoint.AppId,
                    cancellationToken);
            }
        }
        else
        {
            // Mark as unavailable but keep registered (will expire via TTL if threshold=0)
            await stateManager.UpdateHeartbeatAsync(
                endpoint.InstanceId,
                endpoint.AppId,
                EndpointStatus.Unavailable,
                endpoint.LoadPercent,
                endpoint.CurrentConnections,
                endpoint.Issues,
                _configuration.HealthCheckIntervalSeconds * 3);

            if (threshold > 0)
            {
                _logger.LogDebug(
                    "Endpoint {AppId}@{Host}:{Port} failure count: {FailureCount}/{Threshold}",
                    endpoint.AppId, endpoint.Host, endpoint.Port, failureCount, threshold);
            }
        }
    }

    /// <summary>
    /// Publishes a deregistration event for an endpoint removed due to health check failures.
    /// </summary>
    private async Task PublishDeregistrationEventAsync(
        Guid instanceId,
        string appId,
        CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var messageBus = scope.ServiceProvider.GetRequiredService<IMessageBus>();

            var evt = new MeshEndpointDeregisteredEvent
            {
                EventName = "mesh.endpoint_deregistered",
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                InstanceId = instanceId,
                AppId = appId,
                Reason = MeshEndpointDeregisteredEventReason.HealthCheckFailed
            };

            await messageBus.TryPublishAsync(
                "mesh.endpoint.deregistered",
                evt,
                cancellationToken: cancellationToken);

            _logger.LogInformation(
                "Published deregistration event for endpoint {InstanceId} (app: {AppId}, reason: HealthCheckFailed)",
                instanceId, appId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish deregistration event for endpoint {InstanceId}", instanceId);
        }
    }

    /// <summary>
    /// Publishes a health check failure event if not deduplicated.
    /// Follows lib-state deduplication pattern per IMPLEMENTATION TENETS.
    /// </summary>
    private async Task TryPublishHealthCheckFailedEventAsync(
        MeshEndpoint endpoint,
        int consecutiveFailures,
        string? lastError,
        CancellationToken cancellationToken)
    {
        var dedupKey = endpoint.InstanceId.ToString();
        var windowSeconds = _configuration.HealthCheckEventDeduplicationWindowSeconds;
        var now = DateTimeOffset.UtcNow;

        // Check dedup cache - skip if we published this event recently
        if (_healthCheckEventDeduplicationCache.TryGetValue(dedupKey, out var lastPublished))
        {
            if (now - lastPublished < TimeSpan.FromSeconds(windowSeconds))
            {
                _logger.LogDebug(
                    "Skipping duplicate health check failed event for endpoint {InstanceId} (last published {Seconds:F1}s ago)",
                    endpoint.InstanceId, (now - lastPublished).TotalSeconds);
                return;
            }
        }

        // Update cache before publishing to prevent races
        _healthCheckEventDeduplicationCache[dedupKey] = now;

        try
        {
            using var scope = _serviceProvider.CreateScope();
            var messageBus = scope.ServiceProvider.GetRequiredService<IMessageBus>();

            var evt = new MeshEndpointHealthCheckFailedEvent
            {
                EventName = "mesh.endpoint_health_check_failed",
                EventId = Guid.NewGuid(),
                Timestamp = now,
                InstanceId = endpoint.InstanceId,
                AppId = endpoint.AppId,
                ConsecutiveFailures = consecutiveFailures,
                FailureThreshold = _configuration.HealthCheckFailureThreshold,
                LastError = lastError
            };

            await messageBus.TryPublishAsync(
                "mesh.endpoint.health.failed",
                evt,
                cancellationToken: cancellationToken);

            _logger.LogDebug(
                "Published health check failed event for endpoint {InstanceId} ({Failures}/{Threshold})",
                endpoint.InstanceId, consecutiveFailures, _configuration.HealthCheckFailureThreshold);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish health check failed event for endpoint {InstanceId}", endpoint.InstanceId);
        }
    }

    /// <summary>
    /// Tries to publish an error event for health check failures.
    /// </summary>
    private async Task TryPublishErrorAsync(Exception ex, CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var messageBus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
            await messageBus.TryPublishErrorAsync(
                "mesh",
                "HealthCheck",
                ex.GetType().Name,
                ex.Message,
                severity: ServiceErrorEventSeverity.Error,
                cancellationToken: cancellationToken);
        }
        catch
        {
            // Don't let error publishing failures affect the loop
        }
    }

    /// <inheritdoc/>
    public override void Dispose()
    {
        _httpClient.Dispose();
        base.Dispose();
    }
}
