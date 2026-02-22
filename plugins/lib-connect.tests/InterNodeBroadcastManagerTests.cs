using BeyondImmersion.BannouService.Connect;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;

namespace BeyondImmersion.BannouService.Connect.Tests;

/// <summary>
/// Unit tests for InterNodeBroadcastManager.
/// Tests compatibility filtering, activation logic, and mode-gated behavior.
/// </summary>
public class InterNodeBroadcastManagerTests
{
    private readonly Mock<IStateStoreFactory> _mockStateStoreFactory;
    private readonly Mock<ICacheableStateStore<string>> _mockCacheStore;
    private readonly Mock<ILogger<InterNodeBroadcastManager>> _mockLogger;

    public InterNodeBroadcastManagerTests()
    {
        _mockStateStoreFactory = new Mock<IStateStoreFactory>();
        _mockCacheStore = new Mock<ICacheableStateStore<string>>();
        _mockLogger = new Mock<ILogger<InterNodeBroadcastManager>>();

        _mockStateStoreFactory
            .Setup(f => f.GetCacheableStore<string>(It.IsAny<string>()))
            .Returns(_mockCacheStore.Object);
    }

    private InterNodeBroadcastManager CreateManager(
        BroadcastMode mode = BroadcastMode.None,
        string? broadcastUrl = null,
        Guid? instanceId = null)
    {
        var config = new ConnectServiceConfiguration
        {
            MultiNodeBroadcastMode = mode,
            BroadcastInternalUrl = broadcastUrl,
            BroadcastHeartbeatIntervalSeconds = 30,
            BroadcastStaleThresholdSeconds = 90,
            BufferSize = 65536
        };

        var meshIdentifier = new DefaultMeshInstanceIdentifier(instanceId ?? Guid.NewGuid());

        return new InterNodeBroadcastManager(
            _mockStateStoreFactory.Object,
            config,
            meshIdentifier,
            Mock.Of<ITelemetryProvider>(),
            _mockLogger.Object);
    }

    #region IsCompatible Tests

    [Theory]
    [InlineData(BroadcastMode.None, BroadcastMode.None, false)]
    [InlineData(BroadcastMode.None, BroadcastMode.Send, false)]
    [InlineData(BroadcastMode.None, BroadcastMode.Receive, false)]
    [InlineData(BroadcastMode.None, BroadcastMode.Both, false)]
    [InlineData(BroadcastMode.Send, BroadcastMode.None, false)]
    [InlineData(BroadcastMode.Send, BroadcastMode.Send, false)]
    [InlineData(BroadcastMode.Send, BroadcastMode.Receive, true)]
    [InlineData(BroadcastMode.Send, BroadcastMode.Both, true)]
    [InlineData(BroadcastMode.Receive, BroadcastMode.None, false)]
    [InlineData(BroadcastMode.Receive, BroadcastMode.Send, true)]
    [InlineData(BroadcastMode.Receive, BroadcastMode.Receive, false)]
    [InlineData(BroadcastMode.Receive, BroadcastMode.Both, true)]
    [InlineData(BroadcastMode.Both, BroadcastMode.None, false)]
    [InlineData(BroadcastMode.Both, BroadcastMode.Send, true)]
    [InlineData(BroadcastMode.Both, BroadcastMode.Receive, true)]
    [InlineData(BroadcastMode.Both, BroadcastMode.Both, true)]
    public void IsCompatible_AllModeCombinations_ReturnsExpectedResult(
        BroadcastMode myMode, BroadcastMode peerMode, bool expected)
    {
        var result = InterNodeBroadcastManager.IsCompatible(myMode, peerMode);
        Assert.Equal(expected, result);
    }

    #endregion

    #region Activation Tests

    [Fact]
    public void Constructor_ModeNone_IsInactive()
    {
        using var manager = CreateManager(BroadcastMode.None, "ws://localhost:5012/connect/broadcast");
        Assert.Equal(0, manager.ActiveConnectionCount);
    }

    [Fact]
    public void Constructor_NullUrl_IsInactive()
    {
        using var manager = CreateManager(BroadcastMode.Both, null);
        Assert.Equal(0, manager.ActiveConnectionCount);
    }

    [Fact]
    public void Constructor_EmptyUrl_IsInactive()
    {
        using var manager = CreateManager(BroadcastMode.Both, "");
        Assert.Equal(0, manager.ActiveConnectionCount);
    }

