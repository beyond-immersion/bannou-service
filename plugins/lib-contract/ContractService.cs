using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.ServiceClients;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;
using System.Text.Json;

[assembly: InternalsVisibleTo("lib-contract.tests")]
[assembly: InternalsVisibleTo("DynamicProxyGenAssembly2")]

namespace BeyondImmersion.BannouService.Contract;

/// <summary>
/// Implementation of the Contract service.
/// Manages binding agreements between entities with milestone-based progression.
/// </summary>
/// <remarks>
/// <para>
/// <b>Design Principle</b>: Contracts are reactive, not proactive.
/// External systems tell contracts when conditions are met/failed via API calls.
/// Contracts store state, emit events, and execute prebound APIs on state transitions.
/// </para>
/// </remarks>
[BannouService("contract", typeof(IContractService), lifetime: ServiceLifetime.Scoped)]
public partial class ContractService : IContractService
{
    private readonly IMessageBus _messageBus;
    private readonly IServiceNavigator _navigator;
    private readonly IStateStoreFactory _stateStoreFactory;
    private readonly ILogger<ContractService> _logger;
    private readonly ContractServiceConfiguration _configuration;

    // State store key prefixes
    private const string TEMPLATE_PREFIX = "template:";
    private const string TEMPLATE_CODE_INDEX = "template-code:";
    private const string INSTANCE_PREFIX = "instance:";
    private const string BREACH_PREFIX = "breach:";
    private const string PARTY_INDEX_PREFIX = "party-idx:";
    private const string TEMPLATE_INDEX_PREFIX = "template-idx:";
    private const string STATUS_INDEX_PREFIX = "status-idx:";
    private const string ALL_TEMPLATES_KEY = "all-templates";

    /// <summary>
    /// Initializes a new instance of the ContractService.
    /// </summary>
    public ContractService(
        IMessageBus messageBus,
        IServiceNavigator navigator,
        IStateStoreFactory stateStoreFactory,
        ILogger<ContractService> logger,
        ContractServiceConfiguration configuration,
        IEventConsumer eventConsumer)
    {
        _messageBus = messageBus;
        _navigator = navigator;
        _stateStoreFactory = stateStoreFactory;
        _logger = logger;
        _configuration = configuration;

        // Register event handlers via partial class if needed
        ((IBannouService)this).RegisterEventConsumers(eventConsumer);
    }

    #region Template Operations

