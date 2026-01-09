// =============================================================================
// Expression Emitters
// Intent emitters for facial expression actions.
// =============================================================================

using BeyondImmersion.BannouService.Behavior;

namespace BeyondImmersion.BannouService.Behavior.Handlers.CoreEmitters;

/// <summary>
/// Emitter for emote action (facial expressions).
/// </summary>
/// <remarks>
/// ABML usage:
/// <code>
/// - emote: { emotion: "happy", intensity: 0.8 }
/// - emote: { emotion: "angry" }
/// - emote: { emotion: "surprised", duration: 2.0 }
/// </code>
/// </remarks>
public sealed class EmoteEmitter : BaseIntentEmitter
{
    /// <inheritdoc/>
    public override string ActionName => "emote";

    /// <inheritdoc/>
    public override ValueTask<IReadOnlyList<IntentEmission>> EmitAsync(
        IReadOnlyDictionary<string, object> parameters,
        IntentEmissionContext context,
        CancellationToken ct)
    {
        var emotion = GetOptionalString(parameters, "emotion") ?? "neutral";
        var intensity = GetOptionalFloat(parameters, "intensity", 0.5f);
        var urgency = GetOptionalFloat(parameters, "urgency", intensity);

        // Expression channel is only available on humanoids
        if (context.Archetype?.HasChannel("expression") != true)
        {
            // Fallback: emit to stance channel for non-humanoids
            if (context.Archetype?.HasChannel("stance") == true || context.Archetype?.HasChannel("alert") == true)
            {
                var fallbackChannel = context.Archetype?.HasChannel("stance") == true ? "stance" : "alert";
                return ValueTask.FromResult(SingleEmission(
                    fallbackChannel,
                    $"emote_{emotion}",
                    Math.Clamp(urgency, 0f, 1f)));
            }

            return ValueTask.FromResult(NoEmission());
        }

        return ValueTask.FromResult(SingleEmission(
            "expression",
            emotion,
            Math.Clamp(urgency, 0f, 1f)));
    }
}
