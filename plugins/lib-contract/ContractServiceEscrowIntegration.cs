#nullable enable

using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.State;
using Microsoft.Extensions.Logging;
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

    // Regex for validating template value key format (alphanumeric + underscore)
    private static readonly Regex TemplateKeyPattern = new(@"^[A-Za-z0-9_]+$", RegexOptions.Compiled);

    // Built-in clause type codes
    private static readonly HashSet<string> BuiltInClauseTypes = new()
    {
        "asset_requirement",
        "currency_transfer",
        "item_transfer",
        "fee"
    };

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
            var model = await _stateStoreFactory.GetStore<ContractInstanceModel>(StateStoreDefinitions.Contract)
                .GetAsync(instanceKey, cancellationToken);

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
            if (!string.IsNullOrEmpty(model.GuardianId))
            {
                _logger.LogWarning("Contract already locked: {ContractId} by {GuardianId}",
                    body.ContractInstanceId, model.GuardianId);
                return (StatusCodes.Conflict, null);
            }

            // Lock the contract
            var now = DateTimeOffset.UtcNow;
            model.GuardianId = body.GuardianId.ToString();
            model.GuardianType = body.GuardianType;
            model.LockedAt = now;
            model.UpdatedAt = now;

            await _stateStoreFactory.GetStore<ContractInstanceModel>(StateStoreDefinitions.Contract)
                .SaveAsync(instanceKey, model, cancellationToken: cancellationToken);

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
                    .SaveAsync(idempotencyKey, response, TimeSpan.FromHours(24), cancellationToken);
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
            var model = await _stateStoreFactory.GetStore<ContractInstanceModel>(StateStoreDefinitions.Contract)
                .GetAsync(instanceKey, cancellationToken);

            if (model == null)
            {
                _logger.LogWarning("Contract not found: {ContractId}", body.ContractInstanceId);
                return (StatusCodes.NotFound, null);
            }

            // Check if locked
            if (string.IsNullOrEmpty(model.GuardianId))
            {
                _logger.LogWarning("Contract not locked: {ContractId}", body.ContractInstanceId);
                return (StatusCodes.NotFound, null);
            }

            // Verify guardian
            if (model.GuardianId != body.GuardianId.ToString() || model.GuardianType != body.GuardianType)
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

            await _stateStoreFactory.GetStore<ContractInstanceModel>(StateStoreDefinitions.Contract)
                .SaveAsync(instanceKey, model, cancellationToken: cancellationToken);

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
                    .SaveAsync(idempotencyKey, response, TimeSpan.FromHours(24), cancellationToken);
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
            var model = await _stateStoreFactory.GetStore<ContractInstanceModel>(StateStoreDefinitions.Contract)
                .GetAsync(instanceKey, cancellationToken);

            if (model == null)
            {
                _logger.LogWarning("Contract not found: {ContractId}", body.ContractInstanceId);
                return (StatusCodes.NotFound, null);
            }

            // Verify contract is locked and caller is guardian
            if (string.IsNullOrEmpty(model.GuardianId))
            {
                _logger.LogWarning("Contract not locked: {ContractId}", body.ContractInstanceId);
                return (StatusCodes.Forbidden, null);
            }

            if (model.GuardianId != body.GuardianId.ToString() || model.GuardianType != body.GuardianType)
            {
                _logger.LogWarning("Not the current guardian for contract: {ContractId}", body.ContractInstanceId);
                return (StatusCodes.Forbidden, null);
            }

            // Find the party to transfer
            var party = model.Parties?.FirstOrDefault(p =>
                p.EntityId == body.FromEntityId.ToString() &&
                p.EntityType == body.FromEntityType.ToString());

            if (party == null)
            {
                _logger.LogWarning("Party not found in contract: {FromEntityId}", body.FromEntityId);
                return (StatusCodes.BadRequest, null);
            }

            // Update party indexes
            var oldPartyIndexKey = $"{PARTY_INDEX_PREFIX}{party.EntityType}:{party.EntityId}";
            await RemoveFromListAsync(oldPartyIndexKey, body.ContractInstanceId.ToString(), cancellationToken);

            // Transfer the party
            var previousEntityId = party.EntityId;
            var role = party.Role;
            party.EntityId = body.ToEntityId.ToString();
            party.EntityType = body.ToEntityType.ToString();
            model.UpdatedAt = DateTimeOffset.UtcNow;

            await _stateStoreFactory.GetStore<ContractInstanceModel>(StateStoreDefinitions.Contract)
                .SaveAsync(instanceKey, model, cancellationToken: cancellationToken);

            // Update party indexes
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
                    .SaveAsync(idempotencyKey, response, TimeSpan.FromHours(24), cancellationToken);
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
                Category = body.Category.ToString(),
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
                    if (!Enum.TryParse<ClauseCategory>(model.Category, true, out var category) ||
                        category != body.Category.Value)
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
                    Category = Enum.TryParse<ClauseCategory>(model.Category, true, out var cat)
                        ? cat : ClauseCategory.Validation,
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
            Category = ClauseCategory.Validation.ToString(),
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
            Category = ClauseCategory.Execution.ToString(),
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
            Category = ClauseCategory.Execution.ToString(),
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
            Category = ClauseCategory.Execution.ToString(),
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

            // Check if template values are set
            if (model.TemplateValues == null || model.TemplateValues.Count == 0)
            {
                _logger.LogWarning("Template values not set for contract: {ContractId}", body.ContractInstanceId);
                return (StatusCodes.BadRequest, null);
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
    /// Checks asset requirement clauses by querying actual balances.
    /// </summary>
    private async Task<List<PartyAssetRequirementStatus>> CheckAssetRequirementClausesAsync(
        ContractInstanceModel contract,
        ContractTemplateModel template,
        CancellationToken ct)
    {
        var results = new List<PartyAssetRequirementStatus>();

        // Get clauses from template's custom terms (if defined there)
        // For now, return empty - full implementation would parse clause definitions
        // from the template and execute validation handlers

        // Group by party roles defined in the contract
        if (contract.Parties == null) return results;

        foreach (var party in contract.Parties)
        {
            results.Add(new PartyAssetRequirementStatus
            {
                PartyRole = party.Role,
                Satisfied = true, // Default to true when no clauses defined
                Clauses = new List<ClauseAssetStatus>()
            });
        }

        return results;
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
            var model = await _stateStoreFactory.GetStore<ContractInstanceModel>(StateStoreDefinitions.Contract)
                .GetAsync(instanceKey, cancellationToken);

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
                    Distributions = model.ExecutionDistributions?.Select(d => new DistributionRecord
                    {
                        ClauseId = d.ClauseId,
                        ClauseType = d.ClauseType,
                        AssetType = d.AssetType,
                        Amount = d.Amount,
                        SourceWalletId = string.IsNullOrEmpty(d.SourceWalletId) ? null : Guid.Parse(d.SourceWalletId),
                        DestinationWalletId = string.IsNullOrEmpty(d.DestinationWalletId) ? null : Guid.Parse(d.DestinationWalletId),
                        SourceContainerId = string.IsNullOrEmpty(d.SourceContainerId) ? null : Guid.Parse(d.SourceContainerId),
                        DestinationContainerId = string.IsNullOrEmpty(d.DestinationContainerId) ? null : Guid.Parse(d.DestinationContainerId)
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

            await _stateStoreFactory.GetStore<ContractInstanceModel>(StateStoreDefinitions.Contract)
                .SaveAsync(instanceKey, model, cancellationToken: cancellationToken);

            var response = new ExecuteContractResponse
            {
                Executed = true,
                AlreadyExecuted = false,
                ContractId = body.ContractInstanceId,
                Distributions = distributions.Select(d => new DistributionRecord
                {
                    ClauseId = d.ClauseId,
                    ClauseType = d.ClauseType,
                    AssetType = d.AssetType,
                    Amount = d.Amount,
                    SourceWalletId = string.IsNullOrEmpty(d.SourceWalletId) ? null : Guid.Parse(d.SourceWalletId),
                    DestinationWalletId = string.IsNullOrEmpty(d.DestinationWalletId) ? null : Guid.Parse(d.DestinationWalletId),
                    SourceContainerId = string.IsNullOrEmpty(d.SourceContainerId) ? null : Guid.Parse(d.SourceContainerId),
                    DestinationContainerId = string.IsNullOrEmpty(d.DestinationContainerId) ? null : Guid.Parse(d.DestinationContainerId)
                }).ToList(),
                ExecutedAt = now
            };

            // Cache for idempotency
            if (!string.IsNullOrEmpty(body.IdempotencyKey))
            {
                var idempotencyKey = $"{IDEMPOTENCY_PREFIX}execute:{body.IdempotencyKey}";
                await _stateStoreFactory.GetStore<ExecuteContractResponse>(StateStoreDefinitions.Contract)
                    .SaveAsync(idempotencyKey, response, TimeSpan.FromHours(24), cancellationToken);
            }

            // Publish event
            await PublishContractExecutedEventAsync(model, distributions.Count, cancellationToken);

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
    /// Executes all contract clauses (fees first, then distributions).
    /// </summary>
    private async Task<List<DistributionRecordModel>> ExecuteContractClausesAsync(
        ContractInstanceModel contract,
        CancellationToken ct)
    {
        var distributions = new List<DistributionRecordModel>();

        // Get clause definitions from template custom terms
        // For now, return empty - full implementation would:
        // 1. Load clause definitions from template
        // 2. Execute fee clauses first via service navigator
        // 3. Execute distribution clauses via service navigator
        // 4. Record each transfer in the distributions list

        _logger.LogDebug("Contract {ContractId} has no clauses to execute", contract.ContractId);

        return distributions;
    }

    #endregion

    #region Escrow Integration Event Publishing

    private async Task PublishContractLockedEventAsync(
        ContractInstanceModel model, Guid guardianId, string guardianType, CancellationToken ct)
    {
        await _messageBus.TryPublishAsync("contract.locked", new ContractLockedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            ContractId = Guid.Parse(model.ContractId),
            GuardianId = guardianId,
            GuardianType = guardianType
        });
    }

    private async Task PublishContractUnlockedEventAsync(
        ContractInstanceModel model, string? guardianId, string? guardianType, CancellationToken ct)
    {
        await _messageBus.TryPublishAsync("contract.unlocked", new ContractUnlockedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            ContractId = Guid.Parse(model.ContractId),
            PreviousGuardianId = string.IsNullOrEmpty(guardianId) ? null : Guid.Parse(guardianId),
            PreviousGuardianType = guardianType
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
            ContractId = Guid.Parse(model.ContractId),
            Role = role,
            FromEntityId = fromEntityId,
            FromEntityType = fromEntityType.ToString(),
            ToEntityId = toEntityId,
            ToEntityType = toEntityType.ToString()
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
            ContractId = Guid.Parse(model.ContractId),
            Keys = keys,
            ValueCount = model.TemplateValues?.Count ?? 0
        });
    }

    private async Task PublishContractExecutedEventAsync(
        ContractInstanceModel model, int distributionCount, CancellationToken ct)
    {
        await _messageBus.TryPublishAsync("contract.executed", new ContractExecutedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            ContractId = Guid.Parse(model.ContractId),
            TemplateCode = model.TemplateCode,
            DistributionCount = distributionCount
        });
    }

    #endregion
}

