using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Configuration;
using System.ComponentModel.DataAnnotations;

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
    /// Default configuration property - can be removed if not needed.
    /// Environment variable: RELATIONSHIP_ENABLED or BANNOU_RELATIONSHIP_ENABLED
    /// </summary>
    public bool Enabled { get; set; } = true;

}
