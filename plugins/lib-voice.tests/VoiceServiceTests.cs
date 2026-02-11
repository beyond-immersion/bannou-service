using BeyondImmersion.Bannou.Voice.ClientEvents;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.ClientEvents;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Messaging.Services;
using BeyondImmersion.BannouService.Permission;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using BeyondImmersion.BannouService.TestUtilities;
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
    private readonly Mock<IPermissionClient> _mockPermissionClient;
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
        _mockPermissionClient = new Mock<IPermissionClient>();
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
            _mockClientEventPublisher.Object,
            _mockPermissionClient.Object);
    }

    #region Constructor Tests

    /// <summary>
    /// Validates the service constructor follows proper DI patterns.
    ///
    /// This single test replaces N individual null-check tests and catches:
    /// - Multiple constructors (DI might pick wrong one)
    /// - Optional parameters (accidental defaults that hide missing registrations)
    /// - Missing null checks (ArgumentNullException not thrown)
    /// - Wrong parameter names in ArgumentNullException
    /// </summary>
    [Fact]
    public void VoiceService_ConstructorIsValid() =>
        ServiceConstructorValidator.ValidateServiceConstructor<VoiceService>();

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
        Assert.Equal(StatusCodes.OK, status);
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
        Assert.Equal(StatusCodes.OK, status);
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
            Tier = VoiceTier.P2p,
            Codec = VoiceCodec.Opus,
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
            Tier = VoiceTier.P2p,
            Codec = VoiceCodec.Opus,
            MaxParticipants = 6,
            CreatedAt = DateTimeOffset.UtcNow
        };

        var participants = new List<ParticipantRegistration>
        {
            new() { SessionId = Guid.NewGuid(), DisplayName = "Player1", JoinedAt = DateTimeOffset.UtcNow },
            new() { SessionId = Guid.NewGuid(), DisplayName = "Player2", JoinedAt = DateTimeOffset.UtcNow }
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
            SessionId = Guid.NewGuid(),
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
            SessionId = Guid.NewGuid(),
            DisplayName = "TestPlayer",
            SipEndpoint = new SipEndpoint { SdpOffer = "offer", IceCandidates = new List<string>() }
        };

        var roomData = new VoiceRoomData
        {
            RoomId = roomId,
            SessionId = Guid.NewGuid(),
            Tier = VoiceTier.P2p,
            Codec = VoiceCodec.Opus,
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
        var sessionId = Guid.NewGuid();
        var existingSessionId = Guid.NewGuid();
        var request = new JoinVoiceRoomRequest
        {
            RoomId = roomId,
            SessionId = sessionId,
            DisplayName = "TestPlayer",
            SipEndpoint = new SipEndpoint { SdpOffer = "offer", IceCandidates = new List<string> { "candidate1" } }
        };

        var roomData = new VoiceRoomData
        {
            RoomId = roomId,
            SessionId = Guid.NewGuid(),
            Tier = VoiceTier.P2p,
            Codec = VoiceCodec.Opus,
            MaxParticipants = 6,
            CreatedAt = DateTimeOffset.UtcNow
        };

        var existingPeer = new VoicePeer
        {
            SessionId = existingSessionId,
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
            roomId, sessionId, It.IsAny<SipEndpoint>(), "TestPlayer", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _mockP2PCoordinator.Setup(p => p.GetMeshPeersForNewJoinAsync(roomId, sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<VoicePeer> { existingPeer });

        _mockP2PCoordinator.Setup(p => p.ShouldUpgradeToScaledAsync(roomId, 2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _mockEndpointRegistry.Setup(r => r.GetRoomParticipantsAsync(roomId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ParticipantRegistration>
            {
                new() { SessionId = existingSessionId, DisplayName = "ExistingPlayer" }
            });

        _mockClientEventPublisher.Setup(p => p.PublishToSessionsAsync(
            It.IsAny<IEnumerable<string>>(), It.IsAny<VoicePeerJoinedEvent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act
        var (status, result) = await service.JoinVoiceRoomAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(result);
        Assert.Equal(roomId, result.RoomId);
        Assert.Single(result.Peers);
        Assert.Equal(existingSessionId, result.Peers.First().SessionId);
        Assert.False(result.TierUpgradePending);
    }

    [Fact]
    public async Task JoinVoiceRoom_WhenAlreadyInRoom_ReturnsConflict()
    {
        // Arrange
        var service = CreateService();
        var roomId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var request = new JoinVoiceRoomRequest
        {
            RoomId = roomId,
            SessionId = sessionId,
            DisplayName = "TestPlayer",
            SipEndpoint = new SipEndpoint { SdpOffer = "offer", IceCandidates = new List<string>() }
        };

        var roomData = new VoiceRoomData
        {
            RoomId = roomId,
            SessionId = Guid.NewGuid(),
            Tier = VoiceTier.P2p,
            Codec = VoiceCodec.Opus,
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
            roomId, sessionId, It.IsAny<SipEndpoint>(), "TestPlayer", It.IsAny<CancellationToken>()))
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
        // Arrange - joining session should get voice:ringing state when there are existing peers (QUALITY TENETS)
        var mockPermissionClient = new Mock<BeyondImmersion.BannouService.Permission.IPermissionClient>();
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
            mockPermissionClient.Object);

        var roomId = Guid.NewGuid();
        var joiningSessionId = Guid.NewGuid();
        var existingSessionId = Guid.NewGuid();
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
                Tier = VoiceTier.P2p,
                Codec = VoiceCodec.Opus
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
                SessionId = existingSessionId,
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
                new ParticipantRegistration { SessionId = existingSessionId, DisplayName = "ExistingPlayer" },
                new ParticipantRegistration { SessionId = joiningSessionId, DisplayName = "NewPlayer" }
            });

        // Act
        var (status, result) = await service.JoinVoiceRoomAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(result);
        Assert.Single(result.Peers);

        // Verify voice:ringing state was set for the JOINING session (so they can call /voice/peer/answer)
        mockPermissionClient.Verify(p => p.UpdateSessionStateAsync(
            It.Is<BeyondImmersion.BannouService.Permission.SessionStateUpdate>(u =>
                u.SessionId == joiningSessionId &&
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
        var sessionId = Guid.NewGuid();
        var request = new LeaveVoiceRoomRequest { RoomId = roomId, SessionId = sessionId };

        var removedParticipant = new ParticipantRegistration
        {
            DisplayName = "LeavingPlayer",
            SessionId = sessionId
        };

        _mockEndpointRegistry.Setup(r => r.UnregisterAsync(roomId, sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(removedParticipant);

        _mockEndpointRegistry.Setup(r => r.GetParticipantCountAsync(roomId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        _mockEndpointRegistry.Setup(r => r.GetRoomParticipantsAsync(roomId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ParticipantRegistration>
            {
                new() { SessionId = Guid.NewGuid() }
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
            It.Is<VoicePeerLeftEvent>(e => e.PeerSessionId == sessionId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task LeaveVoiceRoom_WhenParticipantNotFound_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        var roomId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var request = new LeaveVoiceRoomRequest { RoomId = roomId, SessionId = sessionId };

        _mockEndpointRegistry.Setup(r => r.UnregisterAsync(roomId, sessionId, It.IsAny<CancellationToken>()))
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
            Tier = VoiceTier.P2p,
            Codec = VoiceCodec.Opus,
            MaxParticipants = 6,
            CreatedAt = DateTimeOffset.UtcNow
        };

        var participants = new List<ParticipantRegistration>
        {
            new() { SessionId = Guid.NewGuid() },
            new() { SessionId = Guid.NewGuid() }
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
        var sessionId = Guid.NewGuid();
        var request = new PeerHeartbeatRequest { RoomId = roomId, SessionId = sessionId };

        _mockEndpointRegistry.Setup(r => r.UpdateHeartbeatAsync(roomId, sessionId, It.IsAny<CancellationToken>()))
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
        var sessionId = Guid.NewGuid();
        var request = new PeerHeartbeatRequest { RoomId = roomId, SessionId = sessionId };

        _mockEndpointRegistry.Setup(r => r.UpdateHeartbeatAsync(roomId, sessionId, It.IsAny<CancellationToken>()))
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
        var senderSessionId = Guid.NewGuid();
        var targetSessionId = Guid.NewGuid();
        var request = new AnswerPeerRequest
        {
            RoomId = roomId,
            SenderSessionId = senderSessionId,
            TargetSessionId = targetSessionId,
            SdpAnswer = "sdp-answer-content",
            IceCandidates = new List<string> { "ice-candidate-1" }
        };

        var targetParticipant = new ParticipantRegistration
        {
            DisplayName = "TargetPlayer",
            SessionId = targetSessionId
        };

        var senderParticipant = new ParticipantRegistration
        {
            DisplayName = "SenderPlayer",
            SessionId = senderSessionId
        };

        _mockEndpointRegistry.Setup(r => r.GetParticipantAsync(roomId, targetSessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(targetParticipant);

        _mockEndpointRegistry.Setup(r => r.GetParticipantAsync(roomId, senderSessionId, It.IsAny<CancellationToken>()))
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
            It.Is<IEnumerable<string>>(list => list.Contains(targetSessionId.ToString())),
            It.Is<VoicePeerUpdatedEvent>(e =>
                e.Peer.PeerSessionId == senderSessionId &&
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
        var unknownSessionId = Guid.NewGuid();
        var request = new AnswerPeerRequest
        {
            RoomId = roomId,
            SenderSessionId = Guid.NewGuid(),
            TargetSessionId = unknownSessionId,
            SdpAnswer = "sdp-answer-content",
            IceCandidates = new List<string>()
        };

        _mockEndpointRegistry.Setup(r => r.GetParticipantAsync(roomId, unknownSessionId, It.IsAny<CancellationToken>()))
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
            It.IsAny<Guid?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region Create Room - New Features Tests

    [Fact]
    public async Task CreateVoiceRoom_ValidRequest_PublishesCreatedEvent()
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
            $"voice:session-room:{sessionId}",
            It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        // Act
        var (status, result) = await service.CreateVoiceRoomAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);

        // Verify voice.room.created event was published
        _mockMessageBus.Verify(m => m.TryPublishAsync(
            "voice.room.created",
            It.Is<VoiceRoomCreatedEvent>(e =>
                e.SessionId == sessionId &&
                e.Tier == VoiceTier.P2p &&
                e.MaxParticipants == 6),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateVoiceRoom_WithPassword_SavesPasswordProtectedRoom()
    {
        // Arrange
        var service = CreateService();
        var sessionId = Guid.NewGuid();
        var request = new CreateVoiceRoomRequest
        {
            SessionId = sessionId,
            PreferredTier = VoiceTier.P2p,
            Codec = VoiceCodec.Opus,
            MaxParticipants = 6,
            Password = "secret123"
        };

        _mockStringStore.Setup(s => s.GetAsync(
            $"voice:session-room:{sessionId}",
            It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        VoiceRoomData? savedRoom = null;
        _mockRoomStore.Setup(s => s.SaveAsync(
            It.IsAny<string>(),
            It.IsAny<VoiceRoomData>(),
            It.IsAny<StateOptions?>(),
            It.IsAny<CancellationToken>()))
            .Callback<string, VoiceRoomData, StateOptions?, CancellationToken>((_, data, _, _) => savedRoom = data);

        // Act
        var (status, result) = await service.CreateVoiceRoomAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(result);
        Assert.True(result.IsPasswordProtected);
        Assert.NotNull(savedRoom);
        Assert.Equal("secret123", savedRoom.Password);
    }

    [Fact]
    public async Task CreateVoiceRoom_WithAutoCleanup_SetsAutoCleanupFlag()
    {
        // Arrange
        var service = CreateService();
        var sessionId = Guid.NewGuid();
        var request = new CreateVoiceRoomRequest
        {
            SessionId = sessionId,
            PreferredTier = VoiceTier.P2p,
            Codec = VoiceCodec.Opus,
            MaxParticipants = 6,
            AutoCleanup = true
        };

        _mockStringStore.Setup(s => s.GetAsync(
            $"voice:session-room:{sessionId}",
            It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        VoiceRoomData? savedRoom = null;
        _mockRoomStore.Setup(s => s.SaveAsync(
            It.IsAny<string>(),
            It.IsAny<VoiceRoomData>(),
            It.IsAny<StateOptions?>(),
            It.IsAny<CancellationToken>()))
            .Callback<string, VoiceRoomData, StateOptions?, CancellationToken>((_, data, _, _) => savedRoom = data);

        // Act
        var (status, result) = await service.CreateVoiceRoomAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(result);
        Assert.True(result.AutoCleanup);
        Assert.NotNull(savedRoom);
        Assert.True(savedRoom.AutoCleanup);
    }

    #endregion

    #region Join Room - New Features Tests

    [Fact]
    public async Task JoinVoiceRoom_NotFound_AdHocEnabled_AutoCreatesAndJoins()
    {
        // Arrange
        var service = CreateService();
        var roomId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var request = new JoinVoiceRoomRequest
        {
            RoomId = roomId,
            SessionId = sessionId,
            DisplayName = "TestPlayer",
            SipEndpoint = new SipEndpoint { SdpOffer = "offer", IceCandidates = new List<string>() }
        };

        // Room not found
        _mockRoomStore.Setup(s => s.GetAsync(
            $"voice:room:{roomId}",
            It.IsAny<CancellationToken>()))
            .ReturnsAsync((VoiceRoomData?)null);

        // Enable ad-hoc rooms
        _mockConfiguration.Setup(c => c.AdHocRoomsEnabled).Returns(true);

        // Registration succeeds
        _mockEndpointRegistry.Setup(r => r.GetParticipantCountAsync(roomId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _mockP2PCoordinator.Setup(p => p.CanAcceptNewParticipantAsync(roomId, 0, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _mockEndpointRegistry.Setup(r => r.RegisterAsync(
            roomId, sessionId, It.IsAny<SipEndpoint>(), "TestPlayer", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _mockP2PCoordinator.Setup(p => p.GetMeshPeersForNewJoinAsync(roomId, sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<VoicePeer>());
        _mockP2PCoordinator.Setup(p => p.ShouldUpgradeToScaledAsync(roomId, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _mockEndpointRegistry.Setup(r => r.GetRoomParticipantsAsync(roomId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ParticipantRegistration>());

        // Act
        var (status, result) = await service.JoinVoiceRoomAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(result);
        Assert.Equal(roomId, result.RoomId);

        // Verify room was saved (ad-hoc auto-creation)
        _mockRoomStore.Verify(s => s.SaveAsync(
            $"voice:room:{roomId}",
            It.Is<VoiceRoomData>(d => d.AutoCleanup == true && d.RoomId == roomId),
            It.IsAny<StateOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);

        // Verify room created event was published
        _mockMessageBus.Verify(m => m.TryPublishAsync(
            "voice.room.created",
            It.Is<VoiceRoomCreatedEvent>(e => e.RoomId == roomId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task JoinVoiceRoom_PasswordProtected_WrongPassword_ReturnsForbidden()
    {
        // Arrange
        var service = CreateService();
        var roomId = Guid.NewGuid();
        var request = new JoinVoiceRoomRequest
        {
            RoomId = roomId,
            SessionId = Guid.NewGuid(),
            DisplayName = "TestPlayer",
            SipEndpoint = new SipEndpoint { SdpOffer = "offer", IceCandidates = new List<string>() },
            Password = "wrong"
        };

        _mockRoomStore.Setup(s => s.GetAsync(
            $"voice:room:{roomId}",
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new VoiceRoomData
            {
                RoomId = roomId,
                SessionId = Guid.NewGuid(),
                Tier = VoiceTier.P2p,
                Codec = VoiceCodec.Opus,
                MaxParticipants = 6,
                Password = "correct"
            });

        // Act
        var (status, result) = await service.JoinVoiceRoomAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.Forbidden, status);
        Assert.Null(result);
    }

    [Fact]
    public async Task JoinVoiceRoom_PasswordProtected_CorrectPassword_Joins()
    {
        // Arrange
        var service = CreateService();
        var roomId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var request = new JoinVoiceRoomRequest
        {
            RoomId = roomId,
            SessionId = sessionId,
            DisplayName = "TestPlayer",
            SipEndpoint = new SipEndpoint { SdpOffer = "offer", IceCandidates = new List<string>() },
            Password = "correct"
        };

        _mockRoomStore.Setup(s => s.GetAsync(
            $"voice:room:{roomId}",
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new VoiceRoomData
            {
                RoomId = roomId,
                SessionId = Guid.NewGuid(),
                Tier = VoiceTier.P2p,
                Codec = VoiceCodec.Opus,
                MaxParticipants = 6,
                Password = "correct"
            });

        _mockEndpointRegistry.Setup(r => r.GetParticipantCountAsync(roomId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _mockP2PCoordinator.Setup(p => p.CanAcceptNewParticipantAsync(roomId, 0, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _mockEndpointRegistry.Setup(r => r.RegisterAsync(
            roomId, sessionId, It.IsAny<SipEndpoint>(), "TestPlayer", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _mockP2PCoordinator.Setup(p => p.GetMeshPeersForNewJoinAsync(roomId, sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<VoicePeer>());
        _mockP2PCoordinator.Setup(p => p.ShouldUpgradeToScaledAsync(roomId, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _mockEndpointRegistry.Setup(r => r.GetRoomParticipantsAsync(roomId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ParticipantRegistration>());

        // Act
        var (status, result) = await service.JoinVoiceRoomAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(result);
    }

    [Fact]
    public async Task JoinVoiceRoom_BroadcastingRoom_ResponseIncludesIsBroadcasting()
    {
        // Arrange
        var service = CreateService();
        var roomId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var request = new JoinVoiceRoomRequest
        {
            RoomId = roomId,
            SessionId = sessionId,
            DisplayName = "TestPlayer",
            SipEndpoint = new SipEndpoint { SdpOffer = "offer", IceCandidates = new List<string>() }
        };

        _mockRoomStore.Setup(s => s.GetAsync(
            $"voice:room:{roomId}",
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new VoiceRoomData
            {
                RoomId = roomId,
                SessionId = Guid.NewGuid(),
                Tier = VoiceTier.P2p,
                Codec = VoiceCodec.Opus,
                MaxParticipants = 6,
                BroadcastState = BroadcastConsentState.Approved
            });

        _mockEndpointRegistry.Setup(r => r.GetParticipantCountAsync(roomId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _mockP2PCoordinator.Setup(p => p.CanAcceptNewParticipantAsync(roomId, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _mockEndpointRegistry.Setup(r => r.RegisterAsync(
            roomId, sessionId, It.IsAny<SipEndpoint>(), "TestPlayer", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _mockP2PCoordinator.Setup(p => p.GetMeshPeersForNewJoinAsync(roomId, sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<VoicePeer>());
        _mockP2PCoordinator.Setup(p => p.ShouldUpgradeToScaledAsync(roomId, 2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _mockEndpointRegistry.Setup(r => r.GetRoomParticipantsAsync(roomId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ParticipantRegistration>());

        // Act
        var (status, result) = await service.JoinVoiceRoomAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(result);
        Assert.True(result.IsBroadcasting);
        Assert.Equal(BroadcastConsentState.Approved, result.BroadcastState);
    }

    [Fact]
    public async Task JoinVoiceRoom_PublishesParticipantJoinedEvent()
    {
        // Arrange
        var service = CreateService();
        var roomId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var request = new JoinVoiceRoomRequest
        {
            RoomId = roomId,
            SessionId = sessionId,
            DisplayName = "TestPlayer",
            SipEndpoint = new SipEndpoint { SdpOffer = "offer", IceCandidates = new List<string>() }
        };

        _mockRoomStore.Setup(s => s.GetAsync(
            $"voice:room:{roomId}",
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new VoiceRoomData
            {
                RoomId = roomId,
                SessionId = Guid.NewGuid(),
                Tier = VoiceTier.P2p,
                Codec = VoiceCodec.Opus,
                MaxParticipants = 6
            });

        _mockEndpointRegistry.Setup(r => r.GetParticipantCountAsync(roomId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _mockP2PCoordinator.Setup(p => p.CanAcceptNewParticipantAsync(roomId, 0, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _mockEndpointRegistry.Setup(r => r.RegisterAsync(
            roomId, sessionId, It.IsAny<SipEndpoint>(), "TestPlayer", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _mockP2PCoordinator.Setup(p => p.GetMeshPeersForNewJoinAsync(roomId, sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<VoicePeer>());
        _mockP2PCoordinator.Setup(p => p.ShouldUpgradeToScaledAsync(roomId, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _mockEndpointRegistry.Setup(r => r.GetRoomParticipantsAsync(roomId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ParticipantRegistration>());

        // Act
        var (status, _) = await service.JoinVoiceRoomAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);

        // Verify participant joined event was published
        _mockMessageBus.Verify(m => m.TryPublishAsync(
            "voice.participant.joined",
            It.Is<VoiceParticipantJoinedEvent>(e =>
                e.RoomId == roomId &&
                e.ParticipantSessionId == sessionId &&
                e.CurrentCount == 1),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region Leave Room - New Features Tests

    [Fact]
    public async Task LeaveVoiceRoom_PublishesParticipantLeftEvent()
    {
        // Arrange
        var service = CreateService();
        var roomId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var request = new LeaveVoiceRoomRequest { RoomId = roomId, SessionId = sessionId };

        _mockEndpointRegistry.Setup(r => r.UnregisterAsync(roomId, sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ParticipantRegistration { DisplayName = "Player", SessionId = sessionId });

        _mockEndpointRegistry.Setup(r => r.GetParticipantCountAsync(roomId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        _mockEndpointRegistry.Setup(r => r.GetRoomParticipantsAsync(roomId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ParticipantRegistration>
            {
                new() { SessionId = Guid.NewGuid() }
            });

        _mockRoomStore.Setup(s => s.GetAsync(
            $"voice:room:{roomId}",
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new VoiceRoomData { RoomId = roomId, SessionId = Guid.NewGuid() });

        _mockClientEventPublisher.Setup(p => p.PublishToSessionsAsync(
            It.IsAny<IEnumerable<string>>(), It.IsAny<VoicePeerLeftEvent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act
        var status = await service.LeaveVoiceRoomAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);

        // Verify participant left event was published
        _mockMessageBus.Verify(m => m.TryPublishAsync(
            "voice.participant.left",
            It.Is<VoiceParticipantLeftEvent>(e =>
                e.RoomId == roomId &&
                e.ParticipantSessionId == sessionId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task LeaveVoiceRoom_LastParticipant_AutoCleanup_SetsLastLeftTimestamp()
    {
        // Arrange
        var service = CreateService();
        var roomId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var request = new LeaveVoiceRoomRequest { RoomId = roomId, SessionId = sessionId };

        _mockEndpointRegistry.Setup(r => r.UnregisterAsync(roomId, sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ParticipantRegistration { DisplayName = "Player", SessionId = sessionId });

        // Room is now empty
        _mockEndpointRegistry.Setup(r => r.GetParticipantCountAsync(roomId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        _mockEndpointRegistry.Setup(r => r.GetRoomParticipantsAsync(roomId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ParticipantRegistration>());

        // Room has autoCleanup enabled
        var roomData = new VoiceRoomData
        {
            RoomId = roomId,
            SessionId = Guid.NewGuid(),
            AutoCleanup = true,
            BroadcastState = BroadcastConsentState.Inactive
        };
        _mockRoomStore.Setup(s => s.GetAsync(
            $"voice:room:{roomId}",
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(roomData);

        // Act
        var status = await service.LeaveVoiceRoomAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);

        // Verify room was saved with LastParticipantLeftAt set
        _mockRoomStore.Verify(s => s.SaveAsync(
            $"voice:room:{roomId}",
            It.Is<VoiceRoomData>(d => d.LastParticipantLeftAt != null),
            It.IsAny<StateOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task LeaveVoiceRoom_WhileBroadcasting_StopsBroadcast()
    {
        // Arrange
        var service = CreateService();
        var roomId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var request = new LeaveVoiceRoomRequest { RoomId = roomId, SessionId = sessionId };

        _mockEndpointRegistry.Setup(r => r.UnregisterAsync(roomId, sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ParticipantRegistration { DisplayName = "Player", SessionId = sessionId });

        _mockEndpointRegistry.Setup(r => r.GetParticipantCountAsync(roomId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        _mockEndpointRegistry.Setup(r => r.GetRoomParticipantsAsync(roomId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ParticipantRegistration>
            {
                new() { SessionId = Guid.NewGuid() }
            });

        _mockClientEventPublisher.Setup(p => p.PublishToSessionsAsync(
            It.IsAny<IEnumerable<string>>(), It.IsAny<VoicePeerLeftEvent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Room is broadcasting
        var roomData = new VoiceRoomData
        {
            RoomId = roomId,
            SessionId = Guid.NewGuid(),
            BroadcastState = BroadcastConsentState.Approved,
            BroadcastConsentedSessions = new HashSet<Guid> { sessionId, Guid.NewGuid() }
        };
        _mockRoomStore.Setup(s => s.GetAsync(
            $"voice:room:{roomId}",
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(roomData);

        // Act
        var status = await service.LeaveVoiceRoomAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);

        // Verify broadcast stopped event was published with ConsentRevoked reason
        _mockMessageBus.Verify(m => m.TryPublishAsync(
            "voice.room.broadcast.stopped",
            It.Is<VoiceRoomBroadcastStoppedEvent>(e =>
                e.RoomId == roomId &&
                e.Reason == VoiceBroadcastStoppedReason.ConsentRevoked),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region Delete Room - New Features Tests

    [Fact]
    public async Task DeleteVoiceRoom_PublishesRoomDeletedEvent()
    {
        // Arrange
        var service = CreateService();
        var roomId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var request = new DeleteVoiceRoomRequest { RoomId = roomId, Reason = "manual" };

        _mockRoomStore.Setup(s => s.GetAsync(
            $"voice:room:{roomId}",
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new VoiceRoomData
            {
                RoomId = roomId,
                SessionId = sessionId,
                Tier = VoiceTier.P2p
            });

        _mockEndpointRegistry.Setup(r => r.GetRoomParticipantsAsync(roomId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ParticipantRegistration>());

        // Act
        var status = await service.DeleteVoiceRoomAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);

        // Verify room deleted event was published
        _mockMessageBus.Verify(m => m.TryPublishAsync(
            "voice.room.deleted",
            It.Is<VoiceRoomDeletedEvent>(e =>
                e.RoomId == roomId &&
                e.Reason == VoiceRoomDeletedReason.Manual),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteVoiceRoom_ActiveBroadcast_StopsBroadcastFirst()
    {
        // Arrange
        var service = CreateService();
        var roomId = Guid.NewGuid();
        var request = new DeleteVoiceRoomRequest { RoomId = roomId, Reason = "manual" };

        _mockRoomStore.Setup(s => s.GetAsync(
            $"voice:room:{roomId}",
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new VoiceRoomData
            {
                RoomId = roomId,
                SessionId = Guid.NewGuid(),
                Tier = VoiceTier.P2p,
                BroadcastState = BroadcastConsentState.Approved,
                BroadcastConsentedSessions = new HashSet<Guid> { Guid.NewGuid() }
            });

        _mockEndpointRegistry.Setup(r => r.GetRoomParticipantsAsync(roomId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ParticipantRegistration>());

        // Act
        var status = await service.DeleteVoiceRoomAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);

        // Verify broadcast stopped event was published with RoomClosed reason BEFORE room deleted
        _mockMessageBus.Verify(m => m.TryPublishAsync(
            "voice.room.broadcast.stopped",
            It.Is<VoiceRoomBroadcastStoppedEvent>(e =>
                e.RoomId == roomId &&
                e.Reason == VoiceBroadcastStoppedReason.RoomClosed),
            It.IsAny<CancellationToken>()), Times.Once);

        // Also verify room deleted event was published
        _mockMessageBus.Verify(m => m.TryPublishAsync(
            "voice.room.deleted",
            It.Is<VoiceRoomDeletedEvent>(e => e.RoomId == roomId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region Broadcast Consent Tests

    [Fact]
    public async Task RequestBroadcastConsent_InactiveRoom_SetsPendingAndNotifiesParticipants()
    {
        // Arrange
        var service = CreateService();
        var roomId = Guid.NewGuid();
        var requestingSessionId = Guid.NewGuid();
        var otherSessionId = Guid.NewGuid();

        _mockRoomStore.Setup(s => s.GetAsync(
            $"voice:room:{roomId}",
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new VoiceRoomData
            {
                RoomId = roomId,
                SessionId = Guid.NewGuid(),
                BroadcastState = BroadcastConsentState.Inactive
            });

        _mockEndpointRegistry.Setup(r => r.GetRoomParticipantsAsync(roomId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ParticipantRegistration>
            {
                new() { SessionId = requestingSessionId, DisplayName = "Requester" },
                new() { SessionId = otherSessionId, DisplayName = "Other" }
            });

        var request = new BroadcastConsentRequest
        {
            RoomId = roomId,
            RequestingSessionId = requestingSessionId
        };

        // Act
        var (status, result) = await service.RequestBroadcastConsentAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(result);
        Assert.Equal(BroadcastConsentState.Pending, result.State);
        Assert.Equal(2, result.PendingSessionIds.Count);
        Assert.Empty(result.ConsentedSessionIds);

        // Verify room was saved with Pending state
        _mockRoomStore.Verify(s => s.SaveAsync(
            $"voice:room:{roomId}",
            It.Is<VoiceRoomData>(d =>
                d.BroadcastState == BroadcastConsentState.Pending &&
                d.BroadcastRequestedBy == requestingSessionId),
            It.IsAny<StateOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);

        // Verify consent request client event was published to all participants
        _mockClientEventPublisher.Verify(p => p.PublishToSessionsAsync(
            It.Is<IEnumerable<string>>(list => list.Count() == 2),
            It.IsAny<VoiceBroadcastConsentRequestEvent>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RequestBroadcastConsent_AlreadyPending_ReturnsConflict()
    {
        // Arrange
        var service = CreateService();
        var roomId = Guid.NewGuid();

        _mockRoomStore.Setup(s => s.GetAsync(
            $"voice:room:{roomId}",
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new VoiceRoomData
            {
                RoomId = roomId,
                SessionId = Guid.NewGuid(),
                BroadcastState = BroadcastConsentState.Pending
            });

        var request = new BroadcastConsentRequest
        {
            RoomId = roomId,
            RequestingSessionId = Guid.NewGuid()
        };

        // Act
        var (status, result) = await service.RequestBroadcastConsentAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.Conflict, status);
        Assert.Null(result);
    }

    [Fact]
    public async Task RespondConsent_AllConsented_SetsApprovedAndPublishesEvent()
    {
        // Arrange
        var service = CreateService();
        var roomId = Guid.NewGuid();
        var session1 = Guid.NewGuid();
        var session2 = Guid.NewGuid();
        var requestedBy = Guid.NewGuid();

        _mockRoomStore.Setup(s => s.GetAsync(
            $"voice:room:{roomId}",
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new VoiceRoomData
            {
                RoomId = roomId,
                SessionId = Guid.NewGuid(),
                BroadcastState = BroadcastConsentState.Pending,
                BroadcastRequestedBy = requestedBy,
                BroadcastConsentedSessions = new HashSet<Guid> { session1 } // session1 already consented
            });

        // Only session1 and session2 are in the room
        _mockEndpointRegistry.Setup(r => r.GetRoomParticipantsAsync(roomId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ParticipantRegistration>
            {
                new() { SessionId = session1 },
                new() { SessionId = session2 }
            });

        var request = new BroadcastConsentResponse
        {
            RoomId = roomId,
            SessionId = session2, // Last one consenting
            Consented = true
        };

        // Act
        var (status, result) = await service.RespondBroadcastConsentAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(result);
        Assert.Equal(BroadcastConsentState.Approved, result.State);
        Assert.Empty(result.PendingSessionIds);

        // Verify approved event was published
        _mockMessageBus.Verify(m => m.TryPublishAsync(
            "voice.room.broadcast.approved",
            It.Is<VoiceRoomBroadcastApprovedEvent>(e => e.RoomId == roomId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RespondConsent_Declined_SetsInactiveAndPublishesDeclineEvent()
    {
        // Arrange
        var service = CreateService();
        var roomId = Guid.NewGuid();
        var decliningSession = Guid.NewGuid();

        _mockRoomStore.Setup(s => s.GetAsync(
            $"voice:room:{roomId}",
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new VoiceRoomData
            {
                RoomId = roomId,
                SessionId = Guid.NewGuid(),
                BroadcastState = BroadcastConsentState.Pending,
                BroadcastRequestedBy = Guid.NewGuid(),
                BroadcastConsentedSessions = new HashSet<Guid>()
            });

        _mockEndpointRegistry.Setup(r => r.GetRoomParticipantsAsync(roomId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ParticipantRegistration>
            {
                new() { SessionId = decliningSession, DisplayName = "Decliner" },
                new() { SessionId = Guid.NewGuid() }
            });

        var request = new BroadcastConsentResponse
        {
            RoomId = roomId,
            SessionId = decliningSession,
            Consented = false
        };

        // Act
        var (status, result) = await service.RespondBroadcastConsentAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(result);
        Assert.Equal(BroadcastConsentState.Inactive, result.State);

        // Verify declined event was published
        _mockMessageBus.Verify(m => m.TryPublishAsync(
            "voice.room.broadcast.declined",
            It.Is<VoiceRoomBroadcastDeclinedEvent>(e =>
                e.RoomId == roomId &&
                e.DeclinedBySessionId == decliningSession),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RespondConsent_PartialConsent_RemainsInPendingState()
    {
        // Arrange
        var service = CreateService();
        var roomId = Guid.NewGuid();
        var session1 = Guid.NewGuid();
        var session2 = Guid.NewGuid();
        var session3 = Guid.NewGuid();

        _mockRoomStore.Setup(s => s.GetAsync(
            $"voice:room:{roomId}",
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new VoiceRoomData
            {
                RoomId = roomId,
                SessionId = Guid.NewGuid(),
                BroadcastState = BroadcastConsentState.Pending,
                BroadcastRequestedBy = session1,
                BroadcastConsentedSessions = new HashSet<Guid>() // No one consented yet
            });

        // Three participants in the room
        _mockEndpointRegistry.Setup(r => r.GetRoomParticipantsAsync(roomId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ParticipantRegistration>
            {
                new() { SessionId = session1 },
                new() { SessionId = session2 },
                new() { SessionId = session3 }
            });

        var request = new BroadcastConsentResponse
        {
            RoomId = roomId,
            SessionId = session1, // First consent
            Consented = true
        };

        // Act
        var (status, result) = await service.RespondBroadcastConsentAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(result);
        Assert.Equal(BroadcastConsentState.Pending, result.State);
        Assert.Single(result.ConsentedSessionIds);
        Assert.Equal(2, result.PendingSessionIds.Count);

        // Verify approved event was NOT published (still waiting)
        _mockMessageBus.Verify(m => m.TryPublishAsync(
            "voice.room.broadcast.approved",
            It.IsAny<VoiceRoomBroadcastApprovedEvent>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task StopBroadcast_ApprovedRoom_SetsInactiveAndPublishesStopEvent()
    {
        // Arrange
        var service = CreateService();
        var roomId = Guid.NewGuid();

        _mockRoomStore.Setup(s => s.GetAsync(
            $"voice:room:{roomId}",
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new VoiceRoomData
            {
                RoomId = roomId,
                SessionId = Guid.NewGuid(),
                BroadcastState = BroadcastConsentState.Approved,
                BroadcastConsentedSessions = new HashSet<Guid> { Guid.NewGuid() }
            });

        _mockEndpointRegistry.Setup(r => r.GetRoomParticipantsAsync(roomId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ParticipantRegistration>
            {
                new() { SessionId = Guid.NewGuid() }
            });

        var request = new StopBroadcastConsentRequest { RoomId = roomId };

        // Act
        var status = await service.StopBroadcastAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);

        // Verify broadcast stopped event was published
        _mockMessageBus.Verify(m => m.TryPublishAsync(
            "voice.room.broadcast.stopped",
            It.Is<VoiceRoomBroadcastStoppedEvent>(e =>
                e.RoomId == roomId &&
                e.Reason == VoiceBroadcastStoppedReason.Manual),
            It.IsAny<CancellationToken>()), Times.Once);

        // Verify room was saved with Inactive state
        _mockRoomStore.Verify(s => s.SaveAsync(
            $"voice:room:{roomId}",
            It.Is<VoiceRoomData>(d => d.BroadcastState == BroadcastConsentState.Inactive),
            It.IsAny<StateOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StopBroadcast_InactiveRoom_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        var roomId = Guid.NewGuid();

        _mockRoomStore.Setup(s => s.GetAsync(
            $"voice:room:{roomId}",
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new VoiceRoomData
            {
                RoomId = roomId,
                SessionId = Guid.NewGuid(),
                BroadcastState = BroadcastConsentState.Inactive
            });

        var request = new StopBroadcastConsentRequest { RoomId = roomId };

        // Act
        var status = await service.StopBroadcastAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
    }

    [Fact]
    public async Task GetBroadcastStatus_ReturnsCurrentState()
    {
        // Arrange
        var service = CreateService();
        var roomId = Guid.NewGuid();
        var session1 = Guid.NewGuid();
        var session2 = Guid.NewGuid();
        var requestedBy = Guid.NewGuid();

        _mockRoomStore.Setup(s => s.GetAsync(
            $"voice:room:{roomId}",
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new VoiceRoomData
            {
                RoomId = roomId,
                SessionId = Guid.NewGuid(),
                BroadcastState = BroadcastConsentState.Pending,
                BroadcastRequestedBy = requestedBy,
                BroadcastConsentedSessions = new HashSet<Guid> { session1 },
                RtpServerUri = "rtp://media.test:5060"
            });

        _mockEndpointRegistry.Setup(r => r.GetRoomParticipantsAsync(roomId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ParticipantRegistration>
            {
                new() { SessionId = session1 },
                new() { SessionId = session2 }
            });

        var request = new BroadcastStatusRequest { RoomId = roomId };

        // Act
        var (status, result) = await service.GetBroadcastStatusAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(result);
        Assert.Equal(BroadcastConsentState.Pending, result.State);
        Assert.Equal(requestedBy, result.RequestedBySessionId);
        Assert.Single(result.ConsentedSessionIds);
        Assert.Contains(session1, result.ConsentedSessionIds);
        Assert.Single(result.PendingSessionIds);
        Assert.Contains(session2, result.PendingSessionIds);
        Assert.Equal("rtp://media.test:5060", result.RtpAudioEndpoint);
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
