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
// RESTART MODELS (internal signaling between SmartRestartManager and service)
// ============================================================================

/// <summary>
/// Internal result from SmartRestartManager, separate from the API response model.
/// Carries success/failure signaling that the service method uses to choose the HTTP status code.
/// </summary>
/// <param name="Succeeded">Whether the restart completed and the service became healthy.</param>
/// <param name="DeclineReason">Non-null if the restart was declined or failed. Used by the service to distinguish 409 (declined) from 500 (failed).</param>
/// <param name="Duration">Formatted duration of the restart operation.</param>
/// <param name="PreviousStatus">Service health status before the restart attempt.</param>
/// <param name="CurrentStatus">Service health status after the restart attempt.</param>
public record RestartOutcome(
    bool Succeeded,
    string? DeclineReason,
    string Duration,
    InstanceHealthStatus? PreviousStatus,
    InstanceHealthStatus? CurrentStatus);

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
    /// Service layers to enable on this node.
    /// When specified, listed layers are enabled and unlisted layers are disabled.
    /// Individual services in the 'services' list override layer settings.
    /// Valid values: AppFoundation, GameFoundation, AppFeatures, GameFeatures, Extensions.
    /// </summary>
    public List<string>? Layers { get; set; }

    /// <summary>
    /// Individual services to enable on this node (overrides layer settings).
    /// When used with 'layers', these act as additional overrides for services
    /// outside the enabled layers. When used without 'layers', falls back to
    /// SERVICES_ENABLED=false with per-service enables for backward compatibility.
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

// ============================================================================
// PROCESSING POOL MODELS (state store models for pool management)
// ============================================================================

/// <summary>
/// Status of a processor instance in the pool.
/// </summary>
public enum ProcessorStatus
{
    /// <summary>Processor is ready to handle requests.</summary>
    Available,
    /// <summary>Processor is starting up and not yet ready.</summary>
    Pending
}

/// <summary>
/// Represents a processor instance in the pool.
/// </summary>
public sealed class ProcessorInstance
{
    /// <summary>Unique identifier for this processor instance.</summary>
    public string ProcessorId { get; set; } = string.Empty;

    /// <summary>Dapr app-id of the processor container.</summary>
    public string AppId { get; set; } = string.Empty;

    /// <summary>Pool type this processor belongs to (e.g., "actor-shared", "asset-image").</summary>
    public string PoolType { get; set; } = string.Empty;

    /// <summary>Current availability status of the processor.</summary>
    public ProcessorStatus Status { get; set; } = ProcessorStatus.Available;

    /// <summary>When the processor instance was created.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>When the processor instance was last updated.</summary>
    public DateTimeOffset LastUpdated { get; set; }
}

/// <summary>
/// Represents an active lease for a processor.
/// </summary>
public sealed class ProcessorLease
{
    /// <summary>Unique identifier for this lease.</summary>
    public Guid LeaseId { get; set; }

    /// <summary>Identifier of the leased processor instance.</summary>
    public string ProcessorId { get; set; } = string.Empty;

    /// <summary>Dapr app-id of the leased processor container.</summary>
    public string AppId { get; set; } = string.Empty;

    /// <summary>Pool type the leased processor belongs to.</summary>
    public string PoolType { get; set; } = string.Empty;

    /// <summary>When the lease was acquired.</summary>
    public DateTimeOffset AcquiredAt { get; set; }

    /// <summary>When the lease expires.</summary>
    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>Lease priority for ordering when multiple leases compete.</summary>
    public int Priority { get; set; }

    /// <summary>Caller-provided opaque metadata associated with the lease.</summary>
    public object? Metadata { get; set; }
}

/// <summary>
/// Pool configuration stored in state store.
/// Contains all settings needed to spawn pool worker containers.
/// </summary>
public sealed class PoolConfiguration
{
    /// <summary>Pool type identifier (e.g., "actor-shared", "asset-image").</summary>
    public string PoolType { get; set; } = string.Empty;

    /// <summary>Service/plugin name to enable on pool workers.</summary>
    public string ServiceName { get; set; } = string.Empty;

    /// <summary>Docker image for pool workers. If empty, uses default bannou image.</summary>
    public string? Image { get; set; }

    /// <summary>Environment variables for pool worker containers.</summary>
    public Dictionary<string, string>? Environment { get; set; }

    /// <summary>Minimum number of instances to maintain.</summary>
    public int MinInstances { get; set; } = 1;

    /// <summary>Maximum number of instances allowed.</summary>
    public int MaxInstances { get; set; } = 5;

    /// <summary>Scale up when utilization exceeds this threshold (0.0-1.0).</summary>
    public double ScaleUpThreshold { get; set; } = 0.8;

    /// <summary>Scale down when utilization drops below this threshold (0.0-1.0).</summary>
    public double ScaleDownThreshold { get; set; } = 0.2;

    /// <summary>Time in minutes before idle workers are cleaned up.</summary>
    public int IdleTimeoutMinutes { get; set; } = 5;
}

/// <summary>
/// Pool metrics data stored in state store.
/// </summary>
public sealed class PoolMetricsData
{
    /// <summary>Number of jobs completed in the last hour.</summary>
    public int JobsCompleted1h { get; set; }

    /// <summary>Number of jobs failed in the last hour.</summary>
    public int JobsFailed1h { get; set; }

    /// <summary>Average processing time in milliseconds over the metrics window.</summary>
    public int AvgProcessingTimeMs { get; set; }

    /// <summary>When the last scaling event occurred.</summary>
    public DateTimeOffset LastScaleEvent { get; set; }

    /// <summary>Start of the current metrics aggregation window.</summary>
    public DateTimeOffset WindowStart { get; set; }
}

