using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Configuration;
using System.ComponentModel.DataAnnotations;

namespace BeyondImmersion.BannouService.Relationship;

/// <summary>
/// Configuration class for Relationship service.
/// Properties are automatically bound from environment variables.
/// </summary>
[ServiceConfiguration(typeof(RelationshipService))]
[Obsolete]
public class RelationshipServiceConfiguration : IServiceConfiguration
{
    /// <inheritdoc />
    public string? Force_Service_ID { get; set; }

    /// <summary>
    /// Enable/disable Relationship service
    /// Environment variable: ENABLED
    /// </summary>
    public bool Enabled { get; set; } = true;

}
