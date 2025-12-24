using System.ComponentModel.DataAnnotations;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Configuration;

namespace BeyondImmersion.BannouService.Relationship;

/// <summary>
/// Configuration class for Relationship service.
/// Properties are automatically bound from environment variables.
/// </summary>
[ServiceConfiguration(typeof(RelationshipService), envPrefix: "BANNOU_")]
public class RelationshipServiceConfiguration : IServiceConfiguration
{
    /// <inheritdoc />
    public string? Force_Service_ID { get; set; }

    /// <summary>
    /// Enable/disable Relationship service
    /// Environment variable: ENABLED or BANNOU_ENABLED
    /// </summary>
    public bool Enabled { get; set; } = true;

}
