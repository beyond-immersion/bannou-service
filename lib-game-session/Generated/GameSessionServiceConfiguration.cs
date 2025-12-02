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
    /// Default configuration property - can be removed if not needed.
    /// Environment variable: GAME-SESSION_ENABLED or BANNOU_GAME-SESSION_ENABLED
    /// </summary>
    public bool Enabled { get; set; } = true;

}
