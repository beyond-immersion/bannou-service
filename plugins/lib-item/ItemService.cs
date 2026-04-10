using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Contract;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Helpers;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Providers;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace BeyondImmersion.BannouService.Item;

/// <summary>
/// Implementation of the Item service.
/// Provides item template and instance management for games.
/// </summary>
[BannouService("item", typeof(IItemService), lifetime: ServiceLifetime.Scoped, layer: ServiceLayer.GameFoundation)]
public partial class ItemService : IItemService, ICleanDeprecatedEntity
{
    private readonly IMessageBus _messageBus;
    private readonly IDistributedLockProvider _lockProvider;
    private readonly IContractClient _contractClient;
    private readonly ITelemetryProvider _telemetryProvider;
    private readonly ILogger<ItemService> _logger;
    private readonly ItemServiceConfiguration _configuration;
    private readonly ItemInstanceEventBatcher _instanceEventBatcher;
    private readonly IReadOnlyList<IItemInstanceDestructionListener> _destructionListeners;

    /// <summary>Persistent store for item template models (MySQL-backed).</summary>
    private readonly IStateStore<ItemTemplateModel> _templateStore;

    /// <summary>String store for item template index keys (code lookups, game indexes).</summary>
    private readonly IStateStore<string> _templateStringStore;

    /// <summary>Redis cache store for item template models (read-through cache).</summary>
    private readonly IStateStore<ItemTemplateModel> _templateCacheStore;

    /// <summary>Persistent store for item instance models (MySQL-backed).</summary>
    private readonly IStateStore<ItemInstanceModel> _instanceStore;

    /// <summary>String store for item instance index keys (container/template indexes).</summary>
    private readonly IStateStore<string> _instanceStringStore;

    /// <summary>Queryable store for item instance models (MySQL LINQ queries).</summary>
    private readonly IQueryableStateStore<ItemInstanceModel> _instanceQueryableStore;

    /// <summary>Redis cache store for item instance models (read-through cache).</summary>
    private readonly IStateStore<ItemInstanceModel> _instanceCacheStore;

    // Parsed config defaults (boundary parsing per IMPLEMENTATION TENETS)
    private readonly ItemRarity _defaultRarity;
    private readonly WeightPrecision _defaultWeightPrecision;
    private readonly SoulboundType _defaultSoulboundType;

    /// <summary>
    /// Thread-safe dictionary for batching item use events by templateId+userId key.
    /// Key format: "{templateId}:{userId}". Values track uses within deduplication window.
    /// </summary>
    /// <remarks>
    /// Per IMPLEMENTATION TENETS: This local cache is acceptable because it's purely for
    /// event batching optimization, not authoritative state. Cross-instance inconsistency results
    /// in multiple smaller batches being published (not data loss). Authoritative item state
    /// lives in persistent stores via lib-state.
    /// </remarks>
    private static readonly ConcurrentDictionary<string, ItemUseBatchState> _useBatches = new();

    /// <summary>
    /// Thread-safe dictionary for batching item use failure events by templateId+userId key.
    /// Key format: "{templateId}:{userId}". Values track failures within deduplication window.
    /// </summary>
    /// <remarks>
    /// Per IMPLEMENTATION TENETS: This local cache is acceptable because it's purely for
    /// event batching optimization, not authoritative state. Cross-instance inconsistency results
    /// in multiple smaller batches being published (not data loss). Authoritative item state
    /// lives in persistent stores via lib-state.
    /// </remarks>
    private static readonly ConcurrentDictionary<string, ItemUseFailureBatchState> _failureBatches = new();

    // Template store key prefixes
    private const string TPL_PREFIX = "tpl:";
    private const string TPL_CODE_INDEX = "tpl-code:";
    private const string TPL_GAME_INDEX = "tpl-game:";
    private const string ALL_TEMPLATES_KEY = "all-templates";

    // Instance store key prefixes
    private const string INST_PREFIX = "inst:";
    private const string INST_CONTAINER_INDEX = "inst-container:";
    private const string INST_TEMPLATE_INDEX = "inst-template:";

    /// <summary>
    /// Builds the key for an item template.
    /// Format: {TPL_PREFIX}{templateId}
    /// </summary>
    internal static string BuildTemplateKey(Guid templateId)
        => $"{TPL_PREFIX}{templateId}";

    /// <summary>
    /// Builds the key for an item instance.
    /// Format: {INST_PREFIX}{instanceId}
    /// </summary>
    internal static string BuildInstanceKey(Guid instanceId)
        => $"{INST_PREFIX}{instanceId}";

    /// <summary>
    /// Initializes a new instance of the ItemService.
    /// </summary>
    /// <param name="messageBus">Message bus for event publishing.</param>
    /// <param name="stateStoreFactory">Factory for accessing state stores.</param>
    /// <param name="lockProvider">Provider for distributed locks.</param>
    /// <param name="contractClient">Client for Contract service (L1) - hard dependency per SERVICE HIERARCHY.</param>
    /// <param name="telemetryProvider">Telemetry provider for distributed tracing spans.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="configuration">Service configuration.</param>
    /// <param name="instanceEventBatcher">Batcher for item instance lifecycle events.</param>
    /// <param name="destructionListeners">DI-discovered listeners for item instance destruction (high-frequency exception per FOUNDATION TENETS).</param>
    public ItemService(
        IMessageBus messageBus,
        IStateStoreFactory stateStoreFactory,
        IDistributedLockProvider lockProvider,
        IContractClient contractClient,
        ITelemetryProvider telemetryProvider,
        ILogger<ItemService> logger,
        ItemServiceConfiguration configuration,
        ItemInstanceEventBatcher instanceEventBatcher,
        IEnumerable<IItemInstanceDestructionListener> destructionListeners)
    {
        _messageBus = messageBus;
        _lockProvider = lockProvider;
        _contractClient = contractClient;
        _telemetryProvider = telemetryProvider;
        _instanceEventBatcher = instanceEventBatcher;
        _destructionListeners = destructionListeners.ToList();
        _logger = logger;
        _configuration = configuration;

        _logger.LogInformation(
            "Item service initialized with {Count} destruction listeners: {Listeners}",
            _destructionListeners.Count,
            string.Join(", ", _destructionListeners.Select(l => l.GetType().Name)));

        // Constructor-cache all state store references per FOUNDATION TENETS
        _templateStore = stateStoreFactory.GetStore<ItemTemplateModel>(StateStoreDefinitions.ItemTemplateStore);
        _templateStringStore = stateStoreFactory.GetStore<string>(StateStoreDefinitions.ItemTemplateStore);
        _templateCacheStore = stateStoreFactory.GetStore<ItemTemplateModel>(StateStoreDefinitions.ItemTemplateCache);
        _instanceStore = stateStoreFactory.GetStore<ItemInstanceModel>(StateStoreDefinitions.ItemInstanceStore);
        _instanceStringStore = stateStoreFactory.GetStore<string>(StateStoreDefinitions.ItemInstanceStore);
        _instanceQueryableStore = stateStoreFactory.GetQueryableStore<ItemInstanceModel>(StateStoreDefinitions.ItemInstanceStore);
        _instanceCacheStore = stateStoreFactory.GetStore<ItemInstanceModel>(StateStoreDefinitions.ItemInstanceCache);

        // Configuration already provides typed enums (IMPLEMENTATION TENETS compliant)
        _defaultRarity = _configuration.DefaultRarity;
        _defaultWeightPrecision = _configuration.DefaultWeightPrecision;
        _defaultSoulboundType = _configuration.DefaultSoulboundType;
    }

    #region Template Operations

