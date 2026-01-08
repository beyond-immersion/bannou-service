// =============================================================================
// Entity Resolver Interface
// Resolves semantic binding names (hero, villain) to entity IDs in cutscenes.
// =============================================================================

using System.Numerics;

namespace BeyondImmersion.BannouService.Behavior;

/// <summary>
/// Resolves semantic binding names to entity references within cutscene contexts.
/// </summary>
/// <remarks>
/// <para>
/// In ABML cutscenes, channels use semantic names like "hero" or "villain" rather
/// than entity IDs. The entity resolver translates these names to actual entity
/// references using the cutscene's bindings.
/// </para>
/// <para>
/// Resolution sources (in priority order):
/// </para>
/// <list type="number">
/// <item>Explicit bindings provided at cutscene start</item>
/// <item>Role-based resolution from participants</item>
/// <item>Expression context for dynamic bindings</item>
/// </list>
/// </remarks>
public interface IEntityResolver
{
    /// <summary>
    /// Resolves a binding name to an entity reference.
    /// </summary>
    /// <param name="bindingName">The semantic name (e.g., "hero", "villain", "target").</param>
    /// <param name="bindings">The cutscene's entity bindings.</param>
    /// <param name="context">Optional expression context for dynamic resolution.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The resolved entity reference, or null if not found.</returns>
    Task<EntityReference?> ResolveAsync(
        string bindingName,
        CutsceneBindings bindings,
        EntityResolutionContext? context = null,
        CancellationToken ct = default);

    /// <summary>
    /// Resolves multiple binding names at once.
    /// </summary>
    /// <param name="bindingNames">The semantic names to resolve.</param>
    /// <param name="bindings">The cutscene's entity bindings.</param>
    /// <param name="context">Optional expression context for dynamic resolution.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Dictionary of resolved references (missing bindings omitted).</returns>
    Task<IReadOnlyDictionary<string, EntityReference>> ResolveManyAsync(
        IEnumerable<string> bindingNames,
        CutsceneBindings bindings,
        EntityResolutionContext? context = null,
        CancellationToken ct = default);

    /// <summary>
    /// Checks if a binding name can be resolved.
    /// </summary>
    /// <param name="bindingName">The semantic name to check.</param>
    /// <param name="bindings">The cutscene's entity bindings.</param>
    /// <returns>True if the binding can be resolved.</returns>
    bool CanResolve(string bindingName, CutsceneBindings bindings);
}

/// <summary>
/// Reference to a resolved entity.
/// </summary>
/// <remarks>
/// Contains the entity ID and optional metadata about the binding,
/// such as the archetype and role information.
/// </remarks>
public sealed class EntityReference
{
    /// <summary>
    /// The resolved entity ID.
    /// </summary>
    public required Guid EntityId { get; init; }

    /// <summary>
    /// The archetype ID of the entity (if known).
    /// </summary>
    public string? ArchetypeId { get; init; }

    /// <summary>
    /// The role this entity plays in the cutscene (e.g., "protagonist", "antagonist").
    /// </summary>
    public string? Role { get; init; }

    /// <summary>
    /// Whether this is a player-controlled entity.
    /// </summary>
    public bool IsPlayer { get; init; }

    /// <summary>
    /// Whether this entity is a prop (non-character).
    /// </summary>
    public bool IsProp { get; init; }

    /// <summary>
    /// Additional metadata about the entity.
    /// </summary>
    public IReadOnlyDictionary<string, object>? Metadata { get; init; }

    /// <summary>
    /// Creates a simple entity reference.
    /// </summary>
    public static EntityReference FromId(Guid entityId) => new() { EntityId = entityId };

    /// <summary>
    /// Creates a player entity reference.
    /// </summary>
    public static EntityReference Player(Guid entityId, string? archetypeId = null) => new()
    {
        EntityId = entityId,
        ArchetypeId = archetypeId,
        IsPlayer = true
    };

    /// <summary>
    /// Creates an NPC entity reference.
    /// </summary>
    public static EntityReference Npc(Guid entityId, string archetypeId, string? role = null) => new()
    {
        EntityId = entityId,
        ArchetypeId = archetypeId,
        Role = role
    };

    /// <summary>
    /// Creates a prop entity reference.
    /// </summary>
    public static EntityReference Prop(Guid entityId, string? archetypeId = null) => new()
    {
        EntityId = entityId,
        ArchetypeId = archetypeId,
        IsProp = true
    };
}

/// <summary>
/// Entity bindings for a cutscene, mapping semantic names to entity references.
/// </summary>
/// <remarks>
/// <para>
/// Bindings are provided when a cutscene starts to establish which entities
/// fill which roles. This allows the same cutscene to be reused with different
/// participants.
/// </para>
/// <para>
/// Standard binding categories:
/// </para>
/// <list type="bullet">
/// <item><b>Participants</b>: Active characters (hero, villain, ally)</item>
/// <item><b>Props</b>: Interactive objects (door, chest, weapon)</item>
/// <item><b>Locations</b>: Named positions (center_stage, exit_point)</item>
/// </list>
/// </remarks>
public sealed class CutsceneBindings
{
    /// <summary>
    /// Character participants mapped by semantic name.
    /// </summary>
    /// <example>
    /// { "hero": EntityRef(player_id), "villain": EntityRef(boss_id) }
    /// </example>
    public IReadOnlyDictionary<string, EntityReference> Participants { get; init; }
        = new Dictionary<string, EntityReference>();

