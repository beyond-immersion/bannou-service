namespace BeyondImmersion.BannouService.Transit;

/// <summary>
/// Calculates optimal routes over the cached connection graph using Dijkstra's algorithm.
/// </summary>
/// <remarks>
/// <para>
/// The route calculator supports three cost functions:
/// <list type="bullet">
///   <item><c>fastest</c>: cost = estimated_hours using baseSpeed x terrainSpeedModifiers</item>
///   <item><c>safest</c>: cost = cumulative_risk</item>
///   <item><c>shortest</c>: cost = distance_km</item>
/// </list>
/// </para>
/// <para>
/// <b>Discovery Filtering</b>: When <c>entityId</c> is provided, discoverable connections
/// are filtered to only those the entity has discovered. When null, discoverable connections
/// are excluded entirely (conservative default).
/// </para>
/// <para>
/// <b>Cross-Realm Routing</b>: When origin and destination are in different realms,
/// the calculator merges graphs for all relevant realms.
/// </para>
/// <para>
/// Does NOT apply entity-specific DI cost modifiers -- those are applied by the
/// variable provider when GOAP evaluates results. Returns objective travel data.
/// </para>
/// </remarks>
public interface ITransitRouteCalculator
{
    /// <summary>
    /// Calculates ranked route options between two locations.
    /// </summary>
    /// <param name="request">Route calculation parameters.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Ranked list of route options, or empty if no route exists.</returns>
    Task<IReadOnlyList<RouteCalculationResult>> CalculateAsync(RouteCalculationRequest request, CancellationToken ct);
}

/// <summary>
/// Parameters for a route calculation request.
/// </summary>
/// <param name="OriginLocationId">Starting location.</param>
/// <param name="DestinationLocationId">Target location.</param>
/// <param name="ModeCode">Preferred mode code, or null for best available.</param>
/// <param name="PreferMultiModal">When true, select best mode per leg.</param>
/// <param name="SortBy">Cost function for ranking routes.</param>
/// <param name="EntityId">Entity requesting the route (for discovery filtering). Null = exclude all discoverable.</param>
/// <param name="IncludeSeasonalClosed">Whether to include seasonally closed connections.</param>
/// <param name="CargoWeightKg">Cargo weight for speed penalty calculation.</param>
/// <param name="MaxLegs">Maximum legs to consider (from configuration).</param>
/// <param name="MaxOptions">Maximum route options to return (from configuration).</param>
/// <param name="CurrentTimeRatio">Current game-time to real-time ratio for real minutes estimation.</param>
/// <param name="CurrentSeason">Current season name from Worldstate, or null if unavailable.</param>
public record RouteCalculationRequest(
    Guid OriginLocationId,
    Guid DestinationLocationId,
    string? ModeCode,
    bool PreferMultiModal,
    RouteSortBy SortBy,
    Guid? EntityId,
    bool IncludeSeasonalClosed,
    decimal CargoWeightKg,
    int MaxLegs,
    int MaxOptions,
    decimal CurrentTimeRatio,
    string? CurrentSeason);

/// <summary>
/// Result of a single route option from the calculator.
/// </summary>
/// <param name="Waypoints">Ordered location IDs from origin to destination.</param>
/// <param name="Connections">Ordered connection IDs for each leg.</param>
/// <param name="LegModes">Per-leg mode codes (for multi-modal routes).</param>
/// <param name="PrimaryModeCode">The mode code used for the most legs (plurality).</param>
/// <param name="TotalDistanceKm">Total route distance in kilometers.</param>
/// <param name="TotalGameHours">Total travel time in game-hours (includes waypoint transfer times).</param>
/// <param name="TotalRealMinutes">Approximate real-time duration based on current time ratio.</param>
/// <param name="AverageRisk">Average risk across all legs.</param>
/// <param name="MaxLegRisk">Maximum risk of any single leg.</param>
/// <param name="AllLegsOpen">Whether all legs are currently open.</param>
/// <param name="SeasonalWarnings">Warnings for legs with upcoming seasonal closures. Null if none.</param>
public record RouteCalculationResult(
    List<Guid> Waypoints,
    List<Guid> Connections,
    List<string> LegModes,
    string PrimaryModeCode,
    decimal TotalDistanceKm,
    decimal TotalGameHours,
    decimal TotalRealMinutes,
    decimal AverageRisk,
    decimal MaxLegRisk,
    bool AllLegsOpen,
    List<SeasonalWarningResult>? SeasonalWarnings);

/// <summary>
/// Seasonal warning for a specific route leg.
/// </summary>
/// <param name="ConnectionId">Connection that has a seasonal warning.</param>
/// <param name="ConnectionName">Display name of the connection, if available.</param>
/// <param name="LegIndex">Index of the leg in the route.</param>
/// <param name="CurrentSeason">Current season name.</param>
/// <param name="ClosingSeason">Season when this connection closes.</param>
/// <param name="ClosingSeasonIndex">How many season transitions until closure (1 = next season).</param>
public record SeasonalWarningResult(
    Guid ConnectionId,
    string? ConnectionName,
    int LegIndex,
    string CurrentSeason,
    string ClosingSeason,
    int ClosingSeasonIndex);
