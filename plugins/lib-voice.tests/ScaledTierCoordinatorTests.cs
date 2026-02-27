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
    private readonly Mock<IRtpEngineClient> _mockRtpEngineClient;
    private readonly Mock<ILogger<ScaledTierCoordinator>> _mockLogger;
    private readonly Mock<IMessageBus> _mockMessageBus;
    private readonly VoiceServiceConfiguration _configuration;
    private readonly Mock<ITelemetryProvider> _mockTelemetryProvider;

    public ScaledTierCoordinatorTests()
    {
        _mockRtpEngineClient = new Mock<IRtpEngineClient>();
        _mockLogger = new Mock<ILogger<ScaledTierCoordinator>>();
        _mockMessageBus = new Mock<IMessageBus>();
        _mockTelemetryProvider = new Mock<ITelemetryProvider>();
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
            _mockRtpEngineClient.Object,
            _mockLogger.Object,
            _mockMessageBus.Object,
            _configuration,
            _mockTelemetryProvider.Object);
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

    #endregion

    #region GenerateSipCredentials Tests

    [Fact]
    public void GenerateSipCredentials_WithValidSession_ReturnsCredentials()
    {
        // Arrange
        var coordinator = CreateCoordinator();
        var sessionId = Guid.NewGuid();
        var roomId = Guid.NewGuid();

        // Act
        var credentials = coordinator.GenerateSipCredentials(sessionId, roomId);

        // Assert
        Assert.NotNull(credentials);
        // Username is "voice-" + first 8 chars of session ID string
        Assert.StartsWith("voice-", credentials.Username);
        Assert.Equal($"voice-{sessionId.ToString()[..8]}", credentials.Username);
        Assert.NotEmpty(credentials.Password);
        Assert.Equal(32, credentials.Password.Length); // SHA256 first 32 chars
        Assert.Contains("voice.bannou", credentials.ConferenceUri);
        Assert.Contains(roomId.ToString(), credentials.ConferenceUri);
        Assert.True(credentials.ExpiresAt > DateTimeOffset.UtcNow);
    }

    [Fact]
    public void GenerateSipCredentials_WithEmptyGuid_ProducesValidCredentials()
    {
        // Arrange
        var coordinator = CreateCoordinator();
        var roomId = Guid.NewGuid();

        // Act - Guid.Empty is a valid Guid, should produce deterministic credentials
        var credentials = coordinator.GenerateSipCredentials(Guid.Empty, roomId);

        // Assert
        Assert.NotNull(credentials);
        Assert.Equal("voice-00000000", credentials.Username);
        Assert.Equal(32, credentials.Password.Length);
    }

    [Fact]
    public void GenerateSipCredentials_SameInputs_ReturnsSameCredentials()
    {
        // Arrange
        var coordinator = CreateCoordinator();
        var sessionId = Guid.NewGuid();
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
        var creds1 = coordinator.GenerateSipCredentials(Guid.NewGuid(), roomId);
        var creds2 = coordinator.GenerateSipCredentials(Guid.NewGuid(), roomId);

        // Assert
        Assert.NotEqual(creds1.Password, creds2.Password);
    }

    [Fact]
    public void GenerateSipCredentials_UsernameContainsFirst8CharsOfGuid()
    {
        // Arrange
        var coordinator = CreateCoordinator();
        var sessionId = Guid.Parse("abcdef01-2345-6789-abcd-ef0123456789");
        var roomId = Guid.NewGuid();

        // Act
        var credentials = coordinator.GenerateSipCredentials(sessionId, roomId);

        // Assert - Username is "voice-" + first 8 chars of Guid string
        Assert.Equal("voice-abcdef01", credentials.Username);
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
        var sessionId = Guid.NewGuid();
        var rtpServerUri = "udp://localhost:22222";

        // Act
        var result = await coordinator.BuildScaledConnectionInfoAsync(
            roomId, sessionId, rtpServerUri, VoiceCodec.Opus, CancellationToken.None);

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
    public async Task BuildScaledConnectionInfo_WithG711Codec_ReturnsCorrectCodec()
    {
        // Arrange
        var coordinator = CreateCoordinator();
        var roomId = Guid.NewGuid();

        // Act
        var result = await coordinator.BuildScaledConnectionInfoAsync(
            roomId, Guid.NewGuid(), "udp://host:1234", VoiceCodec.G711, CancellationToken.None);

        // Assert
        Assert.Equal(VoiceCodec.G711, result.Codec);
    }

    [Fact]
    public async Task BuildScaledConnectionInfo_WithG722Codec_ReturnsCorrectCodec()
    {
        // Arrange
        var coordinator = CreateCoordinator();
        var roomId = Guid.NewGuid();

        // Act
        var result = await coordinator.BuildScaledConnectionInfoAsync(
            roomId, Guid.NewGuid(), "udp://host:1234", VoiceCodec.G722, CancellationToken.None);

        // Assert
        Assert.Equal(VoiceCodec.G722, result.Codec);
    }

    #endregion
}
