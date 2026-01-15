using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.TestUtilities;
using BeyondImmersion.BannouService.Voice;
using BeyondImmersion.BannouService.Voice.Clients;
using BeyondImmersion.BannouService.Voice.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace BeyondImmersion.BannouService.Voice.Tests;

public class ScaledTierCoordinatorTests
{
    private readonly Mock<IKamailioClient> _mockKamailioClient;
    private readonly Mock<IRtpEngineClient> _mockRtpEngineClient;
    private readonly Mock<ILogger<ScaledTierCoordinator>> _mockLogger;
    private readonly Mock<IMessageBus> _mockMessageBus;
    private readonly VoiceServiceConfiguration _configuration;

    public ScaledTierCoordinatorTests()
    {
        _mockKamailioClient = new Mock<IKamailioClient>();
        _mockRtpEngineClient = new Mock<IRtpEngineClient>();
        _mockLogger = new Mock<ILogger<ScaledTierCoordinator>>();
        _mockMessageBus = new Mock<IMessageBus>();
        _configuration = new VoiceServiceConfiguration
        {
            ScaledMaxParticipants = 100,
            SipDomain = "voice.bannou",
            SipPasswordSalt = "test-salt-12345",
            KamailioHost = "localhost",
            KamailioRpcPort = 5080,
            RtpEngineHost = "localhost",
            RtpEnginePort = 22222,
            StunServers = "stun:stun.l.google.com:19302,stun:stun2.l.google.com:19302"
        };
    }

    private ScaledTierCoordinator CreateCoordinator()
    {
        return new ScaledTierCoordinator(
            _mockKamailioClient.Object,
            _mockRtpEngineClient.Object,
            _mockLogger.Object,
            _mockMessageBus.Object,
            _configuration);
    }

    #region Constructor Tests

    [Fact]
    public void ConstructorIsValid()
    {
        ServiceConstructorValidator.ValidateServiceConstructor<ScaledTierCoordinator>();
        Assert.NotNull(CreateCoordinator());
    }

    #endregion

    #region CanAcceptNewParticipantAsync Tests

