using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Configuration;
using System.ComponentModel.DataAnnotations;

namespace BeyondImmersion.BannouService.Species;

/// <summary>
/// Configuration class for Species service.
/// Properties are automatically bound from environment variables.
/// </summary>
[ServiceConfiguration(typeof(SpeciesService), envPrefix: "BANNOU_")]
public class SpeciesServiceConfiguration : IServiceConfiguration
{
    /// <inheritdoc />
    public string? Force_Service_ID { get; set; }

    /// <summary>
    /// Default configuration property - can be removed if not needed.
    /// Environment variable: SPECIES_ENABLED or BANNOU_SPECIES_ENABLED
    /// </summary>
    public bool Enabled { get; set; } = true;

}
