using SIPSorcery.Media;
using SIPSorcery.Net;
using SIPSorceryMedia.Abstractions;

namespace BeyondImmersion.Bannou.Client.Voice;

/// <summary>
/// WebRTC peer connection implementation using SIPSorcery.
/// <para>
/// <b>License:</b> SIPSorcery v8.0.14 is BSD-3-Clause (pre-May 2025 license change).<br/>
/// This version is pinned to maintain permissive licensing for all users.
/// </para>
/// <para>
/// <b>Stride/Unity Integration:</b><br/>
/// - Subscribe to <see cref="OnAudioFrameReceived"/> to play received audio<br/>
/// - Call <see cref="SendAudioFrame"/> with microphone PCM data<br/>
/// - The implementation handles Opus encoding/decoding automatically
/// </para>
/// </summary>
public class SIPSorceryVoicePeer : IVoicePeerConnection
{
    private readonly RTCPeerConnection _peerConnection;
    private readonly AudioEncoder _audioEncoder;
    private readonly object _lock = new();
    private bool _disposed;
    private VoicePeerConnectionState _state = VoicePeerConnectionState.New;
    private AudioFormat? _negotiatedFormat;

    /// <inheritdoc/>
    public Guid PeerId { get; }

    /// <inheritdoc/>
    public string? DisplayName { get; }

    /// <inheritdoc/>
    public VoicePeerConnectionState State
    {
        get => _state;
        private set
        {
            if (_state != value)
            {
                _state = value;
                OnStateChanged?.Invoke(value);
            }
        }
    }

    /// <inheritdoc/>
    public bool IsMuted { get; set; }

    /// <inheritdoc/>
    public event Action<string>? OnIceCandidateGathered;

    /// <inheritdoc/>
    public event Action? OnConnected;

    /// <inheritdoc/>
    public event Action<string?>? OnDisconnected;

    /// <inheritdoc/>
    public event Action<VoicePeerConnectionState>? OnStateChanged;

    /// <inheritdoc/>
    public event Action<float[], int, int>? OnAudioFrameReceived;

    /// <summary>
    /// Creates a new SIPSorcery-based voice peer connection.
    /// </summary>
    /// <param name="peerId">The remote peer's account ID.</param>
    /// <param name="displayName">The remote peer's display name (for UI).</param>
    /// <param name="stunServers">Optional STUN server URIs for NAT traversal. Defaults to Google's public STUN server.</param>
    public SIPSorceryVoicePeer(
        Guid peerId,
        string? displayName = null,
        IEnumerable<string>? stunServers = null)
    {
        PeerId = peerId;
        DisplayName = displayName;

        // Configure ICE servers
        var config = new RTCConfiguration();
        var iceServers = (stunServers ?? new[] { "stun:stun.l.google.com:19302" })
            .Select(uri => new RTCIceServer { urls = uri })
            .ToList();
        config.iceServers = iceServers;

        // Create peer connection
        _peerConnection = new RTCPeerConnection(config);

        // Audio encoder for Opus (preferred) with G.711 fallback
        _audioEncoder = new AudioEncoder();

        // Set up audio track for send/receive
        var audioFormats = _audioEncoder.SupportedFormats;
        var audioTrack = new MediaStreamTrack(audioFormats, MediaStreamStatusEnum.SendRecv);
        _peerConnection.addTrack(audioTrack);

        // Wire up events
        _peerConnection.onicecandidate += (candidate) =>
        {
            if (candidate != null)
            {
                // Format: "candidate:xxx" or full SDP candidate line
                OnIceCandidateGathered?.Invoke(candidate.candidate);
            }
        };

        _peerConnection.onconnectionstatechange += (connectionState) =>
        {
            State = connectionState switch
            {
                RTCPeerConnectionState.connecting => VoicePeerConnectionState.Connecting,
                RTCPeerConnectionState.connected => VoicePeerConnectionState.Connected,
                RTCPeerConnectionState.disconnected => VoicePeerConnectionState.Disconnected,
                RTCPeerConnectionState.failed => VoicePeerConnectionState.Failed,
                RTCPeerConnectionState.closed => VoicePeerConnectionState.Disconnected,
                _ => VoicePeerConnectionState.New
            };

            if (connectionState == RTCPeerConnectionState.connected)
            {
                OnConnected?.Invoke();
            }
            else if (connectionState == RTCPeerConnectionState.disconnected ||
                    connectionState == RTCPeerConnectionState.failed ||
                    connectionState == RTCPeerConnectionState.closed)
            {
                OnDisconnected?.Invoke(connectionState.ToString());
            }
        };

        // Handle negotiated audio format
        _peerConnection.OnAudioFormatsNegotiated += (formats) =>
        {
            if (formats.Any())
            {
                _negotiatedFormat = formats.First();
            }
        };

        // Handle incoming audio RTP packets
        _peerConnection.OnRtpPacketReceived += (ipEndpoint, mediaType, rtpPacket) =>
        {
            if (mediaType == SDPMediaTypesEnum.audio)
            {
                try
                {
                    // Use negotiated format or fall back to first supported format
                    var format = _negotiatedFormat ?? _audioEncoder.SupportedFormats.First();

                    // Decode RTP audio to PCM
                    var decodedAudio = _audioEncoder.DecodeAudio(rtpPacket.Payload, format);

                    if (decodedAudio != null && decodedAudio.Length > 0)
                    {
                        // Convert short[] to float[] (normalized -1.0 to 1.0)
                        var floatSamples = new float[decodedAudio.Length];
                        for (int i = 0; i < decodedAudio.Length; i++)
                        {
                            floatSamples[i] = decodedAudio[i] / 32768f;
                        }

                        // Use the format's clock rate as sample rate
                        var sampleRate = format.ClockRate;
                        OnAudioFrameReceived?.Invoke(floatSamples, sampleRate, 1);
                    }
                }
                catch
                {
                    // Ignore decode errors for individual packets
                }
            }
        };
    }

