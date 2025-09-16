using System.ComponentModel.DataAnnotations;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Configuration;

namespace BeyondImmersion.BannouService.Accounts;

/// <summary>
/// Configuration class for Accounts service.
/// Properties are automatically bound from environment variables.
/// </summary>
[ServiceConfiguration(envPrefix: "BANNOU_")]
public class AccountsServiceConfiguration : IServiceConfiguration
{
    /// <inheritdoc />
    public string? Force_Service_ID { get; set; }

    /// <inheritdoc />
    public bool? Service_Disabled { get; set; }

    /// <summary>
    /// properties configuration property
    /// Environment variable: PROPERTIES or BANNOU_PROPERTIES
    /// </summary>
    public string Properties = string.Empty;

}
