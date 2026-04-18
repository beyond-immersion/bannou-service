using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Character;
using BeyondImmersion.BannouService.Contract;
using BeyondImmersion.BannouService.Currency;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Helpers;
using BeyondImmersion.BannouService.Inventory;
using BeyondImmersion.BannouService.Item;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Providers;
using BeyondImmersion.BannouService.Quest.Caching;
using BeyondImmersion.BannouService.Resource;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
namespace BeyondImmersion.BannouService.Quest;

/// <summary>
/// Implementation of the Quest service.
/// Quest is a thin orchestration layer over lib-contract providing game-flavored
/// quest semantics: objectives are milestones, rewards are prebound API executions.
/// </summary>
/// <remarks>
/// <para>
/// <b>FOUNDATION TENETS - PARTIAL CLASS REQUIRED:</b> This class MUST remain a partial class.
/// Generated code (event handlers, permissions) is placed in companion partial classes.
/// </para>
/// <para>
/// <b>SERVICE HIERARCHY (L2 Game Foundation):</b>
/// <list type="bullet">
///   <item>Hard dependencies (constructor injection): IResourceClient (L1), IContractClient (L1), ICharacterClient (L2), ICurrencyClient (L2), IInventoryClient (L2), IItemClient (L2), IDistributedLockProvider (L0)</item>
///   <item>Dynamic dependencies (DI collection): IEnumerable&lt;IPrerequisiteProviderFactory&gt; for L4 prerequisite providers</item>
/// </list>
/// </para>
/// </remarks>
[BannouService("quest", typeof(IQuestService), lifetime: ServiceLifetime.Scoped, layer: ServiceLayer.GameFoundation)]
public partial class QuestService : IQuestService, ICleanDeprecatedEntity
{
    private readonly IMessageBus _messageBus;
    private readonly ILogger<QuestService> _logger;
    private readonly QuestServiceConfiguration _configuration;
    private readonly IContractClient _contractClient;
    private readonly ICharacterClient _characterClient;
    private readonly ICurrencyClient _currencyClient;
    private readonly IInventoryClient _inventoryClient;
    private readonly IItemClient _itemClient;
    private readonly IDistributedLockProvider _lockProvider;
    private readonly IQuestDataCache _questDataCache;
    private readonly IEnumerable<IPrerequisiteProviderFactory> _prerequisiteProviders;
    private readonly ITelemetryProvider _telemetryProvider;
    private readonly IResourceClient _resourceClient;

    /// <summary>Queryable state store for quest definitions (MySQL).</summary>
    private readonly IQueryableStateStore<QuestDefinitionModel> _definitionStore;

    /// <summary>Queryable state store for quest instances (MySQL).</summary>
    private readonly IQueryableStateStore<QuestInstanceModel> _instanceStore;

    /// <summary>Cache state store for quest definitions (Redis).</summary>
    private readonly IStateStore<QuestDefinitionModel> _definitionCache;

    /// <summary>State store for objective progress tracking (Redis).</summary>
    private readonly IStateStore<ObjectiveProgressModel> _progressStore;

    /// <summary>Cacheable state store for character quest indexes (Redis).</summary>
    private readonly ICacheableStateStore<CharacterQuestIndex> _characterIndex;

    /// <summary>State store for quest cooldown entries (Redis).</summary>
    private readonly IStateStore<CooldownEntry> _cooldownStore;

    /// <summary>String-typed state store for definition→instance reverse indexes.</summary>
    private readonly IStateStore<string> _instanceStringStore;

    #region Key Prefixes

    private const string DEFINITION_KEY_PREFIX = "def:";
    private const string DEFINITION_CODE_KEY_PREFIX = "def:code:";
    private const string INSTANCE_KEY_PREFIX = "inst:";
    private const string PROGRESS_KEY_PREFIX = "prog:";
    private const string CHARACTER_INDEX_KEY_PREFIX = "char:";
    private const string COOLDOWN_KEY_PREFIX = "cd:";
    private const string LOCK_KEY_PREFIX = "quest:lock:";
    private const string DEFINITION_INSTANCE_INDEX = "def-inst:";

    #endregion

    #region Key Building

    internal static string BuildDefinitionKey(Guid definitionId) => $"{DEFINITION_KEY_PREFIX}{definitionId}";
    internal static string BuildDefinitionCodeKey(string code) => $"{DEFINITION_CODE_KEY_PREFIX}{code.ToUpperInvariant()}";
    internal static string BuildInstanceKey(Guid instanceId) => $"{INSTANCE_KEY_PREFIX}{instanceId}";
    internal static string BuildProgressKey(Guid instanceId, string objectiveCode) => $"{PROGRESS_KEY_PREFIX}{instanceId}:{objectiveCode}";
    internal static string BuildCharacterIndexKey(Guid characterId) => $"{CHARACTER_INDEX_KEY_PREFIX}{characterId}";
    internal static string BuildCooldownKey(Guid characterId, string questCode) => $"{COOLDOWN_KEY_PREFIX}{characterId}:{questCode}";
    internal static string BuildLockKey(string resource) => $"{LOCK_KEY_PREFIX}{resource}";
    internal static string BuildDefinitionInstanceIndexKey(Guid definitionId) => $"{DEFINITION_INSTANCE_INDEX}{definitionId}";

    #endregion

    /// <summary>
    /// Initializes a new instance of the <see cref="QuestService"/> class.
    /// </summary>
    /// <param name="messageBus">Message bus for event publishing.</param>
    /// <param name="stateStoreFactory">State store factory for persistence.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="configuration">Service configuration.</param>
    /// <param name="contractClient">Contract client for template/instance management (L1 hard dependency).</param>
    /// <param name="characterClient">Character client for validation (L2 hard dependency).</param>
    /// <param name="currencyClient">Currency client for balance checks (L2 hard dependency).</param>
    /// <param name="inventoryClient">Inventory client for item checks (L2 hard dependency).</param>
    /// <param name="itemClient">Item client for template lookups (L2 hard dependency).</param>
    /// <param name="lockProvider">Distributed lock provider (L0 hard dependency).</param>
    /// <param name="eventConsumer">Event consumer for subscription registration.</param>
    /// <param name="questDataCache">Quest data cache for actor variable provider.</param>
    /// <param name="prerequisiteProviders">Prerequisite provider factories for L4 dynamic prerequisites.</param>
    /// <param name="telemetryProvider">Telemetry provider for span instrumentation.</param>
    /// <param name="resourceClient">Resource client for reference tracking and cleanup (L1 hard dependency).</param>
    public QuestService(
        IMessageBus messageBus,
        IStateStoreFactory stateStoreFactory,
        ILogger<QuestService> logger,
        QuestServiceConfiguration configuration,
        IContractClient contractClient,
        ICharacterClient characterClient,
        ICurrencyClient currencyClient,
        IInventoryClient inventoryClient,
        IItemClient itemClient,
        IDistributedLockProvider lockProvider,
        IEventConsumer eventConsumer,
        IQuestDataCache questDataCache,
        IEnumerable<IPrerequisiteProviderFactory> prerequisiteProviders,
        ITelemetryProvider telemetryProvider,
        IResourceClient resourceClient)
    {
        _messageBus = messageBus;
        _logger = logger;
        _configuration = configuration;
        _contractClient = contractClient;
        _characterClient = characterClient;
        _currencyClient = currencyClient;
        _inventoryClient = inventoryClient;
        _itemClient = itemClient;
        _lockProvider = lockProvider;
        _questDataCache = questDataCache;
        _prerequisiteProviders = prerequisiteProviders;
        _telemetryProvider = telemetryProvider;
        _resourceClient = resourceClient;

        // Eagerly resolve all state stores per FOUNDATION TENETS (constructor-cached, no lazy initialization)
        _definitionStore = stateStoreFactory.GetQueryableStore<QuestDefinitionModel>(StateStoreDefinitions.QuestDefinition);
        _instanceStore = stateStoreFactory.GetQueryableStore<QuestInstanceModel>(StateStoreDefinitions.QuestInstance);
        _definitionCache = stateStoreFactory.GetStore<QuestDefinitionModel>(StateStoreDefinitions.QuestDefinitionCache);
        _progressStore = stateStoreFactory.GetStore<ObjectiveProgressModel>(StateStoreDefinitions.QuestObjectiveProgress);
        _characterIndex = stateStoreFactory.GetCacheableStore<CharacterQuestIndex>(StateStoreDefinitions.QuestCharacterIndex);
        _cooldownStore = stateStoreFactory.GetStore<CooldownEntry>(StateStoreDefinitions.QuestCooldown);
        _instanceStringStore = stateStoreFactory.GetStore<string>(StateStoreDefinitions.QuestInstance);

        // Register event handlers via partial class (QuestServiceEvents.cs)
        RegisterEventConsumers(eventConsumer);
    }

    #region Definition Endpoints

