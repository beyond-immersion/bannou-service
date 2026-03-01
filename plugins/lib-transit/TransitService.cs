using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Character;
using BeyondImmersion.BannouService.ClientEvents;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Inventory;
using BeyondImmersion.BannouService.Location;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Providers;
using BeyondImmersion.BannouService.Resource;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.Species;
using BeyondImmersion.BannouService.Worldstate;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Transit;

/// <summary>
/// Implementation of the Transit service.
/// Provides geographic connectivity, transit mode registry, and journey tracking
/// computed against Worldstate's game clock.
/// </summary>
/// <remarks>
/// <para>
/// <b>FOUNDATION TENETS - PARTIAL CLASS REQUIRED:</b> This class MUST remain a partial class.
/// Generated code (event handlers, permissions) is placed in companion partial classes.
/// </para>
/// <para>
/// <b>SERVICE HIERARCHY (L2 Game Foundation):</b>
/// <list type="bullet">
///   <item>Hard dependencies (constructor injection): ILocationClient (L2), IWorldstateClient (L2),
///         ICharacterClient (L2), ISpeciesClient (L2), IInventoryClient (L2), IResourceClient (L1)</item>
///   <item>Soft dependencies (DI collection): IEnumerable&lt;ITransitCostModifierProvider&gt; (L4, graceful degradation)</item>
/// </list>
/// </para>
/// <para>
/// <b>IMPLEMENTATION TENETS CHECKLIST:</b>
/// <list type="bullet">
///   <item><b>Type Safety:</b> Internal POCOs MUST use proper C# types (enums, Guids, DateTimeOffset) - never string representations. No Enum.Parse in business logic.</item>
///   <item><b>Configuration:</b> ALL config properties in TransitServiceConfiguration MUST be wired up. No hardcoded magic numbers for tunables.</item>
///   <item><b>Events:</b> ALL meaningful state changes MUST publish typed events, even without current consumers.</item>
///   <item><b>Cache Stores:</b> If state-stores.yaml defines cache stores for this service, implement read-through/write-through caching.</item>
///   <item><b>Concurrency:</b> Use GetWithETagAsync + TrySaveAsync for list/index operations. No non-atomic read-modify-write.</item>
/// </list>
/// </para>
/// <para>
/// <b>MODELS:</b> Run <c>make print-models PLUGIN="transit"</c> to view compact request/response model shapes.
/// If print-models fails or generation has not been run, DO NOT proceed with implementation.
/// Generate first (<c>cd scripts &amp;&amp; ./generate-service.sh transit</c>) or ask the developer how to continue.
/// Never guess at model definitions.
/// </para>
/// <para>
/// <b>RELATED FILES:</b>
/// <list type="bullet">
///   <item>Internal data models: TransitServiceModels.cs (storage models, cache entries, internal DTOs)</item>
///   <item>Event handlers: TransitServiceEvents.cs (event consumer registration and handlers)</item>
///   <item>Configuration: Generated/TransitServiceConfiguration.cs</item>
/// </list>
/// </para>
/// </remarks>
[BannouService("transit", typeof(ITransitService), lifetime: ServiceLifetime.Scoped, layer: ServiceLayer.GameFoundation)]
public partial class TransitService : ITransitService
{
    // Infrastructure (L0)
    private readonly IMessageBus _messageBus;
    private readonly IDistributedLockProvider _lockProvider;

    // State stores (7 data stores; lock store accessed via _lockProvider with StateStoreDefinitions.TransitLock)
    private readonly IQueryableStateStore<TransitModeModel> _modeStore;
    private readonly IQueryableStateStore<TransitConnectionModel> _connectionStore;
    private readonly IStateStore<TransitJourneyModel> _journeyStore;
    private readonly IQueryableStateStore<JourneyArchiveModel> _journeyArchiveStore;
    private readonly IStateStore<List<ConnectionGraphEntry>> _connectionGraphStore;
    private readonly IQueryableStateStore<TransitDiscoveryModel> _discoveryStore;
    private readonly IStateStore<HashSet<Guid>> _discoveryCacheStore;

    // Service clients (L1/L2 hard dependencies)
    private readonly ILocationClient _locationClient;
    private readonly IWorldstateClient _worldstateClient;
    private readonly ICharacterClient _characterClient;
    private readonly ISpeciesClient _speciesClient;
    private readonly IInventoryClient _inventoryClient;
    private readonly IResourceClient _resourceClient;

    // Helper services
    private readonly ITransitConnectionGraphCache _graphCache;
    private readonly ITransitRouteCalculator _routeCalculator;

    // DI cost modifier providers (L4 soft dependency, graceful degradation)
    private readonly IReadOnlyList<ITransitCostModifierProvider> _costModifierProviders;

    // Client events
    private readonly IClientEventPublisher _clientEventPublisher;

    // Logging, telemetry, configuration
    private readonly ILogger<TransitService> _logger;
    private readonly ITelemetryProvider _telemetryProvider;
    private readonly TransitServiceConfiguration _configuration;

