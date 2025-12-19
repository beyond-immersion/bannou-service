using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Configuration;
using System.ComponentModel.DataAnnotations;

namespace BeyondImmersion.BannouService.Location;

/// <summary>
/// Configuration class for Location service.
/// Properties are automatically bound from environment variables.
/// </summary>
[ServiceConfiguration(typeof(LocationService), envPrefix: "BANNOU_")]
public class LocationServiceConfiguration : IServiceConfiguration
{
    /// <inheritdoc />
    public string? Force_Service_ID { get; set; }

    /// <summary>
    /// Default page size for location listings
    /// Environment variable: DEFAULTPAGESIZE or BANNOU_DEFAULTPAGESIZE
    /// </summary>
    public int DefaultPageSize { get; set; } = 20;

    /// <summary>
    /// Maximum allowed page size for location listings
    /// Environment variable: MAXPAGESIZE or BANNOU_MAXPAGESIZE
    /// </summary>
    public int MaxPageSize { get; set; } = 100;

    /// <summary>
    /// Maximum allowed depth for location hierarchy
    /// Environment variable: MAXHIERARCHYDEPTH or BANNOU_MAXHIERARCHYDEPTH
    /// </summary>
    public int MaxHierarchyDepth { get; set; } = 10;

}
