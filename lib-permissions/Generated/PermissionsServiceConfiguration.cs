using System.ComponentModel.DataAnnotations;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;

namespace BeyondImmersion.BannouService.Permissions;

/// <summary>
/// Configuration class for Permissions service.
/// Properties are automatically bound from environment variables.
/// </summary>
[ServiceConfiguration(envPrefix: "BANNOU_")]
public class PermissionsServiceConfiguration
{
    /// <summary>
    /// Default configuration property - can be removed if not needed.
    /// Environment variable: PERMISSIONS_ENABLED or BANNOU_PERMISSIONS_ENABLED
    /// </summary>
    public bool Enabled { get; set; } = true;

}