    /// <summary>
    /// Initializes a new instance of the <see cref="TransitService"/> class.
    /// </summary>
    /// <param name="stateStoreFactory">Factory for creating state store instances.</param>
    /// <param name="messageBus">Event publishing via lib-messaging.</param>
    /// <param name="lockProvider">Distributed locking for journey state transitions and connection status updates.</param>
    /// <param name="locationClient">Location service client for validation and position reporting (L2 hard).</param>
    /// <param name="worldstateClient">Worldstate service client for game-time calculations (L2 hard).</param>
    /// <param name="characterClient">Character service client for species lookups (L2 hard).</param>
    /// <param name="speciesClient">Species service client for mode restriction checks (L2 hard).</param>
    /// <param name="inventoryClient">Inventory service client for item requirement checks (L2 hard).</param>
    /// <param name="resourceClient">Resource service client for cleanup registration (L1 hard).</param>
    /// <param name="graphCache">Connection graph cache for route calculation.</param>
    /// <param name="routeCalculator">Route calculator for Dijkstra path finding.</param>
    /// <param name="costModifierProviders">DI collection of L4 cost enrichment providers (empty if none registered).</param>
    /// <param name="clientEventPublisher">Publisher for server-to-client WebSocket events.</param>
    /// <param name="logger">Structured logger.</param>
    /// <param name="telemetryProvider">Telemetry provider for distributed tracing spans.</param>
    /// <param name="configuration">Typed service configuration.</param>
    /// <param name="eventConsumer">Event consumer for registering event handlers.</param>
    public TransitService(
        IStateStoreFactory stateStoreFactory,
        IMessageBus messageBus,
        IDistributedLockProvider lockProvider,
        ILocationClient locationClient,
        IWorldstateClient worldstateClient,
        ICharacterClient characterClient,
        ISpeciesClient speciesClient,
        IInventoryClient inventoryClient,
        IResourceClient resourceClient,
        ITransitConnectionGraphCache graphCache,
        ITransitRouteCalculator routeCalculator,
        IEnumerable<ITransitCostModifierProvider> costModifierProviders,
        IClientEventPublisher clientEventPublisher,
        ILogger<TransitService> logger,
        ITelemetryProvider telemetryProvider,
        TransitServiceConfiguration configuration,
        IEventConsumer eventConsumer)
    {
        _messageBus = messageBus;
        _lockProvider = lockProvider;

        // Create 7 data stores from StateStoreDefinitions constants (lock store used via _lockProvider)
        _modeStore = stateStoreFactory.GetQueryableStore<TransitModeModel>(StateStoreDefinitions.TransitModes);
        _connectionStore = stateStoreFactory.GetQueryableStore<TransitConnectionModel>(StateStoreDefinitions.TransitConnections);
        _journeyStore = stateStoreFactory.GetStore<TransitJourneyModel>(StateStoreDefinitions.TransitJourneys);
        _journeyArchiveStore = stateStoreFactory.GetQueryableStore<JourneyArchiveModel>(StateStoreDefinitions.TransitJourneysArchive);
        _connectionGraphStore = stateStoreFactory.GetStore<List<ConnectionGraphEntry>>(StateStoreDefinitions.TransitConnectionGraph);
        _discoveryStore = stateStoreFactory.GetQueryableStore<TransitDiscoveryModel>(StateStoreDefinitions.TransitDiscovery);
        _discoveryCacheStore = stateStoreFactory.GetStore<HashSet<Guid>>(StateStoreDefinitions.TransitDiscoveryCache);

        // Service clients (L1/L2 hard dependencies - fail at startup if missing)
        _locationClient = locationClient;
        _worldstateClient = worldstateClient;
        _characterClient = characterClient;
        _speciesClient = speciesClient;
        _inventoryClient = inventoryClient;
        _resourceClient = resourceClient;

        // Helper services
        _graphCache = graphCache;
        _routeCalculator = routeCalculator;

        // L4 cost modifier providers (always non-null, may be empty)
        _costModifierProviders = costModifierProviders.ToList();

        // Client events
        _clientEventPublisher = clientEventPublisher;

        // Logging, telemetry, configuration
        _logger = logger;
        _telemetryProvider = telemetryProvider;
        _configuration = configuration;

        // Register event consumers (must be last in constructor)
        RegisterEventConsumers(eventConsumer);
    }

    #region Mode Management

    private const string MODE_KEY_PREFIX = "mode:";

    /// <summary>
    /// Builds the state store key for a transit mode by code.
    /// </summary>
    private static string BuildModeKey(string code) => $"{MODE_KEY_PREFIX}{code}";

    /// <summary>
    /// Registers a new transit mode type. Mode codes must be unique.
    /// </summary>
    /// <param name="body">Registration request containing mode definition.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Created status with the registered mode, or Conflict if code already exists.</returns>
    public async Task<(StatusCodes, ModeResponse?)> RegisterModeAsync(RegisterModeRequest body, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.transit", "TransitService.RegisterModeAsync");

        _logger.LogDebug("Registering transit mode with code {ModeCode}", body.Code);

        // Distributed lock on mode code to prevent duplicate registrations per IMPLEMENTATION TENETS (multi-instance safety)
        var lockOwner = $"register-mode-{Guid.NewGuid():N}";
        await using var lockResponse = await _lockProvider.LockAsync(
            StateStoreDefinitions.TransitLock,
            $"mode:{body.Code}",
            lockOwner,
            _configuration.LockTimeoutSeconds,
            cancellationToken);

        if (!lockResponse.Success)
        {
            _logger.LogDebug("Could not acquire lock for mode registration {ModeCode}, returning Conflict", body.Code);
            return (StatusCodes.Conflict, null);
        }

        // Check if mode code already exists via direct key lookup (within lock)
        var key = BuildModeKey(body.Code);
        var existingMode = await _modeStore.GetAsync(key, cancellationToken: cancellationToken);

        if (existingMode != null)
        {
            _logger.LogDebug("Transit mode code already exists: {ModeCode}", body.Code);
            return (StatusCodes.Conflict, null);
        }

        var now = DateTimeOffset.UtcNow;

        var model = new TransitModeModel
        {
            Code = body.Code,
            Name = body.Name,
            Description = body.Description,
            BaseSpeedKmPerGameHour = body.BaseSpeedKmPerGameHour,
            TerrainSpeedModifiers = body.TerrainSpeedModifiers?.Select(t => new TerrainSpeedModifierEntry
            {
                TerrainType = t.TerrainType,
                Multiplier = t.Multiplier
            }).ToList(),
            PassengerCapacity = body.PassengerCapacity,
            CargoCapacityKg = body.CargoCapacityKg,
            CargoSpeedPenaltyRate = body.CargoSpeedPenaltyRate,
            CompatibleTerrainTypes = body.CompatibleTerrainTypes?.ToList() ?? new List<string>(),
            ValidEntityTypes = body.ValidEntityTypes?.ToList(),
            Requirements = new TransitModeRequirementsModel
            {
                RequiredItemTag = body.Requirements.RequiredItemTag,
                AllowedSpeciesCodes = body.Requirements.AllowedSpeciesCodes?.ToList(),
                ExcludedSpeciesCodes = body.Requirements.ExcludedSpeciesCodes?.ToList(),
                MinimumPartySize = body.Requirements.MinimumPartySize,
                MaximumEntitySizeCategory = body.Requirements.MaximumEntitySizeCategory
            },
            FatigueRatePerGameHour = body.FatigueRatePerGameHour,
            NoiseLevelNormalized = body.NoiseLevelNormalized,
            RealmRestrictions = body.RealmRestrictions?.ToList(),
            Tags = body.Tags?.ToList(),
            CreatedAt = now,
            ModifiedAt = now
        };

        await _modeStore.SaveAsync(key, model, cancellationToken: cancellationToken);

        // Publish transit-mode.registered event
        await PublishModeRegisteredEventAsync(model, cancellationToken);

        _logger.LogInformation("Registered transit mode {ModeCode}", body.Code);
        return (StatusCodes.Created, new ModeResponse { Mode = MapModeToApi(model) });
    }

    /// <summary>
    /// Gets a transit mode by its unique code.
    /// </summary>
    /// <param name="body">Request containing the mode code to look up.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>OK with the mode data, or NotFound if the code does not exist.</returns>
    public async Task<(StatusCodes, ModeResponse?)> GetModeAsync(GetModeRequest body, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.transit", "TransitService.GetModeAsync");

        _logger.LogDebug("Getting transit mode by code: {ModeCode}", body.Code);

        var key = BuildModeKey(body.Code);
        var model = await _modeStore.GetAsync(key, cancellationToken: cancellationToken);

        if (model == null)
        {
            _logger.LogDebug("Transit mode not found: {ModeCode}", body.Code);
            return (StatusCodes.NotFound, null);
        }

        return (StatusCodes.OK, new ModeResponse { Mode = MapModeToApi(model) });
    }

