using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Character;
using BeyondImmersion.BannouService.Contract;
using BeyondImmersion.BannouService.Currency;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.GameService;
using BeyondImmersion.BannouService.Inventory;
using BeyondImmersion.BannouService.Item;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.License;

/// <summary>
/// Topic constants for license board events.
/// </summary>
public static class LicenseTopics
{
    /// <summary>License unlocked event topic.</summary>
    public const string LicenseUnlocked = "license.unlocked";
    /// <summary>License unlock failed event topic.</summary>
    public const string LicenseUnlockFailed = "license.unlock-failed";
    /// <summary>License board cloned event topic.</summary>
    public const string LicenseBoardCloned = "license-board.cloned";
}

/// <summary>
/// Implementation of the License service.
/// Provides grid-based progression boards (skill trees, license boards, tech trees)
/// by combining Inventory (containers), Items (license nodes), and Contracts (unlock behavior).
/// </summary>
/// <remarks>
/// <para>
/// <b>FOUNDATION TENETS - PARTIAL CLASS REQUIRED:</b> This class MUST remain a partial class.
/// Generated code (event handlers, permissions) is placed in companion partial classes.
/// </para>
/// <para>
/// <b>SERVICE HIERARCHY (L4 Game Features):</b>
/// <list type="bullet">
///   <item>Hard dependencies (constructor injection): IContractClient (L1), ICharacterClient (L2, used for character owner validation), IInventoryClient (L2), IItemClient (L2), ICurrencyClient (L2), IGameServiceClient (L2), IDistributedLockProvider (L0)</item>
/// </list>
/// </para>
/// </remarks>
[BannouService("license", typeof(ILicenseService), lifetime: ServiceLifetime.Scoped, layer: ServiceLayer.GameFeatures)]
public partial class LicenseService : ILicenseService
{
    private readonly IMessageBus _messageBus;
    private readonly IStateStoreFactory _stateStoreFactory;
    private readonly ILogger<LicenseService> _logger;
    private readonly LicenseServiceConfiguration _configuration;
    private readonly IContractClient _contractClient;
    private readonly ICharacterClient _characterClient;
    private readonly IInventoryClient _inventoryClient;
    private readonly IItemClient _itemClient;
    private readonly ICurrencyClient _currencyClient;
    private readonly IGameServiceClient _gameServiceClient;
    private readonly IDistributedLockProvider _lockProvider;

    #region State Store Accessors

    private IQueryableStateStore<BoardTemplateModel>? _boardTemplateStore;
    private IQueryableStateStore<BoardTemplateModel> BoardTemplateStore =>
        _boardTemplateStore ??= _stateStoreFactory.GetQueryableStore<BoardTemplateModel>(StateStoreDefinitions.LicenseBoardTemplates);

    private IQueryableStateStore<LicenseDefinitionModel>? _definitionStore;
    private IQueryableStateStore<LicenseDefinitionModel> DefinitionStore =>
        _definitionStore ??= _stateStoreFactory.GetQueryableStore<LicenseDefinitionModel>(StateStoreDefinitions.LicenseDefinitions);

    private IQueryableStateStore<BoardInstanceModel>? _boardStore;
    private IQueryableStateStore<BoardInstanceModel> BoardStore =>
        _boardStore ??= _stateStoreFactory.GetQueryableStore<BoardInstanceModel>(StateStoreDefinitions.LicenseBoards);

    private IStateStore<BoardCacheModel>? _boardCache;
    private IStateStore<BoardCacheModel> BoardCache =>
        _boardCache ??= _stateStoreFactory.GetStore<BoardCacheModel>(StateStoreDefinitions.LicenseBoardCache);

    #endregion

    #region Key Building

    private static string BuildTemplateKey(Guid boardTemplateId) => $"board-tpl:{boardTemplateId}";
    private static string BuildDefinitionKey(Guid boardTemplateId, string code) => $"lic-def:{boardTemplateId}:{code}";
    private static string BuildBoardKey(Guid boardId) => $"board:{boardId}";
    private static string BuildBoardByOwnerKey(string ownerType, Guid ownerId, Guid boardTemplateId) => $"board-owner:{ownerType}:{ownerId}:{boardTemplateId}";
    private static string BuildBoardCacheKey(Guid boardId) => $"cache:{boardId}";
    private static string BuildBoardLockKey(Guid boardId) => $"board:{boardId}";
    private static string BuildTemplateLockKey(Guid boardTemplateId) => $"tpl:{boardTemplateId}";

    #endregion

    #region Owner Type Mapping

    /// <summary>
    /// Maps an opaque owner type string to ContainerOwnerType for inventory operations.
    /// Returns null if the owner type has no known mapping.
    /// </summary>
    private static ContainerOwnerType? MapToContainerOwnerType(string ownerType) => ownerType switch
    {
        "character" => ContainerOwnerType.Character,
        "account" => ContainerOwnerType.Account,
        "location" => ContainerOwnerType.Location,
        "guild" => ContainerOwnerType.Guild,
        _ => null
    };

    /// <summary>
    /// Maps an opaque owner type string to WalletOwnerType for currency operations.
    /// Returns null if the owner type has no known mapping.
    /// </summary>
    private static WalletOwnerType? MapToWalletOwnerType(string ownerType) => ownerType switch
    {
        "character" => WalletOwnerType.Character,
        "account" => WalletOwnerType.Account,
        "guild" => WalletOwnerType.Guild,
        "npc" => WalletOwnerType.Npc,
        _ => null
    };

    /// <summary>
    /// Maps an opaque owner type string to EntityType for contract parties.
    /// Returns null if the owner type has no known mapping.
    /// </summary>
    private static EntityType? MapToEntityType(string ownerType) => ownerType switch
    {
        "character" => EntityType.Character,
        "account" => EntityType.Account,
        "realm" => EntityType.Realm,
        "guild" => EntityType.Guild,
        "location" => EntityType.Location,
        "actor" => EntityType.Actor,
        _ => null
    };

    /// <summary>
    /// Validates that an owner type string does not contain the key separator character.
    /// </summary>
    private static bool IsValidOwnerType(string ownerType)
        => !string.IsNullOrWhiteSpace(ownerType) && !ownerType.Contains(':');

    #endregion

    /// <summary>
    /// Initializes a new instance of the <see cref="LicenseService"/> class.
    /// </summary>
    /// <param name="messageBus">Message bus for event publishing.</param>
    /// <param name="stateStoreFactory">State store factory for persistence.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="configuration">Service configuration.</param>
    /// <param name="contractClient">Contract client for unlock execution (L1 hard dependency).</param>
    /// <param name="characterClient">Character client for validation (L2 hard dependency).</param>
    /// <param name="inventoryClient">Inventory client for board containers (L2 hard dependency).</param>
    /// <param name="itemClient">Item client for license item instances (L2 hard dependency).</param>
    /// <param name="currencyClient">Currency client for LP balance checks (L2 hard dependency).</param>
    /// <param name="gameServiceClient">Game service client for validation (L2 hard dependency).</param>
    /// <param name="lockProvider">Distributed lock provider (L0 hard dependency).</param>
    public LicenseService(
        IMessageBus messageBus,
        IStateStoreFactory stateStoreFactory,
        ILogger<LicenseService> logger,
        LicenseServiceConfiguration configuration,
        IContractClient contractClient,
        ICharacterClient characterClient,
        IInventoryClient inventoryClient,
        IItemClient itemClient,
        ICurrencyClient currencyClient,
        IGameServiceClient gameServiceClient,
        IDistributedLockProvider lockProvider)
    {
        _messageBus = messageBus;
        _stateStoreFactory = stateStoreFactory;
        _logger = logger;
        _configuration = configuration;
        _contractClient = contractClient;
        _characterClient = characterClient;
        _inventoryClient = inventoryClient;
        _itemClient = itemClient;
        _currencyClient = currencyClient;
        _gameServiceClient = gameServiceClient;
        _lockProvider = lockProvider;
    }

    #region Adjacency Helper

    /// <summary>
    /// Checks if two grid positions are adjacent under the given adjacency mode.
    /// </summary>
    private static bool IsAdjacent(int x1, int y1, int x2, int y2, AdjacencyMode mode)
    {
        var dx = Math.Abs(x1 - x2);
        var dy = Math.Abs(y1 - y2);

        if (dx == 0 && dy == 0) return false; // Same position

        return mode switch
        {
            AdjacencyMode.FourWay => dx + dy == 1,
            AdjacencyMode.EightWay => dx <= 1 && dy <= 1,
            _ => dx <= 1 && dy <= 1 // Default to eight-way
        };
    }

    #endregion

    #region Model Mapping Helpers

    private static BoardTemplateResponse MapTemplateToResponse(BoardTemplateModel model)
    {
        return new BoardTemplateResponse
        {
            BoardTemplateId = model.BoardTemplateId,
            GameServiceId = model.GameServiceId,
            Name = model.Name,
            Description = model.Description,
            GridWidth = model.GridWidth,
            GridHeight = model.GridHeight,
            StartingNodes = model.StartingNodes.Select(sn => new GridPosition { X = sn.X, Y = sn.Y }).ToList(),
            BoardContractTemplateId = model.BoardContractTemplateId,
            AdjacencyMode = model.AdjacencyMode,
            AllowedOwnerTypes = model.AllowedOwnerTypes,
            IsActive = model.IsActive,
            CreatedAt = model.CreatedAt,
            UpdatedAt = model.UpdatedAt
        };
    }

    private static LicenseDefinitionResponse MapDefinitionToResponse(LicenseDefinitionModel model)
    {
        return new LicenseDefinitionResponse
        {
            LicenseDefinitionId = model.LicenseDefinitionId,
            BoardTemplateId = model.BoardTemplateId,
            Code = model.Code,
            Position = new GridPosition { X = model.PositionX, Y = model.PositionY },
            LpCost = model.LpCost,
            ItemTemplateId = model.ItemTemplateId,
            Prerequisites = model.Prerequisites?.ToList(),
            Description = model.Description,
            Metadata = model.Metadata,
            CreatedAt = model.CreatedAt
        };
    }