    /// <inheritdoc/>
    public async Task<(StatusCodes, ContractTemplateResponse?)> CreateContractTemplateAsync(
        CreateContractTemplateRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Creating contract template with code: {Code}", body.Code);

            // Check if template code already exists
            var codeIndexKey = $"{TEMPLATE_CODE_INDEX}{body.Code}";
            var existingId = await _stateStoreFactory.GetStore<string>(StateStoreDefinitions.Contract)
                .GetAsync(codeIndexKey, cancellationToken);

            if (!string.IsNullOrEmpty(existingId))
            {
                _logger.LogWarning("Template with code already exists: {Code}", body.Code);
                return (StatusCodes.Conflict, null);
            }

            var templateId = Guid.NewGuid();
            var now = DateTimeOffset.UtcNow;

            var model = new ContractTemplateModel
            {
                TemplateId = templateId.ToString(),
                Code = body.Code,
                Name = body.Name,
                Description = body.Description,
                RealmId = body.RealmId?.ToString(),
                MinParties = body.MinParties,
                MaxParties = body.MaxParties,
                PartyRoles = body.PartyRoles?.Select(r => new PartyRoleModel
                {
                    Role = r.Role,
                    MinCount = r.MinCount,
                    MaxCount = r.MaxCount,
                    AllowedEntityTypes = r.AllowedEntityTypes?.Select(e => e.ToString()).ToList()
                }).ToList() ?? new List<PartyRoleModel>(),
                DefaultTerms = MapTermsToModel(body.DefaultTerms),
                Milestones = body.Milestones?.Select(m => new MilestoneDefinitionModel
                {
                    Code = m.Code,
                    Name = m.Name,
                    Description = m.Description,
                    Sequence = m.Sequence,
                    Required = m.Required,
                    Deadline = m.Deadline,
                    OnComplete = m.OnComplete?.Select(MapPreboundApiToModel).ToList(),
                    OnExpire = m.OnExpire?.Select(MapPreboundApiToModel).ToList()
                }).ToList(),
                DefaultEnforcementMode = body.DefaultEnforcementMode.ToString(),
                Transferable = body.Transferable,
                GameMetadata = body.GameMetadata,
                IsActive = true,
                CreatedAt = now,
                UpdatedAt = now
            };

            // Save template
            var templateKey = $"{TEMPLATE_PREFIX}{templateId}";
            await _stateStoreFactory.GetStore<ContractTemplateModel>(StateStoreDefinitions.Contract)
                .SaveAsync(templateKey, model, cancellationToken: cancellationToken);

            // Save code index
            await _stateStoreFactory.GetStore<string>(StateStoreDefinitions.Contract)
                .SaveAsync(codeIndexKey, templateId.ToString(), cancellationToken: cancellationToken);

            // Add to all templates list
            await AddToListAsync(ALL_TEMPLATES_KEY, templateId.ToString(), cancellationToken);

            // Publish event
            await PublishTemplateCreatedEventAsync(model, cancellationToken);

            _logger.LogInformation("Created contract template: {TemplateId} with code {Code}", templateId, body.Code);
            return (StatusCodes.OK, MapTemplateToResponse(model));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating contract template");
            await EmitErrorAsync("CreateContractTemplate", "post:/contract/template/create", ex);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, ContractTemplateResponse?)> GetContractTemplateAsync(
        GetContractTemplateRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            string? templateId = body.TemplateId?.ToString();

            // If code provided, look up template ID
            if (string.IsNullOrEmpty(templateId) && !string.IsNullOrEmpty(body.Code))
            {
                var codeIndexKey = $"{TEMPLATE_CODE_INDEX}{body.Code}";
                templateId = await _stateStoreFactory.GetStore<string>(StateStoreDefinitions.Contract)
                    .GetAsync(codeIndexKey, cancellationToken);
            }

            if (string.IsNullOrEmpty(templateId))
            {
                _logger.LogWarning("Template not found - no ID or code provided, or code not found");
                return (StatusCodes.BadRequest, null);
            }

            var templateKey = $"{TEMPLATE_PREFIX}{templateId}";
            var model = await _stateStoreFactory.GetStore<ContractTemplateModel>(StateStoreDefinitions.Contract)
                .GetAsync(templateKey, cancellationToken);

            if (model == null)
            {
                _logger.LogWarning("Template not found: {TemplateId}", templateId);
                return (StatusCodes.NotFound, null);
            }

            return (StatusCodes.OK, MapTemplateToResponse(model));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting contract template");
            await EmitErrorAsync("GetContractTemplate", "post:/contract/template/get", ex);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, ListContractTemplatesResponse?)> ListContractTemplatesAsync(
        ListContractTemplatesRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Listing contract templates");

            // Get all template IDs
            var allTemplateIds = await _stateStoreFactory.GetStore<List<string>>(StateStoreDefinitions.Contract)
                .GetAsync(ALL_TEMPLATES_KEY, cancellationToken) ?? new List<string>();

            if (allTemplateIds.Count == 0)
            {
                return (StatusCodes.OK, new ListContractTemplatesResponse
                {
                    Templates = new List<ContractTemplateResponse>(),
                    TotalCount = 0,
                    Page = body.Page,
                    PageSize = body.PageSize,
                    HasNextPage = false
                });
            }

            // Load all templates
            var keys = allTemplateIds.Select(id => $"{TEMPLATE_PREFIX}{id}").ToList();
            var bulkResults = await _stateStoreFactory.GetStore<ContractTemplateModel>(StateStoreDefinitions.Contract)
                .GetBulkAsync(keys, cancellationToken);

            var templates = new List<ContractTemplateModel>();
            foreach (var (key, model) in bulkResults)
            {
                if (model != null) templates.Add(model);
            }

            // Apply filters
            var filtered = templates.AsEnumerable();

            if (body.RealmId.HasValue)
            {
                var realmIdStr = body.RealmId.Value.ToString();
                filtered = filtered.Where(t => t.RealmId == realmIdStr || t.RealmId == null);
            }

            if (body.IsActive.HasValue)
            {
                filtered = filtered.Where(t => t.IsActive == body.IsActive.Value);
            }

            if (!string.IsNullOrEmpty(body.SearchTerm))
            {
                var searchLower = body.SearchTerm.ToLowerInvariant();
                filtered = filtered.Where(t =>
                    t.Name.ToLowerInvariant().Contains(searchLower) ||
                    (t.Description?.ToLowerInvariant().Contains(searchLower) ?? false));
            }

            // Pagination
            var totalCount = filtered.Count();
            var page = body.Page;
            var pageSize = body.PageSize;
            var paged = filtered
                .OrderByDescending(t => t.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList();

            return (StatusCodes.OK, new ListContractTemplatesResponse
            {
                Templates = paged.Select(MapTemplateToResponse).ToList(),
                TotalCount = totalCount,
                Page = page,
                PageSize = pageSize,
                HasNextPage = (page * pageSize) < totalCount
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing contract templates");
            await EmitErrorAsync("ListContractTemplates", "post:/contract/template/list", ex);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, ContractTemplateResponse?)> UpdateContractTemplateAsync(
        UpdateContractTemplateRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Updating contract template: {TemplateId}", body.TemplateId);

            var templateKey = $"{TEMPLATE_PREFIX}{body.TemplateId}";
            var model = await _stateStoreFactory.GetStore<ContractTemplateModel>(StateStoreDefinitions.Contract)
                .GetAsync(templateKey, cancellationToken);

            if (model == null)
            {
                _logger.LogWarning("Template not found: {TemplateId}", body.TemplateId);
                return (StatusCodes.NotFound, null);
            }

            var changedFields = new List<string>();

            if (!string.IsNullOrEmpty(body.Name) && body.Name != model.Name)
            {
                model.Name = body.Name;
                changedFields.Add("name");
            }

            if (body.Description != null && body.Description != model.Description)
            {
                model.Description = body.Description;
                changedFields.Add("description");
            }

            if (body.IsActive.HasValue && body.IsActive.Value != model.IsActive)
            {
                model.IsActive = body.IsActive.Value;
                changedFields.Add("isActive");
            }

            if (body.GameMetadata != null)
            {
                model.GameMetadata = body.GameMetadata;
                changedFields.Add("gameMetadata");
            }

            if (changedFields.Count > 0)
            {
                model.UpdatedAt = DateTimeOffset.UtcNow;
                await _stateStoreFactory.GetStore<ContractTemplateModel>(StateStoreDefinitions.Contract)
                    .SaveAsync(templateKey, model, cancellationToken: cancellationToken);

                await PublishTemplateUpdatedEventAsync(model, changedFields, cancellationToken);
            }

            return (StatusCodes.OK, MapTemplateToResponse(model));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating contract template: {TemplateId}", body.TemplateId);
            await EmitErrorAsync("UpdateContractTemplate", "post:/contract/template/update", ex);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <inheritdoc/>
    public async Task<StatusCodes> DeleteContractTemplateAsync(
        DeleteContractTemplateRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Deleting contract template: {TemplateId}", body.TemplateId);

            var templateKey = $"{TEMPLATE_PREFIX}{body.TemplateId}";
            var model = await _stateStoreFactory.GetStore<ContractTemplateModel>(StateStoreDefinitions.Contract)
                .GetAsync(templateKey, cancellationToken);

            if (model == null)
            {
                _logger.LogWarning("Template not found: {TemplateId}", body.TemplateId);
                return StatusCodes.NotFound;
            }

            // Check for active instances
            var templateIndexKey = $"{TEMPLATE_INDEX_PREFIX}{body.TemplateId}";
            var instanceIds = await _stateStoreFactory.GetStore<List<string>>(StateStoreDefinitions.Contract)
                .GetAsync(templateIndexKey, cancellationToken) ?? new List<string>();

            if (instanceIds.Count > 0)
            {
                // Check if any are active
                var instanceKeys = instanceIds.Select(id => $"{INSTANCE_PREFIX}{id}").ToList();
                var instances = await _stateStoreFactory.GetStore<ContractInstanceModel>(StateStoreDefinitions.Contract)
                    .GetBulkAsync(instanceKeys, cancellationToken);

                var activeStatuses = new[] { "draft", "proposed", "pending", "active" };
                if (instances.Any(i => i.Value != null && activeStatuses.Contains(i.Value.Status)))
                {
                    _logger.LogWarning("Cannot delete template with active instances: {TemplateId}", body.TemplateId);
                    return StatusCodes.Conflict;
                }
            }

            // Soft delete - mark as inactive
            model.IsActive = false;
            model.UpdatedAt = DateTimeOffset.UtcNow;
            await _stateStoreFactory.GetStore<ContractTemplateModel>(StateStoreDefinitions.Contract)
                .SaveAsync(templateKey, model, cancellationToken: cancellationToken);

            await PublishTemplateDeletedEventAsync(model, cancellationToken);

            _logger.LogInformation("Deleted (soft) contract template: {TemplateId}", body.TemplateId);
            return StatusCodes.OK;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting contract template: {TemplateId}", body.TemplateId);
            await EmitErrorAsync("DeleteContractTemplate", "post:/contract/template/delete", ex);
            return StatusCodes.InternalServerError;
        }
    }

    #endregion

    #region Instance Operations

    /// <inheritdoc/>
    public async Task<(StatusCodes, ContractInstanceResponse?)> CreateContractInstanceAsync(
        CreateContractInstanceRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Creating contract instance from template: {TemplateId}", body.TemplateId);

            // Load template
            var templateKey = $"{TEMPLATE_PREFIX}{body.TemplateId}";
            var template = await _stateStoreFactory.GetStore<ContractTemplateModel>(StateStoreDefinitions.Contract)
                .GetAsync(templateKey, cancellationToken);

            if (template == null)
            {
                _logger.LogWarning("Template not found: {TemplateId}", body.TemplateId);
                return (StatusCodes.NotFound, null);
            }

            if (!template.IsActive)
            {
                _logger.LogWarning("Template is not active: {TemplateId}", body.TemplateId);
                return (StatusCodes.BadRequest, null);
            }

            // Validate party count
            if (body.Parties.Count < template.MinParties || body.Parties.Count > template.MaxParties)
            {
                _logger.LogWarning("Invalid party count: {Count}, expected {Min}-{Max}",
                    body.Parties.Count, template.MinParties, template.MaxParties);
                return (StatusCodes.BadRequest, null);
            }

            var contractId = Guid.NewGuid();
            var now = DateTimeOffset.UtcNow;

            // Create parties with pending consent
            var parties = body.Parties.Select(p => new ContractPartyModel
            {
                EntityId = p.EntityId.ToString(),
                EntityType = p.EntityType.ToString(),
                Role = p.Role,
                ConsentStatus = "pending",
                ConsentedAt = null
            }).ToList();

            // Merge terms from template and request
            var mergedTerms = MergeTerms(template.DefaultTerms, MapTermsToModel(body.Terms));

            // Create milestone instances from template
            var milestones = template.Milestones?.Select(m => new MilestoneInstanceModel
            {
                Code = m.Code,
                Name = m.Name,
                Description = m.Description,
                Sequence = m.Sequence,
                Required = m.Required,
                Status = "pending",
                Deadline = m.Deadline,
                OnComplete = m.OnComplete,
                OnExpire = m.OnExpire
            }).ToList();

            var model = new ContractInstanceModel
            {
                ContractId = contractId.ToString(),
                TemplateId = body.TemplateId.ToString(),
                TemplateCode = template.Code,
                Status = "draft",
                Parties = parties,
                Terms = mergedTerms,
                Milestones = milestones,
                CurrentMilestoneIndex = 0,
                EscrowIds = body.EscrowIds?.Select(e => e.ToString()).ToList(),
                EffectiveFrom = body.EffectiveFrom,
                EffectiveUntil = body.EffectiveUntil,
                GameMetadata = body.GameMetadata != null ? new GameMetadataModel
                {
                    InstanceData = body.GameMetadata
                } : null,
                CreatedAt = now,
                UpdatedAt = now
            };

            // Save instance
            var instanceKey = $"{INSTANCE_PREFIX}{contractId}";
            await _stateStoreFactory.GetStore<ContractInstanceModel>(StateStoreDefinitions.Contract)
                .SaveAsync(instanceKey, model, cancellationToken: cancellationToken);

            // Update indexes
            await AddToListAsync($"{TEMPLATE_INDEX_PREFIX}{body.TemplateId}", contractId.ToString(), cancellationToken);
            await AddToListAsync($"{STATUS_INDEX_PREFIX}draft", contractId.ToString(), cancellationToken);

            foreach (var party in parties)
            {
                await AddToListAsync($"{PARTY_INDEX_PREFIX}{party.EntityType}:{party.EntityId}", contractId.ToString(), cancellationToken);
            }

            // Publish event
            await PublishInstanceCreatedEventAsync(model, cancellationToken);

            _logger.LogInformation("Created contract instance: {ContractId}", contractId);
            return (StatusCodes.OK, MapInstanceToResponse(model));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating contract instance");
            await EmitErrorAsync("CreateContractInstance", "post:/contract/instance/create", ex);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, ContractInstanceResponse?)> ProposeContractInstanceAsync(
        ProposeContractInstanceRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Proposing contract: {ContractId}", body.ContractId);

            var instanceKey = $"{INSTANCE_PREFIX}{body.ContractId}";
            var model = await _stateStoreFactory.GetStore<ContractInstanceModel>(StateStoreDefinitions.Contract)
                .GetAsync(instanceKey, cancellationToken);

            if (model == null)
            {
                return (StatusCodes.NotFound, null);
            }

            if (model.Status != "draft")
            {
                _logger.LogWarning("Contract not in draft status: {ContractId}, status: {Status}",
                    body.ContractId, model.Status);
                return (StatusCodes.BadRequest, null);
            }

            // Update status
            await RemoveFromListAsync($"{STATUS_INDEX_PREFIX}draft", body.ContractId.ToString(), cancellationToken);

            model.Status = "proposed";
            model.ProposedAt = DateTimeOffset.UtcNow;
            model.UpdatedAt = DateTimeOffset.UtcNow;

            await _stateStoreFactory.GetStore<ContractInstanceModel>(StateStoreDefinitions.Contract)
                .SaveAsync(instanceKey, model, cancellationToken: cancellationToken);

            await AddToListAsync($"{STATUS_INDEX_PREFIX}proposed", body.ContractId.ToString(), cancellationToken);

            // Publish event
            await PublishContractProposedEventAsync(model, cancellationToken);

            _logger.LogInformation("Proposed contract: {ContractId}", body.ContractId);
            return (StatusCodes.OK, MapInstanceToResponse(model));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error proposing contract: {ContractId}", body.ContractId);
            await EmitErrorAsync("ProposeContractInstance", "post:/contract/instance/propose", ex);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, ContractInstanceResponse?)> ConsentToContractAsync(
        ConsentToContractRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Recording consent for contract: {ContractId} from {EntityId}",
                body.ContractId, body.PartyEntityId);

            var instanceKey = $"{INSTANCE_PREFIX}{body.ContractId}";
            var model = await _stateStoreFactory.GetStore<ContractInstanceModel>(StateStoreDefinitions.Contract)
                .GetAsync(instanceKey, cancellationToken);

            if (model == null)
            {
                return (StatusCodes.NotFound, null);
            }

            if (model.Status != "proposed")
            {
                _logger.LogWarning("Contract not in proposed status: {ContractId}", body.ContractId);
                return (StatusCodes.BadRequest, null);
            }

            // Find party
            var party = model.Parties?.FirstOrDefault(p =>
                p.EntityId == body.PartyEntityId.ToString() &&
                p.EntityType == body.PartyEntityType.ToString());

            if (party == null)
            {
                _logger.LogWarning("Party not found in contract: {EntityId}", body.PartyEntityId);
                return (StatusCodes.BadRequest, null);
            }

            if (party.ConsentStatus == "consented")
            {
                _logger.LogWarning("Party already consented: {EntityId}", body.PartyEntityId);
                return (StatusCodes.BadRequest, null);
            }

            // Record consent
            party.ConsentStatus = "consented";
            party.ConsentedAt = DateTimeOffset.UtcNow;
            model.UpdatedAt = DateTimeOffset.UtcNow;

            // Publish consent received event
            var remainingConsents = model.Parties?.Count(p => p.ConsentStatus != "consented") ?? 0;
            await PublishConsentReceivedEventAsync(model, party, remainingConsents, cancellationToken);

            // Check if all parties have consented
            var allConsented = model.Parties?.All(p => p.ConsentStatus == "consented") ?? false;

            if (allConsented)
            {
                await RemoveFromListAsync($"{STATUS_INDEX_PREFIX}proposed", body.ContractId.ToString(), cancellationToken);

                model.AcceptedAt = DateTimeOffset.UtcNow;

                // Check if we should activate immediately or wait for effectiveFrom
                if (model.EffectiveFrom == null || model.EffectiveFrom <= DateTimeOffset.UtcNow)
                {
                    model.Status = "active";
                    model.EffectiveFrom = DateTimeOffset.UtcNow;
                    await AddToListAsync($"{STATUS_INDEX_PREFIX}active", body.ContractId.ToString(), cancellationToken);

                    // Activate first milestone if any
                    if (model.Milestones?.Count > 0)
                    {
                        model.Milestones[0].Status = "active";
                    }

                    await PublishContractActivatedEventAsync(model, cancellationToken);
                }
                else
                {
                    model.Status = "pending";
                    await AddToListAsync($"{STATUS_INDEX_PREFIX}pending", body.ContractId.ToString(), cancellationToken);
                }

                await PublishContractAcceptedEventAsync(model, cancellationToken);
            }

            await _stateStoreFactory.GetStore<ContractInstanceModel>(StateStoreDefinitions.Contract)
                .SaveAsync(instanceKey, model, cancellationToken: cancellationToken);

            return (StatusCodes.OK, MapInstanceToResponse(model));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error recording consent: {ContractId}", body.ContractId);
            await EmitErrorAsync("ConsentToContract", "post:/contract/instance/consent", ex);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, ContractInstanceResponse?)> GetContractInstanceAsync(
        GetContractInstanceRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var instanceKey = $"{INSTANCE_PREFIX}{body.ContractId}";
            var model = await _stateStoreFactory.GetStore<ContractInstanceModel>(StateStoreDefinitions.Contract)
                .GetAsync(instanceKey, cancellationToken);

            if (model == null)
            {
                return (StatusCodes.NotFound, null);
            }

            return (StatusCodes.OK, MapInstanceToResponse(model));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting contract instance: {ContractId}", body.ContractId);
            await EmitErrorAsync("GetContractInstance", "post:/contract/instance/get", ex);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, QueryContractInstancesResponse?)> QueryContractInstancesAsync(
        QueryContractInstancesRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Querying contract instances");

            List<string> contractIds;

            // Determine which index to use
            if (body.PartyEntityId.HasValue && body.PartyEntityType.HasValue)
            {
                var partyIndexKey = $"{PARTY_INDEX_PREFIX}{body.PartyEntityType}:{body.PartyEntityId}";
                contractIds = await _stateStoreFactory.GetStore<List<string>>(StateStoreDefinitions.Contract)
                    .GetAsync(partyIndexKey, cancellationToken) ?? new List<string>();
            }
            else if (body.TemplateId.HasValue)
            {
                var templateIndexKey = $"{TEMPLATE_INDEX_PREFIX}{body.TemplateId}";
                contractIds = await _stateStoreFactory.GetStore<List<string>>(StateStoreDefinitions.Contract)
                    .GetAsync(templateIndexKey, cancellationToken) ?? new List<string>();
            }
            else if (body.Statuses?.Count > 0)
            {
                // Union of all status indexes
                contractIds = new List<string>();
                foreach (var status in body.Statuses)
                {
                    var statusIndexKey = $"{STATUS_INDEX_PREFIX}{status.ToString().ToLowerInvariant()}";
                    var ids = await _stateStoreFactory.GetStore<List<string>>(StateStoreDefinitions.Contract)
                        .GetAsync(statusIndexKey, cancellationToken) ?? new List<string>();
                    contractIds.AddRange(ids);
                }
                contractIds = contractIds.Distinct().ToList();
            }
            else
            {
                _logger.LogWarning("No filter criteria provided for query");
                return (StatusCodes.BadRequest, null);
            }

            if (contractIds.Count == 0)
            {
                return (StatusCodes.OK, new QueryContractInstancesResponse
                {
                    Contracts = new List<ContractInstanceResponse>(),
                    TotalCount = 0,
                    Page = body.Page,
                    PageSize = body.PageSize,
                    HasNextPage = false
                });
            }

            // Load contracts
            var keys = contractIds.Select(id => $"{INSTANCE_PREFIX}{id}").ToList();
            var bulkResults = await _stateStoreFactory.GetStore<ContractInstanceModel>(StateStoreDefinitions.Contract)
                .GetBulkAsync(keys, cancellationToken);

            var contracts = bulkResults.Where(r => r.Value != null).Select(r => r.Value!).ToList();

            // Apply additional filters
            var filtered = contracts.AsEnumerable();

            if (body.Statuses?.Count > 0)
            {
                var statusStrings = body.Statuses.Select(s => s.ToString().ToLowerInvariant()).ToHashSet();
                filtered = filtered.Where(c => statusStrings.Contains(c.Status.ToLowerInvariant()));
            }

            if (body.TemplateId.HasValue)
            {
                var templateIdStr = body.TemplateId.Value.ToString();
                filtered = filtered.Where(c => c.TemplateId == templateIdStr);
            }

            // Pagination
            var totalCount = filtered.Count();
            var paged = filtered
                .OrderByDescending(c => c.CreatedAt)
                .Skip((body.Page - 1) * body.PageSize)
                .Take(body.PageSize)
                .ToList();

            return (StatusCodes.OK, new QueryContractInstancesResponse
            {
                Contracts = paged.Select(MapInstanceToResponse).ToList(),
                TotalCount = totalCount,
                Page = body.Page,
                PageSize = body.PageSize,
                HasNextPage = (body.Page * body.PageSize) < totalCount
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error querying contract instances");
            await EmitErrorAsync("QueryContractInstances", "post:/contract/instance/query", ex);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, ContractInstanceResponse?)> TerminateContractInstanceAsync(
        TerminateContractInstanceRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Terminating contract: {ContractId}", body.ContractId);

            var instanceKey = $"{INSTANCE_PREFIX}{body.ContractId}";
            var model = await _stateStoreFactory.GetStore<ContractInstanceModel>(StateStoreDefinitions.Contract)
                .GetAsync(instanceKey, cancellationToken);

            if (model == null)
            {
                return (StatusCodes.NotFound, null);
            }

            // Verify requesting entity is a party
            var requestingParty = model.Parties?.FirstOrDefault(p =>
                p.EntityId == body.RequestingEntityId.ToString() &&
                p.EntityType == body.RequestingEntityType.ToString());

            if (requestingParty == null)
            {
                _logger.LogWarning("Requesting entity is not a party to this contract");
                return (StatusCodes.BadRequest, null);
            }

            // Remove from current status index
            await RemoveFromListAsync($"{STATUS_INDEX_PREFIX}{model.Status}", body.ContractId.ToString(), cancellationToken);

            model.Status = "terminated";
            model.TerminatedAt = DateTimeOffset.UtcNow;
            model.UpdatedAt = DateTimeOffset.UtcNow;

            await _stateStoreFactory.GetStore<ContractInstanceModel>(StateStoreDefinitions.Contract)
                .SaveAsync(instanceKey, model, cancellationToken: cancellationToken);

            await AddToListAsync($"{STATUS_INDEX_PREFIX}terminated", body.ContractId.ToString(), cancellationToken);

            // Publish event
            await PublishContractTerminatedEventAsync(model, body.RequestingEntityId.ToString(),
                body.RequestingEntityType.ToString(), body.Reason, false, cancellationToken);

            _logger.LogInformation("Terminated contract: {ContractId}", body.ContractId);
            return (StatusCodes.OK, MapInstanceToResponse(model));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error terminating contract: {ContractId}", body.ContractId);
            await EmitErrorAsync("TerminateContractInstance", "post:/contract/instance/terminate", ex);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, ContractInstanceStatusResponse?)> GetContractInstanceStatusAsync(
        GetContractInstanceStatusRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var instanceKey = $"{INSTANCE_PREFIX}{body.ContractId}";
            var model = await _stateStoreFactory.GetStore<ContractInstanceModel>(StateStoreDefinitions.Contract)
                .GetAsync(instanceKey, cancellationToken);

            if (model == null)
            {
                return (StatusCodes.NotFound, null);
            }

            var milestoneProgress = model.Milestones?.Select(m => new MilestoneProgressSummary
            {
                Code = m.Code,
                Status = Enum.TryParse<MilestoneStatus>(m.Status, true, out var ms) ? ms : MilestoneStatus.Pending
            }).ToList();

            var pendingConsents = model.Parties?
                .Where(p => p.ConsentStatus == "pending")
                .Select(p => new PendingConsentSummary
                {
                    EntityId = Guid.Parse(p.EntityId),
                    EntityType = Enum.TryParse<EntityType>(p.EntityType, true, out var et) ? et : EntityType.Character,
                    Role = p.Role
                }).ToList();

            // Load any active breaches
            List<BreachSummary>? activeBreaches = null;
            if (model.BreachIds?.Count > 0)
            {
                var breachKeys = model.BreachIds.Select(id => $"{BREACH_PREFIX}{id}").ToList();
                var breaches = await _stateStoreFactory.GetStore<BreachModel>(StateStoreDefinitions.Contract)
                    .GetBulkAsync(breachKeys, cancellationToken);

                var activeStatuses = new[] { "detected", "cure_period" };
                activeBreaches = breaches
                    .Where(b => b.Value != null && activeStatuses.Contains(b.Value.Status))
                    .Select(b => new BreachSummary
                    {
                        BreachId = Guid.Parse(b.Value!.BreachId),
                        BreachType = Enum.TryParse<BreachType>(b.Value.BreachType, true, out var bt) ? bt : BreachType.Term_violation,
                        Status = Enum.TryParse<BreachStatus>(b.Value.Status, true, out var bs) ? bs : BreachStatus.Detected
                    }).ToList();
            }

            int? daysUntilExpiration = null;
            if (model.EffectiveUntil.HasValue)
            {
                var remaining = model.EffectiveUntil.Value - DateTimeOffset.UtcNow;
                daysUntilExpiration = (int)Math.Ceiling(remaining.TotalDays);
            }

            return (StatusCodes.OK, new ContractInstanceStatusResponse
            {
                ContractId = Guid.Parse(model.ContractId),
                Status = Enum.TryParse<ContractStatus>(model.Status, true, out var cs) ? cs : ContractStatus.Draft,
                MilestoneProgress = milestoneProgress ?? new List<MilestoneProgressSummary>(),
                PendingConsents = pendingConsents?.Count > 0 ? pendingConsents : null,
                ActiveBreaches = activeBreaches?.Count > 0 ? activeBreaches : null,
                DaysUntilExpiration = daysUntilExpiration
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting contract status: {ContractId}", body.ContractId);
            await EmitErrorAsync("GetContractInstanceStatus", "post:/contract/instance/get-status", ex);
            return (StatusCodes.InternalServerError, null);
        }
    }

    #endregion

    #region Milestone Operations

    /// <inheritdoc/>
    public async Task<(StatusCodes, MilestoneResponse?)> CompleteMilestoneAsync(
        CompleteMilestoneRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Completing milestone: {MilestoneCode} for contract {ContractId}",
                body.MilestoneCode, body.ContractId);

            var instanceKey = $"{INSTANCE_PREFIX}{body.ContractId}";
            var model = await _stateStoreFactory.GetStore<ContractInstanceModel>(StateStoreDefinitions.Contract)
                .GetAsync(instanceKey, cancellationToken);

            if (model == null)
            {
                return (StatusCodes.NotFound, null);
            }

            var milestone = model.Milestones?.FirstOrDefault(m => m.Code == body.MilestoneCode);
            if (milestone == null)
            {
                _logger.LogWarning("Milestone not found: {MilestoneCode}", body.MilestoneCode);
                return (StatusCodes.NotFound, null);
            }

            if (milestone.Status != "active" && milestone.Status != "pending")
            {
                _logger.LogWarning("Milestone not in completable state: {Status}", milestone.Status);
                return (StatusCodes.BadRequest, null);
            }

            // Mark completed
            milestone.Status = "completed";
            milestone.CompletedAt = DateTimeOffset.UtcNow;
            model.UpdatedAt = DateTimeOffset.UtcNow;

            // Execute onComplete prebound APIs
            var apisExecuted = 0;
            if (milestone.OnComplete?.Count > 0)
            {
                foreach (var api in milestone.OnComplete)
                {
                    await ExecutePreboundApiAsync(model, api, "milestone.completed", cancellationToken);
                    apisExecuted++;
                }
            }

            // Activate next milestone if any
            var currentIndex = model.Milestones?.FindIndex(m => m.Code == body.MilestoneCode) ?? -1;
            if (currentIndex >= 0 && currentIndex + 1 < (model.Milestones?.Count ?? 0))
            {
                model.Milestones![currentIndex + 1].Status = "active";
                model.CurrentMilestoneIndex = currentIndex + 1;
            }

            // Check if all required milestones are complete
            var allRequiredComplete = model.Milestones?
                .Where(m => m.Required)
                .All(m => m.Status == "completed") ?? true;

            if (allRequiredComplete && model.Status == "active")
            {
                await RemoveFromListAsync($"{STATUS_INDEX_PREFIX}active", body.ContractId.ToString(), cancellationToken);
                model.Status = "fulfilled";
                await AddToListAsync($"{STATUS_INDEX_PREFIX}fulfilled", body.ContractId.ToString(), cancellationToken);
                await PublishContractFulfilledEventAsync(model, cancellationToken);
            }

            await _stateStoreFactory.GetStore<ContractInstanceModel>(StateStoreDefinitions.Contract)
                .SaveAsync(instanceKey, model, cancellationToken: cancellationToken);

            // Publish milestone completed event
            await PublishMilestoneCompletedEventAsync(model, milestone, body.Evidence, apisExecuted, cancellationToken);

            return (StatusCodes.OK, new MilestoneResponse
            {
                ContractId = Guid.Parse(model.ContractId),
                Milestone = MapMilestoneToResponse(milestone)
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error completing milestone: {MilestoneCode}", body.MilestoneCode);
            await EmitErrorAsync("CompleteMilestone", "post:/contract/milestone/complete", ex);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, MilestoneResponse?)> FailMilestoneAsync(
        FailMilestoneRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Failing milestone: {MilestoneCode} for contract {ContractId}",
                body.MilestoneCode, body.ContractId);

            var instanceKey = $"{INSTANCE_PREFIX}{body.ContractId}";
            var model = await _stateStoreFactory.GetStore<ContractInstanceModel>(StateStoreDefinitions.Contract)
                .GetAsync(instanceKey, cancellationToken);

            if (model == null)
            {
                return (StatusCodes.NotFound, null);
            }

            var milestone = model.Milestones?.FirstOrDefault(m => m.Code == body.MilestoneCode);
            if (milestone == null)
            {
                return (StatusCodes.NotFound, null);
            }

            if (milestone.Status != "active" && milestone.Status != "pending")
            {
                _logger.LogWarning("Milestone not in failable state: {Status}", milestone.Status);
                return (StatusCodes.BadRequest, null);
            }

            // Mark failed or skipped based on required flag
            var triggeredBreach = false;
            if (milestone.Required)
            {
                milestone.Status = "failed";
                triggeredBreach = true;
            }
            else
            {
                milestone.Status = "skipped";
            }
            milestone.FailedAt = DateTimeOffset.UtcNow;
            model.UpdatedAt = DateTimeOffset.UtcNow;

            // Execute onExpire prebound APIs
            if (milestone.OnExpire?.Count > 0)
            {
                foreach (var api in milestone.OnExpire)
                {
                    await ExecutePreboundApiAsync(model, api, "milestone.failed", cancellationToken);
                }
            }

            await _stateStoreFactory.GetStore<ContractInstanceModel>(StateStoreDefinitions.Contract)
                .SaveAsync(instanceKey, model, cancellationToken: cancellationToken);

            await PublishMilestoneFailedEventAsync(model, milestone, body.Reason ?? "Milestone failed",
                milestone.Required, triggeredBreach, cancellationToken);

            return (StatusCodes.OK, new MilestoneResponse
            {
                ContractId = Guid.Parse(model.ContractId),
                Milestone = MapMilestoneToResponse(milestone)
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error failing milestone: {MilestoneCode}", body.MilestoneCode);
            await EmitErrorAsync("FailMilestone", "post:/contract/milestone/fail", ex);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, MilestoneResponse?)> GetMilestoneAsync(
        GetMilestoneRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var instanceKey = $"{INSTANCE_PREFIX}{body.ContractId}";
            var model = await _stateStoreFactory.GetStore<ContractInstanceModel>(StateStoreDefinitions.Contract)
                .GetAsync(instanceKey, cancellationToken);

            if (model == null)
            {
                return (StatusCodes.NotFound, null);
            }

            var milestone = model.Milestones?.FirstOrDefault(m => m.Code == body.MilestoneCode);
            if (milestone == null)
            {
                return (StatusCodes.NotFound, null);
            }

            return (StatusCodes.OK, new MilestoneResponse
            {
                ContractId = Guid.Parse(model.ContractId),
                Milestone = MapMilestoneToResponse(milestone)
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting milestone: {MilestoneCode}", body.MilestoneCode);
            await EmitErrorAsync("GetMilestone", "post:/contract/milestone/get", ex);
            return (StatusCodes.InternalServerError, null);
        }
    }

    #endregion

    #region Breach Operations

    /// <inheritdoc/>
    public async Task<(StatusCodes, BreachResponse?)> ReportBreachAsync(
        ReportBreachRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Reporting breach for contract: {ContractId}", body.ContractId);

            var instanceKey = $"{INSTANCE_PREFIX}{body.ContractId}";
            var model = await _stateStoreFactory.GetStore<ContractInstanceModel>(StateStoreDefinitions.Contract)
                .GetAsync(instanceKey, cancellationToken);

            if (model == null)
            {
                return (StatusCodes.NotFound, null);
            }

            var breachId = Guid.NewGuid();
            var now = DateTimeOffset.UtcNow;

            // Calculate cure deadline if grace period configured
            DateTimeOffset? cureDeadline = null;
            if (!string.IsNullOrEmpty(model.Terms?.GracePeriodForCure))
            {
                // Parse ISO 8601 duration (simplified - just days for now)
                if (model.Terms.GracePeriodForCure.StartsWith("P") && model.Terms.GracePeriodForCure.EndsWith("D"))
                {
                    var daysStr = model.Terms.GracePeriodForCure.TrimStart('P').TrimEnd('D');
                    if (int.TryParse(daysStr, out var days))
                    {
                        cureDeadline = now.AddDays(days);
                    }
                }
            }

            var breachModel = new BreachModel
            {
                BreachId = breachId.ToString(),
                ContractId = body.ContractId.ToString(),
                BreachingEntityId = body.BreachingEntityId.ToString(),
                BreachingEntityType = body.BreachingEntityType.ToString(),
                BreachType = body.BreachType.ToString(),
                BreachedTermOrMilestone = body.BreachedTermOrMilestone,
                Description = body.Description,
                Status = cureDeadline.HasValue ? "cure_period" : "detected",
                DetectedAt = now,
                CureDeadline = cureDeadline
            };

            // Save breach
            var breachKey = $"{BREACH_PREFIX}{breachId}";
            await _stateStoreFactory.GetStore<BreachModel>(StateStoreDefinitions.Contract)
                .SaveAsync(breachKey, breachModel, cancellationToken: cancellationToken);

            // Link breach to contract
            model.BreachIds ??= new List<string>();
            model.BreachIds.Add(breachId.ToString());
            model.UpdatedAt = now;

            await _stateStoreFactory.GetStore<ContractInstanceModel>(StateStoreDefinitions.Contract)
                .SaveAsync(instanceKey, model, cancellationToken: cancellationToken);

            // Publish event
            await PublishBreachDetectedEventAsync(model, breachModel, cancellationToken);

            return (StatusCodes.OK, MapBreachToResponse(breachModel));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reporting breach for contract: {ContractId}", body.ContractId);
            await EmitErrorAsync("ReportBreach", "post:/contract/breach/report", ex);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, BreachResponse?)> CureBreachAsync(
        CureBreachRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Curing breach: {BreachId}", body.BreachId);

            var breachKey = $"{BREACH_PREFIX}{body.BreachId}";
            var breachModel = await _stateStoreFactory.GetStore<BreachModel>(StateStoreDefinitions.Contract)
                .GetAsync(breachKey, cancellationToken);

            if (breachModel == null)
            {
                return (StatusCodes.NotFound, null);
            }

            if (breachModel.Status != "detected" && breachModel.Status != "cure_period")
            {
                _logger.LogWarning("Breach not in curable state: {Status}", breachModel.Status);
                return (StatusCodes.BadRequest, null);
            }

            breachModel.Status = "cured";
            breachModel.CuredAt = DateTimeOffset.UtcNow;

            await _stateStoreFactory.GetStore<BreachModel>(StateStoreDefinitions.Contract)
                .SaveAsync(breachKey, breachModel, cancellationToken: cancellationToken);

            // Publish event
            await PublishBreachCuredEventAsync(breachModel, body.CureEvidence, cancellationToken);

            return (StatusCodes.OK, MapBreachToResponse(breachModel));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error curing breach: {BreachId}", body.BreachId);
            await EmitErrorAsync("CureBreach", "post:/contract/breach/cure", ex);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, BreachResponse?)> GetBreachAsync(
        GetBreachRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var breachKey = $"{BREACH_PREFIX}{body.BreachId}";
            var breachModel = await _stateStoreFactory.GetStore<BreachModel>(StateStoreDefinitions.Contract)
                .GetAsync(breachKey, cancellationToken);

            if (breachModel == null)
            {
                return (StatusCodes.NotFound, null);
            }

            return (StatusCodes.OK, MapBreachToResponse(breachModel));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting breach: {BreachId}", body.BreachId);
            await EmitErrorAsync("GetBreach", "post:/contract/breach/get", ex);
            return (StatusCodes.InternalServerError, null);
        }
    }

    #endregion

    #region Metadata Operations

    /// <inheritdoc/>
    public async Task<(StatusCodes, ContractMetadataResponse?)> UpdateContractMetadataAsync(
        UpdateContractMetadataRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Updating metadata for contract: {ContractId}", body.ContractId);

            var instanceKey = $"{INSTANCE_PREFIX}{body.ContractId}";
            var model = await _stateStoreFactory.GetStore<ContractInstanceModel>(StateStoreDefinitions.Contract)
                .GetAsync(instanceKey, cancellationToken);

            if (model == null)
            {
                return (StatusCodes.NotFound, null);
            }

            model.GameMetadata ??= new GameMetadataModel();

            if (body.MetadataType == MetadataType.Instance_data)
            {
                model.GameMetadata.InstanceData = body.Data;
            }
            else
            {
                model.GameMetadata.RuntimeState = body.Data;
            }

            model.UpdatedAt = DateTimeOffset.UtcNow;

            await _stateStoreFactory.GetStore<ContractInstanceModel>(StateStoreDefinitions.Contract)
                .SaveAsync(instanceKey, model, cancellationToken: cancellationToken);

            return (StatusCodes.OK, new ContractMetadataResponse
            {
                ContractId = Guid.Parse(model.ContractId),
                InstanceData = model.GameMetadata.InstanceData,
                RuntimeState = model.GameMetadata.RuntimeState
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating metadata: {ContractId}", body.ContractId);
            await EmitErrorAsync("UpdateContractMetadata", "post:/contract/metadata/update", ex);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, ContractMetadataResponse?)> GetContractMetadataAsync(
        GetContractMetadataRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var instanceKey = $"{INSTANCE_PREFIX}{body.ContractId}";
            var model = await _stateStoreFactory.GetStore<ContractInstanceModel>(StateStoreDefinitions.Contract)
                .GetAsync(instanceKey, cancellationToken);

            if (model == null)
            {
                return (StatusCodes.NotFound, null);
            }

            return (StatusCodes.OK, new ContractMetadataResponse
            {
                ContractId = Guid.Parse(model.ContractId),
                InstanceData = model.GameMetadata?.InstanceData,
                RuntimeState = model.GameMetadata?.RuntimeState
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting metadata: {ContractId}", body.ContractId);
            await EmitErrorAsync("GetContractMetadata", "post:/contract/metadata/get", ex);
            return (StatusCodes.InternalServerError, null);
        }
    }

    #endregion

    #region Constraint Operations

    /// <inheritdoc/>
    public async Task<(StatusCodes, CheckConstraintResponse?)> CheckContractConstraintAsync(
        CheckConstraintRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Checking constraint for entity: {EntityId}", body.EntityId);

            // Get active contracts for entity
            var partyIndexKey = $"{PARTY_INDEX_PREFIX}{body.EntityType}:{body.EntityId}";
            var contractIds = await _stateStoreFactory.GetStore<List<string>>(StateStoreDefinitions.Contract)
                .GetAsync(partyIndexKey, cancellationToken) ?? new List<string>();

            if (contractIds.Count == 0)
            {
                return (StatusCodes.OK, new CheckConstraintResponse
                {
                    Allowed = true,
                    ConflictingContracts = null,
                    Reason = null
                });
            }

            // Load active contracts
            var keys = contractIds.Select(id => $"{INSTANCE_PREFIX}{id}").ToList();
            var bulkResults = await _stateStoreFactory.GetStore<ContractInstanceModel>(StateStoreDefinitions.Contract)
                .GetBulkAsync(keys, cancellationToken);

            var activeContracts = bulkResults
                .Where(r => r.Value != null && r.Value.Status == "active")
                .Select(r => r.Value!)
                .ToList();

            // Check for constraint violations based on type
            var conflicting = new List<ContractSummary>();
            string? reason = null;

            foreach (var contract in activeContracts)
            {
                var hasViolation = false;
                var party = contract.Parties?.FirstOrDefault(p =>
                    p.EntityId == body.EntityId.ToString() &&
                    p.EntityType == body.EntityType.ToString());

                if (party == null) continue;

                // Check custom terms for constraint-related terms
                if (contract.Terms?.CustomTerms != null)
                {
                    var customTerms = contract.Terms.CustomTerms;

                    switch (body.ConstraintType)
                    {
                        case ConstraintType.Exclusivity:
                            if (customTerms.ContainsKey("exclusivity") &&
                                Convert.ToBoolean(customTerms["exclusivity"]))
                            {
                                hasViolation = true;
                                reason = "Entity has an exclusivity clause in an active contract";
                            }
                            break;

                        case ConstraintType.Non_compete:
                            if (customTerms.ContainsKey("nonCompete") &&
                                Convert.ToBoolean(customTerms["nonCompete"]))
                            {
                                hasViolation = true;
                                reason = "Entity has a non-compete clause in an active contract";
                            }
                            break;

                        case ConstraintType.Territory:
                            // Would need to check territory overlap with proposed action
                            break;

                        case ConstraintType.Time_commitment:
                            // Would need to check time commitment overlap
                            break;
                    }
                }

                if (hasViolation)
                {
                    conflicting.Add(new ContractSummary
                    {
                        ContractId = Guid.Parse(contract.ContractId),
                        TemplateCode = contract.TemplateCode,
                        TemplateName = null, // Would need to load template
                        Status = Enum.TryParse<ContractStatus>(contract.Status, true, out var cs)
                            ? cs : ContractStatus.Active,
                        Role = party.Role,
                        EffectiveUntil = contract.EffectiveUntil
                    });
                }
            }

            return (StatusCodes.OK, new CheckConstraintResponse
            {
                Allowed = conflicting.Count == 0,
                ConflictingContracts = conflicting.Count > 0 ? conflicting : null,
                Reason = reason
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking constraint for entity: {EntityId}", body.EntityId);
            await EmitErrorAsync("CheckContractConstraint", "post:/contract/check-constraint", ex);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, QueryActiveContractsResponse?)> QueryActiveContractsAsync(
        QueryActiveContractsRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Querying active contracts for entity: {EntityId}", body.EntityId);

            var partyIndexKey = $"{PARTY_INDEX_PREFIX}{body.EntityType}:{body.EntityId}";
            var contractIds = await _stateStoreFactory.GetStore<List<string>>(StateStoreDefinitions.Contract)
                .GetAsync(partyIndexKey, cancellationToken) ?? new List<string>();

            if (contractIds.Count == 0)
            {
                return (StatusCodes.OK, new QueryActiveContractsResponse
                {
                    Contracts = new List<ContractSummary>()
                });
            }

            var keys = contractIds.Select(id => $"{INSTANCE_PREFIX}{id}").ToList();
            var bulkResults = await _stateStoreFactory.GetStore<ContractInstanceModel>(StateStoreDefinitions.Contract)
                .GetBulkAsync(keys, cancellationToken);

            var activeContracts = bulkResults
                .Where(r => r.Value != null && r.Value.Status == "active")
                .Select(r => r.Value!)
                .ToList();

            // Filter by template codes if specified
            if (body.TemplateCodes?.Count > 0)
            {
                var codeSet = body.TemplateCodes.ToHashSet(StringComparer.OrdinalIgnoreCase);
                activeContracts = activeContracts
                    .Where(c => codeSet.Any(code =>
                        c.TemplateCode.StartsWith(code.TrimEnd('*'), StringComparison.OrdinalIgnoreCase)))
                    .ToList();
            }

            var summaries = activeContracts.Select(c =>
            {
                var party = c.Parties?.FirstOrDefault(p =>
                    p.EntityId == body.EntityId.ToString() &&
                    p.EntityType == body.EntityType.ToString());

                return new ContractSummary
                {
                    ContractId = Guid.Parse(c.ContractId),
                    TemplateCode = c.TemplateCode,
                    TemplateName = null,
                    Status = Enum.TryParse<ContractStatus>(c.Status, true, out var cs)
                        ? cs : ContractStatus.Active,
                    Role = party?.Role ?? "unknown",
                    EffectiveUntil = c.EffectiveUntil
                };
            }).ToList();

            return (StatusCodes.OK, new QueryActiveContractsResponse
            {
                Contracts = summaries
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error querying active contracts for entity: {EntityId}", body.EntityId);
            await EmitErrorAsync("QueryActiveContracts", "post:/contract/query-active", ex);
            return (StatusCodes.InternalServerError, null);
        }
    }

    #endregion

    #region Permission Registration

    /// <summary>
    /// Registers this service's API permissions with the Permission service.
    /// </summary>
    public async Task RegisterServicePermissionsAsync()
    {
        _logger.LogInformation("Registering Contract service permissions...");
        await ContractPermissionRegistration.RegisterViaEventAsync(_messageBus, _logger);
    }

    #endregion

    #region Helper Methods

    private async Task AddToListAsync(string key, string value, CancellationToken ct)
    {
        var store = _stateStoreFactory.GetStore<List<string>>(StateStoreDefinitions.Contract);
        var list = await store.GetAsync(key, ct) ?? new List<string>();
        if (!list.Contains(value))
        {
            list.Add(value);
            await store.SaveAsync(key, list, cancellationToken: ct);
        }
    }

    private async Task RemoveFromListAsync(string key, string value, CancellationToken ct)
    {
        var store = _stateStoreFactory.GetStore<List<string>>(StateStoreDefinitions.Contract);
        var list = await store.GetAsync(key, ct) ?? new List<string>();
        if (list.Remove(value))
        {
            await store.SaveAsync(key, list, cancellationToken: ct);
        }
    }

    private ContractTermsModel? MapTermsToModel(ContractTerms? terms)
    {
        if (terms == null) return null;

        return new ContractTermsModel
        {
            Duration = terms.Duration,
            PaymentSchedule = terms.PaymentSchedule?.ToString(),
            PaymentFrequency = terms.PaymentFrequency,
            TerminationPolicy = terms.TerminationPolicy?.ToString(),
            TerminationNoticePeriod = terms.TerminationNoticePeriod,
            BreachThreshold = terms.BreachThreshold,
            GracePeriodForCure = terms.GracePeriodForCure,
            // CustomTerms is object? in generated model; deserialize to dictionary if present
            CustomTerms = terms.CustomTerms is System.Text.Json.JsonElement je
                ? BannouJson.Deserialize<Dictionary<string, object>>(je.GetRawText())
                : terms.CustomTerms as Dictionary<string, object>
        };
    }

    private ContractTermsModel? MergeTerms(ContractTermsModel? template, ContractTermsModel? instance)
    {
        if (template == null) return instance;
        if (instance == null) return template;

        return new ContractTermsModel
        {
            Duration = instance.Duration ?? template.Duration,
            PaymentSchedule = instance.PaymentSchedule ?? template.PaymentSchedule,
            PaymentFrequency = instance.PaymentFrequency ?? template.PaymentFrequency,
            TerminationPolicy = instance.TerminationPolicy ?? template.TerminationPolicy,
            TerminationNoticePeriod = instance.TerminationNoticePeriod ?? template.TerminationNoticePeriod,
            BreachThreshold = instance.BreachThreshold ?? template.BreachThreshold,
            GracePeriodForCure = instance.GracePeriodForCure ?? template.GracePeriodForCure,
            CustomTerms = MergeDictionaries(template.CustomTerms, instance.CustomTerms)
        };
    }

    private Dictionary<string, object>? MergeDictionaries(
        Dictionary<string, object>? first,
        Dictionary<string, object>? second)
    {
        if (first == null) return second;
        if (second == null) return first;

        var result = new Dictionary<string, object>(first);
        foreach (var kvp in second)
        {
            result[kvp.Key] = kvp.Value;
        }
        return result;
    }

    private PreboundApiModel MapPreboundApiToModel(PreboundApi api)
    {
        return new PreboundApiModel
        {
            ServiceName = api.ServiceName,
            Endpoint = api.Endpoint,
            PayloadTemplate = api.PayloadTemplate,
            Description = api.Description,
            ExecutionMode = api.ExecutionMode.ToString()
        };
    }

    private async Task ExecutePreboundApiAsync(
        ContractInstanceModel contract,
        PreboundApiModel api,
        string trigger,
        CancellationToken ct)
    {
        try
        {
            _logger.LogInformation("Executing prebound API: {Service}{Endpoint} for contract {ContractId}",
                api.ServiceName, api.Endpoint, contract.ContractId);

            // Substitute variables in payload template
            var payload = SubstituteVariables(api.PayloadTemplate, contract);

            // For now, just log the execution - full implementation would use IMeshInvocationClient
            _logger.LogDebug("Prebound API payload: {Payload}", payload);

            // Publish execution event
            await _messageBus.TryPublishAsync("contract.prebound-api.executed", new ContractPreboundApiExecutedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                ContractId = Guid.Parse(contract.ContractId),
                Trigger = trigger,
                ServiceName = api.ServiceName,
                Endpoint = api.Endpoint,
                StatusCode = 200 // Placeholder
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute prebound API: {Service}{Endpoint}",
                api.ServiceName, api.Endpoint);

            await _messageBus.TryPublishAsync("contract.prebound-api.failed", new ContractPreboundApiFailedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                ContractId = Guid.Parse(contract.ContractId),
                Trigger = trigger,
                ServiceName = api.ServiceName,
                Endpoint = api.Endpoint,
                ErrorMessage = ex.Message,
                StatusCode = null
            });
        }
    }

    private string SubstituteVariables(string template, ContractInstanceModel contract)
    {
        // Simple variable substitution - replace {{variable}} patterns
        var result = template;

        result = result.Replace("{{contract.id}}", contract.ContractId);
        result = result.Replace("{{contract.templateCode}}", contract.TemplateCode);

        // More sophisticated substitution would be needed for production
        return result;
    }

    private async Task EmitErrorAsync(string operation, string endpoint, Exception ex)
    {
        await _messageBus.TryPublishErrorAsync(
            "contract",
            operation,
            "unexpected_exception",
            ex.Message,
            dependency: null,
            endpoint: endpoint,
            details: null,
            stack: ex.StackTrace);
    }

    #endregion

    #region Mapping Methods

    private ContractTemplateResponse MapTemplateToResponse(ContractTemplateModel model)
    {
        return new ContractTemplateResponse
        {
            TemplateId = Guid.Parse(model.TemplateId),
            Code = model.Code,
            Name = model.Name,
            Description = model.Description,
            RealmId = string.IsNullOrEmpty(model.RealmId) ? null : Guid.Parse(model.RealmId),
            MinParties = model.MinParties,
            MaxParties = model.MaxParties,
            PartyRoles = model.PartyRoles?.Select(r => new PartyRoleDefinition
            {
                Role = r.Role,
                MinCount = r.MinCount,
                MaxCount = r.MaxCount,
                AllowedEntityTypes = r.AllowedEntityTypes?.Select(e =>
                    Enum.TryParse<EntityType>(e, true, out var et) ? et : EntityType.Character).ToList()
            }).ToList() ?? new List<PartyRoleDefinition>(),
            DefaultTerms = MapTermsToResponse(model.DefaultTerms),
            Milestones = model.Milestones?.Select(m => new MilestoneDefinition
            {
                Code = m.Code,
                Name = m.Name,
                Description = m.Description,
                Sequence = m.Sequence,
                Required = m.Required,
                Deadline = m.Deadline,
                OnComplete = m.OnComplete?.Select(MapPreboundApiToResponse).ToList(),
                OnExpire = m.OnExpire?.Select(MapPreboundApiToResponse).ToList()
            }).ToList(),
            DefaultEnforcementMode = Enum.TryParse<EnforcementMode>(model.DefaultEnforcementMode, true, out var em)
                ? em : EnforcementMode.Event_only,
            Transferable = model.Transferable,
            GameMetadata = model.GameMetadata,
            IsActive = model.IsActive,
            CreatedAt = model.CreatedAt,
            UpdatedAt = model.UpdatedAt
        };
    }

    private ContractTerms? MapTermsToResponse(ContractTermsModel? model)
    {
        if (model == null) return null;

        return new ContractTerms
        {
            Duration = model.Duration,
            PaymentSchedule = string.IsNullOrEmpty(model.PaymentSchedule) ? null :
                Enum.TryParse<PaymentSchedule>(model.PaymentSchedule, true, out var ps) ? ps : null,
            PaymentFrequency = model.PaymentFrequency,
            TerminationPolicy = string.IsNullOrEmpty(model.TerminationPolicy) ? null :
                Enum.TryParse<TerminationPolicy>(model.TerminationPolicy, true, out var tp) ? tp : null,
            TerminationNoticePeriod = model.TerminationNoticePeriod,
            BreachThreshold = model.BreachThreshold,
            GracePeriodForCure = model.GracePeriodForCure,
            CustomTerms = model.CustomTerms
        };
    }

    private PreboundApi MapPreboundApiToResponse(PreboundApiModel model)
    {
        return new PreboundApi
        {
            ServiceName = model.ServiceName,
            Endpoint = model.Endpoint,
            PayloadTemplate = model.PayloadTemplate,
            Description = model.Description,
            ExecutionMode = string.IsNullOrEmpty(model.ExecutionMode) ? PreboundApiExecutionMode.Sync :
                Enum.TryParse<PreboundApiExecutionMode>(model.ExecutionMode, true, out var em) ? em : PreboundApiExecutionMode.Sync
        };
    }

    private ContractInstanceResponse MapInstanceToResponse(ContractInstanceModel model)
    {
        return new ContractInstanceResponse
        {
            ContractId = Guid.Parse(model.ContractId),
            TemplateId = Guid.Parse(model.TemplateId),
            TemplateCode = model.TemplateCode,
            Status = Enum.TryParse<ContractStatus>(model.Status, true, out var cs) ? cs : ContractStatus.Draft,
            Parties = model.Parties?.Select(p => new ContractPartyResponse
            {
                EntityId = Guid.Parse(p.EntityId),
                EntityType = Enum.TryParse<EntityType>(p.EntityType, true, out var et) ? et : EntityType.Character,
                Role = p.Role,
                ConsentStatus = Enum.TryParse<ConsentStatus>(p.ConsentStatus, true, out var cos) ? cos : ConsentStatus.Pending,
                ConsentedAt = p.ConsentedAt
            }).ToList() ?? new List<ContractPartyResponse>(),
            Terms = MapTermsToResponse(model.Terms),
            Milestones = model.Milestones?.Select(MapMilestoneToResponse).ToList(),
            CurrentMilestoneIndex = model.CurrentMilestoneIndex,
            EscrowIds = model.EscrowIds?.Select(Guid.Parse).ToList(),
            ProposedAt = model.ProposedAt,
            AcceptedAt = model.AcceptedAt,
            EffectiveFrom = model.EffectiveFrom,
            EffectiveUntil = model.EffectiveUntil,
            TerminatedAt = model.TerminatedAt,
            GameMetadata = model.GameMetadata?.InstanceData,
            CreatedAt = model.CreatedAt,
            UpdatedAt = model.UpdatedAt
        };
    }

    private MilestoneInstanceResponse MapMilestoneToResponse(MilestoneInstanceModel model)
    {
        return new MilestoneInstanceResponse
        {
            Code = model.Code,
            Name = model.Name,
            Sequence = model.Sequence,
            Required = model.Required,
            Status = Enum.TryParse<MilestoneStatus>(model.Status, true, out var ms) ? ms : MilestoneStatus.Pending,
            CompletedAt = model.CompletedAt,
            FailedAt = model.FailedAt,
            Deadline = null // Would need to compute absolute deadline
        };
    }

    private BreachResponse MapBreachToResponse(BreachModel model)
    {
        return new BreachResponse
        {
            BreachId = Guid.Parse(model.BreachId),
            ContractId = Guid.Parse(model.ContractId),
            BreachingEntityId = Guid.Parse(model.BreachingEntityId),
            BreachingEntityType = Enum.TryParse<EntityType>(model.BreachingEntityType, true, out var et)
                ? et : EntityType.Character,
            BreachType = Enum.TryParse<BreachType>(model.BreachType, true, out var bt)
                ? bt : BreachType.Term_violation,
            BreachedTermOrMilestone = model.BreachedTermOrMilestone,
            Description = model.Description,
            Status = Enum.TryParse<BreachStatus>(model.Status, true, out var bs)
                ? bs : BreachStatus.Detected,
            DetectedAt = model.DetectedAt,
            CureDeadline = model.CureDeadline,
            CuredAt = model.CuredAt,
            ConsequencesAppliedAt = model.ConsequencesAppliedAt
        };
    }

    #endregion

    #region Event Publishing

    private async Task PublishTemplateCreatedEventAsync(ContractTemplateModel model, CancellationToken ct)
    {
        await _messageBus.TryPublishAsync("contract-template.created", new ContractTemplateCreatedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            TemplateId = Guid.Parse(model.TemplateId),
            Code = model.Code,
            Name = model.Name,
            Description = model.Description,
            RealmId = string.IsNullOrEmpty(model.RealmId) ? default : Guid.Parse(model.RealmId),
            MinParties = model.MinParties,
            MaxParties = model.MaxParties,
            DefaultEnforcementMode = model.DefaultEnforcementMode,
            Transferable = model.Transferable,
            IsActive = model.IsActive,
            CreatedAt = model.CreatedAt
        });
    }

    private async Task PublishTemplateUpdatedEventAsync(
        ContractTemplateModel model, List<string> changedFields, CancellationToken ct)
    {
        await _messageBus.TryPublishAsync("contract-template.updated", new ContractTemplateUpdatedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            TemplateId = Guid.Parse(model.TemplateId),
            Code = model.Code,
            Name = model.Name,
            Description = model.Description,
            RealmId = string.IsNullOrEmpty(model.RealmId) ? default : Guid.Parse(model.RealmId),
            MinParties = model.MinParties,
            MaxParties = model.MaxParties,
            DefaultEnforcementMode = model.DefaultEnforcementMode,
            Transferable = model.Transferable,
            IsActive = model.IsActive,
            CreatedAt = model.CreatedAt,
            UpdatedAt = model.UpdatedAt ?? DateTimeOffset.UtcNow,
            ChangedFields = changedFields
        });
    }

    private async Task PublishTemplateDeletedEventAsync(ContractTemplateModel model, CancellationToken ct)
    {
        await _messageBus.TryPublishAsync("contract-template.deleted", new ContractTemplateDeletedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            TemplateId = Guid.Parse(model.TemplateId),
            Code = model.Code,
            Name = model.Name,
            Description = model.Description,
            RealmId = string.IsNullOrEmpty(model.RealmId) ? default : Guid.Parse(model.RealmId),
            MinParties = model.MinParties,
            MaxParties = model.MaxParties,
            DefaultEnforcementMode = model.DefaultEnforcementMode,
            Transferable = model.Transferable,
            IsActive = model.IsActive,
            CreatedAt = model.CreatedAt,
            UpdatedAt = model.UpdatedAt ?? DateTimeOffset.UtcNow,
            DeletedReason = "Soft deleted"
        });
    }

    private async Task PublishInstanceCreatedEventAsync(ContractInstanceModel model, CancellationToken ct)
    {
        await _messageBus.TryPublishAsync("contract-instance.created", new ContractInstanceCreatedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            ContractId = Guid.Parse(model.ContractId),
            TemplateId = Guid.Parse(model.TemplateId),
            TemplateCode = model.TemplateCode,
            Status = model.Status,
            CreatedAt = model.CreatedAt
        });
    }

    private async Task PublishContractProposedEventAsync(ContractInstanceModel model, CancellationToken ct)
    {
        var parties = model.Parties?.Select(p => new PartyInfo
        {
            EntityId = Guid.Parse(p.EntityId),
            EntityType = p.EntityType,
            Role = p.Role
        }).ToList() ?? new List<PartyInfo>();

        await _messageBus.TryPublishAsync("contract.proposed", new ContractProposedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            ContractId = Guid.Parse(model.ContractId),
            TemplateId = Guid.Parse(model.TemplateId),
            TemplateCode = model.TemplateCode,
            Parties = parties
        });
    }

    private async Task PublishConsentReceivedEventAsync(
        ContractInstanceModel model, ContractPartyModel party, int remaining, CancellationToken ct)
    {
        await _messageBus.TryPublishAsync("contract.consent-received", new ContractConsentReceivedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            ContractId = Guid.Parse(model.ContractId),
            ConsentingEntityId = Guid.Parse(party.EntityId),
            ConsentingEntityType = party.EntityType,
            Role = party.Role,
            RemainingConsentsNeeded = remaining
        });
    }

    private async Task PublishContractAcceptedEventAsync(ContractInstanceModel model, CancellationToken ct)
    {
        var parties = model.Parties?.Select(p => new PartyInfo
        {
            EntityId = Guid.Parse(p.EntityId),
            EntityType = p.EntityType,
            Role = p.Role
        }).ToList() ?? new List<PartyInfo>();

        await _messageBus.TryPublishAsync("contract.accepted", new ContractAcceptedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            ContractId = Guid.Parse(model.ContractId),
            TemplateCode = model.TemplateCode,
            Parties = parties,
            EffectiveFrom = model.EffectiveFrom
        });
    }

    private async Task PublishContractActivatedEventAsync(ContractInstanceModel model, CancellationToken ct)
    {
        var parties = model.Parties?.Select(p => new PartyInfo
        {
            EntityId = Guid.Parse(p.EntityId),
            EntityType = p.EntityType,
            Role = p.Role
        }).ToList() ?? new List<PartyInfo>();

        await _messageBus.TryPublishAsync("contract.activated", new ContractActivatedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            ContractId = Guid.Parse(model.ContractId),
            TemplateCode = model.TemplateCode,
            Parties = parties,
            EffectiveUntil = model.EffectiveUntil
        });
    }

    private async Task PublishMilestoneCompletedEventAsync(
        ContractInstanceModel contract, MilestoneInstanceModel milestone,
        object? evidence, int apisExecuted, CancellationToken ct)
    {
        await _messageBus.TryPublishAsync("contract.milestone.completed", new ContractMilestoneCompletedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            ContractId = Guid.Parse(contract.ContractId),
            MilestoneCode = milestone.Code,
            MilestoneName = milestone.Name,
            Evidence = evidence as Dictionary<string, object>,
            PreboundApisExecuted = apisExecuted
        });
    }

    private async Task PublishMilestoneFailedEventAsync(
        ContractInstanceModel contract, MilestoneInstanceModel milestone,
        string reason, bool wasRequired, bool triggeredBreach, CancellationToken ct)
    {
        await _messageBus.TryPublishAsync("contract.milestone.failed", new ContractMilestoneFailedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            ContractId = Guid.Parse(contract.ContractId),
            MilestoneCode = milestone.Code,
            MilestoneName = milestone.Name,
            Reason = reason,
            WasRequired = wasRequired,
            TriggeredBreach = triggeredBreach
        });
    }

    private async Task PublishBreachDetectedEventAsync(
        ContractInstanceModel contract, BreachModel breach, CancellationToken ct)
    {
        await _messageBus.TryPublishAsync("contract.breach.detected", new ContractBreachDetectedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            ContractId = Guid.Parse(contract.ContractId),
            BreachId = Guid.Parse(breach.BreachId),
            BreachingEntityId = Guid.Parse(breach.BreachingEntityId),
            BreachingEntityType = breach.BreachingEntityType,
            BreachType = breach.BreachType,
            BreachedTermOrMilestone = breach.BreachedTermOrMilestone,
            CureDeadline = breach.CureDeadline
        });
    }

    private async Task PublishBreachCuredEventAsync(BreachModel breach, string? evidence, CancellationToken ct)
    {
        await _messageBus.TryPublishAsync("contract.breach.cured", new ContractBreachCuredEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            ContractId = Guid.Parse(breach.ContractId),
            BreachId = Guid.Parse(breach.BreachId),
            CureEvidence = evidence
        });
    }

