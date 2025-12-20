namespace BeyondImmersion.Bannou.Voice;

/// <summary>
/// Interface for WebRTC peer connection management for P2P voice chat.
/// <para>
/// The default implementation uses SIPSorcery (v8.0.14, BSD-3-Clause license).
/// This interface allows alternative implementations for engines with native WebRTC
/// support (e.g., Unity WebRTC package).
/// </para>
/// <para>
/// <b>Game Engine Integration (Stride, Unity):</b><br/>
/// - Subscribe to <see cref="OnAudioFrameReceived"/> to play received audio<br/>
/// - Call <see cref="SendAudioFrame"/> with microphone capture data<br/>
/// - The SDK handles all WebRTC negotiation (SDP/ICE) automatically
/// </para>
/// </summary>
public interface IVoicePeerConnection : IDisposable
{
    #region Connection Lifecycle

    /// <summary>
    /// Unique identifier for this peer (their account ID).
    /// </summary>
    Guid PeerId { get; }

    /// <summary>
    /// Display name of the peer (for UI purposes).
    /// </summary>
    string? DisplayName { get; }

    /// <summary>
    /// Current connection state.
    /// </summary>
    VoicePeerConnectionState State { get; }

    /// <summary>
    /// Creates an SDP offer for initiating a connection.
    /// Call this when you're the initiator (connecting to an existing peer).
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>SDP offer string to send to the remote peer.</returns>
    Task<string> CreateOfferAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets the remote peer's SDP description (offer or answer).
    /// </summary>
    /// <param name="sdp">The SDP string from the remote peer.</param>
    /// <param name="isOffer">True if this is an offer (you need to create an answer), false if it's an answer.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SetRemoteDescriptionAsync(string sdp, bool isOffer, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates an SDP answer in response to an offer.
    /// Call <see cref="SetRemoteDescriptionAsync"/> with the offer first.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>SDP answer string to send to the remote peer.</returns>
    Task<string> CreateAnswerAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds an ICE candidate received from the remote peer.
    /// </summary>
    /// <param name="candidate">ICE candidate string.</param>
    void AddIceCandidate(string candidate);

    /// <summary>
    /// Closes the peer connection gracefully.
    /// </summary>
    Task CloseAsync();

    #endregion

    #region Events

    /// <summary>
    /// Fired when a local ICE candidate is gathered.
    /// Send this candidate to the remote peer via signaling (Bannou WebSocket).
    /// </summary>
    event Action<string>? OnIceCandidateGathered;

    /// <summary>
    /// Fired when the peer connection successfully connects.
    /// </summary>
    event Action? OnConnected;

    /// <summary>
    /// Fired when the peer connection disconnects or fails.
    /// </summary>
    event Action<string?>? OnDisconnected;

    /// <summary>
    /// Fired when the connection state changes.
    /// </summary>
    event Action<VoicePeerConnectionState>? OnStateChanged;

    #endregion

    #region Audio

    /// <summary>
    /// Sends an audio frame to the remote peer.
    /// <para>
    /// <b>Stride Integration:</b><br/>
    /// Capture microphone audio using Stride's <c>Microphone</c> class,
    /// convert to float[] PCM samples, and call this method.
    /// </para>
    /// </summary>
    /// <param name="pcmSamples">PCM audio samples as normalized floats (-1.0 to 1.0).</param>
    /// <param name="sampleRate">Sample rate in Hz (typically 48000 for Opus).</param>
    /// <param name="channels">Number of audio channels (1 for mono, 2 for stereo).</param>
    void SendAudioFrame(ReadOnlySpan<float> pcmSamples, int sampleRate, int channels = 1);

    /// <summary>
    /// Fired when an audio frame is received from the remote peer.
    /// <para>
    /// <b>Stride Integration:</b><br/>
    /// Convert the float[] samples to Stride's audio format and play via
    /// <c>DynamicSoundSource</c> or similar audio API.
    /// </para>
    /// </summary>
    /// <remarks>
    /// Parameters: (float[] samples, int sampleRate, int channels)
    /// </remarks>
    event Action<float[], int, int>? OnAudioFrameReceived;

    /// <summary>
    /// Gets or sets whether the local audio is muted (not sending).
    /// </summary>
    bool IsMuted { get; set; }

    #endregion
}

/// <summary>
/// Connection state for a voice peer connection.
/// </summary>
public enum VoicePeerConnectionState
{
    /// <summary>
    /// Initial state, not yet started.
    /// </summary>
    New,

    /// <summary>
    /// Gathering ICE candidates and negotiating.
    /// </summary>
    Connecting,

    /// <summary>
    /// Successfully connected, audio flowing.
    /// </summary>
    Connected,

    /// <summary>
    /// Connection temporarily interrupted, attempting recovery.
    /// </summary>
    Reconnecting,

    /// <summary>
    /// Connection closed or failed.
    /// </summary>
    Disconnected,

    /// <summary>
    /// Connection failed with an error.
    /// </summary>
    Failed
}
