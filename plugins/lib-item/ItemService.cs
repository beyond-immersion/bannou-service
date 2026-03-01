using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Contract;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Messaging;
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
public partial class ItemService : IItemService
{
    private readonly IMessageBus _messageBus;
    private readonly IStateStoreFactory _stateStoreFactory;
    private readonly IDistributedLockProvider _lockProvider;
    private readonly IContractClient _contractClient;
    private readonly ITelemetryProvider _telemetryProvider;
    private readonly ILogger<ItemService> _logger;
    private readonly ItemServiceConfiguration _configuration;

    // Parsed config defaults (boundary parsing per IMPLEMENTATION TENETS)
    private readonly ItemRarity _defaultRarity;
    private readonly WeightPrecision _defaultWeightPrecision;
    private readonly SoulboundType _defaultSoulboundType;

    /// <summary>
    /// Thread-safe dictionary for batching item use events by templateId+userId key.
    /// Key format: "{templateId}:{userId}". Values track uses within deduplication window.
    /// </summary>
    /// <remarks>
    /// Per IMPLEMENTATION TENETS (T9): This local cache is acceptable because it's purely for
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
    /// Per IMPLEMENTATION TENETS (T9): This local cache is acceptable because it's purely for
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
    /// Initializes a new instance of the ItemService.
    /// </summary>
    /// <param name="messageBus">Message bus for event publishing.</param>
    /// <param name="stateStoreFactory">Factory for accessing state stores.</param>
    /// <param name="lockProvider">Provider for distributed locks.</param>
    /// <param name="contractClient">Client for Contract service (L1) - hard dependency per SERVICE HIERARCHY.</param>
    /// <param name="telemetryProvider">Telemetry provider for distributed tracing spans.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="configuration">Service configuration.</param>
    public ItemService(
        IMessageBus messageBus,
        IStateStoreFactory stateStoreFactory,
        IDistributedLockProvider lockProvider,
        IContractClient contractClient,
        ITelemetryProvider telemetryProvider,
        ILogger<ItemService> logger,
        ItemServiceConfiguration configuration)
    {
        _messageBus = messageBus;
        _stateStoreFactory = stateStoreFactory;
        _lockProvider = lockProvider;
        _contractClient = contractClient;
        _telemetryProvider = telemetryProvider;
        _logger = logger;
        _configuration = configuration;

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

        var templateStore = _stateStoreFactory.GetStore<ItemTemplateModel>(StateStoreDefinitions.ItemTemplateStore);
        var stringStore = _stateStoreFactory.GetStore<string>(StateStoreDefinitions.ItemTemplateStore);

        // Check code uniqueness within game using atomic claim
        var codeKey = $"{TPL_CODE_INDEX}{body.GameId}:{body.Code}";
        var (existingId, codeEtag) = await stringStore.GetWithETagAsync(codeKey, cancellationToken);
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
        var claimResult = await stringStore.TrySaveAsync(codeKey, templateId.ToString(), codeEtag ?? string.Empty, cancellationToken);
        if (claimResult == null)
        {
            _logger.LogWarning("Item template code claimed concurrently: {Code} for game {GameId}", body.Code, body.GameId);
            return (StatusCodes.Conflict, null);
        }

        // Save template
        await templateStore.SaveAsync($"{TPL_PREFIX}{templateId}", model, cancellationToken: cancellationToken);
        await AddToListAsync(StateStoreDefinitions.ItemTemplateStore, $"{TPL_GAME_INDEX}{body.GameId}", templateId.ToString(), cancellationToken);
        await AddToListAsync(StateStoreDefinitions.ItemTemplateStore, ALL_TEMPLATES_KEY, templateId.ToString(), cancellationToken);

        // Populate cache
        await PopulateTemplateCacheAsync(templateId.ToString(), model, cancellationToken);

        // Publish lifecycle event
        await _messageBus.TryPublishAsync("item.template.created", new ItemTemplateCreatedEvent
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
        var templateStore = _stateStoreFactory.GetStore<ItemTemplateModel>(StateStoreDefinitions.ItemTemplateStore);
        var stringStore = _stateStoreFactory.GetStore<string>(StateStoreDefinitions.ItemTemplateStore);

        // Get templates for game
        string listKey = !string.IsNullOrEmpty(body.GameId)
            ? $"{TPL_GAME_INDEX}{body.GameId}"
            : ALL_TEMPLATES_KEY;

        var idsJson = await stringStore.GetAsync(listKey, cancellationToken);
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
        var templateStore = _stateStoreFactory.GetStore<ItemTemplateModel>(StateStoreDefinitions.ItemTemplateStore);
        var model = await templateStore.GetAsync($"{TPL_PREFIX}{body.TemplateId}", cancellationToken);

        if (model is null)
        {
            return (StatusCodes.NotFound, null);
        }

        var now = DateTimeOffset.UtcNow;

        // Update mutable fields
        if (!string.IsNullOrEmpty(body.Name)) model.Name = body.Name;
        if (body.Description is not null) model.Description = body.Description;
        if (body.Subcategory is not null) model.Subcategory = body.Subcategory;
        if (body.Tags is not null) model.Tags = body.Tags.ToList();
        if (body.Rarity.HasValue) model.Rarity = body.Rarity.Value;
        if (body.Weight.HasValue) model.Weight = body.Weight;
        if (body.Volume.HasValue) model.Volume = body.Volume;
        if (body.GridWidth.HasValue) model.GridWidth = body.GridWidth;
        if (body.GridHeight.HasValue) model.GridHeight = body.GridHeight;
        if (body.CanRotate.HasValue) model.CanRotate = body.CanRotate;
        if (body.BaseValue.HasValue) model.BaseValue = body.BaseValue;
        if (body.Tradeable.HasValue) model.Tradeable = body.Tradeable.Value;
        if (body.Destroyable.HasValue) model.Destroyable = body.Destroyable.Value;
        if (body.MaxDurability.HasValue) model.MaxDurability = body.MaxDurability;
        if (body.AvailableRealms is not null) model.AvailableRealms = body.AvailableRealms.ToList();
        if (body.Stats is not null) model.Stats = BannouJson.Serialize(body.Stats);
        if (body.Effects is not null) model.Effects = BannouJson.Serialize(body.Effects);
        if (body.Requirements is not null) model.Requirements = BannouJson.Serialize(body.Requirements);
        if (body.Display is not null) model.Display = BannouJson.Serialize(body.Display);
        if (body.Metadata is not null) model.Metadata = BannouJson.Serialize(body.Metadata);
        // Contract template IDs: null in request means "don't change", explicit value updates it
        // To clear, caller must pass the null GUID explicitly via a separate endpoint or admin action
        if (body.UseBehaviorContractTemplateId.HasValue) model.UseBehaviorContractTemplateId = body.UseBehaviorContractTemplateId;
        if (body.CanUseBehaviorContractTemplateId.HasValue) model.CanUseBehaviorContractTemplateId = body.CanUseBehaviorContractTemplateId;
        if (body.OnUseFailedBehaviorContractTemplateId.HasValue) model.OnUseFailedBehaviorContractTemplateId = body.OnUseFailedBehaviorContractTemplateId;
        if (body.ItemUseBehavior.HasValue) model.ItemUseBehavior = body.ItemUseBehavior.Value;
        if (body.CanUseBehavior.HasValue) model.CanUseBehavior = body.CanUseBehavior.Value;
        if (body.IsActive.HasValue) model.IsActive = body.IsActive.Value;
        model.UpdatedAt = now;

        await templateStore.SaveAsync($"{TPL_PREFIX}{body.TemplateId}", model, cancellationToken: cancellationToken);

        // Invalidate cache after write
        await InvalidateTemplateCacheAsync(body.TemplateId.ToString(), cancellationToken);

        await _messageBus.TryPublishAsync("item.template.updated", new ItemTemplateUpdatedEvent
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
            UpdatedAt = now
        }, cancellationToken);

        _logger.LogDebug("Updated item template {TemplateId}", body.TemplateId);
        return (StatusCodes.OK, MapTemplateToResponse(model));
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, ItemTemplateResponse?)> DeprecateItemTemplateAsync(
        DeprecateItemTemplateRequest body,
        CancellationToken cancellationToken = default)
    {
        var templateStore = _stateStoreFactory.GetStore<ItemTemplateModel>(StateStoreDefinitions.ItemTemplateStore);
        var model = await templateStore.GetAsync($"{TPL_PREFIX}{body.TemplateId}", cancellationToken);

        if (model is null)
        {
            return (StatusCodes.NotFound, null);
        }

        var now = DateTimeOffset.UtcNow;
        model.IsDeprecated = true;
        model.DeprecatedAt = now;
        model.DeprecationReason = body.Reason;
        model.MigrationTargetId = body.MigrationTargetId;
        model.UpdatedAt = now;

        await templateStore.SaveAsync($"{TPL_PREFIX}{body.TemplateId}", model, cancellationToken: cancellationToken);

        // Invalidate cache after write
        await InvalidateTemplateCacheAsync(body.TemplateId.ToString(), cancellationToken);

        // Per IMPLEMENTATION TENETS: deprecation published as *.updated with changedFields
        await _messageBus.TryPublishAsync("item.template.updated", new ItemTemplateUpdatedEvent
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

        var instanceStore = _stateStoreFactory.GetStore<ItemInstanceModel>(StateStoreDefinitions.ItemInstanceStore);
        var stringStore = _stateStoreFactory.GetStore<string>(StateStoreDefinitions.ItemInstanceStore);

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

        await instanceStore.SaveAsync($"{INST_PREFIX}{instanceId}", model, cancellationToken: cancellationToken);
        await AddToListAsync(StateStoreDefinitions.ItemInstanceStore, $"{INST_CONTAINER_INDEX}{body.ContainerId}", instanceId.ToString(), cancellationToken);
        await AddToListAsync(StateStoreDefinitions.ItemInstanceStore, $"{INST_TEMPLATE_INDEX}{body.TemplateId}", instanceId.ToString(), cancellationToken);

        // Populate instance cache
        await PopulateInstanceCacheAsync(instanceId.ToString(), model, cancellationToken);

        await _messageBus.TryPublishAsync("item.instance.created", new ItemInstanceCreatedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = now,
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
        }, cancellationToken);

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
        // Container changes require a lock to prevent race conditions on index updates
        if (body.NewContainerId.HasValue || body.ClearContainerId == true)
        {
            return await ModifyItemInstanceWithLockAsync(body, cancellationToken);
        }

        // Non-container changes don't need locking
        return await ModifyItemInstanceInternalAsync(body, cancellationToken);
    }

