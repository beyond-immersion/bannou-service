using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Character;
using BeyondImmersion.BannouService.Contract;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Quest.Caching;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("lib-quest.tests")]

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
/// <b>SERVICE HIERARCHY (L4 Game Features):</b>
/// <list type="bullet">
///   <item>Hard dependencies (constructor injection): IContractClient (L1), ICharacterClient (L2), IDistributedLockProvider (L0)</item>
///   <item>Soft dependencies (runtime resolution): IAnalyticsClient, IAchievementClient (L4)</item>
/// </list>
/// </para>
/// </remarks>
[BannouService("quest", typeof(IQuestService), lifetime: ServiceLifetime.Scoped, layer: ServiceLayer.GameFeatures)]
public partial class QuestService : IQuestService
{
    private readonly IMessageBus _messageBus;
    private readonly IStateStoreFactory _stateStoreFactory;
    private readonly ILogger<QuestService> _logger;
    private readonly QuestServiceConfiguration _configuration;
    private readonly IContractClient _contractClient;
    private readonly ICharacterClient _characterClient;
    private readonly IDistributedLockProvider _lockProvider;
    private readonly IServiceProvider _serviceProvider;
    private readonly IQuestDataCache _questDataCache;

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

    private IStateStore<IdempotencyRecord>? _idempotencyStore;
    private IStateStore<IdempotencyRecord> IdempotencyStore =>
        _idempotencyStore ??= _stateStoreFactory.GetStore<IdempotencyRecord>(StateStoreDefinitions.QuestIdempotency);

    #endregion

    #region Key Building

    private static string BuildDefinitionKey(Guid definitionId) => $"def:{definitionId}";
    private static string BuildDefinitionCodeKey(string code) => $"def:code:{code.ToUpperInvariant()}";
    private static string BuildInstanceKey(Guid instanceId) => $"inst:{instanceId}";
    private static string BuildProgressKey(Guid instanceId, string objectiveCode) => $"prog:{instanceId}:{objectiveCode}";
    private static string BuildCharacterIndexKey(Guid characterId) => $"char:{characterId}";
    private static string BuildCooldownKey(Guid characterId, string questCode) => $"cd:{characterId}:{questCode}";
    private static string BuildIdempotencyKey(string idempotencyKey) => $"idem:{idempotencyKey}";
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
    /// <param name="lockProvider">Distributed lock provider (L0 hard dependency).</param>
    /// <param name="eventConsumer">Event consumer for subscription registration.</param>
    /// <param name="serviceProvider">Service provider for L4 soft dependencies.</param>
    public QuestService(
        IMessageBus messageBus,
        IStateStoreFactory stateStoreFactory,
        ILogger<QuestService> logger,
        QuestServiceConfiguration configuration,
        IContractClient contractClient,
        ICharacterClient characterClient,
        IDistributedLockProvider lockProvider,
        IEventConsumer eventConsumer,
        IServiceProvider serviceProvider,
        IQuestDataCache questDataCache)
    {
        _messageBus = messageBus;
        _stateStoreFactory = stateStoreFactory;
        _logger = logger;
        _configuration = configuration;
        _contractClient = contractClient;
        _characterClient = characterClient;
        _lockProvider = lockProvider;
        _serviceProvider = serviceProvider;
        _questDataCache = questDataCache;

        // Register event handlers via partial class (QuestServiceEvents.cs)
        RegisterEventConsumers(eventConsumer);
    }

    #region Definition Endpoints

