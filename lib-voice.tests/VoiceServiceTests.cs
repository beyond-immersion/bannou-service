using BeyondImmersion.Bannou.Voice.ClientEvents;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.ClientEvents;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Messaging.Services;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using BeyondImmersion.BannouService.Voice;
using BeyondImmersion.BannouService.Voice.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace BeyondImmersion.BannouService.Voice.Tests;

public class VoiceServiceTests
{
    private const string STATE_STORE = "voice-statestore";

    private readonly Mock<IStateStoreFactory> _mockStateStoreFactory;
    private readonly Mock<IStateStore<string>> _mockStringStore;
    private readonly Mock<IStateStore<VoiceRoomData>> _mockRoomStore;
    private readonly Mock<IMessageBus> _mockMessageBus;
    private readonly Mock<ILogger<VoiceService>> _mockLogger;
    private readonly Mock<VoiceServiceConfiguration> _mockConfiguration;
    private readonly Mock<ISipEndpointRegistry> _mockEndpointRegistry;
    private readonly Mock<IP2PCoordinator> _mockP2PCoordinator;
    private readonly Mock<IScaledTierCoordinator> _mockScaledTierCoordinator;
    private readonly Mock<IClientEventPublisher> _mockClientEventPublisher;
    private readonly Mock<IEventConsumer> _mockEventConsumer;

    public VoiceServiceTests()
    {
        _mockStateStoreFactory = new Mock<IStateStoreFactory>();
        _mockStringStore = new Mock<IStateStore<string>>();
        _mockRoomStore = new Mock<IStateStore<VoiceRoomData>>();
        _mockMessageBus = new Mock<IMessageBus>();
        _mockLogger = new Mock<ILogger<VoiceService>>();
        _mockConfiguration = new Mock<VoiceServiceConfiguration>();
        _mockEndpointRegistry = new Mock<ISipEndpointRegistry>();
        _mockP2PCoordinator = new Mock<IP2PCoordinator>();
        _mockScaledTierCoordinator = new Mock<IScaledTierCoordinator>();
        _mockClientEventPublisher = new Mock<IClientEventPublisher>();
        _mockEventConsumer = new Mock<IEventConsumer>();

        // Setup state store factory to return typed stores
        _mockStateStoreFactory.Setup(f => f.GetStore<string>(STATE_STORE)).Returns(_mockStringStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<VoiceRoomData>(STATE_STORE)).Returns(_mockRoomStore.Object);

        // Default P2P max participants
        _mockP2PCoordinator.Setup(p => p.GetP2PMaxParticipants()).Returns(6);

        // Default scaled tier settings
        _mockScaledTierCoordinator.Setup(s => s.GetScaledMaxParticipants()).Returns(100);
    }

    private VoiceService CreateService()
    {
        return new VoiceService(
            _mockStateStoreFactory.Object,
            _mockMessageBus.Object,
            _mockLogger.Object,
            _mockConfiguration.Object,
            _mockEndpointRegistry.Object,
            _mockP2PCoordinator.Object,
            _mockScaledTierCoordinator.Object,
            _mockEventConsumer.Object,
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
    public void Constructor_WithNullStateStoreFactory_ShouldThrowArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => new VoiceService(
            null!,
            _mockMessageBus.Object,
            _mockLogger.Object,
            _mockConfiguration.Object,
            _mockEndpointRegistry.Object,
            _mockP2PCoordinator.Object,
            _mockScaledTierCoordinator.Object,
            _mockEventConsumer.Object,
            _mockClientEventPublisher.Object));
    }

