namespace BeyondImmersion.BannouService.Services;

/// <summary>
/// Optional base type for service handlers.
/// Provides default implementation for InstanceId and other common service functionality.
/// </summary>
public abstract class DaprService : IDaprService
{
    /// <summary>
    /// Unique instance identifier for this service plugin.
    /// Generated once per service instance lifetime.
    /// Used for log correlation and debugging across distributed systems.
    /// </summary>
    public Guid InstanceId { get; } = Guid.NewGuid();
}