    /// <summary>
    /// Lists transit modes with optional filters for realm, terrain type, tags, and deprecation status.
    /// </summary>
    /// <param name="body">Request containing filter criteria.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>OK with the list of matching modes.</returns>
    public async Task<(StatusCodes, ListModesResponse?)> ListModesAsync(ListModesRequest body, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.transit", "TransitService.ListModesAsync");

        _logger.LogDebug("Listing transit modes with filters - RealmId: {RealmId}, TerrainType: {TerrainType}, IncludeDeprecated: {IncludeDeprecated}",
            body.RealmId, body.TerrainType, body.IncludeDeprecated);

        // Query all modes and filter in memory (mode count is small -- registry, not transactional data)
        var allModes = await _modeStore.QueryAsync(
            m => true,
            cancellationToken);

        var filtered = allModes.AsEnumerable();

        // Filter out deprecated unless explicitly included
        if (!body.IncludeDeprecated)
        {
            filtered = filtered.Where(m => !m.IsDeprecated);
        }

        // Filter by realm restrictions: include modes with no realm restrictions OR modes that include the specified realm
        if (body.RealmId.HasValue)
        {
            var realmId = body.RealmId.Value;
            filtered = filtered.Where(m => m.RealmRestrictions == null || m.RealmRestrictions.Count == 0 || m.RealmRestrictions.Contains(realmId));
        }

        // Filter by terrain type: include modes with empty compatible terrain (all terrain) OR modes that include the specified terrain
        if (!string.IsNullOrEmpty(body.TerrainType))
        {
            var terrainType = body.TerrainType;
            filtered = filtered.Where(m => m.CompatibleTerrainTypes.Count == 0 || m.CompatibleTerrainTypes.Contains(terrainType));
        }

        // Filter by tags: include modes that contain ALL specified tags
        if (body.Tags != null && body.Tags.Count > 0)
        {
            var requiredTags = body.Tags.ToList();
            filtered = filtered.Where(m => m.Tags != null && requiredTags.All(t => m.Tags.Contains(t)));
        }

        var result = filtered.Select(MapModeToApi).ToList();

        return (StatusCodes.OK, new ListModesResponse { Modes = result });
    }

    /// <summary>
    /// Updates a transit mode's properties. Only non-null request fields are applied.
    /// Tracks which fields changed for the update event.
    /// </summary>
    /// <param name="body">Request containing the mode code and fields to update.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>OK with the updated mode, or NotFound if the code does not exist.</returns>
    public async Task<(StatusCodes, ModeResponse?)> UpdateModeAsync(UpdateModeRequest body, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.transit", "TransitService.UpdateModeAsync");

        _logger.LogDebug("Updating transit mode: {ModeCode}", body.Code);

        var key = BuildModeKey(body.Code);
        var (model, etag) = await _modeStore.GetWithETagAsync(key, cancellationToken);

        if (model == null)
        {
            _logger.LogDebug("Transit mode not found for update: {ModeCode}", body.Code);
            return (StatusCodes.NotFound, null);
        }

        var changedFields = ApplyModeFieldUpdates(model, body);

        if (changedFields.Count > 0)
        {
            model.ModifiedAt = DateTimeOffset.UtcNow;
            var savedEtag = await _modeStore.TrySaveAsync(key, model, etag, cancellationToken);
            if (savedEtag == null)
            {
                _logger.LogDebug("Concurrent modification detected for transit mode {ModeCode}", body.Code);
                return (StatusCodes.Conflict, null);
            }

            // Publish transit-mode.updated event with changedFields
            await PublishModeUpdatedEventAsync(model, changedFields, cancellationToken);

            _logger.LogInformation("Updated transit mode {ModeCode}, changed fields: {ChangedFields}",
                body.Code, string.Join(", ", changedFields));
        }
        else
        {
            _logger.LogDebug("No fields changed for transit mode {ModeCode}, skipping update", body.Code);
        }

        return (StatusCodes.OK, new ModeResponse { Mode = MapModeToApi(model) });
    }

    /// <summary>
    /// Deprecates a transit mode (Category A deprecation, idempotent).
    /// Existing journeys using this mode continue; new journeys cannot use it.
    /// </summary>
    /// <param name="body">Request containing the mode code and deprecation reason.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>OK with the mode data (whether newly deprecated or already deprecated), or NotFound.</returns>
    public async Task<(StatusCodes, ModeResponse?)> DeprecateModeAsync(DeprecateModeRequest body, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.transit", "TransitService.DeprecateModeAsync");

        _logger.LogDebug("Deprecating transit mode: {ModeCode}", body.Code);

        var key = BuildModeKey(body.Code);
        var (model, etag) = await _modeStore.GetWithETagAsync(key, cancellationToken);

        if (model == null)
        {
            _logger.LogDebug("Transit mode not found for deprecation: {ModeCode}", body.Code);
            return (StatusCodes.NotFound, null);
        }

        // Idempotent per IMPLEMENTATION TENETS -- caller's intent (deprecate) is already satisfied
        if (model.IsDeprecated)
        {
            _logger.LogDebug("Transit mode {ModeCode} already deprecated, returning OK (idempotent)", body.Code);
            return (StatusCodes.OK, new ModeResponse { Mode = MapModeToApi(model) });
        }

        model.IsDeprecated = true;
        model.DeprecatedAt = DateTimeOffset.UtcNow;
        model.DeprecationReason = body.Reason;
        model.ModifiedAt = DateTimeOffset.UtcNow;

        var savedEtag = await _modeStore.TrySaveAsync(key, model, etag, cancellationToken);
        if (savedEtag == null)
        {
            _logger.LogDebug("Concurrent modification detected during deprecation of transit mode {ModeCode}", body.Code);
            return (StatusCodes.Conflict, null);
        }

        // Publish transit-mode.updated event with deprecation fields
        await PublishModeUpdatedEventAsync(model, new[] { "isDeprecated", "deprecatedAt", "deprecationReason" }, cancellationToken);

        _logger.LogInformation("Deprecated transit mode {ModeCode}", body.Code);
        return (StatusCodes.OK, new ModeResponse { Mode = MapModeToApi(model) });
    }