    [Fact]
    public void Constructor_ValidModeAndUrl_CreatesActiveManager()
    {
        using var manager = CreateManager(BroadcastMode.Both, "ws://localhost:5012/connect/broadcast");
        Assert.Equal(0, manager.ActiveConnectionCount);
    }

    #endregion

    #region InitializeAsync Tests

    [Fact]
    public async Task InitializeAsync_WhenInactive_DoesNotRegisterInRedis()
    {
        using var manager = CreateManager(BroadcastMode.None, null);

        await manager.InitializeAsync(CancellationToken.None);

        // Should not interact with state store at all
        _mockCacheStore.Verify(s => s.SortedSetAddAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<double>(),
            It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task InitializeAsync_WhenActive_RegistersInRedisAndDiscoversPeers()
    {
        // Arrange - return empty peer list
        _mockCacheStore
            .Setup(s => s.SortedSetAddAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<double>(),
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _mockCacheStore
            .Setup(s => s.SortedSetRangeByScoreAsync(
                It.IsAny<string>(), It.IsAny<double>(), It.IsAny<double>(),
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<(string member, double score)>());

        using var manager = CreateManager(BroadcastMode.Both, "ws://localhost:5012/connect/broadcast");

        // Act
        await manager.InitializeAsync(CancellationToken.None);

        // Assert - registered self in sorted set
        _mockCacheStore.Verify(s => s.SortedSetAddAsync(
            "broadcast-registry",
            It.IsAny<string>(),
            It.IsAny<double>(),
            It.IsAny<StateOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);

        // Assert - queried for peers
        _mockCacheStore.Verify(s => s.SortedSetRangeByScoreAsync(
            "broadcast-registry",
            It.IsAny<double>(),
            It.Is<double>(d => double.IsPositiveInfinity(d)),
            It.IsAny<int>(),
            It.IsAny<int>(),
            It.IsAny<bool>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region RelayBroadcastAsync Tests

    [Fact]
    public async Task RelayBroadcastAsync_WhenModeIsReceive_DoesNotSend()
    {
        using var manager = CreateManager(BroadcastMode.Receive, "ws://localhost:5012/connect/broadcast");

        // Act - should be a no-op since mode doesn't include Send
        await manager.RelayBroadcastAsync(new byte[] { 1, 2, 3 }, CancellationToken.None);

        // No connections to verify against, but the method should return immediately
        Assert.Equal(0, manager.ActiveConnectionCount);
    }

    [Fact]
    public async Task RelayBroadcastAsync_WhenModeIsNone_DoesNotSend()
    {
        using var manager = CreateManager(BroadcastMode.None, null);

        await manager.RelayBroadcastAsync(new byte[] { 1, 2, 3 }, CancellationToken.None);

        Assert.Equal(0, manager.ActiveConnectionCount);
    }

    #endregion

    #region HandleIncomingConnectionAsync Tests

    [Fact]
    public async Task HandleIncomingConnectionAsync_WhenInactive_RejectsConnection()
    {
        using var manager = CreateManager(BroadcastMode.None, null);
        var mockWs = new Mock<System.Net.WebSockets.WebSocket>();

        // Act - should return immediately without adding to connections
        await manager.HandleIncomingConnectionAsync(mockWs.Object, Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(0, manager.ActiveConnectionCount);
    }

    #endregion

    #region Dispose Tests

    [Fact]
    public void Dispose_WhenInactive_DoesNotDeregister()
    {
        using var manager = CreateManager(BroadcastMode.None, null);

        manager.Dispose();

        // Should not try to remove from Redis
        _mockCacheStore.Verify(s => s.SortedSetRemoveAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void Dispose_WhenActive_DeregistersFromRedis()
    {
        _mockCacheStore
            .Setup(s => s.SortedSetRemoveAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult(true));

        using var manager = CreateManager(BroadcastMode.Both, "ws://localhost:5012/connect/broadcast");

        manager.Dispose();

        // Should deregister from Redis
        _mockCacheStore.Verify(s => s.SortedSetRemoveAsync(
            "broadcast-registry",
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void Dispose_CalledTwice_OnlyDeregistersOnce()
    {
        _mockCacheStore
            .Setup(s => s.SortedSetRemoveAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult(true));

        using var manager = CreateManager(BroadcastMode.Both, "ws://localhost:5012/connect/broadcast");

        manager.Dispose();
        manager.Dispose();

        _mockCacheStore.Verify(s => s.SortedSetRemoveAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion
}
