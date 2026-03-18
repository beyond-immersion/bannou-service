using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.Logging;
using System.Reflection;

namespace BeyondImmersion.BannouService.Plugins;

/// <summary>
/// Plugin discovery: assembly scanning, plugin loading, service enablement checks.
/// </summary>
public partial class PluginLoader
{
    /// <summary>
    /// Discover and load all plugins from the specified directory.
    /// This follows the proper flow: 1) Load assemblies for ALL plugins, 2) Determine which services are enabled,
    /// 3) Register types in DI appropriately (all client types + enabled service types only).
    /// </summary>
    /// <param name="pluginsDirectory">Root plugins directory</param>
    /// <param name="requestedPlugins">List of specific plugins to load, or null for all</param>
    /// <returns>Number of enabled plugins successfully loaded</returns>
    public async Task<int?> DiscoverAndLoadPluginsAsync(string pluginsDirectory, IList<string>? requestedPlugins = null)
    {
        _logger.LogInformation("Discovering plugins in: {PluginsDirectory}", pluginsDirectory);

        if (!Directory.Exists(pluginsDirectory))
        {
            _logger.LogWarning("Plugins directory does not exist: {PluginsDirectory}", pluginsDirectory);
            return 0;
        }

        var pluginDirectories = Directory.GetDirectories(pluginsDirectory);

        // STAGE 1: Load ALL plugin assemblies (enabled and disabled)
        foreach (var pluginDir in pluginDirectories)
        {
            var pluginName = Path.GetFileName(pluginDir);

            // Skip if specific plugins requested and this isn't one of them
            if (requestedPlugins != null && !requestedPlugins.Contains(pluginName))
            {
                _logger.LogDebug("Skipping plugin '{PluginName}' (not in requested list)", pluginName);
                continue;
            }

            try
            {
                var plugin = await LoadPluginFromDirectoryAsync(pluginDir, pluginName);
                if (plugin != null)
                {
                    _allPlugins.Add(plugin);

                    // Determine if this plugin's service should be enabled
                    var serviceName = plugin.PluginName;
                    var layer = GetServiceLayer(plugin);
                    if (IsServiceEnabled(serviceName, layer))
                    {
                        _enabledPlugins.Add(plugin);
                        _logger.LogDebug("Service {ServiceName} is enabled", serviceName);
                    }
                    else
                    {
                        _logger.LogDebug("Service {ServiceName} is disabled", serviceName);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load plugin from directory: {PluginDirectory}", pluginDir);
                return null;
            }
        }

        // STAGE 2: Validate required infrastructure plugins are present
        var missingInfrastructure = RequiredInfrastructurePlugins
            .Where(required => !_enabledPlugins.Any(p => p.PluginName.Equals(required, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        if (missingInfrastructure.Count > 0)
        {
            var missing = string.Join(", ", missingInfrastructure);
            _logger.LogCritical(
                "STARTUP FAILURE: Required infrastructure plugins are missing: [{MissingPlugins}]. " +
                "These plugins (lib-messaging, lib-state, lib-mesh) are MANDATORY and must be present for the application to function.",
                missing);
            return null; // Indicate fatal failure
        }

        // STAGE 3: Sort enabled plugins by service hierarchy layer per SERVICE-HIERARCHY.md
        // Order: L0 (Infrastructure) → L1 (AppFoundation) → L2 (GameFoundation) →
        //        L3 (AppFeatures) → L4 (GameFeatures) → L5 (Extensions)
        // Within L0, use InfrastructureLoadOrder for internal ordering (telemetry → state → messaging → mesh)
        // Within other layers, sort alphabetically
        var sortedPlugins = _enabledPlugins
            .OrderBy(p => GetServiceLayer(p))           // Primary: service hierarchy layer
            .ThenBy(p => GetInfrastructureSubOrder(p))  // Secondary: L0 internal order
            .ThenBy(p => p.PluginName)                  // Tertiary: alphabetical within layer
            .ToList();
        _enabledPlugins.Clear();
        _enabledPlugins.AddRange(sortedPlugins);

        _logger.LogInformation(
            "Plugins sorted by service hierarchy. Loading order: [{LoadOrder}]",
            string.Join(" -> ", _enabledPlugins.Select(p => $"{p.PluginName}(L{(int)GetServiceLayer(p) / 100})")));

        // STAGE 4: Discover types for DI registration from ALL assemblies
        DiscoverTypesForRegistration();

        // STAGE 5: Validate service hierarchy compliance
        // This catches services that depend on higher-layer services (violates SERVICE-HIERARCHY.md)
        if (!ValidateServiceHierarchies())
        {
            _logger.LogCritical(
                "STARTUP FAILURE: Service hierarchy violations detected. " +
                "Services cannot depend on higher-layer services per SERVICE-HIERARCHY.md. " +
                "Fix the violations listed above before proceeding.");
            return null; // Indicate fatal failure
        }

        // STAGE 6: Build valid environment variable prefixes for orchestrator forwarding
        PopulateValidEnvironmentPrefixes();

        var discoveredSummary = string.Join(", ", _allPlugins.Select(p => $"{p.DisplayName} v{p.Version}"));
        var enabledSummary = string.Join(", ", _enabledPlugins.Select(p => $"{p.DisplayName} v{p.Version}"));

        _logger.LogInformation(
            "Plugin discovery complete. Discovered {AllCount}: [{Discovered}]; Enabled {EnabledCount}: [{Enabled}]",
            _allPlugins.Count, discoveredSummary, _enabledPlugins.Count, enabledSummary);

        return _enabledPlugins.Count;
    }

    /// <summary>
    /// Check if a service should be enabled based on layer-level controls and individual overrides.
    /// Resolution order:
    /// 1. Required infrastructure (state, messaging, mesh) → ALWAYS enabled
    /// 2. {SERVICE}_SERVICE_ENABLED env var explicitly set → use that value (true/false)
    /// 3. BANNOU_SERVICES_ENABLED=false (master kill switch) → disabled
    /// 4. Layer enabled (from AppConfiguration) → use layer setting (all default true)
    /// </summary>
    /// <param name="serviceName">Name of the service (e.g., "auth", "account")</param>
    /// <param name="layer">The service hierarchy layer this service belongs to</param>
    /// <returns>True if service should be enabled, false if disabled</returns>
    private bool IsServiceEnabled(string serviceName, ServiceLayer layer)
    {
        if (string.IsNullOrWhiteSpace(serviceName))
            return Program.Configuration.ServicesEnabled;

        // 1. Required infrastructure plugins are ALWAYS enabled - they cannot be disabled
        if (RequiredInfrastructurePlugins.Contains(serviceName))
        {
            _logger.LogInformation(
                "Infrastructure plugin '{ServiceName}' is REQUIRED and cannot be disabled",
                serviceName);
            return true;
        }

        // 2. Individual override via {SERVICE}_SERVICE_ENABLED (always checked, no dual-mode)
        var serviceNameUpper = serviceName.ToUpper().Replace("-", "_");
        var enabledEnv = System.Environment.GetEnvironmentVariable($"{serviceNameUpper}_SERVICE_ENABLED");
        if (!string.IsNullOrWhiteSpace(enabledEnv))
        {
            var isEnabled = string.Equals(enabledEnv, "true", StringComparison.OrdinalIgnoreCase);
            _logger.LogDebug(
                "Service {ServiceName} {Status} via {EnvVar}={EnvValue} (individual override)",
                serviceName, isEnabled ? "enabled" : "disabled",
                $"{serviceNameUpper}_SERVICE_ENABLED", enabledEnv);
            return isEnabled;
        }

        // 3. Master kill switch: BANNOU_SERVICES_ENABLED=false disables everything
        if (!Program.Configuration.ServicesEnabled)
        {
            _logger.LogDebug(
                "Service {ServiceName} disabled: BANNOU_SERVICES_ENABLED=false (master kill switch)",
                serviceName);
            return false;
        }

        // 4. Layer-level control
        var layerEnabled = IsLayerEnabled(layer);
        _logger.LogDebug(
            "Service {ServiceName} {Status}: layer {Layer} is {LayerStatus}",
            serviceName, layerEnabled ? "enabled" : "disabled",
            layer, layerEnabled ? "enabled" : "disabled");
        return layerEnabled;
    }

    /// <summary>
    /// Check if a service hierarchy layer is enabled via AppConfiguration.
    /// Infrastructure (L0) is always enabled. All other layers default to true.
    /// Environment variables: BANNOU_ENABLE_APP_FOUNDATION, BANNOU_ENABLE_GAME_FOUNDATION, etc.
    /// </summary>
    private static bool IsLayerEnabled(ServiceLayer layer) => layer switch
    {
        ServiceLayer.Infrastructure => true,
        ServiceLayer.AppFoundation => Program.Configuration.EnableAppFoundation,
        ServiceLayer.GameFoundation => Program.Configuration.EnableGameFoundation,
        ServiceLayer.AppFeatures => Program.Configuration.EnableAppFeatures,
        ServiceLayer.GameFeatures => Program.Configuration.EnableGameFeatures,
        ServiceLayer.Extensions => Program.Configuration.EnableExtensions,
        _ => true
    };

    /// <summary>
    /// Populate the static set of valid environment variable prefixes based on discovered plugins.
    /// This allows the orchestrator to whitelist which ENVs to forward to deployed containers.
    /// </summary>
    private void PopulateValidEnvironmentPrefixes()
    {
        _validEnvironmentPrefixes.Clear();

        // Only BANNOU_ prefix for shared configuration
        // All other configuration uses plugin-derived {SERVICE}_ prefixes
        _validEnvironmentPrefixes.Add("BANNOU_");

        // Add prefix for each discovered plugin
        // e.g., "auth" → "AUTH_", "game-session" → "GAME_SESSION_"
        // Hyphens are always converted to underscores to match schema env: conventions
        foreach (var plugin in _allPlugins)
        {
            var pluginName = plugin.PluginName;
            if (string.IsNullOrWhiteSpace(pluginName))
                continue;

            // Convert plugin name to env prefix: "game-session" → "GAME_SESSION_"
            // Hyphens are converted to underscores to match schema env: conventions
            var prefix = pluginName.ToUpperInvariant().Replace('-', '_') + "_";
            _validEnvironmentPrefixes.Add(prefix);
        }

        _logger.LogInformation(
            "Valid environment prefixes for orchestrator forwarding: {Prefixes}",
            string.Join(", ", _validEnvironmentPrefixes.OrderBy(p => p)));
    }

    /// <summary>
    /// Get the ServiceLayer for a plugin based on its BannouServiceAttribute.
    /// Infrastructure plugins (state, messaging, mesh, telemetry) always return Infrastructure.
    /// Other plugins return the layer specified in their [BannouService] attribute,
    /// defaulting to GameFeatures if not specified.
    /// </summary>
    private ServiceLayer GetServiceLayer(IBannouPlugin plugin)
    {
        // Infrastructure plugins are always L0 regardless of attribute
        if (InfrastructureLoadOrder.ContainsKey(plugin.PluginName))
            return ServiceLayer.Infrastructure;

        // Look up the service's declared layer from BannouServiceAttribute
        var assembly = _loadedAssemblies.GetValueOrDefault(plugin.PluginName);
        if (assembly == null)
            return ServiceLayer.GameFeatures; // Default to highest non-extension layer

        // Find the service type with [BannouService] attribute matching this plugin
        var serviceType = assembly.GetTypes()
            .FirstOrDefault(t =>
            {
                var attr = t.GetCustomAttribute<BannouServiceAttribute>();
                return attr != null && attr.Name.Equals(plugin.PluginName, StringComparison.OrdinalIgnoreCase);
            });

        var bannouServiceAttr = serviceType?.GetCustomAttribute<BannouServiceAttribute>();
        return bannouServiceAttr?.Layer ?? ServiceLayer.GameFeatures;
    }

    /// <summary>
    /// Get the sub-ordering for infrastructure plugins (L0).
    /// Returns the InfrastructureLoadOrder value for L0 plugins, or int.MaxValue for others.
    /// This ensures correct internal ordering: telemetry → state → messaging → mesh.
    /// </summary>
    private int GetInfrastructureSubOrder(IBannouPlugin plugin)
    {
        if (InfrastructureLoadOrder.TryGetValue(plugin.PluginName, out var order))
            return order;
        return int.MaxValue; // Non-infrastructure plugins sort after all L0 plugins
    }

    /// <summary>
    /// Load a plugin from a specific directory.
    /// </summary>
    /// <param name="pluginDirectory">Directory containing the plugin</param>
    /// <param name="expectedPluginName">Expected name of the plugin</param>
    /// <returns>Loaded plugin instance or null if loading failed</returns>
    private async Task<IBannouPlugin?> LoadPluginFromDirectoryAsync(string pluginDirectory, string expectedPluginName)
    {
        await Task.CompletedTask;
        _logger.LogDebug("Loading plugin from directory: {PluginDirectory}", pluginDirectory);

        // Find the main assembly (usually lib-{service}.dll)
        var assemblyPath = Path.Combine(pluginDirectory, $"lib-{expectedPluginName}.dll");
        if (!File.Exists(assemblyPath))
        {
            _logger.LogWarning("Plugin assembly not found: {AssemblyPath}", assemblyPath);
            return null;
        }

        try
        {
            // Check if assembly is already loaded to avoid conflicts
            var assemblyName = AssemblyName.GetAssemblyName(assemblyPath);
            var assembly = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == assemblyName.Name);

            if (assembly == null)
            {
                // Load the assembly if not already loaded
                assembly = Assembly.LoadFrom(assemblyPath);
                _logger.LogDebug("Loaded new assembly: {AssemblyName} from {AssemblyPath}",
                    assembly.GetName().Name, assemblyPath);
            }
            else
            {
                _logger.LogDebug("Using already loaded assembly: {AssemblyName}", assembly.GetName().Name);
            }

            _loadedAssemblies[expectedPluginName] = assembly;

            // Find types that implement IBannouPlugin
            var pluginTypes = assembly.GetTypes()
                .Where(t => typeof(IBannouPlugin).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract)
                .ToList();

            if (pluginTypes.Count == 0)
            {
                _logger.LogWarning("No IBannouPlugin implementations found in assembly: {AssemblyPath}", assemblyPath);
                return null;
            }

            if (pluginTypes.Count > 1)
            {
                _logger.LogWarning("Multiple IBannouPlugin implementations found in assembly: {AssemblyPath}. Using first one.", assemblyPath);
            }

            // Create plugin instance
            var pluginType = pluginTypes.First();
            var plugin = Activator.CreateInstance(pluginType) as IBannouPlugin;

            if (plugin == null)
            {
                _logger.LogError("Failed to create plugin instance for type: {PluginType}", pluginType.Name);
                return null;
            }

            // Validate plugin
            if (!plugin.ValidatePlugin())
            {
                _logger.LogWarning("Plugin validation failed: {PluginName}", plugin.PluginName);
                return null;
            }

            // Verify plugin name matches expected
            if (!string.Equals(plugin.PluginName, expectedPluginName, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Plugin name mismatch. Expected: {Expected}, Got: {Actual}",
                    expectedPluginName, plugin.PluginName);
            }

            return plugin;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load plugin assembly: {AssemblyPath}", assemblyPath);
            return null;
        }
    }
}
