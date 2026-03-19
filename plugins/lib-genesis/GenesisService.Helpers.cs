using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService.Actor;
using BeyondImmersion.BannouService.Currency;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Relationship;
using BeyondImmersion.BannouService.Resource;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Genesis;

/// <summary>
/// Private and internal helper methods for GenesisService.
/// Every async method includes telemetry spans per IMPLEMENTATION TENETS.
/// </summary>
public partial class GenesisService
{
    /// <summary>
    /// Validates template structural integrity per map specification.
    /// </summary>
    private static string? ValidateTemplateStructure(RegisterTemplateRequest body)
    {
        var walletCodes = new HashSet<string>(body.Economy.Wallets.Select(w => w.WalletCode));
        var domainCodes = new HashSet<string>(body.Seed.Domains.Select(d => d.DomainCode));
        var seen = new HashSet<(string, string, GrowthDirection)>();

        foreach (var mapping in body.Economy.GrowthMappings)
        {
            if (!walletCodes.Contains(mapping.WalletCode))
                return $"Growth mapping references unknown wallet code: {mapping.WalletCode}";

            if (!domainCodes.Contains(mapping.Domain))
                return $"Growth mapping references unknown domain: {mapping.Domain}";

            var triple = (mapping.WalletCode, mapping.Domain, mapping.Direction);
            if (!seen.Add(triple))
                return $"Duplicate growth mapping: ({mapping.WalletCode}, {mapping.Domain}, {mapping.Direction})";
        }

        return null;
    }

    /// <summary>
    /// Maps a template model to its API response.
    /// </summary>
    private static GenesisTemplateResponse MapTemplateToResponse(GenesisTemplateModel model) => new()
    {
        TemplateCode = model.TemplateCode,
        GameServiceId = model.GameServiceId,
        DisplayName = model.DisplayName,
        Description = model.Description,
        Seed = model.Seed,
        Economy = model.Economy,
        Storage = model.Storage,
        Awakening = model.Awakening,
        PhysicalFormType = model.PhysicalFormType,
        Bond = model.Bond,
        ArchiveOnDestruction = model.ArchiveOnDestruction,
        IsDeprecated = model.IsDeprecated,
        DeprecatedAt = model.DeprecatedAt,
        DeprecationReason = model.DeprecationReason,
        CreatedAt = model.CreatedAt,
        UpdatedAt = model.UpdatedAt,
    };

    /// <summary>
    /// Maps an entity model to its API response, optionally including wallet balances.
    /// </summary>
    private static GenesisEntityResponse MapEntityToResponse(
        GenesisEntityModel model, Dictionary<string, double>? walletBalances) => new()
    {
        EntityId = model.EntityId,
        TemplateCode = model.TemplateCode,
        GameServiceId = model.GameServiceId,
        RealmId = model.RealmId,
        Code = model.Code,
        DisplayName = model.DisplayName,
        WalletIds = model.WalletIds,
        WalletBalances = walletBalances,
        InventoryIds = model.InventoryIds,
        CurrentPhase = model.CurrentPhase,
        CognitiveStage = model.CognitiveStage,
        ActorId = model.ActorId,
        CharacterId = model.CharacterId,
        PhysicalFormType = model.PhysicalFormType,
        PhysicalFormId = model.PhysicalFormId,
        BondTargetEntityType = model.BondTargetEntityType,
        BondTargetEntityId = model.BondTargetEntityId,
        BondId = model.BondId,
        Status = model.Status,
        CreatedAt = model.CreatedAt,
        UpdatedAt = model.UpdatedAt,
    };

    /// <summary>
    /// Maps an entity model to its cached representation.
    /// </summary>
    private static CachedGenesisEntity MapEntityToCache(GenesisEntityModel model) => new()
    {
        EntityId = model.EntityId,
        TemplateCode = model.TemplateCode,
        GameServiceId = model.GameServiceId,
        RealmId = model.RealmId,
        Code = model.Code,
        DisplayName = model.DisplayName,
        SeedId = model.SeedId,
        WalletIds = new Dictionary<string, Guid>(model.WalletIds),
        InventoryIds = new Dictionary<string, Guid>(model.InventoryIds),
        CurrentPhase = model.CurrentPhase,
        CognitiveStage = model.CognitiveStage,
        ActorId = model.ActorId,
        CharacterId = model.CharacterId,
        PhysicalFormType = model.PhysicalFormType,
        PhysicalFormId = model.PhysicalFormId,
        BondTargetEntityType = model.BondTargetEntityType,
        BondTargetEntityId = model.BondTargetEntityId,
        BondId = model.BondId,
        Status = model.Status,
        CreatedAt = model.CreatedAt,
        UpdatedAt = model.UpdatedAt,
    };

