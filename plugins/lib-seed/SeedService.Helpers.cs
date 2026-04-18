using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Character;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Helpers;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Providers;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using BeyondImmersion.BannouService.Worldstate;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace BeyondImmersion.BannouService.Seed;

// =============================================================================
// SeedService — Private & Internal Helper Methods
// =============================================================================
//
// This partial class file is the designated home for all private and internal
// helper methods used by SeedService. This separation exists to
// support the IMPLEMENTATION TENETS telemetry span rules:
//
//   - PRIMARY FILE (SeedService.cs):
//     Contains ONLY public interface method implementations (the methods
//     declared in ISeedService). These methods MUST NOT call
//     ITelemetryProvider.StartActivity because the generated controller
//     already wraps each endpoint with a telemetry span. Adding a span
//     in the service method would double-instrument the endpoint.
//
//   - THIS FILE (SeedService.Helpers.cs):
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
/// Private and internal helper methods for SeedService.
/// </summary>
/// <remarks>
/// <para>
/// This partial class contains all non-public helper methods. Every async method
/// in this file MUST include a <c>using var activity = _telemetryProvider.StartActivity(...)</c>
/// span per IMPLEMENTATION TENETS (T30). The generated controller instruments the
/// public interface methods; this file instruments the sub-operations.
/// </para>
/// </remarks>
public partial class SeedService
{
    /// <summary>
    /// Resolves the realm association for a seed based on owner type.
    /// Character → character's realmId, Realm → self, others → null.
    /// </summary>
    private async Task<Guid?> ResolveRealmForOwnerAsync(Guid ownerId, EntityType ownerType, CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.seed", "SeedService.ResolveRealmForOwner");

        if (ownerType == EntityType.Realm)
        {
            // Realm-owned seeds are self-evidently tied to their own time
            return ownerId;
        }

        if (ownerType == EntityType.Character)
        {
            try
            {
                var character = await _characterClient.GetCharacterAsync(
                    new BeyondImmersion.BannouService.Character.GetCharacterRequest { CharacterId = ownerId }, ct);
                return character?.RealmId;
            }
            catch (ApiException ex)
            {
                _logger.LogWarning(ex, "Could not resolve realm for character {CharacterId}, realmId will be null", ownerId);
                await _messageBus.TryPublishErrorAsync(
                    "seed", "ResolveRealmForOwner", "CharacterLookupFailed",
                    ex.Message, dependency: "character", endpoint: "get-character",
                    stack: ex.StackTrace, cancellationToken: ct);
                return null;
            }
        }

        // Account, Actor without character binding, etc. → no realm
        return null;
    }

    /// <summary>
    /// Internal helper for recording growth across one or more domains atomically.
    /// Handles locking, phase transitions, bond shared growth, and event publishing.
    /// </summary>
    private async Task<(StatusCodes, GrowthResponse?)> RecordGrowthInternalAsync(
        Guid seedId, (string Domain, float Amount)[] entries, string source, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.seed", "SeedService.RecordGrowthInternal");
        var lockOwner = $"growth-{Guid.NewGuid():N}";
        await using var lockResponse = await _lockProvider.LockAsync(
            StateStoreDefinitions.SeedLock, seedId.ToString(), lockOwner, _configuration.LockTimeoutSeconds, cancellationToken);

        if (!lockResponse.Success)
        {
            return (StatusCodes.Conflict, null);
        }

        var seed = await _seedStore.GetAsync($"seed:{seedId}", cancellationToken);

        if (seed == null)
        {
            return (StatusCodes.NotFound, null);
        }

        if (seed.Status != SeedStatus.Active)
        {
            _logger.LogWarning("Cannot record growth for non-active seed {SeedId} (status: {Status})",
                seedId, seed.Status);
            return (StatusCodes.BadRequest, null);
        }

        var growth = await _growthStore.GetAsync($"growth:{seedId}", cancellationToken);
        growth ??= new SeedGrowthModel { SeedId = seedId, Domains = new() };

        // Apply bond shared growth multiplier only when bond is active
        var multiplier = 1.0f;
        SeedBondModel? bond = null;
        if (seed.BondId.HasValue)
        {
            bond = await _bondStore.GetAsync($"bond:{seed.BondId.Value}", cancellationToken);
            if (bond is { Status: BondStatus.Active })
            {
                multiplier = (float)_configuration.BondSharedGrowthMultiplier;

                var totalAmount = entries.Sum(e => e.Amount);
                bond.SharedGrowth += totalAmount;
                bond.BondStrength += totalAmount * (float)_configuration.BondStrengthGrowthRate;
                await _bondStore.SaveAsync($"bond:{bond.BondId}", bond, cancellationToken: cancellationToken);
            }
        }

        // Record growth in each domain using per-domain tracking
        var now = DateTimeOffset.UtcNow;
        var domainChanges = new List<DomainChange>(entries.Length);
        foreach (var (domain, amount) in entries)
        {
            var adjustedAmount = amount * multiplier;
            var entry = growth.Domains.GetValueOrDefault(domain);
            var previousDepth = entry?.Depth ?? 0f;
            var newDepth = previousDepth + adjustedAmount;

            growth.Domains[domain] = new DomainGrowthEntry
            {
                Depth = newDepth,
                LastActivityAt = now,
                PeakDepth = Math.Max(entry?.PeakDepth ?? 0f, newDepth)
            };

            domainChanges.Add(new DomainChange(domain, previousDepth, newDepth));

            await _messageBus.PublishSeedGrowthUpdatedAsync(new SeedGrowthUpdatedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = now,
                SeedId = seedId,
                SeedTypeCode = seed.SeedTypeCode,
                Domain = domain,
                PreviousDepth = previousDepth,
                NewDepth = newDepth,
                TotalGrowth = growth.Domains.Values.Sum(d => d.Depth),
                CrossPollinated = false
            }, cancellationToken);
        }

