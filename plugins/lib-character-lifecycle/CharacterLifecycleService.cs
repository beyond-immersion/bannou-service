using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Character;
using BeyondImmersion.BannouService.Contract;
using BeyondImmersion.BannouService.Currency;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.GameService;
using BeyondImmersion.BannouService.Inventory;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Relationship;
using BeyondImmersion.BannouService.Resource;
using BeyondImmersion.BannouService.Seed;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.Species;
using BeyondImmersion.BannouService.State;
using BeyondImmersion.BannouService.Worldstate;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.CharacterLifecycle;

/// <summary>
/// Generational cycle orchestration and genetic heritage service.
/// Manages character aging, marriage, procreation, death processing,
/// and cross-generational trait inheritance.
/// </summary>
[BannouService("character-lifecycle", typeof(ICharacterLifecycleService), lifetime: ServiceLifetime.Scoped, layer: ServiceLayer.GameFeatures)]
public partial class CharacterLifecycleService : ICharacterLifecycleService
{
    private readonly ILogger<CharacterLifecycleService> _logger;
    private readonly CharacterLifecycleServiceConfiguration _configuration;
    private readonly IMessageBus _messageBus;
    private readonly ITelemetryProvider _telemetryProvider;
    private readonly IResourceClient _resourceClient;
    private readonly IServiceProvider _serviceProvider;

    // Constructor-cached state stores (per FOUNDATION TENETS)
    private readonly IStateStore<LifecycleProfileModel> _profileStore;
    private readonly IStateStore<PendingPregnancyModel> _pregnancyStore;
    private readonly IStateStore<LifecycleTemplateModel> _lifecycleTemplateStore;
    private readonly IStateStore<HeritableTraitTemplateModel> _traitTemplateStore;
    private readonly IStateStore<HybridTraitTemplateModel> _hybridTemplateStore;
    private readonly IStateStore<GeneticProfileModel> _geneticStore;
    private readonly IStateStore<BloodlineModel> _bloodlineStore;
    private readonly IStateStore<BloodlineMembershipModel> _membershipStore;
    private readonly IStateStore<BloodlineMemberListModel> _memberListStore;
    private readonly IStateStore<string> _bloodlineCodeStore;
    private readonly IStateStore<LifecycleManifestModel> _cacheStore;
    private readonly IQueryableStateStore<LifecycleTemplateModel> _queryableLifecycleTemplateStore;
    private readonly IQueryableStateStore<HeritableTraitTemplateModel> _queryableTraitTemplateStore;
    private readonly IQueryableStateStore<HybridTraitTemplateModel> _queryableHybridTemplateStore;
    private readonly IQueryableStateStore<BloodlineModel> _queryableBloodlineStore;
    private readonly IQueryableStateStore<LifecycleProfileModel> _queryableProfileStore;
    private readonly IDistributedLockProvider _lockProvider;

    // Hard dependencies (L0/L1/L2 — fail at startup if missing)
    private readonly ICharacterClient _characterClient;
    private readonly IRelationshipClient _relationshipClient;
    private readonly ISpeciesClient _speciesClient;
    private readonly IWorldstateClient _worldstateClient;
    private readonly IContractClient _contractClient;
    private readonly ISeedClient _seedClient;
    private readonly IGameServiceClient _gameServiceClient;
    private readonly IInventoryClient _inventoryClient;
    private readonly ICurrencyClient _currencyClient;

    #region Key Building Helpers

    private const string PROFILE_KEY_PREFIX = "profile:";
    private const string GENETIC_KEY_PREFIX = "genetic:";
    private const string TRAIT_TEMPLATE_KEY_PREFIX = "trait-template:";
    private const string LIFECYCLE_TEMPLATE_KEY_PREFIX = "lifecycle-template:";
    private const string HYBRID_TEMPLATE_KEY_PREFIX = "hybrid-template:";
    private const string BLOODLINE_KEY_PREFIX = "bloodline:";
    private const string BLOODLINE_CODE_KEY_PREFIX = "bloodline:code:";
    private const string BLOODLINE_MEMBER_KEY_PREFIX = "bloodline:member:";
    private const string BLOODLINE_MEMBERS_KEY_PREFIX = "bloodline:members:";
    private const string MANIFEST_KEY_PREFIX = "manifest:";
    private const string PREGNANCY_KEY_PREFIX = "pregnancy-pending:";

    internal static string BuildProfileKey(Guid characterId)
        => $"{PROFILE_KEY_PREFIX}{characterId}";

    internal static string BuildGeneticKey(Guid characterId)
        => $"{GENETIC_KEY_PREFIX}{characterId}";

    internal static string BuildTraitTemplateKey(string speciesCode, Guid gameServiceId)
        => $"{TRAIT_TEMPLATE_KEY_PREFIX}{speciesCode}:{gameServiceId}";

    internal static string BuildLifecycleTemplateKey(string speciesCode, Guid gameServiceId)
        => $"{LIFECYCLE_TEMPLATE_KEY_PREFIX}{speciesCode}:{gameServiceId}";

    internal static string BuildHybridTemplateKey(string speciesA, string speciesB, Guid gameServiceId)
        => $"{HYBRID_TEMPLATE_KEY_PREFIX}{speciesA}:{speciesB}:{gameServiceId}";

