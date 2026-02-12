using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Gardener;

/// <summary>
/// Implementation of the Gardener service (L4 GameFeatures).
/// Orchestrates the player experience: void navigation, scenario routing, progressive discovery,
/// and deployment phase management. The player-side counterpart to Puppetmaster.
/// </summary>
/// <remarks>
/// <para>
/// <b>FOUNDATION TENETS - PARTIAL CLASS REQUIRED:</b> This class MUST remain a partial class.
/// Generated code (event handlers, permissions) is placed in companion partial classes.
/// </para>
/// <para>
/// <b>Cross-Service Integration:</b>
/// <list type="bullet">
///   <item>Uses ISeedClient (L2) for growth award recording via RecordGrowthBatchAsync</item>
///   <item>Uses IGameSessionClient (L2) for backing game session creation</item>
///   <item>Implements ISeedEvolutionListener (registered in plugin) for growth/phase notifications</item>
///   <item>Subscribes to seed.bond.formed, seed.activated, game-session.deleted broadcast events</item>
/// </list>
/// </para>
/// </remarks>
[BannouService("gardener", typeof(IGardenerService), lifetime: ServiceLifetime.Scoped, layer: ServiceLayer.GameFeatures)]
public partial class GardenerService : IGardenerService
{
    private readonly IMessageBus _messageBus;
    private readonly IStateStoreFactory _stateStoreFactory;
    private readonly ILogger<GardenerService> _logger;
    private readonly GardenerServiceConfiguration _configuration;
    private readonly IServiceProvider _serviceProvider;

    /// <summary>
    /// Initializes a new instance of <see cref="GardenerService"/>.
    /// </summary>
    public GardenerService(
        IMessageBus messageBus,
        IStateStoreFactory stateStoreFactory,
        ILogger<GardenerService> logger,
        GardenerServiceConfiguration configuration,
        IEventConsumer eventConsumer,
        IServiceProvider serviceProvider)
    {
        _messageBus = messageBus;
        _stateStoreFactory = stateStoreFactory;
        _logger = logger;
        _configuration = configuration;
        _serviceProvider = serviceProvider;

        RegisterEventConsumers(eventConsumer);
    }

    // =========================================================================
    // Void Navigation
    // =========================================================================

    /// <summary>
    /// Creates a void instance for a player, anchored to their active seed.
    /// Only one void instance per account at a time.
    /// </summary>
    public async Task<(StatusCodes, VoidStateResponse?)> EnterVoidAsync(EnterVoidRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("EnterVoid: AccountId={AccountId}, SeedId={SeedId}", body.AccountId, body.SeedId);

        var voidStore = _stateStoreFactory.GetStore<VoidInstanceModel>(StateStoreDefinitions.GardenerVoidInstances);
        var existing = await voidStore.GetAsync($"void:{body.AccountId}", cancellationToken);

        if (existing != null)
        {
            _logger.LogDebug("Account {AccountId} already has active void instance {VoidId}", body.AccountId, existing.VoidInstanceId);
            return (StatusCodes.Conflict, null);
        }

        var voidInstance = new VoidInstanceModel
        {
            VoidInstanceId = Guid.NewGuid(),
            AccountId = body.AccountId,
            SeedId = body.SeedId,
            EnteredAt = DateTimeOffset.UtcNow,
            PlayerPosition = new Position3D(),
            DriftVector = new Position3D(),
            ActivePoiIds = new List<Guid>()
        };

        await voidStore.SaveAsync($"void:{body.AccountId}", voidInstance, cancellationToken);

        await _messageBus.TryPublishAsync("gardener.void.entered", new GardenerVoidEnteredEvent
        {
            EventId = Guid.NewGuid(),
            AccountId = body.AccountId,
            SeedId = body.SeedId,
            VoidInstanceId = voidInstance.VoidInstanceId
        }, cancellationToken: cancellationToken);

        _logger.LogInformation("Void instance created: VoidId={VoidId}, AccountId={AccountId}", voidInstance.VoidInstanceId, body.AccountId);

        return (StatusCodes.Created, BuildVoidStateResponse(voidInstance, new List<PoiModel>()));
    }

    /// <summary>
    /// Returns the current void state for a player, including active POIs.
    /// </summary>
    public async Task<(StatusCodes, VoidStateResponse?)> GetVoidStateAsync(GetVoidStateRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("GetVoidState: AccountId={AccountId}", body.AccountId);

        var voidStore = _stateStoreFactory.GetStore<VoidInstanceModel>(StateStoreDefinitions.GardenerVoidInstances);
        var voidInstance = await voidStore.GetAsync($"void:{body.AccountId}", cancellationToken);

        if (voidInstance == null)
        {
            _logger.LogDebug("No active void instance for account {AccountId}", body.AccountId);
            return (StatusCodes.NotFound, null);
        }

        var pois = await GetActivePoisAsync(voidInstance, cancellationToken);
        return (StatusCodes.OK, BuildVoidStateResponse(voidInstance, pois));
    }

    /// <summary>
    /// Updates the player's position in the void, accumulates drift vector, and checks for POI proximity.
    /// </summary>
    public async Task<(StatusCodes, PositionUpdateResponse?)> UpdatePositionAsync(UpdatePositionRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("UpdatePosition: AccountId={AccountId}", body.AccountId);

        var voidStore = _stateStoreFactory.GetStore<VoidInstanceModel>(StateStoreDefinitions.GardenerVoidInstances);
        var voidInstance = await voidStore.GetAsync($"void:{body.AccountId}", cancellationToken);

        if (voidInstance == null)
        {
            return (StatusCodes.NotFound, null);
        }

        // Accumulate drift vector
        voidInstance.DriftVector.X += (body.Position.X - (float)voidInstance.PlayerPosition.X);
        voidInstance.DriftVector.Y += (body.Position.Y - (float)voidInstance.PlayerPosition.Y);
        voidInstance.DriftVector.Z += (body.Position.Z - (float)voidInstance.PlayerPosition.Z);

        voidInstance.PlayerPosition = new Position3D
        {
            X = body.Position.X,
            Y = body.Position.Y,
            Z = body.Position.Z
        };

        await voidStore.SaveAsync($"void:{body.AccountId}", voidInstance, cancellationToken);

        // Check for newly discovered POIs based on proximity
        var triggeredPois = new List<PoiSummary>();
        var poiStore = _stateStoreFactory.GetStore<PoiModel>(StateStoreDefinitions.GardenerPois);

        foreach (var poiId in voidInstance.ActivePoiIds)
        {
            var poi = await poiStore.GetAsync($"poi:{poiId}", cancellationToken);
            if (poi != null && !poi.Discovered)
            {
                var distance = voidInstance.PlayerPosition.DistanceTo(poi.Position);
                if (distance <= _configuration.PoiSpawnRadiusMin)
                {
                    poi.Discovered = true;
                    await poiStore.SaveAsync($"poi:{poiId}", poi, cancellationToken);
                    triggeredPois.Add(BuildPoiSummary(poi));
                }
            }
        }

        return (StatusCodes.OK, new PositionUpdateResponse
        {
            Acknowledged = true,
            TriggeredPois = triggeredPois.Count > 0 ? triggeredPois : null
        });
    }

