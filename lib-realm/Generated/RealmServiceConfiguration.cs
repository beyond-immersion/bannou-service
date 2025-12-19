using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Configuration;
using System.ComponentModel.DataAnnotations;

namespace BeyondImmersion.BannouService.Realm;

/// <summary>
/// Configuration class for Realm service.
/// Properties are automatically bound from environment variables.
/// </summary>
[ServiceConfiguration(typeof(RealmService), envPrefix: "BANNOU_")]
public class RealmServiceConfiguration : IServiceConfiguration
{
    /// <inheritdoc />
    public string? Force_Service_ID { get; set; }

    /// <summary>
    /// Default realm code for new entities when not specified
    /// Environment variable: DEFAULTREALMCODE or BANNOU_DEFAULTREALMCODE
    /// </summary>
    public string DefaultRealmCode { get; set; } = "OMEGA";

}
