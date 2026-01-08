// ═══════════════════════════════════════════════════════════════════════════
// Archetype Interfaces
// Core interfaces for the Entity Archetype system.
// Implementations provided by lib-behavior plugin.
// ═══════════════════════════════════════════════════════════════════════════

namespace BeyondImmersion.BannouService.Behavior;

/// <summary>
/// Merge strategy for combining multiple intent emissions on the same channel.
/// </summary>
public enum MergeStrategy
{
    /// <summary>Highest urgency wins exclusively.</summary>
    Priority,

    /// <summary>Values are blended proportionally by urgency.</summary>
    Blend,

    /// <summary>Values are summed (for additive effects).</summary>
    Additive
}

/// <summary>
/// Physical output channel for entity animation/physics.
/// </summary>
public enum PhysicalChannel
{
    /// <summary>Primary actions (attacks, abilities).</summary>
    Action,

    /// <summary>Movement and navigation.</summary>
    Locomotion,

    /// <summary>Gaze and head tracking.</summary>
    Attention,

    /// <summary>Body posture.</summary>
    Stance,

    /// <summary>Speech and sounds.</summary>
    Vocalization,

    /// <summary>Facial expressions.</summary>
    Expression
}

/// <summary>
/// Definition of a logical channel within an archetype.
/// </summary>
public interface ILogicalChannelDefinition
{
    /// <summary>The logical channel name (e.g., "combat", "movement").</summary>
    string Name { get; }

    /// <summary>The physical channel this maps to.</summary>
    PhysicalChannel PhysicalChannel { get; }

    /// <summary>Default urgency for emissions on this channel.</summary>
    float DefaultUrgency { get; }

    /// <summary>How to merge multiple emissions on this channel.</summary>
    MergeStrategy MergeStrategy { get; }

    /// <summary>Human-readable description.</summary>
    string Description { get; }
}

/// <summary>
/// Definition of an entity archetype (humanoid, vehicle, creature, etc.).
/// </summary>
public interface IArchetypeDefinition
{
    /// <summary>Unique archetype identifier (e.g., "humanoid", "vehicle").</summary>
    string Id { get; }

    /// <summary>Human-readable description.</summary>
    string Description { get; }

    /// <summary>Logical channels available for this archetype.</summary>
    IReadOnlyList<ILogicalChannelDefinition> Channels { get; }

    /// <summary>
    /// Checks if this archetype has a channel with the given name.
    /// </summary>
    /// <param name="channelName">The channel name to check.</param>
    /// <returns>True if the channel exists.</returns>
    bool HasChannel(string channelName);

    /// <summary>
    /// Gets a channel definition by name.
    /// </summary>
    /// <param name="channelName">The channel name.</param>
    /// <returns>The channel definition, or null if not found.</returns>
    ILogicalChannelDefinition? GetChannel(string channelName);
}

/// <summary>
/// Registry of entity archetypes.
/// </summary>
public interface IArchetypeRegistry
{
    /// <summary>
    /// Gets an archetype definition by ID.
    /// </summary>
    /// <param name="archetypeId">The archetype ID.</param>
    /// <returns>The archetype, or null if not found.</returns>
    IArchetypeDefinition? GetArchetype(string archetypeId);

    /// <summary>
    /// Checks if an archetype exists.
    /// </summary>
    /// <param name="archetypeId">The archetype ID to check.</param>
    /// <returns>True if the archetype is registered.</returns>
    bool HasArchetype(string archetypeId);

    /// <summary>
    /// Gets the default archetype (typically "humanoid").
    /// </summary>
    /// <returns>The default archetype.</returns>
    IArchetypeDefinition GetDefaultArchetype();
}