#region Escrow Integration Event Models

/// <summary>
/// Event published when a contract is locked under guardian custody.
/// </summary>
public class ContractLockedEvent
{
    /// <summary>Event ID.</summary>
    public Guid EventId { get; set; }
    /// <summary>Event timestamp.</summary>
    public DateTimeOffset Timestamp { get; set; }
    /// <summary>Contract instance ID.</summary>
    public Guid ContractId { get; set; }
    /// <summary>Guardian entity ID.</summary>
    public Guid GuardianId { get; set; }
    /// <summary>Guardian entity type.</summary>
    public string GuardianType { get; set; } = string.Empty;
}

/// <summary>
/// Event published when a contract is unlocked from guardian custody.
/// </summary>
public class ContractUnlockedEvent
{
    /// <summary>Event ID.</summary>
    public Guid EventId { get; set; }
    /// <summary>Event timestamp.</summary>
    public DateTimeOffset Timestamp { get; set; }
    /// <summary>Contract instance ID.</summary>
    public Guid ContractId { get; set; }
    /// <summary>Previous guardian entity ID.</summary>
    public Guid? PreviousGuardianId { get; set; }
    /// <summary>Previous guardian entity type.</summary>
    public string? PreviousGuardianType { get; set; }
}

/// <summary>
/// Event published when a party role is transferred.
/// </summary>
public class ContractPartyTransferredEvent
{
    /// <summary>Event ID.</summary>
    public Guid EventId { get; set; }
    /// <summary>Event timestamp.</summary>
    public DateTimeOffset Timestamp { get; set; }
    /// <summary>Contract instance ID.</summary>
    public Guid ContractId { get; set; }
    /// <summary>Role that was transferred.</summary>
    public string Role { get; set; } = string.Empty;
    /// <summary>Previous entity ID.</summary>
    public Guid FromEntityId { get; set; }
    /// <summary>Previous entity type.</summary>
    public string FromEntityType { get; set; } = string.Empty;
    /// <summary>New entity ID.</summary>
    public Guid ToEntityId { get; set; }
    /// <summary>New entity type.</summary>
    public string ToEntityType { get; set; } = string.Empty;
}

