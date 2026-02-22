#nullable enable

namespace BeyondImmersion.BannouService.Services;

/// <summary>
/// Provides the unique identity of this node in the mesh network.
/// </summary>
/// <remarks>
/// <para>
/// Every Bannou node has a unique instance ID used for mesh registration,
/// heartbeat identification, error event sourcing, and distributed coordination.
/// The ID is stable for the lifetime of the process.
/// </para>
/// <para>
/// Registered by lib-mesh (L0 Infrastructure) as a singleton.
/// The default implementation generates a random GUID on first access.
/// Orchestrated deployments can override via <c>MESH_INSTANCE_ID</c> environment variable
/// for stable node identities, or via <c>--force-service-id</c> CLI argument.
/// </para>
/// </remarks>
public interface IMeshInstanceIdentifier
{
    /// <summary>
    /// The unique identity of this node in the mesh network.
    /// Stable for the lifetime of the process.
    /// </summary>
    Guid InstanceId { get; }
}

/// <summary>
/// Default implementation of <see cref="IMeshInstanceIdentifier"/>.
/// Uses a configured value if provided, otherwise generates a random GUID once.
/// </summary>
public sealed class DefaultMeshInstanceIdentifier : IMeshInstanceIdentifier
{
    /// <inheritdoc/>
    public Guid InstanceId { get; }

    /// <summary>
    /// Creates a new instance with an explicit ID or a randomly generated one.
    /// </summary>
    /// <param name="configuredId">
    /// Explicit instance ID from configuration (e.g., <c>MESH_INSTANCE_ID</c>).
    /// When null, a random GUID is generated.
    /// </param>
    public DefaultMeshInstanceIdentifier(Guid? configuredId = null)
    {
        InstanceId = configuredId ?? Guid.NewGuid();
    }
}
