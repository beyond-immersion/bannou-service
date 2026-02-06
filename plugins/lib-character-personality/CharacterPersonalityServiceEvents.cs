// =============================================================================
// Character Personality Service Events
// Event consumer registration and handlers for cache invalidation.
// =============================================================================

using BeyondImmersion.BannouService.CharacterPersonality.Caching;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.CharacterPersonality;

/// <summary>
/// Partial class for CharacterPersonalityService event handling.
/// Contains event consumer registration and handler implementations for cache invalidation.
/// </summary>
public partial class CharacterPersonalityService : IBannouService
{
    /// <summary>
    /// Registers event consumers for pub/sub events this service handles.
    /// Called from the main service constructor via IBannouService interface.
    /// </summary>
    void IBannouService.RegisterEventConsumers(IEventConsumer eventConsumer)
    {
        // Self-subscribe to our own events for cache invalidation.
        // When personality or combat preferences evolve, invalidate the cache so
        // running actors get fresh data on their next behavior tick.
        eventConsumer.RegisterHandler<ICharacterPersonalityService, PersonalityEvolvedEvent>(
            "personality.evolved",
            async (svc, evt) => await ((CharacterPersonalityService)svc).HandlePersonalityEvolvedAsync(evt));

        eventConsumer.RegisterHandler<ICharacterPersonalityService, CombatPreferencesEvolvedEvent>(
            "combat-preferences.evolved",
            async (svc, evt) => await ((CharacterPersonalityService)svc).HandleCombatPreferencesEvolvedAsync(evt));
    }

    /// <summary>
    /// Handles personality.evolved events.
    /// Invalidates the personality cache for the affected character.
    /// </summary>
    private async Task HandlePersonalityEvolvedAsync(PersonalityEvolvedEvent evt)
    {
        _logger.LogDebug(
            "Received personality.evolved event for character {CharacterId}, invalidating cache",
            evt.CharacterId);

        _personalityCache.Invalidate(evt.CharacterId);

        // Yield to honor async contract per IMPLEMENTATION TENETS
        await Task.Yield();
    }

    /// <summary>
    /// Handles combat-preferences.evolved events.
    /// Invalidates the cache for the affected character (both personality and combat prefs).
    /// </summary>
    private async Task HandleCombatPreferencesEvolvedAsync(CombatPreferencesEvolvedEvent evt)
    {
        _logger.LogDebug(
            "Received combat-preferences.evolved event for character {CharacterId}, invalidating cache",
            evt.CharacterId);

        // Use Invalidate which clears all cached data for the character
        _personalityCache.Invalidate(evt.CharacterId);

        // Yield to honor async contract per IMPLEMENTATION TENETS
        await Task.Yield();
    }
}
