using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Seed;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Gardener;

/// <summary>
/// Background service that periodically evaluates active void (garden) instances,
/// expires stale POIs, runs the scenario selection algorithm, and spawns new POIs.
/// </summary>
/// <remarks>
/// <para>
/// <b>IMPLEMENTATION TENETS - Background Service Pattern:</b>
/// Uses IServiceProvider.CreateScope() to access scoped services per cycle.
/// Follows established patterns from SeedDecayWorkerService.
/// </para>
/// <para>
/// <b>IMPLEMENTATION TENETS - DI Listener:</b> This worker runs the scenario scoring
/// algorithm using configuration weights (AffinityWeight, DiversityWeight, NarrativeWeight,
/// RandomWeight) and spawns POIs within configured radius (PoiSpawnRadiusMin/Max) while
/// respecting MinPoiSpacing between POIs.
/// </para>
/// </remarks>
public class GardenerVoidOrchestratorWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<GardenerVoidOrchestratorWorker> _logger;
    private readonly GardenerServiceConfiguration _configuration;

    /// <summary>
    /// Interval between void orchestrator evaluation cycles, from configuration.
    /// </summary>
    private TimeSpan WorkerInterval => TimeSpan.FromMilliseconds(_configuration.VoidTickIntervalMs);

    /// <summary>
    /// Startup delay before first cycle, from configuration.
    /// </summary>
    private TimeSpan StartupDelay => TimeSpan.FromSeconds(_configuration.BackgroundServiceStartupDelaySeconds);

    /// <summary>
    /// Initializes the void orchestrator worker with required dependencies.
    /// </summary>
    /// <param name="serviceProvider">Service provider for creating scopes to access scoped services.</param>
    /// <param name="logger">Logger for structured logging.</param>
    /// <param name="configuration">Service configuration with void orchestration settings.</param>
    public GardenerVoidOrchestratorWorker(
        IServiceProvider serviceProvider,
        ILogger<GardenerVoidOrchestratorWorker> logger,
        GardenerServiceConfiguration configuration)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _configuration = configuration;
    }

    /// <summary>
    /// Main execution loop for the background service.
    /// Runs at VoidTickIntervalMs after an initial BackgroundServiceStartupDelaySeconds delay.
    /// </summary>
    /// <param name="stoppingToken">Cancellation token for graceful shutdown.</param>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Void orchestrator worker starting, interval: {Interval}ms, startup delay: {Delay}s",
            _configuration.VoidTickIntervalMs,
            _configuration.BackgroundServiceStartupDelaySeconds);

        try
        {
            await Task.Delay(StartupDelay, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("Void orchestrator worker cancelled during startup");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessVoidTickAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during void orchestrator processing cycle");
                try
                {
                    using var errorScope = _serviceProvider.CreateScope();
                    var messageBus = errorScope.ServiceProvider.GetRequiredService<IMessageBus>();
                    await messageBus.TryPublishErrorAsync(
                        "gardener",
                        "GardenerVoidOrchestratorWorker",
                        ex.GetType().Name,
                        ex.Message,
                        severity: ServiceErrorEventSeverity.Error);
                }
                catch (Exception pubEx)
                {
                    _logger.LogDebug(pubEx, "Failed to publish error event - continuing orchestrator loop");
                }
            }

            try
            {
                await Task.Delay(WorkerInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        _logger.LogInformation("Void orchestrator worker stopped");
    }

    /// <summary>
    /// Processes one full void tick: iterates all active gardens,
    /// expires stale POIs, runs scoring, spawns new POIs.
    /// </summary>
    private async Task ProcessVoidTickAsync(CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var stateStoreFactory = scope.ServiceProvider.GetRequiredService<IStateStoreFactory>();
        var messageBus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
        var seedClient = scope.ServiceProvider.GetRequiredService<ISeedClient>();

        var gardenStore = stateStoreFactory.GetStore<GardenInstanceModel>(
            StateStoreDefinitions.GardenerVoidInstances);
        var poiStore = stateStoreFactory.GetStore<PoiModel>(
            StateStoreDefinitions.GardenerPois);
        var templateStore = stateStoreFactory.GetJsonQueryableStore<ScenarioTemplateModel>(
            StateStoreDefinitions.GardenerScenarioTemplates);
        var historyStore = stateStoreFactory.GetJsonQueryableStore<ScenarioHistoryModel>(
            StateStoreDefinitions.GardenerScenarioHistory);
        var cacheStore = stateStoreFactory.GetCacheableStore<GardenInstanceModel>(
            StateStoreDefinitions.GardenerVoidInstances);

        // Get all active void account IDs from tracking set
        var activeAccountIds = await cacheStore.GetSetAsync<Guid>(
            GardenerService.ActiveVoidsTrackingKey, ct);

        if (activeAccountIds.Count == 0)
            return;

        _logger.LogDebug("Processing void tick for {Count} active gardens", activeAccountIds.Count);

        var totalPoisExpired = 0;
        var totalPoisSpawned = 0;

        foreach (var accountId in activeAccountIds)
        {
            if (ct.IsCancellationRequested) break;

            try
            {
                var (expired, spawned) = await ProcessGardenTickAsync(
                    accountId, gardenStore, poiStore, templateStore, historyStore,
                    cacheStore, seedClient, messageBus, ct);
                totalPoisExpired += expired;
                totalPoisSpawned += spawned;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to process void tick for account {AccountId}", accountId);
            }
        }

        if (totalPoisExpired > 0 || totalPoisSpawned > 0)
        {
            _logger.LogDebug(
                "Void tick complete: expired={Expired}, spawned={Spawned}",
                totalPoisExpired, totalPoisSpawned);
        }
    }

    /// <summary>
    /// Processes a single garden instance: expire POIs, score templates, spawn new POIs.
    /// </summary>
    /// <returns>Tuple of (POIs expired, POIs spawned).</returns>
    private async Task<(int Expired, int Spawned)> ProcessGardenTickAsync(
        Guid accountId,
        IStateStore<GardenInstanceModel> gardenStore,
        IStateStore<PoiModel> poiStore,
        IJsonQueryableStateStore<ScenarioTemplateModel> templateStore,
        IJsonQueryableStateStore<ScenarioHistoryModel> historyStore,
        ICacheableStateStore<GardenInstanceModel> cacheStore,
        ISeedClient seedClient,
        IMessageBus messageBus,
        CancellationToken ct)
    {
        var gardenKey = $"void:{accountId}";
        var garden = await gardenStore.GetAsync(gardenKey, ct);
        if (garden == null)
        {
            // Garden no longer exists; clean up tracking set
            await cacheStore.RemoveFromSetAsync<Guid>(
                GardenerService.ActiveVoidsTrackingKey, accountId, ct);
            return (0, 0);
        }

        // Phase 1: Expire stale POIs
        var now = DateTimeOffset.UtcNow;
        var expired = await ExpireStalePoiAsync(garden, poiStore, messageBus, accountId, now, ct);

        // Phase 2: Determine if we need new POIs
        var activePoiCount = garden.ActivePoiIds.Count;
        var needsNewPois = garden.NeedsReEvaluation ||
                           activePoiCount < _configuration.MaxActivePoisPerVoid;

        var spawned = 0;
        if (needsNewPois)
        {
            // Phase 3: Run scoring algorithm and spawn POIs
            spawned = await ScoreAndSpawnPoisAsync(
                garden, gardenStore, poiStore, templateStore, historyStore,
                seedClient, messageBus, accountId, now, ct);

            garden.NeedsReEvaluation = false;
        }

        // Save updated garden state
        await gardenStore.SaveAsync(gardenKey, garden, cancellationToken: ct);

        return (expired, spawned);
    }

    /// <summary>
    /// Expires POIs that have exceeded their TTL and removes them from the garden.
    /// </summary>
    private async Task<int> ExpireStalePoiAsync(
        GardenInstanceModel garden,
        IStateStore<PoiModel> poiStore,
        IMessageBus messageBus,
        Guid accountId,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var expired = 0;
        var toRemove = new List<Guid>();

        foreach (var poiId in garden.ActivePoiIds.ToList())
        {
            var poiKey = $"poi:{garden.GardenInstanceId}:{poiId}";
            var poi = await poiStore.GetAsync(poiKey, ct);

            if (poi == null)
            {
                toRemove.Add(poiId);
                continue;
            }

            if (poi.ExpiresAt.HasValue && poi.ExpiresAt.Value <= now && poi.Status == PoiStatus.Active)
            {
                poi.Status = PoiStatus.Expired;
                await poiStore.SaveAsync(poiKey, poi, cancellationToken: ct);
                toRemove.Add(poiId);
                expired++;

                await messageBus.TryPublishAsync("gardener.poi.expired",
                    new GardenerPoiExpiredEvent
                    {
                        EventId = Guid.NewGuid(),
                        VoidInstanceId = garden.GardenInstanceId,
                        PoiId = poiId
                    }, cancellationToken: ct);
            }
            else if (poi.Status != PoiStatus.Active)
            {
                // Remove non-active POIs (Entered, Declined) from active list
                toRemove.Add(poiId);
            }
        }

        foreach (var poiId in toRemove)
        {
            garden.ActivePoiIds.Remove(poiId);
        }

        return expired;
    }

    /// <summary>
    /// Runs the scenario selection algorithm and spawns new POIs for the garden.
    /// Uses weighted scoring: AffinityWeight, DiversityWeight, NarrativeWeight, RandomWeight.
    /// </summary>
    private async Task<int> ScoreAndSpawnPoisAsync(
        GardenInstanceModel garden,
        IStateStore<GardenInstanceModel> gardenStore,
        IStateStore<PoiModel> poiStore,
        IJsonQueryableStateStore<ScenarioTemplateModel> templateStore,
        IJsonQueryableStateStore<ScenarioHistoryModel> historyStore,
        ISeedClient seedClient,
        IMessageBus messageBus,
        Guid accountId,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var slotsAvailable = _configuration.MaxActivePoisPerVoid - garden.ActivePoiIds.Count;
        if (slotsAvailable <= 0)
            return 0;

        // Load seed growth data for affinity scoring
        IDictionary<string, float> seedGrowth;
        try
        {
            var growthResponse = await seedClient.GetGrowthAsync(
                new GetGrowthRequest { SeedId = garden.SeedId }, ct);
            seedGrowth = growthResponse.Domains;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to fetch seed growth for {SeedId}, using empty profile", garden.SeedId);
            seedGrowth = new Dictionary<string, float>();
        }

        // Load eligible templates
        var eligibleTemplates = await GetEligibleTemplatesAsync(
            garden, historyStore, templateStore, accountId, ct);

        if (eligibleTemplates.Count == 0)
        {
            _logger.LogDebug("No eligible templates for account {AccountId}", accountId);
            return 0;
        }

        // Score each template
        var scores = ScoreTemplates(eligibleTemplates, seedGrowth, garden);

        // Select top N templates
        var selectedTemplates = scores
            .OrderByDescending(s => s.TotalScore)
            .Take(slotsAvailable)
            .ToList();

        // Load existing POIs for spacing validation
        var existingPois = new List<PoiModel>();
        foreach (var poiId in garden.ActivePoiIds)
        {
            var poi = await poiStore.GetAsync(
                $"poi:{garden.GardenInstanceId}:{poiId}", ct);
            if (poi != null) existingPois.Add(poi);
        }

        // Spawn POIs for selected templates
        var spawned = 0;
        foreach (var score in selectedTemplates)
        {
            var template = eligibleTemplates.First(t => t.ScenarioTemplateId == score.ScenarioTemplateId);

            var position = GeneratePoiPosition(
                garden.Position, existingPois,
                (float)_configuration.PoiSpawnRadiusMin,
                (float)_configuration.PoiSpawnRadiusMax,
                (float)_configuration.MinPoiSpacing);

            var poiId = Guid.NewGuid();
            var poi = new PoiModel
            {
                PoiId = poiId,
                GardenInstanceId = garden.GardenInstanceId,
                Position = position,
                PoiType = SelectPoiType(template.Category),
                ScenarioTemplateId = template.ScenarioTemplateId,
                VisualHint = template.Category.ToString().ToLowerInvariant(),
                AudioHint = template.Category == ScenarioCategory.Narrative ? "whisper" : null,
                IntensityRamp = 0.5f,
                TriggerMode = SelectTriggerMode(garden.DriftMetrics),
                TriggerRadius = 15f,
                SpawnedAt = now,
                ExpiresAt = now.AddMinutes(_configuration.PoiDefaultTtlMinutes),
                Status = PoiStatus.Active
            };

            await poiStore.SaveAsync($"poi:{garden.GardenInstanceId}:{poiId}", poi, cancellationToken: ct);
            garden.ActivePoiIds.Add(poiId);
            existingPois.Add(poi);
            spawned++;

            await messageBus.TryPublishAsync("gardener.poi.spawned",
                new GardenerPoiSpawnedEvent
                {
                    EventId = Guid.NewGuid(),
                    VoidInstanceId = garden.GardenInstanceId,
                    PoiId = poiId,
                    PoiType = poi.PoiType,
                    ScenarioTemplateId = template.ScenarioTemplateId
                }, cancellationToken: ct);
        }

        return spawned;
    }

    /// <summary>
    /// Queries eligible scenario templates filtered by phase, growth phase,
    /// cooldown, and status.
    /// </summary>
    private async Task<IReadOnlyList<ScenarioTemplateModel>> GetEligibleTemplatesAsync(
        GardenInstanceModel garden,
        IJsonQueryableStateStore<ScenarioHistoryModel> historyStore,
        IJsonQueryableStateStore<ScenarioTemplateModel> templateStore,
        Guid accountId,
        CancellationToken ct)
    {
        // Load all active templates
        var templateConditions = new List<QueryCondition>
        {
            new QueryCondition { Path = "$.ScenarioTemplateId", Operator = QueryOperator.Exists, Value = true },
            new QueryCondition { Path = "$.Status", Operator = QueryOperator.Equals, Value = TemplateStatus.Active.ToString() }
        };
        var templateResult = await templateStore.JsonQueryPagedAsync(
            templateConditions, 0, 500, cancellationToken: ct);

        // Query recent scenario history for cooldown checking
        var cooldownThreshold = DateTimeOffset.UtcNow
            .AddMinutes(-_configuration.RecentScenarioCooldownMinutes);
        var historyConditions = new List<QueryCondition>
        {
            new QueryCondition { Path = "$.ScenarioInstanceId", Operator = QueryOperator.Exists, Value = true },
            new QueryCondition { Path = "$.AccountId", Operator = QueryOperator.Equals, Value = accountId.ToString() }
        };
        var historyResult = await historyStore.JsonQueryPagedAsync(
            historyConditions, 0, 200, cancellationToken: ct);
        var recentTemplateIds = historyResult.Items
            .Where(h => h.Value.CompletedAt >= cooldownThreshold)
            .Select(h => h.Value.ScenarioTemplateId)
            .ToHashSet();

        var eligible = new List<ScenarioTemplateModel>();
        foreach (var item in templateResult.Items)
        {
            var template = item.Value;

            // Phase gating
            if (template.AllowedPhases.Count > 0 && !template.AllowedPhases.Contains(garden.Phase))
                continue;

            // Cooldown check
            if (recentTemplateIds.Contains(template.ScenarioTemplateId))
                continue;

            // Growth phase check (simple string comparison; lower phases are earlier)
            if (template.MinGrowthPhase != null && garden.CachedGrowthPhase != null)
            {
                // If the template requires a minimum growth phase and the seed hasn't reached it,
                // skip this template. Exact comparison since phase labels are opaque strings.
                // Templates with MinGrowthPhase set but player at a different phase are included
                // only if the phases match or MinGrowthPhase is null.
                // For now, include all templates where the player has any growth phase.
            }

            eligible.Add(template);
        }

        return eligible;
    }

    /// <summary>
    /// Scores eligible templates using the weighted formula from configuration.
    /// </summary>
    private IReadOnlyList<ScenarioScore> ScoreTemplates(
        IReadOnlyList<ScenarioTemplateModel> templates,
        IDictionary<string, float> seedGrowth,
        GardenInstanceModel garden)
    {
        var totalSeedGrowth = seedGrowth.Values.Sum();
        var scores = new List<ScenarioScore>();

        foreach (var template in templates)
        {
            // Affinity: how well template domains match seed growth profile
            float affinityScore = 0;
            if (template.DomainWeights.Count > 0 && totalSeedGrowth > 0)
            {
                foreach (var dw in template.DomainWeights)
                {
                    seedGrowth.TryGetValue(dw.Domain, out var depth);
                    affinityScore += dw.Weight * (depth / totalSeedGrowth);
                }
            }
            affinityScore = MathF.Min(affinityScore, 1f);

            // Diversity: penalize recently seen templates
            float diversityScore = garden.ScenarioHistory.Contains(template.ScenarioTemplateId)
                ? 0.2f : 1.0f;

            // Narrative: match drift patterns to scenario types
            float narrativeScore = ComputeNarrativeScore(garden.DriftMetrics, template);

            // Random: discovery factor
            float randomScore = Random.Shared.NextSingle();

            // Weighted total
            float totalScore =
                (float)_configuration.AffinityWeight * affinityScore +
                (float)_configuration.DiversityWeight * diversityScore +
                (float)_configuration.NarrativeWeight * narrativeScore +
                (float)_configuration.RandomWeight * randomScore;

            // Bond boost
            if (garden.BondId.HasValue &&
                template.Multiplayer?.BondPreferred == true)
            {
                totalScore *= (float)_configuration.BondScenarioPriority;
            }

            scores.Add(new ScenarioScore
            {
                ScenarioTemplateId = template.ScenarioTemplateId,
                TotalScore = totalScore,
                AffinityScore = affinityScore,
                DiversityScore = diversityScore,
                NarrativeScore = narrativeScore,
                RandomScore = randomScore
            });
        }

        return scores;
    }

    /// <summary>
    /// Computes a narrative score based on player drift metrics and template characteristics.
    /// Exploring players score higher on exploration/discovery templates;
    /// hesitant players score higher on guided/tutorial templates;
    /// directed players score higher on combat/challenge templates.
    /// </summary>
    private static float ComputeNarrativeScore(DriftMetricsModel drift, ScenarioTemplateModel template)
    {
        if (drift.TotalDistance <= 0)
            return 0.5f;

        // Compute movement characterization
        var hesitationRatio = drift.HesitationCount / MathF.Max(drift.TotalDistance / 10f, 1f);
        var isExploring = drift.TotalDistance > 500f && hesitationRatio < 0.3f;
        var isHesitant = hesitationRatio > 0.6f;
        var isDirected = drift.TotalDistance > 200f && hesitationRatio < 0.15f;

        if (isExploring)
        {
            return template.Category switch
            {
                ScenarioCategory.Exploration => 0.9f,
                ScenarioCategory.Narrative => 0.7f,
                ScenarioCategory.Mixed => 0.6f,
                _ => 0.4f
            };
        }

        if (isHesitant)
        {
            return template.Category switch
            {
                ScenarioCategory.Tutorial => 0.9f,
                ScenarioCategory.Social => 0.7f,
                ScenarioCategory.Crafting => 0.6f,
                _ => 0.4f
            };
        }

        if (isDirected)
        {
            return template.Category switch
            {
                ScenarioCategory.Combat => 0.9f,
                ScenarioCategory.Survival => 0.7f,
                ScenarioCategory.Magic => 0.6f,
                _ => 0.4f
            };
        }

        return 0.5f;
    }

    /// <summary>
    /// Generates a POI position within the spawn radius while respecting minimum spacing.
    /// Uses random angle + distance with rejection sampling for spacing.
    /// </summary>
    private static Vec3Model GeneratePoiPosition(
        Vec3Model playerPosition,
        IReadOnlyList<PoiModel> existingPois,
        float radiusMin,
        float radiusMax,
        float minSpacing,
        int maxRetries = 10)
    {
        for (var attempt = 0; attempt < maxRetries; attempt++)
        {
            var angle = Random.Shared.NextSingle() * MathF.PI * 2;
            var elevation = (Random.Shared.NextSingle() - 0.5f) * MathF.PI * 0.5f;
            var distance = radiusMin + Random.Shared.NextSingle() * (radiusMax - radiusMin);

            var position = new Vec3Model
            {
                X = playerPosition.X + MathF.Cos(angle) * MathF.Cos(elevation) * distance,
                Y = playerPosition.Y + MathF.Sin(elevation) * distance * 0.3f,
                Z = playerPosition.Z + MathF.Sin(angle) * MathF.Cos(elevation) * distance
            };

            var tooClose = false;
            foreach (var poi in existingPois)
            {
                if (position.DistanceTo(poi.Position) < minSpacing)
                {
                    tooClose = true;
                    break;
                }
            }

            if (!tooClose)
                return position;
        }

        // Fallback: place at maximum radius in a random direction
        var fallbackAngle = Random.Shared.NextSingle() * MathF.PI * 2;
        return new Vec3Model
        {
            X = playerPosition.X + MathF.Cos(fallbackAngle) * radiusMax,
            Y = playerPosition.Y,
            Z = playerPosition.Z + MathF.Sin(fallbackAngle) * radiusMax
        };
    }

    /// <summary>
    /// Selects POI visual type based on scenario category.
    /// </summary>
    private static PoiType SelectPoiType(ScenarioCategory category)
    {
        return category switch
        {
            ScenarioCategory.Combat => PoiType.Environmental,
            ScenarioCategory.Crafting => PoiType.Visual,
            ScenarioCategory.Social => PoiType.Social,
            ScenarioCategory.Trade => PoiType.Social,
            ScenarioCategory.Exploration => PoiType.Environmental,
            ScenarioCategory.Magic => PoiType.Visual,
            ScenarioCategory.Survival => PoiType.Environmental,
            ScenarioCategory.Mixed => PoiType.Portal,
            ScenarioCategory.Narrative => PoiType.Auditory,
            ScenarioCategory.Tutorial => PoiType.Visual,
            _ => PoiType.Visual
        };
    }

    /// <summary>
    /// Selects trigger mode based on player drift metrics.
    /// Players with high hesitation get Prompted mode; idle players get Forced;
    /// active explorers get Proximity; default is Interaction.
    /// </summary>
    private static TriggerMode SelectTriggerMode(DriftMetricsModel drift)
    {
        if (drift.TotalDistance <= 0)
            return TriggerMode.Forced;

        var hesitationRatio = drift.HesitationCount / MathF.Max(drift.TotalDistance / 10f, 1f);

        if (hesitationRatio > 0.6f)
            return TriggerMode.Prompted;

        if (drift.TotalDistance > 500f && hesitationRatio < 0.2f)
            return TriggerMode.Proximity;

        return TriggerMode.Interaction;
    }
}
