using BeyondImmersion.BannouService.Events;

namespace BeyondImmersion.BannouService.Asset;

/// <summary>
/// Event subscription handlers for Asset service.
/// Currently empty as Asset service is primarily an event publisher.
/// </summary>
/// <remarks>
/// <para>
/// The Asset service publishes events for:
/// - Asset upload lifecycle (requested, completed)
/// - Asset processing (queued, completed)
/// - Asset availability (ready)
/// - Bundle creation
/// </para>
/// <para>
/// Future subscriptions may include:
/// - Processing completion from pool workers (when delegated processing is implemented)
/// - Cleanup events from Orchestrator
/// </para>
/// </remarks>
public partial class AssetService
{
    /// <summary>
    /// Register event consumers for the Asset service.
    /// Called from constructor after all dependencies are initialized.
    /// </summary>
    /// <param name="eventConsumer">The event consumer for registering handlers.</param>
    protected void RegisterEventConsumers(IEventConsumer eventConsumer)
    {
        // Currently, the Asset service does not subscribe to external events.
        //
        // When processing pool workers are implemented, they will call back
        // via Bannou service invocation rather than pub/sub, so the AssetService
        // interface will expose a completion endpoint.
        //
        // Future subscriptions may include:
        // - orchestrator.cleanup events for removing orphaned assets
        // - account.deleted events for cascading asset deletions
        _ = eventConsumer; // Suppress unused parameter warning until subscriptions are added
    }
}
