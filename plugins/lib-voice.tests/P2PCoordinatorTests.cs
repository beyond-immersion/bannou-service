using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.TestUtilities;
using BeyondImmersion.BannouService.Voice;
using BeyondImmersion.BannouService.Voice.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace BeyondImmersion.BannouService.Voice.Tests;

/// <summary>
/// Unit tests for <see cref="P2PCoordinator"/>.
/// Tests P2P capacity checks, mesh peer generation, tier upgrade decisions,
/// and P2P connection info building.
/// </summary>
public class P2PCoordinatorTests
{
    private readonly Mock<ISipEndpointRegistry> _mockEndpointRegistry;
    private readonly Mock<ILogger<P2PCoordinator>> _mockLogger;
    private readonly VoiceServiceConfiguration _configuration;
    private readonly Mock<ITelemetryProvider> _mockTelemetryProvider;

    public P2PCoordinatorTests()
    {
        _mockEndpointRegistry = new Mock<ISipEndpointRegistry>();
        _mockLogger = new Mock<ILogger<P2PCoordinator>>();
        _configuration = new VoiceServiceConfiguration
        {
            P2PMaxParticipants = 6
        };
        _mockTelemetryProvider = new Mock<ITelemetryProvider>();
    }

    private P2PCoordinator CreateCoordinator()
    {
        return new P2PCoordinator(
            _mockEndpointRegistry.Object,
            _mockLogger.Object,
            _configuration,
            _mockTelemetryProvider.Object);
    }

    #region Constructor Tests

    [Fact]
    public void ConstructorIsValid()
    {
        ServiceConstructorValidator.ValidateServiceConstructor<P2PCoordinator>();
        Assert.NotNull(CreateCoordinator());
    }

    #endregion

    #region CanAcceptNewParticipantAsync Tests