    /// <summary>
    /// Reverses deprecation on a transit mode (Category A undeprecation, idempotent).
    /// </summary>
    /// <param name="body">Request containing the mode code.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>OK with the mode data (whether newly undeprecated or already active), or NotFound.</returns>
    public async Task<(StatusCodes, ModeResponse?)> UndeprecateModeAsync(UndeprecateModeRequest body, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.transit", "TransitService.UndeprecateModeAsync");

        _logger.LogDebug("Undeprecating transit mode: {ModeCode}", body.Code);

        var key = BuildModeKey(body.Code);
        var (model, etag) = await _modeStore.GetWithETagAsync(key, cancellationToken);

        if (model == null)
        {
            _logger.LogDebug("Transit mode not found for undeprecation: {ModeCode}", body.Code);
            return (StatusCodes.NotFound, null);
        }

        // Idempotent per IMPLEMENTATION TENETS -- caller's intent (undeprecate) is already satisfied
        if (!model.IsDeprecated)
        {
            _logger.LogDebug("Transit mode {ModeCode} not deprecated, returning OK (idempotent)", body.Code);
            return (StatusCodes.OK, new ModeResponse { Mode = MapModeToApi(model) });
        }

        model.IsDeprecated = false;
        model.DeprecatedAt = null;
        model.DeprecationReason = null;
        model.ModifiedAt = DateTimeOffset.UtcNow;

        var savedEtag = await _modeStore.TrySaveAsync(key, model, etag, cancellationToken);
        if (savedEtag == null)
        {
            _logger.LogDebug("Concurrent modification detected during undeprecation of transit mode {ModeCode}", body.Code);
            return (StatusCodes.Conflict, null);
        }

        // Publish transit-mode.updated event with deprecation fields cleared
        await PublishModeUpdatedEventAsync(model, new[] { "isDeprecated", "deprecatedAt", "deprecationReason" }, cancellationToken);

        _logger.LogInformation("Undeprecated transit mode {ModeCode}", body.Code);
        return (StatusCodes.OK, new ModeResponse { Mode = MapModeToApi(model) });
    }

    /// <summary>
    /// Deletes a deprecated transit mode permanently. Category A: must be deprecated first.
    /// Rejects if active connections reference this mode or active journeys use it.
    /// </summary>
    /// <param name="body">Request containing the mode code.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>OK on successful deletion, NotFound if missing, BadRequest if not deprecated or in use.</returns>
    public async Task<(StatusCodes, DeleteModeResponse?)> DeleteModeAsync(DeleteModeRequest body, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.transit", "TransitService.DeleteModeAsync");

        _logger.LogDebug("Deleting transit mode: {ModeCode}", body.Code);

        var key = BuildModeKey(body.Code);
        var (model, etag) = await _modeStore.GetWithETagAsync(key, cancellationToken);

        if (model == null)
        {
            _logger.LogDebug("Transit mode not found for deletion: {ModeCode}", body.Code);
            return (StatusCodes.NotFound, null);
        }

        // Category A per IMPLEMENTATION TENETS: must be deprecated before deletion
        if (!model.IsDeprecated)
        {
            _logger.LogDebug("Cannot delete non-deprecated transit mode {ModeCode}: must deprecate first", body.Code);
            return (StatusCodes.BadRequest, null);
        }

        // Check no active connections reference this mode in their compatibleModes
        var connectionsUsingMode = await _connectionStore.QueryAsync(
            c => c.CompatibleModes.Contains(body.Code),
            cancellationToken);

        if (connectionsUsingMode.Count > 0)
        {
            _logger.LogDebug("Cannot delete transit mode {ModeCode}: {Count} connections reference this mode",
                body.Code, connectionsUsingMode.Count);
            return (StatusCodes.BadRequest, null);
        }

        // Check no active journeys use this mode.
        // NOTE: Active journeys live in _journeyStore (Redis, not queryable via LINQ).
        // When journey endpoints are implemented (Phase 6), this check must be revisited
        // to scan active Redis journeys. The archive store only holds completed/abandoned
        // journeys, so this query currently returns 0 for in-progress journeys.
        // Phase 6 should add a helper that scans both stores or maintains a mode->journey index.
        var journeysUsingMode = await _journeyArchiveStore.QueryAsync(
            j => j.PrimaryModeCode == body.Code && (j.Status == JourneyStatus.Preparing || j.Status == JourneyStatus.In_transit || j.Status == JourneyStatus.At_waypoint || j.Status == JourneyStatus.Interrupted),
            cancellationToken);

        if (journeysUsingMode.Count > 0)
        {
            _logger.LogDebug("Cannot delete transit mode {ModeCode}: {Count} active journeys use this mode",
                body.Code, journeysUsingMode.Count);
            return (StatusCodes.BadRequest, null);
        }

        // Use ETag to ensure no concurrent modification occurred between read and delete
        var deleteResult = await _modeStore.TrySaveAsync(key, model, etag, cancellationToken);
        if (deleteResult == null)
        {
            _logger.LogDebug("Concurrent modification detected during deletion of transit mode {ModeCode}", body.Code);
            return (StatusCodes.Conflict, null);
        }

        // Delete from store (ETag check confirmed state hasn't changed)
        await _modeStore.DeleteAsync(key, cancellationToken);

        // Publish transit-mode.deleted event
        await PublishModeDeletedEventAsync(model.Code, model.DeprecationReason ?? "deprecated_mode_deleted", cancellationToken);

        _logger.LogInformation("Deleted transit mode {ModeCode}", body.Code);
        return (StatusCodes.OK, new DeleteModeResponse());
    }

    /// <summary>
    /// Checks which transit modes are available for a specific entity.
    /// Evaluates entity type restrictions, species compatibility, item requirements,
    /// and applies DI cost modifier providers for preference cost and speed adjustments.
    /// </summary>
    /// <param name="body">Request containing entity information and optional filters.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>OK with per-mode availability results.</returns>
    public async Task<(StatusCodes, CheckModeAvailabilityResponse?)> CheckModeAvailabilityAsync(CheckModeAvailabilityRequest body, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.transit", "TransitService.CheckModeAvailabilityAsync");

        _logger.LogDebug("Checking mode availability for entity {EntityId} of type {EntityType}",
            body.EntityId, body.EntityType);

        // Get all non-deprecated modes (optionally filtered by specific mode code)
        IEnumerable<TransitModeModel> modes;
        if (!string.IsNullOrEmpty(body.ModeCode))
        {
            var key = BuildModeKey(body.ModeCode);
            var singleMode = await _modeStore.GetAsync(key, cancellationToken: cancellationToken);
            modes = singleMode != null ? new[] { singleMode } : Array.Empty<TransitModeModel>();
        }
        else
        {
            var allModes = await _modeStore.QueryAsync(m => !m.IsDeprecated, cancellationToken);
            modes = allModes;
        }

        // If locationId is provided, filter by realm restrictions
        if (body.LocationId.HasValue)
        {
            // We could resolve realm from location, but since modes have realmRestrictions
            // and the caller provides locationId as a hint, we leave full location-based
            // realm resolution to later phases (connection creation). For now, accept all modes
            // that pass other filters.
        }

        // Resolve character species if entityType is "character" or "npc"
        string? speciesCode = null;
        if (body.EntityType == "character" || body.EntityType == "npc")
        {
            speciesCode = await ResolveEntitySpeciesCodeAsync(body.EntityId, cancellationToken);
        }

        var results = new List<ModeAvailabilityResult>();

        foreach (var mode in modes)
        {
            var result = await EvaluateModeAvailabilityAsync(
                mode, body.EntityId, body.EntityType, speciesCode, cancellationToken);
            results.Add(result);
        }

        return (StatusCodes.OK, new CheckModeAvailabilityResponse { AvailableModes = results });
    }

