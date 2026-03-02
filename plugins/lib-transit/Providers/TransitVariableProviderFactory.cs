using BeyondImmersion.Bannou.BehaviorExpressions.Expressions;
using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService.Location;
using BeyondImmersion.BannouService.Providers;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Transit.Providers;

/// <summary>
/// Factory for creating <see cref="TransitVariableProvider"/> instances that expose
/// <c>${transit.*}</c> variables for ABML expression evaluation.
/// Registered with DI as <see cref="IVariableProviderFactory"/> for Actor (L2) to discover
/// via <c>IEnumerable&lt;IVariableProviderFactory&gt;</c> dependency injection.
/// </summary>
/// <remarks>
/// <para>
/// Transit is character-scoped: the <c>characterId</c> parameter determines which entity's
/// transit data to load (active journeys, discovery counts, mode availability).
/// If no <c>characterId</c> is provided, returns <see cref="TransitVariableProvider.Empty"/>
/// which returns null for all variable paths.
/// </para>
/// <para>
/// Data is loaded from multiple state stores and service clients:
/// <list type="bullet">
///   <item><c>transit-journeys</c> (Redis): Active journey for the entity</item>
///   <item><c>transit-discovery-cache</c> (Redis): Cached discovery set for fast count queries</item>
///   <item><c>transit-discovery</c> (MySQL): Durable discovery records (fallback when cache misses)</item>
///   <item><c>transit-connections</c> (MySQL): Connection data for code-to-ID mapping</item>
///   <item><c>transit-modes</c> (MySQL): Mode definitions for availability computation</item>
///   <item><c>ILocationClient</c>: Resolves destination location codes from IDs</item>
///   <item><c>ITransitCostModifierProvider</c> (DI collection): L4 cost enrichment for mode preference costs</item>
/// </list>
/// </para>
/// <para>
/// State stores are created from <see cref="IStateStoreFactory"/> in the constructor.
/// Internal model types (<c>TransitJourneyModel</c>, <c>TransitDiscoveryModel</c>) are
/// accessed within the same assembly, avoiding CS0051 exposure issues.
/// </para>
/// </remarks>
public sealed class TransitVariableProviderFactory : IVariableProviderFactory
{
    private readonly IStateStore<TransitJourneyModel> _journeyStore;
    private readonly IStateStore<List<Guid>> _journeyIndexStore;
    private readonly IStateStore<HashSet<Guid>> _discoveryCacheStore;
    private readonly IQueryableStateStore<TransitDiscoveryModel> _discoveryStore;
    private readonly IQueryableStateStore<TransitConnectionModel> _connectionStore;
    private readonly IQueryableStateStore<TransitModeModel> _modeStore;
    private readonly ILocationClient _locationClient;
    private readonly IReadOnlyList<ITransitCostModifierProvider> _costModifierProviders;
    private readonly TransitServiceConfiguration _configuration;
    private readonly ITelemetryProvider _telemetryProvider;
    private readonly ILogger<TransitVariableProviderFactory> _logger;

    /// <summary>
    /// Creates a new transit variable provider factory.
    /// </summary>
    /// <param name="stateStoreFactory">State store factory for accessing transit state stores.</param>
    /// <param name="locationClient">Location service client for resolving location codes (L2 hard).</param>
    /// <param name="costModifierProviders">DI collection of L4 cost enrichment providers (empty if none registered).</param>
    /// <param name="configuration">Transit service configuration for cache TTL settings.</param>
    /// <param name="telemetryProvider">Telemetry provider for span instrumentation.</param>
    /// <param name="logger">Structured logger.</param>
    public TransitVariableProviderFactory(
        IStateStoreFactory stateStoreFactory,
        ILocationClient locationClient,
        IEnumerable<ITransitCostModifierProvider> costModifierProviders,
        TransitServiceConfiguration configuration,
        ITelemetryProvider telemetryProvider,
        ILogger<TransitVariableProviderFactory> logger)
    {
        _journeyStore = stateStoreFactory.GetStore<TransitJourneyModel>(StateStoreDefinitions.TransitJourneys);
        _journeyIndexStore = stateStoreFactory.GetStore<List<Guid>>(StateStoreDefinitions.TransitJourneys);
        _discoveryCacheStore = stateStoreFactory.GetStore<HashSet<Guid>>(StateStoreDefinitions.TransitDiscoveryCache);
        _discoveryStore = stateStoreFactory.GetQueryableStore<TransitDiscoveryModel>(StateStoreDefinitions.TransitDiscovery);
        _connectionStore = stateStoreFactory.GetQueryableStore<TransitConnectionModel>(StateStoreDefinitions.TransitConnections);
        _modeStore = stateStoreFactory.GetQueryableStore<TransitModeModel>(StateStoreDefinitions.TransitModes);
        _locationClient = locationClient;
        _costModifierProviders = costModifierProviders.ToList();
        _configuration = configuration;
        _telemetryProvider = telemetryProvider;
        _logger = logger;
    }

