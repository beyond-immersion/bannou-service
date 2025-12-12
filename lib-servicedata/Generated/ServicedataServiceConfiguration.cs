using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Configuration;
using System.ComponentModel.DataAnnotations;

namespace BeyondImmersion.BannouService.Servicedata;

/// <summary>
/// Configuration class for Servicedata service.
/// Properties are automatically bound from environment variables.
/// </summary>
[ServiceConfiguration(typeof(ServicedataService), envPrefix: "BANNOU_")]
public class ServicedataServiceConfiguration : IServiceConfiguration
{
    /// <inheritdoc />
    public string? Force_Service_ID { get; set; }

    /// <summary>
    /// Name of the Dapr state store for service data
    /// Environment variable: STATESTORENAME or BANNOU_STATESTORENAME
    /// </summary>
    public string StateStoreName { get; set; } = "servicedata-statestore";

}
