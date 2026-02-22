using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Configuration;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("lib-telemetry.tests")]

namespace BeyondImmersion.BannouService.Telemetry;

/// <summary>
/// Implementation of the Telemetry service.
/// Provides health and status endpoints for observability configuration.
/// </summary>
/// <remarks>
/// <para>
/// <b>FOUNDATION TENETS - PARTIAL CLASS REQUIRED:</b> This class MUST remain a partial class.
/// Generated code (event handlers, permissions) is placed in companion partial classes.
/// </para>
/// <para>
/// The telemetry service is unique in that it does not use state stores or publish events.
/// It provides configuration status for the OpenTelemetry instrumentation that is injected
/// into other infrastructure libs (lib-state, lib-messaging, lib-mesh).
/// </para>
/// </remarks>
[BannouService("telemetry", typeof(ITelemetryService), lifetime: ServiceLifetime.Scoped, layer: ServiceLayer.Infrastructure)]
public partial class TelemetryService : ITelemetryService
{
    private readonly ILogger<TelemetryService> _logger;
    private readonly TelemetryServiceConfiguration _configuration;
    private readonly AppConfiguration _appConfiguration;
    private readonly ITelemetryProvider _telemetryProvider;

    /// <summary>
    /// Creates a new TelemetryService instance.
    /// </summary>
    /// <param name="logger">Logger instance.</param>
    /// <param name="configuration">Telemetry service configuration.</param>
    /// <param name="appConfiguration">Application configuration for effective app-id.</param>
    /// <param name="telemetryProvider">Telemetry provider for span instrumentation.</param>
    public TelemetryService(
        ILogger<TelemetryService> logger,
        TelemetryServiceConfiguration configuration,
        AppConfiguration appConfiguration,
        ITelemetryProvider telemetryProvider)
    {
        _logger = logger;
        _configuration = configuration;
        _appConfiguration = appConfiguration;
        _telemetryProvider = telemetryProvider;
    }

    /// <summary>
    /// Check telemetry exporter health.
    /// </summary>
    /// <param name="body">Health request (empty object).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Health status response.</returns>
    public async Task<(StatusCodes, TelemetryHealthResponse?)> HealthAsync(
        TelemetryHealthRequest body,
        CancellationToken cancellationToken = default)
    {
        using var activity = _telemetryProvider.StartActivity(
            TelemetryComponents.Telemetry, "health");

        _logger.LogDebug("Health check requested");

        var response = new TelemetryHealthResponse
        {
            TracingEnabled = _configuration.TracingEnabled,
            MetricsEnabled = _configuration.MetricsEnabled,
            OtlpEndpoint = (_configuration.TracingEnabled || _configuration.MetricsEnabled)
                ? _configuration.OtlpEndpoint
                : null
        };

        await Task.CompletedTask;
        return (StatusCodes.OK, response);
    }

    /// <summary>
    /// Get telemetry status and configuration.
    /// </summary>
    /// <param name="body">Status request (empty object).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Status response with configuration details.</returns>
    public async Task<(StatusCodes, TelemetryStatusResponse?)> StatusAsync(
        TelemetryStatusRequest body,
        CancellationToken cancellationToken = default)
    {
        using var activity = _telemetryProvider.StartActivity(
            TelemetryComponents.Telemetry, "status");

        _logger.LogDebug("Status requested");

        // Use configured service name, or fall back to effective app-id
        var serviceName = !string.IsNullOrWhiteSpace(_configuration.ServiceName)
            ? _configuration.ServiceName
            : _appConfiguration.EffectiveAppId;

        var response = new TelemetryStatusResponse
        {
            TracingEnabled = _configuration.TracingEnabled,
            MetricsEnabled = _configuration.MetricsEnabled,
            SamplingRatio = _configuration.TracingSamplingRatio,
            ServiceName = serviceName,
            ServiceNamespace = _configuration.ServiceNamespace,
            DeploymentEnvironment = _configuration.DeploymentEnvironment,
            OtlpEndpoint = _configuration.OtlpEndpoint,
            OtlpProtocol = _configuration.OtlpProtocol
        };

        await Task.CompletedTask;
        return (StatusCodes.OK, response);
    }
}