    /// <inheritdoc/>
    public async Task<(StatusCodes, QuestDefinitionResponse?)> CreateQuestDefinitionAsync(
        CreateQuestDefinitionRequest body,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Creating quest definition with code {Code}", body.Code);

        // Validate code format (uppercase, underscores)
        var normalizedCode = body.Code.ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(normalizedCode))
        {
            _logger.LogWarning("Quest code cannot be empty");
            return (StatusCodes.BadRequest, null);
        }

        // Check for duplicate code
        var existingByCode = await _definitionStore.QueryAsync(
            d => d.Code == normalizedCode,
            cancellationToken: cancellationToken);
        if (existingByCode.Count > 0)
        {
            _logger.LogWarning("Quest definition with code {Code} already exists", normalizedCode);
            return (StatusCodes.Conflict, null);
        }

        var definitionId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        // Build contract template via IContractClient
        var templateRequest = BuildContractTemplateRequest(body, definitionId, normalizedCode);
        ContractTemplateResponse templateResponse;
        try
        {
            templateResponse = await _contractClient.CreateContractTemplateAsync(
                templateRequest,
                cancellationToken);
        }
        catch (ApiException ex) when (ex.StatusCode == 409)
        {
            // Template with same code already exists - this is expected for repeated quest creation
            _logger.LogWarning("Contract template with code quest_{Code} already exists", normalizedCode.ToLowerInvariant());
            return (StatusCodes.Conflict, null);
        }

        if (templateResponse == null)
        {
            _logger.LogWarning("Failed to create contract template for quest {Code}: null response",
                normalizedCode);
            return (StatusCodes.ServiceUnavailable, null);
        }

        // Build quest definition model
        var definition = new QuestDefinitionModel
        {
            DefinitionId = definitionId,
            ContractTemplateId = templateResponse.TemplateId,
            Code = normalizedCode,
            Name = body.Name,
            Description = body.Description,
            Category = body.Category,
            Difficulty = body.Difficulty,
            LevelRequirement = body.LevelRequirement,
            Repeatable = body.Repeatable,
            CooldownSeconds = body.CooldownSeconds,
            DeadlineSeconds = body.DeadlineSeconds,
            MaxQuestors = body.MaxQuestors,
            Objectives = MapObjectiveDefinitions(body.Objectives),
            Prerequisites = MapPrerequisiteDefinitions(body.Prerequisites),
            Rewards = MapRewardDefinitions(body.Rewards),
            Tags = body.Tags,
            QuestGiverCharacterId = body.QuestGiverCharacterId,
            GameServiceId = body.GameServiceId,
            IsDeprecated = false,
            CreatedAt = now
        };

        // Save to state store
        var definitionKey = BuildDefinitionKey(definitionId);
        await _definitionStore.SaveAsync(definitionKey, definition, cancellationToken: cancellationToken);

        _logger.LogInformation("Quest definition created: {DefinitionId} with code {Code}",
            definitionId, normalizedCode);

