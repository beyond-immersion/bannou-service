// =============================================================================
// Archetype Definition
// Entity type configuration for Intent Channel structure.
// =============================================================================

using BeyondImmersion.Bannou.Server.Behavior.Intent;

namespace BeyondImmersion.Bannou.Server.Behavior.Archetypes;

/// <summary>
/// Defines an entity archetype with its channel configuration.
/// </summary>
/// <remarks>
/// <para>
/// Archetypes are data-driven definitions that specify:
/// - Which logical channels the entity type uses
/// - How those channels map to physical bytecode slots
/// - Which behavior model types are relevant
/// </para>
/// <para>
/// Standard archetypes: humanoid, vehicle, creature, object, environmental
/// </para>
/// </remarks>
public sealed class ArchetypeDefinition : IArchetypeDefinition
{
    /// <summary>
    /// Unique identifier for this archetype (e.g., "humanoid", "vehicle").
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Human-readable description of the archetype.
    /// </summary>
    public required string Description { get; init; }

    /// <summary>
    /// Behavior model types available for this archetype.
    /// </summary>
    public required IReadOnlyList<BehaviorModelType> ModelTypes { get; init; }

    /// <summary>
    /// Logical channel definitions for this archetype.
    /// </summary>
    public required IReadOnlyList<LogicalChannelDefinition> Channels { get; init; }

    /// <summary>
    /// Gets channels as interface type for IArchetypeDefinition implementation.
    /// </summary>
    IReadOnlyList<ILogicalChannelDefinition> IArchetypeDefinition.Channels => _channelInterfaces ??= Channels.Cast<ILogicalChannelDefinition>().ToList();

    private IReadOnlyList<ILogicalChannelDefinition>? _channelInterfaces;

    /// <summary>
    /// FNV-1a hash of the archetype ID for fast lookup.
    /// </summary>
    public int IdHash { get; }

    private readonly Dictionary<string, LogicalChannelDefinition> _channelsByName;
    private readonly Dictionary<int, LogicalChannelDefinition> _channelsByHash;
    private readonly Dictionary<PhysicalChannel, List<LogicalChannelDefinition>> _channelsByPhysical;

    /// <summary>
    /// Creates a new archetype definition.
    /// </summary>
    public ArchetypeDefinition()
    {
        Id = string.Empty;
        Description = string.Empty;
        ModelTypes = Array.Empty<BehaviorModelType>();
        Channels = Array.Empty<LogicalChannelDefinition>();
        _channelsByName = new Dictionary<string, LogicalChannelDefinition>(StringComparer.OrdinalIgnoreCase);
        _channelsByHash = new Dictionary<int, LogicalChannelDefinition>();
        _channelsByPhysical = new Dictionary<PhysicalChannel, List<LogicalChannelDefinition>>();
        IdHash = 0;
    }

    /// <summary>
    /// Initializes lookup dictionaries after properties are set.
    /// Call this after construction via object initializer.
    /// </summary>
    public ArchetypeDefinition Initialize()
    {
        _channelsByName.Clear();
        _channelsByHash.Clear();
        _channelsByPhysical.Clear();
        _channelInterfaces = null;

        foreach (var channel in Channels)
        {
            _channelsByName[channel.Name] = channel;
            _channelsByHash[channel.NameHash] = channel;

            if (!_channelsByPhysical.TryGetValue(channel.PhysicalChannel, out var list))
            {
                list = new List<LogicalChannelDefinition>();
                _channelsByPhysical[channel.PhysicalChannel] = list;
            }

            list.Add(channel);
        }

        return this;
    }

    /// <summary>
    /// Gets a logical channel by name.
    /// </summary>
    /// <param name="name">The channel name (case-insensitive).</param>
    /// <returns>The channel definition, or null if not found.</returns>
    public LogicalChannelDefinition? GetChannel(string name)
    {
        return _channelsByName.GetValueOrDefault(name);
    }

