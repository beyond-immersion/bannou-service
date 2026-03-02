using BeyondImmersion.Bannou.Voice.ClientEvents;
using BeyondImmersion.BannouService.Voice;

namespace BeyondImmersion.Bannou.Client.Voice;

/// <summary>
/// Interface for scaled voice connection management via SIP/RTP (Kamailio + RTPEngine).
/// <para>
/// <b>Architecture Overview:</b><br/>
/// In scaled mode (6+ participants), audio flows through a central RTP server instead
/// of peer-to-peer connections. The client:
/// <list type="number">
///   <item>Registers with Kamailio using SIP credentials from <see cref="VoiceTierUpgradeClientEvent"/></item>
///   <item>Sends audio to RTPEngine, which mixes all participants</item>
///   <item>Receives mixed audio from RTPEngine</item>
/// </list>
/// </para>
/// <para>
/// <b>Key Differences from P2P (<see cref="IVoicePeerConnection"/>):</b><br/>
/// - Single server connection instead of N peer connections<br/>
/// - SIP REGISTER/INVITE instead of WebRTC SDP exchange<br/>
/// - Server handles audio mixing; client receives pre-mixed stream<br/>
/// - No ICE candidates; RTP server address is known
/// </para>
/// <para>
/// <b>Default Implementation:</b><br/>
/// ScaledVoiceConnection uses SIPSorcery for both SIP signaling
/// and RTP media transport.
/// </para>
/// </summary>
public interface IScaledVoiceConnection : IDisposable
{
    #region Connection Properties

    /// <summary>
    /// The voice room ID this connection is for.
    /// </summary>
    Guid RoomId { get; }

    /// <summary>
    /// Current connection state.
    /// </summary>
    ScaledVoiceConnectionState State { get; }

    /// <summary>
    /// Gets whether audio is currently flowing (registered and RTP active).
    /// </summary>
    bool IsAudioActive { get; }

    /// <summary>
    /// SIP registration username.
    /// Returns empty string before credentials are set via <see cref="ConnectAsync"/>.
    /// </summary>
    string SipUsername { get; }

    #endregion

    #region Connection Lifecycle

    /// <summary>
    /// Connects to the voice conference using the provided credentials.
    /// <para>
    /// This method performs:
    /// <list type="number">
    ///   <item>SIP REGISTER with Kamailio using the provided credentials</item>
    ///   <item>SIP INVITE to join the conference room</item>
    ///   <item>RTP session establishment for audio</item>
    /// </list>
    /// </para>
    /// </summary>
    /// <param name="sipCredentials">SIP credentials from <see cref="VoiceTierUpgradeClientEvent"/>.</param>
    /// <param name="rtpServerUri">RTP server URI from <see cref="VoiceTierUpgradeClientEvent"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if connection succeeded, false otherwise.</returns>
    Task<bool> ConnectAsync(
        SipConnectionCredentials sipCredentials,
        string rtpServerUri,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gracefully disconnects from the voice conference.
    /// <para>
    /// Sends SIP BYE to leave the conference and de-registers from Kamailio.
    /// </para>
    /// </summary>
    Task DisconnectAsync();

    /// <summary>
    /// Refreshes the SIP registration before credentials expire.
    /// <para>
    /// Call this before <see cref="SipConnectionCredentials.ExpiresAt"/> to maintain
    /// the connection without interruption.
    /// </para>
    /// </summary>
    /// <param name="newCredentials">New credentials if password changed, or null to re-use existing.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task RefreshRegistrationAsync(
        SipConnectionCredentials? newCredentials = null,
        CancellationToken cancellationToken = default);

    #endregion

    #region Events

    /// <summary>
    /// Fired when the connection state changes.
    /// </summary>
    event Action<ScaledVoiceConnectionState>? OnStateChanged;

    /// <summary>
    /// Fired when an error occurs during connection or streaming.
    /// Parameters: (errorCode, errorMessage)
    /// </summary>
    event Action<ScaledVoiceErrorCode, string>? OnError;

    /// <summary>
    /// Fired when a connection attempt succeeds.
    /// </summary>
    event Action? OnConnected;

    /// <summary>
    /// Fired when the connection is lost (server disconnect, network failure, etc.).
    /// Parameters: (reason)
    /// </summary>
    event Action<string?>? OnDisconnected;

    /// <summary>
    /// Fired when SIP registration is about to expire.
    /// <para>
    /// Subscribe to this event to call <see cref="RefreshRegistrationAsync"/> with new credentials.
    /// Parameter is seconds until expiration.
    /// </para>
    /// </summary>
    event Action<int>? OnRegistrationExpiring;

    #endregion

    #region Audio

    /// <summary>
    /// Sends an audio frame to the RTP server.
    /// <para>
    /// The server mixes this with other participants and distributes the mixed audio.
    /// Use the same format as <see cref="IVoicePeerConnection.SendAudioFrame"/> for consistency.
    /// </para>
    /// </summary>
    /// <param name="pcmSamples">PCM audio samples as normalized floats (-1.0 to 1.0).</param>
    /// <param name="sampleRate">Sample rate in Hz (typically 48000 for Opus).</param>
    /// <param name="channels">Number of audio channels (1 for mono, 2 for stereo).</param>
    void SendAudioFrame(ReadOnlySpan<float> pcmSamples, int sampleRate, int channels = 1);

