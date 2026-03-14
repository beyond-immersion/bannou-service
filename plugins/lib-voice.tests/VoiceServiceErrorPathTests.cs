using BeyondImmersion.Bannou.Voice.ClientEvents;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.ClientEvents;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Messaging.Services;
using BeyondImmersion.BannouService.Permission;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using BeyondImmersion.BannouService.Voice;
using BeyondImmersion.BannouService.Voice.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace BeyondImmersion.BannouService.Voice.Tests;

/// <summary>
/// Unit tests for VoiceService error paths and edge cases.
/// Covers lock failures in CreateVoiceRoom/JoinVoiceRoom, tier upgrade failures,
/// password validation edge cases, and state consistency errors.
/// </summary>
public class VoiceServiceErrorPathTests
{
    private const string STATE_STORE = "voice-statestore";

    private readonly Mock<IStateStoreFactory> _mockStateStoreFactory;
    private readonly Mock<IStateStore<string>> _mockStringStore;
    private readonly Mock<IStateStore<VoiceRoomData>> _mockRoomStore;
    private readonly Mock<IMessageBus> _mockMessageBus;
    private readonly Mock<ILogger<VoiceService>> _mockLogger;
    private readonly VoiceServiceConfiguration _configuration;
    private readonly Mock<ISipEndpointRegistry> _mockEndpointRegistry;
    private readonly Mock<IP2PCoordinator> _mockP2PCoordinator;
    private readonly Mock<IScaledTierCoordinator> _mockScaledTierCoordinator;
    private readonly Mock<IClientEventPublisher> _mockClientEventPublisher;
    private readonly Mock<IPermissionClient> _mockPermissionClient;
    private readonly Mock<IEventConsumer> _mockEventConsumer;
    private readonly Mock<ITelemetryProvider> _mockTelemetryProvider;
    private readonly Mock<IDistributedLockProvider> _mockLockProvider;