    /// <inheritdoc/>
    public string ProviderName => VariableProviderDefinitions.Transit;

    /// <inheritdoc/>
    public async Task<IVariableProvider> CreateAsync(Guid? characterId, Guid realmId, Guid? locationId, CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.transit", "TransitVariableProviderFactory.CreateAsync");

        if (!characterId.HasValue)
        {
            return TransitVariableProvider.Empty;
        }

        var entityId = characterId.Value;

        // Load active journey for this entity and resolve destination location code
        var activeJourney = await FindActiveJourneyForEntityAsync(entityId, ct);
        var destinationLocationCode = await ResolveDestinationLocationCodeAsync(activeJourney, ct);

        // Load discovery data: discovered connection IDs + code-to-discovered map
        var discoveredConnectionIds = await LoadDiscoveredConnectionIdsAsync(entityId, ct);
        var discoveredConnectionCodes = await BuildDiscoveredConnectionCodeMapAsync(discoveredConnectionIds, ct);

        // Load mode availability data with DI cost modifiers
        var modeAvailability = await LoadModeAvailabilityAsync(entityId, ct);

        return new TransitVariableProvider(
            activeJourney,
            destinationLocationCode,
            discoveredConnectionIds,
            discoveredConnectionCodes,
            modeAvailability);
    }

    /// <summary>
    /// Finds the active journey for the given entity by querying the journey archive store
    /// for active journeys belonging to this entity.
    /// Returns null if the entity has no active journey.
    /// </summary>
    /// <param name="entityId">The entity to find an active journey for.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The active journey model, or null if no active journey exists.</returns>
    private async Task<TransitJourneyModel?> FindActiveJourneyForEntityAsync(Guid entityId, CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.transit", "TransitVariableProviderFactory.FindActiveJourneyForEntityAsync");

        var journeyIndex = await _journeyIndexStore.GetAsync(TransitService.JOURNEY_INDEX_KEY, cancellationToken: ct);
        if (journeyIndex == null || journeyIndex.Count == 0)
        {
            return null;
        }

        // Scan active journeys for one belonging to this entity that is not completed/abandoned
        foreach (var journeyId in journeyIndex)
        {
            var journey = await _journeyStore.GetAsync(
                TransitService.BuildJourneyKey(journeyId), cancellationToken: ct);

            if (journey != null &&
                journey.EntityId == entityId &&
                journey.Status != JourneyStatus.Arrived &&
                journey.Status != JourneyStatus.Abandoned)
            {
                return journey;
            }
        }

        return null;
    }

    /// <summary>
    /// Resolves the destination location code for an active journey by calling the Location service.
    /// Returns null if no active journey or the location cannot be resolved.
    /// </summary>
    /// <param name="activeJourney">The active journey, or null.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The destination location code, or null.</returns>
    private async Task<string?> ResolveDestinationLocationCodeAsync(TransitJourneyModel? activeJourney, CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.transit", "TransitVariableProviderFactory.ResolveDestinationLocationCodeAsync");

        if (activeJourney == null)
        {
            return null;
        }

        try
        {
            var locationResponse = await _locationClient.GetLocationAsync(
                new GetLocationRequest { LocationId = activeJourney.DestinationLocationId }, ct);
            return locationResponse.Code;
        }
        catch (ApiException ex) when (ex.StatusCode == 404)
        {
            _logger.LogDebug("Destination location {LocationId} not found for journey {JourneyId}",
                activeJourney.DestinationLocationId, activeJourney.Id);
            return null;
        }
    }

    /// <summary>
    /// Loads the set of discovered connection IDs for an entity.
    /// Tries Redis cache first, falls back to MySQL query.
    /// </summary>
    /// <param name="entityId">The entity whose discoveries to load.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Set of discovered connection IDs, or empty set if none.</returns>
    private async Task<HashSet<Guid>> LoadDiscoveredConnectionIdsAsync(Guid entityId, CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.transit", "TransitVariableProviderFactory.LoadDiscoveredConnectionIdsAsync");

        // Try Redis cache first (TTL governed by DiscoveryCacheTtlSeconds config)
        var cacheKey = $"discovery-cache:{entityId}";
        var cachedSet = await _discoveryCacheStore.GetAsync(cacheKey, cancellationToken: ct);
        if (cachedSet != null)
        {
            return cachedSet;
        }

        // Fallback to MySQL query
        var discoveries = await _discoveryStore.QueryAsync(
            d => d.EntityId == entityId, ct);

        var discoveredIds = new HashSet<Guid>(discoveries.Select(d => d.ConnectionId));
        return discoveredIds;
    }

