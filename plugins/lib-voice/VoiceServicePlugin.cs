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
