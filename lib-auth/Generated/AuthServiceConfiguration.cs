using System.ComponentModel.DataAnnotations;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Configuration;

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
    /// Secret key for JWT token signing
    /// Environment variable: AUTH_JWT_SECRET or BANNOU_AUTH_JWT_SECRET
    /// </summary>
    public string JwtSecret { get; set; } = string.Empty;

    /// <summary>
    /// JWT token issuer
    /// Environment variable: AUTH_JWT_ISSUER or BANNOU_AUTH_JWT_ISSUER
    /// </summary>
    public string JwtIssuer { get; set; } = "bannou-auth";

    /// <summary>
    /// JWT token audience
    /// Environment variable: AUTH_JWT_AUDIENCE or BANNOU_AUTH_JWT_AUDIENCE
    /// </summary>
    public string JwtAudience { get; set; } = "bannou-api";

    /// <summary>
    /// JWT token expiration time in minutes
    /// Environment variable: AUTH_JWT_EXPIRATION_MINUTES or BANNOU_AUTH_JWT_EXPIRATION_MINUTES
    /// </summary>
    public int JwtExpirationMinutes { get; set; } = 60;

    /// <summary>
    /// URL to the Connect service for WebSocket connections
    /// Environment variable: AUTH_CONNECT_URL or BANNOU_AUTH_CONNECT_URL
    /// </summary>
    public string ConnectUrl { get; set; } = "ws://localhost:5014/connect";

    /// <summary>
    /// Enable mock OAuth providers for testing
    /// Environment variable: AUTH_MOCK_PROVIDERS or BANNOU_AUTH_MOCK_PROVIDERS
    /// </summary>
    public bool MockProviders { get; set; } = false;

    /// <summary>
    /// Mock Discord user ID for testing
    /// Environment variable: AUTH_MOCK_DISCORD_ID or BANNOU_AUTH_MOCK_DISCORD_ID
    /// </summary>
    public string MockDiscordId { get; set; } = "mock-discord-123456";

    /// <summary>
    /// Mock Google user ID for testing
    /// Environment variable: AUTH_MOCK_GOOGLE_ID or BANNOU_AUTH_MOCK_GOOGLE_ID
    /// </summary>
    public string MockGoogleId { get; set; } = "mock-google-123456";

    /// <summary>
    /// Mock Steam user ID for testing
    /// Environment variable: AUTH_MOCK_STEAM_ID or BANNOU_AUTH_MOCK_STEAM_ID
    /// </summary>
    public string MockSteamId { get; set; } = "76561198000000000";

    /// <summary>
    /// Discord OAuth client ID
    /// Environment variable: AUTH_DISCORD_CLIENT_ID or BANNOU_AUTH_DISCORD_CLIENT_ID
    /// </summary>
    public string DiscordClientId { get; set; } = string.Empty;

    /// <summary>
    /// Discord OAuth client secret
    /// Environment variable: AUTH_DISCORD_CLIENT_SECRET or BANNOU_AUTH_DISCORD_CLIENT_SECRET
    /// </summary>
    public string DiscordClientSecret { get; set; } = string.Empty;

    /// <summary>
    /// Discord OAuth redirect URI
    /// Environment variable: AUTH_DISCORD_REDIRECT_URI or BANNOU_AUTH_DISCORD_REDIRECT_URI
    /// </summary>
    public string DiscordRedirectUri { get; set; } = string.Empty;

    /// <summary>
    /// Google OAuth client ID
    /// Environment variable: AUTH_GOOGLE_CLIENT_ID or BANNOU_AUTH_GOOGLE_CLIENT_ID
    /// </summary>
    public string GoogleClientId { get; set; } = string.Empty;

    /// <summary>
    /// Google OAuth client secret
    /// Environment variable: AUTH_GOOGLE_CLIENT_SECRET or BANNOU_AUTH_GOOGLE_CLIENT_SECRET
    /// </summary>
    public string GoogleClientSecret { get; set; } = string.Empty;

    /// <summary>
    /// Google OAuth redirect URI
    /// Environment variable: AUTH_GOOGLE_REDIRECT_URI or BANNOU_AUTH_GOOGLE_REDIRECT_URI
    /// </summary>
    public string GoogleRedirectUri { get; set; } = string.Empty;

    /// <summary>
    /// Twitch OAuth client ID
    /// Environment variable: AUTH_TWITCH_CLIENT_ID or BANNOU_AUTH_TWITCH_CLIENT_ID
    /// </summary>
    public string TwitchClientId { get; set; } = string.Empty;

    /// <summary>
    /// Twitch OAuth client secret
    /// Environment variable: AUTH_TWITCH_CLIENT_SECRET or BANNOU_AUTH_TWITCH_CLIENT_SECRET
    /// </summary>
    public string TwitchClientSecret { get; set; } = string.Empty;

    /// <summary>
    /// Twitch OAuth redirect URI
    /// Environment variable: AUTH_TWITCH_REDIRECT_URI or BANNOU_AUTH_TWITCH_REDIRECT_URI
    /// </summary>
    public string TwitchRedirectUri { get; set; } = string.Empty;

    /// <summary>
    /// Steam Web API key for session ticket validation
    /// Environment variable: AUTH_STEAM_API_KEY or BANNOU_AUTH_STEAM_API_KEY
    /// </summary>
    public string SteamApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Steam application ID
    /// Environment variable: AUTH_STEAM_APP_ID or BANNOU_AUTH_STEAM_APP_ID
    /// </summary>
    public string SteamAppId { get; set; } = string.Empty;

    /// <summary>
    /// Password reset token expiration time in minutes
    /// Environment variable: AUTH_PASSWORD_RESET_TOKEN_TTL_MINUTES or BANNOU_AUTH_PASSWORD_RESET_TOKEN_TTL_MINUTES
    /// </summary>
    public int PasswordResetTokenTtlMinutes { get; set; } = 30;

    /// <summary>
    /// Base URL for password reset page
    /// Environment variable: AUTH_PASSWORD_RESET_BASE_URL or BANNOU_AUTH_PASSWORD_RESET_BASE_URL
    /// </summary>
    public string PasswordResetBaseUrl { get; set; } = string.Empty;

}
