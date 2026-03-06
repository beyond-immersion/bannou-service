using BeyondImmersion.BannouService.Connect;
using BeyondImmersion.BannouService.Connect.Protocol;
using Moq;
using System.Net.WebSockets;

namespace BeyondImmersion.BannouService.Connect.Tests;

/// <summary>
/// Unit tests for WebSocketConnectionManager.
/// Tests connection lifecycle, message sending, and cleanup behavior.
/// </summary>
public class WebSocketConnectionManagerTests
{
    private readonly WebSocketConnectionManager _manager;

    public WebSocketConnectionManagerTests()
    {
        _manager = new WebSocketConnectionManager();
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_ShouldInitializeWithEmptyConnections()
    {
        // Arrange & Act
        using var manager = new WebSocketConnectionManager();

        // Assert
        Assert.Equal(0, manager.ConnectionCount);
        Assert.Empty(manager.GetActiveSessionIds());
    }

    #endregion

    #region AddConnection Tests

    [Fact]
    public void AddConnection_WithValidParameters_ShouldAddConnection()
    {
        // Arrange
        var sessionId = "test-session-123";
        var mockWebSocket = CreateMockWebSocket(WebSocketState.Open);
        var connectionState = new ConnectionState(sessionId);

        // Act
        _manager.AddConnection(sessionId, mockWebSocket.Object, connectionState);

        // Assert
        Assert.Equal(1, _manager.ConnectionCount);
        Assert.Contains(sessionId, _manager.GetActiveSessionIds());
    }

    [Fact]
    public void AddConnection_WithDuplicateSessionId_ShouldReplaceConnection()
    {
        // Arrange
        var sessionId = "test-session";
        var mockWebSocket1 = CreateMockWebSocket(WebSocketState.Open);
        var mockWebSocket2 = CreateMockWebSocket(WebSocketState.Open);
        var connectionState1 = new ConnectionState(sessionId);
        var connectionState2 = new ConnectionState(sessionId);

        // Act
        _manager.AddConnection(sessionId, mockWebSocket1.Object, connectionState1);
        _manager.AddConnection(sessionId, mockWebSocket2.Object, connectionState2);

        // Assert - should still have only one connection
        Assert.Equal(1, _manager.ConnectionCount);

        // Get the connection and verify it's the second one
        var connection = _manager.GetConnection(sessionId);
        Assert.NotNull(connection);
        Assert.Same(mockWebSocket2.Object, connection.WebSocket);
    }

    [Fact]
    public void AddConnection_MultipleConnections_ShouldTrackAllSessions()
    {
        // Arrange
        var sessionId1 = "session-1";
        var sessionId2 = "session-2";
        var sessionId3 = "session-3";

        // Act
        _manager.AddConnection(sessionId1, CreateMockWebSocket(WebSocketState.Open).Object, new ConnectionState(sessionId1));
        _manager.AddConnection(sessionId2, CreateMockWebSocket(WebSocketState.Open).Object, new ConnectionState(sessionId2));
        _manager.AddConnection(sessionId3, CreateMockWebSocket(WebSocketState.Open).Object, new ConnectionState(sessionId3));

        // Assert
        Assert.Equal(3, _manager.ConnectionCount);
        var sessionIds = _manager.GetActiveSessionIds().ToList();
        Assert.Contains(sessionId1, sessionIds);
        Assert.Contains(sessionId2, sessionIds);
        Assert.Contains(sessionId3, sessionIds);
    }

    #endregion

    #region GetConnection Tests

    [Fact]
    public void GetConnection_WithExistingSession_ShouldReturnConnection()
    {
        // Arrange
        var sessionId = "test-session";
        var mockWebSocket = CreateMockWebSocket(WebSocketState.Open);
        var connectionState = new ConnectionState(sessionId);
        _manager.AddConnection(sessionId, mockWebSocket.Object, connectionState);

        // Act
        var connection = _manager.GetConnection(sessionId);

        // Assert
        Assert.NotNull(connection);
        Assert.Equal(sessionId, connection.SessionId);
        Assert.Same(mockWebSocket.Object, connection.WebSocket);
        Assert.Same(connectionState, connection.ConnectionState);
    }

    [Fact]
    public void GetConnection_WithNonExistingSession_ShouldReturnNull()
    {
        // Arrange
        var sessionId = "non-existent-session";

        // Act
        var connection = _manager.GetConnection(sessionId);

        // Assert
        Assert.Null(connection);
    }

    #endregion

    #region RemoveConnection Tests

    [Fact]
    public void RemoveConnection_WithExistingSession_ShouldRemoveAndReturnTrue()
    {
        // Arrange
        var sessionId = "test-session";
        _manager.AddConnection(sessionId, CreateMockWebSocket(WebSocketState.Open).Object, new ConnectionState(sessionId));

        // Act
        var result = _manager.RemoveConnection(sessionId);

        // Assert
        Assert.True(result);
        Assert.Equal(0, _manager.ConnectionCount);
        Assert.Null(_manager.GetConnection(sessionId));
    }

    [Fact]
    public void RemoveConnection_WithNonExistingSession_ShouldReturnFalse()
    {
        // Arrange
        var sessionId = "non-existent-session";

        // Act
        var result = _manager.RemoveConnection(sessionId);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void RemoveConnection_ShouldOnlyRemoveSpecifiedSession()
    {
        // Arrange
        var sessionId1 = "session-1";
        var sessionId2 = "session-2";
        _manager.AddConnection(sessionId1, CreateMockWebSocket(WebSocketState.Open).Object, new ConnectionState(sessionId1));
        _manager.AddConnection(sessionId2, CreateMockWebSocket(WebSocketState.Open).Object, new ConnectionState(sessionId2));

        // Act
        _manager.RemoveConnection(sessionId1);

        // Assert
        Assert.Equal(1, _manager.ConnectionCount);
        Assert.Null(_manager.GetConnection(sessionId1));
        Assert.NotNull(_manager.GetConnection(sessionId2));
    }

    #endregion

    #region RemoveConnectionIfMatch Tests

    [Fact]
    public void RemoveConnectionIfMatch_WithMatchingWebSocket_ShouldRemoveAndReturnTrue()
    {
        // Arrange
        var sessionId = "test-session";
        var mockWebSocket = CreateMockWebSocket(WebSocketState.Open);
        _manager.AddConnection(sessionId, mockWebSocket.Object, new ConnectionState(sessionId));

        // Act
        var result = _manager.RemoveConnectionIfMatch(sessionId, mockWebSocket.Object);

        // Assert
        Assert.True(result);
        Assert.Null(_manager.GetConnection(sessionId));
    }

    [Fact]
    public void RemoveConnectionIfMatch_WithDifferentWebSocket_ShouldNotRemoveAndReturnFalse()
    {
        // Arrange
        var sessionId = "test-session";
        var mockWebSocket1 = CreateMockWebSocket(WebSocketState.Open);
        var mockWebSocket2 = CreateMockWebSocket(WebSocketState.Open);
        _manager.AddConnection(sessionId, mockWebSocket1.Object, new ConnectionState(sessionId));

        // Act
        var result = _manager.RemoveConnectionIfMatch(sessionId, mockWebSocket2.Object);

        // Assert
        Assert.False(result);
        Assert.NotNull(_manager.GetConnection(sessionId)); // Connection should still exist
    }

    [Fact]
    public void RemoveConnectionIfMatch_WithNonExistingSession_ShouldReturnFalse()
    {
        // Arrange
        var mockWebSocket = CreateMockWebSocket(WebSocketState.Open);

        // Act
        var result = _manager.RemoveConnectionIfMatch("non-existent", mockWebSocket.Object);

        // Assert
        Assert.False(result);
    }

    #endregion

    #region GetActiveSessionIds Tests

    [Fact]
    public void GetActiveSessionIds_WithNoConnections_ShouldReturnEmpty()
    {
        // Act
        var sessionIds = _manager.GetActiveSessionIds();

        // Assert
        Assert.Empty(sessionIds);
    }

    [Fact]
    public void GetActiveSessionIds_WithMultipleConnections_ShouldReturnAllSessionIds()
    {
        // Arrange
        var expectedIds = new[] { "session-1", "session-2", "session-3" };
        foreach (var id in expectedIds)
        {
            _manager.AddConnection(id, CreateMockWebSocket(WebSocketState.Open).Object, new ConnectionState(id));
        }

        // Act
        var sessionIds = _manager.GetActiveSessionIds().ToList();

        // Assert
        Assert.Equal(expectedIds.Length, sessionIds.Count);
        foreach (var id in expectedIds)
        {
            Assert.Contains(id, sessionIds);
        }
    }

    #endregion

    #region ConnectionCount Tests

    [Fact]
    public void ConnectionCount_ShouldReflectCurrentConnectionCount()
    {
        // Assert initial
        Assert.Equal(0, _manager.ConnectionCount);

        // Add connections
        _manager.AddConnection("session-1", CreateMockWebSocket(WebSocketState.Open).Object, new ConnectionState("session-1"));
        Assert.Equal(1, _manager.ConnectionCount);

        _manager.AddConnection("session-2", CreateMockWebSocket(WebSocketState.Open).Object, new ConnectionState("session-2"));
        Assert.Equal(2, _manager.ConnectionCount);

        // Remove connection
        _manager.RemoveConnection("session-1");
        Assert.Equal(1, _manager.ConnectionCount);

        _manager.RemoveConnection("session-2");
        Assert.Equal(0, _manager.ConnectionCount);
    }

    #endregion

    #region SendMessageAsync Tests

    [Fact]
    public async Task SendMessageAsync_WithNonExistingSession_ShouldReturnFalse()
    {
        // Arrange
        var message = CreateTestMessage();

        // Act
        var result = await _manager.SendMessageAsync("non-existent", message);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task SendMessageAsync_WithClosedWebSocket_ShouldReturnFalse()
    {
        // Arrange
        var sessionId = "test-session";
        var mockWebSocket = CreateMockWebSocket(WebSocketState.Closed);
        _manager.AddConnection(sessionId, mockWebSocket.Object, new ConnectionState(sessionId));
        var message = CreateTestMessage();

        // Act
        var result = await _manager.SendMessageAsync(sessionId, message);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task SendMessageAsync_WithOpenWebSocket_ShouldSendAndReturnTrue()
    {
        // Arrange
        var sessionId = "test-session";
        var mockWebSocket = CreateMockWebSocket(WebSocketState.Open);
        _manager.AddConnection(sessionId, mockWebSocket.Object, new ConnectionState(sessionId));
        var message = CreateTestMessage();

        // Act
        var result = await _manager.SendMessageAsync(sessionId, message);

        // Assert
        Assert.True(result);
        mockWebSocket.Verify(ws => ws.SendAsync(
            It.IsAny<ArraySegment<byte>>(),
            WebSocketMessageType.Binary,
            true,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendMessageAsync_WhenWebSocketThrows_ShouldReturnFalseAndRemoveConnection()
    {
        // Arrange
        var sessionId = "test-session";
        var mockWebSocket = CreateMockWebSocket(WebSocketState.Open);
        mockWebSocket
            .Setup(ws => ws.SendAsync(It.IsAny<ArraySegment<byte>>(), It.IsAny<WebSocketMessageType>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new WebSocketException("Connection closed"));

        _manager.AddConnection(sessionId, mockWebSocket.Object, new ConnectionState(sessionId));
        var message = CreateTestMessage();

        // Act
        var result = await _manager.SendMessageAsync(sessionId, message);

        // Assert
        Assert.False(result);
        Assert.Null(_manager.GetConnection(sessionId)); // Connection should be removed
    }

    #endregion

    #region BroadcastMessageAsync Tests

    [Fact]
    public async Task BroadcastMessageAsync_WithNoConnections_ShouldCompleteWithoutError()
    {
        // Arrange
        var message = CreateTestMessage();

        // Act & Assert - should complete without exception
        var exception = await Record.ExceptionAsync(() => _manager.BroadcastMessageAsync(message));
        Assert.Null(exception);
    }

    [Fact]
    public async Task BroadcastMessageAsync_WithMultipleConnections_ShouldSendToAll()
    {
        // Arrange
        var mockWebSocket1 = CreateMockWebSocket(WebSocketState.Open);
        var mockWebSocket2 = CreateMockWebSocket(WebSocketState.Open);
        var mockWebSocket3 = CreateMockWebSocket(WebSocketState.Open);

        _manager.AddConnection("session-1", mockWebSocket1.Object, new ConnectionState("session-1"));
        _manager.AddConnection("session-2", mockWebSocket2.Object, new ConnectionState("session-2"));
        _manager.AddConnection("session-3", mockWebSocket3.Object, new ConnectionState("session-3"));

        var message = CreateTestMessage();

        // Act
        await _manager.BroadcastMessageAsync(message);

        // Assert - each WebSocket should receive the message
        mockWebSocket1.Verify(ws => ws.SendAsync(
            It.IsAny<ArraySegment<byte>>(), WebSocketMessageType.Binary, true, It.IsAny<CancellationToken>()), Times.Once);
        mockWebSocket2.Verify(ws => ws.SendAsync(
            It.IsAny<ArraySegment<byte>>(), WebSocketMessageType.Binary, true, It.IsAny<CancellationToken>()), Times.Once);
        mockWebSocket3.Verify(ws => ws.SendAsync(
            It.IsAny<ArraySegment<byte>>(), WebSocketMessageType.Binary, true, It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region WebSocketConnection Tests

    [Fact]
    public void WebSocketConnection_Constructor_ShouldInitializeProperties()
    {
        // Arrange
        var sessionId = "test-session";
        var mockWebSocket = CreateMockWebSocket(WebSocketState.Open);
        var connectionState = new ConnectionState(sessionId);

        // Act
        var connection = new WebSocketConnection(sessionId, mockWebSocket.Object, connectionState);

        // Assert
        Assert.Equal(sessionId, connection.SessionId);
        Assert.Same(mockWebSocket.Object, connection.WebSocket);
        Assert.Same(connectionState, connection.ConnectionState);
        Assert.NotEqual(default, connection.CreatedAt);
        Assert.NotNull(connection.Metadata);
        Assert.Empty(connection.Metadata);
    }

    [Fact]
    public void WebSocketConnection_Metadata_ShouldAllowSettingValues()
    {
        // Arrange
        var sessionId = "test-session";
        var mockWebSocket = CreateMockWebSocket(WebSocketState.Open);
        var connectionState = new ConnectionState(sessionId);
        var connection = new WebSocketConnection(sessionId, mockWebSocket.Object, connectionState);

        // Act
        connection.Metadata["forced_disconnect"] = true;
        connection.Metadata["disconnect_reason"] = "session_expired";

        // Assert
        Assert.True((bool)connection.Metadata["forced_disconnect"]);
        Assert.Equal("session_expired", connection.Metadata["disconnect_reason"]);
    }

    #endregion

    #region Dispose Tests

    [Fact]
    public void Dispose_WithOpenConnections_ShouldCloseAllConnections()
    {
        // Arrange
        var manager = new WebSocketConnectionManager();
        var mockWebSocket1 = CreateMockWebSocket(WebSocketState.Open);
        var mockWebSocket2 = CreateMockWebSocket(WebSocketState.Open);

        manager.AddConnection("session-1", mockWebSocket1.Object, new ConnectionState("session-1"));
        manager.AddConnection("session-2", mockWebSocket2.Object, new ConnectionState("session-2"));

        // Act
        manager.Dispose();

        // Assert - WebSockets should have CloseAsync called
        mockWebSocket1.Verify(ws => ws.CloseAsync(
            WebSocketCloseStatus.NormalClosure,
            "Server shutdown",
            It.IsAny<CancellationToken>()), Times.Once);
        mockWebSocket2.Verify(ws => ws.CloseAsync(
            WebSocketCloseStatus.NormalClosure,
            "Server shutdown",
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void Dispose_MultipleTimes_ShouldNotThrow()
    {
        // Arrange
        var manager = new WebSocketConnectionManager();

        // Act & Assert - should not throw
        manager.Dispose();
        manager.Dispose();
    }

    #endregion

    #region Helper Methods

    private static Mock<WebSocket> CreateMockWebSocket(WebSocketState state)
    {
        var mock = new Mock<WebSocket>();
        mock.Setup(ws => ws.State).Returns(state);
        mock.Setup(ws => ws.SendAsync(It.IsAny<ArraySegment<byte>>(), It.IsAny<WebSocketMessageType>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        mock.Setup(ws => ws.CloseAsync(It.IsAny<WebSocketCloseStatus>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return mock;
    }

    private static BinaryMessage CreateTestMessage()
    {
        return BinaryMessage.FromJson(
            1, // channel
            0, // sequence
            Guid.NewGuid(),
            GuidGenerator.GenerateMessageId(),
            "{\"test\":\"message\"}");
    }

    #endregion
}
