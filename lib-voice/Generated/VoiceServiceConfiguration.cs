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
    /// Whether scaled tier (Kamailio/RTPEngine) is available
    /// Environment variable: SCALEDTIERENABLED or BANNOU_SCALEDTIERENABLED
    /// </summary>
    public bool ScaledTierEnabled { get; set; } = false;

    /// <summary>
    /// Maximum number of participants in scaled tier mode
    /// Environment variable: SCALEDMAXPARTICIPANTS or BANNOU_SCALEDMAXPARTICIPANTS
    /// </summary>
    public int ScaledMaxParticipants { get; set; } = 100;

    /// <summary>
    /// Whether automatic tier upgrade from P2P to Scaled is enabled
    /// Environment variable: TIERUPGRADEENABLED or BANNOU_TIERUPGRADEENABLED
    /// </summary>
    public bool TierUpgradeEnabled { get; set; } = false;

    /// <summary>
    /// Time in milliseconds to wait for clients to migrate during tier upgrade
    /// Environment variable: TIERUPGRADEMIGRATIONDEADLINEMS or BANNOU_TIERUPGRADEMIGRATIONDEADLINEMS
    /// </summary>
    public int TierUpgradeMigrationDeadlineMs { get; set; } = 5000;

    /// <summary>
    /// Kamailio SIP proxy hostname
    /// Environment variable: KAMAILIOHOST or BANNOU_KAMAILIOHOST
    /// </summary>
    public string KamailioHost { get; set; } = "localhost";

    /// <summary>
    /// Kamailio JSONRPC HTTP port
    /// Environment variable: KAMAILIORPCPORT or BANNOU_KAMAILIORPCPORT
    /// </summary>
    public int KamailioRpcPort { get; set; } = 5080;

    /// <summary>
    /// RTPEngine media relay hostname
    /// Environment variable: RTPENGINEHOST or BANNOU_RTPENGINEHOST
    /// </summary>
    public string RtpEngineHost { get; set; } = "localhost";

    /// <summary>
    /// RTPEngine ng protocol UDP port
    /// Environment variable: RTPENGINEPORT or BANNOU_RTPENGINEPORT
    /// </summary>
    public int RtpEnginePort { get; set; } = 22222;

    /// <summary>
    /// SIP domain for conference URIs
    /// Environment variable: SIPDOMAIN or BANNOU_SIPDOMAIN
    /// </summary>
    public string SipDomain { get; set; } = "voice.bannou";

    /// <summary>
    /// Salt for deterministic SIP password generation (change in production)
    /// Environment variable: SIPPASSWORDSALT or BANNOU_SIPPASSWORDSALT
    /// </summary>
    public string SipPasswordSalt { get; set; } = "bannou-voice-default-salt";

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
