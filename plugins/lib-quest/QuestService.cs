using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Character;
using BeyondImmersion.BannouService.Contract;
using BeyondImmersion.BannouService.Currency;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Inventory;
using BeyondImmersion.BannouService.Item;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Providers;
using BeyondImmersion.BannouService.Quest.Caching;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
namespace BeyondImmersion.BannouService.Quest;

/// <summary>
/// Topic constants for quest events.
/// </summary>
public static class QuestTopics
{
    /// <summary>Quest accepted event topic.</summary>
    public const string QuestAccepted = "quest.accepted";
    /// <summary>Objective progress updated event topic.</summary>
    public const string QuestObjectiveProgressed = "quest.objective.progressed";
    /// <summary>Quest completed event topic.</summary>
    public const string QuestCompleted = "quest.completed";
    /// <summary>Quest failed event topic.</summary>
    public const string QuestFailed = "quest.failed";
    /// <summary>Quest abandoned event topic.</summary>
    public const string QuestAbandoned = "quest.abandoned";
}

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
///   <item>Hard dependencies (constructor injection): IContractClient (L1), ICharacterClient (L2), ICurrencyClient (L2), IInventoryClient (L2), IItemClient (L2), IDistributedLockProvider (L0)</item>
///   <item>Dynamic dependencies (DI collection): IEnumerable&lt;IPrerequisiteProviderFactory&gt; for L4 prerequisite providers</item>
///   <item>Soft dependencies (runtime resolution): IAnalyticsClient, IAchievementClient (L4)</item>
/// </list>
/// </para>
/// </remarks>
[BannouService("quest", typeof(IQuestService), lifetime: ServiceLifetime.Scoped, layer: ServiceLayer.GameFoundation)]
public partial class QuestService : IQuestService
{
    private readonly IMessageBus _messageBus;
    private readonly IStateStoreFactory _stateStoreFactory;
    private readonly ILogger<QuestService> _logger;
    private readonly QuestServiceConfiguration _configuration;
    private readonly IContractClient _contractClient;
    private readonly ICharacterClient _characterClient;
    private readonly ICurrencyClient _currencyClient;
    private readonly IInventoryClient _inventoryClient;
    private readonly IItemClient _itemClient;
    private readonly IDistributedLockProvider _lockProvider;
    private readonly IServiceProvider _serviceProvider;
    private readonly IQuestDataCache _questDataCache;
    private readonly IEnumerable<IPrerequisiteProviderFactory> _prerequisiteProviders;
    private readonly ITelemetryProvider _telemetryProvider;

    #region State Store Accessors

    // Lazy-initialized state stores per IMPLEMENTATION TENETS
    private IQueryableStateStore<QuestDefinitionModel>? _definitionStore;
    private IQueryableStateStore<QuestDefinitionModel> DefinitionStore =>
        _definitionStore ??= _stateStoreFactory.GetQueryableStore<QuestDefinitionModel>(StateStoreDefinitions.QuestDefinition);

    private IQueryableStateStore<QuestInstanceModel>? _instanceStore;
    private IQueryableStateStore<QuestInstanceModel> InstanceStore =>
        _instanceStore ??= _stateStoreFactory.GetQueryableStore<QuestInstanceModel>(StateStoreDefinitions.QuestInstance);

    private IStateStore<QuestDefinitionModel>? _definitionCache;
    private IStateStore<QuestDefinitionModel> DefinitionCache =>
        _definitionCache ??= _stateStoreFactory.GetStore<QuestDefinitionModel>(StateStoreDefinitions.QuestDefinitionCache);

    private IStateStore<ObjectiveProgressModel>? _progressStore;
    private IStateStore<ObjectiveProgressModel> ProgressStore =>
        _progressStore ??= _stateStoreFactory.GetStore<ObjectiveProgressModel>(StateStoreDefinitions.QuestObjectiveProgress);

    private ICacheableStateStore<CharacterQuestIndex>? _characterIndex;
    private ICacheableStateStore<CharacterQuestIndex> CharacterIndex =>
        _characterIndex ??= _stateStoreFactory.GetCacheableStore<CharacterQuestIndex>(StateStoreDefinitions.QuestCharacterIndex);

    private IStateStore<CooldownEntry>? _cooldownStore;
    private IStateStore<CooldownEntry> CooldownStore =>
        _cooldownStore ??= _stateStoreFactory.GetStore<CooldownEntry>(StateStoreDefinitions.QuestCooldown);

    #endregion

    #region Key Building

