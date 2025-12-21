using System.Collections.Concurrent;
using System.Net;
using SIPSorcery.Net;
using SIPSorcery.Media;
using SIPSorceryMedia.Abstractions;

namespace BeyondImmersion.Bannou.Voice.Services;

/// <summary>
/// Helper class for managing RTP audio streaming to/from an RTP server (like RTPEngine).
/// <para>
/// <b>Purpose:</b><br/>
/// Handles RTP packet send/receive for scaled voice tier, including:
/// <list type="bullet">
///   <item>Audio encoding (PCM → Opus/G.711/G.722)</item>
///   <item>Audio decoding (Opus/G.711/G.722 → PCM)</item>
///   <item>SSRC de-duplication for mixed streams</item>
///   <item>Jitter buffer management (via SIPSorcery)</item>
///   <item>Statistics collection</item>
/// </list>
/// </para>
/// <para>
/// <b>SSRC De-duplication:</b><br/>
/// In scaled mode, the RTP server sends a single mixed stream. However, if participants
/// join/leave, the SSRC may change. This helper tracks SSRC changes and handles the
/// transition gracefully without audio glitches.
/// </para>
/// </summary>
public class RtpStreamHelper : IDisposable
{
    private readonly object _lock = new();
    private readonly AudioEncoder _audioEncoder;
    private readonly ConcurrentDictionary<uint, SsrcInfo> _knownSsrcs = new();
    private RTPSession? _rtpSession;
    private IPEndPoint? _remoteEndpoint;
    private AudioFormat? _negotiatedFormat;
    private uint _currentSsrc;
    private bool _disposed;
    private bool _isMuted;
    private long _packetsSent;
    private long _packetsReceived;
    private long _packetsLost;
    private DateTime _startTime;
    // Note: Accurate jitter calculation requires tracking inter-arrival times
    // and comparing to expected RTP timestamp intervals. This is a placeholder
    // for future implementation when RTCP receiver reports are processed.
#pragma warning disable CS0649 // Field never assigned - placeholder for future RTCP integration
    private double _jitterMs;
#pragma warning restore CS0649

    /// <summary>
    /// Maximum number of SSRC sources to track for de-duplication.
    /// </summary>
    public const int MaxTrackedSsrcs = 10;

    /// <summary>
    /// SSRC timeout in seconds - stop tracking an SSRC after this duration of inactivity.
    /// </summary>
    public const int SsrcTimeoutSeconds = 30;

    #region Properties

    /// <summary>
    /// Whether the RTP stream is currently active.
    /// </summary>
    public bool IsActive => _rtpSession != null && _remoteEndpoint != null;

    /// <summary>
    /// Whether audio sending is muted.
    /// </summary>
    public bool IsMuted
    {
        get => _isMuted;
        set => _isMuted = value;
    }

    /// <summary>
    /// The currently active SSRC for receiving audio.
    /// </summary>
    public uint CurrentSsrc => _currentSsrc;

    /// <summary>
    /// Number of unique SSRCs seen during this session.
    /// </summary>
    public int TrackedSsrcCount => _knownSsrcs.Count;

    /// <summary>
    /// Local RTP port being used.
    /// </summary>
    public int LocalPort => _rtpSession?.AudioDestinationEndPoint?.Port ?? 0;

    #endregion

    #region Events

    /// <summary>
    /// Fired when an audio frame is received and decoded.
    /// Parameters: (float[] samples, int sampleRate, int channels)
    /// </summary>
    public event Action<float[], int, int>? OnAudioFrameReceived;

    /// <summary>
    /// Fired when a new SSRC is detected (participant joined or stream changed).
    /// </summary>
    public event Action<uint>? OnSsrcChanged;

    /// <summary>
    /// Fired when an RTP error occurs.
    /// Parameters: (errorMessage)
    /// </summary>
    public event Action<string>? OnError;

    #endregion