    /// <summary>
    /// Prop entities mapped by semantic name.
    /// </summary>
    /// <example>
    /// { "mcguffin": EntityRef(item_id), "door": EntityRef(door_id) }
    /// </example>
    public IReadOnlyDictionary<string, EntityReference> Props { get; init; }
        = new Dictionary<string, EntityReference>();

    /// <summary>
    /// Named locations mapped by semantic name.
    /// </summary>
    /// <example>
    /// { "center": { x: 0, y: 0, z: 0 }, "exit": { x: 10, y: 0, z: 5 } }
    /// </example>
    public IReadOnlyDictionary<string, Vector3> Locations { get; init; }
        = new Dictionary<string, Vector3>();

    /// <summary>
    /// Role assignments mapping semantic names to roles.
    /// </summary>
    /// <remarks>
    /// Roles provide semantic meaning for resolution priority.
    /// E.g., "protagonist" → "hero", "antagonist" → "villain"
    /// </remarks>
    public IReadOnlyDictionary<string, string> Roles { get; init; }
        = new Dictionary<string, string>();

    /// <summary>
    /// Additional custom bindings.
    /// </summary>
    public IReadOnlyDictionary<string, object> Custom { get; init; }
        = new Dictionary<string, object>();

    /// <summary>
    /// Creates empty bindings.
    /// </summary>
    public static CutsceneBindings Empty => new();

    /// <summary>
    /// Creates bindings from a participant list.
    /// </summary>
    /// <param name="participants">Participant name-to-reference mappings.</param>
    public static CutsceneBindings FromParticipants(
        IReadOnlyDictionary<string, EntityReference> participants) => new()
        {
            Participants = participants
        };

    /// <summary>
    /// Creates a builder for constructing bindings.
    /// </summary>
    public static CutsceneBindingsBuilder Builder() => new();
}

/// <summary>
/// Builder for constructing cutscene bindings.
/// </summary>
public sealed class CutsceneBindingsBuilder
{
    private readonly Dictionary<string, EntityReference> _participants = new();
    private readonly Dictionary<string, EntityReference> _props = new();
    private readonly Dictionary<string, Vector3> _locations = new();
    private readonly Dictionary<string, string> _roles = new();
    private readonly Dictionary<string, object> _custom = new();

    /// <summary>
    /// Adds a participant binding.
    /// </summary>
    public CutsceneBindingsBuilder AddParticipant(string name, EntityReference reference)
    {
        _participants[name] = reference;
        return this;
    }

    /// <summary>
    /// Adds a participant binding by ID.
    /// </summary>
    public CutsceneBindingsBuilder AddParticipant(string name, Guid entityId)
    {
        _participants[name] = EntityReference.FromId(entityId);
        return this;
    }

    /// <summary>
    /// Adds a prop binding.
    /// </summary>
    public CutsceneBindingsBuilder AddProp(string name, EntityReference reference)
    {
        _props[name] = reference;
        return this;
    }

    /// <summary>
    /// Adds a prop binding by ID.
    /// </summary>
    public CutsceneBindingsBuilder AddProp(string name, Guid entityId)
    {
        _props[name] = EntityReference.Prop(entityId);
        return this;
    }

    /// <summary>
    /// Adds a location binding.
    /// </summary>
    public CutsceneBindingsBuilder AddLocation(string name, Vector3 position)
    {
        _locations[name] = position;
        return this;
    }

    /// <summary>
    /// Adds a role assignment.
    /// </summary>
    public CutsceneBindingsBuilder AddRole(string roleName, string bindingName)
    {
        _roles[roleName] = bindingName;
        return this;
    }

    /// <summary>
    /// Adds a custom binding.
    /// </summary>
    public CutsceneBindingsBuilder AddCustom(string name, object value)
    {
        _custom[name] = value;
        return this;
    }

    /// <summary>
    /// Builds the bindings.
    /// </summary>
    public CutsceneBindings Build() => new()
    {
        Participants = _participants,
        Props = _props,
        Locations = _locations,
        Roles = _roles,
        Custom = _custom
    };
}

/// <summary>
/// Context for entity resolution, providing additional information for dynamic bindings.
/// </summary>
public sealed class EntityResolutionContext
{
    /// <summary>
    /// The cutscene session ID (if in a cutscene).
    /// </summary>
    public string? SessionId { get; init; }

    /// <summary>
    /// The current document being executed.
    /// </summary>
    public string? DocumentId { get; init; }

    /// <summary>
    /// Variables from the expression context.
    /// </summary>
    public IReadOnlyDictionary<string, object?>? Variables { get; init; }

    /// <summary>
    /// The requesting entity (for relative bindings like "self" or "target").
    /// </summary>
    public Guid? RequestingEntity { get; init; }

    /// <summary>
    /// Creates empty context.
    /// </summary>
    public static EntityResolutionContext Empty => new();
}
