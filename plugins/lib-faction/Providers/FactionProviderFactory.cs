// =============================================================================
// Faction Variable Provider Factory
// Creates FactionProvider instances for ABML expression evaluation.
// Registered with DI for Actor to consume via IEnumerable<IVariableProviderFactory>.
// =============================================================================

using BeyondImmersion.Bannou.BehaviorExpressions.Expressions;
using BeyondImmersion.BannouService.Providers;
using BeyondImmersion.BannouService.Services;

namespace BeyondImmersion.BannouService.Faction.Providers;

/// <summary>
/// Factory for creating FactionProvider instances.
/// Registered with DI as IVariableProviderFactory for Actor to discover.
/// </summary>
/// <remarks>
/// <para>
/// Provides <c>${faction.*}</c> namespace variables to ABML behavior expressions.
/// Actor (L2) discovers this factory via <c>IEnumerable&lt;IVariableProviderFactory&gt;</c>.
/// </para>
/// </remarks>
public sealed class FactionProviderFactory : IVariableProviderFactory
{
    private readonly IStateStoreFactory _stateStoreFactory;

    /// <summary>
    /// Creates a new faction provider factory.
    /// </summary>
    /// <param name="stateStoreFactory">State store factory for faction/membership data access.</param>
    public FactionProviderFactory(IStateStoreFactory stateStoreFactory)
    {
        _stateStoreFactory = stateStoreFactory;
    }

    /// <inheritdoc/>
    public string ProviderName => VariableProviderDefinitions.Faction;

    /// <inheritdoc/>
    public async Task<IVariableProvider> CreateAsync(Guid? characterId, Guid realmId, Guid? locationId, CancellationToken ct)
    {
        if (!characterId.HasValue)
        {
            return FactionProvider.Empty;
        }

        // Load the character's faction memberships
        var memberListStore = _stateStoreFactory.GetStore<MembershipListModel>(
            StateStoreDefinitions.FactionMembership);
        var membershipList = await memberListStore.GetAsync(
            $"mem:char:{characterId.Value}", ct);

        if (membershipList == null || membershipList.Memberships.Count == 0)
        {
            return FactionProvider.Empty;
        }

        // Load faction details and norms for each membership
        var factionStore = _stateStoreFactory.GetStore<FactionModel>(
            StateStoreDefinitions.Faction);
        var normListStore = _stateStoreFactory.GetStore<NormListModel>(
            StateStoreDefinitions.FactionNorm);
        var normStore = _stateStoreFactory.GetStore<NormDefinitionModel>(
            StateStoreDefinitions.FactionNorm);
        var factions = new List<FactionProvider.FactionSnapshot>();
        var mergedNorms = new Dictionary<string, FactionProvider.NormSnapshot>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var membership in membershipList.Memberships)
        {
            var faction = await factionStore.GetAsync($"fac:{membership.FactionId}", ct);
            if (faction == null) continue;

            // Filter to realm-relevant factions — skip factions in other realms
            if (faction.RealmId != realmId) continue;

            factions.Add(new FactionProvider.FactionSnapshot(
                faction.FactionId,
                faction.Name,
                faction.Code,
                faction.Status,
                faction.CurrentPhase,
                faction.IsRealmBaseline,
                faction.MemberCount,
                membership.Role));

            // Load norms for this faction (membership-scoped norm resolution)
            var normList = await normListStore.GetAsync($"nrm:fac:{membership.FactionId}", ct);
            if (normList == null) continue;

            foreach (var normId in normList.NormIds)
            {
                var norm = await normStore.GetAsync($"nrm:{normId}", ct);
                if (norm == null) continue;

                // Keep highest penalty per violation type across all membership factions
                if (!mergedNorms.TryGetValue(norm.ViolationType, out var existing) ||
                    norm.BasePenalty > existing.BasePenalty)
                {
                    mergedNorms[norm.ViolationType] = new FactionProvider.NormSnapshot(
                        norm.ViolationType,
                        norm.BasePenalty,
                        norm.Severity,
                        faction.Name);
                }
            }
        }