    private static BoardResponse MapBoardToResponse(BoardInstanceModel model)
    {
        return new BoardResponse
        {
            BoardId = model.BoardId,
            OwnerType = model.OwnerType,
            OwnerId = model.OwnerId,
            RealmId = model.RealmId,
            BoardTemplateId = model.BoardTemplateId,
            GameServiceId = model.GameServiceId,
            ContainerId = model.ContainerId,
            CreatedAt = model.CreatedAt
        };
    }

    #endregion

    #region Board Cache Reconciliation

    /// <summary>
    /// Loads or rebuilds the board cache. Tries Redis cache first; on miss,
    /// queries inventory as the authoritative source and rebuilds the cache.
    /// </summary>
    private async Task<BoardCacheModel> LoadOrRebuildBoardCacheAsync(
        BoardInstanceModel board,
        IReadOnlyList<LicenseDefinitionModel> definitions,
        CancellationToken cancellationToken)
    {
        // Try cache first
        var cache = await BoardCache.GetAsync(BuildBoardCacheKey(board.BoardId), cancellationToken);
        if (cache != null)
        {
            return cache;
        }

        // Cache miss: rebuild from inventory (authoritative source)
        _logger.LogInformation("Board cache miss for board {BoardId}, rebuilding from inventory", board.BoardId);

        var containerContents = await _inventoryClient.GetContainerAsync(
            new GetContainerRequest { ContainerId = board.ContainerId, IncludeContents = true },
            cancellationToken);

        // Build a lookup from item template ID to definition for matching
        var defsByTemplateId = definitions
            .GroupBy(d => d.ItemTemplateId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var unlockedEntries = new List<UnlockedLicenseEntry>();
        foreach (var item in containerContents.Items)
        {
            if (defsByTemplateId.TryGetValue(item.TemplateId, out var matchingDefs))
            {
                // Find the definition that matches this item (could be multiple defs with same template)
                // Use the first unmatched one
                var matchedDef = matchingDefs.FirstOrDefault(d =>
                    !unlockedEntries.Any(u => u.Code == d.Code));

                if (matchedDef != null)
                {
                    unlockedEntries.Add(new UnlockedLicenseEntry
                    {
                        Code = matchedDef.Code,
                        PositionX = matchedDef.PositionX,
                        PositionY = matchedDef.PositionY,
                        ItemInstanceId = item.InstanceId,
                        UnlockedAt = board.CreatedAt // Best approximation when rebuilding
                    });
                }
            }
        }

        cache = new BoardCacheModel
        {
            BoardId = board.BoardId,
            UnlockedPositions = unlockedEntries,
            LastUpdated = DateTimeOffset.UtcNow
        };

        // Persist rebuilt cache
        await BoardCache.SaveAsync(
            BuildBoardCacheKey(board.BoardId),
            cache,
            new StateOptions { Ttl = _configuration.BoardCacheTtlSeconds },
            cancellationToken);

        return cache;
    }

    #endregion

    #region Board Template Management

    /// <inheritdoc/>
    public async Task<(StatusCodes, BoardTemplateResponse?)> CreateBoardTemplateAsync(
        CreateBoardTemplateRequest body,
        CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Creating board template {Name} for game service {GameServiceId}",
                body.Name, body.GameServiceId);

            // Validate game service exists
            try
            {
                await _gameServiceClient.GetServiceAsync(
                    new GetServiceRequest { ServiceId = body.GameServiceId },
                    cancellationToken);
            }
            catch (ApiException ex) when (ex.StatusCode == 404)
            {
                _logger.LogWarning("Game service {GameServiceId} not found", body.GameServiceId);
                return (StatusCodes.NotFound, null);
            }

            // Validate starting nodes within grid bounds
            foreach (var node in body.StartingNodes)
            {
                if (node.X < 0 || node.X >= body.GridWidth || node.Y < 0 || node.Y >= body.GridHeight)
                {
                    _logger.LogWarning(
                        "Starting node ({X}, {Y}) is out of bounds for grid {Width}x{Height}",
                        node.X, node.Y, body.GridWidth, body.GridHeight);
                    return (StatusCodes.BadRequest, null);
                }
            }

            // Validate allowedOwnerTypes all map to known ContainerOwnerType values
            foreach (var ownerType in body.AllowedOwnerTypes)
            {
                if (MapToContainerOwnerType(ownerType) == null)
                {
                    _logger.LogWarning("Unknown owner type {OwnerType} in allowedOwnerTypes", ownerType);
                    return (StatusCodes.BadRequest, null);
                }
            }

            // Validate contract template exists
            try
            {
                await _contractClient.GetContractTemplateAsync(
                    new GetContractTemplateRequest { TemplateId = body.BoardContractTemplateId },
                    cancellationToken);
            }
            catch (ApiException ex) when (ex.StatusCode == 404)
            {
                _logger.LogWarning("Contract template {ContractTemplateId} not found", body.BoardContractTemplateId);
                return (StatusCodes.NotFound, null);
            }

            var now = DateTimeOffset.UtcNow;
            var template = new BoardTemplateModel
            {
                BoardTemplateId = Guid.NewGuid(),
                GameServiceId = body.GameServiceId,
                Name = body.Name,
                Description = body.Description,
                GridWidth = body.GridWidth,
                GridHeight = body.GridHeight,
                StartingNodes = body.StartingNodes.Select(sn => new GridPositionEntry { X = sn.X, Y = sn.Y }).ToList(),
                BoardContractTemplateId = body.BoardContractTemplateId,
                AdjacencyMode = body.AdjacencyMode ?? _configuration.DefaultAdjacencyMode,
                AllowedOwnerTypes = body.AllowedOwnerTypes.ToList(),
                IsActive = true,
                CreatedAt = now
            };

            await BoardTemplateStore.SaveAsync(
                BuildTemplateKey(template.BoardTemplateId),
                template,
                cancellationToken: cancellationToken);

            _logger.LogInformation("Created board template {BoardTemplateId}", template.BoardTemplateId);

            await _messageBus.TryPublishAsync(
                "license-board-template.created",
                new LicenseBoardTemplateCreatedEvent
                {
                    BoardTemplateId = template.BoardTemplateId,
                    GameServiceId = template.GameServiceId,
                    Name = template.Name,
                    GridWidth = template.GridWidth,
                    GridHeight = template.GridHeight,
                    BoardContractTemplateId = template.BoardContractTemplateId,
                    AdjacencyMode = template.AdjacencyMode,
                    IsActive = template.IsActive,
                    CreatedAt = template.CreatedAt,
                    UpdatedAt = template.UpdatedAt
                },
                cancellationToken: cancellationToken);

            return (StatusCodes.OK, MapTemplateToResponse(template));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating board template");
            await _messageBus.TryPublishErrorAsync(
                "license", "CreateBoardTemplate", "unexpected_exception", ex.Message,
                dependency: null, endpoint: "post:/license/board-template/create",
                details: null, stack: ex.StackTrace, cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, BoardTemplateResponse?)> GetBoardTemplateAsync(
        GetBoardTemplateRequest body,
        CancellationToken cancellationToken)
    {
        try
        {
            var template = await BoardTemplateStore.GetAsync(
                BuildTemplateKey(body.BoardTemplateId),
                cancellationToken);

            if (template == null)
            {
                return (StatusCodes.NotFound, null);
            }

            return (StatusCodes.OK, MapTemplateToResponse(template));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting board template {BoardTemplateId}", body.BoardTemplateId);
            await _messageBus.TryPublishErrorAsync(
                "license", "GetBoardTemplate", "unexpected_exception", ex.Message,
                dependency: null, endpoint: "post:/license/board-template/get",
                details: null, stack: ex.StackTrace, cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, ListBoardTemplatesResponse?)> ListBoardTemplatesAsync(
        ListBoardTemplatesRequest body,
        CancellationToken cancellationToken)
    {
        try
        {
            var pageSize = body.PageSize ?? _configuration.DefaultPageSize;

            var results = await BoardTemplateStore.QueryAsync(
                t => t.GameServiceId == body.GameServiceId,
                cancellationToken: cancellationToken);

            // Simple cursor-based pagination using index offset
            var startIndex = 0;
            if (!string.IsNullOrEmpty(body.Cursor) && int.TryParse(body.Cursor, out var cursorIndex))
            {
                startIndex = cursorIndex;
            }

            var paged = results.Skip(startIndex).Take(pageSize + 1).ToList();
            var hasMore = paged.Count > pageSize;
            var items = paged.Take(pageSize).ToList();

            return (StatusCodes.OK, new ListBoardTemplatesResponse
            {
                Templates = items.Select(MapTemplateToResponse).ToList(),
                NextCursor = hasMore ? (startIndex + pageSize).ToString() : null,
                HasMore = hasMore
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing board templates for game service {GameServiceId}", body.GameServiceId);
            await _messageBus.TryPublishErrorAsync(
                "license", "ListBoardTemplates", "unexpected_exception", ex.Message,
                dependency: null, endpoint: "post:/license/board-template/list",
                details: null, stack: ex.StackTrace, cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, BoardTemplateResponse?)> UpdateBoardTemplateAsync(
        UpdateBoardTemplateRequest body,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var lockHandle = await _lockProvider.LockAsync(
                StateStoreDefinitions.LicenseLock,
                BuildTemplateLockKey(body.BoardTemplateId),
                Guid.NewGuid().ToString(),
                _configuration.LockTimeoutSeconds,
                cancellationToken);

            if (!lockHandle.Success)
            {
                _logger.LogWarning("Failed to acquire lock for board template {BoardTemplateId}", body.BoardTemplateId);
                return (StatusCodes.Conflict, null);
            }

            var template = await BoardTemplateStore.GetAsync(
                BuildTemplateKey(body.BoardTemplateId),
                cancellationToken);

            if (template == null)
            {
                return (StatusCodes.NotFound, null);
            }

            // Update mutable fields, tracking what changed
            var changedFields = new List<string>();
            if (body.Name != null) { template.Name = body.Name; changedFields.Add("name"); }
            if (body.Description != null) { template.Description = body.Description; changedFields.Add("description"); }
            if (body.IsActive.HasValue) { template.IsActive = body.IsActive.Value; changedFields.Add("isActive"); }
            if (body.AllowedOwnerTypes != null)
            {
                // Validate all entries map to known ContainerOwnerType
                foreach (var ownerType in body.AllowedOwnerTypes)
                {
                    if (MapToContainerOwnerType(ownerType) == null)
                    {
                        _logger.LogWarning("Unknown owner type {OwnerType} in allowedOwnerTypes", ownerType);
                        return (StatusCodes.BadRequest, null);
                    }
                }
                template.AllowedOwnerTypes = body.AllowedOwnerTypes.ToList();
                changedFields.Add("allowedOwnerTypes");
            }
            template.UpdatedAt = DateTimeOffset.UtcNow;

            await BoardTemplateStore.SaveAsync(
                BuildTemplateKey(template.BoardTemplateId),
                template,
                cancellationToken: cancellationToken);

            _logger.LogInformation("Updated board template {BoardTemplateId}", template.BoardTemplateId);

            await _messageBus.TryPublishAsync(
                "license-board-template.updated",
                new LicenseBoardTemplateUpdatedEvent
                {
                    BoardTemplateId = template.BoardTemplateId,
                    GameServiceId = template.GameServiceId,
                    Name = template.Name,
                    GridWidth = template.GridWidth,
                    GridHeight = template.GridHeight,
                    BoardContractTemplateId = template.BoardContractTemplateId,
                    AdjacencyMode = template.AdjacencyMode,
                    IsActive = template.IsActive,
                    CreatedAt = template.CreatedAt,
                    UpdatedAt = template.UpdatedAt,
                    ChangedFields = changedFields
                },
                cancellationToken: cancellationToken);

            return (StatusCodes.OK, MapTemplateToResponse(template));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating board template {BoardTemplateId}", body.BoardTemplateId);
            await _messageBus.TryPublishErrorAsync(
                "license", "UpdateBoardTemplate", "unexpected_exception", ex.Message,
                dependency: null, endpoint: "post:/license/board-template/update",
                details: null, stack: ex.StackTrace, cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, BoardTemplateResponse?)> DeleteBoardTemplateAsync(
        DeleteBoardTemplateRequest body,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var lockHandle = await _lockProvider.LockAsync(
                StateStoreDefinitions.LicenseLock,
                BuildTemplateLockKey(body.BoardTemplateId),
                Guid.NewGuid().ToString(),
                _configuration.LockTimeoutSeconds,
                cancellationToken);

            if (!lockHandle.Success)
            {
                _logger.LogWarning("Failed to acquire lock for board template {BoardTemplateId}", body.BoardTemplateId);
                return (StatusCodes.Conflict, null);
            }

            var template = await BoardTemplateStore.GetAsync(
                BuildTemplateKey(body.BoardTemplateId),
                cancellationToken);

            if (template == null)
            {
                return (StatusCodes.NotFound, null);
            }

            // Check for active board instances
            var activeBoards = await BoardStore.QueryAsync(
                b => b.BoardTemplateId == body.BoardTemplateId,
                cancellationToken: cancellationToken);

            if (activeBoards.Count > 0)
            {
                _logger.LogWarning(
                    "Cannot delete board template {BoardTemplateId} with {ActiveCount} active board instances",
                    body.BoardTemplateId, activeBoards.Count);
                return (StatusCodes.Conflict, null);
            }

            await BoardTemplateStore.DeleteAsync(
                BuildTemplateKey(body.BoardTemplateId),
                cancellationToken);

            _logger.LogInformation("Deleted board template {BoardTemplateId}", body.BoardTemplateId);

            await _messageBus.TryPublishAsync(
                "license-board-template.deleted",
                new LicenseBoardTemplateDeletedEvent
                {
                    BoardTemplateId = template.BoardTemplateId,
                    GameServiceId = template.GameServiceId,
                    Name = template.Name,
                    GridWidth = template.GridWidth,
                    GridHeight = template.GridHeight,
                    BoardContractTemplateId = template.BoardContractTemplateId,
                    AdjacencyMode = template.AdjacencyMode,
                    IsActive = template.IsActive,
                    CreatedAt = template.CreatedAt,
                    UpdatedAt = template.UpdatedAt
                },
                cancellationToken: cancellationToken);

            return (StatusCodes.OK, MapTemplateToResponse(template));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting board template {BoardTemplateId}", body.BoardTemplateId);
            await _messageBus.TryPublishErrorAsync(
                "license", "DeleteBoardTemplate", "unexpected_exception", ex.Message,
                dependency: null, endpoint: "post:/license/board-template/delete",
                details: null, stack: ex.StackTrace, cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    #endregion

    #region License Definition Management

    /// <inheritdoc/>
    public async Task<(StatusCodes, LicenseDefinitionResponse?)> AddLicenseDefinitionAsync(
        AddLicenseDefinitionRequest body,
        CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Adding license definition {Code} to board template {BoardTemplateId}",
                body.Code, body.BoardTemplateId);

            // Acquire template lock for multi-instance safety (IMPLEMENTATION TENETS)
            await using var lockHandle = await _lockProvider.LockAsync(
                StateStoreDefinitions.LicenseLock,
                BuildTemplateLockKey(body.BoardTemplateId),
                Guid.NewGuid().ToString(),
                _configuration.LockTimeoutSeconds,
                cancellationToken);

            if (!lockHandle.Success)
            {
                _logger.LogWarning("Failed to acquire lock for board template {BoardTemplateId}", body.BoardTemplateId);
                return (StatusCodes.Conflict, null);
            }

            // Validate board template exists
            var template = await BoardTemplateStore.GetAsync(
                BuildTemplateKey(body.BoardTemplateId),
                cancellationToken);

            if (template == null)
            {
                return (StatusCodes.NotFound, null);
            }

            // Validate position within grid bounds
            if (body.Position.X < 0 || body.Position.X >= template.GridWidth ||
                body.Position.Y < 0 || body.Position.Y >= template.GridHeight)
            {
                _logger.LogWarning(
                    "Position ({X}, {Y}) is out of bounds for grid {Width}x{Height}",
                    body.Position.X, body.Position.Y, template.GridWidth, template.GridHeight);
                return (StatusCodes.BadRequest, null);
            }

            // Check max definitions per board
            var existingDefs = await DefinitionStore.QueryAsync(
                d => d.BoardTemplateId == body.BoardTemplateId,
                cancellationToken: cancellationToken);

            if (existingDefs.Count >= _configuration.MaxDefinitionsPerBoard)
            {
                _logger.LogWarning(
                    "Board template {BoardTemplateId} has reached max definitions limit of {Max}",
                    body.BoardTemplateId, _configuration.MaxDefinitionsPerBoard);
                return (StatusCodes.Conflict, null);
            }

            // Check for duplicate code
            var existingByCode = existingDefs.FirstOrDefault(d => d.Code == body.Code);
            if (existingByCode != null)
            {
                _logger.LogWarning("Duplicate license code {Code} on board template {BoardTemplateId}",
                    body.Code, body.BoardTemplateId);
                return (StatusCodes.Conflict, null);
            }

            // Check for duplicate position
            var existingByPos = existingDefs.FirstOrDefault(
                d => d.PositionX == body.Position.X && d.PositionY == body.Position.Y);
            if (existingByPos != null)
            {
                _logger.LogWarning(
                    "Duplicate position ({X}, {Y}) on board template {BoardTemplateId}",
                    body.Position.X, body.Position.Y, body.BoardTemplateId);
                return (StatusCodes.Conflict, null);
            }

            // Validate item template exists
            try
            {
                await _itemClient.GetItemTemplateAsync(
                    new GetItemTemplateRequest { TemplateId = body.ItemTemplateId },
                    cancellationToken);
            }
            catch (ApiException ex) when (ex.StatusCode == 404)
            {
                _logger.LogWarning("Item template {ItemTemplateId} not found", body.ItemTemplateId);
                return (StatusCodes.NotFound, null);
            }

            var now = DateTimeOffset.UtcNow;
            var definition = new LicenseDefinitionModel
            {
                LicenseDefinitionId = Guid.NewGuid(),
                BoardTemplateId = body.BoardTemplateId,
                Code = body.Code,
                PositionX = body.Position.X,
                PositionY = body.Position.Y,
                LpCost = body.LpCost,
                ItemTemplateId = body.ItemTemplateId,
                Prerequisites = body.Prerequisites?.ToList(),
                Description = body.Description,
                Metadata = MetadataHelper.ConvertToDictionary(body.Metadata),
                CreatedAt = now
            };

            await DefinitionStore.SaveAsync(
                BuildDefinitionKey(body.BoardTemplateId, body.Code),
                definition,
                cancellationToken: cancellationToken);

            _logger.LogInformation("Added license definition {Code} at ({X}, {Y}) to board template {BoardTemplateId}",
                body.Code, body.Position.X, body.Position.Y, body.BoardTemplateId);

            return (StatusCodes.OK, MapDefinitionToResponse(definition));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding license definition {Code}", body.Code);
            await _messageBus.TryPublishErrorAsync(
                "license", "AddLicenseDefinition", "unexpected_exception", ex.Message,
                dependency: null, endpoint: "post:/license/definition/add",
                details: null, stack: ex.StackTrace, cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, LicenseDefinitionResponse?)> GetLicenseDefinitionAsync(
        GetLicenseDefinitionRequest body,
        CancellationToken cancellationToken)
    {
        try
        {
            var definition = await DefinitionStore.GetAsync(
                BuildDefinitionKey(body.BoardTemplateId, body.Code),
                cancellationToken);

            if (definition == null)
            {
                return (StatusCodes.NotFound, null);
            }

            return (StatusCodes.OK, MapDefinitionToResponse(definition));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting license definition {Code} for template {BoardTemplateId}",
                body.Code, body.BoardTemplateId);
            await _messageBus.TryPublishErrorAsync(
                "license", "GetLicenseDefinition", "unexpected_exception", ex.Message,
                dependency: null, endpoint: "post:/license/definition/get",
                details: null, stack: ex.StackTrace, cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, ListLicenseDefinitionsResponse?)> ListLicenseDefinitionsAsync(
        ListLicenseDefinitionsRequest body,
        CancellationToken cancellationToken)
    {
        try
        {
            var definitions = await DefinitionStore.QueryAsync(
                d => d.BoardTemplateId == body.BoardTemplateId,
                cancellationToken: cancellationToken);

            return (StatusCodes.OK, new ListLicenseDefinitionsResponse
            {
                BoardTemplateId = body.BoardTemplateId,
                Definitions = definitions.Select(MapDefinitionToResponse).ToList()
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing license definitions for template {BoardTemplateId}", body.BoardTemplateId);
            await _messageBus.TryPublishErrorAsync(
                "license", "ListLicenseDefinitions", "unexpected_exception", ex.Message,
                dependency: null, endpoint: "post:/license/definition/list",
                details: null, stack: ex.StackTrace, cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, LicenseDefinitionResponse?)> UpdateLicenseDefinitionAsync(
        UpdateLicenseDefinitionRequest body,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var lockHandle = await _lockProvider.LockAsync(
                StateStoreDefinitions.LicenseLock,
                BuildTemplateLockKey(body.BoardTemplateId),
                Guid.NewGuid().ToString(),
                _configuration.LockTimeoutSeconds,
                cancellationToken);

            if (!lockHandle.Success)
            {
                _logger.LogWarning("Failed to acquire lock for template {BoardTemplateId}", body.BoardTemplateId);
                return (StatusCodes.Conflict, null);
            }

            var definition = await DefinitionStore.GetAsync(
                BuildDefinitionKey(body.BoardTemplateId, body.Code),
                cancellationToken);

            if (definition == null)
            {
                return (StatusCodes.NotFound, null);
            }

            // Update mutable fields
            if (body.LpCost.HasValue) definition.LpCost = body.LpCost.Value;
            if (body.Prerequisites != null) definition.Prerequisites = body.Prerequisites.ToList();
            if (body.Description != null) definition.Description = body.Description;
            if (body.Metadata != null) definition.Metadata = MetadataHelper.ConvertToDictionary(body.Metadata);

            await DefinitionStore.SaveAsync(
                BuildDefinitionKey(body.BoardTemplateId, body.Code),
                definition,
                cancellationToken: cancellationToken);

            _logger.LogInformation("Updated license definition {Code} on template {BoardTemplateId}",
                body.Code, body.BoardTemplateId);

            return (StatusCodes.OK, MapDefinitionToResponse(definition));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating license definition {Code}", body.Code);
            await _messageBus.TryPublishErrorAsync(
                "license", "UpdateLicenseDefinition", "unexpected_exception", ex.Message,
                dependency: null, endpoint: "post:/license/definition/update",
                details: null, stack: ex.StackTrace, cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, LicenseDefinitionResponse?)> RemoveLicenseDefinitionAsync(
        RemoveLicenseDefinitionRequest body,
        CancellationToken cancellationToken)
    {
        try
        {
            // Acquire template lock for multi-instance safety (IMPLEMENTATION TENETS)
            await using var lockHandle = await _lockProvider.LockAsync(
                StateStoreDefinitions.LicenseLock,
                BuildTemplateLockKey(body.BoardTemplateId),
                Guid.NewGuid().ToString(),
                _configuration.LockTimeoutSeconds,
                cancellationToken);

            if (!lockHandle.Success)
            {
                _logger.LogWarning("Failed to acquire lock for board template {BoardTemplateId}", body.BoardTemplateId);
                return (StatusCodes.Conflict, null);
            }

            var definition = await DefinitionStore.GetAsync(
                BuildDefinitionKey(body.BoardTemplateId, body.Code),
                cancellationToken);

            if (definition == null)
            {
                return (StatusCodes.NotFound, null);
            }

            // Check if any board instances have this license unlocked
            var boardInstances = await BoardStore.QueryAsync(
                b => b.BoardTemplateId == body.BoardTemplateId,
                cancellationToken: cancellationToken);

            // Load all definitions for cache rebuild support (authoritative inventory fallback)
            var allDefinitions = await DefinitionStore.QueryAsync(
                d => d.BoardTemplateId == body.BoardTemplateId,
                cancellationToken: cancellationToken);

            foreach (var board in boardInstances)
            {
                var cache = await LoadOrRebuildBoardCacheAsync(board, allDefinitions, cancellationToken);

                if (cache.UnlockedPositions.Any(u => u.Code == body.Code))
                {
                    _logger.LogWarning(
                        "Cannot remove definition {Code}: unlocked on board {BoardId}",
                        body.Code, board.BoardId);
                    return (StatusCodes.Conflict, null);
                }
            }

            await DefinitionStore.DeleteAsync(
                BuildDefinitionKey(body.BoardTemplateId, body.Code),
                cancellationToken);

            _logger.LogInformation("Removed license definition {Code} from template {BoardTemplateId}",
                body.Code, body.BoardTemplateId);

            return (StatusCodes.OK, MapDefinitionToResponse(definition));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing license definition {Code}", body.Code);
            await _messageBus.TryPublishErrorAsync(
                "license", "RemoveLicenseDefinition", "unexpected_exception", ex.Message,
                dependency: null, endpoint: "post:/license/definition/remove",
                details: null, stack: ex.StackTrace, cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    #endregion

    #region Board Instance Management

    /// <inheritdoc/>
    public async Task<(StatusCodes, BoardResponse?)> CreateBoardAsync(
        CreateBoardRequest body,
        CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation(
                "Creating board for owner {OwnerType}:{OwnerId} from template {BoardTemplateId}",
                body.OwnerType, body.OwnerId, body.BoardTemplateId);

            // Validate ownerType format (no key separator characters)
            if (!IsValidOwnerType(body.OwnerType))
            {
                _logger.LogWarning("Invalid owner type format: {OwnerType}", body.OwnerType);
                return (StatusCodes.BadRequest, null);
            }

            // Validate board template exists and is active
            var template = await BoardTemplateStore.GetAsync(
                BuildTemplateKey(body.BoardTemplateId),
                cancellationToken);

            if (template == null)
            {
                _logger.LogWarning("Board template {BoardTemplateId} not found", body.BoardTemplateId);
                return (StatusCodes.NotFound, null);
            }

            if (!template.IsActive)
            {
                _logger.LogWarning("Board template {BoardTemplateId} is not active", body.BoardTemplateId);
                return (StatusCodes.BadRequest, null);
            }

            // Validate ownerType is in template's allowedOwnerTypes
            if (!template.AllowedOwnerTypes.Contains(body.OwnerType))
            {
                _logger.LogWarning(
                    "Owner type {OwnerType} not in template's allowedOwnerTypes [{Allowed}]",
                    body.OwnerType, string.Join(", ", template.AllowedOwnerTypes));
                return (StatusCodes.BadRequest, null);
            }

            // Map ownerType to ContainerOwnerType (guaranteed to succeed due to template-time validation)
            var containerOwnerType = MapToContainerOwnerType(body.OwnerType)
                ?? throw new InvalidOperationException($"Owner type {body.OwnerType} passed template validation but has no ContainerOwnerType mapping");

            // Validate game service matches
            if (template.GameServiceId != body.GameServiceId)
            {
                _logger.LogWarning(
                    "Game service mismatch: template belongs to {TemplateGameServiceId} but request specified {RequestGameServiceId}",
                    template.GameServiceId, body.GameServiceId);
                return (StatusCodes.BadRequest, null);
            }

            // Owner-type-aware validation and realm context resolution
            Guid? resolvedRealmId = body.RealmId;

            if (body.OwnerType == "character")
            {
                // For character owners: validate character exists and resolve realm
                try
                {
                    var character = await _characterClient.GetCharacterAsync(
                        new GetCharacterRequest { CharacterId = body.OwnerId },
                        cancellationToken);

                    if (body.RealmId.HasValue && body.RealmId.Value != character.RealmId)
                    {
                        _logger.LogWarning(
                            "RealmId {RealmId} does not match character's realm {CharacterRealmId}",
                            body.RealmId, character.RealmId);
                        return (StatusCodes.BadRequest, null);
                    }

                    // Use character's realm if not explicitly provided
                    resolvedRealmId = character.RealmId;
                }
                catch (ApiException ex) when (ex.StatusCode == 404)
                {
                    _logger.LogWarning("Character {OwnerId} not found", body.OwnerId);
                    return (StatusCodes.NotFound, null);
                }
            }

            // Enforce one board per template per owner
            var existingBoard = await BoardStore.GetAsync(
                BuildBoardByOwnerKey(body.OwnerType, body.OwnerId, body.BoardTemplateId),
                cancellationToken);

            if (existingBoard != null)
            {
                _logger.LogWarning(
                    "Owner {OwnerType}:{OwnerId} already has a board for template {BoardTemplateId}",
                    body.OwnerType, body.OwnerId, body.BoardTemplateId);
                return (StatusCodes.Conflict, null);
            }

            // Enforce MaxBoardsPerOwner
            var ownerBoards = await BoardStore.QueryAsync(
                b => b.OwnerType == body.OwnerType && b.OwnerId == body.OwnerId,
                cancellationToken: cancellationToken);

            if (ownerBoards.Count >= _configuration.MaxBoardsPerOwner)
            {
                _logger.LogWarning(
                    "Owner {OwnerType}:{OwnerId} has reached max boards limit of {Max}",
                    body.OwnerType, body.OwnerId, _configuration.MaxBoardsPerOwner);
                return (StatusCodes.Conflict, null);
            }

            // Create inventory container (slot_only, maxSlots = gridWidth * gridHeight)
            ContainerResponse containerResponse;
            try
            {
                containerResponse = await _inventoryClient.CreateContainerAsync(
                    new CreateContainerRequest
                    {
                        OwnerId = body.OwnerId,
                        OwnerType = containerOwnerType,
                        ContainerType = "license_board",
                        ConstraintModel = ContainerConstraintModel.SlotOnly,
                        MaxSlots = template.GridWidth * template.GridHeight
                    },
                    cancellationToken);
            }
            catch (ApiException ex)
            {
                _logger.LogError(ex, "Failed to create inventory container for board (owner {OwnerType}:{OwnerId})", body.OwnerType, body.OwnerId);
                await _messageBus.TryPublishErrorAsync(
                    "license", "CreateBoard", "container_creation_failed", ex.Message,
                    dependency: "inventory", endpoint: "post:/license/board/create",
                    details: $"OwnerType: {body.OwnerType}, OwnerId: {body.OwnerId}", stack: ex.StackTrace,
                    cancellationToken: cancellationToken);
                return (StatusCodes.InternalServerError, null);
            }

            var now = DateTimeOffset.UtcNow;
            var board = new BoardInstanceModel
            {
                BoardId = Guid.NewGuid(),
                OwnerType = body.OwnerType,
                OwnerId = body.OwnerId,
                RealmId = resolvedRealmId,
                BoardTemplateId = body.BoardTemplateId,
                GameServiceId = body.GameServiceId,
                ContainerId = containerResponse.ContainerId,
                CreatedAt = now
            };

            // Save board instance
            await BoardStore.SaveAsync(BuildBoardKey(board.BoardId), board, cancellationToken: cancellationToken);

            // Save uniqueness key (owner + template)
            await BoardStore.SaveAsync(
                BuildBoardByOwnerKey(board.OwnerType, board.OwnerId, board.BoardTemplateId),
                board,
                cancellationToken: cancellationToken);

            // Initialize empty board cache
            await BoardCache.SaveAsync(
                BuildBoardCacheKey(board.BoardId),
                new BoardCacheModel
                {
                    BoardId = board.BoardId,
                    UnlockedPositions = new List<UnlockedLicenseEntry>(),
                    LastUpdated = now
                },
                new StateOptions { Ttl = _configuration.BoardCacheTtlSeconds },
                cancellationToken);

            // Register resource reference with lib-resource for cleanup coordination
            // Day-one: only character owners get reference tracking
            if (board.OwnerType == "character")
            {
                await RegisterCharacterReferenceAsync(
                    board.BoardId.ToString(),
                    board.OwnerId,
                    cancellationToken);
            }

            _logger.LogInformation(
                "Created board {BoardId} for owner {OwnerType}:{OwnerId} with container {ContainerId}",
                board.BoardId, board.OwnerType, board.OwnerId, board.ContainerId);

            await _messageBus.TryPublishAsync(
                "license-board.created",
                new LicenseBoardCreatedEvent
                {
                    BoardId = board.BoardId,
                    OwnerType = board.OwnerType,
                    OwnerId = board.OwnerId,
                    RealmId = board.RealmId,
                    BoardTemplateId = board.BoardTemplateId,
                    GameServiceId = board.GameServiceId,
                    ContainerId = board.ContainerId,
                    CreatedAt = board.CreatedAt
                },
                cancellationToken: cancellationToken);

            return (StatusCodes.OK, MapBoardToResponse(board));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating board for owner {OwnerType}:{OwnerId}", body.OwnerType, body.OwnerId);
            await _messageBus.TryPublishErrorAsync(
                "license", "CreateBoard", "unexpected_exception", ex.Message,
                dependency: null, endpoint: "post:/license/board/create",
                details: null, stack: ex.StackTrace, cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, BoardResponse?)> GetBoardAsync(
        GetBoardRequest body,
        CancellationToken cancellationToken)
    {
        try
        {
            var board = await BoardStore.GetAsync(BuildBoardKey(body.BoardId), cancellationToken);

            if (board == null)
            {
                return (StatusCodes.NotFound, null);
            }

            return (StatusCodes.OK, MapBoardToResponse(board));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting board {BoardId}", body.BoardId);
            await _messageBus.TryPublishErrorAsync(
                "license", "GetBoard", "unexpected_exception", ex.Message,
                dependency: null, endpoint: "post:/license/board/get",
                details: null, stack: ex.StackTrace, cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, ListBoardsByOwnerResponse?)> ListBoardsByOwnerAsync(
        ListBoardsByOwnerRequest body,
        CancellationToken cancellationToken)
    {
        try
        {
            IReadOnlyList<BoardInstanceModel> boards;

            if (body.GameServiceId.HasValue)
            {
                boards = await BoardStore.QueryAsync(
                    b => b.OwnerType == body.OwnerType && b.OwnerId == body.OwnerId && b.GameServiceId == body.GameServiceId.Value,
                    cancellationToken: cancellationToken);
            }
            else
            {
                boards = await BoardStore.QueryAsync(
                    b => b.OwnerType == body.OwnerType && b.OwnerId == body.OwnerId,
                    cancellationToken: cancellationToken);
            }

            return (StatusCodes.OK, new ListBoardsByOwnerResponse
            {
                Boards = boards.Select(MapBoardToResponse).ToList()
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing boards for owner {OwnerType}:{OwnerId}", body.OwnerType, body.OwnerId);
            await _messageBus.TryPublishErrorAsync(
                "license", "ListBoardsByOwner", "unexpected_exception", ex.Message,
                dependency: null, endpoint: "post:/license/board/list-by-owner",
                details: null, stack: ex.StackTrace, cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, BoardResponse?)> DeleteBoardAsync(
        DeleteBoardRequest body,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var lockHandle = await _lockProvider.LockAsync(
                StateStoreDefinitions.LicenseLock,
                BuildBoardLockKey(body.BoardId),
                Guid.NewGuid().ToString(),
                _configuration.LockTimeoutSeconds,
                cancellationToken);

            if (!lockHandle.Success)
            {
                _logger.LogWarning("Failed to acquire lock for board {BoardId}", body.BoardId);
                return (StatusCodes.Conflict, null);
            }

            var board = await BoardStore.GetAsync(BuildBoardKey(body.BoardId), cancellationToken);

            if (board == null)
            {
                return (StatusCodes.NotFound, null);
            }

            // Unregister resource reference before deletion (day-one: character only)
            if (board.OwnerType == "character")
            {
                await UnregisterCharacterReferenceAsync(
                    board.BoardId.ToString(),
                    board.OwnerId,
                    cancellationToken);
            }

            // Delete inventory container (destroys all contained items)
            try
            {
                await _inventoryClient.DeleteContainerAsync(
                    new DeleteContainerRequest { ContainerId = board.ContainerId },
                    cancellationToken);
            }
            catch (ApiException ex) when (ex.StatusCode == 404)
            {
                _logger.LogWarning("Container {ContainerId} already deleted", board.ContainerId);
            }

            // Delete board records
            await BoardStore.DeleteAsync(BuildBoardKey(board.BoardId), cancellationToken);
            await BoardStore.DeleteAsync(
                BuildBoardByOwnerKey(board.OwnerType, board.OwnerId, board.BoardTemplateId),
                cancellationToken);

            // Invalidate cache
            await BoardCache.DeleteAsync(BuildBoardCacheKey(board.BoardId), cancellationToken);

            _logger.LogInformation("Deleted board {BoardId} for owner {OwnerType}:{OwnerId}",
                board.BoardId, board.OwnerType, board.OwnerId);

            await _messageBus.TryPublishAsync(
                "license-board.deleted",
                new LicenseBoardDeletedEvent
                {
                    BoardId = board.BoardId,
                    OwnerType = board.OwnerType,
                    OwnerId = board.OwnerId,
                    RealmId = board.RealmId,
                    BoardTemplateId = board.BoardTemplateId,
                    GameServiceId = board.GameServiceId,
                    ContainerId = board.ContainerId,
                    CreatedAt = board.CreatedAt
                },
                cancellationToken: cancellationToken);

            return (StatusCodes.OK, MapBoardToResponse(board));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting board {BoardId}", body.BoardId);
            await _messageBus.TryPublishErrorAsync(
                "license", "DeleteBoard", "unexpected_exception", ex.Message,
                dependency: null, endpoint: "post:/license/board/delete",
                details: null, stack: ex.StackTrace, cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    #endregion

    #region Gameplay Operations

    /// <inheritdoc/>
    public async Task<(StatusCodes, UnlockLicenseResponse?)> UnlockLicenseAsync(
        UnlockLicenseRequest body,
        CancellationToken cancellationToken)
    {
        try
        {
            // 1. Acquire distributed lock on board
            await using var lockHandle = await _lockProvider.LockAsync(
                StateStoreDefinitions.LicenseLock,
                BuildBoardLockKey(body.BoardId),
                Guid.NewGuid().ToString(),
                _configuration.LockTimeoutSeconds,
                cancellationToken);

            if (!lockHandle.Success)
            {
                _logger.LogWarning("Failed to acquire lock for board {BoardId}", body.BoardId);
                return (StatusCodes.Conflict, null);
            }

            // 2. Load board instance
            var board = await BoardStore.GetAsync(BuildBoardKey(body.BoardId), cancellationToken);
            if (board == null)
            {
                _logger.LogWarning("Board {BoardId} not found for unlock attempt", body.BoardId);
                return (StatusCodes.NotFound, null);
            }

            // 3. Load board template
            var template = await BoardTemplateStore.GetAsync(
                BuildTemplateKey(board.BoardTemplateId), cancellationToken);
            if (template == null)
            {
                _logger.LogError("Board template {BoardTemplateId} missing for board {BoardId} - data integrity error",
                    board.BoardTemplateId, body.BoardId);
                return (StatusCodes.InternalServerError, null);
            }

            // 4. Load license definition
            var definition = await DefinitionStore.GetAsync(
                BuildDefinitionKey(board.BoardTemplateId, body.LicenseCode), cancellationToken);
            if (definition == null)
            {
                await PublishUnlockFailedAsync(body.BoardId, board.OwnerType, board.OwnerId, body.LicenseCode,
                    UnlockFailureReason.LicenseNotFound, cancellationToken);
                return (StatusCodes.NotFound, null);
            }

            // 5. Load all definitions for adjacency/prerequisite checks and cache rebuild
            var allDefinitions = await DefinitionStore.QueryAsync(
                d => d.BoardTemplateId == board.BoardTemplateId,
                cancellationToken: cancellationToken);

            // 5-6. Load board cache (with inventory fallback as authoritative source)
            var cache = await LoadOrRebuildBoardCacheAsync(board, allDefinitions, cancellationToken);

            var alreadyUnlocked = cache.UnlockedPositions.Any(u => u.Code == body.LicenseCode);
            if (alreadyUnlocked)
            {
                await PublishUnlockFailedAsync(body.BoardId, board.OwnerType, board.OwnerId, body.LicenseCode,
                    UnlockFailureReason.AlreadyUnlocked, cancellationToken);
                return (StatusCodes.Conflict, null);
            }

            // 7. Validate adjacency
            var isStartingNode = template.StartingNodes.Any(
                sn => sn.X == definition.PositionX && sn.Y == definition.PositionY);

            if (!isStartingNode)
            {
                var hasAdjacentUnlocked = cache.UnlockedPositions.Any(u =>
                    IsAdjacent(u.PositionX, u.PositionY, definition.PositionX, definition.PositionY, template.AdjacencyMode));

                if (!hasAdjacentUnlocked)
                {
                    await PublishUnlockFailedAsync(body.BoardId, board.OwnerType, board.OwnerId, body.LicenseCode,
                        UnlockFailureReason.NotAdjacent, cancellationToken);
                    return (StatusCodes.BadRequest, null);
                }
            }

            // 8. Validate non-adjacent prerequisites
            if (definition.Prerequisites is { Count: > 0 })
            {
                var unlockedCodes = new HashSet<string>(cache.UnlockedPositions.Select(u => u.Code));
                var missingPrereqs = definition.Prerequisites.Where(p => !unlockedCodes.Contains(p)).ToList();

                if (missingPrereqs.Count > 0)
                {
                    _logger.LogInformation(
                        "Prerequisites not met for {Code} on board {BoardId}: missing {Missing}",
                        body.LicenseCode, body.BoardId, string.Join(", ", missingPrereqs));
                    await PublishUnlockFailedAsync(body.BoardId, board.OwnerType, board.OwnerId, body.LicenseCode,
                        UnlockFailureReason.PrerequisitesNotMet, cancellationToken);
                    return (StatusCodes.BadRequest, null);
                }
            }

            // 9. Validate realm context is available for item creation
            if (!board.RealmId.HasValue)
            {
                _logger.LogError("Board {BoardId} has no realm context — cannot create item instances", body.BoardId);
                await PublishUnlockFailedAsync(body.BoardId, board.OwnerType, board.OwnerId, body.LicenseCode,
                    UnlockFailureReason.ContractFailed, cancellationToken);
                return (StatusCodes.BadRequest, null);
            }

            // 9b. Create item instance FIRST (easily reversible via DestroyItemInstance)
            // Realm context stored on board at creation time — no character load needed.
            // Saga ordering: item creation before contract completion ensures the worst failure
            // mode (LP deducted, no item) cannot occur. If contract fails after item creation,
            // we compensate by destroying the item — owner loses nothing either way.
            ItemInstanceResponse itemInstance;
            try
            {
                itemInstance = await _itemClient.CreateItemInstanceAsync(
                    new CreateItemInstanceRequest
                    {
                        TemplateId = definition.ItemTemplateId,
                        ContainerId = board.ContainerId,
                        RealmId = board.RealmId.Value,
                        Quantity = 1
                    },
                    cancellationToken);
            }
            catch (ApiException ex)
            {
                _logger.LogError(ex, "Item creation failed for unlock of {Code} on board {BoardId} (status {StatusCode})",
                    body.LicenseCode, body.BoardId, ex.StatusCode);
                await PublishUnlockFailedAsync(body.BoardId, board.OwnerType, board.OwnerId, body.LicenseCode,
                    UnlockFailureReason.ContractFailed, cancellationToken);
                return (StatusCodes.InternalServerError, null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error creating item for unlock of {Code} on board {BoardId}",
                    body.LicenseCode, body.BoardId);
                await PublishUnlockFailedAsync(body.BoardId, board.OwnerType, board.OwnerId, body.LicenseCode,
                    UnlockFailureReason.ContractFailed, cancellationToken);
                return (StatusCodes.InternalServerError, null);
            }

            // 10. Contract lifecycle — LP deduction via prebound API execution
            // If this fails, compensate by destroying the item created in step 9
            var licenseeEntityType = MapToEntityType(board.OwnerType) ?? EntityType.Custom;

            Guid contractInstanceId;
            try
            {
                var contractInstance = await _contractClient.CreateContractInstanceAsync(
                    new CreateContractInstanceRequest
                    {
                        TemplateId = template.BoardContractTemplateId,
                        Parties = new List<ContractPartyInput>
                        {
                            new ContractPartyInput
                            {
                                EntityId = board.OwnerId,
                                EntityType = licenseeEntityType,
                                Role = "licensee"
                            },
                            new ContractPartyInput
                            {
                                EntityId = board.GameServiceId,
                                EntityType = EntityType.System,
                                Role = "licensor"
                            }
                        }
                    },
                    cancellationToken);
                contractInstanceId = contractInstance.ContractId;

                // 10b. Set template values for prebound API execution
                var templateValues = new Dictionary<string, string>
                {
                    ["ownerType"] = board.OwnerType,
                    ["ownerId"] = board.OwnerId.ToString(),
                    ["boardId"] = body.BoardId.ToString(),
                    ["lpCost"] = definition.LpCost.ToString(),
                    ["licenseCode"] = body.LicenseCode,
                    ["itemTemplateId"] = definition.ItemTemplateId.ToString(),
                    ["gameServiceId"] = board.GameServiceId.ToString()
                };

                // Add definition metadata values as template values (skip null values)
                if (definition.Metadata != null)
                {
                    foreach (var kvp in definition.Metadata)
                    {
                        var value = kvp.Value?.ToString();
                        if (value != null)
                        {
                            templateValues[kvp.Key] = value;
                        }
                    }
                }

                await _contractClient.SetContractTemplateValuesAsync(
                    new SetTemplateValuesRequest
                    {
                        ContractInstanceId = contractInstanceId,
                        TemplateValues = templateValues
                    },
                    cancellationToken);

                // 10c. Auto-propose the contract
                await _contractClient.ProposeContractInstanceAsync(
                    new ProposeContractInstanceRequest { ContractId = contractInstanceId },
                    cancellationToken);

                // 10d. Auto-consent both parties
                await _contractClient.ConsentToContractAsync(
                    new ConsentToContractRequest
                    {
                        ContractId = contractInstanceId,
                        PartyEntityId = board.OwnerId,
                        PartyEntityType = licenseeEntityType
                    },
                    cancellationToken);

                await _contractClient.ConsentToContractAsync(
                    new ConsentToContractRequest
                    {
                        ContractId = contractInstanceId,
                        PartyEntityId = board.GameServiceId,
                        PartyEntityType = EntityType.System
                    },
                    cancellationToken);

                // 10e. Complete the "unlock" milestone (triggers LP deduction via prebound API)
                await _contractClient.CompleteMilestoneAsync(
                    new CompleteMilestoneRequest
                    {
                        ContractId = contractInstanceId,
                        MilestoneCode = "unlock",
                        Evidence = new { boardId = body.BoardId, licenseCode = body.LicenseCode }
                    },
                    cancellationToken);
            }
            catch (ApiException ex)
            {
                _logger.LogError(ex, "Contract execution failed for unlock of {Code} on board {BoardId} (status {StatusCode}), compensating by destroying item {ItemInstanceId}",
                    body.LicenseCode, body.BoardId, ex.StatusCode, itemInstance.InstanceId);
                await CompensateItemCreationAsync(itemInstance.InstanceId, body.BoardId, body.LicenseCode, cancellationToken);
                await PublishUnlockFailedAsync(body.BoardId, board.OwnerType, board.OwnerId, body.LicenseCode,
                    UnlockFailureReason.ContractFailed, cancellationToken);
                return (StatusCodes.InternalServerError, null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Contract execution failed for unlock of {Code} on board {BoardId}, compensating by destroying item {ItemInstanceId}",
                    body.LicenseCode, body.BoardId, itemInstance.InstanceId);
                await CompensateItemCreationAsync(itemInstance.InstanceId, body.BoardId, body.LicenseCode, cancellationToken);
                await PublishUnlockFailedAsync(body.BoardId, board.OwnerType, board.OwnerId, body.LicenseCode,
                    UnlockFailureReason.ContractFailed, cancellationToken);
                return (StatusCodes.InternalServerError, null);
            }

            // 12. Update board cache with optimistic concurrency retry
            var cacheKey = BuildBoardCacheKey(body.BoardId);
            for (var attempt = 0; attempt <= _configuration.MaxConcurrencyRetries; attempt++)
            {
                var (currentCache, etag) = await BoardCache.GetWithETagAsync(cacheKey, cancellationToken);
                currentCache ??= new BoardCacheModel { BoardId = body.BoardId, UnlockedPositions = new List<UnlockedLicenseEntry>() };

                currentCache.UnlockedPositions.Add(new UnlockedLicenseEntry
                {
                    Code = body.LicenseCode,
                    PositionX = definition.PositionX,
                    PositionY = definition.PositionY,
                    ItemInstanceId = itemInstance.InstanceId,
                    UnlockedAt = DateTimeOffset.UtcNow
                });
                currentCache.LastUpdated = DateTimeOffset.UtcNow;

                if (etag == null)
                {
                    // No existing cache entry - create new with TTL
                    await BoardCache.SaveAsync(cacheKey, currentCache,
                        new StateOptions { Ttl = _configuration.BoardCacheTtlSeconds },
                        cancellationToken);
                    break;
                }

                var newEtag = await BoardCache.TrySaveAsync(cacheKey, currentCache, etag, cancellationToken);
                if (newEtag != null)
                {
                    break;
                }

                if (attempt == _configuration.MaxConcurrencyRetries)
                {
                    _logger.LogWarning("Board cache concurrency conflict after {Attempts} retries for board {BoardId}",
                        _configuration.MaxConcurrencyRetries, body.BoardId);
                    return (StatusCodes.Conflict, null);
                }

                _logger.LogDebug("Board cache concurrency conflict on attempt {Attempt} for board {BoardId}, retrying",
                    attempt + 1, body.BoardId);
            }

            // 12. Publish license.unlocked event
            await _messageBus.TryPublishAsync(
                LicenseTopics.LicenseUnlocked,
                new LicenseUnlockedEvent
                {
                    EventId = Guid.NewGuid(),
                    Timestamp = DateTimeOffset.UtcNow,
                    BoardId = body.BoardId,
                    OwnerType = board.OwnerType,
                    OwnerId = board.OwnerId,
                    GameServiceId = board.GameServiceId,
                    LicenseCode = body.LicenseCode,
                    Position = new GridPosition { X = definition.PositionX, Y = definition.PositionY },
                    ItemInstanceId = itemInstance.InstanceId,
                    ContractInstanceId = contractInstanceId,
                    LpCost = definition.LpCost
                },
                cancellationToken: cancellationToken);

            _logger.LogInformation(
                "Unlocked license {Code} at ({X}, {Y}) on board {BoardId} for owner {OwnerType}:{OwnerId}",
                body.LicenseCode, definition.PositionX, definition.PositionY, body.BoardId, board.OwnerType, board.OwnerId);

            // 13. Return success
            return (StatusCodes.OK, new UnlockLicenseResponse
            {
                BoardId = body.BoardId,
                LicenseCode = body.LicenseCode,
                Position = new GridPosition { X = definition.PositionX, Y = definition.PositionY },
                ItemInstanceId = itemInstance.InstanceId,
                ContractInstanceId = contractInstanceId
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error unlocking license {LicenseCode} on board {BoardId}",
                body.LicenseCode, body.BoardId);
            await _messageBus.TryPublishErrorAsync(
                "license", "UnlockLicense", "unexpected_exception", ex.Message,
                dependency: null, endpoint: "post:/license/unlock",
                details: null, stack: ex.StackTrace, cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Compensates for a failed contract by destroying the item created during the unlock flow.
    /// Called when contract execution fails after item creation succeeded (saga compensation).
    /// </summary>
    private async Task CompensateItemCreationAsync(
        Guid itemInstanceId, Guid boardId, string licenseCode, CancellationToken cancellationToken)
    {
        try
        {
            await _itemClient.DestroyItemInstanceAsync(
                new DestroyItemInstanceRequest { InstanceId = itemInstanceId },
                cancellationToken);
            _logger.LogInformation(
                "Compensation successful: destroyed item {ItemInstanceId} after contract failure for {Code} on board {BoardId}",
                itemInstanceId, licenseCode, boardId);
        }
        catch (Exception compensationEx)
        {
            // Compensation failure is serious but must not mask the original error.
            // The orphaned item will be cleaned up when the board is deleted or manually reconciled.
            _logger.LogError(compensationEx,
                "Compensation FAILED: could not destroy item {ItemInstanceId} after contract failure for {Code} on board {BoardId}. Orphaned item requires manual cleanup",
                itemInstanceId, licenseCode, boardId);
            await _messageBus.TryPublishErrorAsync(
                "license", "CompensateItemCreation", "compensation_failed",
                $"Failed to destroy orphaned item {itemInstanceId} after contract failure",
                dependency: "item", endpoint: "post:/item/instance/destroy",
                details: null, stack: compensationEx.StackTrace, cancellationToken: cancellationToken);
        }
    }

    /// <summary>
    /// Publishes a license.unlock-failed event.
    /// </summary>
    private async Task PublishUnlockFailedAsync(
        Guid boardId, string ownerType, Guid ownerId, string licenseCode,
        UnlockFailureReason reason, CancellationToken cancellationToken)
    {
        await _messageBus.TryPublishAsync(
            LicenseTopics.LicenseUnlockFailed,
            new LicenseUnlockFailedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                BoardId = boardId,
                OwnerType = ownerType,
                OwnerId = ownerId,
                LicenseCode = licenseCode,
                Reason = reason
            },
            cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, CheckUnlockableResponse?)> CheckUnlockableAsync(
        CheckUnlockableRequest body,
        CancellationToken cancellationToken)
    {
        try
        {
            // Load board instance
            var board = await BoardStore.GetAsync(BuildBoardKey(body.BoardId), cancellationToken);
            if (board == null)
            {
                return (StatusCodes.NotFound, null);
            }

            // Load template
            var template = await BoardTemplateStore.GetAsync(
                BuildTemplateKey(board.BoardTemplateId), cancellationToken);
            if (template == null)
            {
                return (StatusCodes.InternalServerError, null);
            }

            // Load definition
            var definition = await DefinitionStore.GetAsync(
                BuildDefinitionKey(board.BoardTemplateId, body.LicenseCode), cancellationToken);
            if (definition == null)
            {
                return (StatusCodes.NotFound, null);
            }

            // Load all definitions for cache rebuild if needed
            var allDefinitions = await DefinitionStore.QueryAsync(
                d => d.BoardTemplateId == board.BoardTemplateId,
                cancellationToken: cancellationToken);

            // Load board cache (with inventory fallback as authoritative source)
            var cache = await LoadOrRebuildBoardCacheAsync(board, allDefinitions, cancellationToken);

            // Check adjacency
            var isStartingNode = template.StartingNodes.Any(
                sn => sn.X == definition.PositionX && sn.Y == definition.PositionY);
            var adjacencyMet = isStartingNode || cache.UnlockedPositions.Any(u =>
                IsAdjacent(u.PositionX, u.PositionY, definition.PositionX, definition.PositionY, template.AdjacencyMode));

            // Check prerequisites
            var prerequisitesMet = true;
            if (definition.Prerequisites is { Count: > 0 })
            {
                var unlockedCodes = new HashSet<string>(cache.UnlockedPositions.Select(u => u.Code));
                prerequisitesMet = definition.Prerequisites.All(p => unlockedCodes.Contains(p));
            }

            // Check LP balance via currency client (owner-type-aware)
            double? currentLp = null;
            bool? lpSufficient = null;

            // LP check is best-effort and advisory only.
            // Actual LP deduction happens in the contract milestone execution (not here).
            var walletOwnerType = MapToWalletOwnerType(board.OwnerType);
            if (walletOwnerType.HasValue)
            {
                try
                {
                    var walletResponse = await _currencyClient.GetOrCreateWalletAsync(
                        new GetOrCreateWalletRequest
                        {
                            OwnerId = board.OwnerId,
                            OwnerType = walletOwnerType.Value
                        },
                        cancellationToken);

                    // Sum all balances as a simple LP approximation
                    // The contract template defines which specific currency to deduct
                    var totalBalance = walletResponse.Balances.Sum(b => b.Amount);
                    currentLp = totalBalance;
                    lpSufficient = totalBalance >= definition.LpCost;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to check LP balance for owner {OwnerType}:{OwnerId}", board.OwnerType, board.OwnerId);
                    lpSufficient = false;
                }
            }

            return (StatusCodes.OK, new CheckUnlockableResponse
            {
                Unlockable = adjacencyMet && prerequisitesMet && (lpSufficient ?? true),
                AdjacencyMet = adjacencyMet,
                PrerequisitesMet = prerequisitesMet,
                LpSufficient = lpSufficient,
                CurrentLp = currentLp,
                RequiredLp = definition.LpCost
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking unlockable for {LicenseCode} on board {BoardId}",
                body.LicenseCode, body.BoardId);
            await _messageBus.TryPublishErrorAsync(
                "license", "CheckUnlockable", "unexpected_exception", ex.Message,
                dependency: null, endpoint: "post:/license/check-unlockable",
                details: null, stack: ex.StackTrace, cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, BoardStateResponse?)> GetBoardStateAsync(
        BoardStateRequest body,
        CancellationToken cancellationToken)
    {
        try
        {
            // Load board instance
            var board = await BoardStore.GetAsync(BuildBoardKey(body.BoardId), cancellationToken);
            if (board == null)
            {
                return (StatusCodes.NotFound, null);
            }

            // Load template
            var template = await BoardTemplateStore.GetAsync(
                BuildTemplateKey(board.BoardTemplateId), cancellationToken);
            if (template == null)
            {
                return (StatusCodes.InternalServerError, null);
            }

            // Load all definitions for this template
            var definitions = await DefinitionStore.QueryAsync(
                d => d.BoardTemplateId == board.BoardTemplateId,
                cancellationToken: cancellationToken);

            // Load board cache (with inventory fallback as authoritative source)
            var cache = await LoadOrRebuildBoardCacheAsync(board, definitions, cancellationToken);

            var unlockedLookup = cache.UnlockedPositions.ToDictionary(u => u.Code);

            // Compute status for each node
            var nodes = new List<BoardNodeState>();
            foreach (var def in definitions)
            {
                LicenseStatus status;
                Guid? itemInstanceId = null;

                if (unlockedLookup.TryGetValue(def.Code, out var unlockedEntry))
                {
                    status = LicenseStatus.Unlocked;
                    itemInstanceId = unlockedEntry.ItemInstanceId;
                }
                else
                {
                    // Check if unlockable (adjacent or starting node)
                    var isStartingNode = template.StartingNodes.Any(
                        sn => sn.X == def.PositionX && sn.Y == def.PositionY);
                    var hasAdjacentUnlocked = cache.UnlockedPositions.Any(u =>
                        IsAdjacent(u.PositionX, u.PositionY, def.PositionX, def.PositionY, template.AdjacencyMode));

                    status = (isStartingNode || hasAdjacentUnlocked) ? LicenseStatus.Unlockable : LicenseStatus.Locked;
                }

                nodes.Add(new BoardNodeState
                {
                    Code = def.Code,
                    Position = new GridPosition { X = def.PositionX, Y = def.PositionY },
                    LpCost = def.LpCost,
                    Status = status,
                    ItemTemplateId = def.ItemTemplateId,
                    ItemInstanceId = itemInstanceId,
                    Prerequisites = def.Prerequisites?.ToList(),
                    Description = def.Description,
                    Metadata = def.Metadata
                });
            }

            return (StatusCodes.OK, new BoardStateResponse
            {
                BoardId = body.BoardId,
                OwnerType = board.OwnerType,
                OwnerId = board.OwnerId,
                BoardTemplateId = board.BoardTemplateId,
                GridWidth = template.GridWidth,
                GridHeight = template.GridHeight,
                Nodes = nodes
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting board state for {BoardId}", body.BoardId);
            await _messageBus.TryPublishErrorAsync(
                "license", "GetBoardState", "unexpected_exception", ex.Message,
                dependency: null, endpoint: "post:/license/board-state",
                details: null, stack: ex.StackTrace, cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, SeedBoardTemplateResponse?)> SeedBoardTemplateAsync(
        SeedBoardTemplateRequest body,
        CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation(
                "Seeding board template {BoardTemplateId} with {Count} definitions",
                body.BoardTemplateId, body.Definitions.Count);

            // Acquire template lock for multi-instance safety (IMPLEMENTATION TENETS)
            await using var lockHandle = await _lockProvider.LockAsync(
                StateStoreDefinitions.LicenseLock,
                BuildTemplateLockKey(body.BoardTemplateId),
                Guid.NewGuid().ToString(),
                _configuration.LockTimeoutSeconds,
                cancellationToken);

            if (!lockHandle.Success)
            {
                _logger.LogWarning("Failed to acquire lock for board template {BoardTemplateId}", body.BoardTemplateId);
                return (StatusCodes.Conflict, null);
            }

            // Validate board template exists
            var template = await BoardTemplateStore.GetAsync(
                BuildTemplateKey(body.BoardTemplateId), cancellationToken);
            if (template == null)
            {
                return (StatusCodes.NotFound, null);
            }

            // Load existing definitions for duplicate and limit checks
            var existingDefs = await DefinitionStore.QueryAsync(
                d => d.BoardTemplateId == body.BoardTemplateId,
                cancellationToken: cancellationToken);

            var existingCodes = new HashSet<string>(existingDefs.Select(d => d.Code));
            var existingPositions = new HashSet<(int, int)>(existingDefs.Select(d => (d.PositionX, d.PositionY)));

            // Check MaxDefinitionsPerBoard against total (existing + new)
            if (existingDefs.Count + body.Definitions.Count > _configuration.MaxDefinitionsPerBoard)
            {
                _logger.LogWarning(
                    "Seed would exceed max definitions limit of {Max}: {Existing} existing + {New} new for template {BoardTemplateId}",
                    _configuration.MaxDefinitionsPerBoard, existingDefs.Count, body.Definitions.Count, body.BoardTemplateId);
                return (StatusCodes.Conflict, null);
            }

            // Track codes and positions within the seed batch for intra-batch duplicate detection
            var batchCodes = new HashSet<string>();
            var batchPositions = new HashSet<(int, int)>();

            var created = new List<LicenseDefinitionResponse>();
            var skipped = new List<string>();

            foreach (var defRequest in body.Definitions)
            {
                // Validate position within grid bounds
                if (defRequest.Position.X < 0 || defRequest.Position.X >= template.GridWidth ||
                    defRequest.Position.Y < 0 || defRequest.Position.Y >= template.GridHeight)
                {
                    _logger.LogWarning(
                        "Seed: position ({X}, {Y}) out of bounds for definition {Code}",
                        defRequest.Position.X, defRequest.Position.Y, defRequest.Code);
                    skipped.Add(defRequest.Code);
                    continue;
                }

                // Check for duplicate code against existing definitions and current batch
                if (existingCodes.Contains(defRequest.Code) || !batchCodes.Add(defRequest.Code))
                {
                    _logger.LogWarning("Seed: duplicate code {Code} on board template {BoardTemplateId}",
                        defRequest.Code, body.BoardTemplateId);
                    skipped.Add(defRequest.Code);
                    continue;
                }

                // Check for duplicate position against existing definitions and current batch
                var position = (defRequest.Position.X, defRequest.Position.Y);
                if (existingPositions.Contains(position) || !batchPositions.Add(position))
                {
                    _logger.LogWarning(
                        "Seed: duplicate position ({X}, {Y}) for definition {Code} on board template {BoardTemplateId}",
                        defRequest.Position.X, defRequest.Position.Y, defRequest.Code, body.BoardTemplateId);
                    skipped.Add(defRequest.Code);
                    continue;
                }

                // Validate item template exists
                try
                {
                    await _itemClient.GetItemTemplateAsync(
                        new GetItemTemplateRequest { TemplateId = defRequest.ItemTemplateId },
                        cancellationToken);
                }
                catch (ApiException ex) when (ex.StatusCode == 404)
                {
                    _logger.LogWarning("Seed: item template {ItemTemplateId} not found for definition {Code}",
                        defRequest.ItemTemplateId, defRequest.Code);
                    skipped.Add(defRequest.Code);
                    continue;
                }

                var now = DateTimeOffset.UtcNow;
                var definition = new LicenseDefinitionModel
                {
                    LicenseDefinitionId = Guid.NewGuid(),
                    BoardTemplateId = body.BoardTemplateId,
                    Code = defRequest.Code,
                    PositionX = defRequest.Position.X,
                    PositionY = defRequest.Position.Y,
                    LpCost = defRequest.LpCost,
                    ItemTemplateId = defRequest.ItemTemplateId,
                    Prerequisites = defRequest.Prerequisites?.ToList(),
                    Description = defRequest.Description,
                    Metadata = MetadataHelper.ConvertToDictionary(defRequest.Metadata),
                    CreatedAt = now
                };

                await DefinitionStore.SaveAsync(
                    BuildDefinitionKey(body.BoardTemplateId, defRequest.Code),
                    definition,
                    cancellationToken: cancellationToken);

                created.Add(MapDefinitionToResponse(definition));
            }

            if (skipped.Count > 0)
            {
                _logger.LogInformation(
                    "Seed skipped {SkippedCount} definitions for template {BoardTemplateId}: {SkippedCodes}",
                    skipped.Count, body.BoardTemplateId, string.Join(", ", skipped));
            }

            _logger.LogInformation(
                "Seeded {Count} definitions for board template {BoardTemplateId}",
                created.Count, body.BoardTemplateId);

            return (StatusCodes.OK, new SeedBoardTemplateResponse
            {
                BoardTemplateId = body.BoardTemplateId,
                DefinitionsCreated = created.Count,
                Definitions = created
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error seeding board template {BoardTemplateId}", body.BoardTemplateId);
            await _messageBus.TryPublishErrorAsync(
                "license", "SeedBoardTemplate", "unexpected_exception", ex.Message,
                dependency: null, endpoint: "post:/license/board-template/seed",
                details: null, stack: ex.StackTrace, cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    #endregion

    #region Cleanup Operations

    /// <inheritdoc/>
    public async Task<(StatusCodes, CleanupByOwnerResponse?)> CleanupByOwnerAsync(
        CleanupByOwnerRequest body,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Cleaning up license boards for deleted owner {OwnerType}:{OwnerId}", body.OwnerType, body.OwnerId);

        var boardsDeleted = 0;

        try
        {
            var boards = await BoardStore.QueryAsync(
                b => b.OwnerType == body.OwnerType && b.OwnerId == body.OwnerId,
                cancellationToken: cancellationToken);

            if (boards.Count == 0)
            {
                _logger.LogDebug("No license boards found for owner {OwnerType}:{OwnerId}", body.OwnerType, body.OwnerId);
                return (StatusCodes.OK, new CleanupByOwnerResponse
                {
                    OwnerType = body.OwnerType,
                    OwnerId = body.OwnerId,
                    BoardsDeleted = 0
                });
            }

            _logger.LogInformation(
                "Found {BoardCount} license boards to clean up for owner {OwnerType}:{OwnerId}",
                boards.Count, body.OwnerType, body.OwnerId);

            foreach (var board in boards)
            {
                try
                {
                    // Delete the inventory container (destroys all contained license items)
                    await _inventoryClient.DeleteContainerAsync(
                        new DeleteContainerRequest { ContainerId = board.ContainerId },
                        cancellationToken);
                }
                catch (ApiException ex) when (ex.StatusCode == 404)
                {
                    _logger.LogWarning("Container {ContainerId} already deleted for board {BoardId}",
                        board.ContainerId, board.BoardId);
                }

                // Delete the board instance record
                await BoardStore.DeleteAsync(
                    BuildBoardKey(board.BoardId),
                    cancellationToken);

                // Delete the owner-template uniqueness key
                await BoardStore.DeleteAsync(
                    BuildBoardByOwnerKey(board.OwnerType, board.OwnerId, board.BoardTemplateId),
                    cancellationToken);

                // Invalidate board cache
                await BoardCache.DeleteAsync(
                    BuildBoardCacheKey(board.BoardId),
                    cancellationToken);

                boardsDeleted++;

                _logger.LogDebug("Deleted board {BoardId} for owner {OwnerType}:{OwnerId}",
                    board.BoardId, body.OwnerType, body.OwnerId);
            }

            _logger.LogInformation(
                "Completed cleanup of {BoardsDeleted} license boards for owner {OwnerType}:{OwnerId}",
                boardsDeleted, body.OwnerType, body.OwnerId);

            return (StatusCodes.OK, new CleanupByOwnerResponse
            {
                OwnerType = body.OwnerType,
                OwnerId = body.OwnerId,
                BoardsDeleted = boardsDeleted
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cleaning up license boards for owner {OwnerType}:{OwnerId}", body.OwnerType, body.OwnerId);
            await _messageBus.TryPublishErrorAsync(
                "license",
                "CleanupByOwner",
                "cleanup_failed",
                ex.Message,
                dependency: null,
                endpoint: "post:/license/cleanup-by-owner",
                details: $"OwnerType: {body.OwnerType}, OwnerId: {body.OwnerId}, BoardsDeletedSoFar: {boardsDeleted}",
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    #endregion
}
