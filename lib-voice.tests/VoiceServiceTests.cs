using BeyondImmersion.Bannou.Voice.ClientEvents;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.ClientEvents;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.Voice;
using BeyondImmersion.BannouService.Voice.Services;
using Dapr.Client;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

// Alias to disambiguate from VoicePeerInfo in Voice.ClientEvents namespace
using GameSessionPeerInfo = BeyondImmersion.BannouService.GameSession.VoicePeerInfo;

namespace BeyondImmersion.BannouService.Voice.Tests;

public class VoiceServiceTests
{
    private readonly Mock<DaprClient> _mockDaprClient;
    private readonly Mock<ILogger<VoiceService>> _mockLogger;
    private readonly Mock<VoiceServiceConfiguration> _mockConfiguration;
    private readonly Mock<IErrorEventEmitter> _mockErrorEventEmitter;
    private readonly Mock<ISipEndpointRegistry> _mockEndpointRegistry;
    private readonly Mock<IP2PCoordinator> _mockP2PCoordinator;
    private readonly Mock<IClientEventPublisher> _mockClientEventPublisher;

    public VoiceServiceTests()
    {
        _mockDaprClient = new Mock<DaprClient>();
        _mockLogger = new Mock<ILogger<VoiceService>>();
        _mockConfiguration = new Mock<VoiceServiceConfiguration>();
        _mockErrorEventEmitter = new Mock<IErrorEventEmitter>();
        _mockEndpointRegistry = new Mock<ISipEndpointRegistry>();
        _mockP2PCoordinator = new Mock<IP2PCoordinator>();
        _mockClientEventPublisher = new Mock<IClientEventPublisher>();

        // Default P2P max participants
        _mockP2PCoordinator.Setup(p => p.GetP2PMaxParticipants()).Returns(6);
    }

