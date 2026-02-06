// =============================================================================
// Combat Emitters
// Intent emitters for combat actions.
// =============================================================================

using BeyondImmersion.Bannou.BehaviorCompiler.Archetypes;
using BeyondImmersion.BannouService.Behavior;

namespace BeyondImmersion.BannouService.Behavior.Handlers.CoreEmitters;

/// <summary>
/// Emitter for attack action.
/// </summary>
/// <remarks>
/// ABML usage:
/// <code>
/// - attack: { type: "heavy", target: "${enemy}" }
/// - attack: { type: "light", direction: "forward" }
/// </code>
/// </remarks>
public sealed class AttackEmitter : BaseIntentEmitter
{
    /// <inheritdoc/>
    public override string ActionName => "attack";

    /// <inheritdoc/>
    public override ValueTask<IReadOnlyList<IntentEmission>> EmitAsync(
        IReadOnlyDictionary<string, object> parameters,
        IntentEmissionContext context,
        CancellationToken ct)
    {
        var attackType = GetOptionalString(parameters, "type") ?? "basic";
        var urgency = GetOptionalFloat(parameters, "urgency", 0.8f);
        var target = GetOptionalGuid(parameters, "target");

        // Combat maps to action channel for most archetypes
        var channel = context.Archetype?.HasChannel("combat") == true ? "combat" : "action";
        var intent = $"attack_{attackType}";

        return ValueTask.FromResult(SingleEmission(
            channel,
            intent,
            Math.Clamp(urgency, 0f, 1f),
            target));
    }
}

/// <summary>
/// Emitter for block action.
/// </summary>
/// <remarks>
/// ABML usage:
/// <code>
/// - block: { direction: "front" }
/// - block: {}
/// </code>
/// </remarks>
public sealed class BlockEmitter : BaseIntentEmitter
{
    /// <inheritdoc/>
    public override string ActionName => "block";

    /// <inheritdoc/>
    public override ValueTask<IReadOnlyList<IntentEmission>> EmitAsync(
        IReadOnlyDictionary<string, object> parameters,
        IntentEmissionContext context,
        CancellationToken ct)
    {
        var direction = GetOptionalString(parameters, "direction") ?? "front";
        var urgency = GetOptionalFloat(parameters, "urgency", 0.7f);

        var channel = context.Archetype?.HasChannel("combat") == true ? "combat" : "action";
        var intent = $"block_{direction}";

        return ValueTask.FromResult(SingleEmission(
            channel,
            intent,
            Math.Clamp(urgency, 0f, 1f)));
    }
}

/// <summary>
/// Emitter for dodge action.
/// </summary>
/// <remarks>
/// ABML usage:
/// <code>
/// - dodge: { direction: "left" }
/// - dodge: { direction: "back", urgency: 0.9 }
/// </code>
/// </remarks>
public sealed class DodgeEmitter : BaseIntentEmitter
{
    /// <inheritdoc/>
    public override string ActionName => "dodge";

    /// <inheritdoc/>
    public override ValueTask<IReadOnlyList<IntentEmission>> EmitAsync(
        IReadOnlyDictionary<string, object> parameters,
        IntentEmissionContext context,
        CancellationToken ct)
    {
        var direction = GetOptionalString(parameters, "direction") ?? "back";
        var urgency = GetOptionalFloat(parameters, "urgency", 0.85f);

        var channel = context.Archetype?.HasChannel("combat") == true ? "combat" : "action";
        var intent = $"dodge_{direction}";

        // Dodge also affects movement channel
        var emissions = new List<IntentEmission>
        {
            new IntentEmission(channel, intent, Math.Clamp(urgency, 0f, 1f)),
        };

        // If archetype has movement channel, also emit there
        if (context.Archetype?.HasChannel("movement") == true || context.Archetype?.HasChannel("locomotion") == true)
        {
            var movementChannel = context.Archetype?.HasChannel("movement") == true ? "movement" : "locomotion";
            emissions.Add(new IntentEmission(movementChannel, $"dodge_{direction}", Math.Clamp(urgency, 0f, 1f)));
        }

        return ValueTask.FromResult<IReadOnlyList<IntentEmission>>(emissions);
    }
}
