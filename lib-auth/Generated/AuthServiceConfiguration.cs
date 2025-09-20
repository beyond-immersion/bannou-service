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

}
