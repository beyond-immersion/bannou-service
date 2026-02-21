// =============================================================================
// Actor Query Handler
// ABML action handler for querying actor state (Event Brain support).
// =============================================================================

using BeyondImmersion.Bannou.BehaviorCompiler.Documents.Actions;
using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService.Abml.Execution;
using BeyondImmersion.BannouService.Actor;
using Microsoft.Extensions.Logging;
using AbmlExecutionContext = BeyondImmersion.BannouService.Abml.Execution.ExecutionContext;

namespace BeyondImmersion.BannouService.Actor.Handlers;

/// <summary>
/// ABML action handler for querying actor state.
/// Enables Event Brain actors to query Character Brain actors for their current state.
/// </summary>
/// <remarks>
/// <para>
/// ABML usage:
/// <code>
/// - actor_query:
///     target: ${defender.actor_id}    # Required - expression evaluating to actor ID
///     query: combat_readiness         # Required - query type
///     into: defender_status           # Required - variable to store result
///     timeout: 1000                   # Optional - default 1000ms
/// </code>
/// </para>
/// <para>
/// Queries the target actor for options of the specified type and stores the result
/// in the specified variable. The result contains a list of <see cref="ActorOption"/>
/// objects with action preferences, availability, and risk assessments.
/// </para>
/// </remarks>
public sealed class ActorQueryHandler : IActionHandler
{
    private const string ActionName = "actor_query";
    private const int DefaultTimeoutMs = 1000;

    private readonly IActorClient _actorClient;
    private readonly ILogger<ActorQueryHandler> _logger;

    /// <summary>
    /// Creates a new actor query handler.
    /// </summary>
    /// <param name="actorClient">Actor client for service calls.</param>
    /// <param name="logger">Logger instance.</param>
    public ActorQueryHandler(
        IActorClient actorClient,
        ILogger<ActorQueryHandler> logger)
    {
        _actorClient = actorClient;
        _logger = logger;
    }

    /// <inheritdoc/>
    public bool CanHandle(ActionNode action)
        => action is DomainAction da && da.Name.Equals(ActionName, StringComparison.OrdinalIgnoreCase);

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

        // Extract required parameters
        var targetActorId = evaluatedParams.GetValueOrDefault("target")?.ToString();
        if (string.IsNullOrEmpty(targetActorId))
        {
            throw new InvalidOperationException("actor_query requires 'target' parameter");
        }

        var queryStr = evaluatedParams.GetValueOrDefault("query")?.ToString();
        if (string.IsNullOrEmpty(queryStr))
        {
            throw new InvalidOperationException("actor_query requires 'query' parameter");
        }

        var intoVariable = evaluatedParams.GetValueOrDefault("into")?.ToString();
        if (string.IsNullOrEmpty(intoVariable))
        {
            throw new InvalidOperationException("actor_query requires 'into' parameter");
        }

        // Parse query type - use custom if not a known type
        var queryType = Enum.TryParse<OptionsQueryType>(queryStr, ignoreCase: true, out var qt)
            ? qt
            : OptionsQueryType.Custom;

        // Extract optional timeout
        var timeoutMs = DefaultTimeoutMs;
        if (evaluatedParams.TryGetValue("timeout", out var timeoutObj) && timeoutObj != null)
        {
            timeoutMs = Convert.ToInt32(timeoutObj);
        }

        var request = new QueryOptionsRequest
        {
            ActorId = targetActorId,
            QueryType = queryType,
            Freshness = OptionsFreshness.Fresh, // Always fresh for explicit queries
            MaxAgeMs = timeoutMs
        };

        try
        {
            _logger.LogDebug("Querying actor {ActorId} for {Query}, storing in {Variable}",
                targetActorId, queryStr, intoVariable);

            var response = await _actorClient.QueryOptionsAsync(request, ct);

            // Store the options list in scope
            scope.SetValue(intoVariable, response.Options);

            _logger.LogDebug("Query returned {OptionCount} options for actor {ActorId}",
                response.Options.Count, targetActorId);

            return ActionResult.Continue;
        }
        catch (ApiException ex) when (ex.StatusCode == 404)
        {
            _logger.LogWarning("Actor {ActorId} not found when querying {Query}",
                targetActorId, queryStr);

            // Store null in result variable on not found
            scope.SetValue(intoVariable, null);

            // If OnError is defined, store error info
            if (domainAction.OnError != null && domainAction.OnError.Count > 0)
            {
                scope.SetValue("_error", new Dictionary<string, object?>
                {
                    ["message"] = $"Actor not found: {targetActorId}",
                    ["query"] = queryStr,
                    ["target_actor_id"] = targetActorId,
                    ["status_code"] = 404
                });
            }

            return ActionResult.Continue;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to query actor {ActorId} for {Query}",
                targetActorId, queryStr);

            // If OnError is defined, store error info and let caller handle it
            if (domainAction.OnError != null && domainAction.OnError.Count > 0)
            {
                scope.SetValue(intoVariable, null);
                scope.SetValue("_error", new Dictionary<string, object?>
                {
                    ["message"] = ex.Message,
                    ["query"] = queryStr,
                    ["target_actor_id"] = targetActorId
                });
                return ActionResult.Continue;
            }
            throw;
        }
    }
}