    /// <summary>
    /// Evaluates whether a specific entity can use a given transit mode.
    /// Checks entity type, species compatibility, item requirements, and applies DI cost modifiers.
    /// </summary>
    private async Task<ModeAvailabilityResult> EvaluateModeAvailabilityAsync(
        TransitModeModel mode,
        Guid entityId,
        string entityType,
        string? speciesCode,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.transit", "TransitService.EvaluateModeAvailabilityAsync");

        // Check entity type restrictions
        if (mode.ValidEntityTypes != null && mode.ValidEntityTypes.Count > 0)
        {
            if (!mode.ValidEntityTypes.Contains(entityType))
            {
                return new ModeAvailabilityResult
                {
                    Code = mode.Code,
                    Available = false,
                    UnavailableReason = "entity_type_not_allowed",
                    EffectiveSpeed = 0,
                    PreferenceCost = 0
                };
            }
        }

        // Check species compatibility
        if (speciesCode != null)
        {
            // Check excluded species
            if (mode.Requirements.ExcludedSpeciesCodes != null &&
                mode.Requirements.ExcludedSpeciesCodes.Contains(speciesCode))
            {
                return new ModeAvailabilityResult
                {
                    Code = mode.Code,
                    Available = false,
                    UnavailableReason = "species_excluded",
                    EffectiveSpeed = 0,
                    PreferenceCost = 0
                };
            }

            // Check allowed species (null = any species allowed)
            if (mode.Requirements.AllowedSpeciesCodes != null &&
                mode.Requirements.AllowedSpeciesCodes.Count > 0 &&
                !mode.Requirements.AllowedSpeciesCodes.Contains(speciesCode))
            {
                return new ModeAvailabilityResult
                {
                    Code = mode.Code,
                    Available = false,
                    UnavailableReason = "wrong_species",
                    EffectiveSpeed = 0,
                    PreferenceCost = 0
                };
            }
        }

        // Check item requirements via IInventoryClient
        if (!string.IsNullOrEmpty(mode.Requirements.RequiredItemTag))
        {
            var hasItem = await CheckEntityHasItemTagAsync(entityId, entityType, mode.Requirements.RequiredItemTag, cancellationToken);
            if (!hasItem)
            {
                return new ModeAvailabilityResult
                {
                    Code = mode.Code,
                    Available = false,
                    UnavailableReason = "missing_item",
                    EffectiveSpeed = 0,
                    PreferenceCost = 0
                };
            }
        }

        // Base effective speed
        var effectiveSpeed = mode.BaseSpeedKmPerGameHour;

        // Apply DI cost modifiers with graceful degradation
        var aggregatedPreferenceCost = 0m;
        var aggregatedSpeedMultiplier = 1m;

        foreach (var provider in _costModifierProviders)
        {
            try
            {
                var modifier = await provider.GetModifierAsync(
                    entityId, entityType, mode.Code, null, cancellationToken);

                aggregatedPreferenceCost += modifier.PreferenceCostDelta;
                aggregatedSpeedMultiplier *= modifier.SpeedMultiplier;
            }
            catch (Exception ex)
            {
                // Graceful degradation per service hierarchy -- L4 providers may fail
                _logger.LogWarning(ex, "Cost modifier provider {ProviderName} failed for entity {EntityId} and mode {ModeCode}, skipping",
                    provider.ProviderName, entityId, mode.Code);
            }
        }

        // Clamp aggregated values to configured bounds per IMPLEMENTATION TENETS (configuration-first)
        aggregatedPreferenceCost = Math.Clamp(aggregatedPreferenceCost, (decimal)_configuration.MinPreferenceCost, (decimal)_configuration.MaxPreferenceCost);
        aggregatedSpeedMultiplier = Math.Clamp(aggregatedSpeedMultiplier, (decimal)_configuration.MinSpeedMultiplier, (decimal)_configuration.MaxSpeedMultiplier);

        effectiveSpeed *= aggregatedSpeedMultiplier;

        return new ModeAvailabilityResult
        {
            Code = mode.Code,
            Available = true,
            UnavailableReason = null,
            EffectiveSpeed = effectiveSpeed,
            PreferenceCost = aggregatedPreferenceCost
        };
    }

    /// <summary>
    /// Resolves the species code for an entity by calling the Character service.
    /// Returns null if the entity is not found or the character has no species.
    /// </summary>
    private async Task<string?> ResolveEntitySpeciesCodeAsync(Guid entityId, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.transit", "TransitService.ResolveEntitySpeciesCodeAsync");
        try
        {
            var characterResponse = await _characterClient.GetCharacterAsync(
                new Character.GetCharacterRequest { CharacterId = entityId },
                cancellationToken);

            var speciesResponse = await _speciesClient.GetSpeciesAsync(
                new Species.GetSpeciesRequest { SpeciesId = characterResponse.SpeciesId },
                cancellationToken);

            return speciesResponse.Code;
        }
        catch (ApiException ex) when (ex.StatusCode == 404)
        {
            _logger.LogDebug("Character or species not found for entity {EntityId}: {StatusCode}", entityId, ex.StatusCode);
            return null;
        }
    }

    /// <summary>
    /// Checks whether the entity has an item with the required tag in any of their inventories.
    /// Uses QueryItems with tag filtering for a single API call instead of iterating containers.
    /// </summary>
    private async Task<bool> CheckEntityHasItemTagAsync(Guid entityId, string entityType, string requiredItemTag, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.transit", "TransitService.CheckEntityHasItemTagAsync");

        // Map entity type string to ContainerOwnerType for inventory query
        if (!Enum.TryParse<Inventory.ContainerOwnerType>(entityType, ignoreCase: true, out var ownerType))
        {
            // Entity type doesn't map to an inventory owner type -- cannot have items
            _logger.LogDebug("Entity type {EntityType} has no inventory mapping, skipping item tag check", entityType);
            return false;
        }

        try
        {
            var queryResponse = await _inventoryClient.QueryItemsAsync(
                new Inventory.QueryItemsRequest
                {
                    OwnerId = entityId,
                    OwnerType = ownerType,
                    Tags = new[] { requiredItemTag },
                    Limit = 1
                },
                cancellationToken);

            return queryResponse.TotalCount > 0;
        }
        catch (ApiException ex) when (ex.StatusCode == 404)
        {
            _logger.LogDebug("Inventory not found for entity {EntityId}: {StatusCode}", entityId, ex.StatusCode);
            return false;
        }
    }

