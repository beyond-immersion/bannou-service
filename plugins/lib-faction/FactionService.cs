using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.GameService;
using BeyondImmersion.BannouService.Location;
using BeyondImmersion.BannouService.Realm;
using BeyondImmersion.BannouService.Resource;
using BeyondImmersion.BannouService.Seed;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Faction;

/// <summary>
/// Faction management as seed-based living entities (L4 GameFeatures).
/// Factions own seeds whose growth unlocks governance capabilities (norm definition,
/// territory claiming, trade regulation). Primary norm data consumer is lib-obligation.
/// </summary>
[BannouService("faction", typeof(IFactionService), lifetime: ServiceLifetime.Scoped, layer: ServiceLayer.GameFeatures)]
public partial class FactionService : IFactionService
{
    private readonly IMessageBus _messageBus;
    private readonly IStateStoreFactory _stateStoreFactory;
    private readonly IResourceClient _resourceClient;
    private readonly ILogger<FactionService> _logger;
    private readonly FactionServiceConfiguration _configuration;
    private readonly ISeedClient _seedClient;
    private readonly ILocationClient _locationClient;
    private readonly IRealmClient _realmClient;
    private readonly IGameServiceClient _gameServiceClient;
    private readonly IDistributedLockProvider _lockProvider;

    // Faction store (MySQL)
    private readonly IStateStore<FactionModel> _factionStore;
    private readonly IJsonQueryableStateStore<FactionModel> _factionQueryStore;

    // Membership store (MySQL)
    private readonly IStateStore<FactionMemberModel> _memberStore;
    private readonly IStateStore<MembershipListModel> _memberListStore;

    // Territory store (MySQL)
    private readonly IStateStore<TerritoryClaimModel> _territoryStore;
    private readonly IStateStore<TerritoryClaimListModel> _territoryListStore;

    // Norm store (MySQL)
    private readonly IStateStore<NormDefinitionModel> _normStore;
    private readonly IStateStore<NormListModel> _normListStore;

    // Cache store (Redis)
    private readonly IStateStore<ResolvedNormCacheModel> _normCacheStore;

    /// <summary>
    /// Initializes a new instance of the FactionService.
    /// All L0/L1/L2 dependencies are hard (constructor-injected, fail at startup if missing).
    /// </summary>
    public FactionService(
        IMessageBus messageBus,
        IStateStoreFactory stateStoreFactory,
        IResourceClient resourceClient,
        ILogger<FactionService> logger,
        FactionServiceConfiguration configuration,
        ISeedClient seedClient,
        ILocationClient locationClient,
        IRealmClient realmClient,
        IGameServiceClient gameServiceClient,
        IDistributedLockProvider lockProvider)
    {
        _messageBus = messageBus;
        _stateStoreFactory = stateStoreFactory;
        _resourceClient = resourceClient;
        _logger = logger;
        _configuration = configuration;
        _seedClient = seedClient;
        _locationClient = locationClient;
        _realmClient = realmClient;
        _gameServiceClient = gameServiceClient;
        _lockProvider = lockProvider;

        _factionStore = stateStoreFactory.GetStore<FactionModel>(StateStoreDefinitions.Faction);
        _factionQueryStore = stateStoreFactory.GetJsonQueryableStore<FactionModel>(StateStoreDefinitions.Faction);
        _memberStore = stateStoreFactory.GetStore<FactionMemberModel>(StateStoreDefinitions.FactionMembership);
        _memberListStore = stateStoreFactory.GetStore<MembershipListModel>(StateStoreDefinitions.FactionMembership);
        _territoryStore = stateStoreFactory.GetStore<TerritoryClaimModel>(StateStoreDefinitions.FactionTerritory);
        _territoryListStore = stateStoreFactory.GetStore<TerritoryClaimListModel>(StateStoreDefinitions.FactionTerritory);
        _normStore = stateStoreFactory.GetStore<NormDefinitionModel>(StateStoreDefinitions.FactionNorm);
        _normListStore = stateStoreFactory.GetStore<NormListModel>(StateStoreDefinitions.FactionNorm);
        _normCacheStore = stateStoreFactory.GetStore<ResolvedNormCacheModel>(StateStoreDefinitions.FactionCache);
    }

    // ========================================================================
    // KEY BUILDERS
    // ========================================================================

    private static string FactionKey(Guid factionId) => $"fac:{factionId}";
    private static string FactionCodeKey(Guid gameServiceId, string code) => $"fac:{gameServiceId}:{code}";
    private static string MemberKey(Guid factionId, Guid characterId) => $"mem:{factionId}:{characterId}";
    private static string CharacterMembershipsKey(Guid characterId) => $"mem:char:{characterId}";
    private static string ClaimKey(Guid claimId) => $"tcl:{claimId}";
    private static string LocationClaimKey(Guid locationId) => $"tcl:loc:{locationId}";
    private static string FactionClaimsKey(Guid factionId) => $"tcl:fac:{factionId}";
    private static string NormKey(Guid normId) => $"nrm:{normId}";
    private static string FactionNormsKey(Guid factionId) => $"nrm:fac:{factionId}";
    private static string NormCacheKey(Guid characterId, Guid? locationId) =>
        $"ncache:{characterId}:{locationId?.ToString() ?? "none"}";

    // ========================================================================
    // MAPPING HELPERS
    // ========================================================================

    private static FactionResponse MapToResponse(FactionModel model) => new()
    {
        FactionId = model.FactionId,
        GameServiceId = model.GameServiceId,
        Name = model.Name,
        Code = model.Code,
        Description = model.Description,
        RealmId = model.RealmId,
        IsRealmBaseline = model.IsRealmBaseline,
        ParentFactionId = model.ParentFactionId,
        SeedId = model.SeedId,
        Status = model.Status,
        CurrentPhase = model.CurrentPhase,
        MemberCount = model.MemberCount,
        CreatedAt = model.CreatedAt,
        UpdatedAt = model.UpdatedAt,
    };

    private static FactionMemberResponse MapToMemberResponse(FactionMemberModel model) => new()
    {
        FactionId = model.FactionId,
        CharacterId = model.CharacterId,
        Role = model.Role,
        JoinedAt = model.JoinedAt,
        UpdatedAt = model.UpdatedAt,
    };

    private static TerritoryClaimResponse MapToClaimResponse(TerritoryClaimModel model) => new()
    {
        ClaimId = model.ClaimId,
        FactionId = model.FactionId,
        LocationId = model.LocationId,
        Status = model.Status,
        ClaimedAt = model.ClaimedAt,
        ReleasedAt = model.ReleasedAt,
    };

    private static NormDefinitionResponse MapToNormResponse(NormDefinitionModel model) => new()
    {
        NormId = model.NormId,
        FactionId = model.FactionId,
        ViolationType = model.ViolationType,
        BasePenalty = model.BasePenalty,
        Severity = model.Severity,
        Scope = model.Scope,
        Description = model.Description,
        CreatedAt = model.CreatedAt,
        UpdatedAt = model.UpdatedAt,
    };

    // ========================================================================
    // CAPABILITY HELPERS
    // ========================================================================

    /// <summary>
    /// Checks whether a faction's seed has a specific capability unlocked.
    /// </summary>
    private async Task<bool> HasCapabilityAsync(Guid? seedId, string capabilityCode, CancellationToken ct)
    {
        if (seedId == null) return false;

        try
        {
            var manifest = await _seedClient.GetCapabilityManifestAsync(
                new GetCapabilityManifestRequest { SeedId = seedId.Value }, ct);

            return manifest.Capabilities.Any(c => c.CapabilityCode == capabilityCode);
        }
        catch (ApiException ex)
        {
            _logger.LogWarning(ex, "Failed to check seed capability {Capability} for seed {SeedId}", capabilityCode, seedId);
            return false;
        }
    }

