using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Plugins;

/// <summary>
/// Base implementation of IBannouPlugin that provides default behavior
/// and integrates with existing IDaprService implementations.
/// </summary>
public abstract class BaseBannouPlugin : IBannouPlugin
{
    /// <inheritdoc/>
    protected ILogger? Logger { get; private set; }

    /// <inheritdoc />
    public abstract string PluginName { get; }

    /// <inheritdoc />
    public virtual string DisplayName => PluginName;

    /// <inheritdoc />
    public virtual string Version => "1.0.0";

    /// <inheritdoc />
    public virtual void ConfigureServices(IServiceCollection services)
    {
        // Default implementation - override in derived classes if needed
        // Most services will use the existing [DaprService] registration
    }

    /// <inheritdoc />
    public virtual void ConfigureApplication(WebApplication app)
    {
        // Default implementation - override in derived classes if needed
        // Most services will use existing controller routing
    }

    /// <inheritdoc />
    [Obsolete]
    public virtual async Task<bool> InitializeAsync()
    {
        // Create logger for this plugin
        var loggerFactory = GetLoggerFactory();
        if (loggerFactory != null)
        {
            Logger = loggerFactory.CreateLogger(GetType());
            Logger?.LogInformation("Initializing plugin: {PluginName}", PluginName);
        }

        return await OnInitializeAsync();
    }

    /// <inheritdoc />
    public virtual async Task<bool> StartAsync()
    {
        Logger?.LogInformation("Starting plugin: {PluginName}", PluginName);
        return await OnStartAsync();
    }

    /// <inheritdoc />
    public virtual async Task RunningAsync()
    {
        Logger?.LogDebug("Plugin running: {PluginName}", PluginName);
        await OnRunningAsync();
    }

    /// <inheritdoc />
    public virtual async Task ShutdownAsync()
    {
        Logger?.LogInformation("Shutting down plugin: {PluginName}", PluginName);
        await OnShutdownAsync();
    }

    /// <inheritdoc />
    public virtual bool ValidatePlugin()
    {
        // Basic validation - ensure plugin name is set
        if (string.IsNullOrWhiteSpace(PluginName))
        {
            return false;
        }

        return OnValidatePlugin();
    }

    /// <summary>
    /// Override this method to perform plugin-specific initialization.
    /// </summary>
    /// <returns>True if initialization succeeded</returns>
    protected virtual Task<bool> OnInitializeAsync()
    {
        return Task.FromResult(true);
    }

    /// <summary>
    /// Override this method to perform plugin-specific startup.
    /// </summary>
    /// <returns>True if startup succeeded</returns>
    protected virtual Task<bool> OnStartAsync()
    {
        return Task.FromResult(true);
    }

    /// <summary>
    /// Override this method to perform plugin-specific running operations.
    /// </summary>
    protected virtual Task OnRunningAsync()
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// Override this method to perform plugin-specific shutdown.
    /// </summary>
    protected virtual Task OnShutdownAsync()
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// Override this method to perform plugin-specific validation.
    /// </summary>
    /// <returns>True if plugin is valid</returns>
    protected virtual bool OnValidatePlugin()
    {
        return true;
    }

    /// <summary>
    /// Get logger factory from service provider if available.
    /// </summary>
    /// <returns>Logger factory or null if not available</returns>
    [Obsolete]
    private static ILoggerFactory? GetLoggerFactory()
    {
        try
        {
            // Try to get logger factory from the service provider
            // This will work after services are configured
            var serviceProvider = Program.ConfigurationRoot?.Get<IServiceProvider>();
            return serviceProvider?.GetService<ILoggerFactory>();
        }
        catch
        {
            // Fallback to the static logger from Program
            return null;
        }
    }
}
