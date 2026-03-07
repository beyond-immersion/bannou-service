// =============================================================================
// Intent Channel Factory
// Creates runtime intent channel instances for entities based on their archetype.
// =============================================================================

namespace BeyondImmersion.Bannou.Server.Behavior.Archetypes;

/// <summary>
/// A single Intent emission targeting a specific channel.
/// </summary>
/// <param name="Channel">The logical channel name (e.g., "combat", "movement").</param>
/// <param name="Intent">The intent identifier (e.g., "attack", "walk").</param>
/// <param name="Urgency">Urgency value 0.0-1.0 for merge priority.</param>
/// <param name="Target">Optional target entity ID.</param>
/// <param name="Data">Additional emission data.</param>
public sealed record IntentEmission(
    string Channel,
    string Intent,
    float Urgency,
    Guid? Target = null,
    IReadOnlyDictionary<string, object>? Data = null);

/// <summary>
/// Factory for creating runtime intent channel instances.
/// </summary>
public interface IIntentChannelFactory
{
    /// <summary>
    /// Creates intent channel instances for an entity based on its archetype.
    /// </summary>
    /// <param name="entityId">The entity ID.</param>
    /// <param name="archetype">The archetype defining available channels.</param>
    /// <returns>A set of intent channels for the entity.</returns>
    RuntimeChannelSet CreateChannels(Guid entityId, IArchetypeDefinition archetype);
}

/// <summary>
/// Default implementation of intent channel factory.
/// </summary>
public sealed class IntentChannelFactory : IIntentChannelFactory
{
    /// <inheritdoc/>
    public RuntimeChannelSet CreateChannels(Guid entityId, IArchetypeDefinition archetype)
    {
        var channels = new Dictionary<string, RuntimeChannel>(StringComparer.OrdinalIgnoreCase);

        foreach (var channelDef in archetype.Channels)
        {
            channels[channelDef.Name] = new RuntimeChannel(channelDef.Name, channelDef);
        }

        return new RuntimeChannelSet(entityId, archetype.Id, channels);
    }
}

/// <summary>
/// A set of runtime intent channels for an entity.
/// </summary>
public sealed class RuntimeChannelSet
{
    private readonly Dictionary<string, RuntimeChannel> _channels;

    /// <summary>
    /// Creates a new runtime channel set.
    /// </summary>
    /// <param name="entityId">The entity ID.</param>
    /// <param name="archetypeId">The archetype ID.</param>
    /// <param name="channels">The channel instances.</param>
    public RuntimeChannelSet(
        Guid entityId,
        string archetypeId,
        IReadOnlyDictionary<string, RuntimeChannel> channels)
    {
        EntityId = entityId;
        ArchetypeId = archetypeId;
        _channels = new Dictionary<string, RuntimeChannel>(
            channels,
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Gets the entity ID.
    /// </summary>
    public Guid EntityId { get; }

    /// <summary>
    /// Gets the archetype ID.
    /// </summary>
    public string ArchetypeId { get; }

    /// <summary>
    /// Gets all channels in this set.
    /// </summary>
    public IReadOnlyDictionary<string, RuntimeChannel> Channels => _channels;

    /// <summary>
    /// Gets a channel by name.
    /// </summary>
    /// <param name="name">The channel name.</param>
    /// <returns>The channel, or null if not found.</returns>
    public RuntimeChannel? GetChannel(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        return _channels.GetValueOrDefault(name);
    }

    /// <summary>
    /// Applies an intent emission to the appropriate channel.
    /// </summary>
    /// <param name="emission">The emission to apply.</param>
    /// <returns>True if the emission was applied, false if channel not found.</returns>
    public bool ApplyEmission(IntentEmission emission)
    {
        var channel = GetChannel(emission.Channel);
        if (channel == null)
        {
            return false;
        }

        channel.SetValue(new RuntimeChannelValue(emission.Intent, emission.Urgency, emission.Target, emission.Data));
        return true;
    }

    /// <summary>
    /// Clears all channels.
    /// </summary>
    public void ClearAll()
    {
        foreach (var channel in _channels.Values)
        {
            channel.Clear();
        }
    }
}

/// <summary>
/// A single runtime intent channel instance.
/// </summary>
public sealed class RuntimeChannel
{
    /// <summary>
    /// Creates a new runtime channel.
    /// </summary>
    /// <param name="name">The channel name.</param>
    /// <param name="definition">The channel definition.</param>
    public RuntimeChannel(string name, ILogicalChannelDefinition definition)
    {
        Name = name;
        Definition = definition;
    }

    /// <summary>
    /// Gets the channel name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the channel definition from the archetype.
    /// </summary>
    public ILogicalChannelDefinition Definition { get; }

    /// <summary>
    /// Gets the current value of this channel.
    /// </summary>
    public RuntimeChannelValue? CurrentValue { get; private set; }

    /// <summary>
    /// Gets the timestamp when the value was last set.
    /// </summary>
    public DateTime? LastUpdated { get; private set; }

    /// <summary>
    /// Sets the channel value.
    /// </summary>
    /// <param name="value">The value to set.</param>
    public void SetValue(RuntimeChannelValue value)
    {
        CurrentValue = value;
        LastUpdated = DateTime.UtcNow;
    }

    /// <summary>
    /// Clears the channel value.
    /// </summary>
    public void Clear()
    {
        CurrentValue = null;
        LastUpdated = null;
    }
}

/// <summary>
/// A value stored in a runtime channel.
/// </summary>
/// <param name="Intent">The intent name.</param>
/// <param name="Urgency">The urgency level (0-1).</param>
/// <param name="Target">Optional target entity.</param>
/// <param name="Data">Additional data.</param>
public sealed record RuntimeChannelValue(
    string Intent,
    float Urgency,
    Guid? Target = null,
    IReadOnlyDictionary<string, object>? Data = null);
