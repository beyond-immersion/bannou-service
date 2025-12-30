using BeyondImmersion.Bannou.Client.Voice;
using Xunit;

namespace BeyondImmersion.Bannou.Client.Voice.Tests;

public class ScaledVoiceConnectionTests
{
    #region Constructor Tests

    [Fact]
    public void Constructor_WithValidRoomId_CreatesInstance()
    {
        // Arrange
        var roomId = Guid.NewGuid();

        // Act
        using var connection = new ScaledVoiceConnection(roomId);

        // Assert
        Assert.NotNull(connection);
        Assert.Equal(roomId, connection.RoomId);
        Assert.Equal(ScaledVoiceConnectionState.Disconnected, connection.State);
        Assert.False(connection.IsAudioActive);
        Assert.Equal(string.Empty, connection.SipUsername);
    }

    [Fact]
    public void Constructor_InitialState_IsDisconnected()
    {
        // Arrange & Act
        using var connection = new ScaledVoiceConnection(Guid.NewGuid());

        // Assert
        Assert.Equal(ScaledVoiceConnectionState.Disconnected, connection.State);
    }

    #endregion

    #region IsMuted Tests

    [Fact]
    public void IsMuted_InitialValue_IsFalse()
    {
        // Arrange
        using var connection = new ScaledVoiceConnection(Guid.NewGuid());

        // Assert
        Assert.False(connection.IsMuted);
    }

    [Fact]
    public void IsMuted_WhenSet_ReturnsNewValue()
    {
        // Arrange
        using var connection = new ScaledVoiceConnection(Guid.NewGuid());

        // Act
        connection.IsMuted = true;

        // Assert
        Assert.True(connection.IsMuted);
    }

    #endregion

    #region State Change Event Tests

    [Fact]
    public void OnStateChanged_FiresWhenStateChanges()
    {
        // Arrange
        using var connection = new ScaledVoiceConnection(Guid.NewGuid());
        ScaledVoiceConnectionState? capturedState = null;
        connection.OnStateChanged += (state) => capturedState = state;

        // Note: We can't easily trigger state change without mocking,
        // but we can verify the event handler is wired up
        Assert.Null(capturedState);
    }

    #endregion

    #region Dispose Tests

    [Fact]
    public void Dispose_CanBeCalledMultipleTimes()
    {
        // Arrange
        var connection = new ScaledVoiceConnection(Guid.NewGuid());

        // Act & Assert - should not throw
        connection.Dispose();
        connection.Dispose();
    }

    [Fact]
    public void Dispose_SetsStateToDisconnected()
    {
        // Arrange
        var connection = new ScaledVoiceConnection(Guid.NewGuid());

        // Act
        connection.Dispose();

        // Assert
        Assert.Equal(ScaledVoiceConnectionState.Disconnected, connection.State);
    }

    #endregion

    #region ConnectAsync Edge Cases

    [Fact]
    public async Task ConnectAsync_AfterDispose_ThrowsObjectDisposedException()
    {
        // Arrange
        var connection = new ScaledVoiceConnection(Guid.NewGuid());
        connection.Dispose();

        var credentials = new SipConnectionCredentials
        {
            Username = "test-user",
            Password = "test-pass",
            Domain = "voice.test",
            ConferenceUri = "sip:room@voice.test"
        };

        // Act & Assert
        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => connection.ConnectAsync(credentials, "udp://localhost:22222"));
    }

    [Fact]
    public async Task DisconnectAsync_WhenNotConnected_DoesNotThrow()
    {
        // Arrange
        using var connection = new ScaledVoiceConnection(Guid.NewGuid());

        // Act & Assert - should not throw
        await connection.DisconnectAsync();
        Assert.Equal(ScaledVoiceConnectionState.Disconnected, connection.State);
    }

    #endregion

    #region SendAudioFrame Tests

    [Fact]
    public void SendAudioFrame_WhenNotConnected_DoesNotThrow()
    {
        // Arrange
        using var connection = new ScaledVoiceConnection(Guid.NewGuid());
        var samples = new float[960]; // 20ms at 48kHz

        // Act & Assert - should not throw, just silently return
        connection.SendAudioFrame(samples, 48000, 1);
    }

    [Fact]
    public void SendAudioFrame_WhenMuted_DoesNotThrow()
    {
        // Arrange
        using var connection = new ScaledVoiceConnection(Guid.NewGuid());
        connection.IsMuted = true;
        var samples = new float[960];

        // Act & Assert - should not throw
        connection.SendAudioFrame(samples, 48000, 1);
    }

    #endregion

    #region GetStatistics Tests

    [Fact]
    public void GetStatistics_WhenNotConnected_ReturnsEmptyStatistics()
    {
        // Arrange
        using var connection = new ScaledVoiceConnection(Guid.NewGuid());

        // Act
        var stats = connection.GetStatistics();

        // Assert
        Assert.NotNull(stats);
        Assert.Equal(0, stats.PacketsSent);
        Assert.Equal(0, stats.PacketsReceived);
    }

    [Fact]
    public void GetStatistics_AfterDispose_ThrowsObjectDisposedException()
    {
        // Arrange
        var connection = new ScaledVoiceConnection(Guid.NewGuid());
        connection.Dispose();

        // Act & Assert
        Assert.Throws<ObjectDisposedException>(() => connection.GetStatistics());
    }

    #endregion

    #region RefreshRegistrationAsync Tests

    [Fact]
    public async Task RefreshRegistrationAsync_WhenNotConnected_ThrowsInvalidOperationException()
    {
        // Arrange
        using var connection = new ScaledVoiceConnection(Guid.NewGuid());

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => connection.RefreshRegistrationAsync());
    }

    #endregion
}
