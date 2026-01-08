// =============================================================================
// Interaction Emitters
// Intent emitters for object/NPC interaction actions.
// =============================================================================

using BeyondImmersion.BannouService.Behavior;

namespace BeyondImmersion.BannouService.Behavior.Handlers.CoreEmitters;

/// <summary>
/// Emitter for use action.
/// </summary>
/// <remarks>
/// ABML usage:
/// <code>
/// - use: { target: "${object}" }
/// - use: { item: "${inventory_item}", target: "${door}" }
/// </code>
/// </remarks>
public sealed class UseEmitter : BaseIntentEmitter
{
    /// <inheritdoc/>
    public override string ActionName => "use";

    /// <inheritdoc/>
    public override ValueTask<IReadOnlyList<IntentEmission>> EmitAsync(
        IReadOnlyDictionary<string, object> parameters,
        IntentEmissionContext context,
        CancellationToken ct)
    {
        var urgency = GetOptionalFloat(parameters, "urgency", 0.5f);
        var target = GetOptionalGuid(parameters, "target");
        var item = GetOptionalString(parameters, "item");

        // Interaction maps to action channel for most archetypes
        var channel = context.Archetype?.HasChannel("interaction") == true ? "interaction" : "action";
        var intent = string.IsNullOrEmpty(item) ? "use" : $"use_{item}";

        return ValueTask.FromResult(SingleEmission(
            channel,
            intent,
            Math.Clamp(urgency, 0f, 1f),
            target));
    }
}

/// <summary>
/// Emitter for pick_up action.
/// </summary>
/// <remarks>
/// ABML usage:
/// <code>
/// - pick_up: { target: "${item}" }
/// </code>
/// </remarks>
public sealed class PickUpEmitter : BaseIntentEmitter
{
    /// <inheritdoc/>
    public override string ActionName => "pick_up";

    /// <inheritdoc/>
    public override ValueTask<IReadOnlyList<IntentEmission>> EmitAsync(
        IReadOnlyDictionary<string, object> parameters,
        IntentEmissionContext context,
        CancellationToken ct)
    {
        var urgency = GetOptionalFloat(parameters, "urgency", 0.5f);
        var target = GetOptionalGuid(parameters, "target");

        var channel = context.Archetype?.HasChannel("interaction") == true ? "interaction" : "action";

        return ValueTask.FromResult(SingleEmission(
            channel,
            "pick_up",
            Math.Clamp(urgency, 0f, 1f),
            target));
    }
}

/// <summary>
/// Emitter for talk_to action.
/// </summary>
/// <remarks>
/// ABML usage:
/// <code>
/// - talk_to: { target: "${npc}" }
/// </code>
/// </remarks>
public sealed class TalkToEmitter : BaseIntentEmitter
{
    /// <inheritdoc/>
    public override string ActionName => "talk_to";

    /// <inheritdoc/>
    public override ValueTask<IReadOnlyList<IntentEmission>> EmitAsync(
        IReadOnlyDictionary<string, object> parameters,
        IntentEmissionContext context,
        CancellationToken ct)
    {
        var urgency = GetOptionalFloat(parameters, "urgency", 0.5f);
        var target = GetOptionalGuid(parameters, "target");

        // Talk_to affects both interaction and attention
        var emissions = new List<IntentEmission>();

        // Primary interaction
        var interactionChannel = context.Archetype?.HasChannel("interaction") == true ? "interaction" : "action";
        emissions.Add(new IntentEmission(interactionChannel, "talk_to", Math.Clamp(urgency, 0f, 1f), target));

        // Also direct attention
        if (context.Archetype?.HasChannel("attention") == true)
        {
            emissions.Add(new IntentEmission("attention", "look_at", Math.Clamp(urgency, 0f, 1f), target));
        }

        return ValueTask.FromResult<IReadOnlyList<IntentEmission>>(emissions);
    }
}
