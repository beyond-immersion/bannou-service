using System.ComponentModel.DataAnnotations;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;

namespace BeyondImmersion.BannouService.Connect;

/// <summary>
/// Configuration class for Connect service.
/// Properties are automatically bound from environment variables.
/// </summary>
[ServiceConfiguration(envPrefix: "BANNOU_")]
public class ConnectServiceConfiguration
{
    /// <summary>
    /// properties configuration property
    /// Environment variable: PROPERTIES or BANNOU_PROPERTIES
    /// </summary>
    public string Properties = string.Empty;

}
