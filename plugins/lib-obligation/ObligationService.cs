using System.Text;
using System.Text.Json;
using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Contract;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Providers;
using BeyondImmersion.BannouService.Resource;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Obligation;

/// <summary>
/// Implementation of the Obligation service providing contract-aware obligation tracking for NPC cognition.
/// </summary>
/// <remarks>
/// <para>
/// Obligation bridges the Contract service's behavioral clauses and the GOAP planner's action cost system,
/// enabling NPCs to have "second thoughts" before violating their obligations.
/// </para>
/// <para>
/// <b>Two-Layer Design:</b> Works standalone with raw contract penalties. When personality data is available
/// (soft L4 dependency via character-personality's variable provider), obligation costs are enriched with
/// trait-weighted moral reasoning. Without personality enrichment, costs are unweighted base penalties.
/// </para>
/// </remarks>
[BannouService("obligation", typeof(IObligationService), lifetime: ServiceLifetime.Scoped, layer: ServiceLayer.GameFeatures)]
public partial class ObligationService : IObligationService
{
    private readonly IMessageBus _messageBus;
    private readonly IDistributedLockProvider _lockProvider;
    private readonly IContractClient _contractClient;
    private readonly IResourceClient _resourceClient;
    private readonly ILogger<ObligationService> _logger;
    private readonly ObligationServiceConfiguration _configuration;
    private readonly IEnumerable<IVariableProviderFactory> _providerFactories;

    private readonly IStateStore<ObligationManifestModel> _cacheStore;
    private readonly IStateStore<ActionMappingModel> _actionMappingStore;
    private readonly IStateStore<ViolationRecordModel> _violationStore;
    private readonly IStateStore<IdempotencyEntry> _idempotencyStore;
    private readonly IJsonQueryableStateStore<ActionMappingModel> _actionMappingJsonStore;
    private readonly IJsonQueryableStateStore<ViolationRecordModel> _violationJsonStore;

    /// <summary>
    /// Creates a new instance of the ObligationService.
    /// </summary>
    public ObligationService(
        IStateStoreFactory stateStoreFactory,
        IMessageBus messageBus,
        IDistributedLockProvider lockProvider,
        IContractClient contractClient,
        IResourceClient resourceClient,
        ILogger<ObligationService> logger,
        ObligationServiceConfiguration configuration,
        IEventConsumer eventConsumer,
        IEnumerable<IVariableProviderFactory> providerFactories)
    {
        ArgumentNullException.ThrowIfNull(stateStoreFactory, nameof(stateStoreFactory));
        ArgumentNullException.ThrowIfNull(messageBus, nameof(messageBus));
        ArgumentNullException.ThrowIfNull(lockProvider, nameof(lockProvider));
        ArgumentNullException.ThrowIfNull(contractClient, nameof(contractClient));
        ArgumentNullException.ThrowIfNull(resourceClient, nameof(resourceClient));
        ArgumentNullException.ThrowIfNull(logger, nameof(logger));
        ArgumentNullException.ThrowIfNull(configuration, nameof(configuration));
        ArgumentNullException.ThrowIfNull(eventConsumer, nameof(eventConsumer));

        _messageBus = messageBus;
        _lockProvider = lockProvider;
        _contractClient = contractClient;
        _resourceClient = resourceClient;
        _logger = logger;
        _configuration = configuration;
        _providerFactories = providerFactories;

        _cacheStore = stateStoreFactory.GetStore<ObligationManifestModel>(StateStoreDefinitions.ObligationCache);
        _actionMappingStore = stateStoreFactory.GetStore<ActionMappingModel>(StateStoreDefinitions.ObligationActionMappings);
        _violationStore = stateStoreFactory.GetStore<ViolationRecordModel>(StateStoreDefinitions.ObligationViolations);
        _idempotencyStore = stateStoreFactory.GetStore<IdempotencyEntry>(StateStoreDefinitions.ObligationIdempotency);
        _actionMappingJsonStore = stateStoreFactory.GetJsonQueryableStore<ActionMappingModel>(StateStoreDefinitions.ObligationActionMappings);
        _violationJsonStore = stateStoreFactory.GetJsonQueryableStore<ViolationRecordModel>(StateStoreDefinitions.ObligationViolations);

        RegisterEventConsumers(eventConsumer);
    }

    // ========================================================================
    // Action Tag Mapping Endpoints
    // ========================================================================

