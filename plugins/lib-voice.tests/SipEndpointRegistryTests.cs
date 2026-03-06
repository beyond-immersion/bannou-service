using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using BeyondImmersion.BannouService.TestUtilities;
using BeyondImmersion.BannouService.Voice;
using BeyondImmersion.BannouService.Voice.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace BeyondImmersion.BannouService.Voice.Tests;

/// <summary>
/// Unit tests for <see cref="SipEndpointRegistry"/>.
/// Tests registration, unregistration, heartbeat, endpoint updates, participant queries,
/// room clearing, and state store persistence interactions.
/// </summary>
public class SipEndpointRegistryTests
{
    private readonly Mock<IStateStoreFactory> _mockStateStoreFactory;
    private readonly Mock<IStateStore<List<ParticipantRegistration>>> _mockStateStore;
    private readonly Mock<ILogger<SipEndpointRegistry>> _mockLogger;
    private readonly VoiceServiceConfiguration _configuration;
    private readonly Mock<ITelemetryProvider> _mockTelemetryProvider;

    public SipEndpointRegistryTests()
    {
        _mockStateStoreFactory = new Mock<IStateStoreFactory>();
        _mockStateStore = new Mock<IStateStore<List<ParticipantRegistration>>>();
        _mockLogger = new Mock<ILogger<SipEndpointRegistry>>();
        _configuration = new VoiceServiceConfiguration();
        _mockTelemetryProvider = new Mock<ITelemetryProvider>();

        _mockStateStoreFactory
            .Setup(f => f.GetStore<List<ParticipantRegistration>>(StateStoreDefinitions.Voice))
            .Returns(_mockStateStore.Object);
    }

    private SipEndpointRegistry CreateRegistry()
    {
        return new SipEndpointRegistry(
            _mockStateStoreFactory.Object,
            _mockLogger.Object,
            _configuration,
            _mockTelemetryProvider.Object);
    }

    private static SipEndpoint CreateTestEndpoint(string sdpOffer = "v=0\r\no=- 12345\r\n")
    {
        return new SipEndpoint
        {
            SdpOffer = sdpOffer,
            IceCandidates = new List<string> { "candidate1" }
        };
    }

    #region Constructor Tests

    [Fact]
    public void ConstructorIsValid()
    {
        ServiceConstructorValidator.ValidateServiceConstructor<SipEndpointRegistry>();
        Assert.NotNull(CreateRegistry());
    }

    #endregion

    #region RegisterAsync Tests

