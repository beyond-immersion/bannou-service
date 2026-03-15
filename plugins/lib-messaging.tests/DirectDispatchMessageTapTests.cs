using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Messaging.Services;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.TestUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace BeyondImmersion.BannouService.Messaging.Tests;

/// <summary>
/// Parity tests for InMemoryMessageTap backed by DirectDispatchMessageBus.
/// Mirrors InMemoryMessageTapTests to confirm both backends produce identical
/// client-observable behavior through the IMessageTap interface.
/// </summary>
public class DirectDispatchMessageTapTests : IDisposable
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly DirectDispatchMessageBus _messageBus;
    private readonly InMemoryMessageTap _messageTap;

    public DirectDispatchMessageTapTests()
    {
        _loggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Debug));

        // Wire up a real EventConsumer + ServiceProvider for DirectDispatch
        var services = new ServiceCollection();
        services.AddSingleton<IEventConsumer, EventConsumer>();
        services.AddSingleton<ILoggerFactory>(_loggerFactory);
        services.AddSingleton(typeof(ILogger<>), typeof(Logger<>));
        services.AddSingleton(new MessagingServiceConfiguration());
        var provider = services.BuildServiceProvider();

        _messageBus = new DirectDispatchMessageBus(
            provider.GetRequiredService<IEventConsumer>(),
            provider,
            _loggerFactory.CreateLogger<DirectDispatchMessageBus>(),
            new MessagingServiceConfiguration());

        // Same InMemoryMessageTap — now backed by DirectDispatchMessageBus via interfaces
        _messageTap = new InMemoryMessageTap(
            _messageBus,
            _messageBus,
            _loggerFactory.CreateLogger<InMemoryMessageTap>());
    }

    public void Dispose()
    {
        _loggerFactory.Dispose();
        GC.SuppressFinalize(this);
    }

    #region Constructor Tests

    [Fact]
    public void ConstructorIsValid()
    {
        Assert.NotNull(_messageTap);
    }

    #endregion

    #region CreateTapAsync Tests

    [Fact]
    public async Task CreateTapAsync_WithValidParameters_ReturnsTapHandle()
    {
        // Arrange
        var sourceTopic = "test.source";
        var destination = new TapDestination
        {
            Exchange = "test-exchange",
            RoutingKey = "test-key"
        };

        // Act
        var handle = await _messageTap.CreateTapAsync(sourceTopic, destination, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(handle);
        Assert.NotEqual(Guid.Empty, handle.TapId);
        Assert.Equal(sourceTopic, handle.SourceTopic);
        Assert.True(handle.IsActive);

        // Cleanup
        await handle.DisposeAsync();
    }

    #endregion

    #region Message Forwarding Tests

    [Fact]
    public async Task Tap_ForwardsMessagesWithTappedEnvelope()
    {
        // Arrange
        var sourceTopic = "test.source";
        var destExchange = "test-dest";
        var destRoutingKey = "test-key";
        var destTopic = $"{destExchange}.{destRoutingKey}";

        TappedMessageEnvelope? receivedEnvelope = null;
        var receivedEvent = new TaskCompletionSource<bool>();

        await _messageBus.SubscribeDynamicAsync<TappedMessageEnvelope>(
            destTopic,
            (env, ct) =>
            {
                receivedEnvelope = env;
                receivedEvent.TrySetResult(true);
                return Task.CompletedTask;
            }, cancellationToken: TestContext.Current.CancellationToken);

        var handle = await _messageTap.CreateTapAsync(sourceTopic, new TapDestination
        {
            Exchange = destExchange,
            RoutingKey = destRoutingKey
        }, cancellationToken: TestContext.Current.CancellationToken);

        // Act
        var originalEnvelope = new GenericMessageEnvelope(sourceTopic, new { Test = "Value" });
        await _messageBus.TryPublishAsync(sourceTopic, originalEnvelope, cancellationToken: TestContext.Current.CancellationToken);

        await Task.WhenAny(receivedEvent.Task, Task.Delay(1000, cancellationToken: TestContext.Current.CancellationToken));

        // Assert
        Assert.NotNull(receivedEnvelope);
        Assert.Equal(handle.TapId, receivedEnvelope.TapId);
        Assert.Equal(sourceTopic, receivedEnvelope.SourceTopic);
        Assert.Equal(destExchange, receivedEnvelope.DestinationExchange);
        Assert.Equal(destRoutingKey, receivedEnvelope.DestinationRoutingKey);

        // Cleanup
        await handle.DisposeAsync();
    }

    #endregion

    #region Tap Disposal Tests

    [Fact]
    public async Task DisposeTap_StopsForwardingMessages()
    {
        // Arrange
        var sourceTopic = "test.source";
        var destTopic = "test-dest.test-key";

        var receivedCount = 0;

        await _messageBus.SubscribeDynamicAsync<TappedMessageEnvelope>(
            destTopic,
            (env, ct) =>
            {
                Interlocked.Increment(ref receivedCount);
                return Task.CompletedTask;
            }, cancellationToken: TestContext.Current.CancellationToken);

        var handle = await _messageTap.CreateTapAsync(sourceTopic, new TapDestination
        {
            Exchange = "test-dest",
            RoutingKey = "test-key"
        }, cancellationToken: TestContext.Current.CancellationToken);

        // Send first message
        await _messageBus.TryPublishAsync(sourceTopic, new GenericMessageEnvelope(sourceTopic, new { }), cancellationToken: TestContext.Current.CancellationToken);
        await Task.Delay(100, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(1, receivedCount);

        // Act — Dispose
        await handle.DisposeAsync();
        Assert.False(handle.IsActive);

        // Send second message
        await _messageBus.TryPublishAsync(sourceTopic, new GenericMessageEnvelope(sourceTopic, new { }), cancellationToken: TestContext.Current.CancellationToken);
        await Task.Delay(100, cancellationToken: TestContext.Current.CancellationToken);

        // Assert — Still only 1
        Assert.Equal(1, receivedCount);
    }

    [Fact]
    public async Task DisposeTap_CanBeCalledMultipleTimes()
    {
        // Arrange
        var handle = await _messageTap.CreateTapAsync("test.source", new TapDestination
        {
            Exchange = "dest",
            RoutingKey = "key"
        }, cancellationToken: TestContext.Current.CancellationToken);

        // Act — Dispose multiple times
        await handle.DisposeAsync();
        await handle.DisposeAsync();
        await handle.DisposeAsync();

        // Assert — Should not throw
        Assert.False(handle.IsActive);
    }

    #endregion

    #region Multiple Taps Tests

    [Fact]
    public async Task MultipleTaps_IndependentlyDisposable()
    {
        // Arrange
        var sourceTopic = "test.source";
        var destTopic1 = "dest1.key";
        var destTopic2 = "dest2.key";

        var received1 = new ConcurrentBag<TappedMessageEnvelope>();
        var received2 = new ConcurrentBag<TappedMessageEnvelope>();

        await _messageBus.SubscribeDynamicAsync<TappedMessageEnvelope>(
            destTopic1,
            (env, ct) => { received1.Add(env); return Task.CompletedTask; }, cancellationToken: TestContext.Current.CancellationToken);

        await _messageBus.SubscribeDynamicAsync<TappedMessageEnvelope>(
            destTopic2,
            (env, ct) => { received2.Add(env); return Task.CompletedTask; }, cancellationToken: TestContext.Current.CancellationToken);

        var tap1 = await _messageTap.CreateTapAsync(sourceTopic, new TapDestination { Exchange = "dest1", RoutingKey = "key" }, cancellationToken: TestContext.Current.CancellationToken);
        var tap2 = await _messageTap.CreateTapAsync(sourceTopic, new TapDestination { Exchange = "dest2", RoutingKey = "key" }, cancellationToken: TestContext.Current.CancellationToken);

        // Send first message (both receive)
        await _messageBus.TryPublishAsync(sourceTopic, new GenericMessageEnvelope(sourceTopic, new { Round = 1 }), cancellationToken: TestContext.Current.CancellationToken);
        await Task.Delay(100, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Single(received1);
        Assert.Single(received2);

        // Dispose tap1 only
        await tap1.DisposeAsync();

        // Send second message (only tap2 receives)
        await _messageBus.TryPublishAsync(sourceTopic, new GenericMessageEnvelope(sourceTopic, new { Round = 2 }), cancellationToken: TestContext.Current.CancellationToken);
        await Task.Delay(100, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(received1);
        Assert.Equal(2, received2.Count);

        // Cleanup
        await tap2.DisposeAsync();
    }

    #endregion

    #region TapHandle Properties Tests

    [Fact]
    public async Task TapHandle_HasCorrectMetadata()
    {
        // Arrange
        var sourceTopic = "character.events.char-123";
        var destination = new TapDestination
        {
            Exchange = "bannou-client-events",
            RoutingKey = "CONNECT_SESSION_abc",
            ExchangeType = TapExchangeType.Direct
        };

        var beforeCreate = DateTimeOffset.UtcNow;

        // Act
        var handle = await _messageTap.CreateTapAsync(sourceTopic, destination, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotEqual(Guid.Empty, handle.TapId);
        Assert.Equal(sourceTopic, handle.SourceTopic);
        Assert.Same(destination, handle.Destination);
        Assert.True(handle.CreatedAt >= beforeCreate);
        Assert.True(handle.IsActive);

        // Cleanup
        await handle.DisposeAsync();
    }

    #endregion

    #region DisposeAsync Tests

    [Fact]
    public async Task DisposeAsync_CleansUpAllTaps()
    {
        // Arrange
        var tap1 = await _messageTap.CreateTapAsync("source1", new TapDestination { Exchange = "dest", RoutingKey = "key1" }, cancellationToken: TestContext.Current.CancellationToken);
        var tap2 = await _messageTap.CreateTapAsync("source2", new TapDestination { Exchange = "dest", RoutingKey = "key2" }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(tap1.IsActive);
        Assert.True(tap2.IsActive);

        // Act
        await _messageTap.DisposeAsync();

        // Assert — Both should be inactive
        Assert.False(tap1.IsActive);
        Assert.False(tap2.IsActive);
    }

    #endregion
}
