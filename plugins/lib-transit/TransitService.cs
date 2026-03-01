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

        // Distributed lock to prevent concurrent modification during precondition checks and deletion
        var lockOwner = $"delete-mode-{Guid.NewGuid():N}";
        await using var lockResponse = await _lockProvider.LockAsync(
            StateStoreDefinitions.TransitLock,
            $"mode:{body.Code}",
            lockOwner,
            _configuration.LockTimeoutSeconds,
            cancellationToken);

        if (!lockResponse.Success)
        {
            _logger.LogDebug("Could not acquire lock for mode deletion {ModeCode}, returning Conflict", body.Code);
            return (StatusCodes.Conflict, null);
        }

        var key = BuildModeKey(body.Code);
        var model = await _modeStore.GetAsync(key, cancellationToken: cancellationToken);

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

        // Delete from store (lock ensures no concurrent modification)
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

    #region Connection Management

    private const string CONNECTION_KEY_PREFIX = "connection:";
    private const string CONNECTION_CODE_KEY_PREFIX = "connection:code:";

    /// <summary>
    /// Builds the state store key for a connection by ID.
    /// </summary>
    private static string BuildConnectionKey(Guid id) => $"{CONNECTION_KEY_PREFIX}{id}";

    /// <summary>
    /// Builds the state store key for a connection by code.
    /// </summary>
    private static string BuildConnectionCodeKey(string code) => $"{CONNECTION_CODE_KEY_PREFIX}{code}";

    /// <summary>
    /// Creates a connection between two locations. Validates locations exist, derives realm IDs,
    /// validates mode codes, validates seasonal keys against Worldstate calendars, and checks for duplicates.
    /// </summary>
    /// <param name="body">Connection creation request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Created with the new connection, or error status.</returns>
    public async Task<(StatusCodes, ConnectionResponse?)> CreateConnectionAsync(CreateConnectionRequest body, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.transit", "TransitService.CreateConnectionAsync");

        _logger.LogDebug("Creating connection from location {FromLocationId} to location {ToLocationId}",
            body.FromLocationId, body.ToLocationId);

        // Validate not same location
        if (body.FromLocationId == body.ToLocationId)
        {
            _logger.LogDebug("Cannot create connection from location to itself: {LocationId}", body.FromLocationId);
            return (StatusCodes.BadRequest, null);
        }

        // Validate both locations exist and derive realm IDs
        Location.LocationResponse fromLocation;
        Location.LocationResponse toLocation;
        try
        {
            fromLocation = await _locationClient.GetLocationAsync(
                new Location.GetLocationRequest { LocationId = body.FromLocationId },
                cancellationToken);
        }
        catch (ApiException ex) when (ex.StatusCode == 404)
        {
            _logger.LogDebug("From location not found: {FromLocationId}", body.FromLocationId);
            return (StatusCodes.BadRequest, null);
        }

        try
        {
            toLocation = await _locationClient.GetLocationAsync(
                new Location.GetLocationRequest { LocationId = body.ToLocationId },
                cancellationToken);
        }
        catch (ApiException ex) when (ex.StatusCode == 404)
        {
            _logger.LogDebug("To location not found: {ToLocationId}", body.ToLocationId);
            return (StatusCodes.BadRequest, null);
        }

        var fromRealmId = fromLocation.RealmId;
        var toRealmId = toLocation.RealmId;
        var crossRealm = fromRealmId != toRealmId;

        // Validate all mode codes exist
        if (body.CompatibleModes != null && body.CompatibleModes.Count > 0)
        {
            foreach (var modeCode in body.CompatibleModes)
            {
                var modeKey = BuildModeKey(modeCode);
                var mode = await _modeStore.GetAsync(modeKey, cancellationToken: cancellationToken);
                if (mode == null)
                {
                    _logger.LogDebug("Invalid mode code in compatible modes: {ModeCode}", modeCode);
                    return (StatusCodes.BadRequest, null);
                }
            }
        }

        // Validate seasonal keys against Worldstate calendar if seasonal availability specified
        if (body.SeasonalAvailability != null && body.SeasonalAvailability.Count > 0)
        {
            var seasonValidationResult = await ValidateSeasonalKeysAsync(
                body.SeasonalAvailability, fromRealmId, toRealmId, crossRealm, cancellationToken);
            if (!seasonValidationResult)
            {
                return (StatusCodes.BadRequest, null);
            }
        }

        // Distributed lock on connection identity to prevent duplicate creation per IMPLEMENTATION TENETS (multi-instance safety).
        // Lock key uses code if provided (primary uniqueness), otherwise the location pair.
        var lockResource = !string.IsNullOrEmpty(body.Code)
            ? $"connection:code:{body.Code}"
            : $"connection:pair:{body.FromLocationId}:{body.ToLocationId}";
        var lockOwner = $"create-connection-{Guid.NewGuid():N}";
        await using var lockResponse = await _lockProvider.LockAsync(
            StateStoreDefinitions.TransitLock,
            lockResource,
            lockOwner,
            _configuration.LockTimeoutSeconds,
            cancellationToken);

        if (!lockResponse.Success)
        {
            _logger.LogDebug("Could not acquire lock for connection creation ({LockResource}), returning Conflict", lockResource);
            return (StatusCodes.Conflict, null);
        }

        // Check for duplicate connection by code (within lock)
        if (!string.IsNullOrEmpty(body.Code))
        {
            var existingByCode = await _connectionStore.QueryAsync(
                c => c.Code == body.Code,
                cancellationToken);
            if (existingByCode.Count > 0)
            {
                _logger.LogDebug("Connection code already exists: {Code}", body.Code);
                return (StatusCodes.Conflict, null);
            }
        }

        // Check for duplicate connection by location pair -- same direction (within lock)
        var existingByPair = await _connectionStore.QueryAsync(
            c => c.FromLocationId == body.FromLocationId && c.ToLocationId == body.ToLocationId,
            cancellationToken);
        if (existingByPair.Count > 0)
        {
            _logger.LogDebug("Connection already exists between locations {FromLocationId} and {ToLocationId}",
                body.FromLocationId, body.ToLocationId);
            return (StatusCodes.Conflict, null);
        }

        var now = DateTimeOffset.UtcNow;
        var connectionId = Guid.NewGuid();

        var model = new TransitConnectionModel
        {
            Id = connectionId,
            FromLocationId = body.FromLocationId,
            ToLocationId = body.ToLocationId,
            Bidirectional = body.Bidirectional,
            DistanceKm = body.DistanceKm,
            TerrainType = body.TerrainType,
            CompatibleModes = body.CompatibleModes?.ToList() ?? new List<string>(),
            SeasonalAvailability = body.SeasonalAvailability?.Select(s => new SeasonalAvailabilityModel
            {
                Season = s.Season,
                Available = s.Available
            }).ToList(),
            BaseRiskLevel = body.BaseRiskLevel,
            RiskDescription = body.RiskDescription,
            Status = ConnectionStatus.Open,
            StatusReason = null,
            StatusChangedAt = now,
            Discoverable = body.Discoverable,
            Name = body.Name,
            Code = body.Code,
            Tags = body.Tags?.ToList(),
            FromRealmId = fromRealmId,
            ToRealmId = toRealmId,
            CrossRealm = crossRealm,
            CreatedAt = now,
            ModifiedAt = now
        };

        var key = BuildConnectionKey(connectionId);
        await _connectionStore.SaveAsync(key, model, cancellationToken: cancellationToken);

        // Invalidate graph cache for affected realms
        var affectedRealms = crossRealm
            ? new[] { fromRealmId, toRealmId }
            : new[] { fromRealmId };
        await _graphCache.InvalidateAsync(affectedRealms, cancellationToken);

        // Publish transit-connection.created event (x-lifecycle)
        await PublishConnectionCreatedEventAsync(model, cancellationToken);

        _logger.LogInformation("Created connection {ConnectionId} from {FromLocationId} to {ToLocationId} in realm {FromRealmId}",
            connectionId, body.FromLocationId, body.ToLocationId, fromRealmId);

        return (StatusCodes.Created, new ConnectionResponse { Connection = MapConnectionToApi(model) });
    }

    /// <summary>
    /// Gets a connection by ID or code. One of connectionId or code must be provided.
    /// </summary>
    /// <param name="body">Request containing connectionId or code.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>OK with the connection data, or NotFound.</returns>
    public async Task<(StatusCodes, ConnectionResponse?)> GetConnectionAsync(GetConnectionRequest body, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.transit", "TransitService.GetConnectionAsync");

        _logger.LogDebug("Getting connection by ID {ConnectionId} or code {Code}",
            body.ConnectionId, body.Code);

        TransitConnectionModel? model = null;

        if (body.ConnectionId.HasValue)
        {
            var key = BuildConnectionKey(body.ConnectionId.Value);
            model = await _connectionStore.GetAsync(key, cancellationToken: cancellationToken);
        }
        else if (!string.IsNullOrEmpty(body.Code))
        {
            var results = await _connectionStore.QueryAsync(
                c => c.Code == body.Code,
                cancellationToken);
            model = results.FirstOrDefault();
        }

        if (model == null)
        {
            _logger.LogDebug("Connection not found for ID {ConnectionId} or code {Code}",
                body.ConnectionId, body.Code);
            return (StatusCodes.NotFound, null);
        }

        return (StatusCodes.OK, new ConnectionResponse { Connection = MapConnectionToApi(model) });
    }

    /// <summary>
    /// Queries connections with filters: locationId, realmId, terrainType, modeCode, status, tags,
    /// crossRealm, includeSeasonalClosed, with pagination.
    /// </summary>
    /// <param name="body">Query filters and pagination parameters.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>OK with the filtered connections list.</returns>
    public async Task<(StatusCodes, QueryConnectionsResponse?)> QueryConnectionsAsync(QueryConnectionsRequest body, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.transit", "TransitService.QueryConnectionsAsync");

        _logger.LogDebug("Querying connections with filters - LocationId: {LocationId}, RealmId: {RealmId}, TerrainType: {TerrainType}, Status: {Status}",
            body.LocationId, body.RealmId, body.TerrainType, body.Status);

        // Query all connections and filter in memory (connection count is moderate)
        var allConnections = await _connectionStore.QueryAsync(c => true, cancellationToken);
        var filtered = allConnections.AsEnumerable();

        // Filter by specific from location
        if (body.FromLocationId.HasValue)
        {
            var fromId = body.FromLocationId.Value;
            filtered = filtered.Where(c => c.FromLocationId == fromId);
        }

        // Filter by specific to location
        if (body.ToLocationId.HasValue)
        {
            var toId = body.ToLocationId.Value;
            filtered = filtered.Where(c => c.ToLocationId == toId);
        }

        // Filter by either end (locationId matches from or to)
        if (body.LocationId.HasValue)
        {
            var locId = body.LocationId.Value;
            filtered = filtered.Where(c => c.FromLocationId == locId || c.ToLocationId == locId);
        }

        // Filter by realm (touching this realm)
        if (body.RealmId.HasValue)
        {
            var realmId = body.RealmId.Value;
            filtered = filtered.Where(c => c.FromRealmId == realmId || c.ToRealmId == realmId);
        }

        // Filter by cross-realm flag
        if (body.CrossRealm.HasValue)
        {
            var crossRealm = body.CrossRealm.Value;
            filtered = filtered.Where(c => c.CrossRealm == crossRealm);
        }

        // Filter by terrain type
        if (!string.IsNullOrEmpty(body.TerrainType))
        {
            var terrainType = body.TerrainType;
            filtered = filtered.Where(c => c.TerrainType == terrainType);
        }

        // Filter by mode code
        if (!string.IsNullOrEmpty(body.ModeCode))
        {
            var modeCode = body.ModeCode;
            filtered = filtered.Where(c => c.CompatibleModes.Contains(modeCode));
        }

        // Filter by status
        if (body.Status.HasValue)
        {
            var status = body.Status.Value;
            filtered = filtered.Where(c => c.Status == status);
        }

        // Filter out seasonal_closed unless explicitly included
        if (!body.IncludeSeasonalClosed)
        {
            filtered = filtered.Where(c => c.Status != ConnectionStatus.Seasonal_closed);
        }

        // Filter by tags (all specified tags must be present)
        if (body.Tags != null && body.Tags.Count > 0)
        {
            var requiredTags = body.Tags.ToList();
            filtered = filtered.Where(c => c.Tags != null && requiredTags.All(t => c.Tags.Contains(t)));
        }

        // Pagination -- schema defaults (Page=1, PageSize=20) and [Range] validation
        // ensure body.Page and body.PageSize are always valid; no secondary fallback per IMPLEMENTATION TENETS
        var totalCount = filtered.Count();
        var paged = filtered.Skip((body.Page - 1) * body.PageSize).Take(body.PageSize);

        var result = paged.Select(MapConnectionToApi).ToList();

        return (StatusCodes.OK, new QueryConnectionsResponse
        {
            Connections = result,
            TotalCount = totalCount
        });
    }

    /// <summary>
    /// Updates a connection's properties (not status -- use update-status for that).
    /// Uses optimistic concurrency via ETag.
    /// </summary>
    /// <param name="body">Request containing the connection ID and fields to update.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>OK with the updated connection, NotFound, or Conflict on ETag mismatch.</returns>
    public async Task<(StatusCodes, ConnectionResponse?)> UpdateConnectionAsync(UpdateConnectionRequest body, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.transit", "TransitService.UpdateConnectionAsync");

        _logger.LogDebug("Updating connection {ConnectionId}", body.ConnectionId);

        var key = BuildConnectionKey(body.ConnectionId);
        var (model, etag) = await _connectionStore.GetWithETagAsync(key, cancellationToken);

        if (model == null)
        {
            _logger.LogDebug("Connection not found for update: {ConnectionId}", body.ConnectionId);
            return (StatusCodes.NotFound, null);
        }

        var changedFields = await ApplyConnectionFieldUpdatesAsync(model, body, cancellationToken);

        // Null indicates a validation failure (invalid mode code or duplicate connection code)
        if (changedFields == null)
        {
            _logger.LogDebug("Validation failed during connection field updates for {ConnectionId}", body.ConnectionId);
            return (StatusCodes.BadRequest, null);
        }

        if (changedFields.Count > 0)
        {
            model.ModifiedAt = DateTimeOffset.UtcNow;
            var savedEtag = await _connectionStore.TrySaveAsync(key, model, etag, cancellationToken);
            if (savedEtag == null)
            {
                _logger.LogDebug("Concurrent modification detected for connection {ConnectionId}", body.ConnectionId);
                return (StatusCodes.Conflict, null);
            }

            // Invalidate graph cache for affected realms
            var affectedRealms = model.CrossRealm
                ? new[] { model.FromRealmId, model.ToRealmId }
                : new[] { model.FromRealmId };
            await _graphCache.InvalidateAsync(affectedRealms, cancellationToken);

            // Publish transit-connection.updated event with changedFields
            await PublishConnectionUpdatedEventAsync(model, changedFields, cancellationToken);

            _logger.LogInformation("Updated connection {ConnectionId}, changed fields: {ChangedFields}",
                body.ConnectionId, string.Join(", ", changedFields));
        }
        else
        {
            _logger.LogDebug("No fields changed for connection {ConnectionId}, skipping update", body.ConnectionId);
        }

        return (StatusCodes.OK, new ConnectionResponse { Connection = MapConnectionToApi(model) });
    }

    /// <summary>
    /// Updates a connection's operational status with optimistic concurrency.
    /// Uses distributed lock for multi-step state transitions. Rejects seasonal_closed
    /// as newStatus unless forceUpdate (worker-only).
    /// </summary>
    /// <param name="body">Request containing connection ID, current/new status, reason, and forceUpdate flag.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>OK with the updated connection, NotFound, BadRequest on mismatch, or Conflict on lock failure.</returns>
    public async Task<(StatusCodes, ConnectionResponse?)> UpdateConnectionStatusAsync(UpdateConnectionStatusRequest body, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.transit", "TransitService.UpdateConnectionStatusAsync");

        _logger.LogDebug("Updating connection status for {ConnectionId}: {NewStatus} (forceUpdate: {ForceUpdate})",
            body.ConnectionId, body.NewStatus, body.ForceUpdate);

        // Distributed lock on connection ID for status transitions per IMPLEMENTATION TENETS
        var lockOwner = $"update-status-{Guid.NewGuid():N}";
        await using var lockResponse = await _lockProvider.LockAsync(
            StateStoreDefinitions.TransitLock,
            $"connection-status:{body.ConnectionId}",
            lockOwner,
            _configuration.LockTimeoutSeconds,
            cancellationToken);

        if (!lockResponse.Success)
        {
            _logger.LogDebug("Could not acquire lock for connection status update {ConnectionId}, returning Conflict",
                body.ConnectionId);
            return (StatusCodes.Conflict, null);
        }

        var key = BuildConnectionKey(body.ConnectionId);
        var (model, etag) = await _connectionStore.GetWithETagAsync(key, cancellationToken);

        if (model == null)
        {
            _logger.LogDebug("Connection not found for status update: {ConnectionId}", body.ConnectionId);
            return (StatusCodes.NotFound, null);
        }

        var previousStatus = model.Status;

        // Optimistic concurrency: check currentStatus matches actual (unless forceUpdate)
        if (!body.ForceUpdate)
        {
            if (body.CurrentStatus.HasValue && body.CurrentStatus.Value != model.Status)
            {
                _logger.LogDebug("Status mismatch for connection {ConnectionId}: expected {ExpectedStatus}, actual {ActualStatus}",
                    body.ConnectionId, body.CurrentStatus.Value, model.Status);
                return (StatusCodes.BadRequest, null);
            }
        }

        // Map the settable status to the full connection status enum
        var newStatus = MapSettableToConnectionStatus(body.NewStatus);

        // Update status fields
        model.Status = newStatus;
        model.StatusReason = body.Reason;
        model.StatusChangedAt = DateTimeOffset.UtcNow;
        model.ModifiedAt = DateTimeOffset.UtcNow;

        var savedEtag = await _connectionStore.TrySaveAsync(key, model, etag, cancellationToken);
        if (savedEtag == null)
        {
            _logger.LogDebug("Concurrent modification detected during status update of connection {ConnectionId}",
                body.ConnectionId);
            return (StatusCodes.Conflict, null);
        }

        // Invalidate graph cache for affected realms
        var affectedRealms = model.CrossRealm
            ? new[] { model.FromRealmId, model.ToRealmId }
            : new[] { model.FromRealmId };
        await _graphCache.InvalidateAsync(affectedRealms, cancellationToken);

        // Publish transit-connection.status-changed event
        await PublishConnectionStatusChangedEventAsync(model, previousStatus, body.ForceUpdate, cancellationToken);

        // Client event via IClientEventPublisher: TransitConnectionStatusChanged
        await PublishConnectionStatusChangedClientEventAsync(model, previousStatus, body.Reason, cancellationToken);

        _logger.LogInformation("Updated connection {ConnectionId} status from {PreviousStatus} to {NewStatus}: {Reason}",
            body.ConnectionId, previousStatus, newStatus, body.Reason);

        return (StatusCodes.OK, new ConnectionResponse { Connection = MapConnectionToApi(model) });
    }

    /// <summary>
    /// Deletes a connection. Rejects if active journeys reference this connection.
    /// Invalidates graph cache and publishes transit-connection.deleted event.
    /// </summary>
    /// <param name="body">Request containing the connection ID to delete.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>OK on successful deletion, NotFound if missing, BadRequest if active journeys exist.</returns>
    public async Task<(StatusCodes, DeleteConnectionResponse?)> DeleteConnectionAsync(DeleteConnectionRequest body, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.transit", "TransitService.DeleteConnectionAsync");

        _logger.LogDebug("Deleting connection {ConnectionId}", body.ConnectionId);

        var key = BuildConnectionKey(body.ConnectionId);
        var model = await _connectionStore.GetAsync(key, cancellationToken: cancellationToken);

        if (model == null)
        {
            _logger.LogDebug("Connection not found for deletion: {ConnectionId}", body.ConnectionId);
            return (StatusCodes.NotFound, null);
        }

        // Check no active journeys reference this connection
        // NOTE: Active journeys live in _journeyStore (Redis, not queryable via LINQ).
        // When journey endpoints are implemented (Phase 6), this check must be revisited
        // to scan active Redis journeys. The archive store only holds completed/abandoned
        // journeys, so this query currently returns 0 for in-progress journeys.
        // Phase 6 should add a helper that scans both stores or maintains a connection->journey index.
        var activeJourneys = await _journeyArchiveStore.QueryAsync(
            j => j.Legs.Any(l => l.ConnectionId == body.ConnectionId) &&
                 (j.Status == JourneyStatus.Preparing || j.Status == JourneyStatus.In_transit ||
                  j.Status == JourneyStatus.At_waypoint || j.Status == JourneyStatus.Interrupted),
            cancellationToken);

        if (activeJourneys.Count > 0)
        {
            _logger.LogDebug("Cannot delete connection {ConnectionId}: {Count} active journeys reference this connection",
                body.ConnectionId, activeJourneys.Count);
            return (StatusCodes.BadRequest, null);
        }

        // Delete from store
        await _connectionStore.DeleteAsync(key, cancellationToken);

        // Invalidate graph cache for affected realms
        var affectedRealms = model.CrossRealm
            ? new[] { model.FromRealmId, model.ToRealmId }
            : new[] { model.FromRealmId };
        await _graphCache.InvalidateAsync(affectedRealms, cancellationToken);

        // Publish transit-connection.deleted event (x-lifecycle)
        await PublishConnectionDeletedEventAsync(model, "deleted_via_api", cancellationToken);

        _logger.LogInformation("Deleted connection {ConnectionId} from {FromLocationId} to {ToLocationId}",
            body.ConnectionId, model.FromLocationId, model.ToLocationId);

        return (StatusCodes.OK, new DeleteConnectionResponse());
    }

    /// <summary>
    /// Bulk seeds connections from configuration. Two-pass: resolve location codes to IDs,
    /// then create each connection with error collection. Uses replaceExisting flag to
    /// optionally update existing connections matched by code.
    /// </summary>
    /// <param name="body">Request containing realm scope, connection entries, and replace flag.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>OK with counts of created, updated, and any errors.</returns>
    public async Task<(StatusCodes, BulkSeedConnectionsResponse?)> BulkSeedConnectionsAsync(BulkSeedConnectionsRequest body, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.transit", "TransitService.BulkSeedConnectionsAsync");

        _logger.LogDebug("Bulk seeding {Count} connections (replaceExisting: {ReplaceExisting})",
            body.Connections.Count, body.ReplaceExisting);

        var created = 0;
        var updated = 0;
        var errors = new List<string>();
        var affectedRealmIds = new HashSet<Guid>();

        // Pass 1: Resolve all unique location codes to IDs
        var locationCodeToId = new Dictionary<string, Guid>();
        var locationCodeToRealmId = new Dictionary<string, Guid>();
        var allLocationCodes = body.Connections
            .SelectMany(c => new[] { c.FromLocationCode, c.ToLocationCode })
            .Distinct()
            .ToList();

        foreach (var code in allLocationCodes)
        {
            try
            {
                var locationResponse = await _locationClient.GetLocationByCodeAsync(
                    new Location.GetLocationByCodeRequest { Code = code },
                    cancellationToken);

                locationCodeToId[code] = locationResponse.LocationId;
                locationCodeToRealmId[code] = locationResponse.RealmId;
            }
            catch (ApiException ex) when (ex.StatusCode == 404)
            {
                _logger.LogDebug("Location code not found during bulk seed: {LocationCode}", code);
                // Don't add error yet -- will be caught per-connection below
            }
        }

        // Pass 2: Create/update each connection
        for (var i = 0; i < body.Connections.Count; i++)
        {
            var entry = body.Connections.ElementAt(i);

            // Validate location codes resolved
            if (!locationCodeToId.TryGetValue(entry.FromLocationCode, out var fromLocationId))
            {
                errors.Add($"Connection [{i}]: fromLocationCode '{entry.FromLocationCode}' not found");
                continue;
            }

            if (!locationCodeToId.TryGetValue(entry.ToLocationCode, out var toLocationId))
            {
                errors.Add($"Connection [{i}]: toLocationCode '{entry.ToLocationCode}' not found");
                continue;
            }

            if (fromLocationId == toLocationId)
            {
                errors.Add($"Connection [{i}]: fromLocationCode and toLocationCode resolve to the same location");
                continue;
            }

            var fromRealmId = locationCodeToRealmId[entry.FromLocationCode];
            var toRealmId = locationCodeToRealmId[entry.ToLocationCode];

            // If realmId filter is specified, skip connections not in the target realm
            if (body.RealmId.HasValue)
            {
                if (fromRealmId != body.RealmId.Value && toRealmId != body.RealmId.Value)
                {
                    errors.Add($"Connection [{i}]: neither endpoint is in realm {body.RealmId.Value}");
                    continue;
                }
            }

            var crossRealm = fromRealmId != toRealmId;

            // Validate all mode codes exist
            var modesValid = true;
            foreach (var modeCode in entry.CompatibleModes)
            {
                var modeKey = BuildModeKey(modeCode);
                var mode = await _modeStore.GetAsync(modeKey, cancellationToken: cancellationToken);
                if (mode == null)
                {
                    errors.Add($"Connection [{i}]: invalid mode code '{modeCode}'");
                    modesValid = false;
                    break;
                }
            }

            if (!modesValid)
            {
                continue;
            }

            // Distributed lock per connection to prevent duplicate creation per IMPLEMENTATION TENETS (multi-instance safety).
            // Lock key uses code if provided (primary uniqueness), otherwise the location pair.
            var bulkLockResource = !string.IsNullOrEmpty(entry.Code)
                ? $"connection:code:{entry.Code}"
                : $"connection:pair:{fromLocationId}:{toLocationId}";
            var bulkLockOwner = $"bulk-seed-connection-{Guid.NewGuid():N}";
            await using var bulkLockResponse = await _lockProvider.LockAsync(
                StateStoreDefinitions.TransitLock,
                bulkLockResource,
                bulkLockOwner,
                _configuration.LockTimeoutSeconds,
                cancellationToken);

            if (!bulkLockResponse.Success)
            {
                errors.Add($"Connection [{i}]: could not acquire lock for connection creation");
                continue;
            }

            // Check for existing connection by code (within lock, for replaceExisting)
            TransitConnectionModel? existingModel = null;
            if (!string.IsNullOrEmpty(entry.Code))
            {
                var existingByCode = await _connectionStore.QueryAsync(
                    c => c.Code == entry.Code,
                    cancellationToken);
                existingModel = existingByCode.FirstOrDefault();
            }

            if (existingModel != null && body.ReplaceExisting)
            {
                // Update existing connection
                var key = BuildConnectionKey(existingModel.Id);
                var (currentModel, etag) = await _connectionStore.GetWithETagAsync(key, cancellationToken);

                if (currentModel != null)
                {
                    currentModel.FromLocationId = fromLocationId;
                    currentModel.ToLocationId = toLocationId;
                    currentModel.Bidirectional = entry.Bidirectional;
                    currentModel.DistanceKm = entry.DistanceKm;
                    currentModel.TerrainType = entry.TerrainType;
                    currentModel.CompatibleModes = entry.CompatibleModes.ToList();
                    currentModel.SeasonalAvailability = entry.SeasonalAvailability?.Select(s => new SeasonalAvailabilityModel
                    {
                        Season = s.Season,
                        Available = s.Available
                    }).ToList();
                    currentModel.BaseRiskLevel = entry.BaseRiskLevel;
                    currentModel.Name = entry.Name;
                    currentModel.Tags = entry.Tags?.ToList();
                    currentModel.FromRealmId = fromRealmId;
                    currentModel.ToRealmId = toRealmId;
                    currentModel.CrossRealm = crossRealm;
                    currentModel.ModifiedAt = DateTimeOffset.UtcNow;

                    var savedEtag = await _connectionStore.TrySaveAsync(key, currentModel, etag, cancellationToken);
                    if (savedEtag != null)
                    {
                        updated++;
                        affectedRealmIds.Add(fromRealmId);
                        if (crossRealm) affectedRealmIds.Add(toRealmId);

                        await PublishConnectionUpdatedEventAsync(currentModel,
                            new[] { "fromLocationId", "toLocationId", "bidirectional", "distanceKm", "terrainType",
                                    "compatibleModes", "seasonalAvailability", "baseRiskLevel", "name", "tags" },
                            cancellationToken);
                    }
                    else
                    {
                        errors.Add($"Connection [{i}]: concurrent modification on existing connection with code '{entry.Code}'");
                    }
                }
            }
            else if (existingModel != null && !body.ReplaceExisting)
            {
                // Skip existing connection (not an error -- idempotent seeding)
                _logger.LogDebug("Skipping existing connection with code {Code} (replaceExisting=false)", entry.Code);
            }
            else
            {
                // Check for duplicate connection by location pair (within lock)
                var existingByPair = await _connectionStore.QueryAsync(
                    c => c.FromLocationId == fromLocationId && c.ToLocationId == toLocationId,
                    cancellationToken);

                if (existingByPair.Count > 0)
                {
                    if (body.ReplaceExisting)
                    {
                        // Location pair already exists but different code or no code -- skip to avoid confusion
                        errors.Add($"Connection [{i}]: connection already exists between locations (different code)");
                    }
                    // If not replacing, silently skip (idempotent)
                    continue;
                }

                // Create new connection
                var now = DateTimeOffset.UtcNow;
                var connectionId = Guid.NewGuid();

                var model = new TransitConnectionModel
                {
                    Id = connectionId,
                    FromLocationId = fromLocationId,
                    ToLocationId = toLocationId,
                    Bidirectional = entry.Bidirectional,
                    DistanceKm = entry.DistanceKm,
                    TerrainType = entry.TerrainType,
                    CompatibleModes = entry.CompatibleModes.ToList(),
                    SeasonalAvailability = entry.SeasonalAvailability?.Select(s => new SeasonalAvailabilityModel
                    {
                        Season = s.Season,
                        Available = s.Available
                    }).ToList(),
                    BaseRiskLevel = entry.BaseRiskLevel,
                    RiskDescription = null,
                    Status = ConnectionStatus.Open,
                    StatusReason = null,
                    StatusChangedAt = now,
                    Discoverable = false,
                    Name = entry.Name,
                    Code = entry.Code,
                    Tags = entry.Tags?.ToList(),
                    FromRealmId = fromRealmId,
                    ToRealmId = toRealmId,
                    CrossRealm = crossRealm,
                    CreatedAt = now,
                    ModifiedAt = now
                };

                var key = BuildConnectionKey(connectionId);
                await _connectionStore.SaveAsync(key, model, cancellationToken: cancellationToken);

                created++;
                affectedRealmIds.Add(fromRealmId);
                if (crossRealm) affectedRealmIds.Add(toRealmId);

                await PublishConnectionCreatedEventAsync(model, cancellationToken);
            }
        }

        // Invalidate graph cache for all affected realms in one batch
        if (affectedRealmIds.Count > 0)
        {
            await _graphCache.InvalidateAsync(affectedRealmIds, cancellationToken);
        }

        _logger.LogInformation("Bulk seed completed: {Created} created, {Updated} updated, {ErrorCount} errors",
            created, updated, errors.Count);

        return (StatusCodes.OK, new BulkSeedConnectionsResponse
        {
            Created = created,
            Updated = updated,
            Errors = errors
        });
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
    /// Calculates optimal route options between two locations.
    /// Pure computation endpoint -- no state mutation. Uses Dijkstra's algorithm over the
    /// cached connection graph with configurable cost functions (fastest, safest, shortest).
    /// </summary>
    /// <param name="body">Request containing origin/destination locations and calculation preferences.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>OK with ranked route options, or BadRequest if locations not found.</returns>
    public async Task<(StatusCodes, CalculateRouteResponse?)> CalculateRouteAsync(CalculateRouteRequest body, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.transit", "TransitService.CalculateRouteAsync");

        _logger.LogDebug("Calculating route from {FromLocationId} to {ToLocationId}, sortBy: {SortBy}, multiModal: {MultiModal}",
            body.FromLocationId, body.ToLocationId, body.SortBy, body.PreferMultiModal);

        // Validate origin and destination locations exist
        Location.LocationResponse fromLocation;
        Location.LocationResponse toLocation;

        try
        {
            fromLocation = await _locationClient.GetLocationAsync(
                new Location.GetLocationRequest { LocationId = body.FromLocationId },
                cancellationToken);
        }
        catch (ApiException ex) when (ex.StatusCode == 404)
        {
            _logger.LogDebug("From location not found: {FromLocationId}", body.FromLocationId);
            return (StatusCodes.BadRequest, null);
        }

        try
        {
            toLocation = await _locationClient.GetLocationAsync(
                new Location.GetLocationRequest { LocationId = body.ToLocationId },
                cancellationToken);
        }
        catch (ApiException ex) when (ex.StatusCode == 404)
        {
            _logger.LogDebug("To location not found: {ToLocationId}", body.ToLocationId);
            return (StatusCodes.BadRequest, null);
        }

        // Resolve current time ratio for real-minutes estimation
        // Use the origin location's realm for the time ratio
        decimal currentTimeRatio = 0;
        try
        {
            var snapshot = await _worldstateClient.GetRealmTimeAsync(
                new Worldstate.GetRealmTimeRequest { RealmId = fromLocation.RealmId },
                cancellationToken);
            currentTimeRatio = (decimal)snapshot.TimeRatio;
        }
        catch (ApiException ex)
        {
            // Non-fatal: if Worldstate is unavailable, we can still calculate routes
            // but totalRealMinutes will be 0
            _logger.LogWarning(ex, "Could not retrieve time ratio for realm {RealmId}, real-minutes estimation will be unavailable",
                fromLocation.RealmId);
        }

        // Build the route calculation request for the calculator
        var calcRequest = new RouteCalculationRequest(
            OriginLocationId: body.FromLocationId,
            DestinationLocationId: body.ToLocationId,
            ModeCode: body.ModeCode,
            PreferMultiModal: body.PreferMultiModal,
            SortBy: body.SortBy ?? RouteSortBy.Fastest,
            EntityId: body.EntityId,
            IncludeSeasonalClosed: body.IncludeSeasonalClosed,
            CargoWeightKg: 0m,
            MaxLegs: body.MaxLegs ?? _configuration.MaxRouteCalculationLegs,
            MaxOptions: body.MaxOptions ?? _configuration.MaxRouteOptions,
            CurrentTimeRatio: currentTimeRatio);

        // Invoke the route calculator (stateless computation)
        var results = await _routeCalculator.CalculateAsync(calcRequest, cancellationToken);

        // Map results to API response model
        var options = results.Select((r, index) => new TransitRouteOption
        {
            Waypoints = r.Waypoints,
            Connections = r.Connections,
            LegCount = r.Connections.Count,
            PrimaryModeCode = r.LegModes.Count > 0
                ? r.LegModes.GroupBy(m => m).OrderByDescending(g => g.Count()).First().Key
                : "walking",
            LegModes = r.LegModes,
            TotalDistanceKm = r.TotalDistanceKm,
            TotalGameHours = r.TotalGameHours,
            TotalRealMinutes = r.TotalRealMinutes,
            AverageRisk = r.AverageRisk,
            MaxLegRisk = r.MaxLegRisk,
            AllLegsOpen = r.AllLegsOpen,
            SeasonalWarnings = r.SeasonalWarnings?.Select(w => new SeasonalRouteWarning
            {
                ConnectionId = w.ConnectionId,
                ConnectionName = w.ConnectionName,
                LegIndex = w.LegIndex,
                CurrentSeason = w.CurrentSeason,
                ClosingSeason = w.ClosingSeason,
                ClosingSeasonIndex = w.ClosingSeasonIndex
            }).ToList(),
            Rank = index + 1
        }).ToList();

        _logger.LogInformation("Route calculation returned {OptionCount} options from {FromLocationId} to {ToLocationId}",
            options.Count, body.FromLocationId, body.ToLocationId);

        return (StatusCodes.OK, new CalculateRouteResponse { Options = options });
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

    #endregion

    #region Connection Helpers

    /// <summary>
    /// Maps an internal <see cref="TransitConnectionModel"/> to the generated <see cref="TransitConnection"/> API model.
    /// </summary>
    /// <param name="model">The internal connection storage model.</param>
    /// <returns>The API-facing connection model.</returns>
    private static TransitConnection MapConnectionToApi(TransitConnectionModel model)
    {
        return new TransitConnection
        {
            Id = model.Id,
            FromLocationId = model.FromLocationId,
            ToLocationId = model.ToLocationId,
            Bidirectional = model.Bidirectional,
            DistanceKm = model.DistanceKm,
            TerrainType = model.TerrainType,
            CompatibleModes = model.CompatibleModes.ToList(),
            SeasonalAvailability = model.SeasonalAvailability?.Select(s => new SeasonalAvailabilityEntry
            {
                Season = s.Season,
                Available = s.Available
            }).ToList(),
            BaseRiskLevel = model.BaseRiskLevel,
            RiskDescription = model.RiskDescription,
            Status = model.Status,
            StatusReason = model.StatusReason,
            StatusChangedAt = model.StatusChangedAt,
            Discoverable = model.Discoverable,
            Name = model.Name,
            Code = model.Code,
            Tags = model.Tags?.ToList(),
            FromRealmId = model.FromRealmId,
            ToRealmId = model.ToRealmId,
            CrossRealm = model.CrossRealm,
            CreatedAt = model.CreatedAt,
            ModifiedAt = model.ModifiedAt
        };
    }

    /// <summary>
    /// Validates that seasonal availability season keys match valid season codes
    /// from the Worldstate calendar system. For cross-realm connections, validates
    /// against both realms' calendars.
    /// </summary>
    /// <remarks>
    /// This validation uses a best-effort approach: it queries Worldstate's calendar
    /// templates for the affected realms. If calendars are not yet configured, validation
    /// passes with a warning (seasonal connections may be created before calendars).
    /// Full calendar-aware enforcement happens at journey creation time and via the
    /// seasonal connection worker (Phase 8).
    /// </remarks>
    /// <param name="seasonalAvailability">The seasonal availability entries to validate.</param>
    /// <param name="fromRealmId">The source realm ID.</param>
    /// <param name="toRealmId">The destination realm ID.</param>
    /// <param name="crossRealm">Whether this is a cross-realm connection.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if validation passes, false if invalid season keys detected.</returns>
    private async Task<bool> ValidateSeasonalKeysAsync(
        ICollection<SeasonalAvailabilityEntry> seasonalAvailability,
        Guid fromRealmId,
        Guid toRealmId,
        bool crossRealm,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.transit", "TransitService.ValidateSeasonalKeysAsync");

        // Validate basic format: season codes must be non-empty
        foreach (var entry in seasonalAvailability)
        {
            if (string.IsNullOrWhiteSpace(entry.Season))
            {
                _logger.LogDebug("Invalid seasonal availability: empty season code");
                return false;
            }
        }

        // Check for duplicate season codes
        var seasonCodes = seasonalAvailability.Select(s => s.Season).ToList();
        if (seasonCodes.Distinct().Count() != seasonCodes.Count)
        {
            _logger.LogDebug("Invalid seasonal availability: duplicate season codes");
            return false;
        }

        // Attempt Worldstate calendar validation (best-effort)
        // The calendar lookup requires a gameServiceId which we don't have directly from locations.
        // Phase 8's seasonal worker will enforce calendar-aware validation.
        // For now, we validate format and uniqueness but log a warning about deferred calendar validation.
        _logger.LogDebug("Seasonal availability entries accepted with format validation. Calendar-aware validation deferred to seasonal worker (Phase 8) for realms {FromRealmId}/{ToRealmId}",
            fromRealmId, crossRealm ? toRealmId : fromRealmId);

        await Task.CompletedTask;
        return true;
    }

    /// <summary>
    /// Applies non-null field updates from an <see cref="UpdateConnectionRequest"/> to an existing connection model.
    /// Validates mode codes if compatibleModes is being updated.
    /// Returns the list of field names that were actually changed, or null if validation failed.
    /// </summary>
    /// <param name="model">The connection model to update in place.</param>
    /// <param name="request">The update request containing fields to apply.</param>
    /// <param name="cancellationToken">Cancellation token for async validation.</param>
    /// <returns>List of camelCase field names that were changed, or null if validation failed (caller should return BadRequest).</returns>
    private async Task<List<string>?> ApplyConnectionFieldUpdatesAsync(
        TransitConnectionModel model,
        UpdateConnectionRequest request,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.transit", "TransitService.ApplyConnectionFieldUpdatesAsync");

        var changedFields = new List<string>();

        if (request.DistanceKm.HasValue && request.DistanceKm.Value != model.DistanceKm)
        {
            model.DistanceKm = request.DistanceKm.Value;
            changedFields.Add("distanceKm");
        }

        if (request.TerrainType != null && request.TerrainType != model.TerrainType)
        {
            model.TerrainType = request.TerrainType;
            changedFields.Add("terrainType");
        }

        if (request.CompatibleModes != null)
        {
            // Validate all new mode codes exist
            foreach (var modeCode in request.CompatibleModes)
            {
                var modeKey = BuildModeKey(modeCode);
                var mode = await _modeStore.GetAsync(modeKey, cancellationToken: cancellationToken);
                if (mode == null)
                {
                    _logger.LogDebug("Invalid mode code in compatible modes update: {ModeCode}", modeCode);
                    // Return null to signal validation failure -- caller returns BadRequest
                    return null;
                }
            }

            model.CompatibleModes = request.CompatibleModes.ToList();
            changedFields.Add("compatibleModes");
        }

        if (request.SeasonalAvailability != null)
        {
            model.SeasonalAvailability = request.SeasonalAvailability.Select(s => new SeasonalAvailabilityModel
            {
                Season = s.Season,
                Available = s.Available
            }).ToList();
            changedFields.Add("seasonalAvailability");
        }

        if (request.BaseRiskLevel.HasValue && request.BaseRiskLevel.Value != model.BaseRiskLevel)
        {
            model.BaseRiskLevel = request.BaseRiskLevel.Value;
            changedFields.Add("baseRiskLevel");
        }

        if (request.RiskDescription != null && request.RiskDescription != model.RiskDescription)
        {
            model.RiskDescription = request.RiskDescription;
            changedFields.Add("riskDescription");
        }

        if (request.Discoverable.HasValue && request.Discoverable.Value != model.Discoverable)
        {
            model.Discoverable = request.Discoverable.Value;
            changedFields.Add("discoverable");
        }

        if (request.Name != null && request.Name != model.Name)
        {
            model.Name = request.Name;
            changedFields.Add("name");
        }

        if (request.Code != null && request.Code != model.Code)
        {
            // Check uniqueness of new code
            if (!string.IsNullOrEmpty(request.Code))
            {
                var existingByCode = await _connectionStore.QueryAsync(
                    c => c.Code == request.Code && c.Id != model.Id,
                    cancellationToken);
                if (existingByCode.Count > 0)
                {
                    _logger.LogDebug("Cannot update connection code: code {Code} already in use", request.Code);
                    // Return null to signal validation failure -- caller returns Conflict
                    return null;
                }
            }

            model.Code = request.Code;
            changedFields.Add("code");
        }

        if (request.Tags != null)
        {
            model.Tags = request.Tags.ToList();
            changedFields.Add("tags");
        }

        return changedFields;
    }

    /// <summary>
    /// Maps a <see cref="SettableConnectionStatus"/> (API enum without seasonal_closed)
    /// to the full <see cref="ConnectionStatus"/> enum.
    /// </summary>
    /// <param name="settableStatus">The settable status from the API request.</param>
    /// <returns>The corresponding full connection status.</returns>
    private static ConnectionStatus MapSettableToConnectionStatus(SettableConnectionStatus settableStatus)
    {
        return settableStatus switch
        {
            SettableConnectionStatus.Open => ConnectionStatus.Open,
            SettableConnectionStatus.Closed => ConnectionStatus.Closed,
            SettableConnectionStatus.Dangerous => ConnectionStatus.Dangerous,
            SettableConnectionStatus.Blocked => ConnectionStatus.Blocked,
            _ => throw new ArgumentOutOfRangeException(nameof(settableStatus), settableStatus, "Unknown settable connection status")
        };
    }

    #endregion

    #region Connection Event Publishing

    /// <summary>
    /// Publishes a transit-connection.created lifecycle event with full entity data.
    /// </summary>
    /// <param name="model">The newly created connection model.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    private async Task PublishConnectionCreatedEventAsync(TransitConnectionModel model, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.transit", "TransitService.PublishConnectionCreatedEventAsync");

        var eventModel = new TransitConnectionCreatedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            Id = model.Id,
            FromLocationId = model.FromLocationId,
            ToLocationId = model.ToLocationId,
            Bidirectional = model.Bidirectional,
            DistanceKm = model.DistanceKm,
            TerrainType = model.TerrainType,
            CompatibleModes = model.CompatibleModes.ToList(),
            SeasonalAvailability = model.SeasonalAvailability?.Select(s => new SeasonalAvailabilityEntry
            {
                Season = s.Season,
                Available = s.Available
            }).ToList(),
            BaseRiskLevel = model.BaseRiskLevel,
            RiskDescription = model.RiskDescription,
            Status = model.Status,
            StatusReason = model.StatusReason,
            StatusChangedAt = model.StatusChangedAt,
            Discoverable = model.Discoverable,
            Name = model.Name,
            Code = model.Code,
            Tags = model.Tags?.ToList(),
            FromRealmId = model.FromRealmId,
            ToRealmId = model.ToRealmId,
            CrossRealm = model.CrossRealm,
            CreatedAt = model.CreatedAt,
            ModifiedAt = model.ModifiedAt
        };

        var published = await _messageBus.TryPublishAsync("transit-connection.created", eventModel, cancellationToken: cancellationToken);
        if (published)
        {
            _logger.LogDebug("Published transit-connection.created event for {ConnectionId}", model.Id);
        }
        else
        {
            _logger.LogWarning("Failed to publish transit-connection.created event for {ConnectionId}", model.Id);
        }
    }

    /// <summary>
    /// Publishes a transit-connection.updated lifecycle event with current state and changed fields.
    /// </summary>
    /// <param name="model">The updated connection model.</param>
    /// <param name="changedFields">List of field names that changed.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    private async Task PublishConnectionUpdatedEventAsync(
        TransitConnectionModel model,
        IEnumerable<string> changedFields,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.transit", "TransitService.PublishConnectionUpdatedEventAsync");

        var changedFieldsList = changedFields.ToList();
        var eventModel = new TransitConnectionUpdatedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            Id = model.Id,
            FromLocationId = model.FromLocationId,
            ToLocationId = model.ToLocationId,
            Bidirectional = model.Bidirectional,
            DistanceKm = model.DistanceKm,
            TerrainType = model.TerrainType,
            CompatibleModes = model.CompatibleModes.ToList(),
            SeasonalAvailability = model.SeasonalAvailability?.Select(s => new SeasonalAvailabilityEntry
            {
                Season = s.Season,
                Available = s.Available
            }).ToList(),
            BaseRiskLevel = model.BaseRiskLevel,
            RiskDescription = model.RiskDescription,
            Status = model.Status,
            StatusReason = model.StatusReason,
            StatusChangedAt = model.StatusChangedAt,
            Discoverable = model.Discoverable,
            Name = model.Name,
            Code = model.Code,
            Tags = model.Tags?.ToList(),
            FromRealmId = model.FromRealmId,
            ToRealmId = model.ToRealmId,
            CrossRealm = model.CrossRealm,
            CreatedAt = model.CreatedAt,
            ModifiedAt = model.ModifiedAt,
            ChangedFields = changedFieldsList
        };

        var published = await _messageBus.TryPublishAsync("transit-connection.updated", eventModel, cancellationToken: cancellationToken);
        if (published)
        {
            _logger.LogDebug("Published transit-connection.updated event for {ConnectionId} with changed fields: {ChangedFields}",
                model.Id, string.Join(", ", changedFieldsList));
        }
        else
        {
            _logger.LogWarning("Failed to publish transit-connection.updated event for {ConnectionId}", model.Id);
        }
    }

    /// <summary>
    /// Publishes a transit-connection.deleted lifecycle event with full entity data and deletion reason.
    /// </summary>
    /// <param name="model">The deleted connection model (state before deletion).</param>
    /// <param name="deletedReason">Reason for deletion.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    private async Task PublishConnectionDeletedEventAsync(
        TransitConnectionModel model,
        string deletedReason,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.transit", "TransitService.PublishConnectionDeletedEventAsync");

        var eventModel = new TransitConnectionDeletedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            Id = model.Id,
            FromLocationId = model.FromLocationId,
            ToLocationId = model.ToLocationId,
            Bidirectional = model.Bidirectional,
            DistanceKm = model.DistanceKm,
            TerrainType = model.TerrainType,
            CompatibleModes = model.CompatibleModes.ToList(),
            SeasonalAvailability = model.SeasonalAvailability?.Select(s => new SeasonalAvailabilityEntry
            {
                Season = s.Season,
                Available = s.Available
            }).ToList(),
            BaseRiskLevel = model.BaseRiskLevel,
            RiskDescription = model.RiskDescription,
            Status = model.Status,
            StatusReason = model.StatusReason,
            StatusChangedAt = model.StatusChangedAt,
            Discoverable = model.Discoverable,
            Name = model.Name,
            Code = model.Code,
            Tags = model.Tags?.ToList(),
            FromRealmId = model.FromRealmId,
            ToRealmId = model.ToRealmId,
            CrossRealm = model.CrossRealm,
            CreatedAt = model.CreatedAt,
            ModifiedAt = model.ModifiedAt,
            DeletedReason = deletedReason
        };

        var published = await _messageBus.TryPublishAsync("transit-connection.deleted", eventModel, cancellationToken: cancellationToken);
        if (published)
        {
            _logger.LogDebug("Published transit-connection.deleted event for {ConnectionId}", model.Id);
        }
        else
        {
            _logger.LogWarning("Failed to publish transit-connection.deleted event for {ConnectionId}", model.Id);
        }
    }

    /// <summary>
    /// Publishes a transit-connection.status-changed custom event (not lifecycle).
    /// This event is published on any status transition, including seasonal worker updates.
    /// </summary>
    /// <param name="model">The connection after status change.</param>
    /// <param name="previousStatus">The status before the change.</param>
    /// <param name="forceUpdated">Whether this was a force update (e.g., from seasonal worker).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    private async Task PublishConnectionStatusChangedEventAsync(
        TransitConnectionModel model,
        ConnectionStatus previousStatus,
        bool forceUpdated,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.transit", "TransitService.PublishConnectionStatusChangedEventAsync");

        var eventModel = new TransitConnectionStatusChangedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            ConnectionId = model.Id,
            FromLocationId = model.FromLocationId,
            ToLocationId = model.ToLocationId,
            PreviousStatus = previousStatus,
            NewStatus = model.Status,
            // StatusReason is nullable (null when status is "open"), but the event schema requires
            // a non-null string for Reason. Empty string represents "no specific reason" for the transition.
            // Coalesce satisfies the required field contract (will execute when StatusReason is legitimately null).
            Reason = model.StatusReason ?? string.Empty,
            ForceUpdated = forceUpdated,
            FromRealmId = model.FromRealmId,
            ToRealmId = model.ToRealmId,
            CrossRealm = model.CrossRealm
        };

        var published = await _messageBus.TryPublishAsync("transit-connection.status-changed", eventModel, cancellationToken: cancellationToken);
        if (published)
        {
            _logger.LogDebug("Published transit-connection.status-changed event for {ConnectionId}: {PreviousStatus} -> {NewStatus}",
                model.Id, previousStatus, model.Status);
        }
        else
        {
            _logger.LogWarning("Failed to publish transit-connection.status-changed event for {ConnectionId}", model.Id);
        }
    }

    /// <summary>
    /// Publishes a client event for connection status changes to WebSocket sessions
    /// in the affected realm(s) via IEntitySessionRegistry.
    /// </summary>
    /// <remarks>
    /// Uses IEntitySessionRegistry to route client events to all sessions watching
    /// the affected realm(s). Cross-realm connections publish to both realms.
    /// If IEntitySessionRegistry is not available (not yet injected), this is a no-op
    /// with a debug log -- the service bus event provides guaranteed delivery to server-side
    /// consumers regardless.
    /// </remarks>
    /// <param name="model">The connection after status change.</param>
    /// <param name="previousStatus">The status before the change.</param>
    /// <param name="reason">Reason for the status change.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    private async Task PublishConnectionStatusChangedClientEventAsync(
        TransitConnectionModel model,
        ConnectionStatus previousStatus,
        string reason,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.transit", "TransitService.PublishConnectionStatusChangedClientEventAsync");

        // TODO: Inject IEntitySessionRegistry in constructor to enable realm-based client event routing.
        // Connection status changes should broadcast to all sessions watching the affected realm(s).
        // For now, the transit-connection.status-changed service bus event provides guaranteed delivery
        // to server-side consumers. Client-side delivery will be implemented when IEntitySessionRegistry
        // is integrated (requires adding to constructor in future phase).
        _logger.LogDebug("Client event publishing for connection status change deferred -- IEntitySessionRegistry not yet integrated. Connection {ConnectionId}: {PreviousStatus} -> {NewStatus}",
            model.Id, previousStatus, model.Status);

        await Task.CompletedTask;
    }

    #endregion
}
