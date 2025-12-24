using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Configuration;
using System.ComponentModel.DataAnnotations;

namespace BeyondImmersion.BannouService.State;

/// <summary>
/// Configuration class for State service.
/// Properties are automatically bound from environment variables.
/// </summary>
[ServiceConfiguration(typeof(StateService), envPrefix: "BANNOU_")]
public class StateServiceConfiguration : IServiceConfiguration
{
    /// <inheritdoc />
    public string? Force_Service_ID { get; set; }

    /// <summary>
    /// Default configuration property - can be removed if not needed.
    /// Environment variable: STATE_ENABLED or BANNOU_STATE_ENABLED
    /// </summary>
    public bool Enabled { get; set; } = true;

}
