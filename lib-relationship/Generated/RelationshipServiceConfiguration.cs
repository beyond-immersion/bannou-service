using System.ComponentModel.DataAnnotations;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Configuration;

namespace BeyondImmersion.BannouService.Relationship;

/// <summary>
/// Configuration class for Relationship service.
/// Properties are automatically bound from environment variables.
/// </summary>
[ServiceConfiguration(typeof(RelationshipService))]
public class RelationshipServiceConfiguration : IServiceConfiguration
{
    /// <inheritdoc />
    public string? ForceServiceId { get; set; }

    /// <summary>
    /// Enable/disable Relationship service
    /// Environment variable: ENABLED
    /// </summary>
    public bool Enabled { get; set; } = true;

}