    internal static string BuildBloodlineKey(Guid bloodlineId)
        => $"{BLOODLINE_KEY_PREFIX}{bloodlineId}";

    internal static string BuildBloodlineCodeKey(Guid gameServiceId, string bloodlineCode)
        => $"{BLOODLINE_CODE_KEY_PREFIX}{gameServiceId}:{bloodlineCode}";

    internal static string BuildBloodlineMemberKey(Guid characterId)
        => $"{BLOODLINE_MEMBER_KEY_PREFIX}{characterId}";

    internal static string BuildBloodlineMembersKey(Guid bloodlineId)
        => $"{BLOODLINE_MEMBERS_KEY_PREFIX}{bloodlineId}";

    internal static string BuildManifestKey(Guid characterId)
        => $"{MANIFEST_KEY_PREFIX}{characterId}";

    #endregion

    public CharacterLifecycleService(
        ILogger<CharacterLifecycleService> logger,
        CharacterLifecycleServiceConfiguration configuration,
        IStateStoreFactory stateStoreFactory,
        IDistributedLockProvider lockProvider,
        IMessageBus messageBus,
        IEventConsumer eventConsumer,
        ITelemetryProvider telemetryProvider,
        ICharacterClient characterClient,
        IRelationshipClient relationshipClient,
        ISpeciesClient speciesClient,
        IWorldstateClient worldstateClient,
        IContractClient contractClient,
        IResourceClient resourceClient,
        ISeedClient seedClient,
        IGameServiceClient gameServiceClient,
        IInventoryClient inventoryClient,
        ICurrencyClient currencyClient,
        IServiceProvider serviceProvider)
    {
        _logger = logger;
        _configuration = configuration;
        _messageBus = messageBus;
        _telemetryProvider = telemetryProvider;
        _resourceClient = resourceClient;
        _lockProvider = lockProvider;
        _serviceProvider = serviceProvider;

        // Constructor-cached store references (per FOUNDATION TENETS)
        _profileStore = stateStoreFactory.GetStore<LifecycleProfileModel>(StateStoreDefinitions.CharacterLifecycleProfiles);
        _pregnancyStore = stateStoreFactory.GetStore<PendingPregnancyModel>(StateStoreDefinitions.CharacterLifecycleProfiles);
        _lifecycleTemplateStore = stateStoreFactory.GetStore<LifecycleTemplateModel>(StateStoreDefinitions.CharacterLifecycleHeritage);
        _traitTemplateStore = stateStoreFactory.GetStore<HeritableTraitTemplateModel>(StateStoreDefinitions.CharacterLifecycleHeritage);
        _hybridTemplateStore = stateStoreFactory.GetStore<HybridTraitTemplateModel>(StateStoreDefinitions.CharacterLifecycleHeritage);
        _geneticStore = stateStoreFactory.GetStore<GeneticProfileModel>(StateStoreDefinitions.CharacterLifecycleHeritage);
        _bloodlineStore = stateStoreFactory.GetStore<BloodlineModel>(StateStoreDefinitions.CharacterLifecycleBloodlines);
        _membershipStore = stateStoreFactory.GetStore<BloodlineMembershipModel>(StateStoreDefinitions.CharacterLifecycleBloodlines);
        _memberListStore = stateStoreFactory.GetStore<BloodlineMemberListModel>(StateStoreDefinitions.CharacterLifecycleBloodlines);
        _bloodlineCodeStore = stateStoreFactory.GetStore<string>(StateStoreDefinitions.CharacterLifecycleBloodlines);
        _cacheStore = stateStoreFactory.GetStore<LifecycleManifestModel>(StateStoreDefinitions.CharacterLifecycleCache);
        _queryableLifecycleTemplateStore = stateStoreFactory.GetQueryableStore<LifecycleTemplateModel>(StateStoreDefinitions.CharacterLifecycleHeritage);
        _queryableTraitTemplateStore = stateStoreFactory.GetQueryableStore<HeritableTraitTemplateModel>(StateStoreDefinitions.CharacterLifecycleHeritage);
        _queryableHybridTemplateStore = stateStoreFactory.GetQueryableStore<HybridTraitTemplateModel>(StateStoreDefinitions.CharacterLifecycleHeritage);
        _queryableBloodlineStore = stateStoreFactory.GetQueryableStore<BloodlineModel>(StateStoreDefinitions.CharacterLifecycleBloodlines);
        _queryableProfileStore = stateStoreFactory.GetQueryableStore<LifecycleProfileModel>(StateStoreDefinitions.CharacterLifecycleProfiles);

        // Hard dependencies (L1/L2)
        _characterClient = characterClient;
        _relationshipClient = relationshipClient;
        _speciesClient = speciesClient;
        _worldstateClient = worldstateClient;
        _contractClient = contractClient;
        _seedClient = seedClient;
        _gameServiceClient = gameServiceClient;
        _inventoryClient = inventoryClient;
        _currencyClient = currencyClient;

        // Event consumer registration (partial class)
        RegisterEventConsumers(eventConsumer);
    }

