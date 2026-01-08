// =============================================================================
// Intent Emitter Implementation Types
// Extends bannou-service interfaces with implementation-specific details.
// =============================================================================

using BeyondImmersion.BannouService.Behavior.Archetypes;
// Import the core interfaces from bannou-service
using CoreBehavior = BeyondImmersion.BannouService.Behavior;

namespace BeyondImmersion.BannouService.Behavior.Handlers;

/// <summary>
/// Extended intent emission with additional data like target position.
/// Converts to/from the core IntentEmission type.
/// </summary>
/// <param name="Channel">The logical channel to emit to.</param>
/// <param name="Intent">The intent value (action name, target, etc.).</param>
/// <param name="Urgency">Urgency level (0.0-1.0).</param>
/// <param name="Target">Optional target entity ID.</param>
/// <param name="TargetPosition">Optional target position for movement.</param>
public sealed record IntentEmission(
    string Channel,
    string Intent,
    float Urgency,
    Guid? Target = null,
    System.Numerics.Vector3? TargetPosition = null)
{
    /// <summary>
    /// Converts to the core IntentEmission type for use by bannou-service.
    /// </summary>
    public CoreBehavior.IntentEmission ToCore()
    {
        var data = TargetPosition.HasValue
            ? new Dictionary<string, object> { ["target_position"] = TargetPosition.Value }
            : null;
        return new CoreBehavior.IntentEmission(Channel, Intent, Urgency, Target, data);
    }

    /// <summary>
    /// Creates from a core IntentEmission.
    /// </summary>
    public static IntentEmission FromCore(CoreBehavior.IntentEmission core)
    {
        System.Numerics.Vector3? pos = null;
        if (core.Data?.TryGetValue("target_position", out var posObj) == true &&
            posObj is System.Numerics.Vector3 vec)
        {
            pos = vec;
        }
        return new IntentEmission(core.Channel, core.Intent, core.Urgency, core.Target, pos);
    }
}

/// <summary>
/// Context for intent emission, providing archetype and entity information.
/// </summary>
public sealed class IntentEmissionContext
{
    /// <summary>
    /// The entity emitting intents.
    /// </summary>
    public required Guid EntityId { get; init; }

    /// <summary>
    /// The archetype definition for the entity.
    /// </summary>
    public required ArchetypeDefinition Archetype { get; init; }

    /// <summary>
    /// The document type being executed (behavior, cutscene, dialogue).
    /// </summary>
    public required string DocumentType { get; init; }

    /// <summary>
    /// Additional context data (entity state, variables, etc.).
    /// </summary>
    public IReadOnlyDictionary<string, object> Data { get; init; } = new Dictionary<string, object>();

    /// <summary>
    /// Creates from a core IntentEmissionContext.
    /// </summary>
    public static IntentEmissionContext FromCore(
        CoreBehavior.IntentEmissionContext core,
        ArchetypeDefinition archetype)
    {
        return new IntentEmissionContext
        {
            EntityId = core.EntityId,
            Archetype = archetype,
            DocumentType = core.DocumentType,
            Data = core.Data
        };
    }

    /// <summary>
    /// Converts to a core IntentEmissionContext.
    /// </summary>
    public CoreBehavior.IntentEmissionContext ToCore()
    {
        return new CoreBehavior.IntentEmissionContext
        {
            EntityId = EntityId,
            Archetype = Archetype,
            DocumentType = DocumentType,
            Data = Data
        };
    }
}

/// <summary>
/// Handler that translates ABML actions to Intent Channel emissions.
/// </summary>
/// <remarks>
/// <para>
/// Intent emitters are the bridge between ABML action execution and the
/// behavior runtime's Intent Channel system. They translate semantic actions
/// (like "walk_to", "attack") into structured intent emissions.
/// </para>
/// </remarks>
public interface IIntentEmitter
{
    /// <summary>
    /// The action name this emitter handles (e.g., "walk_to", "attack").
    /// </summary>
    string ActionName { get; }

    /// <summary>
    /// Document types this emitter supports.
    /// Empty means all document types.
    /// </summary>
    IReadOnlySet<string> SupportedDocumentTypes { get; }

    /// <summary>
    /// Determines if this emitter can handle the given action in context.
    /// </summary>
    /// <param name="actionName">The ABML action name.</param>
    /// <param name="context">The emission context.</param>
    /// <returns>True if this emitter can handle the action.</returns>
    bool CanEmit(string actionName, IntentEmissionContext context);

    /// <summary>
    /// Emits intents for the action.
    /// </summary>
    /// <param name="parameters">Action parameters from ABML.</param>
    /// <param name="context">The emission context.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The intent emissions.</returns>
    ValueTask<IReadOnlyList<IntentEmission>> EmitAsync(
        IReadOnlyDictionary<string, object> parameters,
        IntentEmissionContext context,
        CancellationToken ct);
}

/// <summary>
/// Registry for intent emitters, mapping ABML actions to Intent Channel emissions.
/// </summary>
public interface IIntentEmitterRegistry
{
    /// <summary>
    /// Registers an intent emitter.
    /// </summary>
    /// <param name="emitter">The emitter to register.</param>
    void Register(IIntentEmitter emitter);

    /// <summary>
    /// Gets an emitter for an action name and context.
    /// </summary>
    /// <param name="actionName">The ABML action name.</param>
    /// <param name="context">The emission context.</param>
    /// <returns>The emitter, or null if none found.</returns>
    IIntentEmitter? GetEmitter(string actionName, IntentEmissionContext context);

    /// <summary>
    /// Gets all registered action names.
    /// </summary>
    IReadOnlyCollection<string> GetActionNames();

    /// <summary>
    /// Checks if an action is registered.
    /// </summary>
    /// <param name="actionName">The action name.</param>
    /// <returns>True if an emitter is registered for this action.</returns>
    bool HasEmitter(string actionName);
}