    /// <summary>
    /// Applies non-null field updates from an UpdateModeRequest to an existing mode model.
    /// Returns the list of field names that were actually changed.
    /// </summary>
    private static List<string> ApplyModeFieldUpdates(TransitModeModel model, UpdateModeRequest request)
    {
        var changedFields = new List<string>();

        if (request.Name != null && request.Name != model.Name)
        {
            model.Name = request.Name;
            changedFields.Add("name");
        }

        if (request.Description != null && request.Description != model.Description)
        {
            model.Description = request.Description;
            changedFields.Add("description");
        }

        if (request.BaseSpeedKmPerGameHour.HasValue && request.BaseSpeedKmPerGameHour.Value != model.BaseSpeedKmPerGameHour)
        {
            model.BaseSpeedKmPerGameHour = request.BaseSpeedKmPerGameHour.Value;
            changedFields.Add("baseSpeedKmPerGameHour");
        }

        if (request.TerrainSpeedModifiers != null)
        {
            model.TerrainSpeedModifiers = request.TerrainSpeedModifiers.Select(t => new TerrainSpeedModifierEntry
            {
                TerrainType = t.TerrainType,
                Multiplier = t.Multiplier
            }).ToList();
            changedFields.Add("terrainSpeedModifiers");
        }

        if (request.PassengerCapacity.HasValue && request.PassengerCapacity.Value != model.PassengerCapacity)
        {
            model.PassengerCapacity = request.PassengerCapacity.Value;
            changedFields.Add("passengerCapacity");
        }

        if (request.CargoCapacityKg.HasValue && request.CargoCapacityKg.Value != model.CargoCapacityKg)
        {
            model.CargoCapacityKg = request.CargoCapacityKg.Value;
            changedFields.Add("cargoCapacityKg");
        }

        if (request.CargoSpeedPenaltyRate.HasValue && request.CargoSpeedPenaltyRate != model.CargoSpeedPenaltyRate)
        {
            model.CargoSpeedPenaltyRate = request.CargoSpeedPenaltyRate;
            changedFields.Add("cargoSpeedPenaltyRate");
        }

        if (request.CompatibleTerrainTypes != null)
        {
            model.CompatibleTerrainTypes = request.CompatibleTerrainTypes.ToList();
            changedFields.Add("compatibleTerrainTypes");
        }

        if (request.ValidEntityTypes != null)
        {
            model.ValidEntityTypes = request.ValidEntityTypes.ToList();
            changedFields.Add("validEntityTypes");
        }

        if (request.Requirements != null)
        {
            model.Requirements = new TransitModeRequirementsModel
            {
                RequiredItemTag = request.Requirements.RequiredItemTag,
                AllowedSpeciesCodes = request.Requirements.AllowedSpeciesCodes?.ToList(),
                ExcludedSpeciesCodes = request.Requirements.ExcludedSpeciesCodes?.ToList(),
                MinimumPartySize = request.Requirements.MinimumPartySize,
                MaximumEntitySizeCategory = request.Requirements.MaximumEntitySizeCategory
            };
            changedFields.Add("requirements");
        }

        if (request.FatigueRatePerGameHour.HasValue && request.FatigueRatePerGameHour.Value != model.FatigueRatePerGameHour)
        {
            model.FatigueRatePerGameHour = request.FatigueRatePerGameHour.Value;
            changedFields.Add("fatigueRatePerGameHour");
        }

        if (request.NoiseLevelNormalized.HasValue && request.NoiseLevelNormalized.Value != model.NoiseLevelNormalized)
        {
            model.NoiseLevelNormalized = request.NoiseLevelNormalized.Value;
            changedFields.Add("noiseLevelNormalized");
        }

        if (request.RealmRestrictions != null)
        {
            model.RealmRestrictions = request.RealmRestrictions.ToList();
            changedFields.Add("realmRestrictions");
        }

        if (request.Tags != null)
        {
            model.Tags = request.Tags.ToList();
            changedFields.Add("tags");
        }

        return changedFields;
    }

    /// <summary>
    /// Maps an internal <see cref="TransitModeModel"/> to the generated <see cref="TransitMode"/> API model.
    /// </summary>
    private static TransitMode MapModeToApi(TransitModeModel model)
    {
        return new TransitMode
        {
            Code = model.Code,
            Name = model.Name,
            Description = model.Description,
            BaseSpeedKmPerGameHour = model.BaseSpeedKmPerGameHour,
            TerrainSpeedModifiers = model.TerrainSpeedModifiers?.Select(t => new TerrainSpeedModifier
            {
                TerrainType = t.TerrainType,
                Multiplier = t.Multiplier
            }).ToList(),
            PassengerCapacity = model.PassengerCapacity,
            CargoCapacityKg = model.CargoCapacityKg,
            CargoSpeedPenaltyRate = model.CargoSpeedPenaltyRate,
            CompatibleTerrainTypes = model.CompatibleTerrainTypes.ToList(),
            ValidEntityTypes = model.ValidEntityTypes?.ToList(),
            Requirements = new TransitModeRequirements
            {
                RequiredItemTag = model.Requirements.RequiredItemTag,
                AllowedSpeciesCodes = model.Requirements.AllowedSpeciesCodes?.ToList(),
                ExcludedSpeciesCodes = model.Requirements.ExcludedSpeciesCodes?.ToList(),
                MinimumPartySize = model.Requirements.MinimumPartySize,
                MaximumEntitySizeCategory = model.Requirements.MaximumEntitySizeCategory
            },
            FatigueRatePerGameHour = model.FatigueRatePerGameHour,
            NoiseLevelNormalized = model.NoiseLevelNormalized,
            RealmRestrictions = model.RealmRestrictions?.ToList(),
            IsDeprecated = model.IsDeprecated,
            DeprecatedAt = model.DeprecatedAt,
            DeprecationReason = model.DeprecationReason,
            Tags = model.Tags?.ToList(),
            CreatedAt = model.CreatedAt,
            ModifiedAt = model.ModifiedAt
        };
    }

    #endregion

    #region Mode Event Publishing

    /// <summary>
    /// Publishes a transit-mode.registered event with full entity data.
    /// </summary>
    private async Task PublishModeRegisteredEventAsync(TransitModeModel model, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.transit", "TransitService.PublishModeRegisteredEventAsync");

        var eventModel = new TransitModeRegisteredEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            Code = model.Code,
            Name = model.Name,
            Description = model.Description,
            BaseSpeedKmPerGameHour = model.BaseSpeedKmPerGameHour,
            TerrainSpeedModifiers = model.TerrainSpeedModifiers?.Select(t => new TerrainSpeedModifier
            {
                TerrainType = t.TerrainType,
                Multiplier = t.Multiplier
            }).ToList(),
            PassengerCapacity = model.PassengerCapacity,
            CargoCapacityKg = model.CargoCapacityKg,
            CargoSpeedPenaltyRate = model.CargoSpeedPenaltyRate,
            CompatibleTerrainTypes = model.CompatibleTerrainTypes.ToList(),
            ValidEntityTypes = model.ValidEntityTypes?.ToList(),
            Requirements = new TransitModeRequirements
            {
                RequiredItemTag = model.Requirements.RequiredItemTag,
                AllowedSpeciesCodes = model.Requirements.AllowedSpeciesCodes?.ToList(),
                ExcludedSpeciesCodes = model.Requirements.ExcludedSpeciesCodes?.ToList(),
                MinimumPartySize = model.Requirements.MinimumPartySize,
                MaximumEntitySizeCategory = model.Requirements.MaximumEntitySizeCategory
            },
            FatigueRatePerGameHour = model.FatigueRatePerGameHour,
            NoiseLevelNormalized = model.NoiseLevelNormalized,
            RealmRestrictions = model.RealmRestrictions?.ToList(),
            IsDeprecated = model.IsDeprecated,
            DeprecatedAt = model.DeprecatedAt,
            DeprecationReason = model.DeprecationReason,
            Tags = model.Tags?.ToList(),
            CreatedAt = model.CreatedAt,
            ModifiedAt = model.ModifiedAt
        };

