using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService.Actor;
using BeyondImmersion.BannouService.Currency;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Relationship;
using BeyondImmersion.BannouService.Resource;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
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

        if (entity.ActorId != null)
        {
            try { await _actorClient.StopActorAsync(new StopActorRequest { ActorId = entity.ActorId.Value.ToString() }, cancellationToken); }
            catch (ApiException ex) { _logger.LogWarning(ex, "Failed to stop actor for entity {EntityId}", entity.EntityId); }
        }

        if (entity.CharacterId != null && template?.ArchiveOnDestruction == true)
        {
            try { await _resourceClient.ExecuteCompressAsync(new ExecuteCompressRequest { ResourceType = "character", ResourceId = entity.CharacterId.Value }, cancellationToken); }
            catch (ApiException ex) { _logger.LogWarning(ex, "Failed to archive character for entity {EntityId}", entity.EntityId); }
        }

        if (entity.BondId != null)
        {
            try { await _relationshipClient.EndRelationshipAsync(new EndRelationshipRequest { RelationshipId = entity.BondId.Value }, cancellationToken); }
            catch (ApiException ex) { _logger.LogWarning(ex, "Failed to end bond for entity {EntityId}", entity.EntityId); }
        }

        try { await _resourceClient.ExecuteCleanupAsync(new ExecuteCleanupRequest { ResourceType = "genesis-entity", ResourceId = entity.EntityId }, cancellationToken); }
        catch (ApiException ex) { _logger.LogWarning(ex, "Failed resource cleanup for entity {EntityId}", entity.EntityId); }

        await DeleteEntityRecordsAsync(entity, cancellationToken);
        await _messageBus.PublishEntityDeletedAsync(BuildEntityDeletedEvent(entity), cancellationToken);
    }

    /// <summary>
    /// Deletes all entity records from primary store, index stores, and cache.
    /// </summary>
    private async Task DeleteEntityRecordsAsync(
        GenesisEntityModel entity, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.genesis", "GenesisService.DeleteEntityRecords");

        // Primary entity record
        await _entityStore.DeleteAsync(BuildEntityKey(entity.EntityId), cancellationToken);

        // Index store entries
        if (entity.Code != null)
            await _entityIndexStore.DeleteAsync(
                BuildEntityCodeKey(entity.GameServiceId, entity.RealmId, entity.Code), cancellationToken);

        await RemoveFromEntityTemplateIndexAsync(
            entity.TemplateCode, entity.RealmId, entity.EntityId, cancellationToken);

        foreach (var walletId in entity.WalletIds.Values)
            await _entityIndexStore.DeleteAsync(BuildEntityWalletKey(walletId), cancellationToken);

        // Remove from template→entity reverse index for clean-deprecated instance checks
        await _entityIndexStore.RemoveFromStringListAsync(
            BuildEntityTemplateInstancesKey(entity.TemplateCode),
            entity.EntityId.ToString(),
            _configuration.ListOperationMaxRetries,
            _logger,
            cancellationToken);

        // Cache entries
        await _entityCacheStore.DeleteAsync(BuildEntityCacheKey(entity.EntityId), cancellationToken);
        await _capsCacheStore.DeleteAsync(BuildCapsCacheKey(entity.EntityId), cancellationToken);

        // Remove this entity's wallets from the in-memory wallet map on this node.
        // Other nodes receive the same cleanup via the genesis.entity.deleted event handler.
        RemoveFromWalletMap(entity);
    }

    /// <summary>
    /// Fetches wallet balances by resolving currency definitions from the template.
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
                _logger.LogWarning(ex, "Failed to get balance for wallet {WalletCode} ({WalletId})", walletCode, walletId);
            }
        }
        return walletBalances;
    }

    /// <summary>
    /// Compensates for partially-provisioned infrastructure when CreateEntity fails mid-provisioning.
    /// Directly closes wallets and deletes containers that were successfully created. Seed has no
    /// individual delete API — orphaned seeds owned by a nonexistent entity are cleaned up by Seed's
    /// owner reconciliation worker. Best-effort: logs warnings on individual failures, does not throw.
    /// </summary>
    private async Task CompensateProvisioningAsync(
        Dictionary<string, Guid> walletIds, Dictionary<string, Guid> inventoryIds,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.genesis", "GenesisService.CompensateProvisioning");

        _logger.LogWarning("Compensating failed entity provisioning: wallets={WalletCount}, inventories={InventoryCount}",
            walletIds.Count, inventoryIds.Count);

        foreach (var (code, containerId) in inventoryIds)
        {
            try { await _inventoryClient.DeleteContainerAsync(new Inventory.DeleteContainerRequest { ContainerId = containerId }, cancellationToken); }
            catch (ApiException ex) { _logger.LogWarning(ex, "Failed to compensate inventory {Code} ({ContainerId})", code, containerId); }
        }

        foreach (var (code, walletId) in walletIds)
        {
            try { await _currencyClient.CloseWalletAsync(new CloseWalletRequest { WalletId = walletId }, cancellationToken); }
            catch (ApiException ex) { _logger.LogWarning(ex, "Failed to compensate wallet {Code} ({WalletId})", code, walletId); }
        }
    }

    /// <summary>
    /// Adds a template code to the template-game index (template index store).
    /// </summary>
    private async Task AddToTemplateGameIndexAsync(
        Guid gameServiceId, string templateCode, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.genesis", "GenesisService.AddToTemplateGameIndex");

        var key = BuildTemplateGameKey(gameServiceId);
        var index = await _templateListIndexStore.GetAsync(key, cancellationToken) ?? new GenesisTemplateListModel();
        if (!index.TemplateCodes.Contains(templateCode))
        {
            index.TemplateCodes.Add(templateCode);
            await _templateListIndexStore.SaveAsync(key, index, cancellationToken: cancellationToken);
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
        var index = await _templateListIndexStore.GetAsync(key, cancellationToken);
        if (index != null)
        {
            index.TemplateCodes.Remove(templateCode);
            await _templateListIndexStore.SaveAsync(key, index, cancellationToken: cancellationToken);
        }
    }

    /// <summary>
    /// Adds an entity ID to the entity-template index (entity index store).
    /// </summary>
    private async Task AddToEntityTemplateIndexAsync(
        string templateCode, Guid realmId, Guid entityId, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.genesis", "GenesisService.AddToEntityTemplateIndex");

        var key = BuildEntityTemplateKey(templateCode, realmId);
        var index = await _entityListIndexStore.GetAsync(key, cancellationToken) ?? new GenesisEntityListModel();
        if (!index.EntityIds.Contains(entityId))
        {
            index.EntityIds.Add(entityId);
            await _entityListIndexStore.SaveAsync(key, index, cancellationToken: cancellationToken);
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
        var index = await _entityListIndexStore.GetAsync(key, cancellationToken);
        if (index != null)
        {
            index.EntityIds.Remove(entityId);
            await _entityListIndexStore.SaveAsync(key, index, cancellationToken: cancellationToken);
        }
    }

    /// <summary>
    /// Finds all genesis entities linked to a character ID via IQueryableStateStore.
    /// Clean query — entity store contains only primary entity records, no index duplicates.
    /// </summary>
    private async Task<List<GenesisEntityModel>> FindEntitiesByCharacterIdAsync(
        Guid characterId, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.genesis", "GenesisService.FindEntitiesByCharacterId");

        var results = await _entityQueryStore.QueryAsync(
            e => e.CharacterId == characterId, cancellationToken);
        return results.ToList();
    }

    /// <summary>
    /// Finds all genesis entities in a realm via IQueryableStateStore.
    /// Clean query — entity store contains only primary entity records, no index duplicates.
    /// </summary>
    private async Task<List<GenesisEntityModel>> FindEntitiesByRealmIdAsync(
        Guid realmId, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.genesis", "GenesisService.FindEntitiesByRealmId");

        var results = await _entityQueryStore.QueryAsync(
            e => e.RealmId == realmId, cancellationToken);
        return results.ToList();
    }

    /// <summary>
    /// Populates the in-memory wallet map on this node with mappings for every wallet owned by
    /// the given entity. Called immediately after entity creation/restoration to avoid depending
    /// on the event round-trip for same-node mutations.
    /// </summary>
    /// <remarks>
    /// This method is synchronous (no telemetry span — it's a pure in-memory dictionary mutation
    /// that never awaits). The canonical populate path for remote-created entities is
    /// <see cref="HandleGenesisEntityCreatedAsync"/>, which runs on every node via event fan-out.
    /// </remarks>
    internal void PopulateWalletMap(GenesisEntityModel entity, GenesisTemplateModel template)
    {
        var growthMappings = template.Economy.GrowthMappings.ToList();
        foreach (var (walletCode, walletId) in entity.WalletIds)
        {
            _growthState.SetWalletMapping(walletId, new GenesisWalletMapping(
                EntityId: entity.EntityId,
                TemplateCode: entity.TemplateCode,
                WalletCode: walletCode,
                GrowthMappings: growthMappings));
        }
    }

    /// <summary>
    /// Removes wallet map entries for the given entity's wallets. Called when the entity is
    /// destroyed so subsequent currency mutations for reused wallet IDs (if any) are not
    /// misattributed.
    /// </summary>
    internal void RemoveFromWalletMap(GenesisEntityModel entity)
    {
        foreach (var walletId in entity.WalletIds.Values)
            _growthState.TryRemoveWalletMapping(walletId);
    }

    /// <summary>
    /// Creates actor templates for every phase in the request that requires an actor
    /// (EventBrain/CharacterBrain with a behaviorRef), storing the resolved template IDs in
    /// <see cref="GenesisGrowthState.ActorTemplateMap"/>. Failures are logged and skipped —
    /// if actor template creation fails here, the subsequent phase transition will publish a
    /// transition-failed event instead of silently losing the transition.
    /// </summary>
    private async Task EnsureActorTemplatesForRegistrationAsync(
        RegisterTemplateRequest body, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.genesis", "GenesisService.EnsureActorTemplatesForRegistration");

        foreach (var phase in body.Seed.Phases)
        {
            if (phase.CognitiveStage == CognitiveStage.Dormant) continue;
            if (string.IsNullOrWhiteSpace(phase.BehaviorRef)) continue;

            var mapKey = GenesisSeedEvolutionListener.BuildActorTemplateKey(body.TemplateCode, phase.PhaseName);
            if (_growthState.ContainsActorTemplate(mapKey)) continue;

            var category = $"genesis:{body.TemplateCode}:{phase.PhaseName}";
            Guid actorTemplateId;
            try
            {
                var response = await _actorClient.CreateActorTemplateAsync(
                    new CreateActorTemplateRequest
                    {
                        Category = category,
                        BehaviorRef = phase.BehaviorRef!,
                    }, cancellationToken);
                actorTemplateId = response.TemplateId;
            }
            catch (ApiException ex) when (ex.StatusCode == 409)
            {
                // Template for this category already exists — resolve it
                try
                {
                    var existing = await _actorClient.GetActorTemplateAsync(
                        new GetActorTemplateRequest { Category = category }, cancellationToken);
                    actorTemplateId = existing.TemplateId;
                }
                catch (ApiException lookupEx)
                {
                    _logger.LogWarning(lookupEx,
                        "Actor template for category {Category} reported conflict but could not be resolved",
                        category);
                    continue;
                }
            }
            catch (ApiException ex)
            {
                _logger.LogWarning(ex,
                    "Actor template creation failed for {Category}, skipping — phase transitions will publish transition-failed",
                    category);
                continue;
            }

            _growthState.SetActorTemplate(mapKey, actorTemplateId);
            _logger.LogDebug(
                "Registered actor template {ActorTemplateId} for genesis template {TemplateCode} phase {PhaseName}",
                actorTemplateId, body.TemplateCode, phase.PhaseName);
        }
    }
}