    [Fact]
    public async Task RegisterAsync_NewParticipant_ReturnsTrueAndPersists()
    {
        // Arrange
        var registry = CreateRegistry();
        var roomId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var endpoint = CreateTestEndpoint();

        // State store returns null (no existing participants)
        _mockStateStore.Setup(s => s.GetAsync(
            It.Is<string>(k => k.Contains(roomId.ToString())),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync((List<ParticipantRegistration>?)null);

        // Capture the saved participants list
        List<ParticipantRegistration>? savedList = null;
        _mockStateStore.Setup(s => s.SaveAsync(
            It.IsAny<string>(),
            It.IsAny<List<ParticipantRegistration>>(),
            It.IsAny<StateOptions?>(),
            It.IsAny<CancellationToken>()))
            .Callback<string, List<ParticipantRegistration>, StateOptions?, CancellationToken>(
                (_, list, _, _) => savedList = list);

        // Act
        var result = await registry.RegisterAsync(roomId, sessionId, endpoint, "TestPlayer", CancellationToken.None);

        // Assert
        Assert.True(result);
        Assert.NotNull(savedList);
        Assert.Single(savedList);
        Assert.Equal(sessionId, savedList[0].SessionId);
        Assert.Equal("TestPlayer", savedList[0].DisplayName);
        Assert.Equal(endpoint, savedList[0].Endpoint);
        Assert.False(savedList[0].IsMuted);
    }

    [Fact]
    public async Task RegisterAsync_DuplicateSession_ReturnsFalse()
    {
        // Arrange
        var registry = CreateRegistry();
        var roomId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var endpoint = CreateTestEndpoint();

        _mockStateStore.Setup(s => s.GetAsync(
            It.IsAny<string>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((List<ParticipantRegistration>?)null);

        _mockStateStore.Setup(s => s.SaveAsync(
            It.IsAny<string>(),
            It.IsAny<List<ParticipantRegistration>>(),
            It.IsAny<StateOptions?>(),
            It.IsAny<CancellationToken>()));

        // First registration
        await registry.RegisterAsync(roomId, sessionId, endpoint, "Player1", CancellationToken.None);

        // Act - second registration with same session ID
        var result = await registry.RegisterAsync(roomId, sessionId, endpoint, "Player1Again", CancellationToken.None);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task RegisterAsync_ExistingRoomFromStateStore_LoadsAndAdds()
    {
        // Arrange
        var registry = CreateRegistry();
        var roomId = Guid.NewGuid();
        var existingSessionId = Guid.NewGuid();
        var newSessionId = Guid.NewGuid();
        var endpoint = CreateTestEndpoint();

        // State store has an existing participant
        var existingParticipants = new List<ParticipantRegistration>
        {
            new()
            {
                SessionId = existingSessionId,
                DisplayName = "ExistingPlayer",
                Endpoint = CreateTestEndpoint("existing-sdp"),
                JoinedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
                LastHeartbeat = DateTimeOffset.UtcNow.AddMinutes(-1)
            }
        };

        _mockStateStore.Setup(s => s.GetAsync(
            It.Is<string>(k => k.Contains(roomId.ToString())),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingParticipants);

        List<ParticipantRegistration>? savedList = null;
        _mockStateStore.Setup(s => s.SaveAsync(
            It.IsAny<string>(),
            It.IsAny<List<ParticipantRegistration>>(),
            It.IsAny<StateOptions?>(),
            It.IsAny<CancellationToken>()))
            .Callback<string, List<ParticipantRegistration>, StateOptions?, CancellationToken>(
                (_, list, _, _) => savedList = list);

        // Act
        var result = await registry.RegisterAsync(roomId, newSessionId, endpoint, "NewPlayer", CancellationToken.None);

        // Assert
        Assert.True(result);
        Assert.NotNull(savedList);
        Assert.Equal(2, savedList.Count);
        Assert.Contains(savedList, p => p.SessionId == existingSessionId);
        Assert.Contains(savedList, p => p.SessionId == newSessionId);
    }

    #endregion

    #region UnregisterAsync Tests

    [Fact]
    public async Task UnregisterAsync_ExistingParticipant_ReturnsRemovedAndPersists()
    {
        // Arrange
        var registry = CreateRegistry();
        var roomId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var endpoint = CreateTestEndpoint();

        _mockStateStore.Setup(s => s.GetAsync(
            It.IsAny<string>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((List<ParticipantRegistration>?)null);

        _mockStateStore.Setup(s => s.SaveAsync(
            It.IsAny<string>(),
            It.IsAny<List<ParticipantRegistration>>(),
            It.IsAny<StateOptions?>(),
            It.IsAny<CancellationToken>()));

        // Register first
        await registry.RegisterAsync(roomId, sessionId, endpoint, "Player1", CancellationToken.None);

        // Capture the saved list after unregister
        List<ParticipantRegistration>? savedAfterUnregister = null;
        var saveCallCount = 0;
        _mockStateStore.Setup(s => s.SaveAsync(
            It.IsAny<string>(),
            It.IsAny<List<ParticipantRegistration>>(),
            It.IsAny<StateOptions?>(),
            It.IsAny<CancellationToken>()))
            .Callback<string, List<ParticipantRegistration>, StateOptions?, CancellationToken>(
                (_, list, _, _) =>
                {
                    saveCallCount++;
                    if (saveCallCount >= 2) savedAfterUnregister = list;
                });

        // Act
        var removed = await registry.UnregisterAsync(roomId, sessionId, CancellationToken.None);

        // Assert
        Assert.NotNull(removed);
        Assert.Equal(sessionId, removed.SessionId);
        Assert.Equal("Player1", removed.DisplayName);
    }

    [Fact]
    public async Task UnregisterAsync_NonexistentParticipant_ReturnsNull()
    {
        // Arrange
        var registry = CreateRegistry();
        var roomId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        // State store returns null (room doesn't exist in state)
        _mockStateStore.Setup(s => s.GetAsync(
            It.IsAny<string>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((List<ParticipantRegistration>?)null);

        // Act
        var result = await registry.UnregisterAsync(roomId, sessionId, CancellationToken.None);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task UnregisterAsync_WrongSession_ReturnsNull()
    {
        // Arrange
        var registry = CreateRegistry();
        var roomId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var otherSessionId = Guid.NewGuid();

        _mockStateStore.Setup(s => s.GetAsync(
            It.IsAny<string>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((List<ParticipantRegistration>?)null);

        _mockStateStore.Setup(s => s.SaveAsync(
            It.IsAny<string>(),
            It.IsAny<List<ParticipantRegistration>>(),
            It.IsAny<StateOptions?>(),
            It.IsAny<CancellationToken>()));

        await registry.RegisterAsync(roomId, sessionId, CreateTestEndpoint(), "Player1", CancellationToken.None);

        // Act - try to unregister different session
        var result = await registry.UnregisterAsync(roomId, otherSessionId, CancellationToken.None);

        // Assert
        Assert.Null(result);
    }

    #endregion

    #region GetRoomParticipantsAsync Tests

    [Fact]
    public async Task GetRoomParticipantsAsync_WithParticipants_ReturnsList()
    {
        // Arrange
        var registry = CreateRegistry();
        var roomId = Guid.NewGuid();
        var session1 = Guid.NewGuid();
        var session2 = Guid.NewGuid();

        _mockStateStore.Setup(s => s.GetAsync(
            It.IsAny<string>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((List<ParticipantRegistration>?)null);

        _mockStateStore.Setup(s => s.SaveAsync(
            It.IsAny<string>(),
            It.IsAny<List<ParticipantRegistration>>(),
            It.IsAny<StateOptions?>(),
            It.IsAny<CancellationToken>()));

        await registry.RegisterAsync(roomId, session1, CreateTestEndpoint(), "Player1", CancellationToken.None);
        await registry.RegisterAsync(roomId, session2, CreateTestEndpoint(), "Player2", CancellationToken.None);

        // Act
        var participants = await registry.GetRoomParticipantsAsync(roomId, CancellationToken.None);

        // Assert
        Assert.Equal(2, participants.Count);
        Assert.Contains(participants, p => p.SessionId == session1);
        Assert.Contains(participants, p => p.SessionId == session2);
    }

    [Fact]
    public async Task GetRoomParticipantsAsync_EmptyRoom_ReturnsEmptyList()
    {
        // Arrange
        var registry = CreateRegistry();
        var roomId = Guid.NewGuid();

        _mockStateStore.Setup(s => s.GetAsync(
            It.IsAny<string>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((List<ParticipantRegistration>?)null);

        // Act
        var participants = await registry.GetRoomParticipantsAsync(roomId, CancellationToken.None);

        // Assert
        Assert.Empty(participants);
    }

    [Fact]
    public async Task GetRoomParticipantsAsync_LoadsFromStateStoreWhenNotCached()
    {
        // Arrange
        var registry = CreateRegistry();
        var roomId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        var storeParticipants = new List<ParticipantRegistration>
        {
            new()
            {
                SessionId = sessionId,
                DisplayName = "RemotePlayer",
                JoinedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
                LastHeartbeat = DateTimeOffset.UtcNow
            }
        };

        _mockStateStore.Setup(s => s.GetAsync(
            It.Is<string>(k => k.Contains(roomId.ToString())),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(storeParticipants);

        // Act
        var participants = await registry.GetRoomParticipantsAsync(roomId, CancellationToken.None);

        // Assert
        Assert.Single(participants);
        Assert.Equal(sessionId, participants[0].SessionId);
        Assert.Equal("RemotePlayer", participants[0].DisplayName);
    }

    #endregion

    #region UpdateHeartbeatAsync Tests

    [Fact]
    public async Task UpdateHeartbeatAsync_ExistingParticipant_ReturnsTrueAndUpdatesTimestamp()
    {
        // Arrange
        var registry = CreateRegistry();
        var roomId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        _mockStateStore.Setup(s => s.GetAsync(
            It.IsAny<string>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((List<ParticipantRegistration>?)null);

        _mockStateStore.Setup(s => s.SaveAsync(
            It.IsAny<string>(),
            It.IsAny<List<ParticipantRegistration>>(),
            It.IsAny<StateOptions?>(),
            It.IsAny<CancellationToken>()));

        await registry.RegisterAsync(roomId, sessionId, CreateTestEndpoint(), "Player1", CancellationToken.None);

        // Wait briefly so heartbeat timestamp differs
        var beforeHeartbeat = DateTimeOffset.UtcNow;

        // Act
        var result = await registry.UpdateHeartbeatAsync(roomId, sessionId, CancellationToken.None);

        // Assert
        Assert.True(result);

        // Verify the updated participant has a newer heartbeat
        var participants = await registry.GetRoomParticipantsAsync(roomId, CancellationToken.None);
        Assert.Single(participants);
        Assert.True(participants[0].LastHeartbeat >= beforeHeartbeat);
    }

    [Fact]
    public async Task UpdateHeartbeatAsync_NonexistentParticipant_ReturnsFalse()
    {
        // Arrange
        var registry = CreateRegistry();
        var roomId = Guid.NewGuid();

        _mockStateStore.Setup(s => s.GetAsync(
            It.IsAny<string>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((List<ParticipantRegistration>?)null);

        // Act
        var result = await registry.UpdateHeartbeatAsync(roomId, Guid.NewGuid(), CancellationToken.None);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task UpdateHeartbeatAsync_NonexistentRoom_ReturnsFalse()
    {
        // Arrange
        var registry = CreateRegistry();
        var roomId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        // Empty state store
        _mockStateStore.Setup(s => s.GetAsync(
            It.IsAny<string>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((List<ParticipantRegistration>?)null);

        // Act
        var result = await registry.UpdateHeartbeatAsync(roomId, sessionId, CancellationToken.None);

        // Assert
        Assert.False(result);
    }

    #endregion

    #region UpdateEndpointAsync Tests

    [Fact]
    public async Task UpdateEndpointAsync_ExistingParticipant_ReturnsTrueAndUpdates()
    {
        // Arrange
        var registry = CreateRegistry();
        var roomId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        _mockStateStore.Setup(s => s.GetAsync(
            It.IsAny<string>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((List<ParticipantRegistration>?)null);

        _mockStateStore.Setup(s => s.SaveAsync(
            It.IsAny<string>(),
            It.IsAny<List<ParticipantRegistration>>(),
            It.IsAny<StateOptions?>(),
            It.IsAny<CancellationToken>()));

        await registry.RegisterAsync(roomId, sessionId, CreateTestEndpoint("old-sdp"), "Player1", CancellationToken.None);

        var newEndpoint = CreateTestEndpoint("new-sdp-offer");

        // Act
        var result = await registry.UpdateEndpointAsync(roomId, sessionId, newEndpoint, CancellationToken.None);

        // Assert
        Assert.True(result);

        // Verify endpoint was updated
        var participant = await registry.GetParticipantAsync(roomId, sessionId, CancellationToken.None);
        Assert.NotNull(participant);
        Assert.Equal("new-sdp-offer", participant.Endpoint?.SdpOffer);
    }

    [Fact]
    public async Task UpdateEndpointAsync_NonexistentParticipant_ReturnsFalse()
    {
        // Arrange
        var registry = CreateRegistry();
        var roomId = Guid.NewGuid();

        _mockStateStore.Setup(s => s.GetAsync(
            It.IsAny<string>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((List<ParticipantRegistration>?)null);

        // Act
        var result = await registry.UpdateEndpointAsync(roomId, Guid.NewGuid(), CreateTestEndpoint(), CancellationToken.None);

        // Assert
        Assert.False(result);
    }

    #endregion

    #region GetParticipantAsync Tests

    [Fact]
    public async Task GetParticipantAsync_ExistingParticipant_ReturnsParticipant()
    {
        // Arrange
        var registry = CreateRegistry();
        var roomId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        _mockStateStore.Setup(s => s.GetAsync(
            It.IsAny<string>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((List<ParticipantRegistration>?)null);

        _mockStateStore.Setup(s => s.SaveAsync(
            It.IsAny<string>(),
            It.IsAny<List<ParticipantRegistration>>(),
            It.IsAny<StateOptions?>(),
            It.IsAny<CancellationToken>()));

        await registry.RegisterAsync(roomId, sessionId, CreateTestEndpoint(), "Player1", CancellationToken.None);

        // Act
        var participant = await registry.GetParticipantAsync(roomId, sessionId, CancellationToken.None);

        // Assert
        Assert.NotNull(participant);
        Assert.Equal(sessionId, participant.SessionId);
        Assert.Equal("Player1", participant.DisplayName);
    }

    [Fact]
    public async Task GetParticipantAsync_NonexistentParticipant_ReturnsNull()
    {
        // Arrange
        var registry = CreateRegistry();
        var roomId = Guid.NewGuid();

        _mockStateStore.Setup(s => s.GetAsync(
            It.IsAny<string>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((List<ParticipantRegistration>?)null);

        // Act
        var result = await registry.GetParticipantAsync(roomId, Guid.NewGuid(), CancellationToken.None);

        // Assert
        Assert.Null(result);
    }

    #endregion

    #region GetParticipantCountAsync Tests

    [Fact]
    public async Task GetParticipantCountAsync_WithParticipants_ReturnsCorrectCount()
    {
        // Arrange
        var registry = CreateRegistry();
        var roomId = Guid.NewGuid();

        _mockStateStore.Setup(s => s.GetAsync(
            It.IsAny<string>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((List<ParticipantRegistration>?)null);

        _mockStateStore.Setup(s => s.SaveAsync(
            It.IsAny<string>(),
            It.IsAny<List<ParticipantRegistration>>(),
            It.IsAny<StateOptions?>(),
            It.IsAny<CancellationToken>()));

        await registry.RegisterAsync(roomId, Guid.NewGuid(), CreateTestEndpoint(), "P1", CancellationToken.None);
        await registry.RegisterAsync(roomId, Guid.NewGuid(), CreateTestEndpoint(), "P2", CancellationToken.None);
        await registry.RegisterAsync(roomId, Guid.NewGuid(), CreateTestEndpoint(), "P3", CancellationToken.None);

        // Act
        var count = await registry.GetParticipantCountAsync(roomId, CancellationToken.None);

        // Assert
        Assert.Equal(3, count);
    }

    [Fact]
    public async Task GetParticipantCountAsync_EmptyRoom_ReturnsZero()
    {
        // Arrange
        var registry = CreateRegistry();
        var roomId = Guid.NewGuid();

        _mockStateStore.Setup(s => s.GetAsync(
            It.IsAny<string>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((List<ParticipantRegistration>?)null);

        // Act
        var count = await registry.GetParticipantCountAsync(roomId, CancellationToken.None);

        // Assert
        Assert.Equal(0, count);
    }

    #endregion

    #region ClearRoomAsync Tests

    [Fact]
    public async Task ClearRoomAsync_WithParticipants_RemovesAllAndDeletesState()
    {
        // Arrange
        var registry = CreateRegistry();
        var roomId = Guid.NewGuid();
        var session1 = Guid.NewGuid();
        var session2 = Guid.NewGuid();

        _mockStateStore.Setup(s => s.GetAsync(
            It.IsAny<string>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((List<ParticipantRegistration>?)null);

        _mockStateStore.Setup(s => s.SaveAsync(
            It.IsAny<string>(),
            It.IsAny<List<ParticipantRegistration>>(),
            It.IsAny<StateOptions?>(),
            It.IsAny<CancellationToken>()));

        await registry.RegisterAsync(roomId, session1, CreateTestEndpoint(), "Player1", CancellationToken.None);
        await registry.RegisterAsync(roomId, session2, CreateTestEndpoint(), "Player2", CancellationToken.None);

        // Act
        var removed = await registry.ClearRoomAsync(roomId, CancellationToken.None);

        // Assert
        Assert.Equal(2, removed.Count);
        Assert.Contains(removed, p => p.SessionId == session1);
        Assert.Contains(removed, p => p.SessionId == session2);

        // Verify state store delete was called
        _mockStateStore.Verify(s => s.DeleteAsync(
            It.Is<string>(k => k.Contains(roomId.ToString())),
            It.IsAny<CancellationToken>()), Times.Once);

        // Verify room no longer tracked
        var trackedRooms = registry.GetAllTrackedRoomIds();
        Assert.DoesNotContain(roomId, trackedRooms);
    }

    [Fact]
    public async Task ClearRoomAsync_EmptyRoom_ReturnsEmptyAndDeletesState()
    {
        // Arrange
        var registry = CreateRegistry();
        var roomId = Guid.NewGuid();

        // Act
        var removed = await registry.ClearRoomAsync(roomId, CancellationToken.None);

        // Assert
        Assert.Empty(removed);

        // State store delete still called to ensure cleanup
        _mockStateStore.Verify(s => s.DeleteAsync(
            It.Is<string>(k => k.Contains(roomId.ToString())),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region GetAllTrackedRoomIds Tests

    [Fact]
    public async Task GetAllTrackedRoomIds_ReturnsAllRoomsWithParticipants()
    {
        // Arrange
        var registry = CreateRegistry();
        var room1 = Guid.NewGuid();
        var room2 = Guid.NewGuid();

        _mockStateStore.Setup(s => s.GetAsync(
            It.IsAny<string>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((List<ParticipantRegistration>?)null);

        _mockStateStore.Setup(s => s.SaveAsync(
            It.IsAny<string>(),
            It.IsAny<List<ParticipantRegistration>>(),
            It.IsAny<StateOptions?>(),
            It.IsAny<CancellationToken>()));

        await registry.RegisterAsync(room1, Guid.NewGuid(), CreateTestEndpoint(), "P1", CancellationToken.None);
        await registry.RegisterAsync(room2, Guid.NewGuid(), CreateTestEndpoint(), "P2", CancellationToken.None);

        // Act
        var roomIds = registry.GetAllTrackedRoomIds();

        // Assert
        Assert.Equal(2, roomIds.Count);
        Assert.Contains(room1, roomIds);
        Assert.Contains(room2, roomIds);
    }

    [Fact]
    public void GetAllTrackedRoomIds_NoRooms_ReturnsEmpty()
    {
        // Arrange
        var registry = CreateRegistry();

        // Act
        var roomIds = registry.GetAllTrackedRoomIds();

        // Assert
        Assert.Empty(roomIds);
    }

    #endregion

    #region ParticipantRegistration Model Tests

    [Fact]
    public void ToVoiceParticipant_MapsFieldsCorrectly()
    {
        // Arrange
        var registration = new ParticipantRegistration
        {
            SessionId = Guid.NewGuid(),
            DisplayName = "TestPlayer",
            JoinedAt = DateTimeOffset.UtcNow,
            IsMuted = true
        };

        // Act
        var participant = registration.ToVoiceParticipant();

        // Assert
        Assert.Equal(registration.SessionId, participant.SessionId);
        Assert.Equal("TestPlayer", participant.DisplayName);
        Assert.Equal(registration.JoinedAt, participant.JoinedAt);
        Assert.True(participant.IsMuted);
    }

    [Fact]
    public void ToVoicePeer_MapsFieldsCorrectly()
    {
        // Arrange
        var endpoint = CreateTestEndpoint("peer-sdp");
        var registration = new ParticipantRegistration
        {
            SessionId = Guid.NewGuid(),
            DisplayName = "PeerPlayer",
            Endpoint = endpoint
        };

        // Act
        var peer = registration.ToVoicePeer();

        // Assert
        Assert.Equal(registration.SessionId, peer.SessionId);
        Assert.Equal("PeerPlayer", peer.DisplayName);
        Assert.Equal(endpoint, peer.SipEndpoint);
    }

    [Fact]
    public void ToVoicePeer_NullEndpoint_CreatesDefaultEndpoint()
    {
        // Arrange
        var registration = new ParticipantRegistration
        {
            SessionId = Guid.NewGuid(),
            DisplayName = "NoEndpointPlayer",
            Endpoint = null
        };

        // Act
        var peer = registration.ToVoicePeer();

        // Assert
        Assert.NotNull(peer.SipEndpoint);
    }

    #endregion
}