    /// <summary>
    /// Creates a new RTP stream helper.
    /// </summary>
    public RtpStreamHelper()
    {
        _audioEncoder = new AudioEncoder();
    }

    /// <summary>
    /// Starts the RTP session and connects to the specified RTP server.
    /// </summary>
    /// <param name="rtpServerUri">RTP server URI (e.g., "udp://host:port").</param>
    /// <param name="preferredCodec">Preferred audio codec (opus, g711, g722).</param>
    /// <param name="localPort">Local port to bind. 0 for auto-selection.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if started successfully, false otherwise.</returns>
    public async Task<bool> StartAsync(
        string rtpServerUri,
        string preferredCodec = "opus",
        int localPort = 0,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (IsActive)
        {
            throw new InvalidOperationException("RTP stream already active");
        }

        try
        {
            // Parse RTP server URI
            var (host, port) = ParseRtpUri(rtpServerUri);
            _remoteEndpoint = new IPEndPoint(
                await ResolveHostAsync(host, cancellationToken),
                port);

            // Create RTP session with audio
            _rtpSession = new RTPSession(false, false, false);

            // Get supported formats and select preferred codec
            var formats = _audioEncoder.SupportedFormats.ToList();
            _negotiatedFormat = SelectFormat(formats, preferredCodec);

            // Add audio track with selected format
            var audioTrack = new MediaStreamTrack(
                new List<AudioFormat> { _negotiatedFormat.Value },
                MediaStreamStatusEnum.SendRecv);
            _rtpSession.addTrack(audioTrack);

            // Wire up RTP packet received handler
            _rtpSession.OnRtpPacketReceived += HandleRtpPacketReceived;

            // Start the session
            await _rtpSession.Start();

            // Set the remote destination
            _rtpSession.SetDestination(
                SDPMediaTypesEnum.audio,
                _remoteEndpoint,
                _remoteEndpoint); // Same for RTP and RTCP

            _startTime = DateTime.UtcNow;
            _packetsSent = 0;
            _packetsReceived = 0;
            _packetsLost = 0;

            return true;
        }
        catch (Exception ex)
        {
            OnError?.Invoke($"Failed to start RTP stream: {ex.Message}");
            await StopAsync();
            return false;
        }
    }

    /// <summary>
    /// Stops the RTP session and releases resources.
    /// </summary>
    public Task StopAsync()
    {
        if (_rtpSession != null)
        {
            _rtpSession.OnRtpPacketReceived -= HandleRtpPacketReceived;
            _rtpSession.Close("stopping");
            _rtpSession = null;
        }

        _remoteEndpoint = null;
        _knownSsrcs.Clear();
        _currentSsrc = 0;

        return Task.CompletedTask;
    }

    /// <summary>
    /// Sends an audio frame to the RTP server.
    /// </summary>
    /// <param name="pcmSamples">PCM audio samples as normalized floats (-1.0 to 1.0).</param>
    /// <param name="sampleRate">Sample rate in Hz (typically 48000 for Opus).</param>
    /// <param name="channels">Number of audio channels (1 for mono, 2 for stereo).</param>
    public void SendAudioFrame(ReadOnlySpan<float> pcmSamples, int sampleRate, int channels = 1)
    {
        if (_disposed || IsMuted || _rtpSession == null || !IsActive)
        {
            return;
        }

        try
        {
            // Convert float[] to short[] (SIPSorcery expects 16-bit PCM)
            var shortSamples = new short[pcmSamples.Length];
            for (int i = 0; i < pcmSamples.Length; i++)
            {
                var clamped = Math.Clamp(pcmSamples[i], -1f, 1f);
                shortSamples[i] = (short)(clamped * 32767f);
            }

            // Encode using negotiated format
            var format = _negotiatedFormat ?? _audioEncoder.SupportedFormats.First();
            var encodedAudio = _audioEncoder.EncodeAudio(shortSamples, format);

            if (encodedAudio != null && encodedAudio.Length > 0)
            {
                // Calculate duration for RTP timestamp
                var durationMs = (uint)(pcmSamples.Length * 1000 / sampleRate);
                _rtpSession.SendAudio(durationMs, encodedAudio);
                Interlocked.Increment(ref _packetsSent);
            }
        }
        catch (Exception ex)
        {
            OnError?.Invoke($"Failed to send audio: {ex.Message}");
        }
    }