    /// <summary>
    /// Destroys the player's void instance and publishes a void.left event.
    /// Active scenarios should be completed or abandoned before leaving.
    /// </summary>
    public async Task<(StatusCodes, LeaveVoidResponse?)> LeaveVoidAsync(LeaveVoidRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("LeaveVoid: AccountId={AccountId}", body.AccountId);

        var voidStore = _stateStoreFactory.GetStore<VoidInstanceModel>(StateStoreDefinitions.GardenerVoidInstances);
        var voidInstance = await voidStore.GetAsync($"void:{body.AccountId}", cancellationToken);

        if (voidInstance == null)
        {
            return (StatusCodes.NotFound, null);
        }

        var sessionDuration = (float)(DateTimeOffset.UtcNow - voidInstance.EnteredAt).TotalSeconds;

        // Clean up POIs
        var poiStore = _stateStoreFactory.GetStore<PoiModel>(StateStoreDefinitions.GardenerPois);
        foreach (var poiId in voidInstance.ActivePoiIds)
        {
            await poiStore.DeleteAsync($"poi:{poiId}", cancellationToken);
        }

        await voidStore.DeleteAsync($"void:{body.AccountId}", cancellationToken);

        await _messageBus.TryPublishAsync("gardener.void.left", new GardenerVoidLeftEvent
        {
            EventId = Guid.NewGuid(),
            AccountId = body.AccountId,
            VoidInstanceId = voidInstance.VoidInstanceId,
            SessionDurationSeconds = sessionDuration
        }, cancellationToken: cancellationToken);

        _logger.LogInformation("Void instance destroyed: VoidId={VoidId}, Duration={Duration}s", voidInstance.VoidInstanceId, sessionDuration);

        return (StatusCodes.OK, new LeaveVoidResponse
        {
            AccountId = body.AccountId,
            SessionDurationSeconds = sessionDuration,
            ScenariosCompleted = 0 // Tracked by history, not void state
        });
    }

    // =========================================================================
    // POI Interaction
    // =========================================================================

    /// <summary>
    /// Lists all active POIs in a player's void instance.
    /// </summary>
    public async Task<(StatusCodes, ListPoisResponse?)> ListPoisAsync(ListPoisRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("ListPois: VoidInstanceId={VoidInstanceId}", body.VoidInstanceId);

        var voidStore = _stateStoreFactory.GetStore<VoidInstanceModel>(StateStoreDefinitions.GardenerVoidInstances);
        var voidInstance = await voidStore.GetAsync($"void:{body.AccountId}", cancellationToken);

        if (voidInstance == null || voidInstance.VoidInstanceId != body.VoidInstanceId)
        {
            return (StatusCodes.NotFound, null);
        }

        var pois = await GetActivePoisAsync(voidInstance, cancellationToken);

        return (StatusCodes.OK, new ListPoisResponse
        {
            VoidInstanceId = voidInstance.VoidInstanceId,
            Pois = pois.Select(BuildPoiSummary).ToList()
        });
    }

    /// <summary>
    /// Player interacts with a POI, triggering the associated scenario.
    /// </summary>
    public async Task<(StatusCodes, PoiInteractionResponse?)> InteractWithPoiAsync(InteractWithPoiRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("InteractWithPoi: AccountId={AccountId}, PoiId={PoiId}", body.AccountId, body.PoiId);

        var poiStore = _stateStoreFactory.GetStore<PoiModel>(StateStoreDefinitions.GardenerPois);
        var poi = await poiStore.GetAsync($"poi:{body.PoiId}", cancellationToken);

        if (poi == null || poi.AccountId != body.AccountId)
        {
            return (StatusCodes.NotFound, null);
        }

        // Get the template for this POI
        var templateStore = _stateStoreFactory.GetStore<ScenarioTemplateModel>(StateStoreDefinitions.GardenerScenarioTemplates);
        var template = await templateStore.GetAsync($"template:{poi.ScenarioTemplateId}", cancellationToken);

        if (template == null)
        {
            _logger.LogError("POI {PoiId} references non-existent template {TemplateId}", poi.PoiId, poi.ScenarioTemplateId);
            return (StatusCodes.InternalServerError, null);
        }

        await _messageBus.TryPublishAsync("gardener.poi.entered", new GardenerPoiEnteredEvent
        {
            EventId = Guid.NewGuid(),
            AccountId = body.AccountId,
            PoiId = body.PoiId,
            ScenarioTemplateId = poi.ScenarioTemplateId
        }, cancellationToken: cancellationToken);

        _logger.LogInformation("POI interaction: PoiId={PoiId}, TemplateId={TemplateId}", poi.PoiId, poi.ScenarioTemplateId);

        return (StatusCodes.OK, new PoiInteractionResponse
        {
            PoiId = body.PoiId,
            ScenarioTemplateId = poi.ScenarioTemplateId,
            Category = template.Category,
            ConnectivityMode = template.ConnectivityMode,
            EstimatedDurationMinutes = template.EstimatedDurationMinutes,
            BondCompatible = template.BondCompatible
        });
    }

