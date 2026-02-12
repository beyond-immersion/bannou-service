// =============================================================================
// Seed Variable Provider
// Provides seed data for ABML expressions via ${seed.*} paths.
// Owned by lib-seed per service hierarchy (L2).
// =============================================================================

using BeyondImmersion.Bannou.BehaviorExpressions.Expressions;
using BeyondImmersion.BannouService.Seed.Caching;

namespace BeyondImmersion.BannouService.Seed.Providers;

/// <summary>
/// Provides seed data for ABML expressions.
/// </summary>
/// <remarks>
/// <para>Variables available (type-scoped by seed type code):</para>
/// <list type="bullet">
///   <item><description><c>${seed.active_count}</c> - int: Number of active seeds</description></item>
///   <item><description><c>${seed.types}</c> - List: Active seed type codes</description></item>
///   <item><description><c>${seed.TYPE.phase}</c> - string: Current growth phase</description></item>
///   <item><description><c>${seed.TYPE.total_growth}</c> - float: Aggregate growth</description></item>
///   <item><description><c>${seed.TYPE.status}</c> - string: Lifecycle status</description></item>
///   <item><description><c>${seed.TYPE.display_name}</c> - string: Human-readable name</description></item>
///   <item><description><c>${seed.TYPE.has_bond}</c> - bool: Whether seed is bonded</description></item>
///   <item><description><c>${seed.TYPE.growth.DOMAIN}</c> - float: Domain depth</description></item>
///   <item><description><c>${seed.TYPE.capabilities.CAP.fidelity}</c> - float: 0-1 fidelity</description></item>
///   <item><description><c>${seed.TYPE.capabilities.CAP.unlocked}</c> - bool: Available?</description></item>
/// </list>
/// </remarks>
public sealed class SeedProvider : IVariableProvider
{
    /// <summary>
    /// Empty provider for non-character actors.
    /// </summary>
    public static SeedProvider Empty { get; } = new(CachedSeedData.Empty);

    private readonly Dictionary<string, SeedSnapshot> _seedsByType;

    /// <inheritdoc/>
    public string Name => "seed";

    /// <summary>
    /// Creates a new SeedProvider with the given seed data.
    /// </summary>
    /// <param name="data">Cached seed data. Use <see cref="Empty"/> for non-character actors.</param>
    public SeedProvider(CachedSeedData data)
    {
        _seedsByType = new Dictionary<string, SeedSnapshot>(StringComparer.OrdinalIgnoreCase);

        foreach (var seed in data.Seeds)
        {
            // Build growth domain lookup
            var growthDomains = data.Growth.TryGetValue(seed.SeedId, out var growth)
                ? growth.Domains.ToDictionary(
                    kvp => kvp.Key,
                    kvp => kvp.Value,
                    StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);

            // Build capability lookup
            var capabilities = new Dictionary<string, CapabilitySnapshot>(StringComparer.OrdinalIgnoreCase);
            if (data.Capabilities.TryGetValue(seed.SeedId, out var manifest))
            {
                foreach (var cap in manifest.Capabilities)
                {
                    capabilities[cap.CapabilityCode] = new CapabilitySnapshot(
                        cap.Fidelity, cap.Unlocked, cap.Domain);
                }
            }

            _seedsByType[seed.SeedTypeCode] = new SeedSnapshot(seed, growthDomains, capabilities);
        }
    }

    /// <inheritdoc/>
    public object? GetValue(ReadOnlySpan<string> path)
    {
        if (path.Length == 0) return GetRootValue();

        var firstSegment = path[0];

        // Handle top-level convenience variables
        if (firstSegment.Equals("active_count", StringComparison.OrdinalIgnoreCase))
        {
            return _seedsByType.Count;
        }

        if (firstSegment.Equals("types", StringComparison.OrdinalIgnoreCase))
        {
            return _seedsByType.Keys.ToList();
        }

        // Handle type-scoped access: ${seed.<typeCode>.*}
        if (!_seedsByType.TryGetValue(firstSegment, out var snapshot))
        {
            return null;
        }

        if (path.Length == 1) return SeedToDict(snapshot);

        return ResolveSeedPath(snapshot, path.Slice(1));
    }

