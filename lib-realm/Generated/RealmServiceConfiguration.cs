using System.ComponentModel.DataAnnotations;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Configuration;

namespace BeyondImmersion.BannouService.Realm;

/// <summary>
/// Configuration class for Realm service.
/// Properties are automatically bound from environment variables.
/// </summary>
[ServiceConfiguration(typeof(RealmService))]
public class RealmServiceConfiguration : IServiceConfiguration
{
    /// <inheritdoc />
    public string? Force_Service_ID { get; set; }

    /// <summary>
    /// Enable/disable Realm service
    /// Environment variable: ENABLED
    /// </summary>
    public bool Enabled { get; set; } = true;

}