    [Fact]
    public async Task CanAcceptNewParticipant_WhenBelowCapacity_ReturnsTrue()
    {
        // Arrange
        var coordinator = CreateCoordinator();
        var roomId = Guid.NewGuid();

        // Act
        var result = await coordinator.CanAcceptNewParticipantAsync(roomId, 50, CancellationToken.None);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task CanAcceptNewParticipant_WhenAtCapacity_ReturnsFalse()
    {
        // Arrange
        var coordinator = CreateCoordinator();
        var roomId = Guid.NewGuid();

        // Act
        var result = await coordinator.CanAcceptNewParticipantAsync(roomId, 100, CancellationToken.None);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task CanAcceptNewParticipant_WhenAboveCapacity_ReturnsFalse()
    {
        // Arrange
        var coordinator = CreateCoordinator();
        var roomId = Guid.NewGuid();

        // Act
        var result = await coordinator.CanAcceptNewParticipantAsync(roomId, 150, CancellationToken.None);

        // Assert
        Assert.False(result);
    }

    #endregion

    #region GetScaledMaxParticipants Tests

    [Fact]
    public void GetScaledMaxParticipants_ReturnsConfiguredValue()
    {
        // Arrange
        var coordinator = CreateCoordinator();

        // Act
        var result = coordinator.GetScaledMaxParticipants();

        // Assert
        Assert.Equal(100, result);
    }

    [Fact]
    public void GetScaledMaxParticipants_WithZeroConfiguration_ReturnsDefault()
    {
        // Arrange
        var config = new VoiceServiceConfiguration { ScaledMaxParticipants = 0 };
        var coordinator = new ScaledTierCoordinator(
            _mockKamailioClient.Object,
            _mockRtpEngineClient.Object,
            _mockLogger.Object,
            _mockMessageBus.Object,
            config);

        // Act
        var result = coordinator.GetScaledMaxParticipants();

        // Assert
        Assert.Equal(100, result); // Default fallback
    }

    #endregion

    #region GenerateSipCredentials Tests

    [Fact]
    public void GenerateSipCredentials_WithValidSession_ReturnsCredentials()
    {
        // Arrange
        var coordinator = CreateCoordinator();
        var sessionId = "session-abc-12345";
        var roomId = Guid.NewGuid();

        // Act
        var credentials = coordinator.GenerateSipCredentials(sessionId, roomId);

        // Assert
        Assert.NotNull(credentials);
        Assert.StartsWith("voice-", credentials.Username);
        // Username is "voice-" + first 8 chars of sessionId = "voice-session-"
        Assert.Equal("voice-session-", credentials.Username);
        Assert.NotEmpty(credentials.Password);
        Assert.Equal(32, credentials.Password.Length); // SHA256 first 32 chars
        Assert.Contains("voice.bannou", credentials.ConferenceUri);
        Assert.Contains(roomId.ToString(), credentials.ConferenceUri);
        Assert.True(credentials.ExpiresAt > DateTimeOffset.UtcNow);
    }

    [Fact]
    public void GenerateSipCredentials_WithEmptySessionId_ThrowsArgumentException()
    {
        // Arrange
        var coordinator = CreateCoordinator();
        var roomId = Guid.NewGuid();

        // Act & Assert
        Assert.Throws<ArgumentException>(() => coordinator.GenerateSipCredentials(string.Empty, roomId));
    }

    [Fact]
    public void GenerateSipCredentials_SameInputs_ReturnsSameCredentials()
    {
        // Arrange
        var coordinator = CreateCoordinator();
        var sessionId = "session-xyz-67890";
        var roomId = Guid.NewGuid();

        // Act
        var creds1 = coordinator.GenerateSipCredentials(sessionId, roomId);
        var creds2 = coordinator.GenerateSipCredentials(sessionId, roomId);

        // Assert - Deterministic password generation
        Assert.Equal(creds1.Password, creds2.Password);
        Assert.Equal(creds1.Username, creds2.Username);
        Assert.Equal(creds1.ConferenceUri, creds2.ConferenceUri);
    }

    [Fact]
    public void GenerateSipCredentials_DifferentSessions_ReturnsDifferentPasswords()
    {
        // Arrange
        var coordinator = CreateCoordinator();
        var roomId = Guid.NewGuid();

        // Act
        var creds1 = coordinator.GenerateSipCredentials("session-1", roomId);
        var creds2 = coordinator.GenerateSipCredentials("session-2", roomId);

        // Assert
        Assert.NotEqual(creds1.Password, creds2.Password);
    }

    [Fact]
    public void GenerateSipCredentials_ShortSessionId_HandlesGracefully()
    {
        // Arrange
        var coordinator = CreateCoordinator();
        var roomId = Guid.NewGuid();

        // Act - Session ID shorter than 8 chars
        var credentials = coordinator.GenerateSipCredentials("abc", roomId);

        // Assert
        Assert.StartsWith("voice-abc", credentials.Username);
    }

    #endregion

    #region AllocateRtpServerAsync Tests

    [Fact]
    public async Task AllocateRtpServer_WhenRtpEngineHealthy_ReturnsUri()
    {
        // Arrange
        var coordinator = CreateCoordinator();
        var roomId = Guid.NewGuid();

        _mockRtpEngineClient.Setup(r => r.IsHealthyAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await coordinator.AllocateRtpServerAsync(roomId, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.StartsWith("udp://", result);
        Assert.Contains("localhost", result);
        Assert.Contains("22222", result);
    }

    [Fact]
    public async Task AllocateRtpServer_WhenRtpEngineUnhealthy_ThrowsInvalidOperationException()
    {
        // Arrange
        var coordinator = CreateCoordinator();
        var roomId = Guid.NewGuid();

        _mockRtpEngineClient.Setup(r => r.IsHealthyAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => coordinator.AllocateRtpServerAsync(roomId, CancellationToken.None));
    }

    #endregion

    #region ReleaseRtpServerAsync Tests

    [Fact]
    public async Task ReleaseRtpServer_WithActiveStreams_DeletesSession()
    {
        // Arrange
        var coordinator = CreateCoordinator();
        var roomId = Guid.NewGuid();

        _mockRtpEngineClient.Setup(r => r.QueryAsync($"room-{roomId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RtpEngineQueryResponse
            {
                Result = "ok",
                Streams = new Dictionary<string, object> { { "stream-1", new object() } }
            });

        _mockRtpEngineClient.Setup(r => r.DeleteAsync($"room-{roomId}", "bannou", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RtpEngineDeleteResponse { Result = "ok" });

        // Act
        await coordinator.ReleaseRtpServerAsync(roomId, CancellationToken.None);

        // Assert
        _mockRtpEngineClient.Verify(r => r.DeleteAsync($"room-{roomId}", "bannou", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ReleaseRtpServer_WithNoStreams_DoesNotCallDelete()
    {
        // Arrange
        var coordinator = CreateCoordinator();
        var roomId = Guid.NewGuid();

        _mockRtpEngineClient.Setup(r => r.QueryAsync($"room-{roomId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RtpEngineQueryResponse
            {
                Result = "ok",
                Streams = new Dictionary<string, object>()
            });

        // Act
        await coordinator.ReleaseRtpServerAsync(roomId, CancellationToken.None);

        // Assert
        _mockRtpEngineClient.Verify(r => r.DeleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ReleaseRtpServer_WhenQueryFails_ThrowsException()
    {
        // Arrange
        var coordinator = CreateCoordinator();
        var roomId = Guid.NewGuid();

        _mockRtpEngineClient.Setup(r => r.QueryAsync($"room-{roomId}", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Query failed"));

        // Act & Assert - Should throw (fail-fast behavior per Phase B1)
        await Assert.ThrowsAsync<Exception>(() => coordinator.ReleaseRtpServerAsync(roomId, CancellationToken.None));
    }

    #endregion

    #region BuildScaledConnectionInfoAsync Tests

    [Fact]
    public async Task BuildScaledConnectionInfo_ReturnsCorrectInfo()
    {
        // Arrange
        var coordinator = CreateCoordinator();
        var roomId = Guid.NewGuid();
        var sessionId = "session-test-123";
        var rtpServerUri = "udp://localhost:22222";

        // Act
        var result = await coordinator.BuildScaledConnectionInfoAsync(
            roomId, sessionId, rtpServerUri, "opus", CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(roomId, result.RoomId);
        Assert.Equal(VoiceTier.Scaled, result.Tier);
        Assert.Equal(VoiceCodec.Opus, result.Codec);
        Assert.Empty(result.Peers); // No peers in scaled mode
        Assert.Equal(rtpServerUri, result.RtpServerUri);
        Assert.NotNull(result.StunServers);
        Assert.Contains("stun:stun.l.google.com:19302", result.StunServers);
    }

    [Fact]
    public async Task BuildScaledConnectionInfo_WithG711Codec_ParsesCorrectly()
    {
        // Arrange
        var coordinator = CreateCoordinator();
        var roomId = Guid.NewGuid();

        // Act
        var result = await coordinator.BuildScaledConnectionInfoAsync(
            roomId, "session-1", "udp://host:1234", "g711", CancellationToken.None);

        // Assert
        Assert.Equal(VoiceCodec.G711, result.Codec);
    }

    [Fact]
    public async Task BuildScaledConnectionInfo_WithG722Codec_ParsesCorrectly()
    {
        // Arrange
        var coordinator = CreateCoordinator();
        var roomId = Guid.NewGuid();

        // Act
        var result = await coordinator.BuildScaledConnectionInfoAsync(
            roomId, "session-1", "udp://host:1234", "g722", CancellationToken.None);

        // Assert
        Assert.Equal(VoiceCodec.G722, result.Codec);
    }

    [Fact]
    public async Task BuildScaledConnectionInfo_WithUnknownCodec_DefaultsToOpus()
    {
        // Arrange
        var coordinator = CreateCoordinator();
        var roomId = Guid.NewGuid();

        // Act
        var result = await coordinator.BuildScaledConnectionInfoAsync(
            roomId, "session-1", "udp://host:1234", "unknown", CancellationToken.None);

        // Assert
        Assert.Equal(VoiceCodec.Opus, result.Codec);
    }

    #endregion
}
