// =============================================================================
// Seed Data Cache Interface
// Caches character seed data for actor behavior execution.
// Owned by lib-seed per service hierarchy (L2).
// =============================================================================

namespace BeyondImmersion.BannouService.Seed.Caching;

/// <summary>
/// Caches character seed data for actor behavior execution.
/// Supports TTL-based expiration and manual invalidation.
/// </summary>
public interface ISeedDataCache
{
    /// <summary>
    /// Gets seed data for a character, loading from service if not cached or expired.
    /// </summary>
    /// <param name="characterId">The character ID (owner entity ID).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Cached seed data (never null, may contain empty collections).</returns>
    Task<CachedSeedData> GetSeedDataOrLoadAsync(Guid characterId, CancellationToken ct = default);

    /// <summary>
    /// Invalidates cached seed data for a character.
    /// </summary>
    /// <param name="characterId">The character ID to invalidate.</param>
    void Invalidate(Guid characterId);

    /// <summary>
    /// Invalidates all cached seed data.
    /// </summary>
    void InvalidateAll();
}

/// <summary>
/// Bundled seed data for a single character, combining seeds, growth, and capabilities.
/// </summary>
/// <param name="Seeds">Active seeds for the character.</param>
/// <param name="Growth">Growth data keyed by seed ID.</param>
/// <param name="Capabilities">Capability manifests keyed by seed ID.</param>
public sealed record CachedSeedData(
    IReadOnlyList<SeedResponse> Seeds,
    IReadOnlyDictionary<Guid, GrowthResponse> Growth,
    IReadOnlyDictionary<Guid, CapabilityManifestResponse> Capabilities)
{
    /// <summary>
    /// Empty seed data for characters with no active seeds.
    /// </summary>
    public static CachedSeedData Empty { get; } = new(
        Array.Empty<SeedResponse>(),
        new Dictionary<Guid, GrowthResponse>(),
        new Dictionary<Guid, CapabilityManifestResponse>());
}