    /// <summary>
    /// Publishes a lifecycle created event for a faction.
    /// </summary>
    private async Task PublishCreatedEventAsync(FactionModel model, CancellationToken ct)
    {
        var evt = new FactionCreatedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            EventName = "faction.created",
            FactionId = model.FactionId,
            GameServiceId = model.GameServiceId,
            Name = model.Name,
            Code = model.Code,
            RealmId = model.RealmId,
            IsRealmBaseline = model.IsRealmBaseline,
            ParentFactionId = model.ParentFactionId,
            SeedId = model.SeedId,
            Status = model.Status,
            CurrentPhase = model.CurrentPhase,
            MemberCount = model.MemberCount,
            CreatedAt = model.CreatedAt,
            UpdatedAt = model.UpdatedAt,
        };
        await _messageBus.TryPublishAsync("faction.created", evt, cancellationToken: ct);
    }

    /// <summary>
    /// Publishes a lifecycle updated event for a faction.
    /// </summary>
    private async Task PublishUpdatedEventAsync(FactionModel model, ICollection<string> changedFields, CancellationToken ct)
    {
        var evt = new FactionUpdatedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            EventName = "faction.updated",
            FactionId = model.FactionId,
            GameServiceId = model.GameServiceId,
            Name = model.Name,
            Code = model.Code,
            RealmId = model.RealmId,
            IsRealmBaseline = model.IsRealmBaseline,
            ParentFactionId = model.ParentFactionId,
            SeedId = model.SeedId,
            Status = model.Status,
            CurrentPhase = model.CurrentPhase,
            MemberCount = model.MemberCount,
            CreatedAt = model.CreatedAt,
            UpdatedAt = model.UpdatedAt,
            ChangedFields = changedFields.ToList(),
        };
        await _messageBus.TryPublishAsync("faction.updated", evt, cancellationToken: ct);
    }

    // ========================================================================
    // NORM CACHE INVALIDATION
    // ========================================================================

    /// <summary>
    /// Invalidates norm resolution cache entries for all members of a faction.
    /// Called after mutations that affect norm resolution (norm CRUD, territory, baseline).
    /// </summary>
    /// <remarks>
    /// Cache keys are <c>ncache:{characterId}:{locationId}</c>. Since we cannot enumerate
    /// all location combinations per character, this deletes the known character-scoped entries
    /// by querying faction members. For location-specific invalidation, callers should use
    /// the <c>forceRefresh</c> parameter on QueryApplicableNorms. Remaining stale entries
    /// expire via TTL (configured by NormQueryCacheTtlSeconds).
    /// </remarks>
    private async Task InvalidateNormCacheForFactionAsync(Guid factionId, CancellationToken ct)
    {
        // Query members of this faction to invalidate their norm caches
        var memberConditions = new List<QueryCondition>
        {
            new QueryCondition { Path = "$.FactionId", Operator = QueryOperator.Exists },
            new QueryCondition { Path = "$.FactionId", Operator = QueryOperator.Equals, Value = factionId.ToString() },
        };
        var memberQuery = _stateStoreFactory.GetJsonQueryableStore<FactionMemberModel>(
            StateStoreDefinitions.FactionMembership);

        var offset = 0;
        var pageSize = _configuration.SeedBulkPageSize;
        bool hasMore;
        do
        {
            var members = await memberQuery.JsonQueryPagedAsync(
                memberConditions, offset, pageSize, cancellationToken: ct);

            foreach (var member in members.Items)
            {
                // Delete the generic cache key (no location)
                await _normCacheStore.DeleteAsync(NormCacheKey(member.Value.CharacterId, null), ct);
            }

            offset += pageSize;
            hasMore = members.HasMore;
        } while (hasMore);
    }

    /// <summary>
    /// Invalidates norm resolution cache entries for a specific character.
    /// Called after membership mutations (add/remove member).
    /// </summary>
    private async Task InvalidateNormCacheForCharacterAsync(Guid characterId, CancellationToken ct)
    {
        // Delete the generic cache key (no location); location-specific entries expire via TTL
        await _normCacheStore.DeleteAsync(NormCacheKey(characterId, null), ct);
    }

    // ========================================================================
    // FACTION CRUD
    // ========================================================================

    /// <summary>
    /// Creates a new faction with seed growth tracking.
    /// Validates game service and realm existence, creates a seed via lib-seed,
    /// and saves under both ID and code lookup keys.
    /// </summary>
    public async Task<(StatusCodes, FactionResponse?)> CreateFactionAsync(CreateFactionRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Creating faction {Code} for game service {GameServiceId} in realm {RealmId}",
            body.Code, body.GameServiceId, body.RealmId);

        // Validate game service exists
        try
        {
            await _gameServiceClient.GetServiceAsync(
                new GetServiceRequest { ServiceId = body.GameServiceId }, cancellationToken);
        }
        catch (ApiException ex)
        {
            _logger.LogWarning(ex, "Game service validation failed for {GameServiceId}", body.GameServiceId);
            return ((StatusCodes)ex.StatusCode, null);
        }

        // Validate realm exists
        try
        {
            await _realmClient.RealmExistsAsync(
                new RealmExistsRequest { RealmId = body.RealmId }, cancellationToken);
        }
        catch (ApiException ex)
        {
            _logger.LogWarning(ex, "Realm validation failed for {RealmId}", body.RealmId);
            return ((StatusCodes)ex.StatusCode, null);
        }

        // Check code uniqueness within game service scope
        var existing = await _factionStore.GetAsync(FactionCodeKey(body.GameServiceId, body.Code), cancellationToken);
        if (existing != null) return (StatusCodes.Conflict, null);

        // Validate parent faction if specified and check hierarchy depth
        if (body.ParentFactionId.HasValue)
        {
            var parent = await _factionStore.GetAsync(FactionKey(body.ParentFactionId.Value), cancellationToken);
            if (parent == null) return (StatusCodes.BadRequest, null);

            // Walk up the parent chain to check hierarchy depth
            int depth = 1;
            var current = parent;
            while (current.ParentFactionId.HasValue)
            {
                if (depth >= _configuration.MaxHierarchyDepth)
                {
                    _logger.LogWarning(
                        "Faction hierarchy depth {Depth} would exceed maximum {MaxDepth} for parent {ParentId}",
                        depth + 1, _configuration.MaxHierarchyDepth, body.ParentFactionId.Value);
                    return (StatusCodes.BadRequest, null);
                }
                current = await _factionStore.GetAsync(FactionKey(current.ParentFactionId.Value), cancellationToken);
                if (current == null) break;
                depth++;
            }
        }

        var now = DateTimeOffset.UtcNow;
        var factionId = Guid.NewGuid();

        // Create seed for faction growth tracking (L2 hard dependency - must succeed)
        Guid seedId;
        try
        {
            var seedResponse = await _seedClient.CreateSeedAsync(new CreateSeedRequest
            {
                OwnerId = factionId,
                OwnerType = "faction",
                SeedTypeCode = _configuration.SeedTypeCode,
                GameServiceId = body.GameServiceId,
                DisplayName = body.Name,
            }, cancellationToken);

            seedId = seedResponse.SeedId;
        }
        catch (ApiException ex)
        {
            _logger.LogWarning(ex, "Seed creation failed for faction {FactionId}", factionId);
            return ((StatusCodes)ex.StatusCode, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected failure during seed creation for faction {FactionId}", factionId);
            await _messageBus.TryPublishErrorAsync(
                "faction", "CreateFaction", ex.GetType().Name,
                ex.Message, dependency: "seed", endpoint: "post:/faction/create",
                details: null, stack: ex.StackTrace, cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }

        var model = new FactionModel
        {
            FactionId = factionId,
            GameServiceId = body.GameServiceId,
            Name = body.Name,
            Code = body.Code,
            Description = body.Description,
            RealmId = body.RealmId,
            IsRealmBaseline = false,
            ParentFactionId = body.ParentFactionId,
            SeedId = seedId,
            Status = FactionStatus.Active,
            CurrentPhase = null,
            MemberCount = 0,
            CreatedAt = now,
            UpdatedAt = now,
        };

        await _factionStore.SaveAsync(FactionKey(factionId), model, cancellationToken: cancellationToken);
        await _factionStore.SaveAsync(FactionCodeKey(body.GameServiceId, body.Code), model, cancellationToken: cancellationToken);

        await PublishCreatedEventAsync(model, cancellationToken);

        _logger.LogInformation("Created faction {FactionId} with code {Code}", factionId, body.Code);
        return (StatusCodes.OK, MapToResponse(model));
    }

    /// <summary>
    /// Gets a faction by its unique identifier.
    /// </summary>
    public async Task<(StatusCodes, FactionResponse?)> GetFactionAsync(GetFactionRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Getting faction {FactionId}", body.FactionId);

        var model = await _factionStore.GetAsync(FactionKey(body.FactionId), cancellationToken);
        if (model == null) return (StatusCodes.NotFound, null);

        return (StatusCodes.OK, MapToResponse(model));
    }

    /// <summary>
    /// Gets a faction by game service and code.
    /// </summary>
    public async Task<(StatusCodes, FactionResponse?)> GetFactionByCodeAsync(GetFactionByCodeRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Getting faction by code {Code} for game service {GameServiceId}", body.Code, body.GameServiceId);

        var model = await _factionStore.GetAsync(FactionCodeKey(body.GameServiceId, body.Code), cancellationToken);
        if (model == null) return (StatusCodes.NotFound, null);

        return (StatusCodes.OK, MapToResponse(model));
    }

    /// <summary>
    /// Lists factions with cursor-based pagination and optional filters.
    /// </summary>
    public async Task<(StatusCodes, ListFactionsResponse?)> ListFactionsAsync(ListFactionsRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Listing factions with filters");

        var conditions = new List<QueryCondition>
        {
            new QueryCondition { Path = "$.FactionId", Operator = QueryOperator.Exists },
        };

        if (body.GameServiceId.HasValue)
            conditions.Add(new QueryCondition { Path = "$.GameServiceId", Operator = QueryOperator.Equals, Value = body.GameServiceId.Value.ToString() });

        if (body.RealmId.HasValue)
            conditions.Add(new QueryCondition { Path = "$.RealmId", Operator = QueryOperator.Equals, Value = body.RealmId.Value.ToString() });

        if (body.Status.HasValue)
            conditions.Add(new QueryCondition { Path = "$.Status", Operator = QueryOperator.Equals, Value = body.Status.Value.ToString() });

        if (body.ParentFactionId.HasValue)
            conditions.Add(new QueryCondition { Path = "$.ParentFactionId", Operator = QueryOperator.Equals, Value = body.ParentFactionId.Value.ToString() });

        if (body.IsTopLevelOnly)
            conditions.Add(new QueryCondition { Path = "$.ParentFactionId", Operator = QueryOperator.NotExists });

        if (body.IsRealmBaseline.HasValue)
            conditions.Add(new QueryCondition { Path = "$.IsRealmBaseline", Operator = QueryOperator.Equals, Value = body.IsRealmBaseline.Value.ToString().ToLowerInvariant() });

        var offset = 0;
        if (!string.IsNullOrEmpty(body.Cursor) && int.TryParse(body.Cursor, out var parsedOffset))
        {
            offset = parsedOffset;
        }

        var result = await _factionQueryStore.JsonQueryPagedAsync(
            conditions, offset, body.PageSize, cancellationToken: cancellationToken);

        var factions = result.Items.Select(r => MapToResponse(r.Value)).ToList();

        return (StatusCodes.OK, new ListFactionsResponse
        {
            Factions = factions,
            NextCursor = result.HasMore ? (offset + body.PageSize).ToString() : null,
            HasMore = result.HasMore,
        });
    }

    /// <summary>
    /// Updates a faction's mutable fields. Acquires distributed lock.
    /// </summary>
    public async Task<(StatusCodes, FactionResponse?)> UpdateFactionAsync(UpdateFactionRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Updating faction {FactionId}", body.FactionId);

        await using var lockResponse = await _lockProvider.LockAsync(
            StateStoreDefinitions.FactionLock,
            resourceId: $"faction:{body.FactionId}",
            lockOwner: Guid.NewGuid().ToString(),
            expiryInSeconds: _configuration.DistributedLockTimeoutSeconds,
            cancellationToken: cancellationToken);
        if (!lockResponse.Success) return (StatusCodes.Conflict, null);

        var model = await _factionStore.GetAsync(FactionKey(body.FactionId), cancellationToken);
        if (model == null) return (StatusCodes.NotFound, null);

        var changedFields = new List<string>();
        var oldCode = model.Code;

        if (body.Name != null && body.Name != model.Name)
        {
            model.Name = body.Name;
            changedFields.Add("name");
        }

        if (body.Code != null && body.Code != model.Code)
        {
            // Check new code uniqueness
            var codeCheck = await _factionStore.GetAsync(FactionCodeKey(model.GameServiceId, body.Code), cancellationToken);
            if (codeCheck != null && codeCheck.FactionId != model.FactionId) return (StatusCodes.Conflict, null);
            model.Code = body.Code;
            changedFields.Add("code");
        }

        if (body.Description != null && body.Description != model.Description)
        {
            model.Description = body.Description;
            changedFields.Add("description");
        }

        if (changedFields.Count == 0) return (StatusCodes.OK, MapToResponse(model));

        model.UpdatedAt = DateTimeOffset.UtcNow;

        await _factionStore.SaveAsync(FactionKey(model.FactionId), model, cancellationToken: cancellationToken);
        await _factionStore.SaveAsync(FactionCodeKey(model.GameServiceId, model.Code), model, cancellationToken: cancellationToken);

        // If code changed, remove old code key
        if (changedFields.Contains("code"))
        {
            await _factionStore.DeleteAsync(FactionCodeKey(model.GameServiceId, oldCode), cancellationToken: cancellationToken);
        }

        await PublishUpdatedEventAsync(model, changedFields, cancellationToken);

        _logger.LogInformation("Updated faction {FactionId}, changed: {ChangedFields}", body.FactionId, changedFields);
        return (StatusCodes.OK, MapToResponse(model));
    }

    /// <summary>
    /// Deprecates a faction, preventing new member additions.
    /// </summary>
    public async Task<(StatusCodes, FactionResponse?)> DeprecateFactionAsync(DeprecateFactionRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Deprecating faction {FactionId}", body.FactionId);

        await using var lockResponse = await _lockProvider.LockAsync(
            StateStoreDefinitions.FactionLock,
            resourceId: $"faction:{body.FactionId}",
            lockOwner: Guid.NewGuid().ToString(),
            expiryInSeconds: _configuration.DistributedLockTimeoutSeconds,
            cancellationToken: cancellationToken);
        if (!lockResponse.Success) return (StatusCodes.Conflict, null);

        var model = await _factionStore.GetAsync(FactionKey(body.FactionId), cancellationToken);
        if (model == null) return (StatusCodes.NotFound, null);
        if (model.Status == FactionStatus.Deprecated) return (StatusCodes.OK, MapToResponse(model));

        model.Status = FactionStatus.Deprecated;
        model.UpdatedAt = DateTimeOffset.UtcNow;

        await _factionStore.SaveAsync(FactionKey(model.FactionId), model, cancellationToken: cancellationToken);
        await _factionStore.SaveAsync(FactionCodeKey(model.GameServiceId, model.Code), model, cancellationToken: cancellationToken);

        await PublishUpdatedEventAsync(model, new[] { "status" }, cancellationToken);

        _logger.LogInformation("Deprecated faction {FactionId}", body.FactionId);
        return (StatusCodes.OK, MapToResponse(model));
    }

    /// <summary>
    /// Restores a deprecated faction to active status.
    /// </summary>
    public async Task<(StatusCodes, FactionResponse?)> UndeprecateFactionAsync(UndeprecateFactionRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Undeprecating faction {FactionId}", body.FactionId);

        await using var lockResponse = await _lockProvider.LockAsync(
            StateStoreDefinitions.FactionLock,
            resourceId: $"faction:{body.FactionId}",
            lockOwner: Guid.NewGuid().ToString(),
            expiryInSeconds: _configuration.DistributedLockTimeoutSeconds,
            cancellationToken: cancellationToken);
        if (!lockResponse.Success) return (StatusCodes.Conflict, null);

        var model = await _factionStore.GetAsync(FactionKey(body.FactionId), cancellationToken);
        if (model == null) return (StatusCodes.NotFound, null);
        if (model.Status != FactionStatus.Deprecated) return (StatusCodes.BadRequest, null);

        model.Status = FactionStatus.Active;
        model.UpdatedAt = DateTimeOffset.UtcNow;

        await _factionStore.SaveAsync(FactionKey(model.FactionId), model, cancellationToken: cancellationToken);
        await _factionStore.SaveAsync(FactionCodeKey(model.GameServiceId, model.Code), model, cancellationToken: cancellationToken);

        await PublishUpdatedEventAsync(model, new[] { "status" }, cancellationToken);

        _logger.LogInformation("Undeprecated faction {FactionId}", body.FactionId);
        return (StatusCodes.OK, MapToResponse(model));
    }

    /// <summary>
    /// Deletes a faction, cascading member removals, territory releases, and norm deletions.
    /// </summary>
    public async Task<StatusCodes> DeleteFactionAsync(DeleteFactionRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Deleting faction {FactionId}", body.FactionId);

        await using var lockResponse = await _lockProvider.LockAsync(
            StateStoreDefinitions.FactionLock,
            resourceId: $"faction:{body.FactionId}",
            lockOwner: Guid.NewGuid().ToString(),
            expiryInSeconds: _configuration.DistributedLockTimeoutSeconds,
            cancellationToken: cancellationToken);
        if (!lockResponse.Success) return StatusCodes.Conflict;

        var model = await _factionStore.GetAsync(FactionKey(body.FactionId), cancellationToken);
        if (model == null) return StatusCodes.NotFound;

        // Cascade: remove all members (paginated to handle large factions)
        var memberConditions = new List<QueryCondition>
        {
            new QueryCondition { Path = "$.FactionId", Operator = QueryOperator.Exists },
            new QueryCondition { Path = "$.FactionId", Operator = QueryOperator.Equals, Value = body.FactionId.ToString() },
        };
        var memberQuery = _stateStoreFactory.GetJsonQueryableStore<FactionMemberModel>(StateStoreDefinitions.FactionMembership);
        var memberOffset = 0;
        bool memberHasMore;
        do
        {
            var members = await memberQuery.JsonQueryPagedAsync(
                memberConditions, memberOffset, _configuration.SeedBulkPageSize, cancellationToken: cancellationToken);
            foreach (var member in members.Items)
            {
                await RemoveMemberInternalAsync(member.Value.FactionId, member.Value.CharacterId, cancellationToken);
            }
            memberOffset += _configuration.SeedBulkPageSize;
            memberHasMore = members.HasMore;
        } while (memberHasMore);

        // Cascade: release all territory claims
        var claimList = await _territoryListStore.GetAsync(FactionClaimsKey(body.FactionId), cancellationToken);
        if (claimList != null)
        {
            foreach (var claimId in claimList.ClaimIds.ToList())
            {
                var claim = await _territoryStore.GetAsync(ClaimKey(claimId), cancellationToken);
                if (claim != null && claim.Status == TerritoryClaimStatus.Active)
                {
                    await ReleaseTerritoryInternalAsync(claim, cancellationToken);
                }
            }
            await _territoryListStore.DeleteAsync(FactionClaimsKey(body.FactionId), cancellationToken: cancellationToken);
        }

        // Cascade: delete all norms
        var normList = await _normListStore.GetAsync(FactionNormsKey(body.FactionId), cancellationToken);
        if (normList != null)
        {
            foreach (var normId in normList.NormIds.ToList())
            {
                await _normStore.DeleteAsync(NormKey(normId), cancellationToken: cancellationToken);
            }
            await _normListStore.DeleteAsync(FactionNormsKey(body.FactionId), cancellationToken: cancellationToken);
        }

        // Delete faction records
        await _factionStore.DeleteAsync(FactionKey(model.FactionId), cancellationToken: cancellationToken);
        await _factionStore.DeleteAsync(FactionCodeKey(model.GameServiceId, model.Code), cancellationToken: cancellationToken);

        var deletedEvt = new FactionDeletedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            EventName = "faction.deleted",
            FactionId = model.FactionId,
            GameServiceId = model.GameServiceId,
            Name = model.Name,
            Code = model.Code,
            RealmId = model.RealmId,
            IsRealmBaseline = model.IsRealmBaseline,
            ParentFactionId = model.ParentFactionId,
            SeedId = model.SeedId,
            Status = model.Status,
            CurrentPhase = model.CurrentPhase,
            MemberCount = model.MemberCount,
            CreatedAt = model.CreatedAt,
            UpdatedAt = model.UpdatedAt,
        };
        await _messageBus.TryPublishAsync("faction.deleted", deletedEvt, cancellationToken: cancellationToken);

        _logger.LogInformation("Deleted faction {FactionId}", body.FactionId);
        return StatusCodes.OK;
    }

    // ========================================================================
    // SEEDING
    // ========================================================================

    /// <summary>
    /// Bulk creates factions with two-pass parent resolution. Skips duplicates by code lookup key.
    /// </summary>
    public async Task<(StatusCodes, SeedFactionsResponse?)> SeedFactionsAsync(SeedFactionsRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Seeding {Count} factions for game service {GameServiceId}", body.Factions.Count, body.GameServiceId);

        int created = 0, skipped = 0, failed = 0;
        var errors = new List<string>();
        var createdFactions = new Dictionary<string, FactionModel>();

        // Pass 1: Create factions without parent references
        foreach (var def in body.Factions)
        {
            var existing = await _factionStore.GetAsync(FactionCodeKey(body.GameServiceId, def.Code), cancellationToken);
            if (existing != null)
            {
                skipped++;
                createdFactions[def.Code] = existing;
                continue;
            }

            var now = DateTimeOffset.UtcNow;
            var factionId = Guid.NewGuid();

            Guid seedId;
            try
            {
                var seedResponse = await _seedClient.CreateSeedAsync(new CreateSeedRequest
                {
                    OwnerId = factionId,
                    OwnerType = "faction",
                    SeedTypeCode = _configuration.SeedTypeCode,
                    GameServiceId = body.GameServiceId,
                    DisplayName = def.Name,
                }, cancellationToken);

                seedId = seedResponse.SeedId;
            }
            catch (ApiException ex)
            {
                _logger.LogWarning(ex, "Seed creation failed during faction seeding for {Code}", def.Code);
                errors.Add($"Seed creation failed for faction '{def.Code}': {ex.StatusCode}");
                failed++;
                continue;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected failure during seed creation for faction {Code}", def.Code);
                await _messageBus.TryPublishErrorAsync(
                    "faction", "SeedFactions", ex.GetType().Name,
                    ex.Message, dependency: "seed", endpoint: "post:/faction/seed",
                    details: null, stack: ex.StackTrace, cancellationToken: cancellationToken);
                errors.Add($"Unexpected seed creation failure for faction '{def.Code}'");
                failed++;
                continue;
            }

            var model = new FactionModel
            {
                FactionId = factionId,
                GameServiceId = body.GameServiceId,
                Name = def.Name,
                Code = def.Code,
                Description = def.Description,
                RealmId = def.RealmId,
                IsRealmBaseline = def.IsRealmBaseline,
                ParentFactionId = null,
                SeedId = seedId,
                Status = FactionStatus.Active,
                CurrentPhase = null,
                MemberCount = 0,
                CreatedAt = now,
                UpdatedAt = now,
            };

            await _factionStore.SaveAsync(FactionKey(factionId), model, cancellationToken: cancellationToken);
            await _factionStore.SaveAsync(FactionCodeKey(body.GameServiceId, def.Code), model, cancellationToken: cancellationToken);

            createdFactions[def.Code] = model;
            created++;

            await PublishCreatedEventAsync(model, cancellationToken);
        }

        // Pass 2: Resolve parent references
        foreach (var def in body.Factions.Where(d => d.ParentCode != null))
        {
            if (!createdFactions.TryGetValue(def.Code, out var child)) continue;

            if (def.ParentCode != null && createdFactions.TryGetValue(def.ParentCode, out var parent))
            {
                child.ParentFactionId = parent.FactionId;
                child.UpdatedAt = DateTimeOffset.UtcNow;
                await _factionStore.SaveAsync(FactionKey(child.FactionId), child, cancellationToken: cancellationToken);
                await _factionStore.SaveAsync(FactionCodeKey(body.GameServiceId, child.Code), child, cancellationToken: cancellationToken);
            }
            else
            {
                errors.Add($"Parent code '{def.ParentCode}' not found for faction '{def.Code}'");
                failed++;
            }
        }

        _logger.LogInformation("Seeded factions: {Created} created, {Skipped} skipped, {Failed} failed",
            created, skipped, failed);

        return (StatusCodes.OK, new SeedFactionsResponse
        {
            Created = created,
            Skipped = skipped,
            Failed = failed,
            Errors = errors.Count > 0 ? errors : null,
        });
    }

    // ========================================================================
    // REALM BASELINE
    // ========================================================================

    /// <summary>
    /// Designates a faction as the realm baseline cultural faction.
    /// Replaces previous baseline if one existed.
    /// </summary>
    public async Task<(StatusCodes, FactionResponse?)> DesignateRealmBaselineAsync(DesignateRealmBaselineRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Designating faction {FactionId} as realm baseline", body.FactionId);

        await using var lockResponse = await _lockProvider.LockAsync(
            StateStoreDefinitions.FactionLock,
            resourceId: $"faction:{body.FactionId}",
            lockOwner: Guid.NewGuid().ToString(),
            expiryInSeconds: _configuration.DistributedLockTimeoutSeconds,
            cancellationToken: cancellationToken);
        if (!lockResponse.Success) return (StatusCodes.Conflict, null);

        var model = await _factionStore.GetAsync(FactionKey(body.FactionId), cancellationToken);
        if (model == null) return (StatusCodes.NotFound, null);

        Guid? previousBaselineId = null;

        // Find and clear previous baseline for this realm
        var baselineConditions = new List<QueryCondition>
        {
            new QueryCondition { Path = "$.FactionId", Operator = QueryOperator.Exists },
            new QueryCondition { Path = "$.RealmId", Operator = QueryOperator.Equals, Value = model.RealmId.ToString() },
            new QueryCondition { Path = "$.IsRealmBaseline", Operator = QueryOperator.Equals, Value = "true" },
        };
        var results = await _factionQueryStore.JsonQueryPagedAsync(baselineConditions, 0, 10, cancellationToken: cancellationToken);
        foreach (var existing in results.Items)
        {
            if (existing.Value.FactionId != model.FactionId)
            {
                previousBaselineId = existing.Value.FactionId;
                existing.Value.IsRealmBaseline = false;
                existing.Value.UpdatedAt = DateTimeOffset.UtcNow;
                await _factionStore.SaveAsync(FactionKey(existing.Value.FactionId), existing.Value, cancellationToken: cancellationToken);
                await _factionStore.SaveAsync(FactionCodeKey(existing.Value.GameServiceId, existing.Value.Code), existing.Value, cancellationToken: cancellationToken);
            }
        }

        model.IsRealmBaseline = true;
        model.UpdatedAt = DateTimeOffset.UtcNow;

        await _factionStore.SaveAsync(FactionKey(model.FactionId), model, cancellationToken: cancellationToken);
        await _factionStore.SaveAsync(FactionCodeKey(model.GameServiceId, model.Code), model, cancellationToken: cancellationToken);

        var evt = new FactionRealmBaselineDesignatedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            FactionId = model.FactionId,
            RealmId = model.RealmId,
            PreviousBaselineFactionId = previousBaselineId,
        };
        await _messageBus.TryPublishAsync("faction.realm-baseline.designated", evt, cancellationToken: cancellationToken);
        await InvalidateNormCacheForFactionAsync(model.FactionId, cancellationToken);
        if (previousBaselineId.HasValue)
        {
            await InvalidateNormCacheForFactionAsync(previousBaselineId.Value, cancellationToken);
        }

        _logger.LogInformation("Designated faction {FactionId} as realm baseline for realm {RealmId}", body.FactionId, model.RealmId);
        return (StatusCodes.OK, MapToResponse(model));
    }

    /// <summary>
    /// Gets the realm baseline faction for a given realm.
    /// </summary>
    public async Task<(StatusCodes, FactionResponse?)> GetRealmBaselineAsync(GetRealmBaselineRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Getting realm baseline for realm {RealmId}", body.RealmId);

        var conditions = new List<QueryCondition>
        {
            new QueryCondition { Path = "$.FactionId", Operator = QueryOperator.Exists },
            new QueryCondition { Path = "$.RealmId", Operator = QueryOperator.Equals, Value = body.RealmId.ToString() },
            new QueryCondition { Path = "$.IsRealmBaseline", Operator = QueryOperator.Equals, Value = "true" },
        };
        var results = await _factionQueryStore.JsonQueryPagedAsync(conditions, 0, 1, cancellationToken: cancellationToken);

        if (results.Items.Count == 0) return (StatusCodes.NotFound, null);

        return (StatusCodes.OK, MapToResponse(results.Items[0].Value));
    }

    // ========================================================================
    // MEMBERSHIP MANAGEMENT
    // ========================================================================

    /// <summary>
    /// Adds a character as a member of a faction.
    /// </summary>
    public async Task<(StatusCodes, FactionMemberResponse?)> AddMemberAsync(AddMemberRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Adding member {CharacterId} to faction {FactionId}", body.CharacterId, body.FactionId);

        // Lock per faction (not per member pair) to serialize MemberCount updates
        // and prevent concurrent additions from racing on the denormalized count
        await using var lockResponse = await _lockProvider.LockAsync(
            StateStoreDefinitions.FactionLock,
            resourceId: $"faction-membership:{body.FactionId}",
            lockOwner: Guid.NewGuid().ToString(),
            expiryInSeconds: _configuration.DistributedLockTimeoutSeconds,
            cancellationToken: cancellationToken);
        if (!lockResponse.Success) return (StatusCodes.Conflict, null);

        var faction = await _factionStore.GetAsync(FactionKey(body.FactionId), cancellationToken);
        if (faction == null) return (StatusCodes.NotFound, null);
        if (faction.Status != FactionStatus.Active) return (StatusCodes.BadRequest, null);

        // Check for existing membership
        var existingMember = await _memberStore.GetAsync(MemberKey(body.FactionId, body.CharacterId), cancellationToken);
        if (existingMember != null) return (StatusCodes.Conflict, null);

        var now = DateTimeOffset.UtcNow;
        var role = body.Role ?? _configuration.DefaultMemberRole;

        var member = new FactionMemberModel
        {
            FactionId = body.FactionId,
            CharacterId = body.CharacterId,
            Role = role,
            JoinedAt = now,
            FactionName = faction.Name,
            FactionCode = faction.Code,
        };

        await _memberStore.SaveAsync(MemberKey(body.FactionId, body.CharacterId), member, cancellationToken: cancellationToken);

        // Update character's membership list
        var charList = await _memberListStore.GetAsync(CharacterMembershipsKey(body.CharacterId), cancellationToken)
            ?? new MembershipListModel { CharacterId = body.CharacterId };
        charList.Memberships.Add(new MembershipEntry
        {
            FactionId = body.FactionId,
            Role = role,
            JoinedAt = now,
        });
        await _memberListStore.SaveAsync(CharacterMembershipsKey(body.CharacterId), charList, cancellationToken: cancellationToken);

        // Update faction member count
        faction.MemberCount++;
        faction.UpdatedAt = now;
        await _factionStore.SaveAsync(FactionKey(faction.FactionId), faction, cancellationToken: cancellationToken);
        await _factionStore.SaveAsync(FactionCodeKey(faction.GameServiceId, faction.Code), faction, cancellationToken: cancellationToken);

        // Register reference with lib-resource for cleanup coordination
        try
        {
            await _resourceClient.RegisterReferenceAsync(new RegisterReferenceRequest
            {
                ResourceType = "character",
                ResourceId = body.CharacterId,
                SourceType = "faction",
                SourceId = body.FactionId.ToString(),
            }, cancellationToken);
        }
        catch (ApiException ex)
        {
            _logger.LogWarning(ex, "Failed to register resource reference for character {CharacterId}", body.CharacterId);
        }

        var evt = new FactionMemberAddedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = now,
            FactionId = body.FactionId,
            CharacterId = body.CharacterId,
            Role = role,
        };
        await _messageBus.TryPublishAsync("faction.member.added", evt, cancellationToken: cancellationToken);
        await InvalidateNormCacheForCharacterAsync(body.CharacterId, cancellationToken);

        _logger.LogInformation("Added member {CharacterId} to faction {FactionId} with role {Role}",
            body.CharacterId, body.FactionId, role);
        return (StatusCodes.OK, MapToMemberResponse(member));
    }

    /// <summary>
    /// Removes a character from a faction.
    /// </summary>
    public async Task<StatusCodes> RemoveMemberAsync(RemoveMemberRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Removing member {CharacterId} from faction {FactionId}", body.CharacterId, body.FactionId);

        // Lock per faction (not per member pair) to serialize MemberCount updates
        // and prevent concurrent removals from racing on the denormalized count
        await using var lockResponse = await _lockProvider.LockAsync(
            StateStoreDefinitions.FactionLock,
            resourceId: $"faction-membership:{body.FactionId}",
            lockOwner: Guid.NewGuid().ToString(),
            expiryInSeconds: _configuration.DistributedLockTimeoutSeconds,
            cancellationToken: cancellationToken);
        if (!lockResponse.Success) return StatusCodes.Conflict;

        return await RemoveMemberInternalAsync(body.FactionId, body.CharacterId, cancellationToken);
    }

    private async Task<StatusCodes> RemoveMemberInternalAsync(Guid factionId, Guid characterId, CancellationToken ct)
    {
        var member = await _memberStore.GetAsync(MemberKey(factionId, characterId), ct);
        if (member == null) return StatusCodes.NotFound;

        await _memberStore.DeleteAsync(MemberKey(factionId, characterId), cancellationToken: ct);

        // Unregister resource reference with lib-resource (mirrors RegisterReferenceAsync in AddMemberAsync)
        try
        {
            await _resourceClient.UnregisterReferenceAsync(new UnregisterReferenceRequest
            {
                ResourceType = "character",
                ResourceId = characterId,
                SourceType = "faction",
                SourceId = factionId.ToString(),
            }, ct);
        }
        catch (ApiException ex)
        {
            _logger.LogWarning(ex, "Failed to unregister resource reference for character {CharacterId}", characterId);
        }

        // Update character's membership list
        var charList = await _memberListStore.GetAsync(CharacterMembershipsKey(characterId), ct);
        if (charList != null)
        {
            charList.Memberships.RemoveAll(m => m.FactionId == factionId);
            await _memberListStore.SaveAsync(CharacterMembershipsKey(characterId), charList, cancellationToken: ct);
        }

        // Update faction member count
        var faction = await _factionStore.GetAsync(FactionKey(factionId), ct);
        if (faction != null)
        {
            faction.MemberCount = Math.Max(0, faction.MemberCount - 1);
            faction.UpdatedAt = DateTimeOffset.UtcNow;
            await _factionStore.SaveAsync(FactionKey(faction.FactionId), faction, cancellationToken: ct);
            await _factionStore.SaveAsync(FactionCodeKey(faction.GameServiceId, faction.Code), faction, cancellationToken: ct);
        }

        var evt = new FactionMemberRemovedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            FactionId = factionId,
            CharacterId = characterId,
        };
        await _messageBus.TryPublishAsync("faction.member.removed", evt, cancellationToken: ct);
        await InvalidateNormCacheForCharacterAsync(characterId, ct);

        _logger.LogInformation("Removed member {CharacterId} from faction {FactionId}", characterId, factionId);
        return StatusCodes.OK;
    }

    /// <summary>
    /// Lists members of a faction with optional role filter and pagination.
    /// </summary>
    public async Task<(StatusCodes, ListMembersResponse?)> ListMembersAsync(ListMembersRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Listing members for faction {FactionId}", body.FactionId);

        var conditions = new List<QueryCondition>
        {
            new QueryCondition { Path = "$.FactionId", Operator = QueryOperator.Exists },
            new QueryCondition { Path = "$.FactionId", Operator = QueryOperator.Equals, Value = body.FactionId.ToString() },
        };

        if (body.Role.HasValue)
            conditions.Add(new QueryCondition { Path = "$.Role", Operator = QueryOperator.Equals, Value = body.Role.Value.ToString() });

        var queryStore = _stateStoreFactory.GetJsonQueryableStore<FactionMemberModel>(StateStoreDefinitions.FactionMembership);
        var memberOffset = 0;
        if (!string.IsNullOrEmpty(body.Cursor) && int.TryParse(body.Cursor, out var parsedMemberOffset))
        {
            memberOffset = parsedMemberOffset;
        }
        var result = await queryStore.JsonQueryPagedAsync(conditions, memberOffset, body.PageSize, cancellationToken: cancellationToken);

        var members = result.Items.Select(r => MapToMemberResponse(r.Value)).ToList();

        return (StatusCodes.OK, new ListMembersResponse
        {
            Members = members,
            NextCursor = result.HasMore ? (memberOffset + body.PageSize).ToString() : null,
            HasMore = result.HasMore,
        });
    }

    /// <summary>
    /// Lists all factions a character belongs to.
    /// </summary>
    public async Task<(StatusCodes, ListMembershipsByCharacterResponse?)> ListMembershipsByCharacterAsync(
        ListMembershipsByCharacterRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Listing memberships for character {CharacterId}", body.CharacterId);

        var charList = await _memberListStore.GetAsync(CharacterMembershipsKey(body.CharacterId), cancellationToken);

        var entries = new List<CharacterMembershipEntry>();
        if (charList != null)
        {
            foreach (var membership in charList.Memberships)
            {
                var faction = await _factionStore.GetAsync(FactionKey(membership.FactionId), cancellationToken);
                if (faction == null) continue;

                if (body.GameServiceId.HasValue && faction.GameServiceId != body.GameServiceId.Value) continue;

                entries.Add(new CharacterMembershipEntry
                {
                    FactionId = membership.FactionId,
                    FactionName = faction.Name,
                    FactionCode = faction.Code,
                    Role = membership.Role,
                    JoinedAt = membership.JoinedAt,
                });
            }
        }

        return (StatusCodes.OK, new ListMembershipsByCharacterResponse
        {
            CharacterId = body.CharacterId,
            Memberships = entries,
        });
    }

    /// <summary>
    /// Updates a member's role within a faction.
    /// </summary>
    public async Task<(StatusCodes, FactionMemberResponse?)> UpdateMemberRoleAsync(UpdateMemberRoleRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Updating role for member {CharacterId} in faction {FactionId} to {Role}",
            body.CharacterId, body.FactionId, body.Role);

        await using var lockResponse = await _lockProvider.LockAsync(
            StateStoreDefinitions.FactionLock,
            resourceId: $"membership:{body.FactionId}:{body.CharacterId}",
            lockOwner: Guid.NewGuid().ToString(),
            expiryInSeconds: _configuration.DistributedLockTimeoutSeconds,
            cancellationToken: cancellationToken);
        if (!lockResponse.Success) return (StatusCodes.Conflict, null);

        var member = await _memberStore.GetAsync(MemberKey(body.FactionId, body.CharacterId), cancellationToken);
        if (member == null) return (StatusCodes.NotFound, null);

        var previousRole = member.Role;
        if (previousRole == body.Role) return (StatusCodes.OK, MapToMemberResponse(member));

        member.Role = body.Role;
        member.UpdatedAt = DateTimeOffset.UtcNow;

        await _memberStore.SaveAsync(MemberKey(body.FactionId, body.CharacterId), member, cancellationToken: cancellationToken);

        // Update character's membership list
        var charList = await _memberListStore.GetAsync(CharacterMembershipsKey(body.CharacterId), cancellationToken);
        if (charList != null)
        {
            var entry = charList.Memberships.Find(m => m.FactionId == body.FactionId);
            if (entry != null) entry.Role = body.Role;
            await _memberListStore.SaveAsync(CharacterMembershipsKey(body.CharacterId), charList, cancellationToken: cancellationToken);
        }

        var evt = new FactionMemberRoleChangedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            FactionId = body.FactionId,
            CharacterId = body.CharacterId,
            PreviousRole = previousRole,
            NewRole = body.Role,
        };
        await _messageBus.TryPublishAsync("faction.member.role-changed", evt, cancellationToken: cancellationToken);

        _logger.LogInformation("Updated member {CharacterId} role in faction {FactionId} from {PreviousRole} to {NewRole}",
            body.CharacterId, body.FactionId, previousRole, body.Role);
        return (StatusCodes.OK, MapToMemberResponse(member));
    }

    /// <summary>
    /// Quick membership check returning boolean and role.
    /// </summary>
    public async Task<(StatusCodes, CheckMembershipResponse?)> CheckMembershipAsync(CheckMembershipRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Checking membership for {CharacterId} in faction {FactionId}", body.CharacterId, body.FactionId);

        var member = await _memberStore.GetAsync(MemberKey(body.FactionId, body.CharacterId), cancellationToken);

        return (StatusCodes.OK, new CheckMembershipResponse
        {
            FactionId = body.FactionId,
            CharacterId = body.CharacterId,
            IsMember = member != null,
            Role = member?.Role,
        });
    }

    // ========================================================================
    // TERRITORY MANAGEMENT
    // ========================================================================

    /// <summary>
    /// Claims a location as faction territory. Requires territory.claim seed capability.
    /// One controlling faction per location.
    /// </summary>
    public async Task<(StatusCodes, TerritoryClaimResponse?)> ClaimTerritoryAsync(ClaimTerritoryRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Faction {FactionId} claiming territory at location {LocationId}", body.FactionId, body.LocationId);

        await using var lockResponse = await _lockProvider.LockAsync(
            StateStoreDefinitions.FactionLock,
            resourceId: $"territory:{body.LocationId}",
            lockOwner: Guid.NewGuid().ToString(),
            expiryInSeconds: _configuration.DistributedLockTimeoutSeconds,
            cancellationToken: cancellationToken);
        if (!lockResponse.Success) return (StatusCodes.Conflict, null);

        var faction = await _factionStore.GetAsync(FactionKey(body.FactionId), cancellationToken);
        if (faction == null) return (StatusCodes.NotFound, null);
        if (faction.Status != FactionStatus.Active) return (StatusCodes.BadRequest, null);

        // Check seed capability
        if (!await HasCapabilityAsync(faction.SeedId, "territory.claim", cancellationToken))
        {
            _logger.LogDebug("Faction {FactionId} lacks territory.claim capability", body.FactionId);
            return (StatusCodes.Forbidden, null);
        }

        // Validate location exists
        try
        {
            await _locationClient.LocationExistsAsync(
                new LocationExistsRequest { LocationId = body.LocationId }, cancellationToken);
        }
        catch (ApiException ex)
        {
            _logger.LogWarning(ex, "Location validation failed for {LocationId}", body.LocationId);
            return ((StatusCodes)ex.StatusCode, null);
        }

        // Check no existing active claim on this location
        var existingClaim = await _territoryStore.GetAsync(LocationClaimKey(body.LocationId), cancellationToken);
        if (existingClaim != null && existingClaim.Status == TerritoryClaimStatus.Active)
            return (StatusCodes.Conflict, null);

        // Check faction territory limit
        var claimList = await _territoryListStore.GetAsync(FactionClaimsKey(body.FactionId), cancellationToken)
            ?? new TerritoryClaimListModel { FactionId = body.FactionId };
        if (claimList.ClaimIds.Count >= _configuration.MaxTerritoriesPerFaction) return (StatusCodes.BadRequest, null);

        var now = DateTimeOffset.UtcNow;
        var claimId = Guid.NewGuid();

        var claim = new TerritoryClaimModel
        {
            ClaimId = claimId,
            FactionId = body.FactionId,
            LocationId = body.LocationId,
            Status = TerritoryClaimStatus.Active,
            ClaimedAt = now,
        };

        await _territoryStore.SaveAsync(ClaimKey(claimId), claim, cancellationToken: cancellationToken);
        await _territoryStore.SaveAsync(LocationClaimKey(body.LocationId), claim, cancellationToken: cancellationToken);

        claimList.ClaimIds.Add(claimId);
        await _territoryListStore.SaveAsync(FactionClaimsKey(body.FactionId), claimList, cancellationToken: cancellationToken);

        // Register reference with lib-resource
        try
        {
            await _resourceClient.RegisterReferenceAsync(new RegisterReferenceRequest
            {
                ResourceType = "location",
                ResourceId = body.LocationId,
                SourceType = "faction",
                SourceId = body.FactionId.ToString(),
            }, cancellationToken);
        }
        catch (ApiException ex)
        {
            _logger.LogWarning(ex, "Failed to register resource reference for location {LocationId}", body.LocationId);
        }

        var evt = new FactionTerritoryClaimedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = now,
            FactionId = body.FactionId,
            LocationId = body.LocationId,
            ClaimId = claimId,
        };
        await _messageBus.TryPublishAsync("faction.territory.claimed", evt, cancellationToken: cancellationToken);
        await InvalidateNormCacheForFactionAsync(body.FactionId, cancellationToken);

        _logger.LogInformation("Faction {FactionId} claimed territory at location {LocationId}", body.FactionId, body.LocationId);
        return (StatusCodes.OK, MapToClaimResponse(claim));
    }

    /// <summary>
    /// Releases a territory claim.
    /// </summary>
    public async Task<StatusCodes> ReleaseTerritoryAsync(ReleaseTerritoryRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Releasing territory claim {ClaimId}", body.ClaimId);

        var claim = await _territoryStore.GetAsync(ClaimKey(body.ClaimId), cancellationToken);
        if (claim == null) return StatusCodes.NotFound;

        await using var lockResponse = await _lockProvider.LockAsync(
            StateStoreDefinitions.FactionLock,
            resourceId: $"territory:{claim.LocationId}",
            lockOwner: Guid.NewGuid().ToString(),
            expiryInSeconds: _configuration.DistributedLockTimeoutSeconds,
            cancellationToken: cancellationToken);
        if (!lockResponse.Success) return StatusCodes.Conflict;

        return await ReleaseTerritoryInternalAsync(claim, cancellationToken);
    }

    private async Task<StatusCodes> ReleaseTerritoryInternalAsync(TerritoryClaimModel claim, CancellationToken ct)
    {
        claim.Status = TerritoryClaimStatus.Released;
        claim.ReleasedAt = DateTimeOffset.UtcNow;

        await _territoryStore.SaveAsync(ClaimKey(claim.ClaimId), claim, cancellationToken: ct);
        await _territoryStore.DeleteAsync(LocationClaimKey(claim.LocationId), cancellationToken: ct);

        // Unregister resource reference with lib-resource (mirrors RegisterReferenceAsync in ClaimTerritoryAsync)
        try
        {
            await _resourceClient.UnregisterReferenceAsync(new UnregisterReferenceRequest
            {
                ResourceType = "location",
                ResourceId = claim.LocationId,
                SourceType = "faction",
                SourceId = claim.FactionId.ToString(),
            }, ct);
        }
        catch (ApiException ex)
        {
            _logger.LogWarning(ex, "Failed to unregister resource reference for location {LocationId}", claim.LocationId);
        }

        // Update faction's claim list
        var claimList = await _territoryListStore.GetAsync(FactionClaimsKey(claim.FactionId), ct);
        if (claimList != null)
        {
            claimList.ClaimIds.Remove(claim.ClaimId);
            await _territoryListStore.SaveAsync(FactionClaimsKey(claim.FactionId), claimList, cancellationToken: ct);
        }

        var evt = new FactionTerritoryReleasedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            FactionId = claim.FactionId,
            LocationId = claim.LocationId,
            ClaimId = claim.ClaimId,
        };
        await _messageBus.TryPublishAsync("faction.territory.released", evt, cancellationToken: ct);
        await InvalidateNormCacheForFactionAsync(claim.FactionId, ct);

        _logger.LogInformation("Released territory claim {ClaimId} at location {LocationId}", claim.ClaimId, claim.LocationId);
        return StatusCodes.OK;
    }

    /// <summary>
    /// Lists territory claims for a faction with optional status filter and pagination.
    /// </summary>
    public async Task<(StatusCodes, ListTerritoryClaimsResponse?)> ListTerritoryClaimsAsync(ListTerritoryClaimsRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Listing territory claims for faction {FactionId}", body.FactionId);

        var conditions = new List<QueryCondition>
        {
            new QueryCondition { Path = "$.FactionId", Operator = QueryOperator.Exists },
            new QueryCondition { Path = "$.FactionId", Operator = QueryOperator.Equals, Value = body.FactionId.ToString() },
        };

        if (body.Status.HasValue)
            conditions.Add(new QueryCondition { Path = "$.Status", Operator = QueryOperator.Equals, Value = body.Status.Value.ToString() });

        var queryStore = _stateStoreFactory.GetJsonQueryableStore<TerritoryClaimModel>(StateStoreDefinitions.FactionTerritory);
        var claimOffset = 0;
        if (!string.IsNullOrEmpty(body.Cursor) && int.TryParse(body.Cursor, out var parsedClaimOffset))
        {
            claimOffset = parsedClaimOffset;
        }
        var result = await queryStore.JsonQueryPagedAsync(conditions, claimOffset, body.PageSize, cancellationToken: cancellationToken);

        var claims = result.Items.Select(r => MapToClaimResponse(r.Value)).ToList();

        return (StatusCodes.OK, new ListTerritoryClaimsResponse
        {
            Claims = claims,
            NextCursor = result.HasMore ? (claimOffset + body.PageSize).ToString() : null,
            HasMore = result.HasMore,
        });
    }

    /// <summary>
    /// Gets the faction controlling a location.
    /// </summary>
    public async Task<(StatusCodes, ControllingFactionResponse?)> GetControllingFactionAsync(GetControllingFactionRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Getting controlling faction for location {LocationId}", body.LocationId);

        var claim = await _territoryStore.GetAsync(LocationClaimKey(body.LocationId), cancellationToken);
        if (claim == null || claim.Status != TerritoryClaimStatus.Active) return (StatusCodes.NotFound, null);

        var faction = await _factionStore.GetAsync(FactionKey(claim.FactionId), cancellationToken);
        if (faction == null) return (StatusCodes.NotFound, null);

        return (StatusCodes.OK, new ControllingFactionResponse
        {
            LocationId = body.LocationId,
            Faction = MapToResponse(faction),
            ClaimId = claim.ClaimId,
            ClaimedAt = claim.ClaimedAt,
        });
    }

    // ========================================================================
    // NORM MANAGEMENT
    // ========================================================================

    /// <summary>
    /// Defines a behavioral norm for a faction. Requires norm.define seed capability.
    /// </summary>
    public async Task<(StatusCodes, NormDefinitionResponse?)> DefineNormAsync(DefineNormRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Defining norm for faction {FactionId}, violation type {ViolationType}",
            body.FactionId, body.ViolationType);

        await using var lockResponse = await _lockProvider.LockAsync(
            StateStoreDefinitions.FactionLock,
            resourceId: $"norm:{body.FactionId}",
            lockOwner: Guid.NewGuid().ToString(),
            expiryInSeconds: _configuration.DistributedLockTimeoutSeconds,
            cancellationToken: cancellationToken);
        if (!lockResponse.Success) return (StatusCodes.Conflict, null);

        var faction = await _factionStore.GetAsync(FactionKey(body.FactionId), cancellationToken);
        if (faction == null) return (StatusCodes.NotFound, null);
        if (faction.Status != FactionStatus.Active) return (StatusCodes.BadRequest, null);

        // Check seed capability
        if (!await HasCapabilityAsync(faction.SeedId, "norm.define", cancellationToken))
        {
            _logger.LogDebug("Faction {FactionId} lacks norm.define capability", body.FactionId);
            return (StatusCodes.Forbidden, null);
        }

        // Check norm limit
        var normList = await _normListStore.GetAsync(FactionNormsKey(body.FactionId), cancellationToken)
            ?? new NormListModel { FactionId = body.FactionId };
        if (normList.NormIds.Count >= _configuration.MaxNormsPerFaction) return (StatusCodes.BadRequest, null);

        var now = DateTimeOffset.UtcNow;
        var normId = Guid.NewGuid();

        var norm = new NormDefinitionModel
        {
            NormId = normId,
            FactionId = body.FactionId,
            ViolationType = body.ViolationType,
            BasePenalty = body.BasePenalty,
            Severity = body.Severity,
            Scope = body.Scope,
            Description = body.Description,
            CreatedAt = now,
        };

        await _normStore.SaveAsync(NormKey(normId), norm, cancellationToken: cancellationToken);

        normList.NormIds.Add(normId);
        await _normListStore.SaveAsync(FactionNormsKey(body.FactionId), normList, cancellationToken: cancellationToken);

        var evt = new FactionNormDefinedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = now,
            FactionId = body.FactionId,
            NormId = normId,
            ViolationType = body.ViolationType,
            BasePenalty = body.BasePenalty,
            Severity = body.Severity,
            Scope = body.Scope,
        };
        await _messageBus.TryPublishAsync("faction.norm.defined", evt, cancellationToken: cancellationToken);
        await InvalidateNormCacheForFactionAsync(body.FactionId, cancellationToken);

        _logger.LogInformation("Defined norm {NormId} for faction {FactionId}, violation type {ViolationType}",
            normId, body.FactionId, body.ViolationType);
        return (StatusCodes.OK, MapToNormResponse(norm));
    }

    /// <summary>
    /// Updates a norm definition with partial updates.
    /// </summary>
    public async Task<(StatusCodes, NormDefinitionResponse?)> UpdateNormAsync(UpdateNormRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Updating norm {NormId}", body.NormId);

        var norm = await _normStore.GetAsync(NormKey(body.NormId), cancellationToken);
        if (norm == null) return (StatusCodes.NotFound, null);

        await using var lockResponse = await _lockProvider.LockAsync(
            StateStoreDefinitions.FactionLock,
            resourceId: $"norm:{norm.FactionId}",
            lockOwner: Guid.NewGuid().ToString(),
            expiryInSeconds: _configuration.DistributedLockTimeoutSeconds,
            cancellationToken: cancellationToken);
        if (!lockResponse.Success) return (StatusCodes.Conflict, null);

        float? updatedPenalty = null;
        NormSeverity? updatedSeverity = null;
        NormScope? updatedScope = null;

        if (body.BasePenalty.HasValue && Math.Abs(body.BasePenalty.Value - norm.BasePenalty) > float.Epsilon)
        {
            norm.BasePenalty = body.BasePenalty.Value;
            updatedPenalty = body.BasePenalty;
        }

        if (body.Severity.HasValue && body.Severity.Value != norm.Severity)
        {
            norm.Severity = body.Severity.Value;
            updatedSeverity = body.Severity;
        }

        if (body.Scope.HasValue && body.Scope.Value != norm.Scope)
        {
            norm.Scope = body.Scope.Value;
            updatedScope = body.Scope;
        }

        if (body.Description != null && body.Description != norm.Description)
        {
            norm.Description = body.Description;
        }

        norm.UpdatedAt = DateTimeOffset.UtcNow;
        await _normStore.SaveAsync(NormKey(body.NormId), norm, cancellationToken: cancellationToken);

        var evt = new FactionNormUpdatedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            FactionId = norm.FactionId,
            NormId = body.NormId,
            ViolationType = norm.ViolationType,
            BasePenalty = updatedPenalty,
            Severity = updatedSeverity,
            Scope = updatedScope,
        };
        await _messageBus.TryPublishAsync("faction.norm.updated", evt, cancellationToken: cancellationToken);
        await InvalidateNormCacheForFactionAsync(norm.FactionId, cancellationToken);

        _logger.LogInformation("Updated norm {NormId}", body.NormId);
        return (StatusCodes.OK, MapToNormResponse(norm));
    }

    /// <summary>
    /// Deletes a norm definition.
    /// </summary>
    public async Task<StatusCodes> DeleteNormAsync(DeleteNormRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Deleting norm {NormId}", body.NormId);

        var norm = await _normStore.GetAsync(NormKey(body.NormId), cancellationToken);
        if (norm == null) return StatusCodes.NotFound;

        await using var lockResponse = await _lockProvider.LockAsync(
            StateStoreDefinitions.FactionLock,
            resourceId: $"norm:{norm.FactionId}",
            lockOwner: Guid.NewGuid().ToString(),
            expiryInSeconds: _configuration.DistributedLockTimeoutSeconds,
            cancellationToken: cancellationToken);
        if (!lockResponse.Success) return StatusCodes.Conflict;

        await _normStore.DeleteAsync(NormKey(body.NormId), cancellationToken: cancellationToken);

        var normList = await _normListStore.GetAsync(FactionNormsKey(norm.FactionId), cancellationToken);
        if (normList != null)
        {
            normList.NormIds.Remove(body.NormId);
            await _normListStore.SaveAsync(FactionNormsKey(norm.FactionId), normList, cancellationToken: cancellationToken);
        }

        var evt = new FactionNormDeletedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            FactionId = norm.FactionId,
            NormId = body.NormId,
            ViolationType = norm.ViolationType,
        };
        await _messageBus.TryPublishAsync("faction.norm.deleted", evt, cancellationToken: cancellationToken);
        await InvalidateNormCacheForFactionAsync(norm.FactionId, cancellationToken);

        _logger.LogInformation("Deleted norm {NormId}", body.NormId);
        return StatusCodes.OK;
    }

    /// <summary>
    /// Lists all norms for a faction with optional severity/scope filters.
    /// </summary>
    public async Task<(StatusCodes, ListNormsResponse?)> ListNormsAsync(ListNormsRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Listing norms for faction {FactionId}", body.FactionId);

        var normList = await _normListStore.GetAsync(FactionNormsKey(body.FactionId), cancellationToken);
        if (normList == null)
        {
            return (StatusCodes.OK, new ListNormsResponse
            {
                FactionId = body.FactionId,
                Norms = new List<NormDefinitionResponse>(),
            });
        }

        var norms = new List<NormDefinitionResponse>();
        foreach (var normId in normList.NormIds)
        {
            var norm = await _normStore.GetAsync(NormKey(normId), cancellationToken);
            if (norm == null) continue;

            if (body.Severity.HasValue && norm.Severity != body.Severity.Value) continue;
            if (body.Scope.HasValue && norm.Scope != body.Scope.Value) continue;

            norms.Add(MapToNormResponse(norm));
        }

        return (StatusCodes.OK, new ListNormsResponse
        {
            FactionId = body.FactionId,
            Norms = norms,
        });
    }

    /// <summary>
    /// Resolves the full norm hierarchy for a character at a location.
    /// Aggregates guild faction norms, location controlling faction norms, and realm baseline norms.
    /// Cached in Redis with configurable TTL; forceRefresh bypasses cache.
    /// </summary>
    public async Task<(StatusCodes, QueryApplicableNormsResponse?)> QueryApplicableNormsAsync(
        QueryApplicableNormsRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Querying applicable norms for character {CharacterId} in realm {RealmId}",
            body.CharacterId, body.RealmId);

        var cacheKey = NormCacheKey(body.CharacterId, body.LocationId);

        // Check cache unless force refresh
        if (!body.ForceRefresh)
        {
            var cached = await _normCacheStore.GetAsync(cacheKey, cancellationToken);
            if (cached != null)
            {
                return (StatusCodes.OK, new QueryApplicableNormsResponse
                {
                    CharacterId = body.CharacterId,
                    RealmId = body.RealmId,
                    LocationId = body.LocationId,
                    ApplicableNorms = cached.ApplicableNorms.Select(n => new ApplicableNormEntry
                    {
                        NormId = n.NormId,
                        FactionId = n.FactionId,
                        FactionName = n.FactionName,
                        ViolationType = n.ViolationType,
                        BasePenalty = n.BasePenalty,
                        Severity = n.Severity,
                        Scope = n.Scope,
                        Source = n.Source,
                        Description = n.Description,
                    }).ToList(),
                    MergedNormMap = cached.MergedNormMap.ToDictionary(
                        kvp => kvp.Key,
                        kvp => new MergedNormEntry
                        {
                            ViolationType = kvp.Value.ViolationType,
                            BasePenalty = kvp.Value.BasePenalty,
                            Source = kvp.Value.Source,
                            FactionId = kvp.Value.FactionId,
                            Severity = kvp.Value.Severity,
                        }),
                    MembershipFactionCount = cached.MembershipFactionCount,
                    TerritoryFactionResolved = cached.TerritoryFactionResolved,
                    RealmBaselineResolved = cached.RealmBaselineResolved,
                });
            }
        }

        var applicableNorms = new List<CachedApplicableNorm>();
        var mergedNormMap = new Dictionary<string, CachedMergedNorm>();
        int membershipFactionCount = 0;
        bool territoryResolved = false;
        bool baselineResolved = false;

        // 1. Guild faction norms (character's direct memberships) - HIGHEST PRIORITY
        var charList = await _memberListStore.GetAsync(CharacterMembershipsKey(body.CharacterId), cancellationToken);
        if (charList != null)
        {
            foreach (var membership in charList.Memberships)
            {
                var faction = await _factionStore.GetAsync(FactionKey(membership.FactionId), cancellationToken);
                if (faction == null || faction.Status != FactionStatus.Active) continue;
                if (body.GameServiceId.HasValue && faction.GameServiceId != body.GameServiceId.Value) continue;

                var normListModel = await _normListStore.GetAsync(FactionNormsKey(membership.FactionId), cancellationToken);
                if (normListModel == null) continue;

                membershipFactionCount++;
                foreach (var normId in normListModel.NormIds)
                {
                    var norm = await _normStore.GetAsync(NormKey(normId), cancellationToken);
                    if (norm == null) continue;

                    var entry = new CachedApplicableNorm
                    {
                        NormId = norm.NormId,
                        FactionId = norm.FactionId,
                        FactionName = faction.Name,
                        ViolationType = norm.ViolationType,
                        BasePenalty = norm.BasePenalty,
                        Severity = norm.Severity,
                        Scope = norm.Scope,
                        Source = NormSource.Membership,
                        Description = norm.Description,
                    };
                    applicableNorms.Add(entry);

                    // Membership norms have highest priority in merged map
                    if (!mergedNormMap.ContainsKey(norm.ViolationType))
                    {
                        mergedNormMap[norm.ViolationType] = new CachedMergedNorm
                        {
                            ViolationType = norm.ViolationType,
                            BasePenalty = norm.BasePenalty,
                            Source = NormSource.Membership,
                            FactionId = norm.FactionId,
                            Severity = norm.Severity,
                        };
                    }
                }
            }
        }

        // 2. Location controlling faction norms - MEDIUM PRIORITY
        if (body.LocationId.HasValue)
        {
            var locationClaim = await _territoryStore.GetAsync(LocationClaimKey(body.LocationId.Value), cancellationToken);
            if (locationClaim != null && locationClaim.Status == TerritoryClaimStatus.Active)
            {
                var controllingFaction = await _factionStore.GetAsync(FactionKey(locationClaim.FactionId), cancellationToken);
                if (controllingFaction != null && controllingFaction.Status == FactionStatus.Active)
                {
                    territoryResolved = true;
                    var normListModel = await _normListStore.GetAsync(FactionNormsKey(locationClaim.FactionId), cancellationToken);
                    if (normListModel != null)
                    {
                        foreach (var normId in normListModel.NormIds)
                        {
                            var norm = await _normStore.GetAsync(NormKey(normId), cancellationToken);
                            if (norm == null) continue;

                            var entry = new CachedApplicableNorm
                            {
                                NormId = norm.NormId,
                                FactionId = norm.FactionId,
                                FactionName = controllingFaction.Name,
                                ViolationType = norm.ViolationType,
                                BasePenalty = norm.BasePenalty,
                                Severity = norm.Severity,
                                Scope = norm.Scope,
                                Source = NormSource.Territory,
                                Description = norm.Description,
                            };
                            applicableNorms.Add(entry);

                            // Territory norms fill in where membership norms don't exist
                            if (!mergedNormMap.ContainsKey(norm.ViolationType))
                            {
                                mergedNormMap[norm.ViolationType] = new CachedMergedNorm
                                {
                                    ViolationType = norm.ViolationType,
                                    BasePenalty = norm.BasePenalty,
                                    Source = NormSource.Territory,
                                    FactionId = norm.FactionId,
                                    Severity = norm.Severity,
                                };
                            }
                        }
                    }
                }
            }
        }

        // 3. Realm baseline faction norms - LOWEST PRIORITY
        var baselineConditions2 = new List<QueryCondition>
        {
            new QueryCondition { Path = "$.FactionId", Operator = QueryOperator.Exists },
            new QueryCondition { Path = "$.RealmId", Operator = QueryOperator.Equals, Value = body.RealmId.ToString() },
            new QueryCondition { Path = "$.IsRealmBaseline", Operator = QueryOperator.Equals, Value = "true" },
        };
        var baselineResults = await _factionQueryStore.JsonQueryPagedAsync(baselineConditions2, 0, 1, cancellationToken: cancellationToken);
        if (baselineResults.Items.Count > 0)
        {
            var baselineFaction = baselineResults.Items[0].Value;
            if (baselineFaction.Status == FactionStatus.Active)
            {
                baselineResolved = true;
                var normListModel = await _normListStore.GetAsync(FactionNormsKey(baselineFaction.FactionId), cancellationToken);
                if (normListModel != null)
                {
                    foreach (var normId in normListModel.NormIds)
                    {
                        var norm = await _normStore.GetAsync(NormKey(normId), cancellationToken);
                        if (norm == null) continue;

                        var entry = new CachedApplicableNorm
                        {
                            NormId = norm.NormId,
                            FactionId = norm.FactionId,
                            FactionName = baselineFaction.Name,
                            ViolationType = norm.ViolationType,
                            BasePenalty = norm.BasePenalty,
                            Severity = norm.Severity,
                            Scope = norm.Scope,
                            Source = NormSource.RealmBaseline,
                            Description = norm.Description,
                        };
                        applicableNorms.Add(entry);

                        // Realm baseline norms are lowest priority
                        if (!mergedNormMap.ContainsKey(norm.ViolationType))
                        {
                            mergedNormMap[norm.ViolationType] = new CachedMergedNorm
                            {
                                ViolationType = norm.ViolationType,
                                BasePenalty = norm.BasePenalty,
                                Source = NormSource.RealmBaseline,
                                FactionId = norm.FactionId,
                                Severity = norm.Severity,
                            };
                        }
                    }
                }
            }
        }

        // Cache the result
        var cacheModel = new ResolvedNormCacheModel
        {
            CharacterId = body.CharacterId,
            LocationId = body.LocationId,
            ApplicableNorms = applicableNorms,
            MergedNormMap = mergedNormMap,
            MembershipFactionCount = membershipFactionCount,
            TerritoryFactionResolved = territoryResolved,
            RealmBaselineResolved = baselineResolved,
            CachedAt = DateTimeOffset.UtcNow,
        };

        if (_configuration.NormQueryCacheTtlSeconds > 0)
        {
            await _normCacheStore.SaveAsync(cacheKey, cacheModel,
                new StateOptions { Ttl = _configuration.NormQueryCacheTtlSeconds },
                cancellationToken: cancellationToken);
        }

        return (StatusCodes.OK, new QueryApplicableNormsResponse
        {
            CharacterId = body.CharacterId,
            RealmId = body.RealmId,
            LocationId = body.LocationId,
            ApplicableNorms = applicableNorms.Select(n => new ApplicableNormEntry
            {
                NormId = n.NormId,
                FactionId = n.FactionId,
                FactionName = n.FactionName,
                ViolationType = n.ViolationType,
                BasePenalty = n.BasePenalty,
                Severity = n.Severity,
                Scope = n.Scope,
                Source = n.Source,
                Description = n.Description,
            }).ToList(),
            MergedNormMap = mergedNormMap.ToDictionary(
                kvp => kvp.Key,
                kvp => new MergedNormEntry
                {
                    ViolationType = kvp.Value.ViolationType,
                    BasePenalty = kvp.Value.BasePenalty,
                    Source = kvp.Value.Source,
                    FactionId = kvp.Value.FactionId,
                    Severity = kvp.Value.Severity,
                }),
            MembershipFactionCount = membershipFactionCount,
            TerritoryFactionResolved = territoryResolved,
            RealmBaselineResolved = baselineResolved,
        });
    }

    // ========================================================================
    // CLEANUP (Resource-managed via lib-resource, per FOUNDATION TENETS)
    // ========================================================================

    /// <summary>
    /// Removes all memberships for a character (called by lib-resource on character deletion).
    /// </summary>
    public async Task<(StatusCodes, CleanupByCharacterResponse?)> CleanupByCharacterAsync(
        CleanupByCharacterRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Cleaning up faction data for character {CharacterId}", body.CharacterId);

        int membershipsRemoved = 0;
        var charList = await _memberListStore.GetAsync(CharacterMembershipsKey(body.CharacterId), cancellationToken);

        if (charList != null)
        {
            foreach (var membership in charList.Memberships.ToList())
            {
                await RemoveMemberInternalAsync(membership.FactionId, body.CharacterId, cancellationToken);
                membershipsRemoved++;
            }
        }

        _logger.LogInformation("Cleaned up {Count} memberships for character {CharacterId}",
            membershipsRemoved, body.CharacterId);

        return (StatusCodes.OK, new CleanupByCharacterResponse
        {
            MembershipsRemoved = membershipsRemoved,
            Success = true,
        });
    }

    /// <summary>
    /// Removes all factions in a realm and cascades (called by lib-resource on realm deletion).
    /// </summary>
    public async Task<(StatusCodes, CleanupByRealmResponse?)> CleanupByRealmAsync(
        CleanupByRealmRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Cleaning up faction data for realm {RealmId}", body.RealmId);

        int factionsRemoved = 0, membershipsRemoved = 0, claimsRemoved = 0, normsRemoved = 0;

        var conditions = new List<QueryCondition>
        {
            new QueryCondition { Path = "$.FactionId", Operator = QueryOperator.Exists },
            new QueryCondition { Path = "$.RealmId", Operator = QueryOperator.Equals, Value = body.RealmId.ToString() },
        };

        var cleanupOffset = 0;
        bool cleanupHasMore;
        do
        {
            var results = await _factionQueryStore.JsonQueryPagedAsync(
                conditions, cleanupOffset, _configuration.SeedBulkPageSize, cancellationToken: cancellationToken);

            foreach (var result in results.Items)
            {
                var faction = result.Value;

                // Count cascaded data before deletion
                var normList = await _normListStore.GetAsync(FactionNormsKey(faction.FactionId), cancellationToken);
                normsRemoved += normList?.NormIds.Count ?? 0;

                var claimList = await _territoryListStore.GetAsync(FactionClaimsKey(faction.FactionId), cancellationToken);
                claimsRemoved += claimList?.ClaimIds.Count ?? 0;

                membershipsRemoved += faction.MemberCount;

                await DeleteFactionAsync(new DeleteFactionRequest { FactionId = faction.FactionId }, cancellationToken);
                factionsRemoved++;
            }

            cleanupOffset += _configuration.SeedBulkPageSize;
            cleanupHasMore = results.HasMore;
        } while (cleanupHasMore);

        _logger.LogInformation("Cleaned up {Factions} factions for realm {RealmId}", factionsRemoved, body.RealmId);

        return (StatusCodes.OK, new CleanupByRealmResponse
        {
            FactionsRemoved = factionsRemoved,
            MembershipsRemoved = membershipsRemoved,
            TerritoryClaimsRemoved = claimsRemoved,
            NormsRemoved = normsRemoved,
            Success = true,
        });
    }

    /// <summary>
    /// Removes all territory claims for a location (called by lib-resource on location deletion).
    /// </summary>
    public async Task<(StatusCodes, CleanupByLocationResponse?)> CleanupByLocationAsync(
        CleanupByLocationRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Cleaning up territory claims for location {LocationId}", body.LocationId);

        int claimsRemoved = 0;
        var claim = await _territoryStore.GetAsync(LocationClaimKey(body.LocationId), cancellationToken);
        if (claim != null)
        {
            await ReleaseTerritoryInternalAsync(claim, cancellationToken);
            claimsRemoved++;
        }

        _logger.LogInformation("Cleaned up {Count} territory claims for location {LocationId}",
            claimsRemoved, body.LocationId);

        return (StatusCodes.OK, new CleanupByLocationResponse
        {
            ClaimsRemoved = claimsRemoved,
            Success = true,
        });
    }

    // ========================================================================
    // COMPRESSION / ARCHIVE
    // ========================================================================

    /// <summary>
    /// Returns faction membership data for character compression via lib-resource.
    /// </summary>
    public async Task<(StatusCodes, FactionArchive?)> GetCompressDataAsync(
        GetCompressDataRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Getting compress data for character {CharacterId}", body.CharacterId);

        var charList = await _memberListStore.GetAsync(CharacterMembershipsKey(body.CharacterId), cancellationToken);

        var memberships = new List<FactionMemberResponse>();
        if (charList != null)
        {
            foreach (var entry in charList.Memberships)
            {
                var member = await _memberStore.GetAsync(MemberKey(entry.FactionId, body.CharacterId), cancellationToken);
                if (member != null)
                {
                    memberships.Add(MapToMemberResponse(member));
                }
            }
        }

        return (StatusCodes.OK, new FactionArchive
        {
            CharacterId = body.CharacterId,
            HasMemberships = memberships.Count > 0,
            MembershipCount = memberships.Count,
            Memberships = memberships.Count > 0 ? memberships : null,
        });
    }

    /// <summary>
    /// Restores faction memberships from a compressed archive.
    /// </summary>
    public async Task<(StatusCodes, RestoreFromArchiveResponse?)> RestoreFromArchiveAsync(
        RestoreFromArchiveRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Restoring faction data from archive for character {CharacterId}", body.CharacterId);

        var archive = BannouJson.Deserialize<FactionArchive>(body.Data);
        if (archive == null)
        {
            return (StatusCodes.BadRequest, null);
        }

        bool membershipsRestored = false;
        if (archive.Memberships != null && archive.Memberships.Count > 0)
        {
            foreach (var membership in archive.Memberships)
            {
                var faction = await _factionStore.GetAsync(FactionKey(membership.FactionId), cancellationToken);
                if (faction == null || faction.Status != FactionStatus.Active) continue;

                var existingMember = await _memberStore.GetAsync(
                    MemberKey(membership.FactionId, body.CharacterId), cancellationToken);
                if (existingMember != null) continue;

                var member = new FactionMemberModel
                {
                    FactionId = membership.FactionId,
                    CharacterId = body.CharacterId,
                    Role = membership.Role,
                    JoinedAt = membership.JoinedAt,
                    FactionName = faction.Name,
                    FactionCode = faction.Code,
                };

                await _memberStore.SaveAsync(
                    MemberKey(membership.FactionId, body.CharacterId), member, cancellationToken: cancellationToken);

                // Update character's membership list
                var charList = await _memberListStore.GetAsync(CharacterMembershipsKey(body.CharacterId), cancellationToken)
                    ?? new MembershipListModel { CharacterId = body.CharacterId };
                charList.Memberships.Add(new MembershipEntry
                {
                    FactionId = membership.FactionId,
                    Role = membership.Role,
                    JoinedAt = membership.JoinedAt,
                });
                await _memberListStore.SaveAsync(CharacterMembershipsKey(body.CharacterId), charList, cancellationToken: cancellationToken);

                // Update faction member count
                faction.MemberCount++;
                faction.UpdatedAt = DateTimeOffset.UtcNow;
                await _factionStore.SaveAsync(FactionKey(faction.FactionId), faction, cancellationToken: cancellationToken);
                await _factionStore.SaveAsync(FactionCodeKey(faction.GameServiceId, faction.Code), faction, cancellationToken: cancellationToken);

                // Register reference with lib-resource for cleanup coordination (mirrors AddMemberAsync)
                try
                {
                    await _resourceClient.RegisterReferenceAsync(new RegisterReferenceRequest
                    {
                        ResourceType = "character",
                        ResourceId = body.CharacterId,
                        SourceType = "faction",
                        SourceId = membership.FactionId.ToString(),
                    }, cancellationToken);
                }
                catch (ApiException ex)
                {
                    _logger.LogWarning(ex, "Failed to register resource reference during archive restore for character {CharacterId} in faction {FactionId}",
                        body.CharacterId, membership.FactionId);
                }
            }

            membershipsRestored = true;
        }

        _logger.LogInformation("Restored faction data for character {CharacterId}, memberships restored: {Restored}",
            body.CharacterId, membershipsRestored);

        return (StatusCodes.OK, new RestoreFromArchiveResponse
        {
            CharacterId = body.CharacterId,
            MembershipsRestored = membershipsRestored,
            Success = true,
        });
    }
}