    /// <summary>
    /// Idempotent upsert of GOAP action tag to violation type code mapping.
    /// </summary>
    public async Task<(StatusCodes, ActionMappingResponse?)> SetActionMappingAsync(
        SetActionMappingRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Setting action mapping for tag {Tag}", body.Tag);

        var key = $"mapping:{body.Tag}";
        var existing = await _actionMappingStore.GetAsync(key, cancellationToken);
        var now = DateTimeOffset.UtcNow;

        var model = new ActionMappingModel
        {
            Tag = body.Tag,
            ViolationTypes = body.ViolationTypes.ToList(),
            Description = body.Description,
            CreatedAt = existing?.CreatedAt ?? now,
            UpdatedAt = existing != null ? now : null
        };

        await _actionMappingStore.SaveAsync(key, model, cancellationToken: cancellationToken);

        _logger.LogInformation("Action mapping set for tag {Tag} with {Count} violation types",
            body.Tag, model.ViolationTypes.Count);

        return (StatusCodes.OK, new ActionMappingResponse
        {
            Tag = model.Tag,
            ViolationTypes = model.ViolationTypes,
            Description = model.Description,
            CreatedAt = model.CreatedAt,
            UpdatedAt = model.UpdatedAt
        });
    }

    /// <summary>
    /// Lists action mappings with cursor-based pagination and optional text search.
    /// </summary>
    public async Task<(StatusCodes, ListActionMappingsResponse?)> ListActionMappingsAsync(
        ListActionMappingsRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Listing action mappings, search={SearchTerm}", body.SearchTerm);

        var pageSize = body.PageSize ?? _configuration.DefaultPageSize;
        var offset = DecodeCursor(body.Cursor);

        var conditions = new List<QueryCondition>
        {
            // Type discriminator: only action mapping records
            new("$.Tag", QueryOperator.Exists, null)
        };

        if (!string.IsNullOrEmpty(body.SearchTerm))
        {
            conditions.Add(new QueryCondition("$.Tag", QueryOperator.Contains, body.SearchTerm));
        }

        var result = await _actionMappingJsonStore.JsonQueryPagedAsync(
            conditions, offset, pageSize + 1, cancellationToken: cancellationToken);

        var mappings = result.Items
            .Take(pageSize)
            .Select(r => new ActionMappingResponse
            {
                Tag = r.Value.Tag,
                ViolationTypes = r.Value.ViolationTypes,
                Description = r.Value.Description,
                CreatedAt = r.Value.CreatedAt,
                UpdatedAt = r.Value.UpdatedAt
            })
            .ToList();

        var hasMore = result.Items.Count > pageSize;

        return (StatusCodes.OK, new ListActionMappingsResponse
        {
            Mappings = mappings,
            NextCursor = hasMore ? EncodeCursor(offset + pageSize) : null,
            HasMore = hasMore
        });
    }

    /// <summary>
    /// Deletes an action mapping by tag. The tag falls back to 1:1 convention matching.
    /// </summary>
    public async Task<StatusCodes> DeleteActionMappingAsync(
        DeleteActionMappingRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Deleting action mapping for tag {Tag}", body.Tag);

        var key = $"mapping:{body.Tag}";
        var existing = await _actionMappingStore.GetAsync(key, cancellationToken);

        if (existing == null)
        {
            _logger.LogDebug("Action mapping not found for tag {Tag}", body.Tag);
            return StatusCodes.NotFound;
        }

        await _actionMappingStore.DeleteAsync(key, cancellationToken);
        _logger.LogInformation("Action mapping deleted for tag {Tag}", body.Tag);

        return StatusCodes.NoContent;
    }

    // ========================================================================
    // Obligation Query Endpoints
    // ========================================================================

    /// <summary>
    /// Returns all active obligations for a character derived from active contracts with behavioral clauses.
    /// Cache-backed with event-driven invalidation; forceRefresh bypasses cache.
    /// </summary>
    public async Task<(StatusCodes, QueryObligationsResponse?)> QueryObligationsAsync(
        QueryObligationsRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Querying obligations for character {CharacterId}, forceRefresh={ForceRefresh}",
            body.CharacterId, body.ForceRefresh);

        ObligationManifestModel? manifest = null;

        if (!body.ForceRefresh)
        {
            manifest = await _cacheStore.GetAsync(body.CharacterId.ToString(), cancellationToken);
        }

        manifest ??= await RebuildObligationCacheAsync(body.CharacterId, "query", cancellationToken);

