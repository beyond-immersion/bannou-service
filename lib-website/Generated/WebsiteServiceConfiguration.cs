using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Configuration;
using System.ComponentModel.DataAnnotations;

namespace BeyondImmersion.BannouService.Website;

/// <summary>
/// Configuration class for Website service.
/// Properties are automatically bound from environment variables.
/// </summary>
[ServiceConfiguration(typeof(WebsiteService))]
public class WebsiteServiceConfiguration : IServiceConfiguration
{
    /// <inheritdoc />
    public string? ForceServiceId { get; set; }

    /// <summary>
    /// Enable/disable Website service
    /// Environment variable: ENABLED
    /// </summary>
    public bool Enabled { get; set; } = true;

}
