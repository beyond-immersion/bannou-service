#nullable enable

using BeyondImmersion.BannouService.Configuration;
using BeyondImmersion.BannouService.Messaging.Services;
using BeyondImmersion.BannouService.Services;
using MassTransit;
using Microsoft.Extensions.Logging;
using Moq;
using System.Collections.Concurrent;

namespace BeyondImmersion.BannouService.Messaging.Tests;

/// <summary>
/// Unit tests for MassTransitMessageTap - tests constructor validation and argument validation.
/// Integration testing against real RabbitMQ is done in http-tester.
/// </summary>
public class MassTransitMessageTapTests
{
    private readonly Mock<IBusControl> _mockBusControl;
    private readonly Mock<ILogger<MassTransitMessageTap>> _mockLogger;
    private readonly MessagingServiceConfiguration _configuration;

    public MassTransitMessageTapTests()
    {
        _mockBusControl = new Mock<IBusControl>();
        _mockLogger = new Mock<ILogger<MassTransitMessageTap>>();
        _configuration = new MessagingServiceConfiguration
        {
            DefaultExchange = "bannou",
            DefaultPrefetchCount = 10
        };
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithValidParameters_ShouldNotThrow()
    {
        // Arrange & Act
        var tap = new MassTransitMessageTap(
            _mockBusControl.Object,
            _mockLogger.Object,
            _configuration);

        // Assert
        Assert.NotNull(tap);
    }

    [Fact]
    public void Constructor_WithNullBusControl_ShouldThrowArgumentNullException()
    {
        // Arrange, Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() => new MassTransitMessageTap(
            null!,
            _mockLogger.Object,
            _configuration));
        Assert.Equal("busControl", ex.ParamName);
    }

    [Fact]
    public void Constructor_WithNullLogger_ShouldThrowArgumentNullException()
    {
        // Arrange, Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() => new MassTransitMessageTap(
            _mockBusControl.Object,
            null!,
            _configuration));
        Assert.Equal("logger", ex.ParamName);
    }

    [Fact]
    public void Constructor_WithNullConfiguration_ShouldThrowArgumentNullException()
    {
        // Arrange, Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() => new MassTransitMessageTap(
            _mockBusControl.Object,
            _mockLogger.Object,
            null!));
        Assert.Equal("configuration", ex.ParamName);
    }

    #endregion

    #region CreateTapAsync Argument Validation Tests

    [Fact]
    public async Task CreateTapAsync_WithNullSourceTopic_ShouldThrowArgumentNullException()
    {
        // Arrange
        var tap = new MassTransitMessageTap(
            _mockBusControl.Object,
            _mockLogger.Object,
            _configuration);

        var destination = new TapDestination
        {
            Exchange = "test-exchange",
            RoutingKey = "test-key"
        };

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            tap.CreateTapAsync(null!, destination));
    }

    [Fact]
    public async Task CreateTapAsync_WithNullDestination_ShouldThrowArgumentNullException()
    {
        // Arrange
        var tap = new MassTransitMessageTap(
            _mockBusControl.Object,
            _mockLogger.Object,
            _configuration);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            tap.CreateTapAsync("test.topic", null!));
    }

    #endregion
}

/// <summary>
/// Unit tests for InMemoryMessageTap - tests in-memory tap functionality without infrastructure.
/// These are true unit tests that don't require RabbitMQ.
/// </summary>
public class InMemoryMessageTapTests
{
    private readonly InMemoryMessageBus _messageBus;
    private readonly InMemoryMessageTap _messageTap;

