namespace BeyondImmersion.BannouService.Orchestrator;

/// <summary>
/// Service heartbeat event from common-events.yaml schema.
/// Published by services to indicate health and capacity status.
/// </summary>
public class ServiceHeartbeatEvent
{
    public required string EventId { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public required string ServiceId { get; set; }      // e.g., "behavior", "accounts"
    public required string AppId { get; set; }          // e.g., "bannou", "npc-omega-01"
    public required string Status { get; set; }          // healthy, degraded, overloaded, shutting_down
    public ServiceCapacity? Capacity { get; set; }
    public Dictionary<string, object>? Metadata { get; set; }
}

/// <summary>
/// Service capacity information from heartbeat.
/// </summary>
public class ServiceCapacity
{
    public int MaxConnections { get; set; }
    public int CurrentConnections { get; set; }
    public double CpuUsage { get; set; }
    public double MemoryUsage { get; set; }
}
