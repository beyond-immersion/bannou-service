// =============================================================================
// Seed Evolution Listener Interface
// Enables in-process notification when seeds grow, change phase, or gain capabilities.
// Seed (L2) discovers listeners via DI; L4 services implement them for targeted dispatch.
// =============================================================================

namespace BeyondImmersion.BannouService.Providers;

/// <summary>
/// Listener interface for receiving seed evolution notifications via DI.
/// </summary>
/// <remarks>
/// <para>
/// This interface enables targeted in-process delivery of seed evolution events
/// to co-located consumers without requiring them to subscribe to the full broadcast
/// event firehose (<c>seed.growth.updated</c>, <c>seed.phase.changed</c>,
/// <c>seed.capability.updated</c>):
/// </para>
/// <list type="bullet">
///   <item>Seed (L2) discovers this interface via <c>IEnumerable&lt;ISeedEvolutionListener&gt;</c></item>
///   <item>L4 services implement the listener and register as Singleton</item>
///   <item>Seed calls listeners AFTER state is saved and broadcast events are published</item>
///   <item>Listeners filter by <see cref="InterestedSeedTypes"/> to avoid processing irrelevant notifications</item>
///   <item>Listener failures are logged as warnings and never affect core seed logic or other listeners</item>
/// </list>
/// <para>
/// <strong>DISTRIBUTED DEPLOYMENT NOTE</strong>: Listeners are LOCAL-ONLY fan-out.
/// Only listeners co-located on the same node where the seed mutation occurs are called.
/// In a multi-node deployment, nodes that do not process the seed API request will NOT
/// have their listeners invoked.
/// </para>
/// <para>
/// This is safe when the listener's reaction writes to distributed state (Redis, MySQL)
/// because all nodes read from the same distributed store. It is NOT safe if the listener
/// maintains local in-memory state that must be consistent across nodes.
/// </para>
/// <para>
/// If your consumer requires per-node awareness (e.g., local cache invalidation on
/// every node), you MUST ALSO subscribe to the corresponding seed broadcast events
/// (<c>seed.growth.updated</c>, <c>seed.phase.changed</c>, <c>seed.capability.updated</c>)
/// via IEventConsumer. Listeners are an in-process optimization for co-located services,
/// not a replacement for distributed event subscriptions.
/// </para>
/// <para>
/// <b>Example Implementation</b>:
/// </para>
/// <code>
/// public class StatusSeedEvolutionListener : ISeedEvolutionListener
/// {
///     public IReadOnlySet&lt;string&gt; InterestedSeedTypes { get; } = new HashSet&lt;string&gt; { "guardian" };
///
///     public async Task OnGrowthRecordedAsync(SeedGrowthNotification notification, CancellationToken ct)
///     {
///         // Update status display based on growth domains
///         await Task.CompletedTask;
///     }
///
///     public async Task OnPhaseChangedAsync(SeedPhaseNotification notification, CancellationToken ct)
///     {
///         // Trigger phase-specific visual effects
///         await Task.CompletedTask;
///     }
///
///     public async Task OnCapabilitiesChangedAsync(SeedCapabilityNotification notification, CancellationToken ct)
///     {
///         // Refresh capability manifest in status display
///         await Task.CompletedTask;
///     }
/// }
/// // DI registration: services.AddSingleton&lt;ISeedEvolutionListener, StatusSeedEvolutionListener&gt;();
/// </code>
/// </remarks>
public interface ISeedEvolutionListener
{
    /// <summary>
    /// Seed type codes this listener cares about. Empty set means all types (wildcard).
    /// </summary>
    /// <remarks>
    /// Seed checks this set before invoking callbacks. When non-empty, only notifications
    /// for matching seed type codes are dispatched to this listener.
    /// </remarks>
    IReadOnlySet<string> InterestedSeedTypes { get; }

    /// <summary>
    /// Called after growth is recorded for a seed, aggregated across all domains in a single recording operation.
    /// </summary>
    /// <param name="notification">Details of the growth recording including per-domain changes.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <remarks>
    /// Implementations should handle their own errors internally. Exceptions thrown
    /// from this method are caught by Seed and logged as warnings -- they never
    /// affect the growth operation, other listeners, or the caller.
    /// </remarks>
    Task OnGrowthRecordedAsync(SeedGrowthNotification notification, CancellationToken ct);

