using BeyondImmersion.Bannou.Core;
using BeyondImmersion.Bannou.StorylineTheory.Archives;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Relationship;
using BeyondImmersion.BannouService.Resource;
using BeyondImmersion.BannouService.State;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Storyline;

// =============================================================================
// StorylineService — Private & Internal Helper Methods
// =============================================================================
//
// This partial class file is the designated home for all private and internal
// helper methods used by StorylineService. This separation exists to
// support the IMPLEMENTATION TENETS telemetry span rules:
//
//   - PRIMARY FILE (StorylineService.cs):
//     Contains ONLY public interface method implementations (the methods
//     declared in IStorylineService). These methods MUST NOT call
//     ITelemetryProvider.StartActivity because the generated controller
//     already wraps each endpoint with a telemetry span. Adding a span
//     in the service method would double-instrument the endpoint.
//
//   - THIS FILE (StorylineService.Helpers.cs):
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
/// Private and internal helper methods for StorylineService.
/// </summary>
/// <remarks>
/// <para>
/// This partial class contains all non-public helper methods. Every async method
/// in this file MUST include a <c>using var activity = _telemetryProvider.StartActivity(...)</c>
/// span per IMPLEMENTATION TENETS (T30). The generated controller instruments the
/// public interface methods; this file instruments the sub-operations.
/// </para>
/// </remarks>
public partial class StorylineService
{

    /// <summary>
    /// Fetches archive and snapshot data from the Resource service.
    /// </summary>
    private async Task<(ArchiveBundle bundle, List<Guid> archiveIds, List<Guid> snapshotIds, string? error)>
        FetchSeedDataAsync(ICollection<SeedSource> seedSources, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.storyline", "StorylineService.FetchSeedDataAsync");
        var bundle = new ArchiveBundle();
        var archiveIds = new List<Guid>();
        var snapshotIds = new List<Guid>();

        foreach (var source in seedSources)
        {
            if (source.ArchiveId.HasValue)
            {
                var archiveId = source.ArchiveId.Value;
                archiveIds.Add(archiveId);

                try
                {
                    var response = await _resourceClient.GetArchiveAsync(new GetArchiveRequest
                    {
                        ResourceType = "character",
                        ResourceId = archiveId,
                        ArchiveId = archiveId
                    }, cancellationToken);

                    if (!response.Found || response.Archive == null)
                    {
                        return (bundle, archiveIds, snapshotIds, $"Archive {archiveId} not found");
                    }

                    // Convert archive entries to ArchiveBundle
                    PopulateBundleFromEntries(bundle, response.Archive.Entries, archiveId);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to fetch archive {ArchiveId}", archiveId);
                    return (bundle, archiveIds, snapshotIds, $"Failed to fetch archive {archiveId}: {ex.Message}");
                }
            }
            else if (source.SnapshotId.HasValue)
            {
                var snapshotId = source.SnapshotId.Value;
                snapshotIds.Add(snapshotId);

                try
                {
                    var response = await _resourceClient.GetSnapshotAsync(new GetSnapshotRequest
                    {
                        SnapshotId = snapshotId
                    }, cancellationToken);

                    if (!response.Found || response.Snapshot == null)
                    {
                        return (bundle, archiveIds, snapshotIds, $"Snapshot {snapshotId} not found");
                    }

                    // Convert snapshot entries to ArchiveBundle
                    PopulateBundleFromEntries(bundle, response.Snapshot.Entries, snapshotId);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to fetch snapshot {SnapshotId}", snapshotId);
                    return (bundle, archiveIds, snapshotIds, $"Failed to fetch snapshot {snapshotId}: {ex.Message}");
                }
            }
            else
            {
                return (bundle, archiveIds, snapshotIds, "Seed source must have either archiveId or snapshotId");
            }
        }

        return (bundle, archiveIds, snapshotIds, null);
    }