        var published = await _messageBus.TryPublishAsync("transit-mode.registered", eventModel, cancellationToken: cancellationToken);
        if (published)
        {
            _logger.LogDebug("Published transit-mode.registered event for {ModeCode}", model.Code);
        }
        else
        {
            _logger.LogWarning("Failed to publish transit-mode.registered event for {ModeCode}", model.Code);
        }
    }

    /// <summary>
    /// Publishes a transit-mode.updated event with current state and changed fields.
    /// Used for property updates, deprecation, and undeprecation.
    /// </summary>
    private async Task PublishModeUpdatedEventAsync(TransitModeModel model, IEnumerable<string> changedFields, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.transit", "TransitService.PublishModeUpdatedEventAsync");

        var changedFieldsList = changedFields.ToList();
        var eventModel = new TransitModeUpdatedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            Mode = MapModeToApi(model),
            ChangedFields = changedFieldsList
        };

        var published = await _messageBus.TryPublishAsync("transit-mode.updated", eventModel, cancellationToken: cancellationToken);
        if (published)
        {
            _logger.LogDebug("Published transit-mode.updated event for {ModeCode} with changed fields: {ChangedFields}",
                model.Code, string.Join(", ", changedFieldsList));
        }
        else
        {
            _logger.LogWarning("Failed to publish transit-mode.updated event for {ModeCode}", model.Code);
        }
    }

    /// <summary>
    /// Publishes a transit-mode.deleted event after mode removal.
    /// </summary>
    private async Task PublishModeDeletedEventAsync(string code, string deletedReason, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.transit", "TransitService.PublishModeDeletedEventAsync");

        var eventModel = new TransitModeDeletedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            Code = code,
            DeletedReason = deletedReason
        };

        var published = await _messageBus.TryPublishAsync("transit-mode.deleted", eventModel, cancellationToken: cancellationToken);
        if (published)
        {
            _logger.LogDebug("Published transit-mode.deleted event for {ModeCode}", code);
        }
        else
        {
            _logger.LogWarning("Failed to publish transit-mode.deleted event for {ModeCode}", code);
        }
    }

    #endregion

    /// <summary>
    /// Implementation of CreateConnection operation.
    /// </summary>
    public async Task<(StatusCodes, ConnectionResponse?)> CreateConnectionAsync(CreateConnectionRequest body, CancellationToken cancellationToken)
    {
        // TODO: Implement CreateConnection
        await Task.CompletedTask;
        throw new NotImplementedException("Method CreateConnection not yet implemented");
    }

    /// <summary>
    /// Implementation of GetConnection operation.
    /// </summary>
    public async Task<(StatusCodes, ConnectionResponse?)> GetConnectionAsync(GetConnectionRequest body, CancellationToken cancellationToken)
    {
        // TODO: Implement GetConnection
        await Task.CompletedTask;
        throw new NotImplementedException("Method GetConnection not yet implemented");
    }

    /// <summary>
    /// Implementation of QueryConnections operation.
    /// </summary>
    public async Task<(StatusCodes, QueryConnectionsResponse?)> QueryConnectionsAsync(QueryConnectionsRequest body, CancellationToken cancellationToken)
    {
        // TODO: Implement QueryConnections
        await Task.CompletedTask;
        throw new NotImplementedException("Method QueryConnections not yet implemented");
    }

    /// <summary>
    /// Implementation of UpdateConnection operation.
    /// </summary>
    public async Task<(StatusCodes, ConnectionResponse?)> UpdateConnectionAsync(UpdateConnectionRequest body, CancellationToken cancellationToken)
    {
        // TODO: Implement UpdateConnection
        await Task.CompletedTask;
        throw new NotImplementedException("Method UpdateConnection not yet implemented");
    }

    /// <summary>
    /// Implementation of UpdateConnectionStatus operation.
    /// </summary>
    public async Task<(StatusCodes, ConnectionResponse?)> UpdateConnectionStatusAsync(UpdateConnectionStatusRequest body, CancellationToken cancellationToken)
    {
        // TODO: Implement UpdateConnectionStatus
        await Task.CompletedTask;
        throw new NotImplementedException("Method UpdateConnectionStatus not yet implemented");
    }

    /// <summary>
    /// Implementation of DeleteConnection operation.
    /// </summary>
    public async Task<(StatusCodes, DeleteConnectionResponse?)> DeleteConnectionAsync(DeleteConnectionRequest body, CancellationToken cancellationToken)
    {
        // TODO: Implement DeleteConnection
        await Task.CompletedTask;
        throw new NotImplementedException("Method DeleteConnection not yet implemented");
    }

    /// <summary>
    /// Implementation of BulkSeedConnections operation.
    /// </summary>
    public async Task<(StatusCodes, BulkSeedConnectionsResponse?)> BulkSeedConnectionsAsync(BulkSeedConnectionsRequest body, CancellationToken cancellationToken)
    {
        // TODO: Implement BulkSeedConnections
        await Task.CompletedTask;
        throw new NotImplementedException("Method BulkSeedConnections not yet implemented");
    }

    /// <summary>
    /// Implementation of CreateJourney operation.
    /// </summary>
    public async Task<(StatusCodes, JourneyResponse?)> CreateJourneyAsync(CreateJourneyRequest body, CancellationToken cancellationToken)
    {
        // TODO: Implement CreateJourney
        await Task.CompletedTask;
        throw new NotImplementedException("Method CreateJourney not yet implemented");
    }

    /// <summary>
    /// Implementation of DepartJourney operation.
    /// </summary>
    public async Task<(StatusCodes, JourneyResponse?)> DepartJourneyAsync(DepartJourneyRequest body, CancellationToken cancellationToken)
    {
        // TODO: Implement DepartJourney
        await Task.CompletedTask;
        throw new NotImplementedException("Method DepartJourney not yet implemented");
    }

    /// <summary>
    /// Implementation of ResumeJourney operation.
    /// </summary>
    public async Task<(StatusCodes, JourneyResponse?)> ResumeJourneyAsync(ResumeJourneyRequest body, CancellationToken cancellationToken)
    {
        // TODO: Implement ResumeJourney
        await Task.CompletedTask;
        throw new NotImplementedException("Method ResumeJourney not yet implemented");
    }

    /// <summary>
    /// Implementation of AdvanceJourney operation.
    /// </summary>
    public async Task<(StatusCodes, JourneyResponse?)> AdvanceJourneyAsync(AdvanceJourneyRequest body, CancellationToken cancellationToken)
    {
        // TODO: Implement AdvanceJourney
        await Task.CompletedTask;
        throw new NotImplementedException("Method AdvanceJourney not yet implemented");
    }

    /// <summary>
    /// Implementation of AdvanceBatchJourneys operation.
    /// </summary>
    public async Task<(StatusCodes, AdvanceBatchResponse?)> AdvanceBatchJourneysAsync(AdvanceBatchRequest body, CancellationToken cancellationToken)
    {
        // TODO: Implement AdvanceBatchJourneys
        await Task.CompletedTask;
        throw new NotImplementedException("Method AdvanceBatchJourneys not yet implemented");
    }

    /// <summary>
    /// Implementation of ArriveJourney operation.
    /// </summary>
    public async Task<(StatusCodes, JourneyResponse?)> ArriveJourneyAsync(ArriveJourneyRequest body, CancellationToken cancellationToken)
    {
        // TODO: Implement ArriveJourney
        await Task.CompletedTask;
        throw new NotImplementedException("Method ArriveJourney not yet implemented");
    }

    /// <summary>
    /// Implementation of InterruptJourney operation.
    /// </summary>
    public async Task<(StatusCodes, JourneyResponse?)> InterruptJourneyAsync(InterruptJourneyRequest body, CancellationToken cancellationToken)
    {
        // TODO: Implement InterruptJourney
        await Task.CompletedTask;
        throw new NotImplementedException("Method InterruptJourney not yet implemented");
    }

    /// <summary>
    /// Implementation of AbandonJourney operation.
    /// </summary>
    public async Task<(StatusCodes, JourneyResponse?)> AbandonJourneyAsync(AbandonJourneyRequest body, CancellationToken cancellationToken)
    {
        // TODO: Implement AbandonJourney
        await Task.CompletedTask;
        throw new NotImplementedException("Method AbandonJourney not yet implemented");
    }

    /// <summary>
    /// Implementation of GetJourney operation.
    /// </summary>
    public async Task<(StatusCodes, JourneyResponse?)> GetJourneyAsync(GetJourneyRequest body, CancellationToken cancellationToken)
    {
        // TODO: Implement GetJourney
        await Task.CompletedTask;
        throw new NotImplementedException("Method GetJourney not yet implemented");
    }

    /// <summary>
    /// Implementation of QueryJourneysByConnection operation.
    /// </summary>
    public async Task<(StatusCodes, ListJourneysResponse?)> QueryJourneysByConnectionAsync(QueryJourneysByConnectionRequest body, CancellationToken cancellationToken)
    {
        // TODO: Implement QueryJourneysByConnection
        await Task.CompletedTask;
        throw new NotImplementedException("Method QueryJourneysByConnection not yet implemented");
    }

    /// <summary>
    /// Implementation of ListJourneys operation.
    /// </summary>
    public async Task<(StatusCodes, ListJourneysResponse?)> ListJourneysAsync(ListJourneysRequest body, CancellationToken cancellationToken)
    {
        // TODO: Implement ListJourneys
        await Task.CompletedTask;
        throw new NotImplementedException("Method ListJourneys not yet implemented");
    }

    /// <summary>
    /// Implementation of QueryJourneyArchive operation.
    /// </summary>
    public async Task<(StatusCodes, ListJourneysResponse?)> QueryJourneyArchiveAsync(QueryJourneyArchiveRequest body, CancellationToken cancellationToken)
    {
        // TODO: Implement QueryJourneyArchive
        await Task.CompletedTask;
        throw new NotImplementedException("Method QueryJourneyArchive not yet implemented");
    }

    /// <summary>
    /// Implementation of CalculateRoute operation.
    /// </summary>
    public async Task<(StatusCodes, CalculateRouteResponse?)> CalculateRouteAsync(CalculateRouteRequest body, CancellationToken cancellationToken)
    {
        // TODO: Implement CalculateRoute
        await Task.CompletedTask;
        throw new NotImplementedException("Method CalculateRoute not yet implemented");
    }

    /// <summary>
    /// Implementation of RevealDiscovery operation.
    /// </summary>
    public async Task<(StatusCodes, RevealDiscoveryResponse?)> RevealDiscoveryAsync(RevealDiscoveryRequest body, CancellationToken cancellationToken)
    {
        // TODO: Implement RevealDiscovery
        await Task.CompletedTask;
        throw new NotImplementedException("Method RevealDiscovery not yet implemented");
    }

    /// <summary>
    /// Implementation of ListDiscoveries operation.
    /// </summary>
    public async Task<(StatusCodes, ListDiscoveriesResponse?)> ListDiscoveriesAsync(ListDiscoveriesRequest body, CancellationToken cancellationToken)
    {
        // TODO: Implement ListDiscoveries
        await Task.CompletedTask;
        throw new NotImplementedException("Method ListDiscoveries not yet implemented");
    }

    /// <summary>
    /// Implementation of CheckDiscoveries operation.
    /// </summary>
    public async Task<(StatusCodes, CheckDiscoveriesResponse?)> CheckDiscoveriesAsync(CheckDiscoveriesRequest body, CancellationToken cancellationToken)
    {
        // TODO: Implement CheckDiscoveries
        await Task.CompletedTask;
        throw new NotImplementedException("Method CheckDiscoveries not yet implemented");
    }

    /// <summary>
    /// Implementation of CleanupByLocation operation.
    /// TODO: Implement business logic for this method.
    /// </summary>
    public async Task<StatusCodes> CleanupByLocationAsync(CleanupByLocationRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Executing CleanupByLocation operation");

        // TODO: Implement your business logic here
        // Note: The generated controller wraps this method with try/catch for error handling.
        // Do NOT add an outer try/catch here -- exceptions will be caught, logged, and
        // published as error events by the controller automatically.
        await Task.CompletedTask; // IMPLEMENTATION TENETS: async methods must contain await
        throw new NotImplementedException("Method CleanupByLocation not yet implemented");

        // Example: return StatusCodes.NoContent;
    }

    /// <summary>
    /// Implementation of CleanupByCharacter operation.
    /// TODO: Implement business logic for this method.
    /// </summary>
    public async Task<StatusCodes> CleanupByCharacterAsync(CleanupByCharacterRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Executing CleanupByCharacter operation");

        // TODO: Implement your business logic here
        // Note: The generated controller wraps this method with try/catch for error handling.
        // Do NOT add an outer try/catch here -- exceptions will be caught, logged, and
        // published as error events by the controller automatically.
        await Task.CompletedTask; // IMPLEMENTATION TENETS: async methods must contain await
        throw new NotImplementedException("Method CleanupByCharacter not yet implemented");

        // Example: return StatusCodes.NoContent;
    }

}
