using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Configuration;
using System.ComponentModel.DataAnnotations;

namespace BeyondImmersion.BannouService.Auth;

/// <summary>
/// Configuration class for Auth service.
/// Properties are automatically bound from environment variables.
/// </summary>
[ServiceConfiguration(typeof(AuthService), envPrefix: "BANNOU_")]
public class AuthServiceConfiguration : IServiceConfiguration
{
    /// <inheritdoc />
    public string? Force_Service_ID { get; set; }

    /// <summary>
    /// Bcrypt work factor for password hashing (higher = more secure but slower)
    /// Environment variable: BCRYPTWORKFACTOR or BANNOU_BCRYPTWORKFACTOR
    /// </summary>
    public int BcryptWorkFactor { get; set; } = 12;

    /// <summary>
    /// JWT secret key for token signing
    /// Environment variable: JWTSECRET or BANNOU_JWTSECRET
    /// </summary>
    public string JwtSecret { get; set; } = string.Empty;

    /// <summary>
    /// JWT issuer identifier
    /// Environment variable: JWTISSUER or BANNOU_JWTISSUER
    /// </summary>
    public string JwtIssuer { get; set; } = "bannou-auth";

    /// <summary>
    /// JWT audience identifier
    /// Environment variable: JWTAUDIENCE or BANNOU_JWTAUDIENCE
    /// </summary>
    public string JwtAudience { get; set; } = "bannou-services";

    /// <summary>
    /// JWT expiration time in minutes
    /// Environment variable: JWTEXPIRATIONMINUTES or BANNOU_JWTEXPIRATIONMINUTES
    /// </summary>
    public int JwtExpirationMinutes { get; set; } = 1440;

    /// <summary>
    /// Access token expiration time in minutes (alias for JwtExpirationMinutes)
    /// Environment variable: ACCESSTOKENEXPIRATIONMINUTES or BANNOU_ACCESSTOKENEXPIRATIONMINUTES
    /// </summary>
    public int AccessTokenExpirationMinutes { get; set; } = 1440;

    /// <summary>
    /// When true, use mock providers instead of real OAuth/Steam APIs
    /// Environment variable: MOCKPROVIDERS or BANNOU_MOCKPROVIDERS
    /// </summary>
    public bool MockProviders { get; set; } = false;

    /// <summary>
    /// Mock SteamID returned when MockProviders=true (64-bit Steam ID)
    /// Environment variable: MOCKSTEAMID or BANNOU_MOCKSTEAMID
    /// </summary>
    public string MockSteamId { get; set; } = "76561198000000001";

    /// <summary>
    /// Mock Discord user ID returned when MockProviders=true
    /// Environment variable: MOCKDISCORDID or BANNOU_MOCKDISCORDID
    /// </summary>
    public string MockDiscordId { get; set; } = "123456789012345678";

    /// <summary>
    /// Mock Google user ID returned when MockProviders=true
    /// Environment variable: MOCKGOOGLEID or BANNOU_MOCKGOOGLEID
    /// </summary>
    public string MockGoogleId { get; set; } = "mock-google-user-id-12345";

    /// <summary>
    /// Steam Web API Publisher Key for ticket validation (from partner.steamgames.com)
    /// Environment variable: STEAMAPIKEY or BANNOU_STEAMAPIKEY
    /// </summary>
    public string SteamApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Steam Application ID for the game
    /// Environment variable: STEAMAPPID or BANNOU_STEAMAPPID
    /// </summary>
    public string SteamAppId { get; set; } = string.Empty;

    /// <summary>
    /// Discord application client ID (from discord.com/developers)
    /// Environment variable: DISCORDCLIENTID or BANNOU_DISCORDCLIENTID
    /// </summary>
    public string DiscordClientId { get; set; } = string.Empty;

    /// <summary>
    /// Discord application client secret
    /// Environment variable: DISCORDCLIENTSECRET or BANNOU_DISCORDCLIENTSECRET
    /// </summary>
    public string DiscordClientSecret { get; set; } = string.Empty;

    /// <summary>
    /// Discord OAuth2 redirect URI
    /// Environment variable: DISCORDREDIRECTURI or BANNOU_DISCORDREDIRECTURI
    /// </summary>
    public string DiscordRedirectUri { get; set; } = "http://localhost:5012/auth/oauth/discord/callback";

    /// <summary>
    /// Google OAuth2 client ID (from console.cloud.google.com)
    /// Environment variable: GOOGLECLIENTID or BANNOU_GOOGLECLIENTID
    /// </summary>
    public string GoogleClientId { get; set; } = string.Empty;

    /// <summary>
    /// Google OAuth2 client secret
    /// Environment variable: GOOGLECLIENTSECRET or BANNOU_GOOGLECLIENTSECRET
    /// </summary>
    public string GoogleClientSecret { get; set; } = string.Empty;

    /// <summary>
    /// Google OAuth2 redirect URI
    /// Environment variable: GOOGLEREDIRECTURI or BANNOU_GOOGLEREDIRECTURI
    /// </summary>
    public string GoogleRedirectUri { get; set; } = "http://localhost:5012/auth/oauth/google/callback";

    /// <summary>
    /// Twitch application client ID (from dev.twitch.tv)
    /// Environment variable: TWITCHCLIENTID or BANNOU_TWITCHCLIENTID
    /// </summary>
    public string TwitchClientId { get; set; } = string.Empty;

    /// <summary>
    /// Twitch application client secret
    /// Environment variable: TWITCHCLIENTSECRET or BANNOU_TWITCHCLIENTSECRET
    /// </summary>
    public string TwitchClientSecret { get; set; } = string.Empty;

    /// <summary>
    /// Twitch OAuth2 redirect URI
    /// Environment variable: TWITCHREDIRECTURI or BANNOU_TWITCHREDIRECTURI
    /// </summary>
    public string TwitchRedirectUri { get; set; } = "http://localhost:5012/auth/oauth/twitch/callback";

}
