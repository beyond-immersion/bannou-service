using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Configuration;
using System.ComponentModel.DataAnnotations;

namespace BeyondImmersion.BannouService.RelationshipType;

/// <summary>
/// Configuration class for RelationshipType service.
/// Properties are automatically bound from environment variables.
/// </summary>
[ServiceConfiguration(typeof(RelationshipTypeService))]
[Obsolete]
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