    private async Task PublishContractFulfilledEventAsync(ContractInstanceModel model, CancellationToken ct)
    {
        var parties = model.Parties?.Select(p => new PartyInfo
        {
            EntityId = Guid.Parse(p.EntityId),
            EntityType = p.EntityType,
            Role = p.Role
        }).ToList() ?? new List<PartyInfo>();

        await _messageBus.TryPublishAsync("contract.fulfilled", new ContractFulfilledEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            ContractId = Guid.Parse(model.ContractId),
            TemplateCode = model.TemplateCode,
            Parties = parties,
            MilestonesCompleted = model.Milestones?.Count(m => m.Status == "completed") ?? 0
        });
    }

    private async Task PublishContractTerminatedEventAsync(
        ContractInstanceModel model, string terminatedById, string terminatedByType,
        string? reason, bool wasBreachRelated, CancellationToken ct)
    {
        await _messageBus.TryPublishAsync("contract.terminated", new ContractTerminatedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            ContractId = Guid.Parse(model.ContractId),
            TerminatedByEntityId = Guid.Parse(terminatedById),
            TerminatedByEntityType = terminatedByType,
            Reason = reason,
            WasBreachRelated = wasBreachRelated
        });
    }

    #endregion
}

#region Internal Storage Models

/// <summary>
/// Internal model for storing contract templates.
/// </summary>
internal class ContractTemplateModel
{
    public string TemplateId { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? RealmId { get; set; }
    public int MinParties { get; set; }
    public int MaxParties { get; set; }
    public List<PartyRoleModel>? PartyRoles { get; set; }
    public ContractTermsModel? DefaultTerms { get; set; }
    public List<MilestoneDefinitionModel>? Milestones { get; set; }
    public string DefaultEnforcementMode { get; set; } = "event_only";
    public bool Transferable { get; set; }
    public object? GameMetadata { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}

/// <summary>
/// Internal model for storing party role definitions.
/// </summary>
internal class PartyRoleModel
{
    public string Role { get; set; } = string.Empty;
    public int MinCount { get; set; }
    public int MaxCount { get; set; }
    public List<string>? AllowedEntityTypes { get; set; }
}

/// <summary>
/// Internal model for storing contract terms.
/// </summary>
internal class ContractTermsModel
{
    public string? Duration { get; set; }
    public string? PaymentSchedule { get; set; }
    public string? PaymentFrequency { get; set; }
    public string? TerminationPolicy { get; set; }
    public string? TerminationNoticePeriod { get; set; }
    public int? BreachThreshold { get; set; }
    public string? GracePeriodForCure { get; set; }
    public Dictionary<string, object>? CustomTerms { get; set; }
}

/// <summary>
/// Internal model for storing milestone definitions.
/// </summary>
internal class MilestoneDefinitionModel
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int Sequence { get; set; }
    public bool Required { get; set; }
    public string? Deadline { get; set; }
    public List<PreboundApiModel>? OnComplete { get; set; }
    public List<PreboundApiModel>? OnExpire { get; set; }
}

/// <summary>
/// Internal model for storing prebound API configurations.
/// </summary>
internal class PreboundApiModel
{
    public string ServiceName { get; set; } = string.Empty;
    public string Endpoint { get; set; } = string.Empty;
    public string PayloadTemplate { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string ExecutionMode { get; set; } = "sync";
}

/// <summary>
/// Internal model for storing contract instances.
/// </summary>
internal class ContractInstanceModel
{
    public string ContractId { get; set; } = string.Empty;
    public string TemplateId { get; set; } = string.Empty;
    public string TemplateCode { get; set; } = string.Empty;
    public string Status { get; set; } = "draft";
    public List<ContractPartyModel>? Parties { get; set; }
    public ContractTermsModel? Terms { get; set; }
    public List<MilestoneInstanceModel>? Milestones { get; set; }
    public int CurrentMilestoneIndex { get; set; }
    public List<string>? EscrowIds { get; set; }
    public List<string>? BreachIds { get; set; }
    public DateTimeOffset? ProposedAt { get; set; }
    public DateTimeOffset? AcceptedAt { get; set; }
    public DateTimeOffset? EffectiveFrom { get; set; }
    public DateTimeOffset? EffectiveUntil { get; set; }
    public DateTimeOffset? TerminatedAt { get; set; }
    public GameMetadataModel? GameMetadata { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}

/// <summary>
/// Internal model for storing contract party information.
/// </summary>
internal class ContractPartyModel
{
    public string EntityId { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string ConsentStatus { get; set; } = "pending";
    public DateTimeOffset? ConsentedAt { get; set; }
}

/// <summary>
/// Internal model for storing milestone instance state.
/// </summary>
internal class MilestoneInstanceModel
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int Sequence { get; set; }
    public bool Required { get; set; }
    public string Status { get; set; } = "pending";
    public string? Deadline { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset? FailedAt { get; set; }
    public List<PreboundApiModel>? OnComplete { get; set; }
    public List<PreboundApiModel>? OnExpire { get; set; }
}

/// <summary>
/// Internal model for storing game metadata.
/// </summary>
internal class GameMetadataModel
{
    public object? InstanceData { get; set; }
    public object? RuntimeState { get; set; }
}

/// <summary>
/// Internal model for storing breach records.
/// </summary>
internal class BreachModel
{
    public string BreachId { get; set; } = string.Empty;
    public string ContractId { get; set; } = string.Empty;
    public string BreachingEntityId { get; set; } = string.Empty;
    public string BreachingEntityType { get; set; } = string.Empty;
    public string BreachType { get; set; } = string.Empty;
    public string? BreachedTermOrMilestone { get; set; }
    public string? Description { get; set; }
    public string Status { get; set; } = "detected";
    public DateTimeOffset DetectedAt { get; set; }
    public DateTimeOffset? CureDeadline { get; set; }
    public DateTimeOffset? CuredAt { get; set; }
    public DateTimeOffset? ConsequencesAppliedAt { get; set; }
}

#endregion
