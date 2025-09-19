using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Configuration;
using System.ComponentModel.DataAnnotations;

namespace BeyondImmersion.BannouService.Accounts;

/// <summary>
/// Configuration class for Accounts service.
/// Properties are automatically bound from environment variables.
/// </summary>
[ServiceConfiguration(typeof(IAccountsService), envPrefix: "BANNOU_")]
public class AccountsServiceConfiguration : IServiceConfiguration
{
    /// <inheritdoc />
    public string? Force_Service_ID { get; set; }

    /// <summary>
    /// Default page size for account listings
    /// Environment variable: DEFAULTPAGESIZE or BANNOU_DEFAULTPAGESIZE
    /// </summary>
    public int DefaultPageSize = 20;

    /// <summary>
    /// Maximum allowed page size for account listings
    /// Environment variable: MAXPAGESIZE or BANNOU_MAXPAGESIZE
    /// </summary>
    public int MaxPageSize = 100;

    /// <summary>
    /// Number of days to retain deleted account data
    /// Environment variable: ACCOUNTRETENTIONDAYS or BANNOU_ACCOUNTRETENTIONDAYS
    /// </summary>
    public int AccountRetentionDays = 30;

}
