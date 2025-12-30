// =============================================================================
// Intent Merger
// Urgency-based conflict resolution for multi-model behavior coordination.
// =============================================================================

using BeyondImmersion.Bannou.Client.SDK.Behavior.Runtime;
using System.Numerics;

namespace BeyondImmersion.Bannou.Client.SDK.Behavior.Intent;

/// <summary>
/// Merges behavior outputs from multiple models using urgency-based resolution.
/// </summary>
/// <remarks>
/// <para>
/// Each character can have up to 4 simultaneously-active behavior models.
/// The IntentMerger resolves which model's output wins for each channel.
/// </para>
/// <para>
/// Resolution strategies:
/// - Exclusive channels (Action, Stance, Vocalization): Highest urgency wins
/// - Blendable channels (Locomotion): Weighted blend by urgency
/// - Attention: Blendable (outputs primary/secondary targets with weights for gaze interpolation)
/// </para>
/// <para>
/// Zero-urgency outputs are ignored (model didn't emit for that channel).
/// </para>
/// </remarks>
public sealed class IntentMerger
{
    /// <summary>
    /// Minimum urgency threshold. Outputs below this are treated as zero.
    /// </summary>
    public const float UrgencyThreshold = 0.001f;

    /// <summary>
    /// Merge outputs from all active behavior models into a single MergedIntent.
    /// </summary>
    /// <param name="combat">Output from combat behavior model (may be null).</param>
    /// <param name="movement">Output from movement behavior model (may be null).</param>
    /// <param name="interaction">Output from interaction behavior model (may be null).</param>
    /// <param name="idle">Output from idle behavior model (may be null).</param>
    /// <returns>Merged intent for game engine consumption.</returns>
    public MergedIntent Merge(
        BehaviorOutput? combat,
        BehaviorOutput? movement,
        BehaviorOutput? interaction,
        BehaviorOutput? idle)
    {
#if DEBUG
        var trace = new ContributionTrace();
#endif

        // Collect all contributions per channel
        var outputs = new (BehaviorOutput? output, BehaviorModelType type)[]
        {
            (combat, BehaviorModelType.Combat),
            (movement, BehaviorModelType.Movement),
            (interaction, BehaviorModelType.Interaction),
            (idle, BehaviorModelType.Idle),
        };

        // ACTION: Exclusive - highest urgency wins
        var (action, actionTarget, actionUrgency, actionWinner) = SelectExclusiveAction(outputs);

        // LOCOMOTION: Blendable - weighted blend by urgency
        var locomotion = BlendLocomotion(outputs);

        // ATTENTION: Blendable - outputs primary/secondary targets with weights
        var attention = BlendAttention(outputs);

        // STANCE: Exclusive - highest urgency wins
        var (stance, stanceUrgency, stanceWinner) = SelectExclusiveStance(outputs);

        // VOCALIZATION: Exclusive - highest urgency wins
        var (vocalization, vocalizationUrgency, vocalizationWinner) = SelectExclusiveVocalization(outputs);

#if DEBUG
        // Record trace information
        trace.AddResolution(IntentChannel.Action, actionWinner, actionUrgency,
            CollectContributions(outputs, o => (o.ActionIntent, o.ActionUrgency)));
        trace.AddResolution(IntentChannel.Locomotion, null, locomotion.Urgency,
            CollectLocomotionContributions(outputs));
        trace.AddResolution(IntentChannel.Attention, null, attention.TotalUrgency,
            CollectAttentionContributions(outputs));
        trace.AddResolution(IntentChannel.Stance, stanceWinner, stanceUrgency,
            CollectContributions(outputs, o => (o.StanceIntent, o.StanceUrgency)));
        trace.AddResolution(IntentChannel.Vocalization, vocalizationWinner, vocalizationUrgency,
            CollectContributions(outputs, o => (o.VocalizationIntent, o.VocalizationUrgency)));
#endif

        return new MergedIntent
        {
            Action = action,
            ActionTarget = actionTarget,
            ActionUrgency = actionUrgency,
            Locomotion = locomotion,
            Attention = attention,
            Stance = stance,
            StanceUrgency = stanceUrgency,
            Vocalization = vocalization,
            VocalizationUrgency = vocalizationUrgency,
#if DEBUG
            Trace = trace,
#endif
        };
    }

