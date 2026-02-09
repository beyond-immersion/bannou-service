namespace BeyondImmersion.BannouService.Orchestrator;

/// <summary>
/// Internal data models for OrchestratorService.
/// </summary>
/// <remarks>
/// <para>
/// This file contains internal data models, DTOs, and helper structures used
/// exclusively by this service. These are NOT exposed via the API and are NOT
/// generated from schemas.
/// </para>
/// <para>
/// <b>When to add models here:</b>
/// <list type="bullet">
///   <item>Storage models for state stores (different from API request/response types)</item>
///   <item>Cache entry structures</item>
///   <item>Internal DTOs for service-to-service communication not exposed in API</item>
///   <item>Helper records for intermediate processing</item>
/// </list>
/// </para>
/// <para>
/// <b>IMPLEMENTATION TENETS - Type Safety:</b> Internal models MUST use proper C# types
/// (enums, Guids, DateTimeOffset) - never string representations. "JSON requires strings"
/// is FALSE - BannouJson handles serialization correctly.
/// </para>
/// </remarks>
public partial class OrchestratorService
{
    // This partial class declaration exists to signal that the models below
    // are owned by and used exclusively by this service. The models themselves
    // are defined at namespace level as internal classes.
}

// ============================================================================
// PRESET MODELS (used by PresetLoader for YAML-based deployment presets)
// ============================================================================

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
    public int MinInstances { get; set; } = 1;

    /// <summary>
    /// Maximum number of instances allowed.
    /// </summary>
    public int MaxInstances { get; set; } = 5;

    /// <summary>
    /// Scale up when utilization exceeds this threshold (0.0-1.0).
    /// </summary>
    public double ScaleUpThreshold { get; set; } = 0.8;

    /// <summary>
    /// Scale down when utilization drops below this threshold (0.0-1.0).
    /// </summary>
    public double ScaleDownThreshold { get; set; } = 0.2;

    /// <summary>
    /// Time in minutes before idle workers are cleaned up.
    /// </summary>
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
