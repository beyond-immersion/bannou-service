using BeyondImmersion.Bannou.Core;
using BeyondImmersion.Bannou.StorylineStoryteller.Actions;
using BeyondImmersion.Bannou.StorylineStoryteller.Composition;
using BeyondImmersion.Bannou.StorylineStoryteller.Planning;
using BeyondImmersion.Bannou.StorylineStoryteller.Templates;
using BeyondImmersion.Bannou.StorylineTheory.Actants;
using BeyondImmersion.Bannou.StorylineTheory.Archives;
using BeyondImmersion.Bannou.StorylineTheory.Arcs;
using BeyondImmersion.Bannou.StorylineTheory.Spectrums;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.CharacterHistory;
using BeyondImmersion.BannouService.CharacterPersonality;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Quest;
using BeyondImmersion.BannouService.Relationship;
using BeyondImmersion.BannouService.Resource;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("lib-storyline.tests")]

namespace BeyondImmersion.BannouService.Storyline;

/// <summary>
/// Implementation of the Storyline service.
/// Wraps storyline SDKs to provide HTTP endpoints for seeded narrative generation from compressed archives.
/// </summary>
/// <remarks>
/// <para>
/// <b>FOUNDATION TENETS - PARTIAL CLASS REQUIRED:</b> This class MUST remain a partial class.
/// Generated code (event handlers, permissions) is placed in companion partial classes.
/// </para>
/// <para>
/// <b>IMPLEMENTATION TENETS CHECKLIST:</b>
/// <list type="bullet">
///   <item><b>Type Safety:</b> Internal POCOs MUST use proper C# types (enums, Guids, DateTimeOffset) - never string representations. No Enum.Parse in business logic.</item>
///   <item><b>Configuration:</b> ALL config properties in StorylineServiceConfiguration MUST be wired up. No hardcoded magic numbers for tunables.</item>
///   <item><b>Events:</b> ALL meaningful state changes MUST publish typed events, even without current consumers.</item>
///   <item><b>Cache Stores:</b> If state-stores.yaml defines cache stores for this service, implement read-through/write-through caching.</item>
///   <item><b>Concurrency:</b> Use GetWithETagAsync + TrySaveAsync for list/index operations. No non-atomic read-modify-write.</item>
/// </list>
/// </para>
/// <para>
/// <b>RELATED FILES:</b>
/// <list type="bullet">
///   <item>Request/Response models: bannou-service/Generated/Models/StorylineModels.cs</item>
///   <item>Event models: bannou-service/Generated/Events/StorylineEventsModels.cs</item>
///   <item>Lifecycle events: bannou-service/Generated/Events/StorylineLifecycleEvents.cs</item>
///   <item>Configuration: Generated/StorylineServiceConfiguration.cs</item>
///   <item>State stores: bannou-service/Generated/StateStoreDefinitions.cs</item>
/// </list>
/// </para>
/// </remarks>
[BannouService("storyline", typeof(IStorylineService), lifetime: ServiceLifetime.Scoped, layer: ServiceLayer.GameFeatures)]
public partial class StorylineService : IStorylineService
{
    private readonly IMessageBus _messageBus;
    private readonly IStateStoreFactory _stateStoreFactory;
    private readonly IResourceClient _resourceClient;
    private readonly ILogger<StorylineService> _logger;
    private readonly StorylineServiceConfiguration _configuration;
    private readonly IServiceProvider _serviceProvider;
    private readonly IDistributedLockProvider _lockProvider;

    // Hard L2 dependency - constructor injection, crash if missing
    private readonly IRelationshipClient _relationshipClient;

    // State stores - use StateStoreDefinitions constants per IMPLEMENTATION TENETS
    private readonly IStateStore<CachedPlan> _planStore;
    private readonly ICacheableStateStore<PlanIndexEntry> _planIndexStore;

    // Scenario state stores
    private readonly IQueryableStateStore<ScenarioDefinitionModel> _scenarioDefinitionStore;
    private readonly IQueryableStateStore<ScenarioExecutionModel> _scenarioExecutionStore;
    private readonly IStateStore<ScenarioDefinitionModel> _scenarioCacheStore;
    private readonly IStateStore<CooldownMarker> _scenarioCooldownStore;
    private readonly ICacheableStateStore<ActiveScenarioEntry> _scenarioActiveStore;
    private readonly IStateStore<IdempotencyMarker> _scenarioIdempotencyStore;

    // SDK - direct instantiation (pure computation, not DI)
    private readonly StorylineComposer _composer;

    /// <summary>
    /// Creates a new StorylineService instance.
    /// </summary>
    public StorylineService(
        IMessageBus messageBus,
        IStateStoreFactory stateStoreFactory,
        IResourceClient resourceClient,
        IRelationshipClient relationshipClient,
        IDistributedLockProvider lockProvider,
        IServiceProvider serviceProvider,
        ILogger<StorylineService> logger,
        StorylineServiceConfiguration configuration)
    {
        // Null checks with ArgumentNullException - per IMPLEMENTATION TENETS
        ArgumentNullException.ThrowIfNull(messageBus);
        ArgumentNullException.ThrowIfNull(stateStoreFactory);
        ArgumentNullException.ThrowIfNull(resourceClient);
        ArgumentNullException.ThrowIfNull(relationshipClient);
        ArgumentNullException.ThrowIfNull(lockProvider);
        ArgumentNullException.ThrowIfNull(serviceProvider);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(configuration);

        _messageBus = messageBus;
        _stateStoreFactory = stateStoreFactory;
        _resourceClient = resourceClient;
        _relationshipClient = relationshipClient;
        _lockProvider = lockProvider;
        _serviceProvider = serviceProvider;
        _logger = logger;
        _configuration = configuration;

        // Use StateStoreDefinitions constants per IMPLEMENTATION TENETS
        _planStore = stateStoreFactory.GetStore<CachedPlan>(StateStoreDefinitions.StorylinePlans);
        _planIndexStore = stateStoreFactory.GetCacheableStore<PlanIndexEntry>(StateStoreDefinitions.StorylinePlanIndex);

        // Scenario state stores
        _scenarioDefinitionStore = stateStoreFactory.GetQueryableStore<ScenarioDefinitionModel>(StateStoreDefinitions.StorylineScenarioDefinitions);
        _scenarioExecutionStore = stateStoreFactory.GetQueryableStore<ScenarioExecutionModel>(StateStoreDefinitions.StorylineScenarioExecutions);
        _scenarioCacheStore = stateStoreFactory.GetStore<ScenarioDefinitionModel>(StateStoreDefinitions.StorylineScenarioCache);
        _scenarioCooldownStore = stateStoreFactory.GetStore<CooldownMarker>(StateStoreDefinitions.StorylineScenarioCooldown);
        _scenarioActiveStore = stateStoreFactory.GetCacheableStore<ActiveScenarioEntry>(StateStoreDefinitions.StorylineScenarioActive);
        _scenarioIdempotencyStore = stateStoreFactory.GetStore<IdempotencyMarker>(StateStoreDefinitions.StorylineScenarioIdempotency);

        // SDK instantiation - pure computation, no DI needed
        _composer = new StorylineComposer();
    }