    /// <summary>
    /// Player declines a POI. The POI is removed and recorded for cooldown purposes.
    /// </summary>
    public async Task<(StatusCodes, DeclinePoiResponse?)> DeclinePoiAsync(DeclinePoiRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("DeclinePoi: AccountId={AccountId}, PoiId={PoiId}", body.AccountId, body.PoiId);

        var poiStore = _stateStoreFactory.GetStore<PoiModel>(StateStoreDefinitions.GardenerPois);
        var poi = await poiStore.GetAsync($"poi:{body.PoiId}", cancellationToken);

        if (poi == null || poi.AccountId != body.AccountId)
        {
            return (StatusCodes.NotFound, null);
        }

        // Remove the POI from active set
        var voidStore = _stateStoreFactory.GetStore<VoidInstanceModel>(StateStoreDefinitions.GardenerVoidInstances);
        var voidInstance = await voidStore.GetAsync($"void:{body.AccountId}", cancellationToken);
        if (voidInstance != null)
        {
            voidInstance.ActivePoiIds.Remove(body.PoiId);
            await voidStore.SaveAsync($"void:{body.AccountId}", voidInstance, cancellationToken);
        }

        await poiStore.DeleteAsync($"poi:{body.PoiId}", cancellationToken);

        await _messageBus.TryPublishAsync("gardener.poi.declined", new GardenerPoiDeclinedEvent
        {
            EventId = Guid.NewGuid(),
            AccountId = body.AccountId,
            PoiId = body.PoiId,
            ScenarioTemplateId = poi.ScenarioTemplateId
        }, cancellationToken: cancellationToken);

        return (StatusCodes.OK, new DeclinePoiResponse
        {
            PoiId = body.PoiId,
            ScenarioTemplateId = poi.ScenarioTemplateId,
            RemainingPois = voidInstance?.ActivePoiIds.Count ?? 0
        });
    }

    // =========================================================================
    // Scenario Lifecycle
    // =========================================================================

    /// <summary>
    /// Creates a scenario instance from a template, creates a backing game session,
    /// and publishes a scenario.started event.
    /// </summary>
    public async Task<(StatusCodes, ScenarioStateResponse?)> EnterScenarioAsync(EnterScenarioRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("EnterScenario: AccountId={AccountId}, TemplateId={TemplateId}", body.AccountId, body.ScenarioTemplateId);

        var templateStore = _stateStoreFactory.GetStore<ScenarioTemplateModel>(StateStoreDefinitions.GardenerScenarioTemplates);
        var template = await templateStore.GetAsync($"template:{body.ScenarioTemplateId}", cancellationToken);

        if (template == null)
        {
            return (StatusCodes.NotFound, null);
        }

        if (template.Status != TemplateStatus.Active)
        {
            _logger.LogDebug("Template {TemplateId} is not active (status={Status})", body.ScenarioTemplateId, template.Status);
            return (StatusCodes.BadRequest, null);
        }

        var now = DateTimeOffset.UtcNow;
        var scenarioInstance = new ScenarioInstanceModel
        {
            ScenarioInstanceId = Guid.NewGuid(),
            ScenarioTemplateId = body.ScenarioTemplateId,
            GameSessionId = body.GameSessionId,
            AccountId = body.AccountId,
            SeedId = body.SeedId,
            Status = ScenarioInstanceStatus.Active,
            StartedAt = now,
            LastActivityAt = now,
            ChainDepth = 0
        };

        var scenarioStore = _stateStoreFactory.GetStore<ScenarioInstanceModel>(StateStoreDefinitions.GardenerScenarioInstances);
        await scenarioStore.SaveAsync($"scenario:{scenarioInstance.ScenarioInstanceId}", scenarioInstance, cancellationToken);

        await _messageBus.TryPublishAsync("gardener.scenario.started", new GardenerScenarioStartedEvent
        {
            EventId = Guid.NewGuid(),
            ScenarioInstanceId = scenarioInstance.ScenarioInstanceId,
            ScenarioTemplateId = body.ScenarioTemplateId,
            GameSessionId = body.GameSessionId,
            AccountId = body.AccountId,
            SeedId = body.SeedId
        }, cancellationToken: cancellationToken);

        _logger.LogInformation("Scenario started: InstanceId={InstanceId}, TemplateId={TemplateId}", scenarioInstance.ScenarioInstanceId, body.ScenarioTemplateId);

        return (StatusCodes.Created, BuildScenarioStateResponse(scenarioInstance, template));
    }

    /// <summary>
    /// Returns the current state of a scenario instance.
    /// </summary>
    public async Task<(StatusCodes, ScenarioStateResponse?)> GetScenarioStateAsync(GetScenarioStateRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("GetScenarioState: ScenarioInstanceId={ScenarioInstanceId}", body.ScenarioInstanceId);

        var scenarioStore = _stateStoreFactory.GetStore<ScenarioInstanceModel>(StateStoreDefinitions.GardenerScenarioInstances);
        var instance = await scenarioStore.GetAsync($"scenario:{body.ScenarioInstanceId}", cancellationToken);

        if (instance == null)
        {
            return (StatusCodes.NotFound, null);
        }

        var templateStore = _stateStoreFactory.GetStore<ScenarioTemplateModel>(StateStoreDefinitions.GardenerScenarioTemplates);
        var template = await templateStore.GetAsync($"template:{instance.ScenarioTemplateId}", cancellationToken);

        if (template == null)
        {
            _logger.LogError("Scenario {InstanceId} references non-existent template {TemplateId}", instance.ScenarioInstanceId, instance.ScenarioTemplateId);
            return (StatusCodes.InternalServerError, null);
        }

        return (StatusCodes.OK, BuildScenarioStateResponse(instance, template));
    }

    /// <summary>
    /// Completes a scenario, awards growth to the player's seed, records history, and publishes events.
    /// </summary>
    public async Task<(StatusCodes, ScenarioCompletionResponse?)> CompleteScenarioAsync(CompleteScenarioRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("CompleteScenario: ScenarioInstanceId={ScenarioInstanceId}", body.ScenarioInstanceId);

        var scenarioStore = _stateStoreFactory.GetStore<ScenarioInstanceModel>(StateStoreDefinitions.GardenerScenarioInstances);
        var instance = await scenarioStore.GetAsync($"scenario:{body.ScenarioInstanceId}", cancellationToken);

        if (instance == null)
        {
            return (StatusCodes.NotFound, null);
        }

        if (instance.Status != ScenarioInstanceStatus.Active)
        {
            _logger.LogDebug("Scenario {InstanceId} is not active (status={Status})", instance.ScenarioInstanceId, instance.Status);
            return (StatusCodes.BadRequest, null);
        }

        if (instance.AccountId != body.AccountId)
        {
            return (StatusCodes.Forbidden, null);
        }

        var templateStore = _stateStoreFactory.GetStore<ScenarioTemplateModel>(StateStoreDefinitions.GardenerScenarioTemplates);
        var template = await templateStore.GetAsync($"template:{instance.ScenarioTemplateId}", cancellationToken);

        if (template == null)
        {
            _logger.LogError("Scenario {InstanceId} references non-existent template", instance.ScenarioInstanceId);
            return (StatusCodes.InternalServerError, null);
        }

        var now = DateTimeOffset.UtcNow;
        instance.Status = ScenarioInstanceStatus.Completed;
        instance.CompletedAt = now;

        await scenarioStore.SaveAsync($"scenario:{instance.ScenarioInstanceId}", instance, cancellationToken);

        // Calculate growth awards with multiplier
        var growthAwarded = new Dictionary<string, float>();
        foreach (var (domain, amount) in template.GrowthAwards)
        {
            growthAwarded[domain] = (float)(amount * _configuration.GrowthAwardMultiplier);
        }

        // Award growth via ISeedClient (L4 -> L2 direct API call per IMPLEMENTATION TENETS)
        if (growthAwarded.Count > 0)
        {
            await AwardGrowthAsync(instance.SeedId, growthAwarded, "scenario_completion", cancellationToken);
        }

        // Record history
        await RecordScenarioHistoryAsync(instance, ScenarioOutcome.Completed, growthAwarded, cancellationToken);

        await _messageBus.TryPublishAsync("gardener.scenario.completed", new GardenerScenarioCompletedEvent
        {
            EventId = Guid.NewGuid(),
            ScenarioInstanceId = instance.ScenarioInstanceId,
            ScenarioTemplateId = instance.ScenarioTemplateId,
            AccountId = instance.AccountId,
            GrowthAwarded = growthAwarded.ToDictionary(kvp => kvp.Key, kvp => (double)kvp.Value)
        }, cancellationToken: cancellationToken);

        _logger.LogInformation("Scenario completed: InstanceId={InstanceId}, GrowthDomains={DomainCount}", instance.ScenarioInstanceId, growthAwarded.Count);

        // Clean up the Redis instance
        await scenarioStore.DeleteAsync($"scenario:{instance.ScenarioInstanceId}", cancellationToken);

        return (StatusCodes.OK, new ScenarioCompletionResponse
        {
            ScenarioInstanceId = instance.ScenarioInstanceId,
            GrowthAwarded = growthAwarded,
            ReturnToVoid = true
        });
    }

