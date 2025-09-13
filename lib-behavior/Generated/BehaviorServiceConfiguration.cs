using System.ComponentModel.DataAnnotations;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Configuration;

namespace BeyondImmersion.BannouService.Behavior;

/// <summary>
/// Generated configuration for Behavior service
/// </summary>
[ServiceConfiguration(typeof(BehaviorService), envPrefix: "BEHAVIOR_")]
public class BehaviorServiceConfiguration : IServiceConfiguration
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