    /// <summary>
    /// Gets a channel as interface type for IArchetypeDefinition implementation.
    /// </summary>
    ILogicalChannelDefinition? IArchetypeDefinition.GetChannel(string channelName)
    {
        return GetChannel(channelName);
    }

    /// <summary>
    /// Gets a logical channel by name hash.
    /// </summary>
    /// <param name="nameHash">The FNV-1a hash of the channel name.</param>
    /// <returns>The channel definition, or null if not found.</returns>
    public LogicalChannelDefinition? GetChannelByHash(int nameHash)
    {
        return _channelsByHash.GetValueOrDefault(nameHash);
    }

    /// <summary>
    /// Gets all logical channels that map to a physical channel.
    /// </summary>
    /// <param name="physical">The physical channel.</param>
    /// <returns>List of logical channels, empty if none.</returns>
    public IReadOnlyList<LogicalChannelDefinition> GetChannelsForPhysical(PhysicalChannel physical)
    {
        return _channelsByPhysical.TryGetValue(physical, out var list)
            ? list.AsReadOnly()
            : Array.Empty<LogicalChannelDefinition>();
    }

    /// <summary>
    /// Checks if this archetype supports a behavior model type.
    /// </summary>
    /// <param name="modelType">The model type to check.</param>
    /// <returns>True if the model type is supported.</returns>
    public bool SupportsModelType(BehaviorModelType modelType)
    {
        return ModelTypes.Contains(modelType);
    }

    /// <summary>
    /// Checks if this archetype has a channel with the given name.
    /// </summary>
    /// <param name="channelName">The channel name (case-insensitive).</param>
    /// <returns>True if the channel exists.</returns>
    public bool HasChannel(string channelName)
    {
        return _channelsByName.ContainsKey(channelName);
    }

    /// <summary>
    /// Gets the physical channels used by this archetype.
    /// </summary>
    /// <returns>Set of physical channels.</returns>
    public IReadOnlySet<PhysicalChannel> GetUsedPhysicalChannels()
    {
        return _channelsByPhysical.Keys.ToHashSet();
    }

    /// <summary>
    /// Creates the standard Humanoid archetype.
    /// </summary>
    public static ArchetypeDefinition CreateHumanoid()
    {
        return new ArchetypeDefinition
        {
            Id = "humanoid",
            Description = "Standard humanoid entity with full expression capability",
            ModelTypes = new[] { BehaviorModelType.Combat, BehaviorModelType.Movement, BehaviorModelType.Interaction, BehaviorModelType.Idle },
            Channels = new[]
            {
                new LogicalChannelDefinition("combat", PhysicalChannel.Action, 0.5f, MergeStrategy.Priority, "Attack, defend, and ability actions"),
                new LogicalChannelDefinition("movement", PhysicalChannel.Locomotion, 0.5f, MergeStrategy.Blend, "Navigation, pathfinding, steering"),
                new LogicalChannelDefinition("interaction", PhysicalChannel.Action, 0.4f, MergeStrategy.Priority, "Pick up, talk to, use objects"),
                new LogicalChannelDefinition("expression", PhysicalChannel.Expression, 0.4f, MergeStrategy.Blend, "Facial emotions - smile, frown, surprise"),
                new LogicalChannelDefinition("attention", PhysicalChannel.Attention, 0.6f, MergeStrategy.Blend, "Gaze direction, target tracking"),
                new LogicalChannelDefinition("speech", PhysicalChannel.Vocalization, 0.5f, MergeStrategy.Priority, "Vocalization - talk, shout, whisper"),
                new LogicalChannelDefinition("stance", PhysicalChannel.Stance, 0.3f, MergeStrategy.Blend, "Body posture - crouch, ready, relaxed"),
            }
        }.Initialize();
    }

