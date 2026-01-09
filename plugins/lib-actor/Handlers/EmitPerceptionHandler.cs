// =============================================================================
// Emit Perception Handler
// ABML action handler for sending perceptions to other actors (Event Brain support).
// =============================================================================

using BeyondImmersion.BannouService.Abml.Documents.Actions;
using BeyondImmersion.BannouService.Abml.Execution;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.Logging;
using AbmlExecutionContext = BeyondImmersion.BannouService.Abml.Execution.ExecutionContext;

namespace BeyondImmersion.BannouService.Actor.Handlers;

/// <summary>
/// ABML action handler for emitting perception events to other actors.
/// Enables Event Brain to send choreography instructions to participants.
/// </summary>
/// <remarks>
/// <para>
/// ABML usage:
/// <code>
/// - emit_perception:
///     target_actor: "${participant.actorId}"
///     perception_type: "choreography_instruction"
///     data:
///       encounter_id: "${encounter.id}"
///       sequence_id: "${sequence.id}"
///       actions: "${sequence.actions}"
///       timing: "${sequence.timing}"
///       priority: "high"
/// </code>
/// </para>
/// </remarks>
public sealed class EmitPerceptionHandler : IActionHandler
{
    private const string ACTION_NAME = "emit_perception";
    private readonly IMessageBus _messageBus;
    private readonly ILogger<EmitPerceptionHandler> _logger;

    /// <summary>
    /// Creates a new emit perception handler.
    /// </summary>
    /// <param name="messageBus">Message bus for publishing events.</param>
    /// <param name="logger">Logger instance.</param>
    public EmitPerceptionHandler(
        IMessageBus messageBus,
        ILogger<EmitPerceptionHandler> logger)
    {
        _messageBus = messageBus ?? throw new ArgumentNullException(nameof(messageBus));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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
        var targetActor = evaluatedParams.GetValueOrDefault("target_actor")?.ToString();
        if (string.IsNullOrEmpty(targetActor))
        {
            throw new InvalidOperationException("emit_perception requires target_actor parameter");
        }

        var perceptionType = evaluatedParams.GetValueOrDefault("perception_type")?.ToString();
        if (string.IsNullOrEmpty(perceptionType))
        {
            throw new InvalidOperationException("emit_perception requires perception_type parameter");
        }

        // Get perception data
        var data = evaluatedParams.GetValueOrDefault("data") ?? throw new InvalidOperationException("emit_perception requires data parameter");

        // Build perception event
        var perceptionEvent = new PerceptionEvent
        {
            TargetActorId = targetActor,
            PerceptionType = perceptionType,
            Timestamp = DateTimeOffset.UtcNow,
            Data = ConvertToDictionary(data)
        };

        // Determine topic - perceptions route to character.{characterId}.perceptions
        // For actor-based routing, we use actor.{actorId}.perceptions
        var topic = $"actor.{targetActor}.perceptions";

        try
        {
            _logger.LogDebug("Emitting perception to actor {TargetActor}, type {PerceptionType}",
                targetActor, perceptionType);

            await _messageBus.TryPublishAsync(topic, perceptionEvent, ct);

            _logger.LogDebug("Successfully emitted perception to actor {TargetActor}", targetActor);

            return ActionResult.Continue;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to emit perception to actor {TargetActor}", targetActor);
            throw;
        }
    }

    private static Dictionary<string, object?> ConvertToDictionary(object? data)
    {
        if (data == null)
        {
            return new Dictionary<string, object?>();
        }

        if (data is Dictionary<string, object?> dict)
        {
            return dict;
        }

        if (data is IReadOnlyDictionary<string, object?> roDict)
        {
            return new Dictionary<string, object?>(roDict);
        }

        // For other types, wrap in a "value" key
        return new Dictionary<string, object?> { ["value"] = data };
    }
}

/// <summary>
/// Perception event sent to actors by Event Brain.
/// </summary>
public sealed class PerceptionEvent
{
    /// <summary>
    /// Target actor ID.
    /// </summary>
    public required string TargetActorId { get; init; }

    /// <summary>
    /// Type of perception (e.g., "choreography_instruction", "combat_started").
    /// </summary>
    public required string PerceptionType { get; init; }

    /// <summary>
    /// When this perception was emitted.
    /// </summary>
    public DateTimeOffset Timestamp { get; init; }

    /// <summary>
    /// Perception data payload.
    /// </summary>
    public Dictionary<string, object?>? Data { get; init; }
}
