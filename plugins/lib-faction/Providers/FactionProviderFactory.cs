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
    public string ProviderName => "faction";

    /// <inheritdoc/>
    public async Task<IVariableProvider> CreateAsync(Guid? entityId, CancellationToken ct)
    {
        if (!entityId.HasValue)
        {
            return FactionProvider.Empty;
        }

        // Load the character's faction memberships
        var memberListStore = _stateStoreFactory.GetStore<MembershipListModel>(
            StateStoreDefinitions.FactionMembership);
        var membershipList = await memberListStore.GetAsync(
            $"mem:char:{entityId.Value}", ct);

        if (membershipList == null || membershipList.Memberships.Count == 0)
        {
            return FactionProvider.Empty;
        }

        // Load faction details for each membership
        var factionStore = _stateStoreFactory.GetStore<FactionModel>(
            StateStoreDefinitions.Faction);
        var factions = new List<FactionProvider.FactionSnapshot>();

        foreach (var membership in membershipList.Memberships)
        {
            var faction = await factionStore.GetAsync($"fac:{membership.FactionId}", ct);
            if (faction == null) continue;

            factions.Add(new FactionProvider.FactionSnapshot(
                faction.FactionId,
                faction.Name,
                faction.Code,
                faction.Status,
                faction.CurrentPhase,
                faction.IsRealmBaseline,
                faction.MemberCount,
                membership.Role));
        }

        return new FactionProvider(factions);
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
    public static FactionProvider Empty { get; } = new(new List<FactionSnapshot>());

    private readonly Dictionary<string, FactionSnapshot> _factionsByCode;
    private readonly List<FactionSnapshot> _factions;

    /// <inheritdoc/>
    public string Name => "faction";

    /// <summary>
    /// Creates a new FactionProvider with the given faction data.
    /// </summary>
    /// <param name="factions">Faction snapshots for this character.</param>
    internal FactionProvider(IReadOnlyList<FactionSnapshot> factions)
    {
        _factions = factions.ToList();
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
            ["codes"] = _factions.Select(f => f.Code).ToList()
        };
    }

    /// <inheritdoc/>
    public bool CanResolve(ReadOnlySpan<string> path)
    {
        if (path.Length == 0) return true;

        var firstSegment = path[0];

        if (firstSegment.Equals("count", StringComparison.OrdinalIgnoreCase) ||
            firstSegment.Equals("names", StringComparison.OrdinalIgnoreCase) ||
            firstSegment.Equals("codes", StringComparison.OrdinalIgnoreCase))
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
}
