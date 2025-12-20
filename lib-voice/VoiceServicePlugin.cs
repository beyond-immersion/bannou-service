using BeyondImmersion.BannouService.Plugins;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.Voice.Clients;
using BeyondImmersion.BannouService.Voice.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Voice;

/// <summary>
/// Plugin wrapper for Voice service enabling plugin-based discovery and lifecycle management.
/// Bridges existing IDaprService implementation with the new Plugin system.
/// </summary>
public class VoiceServicePlugin : BaseBannouPlugin
{
    public override string PluginName => "voice";
    public override string DisplayName => "Voice Service";

    private IVoiceService? _service;
    private IServiceProvider? _serviceProvider;

    /// <summary>
    /// Validate that this plugin should be loaded based on environment configuration.
    /// </summary>
    protected override bool OnValidatePlugin()
    {
        var enabled = Environment.GetEnvironmentVariable("VOICE_SERVICE_ENABLED")?.ToLower();
        Logger?.LogDebug("Voice service enabled check: {EnabledValue}", enabled);
        return enabled == "true";
    }

    /// <summary>
    /// Configure services for dependency injection - mimics existing [DaprService] registration.
    /// </summary>
    public override void ConfigureServices(IServiceCollection services)
    {
        if (!OnValidatePlugin())
        {
            Logger?.LogInformation("Voice service disabled, skipping service registration");
            return;
        }

        Logger?.LogInformation("Configuring Voice service dependencies");

        // Register the service implementation (existing pattern from [DaprService] attribute)
        services.AddScoped<IVoiceService, VoiceService>();
        services.AddScoped<VoiceService>();

        // Register generated configuration class
        services.AddScoped<VoiceServiceConfiguration>();

        // Register helper services for P2P voice coordination
        services.AddScoped<ISipEndpointRegistry, SipEndpointRegistry>();
        services.AddScoped<IP2PCoordinator, P2PCoordinator>();
        Logger?.LogDebug("Registered Voice helper services (SipEndpointRegistry, P2PCoordinator)");

        // Register scaled tier coordinator and clients for SFU-based conferencing
        services.AddScoped<IScaledTierCoordinator, ScaledTierCoordinator>();

        // Register Kamailio and RTPEngine clients with configuration-driven settings
        // These are singleton because they manage long-lived connections
        services.AddSingleton<IKamailioClient>(sp =>
        {
            var config = sp.GetRequiredService<VoiceServiceConfiguration>();
            var logger = sp.GetRequiredService<ILogger<KamailioClient>>();
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient("Kamailio");
            return new KamailioClient(
                httpClient,
                config.KamailioHost ?? "localhost",
                config.KamailioRpcPort > 0 ? config.KamailioRpcPort : 5080,
                logger);
        });

        services.AddSingleton<IRtpEngineClient>(sp =>
        {
            var config = sp.GetRequiredService<VoiceServiceConfiguration>();
            var logger = sp.GetRequiredService<ILogger<RtpEngineClient>>();
            return new RtpEngineClient(
                config.RtpEngineHost ?? "localhost",
                config.RtpEnginePort > 0 ? config.RtpEnginePort : 22222,
                logger);
        });
        Logger?.LogDebug("Registered Voice scaled tier services (ScaledTierCoordinator, KamailioClient, RtpEngineClient)");

        // Add any service-specific dependencies
        // The generated clients should already be registered by AddAllBannouServiceClients()

        Logger?.LogInformation("Voice service dependencies configured");
    }

    /// <summary>
    /// Configure application pipeline - handles controller registration.
    /// </summary>
    public override void ConfigureApplication(WebApplication app)
    {
        if (!OnValidatePlugin())
        {
            Logger?.LogInformation("Voice service disabled, skipping application configuration");
            return;
        }

        Logger?.LogInformation("Configuring Voice service application pipeline");

        // The generated VoiceController should already be discovered via standard ASP.NET Core controller discovery
        // since we're not excluding the assembly like we did with IDaprController approach

        // Store service provider for lifecycle management
        _serviceProvider = app.Services;

        Logger?.LogInformation("Voice service application pipeline configured");
    }

    /// <summary>
    /// Start the service - calls existing IDaprService lifecycle if present.
    /// </summary>
    protected override async Task<bool> OnStartAsync()
    {
        if (!OnValidatePlugin()) return true;

        Logger?.LogInformation("Starting Voice service");

        try
        {
            // Get service instance from DI container with proper scope handling
            // Note: CreateScope() is required for Scoped services to avoid "Cannot resolve scoped service from root provider" error
            using var scope = _serviceProvider?.CreateScope();
            _service = scope?.ServiceProvider.GetService<IVoiceService>();

            if (_service == null)
            {
                Logger?.LogError("Failed to resolve IVoiceService from DI container");
                return false;
            }

            // Call existing IDaprService.OnStartAsync if the service implements it
            if (_service is IDaprService daprService)
            {
                Logger?.LogDebug("Calling IDaprService.OnStartAsync for Voice service");
                await daprService.OnStartAsync(CancellationToken.None);
            }

            Logger?.LogInformation("Voice service started successfully");
            return true;
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "Failed to start Voice service");
            return false;
        }
    }

    /// <summary>
    /// Running phase - calls existing IDaprService lifecycle if present.
    /// </summary>
    protected override async Task OnRunningAsync()
    {
        if (!OnValidatePlugin() || _service == null) return;

        Logger?.LogDebug("Voice service running");

        try
        {
            // Call existing IDaprService.OnRunningAsync if the service implements it
            if (_service is IDaprService daprService)
            {
                Logger?.LogDebug("Calling IDaprService.OnRunningAsync for Voice service");
                await daprService.OnRunningAsync(CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            Logger?.LogWarning(ex, "Exception during Voice service running phase");
        }
    }

    /// <summary>
    /// Shutdown the service - calls existing IDaprService lifecycle if present.
    /// </summary>
    protected override async Task OnShutdownAsync()
    {
        if (!OnValidatePlugin() || _service == null) return;

        Logger?.LogInformation("Shutting down Voice service");

        try
        {
            // Call existing IDaprService.OnShutdownAsync if the service implements it
            if (_service is IDaprService daprService)
            {
                Logger?.LogDebug("Calling IDaprService.OnShutdownAsync for Voice service");
                await daprService.OnShutdownAsync();
            }

            Logger?.LogInformation("Voice service shutdown complete");
        }
        catch (Exception ex)
        {
            Logger?.LogWarning(ex, "Exception during Voice service shutdown");
        }
    }
}