    /// <summary>
    /// Abandons a scenario, awards partial growth, records history, and publishes events.
    /// </summary>
    public async Task<(StatusCodes, AbandonScenarioResponse?)> AbandonScenarioAsync(AbandonScenarioRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("AbandonScenario: ScenarioInstanceId={ScenarioInstanceId}", body.ScenarioInstanceId);

        var scenarioStore = _stateStoreFactory.GetStore<ScenarioInstanceModel>(StateStoreDefinitions.GardenerScenarioInstances);
        var instance = await scenarioStore.GetAsync($"scenario:{body.ScenarioInstanceId}", cancellationToken);

        if (instance == null)
        {
            return (StatusCodes.NotFound, null);
        }

        if (instance.Status != ScenarioInstanceStatus.Active)
        {
            return (StatusCodes.BadRequest, null);
        }

        if (instance.AccountId != body.AccountId)
        {
            return (StatusCodes.Forbidden, null);
        }

        var templateStore = _stateStoreFactory.GetStore<ScenarioTemplateModel>(StateStoreDefinitions.GardenerScenarioTemplates);
        var template = await templateStore.GetAsync($"template:{instance.ScenarioTemplateId}", cancellationToken);

        instance.Status = ScenarioInstanceStatus.Abandoned;
        instance.CompletedAt = DateTimeOffset.UtcNow;

        await scenarioStore.SaveAsync($"scenario:{instance.ScenarioInstanceId}", instance, cancellationToken);

        // Calculate partial growth (25% of full awards)
        var partialGrowth = new Dictionary<string, float>();
        if (template != null)
        {
            foreach (var (domain, amount) in template.GrowthAwards)
            {
                partialGrowth[domain] = (float)(amount * _configuration.GrowthAwardMultiplier * 0.25);
            }
        }

        if (partialGrowth.Count > 0)
        {
            await AwardGrowthAsync(instance.SeedId, partialGrowth, "scenario_abandoned", cancellationToken);
        }

        await RecordScenarioHistoryAsync(instance, ScenarioOutcome.Abandoned, partialGrowth, cancellationToken);

        await _messageBus.TryPublishAsync("gardener.scenario.abandoned", new GardenerScenarioAbandonedEvent
        {
            EventId = Guid.NewGuid(),
            ScenarioInstanceId = instance.ScenarioInstanceId,
            AccountId = instance.AccountId
        }, cancellationToken: cancellationToken);

        _logger.LogInformation("Scenario abandoned: InstanceId={InstanceId}", instance.ScenarioInstanceId);

        await scenarioStore.DeleteAsync($"scenario:{instance.ScenarioInstanceId}", cancellationToken);

        return (StatusCodes.OK, new AbandonScenarioResponse
        {
            ScenarioInstanceId = instance.ScenarioInstanceId,
            PartialGrowthAwarded = partialGrowth
        });
    }

    /// <summary>
    /// Chains from a completed scenario to a new one, incrementing chain depth.
    /// </summary>
    public async Task<(StatusCodes, ScenarioStateResponse?)> ChainScenarioAsync(ChainScenarioRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("ChainScenario: PreviousInstanceId={PreviousId}, NewTemplateId={NewTemplateId}", body.PreviousScenarioInstanceId, body.NewScenarioTemplateId);

        var templateStore = _stateStoreFactory.GetStore<ScenarioTemplateModel>(StateStoreDefinitions.GardenerScenarioTemplates);
        var newTemplate = await templateStore.GetAsync($"template:{body.NewScenarioTemplateId}", cancellationToken);

        if (newTemplate == null || newTemplate.Status != TemplateStatus.Active)
        {
            return (StatusCodes.NotFound, null);
        }

        var now = DateTimeOffset.UtcNow;
        var chainDepth = body.ChainDepth + 1;

        var newInstance = new ScenarioInstanceModel
        {
            ScenarioInstanceId = Guid.NewGuid(),
            ScenarioTemplateId = body.NewScenarioTemplateId,
            GameSessionId = body.GameSessionId,
            AccountId = body.AccountId,
            SeedId = body.SeedId,
            Status = ScenarioInstanceStatus.Active,
            StartedAt = now,
            LastActivityAt = now,
            ChainDepth = chainDepth,
            PreviousScenarioInstanceId = body.PreviousScenarioInstanceId
        };

        var scenarioStore = _stateStoreFactory.GetStore<ScenarioInstanceModel>(StateStoreDefinitions.GardenerScenarioInstances);
        await scenarioStore.SaveAsync($"scenario:{newInstance.ScenarioInstanceId}", newInstance, cancellationToken);

        await _messageBus.TryPublishAsync("gardener.scenario.chained", new GardenerScenarioChainedEvent
        {
            EventId = Guid.NewGuid(),
            PreviousScenarioInstanceId = body.PreviousScenarioInstanceId,
            NewScenarioInstanceId = newInstance.ScenarioInstanceId,
            AccountId = body.AccountId,
            ChainDepth = chainDepth
        }, cancellationToken: cancellationToken);

        _logger.LogInformation("Scenario chained: NewInstanceId={NewId}, ChainDepth={Depth}", newInstance.ScenarioInstanceId, chainDepth);

        return (StatusCodes.Created, BuildScenarioStateResponse(newInstance, newTemplate));
    }

