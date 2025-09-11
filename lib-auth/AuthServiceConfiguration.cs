using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Configuration;
using System.ComponentModel.DataAnnotations;

namespace BeyondImmersion.BannouService.Auth;

/// <summary>
/// Configuration for the Auth service.
/// </summary>
[ServiceConfiguration(typeof(AuthService), envPrefix: "AUTH_")]
public class AuthServiceConfiguration : IServiceConfiguration
{
    /// <summary>
    /// JWT signing secret (required for production).
    /// </summary>
    [Required]
    public string JwtSecret { get; set; } = string.Empty;

    /// <summary>
    /// JWT token issuer.
    /// </summary>
    public string JwtIssuer { get; set; } = "bannou-auth";

    /// <summary>
    /// JWT token audience.
    /// </summary>
    public string JwtAudience { get; set; } = "bannou-services";

    /// <summary>
    /// Access token expiration time in minutes.
    /// </summary>
    public int AccessTokenExpirationMinutes { get; set; } = 15;

    /// <summary>
    /// Refresh token expiration time in days.
    /// </summary>
    public int RefreshTokenExpirationDays { get; set; } = 30;

    /// <summary>
    /// Maximum login attempts before rate limiting.
    /// </summary>
    public int MaxLoginAttempts { get; set; } = 5;

    /// <summary>
    /// Rate limiting window in minutes.
    /// </summary>
    public int RateLimitWindowMinutes { get; set; } = 15;

    /// <summary>
    /// BCrypt work factor for password hashing.
    /// </summary>
    public int BcryptWorkFactor { get; set; } = 12;

    /// <summary>
    /// Whether to require email verification for new accounts.
    /// </summary>
    public bool RequireEmailVerification { get; set; } = true;

    /// <summary>
    /// Email verification token expiration time in hours.
    /// </summary>
    public int EmailVerificationExpirationHours { get; set; } = 24;

    /// <summary>
    /// Password reset token expiration time in hours.
    /// </summary>
    public int PasswordResetExpirationHours { get; set; } = 2;

    /// <summary>
    /// Google OAuth client ID.
    /// </summary>
    public string? GoogleClientId { get; set; }

    /// <summary>
    /// Google OAuth client secret.
    /// </summary>
    public string? GoogleClientSecret { get; set; }

    /// <summary>
    /// Discord OAuth client ID.
    /// </summary>
    public string? DiscordClientId { get; set; }

    /// <summary>
    /// Discord OAuth client secret.
    /// </summary>
    public string? DiscordClientSecret { get; set; }

    /// <summary>
    /// Twitch OAuth client ID.
    /// </summary>
    public string? TwitchClientId { get; set; }

    /// <summary>
    /// Twitch OAuth client secret.
    /// </summary>
    public string? TwitchClientSecret { get; set; }

    /// <summary>
    /// Steam Web API key.
    /// </summary>
    public string? SteamApiKey { get; set; }

    /// <summary>
    /// Base URL for OAuth redirect URIs.
    /// </summary>
    public string BaseUrl { get; set; } = "http://localhost:5000";

    /// <summary>
    /// SMTP configuration for email sending.
    /// </summary>
    public SmtpConfiguration Smtp { get; set; } = new();
}

/// <summary>
/// SMTP configuration for email sending.
/// </summary>
public class SmtpConfiguration
{
    /// <summary>
    /// SMTP server host.
    /// </summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>
    /// SMTP server port.
    /// </summary>
    public int Port { get; set; } = 587;

    /// <summary>
    /// Whether to use SSL/TLS.
    /// </summary>
    public bool EnableSsl { get; set; } = true;

    /// <summary>
    /// SMTP username.
    /// </summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// SMTP password.
    /// </summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>
    /// From email address.
    /// </summary>
    public string FromEmail { get; set; } = string.Empty;

    /// <summary>
    /// From display name.
    /// </summary>
    public string FromName { get; set; } = "Bannou Service";
}