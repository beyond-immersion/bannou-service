using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Reflection;

namespace BeyondImmersion.BannouService.Plugins;

/// <summary>
/// Responsible for discovering, loading, and managing Bannou service plugins.
/// </summary>
public class PluginLoader
{
    private readonly ILogger<PluginLoader> _logger;
    private readonly List<IBannouPlugin> _loadedPlugins = new();
    private readonly Dictionary<string, Assembly> _loadedAssemblies = new();

    /// <inheritdoc/>
    public PluginLoader(ILogger<PluginLoader> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Get all currently loaded plugins.
    /// </summary>
    public IReadOnlyList<IBannouPlugin> LoadedPlugins => _loadedPlugins.AsReadOnly();

    /// <summary>
    /// Discover and load plugins from the specified directory based on configuration.
    /// </summary>
    /// <param name="pluginsDirectory">Root plugins directory</param>
    /// <param name="requestedPlugins">List of specific plugins to load, or null for all</param>
    /// <returns>Number of plugins successfully loaded</returns>
    public async Task<int> LoadPluginsAsync(string pluginsDirectory, IList<string>? requestedPlugins = null)
    {
        _logger.LogInformation("🔍 Discovering plugins in: {PluginsDirectory}", pluginsDirectory);

        if (!Directory.Exists(pluginsDirectory))
        {
            _logger.LogWarning("⚠️  Plugins directory does not exist: {PluginsDirectory}", pluginsDirectory);
            return 0;
        }

        var pluginDirectories = Directory.GetDirectories(pluginsDirectory);
        var pluginsLoaded = 0;

        foreach (var pluginDir in pluginDirectories)
        {
            var pluginName = Path.GetFileName(pluginDir);

            // Skip if specific plugins requested and this isn't one of them
            if (requestedPlugins != null && !requestedPlugins.Contains(pluginName))
            {
                _logger.LogDebug("⏭️  Skipping plugin '{PluginName}' (not in requested list)", pluginName);
                continue;
            }

            try
            {
                var plugin = await LoadPluginFromDirectoryAsync(pluginDir, pluginName);
                if (plugin != null)
                {
                    _loadedPlugins.Add(plugin);
                    pluginsLoaded++;
                    _logger.LogInformation("✅ Loaded plugin: {PluginName} v{Version}", plugin.DisplayName, plugin.Version);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to load plugin from directory: {PluginDirectory}", pluginDir);
            }
        }

        _logger.LogInformation("📋 Plugin loading complete. {LoadedCount} plugins loaded", pluginsLoaded);
        return pluginsLoaded;
    }

    /// <summary>
    /// Configure services for all loaded plugins.
    /// </summary>
    /// <param name="services">Service collection</param>
    public void ConfigureServices(IServiceCollection services)
    {
        _logger.LogInformation("🔧 Configuring services for {PluginCount} plugins", _loadedPlugins.Count);

        foreach (var plugin in _loadedPlugins)
        {
            try
            {
                _logger.LogDebug("⚙️  Configuring services for plugin: {PluginName}", plugin.PluginName);
                plugin.ConfigureServices(services);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to configure services for plugin: {PluginName}", plugin.PluginName);
                throw; // Re-throw to fail startup if plugin configuration fails
            }
        }

        _logger.LogInformation("✅ Service configuration complete for all plugins");
    }

    /// <summary>
    /// Configure application pipeline for all loaded plugins.
    /// </summary>
    /// <param name="app">Web application</param>
    public void ConfigureApplication(WebApplication app)
    {
        _logger.LogInformation("🔧 Configuring application pipeline for {PluginCount} plugins", _loadedPlugins.Count);

        foreach (var plugin in _loadedPlugins)
        {
            try
            {
                _logger.LogDebug("⚙️  Configuring application for plugin: {PluginName}", plugin.PluginName);
                plugin.ConfigureApplication(app);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to configure application for plugin: {PluginName}", plugin.PluginName);
                throw; // Re-throw to fail startup if plugin configuration fails
            }
        }

        _logger.LogInformation("✅ Application configuration complete for all plugins");
    }

    /// <summary>
    /// Initialize all loaded plugins.
    /// </summary>
    /// <returns>True if all plugins initialized successfully</returns>
    public async Task<bool> InitializePluginsAsync()
    {
        _logger.LogInformation("🚀 Initializing {PluginCount} plugins", _loadedPlugins.Count);

        foreach (var plugin in _loadedPlugins)
        {
            try
            {
                _logger.LogDebug("🔄 Initializing plugin: {PluginName}", plugin.PluginName);
                var success = await plugin.InitializeAsync();
                if (!success)
                {
                    _logger.LogError("❌ Plugin initialization failed: {PluginName}", plugin.PluginName);
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Exception during plugin initialization: {PluginName}", plugin.PluginName);
                return false;
            }
        }

        _logger.LogInformation("✅ All plugins initialized successfully");
        return true;
    }

    /// <summary>
    /// Start all loaded plugins.
    /// </summary>
    /// <returns>True if all plugins started successfully</returns>
    public async Task<bool> StartPluginsAsync()
    {
        _logger.LogInformation("▶️  Starting {PluginCount} plugins", _loadedPlugins.Count);

        foreach (var plugin in _loadedPlugins)
        {
            try
            {
                _logger.LogDebug("▶️  Starting plugin: {PluginName}", plugin.PluginName);
                var success = await plugin.StartAsync();
                if (!success)
                {
                    _logger.LogError("❌ Plugin start failed: {PluginName}", plugin.PluginName);
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Exception during plugin start: {PluginName}", plugin.PluginName);
                return false;
            }
        }

        _logger.LogInformation("✅ All plugins started successfully");
        return true;
    }

    /// <summary>
    /// Invoke running methods for all loaded plugins.
    /// </summary>
    public async Task InvokeRunningAsync()
    {
        _logger.LogInformation("🏃 Invoking running methods for {PluginCount} plugins", _loadedPlugins.Count);

        var runningTasks = _loadedPlugins.Select(async plugin =>
        {
            try
            {
                _logger.LogDebug("🏃 Invoking running for plugin: {PluginName}", plugin.PluginName);
                await plugin.RunningAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Exception during plugin running: {PluginName}", plugin.PluginName);
            }
        });

        await Task.WhenAll(runningTasks);
        _logger.LogInformation("✅ All plugin running methods invoked");
    }

    /// <summary>
    /// Shutdown all loaded plugins gracefully.
    /// </summary>
    public async Task ShutdownPluginsAsync()
    {
        _logger.LogInformation("🛑 Shutting down {PluginCount} plugins", _loadedPlugins.Count);

        var shutdownTasks = _loadedPlugins.Select(async plugin =>
        {
            try
            {
                _logger.LogDebug("🛑 Shutting down plugin: {PluginName}", plugin.PluginName);
                await plugin.ShutdownAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Exception during plugin shutdown: {PluginName}", plugin.PluginName);
            }
        });

        await Task.WhenAll(shutdownTasks);
        _logger.LogInformation("✅ All plugins shut down");
    }

    /// <summary>
    /// Load a plugin from a specific directory.
    /// </summary>
    /// <param name="pluginDirectory">Directory containing the plugin</param>
    /// <param name="expectedPluginName">Expected name of the plugin</param>
    /// <returns>Loaded plugin instance or null if loading failed</returns>
    private Task<IBannouPlugin?> LoadPluginFromDirectoryAsync(string pluginDirectory, string expectedPluginName)
    {
        _logger.LogDebug("🔍 Loading plugin from directory: {PluginDirectory}", pluginDirectory);

        // Find the main assembly (usually lib-{service}.dll)
        var assemblyPath = Path.Combine(pluginDirectory, $"lib-{expectedPluginName}.dll");
        if (!File.Exists(assemblyPath))
        {
            _logger.LogWarning("⚠️  Plugin assembly not found: {AssemblyPath}", assemblyPath);
            return Task.FromResult<IBannouPlugin?>(null);
        }

        try
        {
            // Load the assembly
            var assembly = Assembly.LoadFrom(assemblyPath);
            _loadedAssemblies[expectedPluginName] = assembly;

            // Find types that implement IBannouPlugin
            var pluginTypes = assembly.GetTypes()
                .Where(t => typeof(IBannouPlugin).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract)
                .ToList();

            if (pluginTypes.Count == 0)
            {
                _logger.LogWarning("⚠️  No IBannouPlugin implementations found in assembly: {AssemblyPath}", assemblyPath);
                return Task.FromResult<IBannouPlugin?>(null);
            }

            if (pluginTypes.Count > 1)
            {
                _logger.LogWarning("⚠️  Multiple IBannouPlugin implementations found in assembly: {AssemblyPath}. Using first one.", assemblyPath);
            }

            // Create plugin instance
            var pluginType = pluginTypes.First();
            var plugin = Activator.CreateInstance(pluginType) as IBannouPlugin;

            if (plugin == null)
            {
                _logger.LogError("❌ Failed to create plugin instance for type: {PluginType}", pluginType.Name);
                return Task.FromResult<IBannouPlugin?>(null);
            }

            // Validate plugin
            if (!plugin.ValidatePlugin())
            {
                _logger.LogWarning("⚠️  Plugin validation failed: {PluginName}", plugin.PluginName);
                return Task.FromResult<IBannouPlugin?>(null);
            }

            // Verify plugin name matches expected
            if (!string.Equals(plugin.PluginName, expectedPluginName, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("⚠️  Plugin name mismatch. Expected: {Expected}, Got: {Actual}",
                    expectedPluginName, plugin.PluginName);
            }

            return Task.FromResult<IBannouPlugin?>(plugin);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to load plugin assembly: {AssemblyPath}", assemblyPath);
            return Task.FromResult<IBannouPlugin?>(null);
        }
    }
}