    /// <summary>
    /// Composes a storyline plan from archive seeds.
    /// </summary>
    public async Task<(StatusCodes, ComposeResponse?)> ComposeAsync(ComposeRequest body, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        _logger.LogDebug("Composing storyline for {SeedCount} seeds, goal {Goal}",
            body.SeedSources.Count, body.Goal);

        try
        {
            // Validate request
            if (body.SeedSources.Count == 0)
            {
                _logger.LogWarning("Compose request has no seed sources");
                return (StatusCodes.BadRequest, null);
            }

            if (body.SeedSources.Count > _configuration.MaxSeedSources)
            {
                _logger.LogWarning("Compose request exceeds max seed sources: {Count} > {Max}",
                    body.SeedSources.Count, _configuration.MaxSeedSources);
                return (StatusCodes.BadRequest, null);
            }

            // Check cache if seed is provided (deterministic output)
            if (body.Seed.HasValue && _configuration.PlanCacheEnabled)
            {
                var cacheKey = ComputeCacheKey(body);
                var cached = await _planStore.GetAsync(cacheKey, cancellationToken);
                if (cached != null)
                {
                    _logger.LogDebug("Returning cached plan {PlanId} for seed {Seed}",
                        cached.PlanId, body.Seed.Value);

                    var cachedResponse = BuildResponseFromCachedPlan(cached, isCached: true, generationTimeMs: 0);
                    return (StatusCodes.OK, cachedResponse);
                }
            }

            // Fetch archives and snapshots
            var (archiveBundle, archiveIds, snapshotIds, fetchError) = await FetchSeedDataAsync(body.SeedSources, cancellationToken);
            if (fetchError != null)
            {
                _logger.LogWarning("Failed to fetch seed data: {Error}", fetchError);
                return (StatusCodes.BadRequest, null);
            }

            // Determine arc type and template
            var arcType = ResolveArcType(body.ArcType, body.Goal);
            var template = TemplateRegistry.Get(arcType);

            // Determine genre
            var genre = body.Genre ?? _configuration.DefaultGenre;

            // Determine primary spectrum from goal
            var primarySpectrum = ResolveSpectrumFromGoal(body.Goal);

            // Build character IDs and realm ID from archive data
            var (characterIds, realmId) = ExtractEntitiesFromArchives(archiveBundle);

            // Build actant assignments from seed source roles
            var actantAssignments = BuildActantAssignments(body.SeedSources, characterIds);

            // Create story context
            var context = new StoryContext
            {
                Template = template,
                Genre = genre,
                Subgenre = null,
                PrimarySpectrum = primarySpectrum,
                CharacterIds = characterIds.ToArray(),
                RealmId = realmId ?? body.Constraints?.RealmId ?? Guid.Empty,
                ActantAssignments = actantAssignments
            };

            // Resolve planning urgency: request override → config default
            var urgency = ResolveUrgency(body.Urgency);

            // Call SDK to compose storyline
            var sdkPlan = _composer.Compose(context, archiveBundle, urgency);

            stopwatch.Stop();
            var generationTimeMs = (int)stopwatch.ElapsedMilliseconds;

            // Generate plan ID
            var planId = Guid.NewGuid();

            // Build response
            var response = BuildComposeResponse(
                planId,
                body.Goal,
                sdkPlan,
                generationTimeMs,
                cached: false);

            // Cache the plan
            var ttl = TimeSpan.FromSeconds(_configuration.PlanCacheTtlSeconds);
            var cachedPlan = new CachedPlan
            {
                PlanId = planId,
                Goal = body.Goal,
                ArcType = response.ArcType,
                PrimarySpectrum = response.PrimarySpectrum,
                Genre = response.Genre,
                Confidence = response.Confidence,
                Phases = response.Phases.ToList(),
                EntitiesToSpawn = response.EntitiesToSpawn?.ToList(),
                Links = response.Links?.ToList(),
                Risks = response.Risks?.ToList(),
                Themes = response.Themes?.ToList(),
                RealmId = body.Constraints?.RealmId,
                ArchiveIds = archiveIds,
                SnapshotIds = snapshotIds,
                Seed = body.Seed,
                GenerationTimeMs = generationTimeMs,
                CreatedAt = DateTimeOffset.UtcNow,
                ExpiresAt = DateTimeOffset.UtcNow.Add(ttl)
            };

            var planKey = planId.ToString();
            await _planStore.SaveAsync(planKey, cachedPlan, new StateOptions { Ttl = (int)ttl.TotalSeconds }, cancellationToken);

            // Update plan index for realm-based queries
            if (body.Constraints?.RealmId.HasValue == true)
            {
                await UpdatePlanIndexAsync(planId, body.Constraints.RealmId.Value, cachedPlan, cancellationToken);
            }

            // Publish storyline.composed event
            await PublishComposedEventAsync(
                planId,
                body,
                response,
                archiveIds,
                snapshotIds,
                generationTimeMs,
                cancellationToken);

            _logger.LogInformation("Composed storyline plan {PlanId} with confidence {Confidence} in {TimeMs}ms",
                planId, response.Confidence, generationTimeMs);

            return (StatusCodes.OK, response);
        }
        catch (KeyNotFoundException ex)
        {
            // Template or action not found in SDK
            _logger.LogWarning(ex, "SDK resource not found during composition");
            return (StatusCodes.BadRequest, null);
        }
        catch (InvalidOperationException ex)
        {
            // SDK operation error
            _logger.LogWarning(ex, "Invalid operation during composition");
            return (StatusCodes.BadRequest, null);
        }
        catch (ApiException ex)
        {
            // Expected API error from inter-service call (e.g., resource not found)
            _logger.LogWarning(ex, "Resource service call failed with status {Status}", ex.StatusCode);
            return ((StatusCodes)ex.StatusCode, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing Compose operation");
            await _messageBus.TryPublishErrorAsync(
                "storyline",
                "Compose",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                endpoint: "post:/storyline/compose",
                details: null,
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Retrieves a cached plan by ID.
    /// </summary>
    public async Task<(StatusCodes, GetPlanResponse?)> GetPlanAsync(GetPlanRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Retrieving plan {PlanId}", body.PlanId);

        try
        {
            var planKey = body.PlanId.ToString();
            var cached = await _planStore.GetAsync(planKey, cancellationToken);

            if (cached == null)
            {
                return (StatusCodes.OK, new GetPlanResponse
                {
                    Found = false,
                    Plan = null
                });
            }

            var response = new GetPlanResponse
            {
                Found = true,
                Plan = BuildResponseFromCachedPlan(cached, isCached: true, generationTimeMs: cached.GenerationTimeMs)
            };

            return (StatusCodes.OK, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing GetPlan operation");
            await _messageBus.TryPublishErrorAsync(
                "storyline",
                "GetPlan",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                endpoint: "post:/storyline/plan/get",
                details: null,
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Lists cached plans, optionally filtered by realm.
    /// </summary>
    public async Task<(StatusCodes, ListPlansResponse?)> ListPlansAsync(ListPlansRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Listing plans, realm filter {RealmId}, limit {Limit}, offset {Offset}",
            body.RealmId, body.Limit, body.Offset);

        try
        {
            var planSummaries = new List<PlanSummary>();
            var totalCount = 0;

            if (body.RealmId.HasValue)
            {
                // Query plans by realm from index using sorted set
                var indexKey = $"realm:{body.RealmId.Value}";

                // Get total count
                totalCount = (int)await _planIndexStore.SortedSetCountAsync(indexKey, cancellationToken);

                // Get paginated results (sorted by timestamp, descending = newest first)
                var rangeResults = await _planIndexStore.SortedSetRangeByRankAsync(
                    indexKey,
                    body.Offset,
                    body.Offset + body.Limit - 1,
                    descending: true,
                    cancellationToken: cancellationToken);

                // Fetch plan summaries
                foreach (var (planIdStr, _) in rangeResults)
                {
                    if (Guid.TryParse(planIdStr, out var planGuid))
                    {
                        var cached = await _planStore.GetAsync(planIdStr, cancellationToken);
                        if (cached != null)
                        {
                            planSummaries.Add(new PlanSummary
                            {
                                PlanId = planGuid,
                                Goal = cached.Goal,
                                ArcType = cached.ArcType,
                                Confidence = cached.Confidence,
                                RealmId = cached.RealmId,
                                CreatedAt = cached.CreatedAt,
                                ExpiresAt = cached.ExpiresAt
                            });
                        }
                    }
                }
            }
            else
            {
                // No realm filter - return empty list for now
                // Full scan would be expensive; callers should use realm filter
                _logger.LogDebug("ListPlans called without realm filter, returning empty results");
            }

            return (StatusCodes.OK, new ListPlansResponse
            {
                Plans = planSummaries,
                TotalCount = totalCount
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing ListPlans operation");
            await _messageBus.TryPublishErrorAsync(
                "storyline",
                "ListPlans",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                endpoint: "post:/storyline/plan/list",
                details: null,
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    #region Private Helper Methods

    /// <summary>
    /// Computes a deterministic cache key from the compose request.
    /// </summary>
    private static string ComputeCacheKey(ComposeRequest body)
    {
        // Use seed + goal + sorted archive/snapshot IDs for cache key
        var archiveIds = body.SeedSources
            .Where(s => s.ArchiveId.HasValue)
            .Select(s => s.ArchiveId!.Value.ToString())
            .OrderBy(x => x);

        var snapshotIds = body.SeedSources
            .Where(s => s.SnapshotId.HasValue)
            .Select(s => s.SnapshotId!.Value.ToString())
            .OrderBy(x => x);

        var keyParts = new List<string>
        {
            $"seed:{body.Seed}",
            $"goal:{body.Goal}",
            $"arc:{body.ArcType}",
            $"genre:{body.Genre ?? "default"}",
            $"archives:{string.Join(",", archiveIds)}",
            $"snapshots:{string.Join(",", snapshotIds)}"
        };

        return $"cache:{string.Join("|", keyParts)}";
    }

    /// <summary>
    /// Fetches archive and snapshot data from the Resource service.
    /// </summary>
    private async Task<(ArchiveBundle bundle, List<Guid> archiveIds, List<Guid> snapshotIds, string? error)>
        FetchSeedDataAsync(ICollection<SeedSource> seedSources, CancellationToken cancellationToken)
    {
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
    /// Populates an ArchiveBundle from archive/snapshot entries.
    /// </summary>
    private void PopulateBundleFromEntries(ArchiveBundle bundle, ICollection<ArchiveBundleEntry> entries, Guid sourceId)
    {
        foreach (var entry in entries)
        {
            try
            {
                // Decompress and deserialize the entry data
                var json = DecompressEntry(entry.Data);

                // Add to bundle based on source type
                switch (entry.SourceType.ToLowerInvariant())
                {
                    case "character":
                        var character = BannouJson.Deserialize<CharacterBaseArchive>(json);
                        if (character != null)
                        {
                            bundle.AddEntry("character", character);
                        }
                        break;

                    case "character-history":
                        var history = BannouJson.Deserialize<CharacterHistoryArchive>(json);
                        if (history != null)
                        {
                            bundle.AddEntry("character-history", history);
                        }
                        break;

                    case "character-encounter":
                        var encounters = BannouJson.Deserialize<CharacterEncounterArchive>(json);
                        if (encounters != null)
                        {
                            bundle.AddEntry("character-encounter", encounters);
                        }
                        break;

                    case "character-personality":
                        var personality = BannouJson.Deserialize<CharacterPersonalityArchive>(json);
                        if (personality != null)
                        {
                            bundle.AddEntry("character-personality", personality);
                        }
                        break;

                    default:
                        _logger.LogDebug("Unknown archive entry type: {SourceType}", entry.SourceType);
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse archive entry {SourceType} from {SourceId}",
                    entry.SourceType, sourceId);
            }
        }
    }

    /// <summary>
    /// Decompresses a base64-encoded gzipped JSON string.
    /// </summary>
    private static string DecompressEntry(string base64Data)
    {
        var compressed = Convert.FromBase64String(base64Data);
        using var inputStream = new MemoryStream(compressed);
        using var gzipStream = new GZipStream(inputStream, CompressionMode.Decompress);
        using var reader = new StreamReader(gzipStream);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// Resolves planning urgency from request or configuration.
    /// </summary>
    private PlanningUrgency ResolveUrgency(PlanningUrgency? requestedUrgency)
    {
        // Request override takes precedence, then config default
        return requestedUrgency ?? _configuration.DefaultPlanningUrgency;
    }

    /// <summary>
    /// Resolves arc type from request or goal.
    /// </summary>
    private static ArcType ResolveArcType(ArcType? requestedArcType, StorylineGoal goal)
    {
        if (requestedArcType.HasValue)
        {
            return requestedArcType.Value;
        }

        // Map goal to appropriate arc type
        return goal switch
        {
            StorylineGoal.Revenge => ArcType.Oedipus,       // Fall-rise-fall, seeking vengeance ends badly
            StorylineGoal.Resurrection => ArcType.ManInHole, // Fall then rise, bringing back what was lost
            StorylineGoal.Legacy => ArcType.RagsToRiches,   // Monotonic rise, building lasting impact
            StorylineGoal.Mystery => ArcType.Cinderella,    // Rise-fall-rise, uncovering and resolving
            StorylineGoal.Peace => ArcType.ManInHole,       // Fall then rise, resolving conflicts
            _ => ArcType.ManInHole                          // Default: most common arc
        };
    }

    /// <summary>
    /// Resolves primary spectrum from story goal.
    /// </summary>
    private static SpectrumType ResolveSpectrumFromGoal(StorylineGoal goal)
    {
        return goal switch
        {
            StorylineGoal.Revenge => SpectrumType.JusticeInjustice,
            StorylineGoal.Resurrection => SpectrumType.LifeDeath,
            StorylineGoal.Legacy => SpectrumType.SuccessFailure,
            StorylineGoal.Mystery => SpectrumType.WisdomIgnorance,
            StorylineGoal.Peace => SpectrumType.LoveHate,
            _ => SpectrumType.JusticeInjustice
        };
    }

    /// <summary>
    /// Extracts character IDs and realm ID from archive bundle.
    /// </summary>
    private static (List<Guid> characterIds, Guid? realmId) ExtractEntitiesFromArchives(ArchiveBundle bundle)
    {
        var characterIds = new List<Guid>();
        Guid? realmId = null;

        if (bundle.TryGetEntry<CharacterBaseArchive>("character", out var character) && character != null)
        {
            characterIds.Add(character.CharacterId);
            realmId = character.RealmId;
        }

        return (characterIds, realmId);
    }

    /// <summary>
    /// Builds Greimas actant assignments from seed source roles.
    /// </summary>
    private static Dictionary<ActantRole, Guid[]> BuildActantAssignments(
        ICollection<SeedSource> seedSources,
        List<Guid> characterIds)
    {
        var assignments = new Dictionary<ActantRole, Guid[]>();

        // Map role hints to actant roles
        foreach (var source in seedSources)
        {
            if (string.IsNullOrEmpty(source.Role))
            {
                continue;
            }

            var archiveId = source.ArchiveId ?? source.SnapshotId;
            if (!archiveId.HasValue || !characterIds.Contains(archiveId.Value))
            {
                // Archive ID doesn't map directly to character ID in our simple model
                // For MVP, skip role assignment if we can't map
                continue;
            }

            var role = source.Role.ToLowerInvariant() switch
            {
                "protagonist" or "subject" or "hero" => ActantRole.Subject,
                "antagonist" or "opponent" or "villain" => ActantRole.Opponent,
                "helper" or "ally" or "sidekick" => ActantRole.Helper,
                "object" or "goal" or "mcguffin" => ActantRole.Object,
                "sender" or "mentor" or "initiator" => ActantRole.Sender,
                "receiver" or "beneficiary" => ActantRole.Receiver,
                _ => (ActantRole?)null
            };

            if (role.HasValue)
            {
                if (!assignments.ContainsKey(role.Value))
                {
                    assignments[role.Value] = new[] { archiveId.Value };
                }
                else
                {
                    var existing = assignments[role.Value];
                    assignments[role.Value] = existing.Append(archiveId.Value).ToArray();
                }
            }
        }

        // Default: first character is Subject if no assignments
        if (assignments.Count == 0 && characterIds.Count > 0)
        {
            assignments[ActantRole.Subject] = new[] { characterIds[0] };
        }

        return assignments;
    }

    /// <summary>
    /// Builds ComposeResponse from SDK StorylinePlan.
    /// </summary>
    private ComposeResponse BuildComposeResponse(
        Guid planId,
        StorylineGoal goal,
        StorylinePlan sdkPlan,
        int generationTimeMs,
        bool cached)
    {
        return new ComposeResponse
        {
            PlanId = planId,
            Confidence = CalculateConfidence(sdkPlan),
            Goal = goal,
            Genre = sdkPlan.Genre,
            ArcType = sdkPlan.ArcType,
            PrimarySpectrum = sdkPlan.PrimarySpectrum,
            Themes = InferThemes(goal, sdkPlan).ToList(),
            Phases = sdkPlan.Phases.ToList(), // SDK types used directly via x-sdk-type
            EntitiesToSpawn = null, // MVP: callers provide archive IDs, no entity spawning
            Links = null,           // MVP: no link extraction
            Risks = IdentifyRisks(sdkPlan).ToList(),
            GenerationTimeMs = generationTimeMs,
            Cached = cached
        };
    }

    /// <summary>
    /// Builds ComposeResponse from cached plan.
    /// </summary>
    private static ComposeResponse BuildResponseFromCachedPlan(CachedPlan cached, bool isCached, int generationTimeMs)
    {
        return new ComposeResponse
        {
            PlanId = cached.PlanId,
            Confidence = cached.Confidence,
            Goal = cached.Goal,
            Genre = cached.Genre,
            ArcType = cached.ArcType,
            PrimarySpectrum = cached.PrimarySpectrum,
            Themes = cached.Themes,
            Phases = cached.Phases ?? new List<StorylinePlanPhase>(),
            EntitiesToSpawn = cached.EntitiesToSpawn,
            Links = cached.Links,
            Risks = cached.Risks,
            GenerationTimeMs = generationTimeMs,
            Cached = isCached
        };
    }

    /// <summary>
    /// Calculates plan confidence score using config-driven thresholds (T21 compliant).
    /// </summary>
    private double CalculateConfidence(StorylinePlan plan)
    {
        // Simple confidence heuristic based on plan completeness
        var phaseCount = plan.Phases.Length;
        var actionCount = plan.Phases.Sum(p => p.Actions.Length);
        var coreEventCount = plan.Phases.Sum(p => p.Actions.Count(a => a.IsCoreEvent));

        // Base confidence from config
        var confidence = _configuration.ConfidenceBaseScore;

        // Boost for having multiple phases (threshold from config)
        if (phaseCount >= _configuration.ConfidencePhaseThreshold)
        {
            confidence += _configuration.ConfidencePhaseBonus;
        }

        // Boost for having core events
        if (coreEventCount > 0)
        {
            confidence += _configuration.ConfidenceCoreEventBonus;
        }

        // Boost for reasonable action count (range from config)
        if (actionCount >= _configuration.ConfidenceMinActionCount &&
            actionCount <= _configuration.ConfidenceMaxActionCount)
        {
            confidence += _configuration.ConfidenceActionCountBonus;
        }

        return Math.Min(1.0, confidence);
    }

    /// <summary>
    /// Infers thematic elements from goal and plan.
    /// </summary>
    private static IEnumerable<string> InferThemes(StorylineGoal goal, StorylinePlan plan)
    {
        // Base theme from goal
        yield return goal.ToString().ToLowerInvariant();

        // Additional themes based on arc type
        switch (plan.ArcType)
        {
            case ArcType.Tragedy:
                yield return "loss";
                yield return "fate";
                break;
            case ArcType.RagsToRiches:
                yield return "transformation";
                yield return "hope";
                break;
            case ArcType.ManInHole:
                yield return "resilience";
                yield return "recovery";
                break;
            case ArcType.Icarus:
                yield return "hubris";
                yield return "warning";
                break;
            case ArcType.Cinderella:
                yield return "perseverance";
                yield return "triumph";
                break;
            case ArcType.Oedipus:
                yield return "fate";
                yield return "inevitability";
                break;
        }
    }

    /// <summary>
    /// Identifies potential risks in the plan using config-driven thresholds (T21 compliant).
    /// </summary>
    private IEnumerable<StorylineRisk> IdentifyRisks(StorylinePlan plan)
    {
        var actionCount = plan.Phases.Sum(p => p.Actions.Length);
        var coreEventCount = plan.Phases.Sum(p => p.Actions.Count(a => a.IsCoreEvent));

        // Low action count risk (threshold from config)
        if (actionCount < _configuration.RiskMinActionThreshold)
        {
            yield return new StorylineRisk
            {
                RiskType = "thin_content",
                Description = "Plan has very few actions, story may feel rushed",
                Severity = StorylineRiskSeverity.Medium,
                Mitigation = "Consider adding more intermediate actions"
            };
        }

        // Missing core events risk (this is a logical check, not a tunable)
        if (coreEventCount == 0)
        {
            yield return new StorylineRisk
            {
                RiskType = "missing_obligatory_scenes",
                Description = "No core events identified, story may lack genre satisfaction",
                Severity = StorylineRiskSeverity.High,
                Mitigation = "Verify genre-required scenes are present"
            };
        }

        // Single phase risk (threshold from config)
        if (plan.Phases.Length < _configuration.RiskMinPhaseThreshold)
        {
            yield return new StorylineRisk
            {
                RiskType = "flat_arc",
                Description = "Plan has only one phase, limiting emotional range",
                Severity = StorylineRiskSeverity.Low,
                Mitigation = "Consider extending story across multiple phases"
            };
        }
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
        var indexKey = $"realm:{realmId}";
        var score = plan.CreatedAt.ToUnixTimeSeconds();

        await _planIndexStore.SortedSetAddAsync(
            indexKey,
            planId.ToString(),
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
        var composedEvent = new StorylineComposedEvent
        {
            PlanId = planId,
            RealmId = request.Constraints?.RealmId,
            Goal = request.Goal.ToString(),
            ArcType = response.ArcType.ToString(),
            PrimarySpectrum = response.PrimarySpectrum.ToString(),
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

        await _messageBus.TryPublishAsync(
            "storyline.composed",
            composedEvent,
            cancellationToken: cancellationToken);
    }

    #endregion

    #region Scenario CRUD Methods

    /// <summary>
    /// Creates a new scenario definition.
    /// </summary>
    public async Task<(StatusCodes, ScenarioDefinition?)> CreateScenarioDefinitionAsync(
        CreateScenarioDefinitionRequest body,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Creating scenario definition {Code}", body.Code);

        try
        {
            // Normalize code to uppercase
            var normalizedCode = body.Code.ToUpperInvariant();

            // Check for duplicate code within scope
            var existing = await _scenarioDefinitionStore
                .Query()
                .Where(s => s.Code == normalizedCode &&
                           s.Status != ScenarioStatus.Cancelled &&
                           s.RealmId == body.RealmId &&
                           s.GameServiceId == body.GameServiceId)
                .FirstOrDefaultAsync(cancellationToken);

            if (existing != null)
            {
                _logger.LogWarning("Scenario code {Code} already exists in scope", normalizedCode);
                return (StatusCodes.Conflict, null);
            }

            var scenarioId = Guid.NewGuid();
            var now = DateTimeOffset.UtcNow;

            var model = new ScenarioDefinitionModel
            {
                ScenarioId = scenarioId,
                Code = normalizedCode,
                Name = body.Name,
                Description = body.Description,
                RealmId = body.RealmId,
                GameServiceId = body.GameServiceId,
                TriggerConditions = BannouJson.Serialize(body.TriggerConditions),
                Phases = BannouJson.Serialize(body.Phases),
                Mutations = body.Mutations != null ? BannouJson.Serialize(body.Mutations) : null,
                QuestHooks = body.QuestHooks != null ? BannouJson.Serialize(body.QuestHooks) : null,
                CooldownSeconds = body.CooldownSeconds,
                Tags = body.Tags != null ? BannouJson.Serialize(body.Tags) : null,
                ExclusivityTags = body.ExclusivityTags != null ? BannouJson.Serialize(body.ExclusivityTags) : null,
                Status = ScenarioStatus.Active,
                CreatedAt = now,
                UpdatedAt = now
            };

            await _scenarioDefinitionStore.SaveAsync(scenarioId.ToString(), model, cancellationToken: cancellationToken);

            _logger.LogInformation("Created scenario definition {ScenarioId} with code {Code}", scenarioId, normalizedCode);

            var response = MapToScenarioDefinition(model);
            return (StatusCodes.OK, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating scenario definition");
            await _messageBus.TryPublishErrorAsync(
                "storyline", "CreateScenarioDefinition", "unexpected_exception", ex.Message,
                dependency: null, endpoint: "post:/storyline/scenario/create",
                details: null, stack: ex.StackTrace, cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Retrieves a scenario definition by ID or code.
    /// </summary>
    public async Task<(StatusCodes, GetScenarioDefinitionResponse?)> GetScenarioDefinitionAsync(
        GetScenarioDefinitionRequest body,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Getting scenario definition, Id={ScenarioId}, Code={Code}", body.ScenarioId, body.Code);

        try
        {
            ScenarioDefinitionModel? model = null;

            if (body.ScenarioId.HasValue)
            {
                // Try cache first
                var cacheKey = $"id:{body.ScenarioId.Value}";
                model = await _scenarioCacheStore.GetAsync(cacheKey, cancellationToken);

                if (model == null)
                {
                    // Cache miss - fetch from MySQL
                    model = await _scenarioDefinitionStore.GetAsync(body.ScenarioId.Value.ToString(), cancellationToken);
                    if (model != null)
                    {
                        // Populate cache
                        var ttl = _configuration.ScenarioDefinitionCacheTtlSeconds;
                        await _scenarioCacheStore.SaveAsync(cacheKey, model, new StateOptions { Ttl = ttl }, cancellationToken);
                    }
                }
            }
            else if (!string.IsNullOrEmpty(body.Code))
            {
                var normalizedCode = body.Code.ToUpperInvariant();

                // Try cache first
                var cacheKey = $"code:{normalizedCode}:{body.RealmId}:{body.GameServiceId}";
                model = await _scenarioCacheStore.GetAsync(cacheKey, cancellationToken);

                if (model == null)
                {
                    // Cache miss - query MySQL
                    model = await _scenarioDefinitionStore
                        .Query()
                        .Where(s => s.Code == normalizedCode &&
                                   s.Status != ScenarioStatus.Cancelled &&
                                   s.RealmId == body.RealmId &&
                                   s.GameServiceId == body.GameServiceId)
                        .FirstOrDefaultAsync(cancellationToken);

                    if (model != null)
                    {
                        // Populate cache
                        var ttl = _configuration.ScenarioDefinitionCacheTtlSeconds;
                        await _scenarioCacheStore.SaveAsync(cacheKey, model, new StateOptions { Ttl = ttl }, cancellationToken);
                    }
                }
            }
            else
            {
                _logger.LogWarning("GetScenarioDefinition requires either scenarioId or code");
                return (StatusCodes.BadRequest, null);
            }

            var response = new GetScenarioDefinitionResponse
            {
                Found = model != null,
                Definition = model != null ? MapToScenarioDefinition(model) : null
            };

            return (StatusCodes.OK, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting scenario definition");
            await _messageBus.TryPublishErrorAsync(
                "storyline", "GetScenarioDefinition", "unexpected_exception", ex.Message,
                dependency: null, endpoint: "post:/storyline/scenario/get",
                details: null, stack: ex.StackTrace, cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Lists scenario definitions with optional filtering.
    /// </summary>
    public async Task<(StatusCodes, ListScenarioDefinitionsResponse?)> ListScenarioDefinitionsAsync(
        ListScenarioDefinitionsRequest body,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Listing scenario definitions, Realm={RealmId}, GameService={GameServiceId}, Limit={Limit}",
            body.RealmId, body.GameServiceId, body.Limit);

        try
        {
            var query = _scenarioDefinitionStore.Query()
                .Where(s => s.Status != ScenarioStatus.Cancelled);

            if (body.RealmId.HasValue)
            {
                query = query.Where(s => s.RealmId == body.RealmId);
            }

            if (body.GameServiceId.HasValue)
            {
                query = query.Where(s => s.GameServiceId == body.GameServiceId);
            }

            if (body.Status.HasValue)
            {
                query = query.Where(s => s.Status == body.Status.Value);
            }

            // Get total count
            var totalCount = await query.CountAsync(cancellationToken);

            // Apply pagination
            var models = await query
                .OrderByDescending(s => s.CreatedAt)
                .Skip(body.Offset)
                .Take(body.Limit)
                .ToListAsync(cancellationToken);

            var summaries = models.Select(m => new ScenarioDefinitionSummary
            {
                ScenarioId = m.ScenarioId,
                Code = m.Code,
                Name = m.Name,
                Status = m.Status,
                RealmId = m.RealmId,
                GameServiceId = m.GameServiceId,
                CreatedAt = m.CreatedAt
            }).ToList();

            return (StatusCodes.OK, new ListScenarioDefinitionsResponse
            {
                Definitions = summaries,
                TotalCount = totalCount
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing scenario definitions");
            await _messageBus.TryPublishErrorAsync(
                "storyline", "ListScenarioDefinitions", "unexpected_exception", ex.Message,
                dependency: null, endpoint: "post:/storyline/scenario/list",
                details: null, stack: ex.StackTrace, cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Updates a scenario definition.
    /// </summary>
    public async Task<(StatusCodes, ScenarioDefinition?)> UpdateScenarioDefinitionAsync(
        UpdateScenarioDefinitionRequest body,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Updating scenario definition {ScenarioId}", body.ScenarioId);

        try
        {
            var (model, etag) = await _scenarioDefinitionStore.GetWithETagAsync(body.ScenarioId.ToString(), cancellationToken);

            if (model == null)
            {
                return (StatusCodes.NotFound, null);
            }

            if (model.Status == ScenarioStatus.Cancelled)
            {
                _logger.LogWarning("Cannot update cancelled scenario {ScenarioId}", body.ScenarioId);
                return (StatusCodes.Conflict, null);
            }

            // ETag check for optimistic concurrency
            if (!string.IsNullOrEmpty(body.Etag) && body.Etag != etag)
            {
                _logger.LogWarning("ETag mismatch for scenario {ScenarioId}", body.ScenarioId);
                return (StatusCodes.Conflict, null);
            }

            // Apply updates
            if (body.Name != null) model.Name = body.Name;
            if (body.Description != null) model.Description = body.Description;
            if (body.TriggerConditions != null) model.TriggerConditions = BannouJson.Serialize(body.TriggerConditions);
            if (body.Phases != null) model.Phases = BannouJson.Serialize(body.Phases);
            if (body.Mutations != null) model.Mutations = BannouJson.Serialize(body.Mutations);
            if (body.QuestHooks != null) model.QuestHooks = BannouJson.Serialize(body.QuestHooks);
            if (body.CooldownSeconds.HasValue) model.CooldownSeconds = body.CooldownSeconds;
            if (body.Tags != null) model.Tags = BannouJson.Serialize(body.Tags);
            if (body.ExclusivityTags != null) model.ExclusivityTags = BannouJson.Serialize(body.ExclusivityTags);
            model.UpdatedAt = DateTimeOffset.UtcNow;

            var saved = await _scenarioDefinitionStore.TrySaveAsync(body.ScenarioId.ToString(), model, etag, cancellationToken);
            if (!saved)
            {
                _logger.LogWarning("Concurrent modification detected for scenario {ScenarioId}", body.ScenarioId);
                return (StatusCodes.Conflict, null);
            }

            // Invalidate cache
            await InvalidateScenarioCacheAsync(model, cancellationToken);

            _logger.LogInformation("Updated scenario definition {ScenarioId}", body.ScenarioId);

            return (StatusCodes.OK, MapToScenarioDefinition(model));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating scenario definition");
            await _messageBus.TryPublishErrorAsync(
                "storyline", "UpdateScenarioDefinition", "unexpected_exception", ex.Message,
                dependency: null, endpoint: "post:/storyline/scenario/update",
                details: null, stack: ex.StackTrace, cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Deprecates (soft-deletes) a scenario definition.
    /// </summary>
    public async Task<StatusCodes> DeprecateScenarioDefinitionAsync(
        DeprecateScenarioDefinitionRequest body,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Deprecating scenario definition {ScenarioId}", body.ScenarioId);

        try
        {
            var (model, etag) = await _scenarioDefinitionStore.GetWithETagAsync(body.ScenarioId.ToString(), cancellationToken);

            if (model == null)
            {
                return StatusCodes.NotFound;
            }

            if (model.Status == ScenarioStatus.Cancelled)
            {
                return StatusCodes.OK; // Already deprecated
            }

            model.Status = ScenarioStatus.Cancelled;
            model.UpdatedAt = DateTimeOffset.UtcNow;

            var saved = await _scenarioDefinitionStore.TrySaveAsync(body.ScenarioId.ToString(), model, etag, cancellationToken);
            if (!saved)
            {
                return StatusCodes.Conflict;
            }

            // Invalidate cache
            await InvalidateScenarioCacheAsync(model, cancellationToken);

            _logger.LogInformation("Deprecated scenario definition {ScenarioId}", body.ScenarioId);

            return StatusCodes.OK;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deprecating scenario definition");
            await _messageBus.TryPublishErrorAsync(
                "storyline", "DeprecateScenarioDefinition", "unexpected_exception", ex.Message,
                dependency: null, endpoint: "post:/storyline/scenario/deprecate",
                details: null, stack: ex.StackTrace, cancellationToken: cancellationToken);
            return StatusCodes.InternalServerError;
        }
    }

    #endregion

    #region Scenario Discovery Methods

    /// <summary>
    /// Finds available scenarios for a character based on provided state.
    /// </summary>
    public async Task<(StatusCodes, FindAvailableScenariosResponse?)> FindAvailableScenariosAsync(
        FindAvailableScenariosRequest body,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Finding available scenarios for character {CharacterId}", body.CharacterId);

        try
        {
            // Query active scenarios within scope
            var query = _scenarioDefinitionStore.Query()
                .Where(s => s.Status == ScenarioStatus.Active);

            if (body.RealmId.HasValue)
            {
                query = query.Where(s => s.RealmId == null || s.RealmId == body.RealmId);
            }

            if (body.GameServiceId.HasValue)
            {
                query = query.Where(s => s.GameServiceId == null || s.GameServiceId == body.GameServiceId);
            }

            var scenarios = await query.ToListAsync(cancellationToken);

            var availableScenarios = new List<AvailableScenario>();

            foreach (var scenario in scenarios)
            {
                // Check cooldown
                var cooldownKey = $"{body.CharacterId}:{scenario.ScenarioId}";
                var onCooldown = await _scenarioCooldownStore.GetAsync(cooldownKey, cancellationToken);
                if (onCooldown != null)
                {
                    continue; // Skip scenarios on cooldown
                }

                // Check exclusivity
                if (await IsExcludedByActiveScenarioAsync(body.CharacterId, scenario, cancellationToken))
                {
                    continue;
                }

                // Evaluate conditions against provided state
                var (conditionsMet, fitScore) = EvaluateConditions(scenario, body.CharacterState);

                if (conditionsMet && fitScore >= _configuration.ScenarioFitScoreMinimumThreshold)
                {
                    availableScenarios.Add(new AvailableScenario
                    {
                        ScenarioId = scenario.ScenarioId,
                        Code = scenario.Code,
                        Name = scenario.Name,
                        Description = scenario.Description,
                        FitScore = fitScore,
                        TriggerRecommended = fitScore >= _configuration.ScenarioFitScoreRecommendThreshold
                    });
                }
            }

            // Sort by fit score descending
            availableScenarios = availableScenarios.OrderByDescending(s => s.FitScore).ToList();

            // Publish available event if scenarios found
            if (availableScenarios.Count > 0)
            {
                await PublishScenarioAvailableEventAsync(
                    body.CharacterId,
                    body.RealmId,
                    body.GameServiceId,
                    availableScenarios,
                    cancellationToken);
            }

            return (StatusCodes.OK, new FindAvailableScenariosResponse
            {
                Scenarios = availableScenarios,
                TotalAvailable = availableScenarios.Count
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error finding available scenarios");
            await _messageBus.TryPublishErrorAsync(
                "storyline", "FindAvailableScenarios", "unexpected_exception", ex.Message,
                dependency: null, endpoint: "post:/storyline/scenario/find-available",
                details: null, stack: ex.StackTrace, cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Tests scenario trigger conditions without executing.
    /// </summary>
    public async Task<(StatusCodes, TestScenarioResponse?)> TestScenarioTriggerAsync(
        TestScenarioRequest body,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Testing scenario {ScenarioId} for character {CharacterId}",
            body.ScenarioId, body.CharacterId);

        try
        {
            var scenario = await _scenarioDefinitionStore.GetAsync(body.ScenarioId.ToString(), cancellationToken);
            if (scenario == null)
            {
                return (StatusCodes.NotFound, null);
            }

            var blockingReasons = new List<string>();

            // Check cooldown
            var cooldownKey = $"{body.CharacterId}:{scenario.ScenarioId}";
            var cooldownMarker = await _scenarioCooldownStore.GetAsync(cooldownKey, cancellationToken);
            if (cooldownMarker != null)
            {
                blockingReasons.Add($"On cooldown until {cooldownMarker.ExpiresAt:u}");
            }

            // Check active count
            var activeCount = await GetActiveScenarioCountAsync(body.CharacterId, cancellationToken);
            if (activeCount >= _configuration.ScenarioMaxActivePerCharacter)
            {
                blockingReasons.Add($"Maximum active scenarios ({_configuration.ScenarioMaxActivePerCharacter}) reached");
            }

            // Check exclusivity
            if (await IsExcludedByActiveScenarioAsync(body.CharacterId, scenario, cancellationToken))
            {
                blockingReasons.Add("Excluded by active scenario with conflicting exclusivity tag");
            }

            // Evaluate conditions
            var (conditionsMet, fitScore) = EvaluateConditions(scenario, body.CharacterState);

            var conditionResults = EvaluateConditionDetails(scenario, body.CharacterState);

            // Predict mutations without applying
            var predictedMutations = new List<PredictedMutation>();
            if (!string.IsNullOrEmpty(scenario.Mutations))
            {
                var mutations = BannouJson.Deserialize<List<ScenarioMutation>>(scenario.Mutations);
                if (mutations != null)
                {
                    foreach (var mutation in mutations)
                    {
                        predictedMutations.Add(new PredictedMutation
                        {
                            MutationType = mutation.MutationType,
                            Description = DescribeMutation(mutation)
                        });
                    }
                }
            }

            var canTrigger = blockingReasons.Count == 0 && conditionsMet;

            return (StatusCodes.OK, new TestScenarioResponse
            {
                CanTrigger = canTrigger,
                FitScore = fitScore,
                ConditionResults = conditionResults,
                BlockingReasons = blockingReasons.Count > 0 ? blockingReasons : null,
                PredictedMutations = predictedMutations.Count > 0 ? predictedMutations : null,
                DramaticallyInteresting = canTrigger && fitScore >= _configuration.ScenarioFitScoreRecommendThreshold
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error testing scenario trigger");
            await _messageBus.TryPublishErrorAsync(
                "storyline", "TestScenarioTrigger", "unexpected_exception", ex.Message,
                dependency: null, endpoint: "post:/storyline/scenario/test",
                details: null, stack: ex.StackTrace, cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Evaluates scenario fit score without full condition check.
    /// </summary>
    public async Task<(StatusCodes, EvaluateFitResponse?)> EvaluateScenarioFitAsync(
        EvaluateFitRequest body,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Evaluating fit for scenario {ScenarioId}, character {CharacterId}",
            body.ScenarioId, body.CharacterId);

        try
        {
            var scenario = await _scenarioDefinitionStore.GetAsync(body.ScenarioId.ToString(), cancellationToken);
            if (scenario == null)
            {
                return (StatusCodes.NotFound, null);
            }

            var (_, fitScore) = EvaluateConditions(scenario, body.CharacterState);

            return (StatusCodes.OK, new EvaluateFitResponse
            {
                ScenarioId = scenario.ScenarioId,
                FitScore = fitScore,
                MeetsMinimumThreshold = fitScore >= _configuration.ScenarioFitScoreMinimumThreshold,
                RecommendTrigger = fitScore >= _configuration.ScenarioFitScoreRecommendThreshold
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error evaluating scenario fit");
            await _messageBus.TryPublishErrorAsync(
                "storyline", "EvaluateScenarioFit", "unexpected_exception", ex.Message,
                dependency: null, endpoint: "post:/storyline/scenario/evaluate-fit",
                details: null, stack: ex.StackTrace, cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    #endregion

    #region Scenario Execution Methods

    /// <summary>
    /// Triggers a scenario for execution.
    /// </summary>
    public async Task<(StatusCodes, TriggerScenarioResponse?)> TriggerScenarioAsync(
        TriggerScenarioRequest body,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Triggering scenario {ScenarioId} for character {CharacterId}",
            body.ScenarioId, body.PrimaryCharacterId);

        try
        {
            // Idempotency check
            if (!string.IsNullOrEmpty(body.IdempotencyKey))
            {
                var existing = await _scenarioIdempotencyStore.GetAsync(body.IdempotencyKey, cancellationToken);
                if (existing != null)
                {
                    _logger.LogDebug("Idempotent trigger detected for key {IdempotencyKey}", body.IdempotencyKey);
                    // Return the existing execution
                    var existingExecution = await _scenarioExecutionStore.GetAsync(existing.ExecutionId.ToString(), cancellationToken);
                    if (existingExecution != null)
                    {
                        return (StatusCodes.OK, new TriggerScenarioResponse
                        {
                            ExecutionId = existingExecution.ExecutionId,
                            Status = existingExecution.Status,
                            TriggeredAt = existingExecution.StartedAt
                        });
                    }
                }
            }

            // Acquire distributed lock per IMPLEMENTATION TENETS (multi-instance safety)
            var lockResourceId = $"scenario-trigger:{body.PrimaryCharacterId}:{body.ScenarioId}";
            await using var lockResponse = await _lockProvider.LockAsync(
                resourceId: lockResourceId,
                lockOwner: Guid.NewGuid().ToString(),
                expiryInSeconds: _configuration.ScenarioTriggerLockTimeoutSeconds,
                cancellationToken: cancellationToken);

            if (!lockResponse.Success)
            {
                _logger.LogWarning("Failed to acquire lock for scenario trigger");
                return (StatusCodes.Conflict, null);
            }

            // Load scenario definition
            var scenario = await _scenarioDefinitionStore.GetAsync(body.ScenarioId.ToString(), cancellationToken);
            if (scenario == null)
            {
                return (StatusCodes.NotFound, null);
            }

            if (scenario.Status != ScenarioStatus.Active)
            {
                _logger.LogWarning("Cannot trigger inactive scenario {ScenarioId}", body.ScenarioId);
                return (StatusCodes.BadRequest, null);
            }

            // Check cooldown
            var cooldownKey = $"{body.PrimaryCharacterId}:{scenario.ScenarioId}";
            var onCooldown = await _scenarioCooldownStore.GetAsync(cooldownKey, cancellationToken);
            if (onCooldown != null)
            {
                _logger.LogWarning("Scenario {ScenarioId} is on cooldown for character {CharacterId}",
                    body.ScenarioId, body.PrimaryCharacterId);
                return (StatusCodes.Conflict, null);
            }

            // Check active count
            var activeCount = await GetActiveScenarioCountAsync(body.PrimaryCharacterId, cancellationToken);
            if (activeCount >= _configuration.ScenarioMaxActivePerCharacter)
            {
                _logger.LogWarning("Character {CharacterId} has maximum active scenarios", body.PrimaryCharacterId);
                return (StatusCodes.Conflict, null);
            }

            // Validate conditions against provided state
            var (conditionsMet, fitScore) = EvaluateConditions(scenario, body.CharacterState);
            if (!conditionsMet)
            {
                _logger.LogWarning("Conditions not met for scenario {ScenarioId}", body.ScenarioId);
                return (StatusCodes.BadRequest, null);
            }

            // Create execution record
            var executionId = Guid.NewGuid();
            var now = DateTimeOffset.UtcNow;

            var execution = new ScenarioExecutionModel
            {
                ExecutionId = executionId,
                ScenarioId = scenario.ScenarioId,
                ScenarioCode = scenario.Code,
                PrimaryCharacterId = body.PrimaryCharacterId,
                AdditionalParticipantIds = body.AdditionalParticipants != null
                    ? BannouJson.Serialize(body.AdditionalParticipants)
                    : null,
                OrchestratorId = body.OrchestratorId,
                RealmId = scenario.RealmId,
                GameServiceId = scenario.GameServiceId,
                FitScore = fitScore,
                Status = ScenarioStatus.Active,
                CurrentPhaseIndex = 0,
                MutationsApplied = 0,
                QuestsSpawned = 0,
                StartedAt = now,
                CompletedAt = null
            };

            await _scenarioExecutionStore.SaveAsync(executionId.ToString(), execution, cancellationToken: cancellationToken);

            // Add to active set
            var activeKey = $"character:{body.PrimaryCharacterId}";
            await _scenarioActiveStore.SetAddAsync(activeKey, executionId.ToString(), cancellationToken);

            // Store idempotency key if provided
            if (!string.IsNullOrEmpty(body.IdempotencyKey))
            {
                var idempotencyTtl = _configuration.ScenarioIdempotencyTtlSeconds;
                await _scenarioIdempotencyStore.SaveAsync(body.IdempotencyKey, new IdempotencyMarker
                {
                    ExecutionId = executionId,
                    CreatedAt = now
                }, new StateOptions { Ttl = idempotencyTtl }, cancellationToken);
            }

            // Publish triggered event
            await PublishScenarioTriggeredEventAsync(execution, scenario, fitScore, cancellationToken);

            // Apply mutations - track partial progress for failure reporting
            int mutationsApplied;
            int questsSpawned;
            List<Guid> questIds;
            try
            {
                (mutationsApplied, questsSpawned, questIds) = await ApplyMutationsAsync(scenario, execution, cancellationToken);
            }
            catch (Exception mutationEx)
            {
                // Mutations failed - mark execution as failed
                execution.Status = ScenarioStatus.Failed;
                execution.FailureReason = $"Mutation application failed: {mutationEx.Message}";
                execution.CompletedAt = DateTimeOffset.UtcNow;
                await _scenarioExecutionStore.SaveAsync(executionId.ToString(), execution, cancellationToken: cancellationToken);

                // Remove from active set
                await _scenarioActiveStore.SetRemoveAsync(activeKey, executionId.ToString(), cancellationToken);

                // Publish failed event
                await PublishScenarioFailedEventAsync(execution, mutationEx.Message, isRecoverable: false, cancellationToken);

                _logger.LogWarning(mutationEx, "Scenario {ScenarioId} failed during mutation for character {CharacterId}",
                    body.ScenarioId, body.PrimaryCharacterId);

                return (StatusCodes.OK, new TriggerScenarioResponse
                {
                    ExecutionId = executionId,
                    Status = ScenarioStatus.Failed,
                    TriggeredAt = now,
                    CompletedAt = execution.CompletedAt
                });
            }

            // Update execution with results
            execution.MutationsApplied = mutationsApplied;
            execution.QuestsSpawned = questsSpawned;
            execution.QuestIds = questIds.Count > 0 ? BannouJson.Serialize(questIds) : null;
            execution.Status = ScenarioStatus.Completed;
            execution.CompletedAt = DateTimeOffset.UtcNow;

            await _scenarioExecutionStore.SaveAsync(executionId.ToString(), execution, cancellationToken: cancellationToken);

            // Remove from active set
            await _scenarioActiveStore.SetRemoveAsync(activeKey, executionId.ToString(), cancellationToken);

            // Set cooldown
            var cooldownSeconds = scenario.CooldownSeconds ?? _configuration.ScenarioCooldownDefaultSeconds;
            if (cooldownSeconds > 0)
            {
                await _scenarioCooldownStore.SaveAsync(cooldownKey, new CooldownMarker
                {
                    ScenarioId = scenario.ScenarioId,
                    CharacterId = body.PrimaryCharacterId,
                    ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(cooldownSeconds)
                }, new StateOptions { Ttl = cooldownSeconds }, cancellationToken);
            }

            // Publish completed event
            await PublishScenarioCompletedEventAsync(execution, mutationsApplied, questsSpawned, questIds, cancellationToken);

            _logger.LogInformation("Triggered scenario {ScenarioId} for character {CharacterId}, execution {ExecutionId}",
                body.ScenarioId, body.PrimaryCharacterId, executionId);

            return (StatusCodes.OK, new TriggerScenarioResponse
            {
                ExecutionId = executionId,
                Status = ScenarioStatus.Completed,
                TriggeredAt = now,
                CompletedAt = execution.CompletedAt,
                MutationsApplied = mutationsApplied,
                QuestsSpawned = questsSpawned,
                QuestIds = questIds.Count > 0 ? questIds : null
            });
        }
        catch (ApiException ex)
        {
            _logger.LogWarning(ex, "Service call failed during scenario trigger with status {Status}", ex.StatusCode);
            return ((StatusCodes)ex.StatusCode, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error triggering scenario");
            await _messageBus.TryPublishErrorAsync(
                "storyline", "TriggerScenario", "unexpected_exception", ex.Message,
                dependency: null, endpoint: "post:/storyline/scenario/trigger",
                details: null, stack: ex.StackTrace, cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Gets active scenarios for a character.
    /// </summary>
    public async Task<(StatusCodes, GetActiveScenariosResponse?)> GetActiveScenariosAsync(
        GetActiveScenariosRequest body,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Getting active scenarios for character {CharacterId}", body.CharacterId);

        try
        {
            var activeKey = $"character:{body.CharacterId}";
            var executionIds = await _scenarioActiveStore.SetMembersAsync(activeKey, cancellationToken);

            var activeScenarios = new List<ActiveScenario>();

            foreach (var executionIdStr in executionIds)
            {
                var execution = await _scenarioExecutionStore.GetAsync(executionIdStr, cancellationToken);
                if (execution != null && execution.Status == ScenarioStatus.Active)
                {
                    activeScenarios.Add(new ActiveScenario
                    {
                        ExecutionId = execution.ExecutionId,
                        ScenarioId = execution.ScenarioId,
                        ScenarioCode = execution.ScenarioCode,
                        Status = execution.Status,
                        CurrentPhaseIndex = execution.CurrentPhaseIndex,
                        StartedAt = execution.StartedAt
                    });
                }
            }

            return (StatusCodes.OK, new GetActiveScenariosResponse
            {
                Scenarios = activeScenarios,
                Count = activeScenarios.Count
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting active scenarios");
            await _messageBus.TryPublishErrorAsync(
                "storyline", "GetActiveScenarios", "unexpected_exception", ex.Message,
                dependency: null, endpoint: "post:/storyline/scenario/get-active",
                details: null, stack: ex.StackTrace, cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Gets scenario execution history.
    /// </summary>
    public async Task<(StatusCodes, GetScenarioHistoryResponse?)> GetScenarioHistoryAsync(
        GetScenarioHistoryRequest body,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Getting scenario history for character {CharacterId}", body.CharacterId);

        try
        {
            var query = _scenarioExecutionStore.Query()
                .Where(e => e.PrimaryCharacterId == body.CharacterId);

            if (body.ScenarioId.HasValue)
            {
                query = query.Where(e => e.ScenarioId == body.ScenarioId.Value);
            }

            if (body.Status.HasValue)
            {
                query = query.Where(e => e.Status == body.Status.Value);
            }

            var totalCount = await query.CountAsync(cancellationToken);

            var executions = await query
                .OrderByDescending(e => e.StartedAt)
                .Skip(body.Offset)
                .Take(body.Limit)
                .ToListAsync(cancellationToken);

            var history = executions.Select(e => new ScenarioExecutionSummary
            {
                ExecutionId = e.ExecutionId,
                ScenarioId = e.ScenarioId,
                ScenarioCode = e.ScenarioCode,
                Status = e.Status,
                FitScore = e.FitScore,
                MutationsApplied = e.MutationsApplied,
                QuestsSpawned = e.QuestsSpawned,
                StartedAt = e.StartedAt,
                CompletedAt = e.CompletedAt
            }).ToList();

            return (StatusCodes.OK, new GetScenarioHistoryResponse
            {
                Executions = history,
                TotalCount = totalCount
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting scenario history");
            await _messageBus.TryPublishErrorAsync(
                "storyline", "GetScenarioHistory", "unexpected_exception", ex.Message,
                dependency: null, endpoint: "post:/storyline/scenario/get-history",
                details: null, stack: ex.StackTrace, cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    #endregion

    #region Scenario Helper Methods

    /// <summary>
    /// Maps internal model to API response model.
    /// </summary>
    private static ScenarioDefinition MapToScenarioDefinition(ScenarioDefinitionModel model)
    {
        return new ScenarioDefinition
        {
            ScenarioId = model.ScenarioId,
            Code = model.Code,
            Name = model.Name,
            Description = model.Description,
            RealmId = model.RealmId,
            GameServiceId = model.GameServiceId,
            TriggerConditions = !string.IsNullOrEmpty(model.TriggerConditions)
                ? BannouJson.Deserialize<List<TriggerCondition>>(model.TriggerConditions) ?? new List<TriggerCondition>()
                : new List<TriggerCondition>(),
            Phases = !string.IsNullOrEmpty(model.Phases)
                ? BannouJson.Deserialize<List<ScenarioPhase>>(model.Phases) ?? new List<ScenarioPhase>()
                : new List<ScenarioPhase>(),
            Mutations = !string.IsNullOrEmpty(model.Mutations)
                ? BannouJson.Deserialize<List<ScenarioMutation>>(model.Mutations)
                : null,
            QuestHooks = !string.IsNullOrEmpty(model.QuestHooks)
                ? BannouJson.Deserialize<List<ScenarioQuestHook>>(model.QuestHooks)
                : null,
            CooldownSeconds = model.CooldownSeconds,
            Tags = !string.IsNullOrEmpty(model.Tags)
                ? BannouJson.Deserialize<List<string>>(model.Tags)
                : null,
            ExclusivityTags = !string.IsNullOrEmpty(model.ExclusivityTags)
                ? BannouJson.Deserialize<List<string>>(model.ExclusivityTags)
                : null,
            Status = model.Status,
            CreatedAt = model.CreatedAt,
            UpdatedAt = model.UpdatedAt
        };
    }

    /// <summary>
    /// Invalidates cache entries for a scenario definition.
    /// </summary>
    private async Task InvalidateScenarioCacheAsync(ScenarioDefinitionModel model, CancellationToken cancellationToken)
    {
        await _scenarioCacheStore.DeleteAsync($"id:{model.ScenarioId}", cancellationToken);
        await _scenarioCacheStore.DeleteAsync($"code:{model.Code}:{model.RealmId}:{model.GameServiceId}", cancellationToken);
    }

    /// <summary>
    /// Evaluates trigger conditions against provided character state.
    /// </summary>
    private (bool conditionsMet, double fitScore) EvaluateConditions(
        ScenarioDefinitionModel scenario,
        CharacterStateSnapshot? state)
    {
        if (string.IsNullOrEmpty(scenario.TriggerConditions))
        {
            return (true, _configuration.ScenarioFitScoreBaseWeight);
        }

        var conditions = BannouJson.Deserialize<List<TriggerCondition>>(scenario.TriggerConditions);
        if (conditions == null || conditions.Count == 0)
        {
            return (true, _configuration.ScenarioFitScoreBaseWeight);
        }

        if (state == null)
        {
            // No state provided - cannot evaluate conditions
            return (false, 0);
        }

        var fitScore = _configuration.ScenarioFitScoreBaseWeight;
        var allConditionsMet = true;

        foreach (var condition in conditions)
        {
            var (met, bonus) = EvaluateSingleCondition(condition, state);
            if (!met && condition.Required != false)
            {
                allConditionsMet = false;
            }
            if (met)
            {
                fitScore += bonus;
            }
        }

        return (allConditionsMet, Math.Min(1.0, fitScore));
    }

    /// <summary>
    /// Evaluates a single trigger condition.
    /// </summary>
    private (bool met, double bonus) EvaluateSingleCondition(TriggerCondition condition, CharacterStateSnapshot state)
    {
        switch (condition.ConditionType)
        {
            case TriggerConditionType.TraitRange:
                if (string.IsNullOrEmpty(condition.TraitAxis) || state.Traits == null)
                    return (false, 0);
                if (state.Traits.TryGetValue(condition.TraitAxis, out var traitValue))
                {
                    var inRange = (!condition.TraitMin.HasValue || traitValue >= condition.TraitMin.Value) &&
                                  (!condition.TraitMax.HasValue || traitValue <= condition.TraitMax.Value);
                    return (inRange, inRange ? _configuration.ScenarioTraitMatchBonus : 0);
                }
                return (false, 0);

            case TriggerConditionType.BackstoryElement:
                if (string.IsNullOrEmpty(condition.BackstoryKey) || state.BackstoryKeys == null)
                    return (false, 0);
                var hasBackstory = state.BackstoryKeys.Contains(condition.BackstoryKey);
                return (hasBackstory, hasBackstory ? _configuration.ScenarioBackstoryMatchBonus : 0);

            case TriggerConditionType.RelationshipExists:
                if (string.IsNullOrEmpty(condition.RelationshipType) || state.RelationshipTypes == null)
                    return (false, 0);
                var hasRelationship = state.RelationshipTypes.Contains(condition.RelationshipType);
                return (hasRelationship, hasRelationship ? _configuration.ScenarioRelationshipMatchBonus : 0);

            case TriggerConditionType.RelationshipMissing:
                if (string.IsNullOrEmpty(condition.RelationshipType) || state.RelationshipTypes == null)
                    return (true, _configuration.ScenarioRelationshipMatchBonus); // Missing is true if no data
                var lacksRelationship = !state.RelationshipTypes.Contains(condition.RelationshipType);
                return (lacksRelationship, lacksRelationship ? _configuration.ScenarioRelationshipMatchBonus : 0);

            case TriggerConditionType.AgeRange:
                if (!state.Age.HasValue)
                    return (false, 0);
                var ageInRange = (!condition.AgeMin.HasValue || state.Age.Value >= condition.AgeMin.Value) &&
                                 (!condition.AgeMax.HasValue || state.Age.Value <= condition.AgeMax.Value);
                return (ageInRange, ageInRange ? _configuration.ScenarioTraitMatchBonus : 0);

            case TriggerConditionType.LocationAt:
                if (!condition.LocationId.HasValue || !state.LocationId.HasValue)
                    return (false, 0);
                var atLocation = state.LocationId.Value == condition.LocationId.Value;
                return (atLocation, atLocation ? _configuration.ScenarioLocationMatchBonus : 0);

            case TriggerConditionType.WorldState:
                if (string.IsNullOrEmpty(condition.WorldStateKey) || state.WorldState == null)
                    return (false, 0);
                if (state.WorldState.TryGetValue(condition.WorldStateKey, out var worldValue))
                {
                    var matches = condition.WorldStateValue == null || worldValue == condition.WorldStateValue;
                    return (matches, matches ? _configuration.ScenarioWorldStateMatchBonus : 0);
                }
                return (false, 0);

            case TriggerConditionType.Custom:
                // Custom conditions are not evaluated server-side
                return (true, 0);

            default:
                return (false, 0);
        }
    }

    /// <summary>
    /// Evaluates conditions with detailed results for testing.
    /// </summary>
    private List<ConditionResult> EvaluateConditionDetails(ScenarioDefinitionModel scenario, CharacterStateSnapshot? state)
    {
        var results = new List<ConditionResult>();

        if (string.IsNullOrEmpty(scenario.TriggerConditions))
        {
            return results;
        }

        var conditions = BannouJson.Deserialize<List<TriggerCondition>>(scenario.TriggerConditions);
        if (conditions == null)
        {
            return results;
        }

        foreach (var condition in conditions)
        {
            var (met, _) = state != null
                ? EvaluateSingleCondition(condition, state)
                : (false, 0);

            results.Add(new ConditionResult
            {
                ConditionType = condition.ConditionType,
                Met = met,
                Description = DescribeCondition(condition)
            });
        }

        return results;
    }

    /// <summary>
    /// Describes a condition in human-readable form.
    /// </summary>
    private static string DescribeCondition(TriggerCondition condition)
    {
        return condition.ConditionType switch
        {
            TriggerConditionType.TraitRange => $"Trait {condition.TraitAxis} in range [{condition.TraitMin}, {condition.TraitMax}]",
            TriggerConditionType.BackstoryElement => $"Has backstory element: {condition.BackstoryKey}",
            TriggerConditionType.RelationshipExists => $"Has relationship type: {condition.RelationshipType}",
            TriggerConditionType.RelationshipMissing => $"Lacks relationship type: {condition.RelationshipType}",
            TriggerConditionType.AgeRange => $"Age in range [{condition.AgeMin}, {condition.AgeMax}]",
            TriggerConditionType.LocationAt => $"At location: {condition.LocationId}",
            TriggerConditionType.WorldState => $"World state {condition.WorldStateKey} = {condition.WorldStateValue}",
            TriggerConditionType.Custom => "Custom condition (client-evaluated)",
            _ => "Unknown condition"
        };
    }

    /// <summary>
    /// Describes a mutation in human-readable form.
    /// </summary>
    private static string DescribeMutation(ScenarioMutation mutation)
    {
        return mutation.MutationType switch
        {
            MutationType.PersonalityEvolve => $"Evolve personality via experience: {mutation.ExperienceType}",
            MutationType.BackstoryAdd => $"Add backstory element: {mutation.BackstoryKey}",
            MutationType.RelationshipCreate => $"Create relationship type: {mutation.RelationshipTypeCode}",
            MutationType.RelationshipEnd => $"End relationship type: {mutation.RelationshipTypeCode}",
            MutationType.Custom => $"Custom mutation: {mutation.CustomMutationType}",
            _ => "Unknown mutation"
        };
    }

    /// <summary>
    /// Gets the count of active scenarios for a character.
    /// </summary>
    private async Task<int> GetActiveScenarioCountAsync(Guid characterId, CancellationToken cancellationToken)
    {
        var activeKey = $"character:{characterId}";
        var count = await _scenarioActiveStore.SetCountAsync(activeKey, cancellationToken);
        return (int)count;
    }

    /// <summary>
    /// Checks if a scenario is excluded by an active scenario's exclusivity tags.
    /// </summary>
    private async Task<bool> IsExcludedByActiveScenarioAsync(
        Guid characterId,
        ScenarioDefinitionModel candidate,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(candidate.ExclusivityTags))
        {
            return false;
        }

        var candidateTags = BannouJson.Deserialize<List<string>>(candidate.ExclusivityTags);
        if (candidateTags == null || candidateTags.Count == 0)
        {
            return false;
        }

        var activeKey = $"character:{characterId}";
        var executionIds = await _scenarioActiveStore.SetMembersAsync(activeKey, cancellationToken);

        foreach (var executionIdStr in executionIds)
        {
            var execution = await _scenarioExecutionStore.GetAsync(executionIdStr, cancellationToken);
            if (execution == null || execution.Status != ScenarioStatus.Active)
            {
                continue;
            }

            var activeScenario = await _scenarioDefinitionStore.GetAsync(execution.ScenarioId.ToString(), cancellationToken);
            if (activeScenario == null || string.IsNullOrEmpty(activeScenario.ExclusivityTags))
            {
                continue;
            }

            var activeTags = BannouJson.Deserialize<List<string>>(activeScenario.ExclusivityTags);
            if (activeTags != null && activeTags.Intersect(candidateTags).Any())
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Applies mutations defined in the scenario.
    /// </summary>
    private async Task<(int mutationsApplied, int questsSpawned, List<Guid> questIds)> ApplyMutationsAsync(
        ScenarioDefinitionModel scenario,
        ScenarioExecutionModel execution,
        CancellationToken cancellationToken)
    {
        var mutationsApplied = 0;
        var questsSpawned = 0;
        var questIds = new List<Guid>();

        // Apply mutations
        if (!string.IsNullOrEmpty(scenario.Mutations))
        {
            var mutations = BannouJson.Deserialize<List<ScenarioMutation>>(scenario.Mutations);
            if (mutations != null)
            {
                foreach (var mutation in mutations)
                {
                    var success = await ApplySingleMutationAsync(mutation, execution.PrimaryCharacterId, cancellationToken);
                    if (success)
                    {
                        mutationsApplied++;
                    }
                }
            }
        }

        // Spawn quests
        if (!string.IsNullOrEmpty(scenario.QuestHooks))
        {
            var questHooks = BannouJson.Deserialize<List<ScenarioQuestHook>>(scenario.QuestHooks);
            if (questHooks != null)
            {
                foreach (var hook in questHooks)
                {
                    var questId = await SpawnQuestAsync(hook, execution.PrimaryCharacterId, cancellationToken);
                    if (questId.HasValue)
                    {
                        questsSpawned++;
                        questIds.Add(questId.Value);
                    }
                }
            }
        }

        return (mutationsApplied, questsSpawned, questIds);
    }

    /// <summary>
    /// Applies a single mutation.
    /// </summary>
    private async Task<bool> ApplySingleMutationAsync(
        ScenarioMutation mutation,
        Guid characterId,
        CancellationToken cancellationToken)
    {
        try
        {
            switch (mutation.MutationType)
            {
                case MutationType.PersonalityEvolve:
                    return await ApplyPersonalityMutationAsync(mutation, characterId, cancellationToken);

                case MutationType.BackstoryAdd:
                    return await ApplyBackstoryMutationAsync(mutation, characterId, cancellationToken);

                case MutationType.RelationshipCreate:
                    return await ApplyRelationshipCreateMutationAsync(mutation, characterId, cancellationToken);

                case MutationType.RelationshipEnd:
                    return await ApplyRelationshipEndMutationAsync(mutation, characterId, cancellationToken);

                case MutationType.Custom:
                    _logger.LogDebug("Custom mutation type not applied server-side: {CustomType}", mutation.CustomMutationType);
                    return true;

                default:
                    _logger.LogWarning("Unknown mutation type: {MutationType}", mutation.MutationType);
                    return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to apply mutation {MutationType}", mutation.MutationType);
            return false;
        }
    }

    /// <summary>
    /// Applies personality mutation via soft L4 dependency.
    /// </summary>
    private async Task<bool> ApplyPersonalityMutationAsync(
        ScenarioMutation mutation,
        Guid characterId,
        CancellationToken cancellationToken)
    {
        // Soft L4 dependency - runtime resolution with graceful degradation
        var client = _serviceProvider.GetService<ICharacterPersonalityClient>();
        if (client == null)
        {
            _logger.LogDebug("Character personality service unavailable, skipping personality mutation");
            return true; // Graceful degradation, not failure
        }

        if (!mutation.ExperienceType.HasValue)
        {
            _logger.LogWarning("Personality mutation missing experienceType");
            return false;
        }

        try
        {
            var response = await client.RecordExperienceAsync(new RecordExperienceRequest
            {
                CharacterId = characterId,
                ExperienceType = mutation.ExperienceType.Value,
                Intensity = mutation.ExperienceIntensity ?? 0.5
            }, cancellationToken);

            return response.Applied;
        }
        catch (ApiException ex)
        {
            _logger.LogWarning(ex, "Personality mutation failed, continuing with scenario");
            return false;
        }
    }

    /// <summary>
    /// Applies backstory mutation via soft L4 dependency.
    /// </summary>
    private async Task<bool> ApplyBackstoryMutationAsync(
        ScenarioMutation mutation,
        Guid characterId,
        CancellationToken cancellationToken)
    {
        // Soft L4 dependency - runtime resolution with graceful degradation
        var client = _serviceProvider.GetService<ICharacterHistoryClient>();
        if (client == null)
        {
            _logger.LogDebug("Character history service unavailable, skipping backstory mutation");
            return true; // Graceful degradation, not failure
        }

        if (string.IsNullOrEmpty(mutation.BackstoryKey))
        {
            _logger.LogWarning("Backstory mutation missing backstoryKey");
            return false;
        }

        try
        {
            var (status, _) = await client.AddBackstoryElementAsync(new AddBackstoryElementRequest
            {
                CharacterId = characterId,
                ElementKey = mutation.BackstoryKey,
                ElementValue = mutation.BackstoryValue ?? string.Empty,
                Category = mutation.BackstoryCategory
            }, cancellationToken);

            return status == StatusCodes.OK;
        }
        catch (ApiException ex)
        {
            _logger.LogWarning(ex, "Backstory mutation failed, continuing with scenario");
            return false;
        }
    }

    /// <summary>
    /// Applies relationship create mutation via hard L2 dependency.
    /// </summary>
    private async Task<bool> ApplyRelationshipCreateMutationAsync(
        ScenarioMutation mutation,
        Guid characterId,
        CancellationToken cancellationToken)
    {
        // Hard L2 dependency - constructor injection, crash if missing
        if (string.IsNullOrEmpty(mutation.RelationshipTypeCode) || !mutation.TargetEntityId.HasValue)
        {
            _logger.LogWarning("Relationship create mutation missing typeCode or targetEntityId");
            return false;
        }

        try
        {
            var (status, _) = await _relationshipClient.CreateRelationshipAsync(new CreateRelationshipRequest
            {
                SourceEntityId = characterId,
                SourceEntityType = "character",
                TargetEntityId = mutation.TargetEntityId.Value,
                TargetEntityType = mutation.TargetEntityType ?? "character",
                RelationshipTypeCode = mutation.RelationshipTypeCode
            }, cancellationToken);

            return status == StatusCodes.OK;
        }
        catch (ApiException ex)
        {
            _logger.LogWarning(ex, "Relationship create failed with status {Status}", ex.StatusCode);
            return false;
        }
    }

    /// <summary>
    /// Applies relationship end mutation via hard L2 dependency.
    /// </summary>
    private async Task<bool> ApplyRelationshipEndMutationAsync(
        ScenarioMutation mutation,
        Guid characterId,
        CancellationToken cancellationToken)
    {
        // Hard L2 dependency - constructor injection, crash if missing
        if (!mutation.RelationshipId.HasValue)
        {
            _logger.LogWarning("Relationship end mutation missing relationshipId");
            return false;
        }

        try
        {
            var status = await _relationshipClient.EndRelationshipAsync(new EndRelationshipRequest
            {
                RelationshipId = mutation.RelationshipId.Value,
                Reason = mutation.EndReason ?? "Scenario outcome"
            }, cancellationToken);

            return status == StatusCodes.OK;
        }
        catch (ApiException ex)
        {
            _logger.LogWarning(ex, "Relationship end failed with status {Status}", ex.StatusCode);
            return false;
        }
    }

    /// <summary>
    /// Spawns a quest from a quest hook via soft L4 dependency.
    /// </summary>
    private async Task<Guid?> SpawnQuestAsync(
        ScenarioQuestHook hook,
        Guid characterId,
        CancellationToken cancellationToken)
    {
        // Soft L4 dependency - runtime resolution with graceful degradation
        var client = _serviceProvider.GetService<IQuestClient>();
        if (client == null)
        {
            _logger.LogDebug("Quest service unavailable, skipping quest spawn");
            return null; // Graceful degradation
        }

        try
        {
            var (status, response) = await client.CreateFromTemplateAsync(new CreateQuestFromTemplateRequest
            {
                TemplateId = hook.QuestTemplateId,
                OwnerCharacterId = characterId,
                AutoStart = hook.AutoStart ?? true
            }, cancellationToken);

            if (status == StatusCodes.OK && response != null)
            {
                return response.QuestId;
            }

            return null;
        }
        catch (ApiException ex)
        {
            _logger.LogWarning(ex, "Quest spawn failed, continuing with scenario");
            return null;
        }
    }

    /// <summary>
    /// Publishes scenario triggered event.
    /// </summary>
    private async Task PublishScenarioTriggeredEventAsync(
        ScenarioExecutionModel execution,
        ScenarioDefinitionModel scenario,
        double fitScore,
        CancellationToken cancellationToken)
    {
        var phases = !string.IsNullOrEmpty(scenario.Phases)
            ? BannouJson.Deserialize<List<ScenarioPhase>>(scenario.Phases)
            : null;

        var triggeredEvent = new ScenarioTriggeredEvent
        {
            ExecutionId = execution.ExecutionId,
            ScenarioId = execution.ScenarioId,
            ScenarioCode = execution.ScenarioCode,
            PrimaryCharacterId = execution.PrimaryCharacterId,
            AdditionalParticipantIds = !string.IsNullOrEmpty(execution.AdditionalParticipantIds)
                ? BannouJson.Deserialize<List<Guid>>(execution.AdditionalParticipantIds)
                : null,
            OrchestratorId = execution.OrchestratorId,
            RealmId = execution.RealmId,
            GameServiceId = execution.GameServiceId,
            FitScore = fitScore,
            PhaseCount = phases?.Count,
            TriggeredAt = execution.StartedAt
        };

        await _messageBus.TryPublishAsync("storyline.scenario.triggered", triggeredEvent, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Publishes scenario completed event.
    /// </summary>
    private async Task PublishScenarioCompletedEventAsync(
        ScenarioExecutionModel execution,
        int mutationsApplied,
        int questsSpawned,
        List<Guid> questIds,
        CancellationToken cancellationToken)
    {
        var completedEvent = new ScenarioCompletedEvent
        {
            ExecutionId = execution.ExecutionId,
            ScenarioId = execution.ScenarioId,
            ScenarioCode = execution.ScenarioCode,
            PrimaryCharacterId = execution.PrimaryCharacterId,
            AdditionalParticipantIds = !string.IsNullOrEmpty(execution.AdditionalParticipantIds)
                ? BannouJson.Deserialize<List<Guid>>(execution.AdditionalParticipantIds)
                : null,
            OrchestratorId = execution.OrchestratorId,
            RealmId = execution.RealmId,
            GameServiceId = execution.GameServiceId,
            PhasesCompleted = 1, // MVP: single phase
            TotalMutationsApplied = mutationsApplied,
            TotalQuestsSpawned = questsSpawned,
            QuestIds = questIds.Count > 0 ? questIds : null,
            DurationMs = execution.CompletedAt.HasValue
                ? (int)(execution.CompletedAt.Value - execution.StartedAt).TotalMilliseconds
                : null,
            StartedAt = execution.StartedAt,
            CompletedAt = execution.CompletedAt ?? DateTimeOffset.UtcNow
        };

        await _messageBus.TryPublishAsync("storyline.scenario.completed", completedEvent, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Publishes scenario failed event.
    /// </summary>
    private async Task PublishScenarioFailedEventAsync(
        ScenarioExecutionModel execution,
        string failureReason,
        bool isRecoverable,
        CancellationToken cancellationToken)
    {
        var failedEvent = new ScenarioFailedEvent
        {
            ExecutionId = execution.ExecutionId,
            ScenarioId = execution.ScenarioId,
            ScenarioCode = execution.ScenarioCode,
            PrimaryCharacterId = execution.PrimaryCharacterId,
            OrchestratorId = execution.OrchestratorId,
            RealmId = execution.RealmId,
            GameServiceId = execution.GameServiceId,
            FailureReason = failureReason,
            FailedAtPhase = execution.CurrentPhaseIndex,
            FailedAtPhaseName = null, // MVP: single unnamed phase
            PartialMutationsApplied = execution.MutationsApplied,
            IsRecoverable = isRecoverable,
            FailedAt = DateTimeOffset.UtcNow
        };

        await _messageBus.TryPublishAsync("storyline.scenario.failed", failedEvent, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Publishes scenario available event.
    /// </summary>
    private async Task PublishScenarioAvailableEventAsync(
        Guid characterId,
        Guid? realmId,
        Guid? gameServiceId,
        List<AvailableScenario> availableScenarios,
        CancellationToken cancellationToken)
    {
        var topFitScore = availableScenarios.Count > 0 ? availableScenarios[0].FitScore : null;
        var triggerRecommended = availableScenarios.Any(s => s.TriggerRecommended == true);

        var availableEvent = new ScenarioAvailableEvent
        {
            CharacterId = characterId,
            RealmId = realmId,
            GameServiceId = gameServiceId,
            AvailableScenarioIds = availableScenarios.Select(s => s.ScenarioId).ToList(),
            AvailableScenarioCodes = availableScenarios.Select(s => s.Code).ToList(),
            TopFitScore = topFitScore,
            TriggerRecommended = triggerRecommended,
            DetectedAt = DateTimeOffset.UtcNow
        };

        await _messageBus.TryPublishAsync("storyline.scenario.available", availableEvent, cancellationToken: cancellationToken);
    }

    #endregion
}

/// <summary>
/// Internal model for cached storyline plans.
/// </summary>
internal sealed class CachedPlan
{
    public required Guid PlanId { get; init; }
    public required StorylineGoal Goal { get; init; }
    public required ArcType ArcType { get; init; }
    public required SpectrumType PrimarySpectrum { get; init; }
    public string? Genre { get; init; }
    public double Confidence { get; init; }
    public List<StorylinePlanPhase>? Phases { get; init; }
    public List<EntityRequirement>? EntitiesToSpawn { get; init; }
    public List<StorylineLink>? Links { get; init; }
    public List<StorylineRisk>? Risks { get; init; }
    public List<string>? Themes { get; init; }
    public Guid? RealmId { get; init; }
    public List<Guid>? ArchiveIds { get; init; }
    public List<Guid>? SnapshotIds { get; init; }
    public int? Seed { get; init; }
    public int GenerationTimeMs { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset ExpiresAt { get; init; }
}

/// <summary>
/// Internal model for plan index entries.
/// </summary>
internal sealed class PlanIndexEntry
{
    public required Guid PlanId { get; init; }
    public required Guid RealmId { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
}

/// <summary>
/// Internal model for scenario definitions stored in MySQL.
/// Uses JSON serialization for complex fields per IMPLEMENTATION TENETS.
/// </summary>
internal sealed class ScenarioDefinitionModel
{
    public required Guid ScenarioId { get; init; }
    public required string Code { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public Guid? RealmId { get; init; }
    public Guid? GameServiceId { get; init; }
    public required string TriggerConditions { get; set; } // JSON serialized
    public required string Phases { get; set; } // JSON serialized
    public string? Mutations { get; set; } // JSON serialized
    public string? QuestHooks { get; set; } // JSON serialized
    public int? CooldownSeconds { get; set; }
    public string? Tags { get; set; } // JSON serialized
    public string? ExclusivityTags { get; set; } // JSON serialized
    public ScenarioStatus Status { get; set; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// Internal model for scenario executions stored in MySQL.
/// </summary>
internal sealed class ScenarioExecutionModel
{
    public required Guid ExecutionId { get; init; }
    public required Guid ScenarioId { get; init; }
    public required string ScenarioCode { get; init; }
    public required Guid PrimaryCharacterId { get; init; }
    public string? AdditionalParticipantIds { get; init; } // JSON serialized
    public Guid? OrchestratorId { get; init; }
    public Guid? RealmId { get; init; }
    public Guid? GameServiceId { get; init; }
    public double? FitScore { get; init; }
    public ScenarioStatus Status { get; set; }
    public int CurrentPhaseIndex { get; set; }
    public int MutationsApplied { get; set; }
    public int QuestsSpawned { get; set; }
    public string? QuestIds { get; set; } // JSON serialized
    public string? FailureReason { get; set; }
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; set; }
}

/// <summary>
/// Internal marker for scenario cooldowns stored in Redis with TTL.
/// </summary>
internal sealed class CooldownMarker
{
    public required Guid ScenarioId { get; init; }
    public required Guid CharacterId { get; init; }
    public required DateTimeOffset ExpiresAt { get; init; }
}

/// <summary>
/// Internal entry for active scenarios stored in Redis sets.
/// </summary>
internal sealed class ActiveScenarioEntry
{
    public required Guid ExecutionId { get; init; }
    public required DateTimeOffset StartedAt { get; init; }
}

/// <summary>
/// Internal marker for idempotency keys stored in Redis with TTL.
/// </summary>
internal sealed class IdempotencyMarker
{
    public required Guid ExecutionId { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
}