    public VoiceServiceErrorPathTests()
    {
        _mockStateStoreFactory = new Mock<IStateStoreFactory>();
        _mockStringStore = new Mock<IStateStore<string>>();
        _mockRoomStore = new Mock<IStateStore<VoiceRoomData>>();
        _mockMessageBus = new Mock<IMessageBus>();
        _mockLogger = new Mock<ILogger<VoiceService>>();
        _configuration = new VoiceServiceConfiguration();
        _mockEndpointRegistry = new Mock<ISipEndpointRegistry>();
        _mockP2PCoordinator = new Mock<IP2PCoordinator>();
        _mockScaledTierCoordinator = new Mock<IScaledTierCoordinator>();
        _mockClientEventPublisher = new Mock<IClientEventPublisher>();
        _mockPermissionClient = new Mock<IPermissionClient>();
        _mockEventConsumer = new Mock<IEventConsumer>();
        _mockTelemetryProvider = new Mock<ITelemetryProvider>();
        _mockLockProvider = new Mock<IDistributedLockProvider>();

        // Default lock acquisition succeeds
        var mockLockResponse = new Mock<ILockResponse>();
        mockLockResponse.Setup(l => l.Success).Returns(true);
        _mockLockProvider.Setup(l => l.LockAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockLockResponse.Object);

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
            _configuration,
            _mockEndpointRegistry.Object,
            _mockP2PCoordinator.Object,
            _mockScaledTierCoordinator.Object,
            _mockEventConsumer.Object,
            _mockClientEventPublisher.Object,
            _mockPermissionClient.Object,
            _mockTelemetryProvider.Object,
            _mockLockProvider.Object);
    }

    #region CreateVoiceRoom Lock Failure Tests

    [Fact]
    public async Task CreateVoiceRoom_WhenLockFails_ReturnsConflict()
    {
        // Arrange
        var service = CreateService();
        var sessionId = Guid.NewGuid();
        var request = new CreateVoiceRoomRequest
        {
            SessionId = sessionId,
            PreferredTier = VoiceTier.P2P,
            Codec = VoiceCodec.Opus,
            MaxParticipants = 6
        };

        // Override default lock setup to fail for session-room locks
        var failedLock = new Mock<ILockResponse>();
        failedLock.Setup(l => l.Success).Returns(false);
        _mockLockProvider.Setup(l => l.LockAsync(
            It.IsAny<string>(),
            It.Is<string>(s => s.Contains("session-room")),
            It.IsAny<string>(),
            It.IsAny<int>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(failedLock.Object);

        // Act
        var (status, result) = await service.CreateVoiceRoomAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.Conflict, status);
        Assert.Null(result);

        // Verify room was NOT saved (lock prevented creation)
        _mockRoomStore.Verify(s => s.SaveAsync(
            It.IsAny<string>(),
            It.IsAny<VoiceRoomData>(),
            It.IsAny<StateOptions?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    #endregion

    #region JoinVoiceRoom Lock Failure Tests

    [Fact]
    public async Task JoinVoiceRoom_AdHoc_WhenLockFails_ReturnsConflict()
    {
        // Arrange
        var service = CreateService();
        var roomId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        _configuration.AdHocRoomsEnabled = true;
        var request = new JoinVoiceRoomRequest
        {
            RoomId = roomId,
            SessionId = sessionId,
            DisplayName = "TestPlayer",
            SipEndpoint = new SipEndpoint { SdpOffer = "offer", IceCandidates = new List<string>() }
        };

        // Room not found (triggers ad-hoc creation path)
        _mockRoomStore.Setup(s => s.GetAsync(
            $"voice:room:{roomId}",
            It.IsAny<CancellationToken>()))
            .ReturnsAsync((VoiceRoomData?)null);

        // Override lock to fail for ad-hoc room creation
        var failedLock = new Mock<ILockResponse>();
        failedLock.Setup(l => l.Success).Returns(false);
        _mockLockProvider.Setup(l => l.LockAsync(
            It.IsAny<string>(),
            It.Is<string>(s => s.Contains("room-create")),
            It.IsAny<string>(),
            It.IsAny<int>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(failedLock.Object);

        // Act
        var (status, result) = await service.JoinVoiceRoomAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.Conflict, status);
        Assert.Null(result);

        // Verify no registration happened
        _mockEndpointRegistry.Verify(r => r.RegisterAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<SipEndpoint>(),
            It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    #endregion

    #region JoinVoiceRoom Tier Upgrade Failure Tests

    [Fact]
    public async Task JoinVoiceRoom_AtCapacity_TierUpgradeDisabled_ReturnsConflict()
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

        _configuration.ScaledTierEnabled = false;
        _configuration.TierUpgradeEnabled = false;

        var roomData = new VoiceRoomData
        {
            RoomId = roomId,
            SessionId = Guid.NewGuid(),
            Tier = VoiceTier.P2P,
            Codec = VoiceCodec.Opus,
            MaxParticipants = 2
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
        var (status, result) = await service.JoinVoiceRoomAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.Conflict, status);
        Assert.Null(result);
    }

    [Fact]
    public async Task JoinVoiceRoom_AtCapacity_ScaledTierDisabled_ReturnsConflict()
    {
        // Arrange - tier upgrade enabled but scaled tier itself disabled
        var service = CreateService();
        var roomId = Guid.NewGuid();
        var request = new JoinVoiceRoomRequest
        {
            RoomId = roomId,
            SessionId = Guid.NewGuid(),
            DisplayName = "TestPlayer",
            SipEndpoint = new SipEndpoint { SdpOffer = "offer", IceCandidates = new List<string>() }
        };

        _configuration.ScaledTierEnabled = false;
        _configuration.TierUpgradeEnabled = true;

        var roomData = new VoiceRoomData
        {
            RoomId = roomId,
            SessionId = Guid.NewGuid(),
            Tier = VoiceTier.P2P,
            Codec = VoiceCodec.Opus,
            MaxParticipants = 2
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
        var (status, result) = await service.JoinVoiceRoomAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.Conflict, status);
        Assert.Null(result);
    }

    [Fact]
    public async Task JoinVoiceRoom_ScaledTierAtCapacity_ReturnsConflict()
    {
        // Arrange - room already in scaled tier but still at capacity
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
            Tier = VoiceTier.Scaled,
            Codec = VoiceCodec.Opus,
            MaxParticipants = 100,
            RtpServerUri = "rtpengine://rtp.example.com:22222"
        };

        _mockRoomStore.Setup(s => s.GetAsync(
            $"voice:room:{roomId}",
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(roomData);

        _mockEndpointRegistry.Setup(r => r.GetParticipantCountAsync(roomId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(100);

        _mockScaledTierCoordinator.Setup(s => s.CanAcceptNewParticipantAsync(roomId, 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        var (status, result) = await service.JoinVoiceRoomAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.Conflict, status);
        Assert.Null(result);
    }

    #endregion

    #region JoinVoiceRoom Password Validation Edge Cases

    [Fact]
    public async Task JoinVoiceRoom_PasswordProtected_NullPassword_ReturnsForbidden()
    {
        // Arrange - room requires password but none provided
        var service = CreateService();
        var roomId = Guid.NewGuid();
        var request = new JoinVoiceRoomRequest
        {
            RoomId = roomId,
            SessionId = Guid.NewGuid(),
            DisplayName = "TestPlayer",
            SipEndpoint = new SipEndpoint { SdpOffer = "offer", IceCandidates = new List<string>() },
            Password = null
        };

        _mockRoomStore.Setup(s => s.GetAsync(
            $"voice:room:{roomId}",
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new VoiceRoomData
            {
                RoomId = roomId,
                SessionId = Guid.NewGuid(),
                Tier = VoiceTier.P2P,
                Codec = VoiceCodec.Opus,
                MaxParticipants = 6,
                Password = "required-password"
            });

        // Act
        var (status, result) = await service.JoinVoiceRoomAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.Forbidden, status);
        Assert.Null(result);
    }

    [Fact]
    public async Task JoinVoiceRoom_NoPassword_NonProtectedRoom_Succeeds()
    {
        // Arrange - room has no password, join without password should succeed
        var service = CreateService();
        var roomId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var request = new JoinVoiceRoomRequest
        {
            RoomId = roomId,
            SessionId = sessionId,
            DisplayName = "TestPlayer",
            SipEndpoint = new SipEndpoint { SdpOffer = "offer", IceCandidates = new List<string>() },
            Password = null
        };

        _mockRoomStore.Setup(s => s.GetAsync(
            $"voice:room:{roomId}",
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new VoiceRoomData
            {
                RoomId = roomId,
                SessionId = Guid.NewGuid(),
                Tier = VoiceTier.P2P,
                Codec = VoiceCodec.Opus,
                MaxParticipants = 6,
                Password = null
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
        var (status, result) = await service.JoinVoiceRoomAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(result);
    }

    [Fact]
    public async Task JoinVoiceRoom_EmptyPassword_NonProtectedRoom_Succeeds()
    {
        // Arrange - room password is empty string (treated as no password)
        var service = CreateService();
        var roomId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var request = new JoinVoiceRoomRequest
        {
            RoomId = roomId,
            SessionId = sessionId,
            DisplayName = "TestPlayer",
            SipEndpoint = new SipEndpoint { SdpOffer = "offer", IceCandidates = new List<string>() },
            Password = null
        };

        _mockRoomStore.Setup(s => s.GetAsync(
            $"voice:room:{roomId}",
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new VoiceRoomData
            {
                RoomId = roomId,
                SessionId = Guid.NewGuid(),
                Tier = VoiceTier.P2P,
                Codec = VoiceCodec.Opus,
                MaxParticipants = 6,
                Password = "" // Empty string treated as no password
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
        var (status, result) = await service.JoinVoiceRoomAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(result);
    }

    #endregion

    #region JoinVoiceRoom Scaled Tier Join Tests

    [Fact]
    public async Task JoinVoiceRoom_ScaledTier_ReturnsRtpServerUriAndEmptyPeers()
    {
        // Arrange - room already in scaled tier mode
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

        var rtpUri = "rtpengine://rtp.example.com:22222";
        _mockRoomStore.Setup(s => s.GetAsync(
            $"voice:room:{roomId}",
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new VoiceRoomData
            {
                RoomId = roomId,
                SessionId = Guid.NewGuid(),
                Tier = VoiceTier.Scaled,
                Codec = VoiceCodec.G711,
                MaxParticipants = 100,
                RtpServerUri = rtpUri
            });

        _mockEndpointRegistry.Setup(r => r.GetParticipantCountAsync(roomId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(5);
        _mockScaledTierCoordinator.Setup(s => s.CanAcceptNewParticipantAsync(roomId, 5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _mockEndpointRegistry.Setup(r => r.RegisterAsync(
            roomId, sessionId, It.IsAny<SipEndpoint>(), "TestPlayer", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _mockEndpointRegistry.Setup(r => r.GetRoomParticipantsAsync(roomId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ParticipantRegistration>());

        // Act
        var (status, result) = await service.JoinVoiceRoomAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(result);
        Assert.Equal(VoiceTier.Scaled, result.Tier);
        Assert.Equal(VoiceCodec.G711, result.Codec);
        Assert.Equal(rtpUri, result.RtpServerUri);
        Assert.Empty(result.Peers);
        Assert.False(result.TierUpgradePending);
    }

    #endregion

    #region AnswerPeer Edge Cases

    [Fact]
    public async Task AnswerPeer_WhenSenderNotFound_UsesUnknownDisplayName()
    {
        // Arrange - target exists but sender is not found in the room
        var service = CreateService();
        var roomId = Guid.NewGuid();
        var senderSessionId = Guid.NewGuid();
        var targetSessionId = Guid.NewGuid();
        var request = new AnswerPeerRequest
        {
            RoomId = roomId,
            SenderSessionId = senderSessionId,
            TargetSessionId = targetSessionId,
            SdpAnswer = "sdp-answer",
            IceCandidates = new List<string>()
        };

        // Target participant exists
        _mockEndpointRegistry.Setup(r => r.GetParticipantAsync(roomId, targetSessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ParticipantRegistration
            {
                SessionId = targetSessionId,
                DisplayName = "Target"
            });

        // Sender participant NOT found
        _mockEndpointRegistry.Setup(r => r.GetParticipantAsync(roomId, senderSessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ParticipantRegistration?)null);

        VoicePeerUpdatedClientEvent? capturedEvent = null;
        _mockClientEventPublisher.Setup(p => p.PublishToSessionsAsync(
            It.IsAny<IEnumerable<string>>(), It.IsAny<VoicePeerUpdatedClientEvent>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<string>, VoicePeerUpdatedClientEvent, CancellationToken>(
                (_, evt, _) => capturedEvent = evt)
            .ReturnsAsync(1);

        // Act
        var status = await service.AnswerPeerAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(capturedEvent);
        Assert.Equal("Unknown", capturedEvent.Peer.DisplayName);
    }

    #endregion

    #region PeerHeartbeat Edge Cases

    [Fact]
    public async Task PeerHeartbeat_WhenUpdateSucceeds_ReturnsOk()
    {
        // Arrange
        var service = CreateService();
        var roomId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var request = new PeerHeartbeatRequest { RoomId = roomId, SessionId = sessionId };

        _mockEndpointRegistry.Setup(r => r.UpdateHeartbeatAsync(roomId, sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var status = await service.PeerHeartbeatAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
    }

    #endregion

    #region DeleteVoiceRoom Scaled Tier Tests

    [Fact]
    public async Task DeleteVoiceRoom_ScaledTier_ReleasesRtpServerResources()
    {
        // Arrange - deleting a scaled tier room should release RTP server resources
        var service = CreateService();
        var roomId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var request = new DeleteVoiceRoomRequest { RoomId = roomId };

        _mockRoomStore.Setup(s => s.GetAsync(
            $"voice:room:{roomId}",
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new VoiceRoomData
            {
                RoomId = roomId,
                SessionId = sessionId,
                Tier = VoiceTier.Scaled,
                Codec = VoiceCodec.G711,
                RtpServerUri = "rtpengine://rtp.example.com:22222"
            });

        _mockEndpointRegistry.Setup(r => r.GetRoomParticipantsAsync(roomId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ParticipantRegistration>());

        // Act
        var status = await service.DeleteVoiceRoomAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, status);

        // Verify RTP server resources were released
        _mockScaledTierCoordinator.Verify(s => s.ReleaseRtpServerAsync(
            roomId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteVoiceRoom_P2PTier_DoesNotReleaseRtpServerResources()
    {
        // Arrange - deleting a P2P tier room should NOT release RTP server resources
        var service = CreateService();
        var roomId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var request = new DeleteVoiceRoomRequest { RoomId = roomId };

        _mockRoomStore.Setup(s => s.GetAsync(
            $"voice:room:{roomId}",
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new VoiceRoomData
            {
                RoomId = roomId,
                SessionId = sessionId,
                Tier = VoiceTier.P2P,
                Codec = VoiceCodec.Opus,
                RtpServerUri = null
            });

        _mockEndpointRegistry.Setup(r => r.GetRoomParticipantsAsync(roomId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ParticipantRegistration>());

        // Act
        var status = await service.DeleteVoiceRoomAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, status);

        // Verify RTP server resources were NOT released
        _mockScaledTierCoordinator.Verify(s => s.ReleaseRtpServerAsync(
            It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    #endregion

    #region CreateVoiceRoom Response Validation Tests

    [Fact]
    public async Task CreateVoiceRoom_ReturnsCorrectResponseShape()
    {
        // Arrange
        var service = CreateService();
        var sessionId = Guid.NewGuid();
        var request = new CreateVoiceRoomRequest
        {
            SessionId = sessionId,
            PreferredTier = VoiceTier.P2P,
            Codec = VoiceCodec.G711,
            MaxParticipants = 4,
            AutoCleanup = true,
            Password = "secret"
        };

        _mockStringStore.Setup(s => s.GetAsync(
            $"voice:session-room:{sessionId}",
            It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        // Act
        var (status, result) = await service.CreateVoiceRoomAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(result);
        Assert.NotEqual(Guid.Empty, result.RoomId);
        Assert.Equal(sessionId, result.SessionId);
        Assert.Equal(VoiceTier.P2P, result.Tier);
        Assert.Equal(VoiceCodec.G711, result.Codec);
        Assert.Equal(4, result.MaxParticipants);
        Assert.Equal(0, result.CurrentParticipants);
        Assert.Empty(result.Participants);
        Assert.Null(result.RtpServerUri);
        Assert.True(result.AutoCleanup);
        Assert.True(result.IsPasswordProtected);
        Assert.Equal(BroadcastConsentState.Inactive, result.BroadcastState);
    }

    [Fact]
    public async Task CreateVoiceRoom_DefaultCodec_UsesOpus()
    {
        // Arrange - when codec is null, should default to Opus
        var service = CreateService();
        var sessionId = Guid.NewGuid();
        var request = new CreateVoiceRoomRequest
        {
            SessionId = sessionId,
            PreferredTier = null,
            Codec = null,
            MaxParticipants = 0
        };

        _mockStringStore.Setup(s => s.GetAsync(
            $"voice:session-room:{sessionId}",
            It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        // Act
        var (status, result) = await service.CreateVoiceRoomAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(result);
        Assert.Equal(VoiceTier.P2P, result.Tier);
        Assert.Equal(VoiceCodec.Opus, result.Codec);
    }

    #endregion

    #region JoinVoiceRoom AdHoc Concurrent Creation Tests

    [Fact]
    public async Task JoinVoiceRoom_AdHoc_RoomCreatedBetweenLockAndCheck_JoinsExistingRoom()
    {
        // Arrange - room doesn't exist initially but appears after acquiring lock
        // (another instance created it between our first check and lock acquisition)
        var service = CreateService();
        var roomId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        _configuration.AdHocRoomsEnabled = true;
        var request = new JoinVoiceRoomRequest
        {
            RoomId = roomId,
            SessionId = sessionId,
            DisplayName = "TestPlayer",
            SipEndpoint = new SipEndpoint { SdpOffer = "offer", IceCandidates = new List<string>() }
        };

        var callCount = 0;
        _mockRoomStore.Setup(s => s.GetAsync(
            $"voice:room:{roomId}",
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                if (callCount == 1)
                {
                    // First call: room doesn't exist (triggers ad-hoc path)
                    return null;
                }
                // Second call (after lock): room now exists (created by another instance)
                return new VoiceRoomData
                {
                    RoomId = roomId,
                    SessionId = Guid.NewGuid(),
                    Tier = VoiceTier.P2P,
                    Codec = VoiceCodec.Opus,
                    MaxParticipants = 6
                };
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
        var (status, result) = await service.JoinVoiceRoomAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(result);

        // Verify room was NOT saved again (we joined the existing one created by the other instance)
        _mockRoomStore.Verify(s => s.SaveAsync(
            $"voice:room:{roomId}",
            It.IsAny<VoiceRoomData>(),
            It.IsAny<StateOptions?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    #endregion

    #region LeaveVoiceRoom Broadcast Lock Failure Tests

    [Fact]
    public async Task LeaveVoiceRoom_BroadcastLockFails_StillReturnsOk()
    {
        // Arrange - leave should succeed even if broadcast consent lock fails
        // (broadcast state may be stale but the leave itself is not blocked)
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
            It.IsAny<IEnumerable<string>>(), It.IsAny<VoicePeerLeftClientEvent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Override lock to fail for broadcast-consent locks
        var failedLock = new Mock<ILockResponse>();
        failedLock.Setup(l => l.Success).Returns(false);
        _mockLockProvider.Setup(l => l.LockAsync(
            It.IsAny<string>(),
            It.Is<string>(s => s.Contains("broadcast-consent")),
            It.IsAny<string>(),
            It.IsAny<int>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(failedLock.Object);

        // Act
        var status = await service.LeaveVoiceRoomAsync(request, TestContext.Current.CancellationToken);

        // Assert - leave still succeeds even though broadcast lock failed
        Assert.Equal(StatusCodes.OK, status);
    }

    #endregion
}
