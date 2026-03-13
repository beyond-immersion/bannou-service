// =============================================================================
// Service Mapping Receiver Interface
// Enables Orchestrator (L3) to push service-to-appId mapping updates into Mesh (L0)
// without requiring Mesh to subscribe to higher-layer events (T27 compliance).
// Mesh (L0) provides the default implementation; Orchestrator discovers and calls it.
// =============================================================================

namespace BeyondImmersion.BannouService.Providers;

/// <summary>
/// Receiver interface for service-to-appId mapping updates via DI.
/// </summary>
/// <remarks>
/// <para>
/// This interface resolves a T27 (Cross-Service Communication Discipline) violation
/// where Mesh (L0) previously subscribed to <c>bannou.full-service-mappings</c> events
/// published by Orchestrator (L3). Lower-layer services must never subscribe to
/// higher-layer events.
/// </para>
/// <para>
/// <strong>Architecture</strong>:
/// </para>
/// <list type="bullet">
///   <item>Mesh (L0) provides the default implementation (<c>MeshServiceMappingReceiver</c>)</item>
///   <item>The implementation updates the local <see cref="Services.IServiceAppMappingResolver"/>
///     and publishes <c>mesh.mappings.updated</c> (L0→L0) events for cross-node sync</item>
///   <item>Orchestrator (L3) discovers implementations via <c>IEnumerable&lt;IServiceMappingReceiver&gt;</c></item>
///   <item>Orchestrator pushes live mapping updates by calling <see cref="UpdateMappingsAsync"/></item>
///   <item>Other mesh nodes subscribe to <c>mesh.mappings.updated</c> to stay in sync</item>
/// </list>
/// <para>
/// <strong>DISTRIBUTED DEPLOYMENT NOTE</strong>: Only one Orchestrator exists in the
/// network (control plane). The DI call is local-only — it updates the co-located Mesh
/// implementation. The Mesh implementation then broadcasts an L0 event so all other nodes
/// receive the update via their event subscription.
/// </para>
/// </remarks>
public interface IServiceMappingReceiver
{
    /// <summary>
    /// Receives a full mapping update, applying it to the local routing resolver
    /// and broadcasting to other nodes via L0 events.
    /// </summary>
    /// <param name="mappings">Complete dictionary of serviceName → appId mappings.
    /// Empty dictionary means "reset to default routing" (all services → default appId).</param>
    /// <param name="defaultAppId">Default app-id for unmapped services (typically "bannou").</param>
    /// <param name="version">Monotonically increasing version for ordering. Stale versions are rejected.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if mappings were applied, false if version was stale.</returns>
    Task<bool> UpdateMappingsAsync(
        IReadOnlyDictionary<string, string> mappings,
        string defaultAppId,
        long version,
        CancellationToken ct = default);
}
