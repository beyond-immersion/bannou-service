using System.ComponentModel.DataAnnotations;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Configuration;

namespace BeyondImmersion.BannouService.Servicedata;

/// <summary>
/// Configuration class for Servicedata service.
/// Properties are automatically bound from environment variables.
/// </summary>
[ServiceConfiguration(typeof(ServicedataService))]
public class ServicedataServiceConfiguration : IServiceConfiguration
{
    /// <inheritdoc />
    public string? Force_Service_ID { get; set; }

    /// <summary>
    /// Dapr state store name for service data
    /// Environment variable: SERVICEDATA_STATE_STORE_NAME
    /// </summary>
    public string StateStoreName { get; set; } = "servicedata-statestore";

}