    /// <summary>
    /// Selects the action with highest urgency.
    /// </summary>
    private static (string? intent, Guid? target, float urgency, BehaviorModelType? winner) SelectExclusiveAction(
        (BehaviorOutput? output, BehaviorModelType type)[] outputs)
    {
        string? winningIntent = null;
        Guid? winningTarget = null;
        float highestUrgency = 0f;
        BehaviorModelType? winner = null;

        foreach (var (output, type) in outputs)
        {
            if (output is not { } o)
                continue;

            if (o.ActionUrgency > highestUrgency && o.ActionUrgency >= UrgencyThreshold)
            {
                highestUrgency = o.ActionUrgency;
                winningIntent = o.ActionIntent;
                winningTarget = o.ActionTarget;
                winner = type;
            }
        }

        return (winningIntent, winningTarget, highestUrgency, winner);
    }

    /// <summary>
    /// Blends locomotion from all contributors weighted by urgency.
    /// </summary>
    private static LocomotionIntent BlendLocomotion(
        (BehaviorOutput? output, BehaviorModelType type)[] outputs)
    {
        // Collect all valid locomotion contributions
        var contributions = new List<(string intent, Vector3? target, float urgency)>();
        float totalUrgency = 0f;

        foreach (var (output, _) in outputs)
        {
            if (output is not { } o)
                continue;

            if (o.LocomotionUrgency >= UrgencyThreshold && o.LocomotionIntent != null)
            {
                contributions.Add((o.LocomotionIntent, o.LocomotionTarget, o.LocomotionUrgency));
                totalUrgency += o.LocomotionUrgency;
            }
        }

        if (contributions.Count == 0)
            return LocomotionIntent.None;

        // If only one contributor, use it directly
        if (contributions.Count == 1)
        {
            var (intent, target, urgency) = contributions[0];
            return target.HasValue
                ? LocomotionIntent.Create(intent, target.Value, 1f, urgency)
                : LocomotionIntent.CreateWithoutTarget(intent, urgency);
        }

        // Multiple contributors - blend targets weighted by urgency
        // Use highest-urgency intent name (can't meaningfully blend intent names)
        var highestUrgencyContribution = contributions.OrderByDescending(c => c.urgency).First();
        var blendedIntent = highestUrgencyContribution.intent;

        // Blend target positions
        Vector3? blendedTarget = null;
        float targetWeightSum = 0f;
        var targetSum = Vector3.Zero;

        foreach (var (_, target, urgency) in contributions)
        {
            if (target.HasValue)
            {
                var weight = urgency / totalUrgency;
                targetSum += target.Value * weight;
                targetWeightSum += weight;
            }
        }

        if (targetWeightSum > 0)
        {
            blendedTarget = targetSum;
        }

        // Blended urgency is the sum clamped to 1.0
        var blendedUrgency = Math.Min(totalUrgency, 1f);

        return blendedTarget.HasValue
            ? LocomotionIntent.Create(blendedIntent, blendedTarget.Value, 1f, blendedUrgency)
            : LocomotionIntent.CreateWithoutTarget(blendedIntent, blendedUrgency);
    }

    /// <summary>
    /// Blends attention from all contributors, outputting primary and secondary targets with weights.
    /// </summary>
    /// <remarks>
    /// Entity IDs (Guids) cannot be mathematically blended, so we output the top two
    /// contributors with their urgency-derived weights. The game resolves entity positions
    /// and performs actual gaze interpolation.
    /// </remarks>
    private static AttentionIntent BlendAttention(
        (BehaviorOutput? output, BehaviorModelType type)[] outputs)
    {
        // Collect all valid attention contributions sorted by urgency
        var contributions = new List<(Guid target, float urgency)>();

        foreach (var (output, _) in outputs)
        {
            if (output is not { } o)
                continue;

            if (o.AttentionUrgency >= UrgencyThreshold && o.AttentionTarget.HasValue)
            {
                contributions.Add((o.AttentionTarget.Value, o.AttentionUrgency));
            }
        }

        if (contributions.Count == 0)
            return AttentionIntent.None;

        // Sort by urgency descending to get primary and secondary
        contributions.Sort((a, b) => b.urgency.CompareTo(a.urgency));

        // If only one contributor, no blending needed
        if (contributions.Count == 1)
        {
            return AttentionIntent.CreateSingle(contributions[0].target, contributions[0].urgency);
        }

        // Two or more contributors - create blended attention with top two
        var primary = contributions[0];
        var secondary = contributions[1];

        return AttentionIntent.CreateBlended(
            primary.target,
            primary.urgency,
            secondary.target,
            secondary.urgency);
    }