    /// <summary>
    /// Maps a cached entity back to the entity model.
    /// </summary>
    private static GenesisEntityModel MapCachedToEntity(CachedGenesisEntity cached) => new()
    {
        EntityId = cached.EntityId,
        TemplateCode = cached.TemplateCode,
        GameServiceId = cached.GameServiceId,
        RealmId = cached.RealmId,
        Code = cached.Code,
        DisplayName = cached.DisplayName,
        SeedId = cached.SeedId,
        WalletIds = new Dictionary<string, Guid>(cached.WalletIds),
        InventoryIds = new Dictionary<string, Guid>(cached.InventoryIds),
        CurrentPhase = cached.CurrentPhase,
        CognitiveStage = cached.CognitiveStage,
        ActorId = cached.ActorId,
        CharacterId = cached.CharacterId,
        PhysicalFormType = cached.PhysicalFormType,
        PhysicalFormId = cached.PhysicalFormId,
        BondTargetEntityType = cached.BondTargetEntityType,
        BondTargetEntityId = cached.BondTargetEntityId,
        BondId = cached.BondId,
        Status = cached.Status,
        CreatedAt = cached.CreatedAt,
        UpdatedAt = cached.UpdatedAt,
    };

    /// <summary>
    /// Builds an EntityDeletedEvent from an entity model.
    /// </summary>
    private static EntityDeletedEvent BuildEntityDeletedEvent(GenesisEntityModel entity) => new()
    {
        EntityId = entity.EntityId,
        TemplateCode = entity.TemplateCode,
        GameServiceId = entity.GameServiceId,
        RealmId = entity.RealmId,
        Code = entity.Code,
        DisplayName = entity.DisplayName,
        WalletIds = entity.WalletIds,
        InventoryIds = entity.InventoryIds,
        CurrentPhase = entity.CurrentPhase,
        CognitiveStage = entity.CognitiveStage,
        ActorId = entity.ActorId,
        CharacterId = entity.CharacterId,
        PhysicalFormType = entity.PhysicalFormType,
        PhysicalFormId = entity.PhysicalFormId,
        Status = entity.Status,
        CreatedAt = entity.CreatedAt,
        UpdatedAt = entity.UpdatedAt,
    };

    /// <summary>
    /// Core entity destruction logic shared by DestroyEntity, CleanupByCharacter, and CleanupByRealm.
    /// </summary>
    private async Task DestroyEntityCoreAsync(
        GenesisEntityModel entity, GenesisTemplateModel? template, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.genesis", "GenesisService.DestroyEntityCore");

        // Stop actor if running
        if (entity.ActorId != null)
        {
            try { await _actorClient.StopActorAsync(new Actor.StopActorRequest { ActorId = entity.ActorId.Value.ToString() }, cancellationToken); }
            catch (ApiException ex) { _logger.LogWarning(ex, "Failed to stop actor for entity {EntityId}", entity.EntityId); }
        }

        // Archive character if awakened and template says to
        if (entity.CharacterId != null && template?.ArchiveOnDestruction == true)
        {
            try
            {
                await _resourceClient.ExecuteCompressAsync(
                    new Resource.ExecuteCompressRequest { ResourceType = "character", ResourceId = entity.CharacterId.Value },
                    cancellationToken);
            }
            catch (ApiException ex) { _logger.LogWarning(ex, "Failed to archive character for entity {EntityId}", entity.EntityId); }
        }

        // Dissolve bond if exists
        if (entity.BondId != null)
        {
            try { await _relationshipClient.EndRelationshipAsync(new Relationship.EndRelationshipRequest { RelationshipId = entity.BondId.Value }, cancellationToken); }
            catch (ApiException ex) { _logger.LogWarning(ex, "Failed to delete bond for entity {EntityId}", entity.EntityId); }
        }

        // Cleanup provisioned infrastructure via Resource
        try
        {
            await _resourceClient.ExecuteCleanupAsync(
                new Resource.ExecuteCleanupRequest { ResourceType = "genesis-entity", ResourceId = entity.EntityId },
                cancellationToken);
        }
        catch (ApiException ex) { _logger.LogWarning(ex, "Failed resource cleanup for entity {EntityId}", entity.EntityId); }

        // Delete all records and indexes
        await DeleteEntityRecordsAsync(entity, cancellationToken);

        // Publish deletion event
        await _messageBus.PublishEntityDeletedAsync(BuildEntityDeletedEvent(entity), cancellationToken);
    }

    /// <summary>
    /// Deletes all entity records and indexes from state stores and cache.
    /// </summary>
    private async Task DeleteEntityRecordsAsync(
        GenesisEntityModel entity, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.genesis", "GenesisService.DeleteEntityRecords");

        await _entityStore.DeleteAsync(BuildEntityKey(entity.EntityId), cancellationToken);

        if (entity.Code != null)
            await _entityStore.DeleteAsync(
                BuildEntityCodeKey(entity.GameServiceId, entity.RealmId, entity.Code), cancellationToken);

        await RemoveFromEntityTemplateIndexAsync(
            entity.TemplateCode, entity.RealmId, entity.EntityId, cancellationToken);

        foreach (var walletId in entity.WalletIds.Values)
            await _entityStore.DeleteAsync(BuildEntityWalletKey(walletId), cancellationToken);

        await _entityCacheStore.DeleteAsync(BuildEntityCacheKey(entity.EntityId), cancellationToken);
        await _capsCacheStore.DeleteAsync(BuildCapsCacheKey(entity.EntityId), cancellationToken);
    }

