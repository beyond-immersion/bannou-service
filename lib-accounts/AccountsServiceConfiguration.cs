using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Configuration;
using System.ComponentModel.DataAnnotations;

namespace BeyondImmersion.BannouService.Accounts;

/// <summary>
/// Configuration for the Accounts service.
/// </summary>
[ServiceConfiguration(typeof(AccountsService), envPrefix: "ACCOUNTS_")]
public class AccountsServiceConfiguration : IServiceConfiguration
{
    /// <summary>
    /// MySQL connection string for accounts database.
    /// </summary>
    [Required]
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// Whether to automatically apply database migrations on startup.
    /// </summary>
    public bool AutoMigrate { get; set; } = true;

    /// <summary>
    /// Maximum page size for account listing operations.
    /// </summary>
    public int MaxPageSize { get; set; } = 100;

    /// <summary>
    /// Default page size for account listing operations.
    /// </summary>
    public int DefaultPageSize { get; set; } = 20;

    /// <summary>
    /// Whether to enable soft delete for accounts (recommended for production).
    /// </summary>
    public bool EnableSoftDelete { get; set; } = true;

    /// <summary>
    /// Account cache expiration time in minutes.
    /// </summary>
    public int CacheExpirationMinutes { get; set; } = 30;

    /// <summary>
    /// Maximum display name length.
    /// </summary>
    public int MaxDisplayNameLength { get; set; } = 100;

    /// <summary>
    /// Default roles assigned to new accounts.
    /// </summary>
    public string[] DefaultRoles { get; set; } = ["user"];

    /// <summary>
    /// Whether email verification is required for new accounts.
    /// </summary>
    public bool RequireEmailVerification { get; set; } = true;
}