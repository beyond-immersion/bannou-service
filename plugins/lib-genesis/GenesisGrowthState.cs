using System.Collections.Concurrent;

namespace BeyondImmersion.BannouService.Genesis;

/// <summary>
/// Shared in-memory state for the Genesis runtime pipeline.
/// </summary>
/// <remarks>
/// <para>
/// This singleton is the shared substrate between four co-operating components:
/// </para>
/// <list type="bullet">
///   <item><see cref="GenesisCurrencyTransactionListener"/> — writes to <see cref="WalletMap"/> and the growth accumulator.</item>
///   <item><see cref="Services.GenesisGrowthFlushWorkerService"/> — drains the accumulator, applies template
///     mappings, and writes batched growth to Seed.</item>
///   <item><see cref="GenesisSeedEvolutionListener"/> — reads the wallet map during phase transition handling
///     (indirectly through the entity store lookup).</item>
///   <item><see cref="GenesisService"/> event handlers — maintain the wallet map across nodes via self-subscription
///     to <c>genesis.entity.created</c> and <c>genesis.entity.deleted</c>.</item>
/// </list>
/// <para>
/// <b>Why a shared singleton?</b> The currency transaction listener (fired from Currency) and the flush worker
/// (BackgroundService) are both Singletons. The wallet map must be populated once and visible to both, plus
/// the event handlers that maintain coherence via broadcast events. Using a shared state object avoids
/// captive-dependency problems where scoped services inject into singletons.
/// </para>
/// <para>
/// <b>Distributed safety:</b> The wallet map is per-node. Each node is populated from MySQL at startup via
/// <see cref="GenesisServicePlugin"/> and kept in sync across nodes via self-subscription to the broadcast
/// <c>genesis.entity.created</c> / <c>genesis.entity.deleted</c> events. The growth accumulator is per-node
/// (local-only by design) — it is drained and written through to distributed state (Seed's MySQL store) by
/// the flush worker on the same node.
/// </para>
/// </remarks>
public class GenesisGrowthState
{
    /// <summary>
    /// Maps <c>walletId → GenesisWalletMapping</c> for microsecond-fast filtering of currency mutations
    /// by <see cref="GenesisCurrencyTransactionListener"/>. Non-genesis wallets miss the lookup and return
    /// immediately without network I/O.
    /// </summary>
    public ConcurrentDictionary<Guid, GenesisWalletMapping> WalletMap { get; } = new();

    /// <summary>
    /// Pre-resolved actor template IDs for phase transitions, keyed by
    /// <c>"{templateCode}:{phaseName}"</c>. Populated during template registration and plugin startup;
    /// read by <see cref="GenesisSeedEvolutionListener"/> at the EventBrain transition point.
    /// </summary>
    public ConcurrentDictionary<string, Guid> ActorTemplateMap { get; } = new();

    /// <summary>
    /// Buffered growth credits pending flush, keyed by entity ID. Replaced atomically on drain via
    /// <see cref="Interlocked.Exchange{T}(ref T, T)"/> — the flush worker grabs the current accumulator
    /// and installs a fresh one in a single operation, so listeners that fire mid-flush write to the
    /// new accumulator without blocking.
    /// </summary>
    private ConcurrentDictionary<Guid, ConcurrentBag<GrowthBufferEntry>> _accumulator = new();

    /// <summary>
    /// Buffers a single growth contribution against an entity. Safe to call from any thread.
    /// </summary>
    /// <param name="entityId">Entity whose seed should grow from this credit.</param>
    /// <param name="entry">The buffered growth entry (wallet code, amount, direction).</param>
    public void BufferGrowth(Guid entityId, GrowthBufferEntry entry)
    {
        var bag = _accumulator.GetOrAdd(entityId, static _ => new ConcurrentBag<GrowthBufferEntry>());
        bag.Add(entry);
    }

    /// <summary>
    /// Atomically drains all buffered growth and returns it grouped by entity.
    /// A fresh empty accumulator is installed atomically so concurrent listeners continue writing
    /// without losing entries or blocking.
    /// </summary>
    /// <returns>A dictionary of <c>entityId → entries</c>. Empty when the accumulator was empty.</returns>
    public Dictionary<Guid, List<GrowthBufferEntry>> DrainAccumulator()
    {
        var drained = Interlocked.Exchange(
            ref _accumulator,
            new ConcurrentDictionary<Guid, ConcurrentBag<GrowthBufferEntry>>());

        var result = new Dictionary<Guid, List<GrowthBufferEntry>>(drained.Count);
        foreach (var (entityId, bag) in drained)
        {
            var entries = bag.ToList();
            if (entries.Count > 0)
                result[entityId] = entries;
        }
        return result;
    }
}

/// <summary>
/// Immutable wallet→entity binding cached in the Genesis wallet map.
/// </summary>
/// <param name="EntityId">Genesis entity that owns this wallet.</param>
/// <param name="TemplateCode">Template the entity was created from (used to look up growth mappings).</param>
/// <param name="WalletCode">Logical wallet code from the template (e.g., "mana", "experience").</param>
/// <param name="GrowthMappings">
/// Snapshot of the template's growth mappings at creation time. Captured per-entity (not per-template)
/// because template updates do not retroactively affect existing entities.
/// </param>
public record GenesisWalletMapping(
    Guid EntityId,
    string TemplateCode,
    string WalletCode,
    IReadOnlyList<GenesisGrowthMapping> GrowthMappings);

/// <summary>
/// A single buffered growth contribution awaiting flush.
/// </summary>
/// <param name="WalletCode">Template wallet code the currency came from.</param>
/// <param name="Amount">Absolute magnitude of the currency mutation (always positive).</param>
/// <param name="Direction">
/// Direction of the mutation — <see cref="GrowthDirection.Credit"/> for credits and autogain,
/// <see cref="GrowthDirection.Debit"/> for debits. The flush worker applies template growth mappings
/// filtered by direction: a mapping with <see cref="GrowthDirection.Both"/> accepts either, while a
/// mapping with <see cref="GrowthDirection.Credit"/> only accepts credits.
/// </param>
public record GrowthBufferEntry(
    string WalletCode,
    double Amount,
    GrowthDirection Direction);
