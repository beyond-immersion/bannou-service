// =============================================================================
// Query Options Handler
// ABML action handler for querying actor options (Event Brain support).
// =============================================================================

using BeyondImmersion.Bannou.BehaviorCompiler.Documents.Actions;
using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService.Abml.Execution;
using BeyondImmersion.BannouService.Actor;
using Microsoft.Extensions.Logging;
using AbmlExecutionContext = BeyondImmersion.BannouService.Abml.Execution.ExecutionContext;

namespace BeyondImmersion.BannouService.Actor.Handlers;

/// <summary>
/// ABML action handler for querying actor options.
/// Enables Event Brain actors to query other actors for their available options.
/// </summary>
/// <remarks>
/// <para>
/// ABML usage:
/// <code>
/// - query_options:
///     actor_id: "${participant.actorId}"
///     query_type: "combat"
///     freshness: "cached"
///     max_age_ms: 3000
///     context:
///       combat_state: "engaged"
///       opponent_ids: "${opponents}"
///     result_variable: "participant_options"
/// </code>
/// </para>
/// </remarks>
public sealed class QueryOptionsHandler : IActionHandler
{
    private const string ACTION_NAME = "query_options";
    private readonly IActorClient _actorClient;
    private readonly ILogger<QueryOptionsHandler> _logger;
    private readonly ActorServiceConfiguration _config;

    /// <summary>
    /// Creates a new query options handler.
    /// </summary>
    /// <param name="actorClient">Actor client for service calls.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="config">Actor service configuration.</param>
    public QueryOptionsHandler(
        IActorClient actorClient,
        ILogger<QueryOptionsHandler> logger,
        ActorServiceConfiguration config)
    {
        _actorClient = actorClient;
        _logger = logger;
        _config = config;
    }

    /// <inheritdoc/>
    public bool CanHandle(ActionNode action)
        => action is DomainAction da && da.Name == ACTION_NAME;

    /// <inheritdoc/>
    public async ValueTask<ActionResult> ExecuteAsync(
        ActionNode action,
        AbmlExecutionContext context,
        CancellationToken ct)
    {
        var domainAction = (DomainAction)action;
        var scope = context.CallStack.Current?.Scope ?? context.RootScope;

        // Evaluate parameters
        var evaluatedParams = ValueEvaluator.EvaluateParameters(
            domainAction.Parameters, scope, context.Evaluator);

        // Get required parameters
        var actorId = evaluatedParams.GetValueOrDefault("actor_id")?.ToString();
        if (string.IsNullOrEmpty(actorId))
        {
            throw new InvalidOperationException("query_options requires actor_id parameter");
        }

        var queryTypeStr = evaluatedParams.GetValueOrDefault("query_type")?.ToString() ?? "combat";
        if (!Enum.TryParse<OptionsQueryType>(queryTypeStr, ignoreCase: true, out var queryType))
        {
            queryType = OptionsQueryType.Custom;
        }

        // Get optional parameters
        var freshnessStr = evaluatedParams.GetValueOrDefault("freshness")?.ToString() ?? "cached";
        if (!Enum.TryParse<OptionsFreshness>(freshnessStr, ignoreCase: true, out var freshness))
        {
            freshness = OptionsFreshness.Cached;
        }

        var maxAgeMs = _config.QueryOptionsDefaultMaxAgeMs;
        if (evaluatedParams.TryGetValue("max_age_ms", out var maxAgeObj) && maxAgeObj != null)
        {
            maxAgeMs = Convert.ToInt32(maxAgeObj);
        }

        var resultVariable = evaluatedParams.GetValueOrDefault("result_variable")?.ToString() ?? "query_result";

        // Build context if provided
        OptionsQueryContext? queryContext = null;
        if (evaluatedParams.TryGetValue("context", out var contextObj) && contextObj is IReadOnlyDictionary<string, object?> contextDict)
        {
            queryContext = BuildQueryContext(contextDict);
        }

        // Build request
        var request = new QueryOptionsRequest
        {
            ActorId = actorId,
            QueryType = queryType,
            Freshness = freshness,
            MaxAgeMs = maxAgeMs,
            Context = queryContext
        };

        try
        {
            _logger.LogDebug("Querying options for actor {ActorId}, type {QueryType}, freshness {Freshness}",
                actorId, queryType, freshness);

            var response = await _actorClient.QueryOptionsAsync(request, ct);

            // Store result in scope
            scope.SetValue(resultVariable, response);

            _logger.LogDebug("Retrieved {OptionCount} options for actor {ActorId}",
                response.Options.Count, actorId);

            return ActionResult.Continue;
        }
        catch (ApiException ex) when (ex.StatusCode == 404)
        {
            _logger.LogWarning("Actor {ActorId} not found when querying options", actorId);
            scope.SetValue(resultVariable, null);
            return ActionResult.Continue;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to query options for actor {ActorId}", actorId);
            throw;
        }
    }

    private static OptionsQueryContext BuildQueryContext(IReadOnlyDictionary<string, object?> dict)
    {
        var context = new OptionsQueryContext();

        if (dict.TryGetValue("combat_state", out var combatState))
        {
            context.CombatState = combatState?.ToString();
        }

        if (dict.TryGetValue("opponent_ids", out var opponentIds) && opponentIds is IEnumerable<object> opponents)
        {
            context.OpponentIds = opponents.Select(o => o?.ToString() ?? "").Where(s => !string.IsNullOrEmpty(s)).ToList();
        }

        if (dict.TryGetValue("ally_ids", out var allyIds) && allyIds is IEnumerable<object> allies)
        {
            context.AllyIds = allies.Select(a => a?.ToString() ?? "").Where(s => !string.IsNullOrEmpty(s)).ToList();
        }

        if (dict.TryGetValue("environment_tags", out var envTags) && envTags is IEnumerable<object> tags)
        {
            context.EnvironmentTags = tags.Select(t => t?.ToString() ?? "").Where(s => !string.IsNullOrEmpty(s)).ToList();
        }

        if (dict.TryGetValue("urgency", out var urgency) && urgency != null)
        {
            context.Urgency = (float)Convert.ToDouble(urgency);
        }

        return context;
    }
}
