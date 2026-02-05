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
using BeyondImmersion.BannouService.Events;
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

    // State stores - use StateStoreDefinitions constants per IMPLEMENTATION TENETS
    private readonly IStateStore<CachedPlan> _planStore;
    private readonly ICacheableStateStore<PlanIndexEntry> _planIndexStore;

    // SDK - direct instantiation (pure computation, not DI)
    private readonly StorylineComposer _composer;

    /// <summary>
    /// Creates a new StorylineService instance.
    /// </summary>
    public StorylineService(
        IMessageBus messageBus,
        IStateStoreFactory stateStoreFactory,
        IResourceClient resourceClient,
        ILogger<StorylineService> logger,
        StorylineServiceConfiguration configuration)
    {
        // Null checks with ArgumentNullException - per IMPLEMENTATION TENETS
        ArgumentNullException.ThrowIfNull(messageBus);
        ArgumentNullException.ThrowIfNull(stateStoreFactory);
        ArgumentNullException.ThrowIfNull(resourceClient);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(configuration);

        _messageBus = messageBus;
        _stateStoreFactory = stateStoreFactory;
        _resourceClient = resourceClient;
        _logger = logger;
        _configuration = configuration;

        // Use StateStoreDefinitions constants per IMPLEMENTATION TENETS
        _planStore = stateStoreFactory.GetStore<CachedPlan>(StateStoreDefinitions.StorylinePlans);
        _planIndexStore = stateStoreFactory.GetCacheableStore<PlanIndexEntry>(StateStoreDefinitions.StorylinePlanIndex);

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
