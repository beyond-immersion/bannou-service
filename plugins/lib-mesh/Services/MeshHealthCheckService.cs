#nullable enable

using BeyondImmersion.BannouService.Configuration;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Mesh;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Net;

namespace BeyondImmersion.BannouService.Mesh.Services;

/// <summary>
/// Background service that periodically probes registered mesh endpoints
/// with lightweight health requests and updates their status.
/// Proactive failure detection ensures the first real request after an endpoint
/// failure doesn't have to eat the latency penalty.
/// </summary>
public class MeshHealthCheckService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<MeshHealthCheckService> _logger;
    private readonly MeshServiceConfiguration _configuration;
    private readonly HttpMessageInvoker _httpClient;

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

        // CA2000: handler ownership transferred to HttpMessageInvoker
#pragma warning disable CA2000
        var handler = new SocketsHttpHandler
        {
            UseProxy = false,
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.None,
            UseCookies = false,
            ConnectTimeout = TimeSpan.FromSeconds(configuration.HealthCheckTimeoutSeconds)
        };
        _httpClient = new HttpMessageInvoker(handler);
#pragma warning restore CA2000
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_configuration.HealthCheckEnabled)
        {
            _logger.LogInformation("Mesh active health checking is disabled");
            return;
        }

        _logger.LogInformation(
            "Mesh health check service starting, interval: {IntervalSeconds}s, timeout: {TimeoutSeconds}s",
            _configuration.HealthCheckIntervalSeconds, _configuration.HealthCheckTimeoutSeconds);

        // Wait before first check to allow services to register
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
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
                // Only update if status was not already Healthy (avoid unnecessary writes)
                if (endpoint.Status != EndpointStatus.Healthy)
                {
                    _logger.LogInformation(
                        "Endpoint {AppId}@{Host}:{Port} recovered (was {PreviousStatus})",
                        endpoint.AppId, endpoint.Host, endpoint.Port, endpoint.Status);

                    await stateManager.UpdateHeartbeatAsync(
                        endpoint.InstanceId,
                        endpoint.AppId,
                        EndpointStatus.Healthy,
                        endpoint.LoadPercent,
                        endpoint.CurrentConnections,
                        _configuration.HealthCheckIntervalSeconds * 3);
                }
            }
            else
            {
                _logger.LogWarning(
                    "Endpoint {AppId}@{Host}:{Port} returned {StatusCode}",
                    endpoint.AppId, endpoint.Host, endpoint.Port, (int)response.StatusCode);

                await stateManager.UpdateHeartbeatAsync(
                    endpoint.InstanceId,
                    endpoint.AppId,
                    EndpointStatus.Unavailable,
                    endpoint.LoadPercent,
                    endpoint.CurrentConnections,
                    _configuration.HealthCheckIntervalSeconds * 3);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            // Connection failure or timeout - mark as unavailable
            _logger.LogWarning(
                "Endpoint {AppId}@{Host}:{Port} health check failed: {Error}",
                endpoint.AppId, endpoint.Host, endpoint.Port, ex.Message);

            await stateManager.UpdateHeartbeatAsync(
                endpoint.InstanceId,
                endpoint.AppId,
                EndpointStatus.Unavailable,
                endpoint.LoadPercent,
                endpoint.CurrentConnections,
                _configuration.HealthCheckIntervalSeconds * 3);
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
