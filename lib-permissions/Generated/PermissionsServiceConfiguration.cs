using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Configuration;
using System.ComponentModel.DataAnnotations;

namespace BeyondImmersion.BannouService.Permissions;

/// <summary>
/// Configuration class for Permissions service.
/// Properties are automatically bound from environment variables.
/// </summary>
[ServiceConfiguration(typeof(PermissionsService), envPrefix: "BANNOU_")]
public class PermissionsServiceConfiguration : IServiceConfiguration
{
    /// <inheritdoc />
    public string? Force_Service_ID { get; set; }

    /// <summary>
    /// Enable/disable Permissions service
    /// Environment variable: ENABLED or BANNOU_ENABLED
    /// </summary>
    public bool Enabled { get; set; } = true;

}
