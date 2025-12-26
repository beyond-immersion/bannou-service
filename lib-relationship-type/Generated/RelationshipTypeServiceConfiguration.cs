using System.ComponentModel.DataAnnotations;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Configuration;

namespace BeyondImmersion.BannouService.RelationshipType;

/// <summary>
/// Configuration class for RelationshipType service.
/// Properties are automatically bound from environment variables.
/// </summary>
[ServiceConfiguration(typeof(RelationshipTypeService))]
public class RelationshipTypeServiceConfiguration : IServiceConfiguration
{
    /// <inheritdoc />
    public string? Force_Service_ID { get; set; }

    /// <summary>
    /// Enable/disable Relationship Type service
    /// Environment variable: ENABLED
    /// </summary>
    public bool Enabled { get; set; } = true;

}
