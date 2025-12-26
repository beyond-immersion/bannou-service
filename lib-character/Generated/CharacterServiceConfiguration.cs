using System.ComponentModel.DataAnnotations;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Configuration;

namespace BeyondImmersion.BannouService.Character;

/// <summary>
/// Configuration class for Character service.
/// Properties are automatically bound from environment variables.
/// </summary>
[ServiceConfiguration(typeof(CharacterService))]
public class CharacterServiceConfiguration : IServiceConfiguration
{
    /// <inheritdoc />
    public string? Force_Service_ID { get; set; }

    /// <summary>
    /// Maximum page size for list queries
    /// Environment variable: CHARACTER_MAX_PAGE_SIZE
    /// </summary>
    public int MaxPageSize { get; set; } = 100;

    /// <summary>
    /// Default page size when not specified
    /// Environment variable: CHARACTER_DEFAULT_PAGE_SIZE
    /// </summary>
    public int DefaultPageSize { get; set; } = 20;

    /// <summary>
    /// Number of days to retain deleted characters before permanent removal
    /// Environment variable: CHARACTER_RETENTION_DAYS
    /// </summary>
    public int CharacterRetentionDays { get; set; } = 90;

}
