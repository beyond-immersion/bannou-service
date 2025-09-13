using System.ComponentModel.DataAnnotations;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Configuration;

namespace BeyondImmersion.BannouService.Auth;

/// <summary>
/// Generated configuration for Auth service
/// </summary>
[ServiceConfiguration(typeof(AuthService), envPrefix: "AUTH_")]
public class AuthServiceConfiguration : IServiceConfiguration
{
    /// <summary>
    /// Force specific service ID (optional)
    /// </summary>
    public string? Force_Service_ID { get; set; }

    /// <summary>
    /// Disable this service (optional)
    /// </summary>
    public bool? Service_Disabled { get; set; }

    /// <summary>
    /// JWT secret key for token signing and validation
    /// </summary>
    [Required]
    public string JwtSecret { get; set; } = string.Empty;

    /// <summary>
    /// JWT token issuer
    /// </summary>
    [Required]
    public string JwtIssuer { get; set; } = "bannou-auth";

    /// <summary>
    /// JWT token audience
    /// </summary>
    [Required]
    public string JwtAudience { get; set; } = "bannou-api";

    /// <summary>
    /// JWT access token expiration time in minutes
    /// </summary>
    public int JwtExpirationMinutes { get; set; } = 60;

    /// <summary>
    /// OAuth callback base URL for external providers
    /// </summary>
    public string OAuthCallbackBaseUrl { get; set; } = "https://localhost/auth/callback";

    /// <summary>
    /// Maximum login attempts before rate limiting
    /// </summary>
    public int MaxLoginAttempts { get; set; } = 5;

    /// <summary>
    /// Rate limit window in minutes
    /// </summary>
    public int RateLimitWindowMinutes { get; set; } = 15;
}
