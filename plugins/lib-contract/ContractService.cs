using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Location;
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
    private readonly IDistributedLockProvider _lockProvider;
    private readonly ILogger<ContractService> _logger;
    private readonly ContractServiceConfiguration _configuration;
    private readonly ILocationClient _locationClient;

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
    /// Safely extracts a boolean value from custom terms dictionary.
    /// Handles both native bool values and JsonElement values from JSON deserialization.
    /// </summary>
    /// <param name="customTerms">The custom terms dictionary.</param>
    /// <param name="key">The key to look up.</param>
    /// <returns>True if the key exists and has a truthy boolean value, false otherwise.</returns>
    private static bool GetCustomTermBool(IDictionary<string, object>? customTerms, string key)
    {
        if (customTerms == null || !customTerms.TryGetValue(key, out var value))
            return false;

        // Handle JsonElement from JSON deserialization
        if (value is System.Text.Json.JsonElement jsonElement)
        {
            return jsonElement.ValueKind == System.Text.Json.JsonValueKind.True;
        }

        // Handle native bool
        if (value is bool boolValue)
            return boolValue;

        // Fallback for other types - catch expected conversion failures
        try
        {
            return Convert.ToBoolean(value);
        }
        catch (FormatException)
        {
            // Value was a string that couldn't be parsed as boolean
            return false;
        }
        catch (InvalidCastException)
        {
            // Value type doesn't support IConvertible
            return false;
        }
    }

    /// <summary>
    /// Parses an ISO 8601 duration string (e.g., "P10D", "PT2H", "P1DT12H") into a TimeSpan.
    /// </summary>
    /// <param name="duration">The ISO 8601 duration string.</param>
    /// <returns>The parsed TimeSpan, or null if the format is invalid or duration is null/empty.</returns>
    private static TimeSpan? ParseIsoDuration(string? duration)
    {
        if (string.IsNullOrEmpty(duration)) return null;
        try
        {
            return System.Xml.XmlConvert.ToTimeSpan(duration);
        }
        catch (FormatException)
        {
            // Invalid format - log warning handled at call site
            return null;
        }
    }

    /// <summary>
    /// Safely extracts a list of GUIDs from custom terms dictionary.
    /// Handles JsonElement arrays from JSON deserialization.
    /// </summary>
    /// <param name="customTerms">The custom terms dictionary.</param>
    /// <param name="key">The key to look up.</param>
    /// <returns>List of parsed GUIDs, or empty list if key not found or parsing fails.</returns>
    private static List<Guid> GetCustomTermGuidList(IDictionary<string, object>? customTerms, string key)
    {
        if (customTerms == null || !customTerms.TryGetValue(key, out var value))
            return new List<Guid>();

        if (value is System.Text.Json.JsonElement element && element.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            var result = new List<Guid>();
            foreach (var item in element.EnumerateArray())
            {
                if (item.ValueKind == System.Text.Json.JsonValueKind.String &&
                    Guid.TryParse(item.GetString(), out var guid))
                {
                    result.Add(guid);
                }
            }
            return result;
        }
        return new List<Guid>();
    }

    /// <summary>
    /// Safely extracts a string value from custom terms dictionary.
    /// Handles both native string values and JsonElement values from JSON deserialization.
    /// </summary>
    /// <param name="customTerms">The custom terms dictionary.</param>
    /// <param name="key">The key to look up.</param>
    /// <returns>The string value, or null if key not found or not a string.</returns>
    private static string? GetCustomTermString(IDictionary<string, object>? customTerms, string key)
    {
        if (customTerms == null || !customTerms.TryGetValue(key, out var value))
            return null;

        if (value is string str) return str;
        if (value is System.Text.Json.JsonElement element && element.ValueKind == System.Text.Json.JsonValueKind.String)
            return element.GetString();
        return null;
    }

    /// <summary>
    /// Safely extracts a GUID from a proposed action object.
    /// Handles JsonElement objects from JSON deserialization.
    /// </summary>
    /// <param name="proposedAction">The proposed action object.</param>
    /// <param name="key">The property key to look up.</param>
    /// <returns>The parsed GUID, or null if not found or parsing fails.</returns>
    private static Guid? GetProposedActionGuid(object? proposedAction, string key)
    {
        if (proposedAction is System.Text.Json.JsonElement element && element.ValueKind == System.Text.Json.JsonValueKind.Object)
        {
            if (element.TryGetProperty(key, out var prop) &&
                prop.ValueKind == System.Text.Json.JsonValueKind.String &&
                Guid.TryParse(prop.GetString(), out var guid))
            {
                return guid;
            }
        }
        return null;
    }

    /// <summary>
    /// Initializes a new instance of the ContractService.
    /// </summary>
    public ContractService(
        IMessageBus messageBus,
        IServiceNavigator navigator,
        IStateStoreFactory stateStoreFactory,
        IDistributedLockProvider lockProvider,
        ILogger<ContractService> logger,
        ContractServiceConfiguration configuration,
        IEventConsumer eventConsumer,
        ILocationClient locationClient)
    {
        _messageBus = messageBus;
        _navigator = navigator;
        _stateStoreFactory = stateStoreFactory;
        _lockProvider = lockProvider;
        _logger = logger;
        _configuration = configuration;
        _locationClient = locationClient;

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

            // Validate milestone count against configuration limit
            if (body.Milestones?.Count > _configuration.MaxMilestonesPerTemplate)
            {
                _logger.LogWarning("Template milestone count {Count} exceeds maximum {Max}",
                    body.Milestones.Count, _configuration.MaxMilestonesPerTemplate);
                return (StatusCodes.BadRequest, null);
            }

            // Validate prebound API count per milestone and deadline format
            if (body.Milestones != null)
            {
                foreach (var milestone in body.Milestones)
                {
                    var onCompleteCount = milestone.OnComplete?.Count ?? 0;
                    var onExpireCount = milestone.OnExpire?.Count ?? 0;

                    if (onCompleteCount > _configuration.MaxPreboundApisPerMilestone ||
                        onExpireCount > _configuration.MaxPreboundApisPerMilestone)
                    {
                        _logger.LogWarning(
                            "Milestone {Code} exceeds prebound API limit: onComplete={OnComplete}, onExpire={OnExpire}, max={Max}",
                            milestone.Code, onCompleteCount, onExpireCount, _configuration.MaxPreboundApisPerMilestone);
                        return (StatusCodes.BadRequest, null);
                    }

                    // Validate deadline format if specified (ISO 8601 duration)
                    if (!string.IsNullOrEmpty(milestone.Deadline) && ParseIsoDuration(milestone.Deadline) == null)
                    {
                        _logger.LogWarning(
                            "Milestone {Code} has invalid deadline format: {Deadline}. Expected ISO 8601 duration (e.g., P10D, PT2H)",
                            milestone.Code, milestone.Deadline);
                        return (StatusCodes.BadRequest, null);
                    }
                }
            }

            var templateId = Guid.NewGuid();
            var now = DateTimeOffset.UtcNow;

            var model = new ContractTemplateModel
            {
                TemplateId = templateId,
                Code = body.Code,
                Name = body.Name,
                Description = body.Description,
                RealmId = body.RealmId,
                MinParties = body.MinParties,
                MaxParties = body.MaxParties,
                PartyRoles = body.PartyRoles?.Select(r => new PartyRoleModel
                {
                    Role = r.Role,
                    MinCount = r.MinCount,
                    MaxCount = r.MaxCount,
                    AllowedEntityTypes = r.AllowedEntityTypes?.ToList()
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
                    DeadlineBehavior = m.DeadlineBehavior,
                    OnComplete = m.OnComplete?.Select(MapPreboundApiToModel).ToList(),
                    OnExpire = m.OnExpire?.Select(MapPreboundApiToModel).ToList()
                }).ToList(),
                DefaultEnforcementMode = body.DefaultEnforcementMode != default
                    ? body.DefaultEnforcementMode
                    : _configuration.DefaultEnforcementMode,
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

            if (model == null || !model.IsActive)
            {
                _logger.LogWarning("Template not found or inactive: {TemplateId}", templateId);
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
            _logger.LogDebug("Listing contract templates");

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
                filtered = filtered.Where(t => t.RealmId == body.RealmId.Value || t.RealmId == null);
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

                var activeStatuses = new[] { ContractStatus.Draft, ContractStatus.Proposed, ContractStatus.Pending, ContractStatus.Active };
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

            // Validate party count against template bounds
            if (body.Parties.Count < template.MinParties || body.Parties.Count > template.MaxParties)
            {
                _logger.LogWarning("Invalid party count: {Count}, expected {Min}-{Max}",
                    body.Parties.Count, template.MinParties, template.MaxParties);
                return (StatusCodes.BadRequest, null);
            }

            // Validate party count against configuration hard cap
            if (body.Parties.Count > _configuration.MaxPartiesPerContract)
            {
                _logger.LogWarning("Party count {Count} exceeds configuration maximum {Max}",
                    body.Parties.Count, _configuration.MaxPartiesPerContract);
                return (StatusCodes.BadRequest, null);
            }

            // Validate active contract limit per entity (0 = unlimited)
            if (_configuration.MaxActiveContractsPerEntity > 0)
            {
                foreach (var party in body.Parties)
                {
                    var partyIndexKey = $"{PARTY_INDEX_PREFIX}{party.EntityType}:{party.EntityId}";
                    var contractIds = await _stateStoreFactory.GetStore<List<string>>(StateStoreDefinitions.Contract)
                        .GetAsync(partyIndexKey, cancellationToken) ?? new List<string>();

                    if (contractIds.Count >= _configuration.MaxActiveContractsPerEntity)
                    {
                        _logger.LogWarning(
                            "Entity {EntityType}:{EntityId} has {Count} contracts, exceeds limit {Max}",
                            party.EntityType, party.EntityId, contractIds.Count, _configuration.MaxActiveContractsPerEntity);
                        return (StatusCodes.BadRequest, null);
                    }
                }
            }

            var contractId = Guid.NewGuid();
            var now = DateTimeOffset.UtcNow;

            // Create parties with pending consent
            var parties = body.Parties.Select(p => new ContractPartyModel
            {
                EntityId = p.EntityId,
                EntityType = p.EntityType,
                Role = p.Role,
                ConsentStatus = ConsentStatus.Pending,
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
                Status = MilestoneStatus.Pending,
                Deadline = m.Deadline,
                DeadlineBehavior = m.DeadlineBehavior,
                OnComplete = m.OnComplete,
                OnExpire = m.OnExpire
            }).ToList();

            var model = new ContractInstanceModel
            {
                ContractId = contractId,
                TemplateId = body.TemplateId,
                TemplateCode = template.Code,
                Status = ContractStatus.Draft,
                Parties = parties,
                Terms = mergedTerms,
                Milestones = milestones,
                CurrentMilestoneIndex = 0,
                EscrowIds = body.EscrowIds?.ToList(),
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
            var store = _stateStoreFactory.GetStore<ContractInstanceModel>(StateStoreDefinitions.Contract);

            // Acquire contract lock for state transition
            await using var contractLock = await _lockProvider.LockAsync(
                "contract-instance", body.ContractId.ToString(), Guid.NewGuid().ToString(), _configuration.ContractLockTimeoutSeconds, cancellationToken);
            if (!contractLock.Success)
            {
                _logger.LogWarning("Could not acquire contract lock for {ContractId}", body.ContractId);
                return (StatusCodes.Conflict, null);
            }

            var (model, etag) = await store.GetWithETagAsync(instanceKey, cancellationToken);

            if (model == null)
            {
                return (StatusCodes.NotFound, null);
            }

            if (model.Status != ContractStatus.Draft)
            {
                _logger.LogWarning("Contract not in draft status: {ContractId}, status: {Status}",
                    body.ContractId, model.Status);
                return (StatusCodes.BadRequest, null);
            }

            model.Status = ContractStatus.Proposed;
            model.ProposedAt = DateTimeOffset.UtcNow;
            model.UpdatedAt = DateTimeOffset.UtcNow;

            // Persist first
            var newEtag = await store.TrySaveAsync(instanceKey, model, etag ?? string.Empty, cancellationToken);
            if (newEtag == null)
            {
                _logger.LogWarning("Concurrent modification detected for contract: {ContractId}", body.ContractId);
                return (StatusCodes.Conflict, null);
            }

            // Then update indexes
            await RemoveFromListAsync($"{STATUS_INDEX_PREFIX}draft", body.ContractId.ToString(), cancellationToken);
            await AddToListAsync($"{STATUS_INDEX_PREFIX}proposed", body.ContractId.ToString(), cancellationToken);

            // Then publish events
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
            var store = _stateStoreFactory.GetStore<ContractInstanceModel>(StateStoreDefinitions.Contract);

            // Acquire contract lock for state transition
            await using var contractLock = await _lockProvider.LockAsync(
                "contract-instance", body.ContractId.ToString(), Guid.NewGuid().ToString(), _configuration.ContractLockTimeoutSeconds, cancellationToken);
            if (!contractLock.Success)
            {
                _logger.LogWarning("Could not acquire contract lock for {ContractId}", body.ContractId);
                return (StatusCodes.Conflict, null);
            }

            var (model, etag) = await store.GetWithETagAsync(instanceKey, cancellationToken);

            if (model == null)
            {
                return (StatusCodes.NotFound, null);
            }

            // Guardian enforcement: locked contracts cannot be modified by parties
            if (model.GuardianId.HasValue)
            {
                _logger.LogWarning("Cannot consent to locked contract: {ContractId}, guardian: {GuardianId}",
                    body.ContractId, model.GuardianId);
                return (StatusCodes.Forbidden, null);
            }

            if (model.Status != ContractStatus.Proposed)
            {
                _logger.LogWarning("Contract not in proposed status: {ContractId}", body.ContractId);
                return (StatusCodes.BadRequest, null);
            }

            // Check consent deadline (lazy expiration)
            if (model.ProposedAt.HasValue && _configuration.DefaultConsentTimeoutDays > 0)
            {
                var deadline = model.ProposedAt.Value.AddDays(_configuration.DefaultConsentTimeoutDays);
                if (DateTimeOffset.UtcNow > deadline)
                {
                    _logger.LogWarning(
                        "Consent deadline expired for contract {ContractId}: proposed {ProposedAt}, deadline {Deadline}",
                        body.ContractId, model.ProposedAt, deadline);

                    // Transition to expired
                    model.Status = ContractStatus.Expired;
                    model.UpdatedAt = DateTimeOffset.UtcNow;
                    var expiredEtag = await store.TrySaveAsync(instanceKey, model, etag ?? string.Empty, cancellationToken);
                    if (expiredEtag != null)
                    {
                        await RemoveFromListAsync($"{STATUS_INDEX_PREFIX}proposed", body.ContractId.ToString(), cancellationToken);
                        await AddToListAsync($"{STATUS_INDEX_PREFIX}expired", body.ContractId.ToString(), cancellationToken);
                    }

                    return (StatusCodes.BadRequest, null);
                }
            }

            // Find party
            var party = model.Parties?.FirstOrDefault(p =>
                p.EntityId == body.PartyEntityId &&
                p.EntityType == body.PartyEntityType);

            if (party == null)
            {
                _logger.LogWarning("Party not found in contract: {EntityId}", body.PartyEntityId);
                return (StatusCodes.BadRequest, null);
            }

            if (party.ConsentStatus == ConsentStatus.Consented)
            {
                _logger.LogWarning("Party already consented: {EntityId}", body.PartyEntityId);
                return (StatusCodes.BadRequest, null);
            }

            // Record consent
            party.ConsentStatus = ConsentStatus.Consented;
            party.ConsentedAt = DateTimeOffset.UtcNow;
            model.UpdatedAt = DateTimeOffset.UtcNow;

            // Check if all parties have consented
            var remainingConsents = model.Parties?.Count(p => p.ConsentStatus != ConsentStatus.Consented) ?? 0;
            var allConsented = model.Parties?.All(p => p.ConsentStatus == ConsentStatus.Consented) ?? false;

            if (allConsented)
            {
                model.AcceptedAt = DateTimeOffset.UtcNow;

                // Check if we should activate immediately or wait for effectiveFrom
                if (model.EffectiveFrom == null || model.EffectiveFrom <= DateTimeOffset.UtcNow)
                {
                    model.EffectiveFrom = DateTimeOffset.UtcNow;

                    // Activate first milestone if any, otherwise contract is immediately fulfilled
                    if (model.Milestones?.Count > 0)
                    {
                        model.Status = ContractStatus.Active;
                        model.Milestones[0].Status = MilestoneStatus.Active;
                        model.Milestones[0].ActivatedAt = DateTimeOffset.UtcNow;
                    }
                    else
                    {
                        // No milestones means contract is immediately fulfilled (nothing to perform)
                        model.Status = ContractStatus.Fulfilled;
                    }
                }
                else
                {
                    model.Status = ContractStatus.Pending;
                }
            }

            // Persist state change first, then perform side effects
            var newEtag = await store.TrySaveAsync(instanceKey, model, etag ?? string.Empty, cancellationToken);
            if (newEtag == null)
            {
                _logger.LogWarning("Concurrent modification detected for contract: {ContractId}", body.ContractId);
                return (StatusCodes.Conflict, null);
            }

            // Side effects: index updates and events (only after successful save)
            await PublishConsentReceivedEventAsync(model, party, remainingConsents, cancellationToken);

            if (allConsented)
            {
                await RemoveFromListAsync($"{STATUS_INDEX_PREFIX}proposed", body.ContractId.ToString(), cancellationToken);

                if (model.Status == ContractStatus.Fulfilled)
                {
                    // Contract with no milestones goes directly to fulfilled
                    await AddToListAsync($"{STATUS_INDEX_PREFIX}fulfilled", body.ContractId.ToString(), cancellationToken);
                    await PublishContractActivatedEventAsync(model, cancellationToken);
                    await PublishContractFulfilledEventAsync(model, cancellationToken);
                }
                else if (model.Status == ContractStatus.Active)
                {
                    await AddToListAsync($"{STATUS_INDEX_PREFIX}active", body.ContractId.ToString(), cancellationToken);
                    await PublishContractActivatedEventAsync(model, cancellationToken);
                }
                else
                {
                    await AddToListAsync($"{STATUS_INDEX_PREFIX}pending", body.ContractId.ToString(), cancellationToken);
                }

                await PublishContractAcceptedEventAsync(model, cancellationToken);
            }

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
            _logger.LogDebug("Querying contract instances");

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

            // Filter null results and extract values - OfType safely handles the null filtering
            var contracts = bulkResults.Select(r => r.Value).OfType<ContractInstanceModel>().ToList();

            // Apply additional filters
            var filtered = contracts.AsEnumerable();

            if (body.Statuses?.Count > 0)
            {
                var statusSet = body.Statuses.ToHashSet();
                filtered = filtered.Where(c => statusSet.Contains(c.Status));
            }

            if (body.TemplateId.HasValue)
            {
                var templateId = body.TemplateId.Value;
                filtered = filtered.Where(c => c.TemplateId == templateId);
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
            var store = _stateStoreFactory.GetStore<ContractInstanceModel>(StateStoreDefinitions.Contract);

            // Acquire contract lock for state transition
            await using var contractLock = await _lockProvider.LockAsync(
                "contract-instance", body.ContractId.ToString(), Guid.NewGuid().ToString(), _configuration.ContractLockTimeoutSeconds, cancellationToken);
            if (!contractLock.Success)
            {
                _logger.LogWarning("Could not acquire contract lock for {ContractId}", body.ContractId);
                return (StatusCodes.Conflict, null);
            }

            var (model, etag) = await store.GetWithETagAsync(instanceKey, cancellationToken);

            if (model == null)
            {
                return (StatusCodes.NotFound, null);
            }

            // Guardian enforcement: locked contracts cannot be terminated by parties
            if (model.GuardianId.HasValue)
            {
                _logger.LogWarning("Cannot terminate locked contract: {ContractId}, guardian: {GuardianId}",
                    body.ContractId, model.GuardianId);
                return (StatusCodes.Forbidden, null);
            }

            // Verify requesting entity is a party
            var requestingParty = model.Parties?.FirstOrDefault(p =>
                p.EntityId == body.RequestingEntityId &&
                p.EntityType == body.RequestingEntityType);

            if (requestingParty == null)
            {
                _logger.LogWarning("Requesting entity is not a party to this contract");
                return (StatusCodes.BadRequest, null);
            }

            var previousStatus = model.Status.ToString().ToLowerInvariant();
            model.Status = ContractStatus.Terminated;
            model.TerminatedAt = DateTimeOffset.UtcNow;
            model.UpdatedAt = DateTimeOffset.UtcNow;

            // Persist first
            var newEtag = await store.TrySaveAsync(instanceKey, model, etag ?? string.Empty, cancellationToken);
            if (newEtag == null)
            {
                _logger.LogWarning("Concurrent modification detected for contract: {ContractId}", body.ContractId);
                return (StatusCodes.Conflict, null);
            }

            // Then update indexes
            await RemoveFromListAsync($"{STATUS_INDEX_PREFIX}{previousStatus}", body.ContractId.ToString(), cancellationToken);
            await AddToListAsync($"{STATUS_INDEX_PREFIX}terminated", body.ContractId.ToString(), cancellationToken);

            // Then publish events
            await PublishContractTerminatedEventAsync(model, body.RequestingEntityId,
                body.RequestingEntityType, body.Reason, false, cancellationToken);

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
                Status = m.Status
            }).ToList();

            var pendingConsents = model.Parties?
                .Where(p => p.ConsentStatus == ConsentStatus.Pending)
                .Select(p => new PendingConsentSummary
                {
                    EntityId = p.EntityId,
                    EntityType = p.EntityType,
                    Role = p.Role
                }).ToList();

            // Load any active breaches
            List<BreachSummary>? activeBreaches = null;
            if (model.BreachIds?.Count > 0)
            {
                var breachKeys = model.BreachIds.Select(id => $"{BREACH_PREFIX}{id}").ToList();
                var breaches = await _stateStoreFactory.GetStore<BreachModel>(StateStoreDefinitions.Contract)
                    .GetBulkAsync(breachKeys, cancellationToken);

                var activeStatuses = new[] { BreachStatus.Detected, BreachStatus.CurePeriod };
                activeBreaches = breaches
                    .Select(b => b.Value)
                    .Where(breach => breach != null && activeStatuses.Contains(breach.Status))
                    .Select(breach => new BreachSummary
                    {
                        BreachId = breach.BreachId,
                        BreachType = breach.BreachType,
                        Status = breach.Status
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
                ContractId = model.ContractId,
                Status = model.Status,
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
            var store = _stateStoreFactory.GetStore<ContractInstanceModel>(StateStoreDefinitions.Contract);

            // Acquire contract lock for state transition
            await using var contractLock = await _lockProvider.LockAsync(
                "contract-instance", body.ContractId.ToString(), Guid.NewGuid().ToString(), _configuration.ContractLockTimeoutSeconds, cancellationToken);
            if (!contractLock.Success)
            {
                _logger.LogWarning("Could not acquire contract lock for {ContractId}", body.ContractId);
                return (StatusCodes.Conflict, null);
            }

            var (model, etag) = await store.GetWithETagAsync(instanceKey, cancellationToken);

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

            if (milestone.Status != MilestoneStatus.Active && milestone.Status != MilestoneStatus.Pending)
            {
                _logger.LogWarning("Milestone not in completable state: {Status}", milestone.Status);
                return (StatusCodes.BadRequest, null);
            }

            // Mark completed
            milestone.Status = MilestoneStatus.Completed;
            milestone.CompletedAt = DateTimeOffset.UtcNow;
            model.UpdatedAt = DateTimeOffset.UtcNow;

            // Activate next milestone if any
            var milestones = model.Milestones;
            if (milestones != null)
            {
                var currentIndex = milestones.FindIndex(m => m.Code == body.MilestoneCode);
                if (currentIndex >= 0 && currentIndex + 1 < milestones.Count)
                {
                    milestones[currentIndex + 1].Status = MilestoneStatus.Active;
                    milestones[currentIndex + 1].ActivatedAt = DateTimeOffset.UtcNow;
                    model.CurrentMilestoneIndex = currentIndex + 1;
                }
            }

            // Check if all required milestones are complete
            var allRequiredComplete = model.Milestones?
                .Where(m => m.Required)
                .All(m => m.Status == MilestoneStatus.Completed) ?? true;

            if (allRequiredComplete && model.Status == ContractStatus.Active)
            {
                model.Status = ContractStatus.Fulfilled;
            }

            // Persist first
            var newEtag = await store.TrySaveAsync(instanceKey, model, etag ?? string.Empty, cancellationToken);
            if (newEtag == null)
            {
                _logger.LogWarning("Concurrent modification detected for contract: {ContractId}", body.ContractId);
                return (StatusCodes.Conflict, null);
            }

            // Then update indexes (if status changed to fulfilled)
            if (allRequiredComplete && model.Status == ContractStatus.Fulfilled)
            {
                await RemoveFromListAsync($"{STATUS_INDEX_PREFIX}active", body.ContractId.ToString(), cancellationToken);
                await AddToListAsync($"{STATUS_INDEX_PREFIX}fulfilled", body.ContractId.ToString(), cancellationToken);
                await PublishContractFulfilledEventAsync(model, cancellationToken);
            }

            // Then execute prebound APIs (side effects after persist)
            var apisExecuted = 0;
            if (milestone.OnComplete?.Count > 0)
            {
                apisExecuted = await ExecutePreboundApisBatchedAsync(
                    model, milestone.OnComplete, "milestone.completed", cancellationToken);
            }

            // Publish milestone completed event
            await PublishMilestoneCompletedEventAsync(model, milestone, body.Evidence, apisExecuted, cancellationToken);

            return (StatusCodes.OK, new MilestoneResponse
            {
                ContractId = model.ContractId,
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
            var store = _stateStoreFactory.GetStore<ContractInstanceModel>(StateStoreDefinitions.Contract);

            // Acquire contract lock for state transition
            await using var contractLock = await _lockProvider.LockAsync(
                "contract-instance", body.ContractId.ToString(), Guid.NewGuid().ToString(), _configuration.ContractLockTimeoutSeconds, cancellationToken);
            if (!contractLock.Success)
            {
                _logger.LogWarning("Could not acquire contract lock for {ContractId}", body.ContractId);
                return (StatusCodes.Conflict, null);
            }

            var (model, etag) = await store.GetWithETagAsync(instanceKey, cancellationToken);

            if (model == null)
            {
                return (StatusCodes.NotFound, null);
            }

            var milestone = model.Milestones?.FirstOrDefault(m => m.Code == body.MilestoneCode);
            if (milestone == null)
            {
                return (StatusCodes.NotFound, null);
            }

            if (milestone.Status != MilestoneStatus.Active && milestone.Status != MilestoneStatus.Pending)
            {
                _logger.LogWarning("Milestone not in failable state: {Status}", milestone.Status);
                return (StatusCodes.BadRequest, null);
            }

            // Mark failed or skipped based on required flag
            var triggeredBreach = false;
            if (milestone.Required)
            {
                milestone.Status = MilestoneStatus.Failed;
                triggeredBreach = true;
            }
            else
            {
                milestone.Status = MilestoneStatus.Skipped;
            }
            milestone.FailedAt = DateTimeOffset.UtcNow;
            model.UpdatedAt = DateTimeOffset.UtcNow;

            // Persist first
            var newEtag = await store.TrySaveAsync(instanceKey, model, etag ?? string.Empty, cancellationToken);
            if (newEtag == null)
            {
                _logger.LogWarning("Concurrent modification detected for contract: {ContractId}", body.ContractId);
                return (StatusCodes.Conflict, null);
            }

            // Then execute prebound APIs (side effects after persist)
            if (milestone.OnExpire?.Count > 0)
            {
                await ExecutePreboundApisBatchedAsync(
                    model, milestone.OnExpire, "milestone.failed", cancellationToken);
            }

            // Then publish events
            await PublishMilestoneFailedEventAsync(model, milestone, body.Reason ?? "Milestone failed",
                milestone.Required, triggeredBreach, cancellationToken);

            return (StatusCodes.OK, new MilestoneResponse
            {
                ContractId = model.ContractId,
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
                ContractId = model.ContractId,
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
            var instanceStore = _stateStoreFactory.GetStore<ContractInstanceModel>(StateStoreDefinitions.Contract);

            // Acquire contract lock for state transition
            await using var contractLock = await _lockProvider.LockAsync(
                "contract-instance", body.ContractId.ToString(), Guid.NewGuid().ToString(), _configuration.ContractLockTimeoutSeconds, cancellationToken);
            if (!contractLock.Success)
            {
                _logger.LogWarning("Could not acquire contract lock for {ContractId}", body.ContractId);
                return (StatusCodes.Conflict, null);
            }

            var (model, etag) = await instanceStore.GetWithETagAsync(instanceKey, cancellationToken);

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
                BreachId = breachId,
                ContractId = body.ContractId,
                BreachingEntityId = body.BreachingEntityId,
                BreachingEntityType = body.BreachingEntityType,
                BreachType = body.BreachType,
                BreachedTermOrMilestone = body.BreachedTermOrMilestone,
                Description = body.Description,
                Status = cureDeadline.HasValue ? BreachStatus.CurePeriod : BreachStatus.Detected,
                DetectedAt = now,
                CureDeadline = cureDeadline
            };

            // Save breach (new entity, no concurrency concern)
            var breachKey = $"{BREACH_PREFIX}{breachId}";
            await _stateStoreFactory.GetStore<BreachModel>(StateStoreDefinitions.Contract)
                .SaveAsync(breachKey, breachModel, cancellationToken: cancellationToken);

            // Link breach to contract
            model.BreachIds ??= new List<Guid>();
            model.BreachIds.Add(breachId);
            model.UpdatedAt = now;

            var newEtag = await instanceStore.TrySaveAsync(instanceKey, model, etag ?? string.Empty, cancellationToken);
            if (newEtag == null)
            {
                _logger.LogWarning("Concurrent modification detected for contract: {ContractId}", body.ContractId);
                return (StatusCodes.Conflict, null);
            }

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
            var breachStore = _stateStoreFactory.GetStore<BreachModel>(StateStoreDefinitions.Contract);
            var (breachModel, etag) = await breachStore.GetWithETagAsync(breachKey, cancellationToken);

            if (breachModel == null)
            {
                return (StatusCodes.NotFound, null);
            }

            // Acquire contract lock to serialize breach state changes
            await using var contractLock = await _lockProvider.LockAsync(
                "contract-instance", breachModel.ContractId.ToString(), Guid.NewGuid().ToString(), _configuration.ContractLockTimeoutSeconds, cancellationToken);
            if (!contractLock.Success)
            {
                _logger.LogWarning("Could not acquire contract lock for {ContractId}", breachModel.ContractId);
                return (StatusCodes.Conflict, null);
            }

            if (breachModel.Status != BreachStatus.Detected && breachModel.Status != BreachStatus.CurePeriod)
            {
                _logger.LogWarning("Breach not in curable state: {Status}", breachModel.Status);
                return (StatusCodes.BadRequest, null);
            }

            breachModel.Status = BreachStatus.Cured;
            breachModel.CuredAt = DateTimeOffset.UtcNow;

            var newEtag = await breachStore.TrySaveAsync(breachKey, breachModel, etag ?? string.Empty, cancellationToken);
            if (newEtag == null)
            {
                _logger.LogWarning("Concurrent modification detected for breach: {BreachId}", body.BreachId);
                return (StatusCodes.Conflict, null);
            }

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
            var store = _stateStoreFactory.GetStore<ContractInstanceModel>(StateStoreDefinitions.Contract);
            var (model, etag) = await store.GetWithETagAsync(instanceKey, cancellationToken);

            if (model == null)
            {
                return (StatusCodes.NotFound, null);
            }

            model.GameMetadata ??= new GameMetadataModel();

            if (body.MetadataType == MetadataType.InstanceData)
            {
                model.GameMetadata.InstanceData = body.Data;
            }
            else
            {
                model.GameMetadata.RuntimeState = body.Data;
            }

            model.UpdatedAt = DateTimeOffset.UtcNow;

            var newEtag = await store.TrySaveAsync(instanceKey, model, etag ?? string.Empty, cancellationToken);
            if (newEtag == null)
            {
                _logger.LogWarning("Concurrent modification detected for contract: {ContractId}", body.ContractId);
                return (StatusCodes.Conflict, null);
            }

            return (StatusCodes.OK, new ContractMetadataResponse
            {
                ContractId = model.ContractId,
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
                ContractId = model.ContractId,
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
            _logger.LogDebug("Checking constraint for entity: {EntityId}", body.EntityId);

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

            // Filter null results and extract active contracts - OfType safely handles the null filtering
            var activeContracts = bulkResults
                .Select(r => r.Value)
                .OfType<ContractInstanceModel>()
                .Where(c => c.Status == ContractStatus.Active)
                .ToList();

            // Check for constraint violations based on type
            var conflicting = new List<ContractSummary>();
            string? reason = null;

            foreach (var contract in activeContracts)
            {
                var hasViolation = false;
                var party = contract.Parties?.FirstOrDefault(p =>
                    p.EntityId == body.EntityId &&
                    p.EntityType == body.EntityType);

                if (party == null) continue;

                // Check custom terms for constraint-related terms
                if (contract.Terms?.CustomTerms != null)
                {
                    var customTerms = contract.Terms.CustomTerms;

                    switch (body.ConstraintType)
                    {
                        case ConstraintType.Exclusivity:
                            if (GetCustomTermBool(customTerms, "exclusivity"))
                            {
                                hasViolation = true;
                                reason = "Entity has an exclusivity clause in an active contract";
                            }
                            break;

                        case ConstraintType.NonCompete:
                            if (GetCustomTermBool(customTerms, "nonCompete"))
                            {
                                hasViolation = true;
                                reason = "Entity has a non-compete clause in an active contract";
                            }
                            break;

                        case ConstraintType.Territory:
                            if (GetCustomTermBool(customTerms, "territory"))
                            {
                                var territoryLocationIds = GetCustomTermGuidList(customTerms, "territoryLocationIds");
                                var territoryMode = GetCustomTermString(customTerms, "territoryMode") ?? "exclusive";
                                var proposedLocationId = GetProposedActionGuid(body.ProposedAction, "locationId");

                                if (territoryLocationIds.Count == 0 || !proposedLocationId.HasValue)
                                {
                                    // Missing constraint data - cannot evaluate
                                    _logger.LogWarning(
                                        "Territory constraint check missing data: territoryLocationIds={Count}, proposedLocationId={HasValue}",
                                        territoryLocationIds.Count, proposedLocationId.HasValue);
                                    return (StatusCodes.BadRequest, null);
                                }

                                // Get location ancestry - let ApiException propagate on failure (T7)
                                var ancestryResponse = await _locationClient.GetLocationAncestorsAsync(
                                    new GetLocationAncestorsRequest { LocationId = proposedLocationId.Value },
                                    cancellationToken);

                                // Build set of all relevant location IDs (proposed + ancestors)
                                var locationHierarchy = new HashSet<Guid> { proposedLocationId.Value };
                                foreach (var ancestor in ancestryResponse.Locations)
                                {
                                    locationHierarchy.Add(ancestor.LocationId);
                                }

                                // Check for overlap
                                var hasOverlap = territoryLocationIds.Any(tid => locationHierarchy.Contains(tid));

                                if (territoryMode == "exclusive" && hasOverlap)
                                {
                                    hasViolation = true;
                                    reason = "Proposed location overlaps with exclusive territory in active contract";
                                }
                                else if (territoryMode == "inclusive" && !hasOverlap)
                                {
                                    hasViolation = true;
                                    reason = "Proposed location is outside inclusive territory in active contract";
                                }
                            }
                            break;

                        case ConstraintType.TimeCommitment:
                            if (GetCustomTermBool(customTerms, "timeCommitment"))
                            {
                                var timeCommitmentType = GetCustomTermString(customTerms, "timeCommitmentType") ?? "partial";

                                // Only exclusive commitments can conflict
                                if (timeCommitmentType == "exclusive")
                                {
                                    // Check against all other active exclusive contracts
                                    foreach (var otherContract in activeContracts)
                                    {
                                        if (otherContract.ContractId == contract.ContractId)
                                            continue;

                                        var otherCustomTerms = otherContract.Terms?.CustomTerms;
                                        if (otherCustomTerms == null) continue;

                                        var otherHasTimeCommitment = GetCustomTermBool(otherCustomTerms, "timeCommitment");
                                        var otherTimeCommitmentType = GetCustomTermString(otherCustomTerms, "timeCommitmentType") ?? "partial";

                                        if (!otherHasTimeCommitment || otherTimeCommitmentType != "exclusive")
                                            continue;

                                        // Check date range overlap: thisFrom <= otherUntil && thisUntil >= otherFrom
                                        var thisFrom = contract.EffectiveFrom ?? DateTimeOffset.MinValue;
                                        var thisUntil = contract.EffectiveUntil ?? DateTimeOffset.MaxValue;
                                        var otherFrom = otherContract.EffectiveFrom ?? DateTimeOffset.MinValue;
                                        var otherUntil = otherContract.EffectiveUntil ?? DateTimeOffset.MaxValue;

                                        if (thisFrom <= otherUntil && thisUntil >= otherFrom)
                                        {
                                            hasViolation = true;
                                            reason = "Entity has conflicting exclusive time commitments in active contracts";
                                            break;
                                        }
                                    }
                                }
                            }
                            break;
                    }
                }

                if (hasViolation)
                {
                    conflicting.Add(new ContractSummary
                    {
                        ContractId = contract.ContractId,
                        TemplateCode = contract.TemplateCode,
                        TemplateName = null, // Would need to load template
                        Status = contract.Status,
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
            _logger.LogDebug("Querying active contracts for entity: {EntityId}", body.EntityId);

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

            // Filter null results and extract active contracts - OfType safely handles the null filtering
            var activeContracts = bulkResults
                .Select(r => r.Value)
                .OfType<ContractInstanceModel>()
                .Where(c => c.Status == ContractStatus.Active)
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
                    p.EntityId == body.EntityId &&
                    p.EntityType == body.EntityType);

                return new ContractSummary
                {
                    ContractId = c.ContractId,
                    TemplateCode = c.TemplateCode,
                    TemplateName = null,
                    Status = c.Status,
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
    public async Task RegisterServicePermissionsAsync(string appId)
    {
        _logger.LogInformation("Registering Contract service permissions...");
        await ContractPermissionRegistration.RegisterViaEventAsync(_messageBus, appId, _logger);
    }

    #endregion

    #region Helper Methods

    private async Task AddToListAsync(string key, string value, CancellationToken ct)
    {
        await using var lockResponse = await _lockProvider.LockAsync(
            "contract-index", key, Guid.NewGuid().ToString(), _configuration.IndexLockTimeoutSeconds, ct);
        if (!lockResponse.Success)
        {
            _logger.LogWarning("Could not acquire index lock for key {Key}", key);
            return;
        }

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
        await using var lockResponse = await _lockProvider.LockAsync(
            "contract-index", key, Guid.NewGuid().ToString(), _configuration.IndexLockTimeoutSeconds, ct);
        if (!lockResponse.Success)
        {
            _logger.LogWarning("Could not acquire index lock for key {Key}", key);
            return;
        }

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
            PaymentSchedule = terms.PaymentSchedule,
            PaymentFrequency = terms.PaymentFrequency,
            TerminationPolicy = terms.TerminationPolicy,
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
            ExecutionMode = api.ExecutionMode,
            ResponseValidation = api.ResponseValidation
        };
    }

    /// <summary>
    /// Executes a list of prebound APIs in batches of configured size.
    /// APIs within each batch execute concurrently; batches execute sequentially.
    /// </summary>
    private async Task<int> ExecutePreboundApisBatchedAsync(
        ContractInstanceModel contract,
        List<PreboundApiModel> apis,
        string trigger,
        CancellationToken ct)
    {
        var executed = 0;
        var batchSize = _configuration.PreboundApiBatchSize;

        for (var i = 0; i < apis.Count; i += batchSize)
        {
            var batch = apis.Skip(i).Take(batchSize);
            var tasks = batch.Select(api => ExecutePreboundApiAsync(contract, api, trigger, ct));
            await Task.WhenAll(tasks);
            executed += Math.Min(batchSize, apis.Count - i);
        }

        return executed;
    }

    private async Task ExecutePreboundApiAsync(
        ContractInstanceModel contract,
        PreboundApiModel api,
        string trigger,
        CancellationToken ct)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(_configuration.PreboundApiTimeoutMs);
        var effectiveCt = timeoutCts.Token;

        try
        {
            _logger.LogInformation("Executing prebound API: {Service}{Endpoint} for contract {ContractId}",
                api.ServiceName, api.Endpoint, contract.ContractId);

            // Build context dictionary for template substitution
            var context = BuildContractContext(contract);

            // Convert to PreboundApiDefinition for ServiceNavigator
            var apiDefinition = new ServiceClients.PreboundApiDefinition
            {
                ServiceName = api.ServiceName,
                Endpoint = api.Endpoint,
                PayloadTemplate = api.PayloadTemplate,
                Description = api.Description,
                ExecutionMode = ConvertExecutionMode(api.ExecutionMode)
            };

            // Execute via ServiceNavigator with configured timeout
            var result = await _navigator.ExecutePreboundApiAsync(apiDefinition, context, effectiveCt);

            if (!result.SubstitutionSucceeded)
            {
                _logger.LogWarning("Template substitution failed for prebound API {Service}{Endpoint}: {Error}",
                    api.ServiceName, api.Endpoint, result.SubstitutionError);

                await _messageBus.TryPublishAsync("contract.prebound-api.failed", new ContractPreboundApiFailedEvent
                {
                    EventId = Guid.NewGuid(),
                    Timestamp = DateTimeOffset.UtcNow,
                    ContractId = contract.ContractId,
                    Trigger = trigger,
                    ServiceName = api.ServiceName,
                    Endpoint = api.Endpoint,
                    ErrorMessage = $"Template substitution failed: {result.SubstitutionError}",
                    StatusCode = null
                });
                return;
            }

            // Validate response if validation rules are configured
            if (api.ResponseValidation != null && result.Result != null)
            {
                var validationResult = Utilities.ResponseValidator.Validate(
                    result.Result.StatusCode,
                    result.Result.ResponseBody,
                    api.ResponseValidation);

                if (validationResult.Outcome != Utilities.ValidationOutcome.Success)
                {
                    _logger.LogWarning("Prebound API response validation failed for {Service}{Endpoint}: {Outcome} - {Reason}",
                        api.ServiceName, api.Endpoint, validationResult.Outcome, validationResult.FailureReason);

                    await _messageBus.TryPublishAsync("contract.prebound-api.validation-failed", new ContractPreboundApiValidationFailedEvent
                    {
                        EventId = Guid.NewGuid(),
                        Timestamp = DateTimeOffset.UtcNow,
                        ContractId = contract.ContractId,
                        Trigger = trigger,
                        ServiceName = api.ServiceName,
                        Endpoint = api.Endpoint,
                        StatusCode = result.Result.StatusCode,
                        ValidationOutcome = validationResult.Outcome.ToString(),
                        FailureReason = validationResult.FailureReason
                    });
                    return;
                }
            }

            // Publish execution event
            await _messageBus.TryPublishAsync("contract.prebound-api.executed", new ContractPreboundApiExecutedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                ContractId = contract.ContractId,
                Trigger = trigger,
                ServiceName = api.ServiceName,
                Endpoint = api.Endpoint,
                StatusCode = result.Result?.StatusCode ?? 0
            });
        }
        catch (ApiException ex)
        {
            // Expected API error from downstream service - log at Warning level
            _logger.LogWarning(ex, "Prebound API returned error: {Service}{Endpoint} - {StatusCode}",
                api.ServiceName, api.Endpoint, ex.StatusCode);

            await _messageBus.TryPublishAsync("contract.prebound-api.failed", new ContractPreboundApiFailedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                ContractId = contract.ContractId,
                Trigger = trigger,
                ServiceName = api.ServiceName,
                Endpoint = api.Endpoint,
                ErrorMessage = ex.Message,
                StatusCode = ex.StatusCode
            });
        }
        catch (Exception ex)
        {
            // Unexpected error - log at Error level
            _logger.LogError(ex, "Failed to execute prebound API: {Service}{Endpoint}",
                api.ServiceName, api.Endpoint);

            await _messageBus.TryPublishAsync("contract.prebound-api.failed", new ContractPreboundApiFailedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                ContractId = contract.ContractId,
                Trigger = trigger,
                ServiceName = api.ServiceName,
                Endpoint = api.Endpoint,
                ErrorMessage = ex.Message,
                StatusCode = null
            });
        }
    }

    /// <summary>
    /// Builds a context dictionary from contract data for template substitution.
    /// Values are converted to strings at this boundary for template interpolation.
    /// </summary>
    private static Dictionary<string, object?> BuildContractContext(ContractInstanceModel contract)
    {
        var context = new Dictionary<string, object?>
        {
            // Convert Guids to strings at serialization boundary for template substitution
            ["contract.id"] = contract.ContractId.ToString(),
            ["contract.templateId"] = contract.TemplateId.ToString(),
            ["contract.templateCode"] = contract.TemplateCode,
            ["contract.status"] = contract.Status.ToString().ToLowerInvariant(),
            ["contract.effectiveFrom"] = contract.EffectiveFrom?.ToString("o"),
            ["contract.effectiveUntil"] = contract.EffectiveUntil?.ToString("o"),
            ["contract.currentMilestoneIndex"] = contract.CurrentMilestoneIndex
        };

        // Add parties - convert Guids and enums to strings at serialization boundary
        if (contract.Parties != null)
        {
            context["contract.parties"] = contract.Parties.Select(p => new Dictionary<string, object?>
            {
                ["entityId"] = p.EntityId.ToString(),
                ["entityType"] = p.EntityType.ToString(),
                ["role"] = p.Role,
                ["consentStatus"] = p.ConsentStatus.ToString()
            }).ToList();

            // Add party shortcuts by role
            foreach (var party in contract.Parties)
            {
                context[$"contract.party.{party.Role.ToLowerInvariant()}.entityId"] = party.EntityId.ToString();
                context[$"contract.party.{party.Role.ToLowerInvariant()}.entityType"] = party.EntityType.ToString();
            }
        }

        // Add terms
        if (contract.Terms != null)
        {
            context["contract.terms.duration"] = contract.Terms.Duration;
            context["contract.terms.paymentFrequency"] = contract.Terms.PaymentFrequency;

            if (contract.Terms.CustomTerms != null)
            {
                foreach (var term in contract.Terms.CustomTerms)
                {
                    context[$"contract.terms.custom.{term.Key}"] = term.Value;
                }
            }
        }

        // Add game metadata
        if (contract.GameMetadata?.InstanceData != null)
        {
            context["contract.gameMetadata.instanceData"] = contract.GameMetadata.InstanceData;
        }
        if (contract.GameMetadata?.RuntimeState != null)
        {
            context["contract.gameMetadata.runtimeState"] = contract.GameMetadata.RuntimeState;
        }

        return context;
    }

    /// <summary>
    /// Converts PreboundApiExecutionMode to ServiceClients.ExecutionMode.
    /// </summary>
    private static ServiceClients.ExecutionMode ConvertExecutionMode(PreboundApiExecutionMode mode)
    {
        return mode switch
        {
            PreboundApiExecutionMode.Sync => ServiceClients.ExecutionMode.Sync,
            PreboundApiExecutionMode.Async => ServiceClients.ExecutionMode.Async,
            PreboundApiExecutionMode.FireAndForget => ServiceClients.ExecutionMode.FireAndForget,
            _ => ServiceClients.ExecutionMode.Sync
        };
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
            TemplateId = model.TemplateId,
            Code = model.Code,
            Name = model.Name,
            Description = model.Description,
            RealmId = model.RealmId,
            MinParties = model.MinParties,
            MaxParties = model.MaxParties,
            PartyRoles = model.PartyRoles?.Select(r => new PartyRoleDefinition
            {
                Role = r.Role,
                MinCount = r.MinCount,
                MaxCount = r.MaxCount,
                AllowedEntityTypes = r.AllowedEntityTypes?.ToList()
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
            DefaultEnforcementMode = model.DefaultEnforcementMode,
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
            PaymentSchedule = model.PaymentSchedule,
            PaymentFrequency = model.PaymentFrequency,
            TerminationPolicy = model.TerminationPolicy,
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
            ExecutionMode = model.ExecutionMode,
            ResponseValidation = model.ResponseValidation
        };
    }

    private ContractInstanceResponse MapInstanceToResponse(ContractInstanceModel model)
    {
        return new ContractInstanceResponse
        {
            ContractId = model.ContractId,
            TemplateId = model.TemplateId,
            TemplateCode = model.TemplateCode,
            Status = model.Status,
            Parties = model.Parties?.Select(p => new ContractPartyResponse
            {
                EntityId = p.EntityId,
                EntityType = p.EntityType,
                Role = p.Role,
                ConsentStatus = p.ConsentStatus,
                ConsentedAt = p.ConsentedAt
            }).ToList() ?? new List<ContractPartyResponse>(),
            Terms = MapTermsToResponse(model.Terms),
            Milestones = model.Milestones?.Select(MapMilestoneToResponse).ToList(),
            CurrentMilestoneIndex = model.CurrentMilestoneIndex,
            EscrowIds = model.EscrowIds?.ToList(),
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
        // Compute absolute deadline from ActivatedAt + duration
        DateTimeOffset? absoluteDeadline = null;
        if (model.ActivatedAt.HasValue && !string.IsNullOrEmpty(model.Deadline))
        {
            var duration = ParseIsoDuration(model.Deadline);
            if (duration.HasValue)
            {
                absoluteDeadline = model.ActivatedAt.Value.Add(duration.Value);
            }
        }

        return new MilestoneInstanceResponse
        {
            Code = model.Code,
            Name = model.Name,
            Sequence = model.Sequence,
            Required = model.Required,
            Status = model.Status,
            CompletedAt = model.CompletedAt,
            FailedAt = model.FailedAt,
            ActivatedAt = model.ActivatedAt,
            Deadline = absoluteDeadline,
            DeadlineBehavior = model.DeadlineBehavior
        };
    }

    private BreachResponse MapBreachToResponse(BreachModel model)
    {
        return new BreachResponse
        {
            BreachId = model.BreachId,
            ContractId = model.ContractId,
            BreachingEntityId = model.BreachingEntityId,
            BreachingEntityType = model.BreachingEntityType,
            BreachType = model.BreachType,
            BreachedTermOrMilestone = model.BreachedTermOrMilestone,
            Description = model.Description,
            Status = model.Status,
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
            TemplateId = model.TemplateId,
            Code = model.Code,
            Name = model.Name,
            Description = model.Description,
            RealmId = model.RealmId,
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
            TemplateId = model.TemplateId,
            Code = model.Code,
            Name = model.Name,
            Description = model.Description,
            RealmId = model.RealmId,
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
            TemplateId = model.TemplateId,
            Code = model.Code,
            Name = model.Name,
            Description = model.Description,
            RealmId = model.RealmId,
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
            ContractId = model.ContractId,
            TemplateId = model.TemplateId,
            TemplateCode = model.TemplateCode,
            Status = model.Status,
            CreatedAt = model.CreatedAt
        });
    }

    private async Task PublishContractProposedEventAsync(ContractInstanceModel model, CancellationToken ct)
    {
        var parties = model.Parties?.Select(p => new PartyInfo
        {
            EntityId = p.EntityId,
            EntityType = p.EntityType,
            Role = p.Role
        }).ToList() ?? new List<PartyInfo>();

        await _messageBus.TryPublishAsync("contract.proposed", new ContractProposedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            ContractId = model.ContractId,
            TemplateId = model.TemplateId,
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
            ContractId = model.ContractId,
            ConsentingEntityId = party.EntityId,
            ConsentingEntityType = party.EntityType,
            Role = party.Role,
            RemainingConsentsNeeded = remaining
        });
    }

    private async Task PublishContractAcceptedEventAsync(ContractInstanceModel model, CancellationToken ct)
    {
        var parties = model.Parties?.Select(p => new PartyInfo
        {
            EntityId = p.EntityId,
            EntityType = p.EntityType,
            Role = p.Role
        }).ToList() ?? new List<PartyInfo>();

        await _messageBus.TryPublishAsync("contract.accepted", new ContractAcceptedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            ContractId = model.ContractId,
            TemplateCode = model.TemplateCode,
            Parties = parties,
            EffectiveFrom = model.EffectiveFrom
        });
    }

    private async Task PublishContractActivatedEventAsync(ContractInstanceModel model, CancellationToken ct)
    {
        var parties = model.Parties?.Select(p => new PartyInfo
        {
            EntityId = p.EntityId,
            EntityType = p.EntityType,
            Role = p.Role
        }).ToList() ?? new List<PartyInfo>();

        await _messageBus.TryPublishAsync("contract.activated", new ContractActivatedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            ContractId = model.ContractId,
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
            ContractId = contract.ContractId,
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
            ContractId = contract.ContractId,
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
            ContractId = contract.ContractId,
            BreachId = breach.BreachId,
            BreachingEntityId = breach.BreachingEntityId,
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
            ContractId = breach.ContractId,
            BreachId = breach.BreachId,
            CureEvidence = evidence
        });
    }

    private async Task PublishContractFulfilledEventAsync(ContractInstanceModel model, CancellationToken ct)
    {
        var parties = model.Parties?.Select(p => new PartyInfo
        {
            EntityId = p.EntityId,
            EntityType = p.EntityType,
            Role = p.Role
        }).ToList() ?? new List<PartyInfo>();

        await _messageBus.TryPublishAsync("contract.fulfilled", new ContractFulfilledEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            ContractId = model.ContractId,
            TemplateCode = model.TemplateCode,
            Parties = parties,
            MilestonesCompleted = model.Milestones?.Count(m => m.Status == MilestoneStatus.Completed) ?? 0
        });
    }

    private async Task PublishContractTerminatedEventAsync(
        ContractInstanceModel model, Guid terminatedById, EntityType terminatedByType,
        string? reason, bool wasBreachRelated, CancellationToken ct)
    {
        await _messageBus.TryPublishAsync("contract.terminated", new ContractTerminatedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            ContractId = model.ContractId,
            TerminatedByEntityId = terminatedById,
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
    public Guid TemplateId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Guid? RealmId { get; set; }
    public int MinParties { get; set; }
    public int MaxParties { get; set; }
    public List<PartyRoleModel>? PartyRoles { get; set; }
    public ContractTermsModel? DefaultTerms { get; set; }
    public List<MilestoneDefinitionModel>? Milestones { get; set; }
    public EnforcementMode DefaultEnforcementMode { get; set; } = EnforcementMode.EventOnly;
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
    public List<EntityType>? AllowedEntityTypes { get; set; }
}

/// <summary>
/// Internal model for storing contract terms.
/// </summary>
internal class ContractTermsModel
{
    public string? Duration { get; set; }
    public PaymentSchedule? PaymentSchedule { get; set; }
    public string? PaymentFrequency { get; set; }
    public TerminationPolicy? TerminationPolicy { get; set; }
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
    public MilestoneDeadlineBehavior? DeadlineBehavior { get; set; }
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
    public PreboundApiExecutionMode ExecutionMode { get; set; } = PreboundApiExecutionMode.Sync;
    public ResponseValidation? ResponseValidation { get; set; }
}

/// <summary>
/// Internal model for storing contract instances.
/// </summary>
internal class ContractInstanceModel
{
    public Guid ContractId { get; set; }
    public Guid TemplateId { get; set; }
    public string TemplateCode { get; set; } = string.Empty;
    public ContractStatus Status { get; set; } = ContractStatus.Draft;
    public List<ContractPartyModel>? Parties { get; set; }
    public ContractTermsModel? Terms { get; set; }
    public List<MilestoneInstanceModel>? Milestones { get; set; }
    public int CurrentMilestoneIndex { get; set; }
    public List<Guid>? EscrowIds { get; set; }
    public List<Guid>? BreachIds { get; set; }
    public DateTimeOffset? ProposedAt { get; set; }
    public DateTimeOffset? AcceptedAt { get; set; }
    public DateTimeOffset? EffectiveFrom { get; set; }
    public DateTimeOffset? EffectiveUntil { get; set; }
    public DateTimeOffset? TerminatedAt { get; set; }
    public GameMetadataModel? GameMetadata { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }

    // Guardian state (for escrow integration)
    public Guid? GuardianId { get; set; }
    public string? GuardianType { get; set; }
    public DateTimeOffset? LockedAt { get; set; }

    // Template values for clause execution (escrow integration)
    public Dictionary<string, string>? TemplateValues { get; set; }

    // Execution tracking (idempotency for escrow integration)
    public DateTimeOffset? ExecutedAt { get; set; }
    public string? ExecutionIdempotencyKey { get; set; }
    public List<DistributionRecordModel>? ExecutionDistributions { get; set; }
}

/// <summary>
/// Internal model for storing contract party information.
/// </summary>
internal class ContractPartyModel
{
    public Guid EntityId { get; set; }
    public EntityType EntityType { get; set; }
    public string Role { get; set; } = string.Empty;
    public ConsentStatus ConsentStatus { get; set; } = ConsentStatus.Pending;
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
    public MilestoneStatus Status { get; set; } = MilestoneStatus.Pending;
    public string? Deadline { get; set; }
    public MilestoneDeadlineBehavior? DeadlineBehavior { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
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
    public Guid BreachId { get; set; }
    public Guid ContractId { get; set; }
    public Guid BreachingEntityId { get; set; }
    public EntityType BreachingEntityType { get; set; }
    public BreachType BreachType { get; set; }
    public string? BreachedTermOrMilestone { get; set; }
    public string? Description { get; set; }
    public BreachStatus Status { get; set; } = BreachStatus.Detected;
    public DateTimeOffset DetectedAt { get; set; }
    public DateTimeOffset? CureDeadline { get; set; }
    public DateTimeOffset? CuredAt { get; set; }
    public DateTimeOffset? ConsequencesAppliedAt { get; set; }
}

#endregion