    /// <inheritdoc/>
    public async Task<string> CreateOfferAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        State = VoicePeerConnectionState.Connecting;

        var offer = _peerConnection.createOffer();
        await _peerConnection.setLocalDescription(offer);

        return offer.sdp;
    }

    /// <inheritdoc/>
    public Task SetRemoteDescriptionAsync(string sdp, bool isOffer, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        var description = new RTCSessionDescriptionInit
        {
            sdp = sdp,
            type = isOffer ? RTCSdpType.offer : RTCSdpType.answer
        };

        _peerConnection.setRemoteDescription(description);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async Task<string> CreateAnswerAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        State = VoicePeerConnectionState.Connecting;

        var answer = _peerConnection.createAnswer();
        await _peerConnection.setLocalDescription(answer);

        return answer.sdp;
    }

    /// <inheritdoc/>
    public void AddIceCandidate(string candidate)
    {
        ThrowIfDisposed();

        // Parse ICE candidate - SIPSorcery expects the full candidate line
        var iceCandidate = new RTCIceCandidateInit { candidate = candidate };
        _peerConnection.addIceCandidate(iceCandidate);
    }

    /// <inheritdoc/>
    public void SendAudioFrame(ReadOnlySpan<float> pcmSamples, int sampleRate, int channels = 1)
    {
        if (_disposed || IsMuted || State != VoicePeerConnectionState.Connected)
        {
            return;
        }

        try
        {
            // Convert float[] to short[] (SIPSorcery expects 16-bit PCM)
            var shortSamples = new short[pcmSamples.Length];
            for (int i = 0; i < pcmSamples.Length; i++)
            {
                // Clamp to valid range and convert
                var clamped = Math.Clamp(pcmSamples[i], -1f, 1f);
                shortSamples[i] = (short)(clamped * 32767f);
            }

            // Encode and send using negotiated format
            var format = _negotiatedFormat ?? _audioEncoder.SupportedFormats.First();
            var encodedAudio = _audioEncoder.EncodeAudio(shortSamples, format);

            if (encodedAudio != null && encodedAudio.Length > 0)
            {
                // Calculate duration based on sample count
                // Opus typically uses 20ms frames at 48kHz = 960 samples
                var durationMs = (uint)(pcmSamples.Length * 1000 / sampleRate);
                _peerConnection.SendAudio(durationMs, encodedAudio);
            }
        }
        catch
        {
            // Ignore encoding errors for individual frames
        }
    }

    /// <inheritdoc/>
    public Task CloseAsync()
    {
        if (!_disposed)
        {
            _peerConnection.close();
            State = VoicePeerConnectionState.Disconnected;
        }
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (!_disposed)
        {
            lock (_lock)
            {
                if (!_disposed)
                {
                    _disposed = true;
                    _peerConnection.Dispose();
                }
            }
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(SIPSorceryVoicePeer));
        }
    }
}
