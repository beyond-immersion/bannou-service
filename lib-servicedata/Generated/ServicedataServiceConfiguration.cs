using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Configuration;
using System.ComponentModel.DataAnnotations;

namespace BeyondImmersion.BannouService.Servicedata;

/// <summary>
/// Configuration class for Servicedata service.
/// Properties are automatically bound from environment variables.
/// </summary>
[ServiceConfiguration(typeof(ServicedataService))]
public class ServicedataServiceConfiguration : IServiceConfiguration
{
    /// <inheritdoc />
    public string? ForceServiceId { get; set; }

    /// <summary>
    /// State store name for service data
    /// Environment variable: SERVICEDATA_STATE_STORE_NAME
    /// </summary>
    public string StateStoreName { get; set; } = "servicedata-statestore";

}
