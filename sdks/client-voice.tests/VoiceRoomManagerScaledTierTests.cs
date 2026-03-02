using BeyondImmersion.Bannou.Client;
using BeyondImmersion.Bannou.Client.Voice;
using BeyondImmersion.Bannou.Voice.ClientEvents;
using BeyondImmersion.BannouService.Voice;
using Moq;
using Xunit;

namespace BeyondImmersion.Bannou.Client.Voice.Tests;

public class VoiceRoomManagerScaledTierTests
{
    private readonly Mock<IScaledVoiceConnection> _mockScaledConnection;
    private readonly Mock<BannouClient> _mockClient;
    private readonly Guid _testRoomId = Guid.NewGuid();

    public VoiceRoomManagerScaledTierTests()
    {
        _mockScaledConnection = new Mock<IScaledVoiceConnection>();
        _mockScaledConnection.Setup(c => c.RoomId).Returns(_testRoomId);
        _mockScaledConnection.Setup(c => c.State).Returns(ScaledVoiceConnectionState.Disconnected);

        // Mock BannouClient - it doesn't need full implementation for these tests
        _mockClient = new Mock<BannouClient>(MockBehavior.Loose);
    }

    private VoiceRoomManager CreateManager(Func<Guid, IScaledVoiceConnection>? factory = null)
    {
        return new VoiceRoomManager(
            _mockClient.Object,
            null, // Use default peer factory
            factory ?? ((roomId) => _mockScaledConnection.Object));
    }

    #region Initial State Tests

    [Fact]
    public void InitialState_NoScaledConnection()
    {
        // Arrange & Act
        using var manager = CreateManager();

        // Assert
        Assert.Null(manager.ScaledConnection);
        Assert.False(manager.IsScaledConnectionActive);
    }

    [Fact]
    public void InitialState_TierIsP2P()
    {
        // Arrange & Act
        using var manager = CreateManager();

        // Assert
        Assert.Equal(VoiceTier.P2P, manager.CurrentTier);
    }

    [Fact]
    public void InitialState_NotInRoom()
    {
        // Arrange & Act
        using var manager = CreateManager();

        // Assert
        Assert.False(manager.IsInRoom);
        Assert.Null(manager.CurrentRoomId);
    }

    #endregion

    #region IsMuted Tests

    [Fact]
    public void IsMuted_InitiallyFalse()
    {
        // Arrange & Act
        using var manager = CreateManager();

        // Assert
        Assert.False(manager.IsMuted);
    }

    [Fact]
    public void IsMuted_CanBeSetToTrue()
    {
        // Arrange
        using var manager = CreateManager();

        // Act
        manager.IsMuted = true;

        // Assert
        Assert.True(manager.IsMuted);
    }

    [Fact]
    public void IsMuted_CanBeToggledBackToFalse()
    {
        // Arrange
        using var manager = CreateManager();
        manager.IsMuted = true;

        // Act
        manager.IsMuted = false;

        // Assert
        Assert.False(manager.IsMuted);
    }

    #endregion

    #region SendAudioToAllPeers Tests

    [Fact]
    public void SendAudioToAllPeers_WhenNotInRoom_DoesNotThrow()
    {
        // Arrange
        using var manager = CreateManager();
        var samples = new float[960];

        // Act & Assert - should silently return
        manager.SendAudioToAllPeers(samples, 48000, 1);
    }

    [Fact]
    public void SendAudioToAllPeers_WhenMuted_DoesNotThrow()
    {
        // Arrange
        using var manager = CreateManager();
        manager.IsMuted = true;
        var samples = new float[960];

        // Act & Assert - should silently return
        manager.SendAudioToAllPeers(samples, 48000, 1);
    }

    #endregion

    #region Event Handler Tests

