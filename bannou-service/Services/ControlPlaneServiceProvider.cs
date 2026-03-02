using BeyondImmersion.BannouService.Configuration;
using BeyondImmersion.BannouService.Orchestrator;
using BeyondImmersion.BannouService.Plugins;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Services;

/// <summary>
/// Provides control plane service information by querying the PluginLoader.
/// Registered as a singleton service for use by the orchestrator.
/// </summary>
public class ControlPlaneServiceProvider : IControlPlaneServiceProvider
{
    private readonly ILogger<ControlPlaneServiceProvider> _logger;
    private readonly AppConfiguration _appConfiguration;

    /// <summary>
    /// Creates a new control plane service provider.
    /// </summary>
    public ControlPlaneServiceProvider(
        ILogger<ControlPlaneServiceProvider> logger,
        AppConfiguration appConfiguration)
    {
        _logger = logger;
        _appConfiguration = appConfiguration;
    }

    /// <inheritdoc/>
    public string ControlPlaneAppId => _appConfiguration.EffectiveAppId;

    /// <inheritdoc/>
    public IReadOnlyList<ServiceHealthEntry> GetControlPlaneServiceHealth()
    {
        var enabledServices = GetEnabledServiceNames();
        var now = DateTimeOffset.UtcNow;
        var controlPlaneAppId = ControlPlaneAppId;

        var healthStatuses = new List<ServiceHealthEntry>();

        foreach (var serviceName in enabledServices)
        {
            healthStatuses.Add(new ServiceHealthEntry
            {
                ServiceId = serviceName,
                AppId = controlPlaneAppId,
                Status = (InstanceHealthStatus)ServiceHealthStatus.Healthy, // Control plane services are considered healthy if enabled
                LastSeen = now,
                Capacity = null, // Capacity tracking is done at instance level, not per-service
                Metadata = null
            });
        }

        _logger.LogDebug(
            "Generated {Count} control plane service health entries for app-id {AppId}",
            healthStatuses.Count, controlPlaneAppId);

        return healthStatuses;
    }

    /// <inheritdoc/>
    public IReadOnlyList<string> GetEnabledServiceNames()
    {
        var pluginLoader = Program.PluginLoader;
        if (pluginLoader == null)
        {
            _logger.LogWarning("PluginLoader not available - cannot enumerate control plane services");
            return Array.Empty<string>();
        }

        var enabledPlugins = pluginLoader.EnabledPlugins;
        var serviceNames = enabledPlugins
            .Select(p => p.PluginName)
            .OrderBy(n => n)
            .ToList();

        _logger.LogDebug(
            "Found {Count} enabled services on control plane: {Services}",
            serviceNames.Count, string.Join(", ", serviceNames));

        return serviceNames;
    }
}