        await _growthStore.SaveAsync($"growth:{seedId}", growth, cancellationToken: cancellationToken);

        // Dispatch growth notification to evolution listeners
        await SeedEvolutionDispatcher.DispatchGrowthRecordedAsync(
            _evolutionListeners, seed.SeedTypeCode,
            new SeedGrowthNotification(
                seed.SeedId, seed.SeedTypeCode, seed.OwnerId, seed.OwnerType,
                domainChanges, growth.Domains.Values.Sum(d => d.Depth),
                CrossPollinated: false, Source: source),
            _telemetryProvider, _logger, cancellationToken);

        // Reset partner's decay timer for exact domains when permanently bonded
        if (bond is { Status: BondStatus.Active, Permanent: true })
        {
            var affectedDomains = entries.Select(e => e.Domain).ToHashSet();
            foreach (var participant in bond.Participants.Where(p => p.SeedId != seedId))
            {
                var partnerGrowth = await _growthStore.GetAsync($"growth:{participant.SeedId}", cancellationToken);
                if (partnerGrowth == null) continue;

                var partnerModified = false;
                foreach (var domainKey in affectedDomains)
                {
                    if (partnerGrowth.Domains.TryGetValue(domainKey, out var partnerEntry))
                    {
                        partnerEntry.LastActivityAt = now;
                        partnerModified = true;
                    }
                }

                if (partnerModified)
                {
                    await _growthStore.SaveAsync($"growth:{participant.SeedId}", partnerGrowth,
                        cancellationToken: cancellationToken);
                }
            }
        }

        // Update seed total growth and check phase transition
        var newTotalGrowth = growth.Domains.Values.Sum(d => d.Depth);
        var previousPhase = seed.GrowthPhase;
        seed.TotalGrowth = newTotalGrowth;

        // Load type definition for phase check
        var seedType = await _typeStore.GetAsync(TypeKey(seed.GameServiceId, seed.SeedTypeCode), cancellationToken);

        if (seedType != null)
        {
            var (currentPhase, _) = ComputePhaseInfo(seedType.GrowthPhases, newTotalGrowth);
            seed.GrowthPhase = currentPhase.PhaseCode;
        }

        await _seedStore.SaveAsync($"seed:{seedId}", seed, cancellationToken: cancellationToken);

        // Publish phase change event if phase transitioned
        if (previousPhase != seed.GrowthPhase)
        {
            _logger.LogInformation("Seed {SeedId} transitioned from phase {OldPhase} to {NewPhase}",
                seedId, previousPhase, seed.GrowthPhase);

            await _messageBus.PublishSeedPhaseChangedAsync(new SeedPhaseChangedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                SeedId = seedId,
                SeedTypeCode = seed.SeedTypeCode,
                PreviousPhase = previousPhase,
                NewPhase = seed.GrowthPhase,
                TotalGrowth = newTotalGrowth,
                Direction = PhaseChangeDirection.Progressed
            }, cancellationToken);