        return new FactionProvider(factions, mergedNorms);
    }
}

/// <summary>
/// Provides faction data for ABML expressions via <c>${faction.*}</c> paths.
/// </summary>
/// <remarks>
/// <para>Variables available:</para>
/// <list type="bullet">
///   <item><description><c>${faction.count}</c> - int: Number of factions the character belongs to</description></item>
///   <item><description><c>${faction.names}</c> - List: Names of all factions</description></item>
///   <item><description><c>${faction.codes}</c> - List: Codes of all factions</description></item>
///   <item><description><c>${faction.primary_faction}</c> - string: Code of the highest-role faction</description></item>
///   <item><description><c>${faction.has_norm.TYPE}</c> - bool: Whether any membership faction defines a norm for this violation type</description></item>
///   <item><description><c>${faction.norm_penalty.TYPE}</c> - float: Highest penalty across membership factions for this violation type</description></item>
///   <item><description><c>${faction.norm_count}</c> - int: Total unique violation types with norms across all membership factions</description></item>
///   <item><description><c>${faction.CODE.name}</c> - string: Faction display name</description></item>
///   <item><description><c>${faction.CODE.status}</c> - string: Faction status</description></item>
///   <item><description><c>${faction.CODE.phase}</c> - string: Current seed growth phase</description></item>
///   <item><description><c>${faction.CODE.is_realm_baseline}</c> - bool: Whether realm baseline</description></item>
///   <item><description><c>${faction.CODE.member_count}</c> - int: Total member count</description></item>
///   <item><description><c>${faction.CODE.role}</c> - string: Character's role in this faction</description></item>
/// </list>
/// </remarks>
public sealed class FactionProvider : IVariableProvider
{
    /// <summary>
    /// Empty provider for non-character actors or characters with no factions.
    /// </summary>
    public static FactionProvider Empty { get; } = new(
        new List<FactionSnapshot>(),
        new Dictionary<string, NormSnapshot>(StringComparer.OrdinalIgnoreCase));

    private readonly Dictionary<string, FactionSnapshot> _factionsByCode;
    private readonly List<FactionSnapshot> _factions;
    private readonly Dictionary<string, NormSnapshot> _mergedNorms;

    /// <inheritdoc/>
    public string Name => VariableProviderDefinitions.Faction;

    /// <summary>
    /// Creates a new FactionProvider with the given faction and norm data.
    /// </summary>
    /// <param name="factions">Faction snapshots for this character.</param>
    /// <param name="mergedNorms">Merged norms across all membership factions (highest penalty per violation type).</param>
    internal FactionProvider(
        IReadOnlyList<FactionSnapshot> factions,
        Dictionary<string, NormSnapshot> mergedNorms)
    {
        _factions = factions.ToList();
        _mergedNorms = mergedNorms;
        _factionsByCode = new Dictionary<string, FactionSnapshot>(StringComparer.OrdinalIgnoreCase);
        foreach (var faction in factions)
        {
            _factionsByCode[faction.Code] = faction;
        }
    }

    /// <inheritdoc/>
    public object? GetValue(ReadOnlySpan<string> path)
    {
        if (path.Length == 0) return GetRootValue();

        var firstSegment = path[0];

        if (firstSegment.Equals("count", StringComparison.OrdinalIgnoreCase))
        {
            return _factions.Count;
        }

        if (firstSegment.Equals("names", StringComparison.OrdinalIgnoreCase))
        {
            return _factions.Select(f => f.Name).ToList();
        }

        if (firstSegment.Equals("codes", StringComparison.OrdinalIgnoreCase))
        {
            return _factions.Select(f => f.Code).ToList();
        }

        // ${faction.primary_faction} - code of highest-role faction (lowest enum value = highest rank)
        if (firstSegment.Equals("primary_faction", StringComparison.OrdinalIgnoreCase))
        {
            if (_factions.Count == 0) return null;
            return _factions.MinBy(f => f.Role)?.Code;
        }

        // ${faction.norm_count} - total unique violation types with norms
        if (firstSegment.Equals("norm_count", StringComparison.OrdinalIgnoreCase))
        {
            return _mergedNorms.Count;
        }

