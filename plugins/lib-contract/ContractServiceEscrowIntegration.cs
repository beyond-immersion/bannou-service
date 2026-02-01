#nullable enable

using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.ServiceClients;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BeyondImmersion.BannouService.Contract;

/// <summary>
/// Partial class implementing escrow integration functionality for ContractService.
/// </summary>
/// <remarks>
/// <para>
/// This includes:
/// - Guardian system: lock/unlock/transfer-party for contracts as escrow assets
/// - Clause type system: registration and listing of extensible clause types
/// - Execution system: template values, asset requirements, and contract execution
/// </para>
/// </remarks>
public partial class ContractService
{
    // Additional state store key prefixes for escrow integration
    private const string CLAUSE_TYPE_PREFIX = "clause-type:";
    private const string ALL_CLAUSE_TYPES_KEY = "all-clause-types";
    private const string IDEMPOTENCY_PREFIX = "idempotency:";

    // TTL for idempotency cache entries configured in contract-configuration.yaml

    // Regex for validating template value key format (alphanumeric + underscore)
    private static readonly Regex TemplateKeyPattern = new(@"^[A-Za-z0-9_]+$", RegexOptions.Compiled);


    #region Guardian System

    /// <inheritdoc/>
    public async Task<(StatusCodes, LockContractResponse?)> LockContractAsync(
        LockContractRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Locking contract: {ContractId} with guardian: {GuardianId}",
                body.ContractInstanceId, body.GuardianId);

            // Check idempotency
            if (!string.IsNullOrEmpty(body.IdempotencyKey))
            {
                var idempotencyKey = $"{IDEMPOTENCY_PREFIX}lock:{body.IdempotencyKey}";
                var existingResult = await _stateStoreFactory.GetStore<LockContractResponse>(StateStoreDefinitions.Contract)
                    .GetAsync(idempotencyKey, cancellationToken);
                if (existingResult != null)
                {
                    _logger.LogInformation("Returning cached lock result for idempotency key: {Key}", body.IdempotencyKey);
                    return (StatusCodes.OK, existingResult);
                }
            }

            var instanceKey = $"{INSTANCE_PREFIX}{body.ContractInstanceId}";
            var store = _stateStoreFactory.GetStore<ContractInstanceModel>(StateStoreDefinitions.Contract);
            var (model, etag) = await store.GetWithETagAsync(instanceKey, cancellationToken);

            if (model == null)
            {
                _logger.LogWarning("Contract not found: {ContractId}", body.ContractInstanceId);
                return (StatusCodes.NotFound, null);
            }

            // Load template to check if transferable
            var templateKey = $"{TEMPLATE_PREFIX}{model.TemplateId}";
            var template = await _stateStoreFactory.GetStore<ContractTemplateModel>(StateStoreDefinitions.Contract)
                .GetAsync(templateKey, cancellationToken);

            if (template == null || !template.Transferable)
            {
                _logger.LogWarning("Contract template not transferable: {TemplateId}", model.TemplateId);
                return (StatusCodes.BadRequest, null);
            }

            // Check if already locked
            if (model.GuardianId.HasValue)
            {
                _logger.LogWarning("Contract already locked: {ContractId} by {GuardianId}",
                    body.ContractInstanceId, model.GuardianId);
                return (StatusCodes.Conflict, null);
            }

            // Lock the contract
            var now = DateTimeOffset.UtcNow;
            model.GuardianId = body.GuardianId;
            model.GuardianType = body.GuardianType;
            model.LockedAt = now;
            model.UpdatedAt = now;

            var savedEtag = await store.TrySaveAsync(instanceKey, model, etag ?? string.Empty, cancellationToken);
            if (savedEtag == null)
            {
                _logger.LogWarning("Concurrent modification detected for contract lock: {ContractId}", body.ContractInstanceId);
                return (StatusCodes.Conflict, null);
            }

            var response = new LockContractResponse
            {
                Locked = true,
                ContractId = body.ContractInstanceId,
                GuardianId = body.GuardianId,
                LockedAt = now
            };

            // Cache for idempotency
            if (!string.IsNullOrEmpty(body.IdempotencyKey))
            {
                var idempotencyKey = $"{IDEMPOTENCY_PREFIX}lock:{body.IdempotencyKey}";
                await _stateStoreFactory.GetStore<LockContractResponse>(StateStoreDefinitions.Contract)
                    .SaveAsync(idempotencyKey, response, new StateOptions { Ttl = _configuration.IdempotencyTtlSeconds }, cancellationToken);
            }

            // Publish event
            await PublishContractLockedEventAsync(model, body.GuardianId, body.GuardianType, cancellationToken);

