// =============================================================================
// Collection Unlock Listener Interface
// Enables in-process notification when collection entries are unlocked.
// Collection (L2) discovers listeners via DI; other L2 services implement them.
// =============================================================================

namespace BeyondImmersion.BannouService.Providers;

/// <summary>
/// Listener interface for receiving collection entry unlock notifications via DI.
/// </summary>
/// <remarks>
/// <para>
/// This interface enables guaranteed in-process delivery for the Collection-to-Seed
/// growth pipeline (and any future consumers) without relying on event bus delivery:
/// </para>
/// <list type="bullet">
///   <item>Collection (L2) defines and discovers this interface via <c>IEnumerable&lt;ICollectionUnlockListener&gt;</c></item>
///   <item>Seed (L2) implements the listener and registers as Singleton</item>
///   <item>Collection calls listeners during <c>GrantEntryAsync</c> after state is saved and events are published</item>
///   <item>Listener failures are logged as warnings and never affect the grant operation or other listeners</item>
/// </list>
/// <para>
/// <b>Why DI instead of events?</b> Both Collection and Seed are L2 (always co-located,
/// always available). In-process DI calls provide guaranteed delivery without event bus
/// reliability concerns. The <c>collection.entry-unlocked</c> event is still published
/// for external/distributed consumers (analytics, achievements, etc.).
/// </para>
/// <para>
/// <b>DISTRIBUTED SAFETY — LOCAL-ONLY FAN-OUT:</b> This is a push-based Listener pattern.
/// In multi-node deployments, only listeners on the node that processed the API request
/// are called. Other nodes are NOT notified via this interface. This is safe because
/// listener reactions write to distributed state (Redis/MySQL), so all nodes see the
/// updated state on their next read. If per-node awareness is required (e.g., invalidating
/// a local cache on every node), the consumer MUST subscribe to the broadcast event via
/// <c>IEventConsumer</c> instead. See SERVICE-HIERARCHY.md §"DI Provider vs Listener".
/// </para>
/// <para>
/// <b>Example Implementation</b>:
/// </para>
/// <code>
/// public class SeedCollectionUnlockListener : ICollectionUnlockListener
/// {
///     public Task OnEntryUnlockedAsync(CollectionUnlockNotification notification, CancellationToken ct)
///     {
///         // Match entry tags against seed type growth mappings, record growth
///     }
/// }
/// // DI registration: services.AddSingleton&lt;ICollectionUnlockListener, SeedCollectionUnlockListener&gt;();
/// </code>
/// </remarks>
public interface ICollectionUnlockListener
{
    /// <summary>
    /// Called when a collection entry is successfully unlocked.
    /// </summary>
    /// <param name="notification">Details of the unlocked entry including tags for domain mapping.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <remarks>
    /// Implementations should handle their own errors internally. Exceptions thrown
    /// from this method are caught by Collection and logged as warnings -- they never
    /// affect the grant operation, other listeners, or the caller.
    /// </remarks>
    Task OnEntryUnlockedAsync(CollectionUnlockNotification notification, CancellationToken ct);
}

/// <summary>
/// Notification data for a collection entry unlock event, delivered via DI.
/// </summary>
/// <param name="CollectionId">The collection instance the entry was unlocked in.</param>
/// <param name="OwnerId">Entity that owns the collection.</param>
/// <param name="OwnerType">Entity type discriminator (e.g., "character", "account").</param>
/// <param name="GameServiceId">Game service scope.</param>
/// <param name="CollectionType">Type of collection (opaque string code).</param>
/// <param name="EntryCode">Entry template code that was unlocked.</param>
/// <param name="DisplayName">Display name of the unlocked entry.</param>
/// <param name="Category">Category within the collection type (e.g., "boss", "ambient").</param>
/// <param name="Tags">Tags from the entry template for downstream consumer matching.</param>
/// <param name="DiscoveryLevel">Current discovery level of the entry (0 for newly unlocked).</param>
public record CollectionUnlockNotification(
    Guid CollectionId,
    Guid OwnerId,
    string OwnerType,
    Guid GameServiceId,
    string CollectionType,
    string EntryCode,
    string DisplayName,
    string? Category,
    IReadOnlyList<string>? Tags,
    int DiscoveryLevel);
