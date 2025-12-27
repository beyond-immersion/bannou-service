using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Configuration;
using System.ComponentModel.DataAnnotations;

namespace BeyondImmersion.BannouService.Accounts;

/// <summary>
/// Configuration class for Accounts service.
/// Properties are automatically bound from environment variables.
/// </summary>
[ServiceConfiguration(typeof(AccountsService))]
public class AccountsServiceConfiguration : IServiceConfiguration
{
    /// <inheritdoc />
    public string? ForceServiceId { get; set; }

    /// <summary>
    /// Comma-separated list of admin email addresses
    /// Environment variable: ACCOUNTS_ADMIN_EMAILS
    /// </summary>
    public string AdminEmails { get; set; } = string.Empty;

    /// <summary>
    /// Email domain that grants admin access (e.g., "@company.com")
    /// Environment variable: ACCOUNTS_ADMIN_EMAIL_DOMAIN
    /// </summary>
    public string AdminEmailDomain { get; set; } = string.Empty;

}