    /// <summary>
    /// Selects the stance with highest urgency.
    /// </summary>
    private static (string? intent, float urgency, BehaviorModelType? winner) SelectExclusiveStance(
        (BehaviorOutput? output, BehaviorModelType type)[] outputs)
    {
        string? winningIntent = null;
        float highestUrgency = 0f;
        BehaviorModelType? winner = null;

        foreach (var (output, type) in outputs)
        {
            if (output is not { } o)
                continue;

            if (o.StanceUrgency > highestUrgency && o.StanceUrgency >= UrgencyThreshold)
            {
                highestUrgency = o.StanceUrgency;
                winningIntent = o.StanceIntent;
                winner = type;
            }
        }

        return (winningIntent, highestUrgency, winner);
    }

    /// <summary>
    /// Selects the vocalization with highest urgency.
    /// </summary>
    private static (string? intent, float urgency, BehaviorModelType? winner) SelectExclusiveVocalization(
        (BehaviorOutput? output, BehaviorModelType type)[] outputs)
    {
        string? winningIntent = null;
        float highestUrgency = 0f;
        BehaviorModelType? winner = null;

        foreach (var (output, type) in outputs)
        {
            if (output is not { } o)
                continue;

            if (o.VocalizationUrgency > highestUrgency && o.VocalizationUrgency >= UrgencyThreshold)
            {
                highestUrgency = o.VocalizationUrgency;
                winningIntent = o.VocalizationIntent;
                winner = type;
            }
        }

        return (winningIntent, highestUrgency, winner);
    }

#if DEBUG
    /// <summary>
    /// Collects contributions for debugging from all outputs.
    /// </summary>
    private static ChannelContribution[] CollectContributions(
        (BehaviorOutput? output, BehaviorModelType type)[] outputs,
        Func<BehaviorOutput, (string? intent, float urgency)> selector)
    {
        var contributions = new List<ChannelContribution>();

        foreach (var (output, type) in outputs)
        {
            if (output is not { } o)
                continue;

            var (intent, urgency) = selector(o);
            if (urgency >= UrgencyThreshold)
            {
                contributions.Add(new ChannelContribution(type, intent, urgency));
            }
        }

        return contributions.ToArray();
    }

    /// <summary>
    /// Collects locomotion contributions for debugging.
    /// </summary>
    private static ChannelContribution[] CollectLocomotionContributions(
        (BehaviorOutput? output, BehaviorModelType type)[] outputs)
    {
        var contributions = new List<ChannelContribution>();

        foreach (var (output, type) in outputs)
        {
            if (output is not { } o)
                continue;

            if (o.LocomotionUrgency >= UrgencyThreshold)
            {
                contributions.Add(new ChannelContribution(type, o.LocomotionIntent, o.LocomotionUrgency));
            }
        }

        return contributions.ToArray();
    }

    /// <summary>
    /// Collects attention contributions for debugging.
    /// </summary>
    private static ChannelContribution[] CollectAttentionContributions(
        (BehaviorOutput? output, BehaviorModelType type)[] outputs)
    {
        var contributions = new List<ChannelContribution>();

        foreach (var (output, type) in outputs)
        {
            if (output is not { } o)
                continue;

            if (o.AttentionUrgency >= UrgencyThreshold)
            {
                contributions.Add(new ChannelContribution(type, o.AttentionTarget?.ToString(), o.AttentionUrgency));
            }
        }

        return contributions.ToArray();
    }
#endif
}
