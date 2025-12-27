using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Configuration;
using System.ComponentModel.DataAnnotations;

namespace BeyondImmersion.BannouService.Permissions;

/// <summary>
/// Configuration class for Permissions service.
/// Properties are automatically bound from environment variables.
/// </summary>
[ServiceConfiguration(typeof(PermissionsService))]
public class PermissionsServiceConfiguration : IServiceConfiguration
{
    /// <inheritdoc />
    public string? ForceServiceId { get; set; }

    /// <summary>
    /// Enable/disable Permissions service
    /// Environment variable: ENABLED
    /// </summary>
    public bool Enabled { get; set; } = true;

}
