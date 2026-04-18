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

// =============================================================================
// QuestService — Private & Internal Helper Methods
// =============================================================================
//
// This partial class file is the designated home for all private and internal
// helper methods used by QuestService. This separation exists to
// support the IMPLEMENTATION TENETS telemetry span rules:
//
//   - PRIMARY FILE (QuestService.cs):
//     Contains ONLY public interface method implementations (the methods
//     declared in IQuestService). These methods MUST NOT call
//     ITelemetryProvider.StartActivity because the generated controller
//     already wraps each endpoint with a telemetry span. Adding a span
//     in the service method would double-instrument the endpoint.
//
//   - THIS FILE (QuestService.Helpers.cs):
//     Contains all private/internal helper methods, core logic extracted
//     from endpoints, event publishing helpers, query builders, mapping
//     functions, and any other non-public methods. Every async method in
//     this file MUST call ITelemetryProvider.StartActivity to ensure
//     sub-operations are properly instrumented.
//
// Structural tests enforce both rules:
//   - Services_PrimaryFile_DoesNotCallStartActivity
//   - Services_HelperFiles_HaveStartActivityWhenAsync
//
// WHAT GOES HERE:
//   - Private async helper methods (with StartActivity spans)
//   - Private sync helper methods (query builders, mappers, validators)
//   - Internal static key builders (already in primary file by convention,
//     but may be moved here if the primary file is large)
//   - Event publishing helper methods
//   - Any extracted "core" logic (e.g., CreateAccountCoreAsync)
//
// WHAT STAYS IN THE PRIMARY FILE:
//   - Public interface method implementations (/// <inheritdoc/> methods)
//   - Constructor and field declarations
//   - Constants and key prefix definitions
//
// See: docs/reference/tenets/IMPLEMENTATION-BEHAVIOR.md (T30)
// See: docs/reference/HELPERS-AND-COMMON-PATTERNS.md
// =============================================================================

/// <summary>
/// Private and internal helper methods for QuestService.
/// </summary>
/// <remarks>
/// <para>
/// This partial class contains all non-public helper methods. Every async method
/// in this file MUST include a <c>using var activity = _telemetryProvider.StartActivity(...)</c>
/// span per IMPLEMENTATION TENETS (T30). The generated controller instruments the
/// public interface methods; this file instruments the sub-operations.
/// </para>
/// </remarks>
public partial class QuestService
{

    #region Helper Methods