    public InMemoryMessageTapTests()
    {
        var loggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Debug));
        _messageBus = new InMemoryMessageBus(loggerFactory.CreateLogger<InMemoryMessageBus>());
        _messageTap = new InMemoryMessageTap(_messageBus, loggerFactory.CreateLogger<InMemoryMessageTap>());
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithValidParameters_ShouldNotThrow()
    {
        // Arrange
        var loggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Debug));
        var bus = new InMemoryMessageBus(loggerFactory.CreateLogger<InMemoryMessageBus>());

        // Act
        var tap = new InMemoryMessageTap(bus, loggerFactory.CreateLogger<InMemoryMessageTap>());

        // Assert
        Assert.NotNull(tap);
    }

    [Fact]
    public void Constructor_WithNullMessageBus_ShouldThrowArgumentNullException()
    {
        // Arrange
        var loggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Debug));

        // Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() =>
            new InMemoryMessageTap(null!, loggerFactory.CreateLogger<InMemoryMessageTap>()));
        Assert.Equal("messageBus", ex.ParamName);
    }

    [Fact]
    public void Constructor_WithNullLogger_ShouldThrowArgumentNullException()
    {
        // Arrange
        var loggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Debug));
        var bus = new InMemoryMessageBus(loggerFactory.CreateLogger<InMemoryMessageBus>());

        // Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() =>
            new InMemoryMessageTap(bus, null!));
        Assert.Equal("logger", ex.ParamName);
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
        var handle = await _messageTap.CreateTapAsync(sourceTopic, destination);

        // Assert
        Assert.NotNull(handle);
        Assert.NotEqual(Guid.Empty, handle.TapId);
        Assert.Equal(sourceTopic, handle.SourceTopic);
        Assert.True(handle.IsActive);

        // Cleanup
        await handle.DisposeAsync();
    }

    [Fact]
    public async Task CreateTapAsync_WithNullSourceTopic_ShouldThrowArgumentNullException()
    {
        // Arrange
        var destination = new TapDestination
        {
            Exchange = "test-exchange",
            RoutingKey = "test-key"
        };

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _messageTap.CreateTapAsync(null!, destination));
    }

    [Fact]
    public async Task CreateTapAsync_WithNullDestination_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _messageTap.CreateTapAsync("test.topic", null!));
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
            });

        var handle = await _messageTap.CreateTapAsync(sourceTopic, new TapDestination
        {
            Exchange = destExchange,
            RoutingKey = destRoutingKey
        });

        // Act
        var originalEnvelope = new GenericMessageEnvelope(sourceTopic, new { Test = "Value" });
        await _messageBus.PublishAsync(sourceTopic, originalEnvelope);

        await Task.WhenAny(receivedEvent.Task, Task.Delay(1000));

        // Assert
        Assert.NotNull(receivedEnvelope);
        Assert.Equal(handle.TapId, receivedEnvelope.TapId);
        Assert.Equal(sourceTopic, receivedEnvelope.SourceTopic);
        Assert.Equal(destExchange, receivedEnvelope.DestinationExchange);
        Assert.Equal(destRoutingKey, receivedEnvelope.DestinationRoutingKey);

        // Cleanup
        await handle.DisposeAsync();
    }

    [Fact]
    public async Task Tap_PreservesOriginalEnvelopeData()
    {
        // Arrange
        var sourceTopic = "test.source";
        var destTopic = "dest.key";

        TappedMessageEnvelope? receivedEnvelope = null;
        var receivedEvent = new TaskCompletionSource<bool>();

        await _messageBus.SubscribeDynamicAsync<TappedMessageEnvelope>(
            destTopic,
            (env, ct) =>
            {
                receivedEnvelope = env;
                receivedEvent.TrySetResult(true);
                return Task.CompletedTask;
            });

        var handle = await _messageTap.CreateTapAsync(sourceTopic, new TapDestination
        {
            Exchange = "dest",
            RoutingKey = "key"
        });

        // Act
        var payload = new TestPayload { Message = "Hello", Value = 42 };
        var originalEnvelope = new GenericMessageEnvelope(sourceTopic, payload);
        await _messageBus.PublishAsync(sourceTopic, originalEnvelope);

        await Task.WhenAny(receivedEvent.Task, Task.Delay(1000));

        // Assert
        Assert.NotNull(receivedEnvelope);
        Assert.Equal(originalEnvelope.Topic, receivedEnvelope.Topic);
        Assert.NotNull(receivedEnvelope.PayloadJson);

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
            });

        var handle = await _messageTap.CreateTapAsync(sourceTopic, new TapDestination
        {
            Exchange = "test-dest",
            RoutingKey = "test-key"
        });

        // Send first message
        await _messageBus.PublishAsync(sourceTopic, new GenericMessageEnvelope(sourceTopic, new { }));
        await Task.Delay(100);

        Assert.Equal(1, receivedCount);

        // Act - Dispose
        await handle.DisposeAsync();
        Assert.False(handle.IsActive);

        // Send second message
        await _messageBus.PublishAsync(sourceTopic, new GenericMessageEnvelope(sourceTopic, new { }));
        await Task.Delay(100);

        // Assert - Still only 1
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
        });

        // Act - Dispose multiple times
        await handle.DisposeAsync();
        await handle.DisposeAsync();
        await handle.DisposeAsync();

        // Assert - Should not throw
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
            (env, ct) => { received1.Add(env); return Task.CompletedTask; });

        await _messageBus.SubscribeDynamicAsync<TappedMessageEnvelope>(
            destTopic2,
            (env, ct) => { received2.Add(env); return Task.CompletedTask; });

        var tap1 = await _messageTap.CreateTapAsync(sourceTopic, new TapDestination { Exchange = "dest1", RoutingKey = "key" });
        var tap2 = await _messageTap.CreateTapAsync(sourceTopic, new TapDestination { Exchange = "dest2", RoutingKey = "key" });

        // Send first message (both receive)
        await _messageBus.PublishAsync(sourceTopic, new GenericMessageEnvelope(sourceTopic, new { Round = 1 }));
        await Task.Delay(100);

        Assert.Single(received1);
        Assert.Single(received2);

        // Dispose tap1 only
        await tap1.DisposeAsync();

        // Send second message (only tap2 receives)
        await _messageBus.PublishAsync(sourceTopic, new GenericMessageEnvelope(sourceTopic, new { Round = 2 }));
        await Task.Delay(100);

        // Assert
        Assert.Single(received1);
        Assert.Equal(2, received2.Count);

        // Cleanup
        await tap2.DisposeAsync();
    }

    [Fact]
    public async Task MultipleTaps_SameSourceToDifferentDestinations_AllReceive()
    {
        // Arrange - Simulates character event tapping pattern (D→A, C→A, B→A)
        var sourceTopic = "test.source";

        var received1 = new ConcurrentBag<TappedMessageEnvelope>();
        var received2 = new ConcurrentBag<TappedMessageEnvelope>();
        var received3 = new ConcurrentBag<TappedMessageEnvelope>();

        await _messageBus.SubscribeDynamicAsync<TappedMessageEnvelope>(
            "session.client-abc",
            (env, ct) => { received1.Add(env); return Task.CompletedTask; });

        await _messageBus.SubscribeDynamicAsync<TappedMessageEnvelope>(
            "session.client-def",
            (env, ct) => { received2.Add(env); return Task.CompletedTask; });

        await _messageBus.SubscribeDynamicAsync<TappedMessageEnvelope>(
            "session.client-ghi",
            (env, ct) => { received3.Add(env); return Task.CompletedTask; });

        var tap1 = await _messageTap.CreateTapAsync(sourceTopic, new TapDestination { Exchange = "session", RoutingKey = "client-abc" });
        var tap2 = await _messageTap.CreateTapAsync(sourceTopic, new TapDestination { Exchange = "session", RoutingKey = "client-def" });
        var tap3 = await _messageTap.CreateTapAsync(sourceTopic, new TapDestination { Exchange = "session", RoutingKey = "client-ghi" });

        // Act - Send one message
        await _messageBus.PublishAsync(sourceTopic, new GenericMessageEnvelope(sourceTopic, new { Event = "test" }));
        await Task.Delay(100);

        // Assert - All three destinations receive
        Assert.Single(received1);
        Assert.Single(received2);
        Assert.Single(received3);

        // Each has different TapId
        Assert.Equal(tap1.TapId, received1.First().TapId);
        Assert.Equal(tap2.TapId, received2.First().TapId);
        Assert.Equal(tap3.TapId, received3.First().TapId);

        // Cleanup
        await tap1.DisposeAsync();
        await tap2.DisposeAsync();
        await tap3.DisposeAsync();
    }

    [Fact]
    public async Task MultipleSources_TappedToSameDestination_AllDistinguishable()
    {
        // Arrange - Simulates multiple character streams tapped to one client session
        var sourceA = "character.events.char-a";
        var sourceB = "character.events.char-b";
        var sourceC = "character.events.char-c";
        var destTopic = "client-events.session-123";

        var receivedEnvelopes = new ConcurrentBag<TappedMessageEnvelope>();

        await _messageBus.SubscribeDynamicAsync<TappedMessageEnvelope>(
            destTopic,
            (env, ct) => { receivedEnvelopes.Add(env); return Task.CompletedTask; });

        var dest = new TapDestination { Exchange = "client-events", RoutingKey = "session-123" };
        var tapA = await _messageTap.CreateTapAsync(sourceA, dest);
        var tapB = await _messageTap.CreateTapAsync(sourceB, dest);
        var tapC = await _messageTap.CreateTapAsync(sourceC, dest);

        // Act - Send from each source
        await _messageBus.PublishAsync(sourceA, new GenericMessageEnvelope(sourceA, new { Character = "A" }));
        await _messageBus.PublishAsync(sourceB, new GenericMessageEnvelope(sourceB, new { Character = "B" }));
        await _messageBus.PublishAsync(sourceC, new GenericMessageEnvelope(sourceC, new { Character = "C" }));
        await Task.Delay(100);

        // Assert
        Assert.Equal(3, receivedEnvelopes.Count);

        // Can distinguish by TapId
        var tapIds = receivedEnvelopes.Select(e => e.TapId).Distinct().ToList();
        Assert.Equal(3, tapIds.Count);
        Assert.Contains(tapA.TapId, tapIds);
        Assert.Contains(tapB.TapId, tapIds);
        Assert.Contains(tapC.TapId, tapIds);

        // Can distinguish by SourceTopic
        var sourcesReceived = receivedEnvelopes.Select(e => e.SourceTopic).Distinct().ToList();
        Assert.Equal(3, sourcesReceived.Count);
        Assert.Contains(sourceA, sourcesReceived);
        Assert.Contains(sourceB, sourcesReceived);
        Assert.Contains(sourceC, sourcesReceived);

        // Cleanup
        await tapA.DisposeAsync();
        await tapB.DisposeAsync();
        await tapC.DisposeAsync();
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
        var handle = await _messageTap.CreateTapAsync(sourceTopic, destination);

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
        var tap1 = await _messageTap.CreateTapAsync("source1", new TapDestination { Exchange = "dest", RoutingKey = "key1" });
        var tap2 = await _messageTap.CreateTapAsync("source2", new TapDestination { Exchange = "dest", RoutingKey = "key2" });

        Assert.True(tap1.IsActive);
        Assert.True(tap2.IsActive);

        // Act
        await _messageTap.DisposeAsync();

        // Assert - Both should be inactive
        Assert.False(tap1.IsActive);
        Assert.False(tap2.IsActive);
    }

    #endregion

    #region Test Models

    private class TestPayload
    {
        public string Message { get; set; } = string.Empty;
        public int Value { get; set; }
    }

    #endregion
}

