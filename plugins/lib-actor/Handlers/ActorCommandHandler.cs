// =============================================================================
// Actor Command Handler
// ABML action handler for sending commands to character actors (Event Brain support).
// =============================================================================

using BeyondImmersion.Bannou.BehaviorCompiler.Documents.Actions;
using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService.Abml.Execution;
using BeyondImmersion.BannouService.Actor;
using Microsoft.Extensions.Logging;
using AbmlExecutionContext = BeyondImmersion.BannouService.Abml.Execution.ExecutionContext;

namespace BeyondImmersion.BannouService.Actor.Handlers;

/// <summary>
/// ABML action handler for sending commands to character actors.
/// Enables Event Brain actors to direct Character Brain actors via perception injection.
/// </summary>
/// <remarks>
/// <para>
/// ABML usage:
/// <code>
/// - actor_command:
///     target: ${attacker.actor_id}    # Required - expression evaluating to actor ID
///     command: engage_target          # Required - command name (identifier)
///     urgency: 0.8                    # Optional - default 0.7
///     params:                         # Optional - command parameters
///       target_id: ${defender.character_id}
///       strategy: aggressive
/// </code>
/// </para>
/// <para>
/// Injects a perception of type <c>command:{commandName}</c> into the target actor's queue.
/// Character Brain behaviors can handle commands via flows named <c>on_command_{command_name}</c>.
/// </para>
/// </remarks>
public sealed class ActorCommandHandler : IActionHandler
{
    private const string ActionName = "actor_command";
    private const float DefaultUrgency = 0.7f;

    private readonly IActorClient _actorClient;
    private readonly ILogger<ActorCommandHandler> _logger;

    /// <summary>
    /// Creates a new actor command handler.
    /// </summary>
    /// <param name="actorClient">Actor client for service calls.</param>
    /// <param name="logger">Logger instance.</param>
    public ActorCommandHandler(
        IActorClient actorClient,
        ILogger<ActorCommandHandler> logger)
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
            throw new InvalidOperationException("actor_command requires 'target' parameter");
        }

        var command = evaluatedParams.GetValueOrDefault("command")?.ToString();
        if (string.IsNullOrEmpty(command))
        {
            throw new InvalidOperationException("actor_command requires 'command' parameter");
        }

        // Extract optional urgency parameter
        var urgency = DefaultUrgency;
        if (evaluatedParams.TryGetValue("urgency", out var urgencyObj) && urgencyObj != null)
        {
            urgency = urgencyObj switch
            {
                float f => f,
                double d => (float)d,
                _ when float.TryParse(urgencyObj.ToString(), out var parsed) => parsed,
                _ => DefaultUrgency
            };
        }

        // Extract command params (nested dictionary)
        Dictionary<string, object?>? commandParams = null;
        if (evaluatedParams.TryGetValue("params", out var paramsObj)
            && paramsObj is IReadOnlyDictionary<string, object?> paramsDict)
        {
            commandParams = new Dictionary<string, object?>(paramsDict);
        }

        // Build perception for command
        var perception = new PerceptionData
        {
            PerceptionType = $"command:{command}",
            SourceId = context.ActorId?.ToString() ?? "event-brain",
            SourceType = PerceptionSourceType.Coordinator,
            Urgency = urgency,
            Data = commandParams
        };

        var request = new InjectPerceptionRequest
        {
            ActorId = targetActorId,
            Perception = perception
        };

        try
        {
            _logger.LogDebug("Sending command {Command} to actor {ActorId} with urgency {Urgency}",
                command, targetActorId, urgency);

            await _actorClient.InjectPerceptionAsync(request, ct);

            _logger.LogDebug("Successfully sent command {Command} to actor {ActorId}",
                command, targetActorId);

            return ActionResult.Continue;
        }
        catch (ApiException ex) when (ex.StatusCode == 404)
        {
            _logger.LogWarning("Actor {ActorId} not found when sending command {Command}",
                targetActorId, command);

            // If OnError is defined, store error info and let caller handle it
            if (domainAction.OnError != null && domainAction.OnError.Count > 0)
            {
                scope.SetValue("_error", new Dictionary<string, object?>
                {
                    ["message"] = $"Actor not found: {targetActorId}",
                    ["command"] = command,
                    ["target_actor_id"] = targetActorId,
                    ["status_code"] = 404
                });
                return ActionResult.Continue;
            }
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send command {Command} to actor {ActorId}",
                command, targetActorId);

            // If OnError is defined, store error info and let caller handle it
            if (domainAction.OnError != null && domainAction.OnError.Count > 0)
            {
                scope.SetValue("_error", new Dictionary<string, object?>
                {
                    ["message"] = ex.Message,
                    ["command"] = command,
                    ["target_actor_id"] = targetActorId
                });
                return ActionResult.Continue;
            }
            throw;
        }
    }
}
