using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Configuration;
using System.ComponentModel.DataAnnotations;

namespace BeyondImmersion.BannouService.GameSession;

/// <summary>
/// Configuration class for GameSession service.
/// Properties are automatically bound from environment variables.
/// </summary>
[ServiceConfiguration(typeof(GameSessionService))]
public class GameSessionServiceConfiguration : IServiceConfiguration
{
    /// <inheritdoc />
    public string? ForceServiceId { get; set; }

    /// <summary>
    /// Enable/disable Game Session service
    /// Environment variable: ENABLED
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Server salt for GUID generation. If not set, generates random salt (OK for development, must be shared in production)
    /// Environment variable: SERVERSALT
    /// </summary>
    public string ServerSalt { get; set; } = string.Empty;

    /// <summary>
    /// Maximum players allowed per session
    /// Environment variable: MAXPLAYERSPERSESSION
    /// </summary>
    public int MaxPlayersPerSession { get; set; } = 16;

    /// <summary>
    /// Default session timeout in seconds
    /// Environment variable: DEFAULTSESSIONTIMEOUTSECONDS
    /// </summary>
    public int DefaultSessionTimeoutSeconds { get; set; } = 7200;

}
