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
public partial class CharacterLifecycleService : ICharacterLifecycleService, ICleanDeprecatedEntity
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
    private readonly IStateStore<string> _profileStringStore;
    private readonly IStateStore<string> _heritageStringStore;
    private readonly IStateStore<LifecycleManifestModel> _cacheStore;
    private readonly IQueryableStateStore<LifecycleTemplateModel> _queryableLifecycleTemplateStore;
    private readonly IQueryableStateStore<HeritableTraitTemplateModel> _queryableTraitTemplateStore;
    private readonly IQueryableStateStore<HybridTraitTemplateModel> _queryableHybridTemplateStore;
    private readonly IQueryableStateStore<BloodlineModel> _queryableBloodlineStore;
    private readonly IQueryableStateStore<LifecycleProfileModel> _queryableProfileStore;
    private readonly IQueryableStateStore<GeneticProfileModel> _queryableGeneticStore;
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
    private const string LIFECYCLE_BY_TEMPLATE_PREFIX = "lifecycle-by-template:";
    private const string GENETIC_BY_TRAIT_TEMPLATE_PREFIX = "genetic-by-trait-template:";
    private const string GENETIC_BY_HYBRID_TEMPLATE_PREFIX = "genetic-by-hybrid-template:";

    /// <summary>
    /// Optimistic concurrency retry count for reverse-index string-list operations
    /// (AddToStringListAsync / RemoveFromStringListAsync / HasStringListEntriesAsync).
    /// Matches Seed's precedent for Category B clean-deprecated reverse-index maintenance.
    /// </summary>
    private const int ReverseIndexMaxRetries = 3;

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

    /// <summary>
    /// Reverse-index key for lifecycle profiles keyed by their template identity.
    /// Used by the Category B clean-deprecated sweep to check O(1) whether any
    /// LifecycleProfile instances still reference a deprecated lifecycle template.
    /// Key pattern: lifecycle-by-template:{speciesCode}:{gameServiceId}
    /// </summary>
    internal static string BuildLifecycleByTemplateKey(string speciesCode, Guid gameServiceId)
        => $"{LIFECYCLE_BY_TEMPLATE_PREFIX}{speciesCode}:{gameServiceId}";

    /// <summary>
    /// Reverse-index key for genetic profiles keyed by their heritable trait template identity.
    /// Used by the Category B clean-deprecated sweep to check O(1) whether any
    /// GeneticProfile instances still reference a deprecated heritable trait template.
    /// Key pattern: genetic-by-trait-template:{speciesCode}:{gameServiceId}
    /// </summary>
    internal static string BuildGeneticByTraitTemplateKey(string speciesCode, Guid gameServiceId)
        => $"{GENETIC_BY_TRAIT_TEMPLATE_PREFIX}{speciesCode}:{gameServiceId}";

    /// <summary>
    /// Reverse-index key for hybrid genetic profiles keyed by their hybrid template identity.
    /// Only populated for GeneticProfile instances whose SecondarySpecies is non-null.
    /// Key pattern: genetic-by-hybrid-template:{speciesA}:{speciesB}:{gameServiceId}
    /// </summary>
    internal static string BuildGeneticByHybridTemplateKey(string speciesA, string speciesB, Guid gameServiceId)
        => $"{GENETIC_BY_HYBRID_TEMPLATE_PREFIX}{speciesA}:{speciesB}:{gameServiceId}";

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
        _profileStringStore = stateStoreFactory.GetStore<string>(StateStoreDefinitions.CharacterLifecycleProfiles);
        _heritageStringStore = stateStoreFactory.GetStore<string>(StateStoreDefinitions.CharacterLifecycleHeritage);
        _cacheStore = stateStoreFactory.GetStore<LifecycleManifestModel>(StateStoreDefinitions.CharacterLifecycleCache);
        _queryableLifecycleTemplateStore = stateStoreFactory.GetQueryableStore<LifecycleTemplateModel>(StateStoreDefinitions.CharacterLifecycleHeritage);
        _queryableTraitTemplateStore = stateStoreFactory.GetQueryableStore<HeritableTraitTemplateModel>(StateStoreDefinitions.CharacterLifecycleHeritage);
        _queryableHybridTemplateStore = stateStoreFactory.GetQueryableStore<HybridTraitTemplateModel>(StateStoreDefinitions.CharacterLifecycleHeritage);
        _queryableBloodlineStore = stateStoreFactory.GetQueryableStore<BloodlineModel>(StateStoreDefinitions.CharacterLifecycleBloodlines);
        _queryableProfileStore = stateStoreFactory.GetQueryableStore<LifecycleProfileModel>(StateStoreDefinitions.CharacterLifecycleProfiles);
        _queryableGeneticStore = stateStoreFactory.GetQueryableStore<GeneticProfileModel>(StateStoreDefinitions.CharacterLifecycleHeritage);

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
    /// Initiates marriage between two characters. Acquires lock, validates eligibility via lifecycle
    /// template stage capabilities, creates marriage contract and spouse relationship, optionally
    /// integrates household and disposition. Updates profiles and publishes marriage event.
    /// Self-healing: orphaned contract expires via ContractExpirationService TTL.
    /// </summary>
    public async Task<(StatusCodes, InitiateMarriageResponse?)> InitiateMarriageAsync(InitiateMarriageRequest body, CancellationToken cancellationToken)
    {
        // Acquire marriage lock (per map: -> 409 if fails)
        var lockKey = $"{body.CharacterAId}:{body.CharacterBId}";
        await using var lockResponse = await _lockProvider.LockAsync(
            StateStoreDefinitions.CharacterLifecycleLock, lockKey,
            "marriage", _configuration.DistributedLockTimeoutSeconds, cancellationToken);
        if (!lockResponse.Success)
            return (StatusCodes.Conflict, null);

        // Read both profiles (per map: -> 404 if null)
        var profileA = await _profileStore.GetAsync(BuildProfileKey(body.CharacterAId), cancellationToken);
        if (profileA == null)
            return (StatusCodes.NotFound, null);

        var profileB = await _profileStore.GetAsync(BuildProfileKey(body.CharacterBId), cancellationToken);
        if (profileB == null)
            return (StatusCodes.NotFound, null);

        // Read lifecycle template to check stage capabilities (per map: canMarry check)
        var template = await _lifecycleTemplateStore.GetAsync(
            BuildLifecycleTemplateKey(profileA.SpeciesCode, profileA.GameServiceId), cancellationToken);
        if (template != null)
        {
            var stageA = template.Stages.FirstOrDefault(s => s.Code == profileA.CurrentStage);
            if (stageA != null && !stageA.CanMarry)
                return (StatusCodes.BadRequest, null);

            var stageB = template.Stages.FirstOrDefault(s => s.Code == profileB.CurrentStage);
            if (stageB != null && !stageB.CanMarry)
                return (StatusCodes.BadRequest, null);
        }

        // Resolve marriage contract template by code
        Guid templateId;
        try
        {
            var templateResponse = await _contractClient.GetContractTemplateAsync(
                new GetContractTemplateRequest { Code = _configuration.MarriageContractTemplateCode },
                cancellationToken);
            templateId = templateResponse.TemplateId;
        }
        catch (ApiException ex)
        {
            _logger.LogWarning(ex, "Marriage contract template lookup failed for code {Code}",
                _configuration.MarriageContractTemplateCode);
            return ((StatusCodes)ex.StatusCode, null);
        }

        // Create marriage contract (self-healing: orphaned contract expires via ContractExpirationService)
        Guid contractId;
        try
        {
            var contractResponse = await _contractClient.CreateContractInstanceAsync(
                new CreateContractInstanceRequest
                {
                    TemplateId = templateId,
                    Parties = new[]
                    {
                        new ContractPartyInput { EntityId = body.CharacterAId, EntityType = EntityType.Character, Role = "spouse" },
                        new ContractPartyInput { EntityId = body.CharacterBId, EntityType = EntityType.Character, Role = "spouse" }
                    }
                }, cancellationToken);
            contractId = contractResponse.ContractId;
        }
        catch (ApiException ex)
        {
            _logger.LogWarning(ex, "Contract creation failed for marriage between {CharA} and {CharB}",
                body.CharacterAId, body.CharacterBId);
            return ((StatusCodes)ex.StatusCode, null);
        }

        // Resolve SPOUSE relationship type by code
        Guid spouseRelTypeId;
        try
        {
            var relTypeResponse = await _relationshipClient.GetRelationshipTypeByCodeAsync(
                new GetRelationshipTypeByCodeRequest { Code = "SPOUSE" }, cancellationToken);
            spouseRelTypeId = relTypeResponse.RelationshipTypeId;
        }
        catch (ApiException ex)
        {
            _logger.LogWarning(ex, "SPOUSE relationship type lookup failed, contract {ContractId} will self-heal via TTL",
                contractId);
            return ((StatusCodes)ex.StatusCode, null);
        }

        // Create spouse relationship (if fails after contract: contract self-heals via TTL expiration)
        try
        {
            await _relationshipClient.CreateRelationshipAsync(
                new CreateRelationshipRequest
                {
                    Entity1Id = body.CharacterAId,
                    Entity1Type = EntityType.Character,
                    Entity2Id = body.CharacterBId,
                    Entity2Type = EntityType.Character,
                    RelationshipTypeId = spouseRelTypeId
                }, cancellationToken);
        }
        catch (ApiException ex)
        {
            _logger.LogWarning(ex, "Relationship creation failed for marriage, contract {ContractId} will self-heal via TTL",
                contractId);
            return ((StatusCodes)ex.StatusCode, null);
        }

        // Soft: household resolution (L4 — degrade if unavailable)
        Guid? householdOrgId = null;
        // Organization and Disposition are L4 soft dependencies — not yet implemented as services.
        // When lib-organization and lib-disposition are available, resolve via:
        //   var orgClient = _serviceProvider.GetService<IOrganizationClient>();
        //   if (orgClient != null) { ... }
        //   var dispClient = _serviceProvider.GetService<IDispositionClient>();
        //   if (dispClient != null) { ... }

        // Update profiles with marriage data
        var now = DateTimeOffset.UtcNow;
        profileA.MarriageContractIds.Add(contractId);
        profileA.SpouseCharacterIds.Add(body.CharacterBId);
        profileA.UpdatedAt = now;
        await _profileStore.SaveAsync(BuildProfileKey(body.CharacterAId), profileA, cancellationToken: cancellationToken);

        profileB.MarriageContractIds.Add(contractId);
        profileB.SpouseCharacterIds.Add(body.CharacterAId);
        profileB.UpdatedAt = now;
        await _profileStore.SaveAsync(BuildProfileKey(body.CharacterBId), profileB, cancellationToken: cancellationToken);

        // Invalidate cache manifests
        await _cacheStore.DeleteAsync(BuildManifestKey(body.CharacterAId), cancellationToken);
        await _cacheStore.DeleteAsync(BuildManifestKey(body.CharacterBId), cancellationToken);

        // Publish marriage event
        await _messageBus.PublishCharacterLifecycleMarriageAsync(
            new CharacterLifecycleMarriageEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = now,
                CharacterAId = body.CharacterAId,
                CharacterBId = body.CharacterBId,
                ContractId = contractId,
                HouseholdOrgId = householdOrgId,
                RealmId = profileA.RealmId
            }, cancellationToken);

        _logger.LogInformation("Marriage initiated between {CharA} and {CharB} with contract {ContractId}",
            body.CharacterAId, body.CharacterBId, contractId);

        return (StatusCodes.OK, new InitiateMarriageResponse
        {
            ContractId = contractId,
            HouseholdOrgId = householdOrgId
        });
    }

    /// <summary>
    /// Initiates procreation between two characters. Validates eligibility, fertility, and child limits.
    /// Creates pending pregnancy record for the pregnancy worker to process at expected birth date.
    /// </summary>
    public async Task<(StatusCodes, InitiateProcreationResponse?)> InitiateProcreationAsync(InitiateProcreationRequest body, CancellationToken cancellationToken)
    {
        // Acquire procreation lock (per map: -> 409 if fails)
        var lockKey = $"{body.ParentAId}:{body.ParentBId}";
        await using var lockResponse = await _lockProvider.LockAsync(
            StateStoreDefinitions.CharacterLifecycleLock, lockKey,
            "procreation", _configuration.DistributedLockTimeoutSeconds, cancellationToken);
        if (!lockResponse.Success)
            return (StatusCodes.Conflict, null);

        // Read both parent profiles (per map: -> 404 if null)
        var profileA = await _profileStore.GetAsync(BuildProfileKey(body.ParentAId), cancellationToken);
        if (profileA == null)
            return (StatusCodes.NotFound, null);

        var profileB = await _profileStore.GetAsync(BuildProfileKey(body.ParentBId), cancellationToken);
        if (profileB == null)
            return (StatusCodes.NotFound, null);

        // Read lifecycle template to check stage capabilities (per map: canProcreate check)
        var template = await _lifecycleTemplateStore.GetAsync(
            BuildLifecycleTemplateKey(profileA.SpeciesCode, profileA.GameServiceId), cancellationToken);
        if (template != null)
        {
            var stageA = template.Stages.FirstOrDefault(s => s.Code == profileA.CurrentStage);
            if (stageA != null && !stageA.CanProcreate)
                return (StatusCodes.BadRequest, null);

            var stageB = template.Stages.FirstOrDefault(s => s.Code == profileB.CurrentStage);
            if (stageB != null && !stageB.CanProcreate)
                return (StatusCodes.BadRequest, null);
        }

        // Fertility check: combined modifier produces probability (per map)
        var combinedFertility = profileA.FertilityModifier * profileB.FertilityModifier;
        if (Random.Shared.NextDouble() > combinedFertility)
            return (StatusCodes.BadRequest, null);

        // Child limit checks (per map)
        if (profileA.ChildCount >= _configuration.MaxChildrenPerPair)
            return (StatusCodes.BadRequest, null);
        if (profileA.TotalChildCount >= _configuration.MaxChildrenPerCharacter)
            return (StatusCodes.BadRequest, null);

        // Get current game time for expected birth date calculation
        int expectedBirthGameDay;
        try
        {
            var timeSnapshot = await _worldstateClient.GetRealmTimeAsync(
                new GetRealmTimeRequest { RealmId = profileA.RealmId }, cancellationToken);
            expectedBirthGameDay = timeSnapshot.DayOfYear + _configuration.PregnancyDurationGameDays;
        }
        catch (ApiException ex)
        {
            _logger.LogWarning(ex, "Worldstate time query failed for realm {RealmId}", profileA.RealmId);
            return ((StatusCodes)ex.StatusCode, null);
        }

        // Create pending pregnancy record (per map: WRITE profileStore:pregnancy-pending:{pregnancyId})
        var pregnancyId = Guid.NewGuid();
        var pregnancy = new PendingPregnancyModel
        {
            PregnancyId = pregnancyId,
            ParentAId = body.ParentAId,
            ParentBId = body.ParentBId,
            GameServiceId = body.GameServiceId,
            RealmId = profileA.RealmId,
            SpeciesCode = profileA.SpeciesCode,
            ExpectedBirthGameDay = expectedBirthGameDay,
            CreatedAt = DateTimeOffset.UtcNow
        };

        await _pregnancyStore.SaveAsync($"{PREGNANCY_KEY_PREFIX}{pregnancyId}", pregnancy,
            cancellationToken: cancellationToken);

        _logger.LogInformation("Procreation initiated between {ParentA} and {ParentB}, pregnancy {PregnancyId} expected day {BirthDay}",
            body.ParentAId, body.ParentBId, pregnancyId, expectedBirthGameDay);

        return (StatusCodes.OK, new InitiateProcreationResponse
        {
            PregnancyId = pregnancyId,
            ExpectedBirthDate = expectedBirthGameDay
        });
    }

    /// <summary>
    /// Records a character death. Acquires lock, checks idempotency (already dead -> 200),
    /// calculates fulfillment, contributes to guardian spirit, triggers archive compression,
    /// processes inheritance, computes afterlife path, updates profile, and publishes events.
    /// </summary>
    public async Task<(StatusCodes, RecordDeathResponse?)> RecordDeathAsync(RecordDeathRequest body, CancellationToken cancellationToken)
    {
        // Acquire death lock (per map: -> 409 if fails)
        var lockKey = body.CharacterId.ToString();
        await using var lockResponse = await _lockProvider.LockAsync(
            StateStoreDefinitions.CharacterLifecycleLock, lockKey,
            "death", _configuration.DistributedLockTimeoutSeconds, cancellationToken);
        if (!lockResponse.Success)
            return (StatusCodes.Conflict, null);

        // Read profile with ETag (per map: -> 404 if null)
        var (profile, etag) = await _profileStore.GetWithETagAsync(
            BuildProfileKey(body.CharacterId), cancellationToken);
        if (profile == null || etag == null)
            return (StatusCodes.NotFound, null);

        // Idempotent check (per map: Dead -> 200)
        if (profile.Status == LifecycleStatus.Dead)
        {
            return (StatusCodes.OK, new RecordDeathResponse
            {
                FulfillmentScore = profile.FulfillmentScore ?? _configuration.FulfillmentNeutralDefault,
                // Compiler satisfaction: AfterlifePath is always set when status transitions to Dead (step 6 below),
                // so this coalesce can never execute on a properly-stored Dead profile
                AfterlifePath = profile.AfterlifePath ?? "peace"
            });
        }

        // Step 2: Fulfillment calculation (soft: DispositionClient)
        var fulfillment = _configuration.FulfillmentNeutralDefault;
        // Disposition service integration deferred — uses neutral default when unavailable

        // Step 3: Guardian spirit contribution
        // Resolution chain: characterId → household Org → account owner → guardian seed
        var guardianContribution = fulfillment * _configuration.GuardianSpiritBaseMultiplier;
        if (profile.HouseholdOrgId != null)
        {
            // Full chain requires IOrganizationClient (L4, not yet available) to resolve org → account owner.
            // When available: orgClient.GetOrganization(householdOrgId) → ownerAccountId
            //   → seedClient.GetSeedsByOwner(accountId, Account, "guardian") → seedId
            //   → seedClient.RecordGrowth(seedId, "logos", guardianContribution)
            // Degrades: no guardian spirit contribution when Organization service unavailable
            try
            {
                var seedsResponse = await _seedClient.GetSeedsByOwnerAsync(
                    new GetSeedsByOwnerRequest
                    {
                        OwnerId = body.CharacterId,
                        OwnerType = EntityType.Character,
                        SeedTypeCode = "guardian"
                    }, cancellationToken);

                var guardianSeed = seedsResponse.Seeds.FirstOrDefault();
                if (guardianSeed != null)
                {
                    await _seedClient.RecordGrowthAsync(
                        new RecordGrowthRequest
                        {
                            SeedId = guardianSeed.SeedId,
                            Domain = "logos",
                            Amount = guardianContribution
                        }, cancellationToken);
                }
            }
            catch (ApiException ex)
            {
                _logger.LogWarning(ex, "Guardian spirit growth recording failed for character {CharacterId}", body.CharacterId);
                await _messageBus.TryPublishErrorAsync(
                    "character-lifecycle", "DeathProcessing.GuardianGrowth",
                    ex.GetType().Name, ex.Message, stack: ex.StackTrace);
            }
        }

        // Step 4: Archive compression (idempotent per map)
        try
        {
            await _resourceClient.ExecuteCleanupAsync(
                new ExecuteCleanupRequest { ResourceId = body.CharacterId, ResourceType = "character" },
                cancellationToken);
        }
        catch (ApiException ex)
        {
            _logger.LogWarning(ex, "Archive compression failed for character {CharacterId}", body.CharacterId);
            await _messageBus.TryPublishErrorAsync(
                "character-lifecycle", "DeathProcessing.ArchiveCompression",
                ex.GetType().Name, ex.Message, stack: ex.StackTrace);
        }

        // Step 5: Inheritance (each step individually safe to re-execute per map)
        Guid? heirId = null;
        var assetsTransferred = 0;

        // Testament contract execution (per-item error isolation per IMPLEMENTATION TENETS)
        try
        {
            var queryResponse = await _contractClient.QueryContractInstancesAsync(
                new QueryContractInstancesRequest
                {
                    PartyEntityId = body.CharacterId,
                    PartyEntityType = EntityType.Character
                }, cancellationToken);

            foreach (var contract in queryResponse.Contracts)
            {
                try
                {
                    await _contractClient.TerminateContractInstanceAsync(
                        new TerminateContractInstanceRequest
                        {
                            ContractId = contract.ContractId,
                            RequestingEntityId = body.CharacterId,
                            RequestingEntityType = EntityType.Character,
                            Reason = $"Death of party {body.CharacterId}"
                        }, cancellationToken);
                }
                catch (ApiException ex)
                {
                    _logger.LogWarning(ex, "Contract termination failed for {ContractId}", contract.ContractId);
                    await _messageBus.TryPublishErrorAsync(
                        "character-lifecycle", "DeathProcessing.ContractTermination",
                        ex.GetType().Name, ex.Message, stack: ex.StackTrace);
                }
            }
        }
        catch (ApiException ex)
        {
            _logger.LogWarning(ex, "Contract query failed during death processing for {CharacterId}", body.CharacterId);
            await _messageBus.TryPublishErrorAsync(
                "character-lifecycle", "DeathProcessing.ContractQuery",
                ex.GetType().Name, ex.Message, stack: ex.StackTrace);
        }

        // Step 6: Afterlife pathway (pure computation)
        var afterlifePath = ComputeAfterlifePath(fulfillment, body.DeathCause);

        // Step 7: Update profile
        profile.Status = LifecycleStatus.Dead;
        profile.DeathCause = body.DeathCause;
        profile.FulfillmentScore = fulfillment;
        profile.AfterlifePath = afterlifePath;
        profile.UpdatedAt = DateTimeOffset.UtcNow;

        var savedEtag = await _profileStore.TrySaveAsync(BuildProfileKey(body.CharacterId), profile, etag,
            cancellationToken: cancellationToken);
        if (savedEtag == null)
            return (StatusCodes.Conflict, null);

        await _cacheStore.DeleteAsync(BuildManifestKey(body.CharacterId), cancellationToken);

        // Step 8: Publish events
        var now = DateTimeOffset.UtcNow;
        await _messageBus.PublishCharacterLifecycleDeathAsync(
            new CharacterLifecycleDeathEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = now,
                CharacterId = body.CharacterId,
                DeathCause = body.DeathCause,
                FulfillmentScore = fulfillment,
                AfterlifePath = afterlifePath,
                RealmId = profile.RealmId,
                GuardianSpiritContribution = guardianContribution
            }, cancellationToken);

        await _messageBus.PublishCharacterLifecycleInheritanceProcessedAsync(
            new CharacterLifecycleInheritanceProcessedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = now,
                DeceasedId = body.CharacterId,
                HeirId = heirId,
                AssetsTransferred = assetsTransferred
            }, cancellationToken);

        _logger.LogInformation("Death recorded for character {CharacterId}, cause: {DeathCause}, afterlife: {Afterlife}",
            body.CharacterId, body.DeathCause, afterlifePath);

        return (StatusCodes.OK, new RecordDeathResponse
        {
            FulfillmentScore = fulfillment,
            AfterlifePath = afterlifePath
        });
    }

    /// <summary>
    /// Pure computation: determines afterlife pathway based on fulfillment score and death cause.
    /// </summary>
    private static string ComputeAfterlifePath(float fulfillment, string deathCause)
    {
        if (fulfillment >= 0.8f) return "transcendence";
        if (fulfillment >= 0.5f) return "peace";
        if (deathCause == "heroic" || deathCause == "sacrifice") return "glory";
        if (fulfillment >= 0.2f) return "wandering";
        return "shadow";
    }

    /// <summary>
    /// Returns lifecycle profile for a character.
    /// </summary>
    public async Task<(StatusCodes, GetLifecycleProfileResponse?)> GetLifecycleProfileAsync(GetLifecycleProfileRequest body, CancellationToken cancellationToken)
    {
        var profile = await _profileStore.GetAsync(BuildProfileKey(body.CharacterId), cancellationToken);
        if (profile == null)
            return (StatusCodes.NotFound, null);

        return (StatusCodes.OK, new GetLifecycleProfileResponse
        {
            Profile = MapProfileToSummary(profile)
        });
    }

    /// <summary>
    /// Queries lifecycle profiles by realm and stage code, with optional archived filter and paging.
    /// </summary>
    public async Task<(StatusCodes, QueryByStageResponse?)> QueryByStageAsync(QueryByStageRequest body, CancellationToken cancellationToken)
    {
        var results = body.IncludeArchived
            ? await _queryableProfileStore.QueryAsync(
                p => p.RealmId == body.RealmId && p.CurrentStage == body.StageCode, cancellationToken)
            : await _queryableProfileStore.QueryAsync(
                p => p.RealmId == body.RealmId && p.CurrentStage == body.StageCode && p.Status != LifecycleStatus.Archived, cancellationToken);

        var allResults = results.ToList();
        var page = body.Page > 0 ? body.Page : 1;
        var pageSize = body.PageSize > 0 ? body.PageSize : _configuration.QueryPageSize;
        var paged = allResults.Skip((page - 1) * pageSize).Take(pageSize);

        return (StatusCodes.OK, new QueryByStageResponse
        {
            Profiles = paged.Select(MapProfileToSummary).ToList(),
            TotalCount = allResults.Count
        });
    }

    /// <summary>
    /// Queries lifecycle profiles for living members of a bloodline.
    /// </summary>
    public async Task<(StatusCodes, QueryByBloodlineResponse?)> QueryByBloodlineAsync(QueryByBloodlineRequest body, CancellationToken cancellationToken)
    {
        var memberList = await _memberListStore.GetAsync(
            BuildBloodlineMembersKey(body.BloodlineId), cancellationToken);
        if (memberList == null)
            return (StatusCodes.NotFound, null);

        var aliveProfiles = new List<LifecycleProfileSummary>();
        foreach (var memberId in memberList.MemberIds)
        {
            try
            {
                var profile = await _profileStore.GetAsync(BuildProfileKey(memberId), cancellationToken);
                if (profile?.Status == LifecycleStatus.Alive)
                    aliveProfiles.Add(MapProfileToSummary(profile));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load profile for member {MemberId} in bloodline query", memberId);
            }
        }

        var page = body.Page > 0 ? body.Page : 1;
        var pageSize = body.PageSize > 0 ? body.PageSize : _configuration.QueryPageSize;
        var paged = aliveProfiles.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        return (StatusCodes.OK, new QueryByBloodlineResponse
        {
            Profiles = paged,
            TotalCount = aliveProfiles.Count
        });
    }

    /// <summary>
    /// Sets the natural death year for a character. Uses ETag for optimistic concurrency.
    /// No event published (silent adjustment per map).
    /// </summary>
    public async Task<(StatusCodes, SetNaturalDeathYearResponse?)> SetNaturalDeathYearAsync(SetNaturalDeathYearRequest body, CancellationToken cancellationToken)
    {
        var key = BuildProfileKey(body.CharacterId);
        var (profile, etag) = await _profileStore.GetWithETagAsync(key, cancellationToken);
        if (profile == null || etag == null)
            return (StatusCodes.NotFound, null);

        profile.NaturalDeathYear = body.NaturalDeathYear;
        profile.UpdatedAt = DateTimeOffset.UtcNow;

        var newEtag = await _profileStore.TrySaveAsync(key, profile, etag, cancellationToken: cancellationToken);
        if (newEtag == null)
            return (StatusCodes.Conflict, null);

        await _cacheStore.DeleteAsync(BuildManifestKey(body.CharacterId), cancellationToken);

        _logger.LogInformation("Set natural death year to {DeathYear} for character {CharacterId}",
            body.NaturalDeathYear, body.CharacterId);

        return (StatusCodes.OK, new SetNaturalDeathYearResponse());
    }

    /// <summary>
    /// Seeds lifecycle profiles for multiple characters. Uses per-item error isolation.
    /// Skips characters that already have profiles. Optionally writes genetic data if heritageData provided.
    /// </summary>
    public async Task<(StatusCodes, SeedLifecycleProfileResponse?)> SeedLifecycleProfileAsync(SeedLifecycleProfileRequest body, CancellationToken cancellationToken)
    {
        var createdCount = 0;
        var errors = new List<SeedItemError>();

        foreach (var entry in body.Characters)
        {
            try
            {
                // Skip if profile already exists (per map: READ → skip if exists)
                var profileKey = BuildProfileKey(entry.CharacterId);
                var existing = await _profileStore.GetAsync(profileKey, cancellationToken);
                if (existing != null)
                    continue;

                // Read lifecycle template for naturalDeathYear computation (per map)
                var templateKey = BuildLifecycleTemplateKey(entry.SpeciesCode, entry.GameServiceId);
                var template = await _lifecycleTemplateStore.GetAsync(templateKey, cancellationToken);

                // Category B instance creation guard (per IMPLEMENTATION TENETS):
                // reject entries referencing deprecated lifecycle templates. Per-item
                // error isolation — record the failure and continue with other entries.
                if (template != null && template.IsDeprecated)
                {
                    _logger.LogWarning(
                        "Skipping lifecycle profile seed for character {CharacterId}: " +
                        "lifecycle template for species {SpeciesCode} in game {GameServiceId} is deprecated",
                        entry.CharacterId, entry.SpeciesCode, entry.GameServiceId);
                    errors.Add(new SeedItemError
                    {
                        CharacterId = entry.CharacterId,
                        Error = $"Lifecycle template for species '{entry.SpeciesCode}' is deprecated"
                    });
                    continue;
                }

                // If heritage data is provided, also guard against a deprecated trait template
                // (the heritage template that will be used to back this profile's genetics).
                HeritableTraitTemplateModel? traitTemplate = null;
                if (entry.HeritageData != null && entry.HeritageData.Count > 0)
                {
                    traitTemplate = await _traitTemplateStore.GetAsync(
                        BuildTraitTemplateKey(entry.SpeciesCode, entry.GameServiceId), cancellationToken);
                    if (traitTemplate != null && traitTemplate.IsDeprecated)
                    {
                        _logger.LogWarning(
                            "Skipping lifecycle profile seed for character {CharacterId}: " +
                            "heritable trait template for species {SpeciesCode} in game {GameServiceId} is deprecated",
                            entry.CharacterId, entry.SpeciesCode, entry.GameServiceId);
                        errors.Add(new SeedItemError
                        {
                            CharacterId = entry.CharacterId,
                            Error = $"Heritable trait template for species '{entry.SpeciesCode}' is deprecated"
                        });
                        continue;
                    }
                }

                // Compute naturalDeathYear from template + random
                int? naturalDeathYear = null;
                if (template != null)
                {
                    var range = template.NaturalDeathRange;
                    naturalDeathYear = entry.BirthGameYear + range.MinAge
                        + Random.Shared.Next(range.MaxAge - range.MinAge + 1);
                }

                var now = DateTimeOffset.UtcNow;
                var profile = new LifecycleProfileModel
                {
                    CharacterId = entry.CharacterId,
                    GameServiceId = entry.GameServiceId,
                    RealmId = entry.RealmId,
                    SpeciesCode = entry.SpeciesCode,
                    BirthGameYear = entry.BirthGameYear,
                    BirthSeason = entry.BirthSeason,
                    CurrentAge = entry.CurrentAge,
                    CurrentStage = entry.CurrentStage,
                    CauseOfCreation = CreationCause.Seeded,
                    NaturalDeathYear = naturalDeathYear,
                    FertilityModifier = 1.0f,
                    HealthModifier = 1.0f,
                    Status = LifecycleStatus.Alive,
                    CreatedAt = now
                };

                await _profileStore.SaveAsync(profileKey, profile, cancellationToken: cancellationToken);

                // Maintain reverse index for Category B clean-deprecated sweep
                // (lifecycle-template → lifecycle-profile list)
                await _profileStringStore.AddToStringListAsync(
                    BuildLifecycleByTemplateKey(entry.SpeciesCode, entry.GameServiceId),
                    entry.CharacterId.ToString(),
                    ReverseIndexMaxRetries,
                    _logger,
                    cancellationToken);

                // Write minimal genetic profile if heritage data provided (per map)
                if (entry.HeritageData != null && entry.HeritageData.Count > 0)
                {
                    var geneticKey = BuildGeneticKey(entry.CharacterId);
                    var geneticProfile = new GeneticProfileModel
                    {
                        CharacterId = entry.CharacterId,
                        SpeciesCode = entry.SpeciesCode,
                        GenerationDepth = 0,
                        Genotype = entry.HeritageData.Select(p => new GenotypeEntry
                        {
                            TraitCode = p.TraitCode,
                            AlleleA = p.Value,
                            AlleleB = p.Value,
                            Dominance = p.ExpressionRule
                        }).ToList(),
                        Phenotype = entry.HeritageData.ToList(),
                        Aptitudes = new List<AptitudeEntry>(),
                        Bloodlines = new List<BloodlineEntry>(),
                        Mutations = new List<MutationEntry>(),
                        CreatedAt = now
                    };
                    await _geneticStore.SaveAsync(geneticKey, geneticProfile, cancellationToken: cancellationToken);

                    // Maintain reverse index for Category B clean-deprecated sweep
                    // (heritable-trait-template → genetic-profile list). Seeded genetic
                    // profiles are never hybrids, so only the trait-template index is
                    // written here; the hybrid index is populated by procreation paths.
                    await _heritageStringStore.AddToStringListAsync(
                        BuildGeneticByTraitTemplateKey(entry.SpeciesCode, entry.GameServiceId),
                        entry.CharacterId.ToString(),
                        ReverseIndexMaxRetries,
                        _logger,
                        cancellationToken);
                }

                createdCount++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to seed lifecycle profile for character {CharacterId}", entry.CharacterId);
                errors.Add(new SeedItemError
                {
                    CharacterId = entry.CharacterId,
                    Error = ex.Message
                });
            }
        }

        return (StatusCodes.OK, new SeedLifecycleProfileResponse
        {
            CreatedCount = createdCount,
            Errors = errors.Count > 0 ? errors : null
        });
    }

    /// <summary>
    /// Returns full genetic profile for a character including genotype, phenotype, aptitudes, and bloodlines.
    /// </summary>
    public async Task<(StatusCodes, GetGeneticProfileResponse?)> GetGeneticProfileAsync(GetGeneticProfileRequest body, CancellationToken cancellationToken)
    {
        var genetic = await _geneticStore.GetAsync(BuildGeneticKey(body.CharacterId), cancellationToken);
        if (genetic == null)
            return (StatusCodes.NotFound, null);

        return (StatusCodes.OK, new GetGeneticProfileResponse
        {
            CharacterId = genetic.CharacterId,
            SpeciesCode = genetic.SpeciesCode,
            SecondarySpecies = genetic.SecondarySpecies,
            ParentAId = genetic.ParentAId,
            ParentBId = genetic.ParentBId,
            GenerationDepth = genetic.GenerationDepth,
            Genotype = genetic.Genotype.ToList(),
            Phenotype = genetic.Phenotype.ToList(),
            Aptitudes = genetic.Aptitudes.ToList(),
            Bloodlines = genetic.Bloodlines.ToList(),
            Mutations = genetic.Mutations.ToList()
        });
    }

    /// <summary>
    /// Returns phenotype and aptitudes subset for a character (no genotype details).
    /// </summary>
    public async Task<(StatusCodes, GetPhenotypeResponse?)> GetPhenotypeAsync(GetPhenotypeRequest body, CancellationToken cancellationToken)
    {
        var genetic = await _geneticStore.GetAsync(BuildGeneticKey(body.CharacterId), cancellationToken);
        if (genetic == null)
            return (StatusCodes.NotFound, null);

        return (StatusCodes.OK, new GetPhenotypeResponse
        {
            CharacterId = genetic.CharacterId,
            Phenotype = genetic.Phenotype.ToList(),
            Aptitudes = genetic.Aptitudes.ToList()
        });
    }

    /// <summary>
    /// Queries genetic profiles by aptitude domain and threshold.
    /// Uses heritage store query per map specification.
    /// </summary>
    public async Task<(StatusCodes, QueryByAptitudeResponse?)> QueryByAptitudeAsync(QueryByAptitudeRequest body, CancellationToken cancellationToken)
    {
        // Query heritage store directly for genetic profiles with matching aptitudes (per map)
        var geneticProfiles = await _queryableGeneticStore.QueryAsync(
            g => g.Aptitudes.Any(a => a.Domain == body.Domain && a.Value > body.Threshold),
            cancellationToken);

        var matchingResults = geneticProfiles.Select(g =>
        {
            var aptitude = g.Aptitudes.First(a => a.Domain == body.Domain && a.Value > body.Threshold);
            return new AptitudeQueryResult
            {
                CharacterId = g.CharacterId,
                AptitudeValue = aptitude.Value
            };
        }).ToList();

        var page = body.Page > 0 ? body.Page : 1;
        var pageSize = body.PageSize > 0 ? body.PageSize : _configuration.QueryPageSize;
        var paged = matchingResults.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        return (StatusCodes.OK, new QueryByAptitudeResponse
        {
            Results = paged,
            TotalCount = matchingResults.Count
        });
    }

    /// <summary>
    /// Seeds a genetic profile for a character. Immutable after creation (409 if exists).
    /// First-gen characters: genotype = phenotype (both alleles equal expressed value).
    /// </summary>
    public async Task<(StatusCodes, SeedGeneticProfileResponse?)> SeedGeneticProfileAsync(SeedGeneticProfileRequest body, CancellationToken cancellationToken)
    {
        var geneticKey = BuildGeneticKey(body.CharacterId);
        var existing = await _geneticStore.GetAsync(geneticKey, cancellationToken);
        if (existing != null)
            return (StatusCodes.Conflict, null);

        // Read trait template for species-default phenotype generation
        var traitTemplate = await _traitTemplateStore.GetAsync(
            BuildTraitTemplateKey(body.SpeciesCode, body.GameServiceId), cancellationToken);
        if (traitTemplate == null)
            return (StatusCodes.NotFound, null);

        // Category B instance creation guard (per IMPLEMENTATION TENETS):
        // reject genetic profile creation if the heritable trait template is deprecated.
        if (traitTemplate.IsDeprecated)
        {
            _logger.LogWarning(
                "Cannot create genetic profile for character {CharacterId}: " +
                "heritable trait template for species {SpeciesCode} in game {GameServiceId} is deprecated",
                body.CharacterId, body.SpeciesCode, body.GameServiceId);
            return (StatusCodes.BadRequest, null);
        }

        // Generate phenotype from provided values or species-default + random variation
        var phenotype = body.PhenotypeValues != null && body.PhenotypeValues.Count > 0
            ? body.PhenotypeValues.ToList()
            : traitTemplate.Traits.Select(t => new PhenotypeEntry
            {
                TraitCode = t.TraitCode,
                Value = 0.5f + (float)(Random.Shared.NextDouble() - 0.5) * 0.4f,
                ExpressionRule = t.DominanceModel
            }).ToList();

        // First-gen: genotype = phenotype (both alleles equal expressed value)
        var genotype = phenotype.Select(p => new GenotypeEntry
        {
            TraitCode = p.TraitCode,
            AlleleA = p.Value,
            AlleleB = p.Value,
            Dominance = p.ExpressionRule
        }).ToList();

        // Compute aptitudes from phenotype using trait template mappings
        var aptitudes = traitTemplate.Traits
            .Where(t => t.AptitudeMapping != null)
            .Select(t =>
            {
                var phenoValue = phenotype.FirstOrDefault(p => p.TraitCode == t.TraitCode);
                return new AptitudeEntry
                {
                    Domain = t.AptitudeMapping ?? t.TraitCode,
                    Value = phenoValue?.Value ?? 0.5f
                };
            }).ToList();

        var geneticProfile = new GeneticProfileModel
        {
            CharacterId = body.CharacterId,
            SpeciesCode = body.SpeciesCode,
            GenerationDepth = 0,
            Genotype = genotype,
            Phenotype = phenotype,
            Aptitudes = aptitudes,
            Bloodlines = new List<BloodlineEntry>(),
            Mutations = new List<MutationEntry>(),
            CreatedAt = DateTimeOffset.UtcNow
        };

        await _geneticStore.SaveAsync(geneticKey, geneticProfile, cancellationToken: cancellationToken);
        await _cacheStore.DeleteAsync(BuildManifestKey(body.CharacterId), cancellationToken);

        // Maintain reverse index for Category B clean-deprecated sweep
        // (heritable-trait-template → genetic-profile list). First-gen seeded profiles
        // are never hybrids, so only the trait-template index is written here; the
        // hybrid index is populated by procreation paths.
        await _heritageStringStore.AddToStringListAsync(
            BuildGeneticByTraitTemplateKey(body.SpeciesCode, body.GameServiceId),
            body.CharacterId.ToString(),
            ReverseIndexMaxRetries,
            _logger,
            cancellationToken);

        _logger.LogInformation("Seeded genetic profile for character {CharacterId} species {SpeciesCode}",
            body.CharacterId, body.SpeciesCode);

        return (StatusCodes.OK, new SeedGeneticProfileResponse());
    }

    /// <summary>
    /// Simulates potential offspring trait ranges from two parents.
    /// Pure computation, no writes.
    /// </summary>
    public async Task<(StatusCodes, SimulateOffspringResponse?)> SimulateOffspringAsync(SimulateOffspringRequest body, CancellationToken cancellationToken)
    {
        var parentA = await _geneticStore.GetAsync(BuildGeneticKey(body.ParentAId), cancellationToken);
        if (parentA == null)
            return (StatusCodes.NotFound, null);

        var parentB = await _geneticStore.GetAsync(BuildGeneticKey(body.ParentBId), cancellationToken);
        if (parentB == null)
            return (StatusCodes.NotFound, null);

        // Run recombination simulation: compute trait ranges from parent alleles
        var traitRanges = new List<TraitRange>();
        var allTraitCodes = parentA.Genotype.Select(g => g.TraitCode)
            .Union(parentB.Genotype.Select(g => g.TraitCode))
            .Distinct();

        foreach (var traitCode in allTraitCodes)
        {
            var genoA = parentA.Genotype.FirstOrDefault(g => g.TraitCode == traitCode);
            var genoB = parentB.Genotype.FirstOrDefault(g => g.TraitCode == traitCode);

            // All possible allele combinations
            var alleleValues = new List<float>();
            if (genoA != null && genoB != null)
            {
                alleleValues.Add(genoA.AlleleA);
                alleleValues.Add(genoA.AlleleB);
                alleleValues.Add(genoB.AlleleA);
                alleleValues.Add(genoB.AlleleB);
            }
            else if (genoA != null)
            {
                alleleValues.Add(genoA.AlleleA);
                alleleValues.Add(genoA.AlleleB);
            }
            else if (genoB != null)
            {
                alleleValues.Add(genoB.AlleleA);
                alleleValues.Add(genoB.AlleleB);
            }

            if (alleleValues.Count > 0)
            {
                traitRanges.Add(new TraitRange
                {
                    TraitCode = traitCode,
                    MinValue = alleleValues.Min(),
                    MaxValue = alleleValues.Max(),
                    MeanValue = alleleValues.Average()
                });
            }
        }

        // Compute aptitude ranges from trait ranges
        var aptitudeRanges = new List<TraitRange>();
        var allDomains = parentA.Aptitudes.Select(a => a.Domain)
            .Union(parentB.Aptitudes.Select(a => a.Domain))
            .Distinct();

        foreach (var domain in allDomains)
        {
            var aptA = parentA.Aptitudes.FirstOrDefault(a => a.Domain == domain);
            var aptB = parentB.Aptitudes.FirstOrDefault(a => a.Domain == domain);
            var values = new List<float>();
            if (aptA != null) values.Add(aptA.Value);
            if (aptB != null) values.Add(aptB.Value);

            aptitudeRanges.Add(new TraitRange
            {
                TraitCode = domain,
                MinValue = values.Min(),
                MaxValue = values.Max(),
                MeanValue = values.Average()
            });
        }

        // Bloodline composition: union of both parents' bloodline codes
        var bloodlineCodes = parentA.Bloodlines.Select(b => b.BloodlineCode)
            .Union(parentB.Bloodlines.Select(b => b.BloodlineCode))
            .Distinct().ToList();

        return (StatusCodes.OK, new SimulateOffspringResponse
        {
            TraitRanges = traitRanges,
            AptitudeRanges = aptitudeRanges,
            BloodlineComposition = bloodlineCodes.Count > 0 ? bloodlineCodes : null
        });
    }

    /// <summary>
    /// Builds a family tree for a character, traversing ancestors and descendants.
    /// </summary>
    public async Task<(StatusCodes, GetFamilyTreeResponse?)> GetFamilyTreeAsync(GetFamilyTreeRequest body, CancellationToken cancellationToken)
    {
        var rootProfile = await _profileStore.GetAsync(BuildProfileKey(body.CharacterId), cancellationToken);
        if (rootProfile == null)
            return (StatusCodes.NotFound, null);

        var nodes = new List<FamilyTreeNode>();
        var visited = new HashSet<Guid>();

        // Add root node at generation 0
        var rootGenetic = await _geneticStore.GetAsync(BuildGeneticKey(body.CharacterId), cancellationToken);
        nodes.Add(new FamilyTreeNode
        {
            CharacterId = body.CharacterId,
            SpeciesCode = rootProfile.SpeciesCode,
            Generation = 0,
            Relationship = "self",
            PhenotypeSummary = rootGenetic?.Phenotype.ToList(),
            BloodlineCodes = rootGenetic?.Bloodlines.Select(b => b.BloodlineCode).ToList()
        });
        visited.Add(body.CharacterId);

        // Traverse ancestors (negative generations)
        await TraverseAncestorsAsync(body.CharacterId, 1, body.AncestorDepth, nodes, visited, cancellationToken);

        // Traverse descendants (positive generations)
        await TraverseDescendantsAsync(body.CharacterId, 1, body.DescendantDepth, nodes, visited, cancellationToken);

        return (StatusCodes.OK, new GetFamilyTreeResponse
        {
            RootCharacterId = body.CharacterId,
            Nodes = nodes
        });
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
            SpeciesCode = t.SpeciesCode,
            GameServiceId = t.GameServiceId,
            Stages = t.Stages,
            NaturalDeathRange = t.NaturalDeathRange,
            FertilityWindow = t.FertilityWindow,
            IsDeprecated = t.IsDeprecated,
            DeprecatedAt = t.DeprecatedAt,
            DeprecationReason = t.DeprecationReason
        }).ToList();

        var traitResults = body.IncludeDeprecated
            ? await _queryableTraitTemplateStore.QueryAsync(t => t.GameServiceId == body.GameServiceId, cancellationToken)
            : await _queryableTraitTemplateStore.QueryAsync(t => t.GameServiceId == body.GameServiceId && !t.IsDeprecated, cancellationToken);

        var traitTemplates = traitResults.Select(t => new GetHeritableTraitTemplateResponse
        {
            SpeciesCode = t.SpeciesCode,
            GameServiceId = t.GameServiceId,
            Traits = t.Traits,
            IsDeprecated = t.IsDeprecated,
            DeprecatedAt = t.DeprecatedAt,
            DeprecationReason = t.DeprecationReason
        }).ToList();

        var hybridResults = body.IncludeDeprecated
            ? await _queryableHybridTemplateStore.QueryAsync(t => t.GameServiceId == body.GameServiceId, cancellationToken)
            : await _queryableHybridTemplateStore.QueryAsync(t => t.GameServiceId == body.GameServiceId && !t.IsDeprecated, cancellationToken);

        var hybridTemplates = hybridResults.Select(t => new GetHybridTraitTemplateResponse
        {
            SpeciesA = t.SpeciesA,
            SpeciesB = t.SpeciesB,
            GameServiceId = t.GameServiceId,
            TraitOverrides = t.TraitOverrides,
            HybridFertilityModifier = t.HybridFertilityModifier,
            IsDeprecated = t.IsDeprecated,
            DeprecatedAt = t.DeprecatedAt,
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

        // Query alive profiles for members (per-item error isolation per IMPLEMENTATION TENETS)
        var aliveMembers = new List<BloodlineMemberSummary>();
        foreach (var memberId in memberList.MemberIds)
        {
            try
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
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load member {MemberId} in bloodline members query", memberId);
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

    private static LifecycleProfileSummary MapProfileToSummary(LifecycleProfileModel model) => new()
    {
        CharacterId = model.CharacterId,
        GameServiceId = model.GameServiceId,
        RealmId = model.RealmId,
        SpeciesCode = model.SpeciesCode,
        BirthGameYear = model.BirthGameYear,
        BirthSeason = model.BirthSeason,
        CurrentAge = model.CurrentAge,
        CurrentStage = model.CurrentStage,
        CauseOfCreation = model.CauseOfCreation,
        ParentAId = model.ParentAId,
        ParentBId = model.ParentBId,
        HouseholdOrgId = model.HouseholdOrgId,
        MarriageContractIds = model.MarriageContractIds,
        SpouseCharacterIds = model.SpouseCharacterIds,
        ChildCount = model.ChildCount,
        TotalChildCount = model.TotalChildCount,
        FertilityModifier = model.FertilityModifier,
        HealthModifier = model.HealthModifier,
        NaturalDeathYear = model.NaturalDeathYear,
        FulfillmentScore = model.FulfillmentScore,
        DeathGameYear = model.DeathGameYear,
        DeathCause = model.DeathCause,
        AfterlifePath = model.AfterlifePath,
        Status = model.Status
    };

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
    /// Cleans up all lifecycle data for a character. Removes profile, genetic, membership,
    /// and decrements parent child counts. Does NOT cascade-delete children.
    /// </summary>
    public async Task<(StatusCodes, CleanupByCharacterResponse?)> CleanupByCharacterAsync(CleanupByCharacterRequest body, CancellationToken cancellationToken)
    {
        // Read profile and genetic for template identity before deletion (needed for reverse-index removal)
        var profile = await _profileStore.GetAsync(BuildProfileKey(body.CharacterId), cancellationToken);
        var genetic = await _geneticStore.GetAsync(BuildGeneticKey(body.CharacterId), cancellationToken);

        // Remove from reverse indexes BEFORE deleting the source records. This order
        // is safe under Category B — a stale index entry is handled by the
        // clean-deprecated sweep (which rechecks existence), so pointing at a
        // deleted record degrades to "has instances" false positives rather than
        // data corruption. The reverse order (delete record, then index) would be
        // identical in effect given HasStringListEntriesAsync's semantics.
        if (profile != null)
        {
            await _profileStringStore.RemoveFromStringListAsync(
                BuildLifecycleByTemplateKey(profile.SpeciesCode, profile.GameServiceId),
                body.CharacterId.ToString(),
                ReverseIndexMaxRetries,
                _logger,
                cancellationToken);
        }
        if (genetic != null)
        {
            // Non-hybrid genetic profiles live in the trait-template reverse index.
            await _heritageStringStore.RemoveFromStringListAsync(
                BuildGeneticByTraitTemplateKey(genetic.SpeciesCode, profile?.GameServiceId ?? default),
                body.CharacterId.ToString(),
                ReverseIndexMaxRetries,
                _logger,
                cancellationToken);

            // Hybrid genetic profiles additionally live in the hybrid-template reverse index.
            if (genetic.SecondarySpecies != null && profile != null)
            {
                await _heritageStringStore.RemoveFromStringListAsync(
                    BuildGeneticByHybridTemplateKey(
                        genetic.SpeciesCode, genetic.SecondarySpecies, profile.GameServiceId),
                    body.CharacterId.ToString(),
                    ReverseIndexMaxRetries,
                    _logger,
                    cancellationToken);
            }
        }

        // Delete profile and genetic data
        await _profileStore.DeleteAsync(BuildProfileKey(body.CharacterId), cancellationToken);
        await _geneticStore.DeleteAsync(BuildGeneticKey(body.CharacterId), cancellationToken);

        // Clean up bloodline memberships
        var membership = await _membershipStore.GetAsync(BuildBloodlineMemberKey(body.CharacterId), cancellationToken);
        await _membershipStore.DeleteAsync(BuildBloodlineMemberKey(body.CharacterId), cancellationToken);

        if (membership != null)
        {
            foreach (var bloodlineEntry in membership.Bloodlines)
            {
                var memberList = await _memberListStore.GetAsync(
                    BuildBloodlineMembersKey(bloodlineEntry.BloodlineId), cancellationToken);
                if (memberList != null)
                {
                    memberList.MemberIds.Remove(body.CharacterId);
                    await _memberListStore.SaveAsync(
                        BuildBloodlineMembersKey(bloodlineEntry.BloodlineId), memberList,
                        cancellationToken: cancellationToken);
                }
            }
        }

        // Decrement parent child counts (per map: ETAG-WRITE)
        if (profile?.ParentAId != null)
        {
            await DecrementChildCountAsync(profile.ParentAId.Value, cancellationToken);
        }
        if (profile?.ParentBId != null)
        {
            await DecrementChildCountAsync(profile.ParentBId.Value, cancellationToken);
        }

        // Invalidate cache
        await _cacheStore.DeleteAsync(BuildManifestKey(body.CharacterId), cancellationToken);

        _logger.LogInformation("Cleaned up lifecycle data for character {CharacterId}", body.CharacterId);

        return (StatusCodes.OK, new CleanupByCharacterResponse());
    }

    /// <summary>
    /// Cleans up all lifecycle data for a realm. Uses per-item error isolation.
    /// </summary>
    public async Task<(StatusCodes, CleanupByRealmResponse?)> CleanupByRealmAsync(CleanupByRealmRequest body, CancellationToken cancellationToken)
    {
        var profiles = await _queryableProfileStore.QueryAsync(
            p => p.RealmId == body.RealmId, cancellationToken);

        var cleanedCount = 0;
        foreach (var profile in profiles)
        {
            try
            {
                // Read genetic profile before deletion to reach the hybrid index when applicable.
                var genetic = await _geneticStore.GetAsync(BuildGeneticKey(profile.CharacterId), cancellationToken);

                // Remove from reverse indexes before deleting source records. See
                // CleanupByCharacterAsync for the ordering rationale.
                await _profileStringStore.RemoveFromStringListAsync(
                    BuildLifecycleByTemplateKey(profile.SpeciesCode, profile.GameServiceId),
                    profile.CharacterId.ToString(),
                    ReverseIndexMaxRetries,
                    _logger,
                    cancellationToken);
                if (genetic != null)
                {
                    await _heritageStringStore.RemoveFromStringListAsync(
                        BuildGeneticByTraitTemplateKey(genetic.SpeciesCode, profile.GameServiceId),
                        profile.CharacterId.ToString(),
                        ReverseIndexMaxRetries,
                        _logger,
                        cancellationToken);
                    if (genetic.SecondarySpecies != null)
                    {
                        await _heritageStringStore.RemoveFromStringListAsync(
                            BuildGeneticByHybridTemplateKey(
                                genetic.SpeciesCode, genetic.SecondarySpecies, profile.GameServiceId),
                            profile.CharacterId.ToString(),
                            ReverseIndexMaxRetries,
                            _logger,
                            cancellationToken);
                    }
                }

                await _profileStore.DeleteAsync(BuildProfileKey(profile.CharacterId), cancellationToken);
                await _geneticStore.DeleteAsync(BuildGeneticKey(profile.CharacterId), cancellationToken);
                await _membershipStore.DeleteAsync(BuildBloodlineMemberKey(profile.CharacterId), cancellationToken);
                await _cacheStore.DeleteAsync(BuildManifestKey(profile.CharacterId), cancellationToken);
                cleanedCount++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to clean up character {CharacterId} during realm cleanup", profile.CharacterId);
            }
        }

        _logger.LogInformation("Cleaned up {CleanedCount} lifecycle profiles for realm {RealmId}",
            cleanedCount, body.RealmId);

        return (StatusCodes.OK, new CleanupByRealmResponse { CleanedCount = cleanedCount });
    }

    /// <summary>
    /// Assembles lifecycle and heritage data for archive compression.
    /// Returns profile summary, phenotype, aptitudes, and bloodline memberships.
    /// </summary>
    public async Task<(StatusCodes, LifecycleArchive?)> GetCompressDataAsync(GetCompressDataRequest body, CancellationToken cancellationToken)
    {
        var profile = await _profileStore.GetAsync(BuildProfileKey(body.CharacterId), cancellationToken);
        if (profile == null)
            return (StatusCodes.NotFound, null);

        // Genetic may be null for first-gen characters
        var genetic = await _geneticStore.GetAsync(BuildGeneticKey(body.CharacterId), cancellationToken);

        // Bloodline memberships
        var membership = await _membershipStore.GetAsync(BuildBloodlineMemberKey(body.CharacterId), cancellationToken);

        return (StatusCodes.OK, new LifecycleArchive
        {
            ResourceId = body.CharacterId,
            CharacterId = body.CharacterId,
            LifecycleSummary = MapProfileToSummary(profile),
            Phenotype = genetic?.Phenotype.ToList(),
            Aptitudes = genetic?.Aptitudes.ToList(),
            Bloodlines = membership?.Bloodlines.ToList()
        });
    }

    /// <summary>
    /// Restores lifecycle and heritage data from archive. Writes profile and genetic data as-is.
    /// </summary>
    public async Task<(StatusCodes, RestoreFromArchiveResponse?)> RestoreFromArchiveAsync(RestoreFromArchiveRequest body, CancellationToken cancellationToken)
    {
        // Restore profile from archive summary
        if (body.Data.LifecycleSummary != null)
        {
            var summary = body.Data.LifecycleSummary;
            var profile = new LifecycleProfileModel
            {
                CharacterId = body.CharacterId,
                GameServiceId = summary.GameServiceId,
                RealmId = summary.RealmId,
                SpeciesCode = summary.SpeciesCode,
                BirthGameYear = summary.BirthGameYear,
                BirthSeason = summary.BirthSeason,
                CurrentAge = summary.CurrentAge,
                CurrentStage = summary.CurrentStage,
                CauseOfCreation = summary.CauseOfCreation,
                ParentAId = summary.ParentAId,
                ParentBId = summary.ParentBId,
                HouseholdOrgId = summary.HouseholdOrgId,
                MarriageContractIds = summary.MarriageContractIds?.ToList() ?? new List<Guid>(),
                SpouseCharacterIds = summary.SpouseCharacterIds?.ToList() ?? new List<Guid>(),
                ChildCount = summary.ChildCount,
                TotalChildCount = summary.TotalChildCount,
                FertilityModifier = summary.FertilityModifier,
                HealthModifier = summary.HealthModifier,
                NaturalDeathYear = summary.NaturalDeathYear,
                FulfillmentScore = summary.FulfillmentScore,
                DeathGameYear = summary.DeathGameYear,
                DeathCause = summary.DeathCause,
                AfterlifePath = summary.AfterlifePath,
                Status = summary.Status,
                CreatedAt = DateTimeOffset.UtcNow
            };

            await _profileStore.SaveAsync(BuildProfileKey(body.CharacterId), profile,
                cancellationToken: cancellationToken);
        }

        // Restore genetic profile as-is (immutable) — requires LifecycleSummary for SpeciesCode
        if (body.Data.Phenotype != null && body.Data.LifecycleSummary != null)
        {
            var geneticProfile = new GeneticProfileModel
            {
                CharacterId = body.CharacterId,
                SpeciesCode = body.Data.LifecycleSummary.SpeciesCode,
                GenerationDepth = 0,
                Genotype = new List<GenotypeEntry>(),
                Phenotype = body.Data.Phenotype.ToList(),
                Aptitudes = body.Data.Aptitudes?.ToList() ?? new List<AptitudeEntry>(),
                Bloodlines = body.Data.Bloodlines?.ToList() ?? new List<BloodlineEntry>(),
                Mutations = new List<MutationEntry>(),
                CreatedAt = DateTimeOffset.UtcNow
            };

            await _geneticStore.SaveAsync(BuildGeneticKey(body.CharacterId), geneticProfile,
                cancellationToken: cancellationToken);
        }

        // Invalidate cache
        await _cacheStore.DeleteAsync(BuildManifestKey(body.CharacterId), cancellationToken);

        _logger.LogInformation("Restored lifecycle data from archive for character {CharacterId}", body.CharacterId);

        return (StatusCodes.OK, new RestoreFromArchiveResponse());
    }

}
