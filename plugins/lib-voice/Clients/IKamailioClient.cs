namespace BeyondImmersion.BannouService.Voice.Clients;

/// <summary>
/// Client interface for Kamailio SIP proxy health checking.
/// Enables Bannou VoiceService to verify Kamailio availability for scaled tier operations.
/// </summary>
public interface IKamailioClient
{
    /// <summary>
    /// Health check for Kamailio JSONRPC endpoint.
    /// </summary>
    Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default);
}
