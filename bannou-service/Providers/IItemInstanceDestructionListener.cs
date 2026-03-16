// =============================================================================
// Item Instance Destruction Listener Interface
// Enables in-process notification when item instances are destroyed.
// Item (L2) discovers listeners via DI; L4 services implement them for targeted dispatch.
// High-frequency exception per FOUNDATION TENETS (T28): DI Listener + orphan reconciliation
// worker instead of event subscription for instance-level cleanup.
// =============================================================================

namespace BeyondImmersion.BannouService.Providers;

/// <summary>
/// Listener interface for receiving item instance destruction notifications via DI.
/// </summary>
/// <remarks>
/// <para>
/// This interface enables targeted in-process delivery of item instance destruction events
/// to co-located consumers. It exists as a high-frequency exception to the standard
/// lib-resource cleanup pattern (per FOUNDATION TENETS): at 100K NPC scale, item creation
/// and destruction occur at loot/combat/trading frequency. Event-based cleanup would flood
/// the message bus; lib-resource reference tracking at per-item granularity would create
/// unsustainable overhead. DI listener dispatch is the required mechanism.
/// </para>
/// <list type="bullet">
///   <item>Item (L2) discovers this interface via <c>IEnumerable&lt;IItemInstanceDestructionListener&gt;</c></item>
///   <item>L4 services implement the listener and register as Singleton</item>
///   <item>Item calls listeners AFTER state is deleted and batch events are recorded</item>
///   <item>Listener failures are logged as warnings and never affect core item logic or other listeners</item>
/// </list>
/// <para>
/// <strong>DISTRIBUTED DEPLOYMENT NOTE</strong>: Listeners are LOCAL-ONLY fan-out.
/// Only listeners co-located on the same node where the item destruction occurs are called.
/// In a multi-node deployment, nodes that do not process the destroy API request will NOT
/// have their listeners invoked.
/// </para>
/// <para>
/// This is safe when the listener's reaction writes to distributed state (Redis, MySQL)
/// because all nodes read from the same distributed store. Consumers MUST implement an
/// orphan reconciliation background worker as the durability guarantee for missed notifications
/// (node partitioning, listener exceptions, deployment gaps).
/// </para>
/// <para>
/// <b>Example Implementation</b>:
/// </para>
/// <code>
/// public class AffixItemDestructionListener : IItemInstanceDestructionListener
/// {
///     public async Task OnItemInstanceDestroyedAsync(
///         ItemInstanceDestructionNotification notification, CancellationToken ct)
///     {
///         // Clean up affix instance data for the destroyed item
///         await _affixInstanceStore.DeleteAsync(key, ct);
///     }
/// }
/// // DI registration: services.AddSingleton&lt;IItemInstanceDestructionListener, AffixItemDestructionListener&gt;();
/// </code>
/// </remarks>
public interface IItemInstanceDestructionListener
{
    /// <summary>
    /// Called after an item instance is destroyed, allowing dependent services to clean up their own state.
    /// </summary>
    /// <param name="notification">Details of the destroyed item instance.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <remarks>
    /// Implementations should handle their own errors internally. Exceptions thrown
    /// from this method are caught by Item and logged as warnings — they never
    /// affect the destroy operation, other listeners, or the caller.
    /// </remarks>
    Task OnItemInstanceDestroyedAsync(ItemInstanceDestructionNotification notification, CancellationToken ct);
}

/// <summary>
/// Notification data for an item instance destruction, delivered via DI to destruction listeners.
/// </summary>
/// <param name="InstanceId">The destroyed item instance ID.</param>
/// <param name="TemplateId">The item template ID for the destroyed instance.</param>
/// <param name="GameId">The game service ID string from the item template (null if template was not found).</param>
/// <param name="ContainerId">The container the item was in (null if uncontained).</param>
/// <param name="RealmId">The realm the item existed in.</param>
/// <param name="DestroyedAt">When the destruction occurred.</param>
public record ItemInstanceDestructionNotification(
    Guid InstanceId,
    Guid TemplateId,
    string? GameId,
    Guid? ContainerId,
    Guid RealmId,
    DateTimeOffset DestroyedAt);