    /// <summary>
    /// Implementation of InitiateMarriage operation.
    /// TODO: Implement business logic for this method.
    /// </summary>
    public async Task<(StatusCodes, InitiateMarriageResponse?)> InitiateMarriageAsync(InitiateMarriageRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Executing InitiateMarriage operation");

        // TODO: Implement your business logic here
        // Note: The generated controller wraps this method with try/catch for error handling.
        // Do NOT add an outer try/catch here -- exceptions will be caught, logged, and
        // published as error events by the controller automatically.
        await Task.CompletedTask; // IMPLEMENTATION TENETS: async methods must contain await
        throw new NotImplementedException("Method InitiateMarriage not yet implemented");

        // Example patterns using infrastructure libs:
        //
        // For data retrieval (lib-state):
        // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
        // var data = await stateStore.GetAsync(key, cancellationToken);
        // return data != null ? (StatusCodes.OK, data) : (StatusCodes.NotFound, default);
        //
        // For data creation (lib-state):
        // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
        // await stateStore.SaveAsync(key, newData, cancellationToken);
        // return (StatusCodes.Created, newData);
        //
        // For data updates (lib-state):
        // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
        // var existing = await stateStore.GetAsync(key, cancellationToken);
        // if (existing == null) return (StatusCodes.NotFound, default);
        // await stateStore.SaveAsync(key, updatedData, cancellationToken);
        // return (StatusCodes.OK, updatedData);
        //
        // For data deletion (lib-state):
        // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
        // await stateStore.DeleteAsync(key, cancellationToken);
        // return StatusCodes.NoContent;
        //
        // For event publishing (lib-messaging):
        // await _messageBus.TryPublishAsync("topic.name", eventModel, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Implementation of InitiateProcreation operation.
    /// </summary>
    public async Task<(StatusCodes, InitiateProcreationResponse?)> InitiateProcreationAsync(InitiateProcreationRequest body, CancellationToken cancellationToken)
    {
        // TODO: Implement InitiateProcreation
        await Task.CompletedTask;
        throw new NotImplementedException("Method InitiateProcreation not yet implemented");
    }

    /// <summary>
    /// Implementation of RecordDeath operation.
    /// </summary>
    public async Task<(StatusCodes, RecordDeathResponse?)> RecordDeathAsync(RecordDeathRequest body, CancellationToken cancellationToken)
    {
        // TODO: Implement RecordDeath
        await Task.CompletedTask;
        throw new NotImplementedException("Method RecordDeath not yet implemented");
    }

    /// <summary>
    /// Implementation of GetLifecycleProfile operation.
    /// </summary>
    public async Task<(StatusCodes, GetLifecycleProfileResponse?)> GetLifecycleProfileAsync(GetLifecycleProfileRequest body, CancellationToken cancellationToken)
    {
        // TODO: Implement GetLifecycleProfile
        await Task.CompletedTask;
        throw new NotImplementedException("Method GetLifecycleProfile not yet implemented");
    }

    /// <summary>
    /// Implementation of QueryByStage operation.
    /// </summary>
    public async Task<(StatusCodes, QueryByStageResponse?)> QueryByStageAsync(QueryByStageRequest body, CancellationToken cancellationToken)
    {
        // TODO: Implement QueryByStage
        await Task.CompletedTask;
        throw new NotImplementedException("Method QueryByStage not yet implemented");
    }

    /// <summary>
    /// Implementation of QueryByBloodline operation.
    /// </summary>
    public async Task<(StatusCodes, QueryByBloodlineResponse?)> QueryByBloodlineAsync(QueryByBloodlineRequest body, CancellationToken cancellationToken)
    {
        // TODO: Implement QueryByBloodline
        await Task.CompletedTask;
        throw new NotImplementedException("Method QueryByBloodline not yet implemented");
    }

    /// <summary>
    /// Implementation of SetNaturalDeathYear operation.
    /// </summary>
    public async Task<(StatusCodes, SetNaturalDeathYearResponse?)> SetNaturalDeathYearAsync(SetNaturalDeathYearRequest body, CancellationToken cancellationToken)
    {
        // TODO: Implement SetNaturalDeathYear
        await Task.CompletedTask;
        throw new NotImplementedException("Method SetNaturalDeathYear not yet implemented");
    }

    /// <summary>
    /// Implementation of SeedLifecycleProfile operation.
    /// </summary>
    public async Task<(StatusCodes, SeedLifecycleProfileResponse?)> SeedLifecycleProfileAsync(SeedLifecycleProfileRequest body, CancellationToken cancellationToken)
    {
        // TODO: Implement SeedLifecycleProfile
        await Task.CompletedTask;
        throw new NotImplementedException("Method SeedLifecycleProfile not yet implemented");
    }

    /// <summary>
    /// Implementation of GetGeneticProfile operation.
    /// </summary>
    public async Task<(StatusCodes, GetGeneticProfileResponse?)> GetGeneticProfileAsync(GetGeneticProfileRequest body, CancellationToken cancellationToken)
    {
        // TODO: Implement GetGeneticProfile
        await Task.CompletedTask;
        throw new NotImplementedException("Method GetGeneticProfile not yet implemented");
    }

    /// <summary>
    /// Implementation of GetPhenotype operation.
    /// </summary>
    public async Task<(StatusCodes, GetPhenotypeResponse?)> GetPhenotypeAsync(GetPhenotypeRequest body, CancellationToken cancellationToken)
    {
        // TODO: Implement GetPhenotype
        await Task.CompletedTask;
        throw new NotImplementedException("Method GetPhenotype not yet implemented");
    }

    /// <summary>
    /// Implementation of QueryByAptitude operation.
    /// </summary>
    public async Task<(StatusCodes, QueryByAptitudeResponse?)> QueryByAptitudeAsync(QueryByAptitudeRequest body, CancellationToken cancellationToken)
    {
        // TODO: Implement QueryByAptitude
        await Task.CompletedTask;
        throw new NotImplementedException("Method QueryByAptitude not yet implemented");
    }

    /// <summary>
    /// Implementation of SeedGeneticProfile operation.
    /// </summary>
    public async Task<(StatusCodes, SeedGeneticProfileResponse?)> SeedGeneticProfileAsync(SeedGeneticProfileRequest body, CancellationToken cancellationToken)
    {
        // TODO: Implement SeedGeneticProfile
        await Task.CompletedTask;
        throw new NotImplementedException("Method SeedGeneticProfile not yet implemented");
    }

    /// <summary>
    /// Implementation of SimulateOffspring operation.
    /// </summary>
    public async Task<(StatusCodes, SimulateOffspringResponse?)> SimulateOffspringAsync(SimulateOffspringRequest body, CancellationToken cancellationToken)
    {
        // TODO: Implement SimulateOffspring
        await Task.CompletedTask;
        throw new NotImplementedException("Method SimulateOffspring not yet implemented");
    }

    /// <summary>
    /// Implementation of GetFamilyTree operation.
    /// </summary>
    public async Task<(StatusCodes, GetFamilyTreeResponse?)> GetFamilyTreeAsync(GetFamilyTreeRequest body, CancellationToken cancellationToken)
    {
        // TODO: Implement GetFamilyTree
        await Task.CompletedTask;
        throw new NotImplementedException("Method GetFamilyTree not yet implemented");
    }

    /// <summary>
    /// Creates lifecycle stage definitions for a species. Validates game service,
    /// checks for existing template, validates stage boundary contiguity.
    /// </summary>
    public async Task<(StatusCodes, SeedLifecycleTemplateResponse?)> SeedLifecycleTemplateAsync(SeedLifecycleTemplateRequest body, CancellationToken cancellationToken)
    {
        // Validate game service (per map: CALL IGameServiceClient.ValidateAsync → 400)
        try
        {
            await _gameServiceClient.GetServiceAsync(
                new GetServiceRequest { ServiceId = body.GameServiceId }, cancellationToken);
        }
        catch (ApiException)
        {
            _logger.LogWarning("Game service validation failed for {GameServiceId}", body.GameServiceId);
            return (StatusCodes.BadRequest, null);
        }

        // Check existing (per map: READ → 409 if exists)
        var key = BuildLifecycleTemplateKey(body.SpeciesCode, body.GameServiceId);
        var existing = await _lifecycleTemplateStore.GetAsync(key, cancellationToken);
        if (existing != null)
            return (StatusCodes.Conflict, null);

        // Validate stage boundaries are contiguous (per map line 448)
        var sortedStages = body.Stages.OrderBy(s => s.MinAge).ToList();
        for (var i = 0; i < sortedStages.Count - 1; i++)
        {
            var current = sortedStages[i];
            var next = sortedStages[i + 1];
            if (current.MaxAge == null || current.MaxAge + 1 != next.MinAge)
            {
                _logger.LogWarning("Stage boundaries not contiguous between {Current} and {Next}",
                    current.Code, next.Code);
                return (StatusCodes.BadRequest, null);
            }
        }

        // Create and save model
        var now = DateTimeOffset.UtcNow;
        var model = new LifecycleTemplateModel
        {
            SpeciesCode = body.SpeciesCode,
            GameServiceId = body.GameServiceId,
            Stages = body.Stages.ToList(),
            NaturalDeathRange = body.NaturalDeathRange,
            FertilityWindow = body.FertilityWindow,
            CreatedAt = now
        };

        await _lifecycleTemplateStore.SaveAsync(key, model, cancellationToken: cancellationToken);

        // Publish lifecycle event (per map: PUBLISH character-lifecycle.lifecycle-template.created)
        await _messageBus.PublishLifecycleTemplateCreatedAsync(
            new LifecycleTemplateCreatedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = now,
                SpeciesCode = body.SpeciesCode,
                GameServiceId = body.GameServiceId,
                Stages = body.Stages.ToList(),
                NaturalDeathRange = body.NaturalDeathRange,
                FertilityWindow = body.FertilityWindow,
                CreatedAt = now
            }, cancellationToken);

        _logger.LogInformation("Created lifecycle template for species {SpeciesCode} in game {GameServiceId}",
            body.SpeciesCode, body.GameServiceId);

        return (StatusCodes.OK, new SeedLifecycleTemplateResponse());
    }

