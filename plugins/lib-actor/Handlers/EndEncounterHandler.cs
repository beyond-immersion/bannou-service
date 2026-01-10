// =============================================================================
// End Encounter Handler
// ABML action handler for ending encounters (Event Brain support).
// =============================================================================

using BeyondImmersion.BannouService.Abml.Documents.Actions;
using BeyondImmersion.BannouService.Abml.Execution;
using BeyondImmersion.BannouService.Abml.Expressions;
using BeyondImmersion.BannouService.Actor.Runtime;
using Microsoft.Extensions.Logging;
using AbmlExecutionContext = BeyondImmersion.BannouService.Abml.Execution.ExecutionContext;

namespace BeyondImmersion.BannouService.Actor.Handlers;

/// <summary>
/// ABML action handler for ending an encounter.
/// Enables Event Brain actors to cleanly end encounters from ABML behavior.
/// </summary>
/// <remarks>
/// <para>
/// ABML usage:
/// <code>
/// - end_encounter:
///     result_variable: "encounter_ended"
/// </code>
/// </para>
/// <para>
/// This handler uses the local actor registry to find the executing actor
/// and call EndEncounter on its runner. The actor ID is obtained from
/// the execution scope (agent.id).
/// </para>
/// </remarks>
public sealed class EndEncounterHandler : IActionHandler
{
    private const string ACTION_NAME = "end_encounter";
    private readonly IActorRegistry _actorRegistry;
    private readonly ILogger<EndEncounterHandler> _logger;

    /// <summary>
    /// Creates a new end encounter handler.
    /// </summary>
    /// <param name="actorRegistry">Actor registry for finding the executing actor.</param>
    /// <param name="logger">Logger instance.</param>
    public EndEncounterHandler(
        IActorRegistry actorRegistry,
        ILogger<EndEncounterHandler> logger)
    {
        _actorRegistry = actorRegistry ?? throw new ArgumentNullException(nameof(actorRegistry));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public bool CanHandle(ActionNode action)
        => action is DomainAction da && da.Name == ACTION_NAME;

    /// <inheritdoc/>
    public ValueTask<ActionResult> ExecuteAsync(
        ActionNode action,
        AbmlExecutionContext context,
        CancellationToken ct)
    {
        var domainAction = (DomainAction)action;
        var scope = context.CallStack.Current?.Scope ?? context.RootScope;

        // Evaluate parameters
        var evaluatedParams = ValueEvaluator.EvaluateParameters(
            domainAction.Parameters, scope, context.Evaluator);

        var resultVariable = evaluatedParams.GetValueOrDefault("result_variable")?.ToString();

        // Get actor ID from scope (agent.id)
        var actorId = GetActorIdFromScope(scope);
        if (string.IsNullOrEmpty(actorId))
        {
            _logger.LogWarning("Cannot end encounter: actor ID not found in scope");
            SetResult(scope, resultVariable, false);
            return ValueTask.FromResult(ActionResult.Continue);
        }

        // Find actor in local registry
        if (!_actorRegistry.TryGet(actorId, out var runner) || runner == null)
        {
            _logger.LogWarning("Cannot end encounter: actor {ActorId} not found in registry", actorId);
            SetResult(scope, resultVariable, false);
            return ValueTask.FromResult(ActionResult.Continue);
        }

        // End the encounter
        var success = runner.EndEncounter();

        if (success)
        {
            _logger.LogInformation("Actor {ActorId} ended encounter", actorId);
        }
        else
        {
            _logger.LogWarning("Actor {ActorId} failed to end encounter (no active encounter?)", actorId);
        }

        SetResult(scope, resultVariable, success);
        return ValueTask.FromResult(ActionResult.Continue);
    }

    /// <summary>
    /// Gets the actor ID from the execution scope.
    /// </summary>
    private static string? GetActorIdFromScope(IVariableScope scope)
    {
        var agent = scope.GetValue("agent");
        if (agent is Dictionary<string, object?> agentDict)
        {
            return agentDict.GetValueOrDefault("id")?.ToString();
        }
        return null;
    }

    /// <summary>
    /// Sets the result variable if specified.
    /// </summary>
    private static void SetResult(IVariableScope scope, string? resultVariable, bool success)
    {
        if (!string.IsNullOrEmpty(resultVariable))
        {
            scope.SetValue(resultVariable, success);
        }
    }
}
