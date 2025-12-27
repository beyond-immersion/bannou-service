using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Configuration;
using System.ComponentModel.DataAnnotations;

namespace BeyondImmersion.BannouService.Voice;

/// <summary>
/// Configuration class for Voice service.
/// Properties are automatically bound from environment variables.
/// </summary>
[ServiceConfiguration(typeof(VoiceService))]
public class VoiceServiceConfiguration : IServiceConfiguration
{
    /// <inheritdoc />
    public string? Force_Service_ID { get; set; }

    /// <summary>
    /// Enable scaled tier voice communication (SIP-based)
    /// Environment variable: VOICE_SCALED_TIER_ENABLED
    /// </summary>
    public bool ScaledTierEnabled { get; set; } = false;

    /// <summary>
    /// Enable automatic tier upgrade from P2P to scaled
    /// Environment variable: VOICE_TIER_UPGRADE_ENABLED
    /// </summary>
    public bool TierUpgradeEnabled { get; set; } = false;

    /// <summary>
    /// Migration deadline in milliseconds when upgrading tiers
    /// Environment variable: VOICE_TIER_UPGRADE_MIGRATION_DEADLINE_MS
    /// </summary>
    public int TierUpgradeMigrationDeadlineMs { get; set; } = 30000;

    /// <summary>
    /// Maximum participants in P2P voice sessions
    /// Environment variable: VOICE_P2P_MAX_PARTICIPANTS
    /// </summary>
    public int P2PMaxParticipants { get; set; } = 8;

    /// <summary>
    /// Maximum participants in scaled tier voice sessions
    /// Environment variable: VOICE_SCALED_MAX_PARTICIPANTS
    /// </summary>
    public int ScaledMaxParticipants { get; set; } = 100;

    /// <summary>
    /// Comma-separated list of STUN server URLs for WebRTC
    /// Environment variable: VOICE_STUN_SERVERS
    /// </summary>
    public string StunServers { get; set; } = "stun:stun.l.google.com:19302";

    /// <summary>
    /// Salt for SIP password generation
    /// Environment variable: VOICE_SIP_PASSWORD_SALT
    /// </summary>
    public string SipPasswordSalt { get; set; } = string.Empty;

    /// <summary>
    /// SIP domain for voice communication
    /// Environment variable: VOICE_SIP_DOMAIN
    /// </summary>
    public string SipDomain { get; set; } = "voice.bannou.local";

    /// <summary>
    /// Kamailio SIP server host
    /// Environment variable: VOICE_KAMAILIO_HOST
    /// </summary>
    public string KamailioHost { get; set; } = "localhost";

    /// <summary>
    /// Kamailio JSON-RPC port (typically 5080, not SIP port 5060)
    /// Environment variable: VOICE_KAMAILIO_RPC_PORT
    /// </summary>
    public int KamailioRpcPort { get; set; } = 5080;

    /// <summary>
    /// RTPEngine media relay host
    /// Environment variable: VOICE_RTPENGINE_HOST
    /// </summary>
    public string RtpEngineHost { get; set; } = "localhost";

    /// <summary>
    /// RTPEngine control port
    /// Environment variable: VOICE_RTPENGINE_PORT
    /// </summary>
    public int RtpEnginePort { get; set; } = 22222;

}
