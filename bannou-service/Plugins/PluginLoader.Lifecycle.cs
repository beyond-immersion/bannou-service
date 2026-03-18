using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Reflection;

namespace BeyondImmersion.BannouService.Plugins;

/// <summary>
/// Service resolution and plugin lifecycle management (initialize, start, running, shutdown).
/// </summary>
public partial class PluginLoader
{
    /// <summary>
    /// Resolve and store service instances centrally for enabled plugins only.
    /// This happens AFTER the web application is built and DI container is ready.
    /// </summary>
    /// <param name="serviceProvider">Service provider to resolve services from</param>
    public void ResolveServices(IServiceProvider serviceProvider)
    {
        _logger.LogInformation("Centrally resolving services for {EnabledCount} enabled plugins", _enabledPlugins.Count);

        // Use a service scope to resolve scoped services correctly
        using var scope = serviceProvider.CreateScope();
        var scopedServiceProvider = scope.ServiceProvider;

        foreach (var plugin in _enabledPlugins)
        {
            try
            {
                _logger.LogDebug("Resolving service for enabled plugin: {PluginName}", plugin.PluginName);

                // Find the service type that was registered for this plugin
                var serviceRegistration = _serviceTypesToRegister
                    .FirstOrDefault(s => GetServiceNameFromType(s.implementationType) == plugin.PluginName);

                if (serviceRegistration != default)
                {
                    var serviceInstance = scopedServiceProvider.GetService(serviceRegistration.interfaceType);
                    if (serviceInstance is IBannouService bannouService)
                    {
                        _resolvedServices[plugin.PluginName] = bannouService;
                        _logger.LogInformation("Resolved {ServiceType} for plugin: {PluginName}",
                            serviceRegistration.implementationType.Name, plugin.PluginName);
                    }
                    else if (serviceInstance != null)
                    {
                        _logger.LogWarning("Service {ServiceType} could not be resolved from DI container for plugin: {PluginName}", serviceRegistration.interfaceType.Name, plugin.PluginName);
                    }
                    else
                    {
                        _logger.LogWarning("Failed to resolve service {ServiceType} for plugin: {PluginName}", serviceRegistration.interfaceType.Name, plugin.PluginName);
                    }
                }
                else
                {
                    _logger.LogDebug("No service registration found for plugin: {PluginName} (plugin may not have a service)", plugin.PluginName);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception while resolving service for plugin: {PluginName}", plugin.PluginName);
            }
        }

        _logger.LogInformation("Service resolution complete. {ResolvedCount} services resolved", _resolvedServices.Count);
    }

    /// <summary>
    /// Extract service name from a service type using BannouService attribute.
    /// </summary>
    private string? GetServiceNameFromType(Type serviceType)
    {
        var bannouServiceAttr = serviceType.GetCustomAttribute<BannouServiceAttribute>();
        return bannouServiceAttr?.Name;
    }

    /// <summary>
    /// Configure application for enabled plugins only.
    /// Receives IServiceProvider to support both web and embedded deployment modes.
    /// </summary>
    /// <param name="services">The built service provider</param>
    public void ConfigureApplication(IServiceProvider services)
    {
        _logger.LogInformation("Configuring application pipeline for {EnabledCount} enabled plugins", _enabledPlugins.Count);

        foreach (var plugin in _enabledPlugins)
        {
            try
            {
                _logger.LogDebug("Configuring application for plugin: {PluginName}", plugin.PluginName);
                plugin.ConfigureApplication(services);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to configure application for plugin: {PluginName}", plugin.PluginName);
                throw; // Re-throw to fail startup if plugin configuration fails
            }
        }

        _logger.LogInformation("Application configuration complete for enabled plugins");
    }

    /// <summary>
    /// Configure web-specific pipeline for enabled plugins (endpoint mapping, middleware, etc.).
    /// Called only in web hosting mode. Stores the WebApplication reference for OnStartAsync.
    /// </summary>
    /// <param name="app">Web application</param>
    public void ConfigureWebPipeline(WebApplication app)
    {
        _webApp = app;

        foreach (var plugin in _enabledPlugins)
        {
            try
            {
                plugin.ConfigureWebPipeline(app);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to configure web pipeline for plugin: {PluginName}", plugin.PluginName);
                throw;
            }
        }
    }

    /// <summary>
    /// Initialize enabled plugins and their resolved services.
    /// This follows the proper lifecycle: plugins first, then services.
    /// Infrastructure plugins (messaging, state, mesh) are initialized first and
    /// their failure causes immediate startup failure.
    /// </summary>
    /// <returns>True if all plugins and services initialized successfully</returns>
    public async Task<bool> InitializeAsync()
    {
        _logger.LogInformation("Initializing {EnabledCount} enabled plugins", _enabledPlugins.Count);

        // STAGE 1: Initialize enabled plugins (infrastructure first due to sorting)
        foreach (var plugin in _enabledPlugins)
        {
            var isInfrastructure = RequiredInfrastructurePlugins.Contains(plugin.PluginName);
            var pluginType = isInfrastructure ? "INFRASTRUCTURE" : "service";

            try
            {
                _logger.LogDebug("Initializing {PluginType} plugin: {PluginName}",
                    pluginType, plugin.PluginName);

                var success = await plugin.InitializeAsync();
                if (!success)
                {
                    if (isInfrastructure)
                    {
                        _logger.LogCritical(
                            "STARTUP FAILURE: Infrastructure plugin '{PluginName}' failed to initialize. " +
                            "This is a required plugin and the application cannot function without it.",
                            plugin.PluginName);
                    }
                    else
                    {
                        _logger.LogError("Plugin initialization failed: {PluginName}", plugin.PluginName);
                    }
                    return false;
                }

                if (isInfrastructure)
                {
                    _logger.LogInformation("Infrastructure plugin '{PluginName}' initialized successfully",
                        plugin.PluginName);
                }
            }
            catch (Exception ex)
            {
                if (isInfrastructure)
                {
                    _logger.LogCritical(ex,
                        "STARTUP FAILURE: Infrastructure plugin '{PluginName}' threw exception during initialization. " +
                        "This is a required plugin and the application cannot function without it.",
                        plugin.PluginName);
                }
                else
                {
                    _logger.LogError(ex, "Exception during plugin initialization: {PluginName}", plugin.PluginName);
                }
                return false;
            }
        }

        // STAGE 2: Initialize centrally resolved services
        // Use OnStartAsync(WebApplication, CancellationToken) to allow services to register minimal API endpoints
        if (_webApp == null)
        {
            _logger.LogWarning("WebApplication reference not set - services requiring WebApplication will not initialize fully. " +
                "Ensure ConfigureApplication() is called before InitializeAsync().");
        }

        foreach (var (pluginName, service) in _resolvedServices)
        {
            try
            {
                _logger.LogDebug("Initializing centrally resolved service for plugin: {PluginName}", pluginName);
                if (_webApp != null)
                {
                    // Call the WebApplication-aware version for services that need endpoint registration
                    await service.OnStartAsync(_webApp, CancellationToken.None);
                }
                else
                {
                    // Fall back to simple version if WebApplication not available
                    await service.OnStartAsync(CancellationToken.None);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception during centrally resolved service initialization for plugin: {PluginName}", pluginName);
                return false;
            }
        }

        _logger.LogInformation("All enabled plugins and services initialized successfully");
        return true;
    }

    /// <summary>
    /// Registers service permissions for all enabled plugins with the Permission service.
    /// Uses DI-based IPermissionRegistry for direct push-based registration.
    /// </summary>
    /// <param name="appId">The effective app ID for this service instance</param>
    /// <param name="registry">The permission registry resolved from DI (null if Permission service is disabled)</param>
    /// <returns>True if all permissions were registered successfully</returns>
    public async Task<bool> RegisterServicePermissionsAsync(string appId, IPermissionRegistry? registry)
    {
        _logger.LogInformation("Registering service permissions for {ServiceCount} services: {ServiceNames}",
            _resolvedServices.Count, string.Join(", ", _resolvedServices.Keys));

        if (registry == null)
        {
            _logger.LogWarning("IPermissionRegistry not available - skipping permission registration");
            return true;
        }

        foreach (var (pluginName, service) in _resolvedServices)
        {
            try
            {
                _logger.LogDebug("Registering permissions for service: {PluginName}", pluginName);
                await service.RegisterServicePermissionsAsync(appId, registry);
                _logger.LogDebug("Permissions registered for service: {PluginName}", pluginName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to register permissions for {PluginName}", pluginName);
                return false;
            }
        }

        _logger.LogInformation("All service permissions registered successfully");
        return true;
    }

    /// <summary>
    /// Start enabled plugins and their resolved services.
    /// This follows the proper lifecycle: plugins first, then services.
    /// </summary>
    /// <returns>True if all plugins and services started successfully</returns>
    public async Task<bool> StartAsync()
    {
        _logger.LogInformation("Starting {EnabledCount} enabled plugins", _enabledPlugins.Count);

        // STAGE 1: Start enabled plugins
        foreach (var plugin in _enabledPlugins)
        {
            try
            {
                _logger.LogDebug("Starting plugin: {PluginName}", plugin.PluginName);
                var success = await plugin.StartAsync();
                if (!success)
                {
                    _logger.LogError("Plugin start failed: {PluginName}", plugin.PluginName);
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception during plugin start: {PluginName}", plugin.PluginName);
                return false;
            }
        }

        // STAGE 2: Start centrally resolved services (with WebApplication access)
        // Note: This would need WebApplication parameter if services need it

        _logger.LogInformation("All enabled plugins started successfully");
        return true;
    }

    /// <summary>
    /// Invoke running methods for enabled plugins and resolved services.
    /// </summary>
    public async Task InvokeRunningAsync()
    {
        _logger.LogInformation("Invoking running methods for {EnabledCount} enabled plugins", _enabledPlugins.Count);

        var runningTasks = _enabledPlugins.Select(async plugin =>
        {
            try
            {
                _logger.LogDebug("Invoking running for plugin: {PluginName}", plugin.PluginName);
                await plugin.RunningAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception during plugin running: {PluginName}", plugin.PluginName);
            }
        });

        // Also invoke running for centrally resolved services
        var serviceRunningTasks = _resolvedServices.Select(async kvp =>
        {
            try
            {
                _logger.LogDebug("Invoking running for centrally resolved service: {PluginName}", kvp.Key);
                await kvp.Value.OnRunningAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception during centrally resolved service running: {PluginName}", kvp.Key);
            }
        });

        await Task.WhenAll(runningTasks.Concat(serviceRunningTasks));
        _logger.LogInformation("All enabled plugin running methods invoked");
    }

    /// <summary>
    /// Shutdown enabled plugins and resolved services gracefully.
    /// </summary>
    public async Task ShutdownAsync()
    {
        _logger.LogInformation("Shutting down {EnabledCount} enabled plugins", _enabledPlugins.Count);

        var shutdownTasks = _enabledPlugins.Select(async plugin =>
        {
            try
            {
                _logger.LogDebug("Shutting down plugin: {PluginName}", plugin.PluginName);
                await plugin.ShutdownAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception during plugin shutdown: {PluginName}", plugin.PluginName);
            }
        });

        // Also shutdown centrally resolved services
        var serviceShutdownTasks = _resolvedServices.Select(async kvp =>
        {
            try
            {
                _logger.LogDebug("Shutting down centrally resolved service: {PluginName}", kvp.Key);
                await kvp.Value.OnShutdownAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception during centrally resolved service shutdown: {PluginName}", kvp.Key);
            }
        });

        await Task.WhenAll(shutdownTasks.Concat(serviceShutdownTasks));
        _logger.LogInformation("All enabled plugins shut down");
    }
}
