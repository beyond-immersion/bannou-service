using Microsoft.Extensions.Logging;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

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
    /// <param name="presetsDirectory">Directory containing preset YAML files.</param>
    public PresetLoader(ILogger<PresetLoader> logger, string? presetsDirectory = null)
    {
        _logger = logger;
        _presetsDirectory = presetsDirectory ?? GetDefaultPresetsDirectory();
        _deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();
    }

    /// <summary>
    /// Gets the default presets directory path.
    /// </summary>
    private static string GetDefaultPresetsDirectory()
    {
        // Check environment variable first
        var envPath = Environment.GetEnvironmentVariable("BANNOU_PRESETS_DIR");
        if (!string.IsNullOrEmpty(envPath) && Directory.Exists(envPath))
        {
            return envPath;
        }

        // Check relative to working directory
        var relativePath = Path.Combine("provisioning", "orchestrator", "presets");
        if (Directory.Exists(relativePath))
        {
            return relativePath;
        }

        // Check common container paths
        var containerPath = "/app/provisioning/orchestrator/presets";
        if (Directory.Exists(containerPath))
        {
            return containerPath;
        }

        // Fallback to relative path even if it doesn't exist
        return relativePath;
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
                    DaprEnabled = node.DaprEnabled ?? true,
                    DaprAppId = node.DaprAppId ?? node.Name // Default to node name if not specified
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

                topologyNode.Environment = mergedEnv;
                topology.Nodes.Add(topologyNode);
            }
        }

        // TODO: Handle infrastructure configuration when implemented
        // topology.Infrastructure = ConvertInfrastructure(preset.Topology?.Infrastructure);

        return topology;
    }
}

/// <summary>
/// Preset definition as loaded from YAML.
/// </summary>
public class PresetDefinition
{
    /// <summary>
    /// Preset name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Human-readable description.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Category (development, testing, production).
    /// </summary>
    public string? Category { get; set; }

    /// <summary>
    /// Topology definition.
    /// </summary>
    public PresetTopology? Topology { get; set; }

    /// <summary>
    /// Global environment variables.
    /// </summary>
    public Dictionary<string, string>? Environment { get; set; }

    /// <summary>
    /// Required backends for this preset.
    /// </summary>
    public List<string>? RequiredBackends { get; set; }
}

/// <summary>
/// Topology definition within a preset.
/// </summary>
public class PresetTopology
{
    /// <summary>
    /// List of topology nodes.
    /// </summary>
    public List<PresetNode>? Nodes { get; set; }

    /// <summary>
    /// Infrastructure configuration.
    /// </summary>
    public PresetInfrastructure? Infrastructure { get; set; }
}

/// <summary>
/// Node definition within a preset topology.
/// </summary>
public class PresetNode
{
    /// <summary>
    /// Node name (container name).
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Services enabled on this node.
    /// </summary>
    public List<string>? Services { get; set; }

    /// <summary>
    /// Number of replicas.
    /// </summary>
    public int? Replicas { get; set; }

    /// <summary>
    /// Node-specific environment variables.
    /// </summary>
    public Dictionary<string, string>? Environment { get; set; }

    /// <summary>
    /// Whether Dapr sidecar is enabled.
    /// </summary>
    public bool? DaprEnabled { get; set; }

    /// <summary>
    /// Override Dapr app-id.
    /// </summary>
    public string? DaprAppId { get; set; }
}

/// <summary>
/// Infrastructure configuration within a preset.
/// </summary>
public class PresetInfrastructure
{
    /// <summary>
    /// MySQL configuration.
    /// </summary>
    public PresetInfraService? Mysql { get; set; }

    /// <summary>
    /// Redis configuration.
    /// </summary>
    public PresetInfraService? Redis { get; set; }

    /// <summary>
    /// RabbitMQ configuration.
    /// </summary>
    public PresetInfraService? Rabbitmq { get; set; }
}

/// <summary>
/// Individual infrastructure service configuration.
/// </summary>
public class PresetInfraService
{
    /// <summary>
    /// Whether the service is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Service version.
    /// </summary>
    public string? Version { get; set; }
}

/// <summary>
/// Preset metadata for listing.
/// </summary>
public class PresetMetadata
{
    /// <summary>
    /// Preset name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Human-readable description.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Category.
    /// </summary>
    public string? Category { get; set; }

    /// <summary>
    /// Required backends.
    /// </summary>
    public List<string> RequiredBackends { get; set; } = new();
}