            _logger.LogInformation("Locked contract: {ContractId} under guardian: {GuardianId}",
                body.ContractInstanceId, body.GuardianId);
            return (StatusCodes.OK, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error locking contract: {ContractId}", body.ContractInstanceId);
            await EmitErrorAsync("LockContract", "post:/contract/lock", ex);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, UnlockContractResponse?)> UnlockContractAsync(
        UnlockContractRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Unlocking contract: {ContractId}", body.ContractInstanceId);

            // Check idempotency
            if (!string.IsNullOrEmpty(body.IdempotencyKey))
            {
                var idempotencyKey = $"{IDEMPOTENCY_PREFIX}unlock:{body.IdempotencyKey}";
                var existingResult = await _stateStoreFactory.GetStore<UnlockContractResponse>(StateStoreDefinitions.Contract)
                    .GetAsync(idempotencyKey, cancellationToken);
                if (existingResult != null)
                {
                    return (StatusCodes.OK, existingResult);
                }
            }

            var instanceKey = $"{INSTANCE_PREFIX}{body.ContractInstanceId}";
            var store = _stateStoreFactory.GetStore<ContractInstanceModel>(StateStoreDefinitions.Contract);
            var (model, etag) = await store.GetWithETagAsync(instanceKey, cancellationToken);

            if (model == null)
            {
                _logger.LogWarning("Contract not found: {ContractId}", body.ContractInstanceId);
                return (StatusCodes.NotFound, null);
            }

            // Check if locked
            if (!model.GuardianId.HasValue)
            {
                _logger.LogWarning("Contract not locked: {ContractId}", body.ContractInstanceId);
                return (StatusCodes.NotFound, null);
            }

            // Verify guardian
            if (model.GuardianId != body.GuardianId || model.GuardianType != body.GuardianType)
            {
                _logger.LogWarning("Not the current guardian for contract: {ContractId}", body.ContractInstanceId);
                return (StatusCodes.Forbidden, null);
            }

            // Unlock the contract
            var previousGuardianId = model.GuardianId;
            var previousGuardianType = model.GuardianType;
            model.GuardianId = null;
            model.GuardianType = null;
            model.LockedAt = null;
            model.UpdatedAt = DateTimeOffset.UtcNow;

            var savedEtag = await store.TrySaveAsync(instanceKey, model, etag ?? string.Empty, cancellationToken);
            if (savedEtag == null)
            {
                _logger.LogWarning("Concurrent modification detected for contract unlock: {ContractId}", body.ContractInstanceId);
                return (StatusCodes.Conflict, null);
            }

            var response = new UnlockContractResponse
            {
                Unlocked = true,
                ContractId = body.ContractInstanceId
            };

            // Cache for idempotency
            if (!string.IsNullOrEmpty(body.IdempotencyKey))
            {
                var idempotencyKey = $"{IDEMPOTENCY_PREFIX}unlock:{body.IdempotencyKey}";
                await _stateStoreFactory.GetStore<UnlockContractResponse>(StateStoreDefinitions.Contract)
                    .SaveAsync(idempotencyKey, response, new StateOptions { Ttl = _configuration.IdempotencyTtlSeconds }, cancellationToken);
            }

            // Publish event
            await PublishContractUnlockedEventAsync(model, previousGuardianId, previousGuardianType, cancellationToken);

            _logger.LogInformation("Unlocked contract: {ContractId}", body.ContractInstanceId);
            return (StatusCodes.OK, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error unlocking contract: {ContractId}", body.ContractInstanceId);
            await EmitErrorAsync("UnlockContract", "post:/contract/unlock", ex);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, TransferContractPartyResponse?)> TransferContractPartyAsync(
        TransferContractPartyRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Transferring party in contract: {ContractId} from {FromId} to {ToId}",
                body.ContractInstanceId, body.FromEntityId, body.ToEntityId);

            // Check idempotency
            if (!string.IsNullOrEmpty(body.IdempotencyKey))
            {
                var idempotencyKey = $"{IDEMPOTENCY_PREFIX}transfer:{body.IdempotencyKey}";
                var existingResult = await _stateStoreFactory.GetStore<TransferContractPartyResponse>(StateStoreDefinitions.Contract)
                    .GetAsync(idempotencyKey, cancellationToken);
                if (existingResult != null)
                {
                    return (StatusCodes.OK, existingResult);
                }
            }

            var instanceKey = $"{INSTANCE_PREFIX}{body.ContractInstanceId}";
            var store = _stateStoreFactory.GetStore<ContractInstanceModel>(StateStoreDefinitions.Contract);
            var (model, etag) = await store.GetWithETagAsync(instanceKey, cancellationToken);

            if (model == null)
            {
                _logger.LogWarning("Contract not found: {ContractId}", body.ContractInstanceId);
                return (StatusCodes.NotFound, null);
            }

            // Verify contract is locked and caller is guardian
            if (!model.GuardianId.HasValue)
            {
                _logger.LogWarning("Contract not locked: {ContractId}", body.ContractInstanceId);
                return (StatusCodes.Forbidden, null);
            }

            if (model.GuardianId != body.GuardianId || model.GuardianType != body.GuardianType)
            {
                _logger.LogWarning("Not the current guardian for contract: {ContractId}", body.ContractInstanceId);
                return (StatusCodes.Forbidden, null);
            }

            // Find the party to transfer
            var party = model.Parties?.FirstOrDefault(p =>
                p.EntityId == body.FromEntityId &&
                p.EntityType == body.FromEntityType);

            if (party == null)
            {
                _logger.LogWarning("Party not found in contract: {FromEntityId}", body.FromEntityId);
                return (StatusCodes.BadRequest, null);
            }

            // Transfer the party
            var previousEntityId = party.EntityId;
            var previousEntityType = party.EntityType;
            var role = party.Role;
            party.EntityId = body.ToEntityId;
            party.EntityType = body.ToEntityType;
            model.UpdatedAt = DateTimeOffset.UtcNow;

            var savedEtag = await store.TrySaveAsync(instanceKey, model, etag ?? string.Empty, cancellationToken);
            if (savedEtag == null)
            {
                _logger.LogWarning("Concurrent modification detected for contract transfer: {ContractId}", body.ContractInstanceId);
                return (StatusCodes.Conflict, null);
            }

            // Update party indexes (after successful save)
            var oldPartyIndexKey = $"{PARTY_INDEX_PREFIX}{previousEntityType}:{previousEntityId}";
            await RemoveFromListAsync(oldPartyIndexKey, body.ContractInstanceId.ToString(), cancellationToken);
            var newPartyIndexKey = $"{PARTY_INDEX_PREFIX}{party.EntityType}:{party.EntityId}";
            await AddToListAsync(newPartyIndexKey, body.ContractInstanceId.ToString(), cancellationToken);

            var response = new TransferContractPartyResponse
            {
                Transferred = true,
                ContractId = body.ContractInstanceId,
                Role = role,
                FromEntityId = body.FromEntityId,
                ToEntityId = body.ToEntityId
            };

            // Cache for idempotency
            if (!string.IsNullOrEmpty(body.IdempotencyKey))
            {
                var idempotencyKey = $"{IDEMPOTENCY_PREFIX}transfer:{body.IdempotencyKey}";
                await _stateStoreFactory.GetStore<TransferContractPartyResponse>(StateStoreDefinitions.Contract)
                    .SaveAsync(idempotencyKey, response, new StateOptions { Ttl = _configuration.IdempotencyTtlSeconds }, cancellationToken);
            }

            // Publish event
            await PublishPartyTransferredEventAsync(model, role, body.FromEntityId, body.FromEntityType,
                body.ToEntityId, body.ToEntityType, cancellationToken);

            _logger.LogInformation("Transferred party in contract: {ContractId}, role: {Role}",
                body.ContractInstanceId, role);
            return (StatusCodes.OK, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error transferring party in contract: {ContractId}", body.ContractInstanceId);
            await EmitErrorAsync("TransferContractParty", "post:/contract/transfer-party", ex);
            return (StatusCodes.InternalServerError, null);
        }
    }

    #endregion

    #region Clause Type System

