// =============================================================================
// Set Encounter Phase Handler
// ABML action handler for transitioning encounter phases (Event Brain support).
// =============================================================================

using BeyondImmersion.Bannou.BehaviorCompiler.Documents.Actions;
using BeyondImmersion.BannouService.Abml.Execution;
using BeyondImmersion.BannouService.Abml.Expressions;
using BeyondImmersion.BannouService.Actor.Runtime;
using Microsoft.Extensions.Logging;
using AbmlExecutionContext = BeyondImmersion.BannouService.Abml.Execution.ExecutionContext;

namespace BeyondImmersion.BannouService.Actor.Handlers;

/// <summary>
/// ABML action handler for setting encounter phase.
/// Enables Event Brain actors to transition through encounter phases from ABML behavior.
/// </summary>
/// <remarks>
/// <para>
/// ABML usage:
/// <code>
/// - set_encounter_phase:
///     phase: "gathering_options"
///
/// - set_encounter_phase:
///     phase: "${next_phase}"
///     result_variable: "phase_set_success"
/// </code>
/// </para>
/// <para>
/// This handler uses the local actor registry to find the executing actor
/// and call SetEncounterPhase on its runner. The actor ID is obtained from
/// the execution scope (agent.id).
/// </para>
/// </remarks>
public sealed class SetEncounterPhaseHandler : IActionHandler
{
    private const string ACTION_NAME = "set_encounter_phase";
    private readonly IActorRegistry _actorRegistry;
    private readonly ILogger<SetEncounterPhaseHandler> _logger;

    /// <summary>
    /// Creates a new set encounter phase handler.
    /// </summary>
    /// <param name="actorRegistry">Actor registry for finding the executing actor.</param>
    /// <param name="logger">Logger instance.</param>
    public SetEncounterPhaseHandler(
        IActorRegistry actorRegistry,
        ILogger<SetEncounterPhaseHandler> logger)
    {
        _actorRegistry = actorRegistry;
        _logger = logger;
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

        // Get required phase parameter
        var phase = evaluatedParams.GetValueOrDefault("phase")?.ToString();
        if (string.IsNullOrEmpty(phase))
        {
            throw new InvalidOperationException("set_encounter_phase requires phase parameter");
        }

        var resultVariable = evaluatedParams.GetValueOrDefault("result_variable")?.ToString();

        // Get actor ID from scope (agent.id)
        var actorId = GetActorIdFromScope(scope);
        if (string.IsNullOrEmpty(actorId))
        {
            _logger.LogWarning("Cannot set encounter phase: actor ID not found in scope");
            SetResult(scope, resultVariable, false);
            return ValueTask.FromResult(ActionResult.Continue);
        }

        // Find actor in local registry
        if (!_actorRegistry.TryGet(actorId, out var runner) || runner == null)
        {
            _logger.LogWarning("Cannot set encounter phase: actor {ActorId} not found in registry", actorId);
            SetResult(scope, resultVariable, false);
            return ValueTask.FromResult(ActionResult.Continue);
        }

        // Set the encounter phase
        var success = runner.SetEncounterPhase(phase);

        if (success)
        {
            _logger.LogDebug("Actor {ActorId} encounter phase set to {Phase}", actorId, phase);
        }
        else
        {
            _logger.LogWarning("Actor {ActorId} failed to set encounter phase to {Phase} (no active encounter?)",
                actorId, phase);
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
