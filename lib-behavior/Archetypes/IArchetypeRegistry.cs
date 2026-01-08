// =============================================================================
// Archetype Registry Interface
// Service for managing entity archetype definitions.
// =============================================================================

namespace BeyondImmersion.BannouService.Behavior.Archetypes;

/// <summary>
/// Registry for entity archetype definitions.
/// </summary>
/// <remarks>
/// <para>
/// Provides lookup and management of archetype definitions that specify
/// how different entity types configure their Intent Channels.
/// </para>
/// <para>
/// Standard archetypes are registered by default: humanoid, vehicle,
/// creature, object, environmental. Custom archetypes can be added.
/// </para>
/// </remarks>
public interface IArchetypeRegistry
{
    /// <summary>
    /// Gets an archetype definition by ID.
    /// </summary>
    /// <param name="archetypeId">The archetype identifier (e.g., "humanoid").</param>
    /// <returns>The archetype definition, or null if not found.</returns>
    ArchetypeDefinition? GetArchetype(string archetypeId);

    /// <summary>
    /// Gets an archetype definition by ID hash.
    /// </summary>
    /// <param name="archetypeIdHash">The FNV-1a hash of the archetype ID.</param>
    /// <returns>The archetype definition, or null if not found.</returns>
    ArchetypeDefinition? GetArchetypeByHash(int archetypeIdHash);

    /// <summary>
    /// Gets all registered archetype IDs.
    /// </summary>
    /// <returns>Collection of archetype identifiers.</returns>
    IReadOnlyCollection<string> GetArchetypeIds();

    /// <summary>
    /// Gets all registered archetype definitions.
    /// </summary>
    /// <returns>Collection of archetype definitions.</returns>
    IReadOnlyCollection<ArchetypeDefinition> GetAllArchetypes();

    /// <summary>
    /// Registers a new archetype definition.
    /// </summary>
    /// <param name="archetype">The archetype to register.</param>
    /// <exception cref="ArgumentException">If an archetype with the same ID already exists.</exception>
    void RegisterArchetype(ArchetypeDefinition archetype);

    /// <summary>
    /// Checks if an archetype with the given ID is registered.
    /// </summary>
    /// <param name="archetypeId">The archetype identifier.</param>
    /// <returns>True if the archetype is registered.</returns>
    bool HasArchetype(string archetypeId);

    /// <summary>
    /// Gets the default archetype for entities without explicit archetype assignment.
    /// </summary>
    /// <returns>The default archetype (typically "humanoid").</returns>
    ArchetypeDefinition GetDefaultArchetype();
}
