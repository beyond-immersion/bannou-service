using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService.Contract;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Providers;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Obligation;

// =============================================================================
// ObligationService — Private & Internal Helper Methods
// =============================================================================
//
// This partial class file is the designated home for all private and internal
// helper methods used by ObligationService. This separation exists to
// support the IMPLEMENTATION TENETS telemetry span rules:
//
//   - PRIMARY FILE (ObligationService.cs):
//     Contains ONLY public interface method implementations (the methods
//     declared in IObligationService). These methods MUST NOT call
//     ITelemetryProvider.StartActivity because the generated controller
//     already wraps each endpoint with a telemetry span. Adding a span
//     in the service method would double-instrument the endpoint.
//
//   - THIS FILE (ObligationService.Helpers.cs):
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
/// Private and internal helper methods for ObligationService.
/// </summary>
/// <remarks>
/// <para>
/// This partial class contains all non-public helper methods. Every async method
/// in this file MUST include a <c>using var activity = _telemetryProvider.StartActivity(...)</c>
/// span per IMPLEMENTATION TENETS (T30). The generated controller instruments the
/// public interface methods; this file instruments the sub-operations.
/// </para>
/// </remarks>
public partial class ObligationService
{
    // ========================================================================
    // Internal: Cache Rebuild
    // ========================================================================

    /// <summary>
    /// Rebuilds the obligation cache for a character from active contracts.
    /// Acquires a distributed lock to serialize concurrent rebuilds.
    /// </summary>
    internal async Task<ObligationManifestModel> RebuildObligationCacheAsync(
        Guid characterId, string trigger, CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.obligation", "ObligationService.RebuildObligationCacheAsync");
        var lockOwner = $"rebuild-cache-{Guid.NewGuid():N}";
        await using var lockResponse = await _lockProvider.LockAsync(
            storeName: StateStoreDefinitions.ObligationLock,
            resourceId: $"cache:{characterId}",
            lockOwner,
            expiryInSeconds: _configuration.LockTimeoutSeconds,
            cancellationToken: ct);

        if (!lockResponse.Success)
        {
            _logger.LogWarning("Failed to acquire lock for cache rebuild of character {CharacterId}", characterId);
            // Return existing cached value if available (stale data is better than no data on the query path)
            var existingCached = await _cacheStore.GetAsync(characterId.ToString(), ct);
            if (existingCached != null)
            {
                return existingCached;
            }
            // No cached data exists; return empty manifest rather than failing the request
            return new ObligationManifestModel
            {
                CharacterId = characterId,
                LastRefreshedAt = DateTimeOffset.UtcNow
            };
        }

        // Check cache again under lock (another instance may have rebuilt it)
        var cached = await _cacheStore.GetAsync(characterId.ToString(), ct);
        if (cached != null && trigger != "manual")
        {
            return cached;
        }

        // Query active contracts for this character
        QueryContractInstancesResponse contractsResponse;
        try
        {
            contractsResponse = await _contractClient.QueryContractInstancesAsync(
                new QueryContractInstancesRequest
                {
                    PartyEntityId = characterId,
                    PartyEntityType = EntityType.Character,
                    Statuses = new[] { ContractStatus.Active },
                    PageSize = _configuration.MaxActiveContractsQuery
                }, ct);
        }
        catch (ApiException ex)
        {
            _logger.LogWarning(ex,
                "Failed to query active contracts for character {CharacterId}, status {Status}",
                characterId, ex.StatusCode);
            await _messageBus.TryPublishErrorAsync(
                "obligation",
                "RebuildObligationCache",
                "ContractQueryFailed",
                ex.Message,
                dependency: "contract",
                endpoint: "query-contract-instances",
                stack: ex.StackTrace,
                cancellationToken: ct);
            return new ObligationManifestModel
            {
                CharacterId = characterId,
                LastRefreshedAt = DateTimeOffset.UtcNow
            };
        }

        var obligations = new List<ObligationEntryModel>();
        var contractCount = 0;

        foreach (var contract in contractsResponse.Contracts)
        {
            var clauses = ExtractBehavioralClauses(contract.Terms);
            if (clauses.Count == 0) continue;

            contractCount++;
            var party = contract.Parties.FirstOrDefault(p => p.EntityId == characterId);
            var role = party?.Role;

            foreach (var clause in clauses)
            {
                obligations.Add(new ObligationEntryModel
                {
                    ContractId = contract.ContractId,
                    TemplateCode = contract.TemplateCode,
                    ClauseCode = clause.ClauseCode,
                    ViolationType = clause.ViolationType,
                    BasePenalty = clause.BasePenalty,
                    Description = clause.Description,
                    ContractRole = role,
                    EffectiveUntil = contract.EffectiveUntil
                });
            }
        }

        // Apply max obligations safety limit
        if (obligations.Count > _configuration.MaxObligationsPerCharacter)
        {
            _logger.LogWarning(
                "Character {CharacterId} has {Count} obligations exceeding limit {Limit}, truncating",
                characterId, obligations.Count, _configuration.MaxObligationsPerCharacter);
            obligations = obligations.Take(_configuration.MaxObligationsPerCharacter).ToList();
        }

        // Build violation cost map (aggregate by violation type)
        var violationCostMap = obligations
            .GroupBy(o => o.ViolationType, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Sum(o => o.BasePenalty));

        var manifest = new ObligationManifestModel
        {
            CharacterId = characterId,
            Obligations = obligations,
            ViolationCostMap = violationCostMap,
            TotalActiveContracts = contractCount,
            LastRefreshedAt = DateTimeOffset.UtcNow
        };

        // Store in cache with TTL
        await _cacheStore.SaveAsync(
            characterId.ToString(),
            manifest,
            new StateOptions { Ttl = _configuration.CacheTtlMinutes * 60 },
            ct);

        // Publish cache rebuilt event (observability)
        await _messageBus.PublishObligationCacheRebuiltAsync(new ObligationCacheRebuiltEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            CharacterId = characterId,
            ObligationCount = obligations.Count,
            ContractCount = contractCount,
            Trigger = trigger
        }, ct);

