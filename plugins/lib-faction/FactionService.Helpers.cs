using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Resource;
using BeyondImmersion.BannouService.Seed;
using BeyondImmersion.BannouService.State;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Faction;

// =============================================================================
// FactionService — Private & Internal Helper Methods
// =============================================================================
//
// This partial class file is the designated home for all private and internal
// helper methods used by FactionService. This separation exists to
// support the IMPLEMENTATION TENETS telemetry span rules:
//
//   - PRIMARY FILE (FactionService.cs):
//     Contains ONLY public interface method implementations (the methods
//     declared in IFactionService). These methods MUST NOT call
//     ITelemetryProvider.StartActivity because the generated controller
//     already wraps each endpoint with a telemetry span. Adding a span
//     in the service method would double-instrument the endpoint.
//
//   - THIS FILE (FactionService.Helpers.cs):
//     Contains all private/internal helper methods, core logic extracted
//     from endpoints, event publishing helpers, query builders, mapping
//     functions, and any other non-public methods. Every async method in
//     this file MUST call ITelemetryProvider.StartActivity to ensure
//     sub-operations are properly instrumented.
//
// Structural tests enforce both rules:
//   - Services_PrimaryFile_DoesNotCallStartActivity
//   - Services_HelperFiles_HaveStartActivityWhenAsync
//
// WHAT GOES HERE:
//   - Private async helper methods (with StartActivity spans)
//   - Private sync helper methods (query builders, mappers, validators)
//   - Internal static key builders (already in primary file by convention,
//     but may be moved here if the primary file is large)
//   - Event publishing helper methods
//   - Any extracted "core" logic (e.g., CreateAccountCoreAsync)
//
// WHAT STAYS IN THE PRIMARY FILE:
//   - Public interface method implementations (/// <inheritdoc/> methods)
//   - Constructor and field declarations
//   - Constants and key prefix definitions
//
// See: docs/reference/tenets/IMPLEMENTATION-BEHAVIOR.md (T30)
// See: docs/reference/HELPERS-AND-COMMON-PATTERNS.md
// =============================================================================

