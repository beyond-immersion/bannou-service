using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace BeyondImmersion.BannouService.Plugins;

/// <summary>
/// Interface for Bannou service plugins that can be dynamically loaded and registered.
/// </summary>
public interface IBannouPlugin
{
    /// <summary>
    /// Unique name identifier for this plugin.
    /// Should match the service name from ServiceLib property.
    /// </summary>
    string PluginName { get; }

    /// <summary>
    /// Human-readable display name for this plugin.
    /// </summary>
    string DisplayName { get; }

    /// <summary>
    /// Version of this plugin.
    /// </summary>
    string Version { get; }

    /// <summary>
    /// Configure services for dependency injection.
    /// Called during application startup before building the application.
    /// </summary>
    /// <param name="services">Service collection to register dependencies</param>
    void ConfigureServices(IServiceCollection services);

    /// <summary>
    /// Configure the application pipeline.
    /// Called after the application is built but before it starts running.
    /// </summary>
    /// <param name="app">Web application to configure</param>
    void ConfigureApplication(WebApplication app);

    /// <summary>
    /// Initialize the plugin.
    /// Called when the plugin is first loaded and validated.
    /// </summary>
    /// <returns>True if initialization succeeded, false otherwise</returns>
    Task<bool> InitializeAsync();

    /// <summary>
    /// Start the plugin.
    /// Called when all plugins have been loaded and the application is starting.
    /// </summary>
    /// <returns>True if startup succeeded, false otherwise</returns>
    Task<bool> StartAsync();

    /// <summary>
    /// Called when the plugin is running and all startup processes are complete.
    /// Can be used for background tasks or periodic operations.
    /// </summary>
    /// <returns>Task that represents the running operation</returns>
    Task RunningAsync();

    /// <summary>
    /// Shutdown the plugin gracefully.
    /// Called when the application is shutting down.
    /// </summary>
    /// <returns>Task that completes when shutdown is finished</returns>
    Task ShutdownAsync();

    /// <summary>
    /// Validate that this plugin can operate with the current configuration.
    /// Called during plugin discovery before initialization.
    /// </summary>
    /// <returns>True if plugin is valid and can be loaded, false otherwise</returns>
    bool ValidatePlugin();
}