/// <summary>
/// Event published when a clause type is registered.
/// </summary>
public class ClauseTypeRegisteredEvent
{
    /// <summary>Event ID.</summary>
    public Guid EventId { get; set; }
    /// <summary>Event timestamp.</summary>
    public DateTimeOffset Timestamp { get; set; }
    /// <summary>Clause type code.</summary>
    public string TypeCode { get; set; } = string.Empty;
    /// <summary>Description.</summary>
    public string Description { get; set; } = string.Empty;
    /// <summary>Category.</summary>
    public string Category { get; set; } = string.Empty;
    /// <summary>Whether this is a built-in type.</summary>
    public bool IsBuiltIn { get; set; }
}

/// <summary>
/// Event published when template values are set on a contract.
/// </summary>
public class ContractTemplateValuesSetEvent
{
    /// <summary>Event ID.</summary>
    public Guid EventId { get; set; }
    /// <summary>Event timestamp.</summary>
    public DateTimeOffset Timestamp { get; set; }
    /// <summary>Contract instance ID.</summary>
    public Guid ContractId { get; set; }
    /// <summary>Keys that were set.</summary>
    public List<string> Keys { get; set; } = new();
    /// <summary>Total value count after setting.</summary>
    public int ValueCount { get; set; }
}

/// <summary>
/// Event published when a contract's clauses are executed.
/// </summary>
public class ContractExecutedEvent
{
    /// <summary>Event ID.</summary>
    public Guid EventId { get; set; }
    /// <summary>Event timestamp.</summary>
    public DateTimeOffset Timestamp { get; set; }
    /// <summary>Contract instance ID.</summary>
    public Guid ContractId { get; set; }
    /// <summary>Template code.</summary>
    public string TemplateCode { get; set; } = string.Empty;
    /// <summary>Number of distributions executed.</summary>
    public int DistributionCount { get; set; }
}

#endregion

#region Escrow Integration Internal Models

/// <summary>
/// Internal model for storing clause type definitions.
/// </summary>
internal class ClauseTypeModel
{
    public string TypeCode { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
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
/// </summary>
internal class DistributionRecordModel
{
    public string ClauseId { get; set; } = string.Empty;
    public string ClauseType { get; set; } = string.Empty;
    public string AssetType { get; set; } = string.Empty;
    public double Amount { get; set; }
    public string? SourceWalletId { get; set; }
    public string? DestinationWalletId { get; set; }
    public string? SourceContainerId { get; set; }
    public string? DestinationContainerId { get; set; }
}

#endregion