    private VoiceService CreateService()
    {
        return new VoiceService(
            _mockDaprClient.Object,
            _mockLogger.Object,
            _mockConfiguration.Object,
            _mockErrorEventEmitter.Object,
            _mockEndpointRegistry.Object,
            _mockP2PCoordinator.Object,
            _mockClientEventPublisher.Object);
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithValidParameters_ShouldNotThrow()
    {
        // Arrange & Act
        var service = CreateService();

        // Assert
        Assert.NotNull(service);
    }

    [Fact]
    public void Constructor_WithNullDaprClient_ShouldThrowArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => new VoiceService(
            null!,
            _mockLogger.Object,
            _mockConfiguration.Object,
            _mockErrorEventEmitter.Object,
            _mockEndpointRegistry.Object,
            _mockP2PCoordinator.Object,
            _mockClientEventPublisher.Object));
    }

    [Fact]
    public void Constructor_WithNullLogger_ShouldThrowArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => new VoiceService(
            _mockDaprClient.Object,
            null!,
            _mockConfiguration.Object,
            _mockErrorEventEmitter.Object,
            _mockEndpointRegistry.Object,
            _mockP2PCoordinator.Object,
            _mockClientEventPublisher.Object));
    }

    [Fact]
    public void Constructor_WithNullConfiguration_ShouldThrowArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => new VoiceService(
            _mockDaprClient.Object,
            _mockLogger.Object,
            null!,
            _mockErrorEventEmitter.Object,
            _mockEndpointRegistry.Object,
            _mockP2PCoordinator.Object,
            _mockClientEventPublisher.Object));
    }

    [Fact]
    public void Constructor_WithNullErrorEventEmitter_ShouldThrowArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => new VoiceService(
            _mockDaprClient.Object,
            _mockLogger.Object,
            _mockConfiguration.Object,
            null!,
            _mockEndpointRegistry.Object,
            _mockP2PCoordinator.Object,
            _mockClientEventPublisher.Object));
    }

    [Fact]
    public void Constructor_WithNullEndpointRegistry_ShouldThrowArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => new VoiceService(
            _mockDaprClient.Object,
            _mockLogger.Object,
            _mockConfiguration.Object,
            _mockErrorEventEmitter.Object,
            null!,
            _mockP2PCoordinator.Object,
            _mockClientEventPublisher.Object));
    }

    [Fact]
    public void Constructor_WithNullP2PCoordinator_ShouldThrowArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => new VoiceService(
            _mockDaprClient.Object,
            _mockLogger.Object,
            _mockConfiguration.Object,
            _mockErrorEventEmitter.Object,
            _mockEndpointRegistry.Object,
            null!,
            _mockClientEventPublisher.Object));
    }

    [Fact]
    public void Constructor_WithNullClientEventPublisher_ShouldSucceed()
    {
        // Arrange, Act - IClientEventPublisher is optional per Tenet 5 (context-dependent)
        var service = new VoiceService(
            _mockDaprClient.Object,
            _mockLogger.Object,
            _mockConfiguration.Object,
            _mockErrorEventEmitter.Object,
            _mockEndpointRegistry.Object,
            _mockP2PCoordinator.Object,
            null);

        // Assert - Service should be created successfully
        Assert.NotNull(service);
    }

    #endregion

    #region CreateVoiceRoomAsync Tests

    [Fact]
    public async Task CreateVoiceRoom_WhenSessionHasNoRoom_ReturnsCreatedWithRoom()
    {
        // Arrange
        var service = CreateService();
        var sessionId = Guid.NewGuid();
        var request = new CreateVoiceRoomRequest
        {
            SessionId = sessionId,
            PreferredTier = VoiceTier.P2p,
            Codec = VoiceCodec.Opus,
            MaxParticipants = 6
        };

        // No existing room
        _mockDaprClient.Setup(d => d.GetStateAsync<Guid?>(
            "voice-statestore",
            $"voice:session-room:{sessionId}",
            It.IsAny<ConsistencyMode?>(),
            It.IsAny<IReadOnlyDictionary<string, string>?>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid?)null);

        // Act
        var (status, result) = await service.CreateVoiceRoomAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.Created, status);
        Assert.NotNull(result);
        Assert.Equal(sessionId, result.SessionId);
        Assert.Equal(VoiceTier.P2p, result.Tier);
        Assert.Equal(VoiceCodec.Opus, result.Codec);
        Assert.Equal(6, result.MaxParticipants);
        Assert.Equal(0, result.CurrentParticipants);

        // Verify state was saved
        _mockDaprClient.Verify(d => d.SaveStateAsync(
            "voice-statestore",
            It.Is<string>(k => k.StartsWith("voice:room:")),
            It.IsAny<VoiceRoomData>(),
            It.IsAny<StateOptions?>(),
            It.IsAny<IReadOnlyDictionary<string, string>?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateVoiceRoom_WhenSessionAlreadyHasRoom_ReturnsConflict()
    {
        // Arrange
        var service = CreateService();
        var sessionId = Guid.NewGuid();
        var existingRoomId = Guid.NewGuid();
        var request = new CreateVoiceRoomRequest
        {
            SessionId = sessionId,
            PreferredTier = VoiceTier.P2p,
            Codec = VoiceCodec.Opus,
            MaxParticipants = 6
        };

        // Existing room
        _mockDaprClient.Setup(d => d.GetStateAsync<Guid?>(
            "voice-statestore",
            $"voice:session-room:{sessionId}",
            It.IsAny<ConsistencyMode?>(),
            It.IsAny<IReadOnlyDictionary<string, string>?>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingRoomId);

        // Act
        var (status, result) = await service.CreateVoiceRoomAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.Conflict, status);
        Assert.Null(result);
    }

    [Fact]
    public async Task CreateVoiceRoom_WhenMaxParticipantsZero_UsesDefaultFromP2PCoordinator()
    {
        // Arrange
        var service = CreateService();
        var sessionId = Guid.NewGuid();
        var request = new CreateVoiceRoomRequest
        {
            SessionId = sessionId,
            PreferredTier = VoiceTier.P2p,
            Codec = VoiceCodec.Opus,
            MaxParticipants = 0 // Zero means use default
        };

        _mockDaprClient.Setup(d => d.GetStateAsync<Guid?>(
            "voice-statestore",
            $"voice:session-room:{sessionId}",
            It.IsAny<ConsistencyMode?>(),
            It.IsAny<IReadOnlyDictionary<string, string>?>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid?)null);

        _mockP2PCoordinator.Setup(p => p.GetP2PMaxParticipants()).Returns(8);

        // Act
        var (status, result) = await service.CreateVoiceRoomAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.Created, status);
        Assert.NotNull(result);
        Assert.Equal(8, result.MaxParticipants);
    }

    #endregion

    #region GetVoiceRoomAsync Tests

    [Fact]
    public async Task GetVoiceRoom_WhenRoomExists_ReturnsOkWithRoom()
    {
        // Arrange
        var service = CreateService();
        var roomId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var request = new GetVoiceRoomRequest { RoomId = roomId };

        var roomData = new VoiceRoomData
        {
            RoomId = roomId,
            SessionId = sessionId,
            Tier = "p2p",
            Codec = "opus",
            MaxParticipants = 6,
            CreatedAt = DateTimeOffset.UtcNow
        };

        _mockDaprClient.Setup(d => d.GetStateAsync<VoiceRoomData>(
            "voice-statestore",
            $"voice:room:{roomId}",
            It.IsAny<ConsistencyMode?>(),
            It.IsAny<IReadOnlyDictionary<string, string>?>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(roomData);

        _mockEndpointRegistry.Setup(r => r.GetRoomParticipantsAsync(roomId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ParticipantRegistration>());

        // Act
        var (status, result) = await service.GetVoiceRoomAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(result);
        Assert.Equal(roomId, result.RoomId);
        Assert.Equal(sessionId, result.SessionId);
        Assert.Equal(VoiceTier.P2p, result.Tier);
        Assert.Equal(VoiceCodec.Opus, result.Codec);
    }

    [Fact]
    public async Task GetVoiceRoom_WhenRoomNotFound_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        var roomId = Guid.NewGuid();
        var request = new GetVoiceRoomRequest { RoomId = roomId };

        _mockDaprClient.Setup(d => d.GetStateAsync<VoiceRoomData>(
            "voice-statestore",
            $"voice:room:{roomId}",
            It.IsAny<ConsistencyMode?>(),
            It.IsAny<IReadOnlyDictionary<string, string>?>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync((VoiceRoomData?)null);

        // Act
        var (status, result) = await service.GetVoiceRoomAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(result);
    }

    [Fact]
    public async Task GetVoiceRoom_WhenRoomHasParticipants_ReturnsCorrectCount()
    {
        // Arrange
        var service = CreateService();
        var roomId = Guid.NewGuid();
        var request = new GetVoiceRoomRequest { RoomId = roomId };

        var roomData = new VoiceRoomData
        {
            RoomId = roomId,
            SessionId = Guid.NewGuid(),
            Tier = "p2p",
            Codec = "opus",
            MaxParticipants = 6,
            CreatedAt = DateTimeOffset.UtcNow
        };

        var participants = new List<ParticipantRegistration>
        {
            new() { AccountId = Guid.NewGuid(), DisplayName = "Player1", JoinedAt = DateTimeOffset.UtcNow },
            new() { AccountId = Guid.NewGuid(), DisplayName = "Player2", JoinedAt = DateTimeOffset.UtcNow }
        };

        _mockDaprClient.Setup(d => d.GetStateAsync<VoiceRoomData>(
            "voice-statestore",
            $"voice:room:{roomId}",
            It.IsAny<ConsistencyMode?>(),
            It.IsAny<IReadOnlyDictionary<string, string>?>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(roomData);

        _mockEndpointRegistry.Setup(r => r.GetRoomParticipantsAsync(roomId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(participants);

        // Act
        var (status, result) = await service.GetVoiceRoomAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(result);
        Assert.Equal(2, result.CurrentParticipants);
        Assert.Equal(2, result.Participants.Count);
    }

    #endregion

    #region JoinVoiceRoomAsync Tests

    [Fact]
    public async Task JoinVoiceRoom_WhenRoomNotFound_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        var roomId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var request = new JoinVoiceRoomRequest
        {
            RoomId = roomId,
            AccountId = accountId,
            SessionId = "session-123",
            DisplayName = "TestPlayer",
            SipEndpoint = new SipEndpoint { SdpOffer = "offer", IceCandidates = new List<string>() }
        };

        _mockDaprClient.Setup(d => d.GetStateAsync<VoiceRoomData>(
            "voice-statestore",
            $"voice:room:{roomId}",
            It.IsAny<ConsistencyMode?>(),
            It.IsAny<IReadOnlyDictionary<string, string>?>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync((VoiceRoomData?)null);

        // Act
        var (status, result) = await service.JoinVoiceRoomAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(result);
    }

    [Fact]
    public async Task JoinVoiceRoom_WhenRoomAtCapacity_ReturnsConflict()
    {
        // Arrange
        var service = CreateService();
        var roomId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var request = new JoinVoiceRoomRequest
        {
            RoomId = roomId,
            AccountId = accountId,
            SessionId = "session-123",
            DisplayName = "TestPlayer",
            SipEndpoint = new SipEndpoint { SdpOffer = "offer", IceCandidates = new List<string>() }
        };

        var roomData = new VoiceRoomData
        {
            RoomId = roomId,
            SessionId = Guid.NewGuid(),
            Tier = "p2p",
            Codec = "opus",
            MaxParticipants = 2,
            CreatedAt = DateTimeOffset.UtcNow
        };

        _mockDaprClient.Setup(d => d.GetStateAsync<VoiceRoomData>(
            "voice-statestore",
            $"voice:room:{roomId}",
            It.IsAny<ConsistencyMode?>(),
            It.IsAny<IReadOnlyDictionary<string, string>?>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(roomData);

        _mockEndpointRegistry.Setup(r => r.GetParticipantCountAsync(roomId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(2);

        _mockP2PCoordinator.Setup(p => p.CanAcceptNewParticipantAsync(roomId, 2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        var (status, result) = await service.JoinVoiceRoomAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.Conflict, status);
        Assert.Null(result);
    }

    [Fact]
    public async Task JoinVoiceRoom_WhenSuccessful_ReturnsOkWithPeers()
    {
        // Arrange
        var service = CreateService();
        var roomId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var existingPeerId = Guid.NewGuid();
        var request = new JoinVoiceRoomRequest
        {
            RoomId = roomId,
            AccountId = accountId,
            SessionId = "session-123",
            DisplayName = "TestPlayer",
            SipEndpoint = new SipEndpoint { SdpOffer = "offer", IceCandidates = new List<string> { "candidate1" } }
        };

        var roomData = new VoiceRoomData
        {
            RoomId = roomId,
            SessionId = Guid.NewGuid(),
            Tier = "p2p",
            Codec = "opus",
            MaxParticipants = 6,
            CreatedAt = DateTimeOffset.UtcNow
        };

        var existingPeer = new GameSessionPeerInfo
        {
            AccountId = existingPeerId,
            DisplayName = "ExistingPlayer",
            SdpOffer = "peer-offer",
            IceCandidates = new List<string> { "peer-candidate" }
        };

        _mockDaprClient.Setup(d => d.GetStateAsync<VoiceRoomData>(
            "voice-statestore",
            $"voice:room:{roomId}",
            It.IsAny<ConsistencyMode?>(),
            It.IsAny<IReadOnlyDictionary<string, string>?>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(roomData);

        _mockEndpointRegistry.Setup(r => r.GetParticipantCountAsync(roomId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        _mockP2PCoordinator.Setup(p => p.CanAcceptNewParticipantAsync(roomId, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _mockEndpointRegistry.Setup(r => r.RegisterAsync(
            roomId, accountId, It.IsAny<SipEndpoint>(), "session-123", "TestPlayer", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _mockP2PCoordinator.Setup(p => p.GetMeshPeersForNewJoinAsync(roomId, accountId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<GameSessionPeerInfo> { existingPeer });

        _mockP2PCoordinator.Setup(p => p.ShouldUpgradeToScaledAsync(roomId, 2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _mockEndpointRegistry.Setup(r => r.GetRoomParticipantsAsync(roomId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ParticipantRegistration>
            {
                new() { AccountId = existingPeerId, SessionId = "existing-session", DisplayName = "ExistingPlayer" }
            });

        _mockClientEventPublisher.Setup(p => p.PublishToSessionsAsync(
            It.IsAny<IEnumerable<string>>(), It.IsAny<VoicePeerJoinedEvent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act
        var (status, result) = await service.JoinVoiceRoomAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.Equal(roomId, result.RoomId);
        Assert.Single(result.Peers);
        Assert.Equal(existingPeerId, result.Peers.First().AccountId);
        Assert.False(result.TierUpgradePending);
    }

    [Fact]
    public async Task JoinVoiceRoom_WhenAlreadyInRoom_ReturnsConflict()
    {
        // Arrange
        var service = CreateService();
        var roomId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var request = new JoinVoiceRoomRequest
        {
            RoomId = roomId,
            AccountId = accountId,
            SessionId = "session-123",
            DisplayName = "TestPlayer",
            SipEndpoint = new SipEndpoint { SdpOffer = "offer", IceCandidates = new List<string>() }
        };

        var roomData = new VoiceRoomData
        {
            RoomId = roomId,
            SessionId = Guid.NewGuid(),
            Tier = "p2p",
            Codec = "opus",
            MaxParticipants = 6,
            CreatedAt = DateTimeOffset.UtcNow
        };

        _mockDaprClient.Setup(d => d.GetStateAsync<VoiceRoomData>(
            "voice-statestore",
            $"voice:room:{roomId}",
            It.IsAny<ConsistencyMode?>(),
            It.IsAny<IReadOnlyDictionary<string, string>?>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(roomData);

        _mockEndpointRegistry.Setup(r => r.GetParticipantCountAsync(roomId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        _mockP2PCoordinator.Setup(p => p.CanAcceptNewParticipantAsync(roomId, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _mockEndpointRegistry.Setup(r => r.RegisterAsync(
            roomId, accountId, It.IsAny<SipEndpoint>(), "session-123", "TestPlayer", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false); // Already registered

        // Act
        var (status, result) = await service.JoinVoiceRoomAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.Conflict, status);
        Assert.Null(result);
    }

    #endregion

    #region LeaveVoiceRoomAsync Tests

    [Fact]
    public async Task LeaveVoiceRoom_WhenParticipantExists_ReturnsOk()
    {
        // Arrange
        var service = CreateService();
        var roomId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var request = new LeaveVoiceRoomRequest { RoomId = roomId, AccountId = accountId };

        var removedParticipant = new ParticipantRegistration
        {
            AccountId = accountId,
            DisplayName = "LeavingPlayer",
            SessionId = "session-123"
        };

        _mockEndpointRegistry.Setup(r => r.UnregisterAsync(roomId, accountId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(removedParticipant);

        _mockEndpointRegistry.Setup(r => r.GetParticipantCountAsync(roomId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        _mockEndpointRegistry.Setup(r => r.GetRoomParticipantsAsync(roomId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ParticipantRegistration>
            {
                new() { AccountId = Guid.NewGuid(), SessionId = "other-session" }
            });

        _mockClientEventPublisher.Setup(p => p.PublishToSessionsAsync(
            It.IsAny<IEnumerable<string>>(), It.IsAny<VoicePeerLeftEvent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act
        var (status, result) = await service.LeaveVoiceRoomAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);

        // Verify peer left event was published
        _mockClientEventPublisher.Verify(p => p.PublishToSessionsAsync(
            It.IsAny<IEnumerable<string>>(),
            It.Is<VoicePeerLeftEvent>(e => e.Account_id == accountId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task LeaveVoiceRoom_WhenParticipantNotFound_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        var roomId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var request = new LeaveVoiceRoomRequest { RoomId = roomId, AccountId = accountId };

        _mockEndpointRegistry.Setup(r => r.UnregisterAsync(roomId, accountId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ParticipantRegistration?)null);

        // Act
        var (status, result) = await service.LeaveVoiceRoomAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
    }

    #endregion

    #region DeleteVoiceRoomAsync Tests

    [Fact]
    public async Task DeleteVoiceRoom_WhenRoomExists_ReturnsOkAndNotifiesParticipants()
    {
        // Arrange
        var service = CreateService();
        var roomId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var request = new DeleteVoiceRoomRequest { RoomId = roomId, Reason = "session_ended" };

        var roomData = new VoiceRoomData
        {
            RoomId = roomId,
            SessionId = sessionId,
            Tier = "p2p",
            Codec = "opus",
            MaxParticipants = 6,
            CreatedAt = DateTimeOffset.UtcNow
        };

        var participants = new List<ParticipantRegistration>
        {
            new() { AccountId = Guid.NewGuid(), SessionId = "session-1" },
            new() { AccountId = Guid.NewGuid(), SessionId = "session-2" }
        };

        _mockDaprClient.Setup(d => d.GetStateAsync<VoiceRoomData>(
            "voice-statestore",
            $"voice:room:{roomId}",
            It.IsAny<ConsistencyMode?>(),
            It.IsAny<IReadOnlyDictionary<string, string>?>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(roomData);

        _mockEndpointRegistry.Setup(r => r.GetRoomParticipantsAsync(roomId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(participants);

        _mockClientEventPublisher.Setup(p => p.PublishToSessionsAsync(
            It.IsAny<IEnumerable<string>>(), It.IsAny<VoiceRoomClosedEvent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(2);

        // Act
        var (status, result) = await service.DeleteVoiceRoomAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);

        // Verify state was deleted
        _mockDaprClient.Verify(d => d.DeleteStateAsync(
            "voice-statestore",
            $"voice:room:{roomId}",
            It.IsAny<StateOptions?>(),
            It.IsAny<IReadOnlyDictionary<string, string>?>(),
            It.IsAny<CancellationToken>()), Times.Once);

        _mockDaprClient.Verify(d => d.DeleteStateAsync(
            "voice-statestore",
            $"voice:session-room:{sessionId}",
            It.IsAny<StateOptions?>(),
            It.IsAny<IReadOnlyDictionary<string, string>?>(),
            It.IsAny<CancellationToken>()), Times.Once);

        // Verify room closed event was published
        _mockClientEventPublisher.Verify(p => p.PublishToSessionsAsync(
            It.Is<IEnumerable<string>>(list => list.Count() == 2),
            It.IsAny<VoiceRoomClosedEvent>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteVoiceRoom_WhenRoomNotFound_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        var roomId = Guid.NewGuid();
        var request = new DeleteVoiceRoomRequest { RoomId = roomId };

        _mockDaprClient.Setup(d => d.GetStateAsync<VoiceRoomData>(
            "voice-statestore",
            $"voice:room:{roomId}",
            It.IsAny<ConsistencyMode?>(),
            It.IsAny<IReadOnlyDictionary<string, string>?>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync((VoiceRoomData?)null);

        // Act
        var (status, result) = await service.DeleteVoiceRoomAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
    }

    #endregion

    #region PeerHeartbeatAsync Tests

    [Fact]
    public async Task PeerHeartbeat_WhenParticipantExists_ReturnsOk()
    {
        // Arrange
        var service = CreateService();
        var roomId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var request = new PeerHeartbeatRequest { RoomId = roomId, AccountId = accountId };

        _mockEndpointRegistry.Setup(r => r.UpdateHeartbeatAsync(roomId, accountId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var (status, result) = await service.PeerHeartbeatAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
    }

    [Fact]
    public async Task PeerHeartbeat_WhenParticipantNotFound_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        var roomId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var request = new PeerHeartbeatRequest { RoomId = roomId, AccountId = accountId };

        _mockEndpointRegistry.Setup(r => r.UpdateHeartbeatAsync(roomId, accountId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        var (status, result) = await service.PeerHeartbeatAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
    }

    #endregion

    #region UpdatePeerEndpointAsync Tests

    [Fact]
    public async Task UpdatePeerEndpoint_WhenParticipantExists_ReturnsOkAndNotifiesPeers()
    {
        // Arrange
        var service = CreateService();
        var roomId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var request = new UpdatePeerEndpointRequest
        {
            RoomId = roomId,
            AccountId = accountId,
            SipEndpoint = new SipEndpoint
            {
                SdpOffer = "new-offer",
                IceCandidates = new List<string> { "new-candidate" }
            }
        };

        var participant = new ParticipantRegistration
        {
            AccountId = accountId,
            DisplayName = "UpdatedPlayer",
            SessionId = "my-session"
        };

        var otherParticipant = new ParticipantRegistration
        {
            AccountId = Guid.NewGuid(),
            DisplayName = "OtherPlayer",
            SessionId = "other-session"
        };

        _mockEndpointRegistry.Setup(r => r.UpdateEndpointAsync(
            roomId, accountId, It.IsAny<SipEndpoint>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _mockEndpointRegistry.Setup(r => r.GetParticipantAsync(roomId, accountId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(participant);

        _mockEndpointRegistry.Setup(r => r.GetRoomParticipantsAsync(roomId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ParticipantRegistration> { participant, otherParticipant });

        _mockClientEventPublisher.Setup(p => p.PublishToSessionsAsync(
            It.IsAny<IEnumerable<string>>(), It.IsAny<VoicePeerUpdatedEvent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act
        var (status, result) = await service.UpdatePeerEndpointAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);

        // Verify peer updated event was published to OTHER peers only (not self)
        _mockClientEventPublisher.Verify(p => p.PublishToSessionsAsync(
            It.Is<IEnumerable<string>>(list => list.Contains("other-session") && !list.Contains("my-session")),
            It.IsAny<VoicePeerUpdatedEvent>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdatePeerEndpoint_WhenParticipantNotFound_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        var roomId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var request = new UpdatePeerEndpointRequest
        {
            RoomId = roomId,
            AccountId = accountId,
            SipEndpoint = new SipEndpoint { SdpOffer = "offer", IceCandidates = new List<string>() }
        };

        _mockEndpointRegistry.Setup(r => r.UpdateEndpointAsync(
            roomId, accountId, It.IsAny<SipEndpoint>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        var (status, result) = await service.UpdatePeerEndpointAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
    }

    #endregion

    #region Error Handling Tests

    [Fact]
    public async Task CreateVoiceRoom_WhenDaprThrows_ReturnsInternalServerErrorAndEmitsEvent()
    {
        // Arrange
        var service = CreateService();
        var sessionId = Guid.NewGuid();
        var request = new CreateVoiceRoomRequest
        {
            SessionId = sessionId,
            PreferredTier = VoiceTier.P2p,
            Codec = VoiceCodec.Opus,
            MaxParticipants = 6
        };

        _mockDaprClient.Setup(d => d.GetStateAsync<Guid?>(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<ConsistencyMode?>(),
            It.IsAny<IReadOnlyDictionary<string, string>?>(),
            It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Dapr connection failed"));

        // Act
        var (status, result) = await service.CreateVoiceRoomAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.InternalServerError, status);
        Assert.Null(result);

        // Verify error event was emitted
        _mockErrorEventEmitter.Verify(e => e.TryPublishAsync(
            "voice",
            "CreateVoiceRoom",
            "unexpected_exception",
            It.IsAny<string>(),
            It.IsAny<string?>(),
            It.IsAny<string?>(),
            It.IsAny<ServiceErrorEventSeverity>(),
            It.IsAny<object?>(),
            It.IsAny<string?>(),
            It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion
}

public class VoiceConfigurationTests
{
    [Fact]
    public void Configuration_WithValidSettings_ShouldInitializeCorrectly()
    {
        // Arrange
        var config = new VoiceServiceConfiguration();

        // Act & Assert
        Assert.NotNull(config);
    }
}
