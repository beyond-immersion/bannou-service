// =============================================================================
// Logical Channel Definition
// Semantic channel configuration for entity archetypes.
// =============================================================================

namespace BeyondImmersion.Bannou.Client.Behavior.Archetypes;

/// <summary>
/// Defines a logical channel for an entity archetype.
/// </summary>
/// <remarks>
/// <para>
/// Logical channels are semantic (e.g., "Combat", "Expression") and map to
/// physical bytecode output slots. Multiple logical channels can map to the
/// same physical channel, with merge strategy determining conflict resolution.
/// </para>
/// </remarks>
/// <param name="Name">The semantic name of the channel (e.g., "combat", "expression").</param>
/// <param name="PhysicalChannel">The bytecode output slot this channel maps to.</param>
/// <param name="DefaultUrgency">Default urgency for outputs on this channel (0.0-1.0).</param>
/// <param name="MergeStrategy">How to resolve conflicts when multiple behaviors output to this channel.</param>
/// <param name="Description">Human-readable description of the channel's purpose.</param>
public sealed record LogicalChannelDefinition(
    string Name,
    PhysicalChannel PhysicalChannel,
    float DefaultUrgency,
    MergeStrategy MergeStrategy,
    string Description) : ILogicalChannelDefinition
{
    /// <summary>
    /// FNV-1a hash of the channel name for fast lookup.
    /// </summary>
    public int NameHash { get; } = ComputeNameHash(Name);

    /// <summary>
    /// Computes FNV-1a hash for a channel name.
    /// </summary>
    private static int ComputeNameHash(string name)
    {
        unchecked
        {
            const uint fnvPrime = 16777619;
            const uint fnvOffset = 2166136261;
            uint hash = fnvOffset;

            foreach (char c in name.ToLowerInvariant())
            {
                hash ^= c;
                hash *= fnvPrime;
            }

            return (int)hash;
        }
    }

    /// <summary>
    /// Creates a channel definition with default values.
    /// </summary>
    /// <param name="name">The channel name.</param>
    /// <param name="physicalChannel">The physical output slot.</param>
    /// <returns>A new channel definition with defaults.</returns>
    public static LogicalChannelDefinition Create(string name, PhysicalChannel physicalChannel)
    {
        return new LogicalChannelDefinition(
            name,
            physicalChannel,
            DefaultUrgency: 0.5f,
            MergeStrategy: MergeStrategy.Priority,
            Description: string.Empty);
    }

    /// <summary>
    /// Creates a copy with modified urgency.
    /// </summary>
    public LogicalChannelDefinition WithUrgency(float urgency)
    {
        return this with { DefaultUrgency = Math.Clamp(urgency, 0f, 1f) };
    }

    /// <summary>
    /// Creates a copy with modified merge strategy.
    /// </summary>
    public LogicalChannelDefinition WithMergeStrategy(MergeStrategy strategy)
    {
        return this with { MergeStrategy = strategy };
    }
}