    private async Task<QuestDefinitionModel?> GetDefinitionModelAsync(Guid definitionId, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.quest", "QuestService.GetDefinitionModelAsync");
        var cacheKey = BuildDefinitionKey(definitionId);
        var definition = await _definitionCache.GetAsync(cacheKey, cancellationToken);
        if (definition != null) return definition;

        var results = await _definitionStore.QueryAsync(
            d => d.DefinitionId == definitionId,
            cancellationToken: cancellationToken);
        definition = results.FirstOrDefault();

        if (definition != null)
        {
            await _definitionCache.SaveAsync(
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

        var failFast = _configuration.PrerequisiteValidationMode == PrerequisiteValidationMode.FailFast;

        foreach (var prereq in definition.Prerequisites)
        {
            PrerequisiteFailure? failure = null;

            switch (prereq.Type)
            {
                case PrerequisiteType.QuestCompleted:
                    failure = CheckQuestCompletedPrerequisite(prereq, completedCodes, definition.Code, characterId);
                    break;

                case PrerequisiteType.CurrencyAmount:
                    failure = await CheckCurrencyPrerequisiteAsync(prereq, characterId, cancellationToken);
                    break;

                case PrerequisiteType.ItemOwned:
                    failure = await CheckItemPrerequisiteAsync(prereq, characterId, cancellationToken);
                    break;

                case PrerequisiteType.CharacterLevel:
                    // Dynamic: Check via IPrerequisiteProviderFactory (no built-in L2 level tracking)
                    failure = await CheckDynamicPrerequisiteAsync("character_level", prereq, characterId, cancellationToken);
                    break;

                case PrerequisiteType.Reputation:
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
            Type = PrerequisiteType.QuestCompleted,
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
                    Type = PrerequisiteType.CurrencyAmount,
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
            catch (ApiException ex)
            {
                await _messageBus.TryPublishErrorAsync(
                    "quest",
                    "CheckCurrencyPrerequisite",
                    "GetOrCreateWalletFailed",
                    ex.Message,
                    dependency: "currency",
                    endpoint: "get-or-create-wallet",
                    stack: ex.StackTrace,
                    cancellationToken: cancellationToken);
                return new PrerequisiteFailure
                {
                    Type = PrerequisiteType.CurrencyAmount,
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
                    Type = PrerequisiteType.CurrencyAmount,
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
            catch (ApiException ex)
            {
                await _messageBus.TryPublishErrorAsync(
                    "quest",
                    "CheckCurrencyPrerequisite",
                    "GetBalanceFailed",
                    ex.Message,
                    dependency: "currency",
                    endpoint: "get-balance",
                    stack: ex.StackTrace,
                    cancellationToken: cancellationToken);
                return new PrerequisiteFailure
                {
                    Type = PrerequisiteType.CurrencyAmount,
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
                Type = PrerequisiteType.CurrencyAmount,
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
                Type = PrerequisiteType.CurrencyAmount,
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
                    Type = PrerequisiteType.ItemOwned,
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
                    Type = PrerequisiteType.ItemOwned,
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
                Type = PrerequisiteType.ItemOwned,
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
                Type = PrerequisiteType.ItemOwned,
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
            var progress = await _progressStore.GetAsync(progressKey, cancellationToken);
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
            var (current, etag) = await _instanceStore.GetWithETagAsync(instanceKey, cancellationToken);
            if (current == null || current.Status != QuestStatus.Active) return;

            var now = DateTimeOffset.UtcNow;
            current.Status = QuestStatus.Completed;
            current.CompletedAt = now;

            // GetWithETagAsync returns non-null etag for existing records;
            // coalesce satisfies compiler's nullable analysis (will never execute)
            var saveResult = await _instanceStore.TrySaveAsync(instanceKey, current, etag ?? string.Empty, cancellationToken: cancellationToken);
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
                    await _cooldownStore.SaveAsync(
                        cooldownKey,
                        cooldown,
                        new StateOptions { Ttl = definition.CooldownSeconds.Value },
                        cancellationToken);
                }
            }

            // Publish completed event (action event)
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
            await _messageBus.PublishQuestCompletedAsync(completedEvent, cancellationToken);

            // Publish lifecycle updated event with changedFields
            await _messageBus.PublishQuestInstanceUpdatedAsync(new QuestInstanceUpdatedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = now,
                QuestInstanceId = instance.QuestInstanceId,
                DefinitionId = current.DefinitionId,
                ContractInstanceId = current.ContractInstanceId,
                Code = current.Code,
                Name = current.Name,
                Status = QuestStatus.Completed,
                QuestGiverCharacterId = current.QuestGiverCharacterId,
                GameServiceId = current.GameServiceId,
                AcceptedAt = current.AcceptedAt,
                CompletedAt = now,
                Deadline = current.Deadline,
                CreatedAt = current.AcceptedAt,
                UpdatedAt = now,
                ChangedFields = new List<string> { "status", "completedAt" }
            }, cancellationToken);

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
                case RewardType.Currency:
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

                case RewardType.Item:
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

                case RewardType.Experience:
                    // Stub: Experience system not implemented
                    _logger.LogWarning(
                        "EXPERIENCE reward for quest {QuestCode} skipped: experience system not implemented",
                        questCode);
                    break;

                case RewardType.Reputation:
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
        var hasCurrencyRewards = definition.Rewards?.Any(r => r.Type == RewardType.Currency) ?? false;
        var hasItemRewards = definition.Rewards?.Any(r => r.Type == RewardType.Item) ?? false;

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
            RevealBehavior = o.RevealBehavior ?? ObjectiveRevealBehavior.Always,
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
            var (current, etag) = await _characterIndex.GetWithETagAsync(key, cancellationToken);
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
            var saveResult = await _characterIndex.TrySaveAsync(key, current, etag ?? string.Empty, cancellationToken: cancellationToken);
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
    /// <summary>
    /// Deletes a quest instance record and all associated data (progress, indexes, reverse index).
    /// Publishes quest.instance.deleted lifecycle event.
    /// </summary>
    private async Task DeleteInstanceRecordAsync(QuestInstanceModel instance, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.quest", "QuestService.DeleteInstanceRecordAsync");

        var instanceKey = BuildInstanceKey(instance.QuestInstanceId);

        // Delete objective progress records
        var definition = await GetDefinitionModelAsync(instance.DefinitionId, cancellationToken);
        if (definition?.Objectives != null)
        {
            foreach (var objective in definition.Objectives)
            {
                var progressKey = BuildProgressKey(instance.QuestInstanceId, objective.Code);
                await _progressStore.DeleteAsync(progressKey, cancellationToken);
            }
        }

        // Remove from character indexes
        foreach (var characterId in instance.QuestorCharacterIds)
        {
            await UpdateCharacterIndexAsync(characterId, idx =>
            {
                idx.ActiveQuestIds?.Remove(instance.QuestInstanceId);
            }, cancellationToken);
        }

        // Remove from definition→instance reverse index
        await _instanceStringStore.RemoveFromStringListAsync(
            BuildDefinitionInstanceIndexKey(instance.DefinitionId),
            instance.QuestInstanceId.ToString(),
            _configuration.MaxConcurrencyRetries,
            _logger,
            cancellationToken);

        // Delete the instance record
        await _instanceStore.DeleteAsync(instanceKey, cancellationToken);

        // Publish lifecycle deleted event
        var now = DateTimeOffset.UtcNow;
        await _messageBus.PublishQuestInstanceDeletedAsync(new QuestInstanceDeletedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = now,
            QuestInstanceId = instance.QuestInstanceId,
            DefinitionId = instance.DefinitionId,
            ContractInstanceId = instance.ContractInstanceId,
            Code = instance.Code,
            Name = instance.Name,
            Status = instance.Status,
            QuestGiverCharacterId = instance.QuestGiverCharacterId,
            GameServiceId = instance.GameServiceId,
            AcceptedAt = instance.AcceptedAt,
            CompletedAt = instance.CompletedAt,
            Deadline = instance.Deadline,
            CreatedAt = instance.AcceptedAt,
            UpdatedAt = now
        }, cancellationToken);

        _logger.LogInformation("Quest instance deleted: {QuestInstanceId} ({Code})",
            instance.QuestInstanceId, instance.Code);
    }
}
