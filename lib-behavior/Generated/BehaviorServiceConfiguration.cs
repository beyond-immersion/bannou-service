using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Configuration;
using System.ComponentModel.DataAnnotations;

namespace BeyondImmersion.BannouService.Behavior;

/// <summary>
/// Configuration class for Behavior service.
/// Properties are automatically bound from environment variables.
/// </summary>
[ServiceConfiguration(typeof(BehaviorService))]
public class BehaviorServiceConfiguration : IServiceConfiguration
{
    /// <inheritdoc />
    public string? ForceServiceId { get; set; }

    /// <summary>
    /// Enable/disable Behavior service
    /// Environment variable: ENABLED
    /// </summary>
    public bool Enabled { get; set; } = true;

}