    [Fact]
    public void Constructor_WithNullLogger_ShouldThrowArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => new VoiceService(
            _mockStateStoreFactory.Object,
            _mockMessageBus.Object,
            null!,
            _mockConfiguration.Object,
            _mockEndpointRegistry.Object,
            _mockP2PCoordinator.Object,
            _mockScaledTierCoordinator.Object,
            _mockEventConsumer.Object,
            _mockClientEventPublisher.Object));
    }

    [Fact]
    public void Constructor_WithNullConfiguration_ShouldThrowArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => new VoiceService(
            _mockStateStoreFactory.Object,
            _mockMessageBus.Object,
            _mockLogger.Object,
            null!,
            _mockEndpointRegistry.Object,
            _mockP2PCoordinator.Object,
            _mockScaledTierCoordinator.Object,
            _mockEventConsumer.Object,
            _mockClientEventPublisher.Object));
    }

    [Fact]
    public void Constructor_WithNullEndpointRegistry_ShouldThrowArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => new VoiceService(
            _mockStateStoreFactory.Object,
            _mockMessageBus.Object,
            _mockLogger.Object,
            _mockConfiguration.Object,
            null!,
            _mockP2PCoordinator.Object,
            _mockScaledTierCoordinator.Object,
            _mockEventConsumer.Object,
            _mockClientEventPublisher.Object));
    }

    [Fact]
    public void Constructor_WithNullP2PCoordinator_ShouldThrowArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => new VoiceService(
            _mockStateStoreFactory.Object,
            _mockMessageBus.Object,
            _mockLogger.Object,
            _mockConfiguration.Object,
            _mockEndpointRegistry.Object,
            null!,
            _mockScaledTierCoordinator.Object,
            _mockEventConsumer.Object,
            _mockClientEventPublisher.Object));
    }

    [Fact]
    public void Constructor_WithNullScaledTierCoordinator_ShouldThrowArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => new VoiceService(
            _mockStateStoreFactory.Object,
            _mockMessageBus.Object,
            _mockLogger.Object,
            _mockConfiguration.Object,
            _mockEndpointRegistry.Object,
            _mockP2PCoordinator.Object,
            null!,
            _mockEventConsumer.Object,
            _mockClientEventPublisher.Object));
    }

    [Fact]
    public void Constructor_WithNullEventConsumer_ShouldThrowArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => new VoiceService(
            _mockStateStoreFactory.Object,
            _mockMessageBus.Object,
            _mockLogger.Object,
            _mockConfiguration.Object,
            _mockEndpointRegistry.Object,
            _mockP2PCoordinator.Object,
            _mockScaledTierCoordinator.Object,
            null!,
            _mockClientEventPublisher.Object));
    }

    [Fact]
    public void Constructor_WithNullClientEventPublisher_ShouldSucceed()
    {
        // Arrange, Act - IClientEventPublisher is optional per Tenet 5 (context-dependent)
        var service = new VoiceService(
            _mockStateStoreFactory.Object,
            _mockMessageBus.Object,
            _mockLogger.Object,
            _mockConfiguration.Object,
            _mockEndpointRegistry.Object,
            _mockP2PCoordinator.Object,
            _mockScaledTierCoordinator.Object,
            _mockEventConsumer.Object,
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
        _mockStringStore.Setup(s => s.GetAsync(
            $"voice:session-room:{sessionId}",
            It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

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
        _mockRoomStore.Verify(s => s.SaveAsync(
            It.Is<string>(k => k.StartsWith("voice:room:")),
            It.IsAny<VoiceRoomData>(),
            It.IsAny<StateOptions?>(),
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
        _mockStringStore.Setup(s => s.GetAsync(
            $"voice:session-room:{sessionId}",
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingRoomId.ToString());

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

        _mockStringStore.Setup(s => s.GetAsync(
            $"voice:session-room:{sessionId}",
            It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

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

        _mockRoomStore.Setup(s => s.GetAsync(
            $"voice:room:{roomId}",
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

        _mockRoomStore.Setup(s => s.GetAsync(
            $"voice:room:{roomId}",
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
            new() { SessionId = "session-player1", DisplayName = "Player1", JoinedAt = DateTimeOffset.UtcNow },
            new() { SessionId = "session-player2", DisplayName = "Player2", JoinedAt = DateTimeOffset.UtcNow }
        };

        _mockRoomStore.Setup(s => s.GetAsync(
            $"voice:room:{roomId}",
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
        var request = new JoinVoiceRoomRequest
        {
            RoomId = roomId,
            SessionId = "session-123",
            DisplayName = "TestPlayer",
            SipEndpoint = new SipEndpoint { SdpOffer = "offer", IceCandidates = new List<string>() }
        };

        _mockRoomStore.Setup(s => s.GetAsync(
            $"voice:room:{roomId}",
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
        var request = new JoinVoiceRoomRequest
        {
            RoomId = roomId,
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

        _mockRoomStore.Setup(s => s.GetAsync(
            $"voice:room:{roomId}",
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
        var request = new JoinVoiceRoomRequest
        {
            RoomId = roomId,
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

        var existingPeer = new VoicePeer
        {
            SessionId = "existing-session-123",
            DisplayName = "ExistingPlayer",
            SipEndpoint = new SipEndpoint
            {
                SdpOffer = "peer-offer",
                IceCandidates = new List<string> { "peer-candidate" }
            }
        };

        _mockRoomStore.Setup(s => s.GetAsync(
            $"voice:room:{roomId}",
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(roomData);

        _mockEndpointRegistry.Setup(r => r.GetParticipantCountAsync(roomId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        _mockP2PCoordinator.Setup(p => p.CanAcceptNewParticipantAsync(roomId, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _mockEndpointRegistry.Setup(r => r.RegisterAsync(
            roomId, "session-123", It.IsAny<SipEndpoint>(), "TestPlayer", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _mockP2PCoordinator.Setup(p => p.GetMeshPeersForNewJoinAsync(roomId, "session-123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<VoicePeer> { existingPeer });

        _mockP2PCoordinator.Setup(p => p.ShouldUpgradeToScaledAsync(roomId, 2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _mockEndpointRegistry.Setup(r => r.GetRoomParticipantsAsync(roomId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ParticipantRegistration>
            {
                new() { SessionId = "existing-session", DisplayName = "ExistingPlayer" }
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
        Assert.Equal("existing-session-123", result.Peers.First().SessionId);
        Assert.False(result.TierUpgradePending);
    }

    [Fact]
    public async Task JoinVoiceRoom_WhenAlreadyInRoom_ReturnsConflict()
    {
        // Arrange
        var service = CreateService();
        var roomId = Guid.NewGuid();
        var request = new JoinVoiceRoomRequest
        {
            RoomId = roomId,
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

        _mockRoomStore.Setup(s => s.GetAsync(
            $"voice:room:{roomId}",
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(roomData);

        _mockEndpointRegistry.Setup(r => r.GetParticipantCountAsync(roomId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        _mockP2PCoordinator.Setup(p => p.CanAcceptNewParticipantAsync(roomId, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _mockEndpointRegistry.Setup(r => r.RegisterAsync(
            roomId, "session-123", It.IsAny<SipEndpoint>(), "TestPlayer", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false); // Already registered

        // Act
        var (status, result) = await service.JoinVoiceRoomAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.Conflict, status);
        Assert.Null(result);
    }

    [Fact]
    public async Task JoinVoiceRoom_WhenExistingPeers_SetsVoiceRingingStateForJoiningSession()
    {
        // Arrange - joining session should get voice:ringing state when there are existing peers (Tenet 10)
        var mockPermissionsClient = new Mock<BeyondImmersion.BannouService.Permissions.IPermissionsClient>();
        var service = new VoiceService(
            _mockStateStoreFactory.Object,
            _mockMessageBus.Object,
            _mockLogger.Object,
            _mockConfiguration.Object,
            _mockEndpointRegistry.Object,
            _mockP2PCoordinator.Object,
            _mockScaledTierCoordinator.Object,
            _mockEventConsumer.Object,
            _mockClientEventPublisher.Object,
            mockPermissionsClient.Object);

        var roomId = Guid.NewGuid();
        var joiningSessionGuid = Guid.NewGuid();
        var joiningSessionId = joiningSessionGuid.ToString();
        var request = new JoinVoiceRoomRequest
        {
            RoomId = roomId,
            SessionId = joiningSessionId,
            DisplayName = "NewPlayer",
            SipEndpoint = new SipEndpoint { SdpOffer = "v=0\r\no=- 12345\r\n" }
        };

        // Room exists in P2P mode
        _mockRoomStore.Setup(s => s.GetAsync(
            $"voice:room:{roomId}",
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new VoiceRoomData
            {
                RoomId = roomId,
                SessionId = Guid.NewGuid(),
                Tier = "p2p",
                Codec = "opus"
            });

        // Room has one existing participant
        _mockEndpointRegistry.Setup(r => r.GetParticipantCountAsync(roomId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // P2P can accept more participants
        _mockP2PCoordinator.Setup(p => p.CanAcceptNewParticipantAsync(roomId, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Registration succeeds
        _mockEndpointRegistry.Setup(r => r.RegisterAsync(
            roomId, joiningSessionId, It.IsAny<SipEndpoint>(), "NewPlayer", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Return one existing peer (so joining session needs voice:ringing to answer them)
        var existingPeers = new List<VoicePeer>
        {
            new VoicePeer
            {
                SessionId = "existing-session-456",
                DisplayName = "ExistingPlayer",
                SipEndpoint = new SipEndpoint { SdpOffer = "v=0\r\no=- 67890\r\n" }
            }
        };
        _mockP2PCoordinator.Setup(p => p.GetMeshPeersForNewJoinAsync(roomId, joiningSessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingPeers);

        _mockP2PCoordinator.Setup(p => p.ShouldUpgradeToScaledAsync(roomId, 2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Existing participants for notification
        _mockEndpointRegistry.Setup(r => r.GetRoomParticipantsAsync(roomId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ParticipantRegistration>
            {
                new ParticipantRegistration { SessionId = "existing-session-456", DisplayName = "ExistingPlayer" },
                new ParticipantRegistration { SessionId = joiningSessionId, DisplayName = "NewPlayer" }
            });

        // Act
        var (status, result) = await service.JoinVoiceRoomAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(result);
        Assert.Single(result.Peers);

        // Verify voice:ringing state was set for the JOINING session (so they can call /voice/peer/answer)
        mockPermissionsClient.Verify(p => p.UpdateSessionStateAsync(
            It.Is<BeyondImmersion.BannouService.Permissions.SessionStateUpdate>(u =>
                u.SessionId == joiningSessionGuid &&
                u.ServiceId == "voice" &&
                u.NewState == "ringing"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region LeaveVoiceRoomAsync Tests

    [Fact]
    public async Task LeaveVoiceRoom_WhenParticipantExists_ReturnsOk()
    {
        // Arrange
        var service = CreateService();
        var roomId = Guid.NewGuid();
        var request = new LeaveVoiceRoomRequest { RoomId = roomId, SessionId = "session-123" };

        var removedParticipant = new ParticipantRegistration
        {
            DisplayName = "LeavingPlayer",
            SessionId = "session-123"
        };

        _mockEndpointRegistry.Setup(r => r.UnregisterAsync(roomId, "session-123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(removedParticipant);

        _mockEndpointRegistry.Setup(r => r.GetParticipantCountAsync(roomId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        _mockEndpointRegistry.Setup(r => r.GetRoomParticipantsAsync(roomId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ParticipantRegistration>
            {
                new() { SessionId = "other-session" }
            });

        _mockClientEventPublisher.Setup(p => p.PublishToSessionsAsync(
            It.IsAny<IEnumerable<string>>(), It.IsAny<VoicePeerLeftEvent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act
        var status = await service.LeaveVoiceRoomAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);

        // Verify peer left event was published with sessionId (not accountId for privacy)
        _mockClientEventPublisher.Verify(p => p.PublishToSessionsAsync(
            It.IsAny<IEnumerable<string>>(),
            It.Is<VoicePeerLeftEvent>(e => e.PeerSessionId == "session-123"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task LeaveVoiceRoom_WhenParticipantNotFound_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        var roomId = Guid.NewGuid();
        var request = new LeaveVoiceRoomRequest { RoomId = roomId, SessionId = "session-123" };

        _mockEndpointRegistry.Setup(r => r.UnregisterAsync(roomId, "session-123", It.IsAny<CancellationToken>()))
            .ReturnsAsync((ParticipantRegistration?)null);

        // Act
        var status = await service.LeaveVoiceRoomAsync(request, CancellationToken.None);

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
            new() { SessionId = "session-1" },
            new() { SessionId = "session-2" }
        };

        _mockRoomStore.Setup(s => s.GetAsync(
            $"voice:room:{roomId}",
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(roomData);

        _mockEndpointRegistry.Setup(r => r.GetRoomParticipantsAsync(roomId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(participants);

        _mockClientEventPublisher.Setup(p => p.PublishToSessionsAsync(
            It.IsAny<IEnumerable<string>>(), It.IsAny<VoiceRoomClosedEvent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(2);

        // Act
        var status = await service.DeleteVoiceRoomAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);

        // Verify state was deleted
        _mockRoomStore.Verify(s => s.DeleteAsync(
            $"voice:room:{roomId}",
            It.IsAny<CancellationToken>()), Times.Once);

        _mockStringStore.Verify(s => s.DeleteAsync(
            $"voice:session-room:{sessionId}",
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

        _mockRoomStore.Setup(s => s.GetAsync(
            $"voice:room:{roomId}",
            It.IsAny<CancellationToken>()))
            .ReturnsAsync((VoiceRoomData?)null);

        // Act
        var status = await service.DeleteVoiceRoomAsync(request, CancellationToken.None);

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
        var request = new PeerHeartbeatRequest { RoomId = roomId, SessionId = "session-123" };

        _mockEndpointRegistry.Setup(r => r.UpdateHeartbeatAsync(roomId, "session-123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var status = await service.PeerHeartbeatAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
    }

    [Fact]
    public async Task PeerHeartbeat_WhenParticipantNotFound_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        var roomId = Guid.NewGuid();
        var request = new PeerHeartbeatRequest { RoomId = roomId, SessionId = "session-123" };

        _mockEndpointRegistry.Setup(r => r.UpdateHeartbeatAsync(roomId, "session-123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        var status = await service.PeerHeartbeatAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
    }

    #endregion

    #region AnswerPeerAsync Tests

    [Fact]
    public async Task AnswerPeer_WhenTargetPeerExists_ReturnsOkAndNotifiesTarget()
    {
        // Arrange
        var service = CreateService();
        var roomId = Guid.NewGuid();
        var request = new AnswerPeerRequest
        {
            RoomId = roomId,
            SenderSessionId = "sender-session",
            TargetSessionId = "target-session",
            SdpAnswer = "sdp-answer-content",
            IceCandidates = new List<string> { "ice-candidate-1" }
        };

        var targetParticipant = new ParticipantRegistration
        {
            DisplayName = "TargetPlayer",
            SessionId = "target-session"
        };

        var senderParticipant = new ParticipantRegistration
        {
            DisplayName = "SenderPlayer",
            SessionId = "sender-session"
        };

        _mockEndpointRegistry.Setup(r => r.GetParticipantAsync(roomId, "target-session", It.IsAny<CancellationToken>()))
            .ReturnsAsync(targetParticipant);

        _mockEndpointRegistry.Setup(r => r.GetParticipantAsync(roomId, "sender-session", It.IsAny<CancellationToken>()))
            .ReturnsAsync(senderParticipant);

        _mockEndpointRegistry.Setup(r => r.GetRoomParticipantsAsync(roomId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ParticipantRegistration> { targetParticipant, senderParticipant });

        _mockClientEventPublisher.Setup(p => p.PublishToSessionsAsync(
            It.IsAny<IEnumerable<string>>(), It.IsAny<VoicePeerUpdatedEvent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act
        var status = await service.AnswerPeerAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);

        // Verify peer updated event was published to the target session with sender info
        _mockClientEventPublisher.Verify(p => p.PublishToSessionsAsync(
            It.Is<IEnumerable<string>>(list => list.Contains("target-session")),
            It.Is<VoicePeerUpdatedEvent>(e =>
                e.Peer.PeerSessionId == "sender-session" &&
                e.Peer.DisplayName == "SenderPlayer" &&
                e.Peer.SdpOffer == "sdp-answer-content"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AnswerPeer_WhenTargetPeerNotFound_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        var roomId = Guid.NewGuid();
        var request = new AnswerPeerRequest
        {
            RoomId = roomId,
            SenderSessionId = "sender-session",
            TargetSessionId = "unknown-session",
            SdpAnswer = "sdp-answer-content",
            IceCandidates = new List<string>()
        };

        _mockEndpointRegistry.Setup(r => r.GetParticipantAsync(roomId, "unknown-session", It.IsAny<CancellationToken>()))
            .ReturnsAsync((ParticipantRegistration?)null);

        // Act
        var status = await service.AnswerPeerAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
    }

    #endregion

    #region Error Handling Tests

    [Fact]
    public async Task CreateVoiceRoom_WhenStateStoreThrows_ReturnsInternalServerErrorAndEmitsEvent()
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

        _mockStringStore.Setup(s => s.GetAsync(
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("State store connection failed"));

        // Act
        var (status, result) = await service.CreateVoiceRoomAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.InternalServerError, status);
        Assert.Null(result);

        // Verify error event was emitted
        _mockMessageBus.Verify(m => m.TryPublishErrorAsync(
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
