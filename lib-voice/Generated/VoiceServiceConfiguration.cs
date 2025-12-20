using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Configuration;
using System.ComponentModel.DataAnnotations;

namespace BeyondImmersion.BannouService.Voice;

/// <summary>
/// Configuration class for Voice service.
/// Properties are automatically bound from environment variables.
/// </summary>
[ServiceConfiguration(typeof(VoiceService), envPrefix: "BANNOU_")]
public class VoiceServiceConfiguration : IServiceConfiguration
{
    /// <inheritdoc />
    public string? Force_Service_ID { get; set; }

    /// <summary>
    /// Whether P2P mode is enabled for voice rooms
    /// Environment variable: P2PENABLED or BANNOU_P2PENABLED
    /// </summary>
    public bool P2PEnabled { get; set; } = true;

    /// <summary>
    /// Maximum number of participants in P2P mode before upgrade to scaled tier
    /// Environment variable: P2PMAXPARTICIPANTS or BANNOU_P2PMAXPARTICIPANTS
    /// </summary>
    public int P2PMaxParticipants { get; set; } = 6;

    /// <summary>
    /// Whether scaled tier (RTP server) is available. Set to false in Phase 1
    /// Environment variable: SCALEDTIERENABLED or BANNOU_SCALEDTIERENABLED
    /// </summary>
    public bool ScaledTierEnabled { get; set; } = false;

    /// <summary>
    /// URI of the RTP server for scaled tier mode
    /// Environment variable: RTPSERVERURI or BANNOU_RTPSERVERURI
    /// </summary>
    public string RtpServerUri { get; set; } = "";

    /// <summary>
    /// TTL for endpoint registrations before they expire
    /// Environment variable: ENDPOINTTTLSECONDS or BANNOU_ENDPOINTTTLSECONDS
    /// </summary>
    public int EndpointTtlSeconds { get; set; } = 30;

    /// <summary>
    /// Default audio codec for voice communication
    /// Environment variable: DEFAULTCODEC or BANNOU_DEFAULTCODEC
    /// </summary>
    public string DefaultCodec { get; set; } = "opus";

    /// <summary>
    /// Comma-separated list of STUN server URIs for NAT traversal
    /// Environment variable: STUNSERVERS or BANNOU_STUNSERVERS
    /// </summary>
    public string StunServers { get; set; } = "stun:stun.l.google.com:19302";

}
