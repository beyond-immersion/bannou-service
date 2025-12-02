using System.ComponentModel.DataAnnotations;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Configuration;

namespace BeyondImmersion.BannouService.Accounts;

/// <summary>
/// Configuration class for Accounts service.
/// Properties are automatically bound from environment variables.
/// </summary>
[ServiceConfiguration(typeof(AccountsService), envPrefix: "BANNOU_")]
public class AccountsServiceConfiguration : IServiceConfiguration
{
    /// <inheritdoc />
    public string? Force_Service_ID { get; set; }

    /// <summary>
    /// Default page size for account listings
    /// Environment variable: DEFAULTPAGESIZE or BANNOU_DEFAULTPAGESIZE
    /// </summary>
    public int DefaultPageSize { get; set; } = 20;

    /// <summary>
    /// Maximum allowed page size for account listings
    /// Environment variable: MAXPAGESIZE or BANNOU_MAXPAGESIZE
    /// </summary>
    public int MaxPageSize { get; set; } = 100;

    /// <summary>
    /// Number of days to retain deleted account data
    /// Environment variable: ACCOUNTRETENTIONDAYS or BANNOU_ACCOUNTRETENTIONDAYS
    /// </summary>
    public int AccountRetentionDays { get; set; } = 30;

    /// <summary>
    /// Comma-separated list of emails that automatically get admin role assigned
    /// Environment variable: ADMINEMAILS or BANNOU_ADMINEMAILS
    /// </summary>
    public string AdminEmails { get; set; } = "";

    /// <summary>
    /// Email domain that automatically gets admin role (e.g., "@admin.test.local")
    /// Environment variable: ADMINEMAILDOMAIN or BANNOU_ADMINEMAILDOMAIN
    /// </summary>
    public string AdminEmailDomain { get; set; } = "";

}
