using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Configuration;
using System.ComponentModel.DataAnnotations;

namespace BeyondImmersion.BannouService.Character;

/// <summary>
/// Configuration class for Character service.
/// Properties are automatically bound from environment variables.
/// </summary>
[ServiceConfiguration(typeof(CharacterService), envPrefix: "BANNOU_")]
public class CharacterServiceConfiguration : IServiceConfiguration
{
    /// <inheritdoc />
    public string? Force_Service_ID { get; set; }

    /// <summary>
    /// Default page size for character listings
    /// Environment variable: DEFAULTPAGESIZE or BANNOU_DEFAULTPAGESIZE
    /// </summary>
    public int DefaultPageSize { get; set; } = 20;

    /// <summary>
    /// Maximum allowed page size for character listings
    /// Environment variable: MAXPAGESIZE or BANNOU_MAXPAGESIZE
    /// </summary>
    public int MaxPageSize { get; set; } = 100;

    /// <summary>
    /// Number of days to retain deleted character data
    /// Environment variable: CHARACTERRETENTIONDAYS or BANNOU_CHARACTERRETENTIONDAYS
    /// </summary>
    public int CharacterRetentionDays { get; set; } = 90;

}