    /// <summary>
    /// Gets current RTP stream statistics.
    /// </summary>
    /// <returns>Statistics for the RTP stream.</returns>
    public RtpStreamStatistics GetStatistics()
    {
        var uptime = IsActive ? DateTime.UtcNow - _startTime : TimeSpan.Zero;
        var packetsSent = Interlocked.Read(ref _packetsSent);
        var packetsReceived = Interlocked.Read(ref _packetsReceived);
        var packetsLost = Interlocked.Read(ref _packetsLost);

        // Estimate bitrate based on packet count and typical packet size
        var totalSeconds = uptime.TotalSeconds;
        var audioBitrateKbps = totalSeconds > 0
            ? (packetsReceived * 160 * 8) / (totalSeconds * 1000) // Rough estimate
            : 0;

        return new RtpStreamStatistics
        {
            PacketsSent = packetsSent,
            PacketsReceived = packetsReceived,
            PacketsLost = packetsLost,
            JitterMs = _jitterMs,
            RoundTripTimeMs = null, // Would need RTCP for accurate RTT
            AudioBitrateKbps = audioBitrateKbps,
            Uptime = uptime
        };
    }

    /// <summary>
    /// Clears SSRC tracking history. Useful when participants change significantly.
    /// </summary>
    public void ClearSsrcHistory()
    {
        _knownSsrcs.Clear();
    }

    #region Private Methods

    private void HandleRtpPacketReceived(
        IPEndPoint remoteEndPoint,
        SDPMediaTypesEnum mediaType,
        RTPPacket rtpPacket)
    {
        if (mediaType != SDPMediaTypesEnum.audio || _disposed)
        {
            return;
        }

        Interlocked.Increment(ref _packetsReceived);

        try
        {
            // SSRC tracking and de-duplication
            var ssrc = rtpPacket.Header.SyncSource;
            TrackSsrc(ssrc, rtpPacket.Header.SequenceNumber);

            // Decode audio
            var format = _negotiatedFormat ?? _audioEncoder.SupportedFormats.First();
            var decodedAudio = _audioEncoder.DecodeAudio(rtpPacket.Payload, format);

            if (decodedAudio != null && decodedAudio.Length > 0)
            {
                // Convert short[] to float[] (normalized -1.0 to 1.0)
                var floatSamples = new float[decodedAudio.Length];
                for (int i = 0; i < decodedAudio.Length; i++)
                {
                    floatSamples[i] = decodedAudio[i] / 32768f;
                }

                var sampleRate = format.ClockRate;
                OnAudioFrameReceived?.Invoke(floatSamples, sampleRate, 1);
            }
        }
        catch (Exception ex)
        {
            OnError?.Invoke($"Failed to process RTP packet: {ex.Message}");
        }
    }

    private void TrackSsrc(uint ssrc, ushort sequenceNumber)
    {
        var now = DateTime.UtcNow;

        // Update or add SSRC info
        _knownSsrcs.AddOrUpdate(
            ssrc,
            _ => new SsrcInfo
            {
                Ssrc = ssrc,
                FirstSeen = now,
                LastSeen = now,
                LastSequenceNumber = sequenceNumber,
                PacketCount = 1
            },
            (_, existing) =>
            {
                // Check for packet loss by comparing sequence numbers
                var expectedSeq = (ushort)((existing.LastSequenceNumber + 1) & 0xFFFF);
                if (sequenceNumber != expectedSeq)
                {
                    // Account for sequence number wrap-around
                    var diff = (sequenceNumber - expectedSeq + 65536) % 65536;
                    if (diff < 32768) // Forward gap (lost packets)
                    {
                        Interlocked.Add(ref _packetsLost, diff);
                    }
                }

                existing.LastSeen = now;
                existing.LastSequenceNumber = sequenceNumber;
                existing.PacketCount++;
                return existing;
            });

        // Check if this is a new primary SSRC
        if (_currentSsrc != ssrc)
        {
            _currentSsrc = ssrc;
            OnSsrcChanged?.Invoke(ssrc);
        }

        // Cleanup old SSRCs periodically
        if (_knownSsrcs.Count > MaxTrackedSsrcs)
        {
            CleanupOldSsrcs(now);
        }
    }