    /// <summary>
    /// Builds a mapping of connection codes to discovery status for the entity.
    /// Queries the connection store to resolve connection IDs to their codes.
    /// Only connections that have a code assigned are included in the map.
    /// </summary>
    /// <param name="discoveredConnectionIds">The set of connection IDs the entity has discovered.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Dictionary mapping connection codes (lowercase) to true for discovered connections.</returns>
    private async Task<Dictionary<string, bool>> BuildDiscoveredConnectionCodeMapAsync(
        HashSet<Guid> discoveredConnectionIds, CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.transit", "TransitVariableProviderFactory.BuildDiscoveredConnectionCodeMapAsync");

        if (discoveredConnectionIds.Count == 0)
        {
            return new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        }

        // Query all discoverable connections to build a code-to-ID reference.
        // The connection store is MySQL (durable registry), so this is a reasonable query.
        // Only discoverable connections are relevant for the ${transit.connection.<code>.discovered} variable.
        var discoverableConnections = await _connectionStore.QueryAsync(
            c => c.Discoverable, ct);

        var codeMap = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var connection in discoverableConnections)
        {
            if (!string.IsNullOrEmpty(connection.Code))
            {
                codeMap[connection.Code] = discoveredConnectionIds.Contains(connection.Id);
            }
        }

        return codeMap;
    }

    /// <summary>
    /// Loads all non-deprecated transit modes and computes per-mode availability data
    /// for the given entity, including DI cost modifier aggregation. The mode registry
    /// is a small durable MySQL dataset (typically tens of modes per game), making
    /// snapshot-time loading feasible.
    /// </summary>
    /// <param name="entityId">The entity to evaluate mode availability for.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Dictionary mapping mode codes to availability data.</returns>
    private async Task<Dictionary<string, TransitModeSnapshot>> LoadModeAvailabilityAsync(Guid entityId, CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.transit", "TransitVariableProviderFactory.LoadModeAvailabilityAsync");

        var modes = await _modeStore.QueryAsync(m => !m.IsDeprecated, ct);
        var result = new Dictionary<string, TransitModeSnapshot>(StringComparer.OrdinalIgnoreCase);

        foreach (var mode in modes)
        {
            // Base effective speed from mode definition
            var effectiveSpeed = mode.BaseSpeedKmPerGameHour;

            // Aggregate DI cost modifiers with graceful degradation (L4 providers may fail)
            var aggregatedPreferenceCost = 0m;
            var aggregatedSpeedMultiplier = 1m;

            foreach (var provider in _costModifierProviders)
            {
                try
                {
                    // Pass null for connectionId since this is a general mode query, not connection-specific
                    var modifier = await provider.GetModifierAsync(
                        entityId, "character", mode.Code, null, ct);

                    aggregatedPreferenceCost += modifier.PreferenceCostDelta;
                    aggregatedSpeedMultiplier *= modifier.SpeedMultiplier;
                }
                catch (Exception ex)
                {
                    // Graceful degradation per service hierarchy -- L4 providers may fail
                    _logger.LogWarning(ex,
                        "Cost modifier provider {ProviderName} failed for entity {EntityId} mode {ModeCode}, skipping",
                        provider.ProviderName, entityId, mode.Code);
                }
            }

            // Clamp aggregated values to configured bounds per IMPLEMENTATION TENETS (configuration-first)
            aggregatedPreferenceCost = Math.Clamp(
                aggregatedPreferenceCost,
                (decimal)_configuration.MinPreferenceCost,
                (decimal)_configuration.MaxPreferenceCost);
            aggregatedSpeedMultiplier = Math.Clamp(
                aggregatedSpeedMultiplier,
                (decimal)_configuration.MinSpeedMultiplier,
                (decimal)_configuration.MaxSpeedMultiplier);

            effectiveSpeed *= aggregatedSpeedMultiplier;

            result[mode.Code] = new TransitModeSnapshot(
                Available: true,
                EffectiveSpeed: effectiveSpeed,
                PreferenceCost: aggregatedPreferenceCost);
        }

        return result;
    }
}

/// <summary>
/// Snapshot of a transit mode's availability and computed values for a specific entity.
/// Used by <see cref="TransitVariableProvider"/> to resolve <c>${transit.mode.CODE.*}</c> variables.
/// </summary>
/// <param name="Available">Whether the entity can currently use this mode.</param>
/// <param name="EffectiveSpeed">Effective speed in km/game-hour accounting for DI cost modifier speed multipliers.</param>
/// <param name="PreferenceCost">Aggregated GOAP preference cost from DI enrichment providers (0.0 = neutral).</param>
internal sealed record TransitModeSnapshot(
    bool Available,
    decimal EffectiveSpeed,
    decimal PreferenceCost);