            // Dispatch phase change notification to evolution listeners
            await SeedEvolutionDispatcher.DispatchPhaseChangedAsync(
                _evolutionListeners, seed.SeedTypeCode,
                new SeedPhaseNotification(
                    seed.SeedId, seed.SeedTypeCode, seed.OwnerId, seed.OwnerType,
                    previousPhase, seed.GrowthPhase, newTotalGrowth, Progressed: true),
                _telemetryProvider, _logger, cancellationToken);
        }

        // Invalidate capability cache so next read recomputes
        await _capabilityCacheStore.DeleteAsync($"cap:{seedId}", cancellationToken);

        // Cross-pollinate to same-type same-owner siblings if configured (uses raw amounts, not bond-boosted)
        if (seedType != null && seedType.SameOwnerGrowthMultiplier > 0f)
        {
            var crossPollEntries = entries
                .Select(e => (e.Domain, Amount: e.Amount * seedType.SameOwnerGrowthMultiplier))
                .ToArray();

            var siblingConditions = new List<QueryCondition>
            {
                new QueryCondition { Path = "$.OwnerId", Operator = QueryOperator.Equals, Value = seed.OwnerId.ToString() },
                new QueryCondition { Path = "$.OwnerType", Operator = QueryOperator.Equals, Value = seed.OwnerType },
                new QueryCondition { Path = "$.SeedTypeCode", Operator = QueryOperator.Equals, Value = seed.SeedTypeCode },
                GameServiceIdCondition(seed.GameServiceId),
                new QueryCondition { Path = "$.Status", Operator = QueryOperator.NotEquals, Value = SeedStatus.Archived.ToString() },
                new QueryCondition { Path = "$.SeedId", Operator = QueryOperator.Exists, Value = true }
            };

            var effectiveMaxPerOwner = seedType.MaxPerOwner > 0
                ? seedType.MaxPerOwner
                : _configuration.DefaultMaxSeedsPerOwner;
            var siblings = await _seedQueryStore.JsonQueryPagedAsync(
                siblingConditions, 0, effectiveMaxPerOwner + 1, null, cancellationToken);

            foreach (var sibling in siblings.Items)
            {
                if (sibling.Value.SeedId == seedId)
                    continue;

                try
                {
                    await ApplyCrossPollination(
                        sibling.Value.SeedId, crossPollEntries, seedType, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Cross-pollination failed for sibling seed {SiblingSeedId}", sibling.Value.SeedId);
                }
            }
        }

        return (StatusCodes.OK, new GrowthResponse
        {
            SeedId = seedId,
            TotalGrowth = newTotalGrowth,
            Domains = growth.Domains.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Depth)
        });
    }

    /// <summary>
    /// Applies cross-pollinated growth to a single sibling seed. Uses try-lock with
    /// short timeout -- failure is expected and graceful (best-effort semantics).
    /// Structurally cannot cascade: this method never queries for further siblings.
    /// Bond multiplier is NOT applied to cross-pollinated growth.
    /// </summary>
    private async Task ApplyCrossPollination(
        Guid siblingSeedId,
        (string Domain, float Amount)[] entries,
        SeedTypeDefinitionModel seedType,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.seed", "SeedService.ApplyCrossPollination");
        var lockOwner = $"crosspoll-{Guid.NewGuid():N}";
        await using var lockResponse = await _lockProvider.LockAsync(
            StateStoreDefinitions.SeedLock, siblingSeedId.ToString(), lockOwner, _configuration.CrossPollinationLockTimeoutSeconds, cancellationToken);

        if (!lockResponse.Success)
        {
            _logger.LogDebug("Cross-pollination skipped for seed {SeedId}: could not acquire lock", siblingSeedId);
            return;
        }

        var seed = await _seedStore.GetAsync($"seed:{siblingSeedId}", cancellationToken);

        if (seed == null || seed.Status == SeedStatus.Archived)
            return;

        var growth = await _growthStore.GetAsync($"growth:{siblingSeedId}", cancellationToken);
        growth ??= new SeedGrowthModel { SeedId = siblingSeedId, Domains = new() };

        var now = DateTimeOffset.UtcNow;
        var crossDomainChanges = new List<DomainChange>(entries.Length);
        foreach (var (domain, amount) in entries)
        {
            var entry = growth.Domains.GetValueOrDefault(domain);
            var previousDepth = entry?.Depth ?? 0f;
            var newDepth = previousDepth + amount;

            growth.Domains[domain] = new DomainGrowthEntry
            {
                Depth = newDepth,
                LastActivityAt = now,
                PeakDepth = Math.Max(entry?.PeakDepth ?? 0f, newDepth)
            };

            crossDomainChanges.Add(new DomainChange(domain, previousDepth, newDepth));

            await _messageBus.PublishSeedGrowthUpdatedAsync(new SeedGrowthUpdatedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = now,
                SeedId = siblingSeedId,
                SeedTypeCode = seedType.SeedTypeCode,
                Domain = domain,
                PreviousDepth = previousDepth,
                NewDepth = newDepth,
                TotalGrowth = growth.Domains.Values.Sum(d => d.Depth),
                CrossPollinated = true
            }, cancellationToken);
        }

        await _growthStore.SaveAsync($"growth:{siblingSeedId}", growth, cancellationToken: cancellationToken);

        // Dispatch cross-pollinated growth notification to evolution listeners
        await SeedEvolutionDispatcher.DispatchGrowthRecordedAsync(
            _evolutionListeners, seedType.SeedTypeCode,
            new SeedGrowthNotification(
                seed.SeedId, seedType.SeedTypeCode, seed.OwnerId, seed.OwnerType,
                crossDomainChanges, growth.Domains.Values.Sum(d => d.Depth),
                CrossPollinated: true, Source: "cross-pollination"),
            _telemetryProvider, _logger, cancellationToken);

        // Update seed total growth and check phase transition
        var newTotalGrowth = growth.Domains.Values.Sum(d => d.Depth);
        var previousPhase = seed.GrowthPhase;
        seed.TotalGrowth = newTotalGrowth;

        var (currentPhase, _) = ComputePhaseInfo(seedType.GrowthPhases, newTotalGrowth);
        seed.GrowthPhase = currentPhase.PhaseCode;

        await _seedStore.SaveAsync($"seed:{siblingSeedId}", seed, cancellationToken: cancellationToken);

        if (previousPhase != seed.GrowthPhase)
        {
            _logger.LogInformation(
                "Seed {SeedId} transitioned from phase {OldPhase} to {NewPhase} via cross-pollination",
                siblingSeedId, previousPhase, seed.GrowthPhase);

            await _messageBus.PublishSeedPhaseChangedAsync(new SeedPhaseChangedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                SeedId = siblingSeedId,
                SeedTypeCode = seedType.SeedTypeCode,
                PreviousPhase = previousPhase,
                NewPhase = seed.GrowthPhase,
                TotalGrowth = newTotalGrowth,
                Direction = PhaseChangeDirection.Progressed
            }, cancellationToken);

            // Dispatch phase change notification to evolution listeners
            await SeedEvolutionDispatcher.DispatchPhaseChangedAsync(
                _evolutionListeners, seedType.SeedTypeCode,
                new SeedPhaseNotification(
                    seed.SeedId, seedType.SeedTypeCode, seed.OwnerId, seed.OwnerType,
                    previousPhase, seed.GrowthPhase, newTotalGrowth, Progressed: true),
                _telemetryProvider, _logger, cancellationToken);
        }

        // Invalidate capability cache
        await _capabilityCacheStore.DeleteAsync($"cap:{siblingSeedId}", cancellationToken);
    }

    /// <summary>
    /// Recomputes phase assignments and capability caches for all seeds of a given type
    /// after a type definition update.
    /// </summary>
    private async Task RecomputeSeedsForTypeAsync(
        SeedTypeDefinitionModel seedType, bool phasesChanged, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.seed", "SeedService.RecomputeSeedsForType");
        var conditions = new List<QueryCondition>
        {
            GameServiceIdCondition(seedType.GameServiceId),
            new QueryCondition { Path = "$.SeedTypeCode", Operator = QueryOperator.Equals, Value = seedType.SeedTypeCode },
            new QueryCondition { Path = "$.SeedId", Operator = QueryOperator.Exists, Value = true }
        };

        var pageSize = _configuration.DefaultQueryPageSize;
        var offset = 0;
        var totalProcessed = 0;

        while (true)
        {
            var result = await _seedQueryStore.JsonQueryPagedAsync(conditions, offset, pageSize, null, cancellationToken);

            foreach (var item in result.Items)
            {
                var seed = item.Value;

                // Re-evaluate phase if thresholds changed
                if (phasesChanged)
                {
                    var previousPhase = seed.GrowthPhase;
                    var (currentPhase, _) = ComputePhaseInfo(seedType.GrowthPhases, seed.TotalGrowth);
                    seed.GrowthPhase = currentPhase.PhaseCode;

                    if (previousPhase != seed.GrowthPhase)
                    {
                        await _seedStore.SaveAsync($"seed:{seed.SeedId}", seed, cancellationToken: cancellationToken);

                        // Determine direction based on phase threshold comparison
                        var prevPhaseThreshold = seedType.GrowthPhases.FirstOrDefault(p => p.PhaseCode == previousPhase)?.MinTotalGrowth ?? 0f;
                        var newPhaseThreshold = seedType.GrowthPhases.FirstOrDefault(p => p.PhaseCode == seed.GrowthPhase)?.MinTotalGrowth ?? 0f;
                        var direction = newPhaseThreshold >= prevPhaseThreshold
                            ? PhaseChangeDirection.Progressed
                            : PhaseChangeDirection.Regressed;

                        await _messageBus.PublishSeedPhaseChangedAsync(new SeedPhaseChangedEvent
                        {
                            EventId = Guid.NewGuid(),
                            Timestamp = DateTimeOffset.UtcNow,
                            SeedId = seed.SeedId,
                            SeedTypeCode = seed.SeedTypeCode,
                            PreviousPhase = previousPhase,
                            NewPhase = seed.GrowthPhase,
                            TotalGrowth = seed.TotalGrowth,
                            Direction = direction
                        }, cancellationToken);

                        // Dispatch phase change notification to evolution listeners
                        await SeedEvolutionDispatcher.DispatchPhaseChangedAsync(
                            _evolutionListeners, seed.SeedTypeCode,
                            new SeedPhaseNotification(
                                seed.SeedId, seed.SeedTypeCode, seed.OwnerId, seed.OwnerType,
                                previousPhase, seed.GrowthPhase, seed.TotalGrowth,
                                Progressed: direction == PhaseChangeDirection.Progressed),
                            _telemetryProvider, _logger, cancellationToken);
                    }
                }

                // Invalidate capability cache so next read recomputes with new rules
                await _capabilityCacheStore.DeleteAsync($"cap:{seed.SeedId}", cancellationToken);
            }

            totalProcessed += result.Items.Count;
            offset += pageSize;

            if (result.Items.Count < pageSize || totalProcessed >= result.TotalCount)
            {
                break;
            }
        }

        _logger.LogInformation("Recomputed {Count} seeds for type {SeedTypeCode} after type definition update",
            totalProcessed, seedType.SeedTypeCode);
    }

    /// <summary>
    /// Internal implementation for transferring a proportion of growth from one seed to another.
    /// Both seeds must be active and of the same type. Uses ordered dual-lock and Redis idempotency.
    /// </summary>
    private async Task<(StatusCodes, TransferGrowthResponse?)> TransferGrowthInternalAsync(
        TransferGrowthRequest body, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.seed", "SeedService.TransferGrowthInternal");

        if (body.SourceSeedId == body.TargetSeedId)
        {
            _logger.LogWarning("Cannot transfer growth from seed {SeedId} to itself", body.SourceSeedId);
            return (StatusCodes.BadRequest, null);
        }

        // Idempotency check
        var existingResult = await _idempotencyStore.GetAsync(body.TransferReferenceId.ToString(), cancellationToken);
        if (!string.IsNullOrEmpty(existingResult))
        {
            _logger.LogDebug("Transfer reference {ReferenceId} already processed, returning idempotent OK",
                body.TransferReferenceId);
            return (StatusCodes.OK, BannouJson.Deserialize<TransferGrowthResponse>(existingResult));
        }

        // Ordered dual-lock to prevent deadlocks (smaller GUID first)
        var orderedIds = new[] { body.SourceSeedId, body.TargetSeedId }.OrderBy(id => id).ToArray();
        var lockOwner = $"transfer-{Guid.NewGuid():N}";

        await using var lock1 = await _lockProvider.LockAsync(
            StateStoreDefinitions.SeedLock, orderedIds[0].ToString(), lockOwner, _configuration.LockTimeoutSeconds, cancellationToken);
        if (!lock1.Success)
            return (StatusCodes.Conflict, null);

        await using var lock2 = await _lockProvider.LockAsync(
            StateStoreDefinitions.SeedLock, orderedIds[1].ToString(), lockOwner, _configuration.LockTimeoutSeconds, cancellationToken);
        if (!lock2.Success)
            return (StatusCodes.Conflict, null);

        // Read both seeds
        var source = await _seedStore.GetAsync($"seed:{body.SourceSeedId}", cancellationToken);
        if (source == null)
            return (StatusCodes.NotFound, null);

        var target = await _seedStore.GetAsync($"seed:{body.TargetSeedId}", cancellationToken);
        if (target == null)
            return (StatusCodes.NotFound, null);

        // Validate both seeds
        if (source.Status != SeedStatus.Active)
        {
            _logger.LogWarning("Cannot transfer from non-active source seed {SeedId} (status: {Status})",
                body.SourceSeedId, source.Status);
            return (StatusCodes.BadRequest, null);
        }

        if (target.Status != SeedStatus.Active)
        {
            _logger.LogWarning("Cannot transfer to non-active target seed {SeedId} (status: {Status})",
                body.TargetSeedId, target.Status);
            return (StatusCodes.BadRequest, null);
        }

        if (source.SeedTypeCode != target.SeedTypeCode)
        {
            _logger.LogWarning("Cannot transfer between different seed types: {SourceType} vs {TargetType}",
                source.SeedTypeCode, target.SeedTypeCode);
            return (StatusCodes.BadRequest, null);
        }

        if (source.GameServiceId != target.GameServiceId)
        {
            _logger.LogWarning("Cannot transfer between seeds in different game services");
            return (StatusCodes.BadRequest, null);
        }

        // Read growth data
        var sourceGrowth = await _growthStore.GetAsync($"growth:{body.SourceSeedId}", cancellationToken);
        if (sourceGrowth == null)
            return (StatusCodes.NotFound, null);

        var targetGrowth = await _growthStore.GetAsync($"growth:{body.TargetSeedId}", cancellationToken);
        targetGrowth ??= new SeedGrowthModel { SeedId = body.TargetSeedId, Domains = new() };

        // Transfer growth across all domains
        var now = DateTimeOffset.UtcNow;
        var domainsTransferred = 0;
        var targetDomainChanges = new List<DomainChange>();

        foreach (var (domain, sourceEntry) in sourceGrowth.Domains)
        {
            var transferAmount = sourceEntry.Depth * (float)body.Proportion;
            if (transferAmount <= 0f)
                continue;

            domainsTransferred++;

            // Deduct from source
            sourceEntry.Depth -= transferAmount;

            // Add to target
            var targetEntry = targetGrowth.Domains.GetValueOrDefault(domain);
            var targetPreviousDepth = targetEntry?.Depth ?? 0f;
            var targetNewDepth = targetPreviousDepth + transferAmount;

            targetGrowth.Domains[domain] = new DomainGrowthEntry
            {
                Depth = targetNewDepth,
                LastActivityAt = now,
                PeakDepth = Math.Max(targetEntry?.PeakDepth ?? 0f, targetNewDepth)
            };

            targetDomainChanges.Add(new DomainChange(domain, targetPreviousDepth, targetNewDepth));

            await _messageBus.PublishSeedGrowthUpdatedAsync(new SeedGrowthUpdatedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = now,
                SeedId = body.TargetSeedId,
                SeedTypeCode = target.SeedTypeCode,
                Domain = domain,
                PreviousDepth = targetPreviousDepth,
                NewDepth = targetNewDepth,
                TotalGrowth = targetGrowth.Domains.Values.Sum(d => d.Depth),
                CrossPollinated = false
            }, cancellationToken);
        }

        // Save growth data
        await _growthStore.SaveAsync($"growth:{body.SourceSeedId}", sourceGrowth, cancellationToken: cancellationToken);
        await _growthStore.SaveAsync($"growth:{body.TargetSeedId}", targetGrowth, cancellationToken: cancellationToken);

        // Recompute source totals and phase
        var sourceTotalGrowth = sourceGrowth.Domains.Values.Sum(d => d.Depth);
        var sourcePreviousPhase = source.GrowthPhase;
        source.TotalGrowth = sourceTotalGrowth;

        var seedType = await _typeStore.GetAsync(TypeKey(source.GameServiceId, source.SeedTypeCode), cancellationToken);

        if (seedType != null)
        {
            var (sourcePhase, _) = ComputePhaseInfo(seedType.GrowthPhases, sourceTotalGrowth);
            source.GrowthPhase = sourcePhase.PhaseCode;
        }

        await _seedStore.SaveAsync($"seed:{body.SourceSeedId}", source, cancellationToken: cancellationToken);

        if (sourcePreviousPhase != source.GrowthPhase)
        {
            _logger.LogInformation("Source seed {SeedId} regressed from phase {OldPhase} to {NewPhase} after transfer",
                body.SourceSeedId, sourcePreviousPhase, source.GrowthPhase);

            await _messageBus.PublishSeedPhaseChangedAsync(new SeedPhaseChangedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = now,
                SeedId = body.SourceSeedId,
                SeedTypeCode = source.SeedTypeCode,
                PreviousPhase = sourcePreviousPhase,
                NewPhase = source.GrowthPhase,
                TotalGrowth = sourceTotalGrowth,
                Direction = PhaseChangeDirection.Regressed
            }, cancellationToken);

            await SeedEvolutionDispatcher.DispatchPhaseChangedAsync(
                _evolutionListeners, source.SeedTypeCode,
                new SeedPhaseNotification(
                    source.SeedId, source.SeedTypeCode, source.OwnerId, source.OwnerType,
                    sourcePreviousPhase, source.GrowthPhase, sourceTotalGrowth, Progressed: false),
                _telemetryProvider, _logger, cancellationToken);
        }

        await _capabilityCacheStore.DeleteAsync($"cap:{body.SourceSeedId}", cancellationToken);

        // Recompute target totals and phase
        var targetTotalGrowth = targetGrowth.Domains.Values.Sum(d => d.Depth);
        var targetPreviousPhase = target.GrowthPhase;
        target.TotalGrowth = targetTotalGrowth;

        if (seedType != null)
        {
            var (targetPhase, _) = ComputePhaseInfo(seedType.GrowthPhases, targetTotalGrowth);
            target.GrowthPhase = targetPhase.PhaseCode;
        }

        await _seedStore.SaveAsync($"seed:{body.TargetSeedId}", target, cancellationToken: cancellationToken);

        if (targetPreviousPhase != target.GrowthPhase)
        {
            _logger.LogInformation("Target seed {SeedId} progressed from phase {OldPhase} to {NewPhase} after transfer",
                body.TargetSeedId, targetPreviousPhase, target.GrowthPhase);

            await _messageBus.PublishSeedPhaseChangedAsync(new SeedPhaseChangedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = now,
                SeedId = body.TargetSeedId,
                SeedTypeCode = target.SeedTypeCode,
                PreviousPhase = targetPreviousPhase,
                NewPhase = target.GrowthPhase,
                TotalGrowth = targetTotalGrowth,
                Direction = PhaseChangeDirection.Progressed
            }, cancellationToken);

            await SeedEvolutionDispatcher.DispatchPhaseChangedAsync(
                _evolutionListeners, target.SeedTypeCode,
                new SeedPhaseNotification(
                    target.SeedId, target.SeedTypeCode, target.OwnerId, target.OwnerType,
                    targetPreviousPhase, target.GrowthPhase, targetTotalGrowth, Progressed: true),
                _telemetryProvider, _logger, cancellationToken);
        }

        await _capabilityCacheStore.DeleteAsync($"cap:{body.TargetSeedId}", cancellationToken);

        // Dispatch growth notifications for target
        if (targetDomainChanges.Count > 0)
        {
            await SeedEvolutionDispatcher.DispatchGrowthRecordedAsync(
                _evolutionListeners, target.SeedTypeCode,
                new SeedGrowthNotification(
                    target.SeedId, target.SeedTypeCode, target.OwnerId, target.OwnerType,
                    targetDomainChanges, targetTotalGrowth,
                    CrossPollinated: false, Source: "transfer"),
                _telemetryProvider, _logger, cancellationToken);
        }

        // Publish transfer event
        await _messageBus.PublishSeedGrowthTransferredAsync(new SeedGrowthTransferredEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = now,
            SourceSeedId = body.SourceSeedId,
            TargetSeedId = body.TargetSeedId,
            SeedTypeCode = source.SeedTypeCode,
            Proportion = (float)body.Proportion,
            DomainsTransferred = domainsTransferred,
            TransferReferenceId = body.TransferReferenceId,
            SourceTotalGrowth = sourceTotalGrowth,
            TargetTotalGrowth = targetTotalGrowth
        }, cancellationToken);

        var response = new TransferGrowthResponse
        {
            SourceSeedId = body.SourceSeedId,
            TargetSeedId = body.TargetSeedId,
            DomainsTransferred = domainsTransferred,
            SourceTotalGrowth = sourceTotalGrowth,
            TargetTotalGrowth = targetTotalGrowth
        };

        // Record idempotency key with TTL
        await _idempotencyStore.SaveAsync(
            body.TransferReferenceId.ToString(),
            BannouJson.Serialize(response),
            new StateOptions { Ttl = _configuration.IdempotencyTtlSeconds },
            cancellationToken);

        _logger.LogInformation(
            "Transferred growth from seed {SourceSeedId} to {TargetSeedId} (proportion: {Proportion}, domains: {Domains})",
            body.SourceSeedId, body.TargetSeedId, body.Proportion, domainsTransferred);

        return (StatusCodes.OK, response);
    }

    /// <summary>
    /// Computes a capability manifest from growth domains and seed type rules, then caches in Redis.
    /// </summary>
    private async Task<CapabilityManifestModel> ComputeAndCacheManifestAsync(SeedModel seed, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.seed", "SeedService.ComputeAndCacheManifest");
        var growth = await _growthStore.GetAsync($"growth:{seed.SeedId}", cancellationToken);
        var domains = growth?.Domains ?? new Dictionary<string, DomainGrowthEntry>();

        var seedType = await _typeStore.GetAsync(TypeKey(seed.GameServiceId, seed.SeedTypeCode), cancellationToken);

        var capabilities = new List<CapabilityEntry>();
        if (seedType?.CapabilityRules != null)
        {
            foreach (var rule in seedType.CapabilityRules)
            {
                var domainDepth = domains.GetValueOrDefault(rule.Domain)?.Depth ?? 0f;
                var unlocked = domainDepth >= rule.UnlockThreshold;
                var fidelity = unlocked ? ComputeFidelity(domainDepth, rule.UnlockThreshold, rule.FidelityFormula) : 0f;

                capabilities.Add(new CapabilityEntry
                {
                    CapabilityCode = rule.CapabilityCode,
                    Domain = rule.Domain,
                    Fidelity = fidelity,
                    Unlocked = unlocked
                });
            }
        }

        // Load existing manifest to increment version
        var existing = await _capabilityCacheStore.GetAsync($"cap:{seed.SeedId}", cancellationToken);

        var manifest = new CapabilityManifestModel
        {
            SeedId = seed.SeedId,
            SeedTypeCode = seed.SeedTypeCode,
            ComputedAt = DateTimeOffset.UtcNow,
            Version = (existing?.Version ?? 0) + 1,
            Capabilities = capabilities
        };

        await _capabilityCacheStore.SaveAsync($"cap:{seed.SeedId}", manifest, cancellationToken: cancellationToken);

        await _messageBus.PublishSeedCapabilityUpdatedAsync(new SeedCapabilityUpdatedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = manifest.ComputedAt,
            SeedId = seed.SeedId,
            SeedTypeCode = seed.SeedTypeCode,
            Version = manifest.Version,
            CapabilityCount = capabilities.Count(c => c.Unlocked)
        }, cancellationToken);

        // Dispatch capability notification to evolution listeners
        await SeedEvolutionDispatcher.DispatchCapabilitiesChangedAsync(
            _evolutionListeners, seed.SeedTypeCode,
            new SeedCapabilityNotification(
                seed.SeedId, seed.SeedTypeCode, seed.OwnerId, seed.OwnerType,
                manifest.Version, capabilities.Count(c => c.Unlocked),
                capabilities.Select(c => new CapabilitySnapshot(
                    c.CapabilityCode, c.Domain, c.Fidelity, c.Unlocked)).ToList()),
            _telemetryProvider, _logger, cancellationToken);

        return manifest;
    }
}