    /// <summary>
    /// Creates heritable trait definitions for a species. Validates game service,
    /// checks for existing template.
    /// </summary>
    public async Task<(StatusCodes, SeedHeritableTraitTemplateResponse?)> SeedHeritableTraitTemplateAsync(SeedHeritableTraitTemplateRequest body, CancellationToken cancellationToken)
    {
        try
        {
            await _gameServiceClient.GetServiceAsync(
                new GetServiceRequest { ServiceId = body.GameServiceId }, cancellationToken);
        }
        catch (ApiException)
        {
            _logger.LogWarning("Game service validation failed for {GameServiceId}", body.GameServiceId);
            return (StatusCodes.BadRequest, null);
        }

        var key = BuildTraitTemplateKey(body.SpeciesCode, body.GameServiceId);
        var existing = await _traitTemplateStore.GetAsync(key, cancellationToken);
        if (existing != null)
            return (StatusCodes.Conflict, null);

        var now = DateTimeOffset.UtcNow;
        var model = new HeritableTraitTemplateModel
        {
            SpeciesCode = body.SpeciesCode,
            GameServiceId = body.GameServiceId,
            Traits = body.Traits.ToList(),
            CreatedAt = now
        };

        await _traitTemplateStore.SaveAsync(key, model, cancellationToken: cancellationToken);

        await _messageBus.PublishHeritableTraitTemplateCreatedAsync(
            new HeritableTraitTemplateCreatedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = now,
                SpeciesCode = body.SpeciesCode,
                GameServiceId = body.GameServiceId,
                Traits = body.Traits.ToList(),
                CreatedAt = now
            }, cancellationToken);

