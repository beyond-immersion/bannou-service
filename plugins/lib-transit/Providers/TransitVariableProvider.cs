using BeyondImmersion.Bannou.BehaviorExpressions.Expressions;
using BeyondImmersion.BannouService.Services;

namespace BeyondImmersion.BannouService.Transit.Providers;

/// <summary>
/// Provides transit data for ABML expressions via <c>${transit.*}</c> paths.
/// Created per-entity by <see cref="TransitVariableProviderFactory"/>.
/// </summary>
/// <remarks>
/// <para>Supported variable paths:</para>
/// <list type="bullet">
///   <item><description><c>${transit.mode.CODE.available}</c> - bool: Can this entity currently use this transit mode?</description></item>
///   <item><description><c>${transit.mode.CODE.speed}</c> - decimal: Effective speed in km/game-hour for this entity on this mode</description></item>
///   <item><description><c>${transit.mode.CODE.preference_cost}</c> - decimal: GOAP cost modifier from DI enrichment providers</description></item>
///   <item><description><c>${transit.journey.active}</c> - bool: Is this entity currently on a journey?</description></item>
///   <item><description><c>${transit.journey.mode}</c> - string: Transit mode of the active journey (null if no active journey)</description></item>
///   <item><description><c>${transit.journey.destination_code}</c> - string: Location code of the journey destination (null if no active journey)</description></item>
///   <item><description><c>${transit.journey.eta_hours}</c> - decimal: Estimated game-hours until arrival (null if no active journey)</description></item>
///   <item><description><c>${transit.journey.progress}</c> - decimal: Journey progress 0.0 to 1.0 (null if no active journey)</description></item>
///   <item><description><c>${transit.journey.remaining_legs}</c> - int: Number of route legs remaining (null if no active journey)</description></item>
///   <item><description><c>${transit.discovered_connections}</c> - int: Total number of discoverable connections this entity has revealed</description></item>
///   <item><description><c>${transit.connection.CODE.discovered}</c> - bool: Has this entity discovered the named connection?</description></item>
/// </list>
/// <para>
/// <b>Not implemented as snapshot variables:</b> <c>${transit.nearest.CODE.hours}</c> and
/// <c>${transit.nearest.CODE.best_mode}</c> require per-target Dijkstra route calculations
/// with an unbounded target set (ABML expressions can reference any location code).
/// This makes snapshot-time precomputation infeasible: the factory cannot know which location
/// codes will be queried, and pre-computing routes to all locations would require O(locations)
/// Dijkstra invocations per entity per tick. These variables are available via direct Transit
/// API calls (<c>POST /transit/route/calculate</c>) from ABML action handlers instead, where
/// the specific target location is known at evaluation time.
/// </para>
/// </remarks>
public sealed class TransitVariableProvider : IVariableProvider
{
    /// <summary>
    /// Empty provider for actors without transit data (non-character actors or no active journeys).
    /// Returns null for all variable paths.
    /// </summary>
    public static TransitVariableProvider Empty { get; } = new(
        null,
        null,
        new HashSet<Guid>(),
        new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase),
        new Dictionary<string, TransitModeSnapshot>(StringComparer.OrdinalIgnoreCase));

    private readonly TransitJourneyModel? _activeJourney;
    private readonly string? _destinationLocationCode;
    private readonly HashSet<Guid> _discoveredConnectionIds;
    private readonly Dictionary<string, bool> _discoveredConnectionCodes;
    private readonly Dictionary<string, TransitModeSnapshot> _modeAvailability;

    /// <inheritdoc/>
    public string Name => VariableProviderDefinitions.Transit;

    /// <summary>
    /// Creates a new transit variable provider with the given transit data.
    /// </summary>
    /// <param name="activeJourney">The entity's active journey, or null if no journey is active.</param>
    /// <param name="destinationLocationCode">The destination location code, or null if no active journey.</param>
    /// <param name="discoveredConnectionIds">Set of connection IDs the entity has discovered (for count).</param>
    /// <param name="discoveredConnectionCodes">Map of connection codes to discovery status (for code-based lookup).</param>
    /// <param name="modeAvailability">Per-mode availability snapshots with DI cost modifier data.</param>
    internal TransitVariableProvider(
        TransitJourneyModel? activeJourney,
        string? destinationLocationCode,
        HashSet<Guid> discoveredConnectionIds,
        Dictionary<string, bool> discoveredConnectionCodes,
        Dictionary<string, TransitModeSnapshot> modeAvailability)
    {
        _activeJourney = activeJourney;
        _destinationLocationCode = destinationLocationCode;
        _discoveredConnectionIds = discoveredConnectionIds;
        _discoveredConnectionCodes = discoveredConnectionCodes;
        _modeAvailability = modeAvailability;
    }

    /// <inheritdoc/>
    public object? GetValue(ReadOnlySpan<string> path)
    {
        if (path.Length == 0) return GetRootValue();

        var firstSegment = path[0];

        // ${transit.mode.<code>.*}
        if (firstSegment.Equals("mode", StringComparison.OrdinalIgnoreCase))
        {
            return path.Length > 1 ? GetModeValue(path.Slice(1)) : GetModeRoot();
        }

        // ${transit.journey.*}
        if (firstSegment.Equals("journey", StringComparison.OrdinalIgnoreCase))
        {
            return path.Length > 1 ? GetJourneyValue(path.Slice(1)) : GetJourneyRoot();
        }

        // ${transit.discovered_connections}
        if (firstSegment.Equals("discovered_connections", StringComparison.OrdinalIgnoreCase))
        {
            return _discoveredConnectionIds.Count;
        }

        // ${transit.connection.<code>.discovered}
        if (firstSegment.Equals("connection", StringComparison.OrdinalIgnoreCase))
        {
            return path.Length > 1 ? GetConnectionValue(path.Slice(1)) : null;
        }

        return null;
    }

    /// <inheritdoc/>
    public object? GetRootValue()
    {
        return new Dictionary<string, object?>
        {
            ["mode"] = GetModeRoot(),
            ["journey"] = GetJourneyRoot(),
            ["discovered_connections"] = _discoveredConnectionIds.Count
        };
    }

    /// <inheritdoc/>
    public bool CanResolve(ReadOnlySpan<string> path)
    {
        if (path.Length == 0) return true;

        var firstSegment = path[0];

        if (firstSegment.Equals("mode", StringComparison.OrdinalIgnoreCase))
        {
            // ${transit.mode} or ${transit.mode.<code>.<property>}
            return path.Length == 1 || (path.Length >= 3 && IsValidModeProperty(path[2]));
        }

        if (firstSegment.Equals("journey", StringComparison.OrdinalIgnoreCase))
        {
            if (path.Length == 1) return true;
            return IsValidJourneyPath(path[1]);
        }

        if (firstSegment.Equals("discovered_connections", StringComparison.OrdinalIgnoreCase))
        {
            return path.Length == 1;
        }

        if (firstSegment.Equals("connection", StringComparison.OrdinalIgnoreCase))
        {
            // ${transit.connection.<code>.discovered} requires at least 2 more segments
            return path.Length >= 3 && path[2].Equals("discovered", StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    /// <summary>
    /// Gets a mode sub-variable value.
    /// Path format: <c>mode.{code}.{property}</c> where property is available, speed, or preference_cost.
    /// </summary>
    /// <param name="path">Path segments after "mode".</param>
    /// <returns>The variable value, or null if the mode code is not found.</returns>
    private object? GetModeValue(ReadOnlySpan<string> path)
    {
        // Expect: <code>.<property>
        if (path.Length < 2) return null;

        var modeCode = path[0];
        var property = path[1];

        if (!_modeAvailability.TryGetValue(modeCode, out var snapshot))
        {
            return null;
        }

        if (property.Equals("available", StringComparison.OrdinalIgnoreCase))
        {
            return snapshot.Available;
        }

        if (property.Equals("speed", StringComparison.OrdinalIgnoreCase))
        {
            return snapshot.EffectiveSpeed;
        }

        if (property.Equals("preference_cost", StringComparison.OrdinalIgnoreCase))
        {
            return snapshot.PreferenceCost;
        }

        return null;
    }

    /// <summary>
    /// Gets all mode variables as a dictionary keyed by mode code.
    /// </summary>
    /// <returns>Dictionary of mode code to mode variable dictionaries.</returns>
    private Dictionary<string, object?> GetModeRoot()
    {
        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (code, snapshot) in _modeAvailability)
        {
            result[code] = new Dictionary<string, object?>
            {
                ["available"] = snapshot.Available,
                ["speed"] = snapshot.EffectiveSpeed,
                ["preference_cost"] = snapshot.PreferenceCost
            };
        }

        return result;
    }

    /// <summary>
    /// Gets a journey sub-variable value.
    /// </summary>
    /// <param name="path">Path segments after "journey".</param>
    /// <returns>The variable value, or null if not found or no active journey.</returns>
    private object? GetJourneyValue(ReadOnlySpan<string> path)
    {
        if (path.Length == 0) return GetJourneyRoot();

        var segment = path[0];

        // ${transit.journey.active}
        if (segment.Equals("active", StringComparison.OrdinalIgnoreCase))
        {
            return _activeJourney != null;
        }

        // All remaining journey variables require an active journey
        if (_activeJourney == null) return null;

        // ${transit.journey.mode}
        if (segment.Equals("mode", StringComparison.OrdinalIgnoreCase))
        {
            return _activeJourney.PrimaryModeCode;
        }

        // ${transit.journey.destination_code}
        if (segment.Equals("destination_code", StringComparison.OrdinalIgnoreCase))
        {
            return _destinationLocationCode;
        }

        // ${transit.journey.eta_hours}
        if (segment.Equals("eta_hours", StringComparison.OrdinalIgnoreCase))
        {
            return ComputeEtaHours();
        }

        // ${transit.journey.progress}
        if (segment.Equals("progress", StringComparison.OrdinalIgnoreCase))
        {
            return ComputeProgress();
        }

        // ${transit.journey.remaining_legs}
        if (segment.Equals("remaining_legs", StringComparison.OrdinalIgnoreCase))
        {
            return ComputeRemainingLegs();
        }

        return null;
    }

    /// <summary>
    /// Gets all journey variables as a dictionary.
    /// </summary>
    /// <returns>Dictionary of journey variable values.</returns>
    private Dictionary<string, object?> GetJourneyRoot()
    {
        var isActive = _activeJourney != null;

        return new Dictionary<string, object?>
        {
            ["active"] = isActive,
            ["mode"] = _activeJourney?.PrimaryModeCode,
            ["destination_code"] = isActive ? _destinationLocationCode : null,
            ["eta_hours"] = isActive ? ComputeEtaHours() : null,
            ["progress"] = isActive ? ComputeProgress() : null,
            ["remaining_legs"] = isActive ? ComputeRemainingLegs() : null
        };
    }

    /// <summary>
    /// Gets a connection sub-variable value.
    /// Path format: <c>connection.{code}.discovered</c> where code is the connection's
    /// human-readable code string (e.g., "ironforge-stormwind"), not a GUID.
    /// </summary>
    /// <param name="path">Path segments after "connection".</param>
    /// <returns>True if discovered, false if not, null if connection code not found.</returns>
    private object? GetConnectionValue(ReadOnlySpan<string> path)
    {
        // Expect: <code>.discovered
        if (path.Length < 2) return null;

        var connectionCode = path[0];
        var property = path[1];

        if (!property.Equals("discovered", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        // Look up connection code in the discovered-codes map (case-insensitive)
        if (_discoveredConnectionCodes.TryGetValue(connectionCode, out var isDiscovered))
        {
            return isDiscovered;
        }

        // Connection code not found in discoverable connections -- return null
        return null;
    }

    /// <summary>
    /// Computes the estimated game-hours remaining until arrival.
    /// Uses the sum of remaining leg durations including waypoint transfer times.
    /// </summary>
    /// <returns>Estimated hours remaining, or null if computation is not possible.</returns>
    private decimal? ComputeEtaHours()
    {
        if (_activeJourney == null) return null;

        // Sum remaining leg durations (current leg onwards)
        var remainingHours = 0m;
        for (var i = _activeJourney.CurrentLegIndex; i < _activeJourney.Legs.Count; i++)
        {
            var leg = _activeJourney.Legs[i];
            if (leg.Status != JourneyLegStatus.Completed && leg.Status != JourneyLegStatus.Skipped)
            {
                remainingHours += leg.EstimatedDurationGameHours;
                if (leg.WaypointTransferTimeGameHours.HasValue)
                {
                    remainingHours += leg.WaypointTransferTimeGameHours.Value;
                }
            }
        }

        return remainingHours;
    }

    /// <summary>
    /// Computes the journey progress as a value from 0.0 to 1.0.
    /// Based on the ratio of completed legs to total legs.
    /// </summary>
    /// <returns>Progress value, or null if no legs exist.</returns>
    private decimal? ComputeProgress()
    {
        if (_activeJourney == null || _activeJourney.Legs.Count == 0) return null;

        var completedLegs = _activeJourney.Legs.Count(l =>
            l.Status == JourneyLegStatus.Completed || l.Status == JourneyLegStatus.Skipped);

        return (decimal)completedLegs / _activeJourney.Legs.Count;
    }

    /// <summary>
    /// Computes the number of remaining (non-completed, non-skipped) legs.
    /// </summary>
    /// <returns>Number of remaining legs, or null if no active journey.</returns>
    private int? ComputeRemainingLegs()
    {
        if (_activeJourney == null) return null;

        return _activeJourney.Legs.Count(l =>
            l.Status != JourneyLegStatus.Completed && l.Status != JourneyLegStatus.Skipped);
    }

    /// <summary>
    /// Checks if a path segment is a valid journey sub-path.
    /// </summary>
    /// <param name="segment">The path segment to check.</param>
    /// <returns>True if the segment is a recognized journey variable name.</returns>
    private static bool IsValidJourneyPath(string segment)
    {
        return segment.Equals("active", StringComparison.OrdinalIgnoreCase)
            || segment.Equals("mode", StringComparison.OrdinalIgnoreCase)
            || segment.Equals("destination_code", StringComparison.OrdinalIgnoreCase)
            || segment.Equals("eta_hours", StringComparison.OrdinalIgnoreCase)
            || segment.Equals("progress", StringComparison.OrdinalIgnoreCase)
            || segment.Equals("remaining_legs", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Checks if a path segment is a valid mode property name.
    /// </summary>
    /// <param name="segment">The path segment to check.</param>
    /// <returns>True if the segment is a recognized mode property.</returns>
    private static bool IsValidModeProperty(string segment)
    {
        return segment.Equals("available", StringComparison.OrdinalIgnoreCase)
            || segment.Equals("speed", StringComparison.OrdinalIgnoreCase)
            || segment.Equals("preference_cost", StringComparison.OrdinalIgnoreCase);
    }
}
