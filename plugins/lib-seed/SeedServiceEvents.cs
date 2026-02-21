using BeyondImmersion.BannouService.Events;

namespace BeyondImmersion.BannouService.Seed;

/// <summary>
/// Partial class for SeedService event handling.
/// Contains event consumer registration and handler implementations.
/// </summary>
/// <remarks>
/// <para>
/// <b>Collection→Seed growth pipeline</b> uses the DI provider pattern
/// (ICollectionUnlockListener) instead of event subscriptions for guaranteed
/// in-process delivery. See SeedCollectionUnlockListener.cs.
/// </para>
/// </remarks>
public partial class SeedService
{
    /// <summary>
    /// Registers event consumers for pub/sub events this service handles.
    /// Called from the main service constructor.
    /// </summary>
    /// <param name="eventConsumer">The event consumer for registering handlers.</param>
    protected void RegisterEventConsumers(IEventConsumer eventConsumer)
    {
        // No event subscriptions registered.
        // Collection→Seed growth pipeline uses ICollectionUnlockListener (DI provider pattern)
        // for guaranteed in-process delivery instead of event bus subscriptions.
    }
}
