namespace BeyondImmersion.BannouService.Voice.Clients;

/// <summary>
/// Client interface for RTPEngine ng protocol control.
/// Enables Bannou VoiceService to manage media streams for SFU conferencing.
/// </summary>
public interface IRtpEngineClient : IDisposable
{
    /// <summary>
    /// Sends SDP offer to RTPEngine and receives modified SDP.
    /// Used when a participant joins and sends their initial offer.
    /// </summary>
    Task<RtpEngineOfferResponse> OfferAsync(
        string callId,
        string fromTag,
        string sdp,
        string[]? flags = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends SDP answer to RTPEngine.
    /// Used after the callee responds to an offer.
    /// </summary>
    Task<RtpEngineAnswerResponse> AnswerAsync(
        string callId,
        string fromTag,
        string toTag,
        string sdp,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a media session from RTPEngine.
    /// Used when a participant leaves.
    /// </summary>
    Task<RtpEngineDeleteResponse> DeleteAsync(
        string callId,
        string fromTag,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Publishes a stream for SFU mode (conference participant).
    /// The publisher sends media to RTPEngine, which forwards to subscribers.
    /// </summary>
    /// <remarks>
    /// <para><b>Note</b>: This method is fully implemented but not yet called by VoiceService.</para>
    /// <para>
    /// Future use: When implementing SFU conferencing where RTPEngine acts as a media hub,
    /// publishers call this method to send media that RTPEngine will forward to all subscribers.
    /// This enables server-side mixing/forwarding for large rooms where P2P mesh becomes impractical.
    /// </para>
    /// </remarks>
    Task<RtpEnginePublishResponse> PublishAsync(
        string callId,
        string fromTag,
        string sdp,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Subscribes to published streams (SFU mode).
    /// The subscriber receives media from specified publishers.
    /// </summary>
    /// <remarks>
    /// <para><b>Note</b>: This method is fully implemented but not yet called by VoiceService.</para>
    /// <para>
    /// Future use: When implementing SFU conferencing, subscribers call this method to receive
    /// media streams from one or more publishers. The subscriberLabel identifies this subscriber
    /// for routing purposes. This enables selective forwarding where clients only receive
    /// streams they're interested in (e.g., active speakers in large rooms).
    /// </para>
    /// </remarks>
    Task<RtpEngineSubscribeResponse> SubscribeRequestAsync(
        string callId,
        string[] fromTags,
        string subscriberLabel,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Queries information about a call/session.
    /// </summary>
    Task<RtpEngineQueryResponse> QueryAsync(
        string callId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Health check for RTPEngine ng protocol endpoint.
    /// </summary>
    Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Base response from RTPEngine ng protocol.
/// </summary>
public abstract class RtpEngineBaseResponse
{
    /// <summary>
    /// Result status: "ok" for success, "error" for failure.
    /// </summary>
    public string Result { get; set; } = string.Empty;

    /// <summary>
    /// Error reason if Result is "error".
    /// </summary>
    public string? ErrorReason { get; set; }

    /// <summary>
    /// Warning message if any.
    /// </summary>
    public string? Warning { get; set; }

    /// <summary>
    /// Returns true if the operation was successful.
    /// </summary>
    public bool IsSuccess => Result?.Equals("ok", StringComparison.OrdinalIgnoreCase) == true;
}

/// <summary>
/// Response from RTPEngine offer command.
/// </summary>
public class RtpEngineOfferResponse : RtpEngineBaseResponse
{
    /// <summary>
    /// Modified SDP with RTPEngine media addresses.
    /// </summary>
    public string Sdp { get; set; } = string.Empty;
}

/// <summary>
/// Response from RTPEngine answer command.
/// </summary>
public class RtpEngineAnswerResponse : RtpEngineBaseResponse
{
    /// <summary>
    /// Modified SDP with RTPEngine media addresses.
    /// </summary>
    public string Sdp { get; set; } = string.Empty;
}

/// <summary>
/// Response from RTPEngine delete command.
/// </summary>
public class RtpEngineDeleteResponse : RtpEngineBaseResponse
{
    /// <summary>
    /// Unix timestamp when the session was created.
    /// </summary>
    public long Created { get; set; }

    /// <summary>
    /// Unix timestamp of the last signal.
    /// </summary>
    public long LastSignal { get; set; }

    /// <summary>
    /// Statistics totals for the deleted session.
    /// </summary>
    public Dictionary<string, object>? Totals { get; set; }
}

/// <summary>
/// Response from RTPEngine publish command (SFU mode).
/// </summary>
public class RtpEnginePublishResponse : RtpEngineBaseResponse
{
    /// <summary>
    /// RTPEngine-generated recvonly SDP for the publisher.
    /// </summary>
    public string Sdp { get; set; } = string.Empty;
}

/// <summary>
/// Response from RTPEngine subscribe request command (SFU mode).
/// </summary>
public class RtpEngineSubscribeResponse : RtpEngineBaseResponse
{
    /// <summary>
    /// Sendonly SDP for receiving published streams.
    /// </summary>
    public string Sdp { get; set; } = string.Empty;
}

/// <summary>
/// Response from RTPEngine query command.
/// </summary>
public class RtpEngineQueryResponse : RtpEngineBaseResponse
{
    /// <summary>
    /// Stream information for the queried call.
    /// </summary>
    public Dictionary<string, object>? Streams { get; set; }

    /// <summary>
    /// Total number of active streams.
    /// </summary>
    public int StreamCount => Streams?.Count ?? 0;
}
