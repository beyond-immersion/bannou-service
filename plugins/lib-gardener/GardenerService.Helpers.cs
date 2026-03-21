using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService.GameSession;
using BeyondImmersion.BannouService.Seed;
using BeyondImmersion.BannouService.State;

namespace BeyondImmersion.BannouService.Gardener;

// =============================================================================
// GardenerService — Private & Internal Helper Methods
// =============================================================================
//
// This partial class file is the designated home for all private and internal
// helper methods used by GardenerService. This separation exists to
// support the IMPLEMENTATION TENETS telemetry span rules:
//
//   - PRIMARY FILE (GardenerService.cs):
//     Contains ONLY public interface method implementations (the methods
//     declared in IGardenerService). These methods MUST NOT call
//     ITelemetryProvider.StartActivity because the generated controller
//     already wraps each endpoint with a telemetry span. Adding a span
//     in the service method would double-instrument the endpoint.
//
//   - THIS FILE (GardenerService.Helpers.cs):
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
/// Private and internal helper methods for GardenerService.
/// </summary>
/// <remarks>
/// <para>
/// This partial class contains all non-public helper methods. Every async method
/// in this file MUST include a <c>using var activity = _telemetryProvider.StartActivity(...)</c>
/// span per IMPLEMENTATION TENETS (T30). The generated controller instruments the
/// public interface methods; this file instruments the sub-operations.
/// </para>
/// </remarks>
public partial class GardenerService
{
    /// <summary>
    /// Determines whether a player meets a scenario template's prerequisite requirements.
    /// Returns true if all prerequisites are satisfied or if no prerequisites are defined.
    /// </summary>
    /// <param name="prerequisites">The template's prerequisite requirements (nullable — null means no prerequisites).</param>
    /// <param name="seedGrowth">The player's per-domain growth totals from Seed service.</param>
    /// <param name="completedTemplateCodes">Set of template codes the player has completed.</param>
    /// <returns>True if the player meets all prerequisites or none are defined.</returns>
    internal static bool MeetsPrerequisites(
        ScenarioPrerequisitesModel? prerequisites,
        IDictionary<string, float> seedGrowth,
        IReadOnlySet<string> completedTemplateCodes)
    {
        if (prerequisites == null)
            return true;

        // Check required domain growth minimums
        if (prerequisites.RequiredDomains != null)
        {
            foreach (var (domain, requiredAmount) in prerequisites.RequiredDomains)
            {
                if (!seedGrowth.TryGetValue(domain, out var currentAmount) || currentAmount < requiredAmount)
                    return false;
            }
        }

        // Check required completed scenarios
        if (prerequisites.RequiredScenarios != null)
        {
            foreach (var requiredCode in prerequisites.RequiredScenarios)
            {
                if (!completedTemplateCodes.Contains(requiredCode))
                    return false;
            }
        }

        // Check excluded scenarios (player must NOT have completed these)
        if (prerequisites.ExcludedScenarios != null)
        {
            foreach (var excludedCode in prerequisites.ExcludedScenarios)
            {
                if (completedTemplateCodes.Contains(excludedCode))
                    return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Queries the set of scenario template codes that the player has completed.
    /// Used for prerequisite validation (required/excluded scenarios).
    /// </summary>
    /// <param name="accountId">The account ID to query history for.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Set of completed template codes.</returns>
    private async Task<HashSet<string>> GetCompletedScenarioCodesAsync(
        Guid accountId, CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.gardener", "GardenerService.GetCompletedScenarioCodesAsync");

        var conditions = new List<QueryCondition>
        {
            new QueryCondition
            {
                Path = "$.AccountId",
                Operator = QueryOperator.Equals,
                Value = accountId.ToString()
            },
            new QueryCondition
            {
                Path = "$.Status",
                Operator = QueryOperator.Equals,
                Value = ScenarioStatus.Completed.ToString()
            }
        };

        var result = await _historyStore.JsonQueryPagedAsync(
            conditions, 0, 10000, cancellationToken: ct);

        return result.Items
            .Where(h => h.Value.TemplateCode != null)
            .Select(h => h.Value.TemplateCode!)
            .ToHashSet();
    }

    /// <summary>
    /// Loads all active POIs for a garden instance from Redis.
    /// </summary>
    private async Task<IReadOnlyList<PoiModel>> LoadActivePoisAsync(
        GardenInstanceModel garden, CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.gardener", "GardenerService.LoadActivePoisAsync");
        var pois = new List<PoiModel>();
        foreach (var poiId in garden.ActivePoiIds)
        {
            var poi = await _poiStore.GetAsync(PoiKey(garden.GardenInstanceId, poiId), ct);
            if (poi != null)
                pois.Add(poi);
        }
        return pois;
    }

    /// <summary>
    /// Loads or creates the deployment phase configuration singleton.
    /// </summary>
    internal async Task<DeploymentPhaseConfigModel> GetOrCreatePhaseConfigAsync(
        CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.gardener", "GardenerService.GetOrCreatePhaseConfigAsync");
        var config = await _phaseStore.GetAsync(PhaseConfigKey, ct);
        if (config != null)
            return config;

        config = new DeploymentPhaseConfigModel
        {
            CurrentPhase = _configuration.DefaultPhase,
            MaxConcurrentScenariosGlobal = _configuration.MaxConcurrentScenariosGlobal,
            PersistentEntryEnabled = false,
            GardenMinigamesEnabled = false,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        await _phaseStore.SaveAsync(PhaseConfigKey, config, cancellationToken: ct);
        _logger.LogInformation(
            "Created default phase config with phase {Phase}", config.CurrentPhase);
        return config;
    }

    /// <summary>
    /// Calculates growth awards and records them via ISeedClient (per FOUNDATION TENETS cross-service communication).
    /// </summary>
    private async Task<Dictionary<string, float>> CalculateAndAwardGrowthAsync(
        ScenarioInstanceModel scenario,
        ScenarioTemplateModel? template,
        bool fullCompletion,
        CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.gardener", "GardenerService.CalculateAndAwardGrowthAsync");
        var growthAwarded = GardenerGrowthCalculation.CalculateGrowth(
            scenario, template, _configuration.GrowthAwardMultiplier, fullCompletion,
            (float)_configuration.GrowthFullCompletionMaxRatio,
            (float)_configuration.GrowthFullCompletionMinRatio,
            (float)_configuration.GrowthPartialMaxRatio,
            _configuration.DefaultEstimatedDurationMinutes);

        if (growthAwarded.Count == 0)
            return growthAwarded;

        var entries = growthAwarded.Select(kvp =>
            new GrowthEntry { Domain = kvp.Key, Amount = kvp.Value }).ToList();

        // Award growth for primary participant via batch API (per FOUNDATION TENETS)
        var primaryParticipant = scenario.Participants.FirstOrDefault();
        if (primaryParticipant != null && entries.Count > 0)
        {
            try
            {
                await _seedClient.RecordGrowthBatchAsync(new RecordGrowthBatchRequest
                {
                    SeedId = primaryParticipant.SeedId,
                    Entries = entries,
                    Source = "gardener"
                }, ct);
            }
            catch (ApiException ex)
            {
                _logger.LogError(ex,
                    "Failed to record growth for seed {SeedId}, scenario {ScenarioId}",
                    primaryParticipant.SeedId, scenario.ScenarioInstanceId);
            }

            // Award growth for additional participants
            foreach (var participant in scenario.Participants.Skip(1))
            {
                try
                {
                    await _seedClient.RecordGrowthBatchAsync(new RecordGrowthBatchRequest
                    {
                        SeedId = participant.SeedId,
                        Entries = entries,
                        Source = "gardener"
                    }, ct);
                }
                catch (ApiException ex)
                {
                    _logger.LogError(ex,
                        "Failed to record growth for participant seed {SeedId}",
                        participant.SeedId);
                }
            }
        }

        return growthAwarded;
    }

    /// <summary>
    /// Writes a completed/abandoned scenario to the durable MySQL history store.
    /// </summary>
    private async Task WriteScenarioHistoryAsync(
        ScenarioInstanceModel scenario,
        ScenarioTemplateModel? template,
        CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.gardener", "GardenerService.WriteScenarioHistoryAsync");
        if (scenario.Participants.Count == 0) return;

        var durationSeconds = scenario.CompletedAt.HasValue
            ? (float)(scenario.CompletedAt.Value - scenario.CreatedAt).TotalSeconds
            : (float)(DateTimeOffset.UtcNow - scenario.CreatedAt).TotalSeconds;

        var completedAt = scenario.CompletedAt ?? DateTimeOffset.UtcNow;

        foreach (var participant in scenario.Participants)
        {
            try
            {
                var history = new ScenarioHistoryModel
                {
                    ScenarioInstanceId = scenario.ScenarioInstanceId,
                    ScenarioTemplateId = scenario.ScenarioTemplateId,
                    AccountId = participant.AccountId,
                    SeedId = participant.SeedId,
                    CompletedAt = completedAt,
                    Status = scenario.Status,
                    GrowthAwarded = scenario.GrowthAwarded,
                    DurationSeconds = durationSeconds,
                    TemplateCode = template?.Code
                };

                await _historyStore.SaveAsync(
                    HistoryKey(scenario.ScenarioInstanceId, participant.AccountId),
                    history, cancellationToken: ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to write scenario history for participant {AccountId} in scenario {ScenarioId}",
                    participant.AccountId, scenario.ScenarioInstanceId);
            }
        }
    }

    /// <summary>
    /// Attempts to clean up the game session by having participants leave.
    /// </summary>
    private async Task TryCleanupGameSessionAsync(
        ScenarioInstanceModel scenario, CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.gardener", "GardenerService.TryCleanupGameSessionAsync");
        foreach (var participant in scenario.Participants)
        {
            try
            {
                await _gameSessionClient.LeaveGameSessionByIdAsync(
                    new LeaveGameSessionByIdRequest
                    {
                        GameSessionId = scenario.GameSessionId,
                        AccountId = participant.AccountId,
                        WebSocketSessionId = null
                    }, ct);
            }
            catch (ApiException ex)
            {
                _logger.LogWarning(ex,
                    "Failed to remove participant {AccountId} from game session {SessionId}",
                    participant.AccountId, scenario.GameSessionId);
            }
        }
    }
}
