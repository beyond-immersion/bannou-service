using System.ComponentModel.DataAnnotations;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Configuration;

namespace BeyondImmersion.BannouService.GameSession;

/// <summary>
/// Generated configuration for GameSession service
/// </summary>
[ServiceConfiguration(typeof(GameSessionService), envPrefix: "GAME_SESSION_")]
public class GameSessionServiceConfiguration : IServiceConfiguration
{
    /// <summary>
    /// Force specific service ID (optional)
    /// </summary>
    public string? Force_Service_ID { get; set; }

    /// <summary>
    /// Disable this service (optional)
    /// </summary>
    public bool? Service_Disabled { get; set; }

    // TODO: Add service-specific configuration properties from schema
    // Example properties:
    // [Required]
    // public string ConnectionString { get; set; } = string.Empty;
    //
    // public int MaxRetries { get; set; } = 3;
    //
    // public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);
}