    /// <inheritdoc/>
    public async Task<(StatusCodes, ItemTemplateResponse?)> CreateItemTemplateAsync(
        CreateItemTemplateRequest body,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Creating item template with code {Code} for game {GameId}", body.Code, body.GameId);

        // Check code uniqueness within game using atomic claim
        var codeKey = $"{TPL_CODE_INDEX}{body.GameId}:{body.Code}";
        var (existingId, codeEtag) = await _templateStringStore.GetWithETagAsync(codeKey, cancellationToken);
        if (!string.IsNullOrEmpty(existingId))
        {
            _logger.LogWarning("Item template code already exists: {Code} for game {GameId}", body.Code, body.GameId);
            return (StatusCodes.Conflict, null);
        }

        var templateId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        var model = new ItemTemplateModel
        {
            TemplateId = templateId,
            Code = body.Code,
            GameId = body.GameId,
            Name = body.Name,
            Description = body.Description,
            Category = body.Category,
            Subcategory = body.Subcategory,
            Tags = body.Tags?.ToList() ?? new List<string>(),
            Rarity = body.Rarity ?? _defaultRarity,
            QuantityModel = body.QuantityModel,
            MaxStackSize = body.MaxStackSize,
            UnitOfMeasure = body.UnitOfMeasure,
            WeightPrecision = body.WeightPrecision ?? _defaultWeightPrecision,
            Weight = body.Weight,
            Volume = body.Volume,
            GridWidth = body.GridWidth,
            GridHeight = body.GridHeight,
            CanRotate = body.CanRotate,
            BaseValue = body.BaseValue,
            Tradeable = body.Tradeable,
            Destroyable = body.Destroyable,
            SoulboundType = body.SoulboundType ?? _defaultSoulboundType,
            HasDurability = body.HasDurability,
            MaxDurability = body.MaxDurability,
            Scope = body.Scope,
            AvailableRealms = body.AvailableRealms?.ToList(),
            Stats = body.Stats is not null ? BannouJson.Serialize(body.Stats) : null,
            Effects = body.Effects is not null ? BannouJson.Serialize(body.Effects) : null,
            Requirements = body.Requirements is not null ? BannouJson.Serialize(body.Requirements) : null,
            Display = body.Display is not null ? BannouJson.Serialize(body.Display) : null,
            Metadata = body.Metadata is not null ? BannouJson.Serialize(body.Metadata) : null,
            UseBehaviorContractTemplateId = body.UseBehaviorContractTemplateId,
            CanUseBehaviorContractTemplateId = body.CanUseBehaviorContractTemplateId,
            OnUseFailedBehaviorContractTemplateId = body.OnUseFailedBehaviorContractTemplateId,
            ItemUseBehavior = body.ItemUseBehavior ?? ItemUseBehavior.DestroyOnSuccess,
            CanUseBehavior = body.CanUseBehavior ?? CanUseBehavior.Block,
            IsActive = true,
            IsDeprecated = false,
            CreatedAt = now,
            UpdatedAt = now
        };

        // Claim the code index key atomically (prevents TOCTOU race on code uniqueness)
        // GetWithETagAsync returns null etag when key doesn't exist; TrySaveAsync treats
        // empty string as "no existing version" for new entries (will never execute for updates)
        var claimResult = await _templateStringStore.TrySaveAsync(codeKey, templateId.ToString(), codeEtag ?? string.Empty, cancellationToken: cancellationToken);
        if (claimResult == null)
        {
            _logger.LogWarning("Item template code claimed concurrently: {Code} for game {GameId}", body.Code, body.GameId);
            return (StatusCodes.Conflict, null);
        }

        // Save template
        await _templateStore.SaveAsync($"{TPL_PREFIX}{templateId}", model, cancellationToken: cancellationToken);
        await AddToListAsync(_templateStringStore, $"{TPL_GAME_INDEX}{body.GameId}", templateId.ToString(), cancellationToken);
        await AddToListAsync(_templateStringStore, ALL_TEMPLATES_KEY, templateId.ToString(), cancellationToken);

        // Populate cache
        await PopulateTemplateCacheAsync(templateId.ToString(), model, cancellationToken);

        // Publish lifecycle event
        await _messageBus.PublishItemTemplateCreatedAsync(new ItemTemplateCreatedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = now,
            TemplateId = templateId,
            Code = model.Code,
            GameId = model.GameId,
            Name = model.Name,
            Description = model.Description,
            Category = model.Category,
            Rarity = model.Rarity,
            QuantityModel = model.QuantityModel,
            MaxStackSize = model.MaxStackSize,
            Scope = model.Scope,
            SoulboundType = model.SoulboundType,
            Tradeable = model.Tradeable,
            Destroyable = model.Destroyable,
            HasDurability = model.HasDurability,
            MaxDurability = model.MaxDurability,
            IsActive = model.IsActive,
            IsDeprecated = model.IsDeprecated,
            DeprecatedAt = model.DeprecatedAt,
            DeprecationReason = model.DeprecationReason,
            MigrationTargetId = model.MigrationTargetId,
            CreatedAt = now,
            UpdatedAt = model.UpdatedAt
        }, cancellationToken);