    /// <summary>
    /// Fired when a mixed audio frame is received from the RTP server.
    /// <para>
    /// Unlike P2P mode, this is a SINGLE mixed stream containing all other participants.
    /// The server has already combined everyone's audio.
    /// </para>
    /// </summary>
    /// <remarks>
    /// Parameters: (float[] samples, int sampleRate, int channels)
    /// </remarks>
    event Action<float[], int, int>? OnAudioFrameReceived;

    /// <summary>
    /// Gets or sets whether local audio is muted (not sent to server).
    /// </summary>
    bool IsMuted { get; set; }

    #endregion

    #region Statistics

    /// <summary>
    /// Gets current RTP statistics for monitoring connection quality.
    /// </summary>
    RtpStreamStatistics GetStatistics();

    #endregion
}

/// <summary>
/// SIP connection credentials for scaled voice tier.
/// <para>
/// These credentials are provided by the server in the tier upgrade event
/// and are dynamically generated per-user per-room.
/// </para>
/// </summary>
public sealed class SipConnectionCredentials
{
    /// <summary>
    /// SIP username for authentication.
    /// </summary>
    public required string Username { get; init; }

    /// <summary>
    /// SIP password for authentication.
    /// </summary>
    public required string Password { get; init; }

    /// <summary>
    /// SIP domain/realm (e.g., "voice.bannou.local").
    /// </summary>
    public required string Domain { get; init; }

    /// <summary>
    /// Conference URI to dial (e.g., "sip:room-{guid}@voice.bannou.local").
    /// </summary>
    public required string ConferenceUri { get; init; }

    /// <summary>
    /// When these credentials expire. Null if no expiration.
    /// </summary>
    public DateTimeOffset? ExpiresAt { get; init; }

    /// <summary>
    /// Creates credentials from the generated SipCredentials event model.
    /// </summary>
    /// <param name="credentials">SipCredentials from <see cref="VoiceTierUpgradeClientEvent"/>.</param>
    /// <param name="conferenceUri">Conference URI from room state.</param>
    public static SipConnectionCredentials FromEvent(
        SipCredentials credentials,
        string conferenceUri)
    {
        return new SipConnectionCredentials
        {
            Username = credentials.Username,
            Password = credentials.Password,
            Domain = credentials.Domain,
            ConferenceUri = conferenceUri,
            ExpiresAt = credentials.ExpiresAt
        };
    }
}

/// <summary>
/// Connection state for a scaled voice connection.
/// </summary>
public enum ScaledVoiceConnectionState
{
    /// <summary>
    /// Initial state, not connected.
    /// </summary>
    Disconnected,

    /// <summary>
    /// Registering with the SIP server.
    /// </summary>
    Registering,

    /// <summary>
    /// Registered with SIP, dialing into conference.
    /// </summary>
    Dialing,

    /// <summary>
    /// Connected to conference, audio active.
    /// </summary>
    Connected,

    /// <summary>
    /// Connection lost, attempting to reconnect.
    /// </summary>
    Reconnecting,

    /// <summary>
    /// Connection failed with an error.
    /// </summary>
    Failed
}

/// <summary>
/// Error codes for scaled voice connection failures.
/// </summary>
public enum ScaledVoiceErrorCode
{
    /// <summary>
    /// Unknown error.
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// SIP registration failed (invalid credentials).
    /// </summary>
    RegistrationFailed = 100,

    /// <summary>
    /// SIP registration timed out.
    /// </summary>
    RegistrationTimeout = 101,

    /// <summary>
    /// Conference dial failed (room not found, full, etc.).
    /// </summary>
    DialFailed = 200,

    /// <summary>
    /// RTP session setup failed.
    /// </summary>
    RtpSetupFailed = 300,

    /// <summary>
    /// Network connectivity lost.
    /// </summary>
    NetworkError = 400,

    /// <summary>
    /// Server disconnected the call.
    /// </summary>
    ServerDisconnect = 500,

    /// <summary>
    /// Credentials expired and refresh failed.
    /// </summary>
    CredentialsExpired = 600
}

/// <summary>
/// Statistics for an RTP stream.
/// </summary>
public sealed class RtpStreamStatistics
{
    /// <summary>
    /// Total RTP packets sent.
    /// </summary>
    public long PacketsSent { get; init; }

    /// <summary>
    /// Total RTP packets received.
    /// </summary>
    public long PacketsReceived { get; init; }

    /// <summary>
    /// Packets lost (detected via sequence number gaps).
    /// </summary>
    public long PacketsLost { get; init; }

    /// <summary>
    /// Current jitter in milliseconds.
    /// </summary>
    public double JitterMs { get; init; }

    /// <summary>
    /// Round-trip time in milliseconds (if RTCP enabled).
    /// </summary>
    public double? RoundTripTimeMs { get; init; }

    /// <summary>
    /// Audio bitrate in kbps.
    /// </summary>
    public double AudioBitrateKbps { get; init; }

    /// <summary>
    /// Connection uptime.
    /// </summary>
    public TimeSpan Uptime { get; init; }

    /// <summary>
    /// Empty statistics for when no connection exists.
    /// </summary>
    public static readonly RtpStreamStatistics Empty = new()
    {
        PacketsSent = 0,
        PacketsReceived = 0,
        PacketsLost = 0,
        JitterMs = 0,
        RoundTripTimeMs = null,
        AudioBitrateKbps = 0,
        Uptime = TimeSpan.Zero
    };
}
