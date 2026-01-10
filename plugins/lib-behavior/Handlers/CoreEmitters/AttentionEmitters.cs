// =============================================================================
// Attention Emitters
// Intent emitters for attention/gaze actions.
// =============================================================================

using BeyondImmersion.BannouService.Behavior;

namespace BeyondImmersion.BannouService.Behavior.Handlers.CoreEmitters;

/// <summary>
/// Emitter for look_at action.
/// </summary>
/// <remarks>
/// ABML usage:
/// <code>
/// - look_at: { target: "${entity}" }
/// - look_at: { position: [10, 0, 5] }
/// </code>
/// </remarks>
public sealed class LookAtEmitter : BaseIntentEmitter
{
    /// <inheritdoc/>
    public override string ActionName => "look_at";

    /// <inheritdoc/>
    public override ValueTask<IReadOnlyList<IntentEmission>> EmitAsync(
        IReadOnlyDictionary<string, object> parameters,
        IntentEmissionContext context,
        CancellationToken ct)
    {
        var urgency = GetOptionalFloat(parameters, "urgency", 0.6f);
        var target = GetOptionalGuid(parameters, "target");
        var position = GetOptionalVector3(parameters, "position");

        // Attention channel is standard across most archetypes
        var channel = context.Archetype?.HasChannel("attention") == true ? "attention" : "alert";

        return ValueTask.FromResult(SingleEmission(
            channel,
            "look_at",
            Math.Clamp(urgency, 0f, 1f),
            target,
            position));
    }
}

/// <summary>
/// Emitter for track action (continuous tracking).
/// </summary>
/// <remarks>
/// ABML usage:
/// <code>
/// - track: { target: "${enemy}", duration: 5.0 }
/// </code>
/// </remarks>
public sealed class TrackEmitter : BaseIntentEmitter
{
    /// <inheritdoc/>
    public override string ActionName => "track";

    /// <inheritdoc/>
    public override ValueTask<IReadOnlyList<IntentEmission>> EmitAsync(
        IReadOnlyDictionary<string, object> parameters,
        IntentEmissionContext context,
        CancellationToken ct)
    {
        var urgency = GetOptionalFloat(parameters, "urgency", 0.7f);
        var target = GetOptionalGuid(parameters, "target");

        var channel = context.Archetype?.HasChannel("attention") == true ? "attention" : "alert";

        return ValueTask.FromResult(SingleEmission(
            channel,
            "track",
            Math.Clamp(urgency, 0f, 1f),
            target));
    }
}