    /// <inheritdoc/>
    public async Task<(StatusCodes, QuestDefinitionResponse?)> CreateQuestDefinitionAsync(
        CreateQuestDefinitionRequest body,
        CancellationToken cancellationToken)
    {
        try
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
                Deprecated = false,
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
        catch (ApiException ex)
        {
            _logger.LogWarning(ex, "Dependency error creating quest definition: {Code}", body.Code);
            return (StatusCodes.ServiceUnavailable, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating quest definition: {Code}", body.Code);
            await _messageBus.TryPublishErrorAsync(
                "quest",
                "CreateQuestDefinition",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                endpoint: "post:/quest/definition/create",
                details: null,
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, QuestDefinitionResponse?)> GetQuestDefinitionAsync(
        GetQuestDefinitionRequest body,
        CancellationToken cancellationToken)
    {
        try
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting quest definition");
            await _messageBus.TryPublishErrorAsync(
                "quest",
                "GetQuestDefinition",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                endpoint: "post:/quest/definition/get",
                details: null,
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, ListQuestDefinitionsResponse?)> ListQuestDefinitionsAsync(
        ListQuestDefinitionsRequest body,
        CancellationToken cancellationToken)
    {
        try
        {
            var results = await DefinitionStore.QueryAsync(
                d => (body.GameServiceId == null || d.GameServiceId == body.GameServiceId) &&
                    (body.Category == null || d.Category == body.Category) &&
                    (body.Difficulty == null || d.Difficulty == body.Difficulty) &&
                    (body.IncludeDeprecated == true || !d.Deprecated),
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing quest definitions");
            await _messageBus.TryPublishErrorAsync(
                "quest",
                "ListQuestDefinitions",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                endpoint: "post:/quest/definition/list",
                details: null,
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, QuestDefinitionResponse?)> UpdateQuestDefinitionAsync(
        UpdateQuestDefinitionRequest body,
        CancellationToken cancellationToken)
    {
        try
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating quest definition: {DefinitionId}", body.DefinitionId);
            await _messageBus.TryPublishErrorAsync(
                "quest",
                "UpdateQuestDefinition",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                endpoint: "post:/quest/definition/update",
                details: null,
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, QuestDefinitionResponse?)> DeprecateQuestDefinitionAsync(
        DeprecateQuestDefinitionRequest body,
        CancellationToken cancellationToken)
    {
        try
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

                if (definition.Deprecated)
                {
                    // Already deprecated - idempotent success
                    return (StatusCodes.OK, MapToDefinitionResponse(definition));
                }

                definition.Deprecated = true;

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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deprecating quest definition: {DefinitionId}", body.DefinitionId);
            await _messageBus.TryPublishErrorAsync(
                "quest",
                "DeprecateQuestDefinition",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                endpoint: "post:/quest/definition/deprecate",
                details: null,
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    #endregion

    #region Instance Endpoints

    /// <inheritdoc/>
    public async Task<(StatusCodes, QuestInstanceResponse?)> AcceptQuestAsync(
        AcceptQuestRequest body,
        CancellationToken cancellationToken)
    {
        try
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
            if (definition.Deprecated)
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
                StateStoreDefinitions.QuestIdempotency,
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
            var questGiverId = body.QuestGiverCharacterId ?? definition.QuestGiverCharacterId ?? Guid.Empty;
            if (questGiverId != Guid.Empty)
            {
                parties.Add(new ContractPartyInput
                {
                    EntityId = questGiverId,
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

            // Update character index
            characterIndex ??= new CharacterQuestIndex
            {
                CharacterId = body.QuestorCharacterId,
                ActiveQuestIds = new List<Guid>(),
                CompletedQuestCodes = new List<string>()
            };
            characterIndex.ActiveQuestIds ??= new List<Guid>();
            characterIndex.ActiveQuestIds.Add(questInstanceId);

            await CharacterIndex.SaveAsync(characterIndexKey, characterIndex, cancellationToken: cancellationToken);

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
        catch (ApiException ex)
        {
            _logger.LogWarning(ex, "Dependency error accepting quest");
            return (StatusCodes.ServiceUnavailable, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error accepting quest");
            await _messageBus.TryPublishErrorAsync(
                "quest",
                "AcceptQuest",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                endpoint: "post:/quest/accept",
                details: null,
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, QuestInstanceResponse?)> AbandonQuestAsync(
        AbandonQuestRequest body,
        CancellationToken cancellationToken)
    {
        try
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

                // Update character index
                var characterIndexKey = BuildCharacterIndexKey(body.QuestorCharacterId);
                var characterIndex = await CharacterIndex.GetAsync(characterIndexKey, cancellationToken);
                if (characterIndex?.ActiveQuestIds != null)
                {
                    characterIndex.ActiveQuestIds.Remove(body.QuestInstanceId);
                    await CharacterIndex.SaveAsync(characterIndexKey, characterIndex, cancellationToken: cancellationToken);
                }

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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error abandoning quest: {QuestInstanceId}", body.QuestInstanceId);
            await _messageBus.TryPublishErrorAsync(
                "quest",
                "AbandonQuest",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                endpoint: "post:/quest/abandon",
                details: null,
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, QuestInstanceResponse?)> GetQuestAsync(
        GetQuestRequest body,
        CancellationToken cancellationToken)
    {
        try
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting quest: {QuestInstanceId}", body.QuestInstanceId);
            await _messageBus.TryPublishErrorAsync(
                "quest",
                "GetQuest",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                endpoint: "post:/quest/get",
                details: null,
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, ListQuestsResponse?)> ListQuestsAsync(
        ListQuestsRequest body,
        CancellationToken cancellationToken)
    {
        try
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing quests for character: {CharacterId}", body.CharacterId);
            await _messageBus.TryPublishErrorAsync(
                "quest",
                "ListQuests",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                endpoint: "post:/quest/list",
                details: null,
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, ListAvailableQuestsResponse?)> ListAvailableQuestsAsync(
        ListAvailableQuestsRequest body,
        CancellationToken cancellationToken)
    {
        try
        {
            // Get all non-deprecated definitions
            var definitions = await DefinitionStore.QueryAsync(
                d => !d.Deprecated &&
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
                if (!await CheckPrerequisitesAsync(definition, body.CharacterId, completedCodes, cancellationToken))
                    continue;

                available.Add(MapToDefinitionResponse(definition));
            }

            var response = new ListAvailableQuestsResponse
            {
                Available = available
            };

            return (StatusCodes.OK, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing available quests for character: {CharacterId}", body.CharacterId);
            await _messageBus.TryPublishErrorAsync(
                "quest",
                "ListAvailableQuests",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                endpoint: "post:/quest/list-available",
                details: null,
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, QuestLogResponse?)> GetQuestLogAsync(
        GetQuestLogRequest body,
        CancellationToken cancellationToken)
    {
        try
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting quest log for character: {CharacterId}", body.CharacterId);
            await _messageBus.TryPublishErrorAsync(
                "quest",
                "GetQuestLog",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                endpoint: "post:/quest/log",
                details: null,
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    #endregion

    #region Objective Endpoints

    /// <inheritdoc/>
    public async Task<(StatusCodes, ObjectiveProgressResponse?)> ReportObjectiveProgressAsync(
        ReportProgressRequest body,
        CancellationToken cancellationToken)
    {
        try
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reporting objective progress: {QuestInstanceId}/{ObjectiveCode}",
                body.QuestInstanceId, body.ObjectiveCode);
            await _messageBus.TryPublishErrorAsync(
                "quest",
                "ReportObjectiveProgress",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                endpoint: "post:/quest/objective/progress",
                details: null,
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, ObjectiveProgressResponse?)> ForceCompleteObjectiveAsync(
        ForceCompleteObjectiveRequest body,
        CancellationToken cancellationToken)
    {
        try
        {
            var instanceKey = BuildInstanceKey(body.QuestInstanceId);
            var instance = await InstanceStore.GetAsync(instanceKey, cancellationToken);

            if (instance == null)
            {
                return (StatusCodes.NotFound, null);
            }

            var progressKey = BuildProgressKey(body.QuestInstanceId, body.ObjectiveCode);
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

            await ProgressStore.SaveAsync(
                progressKey,
                progress,
                new StateOptions { Ttl = _configuration.ProgressCacheTtlSeconds },
                cancellationToken);

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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error force completing objective: {QuestInstanceId}/{ObjectiveCode}",
                body.QuestInstanceId, body.ObjectiveCode);
            await _messageBus.TryPublishErrorAsync(
                "quest",
                "ForceCompleteObjective",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                endpoint: "post:/quest/objective/complete",
                details: null,
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, ObjectiveProgressResponse?)> GetObjectiveProgressAsync(
        GetObjectiveProgressRequest body,
        CancellationToken cancellationToken)
    {
        try
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting objective progress: {QuestInstanceId}/{ObjectiveCode}",
                body.QuestInstanceId, body.ObjectiveCode);
            await _messageBus.TryPublishErrorAsync(
                "quest",
                "GetObjectiveProgress",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                endpoint: "post:/quest/objective/get",
                details: null,
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    #endregion

    #region Internal Callbacks

    /// <inheritdoc/>
    public async Task<StatusCodes> HandleMilestoneCompletedAsync(
        MilestoneCompletedCallback body,
        CancellationToken cancellationToken)
    {
        try
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling milestone completed callback for contract {ContractInstanceId}",
                body.ContractInstanceId);
            await _messageBus.TryPublishErrorAsync(
                "quest",
                "HandleMilestoneCompleted",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                endpoint: "post:/quest/internal/milestone-completed",
                details: null,
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return StatusCodes.InternalServerError;
        }
    }

    /// <inheritdoc/>
    public async Task<StatusCodes> HandleQuestCompletedAsync(
        QuestCompletedCallback body,
        CancellationToken cancellationToken)
    {
        try
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling quest completed callback for contract {ContractInstanceId}",
                body.ContractInstanceId);
            await _messageBus.TryPublishErrorAsync(
                "quest",
                "HandleQuestCompleted",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                endpoint: "post:/quest/internal/quest-completed",
                details: null,
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return StatusCodes.InternalServerError;
        }
    }

    #endregion

    #region Helper Methods

    private async Task<QuestDefinitionModel?> GetDefinitionModelAsync(Guid definitionId, CancellationToken cancellationToken)
    {
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
    /// </summary>
    /// <param name="definition">The quest definition to check prerequisites for.</param>
    /// <param name="characterId">The character ID to check.</param>
    /// <param name="completedCodes">List of quest codes the character has completed.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if all prerequisites are met, false otherwise.</returns>
    private async Task<bool> CheckPrerequisitesAsync(
        QuestDefinitionModel definition,
        Guid characterId,
        List<string> completedCodes,
        CancellationToken cancellationToken)
    {
        // No prerequisites = always available
        if (definition.Prerequisites == null || definition.Prerequisites.Count == 0)
            return true;

        foreach (var prereq in definition.Prerequisites)
        {
            switch (prereq.Type)
            {
                case PrerequisiteType.QUEST_COMPLETED:
                    // Check if character has completed the required quest
                    if (!string.IsNullOrWhiteSpace(prereq.QuestCode))
                    {
                        var normalizedCode = prereq.QuestCode.ToUpperInvariant();
                        if (!completedCodes.Contains(normalizedCode))
                        {
                            _logger.LogDebug("Character {CharacterId} has not completed prerequisite quest {QuestCode} for quest {DefinitionCode}",
                                characterId, prereq.QuestCode, definition.Code);
                            return false;
                        }
                    }
                    break;

                case PrerequisiteType.CHARACTER_LEVEL:
                    // Character service doesn't track level - skip with log
                    // Level tracking would require a separate progression/stats service
                    _logger.LogDebug("Skipping CHARACTER_LEVEL prerequisite for quest {DefinitionCode} - level tracking not implemented",
                        definition.Code);
                    break;

                case PrerequisiteType.REPUTATION:
                    // Reputation service doesn't exist yet - skip with log
                    _logger.LogDebug("Skipping REPUTATION prerequisite for quest {DefinitionCode} - reputation service not implemented",
                        definition.Code);
                    break;

                case PrerequisiteType.ITEM_OWNED:
                    // Would require complex inventory traversal via soft dependency
                    // Skipping for now - can be implemented when needed
                    _logger.LogDebug("Skipping ITEM_OWNED prerequisite for quest {DefinitionCode} - item ownership check not implemented",
                        definition.Code);
                    break;

                case PrerequisiteType.CURRENCY_AMOUNT:
                    // Would require wallet lookup + balance check via soft dependency
                    // Skipping for now - can be implemented when needed
                    _logger.LogDebug("Skipping CURRENCY_AMOUNT prerequisite for quest {DefinitionCode} - currency check not implemented",
                        definition.Code);
                    break;

                default:
                    _logger.LogWarning("Unknown prerequisite type {Type} for quest {DefinitionCode}",
                        prereq.Type, definition.Code);
                    break;
            }
        }

        // Await to maintain async signature for future implementations
        await Task.CompletedTask;
        return true;
    }

    private async Task<List<ObjectiveProgress>> GetObjectiveProgressListAsync(
        Guid questInstanceId,
        QuestDefinitionModel? definition,
        CancellationToken cancellationToken)
    {
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
        var instanceKey = BuildInstanceKey(instance.QuestInstanceId);

        for (var attempt = 0; attempt < _configuration.MaxConcurrencyRetries; attempt++)
        {
            var (current, etag) = await InstanceStore.GetWithETagAsync(instanceKey, cancellationToken);
            if (current == null || current.Status != QuestStatus.ACTIVE) return;

            var now = DateTimeOffset.UtcNow;
            current.Status = QuestStatus.COMPLETED;
            current.CompletedAt = now;

            var saveResult = await InstanceStore.TrySaveAsync(instanceKey, current, etag ?? string.Empty, cancellationToken: cancellationToken);
            if (saveResult == null) continue;

            // Update character indexes
            foreach (var characterId in current.QuestorCharacterIds)
            {
                var characterIndexKey = BuildCharacterIndexKey(characterId);
                var characterIndex = await CharacterIndex.GetAsync(characterIndexKey, cancellationToken);
                if (characterIndex != null)
                {
                    characterIndex.ActiveQuestIds?.Remove(instance.QuestInstanceId);
                    characterIndex.CompletedQuestCodes ??= new List<string>();
                    if (!characterIndex.CompletedQuestCodes.Contains(current.Code))
                    {
                        characterIndex.CompletedQuestCodes.Add(current.Code);
                    }
                    await CharacterIndex.SaveAsync(characterIndexKey, characterIndex, cancellationToken: cancellationToken);
                }

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
        // Build milestones from objectives - contract milestones map to quest objectives
        var milestones = body.Objectives.Select((obj, index) => new MilestoneDefinition
        {
            Code = obj.Code,
            Name = obj.Name,
            Description = obj.Description,
            Sequence = index
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
            RevealBehavior = o.RevealBehavior,
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
            Deprecated = definition.Deprecated,
            CreatedAt = definition.CreatedAt,
            GameServiceId = definition.GameServiceId
        };
    }

    private async Task<QuestInstanceResponse> MapToInstanceResponseAsync(
        QuestInstanceModel instance,
        QuestDefinitionModel? definition,
        CancellationToken cancellationToken)
    {
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
}
