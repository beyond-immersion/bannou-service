// ═══════════════════════════════════════════════════════════════════════════
// Intent Emitter Interfaces
// Core interfaces for the Intent Channel system.
// Implementations provided by lib-behavior plugin.
// ═══════════════════════════════════════════════════════════════════════════

namespace BeyondImmersion.BannouService.Behavior;

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
/// Context for intent emission, providing entity and archetype information.
/// </summary>
public sealed class IntentEmissionContext
{
    /// <summary>
    /// The entity ID emitting the intent.
    /// </summary>
    public Guid EntityId { get; init; }

    /// <summary>
    /// The entity's archetype definition.
    /// </summary>
    public IArchetypeDefinition? Archetype { get; init; }

    /// <summary>
    /// The document type being executed (e.g., "behavior", "cutscene").
    /// </summary>
    public string DocumentType { get; init; } = "behavior";

    /// <summary>
    /// Additional context data from the execution scope.
    /// </summary>
    public IReadOnlyDictionary<string, object> Data { get; init; } =
        new Dictionary<string, object>();
}

/// <summary>
/// Interface for handlers that translate ABML actions to Intent Channel emissions.
/// </summary>
public interface IIntentEmitter
{
    /// <summary>
    /// The action name this emitter handles (e.g., "walk_to", "attack").
    /// </summary>
    string ActionName { get; }

    /// <summary>
    /// Document types this emitter supports.
    /// </summary>
    IReadOnlySet<string> SupportedDocumentTypes { get; }

    /// <summary>
    /// Checks if this emitter can handle the given action in the context.
    /// </summary>
    /// <param name="actionName">The action name to check.</param>
    /// <param name="context">The emission context.</param>
    /// <returns>True if this emitter can handle the action.</returns>
    bool CanEmit(string actionName, IntentEmissionContext context);

    /// <summary>
    /// Emits intents for the given action.
    /// </summary>
    /// <param name="parameters">The action parameters.</param>
    /// <param name="context">The emission context.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The emitted intents.</returns>
    ValueTask<IReadOnlyList<IntentEmission>> EmitAsync(
        IReadOnlyDictionary<string, object> parameters,
        IntentEmissionContext context,
        CancellationToken ct);
}

/// <summary>
/// Registry of Intent Emitters for action-to-intent translation.
/// </summary>
public interface IIntentEmitterRegistry
{
    /// <summary>
    /// Registers an intent emitter.
    /// </summary>
    /// <param name="emitter">The emitter to register.</param>
    void Register(IIntentEmitter emitter);

    /// <summary>
    /// Gets an emitter for the given action name and context.
    /// </summary>
    /// <param name="actionName">The action name to look up.</param>
    /// <param name="context">The emission context.</param>
    /// <returns>The matching emitter, or null if none found.</returns>
    IIntentEmitter? GetEmitter(string actionName, IntentEmissionContext context);

    /// <summary>
    /// Checks if an emitter exists for the given action.
    /// </summary>
    /// <param name="actionName">The action name to check.</param>
    /// <returns>True if an emitter is registered.</returns>
    bool HasEmitter(string actionName);

    /// <summary>
    /// Gets all registered action names.
    /// </summary>
    /// <returns>Collection of registered action names.</returns>
    IReadOnlyCollection<string> GetActionNames();
}
