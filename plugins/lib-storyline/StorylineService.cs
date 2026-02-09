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
    private readonly IRelationshipClient _relationshipClient;
    private readonly IServiceProvider _serviceProvider;
    private readonly IDistributedLockProvider _lockProvider;
    private readonly ILogger<StorylineService> _logger;
    private readonly StorylineServiceConfiguration _configuration;

    // State stores - use StateStoreDefinitions constants per IMPLEMENTATION TENETS
    private readonly IStateStore<CachedPlan> _planStore;
    private readonly ICacheableStateStore<PlanIndexEntry> _planIndexStore;

    // Scenario state stores (MySQL stores use IQueryableStateStore for QueryAsync support)
    private readonly IQueryableStateStore<ScenarioDefinitionModel> _scenarioDefinitionStore;
    private readonly ICacheableStateStore<ScenarioDefinitionModel> _scenarioCacheStore;
    private readonly IQueryableStateStore<ScenarioExecutionModel> _scenarioExecutionStore;
    private readonly ICacheableStateStore<CooldownMarker> _scenarioCooldownStore;
    private readonly ICacheableStateStore<ActiveScenarioEntry> _scenarioActiveStore;
    private readonly ICacheableStateStore<IdempotencyMarker> _scenarioIdempotencyStore;

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
        IServiceProvider serviceProvider,
        IDistributedLockProvider lockProvider,
        ILogger<StorylineService> logger,
        StorylineServiceConfiguration configuration)
    {
        // Null checks with ArgumentNullException - per IMPLEMENTATION TENETS
        ArgumentNullException.ThrowIfNull(messageBus);
        ArgumentNullException.ThrowIfNull(stateStoreFactory);
        ArgumentNullException.ThrowIfNull(resourceClient);
        ArgumentNullException.ThrowIfNull(relationshipClient);
        ArgumentNullException.ThrowIfNull(serviceProvider);
        ArgumentNullException.ThrowIfNull(lockProvider);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(configuration);

        _messageBus = messageBus;
        _stateStoreFactory = stateStoreFactory;
        _resourceClient = resourceClient;
        _relationshipClient = relationshipClient;
        _serviceProvider = serviceProvider;
        _lockProvider = lockProvider;
        _logger = logger;
        _configuration = configuration;

        // Use StateStoreDefinitions constants per IMPLEMENTATION TENETS
        _planStore = stateStoreFactory.GetStore<CachedPlan>(StateStoreDefinitions.StorylinePlans);
        _planIndexStore = stateStoreFactory.GetCacheableStore<PlanIndexEntry>(StateStoreDefinitions.StorylinePlanIndex);

        // Scenario state stores per IMPLEMENTATION TENETS
        // MySQL stores use GetQueryableStore for QueryAsync support
        _scenarioDefinitionStore = stateStoreFactory.GetQueryableStore<ScenarioDefinitionModel>(StateStoreDefinitions.StorylineScenarioDefinitions);
        _scenarioCacheStore = stateStoreFactory.GetCacheableStore<ScenarioDefinitionModel>(StateStoreDefinitions.StorylineScenarioCache);
        _scenarioExecutionStore = stateStoreFactory.GetQueryableStore<ScenarioExecutionModel>(StateStoreDefinitions.StorylineScenarioExecutions);
        _scenarioCooldownStore = stateStoreFactory.GetCacheableStore<CooldownMarker>(StateStoreDefinitions.StorylineScenarioCooldown);
        _scenarioActiveStore = stateStoreFactory.GetCacheableStore<ActiveScenarioEntry>(StateStoreDefinitions.StorylineScenarioActive);
        _scenarioIdempotencyStore = stateStoreFactory.GetCacheableStore<IdempotencyMarker>(StateStoreDefinitions.StorylineScenarioIdempotency);

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

    // ═══════════════════════════════════════════════════════════════════════════
    // SCENARIO DEFINITION CRUD
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Creates a new scenario definition.
    /// </summary>
    public async Task<(StatusCodes, ScenarioDefinition?)> CreateScenarioDefinitionAsync(
        CreateScenarioDefinitionRequest body,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Creating scenario definition with code {Code}", body.Code);

        try
        {
            // Validate code format (uppercase with underscores)
            var normalizedCode = body.Code.ToUpperInvariant().Replace('-', '_');
            if (normalizedCode != body.Code)
            {
                _logger.LogWarning("Scenario code should be uppercase with underscores: {Code}", body.Code);
            }

            // Check for duplicate code within scope
            var existingByCode = await FindScenarioByCodeAsync(normalizedCode, body.RealmId, body.GameServiceId, cancellationToken);
            if (existingByCode is not null)
            {
                _logger.LogWarning("Scenario code {Code} already exists", normalizedCode);
                return (StatusCodes.Conflict, null);
            }

            var scenarioId = Guid.NewGuid();
            var now = DateTimeOffset.UtcNow;
            var etag = Guid.NewGuid().ToString("N");

            // Create storage model with JSON-serialized nested objects
            var model = new ScenarioDefinitionModel
            {
                ScenarioId = scenarioId,
                Code = normalizedCode,
                Name = body.Name,
                Description = body.Description,
                TriggerConditionsJson = BannouJson.Serialize(body.TriggerConditions),
                PhasesJson = BannouJson.Serialize(body.Phases),
                MutationsJson = body.Mutations is not null ? BannouJson.Serialize(body.Mutations) : null,
                QuestHooksJson = body.QuestHooks is not null ? BannouJson.Serialize(body.QuestHooks) : null,
                CooldownSeconds = body.CooldownSeconds,
                ExclusivityTagsJson = body.ExclusivityTags is not null ? BannouJson.Serialize(body.ExclusivityTags) : null,
                Priority = body.Priority,
                Enabled = body.Enabled,
                RealmId = body.RealmId,
                GameServiceId = body.GameServiceId,
                TagsJson = body.Tags is not null ? BannouJson.Serialize(body.Tags) : null,
                Deprecated = false,
                CreatedAt = now,
                UpdatedAt = null,
                Etag = etag
            };

            // Save to MySQL (durable store)
            await _scenarioDefinitionStore.SaveAsync(scenarioId.ToString(), model, null, cancellationToken);

            // Cache in Redis
            var cacheTtl = _configuration.ScenarioDefinitionCacheTtlSeconds;
            await _scenarioCacheStore.SaveAsync(
                scenarioId.ToString(),
                model,
                new StateOptions { Ttl = cacheTtl },
                cancellationToken);

            // Build response
            var response = BuildScenarioDefinitionResponse(model);

            _logger.LogInformation("Created scenario definition {ScenarioId} with code {Code}", scenarioId, normalizedCode);
            return (StatusCodes.OK, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating scenario definition");
            await _messageBus.TryPublishErrorAsync(
                "storyline",
                "CreateScenarioDefinition",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                endpoint: "post:/storyline/scenario/create",
                details: null,
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
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
        _logger.LogDebug("Getting scenario definition by ID {ScenarioId} or code {Code}",
            body.ScenarioId, body.Code);

        try
        {
            ScenarioDefinitionModel? model = null;

            if (body.ScenarioId.HasValue)
            {
                model = await GetScenarioDefinitionWithCacheAsync(body.ScenarioId.Value, cancellationToken);
            }
            else if (!string.IsNullOrEmpty(body.Code))
            {
                var normalizedCode = body.Code.ToUpperInvariant().Replace('-', '_');
                model = await FindScenarioByCodeAsync(normalizedCode, realmId: null, gameServiceId: null, cancellationToken);
            }
            else
            {
                return (StatusCodes.BadRequest, null);
            }

            if (model is null)
            {
                return (StatusCodes.OK, new GetScenarioDefinitionResponse { Found = false, Scenario = null });
            }

            var response = new GetScenarioDefinitionResponse
            {
                Found = true,
                Scenario = BuildScenarioDefinitionResponse(model)
            };

            return (StatusCodes.OK, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting scenario definition");
            await _messageBus.TryPublishErrorAsync(
                "storyline",
                "GetScenarioDefinition",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                endpoint: "post:/storyline/scenario/get",
                details: null,
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Lists scenario definitions with optional filters.
    /// </summary>
    public async Task<(StatusCodes, ListScenarioDefinitionsResponse?)> ListScenarioDefinitionsAsync(
        ListScenarioDefinitionsRequest body,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Listing scenario definitions with realm {RealmId}, game {GameServiceId}, tags {Tags}",
            body.RealmId, body.GameServiceId, body.Tags);

        try
        {
            // Query all definitions from MySQL using IQueryableStateStore
            var allDefinitions = await _scenarioDefinitionStore.QueryAsync(d => true, cancellationToken);

            // Apply filters
            var filtered = allDefinitions.AsEnumerable();

            // Filter by realm (null matches global scenarios)
            if (body.RealmId.HasValue)
            {
                filtered = filtered.Where(d =>
                    !d.RealmId.HasValue || d.RealmId.Value == body.RealmId.Value);
            }

            // Filter by game service (null matches global scenarios)
            if (body.GameServiceId.HasValue)
            {
                filtered = filtered.Where(d =>
                    !d.GameServiceId.HasValue || d.GameServiceId.Value == body.GameServiceId.Value);
            }

            // Filter by tags (OR logic)
            if (body.Tags is not null && body.Tags.Count > 0)
            {
                var requestedTags = body.Tags.ToHashSet(StringComparer.OrdinalIgnoreCase);
                filtered = filtered.Where(d =>
                {
                    if (string.IsNullOrEmpty(d.TagsJson)) return false;
                    var tags = BannouJson.Deserialize<List<string>>(d.TagsJson);
                    return tags is not null && tags.Any(t => requestedTags.Contains(t));
                });
            }

            // Filter deprecated
            if (!body.IncludeDeprecated)
            {
                filtered = filtered.Where(d => !d.Deprecated);
            }

            // Get total count before pagination
            var filteredList = filtered.ToList();
            var totalCount = filteredList.Count;

            // Apply pagination
            var paginated = filteredList
                .OrderByDescending(d => d.Priority)
                .ThenBy(d => d.Code)
                .Skip(body.Offset)
                .Take(body.Limit)
                .ToList();

            // Build summaries
            var summaries = paginated.Select(BuildScenarioSummary).ToList();

            return (StatusCodes.OK, new ListScenarioDefinitionsResponse
            {
                Scenarios = summaries,
                TotalCount = totalCount
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing scenario definitions");
            await _messageBus.TryPublishErrorAsync(
                "storyline",
                "ListScenarioDefinitions",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                endpoint: "post:/storyline/scenario/list",
                details: null,
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Updates a scenario definition with optimistic concurrency.
    /// </summary>
    public async Task<(StatusCodes, ScenarioDefinition?)> UpdateScenarioDefinitionAsync(
        UpdateScenarioDefinitionRequest body,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Updating scenario definition {ScenarioId}", body.ScenarioId);

        try
        {
            // Get existing definition
            var existing = await GetScenarioDefinitionWithCacheAsync(body.ScenarioId, cancellationToken);
            if (existing is null)
            {
                return (StatusCodes.NotFound, null);
            }

            // Check ETag for optimistic concurrency
            if (existing.Etag != body.Etag)
            {
                _logger.LogWarning("ETag mismatch for scenario {ScenarioId}: expected {Expected}, got {Actual}",
                    body.ScenarioId, existing.Etag, body.Etag);
                return (StatusCodes.Conflict, null);
            }

            // Apply updates
            if (body.Name is not null)
                existing.Name = body.Name;
            if (body.Description is not null)
                existing.Description = body.Description;
            if (body.TriggerConditions is not null)
                existing.TriggerConditionsJson = BannouJson.Serialize(body.TriggerConditions);
            if (body.Phases is not null)
                existing.PhasesJson = BannouJson.Serialize(body.Phases);
            if (body.Mutations is not null)
                existing.MutationsJson = BannouJson.Serialize(body.Mutations);
            if (body.QuestHooks is not null)
                existing.QuestHooksJson = BannouJson.Serialize(body.QuestHooks);
            if (body.CooldownSeconds.HasValue)
                existing.CooldownSeconds = body.CooldownSeconds;
            if (body.ExclusivityTags is not null)
                existing.ExclusivityTagsJson = BannouJson.Serialize(body.ExclusivityTags);
            if (body.Priority.HasValue)
                existing.Priority = body.Priority.Value;
            if (body.Enabled.HasValue)
                existing.Enabled = body.Enabled.Value;
            if (body.Tags is not null)
                existing.TagsJson = BannouJson.Serialize(body.Tags);

            existing.UpdatedAt = DateTimeOffset.UtcNow;
            existing.Etag = Guid.NewGuid().ToString("N");

            // Save to MySQL
            await _scenarioDefinitionStore.SaveAsync(body.ScenarioId.ToString(), existing, null, cancellationToken);

            // Invalidate and update cache
            await _scenarioCacheStore.DeleteAsync(body.ScenarioId.ToString(), cancellationToken);
            await _scenarioCacheStore.SaveAsync(
                body.ScenarioId.ToString(),
                existing,
                new StateOptions { Ttl = _configuration.ScenarioDefinitionCacheTtlSeconds },
                cancellationToken);

            var response = BuildScenarioDefinitionResponse(existing);
            _logger.LogInformation("Updated scenario definition {ScenarioId}", body.ScenarioId);
            return (StatusCodes.OK, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating scenario definition");
            await _messageBus.TryPublishErrorAsync(
                "storyline",
                "UpdateScenarioDefinition",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                endpoint: "post:/storyline/scenario/update",
                details: null,
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Soft-deletes a scenario definition.
    /// </summary>
    public async Task<StatusCodes> DeprecateScenarioDefinitionAsync(
        DeprecateScenarioDefinitionRequest body,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Deprecating scenario definition {ScenarioId}", body.ScenarioId);

        try
        {
            var existing = await GetScenarioDefinitionWithCacheAsync(body.ScenarioId, cancellationToken);
            if (existing is null)
            {
                return StatusCodes.NotFound;
            }

            existing.Deprecated = true;
            existing.Enabled = false;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
            existing.Etag = Guid.NewGuid().ToString("N");

            await _scenarioDefinitionStore.SaveAsync(body.ScenarioId.ToString(), existing, null, cancellationToken);
            await _scenarioCacheStore.DeleteAsync(body.ScenarioId.ToString(), cancellationToken);

            _logger.LogInformation("Deprecated scenario definition {ScenarioId}", body.ScenarioId);
            return StatusCodes.OK;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deprecating scenario definition");
            await _messageBus.TryPublishErrorAsync(
                "storyline",
                "DeprecateScenarioDefinition",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                endpoint: "post:/storyline/scenario/deprecate",
                details: null,
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return StatusCodes.InternalServerError;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // SCENARIO DISCOVERY
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Finds scenarios matching character state with fit scores.
    /// </summary>
    public async Task<(StatusCodes, FindAvailableScenariosResponse?)> FindAvailableScenariosAsync(
        FindAvailableScenariosRequest body,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Finding available scenarios for character {CharacterId}", body.CharacterId);

        try
        {
            // Get all candidate scenarios using IQueryableStateStore
            var allDefinitions = await _scenarioDefinitionStore.QueryAsync(d => true, cancellationToken);

            // Filter by scope and enabled status
            var candidates = allDefinitions
                .Where(d => d.Enabled && !d.Deprecated)
                .Where(d => !d.RealmId.HasValue || d.RealmId == body.RealmId)
                .Where(d => !d.GameServiceId.HasValue || d.GameServiceId == body.GameServiceId)
                .ToList();

            // Filter by excluded tags
            if (body.ExcludeTags is not null && body.ExcludeTags.Count > 0)
            {
                var excludedTags = body.ExcludeTags.ToHashSet(StringComparer.OrdinalIgnoreCase);
                candidates = candidates.Where(d =>
                {
                    if (string.IsNullOrEmpty(d.TagsJson)) return true;
                    var tags = BannouJson.Deserialize<List<string>>(d.TagsJson);
                    return tags is null || !tags.Any(t => excludedTags.Contains(t));
                }).ToList();
            }

            var matches = new List<ScenarioMatch>();

            foreach (var definition in candidates)
            {
                var conditions = BannouJson.Deserialize<List<TriggerCondition>>(definition.TriggerConditionsJson)
                    ?? new List<TriggerCondition>();

                // Evaluate all conditions
                var (conditionsMet, fitScore) = EvaluateConditions(conditions, body.CharacterState, body.LocationId, body.TimeOfDay, body.WorldState);

                // Check if minimum threshold is met
                if (fitScore < _configuration.ScenarioFitScoreMinimumThreshold)
                {
                    continue;
                }

                // Check cooldown
                var cooldownKey = $"{body.CharacterId}:{definition.ScenarioId}";
                var cooldownMarker = await _scenarioCooldownStore.GetAsync(cooldownKey, cancellationToken);
                var onCooldown = cooldownMarker is not null;

                matches.Add(new ScenarioMatch
                {
                    ScenarioId = definition.ScenarioId,
                    Code = definition.Code,
                    Name = definition.Name,
                    FitScore = fitScore,
                    ConditionsMet = conditionsMet,
                    ConditionsTotal = conditions.Count,
                    OnCooldown = onCooldown,
                    CooldownExpiresAt = cooldownMarker?.ExpiresAt
                });
            }

            // Sort by fit score descending and take max results
            var sortedMatches = matches
                .OrderByDescending(m => m.FitScore)
                .ThenByDescending(m => m.ConditionsMet)
                .Take(body.MaxResults)
                .ToList();

            return (StatusCodes.OK, new FindAvailableScenariosResponse { Matches = sortedMatches });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error finding available scenarios");
            await _messageBus.TryPublishErrorAsync(
                "storyline",
                "FindAvailableScenarios",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                endpoint: "post:/storyline/scenario/find-available",
                details: null,
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Dry-run test of scenario trigger with detailed results.
    /// </summary>
    public async Task<(StatusCodes, TestScenarioResponse?)> TestScenarioTriggerAsync(
        TestScenarioRequest body,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Testing scenario {ScenarioId} for character {CharacterId}",
            body.ScenarioId, body.CharacterId);

        try
        {
            var definition = await GetScenarioDefinitionWithCacheAsync(body.ScenarioId, cancellationToken);
            if (definition is null)
            {
                return (StatusCodes.NotFound, null);
            }

            var conditions = BannouJson.Deserialize<List<TriggerCondition>>(definition.TriggerConditionsJson)
                ?? new List<TriggerCondition>();

            // Evaluate each condition with detailed results
            var conditionResults = new List<ConditionResult>();
            var allConditionsMet = true;

            foreach (var condition in conditions)
            {
                var (met, actualValue, expectedValue, details) = EvaluateSingleCondition(
                    condition, body.CharacterState, body.LocationId, body.TimeOfDay, body.WorldState);

                conditionResults.Add(new ConditionResult
                {
                    ConditionType = condition.ConditionType,
                    Met = met,
                    ActualValue = actualValue,
                    ExpectedValue = expectedValue,
                    Details = details
                });

                if (!met)
                {
                    allConditionsMet = false;
                }
            }

            // Check blocking reasons
            string? blockingReason = null;

            if (!definition.Enabled)
            {
                blockingReason = "Scenario is disabled";
            }
            else if (definition.Deprecated)
            {
                blockingReason = "Scenario is deprecated";
            }
            else if (!allConditionsMet)
            {
                blockingReason = "Not all conditions are met";
            }
            else
            {
                // Check cooldown
                var cooldownKey = $"{body.CharacterId}:{body.ScenarioId}";
                var cooldownMarker = await _scenarioCooldownStore.GetAsync(cooldownKey, cancellationToken);
                if (cooldownMarker is not null)
                {
                    blockingReason = $"On cooldown until {cooldownMarker.ExpiresAt:O}";
                }
                else
                {
                    // Check active scenario limit
                    var activeKey = body.CharacterId.ToString();
                    var activeCount = await _scenarioActiveStore.SetCountAsync(activeKey, cancellationToken);
                    if (activeCount >= _configuration.ScenarioMaxActivePerCharacter)
                    {
                        blockingReason = $"Character has {activeCount} active scenarios (max {_configuration.ScenarioMaxActivePerCharacter})";
                    }
                }
            }

            // Predict mutations
            var predictedMutations = new List<PredictedMutation>();
            if (allConditionsMet && blockingReason is null)
            {
                var mutations = BannouJson.Deserialize<List<ScenarioMutation>>(definition.MutationsJson ?? "[]")
                    ?? new List<ScenarioMutation>();

                foreach (var mutation in mutations)
                {
                    predictedMutations.Add(new PredictedMutation
                    {
                        MutationType = mutation.MutationType,
                        Description = DescribeMutation(mutation)
                    });
                }
            }

            return (StatusCodes.OK, new TestScenarioResponse
            {
                WouldTrigger = allConditionsMet && blockingReason is null,
                ConditionResults = conditionResults,
                PredictedMutations = predictedMutations,
                BlockingReason = blockingReason
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error testing scenario trigger");
            await _messageBus.TryPublishErrorAsync(
                "storyline",
                "TestScenarioTrigger",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                endpoint: "post:/storyline/scenario/test",
                details: null,
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Lightweight fit score evaluation without full condition details.
    /// </summary>
    public async Task<(StatusCodes, EvaluateFitResponse?)> EvaluateScenarioFitAsync(
        EvaluateFitRequest body,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Evaluating fit score for scenario {ScenarioId}", body.ScenarioId);

        try
        {
            var definition = await GetScenarioDefinitionWithCacheAsync(body.ScenarioId, cancellationToken);
            if (definition is null)
            {
                return (StatusCodes.NotFound, null);
            }

            var conditions = BannouJson.Deserialize<List<TriggerCondition>>(definition.TriggerConditionsJson)
                ?? new List<TriggerCondition>();

            var (conditionsMet, fitScore) = EvaluateConditions(
                conditions, body.CharacterState, locationId: null, timeOfDay: null, worldState: null);

            return (StatusCodes.OK, new EvaluateFitResponse
            {
                FitScore = fitScore,
                ConditionsMet = conditionsMet,
                ConditionsTotal = conditions.Count
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error evaluating scenario fit");
            await _messageBus.TryPublishErrorAsync(
                "storyline",
                "EvaluateScenarioFit",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                endpoint: "post:/storyline/scenario/evaluate-fit",
                details: null,
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // SCENARIO EXECUTION
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Triggers scenario execution with distributed locking and mutation application.
    /// </summary>
    public async Task<(StatusCodes, TriggerScenarioResponse?)> TriggerScenarioAsync(
        TriggerScenarioRequest body,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Triggering scenario {ScenarioId} for character {CharacterId}",
            body.ScenarioId, body.CharacterId);

        var stopwatch = Stopwatch.StartNew();
        var executionId = Guid.NewGuid();

        try
        {
            // Check idempotency if key provided
            if (!string.IsNullOrEmpty(body.IdempotencyKey))
            {
                var existingIdempotency = await _scenarioIdempotencyStore.GetAsync(body.IdempotencyKey, cancellationToken);
                if (existingIdempotency is not null)
                {
                    _logger.LogDebug("Returning idempotent result for key {Key}", body.IdempotencyKey);
                    // Return the existing execution - retrieve it from history
                    var existingExecution = await _scenarioExecutionStore.GetAsync(
                        existingIdempotency.ExecutionId.ToString(), cancellationToken);
                    if (existingExecution is not null)
                    {
                        return (StatusCodes.OK, BuildTriggerResponse(existingExecution));
                    }
                }
            }

            // Acquire distributed lock to prevent double-trigger
            var lockResource = $"{body.CharacterId}:{body.ScenarioId}";
            var lockOwner = $"scenario-trigger-{executionId:N}";
            await using var lockHandle = await _lockProvider.LockAsync(
                "storyline-scenario-lock",  // Lock store name
                lockResource,
                lockOwner,
                _configuration.ScenarioTriggerLockTimeoutSeconds,
                cancellationToken);

            if (!lockHandle.Success)
            {
                _logger.LogWarning("Failed to acquire lock for scenario trigger {ScenarioId}", body.ScenarioId);
                return (StatusCodes.Conflict, null);
            }

            // Get scenario definition
            var definition = await GetScenarioDefinitionWithCacheAsync(body.ScenarioId, cancellationToken);
            if (definition is null)
            {
                return (StatusCodes.NotFound, null);
            }

            if (!definition.Enabled || definition.Deprecated)
            {
                return (StatusCodes.BadRequest, null);
            }

            // Validate conditions unless skipped
            if (!body.SkipConditionCheck)
            {
                var conditions = BannouJson.Deserialize<List<TriggerCondition>>(definition.TriggerConditionsJson)
                    ?? new List<TriggerCondition>();

                var (conditionsMet, fitScore) = EvaluateConditions(
                    conditions, body.CharacterState, body.LocationId, body.TimeOfDay, body.WorldState);

                if (conditionsMet < conditions.Count)
                {
                    _logger.LogWarning("Not all conditions met for scenario {ScenarioId}", body.ScenarioId);
                    return (StatusCodes.BadRequest, null);
                }
            }

            // Check cooldown
            var cooldownKey = $"{body.CharacterId}:{body.ScenarioId}";
            var cooldownMarker = await _scenarioCooldownStore.GetAsync(cooldownKey, cancellationToken);
            if (cooldownMarker is not null)
            {
                _logger.LogWarning("Scenario {ScenarioId} on cooldown for character {CharacterId}",
                    body.ScenarioId, body.CharacterId);
                return (StatusCodes.Conflict, null);
            }

            // Check active scenario limit
            var activeKey = body.CharacterId.ToString();
            var activeCount = await _scenarioActiveStore.SetCountAsync(activeKey, cancellationToken);
            if (activeCount >= _configuration.ScenarioMaxActivePerCharacter)
            {
                _logger.LogWarning("Character {CharacterId} has max active scenarios", body.CharacterId);
                return (StatusCodes.Conflict, null);
            }

            var phases = BannouJson.Deserialize<List<ScenarioPhase>>(definition.PhasesJson)
                ?? new List<ScenarioPhase>();
            var mutations = BannouJson.Deserialize<List<ScenarioMutation>>(definition.MutationsJson ?? "[]")
                ?? new List<ScenarioMutation>();
            var questHooks = BannouJson.Deserialize<List<ScenarioQuestHook>>(definition.QuestHooksJson ?? "[]")
                ?? new List<ScenarioQuestHook>();

            var now = DateTimeOffset.UtcNow;

            // Create execution record
            var execution = new ScenarioExecutionModel
            {
                ExecutionId = executionId,
                ScenarioId = body.ScenarioId,
                ScenarioCode = definition.Code,
                ScenarioName = definition.Name,
                PrimaryCharacterId = body.CharacterId,
                AdditionalParticipantsJson = body.AdditionalParticipants is not null
                    ? BannouJson.Serialize(body.AdditionalParticipants)
                    : null,
                OrchestratorId = body.OrchestratorId,
                RealmId = definition.RealmId,
                GameServiceId = definition.GameServiceId,
                Status = ScenarioStatus.Active,
                CurrentPhase = 1,
                TotalPhases = phases.Count,
                FitScore = null,
                TriggeredAt = now
            };

            // Save execution record
            await _scenarioExecutionStore.SaveAsync(executionId.ToString(), execution, null, cancellationToken);

            // Add to active set
            var activeEntry = new ActiveScenarioEntry
            {
                ExecutionId = executionId,
                ScenarioId = body.ScenarioId,
                ScenarioCode = definition.Code
            };
            await _scenarioActiveStore.AddToSetAsync(activeKey, activeEntry, null, cancellationToken);

            // Store idempotency key if provided
            if (!string.IsNullOrEmpty(body.IdempotencyKey))
            {
                await _scenarioIdempotencyStore.SaveAsync(
                    body.IdempotencyKey,
                    new IdempotencyMarker { ExecutionId = executionId, CreatedAt = now },
                    new StateOptions { Ttl = _configuration.ScenarioIdempotencyTtlSeconds },
                    cancellationToken);
            }

            // Publish triggered event
            await _messageBus.TryPublishAsync("storyline.scenario.triggered", new ScenarioTriggeredEvent
            {
                ExecutionId = executionId,
                ScenarioId = body.ScenarioId,
                ScenarioCode = definition.Code,
                PrimaryCharacterId = body.CharacterId,
                AdditionalParticipantIds = body.AdditionalParticipants?.Values.ToList(),
                OrchestratorId = body.OrchestratorId,
                RealmId = definition.RealmId,
                GameServiceId = definition.GameServiceId,
                FitScore = null,
                PhaseCount = phases.Count,
                TriggeredAt = now
            }, cancellationToken: cancellationToken);

            // Apply mutations (Phase 1: all mutations applied immediately)
            var appliedMutations = new List<AppliedMutation>();
            foreach (var mutation in mutations)
            {
                var (success, details) = await ApplyMutationAsync(
                    mutation, body.CharacterId, body.AdditionalParticipants, cancellationToken);

                appliedMutations.Add(new AppliedMutation
                {
                    MutationType = mutation.MutationType,
                    Success = success,
                    TargetCharacterId = body.CharacterId,
                    Details = details
                });
            }

            // Spawn quests (Phase 1: immediate spawn, no delay)
            var spawnedQuests = new List<SpawnedQuest>();
            foreach (var hook in questHooks)
            {
                var (questId, questSpawned) = await SpawnQuestAsync(hook, body.CharacterId, cancellationToken);
                if (questSpawned && questId.HasValue)
                {
                    spawnedQuests.Add(new SpawnedQuest
                    {
                        QuestInstanceId = questId.Value,
                        QuestCode = hook.QuestCode
                    });
                }
            }

            // Update execution to completed
            execution.Status = ScenarioStatus.Completed;
            execution.CurrentPhase = phases.Count;
            execution.CompletedAt = DateTimeOffset.UtcNow;
            execution.MutationsAppliedJson = BannouJson.Serialize(appliedMutations);
            execution.QuestsSpawnedJson = BannouJson.Serialize(spawnedQuests);
            await _scenarioExecutionStore.SaveAsync(executionId.ToString(), execution, null, cancellationToken);

            // Remove from active set
            await _scenarioActiveStore.RemoveFromSetAsync(activeKey, activeEntry, cancellationToken);

            // Set cooldown
            var cooldownSeconds = definition.CooldownSeconds ?? _configuration.ScenarioCooldownDefaultSeconds;
            if (cooldownSeconds > 0)
            {
                await _scenarioCooldownStore.SaveAsync(
                    cooldownKey,
                    new CooldownMarker { ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(cooldownSeconds) },
                    new StateOptions { Ttl = cooldownSeconds },
                    cancellationToken);
            }

            stopwatch.Stop();

            // Publish completed event
            await _messageBus.TryPublishAsync("storyline.scenario.completed", new ScenarioCompletedEvent
            {
                ExecutionId = executionId,
                ScenarioId = body.ScenarioId,
                ScenarioCode = definition.Code,
                PrimaryCharacterId = body.CharacterId,
                AdditionalParticipantIds = body.AdditionalParticipants?.Values.ToList(),
                OrchestratorId = body.OrchestratorId,
                RealmId = definition.RealmId,
                GameServiceId = definition.GameServiceId,
                PhasesCompleted = phases.Count,
                TotalMutationsApplied = appliedMutations.Count(m => m.Success),
                TotalQuestsSpawned = spawnedQuests.Count,
                QuestIds = spawnedQuests.Select(q => q.QuestInstanceId).ToList(),
                DurationMs = (int)stopwatch.ElapsedMilliseconds,
                StartedAt = now,
                CompletedAt = execution.CompletedAt.Value
            }, cancellationToken: cancellationToken);

            _logger.LogInformation("Completed scenario {ScenarioId} execution {ExecutionId} in {DurationMs}ms",
                body.ScenarioId, executionId, stopwatch.ElapsedMilliseconds);

            return (StatusCodes.OK, new TriggerScenarioResponse
            {
                ExecutionId = executionId,
                ScenarioId = body.ScenarioId,
                Status = ScenarioStatus.Completed,
                TriggeredAt = now,
                MutationsApplied = appliedMutations,
                QuestsSpawned = spawnedQuests,
                FailureReason = null
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error triggering scenario");

            // Publish failed event
            await _messageBus.TryPublishAsync("storyline.scenario.failed", new ScenarioFailedEvent
            {
                ExecutionId = executionId,
                ScenarioId = body.ScenarioId,
                ScenarioCode = string.Empty,
                PrimaryCharacterId = body.CharacterId,
                OrchestratorId = body.OrchestratorId,
                FailureReason = ex.Message,
                FailedAt = DateTimeOffset.UtcNow
            }, cancellationToken: cancellationToken);

            await _messageBus.TryPublishErrorAsync(
                "storyline",
                "TriggerScenario",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                endpoint: "post:/storyline/scenario/trigger",
                details: null,
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Gets active scenario executions for a character.
    /// </summary>
    public async Task<(StatusCodes, GetActiveScenariosResponse?)> GetActiveScenariosAsync(
        GetActiveScenariosRequest body,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Getting active scenarios for character {CharacterId}", body.CharacterId);

        try
        {
            var activeKey = body.CharacterId.ToString();
            var activeMembers = await _scenarioActiveStore.GetSetAsync<ActiveScenarioEntry>(activeKey, cancellationToken);

            var executions = new List<ScenarioExecution>();
            foreach (var entry in activeMembers)
            {
                var execution = await _scenarioExecutionStore.GetAsync(entry.ExecutionId.ToString(), cancellationToken);
                if (execution is not null)
                {
                    executions.Add(new ScenarioExecution
                    {
                        ExecutionId = execution.ExecutionId,
                        ScenarioId = execution.ScenarioId,
                        Code = execution.ScenarioCode,
                        Name = execution.ScenarioName,
                        Status = execution.Status,
                        CurrentPhase = execution.CurrentPhase,
                        TotalPhases = execution.TotalPhases,
                        TriggeredAt = execution.TriggeredAt,
                        CompletedAt = execution.CompletedAt
                    });
                }
            }

            return (StatusCodes.OK, new GetActiveScenariosResponse { Executions = executions });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting active scenarios");
            await _messageBus.TryPublishErrorAsync(
                "storyline",
                "GetActiveScenarios",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                endpoint: "post:/storyline/scenario/get-active",
                details: null,
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Gets scenario execution history for a character.
    /// </summary>
    public async Task<(StatusCodes, GetScenarioHistoryResponse?)> GetScenarioHistoryAsync(
        GetScenarioHistoryRequest body,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Getting scenario history for character {CharacterId}", body.CharacterId);

        try
        {
            // Query executions for character using IQueryableStateStore
            var allExecutions = await _scenarioExecutionStore.QueryAsync(e => e.PrimaryCharacterId == body.CharacterId, cancellationToken);

            var characterExecutions = allExecutions
                .Where(e => e.PrimaryCharacterId == body.CharacterId)
                .OrderByDescending(e => e.TriggeredAt)
                .ToList();

            var totalCount = characterExecutions.Count;

            var paginated = characterExecutions
                .Skip(body.Offset)
                .Take(body.Limit)
                .Select(e => new ScenarioExecution
                {
                    ExecutionId = e.ExecutionId,
                    ScenarioId = e.ScenarioId,
                    Code = e.ScenarioCode,
                    Name = e.ScenarioName,
                    Status = e.Status,
                    CurrentPhase = e.CurrentPhase,
                    TotalPhases = e.TotalPhases,
                    TriggeredAt = e.TriggeredAt,
                    CompletedAt = e.CompletedAt
                })
                .ToList();

            return (StatusCodes.OK, new GetScenarioHistoryResponse
            {
                Executions = paginated,
                TotalCount = totalCount
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting scenario history");
            await _messageBus.TryPublishErrorAsync(
                "storyline",
                "GetScenarioHistory",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                endpoint: "post:/storyline/scenario/get-history",
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
        var key = scenarioId.ToString();

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
    /// Builds API response from storage model.
    /// </summary>
    private ScenarioDefinition BuildScenarioDefinitionResponse(ScenarioDefinitionModel model)
    {
        return new ScenarioDefinition
        {
            ScenarioId = model.ScenarioId,
            Code = model.Code,
            Name = model.Name,
            Description = model.Description,
            TriggerConditions = BannouJson.Deserialize<List<TriggerCondition>>(model.TriggerConditionsJson)
                ?? new List<TriggerCondition>(),
            Phases = BannouJson.Deserialize<List<ScenarioPhase>>(model.PhasesJson)
                ?? new List<ScenarioPhase>(),
            Mutations = string.IsNullOrEmpty(model.MutationsJson)
                ? null
                : BannouJson.Deserialize<List<ScenarioMutation>>(model.MutationsJson),
            QuestHooks = string.IsNullOrEmpty(model.QuestHooksJson)
                ? null
                : BannouJson.Deserialize<List<ScenarioQuestHook>>(model.QuestHooksJson),
            CooldownSeconds = model.CooldownSeconds,
            ExclusivityTags = string.IsNullOrEmpty(model.ExclusivityTagsJson)
                ? null
                : BannouJson.Deserialize<List<string>>(model.ExclusivityTagsJson),
            Priority = model.Priority,
            Enabled = model.Enabled,
            RealmId = model.RealmId,
            GameServiceId = model.GameServiceId,
            Tags = string.IsNullOrEmpty(model.TagsJson)
                ? null
                : BannouJson.Deserialize<List<string>>(model.TagsJson),
            Deprecated = model.Deprecated,
            CreatedAt = model.CreatedAt,
            UpdatedAt = model.UpdatedAt,
            Etag = model.Etag
        };
    }

    /// <summary>
    /// Builds summary for list response.
    /// </summary>
    private ScenarioDefinitionSummary BuildScenarioSummary(ScenarioDefinitionModel model)
    {
        var conditions = BannouJson.Deserialize<List<TriggerCondition>>(model.TriggerConditionsJson);
        var phases = BannouJson.Deserialize<List<ScenarioPhase>>(model.PhasesJson);
        var mutations = string.IsNullOrEmpty(model.MutationsJson)
            ? null
            : BannouJson.Deserialize<List<ScenarioMutation>>(model.MutationsJson);
        var questHooks = string.IsNullOrEmpty(model.QuestHooksJson)
            ? null
            : BannouJson.Deserialize<List<ScenarioQuestHook>>(model.QuestHooksJson);

        return new ScenarioDefinitionSummary
        {
            ScenarioId = model.ScenarioId,
            Code = model.Code,
            Name = model.Name,
            Priority = model.Priority,
            Enabled = model.Enabled,
            Deprecated = model.Deprecated,
            ConditionCount = conditions?.Count ?? 0,
            PhaseCount = phases?.Count ?? 0,
            MutationCount = mutations?.Count ?? 0,
            QuestHookCount = questHooks?.Count ?? 0,
            RealmId = model.RealmId,
            GameServiceId = model.GameServiceId,
            Tags = string.IsNullOrEmpty(model.TagsJson)
                ? null
                : BannouJson.Deserialize<List<string>>(model.TagsJson),
            CreatedAt = model.CreatedAt
        };
    }

    /// <summary>
    /// Evaluates all conditions and calculates fit score.
    /// </summary>
    private (int conditionsMet, double fitScore) EvaluateConditions(
        List<TriggerCondition> conditions,
        CharacterStateSnapshot characterState,
        Guid? locationId,
        int? timeOfDay,
        IDictionary<string, string>? worldState)
    {
        if (conditions.Count == 0)
        {
            return (0, _configuration.ScenarioFitScoreBaseWeight);
        }

        var conditionsMet = 0;
        var fitScore = _configuration.ScenarioFitScoreBaseWeight;

        foreach (var condition in conditions)
        {
            var (met, _, _, _) = EvaluateSingleCondition(condition, characterState, locationId, timeOfDay, worldState);
            if (met)
            {
                conditionsMet++;
                fitScore += GetConditionBonus(condition.ConditionType);
            }
        }

        // Normalize fit score to 0-1 range
        return (conditionsMet, Math.Min(1.0, fitScore));
    }

    /// <summary>
    /// Gets the configuration-driven bonus for a condition type.
    /// </summary>
    private double GetConditionBonus(TriggerConditionType conditionType)
    {
        return conditionType switch
        {
            TriggerConditionType.TraitRange => _configuration.ScenarioTraitMatchBonus,
            TriggerConditionType.BackstoryElement => _configuration.ScenarioBackstoryMatchBonus,
            TriggerConditionType.RelationshipExists => _configuration.ScenarioRelationshipMatchBonus,
            TriggerConditionType.RelationshipMissing => _configuration.ScenarioRelationshipMatchBonus,
            TriggerConditionType.AgeRange => _configuration.ScenarioTraitMatchBonus, // Reuse trait bonus for age
            TriggerConditionType.LocationAt => _configuration.ScenarioLocationMatchBonus,
            TriggerConditionType.TimeOfDay => _configuration.ScenarioWorldStateMatchBonus, // Reuse world state bonus for time
            TriggerConditionType.WorldState => _configuration.ScenarioWorldStateMatchBonus,
            TriggerConditionType.Custom => 0.0, // Custom conditions not evaluated server-side
            _ => 0.0
        };
    }

    /// <summary>
    /// Evaluates a single condition with detailed results.
    /// </summary>
    private (bool met, string? actualValue, string? expectedValue, string? details) EvaluateSingleCondition(
        TriggerCondition condition,
        CharacterStateSnapshot characterState,
        Guid? locationId,
        int? timeOfDay,
        IDictionary<string, string>? worldState)
    {
        switch (condition.ConditionType)
        {
            case TriggerConditionType.TraitRange:
                {
                    if (string.IsNullOrEmpty(condition.TraitAxis))
                        return (false, null, null, "Missing trait axis");

                    var trait = characterState.Traits?.FirstOrDefault(t =>
                        t.Axis.Equals(condition.TraitAxis, StringComparison.OrdinalIgnoreCase));

                    if (trait is null)
                        return (false, "not found", $"{condition.TraitMin}-{condition.TraitMax}", $"Trait {condition.TraitAxis} not in snapshot");

                    var inRange = (!condition.TraitMin.HasValue || trait.Value >= condition.TraitMin.Value) &&
                                (!condition.TraitMax.HasValue || trait.Value <= condition.TraitMax.Value);

                    return (inRange, trait.Value.ToString("F2"), $"{condition.TraitMin}-{condition.TraitMax}", null);
                }

            case TriggerConditionType.BackstoryElement:
                {
                    if (string.IsNullOrEmpty(condition.BackstoryType))
                        return (false, null, null, "Missing backstory type");

                    var hasElement = characterState.BackstoryElements?.Any(b =>
                        b.ElementType.Equals(condition.BackstoryType, StringComparison.OrdinalIgnoreCase) &&
                        (string.IsNullOrEmpty(condition.BackstoryKey) ||
                        b.Key.Equals(condition.BackstoryKey, StringComparison.OrdinalIgnoreCase))) ?? false;

                    return (hasElement, hasElement ? "present" : "absent", "present", null);
                }

            case TriggerConditionType.RelationshipExists:
                {
                    if (string.IsNullOrEmpty(condition.RelationshipTypeCode))
                        return (false, null, null, "Missing relationship type code");

                    var hasRelationship = characterState.Relationships?.Any(r =>
                        r.RelationshipTypeCode.Equals(condition.RelationshipTypeCode, StringComparison.OrdinalIgnoreCase) &&
                        (string.IsNullOrEmpty(condition.OtherEntityType) ||
                        r.OtherEntityType.Equals(condition.OtherEntityType, StringComparison.OrdinalIgnoreCase))) ?? false;

                    return (hasRelationship, hasRelationship ? "exists" : "missing", "exists", null);
                }

            case TriggerConditionType.RelationshipMissing:
                {
                    if (string.IsNullOrEmpty(condition.RelationshipTypeCode))
                        return (false, null, null, "Missing relationship type code");

                    var hasRelationship = characterState.Relationships?.Any(r =>
                        r.RelationshipTypeCode.Equals(condition.RelationshipTypeCode, StringComparison.OrdinalIgnoreCase)) ?? false;

                    return (!hasRelationship, hasRelationship ? "exists" : "missing", "missing", null);
                }

            case TriggerConditionType.AgeRange:
                {
                    if (!characterState.Age.HasValue)
                        return (false, "unknown", $"{condition.AgeMin}-{condition.AgeMax}", "Character age not in snapshot");

                    var inRange = (!condition.AgeMin.HasValue || characterState.Age.Value >= condition.AgeMin.Value) &&
                                (!condition.AgeMax.HasValue || characterState.Age.Value <= condition.AgeMax.Value);

                    return (inRange, characterState.Age.Value.ToString(), $"{condition.AgeMin}-{condition.AgeMax}", null);
                }

            case TriggerConditionType.LocationAt:
                {
                    if (!condition.LocationId.HasValue)
                        return (false, null, null, "Missing location ID in condition");

                    if (!locationId.HasValue)
                        return (false, "unknown", condition.LocationId.Value.ToString(), "Location not provided in request");

                    var matches = locationId.Value == condition.LocationId.Value;
                    return (matches, locationId.Value.ToString(), condition.LocationId.Value.ToString(), null);
                }

            case TriggerConditionType.TimeOfDay:
                {
                    if (!timeOfDay.HasValue)
                        return (false, "unknown", $"{condition.TimeOfDayMin}-{condition.TimeOfDayMax}", "Time of day not provided in request");

                    var inRange = (!condition.TimeOfDayMin.HasValue || timeOfDay.Value >= condition.TimeOfDayMin.Value) &&
                                (!condition.TimeOfDayMax.HasValue || timeOfDay.Value <= condition.TimeOfDayMax.Value);

                    return (inRange, timeOfDay.Value.ToString(), $"{condition.TimeOfDayMin}-{condition.TimeOfDayMax}", null);
                }

            case TriggerConditionType.WorldState:
                {
                    if (string.IsNullOrEmpty(condition.WorldStateKey))
                        return (false, null, null, "Missing world state key");

                    if (worldState is null || !worldState.TryGetValue(condition.WorldStateKey, out var actualValue))
                        return (false, "not set", condition.WorldStateValue, $"World state key {condition.WorldStateKey} not provided");

                    var matches = string.IsNullOrEmpty(condition.WorldStateValue) ||
                                actualValue.Equals(condition.WorldStateValue, StringComparison.OrdinalIgnoreCase);

                    return (matches, actualValue, condition.WorldStateValue, null);
                }

            case TriggerConditionType.Custom:
                // Custom conditions are not evaluated server-side
                return (true, "custom", "custom", "Custom conditions evaluated by caller");

            default:
                return (false, null, null, $"Unknown condition type: {condition.ConditionType}");
        }
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

                        if (string.IsNullOrEmpty(mutation.ExperienceType))
                        {
                            return (false, "Missing experience type for personality mutation");
                        }

                        // Parse experience type string to enum (schema design limitation - should use enum type)
                        if (!Enum.TryParse<CharacterPersonality.ExperienceType>(mutation.ExperienceType, ignoreCase: true, out var experienceType))
                        {
                            return (false, $"Unknown experience type: {mutation.ExperienceType}");
                        }

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

                        if (string.IsNullOrEmpty(mutation.BackstoryElementType) || string.IsNullOrEmpty(mutation.BackstoryKey))
                        {
                            return (false, "Missing backstory element type or key");
                        }

                        // Parse backstory element type string to enum (schema design limitation - should use enum type)
                        if (!Enum.TryParse<CharacterHistory.BackstoryElementType>(mutation.BackstoryElementType, ignoreCase: true, out var elementType))
                        {
                            return (false, $"Unknown backstory element type: {mutation.BackstoryElementType}");
                        }

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
                                        Value = mutation.BackstoryValue ?? string.Empty,
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

    /// <summary>
    /// Generates a human-readable description of a mutation.
    /// </summary>
    private static string DescribeMutation(ScenarioMutation mutation)
    {
        return mutation.MutationType switch
        {
            MutationType.PersonalityEvolve =>
                $"Apply {mutation.ExperienceType} experience (intensity: {mutation.ExperienceIntensity ?? 0.5f:F2})",
            MutationType.BackstoryAdd =>
                $"Add backstory: {mutation.BackstoryElementType}/{mutation.BackstoryKey}",
            MutationType.RelationshipCreate =>
                $"Create relationship: {mutation.RelationshipTypeCode} with {mutation.OtherParticipantRole}",
            MutationType.RelationshipEnd =>
                $"End relationship: {mutation.RelationshipTypeCode} with {mutation.OtherParticipantRole}",
            MutationType.Custom =>
                "Custom mutation (caller-handled)",
            _ =>
                $"Unknown mutation type: {mutation.MutationType}"
        };
    }

    /// <summary>
    /// Builds trigger response from execution model.
    /// </summary>
    private static TriggerScenarioResponse BuildTriggerResponse(ScenarioExecutionModel execution)
    {
        return new TriggerScenarioResponse
        {
            ExecutionId = execution.ExecutionId,
            ScenarioId = execution.ScenarioId,
            Status = execution.Status,
            TriggeredAt = execution.TriggeredAt,
            MutationsApplied = string.IsNullOrEmpty(execution.MutationsAppliedJson)
                ? null
                : BannouJson.Deserialize<List<AppliedMutation>>(execution.MutationsAppliedJson),
            QuestsSpawned = string.IsNullOrEmpty(execution.QuestsSpawnedJson)
                ? null
                : BannouJson.Deserialize<List<SpawnedQuest>>(execution.QuestsSpawnedJson),
            FailureReason = execution.FailureReason
        };
    }

    #endregion

    #region Compression

    /// <summary>
    /// Gets storyline data for character compression/archival.
    /// </summary>
    public async Task<(StatusCodes, StorylineArchive?)> GetCompressDataAsync(
        GetCompressDataRequest body,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Getting compress data for character {CharacterId}", body.CharacterId);

        try
        {
            // Query all scenario executions for this character
            var allExecutions = await _scenarioExecutionStore.QueryAsync(
                e => e.PrimaryCharacterId == body.CharacterId,
                cancellationToken);

            var characterExecutions = allExecutions
                .OrderByDescending(e => e.TriggeredAt)
                .ToList();

            // Get active scenarios from Redis set
            var activeKey = body.CharacterId.ToString();
            var activeMembers = await _scenarioActiveStore.GetSetAsync<ActiveScenarioEntry>(activeKey, cancellationToken);
            var activeScenarioCodes = activeMembers.Select(a => a.ScenarioCode).ToHashSet();

            // Build participation entries from executions
            var participations = new List<StorylineParticipation>();
            var completedCount = 0;

            foreach (var execution in characterExecutions)
            {
                var participation = new StorylineParticipation
                {
                    ExecutionId = execution.ExecutionId,
                    ScenarioId = execution.ScenarioId,
                    ScenarioCode = execution.ScenarioCode,
                    ScenarioName = execution.ScenarioName,
                    Role = "primary", // PrimaryCharacterId means primary role
                    Phase = execution.CurrentPhase,
                    TotalPhases = execution.TotalPhases,
                    Status = execution.Status,
                    StartedAt = execution.TriggeredAt,
                    CompletedAt = execution.CompletedAt,
                    Choices = null // Could parse from mutations if needed
                };
                participations.Add(participation);

                if (execution.Status == ScenarioStatus.Completed)
                {
                    completedCount++;
                }
            }

            // Derive active arcs from active scenario codes
            // Arc types are typically the prefix before underscore (e.g., "romance_first_meeting" -> "romance")
            var activeArcs = activeScenarioCodes
                .Select(code => code.Split('_').FirstOrDefault() ?? code)
                .Distinct()
                .ToList();

            var archive = new StorylineArchive
            {
                ResourceId = body.CharacterId,
                ResourceType = "storyline",
                ArchivedAt = DateTimeOffset.UtcNow,
                SchemaVersion = 1,
                CharacterId = body.CharacterId,
                Participations = participations,
                ActiveArcs = activeArcs,
                CompletedStorylines = completedCount
            };

            _logger.LogDebug(
                "Compress data for character {CharacterId}: {ParticipationCount} participations, {ActiveArcCount} active arcs, {CompletedCount} completed",
                body.CharacterId,
                participations.Count,
                activeArcs.Count,
                completedCount);

            return (StatusCodes.OK, archive);
        }
        catch (ApiException ex)
        {
            _logger.LogWarning(ex, "API exception getting compress data for character {CharacterId}", body.CharacterId);
            return ((StatusCodes)ex.StatusCode, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting compress data for character {CharacterId}", body.CharacterId);
            await _messageBus.TryPublishErrorAsync(
                "storyline",
                "GetCompressData",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    #endregion
}