    [Fact]
    public async Task CanAcceptNewParticipant_BelowMax_ReturnsTrue()
    {
        // Arrange
        var coordinator = CreateCoordinator();
        var roomId = Guid.NewGuid();

        // Act
        var result = await coordinator.CanAcceptNewParticipantAsync(roomId, 3, CancellationToken.None);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task CanAcceptNewParticipant_AtMax_ReturnsFalse()
    {
        // Arrange
        var coordinator = CreateCoordinator();
        var roomId = Guid.NewGuid();

        // Act - at 6/6
        var result = await coordinator.CanAcceptNewParticipantAsync(roomId, 6, CancellationToken.None);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task CanAcceptNewParticipant_AboveMax_ReturnsFalse()
    {
        // Arrange
        var coordinator = CreateCoordinator();
        var roomId = Guid.NewGuid();

        // Act
        var result = await coordinator.CanAcceptNewParticipantAsync(roomId, 10, CancellationToken.None);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task CanAcceptNewParticipant_AtZero_ReturnsTrue()
    {
        // Arrange
        var coordinator = CreateCoordinator();
        var roomId = Guid.NewGuid();

        // Act
        var result = await coordinator.CanAcceptNewParticipantAsync(roomId, 0, CancellationToken.None);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task CanAcceptNewParticipant_CustomMaxConfig_UsesConfigValue()
    {
        // Arrange
        _configuration.P2PMaxParticipants = 2;
        var coordinator = CreateCoordinator();
        var roomId = Guid.NewGuid();

        // Act - 2/2 = at capacity
        var result = await coordinator.CanAcceptNewParticipantAsync(roomId, 2, CancellationToken.None);

        // Assert
        Assert.False(result);
    }

    #endregion

    #region ShouldUpgradeToScaledAsync Tests

    [Fact]
    public async Task ShouldUpgradeToScaled_BelowMax_ReturnsFalse()
    {
        // Arrange
        var coordinator = CreateCoordinator();
        var roomId = Guid.NewGuid();

        // Act - 5/6 = not exceeding
        var result = await coordinator.ShouldUpgradeToScaledAsync(roomId, 5, CancellationToken.None);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task ShouldUpgradeToScaled_AtMax_ReturnsFalse()
    {
        // Arrange
        var coordinator = CreateCoordinator();
        var roomId = Guid.NewGuid();

        // Act - 6/6 = at capacity but not exceeding (> not >=)
        var result = await coordinator.ShouldUpgradeToScaledAsync(roomId, 6, CancellationToken.None);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task ShouldUpgradeToScaled_AboveMax_ReturnsTrue()
    {
        // Arrange
        var coordinator = CreateCoordinator();
        var roomId = Guid.NewGuid();

        // Act - 7/6 = exceeding
        var result = await coordinator.ShouldUpgradeToScaledAsync(roomId, 7, CancellationToken.None);

        // Assert
        Assert.True(result);
    }

    #endregion

    #region GetMeshPeersForNewJoinAsync Tests

    [Fact]
    public async Task GetMeshPeersForNewJoin_ExcludesJoiningSession()
    {
        // Arrange
        var coordinator = CreateCoordinator();
        var roomId = Guid.NewGuid();
        var joiningSessionId = Guid.NewGuid();
        var existingSessionId = Guid.NewGuid();

        _mockEndpointRegistry.Setup(r => r.GetRoomParticipantsAsync(roomId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ParticipantRegistration>
            {
                new()
                {
                    SessionId = joiningSessionId,
                    DisplayName = "JoiningPlayer",
                    Endpoint = new SipEndpoint { SdpOffer = "joining-sdp" }
                },
                new()
                {
                    SessionId = existingSessionId,
                    DisplayName = "ExistingPlayer",
                    Endpoint = new SipEndpoint { SdpOffer = "existing-sdp" }
                }
            });

        // Act
        var peers = await coordinator.GetMeshPeersForNewJoinAsync(roomId, joiningSessionId, CancellationToken.None);

        // Assert
        Assert.Single(peers);
        Assert.Equal(existingSessionId, peers[0].SessionId);
        Assert.Equal("ExistingPlayer", peers[0].DisplayName);
    }

    [Fact]
    public async Task GetMeshPeersForNewJoin_EmptyRoom_ReturnsEmptyList()
    {
        // Arrange
        var coordinator = CreateCoordinator();
        var roomId = Guid.NewGuid();

        _mockEndpointRegistry.Setup(r => r.GetRoomParticipantsAsync(roomId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ParticipantRegistration>());

        // Act
        var peers = await coordinator.GetMeshPeersForNewJoinAsync(roomId, Guid.NewGuid(), CancellationToken.None);

        // Assert
        Assert.Empty(peers);
    }

    [Fact]
    public async Task GetMeshPeersForNewJoin_NullEndpoint_CreatesDefaultEndpoint()
    {
        // Arrange
        var coordinator = CreateCoordinator();
        var roomId = Guid.NewGuid();
        var joiningSessionId = Guid.NewGuid();
        var otherSessionId = Guid.NewGuid();

        _mockEndpointRegistry.Setup(r => r.GetRoomParticipantsAsync(roomId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ParticipantRegistration>
            {
                new()
                {
                    SessionId = otherSessionId,
                    DisplayName = "PeerWithNoEndpoint",
                    Endpoint = null // Null endpoint
                }
            });

        // Act
        var peers = await coordinator.GetMeshPeersForNewJoinAsync(roomId, joiningSessionId, CancellationToken.None);

        // Assert
        Assert.Single(peers);
        Assert.NotNull(peers[0].SipEndpoint);
        Assert.Equal(string.Empty, peers[0].SipEndpoint.SdpOffer);
    }

    [Fact]
    public async Task GetMeshPeersForNewJoin_MultipleExistingPeers_ReturnsAll()
    {
        // Arrange
        var coordinator = CreateCoordinator();
        var roomId = Guid.NewGuid();
        var joiningSessionId = Guid.NewGuid();
        var session1 = Guid.NewGuid();
        var session2 = Guid.NewGuid();
        var session3 = Guid.NewGuid();

        _mockEndpointRegistry.Setup(r => r.GetRoomParticipantsAsync(roomId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ParticipantRegistration>
            {
                new() { SessionId = session1, DisplayName = "Player1", Endpoint = new SipEndpoint { SdpOffer = "sdp1" } },
                new() { SessionId = session2, DisplayName = "Player2", Endpoint = new SipEndpoint { SdpOffer = "sdp2" } },
                new() { SessionId = session3, DisplayName = "Player3", Endpoint = new SipEndpoint { SdpOffer = "sdp3" } }
            });

        // Act
        var peers = await coordinator.GetMeshPeersForNewJoinAsync(roomId, joiningSessionId, CancellationToken.None);

        // Assert
        Assert.Equal(3, peers.Count);
    }

    #endregion

    #region GetP2PMaxParticipants Tests

    [Fact]
    public void GetP2PMaxParticipants_ReturnsConfiguredValue()
    {
        // Arrange
        _configuration.P2PMaxParticipants = 10;
        var coordinator = CreateCoordinator();

        // Act
        var result = coordinator.GetP2PMaxParticipants();

        // Assert
        Assert.Equal(10, result);
    }

    #endregion

    #region BuildP2PConnectionInfoAsync Tests

    [Fact]
    public async Task BuildP2PConnectionInfo_ReturnsCorrectResponse()
    {
        // Arrange
        var coordinator = CreateCoordinator();
        var roomId = Guid.NewGuid();
        var peers = new List<VoicePeer>
        {
            new() { SessionId = Guid.NewGuid(), DisplayName = "Peer1" }
        };
        var stunServers = new List<string> { "stun:stun.example.com:3478" };

        // Act
        var result = await coordinator.BuildP2PConnectionInfoAsync(
            roomId, peers, VoiceCodec.Opus, stunServers, false, CancellationToken.None);

        // Assert
        Assert.Equal(roomId, result.RoomId);
        Assert.Equal(VoiceTier.P2P, result.Tier);
        Assert.Equal(VoiceCodec.Opus, result.Codec);
        Assert.Single(result.Peers);
        Assert.Null(result.RtpServerUri); // P2P mode has no RTP server
        Assert.Equal(stunServers, result.StunServers);
        Assert.False(result.TierUpgradePending);
    }

    [Fact]
    public async Task BuildP2PConnectionInfo_WithTierUpgradePending_SetsFlag()
    {
        // Arrange
        var coordinator = CreateCoordinator();
        var roomId = Guid.NewGuid();

        // Act
        var result = await coordinator.BuildP2PConnectionInfoAsync(
            roomId, new List<VoicePeer>(), VoiceCodec.G711, new List<string>(), true, CancellationToken.None);

        // Assert
        Assert.True(result.TierUpgradePending);
        Assert.Equal(VoiceCodec.G711, result.Codec);
    }

    [Fact]
    public async Task BuildP2PConnectionInfo_EmptyPeers_ReturnsEmptyPeerList()
    {
        // Arrange
        var coordinator = CreateCoordinator();
        var roomId = Guid.NewGuid();

        // Act
        var result = await coordinator.BuildP2PConnectionInfoAsync(
            roomId, new List<VoicePeer>(), VoiceCodec.Opus, new List<string>(), false, CancellationToken.None);

        // Assert
        Assert.Empty(result.Peers);
    }

    #endregion
}
