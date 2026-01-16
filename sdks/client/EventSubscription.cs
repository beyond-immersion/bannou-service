namespace BeyondImmersion.Bannou.Client;

/// <summary>
/// Internal implementation of event subscription handle.
/// Tracks subscription state and invokes unsubscribe callback on disposal.
/// </summary>
internal sealed class EventSubscription : IEventSubscription
{
    private readonly string _eventName;
    private readonly Guid _subscriptionId;
    private readonly Action<Guid> _unsubscribe;
    private volatile bool _isActive = true;

    /// <summary>
    /// Creates a new event subscription handle.
    /// </summary>
    /// <param name="eventName">The eventName being subscribed to.</param>
    /// <param name="subscriptionId">Unique ID for this subscription instance.</param>
    /// <param name="unsubscribe">Callback to invoke when disposing to remove the handler.</param>
    internal EventSubscription(string eventName, Guid subscriptionId, Action<Guid> unsubscribe)
    {
        _eventName = eventName ?? throw new ArgumentNullException(nameof(eventName));
        _subscriptionId = subscriptionId;
        _unsubscribe = unsubscribe ?? throw new ArgumentNullException(nameof(unsubscribe));
    }

    /// <inheritdoc />
    public string EventName => _eventName;

    /// <inheritdoc />
    public bool IsActive => _isActive;

    /// <summary>
    /// Unsubscribes from the event. Safe to call multiple times.
    /// </summary>
    public void Dispose()
    {
        if (_isActive)
        {
            _isActive = false;
            _unsubscribe(_subscriptionId);
        }
    }
}