/// <summary>
/// Unit tests for TappedMessageEnvelope.
/// </summary>
public class TappedMessageEnvelopeTests
{
    [Fact]
    public void Constructor_Default_InitializesWithDefaults()
    {
        // Arrange & Act
        var envelope = new TappedMessageEnvelope();

        // Assert
        Assert.Equal(Guid.Empty, envelope.TapId);
        Assert.Equal(string.Empty, envelope.SourceTopic);
        Assert.Equal(string.Empty, envelope.SourceExchange);
        Assert.Equal(string.Empty, envelope.DestinationExchange);
        Assert.Equal(string.Empty, envelope.DestinationRoutingKey);
        Assert.Equal("fanout", envelope.DestinationExchangeType);
    }

    [Fact]
    public void Constructor_FromOriginalEnvelope_CopiesBaseFields()
    {
        // Arrange
        var original = new GenericMessageEnvelope("test.topic", new { Key = "Value" });
        var tapId = Guid.NewGuid();
        var tapCreatedAt = DateTimeOffset.UtcNow;

        // Act
        var tapped = new TappedMessageEnvelope(
            original,
            tapId,
            "source.topic",
            "source-exchange",
            "dest-exchange",
            "dest-routing-key",
            "direct",
            tapCreatedAt);

        // Assert
        Assert.Equal(original.EventId, tapped.EventId);
        Assert.Equal(original.Timestamp, tapped.Timestamp);
        Assert.Equal(original.Topic, tapped.Topic);
        Assert.Equal(original.PayloadJson, tapped.PayloadJson);
        Assert.Equal(tapId, tapped.TapId);
        Assert.Equal("source.topic", tapped.SourceTopic);
        Assert.Equal("source-exchange", tapped.SourceExchange);
        Assert.Equal("dest-exchange", tapped.DestinationExchange);
        Assert.Equal("dest-routing-key", tapped.DestinationRoutingKey);
        Assert.Equal("direct", tapped.DestinationExchangeType);
        Assert.Equal(tapCreatedAt, tapped.TapCreatedAt);
    }

