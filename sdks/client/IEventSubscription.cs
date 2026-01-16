namespace BeyondImmersion.Bannou.Client;

/// <summary>
/// Represents an active event subscription that can be disposed to unsubscribe.
/// Returned by <see cref="IBannouClient.OnEvent{TEvent}"/> for managing subscription lifecycle.
/// </summary>
public interface IEventSubscription : IDisposable
{
    /// <summary>
    /// The eventName this subscription is listening for (e.g., "game_session.chat_received").
    /// </summary>
    string EventName { get; }

    /// <summary>
    /// Whether this subscription is still active.
    /// Returns false after <see cref="IDisposable.Dispose"/> has been called.
    /// </summary>
    bool IsActive { get; }
}
