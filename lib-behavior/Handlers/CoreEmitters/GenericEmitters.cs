// =============================================================================
// Generic Emitters
// Direct intent emission for fine-grained control.
// =============================================================================

namespace BeyondImmersion.BannouService.Behavior.Handlers.CoreEmitters;

/// <summary>
/// Emitter for direct emit_intent action.
/// </summary>
/// <remarks>
/// <para>
/// Allows direct emission to any channel with full parameter control.
/// Used by behavior documents for fine-grained intent control.
/// </para>
/// <para>
/// ABML usage:
/// <code>
/// - emit_intent:
///     channel: combat
///     intent: attack_heavy
///     urgency: 0.9
///     target: "${enemy.id}"
///
/// - emit_intent:
///     channel: movement
///     intent: walk
///     urgency: 0.5
///     position: [10, 0, 5]
/// </code>
/// </para>
/// </remarks>
public sealed class EmitIntentEmitter : BaseIntentEmitter
{
    /// <inheritdoc/>
    public override string ActionName => "emit_intent";

    /// <inheritdoc/>
    public override ValueTask<IReadOnlyList<IntentEmission>> EmitAsync(
        IReadOnlyDictionary<string, object> parameters,
        IntentEmissionContext context,
        CancellationToken ct)
    {
        var channel = GetRequiredString(parameters, "channel");
        var intent = GetRequiredString(parameters, "intent");
        var urgency = GetOptionalFloat(parameters, "urgency", 0.5f);
        var target = GetOptionalGuid(parameters, "target");
        var position = GetOptionalVector3(parameters, "position");

        // Validate channel exists in archetype
        if (!context.Archetype.HasChannel(channel))
        {
            // Try to find a similar channel or use a fallback
            var fallbackChannel = FindFallbackChannel(channel, context);
            if (fallbackChannel == null)
            {
                // Silently drop emission for unsupported channels
                return ValueTask.FromResult(NoEmission());
            }

            channel = fallbackChannel;
        }

        return ValueTask.FromResult(SingleEmission(
            channel,
            intent,
            Math.Clamp(urgency, 0f, 1f),
            target,
            position));
    }

    /// <summary>
    /// Finds a fallback channel when the requested channel doesn't exist.
    /// </summary>
    private static string? FindFallbackChannel(string requestedChannel, IntentEmissionContext context)
    {
        // Map common channel requests to archetype-specific channels
        return requestedChannel.ToLowerInvariant() switch
        {
            "combat" when context.Archetype.HasChannel("action") => "action",
            "movement" when context.Archetype.HasChannel("locomotion") => "locomotion",
            "locomotion" when context.Archetype.HasChannel("movement") => "movement",
            "speech" when context.Archetype.HasChannel("vocalization") => "vocalization",
            "vocalization" when context.Archetype.HasChannel("speech") => "speech",
            "interaction" when context.Archetype.HasChannel("action") => "action",
            "expression" when context.Archetype.HasChannel("stance") => "stance",
            "alert" when context.Archetype.HasChannel("stance") => "stance",
            "stance" when context.Archetype.HasChannel("alert") => "alert",
            "social" when context.Archetype.HasChannel("vocalization") => "vocalization",
            "signals" when context.Archetype.HasChannel("vocalization") => "vocalization",
            _ => null,
        };
    }
}

/// <summary>
/// Emitter for multi_emit action (emit to multiple channels at once).
/// </summary>
/// <remarks>
/// ABML usage:
/// <code>
/// - multi_emit:
///     emissions:
///       - channel: combat
///         intent: attack
///         urgency: 0.8
///       - channel: attention
///         intent: look_at
///         target: "${enemy.id}"
///         urgency: 0.7
/// </code>
/// </remarks>
public sealed class MultiEmitEmitter : BaseIntentEmitter
{
    /// <inheritdoc/>
    public override string ActionName => "multi_emit";

    /// <inheritdoc/>
    public override ValueTask<IReadOnlyList<IntentEmission>> EmitAsync(
        IReadOnlyDictionary<string, object> parameters,
        IntentEmissionContext context,
        CancellationToken ct)
    {
        if (!parameters.TryGetValue("emissions", out var emissionsObj))
        {
            return ValueTask.FromResult(NoEmission());
        }

        var emissions = new List<IntentEmission>();

        if (emissionsObj is IEnumerable<object> emissionList)
        {
            foreach (var item in emissionList)
            {
                if (item is IDictionary<string, object> emissionParams)
                {
                    var channel = emissionParams.TryGetValue("channel", out var c) ? c?.ToString() : null;
                    var intent = emissionParams.TryGetValue("intent", out var i) ? i?.ToString() : null;

                    if (string.IsNullOrEmpty(channel) || string.IsNullOrEmpty(intent))
                    {
                        continue;
                    }

                    if (!context.Archetype.HasChannel(channel))
                    {
                        continue;
                    }

                    var urgency = GetOptionalFloat(emissionParams.AsReadOnly(), "urgency", 0.5f);
                    var target = GetOptionalGuid(emissionParams.AsReadOnly(), "target");
                    var position = GetOptionalVector3(emissionParams.AsReadOnly(), "position");

                    emissions.Add(new IntentEmission(channel, intent, Math.Clamp(urgency, 0f, 1f), target, position));
                }
            }
        }

        return ValueTask.FromResult<IReadOnlyList<IntentEmission>>(emissions);
    }
}

/// <summary>
/// Extension method for dictionary conversion.
/// </summary>
internal static class DictionaryExtensions
{
    /// <summary>
    /// Converts a dictionary to read-only.
    /// </summary>
    public static IReadOnlyDictionary<string, object> AsReadOnly(this IDictionary<string, object> dict)
    {
        return dict as IReadOnlyDictionary<string, object>
            ?? new Dictionary<string, object>(dict);
    }
}
