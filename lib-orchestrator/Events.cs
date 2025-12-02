namespace BeyondImmersion.BannouService.Orchestrator;

/// <summary>
/// Service heartbeat event from common-events.yaml schema.
/// Published by bannou instances to indicate health and capacity status.
/// Contains aggregated information for ALL services hosted by the instance.
/// </summary>
public class ServiceHeartbeatEvent
{
    /// <summary>Unique identifier for this heartbeat event.</summary>
    public required string EventId { get; set; }

    /// <summary>When the heartbeat was sent.</summary>
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Unique GUID identifying this bannou instance (for log correlation).</summary>
    public required Guid ServiceId { get; set; }

    /// <summary>Dapr app-id for this instance (e.g., "bannou", "jobberwocky").</summary>
    public required string AppId { get; set; }

    /// <summary>Overall instance health status.</summary>
    public required string Status { get; set; }

    /// <summary>Status of each service/plugin hosted by this instance.</summary>
    public required List<ServiceStatus> Services { get; set; }

    /// <summary>Instance-level capacity and load information.</summary>
    public InstanceCapacity? Capacity { get; set; }

    /// <summary>List of current issues affecting this instance.</summary>
    public List<string>? Issues { get; set; }

    /// <summary>Additional instance-level metadata.</summary>
    public Dictionary<string, object>? Metadata { get; set; }
}

/// <summary>
/// Status of an individual service/plugin within an app instance.
/// </summary>
public class ServiceStatus
{
    /// <summary>Unique GUID identifying this plugin instance (for log correlation).</summary>
    public required Guid ServiceId { get; set; }

    /// <summary>Service name (e.g., "auth", "accounts", "behavior").</summary>
    public required string ServiceName { get; set; }

    /// <summary>Individual service health status.</summary>
    public required string Status { get; set; }

    /// <summary>Service API version.</summary>
    public string? Version { get; set; }

    /// <summary>Service-specific metadata from OnHeartbeat callback.</summary>
    public Dictionary<string, object>? Metadata { get; set; }
}

/// <summary>
/// Instance-level capacity and load information.
/// </summary>
public class InstanceCapacity
{
    public int MaxConnections { get; set; }
    public int CurrentConnections { get; set; }
    public float CpuUsage { get; set; }
    public float MemoryUsage { get; set; }
}
