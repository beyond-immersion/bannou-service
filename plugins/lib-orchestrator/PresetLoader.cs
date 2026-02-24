using Microsoft.Extensions.Logging;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

// YamlMember is used for snake_case YAML field mapping

namespace BeyondImmersion.BannouService.Orchestrator;

/// <summary>
/// Loads deployment presets from YAML files.
/// </summary>
public class PresetLoader
{
    private readonly ILogger<PresetLoader> _logger;
    private readonly string _presetsDirectory;
    private readonly IDeserializer _deserializer;

    /// <summary>
    /// Initializes a new instance of the PresetLoader.
    /// </summary>
    /// <param name="logger">Logger instance.</param>
    /// <param name="presetsDirectory">Directory containing preset YAML files. Required - from OrchestratorServiceConfiguration.</param>
    public PresetLoader(ILogger<PresetLoader> logger, string presetsDirectory)
    {
        _logger = logger;
        _presetsDirectory = presetsDirectory;
        _deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();
    }

    /// <summary>
    /// Lists all available presets.
    /// </summary>
    /// <returns>List of preset metadata.</returns>
    public async Task<List<PresetMetadata>> ListPresetsAsync(CancellationToken cancellationToken = default)
    {
        var presets = new List<PresetMetadata>();

        if (!Directory.Exists(_presetsDirectory))
        {
            _logger.LogWarning("Presets directory not found: {Directory}", _presetsDirectory);
            return presets;
        }

        foreach (var file in Directory.GetFiles(_presetsDirectory, "*.yaml"))
        {
            try
            {
                var preset = await LoadPresetAsync(Path.GetFileNameWithoutExtension(file), cancellationToken);
                if (preset != null)
                {
                    presets.Add(new PresetMetadata
                    {
                        Name = preset.Name,
                        Description = preset.Description,
                        Category = preset.Category,
                        RequiredBackends = preset.RequiredBackends ?? new List<string>()
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load preset metadata from {File}", file);
            }
        }

        return presets;
    }

    /// <summary>
    /// Loads a preset by name.
    /// </summary>
    /// <param name="presetName">Name of the preset (without .yaml extension).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The loaded preset, or null if not found.</returns>
    public async Task<PresetDefinition?> LoadPresetAsync(string presetName, CancellationToken cancellationToken = default)
    {
        var filePath = Path.Combine(_presetsDirectory, $"{presetName}.yaml");

        if (!File.Exists(filePath))
        {
            _logger.LogWarning("Preset file not found: {FilePath}", filePath);
            return null;
        }

        try
        {
            var yamlContent = await File.ReadAllTextAsync(filePath, cancellationToken);
            var preset = _deserializer.Deserialize<PresetDefinition>(yamlContent);

            if (preset == null)
            {
                _logger.LogWarning("Failed to deserialize preset: {PresetName}", presetName);
                return null;
            }

            // Ensure name is set (use filename if not specified in YAML)
            if (string.IsNullOrEmpty(preset.Name))
            {
                preset.Name = presetName;
            }

            _logger.LogInformation(
                "Loaded preset: {PresetName} with {NodeCount} nodes",
                preset.Name,
                preset.Topology?.Nodes?.Count ?? 0);

            return preset;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading preset: {PresetName}", presetName);
            throw;
        }
    }

    /// <summary>
    /// Converts a preset definition to a ServiceTopology for deployment.
    /// </summary>
    /// <param name="preset">The preset to convert.</param>
    /// <returns>A ServiceTopology ready for deployment.</returns>
    public ServiceTopology ConvertToTopology(PresetDefinition preset)
    {
        var topology = new ServiceTopology
        {
            Nodes = new List<TopologyNode>()
        };

        if (preset.Topology?.Nodes != null)
        {
            foreach (var node in preset.Topology.Nodes)
            {
                var topologyNode = new TopologyNode
                {
                    Name = node.Name,
                    Services = node.Services ?? new List<string>(),
                    Replicas = node.Replicas ?? 1,
                    MeshEnabled = node.MeshEnabled ?? true,
                    AppId = node.AppId ?? node.Name // Default to node name if not specified
                };

                // Merge node-specific and global environment
                var mergedEnv = new Dictionary<string, string>();

                // Add global environment first
                if (preset.Environment != null)
                {
                    foreach (var kvp in preset.Environment)
                    {
                        mergedEnv[kvp.Key] = kvp.Value;
                    }
                }

                // Add node-specific environment (overrides global)
                if (node.Environment != null)
                {
                    foreach (var kvp in node.Environment)
                    {
                        mergedEnv[kvp.Key] = kvp.Value;
                    }
                }

                // Translate layer and service lists to environment variables.
                // Two modes:
                // 1. layers: [AppFoundation, GameFoundation] → BANNOU_ENABLE_* env vars per layer
                // 2. services: [auth, account] (without layers) → BANNOU_SERVICES_ENABLED=false + per-service enables
                // Both can be combined: layers set the base, services add individual overrides.
                if (node.Layers != null && node.Layers.Count > 0)
                {
                    // Layer-based enablement: enable listed layers, disable unlisted
                    var allLayers = new[] { "APP_FOUNDATION", "GAME_FOUNDATION", "APP_FEATURES", "GAME_FEATURES", "EXTENSIONS" };
                    foreach (var layerEnvSuffix in allLayers)
                    {
                        var envKey = $"BANNOU_ENABLE_{layerEnvSuffix}";
                        if (!mergedEnv.ContainsKey(envKey))
                        {
                            // Match: "AppFoundation" → "APP_FOUNDATION", "app_foundation" → "APP_FOUNDATION", etc.
                            var isEnabled = node.Layers.Any(l =>
                                string.Equals(
                                    l.Replace(" ", "_").ToUpperInvariant(),
                                    layerEnvSuffix,
                                    StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(
                                    string.Concat(l.Select((c, i) => i > 0 && char.IsUpper(c) ? $"_{c}" : $"{c}")).ToUpperInvariant(),
                                    layerEnvSuffix,
                                    StringComparison.OrdinalIgnoreCase));

                            mergedEnv[envKey] = isEnabled ? "true" : "false";
                        }
                    }
                }

                // Individual service overrides (works with or without layers)
                if (node.Services != null && node.Services.Count > 0)
                {
                    // When NO layers specified, use BANNOU_SERVICES_ENABLED=false + per-service enables
                    if ((node.Layers == null || node.Layers.Count == 0) && !mergedEnv.ContainsKey("BANNOU_SERVICES_ENABLED"))
                    {
                        mergedEnv["BANNOU_SERVICES_ENABLED"] = "false";
                    }

                    // Enable each individually listed service
                    foreach (var serviceName in node.Services)
                    {
                        var envVarName = serviceName.ToUpperInvariant().Replace("-", "_") + "_SERVICE_ENABLED";
                        if (!mergedEnv.ContainsKey(envVarName))
                        {
                            mergedEnv[envVarName] = "true";
                        }
                    }
                }

                topologyNode.Environment = mergedEnv;
                topology.Nodes.Add(topologyNode);
            }
        }

        // TODO: Handle infrastructure configuration when implemented
        // topology.Infrastructure = ConvertInfrastructure(preset.Topology?.Infrastructure);

        return topology;
    }
}
