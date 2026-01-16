// =============================================================================
// Event Subscription Tests
// Tests for IEventSubscription lifecycle and behavior.
// =============================================================================

using Xunit;

namespace BeyondImmersion.Bannou.Client.Tests.TypedApi;

/// <summary>
/// Tests for the EventSubscription class.
/// Verifies subscription lifecycle, disposal behavior, and callback invocation.
/// </summary>
public class EventSubscriptionTests
{
    // =========================================================================
    // INITIAL STATE TESTS
    // =========================================================================

    [Fact]
    public void Constructor_SetsEventName()
    {
        var subscription = new EventSubscription(
            "test.event",
            Guid.NewGuid(),
            _ => { });

        Assert.Equal("test.event", subscription.EventName);
    }

    [Fact]
    public void Constructor_IsActiveStartsTrue()
    {
        var subscription = new EventSubscription(
            "test.event",
            Guid.NewGuid(),
            _ => { });

        Assert.True(subscription.IsActive);
    }

    [Fact]
    public void Constructor_NullEventName_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new EventSubscription(null!, Guid.NewGuid(), _ => { }));
    }

    [Fact]
    public void Constructor_NullCallback_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new EventSubscription("test.event", Guid.NewGuid(), null!));
    }

    // =========================================================================
    // DISPOSE TESTS
    // =========================================================================

    [Fact]
    public void Dispose_SetsIsActiveToFalse()
    {
        var subscription = new EventSubscription(
            "test.event",
            Guid.NewGuid(),
            _ => { });

        subscription.Dispose();

        Assert.False(subscription.IsActive);
    }

    [Fact]
    public void Dispose_InvokesUnsubscribeCallback()
    {
        var callbackInvoked = false;
        var expectedId = Guid.NewGuid();
        Guid? receivedId = null;

        var subscription = new EventSubscription(
            "test.event",
            expectedId,
            id =>
            {
                callbackInvoked = true;
                receivedId = id;
            });

        subscription.Dispose();

        Assert.True(callbackInvoked);
        Assert.Equal(expectedId, receivedId);
    }

    [Fact]
    public void Dispose_CalledTwice_OnlyInvokesCallbackOnce()
    {
        var callbackCount = 0;
        var subscription = new EventSubscription(
            "test.event",
            Guid.NewGuid(),
            _ => callbackCount++);

        subscription.Dispose();
        subscription.Dispose();

        Assert.Equal(1, callbackCount);
    }

    [Fact]
    public void Dispose_CalledMultipleTimes_IsActivRemaninsFalse()
    {
        var subscription = new EventSubscription(
            "test.event",
            Guid.NewGuid(),
            _ => { });

        subscription.Dispose();
        Assert.False(subscription.IsActive);

        subscription.Dispose();
        Assert.False(subscription.IsActive);

        subscription.Dispose();
        Assert.False(subscription.IsActive);
    }

    // =========================================================================
    // USING PATTERN TESTS
    // =========================================================================

    [Fact]
    public void UsingPattern_DisposesOnBlockExit()
    {
        EventSubscription? subscriptionRef = null;
        var callbackInvoked = false;

        using (var subscription = new EventSubscription(
            "test.event",
            Guid.NewGuid(),
            _ => callbackInvoked = true))
        {
            subscriptionRef = subscription;
            Assert.True(subscription.IsActive);
        }

        Assert.False(subscriptionRef.IsActive);
        Assert.True(callbackInvoked);
    }

    // =========================================================================
    // CONCURRENT ACCESS TESTS
    // =========================================================================

    [Fact]
    public void Dispose_ConcurrentCalls_OnlyInvokesCallbackOnce()
    {
        var callbackCount = 0;
        var subscription = new EventSubscription(
            "test.event",
            Guid.NewGuid(),
            _ => Interlocked.Increment(ref callbackCount));

        // Spawn multiple threads that all try to dispose concurrently
        var tasks = Enumerable.Range(0, 10)
            .Select(_ => Task.Run(() => subscription.Dispose()))
            .ToArray();

        Task.WaitAll(tasks);

        // Callback should only be invoked once despite concurrent disposal attempts
        Assert.Equal(1, callbackCount);
    }

    // =========================================================================
    // SUBSCRIPTION ID TESTS
    // =========================================================================

    [Fact]
    public void MultipleSubscriptions_HaveDifferentBehavior()
    {
        var disposedIds = new List<Guid>();
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();

        var subscription1 = new EventSubscription("event1", id1, id => disposedIds.Add(id));
        var subscription2 = new EventSubscription("event2", id2, id => disposedIds.Add(id));

        subscription1.Dispose();

        Assert.Single(disposedIds);
        Assert.Contains(id1, disposedIds);
        Assert.DoesNotContain(id2, disposedIds);
        Assert.False(subscription1.IsActive);
        Assert.True(subscription2.IsActive);

        subscription2.Dispose();

        Assert.Equal(2, disposedIds.Count);
        Assert.Contains(id2, disposedIds);
    }
}
