// =============================================================================
// Emit Perception Handler
// ABML action handler for sending perceptions to characters (Event Brain support).
// Uses character.{characterId}.perceptions topic per ACTOR_BEHAVIORS_GAP_ANALYSIS §6.
// =============================================================================

using BeyondImmersion.BannouService.Abml.Documents.Actions;
using BeyondImmersion.BannouService.Abml.Execution;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.Logging;
using AbmlExecutionContext = BeyondImmersion.BannouService.Abml.Execution.ExecutionContext;

namespace BeyondImmersion.BannouService.Actor.Handlers;

/// <summary>
/// ABML action handler for emitting perception events to character actors.
/// Enables Event Brain to send choreography instructions to participants via the character perception channel.
/// </summary>
/// <remarks>
/// <para>
/// ABML usage:
/// <code>
/// - emit_perception:
///     target_character: "${participant.characterId}"
///     perception_type: "encounter_instruction"
///     source_id: "${encounter.id}"
///     urgency: 0.8
///     data:
///       instruction_type: "move_to_position"
///       position: "${sequence.target_position}"
///       speed: "walk"
/// </code>
/// </para>
/// <para>
/// Publishes to: character.{characterId}.perceptions (same channel game servers use).
/// This follows the "tap" pattern - actors subscribe to their character's perception channel.
/// </para>
/// </remarks>
public sealed class EmitPerceptionHandler : IActionHandler
{
    private const string ACTION_NAME = "emit_perception";
    private readonly IMessageBus _messageBus;
    private readonly ILogger<EmitPerceptionHandler> _logger;
    private readonly ActorServiceConfiguration _config;

    /// <summary>
    /// Creates a new emit perception handler.
    /// </summary>
    /// <param name="messageBus">Message bus for publishing events.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="config">Actor service configuration.</param>
    public EmitPerceptionHandler(
        IMessageBus messageBus,
        ILogger<EmitPerceptionHandler> logger,
        ActorServiceConfiguration config)
    {
        _messageBus = messageBus;
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

        // Get required target_character parameter (must be a Guid)
        var targetCharacterStr = evaluatedParams.GetValueOrDefault("target_character")?.ToString();
        if (string.IsNullOrEmpty(targetCharacterStr))
        {
            throw new InvalidOperationException("emit_perception requires target_character parameter (character ID)");
        }

        if (!Guid.TryParse(targetCharacterStr, out var targetCharacterId))
        {
            throw new InvalidOperationException($"emit_perception target_character must be a valid GUID, got: {targetCharacterStr}");
        }

        // Get required perception_type
        var perceptionType = evaluatedParams.GetValueOrDefault("perception_type")?.ToString();
        if (string.IsNullOrEmpty(perceptionType))
        {
            throw new InvalidOperationException("emit_perception requires perception_type parameter");
        }

        // Get source_id (who/what is sending this perception)
        var sourceId = evaluatedParams.GetValueOrDefault("source_id")?.ToString() ?? "event-brain";

        // Get optional source_type - parse from string to enum for type safety
        var sourceTypeStr = evaluatedParams.GetValueOrDefault("source_type")?.ToString() ?? "coordinator";
        var sourceType = Enum.TryParse<PerceptionSourceType>(sourceTypeStr, ignoreCase: true, out var st) ? st : PerceptionSourceType.Coordinator;

        // Get urgency (default from configuration for Event Brain instructions)
        var urgency = (float)_config.EventBrainDefaultUrgency;
        if (evaluatedParams.TryGetValue("urgency", out var urgencyObj) && urgencyObj != null)
        {
            if (urgencyObj is float f)
                urgency = f;
            else if (urgencyObj is double d)
                urgency = (float)d;
            else if (float.TryParse(urgencyObj.ToString(), out var parsed))
                urgency = parsed;
        }

        // Get perception data
        var data = evaluatedParams.GetValueOrDefault("data");

        // Build the CharacterPerceptionEvent using existing schema types
        var perceptionEvent = new CharacterPerceptionEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            CharacterId = targetCharacterId,
            SourceAppId = "bannou", // Event Brain runs within Bannou
            Perception = new Events.PerceptionData
            {
                PerceptionType = perceptionType,
                SourceId = sourceId,
                SourceType = sourceType,
                Data = data,
                Urgency = urgency
            }
        };

        // Use the standard character perception topic (tap pattern)
        var topic = $"character.{targetCharacterId}.perceptions";

        try
        {
            _logger.LogDebug("Emitting perception to character {TargetCharacterId}, type {PerceptionType}, urgency {Urgency}",
                targetCharacterId, perceptionType, urgency);

            await _messageBus.TryPublishAsync(topic, perceptionEvent, ct);

            _logger.LogDebug("Successfully emitted perception to character {TargetCharacterId}", targetCharacterId);

            return ActionResult.Continue;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to emit perception to character {TargetCharacterId}", targetCharacterId);
            throw;
        }
    }
}
