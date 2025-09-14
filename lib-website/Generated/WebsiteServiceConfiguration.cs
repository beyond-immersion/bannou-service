using System.ComponentModel.DataAnnotations;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;

namespace BeyondImmersion.BannouService.Website;

/// <summary>
/// Configuration class for Website service.
/// Properties are automatically bound from environment variables.
/// </summary>
[ServiceConfiguration(envPrefix: "BANNOU_")]
public class WebsiteServiceConfiguration
{
    /// <summary>
    /// Default configuration property - can be removed if not needed.
    /// Environment variable: WEBSITE_ENABLED or BANNOU_WEBSITE_ENABLED
    /// </summary>
    public bool Enabled { get; set; } = true;

}
