namespace BeyondImmersion.Bannou.Client.Voice;

/// <summary>
/// Interface for single-peer WebRTC video connection.
/// <para>
/// <b>Important Limitations:</b><br/>
/// - Video is intentionally NOT supported in <see cref="VoiceRoomManager"/> multi-peer scenarios<br/>
/// - P2P video bandwidth scales quadratically with participants (N*(N-1)/2 connections)<br/>
/// - For 3+ participants, use server-side media mixing (Kamailio+RTPEngine)<br/>
/// - This interface is for 1:1 video calls only
/// </para>
/// <para>
/// <b>Stride Integration:</b><br/>
/// Video frames are provided as raw RGBA byte arrays. Use Stride's <c>Texture</c>
/// API to display received video and camera capture for sending.
/// </para>
/// </summary>
/// <remarks>
/// The default SIPSorcery implementation is a stub that throws <see cref="NotImplementedException"/>.
/// Full video support will be added when needed for 1:1 video scenarios.
/// </remarks>
public interface IVideoPeerConnection : IVoicePeerConnection
{
    #region Video

    /// <summary>
    /// Sends a video frame to the remote peer.
    /// </summary>
    /// <param name="rgbaPixels">Raw RGBA pixel data (width * height * 4 bytes).</param>
    /// <param name="width">Frame width in pixels.</param>
    /// <param name="height">Frame height in pixels.</param>
    /// <param name="timestampMs">Timestamp in milliseconds (for frame ordering).</param>
    void SendVideoFrame(ReadOnlySpan<byte> rgbaPixels, int width, int height, long timestampMs);

    /// <summary>
    /// Fired when a video frame is received from the remote peer.
    /// </summary>
    /// <remarks>
    /// Parameters: (byte[] rgbaPixels, int width, int height, long timestampMs)
    /// </remarks>
    event Action<byte[], int, int, long>? OnVideoFrameReceived;

    /// <summary>
    /// Gets or sets whether video sending is enabled.
    /// </summary>
    bool IsVideoEnabled { get; set; }

    /// <summary>
    /// Gets the video codec being used.
    /// </summary>
    string VideoCodec { get; }

    #endregion
}
