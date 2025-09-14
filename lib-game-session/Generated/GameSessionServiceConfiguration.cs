using System.ComponentModel.DataAnnotations;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;

namespace BeyondImmersion.BannouService.GameSession;

/// <summary>
/// Configuration class for GameSession service.
/// Properties are automatically bound from environment variables.
/// </summary>
[ServiceConfiguration(envPrefix: "BANNOU_")]
public class GameSessionServiceConfiguration
{
    /// <summary>
    /// Default configuration property - can be removed if not needed.
    /// Environment variable: GAME-SESSION_ENABLED or BANNOU_GAME-SESSION_ENABLED
    /// </summary>
    public bool Enabled { get; set; } = true;

}
