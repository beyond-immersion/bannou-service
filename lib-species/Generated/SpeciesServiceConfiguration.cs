using System.ComponentModel.DataAnnotations;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Configuration;

namespace BeyondImmersion.BannouService.Species;

/// <summary>
/// Configuration class for Species service.
/// Properties are automatically bound from environment variables.
/// </summary>
[ServiceConfiguration(typeof(SpeciesService))]
public class SpeciesServiceConfiguration : IServiceConfiguration
{
    /// <inheritdoc />
    public string? ForceServiceId { get; set; }

    /// <summary>
    /// Enable/disable Species service
    /// Environment variable: ENABLED
    /// </summary>
    public bool Enabled { get; set; } = true;

}