    /// <summary>
    /// Fetches wallet balances for an entity by resolving currency definitions from the template
    /// and querying the Currency service for each wallet.
    /// </summary>
    private async Task<Dictionary<string, double>?> FetchWalletBalancesAsync(
        GenesisEntityModel entity, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.genesis", "GenesisService.FetchWalletBalances");

        var template = await _templateStore.GetAsync(BuildTemplateKey(entity.TemplateCode), cancellationToken);
        if (template == null) return null;

        var walletBalances = new Dictionary<string, double>();
        foreach (var (walletCode, walletId) in entity.WalletIds)
        {
            try
            {
                // Resolve currency definition ID from template wallet config
                var walletConfig = template.Economy.Wallets.FirstOrDefault(w => w.WalletCode == walletCode);
                if (walletConfig == null) continue;

                var currencyDef = await _currencyClient.GetCurrencyDefinitionAsync(
                    new GetCurrencyDefinitionRequest { Code = walletConfig.CurrencyCode }, cancellationToken);

                var balResponse = await _currencyClient.GetBalanceAsync(
                    new GetBalanceRequest { WalletId = walletId, CurrencyDefinitionId = currencyDef.DefinitionId },
                    cancellationToken);
                walletBalances[walletCode] = balResponse.Amount;
            }
            catch (ApiException ex)
            {
                _logger.LogWarning(ex, "Failed to get balance for wallet {WalletCode} ({WalletId})",
                    walletCode, walletId);
            }
        }
        return walletBalances;
    }

    /// <summary>
    /// Adds a template code to the template-game index.
    /// </summary>
    private async Task AddToTemplateGameIndexAsync(
        Guid gameServiceId, string templateCode, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.genesis", "GenesisService.AddToTemplateGameIndex");

        var key = BuildTemplateGameKey(gameServiceId);
        var index = await _templateListStore.GetAsync(key, cancellationToken) ?? new GenesisTemplateListModel();
        if (!index.TemplateCodes.Contains(templateCode))
        {
            index.TemplateCodes.Add(templateCode);
            await _templateListStore.SaveAsync(key, index, cancellationToken: cancellationToken);
        }
    }

    /// <summary>
    /// Removes a template code from the template-game index.
    /// </summary>
    private async Task RemoveFromTemplateGameIndexAsync(
        Guid gameServiceId, string templateCode, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.genesis", "GenesisService.RemoveFromTemplateGameIndex");

        var key = BuildTemplateGameKey(gameServiceId);
        var index = await _templateListStore.GetAsync(key, cancellationToken);
        if (index != null)
        {
            index.TemplateCodes.Remove(templateCode);
            await _templateListStore.SaveAsync(key, index, cancellationToken: cancellationToken);
        }
    }

    /// <summary>
    /// Adds an entity ID to the entity-template index.
    /// </summary>
    private async Task AddToEntityTemplateIndexAsync(
        string templateCode, Guid realmId, Guid entityId, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.genesis", "GenesisService.AddToEntityTemplateIndex");

        var key = BuildEntityTemplateKey(templateCode, realmId);
        var index = await _entityListStore.GetAsync(key, cancellationToken) ?? new GenesisEntityListModel();
        if (!index.EntityIds.Contains(entityId))
        {
            index.EntityIds.Add(entityId);
            await _entityListStore.SaveAsync(key, index, cancellationToken: cancellationToken);
        }
    }

    /// <summary>
    /// Removes an entity ID from the entity-template index.
    /// </summary>
    private async Task RemoveFromEntityTemplateIndexAsync(
        string templateCode, Guid realmId, Guid entityId, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.genesis", "GenesisService.RemoveFromEntityTemplateIndex");

        var key = BuildEntityTemplateKey(templateCode, realmId);
        var index = await _entityListStore.GetAsync(key, cancellationToken);
        if (index != null)
        {
            index.EntityIds.Remove(entityId);
            await _entityListStore.SaveAsync(key, index, cancellationToken: cancellationToken);
        }
    }

    /// <summary>
    /// Finds all genesis entities linked to a character ID via IQueryableStateStore.
    /// </summary>
    private async Task<List<GenesisEntityModel>> FindEntitiesByCharacterIdAsync(
        Guid characterId, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.genesis", "GenesisService.FindEntitiesByCharacterId");

        var results = await _entityQueryStore.QueryAsync(
            e => e.CharacterId == characterId, cancellationToken);

        // Deduplicate by EntityId (duplicate key entries in MySQL store)
        return results.DistinctBy(e => e.EntityId).ToList();
    }

    /// <summary>
    /// Finds all genesis entities in a realm via IQueryableStateStore.
    /// </summary>
    private async Task<List<GenesisEntityModel>> FindEntitiesByRealmIdAsync(
        Guid realmId, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.genesis", "GenesisService.FindEntitiesByRealmId");

        var results = await _entityQueryStore.QueryAsync(
            e => e.RealmId == realmId, cancellationToken);

        return results.DistinctBy(e => e.EntityId).ToList();
    }
}
