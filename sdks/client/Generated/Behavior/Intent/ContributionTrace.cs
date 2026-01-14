// =============================================================================
// Contribution Trace
// Debug support for understanding merge decisions.
// =============================================================================

using BeyondImmersion.Bannou.Client.Behavior.Runtime;

namespace BeyondImmersion.Bannou.Client.Behavior.Intent;

/// <summary>
/// A single contribution from a behavior model to a channel.
/// </summary>
/// <param name="Source">The behavior model type that contributed.</param>
/// <param name="Intent">The intent string (or target for attention).</param>
/// <param name="Urgency">The urgency value [0.0 - 1.0].</param>
public readonly record struct ChannelContribution(
    BehaviorModelType Source,
    string? Intent,
    float Urgency);

/// <summary>
/// Resolution result for a single channel.
/// </summary>
/// <param name="Channel">The intent channel.</param>
/// <param name="Winner">The winning behavior model type (null for blended channels).</param>
/// <param name="WinningUrgency">The winning/combined urgency value.</param>
/// <param name="AllContributions">All contributions to this channel.</param>
public readonly record struct ChannelResolution(
    IntentChannel Channel,
    BehaviorModelType? Winner,
    float WinningUrgency,
    ChannelContribution[] AllContributions);

#if DEBUG
/// <summary>
/// Debug trace showing which model contributed to each channel.
/// Only available in DEBUG builds to avoid allocation in release.
/// </summary>
/// <remarks>
/// <para>
/// This trace helps understand why a particular action was chosen.
/// For example, if Combat model's "attack" was chosen over Interaction
/// model's "talk", the trace shows both contributions and their urgencies.
/// </para>
/// <para>
/// Usage in game debugger:
/// <code>
/// var merged = evaluator.EvaluateCharacter(characterId, inputs);
/// if (merged.Trace != null)
/// {
///     foreach (var resolution in merged.Trace.Channels)
///     {
///         Debug.Log($"{resolution.Channel}: Winner={resolution.Winner}");
///         foreach (var contrib in resolution.AllContributions)
///         {
///             Debug.Log($"  {contrib.Source}: {contrib.Intent} ({contrib.Urgency:F2})");
///         }
///     }
/// }
/// </code>
/// </para>
/// </remarks>
public sealed class ContributionTrace
{
    private readonly List<ChannelResolution> _resolutions = new(5);

    /// <summary>
    /// Timestamp when this trace was created.
    /// </summary>
    public DateTime Timestamp { get; } = DateTime.UtcNow;

    /// <summary>
    /// All channel resolutions in this trace.
    /// </summary>
    public IReadOnlyList<ChannelResolution> Channels => _resolutions;

    /// <summary>
    /// Adds a channel resolution to the trace.
    /// </summary>
    /// <param name="channel">The intent channel.</param>
    /// <param name="winner">The winning model type (null for blended).</param>
    /// <param name="winningUrgency">The winning/combined urgency.</param>
    /// <param name="contributions">All contributions to this channel.</param>
    internal void AddResolution(
        IntentChannel channel,
        BehaviorModelType? winner,
        float winningUrgency,
        ChannelContribution[] contributions)
    {
        _resolutions.Add(new ChannelResolution(channel, winner, winningUrgency, contributions));
    }

    /// <summary>
    /// Gets the resolution for a specific channel.
    /// </summary>
    /// <param name="channel">The intent channel to look up.</param>
    /// <returns>The resolution for that channel, or null if not found.</returns>
    public ChannelResolution? GetResolution(IntentChannel channel)
    {
        foreach (var resolution in _resolutions)
        {
            if (resolution.Channel == channel)
                return resolution;
        }
        return null;
    }

    /// <summary>
    /// Gets whether a specific model contributed to any channel.
    /// </summary>
    /// <param name="modelType">The behavior model type to check.</param>
    /// <returns>True if the model contributed to at least one channel.</returns>
    public bool HasContributionFrom(BehaviorModelType modelType)
    {
        foreach (var resolution in _resolutions)
        {
            foreach (var contribution in resolution.AllContributions)
            {
                if (contribution.Source == modelType)
                    return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Gets all channels where the specified model won.
    /// </summary>
    /// <param name="modelType">The behavior model type to check.</param>
    /// <returns>Channels won by this model.</returns>
    public IEnumerable<IntentChannel> GetChannelsWonBy(BehaviorModelType modelType)
    {
        foreach (var resolution in _resolutions)
        {
            if (resolution.Winner == modelType)
                yield return resolution.Channel;
        }
    }

    /// <summary>
    /// Formats the trace as a human-readable string.
    /// </summary>
    public override string ToString()
    {
        var lines = new List<string>
        {
            $"ContributionTrace @ {Timestamp:HH:mm:ss.fff}",
        };

        foreach (var resolution in _resolutions)
        {
            var winnerStr = resolution.Winner.HasValue
                ? resolution.Winner.Value.ToString()
                : "(blended)";
            lines.Add($"  {resolution.Channel}: Winner={winnerStr}, Urgency={resolution.WinningUrgency:F2}");

            foreach (var contrib in resolution.AllContributions)
            {
                var marker = contrib.Source == resolution.Winner ? "*" : " ";
                lines.Add($"    {marker} {contrib.Source}: {contrib.Intent ?? "(null)"} ({contrib.Urgency:F2})");
            }
        }

        return string.Join(Environment.NewLine, lines);
    }
}
#endif