    /// <summary>
    /// Modifies an item instance with a distributed lock to prevent container index races.
    /// </summary>
    private async Task<(StatusCodes, ItemInstanceResponse?)> ModifyItemInstanceWithLockAsync(
        ModifyItemInstanceRequest body,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.item", "ItemService.ModifyItemInstanceWithLockAsync");
        var lockOwner = $"modify-item-{Guid.NewGuid():N}";
        await using var lockResponse = await _lockProvider.LockAsync(
            StateStoreDefinitions.ItemLock,
            body.InstanceId.ToString(),
            lockOwner,
            _configuration.LockTimeoutSeconds,
            cancellationToken);

        if (!lockResponse.Success)
        {
            _logger.LogWarning("Failed to acquire lock for item instance {InstanceId}", body.InstanceId);
            return (StatusCodes.Conflict, null);
        }

        return await ModifyItemInstanceInternalAsync(body, cancellationToken);
    }

    /// <summary>
    /// Internal implementation of item instance modification.
    /// </summary>
    private async Task<(StatusCodes, ItemInstanceResponse?)> ModifyItemInstanceInternalAsync(
        ModifyItemInstanceRequest body,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.item", "ItemService.ModifyItemInstanceInternalAsync");
        var instanceStore = _stateStoreFactory.GetStore<ItemInstanceModel>(StateStoreDefinitions.ItemInstanceStore);
        var model = await instanceStore.GetAsync($"{INST_PREFIX}{body.InstanceId}", cancellationToken);

        if (model is null)
        {
            return (StatusCodes.NotFound, null);
        }

        var now = DateTimeOffset.UtcNow;

        // Capture old container ID for index updates (must be captured before modification)
        var oldContainerId = model.ContainerId;
        var containerCleared = body.ClearContainerId == true && oldContainerId.HasValue;
        var containerChanged = body.NewContainerId.HasValue && body.NewContainerId.Value != oldContainerId;

        // Apply modifications
        if (body.DurabilityDelta.HasValue && model.CurrentDurability.HasValue)
        {
            model.CurrentDurability = Math.Max(0, model.CurrentDurability.Value + body.DurabilityDelta.Value);
        }
        if (body.QuantityDelta.HasValue)
        {
            model.Quantity = Math.Max(0, model.Quantity + body.QuantityDelta.Value);
        }
        if (body.CustomStats is not null)
        {
            model.CustomStats = BannouJson.Serialize(body.CustomStats);
        }
        if (body.CustomName is not null)
        {
            model.CustomName = body.CustomName;
        }
        if (body.InstanceMetadata is not null)
        {
            model.InstanceMetadata = BannouJson.Serialize(body.InstanceMetadata);
        }
        if (body.ClearContainerId == true)
        {
            model.ContainerId = null;
            model.SlotIndex = null;
            model.SlotX = null;
            model.SlotY = null;
        }
        else if (body.NewContainerId.HasValue)
        {
            model.ContainerId = body.NewContainerId.Value;
        }
        if (body.NewSlotIndex.HasValue)
        {
            model.SlotIndex = body.NewSlotIndex.Value;
        }
        if (body.NewSlotX.HasValue)
        {
            model.SlotX = body.NewSlotX.Value;
        }
        if (body.NewSlotY.HasValue)
        {
            model.SlotY = body.NewSlotY.Value;
        }
        model.ModifiedAt = now;

        // Save the model first, then update indexes
        await instanceStore.SaveAsync($"{INST_PREFIX}{body.InstanceId}", model, cancellationToken: cancellationToken);

        // Update container indexes if container changed or cleared (after successful save)
        if (containerCleared && oldContainerId.HasValue)
        {
            await RemoveFromListAsync(StateStoreDefinitions.ItemInstanceStore, $"{INST_CONTAINER_INDEX}{oldContainerId.Value}", body.InstanceId.ToString(), cancellationToken);
        }
        else if (containerChanged)
        {
            var newContainerId = body.NewContainerId
                ?? throw new InvalidOperationException("NewContainerId is null when containerChanged is true");
            if (oldContainerId.HasValue)
            {
                await RemoveFromListAsync(StateStoreDefinitions.ItemInstanceStore, $"{INST_CONTAINER_INDEX}{oldContainerId.Value}", body.InstanceId.ToString(), cancellationToken);
            }
            await AddToListAsync(StateStoreDefinitions.ItemInstanceStore, $"{INST_CONTAINER_INDEX}{newContainerId}", body.InstanceId.ToString(), cancellationToken);
        }

        // Invalidate cache after write
        await InvalidateInstanceCacheAsync(body.InstanceId.ToString(), cancellationToken);

        await _messageBus.TryPublishAsync("item.instance.modified", new ItemInstanceModifiedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = now,
            InstanceId = body.InstanceId,
            TemplateId = model.TemplateId,
            ContainerId = model.ContainerId,
            RealmId = model.RealmId,
            Quantity = model.Quantity,
            OriginType = model.OriginType,
            CreatedAt = model.CreatedAt,
            ModifiedAt = now
        }, cancellationToken);