        _logger.LogInformation("Created heritable trait template for species {SpeciesCode} in game {GameServiceId}",
            body.SpeciesCode, body.GameServiceId);

        return (StatusCodes.OK, new SeedHeritableTraitTemplateResponse());
    }

    /// <summary>
    /// Creates cross-species hybridization rules for a species pair.
    /// </summary>
    public async Task<(StatusCodes, SeedHybridTemplateResponse?)> SeedHybridTemplateAsync(SeedHybridTemplateRequest body, CancellationToken cancellationToken)
    {
        try
        {
            await _gameServiceClient.GetServiceAsync(
                new GetServiceRequest { ServiceId = body.GameServiceId }, cancellationToken);
        }
        catch (ApiException)
        {
            _logger.LogWarning("Game service validation failed for {GameServiceId}", body.GameServiceId);
            return (StatusCodes.BadRequest, null);
        }

        var key = BuildHybridTemplateKey(body.SpeciesA, body.SpeciesB, body.GameServiceId);
        var existing = await _hybridTemplateStore.GetAsync(key, cancellationToken);
        if (existing != null)
            return (StatusCodes.Conflict, null);

        var now = DateTimeOffset.UtcNow;
        var model = new HybridTraitTemplateModel
        {
            SpeciesA = body.SpeciesA,
            SpeciesB = body.SpeciesB,
            GameServiceId = body.GameServiceId,
            TraitOverrides = body.TraitOverrides.ToList(),
            HybridFertilityModifier = (float)body.HybridFertilityModifier,
            CreatedAt = now
        };

        await _hybridTemplateStore.SaveAsync(key, model, cancellationToken: cancellationToken);

        await _messageBus.PublishHybridTraitTemplateCreatedAsync(
            new HybridTraitTemplateCreatedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = now,
                SpeciesA = body.SpeciesA,
                SpeciesB = body.SpeciesB,
                GameServiceId = body.GameServiceId,
                TraitOverrides = body.TraitOverrides.ToList(),
                HybridFertilityModifier = model.HybridFertilityModifier,
                CreatedAt = now
            }, cancellationToken);

        _logger.LogInformation("Created hybrid template for {SpeciesA}x{SpeciesB} in game {GameServiceId}",
            body.SpeciesA, body.SpeciesB, body.GameServiceId);

        return (StatusCodes.OK, new SeedHybridTemplateResponse());
    }

    /// <summary>
    /// Returns lifecycle template for a species within a game service.
    /// </summary>
    public async Task<(StatusCodes, GetLifecycleTemplateResponse?)> GetLifecycleTemplateAsync(GetLifecycleTemplateRequest body, CancellationToken cancellationToken)
    {
        var key = BuildLifecycleTemplateKey(body.SpeciesCode, body.GameServiceId);
        var model = await _lifecycleTemplateStore.GetAsync(key, cancellationToken);
        if (model == null)
            return (StatusCodes.NotFound, null);

        return (StatusCodes.OK, new GetLifecycleTemplateResponse
        {
            SpeciesCode = model.SpeciesCode,
            GameServiceId = model.GameServiceId,
            Stages = model.Stages.ToList(),
            NaturalDeathRange = model.NaturalDeathRange,
            FertilityWindow = model.FertilityWindow,
            IsDeprecated = model.IsDeprecated,
            DeprecatedAt = model.DeprecatedAt,
            DeprecationReason = model.DeprecationReason
        });
    }

    /// <summary>
    /// Returns heritable trait template for a species within a game service.
    /// </summary>
    public async Task<(StatusCodes, GetHeritableTraitTemplateResponse?)> GetHeritableTraitTemplateAsync(GetHeritableTraitTemplateRequest body, CancellationToken cancellationToken)
    {
        var key = BuildTraitTemplateKey(body.SpeciesCode, body.GameServiceId);
        var model = await _traitTemplateStore.GetAsync(key, cancellationToken);
        if (model == null)
            return (StatusCodes.NotFound, null);

        return (StatusCodes.OK, new GetHeritableTraitTemplateResponse
        {
            SpeciesCode = model.SpeciesCode,
            GameServiceId = model.GameServiceId,
            Traits = model.Traits.ToList(),
            IsDeprecated = model.IsDeprecated,
            DeprecatedAt = model.DeprecatedAt,
            DeprecationReason = model.DeprecationReason
        });
    }

    /// <summary>
    /// Lists all lifecycle and heritage templates for a game service.
    /// Supports Category B deprecation filtering.
    /// </summary>
    public async Task<(StatusCodes, ListTemplatesResponse?)> ListTemplatesAsync(ListTemplatesRequest body, CancellationToken cancellationToken)
    {
        // Query all template types for this game service (MySQL queryable stores, constructor-cached)
        var lifecycleResults = body.IncludeDeprecated
            ? await _queryableLifecycleTemplateStore.QueryAsync(t => t.GameServiceId == body.GameServiceId, cancellationToken)
            : await _queryableLifecycleTemplateStore.QueryAsync(t => t.GameServiceId == body.GameServiceId && !t.IsDeprecated, cancellationToken);

        var lifecycleTemplates = lifecycleResults.Select(t => new GetLifecycleTemplateResponse
        {
            SpeciesCode = t.SpeciesCode, GameServiceId = t.GameServiceId,
            Stages = t.Stages, NaturalDeathRange = t.NaturalDeathRange,
            FertilityWindow = t.FertilityWindow, IsDeprecated = t.IsDeprecated,
            DeprecatedAt = t.DeprecatedAt, DeprecationReason = t.DeprecationReason
        }).ToList();

        var traitResults = body.IncludeDeprecated
            ? await _queryableTraitTemplateStore.QueryAsync(t => t.GameServiceId == body.GameServiceId, cancellationToken)
            : await _queryableTraitTemplateStore.QueryAsync(t => t.GameServiceId == body.GameServiceId && !t.IsDeprecated, cancellationToken);

        var traitTemplates = traitResults.Select(t => new GetHeritableTraitTemplateResponse
        {
            SpeciesCode = t.SpeciesCode, GameServiceId = t.GameServiceId,
            Traits = t.Traits, IsDeprecated = t.IsDeprecated,
            DeprecatedAt = t.DeprecatedAt, DeprecationReason = t.DeprecationReason
        }).ToList();

        var hybridResults = body.IncludeDeprecated
            ? await _queryableHybridTemplateStore.QueryAsync(t => t.GameServiceId == body.GameServiceId, cancellationToken)
            : await _queryableHybridTemplateStore.QueryAsync(t => t.GameServiceId == body.GameServiceId && !t.IsDeprecated, cancellationToken);

        var hybridTemplates = hybridResults.Select(t => new GetHybridTraitTemplateResponse
        {
            SpeciesA = t.SpeciesA, SpeciesB = t.SpeciesB,
            GameServiceId = t.GameServiceId, TraitOverrides = t.TraitOverrides,
            HybridFertilityModifier = t.HybridFertilityModifier,
            IsDeprecated = t.IsDeprecated, DeprecatedAt = t.DeprecatedAt,
            DeprecationReason = t.DeprecationReason
        }).ToList();

        return (StatusCodes.OK, new ListTemplatesResponse
        {
            LifecycleTemplates = lifecycleTemplates,
            HeritableTraitTemplates = traitTemplates,
            HybridTemplates = hybridTemplates
        });
    }

    /// <summary>
    /// Returns bloodline definition by ID.
    /// </summary>
    public async Task<(StatusCodes, GetBloodlineResponse?)> GetBloodlineAsync(GetBloodlineRequest body, CancellationToken cancellationToken)
    {
        var model = await _bloodlineStore.GetAsync(BuildBloodlineKey(body.BloodlineId), cancellationToken);
        if (model == null)
            return (StatusCodes.NotFound, null);

        return (StatusCodes.OK, new GetBloodlineResponse
        {
            Bloodline = MapBloodlineToSummary(model)
        });
    }

    /// <summary>
    /// Lists bloodlines within a game service with optional filters.
    /// </summary>
    public async Task<(StatusCodes, ListBloodlinesResponse?)> ListBloodlinesAsync(ListBloodlinesRequest body, CancellationToken cancellationToken)
    {
        var results = await _queryableBloodlineStore.QueryAsync(
            b => b.GameServiceId == body.GameServiceId, cancellationToken);

        var filtered = results.AsEnumerable();

        if (body.TraitSignatureFilter != null && body.TraitSignatureFilter.Count > 0)
            filtered = filtered.Where(b => body.TraitSignatureFilter.All(t => b.TraitSignature.Contains(t)));

        if (body.MinGenerationDepth.HasValue)
            filtered = filtered.Where(b => b.GenerationSpan >= body.MinGenerationDepth.Value);

        if (body.MinMemberCount.HasValue)
            filtered = filtered.Where(b => b.MemberCount >= body.MinMemberCount.Value);

        var filteredList = filtered.ToList();
        var page = body.Page > 0 ? body.Page : 1;
        var pageSize = body.PageSize > 0 ? body.PageSize : _configuration.QueryPageSize;
        var paged = filteredList.Skip((page - 1) * pageSize).Take(pageSize);

        return (StatusCodes.OK, new ListBloodlinesResponse
        {
            Bloodlines = paged.Select(MapBloodlineToSummary).ToList(),
            TotalCount = filteredList.Count
        });
    }

    /// <summary>
    /// Manually establishes a bloodline. Creates record, code lookup, assigns members retroactively.
    /// Publishes bloodline.formed and bloodline.created events.
    /// </summary>
    public async Task<(StatusCodes, EstablishBloodlineResponse?)> EstablishBloodlineAsync(EstablishBloodlineRequest body, CancellationToken cancellationToken)
    {
        // Check code uniqueness (per map: READ code lookup → 409 if exists)
        var codeKey = BuildBloodlineCodeKey(body.GameServiceId, body.BloodlineCode);
        var existingCode = await _bloodlineCodeStore.GetAsync(codeKey, cancellationToken);
        if (existingCode != null)
            return (StatusCodes.Conflict, null);

        var bloodlineId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        // Create bloodline record
        var model = new BloodlineModel
        {
            BloodlineId = bloodlineId,
            BloodlineCode = body.BloodlineCode,
            GameServiceId = body.GameServiceId,
            OriginCharacterId = body.OriginCharacterId,
            OriginGameYear = 0, // Will be set from worldstate if needed
            TraitSignature = body.TraitSignature.ToList(),
            MemberCount = 0,
            GenerationSpan = 0,
            CreatedAt = now
        };

        // Save bloodline + code lookup
        await _bloodlineStore.SaveAsync(BuildBloodlineKey(bloodlineId), model, cancellationToken: cancellationToken);
        await _bloodlineCodeStore.SaveAsync(codeKey, bloodlineId.ToString(), cancellationToken: cancellationToken);

        // Retroactive ancestor assignment (per map lines 530-532)
        var allMembers = new List<Guid> { body.OriginCharacterId };
        if (body.AncestorCharacterIds != null)
            allMembers.AddRange(body.AncestorCharacterIds);

        foreach (var characterId in allMembers)
        {
            var memberKey = BuildBloodlineMemberKey(characterId);
            var membership = await _membershipStore.GetAsync(memberKey, cancellationToken)
                ?? new BloodlineMembershipModel { CharacterId = characterId, Bloodlines = new List<BloodlineEntry>() };

            membership.Bloodlines.Add(new BloodlineEntry
            {
                BloodlineId = bloodlineId,
                BloodlineCode = body.BloodlineCode,
                OriginCharacterId = body.OriginCharacterId,
                OriginGameYear = 0,
                TraitSignature = body.TraitSignature.ToList(),
                GenerationFrom = 0
            });

            await _membershipStore.SaveAsync(memberKey, membership, cancellationToken: cancellationToken);

            // Invalidate cache manifest (per map line 533)
            await _cacheStore.DeleteAsync(BuildManifestKey(characterId), cancellationToken);
        }

        // Save member list
        await _memberListStore.SaveAsync(BuildBloodlineMembersKey(bloodlineId),
            new BloodlineMemberListModel { BloodlineId = bloodlineId, MemberIds = allMembers },
            cancellationToken: cancellationToken);

        // Update member count
        model.MemberCount = allMembers.Count;
        await _bloodlineStore.SaveAsync(BuildBloodlineKey(bloodlineId), model, cancellationToken: cancellationToken);

        // Publish events (per map lines 534-535)
        await _messageBus.PublishCharacterLifecycleBloodlineFormedAsync(
            new CharacterLifecycleBloodlineFormedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = now,
                BloodlineCode = body.BloodlineCode,
                OriginCharacterId = body.OriginCharacterId,
                TraitSignature = body.TraitSignature.ToList()
            }, cancellationToken);

        await _messageBus.PublishBloodlineCreatedAsync(
            new BloodlineCreatedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = now,
                BloodlineId = bloodlineId,
                BloodlineCode = body.BloodlineCode,
                GameServiceId = body.GameServiceId,
                OriginCharacterId = body.OriginCharacterId,
                OriginGameYear = 0,
                TraitSignature = body.TraitSignature.ToList(),
                MemberCount = allMembers.Count,
                GenerationSpan = 0,
                CreatedAt = now
            }, cancellationToken);

        _logger.LogInformation("Established bloodline {BloodlineCode} with {MemberCount} initial members",
            body.BloodlineCode, allMembers.Count);

        return (StatusCodes.OK, new EstablishBloodlineResponse { BloodlineId = bloodlineId });
    }

    /// <summary>
    /// Deletes a bloodline (immediate hard delete, no deprecation).
    /// Calls lib-resource for CASCADE cleanup of membership indexes.
    /// </summary>
    public async Task<(StatusCodes, DeleteBloodlineResponse?)> DeleteBloodlineAsync(DeleteBloodlineRequest body, CancellationToken cancellationToken)
    {
        var model = await _bloodlineStore.GetAsync(BuildBloodlineKey(body.BloodlineId), cancellationToken);
        if (model == null)
            return (StatusCodes.NotFound, null);

        // CASCADE cleanup via lib-resource FIRST — membership indexes must be cleaned
        // before the bloodline record is deleted. If cleanup fails, nothing has been
        // deleted yet, so the caller can retry safely. Per IMPLEMENTATION TENETS (T7):
        // no irreversible mutations before the operation that might fail.
        await _resourceClient.ExecuteCleanupAsync(
            new ExecuteCleanupRequest { ResourceId = body.BloodlineId, ResourceType = "bloodline" },
            cancellationToken);

        // Delete bloodline record and code lookup (safe — cleanup already succeeded)
        await _bloodlineStore.DeleteAsync(BuildBloodlineKey(body.BloodlineId), cancellationToken);
        await _bloodlineCodeStore.DeleteAsync(
            BuildBloodlineCodeKey(model.GameServiceId, model.BloodlineCode), cancellationToken);

        // Publish deleted event
        await _messageBus.PublishBloodlineDeletedAsync(
            new BloodlineDeletedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                BloodlineId = body.BloodlineId,
                BloodlineCode = model.BloodlineCode,
                GameServiceId = model.GameServiceId,
                OriginCharacterId = model.OriginCharacterId,
                OriginGameYear = model.OriginGameYear,
                TraitSignature = model.TraitSignature,
                MemberCount = model.MemberCount,
                GenerationSpan = model.GenerationSpan,
                CreatedAt = model.CreatedAt
            }, cancellationToken);

        _logger.LogInformation("Deleted bloodline {BloodlineCode} ({BloodlineId})",
            model.BloodlineCode, body.BloodlineId);

        return (StatusCodes.OK, new DeleteBloodlineResponse());
    }

    /// <summary>
    /// Returns living members of a bloodline with generation depth and trait expression.
    /// </summary>
    public async Task<(StatusCodes, QueryBloodlineMembersResponse?)> QueryBloodlineMembersAsync(QueryBloodlineMembersRequest body, CancellationToken cancellationToken)
    {
        var memberList = await _memberListStore.GetAsync(
            BuildBloodlineMembersKey(body.BloodlineId), cancellationToken);
        if (memberList == null)
            return (StatusCodes.NotFound, null);

        // Query alive profiles for members
        var aliveMembers = new List<BloodlineMemberSummary>();
        foreach (var memberId in memberList.MemberIds)
        {
            var profile = await _profileStore.GetAsync(BuildProfileKey(memberId), cancellationToken);
            if (profile?.Status == LifecycleStatus.Alive)
            {
                var membership = await _membershipStore.GetAsync(
                    BuildBloodlineMemberKey(memberId), cancellationToken);
                var bloodlineEntry = membership?.Bloodlines
                    .FirstOrDefault(b => b.BloodlineId == body.BloodlineId);

                aliveMembers.Add(new BloodlineMemberSummary
                {
                    CharacterId = memberId,
                    GenerationFrom = bloodlineEntry?.GenerationFrom ?? 0
                });
            }
        }

        var page = body.Page > 0 ? body.Page : 1;
        var pageSize = body.PageSize > 0 ? body.PageSize : _configuration.QueryPageSize;
        var paged = aliveMembers.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        return (StatusCodes.OK, new QueryBloodlineMembersResponse
        {
            Members = paged,
            TotalCount = aliveMembers.Count
        });
    }

    private static BloodlineSummary MapBloodlineToSummary(BloodlineModel model) => new()
    {
        BloodlineId = model.BloodlineId,
        BloodlineCode = model.BloodlineCode,
        GameServiceId = model.GameServiceId,
        OriginCharacterId = model.OriginCharacterId,
        OriginGameYear = model.OriginGameYear,
        TraitSignature = model.TraitSignature,
        MemberCount = model.MemberCount,
        GenerationSpan = model.GenerationSpan
    };

    /// <summary>
    /// Implementation of CleanupByCharacter operation.
    /// </summary>
    public async Task<(StatusCodes, CleanupByCharacterResponse?)> CleanupByCharacterAsync(CleanupByCharacterRequest body, CancellationToken cancellationToken)
    {
        // TODO: Implement CleanupByCharacter
        await Task.CompletedTask;
        throw new NotImplementedException("Method CleanupByCharacter not yet implemented");
    }

    /// <summary>
    /// Implementation of CleanupByRealm operation.
    /// </summary>
    public async Task<(StatusCodes, CleanupByRealmResponse?)> CleanupByRealmAsync(CleanupByRealmRequest body, CancellationToken cancellationToken)
    {
        // TODO: Implement CleanupByRealm
        await Task.CompletedTask;
        throw new NotImplementedException("Method CleanupByRealm not yet implemented");
    }

    /// <summary>
    /// Implementation of GetCompressData operation.
    /// </summary>
    public async Task<(StatusCodes, LifecycleArchive?)> GetCompressDataAsync(GetCompressDataRequest body, CancellationToken cancellationToken)
    {
        // TODO: Implement GetCompressData
        await Task.CompletedTask;
        throw new NotImplementedException("Method GetCompressData not yet implemented");
    }

    /// <summary>
    /// Implementation of RestoreFromArchive operation.
    /// </summary>
    public async Task<(StatusCodes, RestoreFromArchiveResponse?)> RestoreFromArchiveAsync(RestoreFromArchiveRequest body, CancellationToken cancellationToken)
    {
        // TODO: Implement RestoreFromArchive
        await Task.CompletedTask;
        throw new NotImplementedException("Method RestoreFromArchive not yet implemented");
    }

}
