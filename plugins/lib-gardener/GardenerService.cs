using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.GameSession;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Providers;
using BeyondImmersion.BannouService.Puppetmaster;
using BeyondImmersion.BannouService.Seed;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Gardener;

/// <summary>
/// Implementation of the Gardener service.
/// Orchestrates player garden navigation, scenario routing, progressive discovery,
/// and deployment phase management.
/// </summary>
/// <remarks>
/// <para>
/// The Gardener is an L4 GameFeatures service that manages the "Garden" experience --
/// a procedural discovery space where players encounter POIs (Points of Interest)
/// that lead into scenarios. Scenarios are backed by Game Sessions and award Seed
/// growth on completion. Internal naming uses "Garden" for clarity.
/// </para>
/// <para>
/// <b>FOUNDATION TENETS - PARTIAL CLASS REQUIRED:</b> This class MUST remain a partial class.
/// Event handlers and ISeedEvolutionListener are in GardenerServiceEvents.cs.
/// </para>
/// </remarks>
[BannouService("gardener", typeof(IGardenerService), lifetime: ServiceLifetime.Scoped, layer: ServiceLayer.GameFeatures)]
public partial class GardenerService : IGardenerService
{
    private readonly IMessageBus _messageBus;
    private readonly IStateStoreFactory _stateStoreFactory;
    private readonly ILogger<GardenerService> _logger;
    private readonly GardenerServiceConfiguration _configuration;
    private readonly IDistributedLockProvider _lockProvider;
    private readonly ISeedClient _seedClient;
    private readonly IGameSessionClient _gameSessionClient;
    private readonly IServiceProvider _serviceProvider;
    private readonly IEntitySessionRegistry _entitySessionRegistry;
    private readonly ITelemetryProvider _telemetryProvider;

    /// <summary>
    /// POI interaction result values are now the generated PoiInteractionResult enum
    /// (per IMPLEMENTATION TENETS type safety).
    /// </summary>

    /// <summary>
    /// Constructs the Gardener service with all required dependencies.
    /// </summary>
    public GardenerService(
        IMessageBus messageBus,
        IStateStoreFactory stateStoreFactory,
        ILogger<GardenerService> logger,
        GardenerServiceConfiguration configuration,
        IDistributedLockProvider lockProvider,
        IEventConsumer eventConsumer,
        ISeedClient seedClient,
        IGameSessionClient gameSessionClient,
        IServiceProvider serviceProvider,
        IEntitySessionRegistry entitySessionRegistry,
        ITelemetryProvider telemetryProvider)
    {
        _messageBus = messageBus;
        _stateStoreFactory = stateStoreFactory;
        _logger = logger;
        _configuration = configuration;
        _lockProvider = lockProvider;
        _seedClient = seedClient;
        _gameSessionClient = gameSessionClient;
        _serviceProvider = serviceProvider;
        _entitySessionRegistry = entitySessionRegistry;
        _telemetryProvider = telemetryProvider;

        RegisterEventConsumers(eventConsumer);
    }

    #region State Store Accessors

    private IStateStore<GardenInstanceModel>? _gardenStore;

    /// <summary>
    /// Redis-backed store for active garden instances.
    /// Key pattern: garden:{accountId}
    /// </summary>
    internal IStateStore<GardenInstanceModel> GardenStore =>
        _gardenStore ??= _stateStoreFactory.GetStore<GardenInstanceModel>(
            StateStoreDefinitions.GardenerGardenInstances);

    private IStateStore<PoiModel>? _poiStore;

    /// <summary>
    /// Redis-backed store for active POIs.
    /// Key pattern: poi:{gardenInstanceId}:{poiId}
    /// </summary>
    internal IStateStore<PoiModel> PoiStore =>
        _poiStore ??= _stateStoreFactory.GetStore<PoiModel>(
            StateStoreDefinitions.GardenerPois);

    private IJsonQueryableStateStore<ScenarioTemplateModel>? _templateStore;

    /// <summary>
    /// MySQL-backed store for scenario template definitions.
    /// Key pattern: template:{scenarioTemplateId}
    /// </summary>
    internal IJsonQueryableStateStore<ScenarioTemplateModel> TemplateStore =>
        _templateStore ??= _stateStoreFactory.GetJsonQueryableStore<ScenarioTemplateModel>(
            StateStoreDefinitions.GardenerScenarioTemplates);

    private IStateStore<ScenarioInstanceModel>? _scenarioStore;

    /// <summary>
    /// Redis-backed store for active scenario instances.
    /// Key pattern: scenario:{accountId}
    /// </summary>
    internal IStateStore<ScenarioInstanceModel> ScenarioStore =>
        _scenarioStore ??= _stateStoreFactory.GetStore<ScenarioInstanceModel>(
            StateStoreDefinitions.GardenerScenarioInstances);

    private IJsonQueryableStateStore<ScenarioHistoryModel>? _historyStore;

    /// <summary>
    /// MySQL-backed store for completed scenario history records.
    /// Key pattern: history:{scenarioInstanceId}
    /// </summary>
    internal IJsonQueryableStateStore<ScenarioHistoryModel> HistoryStore =>
        _historyStore ??= _stateStoreFactory.GetJsonQueryableStore<ScenarioHistoryModel>(
            StateStoreDefinitions.GardenerScenarioHistory);

    private IStateStore<DeploymentPhaseConfigModel>? _phaseStore;

    /// <summary>
    /// MySQL-backed store for deployment phase configuration.
    /// Key: phase:config (singleton)
    /// </summary>
    internal IStateStore<DeploymentPhaseConfigModel> PhaseStore =>
        _phaseStore ??= _stateStoreFactory.GetStore<DeploymentPhaseConfigModel>(
            StateStoreDefinitions.GardenerPhaseConfig);

    private ICacheableStateStore<GardenInstanceModel>? _gardenCacheStore;

    /// <summary>
    /// Cacheable store providing Redis set operations for tracking active instances.
    /// Used for maintaining tracking sets of active garden and scenario account IDs.
    /// </summary>
    internal ICacheableStateStore<GardenInstanceModel> GardenCacheStore =>
        _gardenCacheStore ??= _stateStoreFactory.GetCacheableStore<GardenInstanceModel>(
            StateStoreDefinitions.GardenerGardenInstances);

    #endregion

    #region Key Helpers

    private static string GardenKey(Guid accountId) => $"garden:{accountId}";
    private static string PoiKey(Guid gardenInstanceId, Guid poiId) => $"poi:{gardenInstanceId}:{poiId}";
    private static string ScenarioKey(Guid accountId) => $"scenario:{accountId}";
    private static string TemplateKey(Guid templateId) => $"template:{templateId}";
    private static string HistoryKey(Guid scenarioInstanceId) => $"history:{scenarioInstanceId}";
    private const string PhaseConfigKey = "phase:config";

    /// <summary>
    /// Redis set key tracking account IDs with active garden instances.
    /// Used by GardenerGardenOrchestratorWorker to iterate active gardens.
    /// </summary>
    internal const string ActiveGardensTrackingKey = "gardener:active-gardens";

    /// <summary>
    /// Redis set key tracking account IDs with active scenario instances.
    /// Used by GardenerScenarioLifecycleWorker to iterate active scenarios.
    /// </summary>
    internal const string ActiveScenariosTrackingKey = "gardener:active-scenarios";

    /// <summary>
    /// All deployment phases. Used to normalize null/empty AllowedPhases into explicit values
    /// so storage always contains queryable phase entries (no empty-array sentinel).
    /// </summary>
    private static readonly List<DeploymentPhase> AllDeploymentPhases =
        new() { DeploymentPhase.Alpha, DeploymentPhase.Beta, DeploymentPhase.Release };

    /// <summary>
    /// Converts API-provided AllowedPhases (nullable/empty = all phases) to storage representation
    /// (always explicit, never empty).
    /// </summary>
    private static List<DeploymentPhase> ToStorageAllowedPhases(ICollection<DeploymentPhase>? phases) =>
        phases == null || phases.Count == 0 ? AllDeploymentPhases.ToList() : phases.ToList();

    #endregion

    #region Garden Management

