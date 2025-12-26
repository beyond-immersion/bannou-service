using System.ComponentModel.DataAnnotations;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Configuration;

namespace BeyondImmersion.BannouService.Location;

/// <summary>
/// Configuration class for Location service.
/// Properties are automatically bound from environment variables.
/// </summary>
[ServiceConfiguration(typeof(LocationService))]
public class LocationServiceConfiguration : IServiceConfiguration
{
    /// <inheritdoc />
    public string? Force_Service_ID { get; set; }

    /// <summary>
    /// Enable/disable Location service
    /// Environment variable: ENABLED
    /// </summary>
    public bool Enabled { get; set; } = true;

}
