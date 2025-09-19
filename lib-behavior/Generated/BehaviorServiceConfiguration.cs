using System.ComponentModel.DataAnnotations;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Configuration;

namespace BeyondImmersion.BannouService.Behavior;

/// <summary>
/// Configuration class for Behavior service.
/// Properties are automatically bound from environment variables.
/// </summary>
[ServiceConfiguration(typeof(IBehaviorService), envPrefix: "BANNOU_")]
public class BehaviorServiceConfiguration : IServiceConfiguration
{
    /// <inheritdoc />
    public string? Force_Service_ID { get; set; }

    /// <summary>
    /// properties configuration property
    /// Environment variable: PROPERTIES or BANNOU_PROPERTIES
    /// </summary>
    public string Properties = string.Empty;

}