    /// <inheritdoc />
    public async Task<(StatusCodes, GardenStateResponse?)> EnterGardenAsync(
        EnterGardenRequest body, CancellationToken cancellationToken)
    {
        var lockOwner = $"enter-garden-{Guid.NewGuid():N}";
        await using var lockResult = await _lockProvider.LockAsync(
            StateStoreDefinitions.GardenerLock,
            $"garden:{body.AccountId}",
            lockOwner,
            _configuration.DistributedLockTimeoutSeconds,
            cancellationToken);

        if (!lockResult.Success)
        {
            _logger.LogWarning("Could not acquire lock for entering garden, account {AccountId}", body.AccountId);
            return (StatusCodes.Conflict, null);
        }

        var existing = await GardenStore.GetAsync(GardenKey(body.AccountId), cancellationToken);
        if (existing != null)
        {
            _logger.LogWarning("Account {AccountId} already has an active garden instance", body.AccountId);
            return (StatusCodes.Conflict, null);
        }

        // Find active guardian seed for this account
        var seedsResponse = await _seedClient.GetSeedsByOwnerAsync(
            new GetSeedsByOwnerRequest
            {
                OwnerId = body.AccountId,
                OwnerType = EntityType.Account,
                SeedTypeCode = _configuration.SeedTypeCode
            }, cancellationToken);

        var activeSeed = seedsResponse.Seeds.FirstOrDefault(s => s.Status == SeedStatus.Active);
        if (activeSeed == null)
        {
            _logger.LogInformation(
                "No active guardian seed found for account {AccountId}", body.AccountId);
            return (StatusCodes.NotFound, null);
        }

        var phaseConfig = await GetOrCreatePhaseConfigAsync(cancellationToken);

        var gardenInstanceId = Guid.NewGuid();
        var garden = new GardenInstanceModel
        {
            GardenInstanceId = gardenInstanceId,
            SeedId = activeSeed.SeedId,
            AccountId = body.AccountId,
            SessionId = body.SessionId,
            CreatedAt = DateTimeOffset.UtcNow,
            Phase = phaseConfig.CurrentPhase,
            CachedGrowthPhase = activeSeed.GrowthPhase,
            BondId = activeSeed.BondId,
            NeedsReEvaluation = true
        };

        await GardenStore.SaveAsync(GardenKey(body.AccountId), garden, cancellationToken: cancellationToken);

        // Track active garden instance for background worker iteration
        await GardenCacheStore.AddToSetAsync<Guid>(
            ActiveGardensTrackingKey, body.AccountId, cancellationToken: cancellationToken);

        await _messageBus.TryPublishAsync("gardener.garden.entered",
            new GardenerGardenEnteredEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                AccountId = body.AccountId,
                SeedId = activeSeed.SeedId,
                GardenInstanceId = gardenInstanceId
            }, cancellationToken: cancellationToken);

        _logger.LogInformation(
            "Created garden instance {GardenInstanceId} for account {AccountId} with seed {SeedId}",
            gardenInstanceId, body.AccountId, activeSeed.SeedId);

