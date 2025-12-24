using System.ComponentModel.DataAnnotations;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Configuration;

namespace BeyondImmersion.BannouService.GameSession;

/// <summary>
/// Configuration class for GameSession service.
/// Properties are automatically bound from environment variables.
/// </summary>
[ServiceConfiguration(typeof(GameSessionService), envPrefix: "BANNOU_")]
public class GameSessionServiceConfiguration : IServiceConfiguration
{
    /// <inheritdoc />
    public string? Force_Service_ID { get; set; }

    /// <summary>
    /// Enable/disable Game Session service
    /// Environment variable: ENABLED or BANNOU_ENABLED
    /// </summary>
    public bool Enabled { get; set; } = true;

}