    [Fact]
    public void OnTierUpgraded_EventCanBeSubscribed()
    {
        // Arrange
        using var manager = CreateManager();
        string? capturedReason = null;
        manager.OnTierUpgraded += (reason) => capturedReason = reason;

        // Assert - event handler was wired (no invocation yet)
        Assert.Null(capturedReason);
    }

    [Fact]
    public void OnScaledConnectionStateChanged_EventCanBeSubscribed()
    {
        // Arrange
        using var manager = CreateManager();
        ScaledVoiceConnectionState? capturedState = null;
        manager.OnScaledConnectionStateChanged += (state) => capturedState = state;

        // Assert - event handler was wired
        Assert.Null(capturedState);
    }

    [Fact]
    public void OnScaledConnectionError_EventCanBeSubscribed()
    {
        // Arrange
        using var manager = CreateManager();
        ScaledVoiceErrorCode? capturedCode = null;
        string? capturedMessage = null;
        manager.OnScaledConnectionError += (code, msg) =>
        {
            capturedCode = code;
            capturedMessage = msg;
        };

        // Assert - event handler was wired
        Assert.Null(capturedCode);
        Assert.Null(capturedMessage);
    }

    [Fact]
    public void OnRoomClosed_EventCanBeSubscribed()
    {
        // Arrange
        using var manager = CreateManager();
        string? capturedReason = null;
        manager.OnRoomClosed += (reason) => capturedReason = reason;

        // Assert - event handler was wired
        Assert.Null(capturedReason);
    }

    #endregion

    #region Dispose Tests

    [Fact]
    public void Dispose_DoesNotThrow()
    {
        // Arrange
        var manager = CreateManager();

        // Act & Assert - should not throw
        manager.Dispose();
    }

    [Fact]
    public void Dispose_CanBeCalledMultipleTimes()
    {
        // Arrange
        var manager = CreateManager();

        // Act & Assert - should not throw
        manager.Dispose();
        manager.Dispose();
    }

    [Fact]
    public void Dispose_ClearsRoomState()
    {
        // Arrange
        var manager = CreateManager();

        // Act
        manager.Dispose();

        // Assert
        Assert.Null(manager.CurrentRoomId);
        Assert.False(manager.IsInRoom);
    }

    #endregion

    #region LeaveRoomAsync Tests

    [Fact]
    public async Task LeaveRoomAsync_WhenNotInRoom_DoesNotThrow()
    {
        // Arrange
        using var manager = CreateManager();

        // Act & Assert - should complete without throwing
        await manager.LeaveRoomAsync();
    }

    [Fact]
    public async Task LeaveRoomAsync_ResetsRoomState()
    {
        // Arrange
        using var manager = CreateManager();

        // Act
        await manager.LeaveRoomAsync();

        // Assert
        Assert.Null(manager.CurrentRoomId);
        Assert.False(manager.IsInRoom);
    }

    #endregion

    #region Factory Tests

    [Fact]
    public void Constructor_WithNullScaledFactory_UsesDefaultFactory()
    {
        // Arrange & Act
        using var manager = new VoiceRoomManager(_mockClient.Object, null, null);

        // Assert - should not throw and scaled connection should be null initially
        Assert.Null(manager.ScaledConnection);
    }

    [Fact]
    public void Constructor_FactoryNotInvokedUntilTierUpgrade()
    {
        // Arrange
        var factoryInvoked = false;

        // Act
        using var manager = new VoiceRoomManager(
            _mockClient.Object,
            null,
            (roomId) =>
            {
                factoryInvoked = true;
                return _mockScaledConnection.Object;
            });

        // Assert - factory not invoked until tier upgrade event
        Assert.False(factoryInvoked);
        Assert.Null(manager.ScaledConnection);
    }

    #endregion

    #region Peers Tests

    [Fact]
    public void Peers_InitiallyEmpty()
    {
        // Arrange & Act
        using var manager = CreateManager();

        // Assert
        Assert.NotNull(manager.Peers);
        Assert.Empty(manager.Peers);
    }

    #endregion
}