    /// <summary>
    /// Called after a phase transition (progression or regression) for a seed.
    /// </summary>
    /// <param name="notification">Details of the phase change including previous and new phase.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <remarks>
    /// Implementations should handle their own errors internally. Exceptions thrown
    /// from this method are caught by Seed and logged as warnings -- they never
    /// affect the phase change operation, other listeners, or the caller.
    /// </remarks>
    Task OnPhaseChangedAsync(SeedPhaseNotification notification, CancellationToken ct);

    /// <summary>
    /// Called after the capability manifest is recomputed for a seed.
    /// </summary>
    /// <param name="notification">Details of the capability update including full capability snapshot.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <remarks>
    /// Implementations should handle their own errors internally. Exceptions thrown
    /// from this method are caught by Seed and logged as warnings -- they never
    /// affect the capability computation, other listeners, or the caller.
    /// </remarks>
    Task OnCapabilitiesChangedAsync(SeedCapabilityNotification notification, CancellationToken ct);
}

/// <summary>
/// Notification data for a seed growth recording, delivered via DI to evolution listeners.
/// Aggregates all domain changes from a single recording operation.
/// </summary>
/// <param name="SeedId">The seed that grew.</param>
/// <param name="SeedTypeCode">Seed type code for consumer filtering.</param>
/// <param name="OwnerId">Entity that owns the seed.</param>
/// <param name="OwnerType">Owner entity type discriminator.</param>
/// <param name="DomainChanges">Per-domain changes from this recording.</param>
/// <param name="TotalGrowth">Aggregate growth across all domains after recording.</param>
/// <param name="CrossPollinated">Whether this growth came from cross-pollination.</param>
/// <param name="Source">Growth source identifier (e.g., "collection", "api").</param>
public record SeedGrowthNotification(
    Guid SeedId,
    string SeedTypeCode,
    Guid OwnerId,
    EntityType OwnerType,
    IReadOnlyList<DomainChange> DomainChanges,
    float TotalGrowth,
    bool CrossPollinated,
    string Source);

/// <summary>
/// Represents a single domain's depth change during a growth recording.
/// </summary>
/// <param name="Domain">Domain path (e.g., "combat.melee").</param>
/// <param name="PreviousDepth">Depth before this recording.</param>
/// <param name="NewDepth">Depth after this recording.</param>
public record DomainChange(
    string Domain,
    float PreviousDepth,
    float NewDepth);

/// <summary>
/// Notification data for a seed phase transition, delivered via DI to evolution listeners.
/// </summary>
/// <param name="SeedId">The seed that changed phase.</param>
/// <param name="SeedTypeCode">Seed type code for consumer filtering.</param>
/// <param name="OwnerId">Entity that owns the seed.</param>
/// <param name="OwnerType">Owner entity type discriminator.</param>
/// <param name="PreviousPhase">Phase code before transition.</param>
/// <param name="NewPhase">Phase code after transition.</param>
/// <param name="TotalGrowth">Current total growth at time of transition.</param>
/// <param name="Progressed">True if moving to a higher phase, false if regressing (decay).</param>
public record SeedPhaseNotification(
    Guid SeedId,
    string SeedTypeCode,
    Guid OwnerId,
    EntityType OwnerType,
    string PreviousPhase,
    string NewPhase,
    float TotalGrowth,
    bool Progressed);

/// <summary>
/// Notification data for a seed capability manifest recomputation, delivered via DI to evolution listeners.
/// </summary>
/// <param name="SeedId">The seed whose capabilities changed.</param>
/// <param name="SeedTypeCode">Seed type code for consumer filtering.</param>
/// <param name="OwnerId">Entity that owns the seed.</param>
/// <param name="OwnerType">Owner entity type discriminator.</param>
/// <param name="Version">Monotonically increasing manifest version.</param>
/// <param name="UnlockedCount">Number of currently unlocked capabilities.</param>
/// <param name="Capabilities">Full capability state snapshot.</param>
public record SeedCapabilityNotification(
    Guid SeedId,
    string SeedTypeCode,
    Guid OwnerId,
    EntityType OwnerType,
    int Version,
    int UnlockedCount,
    IReadOnlyList<CapabilitySnapshot> Capabilities);

/// <summary>
/// Snapshot of a single capability's state at the time of notification.
/// </summary>
/// <param name="CapabilityCode">Capability identifier code.</param>
/// <param name="Domain">Growth domain governing this capability.</param>
/// <param name="Fidelity">Current fidelity value (0.0-1.0).</param>
/// <param name="Unlocked">Whether the capability is currently unlocked.</param>
public record CapabilitySnapshot(
    string CapabilityCode,
    string Domain,
    float Fidelity,
    bool Unlocked);