    /// <summary>
    /// Creates the standard Vehicle archetype.
    /// </summary>
    public static ArchetypeDefinition CreateVehicle()
    {
        return new ArchetypeDefinition
        {
            Id = "vehicle",
            Description = "Vehicles and mounts with movement controls",
            ModelTypes = new[] { BehaviorModelType.Movement, BehaviorModelType.Interaction },
            Channels = new[]
            {
                new LogicalChannelDefinition("throttle", PhysicalChannel.Locomotion, 0.5f, MergeStrategy.Blend, "Acceleration and braking"),
                new LogicalChannelDefinition("steering", PhysicalChannel.Locomotion, 0.5f, MergeStrategy.Blend, "Turn direction and course"),
                new LogicalChannelDefinition("signals", PhysicalChannel.Vocalization, 0.3f, MergeStrategy.Priority, "Horn, lights, indicators"),
                new LogicalChannelDefinition("systems", PhysicalChannel.Action, 0.4f, MergeStrategy.Priority, "Weapons, shields, cargo, special systems"),
            }
        }.Initialize();
    }

    /// <summary>
    /// Creates the standard Creature archetype.
    /// </summary>
    public static ArchetypeDefinition CreateCreature()
    {
        return new ArchetypeDefinition
        {
            Id = "creature",
            Description = "Non-humanoid creatures with simplified behavior",
            ModelTypes = new[] { BehaviorModelType.Combat, BehaviorModelType.Movement, BehaviorModelType.Idle },
            Channels = new[]
            {
                new LogicalChannelDefinition("locomotion", PhysicalChannel.Locomotion, 0.5f, MergeStrategy.Blend, "Walk, run, fly, swim, burrow"),
                new LogicalChannelDefinition("action", PhysicalChannel.Action, 0.5f, MergeStrategy.Priority, "Hunt, flee, forage, attack, rest"),
                new LogicalChannelDefinition("social", PhysicalChannel.Vocalization, 0.4f, MergeStrategy.Priority, "Pack signals, danger calls"),
                new LogicalChannelDefinition("alert", PhysicalChannel.Stance, 0.6f, MergeStrategy.Priority, "Vigilant, relaxed, alarmed"),
            }
        }.Initialize();
    }

    /// <summary>
    /// Creates the standard Object archetype.
    /// </summary>
    public static ArchetypeDefinition CreateObject()
    {
        return new ArchetypeDefinition
        {
            Id = "object",
            Description = "Interactive objects with state machines",
            ModelTypes = new[] { BehaviorModelType.Interaction },
            Channels = new[]
            {
                new LogicalChannelDefinition("state", PhysicalChannel.Action, 0.8f, MergeStrategy.Priority, "Open, closed, locked, triggered"),
                new LogicalChannelDefinition("timing", PhysicalChannel.Stance, 0.5f, MergeStrategy.Priority, "Delay, cycle, hold durations"),
                new LogicalChannelDefinition("feedback", PhysicalChannel.Vocalization, 0.3f, MergeStrategy.Priority, "Visual/audio feedback - ready, warning, confirm"),
            }
        }.Initialize();
    }

    /// <summary>
    /// Creates the standard Environmental archetype.
    /// </summary>
    public static ArchetypeDefinition CreateEnvironmental()
    {
        return new ArchetypeDefinition
        {
            Id = "environmental",
            Description = "Environmental effects with mood control",
            ModelTypes = new[] { BehaviorModelType.Idle },
            Channels = new[]
            {
                new LogicalChannelDefinition("intensity", PhysicalChannel.Action, 0.5f, MergeStrategy.Blend, "Light, moderate, heavy, extreme"),
                new LogicalChannelDefinition("type", PhysicalChannel.Stance, 0.5f, MergeStrategy.Priority, "Rain, snow, fog, clear"),
                new LogicalChannelDefinition("direction", PhysicalChannel.Locomotion, 0.3f, MergeStrategy.Blend, "Wind direction, effect source"),
                new LogicalChannelDefinition("mood", PhysicalChannel.Attention, 0.4f, MergeStrategy.Blend, "Ominous, peaceful, tense, celebratory"),
            }
        }.Initialize();
    }
}