        return (StatusCodes.OK, MapToGardenStateResponse(garden, Array.Empty<PoiModel>()));
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, GardenStateResponse?)> GetGardenStateAsync(
        GetGardenStateRequest body, CancellationToken cancellationToken)
    {
        var garden = await GardenStore.GetAsync(GardenKey(body.AccountId), cancellationToken);
        if (garden == null)
            return (StatusCodes.NotFound, null);

        var pois = await LoadActivePoisAsync(garden, cancellationToken);
        return (StatusCodes.OK, MapToGardenStateResponse(garden, pois));
    }

    /// <inheritdoc />
    /// <remarks>
    /// No distributed lock: UpdatePosition is high-frequency (every tick) and idempotent
    /// (last-write-wins). A distributed lock would add unacceptable latency.
    /// Per IMPLEMENTATION TENETS multi-instance safety.
    /// </remarks>
    public async Task<(StatusCodes, PositionUpdateResponse?)> UpdatePositionAsync(
        UpdatePositionRequest body, CancellationToken cancellationToken)
    {
        var garden = await GardenStore.GetAsync(GardenKey(body.AccountId), cancellationToken);
        if (garden == null)
            return (StatusCodes.NotFound, null);

        var oldPosition = garden.Position;
        garden.Position = MapFromVec3(body.Position);
        if (body.Velocity != null)
            garden.Velocity = MapFromVec3(body.Velocity);

        // Accumulate drift metrics
        var distance = oldPosition.DistanceTo(garden.Position);
        garden.DriftMetrics.TotalDistance += distance;
        garden.DriftMetrics.DirectionalBiasX += garden.Position.X - oldPosition.X;
        garden.DriftMetrics.DirectionalBiasY += garden.Position.Y - oldPosition.Y;
        garden.DriftMetrics.DirectionalBiasZ += garden.Position.Z - oldPosition.Z;

        // Hesitation detection: if velocity is very low or direction reversed
        if (distance < (float)_configuration.HesitationDetectionThreshold)
            garden.DriftMetrics.HesitationCount++;

        // Check proximity triggers against active POIs
        var triggeredPois = new List<PoiSummary>();
        foreach (var poiId in garden.ActivePoiIds.ToList())
        {
            var poi = await PoiStore.GetAsync(PoiKey(garden.GardenInstanceId, poiId), cancellationToken);
            if (poi == null || poi.Status != PoiStatus.Active) continue;

            if (poi.TriggerMode == TriggerMode.Proximity)
            {
                var dist = garden.Position.DistanceTo(poi.Position);
                if (dist <= poi.TriggerRadius)
                {
                    poi.Status = PoiStatus.Entered;
                    await PoiStore.SaveAsync(
                        PoiKey(garden.GardenInstanceId, poiId), poi, cancellationToken: cancellationToken);
                    triggeredPois.Add(MapToPoiSummary(poi));

                    await _messageBus.TryPublishAsync("gardener.poi.entered",
                        new GardenerPoiEnteredEvent
                        {
                            EventId = Guid.NewGuid(),
                            Timestamp = DateTimeOffset.UtcNow,
                            AccountId = body.AccountId,
                            PoiId = poiId,
                            ScenarioTemplateId = poi.ScenarioTemplateId
                        }, cancellationToken: cancellationToken);
                }
            }
        }

        await GardenStore.SaveAsync(GardenKey(body.AccountId), garden, cancellationToken: cancellationToken);

        return (StatusCodes.OK, new PositionUpdateResponse
        {
            Acknowledged = true,
            TriggeredPois = triggeredPois.Count > 0 ? triggeredPois : null
        });
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, LeaveGardenResponse?)> LeaveGardenAsync(
        LeaveGardenRequest body, CancellationToken cancellationToken)
    {
        await using var lockResult = await _lockProvider.LockAsync(
            StateStoreDefinitions.GardenerLock,
            $"garden:{body.AccountId}",
            Guid.NewGuid().ToString(),
            _configuration.DistributedLockTimeoutSeconds,
            cancellationToken);

        if (!lockResult.Success)
        {
            _logger.LogWarning("Failed to acquire lock for garden leave, account {AccountId}", body.AccountId);
            return (StatusCodes.Conflict, null);
        }

        var garden = await GardenStore.GetAsync(GardenKey(body.AccountId), cancellationToken);
        if (garden == null)
            return (StatusCodes.NotFound, null);

        // Clean up all associated POIs
        foreach (var poiId in garden.ActivePoiIds)
        {
            await PoiStore.DeleteAsync(PoiKey(garden.GardenInstanceId, poiId), cancellationToken);
        }

        var sessionDuration = (float)(DateTimeOffset.UtcNow - garden.CreatedAt).TotalSeconds;
        await GardenStore.DeleteAsync(GardenKey(body.AccountId), cancellationToken);

        // Remove from tracking set
        await GardenCacheStore.RemoveFromSetAsync<Guid>(
            ActiveGardensTrackingKey, body.AccountId, cancellationToken);

        await _messageBus.TryPublishAsync("gardener.garden.left",
            new GardenerGardenLeftEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                AccountId = body.AccountId,
                GardenInstanceId = garden.GardenInstanceId,
                SessionDurationSeconds = sessionDuration
            }, cancellationToken: cancellationToken);

        _logger.LogInformation(
            "Garden instance {GardenInstanceId} ended for account {AccountId}, duration {Duration}s",
            garden.GardenInstanceId, body.AccountId, sessionDuration);

        return (StatusCodes.OK, new LeaveGardenResponse
        {
            AccountId = body.AccountId,
            SessionDurationSeconds = sessionDuration
        });
    }

    #endregion

    #region POI Interaction

    /// <inheritdoc />
    public async Task<(StatusCodes, ListPoisResponse?)> ListPoisAsync(
        ListPoisRequest body, CancellationToken cancellationToken)
    {
        var garden = await GardenStore.GetAsync(GardenKey(body.AccountId), cancellationToken);
        if (garden == null)
            return (StatusCodes.NotFound, null);

        var pois = await LoadActivePoisAsync(garden, cancellationToken);
        return (StatusCodes.OK, new ListPoisResponse
        {
            GardenInstanceId = garden.GardenInstanceId,
            Pois = pois.Select(MapToPoiSummary).ToList()
        });
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, PoiInteractionResponse?)> InteractWithPoiAsync(
        InteractWithPoiRequest body, CancellationToken cancellationToken)
    {
        await using var lockResult = await _lockProvider.LockAsync(
            StateStoreDefinitions.GardenerLock,
            $"garden:{body.AccountId}",
            Guid.NewGuid().ToString(),
            _configuration.DistributedLockTimeoutSeconds,
            cancellationToken);

        if (!lockResult.Success)
            return (StatusCodes.Conflict, null);

        var garden = await GardenStore.GetAsync(GardenKey(body.AccountId), cancellationToken);
        if (garden == null)
            return (StatusCodes.NotFound, null);

        var poi = await PoiStore.GetAsync(
            PoiKey(garden.GardenInstanceId, body.PoiId), cancellationToken);

        if (poi == null)
            return (StatusCodes.NotFound, null);

        if (poi.Status != PoiStatus.Active)
        {
            _logger.LogWarning(
                "POI {PoiId} has status {Status}, expected Active", body.PoiId, poi.Status);
            return (StatusCodes.BadRequest, null);
        }

        // Load the template to include in the response
        var template = await TemplateStore.GetAsync(
            TemplateKey(poi.ScenarioTemplateId), cancellationToken);

        PoiInteractionResult result;
        string? promptText = null;
        ICollection<string>? promptChoices = null;

        switch (poi.TriggerMode)
        {
            case TriggerMode.Prompted:
                result = PoiInteractionResult.ScenarioPrompt;
                promptText = template?.DisplayName;
                promptChoices = new List<string> { "Enter", "Decline" };
                break;

            case TriggerMode.Proximity:
            case TriggerMode.Interaction:
                result = PoiInteractionResult.ScenarioEnter;
                break;

            case TriggerMode.Forced:
                result = PoiInteractionResult.ScenarioEnter;
                break;

            default:
                result = PoiInteractionResult.PoiUpdate;
                break;
        }

        poi.Status = PoiStatus.Entered;
        await PoiStore.SaveAsync(
            PoiKey(garden.GardenInstanceId, body.PoiId), poi, cancellationToken: cancellationToken);

        await _messageBus.TryPublishAsync("gardener.poi.entered",
            new GardenerPoiEnteredEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                AccountId = body.AccountId,
                PoiId = body.PoiId,
                ScenarioTemplateId = poi.ScenarioTemplateId
            }, cancellationToken: cancellationToken);

        return (StatusCodes.OK, new PoiInteractionResponse
        {
            PoiId = body.PoiId,
            Result = result,
            ScenarioTemplateId = poi.ScenarioTemplateId,
            PromptText = promptText,
            PromptChoices = promptChoices
        });
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, DeclinePoiResponse?)> DeclinePoiAsync(
        DeclinePoiRequest body, CancellationToken cancellationToken)
    {
        await using var lockResult = await _lockProvider.LockAsync(
            StateStoreDefinitions.GardenerLock,
            $"garden:{body.AccountId}",
            Guid.NewGuid().ToString(),
            _configuration.DistributedLockTimeoutSeconds,
            cancellationToken);

        if (!lockResult.Success)
            return (StatusCodes.Conflict, null);

        var garden = await GardenStore.GetAsync(GardenKey(body.AccountId), cancellationToken);
        if (garden == null)
            return (StatusCodes.NotFound, null);

        var poi = await PoiStore.GetAsync(
            PoiKey(garden.GardenInstanceId, body.PoiId), cancellationToken);

        if (poi == null)
            return (StatusCodes.NotFound, null);

        if (poi.Status != PoiStatus.Active)
            return (StatusCodes.BadRequest, null);

        poi.Status = PoiStatus.Declined;
        await PoiStore.SaveAsync(
            PoiKey(garden.GardenInstanceId, body.PoiId), poi, cancellationToken: cancellationToken);

        // Track declined template for diversity scoring
        if (!garden.ScenarioHistory.Contains(poi.ScenarioTemplateId))
            garden.ScenarioHistory.Add(poi.ScenarioTemplateId);
        garden.NeedsReEvaluation = true;
        await GardenStore.SaveAsync(GardenKey(body.AccountId), garden, cancellationToken: cancellationToken);

        await _messageBus.TryPublishAsync("gardener.poi.declined",
            new GardenerPoiDeclinedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                AccountId = body.AccountId,
                PoiId = body.PoiId,
                ScenarioTemplateId = poi.ScenarioTemplateId
            }, cancellationToken: cancellationToken);

        return (StatusCodes.OK, new DeclinePoiResponse
        {
            PoiId = body.PoiId,
            Acknowledged = true
        });
    }

    #endregion

    #region Scenario Lifecycle

    /// <inheritdoc />
    public async Task<(StatusCodes, ScenarioStateResponse?)> EnterScenarioAsync(
        EnterScenarioRequest body, CancellationToken cancellationToken)
    {
        await using var lockResult = await _lockProvider.LockAsync(
            StateStoreDefinitions.GardenerLock,
            $"scenario:{body.AccountId}",
            Guid.NewGuid().ToString(),
            _configuration.DistributedLockTimeoutSeconds,
            cancellationToken);

        if (!lockResult.Success)
            return (StatusCodes.Conflict, null);

        var garden = await GardenStore.GetAsync(GardenKey(body.AccountId), cancellationToken);
        if (garden == null)
        {
            _logger.LogWarning("Account {AccountId} not in garden, cannot enter scenario", body.AccountId);
            return (StatusCodes.BadRequest, null);
        }

        // Check for existing active scenario
        var existingScenario = await ScenarioStore.GetAsync(
            ScenarioKey(body.AccountId), cancellationToken);
        if (existingScenario != null && existingScenario.Status == ScenarioStatus.Active)
        {
            _logger.LogWarning(
                "Account {AccountId} already has an active scenario {ScenarioId}",
                body.AccountId, existingScenario.ScenarioInstanceId);
            return (StatusCodes.Conflict, null);
        }

        var template = await TemplateStore.GetAsync(
            TemplateKey(body.ScenarioTemplateId), cancellationToken);
        if (template == null)
            return (StatusCodes.NotFound, null);

        if (template.Status != TemplateStatus.Active)
        {
            _logger.LogWarning(
                "Template {TemplateId} has status {Status}, expected Active",
                body.ScenarioTemplateId, template.Status);
            return (StatusCodes.BadRequest, null);
        }

        // Phase gating (empty AllowedPhases = unrestricted, allowed in all phases)
        var phaseConfig = await GetOrCreatePhaseConfigAsync(cancellationToken);
        if (template.AllowedPhases.Count > 0 && !template.AllowedPhases.Contains(phaseConfig.CurrentPhase))
        {
            _logger.LogInformation(
                "Template {TemplateId} not allowed in current phase {Phase}",
                body.ScenarioTemplateId, phaseConfig.CurrentPhase);
            return (StatusCodes.BadRequest, null);
        }

        // Global scenario capacity check
        var activeScenarioCount = (int)await GardenCacheStore.SetCountAsync(
            ActiveScenariosTrackingKey, cancellationToken);
        if (activeScenarioCount >= phaseConfig.MaxConcurrentScenariosGlobal)
        {
            _logger.LogInformation(
                "Global scenario capacity reached ({Active}/{Max})",
                activeScenarioCount, phaseConfig.MaxConcurrentScenariosGlobal);
            return (StatusCodes.ServiceUnavailable, null);
        }

        // Create backing game session
        GameSessionResponse gameSession;
        try
        {
            gameSession = await _gameSessionClient.CreateGameSessionAsync(
                new CreateGameSessionRequest
                {
                    GameType = "gardener-scenario",
                    MaxPlayers = template.Multiplayer?.MaxPlayers ?? 1,
                    SessionName = $"Scenario: {template.DisplayName}",
                    SessionType = SessionType.Matchmade,
                    OwnerId = body.AccountId,
                    ExpectedPlayers = new List<Guid> { body.AccountId },
                    ReservationTtlSeconds = (int)(_configuration.ScenarioTimeoutMinutes * 60)
                }, cancellationToken);
        }
        catch (ApiException ex)
        {
            _logger.LogError(ex,
                "Failed to create game session for scenario {TemplateId}, account {AccountId}: {StatusCode}",
                body.ScenarioTemplateId, body.AccountId, ex.StatusCode);
            await _messageBus.TryPublishErrorAsync(
                "gardener", "EnterScenario", "game_session_creation_failed", ex.Message,
                dependency: "game-session", endpoint: "post:/game-session/create",
                details: new { TemplateId = body.ScenarioTemplateId, AccountId = body.AccountId });
            return (StatusCodes.ServiceUnavailable, null);
        }

        var scenarioInstanceId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var scenario = new ScenarioInstanceModel
        {
            ScenarioInstanceId = scenarioInstanceId,
            ScenarioTemplateId = body.ScenarioTemplateId,
            GameSessionId = gameSession.SessionId,
            ConnectivityMode = template.ConnectivityMode,
            Status = ScenarioStatus.Active,
            CreatedAt = now,
            LastActivityAt = now,
            ChainDepth = 0,
            Participants = new List<ScenarioParticipantModel>
            {
                new()
                {
                    SeedId = garden.SeedId,
                    AccountId = body.AccountId,
                    SessionId = garden.SessionId,
                    JoinedAt = now,
                    Role = ScenarioParticipantRole.Primary
                }
            }
        };

        await ScenarioStore.SaveAsync(ScenarioKey(body.AccountId), scenario, cancellationToken: cancellationToken);

        // Update tracking sets: leaving garden, entering scenario
        await GardenCacheStore.RemoveFromSetAsync<Guid>(
            ActiveGardensTrackingKey, body.AccountId, cancellationToken);
        await GardenCacheStore.AddToSetAsync<Guid>(
            ActiveScenariosTrackingKey, body.AccountId, cancellationToken: cancellationToken);

        // Clean up garden instance -- player leaves the garden to enter the scenario
        foreach (var poiId in garden.ActivePoiIds)
        {
            await PoiStore.DeleteAsync(PoiKey(garden.GardenInstanceId, poiId), cancellationToken);
        }
        await GardenStore.DeleteAsync(GardenKey(body.AccountId), cancellationToken);

        await _messageBus.TryPublishAsync("gardener.scenario.started",
            new GardenerScenarioStartedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                ScenarioInstanceId = scenarioInstanceId,
                ScenarioTemplateId = body.ScenarioTemplateId,
                GameSessionId = gameSession.SessionId,
                AccountId = body.AccountId,
                SeedId = garden.SeedId
            }, cancellationToken: cancellationToken);

        // Notify Puppetmaster if available (L4 soft dependency)
        var puppetmasterClient = _serviceProvider.GetService<IPuppetmasterClient>();
        if (puppetmasterClient != null && template.Content?.BehaviorDocumentId != null)
        {
            _logger.LogDebug(
                "Notifying Puppetmaster of scenario {ScenarioId} with behavior {BehaviorDoc}",
                scenarioInstanceId, template.Content.BehaviorDocumentId);
        }

        _logger.LogInformation(
            "Scenario {ScenarioId} started for account {AccountId}, template {TemplateCode}",
            scenarioInstanceId, body.AccountId, template.Code);

        return (StatusCodes.OK, MapToScenarioStateResponse(scenario));
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, ScenarioStateResponse?)> GetScenarioStateAsync(
        GetScenarioStateRequest body, CancellationToken cancellationToken)
    {
        var scenario = await ScenarioStore.GetAsync(ScenarioKey(body.AccountId), cancellationToken);
        if (scenario == null)
            return (StatusCodes.NotFound, null);

        return (StatusCodes.OK, MapToScenarioStateResponse(scenario));
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, ScenarioCompletionResponse?)> CompleteScenarioAsync(
        CompleteScenarioRequest body, CancellationToken cancellationToken)
    {
        await using var lockResult = await _lockProvider.LockAsync(
            StateStoreDefinitions.GardenerLock,
            $"scenario:{body.AccountId}",
            Guid.NewGuid().ToString(),
            _configuration.DistributedLockTimeoutSeconds,
            cancellationToken);

        if (!lockResult.Success)
            return (StatusCodes.Conflict, null);

        var scenario = await ScenarioStore.GetAsync(ScenarioKey(body.AccountId), cancellationToken);
        if (scenario == null)
            return (StatusCodes.NotFound, null);

        if (scenario.ScenarioInstanceId != body.ScenarioInstanceId)
        {
            _logger.LogWarning(
                "Scenario ID mismatch: expected {Expected}, got {Actual}",
                scenario.ScenarioInstanceId, body.ScenarioInstanceId);
            return (StatusCodes.BadRequest, null);
        }

        if (scenario.Status != ScenarioStatus.Active)
            return (StatusCodes.BadRequest, null);

        var template = await TemplateStore.GetAsync(
            TemplateKey(scenario.ScenarioTemplateId), cancellationToken);

        // Calculate growth awards per IMPLEMENTATION TENETS
        var growthAwarded = await CalculateAndAwardGrowthAsync(
            scenario, template, fullCompletion: true, cancellationToken);

        scenario.Status = ScenarioStatus.Completed;
        scenario.CompletedAt = DateTimeOffset.UtcNow;
        scenario.GrowthAwarded = growthAwarded;
        await ScenarioStore.SaveAsync(ScenarioKey(body.AccountId), scenario, cancellationToken: cancellationToken);

        // Move to durable history
        await WriteScenarioHistoryAsync(scenario, template, cancellationToken);

        // Clean up from Redis
        await ScenarioStore.DeleteAsync(ScenarioKey(body.AccountId), cancellationToken);

        // Remove from tracking set
        await GardenCacheStore.RemoveFromSetAsync<Guid>(
            ActiveScenariosTrackingKey, body.AccountId, cancellationToken);

        // Clean up game session by having participant leave
        await TryCleanupGameSessionAsync(scenario, cancellationToken);

        await _messageBus.TryPublishAsync("gardener.scenario.completed",
            new GardenerScenarioCompletedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                ScenarioInstanceId = scenario.ScenarioInstanceId,
                ScenarioTemplateId = scenario.ScenarioTemplateId,
                AccountId = body.AccountId,
                GrowthAwarded = growthAwarded
            }, cancellationToken: cancellationToken);

        _logger.LogInformation(
            "Scenario {ScenarioId} completed for account {AccountId}, growth awarded in {DomainCount} domains",
            scenario.ScenarioInstanceId, body.AccountId, growthAwarded.Count);

        return (StatusCodes.OK, new ScenarioCompletionResponse
        {
            ScenarioInstanceId = scenario.ScenarioInstanceId,
            GrowthAwarded = growthAwarded,
            ReturnToGarden = true
        });
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, AbandonScenarioResponse?)> AbandonScenarioAsync(
        AbandonScenarioRequest body, CancellationToken cancellationToken)
    {
        await using var lockResult = await _lockProvider.LockAsync(
            StateStoreDefinitions.GardenerLock,
            $"scenario:{body.AccountId}",
            Guid.NewGuid().ToString(),
            _configuration.DistributedLockTimeoutSeconds,
            cancellationToken);

        if (!lockResult.Success)
            return (StatusCodes.Conflict, null);

        var scenario = await ScenarioStore.GetAsync(ScenarioKey(body.AccountId), cancellationToken);
        if (scenario == null)
            return (StatusCodes.NotFound, null);

        if (scenario.ScenarioInstanceId != body.ScenarioInstanceId)
            return (StatusCodes.BadRequest, null);

        if (scenario.Status != ScenarioStatus.Active)
            return (StatusCodes.BadRequest, null);

        var template = await TemplateStore.GetAsync(
            TemplateKey(scenario.ScenarioTemplateId), cancellationToken);

        // Calculate partial growth based on time spent
        var partialGrowth = await CalculateAndAwardGrowthAsync(
            scenario, template, fullCompletion: false, cancellationToken);

        scenario.Status = ScenarioStatus.Abandoned;
        scenario.CompletedAt = DateTimeOffset.UtcNow;
        scenario.GrowthAwarded = partialGrowth;

        // Move to durable history
        await WriteScenarioHistoryAsync(scenario, template, cancellationToken);

        // Clean up from Redis
        await ScenarioStore.DeleteAsync(ScenarioKey(body.AccountId), cancellationToken);

        // Remove from tracking set
        await GardenCacheStore.RemoveFromSetAsync<Guid>(
            ActiveScenariosTrackingKey, body.AccountId, cancellationToken);

        await TryCleanupGameSessionAsync(scenario, cancellationToken);

        await _messageBus.TryPublishAsync("gardener.scenario.abandoned",
            new GardenerScenarioAbandonedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                ScenarioInstanceId = scenario.ScenarioInstanceId,
                AccountId = body.AccountId
            }, cancellationToken: cancellationToken);

        _logger.LogInformation(
            "Scenario {ScenarioId} abandoned by account {AccountId}",
            scenario.ScenarioInstanceId, body.AccountId);

        return (StatusCodes.OK, new AbandonScenarioResponse
        {
            ScenarioInstanceId = scenario.ScenarioInstanceId,
            PartialGrowthAwarded = partialGrowth
        });
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, ScenarioStateResponse?)> ChainScenarioAsync(
        ChainScenarioRequest body, CancellationToken cancellationToken)
    {
        await using var lockResult = await _lockProvider.LockAsync(
            StateStoreDefinitions.GardenerLock,
            $"scenario:{body.AccountId}",
            Guid.NewGuid().ToString(),
            _configuration.DistributedLockTimeoutSeconds,
            cancellationToken);

        if (!lockResult.Success)
            return (StatusCodes.Conflict, null);

        var currentScenario = await ScenarioStore.GetAsync(
            ScenarioKey(body.AccountId), cancellationToken);
        if (currentScenario == null)
            return (StatusCodes.NotFound, null);

        if (currentScenario.ScenarioInstanceId != body.CurrentScenarioInstanceId)
            return (StatusCodes.BadRequest, null);

        if (currentScenario.Status != ScenarioStatus.Active)
            return (StatusCodes.BadRequest, null);

        // Load current template to check chaining rules
        var currentTemplate = await TemplateStore.GetAsync(
            TemplateKey(currentScenario.ScenarioTemplateId), cancellationToken);

        var targetTemplate = await TemplateStore.GetAsync(
            TemplateKey(body.TargetTemplateId), cancellationToken);
        if (targetTemplate == null || targetTemplate.Status != TemplateStatus.Active)
            return (StatusCodes.NotFound, null);

        // Validate chaining rules
        if (currentTemplate?.Chaining?.LeadsTo == null ||
            !currentTemplate.Chaining.LeadsTo.Contains(targetTemplate.Code))
        {
            _logger.LogWarning(
                "Template {CurrentCode} does not chain to {TargetCode}",
                currentTemplate?.Code, targetTemplate.Code);
            return (StatusCodes.BadRequest, null);
        }

        var maxChainDepth = currentTemplate.Chaining.MaxChainDepth;
        if (currentScenario.ChainDepth + 1 >= maxChainDepth)
        {
            _logger.LogWarning(
                "Chain depth {Depth} would exceed max {Max}",
                currentScenario.ChainDepth + 1, maxChainDepth);
            return (StatusCodes.BadRequest, null);
        }

        // Complete current scenario with growth
        var growthAwarded = await CalculateAndAwardGrowthAsync(
            currentScenario, currentTemplate, fullCompletion: true, cancellationToken);
        currentScenario.Status = ScenarioStatus.Completed;
        currentScenario.CompletedAt = DateTimeOffset.UtcNow;
        currentScenario.GrowthAwarded = growthAwarded;
        await WriteScenarioHistoryAsync(currentScenario, currentTemplate, cancellationToken);

        // Create new chained scenario
        var newScenarioId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var newScenario = new ScenarioInstanceModel
        {
            ScenarioInstanceId = newScenarioId,
            ScenarioTemplateId = body.TargetTemplateId,
            GameSessionId = currentScenario.GameSessionId,
            ConnectivityMode = targetTemplate.ConnectivityMode,
            Status = ScenarioStatus.Active,
            CreatedAt = now,
            LastActivityAt = now,
            ChainedFrom = currentScenario.ScenarioInstanceId,
            ChainDepth = currentScenario.ChainDepth + 1,
            Participants = currentScenario.Participants
        };

        await ScenarioStore.SaveAsync(ScenarioKey(body.AccountId), newScenario, cancellationToken: cancellationToken);

        await _messageBus.TryPublishAsync("gardener.scenario.chained",
            new GardenerScenarioChainedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                PreviousScenarioInstanceId = currentScenario.ScenarioInstanceId,
                NewScenarioInstanceId = newScenarioId,
                AccountId = body.AccountId,
                ChainDepth = newScenario.ChainDepth
            }, cancellationToken: cancellationToken);

        _logger.LogInformation(
            "Scenario chained: {PrevId} -> {NewId} (depth {Depth}) for account {AccountId}",
            currentScenario.ScenarioInstanceId, newScenarioId,
            newScenario.ChainDepth, body.AccountId);

        return (StatusCodes.OK, MapToScenarioStateResponse(newScenario));
    }

    #endregion

    #region Template Management

    /// <inheritdoc />
    public async Task<(StatusCodes, ScenarioTemplateResponse?)> CreateTemplateAsync(
        CreateTemplateRequest body, CancellationToken cancellationToken)
    {
        // Check code uniqueness via JSON query
        var codeCheck = await TemplateStore.JsonQueryPagedAsync(
            new List<QueryCondition>
            {
                new QueryCondition { Path = "$.ScenarioTemplateId", Operator = QueryOperator.Exists, Value = true },
                new QueryCondition { Path = "$.Code", Operator = QueryOperator.Equals, Value = body.Code }
            }, 1, 1, cancellationToken: cancellationToken);

        if (codeCheck.TotalCount > 0)
        {
            _logger.LogWarning("Template code {Code} already exists", body.Code);
            return (StatusCodes.Conflict, null);
        }

        var templateId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var template = new ScenarioTemplateModel
        {
            ScenarioTemplateId = templateId,
            Code = body.Code,
            DisplayName = body.DisplayName,
            Description = body.Description,
            Category = body.Category,
            Subcategory = body.Subcategory,
            ConnectivityMode = body.ConnectivityMode,
            DomainWeights = body.DomainWeights.Select(dw => new DomainWeightModel
            {
                Domain = dw.Domain,
                Weight = dw.Weight
            }).ToList(),
            MinGrowthPhase = body.MinGrowthPhase,
            EstimatedDurationMinutes = body.EstimatedDurationMinutes,
            AllowedPhases = ToStorageAllowedPhases(body.AllowedPhases),
            MaxConcurrentInstances = body.MaxConcurrentInstances,
            Prerequisites = body.Prerequisites != null ? MapFromPrerequisites(body.Prerequisites) : null,
            Chaining = body.Chaining != null ? MapFromChaining(body.Chaining) : null,
            Multiplayer = body.Multiplayer != null ? MapFromMultiplayer(body.Multiplayer) : null,
            Content = body.Content != null ? MapFromContent(body.Content) : null,
            Status = TemplateStatus.Active,
            CreatedAt = now,
            UpdatedAt = now
        };

        await TemplateStore.SaveAsync(TemplateKey(templateId), template, cancellationToken: cancellationToken);

        await _messageBus.TryPublishAsync("scenario-template.created",
            new ScenarioTemplateCreatedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                ScenarioTemplateId = template.ScenarioTemplateId,
                Code = template.Code,
                DisplayName = template.DisplayName,
                Description = template.Description,
                Category = template.Category,
                ConnectivityMode = template.ConnectivityMode,
                Status = template.Status,
                CreatedAt = now,
                UpdatedAt = now
            }, cancellationToken: cancellationToken);

        _logger.LogInformation(
            "Created scenario template {TemplateId} with code {Code}", templateId, body.Code);

        return (StatusCodes.OK, MapToTemplateResponse(template));
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, ScenarioTemplateResponse?)> GetTemplateAsync(
        GetTemplateRequest body, CancellationToken cancellationToken)
    {
        var template = await TemplateStore.GetAsync(
            TemplateKey(body.ScenarioTemplateId), cancellationToken);
        if (template == null)
            return (StatusCodes.NotFound, null);

        return (StatusCodes.OK, MapToTemplateResponse(template));
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, ScenarioTemplateResponse?)> GetTemplateByCodeAsync(
        GetTemplateByCodeRequest body, CancellationToken cancellationToken)
    {
        var result = await TemplateStore.JsonQueryPagedAsync(
            new List<QueryCondition>
            {
                new QueryCondition { Path = "$.ScenarioTemplateId", Operator = QueryOperator.Exists, Value = true },
                new QueryCondition { Path = "$.Code", Operator = QueryOperator.Equals, Value = body.Code }
            }, 1, 1, cancellationToken: cancellationToken);

        var item = result.Items.FirstOrDefault();
        if (item == null)
            return (StatusCodes.NotFound, null);

        return (StatusCodes.OK, MapToTemplateResponse(item.Value));
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, ListTemplatesResponse?)> ListTemplatesAsync(
        ListTemplatesRequest body, CancellationToken cancellationToken)
    {
        var conditions = new List<QueryCondition>
        {
            new QueryCondition { Path = "$.ScenarioTemplateId", Operator = QueryOperator.Exists, Value = true }
        };

        if (body.Category != null)
            conditions.Add(new QueryCondition { Path = "$.Category", Operator = QueryOperator.Equals, Value = body.Category.Value.ToString() });

        if (body.ConnectivityMode != null)
            conditions.Add(new QueryCondition
            {
                Path = "$.ConnectivityMode",
                Operator = QueryOperator.Equals,
                Value = body.ConnectivityMode.Value.ToString()
            });

        if (body.Status != null)
            conditions.Add(new QueryCondition { Path = "$.Status", Operator = QueryOperator.Equals, Value = body.Status.Value.ToString() });

        // AllowedPhases always contains explicit values (no empty-array sentinel),
        // so JSON_CONTAINS query works directly via QueryOperator.In with a scalar value
        if (body.DeploymentPhase != null)
            conditions.Add(new QueryCondition { Path = "$.AllowedPhases", Operator = QueryOperator.In, Value = body.DeploymentPhase.Value.ToString() });

        var result = await TemplateStore.JsonQueryPagedAsync(
            conditions, body.Page, body.PageSize, cancellationToken: cancellationToken);

        return (StatusCodes.OK, new ListTemplatesResponse
        {
            Templates = result.Items.Select(i => MapToTemplateResponse(i.Value)).ToList(),
            TotalCount = (int)result.TotalCount,
            Page = body.Page,
            PageSize = body.PageSize
        });
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, ScenarioTemplateResponse?)> UpdateTemplateAsync(
        UpdateTemplateRequest body, CancellationToken cancellationToken)
    {
        await using var lockResult = await _lockProvider.LockAsync(
            StateStoreDefinitions.GardenerLock,
            $"template:{body.ScenarioTemplateId}",
            Guid.NewGuid().ToString(),
            _configuration.DistributedLockTimeoutSeconds,
            cancellationToken);

        if (!lockResult.Success)
            return (StatusCodes.Conflict, null);

        var template = await TemplateStore.GetAsync(
            TemplateKey(body.ScenarioTemplateId), cancellationToken);
        if (template == null)
            return (StatusCodes.NotFound, null);

        // Update only provided (non-null) fields
        if (body.DisplayName != null)
            template.DisplayName = body.DisplayName;
        if (body.Description != null)
            template.Description = body.Description;
        if (body.DomainWeights != null)
            template.DomainWeights = body.DomainWeights.Select(dw =>
                new DomainWeightModel { Domain = dw.Domain, Weight = dw.Weight }).ToList();
        if (body.MaxConcurrentInstances != null)
            template.MaxConcurrentInstances = body.MaxConcurrentInstances.Value;
        if (body.Prerequisites != null)
            template.Prerequisites = MapFromPrerequisites(body.Prerequisites);
        if (body.Chaining != null)
            template.Chaining = MapFromChaining(body.Chaining);
        if (body.Multiplayer != null)
            template.Multiplayer = MapFromMultiplayer(body.Multiplayer);
        if (body.Content != null)
            template.Content = MapFromContent(body.Content);

        template.UpdatedAt = DateTimeOffset.UtcNow;
        await TemplateStore.SaveAsync(TemplateKey(body.ScenarioTemplateId), template, cancellationToken: cancellationToken);

        await _messageBus.TryPublishAsync("scenario-template.updated",
            new ScenarioTemplateUpdatedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                ScenarioTemplateId = template.ScenarioTemplateId,
                Code = template.Code,
                DisplayName = template.DisplayName,
                Description = template.Description,
                Category = template.Category,
                ConnectivityMode = template.ConnectivityMode,
                Status = template.Status,
                CreatedAt = template.CreatedAt,
                UpdatedAt = template.UpdatedAt
            }, cancellationToken: cancellationToken);

        _logger.LogInformation(
            "Updated scenario template {TemplateId}", body.ScenarioTemplateId);

        return (StatusCodes.OK, MapToTemplateResponse(template));
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, ScenarioTemplateResponse?)> DeprecateTemplateAsync(
        DeprecateTemplateRequest body, CancellationToken cancellationToken)
    {
        await using var lockResult = await _lockProvider.LockAsync(
            StateStoreDefinitions.GardenerLock,
            $"template:{body.ScenarioTemplateId}",
            Guid.NewGuid().ToString(),
            _configuration.DistributedLockTimeoutSeconds,
            cancellationToken);

        if (!lockResult.Success)
            return (StatusCodes.Conflict, null);

        var template = await TemplateStore.GetAsync(
            TemplateKey(body.ScenarioTemplateId), cancellationToken);
        if (template == null)
            return (StatusCodes.NotFound, null);

        template.Status = TemplateStatus.Deprecated;
        template.UpdatedAt = DateTimeOffset.UtcNow;
        await TemplateStore.SaveAsync(TemplateKey(body.ScenarioTemplateId), template, cancellationToken: cancellationToken);

        await _messageBus.TryPublishAsync("scenario-template.updated",
            new ScenarioTemplateUpdatedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                ScenarioTemplateId = template.ScenarioTemplateId,
                Code = template.Code,
                DisplayName = template.DisplayName,
                Description = template.Description,
                Category = template.Category,
                ConnectivityMode = template.ConnectivityMode,
                Status = template.Status,
                CreatedAt = template.CreatedAt,
                UpdatedAt = template.UpdatedAt
            }, cancellationToken: cancellationToken);

        _logger.LogInformation(
            "Deprecated scenario template {TemplateId} ({Code})",
            body.ScenarioTemplateId, template.Code);

        return (StatusCodes.OK, MapToTemplateResponse(template));
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, ScenarioTemplateResponse?)> DeleteTemplateAsync(
        DeleteTemplateRequest body, CancellationToken cancellationToken)
    {
        await using var lockResult = await _lockProvider.LockAsync(
            StateStoreDefinitions.GardenerLock,
            $"template:{body.ScenarioTemplateId}",
            Guid.NewGuid().ToString(),
            _configuration.DistributedLockTimeoutSeconds,
            cancellationToken);

        if (!lockResult.Success)
            return (StatusCodes.Conflict, null);

        var template = await TemplateStore.GetAsync(
            TemplateKey(body.ScenarioTemplateId), cancellationToken);
        if (template == null)
            return (StatusCodes.NotFound, null);

        if (template.Status != TemplateStatus.Deprecated)
            return (StatusCodes.Conflict, null);

        await TemplateStore.DeleteAsync(TemplateKey(body.ScenarioTemplateId), cancellationToken);

        await _messageBus.TryPublishAsync("scenario-template.deleted",
            new ScenarioTemplateDeletedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                ScenarioTemplateId = template.ScenarioTemplateId,
                Code = template.Code,
                DisplayName = template.DisplayName,
                Description = template.Description,
                Category = template.Category,
                ConnectivityMode = template.ConnectivityMode,
                Status = template.Status,
                CreatedAt = template.CreatedAt,
                UpdatedAt = template.UpdatedAt
            }, cancellationToken: cancellationToken);

        _logger.LogInformation(
            "Deleted scenario template {TemplateId} ({Code})",
            body.ScenarioTemplateId, template.Code);

        return (StatusCodes.OK, MapToTemplateResponse(template));
    }

    #endregion

    #region Phase Management

    /// <inheritdoc />
    public async Task<(StatusCodes, PhaseConfigResponse?)> GetPhaseConfigAsync(
        GetPhaseConfigRequest body, CancellationToken cancellationToken)
    {
        var config = await GetOrCreatePhaseConfigAsync(cancellationToken);
        return (StatusCodes.OK, MapToPhaseConfigResponse(config));
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, PhaseConfigResponse?)> UpdatePhaseConfigAsync(
        UpdatePhaseConfigRequest body, CancellationToken cancellationToken)
    {
        await using var lockResult = await _lockProvider.LockAsync(
            StateStoreDefinitions.GardenerLock,
            "phase",
            Guid.NewGuid().ToString(),
            _configuration.DistributedLockTimeoutSeconds,
            cancellationToken);

        if (!lockResult.Success)
            return (StatusCodes.Conflict, null);

        var config = await GetOrCreatePhaseConfigAsync(cancellationToken);
        var previousPhase = config.CurrentPhase;

        if (body.CurrentPhase != null)
            config.CurrentPhase = body.CurrentPhase.Value;
        if (body.MaxConcurrentScenariosGlobal != null)
            config.MaxConcurrentScenariosGlobal = body.MaxConcurrentScenariosGlobal.Value;
        if (body.PersistentEntryEnabled != null)
            config.PersistentEntryEnabled = body.PersistentEntryEnabled.Value;
        if (body.GardenMinigamesEnabled != null)
            config.GardenMinigamesEnabled = body.GardenMinigamesEnabled.Value;

        config.UpdatedAt = DateTimeOffset.UtcNow;
        await PhaseStore.SaveAsync(PhaseConfigKey, config, cancellationToken: cancellationToken);

        // Publish phase change event if phase changed
        if (body.CurrentPhase != null && body.CurrentPhase.Value != previousPhase)
        {
            await _messageBus.TryPublishAsync("gardener.phase.changed",
                new GardenerPhaseChangedEvent
                {
                    EventId = Guid.NewGuid(),
                    Timestamp = DateTimeOffset.UtcNow,
                    PreviousPhase = previousPhase,
                    NewPhase = body.CurrentPhase.Value
                }, cancellationToken: cancellationToken);

            _logger.LogInformation(
                "Deployment phase changed from {PreviousPhase} to {NewPhase}",
                previousPhase, body.CurrentPhase.Value);
        }

        return (StatusCodes.OK, MapToPhaseConfigResponse(config));
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, PhaseMetricsResponse?)> GetPhaseMetricsAsync(
        GetPhaseMetricsRequest body, CancellationToken cancellationToken)
    {
        var config = await GetOrCreatePhaseConfigAsync(cancellationToken);

        // Count active instances from Redis tracking sets maintained by Enter/Leave operations
        var activeGardenCount = (int)await GardenCacheStore.SetCountAsync(
            ActiveGardensTrackingKey, cancellationToken);
        var activeScenarioCount = (int)await GardenCacheStore.SetCountAsync(
            ActiveScenariosTrackingKey, cancellationToken);

        // Phase metrics are best-effort counts from available data
        var utilization = config.MaxConcurrentScenariosGlobal > 0
            ? (float)activeScenarioCount / config.MaxConcurrentScenariosGlobal
            : 0f;

        return (StatusCodes.OK, new PhaseMetricsResponse
        {
            CurrentPhase = config.CurrentPhase,
            ActiveGardenInstances = activeGardenCount,
            ActiveScenarioInstances = activeScenarioCount,
            ScenarioCapacityUtilization = utilization
        });
    }

    #endregion

    #region Bond Features

    /// <inheritdoc />
    public async Task<(StatusCodes, ScenarioStateResponse?)> EnterScenarioTogetherAsync(
        EnterTogetherRequest body, CancellationToken cancellationToken)
    {
        await using var lockResult = await _lockProvider.LockAsync(
            StateStoreDefinitions.GardenerLock,
            $"bond-scenario:{body.BondId}",
            Guid.NewGuid().ToString(),
            _configuration.DistributedLockTimeoutSeconds,
            cancellationToken);

        if (!lockResult.Success)
            return (StatusCodes.Conflict, null);

        if (!_configuration.BondSharedGardenEnabled)
        {
            _logger.LogDebug("Bond shared garden is disabled");
            return (StatusCodes.BadRequest, null);
        }

        // Load bond to find both participants
        BondResponse bond;
        try
        {
            bond = await _seedClient.GetBondAsync(
                new GetBondRequest { BondId = body.BondId }, cancellationToken);
        }
        catch (ApiException ex) when (ex.StatusCode == 404)
        {
            _logger.LogWarning("Bond {BondId} not found", body.BondId);
            return (StatusCodes.NotFound, null);
        }

        // Resolve participant seed IDs to owner account IDs
        var partnerAccountIds = new List<Guid>();
        foreach (var participant in bond.Participants)
        {
            var seed = await _seedClient.GetSeedAsync(
                new GetSeedRequest { SeedId = participant.SeedId }, cancellationToken);
            partnerAccountIds.Add(seed.OwnerId);
        }

        if (partnerAccountIds.Count < 2)
        {
            _logger.LogWarning("Bond {BondId} has fewer than 2 partners", body.BondId);
            return (StatusCodes.BadRequest, null);
        }

        // Verify both participants are in the garden
        var gardens = new List<GardenInstanceModel>();
        foreach (var accountId in partnerAccountIds)
        {
            var garden = await GardenStore.GetAsync(GardenKey(accountId), cancellationToken);
            if (garden == null)
            {
                _logger.LogWarning(
                    "Account {AccountId} not in garden for bond scenario", accountId);
                return (StatusCodes.BadRequest, null);
            }
            gardens.Add(garden);
        }

        var template = await TemplateStore.GetAsync(
            TemplateKey(body.ScenarioTemplateId), cancellationToken);
        if (template == null)
            return (StatusCodes.NotFound, null);

        if (template.Multiplayer == null || template.Multiplayer.MaxPlayers < 2)
        {
            _logger.LogWarning(
                "Template {TemplateId} does not support multiplayer", body.ScenarioTemplateId);
            return (StatusCodes.BadRequest, null);
        }

        // Phase gating (empty AllowedPhases = unrestricted, allowed in all phases)
        var phaseConfig = await GetOrCreatePhaseConfigAsync(cancellationToken);
        if (template.AllowedPhases.Count > 0 && !template.AllowedPhases.Contains(phaseConfig.CurrentPhase))
        {
            _logger.LogInformation(
                "Template {TemplateId} not allowed in current phase {Phase}",
                body.ScenarioTemplateId, phaseConfig.CurrentPhase);
            return (StatusCodes.BadRequest, null);
        }

        // Global scenario capacity check
        var activeScenarioCount = (int)await GardenCacheStore.SetCountAsync(
            ActiveScenariosTrackingKey, cancellationToken);
        if (activeScenarioCount >= phaseConfig.MaxConcurrentScenariosGlobal)
        {
            _logger.LogInformation(
                "Global scenario capacity reached ({Active}/{Max})",
                activeScenarioCount, phaseConfig.MaxConcurrentScenariosGlobal);
            return (StatusCodes.ServiceUnavailable, null);
        }

        // Create shared game session
        GameSessionResponse gameSession;
        try
        {
            gameSession = await _gameSessionClient.CreateGameSessionAsync(
                new CreateGameSessionRequest
                {
                    GameType = "gardener-scenario",
                    MaxPlayers = template.Multiplayer.MaxPlayers,
                    SessionName = $"Bond Scenario: {template.DisplayName}",
                    SessionType = SessionType.Matchmade,
                    OwnerId = partnerAccountIds[0],
                    ExpectedPlayers = partnerAccountIds,
                    ReservationTtlSeconds = (int)(_configuration.ScenarioTimeoutMinutes * 60)
                }, cancellationToken);
        }
        catch (ApiException ex)
        {
            _logger.LogError(ex,
                "Failed to create game session for bond scenario {TemplateId}: {StatusCode}",
                body.ScenarioTemplateId, ex.StatusCode);
            await _messageBus.TryPublishErrorAsync(
                "gardener", "EnterScenarioTogether", "game_session_creation_failed", ex.Message,
                dependency: "game-session", endpoint: "post:/game-session/create",
                details: new { TemplateId = body.ScenarioTemplateId, Partners = partnerAccountIds });
            return (StatusCodes.ServiceUnavailable, null);
        }

        var scenarioId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var scenario = new ScenarioInstanceModel
        {
            ScenarioInstanceId = scenarioId,
            ScenarioTemplateId = body.ScenarioTemplateId,
            GameSessionId = gameSession.SessionId,
            ConnectivityMode = template.ConnectivityMode,
            Status = ScenarioStatus.Active,
            CreatedAt = now,
            LastActivityAt = now,
            ChainDepth = 0,
            Participants = gardens.Select((g, i) => new ScenarioParticipantModel
            {
                SeedId = g.SeedId,
                AccountId = g.AccountId,
                SessionId = g.SessionId,
                JoinedAt = now,
                Role = i == 0 ? ScenarioParticipantRole.Primary : ScenarioParticipantRole.Partner
            }).ToList()
        };

        // Save scenario for each participant
        foreach (var accountId in partnerAccountIds)
        {
            await ScenarioStore.SaveAsync(ScenarioKey(accountId), scenario, cancellationToken: cancellationToken);
        }

        // Update tracking sets for all participants
        foreach (var accountId in partnerAccountIds)
        {
            await GardenCacheStore.RemoveFromSetAsync<Guid>(
                ActiveGardensTrackingKey, accountId, cancellationToken);
            await GardenCacheStore.AddToSetAsync<Guid>(
                ActiveScenariosTrackingKey, accountId, cancellationToken: cancellationToken);
        }

        // Clean up all garden instances
        foreach (var garden in gardens)
        {
            foreach (var poiId in garden.ActivePoiIds)
            {
                await PoiStore.DeleteAsync(PoiKey(garden.GardenInstanceId, poiId), cancellationToken);
            }
            await GardenStore.DeleteAsync(GardenKey(garden.AccountId), cancellationToken);
        }

        await _messageBus.TryPublishAsync("gardener.bond.entered-together",
            new GardenerBondEnteredTogetherEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                BondId = body.BondId,
                ScenarioInstanceId = scenarioId,
                ScenarioTemplateId = body.ScenarioTemplateId,
                Participants = partnerAccountIds
            }, cancellationToken: cancellationToken);

        _logger.LogInformation(
            "Bond scenario {ScenarioId} started for bond {BondId} with {Count} participants",
            scenarioId, body.BondId, partnerAccountIds.Count);

        return (StatusCodes.OK, MapToScenarioStateResponse(scenario));
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, SharedGardenStateResponse?)> GetSharedGardenStateAsync(
        GetSharedGardenRequest body, CancellationToken cancellationToken)
    {
        if (!_configuration.BondSharedGardenEnabled)
            return (StatusCodes.BadRequest, null);

        BondResponse bond;
        try
        {
            bond = await _seedClient.GetBondAsync(
                new GetBondRequest { BondId = body.BondId }, cancellationToken);
        }
        catch (ApiException ex) when (ex.StatusCode == 404)
        {
            return (StatusCodes.NotFound, null);
        }

        // Resolve participant seed IDs to owner account IDs
        var partnerAccountIds = new List<Guid>();
        foreach (var participant in bond.Participants)
        {
            var seed = await _seedClient.GetSeedAsync(
                new GetSeedRequest { SeedId = participant.SeedId }, cancellationToken);
            partnerAccountIds.Add(seed.OwnerId);
        }

        var allPois = new List<PoiSummary>();
        var playerStates = new List<BondedPlayerGardenState>();

        foreach (var accountId in partnerAccountIds)
        {
            var garden = await GardenStore.GetAsync(GardenKey(accountId), cancellationToken);
            if (garden == null) continue;

            playerStates.Add(new BondedPlayerGardenState
            {
                AccountId = accountId,
                SeedId = garden.SeedId,
                Position = MapToVec3(garden.Position)
            });

            var pois = await LoadActivePoisAsync(garden, cancellationToken);
            allPois.AddRange(pois.Select(MapToPoiSummary));
        }

        return (StatusCodes.OK, new SharedGardenStateResponse
        {
            BondId = body.BondId,
            Participants = playerStates,
            SharedPois = allPois
        });
    }

    #endregion

    #region Private Helpers

    /// <summary>
    /// Loads all active POIs for a garden instance from Redis.
    /// </summary>
    private async Task<IReadOnlyList<PoiModel>> LoadActivePoisAsync(
        GardenInstanceModel garden, CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.gardener", "GardenerService.LoadActivePoisAsync");
        var pois = new List<PoiModel>();
        foreach (var poiId in garden.ActivePoiIds)
        {
            var poi = await PoiStore.GetAsync(PoiKey(garden.GardenInstanceId, poiId), ct);
            if (poi != null)
                pois.Add(poi);
        }
        return pois;
    }

    /// <summary>
    /// Loads or creates the deployment phase configuration singleton.
    /// </summary>
    internal async Task<DeploymentPhaseConfigModel> GetOrCreatePhaseConfigAsync(
        CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.gardener", "GardenerService.GetOrCreatePhaseConfigAsync");
        var config = await PhaseStore.GetAsync(PhaseConfigKey, ct);
        if (config != null)
            return config;

        config = new DeploymentPhaseConfigModel
        {
            CurrentPhase = _configuration.DefaultPhase,
            MaxConcurrentScenariosGlobal = _configuration.MaxConcurrentScenariosGlobal,
            PersistentEntryEnabled = false,
            GardenMinigamesEnabled = false,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        await PhaseStore.SaveAsync(PhaseConfigKey, config, cancellationToken: ct);
        _logger.LogInformation(
            "Created default phase config with phase {Phase}", config.CurrentPhase);
        return config;
    }

    /// <summary>
    /// Calculates growth awards and records them via ISeedClient (per FOUNDATION TENETS cross-service communication).
    /// </summary>
    private async Task<Dictionary<string, float>> CalculateAndAwardGrowthAsync(
        ScenarioInstanceModel scenario,
        ScenarioTemplateModel? template,
        bool fullCompletion,
        CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.gardener", "GardenerService.CalculateAndAwardGrowthAsync");
        var growthAwarded = GardenerGrowthCalculation.CalculateGrowth(
            scenario, template, _configuration.GrowthAwardMultiplier, fullCompletion,
            (float)_configuration.GrowthFullCompletionMaxRatio,
            (float)_configuration.GrowthFullCompletionMinRatio,
            (float)_configuration.GrowthPartialMaxRatio,
            _configuration.DefaultEstimatedDurationMinutes);

        if (growthAwarded.Count == 0)
            return growthAwarded;

        var entries = growthAwarded.Select(kvp =>
            new GrowthEntry { Domain = kvp.Key, Amount = kvp.Value }).ToList();

        // Award growth for primary participant via batch API (per FOUNDATION TENETS)
        var primaryParticipant = scenario.Participants.FirstOrDefault();
        if (primaryParticipant != null && entries.Count > 0)
        {
            try
            {
                await _seedClient.RecordGrowthBatchAsync(new RecordGrowthBatchRequest
                {
                    SeedId = primaryParticipant.SeedId,
                    Entries = entries,
                    Source = "gardener"
                }, ct);
            }
            catch (ApiException ex)
            {
                _logger.LogError(ex,
                    "Failed to record growth for seed {SeedId}, scenario {ScenarioId}",
                    primaryParticipant.SeedId, scenario.ScenarioInstanceId);
            }

            // Award growth for additional participants
            foreach (var participant in scenario.Participants.Skip(1))
            {
                try
                {
                    await _seedClient.RecordGrowthBatchAsync(new RecordGrowthBatchRequest
                    {
                        SeedId = participant.SeedId,
                        Entries = entries,
                        Source = "gardener"
                    }, ct);
                }
                catch (ApiException ex)
                {
                    _logger.LogError(ex,
                        "Failed to record growth for participant seed {SeedId}",
                        participant.SeedId);
                }
            }
        }

        return growthAwarded;
    }

    /// <summary>
    /// Writes a completed/abandoned scenario to the durable MySQL history store.
    /// </summary>
    private async Task WriteScenarioHistoryAsync(
        ScenarioInstanceModel scenario,
        ScenarioTemplateModel? template,
        CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.gardener", "GardenerService.WriteScenarioHistoryAsync");
        var primaryParticipant = scenario.Participants.FirstOrDefault();
        if (primaryParticipant == null) return;

        var durationSeconds = scenario.CompletedAt.HasValue
            ? (float)(scenario.CompletedAt.Value - scenario.CreatedAt).TotalSeconds
            : (float)(DateTimeOffset.UtcNow - scenario.CreatedAt).TotalSeconds;

        var history = new ScenarioHistoryModel
        {
            ScenarioInstanceId = scenario.ScenarioInstanceId,
            ScenarioTemplateId = scenario.ScenarioTemplateId,
            AccountId = primaryParticipant.AccountId,
            SeedId = primaryParticipant.SeedId,
            CompletedAt = scenario.CompletedAt ?? DateTimeOffset.UtcNow,
            Status = scenario.Status,
            GrowthAwarded = scenario.GrowthAwarded,
            DurationSeconds = durationSeconds,
            TemplateCode = template?.Code
        };

        await HistoryStore.SaveAsync(HistoryKey(scenario.ScenarioInstanceId), history, cancellationToken: ct);
    }

    /// <summary>
    /// Attempts to clean up the game session by having participants leave.
    /// </summary>
    private async Task TryCleanupGameSessionAsync(
        ScenarioInstanceModel scenario, CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.gardener", "GardenerService.TryCleanupGameSessionAsync");
        foreach (var participant in scenario.Participants)
        {
            try
            {
                await _gameSessionClient.LeaveGameSessionByIdAsync(
                    new LeaveGameSessionByIdRequest
                    {
                        GameSessionId = scenario.GameSessionId,
                        AccountId = participant.AccountId,
                        WebSocketSessionId = null
                    }, ct);
            }
            catch (ApiException ex)
            {
                _logger.LogWarning(ex,
                    "Failed to remove participant {AccountId} from game session {SessionId}",
                    participant.AccountId, scenario.GameSessionId);
            }
        }
    }

    #region Model Mapping

    private static GardenStateResponse MapToGardenStateResponse(
        GardenInstanceModel garden, IReadOnlyList<PoiModel> pois)
    {
        return new GardenStateResponse
        {
            GardenInstanceId = garden.GardenInstanceId,
            SeedId = garden.SeedId,
            AccountId = garden.AccountId,
            Position = MapToVec3(garden.Position),
            ActivePois = pois.Select(MapToPoiSummary).ToList()
        };
    }

    private static PoiSummary MapToPoiSummary(PoiModel poi)
    {
        return new PoiSummary
        {
            PoiId = poi.PoiId,
            Position = MapToVec3(poi.Position),
            PoiType = poi.PoiType,
            ScenarioTemplateId = poi.ScenarioTemplateId,
            TriggerMode = poi.TriggerMode,
            TriggerRadius = poi.TriggerRadius,
            Status = poi.Status,
            VisualHint = poi.VisualHint,
            AudioHint = poi.AudioHint,
            IntensityRamp = poi.IntensityRamp
        };
    }

    private static ScenarioStateResponse MapToScenarioStateResponse(ScenarioInstanceModel scenario)
    {
        return new ScenarioStateResponse
        {
            ScenarioInstanceId = scenario.ScenarioInstanceId,
            ScenarioTemplateId = scenario.ScenarioTemplateId,
            GameSessionId = scenario.GameSessionId,
            ConnectivityMode = scenario.ConnectivityMode,
            Status = scenario.Status,
            CreatedAt = scenario.CreatedAt,
            ChainedFrom = scenario.ChainedFrom,
            ChainDepth = scenario.ChainDepth
        };
    }

    private static ScenarioTemplateResponse MapToTemplateResponse(ScenarioTemplateModel template)
    {
        return new ScenarioTemplateResponse
        {
            ScenarioTemplateId = template.ScenarioTemplateId,
            Code = template.Code,
            DisplayName = template.DisplayName,
            Description = template.Description,
            Category = template.Category,
            Subcategory = template.Subcategory,
            ConnectivityMode = template.ConnectivityMode,
            DomainWeights = template.DomainWeights.Select(dw => new DomainWeight
            {
                Domain = dw.Domain,
                Weight = dw.Weight
            }).ToList(),
            MinGrowthPhase = template.MinGrowthPhase,
            EstimatedDurationMinutes = template.EstimatedDurationMinutes,
            AllowedPhases = template.AllowedPhases.ToList(),
            MaxConcurrentInstances = template.MaxConcurrentInstances,
            Prerequisites = template.Prerequisites != null
                ? MapToPrerequisites(template.Prerequisites) : null,
            Chaining = template.Chaining != null
                ? MapToChaining(template.Chaining) : null,
            Multiplayer = template.Multiplayer != null
                ? MapToMultiplayer(template.Multiplayer) : null,
            Content = template.Content != null
                ? MapToContent(template.Content) : null,
            Status = template.Status,
            CreatedAt = template.CreatedAt,
            UpdatedAt = template.UpdatedAt
        };
    }

    private static PhaseConfigResponse MapToPhaseConfigResponse(DeploymentPhaseConfigModel config)
    {
        return new PhaseConfigResponse
        {
            CurrentPhase = config.CurrentPhase,
            MaxConcurrentScenariosGlobal = config.MaxConcurrentScenariosGlobal,
            PersistentEntryEnabled = config.PersistentEntryEnabled,
            GardenMinigamesEnabled = config.GardenMinigamesEnabled
        };
    }

    private static Vec3 MapToVec3(Vec3Model v)
    {
        return new Vec3 { X = v.X, Y = v.Y, Z = v.Z };
    }

    private static Vec3Model MapFromVec3(Vec3 v)
    {
        return new Vec3Model { X = v.X, Y = v.Y, Z = v.Z };
    }

    private static ScenarioPrerequisites MapToPrerequisites(ScenarioPrerequisitesModel m)
    {
        return new ScenarioPrerequisites
        {
            RequiredDomains = m.RequiredDomains,
            RequiredScenarios = m.RequiredScenarios,
            ExcludedScenarios = m.ExcludedScenarios
        };
    }

    private static ScenarioPrerequisitesModel MapFromPrerequisites(ScenarioPrerequisites p)
    {
        return new ScenarioPrerequisitesModel
        {
            RequiredDomains = p.RequiredDomains != null
                ? new Dictionary<string, float>(p.RequiredDomains) : null,
            RequiredScenarios = p.RequiredScenarios?.ToList(),
            ExcludedScenarios = p.ExcludedScenarios?.ToList()
        };
    }

    private static ScenarioChaining MapToChaining(ScenarioChainingModel m)
    {
        return new ScenarioChaining
        {
            LeadsTo = m.LeadsTo,
            ChainProbabilities = m.ChainProbabilities,
            MaxChainDepth = m.MaxChainDepth
        };
    }

    private static ScenarioChainingModel MapFromChaining(ScenarioChaining c)
    {
        return new ScenarioChainingModel
        {
            LeadsTo = c.LeadsTo?.ToList(),
            ChainProbabilities = c.ChainProbabilities != null
                ? new Dictionary<string, float>(c.ChainProbabilities) : null,
            MaxChainDepth = c.MaxChainDepth
        };
    }

    private static ScenarioMultiplayer MapToMultiplayer(ScenarioMultiplayerModel m)
    {
        return new ScenarioMultiplayer
        {
            MinPlayers = m.MinPlayers,
            MaxPlayers = m.MaxPlayers,
            MatchmakingQueueCode = m.MatchmakingQueueCode,
            BondPreferred = m.BondPreferred
        };
    }

    private static ScenarioMultiplayerModel MapFromMultiplayer(ScenarioMultiplayer m)
    {
        return new ScenarioMultiplayerModel
        {
            MinPlayers = m.MinPlayers,
            MaxPlayers = m.MaxPlayers,
            MatchmakingQueueCode = m.MatchmakingQueueCode,
            BondPreferred = m.BondPreferred
        };
    }

    private static ScenarioContent MapToContent(ScenarioContentModel m)
    {
        return new ScenarioContent
        {
            BehaviorDocumentId = m.BehaviorDocumentId,
            SceneDocumentId = m.SceneDocumentId,
            RealmId = m.RealmId,
            LocationCode = m.LocationCode
        };
    }

    private static ScenarioContentModel MapFromContent(ScenarioContent c)
    {
        return new ScenarioContentModel
        {
            BehaviorDocumentId = c.BehaviorDocumentId,
            SceneDocumentId = c.SceneDocumentId,
            RealmId = c.RealmId,
            LocationCode = c.LocationCode
        };
    }

    #endregion

    #endregion
}