        _logger.LogDebug("Modified item instance {InstanceId}", body.InstanceId);
        return (StatusCodes.OK, MapInstanceToResponse(model));
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, ItemInstanceResponse?)> BindItemInstanceAsync(
        BindItemInstanceRequest body,
        CancellationToken cancellationToken = default)
    {
        var instanceStore = _stateStoreFactory.GetStore<ItemInstanceModel>(StateStoreDefinitions.ItemInstanceStore);
        var model = await instanceStore.GetAsync($"{INST_PREFIX}{body.InstanceId}", cancellationToken);

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

        await instanceStore.SaveAsync($"{INST_PREFIX}{body.InstanceId}", model, cancellationToken: cancellationToken);

        // Invalidate cache after write
        await InvalidateInstanceCacheAsync(body.InstanceId.ToString(), cancellationToken);

        // Get template for event enrichment (uses cache)
        var template = await GetTemplateWithCacheAsync(model.TemplateId.ToString(), cancellationToken);

        if (template is null)
        {
            _logger.LogWarning("Template {TemplateId} not found when binding instance {InstanceId}, possible data inconsistency",
                model.TemplateId, body.InstanceId);
        }

        var templateCode = template?.Code ?? $"missing:{model.TemplateId}";

        await _messageBus.TryPublishAsync("item.instance.bound", new ItemInstanceBoundEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = now,
            InstanceId = body.InstanceId,
            TemplateId = model.TemplateId,
            TemplateCode = templateCode,
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
        var instanceStore = _stateStoreFactory.GetStore<ItemInstanceModel>(StateStoreDefinitions.ItemInstanceStore);
        var model = await instanceStore.GetAsync($"{INST_PREFIX}{body.InstanceId}", cancellationToken);

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

        await instanceStore.SaveAsync($"{INST_PREFIX}{body.InstanceId}", model, cancellationToken: cancellationToken);

        // Invalidate cache after write
        await InvalidateInstanceCacheAsync(body.InstanceId.ToString(), cancellationToken);

        // Get template for event enrichment (uses cache)
        var template = await GetTemplateWithCacheAsync(model.TemplateId.ToString(), cancellationToken);

        if (template is null)
        {
            _logger.LogWarning("Template {TemplateId} not found when unbinding instance {InstanceId}, possible data inconsistency",
                model.TemplateId, body.InstanceId);
        }

        var templateCode = template?.Code ?? $"missing:{model.TemplateId}";

        await _messageBus.TryPublishAsync("item.instance.unbound", new ItemInstanceUnboundEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = now,
            InstanceId = body.InstanceId,
            TemplateId = model.TemplateId,
            TemplateCode = templateCode,
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
        var instanceStore = _stateStoreFactory.GetStore<ItemInstanceModel>(StateStoreDefinitions.ItemInstanceStore);
        var model = await instanceStore.GetAsync($"{INST_PREFIX}{body.InstanceId}", cancellationToken);

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
            await RemoveFromListAsync(StateStoreDefinitions.ItemInstanceStore, $"{INST_CONTAINER_INDEX}{model.ContainerId.Value}", body.InstanceId.ToString(), cancellationToken);
        }
        await RemoveFromListAsync(StateStoreDefinitions.ItemInstanceStore, $"{INST_TEMPLATE_INDEX}{model.TemplateId}", body.InstanceId.ToString(), cancellationToken);

        // Delete instance
        await instanceStore.DeleteAsync($"{INST_PREFIX}{body.InstanceId}", cancellationToken);

        // Invalidate cache after delete
        await InvalidateInstanceCacheAsync(body.InstanceId.ToString(), cancellationToken);

        await _messageBus.TryPublishAsync("item.instance.destroyed", new ItemInstanceDestroyedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = now,
            InstanceId = body.InstanceId,
            TemplateId = model.TemplateId,
            ContainerId = model.ContainerId,
            RealmId = model.RealmId,
            Quantity = model.Quantity,
            OriginType = model.OriginType,
            CreatedAt = model.CreatedAt,
            ModifiedAt = now
        }, cancellationToken);

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
        var instanceStore = _stateStoreFactory.GetStore<ItemInstanceModel>(StateStoreDefinitions.ItemInstanceStore);
        var (currentInstance, etag) = await instanceStore.GetWithETagAsync(
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
            var saveResult = await instanceStore.TrySaveAsync(
                $"{INST_PREFIX}{body.InstanceId}",
                currentInstance,
                etag ?? string.Empty,
                cancellationToken);

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
                var (latestInstance, latestEtag) = await instanceStore.GetWithETagAsync(
                    $"{INST_PREFIX}{body.InstanceId}",
                    cancellationToken);

                if (latestInstance is not null)
                {
                    latestInstance.ContractInstanceId = null;
                    latestInstance.ContractBindingType = ContractBindingType.None;
                    latestInstance.ModifiedAt = DateTimeOffset.UtcNow;

                    // latestEtag is non-null here (instance was found above); coalesce satisfies compiler nullable analysis
                    await instanceStore.TrySaveAsync(
                        $"{INST_PREFIX}{body.InstanceId}",
                        latestInstance,
                        latestEtag ?? string.Empty,
                        cancellationToken);

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

    /// <summary>
    /// Gets the remaining uncompleted milestone codes from a contract instance.
    /// </summary>
    private async Task<List<string>> GetRemainingMilestonesAsync(
        Guid contractInstanceId,
        CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.item", "ItemService.GetRemainingMilestonesAsync");
        try
        {
            var response = await _contractClient.GetContractInstanceAsync(
                new GetContractInstanceRequest { ContractId = contractInstanceId },
                ct);

            // Filter milestones that are not yet completed
            return response.Milestones?
                .Where(m => m.Status != MilestoneStatus.Completed && m.Status != MilestoneStatus.Skipped)
                .Select(m => m.Code)
                .ToList() ?? new List<string>();
        }
        catch (ApiException ex)
        {
            _logger.LogWarning(ex, "Failed to get remaining milestones for contract {ContractId}", contractInstanceId);
            return new List<string>();
        }
    }

    /// <summary>
    /// Publishes an ItemUseStepCompletedEvent.
    /// </summary>
    private async Task PublishStepCompletedEventAsync(
        Guid instanceId,
        Guid templateId,
        string templateCode,
        Guid userId,
        EntityType userType,
        Guid contractInstanceId,
        string milestoneCode,
        List<string>? remainingMilestones,
        bool isComplete,
        bool consumed,
        CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.item", "ItemService.PublishStepCompletedEventAsync");
        await _messageBus.TryPublishAsync("item.use-step-completed", new ItemUseStepCompletedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            InstanceId = instanceId,
            TemplateId = templateId,
            TemplateCode = templateCode,
            UserId = userId,
            UserType = userType,
            ContractInstanceId = contractInstanceId,
            MilestoneCode = milestoneCode,
            RemainingMilestones = remainingMilestones,
            IsComplete = isComplete,
            Consumed = consumed
        }, ct);
    }

    /// <summary>
    /// Publishes an ItemUseStepFailedEvent.
    /// </summary>
    private async Task PublishStepFailedEventAsync(
        Guid instanceId,
        Guid templateId,
        string templateCode,
        Guid userId,
        EntityType userType,
        Guid contractInstanceId,
        string milestoneCode,
        string reason,
        CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.item", "ItemService.PublishStepFailedEventAsync");
        await _messageBus.TryPublishAsync("item.use-step-failed", new ItemUseStepFailedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            InstanceId = instanceId,
            TemplateId = templateId,
            TemplateCode = templateCode,
            UserId = userId,
            UserType = userType,
            ContractInstanceId = contractInstanceId,
            MilestoneCode = milestoneCode,
            Reason = reason
        }, ct);
    }

    /// <summary>
    /// Computes or retrieves the deterministic system party ID for item use contracts.
    /// Uses SHA-256 hash of game ID to generate a deterministic UUID v5.
    /// </summary>
    /// <param name="gameId">The game ID to derive the system party ID from.</param>
    /// <returns>The system party ID (from config if set, otherwise computed).</returns>
    private Guid GetOrComputeSystemPartyId(string gameId)
    {
        // If configured explicitly, use that
        if (_configuration.SystemPartyId.HasValue)
        {
            return _configuration.SystemPartyId.Value;
        }

        // Compute deterministic UUID from game ID using SHA-256
        // This ensures the same game always gets the same system party ID across instances
        var inputBytes = Encoding.UTF8.GetBytes($"item-system-party:{gameId}");
        var hashBytes = SHA256.HashData(inputBytes);

        // Take first 16 bytes of hash and convert to GUID
        // Set version (4 bits) and variant (2 bits) per UUID v5 spec
        var guidBytes = new byte[16];
        Array.Copy(hashBytes, guidBytes, 16);
        guidBytes[6] = (byte)((guidBytes[6] & 0x0F) | 0x50); // Version 5
        guidBytes[8] = (byte)((guidBytes[8] & 0x3F) | 0x80); // Variant 1

        return new Guid(guidBytes);
    }

    /// <summary>
    /// Executes the CanUse validation contract if configured.
    /// Returns (passed, failureReason).
    /// </summary>
    /// <remarks>
    /// Uses milestone code from configuration (ITEM_CAN_USE_MILESTONE_CODE, default "validate").
    /// Per IMPLEMENTATION TENETS: No hardcoded tunables.
    /// </remarks>
    private async Task<(bool Passed, string? FailureReason)> ExecuteCanUseValidationAsync(
        Guid canUseTemplateId,
        Guid userId,
        EntityType userType,
        Guid instanceId,
        Guid templateId,
        object? context,
        CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.item", "ItemService.ExecuteCanUseValidationAsync");
        try
        {
            // Get system party for the validation contract
            var template = await GetTemplateWithCacheAsync(templateId.ToString(), ct);
            if (template is null)
            {
                return (false, "Template not found during validation");
            }

            var systemPartyId = GetOrComputeSystemPartyId(template.GameId);

            // Create validation contract instance
            var contractInstanceId = await CreateItemUseContractInstanceAsync(
                canUseTemplateId,
                userId,
                userType,
                systemPartyId,
                instanceId,
                templateId,
                context,
                ct);

            if (!contractInstanceId.HasValue)
            {
                return (false, "Failed to create validation contract");
            }

            // Complete configured milestone (default: "validate")
            var milestoneCode = _configuration.CanUseMilestoneCode;
            var response = await _contractClient.CompleteMilestoneAsync(
                new CompleteMilestoneRequest
                {
                    ContractId = contractInstanceId.Value,
                    MilestoneCode = milestoneCode
                }, ct);

            if (response.Milestone.Status != MilestoneStatus.Completed)
            {
                return (false, $"Validation milestone failed: {milestoneCode}");
            }

            return (true, null);
        }
        catch (ApiException ex)
        {
            _logger.LogWarning(ex, "CanUse validation failed for {InstanceId}", instanceId);
            return (false, ex.Message);
        }
    }

    /// <summary>
    /// Executes the OnUseFailed handler contract if configured.
    /// </summary>
    /// <remarks>
    /// Uses milestone code from configuration (ITEM_ON_USE_FAILED_MILESTONE_CODE, default "handle_failure").
    /// Per IMPLEMENTATION TENETS: Log but don't propagate handler failures.
    /// </remarks>
    private async Task ExecuteOnUseFailedHandlerAsync(
        Guid onUseFailedTemplateId,
        Guid userId,
        EntityType userType,
        Guid instanceId,
        Guid templateId,
        string failureReason,
        object? context,
        CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.item", "ItemService.ExecuteOnUseFailedHandlerAsync");
        try
        {
            // Get system party for the handler contract
            var template = await GetTemplateWithCacheAsync(templateId.ToString(), ct);
            if (template is null)
            {
                _logger.LogWarning(
                    "Template {TemplateId} not found for OnUseFailed handler",
                    templateId);
                return;
            }

            var systemPartyId = GetOrComputeSystemPartyId(template.GameId);

            // Merge failure reason into context
            var enrichedContext = new Dictionary<string, object?>
            {
                ["failureReason"] = failureReason
            };
            if (context is IDictionary<string, object?> contextDict)
            {
                foreach (var kvp in contextDict)
                {
                    enrichedContext[kvp.Key] = kvp.Value;
                }
            }

            // Create failure handler contract instance
            var contractInstanceId = await CreateItemUseContractInstanceAsync(
                onUseFailedTemplateId,
                userId,
                userType,
                systemPartyId,
                instanceId,
                templateId,
                enrichedContext,
                ct);

            if (!contractInstanceId.HasValue)
            {
                _logger.LogWarning(
                    "Failed to create OnUseFailed contract for {InstanceId}",
                    instanceId);
                return;
            }

            // Complete configured milestone (default: "handle_failure")
            var milestoneCode = _configuration.OnUseFailedMilestoneCode;
            await _contractClient.CompleteMilestoneAsync(
                new CompleteMilestoneRequest
                {
                    ContractId = contractInstanceId.Value,
                    MilestoneCode = milestoneCode
                }, ct);
        }
        catch (Exception ex)
        {
            // Log but don't propagate - handler failures shouldn't break the main flow
            _logger.LogError(ex, "OnUseFailed handler error for {InstanceId}", instanceId);
        }
    }

    /// <summary>
    /// Creates a transient contract instance for item use with user and system parties.
    /// </summary>
    /// <returns>Contract instance ID if successful, null otherwise.</returns>
    private async Task<Guid?> CreateItemUseContractInstanceAsync(
        Guid templateId,
        Guid userId,
        EntityType userType,
        Guid systemPartyId,
        Guid instanceId,
        Guid itemTemplateId,
        object? context,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.item", "ItemService.CreateItemUseContractInstanceAsync");
        try
        {
            // Build game metadata with item context for prebound API substitution
            var gameMetadata = new Dictionary<string, object>
            {
                ["itemInstanceId"] = instanceId.ToString(),
                ["itemTemplateId"] = itemTemplateId.ToString(),
                ["userId"] = userId.ToString(),
                ["userType"] = userType
            };

            // Merge any additional context provided by caller
            if (context is IDictionary<string, object> contextDict)
            {
                foreach (var kvp in contextDict)
                {
                    gameMetadata[kvp.Key] = kvp.Value;
                }
            }

            var request = new CreateContractInstanceRequest
            {
                TemplateId = templateId,
                Parties = new List<ContractPartyInput>
                {
                    new()
                    {
                        EntityId = userId,
                        EntityType = userType,
                        Role = "user"
                    },
                    new()
                    {
                        EntityId = systemPartyId,
                        EntityType = _configuration.SystemPartyType,
                        Role = "system"
                    }
                },
                GameMetadata = gameMetadata
            };

            var response = await _contractClient.CreateContractInstanceAsync(request, cancellationToken);
            return response.ContractId;
        }
        catch (ApiException ex)
        {
            _logger.LogWarning(ex,
                "API error creating contract instance for template {TemplateId}",
                templateId);
            return null;
        }
    }

    /// <summary>
    /// Completes the "use" milestone on the contract instance, triggering prebound APIs.
    /// </summary>
    /// <returns>True if milestone completed successfully, false otherwise.</returns>
    private async Task<bool> CompleteUseMilestoneAsync(
        Guid contractInstanceId,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.item", "ItemService.CompleteUseMilestoneAsync");
        try
        {
            var request = new CompleteMilestoneRequest
            {
                ContractId = contractInstanceId,
                MilestoneCode = _configuration.UseMilestoneCode
            };

            var response = await _contractClient.CompleteMilestoneAsync(request, cancellationToken);

            // Check if milestone was actually completed by checking its status
            return response.Milestone.Status == MilestoneStatus.Completed;
        }
        catch (ApiException ex)
        {
            _logger.LogWarning(ex,
                "API error completing milestone for contract {ContractId}",
                contractInstanceId);
            return false;
        }
    }

    /// <summary>
    /// Consumes the item after successful use (decrements quantity or destroys).
    /// </summary>
    /// <returns>Tuple of (wasConsumed, remainingQuantity or null if destroyed).</returns>
    private async Task<(bool Consumed, double? RemainingQuantity)> ConsumeItemAsync(
        Guid instanceId,
        ItemInstanceModel instance,
        ItemTemplateModel template,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.item", "ItemService.ConsumeItemAsync");
        // For now, always consume on use (MVP behavior)
        // Future: per-template configuration for consumable vs reusable items
        var instanceStore = _stateStoreFactory.GetStore<ItemInstanceModel>(StateStoreDefinitions.ItemInstanceStore);

        if (instance.Quantity <= 1)
        {
            // Last item - destroy the instance
            if (instance.ContainerId.HasValue)
            {
                await RemoveFromListAsync(
                    StateStoreDefinitions.ItemInstanceStore,
                    $"{INST_CONTAINER_INDEX}{instance.ContainerId.Value}",
                    instanceId.ToString(),
                    cancellationToken);
            }
            await RemoveFromListAsync(
                StateStoreDefinitions.ItemInstanceStore,
                $"{INST_TEMPLATE_INDEX}{instance.TemplateId}",
                instanceId.ToString(),
                cancellationToken);
            await instanceStore.DeleteAsync($"{INST_PREFIX}{instanceId}", cancellationToken);
            await InvalidateInstanceCacheAsync(instanceId.ToString(), cancellationToken);

            // Publish destroy event
            await _messageBus.TryPublishAsync("item.instance.destroyed", new ItemInstanceDestroyedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                InstanceId = instanceId,
                TemplateId = instance.TemplateId,
                ContainerId = instance.ContainerId,
                RealmId = instance.RealmId,
                Quantity = instance.Quantity,
                OriginType = instance.OriginType,
                CreatedAt = instance.CreatedAt,
                ModifiedAt = DateTimeOffset.UtcNow
            }, cancellationToken);

            return (true, null);
        }
        else
        {
            // Decrement quantity
            instance.Quantity -= 1;
            instance.ModifiedAt = DateTimeOffset.UtcNow;
            await instanceStore.SaveAsync($"{INST_PREFIX}{instanceId}", instance, cancellationToken: cancellationToken);
            await InvalidateInstanceCacheAsync(instanceId.ToString(), cancellationToken);

            // Publish modify event
            await _messageBus.TryPublishAsync("item.instance.modified", new ItemInstanceModifiedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                InstanceId = instanceId,
                TemplateId = instance.TemplateId,
                ContainerId = instance.ContainerId,
                RealmId = instance.RealmId,
                Quantity = instance.Quantity,
                OriginType = instance.OriginType,
                CreatedAt = instance.CreatedAt,
                ModifiedAt = instance.ModifiedAt
            }, cancellationToken);

            return (true, instance.Quantity);
        }
    }

    /// <summary>
    /// Records a successful item use for batched event publishing.
    /// Events are batched by templateId+userId within the deduplication window.
    /// </summary>
    private async Task RecordUseSuccessAsync(
        Guid instanceId,
        Guid templateId,
        string templateCode,
        Guid userId,
        EntityType userType,
        Guid? targetId,
        EntityType? targetType,
        bool consumed,
        Guid contractInstanceId,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.item", "ItemService.RecordUseSuccessAsync");
        var batchKey = $"{templateId}:{userId}";
        var now = DateTimeOffset.UtcNow;
        var windowSeconds = _configuration.UseEventDeduplicationWindowSeconds;
        var maxBatchSize = _configuration.UseEventBatchMaxSize;

        var record = new ItemUseRecord
        {
            InstanceId = instanceId,
            TemplateId = templateId,
            TemplateCode = templateCode,
            UserId = userId,
            UserType = userType,
            TargetId = targetId,
            TargetType = targetType,
            UsedAt = now,
            Consumed = consumed,
            ContractInstanceId = contractInstanceId
        };

        // Pre-flush: if an expired batch exists for this key, publish it before adding new records.
        // Without this, AddOrUpdate would silently discard the old batch's records when replacing
        // it with a fresh one on window expiry (IMPLEMENTATION TENETS).
        if (_useBatches.TryGetValue(batchKey, out var existingBatch)
            && (now - existingBatch.WindowStart).TotalSeconds >= windowSeconds)
        {
            if (_useBatches.TryRemove(batchKey, out var expiredBatch))
            {
                await PublishItemUsedEventAsync(expiredBatch, cancellationToken);
            }
        }

        // Get or create batch (pre-flush already removed expired batches)
        var batch = _useBatches.GetOrAdd(batchKey, _ => new ItemUseBatchState());
        var totalCount = batch.AddRecord(record);

        // Publish if batch is full
        if (totalCount >= maxBatchSize)
        {
            // Try to remove and publish (only one thread will succeed)
            if (_useBatches.TryRemove(batchKey, out var publishBatch))
            {
                await PublishItemUsedEventAsync(publishBatch, cancellationToken);
            }
        }
    }

    /// <summary>
    /// Records a failed item use for batched event publishing.
    /// Events are batched by templateId+userId within the deduplication window.
    /// </summary>
    private async Task RecordUseFailureAsync(
        Guid instanceId,
        Guid templateId,
        string templateCode,
        Guid userId,
        EntityType userType,
        string reason,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.item", "ItemService.RecordUseFailureAsync");
        var batchKey = $"{templateId}:{userId}";
        var now = DateTimeOffset.UtcNow;
        var windowSeconds = _configuration.UseEventDeduplicationWindowSeconds;
        var maxBatchSize = _configuration.UseEventBatchMaxSize;

        var record = new ItemUseFailureRecord
        {
            InstanceId = instanceId,
            TemplateId = templateId,
            TemplateCode = templateCode,
            UserId = userId,
            UserType = userType,
            FailedAt = now,
            Reason = reason
        };

        // Pre-flush: if an expired batch exists for this key, publish it before adding new records.
        // Without this, AddOrUpdate would silently discard the old batch's records when replacing
        // it with a fresh one on window expiry (IMPLEMENTATION TENETS).
        if (_failureBatches.TryGetValue(batchKey, out var existingBatch)
            && (now - existingBatch.WindowStart).TotalSeconds >= windowSeconds)
        {
            if (_failureBatches.TryRemove(batchKey, out var expiredBatch))
            {
                await PublishItemUseFailedEventAsync(expiredBatch, cancellationToken);
            }
        }

        // Get or create batch (pre-flush already removed expired batches)
        var batch = _failureBatches.GetOrAdd(batchKey, _ => new ItemUseFailureBatchState());
        var totalCount = batch.AddRecord(record);

        // Publish if batch is full
        if (totalCount >= maxBatchSize)
        {
            // Try to remove and publish (only one thread will succeed)
            if (_failureBatches.TryRemove(batchKey, out var publishBatch))
            {
                await PublishItemUseFailedEventAsync(publishBatch, cancellationToken);
            }
        }
    }

    /// <summary>
    /// Publishes a batched ItemUsedEvent.
    /// </summary>
    private async Task PublishItemUsedEventAsync(
        ItemUseBatchState batch,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.item", "ItemService.PublishItemUsedEventAsync");
        var (records, totalCount) = batch.GetSnapshot();
        if (records.Count == 0) return;

        var evt = new ItemUsedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            BatchId = batch.BatchId,
            Uses = records,
            TotalCount = totalCount
        };

        await _messageBus.TryPublishAsync("item.used", evt, cancellationToken);
        _logger.LogDebug(
            "Published batched item.used event: batchId={BatchId}, records={Count}, total={Total}",
            batch.BatchId, records.Count, totalCount);
    }

    /// <summary>
    /// Publishes a batched ItemUseFailedEvent.
    /// </summary>
    private async Task PublishItemUseFailedEventAsync(
        ItemUseFailureBatchState batch,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.item", "ItemService.PublishItemUseFailedEventAsync");
        var (records, totalCount) = batch.GetSnapshot();
        if (records.Count == 0) return;

        var evt = new ItemUseFailedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            BatchId = batch.BatchId,
            Failures = records,
            TotalCount = totalCount
        };

        await _messageBus.TryPublishAsync("item.use-failed", evt, cancellationToken);
        _logger.LogDebug(
            "Published batched item.use-failed event: batchId={BatchId}, records={Count}, total={Total}",
            batch.BatchId, records.Count, totalCount);
    }

    #endregion

    #region Query Operations

    /// <inheritdoc/>
    public async Task<(StatusCodes, ListItemsResponse?)> ListItemsByContainerAsync(
        ListItemsByContainerRequest body,
        CancellationToken cancellationToken = default)
    {
        var stringStore = _stateStoreFactory.GetStore<string>(StateStoreDefinitions.ItemInstanceStore);

        var idsJson = await stringStore.GetAsync($"{INST_CONTAINER_INDEX}{body.ContainerId}", cancellationToken);
        var ids = string.IsNullOrEmpty(idsJson)
            ? new List<string>()
            : BannouJson.Deserialize<List<string>>(idsJson) ?? new List<string>();

        var actualCount = ids.Count;
        var effectiveLimit = _configuration.MaxInstancesPerQuery;
        var wasTruncated = actualCount > effectiveLimit;
        var idsToFetch = wasTruncated ? ids.Take(effectiveLimit).ToList() : ids;

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
            TotalCount = actualCount,
            WasTruncated = wasTruncated
        });
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, ListItemsResponse?)> ListItemsByTemplateAsync(
        ListItemsByTemplateRequest body,
        CancellationToken cancellationToken = default)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.item", "ItemService.ListItemsByTemplateAsync");

        // Query MySQL directly with TemplateId + optional RealmId filter
        // per IMPLEMENTATION TENETS — pushes filtering to the database instead of
        // loading all instances and filtering in memory
        var queryableStore = _stateStoreFactory.GetQueryableStore<ItemInstanceModel>(StateStoreDefinitions.ItemInstanceStore);

        var templateId = body.TemplateId;
        IReadOnlyList<ItemInstanceModel> matchingInstances;

        if (body.RealmId.HasValue)
        {
            var realmId = body.RealmId.Value;
            matchingInstances = await queryableStore.QueryAsync(
                m => m.TemplateId == templateId && m.RealmId == realmId,
                cancellationToken);
        }
        else
        {
            matchingInstances = await queryableStore.QueryAsync(
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

    #region Helper Methods

    /// <summary>
    /// Resolves an item template by ID (preferred) or by code+gameId composite key.
    /// </summary>
    /// <param name="templateId">Template ID to look up directly.</param>
    /// <param name="code">Template code for composite key lookup (requires gameId).</param>
    /// <param name="gameId">Game ID for composite key lookup (requires code).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The resolved template, or null if not found.</returns>
    private async Task<ItemTemplateModel?> ResolveTemplateAsync(string? templateId, string? code, string? gameId, CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.item", "ItemService.ResolveTemplateAsync");
        if (!string.IsNullOrEmpty(templateId))
        {
            return await GetTemplateWithCacheAsync(templateId, ct);
        }

        if (!string.IsNullOrEmpty(code) && !string.IsNullOrEmpty(gameId))
        {
            var stringStore = _stateStoreFactory.GetStore<string>(StateStoreDefinitions.ItemTemplateStore);
            var id = await stringStore.GetAsync($"{TPL_CODE_INDEX}{gameId}:{code}", ct);
            if (!string.IsNullOrEmpty(id))
            {
                return await GetTemplateWithCacheAsync(id, ct);
            }
        }

        return null;
    }

    /// <summary>
    /// Add a value to a JSON-serialized list in a state store with optimistic concurrency.
    /// Uses GetWithETagAsync/TrySaveAsync to prevent distributed race conditions (IMPLEMENTATION TENETS).
    /// </summary>
    private async Task AddToListAsync(string storeName, string key, string value, CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.item", "ItemService.AddToListAsync");
        var stringStore = _stateStoreFactory.GetStore<string>(storeName);
        var maxRetries = _configuration.ListOperationMaxRetries;

        for (var attempt = 0; attempt < maxRetries; attempt++)
        {
            var (json, etag) = await stringStore.GetWithETagAsync(key, ct);
            var list = string.IsNullOrEmpty(json)
                ? new List<string>()
                : BannouJson.Deserialize<List<string>>(json) ?? new List<string>();

            if (list.Contains(value)) return;

            list.Add(value);
            var serialized = BannouJson.Serialize(list);

            // Use TrySaveAsync for both paths. Empty etag = "create only if not exists"
            // semantics across all backends (Redis transaction, InMemory TryAdd).
            // This prevents two concurrent first-writes from losing one record (IMPLEMENTATION TENETS).
            var newEtag = await stringStore.TrySaveAsync(key, serialized, etag ?? string.Empty, ct);
            if (newEtag is not null) return;

            _logger.LogDebug("Optimistic concurrency conflict on list {Key}, retry {Attempt}", key, attempt + 1);
        }

        _logger.LogWarning("Failed to add to list {Key} after {MaxRetries} retries", key, maxRetries);
    }

    /// <summary>
    /// Remove a value from a JSON-serialized list in a state store with optimistic concurrency.
    /// Uses GetWithETagAsync/TrySaveAsync to prevent distributed race conditions (IMPLEMENTATION TENETS).
    /// </summary>
    private async Task RemoveFromListAsync(string storeName, string key, string value, CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.item", "ItemService.RemoveFromListAsync");
        var stringStore = _stateStoreFactory.GetStore<string>(storeName);
        var maxRetries = _configuration.ListOperationMaxRetries;

        for (var attempt = 0; attempt < maxRetries; attempt++)
        {
            var (json, etag) = await stringStore.GetWithETagAsync(key, ct);
            if (string.IsNullOrEmpty(json)) return;

            var list = BannouJson.Deserialize<List<string>>(json) ?? new List<string>();
            if (!list.Remove(value)) return;

            // If list is now empty, delete the key to prevent empty index accumulation.
            // All readers handle missing keys identically to empty lists via
            // string.IsNullOrEmpty check, and AddToListAsync recreates from scratch.
            if (list.Count == 0)
            {
                await stringStore.DeleteAsync(key, ct);
                return;
            }

            var serialized = BannouJson.Serialize(list);

            if (etag is null)
            {
                // Backend returned data without ETag. Unconditional save is acceptable for
                // removes — concurrent removes are idempotent (shrinking the list) and cannot
                // lose data, unlike concurrent adds (IMPLEMENTATION TENETS).
                await stringStore.SaveAsync(key, serialized, cancellationToken: ct);
                return;
            }

            var newEtag = await stringStore.TrySaveAsync(key, serialized, etag, ct);
            if (newEtag is not null) return;

            _logger.LogDebug("Optimistic concurrency conflict on list {Key}, retry {Attempt}", key, attempt + 1);
        }

        _logger.LogWarning("Failed to remove from list {Key} after {MaxRetries} retries", key, maxRetries);
    }

    #endregion

    #region Cache Methods

    /// <summary>
    /// Get template with Redis cache read-through. Falls back to MySQL persistent store on cache miss.
    /// </summary>
    private async Task<ItemTemplateModel?> GetTemplateWithCacheAsync(string templateId, CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.item", "ItemService.GetTemplateWithCacheAsync");
        var cacheStore = _stateStoreFactory.GetStore<ItemTemplateModel>(StateStoreDefinitions.ItemTemplateCache);
        var cacheKey = $"{TPL_PREFIX}{templateId}";

        // Try cache first
        var cached = await cacheStore.GetAsync(cacheKey, ct);
        if (cached is not null) return cached;

        // Fallback to persistent store
        var store = _stateStoreFactory.GetStore<ItemTemplateModel>(StateStoreDefinitions.ItemTemplateStore);
        var model = await store.GetAsync(cacheKey, ct);
        if (model is null) return null;

        // Populate cache
        await cacheStore.SaveAsync(cacheKey, model,
            new StateOptions { Ttl = _configuration.TemplateCacheTtlSeconds }, ct);
        return model;
    }

    /// <summary>
    /// Populate template cache after a write operation.
    /// </summary>
    private async Task PopulateTemplateCacheAsync(string templateId, ItemTemplateModel model, CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.item", "ItemService.PopulateTemplateCacheAsync");
        var cacheStore = _stateStoreFactory.GetStore<ItemTemplateModel>(StateStoreDefinitions.ItemTemplateCache);
        await cacheStore.SaveAsync($"{TPL_PREFIX}{templateId}", model,
            new StateOptions { Ttl = _configuration.TemplateCacheTtlSeconds }, ct);
    }

    /// <summary>
    /// Invalidate template cache after a write/update operation.
    /// </summary>
    private async Task InvalidateTemplateCacheAsync(string templateId, CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.item", "ItemService.InvalidateTemplateCacheAsync");
        var cacheStore = _stateStoreFactory.GetStore<ItemTemplateModel>(StateStoreDefinitions.ItemTemplateCache);
        await cacheStore.DeleteAsync($"{TPL_PREFIX}{templateId}", ct);
    }

    /// <summary>
    /// Get instance with Redis cache read-through. Falls back to MySQL persistent store on cache miss.
    /// </summary>
    private async Task<ItemInstanceModel?> GetInstanceWithCacheAsync(string instanceId, CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.item", "ItemService.GetInstanceWithCacheAsync");
        var cacheStore = _stateStoreFactory.GetStore<ItemInstanceModel>(StateStoreDefinitions.ItemInstanceCache);
        var cacheKey = $"{INST_PREFIX}{instanceId}";

        // Try cache first
        var cached = await cacheStore.GetAsync(cacheKey, ct);
        if (cached is not null) return cached;

        // Fallback to persistent store
        var store = _stateStoreFactory.GetStore<ItemInstanceModel>(StateStoreDefinitions.ItemInstanceStore);
        var model = await store.GetAsync(cacheKey, ct);
        if (model is null) return null;

        // Populate cache
        await cacheStore.SaveAsync(cacheKey, model,
            new StateOptions { Ttl = _configuration.InstanceCacheTtlSeconds }, ct);
        return model;
    }

    /// <summary>
    /// Bulk get instances with Redis cache read-through. Falls back to MySQL for cache misses.
    /// Returns a dictionary keyed by instance ID (without prefix).
    /// </summary>
    private async Task<Dictionary<string, ItemInstanceModel>> GetInstancesBulkWithCacheAsync(
        IEnumerable<string> instanceIds,
        CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.item", "ItemService.GetInstancesBulkWithCacheAsync");
        var idList = instanceIds.ToList();
        if (idList.Count == 0) return new Dictionary<string, ItemInstanceModel>();

        var cacheStore = _stateStoreFactory.GetStore<ItemInstanceModel>(StateStoreDefinitions.ItemInstanceCache);
        var persistentStore = _stateStoreFactory.GetStore<ItemInstanceModel>(StateStoreDefinitions.ItemInstanceStore);

        // Build cache keys
        var cacheKeys = idList.Select(id => $"{INST_PREFIX}{id}").ToList();

        // Try cache first (single bulk call)
        var cachedItems = await cacheStore.GetBulkAsync(cacheKeys, ct);

        // Build result from cache hits
        var result = new Dictionary<string, ItemInstanceModel>();
        var cacheMissKeys = new List<string>();

        foreach (var key in cacheKeys)
        {
            if (cachedItems.TryGetValue(key, out var model))
            {
                // Extract instance ID from key (format: "inst:{instanceId}")
                var instanceId = key.Substring(INST_PREFIX.Length);
                result[instanceId] = model;
            }
            else
            {
                cacheMissKeys.Add(key);
            }
        }

        // If all items were in cache, we're done
        if (cacheMissKeys.Count == 0) return result;

        // Fetch cache misses from persistent store (single bulk call)
        var persistentItems = await persistentStore.GetBulkAsync(cacheMissKeys, ct);

        // Add persistent store results and populate cache
        if (persistentItems.Count > 0)
        {
            var cachePopulation = new List<KeyValuePair<string, ItemInstanceModel>>();

            foreach (var (key, model) in persistentItems)
            {
                var instanceId = key.Substring(INST_PREFIX.Length);
                result[instanceId] = model;
                cachePopulation.Add(new KeyValuePair<string, ItemInstanceModel>(key, model));
            }

            // Bulk populate cache for all fetched items
            await cacheStore.SaveBulkAsync(cachePopulation,
                new StateOptions { Ttl = _configuration.InstanceCacheTtlSeconds }, ct);
        }

        return result;
    }

    /// <summary>
    /// Populate instance cache after a write operation.
    /// </summary>
    private async Task PopulateInstanceCacheAsync(string instanceId, ItemInstanceModel model, CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.item", "ItemService.PopulateInstanceCacheAsync");
        var cacheStore = _stateStoreFactory.GetStore<ItemInstanceModel>(StateStoreDefinitions.ItemInstanceCache);
        await cacheStore.SaveAsync($"{INST_PREFIX}{instanceId}", model,
            new StateOptions { Ttl = _configuration.InstanceCacheTtlSeconds }, ct);
    }

    /// <summary>
    /// Invalidate instance cache after a write/delete operation.
    /// </summary>
    private async Task InvalidateInstanceCacheAsync(string instanceId, CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.item", "ItemService.InvalidateInstanceCacheAsync");
        var cacheStore = _stateStoreFactory.GetStore<ItemInstanceModel>(StateStoreDefinitions.ItemInstanceCache);
        await cacheStore.DeleteAsync($"{INST_PREFIX}{instanceId}", ct);
    }

    #endregion

    #region Mapping Methods

    /// <summary>
    /// Maps an internal template storage model to its API response representation.
    /// </summary>
    /// <param name="model">The internal template model.</param>
    /// <returns>The API response model.</returns>
    private static ItemTemplateResponse MapTemplateToResponse(ItemTemplateModel model)
    {
        return new ItemTemplateResponse
        {
            TemplateId = model.TemplateId,
            Code = model.Code,
            GameId = model.GameId,
            Name = model.Name,
            Description = model.Description,
            Category = model.Category,
            Subcategory = model.Subcategory,
            Tags = model.Tags,
            Rarity = model.Rarity,
            QuantityModel = model.QuantityModel,
            MaxStackSize = model.MaxStackSize,
            UnitOfMeasure = model.UnitOfMeasure,
            WeightPrecision = model.WeightPrecision,
            Weight = model.Weight,
            Volume = model.Volume,
            GridWidth = model.GridWidth,
            GridHeight = model.GridHeight,
            CanRotate = model.CanRotate,
            BaseValue = model.BaseValue,
            Tradeable = model.Tradeable,
            Destroyable = model.Destroyable,
            SoulboundType = model.SoulboundType,
            HasDurability = model.HasDurability,
            MaxDurability = model.MaxDurability,
            Scope = model.Scope,
            AvailableRealms = model.AvailableRealms,
            Stats = model.Stats is not null ? BannouJson.Deserialize<object>(model.Stats) : null,
            Effects = model.Effects is not null ? BannouJson.Deserialize<object>(model.Effects) : null,
            Requirements = model.Requirements is not null ? BannouJson.Deserialize<object>(model.Requirements) : null,
            Display = model.Display is not null ? BannouJson.Deserialize<object>(model.Display) : null,
            Metadata = model.Metadata is not null ? BannouJson.Deserialize<object>(model.Metadata) : null,
            UseBehaviorContractTemplateId = model.UseBehaviorContractTemplateId,
            CanUseBehaviorContractTemplateId = model.CanUseBehaviorContractTemplateId,
            OnUseFailedBehaviorContractTemplateId = model.OnUseFailedBehaviorContractTemplateId,
            ItemUseBehavior = model.ItemUseBehavior,
            CanUseBehavior = model.CanUseBehavior,
            IsActive = model.IsActive,
            IsDeprecated = model.IsDeprecated,
            DeprecatedAt = model.DeprecatedAt,
            DeprecationReason = model.DeprecationReason,
            MigrationTargetId = model.MigrationTargetId,
            CreatedAt = model.CreatedAt,
            UpdatedAt = model.UpdatedAt
        };
    }

    /// <summary>
    /// Maps an internal instance storage model to its API response representation.
    /// </summary>
    /// <param name="model">The internal instance model.</param>
    /// <returns>The API response model.</returns>
    private static ItemInstanceResponse MapInstanceToResponse(ItemInstanceModel model)
    {
        return new ItemInstanceResponse
        {
            InstanceId = model.InstanceId,
            TemplateId = model.TemplateId,
            ContainerId = model.ContainerId,
            RealmId = model.RealmId,
            Quantity = model.Quantity,
            SlotIndex = model.SlotIndex,
            SlotX = model.SlotX,
            SlotY = model.SlotY,
            Rotated = model.Rotated,
            CurrentDurability = model.CurrentDurability,
            BoundToId = model.BoundToId,
            BoundAt = model.BoundAt,
            CustomStats = model.CustomStats is not null ? BannouJson.Deserialize<object>(model.CustomStats) : null,
            CustomName = model.CustomName,
            InstanceMetadata = model.InstanceMetadata is not null ? BannouJson.Deserialize<object>(model.InstanceMetadata) : null,
            OriginType = model.OriginType,
            OriginId = model.OriginId,
            ContractInstanceId = model.ContractInstanceId,
            ContractBindingType = model.ContractBindingType,
            CreatedAt = model.CreatedAt,
            ModifiedAt = model.ModifiedAt
        };
    }

    #endregion
}
