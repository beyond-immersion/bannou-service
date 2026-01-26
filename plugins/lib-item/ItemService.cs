using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Item;

/// <summary>
/// Implementation of the Item service.
/// Provides item template and instance management for games.
/// </summary>
[BannouService("item", typeof(IItemService), lifetime: ServiceLifetime.Scoped)]
public partial class ItemService : IItemService
{
    private readonly IMessageBus _messageBus;
    private readonly IStateStoreFactory _stateStoreFactory;
    private readonly ILogger<ItemService> _logger;
    private readonly ItemServiceConfiguration _configuration;

    // Parsed config defaults (boundary parsing per T25)
    private readonly ItemRarity _defaultRarity;
    private readonly WeightPrecision _defaultWeightPrecision;
    private readonly SoulboundType _defaultSoulboundType;

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
    public ItemService(
        IMessageBus messageBus,
        IStateStoreFactory stateStoreFactory,
        ILogger<ItemService> logger,
        ItemServiceConfiguration configuration)
    {
        _messageBus = messageBus;
        _stateStoreFactory = stateStoreFactory;
        _logger = logger;
        _configuration = configuration;

        // Parse config defaults once at startup (boundary parsing per T25)
        if (!Enum.TryParse<ItemRarity>(_configuration.DefaultRarity, true, out _defaultRarity))
        {
            _defaultRarity = ItemRarity.Common;
        }
        if (!Enum.TryParse<WeightPrecision>(_configuration.DefaultWeightPrecision, true, out _defaultWeightPrecision))
        {
            _defaultWeightPrecision = WeightPrecision.Decimal_2;
        }
        if (!Enum.TryParse<SoulboundType>(_configuration.DefaultSoulboundType, true, out _defaultSoulboundType))
        {
            _defaultSoulboundType = SoulboundType.None;
        }
    }

    #region Template Operations

    /// <inheritdoc/>
    public async Task<(StatusCodes, ItemTemplateResponse?)> CreateItemTemplateAsync(
        CreateItemTemplateRequest body,
        CancellationToken cancellationToken = default)
    {
        try
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
                MaxStackSize = body.MaxStackSize > 0 ? body.MaxStackSize : _configuration.DefaultMaxStackSize,
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
                IsActive = true,
                IsDeprecated = false,
                CreatedAt = now,
                UpdatedAt = now
            };

