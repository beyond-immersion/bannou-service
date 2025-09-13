using System.ComponentModel.DataAnnotations;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Configuration;

namespace BeyondImmersion.BannouService.Accounts;

/// <summary>
/// Generated configuration for Accounts service
/// </summary>
[ServiceConfiguration(typeof(AccountsService), envPrefix: "ACCOUNTS_")]
public class AccountsServiceConfiguration : IServiceConfiguration
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