        _logger.LogInformation(
            "Rebuilt obligation cache for character {CharacterId}: {ObligationCount} obligations from {ContractCount} contracts (trigger: {Trigger})",
            characterId, obligations.Count, contractCount, trigger);

        return manifest;
    }

    // ========================================================================
    // Internal: Action Tag Resolution
    // ========================================================================

    /// <summary>
    /// Resolves a GOAP action tag to violation type codes via explicit mappings.
    /// Falls back to 1:1 convention (tag name = violation type) when no mapping exists.
    /// </summary>
    private async Task<List<string>> ResolveViolationTypesAsync(string actionTag, CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.obligation", "ObligationService.ResolveViolationTypesAsync");
        var mapping = await _actionMappingStore.GetAsync($"mapping:{actionTag}", ct);
        if (mapping != null)
        {
            return mapping.ViolationTypes;
        }

        // 1:1 convention: action tag is used directly as violation type
        return new List<string> { actionTag };
    }

    // ========================================================================
    // Internal: Personality Enrichment (Soft L4 Dependency)
    // ========================================================================

    /// <summary>
    /// Attempts to retrieve personality traits for moral weighting via variable provider system.
    /// Returns null if personality data is unavailable (graceful degradation).
    /// </summary>
    private async Task<Dictionary<string, float>?> TryGetPersonalityTraitsAsync(
        Guid characterId, Guid realmId, Guid? locationId, CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.obligation", "ObligationService.TryGetPersonalityTraitsAsync");
        try
        {
            var personalityFactory = _providerFactories
                .FirstOrDefault(f => f.ProviderName == "personality");

            if (personalityFactory == null) return null;

            var provider = await personalityFactory.CreateAsync(characterId, realmId, locationId, ct);

            var traits = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
            foreach (var traitName in PersonalityTraitNames)
            {
                var value = provider.GetValue(new ReadOnlySpan<string>(new[] { traitName }));
                if (value is float f)
                {
                    traits[traitName] = f;
                }
            }

            return traits.Count > 0 ? traits : null;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex,
                "Failed to get personality traits for character {CharacterId}, proceeding without enrichment",
                characterId);
            return null;
        }
    }
}
