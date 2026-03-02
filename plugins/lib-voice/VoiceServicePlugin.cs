using BeyondImmersion.BannouService.Plugins;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.Voice.Clients;
using BeyondImmersion.BannouService.Voice.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Voice;

/// <summary>
/// Plugin wrapper for Voice service enabling plugin-based discovery and lifecycle management.
/// </summary>
public class VoiceServicePlugin : StandardServicePlugin<IVoiceService>
{
    public override string PluginName => "voice";
    public override string DisplayName => "Voice Service";

    public override void ConfigureServices(IServiceCollection services)
    {
        Logger?.LogInformation("Configuring Voice service dependencies");

        // Register helper services for P2P voice coordination
        // These are Singleton because they maintain local caches for multi-instance safety (FOUNDATION TENETS)
        services.AddSingleton<ISipEndpointRegistry, SipEndpointRegistry>();
        services.AddSingleton<IP2PCoordinator, P2PCoordinator>();
        Logger?.LogDebug("Registered Voice helper services (SipEndpointRegistry, P2PCoordinator)");

        // Register scaled tier coordinator and clients for SFU-based conferencing
        services.AddSingleton<IScaledTierCoordinator, ScaledTierCoordinator>();

        // Register RTPEngine client with configuration-driven settings
        services.AddSingleton<IRtpEngineClient>(sp =>
        {
            var config = sp.GetRequiredService<VoiceServiceConfiguration>();
            var logger = sp.GetRequiredService<ILogger<RtpEngineClient>>();
            var messageBus = sp.GetRequiredService<IMessageBus>();
            var telemetryProvider = sp.GetRequiredService<ITelemetryProvider>();
            return new RtpEngineClient(config.RtpEngineHost, config.RtpEnginePort, logger, messageBus, telemetryProvider, timeoutSeconds: config.RtpEngineTimeoutSeconds);
        });
        Logger?.LogDebug("Registered Voice scaled tier services (ScaledTierCoordinator, RtpEngineClient)");

        // Register background worker for participant eviction, empty room cleanup, and consent timeout
        services.AddHostedService<ParticipantEvictionWorker>();
        Logger?.LogDebug("Registered ParticipantEvictionWorker background service");

        Logger?.LogInformation("Voice service dependencies configured");
    }
}
