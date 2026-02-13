using BeyondImmersion.Bannou.BehaviorExpressions.Expressions;

namespace BeyondImmersion.BannouService.Obligation.Providers;

/// <summary>
/// Provides obligation data for ABML expressions via ${obligations.*} paths.
/// Created per-character by <see cref="ObligationProviderFactory"/>.
/// </summary>
/// <remarks>
/// Supported variable paths:
/// <list type="bullet">
///   <item><c>${obligations.active_count}</c> - Number of active obligations</item>
///   <item><c>${obligations.has_obligations}</c> - Whether any obligations exist (bool)</item>
///   <item><c>${obligations.contract_count}</c> - Number of active contracts with behavioral clauses</item>
///   <item><c>${obligations.violation_cost.&lt;type&gt;}</c> - Aggregated cost for a specific violation type</item>
///   <item><c>${obligations.highest_penalty_type}</c> - Violation type with the highest aggregated penalty</item>
///   <item><c>${obligations.total_obligation_cost}</c> - Sum of all violation costs</item>
/// </list>
/// </remarks>
public sealed class ObligationProvider : IVariableProvider
{
    /// <summary>
    /// Empty provider for actors without obligation data.
    /// </summary>
    public static ObligationProvider Empty { get; } = new(null);

    private readonly ObligationManifestModel? _manifest;

    /// <inheritdoc/>
    public string Name => "obligations";

    /// <summary>
    /// Creates a new obligation provider with the given manifest data.
    /// </summary>
    /// <param name="manifest">The obligation manifest, or null for empty provider.</param>
    internal ObligationProvider(ObligationManifestModel? manifest)
    {
        _manifest = manifest;
    }

    /// <inheritdoc/>
    public object? GetValue(ReadOnlySpan<string> path)
    {
        if (_manifest == null) return null;
        if (path.Length == 0) return GetRootValue();

        var firstSegment = path[0];

        // ${obligations.active_count}
        if (firstSegment.Equals("active_count", StringComparison.OrdinalIgnoreCase))
        {
            return _manifest.Obligations.Count;
        }

        // ${obligations.has_obligations}
        if (firstSegment.Equals("has_obligations", StringComparison.OrdinalIgnoreCase))
        {
            return _manifest.Obligations.Count > 0;
        }

        // ${obligations.contract_count}
        if (firstSegment.Equals("contract_count", StringComparison.OrdinalIgnoreCase))
        {
            return _manifest.TotalActiveContracts;
        }

        // ${obligations.total_obligation_cost}
        if (firstSegment.Equals("total_obligation_cost", StringComparison.OrdinalIgnoreCase))
        {
            return _manifest.ViolationCostMap.Values.Sum();
        }

        // ${obligations.highest_penalty_type}
        if (firstSegment.Equals("highest_penalty_type", StringComparison.OrdinalIgnoreCase))
        {
            if (_manifest.ViolationCostMap.Count == 0) return null;
            return _manifest.ViolationCostMap.MaxBy(kvp => kvp.Value).Key;
        }

        // ${obligations.violation_cost} or ${obligations.violation_cost.<type>}
        if (firstSegment.Equals("violation_cost", StringComparison.OrdinalIgnoreCase))
        {
            if (path.Length < 2) return _manifest.ViolationCostMap;
            var violationType = path[1];
            return _manifest.ViolationCostMap.TryGetValue(violationType, out var cost) ? cost : 0f;
        }

        return null;
    }

    /// <inheritdoc/>
    public object? GetRootValue()
    {
        if (_manifest == null) return null;
        return new Dictionary<string, object?>
        {
            ["active_count"] = _manifest.Obligations.Count,
            ["has_obligations"] = _manifest.Obligations.Count > 0,
            ["contract_count"] = _manifest.TotalActiveContracts,
            ["total_obligation_cost"] = _manifest.ViolationCostMap.Values.Sum(),
            ["highest_penalty_type"] = _manifest.ViolationCostMap.Count > 0
                ? _manifest.ViolationCostMap.MaxBy(kvp => kvp.Value).Key
                : null,
            ["violation_cost"] = _manifest.ViolationCostMap
        };
    }

    /// <inheritdoc/>
    public bool CanResolve(ReadOnlySpan<string> path)
    {
        if (path.Length == 0) return true;
        var firstSegment = path[0];
        return firstSegment.Equals("active_count", StringComparison.OrdinalIgnoreCase) ||
                firstSegment.Equals("has_obligations", StringComparison.OrdinalIgnoreCase) ||
                firstSegment.Equals("contract_count", StringComparison.OrdinalIgnoreCase) ||
                firstSegment.Equals("total_obligation_cost", StringComparison.OrdinalIgnoreCase) ||
                firstSegment.Equals("highest_penalty_type", StringComparison.OrdinalIgnoreCase) ||
                firstSegment.Equals("violation_cost", StringComparison.OrdinalIgnoreCase);
    }
}
