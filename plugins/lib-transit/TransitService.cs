using BeyondImmersion.Bannou.Core;
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
using BeyondImmersion.BannouService.State;
using BeyondImmersion.BannouService.Worldstate;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TransitClientEvents = BeyondImmersion.Bannou.Transit.ClientEvents;

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
///         ICharacterClient (L2), ISpeciesClient (L2), IInventoryClient (L2), IResourceClient (L1),
///         IEntitySessionRegistry (L1, Connect-hosted)</item>
///   <item>Soft dependencies (DI collection): IEnumerable&lt;ITransitCostModifierProvider&gt; (L4, graceful degradation)</item>
/// </list>
/// </para>
/// <para>
/// <b>IMPLEMENTATION TENETS CHECKLIST:</b>
/// <list type="bullet">
///   <item><b>Type Safety:</b> Internal POCOs MUST use proper C# types (enums, Guids, DateTimeOffset) - never string representations. No Enum.Parse in business logic.</item>
///   <item><b>Configuration:</b> ALL config properties in TransitServiceConfiguration MUST be wired up. No hardcoded magic numbers for tunables.</item>
///   <item><b>Events:</b> ALL meaningful state changes MUST publish typed events, even without current consumers.</item>
///   <item><b>Cache Stores:</b> If state-stores.yaml defines cache stores for this service, implement read-through/write-through caching. Journey index uses a separate List&lt;Guid&gt; typed reference to the transit-journeys store.</item>
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

    // State stores (7 data stores + 1 index store; lock store accessed via _lockProvider with StateStoreDefinitions.TransitLock)
    private readonly IQueryableStateStore<TransitModeModel> _modeStore;
    private readonly IQueryableStateStore<TransitConnectionModel> _connectionStore;
    private readonly IStateStore<TransitJourneyModel> _journeyStore;
    private readonly IStateStore<List<Guid>> _journeyIndexStore;
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
    private readonly IEntitySessionRegistry _entitySessionRegistry;

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
    /// <param name="entitySessionRegistry">Entity-to-session registry for routing client events to entity-bound sessions (L1 hard).</param>
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
        IEntitySessionRegistry entitySessionRegistry,
        ILogger<TransitService> logger,
        ITelemetryProvider telemetryProvider,
        TransitServiceConfiguration configuration,
        IEventConsumer eventConsumer)
    {
        _messageBus = messageBus;
        _lockProvider = lockProvider;

        // Create 7 data stores + 1 index store from StateStoreDefinitions constants (lock store used via _lockProvider)
        _modeStore = stateStoreFactory.GetQueryableStore<TransitModeModel>(StateStoreDefinitions.TransitModes);
        _connectionStore = stateStoreFactory.GetQueryableStore<TransitConnectionModel>(StateStoreDefinitions.TransitConnections);
        _journeyStore = stateStoreFactory.GetStore<TransitJourneyModel>(StateStoreDefinitions.TransitJourneys);
        _journeyIndexStore = stateStoreFactory.GetStore<List<Guid>>(StateStoreDefinitions.TransitJourneys);
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
        _entitySessionRegistry = entitySessionRegistry;

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

        // Publish transit.mode.registered event
        await PublishModeRegisteredEventAsync(model, cancellationToken);

        _logger.LogInformation("Registered transit mode {ModeCode}", body.Code);
        return (StatusCodes.OK, new ModeResponse { Mode = MapModeToApi(model) });
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
            var savedEtag = await _modeStore.TrySaveAsync(key, model, etag ?? string.Empty, cancellationToken);
            if (savedEtag == null)
            {
                _logger.LogDebug("Concurrent modification detected for transit mode {ModeCode}", body.Code);
                return (StatusCodes.Conflict, null);
            }

            // Publish transit.mode.updated event with changedFields
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

        var savedEtag = await _modeStore.TrySaveAsync(key, model, etag ?? string.Empty, cancellationToken);
        if (savedEtag == null)
        {
            _logger.LogDebug("Concurrent modification detected during deprecation of transit mode {ModeCode}", body.Code);
            return (StatusCodes.Conflict, null);
        }

        // Publish transit.mode.updated event with deprecation fields
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

        var savedEtag = await _modeStore.TrySaveAsync(key, model, etag ?? string.Empty, cancellationToken);
        if (savedEtag == null)
        {
            _logger.LogDebug("Concurrent modification detected during undeprecation of transit mode {ModeCode}", body.Code);
            return (StatusCodes.Conflict, null);
        }

        // Publish transit.mode.updated event with deprecation fields cleared
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

        // Check no active journeys use this mode in the MySQL archive store
        var journeysUsingMode = await _journeyArchiveStore.QueryAsync(
            j => j.PrimaryModeCode == body.Code && (j.Status == JourneyStatus.Preparing || j.Status == JourneyStatus.InTransit || j.Status == JourneyStatus.AtWaypoint || j.Status == JourneyStatus.Interrupted),
            cancellationToken);

        if (journeysUsingMode.Count > 0)
        {
            _logger.LogDebug("Cannot delete transit mode {ModeCode}: {Count} active archived journeys use this mode",
                body.Code, journeysUsingMode.Count);
            return (StatusCodes.BadRequest, null);
        }

        // Also scan Redis active journeys via the journey index
        var modeConflictFound = await ScanRedisJourneysForModeConflictAsync(body.Code, cancellationToken);
        if (modeConflictFound)
        {
            _logger.LogDebug("Cannot delete transit mode {ModeCode}: active Redis journeys use this mode", body.Code);
            return (StatusCodes.BadRequest, null);
        }

        // Delete from store (lock ensures no concurrent modification)
        await _modeStore.DeleteAsync(key, cancellationToken);

        // Publish transit.mode.deleted event
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
    /// Publishes a transit.mode.registered event with full entity data.
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

        var published = await _messageBus.TryPublishAsync("transit.mode.registered", eventModel, cancellationToken: cancellationToken);
        if (published)
        {
            _logger.LogDebug("Published transit.mode.registered event for {ModeCode}", model.Code);
        }
        else
        {
            _logger.LogWarning("Failed to publish transit.mode.registered event for {ModeCode}", model.Code);
        }
    }

    /// <summary>
    /// Publishes a transit.mode.updated event with current state and changed fields.
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

        var published = await _messageBus.TryPublishAsync("transit.mode.updated", eventModel, cancellationToken: cancellationToken);
        if (published)
        {
            _logger.LogDebug("Published transit.mode.updated event for {ModeCode} with changed fields: {ChangedFields}",
                model.Code, string.Join(", ", changedFieldsList));
        }
        else
        {
            _logger.LogWarning("Failed to publish transit.mode.updated event for {ModeCode}", model.Code);
        }
    }

    /// <summary>
    /// Publishes a transit.mode.deleted event after mode removal.
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

        var published = await _messageBus.TryPublishAsync("transit.mode.deleted", eventModel, cancellationToken: cancellationToken);
        if (published)
        {
            _logger.LogDebug("Published transit.mode.deleted event for {ModeCode}", code);
        }
        else
        {
            _logger.LogWarning("Failed to publish transit.mode.deleted event for {ModeCode}", code);
        }
    }

    #endregion

    #region Connection Management

    private const string CONNECTION_KEY_PREFIX = "connection:";
    private const string CONNECTION_CODE_KEY_PREFIX = "connection:code:";

    /// <summary>
    /// Builds the state store key for a connection by ID.
    /// </summary>
    internal static string BuildConnectionKey(Guid id) => $"{CONNECTION_KEY_PREFIX}{id}";

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

        // Publish transit.connection.created event (x-lifecycle)
        await PublishConnectionCreatedEventAsync(model, cancellationToken);

        _logger.LogInformation("Created connection {ConnectionId} from {FromLocationId} to {ToLocationId} in realm {FromRealmId}",
            connectionId, body.FromLocationId, body.ToLocationId, fromRealmId);

        return (StatusCodes.OK, new ConnectionResponse { Connection = MapConnectionToApi(model) });
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
            filtered = filtered.Where(c => c.Status != ConnectionStatus.SeasonalClosed);
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
            var savedEtag = await _connectionStore.TrySaveAsync(key, model, etag ?? string.Empty, cancellationToken);
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

            // Publish transit.connection.updated event with changedFields
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

        var savedEtag = await _connectionStore.TrySaveAsync(key, model, etag ?? string.Empty, cancellationToken);
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

        // Publish transit.connection.status-changed event
        await PublishConnectionStatusChangedEventAsync(model, previousStatus, body.ForceUpdate, cancellationToken);

        // Client event via IEntitySessionRegistry: TransitConnectionStatusChanged to realm sessions
        await PublishConnectionStatusChangedClientEventAsync(model, previousStatus, body.Reason, cancellationToken);

        _logger.LogInformation("Updated connection {ConnectionId} status from {PreviousStatus} to {NewStatus}: {Reason}",
            body.ConnectionId, previousStatus, newStatus, body.Reason);

        return (StatusCodes.OK, new ConnectionResponse { Connection = MapConnectionToApi(model) });
    }

    /// <summary>
    /// Deletes a connection. Rejects if active journeys reference this connection.
    /// Invalidates graph cache and publishes transit.connection.deleted event.
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

        // Check no active journeys reference this connection in the MySQL archive store
        var activeJourneys = await _journeyArchiveStore.QueryAsync(
            j => j.Legs.Any(l => l.ConnectionId == body.ConnectionId) &&
                (j.Status == JourneyStatus.Preparing || j.Status == JourneyStatus.InTransit ||
                j.Status == JourneyStatus.AtWaypoint || j.Status == JourneyStatus.Interrupted),
            cancellationToken);

        if (activeJourneys.Count > 0)
        {
            _logger.LogDebug("Cannot delete connection {ConnectionId}: {Count} active archived journeys reference this connection",
                body.ConnectionId, activeJourneys.Count);
            return (StatusCodes.BadRequest, null);
        }

        // Also scan Redis active journeys via the journey index
        var connectionConflictFound = await ScanRedisJourneysForConnectionConflictAsync(body.ConnectionId, cancellationToken);
        if (connectionConflictFound)
        {
            _logger.LogDebug("Cannot delete connection {ConnectionId}: active Redis journeys reference this connection", body.ConnectionId);
            return (StatusCodes.BadRequest, null);
        }

        // Delete from store
        await _connectionStore.DeleteAsync(key, cancellationToken);

        // Invalidate graph cache for affected realms
        var affectedRealms = model.CrossRealm
            ? new[] { model.FromRealmId, model.ToRealmId }
            : new[] { model.FromRealmId };
        await _graphCache.InvalidateAsync(affectedRealms, cancellationToken);

        // Publish transit.connection.deleted event (x-lifecycle)
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

                    var savedEtag = await _connectionStore.TrySaveAsync(key, currentModel, etag ?? string.Empty, cancellationToken);
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

    #region Journey Lifecycle

    private const string JOURNEY_KEY_PREFIX = "journey:";

    /// <summary>
    /// Key for the Redis list tracking all active journey IDs. Used by the
    /// <see cref="JourneyArchivalWorker"/> to enumerate journeys without Redis SCAN.
    /// Stored as a <c>List&lt;Guid&gt;</c> in the transit-journeys store.
    /// </summary>
    internal const string JOURNEY_INDEX_KEY = "journey-index";

    /// <summary>
    /// Builds the state store key for a journey by ID.
    /// </summary>
    internal static string BuildJourneyKey(Guid id) => $"{JOURNEY_KEY_PREFIX}{id}";

    /// <summary>
    /// Builds the archive store key for a journey by ID.
    /// </summary>
    internal static string BuildJourneyArchiveKey(Guid id) => $"archive:{id}";

    /// <summary>
    /// Plans a new journey from origin to destination. Validates locations, mode availability,
    /// calculates route via the route calculator, computes ETA via Worldstate game-time,
    /// and saves the journey in Redis with status "preparing".
    /// </summary>
    /// <param name="body">Journey creation request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Created with the new journey, or error status.</returns>
    public async Task<(StatusCodes, JourneyResponse?)> CreateJourneyAsync(CreateJourneyRequest body, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.transit", "TransitService.CreateJourneyAsync");

        _logger.LogDebug("Creating journey for entity {EntityId} ({EntityType}) from {OriginLocationId} to {DestinationLocationId} via mode {ModeCode}",
            body.EntityId, body.EntityType, body.OriginLocationId, body.DestinationLocationId, body.PrimaryModeCode);

        // Validate origin location exists
        Location.LocationResponse originLocation;
        try
        {
            originLocation = await _locationClient.GetLocationAsync(
                new Location.GetLocationRequest { LocationId = body.OriginLocationId },
                cancellationToken);
        }
        catch (ApiException ex) when (ex.StatusCode == 404)
        {
            _logger.LogDebug("Origin location not found: {OriginLocationId}", body.OriginLocationId);
            return (StatusCodes.BadRequest, null);
        }

        // Validate destination location exists
        Location.LocationResponse destinationLocation;
        try
        {
            destinationLocation = await _locationClient.GetLocationAsync(
                new Location.GetLocationRequest { LocationId = body.DestinationLocationId },
                cancellationToken);
        }
        catch (ApiException ex) when (ex.StatusCode == 404)
        {
            _logger.LogDebug("Destination location not found: {DestinationLocationId}", body.DestinationLocationId);
            return (StatusCodes.BadRequest, null);
        }

        // Validate mode exists and is not deprecated
        var modeKey = BuildModeKey(body.PrimaryModeCode);
        var modeModel = await _modeStore.GetAsync(modeKey, cancellationToken: cancellationToken);
        if (modeModel == null)
        {
            _logger.LogDebug("Mode not found: {ModeCode}", body.PrimaryModeCode);
            return (StatusCodes.BadRequest, null);
        }

        if (modeModel.IsDeprecated)
        {
            _logger.LogDebug("Mode is deprecated: {ModeCode}", body.PrimaryModeCode);
            return (StatusCodes.BadRequest, null);
        }

        // Check mode availability for the entity
        var availabilityResult = await EvaluateModeAvailabilityAsync(
            modeModel, body.EntityId, body.EntityType, null, cancellationToken);
        if (!availabilityResult.Available)
        {
            _logger.LogDebug("Mode {ModeCode} not available for entity {EntityId}: {Reason}",
                body.PrimaryModeCode, body.EntityId, availabilityResult.UnavailableReason);
            return (StatusCodes.BadRequest, null);
        }

        // Get current game-time context for ETA calculation
        decimal currentTimeRatio = 0;
        string? currentSeason = null;
        decimal currentGameTime = 0;
        try
        {
            var snapshot = await _worldstateClient.GetRealmTimeAsync(
                new Worldstate.GetRealmTimeRequest { RealmId = originLocation.RealmId },
                cancellationToken);
            currentTimeRatio = (decimal)snapshot.TimeRatio;
            currentSeason = snapshot.Season;
            currentGameTime = snapshot.TotalGameSecondsSinceEpoch / 3600m; // Convert to game-hours
        }
        catch (ApiException ex)
        {
            _logger.LogWarning(ex, "Could not retrieve game time for realm {RealmId}, using defaults for ETA",
                originLocation.RealmId);
        }

        // Calculate route via route calculator
        var calcRequest = new RouteCalculationRequest(
            OriginLocationId: body.OriginLocationId,
            DestinationLocationId: body.DestinationLocationId,
            ModeCode: body.PrimaryModeCode,
            PreferMultiModal: body.PreferMultiModal,
            SortBy: RouteSortBy.Fastest,
            EntityId: body.EntityId,
            IncludeSeasonalClosed: false,
            CargoWeightKg: body.CargoWeightKg,
            MaxLegs: _configuration.MaxRouteCalculationLegs,
            MaxOptions: 1,
            CurrentTimeRatio: currentTimeRatio,
            CurrentSeason: currentSeason);

        var routeResults = await _routeCalculator.CalculateAsync(calcRequest, cancellationToken);

        if (routeResults.Count == 0)
        {
            _logger.LogDebug("No route available from {OriginLocationId} to {DestinationLocationId} via mode {ModeCode}",
                body.OriginLocationId, body.DestinationLocationId, body.PrimaryModeCode);
            return (StatusCodes.BadRequest, null);
        }

        var bestRoute = routeResults[0];

        // Pre-resolve distinct leg modes so multi-modal journeys use correct speed per leg
        var legModeModels = new Dictionary<string, TransitModeModel> { { body.PrimaryModeCode, modeModel } };
        foreach (var lm in bestRoute.LegModes)
        {
            if (!legModeModels.ContainsKey(lm))
            {
                var lmKey = BuildModeKey(lm);
                var lmModel = await _modeStore.GetAsync(lmKey, cancellationToken: cancellationToken);
                if (lmModel != null)
                {
                    legModeModels[lm] = lmModel;
                }
            }
        }

        // Build journey legs from route result
        var legs = new List<TransitJourneyLegModel>();
        for (var i = 0; i < bestRoute.Connections.Count; i++)
        {
            var connectionId = bestRoute.Connections[i];
            var connKey = BuildConnectionKey(connectionId);
            var conn = await _connectionStore.GetAsync(connKey, cancellationToken: cancellationToken);

            // Determine leg mode: use per-leg mode from multi-modal route, or primary mode
            var legMode = i < bestRoute.LegModes.Count ? bestRoute.LegModes[i] : body.PrimaryModeCode;
            var legModeModel = legModeModels.GetValueOrDefault(legMode, modeModel);

            // Compute leg duration using effective speed and terrain modifiers
            var legDuration = ComputeLegDurationGameHours(
                conn?.DistanceKm ?? 0m,
                conn?.TerrainType ?? string.Empty,
                legModeModel,
                body.CargoWeightKg);

            legs.Add(new TransitJourneyLegModel
            {
                ConnectionId = connectionId,
                FromLocationId = bestRoute.Waypoints[i],
                ToLocationId = bestRoute.Waypoints[i + 1],
                ModeCode = legMode,
                DistanceKm = conn?.DistanceKm ?? 0m,
                TerrainType = conn?.TerrainType ?? string.Empty,
                EstimatedDurationGameHours = legDuration,
                WaypointTransferTimeGameHours = conn?.SeasonalAvailability != null ? null : null, // No transfer time computed at planning stage
                Status = JourneyLegStatus.Pending,
                CompletedAtGameTime = null
            });
        }

        // Compute effective speed (with cargo penalty)
        var effectiveSpeed = ComputeEffectiveSpeed(modeModel, body.CargoWeightKg);

        // Compute ETA from total game hours
        var totalGameHours = legs.Sum(l => l.EstimatedDurationGameHours + (l.WaypointTransferTimeGameHours ?? 0m));
        var plannedDepartureGameTime = body.PlannedDepartureGameTime ?? currentGameTime;
        var estimatedArrivalGameTime = plannedDepartureGameTime + totalGameHours;

        var now = DateTimeOffset.UtcNow;
        var journeyId = Guid.NewGuid();

        var journey = new TransitJourneyModel
        {
            Id = journeyId,
            EntityId = body.EntityId,
            EntityType = body.EntityType,
            Legs = legs,
            CurrentLegIndex = 0,
            PrimaryModeCode = bestRoute.PrimaryModeCode,
            EffectiveSpeedKmPerGameHour = effectiveSpeed,
            PlannedDepartureGameTime = plannedDepartureGameTime,
            ActualDepartureGameTime = null,
            EstimatedArrivalGameTime = estimatedArrivalGameTime,
            ActualArrivalGameTime = null,
            OriginLocationId = body.OriginLocationId,
            DestinationLocationId = body.DestinationLocationId,
            CurrentLocationId = body.OriginLocationId,
            Status = JourneyStatus.Preparing,
            StatusReason = null,
            Interruptions = new List<TransitInterruptionModel>(),
            PartySize = body.PartySize,
            CargoWeightKg = body.CargoWeightKg,
            RealmId = originLocation.RealmId,
            CreatedAt = now,
            ModifiedAt = now
        };

        var key = BuildJourneyKey(journeyId);
        await _journeyStore.SaveAsync(key, journey, cancellationToken: cancellationToken);

        // Add to journey index for the archival worker to discover (optimistic concurrency)
        await AddToJourneyIndexAsync(journeyId, cancellationToken);

        _logger.LogInformation("Created journey {JourneyId} for entity {EntityId} from {OriginLocationId} to {DestinationLocationId} with {LegCount} legs, ETA {ETA} game-hours",
            journeyId, body.EntityId, body.OriginLocationId, body.DestinationLocationId, legs.Count, totalGameHours);

        return (StatusCodes.OK, new JourneyResponse { Journey = MapJourneyToApi(journey) });
    }

    /// <summary>
    /// Starts travel on a prepared journey (preparing -> in_transit).
    /// Validates the first connection is open, sets actual departure game-time,
    /// and optionally reports location departure via ILocationClient.
    /// </summary>
    /// <param name="body">Request containing the journey ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>OK with the updated journey, or error status.</returns>
    public async Task<(StatusCodes, JourneyResponse?)> DepartJourneyAsync(DepartJourneyRequest body, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.transit", "TransitService.DepartJourneyAsync");

        _logger.LogDebug("Departing journey {JourneyId}", body.JourneyId);

        // Distributed lock on journey ID for state transition per IMPLEMENTATION TENETS
        var lockOwner = $"depart-journey-{Guid.NewGuid():N}";
        await using var lockResponse = await _lockProvider.LockAsync(
            StateStoreDefinitions.TransitLock,
            $"journey:{body.JourneyId}",
            lockOwner,
            _configuration.LockTimeoutSeconds,
            cancellationToken);

        if (!lockResponse.Success)
        {
            _logger.LogDebug("Could not acquire lock for journey departure {JourneyId}, returning Conflict", body.JourneyId);
            return (StatusCodes.Conflict, null);
        }

        var key = BuildJourneyKey(body.JourneyId);
        var journey = await _journeyStore.GetAsync(key, cancellationToken: cancellationToken);

        if (journey == null)
        {
            _logger.LogDebug("Journey not found: {JourneyId}", body.JourneyId);
            return (StatusCodes.NotFound, null);
        }

        // Validate status == preparing
        if (journey.Status != JourneyStatus.Preparing)
        {
            _logger.LogDebug("Cannot depart journey {JourneyId}: status is {Status}, expected Preparing",
                body.JourneyId, journey.Status);
            return (StatusCodes.BadRequest, null);
        }

        // Check first connection is open
        if (journey.Legs.Count > 0)
        {
            var firstLeg = journey.Legs[0];
            var connKey = BuildConnectionKey(firstLeg.ConnectionId);
            var conn = await _connectionStore.GetAsync(connKey, cancellationToken: cancellationToken);
            if (conn != null && conn.Status != ConnectionStatus.Open && conn.Status != ConnectionStatus.Dangerous)
            {
                _logger.LogDebug("Cannot depart journey {JourneyId}: first connection {ConnectionId} status is {Status}",
                    body.JourneyId, firstLeg.ConnectionId, conn.Status);
                return (StatusCodes.BadRequest, null);
            }
        }

        // Get current game-time for actual departure
        decimal actualDepartureGameTime = journey.PlannedDepartureGameTime;
        Guid? originRealmId = null;
        try
        {
            var originLocation = await _locationClient.GetLocationAsync(
                new Location.GetLocationRequest { LocationId = journey.OriginLocationId },
                cancellationToken);
            originRealmId = originLocation.RealmId;

            var snapshot = await _worldstateClient.GetRealmTimeAsync(
                new Worldstate.GetRealmTimeRequest { RealmId = originLocation.RealmId },
                cancellationToken);
            actualDepartureGameTime = snapshot.TotalGameSecondsSinceEpoch / 3600m;
        }
        catch (ApiException ex)
        {
            _logger.LogWarning(ex, "Could not retrieve game time for journey departure {JourneyId}, using planned time", body.JourneyId);
        }

        // Transition to in_transit
        journey.Status = JourneyStatus.InTransit;
        journey.ActualDepartureGameTime = actualDepartureGameTime;
        if (journey.Legs.Count > 0)
        {
            journey.Legs[0].Status = JourneyLegStatus.InProgress;
        }
        journey.ModifiedAt = DateTimeOffset.UtcNow;

        await _journeyStore.SaveAsync(key, journey, cancellationToken: cancellationToken);

        // Report location departure if configured
        await TryReportEntityPositionAsync(journey.EntityType, journey.EntityId, journey.OriginLocationId, null, cancellationToken);

        // Resolve destination realm for event (nullable per IMPLEMENTATION TENETS -- no sentinel values)
        Guid? destinationRealmId = null;
        try
        {
            var destLocation = await _locationClient.GetLocationAsync(
                new Location.GetLocationRequest { LocationId = journey.DestinationLocationId },
                cancellationToken);
            destinationRealmId = destLocation.RealmId;
        }
        catch (ApiException)
        {
            // Non-fatal for event publishing; null will be coalesced in event publisher
        }

        // Publish transit.journey.departed event
        await PublishJourneyDepartedEventAsync(journey, originRealmId, destinationRealmId, cancellationToken);

        // Publish client event
        await PublishJourneyUpdatedClientEventAsync(journey, cancellationToken);

        _logger.LogInformation("Journey {JourneyId} departed for entity {EntityId}", body.JourneyId, journey.EntityId);
        return (StatusCodes.OK, new JourneyResponse { Journey = MapJourneyToApi(journey) });
    }

    /// <summary>
    /// Resumes an interrupted journey (interrupted -> in_transit).
    /// Validates the current connection is open before allowing resumption.
    /// </summary>
    /// <param name="body">Request containing the journey ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>OK with the updated journey, or error status.</returns>
    public async Task<(StatusCodes, JourneyResponse?)> ResumeJourneyAsync(ResumeJourneyRequest body, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.transit", "TransitService.ResumeJourneyAsync");

        _logger.LogDebug("Resuming journey {JourneyId}", body.JourneyId);

        // Distributed lock on journey ID
        var lockOwner = $"resume-journey-{Guid.NewGuid():N}";
        await using var lockResponse = await _lockProvider.LockAsync(
            StateStoreDefinitions.TransitLock,
            $"journey:{body.JourneyId}",
            lockOwner,
            _configuration.LockTimeoutSeconds,
            cancellationToken);

        if (!lockResponse.Success)
        {
            _logger.LogDebug("Could not acquire lock for journey resume {JourneyId}, returning Conflict", body.JourneyId);
            return (StatusCodes.Conflict, null);
        }

        var key = BuildJourneyKey(body.JourneyId);
        var journey = await _journeyStore.GetAsync(key, cancellationToken: cancellationToken);

        if (journey == null)
        {
            _logger.LogDebug("Journey not found: {JourneyId}", body.JourneyId);
            return (StatusCodes.NotFound, null);
        }

        // Validate status == interrupted
        if (journey.Status != JourneyStatus.Interrupted)
        {
            _logger.LogDebug("Cannot resume journey {JourneyId}: status is {Status}, expected Interrupted",
                body.JourneyId, journey.Status);
            return (StatusCodes.BadRequest, null);
        }

        // Check current connection is open
        if (journey.CurrentLegIndex < journey.Legs.Count)
        {
            var currentLeg = journey.Legs[journey.CurrentLegIndex];
            var connKey = BuildConnectionKey(currentLeg.ConnectionId);
            var conn = await _connectionStore.GetAsync(connKey, cancellationToken: cancellationToken);
            if (conn != null && conn.Status != ConnectionStatus.Open && conn.Status != ConnectionStatus.Dangerous)
            {
                _logger.LogDebug("Cannot resume journey {JourneyId}: current connection {ConnectionId} status is {Status}",
                    body.JourneyId, currentLeg.ConnectionId, conn.Status);
                return (StatusCodes.BadRequest, null);
            }
        }

        // Mark unresolved interruptions as resolved
        foreach (var interruption in journey.Interruptions.Where(i => !i.Resolved))
        {
            interruption.Resolved = true;
        }

        // Transition to in_transit
        journey.Status = JourneyStatus.InTransit;
        journey.StatusReason = null;
        if (journey.CurrentLegIndex < journey.Legs.Count)
        {
            journey.Legs[journey.CurrentLegIndex].Status = JourneyLegStatus.InProgress;
        }
        journey.ModifiedAt = DateTimeOffset.UtcNow;

        await _journeyStore.SaveAsync(key, journey, cancellationToken: cancellationToken);

        // Resolve realm for event (nullable per IMPLEMENTATION TENETS -- no sentinel values)
        var realmId = await ResolveLocationRealmIdAsync(journey.CurrentLocationId, cancellationToken);

        // Publish transit.journey.resumed event
        await PublishJourneyResumedEventAsync(journey, realmId, cancellationToken);

        // Publish client event
        await PublishJourneyUpdatedClientEventAsync(journey, cancellationToken);

        _logger.LogInformation("Journey {JourneyId} resumed for entity {EntityId}", body.JourneyId, journey.EntityId);
        return (StatusCodes.OK, new JourneyResponse { Journey = MapJourneyToApi(journey) });
    }

    /// <summary>
    /// Advances a journey to the next waypoint or final destination.
    /// Completes the current leg, auto-reveals discoverable connections at the waypoint,
    /// reports position via ILocationClient, and transitions to at_waypoint or arrived.
    /// </summary>
    /// <param name="body">Request containing the journey ID, arrival game-time, and optional incidents.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>OK with the updated journey, or error status.</returns>
    public async Task<(StatusCodes, JourneyResponse?)> AdvanceJourneyAsync(AdvanceJourneyRequest body, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.transit", "TransitService.AdvanceJourneyAsync");

        _logger.LogDebug("Advancing journey {JourneyId} with arrival game-time {ArrivedAtGameTime}",
            body.JourneyId, body.ArrivedAtGameTime);

        // Distributed lock on journey ID
        var lockOwner = $"advance-journey-{Guid.NewGuid():N}";
        await using var lockResponse = await _lockProvider.LockAsync(
            StateStoreDefinitions.TransitLock,
            $"journey:{body.JourneyId}",
            lockOwner,
            _configuration.LockTimeoutSeconds,
            cancellationToken);

        if (!lockResponse.Success)
        {
            _logger.LogDebug("Could not acquire lock for journey advance {JourneyId}, returning Conflict", body.JourneyId);
            return (StatusCodes.Conflict, null);
        }

        var key = BuildJourneyKey(body.JourneyId);
        var journey = await _journeyStore.GetAsync(key, cancellationToken: cancellationToken);

        if (journey == null)
        {
            _logger.LogDebug("Journey not found: {JourneyId}", body.JourneyId);
            return (StatusCodes.NotFound, null);
        }

        // Validate status == in_transit or at_waypoint
        if (journey.Status != JourneyStatus.InTransit && journey.Status != JourneyStatus.AtWaypoint)
        {
            _logger.LogDebug("Cannot advance journey {JourneyId}: status is {Status}, expected InTransit or AtWaypoint",
                body.JourneyId, journey.Status);
            return (StatusCodes.BadRequest, null);
        }

        // If at_waypoint, start the next leg before completing it
        if (journey.Status == JourneyStatus.AtWaypoint && journey.CurrentLegIndex < journey.Legs.Count)
        {
            journey.Legs[journey.CurrentLegIndex].Status = JourneyLegStatus.InProgress;
        }

        // Validate there is a current leg to complete
        if (journey.CurrentLegIndex >= journey.Legs.Count)
        {
            _logger.LogDebug("Cannot advance journey {JourneyId}: no current leg (index {Index} >= {Count})",
                body.JourneyId, journey.CurrentLegIndex, journey.Legs.Count);
            return (StatusCodes.BadRequest, null);
        }

        // Record any incidents as interruption records
        if (body.Incidents != null)
        {
            foreach (var incident in body.Incidents)
            {
                journey.Interruptions.Add(new TransitInterruptionModel
                {
                    LegIndex = journey.CurrentLegIndex,
                    GameTime = body.ArrivedAtGameTime,
                    Reason = incident.Reason,
                    DurationGameHours = incident.DurationGameHours,
                    Resolved = true // Incidents reported during advance are already resolved
                });
            }
        }

        // Complete the current leg
        var completedLeg = journey.Legs[journey.CurrentLegIndex];
        completedLeg.Status = JourneyLegStatus.Completed;
        completedLeg.CompletedAtGameTime = body.ArrivedAtGameTime;

        // Auto-reveal discoverable connections at the waypoint
        await TryAutoRevealDiscoveryAsync(journey.EntityId, completedLeg.ConnectionId, cancellationToken);

        // Update current location to the leg's destination
        journey.CurrentLocationId = completedLeg.ToLocationId;

        // Report position via ILocationClient
        await TryReportEntityPositionAsync(journey.EntityType, journey.EntityId, completedLeg.ToLocationId, completedLeg.FromLocationId, cancellationToken);

        // Determine if this is the final leg
        var isFinalLeg = journey.CurrentLegIndex >= journey.Legs.Count - 1;

        if (isFinalLeg)
        {
            // Journey arrived at destination
            journey.Status = JourneyStatus.Arrived;
            journey.ActualArrivalGameTime = body.ArrivedAtGameTime;
            journey.ModifiedAt = DateTimeOffset.UtcNow;

            await _journeyStore.SaveAsync(key, journey, cancellationToken: cancellationToken);

            // Resolve realm IDs for event
            var originRealmId = await ResolveLocationRealmIdAsync(journey.OriginLocationId, cancellationToken);
            var destRealmId = await ResolveLocationRealmIdAsync(journey.DestinationLocationId, cancellationToken);

            // Publish transit.journey.arrived event
            await PublishJourneyArrivedEventAsync(journey, originRealmId, destRealmId, cancellationToken);

            // Publish client event
            await PublishJourneyUpdatedClientEventAsync(journey, cancellationToken);

            _logger.LogInformation("Journey {JourneyId} arrived at destination {DestinationLocationId} for entity {EntityId}",
                body.JourneyId, journey.DestinationLocationId, journey.EntityId);
        }
        else
        {
            // Advance to next leg, transition to at_waypoint
            journey.CurrentLegIndex++;
            journey.Status = JourneyStatus.AtWaypoint;
            journey.ModifiedAt = DateTimeOffset.UtcNow;

            await _journeyStore.SaveAsync(key, journey, cancellationToken: cancellationToken);

            // Resolve realm for event
            var waypointRealmId = await ResolveLocationRealmIdAsync(completedLeg.ToLocationId, cancellationToken);
            var nextLeg = journey.Legs[journey.CurrentLegIndex];

            // Determine if a realm boundary was crossed
            var fromRealmId = await ResolveLocationRealmIdAsync(completedLeg.FromLocationId, cancellationToken);
            var crossedRealmBoundary = fromRealmId != waypointRealmId;

            // Publish transit.journey.waypoint-reached event
            await PublishJourneyWaypointReachedEventAsync(
                journey, completedLeg.ToLocationId, nextLeg.ToLocationId,
                journey.CurrentLegIndex - 1, completedLeg.ConnectionId,
                waypointRealmId, crossedRealmBoundary, cancellationToken);

            // Publish client event
            await PublishJourneyUpdatedClientEventAsync(journey, cancellationToken);

            _logger.LogInformation("Journey {JourneyId} reached waypoint {WaypointLocationId}, leg {LegIndex}/{TotalLegs}",
                body.JourneyId, completedLeg.ToLocationId, journey.CurrentLegIndex, journey.Legs.Count);
        }

        return (StatusCodes.OK, new JourneyResponse { Journey = MapJourneyToApi(journey) });
    }

    /// <summary>
    /// Batch advances multiple journeys for NPC-scale efficiency.
    /// Each advance is processed independently -- failure of one does not roll back others.
    /// Ordering within batch preserved for same-journeyId multiple advances.
    /// </summary>
    /// <param name="body">Request containing the list of advances.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>OK with per-journey results.</returns>
    public async Task<(StatusCodes, AdvanceBatchResponse?)> AdvanceBatchJourneysAsync(AdvanceBatchRequest body, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.transit", "TransitService.AdvanceBatchJourneysAsync");

        _logger.LogDebug("Processing batch advance of {Count} journeys", body.Advances.Count);

        var results = new List<BatchAdvanceResult>();

        // Process each advance sequentially to preserve ordering for same-journeyId entries
        foreach (var entry in body.Advances)
        {
            try
            {
                var advanceRequest = new AdvanceJourneyRequest
                {
                    JourneyId = entry.JourneyId,
                    ArrivedAtGameTime = entry.ArrivedAtGameTime,
                    Incidents = entry.Incidents
                };

                var (status, response) = await AdvanceJourneyAsync(advanceRequest, cancellationToken);

                if (response != null)
                {
                    results.Add(new BatchAdvanceResult
                    {
                        JourneyId = entry.JourneyId,
                        Error = null,
                        Journey = response.Journey
                    });
                }
                else
                {
                    results.Add(new BatchAdvanceResult
                    {
                        JourneyId = entry.JourneyId,
                        Error = $"advance_failed:{status}",
                        Journey = null
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Batch advance failed for journey {JourneyId}", entry.JourneyId);
                results.Add(new BatchAdvanceResult
                {
                    JourneyId = entry.JourneyId,
                    Error = "internal_error",
                    Journey = null
                });
            }
        }

        _logger.LogInformation("Batch advance completed: {Succeeded} succeeded, {Failed} failed",
            results.Count(r => r.Error == null), results.Count(r => r.Error != null));

        return (StatusCodes.OK, new AdvanceBatchResponse { Results = results });
    }

    /// <summary>
    /// Force-arrives a journey at its destination, marking remaining legs as skipped.
    /// Used for teleportation, fast-travel, or narrative skip.
    /// </summary>
    /// <param name="body">Request containing the journey ID, arrival game-time, and reason.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>OK with the updated journey, or error status.</returns>
    public async Task<(StatusCodes, JourneyResponse?)> ArriveJourneyAsync(ArriveJourneyRequest body, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.transit", "TransitService.ArriveJourneyAsync");

        _logger.LogDebug("Force-arriving journey {JourneyId}", body.JourneyId);

        // Distributed lock on journey ID
        var lockOwner = $"arrive-journey-{Guid.NewGuid():N}";
        await using var lockResponse = await _lockProvider.LockAsync(
            StateStoreDefinitions.TransitLock,
            $"journey:{body.JourneyId}",
            lockOwner,
            _configuration.LockTimeoutSeconds,
            cancellationToken);

        if (!lockResponse.Success)
        {
            _logger.LogDebug("Could not acquire lock for journey arrive {JourneyId}, returning Conflict", body.JourneyId);
            return (StatusCodes.Conflict, null);
        }

        var key = BuildJourneyKey(body.JourneyId);
        var journey = await _journeyStore.GetAsync(key, cancellationToken: cancellationToken);

        if (journey == null)
        {
            _logger.LogDebug("Journey not found: {JourneyId}", body.JourneyId);
            return (StatusCodes.NotFound, null);
        }

        // Validate status == in_transit or at_waypoint
        if (journey.Status != JourneyStatus.InTransit && journey.Status != JourneyStatus.AtWaypoint)
        {
            _logger.LogDebug("Cannot force-arrive journey {JourneyId}: status is {Status}, expected InTransit or AtWaypoint",
                body.JourneyId, journey.Status);
            return (StatusCodes.BadRequest, null);
        }

        // Mark remaining legs as skipped (current in-progress leg is also skipped since we're force-arriving)
        for (var i = journey.CurrentLegIndex; i < journey.Legs.Count; i++)
        {
            if (journey.Legs[i].Status == JourneyLegStatus.Pending || journey.Legs[i].Status == JourneyLegStatus.InProgress)
            {
                journey.Legs[i].Status = JourneyLegStatus.Skipped;
            }
        }

        // Set final state
        journey.Status = JourneyStatus.Arrived;
        journey.StatusReason = body.Reason;
        journey.ActualArrivalGameTime = body.ArrivedAtGameTime;
        journey.CurrentLocationId = journey.DestinationLocationId;
        journey.ModifiedAt = DateTimeOffset.UtcNow;

        await _journeyStore.SaveAsync(key, journey, cancellationToken: cancellationToken);

        // Report destination position via ILocationClient
        await TryReportEntityPositionAsync(journey.EntityType, journey.EntityId, journey.DestinationLocationId, null, cancellationToken);

        // Resolve realm IDs for event
        var originRealmId = await ResolveLocationRealmIdAsync(journey.OriginLocationId, cancellationToken);
        var destRealmId = await ResolveLocationRealmIdAsync(journey.DestinationLocationId, cancellationToken);

        // Publish transit.journey.arrived event
        await PublishJourneyArrivedEventAsync(journey, originRealmId, destRealmId, cancellationToken);

        // Publish client event
        await PublishJourneyUpdatedClientEventAsync(journey, cancellationToken);

        _logger.LogInformation("Journey {JourneyId} force-arrived at {DestinationLocationId} for entity {EntityId}: {Reason}",
            body.JourneyId, journey.DestinationLocationId, journey.EntityId, body.Reason);

        return (StatusCodes.OK, new JourneyResponse { Journey = MapJourneyToApi(journey) });
    }

    /// <summary>
    /// Interrupts an in-transit journey (in_transit -> interrupted).
    /// Records the interruption with reason, game-time, and leg index.
    /// </summary>
    /// <param name="body">Request containing the journey ID, reason, and game-time.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>OK with the updated journey, or error status.</returns>
    public async Task<(StatusCodes, JourneyResponse?)> InterruptJourneyAsync(InterruptJourneyRequest body, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.transit", "TransitService.InterruptJourneyAsync");

        _logger.LogDebug("Interrupting journey {JourneyId}: {Reason}", body.JourneyId, body.Reason);

        // Distributed lock on journey ID
        var lockOwner = $"interrupt-journey-{Guid.NewGuid():N}";
        await using var lockResponse = await _lockProvider.LockAsync(
            StateStoreDefinitions.TransitLock,
            $"journey:{body.JourneyId}",
            lockOwner,
            _configuration.LockTimeoutSeconds,
            cancellationToken);

        if (!lockResponse.Success)
        {
            _logger.LogDebug("Could not acquire lock for journey interrupt {JourneyId}, returning Conflict", body.JourneyId);
            return (StatusCodes.Conflict, null);
        }

        var key = BuildJourneyKey(body.JourneyId);
        var journey = await _journeyStore.GetAsync(key, cancellationToken: cancellationToken);

        if (journey == null)
        {
            _logger.LogDebug("Journey not found: {JourneyId}", body.JourneyId);
            return (StatusCodes.NotFound, null);
        }

        // Validate status == in_transit
        if (journey.Status != JourneyStatus.InTransit)
        {
            _logger.LogDebug("Cannot interrupt journey {JourneyId}: status is {Status}, expected InTransit",
                body.JourneyId, journey.Status);
            return (StatusCodes.BadRequest, null);
        }

        // Add interruption record
        journey.Interruptions.Add(new TransitInterruptionModel
        {
            LegIndex = journey.CurrentLegIndex,
            GameTime = body.GameTime,
            Reason = body.Reason,
            DurationGameHours = null, // Duration unknown at interruption time; resolved on resume
            Resolved = false
        });

        // Transition to interrupted
        journey.Status = JourneyStatus.Interrupted;
        journey.StatusReason = body.Reason;
        journey.ModifiedAt = DateTimeOffset.UtcNow;

        await _journeyStore.SaveAsync(key, journey, cancellationToken: cancellationToken);

        // Resolve realm for event
        var realmId = await ResolveLocationRealmIdAsync(journey.CurrentLocationId, cancellationToken);

        // Publish transit.journey.interrupted event
        await PublishJourneyInterruptedEventAsync(journey, realmId, cancellationToken);

        // Publish client event
        await PublishJourneyUpdatedClientEventAsync(journey, cancellationToken);

        _logger.LogInformation("Journey {JourneyId} interrupted for entity {EntityId}: {Reason}",
            body.JourneyId, journey.EntityId, body.Reason);

        return (StatusCodes.OK, new JourneyResponse { Journey = MapJourneyToApi(journey) });
    }

    /// <summary>
    /// Abandons a journey. Valid from any active status (preparing, in_transit, at_waypoint, interrupted).
    /// Reports current position via ILocationClient.
    /// </summary>
    /// <param name="body">Request containing the journey ID and reason.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>OK with the updated journey, or error status.</returns>
    public async Task<(StatusCodes, JourneyResponse?)> AbandonJourneyAsync(AbandonJourneyRequest body, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.transit", "TransitService.AbandonJourneyAsync");

        _logger.LogDebug("Abandoning journey {JourneyId}: {Reason}", body.JourneyId, body.Reason);

        // Distributed lock on journey ID
        var lockOwner = $"abandon-journey-{Guid.NewGuid():N}";
        await using var lockResponse = await _lockProvider.LockAsync(
            StateStoreDefinitions.TransitLock,
            $"journey:{body.JourneyId}",
            lockOwner,
            _configuration.LockTimeoutSeconds,
            cancellationToken);

        if (!lockResponse.Success)
        {
            _logger.LogDebug("Could not acquire lock for journey abandon {JourneyId}, returning Conflict", body.JourneyId);
            return (StatusCodes.Conflict, null);
        }

        var key = BuildJourneyKey(body.JourneyId);
        var journey = await _journeyStore.GetAsync(key, cancellationToken: cancellationToken);

        if (journey == null)
        {
            _logger.LogDebug("Journey not found: {JourneyId}", body.JourneyId);
            return (StatusCodes.NotFound, null);
        }

        // Validate status is NOT arrived or abandoned
        if (journey.Status == JourneyStatus.Arrived || journey.Status == JourneyStatus.Abandoned)
        {
            _logger.LogDebug("Cannot abandon journey {JourneyId}: status is {Status}, which is terminal",
                body.JourneyId, journey.Status);
            return (StatusCodes.BadRequest, null);
        }

        // Transition to abandoned
        journey.Status = JourneyStatus.Abandoned;
        journey.StatusReason = body.Reason;
        journey.ModifiedAt = DateTimeOffset.UtcNow;

        await _journeyStore.SaveAsync(key, journey, cancellationToken: cancellationToken);

        // Report current position via ILocationClient
        await TryReportEntityPositionAsync(journey.EntityType, journey.EntityId, journey.CurrentLocationId, null, cancellationToken);

        // Resolve realm IDs for event
        var originRealmId = await ResolveLocationRealmIdAsync(journey.OriginLocationId, cancellationToken);
        var destRealmId = await ResolveLocationRealmIdAsync(journey.DestinationLocationId, cancellationToken);
        var abandonedAtRealmId = await ResolveLocationRealmIdAsync(journey.CurrentLocationId, cancellationToken);

        // Publish transit.journey.abandoned event
        await PublishJourneyAbandonedEventAsync(journey, originRealmId, destRealmId, abandonedAtRealmId, cancellationToken);

        // Publish client event
        await PublishJourneyUpdatedClientEventAsync(journey, cancellationToken);

        _logger.LogInformation("Journey {JourneyId} abandoned for entity {EntityId} at location {CurrentLocationId}: {Reason}",
            body.JourneyId, journey.EntityId, journey.CurrentLocationId, body.Reason);

        return (StatusCodes.OK, new JourneyResponse { Journey = MapJourneyToApi(journey) });
    }

    /// <summary>
    /// Gets a journey by ID. Reads from Redis (active journeys) first, then falls back
    /// to MySQL archive store for completed/abandoned journeys that have been archived.
    /// </summary>
    /// <param name="body">Request containing the journey ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>OK with the journey data, or NotFound.</returns>
    public async Task<(StatusCodes, JourneyResponse?)> GetJourneyAsync(GetJourneyRequest body, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.transit", "TransitService.GetJourneyAsync");

        _logger.LogDebug("Getting journey {JourneyId}", body.JourneyId);

        // Try Redis first (active journeys)
        var key = BuildJourneyKey(body.JourneyId);
        var journey = await _journeyStore.GetAsync(key, cancellationToken: cancellationToken);

        if (journey != null)
        {
            return (StatusCodes.OK, new JourneyResponse { Journey = MapJourneyToApi(journey) });
        }

        // Fallback to MySQL archive
        var archiveKey = BuildJourneyArchiveKey(body.JourneyId);
        var archived = await _journeyArchiveStore.GetAsync(archiveKey, cancellationToken: cancellationToken);

        if (archived != null)
        {
            return (StatusCodes.OK, new JourneyResponse { Journey = MapArchivedJourneyToApi(archived) });
        }

        _logger.LogDebug("Journey not found in active or archive stores: {JourneyId}", body.JourneyId);
        return (StatusCodes.NotFound, null);
    }

    /// <summary>
    /// Queries active journeys whose current leg uses a given connection ID.
    /// Enables "who's on this road?" queries for encounter generation, bandit targeting,
    /// caravan interception, and traffic monitoring.
    /// </summary>
    /// <param name="body">Request containing the connection ID and optional filters.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>OK with matching journeys.</returns>
    public async Task<(StatusCodes, ListJourneysResponse?)> QueryJourneysByConnectionAsync(QueryJourneysByConnectionRequest body, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.transit", "TransitService.QueryJourneysByConnectionAsync");

        _logger.LogDebug("Querying journeys by connection {ConnectionId}", body.ConnectionId);

        // Verify connection exists
        var connKey = BuildConnectionKey(body.ConnectionId);
        var connection = await _connectionStore.GetAsync(connKey, cancellationToken: cancellationToken);
        if (connection == null)
        {
            _logger.LogDebug("Connection not found: {ConnectionId}", body.ConnectionId);
            return (StatusCodes.NotFound, null);
        }

        // Query archive store for journeys referencing this connection.
        // NOTE: Active journeys live in Redis (not queryable via LINQ). The archive store
        // provides supplementary queryable access. The archival worker moves completed/abandoned
        // journeys from Redis to archive, so active journeys remain in Redis only.
        // This endpoint returns archived journeys matching the connection; active journey lookup
        // by connection requires the game server to track journey-connection associations.
        var filterConnectionId = body.ConnectionId;
        var filterStatus = body.Status;

        var archiveMatches = await _journeyArchiveStore.QueryAsync(
            j => j.Legs.Any(l => l.ConnectionId == filterConnectionId) &&
                (!filterStatus.HasValue || j.Status == filterStatus.Value),
            cancellationToken);

        var journeys = archiveMatches
            .Select(MapArchivedJourneyToApi)
            .ToList();

        // Apply pagination
        var totalCount = journeys.Count;
        var paged = journeys
            .Skip((body.Page - 1) * body.PageSize)
            .Take(body.PageSize)
            .ToList();

        return (StatusCodes.OK, new ListJourneysResponse
        {
            Journeys = paged,
            TotalCount = totalCount
        });
    }

    /// <summary>
    /// Lists journeys filtered by entity, realm, status, and active-only flag.
    /// Queries active journeys from Redis and optionally includes archived journeys.
    /// </summary>
    /// <param name="body">Request containing filter criteria.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>OK with matching journeys.</returns>
    public async Task<(StatusCodes, ListJourneysResponse?)> ListJourneysAsync(ListJourneysRequest body, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.transit", "TransitService.ListJourneysAsync");

        _logger.LogDebug("Listing journeys - EntityId: {EntityId}, EntityType: {EntityType}, RealmId: {RealmId}, Status: {Status}, ActiveOnly: {ActiveOnly}",
            body.EntityId, body.EntityType, body.RealmId, body.Status, body.ActiveOnly);

        // Query from archive store with composed LINQ filters (pushed to SQL via IQueryableStateStore).
        // Active journeys are primarily in Redis, but the archive store provides queryable access
        // for non-active journeys. For active journeys, the game server typically tracks them
        // by journey ID. This endpoint provides a secondary query path.
        //
        // Note: RealmId and CrossRealm filters are not yet supported because the archive model
        // does not store realm IDs directly. This will be resolved when realm IDs are added
        // to the archive model.

        // Capture filter values outside the expression to avoid closure issues
        var filterEntityId = body.EntityId;
        var filterEntityType = body.EntityType;
        var filterStatus = body.Status;
        var filterActiveOnly = body.ActiveOnly;

        var results = await _journeyArchiveStore.QueryAsync(j =>
            (!filterEntityId.HasValue || j.EntityId == filterEntityId.Value) &&
            (string.IsNullOrEmpty(filterEntityType) || j.EntityType == filterEntityType) &&
            (!filterStatus.HasValue || j.Status == filterStatus.Value) &&
            (!filterActiveOnly || (j.Status != JourneyStatus.Arrived && j.Status != JourneyStatus.Abandoned)),
            cancellationToken);

        var journeys = results.Select(MapArchivedJourneyToApi).ToList();

        // Pagination
        var totalCount = journeys.Count;
        var paged = journeys
            .Skip((body.Page - 1) * body.PageSize)
            .Take(body.PageSize)
            .ToList();

        return (StatusCodes.OK, new ListJourneysResponse
        {
            Journeys = paged,
            TotalCount = totalCount
        });
    }

    /// <summary>
    /// Queries archived journeys from the MySQL archive store with date range, entity, and mode filters.
    /// Used by Trade (velocity calculations), Analytics (travel patterns), and Character History.
    /// </summary>
    /// <param name="body">Request containing filter criteria and pagination.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>OK with matching archived journeys.</returns>
    public async Task<(StatusCodes, ListJourneysResponse?)> QueryJourneyArchiveAsync(QueryJourneyArchiveRequest body, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.transit", "TransitService.QueryJourneyArchiveAsync");

        _logger.LogDebug("Querying journey archive - EntityId: {EntityId}, ModeCode: {ModeCode}, FromGameTime: {From}, ToGameTime: {To}",
            body.EntityId, body.ModeCode, body.FromGameTime, body.ToGameTime);

        // Capture filter values outside the expression to avoid closure issues
        var filterEntityId = body.EntityId;
        var filterEntityType = body.EntityType;
        var filterOriginLocationId = body.OriginLocationId;
        var filterDestLocationId = body.DestinationLocationId;
        var filterModeCode = body.ModeCode;
        var filterStatus = body.Status;
        var filterFromGameTime = body.FromGameTime;
        var filterToGameTime = body.ToGameTime;

        // Compose all filters into a single LINQ expression (pushed to SQL via IQueryableStateStore)
        var results = await _journeyArchiveStore.QueryAsync(j =>
            (!filterEntityId.HasValue || j.EntityId == filterEntityId.Value) &&
            (string.IsNullOrEmpty(filterEntityType) || j.EntityType == filterEntityType) &&
            (!filterOriginLocationId.HasValue || j.OriginLocationId == filterOriginLocationId.Value) &&
            (!filterDestLocationId.HasValue || j.DestinationLocationId == filterDestLocationId.Value) &&
            (string.IsNullOrEmpty(filterModeCode) || j.PrimaryModeCode == filterModeCode) &&
            (!filterStatus.HasValue || j.Status == filterStatus.Value) &&
            (!filterFromGameTime.HasValue || j.PlannedDepartureGameTime >= filterFromGameTime.Value) &&
            (!filterToGameTime.HasValue || j.PlannedDepartureGameTime <= filterToGameTime.Value),
            cancellationToken);

        var journeys = results.Select(MapArchivedJourneyToApi).ToList();

        // Pagination
        var totalCount = journeys.Count;
        var paged = journeys
            .Skip((body.Page - 1) * body.PageSize)
            .Take(body.PageSize)
            .ToList();

        return (StatusCodes.OK, new ListJourneysResponse
        {
            Journeys = paged,
            TotalCount = totalCount
        });
    }

    #endregion

    #region Journey Helpers

    /// <summary>
    /// Maps an internal <see cref="TransitJourneyModel"/> to the generated <see cref="TransitJourney"/> API model.
    /// </summary>
    private static TransitJourney MapJourneyToApi(TransitJourneyModel model)
    {
        return new TransitJourney
        {
            Id = model.Id,
            EntityId = model.EntityId,
            EntityType = model.EntityType,
            Legs = model.Legs.Select(l => new TransitJourneyLeg
            {
                ConnectionId = l.ConnectionId,
                FromLocationId = l.FromLocationId,
                ToLocationId = l.ToLocationId,
                ModeCode = l.ModeCode,
                DistanceKm = l.DistanceKm,
                TerrainType = l.TerrainType,
                EstimatedDurationGameHours = l.EstimatedDurationGameHours,
                WaypointTransferTimeGameHours = l.WaypointTransferTimeGameHours,
                Status = l.Status,
                CompletedAtGameTime = l.CompletedAtGameTime
            }).ToList(),
            CurrentLegIndex = model.CurrentLegIndex,
            PrimaryModeCode = model.PrimaryModeCode,
            EffectiveSpeedKmPerGameHour = model.EffectiveSpeedKmPerGameHour,
            PlannedDepartureGameTime = model.PlannedDepartureGameTime,
            ActualDepartureGameTime = model.ActualDepartureGameTime,
            EstimatedArrivalGameTime = model.EstimatedArrivalGameTime,
            ActualArrivalGameTime = model.ActualArrivalGameTime,
            OriginLocationId = model.OriginLocationId,
            DestinationLocationId = model.DestinationLocationId,
            CurrentLocationId = model.CurrentLocationId,
            Status = model.Status,
            StatusReason = model.StatusReason,
            Interruptions = model.Interruptions.Select(i => new TransitInterruption
            {
                LegIndex = i.LegIndex,
                GameTime = i.GameTime,
                Reason = i.Reason,
                // API schema uses non-nullable decimal (0 for unresolved); internal model uses null for unknown duration
                DurationGameHours = i.DurationGameHours ?? 0,
                Resolved = i.Resolved
            }).ToList(),
            PartySize = model.PartySize,
            CargoWeightKg = model.CargoWeightKg,
            CreatedAt = model.CreatedAt,
            ModifiedAt = model.ModifiedAt
        };
    }

    /// <summary>
    /// Maps a <see cref="JourneyArchiveModel"/> to the generated <see cref="TransitJourney"/> API model.
    /// </summary>
    private static TransitJourney MapArchivedJourneyToApi(JourneyArchiveModel model)
    {
        return new TransitJourney
        {
            Id = model.Id,
            EntityId = model.EntityId,
            EntityType = model.EntityType,
            Legs = model.Legs.Select(l => new TransitJourneyLeg
            {
                ConnectionId = l.ConnectionId,
                FromLocationId = l.FromLocationId,
                ToLocationId = l.ToLocationId,
                ModeCode = l.ModeCode,
                DistanceKm = l.DistanceKm,
                TerrainType = l.TerrainType,
                EstimatedDurationGameHours = l.EstimatedDurationGameHours,
                WaypointTransferTimeGameHours = l.WaypointTransferTimeGameHours,
                Status = l.Status,
                CompletedAtGameTime = l.CompletedAtGameTime
            }).ToList(),
            CurrentLegIndex = model.CurrentLegIndex,
            PrimaryModeCode = model.PrimaryModeCode,
            EffectiveSpeedKmPerGameHour = model.EffectiveSpeedKmPerGameHour,
            PlannedDepartureGameTime = model.PlannedDepartureGameTime,
            ActualDepartureGameTime = model.ActualDepartureGameTime,
            EstimatedArrivalGameTime = model.EstimatedArrivalGameTime,
            ActualArrivalGameTime = model.ActualArrivalGameTime,
            OriginLocationId = model.OriginLocationId,
            DestinationLocationId = model.DestinationLocationId,
            CurrentLocationId = model.CurrentLocationId,
            Status = model.Status,
            StatusReason = model.StatusReason,
            Interruptions = model.Interruptions.Select(i => new TransitInterruption
            {
                LegIndex = i.LegIndex,
                GameTime = i.GameTime,
                Reason = i.Reason,
                // API schema uses non-nullable decimal (0 for unresolved); internal model uses null for unknown duration
                DurationGameHours = i.DurationGameHours ?? 0,
                Resolved = i.Resolved
            }).ToList(),
            PartySize = model.PartySize,
            CargoWeightKg = model.CargoWeightKg,
            CreatedAt = model.CreatedAt,
            ModifiedAt = model.ModifiedAt
        };
    }

    /// <summary>
    /// Computes the effective speed for a mode accounting for cargo weight penalty.
    /// Uses the cargo speed penalty formula from the deep dive:
    /// speed_reduction = (cargo - threshold) / (capacity - threshold) x rate
    /// </summary>
    private decimal ComputeEffectiveSpeed(TransitModeModel mode, decimal cargoWeightKg)
    {
        var baseSpeed = mode.BaseSpeedKmPerGameHour;

        if (cargoWeightKg <= 0 || mode.CargoCapacityKg <= 0)
        {
            return baseSpeed;
        }

        var threshold = (decimal)_configuration.CargoSpeedPenaltyThresholdKg;
        if (cargoWeightKg <= threshold)
        {
            return baseSpeed;
        }

        var rate = mode.CargoSpeedPenaltyRate ?? (decimal)_configuration.DefaultCargoSpeedPenaltyRate;
        var capacity = mode.CargoCapacityKg;

        if (capacity <= threshold)
        {
            return baseSpeed;
        }

        var penaltyFraction = (cargoWeightKg - threshold) / (capacity - threshold);
        penaltyFraction = Math.Min(penaltyFraction, 1m); // Clamp to 100% of capacity
        var speedReduction = penaltyFraction * rate;

        return baseSpeed * (1m - speedReduction);
    }

    /// <summary>
    /// Computes the estimated duration in game-hours for a single journey leg,
    /// accounting for terrain speed modifiers and cargo penalties.
    /// </summary>
    private decimal ComputeLegDurationGameHours(
        decimal distanceKm,
        string terrainType,
        TransitModeModel legMode,
        decimal cargoWeightKg)
    {
        var effectiveSpeed = ComputeEffectiveSpeed(legMode, cargoWeightKg);

        // Apply terrain speed modifier
        if (legMode.TerrainSpeedModifiers != null && !string.IsNullOrEmpty(terrainType))
        {
            var terrainMod = legMode.TerrainSpeedModifiers
                .FirstOrDefault(t => t.TerrainType == terrainType);
            if (terrainMod != null)
            {
                effectiveSpeed *= terrainMod.Multiplier;
            }
        }

        // Avoid division by zero
        if (effectiveSpeed <= 0)
        {
            effectiveSpeed = (decimal)_configuration.DefaultWalkingSpeedKmPerGameHour;
        }

        return distanceKm / effectiveSpeed;
    }

    /// <summary>
    /// Reports an entity's position to the Location service if AutoUpdateLocationOnTransition is enabled.
    /// Best-effort: failures are logged but do not fail the journey operation.
    /// </summary>
    private async Task TryReportEntityPositionAsync(
        string entityType,
        Guid entityId,
        Guid locationId,
        Guid? previousLocationId,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.transit", "TransitService.TryReportEntityPositionAsync");

        if (!_configuration.AutoUpdateLocationOnTransition)
        {
            return;
        }

        try
        {
            await _locationClient.ReportEntityPositionAsync(
                new Location.ReportEntityPositionRequest
                {
                    EntityType = entityType,
                    EntityId = entityId,
                    LocationId = locationId,
                    PreviousLocationId = previousLocationId,
                    ReportedBy = "transit"
                },
                cancellationToken);

            _logger.LogDebug("Reported entity {EntityId} position at location {LocationId}", entityId, locationId);
        }
        catch (ApiException ex)
        {
            // Best-effort: position reporting failure should not fail the journey operation
            _logger.LogWarning(ex, "Failed to report entity {EntityId} position at location {LocationId}", entityId, locationId);
        }
    }

    /// <summary>
    /// Attempts to auto-reveal a discoverable connection for an entity after traversing it.
    /// Only reveals if the connection is marked as discoverable.
    /// Best-effort: failures are logged but do not fail the advance operation.
    /// </summary>
    private async Task TryAutoRevealDiscoveryAsync(Guid entityId, Guid connectionId, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.transit", "TransitService.TryAutoRevealDiscoveryAsync");

        try
        {
            var connKey = BuildConnectionKey(connectionId);
            var conn = await _connectionStore.GetAsync(connKey, cancellationToken: cancellationToken);
            if (conn == null || !conn.Discoverable)
            {
                return;
            }

            // Call RevealDiscovery internally (reuses the full discovery logic including events)
            var revealRequest = new RevealDiscoveryRequest
            {
                EntityId = entityId,
                ConnectionId = connectionId,
                Source = "travel"
            };

            await RevealDiscoveryAsync(revealRequest, cancellationToken);
        }
        catch (Exception ex)
        {
            // Best-effort: auto-reveal failure should not fail the advance operation
            _logger.LogWarning(ex, "Failed to auto-reveal connection {ConnectionId} for entity {EntityId}", connectionId, entityId);
        }
    }

    /// <summary>
    /// Resolves the realm ID for a location. Returns null if the location is not found.
    /// Best-effort helper for event publishing -- does not fail the calling operation.
    /// </summary>
    private async Task<Guid?> ResolveLocationRealmIdAsync(Guid locationId, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.transit", "TransitService.ResolveLocationRealmIdAsync");

        try
        {
            var location = await _locationClient.GetLocationAsync(
                new Location.GetLocationRequest { LocationId = locationId },
                cancellationToken);
            return location.RealmId;
        }
        catch (ApiException)
        {
            _logger.LogDebug("Could not resolve realm for location {LocationId}", locationId);
            return null;
        }
    }

    /// <summary>
    /// Adds a journey ID to the journey index list in Redis. Used by the
    /// <see cref="JourneyArchivalWorker"/> to discover journeys without Redis SCAN.
    /// Uses optimistic concurrency to handle concurrent additions safely.
    /// </summary>
    /// <param name="journeyId">The journey ID to add to the index.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    internal async Task AddToJourneyIndexAsync(Guid journeyId, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.transit", "TransitService.AddToJourneyIndexAsync");

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var (currentIndex, etag) = await _journeyIndexStore.GetWithETagAsync(JOURNEY_INDEX_KEY, cancellationToken);

            var updatedIndex = currentIndex ?? new List<Guid>();
            updatedIndex.Add(journeyId);

            if (etag == null)
            {
                // First entry -- no concurrency conflict possible
                await _journeyIndexStore.SaveAsync(JOURNEY_INDEX_KEY, updatedIndex, cancellationToken: cancellationToken);
                return;
            }

            var result = await _journeyIndexStore.TrySaveAsync(JOURNEY_INDEX_KEY, updatedIndex, etag ?? string.Empty, cancellationToken);
            if (result != null)
            {
                return;
            }

            _logger.LogDebug("Concurrent modification on journey index during add, retrying (attempt {Attempt})", attempt + 1);
        }

        _logger.LogWarning("Failed to add journey {JourneyId} to index after retries - archival worker will miss this journey until it is re-indexed", journeyId);
    }

    #endregion

    #region Journey Event Publishing

    /// <summary>
    /// Publishes a transit.journey.departed event.
    /// </summary>
    private async Task PublishJourneyDepartedEventAsync(
        TransitJourneyModel journey,
        Guid? originRealmId,
        Guid? destinationRealmId,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.transit", "TransitService.PublishJourneyDepartedEventAsync");

        var eventModel = new TransitJourneyDepartedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            JourneyId = journey.Id,
            EntityId = journey.EntityId,
            EntityType = journey.EntityType,
            OriginLocationId = journey.OriginLocationId,
            DestinationLocationId = journey.DestinationLocationId,
            PrimaryModeCode = journey.PrimaryModeCode,
            EstimatedArrivalGameTime = journey.EstimatedArrivalGameTime,
            PartySize = journey.PartySize,
            OriginRealmId = originRealmId,
            DestinationRealmId = destinationRealmId,
            CrossRealm = originRealmId.HasValue && destinationRealmId.HasValue && originRealmId != destinationRealmId
        };

        var published = await _messageBus.TryPublishAsync("transit.journey.departed", eventModel, cancellationToken: cancellationToken);
        if (published)
        {
            _logger.LogDebug("Published transit.journey.departed event for journey {JourneyId}", journey.Id);
        }
        else
        {
            _logger.LogWarning("Failed to publish transit.journey.departed event for journey {JourneyId}", journey.Id);
        }
    }

    /// <summary>
    /// Publishes a transit.journey.waypoint-reached event.
    /// </summary>
    private async Task PublishJourneyWaypointReachedEventAsync(
        TransitJourneyModel journey,
        Guid waypointLocationId,
        Guid nextLocationId,
        int completedLegIndex,
        Guid connectionId,
        Guid? realmId,
        bool crossedRealmBoundary,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.transit", "TransitService.PublishJourneyWaypointReachedEventAsync");

        var eventModel = new TransitJourneyWaypointReachedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            JourneyId = journey.Id,
            EntityId = journey.EntityId,
            EntityType = journey.EntityType,
            WaypointLocationId = waypointLocationId,
            NextLocationId = nextLocationId,
            LegIndex = completedLegIndex,
            RemainingLegs = journey.Legs.Count - journey.CurrentLegIndex,
            ConnectionId = connectionId,
            RealmId = realmId,
            CrossedRealmBoundary = crossedRealmBoundary
        };

        var published = await _messageBus.TryPublishAsync("transit.journey.waypoint-reached", eventModel, cancellationToken: cancellationToken);
        if (published)
        {
            _logger.LogDebug("Published transit.journey.waypoint-reached event for journey {JourneyId}", journey.Id);
        }
        else
        {
            _logger.LogWarning("Failed to publish transit.journey.waypoint-reached event for journey {JourneyId}", journey.Id);
        }
    }

    /// <summary>
    /// Publishes a transit.journey.arrived event.
    /// </summary>
    private async Task PublishJourneyArrivedEventAsync(
        TransitJourneyModel journey,
        Guid? originRealmId,
        Guid? destinationRealmId,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.transit", "TransitService.PublishJourneyArrivedEventAsync");

        var totalDistanceKm = journey.Legs.Where(l => l.Status == JourneyLegStatus.Completed).Sum(l => l.DistanceKm);
        var totalGameHours = journey.ActualArrivalGameTime.HasValue && journey.ActualDepartureGameTime.HasValue
            ? journey.ActualArrivalGameTime.Value - journey.ActualDepartureGameTime.Value
            : journey.Legs.Sum(l => l.EstimatedDurationGameHours);

        var eventModel = new TransitJourneyArrivedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            JourneyId = journey.Id,
            EntityId = journey.EntityId,
            EntityType = journey.EntityType,
            OriginLocationId = journey.OriginLocationId,
            DestinationLocationId = journey.DestinationLocationId,
            PrimaryModeCode = journey.PrimaryModeCode,
            TotalGameHours = totalGameHours,
            TotalDistanceKm = totalDistanceKm,
            InterruptionCount = journey.Interruptions.Count,
            LegsCompleted = journey.Legs.Count(l => l.Status == JourneyLegStatus.Completed),
            OriginRealmId = originRealmId,
            DestinationRealmId = destinationRealmId,
            CrossRealm = originRealmId.HasValue && destinationRealmId.HasValue && originRealmId != destinationRealmId
        };

        var published = await _messageBus.TryPublishAsync("transit.journey.arrived", eventModel, cancellationToken: cancellationToken);
        if (published)
        {
            _logger.LogDebug("Published transit.journey.arrived event for journey {JourneyId}", journey.Id);
        }
        else
        {
            _logger.LogWarning("Failed to publish transit.journey.arrived event for journey {JourneyId}", journey.Id);
        }
    }

    /// <summary>
    /// Publishes a transit.journey.interrupted event.
    /// </summary>
    private async Task PublishJourneyInterruptedEventAsync(
        TransitJourneyModel journey,
        Guid? realmId,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.transit", "TransitService.PublishJourneyInterruptedEventAsync");

        var eventModel = new TransitJourneyInterruptedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            JourneyId = journey.Id,
            EntityId = journey.EntityId,
            EntityType = journey.EntityType,
            CurrentLocationId = journey.CurrentLocationId,
            CurrentLegIndex = journey.CurrentLegIndex,
            Reason = journey.StatusReason,
            RealmId = realmId
        };

        var published = await _messageBus.TryPublishAsync("transit.journey.interrupted", eventModel, cancellationToken: cancellationToken);
        if (published)
        {
            _logger.LogDebug("Published transit.journey.interrupted event for journey {JourneyId}", journey.Id);
        }
        else
        {
            _logger.LogWarning("Failed to publish transit.journey.interrupted event for journey {JourneyId}", journey.Id);
        }
    }

    /// <summary>
    /// Publishes a transit.journey.resumed event.
    /// </summary>
    private async Task PublishJourneyResumedEventAsync(
        TransitJourneyModel journey,
        Guid? realmId,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.transit", "TransitService.PublishJourneyResumedEventAsync");

        var currentLegModeCode = journey.CurrentLegIndex < journey.Legs.Count
            ? journey.Legs[journey.CurrentLegIndex].ModeCode
            : journey.PrimaryModeCode;

        var eventModel = new TransitJourneyResumedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            JourneyId = journey.Id,
            EntityId = journey.EntityId,
            EntityType = journey.EntityType,
            CurrentLocationId = journey.CurrentLocationId,
            DestinationLocationId = journey.DestinationLocationId,
            CurrentLegIndex = journey.CurrentLegIndex,
            RemainingLegs = journey.Legs.Count - journey.CurrentLegIndex,
            ModeCode = currentLegModeCode,
            RealmId = realmId
        };

        var published = await _messageBus.TryPublishAsync("transit.journey.resumed", eventModel, cancellationToken: cancellationToken);
        if (published)
        {
            _logger.LogDebug("Published transit.journey.resumed event for journey {JourneyId}", journey.Id);
        }
        else
        {
            _logger.LogWarning("Failed to publish transit.journey.resumed event for journey {JourneyId}", journey.Id);
        }
    }

    /// <summary>
    /// Publishes a transit.journey.abandoned event.
    /// </summary>
    private async Task PublishJourneyAbandonedEventAsync(
        TransitJourneyModel journey,
        Guid? originRealmId,
        Guid? destinationRealmId,
        Guid? abandonedAtRealmId,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.transit", "TransitService.PublishJourneyAbandonedEventAsync");

        var eventModel = new TransitJourneyAbandonedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            JourneyId = journey.Id,
            EntityId = journey.EntityId,
            EntityType = journey.EntityType,
            OriginLocationId = journey.OriginLocationId,
            DestinationLocationId = journey.DestinationLocationId,
            AbandonedAtLocationId = journey.CurrentLocationId,
            Reason = journey.StatusReason,
            CompletedLegs = journey.Legs.Count(l => l.Status == JourneyLegStatus.Completed),
            TotalLegs = journey.Legs.Count,
            OriginRealmId = originRealmId,
            DestinationRealmId = destinationRealmId,
            AbandonedAtRealmId = abandonedAtRealmId,
            CrossRealm = originRealmId.HasValue && destinationRealmId.HasValue && originRealmId != destinationRealmId
        };

        var published = await _messageBus.TryPublishAsync("transit.journey.abandoned", eventModel, cancellationToken: cancellationToken);
        if (published)
        {
            _logger.LogDebug("Published transit.journey.abandoned event for journey {JourneyId}", journey.Id);
        }
        else
        {
            _logger.LogWarning("Failed to publish transit.journey.abandoned event for journey {JourneyId}", journey.Id);
        }
    }

    /// <summary>
    /// Publishes a TransitJourneyUpdated client event to the traveling entity's WebSocket session(s)
    /// via IEntitySessionRegistry.
    /// </summary>
    /// <remarks>
    /// Pushes journey state changes (departure, waypoint, arrival, interruption, abandonment)
    /// to the entity's bound sessions for real-time UI updates.
    /// </remarks>
    /// <param name="journey">The journey model after the state change.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    private async Task PublishJourneyUpdatedClientEventAsync(
        TransitJourneyModel journey,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.transit", "TransitService.PublishJourneyUpdatedClientEventAsync");

        var remainingLegs = journey.Legs.Count - journey.CurrentLegIndex;

        var clientEvent = new TransitClientEvents.TransitJourneyUpdatedEvent
        {
            JourneyId = journey.Id,
            EntityId = journey.EntityId,
            Status = journey.Status,
            CurrentLocationId = journey.CurrentLocationId,
            DestinationLocationId = journey.DestinationLocationId,
            EstimatedArrivalGameTime = journey.EstimatedArrivalGameTime,
            CurrentLegIndex = journey.CurrentLegIndex,
            RemainingLegs = remainingLegs,
            PrimaryModeCode = journey.PrimaryModeCode
        };

        var count = await _entitySessionRegistry.PublishToEntitySessionsAsync(
            journey.EntityType, journey.EntityId, clientEvent, cancellationToken);

        _logger.LogDebug(
            "Published transit.journey_updated to {SessionCount} sessions for entity {EntityId}, journey {JourneyId}: {Status}",
            count, journey.EntityId, journey.Id, journey.Status);
    }

    #endregion

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

        // Resolve current time ratio and season for real-minutes estimation and seasonal warnings
        // Use the origin location's realm for the time ratio
        decimal currentTimeRatio = 0;
        string? currentSeason = null;
        try
        {
            var snapshot = await _worldstateClient.GetRealmTimeAsync(
                new Worldstate.GetRealmTimeRequest { RealmId = fromLocation.RealmId },
                cancellationToken);
            currentTimeRatio = (decimal)snapshot.TimeRatio;
            currentSeason = snapshot.Season;
        }
        catch (ApiException ex)
        {
            // Non-fatal: if Worldstate is unavailable, we can still calculate routes
            // but totalRealMinutes will be 0 and seasonal warnings will omit season data
            _logger.LogWarning(ex, "Could not retrieve time ratio for realm {RealmId}, real-minutes estimation will be unavailable",
                fromLocation.RealmId);
        }

        // Build the route calculation request for the calculator
        // CargoWeightKg is intentionally zeroed for the public API endpoint. Internal callers
        // (journey planner, variable provider) will populate it with actual cargo weight.
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
            CurrentTimeRatio: currentTimeRatio,
            CurrentSeason: currentSeason);

        // Invoke the route calculator (stateless computation)
        var results = await _routeCalculator.CalculateAsync(calcRequest, cancellationToken);

        // Map results to API response model
        var options = results.Select((r, index) => new TransitRouteOption
        {
            Waypoints = r.Waypoints,
            Connections = r.Connections,
            LegCount = r.Connections.Count,
            PrimaryModeCode = r.PrimaryModeCode,
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

    #endregion

    #region Discovery

    private const string DISCOVERY_KEY_PREFIX = "discovery:";

    /// <summary>
    /// Builds the state store key for a discovery record.
    /// Composite key: discovery:{entityId}:{connectionId}
    /// </summary>
    private static string BuildDiscoveryKey(Guid entityId, Guid connectionId) =>
        $"{DISCOVERY_KEY_PREFIX}{entityId}:{connectionId}";

    /// <summary>
    /// Builds the cache key for an entity's discovered connections set.
    /// Key: discovery-cache:{entityId}
    /// </summary>
    private static string BuildDiscoveryCacheKey(Guid entityId) =>
        $"discovery-cache:{entityId}";

    /// <summary>
    /// Reveals a discoverable connection to an entity. If the connection has already been
    /// discovered by this entity, returns the existing record with isNew=false.
    /// New discoveries are saved to MySQL for durability and cached in Redis for fast
    /// route calculation filtering. Publishes discovery event and client event on new discovery.
    /// </summary>
    /// <param name="body">Request containing entity ID, connection ID, and discovery source.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>OK with the discovery record, NotFound if connection does not exist, BadRequest if not discoverable.</returns>
    public async Task<(StatusCodes, RevealDiscoveryResponse?)> RevealDiscoveryAsync(RevealDiscoveryRequest body, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.transit", "TransitService.RevealDiscoveryAsync");

        _logger.LogDebug("Revealing connection {ConnectionId} to entity {EntityId} via source {Source}",
            body.ConnectionId, body.EntityId, body.Source);

        // Validate connection exists
        var connectionKey = BuildConnectionKey(body.ConnectionId);
        var connection = await _connectionStore.GetAsync(connectionKey, cancellationToken: cancellationToken);

        if (connection == null)
        {
            _logger.LogDebug("Connection not found for discovery reveal: {ConnectionId}", body.ConnectionId);
            return (StatusCodes.NotFound, null);
        }

        // Check that the connection is discoverable
        if (!connection.Discoverable)
        {
            _logger.LogDebug("Connection {ConnectionId} is not discoverable", body.ConnectionId);
            return (StatusCodes.BadRequest, null);
        }

        // Distributed lock to prevent duplicate discovery events from concurrent reveals
        // per IMPLEMENTATION TENETS (multi-instance safety)
        var lockOwner = $"reveal-discovery-{Guid.NewGuid():N}";
        await using var lockResponse = await _lockProvider.LockAsync(
            StateStoreDefinitions.TransitLock,
            $"discovery:{body.EntityId}:{body.ConnectionId}",
            lockOwner,
            _configuration.LockTimeoutSeconds,
            cancellationToken);

        if (!lockResponse.Success)
        {
            _logger.LogDebug("Could not acquire lock for discovery reveal entity {EntityId} connection {ConnectionId}, returning Conflict",
                body.EntityId, body.ConnectionId);
            return (StatusCodes.Conflict, null);
        }

        // Check if the entity has already discovered this connection (within lock)
        var discoveryKey = BuildDiscoveryKey(body.EntityId, body.ConnectionId);
        var existingDiscovery = await _discoveryStore.GetAsync(discoveryKey, cancellationToken: cancellationToken);

        if (existingDiscovery != null)
        {
            _logger.LogDebug("Entity {EntityId} has already discovered connection {ConnectionId}, returning existing record",
                body.EntityId, body.ConnectionId);

            return (StatusCodes.OK, new RevealDiscoveryResponse
            {
                Discovery = MapDiscoveryToApi(existingDiscovery, isNew: false)
            });
        }

        // New discovery: create and save
        var now = DateTimeOffset.UtcNow;
        var discoveryModel = new TransitDiscoveryModel
        {
            EntityId = body.EntityId,
            ConnectionId = body.ConnectionId,
            Source = body.Source,
            DiscoveredAt = now,
            IsNew = true
        };

        await _discoveryStore.SaveAsync(discoveryKey, discoveryModel, cancellationToken: cancellationToken);

        // Update Redis discovery cache for fast route calculation filtering
        await UpdateDiscoveryCacheAsync(body.EntityId, body.ConnectionId, cancellationToken);

        // Publish transit.discovery.revealed service bus event
        await PublishDiscoveryRevealedEventAsync(discoveryModel, connection, cancellationToken);

        // Publish client event to the discovering entity's session(s).
        // RevealDiscoveryRequest does not carry entityType; default to "character"
        // (the primary discovery use case). Auto-reveal from journeys also routes
        // through this method via TryAutoRevealDiscoveryAsync.
        await PublishDiscoveryRevealedClientEventAsync(discoveryModel, connection, "character", cancellationToken);

        _logger.LogInformation("Entity {EntityId} discovered connection {ConnectionId} via {Source}",
            body.EntityId, body.ConnectionId, body.Source);

        return (StatusCodes.OK, new RevealDiscoveryResponse
        {
            Discovery = MapDiscoveryToApi(discoveryModel, isNew: true)
        });
    }

    /// <summary>
    /// Lists connection IDs that an entity has discovered, optionally filtered by realm.
    /// Queries the MySQL discovery store for durable discovery records.
    /// </summary>
    /// <param name="body">Request containing entity ID and optional realm filter.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>OK with the list of discovered connection IDs.</returns>
    public async Task<(StatusCodes, ListDiscoveriesResponse?)> ListDiscoveriesAsync(ListDiscoveriesRequest body, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.transit", "TransitService.ListDiscoveriesAsync");

        _logger.LogDebug("Listing discoveries for entity {EntityId} with realm filter {RealmId}",
            body.EntityId, body.RealmId);

        // Query all discoveries for this entity from MySQL
        var discoveries = await _discoveryStore.QueryAsync(
            d => d.EntityId == body.EntityId,
            cancellationToken);

        IEnumerable<Guid> connectionIds;

        if (body.RealmId.HasValue)
        {
            // Filter by realm: need to check each connection's realm fields
            var realmId = body.RealmId.Value;
            var filteredConnectionIds = new List<Guid>();

            foreach (var discovery in discoveries)
            {
                var connKey = BuildConnectionKey(discovery.ConnectionId);
                var conn = await _connectionStore.GetAsync(connKey, cancellationToken: cancellationToken);

                // Include if connection still exists and touches the specified realm
                if (conn != null && (conn.FromRealmId == realmId || conn.ToRealmId == realmId))
                {
                    filteredConnectionIds.Add(discovery.ConnectionId);
                }
            }

            connectionIds = filteredConnectionIds;
        }
        else
        {
            connectionIds = discoveries.Select(d => d.ConnectionId);
        }

        var result = connectionIds.ToList();

        _logger.LogDebug("Found {Count} discoveries for entity {EntityId}", result.Count, body.EntityId);

        return (StatusCodes.OK, new ListDiscoveriesResponse { ConnectionIds = result });
    }

    /// <summary>
    /// Checks whether an entity has discovered specific connections.
    /// Returns per-connection discovery status including when and how each was discovered.
    /// </summary>
    /// <param name="body">Request containing entity ID and connection IDs to check.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>OK with per-connection discovery check results.</returns>
    public async Task<(StatusCodes, CheckDiscoveriesResponse?)> CheckDiscoveriesAsync(CheckDiscoveriesRequest body, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.transit", "TransitService.CheckDiscoveriesAsync");

        _logger.LogDebug("Checking {Count} connection discoveries for entity {EntityId}",
            body.ConnectionIds.Count, body.EntityId);

        var results = new List<DiscoveryCheckResult>();

        foreach (var connectionId in body.ConnectionIds)
        {
            var discoveryKey = BuildDiscoveryKey(body.EntityId, connectionId);
            var discovery = await _discoveryStore.GetAsync(discoveryKey, cancellationToken: cancellationToken);

            if (discovery != null)
            {
                results.Add(new DiscoveryCheckResult
                {
                    ConnectionId = connectionId,
                    Discovered = true,
                    DiscoveredAt = discovery.DiscoveredAt,
                    Source = discovery.Source
                });
            }
            else
            {
                results.Add(new DiscoveryCheckResult
                {
                    ConnectionId = connectionId,
                    Discovered = false,
                    DiscoveredAt = null,
                    Source = null
                });
            }
        }

        return (StatusCodes.OK, new CheckDiscoveriesResponse { Results = results });
    }

    /// <summary>
    /// Updates the Redis discovery cache for an entity after a new discovery.
    /// Loads the existing set, adds the new connection ID, and saves with TTL.
    /// </summary>
    /// <remarks>
    /// This is a best-effort cache optimization. The read-modify-write on the cache set is
    /// non-atomic: concurrent cache updates for the same entity could lose entries. This is
    /// acceptable because the cache is rebuilt from the authoritative MySQL discovery store
    /// on cache miss. The route calculator falls back to MySQL when the cache is absent or stale.
    /// </remarks>
    /// <param name="entityId">The entity whose discovery cache should be updated.</param>
    /// <param name="connectionId">The newly discovered connection ID to add to the cache.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    private async Task UpdateDiscoveryCacheAsync(Guid entityId, Guid connectionId, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.transit", "TransitService.UpdateDiscoveryCacheAsync");

        var ttlSeconds = _configuration.DiscoveryCacheTtlSeconds;
        if (ttlSeconds <= 0)
        {
            _logger.LogDebug("Discovery cache TTL is 0, skipping cache update for entity {EntityId}", entityId);
            return;
        }

        var cacheKey = BuildDiscoveryCacheKey(entityId);
        var cachedSet = await _discoveryCacheStore.GetAsync(cacheKey, cancellationToken: cancellationToken) ?? new HashSet<Guid>();
        cachedSet.Add(connectionId);

        await _discoveryCacheStore.SaveAsync(cacheKey, cachedSet, new StateOptions { Ttl = ttlSeconds }, cancellationToken);
    }

    /// <summary>
    /// Maps an internal <see cref="TransitDiscoveryModel"/> to the generated <see cref="DiscoveryRecord"/> API model.
    /// </summary>
    /// <param name="model">The internal discovery storage model.</param>
    /// <param name="isNew">Whether this discovery is new (first time) or a re-revelation.</param>
    /// <returns>The API-facing discovery record.</returns>
    private static DiscoveryRecord MapDiscoveryToApi(TransitDiscoveryModel model, bool isNew)
    {
        return new DiscoveryRecord
        {
            EntityId = model.EntityId,
            ConnectionId = model.ConnectionId,
            Source = model.Source,
            DiscoveredAt = model.DiscoveredAt,
            IsNew = isNew
        };
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
    /// Publishes a transit.connection.created lifecycle event with full entity data.
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

        var published = await _messageBus.TryPublishAsync("transit.connection.created", eventModel, cancellationToken: cancellationToken);
        if (published)
        {
            _logger.LogDebug("Published transit.connection.created event for {ConnectionId}", model.Id);
        }
        else
        {
            _logger.LogWarning("Failed to publish transit.connection.created event for {ConnectionId}", model.Id);
        }
    }

    /// <summary>
    /// Publishes a transit.connection.updated lifecycle event with current state and changed fields.
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

        var published = await _messageBus.TryPublishAsync("transit.connection.updated", eventModel, cancellationToken: cancellationToken);
        if (published)
        {
            _logger.LogDebug("Published transit.connection.updated event for {ConnectionId} with changed fields: {ChangedFields}",
                model.Id, string.Join(", ", changedFieldsList));
        }
        else
        {
            _logger.LogWarning("Failed to publish transit.connection.updated event for {ConnectionId}", model.Id);
        }
    }

    /// <summary>
    /// Publishes a transit.connection.deleted lifecycle event with full entity data and deletion reason.
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

        var published = await _messageBus.TryPublishAsync("transit.connection.deleted", eventModel, cancellationToken: cancellationToken);
        if (published)
        {
            _logger.LogDebug("Published transit.connection.deleted event for {ConnectionId}", model.Id);
        }
        else
        {
            _logger.LogWarning("Failed to publish transit.connection.deleted event for {ConnectionId}", model.Id);
        }
    }

    /// <summary>
    /// Publishes a transit.connection.status-changed custom event (not lifecycle).
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
            Reason = model.StatusReason,
            ForceUpdated = forceUpdated,
            FromRealmId = model.FromRealmId,
            ToRealmId = model.ToRealmId,
            CrossRealm = model.CrossRealm
        };

        var published = await _messageBus.TryPublishAsync("transit.connection.status-changed", eventModel, cancellationToken: cancellationToken);
        if (published)
        {
            _logger.LogDebug("Published transit.connection.status-changed event for {ConnectionId}: {PreviousStatus} -> {NewStatus}",
                model.Id, previousStatus, model.Status);
        }
        else
        {
            _logger.LogWarning("Failed to publish transit.connection.status-changed event for {ConnectionId}", model.Id);
        }
    }

    /// <summary>
    /// Publishes a TransitConnectionStatusChanged client event to WebSocket sessions
    /// in the affected realm(s) via IEntitySessionRegistry.
    /// </summary>
    /// <remarks>
    /// Routes client events to all sessions registered for the affected realm entity.
    /// Cross-realm connections publish to both the origin and destination realms.
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

        var clientEvent = new TransitClientEvents.TransitConnectionStatusChangedEvent
        {
            ConnectionId = model.Id,
            FromLocationId = model.FromLocationId,
            ToLocationId = model.ToLocationId,
            PreviousStatus = previousStatus,
            NewStatus = model.Status,
            Reason = reason
        };

        // Publish to all sessions watching the origin realm
        var fromCount = await _entitySessionRegistry.PublishToEntitySessionsAsync(
            "realm", model.FromRealmId, clientEvent, cancellationToken);

        _logger.LogDebug(
            "Published transit.connection_status_changed to {SessionCount} sessions for realm {RealmId}, connection {ConnectionId}: {PreviousStatus} -> {NewStatus}",
            fromCount, model.FromRealmId, model.Id, previousStatus, model.Status);

        // Cross-realm connections: also publish to the destination realm
        if (model.CrossRealm)
        {
            var toCount = await _entitySessionRegistry.PublishToEntitySessionsAsync(
                "realm", model.ToRealmId, clientEvent, cancellationToken);

            _logger.LogDebug(
                "Published transit.connection_status_changed to {SessionCount} sessions for destination realm {RealmId}, connection {ConnectionId}",
                toCount, model.ToRealmId, model.Id);
        }
    }

    #endregion

    #region Discovery Event Publishing

    /// <summary>
    /// Publishes a transit.discovery.revealed service bus event when a connection
    /// is revealed to an entity for the first time. Consumed by Collection (unlocks),
    /// Quest (objectives), and Analytics (aggregation).
    /// </summary>
    /// <param name="discovery">The discovery record.</param>
    /// <param name="connection">The connection that was discovered (for realm and location data).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    private async Task PublishDiscoveryRevealedEventAsync(
        TransitDiscoveryModel discovery,
        TransitConnectionModel connection,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.transit", "TransitService.PublishDiscoveryRevealedEventAsync");

        var eventModel = new TransitDiscoveryRevealedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            EntityId = discovery.EntityId,
            ConnectionId = discovery.ConnectionId,
            FromLocationId = connection.FromLocationId,
            ToLocationId = connection.ToLocationId,
            Source = discovery.Source,
            FromRealmId = connection.FromRealmId,
            ToRealmId = connection.ToRealmId,
            CrossRealm = connection.CrossRealm
        };

        var published = await _messageBus.TryPublishAsync("transit.discovery.revealed", eventModel, cancellationToken: cancellationToken);
        if (published)
        {
            _logger.LogDebug("Published transit.discovery.revealed event for entity {EntityId} connection {ConnectionId}",
                discovery.EntityId, discovery.ConnectionId);
        }
        else
        {
            _logger.LogWarning("Failed to publish transit.discovery.revealed event for entity {EntityId} connection {ConnectionId}",
                discovery.EntityId, discovery.ConnectionId);
        }
    }

    /// <summary>
    /// Publishes a TransitDiscoveryRevealed client event to the discovering entity's WebSocket
    /// session(s) via IEntitySessionRegistry.
    /// </summary>
    /// <remarks>
    /// Discovery reveals are entity-scoped: the client event is pushed to all sessions
    /// registered for the discovering entity. The entity type is determined from the journey
    /// context when auto-revealed during travel, or defaults to "character" for direct
    /// API reveals (the primary discovery use case).
    /// </remarks>
    /// <param name="discovery">The discovery record containing entity and connection IDs.</param>
    /// <param name="connection">The connection that was discovered (for location data and name).</param>
    /// <param name="entityType">The entity type of the discovering entity.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    private async Task PublishDiscoveryRevealedClientEventAsync(
        TransitDiscoveryModel discovery,
        TransitConnectionModel connection,
        string entityType,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.transit", "TransitService.PublishDiscoveryRevealedClientEventAsync");

        var clientEvent = new TransitClientEvents.TransitDiscoveryRevealedEvent
        {
            ConnectionId = discovery.ConnectionId,
            FromLocationId = connection.FromLocationId,
            ToLocationId = connection.ToLocationId,
            Source = discovery.Source,
            ConnectionName = connection.Name
        };

        var count = await _entitySessionRegistry.PublishToEntitySessionsAsync(
            entityType, discovery.EntityId, clientEvent, cancellationToken);

        _logger.LogDebug(
            "Published transit.discovery_revealed to {SessionCount} sessions for entity {EntityId}, connection {ConnectionId}",
            count, discovery.EntityId, discovery.ConnectionId);
    }

    #endregion

    #region Resource Cleanup

    /// <summary>
    /// Cleans up all transit data referencing a deleted location. Called by lib-resource
    /// when a location is deleted (CASCADE policy). Closes all connections referencing
    /// the deleted location and interrupts active journeys passing through it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>FOUNDATION TENETS (Resource-Managed Cleanup):</b> Transit does NOT subscribe to
    /// <c>location.deleted</c> events for cleanup. This callback is registered via lib-resource's
    /// cleanup coordination pattern and invoked by the Resource service during deletion.
    /// </para>
    /// </remarks>
    /// <param name="body">Request containing the deleted location ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>OK after cleanup completes.</returns>
    public async Task<StatusCodes> CleanupByLocationAsync(CleanupByLocationRequest body, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.transit", "TransitService.CleanupByLocationAsync");

        _logger.LogDebug("Cleaning up transit data for deleted location {LocationId}", body.LocationId);

        var deletedLocationId = body.LocationId;
        var affectedRealmIds = new HashSet<Guid>();
        var connectionsClosedCount = 0;
        var journeysInterruptedCount = 0;

        // Step 1: Find all connections referencing the deleted location (from MySQL, queryable)
        var affectedConnections = await _connectionStore.QueryAsync(
            c => c.FromLocationId == deletedLocationId || c.ToLocationId == deletedLocationId,
            cancellationToken);

        // Step 2: Close each affected connection with optimistic concurrency per IMPLEMENTATION TENETS (multi-instance safety)
        foreach (var connection in affectedConnections)
        {
            // Skip connections that are already closed -- no redundant saves or events
            if (connection.Status == ConnectionStatus.Closed)
            {
                // Still track realms for graph cache invalidation in case graph is stale
                affectedRealmIds.Add(connection.FromRealmId);
                if (connection.CrossRealm)
                {
                    affectedRealmIds.Add(connection.ToRealmId);
                }
                continue;
            }

            var previousStatus = connection.Status;

            // Track affected realms for graph cache invalidation
            affectedRealmIds.Add(connection.FromRealmId);
            if (connection.CrossRealm)
            {
                affectedRealmIds.Add(connection.ToRealmId);
            }

            // Use optimistic concurrency for connection status mutation per IMPLEMENTATION TENETS
            var connKey = BuildConnectionKey(connection.Id);
            var (freshConnection, connEtag) = await _connectionStore.GetWithETagAsync(connKey, cancellationToken);

            if (freshConnection == null)
            {
                _logger.LogDebug("Connection {ConnectionId} no longer exists during cleanup, skipping", connection.Id);
                continue;
            }

            // Re-check after fresh read -- may have been closed concurrently
            if (freshConnection.Status == ConnectionStatus.Closed)
            {
                _logger.LogDebug("Connection {ConnectionId} already closed by concurrent operation, skipping", connection.Id);
                continue;
            }

            freshConnection.Status = ConnectionStatus.Closed;
            freshConnection.StatusReason = "location_deleted";
            freshConnection.StatusChangedAt = DateTimeOffset.UtcNow;
            freshConnection.ModifiedAt = DateTimeOffset.UtcNow;

            var savedEtag = await _connectionStore.TrySaveAsync(connKey, freshConnection, connEtag ?? string.Empty, cancellationToken);
            if (savedEtag == null)
            {
                _logger.LogWarning("Concurrent modification detected for connection {ConnectionId} during location cleanup, skipping", connection.Id);
                continue;
            }

            // Publish service bus event for connection status change
            await PublishConnectionStatusChangedEventAsync(freshConnection, previousStatus, forceUpdated: true, cancellationToken);

            // Publish client event for connection status change to affected realm sessions
            await PublishConnectionStatusChangedClientEventAsync(freshConnection, previousStatus, "location_deleted", cancellationToken);

            connectionsClosedCount++;

            _logger.LogDebug("Closed connection {ConnectionId} due to location {LocationId} deletion",
                connection.Id, deletedLocationId);
        }

        // Step 3: Invalidate graph cache for all affected realms
        if (affectedRealmIds.Count > 0)
        {
            await _graphCache.InvalidateAsync(affectedRealmIds, cancellationToken);
        }

        // Step 4: Interrupt active journeys in archive store that reference the deleted location
        // as origin or destination. Location deletion is external disruption (potentially resumable
        // via rerouting), so use Interrupted rather than Abandoned per design spec.
        var affectedJourneys = await _journeyArchiveStore.QueryAsync(
            j => (j.OriginLocationId == deletedLocationId || j.DestinationLocationId == deletedLocationId) &&
                (j.Status != JourneyStatus.Arrived && j.Status != JourneyStatus.Abandoned && j.Status != JourneyStatus.Interrupted),
            cancellationToken);

        foreach (var archivedJourney in affectedJourneys)
        {
            // Use optimistic concurrency for journey status mutation per IMPLEMENTATION TENETS
            var archiveKey = BuildJourneyArchiveKey(archivedJourney.Id);
            var (freshJourney, journeyEtag) = await _journeyArchiveStore.GetWithETagAsync(archiveKey, cancellationToken);

            if (freshJourney == null)
            {
                _logger.LogDebug("Archived journey {JourneyId} no longer exists during cleanup, skipping", archivedJourney.Id);
                continue;
            }

            // Skip if already in a terminal or interrupted state
            if (freshJourney.Status == JourneyStatus.Arrived || freshJourney.Status == JourneyStatus.Abandoned || freshJourney.Status == JourneyStatus.Interrupted)
            {
                continue;
            }

            freshJourney.Status = JourneyStatus.Interrupted;
            freshJourney.StatusReason = "location_deleted";
            freshJourney.ModifiedAt = DateTimeOffset.UtcNow;

            var savedJourneyEtag = await _journeyArchiveStore.TrySaveAsync(archiveKey, freshJourney, journeyEtag ?? string.Empty, cancellationToken);
            if (savedJourneyEtag == null)
            {
                _logger.LogWarning("Concurrent modification detected for archived journey {JourneyId} during location cleanup, skipping", archivedJourney.Id);
                continue;
            }

            // Publish transit.journey.interrupted event per FOUNDATION TENETS (all state changes publish events)
            await PublishJourneyInterruptedEventAsync(
                new TransitJourneyModel
                {
                    Id = freshJourney.Id,
                    EntityId = freshJourney.EntityId,
                    EntityType = freshJourney.EntityType,
                    CurrentLocationId = freshJourney.CurrentLocationId,
                    CurrentLegIndex = freshJourney.CurrentLegIndex,
                    StatusReason = freshJourney.StatusReason
                },
                freshJourney.RealmId,
                cancellationToken);

            journeysInterruptedCount++;
        }

        // Step 5: Scan Redis active journeys for location references via connection lookup.
        // Active journeys reference connections, not locations directly. We check each active
        // journey's legs to see if any leg's connection references the deleted location.
        var redisLocationInterrupted = await ScanRedisJourneysForLocationCleanupAsync(
            deletedLocationId, affectedConnections, cancellationToken);
        journeysInterruptedCount += redisLocationInterrupted;

        _logger.LogInformation("Cleaned up transit data for deleted location {LocationId}: {ConnectionsClosed} connections closed, {JourneysInterrupted} journeys interrupted (archive + Redis)",
            body.LocationId, connectionsClosedCount, journeysInterruptedCount);

        return StatusCodes.OK;
    }

    /// <summary>
    /// Cleans up all transit data referencing a deleted character (entity). Called by lib-resource
    /// when a character is deleted (CASCADE policy). Clears all discovery records for the entity
    /// and abandons any active journeys belonging to the entity.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>FOUNDATION TENETS (Resource-Managed Cleanup):</b> Transit does NOT subscribe to
    /// <c>character.deleted</c> events for cleanup. This callback is registered via lib-resource's
    /// cleanup coordination pattern and invoked by the Resource service during deletion.
    /// </para>
    /// </remarks>
    /// <param name="body">Request containing the deleted character ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>OK after cleanup completes.</returns>
    public async Task<StatusCodes> CleanupByCharacterAsync(CleanupByCharacterRequest body, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.transit", "TransitService.CleanupByCharacterAsync");

        _logger.LogDebug("Cleaning up transit data for deleted character {CharacterId}", body.CharacterId);

        var deletedCharacterId = body.CharacterId;
        var discoveriesDeletedCount = 0;
        var journeysAbandonedCount = 0;

        // Step 1: Delete all discovery records for this entity from MySQL
        var discoveries = await _discoveryStore.QueryAsync(
            d => d.EntityId == deletedCharacterId,
            cancellationToken);

        foreach (var discovery in discoveries)
        {
            var discoveryKey = BuildDiscoveryKey(discovery.EntityId, discovery.ConnectionId);
            await _discoveryStore.DeleteAsync(discoveryKey, cancellationToken);
            discoveriesDeletedCount++;
        }

        // Log discovery deletions at Information level since no discovery.deleted event schema exists
        if (discoveriesDeletedCount > 0)
        {
            _logger.LogInformation("Deleted {DiscoveriesDeletedCount} discovery records for entity {EntityId} during character cleanup",
                discoveriesDeletedCount, deletedCharacterId);
        }

        // Step 2: Invalidate the Redis discovery cache for this entity
        var cacheKey = BuildDiscoveryCacheKey(deletedCharacterId);
        await _discoveryCacheStore.DeleteAsync(cacheKey, cancellationToken);

        // Step 3: Abandon active journeys in archive store for this entity.
        // Character deletion permanently terminates journeys (Abandoned, not Interrupted).
        var entityJourneys = await _journeyArchiveStore.QueryAsync(
            j => j.EntityId == deletedCharacterId &&
                (j.Status != JourneyStatus.Arrived && j.Status != JourneyStatus.Abandoned),
            cancellationToken);

        foreach (var archivedJourney in entityJourneys)
        {
            // Use optimistic concurrency for journey status mutation per IMPLEMENTATION TENETS
            var archiveKey = BuildJourneyArchiveKey(archivedJourney.Id);
            var (freshJourney, journeyEtag) = await _journeyArchiveStore.GetWithETagAsync(archiveKey, cancellationToken);

            if (freshJourney == null)
            {
                _logger.LogDebug("Archived journey {JourneyId} no longer exists during cleanup, skipping", archivedJourney.Id);
                continue;
            }

            // Skip if already in a terminal state
            if (freshJourney.Status == JourneyStatus.Arrived || freshJourney.Status == JourneyStatus.Abandoned)
            {
                continue;
            }

            freshJourney.Status = JourneyStatus.Abandoned;
            freshJourney.StatusReason = "character_deleted";
            freshJourney.ModifiedAt = DateTimeOffset.UtcNow;

            var savedJourneyEtag = await _journeyArchiveStore.TrySaveAsync(archiveKey, freshJourney, journeyEtag ?? string.Empty, cancellationToken);
            if (savedJourneyEtag == null)
            {
                _logger.LogWarning("Concurrent modification detected for archived journey {JourneyId} during character cleanup, skipping", archivedJourney.Id);
                continue;
            }

            // Publish transit.journey.abandoned event per FOUNDATION TENETS (all state changes publish events)
            // Archive model has a single RealmId (origin realm); use it for all realm fields
            await PublishJourneyAbandonedEventAsync(
                new TransitJourneyModel
                {
                    Id = freshJourney.Id,
                    EntityId = freshJourney.EntityId,
                    EntityType = freshJourney.EntityType,
                    OriginLocationId = freshJourney.OriginLocationId,
                    DestinationLocationId = freshJourney.DestinationLocationId,
                    CurrentLocationId = freshJourney.CurrentLocationId,
                    CurrentLegIndex = freshJourney.CurrentLegIndex,
                    Legs = freshJourney.Legs,
                    PrimaryModeCode = freshJourney.PrimaryModeCode,
                    StatusReason = freshJourney.StatusReason
                },
                freshJourney.RealmId,
                freshJourney.RealmId,
                freshJourney.RealmId,
                cancellationToken);

            journeysAbandonedCount++;
        }

        // Step 4: Scan Redis active journeys for entity match and abandon them.
        // Active journeys in Redis store entity ID and type directly on the journey model.
        var redisCharacterAbandoned = await ScanRedisJourneysForCharacterCleanupAsync(
            deletedCharacterId, "character", cancellationToken);
        journeysAbandonedCount += redisCharacterAbandoned;

        _logger.LogInformation("Cleaned up transit data for deleted character {CharacterId}: {DiscoveriesDeleted} discoveries deleted, {JourneysAbandoned} journeys abandoned (archive + Redis)",
            body.CharacterId, discoveriesDeletedCount, journeysAbandonedCount);

        return StatusCodes.OK;
    }

    #endregion

    #region Redis Journey Scan Helpers

    /// <summary>
    /// Scans all active Redis journeys and checks if any use the given mode code.
    /// Returns true if a conflict is found (an active journey uses the mode).
    /// </summary>
    /// <param name="modeCode">Mode code to check for active journey usage.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if an active journey references the mode; false otherwise.</returns>
    private async Task<bool> ScanRedisJourneysForModeConflictAsync(string modeCode, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.transit", "TransitService.ScanRedisJourneysForModeConflictAsync");

        var journeyIndex = await _journeyIndexStore.GetAsync(JOURNEY_INDEX_KEY, cancellationToken);
        if (journeyIndex == null)
        {
            return false;
        }

        foreach (var journeyId in journeyIndex)
        {
            var journey = await _journeyStore.GetAsync(BuildJourneyKey(journeyId), cancellationToken: cancellationToken);
            if (journey == null) continue;
            if (journey.Status == JourneyStatus.Arrived || journey.Status == JourneyStatus.Abandoned) continue;

            if (journey.PrimaryModeCode == modeCode)
            {
                _logger.LogDebug("Found active Redis journey {JourneyId} using mode {ModeCode}", journeyId, modeCode);
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Scans all active Redis journeys and checks if any reference the given connection ID
    /// in their legs. Returns true if a conflict is found.
    /// </summary>
    /// <param name="connectionId">Connection ID to check for active journey usage.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if an active journey references the connection; false otherwise.</returns>
    private async Task<bool> ScanRedisJourneysForConnectionConflictAsync(Guid connectionId, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.transit", "TransitService.ScanRedisJourneysForConnectionConflictAsync");

        var journeyIndex = await _journeyIndexStore.GetAsync(JOURNEY_INDEX_KEY, cancellationToken);
        if (journeyIndex == null)
        {
            return false;
        }

        foreach (var journeyId in journeyIndex)
        {
            var journey = await _journeyStore.GetAsync(BuildJourneyKey(journeyId), cancellationToken: cancellationToken);
            if (journey == null) continue;
            if (journey.Status == JourneyStatus.Arrived || journey.Status == JourneyStatus.Abandoned) continue;

            if (journey.Legs.Any(l => l.ConnectionId == connectionId))
            {
                _logger.LogDebug("Found active Redis journey {JourneyId} referencing connection {ConnectionId}", journeyId, connectionId);
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Scans Redis active journeys and interrupts any whose legs reference connections
    /// that touch the deleted location. Uses the already-queried affected connections list
    /// to build a set of connection IDs referencing the location.
    /// </summary>
    /// <param name="deletedLocationId">The deleted location ID.</param>
    /// <param name="affectedConnections">Connections already identified as referencing the deleted location.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Number of Redis journeys interrupted.</returns>
    private async Task<int> ScanRedisJourneysForLocationCleanupAsync(
        Guid deletedLocationId,
        IReadOnlyList<TransitConnectionModel> affectedConnections,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.transit", "TransitService.ScanRedisJourneysForLocationCleanupAsync");

        var journeyIndex = await _journeyIndexStore.GetAsync(JOURNEY_INDEX_KEY, cancellationToken);
        if (journeyIndex == null)
        {
            return 0;
        }

        // Build a set of connection IDs that reference the deleted location for efficient lookup
        var affectedConnectionIds = new HashSet<Guid>(affectedConnections.Select(c => c.Id));

        var interruptedCount = 0;

        foreach (var journeyId in journeyIndex)
        {
            var journey = await _journeyStore.GetAsync(BuildJourneyKey(journeyId), cancellationToken: cancellationToken);
            if (journey == null) continue;
            if (journey.Status == JourneyStatus.Arrived || journey.Status == JourneyStatus.Abandoned || journey.Status == JourneyStatus.Interrupted) continue;

            // Check if any leg references a connection touching the deleted location,
            // or if the journey's origin/destination is the deleted location
            var referencesLocation = journey.OriginLocationId == deletedLocationId ||
                                    journey.DestinationLocationId == deletedLocationId ||
                                    journey.Legs.Any(l => affectedConnectionIds.Contains(l.ConnectionId));

            if (!referencesLocation) continue;

            // Interrupt the journey in Redis
            journey.Status = JourneyStatus.Interrupted;
            journey.StatusReason = "location_deleted";
            journey.ModifiedAt = DateTimeOffset.UtcNow;

            var key = BuildJourneyKey(journeyId);
            await _journeyStore.SaveAsync(key, journey, cancellationToken: cancellationToken);

            // Publish transit.journey.interrupted event per FOUNDATION TENETS
            await PublishJourneyInterruptedEventAsync(journey, journey.RealmId, cancellationToken);

            // Publish client event
            await PublishJourneyUpdatedClientEventAsync(journey, cancellationToken);

            interruptedCount++;

            _logger.LogDebug("Interrupted active Redis journey {JourneyId} due to location {LocationId} deletion",
                journeyId, deletedLocationId);
        }

        if (interruptedCount > 0)
        {
            _logger.LogInformation("Interrupted {Count} active Redis journeys during location {LocationId} cleanup",
                interruptedCount, deletedLocationId);
        }

        return interruptedCount;
    }

    /// <summary>
    /// Scans Redis active journeys and abandons any belonging to the deleted entity.
    /// Character deletion permanently terminates journeys (Abandoned, not Interrupted).
    /// </summary>
    /// <param name="entityId">The deleted entity ID.</param>
    /// <param name="entityType">The entity type (e.g., "character").</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Number of Redis journeys abandoned.</returns>
    private async Task<int> ScanRedisJourneysForCharacterCleanupAsync(
        Guid entityId,
        string entityType,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.transit", "TransitService.ScanRedisJourneysForCharacterCleanupAsync");

        var journeyIndex = await _journeyIndexStore.GetAsync(JOURNEY_INDEX_KEY, cancellationToken);
        if (journeyIndex == null)
        {
            return 0;
        }

        var abandonedCount = 0;

        foreach (var journeyId in journeyIndex)
        {
            var journey = await _journeyStore.GetAsync(BuildJourneyKey(journeyId), cancellationToken: cancellationToken);
            if (journey == null) continue;
            if (journey.Status == JourneyStatus.Arrived || journey.Status == JourneyStatus.Abandoned) continue;

            if (journey.EntityId != entityId || journey.EntityType != entityType) continue;

            // Abandon the journey in Redis
            journey.Status = JourneyStatus.Abandoned;
            journey.StatusReason = "character_deleted";
            journey.ModifiedAt = DateTimeOffset.UtcNow;

            var key = BuildJourneyKey(journeyId);
            await _journeyStore.SaveAsync(key, journey, cancellationToken: cancellationToken);

            // Publish transit.journey.abandoned event per FOUNDATION TENETS
            await PublishJourneyAbandonedEventAsync(
                journey, journey.RealmId, journey.RealmId, journey.RealmId, cancellationToken);

            // Publish client event
            await PublishJourneyUpdatedClientEventAsync(journey, cancellationToken);

            abandonedCount++;

            _logger.LogDebug("Abandoned active Redis journey {JourneyId} due to character {EntityId} deletion",
                journeyId, entityId);
        }

        if (abandonedCount > 0)
        {
            _logger.LogInformation("Abandoned {Count} active Redis journeys during character {EntityId} cleanup",
                abandonedCount, entityId);
        }

        return abandonedCount;
    }

    #endregion
}