    [Fact]
    public void FromEvent_CreatesCorrectEnvelope()
    {
        // Arrange
        var payload = new { Message = "Test", Value = 123 };
        var tapId = Guid.NewGuid();
        var tapCreatedAt = DateTimeOffset.UtcNow;

        // Act
        var envelope = TappedMessageEnvelope.FromEvent(
            "source.topic",
            payload,
            tapId,
            "source-exchange",
            "dest-exchange",
            "dest-key",
            "fanout",
            tapCreatedAt);

        // Assert
        Assert.False(string.IsNullOrEmpty(envelope.EventId));
        Assert.Equal("source.topic", envelope.Topic);
        Assert.NotNull(envelope.PayloadJson);
        Assert.Equal(tapId, envelope.TapId);
        Assert.Equal("source.topic", envelope.SourceTopic);
        Assert.Equal("source-exchange", envelope.SourceExchange);
        Assert.Equal("dest-exchange", envelope.DestinationExchange);
        Assert.Equal("dest-key", envelope.DestinationRoutingKey);
        Assert.Equal("fanout", envelope.DestinationExchangeType);
    }
}

/// <summary>
/// Unit tests for TapDestination.
/// </summary>
public class TapDestinationTests
{
    [Fact]
    public void TapDestination_WithRequiredProperties_Initializes()
    {
        // Arrange & Act
        var dest = new TapDestination
        {
            Exchange = "test-exchange",
            RoutingKey = "test-key"
        };

        // Assert
        Assert.Equal("test-exchange", dest.Exchange);
        Assert.Equal("test-key", dest.RoutingKey);
        Assert.Equal(TapExchangeType.Fanout, dest.ExchangeType); // Default
        Assert.True(dest.CreateExchangeIfNotExists); // Default
    }

    [Fact]
    public void TapDestination_WithAllProperties_Initializes()
    {
        // Arrange & Act
        var dest = new TapDestination
        {
            Exchange = "bannou-client-events",
            RoutingKey = "CONNECT_SESSION_abc",
            ExchangeType = TapExchangeType.Direct,
            CreateExchangeIfNotExists = false
        };

        // Assert
        Assert.Equal("bannou-client-events", dest.Exchange);
        Assert.Equal("CONNECT_SESSION_abc", dest.RoutingKey);
        Assert.Equal(TapExchangeType.Direct, dest.ExchangeType);
        Assert.False(dest.CreateExchangeIfNotExists);
    }
}

/// <summary>
/// Unit tests for TapExchangeType enum.
/// </summary>
public class TapExchangeTypeTests
{
    [Theory]
    [InlineData(TapExchangeType.Fanout, "Fanout")]
    [InlineData(TapExchangeType.Direct, "Direct")]
    [InlineData(TapExchangeType.Topic, "Topic")]
    public void TapExchangeType_HasExpectedValues(TapExchangeType type, string expectedName)
    {
        // Assert
        Assert.Equal(expectedName, type.ToString());
    }
}