        var response = MapToDefinitionResponse(definition);
        return (StatusCodes.OK, response);
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, QuestDefinitionResponse?)> GetQuestDefinitionAsync(
        GetQuestDefinitionRequest body,
        CancellationToken cancellationToken)
    {
        QuestDefinitionModel? definition = null;

        // Try cache first if getting by ID
        if (body.DefinitionId.HasValue)
        {
            var cacheKey = BuildDefinitionKey(body.DefinitionId.Value);
            definition = await _definitionCache.GetAsync(cacheKey, cancellationToken);

            if (definition == null)
            {
                // Cache miss - query from MySQL
                var results = await _definitionStore.QueryAsync(
                    d => d.DefinitionId == body.DefinitionId.Value,
                    cancellationToken: cancellationToken);
                definition = results.FirstOrDefault();

                // Populate cache if found
                if (definition != null)
                {
                    await _definitionCache.SaveAsync(
                        cacheKey,
                        definition,
                        new StateOptions { Ttl = _configuration.DefinitionCacheTtlSeconds },
                        cancellationToken);
                }
            }
        }
        else if (!string.IsNullOrWhiteSpace(body.Code))
        {
            var normalizedCode = body.Code.ToUpperInvariant();
            var results = await _definitionStore.QueryAsync(
                d => d.Code == normalizedCode,
                cancellationToken: cancellationToken);
            definition = results.FirstOrDefault();
        }
        else
        {
            _logger.LogWarning("GetQuestDefinition requires either definitionId or code");
            return (StatusCodes.BadRequest, null);
        }

        if (definition == null)
        {
            return (StatusCodes.NotFound, null);
        }

        var response = MapToDefinitionResponse(definition);
        return (StatusCodes.OK, response);
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, ListQuestDefinitionsResponse?)> ListQuestDefinitionsAsync(
        ListQuestDefinitionsRequest body,
        CancellationToken cancellationToken)
    {
        var results = await _definitionStore.QueryAsync(
            d => (body.GameServiceId == null || d.GameServiceId == body.GameServiceId) &&
                (body.Category == null || d.Category == body.Category) &&
                (body.Difficulty == null || d.Difficulty == body.Difficulty) &&
                (body.IncludeDeprecated == true || !d.IsDeprecated),
            cancellationToken: cancellationToken);

        // Filter by tags if specified
        if (body.Tags != null && body.Tags.Count > 0)
        {
            results = results.Where(d =>
                d.Tags != null && d.Tags.Any(t => body.Tags.Contains(t))).ToList();
        }

        var total = results.Count;

        // Apply pagination (Offset and Limit have schema defaults of 0 and 50)
        var paged = results.Skip(body.Offset).Take(body.Limit).ToList();

        var response = new ListQuestDefinitionsResponse
        {
            Definitions = paged.Select(MapToDefinitionResponse).ToList(),
            Total = total
        };

        return (StatusCodes.OK, response);
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, QuestDefinitionResponse?)> UpdateQuestDefinitionAsync(
        UpdateQuestDefinitionRequest body,
        CancellationToken cancellationToken)
    {
        var definitionKey = BuildDefinitionKey(body.DefinitionId);

        var (result, definition) = await _definitionStore.UpdateWithRetryAsync(
            definitionKey,
            d =>
            {
                // Update mutable fields only
                if (!string.IsNullOrWhiteSpace(body.Name))
                    d.Name = body.Name;
                if (body.ChangeFields.IsFieldSet("description"))
                    d.Description = body.Description;
                if (body.Category.HasValue)
                    d.Category = body.Category.Value;
                if (body.Difficulty.HasValue)
                    d.Difficulty = body.Difficulty.Value;
                if (body.Tags != null)
                    d.Tags = body.Tags.ToList();
            },
            _configuration.MaxConcurrencyRetries,
            _logger,
            cancellationToken);

        if (result == UpdateResult.NotFound)
        {
            return (StatusCodes.NotFound, null);
        }

        if (result == UpdateResult.Conflict)
        {
            return (StatusCodes.Conflict, null);
        }

        // UpdateWithRetryAsync guarantees non-null entity on Success
        var updatedDefinition = definition ?? throw new InvalidOperationException("UpdateWithRetryAsync returned Success with null entity");

        // Invalidate cache
        await _definitionCache.DeleteAsync(definitionKey, cancellationToken);

        _logger.LogInformation("Quest definition updated: {DefinitionId}", body.DefinitionId);
        return (StatusCodes.OK, MapToDefinitionResponse(updatedDefinition));
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, QuestDefinitionResponse?)> DeprecateQuestDefinitionAsync(
        DeprecateQuestDefinitionRequest body,
        CancellationToken cancellationToken)
    {
        var definitionKey = BuildDefinitionKey(body.DefinitionId);

        QuestDefinitionModel? idempotentEntity = null;
        var (result, entity, errorStatus) = await _definitionStore.UpdateWithRetryAsync(
            definitionKey,
            async def =>
            {
                if (def.IsDeprecated)
                {
                    // Already deprecated - idempotent success
                    idempotentEntity = def;
                    return MutationResult.SkipWith(StatusCodes.OK);
                }

                def.IsDeprecated = true;
                def.DeprecatedAt = DateTimeOffset.UtcNow;
                def.DeprecationReason = body.Reason;

                await Task.CompletedTask;
                return MutationResult.Mutated;
            },
            _configuration.MaxConcurrencyRetries,
            _logger,
            cancellationToken);

        switch (result)
        {
            case UpdateResult.Success:
                // UpdateWithRetryAsync guarantees non-null entity on Success
                var deprecatedDefinition = entity ?? throw new InvalidOperationException("UpdateWithRetryAsync returned Success with null entity");

                // Invalidate cache
                await _definitionCache.DeleteAsync(definitionKey, cancellationToken);

                // Per IMPLEMENTATION TENETS: deprecation published as *.updated with changedFields
                await _messageBus.PublishQuestDefinitionUpdatedAsync(new QuestDefinitionUpdatedEvent
                {
                    EventId = Guid.NewGuid(),
                    Timestamp = DateTimeOffset.UtcNow,
                    DefinitionId = deprecatedDefinition.DefinitionId,
                    ContractTemplateId = deprecatedDefinition.ContractTemplateId,
                    Code = deprecatedDefinition.Code,
                    Name = deprecatedDefinition.Name,
                    Description = deprecatedDefinition.Description,
                    Category = deprecatedDefinition.Category,
                    Difficulty = deprecatedDefinition.Difficulty,
                    LevelRequirement = deprecatedDefinition.LevelRequirement,
                    Repeatable = deprecatedDefinition.Repeatable,
                    CooldownSeconds = deprecatedDefinition.CooldownSeconds,
                    DeadlineSeconds = deprecatedDefinition.DeadlineSeconds,
                    MaxQuestors = deprecatedDefinition.MaxQuestors,
                    IsDeprecated = deprecatedDefinition.IsDeprecated,
                    DeprecatedAt = deprecatedDefinition.DeprecatedAt,
                    DeprecationReason = deprecatedDefinition.DeprecationReason,
                    GameServiceId = deprecatedDefinition.GameServiceId,
                    CreatedAt = deprecatedDefinition.CreatedAt,
                    ChangedFields = new List<string> { "isDeprecated", "deprecatedAt", "deprecationReason" }
                }, cancellationToken);

                _logger.LogInformation("Quest definition deprecated: {DefinitionId}", body.DefinitionId);
                return (StatusCodes.OK, MapToDefinitionResponse(deprecatedDefinition));

            case UpdateResult.ValidationFailed when errorStatus == StatusCodes.OK:
                // Idempotent success - already deprecated
                var alreadyDeprecated = idempotentEntity ?? throw new InvalidOperationException("Idempotent deprecation path reached with null entity");
                return (StatusCodes.OK, MapToDefinitionResponse(alreadyDeprecated));

            case UpdateResult.NotFound:
                return (StatusCodes.NotFound, null);

            default:
                return (StatusCodes.Conflict, null);
        }
    }

    #endregion

    #region Instance Endpoints

    /// <inheritdoc/>
    public async Task<(StatusCodes, QuestInstanceResponse?)> AcceptQuestAsync(
        AcceptQuestRequest body,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Accepting quest for character {CharacterId}", body.QuestorCharacterId);

        // Get quest definition
        QuestDefinitionModel? definition = null;
        if (body.DefinitionId.HasValue)
        {
            definition = await GetDefinitionModelAsync(body.DefinitionId.Value, cancellationToken);
        }
        else if (!string.IsNullOrWhiteSpace(body.Code))
        {
            var normalizedCode = body.Code.ToUpperInvariant();
            var results = await _definitionStore.QueryAsync(
                d => d.Code == normalizedCode,
                cancellationToken: cancellationToken);
            definition = results.FirstOrDefault();
        }

        if (definition == null)
        {
            _logger.LogWarning("Quest definition not found");
            return (StatusCodes.NotFound, null);
        }

        // Check deprecated
        if (definition.IsDeprecated)
        {
            _logger.LogWarning("Cannot accept deprecated quest {Code}", definition.Code);
            return (StatusCodes.BadRequest, null);
        }

        // Validate character exists
        try
        {
            await _characterClient.GetCharacterAsync(
                new GetCharacterRequest { CharacterId = body.QuestorCharacterId },
                cancellationToken);
        }
        catch (ApiException)
        {
            _logger.LogWarning("Character not found: {CharacterId}", body.QuestorCharacterId);
            return (StatusCodes.BadRequest, null);
        }

        // Acquire distributed lock for character's quest operations
        var lockKey = BuildLockKey($"char:{body.QuestorCharacterId}");
        await using var lockResponse = await _lockProvider.LockAsync(
            StateStoreDefinitions.QuestInstance,
            lockKey,
            Guid.NewGuid().ToString(),
            _configuration.LockExpirySeconds,
            cancellationToken);

        if (!lockResponse.Success)
        {
            _logger.LogWarning("Failed to acquire lock for character {CharacterId}", body.QuestorCharacterId);
            return (StatusCodes.Conflict, null);
        }

        // Check max active quests
        var characterIndexKey = BuildCharacterIndexKey(body.QuestorCharacterId);
        var characterIndex = await _characterIndex.GetAsync(characterIndexKey, cancellationToken);
        var activeCount = characterIndex?.ActiveQuestIds?.Count ?? 0;

        if (activeCount >= _configuration.MaxActiveQuestsPerCharacter)
        {
            _logger.LogWarning("Character {CharacterId} already has maximum active quests ({Max})",
                body.QuestorCharacterId, _configuration.MaxActiveQuestsPerCharacter);
            return (StatusCodes.BadRequest, null);
        }

        // Check cooldown for repeatable quests
        if (definition.Repeatable && definition.CooldownSeconds.HasValue)
        {
            var cooldownKey = BuildCooldownKey(body.QuestorCharacterId, definition.Code);
            var cooldown = await _cooldownStore.GetAsync(cooldownKey, cancellationToken);
            if (cooldown != null && cooldown.ExpiresAt > DateTimeOffset.UtcNow)
            {
                _logger.LogWarning("Quest {Code} is on cooldown for character {CharacterId} until {ExpiresAt}",
                    definition.Code, body.QuestorCharacterId, cooldown.ExpiresAt);
                return (StatusCodes.Conflict, null);
            }
        }

        // Check if already has active instance of this quest
        if (characterIndex?.ActiveQuestIds != null)
        {
            foreach (var activeQuestId in characterIndex.ActiveQuestIds)
            {
                var activeInstance = await _instanceStore.QueryAsync(
                    i => i.QuestInstanceId == activeQuestId && i.DefinitionId == definition.DefinitionId,
                    cancellationToken: cancellationToken);
                if (activeInstance.Any())
                {
                    _logger.LogWarning("Character {CharacterId} already has active instance of quest {Code}",
                        body.QuestorCharacterId, definition.Code);
                    return (StatusCodes.Conflict, null);
                }
            }
        }

        // Check prerequisites
        var completedCodes = characterIndex?.CompletedQuestCodes ?? new List<string>();
        var failedPrerequisites = await CheckPrerequisitesAsync(
            definition, body.QuestorCharacterId, completedCodes, cancellationToken);

        if (failedPrerequisites.Count > 0)
        {
            _logger.LogInformation(
                "Character {CharacterId} failed {Count} prerequisites for quest {QuestCode}",
                body.QuestorCharacterId, failedPrerequisites.Count, definition.Code);
            // Per IMPLEMENTATION TENETS: errors return null payload, status code communicates result
            return (StatusCodes.BadRequest, null);
        }

        var now = DateTimeOffset.UtcNow;
        var questInstanceId = Guid.NewGuid();

        // Build contract parties - questor plus optional quest giver
        var parties = new List<ContractPartyInput>
            {
                new()
                {
                    EntityId = body.QuestorCharacterId,
                    EntityType = EntityType.Character,
                    Role = "questor"
                }
            };

        // Add quest giver as a party (can be a specific NPC or system placeholder)
        var questGiverId = body.QuestGiverCharacterId ?? definition.QuestGiverCharacterId;
        if (questGiverId.HasValue)
        {
            parties.Add(new ContractPartyInput
            {
                EntityId = questGiverId.Value,
                EntityType = EntityType.Character,
                Role = "quest_giver"
            });
        }
        else
        {
            // Use a system placeholder for quests without specific quest givers
            parties.Add(new ContractPartyInput
            {
                EntityId = definition.GameServiceId, // Use game service as system party
                EntityType = EntityType.System,
                Role = "quest_giver"
            });
        }

        // Create contract instance
        var contractRequest = new CreateContractInstanceRequest
        {
            TemplateId = definition.ContractTemplateId,
            Parties = parties
        };

        ContractInstanceResponse contractResponse;
        try
        {
            contractResponse = await _contractClient.CreateContractInstanceAsync(
                contractRequest,
                cancellationToken);
        }
        catch (ApiException ex)
        {
            _logger.LogWarning(ex, "Failed to create contract instance for quest {Code}", definition.Code);
            return (StatusCodes.ServiceUnavailable, null);
        }

        if (contractResponse == null)
        {
            _logger.LogWarning("Failed to create contract instance for quest {Code}: null response",
                definition.Code);
            return (StatusCodes.ServiceUnavailable, null);
        }

        // Auto-consent for questor
        var consentRequest = new ConsentToContractRequest
        {
            ContractId = contractResponse.ContractId,
            PartyEntityId = body.QuestorCharacterId,
            PartyEntityType = EntityType.Character
        };

        try
        {
            await _contractClient.ConsentToContractAsync(consentRequest, cancellationToken);
        }
        catch (ApiException ex)
        {
            _logger.LogWarning(ex, "Failed to record consent for quest {Code}", definition.Code);
            // Contract was created but consent failed - may need cleanup
            return (StatusCodes.ServiceUnavailable, null);
        }

        // Resolve and set template values for reward prebound APIs
        var templateValues = await ResolveTemplateValuesAsync(
            body.QuestorCharacterId,
            definition,
            cancellationToken);

        if (templateValues.Count > 0)
        {
            try
            {
                var setValuesRequest = new SetTemplateValuesRequest
                {
                    ContractInstanceId = contractResponse.ContractId,
                    TemplateValues = templateValues
                };
                await _contractClient.SetContractTemplateValuesAsync(setValuesRequest, cancellationToken);
                _logger.LogDebug(
                    "Set {Count} template values for quest {Code} contract {ContractId}",
                    templateValues.Count, definition.Code, contractResponse.ContractId);
            }
            catch (ApiException ex)
            {
                _logger.LogWarning(ex,
                    "Failed to set template values for quest {Code}, rewards may fail",
                    definition.Code);
                await _messageBus.TryPublishErrorAsync(
                    "quest",
                    "AcceptQuest",
                    "SetTemplateValuesFailed",
                    ex.Message,
                    dependency: "contract",
                    endpoint: "set-contract-template-values",
                    stack: ex.StackTrace,
                    cancellationToken: cancellationToken);
            }
        }

        // Create quest instance
        var questInstance = new QuestInstanceModel
        {
            QuestInstanceId = questInstanceId,
            DefinitionId = definition.DefinitionId,
            ContractInstanceId = contractResponse.ContractId,
            Code = definition.Code,
            Name = definition.Name,
            Status = QuestStatus.Active,
            QuestorCharacterIds = new List<Guid> { body.QuestorCharacterId },
            QuestGiverCharacterId = body.QuestGiverCharacterId,
            AcceptedAt = now,
            Deadline = definition.DeadlineSeconds.HasValue
                ? now.AddSeconds(definition.DeadlineSeconds.Value)
                : null,
            GameServiceId = definition.GameServiceId
        };

        var instanceKey = BuildInstanceKey(questInstanceId);
        await _instanceStore.SaveAsync(instanceKey, questInstance, cancellationToken: cancellationToken);

        // Register character reference with lib-resource for cleanup per FOUNDATION TENETS
        await RegisterCharacterReferenceAsync(questInstanceId.ToString(), body.QuestorCharacterId, cancellationToken);

        // Initialize objective progress
        if (definition.Objectives != null)
        {
            foreach (var objective in definition.Objectives)
            {
                var progressKey = BuildProgressKey(questInstanceId, objective.Code);
                var progress = new ObjectiveProgressModel
                {
                    QuestInstanceId = questInstanceId,
                    ObjectiveCode = objective.Code,
                    Name = objective.Name,
                    Description = objective.Description,
                    ObjectiveType = objective.ObjectiveType,
                    CurrentCount = 0,
                    RequiredCount = objective.RequiredCount,
                    IsComplete = false,
                    Hidden = objective.Hidden,
                    RevealBehavior = objective.RevealBehavior,
                    Optional = objective.Optional
                };
                await _progressStore.SaveAsync(
                    progressKey,
                    progress,
                    new StateOptions { Ttl = _configuration.ProgressCacheTtlSeconds },
                    cancellationToken);
            }
        }

        // Update character index with ETag concurrency per IMPLEMENTATION TENETS
        await UpdateCharacterIndexAsync(body.QuestorCharacterId, idx =>
        {
            idx.ActiveQuestIds ??= new List<Guid>();
            idx.ActiveQuestIds.Add(questInstanceId);
        }, cancellationToken);

        // Maintain definition→instance reverse index for clean-deprecated sweep
        await _instanceStringStore.AddToStringListAsync(
            BuildDefinitionInstanceIndexKey(definition.DefinitionId),
            questInstanceId.ToString(),
            _configuration.MaxConcurrencyRetries,
            _logger,
            cancellationToken);

        // Publish quest accepted event (action event)
        var acceptedEvent = new QuestAcceptedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = now,
            QuestInstanceId = questInstanceId,
            DefinitionId = definition.DefinitionId,
            QuestCode = definition.Code,
            QuestorCharacterIds = new List<Guid> { body.QuestorCharacterId },
            GameServiceId = definition.GameServiceId
        };
        await _messageBus.PublishQuestAcceptedAsync(acceptedEvent, cancellationToken);

        // Publish lifecycle created event
        await _messageBus.PublishQuestInstanceCreatedAsync(new QuestInstanceCreatedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = now,
            QuestInstanceId = questInstanceId,
            DefinitionId = definition.DefinitionId,
            ContractInstanceId = contractResponse.ContractId,
            Code = definition.Code,
            Name = definition.Name,
            Status = QuestStatus.Active,
            QuestGiverCharacterId = questInstance.QuestGiverCharacterId,
            GameServiceId = definition.GameServiceId,
            AcceptedAt = now,
            CompletedAt = null,
            Deadline = questInstance.Deadline,
            CreatedAt = now,
            UpdatedAt = now
        }, cancellationToken);

        _logger.LogInformation("Quest accepted: {QuestInstanceId} ({Code}) by character {CharacterId}",
            questInstanceId, definition.Code, body.QuestorCharacterId);

        var response = await MapToInstanceResponseAsync(questInstance, definition, cancellationToken);
        return (StatusCodes.OK, response);
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, QuestInstanceResponse?)> AbandonQuestAsync(
        AbandonQuestRequest body,
        CancellationToken cancellationToken)
    {
        var instanceKey = BuildInstanceKey(body.QuestInstanceId);

        for (var attempt = 0; attempt < _configuration.MaxConcurrencyRetries; attempt++)
        {
            var (instance, etag) = await _instanceStore.GetWithETagAsync(instanceKey, cancellationToken);

            if (instance == null)
            {
                return (StatusCodes.NotFound, null);
            }

            if (instance.Status != QuestStatus.Active)
            {
                _logger.LogWarning("Cannot abandon quest {QuestInstanceId} in status {Status}",
                    body.QuestInstanceId, instance.Status);
                return (StatusCodes.Conflict, null);
            }

            if (!instance.QuestorCharacterIds.Contains(body.QuestorCharacterId))
            {
                _logger.LogWarning("Character {CharacterId} is not a questor on quest {QuestInstanceId}",
                    body.QuestorCharacterId, body.QuestInstanceId);
                return (StatusCodes.BadRequest, null);
            }

            // Terminate underlying contract FIRST — fallible inter-service call before
            // irreversible local state mutation. Per IMPLEMENTATION TENETS (T7 Strategy 3):
            // if termination fails, quest status is unchanged and caller can retry.
            var terminateRequest = new TerminateContractInstanceRequest
            {
                ContractId = instance.ContractInstanceId,
                RequestingEntityId = body.QuestorCharacterId,
                RequestingEntityType = EntityType.Character,
                Reason = "Quest abandoned by player"
            };
            try
            {
                await _contractClient.TerminateContractInstanceAsync(terminateRequest, cancellationToken);
            }
            catch (ApiException ex)
            {
                _logger.LogWarning(ex, "Failed to terminate contract {ContractId} for quest {QuestInstanceId}, abandonment aborted",
                    instance.ContractInstanceId, body.QuestInstanceId);
                return ((StatusCodes)ex.StatusCode, null);
            }

            var now = DateTimeOffset.UtcNow;
            instance.Status = QuestStatus.Abandoned;
            instance.CompletedAt = now;

            // GetWithETagAsync returns non-null etag for existing records;
            // coalesce satisfies compiler's nullable analysis (will never execute)
            var saveResult = await _instanceStore.TrySaveAsync(instanceKey, instance, etag ?? string.Empty, cancellationToken: cancellationToken);
            if (saveResult == null)
            {
                _logger.LogDebug("Concurrent modification abandoning quest {QuestInstanceId}, retrying (attempt {Attempt})",
                    body.QuestInstanceId, attempt + 1);
                continue;
            }

            // Update character index with ETag concurrency per IMPLEMENTATION TENETS
            await UpdateCharacterIndexAsync(body.QuestorCharacterId, idx =>
            {
                idx.ActiveQuestIds?.Remove(body.QuestInstanceId);
            }, cancellationToken);

            // Publish abandoned event (action event)
            var abandonedEvent = new QuestAbandonedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = now,
                QuestInstanceId = body.QuestInstanceId,
                QuestCode = instance.Code,
                AbandoningCharacterId = body.QuestorCharacterId
            };
            await _messageBus.PublishQuestAbandonedAsync(abandonedEvent, cancellationToken);

            // Publish lifecycle updated event with changedFields
            await _messageBus.PublishQuestInstanceUpdatedAsync(new QuestInstanceUpdatedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = now,
                QuestInstanceId = body.QuestInstanceId,
                DefinitionId = instance.DefinitionId,
                ContractInstanceId = instance.ContractInstanceId,
                Code = instance.Code,
                Name = instance.Name,
                Status = QuestStatus.Abandoned,
                QuestGiverCharacterId = instance.QuestGiverCharacterId,
                GameServiceId = instance.GameServiceId,
                AcceptedAt = instance.AcceptedAt,
                CompletedAt = now,
                Deadline = instance.Deadline,
                CreatedAt = instance.AcceptedAt,
                UpdatedAt = now,
                ChangedFields = new List<string> { "status", "completedAt" }
            }, cancellationToken);

            _logger.LogInformation("Quest abandoned: {QuestInstanceId} by character {CharacterId}",
                body.QuestInstanceId, body.QuestorCharacterId);

            var definition = await GetDefinitionModelAsync(instance.DefinitionId, cancellationToken);
            var response = await MapToInstanceResponseAsync(instance, definition, cancellationToken);
            return (StatusCodes.OK, response);
        }

        _logger.LogWarning("Failed to abandon quest {QuestInstanceId} after {MaxRetries} attempts",
            body.QuestInstanceId, _configuration.MaxConcurrencyRetries);
        return (StatusCodes.Conflict, null);
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, QuestInstanceResponse?)> GetQuestAsync(
        GetQuestRequest body,
        CancellationToken cancellationToken)
    {
        var instanceKey = BuildInstanceKey(body.QuestInstanceId);
        var instance = await _instanceStore.GetAsync(instanceKey, cancellationToken);

        if (instance == null)
        {
            return (StatusCodes.NotFound, null);
        }

        var definition = await GetDefinitionModelAsync(instance.DefinitionId, cancellationToken);
        var response = await MapToInstanceResponseAsync(instance, definition, cancellationToken);
        return (StatusCodes.OK, response);
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, ListQuestsResponse?)> ListQuestsAsync(
        ListQuestsRequest body,
        CancellationToken cancellationToken)
    {
        var results = await _instanceStore.QueryAsync(
            i => i.QuestorCharacterIds.Contains(body.CharacterId),
            cancellationToken: cancellationToken);

        // Filter by statuses if specified
        if (body.Statuses != null && body.Statuses.Count > 0)
        {
            results = results.Where(i => body.Statuses.Contains(i.Status)).ToList();
        }

        var total = results.Count;

        // Apply pagination (Offset and Limit have schema defaults of 0 and 50)
        var paged = results.Skip(body.Offset).Take(body.Limit).ToList();

        var responseQuests = new List<QuestInstanceResponse>();
        foreach (var instance in paged)
        {
            var definition = await GetDefinitionModelAsync(instance.DefinitionId, cancellationToken);
            var instanceResponse = await MapToInstanceResponseAsync(instance, definition, cancellationToken);
            responseQuests.Add(instanceResponse);
        }

        var response = new ListQuestsResponse
        {
            Quests = responseQuests,
            Total = total
        };

        return (StatusCodes.OK, response);
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, ListAvailableQuestsResponse?)> ListAvailableQuestsAsync(
        ListAvailableQuestsRequest body,
        CancellationToken cancellationToken)
    {
        // Get all non-deprecated definitions
        var definitions = await _definitionStore.QueryAsync(
            d => !d.IsDeprecated &&
                (body.GameServiceId == null || d.GameServiceId == body.GameServiceId) &&
                (body.QuestGiverCharacterId == null || d.QuestGiverCharacterId == body.QuestGiverCharacterId),
            cancellationToken: cancellationToken);

        // Get character's quest state
        var characterIndexKey = BuildCharacterIndexKey(body.CharacterId);
        var characterIndex = await _characterIndex.GetAsync(characterIndexKey, cancellationToken);
        var activeQuestIds = characterIndex?.ActiveQuestIds ?? new List<Guid>();
        var completedCodes = characterIndex?.CompletedQuestCodes ?? new List<string>();

        // Get active quest definition IDs
        var activeDefinitionIds = new HashSet<Guid>();
        foreach (var activeQuestId in activeQuestIds)
        {
            var instanceKey = BuildInstanceKey(activeQuestId);
            var instance = await _instanceStore.GetAsync(instanceKey, cancellationToken);
            if (instance != null)
            {
                activeDefinitionIds.Add(instance.DefinitionId);
            }
        }

        var available = new List<QuestDefinitionResponse>();
        foreach (var definition in definitions)
        {
            // Skip if already active
            if (activeDefinitionIds.Contains(definition.DefinitionId))
                continue;

            // Skip if non-repeatable and already completed
            if (!definition.Repeatable && completedCodes.Contains(definition.Code))
                continue;

            // Check cooldown for repeatable quests
            if (definition.Repeatable && definition.CooldownSeconds.HasValue)
            {
                var cooldownKey = BuildCooldownKey(body.CharacterId, definition.Code);
                var cooldown = await _cooldownStore.GetAsync(cooldownKey, cancellationToken);
                if (cooldown != null && cooldown.ExpiresAt > DateTimeOffset.UtcNow)
                    continue;
            }

            // Check prerequisites
            var failedPrereqs = await CheckPrerequisitesAsync(definition, body.CharacterId, completedCodes, cancellationToken);
            if (failedPrereqs.Count > 0)
                continue;

            available.Add(MapToDefinitionResponse(definition));
        }

        var response = new ListAvailableQuestsResponse
        {
            Available = available
        };

        return (StatusCodes.OK, response);
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, QuestLogResponse?)> GetQuestLogAsync(
        GetQuestLogRequest body,
        CancellationToken cancellationToken)
    {
        var characterIndexKey = BuildCharacterIndexKey(body.CharacterId);
        var characterIndex = await _characterIndex.GetAsync(characterIndexKey, cancellationToken);

        var activeQuests = new List<QuestLogEntry>();
        var completedCount = characterIndex?.CompletedQuestCodes?.Count ?? 0;
        var failedCount = 0;

        if (characterIndex?.ActiveQuestIds != null)
        {
            foreach (var questId in characterIndex.ActiveQuestIds)
            {
                var instanceKey = BuildInstanceKey(questId);
                var instance = await _instanceStore.GetAsync(instanceKey, cancellationToken);
                if (instance == null) continue;

                var definition = await GetDefinitionModelAsync(instance.DefinitionId, cancellationToken);

                // Apply category filter if specified
                var questCategory = definition?.Category ?? QuestCategory.Side;
                if (body.Category.HasValue && questCategory != body.Category.Value)
                {
                    continue;
                }

                // Load internal progress models for visibility filtering
                var internalProgressList = new List<ObjectiveProgressModel>();
                if (definition?.Objectives != null)
                {
                    foreach (var objDef in definition.Objectives)
                    {
                        var progressKey = BuildProgressKey(questId, objDef.Code);
                        var progress = await _progressStore.GetAsync(progressKey, cancellationToken);
                        if (progress != null)
                        {
                            internalProgressList.Add(progress);
                        }
                    }
                }

                // Calculate overall progress from required objectives
                var requiredObjectives = internalProgressList.Where(p => !p.Optional).ToList();
                var overallProgress = requiredObjectives.Count > 0
                    ? requiredObjectives.Average(p => p.RequiredCount > 0
                        ? (float)p.CurrentCount / p.RequiredCount * 100
                        : 100f)
                    : 100.0f;

                // Filter visible objectives using internal model's RevealBehavior
                var visibleProgressModels = internalProgressList.Where(p =>
                    !p.Hidden ||
                    p.RevealBehavior == ObjectiveRevealBehavior.Always ||
                    (p.RevealBehavior == ObjectiveRevealBehavior.OnProgress && p.CurrentCount > 0) ||
                    (p.RevealBehavior == ObjectiveRevealBehavior.OnComplete && p.IsComplete)
                ).ToList();

                // Map filtered internal models to response type
                var visibleObjectives = visibleProgressModels.Select(MapToObjectiveProgress).ToList();

                activeQuests.Add(new QuestLogEntry
                {
                    QuestInstanceId = instance.QuestInstanceId,
                    Code = instance.Code,
                    Name = instance.Name,
                    Category = questCategory,
                    Status = instance.Status,
                    OverallProgress = overallProgress,
                    VisibleObjectives = visibleObjectives,
                    Deadline = instance.Deadline,
                    AcceptedAt = instance.AcceptedAt
                });
            }
        }

        // Count failed quests
        var allInstances = await _instanceStore.QueryAsync(
            i => i.QuestorCharacterIds.Contains(body.CharacterId) && i.Status == QuestStatus.Failed,
            cancellationToken: cancellationToken);
        failedCount = allInstances.Count;

        var response = new QuestLogResponse
        {
            ActiveQuests = activeQuests,
            CompletedCount = completedCount,
            FailedCount = failedCount
        };

        return (StatusCodes.OK, response);
    }

    #endregion

    #region Objective Endpoints

    /// <inheritdoc/>
    public async Task<(StatusCodes, ObjectiveProgressResponse?)> ReportObjectiveProgressAsync(
        ReportProgressRequest body,
        CancellationToken cancellationToken)
    {
        var instanceKey = BuildInstanceKey(body.QuestInstanceId);
        var instance = await _instanceStore.GetAsync(instanceKey, cancellationToken);

        if (instance == null)
        {
            return (StatusCodes.NotFound, null);
        }

        if (instance.Status != QuestStatus.Active)
        {
            _logger.LogWarning("Cannot report progress on quest {QuestInstanceId} in status {Status}",
                body.QuestInstanceId, instance.Status);
            return (StatusCodes.BadRequest, null);
        }

        var progressKey = BuildProgressKey(body.QuestInstanceId, body.ObjectiveCode);

        for (var attempt = 0; attempt < _configuration.MaxConcurrencyRetries; attempt++)
        {
            var (progress, etag) = await _progressStore.GetWithETagAsync(progressKey, cancellationToken);

            if (progress == null)
            {
                _logger.LogWarning("Objective {ObjectiveCode} not found on quest {QuestInstanceId}",
                    body.ObjectiveCode, body.QuestInstanceId);
                return (StatusCodes.NotFound, null);
            }

            if (progress.IsComplete)
            {
                // Already complete - return current state
                return (StatusCodes.OK, new ObjectiveProgressResponse
                {
                    QuestInstanceId = body.QuestInstanceId,
                    Objective = MapToObjectiveProgress(progress),
                    MilestoneCompleted = false
                });
            }

            // Check for duplicate entity tracking if TrackedEntityId is provided
            // This prevents counting the same kill/collect multiple times
            if (body.TrackedEntityId.HasValue)
            {
                progress.TrackedEntityIds ??= new HashSet<Guid>();
                if (progress.TrackedEntityIds.Contains(body.TrackedEntityId.Value))
                {
                    _logger.LogDebug("Entity {EntityId} already tracked for objective {ObjectiveCode} on quest {QuestInstanceId}",
                        body.TrackedEntityId.Value, body.ObjectiveCode, body.QuestInstanceId);
                    // Return current state without incrementing - idempotent behavior
                    return (StatusCodes.OK, new ObjectiveProgressResponse
                    {
                        QuestInstanceId = body.QuestInstanceId,
                        Objective = MapToObjectiveProgress(progress),
                        MilestoneCompleted = false
                    });
                }
                // Add entity to tracked set
                progress.TrackedEntityIds.Add(body.TrackedEntityId.Value);
            }

            var previousCount = progress.CurrentCount;
            progress.CurrentCount = Math.Min(
                progress.CurrentCount + body.IncrementBy,
                progress.RequiredCount);

            var wasComplete = progress.IsComplete;
            progress.IsComplete = progress.CurrentCount >= progress.RequiredCount;

            // GetWithETagAsync returns non-null etag for existing records;
            // coalesce satisfies compiler's nullable analysis (will never execute)
            var saveResult = await _progressStore.TrySaveAsync(
                progressKey,
                progress,
                etag ?? string.Empty,
                cancellationToken: cancellationToken);

            if (saveResult == null)
            {
                _logger.LogDebug("Concurrent modification reporting progress on {ObjectiveCode}, retrying (attempt {Attempt})",
                    body.ObjectiveCode, attempt + 1);
                continue;
            }

            var milestoneCompleted = !wasComplete && progress.IsComplete;

            // Publish progress event
            var progressEvent = new QuestObjectiveProgressedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                QuestInstanceId = body.QuestInstanceId,
                QuestCode = instance.Code,
                ObjectiveCode = body.ObjectiveCode,
                CurrentCount = progress.CurrentCount,
                RequiredCount = progress.RequiredCount,
                IsComplete = progress.IsComplete
            };
            await _messageBus.PublishQuestObjectiveProgressedAsync(progressEvent, cancellationToken);

            // If milestone completed, notify Contract service
            if (milestoneCompleted)
            {
                var milestoneRequest = new CompleteMilestoneRequest
                {
                    ContractId = instance.ContractInstanceId,
                    MilestoneCode = body.ObjectiveCode
                };
                try
                {
                    await _contractClient.CompleteMilestoneAsync(milestoneRequest, cancellationToken);
                }
                catch (ApiException ex)
                {
                    _logger.LogWarning(ex, "Failed to complete milestone {MilestoneCode} on contract {ContractId}",
                        body.ObjectiveCode, instance.ContractInstanceId);
                    await _messageBus.TryPublishErrorAsync(
                        "quest",
                        "ReportObjectiveProgress",
                        "CompleteMilestoneFailed",
                        ex.Message,
                        dependency: "contract",
                        endpoint: "complete-milestone",
                        stack: ex.StackTrace,
                        cancellationToken: cancellationToken);
                }
            }

            _logger.LogDebug("Objective progress updated: {QuestInstanceId}/{ObjectiveCode} = {Current}/{Required}",
                body.QuestInstanceId, body.ObjectiveCode, progress.CurrentCount, progress.RequiredCount);

            return (StatusCodes.OK, new ObjectiveProgressResponse
            {
                QuestInstanceId = body.QuestInstanceId,
                Objective = MapToObjectiveProgress(progress),
                MilestoneCompleted = milestoneCompleted
            });
        }

        _logger.LogWarning("Failed to update objective progress after {MaxRetries} attempts",
            _configuration.MaxConcurrencyRetries);
        return (StatusCodes.Conflict, null);
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, ObjectiveProgressResponse?)> ForceCompleteObjectiveAsync(
        ForceCompleteObjectiveRequest body,
        CancellationToken cancellationToken)
    {
        var instanceKey = BuildInstanceKey(body.QuestInstanceId);
        var instance = await _instanceStore.GetAsync(instanceKey, cancellationToken);

        if (instance == null)
        {
            return (StatusCodes.NotFound, null);
        }

        var progressKey = BuildProgressKey(body.QuestInstanceId, body.ObjectiveCode);

        for (var attempt = 0; attempt < _configuration.MaxConcurrencyRetries; attempt++)
        {
            var (progress, etag) = await _progressStore.GetWithETagAsync(progressKey, cancellationToken);

            if (progress == null)
            {
                return (StatusCodes.NotFound, null);
            }

            if (progress.IsComplete)
            {
                return (StatusCodes.OK, new ObjectiveProgressResponse
                {
                    QuestInstanceId = body.QuestInstanceId,
                    Objective = MapToObjectiveProgress(progress),
                    MilestoneCompleted = false
                });
            }

            progress.CurrentCount = progress.RequiredCount;
            progress.IsComplete = true;

            // GetWithETagAsync returns non-null etag for existing records;
            // coalesce satisfies compiler's nullable analysis (will never execute)
            var saveResult = await _progressStore.TrySaveAsync(
                progressKey,
                progress,
                etag ?? string.Empty,
                cancellationToken: cancellationToken);

            if (saveResult == null)
            {
                _logger.LogDebug(
                    "Concurrent modification force-completing objective {ObjectiveCode} for quest {QuestInstanceId}, retrying (attempt {Attempt})",
                    body.ObjectiveCode, body.QuestInstanceId, attempt + 1);
                continue;
            }

            // Notify Contract service
            var milestoneRequest = new CompleteMilestoneRequest
            {
                ContractId = instance.ContractInstanceId,
                MilestoneCode = body.ObjectiveCode
            };
            try
            {
                await _contractClient.CompleteMilestoneAsync(milestoneRequest, cancellationToken);
            }
            catch (ApiException ex)
            {
                _logger.LogWarning(ex, "Failed to complete milestone {MilestoneCode} on contract {ContractId}",
                    body.ObjectiveCode, instance.ContractInstanceId);
                await _messageBus.TryPublishErrorAsync(
                    "quest",
                    "ForceCompleteObjective",
                    "CompleteMilestoneFailed",
                    ex.Message,
                    dependency: "contract",
                    endpoint: "complete-milestone",
                    stack: ex.StackTrace,
                    cancellationToken: cancellationToken);
            }

            _logger.LogInformation("Objective force completed: {QuestInstanceId}/{ObjectiveCode}",
                body.QuestInstanceId, body.ObjectiveCode);

            return (StatusCodes.OK, new ObjectiveProgressResponse
            {
                QuestInstanceId = body.QuestInstanceId,
                Objective = MapToObjectiveProgress(progress),
                MilestoneCompleted = true
            });
        }

        _logger.LogWarning("Failed to force complete objective {ObjectiveCode} for quest {QuestInstanceId} after {MaxRetries} attempts",
            body.ObjectiveCode, body.QuestInstanceId, _configuration.MaxConcurrencyRetries);
        return (StatusCodes.Conflict, null);
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, ObjectiveProgressResponse?)> GetObjectiveProgressAsync(
        GetObjectiveProgressRequest body,
        CancellationToken cancellationToken)
    {
        var instanceKey = BuildInstanceKey(body.QuestInstanceId);
        var instance = await _instanceStore.GetAsync(instanceKey, cancellationToken);

        if (instance == null)
        {
            return (StatusCodes.NotFound, null);
        }

        var progressKey = BuildProgressKey(body.QuestInstanceId, body.ObjectiveCode);
        var progress = await _progressStore.GetAsync(progressKey, cancellationToken);

        if (progress == null)
        {
            return (StatusCodes.NotFound, null);
        }

        return (StatusCodes.OK, new ObjectiveProgressResponse
        {
            QuestInstanceId = body.QuestInstanceId,
            Objective = MapToObjectiveProgress(progress),
            MilestoneCompleted = false
        });
    }

    #endregion

    #region Internal Callbacks

    /// <inheritdoc/>
    public async Task<StatusCodes> HandleMilestoneCompletedAsync(
        MilestoneCompletedCallback body,
        CancellationToken cancellationToken)
    {
        // Find quest instance by contract ID
        var instances = await _instanceStore.QueryAsync(
            i => i.ContractInstanceId == body.ContractInstanceId,
            cancellationToken: cancellationToken);

        var instance = instances.FirstOrDefault();
        if (instance == null)
        {
            _logger.LogDebug("No quest instance found for contract {ContractInstanceId}", body.ContractInstanceId);
            return StatusCodes.OK; // Not an error - contract may not be quest-related
        }

        _logger.LogDebug("Milestone {MilestoneCode} completed for quest {QuestInstanceId}",
            body.MilestoneCode, instance.QuestInstanceId);

        // The objective should already be marked complete via ReportObjectiveProgress
        // This callback is for any additional post-milestone processing

        return StatusCodes.OK;
    }

    /// <inheritdoc/>
    public async Task<StatusCodes> HandleQuestCompletedAsync(
        QuestCompletedCallback body,
        CancellationToken cancellationToken)
    {
        // Find quest instance by contract ID
        var instances = await _instanceStore.QueryAsync(
            i => i.ContractInstanceId == body.ContractInstanceId,
            cancellationToken: cancellationToken);

        var instance = instances.FirstOrDefault();
        if (instance == null)
        {
            _logger.LogDebug("No quest instance found for contract {ContractInstanceId}", body.ContractInstanceId);
            return StatusCodes.OK;
        }

        await CompleteQuestAsync(instance, cancellationToken);

        return StatusCodes.OK;
    }

    #endregion


    #region Compression Methods

    /// <summary>
    /// Gets quest data for compression during character archival.
    /// Called by Resource service during character compression via compression callback.
    /// </summary>
    /// <param name="body">Request containing the character ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Quest archive data for the character.</returns>
    public async Task<(StatusCodes, QuestArchive?)> GetCompressDataAsync(
        GetCompressDataRequest body,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Getting compress data for character {CharacterId}", body.CharacterId);

        {
            // Get character quest index for fast lookup
            var indexKey = BuildCharacterIndexKey(body.CharacterId);
            var characterIndex = await _characterIndex.GetAsync(indexKey, cancellationToken);

            var activeQuestSummaries = new List<ActiveQuestSummary>();
            var categoryBreakdown = new Dictionary<string, int>();
            var completedCount = 0;

            if (characterIndex != null)
            {
                // Gather active quest summaries
                if (characterIndex.ActiveQuestIds != null)
                {
                    foreach (var questId in characterIndex.ActiveQuestIds)
                    {
                        var instances = await _instanceStore.QueryAsync(
                            i => i.QuestInstanceId == questId,
                            cancellationToken: cancellationToken);
                        var questInstance = instances.FirstOrDefault();
                        if (questInstance == null) continue;

                        var definition = await GetDefinitionModelAsync(questInstance.DefinitionId, cancellationToken);

                        // Count completed objectives
                        var completedObjectives = 0;
                        var totalObjectives = definition?.Objectives?.Count ?? 1;
                        if (definition?.Objectives != null)
                        {
                            foreach (var obj in definition.Objectives)
                            {
                                var progressKey = BuildProgressKey(questId, obj.Code);
                                var progress = await _progressStore.GetAsync(progressKey, cancellationToken);
                                if (progress?.IsComplete == true) completedObjectives++;
                            }
                        }

                        activeQuestSummaries.Add(new ActiveQuestSummary
                        {
                            QuestId = questId,
                            QuestCode = questInstance.Code,
                            Name = definition?.Name ?? questInstance.Name,
                            CurrentObjective = completedObjectives,
                            TotalObjectives = totalObjectives > 0 ? totalObjectives : 1,
                            StartedAt = questInstance.AcceptedAt,
                            Category = definition?.Category
                        });
                    }
                }

                // Calculate completed quest count and category breakdown
                if (characterIndex.CompletedQuestCodes != null)
                {
                    completedCount = characterIndex.CompletedQuestCodes.Count;

                    foreach (var code in characterIndex.CompletedQuestCodes)
                    {
                        var defs = await _definitionStore.QueryAsync(
                            d => d.Code == code.ToUpperInvariant(),
                            cancellationToken: cancellationToken);
                        var def = defs.FirstOrDefault();
                        if (def != null)
                        {
                            var categoryName = def.Category.ToString();
                            categoryBreakdown.TryGetValue(categoryName, out var count);
                            categoryBreakdown[categoryName] = count + 1;
                        }
                    }
                }
            }

            var archive = new QuestArchive
            {
                ResourceId = body.CharacterId,
                ResourceType = "quest",
                ArchivedAt = DateTimeOffset.UtcNow,
                SchemaVersion = 1,
                CharacterId = body.CharacterId,
                ActiveQuests = activeQuestSummaries,
                CompletedQuests = completedCount,
                QuestCategories = categoryBreakdown
            };

            _logger.LogInformation(
                "Prepared quest archive for character {CharacterId}: {ActiveCount} active, {CompletedCount} completed",
                body.CharacterId, activeQuestSummaries.Count, completedCount);

            return (StatusCodes.OK, archive);
        }
    }

    #endregion

    #region Cleanup Endpoints

    /// <summary>
    /// Deletes all quest data for a character. Called by lib-resource during character deletion cleanup.
    /// Abandons active quests, deletes objective progress, cooldowns, and character index.
    /// Only deletes quest instances where the deleted character is the sole remaining questor.
    /// For multi-questor instances, removes the character's participation without destroying the quest.
    /// </summary>
    /// <param name="body">Request containing the character ID to clean up.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Counts of affected records.</returns>
    public async Task<(StatusCodes, DeleteByCharacterResponse?)> DeleteByCharacterAsync(
        DeleteByCharacterRequest body,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Deleting all quest data for character {CharacterId}", body.CharacterId);

        var indexKey = BuildCharacterIndexKey(body.CharacterId);
        var characterIndex = await _characterIndex.GetAsync(indexKey, cancellationToken);

        var instancesAbandoned = 0;
        var progressRecordsDeleted = 0;
        var cooldownsDeleted = 0;

        // Query ALL quest instances for this character (active, completed, abandoned, failed)
        // to ensure comprehensive cleanup — the character index only tracks active quests
        var allInstances = await _instanceStore.QueryAsync(
            i => i.QuestorCharacterIds.Contains(body.CharacterId),
            cancellationToken: cancellationToken);

        foreach (var instance in allInstances)
        {
            try
            {
                var isSoleQuestor = instance.QuestorCharacterIds.Count <= 1;

                // Abandon active quests first (terminate contract, publish abandoned event)
                if (instance.Status == QuestStatus.Active)
                {
                    var instanceKey = BuildInstanceKey(instance.QuestInstanceId);
                    var (current, etag) = await _instanceStore.GetWithETagAsync(instanceKey, cancellationToken);

                    if (current != null && current.Status == QuestStatus.Active)
                    {
                        if (isSoleQuestor)
                        {
                            // Terminate contract FIRST — fallible call before irreversible mutation.
                            // Per IMPLEMENTATION TENETS (T7 Strategy 3): if termination fails,
                            // quest status is unchanged. Cleanup is per-item isolated so this
                            // failure is logged and processing continues to next quest.
                            try
                            {
                                await _contractClient.TerminateContractInstanceAsync(
                                    new TerminateContractInstanceRequest
                                    {
                                        ContractId = current.ContractInstanceId,
                                        RequestingEntityId = body.CharacterId,
                                        RequestingEntityType = EntityType.Character,
                                        Reason = "Character deleted - quest cleanup"
                                    },
                                    cancellationToken);
                            }
                            catch (ApiException ex)
                            {
                                _logger.LogWarning(ex,
                                    "Failed to terminate contract {ContractId} for quest {QuestInstanceId} during character cleanup, skipping quest abandonment",
                                    current.ContractInstanceId, instance.QuestInstanceId);
                                await _messageBus.TryPublishErrorAsync(
                                    "quest",
                                    "DeleteByCharacter",
                                    "TerminateContractFailed",
                                    ex.Message,
                                    dependency: "contract",
                                    endpoint: "terminate-contract-instance",
                                    stack: ex.StackTrace,
                                    cancellationToken: cancellationToken);
                                continue;
                            }

                            // Contract terminated — now safe to write abandoned status
                            current.Status = QuestStatus.Abandoned;
                            current.CompletedAt = DateTimeOffset.UtcNow;

                            // GetWithETagAsync returns non-null etag for existing records;
                            // coalesce satisfies compiler's nullable analysis (will never execute)
                            await _instanceStore.TrySaveAsync(instanceKey, current, etag ?? string.Empty, cancellationToken: cancellationToken);

                            // Publish abandoned event
                            var now = DateTimeOffset.UtcNow;
                            await _messageBus.PublishQuestAbandonedAsync(new QuestAbandonedEvent
                            {
                                EventId = Guid.NewGuid(),
                                Timestamp = now,
                                QuestInstanceId = instance.QuestInstanceId,
                                QuestCode = current.Code,
                                AbandoningCharacterId = body.CharacterId
                            }, cancellationToken);

                            // Publish lifecycle updated event for status change
                            await _messageBus.PublishQuestInstanceUpdatedAsync(new QuestInstanceUpdatedEvent
                            {
                                EventId = Guid.NewGuid(),
                                Timestamp = now,
                                QuestInstanceId = instance.QuestInstanceId,
                                DefinitionId = current.DefinitionId,
                                ContractInstanceId = current.ContractInstanceId,
                                Code = current.Code,
                                Name = current.Name,
                                Status = QuestStatus.Abandoned,
                                QuestGiverCharacterId = current.QuestGiverCharacterId,
                                GameServiceId = current.GameServiceId,
                                AcceptedAt = current.AcceptedAt,
                                CompletedAt = now,
                                Deadline = current.Deadline,
                                CreatedAt = current.AcceptedAt,
                                UpdatedAt = now,
                                ChangedFields = new List<string> { "status", "completedAt" }
                            }, cancellationToken);

                            instancesAbandoned++;
                        }
                        else
                        {
                            // Multi-questor: remove this character from the quest without abandoning it
                            current.QuestorCharacterIds.Remove(body.CharacterId);

                            // GetWithETagAsync returns non-null etag for existing records;
                            // coalesce satisfies compiler's nullable analysis (will never execute)
                            await _instanceStore.TrySaveAsync(instanceKey, current, etag ?? string.Empty, cancellationToken: cancellationToken);
                        }
                    }
                }
                else if (!isSoleQuestor)
                {
                    // Non-active multi-questor instance: remove character from questor list
                    var instanceKey = BuildInstanceKey(instance.QuestInstanceId);
                    var (current, etag) = await _instanceStore.GetWithETagAsync(instanceKey, cancellationToken);

                    if (current != null)
                    {
                        current.QuestorCharacterIds.Remove(body.CharacterId);

                        // GetWithETagAsync returns non-null etag for existing records;
                        // coalesce satisfies compiler's nullable analysis (will never execute)
                        await _instanceStore.TrySaveAsync(instanceKey, current, etag ?? string.Empty, cancellationToken: cancellationToken);
                    }
                }

                // Unregister character reference
                try
                {
                    await UnregisterCharacterReferenceAsync(instance.QuestInstanceId.ToString(), body.CharacterId, cancellationToken);
                }
                catch (ApiException ex)
                {
                    _logger.LogWarning(ex,
                        "Failed to unregister character reference for quest {QuestInstanceId}",
                        instance.QuestInstanceId);
                    await _messageBus.TryPublishErrorAsync(
                        "quest",
                        "DeleteByCharacter",
                        "UnregisterReferenceFailed",
                        ex.Message,
                        dependency: "resource",
                        endpoint: "unregister-reference",
                        stack: ex.StackTrace,
                        cancellationToken: cancellationToken);
                }

                if (isSoleQuestor)
                {
                    // Sole questor: delete the entire instance record (progress, reverse index, lifecycle event)
                    await DeleteInstanceRecordAsync(instance, cancellationToken);
                }
                else
                {
                    // Multi-questor: only delete this character's progress records
                    var definition = await GetDefinitionModelAsync(instance.DefinitionId, cancellationToken);
                    if (definition?.Objectives != null)
                    {
                        foreach (var objective in definition.Objectives)
                        {
                            var progressKey = BuildProgressKey(instance.QuestInstanceId, objective.Code);
                            await _progressStore.DeleteAsync(progressKey, cancellationToken);
                            progressRecordsDeleted++;
                        }
                    }

                    // Remove from this character's index
                    await UpdateCharacterIndexAsync(body.CharacterId, idx =>
                    {
                        idx.ActiveQuestIds?.Remove(instance.QuestInstanceId);
                    }, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                // Per-item error isolation per IMPLEMENTATION TENETS
                _logger.LogWarning(ex,
                    "Failed to clean up quest instance {QuestInstanceId} during character deletion",
                    instance.QuestInstanceId);
            }
        }

        // Delete cooldown entries using completed quest codes from the index
        if (characterIndex?.CompletedQuestCodes != null)
        {
            foreach (var questCode in characterIndex.CompletedQuestCodes)
            {
                try
                {
                    var cooldownKey = BuildCooldownKey(body.CharacterId, questCode);
                    await _cooldownStore.DeleteAsync(cooldownKey, cancellationToken);
                    cooldownsDeleted++;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Failed to delete cooldown for quest code {QuestCode} during character deletion",
                        questCode);
                }
            }
        }

        // Delete the character index itself
        if (characterIndex != null)
        {
            await _characterIndex.DeleteAsync(indexKey, cancellationToken);
        }

        _logger.LogInformation(
            "Character cleanup complete for {CharacterId}: {InstancesAbandoned} abandoned, {ProgressRecordsDeleted} progress deleted, {CooldownsDeleted} cooldowns deleted",
            body.CharacterId, instancesAbandoned, progressRecordsDeleted, cooldownsDeleted);

        return (StatusCodes.OK, new DeleteByCharacterResponse
        {
            InstancesAbandoned = instancesAbandoned,
            ProgressRecordsDeleted = progressRecordsDeleted,
            CooldownsDeleted = cooldownsDeleted
        });
    }

    #endregion


    /// <inheritdoc />
    public async Task<(StatusCodes, QuestInstanceResponse?)> DeleteQuestInstanceAsync(
        DeleteQuestInstanceRequest body, CancellationToken cancellationToken)
    {
        var instanceKey = BuildInstanceKey(body.QuestInstanceId);
        var instance = await _instanceStore.GetAsync(instanceKey, cancellationToken);

        if (instance == null)
        {
            return (StatusCodes.NotFound, null);
        }

        // Active quests must be abandoned first — only non-active instances can be deleted
        if (instance.Status == QuestStatus.Active)
        {
            _logger.LogWarning("Cannot delete active quest instance {QuestInstanceId} — abandon it first",
                body.QuestInstanceId);
            return (StatusCodes.Conflict, null);
        }

        await DeleteInstanceRecordAsync(instance, cancellationToken);

        var definition = await GetDefinitionModelAsync(instance.DefinitionId, cancellationToken);
        var response = await MapToInstanceResponseAsync(instance, definition, cancellationToken);
        return (StatusCodes.OK, response);
    }


    /// <inheritdoc />
    public async Task<(StatusCodes, CleanDeprecatedResponse?)> CleanDeprecatedQuestDefinitionsAsync(
        CleanDeprecatedRequest body, CancellationToken cancellationToken = default)
    {
        // Query all deprecated definitions from MySQL
        var deprecated = await _definitionStore.QueryAsync(
            d => d.IsDeprecated, cancellationToken: cancellationToken);

        var result = await DeprecationCleanupHelper.ExecuteCleanupSweepAsync(
            deprecated,
            getEntityId: d => d.DefinitionId,
            getDeprecatedAt: d => d.DeprecatedAt,
            hasInstancesAsync: (d, ct) =>
                _instanceStringStore.HasStringListEntriesAsync(BuildDefinitionInstanceIndexKey(d.DefinitionId), ct),
            deleteAndPublishAsync: async (d, ct) =>
            {
                // Delete from MySQL definition store
                var definitionKey = BuildDefinitionKey(d.DefinitionId);
                await _definitionStore.DeleteAsync(definitionKey, ct);

                // Invalidate Redis definition cache
                await _definitionCache.DeleteAsync(definitionKey, ct);

                // Clean up reverse index entry (should already be empty, but defensive)
                await _instanceStringStore.DeleteAsync(
                    BuildDefinitionInstanceIndexKey(d.DefinitionId), ct);

                // Publish lifecycle deleted event
                await _messageBus.PublishQuestDefinitionDeletedAsync(new QuestDefinitionDeletedEvent
                {
                    EventId = Guid.NewGuid(),
                    Timestamp = DateTimeOffset.UtcNow,
                    DefinitionId = d.DefinitionId,
                    ContractTemplateId = d.ContractTemplateId,
                    Code = d.Code,
                    Name = d.Name,
                    Description = d.Description,
                    Category = d.Category,
                    Difficulty = d.Difficulty,
                    LevelRequirement = d.LevelRequirement,
                    Repeatable = d.Repeatable,
                    CooldownSeconds = d.CooldownSeconds,
                    DeadlineSeconds = d.DeadlineSeconds,
                    MaxQuestors = d.MaxQuestors,
                    GameServiceId = d.GameServiceId,
                    CreatedAt = d.CreatedAt,
                    UpdatedAt = DateTimeOffset.UtcNow,
                    IsDeprecated = d.IsDeprecated,
                    DeprecatedAt = d.DeprecatedAt,
                    DeprecationReason = d.DeprecationReason
                }, ct);
            },
            body.GracePeriodDays,
            body.DryRun,
            _logger,
            _telemetryProvider,
            cancellationToken);

        return (StatusCodes.OK, new CleanDeprecatedResponse
        {
            Cleaned = result.Cleaned,
            Remaining = result.Remaining,
            Errors = result.Errors,
            CleanedIds = result.CleanedIds.ToList()
        });
    }
}