    /// <inheritdoc/>
    public object? GetRootValue()
    {
        return new Dictionary<string, object?>
        {
            ["active_count"] = _seedsByType.Count,
            ["types"] = _seedsByType.Keys.ToList()
        };
    }

    /// <inheritdoc/>
    public bool CanResolve(ReadOnlySpan<string> path)
    {
        if (path.Length == 0) return true;

        var firstSegment = path[0];

        if (firstSegment.Equals("active_count", StringComparison.OrdinalIgnoreCase) ||
            firstSegment.Equals("types", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Type-scoped paths require the type to exist
        return _seedsByType.ContainsKey(firstSegment);
    }

    private static object? ResolveSeedPath(SeedSnapshot snapshot, ReadOnlySpan<string> path)
    {
        if (path.Length == 0) return SeedToDict(snapshot);

        return path[0].ToLowerInvariant() switch
        {
            "phase" => snapshot.Seed.GrowthPhase,
            "total_growth" => snapshot.Seed.TotalGrowth,
            "status" => snapshot.Seed.Status.ToString(),
            "display_name" => snapshot.Seed.DisplayName,
            "has_bond" => snapshot.Seed.BondId.HasValue,
            "growth" => ResolveGrowthPath(snapshot.Growth, path.Slice(1)),
            "capabilities" => ResolveCapabilitiesPath(snapshot.Capabilities, path.Slice(1)),
            _ => null
        };
    }

    private static object? ResolveGrowthPath(
        IReadOnlyDictionary<string, float> domains,
        ReadOnlySpan<string> path)
    {
        // ${seed.TYPE.growth} → all domains as dict
        if (path.Length == 0) return domains;

        // ${seed.TYPE.growth.DOMAIN} → single domain depth
        var domainKey = path[0];
        return domains.TryGetValue(domainKey, out var depth) ? depth : null;
    }

    private static object? ResolveCapabilitiesPath(
        IReadOnlyDictionary<string, CapabilitySnapshot> capabilities,
        ReadOnlySpan<string> path)
    {
        // ${seed.TYPE.capabilities} → all capabilities as dict
        if (path.Length == 0)
        {
            return capabilities.ToDictionary(
                kvp => kvp.Key,
                kvp => (object?)CapabilityToDict(kvp.Value));
        }

        // ${seed.TYPE.capabilities.CAP} → single capability
        var capCode = path[0];
        if (!capabilities.TryGetValue(capCode, out var cap)) return null;

        if (path.Length == 1) return CapabilityToDict(cap);

        // ${seed.TYPE.capabilities.CAP.fidelity} or ${seed.TYPE.capabilities.CAP.unlocked}
        return path[1].ToLowerInvariant() switch
        {
            "fidelity" => cap.Fidelity,
            "unlocked" => cap.Unlocked,
            "domain" => cap.Domain,
            _ => null
        };
    }

    private static Dictionary<string, object?> SeedToDict(SeedSnapshot snapshot)
    {
        return new Dictionary<string, object?>
        {
            ["phase"] = snapshot.Seed.GrowthPhase,
            ["total_growth"] = snapshot.Seed.TotalGrowth,
            ["status"] = snapshot.Seed.Status.ToString(),
            ["display_name"] = snapshot.Seed.DisplayName,
            ["has_bond"] = snapshot.Seed.BondId.HasValue,
            ["growth"] = snapshot.Growth,
            ["capabilities"] = snapshot.Capabilities.ToDictionary(
                kvp => kvp.Key,
                kvp => (object?)CapabilityToDict(kvp.Value))
        };
    }

    private static Dictionary<string, object?> CapabilityToDict(CapabilitySnapshot cap)
    {
        return new Dictionary<string, object?>
        {
            ["fidelity"] = cap.Fidelity,
            ["unlocked"] = cap.Unlocked,
            ["domain"] = cap.Domain
        };
    }

    /// <summary>
    /// Snapshot of a single seed's data for variable resolution.
    /// </summary>
    private sealed record SeedSnapshot(
        SeedResponse Seed,
        IReadOnlyDictionary<string, float> Growth,
        IReadOnlyDictionary<string, CapabilitySnapshot> Capabilities);

    /// <summary>
    /// Snapshot of a single capability entry.
    /// </summary>
    private sealed record CapabilitySnapshot(float Fidelity, bool Unlocked, string Domain);
}
