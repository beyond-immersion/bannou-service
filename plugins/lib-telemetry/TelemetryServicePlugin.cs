using BeyondImmersion.BannouService.Configuration;
using BeyondImmersion.BannouService.Plugins;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.Telemetry.Instrumentation;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace BeyondImmersion.BannouService.Telemetry;

/// <summary>
/// Plugin wrapper for Telemetry service enabling plugin-based discovery and lifecycle management.
/// Configures OpenTelemetry SDK for distributed tracing and metrics export.
/// </summary>
/// <remarks>
/// <para>
/// This plugin MUST load BEFORE lib-state, lib-messaging, and lib-mesh (priority -1)
/// so that ITelemetryProvider is available for injection into those infrastructure libs.
/// </para>
/// </remarks>
public class TelemetryServicePlugin : StandardServicePlugin<ITelemetryService>
{
    /// <inheritdoc/>
    public override string PluginName => "telemetry";

    /// <inheritdoc/>
    public override string DisplayName => "Telemetry Service";

    private HealthTrackingExporter? _healthTrackingExporter;

    /// <summary>
    /// Configure services for dependency injection.
    /// Registers ITelemetryProvider and configures OpenTelemetry SDK.
    /// </summary>
    public override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);

        // Register ITelemetryProvider as Singleton so it's available to all infrastructure libs
        services.AddSingleton<ITelemetryProvider>(sp =>
        {
            var config = sp.GetRequiredService<TelemetryServiceConfiguration>();
            var appConfig = sp.GetRequiredService<AppConfiguration>();
            var logger = sp.GetRequiredService<ILogger<TelemetryProvider>>();

            // Use configured service name, or fall back to effective app-id
            var serviceName = !string.IsNullOrWhiteSpace(config.ServiceName)
                ? config.ServiceName
                : appConfig.EffectiveAppId;

            return new TelemetryProvider(config, serviceName, logger);
        });

        // Configure OpenTelemetry SDK
        ConfigureOpenTelemetry(services);
    }

    /// <summary>
    /// Configure application pipeline - maps Prometheus metrics endpoint.
    /// </summary>
    public override void ConfigureApplication(WebApplication app)
    {
        base.ConfigureApplication(app);

        // Map Prometheus metrics endpoint if metrics are enabled
        var config = app.Services.GetRequiredService<TelemetryServiceConfiguration>();
        if (config.MetricsEnabled)
        {
            // Map Prometheus scraping endpoint at /metrics
            // This exposes all registered meters for Prometheus to scrape
            app.MapPrometheusScrapingEndpoint("/metrics");
            Logger?.LogInformation("Prometheus metrics endpoint mapped at /metrics");
        }

        // Log final configuration summary
        var appConfig = app.Services.GetRequiredService<AppConfiguration>();
        var serviceName = !string.IsNullOrWhiteSpace(config.ServiceName)
            ? config.ServiceName
            : appConfig.EffectiveAppId;

        Logger?.LogInformation(
            "OpenTelemetry finalized: serviceName={ServiceName}, tracing={TracingEnabled}, metrics={MetricsEnabled}, endpoint={Endpoint}",
            serviceName, config.TracingEnabled, config.MetricsEnabled, config.OtlpEndpoint);
    }

    /// <summary>
    /// Configure OpenTelemetry SDK with tracing and metrics exporters.
    /// </summary>
    private void ConfigureOpenTelemetry(IServiceCollection services)
    {
        // Build temporary service provider to access configuration
        // This is the standard pattern for OpenTelemetry SDK setup when config is needed
        using var tempProvider = services.BuildServiceProvider();
        var config = tempProvider.GetService<TelemetryServiceConfiguration>() ?? new TelemetryServiceConfiguration();
        var appConfig = tempProvider.GetService<AppConfiguration>();

        // Determine service name from config or fall back to effective app-id
        var serviceName = !string.IsNullOrWhiteSpace(config.ServiceName)
            ? config.ServiceName
            : appConfig?.EffectiveAppId ?? "bannou";

        Logger?.LogDebug(
            "Configuring OpenTelemetry: serviceName={ServiceName}, tracing={TracingEnabled}, metrics={MetricsEnabled}",
            serviceName, config.TracingEnabled, config.MetricsEnabled);

        var otelBuilder = services.AddOpenTelemetry()
            .ConfigureResource(resource =>
            {
                resource.AddService(
                    serviceName: serviceName,
                    serviceNamespace: config.ServiceNamespace,
                    serviceVersion: "1.0.0");

                // Add deployment environment as resource attribute
                resource.AddAttributes(new[]
                {
                    new KeyValuePair<string, object>("deployment.environment", config.DeploymentEnvironment)
                });
            });

        // Configure tracing if enabled
        if (config.TracingEnabled)
        {
            otelBuilder.WithTracing(tracing =>
            {
                tracing
                    // Configure trace sampling ratio (0.0-1.0)
                    // Use parent-based sampler to respect upstream sampling decisions,
                    // with ratio-based sampling for root spans
                    .SetSampler(new ParentBasedSampler(
                        new TraceIdRatioBasedSampler(config.TracingSamplingRatio)))
                    // Add auto-instrumentation for ASP.NET Core
                    .AddAspNetCoreInstrumentation(options =>
                    {
                        // Filter out health check endpoints to reduce noise
                        options.Filter = context =>
                            !context.Request.Path.StartsWithSegments("/health") &&
                            !context.Request.Path.StartsWithSegments("/telemetry/health") &&
                            !context.Request.Path.StartsWithSegments("/metrics");
                    })
                    // Add auto-instrumentation for HttpClient
                    .AddHttpClientInstrumentation(options =>
                    {
                        // Filter out internal health checks
                        options.FilterHttpRequestMessage = request =>
                            request.RequestUri?.PathAndQuery.Contains("health") != true;
                    })
                    // Add our custom activity sources for infrastructure libs
                    .AddSource(TelemetryComponents.State)
                    .AddSource(TelemetryComponents.Messaging)
                    .AddSource(TelemetryComponents.Mesh)
                    .AddSource(TelemetryComponents.Telemetry)
                    // Configure OTLP exporter wrapped in HealthTrackingExporter for passive health monitoring
                    .AddProcessor(CreateHealthTrackingProcessor(config, services));

                Logger?.LogInformation(
                    "Tracing enabled: endpoint={Endpoint}, protocol={Protocol}, samplingRatio={SamplingRatio}",
                    config.OtlpEndpoint, config.OtlpProtocol, config.TracingSamplingRatio);
            });
        }
        else
        {
            Logger?.LogInformation("Tracing disabled via configuration");
        }

        // Configure metrics if enabled
        if (config.MetricsEnabled)
        {
            otelBuilder.WithMetrics(metrics =>
            {
                metrics
                    // Add ASP.NET Core metrics
                    .AddAspNetCoreInstrumentation()
                    // Add HttpClient metrics
                    .AddHttpClientInstrumentation()
                    // Add our custom meters for infrastructure libs
                    .AddMeter(TelemetryComponents.State)
                    .AddMeter(TelemetryComponents.Messaging)
                    .AddMeter(TelemetryComponents.Mesh)
                    .AddMeter(TelemetryComponents.Telemetry)
                    // Add Prometheus exporter for /metrics endpoint scraping
                    .AddPrometheusExporter();

                Logger?.LogInformation("Metrics enabled with Prometheus exporter");
            });
        }
        else
        {
            Logger?.LogInformation("Metrics disabled via configuration");
        }

        Logger?.LogInformation("OpenTelemetry SDK configuration applied");
    }

    /// <summary>
    /// Creates a BatchExportProcessor with a HealthTrackingExporter wrapping the OtlpTraceExporter.
    /// Registers the HealthTrackingExporter as a singleton for health state queries.
    /// </summary>
    private BaseProcessor<System.Diagnostics.Activity> CreateHealthTrackingProcessor(
        TelemetryServiceConfiguration config,
        IServiceCollection services)
    {
        var otlpExporter = new OtlpTraceExporter(new OtlpExporterOptions
        {
            Endpoint = new Uri(config.OtlpEndpoint),
            Protocol = config.OtlpProtocol == OtlpProtocol.Grpc
                ? OtlpExportProtocol.Grpc
                : OtlpExportProtocol.HttpProtobuf
        });

        // Ownership: BatchActivityExportProcessor owns (and disposes) the HealthTrackingExporter,
        // which in turn owns (and disposes) the OtlpTraceExporter.
        // DI singleton registration is for read-only health state access, not lifecycle.
        _healthTrackingExporter = new HealthTrackingExporter(otlpExporter);
        services.AddSingleton(_healthTrackingExporter);

        return new BatchActivityExportProcessor(_healthTrackingExporter);
    }
}