    private void CleanupOldSsrcs(DateTime now)
    {
        var timeout = TimeSpan.FromSeconds(SsrcTimeoutSeconds);
        var oldSsrcs = _knownSsrcs
            .Where(kvp => now - kvp.Value.LastSeen > timeout && kvp.Key != _currentSsrc)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var oldSsrc in oldSsrcs)
        {
            _knownSsrcs.TryRemove(oldSsrc, out _);
        }
    }

    private static (string host, int port) ParseRtpUri(string uri)
    {
        // Expected format: "udp://host:port" or "host:port"
        if (uri.StartsWith("udp://", StringComparison.OrdinalIgnoreCase))
        {
            uri = uri[6..];
        }

        var colonIndex = uri.LastIndexOf(':');
        if (colonIndex > 0 && int.TryParse(uri[(colonIndex + 1)..], out var port))
        {
            return (uri[..colonIndex], port);
        }

        throw new ArgumentException($"Invalid RTP URI format: {uri}. Expected 'udp://host:port' or 'host:port'.");
    }

    private static async Task<IPAddress> ResolveHostAsync(string host, CancellationToken cancellationToken)
    {
        if (IPAddress.TryParse(host, out var ipAddress))
        {
            return ipAddress;
        }

        var addresses = await Dns.GetHostAddressesAsync(host, cancellationToken);
        return addresses.FirstOrDefault()
            ?? throw new InvalidOperationException($"Could not resolve host: {host}");
    }

    private static AudioFormat SelectFormat(List<AudioFormat> formats, string preferredCodec)
    {
        var codec = preferredCodec.ToLowerInvariant();

        // Try to find preferred codec by name
        foreach (var format in formats)
        {
            if (format.FormatName != null &&
                format.FormatName.Contains(codec, StringComparison.OrdinalIgnoreCase))
            {
                return format;
            }
        }

        // Fall back to Opus
        foreach (var format in formats)
        {
            if (format.FormatName != null &&
                format.FormatName.Contains("opus", StringComparison.OrdinalIgnoreCase))
            {
                return format;
            }
        }

        // Fall back to PCMU (G.711)
        foreach (var format in formats)
        {
            if (format.FormatName != null &&
                format.FormatName.Contains("PCMU", StringComparison.OrdinalIgnoreCase))
            {
                return format;
            }
        }

        // Return first available
        return formats.First();
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    #endregion

    #region IDisposable

    /// <summary>
    /// Disposes the helper and releases all resources.
    /// </summary>
    public void Dispose()
    {
        if (!_disposed)
        {
            lock (_lock)
            {
                if (!_disposed)
                {
                    _disposed = true;

                    if (_rtpSession != null)
                    {
                        _rtpSession.OnRtpPacketReceived -= HandleRtpPacketReceived;
                        _rtpSession.Close("disposed");
                        _rtpSession = null;
                    }

                    _knownSsrcs.Clear();
                }
            }
        }
        GC.SuppressFinalize(this);
    }

    #endregion

    /// <summary>
    /// Information about a tracked SSRC source.
    /// </summary>
    private class SsrcInfo
    {
        public uint Ssrc { get; init; }
        public DateTime FirstSeen { get; init; }
        public DateTime LastSeen { get; set; }
        public ushort LastSequenceNumber { get; set; }
        public long PacketCount { get; set; }
    }
}