    /// <inheritdoc/>
    public async Task<(StatusCodes, RegisterClauseTypeResponse?)> RegisterClauseTypeAsync(
        RegisterClauseTypeRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Registering clause type: {TypeCode}", body.TypeCode);

            // Check if already exists
            var typeKey = $"{CLAUSE_TYPE_PREFIX}{body.TypeCode}";
            var existing = await _stateStoreFactory.GetStore<ClauseTypeModel>(StateStoreDefinitions.Contract)
                .GetAsync(typeKey, cancellationToken);

            if (existing != null)
            {
                _logger.LogWarning("Clause type already exists: {TypeCode}", body.TypeCode);
                return (StatusCodes.Conflict, null);
            }

            var model = new ClauseTypeModel
            {
                TypeCode = body.TypeCode,
                Description = body.Description,
                Category = body.Category,
                IsBuiltIn = false,
                ValidationHandler = body.ValidationHandler != null ? new ClauseHandlerModel
                {
                    Service = body.ValidationHandler.Service,
                    Endpoint = body.ValidationHandler.Endpoint,
                    RequestMapping = body.ValidationHandler.RequestMapping,
                    ResponseMapping = body.ValidationHandler.ResponseMapping
                } : null,
                ExecutionHandler = body.ExecutionHandler != null ? new ClauseHandlerModel
                {
                    Service = body.ExecutionHandler.Service,
                    Endpoint = body.ExecutionHandler.Endpoint,
                    RequestMapping = body.ExecutionHandler.RequestMapping,
                    ResponseMapping = body.ExecutionHandler.ResponseMapping
                } : null,
                CreatedAt = DateTimeOffset.UtcNow
            };

            await _stateStoreFactory.GetStore<ClauseTypeModel>(StateStoreDefinitions.Contract)
                .SaveAsync(typeKey, model, cancellationToken: cancellationToken);

            await AddToListAsync(ALL_CLAUSE_TYPES_KEY, body.TypeCode, cancellationToken);

            // Publish event
            await PublishClauseTypeRegisteredEventAsync(model, cancellationToken);

            _logger.LogInformation("Registered clause type: {TypeCode}", body.TypeCode);
            return (StatusCodes.OK, new RegisterClauseTypeResponse
            {
                Registered = true,
                TypeCode = body.TypeCode
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error registering clause type: {TypeCode}", body.TypeCode);
            await EmitErrorAsync("RegisterClauseType", "post:/contract/clause-type/register", ex);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, ListClauseTypesResponse?)> ListClauseTypesAsync(
        ListClauseTypesRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Listing clause types");

            // Ensure built-in types are registered
            await EnsureBuiltInClauseTypesAsync(cancellationToken);

            // Get all type codes
            var allTypeCodes = await _stateStoreFactory.GetStore<List<string>>(StateStoreDefinitions.Contract)
                .GetAsync(ALL_CLAUSE_TYPES_KEY, cancellationToken) ?? new List<string>();

            var summaries = new List<ClauseTypeSummary>();

            foreach (var typeCode in allTypeCodes)
            {
                var typeKey = $"{CLAUSE_TYPE_PREFIX}{typeCode}";
                var model = await _stateStoreFactory.GetStore<ClauseTypeModel>(StateStoreDefinitions.Contract)
                    .GetAsync(typeKey, cancellationToken);

                if (model == null) continue;

                // Apply filters
                if (body.Category.HasValue)
                {
                    if (model.Category != body.Category.Value)
                    {
                        continue;
                    }
                }

                if (body.IncludeBuiltIn == false && model.IsBuiltIn)
                {
                    continue;
                }

                summaries.Add(new ClauseTypeSummary
                {
                    TypeCode = model.TypeCode,
                    Description = model.Description,
                    Category = model.Category,
                    HasValidationHandler = model.ValidationHandler != null,
                    HasExecutionHandler = model.ExecutionHandler != null,
                    IsBuiltIn = model.IsBuiltIn
                });
            }

            return (StatusCodes.OK, new ListClauseTypesResponse
            {
                ClauseTypes = summaries
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing clause types");
            await EmitErrorAsync("ListClauseTypes", "post:/contract/clause-type/list", ex);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Ensures built-in clause types are registered on first access.
    /// </summary>
    private async Task EnsureBuiltInClauseTypesAsync(CancellationToken ct)
    {
        var allTypeCodes = await _stateStoreFactory.GetStore<List<string>>(StateStoreDefinitions.Contract)
            .GetAsync(ALL_CLAUSE_TYPES_KEY, ct) ?? new List<string>();

        // Check if built-in types are already registered
        if (allTypeCodes.Contains("asset_requirement"))
        {
            return;
        }

        _logger.LogInformation("Registering built-in clause types");

        // Register asset_requirement
        var assetRequirement = new ClauseTypeModel
        {
            TypeCode = "asset_requirement",
            Description = "Validates that required assets exist at a location",
            Category = ClauseCategory.Validation,
            IsBuiltIn = true,
            ValidationHandler = new ClauseHandlerModel
            {
                Service = "currency",
                Endpoint = "/currency/balance/get",
                RequestMapping = null,
                ResponseMapping = null
            },
            ExecutionHandler = null,
            CreatedAt = DateTimeOffset.UtcNow
        };

        // Register currency_transfer
        var currencyTransfer = new ClauseTypeModel
        {
            TypeCode = "currency_transfer",
            Description = "Transfers currency between wallets",
            Category = ClauseCategory.Execution,
            IsBuiltIn = true,
            ValidationHandler = null,
            ExecutionHandler = new ClauseHandlerModel
            {
                Service = "currency",
                Endpoint = "/currency/transfer",
                RequestMapping = null,
                ResponseMapping = null
            },
            CreatedAt = DateTimeOffset.UtcNow
        };

        // Register item_transfer
        var itemTransfer = new ClauseTypeModel
        {
            TypeCode = "item_transfer",
            Description = "Transfers items between containers",
            Category = ClauseCategory.Execution,
            IsBuiltIn = true,
            ValidationHandler = null,
            ExecutionHandler = new ClauseHandlerModel
            {
                Service = "inventory",
                Endpoint = "/inventory/transfer",
                RequestMapping = null,
                ResponseMapping = null
            },
            CreatedAt = DateTimeOffset.UtcNow
        };

        // Register fee
        var fee = new ClauseTypeModel
        {
            TypeCode = "fee",
            Description = "Deducts fee from source wallet and transfers to recipient",
            Category = ClauseCategory.Execution,
            IsBuiltIn = true,
            ValidationHandler = null,
            ExecutionHandler = new ClauseHandlerModel
            {
                Service = "currency",
                Endpoint = "/currency/transfer",
                RequestMapping = null,
                ResponseMapping = null
            },
            CreatedAt = DateTimeOffset.UtcNow
        };

        // Save all
        var store = _stateStoreFactory.GetStore<ClauseTypeModel>(StateStoreDefinitions.Contract);
        await store.SaveAsync($"{CLAUSE_TYPE_PREFIX}asset_requirement", assetRequirement, cancellationToken: ct);
        await store.SaveAsync($"{CLAUSE_TYPE_PREFIX}currency_transfer", currencyTransfer, cancellationToken: ct);
        await store.SaveAsync($"{CLAUSE_TYPE_PREFIX}item_transfer", itemTransfer, cancellationToken: ct);
        await store.SaveAsync($"{CLAUSE_TYPE_PREFIX}fee", fee, cancellationToken: ct);

        // Update list
        allTypeCodes.AddRange(new[] { "asset_requirement", "currency_transfer", "item_transfer", "fee" });
        await _stateStoreFactory.GetStore<List<string>>(StateStoreDefinitions.Contract)
            .SaveAsync(ALL_CLAUSE_TYPES_KEY, allTypeCodes, cancellationToken: ct);
    }

    #endregion

    #region Execution System

    /// <inheritdoc/>
    public async Task<(StatusCodes, SetTemplateValuesResponse?)> SetContractTemplateValuesAsync(
        SetTemplateValuesRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Setting template values on contract: {ContractId}", body.ContractInstanceId);

            var instanceKey = $"{INSTANCE_PREFIX}{body.ContractInstanceId}";
            var model = await _stateStoreFactory.GetStore<ContractInstanceModel>(StateStoreDefinitions.Contract)
                .GetAsync(instanceKey, cancellationToken);

            if (model == null)
            {
                _logger.LogWarning("Contract not found: {ContractId}", body.ContractInstanceId);
                return (StatusCodes.NotFound, null);
            }

            // Validate key format (alphanumeric + underscore)
            foreach (var key in body.TemplateValues.Keys)
            {
                if (!TemplateKeyPattern.IsMatch(key))
                {
                    _logger.LogWarning("Invalid template key format: {Key}", key);
                    return (StatusCodes.BadRequest, null);
                }
            }

            // Merge with existing values
            model.TemplateValues ??= new Dictionary<string, string>();
            foreach (var kvp in body.TemplateValues)
            {
                model.TemplateValues[kvp.Key] = kvp.Value;
            }
            model.UpdatedAt = DateTimeOffset.UtcNow;

            await _stateStoreFactory.GetStore<ContractInstanceModel>(StateStoreDefinitions.Contract)
                .SaveAsync(instanceKey, model, cancellationToken: cancellationToken);

            // Publish event
            await PublishTemplateValuesSetEventAsync(model, body.TemplateValues.Keys.ToList(), cancellationToken);

            _logger.LogInformation("Set {Count} template values on contract: {ContractId}",
                body.TemplateValues.Count, body.ContractInstanceId);

            return (StatusCodes.OK, new SetTemplateValuesResponse
            {
                Updated = true,
                ContractId = body.ContractInstanceId,
                ValueCount = model.TemplateValues.Count
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting template values on contract: {ContractId}", body.ContractInstanceId);
            await EmitErrorAsync("SetContractTemplateValues", "post:/contract/instance/set-template-values", ex);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, CheckAssetRequirementsResponse?)> CheckAssetRequirementsAsync(
        CheckAssetRequirementsRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Checking asset requirements for contract: {ContractId}", body.ContractInstanceId);

            var instanceKey = $"{INSTANCE_PREFIX}{body.ContractInstanceId}";
            var model = await _stateStoreFactory.GetStore<ContractInstanceModel>(StateStoreDefinitions.Contract)
                .GetAsync(instanceKey, cancellationToken);

            if (model == null)
            {
                _logger.LogWarning("Contract not found: {ContractId}", body.ContractInstanceId);
                return (StatusCodes.NotFound, null);
            }

            // Load template for clause definitions
            var templateKey = $"{TEMPLATE_PREFIX}{model.TemplateId}";
            var template = await _stateStoreFactory.GetStore<ContractTemplateModel>(StateStoreDefinitions.Contract)
                .GetAsync(templateKey, cancellationToken);

            if (template == null)
            {
                _logger.LogError("Template not found for contract: {ContractId}", body.ContractInstanceId);
                return (StatusCodes.InternalServerError, null);
            }

            // Check if there are any asset requirement clauses that need template values
            var clauses = ParseClausesFromTemplate(template);
            var hasAssetClauses = clauses.Any(c =>
                string.Equals(c.Type, "asset_requirement", StringComparison.OrdinalIgnoreCase));

            // Only require template values if there are asset requirement clauses
            if (hasAssetClauses && (model.TemplateValues == null || model.TemplateValues.Count == 0))
            {
                _logger.LogWarning("Template values not set for contract with asset clauses: {ContractId}", body.ContractInstanceId);
                return (StatusCodes.BadRequest, null);
            }

            // Get asset requirement clauses from template's custom terms
            var partyStatuses = await CheckAssetRequirementClausesAsync(model, template, cancellationToken);

            var allSatisfied = partyStatuses.All(p => p.Satisfied);

            return (StatusCodes.OK, new CheckAssetRequirementsResponse
            {
                AllSatisfied = allSatisfied,
                ByParty = partyStatuses
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking asset requirements for contract: {ContractId}", body.ContractInstanceId);
            await EmitErrorAsync("CheckAssetRequirements", "post:/contract/instance/check-asset-requirements", ex);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Checks asset requirement clauses by querying actual balances via registered clause type handlers.
    /// </summary>
    private async Task<List<PartyAssetRequirementStatus>> CheckAssetRequirementClausesAsync(
        ContractInstanceModel contract,
        ContractTemplateModel template,
        CancellationToken ct)
    {
        var results = new List<PartyAssetRequirementStatus>();

        if (contract.Parties == null)
        {
            return results;
        }

        // Parse clause definitions from template
        var clauses = ParseClausesFromTemplate(template);
        var requirementClauses = clauses.Where(c =>
            string.Equals(c.Type, "asset_requirement", StringComparison.OrdinalIgnoreCase)).ToList();

        // Initialize results for each party
        var partyStatusMap = new Dictionary<string, PartyAssetRequirementStatus>();
        foreach (var party in contract.Parties)
        {
            var status = new PartyAssetRequirementStatus
            {
                PartyRole = party.Role,
                Satisfied = true,
                Clauses = new List<ClauseAssetStatus>()
            };
            partyStatusMap[party.Role] = status;
            results.Add(status);
        }

        if (requirementClauses.Count == 0)
        {
            return results;
        }

        // Load the asset_requirement clause type handler
        var clauseType = await _stateStoreFactory.GetStore<ClauseTypeModel>(StateStoreDefinitions.Contract)
            .GetAsync($"{CLAUSE_TYPE_PREFIX}asset_requirement", ct);

        // Process each asset requirement clause
        foreach (var clause in requirementClauses)
        {
            var partyRole = clause.GetProperty("party");
            if (string.IsNullOrEmpty(partyRole) || !partyStatusMap.ContainsKey(partyRole))
            {
                _logger.LogWarning("Asset requirement clause {ClauseId} references unknown party role: {Role}",
                    clause.Id, partyRole);
                continue;
            }

            var partyStatus = partyStatusMap[partyRole];
            var checkLocation = ResolveTemplateValue(clause.GetProperty("check_location"), contract.TemplateValues);
            var assets = clause.GetArray("assets");

            foreach (var asset in assets)
            {
                var assetType = GetJsonStringProperty(asset, "type");
                var code = GetJsonStringProperty(asset, "code");
                var requiredAmount = GetJsonDoubleProperty(asset, "amount");

                var clauseAssetStatus = new ClauseAssetStatus
                {
                    ClauseId = clause.Id,
                    Required = new AssetRequirementInfo
                    {
                        Type = assetType,
                        Code = code,
                        Amount = requiredAmount
                    },
                    Satisfied = false,
                    Current = 0
                };

                // Call the validation handler if available
                if (clauseType?.ValidationHandler != null && !string.IsNullOrEmpty(checkLocation))
                {
                    var actualAmount = await QueryAssetBalanceAsync(
                        clauseType.ValidationHandler, checkLocation, code, assetType, contract, ct);

                    clauseAssetStatus.Current = actualAmount;
                    clauseAssetStatus.Satisfied = actualAmount >= requiredAmount;
                    clauseAssetStatus.Missing = Math.Max(0, requiredAmount - actualAmount);
                }
                else
                {
                    clauseAssetStatus.Missing = requiredAmount;
                    _logger.LogWarning("Cannot verify asset requirement for clause {ClauseId}: missing handler or check_location",
                        clause.Id);
                }

                partyStatus.Clauses.Add(clauseAssetStatus);

                if (!clauseAssetStatus.Satisfied)
                {
                    partyStatus.Satisfied = false;
                }
            }
        }

        return results;
    }

    /// <summary>
    /// Queries an asset balance by calling the clause type's validation handler.
    /// </summary>
    private async Task<double> QueryAssetBalanceAsync(
        ClauseHandlerModel handler,
        string walletOrContainerId,
        string currencyOrItemCode,
        string assetType,
        ContractInstanceModel contract,
        CancellationToken ct)
    {
        try
        {
            // Build context with contract data and template values
            var context = BuildContractContext(contract);
            if (contract.TemplateValues != null)
            {
                foreach (var kvp in contract.TemplateValues)
                {
                    context[kvp.Key] = kvp.Value;
                }
            }

            // Build payload based on asset type
            string payloadTemplate;
            if (string.Equals(assetType, "currency", StringComparison.OrdinalIgnoreCase))
            {
                payloadTemplate = BannouJson.Serialize(new Dictionary<string, object>
                {
                    ["wallet_id"] = walletOrContainerId,
                    ["currency_code"] = currencyOrItemCode
                });
            }
            else
            {
                payloadTemplate = BannouJson.Serialize(new Dictionary<string, object>
                {
                    ["container_id"] = walletOrContainerId,
                    ["item_code"] = currencyOrItemCode
                });
            }

            var apiDefinition = new PreboundApiDefinition
            {
                ServiceName = handler.Service,
                Endpoint = handler.Endpoint,
                PayloadTemplate = payloadTemplate,
                ExecutionMode = ExecutionMode.Sync
            };

            var result = await _navigator.ExecutePreboundApiAsync(apiDefinition, context, ct);

            if (!result.SubstitutionSucceeded || result.Result == null)
            {
                _logger.LogWarning("Balance query failed for {Service}{Endpoint}: substitution={SubSuccess}",
                    handler.Service, handler.Endpoint, result.SubstitutionSucceeded);
                return 0;
            }

            if (result.Result.StatusCode != 200)
            {
                _logger.LogWarning("Balance query returned non-200: {StatusCode} from {Service}{Endpoint}",
                    result.Result.StatusCode, handler.Service, handler.Endpoint);
                return 0;
            }

            // Parse the balance from the response
            if (!string.IsNullOrEmpty(result.Result.ResponseBody))
            {
                var root = BannouJson.Deserialize<JsonElement>(result.Result.ResponseBody);

                // Look for common balance fields: "balance", "amount", "quantity"
                if (root.TryGetProperty("balance", out var balanceProp) && balanceProp.TryGetDouble(out var balance))
                {
                    return balance;
                }
                if (root.TryGetProperty("amount", out var amountProp) && amountProp.TryGetDouble(out var amount))
                {
                    return amount;
                }
                if (root.TryGetProperty("quantity", out var quantityProp) && quantityProp.TryGetDouble(out var quantity))
                {
                    return quantity;
                }
            }

            return 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to query asset balance from {Service}{Endpoint}",
                handler.Service, handler.Endpoint);
            return 0;
        }
    }

    /// <summary>
    /// Queries the current balance of a specific currency in a wallet via the currency service.
    /// Used for resolving "remainder" amounts during clause execution.
    /// Returns null on failure to distinguish from actual zero balance.
    /// </summary>
    private async Task<double?> QueryWalletBalanceAsync(
        string walletId, string currencyCode, ContractInstanceModel contract, CancellationToken ct)
    {
        try
        {
            var context = BuildContractContext(contract);
            if (contract.TemplateValues != null)
            {
                foreach (var kvp in contract.TemplateValues)
                {
                    context[kvp.Key] = kvp.Value;
                }
            }

            var payloadTemplate = BannouJson.Serialize(new Dictionary<string, object>
            {
                ["wallet_id"] = walletId,
                ["currency_code"] = currencyCode
            });

            var apiDefinition = new PreboundApiDefinition
            {
                ServiceName = "currency",
                Endpoint = "/currency/balance/get",
                PayloadTemplate = payloadTemplate,
                ExecutionMode = ExecutionMode.Sync
            };

            var result = await _navigator.ExecutePreboundApiAsync(apiDefinition, context, ct);

            if (!result.SubstitutionSucceeded || result.Result == null || result.Result.StatusCode != 200)
            {
                _logger.LogWarning("Wallet balance query failed for wallet {WalletId}: status={Status}",
                    walletId, result.Result?.StatusCode);
                return null;
            }

            if (!string.IsNullOrEmpty(result.Result.ResponseBody))
            {
                var root = BannouJson.Deserialize<JsonElement>(result.Result.ResponseBody);

                if (root.TryGetProperty("balance", out var balanceProp) && balanceProp.TryGetDouble(out var balance))
                {
                    return balance;
                }
                if (root.TryGetProperty("amount", out var amountProp) && amountProp.TryGetDouble(out var amount))
                {
                    return amount;
                }
            }

            _logger.LogWarning("Wallet balance response for {WalletId} had no parseable balance field", walletId);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to query wallet balance for {WalletId}", walletId);
            return null;
        }
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, ExecuteContractResponse?)> ExecuteContractAsync(
        ExecuteContractRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Executing contract: {ContractId}", body.ContractInstanceId);

            var instanceKey = $"{INSTANCE_PREFIX}{body.ContractInstanceId}";
            var store = _stateStoreFactory.GetStore<ContractInstanceModel>(StateStoreDefinitions.Contract);

            // Acquire contract lock for execution
            await using var contractLock = await _lockProvider.LockAsync(
                "contract-instance", body.ContractInstanceId.ToString(), Guid.NewGuid().ToString(), _configuration.ContractLockTimeoutSeconds, cancellationToken);
            if (!contractLock.Success)
            {
                _logger.LogWarning("Could not acquire contract lock for {ContractId}", body.ContractInstanceId);
                return (StatusCodes.Conflict, null);
            }

            var (model, etag) = await store.GetWithETagAsync(instanceKey, cancellationToken);

            if (model == null)
            {
                _logger.LogWarning("Contract not found: {ContractId}", body.ContractInstanceId);
                return (StatusCodes.NotFound, null);
            }

            // Check idempotency - return cached result if already executed
            if (!string.IsNullOrEmpty(model.ExecutionIdempotencyKey) && model.ExecutedAt.HasValue)
            {
                _logger.LogInformation("Contract already executed, returning cached result: {ContractId}",
                    body.ContractInstanceId);

                return (StatusCodes.OK, new ExecuteContractResponse
                {
                    Executed = true,
                    AlreadyExecuted = true,
                    ContractId = body.ContractInstanceId,
                    Distributions = model.ExecutionDistributions?.Select(d => new ClauseDistributionResult
                    {
                        ClauseId = Guid.TryParse(d.ClauseId, out var cid) ? cid : Guid.Empty,
                        ClauseType = d.ClauseType,
                        Amount = d.Amount,
                        Succeeded = d.Succeeded,
                        FailureReason = d.FailureReason
                    }).ToList(),
                    ExecutedAt = model.ExecutedAt
                });
            }

            // Also check by idempotency key if provided
            if (!string.IsNullOrEmpty(body.IdempotencyKey))
            {
                var idempotencyKey = $"{IDEMPOTENCY_PREFIX}execute:{body.IdempotencyKey}";
                var existingResult = await _stateStoreFactory.GetStore<ExecuteContractResponse>(StateStoreDefinitions.Contract)
                    .GetAsync(idempotencyKey, cancellationToken);
                if (existingResult != null)
                {
                    return (StatusCodes.OK, existingResult);
                }
            }

            // Verify contract is in fulfilled status
            if (model.Status != ContractStatus.Fulfilled)
            {
                _logger.LogWarning("Contract not in fulfilled status: {ContractId}, status: {Status}",
                    body.ContractInstanceId, model.Status);
                return (StatusCodes.BadRequest, null);
            }

            // Verify template values are set
            if (model.TemplateValues == null || model.TemplateValues.Count == 0)
            {
                _logger.LogWarning("Template values not set for contract: {ContractId}", body.ContractInstanceId);
                return (StatusCodes.BadRequest, null);
            }

            // Execute clauses via service navigator
            var distributions = await ExecuteContractClausesAsync(model, cancellationToken);

            // Mark as executed
            var now = DateTimeOffset.UtcNow;
            model.ExecutedAt = now;
            model.ExecutionIdempotencyKey = body.IdempotencyKey ?? Guid.NewGuid().ToString();
            model.ExecutionDistributions = distributions;
            model.UpdatedAt = now;

            var newEtag = await store.TrySaveAsync(instanceKey, model, etag ?? string.Empty, cancellationToken);
            if (newEtag == null)
            {
                _logger.LogWarning("Concurrent modification detected for contract: {ContractId}", body.ContractInstanceId);
                return (StatusCodes.Conflict, null);
            }

            var response = new ExecuteContractResponse
            {
                Executed = true,
                AlreadyExecuted = false,
                ContractId = body.ContractInstanceId,
                Distributions = distributions.Select(d => new ClauseDistributionResult
                {
                    ClauseId = Guid.TryParse(d.ClauseId, out var cid) ? cid : Guid.Empty,
                    ClauseType = d.ClauseType,
                    Amount = d.Amount,
                    Succeeded = d.Succeeded,
                    FailureReason = d.FailureReason
                }).ToList(),
                ExecutedAt = now
            };

            // Cache for idempotency
            if (!string.IsNullOrEmpty(body.IdempotencyKey))
            {
                var idempotencyKey = $"{IDEMPOTENCY_PREFIX}execute:{body.IdempotencyKey}";
                await _stateStoreFactory.GetStore<ExecuteContractResponse>(StateStoreDefinitions.Contract)
                    .SaveAsync(idempotencyKey, response, new StateOptions { Ttl = _configuration.IdempotencyTtlSeconds }, cancellationToken);
            }

            // Publish event
            await PublishContractExecutedEventAsync(model, distributions, cancellationToken);

            _logger.LogInformation("Executed contract: {ContractId} with {Count} distributions",
                body.ContractInstanceId, distributions.Count);

            return (StatusCodes.OK, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing contract: {ContractId}", body.ContractInstanceId);
            await EmitErrorAsync("ExecuteContract", "post:/contract/instance/execute", ex);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Executes all contract clauses (fees first, then distributions) via registered clause type handlers.
    /// </summary>
    private async Task<List<DistributionRecordModel>> ExecuteContractClausesAsync(
        ContractInstanceModel contract,
        CancellationToken ct)
    {
        var distributions = new List<DistributionRecordModel>();

        // Ensure built-in clause types are registered before execution
        await EnsureBuiltInClauseTypesAsync(ct);

        // Load the template to get clause definitions
        var templateKey = $"{TEMPLATE_PREFIX}{contract.TemplateId}";
        var template = await _stateStoreFactory.GetStore<ContractTemplateModel>(StateStoreDefinitions.Contract)
            .GetAsync(templateKey, ct);

        if (template == null)
        {
            _logger.LogError("Template not found during execution: {TemplateId}", contract.TemplateId);
            return distributions;
        }

        // Parse clause definitions
        var clauses = ParseClausesFromTemplate(template);

        // Separate into fee clauses and distribution clauses (fees execute first)
        var feeClauses = clauses.Where(c =>
            string.Equals(c.Type, "fee", StringComparison.OrdinalIgnoreCase)).ToList();
        var distributionClauses = clauses.Where(c =>
            string.Equals(c.Type, "distribution", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(c.Type, "currency_transfer", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(c.Type, "item_transfer", StringComparison.OrdinalIgnoreCase)).ToList();

        if (feeClauses.Count == 0 && distributionClauses.Count == 0)
        {
            _logger.LogDebug("Contract {ContractId} has no executable clauses", contract.ContractId);
            return distributions;
        }

        // Build context for template substitution
        var context = BuildContractContext(contract);
        if (contract.TemplateValues != null)
        {
            foreach (var kvp in contract.TemplateValues)
            {
                context[kvp.Key] = kvp.Value;
            }
        }

        // Execute fee clauses first
        foreach (var clause in feeClauses)
        {
            var result = await ExecuteSingleClauseAsync(clause, contract, context, ct);
            distributions.Add(result);
        }

        // Then execute distribution clauses
        foreach (var clause in distributionClauses)
        {
            var result = await ExecuteSingleClauseAsync(clause, contract, context, ct);
            distributions.Add(result);
        }

        _logger.LogInformation("Executed {Count} clauses for contract {ContractId}",
            distributions.Count, contract.ContractId);

        return distributions;
    }

    /// <summary>
    /// Executes a single clause by calling the registered clause type's execution handler.
    /// Always returns a result - Succeeded indicates whether the clause executed successfully.
    /// </summary>
    private async Task<DistributionRecordModel> ExecuteSingleClauseAsync(
        ClauseDefinition clause,
        ContractInstanceModel contract,
        Dictionary<string, object?> context,
        CancellationToken ct)
    {
        try
        {
            // Load the clause type to get its execution handler
            var clauseType = await _stateStoreFactory.GetStore<ClauseTypeModel>(StateStoreDefinitions.Contract)
                .GetAsync($"{CLAUSE_TYPE_PREFIX}{clause.Type}", ct);

            // Fee type uses currency_transfer handler
            if (clauseType == null && string.Equals(clause.Type, "fee", StringComparison.OrdinalIgnoreCase))
            {
                clauseType = await _stateStoreFactory.GetStore<ClauseTypeModel>(StateStoreDefinitions.Contract)
                    .GetAsync($"{CLAUSE_TYPE_PREFIX}fee", ct);
            }

            // Distribution type uses currency_transfer or item_transfer
            if (clauseType == null && string.Equals(clause.Type, "distribution", StringComparison.OrdinalIgnoreCase))
            {
                // Determine if currency or item based on clause properties
                var hasSourceContainer = !string.IsNullOrEmpty(clause.GetProperty("source_container"));
                var typeCode = hasSourceContainer ? "item_transfer" : "currency_transfer";
                clauseType = await _stateStoreFactory.GetStore<ClauseTypeModel>(StateStoreDefinitions.Contract)
                    .GetAsync($"{CLAUSE_TYPE_PREFIX}{typeCode}", ct);
            }

            if (clauseType?.ExecutionHandler == null)
            {
                _logger.LogWarning("No execution handler found for clause type: {Type}, clause: {ClauseId}",
                    clause.Type, clause.Id);
                return new DistributionRecordModel
                {
                    ClauseId = clause.Id,
                    ClauseType = clause.Type,
                    Amount = 0,
                    Succeeded = false,
                    FailureReason = $"No execution handler found for clause type: {clause.Type}"
                };
            }

            // Build the transfer payload based on clause type
            var handler = clauseType.ExecutionHandler;
            string payloadTemplate;
            double amount;
            string? sourceId;
            string? destinationId;

            if (string.Equals(clause.Type, "fee", StringComparison.OrdinalIgnoreCase))
            {
                sourceId = ResolveTemplateValue(clause.GetProperty("source_wallet"), contract.TemplateValues);
                destinationId = ResolveTemplateValue(clause.GetProperty("recipient_wallet"), contract.TemplateValues);
                amount = ParseClauseAmount(clause, contract);

                if (string.IsNullOrEmpty(sourceId) || string.IsNullOrEmpty(destinationId))
                {
                    _logger.LogWarning("Fee clause {ClauseId} missing source or recipient wallet after template resolution", clause.Id);
                    return new DistributionRecordModel
                    {
                        ClauseId = clause.Id,
                        ClauseType = clause.Type,
                        Amount = 0,
                        Succeeded = false,
                        FailureReason = "Missing source or recipient wallet after template resolution"
                    };
                }

                // Remainder for fees: query source wallet balance
                if (amount == REMAINDER_SENTINEL)
                {
                    var currencyForBalance = clause.GetProperty("currency_code") ?? "gold";
                    var balanceResult = await QueryWalletBalanceAsync(sourceId, currencyForBalance, contract, ct);
                    if (balanceResult == null)
                    {
                        _logger.LogWarning("Fee clause {ClauseId} failed: could not query remainder balance for wallet {WalletId}",
                            clause.Id, sourceId);
                        return new DistributionRecordModel
                        {
                            ClauseId = clause.Id,
                            ClauseType = clause.Type,
                            Amount = 0,
                            Succeeded = false,
                            FailureReason = $"Could not query remainder balance for wallet {sourceId}"
                        };
                    }
                    amount = balanceResult.Value;
                }

                var currencyCode = clause.GetProperty("currency_code") ?? "gold";
                payloadTemplate = BannouJson.Serialize(new Dictionary<string, object>
                {
                    ["from_wallet_id"] = sourceId,
                    ["to_wallet_id"] = destinationId,
                    ["currency_code"] = currencyCode,
                    ["amount"] = amount
                });
            }
            else if (!string.IsNullOrEmpty(clause.GetProperty("source_container")))
            {
                // Item transfer
                sourceId = ResolveTemplateValue(clause.GetProperty("source_container"), contract.TemplateValues);
                destinationId = ResolveTemplateValue(clause.GetProperty("destination_container"), contract.TemplateValues);
                amount = GetClauseDoubleProperty(clause, "quantity", 1);

                if (string.IsNullOrEmpty(sourceId) || string.IsNullOrEmpty(destinationId))
                {
                    _logger.LogWarning("Item clause {ClauseId} missing source or destination container after template resolution", clause.Id);
                    return new DistributionRecordModel
                    {
                        ClauseId = clause.Id,
                        ClauseType = clause.Type,
                        Amount = 0,
                        Succeeded = false,
                        FailureReason = "Missing source or destination container after template resolution"
                    };
                }

                var itemCode = clause.GetProperty("item_code") ?? "all";
                payloadTemplate = BannouJson.Serialize(new Dictionary<string, object>
                {
                    ["from_container_id"] = sourceId,
                    ["to_container_id"] = destinationId,
                    ["item_code"] = itemCode,
                    ["quantity"] = (int)amount
                });
            }
            else
            {
                // Currency transfer / distribution
                sourceId = ResolveTemplateValue(clause.GetProperty("source_wallet"), contract.TemplateValues);
                destinationId = ResolveTemplateValue(clause.GetProperty("destination_wallet"), contract.TemplateValues);
                amount = ParseClauseAmount(clause, contract);

                if (string.IsNullOrEmpty(sourceId) || string.IsNullOrEmpty(destinationId))
                {
                    _logger.LogWarning("Distribution clause {ClauseId} missing source or destination wallet after template resolution", clause.Id);
                    return new DistributionRecordModel
                    {
                        ClauseId = clause.Id,
                        ClauseType = clause.Type,
                        Amount = 0,
                        Succeeded = false,
                        FailureReason = "Missing source or destination wallet after template resolution"
                    };
                }

                // Remainder for distributions: query source wallet balance (after fees deducted)
                if (amount == REMAINDER_SENTINEL)
                {
                    var currencyForBalance = clause.GetProperty("currency_code") ?? "gold";
                    var balanceResult = await QueryWalletBalanceAsync(sourceId, currencyForBalance, contract, ct);
                    if (balanceResult == null)
                    {
                        _logger.LogWarning("Distribution clause {ClauseId} failed: could not query remainder balance for wallet {WalletId}",
                            clause.Id, sourceId);
                        return new DistributionRecordModel
                        {
                            ClauseId = clause.Id,
                            ClauseType = clause.Type,
                            Amount = 0,
                            Succeeded = false,
                            FailureReason = $"Could not query remainder balance for wallet {sourceId}"
                        };
                    }
                    amount = balanceResult.Value;
                }

                var currencyCode = clause.GetProperty("currency_code") ?? "gold";
                payloadTemplate = BannouJson.Serialize(new Dictionary<string, object>
                {
                    ["from_wallet_id"] = sourceId,
                    ["to_wallet_id"] = destinationId,
                    ["currency_code"] = currencyCode,
                    ["amount"] = amount
                });
            }

            // Execute via navigator
            var apiDefinition = new PreboundApiDefinition
            {
                ServiceName = handler.Service,
                Endpoint = handler.Endpoint,
                PayloadTemplate = payloadTemplate,
                ExecutionMode = ExecutionMode.Sync
            };

            var result = await _navigator.ExecutePreboundApiAsync(apiDefinition, context, ct);

            if (!result.SubstitutionSucceeded)
            {
                var failureReason = $"Template substitution failed: {result.SubstitutionError}";
                _logger.LogWarning("Template substitution failed for clause {ClauseId}: {Error}",
                    clause.Id, result.SubstitutionError);
                await _messageBus.TryPublishAsync("contract.prebound-api.failed", new ContractPreboundApiFailedEvent
                {
                    EventId = Guid.NewGuid(),
                    Timestamp = DateTimeOffset.UtcNow,
                    ContractId = contract.ContractId,
                    Trigger = "contract.execute",
                    ServiceName = handler.Service,
                    Endpoint = handler.Endpoint,
                    ErrorMessage = failureReason,
                    StatusCode = null
                });
                return new DistributionRecordModel
                {
                    ClauseId = clause.Id,
                    ClauseType = clause.Type,
                    Amount = amount,
                    Succeeded = false,
                    FailureReason = failureReason
                };
            }

            if (result.Result == null || result.Result.StatusCode != 200)
            {
                var failureReason = $"Handler returned status {result.Result?.StatusCode}";
                _logger.LogWarning("Clause execution failed for {ClauseId}: status={StatusCode}",
                    clause.Id, result.Result?.StatusCode);
                await _messageBus.TryPublishAsync("contract.prebound-api.failed", new ContractPreboundApiFailedEvent
                {
                    EventId = Guid.NewGuid(),
                    Timestamp = DateTimeOffset.UtcNow,
                    ContractId = contract.ContractId,
                    Trigger = "contract.execute",
                    ServiceName = handler.Service,
                    Endpoint = handler.Endpoint,
                    ErrorMessage = failureReason,
                    StatusCode = result.Result?.StatusCode
                });
                return new DistributionRecordModel
                {
                    ClauseId = clause.Id,
                    ClauseType = clause.Type,
                    Amount = amount,
                    Succeeded = false,
                    FailureReason = failureReason
                };
            }

            _logger.LogDebug("Clause {ClauseId} executed: {Amount} {ClauseType}",
                clause.Id, amount, clause.Type);

            return new DistributionRecordModel
            {
                ClauseId = clause.Id,
                ClauseType = clause.Type,
                Amount = amount,
                Succeeded = true,
                FailureReason = null
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute clause {ClauseId} for contract {ContractId}",
                clause.Id, contract.ContractId);
            return new DistributionRecordModel
            {
                ClauseId = clause.Id,
                ClauseType = clause.Type,
                Amount = 0,
                Succeeded = false,
                FailureReason = ex.Message
            };
        }
    }

    /// <summary>
    /// Sentinel value indicating the clause should transfer all remaining balance from the source.
    /// </summary>
    private const double REMAINDER_SENTINEL = -1;

    /// <summary>
    /// Parses the amount from a clause definition, handling flat, percentage, and remainder types.
    /// Returns REMAINDER_SENTINEL (-1) when the clause specifies "remainder" to signal the caller
    /// should query the source wallet balance and use the full remaining amount.
    /// </summary>
    private double ParseClauseAmount(ClauseDefinition clause, ContractInstanceModel contract)
    {
        var amountStr = clause.GetProperty("amount");
        var amountType = clause.GetProperty("amount_type") ?? "flat";

        if (string.Equals(amountStr, "remainder", StringComparison.OrdinalIgnoreCase))
        {
            return REMAINDER_SENTINEL;
        }

        if (!double.TryParse(amountStr, out var rawAmount))
        {
            // Try resolving {{TemplateKey}} patterns from template values
            if (!string.IsNullOrEmpty(amountStr) && contract.TemplateValues != null)
            {
                var resolved = ResolveTemplateValue(amountStr, contract.TemplateValues);
                if (double.TryParse(resolved, out var resolvedAmount))
                {
                    return resolvedAmount;
                }
            }
            _logger.LogWarning("Clause {ClauseId} has unparseable amount {Amount}, defaulting to 0",
                clause.Id, amountStr);
            return 0;
        }

        if (string.Equals(amountType, "percentage", StringComparison.OrdinalIgnoreCase))
        {
            // Percentage of the base_amount template value
            if (contract.TemplateValues != null && contract.TemplateValues.TryGetValue("base_amount", out var baseVal))
            {
                if (double.TryParse(baseVal, out var baseAmount))
                {
                    return Math.Floor(baseAmount * rawAmount / 100.0);
                }
                _logger.LogWarning("Clause {ClauseId} has percentage amount_type but base_amount {BaseAmount} is not a valid number, defaulting to 0",
                    clause.Id, baseVal);
            }
            else
            {
                _logger.LogWarning("Clause {ClauseId} has percentage amount_type but no base_amount in template values, defaulting to 0",
                    clause.Id);
            }
            return 0;
        }

        return rawAmount;
    }

    /// <summary>
    /// Gets a double property from a clause definition with a default value.
    /// </summary>
    private static double GetClauseDoubleProperty(ClauseDefinition clause, string key, double defaultValue)
    {
        var value = clause.GetProperty(key);
        if (double.TryParse(value, out var result))
        {
            return result;
        }
        return defaultValue;
    }

    #endregion

    #region Clause Parsing Helpers

    /// <summary>
    /// Parses clause definitions from a template's custom terms.
    /// Clauses are stored as a JSON array under the "clauses" key in DefaultTerms.CustomTerms.
    /// </summary>
    private List<ClauseDefinition> ParseClausesFromTemplate(ContractTemplateModel template)
    {
        var clauses = new List<ClauseDefinition>();

        if (template.DefaultTerms?.CustomTerms == null ||
            !template.DefaultTerms.CustomTerms.TryGetValue("clauses", out var clausesObj))
        {
            return clauses;
        }

        // CustomTerms values are JsonElement when deserialized from state store
        JsonElement clausesElement;
        if (clausesObj is JsonElement je)
        {
            clausesElement = je;
        }
        else
        {
            // Serialize the object directly to JsonElement via BannouJson
            clausesElement = BannouJson.SerializeToElement(clausesObj);
        }

        if (clausesElement.ValueKind != JsonValueKind.Array)
        {
            _logger.LogWarning("Template clauses is not an array, kind: {Kind}", clausesElement.ValueKind);
            return clauses;
        }

        foreach (var element in clausesElement.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object) continue;

            var id = GetJsonStringProperty(element, "id");
            var type = GetJsonStringProperty(element, "type");

            if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(type))
            {
                _logger.LogWarning("Clause missing id or type, skipping");
                continue;
            }

            clauses.Add(new ClauseDefinition(id, type, element.Clone()));
        }

        return clauses;
    }

    /// <summary>
    /// Resolves template value references ({{Key}} patterns) in a string using the contract's template values.
    /// Returns null if input is null or empty.
    /// </summary>
    private static string? ResolveTemplateValue(string? input, Dictionary<string, string>? templateValues)
    {
        if (string.IsNullOrEmpty(input) || templateValues == null)
        {
            return input;
        }

        // Replace all {{Key}} patterns with their values
        return TemplateValuePattern.Replace(input, match =>
        {
            var key = match.Groups[1].Value;
            if (templateValues.TryGetValue(key, out var value))
            {
                return value;
            }
            // Return the original placeholder if no value found
            return match.Value;
        });
    }

    // Pattern for matching {{Key}} template value references
    private static readonly Regex TemplateValuePattern = new(@"\{\{(\w+)\}\}", RegexOptions.Compiled);

    /// <summary>
    /// Gets a string property from a JsonElement, returning empty string if not found.
    /// </summary>
    private static string GetJsonStringProperty(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String)
        {
            // GetString() returns string? but cannot return null when ValueKind is String;
            // coalesce satisfies compiler's nullable analysis (will never execute)
            return prop.GetString() ?? string.Empty;
        }
        return string.Empty;
    }

    /// <summary>
    /// Gets a double property from a JsonElement, returning 0 if not found.
    /// </summary>
    private static double GetJsonDoubleProperty(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var prop))
        {
            if (prop.ValueKind == JsonValueKind.Number && prop.TryGetDouble(out var value))
            {
                return value;
            }
            // Handle string-encoded numbers
            if (prop.ValueKind == JsonValueKind.String)
            {
                var str = prop.GetString();
                if (double.TryParse(str, out var parsed))
                {
                    return parsed;
                }
            }
        }
        return 0;
    }

    #endregion

    #region Escrow Integration Event Publishing

    private async Task PublishContractLockedEventAsync(
        ContractInstanceModel model, Guid guardianId, string guardianType, CancellationToken ct)
    {
        // Parse guardian type from string (API models use string per existing schema)
        if (!Enum.TryParse<EntityType>(guardianType, ignoreCase: true, out var parsedGuardianType))
        {
            _logger.LogWarning("Invalid guardian type '{GuardianType}' when publishing contract locked event for {ContractId}", guardianType, model.ContractId);
            return;
        }

        await _messageBus.TryPublishAsync("contract.locked", new ContractLockedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            ContractId = model.ContractId,
            GuardianId = guardianId,
            GuardianType = parsedGuardianType
        });
    }

    private async Task PublishContractUnlockedEventAsync(
        ContractInstanceModel model, Guid? guardianId, string? guardianType, CancellationToken ct)
    {
        // Parse guardian type from string if present (API models use string per existing schema)
        EntityType? parsedGuardianType = null;
        if (guardianType != null && Enum.TryParse<EntityType>(guardianType, ignoreCase: true, out var parsed))
        {
            parsedGuardianType = parsed;
        }

        await _messageBus.TryPublishAsync("contract.unlocked", new ContractUnlockedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            ContractId = model.ContractId,
            PreviousGuardianId = guardianId,
            PreviousGuardianType = parsedGuardianType
        });
    }

    private async Task PublishPartyTransferredEventAsync(
        ContractInstanceModel model, string role,
        Guid fromEntityId, EntityType fromEntityType,
        Guid toEntityId, EntityType toEntityType,
        CancellationToken ct)
    {
        await _messageBus.TryPublishAsync("contract.party.transferred", new ContractPartyTransferredEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            ContractId = model.ContractId,
            Role = role,
            FromEntityId = fromEntityId,
            FromEntityType = fromEntityType,
            ToEntityId = toEntityId,
            ToEntityType = toEntityType
        });
    }

    private async Task PublishClauseTypeRegisteredEventAsync(ClauseTypeModel model, CancellationToken ct)
    {
        await _messageBus.TryPublishAsync("contract.clausetype.registered", new ClauseTypeRegisteredEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            TypeCode = model.TypeCode,
            Description = model.Description,
            Category = model.Category,
            IsBuiltIn = model.IsBuiltIn
        });
    }

    private async Task PublishTemplateValuesSetEventAsync(
        ContractInstanceModel model, List<string> keys, CancellationToken ct)
    {
        await _messageBus.TryPublishAsync("contract.templatevalues.set", new ContractTemplateValuesSetEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            ContractId = model.ContractId,
            Keys = keys,
            ValueCount = model.TemplateValues?.Count ?? 0
        });
    }

    private async Task PublishContractExecutedEventAsync(
        ContractInstanceModel model, List<DistributionRecordModel> distributions, CancellationToken ct)
    {
        await _messageBus.TryPublishAsync("contract.executed", new ContractExecutedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            ContractId = model.ContractId,
            TemplateCode = model.TemplateCode,
            DistributionCount = distributions.Count,
            DistributionResults = distributions.Select(d => new ClauseDistributionResult
            {
                ClauseId = Guid.TryParse(d.ClauseId, out var cid) ? cid : Guid.Empty,
                ClauseType = d.ClauseType,
                Amount = d.Amount,
                Succeeded = d.Succeeded,
                FailureReason = d.FailureReason
            }).ToList()
        });
    }

    #endregion
}

#region Escrow Integration Internal Models

/// <summary>
/// Internal model for storing clause type definitions.
/// </summary>
internal class ClauseTypeModel
{
    public string TypeCode { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public ClauseCategory Category { get; set; }
    public bool IsBuiltIn { get; set; }
    public ClauseHandlerModel? ValidationHandler { get; set; }
    public ClauseHandlerModel? ExecutionHandler { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// Internal model for clause handler configuration.
/// </summary>
internal class ClauseHandlerModel
{
    public string Service { get; set; } = string.Empty;
    public string Endpoint { get; set; } = string.Empty;
    public object? RequestMapping { get; set; }
    public object? ResponseMapping { get; set; }
}

/// <summary>
/// Internal model for storing distribution records.
/// Contract is a foundational service - no wallet/container IDs (escrow knows the mapping).
/// No assetType - clauseType already implies it (currency_transfer = currency, etc.).
/// </summary>
internal class DistributionRecordModel
{
    public string ClauseId { get; set; } = string.Empty;
    public string ClauseType { get; set; } = string.Empty;
    public double Amount { get; set; }
    public bool Succeeded { get; set; }
    public string? FailureReason { get; set; }
}

/// <summary>
/// Parsed clause definition from template's custom terms.
/// Wraps a JsonElement to provide typed access to clause properties.
/// </summary>
internal class ClauseDefinition
{
    /// <summary>Unique clause identifier.</summary>
    public string Id { get; }

    /// <summary>Clause type code (e.g., asset_requirement, fee, distribution).</summary>
    public string Type { get; }

    private readonly JsonElement _element;

    /// <summary>
    /// Creates a new ClauseDefinition from a parsed JSON element.
    /// </summary>
    public ClauseDefinition(string id, string type, JsonElement element)
    {
        Id = id;
        Type = type;
        _element = element;
    }

    /// <summary>
    /// Gets a string property from the clause definition.
    /// </summary>
    public string? GetProperty(string name)
    {
        if (_element.TryGetProperty(name, out var prop))
        {
            if (prop.ValueKind == JsonValueKind.String)
            {
                return prop.GetString();
            }
            if (prop.ValueKind == JsonValueKind.Number)
            {
                return prop.GetRawText();
            }
        }
        return null;
    }

    /// <summary>
    /// Gets an array of JsonElements from the clause definition.
    /// </summary>
    public List<JsonElement> GetArray(string name)
    {
        var items = new List<JsonElement>();
        if (_element.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in prop.EnumerateArray())
            {
                items.Add(item.Clone());
            }
        }
        return items;
    }
}

#endregion