/// <summary>
/// Private and internal helper methods for FactionService.
/// </summary>
/// <remarks>
/// <para>
/// This partial class contains all non-public helper methods. Every async method
/// in this file MUST include a <c>using var activity = _telemetryProvider.StartActivity(...)</c>
/// span per IMPLEMENTATION TENETS (T30). The generated controller instruments the
/// public interface methods; this file instruments the sub-operations.
/// </para>
/// </remarks>
public partial class FactionService
{
    /// <summary>
    /// Checks whether a faction's seed has a specific capability unlocked.
    /// </summary>
    private async Task<bool> HasCapabilityAsync(Guid? seedId, string capabilityCode, CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.faction", "FactionService.HasCapabilityAsync");
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
        using var activity = _telemetryProvider.StartActivity("bannou.faction", "FactionService.PublishCreatedEventAsync");
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
            AuthorityLevel = model.AuthorityLevel,
            CurrentPhase = model.CurrentPhase,
            MemberCount = model.MemberCount,
            IsDeprecated = model.IsDeprecated,
            DeprecatedAt = model.DeprecatedAt,
            DeprecationReason = model.DeprecationReason,
            CreatedAt = model.CreatedAt,
            UpdatedAt = model.UpdatedAt,
        };
        await _messageBus.PublishFactionCreatedAsync(evt, ct);
    }
    /// <summary>
    /// Publishes a lifecycle updated event for a faction.
    /// </summary>
    private async Task PublishUpdatedEventAsync(FactionModel model, ICollection<string> changedFields, CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.faction", "FactionService.PublishUpdatedEventAsync");
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
            AuthorityLevel = model.AuthorityLevel,
            CurrentPhase = model.CurrentPhase,
            MemberCount = model.MemberCount,
            IsDeprecated = model.IsDeprecated,
            DeprecatedAt = model.DeprecatedAt,
            DeprecationReason = model.DeprecationReason,
            CreatedAt = model.CreatedAt,
            UpdatedAt = model.UpdatedAt,
            ChangedFields = changedFields.ToList(),
        };
        await _messageBus.PublishFactionUpdatedAsync(evt, ct);
    }
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
        using var activity = _telemetryProvider.StartActivity("bannou.faction", "FactionService.InvalidateNormCacheForFactionAsync");
        // Query members of this faction to invalidate their norm caches
        var memberConditions = new List<QueryCondition>
        {
            new QueryCondition { Path = "$.FactionId", Operator = QueryOperator.Exists },
            new QueryCondition { Path = "$.FactionId", Operator = QueryOperator.Equals, Value = factionId.ToString() },
        };
        var offset = 0;
        var pageSize = _configuration.SeedBulkPageSize;
        bool hasMore;
        do
        {
            var members = await _memberQueryStore.JsonQueryPagedAsync(
                memberConditions, offset, pageSize, cancellationToken: ct);

            foreach (var member in members.Items)
            {
                // Delete the generic cache key (no location)
                await _normCacheStore.DeleteAsync(BuildNormCacheKey(member.Value.CharacterId, null), ct);
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
        using var activity = _telemetryProvider.StartActivity("bannou.faction", "FactionService.InvalidateNormCacheForCharacterAsync");
        // Delete the generic cache key (no location); location-specific entries expire via TTL
        await _normCacheStore.DeleteAsync(BuildNormCacheKey(characterId, null), ct);
    }
    /// <summary>
    /// Cascades deletion of all faction sub-entities and deletes the faction itself.
    /// Does NOT check deprecation status — caller is responsible for lifecycle guards.
    /// Used by DeleteFactionAsync (after deprecation check) and CleanupByRealmAsync (bypass).
    /// </summary>
    private async Task DeleteFactionCascadeInternalAsync(FactionModel model, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.faction", "FactionService.DeleteFactionCascadeInternal");

        // Cascade: remove all members (paginated to handle large factions)
        var memberConditions = new List<QueryCondition>
        {
            new QueryCondition { Path = "$.FactionId", Operator = QueryOperator.Exists },
            new QueryCondition { Path = "$.FactionId", Operator = QueryOperator.Equals, Value = model.FactionId.ToString() },
        };
        var memberOffset = 0;
        bool memberHasMore;
        do
        {
            var members = await _memberQueryStore.JsonQueryPagedAsync(
                memberConditions, memberOffset, _configuration.SeedBulkPageSize, cancellationToken: cancellationToken);
            foreach (var member in members.Items)
            {
                try
                {
                    await RemoveMemberInternalAsync(member.Value.FactionId, member.Value.CharacterId, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to remove member {CharacterId} from faction {FactionId} during cascade delete, continuing",
                        member.Value.CharacterId, model.FactionId);
                }
            }
            memberOffset += _configuration.SeedBulkPageSize;
            memberHasMore = members.HasMore;
        } while (memberHasMore);

        // Cascade: release all territory claims
        var claimList = await _territoryListStore.GetAsync(BuildFactionClaimsKey(model.FactionId), cancellationToken);
        if (claimList != null)
        {
            foreach (var claimId in claimList.ClaimIds.ToList())
            {
                try
                {
                    var claim = await _territoryStore.GetAsync(BuildClaimKey(claimId), cancellationToken);
                    if (claim != null && claim.Status == TerritoryClaimStatus.Active)
                    {
                        await ReleaseTerritoryInternalAsync(claim, cancellationToken);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to release territory claim {ClaimId} during cascade delete for faction {FactionId}, continuing",
                        claimId, model.FactionId);
                }
            }
            await _territoryListStore.DeleteAsync(BuildFactionClaimsKey(model.FactionId), cancellationToken: cancellationToken);
        }

        // Cascade: delete all norms (publish events per FOUNDATION TENETS — Event-Driven Architecture)
        var normList = await _normListStore.GetAsync(BuildFactionNormsKey(model.FactionId), cancellationToken);
        if (normList != null)
        {
            foreach (var normId in normList.NormIds.ToList())
            {
                try
                {
                    var norm = await _normStore.GetAsync(BuildNormKey(normId), cancellationToken);
                    await _normStore.DeleteAsync(BuildNormKey(normId), cancellationToken: cancellationToken);
                    if (norm != null)
                    {
                        await _messageBus.PublishFactionNormDeletedAsync(new FactionNormDeletedEvent
                        {
                            EventId = Guid.NewGuid(),
                            Timestamp = DateTimeOffset.UtcNow,
                            FactionId = model.FactionId,
                            NormId = normId,
                            ViolationType = norm.ViolationType,
                        }, cancellationToken);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to delete norm {NormId} during cascade delete for faction {FactionId}, continuing",
                        normId, model.FactionId);
                }
            }
            await _normListStore.DeleteAsync(BuildFactionNormsKey(model.FactionId), cancellationToken: cancellationToken);
        }

        // Cascade governance entries (publish events per FOUNDATION TENETS — Event-Driven Architecture)
        var govList = await _governanceListStore.GetAsync(BuildFactionGovernanceKey(model.FactionId), cancellationToken);
        if (govList != null)
        {
            foreach (var govId in govList.GovernanceIds.ToList())
            {
                try
                {
                    var entry = await _governanceStore.GetAsync(BuildGovernanceKey(govId), cancellationToken);
                    if (entry != null)
                        await _governanceStore.DeleteAsync(BuildFactionGovernanceDomainKey(model.FactionId, entry.Domain), cancellationToken);
                    await _governanceStore.DeleteAsync(BuildGovernanceKey(govId), cancellationToken);
                    if (entry != null)
                    {
                        await _messageBus.PublishFactionGovernanceDeletedAsync(new FactionGovernanceDeletedEvent
                        {
                            EventId = Guid.NewGuid(),
                            Timestamp = DateTimeOffset.UtcNow,
                            FactionId = model.FactionId,
                            GovernanceId = govId,
                            Domain = entry.Domain,
                        }, cancellationToken);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to delete governance entry {GovernanceId} during cascade delete for faction {FactionId}, continuing",
                        govId, model.FactionId);
                }
            }
            await _governanceListStore.DeleteAsync(BuildFactionGovernanceKey(model.FactionId), cancellationToken);
        }

        // Delete faction records
        await _factionStore.DeleteAsync(BuildFactionKey(model.FactionId), cancellationToken: cancellationToken);
        await _factionStore.DeleteAsync(BuildFactionCodeKey(model.GameServiceId, model.Code), cancellationToken: cancellationToken);

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
            IsDeprecated = model.IsDeprecated,
            CreatedAt = model.CreatedAt,
            UpdatedAt = model.UpdatedAt,
        };
        await _messageBus.PublishFactionDeletedAsync(deletedEvt, cancellationToken);
    }
    private async Task<StatusCodes> RemoveMemberInternalAsync(Guid factionId, Guid characterId, CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.faction", "FactionService.RemoveMemberInternalAsync");
        var member = await _memberStore.GetAsync(BuildMemberKey(factionId, characterId), ct);
        if (member == null) return StatusCodes.NotFound;

        await _memberStore.DeleteAsync(BuildMemberKey(factionId, characterId), cancellationToken: ct);

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
        var charList = await _memberListStore.GetAsync(BuildCharacterMembershipsKey(characterId), ct);
        if (charList != null)
        {
            charList.Memberships.RemoveAll(m => m.FactionId == factionId);
            await _memberListStore.SaveAsync(BuildCharacterMembershipsKey(characterId), charList, cancellationToken: ct);
        }

        // Update faction member count
        var faction = await _factionStore.GetAsync(BuildFactionKey(factionId), ct);
        if (faction != null)
        {
            faction.MemberCount = Math.Max(0, faction.MemberCount - 1);
            faction.UpdatedAt = DateTimeOffset.UtcNow;
            await _factionStore.SaveAsync(BuildFactionKey(faction.FactionId), faction, cancellationToken: ct);
            await _factionStore.SaveAsync(BuildFactionCodeKey(faction.GameServiceId, faction.Code), faction, cancellationToken: ct);
        }

        var evt = new FactionMemberRemovedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            FactionId = factionId,
            CharacterId = characterId,
        };
        await _messageBus.PublishFactionMemberRemovedAsync(evt, ct);
        await InvalidateNormCacheForCharacterAsync(characterId, ct);

        _logger.LogInformation("Removed member {CharacterId} from faction {FactionId}", characterId, factionId);
        return StatusCodes.OK;
    }
    private async Task<StatusCodes> ReleaseTerritoryInternalAsync(TerritoryClaimModel claim, CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.faction", "FactionService.ReleaseTerritoryInternalAsync");
        claim.Status = TerritoryClaimStatus.Released;
        claim.ReleasedAt = DateTimeOffset.UtcNow;

        await _territoryStore.SaveAsync(BuildClaimKey(claim.ClaimId), claim, cancellationToken: ct);
        await _territoryStore.DeleteAsync(BuildLocationClaimKey(claim.LocationId), cancellationToken: ct);

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
        var claimList = await _territoryListStore.GetAsync(BuildFactionClaimsKey(claim.FactionId), ct);
        if (claimList != null)
        {
            claimList.ClaimIds.Remove(claim.ClaimId);
            await _territoryListStore.SaveAsync(BuildFactionClaimsKey(claim.FactionId), claimList, cancellationToken: ct);
        }

        var evt = new FactionTerritoryReleasedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            FactionId = claim.FactionId,
            LocationId = claim.LocationId,
            ClaimId = claim.ClaimId,
        };
        await _messageBus.PublishFactionTerritoryReleasedAsync(evt, ct);
        await InvalidateNormCacheForFactionAsync(claim.FactionId, ct);

        _logger.LogInformation("Released territory claim {ClaimId} at location {LocationId}", claim.ClaimId, claim.LocationId);
        return StatusCodes.OK;
    }
    /// <summary>
    /// Walks up the faction parent chain to find a Sovereign faction.
    /// </summary>
    private async Task<FactionModel?> FindSovereignAsync(FactionModel faction, CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.faction", "FactionService.FindSovereign");
        var current = faction;
        int depth = 0;
        while (current.ParentFactionId.HasValue && depth < _configuration.MaxHierarchyDepth)
        {
            current = await _factionStore.GetAsync(BuildFactionKey(current.ParentFactionId.Value), ct);
            if (current == null) return null;
            if (current.AuthorityLevel == AuthorityLevel.Sovereign) return current;
            depth++;
        }
        return null;
    }
    /// <summary>
    /// Attempts to resolve governance data for a faction and domain.
    /// Returns null if no governance entry exists for the domain.
    /// </summary>
    private async Task<GovernanceDataResponse?> TryResolveGovernanceAsync(
        FactionModel faction, string domain, FactionModel? sovereign, CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.faction", "FactionService.TryResolveGovernance");
        var domainKey = BuildFactionGovernanceDomainKey(faction.FactionId, domain);
        var entry = await _governanceStore.GetAsync(domainKey, ct);
        if (entry == null) return null;

        return new GovernanceDataResponse
        {
            JurisdictionalFactionId = faction.FactionId,
            JurisdictionalFactionName = faction.Name,
            AuthorityLevel = faction.AuthorityLevel,
            TemplateCode = entry.TemplateCode,
            GovernanceParameters = entry.GovernanceParameters,
            SovereignFactionId = sovereign?.FactionId ?? faction.FactionId,
        };
    }
}
