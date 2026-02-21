// =============================================================================
// Intent Stack Merger
// Merges intent contributions from multiple behavior layers per channel.
// =============================================================================

using BeyondImmersion.Bannou.BehaviorCompiler.Archetypes;
using BeyondImmersion.BannouService.Behavior;
using Microsoft.Extensions.Logging;
using System.Numerics;

namespace BeyondImmersion.BannouService.Behavior.Stack;

/// <summary>
/// Default implementation of intent stack merger.
/// </summary>
/// <remarks>
/// <para>
/// The merger handles three strategies based on channel configuration:
/// </para>
/// <list type="bullet">
/// <item><description>Priority - Highest effective priority wins exclusively</description></item>
/// <item><description>Blend - Values are weighted by urgency and blended</description></item>
/// <item><description>Additive - Values are summed (urgency is summed and clamped)</description></item>
/// </list>
/// </remarks>
public sealed class IntentStackMerger : IIntentStackMerger
{
    /// <summary>
    /// Minimum urgency threshold. Contributions below this are ignored.
    /// </summary>
    public const float UrgencyThreshold = 0.001f;

    private readonly ILogger<IntentStackMerger>? _logger;

    /// <summary>
    /// Creates a new intent stack merger.
    /// </summary>
    /// <param name="logger">Optional logger.</param>
    public IntentStackMerger(ILogger<IntentStackMerger>? logger = null)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    public IntentEmission? MergeChannel(
        string channelName,
        IReadOnlyList<IntentContribution> contributions,
        ILogicalChannelDefinition channelDef)
    {

        // Filter to only contributions above threshold
        var validContributions = contributions
            .Where(c => c.Emission.Urgency >= UrgencyThreshold)
            .ToList();

        if (validContributions.Count == 0)
        {
            return null;
        }

        return channelDef.MergeStrategy switch
        {
            MergeStrategy.Priority => MergePriority(channelName, validContributions),
            MergeStrategy.Blend => MergeBlend(channelName, validContributions),
            MergeStrategy.Additive => MergeAdditive(channelName, validContributions),
            _ => MergePriority(channelName, validContributions) // Default to priority
        };
    }

