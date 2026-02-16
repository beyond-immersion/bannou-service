namespace BeyondImmersion.BannouService.Actor.Runtime;

/// <summary>
/// Factory for creating ActorRunner instances with proper DI.
/// </summary>
public interface IActorRunnerFactory
{
    /// <summary>
    /// Creates a new actor runner instance.
    /// </summary>
    /// <param name="actorId">The unique identifier for the actor.</param>
    /// <param name="template">The template to instantiate from.</param>
    /// <param name="characterId">Optional character ID for NPC brain actors.</param>
    /// <param name="realmId">The realm this actor operates in.</param>
    /// <param name="configurationOverrides">Optional configuration overrides.</param>
    /// <param name="initialState">Optional initial state.</param>
    /// <returns>A new actor runner instance.</returns>
    IActorRunner Create(
        string actorId,
        ActorTemplateData template,
        Guid? characterId,
        Guid realmId,
        object? configurationOverrides = null,
        object? initialState = null);
}
