using System.ComponentModel.DataAnnotations;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Configuration;

namespace BeyondImmersion.BannouService.Behavior;

/// <summary>
/// Configuration class for Behavior service.
/// Properties are automatically bound from environment variables.
/// </summary>
[ServiceConfiguration(typeof(BehaviorService), envPrefix: "BANNOU_")]
public class BehaviorServiceConfiguration : IServiceConfiguration
{
    /// <inheritdoc />
    public string? Force_Service_ID { get; set; }

    /// <summary>
    /// Enable/disable Behavior service
    /// Environment variable: ENABLED or BANNOU_ENABLED
    /// </summary>
    public bool Enabled { get; set; } = true;

}