    /// <summary>
    /// Updates the plan index for realm-based queries.
    /// </summary>
    private async Task UpdatePlanIndexAsync(
        Guid planId,
        Guid realmId,
        CachedPlan plan,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.storyline", "StorylineService.UpdatePlanIndexAsync");
        var indexKey = BuildPlanIndexKey(realmId);
        var score = plan.CreatedAt.ToUnixTimeSeconds();

        await _planIndexStore.SortedSetAddAsync(
            indexKey,
            BuildPlanKey(planId),
            score,
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Publishes the storyline.composed event.
    /// </summary>
    private async Task PublishComposedEventAsync(
        Guid planId,
        ComposeRequest request,
        ComposeResponse response,
        List<Guid> archiveIds,
        List<Guid> snapshotIds,
        int generationTimeMs,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.storyline", "StorylineService.PublishComposedEventAsync");
        var composedEvent = new StorylinePlanComposedEvent
        {
            PlanId = planId,
            RealmId = request.Constraints?.RealmId,
            Goal = request.Goal,
            ArcType = response.ArcType,
            PrimarySpectrum = response.PrimarySpectrum,
            Confidence = response.Confidence,
            Genre = response.Genre,
            ArchiveIds = archiveIds,
            SnapshotIds = snapshotIds,
            PhaseCount = response.Phases.Count,
            EntityCount = response.EntitiesToSpawn?.Count,
            GenerationTimeMs = generationTimeMs,
            Cached = response.Cached,
            Seed = request.Seed,
            ComposedAt = DateTimeOffset.UtcNow
        };

        await _messageBus.PublishStorylinePlanComposedAsync(composedEvent, cancellationToken);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // SCENARIO HELPERS
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Gets a scenario definition with Redis cache-first lookup.
    /// </summary>
    private async Task<ScenarioDefinitionModel?> GetScenarioDefinitionWithCacheAsync(
        Guid scenarioId,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.storyline", "StorylineService.GetScenarioDefinitionWithCacheAsync");
        var key = BuildScenarioDefinitionKey(scenarioId);

        // Try cache first
        var cached = await _scenarioCacheStore.GetAsync(key, cancellationToken);
        if (cached is not null)
        {
            return cached;
        }

        // Fall back to MySQL
        var definition = await _scenarioDefinitionStore.GetAsync(key, cancellationToken);
        if (definition is not null)
        {
            // Populate cache
            await _scenarioCacheStore.SaveAsync(
                key,
                definition,
                new StateOptions { Ttl = _configuration.ScenarioDefinitionCacheTtlSeconds },
                cancellationToken);
        }

        return definition;
    }

    /// <summary>
    /// Finds a scenario definition by code within scope.
    /// </summary>
    private async Task<ScenarioDefinitionModel?> FindScenarioByCodeAsync(
        string code,
        Guid? realmId,
        Guid? gameServiceId,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.storyline", "StorylineService.FindScenarioByCodeAsync");
        var normalizedCode = code.ToUpperInvariant();

        // Query with code filter (MySQL handles case-insensitive comparison)
        var matchingDefinitions = await _scenarioDefinitionStore.QueryAsync(
            d => d.Code == normalizedCode,
            cancellationToken);

        // Apply additional scope filters in memory
        return matchingDefinitions.FirstOrDefault(d =>
            (!realmId.HasValue || !d.RealmId.HasValue || d.RealmId == realmId) &&
            (!gameServiceId.HasValue || !d.GameServiceId.HasValue || d.GameServiceId == gameServiceId));
    }

    /// <summary>
    /// Applies a mutation via appropriate service client.
    /// Uses soft dependency pattern for L4 peers.
    /// </summary>
    private async Task<(bool success, string? details)> ApplyMutationAsync(
        ScenarioMutation mutation,
        Guid characterId,
        IDictionary<string, Guid>? additionalParticipants,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.storyline", "StorylineService.ApplyMutationAsync");
        try
        {
            switch (mutation.MutationType)
            {
                case MutationType.PersonalityEvolve:
                    {
                        // Soft L4 dependency - graceful degradation
                        var client = _serviceProvider.GetService<CharacterPersonality.ICharacterPersonalityClient>();
                        if (client is null)
                        {
                            _logger.LogDebug("Character personality service unavailable, skipping personality mutation");
                            return (false, "Character personality service unavailable");
                        }

                        if (!mutation.ExperienceType.HasValue)
                        {
                            return (false, "Missing experience type for personality mutation");
                        }

                        // Map Storyline enum to CharacterPersonality enum at service boundary
                        var experienceType = mutation.ExperienceType.Value
                            .MapByName<StorylineExperienceType, CharacterPersonality.ExperienceType>();

                        try
                        {
                            await client.RecordExperienceAsync(
                                new CharacterPersonality.RecordExperienceRequest
                                {
                                    CharacterId = characterId,
                                    ExperienceType = experienceType,
                                    Intensity = mutation.ExperienceIntensity ?? 0.5f
                                }, cancellationToken);

                            return (true, "Personality evolved");
                        }
                        catch (Bannou.Core.ApiException ex)
                        {
                            _logger.LogWarning(ex, "Failed to record personality experience");
                            return (false, $"Failed: {ex.StatusCode}");
                        }
                    }

                case MutationType.BackstoryAdd:
                    {
                        // Soft L4 dependency - graceful degradation
                        var client = _serviceProvider.GetService<CharacterHistory.ICharacterHistoryClient>();
                        if (client is null)
                        {
                            _logger.LogDebug("Character history service unavailable, skipping backstory mutation");
                            return (false, "Character history service unavailable");
                        }

                        if (!mutation.BackstoryElementType.HasValue || string.IsNullOrEmpty(mutation.BackstoryKey) || string.IsNullOrEmpty(mutation.BackstoryValue))
                        {
                            return (false, "Missing backstory element type, key, or value");
                        }

                        // Map Storyline enum to CharacterHistory enum at service boundary
                        var elementType = mutation.BackstoryElementType.Value
                            .MapByName<StorylineBackstoryElementType, CharacterHistory.BackstoryElementType>();

                        try
                        {
                            await client.AddBackstoryElementAsync(
                                new CharacterHistory.AddBackstoryElementRequest
                                {
                                    CharacterId = characterId,
                                    Element = new CharacterHistory.BackstoryElement
                                    {
                                        ElementType = elementType,
                                        Key = mutation.BackstoryKey,
                                        Value = mutation.BackstoryValue,
                                        Strength = mutation.BackstoryStrength ?? 0.5f
                                    }
                                }, cancellationToken);

                            return (true, "Backstory added");
                        }
                        catch (Bannou.Core.ApiException ex)
                        {
                            _logger.LogWarning(ex, "Failed to add backstory element");
                            return (false, $"Failed: {ex.StatusCode}");
                        }
                    }

                case MutationType.RelationshipCreate:
                    {
                        // Hard L2 dependency - should always be available
                        if (string.IsNullOrEmpty(mutation.RelationshipTypeCode))
                        {
                            return (false, "Missing relationship type code");
                        }

                        // Get other participant from additionalParticipants
                        Guid? otherEntityId = null;
                        if (!string.IsNullOrEmpty(mutation.OtherParticipantRole) &&
                            additionalParticipants?.TryGetValue(mutation.OtherParticipantRole, out var participantId) == true)
                        {
                            otherEntityId = participantId;
                        }

                        if (!otherEntityId.HasValue)
                        {
                            return (false, $"No participant found for role {mutation.OtherParticipantRole}");
                        }

                        try
                        {
                            // Resolve relationship type code to ID
                            var relationshipType = await _relationshipClient.GetRelationshipTypeByCodeAsync(
                                new GetRelationshipTypeByCodeRequest { Code = mutation.RelationshipTypeCode },
                                cancellationToken);

                            // Create relationship with proper types
                            await _relationshipClient.CreateRelationshipAsync(
                                new CreateRelationshipRequest
                                {
                                    Entity1Id = characterId,
                                    Entity1Type = EntityType.Character,
                                    Entity2Id = otherEntityId.Value,
                                    Entity2Type = EntityType.Character,
                                    RelationshipTypeId = relationshipType.RelationshipTypeId,
                                    StartedAt = DateTimeOffset.UtcNow
                                }, cancellationToken);

                            return (true, "Relationship created");
                        }
                        catch (Bannou.Core.ApiException ex)
                        {
                            _logger.LogWarning(ex, "Failed to create relationship");
                            return (false, $"Failed: {ex.StatusCode}");
                        }
                    }

                case MutationType.RelationshipEnd:
                    {
                        // Hard L2 dependency - should always be available
                        if (string.IsNullOrEmpty(mutation.RelationshipTypeCode))
                        {
                            return (false, "Missing relationship type code");
                        }

                        Guid? otherEntityId = null;
                        if (!string.IsNullOrEmpty(mutation.OtherParticipantRole) &&
                            additionalParticipants?.TryGetValue(mutation.OtherParticipantRole, out var participantId) == true)
                        {
                            otherEntityId = participantId;
                        }

                        if (!otherEntityId.HasValue)
                        {
                            return (false, $"No participant found for role {mutation.OtherParticipantRole}");
                        }

                        try
                        {
                            // Resolve relationship type code to ID
                            var relationshipType = await _relationshipClient.GetRelationshipTypeByCodeAsync(
                                new GetRelationshipTypeByCodeRequest { Code = mutation.RelationshipTypeCode },
                                cancellationToken);

                            // Find relationships between the two characters of the specified type
                            var relationships = await _relationshipClient.GetRelationshipsBetweenAsync(
                                new GetRelationshipsBetweenRequest
                                {
                                    Entity1Id = characterId,
                                    Entity1Type = EntityType.Character,
                                    Entity2Id = otherEntityId.Value,
                                    Entity2Type = EntityType.Character,
                                    RelationshipTypeId = relationshipType.RelationshipTypeId,
                                    IncludeEnded = false
                                }, cancellationToken);

                            // Find the active relationship to end
                            var activeRelationship = relationships.Relationships.FirstOrDefault(r => r.EndedAt is null);
                            if (activeRelationship is null)
                            {
                                return (false, "Active relationship not found");
                            }

                            // End the relationship
                            await _relationshipClient.EndRelationshipAsync(
                                new EndRelationshipRequest
                                {
                                    RelationshipId = activeRelationship.RelationshipId,
                                    EndedAt = DateTimeOffset.UtcNow
                                }, cancellationToken);

                            return (true, "Relationship ended");
                        }
                        catch (Bannou.Core.ApiException ex)
                        {
                            _logger.LogWarning(ex, "Failed to end relationship");
                            return (false, $"Failed: {ex.StatusCode}");
                        }
                    }

                case MutationType.Custom:
                    // Custom mutations not executed server-side
                    return (true, "Custom mutation - caller must handle");

                default:
                    return (false, $"Unknown mutation type: {mutation.MutationType}");
            }
        }
        catch (ApiException ex)
        {
            _logger.LogWarning(ex, "API exception during mutation application");
            return (false, $"API error: {ex.StatusCode}");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error applying mutation");
            return (false, ex.Message);
        }
    }

    /// <summary>
    /// Spawns a quest via quest client (soft L4 dependency).
    /// </summary>
    private async Task<(Guid? questId, bool success)> SpawnQuestAsync(
        ScenarioQuestHook hook,
        Guid characterId,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.storyline", "StorylineService.SpawnQuestAsync");
        try
        {
            // Soft L4 dependency - graceful degradation
            var client = _serviceProvider.GetService<Quest.IQuestClient>();
            if (client is null)
            {
                _logger.LogDebug("Quest service unavailable, skipping quest spawn for {QuestCode}", hook.QuestCode);
                return (null, false);
            }

            // Phase 1: No delayed spawning, spawn immediately
            if (hook.DelaySeconds > 0)
            {
                _logger.LogDebug("Delayed quest spawning not implemented, spawning {QuestCode} immediately", hook.QuestCode);
            }

            // Use AcceptQuestAsync with quest code to spawn the quest
            var response = await client.AcceptQuestAsync(
                new Quest.AcceptQuestRequest
                {
                    Code = hook.QuestCode,
                    QuestorCharacterId = characterId,
                    TermOverrides = hook.TermOverrides
                }, cancellationToken);

            return (response.QuestInstanceId, true);
        }
        catch (Bannou.Core.ApiException ex)
        {
            _logger.LogWarning(ex, "API exception spawning quest {QuestCode}: {Status}", hook.QuestCode, ex.StatusCode);
            return (null, false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error spawning quest {QuestCode}", hook.QuestCode);
            return (null, false);
        }
    }
}