        return (StatusCodes.OK, new QueryObligationsResponse
        {
            CharacterId = body.CharacterId,
            Obligations = manifest.Obligations.Select(MapToObligationEntry).ToList(),
            ViolationCostMap = manifest.ViolationCostMap,
            TotalActiveContracts = manifest.TotalActiveContracts,
            TotalObligations = manifest.Obligations.Count,
            LastRefreshedAt = manifest.LastRefreshedAt
        });
    }

    /// <summary>
    /// Non-mutating speculative query. Given action tags, resolves to violation types,
    /// matches against character's obligations, and returns per-action cost breakdowns.
    /// When personality enrichment is available, costs are weighted by traits.
    /// </summary>
    public async Task<(StatusCodes, EvaluateActionResponse?)> EvaluateActionAsync(
        EvaluateActionRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Evaluating {Count} actions for character {CharacterId}",
            body.ActionTags.Count, body.CharacterId);

        // Get obligations (cache-first)
        var manifest = await _cacheStore.GetAsync(body.CharacterId.ToString(), cancellationToken)
            ?? await RebuildObligationCacheAsync(body.CharacterId, "evaluate", cancellationToken);

        // Try to get personality data for moral weighting (soft L4 dependency)
        var personalityTraits = await TryGetPersonalityTraitsAsync(body.CharacterId, cancellationToken);
        var moralWeightingApplied = personalityTraits != null;

        var evaluations = new List<ActionEvaluation>();

        foreach (var actionTag in body.ActionTags)
        {
            var violationTypes = await ResolveViolationTypesAsync(actionTag, cancellationToken);

            var obligationDetails = new List<ObligationCostDetail>();
            var totalCost = 0f;
            var isViolation = false;

            foreach (var violationType in violationTypes)
            {
                var matchingObligations = manifest.Obligations
                    .Where(o => o.ViolationType.Equals(violationType, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                foreach (var obligation in matchingObligations)
                {
                    isViolation = true;
                    var basePenalty = obligation.BasePenalty;
                    var weightedPenalty = basePenalty;

                    if (personalityTraits != null)
                    {
                        var personalityWeight = ComputePersonalityWeight(violationType, personalityTraits);
                        weightedPenalty = basePenalty * personalityWeight;
                    }

                    totalCost += weightedPenalty;
                    obligationDetails.Add(new ObligationCostDetail
                    {
                        ContractId = obligation.ContractId,
                        TemplateCode = obligation.TemplateCode,
                        ClauseCode = obligation.ClauseCode,
                        ViolationType = obligation.ViolationType,
                        BasePenalty = basePenalty,
                        WeightedPenalty = weightedPenalty,
                        ContractRole = obligation.ContractRole
                    });
                }
            }

            evaluations.Add(new ActionEvaluation
            {
                ActionTag = actionTag,
                IsViolation = isViolation,
                TotalViolationCost = totalCost,
                ObligationDetails = obligationDetails
            });
        }

        return (StatusCodes.OK, new EvaluateActionResponse
        {
            CharacterId = body.CharacterId,
            Evaluations = evaluations,
            MoralWeightingApplied = moralWeightingApplied
        });
    }

    // ========================================================================
    // Violation Management Endpoints
    // ========================================================================

    /// <summary>
    /// Records a knowing violation, optionally reports breach to lib-contract,
    /// and publishes obligation.violation.reported event. Idempotent via idempotencyKey.
    /// </summary>
    public async Task<(StatusCodes, ReportViolationResponse?)> ReportViolationAsync(
        ReportViolationRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Reporting violation for character {CharacterId} on contract {ContractId}",
            body.CharacterId, body.ContractId);

        // Idempotency check
        if (!string.IsNullOrEmpty(body.IdempotencyKey))
        {
            var existingEntry = await _idempotencyStore.GetAsync(body.IdempotencyKey, cancellationToken);
            if (existingEntry != null)
            {
                _logger.LogDebug("Duplicate violation report for idempotency key {Key}", body.IdempotencyKey);
                var existingViolation = await _violationStore.GetAsync(
                    $"violation:{existingEntry.ViolationId}", cancellationToken);

                if (existingViolation != null)
                {
                    return (StatusCodes.OK, new ReportViolationResponse
                    {
                        ViolationId = existingViolation.ViolationId,
                        CharacterId = existingViolation.CharacterId,
                        BreachReported = existingViolation.BreachReported,
                        BreachId = existingViolation.BreachId
                    });
                }
            }
        }

        var violationId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        // Optionally report breach to contract service
        Guid? breachId = null;
        var breachReported = false;

        if (_configuration.BreachReportEnabled)
        {
            try
            {
                var breachResponse = await _contractClient.ReportBreachAsync(new ReportBreachRequest
                {
                    ContractId = body.ContractId,
                    BreachingEntityId = body.CharacterId,
                    BreachingEntityType = EntityType.Character,
                    BreachType = BreachType.Term_violation,
                    BreachedTermOrMilestone = body.ClauseCode,
                    Description = $"Knowing violation of {body.ViolationType} obligation (action: {body.ActionTag})"
                }, cancellationToken);

                breachId = breachResponse.BreachId;
                breachReported = true;
            }
            catch (ApiException ex)
            {
                _logger.LogWarning(ex,
                    "Failed to report breach to contract service for contract {ContractId}, status {Status}",
                    body.ContractId, ex.StatusCode);
            }
        }

        // Store violation record
        var violation = new ViolationRecordModel
        {
            ViolationId = violationId,
            CharacterId = body.CharacterId,
            ContractId = body.ContractId,
            ClauseCode = body.ClauseCode,
            ViolationType = body.ViolationType,
            ActionTag = body.ActionTag,
            MotivationScore = body.MotivationScore,
            ViolationCost = body.ViolationCost,
            BreachReported = breachReported,
            BreachId = breachId,
            TargetEntityId = body.TargetEntityId,
            TargetEntityType = body.TargetEntityType,
            Timestamp = now
        };

        await _violationStore.SaveAsync($"violation:{violationId}", violation, cancellationToken: cancellationToken);

        // Store idempotency key with TTL
        if (!string.IsNullOrEmpty(body.IdempotencyKey))
        {
            await _idempotencyStore.SaveAsync(
                body.IdempotencyKey,
                new IdempotencyEntry { ViolationId = violationId },
                new StateOptions { Ttl = TimeSpan.FromSeconds(_configuration.IdempotencyTtlSeconds) },
                cancellationToken);
        }

        // Look up template code and role from cached obligations for the event
        var manifest = await _cacheStore.GetAsync(body.CharacterId.ToString(), cancellationToken);
        var matchingObligation = manifest?.Obligations
            .FirstOrDefault(o => o.ContractId == body.ContractId
                && o.ClauseCode.Equals(body.ClauseCode, StringComparison.OrdinalIgnoreCase));

        // Publish violation event for downstream consumers (personality drift, encounter memory)
        await _messageBus.TryPublishAsync("obligation.violation.reported", new ObligationViolationReportedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = now,
            ViolationId = violationId,
            CharacterId = body.CharacterId,
            ContractId = body.ContractId,
            TemplateCode = matchingObligation?.TemplateCode ?? "unknown",
            ClauseCode = body.ClauseCode,
            ViolationType = body.ViolationType,
            ActionTag = body.ActionTag,
            MotivationScore = body.MotivationScore,
            ViolationCost = body.ViolationCost,
            BreachReported = breachReported,
            BreachId = breachId,
            TargetEntityId = body.TargetEntityId,
            TargetEntityType = body.TargetEntityType,
            ContractRole = matchingObligation?.ContractRole ?? "unknown"
        }, cancellationToken: cancellationToken);

        _logger.LogInformation(
            "Violation {ViolationId} recorded for character {CharacterId}, breachReported={BreachReported}",
            violationId, body.CharacterId, breachReported);

        return (StatusCodes.OK, new ReportViolationResponse
        {
            ViolationId = violationId,
            CharacterId = body.CharacterId,
            BreachReported = breachReported,
            BreachId = breachId
        });
    }

    /// <summary>
    /// Cursor-paginated violation history. Filters by contract, violation type, and time range.
    /// </summary>
    public async Task<(StatusCodes, QueryViolationsResponse?)> QueryViolationsAsync(
        QueryViolationsRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Querying violations for character {CharacterId}", body.CharacterId);

        var pageSize = body.PageSize ?? _configuration.DefaultPageSize;
        var offset = DecodeCursor(body.Cursor);

        var conditions = new List<QueryCondition>
        {
            new("$.CharacterId", QueryOperator.Equals, body.CharacterId.ToString())
        };

        if (body.ContractId.HasValue)
        {
            conditions.Add(new QueryCondition("$.ContractId", QueryOperator.Equals,
                body.ContractId.Value.ToString()));
        }

        if (!string.IsNullOrEmpty(body.ViolationType))
        {
            conditions.Add(new QueryCondition("$.ViolationType", QueryOperator.Equals, body.ViolationType));
        }

        if (body.Since.HasValue)
        {
            conditions.Add(new QueryCondition("$.Timestamp", QueryOperator.GreaterThanOrEqual,
                body.Since.Value.ToString("O")));
        }

        var result = await _violationJsonStore.JsonQueryPagedAsync(
            conditions, offset, pageSize + 1, cancellationToken: cancellationToken);

        var violations = result.Items
            .Take(pageSize)
            .Select(r => MapToViolationRecord(r.Value))
            .ToList();

        var hasMore = result.Items.Count > pageSize;

        return (StatusCodes.OK, new QueryViolationsResponse
        {
            Violations = violations,
            NextCursor = hasMore ? EncodeCursor(offset + pageSize) : null,
            HasMore = hasMore
        });
    }

    // ========================================================================
    // Cache Management
    // ========================================================================

    /// <summary>
    /// Administrative endpoint to force a full cache rebuild from current contract state.
    /// </summary>
    public async Task<(StatusCodes, InvalidateCacheResponse?)> InvalidateCacheAsync(
        InvalidateCacheRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Invalidating cache for character {CharacterId}", body.CharacterId);

        var manifest = await RebuildObligationCacheAsync(body.CharacterId, "manual", cancellationToken);

        return (StatusCodes.OK, new InvalidateCacheResponse
        {
            CharacterId = body.CharacterId,
            ObligationsRefreshed = manifest.Obligations.Count,
            Success = true
        });
    }

    // ========================================================================
    // Resource Cleanup (called by lib-resource during character deletion)
    // ========================================================================

    /// <summary>
    /// Removes cached obligation manifests and violation history for a character.
    /// Called by lib-resource during cascading character deletion via x-references.
    /// </summary>
    public async Task<(StatusCodes, CleanupByCharacterResponse?)> CleanupByCharacterAsync(
        CleanupByCharacterRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Cleaning up obligation data for character {CharacterId}", body.CharacterId);

        // Remove cached obligations
        await _cacheStore.DeleteAsync(body.CharacterId.ToString(), cancellationToken);

        // Query and remove violations
        var violationConditions = new List<QueryCondition>
        {
            new("$.CharacterId", QueryOperator.Equals, body.CharacterId.ToString())
        };

        var violations = await _violationJsonStore.JsonQueryPagedAsync(
            violationConditions, 0, 10000, cancellationToken: cancellationToken);

        var violationsRemoved = 0;
        foreach (var violation in violations.Items)
        {
            await _violationStore.DeleteAsync($"violation:{violation.Value.ViolationId}", cancellationToken);
            violationsRemoved++;
        }

        _logger.LogInformation(
            "Cleaned up obligation data for character {CharacterId}: {ViolationsRemoved} violations removed",
            body.CharacterId, violationsRemoved);

        return (StatusCodes.OK, new CleanupByCharacterResponse
        {
            ObligationsRemoved = 1,
            ViolationsRemoved = violationsRemoved,
            Success = true
        });
    }

    // ========================================================================
    // Compression (called by Resource service during character archival)
    // ========================================================================

    /// <summary>
    /// Returns violation history for archival as ObligationArchive.
    /// Obligation cache is rebuilt automatically from active contracts, not from archive.
    /// </summary>
    public async Task<(StatusCodes, ObligationArchive?)> GetCompressDataAsync(
        GetCompressDataRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Getting compress data for character {CharacterId}", body.CharacterId);

        var conditions = new List<QueryCondition>
        {
            new("$.CharacterId", QueryOperator.Equals, body.CharacterId.ToString())
        };

        var result = await _violationJsonStore.JsonQueryPagedAsync(
            conditions, 0, 10000, cancellationToken: cancellationToken);

        var violations = result.Items.Select(r => MapToViolationRecord(r.Value)).ToList();

        return (StatusCodes.OK, new ObligationArchive
        {
            ResourceId = body.CharacterId,
            ResourceType = "character",
            ArchivedAt = DateTimeOffset.UtcNow,
            SchemaVersion = 1,
            CharacterId = body.CharacterId,
            HasViolations = violations.Count > 0,
            ViolationCount = violations.Count,
            Violations = violations.Count > 0 ? violations : null
        });
    }

    /// <summary>
    /// Restores violation history from archive data.
    /// </summary>
    public async Task<(StatusCodes, RestoreFromArchiveResponse?)> RestoreFromArchiveAsync(
        RestoreFromArchiveRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Restoring archive for character {CharacterId}", body.CharacterId);

        var archive = BannouJson.Deserialize<ObligationArchive>(body.Data);
        if (archive == null)
        {
            _logger.LogDebug("Failed to deserialize archive data for character {CharacterId}", body.CharacterId);
            return (StatusCodes.BadRequest, null);
        }

        var violationsRestored = false;
        if (archive.Violations != null)
        {
            foreach (var violation in archive.Violations)
            {
                var model = new ViolationRecordModel
                {
                    ViolationId = violation.ViolationId,
                    CharacterId = violation.CharacterId,
                    ContractId = violation.ContractId,
                    ClauseCode = violation.ClauseCode,
                    ViolationType = violation.ViolationType,
                    ActionTag = violation.ActionTag,
                    MotivationScore = violation.MotivationScore,
                    ViolationCost = violation.ViolationCost,
                    BreachReported = violation.BreachReported,
                    BreachId = violation.BreachId,
                    TargetEntityId = violation.TargetEntityId,
                    TargetEntityType = violation.TargetEntityType,
                    Timestamp = violation.Timestamp
                };

                await _violationStore.SaveAsync(
                    $"violation:{violation.ViolationId}", model, cancellationToken: cancellationToken);
            }

            violationsRestored = true;
        }

        _logger.LogInformation("Restored archive for character {CharacterId}, violations={ViolationsRestored}",
            body.CharacterId, violationsRestored);

        return (StatusCodes.OK, new RestoreFromArchiveResponse
        {
            CharacterId = body.CharacterId,
            ViolationsRestored = violationsRestored,
            Success = true
        });
    }

    // ========================================================================
    // Internal: Cache Rebuild
    // ========================================================================

    /// <summary>
    /// Rebuilds the obligation cache for a character from active contracts.
    /// Acquires a distributed lock to serialize concurrent rebuilds.
    /// </summary>
    internal async Task<ObligationManifestModel> RebuildObligationCacheAsync(
        Guid characterId, string trigger, CancellationToken ct)
    {
        await using var lockResponse = await _lockProvider.LockAsync(
            resourceId: $"cache:{characterId}",
            lockOwner: Guid.NewGuid().ToString(),
            expiryInSeconds: _configuration.LockTimeoutSeconds,
            cancellationToken: ct);

        if (!lockResponse.Success)
        {
            _logger.LogWarning("Failed to acquire lock for cache rebuild of character {CharacterId}", characterId);
            // Return empty manifest rather than failing the request
            return new ObligationManifestModel
            {
                CharacterId = characterId,
                LastRefreshedAt = DateTimeOffset.UtcNow
            };
        }

        // Check cache again under lock (another instance may have rebuilt it)
        var cached = await _cacheStore.GetAsync(characterId.ToString(), ct);
        if (cached != null && trigger != "manual")
        {
            return cached;
        }

        // Query active contracts for this character
        QueryContractInstancesResponse contractsResponse;
        try
        {
            contractsResponse = await _contractClient.QueryContractInstancesAsync(
                new QueryContractInstancesRequest
                {
                    PartyEntityId = characterId,
                    PartyEntityType = EntityType.Character,
                    Statuses = new[] { ContractStatus.Active },
                    PageSize = _configuration.MaxActiveContractsQuery
                }, ct);
        }
        catch (ApiException ex)
        {
            _logger.LogWarning(ex,
                "Failed to query active contracts for character {CharacterId}, status {Status}",
                characterId, ex.StatusCode);
            return new ObligationManifestModel
            {
                CharacterId = characterId,
                LastRefreshedAt = DateTimeOffset.UtcNow
            };
        }

        var obligations = new List<ObligationEntryModel>();
        var contractCount = 0;

        foreach (var contract in contractsResponse.Contracts)
        {
            var clauses = ExtractBehavioralClauses(contract.Terms);
            if (clauses.Count == 0) continue;

            contractCount++;
            var party = contract.Parties.FirstOrDefault(p => p.EntityId == characterId);
            var role = party?.Role ?? "unknown";

            foreach (var clause in clauses)
            {
                obligations.Add(new ObligationEntryModel
                {
                    ContractId = contract.ContractId,
                    TemplateCode = contract.TemplateCode ?? "unknown",
                    ClauseCode = clause.ClauseCode,
                    ViolationType = clause.ViolationType,
                    BasePenalty = clause.BasePenalty,
                    Description = clause.Description,
                    ContractRole = role,
                    EffectiveUntil = contract.EffectiveUntil
                });
            }
        }

        // Apply max obligations safety limit
        if (obligations.Count > _configuration.MaxObligationsPerCharacter)
        {
            _logger.LogWarning(
                "Character {CharacterId} has {Count} obligations exceeding limit {Limit}, truncating",
                characterId, obligations.Count, _configuration.MaxObligationsPerCharacter);
            obligations = obligations.Take(_configuration.MaxObligationsPerCharacter).ToList();
        }

        // Build violation cost map (aggregate by violation type)
        var violationCostMap = obligations
            .GroupBy(o => o.ViolationType, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Sum(o => o.BasePenalty));

        var manifest = new ObligationManifestModel
        {
            CharacterId = characterId,
            Obligations = obligations,
            ViolationCostMap = violationCostMap,
            TotalActiveContracts = contractCount,
            LastRefreshedAt = DateTimeOffset.UtcNow
        };

        // Store in cache with TTL
        await _cacheStore.SaveAsync(
            characterId.ToString(),
            manifest,
            new StateOptions { Ttl = TimeSpan.FromMinutes(_configuration.CacheTtlMinutes) },
            ct);

        // Publish cache rebuilt event (observability)
        await _messageBus.TryPublishAsync("obligation.cache.rebuilt", new ObligationCacheRebuiltEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            CharacterId = characterId,
            ObligationCount = obligations.Count,
            ContractCount = contractCount,
            Trigger = trigger
        }, cancellationToken: ct);

        _logger.LogInformation(
            "Rebuilt obligation cache for character {CharacterId}: {ObligationCount} obligations from {ContractCount} contracts (trigger: {Trigger})",
            characterId, obligations.Count, contractCount, trigger);

        return manifest;
    }

    // ========================================================================
    // Internal: Behavioral Clause Extraction
    // ========================================================================

    /// <summary>
    /// Extracts behavioral clauses from a contract's CustomTerms dictionary.
    /// JsonElement navigation is acceptable per IMPLEMENTATION TENETS for metadata dictionaries.
    /// </summary>
    private static List<BehavioralClause> ExtractBehavioralClauses(ContractTerms? terms)
    {
        if (terms?.CustomTerms == null) return new();

        if (!terms.CustomTerms.TryGetValue("behavioral_clauses", out var clausesObj))
            return new();

        // CustomTerms values arrive as JsonElement from JSON deserialization
        if (clausesObj is not JsonElement element)
            return new();

        if (element.ValueKind != JsonValueKind.Array)
            return new();

        var clauses = new List<BehavioralClause>();
        foreach (var item in element.EnumerateArray())
        {
            var clauseCode = item.TryGetProperty("clauseCode", out var ccProp)
                ? ccProp.GetString() : null;
            var violationType = item.TryGetProperty("violationType", out var vtProp)
                ? vtProp.GetString() : null;
            var basePenalty = item.TryGetProperty("basePenalty", out var bpProp)
                ? bpProp.GetSingle() : 0f;
            var description = item.TryGetProperty("description", out var descProp)
                ? descProp.GetString() : null;

            if (clauseCode != null && violationType != null)
            {
                clauses.Add(new BehavioralClause(clauseCode, violationType, basePenalty, description));
            }
        }

        return clauses;
    }

    // ========================================================================
    // Internal: Action Tag Resolution
    // ========================================================================

    /// <summary>
    /// Resolves a GOAP action tag to violation type codes via explicit mappings.
    /// Falls back to 1:1 convention (tag name = violation type) when no mapping exists.
    /// </summary>
    private async Task<List<string>> ResolveViolationTypesAsync(string actionTag, CancellationToken ct)
    {
        var mapping = await _actionMappingStore.GetAsync($"mapping:{actionTag}", ct);
        if (mapping != null)
        {
            return mapping.ViolationTypes;
        }

        // 1:1 convention: action tag is used directly as violation type
        return new List<string> { actionTag };
    }

    // ========================================================================
    // Internal: Personality Enrichment (Soft L4 Dependency)
    // ========================================================================

    /// <summary>
    /// Attempts to retrieve personality traits for moral weighting via variable provider system.
    /// Returns null if personality data is unavailable (graceful degradation).
    /// </summary>
    private async Task<Dictionary<string, float>?> TryGetPersonalityTraitsAsync(
        Guid characterId, CancellationToken ct)
    {
        try
        {
            var personalityFactory = _providerFactories
                .FirstOrDefault(f => f.ProviderName == "personality");

            if (personalityFactory == null) return null;

            var provider = await personalityFactory.CreateAsync(characterId, ct);

            var traits = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
            foreach (var traitName in PersonalityTraitNames)
            {
                var value = provider.GetValue(new ReadOnlySpan<string>(new[] { traitName }));
                if (value is float f)
                {
                    traits[traitName] = f;
                }
            }

            return traits.Count > 0 ? traits : null;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex,
                "Failed to get personality traits for character {CharacterId}, proceeding without enrichment",
                characterId);
            return null;
        }
    }

    /// <summary>
    /// Computes the personality-based weight for a violation type.
    /// Higher relevant trait values increase the penalty (character cares more about the obligation).
    /// Weight ranges from 0.5 (trait = -1.0) to 1.5 (trait = 1.0).
    /// </summary>
    private static float ComputePersonalityWeight(
        string violationType, Dictionary<string, float> personalityTraits)
    {
        var relevantTraits = ViolationTypeTraitMap.TryGetValue(violationType, out var traits)
            ? traits : DefaultTraits;

        var totalTraitValue = 0f;
        var count = 0;

        foreach (var traitName in relevantTraits)
        {
            if (personalityTraits.TryGetValue(traitName, out var value))
            {
                totalTraitValue += value;
                count++;
            }
        }

        var averageTraitValue = count > 0 ? totalTraitValue / count : 0f;

        // Weight: -1.0 trait → 0.5x penalty, 0.0 → 1.0x, 1.0 → 1.5x
        return 1.0f + (averageTraitValue * 0.5f);
    }

    // ========================================================================
    // Internal: Mapping Helpers
    // ========================================================================

    /// <summary>
    /// Maps internal obligation entry model to API response type.
    /// </summary>
    private static ObligationEntry MapToObligationEntry(ObligationEntryModel model)
    {
        return new ObligationEntry
        {
            ContractId = model.ContractId,
            TemplateCode = model.TemplateCode,
            ClauseCode = model.ClauseCode,
            ViolationType = model.ViolationType,
            BasePenalty = model.BasePenalty,
            Description = model.Description,
            ContractRole = model.ContractRole,
            EffectiveUntil = model.EffectiveUntil
        };
    }

    /// <summary>
    /// Maps internal violation record model to API response type.
    /// </summary>
    private static ViolationRecord MapToViolationRecord(ViolationRecordModel model)
    {
        return new ViolationRecord
        {
            ViolationId = model.ViolationId,
            CharacterId = model.CharacterId,
            ContractId = model.ContractId,
            ClauseCode = model.ClauseCode,
            ViolationType = model.ViolationType,
            ActionTag = model.ActionTag,
            MotivationScore = model.MotivationScore,
            ViolationCost = model.ViolationCost,
            BreachReported = model.BreachReported,
            BreachId = model.BreachId,
            TargetEntityId = model.TargetEntityId,
            TargetEntityType = model.TargetEntityType,
            Timestamp = model.Timestamp
        };
    }

    // ========================================================================
    // Internal: Cursor Encoding
    // ========================================================================

    /// <summary>
    /// Decodes a base64-encoded cursor to an offset value.
    /// </summary>
    private static int DecodeCursor(string? cursor)
    {
        if (string.IsNullOrEmpty(cursor)) return 0;
        try
        {
            return int.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(cursor)));
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>
    /// Encodes an offset value as a base64 cursor string.
    /// </summary>
    private static string EncodeCursor(int offset)
    {
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(offset.ToString()));
    }

    // ========================================================================
    // Static Configuration: Violation Type → Personality Trait Mappings
    // ========================================================================

    /// <summary>
    /// Maps violation type codes to the personality traits that influence their moral weight.
    /// Higher trait values increase the penalty (the character cares more about the obligation).
    /// </summary>
    private static readonly Dictionary<string, string[]> ViolationTypeTraitMap =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["theft"] = new[] { "HONESTY", "CONSCIENTIOUSNESS" },
            ["deception"] = new[] { "HONESTY" },
            ["violence"] = new[] { "AGREEABLENESS" },
            ["honor_combat"] = new[] { "CONSCIENTIOUSNESS", "LOYALTY" },
            ["betrayal"] = new[] { "LOYALTY" },
            ["exploitation"] = new[] { "AGREEABLENESS", "HONESTY" },
            ["oath_breaking"] = new[] { "LOYALTY", "CONSCIENTIOUSNESS" },
            ["trespass"] = new[] { "CONSCIENTIOUSNESS" },
            ["disrespect"] = new[] { "AGREEABLENESS" },
            ["contraband"] = new[] { "CONSCIENTIOUSNESS" },
        };

    /// <summary>Default traits for unknown violation types.</summary>
    private static readonly string[] DefaultTraits = { "CONSCIENTIOUSNESS" };

    /// <summary>Personality trait names to query from the personality provider.</summary>
    private static readonly string[] PersonalityTraitNames =
        { "HONESTY", "LOYALTY", "AGREEABLENESS", "CONSCIENTIOUSNESS" };
}