    // =========================================================================
    // Scenario Template Management
    // =========================================================================

    /// <summary>
    /// Creates a new scenario template.
    /// </summary>
    public async Task<(StatusCodes, ScenarioTemplateResponse?)> CreateTemplateAsync(CreateTemplateRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("CreateTemplate: Code={Code}", body.Code);

        var now = DateTimeOffset.UtcNow;
        var template = new ScenarioTemplateModel
        {
            ScenarioTemplateId = Guid.NewGuid(),
            Code = body.Code,
            DisplayName = body.DisplayName,
            Description = body.Description,
            Category = body.Category,
            ConnectivityMode = body.ConnectivityMode,
            MinimumPhase = body.MinimumPhase,
            Status = TemplateStatus.Draft,
            EstimatedDurationMinutes = body.EstimatedDurationMinutes,
            BondCompatible = body.BondCompatible,
            MaxConcurrentInstances = body.MaxConcurrentInstances,
            GrowthAwards = body.GrowthAwards?.ToDictionary(kvp => kvp.Key, kvp => (double)kvp.Value) ?? new Dictionary<string, double>(),
            DomainAffinities = body.DomainAffinities?.ToDictionary(kvp => kvp.Key, kvp => (double)kvp.Value) ?? new Dictionary<string, double>(),
            ChainTargets = body.ChainTargets?.ToList() ?? new List<Guid>(),
            Tags = body.Tags?.ToList() ?? new List<string>(),
            CreatedAt = now,
            UpdatedAt = now
        };

        var templateStore = _stateStoreFactory.GetStore<ScenarioTemplateModel>(StateStoreDefinitions.GardenerScenarioTemplates);
        await templateStore.SaveAsync($"template:{template.ScenarioTemplateId}", template, cancellationToken);

        await _messageBus.TryPublishAsync("scenario-template.created", new ScenarioTemplateCreatedEvent
        {
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

        _logger.LogInformation("Template created: TemplateId={TemplateId}, Code={Code}", template.ScenarioTemplateId, template.Code);

        return (StatusCodes.Created, BuildTemplateResponse(template));
    }

    /// <summary>
    /// Gets a scenario template by ID.
    /// </summary>
    public async Task<(StatusCodes, ScenarioTemplateResponse?)> GetTemplateAsync(GetTemplateRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("GetTemplate: TemplateId={TemplateId}", body.ScenarioTemplateId);

        var templateStore = _stateStoreFactory.GetStore<ScenarioTemplateModel>(StateStoreDefinitions.GardenerScenarioTemplates);
        var template = await templateStore.GetAsync($"template:{body.ScenarioTemplateId}", cancellationToken);

        if (template == null)
        {
            return (StatusCodes.NotFound, null);
        }

        return (StatusCodes.OK, BuildTemplateResponse(template));
    }

    /// <summary>
    /// Gets a scenario template by its unique code.
    /// </summary>
    public async Task<(StatusCodes, ScenarioTemplateResponse?)> GetTemplateByCodeAsync(GetTemplateByCodeRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("GetTemplateByCode: Code={Code}", body.Code);

        var templateStore = _stateStoreFactory.GetJsonQueryableStore<ScenarioTemplateModel>(StateStoreDefinitions.GardenerScenarioTemplates);
        var conditions = new List<QueryCondition>
        {
            new QueryCondition { Path = "$.Code", Operator = QueryOperator.Equals, Value = body.Code },
            new QueryCondition { Path = "$.ScenarioTemplateId", Operator = QueryOperator.Exists, Value = true }
        };

        var result = await templateStore.JsonQueryPagedAsync(conditions, 0, 1, null, cancellationToken);

        if (result.TotalCount == 0)
        {
            return (StatusCodes.NotFound, null);
        }

        return (StatusCodes.OK, BuildTemplateResponse(result.Items.First().Value));
    }

    /// <summary>
    /// Lists scenario templates with optional filtering by category and status.
    /// </summary>
    public async Task<(StatusCodes, ListTemplatesResponse?)> ListTemplatesAsync(ListTemplatesRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("ListTemplates: Page={Page}, PageSize={PageSize}", body.Page, body.PageSize);

        var templateStore = _stateStoreFactory.GetJsonQueryableStore<ScenarioTemplateModel>(StateStoreDefinitions.GardenerScenarioTemplates);
        var conditions = new List<QueryCondition>
        {
            new QueryCondition { Path = "$.ScenarioTemplateId", Operator = QueryOperator.Exists, Value = true }
        };

        if (body.Category != null)
        {
            conditions.Add(new QueryCondition { Path = "$.Category", Operator = QueryOperator.Equals, Value = body.Category.Value.ToString() });
        }

        if (body.Status != null)
        {
            conditions.Add(new QueryCondition { Path = "$.Status", Operator = QueryOperator.Equals, Value = body.Status.Value.ToString() });
        }

        var page = body.Page > 0 ? body.Page : 0;
        var pageSize = body.PageSize > 0 ? body.PageSize : 20;
        var result = await templateStore.JsonQueryPagedAsync(conditions, page, pageSize, null, cancellationToken);

        return (StatusCodes.OK, new ListTemplatesResponse
        {
            Templates = result.Items.Select(i => BuildTemplateResponse(i.Value)).ToList(),
            TotalCount = result.TotalCount,
            Page = page,
            PageSize = pageSize
        });
    }

    /// <summary>
    /// Updates a scenario template's mutable fields.
    /// </summary>
    public async Task<(StatusCodes, ScenarioTemplateResponse?)> UpdateTemplateAsync(UpdateTemplateRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("UpdateTemplate: TemplateId={TemplateId}", body.ScenarioTemplateId);

        var templateStore = _stateStoreFactory.GetStore<ScenarioTemplateModel>(StateStoreDefinitions.GardenerScenarioTemplates);
        var template = await templateStore.GetAsync($"template:{body.ScenarioTemplateId}", cancellationToken);

        if (template == null)
        {
            return (StatusCodes.NotFound, null);
        }

        if (body.DisplayName != null) template.DisplayName = body.DisplayName;
        if (body.Description != null) template.Description = body.Description;
        if (body.Category != null) template.Category = body.Category.Value;
        if (body.ConnectivityMode != null) template.ConnectivityMode = body.ConnectivityMode.Value;
        if (body.MinimumPhase != null) template.MinimumPhase = body.MinimumPhase.Value;
        if (body.Status != null) template.Status = body.Status.Value;
        if (body.EstimatedDurationMinutes != null) template.EstimatedDurationMinutes = body.EstimatedDurationMinutes.Value;
        if (body.BondCompatible != null) template.BondCompatible = body.BondCompatible.Value;
        if (body.MaxConcurrentInstances != null) template.MaxConcurrentInstances = body.MaxConcurrentInstances.Value;
        if (body.GrowthAwards != null) template.GrowthAwards = body.GrowthAwards.ToDictionary(kvp => kvp.Key, kvp => (double)kvp.Value);
        if (body.DomainAffinities != null) template.DomainAffinities = body.DomainAffinities.ToDictionary(kvp => kvp.Key, kvp => (double)kvp.Value);
        if (body.ChainTargets != null) template.ChainTargets = body.ChainTargets.ToList();
        if (body.Tags != null) template.Tags = body.Tags.ToList();

        template.UpdatedAt = DateTimeOffset.UtcNow;

        await templateStore.SaveAsync($"template:{template.ScenarioTemplateId}", template, cancellationToken);

        await _messageBus.TryPublishAsync("scenario-template.updated", new ScenarioTemplateUpdatedEvent
        {
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

        _logger.LogInformation("Template updated: TemplateId={TemplateId}", template.ScenarioTemplateId);

        return (StatusCodes.OK, BuildTemplateResponse(template));
    }

    /// <summary>
    /// Deprecates a scenario template, preventing new instances from being created.
    /// </summary>
    public async Task<(StatusCodes, ScenarioTemplateResponse?)> DeprecateTemplateAsync(DeprecateTemplateRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("DeprecateTemplate: TemplateId={TemplateId}", body.ScenarioTemplateId);

        var templateStore = _stateStoreFactory.GetStore<ScenarioTemplateModel>(StateStoreDefinitions.GardenerScenarioTemplates);
        var template = await templateStore.GetAsync($"template:{body.ScenarioTemplateId}", cancellationToken);

        if (template == null)
        {
            return (StatusCodes.NotFound, null);
        }

        template.Status = TemplateStatus.Deprecated;
        template.UpdatedAt = DateTimeOffset.UtcNow;

        await templateStore.SaveAsync($"template:{template.ScenarioTemplateId}", template, cancellationToken);

        await _messageBus.TryPublishAsync("scenario-template.updated", new ScenarioTemplateUpdatedEvent
        {
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

        _logger.LogInformation("Template deprecated: TemplateId={TemplateId}", template.ScenarioTemplateId);

        return (StatusCodes.OK, BuildTemplateResponse(template));
    }

    // =========================================================================
    // Deployment Phase
    // =========================================================================

    /// <summary>
    /// Gets the current phase configuration.
    /// </summary>
    public async Task<(StatusCodes, PhaseConfigResponse?)> GetPhaseConfigAsync(GetPhaseConfigRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("GetPhaseConfig");

        var phaseStore = _stateStoreFactory.GetStore<PhaseConfigModel>(StateStoreDefinitions.GardenerPhaseConfig);
        var config = await phaseStore.GetAsync("phase:current", cancellationToken);

        if (config == null)
        {
            // Return defaults from configuration
            return (StatusCodes.OK, new PhaseConfigResponse
            {
                CurrentPhase = _configuration.DefaultPhase,
                MaxConcurrentScenariosGlobal = _configuration.MaxConcurrentScenariosGlobal,
                PersistentEntryEnabled = _configuration.DefaultPhase >= DeploymentPhase.Beta,
                VoidMinigamesEnabled = _configuration.DefaultPhase >= DeploymentPhase.Release
            });
        }

        return (StatusCodes.OK, new PhaseConfigResponse
        {
            CurrentPhase = config.CurrentPhase,
            MaxConcurrentScenariosGlobal = _configuration.MaxConcurrentScenariosGlobal,
            PersistentEntryEnabled = config.CurrentPhase >= DeploymentPhase.Beta,
            VoidMinigamesEnabled = config.CurrentPhase >= DeploymentPhase.Release
        });
    }

    /// <summary>
    /// Updates the deployment phase. Publishes a phase.changed event.
    /// </summary>
    public async Task<(StatusCodes, PhaseConfigResponse?)> UpdatePhaseConfigAsync(UpdatePhaseConfigRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("UpdatePhaseConfig: NewPhase={NewPhase}", body.NewPhase);

        var phaseStore = _stateStoreFactory.GetStore<PhaseConfigModel>(StateStoreDefinitions.GardenerPhaseConfig);
        var existing = await phaseStore.GetAsync("phase:current", cancellationToken);

        var previousPhase = existing?.CurrentPhase ?? _configuration.DefaultPhase;

        var config = existing ?? new PhaseConfigModel
        {
            PhaseConfigId = Guid.NewGuid()
        };

        config.CurrentPhase = body.NewPhase;
        config.LastChangedAt = DateTimeOffset.UtcNow;
        config.ChangedBy = body.ChangedBy;

        await phaseStore.SaveAsync("phase:current", config, cancellationToken);

        if (previousPhase != body.NewPhase)
        {
            await _messageBus.TryPublishAsync("gardener.phase.changed", new GardenerPhaseChangedEvent
            {
                EventId = Guid.NewGuid(),
                PreviousPhase = previousPhase,
                NewPhase = body.NewPhase
            }, cancellationToken: cancellationToken);

            _logger.LogInformation("Phase changed: {PreviousPhase} -> {NewPhase}, ChangedBy={ChangedBy}", previousPhase, body.NewPhase, body.ChangedBy);
        }

        return (StatusCodes.OK, new PhaseConfigResponse
        {
            CurrentPhase = config.CurrentPhase,
            MaxConcurrentScenariosGlobal = _configuration.MaxConcurrentScenariosGlobal,
            PersistentEntryEnabled = config.CurrentPhase >= DeploymentPhase.Beta,
            VoidMinigamesEnabled = config.CurrentPhase >= DeploymentPhase.Release
        });
    }

    /// <summary>
    /// Returns metrics about the current deployment phase.
    /// </summary>
    public async Task<(StatusCodes, PhaseMetricsResponse?)> GetPhaseMetricsAsync(GetPhaseMetricsRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("GetPhaseMetrics");

        var phaseStore = _stateStoreFactory.GetStore<PhaseConfigModel>(StateStoreDefinitions.GardenerPhaseConfig);
        var config = await phaseStore.GetAsync("phase:current", cancellationToken);

        var currentPhase = config?.CurrentPhase ?? _configuration.DefaultPhase;
        var activeScenarios = config?.ActiveScenarioCount ?? 0;
        var activeVoids = config?.TotalVoidEntries ?? 0;

        var utilization = _configuration.MaxConcurrentScenariosGlobal > 0
            ? (float)activeScenarios / _configuration.MaxConcurrentScenariosGlobal
            : 0f;

        return (StatusCodes.OK, new PhaseMetricsResponse
        {
            CurrentPhase = currentPhase,
            ActiveVoidInstances = activeVoids,
            ActiveScenarioInstances = activeScenarios,
            ScenarioCapacityUtilization = utilization
        });
    }

    // =========================================================================
    // Bond Features
    // =========================================================================

    /// <summary>
    /// Creates a shared scenario instance for bonded players entering together.
    /// </summary>
    public async Task<(StatusCodes, ScenarioStateResponse?)> EnterScenarioTogetherAsync(EnterTogetherRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("EnterScenarioTogether: BondId={BondId}, TemplateId={TemplateId}", body.BondId, body.ScenarioTemplateId);

        if (!_configuration.BondSharedVoidEnabled)
        {
            _logger.LogDebug("Bond shared void is disabled");
            return (StatusCodes.BadRequest, null);
        }

        var templateStore = _stateStoreFactory.GetStore<ScenarioTemplateModel>(StateStoreDefinitions.GardenerScenarioTemplates);
        var template = await templateStore.GetAsync($"template:{body.ScenarioTemplateId}", cancellationToken);

        if (template == null || template.Status != TemplateStatus.Active)
        {
            return (StatusCodes.NotFound, null);
        }

        if (!template.BondCompatible)
        {
            _logger.LogDebug("Template {TemplateId} is not bond-compatible", body.ScenarioTemplateId);
            return (StatusCodes.BadRequest, null);
        }

        var now = DateTimeOffset.UtcNow;
        var scenarioInstance = new ScenarioInstanceModel
        {
            ScenarioInstanceId = Guid.NewGuid(),
            ScenarioTemplateId = body.ScenarioTemplateId,
            GameSessionId = body.GameSessionId,
            AccountId = body.AccountId,
            SeedId = body.SeedId,
            Status = ScenarioInstanceStatus.Active,
            StartedAt = now,
            LastActivityAt = now,
            ChainDepth = 0,
            BondId = body.BondId,
            BondParticipants = body.ParticipantSeedIds?.ToList()
        };

        var scenarioStore = _stateStoreFactory.GetStore<ScenarioInstanceModel>(StateStoreDefinitions.GardenerScenarioInstances);
        await scenarioStore.SaveAsync($"scenario:{scenarioInstance.ScenarioInstanceId}", scenarioInstance, cancellationToken);

        await _messageBus.TryPublishAsync("gardener.bond.entered-together", new GardenerBondEnteredTogetherEvent
        {
            EventId = Guid.NewGuid(),
            BondId = body.BondId,
            ScenarioInstanceId = scenarioInstance.ScenarioInstanceId,
            ScenarioTemplateId = body.ScenarioTemplateId,
            Participants = body.ParticipantSeedIds?.ToList() ?? new List<Guid>()
        }, cancellationToken: cancellationToken);

        _logger.LogInformation("Bond scenario started: InstanceId={InstanceId}, BondId={BondId}", scenarioInstance.ScenarioInstanceId, body.BondId);

        return (StatusCodes.Created, BuildScenarioStateResponse(scenarioInstance, template));
    }

    /// <summary>
    /// Gets the shared void state for a bonded pair.
    /// </summary>
    public async Task<(StatusCodes, SharedVoidStateResponse?)> GetSharedVoidStateAsync(GetSharedVoidRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("GetSharedVoidState: BondId={BondId}", body.BondId);

        if (!_configuration.BondSharedVoidEnabled)
        {
            return (StatusCodes.BadRequest, null);
        }

        // Find void instances for this bond's participants
        var voidStore = _stateStoreFactory.GetStore<VoidInstanceModel>(StateStoreDefinitions.GardenerVoidInstances);
        var participants = new List<BondedPlayerVoidState>();
        var sharedPois = new List<PoiSummary>();

        foreach (var accountId in body.AccountIds)
        {
            var voidInstance = await voidStore.GetAsync($"void:{accountId}", cancellationToken);
            if (voidInstance != null)
            {
                participants.Add(new BondedPlayerVoidState
                {
                    AccountId = accountId,
                    SeedId = voidInstance.SeedId,
                    Position = new Vec3
                    {
                        X = (float)voidInstance.PlayerPosition.X,
                        Y = (float)voidInstance.PlayerPosition.Y,
                        Z = (float)voidInstance.PlayerPosition.Z
                    }
                });

                var pois = await GetActivePoisAsync(voidInstance, cancellationToken);
                sharedPois.AddRange(pois.Select(BuildPoiSummary));
            }
        }

        if (participants.Count == 0)
        {
            return (StatusCodes.NotFound, null);
        }

        return (StatusCodes.OK, new SharedVoidStateResponse
        {
            BondId = body.BondId,
            Participants = participants,
            SharedPois = sharedPois.DistinctBy(p => p.PoiId).ToList()
        });
    }

    // =========================================================================
    // Background Processing (called by GardenerScenarioLifecycleWorker)
    // =========================================================================

    /// <summary>
    /// Processes active scenario instances for timeout and abandonment detection.
    /// Called periodically by <see cref="GardenerScenarioLifecycleWorker"/>.
    /// </summary>
    internal async Task ProcessScenarioLifecycleAsync(CancellationToken cancellationToken)
    {
        _logger.LogDebug("Processing scenario lifecycle");

        // This is called by the background worker.
        // In a production implementation, this would iterate active scenario keys in Redis
        // and check for timeouts and abandonment.
        // For now, the lifecycle worker is wired up and ready for implementation.
        await Task.CompletedTask;
    }

    // =========================================================================
    // Internal Helpers
    // =========================================================================

    /// <summary>
    /// Retrieves all active POIs for a void instance.
    /// </summary>
    private async Task<List<PoiModel>> GetActivePoisAsync(VoidInstanceModel voidInstance, CancellationToken cancellationToken)
    {
        var poiStore = _stateStoreFactory.GetStore<PoiModel>(StateStoreDefinitions.GardenerPois);
        var pois = new List<PoiModel>();

        foreach (var poiId in voidInstance.ActivePoiIds)
        {
            var poi = await poiStore.GetAsync($"poi:{poiId}", cancellationToken);
            if (poi != null)
            {
                // Check expiration
                if (poi.ExpiresAt <= DateTimeOffset.UtcNow)
                {
                    await poiStore.DeleteAsync($"poi:{poiId}", cancellationToken);
                    await _messageBus.TryPublishAsync("gardener.poi.expired", new GardenerPoiExpiredEvent
                    {
                        EventId = Guid.NewGuid(),
                        VoidInstanceId = voidInstance.VoidInstanceId,
                        PoiId = poiId
                    }, cancellationToken: cancellationToken);
                    continue;
                }
                pois.Add(poi);
            }
        }

        return pois;
    }

    /// <summary>
    /// Awards growth to a seed via the ISeedClient (L4 -> L2 direct API call).
    /// </summary>
    private async Task AwardGrowthAsync(Guid seedId, Dictionary<string, float> growthAwards, string source, CancellationToken cancellationToken)
    {
        var seedClient = _serviceProvider.GetService<ISeedClient>();
        if (seedClient == null)
        {
            _logger.LogError("ISeedClient not available, cannot award growth for seed {SeedId}", seedId);
            return;
        }

        try
        {
            var growthEntries = growthAwards.Select(kvp => new RecordGrowthBatchRequest_GrowthEntries
            {
                Domain = kvp.Key,
                Amount = kvp.Value,
                Source = source
            }).ToList();

            await seedClient.RecordGrowthBatchAsync(new RecordGrowthBatchRequest
            {
                SeedId = seedId,
                GrowthEntries = growthEntries
            }, cancellationToken);

            _logger.LogDebug("Growth awarded to seed {SeedId}: {Domains}", seedId, string.Join(", ", growthAwards.Keys));
        }
        catch (ApiException ex)
        {
            _logger.LogError(ex, "Failed to award growth to seed {SeedId}", seedId);
        }
    }

    /// <summary>
    /// Records a completed scenario in durable history for cooldown tracking.
    /// </summary>
    private async Task RecordScenarioHistoryAsync(ScenarioInstanceModel instance, ScenarioOutcome outcome, Dictionary<string, float> growthAwarded, CancellationToken cancellationToken)
    {
        var historyStore = _stateStoreFactory.GetStore<ScenarioHistoryModel>(StateStoreDefinitions.GardenerScenarioHistory);

        var durationSeconds = instance.CompletedAt.HasValue
            ? (instance.CompletedAt.Value - instance.StartedAt).TotalSeconds
            : 0;

        var history = new ScenarioHistoryModel
        {
            HistoryId = Guid.NewGuid(),
            ScenarioInstanceId = instance.ScenarioInstanceId,
            ScenarioTemplateId = instance.ScenarioTemplateId,
            AccountId = instance.AccountId,
            Outcome = outcome,
            DurationSeconds = durationSeconds,
            GrowthAwarded = growthAwarded.ToDictionary(kvp => kvp.Key, kvp => (double)kvp.Value),
            ChainDepth = instance.ChainDepth,
            CompletedAt = instance.CompletedAt ?? DateTimeOffset.UtcNow
        };

        await historyStore.SaveAsync($"history:{history.HistoryId}", history, cancellationToken);
    }

    /// <summary>
    /// Builds a <see cref="VoidStateResponse"/> from internal models.
    /// </summary>
    private static VoidStateResponse BuildVoidStateResponse(VoidInstanceModel voidInstance, List<PoiModel> pois)
    {
        return new VoidStateResponse
        {
            VoidInstanceId = voidInstance.VoidInstanceId,
            SeedId = voidInstance.SeedId,
            AccountId = voidInstance.AccountId,
            Position = new Vec3
            {
                X = (float)voidInstance.PlayerPosition.X,
                Y = (float)voidInstance.PlayerPosition.Y,
                Z = (float)voidInstance.PlayerPosition.Z
            },
            ActivePois = pois.Select(BuildPoiSummary).ToList()
        };
    }

    /// <summary>
    /// Builds a <see cref="PoiSummary"/> from an internal POI model.
    /// </summary>
    private static PoiSummary BuildPoiSummary(PoiModel poi)
    {
        return new PoiSummary
        {
            PoiId = poi.PoiId,
            Position = new Vec3
            {
                X = (float)poi.Position.X,
                Y = (float)poi.Position.Y,
                Z = (float)poi.Position.Z
            },
            PoiType = poi.PoiType
        };
    }

    /// <summary>
    /// Builds a <see cref="ScenarioStateResponse"/> from internal models.
    /// </summary>
    private static ScenarioStateResponse BuildScenarioStateResponse(ScenarioInstanceModel instance, ScenarioTemplateModel template)
    {
        return new ScenarioStateResponse
        {
            ScenarioInstanceId = instance.ScenarioInstanceId,
            ScenarioTemplateId = instance.ScenarioTemplateId,
            GameSessionId = instance.GameSessionId,
            ConnectivityMode = template.ConnectivityMode,
            Status = instance.Status == ScenarioInstanceStatus.Active ? ScenarioStatus.Active : ScenarioStatus.Completed,
            CreatedAt = instance.StartedAt,
            ChainedFrom = instance.PreviousScenarioInstanceId,
            ChainDepth = instance.ChainDepth
        };
    }

    /// <summary>
    /// Builds a <see cref="ScenarioTemplateResponse"/> from an internal template model.
    /// </summary>
    private static ScenarioTemplateResponse BuildTemplateResponse(ScenarioTemplateModel template)
    {
        return new ScenarioTemplateResponse
        {
            ScenarioTemplateId = template.ScenarioTemplateId,
            Code = template.Code,
            DisplayName = template.DisplayName,
            Description = template.Description,
            Category = template.Category,
            ConnectivityMode = template.ConnectivityMode,
            MinimumPhase = template.MinimumPhase,
            Status = template.Status,
            EstimatedDurationMinutes = template.EstimatedDurationMinutes,
            BondCompatible = template.BondCompatible,
            MaxConcurrentInstances = template.MaxConcurrentInstances,
            GrowthAwards = template.GrowthAwards.ToDictionary(kvp => kvp.Key, kvp => (float)kvp.Value),
            DomainAffinities = template.DomainAffinities.ToDictionary(kvp => kvp.Key, kvp => (float)kvp.Value),
            ChainTargets = template.ChainTargets.ToList(),
            Tags = template.Tags.ToList(),
            CreatedAt = template.CreatedAt,
            UpdatedAt = template.UpdatedAt
        };
    }
}