    private static string BuildDefinitionKey(Guid definitionId) => $"def:{definitionId}";
    private static string BuildDefinitionCodeKey(string code) => $"def:code:{code.ToUpperInvariant()}";
    private static string BuildInstanceKey(Guid instanceId) => $"inst:{instanceId}";
    private static string BuildProgressKey(Guid instanceId, string objectiveCode) => $"prog:{instanceId}:{objectiveCode}";
    private static string BuildCharacterIndexKey(Guid characterId) => $"char:{characterId}";
    private static string BuildCooldownKey(Guid characterId, string questCode) => $"cd:{characterId}:{questCode}";
    private static string BuildLockKey(string resource) => $"quest:lock:{resource}";

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
    /// <param name="serviceProvider">Service provider for L4 soft dependencies.</param>
    /// <param name="questDataCache">Quest data cache for actor variable provider.</param>
    /// <param name="prerequisiteProviders">Prerequisite provider factories for L4 dynamic prerequisites.</param>
    /// <param name="telemetryProvider">Telemetry provider for span instrumentation.</param>
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
        IServiceProvider serviceProvider,
        IQuestDataCache questDataCache,
        IEnumerable<IPrerequisiteProviderFactory> prerequisiteProviders,
        ITelemetryProvider telemetryProvider)
    {
        _messageBus = messageBus;
        _stateStoreFactory = stateStoreFactory;
        _logger = logger;
        _configuration = configuration;
        _contractClient = contractClient;
        _characterClient = characterClient;
        _currencyClient = currencyClient;
        _inventoryClient = inventoryClient;
        _itemClient = itemClient;
        _lockProvider = lockProvider;
        _serviceProvider = serviceProvider;
        _questDataCache = questDataCache;
        _prerequisiteProviders = prerequisiteProviders;
        _telemetryProvider = telemetryProvider;

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
        var existingByCode = await DefinitionStore.QueryAsync(
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
        await DefinitionStore.SaveAsync(definitionKey, definition, cancellationToken: cancellationToken);

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
            definition = await DefinitionCache.GetAsync(cacheKey, cancellationToken);

            if (definition == null)
            {
                // Cache miss - query from MySQL
                var results = await DefinitionStore.QueryAsync(
                    d => d.DefinitionId == body.DefinitionId.Value,
                    cancellationToken: cancellationToken);
                definition = results.FirstOrDefault();

                // Populate cache if found
                if (definition != null)
                {
                    await DefinitionCache.SaveAsync(
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
            var results = await DefinitionStore.QueryAsync(
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
        var results = await DefinitionStore.QueryAsync(
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

        for (var attempt = 0; attempt < _configuration.MaxConcurrencyRetries; attempt++)
        {
            var (definition, etag) = await DefinitionStore.GetWithETagAsync(
                definitionKey,
                cancellationToken);

            if (definition == null)
            {
                return (StatusCodes.NotFound, null);
            }

            // Update mutable fields only
            if (!string.IsNullOrWhiteSpace(body.Name))
                definition.Name = body.Name;
            if (body.Description != null)
                definition.Description = body.Description;
            if (body.Category.HasValue)
                definition.Category = body.Category.Value;
            if (body.Difficulty.HasValue)
                definition.Difficulty = body.Difficulty.Value;
            if (body.Tags != null)
                definition.Tags = body.Tags.ToList();

            // GetWithETagAsync returns non-null etag for existing records;
            // coalesce satisfies compiler's nullable analysis (will never execute)
            var saveResult = await DefinitionStore.TrySaveAsync(
                definitionKey,
                definition,
                etag ?? string.Empty,
                cancellationToken: cancellationToken);

            if (saveResult == null)
            {
                _logger.LogDebug("Concurrent modification updating quest definition {DefinitionId}, retrying (attempt {Attempt})",
                    body.DefinitionId, attempt + 1);
                continue;
            }

            // Invalidate cache
            await DefinitionCache.DeleteAsync(definitionKey, cancellationToken);

            _logger.LogInformation("Quest definition updated: {DefinitionId}", body.DefinitionId);
            return (StatusCodes.OK, MapToDefinitionResponse(definition));
        }

        _logger.LogWarning("Failed to update quest definition {DefinitionId} after {MaxRetries} attempts",
            body.DefinitionId, _configuration.MaxConcurrencyRetries);
        return (StatusCodes.Conflict, null);
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, QuestDefinitionResponse?)> DeprecateQuestDefinitionAsync(
        DeprecateQuestDefinitionRequest body,
        CancellationToken cancellationToken)
    {
        var definitionKey = BuildDefinitionKey(body.DefinitionId);

        for (var attempt = 0; attempt < _configuration.MaxConcurrencyRetries; attempt++)
        {
            var (definition, etag) = await DefinitionStore.GetWithETagAsync(
                definitionKey,
                cancellationToken);

            if (definition == null)
            {
                return (StatusCodes.NotFound, null);
            }

            if (definition.IsDeprecated)
            {
                // Already deprecated - idempotent success
                return (StatusCodes.OK, MapToDefinitionResponse(definition));
            }

            definition.IsDeprecated = true;
            definition.DeprecatedAt = DateTimeOffset.UtcNow;
            definition.DeprecationReason = body.Reason;

            // GetWithETagAsync returns non-null etag for existing records;
            // coalesce satisfies compiler's nullable analysis (will never execute)
            var saveResult = await DefinitionStore.TrySaveAsync(
                definitionKey,
                definition,
                etag ?? string.Empty,
                cancellationToken: cancellationToken);

            if (saveResult == null)
            {
                _logger.LogDebug("Concurrent modification deprecating quest definition {DefinitionId}, retrying (attempt {Attempt})",
                    body.DefinitionId, attempt + 1);
                continue;
            }

            // Invalidate cache
            await DefinitionCache.DeleteAsync(definitionKey, cancellationToken);

            _logger.LogInformation("Quest definition deprecated: {DefinitionId}", body.DefinitionId);
            return (StatusCodes.OK, MapToDefinitionResponse(definition));
        }

        _logger.LogWarning("Failed to deprecate quest definition {DefinitionId} after {MaxRetries} attempts",
            body.DefinitionId, _configuration.MaxConcurrencyRetries);
        return (StatusCodes.Conflict, null);
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
            var results = await DefinitionStore.QueryAsync(
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
        var characterIndex = await CharacterIndex.GetAsync(characterIndexKey, cancellationToken);
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
            var cooldown = await CooldownStore.GetAsync(cooldownKey, cancellationToken);
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
                var activeInstance = await InstanceStore.QueryAsync(
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
                // Template values are needed for rewards - this is a critical failure
                _logger.LogWarning(ex,
                    "Failed to set template values for quest {Code}, rewards may fail",
                    definition.Code);
                // Continue anyway - quest is already accepted, rewards will fail gracefully
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
            Status = QuestStatus.ACTIVE,
            QuestorCharacterIds = new List<Guid> { body.QuestorCharacterId },
            QuestGiverCharacterId = body.QuestGiverCharacterId,
            AcceptedAt = now,
            Deadline = definition.DeadlineSeconds.HasValue
                ? now.AddSeconds(definition.DeadlineSeconds.Value)
                : null,
            GameServiceId = definition.GameServiceId
        };

        var instanceKey = BuildInstanceKey(questInstanceId);
        await InstanceStore.SaveAsync(instanceKey, questInstance, cancellationToken: cancellationToken);

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
                await ProgressStore.SaveAsync(
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

        // Publish quest accepted event
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
        await _messageBus.TryPublishAsync(QuestTopics.QuestAccepted, acceptedEvent, cancellationToken: cancellationToken);

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
            var (instance, etag) = await InstanceStore.GetWithETagAsync(instanceKey, cancellationToken);

            if (instance == null)
            {
                return (StatusCodes.NotFound, null);
            }

            if (instance.Status != QuestStatus.ACTIVE)
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

            var now = DateTimeOffset.UtcNow;
            instance.Status = QuestStatus.ABANDONED;
            instance.CompletedAt = now;

            // GetWithETagAsync returns non-null etag for existing records;
            // coalesce satisfies compiler's nullable analysis (will never execute)
            var saveResult = await InstanceStore.TrySaveAsync(instanceKey, instance, etag ?? string.Empty, cancellationToken: cancellationToken);
            if (saveResult == null)
            {
                _logger.LogDebug("Concurrent modification abandoning quest {QuestInstanceId}, retrying (attempt {Attempt})",
                    body.QuestInstanceId, attempt + 1);
                continue;
            }

            // Terminate underlying contract
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
                // Log but don't fail - contract termination is best effort
                _logger.LogWarning(ex, "Failed to terminate contract for abandoned quest {QuestInstanceId}",
                    body.QuestInstanceId);
            }

            // Update character index with ETag concurrency per IMPLEMENTATION TENETS
            await UpdateCharacterIndexAsync(body.QuestorCharacterId, idx =>
            {
                idx.ActiveQuestIds?.Remove(body.QuestInstanceId);
            }, cancellationToken);

            // Publish abandoned event
            var abandonedEvent = new QuestAbandonedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = now,
                QuestInstanceId = body.QuestInstanceId,
                QuestCode = instance.Code,
                AbandoningCharacterId = body.QuestorCharacterId
            };
            await _messageBus.TryPublishAsync(QuestTopics.QuestAbandoned, abandonedEvent, cancellationToken: cancellationToken);

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
        var instance = await InstanceStore.GetAsync(instanceKey, cancellationToken);

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
        var results = await InstanceStore.QueryAsync(
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
        var definitions = await DefinitionStore.QueryAsync(
            d => !d.IsDeprecated &&
                (body.GameServiceId == null || d.GameServiceId == body.GameServiceId) &&
                (body.QuestGiverCharacterId == null || d.QuestGiverCharacterId == body.QuestGiverCharacterId),
            cancellationToken: cancellationToken);

        // Get character's quest state
        var characterIndexKey = BuildCharacterIndexKey(body.CharacterId);
        var characterIndex = await CharacterIndex.GetAsync(characterIndexKey, cancellationToken);
        var activeQuestIds = characterIndex?.ActiveQuestIds ?? new List<Guid>();
        var completedCodes = characterIndex?.CompletedQuestCodes ?? new List<string>();

        // Get active quest definition IDs
        var activeDefinitionIds = new HashSet<Guid>();
        foreach (var activeQuestId in activeQuestIds)
        {
            var instanceKey = BuildInstanceKey(activeQuestId);
            var instance = await InstanceStore.GetAsync(instanceKey, cancellationToken);
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
                var cooldown = await CooldownStore.GetAsync(cooldownKey, cancellationToken);
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
        var characterIndex = await CharacterIndex.GetAsync(characterIndexKey, cancellationToken);

        var activeQuests = new List<QuestLogEntry>();
        var completedCount = characterIndex?.CompletedQuestCodes?.Count ?? 0;
        var failedCount = 0;

        if (characterIndex?.ActiveQuestIds != null)
        {
            foreach (var questId in characterIndex.ActiveQuestIds)
            {
                var instanceKey = BuildInstanceKey(questId);
                var instance = await InstanceStore.GetAsync(instanceKey, cancellationToken);
                if (instance == null) continue;

                var definition = await GetDefinitionModelAsync(instance.DefinitionId, cancellationToken);

                // Load internal progress models for visibility filtering
                var internalProgressList = new List<ObjectiveProgressModel>();
                if (definition?.Objectives != null)
                {
                    foreach (var objDef in definition.Objectives)
                    {
                        var progressKey = BuildProgressKey(questId, objDef.Code);
                        var progress = await ProgressStore.GetAsync(progressKey, cancellationToken);
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
                    p.RevealBehavior == ObjectiveRevealBehavior.ALWAYS ||
                    (p.RevealBehavior == ObjectiveRevealBehavior.ON_PROGRESS && p.CurrentCount > 0) ||
                    (p.RevealBehavior == ObjectiveRevealBehavior.ON_COMPLETE && p.IsComplete)
                ).ToList();

                // Map filtered internal models to response type
                var visibleObjectives = visibleProgressModels.Select(MapToObjectiveProgress).ToList();

                activeQuests.Add(new QuestLogEntry
                {
                    QuestInstanceId = instance.QuestInstanceId,
                    Code = instance.Code,
                    Name = instance.Name,
                    Category = definition?.Category ?? QuestCategory.SIDE,
                    Status = instance.Status,
                    OverallProgress = overallProgress,
                    VisibleObjectives = visibleObjectives,
                    Deadline = instance.Deadline,
                    AcceptedAt = instance.AcceptedAt
                });
            }
        }

        // Count failed quests
        var allInstances = await InstanceStore.QueryAsync(
            i => i.QuestorCharacterIds.Contains(body.CharacterId) && i.Status == QuestStatus.FAILED,
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
        var instance = await InstanceStore.GetAsync(instanceKey, cancellationToken);

        if (instance == null)
        {
            return (StatusCodes.NotFound, null);
        }

        if (instance.Status != QuestStatus.ACTIVE)
        {
            _logger.LogWarning("Cannot report progress on quest {QuestInstanceId} in status {Status}",
                body.QuestInstanceId, instance.Status);
            return (StatusCodes.BadRequest, null);
        }

        var progressKey = BuildProgressKey(body.QuestInstanceId, body.ObjectiveCode);

        for (var attempt = 0; attempt < _configuration.MaxConcurrencyRetries; attempt++)
        {
            var (progress, etag) = await ProgressStore.GetWithETagAsync(progressKey, cancellationToken);

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
            var saveResult = await ProgressStore.TrySaveAsync(
                progressKey,
                progress,
                etag ?? string.Empty,
                cancellationToken);

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
            await _messageBus.TryPublishAsync(QuestTopics.QuestObjectiveProgressed, progressEvent, cancellationToken: cancellationToken);

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
                    // Continue - milestone completion is eventually consistent via events
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
        var instance = await InstanceStore.GetAsync(instanceKey, cancellationToken);

        if (instance == null)
        {
            return (StatusCodes.NotFound, null);
        }

        var progressKey = BuildProgressKey(body.QuestInstanceId, body.ObjectiveCode);

        for (var attempt = 0; attempt < _configuration.MaxConcurrencyRetries; attempt++)
        {
            var (progress, etag) = await ProgressStore.GetWithETagAsync(progressKey, cancellationToken);

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
            var saveResult = await ProgressStore.TrySaveAsync(
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
                // Continue - milestone completion is eventually consistent via events
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
        var instance = await InstanceStore.GetAsync(instanceKey, cancellationToken);

        if (instance == null)
        {
            return (StatusCodes.NotFound, null);
        }

        var progressKey = BuildProgressKey(body.QuestInstanceId, body.ObjectiveCode);
        var progress = await ProgressStore.GetAsync(progressKey, cancellationToken);

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
        var instances = await InstanceStore.QueryAsync(
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
        var instances = await InstanceStore.QueryAsync(
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

    #region Helper Methods

    private async Task<QuestDefinitionModel?> GetDefinitionModelAsync(Guid definitionId, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.quest", "QuestService.GetDefinitionModelAsync");
        var cacheKey = BuildDefinitionKey(definitionId);
        var definition = await DefinitionCache.GetAsync(cacheKey, cancellationToken);
        if (definition != null) return definition;

        var results = await DefinitionStore.QueryAsync(
            d => d.DefinitionId == definitionId,
            cancellationToken: cancellationToken);
        definition = results.FirstOrDefault();

        if (definition != null)
        {
            await DefinitionCache.SaveAsync(
                cacheKey,
                definition,
                new StateOptions { Ttl = _configuration.DefinitionCacheTtlSeconds },
                cancellationToken);
        }

        return definition;
    }

    /// <summary>
    /// Checks whether a character meets all prerequisites for a quest definition.
    /// Returns a list of failed prerequisites (empty list = all prerequisites met).
    /// </summary>
    /// <param name="definition">The quest definition to check prerequisites for.</param>
    /// <param name="characterId">The character ID to check.</param>
    /// <param name="completedCodes">List of quest codes the character has completed.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of failed prerequisites (empty if all met).</returns>
    private async Task<List<PrerequisiteFailure>> CheckPrerequisitesAsync(
        QuestDefinitionModel definition,
        Guid characterId,
        List<string> completedCodes,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.quest", "QuestService.CheckPrerequisitesAsync");
        var failures = new List<PrerequisiteFailure>();

        // No prerequisites = always available
        if (definition.Prerequisites == null || definition.Prerequisites.Count == 0)
            return failures;

        var failFast = _configuration.PrerequisiteValidationMode == PrerequisiteValidationMode.FAIL_FAST;

        foreach (var prereq in definition.Prerequisites)
        {
            PrerequisiteFailure? failure = null;

            switch (prereq.Type)
            {
                case PrerequisiteType.QUEST_COMPLETED:
                    failure = CheckQuestCompletedPrerequisite(prereq, completedCodes, definition.Code, characterId);
                    break;

                case PrerequisiteType.CURRENCY_AMOUNT:
                    failure = await CheckCurrencyPrerequisiteAsync(prereq, characterId, cancellationToken);
                    break;

                case PrerequisiteType.ITEM_OWNED:
                    failure = await CheckItemPrerequisiteAsync(prereq, characterId, cancellationToken);
                    break;

                case PrerequisiteType.CHARACTER_LEVEL:
                    // Stub: Character service doesn't track level yet
                    _logger.LogDebug(
                        "Skipping CHARACTER_LEVEL prerequisite for quest {DefinitionCode}: level tracking not implemented",
                        definition.Code);
                    break;

                case PrerequisiteType.REPUTATION:
                    // Dynamic: Check via IPrerequisiteProviderFactory
                    failure = await CheckDynamicPrerequisiteAsync("reputation", prereq, characterId, cancellationToken);
                    break;

                default:
                    // Unknown type: Try dynamic providers
                    failure = await CheckDynamicPrerequisiteAsync(
                        prereq.Type.ToString().ToLowerInvariant(), prereq, characterId, cancellationToken);
                    break;
            }

            if (failure != null)
            {
                failures.Add(failure);
                if (failFast) return failures;
            }
        }

        return failures;
    }

    /// <summary>
    /// Checks the QUEST_COMPLETED prerequisite type.
    /// </summary>
    private PrerequisiteFailure? CheckQuestCompletedPrerequisite(
        PrerequisiteDefinitionModel prereq,
        List<string> completedCodes,
        string questCode,
        Guid characterId)
    {
        if (string.IsNullOrWhiteSpace(prereq.QuestCode))
            return null;

        var normalizedCode = prereq.QuestCode.ToUpperInvariant();
        if (completedCodes.Contains(normalizedCode))
            return null;

        _logger.LogDebug(
            "Character {CharacterId} has not completed prerequisite quest {PrereqQuestCode} for quest {DefinitionCode}",
            characterId, prereq.QuestCode, questCode);

        return new PrerequisiteFailure
        {
            Type = PrerequisiteType.QUEST_COMPLETED,
            Code = prereq.QuestCode,
            Reason = $"Must complete quest '{prereq.QuestCode}' first",
            CurrentValue = "Not completed",
            RequiredValue = "Completed"
        };
    }

    /// <summary>
    /// Checks the CURRENCY_AMOUNT prerequisite type via ICurrencyClient.
    /// </summary>
    private async Task<PrerequisiteFailure?> CheckCurrencyPrerequisiteAsync(
        PrerequisiteDefinitionModel prereq,
        Guid characterId,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.quest", "QuestService.CheckCurrencyPrerequisiteAsync");
        if (string.IsNullOrWhiteSpace(prereq.CurrencyCode) || !prereq.MinAmount.HasValue)
        {
            _logger.LogWarning("CURRENCY_AMOUNT prerequisite missing currencyCode or minAmount");
            return null;
        }

        try
        {
            // Get currency definition by code
            CurrencyDefinitionResponse currencyDef;
            try
            {
                currencyDef = await _currencyClient.GetCurrencyDefinitionAsync(
                    new GetCurrencyDefinitionRequest { Code = prereq.CurrencyCode },
                    cancellationToken);
            }
            catch (ApiException ex) when (ex.StatusCode == 404)
            {
                return new PrerequisiteFailure
                {
                    Type = PrerequisiteType.CURRENCY_AMOUNT,
                    Code = prereq.CurrencyCode,
                    Reason = $"Unknown currency: {prereq.CurrencyCode}"
                };
            }

            // Get or create wallet for character
            GetOrCreateWalletResponse walletResponse;
            try
            {
                walletResponse = await _currencyClient.GetOrCreateWalletAsync(
                    new GetOrCreateWalletRequest
                    {
                        OwnerId = characterId,
                        OwnerType = EntityType.Character
                    },
                    cancellationToken);
            }
            catch (ApiException)
            {
                return new PrerequisiteFailure
                {
                    Type = PrerequisiteType.CURRENCY_AMOUNT,
                    Code = prereq.CurrencyCode,
                    Reason = "Could not access wallet",
                    CurrentValue = "0",
                    RequiredValue = prereq.MinAmount.Value.ToString()
                };
            }

            if (walletResponse?.Wallet == null)
            {
                return new PrerequisiteFailure
                {
                    Type = PrerequisiteType.CURRENCY_AMOUNT,
                    Code = prereq.CurrencyCode,
                    Reason = "Could not access wallet",
                    CurrentValue = "0",
                    RequiredValue = prereq.MinAmount.Value.ToString()
                };
            }

            // Get balance for this currency
            GetBalanceResponse balance;
            try
            {
                balance = await _currencyClient.GetBalanceAsync(
                    new GetBalanceRequest
                    {
                        WalletId = walletResponse.Wallet.WalletId,
                        CurrencyDefinitionId = currencyDef.DefinitionId
                    },
                    cancellationToken);
            }
            catch (ApiException)
            {
                // No balance means 0
                return new PrerequisiteFailure
                {
                    Type = PrerequisiteType.CURRENCY_AMOUNT,
                    Code = prereq.CurrencyCode,
                    Reason = $"Insufficient {prereq.CurrencyCode}: have 0, need {prereq.MinAmount.Value}",
                    CurrentValue = "0",
                    RequiredValue = prereq.MinAmount.Value.ToString()
                };
            }

            var currentAmount = balance?.EffectiveAmount ?? 0;
            if (currentAmount >= prereq.MinAmount.Value)
                return null;

            return new PrerequisiteFailure
            {
                Type = PrerequisiteType.CURRENCY_AMOUNT,
                Code = prereq.CurrencyCode,
                Reason = $"Insufficient {prereq.CurrencyCode}: have {currentAmount}, need {prereq.MinAmount.Value}",
                CurrentValue = currentAmount.ToString(),
                RequiredValue = prereq.MinAmount.Value.ToString()
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking currency prerequisite {CurrencyCode}", prereq.CurrencyCode);

            await _messageBus.TryPublishErrorAsync(
                "quest",
                "CheckCurrencyPrerequisite",
                "prerequisite_check_failed",
                ex.Message,
                dependency: "currency",
                endpoint: "get-balance",
                details: null,
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);

            return new PrerequisiteFailure
            {
                Type = PrerequisiteType.CURRENCY_AMOUNT,
                Code = prereq.CurrencyCode,
                Reason = "Error checking currency balance"
            };
        }
    }

    /// <summary>
    /// Checks the ITEM_OWNED prerequisite type via IInventoryClient and IItemClient.
    /// </summary>
    private async Task<PrerequisiteFailure?> CheckItemPrerequisiteAsync(
        PrerequisiteDefinitionModel prereq,
        Guid characterId,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.quest", "QuestService.CheckItemPrerequisiteAsync");
        if (string.IsNullOrWhiteSpace(prereq.ItemCode))
        {
            _logger.LogWarning("ITEM_OWNED prerequisite missing itemCode");
            return null;
        }

        var requiredQuantity = prereq.MinAmount ?? 1;

        try
        {
            // Get item template by code
            ItemTemplateResponse template;
            try
            {
                template = await _itemClient.GetItemTemplateAsync(
                    new GetItemTemplateRequest { Code = prereq.ItemCode },
                    cancellationToken);
            }
            catch (ApiException ex) when (ex.StatusCode == 404)
            {
                return new PrerequisiteFailure
                {
                    Type = PrerequisiteType.ITEM_OWNED,
                    Code = prereq.ItemCode,
                    Reason = $"Unknown item: {prereq.ItemCode}"
                };
            }

            // Check if character has enough of this item
            HasItemsResponse hasResponse;
            try
            {
                hasResponse = await _inventoryClient.HasItemsAsync(
                    new HasItemsRequest
                    {
                        OwnerId = characterId,
                        OwnerType = ContainerOwnerType.Character,
                        Requirements = new List<ItemRequirement>
                        {
                            new() { TemplateId = template.TemplateId, Quantity = requiredQuantity }
                        }
                    },
                    cancellationToken);
            }
            catch (ApiException ex) when (ex.StatusCode == 404)
            {
                // Character has no containers or items
                return new PrerequisiteFailure
                {
                    Type = PrerequisiteType.ITEM_OWNED,
                    Code = prereq.ItemCode,
                    Reason = $"Insufficient {prereq.ItemCode}: have 0, need {requiredQuantity}",
                    CurrentValue = "0",
                    RequiredValue = requiredQuantity.ToString()
                };
            }

            if (hasResponse.HasAll)
                return null;

            var currentQuantity = hasResponse.Results?.FirstOrDefault()?.Available ?? 0;
            return new PrerequisiteFailure
            {
                Type = PrerequisiteType.ITEM_OWNED,
                Code = prereq.ItemCode,
                Reason = $"Insufficient {prereq.ItemCode}: have {currentQuantity}, need {requiredQuantity}",
                CurrentValue = currentQuantity.ToString(),
                RequiredValue = requiredQuantity.ToString()
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking item prerequisite {ItemCode}", prereq.ItemCode);

            await _messageBus.TryPublishErrorAsync(
                "quest",
                "CheckItemPrerequisite",
                "prerequisite_check_failed",
                ex.Message,
                dependency: "item",
                endpoint: "has-items",
                details: null,
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);

            return new PrerequisiteFailure
            {
                Type = PrerequisiteType.ITEM_OWNED,
                Code = prereq.ItemCode,
                Reason = "Error checking item ownership"
            };
        }
    }

    /// <summary>
    /// Checks dynamic prerequisites via IPrerequisiteProviderFactory implementations.
    /// This handles L4 service-provided prerequisite types like skills, achievements, etc.
    /// </summary>
    private async Task<PrerequisiteFailure?> CheckDynamicPrerequisiteAsync(
        string providerName,
        PrerequisiteDefinitionModel prereq,
        Guid characterId,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.quest", "QuestService.CheckDynamicPrerequisiteAsync");
        var provider = _prerequisiteProviders.FirstOrDefault(p =>
            string.Equals(p.ProviderName, providerName, StringComparison.OrdinalIgnoreCase));

        if (provider == null)
        {
            _logger.LogDebug(
                "No prerequisite provider registered for type {ProviderName}, skipping check",
                providerName);
            return null; // Graceful degradation: L4 service not enabled
        }

        // Build parameters dictionary from prerequisite definition
        var parameters = new Dictionary<string, object?>
        {
            ["minLevel"] = prereq.MinLevel,
            ["minReputation"] = prereq.MinReputation,
            ["minAmount"] = prereq.MinAmount,
            ["factionCode"] = prereq.FactionCode
        };

        var code = prereq.QuestCode ?? prereq.FactionCode ?? prereq.ItemCode ?? prereq.CurrencyCode ?? "";

        try
        {
            var result = await provider.CheckAsync(characterId, code, parameters, cancellationToken);

            if (result.Satisfied)
                return null;

            return new PrerequisiteFailure
            {
                Type = prereq.Type,
                Code = code,
                Reason = result.FailureReason ?? $"Prerequisite {providerName} not met",
                CurrentValue = result.CurrentValue?.ToString(),
                RequiredValue = result.RequiredValue?.ToString()
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking dynamic prerequisite {ProviderName}", providerName);

            await _messageBus.TryPublishErrorAsync(
                "quest",
                "CheckDynamicPrerequisite",
                "prerequisite_check_failed",
                ex.Message,
                dependency: providerName,
                endpoint: "check-prerequisite",
                details: null,
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);

            return new PrerequisiteFailure
            {
                Type = prereq.Type,
                Code = code,
                Reason = $"Error checking {providerName} prerequisite"
            };
        }
    }

    private async Task<List<ObjectiveProgress>> GetObjectiveProgressListAsync(
        Guid questInstanceId,
        QuestDefinitionModel? definition,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.quest", "QuestService.GetObjectiveProgressListAsync");
        var objectives = new List<ObjectiveProgress>();

        if (definition?.Objectives == null) return objectives;

        foreach (var objDef in definition.Objectives)
        {
            var progressKey = BuildProgressKey(questInstanceId, objDef.Code);
            var progress = await ProgressStore.GetAsync(progressKey, cancellationToken);
            if (progress != null)
            {
                objectives.Add(MapToObjectiveProgress(progress));
            }
        }

        return objectives;
    }

    private async Task CompleteQuestAsync(QuestInstanceModel instance, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.quest", "QuestService.CompleteQuestAsync");
        var instanceKey = BuildInstanceKey(instance.QuestInstanceId);

        for (var attempt = 0; attempt < _configuration.MaxConcurrencyRetries; attempt++)
        {
            var (current, etag) = await InstanceStore.GetWithETagAsync(instanceKey, cancellationToken);
            if (current == null || current.Status != QuestStatus.ACTIVE) return;

            var now = DateTimeOffset.UtcNow;
            current.Status = QuestStatus.COMPLETED;
            current.CompletedAt = now;

            // GetWithETagAsync returns non-null etag for existing records;
            // coalesce satisfies compiler's nullable analysis (will never execute)
            var saveResult = await InstanceStore.TrySaveAsync(instanceKey, current, etag ?? string.Empty, cancellationToken: cancellationToken);
            if (saveResult == null) continue;

            // Update character indexes
            foreach (var characterId in current.QuestorCharacterIds)
            {
                // Update character index with ETag concurrency per IMPLEMENTATION TENETS
                await UpdateCharacterIndexAsync(characterId, idx =>
                {
                    idx.ActiveQuestIds?.Remove(instance.QuestInstanceId);
                    idx.CompletedQuestCodes ??= new List<string>();
                    if (!idx.CompletedQuestCodes.Contains(current.Code))
                    {
                        idx.CompletedQuestCodes.Add(current.Code);
                    }
                }, cancellationToken);

                // Set cooldown if repeatable
                var definition = await GetDefinitionModelAsync(current.DefinitionId, cancellationToken);
                if (definition?.Repeatable == true && definition.CooldownSeconds.HasValue)
                {
                    var cooldownKey = BuildCooldownKey(characterId, current.Code);
                    var cooldown = new CooldownEntry
                    {
                        CharacterId = characterId,
                        QuestCode = current.Code,
                        ExpiresAt = now.AddSeconds(definition.CooldownSeconds.Value)
                    };
                    await CooldownStore.SaveAsync(
                        cooldownKey,
                        cooldown,
                        new StateOptions { Ttl = definition.CooldownSeconds.Value },
                        cancellationToken);
                }
            }

            // Publish completed event
            var completedEvent = new QuestCompletedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = now,
                QuestInstanceId = instance.QuestInstanceId,
                DefinitionId = current.DefinitionId,
                QuestCode = current.Code,
                QuestorCharacterIds = current.QuestorCharacterIds,
                GameServiceId = current.GameServiceId
            };
            await _messageBus.TryPublishAsync(QuestTopics.QuestCompleted, completedEvent, cancellationToken: cancellationToken);

            _logger.LogInformation("Quest completed: {QuestInstanceId} ({Code})",
                instance.QuestInstanceId, current.Code);
            return;
        }
    }

    private CreateContractTemplateRequest BuildContractTemplateRequest(
        CreateQuestDefinitionRequest body,
        Guid definitionId,
        string normalizedCode)
    {
        // Build reward prebound APIs from reward definitions
        var rewardApis = BuildRewardPreboundApis(body.Rewards, normalizedCode);

        // Find the last required objective index for reward attachment
        var lastRequiredIndex = -1;
        for (var i = 0; i < body.Objectives.Count; i++)
        {
            var obj = body.Objectives.ElementAt(i);
            if (obj.Optional != true)
            {
                lastRequiredIndex = i;
            }
        }

        // Build milestones from objectives - contract milestones map to quest objectives
        var milestones = body.Objectives.Select((obj, index) =>
        {
            var milestone = new MilestoneDefinition
            {
                Code = obj.Code,
                Name = obj.Name,
                Description = obj.Description,
                Sequence = index,
                Required = obj.Optional != true
            };

            // Attach rewards to the final required milestone
            if (index == lastRequiredIndex && rewardApis.Count > 0)
            {
                milestone.OnComplete = rewardApis;
            }

            return milestone;
        }).ToList();

        // Contract template code must be lowercase per contract schema validation
        var templateCode = $"quest_{normalizedCode.ToLowerInvariant()}";

        return new CreateContractTemplateRequest
        {
            Code = templateCode,
            Name = $"Quest: {body.Name}",
            Description = body.Description,
            MinParties = 1,
            MaxParties = body.MaxQuestors + 1, // questors + optional quest_giver
            PartyRoles = new List<PartyRoleDefinition>
            {
                new() { Role = "questor", MinCount = 1, MaxCount = body.MaxQuestors },
                new() { Role = "quest_giver", MinCount = 0, MaxCount = 1 }
            },
            Milestones = milestones,
            DefaultEnforcementMode = EnforcementMode.EventOnly
        };
    }

    /// <summary>
    /// Builds prebound API calls for quest rewards.
    /// These are executed when the final required milestone is completed.
    /// </summary>
    private List<PreboundApi> BuildRewardPreboundApis(
        ICollection<RewardDefinition>? rewards,
        string questCode)
    {
        var apis = new List<PreboundApi>();

        if (rewards == null || rewards.Count == 0)
            return apis;

        foreach (var reward in rewards)
        {
            switch (reward.Type)
            {
                case RewardType.CURRENCY:
                    if (!string.IsNullOrEmpty(reward.CurrencyCode) && reward.Amount.HasValue)
                    {
                        // Use template variable for wallet ID - resolved at quest acceptance
                        apis.Add(new PreboundApi
                        {
                            ServiceName = "currency",
                            Endpoint = "/currency/credit",
                            PayloadTemplate = BannouJson.Serialize(new Dictionary<string, object>
                            {
                                ["walletId"] = "{{questor_wallet_id}}",
                                ["currencyCode"] = reward.CurrencyCode,
                                ["amount"] = reward.Amount.Value,
                                ["transactionType"] = "quest_reward",
                                ["referenceId"] = "{{contract.id}}",
                                ["description"] = $"Quest reward: {questCode}"
                            })
                        });
                    }
                    break;

                case RewardType.ITEM:
                    if (!string.IsNullOrEmpty(reward.ItemCode) && reward.Quantity.HasValue)
                    {
                        // Use template variable for container ID - resolved at quest acceptance
                        apis.Add(new PreboundApi
                        {
                            ServiceName = "inventory",
                            Endpoint = "/inventory/add-item",
                            PayloadTemplate = BannouJson.Serialize(new Dictionary<string, object>
                            {
                                ["containerId"] = "{{questor_container_id}}",
                                ["itemCode"] = reward.ItemCode,
                                ["quantity"] = reward.Quantity.Value,
                                ["referenceId"] = "{{contract.id}}",
                                ["referenceType"] = "quest_reward"
                            })
                        });
                    }
                    break;

                case RewardType.EXPERIENCE:
                    // Stub: Experience system not implemented
                    _logger.LogWarning(
                        "EXPERIENCE reward for quest {QuestCode} skipped: experience system not implemented",
                        questCode);
                    break;

                case RewardType.REPUTATION:
                    // Stub: Reputation system not implemented
                    _logger.LogWarning(
                        "REPUTATION reward for quest {QuestCode} skipped: reputation system not implemented",
                        questCode);
                    break;

                default:
                    _logger.LogWarning("Unknown reward type {Type} for quest {QuestCode}", reward.Type, questCode);
                    break;
            }
        }

        return apis;
    }

    /// <summary>
    /// Resolves template values needed for reward prebound API execution.
    /// Gets wallet and container IDs for the questor character.
    /// </summary>
    private async Task<Dictionary<string, string>> ResolveTemplateValuesAsync(
        Guid characterId,
        QuestDefinitionModel definition,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.quest", "QuestService.ResolveTemplateValuesAsync");
        var values = new Dictionary<string, string>();

        // Check if we have any currency or item rewards that need wallet/container IDs
        var hasCurrencyRewards = definition.Rewards?.Any(r => r.Type == RewardType.CURRENCY) ?? false;
        var hasItemRewards = definition.Rewards?.Any(r => r.Type == RewardType.ITEM) ?? false;

        if (hasCurrencyRewards)
        {
            try
            {
                var response = await _currencyClient.GetOrCreateWalletAsync(
                    new GetOrCreateWalletRequest
                    {
                        OwnerId = characterId,
                        OwnerType = EntityType.Character
                    },
                    cancellationToken);

                if (response?.Wallet != null)
                {
                    values["questor_wallet_id"] = response.Wallet.WalletId.ToString();
                }
                else
                {
                    _logger.LogWarning(
                        "Failed to resolve wallet for character {CharacterId}, currency rewards may fail",
                        characterId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error resolving wallet for character {CharacterId}", characterId);

                await _messageBus.TryPublishErrorAsync(
                    "quest",
                    "ResolveTemplateValues",
                    "wallet_resolution_failed",
                    ex.Message,
                    dependency: "currency",
                    endpoint: "get-or-create-wallet",
                    details: null,
                    stack: ex.StackTrace,
                    cancellationToken: cancellationToken);
            }
        }

        if (hasItemRewards)
        {
            try
            {
                var container = await _inventoryClient.GetOrCreateContainerAsync(
                    new GetOrCreateContainerRequest
                    {
                        OwnerId = characterId,
                        OwnerType = ContainerOwnerType.Character,
                        ContainerType = "inventory",
                        ConstraintModel = ContainerConstraintModel.SlotOnly,
                        MaxSlots = _configuration.DefaultRewardContainerMaxSlots
                    },
                    cancellationToken);

                if (container != null)
                {
                    values["questor_container_id"] = container.ContainerId.ToString();
                }
                else
                {
                    _logger.LogWarning(
                        "Failed to resolve inventory container for character {CharacterId}, item rewards may fail",
                        characterId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error resolving container for character {CharacterId}", characterId);

                await _messageBus.TryPublishErrorAsync(
                    "quest",
                    "ResolveTemplateValues",
                    "container_resolution_failed",
                    ex.Message,
                    dependency: "inventory",
                    endpoint: "get-or-create-container",
                    details: null,
                    stack: ex.StackTrace,
                    cancellationToken: cancellationToken);
            }
        }

        return values;
    }

    private static List<ObjectiveDefinitionModel> MapObjectiveDefinitions(ICollection<ObjectiveDefinition>? objectives)
    {
        if (objectives == null) return new List<ObjectiveDefinitionModel>();

        return objectives.Select(o => new ObjectiveDefinitionModel
        {
            Code = o.Code,
            Name = o.Name,
            Description = o.Description,
            ObjectiveType = o.ObjectiveType,
            RequiredCount = o.RequiredCount,
            TargetEntityType = o.TargetEntityType,
            TargetEntitySubtype = o.TargetEntitySubtype,
            TargetLocationId = o.TargetLocationId,
            Hidden = o.Hidden,
            RevealBehavior = o.RevealBehavior ?? ObjectiveRevealBehavior.ALWAYS,
            Optional = o.Optional
        }).ToList();
    }

    private static List<PrerequisiteDefinitionModel>? MapPrerequisiteDefinitions(ICollection<PrerequisiteDefinition>? prerequisites)
    {
        if (prerequisites == null) return null;

        return prerequisites.Select(p => new PrerequisiteDefinitionModel
        {
            Type = p.Type,
            QuestCode = p.QuestCode,
            MinLevel = p.MinLevel,
            FactionCode = p.FactionCode,
            MinReputation = p.MinReputation,
            ItemCode = p.ItemCode,
            CurrencyCode = p.CurrencyCode,
            MinAmount = p.MinAmount
        }).ToList();
    }

    private static List<RewardDefinitionModel>? MapRewardDefinitions(ICollection<RewardDefinition>? rewards)
    {
        if (rewards == null) return null;

        return rewards.Select(r => new RewardDefinitionModel
        {
            Type = r.Type,
            CurrencyCode = r.CurrencyCode,
            Amount = r.Amount,
            ItemCode = r.ItemCode,
            Quantity = r.Quantity,
            FactionCode = r.FactionCode
        }).ToList();
    }

    private static QuestDefinitionResponse MapToDefinitionResponse(QuestDefinitionModel definition)
    {
        return new QuestDefinitionResponse
        {
            DefinitionId = definition.DefinitionId,
            ContractTemplateId = definition.ContractTemplateId,
            Code = definition.Code,
            Name = definition.Name,
            Description = definition.Description,
            Category = definition.Category,
            Difficulty = definition.Difficulty,
            LevelRequirement = definition.LevelRequirement,
            Repeatable = definition.Repeatable,
            CooldownSeconds = definition.CooldownSeconds,
            DeadlineSeconds = definition.DeadlineSeconds,
            MaxQuestors = definition.MaxQuestors,
            Objectives = definition.Objectives?.Select(o => new ObjectiveDefinition
            {
                Code = o.Code,
                Name = o.Name,
                Description = o.Description,
                ObjectiveType = o.ObjectiveType,
                RequiredCount = o.RequiredCount,
                TargetEntityType = o.TargetEntityType,
                TargetEntitySubtype = o.TargetEntitySubtype,
                TargetLocationId = o.TargetLocationId,
                Hidden = o.Hidden,
                RevealBehavior = o.RevealBehavior,
                Optional = o.Optional
            }).ToList() ?? new List<ObjectiveDefinition>(),
            Prerequisites = definition.Prerequisites?.Select(p => new PrerequisiteDefinition
            {
                Type = p.Type,
                QuestCode = p.QuestCode,
                MinLevel = p.MinLevel,
                FactionCode = p.FactionCode,
                MinReputation = p.MinReputation,
                ItemCode = p.ItemCode,
                CurrencyCode = p.CurrencyCode,
                MinAmount = p.MinAmount
            }).ToList(),
            Rewards = definition.Rewards?.Select(r => new RewardDefinition
            {
                Type = r.Type,
                CurrencyCode = r.CurrencyCode,
                Amount = r.Amount,
                ItemCode = r.ItemCode,
                Quantity = r.Quantity,
                FactionCode = r.FactionCode
            }).ToList(),
            Tags = definition.Tags,
            IsDeprecated = definition.IsDeprecated,
            DeprecatedAt = definition.DeprecatedAt,
            DeprecationReason = definition.DeprecationReason,
            CreatedAt = definition.CreatedAt,
            GameServiceId = definition.GameServiceId
        };
    }

    private async Task<QuestInstanceResponse> MapToInstanceResponseAsync(
        QuestInstanceModel instance,
        QuestDefinitionModel? definition,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.quest", "QuestService.MapToInstanceResponseAsync");
        var objectives = await GetObjectiveProgressListAsync(instance.QuestInstanceId, definition, cancellationToken);

        return new QuestInstanceResponse
        {
            QuestInstanceId = instance.QuestInstanceId,
            DefinitionId = instance.DefinitionId,
            ContractInstanceId = instance.ContractInstanceId,
            Code = instance.Code,
            Name = instance.Name,
            Status = instance.Status,
            QuestorCharacterIds = instance.QuestorCharacterIds,
            QuestGiverCharacterId = instance.QuestGiverCharacterId,
            Objectives = objectives,
            AcceptedAt = instance.AcceptedAt,
            Deadline = instance.Deadline,
            CompletedAt = instance.CompletedAt
        };
    }

    private static ObjectiveProgress MapToObjectiveProgress(ObjectiveProgressModel progress)
    {
        var progressPercent = progress.RequiredCount > 0
            ? (float)progress.CurrentCount / progress.RequiredCount * 100
            : 100f;

        return new ObjectiveProgress
        {
            Code = progress.ObjectiveCode,
            Name = progress.Name,
            Description = progress.Description,
            ObjectiveType = progress.ObjectiveType,
            CurrentCount = progress.CurrentCount,
            RequiredCount = progress.RequiredCount,
            IsComplete = progress.IsComplete,
            ProgressPercent = progressPercent,
            Hidden = progress.Hidden,
            Optional = progress.Optional
        };
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
            var characterIndex = await CharacterIndex.GetAsync(indexKey, cancellationToken);

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
                        var instances = await InstanceStore.QueryAsync(
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
                                var progress = await ProgressStore.GetAsync(progressKey, cancellationToken);
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
                        var defs = await DefinitionStore.QueryAsync(
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

    #region Character Index Helpers

    /// <summary>
    /// Updates the character quest index with ETag-based optimistic concurrency.
    /// Retries on concurrency conflicts per IMPLEMENTATION TENETS.
    /// </summary>
    /// <param name="characterId">The character whose index to update.</param>
    /// <param name="mutate">Action that modifies the index. Called on each retry with fresh data.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    private async Task UpdateCharacterIndexAsync(
        Guid characterId,
        Action<CharacterQuestIndex> mutate,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.quest", "QuestService.UpdateCharacterIndexAsync");
        var key = BuildCharacterIndexKey(characterId);

        for (var attempt = 0; attempt < _configuration.MaxConcurrencyRetries; attempt++)
        {
            var (current, etag) = await CharacterIndex.GetWithETagAsync(key, cancellationToken);
            current ??= new CharacterQuestIndex
            {
                CharacterId = characterId,
                ActiveQuestIds = new List<Guid>(),
                CompletedQuestCodes = new List<string>()
            };

            mutate(current);

            // GetWithETagAsync returns non-null etag for existing records;
            // coalesce satisfies compiler's nullable analysis (will never execute for existing records,
            // and for new records empty string signals a create)
            var saveResult = await CharacterIndex.TrySaveAsync(key, current, etag ?? string.Empty, cancellationToken: cancellationToken);
            if (saveResult != null)
                return;

            _logger.LogDebug(
                "Concurrent modification on character index {CharacterId}, retrying (attempt {Attempt})",
                characterId, attempt + 1);
        }

        _logger.LogWarning(
            "Failed to update character index for {CharacterId} after {MaxRetries} attempts",
            characterId, _configuration.MaxConcurrencyRetries);
    }

    #endregion
}
