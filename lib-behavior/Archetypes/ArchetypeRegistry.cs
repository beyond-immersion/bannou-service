// =============================================================================
// Archetype Registry
// Manages entity archetype definitions with standard archetypes pre-registered.
// =============================================================================

using System.Collections.Concurrent;
using BeyondImmersion.BannouService.Behavior;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Behavior.Archetypes;

/// <summary>
/// Thread-safe registry for entity archetype definitions.
/// </summary>
/// <remarks>
/// <para>
/// Automatically registers standard archetypes on construction:
/// humanoid, vehicle, creature, object, environmental.
/// </para>
/// <para>
/// Custom archetypes can be added at runtime via <see cref="RegisterArchetype"/>.
/// </para>
/// </remarks>
public sealed class ArchetypeRegistry : IArchetypeRegistry
{
    private readonly ConcurrentDictionary<string, ArchetypeDefinition> _archetypesById;
    private readonly ConcurrentDictionary<int, ArchetypeDefinition> _archetypesByHash;
    private readonly ILogger<ArchetypeRegistry>? _logger;
    private readonly ArchetypeDefinition _defaultArchetype;

    /// <summary>
    /// Creates a new archetype registry with standard archetypes pre-registered.
    /// </summary>
    /// <param name="logger">Optional logger for diagnostic output.</param>
    public ArchetypeRegistry(ILogger<ArchetypeRegistry>? logger = null)
    {
        _logger = logger;
        _archetypesById = new ConcurrentDictionary<string, ArchetypeDefinition>(StringComparer.OrdinalIgnoreCase);
        _archetypesByHash = new ConcurrentDictionary<int, ArchetypeDefinition>();

        // Register standard archetypes
        var humanoid = ArchetypeDefinition.CreateHumanoid();
        var vehicle = ArchetypeDefinition.CreateVehicle();
        var creature = ArchetypeDefinition.CreateCreature();
        var obj = ArchetypeDefinition.CreateObject();
        var environmental = ArchetypeDefinition.CreateEnvironmental();

        RegisterArchetypeInternal(humanoid);
        RegisterArchetypeInternal(vehicle);
        RegisterArchetypeInternal(creature);
        RegisterArchetypeInternal(obj);
        RegisterArchetypeInternal(environmental);

        _defaultArchetype = humanoid;

        _logger?.LogInformation(
            "Archetype registry initialized with {Count} standard archetypes",
            _archetypesById.Count);
    }

    /// <inheritdoc/>
    public IArchetypeDefinition? GetArchetype(string archetypeId)
    {
        if (string.IsNullOrEmpty(archetypeId))
        {
            return null;
        }

        return _archetypesById.GetValueOrDefault(archetypeId);
    }

    /// <inheritdoc/>
    public IArchetypeDefinition? GetArchetypeByHash(int archetypeIdHash)
    {
        return _archetypesByHash.GetValueOrDefault(archetypeIdHash);
    }

    /// <inheritdoc/>
    public IReadOnlyCollection<string> GetArchetypeIds()
    {
        return _archetypesById.Keys.ToList().AsReadOnly();
    }

    /// <inheritdoc/>
    public IReadOnlyCollection<IArchetypeDefinition> GetAllArchetypes()
    {
        return _archetypesById.Values.Cast<IArchetypeDefinition>().ToList().AsReadOnly();
    }

    /// <inheritdoc/>
    public void RegisterArchetype(IArchetypeDefinition archetype)
    {
        ArgumentNullException.ThrowIfNull(archetype);

        if (string.IsNullOrEmpty(archetype.Id))
        {
            throw new ArgumentException("Archetype ID cannot be null or empty", nameof(archetype));
        }

        if (_archetypesById.ContainsKey(archetype.Id))
        {
            throw new ArgumentException($"Archetype with ID '{archetype.Id}' is already registered", nameof(archetype));
        }

        if (archetype is not ArchetypeDefinition concreteArchetype)
        {
            throw new ArgumentException(
                $"Archetype must be an {nameof(ArchetypeDefinition)} instance",
                nameof(archetype));
        }

        RegisterArchetypeInternal(concreteArchetype);

        _logger?.LogInformation(
            "Registered custom archetype {ArchetypeId} with {ChannelCount} channels",
            archetype.Id,
            concreteArchetype.Channels.Count);
    }

    /// <inheritdoc/>
    public bool HasArchetype(string archetypeId)
    {
        if (string.IsNullOrEmpty(archetypeId))
        {
            return false;
        }

        return _archetypesById.ContainsKey(archetypeId);
    }

    /// <inheritdoc/>
    public IArchetypeDefinition GetDefaultArchetype()
    {
        return _defaultArchetype;
    }

    /// <summary>
    /// Internal registration without validation for pre-built archetypes.
    /// </summary>
    private void RegisterArchetypeInternal(ArchetypeDefinition archetype)
    {
        var hash = ComputeIdHash(archetype.Id);

        _archetypesById[archetype.Id] = archetype;
        _archetypesByHash[hash] = archetype;
    }

    /// <summary>
    /// Computes FNV-1a hash for an archetype ID.
    /// </summary>
    private static int ComputeIdHash(string id)
    {
        unchecked
        {
            const uint fnvPrime = 16777619;
            const uint fnvOffset = 2166136261;
            uint hash = fnvOffset;

            foreach (char c in id.ToLowerInvariant())
            {
                hash ^= c;
                hash *= fnvPrime;
            }

            return (int)hash;
        }
    }
}
