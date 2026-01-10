// =============================================================================
// Vocalization Emitters
// Intent emitters for speech and sound actions.
// =============================================================================

using BeyondImmersion.BannouService.Behavior;

namespace BeyondImmersion.BannouService.Behavior.Handlers.CoreEmitters;

/// <summary>
/// Emitter for speak action.
/// </summary>
/// <remarks>
/// ABML usage:
/// <code>
/// - speak: { text: "Hello there!", volume: "normal" }
/// - speak: { sound_id: "greeting_01" }
/// </code>
/// </remarks>
public sealed class SpeakEmitter : BaseIntentEmitter
{
    /// <inheritdoc/>
    public override string ActionName => "speak";

    /// <inheritdoc/>
    public override ValueTask<IReadOnlyList<IntentEmission>> EmitAsync(
        IReadOnlyDictionary<string, object> parameters,
        IntentEmissionContext context,
        CancellationToken ct)
    {
        var volume = GetOptionalString(parameters, "volume") ?? "normal";
        var urgency = GetOptionalFloat(parameters, "urgency", 0.5f);

        // Speech maps to vocalization for humanoids, social for creatures
        var channel = context.Archetype?.HasChannel("speech") == true
            ? "speech"
            : context.Archetype?.HasChannel("vocalization") == true
                ? "vocalization"
                : context.Archetype?.HasChannel("social") == true
                    ? "social"
                    : "feedback"; // Fallback for objects

        return ValueTask.FromResult(SingleEmission(
            channel,
            $"speak_{volume}",
            Math.Clamp(urgency, 0f, 1f)));
    }
}

/// <summary>
/// Emitter for shout action.
/// </summary>
/// <remarks>
/// ABML usage:
/// <code>
/// - shout: { text: "Watch out!" }
/// - shout: { alert_type: "danger" }
/// </code>
/// </remarks>
public sealed class ShoutEmitter : BaseIntentEmitter
{
    /// <inheritdoc/>
    public override string ActionName => "shout";

    /// <inheritdoc/>
    public override ValueTask<IReadOnlyList<IntentEmission>> EmitAsync(
        IReadOnlyDictionary<string, object> parameters,
        IntentEmissionContext context,
        CancellationToken ct)
    {
        var alertType = GetOptionalString(parameters, "alert_type") ?? "general";
        var urgency = GetOptionalFloat(parameters, "urgency", 0.8f);

        var channel = context.Archetype?.HasChannel("speech") == true
            ? "speech"
            : context.Archetype?.HasChannel("vocalization") == true
                ? "vocalization"
                : context.Archetype?.HasChannel("social") == true
                    ? "social"
                    : "signals"; // Fallback for vehicles

        return ValueTask.FromResult(SingleEmission(
            channel,
            $"shout_{alertType}",
            Math.Clamp(urgency, 0f, 1f)));
    }
}