        // ${faction.has_norm.<type>} - whether any membership faction defines this norm
        if (firstSegment.Equals("has_norm", StringComparison.OrdinalIgnoreCase))
        {
            if (path.Length < 2) return _mergedNorms.Count > 0;
            return _mergedNorms.ContainsKey(path[1]);
        }

        // ${faction.norm_penalty.<type>} - highest penalty across membership factions
        if (firstSegment.Equals("norm_penalty", StringComparison.OrdinalIgnoreCase))
        {
            if (path.Length < 2) return _mergedNorms.ToDictionary(kvp => kvp.Key, kvp => (object?)kvp.Value.BasePenalty);
            return _mergedNorms.TryGetValue(path[1], out var norm) ? norm.BasePenalty : 0f;
        }

        // Code-scoped access: ${faction.<code>.*}
        if (!_factionsByCode.TryGetValue(firstSegment, out var snapshot))
        {
            return null;
        }

        if (path.Length == 1) return FactionToDict(snapshot);

        return ResolveFactionPath(snapshot, path[1]);
    }

    /// <inheritdoc/>
    public object? GetRootValue()
    {
        return new Dictionary<string, object?>
        {
            ["count"] = _factions.Count,
            ["names"] = _factions.Select(f => f.Name).ToList(),
            ["codes"] = _factions.Select(f => f.Code).ToList(),
            ["primary_faction"] = _factions.Count > 0 ? _factions.MinBy(f => f.Role)?.Code : null,
            ["norm_count"] = _mergedNorms.Count
        };
    }

    /// <inheritdoc/>
    public bool CanResolve(ReadOnlySpan<string> path)
    {
        if (path.Length == 0) return true;

        var firstSegment = path[0];

        if (firstSegment.Equals("count", StringComparison.OrdinalIgnoreCase) ||
            firstSegment.Equals("names", StringComparison.OrdinalIgnoreCase) ||
            firstSegment.Equals("codes", StringComparison.OrdinalIgnoreCase) ||
            firstSegment.Equals("primary_faction", StringComparison.OrdinalIgnoreCase) ||
            firstSegment.Equals("norm_count", StringComparison.OrdinalIgnoreCase) ||
            firstSegment.Equals("has_norm", StringComparison.OrdinalIgnoreCase) ||
            firstSegment.Equals("norm_penalty", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return _factionsByCode.ContainsKey(firstSegment);
    }

    private static object? ResolveFactionPath(FactionSnapshot snapshot, string segment)
    {
        return segment.ToLowerInvariant() switch
        {
            "name" => snapshot.Name,
            "status" => snapshot.Status.ToString(),
            "phase" => snapshot.CurrentPhase,
            "is_realm_baseline" => snapshot.IsRealmBaseline,
            "member_count" => snapshot.MemberCount,
            "role" => snapshot.Role.ToString(),
            _ => null
        };
    }

    private static Dictionary<string, object?> FactionToDict(FactionSnapshot snapshot)
    {
        return new Dictionary<string, object?>
        {
            ["name"] = snapshot.Name,
            ["status"] = snapshot.Status.ToString(),
            ["phase"] = snapshot.CurrentPhase,
            ["is_realm_baseline"] = snapshot.IsRealmBaseline,
            ["member_count"] = snapshot.MemberCount,
            ["role"] = snapshot.Role.ToString()
        };
    }

    /// <summary>
    /// Snapshot of faction data for variable resolution.
    /// </summary>
    internal sealed record FactionSnapshot(
        Guid FactionId,
        string Name,
        string Code,
        FactionStatus Status,
        string? CurrentPhase,
        bool IsRealmBaseline,
        int MemberCount,
        FactionMemberRole Role);

    /// <summary>
    /// Snapshot of a merged norm for variable resolution.
    /// Contains the highest penalty across all membership factions for a violation type.
    /// </summary>
    internal sealed record NormSnapshot(
        string ViolationType,
        float BasePenalty,
        NormSeverity Severity,
        string SourceFactionName);
}