        _logger.LogDebug("Created item template {TemplateId} code={Code}", templateId, body.Code);
        return (StatusCodes.OK, MapTemplateToResponse(model));
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, ItemTemplateResponse?)> GetItemTemplateAsync(
        GetItemTemplateRequest body,
        CancellationToken cancellationToken = default)
    {
        var model = await ResolveTemplateAsync(body.TemplateId?.ToString(), body.Code, body.GameId, cancellationToken);
        if (model is null)
        {
            return (StatusCodes.NotFound, null);
        }
        return (StatusCodes.OK, MapTemplateToResponse(model));
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, ListItemTemplatesResponse?)> ListItemTemplatesAsync(
        ListItemTemplatesRequest body,
        CancellationToken cancellationToken = default)
    {
        // Get templates for game
        string listKey = !string.IsNullOrEmpty(body.GameId)
            ? $"{TPL_GAME_INDEX}{body.GameId}"
            : ALL_TEMPLATES_KEY;

        var idsJson = await _templateStringStore.GetAsync(listKey, cancellationToken);
        var ids = string.IsNullOrEmpty(idsJson)
            ? new List<string>()
            : BannouJson.Deserialize<List<string>>(idsJson) ?? new List<string>();

        var templates = new List<ItemTemplateResponse>();
        foreach (var id in ids)
        {
            var model = await GetTemplateWithCacheAsync(id, cancellationToken);
            if (model is null) continue;

            // Apply filters
            if (!body.IncludeInactive && !model.IsActive) continue;
            if (!body.IncludeDeprecated && model.IsDeprecated) continue;
            if (body.Category != default && model.Category != body.Category) continue;
            if (!string.IsNullOrEmpty(body.Subcategory) && model.Subcategory != body.Subcategory) continue;
            if (body.Rarity != default && model.Rarity != body.Rarity) continue;
            if (body.Scope != default && model.Scope != body.Scope) continue;
            if (body.RealmId.HasValue && model.AvailableRealms is not null &&
                !model.AvailableRealms.Contains(body.RealmId.Value)) continue;
            if (body.Tags is not null && body.Tags.Count > 0 &&
                !body.Tags.All(t => model.Tags.Contains(t))) continue;
            if (!string.IsNullOrEmpty(body.Search) &&
                !model.Name.Contains(body.Search, StringComparison.OrdinalIgnoreCase) &&
                (model.Description is null || !model.Description.Contains(body.Search, StringComparison.OrdinalIgnoreCase))) continue;

            templates.Add(MapTemplateToResponse(model));
        }

        var totalCount = templates.Count;
        var paged = templates.Skip(body.Offset).Take(body.Limit).ToList();

        return (StatusCodes.OK, new ListItemTemplatesResponse
        {
            Templates = paged,
            TotalCount = totalCount
        });
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, ItemTemplateResponse?)> UpdateItemTemplateAsync(
        UpdateItemTemplateRequest body,
        CancellationToken cancellationToken = default)
    {
        var model = await _templateStore.GetAsync($"{TPL_PREFIX}{body.TemplateId}", cancellationToken);

        if (model is null)
        {
            return (StatusCodes.NotFound, null);
        }

        var now = DateTimeOffset.UtcNow;

        // Update mutable fields using ChangeFields 3-state semantics (Issue #722).
        // Fields explicitly set — including to null — are applied; absent fields are skipped.
        var changedFields = new List<string>();
        if (body.ChangeFields.IsFieldSet("name") && !string.IsNullOrEmpty(body.Name)) { model.Name = body.Name; changedFields.Add("name"); }
        if (body.ChangeFields.IsFieldSet("description")) { model.Description = body.Description; changedFields.Add("description"); }
        if (body.ChangeFields.IsFieldSet("subcategory")) { model.Subcategory = body.Subcategory; changedFields.Add("subcategory"); }
        if (body.ChangeFields.IsFieldSet("tags")) { model.Tags = body.Tags?.ToList() ?? new List<string>(); changedFields.Add("tags"); }
        if (body.ChangeFields.IsFieldSet("rarity") && body.Rarity.HasValue) { model.Rarity = body.Rarity.Value; changedFields.Add("rarity"); }
        if (body.ChangeFields.IsFieldSet("weight")) { model.Weight = body.Weight; changedFields.Add("weight"); }
        if (body.ChangeFields.IsFieldSet("volume")) { model.Volume = body.Volume; changedFields.Add("volume"); }
        if (body.ChangeFields.IsFieldSet("gridWidth")) { model.GridWidth = body.GridWidth; changedFields.Add("gridWidth"); }
        if (body.ChangeFields.IsFieldSet("gridHeight")) { model.GridHeight = body.GridHeight; changedFields.Add("gridHeight"); }
        if (body.ChangeFields.IsFieldSet("canRotate")) { model.CanRotate = body.CanRotate; changedFields.Add("canRotate"); }
        if (body.ChangeFields.IsFieldSet("baseValue")) { model.BaseValue = body.BaseValue; changedFields.Add("baseValue"); }
        if (body.ChangeFields.IsFieldSet("tradeable") && body.Tradeable.HasValue) { model.Tradeable = body.Tradeable.Value; changedFields.Add("tradeable"); }
        if (body.ChangeFields.IsFieldSet("destroyable") && body.Destroyable.HasValue) { model.Destroyable = body.Destroyable.Value; changedFields.Add("destroyable"); }
        if (body.ChangeFields.IsFieldSet("maxDurability")) { model.MaxDurability = body.MaxDurability; changedFields.Add("maxDurability"); }
        if (body.ChangeFields.IsFieldSet("availableRealms")) { model.AvailableRealms = body.AvailableRealms?.ToList(); changedFields.Add("availableRealms"); }
        if (body.ChangeFields.IsFieldSet("stats")) { model.Stats = body.Stats is not null ? BannouJson.Serialize(body.Stats) : null; changedFields.Add("stats"); }
        if (body.ChangeFields.IsFieldSet("effects")) { model.Effects = body.Effects is not null ? BannouJson.Serialize(body.Effects) : null; changedFields.Add("effects"); }
        if (body.ChangeFields.IsFieldSet("requirements")) { model.Requirements = body.Requirements is not null ? BannouJson.Serialize(body.Requirements) : null; changedFields.Add("requirements"); }
        if (body.ChangeFields.IsFieldSet("display")) { model.Display = body.Display is not null ? BannouJson.Serialize(body.Display) : null; changedFields.Add("display"); }
        if (body.ChangeFields.IsFieldSet("metadata")) { model.Metadata = body.Metadata is not null ? BannouJson.Serialize(body.Metadata) : null; changedFields.Add("metadata"); }
        // Contract template IDs: null now genuinely clears them (3-state semantics)
        if (body.ChangeFields.IsFieldSet("useBehaviorContractTemplateId")) { model.UseBehaviorContractTemplateId = body.UseBehaviorContractTemplateId; changedFields.Add("useBehaviorContractTemplateId"); }
        if (body.ChangeFields.IsFieldSet("canUseBehaviorContractTemplateId")) { model.CanUseBehaviorContractTemplateId = body.CanUseBehaviorContractTemplateId; changedFields.Add("canUseBehaviorContractTemplateId"); }
        if (body.ChangeFields.IsFieldSet("onUseFailedBehaviorContractTemplateId")) { model.OnUseFailedBehaviorContractTemplateId = body.OnUseFailedBehaviorContractTemplateId; changedFields.Add("onUseFailedBehaviorContractTemplateId"); }
        if (body.ChangeFields.IsFieldSet("itemUseBehavior") && body.ItemUseBehavior.HasValue) { model.ItemUseBehavior = body.ItemUseBehavior.Value; changedFields.Add("itemUseBehavior"); }
        if (body.ChangeFields.IsFieldSet("canUseBehavior") && body.CanUseBehavior.HasValue) { model.CanUseBehavior = body.CanUseBehavior.Value; changedFields.Add("canUseBehavior"); }
        if (body.ChangeFields.IsFieldSet("isActive") && body.IsActive.HasValue) { model.IsActive = body.IsActive.Value; changedFields.Add("isActive"); }
        model.UpdatedAt = now;

        await _templateStore.SaveAsync($"{TPL_PREFIX}{body.TemplateId}", model, cancellationToken: cancellationToken);

        // Invalidate cache after write
        await InvalidateTemplateCacheAsync(body.TemplateId.ToString(), cancellationToken);

        await _messageBus.PublishItemTemplateUpdatedAsync(new ItemTemplateUpdatedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = now,
            TemplateId = body.TemplateId,
            Code = model.Code,
            GameId = model.GameId,
            Name = model.Name,
            Description = model.Description,
            Category = model.Category,
            Rarity = model.Rarity,
            QuantityModel = model.QuantityModel,
            MaxStackSize = model.MaxStackSize,
            Scope = model.Scope,
            SoulboundType = model.SoulboundType,
            Tradeable = model.Tradeable,
            Destroyable = model.Destroyable,
            HasDurability = model.HasDurability,
            MaxDurability = model.MaxDurability,
            IsActive = model.IsActive,
            IsDeprecated = model.IsDeprecated,
            DeprecatedAt = model.DeprecatedAt,
            DeprecationReason = model.DeprecationReason,
            MigrationTargetId = model.MigrationTargetId,
            CreatedAt = model.CreatedAt,
            UpdatedAt = now,
            ChangedFields = changedFields
        }, cancellationToken);

        _logger.LogDebug("Updated item template {TemplateId}", body.TemplateId);
        return (StatusCodes.OK, MapTemplateToResponse(model));
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, ItemTemplateResponse?)> DeprecateItemTemplateAsync(
        DeprecateItemTemplateRequest body,
        CancellationToken cancellationToken = default)
    {
        var model = await _templateStore.GetAsync($"{TPL_PREFIX}{body.TemplateId}", cancellationToken);

        if (model is null)
        {
            return (StatusCodes.NotFound, null);
        }

        // Per IMPLEMENTATION TENETS: idempotent deprecation — return OK when already deprecated
        if (model.IsDeprecated)
        {
            return (StatusCodes.OK, MapTemplateToResponse(model));
        }

        var now = DateTimeOffset.UtcNow;
        model.IsDeprecated = true;
        model.DeprecatedAt = now;
        model.DeprecationReason = body.Reason;
        model.MigrationTargetId = body.MigrationTargetId;
        model.UpdatedAt = now;

        await _templateStore.SaveAsync($"{TPL_PREFIX}{body.TemplateId}", model, cancellationToken: cancellationToken);

        // Invalidate cache after write
        await InvalidateTemplateCacheAsync(body.TemplateId.ToString(), cancellationToken);

        // Per IMPLEMENTATION TENETS: deprecation published as *.updated with changedFields
        await _messageBus.PublishItemTemplateUpdatedAsync(new ItemTemplateUpdatedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = now,
            TemplateId = body.TemplateId,
            Code = model.Code,
            GameId = model.GameId,
            Name = model.Name,
            Description = model.Description,
            Category = model.Category,
            Rarity = model.Rarity,
            QuantityModel = model.QuantityModel,
            MaxStackSize = model.MaxStackSize,
            Scope = model.Scope,
            SoulboundType = model.SoulboundType,
            Tradeable = model.Tradeable,
            Destroyable = model.Destroyable,
            HasDurability = model.HasDurability,
            MaxDurability = model.MaxDurability,
            IsActive = model.IsActive,
            IsDeprecated = model.IsDeprecated,
            DeprecatedAt = model.DeprecatedAt,
            DeprecationReason = model.DeprecationReason,
            MigrationTargetId = model.MigrationTargetId,
            CreatedAt = model.CreatedAt,
            UpdatedAt = now,
            ChangedFields = new List<string> { "isDeprecated", "deprecatedAt", "deprecationReason", "migrationTargetId" }
        }, cancellationToken);

        _logger.LogDebug("Deprecated item template {TemplateId}", body.TemplateId);
        return (StatusCodes.OK, MapTemplateToResponse(model));
    }

    #endregion

    #region Instance Operations

    /// <inheritdoc/>
    public async Task<(StatusCodes, ItemInstanceResponse?)> CreateItemInstanceAsync(
        CreateItemInstanceRequest body,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Creating item instance for template {TemplateId} in container {ContainerId}",
            body.TemplateId, body.ContainerId);

        // Get template (uses cache)
        var template = await GetTemplateWithCacheAsync(body.TemplateId.ToString(), cancellationToken);
        if (template is null)
        {
            _logger.LogWarning("Template not found: {TemplateId}", body.TemplateId);
            return (StatusCodes.NotFound, null);
        }

        if (!template.IsActive)
        {
            _logger.LogWarning("Template is not active: {TemplateId}", body.TemplateId);
            return (StatusCodes.BadRequest, null);
        }

        // Per IMPLEMENTATION TENETS: deprecated templates must not produce new instances
        if (template.IsDeprecated)
        {
            _logger.LogWarning("Cannot create instance from deprecated template: {TemplateId}", body.TemplateId);
            return (StatusCodes.BadRequest, null);
        }

        var instanceId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        // Validate quantity based on quantity model
        var quantity = body.Quantity;
        if (template.QuantityModel == QuantityModel.Unique)
        {
            quantity = 1;
        }
        else if (template.QuantityModel == QuantityModel.Discrete)
        {
            quantity = Math.Floor(quantity);
            if (quantity > template.MaxStackSize)
            {
                quantity = template.MaxStackSize;
            }
        }

        var model = new ItemInstanceModel
        {
            InstanceId = instanceId,
            TemplateId = body.TemplateId,
            ContainerId = body.ContainerId,
            RealmId = body.RealmId,
            Quantity = quantity,
            SlotIndex = body.SlotIndex,
            SlotX = body.SlotX,
            SlotY = body.SlotY,
            Rotated = body.Rotated,
            CurrentDurability = body.CurrentDurability ?? template.MaxDurability,
            CustomStats = body.CustomStats is not null ? BannouJson.Serialize(body.CustomStats) : null,
            CustomName = body.CustomName,
            InstanceMetadata = body.InstanceMetadata is not null ? BannouJson.Serialize(body.InstanceMetadata) : null,
            OriginType = body.OriginType,
            OriginId = body.OriginId,
            ContractInstanceId = body.ContractInstanceId,
            // Default to lifecycle if contractInstanceId is provided but bindingType is not
            ContractBindingType = body.ContractBindingType
                ?? (body.ContractInstanceId.HasValue ? ContractBindingType.Lifecycle : ContractBindingType.None),
            CreatedAt = now
        };

        await _instanceStore.SaveAsync($"{INST_PREFIX}{instanceId}", model, cancellationToken: cancellationToken);
        await AddToListAsync(_instanceStringStore, $"{INST_CONTAINER_INDEX}{body.ContainerId}", instanceId.ToString(), cancellationToken);
        await AddToListAsync(_instanceStringStore, $"{INST_TEMPLATE_INDEX}{body.TemplateId}", instanceId.ToString(), cancellationToken);

        // Populate instance cache
        await PopulateInstanceCacheAsync(instanceId.ToString(), model, cancellationToken);

        _instanceEventBatcher.AddCreated(new ItemInstanceBatchEntry
        {
            InstanceId = instanceId,
            TemplateId = body.TemplateId,
            ContainerId = body.ContainerId,
            RealmId = body.RealmId,
            Quantity = quantity,
            SlotIndex = model.SlotIndex,
            SlotX = model.SlotX,
            SlotY = model.SlotY,
            Rotated = model.Rotated,
            CurrentDurability = model.CurrentDurability,
            BoundToId = model.BoundToId,
            BoundAt = model.BoundAt,
            CustomName = model.CustomName,
            OriginType = body.OriginType,
            OriginId = model.OriginId,
            CreatedAt = now,
            ModifiedAt = model.ModifiedAt
        });

        _logger.LogDebug("Created item instance {InstanceId}", instanceId);
        return (StatusCodes.OK, MapInstanceToResponse(model));
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, ItemInstanceResponse?)> GetItemInstanceAsync(
        GetItemInstanceRequest body,
        CancellationToken cancellationToken = default)
    {
        var model = await GetInstanceWithCacheAsync(body.InstanceId.ToString(), cancellationToken);

        if (model is null)
        {
            return (StatusCodes.NotFound, null);
        }

        return (StatusCodes.OK, MapInstanceToResponse(model));
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, ItemInstanceResponse?)> ModifyItemInstanceAsync(
        ModifyItemInstanceRequest body,
        CancellationToken cancellationToken = default)
    {
        // Container changes require a lock to prevent race conditions on index updates.
        // Uses ChangeFields 3-state semantics: setting newContainerId explicitly (to a value
        // or null) indicates intent to change containers (Issue #722 / replaces #721 workaround).
        if (body.ChangeFields.IsFieldSet("newContainerId"))
        {
            return await ModifyItemInstanceWithLockAsync(body, cancellationToken);
        }

        // Non-container changes don't need locking
        return await ModifyItemInstanceInternalAsync(body, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, ItemInstanceResponse?)> BindItemInstanceAsync(
        BindItemInstanceRequest body,
        CancellationToken cancellationToken = default)
    {
        var model = await _instanceStore.GetAsync($"{INST_PREFIX}{body.InstanceId}", cancellationToken);

        if (model is null)
        {
            return (StatusCodes.NotFound, null);
        }

        if (model.BoundToId is not null)
        {
            if (!_configuration.BindingAllowAdminOverride)
            {
                _logger.LogWarning("Item {InstanceId} is already bound to {BoundToId}", body.InstanceId, model.BoundToId);
                return (StatusCodes.Conflict, null);
            }
            _logger.LogDebug("Admin override: rebinding item {InstanceId} from {OldBound} to {NewBound}",
                body.InstanceId, model.BoundToId, body.CharacterId);
        }

        var now = DateTimeOffset.UtcNow;
        model.BoundToId = body.CharacterId;
        model.BoundAt = now;
        model.ModifiedAt = now;

        await _instanceStore.SaveAsync($"{INST_PREFIX}{body.InstanceId}", model, cancellationToken: cancellationToken);

        // Invalidate cache after write
        await InvalidateInstanceCacheAsync(body.InstanceId.ToString(), cancellationToken);

        // Get template for event enrichment (uses cache)
        var template = await GetTemplateWithCacheAsync(model.TemplateId.ToString(), cancellationToken);

        if (template is null)
        {
            _logger.LogWarning("Template {TemplateId} not found when binding instance {InstanceId}, possible data inconsistency",
                model.TemplateId, body.InstanceId);
        }

        await _messageBus.PublishItemInstanceBoundAsync(new ItemInstanceBoundEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = now,
            InstanceId = body.InstanceId,
            TemplateId = model.TemplateId,
            TemplateCode = template?.Code,
            RealmId = model.RealmId,
            CharacterId = body.CharacterId,
            BindType = body.BindType
        }, cancellationToken);

        _logger.LogDebug("Bound item {InstanceId} to character {CharacterId}", body.InstanceId, body.CharacterId);
        return (StatusCodes.OK, MapInstanceToResponse(model));
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, ItemInstanceResponse?)> UnbindItemInstanceAsync(
        UnbindItemInstanceRequest body,
        CancellationToken cancellationToken = default)
    {
        var model = await _instanceStore.GetAsync($"{INST_PREFIX}{body.InstanceId}", cancellationToken);

        if (model is null)
        {
            return (StatusCodes.NotFound, null);
        }

        if (model.BoundToId is null)
        {
            _logger.LogWarning("Item {InstanceId} is not bound", body.InstanceId);
            return (StatusCodes.BadRequest, null);
        }

        var previousCharacterId = model.BoundToId.Value;
        var now = DateTimeOffset.UtcNow;

        // Clear binding
        model.BoundToId = null;
        model.BoundAt = null;
        model.ModifiedAt = now;

        await _instanceStore.SaveAsync($"{INST_PREFIX}{body.InstanceId}", model, cancellationToken: cancellationToken);

        // Invalidate cache after write
        await InvalidateInstanceCacheAsync(body.InstanceId.ToString(), cancellationToken);

        // Get template for event enrichment (uses cache)
        var template = await GetTemplateWithCacheAsync(model.TemplateId.ToString(), cancellationToken);

        if (template is null)
        {
            _logger.LogWarning("Template {TemplateId} not found when unbinding instance {InstanceId}, possible data inconsistency",
                model.TemplateId, body.InstanceId);
        }

        await _messageBus.PublishItemInstanceUnboundAsync(new ItemInstanceUnboundEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = now,
            InstanceId = body.InstanceId,
            TemplateId = model.TemplateId,
            TemplateCode = template?.Code,
            RealmId = model.RealmId,
            PreviousCharacterId = previousCharacterId,
            Reason = body.Reason
        }, cancellationToken);

        _logger.LogDebug("Unbound item {InstanceId} from character {CharacterId} reason={Reason}",
            body.InstanceId, previousCharacterId, body.Reason);
        return (StatusCodes.OK, MapInstanceToResponse(model));
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, DestroyItemInstanceResponse?)> DestroyItemInstanceAsync(
        DestroyItemInstanceRequest body,
        CancellationToken cancellationToken = default)
    {
        var model = await _instanceStore.GetAsync($"{INST_PREFIX}{body.InstanceId}", cancellationToken);

        if (model is null)
        {
            return (StatusCodes.NotFound, null);
        }

        // Get template to check destroyable (uses cache)
        var template = await GetTemplateWithCacheAsync(model.TemplateId.ToString(), cancellationToken);
        if (template is not null && !template.Destroyable && body.Reason != DestroyReason.Admin)
        {
            _logger.LogWarning("Item {InstanceId} is not destroyable", body.InstanceId);
            return (StatusCodes.BadRequest, null);
        }

        var now = DateTimeOffset.UtcNow;

        // Remove from indexes
        if (model.ContainerId.HasValue)
        {
            await RemoveFromListAsync(_instanceStringStore, $"{INST_CONTAINER_INDEX}{model.ContainerId.Value}", body.InstanceId.ToString(), cancellationToken);
        }
        await RemoveFromListAsync(_instanceStringStore, $"{INST_TEMPLATE_INDEX}{model.TemplateId}", body.InstanceId.ToString(), cancellationToken);

        // Delete instance
        await _instanceStore.DeleteAsync($"{INST_PREFIX}{body.InstanceId}", cancellationToken);

        // Invalidate cache after delete
        await InvalidateInstanceCacheAsync(body.InstanceId.ToString(), cancellationToken);

        _instanceEventBatcher.AddDestroyed(new ItemInstanceBatchDestroyedEntry
        {
            InstanceId = body.InstanceId,
            TemplateId = model.TemplateId,
            ContainerId = model.ContainerId,
            RealmId = model.RealmId,
            Quantity = model.Quantity,
            OriginType = model.OriginType,
            CreatedAt = model.CreatedAt,
            ModifiedAt = now
        });

        // Dispatch to destruction listeners (high-frequency exception per FOUNDATION TENETS)
        // Listeners clean up their own dependent state (distributed writes, multi-node safe)
        await ItemInstanceDestructionDispatcher.DispatchInstanceDestroyedAsync(
            _destructionListeners,
            new ItemInstanceDestructionNotification(
                body.InstanceId,
                model.TemplateId,
                template?.GameId,
                model.ContainerId,
                model.RealmId,
                now),
            _telemetryProvider,
            _logger,
            cancellationToken);

        _logger.LogDebug("Destroyed item instance {InstanceId} reason={Reason}", body.InstanceId, body.Reason);
        return (StatusCodes.OK, new DestroyItemInstanceResponse
        {
            TemplateId = model.TemplateId
        });
    }

    #endregion

    #region Item Use Operations

    /// <inheritdoc/>
    public async Task<(StatusCodes, UseItemResponse?)> UseItemAsync(
        UseItemRequest body,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug(
            "Using item instance {InstanceId} by user {UserId} ({UserType})",
            body.InstanceId, body.UserId, body.UserType);

        // 1. Load instance
        var instance = await GetInstanceWithCacheAsync(body.InstanceId.ToString(), cancellationToken);
        if (instance is null)
        {
            _logger.LogDebug("Item instance {InstanceId} not found", body.InstanceId);
            return (StatusCodes.NotFound, null);
        }

        // 2. Load template (uses cache)
        var template = await GetTemplateWithCacheAsync(instance.TemplateId.ToString(), cancellationToken);
        if (template is null)
        {
            _logger.LogWarning(
                "Template {TemplateId} not found for instance {InstanceId}, possible data inconsistency",
                instance.TemplateId, body.InstanceId);
            return (StatusCodes.InternalServerError, null);
        }

        // 3. Check if item use is disabled
        if (template.ItemUseBehavior == ItemUseBehavior.Disabled)
        {
            _logger.LogDebug(
                "Item template {TemplateId} ({Code}) has use behavior disabled",
                template.TemplateId, template.Code);
            return (StatusCodes.BadRequest, null);
        }

        // 4. Validate template has behavior contract
        if (!template.UseBehaviorContractTemplateId.HasValue)
        {
            _logger.LogDebug(
                "Item template {TemplateId} ({Code}) has no behavior contract",
                template.TemplateId, template.Code);
            return (StatusCodes.BadRequest, null);
        }

        // 5. Execute CanUse validation if configured
        if (template.CanUseBehaviorContractTemplateId.HasValue &&
            template.CanUseBehavior != CanUseBehavior.Disabled)
        {
            var (validationPassed, validationFailureReason) = await ExecuteCanUseValidationAsync(
                template.CanUseBehaviorContractTemplateId.Value,
                body.UserId,
                body.UserType,
                body.InstanceId,
                template.TemplateId,
                body.Context,
                cancellationToken);

            if (!validationPassed)
            {
                if (template.CanUseBehavior == CanUseBehavior.Block)
                {
                    _logger.LogDebug(
                        "CanUse validation blocked for item {InstanceId}: {Reason}",
                        body.InstanceId, validationFailureReason);
                    return (StatusCodes.BadRequest, null);
                }
                else // warn_and_proceed
                {
                    _logger.LogWarning(
                        "CanUse validation failed but proceeding for item {InstanceId}: {Reason}",
                        body.InstanceId, validationFailureReason);
                }
            }
        }

        // 6. Create contract instance with user + system parties
        var systemPartyId = GetOrComputeSystemPartyId(template.GameId);
        var contractInstanceId = await CreateItemUseContractInstanceAsync(
            template.UseBehaviorContractTemplateId.Value,
            body.UserId,
            body.UserType,
            systemPartyId,
            body.InstanceId,
            template.TemplateId,
            body.Context,
            cancellationToken);

        if (!contractInstanceId.HasValue)
        {
            _logger.LogWarning(
                "Failed to create contract instance for item use: template={TemplateId}, user={UserId}",
                template.UseBehaviorContractTemplateId.Value, body.UserId);

            await RecordUseFailureAsync(
                body.InstanceId,
                template.TemplateId,
                template.Code,
                body.UserId,
                body.UserType,
                "Failed to create contract instance",
                cancellationToken);

            return (StatusCodes.BadRequest, null);
        }

        // 7. Complete the "use" milestone (triggers prebound APIs)
        var milestoneSuccess = await CompleteUseMilestoneAsync(
            contractInstanceId.Value,
            cancellationToken);

        if (!milestoneSuccess)
        {
            _logger.LogWarning(
                "Use milestone failed for item {InstanceId}, contract {ContractId}",
                body.InstanceId, contractInstanceId.Value);

            // Execute OnUseFailed handler if configured
            if (template.OnUseFailedBehaviorContractTemplateId.HasValue)
            {
                await ExecuteOnUseFailedHandlerAsync(
                    template.OnUseFailedBehaviorContractTemplateId.Value,
                    body.UserId,
                    body.UserType,
                    body.InstanceId,
                    template.TemplateId,
                    "Contract use milestone failed",
                    body.Context,
                    cancellationToken);
            }

            await RecordUseFailureAsync(
                body.InstanceId,
                template.TemplateId,
                template.Code,
                body.UserId,
                body.UserType,
                "Contract use milestone failed",
                cancellationToken);

            // Check if item should be consumed on failure (DESTROY_ALWAYS)
            if (template.ItemUseBehavior == ItemUseBehavior.DestroyAlways)
            {
                await ConsumeItemAsync(
                    body.InstanceId,
                    instance,
                    template,
                    cancellationToken);
            }

            return (StatusCodes.BadRequest, null);
        }

        // 8. Consume item on success (per itemUseBehavior: DESTROY_ON_SUCCESS or DESTROY_ALWAYS)
        var (consumed, remainingQuantity) = await ConsumeItemAsync(
            body.InstanceId,
            instance,
            template,
            cancellationToken);

        // 9. Record success for batched event publishing
        await RecordUseSuccessAsync(
            body.InstanceId,
            template.TemplateId,
            template.Code,
            body.UserId,
            body.UserType,
            body.TargetId,
            body.TargetType,
            consumed,
            contractInstanceId.Value,
            cancellationToken);

        _logger.LogDebug(
            "Item {InstanceId} used successfully, consumed={Consumed}, remaining={Remaining}",
            body.InstanceId, consumed, remainingQuantity);

        return (StatusCodes.OK, new UseItemResponse
        {
            TemplateId = template.TemplateId,
            ContractInstanceId = contractInstanceId.Value,
            Consumed = consumed,
            RemainingQuantity = remainingQuantity
        });
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, UseItemStepResponse?)> UseItemStepAsync(
        UseItemStepRequest body,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug(
            "UseItemStep: instance={InstanceId}, user={UserId}, milestone={MilestoneCode}",
            body.InstanceId, body.UserId, body.MilestoneCode);

        // 1. Load instance (read is safe without lock)
        var instance = await GetInstanceWithCacheAsync(body.InstanceId.ToString(), cancellationToken);
        if (instance is null)
        {
            _logger.LogDebug("Item instance {InstanceId} not found", body.InstanceId);
            return (StatusCodes.NotFound, null);
        }

        // 2. Load template
        var template = await GetTemplateWithCacheAsync(instance.TemplateId.ToString(), cancellationToken);
        if (template is null)
        {
            _logger.LogWarning(
                "Template {TemplateId} not found for instance {InstanceId}",
                instance.TemplateId, body.InstanceId);
            return (StatusCodes.InternalServerError, null);
        }

        // 3. Check if item use is disabled
        if (template.ItemUseBehavior == ItemUseBehavior.Disabled)
        {
            return (StatusCodes.BadRequest, null);
        }

        // 4. Validate template has behavior contract
        if (!template.UseBehaviorContractTemplateId.HasValue)
        {
            return (StatusCodes.BadRequest, null);
        }

        // 5. Acquire distributed lock for this instance
        var lockKey = $"item-use-step:{body.InstanceId}";
        var lockOwner = $"use-step-{Guid.NewGuid():N}";

        await using var lockHandle = await _lockProvider.LockAsync(
            StateStoreDefinitions.ItemLock,
            lockKey,
            lockOwner,
            _configuration.UseStepLockTimeoutSeconds,
            cancellationToken);

        if (!lockHandle.Success)
        {
            _logger.LogWarning(
                "Failed to acquire lock for UseItemStep on {InstanceId}",
                body.InstanceId);
            return (StatusCodes.Conflict, null);
        }

        // 6. Re-read instance inside lock to get current state
        var (currentInstance, etag) = await _instanceStore.GetWithETagAsync(
            $"{INST_PREFIX}{body.InstanceId}",
            cancellationToken);

        if (currentInstance is null)
        {
            return (StatusCodes.NotFound, null);
        }

        Guid contractInstanceId;
        var isFirstStep = !currentInstance.ContractInstanceId.HasValue;

        if (isFirstStep)
        {
            // 7a. First step: Execute CanUse validation and create contract

            // Execute CanUse validation if configured
            if (template.CanUseBehaviorContractTemplateId.HasValue &&
                template.CanUseBehavior != CanUseBehavior.Disabled)
            {
                var (validationPassed, validationFailureReason) = await ExecuteCanUseValidationAsync(
                    template.CanUseBehaviorContractTemplateId.Value,
                    body.UserId,
                    body.UserType,
                    body.InstanceId,
                    template.TemplateId,
                    body.Context,
                    cancellationToken);

                if (!validationPassed)
                {
                    if (template.CanUseBehavior == CanUseBehavior.Block)
                    {
                        return (StatusCodes.BadRequest, null);
                    }
                    else // warn_and_proceed
                    {
                        _logger.LogWarning(
                            "CanUse validation failed but proceeding for item {InstanceId}: {Reason}",
                            body.InstanceId, validationFailureReason);
                    }
                }
            }

            // Create contract instance
            var systemPartyId = GetOrComputeSystemPartyId(template.GameId);
            var newContractId = await CreateItemUseContractInstanceAsync(
                template.UseBehaviorContractTemplateId.Value,
                body.UserId,
                body.UserType,
                systemPartyId,
                body.InstanceId,
                template.TemplateId,
                body.Context,
                cancellationToken);

            if (!newContractId.HasValue)
            {
                return (StatusCodes.BadRequest, null);
            }

            contractInstanceId = newContractId.Value;

            // Store contract on instance with session binding
            currentInstance.ContractInstanceId = contractInstanceId;
            currentInstance.ContractBindingType = ContractBindingType.Session;
            currentInstance.ModifiedAt = DateTimeOffset.UtcNow;

            // Save updated instance
            // etag is non-null here (instance was found above); coalesce satisfies compiler nullable analysis
            var saveResult = await _instanceStore.TrySaveAsync(
                $"{INST_PREFIX}{body.InstanceId}",
                currentInstance,
                etag ?? string.Empty,
                cancellationToken: cancellationToken);

            if (saveResult is null)
            {
                _logger.LogWarning(
                    "Optimistic concurrency failure saving contract to instance {InstanceId}",
                    body.InstanceId);
                return (StatusCodes.Conflict, null);
            }

            // Invalidate cache
            await InvalidateInstanceCacheAsync(body.InstanceId.ToString(), cancellationToken);
        }
        else
        {
            // 7b. Subsequent step: Use existing contract
            // Note: isFirstStep is false means ContractInstanceId.HasValue is true
            contractInstanceId = currentInstance.ContractInstanceId
                ?? throw new InvalidOperationException("ContractInstanceId is null when isFirstStep is false");
        }

        // 8. Complete the specified milestone
        MilestoneResponse milestoneResponse;
        try
        {
            milestoneResponse = await _contractClient.CompleteMilestoneAsync(
                new CompleteMilestoneRequest
                {
                    ContractId = contractInstanceId,
                    MilestoneCode = body.MilestoneCode,
                    Evidence = body.Evidence
                },
                cancellationToken);
        }
        catch (ApiException ex)
        {
            _logger.LogWarning(ex,
                "Contract service error completing milestone {MilestoneCode} for item {InstanceId}, contract {ContractId}",
                body.MilestoneCode, body.InstanceId, contractInstanceId);
            return (StatusCodes.BadRequest, null);
        }

        if (milestoneResponse.Milestone.Status != MilestoneStatus.Completed)
        {
            _logger.LogWarning(
                "Milestone {MilestoneCode} failed for item {InstanceId}, contract {ContractId}",
                body.MilestoneCode, body.InstanceId, contractInstanceId);

            // Execute OnUseFailed handler if configured
            if (template.OnUseFailedBehaviorContractTemplateId.HasValue)
            {
                await ExecuteOnUseFailedHandlerAsync(
                    template.OnUseFailedBehaviorContractTemplateId.Value,
                    body.UserId,
                    body.UserType,
                    body.InstanceId,
                    template.TemplateId,
                    $"Milestone {body.MilestoneCode} failed",
                    body.Context,
                    cancellationToken);
            }

            // Publish step failed event
            await PublishStepFailedEventAsync(
                body.InstanceId,
                template.TemplateId,
                template.Code,
                body.UserId,
                body.UserType,
                contractInstanceId,
                body.MilestoneCode,
                $"Milestone completion failed",
                cancellationToken);

            return (StatusCodes.BadRequest, null);
        }

        // 9. Query remaining milestones from contract
        var remainingMilestones = await GetRemainingMilestonesAsync(contractInstanceId, cancellationToken);
        var isComplete = remainingMilestones.Count == 0;
        var consumed = false;

        // 10. If all required milestones complete, handle consumption and cleanup
        if (isComplete)
        {
            // Consume item per itemUseBehavior
            if (template.ItemUseBehavior != ItemUseBehavior.Disabled)
            {
                // Re-fetch the instance for consumption (it may have been modified)
                var freshInstance = await GetInstanceWithCacheAsync(body.InstanceId.ToString(), cancellationToken);
                if (freshInstance is not null)
                {
                    (consumed, _) = await ConsumeItemAsync(
                        body.InstanceId,
                        freshInstance,
                        template,
                        cancellationToken);
                }
            }

            // Clear session binding (only if session type)
            if (currentInstance.ContractBindingType == ContractBindingType.Session)
            {
                currentInstance.ContractInstanceId = null;
                currentInstance.ContractBindingType = ContractBindingType.None;
                currentInstance.ModifiedAt = DateTimeOffset.UtcNow;

                // Re-fetch etag for optimistic concurrency
                var (latestInstance, latestEtag) = await _instanceStore.GetWithETagAsync(
                    $"{INST_PREFIX}{body.InstanceId}",
                    cancellationToken);

                if (latestInstance is not null)
                {
                    latestInstance.ContractInstanceId = null;
                    latestInstance.ContractBindingType = ContractBindingType.None;
                    latestInstance.ModifiedAt = DateTimeOffset.UtcNow;

                    // latestEtag is non-null here (instance was found above); coalesce satisfies compiler nullable analysis
                    await _instanceStore.TrySaveAsync(
                        $"{INST_PREFIX}{body.InstanceId}",
                        latestInstance,
                        latestEtag ?? string.Empty,
                        cancellationToken: cancellationToken);

                    await InvalidateInstanceCacheAsync(body.InstanceId.ToString(), cancellationToken);
                }
            }
        }

        // 11. Publish step completed event
        await PublishStepCompletedEventAsync(
            body.InstanceId,
            template.TemplateId,
            template.Code,
            body.UserId,
            body.UserType,
            contractInstanceId,
            body.MilestoneCode,
            isComplete ? null : remainingMilestones,
            isComplete,
            consumed,
            cancellationToken);

        _logger.LogDebug(
            "UseItemStep completed: instance={InstanceId}, milestone={MilestoneCode}, isComplete={IsComplete}, consumed={Consumed}",
            body.InstanceId, body.MilestoneCode, isComplete, consumed);

        return (StatusCodes.OK, new UseItemStepResponse
        {
            InstanceId = body.InstanceId,
            ContractInstanceId = contractInstanceId,
            CompletedMilestone = body.MilestoneCode,
            RemainingMilestones = isComplete ? null : remainingMilestones,
            IsComplete = isComplete,
            Consumed = consumed
        });
    }

    #endregion

    #region Query Operations

    /// <inheritdoc/>
    public async Task<(StatusCodes, ListItemsResponse?)> ListItemsByContainerAsync(
        ListItemsByContainerRequest body,
        CancellationToken cancellationToken = default)
    {
        var idsJson = await _instanceStringStore.GetAsync($"{INST_CONTAINER_INDEX}{body.ContainerId}", cancellationToken);
        var ids = string.IsNullOrEmpty(idsJson)
            ? new List<string>()
            : BannouJson.Deserialize<List<string>>(idsJson) ?? new List<string>();

        var totalCount = ids.Count;
        var effectiveLimit = Math.Min(body.Limit, _configuration.MaxInstancesPerQuery);
        var idsToFetch = ids.Skip(body.Offset).Take(effectiveLimit).ToList();
        var wasTruncated = totalCount > body.Offset + idsToFetch.Count;

        // Load all instances in bulk (cache + persistent store)
        var modelsById = await GetInstancesBulkWithCacheAsync(idsToFetch, cancellationToken);

        // Map to responses, preserving order from the index
        var items = new List<ItemInstanceResponse>();
        foreach (var id in idsToFetch)
        {
            if (modelsById.TryGetValue(id, out var model))
            {
                items.Add(MapInstanceToResponse(model));
            }
        }

        return (StatusCodes.OK, new ListItemsResponse
        {
            Items = items,
            TotalCount = totalCount,
            WasTruncated = wasTruncated
        });
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, ListItemsResponse?)> ListItemsByTemplateAsync(
        ListItemsByTemplateRequest body,
        CancellationToken cancellationToken = default)
    {
        // Query MySQL directly with TemplateId + optional RealmId filter
        // per IMPLEMENTATION TENETS — pushes filtering to the database instead of
        // loading all instances and filtering in memory
        var templateId = body.TemplateId;
        IReadOnlyList<ItemInstanceModel> matchingInstances;

        if (body.RealmId.HasValue)
        {
            var realmId = body.RealmId.Value;
            matchingInstances = await _instanceQueryableStore.QueryAsync(
                m => m.TemplateId == templateId && m.RealmId == realmId,
                cancellationToken);
        }
        else
        {
            matchingInstances = await _instanceQueryableStore.QueryAsync(
                m => m.TemplateId == templateId,
                cancellationToken);
        }

        var effectiveLimit = Math.Min(body.Limit, _configuration.MaxInstancesPerQuery);
        var totalCount = matchingInstances.Count;
        var paged = matchingInstances
            .Skip(body.Offset)
            .Take(effectiveLimit)
            .Select(MapInstanceToResponse)
            .ToList();

        return (StatusCodes.OK, new ListItemsResponse
        {
            Items = paged,
            TotalCount = totalCount
        });
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, BatchGetItemInstancesResponse?)> BatchGetItemInstancesAsync(
        BatchGetItemInstancesRequest body,
        CancellationToken cancellationToken = default)
    {
        // Convert GUIDs to string IDs for bulk lookup
        var instanceIdStrings = body.InstanceIds.Select(id => id.ToString()).ToList();

        // Load all instances in bulk (cache + persistent store)
        var modelsById = await GetInstancesBulkWithCacheAsync(instanceIdStrings, cancellationToken);

        // Build results, tracking not found items
        var items = new List<ItemInstanceResponse>();
        var notFound = new List<Guid>();

        foreach (var instanceId in body.InstanceIds)
        {
            var idStr = instanceId.ToString();
            if (modelsById.TryGetValue(idStr, out var model))
            {
                items.Add(MapInstanceToResponse(model));
            }
            else
            {
                notFound.Add(instanceId);
            }
        }

        return (StatusCodes.OK, new BatchGetItemInstancesResponse
        {
            Items = items,
            NotFound = notFound
        });
    }

    #endregion

    /// <inheritdoc />
    public async Task<(StatusCodes, CleanDeprecatedResponse?)> CleanDeprecatedItemTemplatesAsync(
        CleanDeprecatedRequest body, CancellationToken cancellationToken = default)
    {
        // 1. Load all template IDs and fetch their models to find deprecated ones
        var allTemplateIdsJson = await _templateStringStore.GetAsync(ALL_TEMPLATES_KEY, cancellationToken);
        var allTemplateIds = string.IsNullOrEmpty(allTemplateIdsJson)
            ? new List<string>()
            : BannouJson.Deserialize<List<string>>(allTemplateIdsJson) ?? new List<string>();

        if (allTemplateIds.Count == 0)
        {
            return (StatusCodes.OK, new CleanDeprecatedResponse
            {
                Cleaned = 0,
                Remaining = 0,
                Errors = 0,
                CleanedIds = new List<Guid>()
            });
        }

        var keys = allTemplateIds
            .Select(id => BuildTemplateKey(Guid.Parse(id)))
            .ToList();
        var bulkResults = await _templateStore.GetBulkAsync(keys, cancellationToken);

        var deprecatedTemplates = bulkResults
            .Where(kvp => kvp.Value is { IsDeprecated: true })
            .Select(kvp => kvp.Value)
            .ToList();

        if (deprecatedTemplates.Count == 0)
        {
            return (StatusCodes.OK, new CleanDeprecatedResponse
            {
                Cleaned = 0,
                Remaining = 0,
                Errors = 0,
                CleanedIds = new List<Guid>()
            });
        }

        // 2. Delegate to shared helper (per IMPLEMENTATION TENETS — B20)
        var result = await DeprecationCleanupHelper.ExecuteCleanupSweepAsync(
            deprecatedTemplates,
            getEntityId: t => t.TemplateId,
            getDeprecatedAt: t => t.DeprecatedAt,
            hasInstancesAsync: (t, ct) =>
                _instanceStringStore.HasStringListEntriesAsync($"{INST_TEMPLATE_INDEX}{t.TemplateId}", ct),
            deleteAndPublishAsync: async (t, ct) =>
            {
                // Remove template from primary store
                await _templateStore.DeleteAsync(BuildTemplateKey(t.TemplateId), ct);

                // Remove code uniqueness index
                await _templateStringStore.DeleteAsync(
                    $"{TPL_CODE_INDEX}{t.GameId}:{t.Code}", ct);

                // Remove from game-scoped template list
                await RemoveFromListAsync(
                    _templateStringStore, $"{TPL_GAME_INDEX}{t.GameId}",
                    t.TemplateId.ToString(), ct);

                // Remove from global template list
                await RemoveFromListAsync(
                    _templateStringStore, ALL_TEMPLATES_KEY,
                    t.TemplateId.ToString(), ct);

                // Invalidate template cache
                await InvalidateTemplateCacheAsync(t.TemplateId.ToString(), ct);

                // Clean up instance template index (should be empty since hasActiveInstances was false)
                await _instanceStringStore.DeleteAsync(
                    $"{INST_TEMPLATE_INDEX}{t.TemplateId}", ct);

                // Publish item.template.deleted lifecycle event
                await _messageBus.PublishItemTemplateDeletedAsync(
                    new ItemTemplateDeletedEvent
                    {
                        EventId = Guid.NewGuid(),
                        Timestamp = DateTimeOffset.UtcNow,
                        TemplateId = t.TemplateId,
                        Code = t.Code,
                        GameId = t.GameId,
                        Name = t.Name,
                        Description = t.Description,
                        Category = t.Category,
                        Rarity = t.Rarity,
                        QuantityModel = t.QuantityModel,
                        MaxStackSize = t.MaxStackSize,
                        Scope = t.Scope,
                        SoulboundType = t.SoulboundType,
                        Tradeable = t.Tradeable,
                        Destroyable = t.Destroyable,
                        HasDurability = t.HasDurability,
                        MaxDurability = t.MaxDurability,
                        IsActive = t.IsActive,
                        MigrationTargetId = t.MigrationTargetId,
                        CreatedAt = t.CreatedAt,
                        UpdatedAt = t.UpdatedAt,
                        IsDeprecated = t.IsDeprecated,
                        DeprecatedAt = t.DeprecatedAt,
                        DeprecationReason = t.DeprecationReason,
                        DeletedReason = "Cleaned via deprecation sweep"
                    }, ct);
            },
            body.GracePeriodDays,
            body.DryRun,
            _logger,
            _telemetryProvider,
            cancellationToken);

        // 3. Map helper result to generated response
        return (StatusCodes.OK, new CleanDeprecatedResponse
        {
            Cleaned = result.Cleaned,
            Remaining = result.Remaining,
            Errors = result.Errors,
            CleanedIds = result.CleanedIds.ToList()
        });
    }
}
