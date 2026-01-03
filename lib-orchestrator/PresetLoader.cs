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
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _presetsDirectory = presetsDirectory ?? throw new ArgumentNullException(nameof(presetsDirectory));
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

                // CRITICAL: Translate services list to proper service enable/disable environment variables
                // Without this, deployed containers have all services enabled by default (including
                // services like Asset that require infrastructure like MinIO that won't be available).
                // Set SERVICES_ENABLED=false so only explicitly listed services are enabled.
                if (node.Services != null && node.Services.Count > 0)
                {
                    // Only set if not already explicitly configured
                    if (!mergedEnv.ContainsKey("SERVICES_ENABLED"))
                    {
                        mergedEnv["SERVICES_ENABLED"] = "false";
                    }

                    // Enable each service listed in the preset
                    foreach (var serviceName in node.Services)
                    {
                        // Convert service name to environment variable format
                        // e.g., "auth" -> "AUTH_SERVICE_ENABLED", "game-session" -> "GAME_SESSION_SERVICE_ENABLED"
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

    /// <summary>
    /// Processing pools configuration for on-demand worker containers.
    /// Used by actor pools, asset processors, etc.
    /// </summary>
    [YamlMember(Alias = "processing_pools")]
    public List<PresetProcessingPool>? ProcessingPools { get; set; }
}

/// <summary>
/// Processing pool configuration for on-demand worker containers.
/// These are NOT topology nodes - they're spawned individually by the control plane.
/// </summary>
public class PresetProcessingPool
{
    /// <summary>
    /// Pool type identifier (e.g., "actor-shared", "asset-image").
    /// </summary>
    [YamlMember(Alias = "pool_type")]
    public string PoolType { get; set; } = string.Empty;

    /// <summary>
    /// Service/plugin name to enable on pool workers (e.g., "actor", "asset").
    /// Maps to {PLUGIN}_SERVICE_ENABLED environment variable.
    /// </summary>
    public string Plugin { get; set; } = string.Empty;

    /// <summary>
    /// Docker image to use for pool workers. If empty, uses default bannou image.
    /// </summary>
    public string? Image { get; set; }

    /// <summary>
    /// Minimum number of instances to maintain.
    /// </summary>
    [YamlMember(Alias = "min_instances")]
    public int MinInstances { get; set; } = 1;

    /// <summary>
    /// Maximum number of instances allowed.
    /// </summary>
    [YamlMember(Alias = "max_instances")]
    public int MaxInstances { get; set; } = 5;

    /// <summary>
    /// Scale up when utilization exceeds this threshold (0.0-1.0).
    /// </summary>
    [YamlMember(Alias = "scale_up_threshold")]
    public double ScaleUpThreshold { get; set; } = 0.8;

    /// <summary>
    /// Scale down when utilization drops below this threshold (0.0-1.0).
    /// </summary>
    [YamlMember(Alias = "scale_down_threshold")]
    public double ScaleDownThreshold { get; set; } = 0.2;

    /// <summary>
    /// Time in minutes before idle workers are cleaned up.
    /// </summary>
    [YamlMember(Alias = "idle_timeout_minutes")]
    public int IdleTimeoutMinutes { get; set; } = 5;

    /// <summary>
    /// Environment variables for pool worker containers.
    /// </summary>
    public Dictionary<string, string>? Environment { get; set; }
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
    /// Whether mesh routing is enabled.
    /// </summary>
    public bool? MeshEnabled { get; set; }

    /// <summary>
    /// Override app-id for mesh routing.
    /// </summary>
    public string? AppId { get; set; }
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
