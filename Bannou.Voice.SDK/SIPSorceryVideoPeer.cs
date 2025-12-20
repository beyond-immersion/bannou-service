namespace BeyondImmersion.Bannou.Voice;

/// <summary>
/// WebRTC video peer connection stub using SIPSorcery.
/// <para>
/// <b>IMPORTANT:</b> This is a stub implementation. Video support is NOT YET IMPLEMENTED.
/// All video methods throw <see cref="NotImplementedException"/>.
/// </para>
/// <para>
/// <b>Limitations:</b><br/>
/// - Video is for 1:1 peer connections ONLY<br/>
/// - P2P video does NOT scale to multiple participants (bandwidth = N*(N-1)/2)<br/>
/// - For 3+ participants, use server-side media mixing (not P2P)<br/>
/// - This class is NOT used by <see cref="VoiceRoomManager"/>
/// </para>
/// <para>
/// <b>Future Implementation Notes:</b><br/>
/// When implementing video support, consider:<br/>
/// - VP8/VP9/H.264 codec selection based on peer capabilities<br/>
/// - Resolution/framerate negotiation via SDP<br/>
/// - Bandwidth estimation and adaptive bitrate<br/>
/// - Frame rate limiting based on network conditions
/// </para>
/// </summary>
/// <remarks>
/// SIPSorcery v8.0.14 supports video via the <c>RTCPeerConnection</c> class.
/// Video tracks can be added similarly to audio tracks, but require:
/// <list type="bullet">
/// <item>Video encoder (VP8/H.264) - see SIPSorceryMedia.FFmpeg or platform-specific encoders</item>
/// <item>Video decoder for received frames</item>
/// <item>Frame format conversion (RGBA to/from encoder format)</item>
/// </list>
/// </remarks>
public sealed class SIPSorceryVideoPeer : SIPSorceryVoicePeer, IVideoPeerConnection
{
    private bool _isVideoEnabled;

    /// <summary>
    /// Creates a new SIPSorcery-based video peer connection stub.
    /// <para>
    /// <b>Note:</b> Video functionality is not yet implemented.
    /// Use this class only if you plan to implement video support yourself
    /// or are waiting for a future SDK update.
    /// </para>
    /// </summary>
    /// <param name="peerId">The remote peer's account ID.</param>
    /// <param name="displayName">The remote peer's display name (for UI).</param>
    /// <param name="stunServers">Optional STUN server URIs for NAT traversal.</param>
    public SIPSorceryVideoPeer(
        Guid peerId,
        string? displayName = null,
        IEnumerable<string>? stunServers = null)
        : base(peerId, displayName, stunServers)
    {
    }

    /// <inheritdoc/>
    /// <exception cref="NotImplementedException">Always thrown - video not yet implemented.</exception>
    public void SendVideoFrame(ReadOnlySpan<byte> rgbaPixels, int width, int height, long timestampMs)
    {
        throw new NotImplementedException(
            "Video support is not yet implemented in Bannou.Voice.SDK. " +
            "For 1:1 video calls, you can implement video encoding/decoding using " +
            "SIPSorceryMedia.FFmpeg or a platform-specific video encoder. " +
            "See the SIPSorcery documentation for video track examples.");
    }

    /// <inheritdoc/>
    public event Action<byte[], int, int, long>? OnVideoFrameReceived;

    /// <inheritdoc/>
    public bool IsVideoEnabled
    {
        get => _isVideoEnabled;
        set
        {
            if (value)
            {
                throw new NotImplementedException(
                    "Video support is not yet implemented. " +
                    "Setting IsVideoEnabled to true is not supported.");
            }
            _isVideoEnabled = value;
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Returns "none" since video is not implemented.
    /// When implemented, this would return the negotiated codec (e.g., "VP8", "H264").
    /// </remarks>
    public string VideoCodec => "none";

    /// <summary>
    /// Raises the <see cref="OnVideoFrameReceived"/> event.
    /// <para>
    /// This method is internal for potential future extensions.
    /// </para>
    /// </summary>
    /// <param name="rgbaPixels">Decoded RGBA pixel data.</param>
    /// <param name="width">Frame width in pixels.</param>
    /// <param name="height">Frame height in pixels.</param>
    /// <param name="timestampMs">Frame timestamp in milliseconds.</param>
    internal void RaiseVideoFrameReceived(byte[] rgbaPixels, int width, int height, long timestampMs)
    {
        OnVideoFrameReceived?.Invoke(rgbaPixels, width, height, timestampMs);
    }
}
