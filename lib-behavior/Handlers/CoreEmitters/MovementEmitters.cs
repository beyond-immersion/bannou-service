// =============================================================================
// Movement Emitters
// Intent emitters for locomotion actions.
// =============================================================================

using BeyondImmersion.BannouService.Behavior;

namespace BeyondImmersion.BannouService.Behavior.Handlers.CoreEmitters;

/// <summary>
/// Emitter for walk_to action.
/// </summary>
/// <remarks>
/// ABML usage:
/// <code>
/// - walk_to: { target: "${pos}", speed: 0.5 }
/// - walk_to: { entity: "${target_entity}" }
/// - walk_to: { mark: "waypoint_1" }
/// </code>
/// </remarks>
public sealed class WalkToEmitter : BaseIntentEmitter
{
    /// <inheritdoc/>
    public override string ActionName => "walk_to";

    /// <inheritdoc/>
    public override ValueTask<IReadOnlyList<IntentEmission>> EmitAsync(
        IReadOnlyDictionary<string, object> parameters,
        IntentEmissionContext context,
        CancellationToken ct)
    {
        var urgency = GetOptionalFloat(parameters, "urgency", 0.5f);
        var targetPos = GetOptionalVector3(parameters, "target");
        var targetEntity = GetOptionalGuid(parameters, "entity");

        // Determine the channel based on archetype (default to movement)
        var channel = context.Archetype?.HasChannel("movement") == true ? "movement" : "locomotion";

        return ValueTask.FromResult(SingleEmission(
            channel,
            "walk",
            Math.Clamp(urgency, 0f, 1f),
            targetEntity,
            targetPos));
    }
}

/// <summary>
/// Emitter for run_to action.
/// </summary>
/// <remarks>
/// ABML usage:
/// <code>
/// - run_to: { target: "${pos}", urgency: 0.8 }
/// </code>
/// </remarks>
public sealed class RunToEmitter : BaseIntentEmitter
{
    /// <inheritdoc/>
    public override string ActionName => "run_to";

    /// <inheritdoc/>
    public override ValueTask<IReadOnlyList<IntentEmission>> EmitAsync(
        IReadOnlyDictionary<string, object> parameters,
        IntentEmissionContext context,
        CancellationToken ct)
    {
        var urgency = GetOptionalFloat(parameters, "urgency", 0.7f);
        var targetPos = GetOptionalVector3(parameters, "target");
        var targetEntity = GetOptionalGuid(parameters, "entity");

        var channel = context.Archetype?.HasChannel("movement") == true ? "movement" : "locomotion";

        return ValueTask.FromResult(SingleEmission(
            channel,
            "run",
            Math.Clamp(urgency, 0f, 1f),
            targetEntity,
            targetPos));
    }
}

/// <summary>
/// Emitter for stop action.
/// </summary>
/// <remarks>
/// ABML usage:
/// <code>
/// - stop: {}
/// - stop: { urgency: 1.0 }
/// </code>
/// </remarks>
public sealed class StopEmitter : BaseIntentEmitter
{
    /// <inheritdoc/>
    public override string ActionName => "stop";

    /// <inheritdoc/>
    public override ValueTask<IReadOnlyList<IntentEmission>> EmitAsync(
        IReadOnlyDictionary<string, object> parameters,
        IntentEmissionContext context,
        CancellationToken ct)
    {
        var urgency = GetOptionalFloat(parameters, "urgency", 0.8f);
        var channel = context.Archetype?.HasChannel("movement") == true ? "movement" : "locomotion";

        return ValueTask.FromResult(SingleEmission(
            channel,
            "stop",
            Math.Clamp(urgency, 0f, 1f)));
    }
}