    /// <inheritdoc/>
    public Dictionary<string, IntentEmission> MergeAll(
        IReadOnlyList<IntentContribution> contributions,
        IArchetypeDefinition archetype)
    {

        var result = new Dictionary<string, IntentEmission>(StringComparer.OrdinalIgnoreCase);

        // Group contributions by channel
        var byChannel = contributions
            .GroupBy(c => c.Emission.Channel, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        foreach (var kvp in byChannel)
        {
            var channelName = kvp.Key;
            var channelContributions = kvp.Value;

            // Get channel definition for merge strategy
            var channelDef = archetype.GetChannel(channelName);
            if (channelDef == null)
            {
                _logger?.LogWarning(
                    "No channel definition found for '{ChannelName}' in archetype '{ArchetypeId}', using Priority strategy",
                    channelName,
                    archetype.Id);

                // Use priority as default for unknown channels
                var merged = MergePriority(channelName, channelContributions);
                if (merged != null)
                {
                    result[channelName] = merged;
                }
                continue;
            }

            var mergedEmission = MergeChannel(channelName, channelContributions, channelDef);
            if (mergedEmission != null)
            {
                result[channelName] = mergedEmission;
            }
        }

        return result;
    }

    /// <summary>
    /// Priority merge: highest effective priority wins.
    /// </summary>
    private IntentEmission? MergePriority(
        string channelName,
        IReadOnlyList<IntentContribution> contributions)
    {
        if (contributions.Count == 0)
        {
            return null;
        }

        // Sort by effective priority (category * 1000 + layer priority), then by urgency
        var winner = contributions
            .OrderByDescending(c => c.EffectivePriority)
            .ThenByDescending(c => c.Emission.Urgency)
            .First();

        return winner.Emission;
    }

    /// <summary>
    /// Blend merge: values weighted by urgency.
    /// </summary>
    private IntentEmission? MergeBlend(
        string channelName,
        IReadOnlyList<IntentContribution> contributions)
    {
        if (contributions.Count == 0)
        {
            return null;
        }

        // If only one contribution, use it directly
        if (contributions.Count == 1)
        {
            return contributions[0].Emission;
        }

        // Calculate total urgency for weight normalization
        var totalUrgency = contributions.Sum(c => c.Emission.Urgency);
        if (totalUrgency < UrgencyThreshold)
        {
            return null;
        }

        // Use highest-priority contribution's intent name (can't blend strings)
        var primaryContribution = contributions
            .OrderByDescending(c => c.EffectivePriority)
            .ThenByDescending(c => c.Emission.Urgency)
            .First();

        // Blend urgency (average weighted by contribution count, clamped to 1.0)
        var blendedUrgency = Math.Min(totalUrgency / contributions.Count * 1.5f, 1.0f);

        // Blend targets if present
        var target = BlendTargets(contributions);

        // Blend position data if present
        var blendedData = BlendData(contributions, totalUrgency);

        return new IntentEmission(
            channelName,
            primaryContribution.Emission.Intent,
            blendedUrgency,
            target,
            blendedData);
    }

    /// <summary>
    /// Additive merge: sum values.
    /// </summary>
    private IntentEmission? MergeAdditive(
        string channelName,
        IReadOnlyList<IntentContribution> contributions)
    {
        if (contributions.Count == 0)
        {
            return null;
        }

        // Sum urgency (clamped to 1.0)
        var summedUrgency = Math.Min(contributions.Sum(c => c.Emission.Urgency), 1.0f);

        // Use highest-priority intent name
        var primaryContribution = contributions
            .OrderByDescending(c => c.EffectivePriority)
            .ThenByDescending(c => c.Emission.Urgency)
            .First();

        // For additive, prefer the highest-priority target
        var target = primaryContribution.Emission.Target;

        return new IntentEmission(
            channelName,
            primaryContribution.Emission.Intent,
            summedUrgency,
            target,
            primaryContribution.Emission.Data);
    }

    /// <summary>
    /// Blends targets weighted by urgency. Returns highest-urgency target.
    /// </summary>
    private static Guid? BlendTargets(IReadOnlyList<IntentContribution> contributions)
    {
        // GUIDs can't be mathematically blended, so return highest urgency target
        return contributions
            .Where(c => c.Emission.Target.HasValue)
            .OrderByDescending(c => c.Emission.Urgency)
            .Select(c => c.Emission.Target)
            .FirstOrDefault();
    }

    /// <summary>
    /// Blends data dictionaries, with special handling for Vector3 target positions.
    /// </summary>
    private static IReadOnlyDictionary<string, object>? BlendData(
        IReadOnlyList<IntentContribution> contributions,
        float totalUrgency)
    {
        // Collect all data keys
        var allKeys = contributions
            .Where(c => c.Emission.Data != null)
            .SelectMany(c => c.Emission.Data!.Keys)
            .Distinct()
            .ToList();

        if (allKeys.Count == 0)
        {
            return null;
        }

        var blendedData = new Dictionary<string, object>();

        foreach (var key in allKeys)
        {
            // Special case: blend Vector3 values for "TargetPosition"
            if (key.Equals("TargetPosition", StringComparison.OrdinalIgnoreCase))
            {
                var blendedPosition = BlendVector3(contributions, key, totalUrgency);
                if (blendedPosition.HasValue)
                {
                    blendedData[key] = blendedPosition.Value;
                }
                continue;
            }

            // For other keys, take value from highest-urgency contributor
            var value = contributions
                .Where(c => c.Emission.Data != null && c.Emission.Data.ContainsKey(key))
                .OrderByDescending(c => c.Emission.Urgency)
                .Select(c => c.Emission.Data![key])
                .FirstOrDefault();

            if (value != null)
            {
                blendedData[key] = value;
            }
        }

        return blendedData.Count > 0 ? blendedData : null;
    }

    /// <summary>
    /// Blends Vector3 values weighted by urgency.
    /// </summary>
    private static Vector3? BlendVector3(
        IReadOnlyList<IntentContribution> contributions,
        string key,
        float totalUrgency)
    {
        var vectorContributions = contributions
            .Where(c => c.Emission.Data != null &&
                        c.Emission.Data.TryGetValue(key, out var v) &&
                        v is Vector3)
            .Select(c => (
                vector: (Vector3)c.Emission.Data![key],
                weight: c.Emission.Urgency / totalUrgency))
            .ToList();

        if (vectorContributions.Count == 0)
        {
            return null;
        }

        var blended = Vector3.Zero;
        foreach (var (vector, weight) in vectorContributions)
        {
            blended += vector * weight;
        }

        return blended;
    }
}