            // Claim the code index key atomically (prevents TOCTOU race on code uniqueness)
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
            await _messageBus.TryPublishAsync("item-template.created", new ItemTemplateCreatedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = now,
                TemplateId = templateId,
                Code = model.Code,
                GameId = model.GameId,
                Name = model.Name,
                Category = model.Category,
                Rarity = model.Rarity,
                QuantityModel = model.QuantityModel,
                Scope = model.Scope,
                IsActive = model.IsActive,
                CreatedAt = now
            }, cancellationToken);

            _logger.LogDebug("Created item template {TemplateId} code={Code}", templateId, body.Code);
            return (StatusCodes.OK, MapTemplateToResponse(model));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating item template for code {Code}", body.Code);
            await _messageBus.TryPublishErrorAsync(
                "item", "CreateItemTemplate", "unexpected_exception", ex.Message,
                dependency: null, endpoint: "post:/item/template/create",
                details: null, stack: ex.StackTrace);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, ItemTemplateResponse?)> GetItemTemplateAsync(
        GetItemTemplateRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var model = await ResolveTemplateAsync(body.TemplateId?.ToString(), body.Code, body.GameId, cancellationToken);
            if (model is null)
            {
                return (StatusCodes.NotFound, null);
            }
            return (StatusCodes.OK, MapTemplateToResponse(model));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting item template");
            await _messageBus.TryPublishErrorAsync(
                "item", "GetItemTemplate", "unexpected_exception", ex.Message,
                dependency: null, endpoint: "post:/item/template/get",
                details: null, stack: ex.StackTrace);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, ListItemTemplatesResponse?)> ListItemTemplatesAsync(
        ListItemTemplatesRequest body,
        CancellationToken cancellationToken = default)
    {
        try
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing item templates");
            await _messageBus.TryPublishErrorAsync(
                "item", "ListItemTemplates", "unexpected_exception", ex.Message,
                dependency: null, endpoint: "post:/item/template/list",
                details: null, stack: ex.StackTrace);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, ItemTemplateResponse?)> UpdateItemTemplateAsync(
        UpdateItemTemplateRequest body,
        CancellationToken cancellationToken = default)
    {
        try
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
            if (body.Rarity != default) model.Rarity = body.Rarity;
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
            if (body.IsActive.HasValue) model.IsActive = body.IsActive.Value;
            model.UpdatedAt = now;

            await templateStore.SaveAsync($"{TPL_PREFIX}{body.TemplateId}", model, cancellationToken: cancellationToken);

            // Invalidate cache after write
            await InvalidateTemplateCacheAsync(body.TemplateId.ToString(), cancellationToken);

            await _messageBus.TryPublishAsync("item-template.updated", new ItemTemplateUpdatedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = now,
                TemplateId = body.TemplateId,
                Code = model.Code,
                GameId = model.GameId,
                Name = model.Name,
                Category = model.Category,
                Rarity = model.Rarity,
                QuantityModel = model.QuantityModel,
                Scope = model.Scope,
                IsActive = model.IsActive,
                CreatedAt = model.CreatedAt,
                UpdatedAt = now
            }, cancellationToken);

            _logger.LogDebug("Updated item template {TemplateId}", body.TemplateId);
            return (StatusCodes.OK, MapTemplateToResponse(model));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating item template {TemplateId}", body.TemplateId);
            await _messageBus.TryPublishErrorAsync(
                "item", "UpdateItemTemplate", "unexpected_exception", ex.Message,
                dependency: null, endpoint: "post:/item/template/update",
                details: null, stack: ex.StackTrace);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, ItemTemplateResponse?)> DeprecateItemTemplateAsync(
        DeprecateItemTemplateRequest body,
        CancellationToken cancellationToken = default)
    {
        try
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
            model.MigrationTargetId = body.MigrationTargetId;
            model.UpdatedAt = now;

            await templateStore.SaveAsync($"{TPL_PREFIX}{body.TemplateId}", model, cancellationToken: cancellationToken);

            // Invalidate cache after write
            await InvalidateTemplateCacheAsync(body.TemplateId.ToString(), cancellationToken);

            await _messageBus.TryPublishAsync("item-template.deprecated", new ItemTemplateDeprecatedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = now,
                TemplateId = body.TemplateId,
                Code = model.Code,
                GameId = model.GameId,
                Name = model.Name,
                Category = model.Category,
                Rarity = model.Rarity,
                QuantityModel = model.QuantityModel,
                Scope = model.Scope,
                IsActive = model.IsActive,
                CreatedAt = model.CreatedAt,
                UpdatedAt = now
            }, cancellationToken);

            _logger.LogDebug("Deprecated item template {TemplateId}", body.TemplateId);
            return (StatusCodes.OK, MapTemplateToResponse(model));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deprecating item template {TemplateId}", body.TemplateId);
            await _messageBus.TryPublishErrorAsync(
                "item", "DeprecateItemTemplate", "unexpected_exception", ex.Message,
                dependency: null, endpoint: "post:/item/template/deprecate",
                details: null, stack: ex.StackTrace);
            return (StatusCodes.InternalServerError, null);
        }
    }

    #endregion

    #region Instance Operations

    /// <inheritdoc/>
    public async Task<(StatusCodes, ItemInstanceResponse?)> CreateItemInstanceAsync(
        CreateItemInstanceRequest body,
        CancellationToken cancellationToken = default)
    {
        try
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
                CreatedAt = now
            };

            await instanceStore.SaveAsync($"{INST_PREFIX}{instanceId}", model, cancellationToken: cancellationToken);
            await AddToListAsync(StateStoreDefinitions.ItemInstanceStore, $"{INST_CONTAINER_INDEX}{body.ContainerId}", instanceId.ToString(), cancellationToken);
            await AddToListAsync(StateStoreDefinitions.ItemInstanceStore, $"{INST_TEMPLATE_INDEX}{body.TemplateId}", instanceId.ToString(), cancellationToken);

            // Populate instance cache
            await PopulateInstanceCacheAsync(instanceId.ToString(), model, cancellationToken);

            await _messageBus.TryPublishAsync("item-instance.created", new ItemInstanceCreatedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = now,
                InstanceId = instanceId,
                TemplateId = body.TemplateId,
                ContainerId = body.ContainerId,
                RealmId = body.RealmId,
                Quantity = quantity,
                OriginType = body.OriginType,
                CreatedAt = now
            }, cancellationToken);

            _logger.LogDebug("Created item instance {InstanceId}", instanceId);
            return (StatusCodes.OK, MapInstanceToResponse(model));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating item instance");
            await _messageBus.TryPublishErrorAsync(
                "item", "CreateItemInstance", "unexpected_exception", ex.Message,
                dependency: null, endpoint: "post:/item/instance/create",
                details: null, stack: ex.StackTrace);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, ItemInstanceResponse?)> GetItemInstanceAsync(
        GetItemInstanceRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var model = await GetInstanceWithCacheAsync(body.InstanceId.ToString(), cancellationToken);

            if (model is null)
            {
                return (StatusCodes.NotFound, null);
            }

            return (StatusCodes.OK, MapInstanceToResponse(model));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting item instance {InstanceId}", body.InstanceId);
            await _messageBus.TryPublishErrorAsync(
                "item", "GetItemInstance", "unexpected_exception", ex.Message,
                dependency: null, endpoint: "post:/item/instance/get",
                details: null, stack: ex.StackTrace);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, ItemInstanceResponse?)> ModifyItemInstanceAsync(
        ModifyItemInstanceRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var instanceStore = _stateStoreFactory.GetStore<ItemInstanceModel>(StateStoreDefinitions.ItemInstanceStore);
            var model = await instanceStore.GetAsync($"{INST_PREFIX}{body.InstanceId}", cancellationToken);

            if (model is null)
            {
                return (StatusCodes.NotFound, null);
            }

            var now = DateTimeOffset.UtcNow;

            // Apply modifications
            if (body.DurabilityDelta.HasValue && model.CurrentDurability.HasValue)
            {
                model.CurrentDurability = Math.Max(0, model.CurrentDurability.Value + body.DurabilityDelta.Value);
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
            model.ModifiedAt = now;

            await instanceStore.SaveAsync($"{INST_PREFIX}{body.InstanceId}", model, cancellationToken: cancellationToken);

            // Invalidate cache after write
            await InvalidateInstanceCacheAsync(body.InstanceId.ToString(), cancellationToken);

            await _messageBus.TryPublishAsync("item-instance.modified", new ItemInstanceModifiedEvent
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error modifying item instance {InstanceId}", body.InstanceId);
            await _messageBus.TryPublishErrorAsync(
                "item", "ModifyItemInstance", "unexpected_exception", ex.Message,
                dependency: null, endpoint: "post:/item/instance/modify",
                details: null, stack: ex.StackTrace);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, ItemInstanceResponse?)> BindItemInstanceAsync(
        BindItemInstanceRequest body,
        CancellationToken cancellationToken = default)
    {
        try
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

            await _messageBus.TryPublishAsync("item-instance.bound", new ItemInstanceBoundEvent
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error binding item instance {InstanceId}", body.InstanceId);
            await _messageBus.TryPublishErrorAsync(
                "item", "BindItemInstance", "unexpected_exception", ex.Message,
                dependency: null, endpoint: "post:/item/instance/bind",
                details: null, stack: ex.StackTrace);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, DestroyItemInstanceResponse?)> DestroyItemInstanceAsync(
        DestroyItemInstanceRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var instanceStore = _stateStoreFactory.GetStore<ItemInstanceModel>(StateStoreDefinitions.ItemInstanceStore);
            var model = await instanceStore.GetAsync($"{INST_PREFIX}{body.InstanceId}", cancellationToken);

            if (model is null)
            {
                return (StatusCodes.NotFound, null);
            }

            // Get template to check destroyable (uses cache)
            var template = await GetTemplateWithCacheAsync(model.TemplateId.ToString(), cancellationToken);
            if (template is not null && !template.Destroyable && body.Reason != "admin")
            {
                _logger.LogWarning("Item {InstanceId} is not destroyable", body.InstanceId);
                return (StatusCodes.BadRequest, null);
            }

            var now = DateTimeOffset.UtcNow;

            // Remove from indexes
            await RemoveFromListAsync(StateStoreDefinitions.ItemInstanceStore, $"{INST_CONTAINER_INDEX}{model.ContainerId}", body.InstanceId.ToString(), cancellationToken);
            await RemoveFromListAsync(StateStoreDefinitions.ItemInstanceStore, $"{INST_TEMPLATE_INDEX}{model.TemplateId}", body.InstanceId.ToString(), cancellationToken);

            // Delete instance
            await instanceStore.DeleteAsync($"{INST_PREFIX}{body.InstanceId}", cancellationToken);

            // Invalidate cache after delete
            await InvalidateInstanceCacheAsync(body.InstanceId.ToString(), cancellationToken);

            await _messageBus.TryPublishAsync("item-instance.destroyed", new ItemInstanceDestroyedEvent
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
                Destroyed = true,
                InstanceId = body.InstanceId,
                TemplateId = model.TemplateId
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error destroying item instance {InstanceId}", body.InstanceId);
            await _messageBus.TryPublishErrorAsync(
                "item", "DestroyItemInstance", "unexpected_exception", ex.Message,
                dependency: null, endpoint: "post:/item/instance/destroy",
                details: null, stack: ex.StackTrace);
            return (StatusCodes.InternalServerError, null);
        }
    }

    #endregion

    #region Query Operations

    /// <inheritdoc/>
    public async Task<(StatusCodes, ListItemsResponse?)> ListItemsByContainerAsync(
        ListItemsByContainerRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var stringStore = _stateStoreFactory.GetStore<string>(StateStoreDefinitions.ItemInstanceStore);

            var idsJson = await stringStore.GetAsync($"{INST_CONTAINER_INDEX}{body.ContainerId}", cancellationToken);
            var ids = string.IsNullOrEmpty(idsJson)
                ? new List<string>()
                : BannouJson.Deserialize<List<string>>(idsJson) ?? new List<string>();

            var effectiveLimit = Math.Min(ids.Count, _configuration.MaxInstancesPerQuery);
            var items = new List<ItemInstanceResponse>();
            foreach (var id in ids.Take(effectiveLimit))
            {
                var model = await GetInstanceWithCacheAsync(id, cancellationToken);
                if (model is not null)
                {
                    items.Add(MapInstanceToResponse(model));
                }
            }

            return (StatusCodes.OK, new ListItemsResponse
            {
                Items = items,
                TotalCount = items.Count
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing items by container {ContainerId}", body.ContainerId);
            await _messageBus.TryPublishErrorAsync(
                "item", "ListItemsByContainer", "unexpected_exception", ex.Message,
                dependency: null, endpoint: "post:/item/instance/list-by-container",
                details: null, stack: ex.StackTrace);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, ListItemsResponse?)> ListItemsByTemplateAsync(
        ListItemsByTemplateRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var stringStore = _stateStoreFactory.GetStore<string>(StateStoreDefinitions.ItemInstanceStore);

            var idsJson = await stringStore.GetAsync($"{INST_TEMPLATE_INDEX}{body.TemplateId}", cancellationToken);
            var ids = string.IsNullOrEmpty(idsJson)
                ? new List<string>()
                : BannouJson.Deserialize<List<string>>(idsJson) ?? new List<string>();

            var effectiveLimit = Math.Min(body.Limit, _configuration.MaxInstancesPerQuery);
            var items = new List<ItemInstanceResponse>();
            foreach (var id in ids)
            {
                var model = await GetInstanceWithCacheAsync(id, cancellationToken);
                if (model is null) continue;

                // Apply realm filter
                if (body.RealmId.HasValue && model.RealmId != body.RealmId.Value) continue;

                items.Add(MapInstanceToResponse(model));
            }

            var totalCount = items.Count;
            var paged = items.Skip(body.Offset).Take(effectiveLimit).ToList();

            return (StatusCodes.OK, new ListItemsResponse
            {
                Items = paged,
                TotalCount = totalCount
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing items by template {TemplateId}", body.TemplateId);
            await _messageBus.TryPublishErrorAsync(
                "item", "ListItemsByTemplate", "unexpected_exception", ex.Message,
                dependency: null, endpoint: "post:/item/instance/list-by-template",
                details: null, stack: ex.StackTrace);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, BatchGetItemInstancesResponse?)> BatchGetItemInstancesAsync(
        BatchGetItemInstancesRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var items = new List<ItemInstanceResponse>();
            var notFound = new List<Guid>();

            foreach (var instanceId in body.InstanceIds)
            {
                var model = await GetInstanceWithCacheAsync(instanceId.ToString(), cancellationToken);
                if (model is not null)
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error batch getting item instances");
            await _messageBus.TryPublishErrorAsync(
                "item", "BatchGetItemInstances", "unexpected_exception", ex.Message,
                dependency: null, endpoint: "post:/item/instance/batch-get",
                details: null, stack: ex.StackTrace);
            return (StatusCodes.InternalServerError, null);
        }
    }

    #endregion

    #region Helper Methods

    private async Task<ItemTemplateModel?> ResolveTemplateAsync(string? templateId, string? code, string? gameId, CancellationToken ct)
    {
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

            if (etag is null)
            {
                // First write - no concurrency concern
                await stringStore.SaveAsync(key, serialized, cancellationToken: ct);
                return;
            }

            var newEtag = await stringStore.TrySaveAsync(key, serialized, etag, ct);
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
        var stringStore = _stateStoreFactory.GetStore<string>(storeName);
        var maxRetries = _configuration.ListOperationMaxRetries;

        for (var attempt = 0; attempt < maxRetries; attempt++)
        {
            var (json, etag) = await stringStore.GetWithETagAsync(key, ct);
            if (string.IsNullOrEmpty(json)) return;

            var list = BannouJson.Deserialize<List<string>>(json) ?? new List<string>();
            if (!list.Remove(value)) return;

            var serialized = BannouJson.Serialize(list);

            if (etag is null)
            {
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
        var cacheStore = _stateStoreFactory.GetStore<ItemTemplateModel>(StateStoreDefinitions.ItemTemplateCache);
        await cacheStore.SaveAsync($"{TPL_PREFIX}{templateId}", model,
            new StateOptions { Ttl = _configuration.TemplateCacheTtlSeconds }, ct);
    }

    /// <summary>
    /// Invalidate template cache after a write/update operation.
    /// </summary>
    private async Task InvalidateTemplateCacheAsync(string templateId, CancellationToken ct)
    {
        var cacheStore = _stateStoreFactory.GetStore<ItemTemplateModel>(StateStoreDefinitions.ItemTemplateCache);
        await cacheStore.DeleteAsync($"{TPL_PREFIX}{templateId}", ct);
    }

    /// <summary>
    /// Get instance with Redis cache read-through. Falls back to MySQL persistent store on cache miss.
    /// </summary>
    private async Task<ItemInstanceModel?> GetInstanceWithCacheAsync(string instanceId, CancellationToken ct)
    {
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
    /// Populate instance cache after a write operation.
    /// </summary>
    private async Task PopulateInstanceCacheAsync(string instanceId, ItemInstanceModel model, CancellationToken ct)
    {
        var cacheStore = _stateStoreFactory.GetStore<ItemInstanceModel>(StateStoreDefinitions.ItemInstanceCache);
        await cacheStore.SaveAsync($"{INST_PREFIX}{instanceId}", model,
            new StateOptions { Ttl = _configuration.InstanceCacheTtlSeconds }, ct);
    }

    /// <summary>
    /// Invalidate instance cache after a write/delete operation.
    /// </summary>
    private async Task InvalidateInstanceCacheAsync(string instanceId, CancellationToken ct)
    {
        var cacheStore = _stateStoreFactory.GetStore<ItemInstanceModel>(StateStoreDefinitions.ItemInstanceCache);
        await cacheStore.DeleteAsync($"{INST_PREFIX}{instanceId}", ct);
    }

    #endregion

    #region Mapping Methods

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
            IsActive = model.IsActive,
            IsDeprecated = model.IsDeprecated,
            DeprecatedAt = model.DeprecatedAt,
            MigrationTargetId = model.MigrationTargetId,
            CreatedAt = model.CreatedAt,
            UpdatedAt = model.UpdatedAt
        };
    }

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
            CreatedAt = model.CreatedAt,
            ModifiedAt = model.ModifiedAt
        };
    }

    #endregion
}

#region Internal Models

/// <summary>
/// Internal storage model for item templates.
/// </summary>
internal class ItemTemplateModel
{
    public Guid TemplateId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string GameId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public ItemCategory Category { get; set; }
    public string? Subcategory { get; set; }
    public List<string> Tags { get; set; } = new();
    public ItemRarity Rarity { get; set; }
    public QuantityModel QuantityModel { get; set; }
    public int MaxStackSize { get; set; }
    public string? UnitOfMeasure { get; set; }
    public WeightPrecision WeightPrecision { get; set; }
    public double? Weight { get; set; }
    public double? Volume { get; set; }
    public int? GridWidth { get; set; }
    public int? GridHeight { get; set; }
    public bool? CanRotate { get; set; }
    public double? BaseValue { get; set; }
    public bool Tradeable { get; set; }
    public bool Destroyable { get; set; }
    public SoulboundType SoulboundType { get; set; }
    public bool HasDurability { get; set; }
    public int? MaxDurability { get; set; }
    public ItemScope Scope { get; set; }
    public List<Guid>? AvailableRealms { get; set; }
    public string? Stats { get; set; }
    public string? Effects { get; set; }
    public string? Requirements { get; set; }
    public string? Display { get; set; }
    public string? Metadata { get; set; }
    public bool IsActive { get; set; }
    public bool IsDeprecated { get; set; }
    public DateTimeOffset? DeprecatedAt { get; set; }
    public Guid? MigrationTargetId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// Internal storage model for item instances.
/// </summary>
internal class ItemInstanceModel
{
    public Guid InstanceId { get; set; }
    public Guid TemplateId { get; set; }
    public Guid ContainerId { get; set; }
    public Guid RealmId { get; set; }
    public double Quantity { get; set; }
    public int? SlotIndex { get; set; }
    public int? SlotX { get; set; }
    public int? SlotY { get; set; }
    public bool? Rotated { get; set; }
    public int? CurrentDurability { get; set; }
    public Guid? BoundToId { get; set; }
    public DateTimeOffset? BoundAt { get; set; }
    public string? CustomStats { get; set; }
    public string? CustomName { get; set; }
    public string? InstanceMetadata { get; set; }
    public ItemOriginType OriginType { get; set; }
    public Guid? OriginId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ModifiedAt { get; set; }
}

#endregion
