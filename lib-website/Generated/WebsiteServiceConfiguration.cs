using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Configuration;
using System.ComponentModel.DataAnnotations;

namespace BeyondImmersion.BannouService.Website;

/// <summary>
/// Configuration class for Website service.
/// Properties are automatically bound from environment variables.
/// </summary>
[ServiceConfiguration(typeof(WebsiteService), envPrefix: "BANNOU_")]
public class WebsiteServiceConfiguration : IServiceConfiguration
{
    /// <inheritdoc />
    public string? Force_Service_ID { get; set; }

    /// <summary>
    /// Default configuration property - can be removed if not needed.
    /// Environment variable: WEBSITE_ENABLED or BANNOU_WEBSITE_ENABLED
    /// </summary>
    public bool Enabled { get; set; } = true;

}
